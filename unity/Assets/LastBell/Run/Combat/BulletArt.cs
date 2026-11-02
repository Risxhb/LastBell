using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Bullet looks for Sylvara v3 (concept sheets deliveries/SYLVARA-ATTACK-PROPS-2026-11-01, proposal). Presentation only:
    // every mesh fits inside its bullet's hit circle, so what you dodge is what you see. Petals and water drops are built
    // here in code (hundreds on screen); the Storm Seed is Chzy18's Meshy model (Resources/SylvaraProps/StormSeed.glb,
    // gitignored art) with a sphere fallback.
    public static class BulletArt
    {
        // 0.60 m petal ("eye" outline with S-curled tips), flat in XZ, nose along +Z. Its hit radius is 0.30 m, the petal's own
        // reach (Chzy18, 1 Nov: the circle shrinks to fit the slimmer petal; it was 0.35 m for the old round blobs).
        public const float PetalHitRadius = 0.30f, DropHitRadius = 0.35f;
        // Submesh 0 is the petal, submesh 1 the bright core at the hit centre: round, or four-lobed for the cobalt petal so
        // the two Phase 2 colours differ in grayscale too.
        public const float PetalHalfLength = 0.30f;
        static Mesh petal, petalLobed, drop;
        public static Mesh Petal(bool lobedCore) => lobedCore ? (petalLobed ??= BuildPetal(true)) : (petal ??= BuildPetal(false));
        public static Mesh Drop => drop ??= BuildDrop();

        static Mesh BuildPetal(bool lobed)
        {
            var v = new List<Vector3>(); var body = new List<int>(); var core = new List<int>();
            const int L = 14, Wd = 6;    // along, across (per side)
            for (int i = 0; i <= L; i++)
            {
                float u = 0.015f + 0.97f * i / L, z = (u * 2 - 1) * PetalHalfLength;   // stop short of the tips: no zero-area triangles (NaN normals)
                float half = 0.17f * Mathf.Pow(Mathf.Sin(Mathf.PI * u), 0.75f);
                float curl = 0.07f * Mathf.Pow(Mathf.Abs(u * 2 - 1), 3) * Mathf.Sign(u * 2 - 1);   // nose tip curls up, tail tip down
                for (int j = -Wd; j <= Wd; j++)
                {
                    float s = j / (float)Wd, x = s * half;
                    v.Add(new Vector3(x, 0.05f * s * s + curl, z));   // cupped across
                }
            }
            int row = 2 * Wd + 1;
            for (int i = 0; i < L; i++)
                for (int j = 0; j < row - 1; j++)
                {
                    int a = i * row + j, b = a + 1, c = a + row, d = c + 1;
                    body.AddRange(new[] { a, c, b, b, c, d });
                }
            // core: a raised disc at the centre, 0.075 m (lobed: a four-petal flower)
            int centre = v.Count; v.Add(new Vector3(0, 0.07f, 0));
            const int N = 24;
            for (int k = 0; k < N; k++)
            {
                float a = k * Mathf.PI * 2 / N, r = 0.075f * (lobed ? 0.55f + 0.45f * Mathf.Abs(Mathf.Cos(2 * a)) : 1f);
                v.Add(new Vector3(Mathf.Cos(a) * r, 0.055f, Mathf.Sin(a) * r));
            }
            for (int k = 0; k < N; k++) core.AddRange(new[] { centre, centre + 1 + (k + 1) % N, centre + 1 + k });
            var m = new Mesh { name = lobed ? "PetalLobed" : "Petal" };
            m.SetVertices(v); m.subMeshCount = 2; m.SetTriangles(body, 0); m.SetTriangles(core, 1);
            m.RecalculateNormals(); SafeNormals(m); m.RecalculateBounds(); return m;
        }
        // a NaN normal turns a lit pixel NaN, and bloom smears that over the whole screen: replace any bad normal with up
        public static void SafeNormals(Mesh m)
        {
            var n = m.normals;
            for (int i = 0; i < n.Length; i++) if (float.IsNaN(n[i].x) || float.IsNaN(n[i].y) || float.IsNaN(n[i].z) || n[i].sqrMagnitude < 0.5f) n[i] = Vector3.up;
            m.normals = n;
        }

        // a stretched water drop (0.6 m long) with a small coral core, for the Waterfall Jet stream
        static Mesh BuildDrop()
        {
            var v = new List<Vector3>(); var water = new List<int>(); var core = new List<int>();
            Ellipsoid(v, water, Vector3.zero, new Vector3(0.2f, 0.16f, 0.25f), 10, 7, 0.35f);   // tail 0.25 x 1.35 = 0.34 m, inside DropHitRadius
            Ellipsoid(v, core, Vector3.zero, new Vector3(0.07f, 0.07f, 0.12f), 8, 5, 0);
            var m = new Mesh { name = "WaterDrop" };
            m.SetVertices(v); m.subMeshCount = 2; m.SetTriangles(water, 0); m.SetTriangles(core, 1);
            m.RecalculateNormals(); SafeNormals(m); m.RecalculateBounds(); return m;
        }
        static void Ellipsoid(List<Vector3> v, List<int> t, Vector3 c, Vector3 r, int segs, int rings, float tail)
        {
            int start = v.Count;
            for (int i = 0; i <= rings; i++)
            {
                float th = (Mathf.PI * i / rings - Mathf.PI / 2) * 0.97f;   // stop short of the poles: no zero-area triangles (NaN normals)
                for (int j = 0; j <= segs; j++)
                {
                    float ph = 2 * Mathf.PI * j / segs, z = Mathf.Sin(th) * r.z;
                    float narrow = z < 0 ? 1 - tail * (-z / r.z) : 1;   // the tail end tapers
                    v.Add(c + new Vector3(Mathf.Cos(th) * Mathf.Cos(ph) * r.x * narrow, Mathf.Cos(th) * Mathf.Sin(ph) * r.y * narrow, z * (z < 0 ? 1 + tail : 1)));
                }
            }
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < segs; j++)
                {
                    int a = start + i * (segs + 1) + j, b = a + 1, cc = a + segs + 1, d = cc + 1;
                    t.AddRange(new[] { a, b, cc, b, d, cc });
                }
        }

        // Storm Seed: the Meshy model (1.1 m across, inside the 0.6 m hit radius), or a sphere when the art isn't installed
        static GameObject seedPrefab; static bool seedLooked;
        public static bool HasSeedModel { get { if (!seedLooked) { seedPrefab = Resources.Load<GameObject>("SylvaraProps/StormSeed"); seedLooked = true; } return seedPrefab; } }
        public static Transform MakeSeed(Transform parent, Material fallback)
        {
            Transform t;
            if (HasSeedModel) { t = Object.Instantiate(seedPrefab, parent, false).transform; foreach (var c in t.GetComponentsInChildren<Collider>()) Object.Destroy(c); }
            else { t = Prim.Make(PrimitiveKind.Sphere, parent, Vector3.zero, Vector3.one * 1.1f, Color.white); t.GetComponent<MeshRenderer>().sharedMaterial = fallback; }
            t.name = "Storm Seed";
            // the fuse glow: a coral core inside the husk that swells and brightens until the burst (BulletView drives it)
            var core = Prim.Make(PrimitiveKind.Sphere, t, Vector3.zero, Vector3.one * 0.6f, new Color(1f, 0.42f, 0.26f), 2f);
            core.name = "Fuse";
            return t;
        }
    }
}
