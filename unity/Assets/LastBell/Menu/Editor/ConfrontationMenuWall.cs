using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LastBell.Menu.EditorTools
{
    // Main menu: move the back parapet (the wall behind the Bellwarden) further back (Risxhb, October 27, 2026).
    // The parapet ran along z = -13.55, right at the boss's feet (boss at z = -15). This pass moves that wall and the two
    // back corner lanterns back by SetBack, extends both side walls by copying their last bays, adds a strip of floor
    // underneath, then rebuilds the parapet caps (ConfrontationMenuRefine.MerlonPass) so the moved and copied blocks get them.
    // SetBack is two parapet bays (2 x 3.39 m), so the copied side bays line up with the existing post rhythm.
    // Not re-runnable on an already moved scene: run it once on the committed scene.
    // Batch: Unity.exe -batchmode -projectPath unity -executeMethod LastBell.Menu.EditorTools.ConfrontationMenuWall.MoveBack
    public static class ConfrontationMenuWall
    {
        public const float Bay = 3.3875f, SetBack = 2 * Bay;
        const float BackZ = -13.55f, SideX = 13.55f;
        const string Platform = "Bellkeeper's Dais/Platform";
        const string Extension = "Menu wall setback floor";

        public static void MoveBack()
        {
            Apply();
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // Apply, then build the Windows player (LASTBELL_WINDOWS_OUTPUT) from the moved scene.
        public static void MoveBackAndBuild()
        {
            Apply();
            ConfrontationMenuBuild.BuildWindows();
        }

        static void Apply()
        {
            var s = EditorSceneManager.OpenScene(ConfrontationMenu.ScenePath);
            var platform = GameObject.Find(Platform)?.transform ?? throw new InvalidOperationException("No " + Platform);
            if (GameObject.Find(Extension)) throw new InvalidOperationException("The wall was already moved back in this scene.");
            var groups = platform.Cast<Transform>().ToArray();
            Vector3 back = Vector3.back * SetBack;
            int moved = 0, copied = 0;

            // Wall groups are the platform children whose blocks use the parapet stone; classify each by where its blocks sit.
            foreach (var g in groups)
            {
                var rs = g.GetComponentsInChildren<Renderer>(true);
                if (rs.Length == 0 || !rs.Any(r => r.sharedMaterials.Any(m => m && m.name == "MenuParapetStone"))) continue;
                var c = rs.Select(r => r.bounds.center).ToArray();
                bool backWall = c.All(p => p.z < BackZ + 1 && p.z > BackZ - 1.2f) && c.Max(p => p.x) - c.Min(p => p.x) > 20;
                bool sideWall = c.All(p => Mathf.Abs(Mathf.Abs(p.x) - SideX) < .6f) && c.Max(p => p.z) - c.Min(p => p.z) > 20;
                bool backLantern = g.name.StartsWith("Lantern") && c.All(p => p.z < BackZ + 1.2f);
                if (backWall || backLantern) { g.position += back; moved++; continue; }
                if (!sideWall) continue;
                // Copy the side blocks of the last two bays (between the -6.78 post and the corner post, both excluded):
                // shifted back two bays they continue the wall to the moved back wall, whose corner posts close it.
                foreach (Transform piece in g.Cast<Transform>().ToArray())
                {
                    var pr = piece.GetComponentsInChildren<Renderer>(true); if (pr.Length == 0) continue;
                    float z = pr[0].bounds.center.z;
                    if (z > BackZ + 2 * Bay - .2f || z < BackZ + .4f) continue;
                    var copy = UnityEngine.Object.Instantiate(piece.gameObject, piece.parent);
                    copy.name = piece.name + " (setback)"; copy.transform.SetPositionAndRotation(piece.position + back, piece.rotation); copy.transform.localScale = piece.localScale;
                    copied++;
                }
            }

            // Lantern heads that sit on the moved corner lanterns.
            var heads = GameObject.Find("Menu lantern heads")?.transform;
            if (heads) foreach (Transform h in heads)
            {
                var r = h.GetComponentInChildren<Renderer>(true); if (!r) continue;
                var p = r.bounds.center;
                if (Mathf.Abs(Mathf.Abs(p.x) - 12.9f) < .3f && Mathf.Abs(p.z + 12.9f) < .3f) { h.position += back; moved++; }
            }

            // Floor strip between the old wall line and the new one: copies of the paving and its stone slab, moved back
            // and dropped a little so the overlap stays under the original floor.
            var ext = new GameObject(Extension).transform;
            foreach (var name in new[] { "Floor", "Node-1" })
            {
                var src = platform.Find(name); if (!src) throw new InvalidOperationException("No floor piece " + name);
                var copy = UnityEngine.Object.Instantiate(src.gameObject, ext).transform;
                copy.name = name + " (setback)"; copy.SetPositionAndRotation(src.position + back + Vector3.down * (name == "Floor" ? .006f : .01f), src.rotation); copy.localScale = src.lossyScale;
            }

            EditorSceneManager.MarkSceneDirty(s); EditorSceneManager.SaveScene(s);
            Debug.Log($"Wall setback: {SetBack:F2} m, {moved} groups moved, {copied} side pieces copied, floor extended");

            // Rebuild the caps for the moved and copied blocks (it reopens and saves the scene).
            ConfrontationMenuRefine.Chained = true;
            try { ConfrontationMenuRefine.MerlonPass(); } finally { ConfrontationMenuRefine.Chained = false; }
        }
    }
}
