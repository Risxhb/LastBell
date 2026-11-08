using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // The Ultimate's arena opening onto its void (proposal; Chzy18 26 Oct: the floor should "fall away and match the background
    // of the arena"). Area 4's flat floor is hidden and the rock spire under it loses its flat top, so what shows beneath the
    // floor is the arena's own backdrop (the void vortex 75 m down, the floating ruins). The spire's sides stay: they show
    // below the rim. The Floor Breaks opens it (its stones wear the floor's material until they fall); P5 keeps it open under
    // the bell's own floor, which falls away when the bell shatters. Close() puts everything back.
    // Prepare() (combat start) finds the floor and builds the open-topped spire without showing either, and the open-topped
    // mesh is kept between opens (performance agent, 2 Nov: each open rebuilt it, part of a 21 ms / 4.4 MB P4 -> P5 frame).
    public static class ArenaVoid
    {
        static readonly List<Renderer> hidden = new List<Renderer>();
        static (MeshFilter mf, Mesh mesh) hollowed;
        static Mesh openSrc, openTop;   // the spire mesh the cached open top was made from, and that open top
        public static bool IsOpen { get; private set; }
        public static Renderer Floor { get; private set; }   // the arena floor (null where the map has none)

        // the arena's floor pieces and its rock spire (not under keep)
        static void Scan(Transform keep, List<Renderer> floors, out MeshFilter spire)
        {
            spire = null;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || (keep && r.transform.IsChildOf(keep))) continue;
                var b = r.bounds;
                if (r.name == "RockSpire" && r.TryGetComponent<MeshFilter>(out var smf) && smf.sharedMesh) { spire = smf; continue; }
                // the floor itself: flat, at floor level, centred, and no wider than the floor (the rim's brass bands are wider)
                // (and a real floor piece: a collider or a floor name, never a passing effect mesh)
                if (!(r.name.Contains("Floor") || r.GetComponent<Collider>())) continue;
                if (b.size.x < 8 || b.extents.x > 14f || b.size.y > 1.2f || Mathf.Abs(b.center.y) > 0.6f || Sim.Flat(b.center).magnitude > 4) continue;
                floors.Add(r);
            }
        }
        static Renderer MainFloor(List<Renderer> floors) { Renderer f = null; foreach (var r in floors) if (r.name == "FloorDisc" || f == null) f = r; return f; }

        // keep the spire's sides, take off its flat top (every triangle lying in its top plane)
        static Mesh OpenTop(Mesh src)
        {
            if (openTop && openSrc == src) return openTop;
            var vs = src.vertices; var tr = src.triangles; var kept = new List<int>(tr.Length);
            for (int i = 0; i < tr.Length; i += 3)
                if (!(Mathf.Abs(vs[tr[i]].y) < 0.01f && Mathf.Abs(vs[tr[i + 1]].y) < 0.01f && Mathf.Abs(vs[tr[i + 2]].y) < 0.01f)) { kept.Add(tr[i]); kept.Add(tr[i + 1]); kept.Add(tr[i + 2]); }
            if (openTop) Object.Destroy(openTop);
            var m = Object.Instantiate(src); m.name = src.name + " (open top)"; m.SetTriangles(kept, 0);
            openSrc = src; openTop = m; return m;
        }

        // combat start: the floor (for the Floor Breaks' stones to copy) and the open top, nothing hidden yet. The scan is kept for
        // Open (performance agent, 2 Nov: scanning every renderer, names and all, was most of a 23 ms / 4.5 MB P4 -> P5 frame)
        static readonly List<Renderer> preparedFloors = new List<Renderer>();
        static MeshFilter preparedSpire; static Transform preparedKeep;
        public static Renderer Prepare(Transform keep)
        {
            if (IsOpen) return Floor;
            preparedFloors.Clear(); Scan(keep, preparedFloors, out preparedSpire); preparedKeep = keep;
            if (preparedSpire) OpenTop(preparedSpire.sharedMesh);
            return MainFloor(preparedFloors);
        }
        static bool PreparedFor(Transform keep)
        {
            if (!preparedKeep || preparedKeep != keep || preparedFloors.Count == 0) return false;
            foreach (var r in preparedFloors) if (!r || !r.enabled) return false;
            return true;
        }

        public static void Open(Transform keep)
        {
            if (IsOpen) return;
            IsOpen = true; hidden.Clear(); Floor = null;
            MeshFilter spire;
            if (PreparedFor(keep)) { hidden.AddRange(preparedFloors); spire = preparedSpire; }
            else Scan(keep, hidden, out spire);
            foreach (var r in hidden) r.enabled = false;
            Floor = MainFloor(hidden);
            if (spire) { hollowed = (spire, spire.sharedMesh); spire.sharedMesh = OpenTop(spire.sharedMesh); }
            Debug.Log("[arena void] open: hid " + string.Join(", ", hidden.ConvertAll(r => r.name)) + (hollowed.mf ? "; opened the rock spire" : ""));
        }

        public static void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            foreach (var r in hidden) if (r) r.enabled = true;
            hidden.Clear(); Floor = null;
            if (hollowed.mf) hollowed.mf.sharedMesh = hollowed.mesh;   // the open top stays cached for the next open
            hollowed = default;
        }
    }
}
