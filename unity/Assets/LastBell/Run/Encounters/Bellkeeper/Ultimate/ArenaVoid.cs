using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // The Ultimate's arena opening onto its void (proposal; Chzy18 26 Oct: the floor should "fall away and match the background
    // of the arena"). Area 4's flat floor is hidden and the rock spire under it loses its flat top, so what shows beneath the
    // floor is the arena's own backdrop (the void vortex 75 m down, the floating ruins). The spire's sides stay: they show
    // below the rim. The Floor Breaks opens it (its stones wear the floor's material until they fall); P5 keeps it open under
    // the bell's own floor, which falls away when the bell shatters. Close() puts everything back.
    public static class ArenaVoid
    {
        static readonly List<Renderer> hidden = new List<Renderer>();
        static (MeshFilter mf, Mesh mesh) hollowed;
        public static bool IsOpen { get; private set; }
        public static Renderer Floor { get; private set; }   // the arena floor (null where the map has none)

        public static void Open(Transform keep)
        {
            if (IsOpen) return;
            IsOpen = true; hidden.Clear(); Floor = null;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || (keep && r.transform.IsChildOf(keep))) continue;
                var b = r.bounds;
                if (r.name == "RockSpire" && r.TryGetComponent<MeshFilter>(out var smf) && smf.sharedMesh)
                {
                    // keep the spire's sides, take off its flat top (every triangle lying in its top plane)
                    hollowed = (smf, smf.sharedMesh); var src = smf.sharedMesh; var vs = src.vertices; var tr = src.triangles; var kept = new List<int>();
                    for (int i = 0; i < tr.Length; i += 3)
                        if (!(Mathf.Abs(vs[tr[i]].y) < 0.01f && Mathf.Abs(vs[tr[i + 1]].y) < 0.01f && Mathf.Abs(vs[tr[i + 2]].y) < 0.01f)) kept.AddRange(new[] { tr[i], tr[i + 1], tr[i + 2] });
                    var m = Object.Instantiate(src); m.name = src.name + " (open top)"; m.SetTriangles(kept, 0); smf.sharedMesh = m;
                    continue;
                }
                // the floor itself: flat, at floor level, centred, and no wider than the floor (the rim's brass bands are wider)
                // (and a real floor piece: a collider or a floor name, never a passing effect mesh)
                if (!(r.name.Contains("Floor") || r.GetComponent<Collider>())) continue;
                if (b.size.x < 8 || b.extents.x > 14f || b.size.y > 1.2f || Mathf.Abs(b.center.y) > 0.6f || Sim.Flat(b.center).magnitude > 4) continue;
                hidden.Add(r); r.enabled = false; if (r.name == "FloorDisc" || Floor == null) Floor = r;
            }
            Debug.Log("[arena void] open: hid " + string.Join(", ", hidden.ConvertAll(r => r.name)) + (hollowed.mf ? "; opened the rock spire" : ""));
        }

        public static void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            foreach (var r in hidden) if (r) r.enabled = true;
            hidden.Clear(); Floor = null;
            if (hollowed.mf) { var open = hollowed.mf.sharedMesh; hollowed.mf.sharedMesh = hollowed.mesh; if (open && open != hollowed.mesh) Object.Destroy(open); }
            hollowed = default;
        }
    }
}
