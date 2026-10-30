using System.Collections.Generic;
using LastBell.Areas.Area1;
using UnityEngine;

namespace LastBell.Run
{
    // Presentation only (Chzy18 2026-10-29: "the floor falling; beneath it the sky and clouds like the arena background"). On
    // the Skyward Spire, Last Light splits the plaza floor (FloorSurface, a 192-segment disc) into its eight 45-degree slices.
    // A slice trembles through its 3 s warning, then drops away and shrinks into the sky the moment it starts burning. The
    // hole shows the open sky below (LastBell/SkyHole: the arena's below-horizon sky with drifting cloud decks), and each
    // standing neighbour shows a stone lip on its cut edge. Damage, timing and the burn are LastLight's and don't change.
    // Off the Skyward Spire, or if the floor mesh can't be read, it does nothing. Kept out of LastLight.cs; the encounter wraps
    // Light.Run() in During(), which puts the whole floor back when the phase ends.
    public sealed class LastLightFloorFx : MonoBehaviour
    {
        const int N = 8;
        const float FallS = 1.4f, Drop = 4.5f, Shrink = 0.45f, Tilt = 16f, Lip = 0.45f, Tremble = 0.05f, HoleShade = 0.18f;
        static readonly Color LipColor = new Color(0.86f, 0.82f, 0.74f);
        LastLight lastLight; Renderer floor; Material holeMat, holeDepthMat, lipMat;
        readonly Transform[] pivot = new Transform[N];        // each slice hangs from a pivot at its middle, so it can tip and shrink
        readonly Vector3[] rest = new Vector3[N];
        readonly Renderer[] slab = new Renderer[N], hole = new Renderer[N], lip = new Renderer[N];   // lip[s]: the cut at yaw s*45+22.5
        readonly Material[] falling = new Material[N]; readonly Color[] baseColor = new Color[N];
        readonly Hazard[] warn = new Hazard[N]; readonly bool[] fallen = new bool[N]; readonly float[] fellAt = new float[N];
        MaterialPropertyBlock mpb;   // made in Awake: Unity forbids it in a field initializer
        int seen;

        public static IEnumerable<int> During(CombatWorld world, LastLight light, IEnumerable<int> run)
        {
            var go = new GameObject("Last Light floor fx"); go.transform.SetParent(world.Root, false);
            var fx = go.AddComponent<LastLightFloorFx>(); fx.lastLight = light;
            if (!fx.Split()) fx.enabled = false;
            try { foreach (var w in run) yield return w; }
            finally { fx.Restore(); Destroy(go); }
        }

        void Awake() => mpb = new MaterialPropertyBlock();

        static int SectorOf(float yaw) => Mathf.RoundToInt(Mathf.Repeat(yaw, 360f) / 45f) % N;
        static Vector3 Dir(float yaw) => Sim.Dir(yaw);

        bool Split()
        {
            var arena = FindAnyObjectByType<SkywardSpireArena>(); if (!arena) return false;
            Transform ft = null;
            foreach (var t in arena.GetComponentsInChildren<Transform>(true)) if (t.name == "FloorSurface") { ft = t; break; }
            if (!ft || !ft.TryGetComponent(out MeshFilter mf) || !ft.TryGetComponent(out floor) || !floor.enabled) return false;
            var src = mf.sharedMesh;
            if (!src || !src.isReadable || src.subMeshCount != 1) { Debug.LogWarning("Last Light floor fx: FloorSurface mesh not readable; the floor stays whole"); return false; }

            // the floor's triangles go to the slice holding their centre (around Centre, yaw as Sim.Dir: 0 = +z, 90 = +x)
            var v = src.vertices; var nrm = src.normals; var tan = src.tangents; var uv = src.uv; var uv1 = src.uv2; var tris = src.triangles;
            var c = AurelionAbility.Centre;
            var map = new Dictionary<int, int>[N]; var idx = new List<int>[N];
            for (int s = 0; s < N; s++) { map[s] = new Dictionary<int, int>(); idx[s] = new List<int>(); }
            for (int i = 0; i < tris.Length; i += 3)
            {
                var w = ft.TransformPoint((v[tris[i]] + v[tris[i + 1]] + v[tris[i + 2]]) / 3f) - c;
                int s = SectorOf(Mathf.Atan2(w.x, w.z) * Mathf.Rad2Deg);
                for (int j = 0; j < 3; j++) { int o = tris[i + j]; if (!map[s].TryGetValue(o, out int n)) map[s][o] = n = map[s].Count; idx[s].Add(n); }
            }
            float radius = floor.bounds.extents.x, y = ft.position.y;
            holeMat = Resources.Load<Material>("Run/FX_SkyHole"); holeDepthMat = Resources.Load<Material>("Run/FX_SkyHoleDepth");
            lipMat = Prim.Material(LipColor); lipMat.renderQueue = 2480;
            for (int s = 0; s < N; s++)
            {
                var m = new Mesh { name = "LastLightSlice" + s };
                var order = new int[map[s].Count]; foreach (var kv in map[s]) order[kv.Value] = kv.Key;
                m.vertices = Pick(v, order);
                if (nrm.Length == v.Length) m.normals = Pick(nrm, order);
                if (tan.Length == v.Length) m.tangents = Pick(tan, order);
                if (uv.Length == v.Length) m.uv = Pick(uv, order);
                if (uv1.Length == v.Length) m.uv2 = Pick(uv1, order);
                m.SetTriangles(idx[s], 0); m.RecalculateBounds();

                // pivot at the slice's middle (half the radius out), in world space; the slab keeps the floor's exact transform
                rest[s] = c + Dir(s * 45f) * radius * 0.5f + Vector3.up * y;
                pivot[s] = new GameObject("Last Light slice " + s).transform; pivot[s].SetParent(ft.parent, false); pivot[s].position = rest[s];
                var go = new GameObject("Slab"); go.transform.SetParent(pivot[s], false);
                go.transform.SetPositionAndRotation(ft.position, ft.rotation); go.transform.localScale = ft.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterials = floor.sharedMaterials;
                r.shadowCastingMode = floor.shadowCastingMode; r.receiveShadows = floor.receiveShadows;
                slab[s] = r;

                // the sky, then (a second material draws the same wedge again) its depth pushed back so the slab can fall into it
                hole[s] = Part("Sky below " + s, Wedge(c, y, radius, s * 45f), holeMat); hole[s].sharedMaterials = new[] { holeMat, holeDepthMat }; hole[s].enabled = false;
                lip[s] = Part("Cut edge " + s, Edge(c, y, radius, s * 45f + 22.5f), lipMat); lip[s].enabled = false;
            }
            floor.enabled = false;
            return true;
        }

        static T[] Pick<T>(T[] a, int[] order) { var o = new T[order.Length]; for (int i = 0; i < o.Length; i++) o[i] = a[order[i]]; return o; }

        Renderer Part(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false); go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return r;
        }

        // the opening: an exact 45-degree fan (world space) at floor height
        static Mesh Wedge(Vector3 c, float y, float radius, float yaw)
        {
            const int seg = 24;
            var vs = new Vector3[seg + 2]; var ts = new int[seg * 3];
            vs[0] = new Vector3(c.x, y, c.z);
            for (int i = 0; i <= seg; i++) vs[i + 1] = vs[0] + Dir(yaw - 22.5f + 45f * i / seg) * radius;
            for (int i = 0; i < seg; i++) { ts[i * 3] = 0; ts[i * 3 + 1] = i + 1; ts[i * 3 + 2] = i + 2; }
            var m = new Mesh { name = "LastLightHole", vertices = vs, triangles = ts }; m.RecalculateBounds(); return m;
        }

        // a cut edge: the floor's thickness along the ray at yaw, both faces (only ever seen from the open side)
        static Mesh Edge(Vector3 c, float y, float radius, float yaw)
        {
            var o = new Vector3(c.x, y, c.z); var e = o + Dir(yaw) * radius; var dn = Vector3.down * Lip;
            var side = Vector3.Cross(Vector3.up, Dir(yaw));
            var vs = new[] { o, e, e + dn, o + dn, o, e, e + dn, o + dn };
            var ns = new[] { side, side, side, side, -side, -side, -side, -side };
            var ts = new[] { 0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6 };
            var m = new Mesh { name = "LastLightEdge", vertices = vs, normals = ns, triangles = ts }; m.RecalculateBounds(); return m;
        }

        void LateUpdate()
        {
            var secs = lastLight.Sectors;
            for (; seen < secs.Count; seen++) { int s = SectorOf(secs[seen].Yaw); if (!fallen[s]) warn[s] = secs[seen]; }
            for (int s = 0; s < N; s++)
            {
                if (!pivot[s]) continue;
                var h = warn[s];
                if (!fallen[s] && h != null)
                {
                    if (h.Age >= h.LockAt) Fall(s);
                    else
                    {
                        // the warning: a tremble that builds toward the drop
                        float k = Mathf.Clamp01(h.Age / (float)Mathf.Max(1, h.LockAt)); k *= k;
                        pivot[s].position = rest[s] + Vector3.up * ((Mathf.PerlinNoise(Time.time * 28f, s * 3.1f) - 0.5f) * 2f * Tremble * k);
                    }
                }
                if (!fallen[s] || !slab[s].enabled) continue;
                float t = (Time.time - fellAt[s]) / FallS;
                if (t >= 1) { slab[s].enabled = false; continue; }
                float e = t * t;   // gravity
                pivot[s].position = rest[s] + Vector3.down * Drop * e;
                pivot[s].rotation = Quaternion.AngleAxis(Tilt * e, Vector3.Cross(Vector3.up, Dir(s * 45f)));   // the outer edge dips first
                pivot[s].localScale = Vector3.one * Mathf.Lerp(1f, Shrink, e);
                if (falling[s]) falling[s].SetColor("baseColorFactor", Color.Lerp(baseColor[s], holeMat.GetColor("_Low"), Mathf.SmoothStep(0, 1, t)));
                mpb.SetFloat("_Open", Mathf.Clamp01(t * 4f)); hole[s].SetPropertyBlock(mpb);
            }
        }

        void Fall(int s)
        {
            fallen[s] = true; fellAt[s] = Time.time; pivot[s].position = rest[s];
            hole[s].enabled = true;
            // its burn stays outlined, but a thin flat fill lets the sky read through (the hole is the stronger "keep off")
            if (warn[s] != null && warn[s].Tele != null) warn[s].Tele.Shade(HoleShade);
            mpb.SetFloat("_Open", 0); hole[s].SetPropertyBlock(mpb);
            // the slab now draws into the hole (Geometry+480, after the sky and its depth reset) and fades into the sky as it
            // falls away; no more floor glow
            var m = new Material(slab[s].sharedMaterial) { renderQueue = 2480 };
            if (m.HasProperty("baseColorFactor")) { baseColor[s] = m.GetColor("baseColorFactor"); falling[s] = m; }
            if (m.HasProperty("emissiveFactor")) m.SetColor("emissiveFactor", Color.black);
            slab[s].sharedMaterial = m; slab[s].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            for (int b = 0; b < N; b++) lip[b].enabled = fallen[b] != fallen[(b + 1) % N];
        }

        // also on destroy: a fight torn down mid-phase (back to the menu, a test reset) drops the script without disposing it
        void OnDestroy() => Restore();
        void Restore()
        {
            if (floor) floor.enabled = true;
            for (int s = 0; s < N; s++)
            {
                if (pivot[s]) Destroy(pivot[s].gameObject);
                if (falling[s]) Destroy(falling[s]);
                pivot[s] = null; falling[s] = null;
            }
            if (lipMat) Destroy(lipMat);
            lipMat = null;
        }
    }
}
