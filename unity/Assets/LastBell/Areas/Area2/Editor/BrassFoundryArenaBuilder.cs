using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LastBell.Areas.Area2.EditorTools
{
    // Last Bell > Area 2 â€“ Brass Foundry: builds (once) and opens the Brass Foundry arena scene, matching the top-down
    // reference prototype/arena-3d/areas/area-2/ref-brass-foundry-topdown.webp (brief: BRASS-FOUNDRY-UNITY-PROMPT.md).
    // Everything is generated from code: meshes (combined per group and material, saved under Generated/Meshes),
    // textures (PNG under Generated/Textures), URP materials, sky, fog, lights, post and FX. Image top = -Z (Furnace Gate),
    // bottom = +Z (Entry Bridge, toward the gameplay camera), right = +X. The playable floor is the disc r <= 14 at y = 0.
    public static class BrassFoundryArenaBuilder
    {
        const string Dir = "Assets/LastBell/Areas/Area2", Gen = Dir + "/Generated", ScenePath = Dir + "/BrassFoundry_Arena.unity";
        const float R = 14f, PI = Mathf.PI, TAU = Mathf.PI * 2;

        [MenuItem("Last Bell/Area 2 â€“ Brass Foundry", priority = 20)]
        static void Open()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) Build();
            EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Last Bell/Rebuild Area 2 â€“ Brass Foundry", priority = 21)]
        static void Rebuild()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(); EditorSceneManager.OpenScene(ScenePath);
        }

        // ================================================================== build ==================================================================
        static Transform root;
        static readonly Dictionary<string, Dictionary<Material, Geo>> batches = new Dictionary<string, Dictionary<Material, Geo>>();
        static readonly List<Material> moltenMats = new List<Material>(), glowMats = new List<Material>();
        static readonly List<Transform> swingers = new List<Transform>();
        static System.Random rng;
        static float Rnd() => (float)rng.NextDouble();
        static float Rnd(float a, float b) => a + (b - a) * Rnd();

        // materials
        static Material mRimTop, mPlank, mAbyss, mRimIron, mFloor, mSlate, mDeck, mMasonry, mMasonryDark, mCity, mIron, mBrass, mDarkBrass, mChain, mRoof;
        static Material mMolten, mRiver, mGlow, mGlowDim, mGlowCurtain, mGlowSheet, mEmber, mSmoke;
        static Mesh lanternIron, lanternGlow;

        public static void Build()
        {
            foreach (var f in new[] { "Generated", "Generated/Meshes", "Generated/Textures", "Generated/Materials" })
            {
                var parts = (Dir + "/" + f).Split('/');
                for (int i = 1; i < parts.Length; i++) { string p = string.Join("/", parts.Take(i + 1)), parent = string.Join("/", parts.Take(i)); if (!AssetDatabase.IsValidFolder(p)) AssetDatabase.CreateFolder(parent, parts[i]); }
            }
            batches.Clear(); moltenMats.Clear(); glowMats.Clear(); swingers.Clear(); rng = new System.Random(7);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            root = new GameObject("LB_ENV_BrassFoundry_Arena").transform;

            MakeMaterials();
            BuildLanternMeshes();
            BuildFloor();
            BuildInlays();
            BuildRim();
            BuildChannel();
            BuildOuterWall();
            BuildTowers();
            BuildGantries();
            BuildEntryBridge();
            BuildFurnaceGate();
            BuildSurroundings();
            FlushBatches();
            var cam = BuildLightingAndCamera();
            BuildFX();
            BuildGameplay();

            var driver = new GameObject("Brass Foundry Arena").AddComponent<BrassFoundryArena>();
            driver.transform.SetParent(root, false);
            var so = new SerializedObject(driver);
            so.FindProperty("viewCamera").objectReferenceValue = cam;
            SetArray(so.FindProperty("moltenMaterials"), moltenMats.Cast<Object>().ToList());
            SetArray(so.FindProperty("glowMaterials"), glowMats.Cast<Object>().ToList());
            SetArray(so.FindProperty("swingers"), swingers.Cast<Object>().ToList());
            so.ApplyModifiedPropertiesWithoutUndo();
            driver.SetView(BrassFoundryArena.View.Gameplay);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
        }

        static void SetArray(SerializedProperty p, List<Object> items)
        {
            p.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }

        // ================================================================== geometry kit ==================================================================
        sealed class Geo { public readonly List<Vector3> V = new List<Vector3>(), N = new List<Vector3>(); public readonly List<Vector2> U = new List<Vector2>(); public readonly List<int> T = new List<int>(); }

        static Geo G(string group, Material m)
        {
            if (!batches.TryGetValue(group, out var d)) batches[group] = d = new Dictionary<Material, Geo>();
            if (!d.TryGetValue(m, out var g)) d[m] = g = new Geo();
            return g;
        }

        static Matrix4x4 TR(Vector3 p, float yaw = 0) => Matrix4x4.TRS(p, Quaternion.Euler(0, yaw, 0), Vector3.one);
        static Matrix4x4 TRQ(Vector3 p, Quaternion q) => Matrix4x4.TRS(p, q, Vector3.one);
        static Vector3 Polar(float a, float r, float y = 0) => new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
        // yaw (degrees) that turns local +X to point along angle a (radians, measured from +X toward +Z)
        static float YawOut(float a) => -a * Mathf.Rad2Deg;

        static void Tri(Geo g, Matrix4x4 m, Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            Vector3 wa = m.MultiplyPoint3x4(a), wb = m.MultiplyPoint3x4(b), wc = m.MultiplyPoint3x4(c);
            Vector3 wna = m.MultiplyVector(na).normalized, wnb = m.MultiplyVector(nb).normalized, wnc = m.MultiplyVector(nc).normalized;
            int i = g.V.Count;
            g.V.Add(wa); g.V.Add(wb); g.V.Add(wc); g.N.Add(wna); g.N.Add(wnb); g.N.Add(wnc); g.U.Add(ua); g.U.Add(ub); g.U.Add(uc);
            if (Vector3.Dot(Vector3.Cross(wb - wa, wc - wa), wna + wnb + wnc) >= 0) { g.T.Add(i); g.T.Add(i + 1); g.T.Add(i + 2); }
            else { g.T.Add(i); g.T.Add(i + 2); g.T.Add(i + 1); }
        }

        static void Quad(Geo g, Matrix4x4 m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            Tri(g, m, a, b, c, n, n, n, ua, ub, uc); Tri(g, m, a, c, d, n, n, n, ua, uc, ud);
        }

        // Chamfer width applied by Box() while building parts that should catch edge highlights (0 = sharp boxes).
        static float bevel;

        // Box with all 12 edges chamfered by b: inset faces, 45Â° edge strips, corner triangles. UVs projected per face.
        static void BevelBox(Geo g, Matrix4x4 m, Vector3 size, Vector3 c, float b)
        {
            Vector3 h = size * 0.5f, i = h - Vector3.one * b;
            Vector3 V(int ax, float va, int bx, float vb, int kx, float vk) { var v = Vector3.zero; v[ax] = va; v[bx] = vb; v[kx] = vk; return c + v; }
            Vector2 UV(Vector3 p, Vector3 n) { var a = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z)); return a.y >= a.x && a.y >= a.z ? new Vector2(p.x, p.z) : a.x >= a.z ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y); }
            void Q(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 n) => Quad(g, m, p0, p1, p2, p3, n, UV(p0, n), UV(p1, n), UV(p2, n), UV(p3, n));
            for (int a = 0; a < 3; a++) { int u = (a + 1) % 3, w = (a + 2) % 3; foreach (float s in new[] { -1f, 1f }) { var n = Vector3.zero; n[a] = s; Q(V(a, s * h[a], u, -i[u], w, -i[w]), V(a, s * h[a], u, i[u], w, -i[w]), V(a, s * h[a], u, i[u], w, i[w]), V(a, s * h[a], u, -i[u], w, i[w]), n); } }
            for (int a = 0; a < 3; a++) { int bb = (a + 1) % 3, k = (a + 2) % 3; foreach (float sa in new[] { -1f, 1f }) foreach (float sb in new[] { -1f, 1f })
            { var n = Vector3.zero; n[a] = sa; n[bb] = sb; n.Normalize(); Q(V(a, sa * h[a], bb, sb * i[bb], k, -i[k]), V(a, sa * h[a], bb, sb * i[bb], k, i[k]), V(a, sa * i[a], bb, sb * h[bb], k, i[k]), V(a, sa * i[a], bb, sb * h[bb], k, -i[k]), n); } }
            foreach (float sx in new[] { -1f, 1f }) foreach (float sy in new[] { -1f, 1f }) foreach (float sz in new[] { -1f, 1f })
            {
                var n = new Vector3(sx, sy, sz).normalized; Vector3 p0 = c + new Vector3(sx * h.x, sy * i.y, sz * i.z), p1 = c + new Vector3(sx * i.x, sy * h.y, sz * i.z), p2 = c + new Vector3(sx * i.x, sy * i.y, sz * h.z);
                Tri(g, m, p0, p1, p2, n, n, n, UV(p0, n), UV(p1, n), UV(p2, n));
            }
        }

        // Axis-aligned box (local), UVs in metres so tiled textures keep their scale.
        static void Box(Geo g, Matrix4x4 m, Vector3 size, Vector3 c = default, bool bottom = false)
        {
            if (bevel > 0 && Mathf.Min(size.x, Mathf.Min(size.y, size.z)) > bevel * 3) { BevelBox(g, m, size, c, bevel); return; }
            Vector3 h = size * 0.5f;
            Vector3 P(float x, float y, float z) => c + new Vector3(x * h.x, y * h.y, z * h.z);
            Vector2 W(float u, float v) => new Vector2(u, v);
            float sx = size.x, sy = size.y, sz = size.z, ox = c.x, oy = c.y, oz = c.z;
            Quad(g, m, P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), Vector3.right, W(oz - sz / 2, oy - sy / 2), W(oz - sz / 2, oy + sy / 2), W(oz + sz / 2, oy + sy / 2), W(oz + sz / 2, oy - sy / 2));
            Quad(g, m, P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), P(-1, -1, -1), Vector3.left, W(oz + sz / 2, oy - sy / 2), W(oz + sz / 2, oy + sy / 2), W(oz - sz / 2, oy + sy / 2), W(oz - sz / 2, oy - sy / 2));
            Quad(g, m, P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), Vector3.up, W(ox - sx / 2, oz - sz / 2), W(ox - sx / 2, oz + sz / 2), W(ox + sx / 2, oz + sz / 2), W(ox + sx / 2, oz - sz / 2));
            if (bottom) Quad(g, m, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), Vector3.down, W(ox - sx / 2, oz - sz / 2), W(ox + sx / 2, oz - sz / 2), W(ox + sx / 2, oz + sz / 2), W(ox - sx / 2, oz + sz / 2));
            Quad(g, m, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), Vector3.forward, W(ox - sx / 2, oy - sy / 2), W(ox + sx / 2, oy - sy / 2), W(ox + sx / 2, oy + sy / 2), W(ox - sx / 2, oy + sy / 2));
            Quad(g, m, P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), Vector3.back, W(ox + sx / 2, oy - sy / 2), W(ox - sx / 2, oy - sy / 2), W(ox - sx / 2, oy + sy / 2), W(ox + sx / 2, oy + sy / 2));
        }

        // Surface of revolution around local Y. Profile runs bottom â†’ top as (radius, y); per-band normals, smooth around.
        static void Lathe(Geo g, Matrix4x4 m, Vector2[] prof, int seg, float a0 = 0, float a1 = TAU)
        {
            float vAcc = 0;
            for (int j = 0; j < prof.Length - 1; j++)
            {
                Vector2 p0 = prof[j], p1 = prof[j + 1], t = p1 - p0; float len = t.magnitude; if (len < 1e-5f) continue;
                Vector2 n2 = new Vector2(t.y, -t.x) / len; float ur = Mathf.Max(0.5f, Mathf.Max(p0.x, p1.x));
                for (int i = 0; i < seg; i++)
                {
                    float aa = a0 + (a1 - a0) * i / seg, ab = a0 + (a1 - a0) * (i + 1) / seg;
                    Vector3 da = new Vector3(Mathf.Cos(aa), 0, Mathf.Sin(aa)), db = new Vector3(Mathf.Cos(ab), 0, Mathf.Sin(ab));
                    Vector3 a = da * p0.x + Vector3.up * p0.y, b = db * p0.x + Vector3.up * p0.y, c = db * p1.x + Vector3.up * p1.y, d = da * p1.x + Vector3.up * p1.y;
                    Vector3 na = da * n2.x + Vector3.up * n2.y, nb = db * n2.x + Vector3.up * n2.y;
                    Vector2 ua = new Vector2(aa * ur, vAcc), ub = new Vector2(ab * ur, vAcc), uc = new Vector2(ab * ur, vAcc + len), ud = new Vector2(aa * ur, vAcc + len);
                    if (p0.x > 1e-4f) Tri(g, m, a, b, c, na, nb, nb, ua, ub, uc);
                    if (p1.x > 1e-4f || p0.x > 1e-4f) Tri(g, m, a, c, d, na, nb, na, ua, uc, ud);
                }
                vAcc += len;
            }
        }

        static void Cyl(Geo g, Matrix4x4 m, float r, float y0, float y1, int seg = 16, bool top = true) =>
            Lathe(g, m, top ? new[] { new Vector2(r, y0), new Vector2(r, y1), new Vector2(0, y1) } : new[] { new Vector2(r, y0), new Vector2(r, y1) }, seg);

        // Annular sector between radii r0..r1 and angles a0..a1 (radians from +X toward +Z), from y0 to y1.
        static void Sector(Geo g, Matrix4x4 m, float r0, float r1, float a0, float a1, float y0, float y1, int seg = 0, bool top = true, bool inner = true, bool outer = true, bool ends = true, bool bottom = false)
        {
            if (seg <= 0) seg = Mathf.Max(1, Mathf.CeilToInt((a1 - a0) * r1 / 0.6f));
            for (int i = 0; i < seg; i++)
            {
                float aa = a0 + (a1 - a0) * i / seg, ab = a0 + (a1 - a0) * (i + 1) / seg;
                Vector3 da = new Vector3(Mathf.Cos(aa), 0, Mathf.Sin(aa)), db = new Vector3(Mathf.Cos(ab), 0, Mathf.Sin(ab)), dm = (da + db).normalized;
                Vector3 Y0 = Vector3.up * y0, Y1 = Vector3.up * y1;
                if (top) Quad(g, m, da * r0 + Y1, db * r0 + Y1, db * r1 + Y1, da * r1 + Y1, Vector3.up, new Vector2(aa * r0, r0), new Vector2(ab * r0, r0), new Vector2(ab * r1, r1), new Vector2(aa * r1, r1));
                if (bottom) Quad(g, m, da * r0 + Y0, db * r0 + Y0, db * r1 + Y0, da * r1 + Y0, Vector3.down, new Vector2(aa * r0, r0), new Vector2(ab * r0, r0), new Vector2(ab * r1, r1), new Vector2(aa * r1, r1));
                if (outer) Quad(g, m, da * r1 + Y0, db * r1 + Y0, db * r1 + Y1, da * r1 + Y1, dm, new Vector2(aa * r1, y0), new Vector2(ab * r1, y0), new Vector2(ab * r1, y1), new Vector2(aa * r1, y1));
                if (inner) Quad(g, m, da * r0 + Y0, db * r0 + Y0, db * r0 + Y1, da * r0 + Y1, -dm, new Vector2(aa * r0, y0), new Vector2(ab * r0, y0), new Vector2(ab * r0, y1), new Vector2(aa * r0, y1));
            }
            if (!ends) return;
            foreach (var (a, s) in new[] { (a0, -1f), (a1, 1f) })
            {
                Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)), n = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a)) * s;
                Quad(g, m, d * r0 + Vector3.up * y0, d * r1 + Vector3.up * y0, d * r1 + Vector3.up * y1, d * r0 + Vector3.up * y1, n, new Vector2(r0, y0), new Vector2(r1, y0), new Vector2(r1, y1), new Vector2(r0, y1));
            }
        }

        // Prism from a star-shaped polygon (x, z) about its centroid, from y0 to y1.
        static void Poly(Geo g, Matrix4x4 m, Vector2[] pts, float y0, float y1, bool sides = true)
        {
            Vector2 c = Vector2.zero; foreach (var p in pts) c += p; c /= pts.Length;
            Vector3 C = new Vector3(c.x, y1, c.y);
            for (int i = 0; i < pts.Length; i++)
            {
                Vector2 p = pts[i], q = pts[(i + 1) % pts.Length];
                Tri(g, m, C, new Vector3(p.x, y1, p.y), new Vector3(q.x, y1, q.y), Vector3.up, Vector3.up, Vector3.up, c, p, q);
                if (!sides) continue;
                Vector2 e = q - p; Vector3 n = new Vector3(e.y, 0, -e.x).normalized; Vector2 mid = (p + q) / 2 - c; if (n.x * mid.x + n.z * mid.y < 0) n = -n;
                float l = e.magnitude;
                Quad(g, m, new Vector3(p.x, y0, p.y), new Vector3(q.x, y0, q.y), new Vector3(q.x, y1, q.y), new Vector3(p.x, y1, p.y), n, new Vector2(0, y0), new Vector2(l, y0), new Vector2(l, y1), new Vector2(0, y1));
            }
        }

        // Oriented thin bar from A to B (world), cross-section w Ã— h, its "up" kept as close to world up as possible.
        static void Bar(Geo g, Vector3 A, Vector3 B, float w, float h)
        {
            Vector3 d = B - A; float len = d.magnitude; if (len < 1e-4f) return;
            var q = Quaternion.LookRotation(d / len, Mathf.Abs(Vector3.Dot(d / len, Vector3.up)) > 0.98f ? Vector3.forward : Vector3.up);
            Box(g, TRQ((A + B) / 2, q), new Vector3(w, h, len));
        }

        static void Link(Geo g, Matrix4x4 m)
        {
            const int U = 10, V = 6; const float Rm = 0.34f, rt = 0.11f, sy = 1.45f;
            Vector3 P(int i, int j, out Vector3 n)
            {
                float u = TAU * i / U, v = TAU * j / V; var dir = new Vector3(Mathf.Cos(u), Mathf.Sin(u), 0);
                n = (dir * Mathf.Cos(v) + Vector3.forward * Mathf.Sin(v)).normalized;
                var c = new Vector3(Mathf.Cos(u) * Rm, Mathf.Sin(u) * Rm * sy, 0);
                return c + n * rt;
            }
            for (int i = 0; i < U; i++) for (int j = 0; j < V; j++)
            {
                var a = P(i, j, out var na); var b = P(i + 1, j, out var nb); var c = P(i + 1, j + 1, out var nc); var d = P(i, j + 1, out var nd);
                Tri(g, m, a, b, c, na, nb, nc, Vector2.zero, Vector2.right, Vector2.one); Tri(g, m, a, c, d, na, nc, nd, Vector2.zero, Vector2.one, Vector2.up);
            }
        }

        static void Chain(Vector3 A, Vector3 B, float sag = 0)
        {
            var g = G("Chains", mChain);
            float len = Vector3.Distance(A, B); int n = Mathf.Max(2, Mathf.CeilToInt(len / 0.78f));
            for (int i = 0; i < n; i++)
            {
                float t = (i + 0.5f) / n, t2 = Mathf.Min(1, t + 0.5f / n), t1 = Mathf.Max(0, t - 0.5f / n);
                Vector3 S(float s) => Vector3.Lerp(A, B, s) + Vector3.down * Mathf.Sin(s * PI) * sag;
                Vector3 dir = (S(t2) - S(t1)).normalized;
                var q = Quaternion.FromToRotation(Vector3.up, dir) * Quaternion.Euler(0, i % 2 == 0 ? 0 : 90, 0);
                Link(g, TRQ(S(t), q));
            }
        }

        static void FlushBatches()
        {
            foreach (var group in batches)
            {
                var parent = Child(root, group.Key);
                foreach (var kv in group.Value)
                {
                    var geo = kv.Value; if (geo.V.Count == 0) continue;
                    var mesh = new Mesh { name = group.Key.Replace("/", "_") + "_" + kv.Key.name, indexFormat = IndexFormat.UInt32 };
                    mesh.SetVertices(geo.V); mesh.SetNormals(geo.N); mesh.SetUVs(0, geo.U); mesh.SetTriangles(geo.T, 0);
                    mesh.RecalculateBounds(); mesh.RecalculateTangents();
                    mesh = SaveAsset(mesh, "Meshes/" + mesh.name + ".asset");
                    var go = new GameObject(kv.Key.name); go.transform.SetParent(parent, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = kv.Key;
                    bool glow = kv.Key.shader.name.Contains("Unlit");
                    bool noCast = glow || group.Key == "Chains" || group.Key == "Towers" || group.Key == "Surroundings";
                    mr.shadowCastingMode = noCast ? ShadowCastingMode.Off : ShadowCastingMode.On; mr.receiveShadows = !glow;
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                }
            }
        }

        static Transform Child(Transform parent, string name)
        {
            foreach (var part in name.Split('/'))
            {
                var t = parent.Find(part);
                if (!t) { t = new GameObject(part).transform; t.SetParent(parent, false); }
                parent = t;
            }
            return parent;
        }

        static T SaveAsset<T>(T asset, string file) where T : Object
        {
            string path = Gen + "/" + file;
            AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        // ================================================================== noise and textures ==================================================================
        static float Hash(int x, int y) { unchecked { int h = x * 374761393 + y * 668265263; h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 0x7fffffff) / 2147483647f; } }
        static int Wrap(int v, int p) => p > 0 ? ((v % p) + p) % p : v;
        static float VNoise(float x, float y, int period = 0)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y); float xf = x - xi, yf = y - yi, u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
            int x0 = Wrap(xi, period), x1 = Wrap(xi + 1, period), y0 = Wrap(yi, period), y1 = Wrap(yi + 1, period);
            float a = Hash(x0, y0), b = Hash(x1, y0), c = Hash(x0, y1), d = Hash(x1, y1);
            return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
        }
        static float Fbm(float x, float y, int p = 0) => VNoise(x, y, p) * 0.55f + VNoise(x * 2, y * 2, p * 2) * 0.3f + VNoise(x * 4, y * 4, p * 4) * 0.15f;
        static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }
        static Color32 C32(float r, float g, float b, float a = 255) => new Color32((byte)Mathf.Clamp(r, 0, 255), (byte)Mathf.Clamp(g, 0, 255), (byte)Mathf.Clamp(b, 0, 255), (byte)Mathf.Clamp(a, 0, 255));

        static Texture2D SaveTex(string name, int w, int h, Color32[] px, bool heightToNormal = false, float heightScale = 0.1f, bool clamp = false, bool alpha = false)
        {
            string path = Gen + "/Textures/" + name + ".png";
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false); tex.SetPixels32(px); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat; imp.anisoLevel = 8; imp.maxTextureSize = Mathf.Max(w, h);
            imp.alphaIsTransparency = alpha;
            if (heightToNormal) { imp.textureType = TextureImporterType.NormalMap; imp.convertToNormalmap = true; imp.heightmapScale = heightScale; imp.normalmapFilter = TextureImporterNormalFilter.Standard; }
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Open combat floor: cool charcoal slate slabs cut by concentric seams and 16 radial seams, worn centre, soot rim,
        // hairline cracks and fine engraved ticks around the medallion. No glow anywhere.
        static void PaintFloor(int N, out Color32[] col, out Color32[] hgt)
        {
            col = new Color32[N * N]; hgt = new Color32[N * N];
            float[] rings = { 2.7f, 7f, 11.2f, 13.3f };
            const float LX = 0.34f, LZ = -0.94f;
            for (int py = 0; py < N; py++)
            {
                float z = (py + 0.5f) / N * 2 * R - R;
                for (int px = 0; px < N; px++)
                {
                    float x = (px + 0.5f) / N * 2 * R - R, r = Mathf.Sqrt(x * x + z * z); int o = py * N + px;
                    if (r > R + 0.05f) { col[o] = C32(40, 42, 46); hgt[o] = C32(160, 160, 160); continue; }
                    float a = Mathf.Atan2(z, x); if (a < 0) a += TAU;
                    float edge = 99, nx = 0, nz = 0;
                    foreach (var rr in rings) { float d = Mathf.Abs(r - rr); if (d < edge) { edge = d; float s = r > rr ? -1 : 1; nx = s * x / r; nz = s * z / r; } }
                    int ring = r < 2.7f ? 0 : r < 7f ? 1 : r < 11.2f ? 2 : r < 13.3f ? 3 : 4, slab = 0;
                    if (ring >= 1)
                    {
                        int cnt = ring == 4 ? 24 : ring == 3 ? 16 : 8; float off = ring == 2 ? PI / 8 : 0, sg = TAU / cnt, aa = (a + off) % sg, d = Mathf.Min(aa, sg - aa) * r;
                        slab = Mathf.FloorToInt((a + off) / sg);
                        if (d < edge) { edge = d; float s = aa < sg - aa ? -1 : 1; nx = -Mathf.Sin(a) * s; nz = Mathf.Cos(a) * s; }
                    }
                    float n1 = Fbm(x * 0.18f, z * 0.18f), n2 = Fbm(x * 0.9f + 7, z * 0.9f), fine = VNoise(x * 9, z * 9), sid = Hash(ring * 97 + slab, 13);
                    float v = (sid - 0.5f) * 0.12f + (n1 - 0.5f) * 0.3f + (n2 - 0.5f) * 0.12f + (fine - 0.5f) * 0.07f;
                    float cr = ring == 4 ? 47 : 61, cg = ring == 4 ? 49 : 64, cb = ring == 4 ? 54 : 70;
                    cr *= 1 + v; cg *= 1 + v; cb *= 1 + v * 0.9f;
                    float wear = Smooth(0.52f, 0.72f, Fbm(x * 0.3f + 3, z * 0.3f)) * Smooth(12, 3, r); cr *= 1 + 0.16f * wear; cg *= 1 + 0.15f * wear; cb *= 1 + 0.12f * wear;
                    float soot = Smooth(10.5f, 13.8f, r) * Smooth(0.45f, 0.7f, Fbm(x * 0.25f + 11, z * 0.25f)); cr *= 1 - 0.22f * soot; cg *= 1 - 0.22f * soot; cb *= 1 - 0.2f * soot;
                    float h = 0.7f + (n2 - 0.5f) * 0.12f;
                    if (VNoise(x * 2.7f, z * 2.7f + 4) < 0.06f) { cr *= 0.94f; cg *= 0.94f; cb *= 0.95f; h -= 0.03f; }
                    if (edge < 0.07f) { float t = 1 - Smooth(0.012f, 0.07f, edge), f = 1 + (nx * LX + nz * LZ) * 0.22f * t; cr *= f; cg *= f; cb *= f; }
                    h = Mathf.Lerp(0.3f, h, Smooth(0.012f, 0.07f, edge));
                    if (edge < 0.014f) { cr = 25; cg = 26; cb = 29; h = 0.12f; }
                    col[o] = C32(cr, cg, cb); byte hb = (byte)Mathf.Clamp(h * 255, 0, 255); hgt[o] = new Color32(hb, hb, hb, 255);
                }
            }
            var cc = col; var hh = hgt;
            void Dot(float x, float z, float rad, float k, byte hv)
            {
                float S = N / (2 * R); int cx = Mathf.RoundToInt((x + R) * S), cz = Mathf.RoundToInt((z + R) * S), ri = Mathf.CeilToInt(rad);
                for (int dy = -ri; dy <= ri; dy++) for (int dx = -ri; dx <= ri; dx++)
                {
                    if (dx * dx + dy * dy > rad * rad) continue; int X = cx + dx, Y = cz + dy; if (X < 0 || Y < 0 || X >= N || Y >= N) continue;
                    int o = Y * N + X; var c = cc[o]; cc[o] = C32(c.r * k, c.g * k, c.b * k); hh[o] = new Color32(hv, hv, hv, 255);
                }
            }
            void Line(Vector2 A, Vector2 B, float rad, float k, byte hv) { int steps = Mathf.CeilToInt(Vector2.Distance(A, B) * N / (2 * R) * 1.5f) + 1; for (int i = 0; i <= steps; i++) { var p = Vector2.Lerp(A, B, (float)i / steps); Dot(p.x, p.y, rad, k, hv); } }
            var rr2 = new System.Random(19);
            for (int i = 0; i < 28; i++)
            {
                float rad = 3 + (float)rr2.NextDouble() * 10, a = (float)rr2.NextDouble() * TAU, d = (float)rr2.NextDouble() * TAU; var p = new Vector2(Mathf.Cos(a) * rad, Mathf.Sin(a) * rad);
                int n = 4 + rr2.Next(6);
                for (int s = 0; s < n; s++) { d += ((float)rr2.NextDouble() - 0.5f) * 1.3f; var q = p + new Vector2(Mathf.Cos(d), Mathf.Sin(d)) * 0.22f; Line(p, q, 0.8f, 0.82f, 110); p = q; }
            }
            for (int i = 0; i < 64; i++) // engraved ticks just outside the medallion
            {
                float a = i / 64f * TAU, l = i % 8 == 0 ? 0.35f : 0.16f;
                Line(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 2.95f, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (2.95f + l), i % 8 == 0 ? 1.4f : 0.9f, 1.32f, 90);
            }
        }

        // Tileable masonry (2 m square): 1.0 Ã— 0.5 m charcoal blocks, running bond, chipped edges and soot.
        static void PaintMasonry(int N, out Color32[] col, out Color32[] hgt)
        {
            col = new Color32[N * N]; hgt = new Color32[N * N]; float S = N / 2f;
            for (int py = 0; py < N; py++) for (int px = 0; px < N; px++)
            {
                float X = px / S, Y = py / S; int row = Mathf.FloorToInt(Y / 0.5f); float off = (row % 2) * 0.5f, xx = (X + off) % 2f; int bx = Mathf.FloorToInt(xx / 1f);
                float fx = xx - bx, fy = Y - row * 0.5f, edge = Mathf.Min(Mathf.Min(fx, 1 - fx), Mathf.Min(fy, 0.5f - fy));
                float id = Hash(bx + row * 7, 3), n = Fbm(X * 4, Y * 4, 8), fine = VNoise(X * 40, Y * 40, 80), chip = VNoise(X * 9 + id * 13, Y * 9, 18);
                float v = (id - 0.5f) * 0.22f + (n - 0.5f) * 0.3f + (fine - 0.5f) * 0.08f, warm = Hash(bx, row + 11) * 0.08f;
                float cr = 66 * (1 + v + warm), cg = 61 * (1 + v), cb = 58 * (1 + v - warm * 0.5f), h = 0.75f + (n - 0.5f) * 0.2f;
                float ed = edge - (chip < 0.25f ? 0.03f : 0);
                if (ed < 0.06f) { float t = 1 - Smooth(0.012f, 0.06f, ed); float f = fy > 0.25f && fy < 0.5f && 0.5f - fy < 0.06f ? 1.25f : 0.85f; cr *= Mathf.Lerp(1, f, t); cg *= Mathf.Lerp(1, f, t); cb *= Mathf.Lerp(1, f, t); h = Mathf.Lerp(h, 0.35f, t); }
                if (ed < 0.014f) { cr = 28; cg = 25; cb = 24; h = 0.1f; }
                if (fine > 0.85f) { cr *= 1.1f; cg *= 1.1f; cb *= 1.08f; }
                int o = py * N + px; col[o] = C32(cr, cg, cb); byte hb = (byte)Mathf.Clamp(h * 255, 0, 255); hgt[o] = new Color32(hb, hb, hb, 255);
            }
        }

        // Tileable riveted iron plate (2 m square, 1 m plates), rust blooms and scratches.
        static void PaintIron(int N, out Color32[] col, out Color32[] hgt)
        {
            col = new Color32[N * N]; hgt = new Color32[N * N]; float S = N / 2f;
            for (int py = 0; py < N; py++) for (int px = 0; px < N; px++)
            {
                float X = px / S, Y = py / S, fx = X % 1, fy = Y % 1, edge = Mathf.Min(Mathf.Min(fx, 1 - fx), Mathf.Min(fy, 1 - fy));
                int pid = Mathf.FloorToInt(X) + Mathf.FloorToInt(Y) * 2; float n = Fbm(X * 3, Y * 3, 6), rust = Fbm(X * 2 + 5, Y * 2, 4), fine = VNoise(X * 60, Y * 60, 120);
                float v = (Hash(pid, 5) - 0.5f) * 0.15f + (n - 0.5f) * 0.25f + (fine - 0.5f) * 0.1f;
                float cr = 56 * (1 + v), cg = 53 * (1 + v), cb = 52 * (1 + v), h = 0.7f;
                float rr = Smooth(0.6f, 0.78f, rust); cr = Mathf.Lerp(cr, 92, rr * 0.7f); cg = Mathf.Lerp(cg, 58, rr * 0.7f); cb = Mathf.Lerp(cb, 40, rr * 0.7f);
                if (edge < 0.03f) { float t = 1 - Smooth(0.006f, 0.03f, edge); cr *= 1 - 0.3f * t; cg *= 1 - 0.3f * t; cb *= 1 - 0.3f * t; h = Mathf.Lerp(h, 0.3f, t); }
                // rivets every 1/8 m, 4 cm in from each plate edge
                float rx = (fx * 8) % 1 - 0.5f, ry = (fy * 8) % 1 - 0.5f, inX = Mathf.Abs(fx - 0.04f) < 0.02f || Mathf.Abs(fx - 0.96f) < 0.02f ? 1 : 0, inY = Mathf.Abs(fy - 0.04f) < 0.02f || Mathf.Abs(fy - 0.96f) < 0.02f ? 1 : 0;
                float dX = Mathf.Sqrt(Mathf.Pow((fx - (Mathf.Abs(fx - 0.04f) < 0.03f ? 0.04f : 0.96f)) * 8, 2) + ry * ry), dY = Mathf.Sqrt(rx * rx + Mathf.Pow((fy - (Mathf.Abs(fy - 0.04f) < 0.03f ? 0.04f : 0.96f)) * 8, 2));
                float rd = Mathf.Min(inX > 0 ? dX : 9, inY > 0 ? dY : 9);
                if (rd < 0.16f) { float k = 1.3f - rd * 2; cr *= k; cg *= k; cb *= k; h = 0.95f - rd; }
                int o = py * N + px; col[o] = C32(cr, cg, cb); byte hb = (byte)Mathf.Clamp(h * 255, 0, 255); hgt[o] = new Color32(hb, hb, hb, 255);
            }
        }

        // Tileable slate deck (3 m square): 1.5 Ã— 0.75 m slabs.
        static void PaintSlate(int N, out Color32[] col, out Color32[] hgt)
        {
            col = new Color32[N * N]; hgt = new Color32[N * N]; float S = N / 3f;
            for (int py = 0; py < N; py++) for (int px = 0; px < N; px++)
            {
                float X = px / S, Y = py / S; int row = Mathf.FloorToInt(Y / 0.75f); float xx = (X + (row % 2) * 0.75f) % 3f; int bx = Mathf.FloorToInt(xx / 1.5f);
                float fx = xx - bx * 1.5f, fy = Y - row * 0.75f, edge = Mathf.Min(Mathf.Min(fx, 1.5f - fx), Mathf.Min(fy, 0.75f - fy));
                float n = Fbm(X * 2, Y * 2, 6), fine = VNoise(X * 30, Y * 30, 90), v = (Hash(bx + row * 5, 9) - 0.5f) * 0.14f + (n - 0.5f) * 0.25f + (fine - 0.5f) * 0.07f;
                float cr = 58 * (1 + v), cg = 61 * (1 + v), cb = 67 * (1 + v), h = 0.7f;
                if (edge < 0.04f) { float t = 1 - Smooth(0.008f, 0.04f, edge); cr *= 1 - 0.25f * t; cg *= 1 - 0.25f * t; cb *= 1 - 0.25f * t; h = Mathf.Lerp(h, 0.3f, t); }
                int o = py * N + px; col[o] = C32(cr, cg, cb); byte hb = (byte)Mathf.Clamp(h * 255, 0, 255); hgt[o] = new Color32(hb, hb, hb, 255);
            }
        }

        // Tileable deck planks (2 m square): 0.25 m dark boards running along U, staggered butt joints, bolts, gaps.
        static void PaintPlanks(int N, out Color32[] col, out Color32[] hgt)
        {
            col = new Color32[N * N]; hgt = new Color32[N * N]; float S = N / 2f;
            for (int py = 0; py < N; py++) for (int px = 0; px < N; px++)
            {
                float X = px / S, Y = py / S; int row = Mathf.FloorToInt(Y / 0.25f); float off = Hash(row, 21) * 2f, xx = (X + off) % 2f;
                int seg = Mathf.FloorToInt(xx / 1f); float fx = xx - seg, fy = Y - row * 0.25f, edge = Mathf.Min(Mathf.Min(fx, 1 - fx) * 0.5f, Mathf.Min(fy, 0.25f - fy));
                float grain = Fbm(X * 2, Y * 40, 4), n = Fbm(X * 3, Y * 3, 6), id = Hash(row * 13 + seg, 4), v = (id - 0.5f) * 0.18f + (n - 0.5f) * 0.2f + (grain - 0.5f) * 0.14f;
                float cr = 54 * (1 + v), cg = 50 * (1 + v), cb = 48 * (1 + v), h = 0.7f + (grain - 0.5f) * 0.1f;
                if (edge < 0.02f) { float t = 1 - Smooth(0.004f, 0.02f, edge); cr *= 1 - 0.45f * t; cg *= 1 - 0.45f * t; cb *= 1 - 0.45f * t; h = Mathf.Lerp(h, 0.2f, t); }
                float bx = Mathf.Min(fx, 1 - fx), bd = Mathf.Sqrt(bx * bx * 400 + Mathf.Pow((fy - 0.125f) * 20, 2)); // bolt at each board end
                if (bx < 0.06f && bd < 0.9f) { cr = 120; cg = 98; cb = 66; h = 0.95f; }
                int o = py * N + px; col[o] = C32(cr, cg, cb); byte hb = (byte)Mathf.Clamp(h * 255, 0, 255); hgt[o] = new Color32(hb, hb, hb, 255);
            }
        }

        static Color32[] PaintBrass(int N)
        {
            var col = new Color32[N * N];
            for (int py = 0; py < N; py++) for (int px = 0; px < N; px++)
            {
                float X = px * 4f / N, Y = py * 4f / N, n = Fbm(X, Y, 4), pat = Smooth(0.62f, 0.8f, Fbm(X * 1.7f + 3, Y * 1.7f, 7)), k = 0.86f + 0.2f * n;
                col[py * N + px] = C32(Mathf.Lerp(255 * k, 120, pat * 0.6f), Mathf.Lerp(255 * k, 110, pat * 0.6f), Mathf.Lerp(255 * k, 90, pat * 0.6f));
            }
            return col;
        }

        static Color32[] PaintMolten(int N)
        {
            var col = new Color32[N * N];
            for (int py = 0; py < N; py++) for (int px = 0; px < N; px++)
            {
                float X = px * 6f / N, Y = py * 6f / N, n = Fbm(X, Y, 6), cr = Mathf.Abs(Fbm(X * 1.7f + 3, Y * 1.7f, 10) - 0.5f);
                float hot = Mathf.Clamp01(0.35f + 0.45f * Smooth(0.35f, 0.8f, n) + 0.55f * Smooth(0.07f, 0f, cr));
                col[py * N + px] = C32(70 + 185 * hot, 18 + 190 * hot * hot, 4 + 110 * hot * hot * hot);
            }
            return col;
        }

        static Color32[] PaintSky(int W, int H)
        {
            var col = new Color32[W * H];
            Color top = Hex(0x141010), mid = Hex(0x2E2420), hor = Hex(0x7A3A1E), below = Hex(0x3A2C26), deep = Hex(0x1A1412);
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
            {
                float e = ((y + 0.5f) / H - 0.5f) * PI, u = (float)x / W; Color c;
                if (e > 0) { c = Color.Lerp(hor, mid, Smooth(0, 0.25f, e)); c = Color.Lerp(c, top, Smooth(0.25f, 1.0f, e)); }
                else c = Color.Lerp(hor, below, Smooth(0, 0.12f, -e)); if (e < 0) c = Color.Lerp(c, deep, Smooth(0.15f, 1.2f, -e));
                float smoke = Fbm(u * 12, (e + 2) * 4, 12); c *= 1 - 0.35f * Smooth(0.45f, 0.75f, smoke) * Smooth(-0.05f, 0.4f, e);
                col[y * W + x] = c;
            }
            return col;
        }

        static Color32[] SoftDot(int N)
        {
            var col = new Color32[N * N];
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++) { float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(N / 2f, N / 2f)) / (N / 2f); float a = Mathf.Pow(Mathf.Clamp01(1 - d), 1.6f); col[y * N + x] = C32(255, 255, 255, a * 255); }
            return col;
        }

        static Color32[] VerticalFade(int H)
        {
            var col = new Color32[4 * H];
            for (int y = 0; y < H; y++) { float a = Mathf.Pow(1 - (float)y / (H - 1), 1.8f); for (int x = 0; x < 4; x++) col[y * 4 + x] = C32(255, 255, 255, a * 255); }
            return col;
        }

        static Color Hex(uint rgb, float k = 1) => new Color(((rgb >> 16) & 255) / 255f * k, ((rgb >> 8) & 255) / 255f * k, (rgb & 255) / 255f * k);

        // ================================================================== materials ==================================================================
        static Material Lit(string name, Texture2D albedo, Texture2D normal, Color tint, float metallic, float smooth, float tileMetres, float bump = 1f)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, enableInstancing = true };
            if (albedo) m.SetTexture("_BaseMap", albedo);
            m.SetColor("_BaseColor", tint); m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", smooth);
            if (normal) { m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", bump); m.EnableKeyword("_NORMALMAP"); }
            if (tileMetres > 0) m.mainTextureScale = Vector2.one / tileMetres;
            return SaveAsset(m, "Materials/" + name + ".mat");
        }

        static Material Unlit(string name, Color c, Texture2D tex = null, float tileMetres = 0)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = name, enableInstancing = true };
            m.SetColor("_BaseColor", c); if (tex) m.SetTexture("_BaseMap", tex); if (tileMetres > 0) m.mainTextureScale = Vector2.one / tileMetres;
            return SaveAsset(m, "Materials/" + name + ".mat");
        }

        static Material Particle(string name, Texture2D tex, Color c, bool additive)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = name };
            m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", c);
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", additive ? 2 : 0);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = (int)RenderQueue.Transparent;
            m.SetOverrideTag("RenderType", "Transparent");
            return SaveAsset(m, "Materials/" + name + ".mat");
        }

        static void MakeMaterials()
        {
            PaintFloor(2048, out var fc, out var fh);
            var floorA = SaveTex("FloorSlate", 2048, 2048, fc); var floorN = SaveTex("FloorSlateHeight", 2048, 2048, fh, true, 0.08f);
            PaintMasonry(1024, out var mc, out var mh);
            var masA = SaveTex("Masonry", 1024, 1024, mc); var masN = SaveTex("MasonryHeight", 1024, 1024, mh, true, 0.12f);
            PaintIron(512, out var ic, out var ih);
            var ironA = SaveTex("IronPlate", 512, 512, ic); var ironN = SaveTex("IronPlateHeight", 512, 512, ih, true, 0.1f);
            PaintSlate(512, out var sc, out var sh);
            var slateA = SaveTex("SlateDeck", 512, 512, sc); var slateN = SaveTex("SlateDeckHeight", 512, 512, sh, true, 0.08f);
            var brassA = SaveTex("BrassWear", 256, 256, PaintBrass(256));
            PaintPlanks(512, out var pc, out var ph);
            var plankA = SaveTex("DeckPlanks", 512, 512, pc); var plankN = SaveTex("DeckPlanksHeight", 512, 512, ph, true, 0.1f);
            mPlank = Lit("DeckPlanks", plankA, plankN, Color.white, 0.2f, 0.22f, 2);
            mAbyss = Lit("AbyssGround", null, null, Hex(0x120806), 0f, 0f, 0);
            var moltenT = SaveTex("Molten", 512, 512, PaintMolten(512));
            var dot = SaveTex("SoftDot", 64, 64, SoftDot(64), clamp: true, alpha: true);
            var fade = SaveTex("VerticalFade", 4, 64, VerticalFade(64), clamp: true, alpha: true);

            mFloor = Lit("FoundryFloor", floorA, floorN, Hex(0xD6D4D2), 0.05f, 0.16f, 0, 1f);
            mSlate = Lit("SlateInset", slateA, slateN, Hex(0x9aa0a8), 0.05f, 0.2f, 3);
            mDeck = Lit("SlateDeck", slateA, slateN, Color.white, 0.05f, 0.2f, 3);
            mMasonry = Lit("Masonry", masA, masN, Hex(0xD8D2CE), 0.0f, 0.12f, 4);
            mMasonryDark = Lit("MasonryDark", masA, masN, Hex(0x8a8480), 0.0f, 0.1f, 4);
            mCity = Lit("CitySilhouette", null, null, Hex(0x1E1816), 0.0f, 0.05f, 0);
            mIron = Lit("IronPlate", ironA, ironN, Color.white, 0.55f, 0.38f, 2);
            mBrass = Lit("Brass", brassA, null, Hex(0x6B5023), 0.8f, 0.5f, 1);
            mDarkBrass = Lit("DarkBrass", brassA, null, Hex(0x4E3820), 0.8f, 0.34f, 1);
            mChain = Lit("ChainIron", null, null, Hex(0x2C2724), 0.75f, 0.42f, 0);
            mRoof = Lit("RoofIron", ironA, ironN, Hex(0x4a4442), 0.5f, 0.25f, 2);
            mRimIron = Lit("RimIron", masA, masN, Hex(0x6a6462), 0.15f, 0.2f, 3f);
            mRimTop = Lit("RimTop", ironA, ironN, Hex(0x5a5654), 0.55f, 0.3f, 3f);
            mMolten = Unlit("Molten", Hex(0xFF5A14, 2.6f), moltenT, 4); moltenMats.Add(mMolten);
            mRiver = Unlit("MoltenRiver", Hex(0xFF5014, 1.8f), moltenT, 6); moltenMats.Add(mRiver);
            mGlow = Unlit("GlowWindow", Hex(0xFF7A22, 1.2f)); glowMats.Add(mGlow);
            mGlowDim = Unlit("GlowGate", Hex(0xFF5A18, 0.32f)); glowMats.Add(mGlowDim);
            mGlowCurtain = Particle("GlowCurtain", fade, Hex(0xFF7A26, 1.0f) * new Color(1, 1, 1, 0.8f), true);
            mGlowSheet = Particle("GlowSheet", dot, Hex(0xFF8A3A) * new Color(1, 1, 1, 0.22f), true);
            mEmber = Particle("Ember", dot, Hex(0xFFC070, 4f), true);
            mSmoke = Particle("Smoke", dot, new Color(0.16f, 0.1f, 0.08f, 0.35f), false);
        }

        // ================================================================== arena parts ==================================================================
        // Entries: angle (radians, +X toward +Z) and half width at the rim. E gantry, S entry bridge, W gantry, N furnace gate.
        static readonly (float a, float half)[] Openings = { (0, 2.75f), (PI / 2, 2.6f), (PI, 2.75f), (PI * 1.5f, 2.6f) };

        // Arcs between the four openings at radius r (with extra clearance in metres).
        static IEnumerable<(float a0, float a1)> Arcs(float r, float extra)
        {
            for (int i = 0; i < 4; i++)
            {
                var (aA, hA) = Openings[i]; var (aB, hB) = Openings[(i + 1) % 4]; if (aB <= aA) aB += TAU;
                float a0 = aA + Mathf.Asin(Mathf.Min(0.99f, (hA + extra) / r)), a1 = aB - Mathf.Asin(Mathf.Min(0.99f, (hB + extra) / r));
                if (a1 > a0) yield return (a0, a1);
            }
        }

        static void BuildFloor()
        {
            // the playable disc: one mesh with planar UVs over the 2048 slate texture
            const int SEG = 256; var mesh = new Mesh { name = "CombatFloor", indexFormat = IndexFormat.UInt32 };
            var v = new List<Vector3> { Vector3.zero }; var uv = new List<Vector2> { new Vector2(0.5f, 0.5f) }; var t = new List<int>();
            for (int ring = 1; ring <= 8; ring++) for (int i = 0; i < SEG; i++)
            {
                float a = TAU * i / SEG, r = R * ring / 8f; v.Add(new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r)); uv.Add(new Vector2((v[v.Count - 1].x + R) / (2 * R), (v[v.Count - 1].z + R) / (2 * R)));
            }
            for (int i = 0; i < SEG; i++) { t.Add(0); t.Add(1 + (i + 1) % SEG); t.Add(1 + i); }
            for (int ring = 1; ring < 8; ring++) for (int i = 0; i < SEG; i++)
            {
                int a = 1 + (ring - 1) * SEG + i, b = 1 + (ring - 1) * SEG + (i + 1) % SEG, c = a + SEG, d = b + SEG;
                t.Add(a); t.Add(b); t.Add(d); t.Add(a); t.Add(d); t.Add(c);
            }
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0); mesh.SetNormals(v.Select(_ => Vector3.up).ToList()); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            mesh = SaveAsset(mesh, "Meshes/CombatFloor.asset");
            var go = new GameObject("CombatFloor"); go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mFloor; mr.shadowCastingMode = ShadowCastingMode.Off;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            int layer = LayerMask.NameToLayer("ArenaFloor"); if (layer >= 0) go.layer = layer;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        }

        // Gold cross, spear tips, end plates, diagonal plaques, compass medallion, brass edge strip. All raised 1â€“3 cm.
        static void BuildInlays()
        {
            var brass = G("CombatFloor/Inlays", mBrass); var dark = G("CombatFloor/Inlays", mSlate); var iron = G("CombatFloor/Inlays", mDarkBrass);
            const float H = 0.012f;
            Sector(brass, Matrix4x4.identity, 13.88f, 13.98f, 0, TAU, 0, 0.01f, 256, ends: false);
            for (int k = 0; k < 4; k++)
            {
                float a = k * PI / 2; var m = TR(Vector3.zero, YawOut(a)); bool ew = k % 2 == 0; // local +X points outward along the arm
                Box(brass, m, new Vector3(10.4f - 2.75f, H, 0.09f), new Vector3((2.75f + 10.4f) / 2, H / 2, 0));
                Box(brass, m, new Vector3(13.2f - 12.55f, H, 0.09f), new Vector3((12.55f + 13.2f) / 2, H / 2, 0));
                // spear tip: brass diamond with fins, dark inset; E and W carry an anchor crossbar
                float w = ew ? 0.95f : 0.75f;
                Poly(brass, m, new[] { new Vector2(10.3f, 0), new Vector2(10.95f, w / 2), new Vector2(12.65f, 0), new Vector2(10.95f, -w / 2) }, 0, H);
                Poly(dark, m, new[] { new Vector2(10.6f, 0), new Vector2(11.0f, w * 0.28f), new Vector2(12.2f, 0), new Vector2(11.0f, -w * 0.28f) }, 0, H + 0.004f, false);
                foreach (float s in new[] { -1f, 1f }) Poly(brass, m, new[] { new Vector2(10.75f, s * w * 0.42f), new Vector2(11.15f, s * w * 0.42f), new Vector2(10.85f, s * (w * 0.42f + 0.38f)), new Vector2(10.55f, s * (w * 0.42f + 0.3f)) }, 0, H);
                if (ew) { Box(brass, m, new Vector3(0.12f, H, 1.7f), new Vector3(10.55f, H / 2, 0)); foreach (float s in new[] { -1f, 1f }) Box(brass, m, new Vector3(0.36f, H, 0.12f), new Vector3(10.7f, H / 2, s * 0.85f)); }
                // end plate
                Box(brass, m, new Vector3(0.5f, H, 0.8f), new Vector3(12.95f, H / 2, 0));
                Box(dark, m, new Vector3(0.32f, H + 0.004f, 0.6f), new Vector3(12.95f, (H + 0.004f) / 2, 0));
                // diagonal plaque at 45Â° off this arm: trapezoid r 10.6..12.6, double brass outline
                var md = TR(Vector3.zero, YawOut(a + PI / 4));
                Vector2[] Trap(float inset) => new[] { new Vector2(10.9f + inset, -0.95f + inset), new Vector2(12.4f - inset, -1.4f + inset * 1.1f), new Vector2(12.4f - inset, 1.4f - inset * 1.1f), new Vector2(10.9f + inset, 0.95f - inset) };
                Poly(brass, md, Trap(0), 0, 0.01f); Poly(dark, md, Trap(0.09f), 0, 0.013f, false); Poly(brass, md, Trap(0.2f), 0, 0.015f, false); Poly(dark, md, Trap(0.27f), 0, 0.018f, false);
                Box(brass, md, new Vector3(0.05f, 0.02f, 1.5f), new Vector3(11.65f, 0.01f, 0)); // engraved centre line
            }
            // compass medallion: two brass rings, 8-point star, dark centre with a brass rim, tick marks
            Sector(brass, Matrix4x4.identity, 2.55f, 2.7f, 0, TAU, 0, 0.025f, 96, ends: false);
            Sector(brass, Matrix4x4.identity, 1.9f, 2.0f, 0, TAU, 0, 0.02f, 96, ends: false);
            Sector(dark, Matrix4x4.identity, 2.0f, 2.55f, 0, TAU, 0, 0.008f, 96, ends: false, inner: false, outer: false);
            for (int i = 0; i < 32; i++) { float a = i / 32f * TAU; Box(brass, TR(Vector3.zero, YawOut(a)), new Vector3(i % 4 == 0 ? 0.42f : 0.22f, 0.02f, 0.05f), new Vector3(i % 4 == 0 ? 2.25f : 2.33f, 0.01f, 0)); }
            var star = new List<Vector2>();
            for (int i = 0; i < 16; i++) { float a = i / 16f * TAU, r = i % 2 == 1 ? 0.42f : (i % 4 == 0 ? 1.8f : 1.1f); star.Add(new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r)); }
            Poly(brass, Matrix4x4.identity, star.ToArray(), 0, 0.03f);
            var inner = star.Select((p, i) => p * (i % 2 == 1 ? 0.6f : 0.55f)).ToArray();
            Poly(iron, Matrix4x4.identity, inner, 0, 0.034f, false);
            Sector(brass, Matrix4x4.identity, 0.28f, 0.36f, 0, TAU, 0, 0.04f, 32, ends: false);
            Lathe(dark, Matrix4x4.identity, new[] { new Vector2(0.28f, 0), new Vector2(0.28f, 0.036f), new Vector2(0, 0.036f) }, 24);
        }

        static void BuildRim()
        {
            bevel = 0.07f; var rimIron = G("FurnaceRim", mRimIron);
            var mas = G("FurnaceRim", mMasonry); var masD = G("FurnaceRim", mMasonryDark); var brass = G("FurnaceRim", mBrass); var dbrass = G("FurnaceRim", mDarkBrass); var iron = G("FurnaceRim", mIron);
            foreach (var (a0, a1) in Arcs(14.2f, 0.1f))
            {
                Sector(mas, Matrix4x4.identity, 14.0f, 14.4f, a0, a1, 0, 0.25f);                          // inner curb
                Sector(brass, Matrix4x4.identity, 13.98f, 14.18f, a0, a1, 0.25f, 0.29f, ends: true);      // brass top strip
                Sector(masD, Matrix4x4.identity, 14.4f, 16.8f, a0, a1, -1.7f, 0.02f, inner: false);       // rim base; outer face is the channel's inner wall
                for (float a = a0 + 0.08f; a < a1; a += 0.62f / 16.8f * 2.2f)                           // corbels down the channel wall
                    Box(masD, TR(Polar(a, 16.95f, -0.75f), YawOut(a)), new Vector3(0.35f, 0.5f, 0.45f));
            }
            // the block ring: big and small blocks alternate like gear teeth, sizes jittered (seeded)
            foreach (var (a0, a1) in Arcs(15.5f, 0.35f))
            {
                float L = (a1 - a0) * 15.5f, unit = (1.55f + 0.95f + 0.24f); int pairs = Mathf.Max(1, Mathf.RoundToInt(L / unit)); float k = L / (pairs * unit), s = 0;
                for (int i = 0; i < pairs * 2; i++)
                {
                    bool big = i % 2 == 0; float w = (big ? 1.55f : 0.95f) * k * Rnd(0.9f, 1.05f);
                    float mid = a0 + (s + (big ? 1.55f : 0.95f) * k / 2) / 15.5f; s += ((big ? 1.55f : 0.95f) + 0.12f) * k;
                    float rIn = big ? 14.45f : 14.6f, depth = big ? 2.35f : 1.7f, h = big ? 1.25f : 0.72f, rc = rIn + depth / 2;
                    // local frame: +X tangential, +Z radial outward
                    var m = TRQ(Polar(mid, 0, 0), Quaternion.LookRotation(Polar(mid, 1), Vector3.up));
                    var body = rimIron;
                    Box(body, m, new Vector3(w, h, depth), new Vector3(0, h / 2, rc));
                    if (big)
                    {
                        Box(G("FurnaceRim", mRimTop), m, new Vector3(w - 0.16f, 0.06f, depth - 0.16f), new Vector3(0, h + 0.03f, rc));       // dark iron top plate
                        foreach (float sx in new[] { -1f, 1f }) Box(brass, m, new Vector3(0.14f, 0.16f, 0.14f), new Vector3(sx * (w / 2 - 0.05f), h - 0.06f, rIn + 0.05f)); // corner studs
                        Box(dbrass, m, new Vector3(w - 0.1f, 0.07f, 0.07f), new Vector3(0, h - 0.02f, rIn - 0.01f));            // brass trim along the inner top edge
                    }
                    else Box(dbrass, m, new Vector3(w - 0.1f, 0.05f, 0.07f), new Vector3(0, h - 0.02f, rIn - 0.01f)); // small blocks: brass trim only
                }
            }
            bevel = 0;
        }

        static void BuildChannel()
        {
            Sector(G("MoltenChannel", mMolten), Matrix4x4.identity, 16.75f, 18.65f, 0, TAU, -1.42f, -1.4f, 220, inner: false, outer: false, ends: false);
            // a glow curtain rising off the molten (both faces), and a soft sheet hovering over it
            var cur = G("MoltenChannel", mGlowCurtain);
            Sector(cur, Matrix4x4.identity, 17.7f, 17.7f, 0, TAU, -1.4f, 0.8f, 160, top: false, ends: false);
            Sector(G("MoltenChannel", mGlowSheet), Matrix4x4.identity, 16.6f, 18.8f, 0, TAU, -0.9f, -0.9f, 160, inner: false, outer: false, ends: false);
            var lights = Child(root, "Lighting/ChannelLights");
            for (int i = 0; i < 16; i++)
            {
                float a = (i + 0.5f) / 16 * TAU; var l = new GameObject("Molten Uplight").AddComponent<Light>(); l.transform.SetParent(lights, false);
                l.type = LightType.Point; l.color = Hex(0xFF7A1F); l.intensity = 14f; l.range = 7; l.shadows = LightShadows.None; l.transform.position = Polar(a, 17.6f, -0.8f);
            }
            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * TAU; var l = new GameObject("Rim Glow").AddComponent<Light>(); l.transform.SetParent(lights, false);
                l.type = LightType.Point; l.color = Hex(0xFF8A30); l.intensity = 5f; l.range = 4.5f; l.shadows = LightShadows.None; l.transform.position = Polar(a, 17.9f, 0.7f);
            }
            for (int i = 0; i < 16; i++)
            {
                float a = (i + 0.5f) / 16 * TAU; var l = new GameObject("Rim Bounce").AddComponent<Light>(); l.transform.SetParent(lights, false);
                l.type = LightType.Point; l.color = Hex(0xFF7A2A); l.intensity = 2.2f; l.range = 2.2f; l.shadows = LightShadows.None; l.transform.position = Polar(a, 15.6f, 1.6f);
            }
            {
                var edgeMat = Particle("FloorEdgeWarmth", AssetDatabase.LoadAssetAtPath<Texture2D>(Gen + "/Textures/VerticalFade.png"), Hex(0xFF6A20) * new Color(1, 1, 1, 0f), true);
                var eg = new Geo(); const int SEG = 128;
                for (int i = 0; i < SEG; i++)
                {
                    float a0 = TAU * i / SEG, a1 = TAU * (i + 1) / SEG;
                    Quad(eg, Matrix4x4.identity, Polar(a0, 13.98f, 0.004f), Polar(a1, 13.98f, 0.004f), Polar(a1, 12.6f, 0.004f), Polar(a0, 12.6f, 0.004f), Vector3.up, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
                }
            }
            // glowing abyss: soft additive sheets far below the arena
            foreach (var (y, size, alpha) in new[] { (-20f, 70f, 0.07f), (-30f, 120f, 0.05f) })
            {
                var mat = Particle("AbyssGlow" + (int)-y, AssetDatabase.LoadAssetAtPath<Texture2D>(Gen + "/Textures/SoftDot.png"), Hex(0xFF6A20) * new Color(1, 1, 1, alpha), true);
                float hs = size / 2; Quad(G("MoltenChannel", mat), Matrix4x4.identity, new Vector3(-hs, y, -hs), new Vector3(hs, y, -hs), new Vector3(hs, y, hs), new Vector3(-hs, y, hs), Vector3.up, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            }
        }

        static void BuildOuterWall()
        {
            var mas = G("OuterWall", mMasonryDark); var brass = G("OuterWall", mDarkBrass); var iron = G("OuterWall", mIron); var glow = G("OuterWall", mGlow); var mas2 = G("OuterWall", mMasonry);
            var gaps = new (float a, float half)[] { (0, 3.0f), (PI / 2, 2.55f), (PI, 3.0f), (PI * 1.5f, 4.35f) };
            for (int q = 0; q < 4; q++)
            {
                var (aA, hA) = gaps[q]; var (aB, hB) = gaps[(q + 1) % 4]; if (aB <= aA) aB += TAU;
                float a0 = aA + Mathf.Asin(hA / 18.6f), a1 = aB - Mathf.Asin(hB / 18.6f); int n = Mathf.Max(1, Mathf.RoundToInt((a1 - a0) / (9f * Mathf.Deg2Rad)));
                for (int i = 0; i < n; i++)
                {
                    float b0 = a0 + (a1 - a0) * i / n, b1 = a0 + (a1 - a0) * (i + 1) / n, h = Rnd(1.5f, 3f);
                    Sector(mas, Matrix4x4.identity, 18.6f, 21.2f, b0 + 0.003f, b1 - 0.003f, -1.7f, h, 4);
                    Sector(brass, Matrix4x4.identity, 18.5f, 18.85f, b0 + 0.003f, b1 - 0.003f, h, h + 0.12f, 4);
                    
                    float am = (b0 + b1) / 2;
                    if (i % 2 == 0) Box(glow, TR(Polar(am, 18.58f, -0.75f), YawOut(am)), new Vector3(0.05f, 0.55f, 0.28f));
                    
                }
                for (float a = a0; a <= a1 + 1e-3f; a += 15 * Mathf.Deg2Rad) // buttresses
                {
                    float h = 3.2f; var m = TR(Polar(a, 21.7f, 0), YawOut(a));
                    Box(mas, m, new Vector3(1.9f, h + 30, 1.3f), new Vector3(0, (h - 30) / 2, 0));
                    Box(iron, m, new Vector3(2.0f, 0.25f, 1.4f), new Vector3(0, h, 0));
                    Cyl(iron, TR(Polar(a + 0.06f, 21.4f, 0)), 0.16f, -8, h - 0.4f, 8);
                }
            }
            // the foundry drum below the wall: masonry, ribs, glowing arches, tapering into the haze
            var drum = G("OuterWall", mMasonryDark);
            Lathe(drum, Matrix4x4.identity, new[] { new Vector2(10, -38), new Vector2(18, -30), new Vector2(21.2f, -26), new Vector2(21.2f, -1.7f) }, 96);
            for (int i = 0; i < 10; i++) // molten falls spilling down the outer drum
            {
                float a = (i + 0.37f) / 10 * TAU, w = Rnd(0.8f, 1.6f); Vector3 o = Polar(a, 21.75f), t = Polar(a + PI / 2, 1) * w / 2;
                Quad(G("OuterWall", mRiver), Matrix4x4.identity, o - t + Vector3.up * -30, o + t + Vector3.up * -30, o + t + Vector3.up * 0.4f, o - t + Vector3.up * 0.4f, Polar(a, 1), new Vector2(0, 0), new Vector2(w, 0), new Vector2(w, 30), new Vector2(0, 30));
                Box(G("OuterWall", mDarkBrass), TR(Polar(a, 21.6f, 0.5f), YawOut(a)), new Vector3(0.9f, 0.35f, w + 0.4f));
            }
            for (int i = 0; i < 36; i++)
            {
                float a = i / 36f * TAU; Box(iron, TR(Polar(a, 21.45f, -14), YawOut(a)), new Vector3(0.45f, 24, 0.55f));
                if (i % 2 == 0) Box(glow, TR(Polar(a + PI / 36, 21.25f, -8), YawOut(a + PI / 36)), new Vector3(0.05f, 1.6f, 0.8f));
            }
        }

        static void BuildTowers()
        {
            var mas = G("Towers", mMasonry); var masD = G("Towers", mMasonryDark); var brass = G("Towers", mDarkBrass); var b2 = G("Towers", mBrass); var roof = G("Towers", mRoof); var glow = G("Towers", mGlow); var iron = G("Towers", mIron);
            foreach (var deg in new[] { 45f, 135f, 225f, 315f })
            {
                float a = deg * Mathf.Deg2Rad; Vector3 c = Polar(a, Mathf.Sin(a) > 0 ? 21.5f : 20f); bool near = c.z > 0; float top = near ? 8f : 14f;
                float t1 = top * 0.45f, t2 = top * 0.72f, t3 = top * 0.88f; var m = TR(c, YawOut(a));
                Lathe(masD, m, new[] { new Vector2(3.6f, -28), new Vector2(3.6f, t1) }, 32);
                Sector(roof, m, 3.25f, 3.95f, 0, TAU, t1, t1 + 0.42f, 48, ends: false, bottom: true); Sector(b2, m, 3.93f, 4.0f, 0, TAU, t1 + 0.05f, t1 + 0.37f, 48, ends: false);
                Lathe(mas, m, new[] { new Vector2(3.05f, t1 + 0.42f), new Vector2(3.05f, t2) }, 32);
                Sector(roof, m, 2.7f, 3.35f, 0, TAU, t2, t2 + 0.35f, 48, ends: false, bottom: true); Sector(b2, m, 3.33f, 3.4f, 0, TAU, t2 + 0.05f, t2 + 0.3f, 48, ends: false);
                Lathe(masD, m, new[] { new Vector2(2.45f, t2 + 0.35f), new Vector2(2.45f, t3) }, 32);
                Sector(roof, m, 2.2f, 2.8f, 0, TAU, t3, t3 + 0.28f, 48, ends: false, bottom: true); Sector(b2, m, 2.78f, 2.84f, 0, TAU, t3 + 0.04f, t3 + 0.24f, 48, ends: false);
                for (int i = 0; i < 12; i++) { float b = i / 12f * TAU; Box(mas, TRQ(c + Polar(b, 2.55f, t3 + 0.28f + 0.25f), Quaternion.LookRotation(Polar(b, 1))), new Vector3(0.45f, 0.5f, 0.4f)); }
                Lathe(roof, m, new[] { new Vector2(2.3f, t3 + 0.28f), new Vector2(2.2f, t3 + 0.8f), new Vector2(1.85f, t3 + 1.55f), new Vector2(1.15f, t3 + 2.15f), new Vector2(0.4f, t3 + 2.45f), new Vector2(0, t3 + 2.5f) }, 32);
                Cyl(b2, m, 0.08f, t3 + 2.4f, t3 + 3.6f, 8);
                for (int i = 0; i < 8; i++) { float b = i / 8f * TAU; Lathe(brass, m * Matrix4x4.Rotate(Quaternion.Euler(0, -b * Mathf.Rad2Deg, 0)), new[] { new Vector2(2.36f, t3 + 0.28f), new Vector2(2.26f, t3 + 0.8f), new Vector2(1.91f, t3 + 1.55f), new Vector2(1.2f, t3 + 2.17f), new Vector2(0.42f, t3 + 2.47f) }, 1, -0.035f, 0.035f); }
                Sector(brass, m, 3.6f, 3.68f, 0, TAU, t1 - 2.5f, t1 - 2.2f, 48, ends: false);
                Sector(brass, m, 3.05f, 3.12f, 0, TAU, (t1 + t2) / 2, (t1 + t2) / 2 + 0.25f, 48, ends: false);
                Lathe(b2, m, new[] { new Vector2(0, t3 + 3.2f), new Vector2(0.18f, t3 + 3.35f), new Vector2(0.18f, t3 + 3.45f), new Vector2(0, t3 + 3.6f) }, 10);
                for (int i = 0; i < 10; i++) // lantern stage slit windows with iron frames
                {
                    float b = (i + 0.5f) / 10 * TAU; var q = Quaternion.LookRotation(Polar(b, 1)); float sh = t3 - t2 - 0.9f;
                    Box(glow, TRQ(c + Polar(b, 2.44f, t2 + 0.35f + 0.25f + sh / 2), q), new Vector3(0.28f, sh, 0.08f));
                    Box(iron, TRQ(c + Polar(b, 2.47f, t2 + 0.35f + 0.25f + sh / 2), q), new Vector3(0.05f, sh, 0.06f));
                }
                for (int i = 0; i < 8; i++)
                {
                    float b = (i + 0.5f) / 8 * TAU; var q = Quaternion.LookRotation(Polar(b, 1));
                    Box(glow, TRQ(c + Polar(b, 3.62f, t1 - 1.6f), q), new Vector3(0.22f, 1.3f, 0.08f));
                    Box(glow, TRQ(c + Polar(b + PI / 8, 3.08f, (t1 + t2) / 2 + 0.9f), Quaternion.LookRotation(Polar(b + PI / 8, 1))), new Vector3(0.2f, 0.8f, 0.08f));
                    Box(masD, TRQ(c + Polar(b + PI / 8, 3.75f, (t1 - 30.2f) / 2), Quaternion.LookRotation(Polar(b + PI / 8, 1))), new Vector3(0.5f, t1 + 29.8f, 0.5f)); // buttress fin
                }
                for (int i = 0; i < 8; i++) { float b = (i + 0.5f) / 8 * TAU; Box(iron, TRQ(c + Polar(b, 3.55f, t1 + 0.42f + 0.25f), Quaternion.LookRotation(Polar(b, 1))), new Vector3(0.7f, 0.5f, 0.55f)); }
                // heavy chains from the tower crown out and down beyond the wall
                foreach (float off in new[] { -0.4f, 0.4f })
                {
                    float b = a + off; Chain(c + Polar(b, 2.4f, t3 + 0.1f), Polar(b, 34, -9), 1.2f);
                }
            }
        }

        // Shared deck with girders, X truss and cross beams under it. Local frame: +X outward along the deck.
        static void Deck(Matrix4x4 m, float x0, float x1, float width, Material deckMat, string group)
        {
            var deck = G(group, deckMat); var iron = G(group, mIron); var brass = G(group, mBrass); float len = x1 - x0, cx = (x0 + x1) / 2;
            Box(deck, m, new Vector3(len, 0.5f, width), new Vector3(cx, -0.25f, 0), true);
            foreach (float s in new[] { -1f, 1f })
            {
                Box(brass, m, new Vector3(len, 0.06f, 0.12f), new Vector3(cx, 0.03f, s * (width / 2 - 0.06f)));
                float gz = s * (width / 2 - 0.3f);
                Box(iron, m, new Vector3(len, 0.3f, 0.3f), new Vector3(cx, -0.65f, gz)); Box(iron, m, new Vector3(len, 0.3f, 0.3f), new Vector3(cx, -2.3f, gz));
                for (float x = x0 + 2.3f; x < x1; x += 2f)
                {
                    Box(iron, m, new Vector3(0.22f, 1.65f, 0.22f), new Vector3(x, -1.48f, gz));
                    Bar(iron, m.MultiplyPoint3x4(new Vector3(x, -0.7f, gz)), m.MultiplyPoint3x4(new Vector3(x + 2f, -2.25f, gz)), 0.12f, 0.12f);
                    Bar(iron, m.MultiplyPoint3x4(new Vector3(x, -2.25f, gz)), m.MultiplyPoint3x4(new Vector3(x + 2f, -0.7f, gz)), 0.12f, 0.12f);
                }
            }
            for (float x = x0 + 2.3f; x < x1; x += 2f) Box(iron, m, new Vector3(0.25f, 0.3f, width - 0.6f), new Vector3(x, -0.65f, 0));
        }

        static void Parapet(Matrix4x4 m, float x0, float x1, float z, string group, bool lamps)
        {
            var iron = G(group, mIron); var brass = G(group, mBrass); float len = x1 - x0;
            for (float x = x0; x <= x1 + 1e-3f; x += 1.5f) Box(iron, m, new Vector3(0.15f, 0.9f, 0.15f), new Vector3(x, 0.45f, z));
            Box(brass, m, new Vector3(len, 0.1f, 0.18f), new Vector3((x0 + x1) / 2, 0.95f, z)); Box(iron, m, new Vector3(len, 0.07f, 0.08f), new Vector3((x0 + x1) / 2, 0.5f, z));
            if (!lamps) return;
            for (float x = x0 + 2.2f; x < x1; x += 6f)
            {
                Box(iron, m, new Vector3(0.16f, 2.4f, 0.16f), new Vector3(x, 1.2f, z));
                Lantern(Child(root, group + "/Lanterns"), m.MultiplyPoint3x4(new Vector3(x, 2.75f, z)), 0.42f, false);
            }
        }

        static void BuildGantries()
        {
            foreach (var (a, name) in new[] { (0f, "East"), (PI, "West") })
            {
                var m = TR(Vector3.zero, YawOut(a)); string grp = "Gantries";
                Deck(m, 14f, 40f, 5.5f, mPlank, grp);
                Box(G(grp, mBrass), m, new Vector3(0.25f, 0.06f, 5.5f), new Vector3(14.15f, 0.03f, 0)); // threshold
                foreach (float s in new[] { -1f, 1f })
                {
                    Parapet(m, 16.9f, 40f, s * 2.62f, grp, s > 0);
                    // suspension pillar: tall square masonry with brass bands and a pyramid cap
                    var pm = m * Matrix4x4.Translate(new Vector3(26f, 0, s * 3.45f));
                    Box(G(grp, mMasonryDark), pm, new Vector3(1.1f, 35, 1.1f), new Vector3(0, -10.5f, 0));
                    foreach (float y in new[] { 0.6f, 6.6f }) Box(G(grp, mDarkBrass), pm, new Vector3(1.3f, 0.25f, 1.3f), new Vector3(0, y, 0));
                    Lathe(G(grp, mRoof), pm * Matrix4x4.Rotate(Quaternion.Euler(0, 45, 0)), new[] { new Vector2(0.95f, 7f), new Vector2(0, 8.3f) }, 4);
                    Vector3 topP = m.MultiplyPoint3x4(new Vector3(26f, 6.9f, s * 3.45f));
                    Chain(topP, m.MultiplyPoint3x4(new Vector3(17.5f, 0.95f, s * 2.62f)), 0.35f);
                    Chain(topP, m.MultiplyPoint3x4(new Vector3(34.5f, 0.95f, s * 2.62f)), 0.35f);
                }
            }
        }

        static void BuildEntryBridge()
        {
            var m = TR(Vector3.zero, YawOut(PI / 2)); string grp = "EntryBridge";
            Deck(m, 14f, 40f, 4.5f, mPlank, grp); bevel = 0.06f;
            var brass = G(grp, mBrass); var mas = G(grp, mMasonry); var dbr = G(grp, mDarkBrass); var glow = G(grp, mGlow); var iron = G(grp, mIron);
            Box(brass, m, new Vector3(26f, 0.012f, 0.12f), new Vector3(27f, 0.006f, 0));
            // arrows pointing in toward the arena
            foreach (var (tip, size) in new[] { (19f, 1.3f), (24f, 0.9f) })
            {
                var t = m.MultiplyPoint3x4(new Vector3(tip, 0.01f, 0));
                foreach (float s in new[] { -1f, 1f }) Bar(brass, t, m.MultiplyPoint3x4(new Vector3(tip + size, 0.01f, s * size)), 0.16f, 0.016f);
                Box(brass, m, new Vector3(size * 2.2f, 0.016f, 0.16f), new Vector3(tip + size * 1.1f, 0.008f, 0));
            }
            foreach (float s in new[] { -1f, 1f })
            {
                Box(mas, m, new Vector3(23f, 0.3f, 0.3f), new Vector3(28.5f, 0.15f, s * 2.1f)); Box(brass, m, new Vector3(23f, 0.06f, 0.34f), new Vector3(28.5f, 0.33f, s * 2.1f));
                Parapet(m, 21.5f, 40f, s * 2.1f, grp, s < 0);
                // gate pillars, set out at r 17.2 so they never cover the floor from the gameplay camera
                var pm = m * Matrix4x4.Translate(new Vector3(17.2f, 0, s * 2.85f));
                Box(mas, pm, new Vector3(1.2f, 10f, 1.2f), new Vector3(0, -1f, 0));
                Box(dbr, pm, new Vector3(1.45f, 0.22f, 1.45f), new Vector3(0, 4.1f, 0));
                Box(iron, pm, new Vector3(0.7f, 0.12f, 0.7f), new Vector3(0, 4.27f, 0));
                Lantern(Child(root, grp + "/Lanterns"), pm.MultiplyPoint3x4(new Vector3(0, 4.85f, 0)), 0.5f, false);
                Box(glow, pm, new Vector3(0.06f, 0.9f, 0.3f), new Vector3(-0.62f, 2.3f, 0));
                Chain(pm.MultiplyPoint3x4(new Vector3(0, 3.9f, 0)), m.MultiplyPoint3x4(new Vector3(21.6f, 0.4f, s * 2.1f)), 0.25f);
            }
            bevel = 0;
        }

        static void BuildFurnaceGate()
        {
            var m = TR(Vector3.zero, YawOut(PI * 1.5f)); string grp = "FurnaceGate";
            var mas = G(grp, mMasonry); var masD = G(grp, mMasonryDark); var dbr = G(grp, mDarkBrass); var brass = G(grp, mBrass); var iron = G(grp, mIron); var roof = G(grp, mRoof); var glow = G(grp, mGlow); var glowDim = G(grp, mGlowDim);
            Deck(m, 14f, 32f, 4.5f, mPlank, grp); bevel = 0.07f;
            foreach (float s in new[] { -1f, 1f })
            {
                var pm = m * Matrix4x4.Translate(new Vector3(19.5f, 0, s * 3.25f));
                Box(masD, pm, new Vector3(2f, 9f, 2f), new Vector3(0, 1.5f, 0));
                for (int b = 0; b < 3; b++) Box(dbr, pm, new Vector3(2.12f, 0.18f, 2.12f), new Vector3(0, 0.8f + b * 2.1f, 0));
                Box(dbr, pm, new Vector3(2.35f, 0.25f, 2.35f), new Vector3(0, 6.12f, 0));
                Box(mas, pm, new Vector3(1.6f, 0.8f, 1.6f), new Vector3(0, 6.65f, 0));
                Box(brass, pm, new Vector3(1.8f, 0.15f, 1.8f), new Vector3(0, 7.12f, 0));
                Lathe(roof, pm * Matrix4x4.Translate(new Vector3(0, 7.2f, 0)) * Matrix4x4.Rotate(Quaternion.Euler(0, 45, 0)), new[] { new Vector2(1.05f, 0), new Vector2(0, 1.3f) }, 4);
                Box(glow, pm, new Vector3(0.06f, 1.1f, 0.5f), new Vector3(-1.02f, 2.6f, 0));                 // window facing the arena
                Box(glow, pm, new Vector3(0.5f, 1.2f, 0.06f), new Vector3(0, 3.0f, s * 1.02f));              // window on the outer side
                Chain(pm.MultiplyPoint3x4(new Vector3(0.3f, 6.9f, 0)), m.MultiplyPoint3x4(new Vector3(31f, 21f, s * 7f)), 0.6f);
            }
            Box(masD, m, new Vector3(1.6f, 1.3f, 4.6f), new Vector3(19.5f, 5.45f, 0));
            Box(dbr, m, new Vector3(1.7f, 0.15f, 4.7f), new Vector3(19.5f, 6.15f, 0));
            // gear emblem on the lintel, facing the arena
            var em = m * Matrix4x4.Translate(new Vector3(18.66f, 5.45f, 0)) * Matrix4x4.Rotate(Quaternion.Euler(0, 0, 90));
            Lathe(brass, em, new[] { new Vector2(0.55f, 0), new Vector2(0.55f, 0.12f), new Vector2(0.18f, 0.16f), new Vector2(0, 0.16f) }, 24);
            for (int i = 0; i < 12; i++) { float b = i / 12f * TAU; Box(brass, em, new Vector3(0.16f, 0.12f, 0.16f), new Vector3(Mathf.Cos(b) * 0.62f, 0.06f, Mathf.Sin(b) * 0.62f)); }
            // closed portcullis lit from behind
            for (int i = 0; i <= 8; i++) { float z = -2.05f + i * 4.1f / 8; Box(iron, m, new Vector3(0.16f, 4.75f, 0.16f), new Vector3(19.1f, 2.38f, z)); }
            for (int j = 0; j < 4; j++) Box(iron, m, new Vector3(0.09f, 0.1f, 4.2f), new Vector3(19.12f, 0.6f + j * 1.15f, 0));
            Box(glowDim, m, new Vector3(0.05f, 4.7f, 4.4f), new Vector3(19.9f, 2.35f, 0));
            Box(masD, m, new Vector3(0.6f, 4.8f, 4.5f), new Vector3(20.6f, 2.4f, 0));
            bevel = 0;
        }

        static void BuildSurroundings()
        {
            string grp = "Surroundings"; var city = G(grp, mCity); var iron = G(grp, mRoof); var glow = G(grp, mGlow); var river = G(grp, mRiver); var brass = G(grp, mDarkBrass);
            Lathe(G(grp, mAbyss), Matrix4x4.identity, new[] { new Vector2(320, -34), new Vector2(0, -34) }, 64); // ground
            for (int i = 0; i < 18; i++) { float a = Rnd() * TAU, r = Rnd(40, 120); var l = new GameObject("Pit Glow").AddComponent<Light>(); l.transform.SetParent(Child(root, "Lighting/PitGlow"), false); l.type = LightType.Point; l.color = Hex(0xFF5A18); l.intensity = 40; l.range = 22; l.shadows = LightShadows.None; l.transform.position = Polar(a, r, -31); }
            // molten rivers between the blocks
            for (int k = 0; k < 0; k++)
            {
                float a = k / 6f * TAU + Rnd(-0.3f, 0.3f), w = Rnd(1.4f, 2.6f); var pts = new List<Vector3>();
                for (float r = 30; r < 200; r += 5) { a += Rnd(-0.09f, 0.09f); pts.Add(Polar(a, r, -33.9f)); }
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    Vector3 d = (pts[i + 1] - pts[i]).normalized, side = Vector3.Cross(Vector3.up, d) * w / 2;
                    Quad(river, Matrix4x4.identity, pts[i] - side, pts[i] + side, pts[i + 1] + side, pts[i + 1] - side, Vector3.up, new Vector2(0, i * 8), new Vector2(w, i * 8), new Vector2(w, i * 8 + 8), new Vector2(0, i * 8 + 8));
                }
            }
            
            // lower city blocks with roofs and a few lit windows
            for (int i = 0; i < 300; i++)
            {
                float a = Rnd() * TAU, r = Rnd(25, 125), w = Rnd(3, 9), d = Rnd(3, 9), top = Mathf.Lerp(-18, -26, Rnd()) + (r > 70 ? Rnd(0, 8) : 0);
                var m = TR(Polar(a, r, 0), Rnd(0, 90)); float h = top + 34;
                Box(city, m, new Vector3(w, h, d), new Vector3(0, -34 + h / 2, 0));
                if (Rnd() < 0.4f) Lathe(iron, m * Matrix4x4.Rotate(Quaternion.Euler(0, 45, 0)), new[] { new Vector2(Mathf.Min(w, d) * 0.7f, top), new Vector2(0, top + Rnd(1.5f, 3)) }, 4);
                else Box(iron, m, new Vector3(w + 0.3f, 0.35f, d + 0.3f), new Vector3(0, top + 0.17f, 0));
                for (int j = 0; j < 4; j++) if (Rnd() < 0.6f) Box(glow, m, new Vector3(0.35f, 1.4f, 0.06f), new Vector3(Rnd(-w / 3, w / 3), top - Rnd(1.5f, 6), d / 2 + 0.03f));
                if (Rnd() < 0.35f) Box(G(grp, mGlowDim), m, new Vector3(0.9f, 0.06f, 0.5f), new Vector3(Rnd(-w / 4, w / 4), top + 0.37f, Rnd(-d / 4, d / 4))); // furnace vent on the roof
            }
            // chimney stacks and far silhouettes
            var smokeTops = new List<Vector3>();
            for (int i = 0; i < 16; i++)
            {
                float a = Rnd() * TAU, r = Rnd(42, 130), rad = Rnd(1, 2.2f), top = Rnd(-2, 26); var m = TR(Polar(a, r, 0));
                Lathe(city, m, new[] { new Vector2(rad * 1.2f, -34), new Vector2(rad, top) }, 16);
                for (float y = -20; y < top - 2; y += 9) Sector(brass, m, rad, rad + 0.18f, 0, TAU, y, y + 0.4f, 16, ends: false);
                Sector(glow, m, rad * 0.7f, rad, 0, TAU, top, top + 0.05f, 16, inner: false, outer: false, ends: false);
                if (i < 7) smokeTops.Add(Polar(a, r, top + 0.5f));
            }
            for (int i = 0; i < 26; i++)
            {
                float a = Rnd() * TAU, r = Rnd(140, 230), w = Rnd(6, 14), top = Rnd(15, 60); var m = TR(Polar(a, r, 0), Rnd(0, 90));
                Box(city, m, new Vector3(w, top + 34, w), new Vector3(0, (top - 34) / 2, 0));
                Box(city, m, new Vector3(w * 0.6f, 8, w * 0.6f), new Vector3(0, top + 4, 0));
                if (Rnd() < 0.5f) Lathe(iron, m * Matrix4x4.Rotate(Quaternion.Euler(0, 45, 0)), new[] { new Vector2(w * 0.45f, top + 8), new Vector2(0, top + 8 + w) }, 4);
            }
            smokeStacks = smokeTops;
            // cage lanterns hanging on chains from far above, behind the arena
            foreach (var p in new[] { new Vector3(-23, 6, -9), new Vector3(23, 5, -8), new Vector3(-11, 8, -25), new Vector3(12, 7, -25) })
            {
                Chain(p + Vector3.up * 0.9f, p + Vector3.up * 40, 0);
                Lantern(Child(root, "Chains/HangingLanterns"), p, 1f, true);
            }
        }
        static List<Vector3> smokeStacks = new List<Vector3>();

        // Cage lantern: iron frame, glowing core, pyramid cap and spike. Shared meshes, separate objects so they can sway.
        static void BuildLanternMeshes()
        {
            var gi = new Geo(); var gg = new Geo();
            foreach (var (x, z) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) }) Box(gi, Matrix4x4.identity, new Vector3(0.12f, 1.5f, 0.12f), new Vector3(x * 0.5f, 0, z * 0.5f));
            foreach (float y in new[] { -0.8f, 0.8f }) Box(gi, Matrix4x4.identity, new Vector3(1.3f, 0.14f, 1.3f), new Vector3(0, y, 0), true);
            for (int i = 0; i < 4; i++) { var q = Quaternion.Euler(0, i * 90, 0); Box(gi, TRQ(Vector3.zero, q), new Vector3(0.06f, 1.5f, 0.06f), new Vector3(0, 0, 0.55f)); Box(gi, TRQ(Vector3.zero, q), new Vector3(1.0f, 0.06f, 0.06f), new Vector3(0, 0.2f, 0.55f)); }
            Lathe(gi, Matrix4x4.Rotate(Quaternion.Euler(0, 45, 0)), new[] { new Vector2(0.95f, 0.87f), new Vector2(0, 1.5f) }, 4);
            Lathe(gi, Matrix4x4.Rotate(Quaternion.Euler(0, 45, 0)), new[] { new Vector2(0, -1.45f), new Vector2(0.5f, -0.87f) }, 4);
            Link(gi, Matrix4x4.Translate(new Vector3(0, 1.9f, 0)));
            Box(gg, Matrix4x4.identity, new Vector3(0.85f, 1.35f, 0.85f));
            Mesh Make(Geo g, string name) { var m = new Mesh { name = name }; m.SetVertices(g.V); m.SetNormals(g.N); m.SetUVs(0, g.U); m.SetTriangles(g.T, 0); m.RecalculateBounds(); m.RecalculateTangents(); return SaveAsset(m, "Meshes/" + name + ".asset"); }
            lanternIron = Make(gi, "LanternIron"); lanternGlow = Make(gg, "LanternGlow");
        }

        static void Lantern(Transform parent, Vector3 pos, float scale, bool swing)
        {
            var go = new GameObject("CageLantern"); go.transform.SetParent(parent, false); go.transform.position = pos; go.transform.localScale = Vector3.one * scale;
            foreach (var (mesh, mat) in new[] { (lanternIron, mIron), (lanternGlow, mGlow) })
            {
                var c = new GameObject(mesh.name); c.transform.SetParent(go.transform, false);
                c.AddComponent<MeshFilter>().sharedMesh = mesh; var mr = c.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
                mr.shadowCastingMode = mat == mGlow ? ShadowCastingMode.Off : ShadowCastingMode.On;
            }
            if (swing) swingers.Add(go.transform);
            if (scale >= 0.4f) { var l = new GameObject("Lantern Light").AddComponent<Light>(); l.transform.SetParent(go.transform, false); l.type = LightType.Point; l.color = Hex(0xFFA040); l.intensity = 3f * scale; l.range = 5f * scale + 1; l.shadows = LightShadows.None; }
        }

        // ================================================================== lighting, sky, post, camera ==================================================================
        static Camera BuildLightingAndCamera()
        {
            var sky = new Material(Shader.Find("Skybox/Panoramic")) { name = "FoundrySky" };
            sky.SetTexture("_MainTex", SaveTex("SkyPanorama", 1024, 512, PaintSky(1024, 512), clamp: false)); sky.SetFloat("_Mapping", 1); sky.SetFloat("_Exposure", 1f);
            RenderSettings.skybox = SaveAsset(sky, "Materials/FoundrySky.mat");
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = Hex(0x3A2C26);
            RenderSettings.fogStartDistance = 35; RenderSettings.fogEndDistance = 160;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex(0x4A4448, 1.0f); RenderSettings.ambientEquatorColor = Hex(0x3A2218, 0.9f); RenderSettings.ambientGroundColor = Hex(0xFF6A1A, 0.4f);

            var lights = Child(root, "Lighting");
            var key = new GameObject("Key Light").AddComponent<Light>(); key.transform.SetParent(lights, false);
            key.type = LightType.Directional; key.color = Hex(0xE9DED0); key.intensity = 1.15f; key.shadows = LightShadows.Soft; key.shadowStrength = 0.8f;
            key.transform.rotation = Quaternion.Euler(60, -20, 0); RenderSettings.sun = key;
            var cool = new GameObject("Cool Fill").AddComponent<Light>(); cool.transform.SetParent(lights, false);
            cool.type = LightType.Directional; cool.color = Hex(0x8FA0C0); cool.intensity = 0.35f; cool.shadows = LightShadows.None; cool.transform.rotation = Quaternion.Euler(45, 160, 0);
            var up = new GameObject("Furnace Uplight").AddComponent<Light>(); up.transform.SetParent(lights, false);
            up.type = LightType.Directional; up.color = Hex(0xFF6A1A); up.intensity = 0.35f; up.shadows = LightShadows.None; up.transform.rotation = Quaternion.Euler(-60, 30, 0);

            var volume = new GameObject("Post").AddComponent<Volume>(); volume.transform.SetParent(lights, false); volume.isGlobal = true;
            string path = Gen + "/BrassFoundryPost.asset"; AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, path);
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true); bloom.threshold.Override(1f); bloom.intensity.Override(0.6f); bloom.scatter.Override(0.6f);
            var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.25f); vig.color.Override(Hex(0x140806));
            var ca = profile.Add<ColorAdjustments>(true); ca.contrast.Override(10f); ca.saturation.Override(-5f); ca.postExposure.Override(0.25f);
            foreach (var c in profile.components) { c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy; AssetDatabase.AddObjectToAsset(c, profile); }
            AssetDatabase.SaveAssets(); volume.sharedProfile = profile;

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>(); cam.nearClipPlane = 0.1f; cam.farClipPlane = 900; cam.clearFlags = CameraClearFlags.Skybox;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>(); data.renderPostProcessing = true; data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.AddComponent<AudioListener>();
            return cam;
        }

        static void BuildFX()
        {
            var fx = Child(root, "FX");
            ParticleSystem Make(string name, Material mat, Vector3 pos, Quaternion rot)
            {
                var go = new GameObject(name); go.transform.SetParent(fx, false); go.transform.SetPositionAndRotation(pos, rot);
                var ps = go.AddComponent<ParticleSystem>(); go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat; return ps;
            }
            // embers rising off the molten ring
            var em = Make("Embers", mEmber, new Vector3(0, -1.3f, 0), Quaternion.Euler(-90, 0, 0));
            var main = em.main; main.startLifetime = new ParticleSystem.MinMaxCurve(4, 8); main.startSpeed = 0; main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.13f);
            main.maxParticles = 400; main.simulationSpace = ParticleSystemSimulationSpace.World; main.prewarm = true; main.loop = true;
            var e = em.emission; e.rateOverTime = 60;
            var sh = em.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 17.7f; sh.radiusThickness = 0.06f;
            var vel = em.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World; vel.x = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f); vel.y = new ParticleSystem.MinMaxCurve(0.7f, 1.8f); vel.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
            var noise = em.noise; noise.enabled = true; noise.strength = 0.4f; noise.frequency = 0.4f;
            var col = em.colorOverLifetime; col.enabled = true; var grad = new Gradient(); grad.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(1, 0.6f, 0.3f), 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.15f), new GradientAlphaKey(0, 1) }); col.color = grad;
            // heat haze smoke over the channel
            var hz = Make("ChannelHaze", mSmoke, new Vector3(0, -1.2f, 0), Quaternion.Euler(-90, 0, 0));
            var hm = hz.main; hm.startLifetime = 9; hm.startSpeed = 0; hm.startSize = new ParticleSystem.MinMaxCurve(3, 6); hm.maxParticles = 120; hm.simulationSpace = ParticleSystemSimulationSpace.World; hm.prewarm = true;
            var he = hz.emission; he.rateOverTime = 18; var hs = hz.shape; hs.shapeType = ParticleSystemShapeType.Circle; hs.radius = 18.2f; hs.radiusThickness = 0.12f;
            var hv = hz.velocityOverLifetime; hv.enabled = true; hv.space = ParticleSystemSimulationSpace.World; hv.x = new ParticleSystem.MinMaxCurve(0, 0); hv.y = new ParticleSystem.MinMaxCurve(0.4f, 0.9f); hv.z = new ParticleSystem.MinMaxCurve(0, 0);
            var hc = hz.colorOverLifetime; hc.enabled = true; var g2 = new Gradient(); g2.SetKeys(new[] { new GradientColorKey(new Color(0.6f, 0.3f, 0.15f), 0), new GradientColorKey(new Color(0.25f, 0.18f, 0.15f), 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.5f, 0.3f), new GradientAlphaKey(0, 1) }); hc.color = g2;
            // chimney smoke columns in the city below
            foreach (var p in smokeStacks)
            {
                var s = Make("ChimneySmoke", mSmoke, p, Quaternion.Euler(-90, 0, 0));
                var sm = s.main; sm.startLifetime = 14; sm.startSpeed = 2.5f; sm.startSize = new ParticleSystem.MinMaxCurve(4, 9); sm.maxParticles = 60; sm.simulationSpace = ParticleSystemSimulationSpace.World; sm.prewarm = true;
                var se = s.emission; se.rateOverTime = 3; var ss = s.shape; ss.shapeType = ParticleSystemShapeType.Cone; ss.angle = 8; ss.radius = 1;
                var sz = s.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 0.6f, 1, 2.2f));
                var sc = s.colorOverLifetime; sc.enabled = true; sc.color = g2;
            }
        }

        static void BuildGameplay()
        {
            var gp = Child(root, "Gameplay"); var wall = Child(gp, "Boundary");
            for (int i = 0; i < 48; i++)
            {
                float a = (i + 0.5f) / 48 * TAU; var go = new GameObject("BoundaryWall"); go.transform.SetParent(wall, false);
                go.transform.SetPositionAndRotation(Polar(a, 14.35f, 1.5f), Quaternion.LookRotation(Polar(a, 1)));
                go.AddComponent<BoxCollider>().size = new Vector3(2 * 14.35f * Mathf.Tan(PI / 48) + 0.05f, 3, 0.3f);
            }
            new GameObject("BossSpawn").transform.SetParent(gp, false);
            for (int i = 0; i < 8; i++)
            {
                var s = new GameObject("PlayerSpawn_" + (i + 1)).transform; s.SetParent(gp, false);
                float a = PI / 2 + i / 8f * TAU; s.position = Polar(a, 10); s.rotation = Quaternion.LookRotation(-Polar(a, 1));
            }
        }

        // ================================================================== batch: build, check, capture ==================================================================
        // Unity.exe -batchmode -projectPath unity -executeMethod LastBell.Areas.Area2.EditorTools.BrassFoundryArenaBuilder.BatchBuildAndCapture [-shots <dir>]
        public static void BatchBuildAndCapture()
        {
            int code = 0;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                Build(); EditorSceneManager.OpenScene(ScenePath);
                root = GameObject.Find("LB_ENV_BrassFoundry_Arena").transform;
                var args = System.Environment.GetCommandLineArgs(); int si = System.Array.IndexOf(args, "-shots");
                string shots = si >= 0 && si + 1 < args.Length ? args[si + 1] : Path.GetFullPath(Path.Combine(Application.dataPath, "../../prototype/arena-3d/areas/area-2/shots"));
                Directory.CreateDirectory(shots);
                var driver = Object.FindFirstObjectByType<BrassFoundryArena>();
                foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) ps.Simulate(8, true, true);
                var report = new System.Text.StringBuilder();
                report.AppendLine(CheckOcclusion());
                report.AppendLine(Stats());
                File.WriteAllText(Path.Combine(shots, "unity-report.txt"), report.ToString());
                Debug.Log(report.ToString());
                driver.SetView(BrassFoundryArena.View.Gameplay); Capture(Path.Combine(shots, "unity-game.png"));
                Capture(Path.Combine(shots, "unity-game.png"));
                driver.SetView(BrassFoundryArena.View.TopDown); Capture(Path.Combine(shots, "unity-top.png"), 1600, 1600);
                driver.SetView(BrassFoundryArena.View.Hero); Capture(Path.Combine(shots, "unity-hero.png"));
                // telegraph test: a coral-red disc on the floor (not saved into the scene)
                var tele = new GameObject("TelegraphTest"); tele.transform.position = new Vector3(5.5f, 0.02f, -2.5f);
                var tg = new Geo(); Sector(tg, Matrix4x4.identity, 0, 4, 0, TAU, 0, 0, 96, inner: false, outer: false, ends: false);
                var eg = new Geo(); Sector(eg, Matrix4x4.identity, 3.85f, 4, 0, TAU, 0, 0.005f, 96, inner: false, outer: false, ends: false);
                foreach (var (g, c) in new[] { (tg, new Color(1, 0.29f, 0.12f, 0.35f)), (eg, new Color(1, 0.42f, 0.23f, 0.95f)) })
                {
                    var mesh = new Mesh(); mesh.SetVertices(g.V); mesh.SetNormals(g.N); mesh.SetUVs(0, g.U); mesh.SetTriangles(g.T, 0);
                    var go = new GameObject("Disc"); go.transform.SetParent(tele.transform, false); go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); mat.SetColor("_BaseColor", c);
                    mat.SetFloat("_Surface", 1); mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); mat.SetFloat("_ZWrite", 0); mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); mat.renderQueue = 3000;
                    go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                }
                driver.SetView(BrassFoundryArena.View.Gameplay); Capture(Path.Combine(shots, "unity-game-tele.png"));
                Object.DestroyImmediate(tele);
            }
            catch (System.Exception e) { Debug.LogException(e); code = 1; }
            EditorApplication.Exit(code);
        }

        // Raycast from the gameplay camera to 64 points on r = 13.7 (30 cm inside the 0.29 m edge curb, which by its height hides the last few cm
        // at an oblique angle) and 64 on r = 10; anything but the floor in the way is a failure.
        static string CheckOcclusion()
        {
            var temp = new List<Collider>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.GetComponent<Collider>() || !mf.sharedMesh) continue;
                var mr = mf.GetComponent<MeshRenderer>(); if (mr && mr.sharedMaterial && mr.sharedMaterial.shader.name.Contains("Particles")) continue;
                var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; temp.Add(mc);
            }
            Physics.SyncTransforms();
            Vector3 cam = new Vector3(0, 29, 22.5f); var hits = new List<string>();
            foreach (float r in new[] { 13.7f, 10f }) for (int i = 0; i < 64; i++)
            {
                float a = i / 64f * TAU; Vector3 p = Polar(a, r, 0.001f), d = p - cam;
                foreach (var h in Physics.RaycastAll(cam, d.normalized, d.magnitude - 0.05f))
                {
                    if (h.collider.transform.parent && h.collider.transform.parent.name == "Boundary") continue;
                    if (h.collider.gameObject.name == "CombatFloor") continue;
                    hits.Add($"r{r} a{a * Mathf.Rad2Deg:0} blocked by {HierarchyPath(h.collider.transform)}"); break;
                }
            }
            foreach (var c in temp) Object.DestroyImmediate(c);
            return $"Occlusion check (gameplay camera, 128 floor points): {hits.Count} blocked" + (hits.Count > 0 ? "\n  " + string.Join("\n  ", hits) : "");
        }

        static string HierarchyPath(Transform t) => t.parent ? HierarchyPath(t.parent) + "/" + t.name : t.name;

        static string Stats()
        {
            var rs = root.GetComponentsInChildren<MeshRenderer>(); long tris = 0;
            foreach (var r in rs) { var mf = r.GetComponent<MeshFilter>(); if (mf && mf.sharedMesh) for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) tris += mf.sharedMesh.GetIndexCount(s) / 3; }
            int lights = root.GetComponentsInChildren<Light>().Length, mats = rs.SelectMany(r => r.sharedMaterials).Distinct().Count();
            return $"Stats: {rs.Length} mesh renderers, {mats} materials, {tris:N0} triangles, {lights} lights";
        }

        public static void Capture(string file, int w = 1920, int h = 1080)
        {
            var cam = Camera.main; cam.aspect = (float)w / h;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
            Debug.Log("Saved " + file);
        }
    }
}
