using System.IO;
using UnityEditor;
using UnityEngine;

namespace LastBell.Characters.EditorTools
{
    // Last Bell > Bellkeeper > Capture Look Shots: in Play mode, renders the spawned Bellkeeper from a few fixed angles
    // into Temp/BellkeeperShots/*.png (for reviewing the look without relying on the Game view's size).
    static class BellkeeperShots
    {
        [MenuItem("Last Bell/Bellkeeper/Capture Look Shots")]
        static void Capture()
        {
            var boss = Object.FindAnyObjectByType<BellkeeperTestController>();
            if (boss == null) { Debug.LogWarning("Bellkeeper shots: enter Play mode in the key-art arena first"); return; }
            var dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp", "BellkeeperShots");
            Directory.CreateDirectory(dir);
            var go = new GameObject("Bellkeeper shot camera");
            var cam = go.AddComponent<Camera>();
            var main = Camera.main;
            if (main) { cam.CopyFrom(main); }
            cam.fieldOfView = 30;
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
                go.transform.position = s.pos; go.transform.LookAt(s.look);
                var rt = new RenderTexture(s.w, s.h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                cam.targetTexture = rt; cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(s.w, s.h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, s.w, s.h), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(dir, s.name + ".png"), tex.EncodeToPNG());
                RenderTexture.active = null; cam.targetTexture = null;
                Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
            }
            Object.DestroyImmediate(go);
            Debug.Log("Bellkeeper shots: " + dir);
        }
    }
}
