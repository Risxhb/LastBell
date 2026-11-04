using System;
using System.Collections;
using System.IO;
using LastBell.Run;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace LastBell.Menu
{
    // Makes the boss card art (Risx, 4 Nov 2026: "fighter cards" on the boss step). Used with -lastbellPractice:
    //   LastBell.exe -lastbellPractice crucivar -lastbellPortrait F:\out\Crucivar.png
    // When the fight starts it hides the heroes, frames the boss from the side it faces, saves a 900x600 PNG and quits.
    // The PNGs go in Menu/Resources/Menu/BossPortraits (tools/boss-portraits/capture.ps1); TestRushSetup loads them.
    public sealed class BossPortraitCapture : MonoBehaviour
    {
        string path;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void FromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-lastbellPortrait");
            if (at < 0 || at + 1 >= args.Length) return;
            var go = new GameObject("Boss portrait capture"); DontDestroyOnLoad(go);
            go.AddComponent<BossPortraitCapture>().path = args[at + 1];
        }

        IEnumerator Start()
        {
            float give = Time.realtimeSinceStartup + 90;
            RunDirector d = null;
            while (Time.realtimeSinceStartup < give)
            {
                d = RunDirector.Instance;
                if (d && d.Boss && d.State == RunState.Combat) break;
                if (d && d.State == RunState.Ready && Time.realtimeSinceStartup > give - 85) d.RequestReady();   // area-boss practice waits in its shop
                yield return null;
            }
            if (!d || !d.Boss) { Debug.LogError("[portrait] no boss fight started"); Application.Quit(2); yield break; }
            yield return new WaitForSecondsRealtime(0.35f);
            Time.timeScale = 0;

            var boss = d.Boss.transform;
            // the boss faces the party at the start: shoot from the party's side
            Vector3 party = Vector3.zero; int n = 0;
            foreach (var p in d.World.Players) if (p) { party += p.transform.position; n++; }
            Vector3 face = n > 0 ? party / n - boss.position : boss.forward; face.y = 0;
            if (face.sqrMagnitude < 0.01f) face = boss.forward;
            face = Quaternion.Euler(0, 18, 0) * face.normalized;   // three-quarter view
            foreach (var p in d.World.Players) if (p) foreach (var r in p.GetComponentsInChildren<Renderer>()) r.enabled = false;

            // the Escapement Twins stand far apart: bring Tock beside Tick for the shot
            var bodies = new System.Collections.Generic.List<Transform> { boss };
            var tock = boss.parent ? boss.parent.Find("Tock") : null;
            if (tock) { tock.position = boss.position + Vector3.Cross(Vector3.up, face) * 2.9f - face * 0.8f; tock.rotation = boss.rotation; bodies.Add(tock); }

            // frame the body: mesh renderers only, skipping floor decals and arena-sized props
            Bounds b = default; bool any = false;
            foreach (var body in bodies) foreach (var r in body.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                var e = r.bounds.size; if (e.x > 14 || e.z > 14 || e.y < 0.05f) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) b = new Bounds(boss.position + Vector3.up * 1.5f, new Vector3(2, 3, 2));
            Debug.Log($"[portrait] {d.Boss.GetType().Name} bounds {b.center} size {b.size}");

            const int W = 900, H = 600; const float fov = 26, aspect = W / (float)H;
            float tan = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            float h = Mathf.Max(b.size.y + 0.4f, 3.4f), w = Mathf.Max(b.size.x, b.size.z);   // headroom; small bosses keep a human scale
            float dist = Mathf.Max(h * 0.5f * 1.18f / tan, w * 0.5f * 1.15f / (tan * aspect)) + w * 0.5f;
            Vector3 aim = new Vector3(b.center.x, b.min.y + h * 0.5f, b.center.z);
            var main = Camera.main;
            var go = new GameObject("Portrait camera"); var cam = go.AddComponent<Camera>();
            if (main) cam.CopyFrom(main);
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            var mainData = main ? main.GetComponent<UniversalAdditionalCameraData>() : null;
            data.renderPostProcessing = mainData ? mainData.renderPostProcessing : true;
            cam.orthographic = false; cam.fieldOfView = fov; cam.nearClipPlane = 0.1f; cam.farClipPlane = 600;
            cam.transform.position = aim + face * dist + Vector3.up * dist * Mathf.Tan(7 * Mathf.Deg2Rad);
            cam.transform.LookAt(aim);
            // hide arena props standing between the camera and the boss (banners, lamps)
            var ray = new Ray(cam.transform.position, aim - cam.transform.position); float reach = (aim - cam.transform.position).magnitude - w * 0.5f;
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                bool own = false; foreach (var body in bodies) own |= r.transform.IsChildOf(body);
                if (own || !r.enabled || r.bounds.size.magnitude > 25) continue;
                var near = r.bounds; near.Expand(0.6f);
                if (near.IntersectRay(ray, out float at) && at < reach) { r.enabled = false; Debug.Log("[portrait] hid " + r.name); }
            }
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt; cam.aspect = aspect;
            yield return new WaitForEndOfFrame();
            cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            RenderTexture.active = prev; cam.targetTexture = null;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log("[portrait] saved " + path);
            Application.Quit(0);
        }
    }
}
