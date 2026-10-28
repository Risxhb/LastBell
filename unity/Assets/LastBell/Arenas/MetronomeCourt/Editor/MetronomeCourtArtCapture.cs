using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LastBell.Arenas.MetronomeCourt.EditorTools
{
    // Art-review renders of the production Metronome Court (editor only, nothing saved). Opens the source scene, builds the
    // arena exactly as Awake does, evaluates one presentation frame (Haste preview, no fight) and renders fixed cameras.
    //   Unity.exe -batchmode -projectPath unity -executeMethod LastBell.Arenas.MetronomeCourt.EditorTools.MetronomeCourtArtCapture.Capture
    //   with LASTBELL_ARENA_EVIDENCE=<folder>. Not a gameplay, warning or performance capture.
    public static class MetronomeCourtArtCapture
    {
        public static void Capture()
        {
            string output = Environment.GetEnvironmentVariable("LASTBELL_ARENA_EVIDENCE");
            if (string.IsNullOrEmpty(output)) throw new InvalidOperationException("Set LASTBELL_ARENA_EVIDENCE.");
            Directory.CreateDirectory(output);
            EditorSceneManager.OpenScene("Assets/LastBell/Arenas/MetronomeCourt/MetronomeCourt_Arena.unity", OpenSceneMode.Single);
            var arena = UnityEngine.Object.FindAnyObjectByType<MetronomeCourtArena>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            typeof(MetronomeCourtArena).GetMethod("Build", flags).Invoke(arena, null);
            var update = typeof(MetronomeCourtArena).GetMethod("Update", flags);
            for (int i = 0; i < 3; i++) update.Invoke(arena, null);   // tempo colours, lantern chase, probe render, sky
            Inventory(arena.transform);

            var cam = Camera.main;
            if (arena.SkyActive) cam.clearFlags = CameraClearFlags.Skybox;
            void Shot(string name, Vector3 pos, Vector3 target, int width = 1920, int height = 1080)
            {
                cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(target - pos));
                var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32); cam.targetTexture = rt;
                bool async = ShaderUtil.allowAsyncCompilation;   // avoid the grey async-compilation fallback in the first render
                try { ShaderUtil.allowAsyncCompilation = false; cam.Render(); cam.Render(); }
                finally { ShaderUtil.allowAsyncCompilation = async; }
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, width, height), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(output, name + ".png"), tex.EncodeToPNG());
                RenderTexture.active = prev; cam.targetTexture = null; UnityEngine.Object.DestroyImmediate(tex); UnityEngine.Object.DestroyImmediate(rt);
            }
            var follow = new Vector3(0, 13, 9.7f);   // RunDirector.LateUpdate offset; looks at the follow point + 1 m
            Shot("01_overview", new Vector3(32, 32, 43), new Vector3(0, -1, -3));
            Shot("02_gameplay_projection_centre", follow, Vector3.up);
            Shot("03_metronome", new Vector3(0, 9, 4), new Vector3(0, 5, -19.5f));
            Shot("04_south_approach", new Vector3(13, 13, 36), new Vector3(0, 0, 7));
            Shot("05_underside", new Vector3(34, 7, 29), new Vector3(0, -6, 0));
            Shot("06_ultrawide", new Vector3(32, 32, 43), new Vector3(0, -1, -3), 2560, 1080);
            Shot("07_west_flank", new Vector3(-36, 10, 14), new Vector3(0, -3, -6));
            foreach (var edge in new[] { new Vector3(0, 0, 12.5f), new Vector3(12.5f, 0, 0), new Vector3(0, 0, -12.5f), new Vector3(-12.5f, 0, 0) })
                Shot($"edge_{edge.x:0}_{edge.z:0}", edge + follow, edge + Vector3.up);
            // pass 5/6: close-ups of the pieces Risxhb flagged (a west pier head, a near bollard, a bridge rail post cap)
            Shot("09_closeup_pier_head", new Vector3(-13f, 3.5f, 3f), new Vector3(-19.5f, -4.5f, -5.5f));
            Shot("10_closeup_bollard", new Vector3(3.6f, 1.9f, 10.9f), new Vector3(5.5f, 0.55f, 13.3f));
            Shot("11_closeup_bridge_post", new Vector3(0.9f, 1.7f, 15.6f), new Vector3(2.45f, 0.6f, 17.65f));
            // pass 4: the same gameplay projections in the Normal (cool) tempo, to check that the stone keeps its colour
            MetronomeCourtArena.ReviewHaste = false;
            typeof(MetronomeCourtArena).GetField("tempoBlend", flags).SetValue(arena, 0f);
            for (int i = 0; i < 3; i++) update.Invoke(arena, null);
            Shot("08_gameplay_normal", follow, Vector3.up);
            Shot("edge_-13_0_normal", new Vector3(-12.5f, 0, 0) + follow, new Vector3(-12.5f, 1, 0));
            MetronomeCourtArena.ReviewHaste = null;
            Debug.Log("METRONOME ARENA CAPTURE: 16 editor views (Haste preview plus two Normal, no fight; not gameplay/performance) -> " + output);
        }

        // Rewrites the Test Rush map tile (Resources/Menu/Maps/metronome-court.png) from the current arena code, with the
        // same view as MapPreviewCapture and the Tempo Tyrant autotest (camera (0,26,20) on the origin, FOV 40, 960x540).
        // No player is spawned and nothing is saved except the PNG.
        //   -executeMethod LastBell.Arenas.MetronomeCourt.EditorTools.MetronomeCourtArtCapture.CapturePreview
        public static void CapturePreview()
        {
            EditorSceneManager.OpenScene("Assets/LastBell/Arenas/MetronomeCourt/MetronomeCourt_Arena.unity", OpenSceneMode.Single);
            var arena = UnityEngine.Object.FindAnyObjectByType<MetronomeCourtArena>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            typeof(MetronomeCourtArena).GetMethod("Build", flags).Invoke(arena, null);
            var update = typeof(MetronomeCourtArena).GetMethod("Update", flags);
            for (int i = 0; i < 3; i++) update.Invoke(arena, null);
            var cam = Camera.main; if (arena.SkyActive) cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 40; var pos = new Vector3(0, 26, 20);
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-pos));
            var rt = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32); cam.targetTexture = rt;
            bool async = ShaderUtil.allowAsyncCompilation;
            try { ShaderUtil.allowAsyncCompilation = false; cam.Render(); cam.Render(); } finally { ShaderUtil.allowAsyncCompilation = async; }
            RenderTexture.active = rt; var tex = new Texture2D(960, 540, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); tex.Apply();
            const string png = "Assets/LastBell/Menu/Resources/Menu/Maps/metronome-court.png";
            File.WriteAllBytes(png, tex.EncodeToPNG()); RenderTexture.active = null; cam.targetTexture = null;
            AssetDatabase.ImportAsset(png, ImportAssetOptions.ForceUpdate);
            Debug.Log("METRONOME ARENA PREVIEW: wrote " + png);
        }

        // Checks the menu's baked Metronome Court stage after a save: renderer count, and that every generated texture
        // it references was saved with its pixels (a texture saved without them would load blank).
        //   -executeMethod LastBell.Arenas.MetronomeCourt.EditorTools.MetronomeCourtArtCapture.VerifyShowcaseStage
        public static void VerifyShowcaseStage()
        {
            EditorSceneManager.OpenScene("Assets/LastBell/Menu/ShowcaseMainMenu.unity", OpenSceneMode.Single);
            var stage = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.name == "Showcase environment - Metronome Court" && t.gameObject.scene.IsValid());
            if (!stage) { Debug.LogError("METRONOME SHOWCASE CHECK: stage not found"); return; }
            var renderers = stage.GetComponentsInChildren<Renderer>(true);
            var textures = renderers.SelectMany(r => r.sharedMaterials).Where(m => m).Distinct()
                .SelectMany(m => m.GetTexturePropertyNameIDs().Select(m.GetTexture)).OfType<Texture2D>().Distinct().ToArray();
            int missingMat = renderers.Count(r => r.sharedMaterials.Any(m => !m));
            int missingMesh = stage.GetComponentsInChildren<MeshFilter>(true).Count(f => !f.sharedMesh);
            foreach (var t in textures.Where(t => t.name.StartsWith("MetronomeCourt")))
            {
                string data = t.isReadable ? $"pixels {(t.GetPixelData<byte>(0).Length > 0 ? "present" : "EMPTY")}" : "not readable";
                Debug.Log($"METRONOME SHOWCASE TEXTURE: {t.name} {t.width}x{t.height} {t.format} mips {t.mipmapCount} {data} asset={(AssetDatabase.Contains(t) ? AssetDatabase.GetAssetPath(t) : "in scene")}");
            }
            Debug.Log($"METRONOME SHOWCASE CHECK: renderers={renderers.Length}, textures={textures.Length}, renderers missing a material={missingMat}, mesh filters missing a mesh={missingMesh}");
        }

        static void Inventory(Transform root)
        {
            var renderers = root.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled).ToArray();
            long tris = 0;
            foreach (var r in renderers)
            {
                var mesh = r.GetComponent<MeshFilter>()?.sharedMesh; if (!mesh) continue;
                int start = r.isPartOfStaticBatch ? r.subMeshStartIndex : 0;
                int count = r.isPartOfStaticBatch ? r.sharedMaterials.Length : mesh.subMeshCount;
                for (int k = start; k < start + count && k < mesh.subMeshCount; k++) tris += mesh.GetIndexCount(k) / 3;
            }
            var mats = renderers.SelectMany(r => r.sharedMaterials).Where(m => m).Distinct().ToArray();
            long texBytes = mats.SelectMany(m => m.GetTexturePropertyNameIDs().Select(m.GetTexture)).Where(t => t).Distinct()
                .Sum(t => UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t));
            int transparent = renderers.Count(r => r.sharedMaterials.Any(m => m && m.renderQueue >= 2500));
            Debug.Log($"METRONOME ARENA INVENTORY: renderers={renderers.Length}, triangles={tris}, materials={mats.Length}, transparent_renderers={transparent}, texture_bytes={texBytes} (editor estimate, after the arena's own static batching)");
            foreach (Transform g in root.GetComponentsInChildren<Transform>().Where(t => t.parent && t.parent.name == "Generated"))
            {
                var rs = g.GetComponentsInChildren<MeshRenderer>(); if (rs.Length == 0) continue;
                Debug.Log($"METRONOME ARENA GROUP: {g.name} | renderers={rs.Length}");
            }
        }
    }
}
