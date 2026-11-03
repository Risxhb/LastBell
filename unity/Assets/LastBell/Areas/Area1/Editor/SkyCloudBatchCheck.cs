using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LastBell.Areas.Area1.EditorTools
{
    // Proof that SkyCloudBatch changes nothing on screen: renders the Area 1 scene twice per view at the same moment,
    // once with every puff its own renderer posed the old way (SkywardSpireArena.AnimateClouds on transforms and
    // material copies) and once as the merged mesh, then compares the pixels. Writes before/after PNGs plus
    // cloud-batch-check.txt to -cloudCheckOut (default Builds/CloudBatchCheck).
    // The old renderers were frustum-culled by their unrotated quads (flat boxes facing +Z), not by the camera-facing
    // billboard the shader draws, so puffs just past a screen edge popped out; the merged mesh draws them. The pass
    // gate therefore compares against a control: the old path with each puff's culling box grown to its billboard
    // (same sort centre). The raw old path is still captured and its difference reported.
    // Unity.exe -batchmode -projectPath unity -executeMethod LastBell.Areas.Area1.EditorTools.SkyCloudBatchCheck.Run -cloudCheckOut <dir>
    public static class SkyCloudBatchCheck
    {
        const string ScenePath = "Assets/LastBell/Areas/Area1/SkywardSpire_Arena.unity";
        static readonly (float clock, float drift)[] Moments = { (0, 0), (37.5f, 37.5f), (240, 240) };   // rest, mid-fight, 4 min of drift

        public static void Run()
        {
            int code = 0;
            try { code = Check() ? 0 : 1; }
            catch (System.Exception e) { Debug.LogException(e); code = 1; }
            EditorApplication.Exit(code);
        }

        static bool Check()
        {
            ShaderUtil.allowAsyncCompilation = false;
            string outDir = Path.GetFullPath(Arg("-cloudCheckOut") ?? "Builds/CloudBatchCheck"); Directory.CreateDirectory(outDir);
            var report = new System.Text.StringBuilder("Skyward Spire cloud batch check: separate puffs (culled by billboard reach) vs one merged mesh, same moment\n");
            bool pass = true;
            var views = new[] { (SkywardSpireArena.View.Gameplay, 1920, 1080), (SkywardSpireArena.View.TopDown, 1700, 1484), (SkywardSpireArena.View.AbyssCloseup, 1920, 1080) };
            int puffs = 0, renderersBefore = 0;
            // One scene load for the whole check: in batch mode URP's post-processing stack is not rebuilt after a
            // second OpenScene and every later frame renders black. Puffs are put back to rest between captures instead.
            EditorSceneManager.OpenScene(ScenePath);
            var arena = Object.FindAnyObjectByType<SkywardSpireArena>();
            foreach (var probe in Object.FindObjectsByType<ReflectionProbe>()) probe.RenderProbe();
            var rest = new List<(Renderer r, Material m, Vector3 pos, Vector3 scale)>();
            foreach (var r in arena.GetComponentsInChildren<Renderer>(true))
                if (r.sharedMaterial && r.sharedMaterial.HasProperty("_SoftDistance")) rest.Add((r, r.sharedMaterial, r.transform.position, r.transform.localScale));
            foreach (var (clock, drift) in Moments)
                foreach (var (view, w, h) in views)
                {
                    string tag = $"{view}-t{clock:0}".ToLowerInvariant(), a = Path.Combine(outDir, tag + "-before.png"), c = Path.Combine(outDir, tag + "-control.png"), b = Path.Combine(outDir, tag + "-after.png");
                    // before: the old path (one renderer per puff, transforms and material copies animated on the CPU)
                    Setup(arena, rest, view, batch: false);
                    arena.AnimateClouds(clock, drift);
                    renderersBefore = CountCloudRenderers(arena);
                    SkywardSpireBuilder.Capture(a, w, h); SkywardSpireBuilder.Capture(a, w, h);
                    // control: the same, culled by the billboard's reach instead of the flat quad
                    Setup(arena, rest, view, batch: false, billboardBounds: true);
                    arena.AnimateClouds(clock, drift);
                    SkywardSpireBuilder.Capture(c, w, h); SkywardSpireBuilder.Capture(c, w, h);
                    // after: the same puffs back at rest, merged, posed by the shader from the same numbers and sorted for this camera
                    Setup(arena, rest, view, batch: true);
                    var batch = arena.CloudBatch; if (batch == null) { report.AppendLine("FAIL no batch built"); pass = false; continue; }
                    puffs = SkyCloudBatch.LastPuffCount;
                    batch.StartTime = clock - drift; arena.AnimateClouds(clock, drift); batch.Sort(Camera.main.transform.position, clock);
                    SkywardSpireBuilder.Capture(b, w, h); SkywardSpireBuilder.Capture(b, w, h);
                    var (rawMax, rawMean, rawOver) = Diff(a, b, Path.Combine(outDir, tag + "-diff-raw.png"));
                    var (max, mean, over) = Diff(c, b, Path.Combine(outDir, tag + "-diff.png"));
                    bool ok = over < 0.00005 && mean < 0.01;   // channel steps (0-255): a few scattered edge pixels may round differently (MSAA), a visible change may not
                    pass &= ok;
                    report.AppendLine($"{(ok ? "PASS" : "FAIL")} {tag}: max channel diff {max}, mean {mean:0.0000}, pixels over 8: {over:P3} (vs raw old path: max {rawMax}, mean {rawMean:0.0000}, over 8: {rawOver:P3})");
                }
            arena.CloudBatch?.Dispose();
            SkywardSpireArena.UseLongShadows(false);
            EditorSceneManager.OpenScene(ScenePath);   // drop the posed copy unsaved
            report.Insert(0, $"cloud renderers before: {renderersBefore} · after: 1 merged renderer holding {puffs} puffs\n");
            report.AppendLine(pass ? "RESULT PASS" : "RESULT FAIL");
            File.WriteAllText(Path.Combine(outDir, "cloud-batch-check.txt"), report.ToString());
            Debug.Log(report.ToString());
            return pass;
        }

        // Every puff back at its saved position, scale and material, then the clouds set up one way or the other.
        static void Setup(SkywardSpireArena arena, List<(Renderer r, Material m, Vector3 pos, Vector3 scale)> rest, SkywardSpireArena.View view, bool batch, bool billboardBounds = false)
        {
            arena.CloudBatch?.Dispose();
            foreach (var (r, m, pos, scale) in rest) { r.enabled = true; r.sharedMaterial = m; r.transform.position = pos; r.transform.localScale = scale; r.ResetLocalBounds(); }
            arena.checkMode = true; arena.batchClouds = batch; arena.CollectNow();
            if (billboardBounds)   // a box that holds the quad however it turns (+3% breathing); centre unchanged, so the sort is too
                foreach (var (r, _, _, _) in rest)
                {
                    var s = r.transform.lossyScale; float e = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y)) * 1.03f;
                    r.localBounds = new Bounds(Vector3.zero, new Vector3(e / Mathf.Abs(s.x), e / Mathf.Abs(s.y), e / Mathf.Max(1e-4f, Mathf.Abs(s.z))));
                }
            arena.SetView(view); SkywardSpireArena.UseLongShadows(view == SkywardSpireArena.View.TopDown);
        }

        static int CountCloudRenderers(SkywardSpireArena arena)
        {
            int n = 0;
            foreach (var r in arena.GetComponentsInChildren<Renderer>(true)) if (r.enabled && r.sharedMaterial && r.sharedMaterial.HasProperty("_SoftDistance")) n++;
            return n;
        }

        static (int max, double mean, double over) Diff(string a, string b, string diffPng)
        {
            var ta = new Texture2D(2, 2); ta.LoadImage(File.ReadAllBytes(a)); var tb = new Texture2D(2, 2); tb.LoadImage(File.ReadAllBytes(b));
            var pa = ta.GetPixels32(); var pb = tb.GetPixels32(); var pd = new Color32[pa.Length];
            int max = 0; long sum = 0, over = 0;
            for (int i = 0; i < pa.Length; i++)
            {
                int d = Mathf.Max(Mathf.Abs(pa[i].r - pb[i].r), Mathf.Max(Mathf.Abs(pa[i].g - pb[i].g), Mathf.Abs(pa[i].b - pb[i].b)));
                max = Mathf.Max(max, d); sum += d; if (d > 8) over++;
                byte v = (byte)Mathf.Min(255, d * 16); pd[i] = new Color32(v, v, v, 255);
            }
            var td = new Texture2D(ta.width, ta.height, TextureFormat.RGB24, false); td.SetPixels32(pd); td.Apply();
            File.WriteAllBytes(diffPng, td.EncodeToPNG());
            Object.DestroyImmediate(ta); Object.DestroyImmediate(tb); Object.DestroyImmediate(td);
            return (max, (double)sum / pa.Length, (double)over / pa.Length);
        }

        static string Arg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
