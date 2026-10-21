using System.IO;
using UnityEditor;
using UnityEngine;

namespace LastBell.Characters.EditorTools
{
    // Review captures for the Bellkeeper (Play mode, key-art arena), written to Temp/BellkeeperShots/:
    //   Capture Look Shots  - a few fixed angles of the boss, now
    //   Record Attack Loop  - 10 s at a locked 30 fps of game time from the gameplay camera (seq_000.png ...), covering a
    //                         full Phase 1 loop of attacks
    static class BellkeeperShots
    {
        static string Dir()
        {
            var dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp", "BellkeeperShots");
            Directory.CreateDirectory(dir); return dir;
        }

        internal static void Render(Camera cam, int w, int h, string path)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = null; cam.targetTexture = null;
            Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
        }

        internal static Camera ShotCamera()
        {
            var cam = new GameObject("Bellkeeper shot camera").AddComponent<Camera>();
            if (Camera.main) cam.CopyFrom(Camera.main);
            cam.enabled = false; return cam;
        }

        [MenuItem("Last Bell/Bellkeeper/Capture Look Shots")]
        static void Capture()
        {
            var boss = Object.FindAnyObjectByType<BellkeeperTestController>();
            if (boss == null) { Debug.LogWarning("Bellkeeper shots: enter Play mode in the key-art arena first"); return; }
            var dir = Dir(); var cam = ShotCamera();
            Vector3 c = boss.transform.position + Vector3.up * 2.5f;
            var shots = new (string name, Vector3 pos, Vector3 look, int w, int h)[]
            {
                ("gameplay", new Vector3(0, 29, 22.5f), new Vector3(0, 0, 1), 1600, 1000),   // the arena's gameplay camera
                ("front", c + new Vector3(0, 1.5f, 13), c, 900, 1100),
                ("threequarter", c + new Vector3(9, 3, 9), c, 900, 1100),
                ("chest", c + new Vector3(0, 1.2f, 5.5f) + Vector3.up * 0.8f, c + Vector3.up * 1.0f, 900, 900),
            };
            foreach (var s in shots)
            {
                cam.fieldOfView = s.name == "gameplay" ? 44 : 30;
                cam.transform.position = s.pos; cam.transform.LookAt(s.look);
                Render(cam, s.w, s.h, Path.Combine(dir, s.name + ".png"));
            }
            Object.DestroyImmediate(cam.gameObject);
            Debug.Log("Bellkeeper shots: " + dir);
        }

        [MenuItem("Last Bell/Bellkeeper/Record Attack Loop")]
        static void Record()
        {
            if (Object.FindAnyObjectByType<BellkeeperTestController>() == null) { Debug.LogWarning("Bellkeeper shots: enter Play mode in the key-art arena first"); return; }
            foreach (var f in Directory.GetFiles(Dir(), "seq_*.png")) File.Delete(f);
            new GameObject("Bellkeeper recorder").AddComponent<BellkeeperRecorder>().dir = Dir();
        }
    }

    // Play-mode helper for Record Attack Loop: locks game time to 30 fps (Time.captureFramerate) and renders every frame.
    sealed class BellkeeperRecorder : MonoBehaviour
    {
        public string dir;
        const int Fps = 30, Frames = 300;
        int n; Camera cam;
        void Start()
        {
            cam = BellkeeperShots.ShotCamera(); cam.fieldOfView = 40;   // the arena's gameplay camera, slightly tighter
            cam.transform.position = new Vector3(0, 27, 21); cam.transform.LookAt(new Vector3(0, 0, 1));
            Time.captureFramerate = Fps;
            Object.FindAnyObjectByType<BellkeeperTestController>()?.RestartLoop();
        }
        void LateUpdate()
        {
            BellkeeperShots.Render(cam, 960, 600, Path.Combine(dir, $"seq_{n++:000}.png"));
            if (n >= Frames) { Time.captureFramerate = 0; Destroy(cam.gameObject); Destroy(gameObject); Debug.Log($"Bellkeeper recorder: {n} frames in {dir}"); }
        }
    }
}
