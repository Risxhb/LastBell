using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LastBell.Areas.Area2.EditorTools
{
    // Last Bell > Area 2 – Brass Foundry: builds (once) and opens the Brass Foundry arena scene, matching the top-down
    // reference prototype/arena-3d/areas/area-2/ref-brass-foundry-topdown.webp (briefs: BRASS-FOUNDRY-UNITY-PROMPT.md and
    // BRASS-FOUNDRY-DETAIL-PASS-PROMPT.md). Everything is generated from code: meshes (combined per group and material,
    // saved under Generated/Meshes), textures (PNG under Generated/Textures), URP materials, sky, fog, lights, post and FX.
    // The molten channel, falls, rivers and pools use Shaders/MoltenFlow.shader, driven by BrassFoundryArena's clock.
    // Image top = -Z (Furnace Gate), bottom = +Z (Entry Bridge, toward the gameplay camera), right = +X.
    // The playable floor is the disc r <= 14 at y = 0. The run loop (Run/Editor/RunBuild.cs) calls Build().
    public static class BrassFoundryArenaBuilder
    {
        const string Dir = "Assets/LastBell/Areas/Area2", Gen = Dir + "/Generated", ScenePath = Dir + "/BrassFoundry_Arena.unity";
        const float R = 14f, PI = Mathf.PI, TAU = Mathf.PI * 2;
        static readonly Matrix4x4 I = Matrix4x4.identity;

        [MenuItem("Last Bell/Area 2 – Brass Foundry", priority = 20)]
        static void Open()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) Build();
            EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Last Bell/Rebuild Area 2 – Brass Foundry", priority = 21)]
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
        static readonly List<Light> flickerLights = new List<Light>();
        static readonly List<Vector3> steamVents = new List<Vector3>(), smokeStacks = new List<Vector3>();
        static readonly Dictionary<string, float> texelDensity = new Dictionary<string, float>();
        static string moltenCoverage = "";
        static System.Random rng;
        static float Rnd() => (float)rng.NextDouble();
        static float Rnd(float a, float b) => a + (b - a) * Rnd();

        // materials
        static Material mGapMolten, mStoneSolid, mCityRoof, mFloor, mSlate, mRimStone, mMasonry, mMasonryDark, mCity, mIron, mPlank, mBrass, mDarkBrass, mChain, mRoof, mAbyss;
        static Material mWindowWarm, mWindowHot, mFurnace, mHalo, mMolten, mRiver, mPool, mFall, mGlow, mGlowDim, mGlowCurtain, mUnderGlow, mEmber, mSpark, mHaze, mSmoke, mSteam;
        static Mesh lanternIron, lanternGlow;

        public static void Build()
        {
            foreach (var f in new[] { "Generated", "Generated/Meshes", "Generated/Textures", "Generated/Materials" })
            {
                var parts = (Dir + "/" + f).Split('/');
                for (int i = 1; i < parts.Length; i++) { string p = string.Join("/", parts.Take(i + 1)), parent = string.Join("/", parts.Take(i)); if (!AssetDatabase.IsValidFolder(p)) AssetDatabase.CreateFolder(parent, parts[i]); }
            }
            batches.Clear(); moltenMats.Clear(); glowMats.Clear(); swingers.Clear(); flickerLights.Clear(); steamVents.Clear(); smokeStacks.Clear(); texelDensity.Clear();
            rng = new System.Random(7); bevel = 0;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            root = new GameObject("LB_ENV_BrassFoundry_Arena").transform;

            MakeMaterials();
            LoadModels();
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
            so.FindProperty("curtainMaterial").objectReferenceValue = mGlowCurtain;
            SetArray(so.FindProperty("moltenMaterials"), moltenMats.Cast<Object>().ToList());
            SetArray(so.FindProperty("glowMaterials"), glowMats.Cast<Object>().ToList());
            SetArray(so.FindProperty("flickerLights"), flickerLights.Cast<Object>().ToList());
            SetArray(so.FindProperty("swingers"), swingers.Cast<Object>().ToList());
            so.ApplyModifiedPropertiesWithoutUndo();
            driver.SetView(BrassFoundryArena.View.Gameplay);
            BrassFoundryArena.SetTime(0);

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
        static Matrix4x4 T(float x, float y, float z) => Matrix4x4.Translate(new Vector3(x, y, z));
        static Vector3 Polar(float a, float r, float y = 0) => new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
        // yaw (degrees) that turns local +X to point along angle a (radians, measured from +X toward +Z)
        static float YawOut(float a) => -a * Mathf.Rad2Deg;
        // local +Z radial (outward), +X tangential
        static Matrix4x4 Radial(float a, float y = 0) => TRQ(new Vector3(0, y, 0), Quaternion.LookRotation(Polar(a, 1), Vector3.up));

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

        // Chamfer applied by Box() while building parts that should catch edge highlights (0 = sharp boxes). Thin pieces
        // get a proportionally smaller chamfer (trims ~2–3 cm); anything under 1.5 cm stays sharp.
        static float bevel;

        static void BevelBox(Geo g, Matrix4x4 m, Vector3 size, Vector3 c, float b)
        {
            Vector3 h = size * 0.5f, i = h - Vector3.one * b;
            Vector3 V(int ax, float va, int bx, float vb, int kx, float vk) { var v = Vector3.zero; v[ax] = va; v[bx] = vb; v[kx] = vk; return c + v; }
            Vector2 UV(Vector3 p, Vector3 n) { var a = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z)); return a.y >= a.x && a.y >= a.z ? new Vector2(p.x, p.z) : a.x >= a.z ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y); }
            void Q(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 n) => Quad(g, m, p0, p1, p2, p3, n, UV(p0, n), UV(p1, n), UV(p2, n), UV(p3, n));
            for (int a = 0; a < 3; a++) { int u = (a + 1) % 3, w = (a + 2) % 3; foreach (float s in new[] { -1f, 1f }) { var n = Vector3.zero; n[a] = s; Q(V(a, s * h[a], u, -i[u], w, -i[w]), V(a, s * h[a], u, i[u], w, -i[w]), V(a, s * h[a], u, i[u], w, i[w]), V(a, s * h[a], u, -i[u], w, i[w]), n); } }
            for (int a = 0; a < 3; a++)
            {
                int bb = (a + 1) % 3, k = (a + 2) % 3;
                foreach (float sa in new[] { -1f, 1f }) foreach (float sb in new[] { -1f, 1f })
                { var n = Vector3.zero; n[a] = sa; n[bb] = sb; n.Normalize(); Q(V(a, sa * h[a], bb, sb * i[bb], k, -i[k]), V(a, sa * h[a], bb, sb * i[bb], k, i[k]), V(a, sa * i[a], bb, sb * h[bb], k, i[k]), V(a, sa * i[a], bb, sb * h[bb], k, -i[k]), n); }
            }
            foreach (float sx in new[] { -1f, 1f }) foreach (float sy in new[] { -1f, 1f }) foreach (float sz in new[] { -1f, 1f })
            {
                var n = new Vector3(sx, sy, sz).normalized; Vector3 p0 = c + new Vector3(sx * h.x, sy * i.y, sz * i.z), p1 = c + new Vector3(sx * i.x, sy * h.y, sz * i.z), p2 = c + new Vector3(sx * i.x, sy * i.y, sz * h.z);
                Tri(g, m, p0, p1, p2, n, n, n, UV(p0, n), UV(p1, n), UV(p2, n));
            }
        }

        // Axis-aligned box (local), UVs in metres so tiled textures keep their scale.
        static void Box(Geo g, Matrix4x4 m, Vector3 size, Vector3 c = default, bool bottom = false)
        {
            if (bevel > 0) { float b = Mathf.Min(bevel, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.3f); if (b >= 0.015f) { BevelBox(g, m, size, c, b); return; } }
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

        // Surface of revolution around local Y. Profile runs bottom → top as (radius, y); per-band normals, smooth around.
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

        // Cylinder from A to B (world), e.g. pipes.
        static void Pipe(Geo g, Vector3 A, Vector3 B, float r, int seg = 10)
        {
            Vector3 d = B - A; float len = d.magnitude; if (len < 1e-4f) return;
            Lathe(g, TRQ(A, Quaternion.FromToRotation(Vector3.up, d / len)), new[] { new Vector2(r, 0), new Vector2(r, len) }, seg);
        }

        // Annular sector between radii r0..r1 and angles a0..a1 (radians from +X toward +Z), from y0 to y1.
        // UVs: top u = arc length (a·r), v = r; sides u = arc length, v = y. All in metres.
        static void Sector(Geo g, Matrix4x4 m, float r0, float r1, float a0, float a1, float y0, float y1, int seg = 0, bool top = true, bool inner = true, bool outer = true, bool ends = true, bool bottom = false)
        {
            if (seg <= 0) seg = Mathf.Max(1, Mathf.CeilToInt((a1 - a0) * r1 / 0.4f));
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

        // Prism from a star-shaped polygon (x, z) about its centroid, from y0 to y1, with an optional top chamfer.
        static void Poly(Geo g, Matrix4x4 m, Vector2[] pts, float y0, float y1, bool sides = true, float bev = 0)
        {
            Vector2 c = Vector2.zero; foreach (var p in pts) c += p; c /= pts.Length;
            var top = pts.Select(p => { float d = (p - c).magnitude; return bev > 0 && d > 1e-4f ? c + (p - c) * Mathf.Max(0, 1 - bev / d) : p; }).ToArray();
            float ys = bev > 0 ? y1 - bev * 0.6f : y1;
            Vector3 C = new Vector3(c.x, y1, c.y);
            for (int i = 0; i < pts.Length; i++)
            {
                Vector2 p = pts[i], q = pts[(i + 1) % pts.Length], tp = top[i], tq = top[(i + 1) % pts.Length];
                Tri(g, m, C, new Vector3(tp.x, y1, tp.y), new Vector3(tq.x, y1, tq.y), Vector3.up, Vector3.up, Vector3.up, c, tp, tq);
                if (!sides) continue;
                Vector2 e = q - p; Vector3 n = new Vector3(e.y, 0, -e.x).normalized; Vector2 mid = (p + q) / 2 - c; if (n.x * mid.x + n.z * mid.y < 0) n = -n;
                float l = e.magnitude;
                Quad(g, m, new Vector3(p.x, y0, p.y), new Vector3(q.x, y0, q.y), new Vector3(q.x, ys, q.y), new Vector3(p.x, ys, p.y), n, new Vector2(0, y0), new Vector2(l, y0), new Vector2(l, ys), new Vector2(0, ys));
                if (bev > 0) Quad(g, m, new Vector3(p.x, ys, p.y), new Vector3(q.x, ys, q.y), new Vector3(tq.x, y1, tq.y), new Vector3(tp.x, y1, tp.y), (n + Vector3.up).normalized, p, q, tq, tp);
            }
        }

        // Oriented thin bar from A to B (world), cross-section w × h, its "up" kept as close to world up as possible.
        static void Bar(Geo g, Vector3 A, Vector3 B, float w, float h)
        {
            Vector3 d = B - A; float len = d.magnitude; if (len < 1e-4f) return;
            var q = Quaternion.LookRotation(d / len, Mathf.Abs(Vector3.Dot(d / len, Vector3.up)) > 0.98f ? Vector3.forward : Vector3.up);
            Box(g, TRQ((A + B) / 2, q), new Vector3(w, h, len));
        }

        // One chain link: a torus in the local XY plane, stretched along Y. 12 × 8 segments.
        static void Link(Geo g, Matrix4x4 m)
        {
            const int U = 12, V = 8; const float Rm = 0.34f, rt = 0.11f, sy = 1.45f;
            Vector3 P(int i, int j, out Vector3 n)
            {
                float u = TAU * i / U, v = TAU * j / V; var dir = new Vector3(Mathf.Cos(u), Mathf.Sin(u), 0);
                n = (dir * Mathf.Cos(v) + Vector3.forward * Mathf.Sin(v)).normalized;
                return new Vector3(Mathf.Cos(u) * Rm, Mathf.Sin(u) * Rm * sy, 0) + n * rt;
            }
            for (int i = 0; i < U; i++) for (int j = 0; j < V; j++)
            {
                var a = P(i, j, out var na); var b = P(i + 1, j, out var nb); var c = P(i + 1, j + 1, out var nc); var d = P(i, j + 1, out var nd);
                Tri(g, m, a, b, c, na, nb, nc, Vector2.zero, Vector2.right, Vector2.one); Tri(g, m, a, c, d, na, nc, nd, Vector2.zero, Vector2.one, Vector2.up);
            }
        }

        // ================================================================== Blender models ==================================================================
        // Hand-modelled assets from prototype/arena-3d/areas/area-2/blender/area2_models.py, exported one .glb per asset into
        // Models/ (git-ignored; Last Bell > Area 2 – Build Models (Blender) regenerates them). Each asset's geometry is
        // appended into the same per-group, per-material batches as the code geometry, with its material slots mapped by
        // name onto this builder's URP materials. Without the models the builder falls back to the code-built versions.
        const string ModelDir = Dir + "/Models";
        sealed class ModelPart { public Vector3[] v, n; public Vector2[] uv; public int[][] tris; public string[] mats; public Matrix4x4 local; }
        static readonly Dictionary<string, List<ModelPart>> models = new Dictionary<string, List<ModelPart>>();
        static bool Modelled => models.Count > 0;

        static string BlenderExe => System.Environment.GetEnvironmentVariable("LASTBELL_BLENDER") ?? @"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe";

        [MenuItem("Last Bell/Area 2 – Build Models (Blender)", priority = 22)]
        public static void BuildModels()
        {
            string script = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../prototype/arena-3d/areas/area-2/blender/area2_models.py"));
            string outDir = System.IO.Path.GetFullPath(ModelDir);
            if (!File.Exists(BlenderExe)) { Debug.LogWarning("Area 2: Blender not found at " + BlenderExe + " (set LASTBELL_BLENDER)"); return; }
            var psi = new System.Diagnostics.ProcessStartInfo(BlenderExe, $"-b --factory-startup -P \"{script}\" -- \"{outDir}\"") { UseShellExecute = false, CreateNoWindow = true };
            using (var p = System.Diagnostics.Process.Start(psi)) p.WaitForExit();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        static void LoadModels()
        {
            models.Clear();
            if (!Directory.Exists(ModelDir) && File.Exists(BlenderExe)) BuildModels();
            if (!Directory.Exists(ModelDir)) { Debug.LogWarning("Area 2: no Blender models in " + ModelDir + "; using the code-built geometry. Run Last Bell > Area 2 – Build Models (Blender)."); return; }
            foreach (var file in Directory.GetFiles(ModelDir, "*.glb"))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(file.Replace('\\', '/')); if (!go) continue;
                var parts = new List<ModelPart>();
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh = mf.sharedMesh; var mr = mf.GetComponent<MeshRenderer>(); if (!mesh || !mr) continue;
                    parts.Add(new ModelPart
                    {
                        v = mesh.vertices, n = mesh.normals, uv = mesh.uv,
                        tris = Enumerable.Range(0, mesh.subMeshCount).Select(s => mesh.GetTriangles(s)).ToArray(),
                        mats = mr.sharedMaterials.Select(m => m ? m.name : "").ToArray(),
                        local = go.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix,
                    });
                }
                if (parts.Count > 0) models[System.IO.Path.GetFileNameWithoutExtension(file)] = parts;
            }
            Debug.Log($"Area 2: {models.Count} Blender models loaded");
        }

        static Material MatFor(string slot)
        {
            switch (slot.Split(' ', '.')[0])
            {
                case "Stone": return mStoneSolid; case "Masonry": return mMasonry; case "MasonryDark": return mMasonryDark;
                case "Iron": return mIron; case "Brass": return mBrass; case "DarkBrass": return mDarkBrass; case "Plank": return mPlank;
                case "Roof": return mRoof; case "Chain": return mChain; case "City": return mCity; case "CityRoof": return mCityRoof;
                case "Slate": return mSlate; case "Glow": return mGlow; case "GlowDim": return mGlowDim; case "GlowWarm": return mWindowWarm;
                case "GlowHot": return mWindowHot; case "Furnace": return mFurnace; default: return mMasonryDark;
            }
        }

        // Appends a model's geometry, transformed by m, into the batches of `group`. False when the model isn't loaded.
        static bool Model(string group, string name, Matrix4x4 m)
        {
            if (!models.TryGetValue(name, out var parts)) return false;
            foreach (var p in parts)
            {
                var M = m * p.local; var N = M.inverse.transpose; bool flip = M.determinant < 0;
                for (int s = 0; s < p.tris.Length; s++)
                {
                    var g = G(group, MatFor(s < p.mats.Length ? p.mats[s] : "")); var map = new Dictionary<int, int>(); var tris = p.tris[s];
                    for (int k = 0; k < tris.Length; k++)
                    {
                        int t = tris[flip ? k - k % 3 + (2 - k % 3) : k];
                        if (!map.TryGetValue(t, out int ni))
                        {
                            ni = g.V.Count; map[t] = ni;
                            g.V.Add(M.MultiplyPoint3x4(p.v[t])); g.N.Add(N.MultiplyVector(p.n.Length > t ? p.n[t] : Vector3.up).normalized); g.U.Add(p.uv.Length > t ? p.uv[t] : Vector2.zero);
                        }
                        g.T.Add(ni);
                    }
                }
            }
            return true;
        }

        // A standalone mesh (submesh per material slot) from a model, for objects that move on their own (lanterns).
        static Mesh ModelMesh(string name, out Material[] mats)
        {
            mats = null; if (!models.TryGetValue(name, out var parts)) return null;
            batchesBackup = new Dictionary<string, Dictionary<Material, Geo>>(batches); batches.Clear();
            Model("__single", name, I);
            var d = batches["__single"]; batches.Clear(); foreach (var kv in batchesBackup) batches[kv.Key] = kv.Value;
            var mesh = new Mesh { name = name }; var V = new List<Vector3>(); var Nn = new List<Vector3>(); var U = new List<Vector2>(); var subs = new List<List<int>>();
            foreach (var kv in d) { int o = V.Count; V.AddRange(kv.Value.V); Nn.AddRange(kv.Value.N); U.AddRange(kv.Value.U); subs.Add(kv.Value.T.Select(t => t + o).ToList()); }
            mesh.SetVertices(V); mesh.SetNormals(Nn); mesh.SetUVs(0, U); mesh.subMeshCount = subs.Count;
            for (int s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s], s);
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            mats = d.Keys.ToArray();
            return SaveAsset(mesh, "Meshes/" + name + ".asset");
        }
        static Dictionary<string, Dictionary<Material, Geo>> batchesBackup;

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
                if (!Model("Chains", "SM_Area2_ChainLink", TRQ(S(t), q))) Link(g, TRQ(S(t), q));
            }
        }

        // Groups whose meshes don't cast shadows onto the floor (towers would throw long shadows across it).
        static readonly HashSet<string> NoCast = new HashSet<string> { "Chains", "Towers", "Surroundings" };

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
                    bool glow = kv.Key.shader.name.Contains("Unlit") || kv.Key.shader.name.Contains("Molten");
                    mr.shadowCastingMode = glow || NoCast.Contains(group.Key) ? ShadowCastingMode.Off : ShadowCastingMode.On; mr.receiveShadows = !glow;
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
        static Color32 Grey(float v) { byte b = (byte)Mathf.Clamp(v * 255, 0, 255); return new Color32(b, b, b, 255); }

        static Texture2D SaveTex(string name, int w, int h, Color32[] px, bool heightToNormal = false, float heightScale = 0.1f, bool clamp = false, bool alpha = false, bool linear = false, bool keepAlpha = false, int maxSize = 0)
        {
            string path = Gen + "/Textures/" + name + ".png";
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false); tex.SetPixels32(px); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat; imp.anisoLevel = 8; imp.maxTextureSize = maxSize > 0 ? maxSize : Mathf.Max(w, h); imp.mipmapEnabled = true;
            imp.alphaIsTransparency = alpha; imp.sRGBTexture = !linear;
            imp.alphaSource = alpha || keepAlpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None; imp.textureCompression = TextureImporterCompression.Compressed;
            if (heightToNormal) { imp.textureType = TextureImporterType.NormalMap; imp.convertToNormalmap = true; imp.heightmapScale = heightScale; imp.normalmapFilter = TextureImporterNormalFilter.Standard; }
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Combat floor (4096): cool charcoal slate slabs cut by concentric seams and radial seams, worn centre and footpaths
        // to the four entries, soot at the rim, sparse low-contrast cracks, engraved tick rings around the medallion. No glow.
        // Also writes a half-resolution AO map (dark in the seams) and a metallic-gloss map (A = smoothness variation).
        static void PaintFloor(int N, out Color32[] col, out Color32[] hgt, out Color32[] ao, out Color32[] gloss)
        {
            int M = N / 2; col = new Color32[N * N]; hgt = new Color32[N * N]; ao = new Color32[M * M]; gloss = new Color32[M * M];
            float[] rings = { 2.7f, 7f, 11.2f, 13.3f };
            const float LX = 0.34f, LZ = -0.94f;
            for (int py = 0; py < N; py++)
            {
                float z = (py + 0.5f) / N * 2 * R - R;
                for (int px = 0; px < N; px++)
                {
                    float x = (px + 0.5f) / N * 2 * R - R, r = Mathf.Sqrt(x * x + z * z); int o = py * N + px; bool half = (px & 1) == 0 && (py & 1) == 0; int oh = (py >> 1) * M + (px >> 1);
                    if (r > R + 0.05f) { col[o] = C32(40, 42, 46); hgt[o] = Grey(0.63f); if (half) { ao[oh] = Grey(1); gloss[oh] = C32(13, 0, 0, 40); } continue; }
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
                    // shallow worn footpaths along the four entry axes
                    float dAxis = Mathf.Min(Mathf.Abs(x), Mathf.Abs(z)) + (Fbm(x * 0.5f + 11, z * 0.5f) - 0.5f) * 0.9f;
                    float path = Smooth(1.5f, 0.3f, dAxis) * Smooth(3.5f, 6f, r); cr *= 1 + 0.07f * path; cg *= 1 + 0.07f * path; cb *= 1 + 0.06f * path;
                    float soot = Smooth(10.5f, 13.8f, r) * Smooth(0.45f, 0.7f, Fbm(x * 0.25f + 11, z * 0.25f)); cr *= 1 - 0.22f * soot; cg *= 1 - 0.22f * soot; cb *= 1 - 0.2f * soot;
                    float h = 0.7f + (n2 - 0.5f) * 0.12f, smooth = 0.16f + (n2 - 0.5f) * 0.06f + 0.08f * path + 0.05f * wear - 0.05f * soot, occl = 1;
                    if (VNoise(x * 2.7f, z * 2.7f + 4) < 0.06f) { cr *= 0.94f; cg *= 0.94f; cb *= 0.95f; h -= 0.03f; smooth -= 0.04f; occl = 0.9f; }
                    if (edge < 0.07f) { float t = 1 - Smooth(0.012f, 0.07f, edge), f = 1 + (nx * LX + nz * LZ) * 0.22f * t; cr *= f; cg *= f; cb *= f; }
                    h = Mathf.Lerp(0.3f, h, Smooth(0.012f, 0.07f, edge));
                    occl *= Mathf.Lerp(0.55f, 1, Smooth(0.0f, 0.09f, edge));
                    if (edge < 0.014f) { cr = 25; cg = 26; cb = 29; h = 0.12f; smooth = 0.08f; }
                    col[o] = C32(cr, cg, cb); hgt[o] = Grey(h);
                    if (half) { ao[oh] = Grey(occl); gloss[oh] = C32(13, 0, 0, Mathf.Clamp01(smooth) * 255); }
                }
            }
            var cc = col; var hh = hgt; float S = N / (2 * R);
            void Dot(float x, float z, float rad, float k, byte hv)
            {
                int cx = Mathf.RoundToInt((x + R) * S), cz = Mathf.RoundToInt((z + R) * S), ri = Mathf.CeilToInt(rad);
                for (int dy = -ri; dy <= ri; dy++) for (int dx = -ri; dx <= ri; dx++)
                {
                    if (dx * dx + dy * dy > rad * rad) continue; int X = cx + dx, Y = cz + dy; if (X < 0 || Y < 0 || X >= N || Y >= N) continue;
                    int o = Y * N + X; var c = cc[o]; cc[o] = C32(c.r * k, c.g * k, c.b * k); hh[o] = new Color32(hv, hv, hv, 255);
                }
            }
            void Line(Vector2 A, Vector2 B, float rad, float k, byte hv) { int steps = Mathf.CeilToInt(Vector2.Distance(A, B) * S * 1.5f) + 1; for (int i = 0; i <= steps; i++) { var p = Vector2.Lerp(A, B, (float)i / steps); Dot(p.x, p.y, rad, k, hv); } }
            var rr2 = new System.Random(19);
            for (int i = 0; i < 28; i++) // sparse hairline cracks, low contrast
            {
                float rad = 3 + (float)rr2.NextDouble() * 10, a = (float)rr2.NextDouble() * TAU, d = (float)rr2.NextDouble() * TAU; var p = new Vector2(Mathf.Cos(a) * rad, Mathf.Sin(a) * rad);
                int n = 4 + rr2.Next(6);
                for (int s = 0; s < n; s++) { d += ((float)rr2.NextDouble() - 0.5f) * 1.3f; var q = p + new Vector2(Mathf.Cos(d), Mathf.Sin(d)) * 0.22f; Line(p, q, 1.6f, 0.84f, 110); p = q; }
            }
            Vector2 U(float a, float r) => new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            for (int i = 0; i < 64; i++) { float a = i / 64f * TAU, l = i % 8 == 0 ? 0.35f : 0.16f; Line(U(a, 2.95f), U(a, 2.95f + l), i % 8 == 0 ? 2.8f : 1.8f, 1.3f, 90); }   // engraved ticks
            for (int i = 0; i < 120; i++) { float a = i / 120f * TAU, l = i % 10 == 0 ? 0.22f : 0.1f; Line(U(a, 3.47f), U(a, 3.47f + l), 1.4f, 1.22f, 95); }        // fine tick ring
            for (int i = 0; i < 360; i++) Line(U(i / 360f * TAU, 3.43f), U((i + 1) / 360f * TAU, 3.43f), 1.2f, 1.18f, 95);                                           // engraved circle
        }

        // Tileable masonry: blockW × blockH blocks in running bond over a square tile of `metres`, with chipped,
        // worn (lighter) edges and soot. Variant A = large blocks, variant B = coursed bricks.
        static void PaintMasonry(int N, float metres, float blockW, float blockH, int seed, out Color32[] col, out Color32[] hgt)
        {
            col = new Color32[N * N]; hgt = new Color32[N * N]; float S = N / metres; int P = Mathf.RoundToInt(metres * 4);
            for (int py = 0; py < N; py++) for (int px = 0; px < N; px++)
            {
                float X = px / S, Y = py / S; int row = Mathf.FloorToInt(Y / blockH); float off = (row % 2) * blockW * 0.5f, xx = (X + off) % metres; int bx = Mathf.FloorToInt(xx / blockW);
                float fx = xx - bx * blockW, fy = Y - row * blockH, edge = Mathf.Min(Mathf.Min(fx, blockW - fx), Mathf.Min(fy, blockH - fy));
                float id = Hash(bx + row * 7 + seed, 3), n = Fbm(X * 4, Y * 4, P * 4), fine = VNoise(X * 40, Y * 40, P * 40), chip = VNoise(X * 9 + id * 13, Y * 9, P * 9);
                float v = (id - 0.5f) * 0.22f + (n - 0.5f) * 0.28f + (fine - 0.5f) * 0.08f, warm = Hash(bx, row + 11 + seed) * 0.08f;
                float cr = 66 * (1 + v + warm), cg = 61 * (1 + v), cb = 58 * (1 + v - warm * 0.5f), h = 0.75f + (n - 0.5f) * 0.2f;
                float ed = edge - (chip < 0.25f ? 0.03f : 0);
                if (ed < 0.07f) { float t = 1 - Smooth(0.012f, 0.07f, ed), wearK = 1 + 0.28f * t * Smooth(0.35f, 0.7f, chip); cr *= wearK; cg *= wearK; cb *= wearK * 0.97f; h = Mathf.Lerp(h, 0.4f, t * 0.8f); }
                if (ed < 0.014f) { cr = 28; cg = 25; cb = 24; h = 0.1f; }
                if (fine > 0.85f) { cr *= 1.1f; cg *= 1.1f; cb *= 1.08f; }
                float sootK = 1 - 0.25f * Smooth(0.55f, 0.8f, Fbm(X * 1.2f + 5, Y * 0.6f, P)); cr *= sootK; cg *= sootK; cb *= sootK;
                int o = py * N + px; col[o] = C32(cr, cg, cb); hgt[o] = Grey(h);
            }
        }

        // Tileable solid stone (4 m, no block grid) for the single-piece Blender rim teeth: mottled value, pitting, fine
        // speckle, faint strata and a few hairline cracks.
        static void PaintStone(int N, out Color32[] col, out Color32[] hgt)
        {
            col = new Color32[N * N]; hgt = new Color32[N * N]; const int P = 16;
            for (int py = 0; py < N; py++) for (int px = 0; px < N; px++)
            {
                float X = px * 4f / N, Y = py * 4f / N;
                float n = Fbm(X * 1.2f, Y * 1.2f, 5), m = Fbm(X * 4, Y * 4, P), fine = VNoise(X * 50, Y * 50, 200), pit = VNoise(X * 9 + 3, Y * 9, 36), strata = VNoise(X * 0.6f, Y * 6, 24);
                float crack = Mathf.Abs(Fbm(X * 1.6f + 7, Y * 1.6f, 6) - 0.5f);
                float v = (n - 0.5f) * 0.32f + (m - 0.5f) * 0.14f + (fine - 0.5f) * 0.1f + (strata - 0.5f) * 0.06f;
                float cr = 70 * (1 + v), cg = 66 * (1 + v), cb = 63 * (1 + v), h = 0.6f + (m - 0.5f) * 0.3f + (n - 0.5f) * 0.2f;
                if (pit < 0.0f) { float k = Smooth(0.1f, 0.04f, pit); cr *= 1 - 0.2f * k; cg *= 1 - 0.2f * k; cb *= 1 - 0.2f * k; h -= 0.18f * k; }
                if (crack < 0.0f) { float k = 1 - crack / 0.006f; cr *= 1 - 0.3f * k; cg *= 1 - 0.3f * k; cb *= 1 - 0.3f * k; h -= 0.15f * k; }
                if (fine > 0.86f) { cr *= 1.1f; cg *= 1.09f; cb *= 1.07f; }
                int o = py * N + px; col[o] = C32(cr, cg, cb); hgt[o] = Grey(h);
            }
        }

        // Tileable riveted iron plate (2 m square, 1 m plates) with rust blooms and rust streaks running down from rivets.
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
                // streaks below the top rivet row of each plate
                float rx = Mathf.Round(fx * 8) / 8, dx = Mathf.Abs(fx - rx), col8 = Hash(Mathf.RoundToInt(X * 8), Mathf.FloorToInt(Y) + 3);
                if (col8 > 0.45f && dx < 0.016f && fy > 0.3f && fy < 0.93f) { float k = Smooth(0.3f, 0.93f, fy) * (1 - dx / 0.016f) * 0.6f; cr = Mathf.Lerp(cr, 104, k); cg = Mathf.Lerp(cg, 60, k); cb = Mathf.Lerp(cb, 36, k); }
                if (edge < 0.03f) { float t = 1 - Smooth(0.006f, 0.03f, edge); cr *= 1 - 0.3f * t; cg *= 1 - 0.3f * t; cb *= 1 - 0.3f * t; h = Mathf.Lerp(h, 0.3f, t); }
                float ry = fy < 0.5f ? 0.04f : 0.96f, rxx = fx < 0.5f ? 0.04f : 0.96f;
                float dRow = Mathf.Sqrt(Mathf.Pow((fx * 8 % 1 - 0.5f) / 8, 2) + Mathf.Pow(fy - ry, 2)), dCol = Mathf.Sqrt(Mathf.Pow(fx - rxx, 2) + Mathf.Pow((fy * 8 % 1 - 0.5f) / 8, 2));
                float rd = Mathf.Min(dRow, dCol);
                if (rd < 0.018f) { float k = 1.35f - rd * 25; cr *= k; cg *= k; cb *= k; h = 0.95f - rd * 10; }
                int o = py * N + px; col[o] = C32(cr, cg, cb); hgt[o] = Grey(h);
            }
        }

        // Deck planks: the square texture spans 2 m along the deck (U) and 6 m across it (V); 0.25 m boards run along U with
        // staggered butt joints and bolts. The worn centre path sits at V = 0 (the deck centre line).
        static void PaintPlanks(int N, out Color32[] col, out Color32[] hgt)
        {
            col = new Color32[N * N]; hgt = new Color32[N * N];
            for (int py = 0; py < N; py++) for (int px = 0; px < N; px++)
            {
                float X = px * 2f / N, Y = py * 6f / N; int row = Mathf.FloorToInt(Y / 0.25f); float off = Hash(row, 21) * 2f, xx = (X + off) % 2f;
                int seg = Mathf.FloorToInt(xx); float fx = xx - seg, fy = Y - row * 0.25f, edge = Mathf.Min(Mathf.Min(fx, 1 - fx) * 0.5f, Mathf.Min(fy, 0.25f - fy));
                float grain = Fbm(X * 2, Y * 13.3f, 4), n = Fbm(X * 3, Y, 6), id = Hash(row * 13 + seg, 4), v = (id - 0.5f) * 0.18f + (n - 0.5f) * 0.2f + (grain - 0.5f) * 0.16f;
                float cr = 56 * (1 + v), cg = 50 * (1 + v), cb = 46 * (1 + v), h = 0.7f + (grain - 0.5f) * 0.12f;
                float dCentre = Mathf.Min(Y, 6 - Y), worn = Smooth(0.9f, 0.2f, dCentre + (n - 0.5f) * 0.5f);
                cr *= 1 + 0.18f * worn; cg *= 1 + 0.16f * worn; cb *= 1 + 0.14f * worn;
                if (edge < 0.02f) { float t = 1 - Smooth(0.004f, 0.02f, edge); cr *= 1 - 0.45f * t; cg *= 1 - 0.45f * t; cb *= 1 - 0.45f * t; h = Mathf.Lerp(h, 0.2f, t); }
                float bx = Mathf.Min(fx, 1 - fx), bd = Mathf.Sqrt(bx * bx * 400 + Mathf.Pow((fy - 0.125f) * 20, 2));
                if (bx < 0.06f && bd < 0.9f) { cr = 120; cg = 98; cb = 66; h = 0.95f; }
                int o = py * N + px; col[o] = C32(cr, cg, cb); hgt[o] = Grey(h);
            }
        }

        // Brass (tileable, 4 m): soft wear variation and fine scratches, with dark green-brown patina collecting in blotches.
        static Color32[] PaintBrass(int N)
        {
            var col = new Color32[N * N];
            for (int py = 0; py < N; py++) for (int px = 0; px < N; px++)
            {
                float X = px * 4f / N, Y = py * 4f / N, n = Fbm(X, Y, 4), pat = Smooth(0.6f, 0.82f, Fbm(X * 1.7f + 3, Y * 1.7f, 7)), scratch = VNoise(X * 2, Y * 60, 240), k = 0.86f + 0.2f * n + (scratch > 0.8f ? 0.08f : 0);
                col[py * N + px] = C32(Mathf.Lerp(255 * k, 96, pat * 0.65f), Mathf.Lerp(255 * k, 104, pat * 0.65f), Mathf.Lerp(255 * k, 80, pat * 0.65f));
            }
            return col;
        }

        // Molten noise (tileable, 1024): R = crust plates from periodic cell noise (0 on the cracks between cells, rising
        // into each plate), G = heat (fbm). Also measures how much of the surface the shader's default threshold crusts.
        static Color32[] PaintMoltenNoise(int N)
        {
            const int C = 5; var pts = new Vector2[C * C];
            for (int j = 0; j < C; j++) for (int i = 0; i < C; i++) pts[j * C + i] = new Vector2(i + 0.15f + 0.7f * Hash(i, j * 3 + 1), j + 0.15f + 0.7f * Hash(i * 7 + 2, j));
            var col = new Color32[N * N]; int crusted = 0;
            for (int py = 0; py < N; py++) for (int px = 0; px < N; px++)
            {
                float u = (px + 0.5f) / N * C, v = (py + 0.5f) / N * C; int ci = Mathf.FloorToInt(u), cj = Mathf.FloorToInt(v); float f1 = 9, f2 = 9;
                for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++)
                {
                    int ii = ci + di, jj = cj + dj, wi = Wrap(ii, C), wj = Wrap(jj, C);
                    Vector2 p = pts[wj * C + wi] + new Vector2(ii - wi, jj - wj); float d = Vector2.Distance(p, new Vector2(u, v));
                    if (d < f1) { f2 = f1; f1 = d; } else if (d < f2) f2 = d;
                }
                float plates = Mathf.Clamp01((f2 - f1) * 1.6f) * (0.78f + 0.22f * Fbm(u * 2, v * 2, C * 2));
                float heat = Fbm(u * 2 + 3, v * 2, C * 2) * 0.7f + VNoise(u * 6, v * 6, C * 6) * 0.3f;
                if (plates > 0.39f) crusted++;
                col[py * N + px] = C32(plates * 255, heat * 255, 0);
            }
            moltenCoverage = $"Molten crust coverage at the channel's default threshold: {100f * crusted / (N * N):0}% (target 45–55%)";
            return col;
        }

        static Color32[] PaintSky(int W, int H)
        {
            var col = new Color32[W * H];
            Color top = Hex(0x141010), mid = Hex(0x2A2220), hor = Hex(0x5A3426), glowC = Hex(0x8A4422), below = Hex(0x36302D), deep = Hex(0x1A1412);
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
            {
                float e = ((y + 0.5f) / H - 0.5f) * PI, u = (float)x / W; Color c;
                if (e > 0) { c = Color.Lerp(hor, mid, Smooth(0, 0.25f, e)); c = Color.Lerp(c, top, Smooth(0.25f, 1.0f, e)); }
                else { c = Color.Lerp(hor, below, Smooth(0, 0.12f, -e)); c = Color.Lerp(c, deep, Smooth(0.15f, 1.2f, -e)); }
                c = Color.Lerp(c, glowC, Smooth(0.09f, 0f, Mathf.Abs(e)) * (0.5f + 0.5f * Fbm(u * 18, 3, 18)));       // horizon glow
                float bank = Fbm(u * 10, (e + 2) * 3, 10), bank2 = Fbm(u * 26 + 4, (e + 2) * 7, 26);
                c *= 1 - 0.4f * Smooth(0.42f, 0.72f, bank) * Smooth(-0.05f, 0.45f, e) - 0.15f * Smooth(0.5f, 0.8f, bank2) * Smooth(0.0f, 0.3f, e);
                col[y * W + x] = c;
            }
            return col;
        }

        static Color32[] PaintDetail(int N)
        {
            var col = new Color32[N * N];
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float u = x * 16f / N, v = y * 16f / N, h = 0.6f + (VNoise(u, v, 16) - 0.5f) * 0.25f + (VNoise(u * 4, v * 4, 64) - 0.5f) * 0.15f;
                if (VNoise(u * 2 + 7, v * 2, 32) < 0.12f) h -= 0.2f;
                col[y * N + x] = Grey(h);
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
        static Texture2D detailNormal, detailGrey;

        static Material Lit(string name, Texture2D albedo, Texture2D normal, Color tint, float metallic, float smooth, float tileMetres, float bump = 1f, bool detail = false)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, enableInstancing = true };
            if (albedo) m.SetTexture("_BaseMap", albedo);
            m.SetColor("_BaseColor", tint); m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", smooth);
            if (normal) { m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", bump); m.EnableKeyword("_NORMALMAP"); }
            if (tileMetres > 0) m.mainTextureScale = Vector2.one / tileMetres;
            if (detail)
            {
                m.SetTexture("_DetailAlbedoMap", detailGrey); m.SetTexture("_DetailNormalMap", detailNormal); m.SetFloat("_DetailNormalMapScale", 0.6f); m.SetFloat("_DetailAlbedoMapScale", 1f);
                m.SetTextureScale("_DetailAlbedoMap", Vector2.one * 2); m.EnableKeyword("_DETAIL_MULX2");
            }
            if (albedo && tileMetres > 0) texelDensity[name] = albedo.width / tileMetres;
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

        static Material Molten(string name, Texture2D noise, float speed, float axis, Vector2 across, float scale, float crust, float bank, float undulate, float emission, float bubbles = 1, float streaks = 0, float lip = 1000)
        {
            var m = new Material(Shader.Find("LastBell/Area2/MoltenFlow")) { name = name, enableInstancing = true };
            m.SetTexture("_NoiseTex", noise); m.SetFloat("_Speed", speed); m.SetFloat("_FlowAxis", axis); m.SetVector("_Across", new Vector4(across.x, across.y, 0, 0));
            m.SetFloat("_Scale", scale); m.SetFloat("_Crust", crust); m.SetFloat("_Bank", bank); m.SetFloat("_Undulate", undulate); m.SetFloat("_Emission", emission);
            m.SetFloat("_Bubbles", bubbles); m.SetFloat("_Streaks", streaks); m.SetFloat("_Lip", lip);
            moltenMats.Add(m);
            return SaveAsset(m, "Materials/" + name + ".mat");
        }

        static void MakeMaterials()
        {
            PaintFloor(4096, out var fc, out var fh, out var fao, out var fgl);
            var floorA = SaveTex("FloorSlate", 4096, 4096, fc); var floorN = SaveTex("FloorSlateHeight", 4096, 4096, fh, true, 0.06f, maxSize: 2048); // painted at 4096, imported at 2048 to stay in the texture budget
            var floorAO = SaveTex("FloorSlateAO", 2048, 2048, fao, linear: true); var floorGloss = SaveTex("FloorSlateMetallicGloss", 2048, 2048, fgl, linear: true, keepAlpha: true);
            PaintMasonry(2048, 3, 1.5f, 0.75f, 0, out var ac, out var ah);
            var masA = SaveTex("MasonryLarge", 2048, 2048, ac); var masAN = SaveTex("MasonryLargeHeight", 2048, 2048, ah, true, 0.1f);
            PaintMasonry(2048, 2, 1.0f, 0.5f, 31, out var bc, out var bh);
            var masB = SaveTex("MasonryCoursed", 2048, 2048, bc); var masBN = SaveTex("MasonryCoursedHeight", 2048, 2048, bh, true, 0.12f);
            PaintStone(1024, out var sc, out var sh);
            mStoneSolid = Lit("StoneSolid", SaveTex("StoneSolid", 1024, 1024, sc), SaveTex("StoneSolidHeight", 1024, 1024, sh, true, 0.05f), Hex(0x948A82), 0.05f, 0.26f, 4, 0.35f);
            PaintIron(1024, out var ic, out var ih);
            var ironA = SaveTex("IronPlate", 1024, 1024, ic); var ironN = SaveTex("IronPlateHeight", 1024, 1024, ih, true, 0.1f);
            PaintPlanks(1024, out var pc, out var ph);
            var plankA = SaveTex("DeckPlanks", 1024, 1024, pc); var plankN = SaveTex("DeckPlanksHeight", 1024, 1024, ph, true, 0.1f);
            var brassA = SaveTex("BrassWear", 512, 512, PaintBrass(512));
            var moltenT = SaveTex("MoltenNoise", 1024, 1024, PaintMoltenNoise(1024), linear: true);
            var dot = SaveTex("SoftDot", 64, 64, SoftDot(64), clamp: true, alpha: true);
            var fade = SaveTex("VerticalFade", 4, 64, VerticalFade(64), clamp: true, alpha: true);
            detailNormal = SaveTex("DetailPitting", 512, 512, PaintDetail(512), true, 0.05f);
            detailGrey = SaveTex("DetailNeutral", 4, 4, Enumerable.Repeat(new Color32(128, 128, 128, 255), 16).ToArray());

            mFloor = Lit("FoundryFloor", floorA, floorN, Hex(0xD6D4D2), 0.05f, 1f, 0, 1f);
            mFloor.SetTexture("_OcclusionMap", floorAO); mFloor.SetFloat("_OcclusionStrength", 1f); mFloor.EnableKeyword("_OCCLUSIONMAP");
            mFloor.SetTexture("_MetallicGlossMap", floorGloss); mFloor.SetFloat("_SmoothnessTextureChannel", 0); mFloor.EnableKeyword("_METALLICSPECGLOSSMAP");
            texelDensity["FoundryFloor"] = 4096 / (2 * R);
            mSlate = Lit("SlateInset", null, null, Hex(0x34363A), 0.05f, 0.2f, 0);
            mRimStone = Lit("RimStone", masA, masAN, Hex(0x74726F), 0.05f, 0.2f, 6f, 0.5f, true);
            mMasonry = Lit("Masonry", masB, masBN, Hex(0xD8D2CE), 0.0f, 0.12f, 2, 1f, true);
            mMasonryDark = Lit("MasonryDark", masB, masBN, Hex(0x6E6A68), 0.0f, 0.1f, 2, 1f, true);
            mCity = Lit("CityMasonry", masB, masBN, Hex(0x403834), 0.0f, 0.05f, 4);
            mIron = Lit("IronPlate", ironA, ironN, Color.white, 0.55f, 0.38f, 2, 1f, true);
            mPlank = Lit("DeckPlanks", plankA, plankN, Color.white, 0.2f, 0.22f, 0, 1f, true); mPlank.mainTextureScale = new Vector2(0.5f, 1 / 6f);
            texelDensity["DeckPlanks"] = Mathf.Min(1024 / 2f, 1024 / 6f);
            mBrass = Lit("Brass", brassA, null, Hex(0x7A5A2C), 0.85f, 0.5f, 4);
            mDarkBrass = Lit("DarkBrass", brassA, null, Hex(0x4E3820), 0.8f, 0.34f, 4);
            mChain = Lit("ChainIron", null, null, Hex(0x2C2724), 0.75f, 0.42f, 0);
            mRoof = Lit("RoofIron", ironA, ironN, Hex(0x4A4442), 0.5f, 0.25f, 2, 1f, true);
            mAbyss = Lit("AbyssGround", null, null, Hex(0x0C0806), 0f, 0f, 0);
            mCityRoof = Lit("CityRoof", null, null, Hex(0x221C1A), 0.3f, 0.2f, 0);
            // flowing molten brass (Shaders/MoltenFlow.shader)
            mMolten = Molten("MoltenChannel", moltenT, 0.35f, 0, new Vector2(16.75f, 18.65f), 3.0f, 0.5f, 0.3f, 0.03f, 2.2f); mMolten.SetFloat("_CorePeak", 3.0f); mMolten.SetColor("_HotColor", Hex(0xFFB060));
            mGapMolten = Molten("MoltenGap", moltenT, 0.15f, 0, new Vector2(14.95f, 16.8f), 2.3f, 0.45f, 0.12f, 0f, 2.0f, 0.6f); mGapMolten.SetFloat("_CorePeak", 3.2f);
            mRiver = Molten("MoltenRiver", moltenT, 0.1f, 1, new Vector2(0, 2f), 3f, 0.6f, 0.05f, 0f, 0.7f); mRiver.SetFloat("_CorePeak", 2.4f);
            mPool = Molten("MoltenPool", moltenT, 0.05f, 0, new Vector2(0, 7f), 2.5f, 0.6f, 0.4f, 0f, 0.8f, 0.5f); mPool.SetFloat("_CorePeak", 2.6f);
            mFall = Molten("MoltenFall", moltenT, -3f, 1, new Vector2(0, 1.2f), 2.5f, 0.2f, 0.15f, 0f, 3f, 0, 1, 30.4f);
            mGlow = Unlit("GlowWindow", Hex(0xFF7A22, 1.2f)); glowMats.Add(mGlow);
            mWindowWarm = Unlit("GlowWindowWarm", Hex(0xE8501A, 0.85f)); glowMats.Add(mWindowWarm);
            mWindowHot = Unlit("GlowWindowHot", Hex(0xFFA040, 1.5f)); glowMats.Add(mWindowHot);
            mGlowDim = Unlit("GlowGate", Hex(0xFF5A18, 0.32f)); glowMats.Add(mGlowDim);
            var furnace = new Color32[8 * 128];
            for (int y = 0; y < 128; y++) { float t = y / 127f; Color c = Color.Lerp(Hex(0xFFB060), Hex(0xC8461A), Smooth(0, 0.45f, t)); c = Color.Lerp(c, Hex(0x2A0A04), Smooth(0.4f, 1f, t)); for (int x = 0; x < 8; x++) furnace[y * 8 + x] = c; }
            mFurnace = Unlit("FurnaceInterior", Hex(0xFFFFFF, 1.1f), SaveTex("FurnaceGradient", 8, 128, furnace, clamp: true)); mFurnace.mainTextureScale = new Vector2(1, 1 / 4.7f); glowMats.Add(mFurnace);
            mHalo = Particle("FurnaceHalo", dot, Hex(0xFF6A20) * new Color(1, 1, 1, 0.3f), true);
            mGlowCurtain = Particle("GlowCurtain", fade, Hex(0xFF7A26, 1.0f) * new Color(1, 1, 1, 0.5f), true);
            mGlowCurtain.mainTextureScale = new Vector2(1, 1 / 2.2f); mGlowCurtain.mainTextureOffset = new Vector2(0, 1.4f / 2.2f);
            mUnderGlow = Particle("UnderDrumGlow", dot, Hex(0xFF6A20) * new Color(1, 1, 1, 0.16f), true);
            mEmber = Particle("Ember", dot, Hex(0xFF8A3A, 2f), true);
            mSpark = Particle("Spark", dot, Hex(0xFFA050, 2.6f), true);
            mHaze = Particle("HeatHaze", dot, new Color(0.45f, 0.22f, 0.12f, 0.14f), false);
            mSmoke = Particle("Smoke", dot, new Color(0.16f, 0.12f, 0.1f, 0.3f), false);
            mSteam = Particle("Steam", dot, new Color(0.55f, 0.5f, 0.47f, 0.28f), false);
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
            // the playable disc: one mesh (16 rings × 256 segments) with planar UVs over the 4096 slate texture
            const int SEG = 256, RINGS = 16; var mesh = new Mesh { name = "CombatFloor", indexFormat = IndexFormat.UInt32 };
            var v = new List<Vector3> { Vector3.zero }; var uv = new List<Vector2> { new Vector2(0.5f, 0.5f) }; var t = new List<int>();
            for (int ring = 1; ring <= RINGS; ring++) for (int i = 0; i < SEG; i++)
            {
                float a = TAU * i / SEG, r = R * ring / RINGS; var p = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                v.Add(p); uv.Add(new Vector2((p.x + R) / (2 * R), (p.z + R) / (2 * R)));
            }
            for (int i = 0; i < SEG; i++) { t.Add(0); t.Add(1 + (i + 1) % SEG); t.Add(1 + i); }
            for (int ring = 1; ring < RINGS; ring++) for (int i = 0; i < SEG; i++)
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

        // Gold cross, spear tips, end plates, diagonal plates, compass medallion, brass edge strip. Raised 1–3 cm, with a
        // 0.8 cm chamfer on the brass shapes.
        static void BuildInlays()
        {
            var brass = G("CombatFloor/Inlays", mBrass); var dark = G("CombatFloor/Inlays", mSlate); var iron = G("CombatFloor/Inlays", mDarkBrass);
            const float H = 0.012f, B = 0.008f;
            Sector(brass, I, 13.88f, 13.98f, 0, TAU, 0, 0.01f, 256, ends: false);
            for (int k = 0; k < 4; k++)
            {
                float a = k * PI / 2; var m = TR(Vector3.zero, YawOut(a)); bool ew = k % 2 == 0; // local +X points outward along the arm
                Box(brass, m, new Vector3(10.4f - 2.75f, H, 0.09f), new Vector3((2.75f + 10.4f) / 2, H / 2, 0));
                Box(brass, m, new Vector3(13.2f - 12.55f, H, 0.09f), new Vector3((12.55f + 13.2f) / 2, H / 2, 0));
                float w = ew ? 0.95f : 0.75f;
                Poly(brass, m, new[] { new Vector2(10.3f, 0), new Vector2(10.95f, w / 2), new Vector2(12.65f, 0), new Vector2(10.95f, -w / 2) }, 0, H, true, B);
                Poly(dark, m, new[] { new Vector2(10.6f, 0), new Vector2(11.0f, w * 0.28f), new Vector2(12.2f, 0), new Vector2(11.0f, -w * 0.28f) }, 0, H + 0.004f, false);
                foreach (float s in new[] { -1f, 1f }) Poly(brass, m, new[] { new Vector2(10.75f, s * w * 0.42f), new Vector2(11.15f, s * w * 0.42f), new Vector2(10.85f, s * (w * 0.42f + 0.38f)), new Vector2(10.55f, s * (w * 0.42f + 0.3f)) }, 0, H, true, B * 0.6f);
                if (ew) { Box(brass, m, new Vector3(0.12f, H, 1.7f), new Vector3(10.55f, H / 2, 0)); foreach (float s in new[] { -1f, 1f }) Box(brass, m, new Vector3(0.36f, H, 0.12f), new Vector3(10.7f, H / 2, s * 0.85f)); }
                Box(brass, m, new Vector3(0.5f, H, 0.8f), new Vector3(12.95f, H / 2, 0));
                Box(dark, m, new Vector3(0.32f, H + 0.004f, 0.6f), new Vector3(12.95f, (H + 0.004f) / 2, 0));
                // diagonal plate at 45° off this arm: a trapezoid facing the centre, double brass outline
                var md = TR(Vector3.zero, YawOut(a + PI / 4));
                Vector2[] Trap(float inset) => new[] { new Vector2(10.9f + inset, -0.95f + inset), new Vector2(12.4f - inset, -1.4f + inset * 1.1f), new Vector2(12.4f - inset, 1.4f - inset * 1.1f), new Vector2(10.9f + inset, 0.95f - inset) };
                Poly(brass, md, Trap(0), 0, 0.01f, true, B); Poly(dark, md, Trap(0.09f), 0, 0.013f, false); Poly(brass, md, Trap(0.2f), 0, 0.015f, false); Poly(dark, md, Trap(0.27f), 0, 0.018f, false);
                Box(brass, md, new Vector3(0.05f, 0.02f, 1.5f), new Vector3(11.65f, 0.01f, 0)); // engraved centre line
            }
            // compass medallion: two brass rings, 8-point star, dark centre with a brass rim, tick marks
            Sector(brass, I, 2.55f, 2.7f, 0, TAU, 0, 0.025f, 256, ends: false);
            Sector(brass, I, 1.9f, 2.0f, 0, TAU, 0, 0.02f, 256, ends: false);
            Sector(dark, I, 2.0f, 2.55f, 0, TAU, 0, 0.008f, 256, ends: false, inner: false, outer: false);
            for (int i = 0; i < 32; i++) { float a = i / 32f * TAU; Box(brass, TR(Vector3.zero, YawOut(a)), new Vector3(i % 4 == 0 ? 0.42f : 0.22f, 0.02f, 0.05f), new Vector3(i % 4 == 0 ? 2.25f : 2.33f, 0.01f, 0)); }
            var star = new List<Vector2>();
            for (int i = 0; i < 16; i++) { float a = i / 16f * TAU, r = i % 2 == 1 ? 0.42f : (i % 4 == 0 ? 1.8f : 1.1f); star.Add(new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r)); }
            Poly(brass, I, star.ToArray(), 0, 0.03f, true, B);
            Poly(iron, I, star.Select((p, i) => p * (i % 2 == 1 ? 0.6f : 0.55f)).ToArray(), 0, 0.034f, false);
            Sector(brass, I, 0.28f, 0.36f, 0, TAU, 0, 0.04f, 64, ends: false);
            Lathe(dark, I, new[] { new Vector2(0.28f, 0), new Vector2(0.28f, 0.036f), new Vector2(0, 0.036f) }, 48);
        }

        // Furnace rim: inner curb, rim base, and a single ring of separate gear-tooth blocks (big and small alternating,
        // 0.4 m gaps so the molten glow shows between them, heights ±15%), with brass edge trims, rivets, chain anchors
        // and small gauge and valve props.
        static void BuildRim()
        {
            bevel = 0.07f;
            var stone = G("FurnaceRim", mRimStone); var stoneD = G("FurnaceRim", mMasonryDark); var brass = G("FurnaceRim", mBrass); var dbrass = G("FurnaceRim", mDarkBrass);
            var iron = G("FurnaceRim", mIron); var chainG = G("FurnaceRim", mChain); var dark = G("FurnaceRim", mSlate);
            foreach (var (a0, a1) in Arcs(14.2f, 0.1f))
            {
                Sector(stone, I, 14.0f, 14.4f, a0, a1, 0, 0.25f);                          // inner curb
                Lathe(brass, I, new[] { new Vector2(13.98f, 0.25f), new Vector2(14.01f, 0.285f), new Vector2(14.08f, 0.305f), new Vector2(14.15f, 0.285f), new Vector2(14.18f, 0.25f) }, Mathf.CeilToInt((a1 - a0) * 14.1f / 0.3f), a0, a1); // half-round brass curb
                for (float a = a0 + 0.04f; a < a1; a += 1.0f / 14.1f) Box(iron, Radial(a), new Vector3(0.05f, 0.03f, 0.05f), new Vector3(0, 0.305f, 14.08f));
                Sector(stoneD, I, 14.4f, 16.8f, a0, a1, -1.7f, 0.02f, inner: false);       // rim base; its outer face is the channel's inner wall
                for (float a = a0 + 0.08f; a < a1; a += 0.62f / 16.8f * 2.2f)              // corbels down the channel wall
                    Box(stoneD, TR(Polar(a, 16.95f, -0.75f), YawOut(a)), new Vector3(0.35f, 0.5f, 0.45f));
            }
            int n = 0;
            foreach (var (a0, a1) in Arcs(15.5f, 0.35f))
            {
                if (Modelled) // one regular crown of identical Blender teeth, evenly spaced with lava in every gap
                {
                    float arc = (a1 - a0) * 15.5f; int cnt = Mathf.Max(1, Mathf.RoundToInt(arc / 2.75f));
                    for (int j = 0; j < cnt; j++, n++)
                    {
                        Model("FurnaceRim", "SM_Area2_RimTooth_Big_" + "ABC"[n % 3], Radial(a0 + (a1 - a0) * (j + 0.5f) / cnt) * T(0, 0, 14.45f));
                        // molten slot in the gap after this tooth, so every gap glows from above (outside the walkable floor)
                        float ga = a0 + (a1 - a0) * (j + 1f) / cnt, hw = 0.42f / 15.8f;
                        if (j < cnt - 1) Sector(G("FurnaceRim", mGapMolten), I, 14.95f, 16.8f, ga - hw, ga + hw, 0.035f, 0.035f, 3, inner: false, outer: false, ends: false);
                    }
                    continue;
                }
                const float BIG = 2.3f, SMALL = 1.3f, GAP = 1.0f;
                float L = (a1 - a0) * 15.5f, unit = BIG + SMALL + 2 * GAP; int pairs = Mathf.Max(1, Mathf.RoundToInt(L / unit)); float k = L / (pairs * unit), s = 0;
                for (int i = 0; i < pairs * 2; i++, n++)
                {
                    bool big = i % 2 == 0; float nominal = big ? BIG : SMALL, w = nominal * k;
                    float mid = a0 + (s + GAP * k / 2 + w / 2) / 15.5f; s += (nominal + GAP) * k;
                    float rIn = big ? 14.45f : 14.6f, depth = big ? 2.55f : 1.7f, h = (big ? 1.5f : 0.9f) * Rnd(0.88f, 1.12f), rc = rIn + depth / 2;
                    var m = Radial(mid);   // local +X tangential, +Z outward
                    if (Model("FurnaceRim", "SM_Area2_RimTooth_" + (big ? "Big_" : "Small_") + "ABC"[(n / 2 + (big ? 0 : 1)) % 3],
                        m * Matrix4x4.TRS(new Vector3(0, 0, rIn), Quaternion.identity, new Vector3(w / (big ? 2.3f : 1.3f), h / (big ? 1.5f : 0.9f), 1)))) continue;
                    { float kb = bevel; bevel = 0.14f; Box(stone, m, new Vector3(w, h, depth), new Vector3(0, h / 2, rc)); bevel = kb; }  // heavy chamfer, no lid
                    for (int r = 0; r < 4; r++) Box(G("FurnaceRim", mIron), m, new Vector3(0.07f, 0.07f, 0.03f), new Vector3(-w * 0.3f + r * w * 0.2f, h * 0.35f, rIn - 0.015f)); // low rivet row
                    if (big)
                    {
                        foreach (float sx in new[] { -1f, 1f }) Box(brass, m, new Vector3(0.14f, 0.16f, 0.14f), new Vector3(sx * (w / 2 - 0.05f), h - 0.06f, rIn + 0.05f)); // corner caps
                        for (int r = 0; r < 5; r++) Box(iron, m, new Vector3(0.07f, 0.07f, 0.03f), new Vector3(-w * 0.36f + r * w * 0.18f, h - 0.24f, rIn - 0.015f)); // rivet row
                        if ((n / 2) % 3 == 0) Box(stone, m, new Vector3(w * 0.55f, 0.6f, 1.0f), new Vector3(0, h + 0.22f + 0.3f, rIn + depth - 0.75f)); // stacked block
                    }
                    else if (n % 4 == 1) // pressure gauge facing the arena
                    {
                        Cyl(iron, m * T(0, h, rc - 0.3f), 0.06f, 0, 0.32f, 10);
                        var gm = m * T(0, h + 0.42f, rc - 0.3f) * Matrix4x4.Rotate(Quaternion.Euler(-90, 0, 0));
                        Lathe(brass, gm, new[] { new Vector2(0.19f, -0.03f), new Vector2(0.19f, 0.03f), new Vector2(0.16f, 0.035f) }, 20);
                        Lathe(dark, gm, new[] { new Vector2(0.16f, 0.032f), new Vector2(0, 0.032f) }, 20);
                        Box(brass, gm, new Vector3(0.02f, 0.01f, 0.12f), new Vector3(0, 0.04f, 0.04f));
                    }
                    else if (n % 4 == 3) Box(dbrass, m, new Vector3(w * 0.5f, 0.06f, 0.5f), new Vector3(0, h + 0.03f, rc)); // brass inspection plate
                }
            }
            bevel = 0;
        }

        static void BuildChannel()
        {
            // the molten ring: 4 radial bands × 470 segments (~0.25 m) so the shader's undulation reads. UV u = arc length at the
            // centre line (a · 17.7, the same at every radius, so the noise doesn't shear across the channel), v = r.
            var g = G("MoltenChannel", mMolten);
            float[] rs = { 16.75f, 17.225f, 17.7f, 18.175f, 18.65f }; const int SEG = 470;
            for (int b = 0; b < 4; b++) for (int i = 0; i < SEG; i++)
            {
                float aa = TAU * i / SEG, ab = TAU * (i + 1) / SEG;
                Quad(g, I, Polar(aa, rs[b], -1.4f), Polar(ab, rs[b], -1.4f), Polar(ab, rs[b + 1], -1.4f), Polar(aa, rs[b + 1], -1.4f), Vector3.up,
                    new Vector2(aa * 17.7f, rs[b]), new Vector2(ab * 17.7f, rs[b]), new Vector2(ab * 17.7f, rs[b + 1]), new Vector2(aa * 17.7f, rs[b + 1]));
            }
            // a glow curtain rising off the molten (both faces)
            foreach (var (c0, c1) in Arcs(17.7f, 0.6f)) Sector(G("MoltenChannel", mGlowCurtain), I, 17.7f, 17.7f, c0, c1, -1.4f, 0.8f, 0, top: false, ends: false);
            var lights = Child(root, "Lighting/ChannelLights");
            void Point(string name, Vector3 at, Color c, float intensity, float range, bool flicker)
            {
                var l = new GameObject(name).AddComponent<Light>(); l.transform.SetParent(lights, false);
                l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None; l.transform.position = at;
                if (flicker) flickerLights.Add(l);
            }
            for (int i = 0; i < 16; i++) Point("Molten Uplight", Polar((i + 0.5f) / 16 * TAU, 17.6f, -0.8f), Hex(0xFF7A1F), 14f, 7, true);
            for (int i = 0; i < 16; i++) Point("Rim Glow", Polar(i / 16f * TAU, 17.3f, 0.5f), Hex(0xFF8A30), 9f, 4.5f, true);
            for (int i = 0; i < 8; i++) Point("Rim Bounce", Polar((i + 0.5f) / 8 * TAU, 15.6f, 1.6f), Hex(0xFF7A2A), 2.2f, 2.2f, false);
        }

        // Outer foundry wall: massive uneven segments, thick buttresses, pipe runs with brass flanges, dimly glowing furnace
        // hatches facing the channel, steam vents, iron catwalks, and the drum below with ribs, arches and molten falls.
        static void BuildOuterWall()
        {
            bevel = 0.07f;
            var mas = G("OuterWall", mMasonryDark); var mas2 = G("OuterWall", mMasonry); var brass = G("OuterWall", mDarkBrass); var b2 = G("OuterWall", mBrass);
            var iron = G("OuterWall", mIron); var glowDim = G("OuterWall", mGlowDim); var deck = G("OuterWall", mPlank);
            var gaps = new (float a, float half)[] { (0, 3.0f), (PI / 2, 2.55f), (PI, 3.0f), (PI * 1.5f, 4.35f) };
            for (int q = 0; q < 4; q++)
            {
                var (aA, hA) = gaps[q]; var (aB, hB) = gaps[(q + 1) % 4]; if (aB <= aA) aB += TAU;
                float a0 = aA + Mathf.Asin(hA / 18.6f), a1 = aB - Mathf.Asin(hB / 18.6f); int n = Mathf.Max(1, Mathf.RoundToInt((a1 - a0) / (9f * Mathf.Deg2Rad)));
                for (int i = 0; i < n; i++)
                {
                    float b0 = a0 + (a1 - a0) * i / n, b1 = a0 + (a1 - a0) * (i + 1) / n, am = (b0 + b1) / 2;
                    if (Modelled) // one 9° Blender module per segment: hatch / buttress / vent, tangentially fitted, height varied
                    {
                        float sy = Rnd(0.92f, 1.12f);
                        Model("OuterWall", i % 3 == 0 ? "SM_Area2_Wall_Hatch" : i % 3 == 1 ? "SM_Area2_Wall_Buttress" : "SM_Area2_Wall_Vent",
                            Matrix4x4.TRS(Vector3.zero, Quaternion.LookRotation(Polar(am, 1)), new Vector3((b1 - b0) / (9f * Mathf.Deg2Rad), sy, 1)));
                        if (i % 3 == 2) steamVents.Add(Polar(am, 22f, (3.4f + 1.4f) * sy));
                        continue;
                    }
                    float h1 = Rnd(1.3f, 1.7f), h2 = h1 + Rnd(0.8f, 1.0f), h3 = h2 + Rnd(0.7f, 1.2f), e0 = b0 + 0.003f, e1 = b1 - 0.003f;
                    Sector(mas, I, 18.6f, 23.0f, e0, e1, -1.7f, h1, 6);                                  // tier 1
                    Sector(G("OuterWall", mDarkBrass), I, 18.5f, 18.85f, e0, e1, h1, h1 + 0.1f, 6);
                    Sector(mas, I, 19.7f, 23.0f, e0, e1, h1, h2, 6);                                     // tier 2
                    Sector(iron, I, 19.62f, 19.82f, e0, e1, h2, h2 + 0.1f, 6);
                    Sector(mas2, I, 20.9f, 23.0f, e0, e1, h2, h3, 6);                                    // tier 3
                    Sector(iron, I, 20.82f, 21.02f, e0, e1, h3, h3 + 0.1f, 6);
                    if (Rnd() < 0.4f) Box(mas, Radial(am + Rnd(-0.03f, 0.03f)), new Vector3(Rnd(1.0f, 1.6f), Rnd(0.5f, 0.9f), 1.2f), new Vector3(0, h3 + 0.3f, 22.1f)); // blocky crest
                    if (i % 2 == 0) // furnace hatch facing the channel
                    {
                        var hm = Radial(am);
                        Box(brass, hm, new Vector3(1.0f, 0.85f, 0.12f), new Vector3(0, -0.55f, 18.56f));
                        Box(glowDim, hm, new Vector3(0.72f, 0.58f, 0.04f), new Vector3(0, -0.55f, 18.49f));
                        for (int bar = 0; bar < 4; bar++) Box(iron, hm, new Vector3(0.05f, 0.6f, 0.05f), new Vector3(-0.27f + bar * 0.18f, -0.55f, 18.47f));
                    }
                    if (i % 3 == 1) steamVents.Add(Polar(am, 21.9f, h3 + 0.4f));
                }
                // buttresses every 15° and pipe runs with brass flanges between them, on the outer face
                var bs = new List<float>(); if (!Modelled) for (float a = a0; a <= a1 + 1e-3f; a += 15 * Mathf.Deg2Rad) bs.Add(a);
                foreach (var a in bs)
                {
                    var m = Radial(a);
                    Box(mas, m, new Vector3(2.6f, 33.4f, 2.2f), new Vector3(0, (3.4f - 30) / 2, 23.9f));
                    Box(iron, m, new Vector3(2.8f, 0.3f, 2.4f), new Vector3(0, 3.55f, 23.9f));
                    Box(mas2, m, new Vector3(1.6f, 0.9f, 1.4f), new Vector3(0, 4.15f, 23.9f));
                }
                for (int i = 0; i < bs.Count - 1; i++)
                    foreach (float y in new[] { 0.5f, 1.3f })
                    {
                        float c0 = bs[i] + 1.4f / 23.2f, c1 = bs[i + 1] - 1.4f / 23.2f; if (c1 <= c0) continue;
                        Vector3 A = Polar(c0, 23.25f, y), B = Polar(c1, 23.25f, y);
                        Pipe(iron, A, B, y < 1 ? 0.17f : 0.12f, 10);
                        foreach (var p in new[] { A, B, (A + B) / 2 }) Pipe(b2, p - (B - A).normalized * 0.06f, p + (B - A).normalized * 0.06f, y < 1 ? 0.25f : 0.19f, 12);
                    }
                // an iron catwalk hung on the outer face, mid-quarter
                float cm = (a0 + a1) / 2, ch = 0.25f;
                Sector(deck, I, 23.0f, 24.3f, cm - ch, cm + ch, -3.15f, -3.0f, 16);
                for (float a = cm - ch; a <= cm + ch + 1e-3f; a += 0.055f) Box(iron, Radial(a), new Vector3(0.08f, 1.0f, 0.08f), new Vector3(0, -2.5f, 24.2f));
                Sector(b2, I, 24.16f, 24.24f, cm - ch, cm + ch, -2.05f, -1.97f, 16, ends: false);
            }
            bevel = 0;
            // the foundry drum below the wall: masonry, ribs, glowing arches, tapering into the haze; molten falls down it
            var drum = G("OuterWall", mMasonryDark);
            Lathe(drum, I, new[] { new Vector2(10, -42), new Vector2(19, -32), new Vector2(23f, -27), new Vector2(23f, -1.7f) }, 128);
            for (int i = 0; i < 36; i++)
            {
                float a = i / 36f * TAU; Box(iron, TR(Polar(a, 23.25f, -14), YawOut(a)), new Vector3(0.45f, 24, 0.55f));
                if (i % 2 == 0) Box(G("OuterWall", mGlow), TR(Polar(a + PI / 36, 23.05f, -8), YawOut(a + PI / 36)), new Vector3(0.05f, 1.6f, 0.8f));
            }
            for (int i = 0; i < 10; i++)
            {
                float a = (i + 0.37f) / 10 * TAU, w = Rnd(0.8f, 1.6f); Vector3 o = Polar(a, 25.0f), t = Polar(a + PI / 2, 1) * w / 2;
                Quad(G("OuterWall", mFall), I, o - t + Vector3.up * -30, o + t + Vector3.up * -30, o + t + Vector3.up * 0.4f, o - t + Vector3.up * 0.4f, Polar(a, 1), new Vector2(0, 0), new Vector2(w, 0), new Vector2(w, 30.4f), new Vector2(0, 30.4f));
                Box(G("OuterWall", mDarkBrass), TR(Polar(a, 24.85f, 0.5f), YawOut(a)), new Vector3(1.0f, 0.35f, w + 0.4f));
            }
        }

        // Corner towers: three stepped tiers with crenellated cornices, glowing slots on the cornice tops (readable from
        // above), a lantern stage with slit windows, a crown of merlons, a ring of machinery boxes, a ribbed iron dome and a
        // brass finial. The far pair carries a smoking chimney. Two heavy chains each, routed outward.
        static void BuildTowers()
        {
            bevel = 0.04f;
            var mas = G("Towers", mMasonry); var masD = G("Towers", mMasonryDark); var brass = G("Towers", mDarkBrass); var b2 = G("Towers", mBrass); var roof = G("Towers", mRoof);
            var glow = G("Towers", mGlow); var glowDim = G("Towers", mGlowDim); var iron = G("Towers", mIron);
            foreach (var deg in new[] { 45f, 135f, 225f, 315f })
            {
                float a = deg * Mathf.Deg2Rad; Vector3 c = Polar(a, Mathf.Sin(a) > 0 ? 21.5f : 20f); bool near = c.z > 0; float top = near ? 8f : 14f;
                float t1 = top * 0.45f, t2 = top * 0.72f, t3 = top * 0.88f; var m = TR(c, YawOut(a));
                if (Model("Towers", near ? "SM_Area2_Tower_Near" : "SM_Area2_Tower_Far", TRQ(c, Quaternion.LookRotation(Polar(a, 1)))))
                {
                    if (!near) smokeStacks.Add(c + Polar(a + PI, 3f) + Vector3.up * (t3 + 4.7f)); // the model's chimney faces the arena
                    foreach (float off in new[] { -0.4f, 0.4f }) { float b = a + off; Chain(c + Polar(b, 2.4f, t3 + 0.1f), Polar(b, 28, -14), 0.8f); }
                    continue;
                }
                Lathe(masD, m, new[] { new Vector2(3.6f, -28), new Vector2(3.6f, t1) }, 64);
                Sector(roof, m, 3.25f, 3.95f, 0, TAU, t1, t1 + 0.42f, 64, ends: false, bottom: true); Sector(b2, m, 3.93f, 4.0f, 0, TAU, t1 + 0.05f, t1 + 0.37f, 64, ends: false);
                Lathe(mas, m, new[] { new Vector2(3.05f, t1 + 0.42f), new Vector2(3.05f, t2) }, 64);
                Sector(roof, m, 2.7f, 3.35f, 0, TAU, t2, t2 + 0.35f, 64, ends: false, bottom: true); Sector(b2, m, 3.33f, 3.4f, 0, TAU, t2 + 0.05f, t2 + 0.3f, 64, ends: false);
                Lathe(masD, m, new[] { new Vector2(2.45f, t2 + 0.35f), new Vector2(2.45f, t3) }, 64);
                Sector(roof, m, 2.2f, 2.8f, 0, TAU, t3, t3 + 0.28f, 64, ends: false, bottom: true); Sector(b2, m, 2.78f, 2.84f, 0, TAU, t3 + 0.04f, t3 + 0.24f, 64, ends: false);
                // crenellations on each tier's cornice, merlon crown on top
                for (int i = 0; i < 16; i++) { float b = (i + 0.5f) / 16 * TAU; Box(mas, TRQ(c + Polar(b, 3.78f, t1 + 0.42f + 0.22f), Quaternion.LookRotation(Polar(b, 1))), new Vector3(0.6f, 0.44f, 0.3f)); }
                for (int i = 0; i < 12; i++) { float b = (i + 0.5f) / 12 * TAU; Box(mas, TRQ(c + Polar(b, 3.2f, t2 + 0.35f + 0.2f), Quaternion.LookRotation(Polar(b, 1))), new Vector3(0.5f, 0.4f, 0.26f)); }
                for (int i = 0; i < 12; i++) { float b = i / 12f * TAU; Box(mas, TRQ(c + Polar(b, 2.6f, t3 + 0.28f + 0.33f), Quaternion.LookRotation(Polar(b, 1))), new Vector3(0.6f, 0.66f, 0.5f)); }
                for (int i = 0; i < 8; i++) // dormer windows on the dome, glowing faces readable from above
                {
                    float b = (i + 0.5f) / 8 * TAU; var q = Quaternion.LookRotation(Polar(b, 1));
                    Box(roof, TRQ(c + Polar(b, 1.75f, t3 + 1.45f), q), new Vector3(0.5f, 0.55f, 0.5f));
                    Box(glow, TRQ(c + Polar(b, 2.01f, t3 + 1.42f), q), new Vector3(0.3f, 0.36f, 0.04f));
                    Box(glowDim, TRQ(c + Polar(b, 1.75f, t3 + 1.74f), q), new Vector3(0.36f, 0.03f, 0.36f));
                }
                // glowing slots on the cornice tops, readable from the top view
                for (int i = 0; i < 8; i++)
                {
                    float b = i / 8f * TAU;
                    Box(glowDim, TRQ(c + Polar(b, 3.45f, t1 + 0.43f), Quaternion.LookRotation(Polar(b, 1))), new Vector3(0.14f, 0.02f, 0.45f));
                    Box(glowDim, TRQ(c + Polar(b + PI / 8, 2.95f, t2 + 0.36f), Quaternion.LookRotation(Polar(b + PI / 8, 1))), new Vector3(0.12f, 0.02f, 0.38f));
                }
                Lathe(roof, m, new[] { new Vector2(2.3f, t3 + 0.28f), new Vector2(2.2f, t3 + 0.8f), new Vector2(1.85f, t3 + 1.55f), new Vector2(1.15f, t3 + 2.15f), new Vector2(0.4f, t3 + 2.45f), new Vector2(0, t3 + 2.5f) }, 64);
                Cyl(b2, m, 0.08f, t3 + 2.4f, t3 + 3.6f, 10);
                Lathe(b2, m, new[] { new Vector2(0, t3 + 3.2f), new Vector2(0.18f, t3 + 3.35f), new Vector2(0.18f, t3 + 3.45f), new Vector2(0, t3 + 3.6f) }, 12);
                for (int i = 0; i < 8; i++) { float b = i / 8f * TAU; Lathe(brass, m * Matrix4x4.Rotate(Quaternion.Euler(0, -b * Mathf.Rad2Deg, 0)), new[] { new Vector2(2.36f, t3 + 0.28f), new Vector2(2.26f, t3 + 0.8f), new Vector2(1.91f, t3 + 1.55f), new Vector2(1.2f, t3 + 2.17f), new Vector2(0.42f, t3 + 2.47f) }, 1, -0.035f, 0.035f); }
                Sector(b2, m, 3.6f, 3.68f, 0, TAU, t1 - 2.5f, t1 - 2.2f, 64, ends: false);
                Sector(b2, m, 3.05f, 3.12f, 0, TAU, (t1 + t2) / 2, (t1 + t2) / 2 + 0.25f, 64, ends: false);
                for (int i = 0; i < 10; i++) // lantern stage slit windows with iron frames
                {
                    float b = (i + 0.5f) / 10 * TAU; var q = Quaternion.LookRotation(Polar(b, 1)); float sh = t3 - t2 - 0.9f;
                    Box(i % 3 == 0 ? G("Towers", mWindowHot) : i % 3 == 1 ? glow : G("Towers", mWindowWarm), TRQ(c + Polar(b, 2.44f, t2 + 0.35f + 0.25f + sh / 2), q), new Vector3(0.28f, sh, 0.08f));
                    Box(iron, TRQ(c + Polar(b, 2.48f, t2 + 0.35f + 0.25f + sh * 0.6f), q), new Vector3(0.3f, 0.05f, 0.05f)); // transom
                    Box(iron, TRQ(c + Polar(b, 2.47f, t2 + 0.35f + 0.25f + sh / 2), q), new Vector3(0.05f, sh, 0.06f));
                }
                for (int i = 0; i < 8; i++)
                {
                    float b = (i + 0.5f) / 8 * TAU; var q = Quaternion.LookRotation(Polar(b, 1));
                    Box(i % 2 == 0 ? G("Towers", mWindowWarm) : glow, TRQ(c + Polar(b, 3.62f, t1 - 1.6f), q), new Vector3(0.22f, 1.3f, 0.08f));
                    Box(glow, TRQ(c + Polar(b + PI / 8, 3.08f, (t1 + t2) / 2 + 0.9f), Quaternion.LookRotation(Polar(b + PI / 8, 1))), new Vector3(0.2f, 0.8f, 0.08f));
                    Box(masD, TRQ(c + Polar(b + PI / 8, 3.75f, (t1 - 30.2f) / 2), Quaternion.LookRotation(Polar(b + PI / 8, 1))), new Vector3(0.5f, t1 + 29.8f, 0.5f)); // buttress fin
                    Box(iron, TRQ(c + Polar(b, 3.42f, t1 + 0.42f + 0.25f), q), new Vector3(0.55f, 0.5f, 0.5f));                      // machinery boxes
                }
                if (!near) // chimney on the second tier, with smoke
                {
                    float b = a + PI; Vector3 cp = c + Polar(b, 3.0f);
                    Cyl(iron, TR(cp), 0.32f, t2 + 0.35f, t3 + 4.5f, 16); Sector(b2, TR(cp), 0.32f, 0.42f, 0, TAU, t3 + 4.2f, t3 + 4.5f, 16, ends: false);
                    smokeStacks.Add(cp + Vector3.up * (t3 + 4.7f));
                }
                foreach (float off in new[] { -0.4f, 0.4f }) { float b = a + off; Chain(c + Polar(b, 2.4f, t3 + 0.1f), Polar(b, 28, -14), 0.8f); }
            }
            bevel = 0;
        }

        // Shared deck with girders, X truss and cross beams under it. Local frame: +X outward along the deck.
        static void Deck(Matrix4x4 m, float x0, float x1, float width, string group)
        {
            if (Modelled) // 2 m Blender deck modules laid outward (the module's +Z runs along the deck's +X)
            {
                string name = width > 5 ? "SM_Area2_Deck_55" : "SM_Area2_Deck_45";
                for (float x = x0; x < x1 - 0.01f; x += 2f)
                    Model(group, name, m * Matrix4x4.TRS(new Vector3(x, 0, 0), Quaternion.LookRotation(Vector3.right), new Vector3(1, 1, Mathf.Min(2f, x1 - x) / 2f)));
                return;
            }
            var deck = G(group, mPlank); var iron = G(group, mIron); var brass = G(group, mBrass); float len = x1 - x0, cx = (x0 + x1) / 2;
            float keep = bevel; bevel = 0; Box(deck, m, new Vector3(len, 0.5f, width), new Vector3(cx, -0.25f, 0), true); bevel = keep;
            foreach (float s in new[] { -1f, 1f })
            {
                Box(brass, m, new Vector3(len, 0.06f, 0.14f), new Vector3(cx, 0.03f, s * (width / 2 - 0.07f)));            // brass nosing
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
            for (float x = x0; x <= x1 + 1e-3f; x += 1.5f) { Box(iron, m, new Vector3(0.15f, 0.9f, 0.15f), new Vector3(x, 0.45f, z)); Box(brass, m, new Vector3(0.2f, 0.06f, 0.2f), new Vector3(x, 0.93f, z)); }
            Box(iron, m, new Vector3(len, 0.08f, 0.12f), new Vector3((x0 + x1) / 2, 0.86f, z)); Box(iron, m, new Vector3(len, 0.06f, 0.07f), new Vector3((x0 + x1) / 2, 0.5f, z));
            if (!lamps) return;
            for (float x = x0 + 2.2f; x < x1; x += 6f)
            {
                Box(iron, m, new Vector3(0.16f, 2.4f, 0.16f), new Vector3(x, 1.2f, z));
                Lantern(Child(root, group + "/Lanterns"), m.MultiplyPoint3x4(new Vector3(x, 2.75f, z)), 0.42f, false);
            }
        }

        // A bevelled block cluster with lit windows and a doorway the deck runs into (the outer end of a gantry).
        static void EndCluster(Matrix4x4 m, float x, float width, string group)
        {
            if (Model(group, "SM_Area2_Gatehouse", m * TRQ(new Vector3(x, 0, 0), Quaternion.LookRotation(Vector3.right)))) return;
            var mas = G(group, mMasonryDark); var mas2 = G(group, mRimStone); var brass = G(group, mDarkBrass); var roof = G(group, mRoof); var glow = G(group, mGlow); var glowDim = G(group, mGlowDim);
            Box(mas, m, new Vector3(6, 16, width + 3), new Vector3(x + 3, -4, 0));
            Box(brass, m, new Vector3(6.3f, 0.3f, width + 3.3f), new Vector3(x + 3, 4.1f, 0));
            Box(mas2, m, new Vector3(3.6f, 2.4f, width), new Vector3(x + 3.4f, 5.4f, 0));
            Lathe(roof, m * T(x + 3.4f, 6.6f, 0) * Matrix4x4.Rotate(Quaternion.Euler(0, 45, 0)), new[] { new Vector2(width * 0.62f, 0), new Vector2(0, 2.2f) }, 4);
            foreach (float s in new[] { -1f, 1f })
            {
                Box(mas2, m, new Vector3(1.2f, 6.5f, 1.2f), new Vector3(x + 0.3f, 1.25f, s * (width / 2 + 0.9f)));
                Lathe(roof, m * T(x + 0.3f, 4.5f, s * (width / 2 + 0.9f)) * Matrix4x4.Rotate(Quaternion.Euler(0, 45, 0)), new[] { new Vector2(0.85f, 0), new Vector2(0, 1.3f) }, 4);
                Box(glow, m, new Vector3(0.06f, 1.2f, 0.45f), new Vector3(x - 0.02f, 2.6f, s * (width / 2 - 0.4f)));
            }
            Box(glowDim, m, new Vector3(0.06f, 2.3f, width - 1.4f), new Vector3(x - 0.02f, 1.15f, 0));                        // doorway glow
            Box(brass, m, new Vector3(0.3f, 0.35f, width - 0.8f), new Vector3(x - 0.1f, 2.45f, 0));                           // lintel
        }

        static void BuildGantries()
        {
            bevel = 0.07f;
            foreach (var a in new[] { 0f, PI })
            {
                var m = TR(Vector3.zero, YawOut(a)); string grp = "Gantries";
                Deck(m, 14f, 34f, 5.5f, grp);
                Box(G(grp, mBrass), m, new Vector3(0.25f, 0.06f, 5.5f), new Vector3(14.15f, 0.03f, 0)); // threshold
                EndCluster(m, 34f, 5.5f, grp);
                Lantern(Child(root, grp + "/Lanterns"), m.MultiplyPoint3x4(new Vector3(33.4f, 3.3f, 3.6f)), 0.5f, false);
                foreach (float s in new[] { -1f, 1f })
                {
                    Parapet(m, 18.9f, 33.5f, s * 2.62f, grp, s > 0);
                    // suspension pylon: tall square masonry with brass bands and a pyramid cap; two chains to the deck rail
                    var pm = m * T(26f, 0, s * 3.45f);
                    if (!Model(grp, "SM_Area2_Pylon", pm))
                    {
                        Box(G(grp, mMasonryDark), pm, new Vector3(1.1f, 35, 1.1f), new Vector3(0, -10.5f, 0));
                        foreach (float y in new[] { 0.6f, 6.6f }) Box(G(grp, mDarkBrass), pm, new Vector3(1.3f, 0.25f, 1.3f), new Vector3(0, y, 0));
                        Lathe(G(grp, mRoof), pm * Matrix4x4.Rotate(Quaternion.Euler(0, 45, 0)), new[] { new Vector2(0.95f, 7f), new Vector2(0, 8.3f) }, 4);
                    }
                    Vector3 topP = m.MultiplyPoint3x4(new Vector3(26f, 6.9f, s * 3.45f));
                    Chain(topP, m.MultiplyPoint3x4(new Vector3(18.5f, 0.95f, s * 2.62f)), 0.35f);
                    Chain(topP, m.MultiplyPoint3x4(new Vector3(32.5f, 0.95f, s * 2.62f)), 0.35f);
                }
            }
            bevel = 0;
        }

        static void BuildEntryBridge()
        {
            var m = TR(Vector3.zero, YawOut(PI / 2)); string grp = "EntryBridge";
            bevel = 0.06f;
            Deck(m, 14f, 40f, 4.5f, grp);
            var brass = G(grp, mBrass); var mas = G(grp, mRimStone); var dbr = G(grp, mDarkBrass); var glow = G(grp, mGlow); var iron = G(grp, mIron); var roof = G(grp, mRoof);
            Box(brass, m, new Vector3(26f, 0.012f, 0.12f), new Vector3(27f, 0.006f, 0));
            foreach (var (tip, size) in new[] { (19f, 1.3f), (24f, 0.9f) }) // arrows pointing in toward the arena
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
                var pm = m * T(17.2f, 0, s * 2.85f);
                if (!Model(grp, "SM_Area2_GatePillar", pm * Matrix4x4.Rotate(Quaternion.LookRotation(Vector3.right)))) // model's window faces the arena (local -X)
                {
                    Box(mas, pm, new Vector3(1.2f, 10f, 1.2f), new Vector3(0, -1f, 0));
                    Box(dbr, pm, new Vector3(1.45f, 0.22f, 1.45f), new Vector3(0, 4.1f, 0));
                    Box(iron, pm, new Vector3(0.7f, 0.12f, 0.7f), new Vector3(0, 4.27f, 0));
                    Box(glow, pm, new Vector3(0.06f, 0.9f, 0.3f), new Vector3(-0.62f, 2.3f, 0));
                }
                Lantern(Child(root, grp + "/Lanterns"), pm.MultiplyPoint3x4(new Vector3(0, 4.85f, 0)), 0.5f, false);
                Chain(pm.MultiplyPoint3x4(new Vector3(0, 3.9f, 0)), m.MultiplyPoint3x4(new Vector3(21.6f, 0.4f, s * 2.1f)), 0.25f);
                // far arch pillars
                var am = m * T(33.5f, 0, s * 2.95f);
                Box(mas, am, new Vector3(1.4f, 13f, 1.4f), new Vector3(0, 0.5f, 0));
                Box(dbr, am, new Vector3(1.6f, 0.25f, 1.6f), new Vector3(0, 7.1f, 0));
                Lathe(roof, am * T(0, 7.2f, 0) * Matrix4x4.Rotate(Quaternion.Euler(0, 45, 0)), new[] { new Vector2(1.0f, 0), new Vector2(0, 1.5f) }, 4);
                Lantern(Child(root, grp + "/Lanterns"), am.MultiplyPoint3x4(new Vector3(-0.95f, 4.6f, 0)), 0.4f, false);
            }
            Box(mas, m, new Vector3(1.6f, 1.4f, 7.3f), new Vector3(33.5f, 6.3f, 0));
            Box(dbr, m, new Vector3(1.7f, 0.15f, 7.4f), new Vector3(33.5f, 7.05f, 0));
            var em = m * T(32.68f, 6.3f, 0) * Matrix4x4.Rotate(Quaternion.Euler(0, 0, 90));
            Lathe(brass, em, new[] { new Vector2(0.45f, 0), new Vector2(0.45f, 0.1f), new Vector2(0.15f, 0.13f), new Vector2(0, 0.13f) }, 24);
            bevel = 0;
        }

        static void BuildFurnaceGate()
        {
            var m = TR(Vector3.zero, YawOut(PI * 1.5f)); string grp = "FurnaceGate";
            var mas = G(grp, mRimStone); var masD = G(grp, mMasonryDark); var dbr = G(grp, mDarkBrass); var brass = G(grp, mBrass); var iron = G(grp, mIron); var roof = G(grp, mRoof); var glow = G(grp, mGlow); var glowDim = G(grp, mGlowDim); var dark = G(grp, mSlate);
            bevel = 0.07f;
            Deck(m, 14f, 32f, 4.5f, grp);
            if (Model(grp, "SM_Area2_FurnaceGate", TRQ(Polar(PI * 1.5f, 19.5f), Quaternion.LookRotation(Polar(PI * 1.5f, 1)))))
            {
                foreach (float s in new[] { -1f, 1f }) { var pm = m * T(19.5f, 0, s * 3.25f); Chain(pm.MultiplyPoint3x4(new Vector3(0.3f, 6.9f, 0)), m.MultiplyPoint3x4(new Vector3(27f, 16f, s * 6f)), 0.5f); }
                bevel = 0; return;
            }
            foreach (float s in new[] { -1f, 1f })
            {
                var pm = m * T(19.5f, 0, s * 3.25f);
                Box(masD, pm, new Vector3(2f, 9f, 2f), new Vector3(0, 1.5f, 0));
                for (int b = 0; b < 3; b++) Box(dbr, pm, new Vector3(2.12f, 0.18f, 2.12f), new Vector3(0, 0.8f + b * 2.1f, 0));
                Box(dbr, pm, new Vector3(2.35f, 0.25f, 2.35f), new Vector3(0, 6.12f, 0));
                Box(mas, pm, new Vector3(1.6f, 0.8f, 1.6f), new Vector3(0, 6.65f, 0));
                Box(brass, pm, new Vector3(1.8f, 0.15f, 1.8f), new Vector3(0, 7.12f, 0));
                Lathe(roof, pm * T(0, 7.2f, 0) * Matrix4x4.Rotate(Quaternion.Euler(0, 45, 0)), new[] { new Vector2(1.05f, 0), new Vector2(0, 1.3f) }, 4);
                Box(G(grp, mWindowWarm), pm, new Vector3(0.06f, 1.1f, 0.5f), new Vector3(-1.02f, 2.6f, 0));      // window facing the arena
                Box(iron, pm, new Vector3(0.1f, 1.25f, 0.08f), new Vector3(-1.04f, 2.6f, 0)); Box(iron, pm, new Vector3(0.1f, 0.08f, 0.62f), new Vector3(-1.04f, 2.75f, 0)); // mullions
                Box(G(grp, mWindowWarm), pm, new Vector3(0.5f, 1.2f, 0.06f), new Vector3(0, 3.0f, s * 1.02f));  // window on the outer side
                Chain(pm.MultiplyPoint3x4(new Vector3(0.3f, 6.9f, 0)), m.MultiplyPoint3x4(new Vector3(27f, 16f, s * 6f)), 0.5f);
            }
            Box(masD, m, new Vector3(1.6f, 1.3f, 4.6f), new Vector3(19.5f, 5.45f, 0));
            Box(dbr, m, new Vector3(1.7f, 0.15f, 4.7f), new Vector3(19.5f, 6.15f, 0));
            // gear emblem on the lintel, facing the arena: 16 teeth, inner ring, six spokes
            var em = m * T(18.66f, 5.45f, 0) * Matrix4x4.Rotate(Quaternion.Euler(0, 0, 90));
            Lathe(brass, em, new[] { new Vector2(0.55f, 0), new Vector2(0.55f, 0.12f), new Vector2(0.44f, 0.12f), new Vector2(0.44f, 0.05f) }, 32);
            Lathe(brass, em, new[] { new Vector2(0.14f, 0), new Vector2(0.14f, 0.16f), new Vector2(0, 0.16f) }, 16);
            Lathe(dark, em, new[] { new Vector2(0.44f, 0.04f), new Vector2(0, 0.04f) }, 32);
            for (int i = 0; i < 16; i++) { float b = i / 16f * TAU; Box(brass, em * Matrix4x4.Rotate(Quaternion.Euler(0, -b * Mathf.Rad2Deg, 0)), new Vector3(0.16f, 0.12f, 0.13f), new Vector3(0.62f, 0.06f, 0)); }
            for (int i = 0; i < 6; i++) { float b = i / 6f * TAU; Box(brass, em * Matrix4x4.Rotate(Quaternion.Euler(0, -b * Mathf.Rad2Deg, 0)), new Vector3(0.32f, 0.08f, 0.06f), new Vector3(0.28f, 0.08f, 0)); }
            // closed portcullis: dark iron bars over a dim glow
            for (int i = 0; i <= 8; i++) { float z = -2.05f + i * 4.1f / 8; Box(iron, m, new Vector3(0.16f, 4.75f, 0.16f), new Vector3(19.1f, 2.38f, z)); }
            for (int j = 0; j < 4; j++) Box(iron, m, new Vector3(0.1f, 0.12f, 4.2f), new Vector3(19.12f, 0.6f + j * 1.15f, 0));
            // a dark recessed furnace mouth behind the bars: side walls, lintel, and a gradient glow at the back
            Box(G(grp, mFurnace), m, new Vector3(0.05f, 4.7f, 4.4f), new Vector3(20.3f, 2.35f, 0));
            Box(masD, m, new Vector3(0.6f, 4.8f, 4.5f), new Vector3(20.65f, 2.4f, 0));
            foreach (float s in new[] { -1f, 1f }) Box(masD, m, new Vector3(1.2f, 4.8f, 0.2f), new Vector3(19.75f, 2.4f, s * 2.3f));
            Box(masD, m, new Vector3(1.2f, 0.25f, 4.6f), new Vector3(19.75f, 4.85f, 0));
            var hm = m * T(18.85f, 2.3f, 0);
            Quad(G(grp, mHalo), hm, new Vector3(0, -2.6f, -2.6f), new Vector3(0, -2.6f, 2.6f), new Vector3(0, 2.6f, 2.6f), new Vector3(0, 2.6f, -2.6f), Vector3.left, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            bevel = 0;
        }

        // The city below: dark foundry blocks in three depth layers (tops y −20 … −60) with lit window slits and roof vents,
        // chimney stacks, glowing molten rivers and pools between the blocks, far silhouettes, and the hanging lanterns.
        // Flat molten pool: a disc with planar UVs in metres (u along x, v across z; _Across spans the diameter).
        static void Pool(Geo g, Vector3 c, float r)
        {
            const int SEG = 28;
            for (int i = 0; i < SEG; i++)
            {
                float a0 = TAU * i / SEG, a1 = TAU * (i + 1) / SEG; Vector3 p0 = c + Polar(a0, r), p1 = c + Polar(a1, r);
                Vector2 Uv(Vector3 p) => new Vector2(p.x - c.x + r, p.z - c.z + r);
                Tri(g, I, c, p0, p1, Vector3.up, Vector3.up, Vector3.up, Uv(c), Uv(p0), Uv(p1));
            }
        }

        static void BuildSurroundings()
        {
            string grp = "Surroundings"; var city = G(grp, mCity); var iron = G(grp, mCityRoof); var glow = G(grp, mGlow); var glowDim = G(grp, mGlowDim); var brass = G(grp, mDarkBrass);
            const float GROUND = -50f; var riverPts = new List<(Vector3 p, float w)>();
            Lathe(G(grp, mAbyss), I, new[] { new Vector2(360, GROUND), new Vector2(0, GROUND) }, 64);
            var river = G(grp, mRiver); var pool = G(grp, mPool);
            for (int k = 0; k < 8; k++)
            {
                float a = k / 8f * TAU + Rnd(-0.25f, 0.25f), w = Rnd(1.0f, 1.6f), turn = (k % 2 == 0 ? 1 : -1) * Rnd(0.012f, 0.03f); var pts = new List<Vector3>();
                for (float r = 14; r < 220; r += 4) { a += turn + Rnd(-0.04f, 0.04f); pts.Add(Polar(a, r, GROUND + 0.08f)); }   // starts under the drum, curving along the streets
                // one continuous ribbon: each point's side vector averages its neighbouring segments, so there are no gaps
                var sides = new Vector3[pts.Count];
                for (int i = 0; i < pts.Count; i++) { var d = (pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[Mathf.Max(i - 1, 0)]).normalized; sides[i] = Vector3.Cross(Vector3.up, d) * w / 2; riverPts.Add((pts[i], w)); }
                float along = 0;
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    float l = Vector3.Distance(pts[i], pts[i + 1]);
                    Quad(river, I, pts[i] - sides[i], pts[i] + sides[i], pts[i + 1] + sides[i + 1], pts[i + 1] - sides[i + 1], Vector3.up, new Vector2(0, along), new Vector2(w * 0.86f, along), new Vector2(w * 0.86f, along + l), new Vector2(0, along + l));
                    along += l;
                }
            }
            var pools = new List<(Vector3 p, float r)>();
             // corner pools under the reference's glowing corners
            for (int i = 0; i < 12; i++) { float a = Rnd() * TAU, r = Rnd(40, 150), pr = Rnd(2, 4); pools.Add((Polar(a, r, GROUND), pr)); Pool(pool, Polar(a, r, GROUND + 0.1f), pr); }
            bool Clear(Vector3 at, float half) => riverPts.All(q => Vector2.Distance(new Vector2(at.x, at.z), new Vector2(q.p.x, q.p.z)) > half + q.w / 2 + 0.5f) && pools.All(q => Vector2.Distance(new Vector2(at.x, at.z), new Vector2(q.p.x, q.p.z)) > half + q.r + 0.4f);
            foreach (var (n, r0, r1, t0, t1) in new[] { (320, 24f, 70f, -20f, -32f), (260, 70f, 130f, -28f, -40f), (150, 130f, 210f, -38f, -48f) })
                for (int i = 0; i < n; i++)
                {
                    float a = Rnd() * TAU, r = Rnd(r0, r1), w = Rnd(3, 9), d = Rnd(3, 9), top = Mathf.Lerp(t0, t1, Rnd()), yaw = Rnd(0, 90);
                    if (!Clear(Polar(a, r), Mathf.Max(w, d) * 0.72f)) continue;
                    var m = TR(Polar(a, r, 0), yaw); float h = top - GROUND;
                    if (Model(grp, "SM_Area2_City_" + "ABCDEF"[i % 6], Matrix4x4.TRS(Polar(a, r, GROUND), Quaternion.Euler(0, yaw, 0), new Vector3(w / 6f, h / 12f, d / 6f)))) continue;
                    Box(city, m, new Vector3(w, h, d), new Vector3(0, GROUND + h / 2, 0));
                    if (Rnd() < 0.4f) Lathe(iron, m * Matrix4x4.Rotate(Quaternion.Euler(0, 45, 0)), new[] { new Vector2(Mathf.Min(w, d) * 0.7f, top), new Vector2(0, top + Rnd(1.5f, 3)) }, 4);
                    else
                    {
                        Box(iron, m, new Vector3(w + 0.3f, 0.35f, d + 0.3f), new Vector3(0, top + 0.17f, 0));
                        if (Rnd() < 0.4f) Box(glowDim, m, new Vector3(0.9f, 0.06f, 0.5f), new Vector3(Rnd(-w / 4, w / 4), top + 0.37f, Rnd(-d / 4, d / 4))); // roof vent
                    }
                    for (int j = 0; j < 4; j++) if (Rnd() < 0.6f) Box(Rnd() < 0.5f ? G(grp, mWindowWarm) : glow, m, new Vector3(0.35f, 1.4f, 0.06f), new Vector3(Rnd(-w / 3, w / 3), top - Rnd(1.5f, 6), d / 2 + 0.03f));
                }
            for (int i = 0; i < 16; i++) // chimney stacks
            {
                float a = Rnd() * TAU, r = Rnd(42, 130), rad = Rnd(1, 2.2f), top = Rnd(-8, 22); var m = TR(Polar(a, r, 0));
                Lathe(city, m, new[] { new Vector2(rad * 1.2f, GROUND), new Vector2(rad, top) }, 20);
                for (float y = -40; y < top - 2; y += 9) Sector(brass, m, rad, rad + 0.18f, 0, TAU, y, y + 0.4f, 20, ends: false);
                Sector(glow, m, rad * 0.7f, rad, 0, TAU, top, top + 0.05f, 20, inner: false, outer: false, ends: false);
                if (i < 7) smokeStacks.Add(Polar(a, r, top + 0.5f));
            }
            for (int i = 0; i < 26; i++) // far silhouettes on the horizon
            {
                float a = Rnd() * TAU, r = Rnd(170, 250), w = Rnd(6, 14), top = Rnd(15, 60); var m = TR(Polar(a, r, 0), Rnd(0, 90));
                Box(city, m, new Vector3(w, top - GROUND, w), new Vector3(0, (top + GROUND) / 2, 0));
                Box(city, m, new Vector3(w * 0.6f, 8, w * 0.6f), new Vector3(0, top + 4, 0));
                if (Rnd() < 0.5f) Lathe(iron, m * Matrix4x4.Rotate(Quaternion.Euler(0, 45, 0)), new[] { new Vector2(w * 0.45f, top + 8), new Vector2(0, top + 8 + w) }, 4);
            }
            // cage lanterns hanging on chains from far above, behind the arena
            foreach (var p in new[] { new Vector3(-23, 6, -9), new Vector3(23, 5, -8), new Vector3(-11, 8, -25), new Vector3(12, 7, -25) })
            {
                Chain(p + Vector3.up * 1.0f, p + Vector3.up * 40, 0);
                Lantern(Child(root, "Chains/HangingLanterns"), p, 1f, true);
            }
            // warm light pooling under the arena drum
            Quad(G("MoltenChannel", mUnderGlow), I, new Vector3(-30, -40, -30), new Vector3(30, -40, -30), new Vector3(30, -40, 30), new Vector3(-30, -40, 30), Vector3.up, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
        }

        // Cage lantern: iron frame, glowing core, pyramid cap and spike. Shared meshes, separate objects so they can sway.
        static void BuildLanternMeshes()
        {
            var gi = new Geo(); var gg = new Geo();
            foreach (var (x, z) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) }) Box(gi, I, new Vector3(0.12f, 1.5f, 0.12f), new Vector3(x * 0.5f, 0, z * 0.5f));
            foreach (float y in new[] { -0.8f, 0.8f }) Box(gi, I, new Vector3(1.3f, 0.14f, 1.3f), new Vector3(0, y, 0), true);
            for (int i = 0; i < 4; i++) { var q = Quaternion.Euler(0, i * 90, 0); Box(gi, TRQ(Vector3.zero, q), new Vector3(0.06f, 1.5f, 0.06f), new Vector3(0, 0, 0.55f)); Box(gi, TRQ(Vector3.zero, q), new Vector3(1.0f, 0.06f, 0.06f), new Vector3(0, 0.2f, 0.55f)); }
            Lathe(gi, Matrix4x4.Rotate(Quaternion.Euler(0, 45, 0)), new[] { new Vector2(0.95f, 0.87f), new Vector2(0, 1.5f) }, 4);
            Lathe(gi, Matrix4x4.Rotate(Quaternion.Euler(0, 45, 0)), new[] { new Vector2(0, -1.45f), new Vector2(0.5f, -0.87f) }, 4);
            Link(gi, Matrix4x4.Translate(new Vector3(0, 1.9f, 0)));
            Box(gg, I, new Vector3(0.85f, 1.35f, 0.85f));
            Mesh Make(Geo g, string name) { var m = new Mesh { name = name }; m.SetVertices(g.V); m.SetNormals(g.N); m.SetUVs(0, g.U); m.SetTriangles(g.T, 0); m.RecalculateBounds(); m.RecalculateTangents(); return SaveAsset(m, "Meshes/" + name + ".asset"); }
            lanternIron = Make(gi, "LanternIron"); lanternGlow = Make(gg, "LanternGlow");
            lanternModel = ModelMesh("SM_Area2_Lantern", out lanternMats);
        }
        static Mesh lanternModel; static Material[] lanternMats;

        static void Lantern(Transform parent, Vector3 pos, float scale, bool swing)
        {
            var go = new GameObject("CageLantern"); go.transform.SetParent(parent, false); go.transform.position = pos; go.transform.localScale = Vector3.one * scale;
            if (lanternModel)
            {
                var c = new GameObject(lanternModel.name); c.transform.SetParent(go.transform, false);
                c.AddComponent<MeshFilter>().sharedMesh = lanternModel; c.AddComponent<MeshRenderer>().sharedMaterials = lanternMats;
            }
            else foreach (var (mesh, mat) in new[] { (lanternIron, mIron), (lanternGlow, mGlow) })
            {
                var c = new GameObject(mesh.name); c.transform.SetParent(go.transform, false);
                c.AddComponent<MeshFilter>().sharedMesh = mesh; var mr = c.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
                mr.shadowCastingMode = mat == mGlow ? ShadowCastingMode.Off : ShadowCastingMode.On;
            }
            if (swing) swingers.Add(go.transform);
            if (scale >= 0.4f)
            {
                var l = new GameObject("Lantern Light").AddComponent<Light>(); l.transform.SetParent(go.transform, false);
                l.type = LightType.Point; l.color = Hex(0xFFA040); l.intensity = 3f * scale; l.range = 5f * scale + 1; l.shadows = LightShadows.None;
                flickerLights.Add(l);
            }
        }

        // ================================================================== lighting, sky, post, camera ==================================================================
        static Camera BuildLightingAndCamera()
        {
            var sky = new Material(Shader.Find("Skybox/Panoramic")) { name = "FoundrySky" };
            sky.SetTexture("_MainTex", SaveTex("SkyPanorama", 2048, 1024, PaintSky(2048, 1024))); sky.SetFloat("_Mapping", 1); sky.SetFloat("_Exposure", 1f);
            RenderSettings.skybox = SaveAsset(sky, "Materials/FoundrySky.mat");
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogColor = Hex(0x3A2622); RenderSettings.fogDensity = 0.006f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex(0x4A4448, 1.0f); RenderSettings.ambientEquatorColor = Hex(0x3A2218, 0.9f); RenderSettings.ambientGroundColor = Hex(0xFF6A1A, 0.4f);

            var lights = Child(root, "Lighting");
            Light Dir(string name, Color c, float intensity, Vector3 euler, bool shadows)
            {
                var l = new GameObject(name).AddComponent<Light>(); l.transform.SetParent(lights, false);
                l.type = LightType.Directional; l.color = c; l.intensity = intensity; l.shadows = shadows ? LightShadows.Soft : LightShadows.None; l.transform.rotation = Quaternion.Euler(euler);
                return l;
            }
            var key = Dir("Key Light", Hex(0xE9DED0), 1.15f, new Vector3(60, -20, 0), true); key.shadowStrength = 0.8f; RenderSettings.sun = key;
            Dir("Cool Fill", Hex(0x8098C8), 0.5f, new Vector3(40, 165, 0), false);
            Dir("Furnace Uplight", Hex(0xFF6A1A), 0.35f, new Vector3(-60, 30, 0), false);
            var pit = Child(lights, "PitGlow");
            for (int i = 0; i < 12; i++)
            {
                float a = Rnd() * TAU, r = Rnd(40, 130); var l = new GameObject("Pit Glow").AddComponent<Light>(); l.transform.SetParent(pit, false);
                l.type = LightType.Point; l.color = Hex(0xFF5A18); l.intensity = 8; l.range = 16; l.shadows = LightShadows.None; l.transform.position = Polar(a, r, -44);
            }
            for (int i = 0; i < 4; i++)
            {
                var l = new GameObject("Under Drum Glow").AddComponent<Light>(); l.transform.SetParent(pit, false);
                l.type = LightType.Point; l.color = Hex(0xFF6A20); l.intensity = 30; l.range = 18; l.shadows = LightShadows.None; l.transform.position = Polar(i / 4f * TAU + 0.4f, 8, -26);
            }

            var volume = new GameObject("Post").AddComponent<Volume>(); volume.transform.SetParent(lights, false); volume.isGlobal = true;
            string path = Gen + "/BrassFoundryPost.asset"; AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, path);
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true); bloom.threshold.Override(1.05f); bloom.intensity.Override(0.6f); bloom.scatter.Override(0.62f);
            var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.25f); vig.color.Override(Hex(0x140806));
            var ca = profile.Add<ColorAdjustments>(true); ca.contrast.Override(12f); ca.saturation.Override(-4f); ca.postExposure.Override(0.25f);
            foreach (var c in profile.components) { c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy; AssetDatabase.AddObjectToAsset(c, profile); }
            AssetDatabase.SaveAssets(); volume.sharedProfile = profile;

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>(); cam.nearClipPlane = 0.1f; cam.farClipPlane = 900; cam.clearFlags = CameraClearFlags.Skybox;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>(); data.renderPostProcessing = true; data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.AddComponent<AudioListener>();
            return cam;
        }

        // Ambient FX on Area2Particles (the project has no ParticleSystem module). Nothing spawns over the floor.
        static void BuildFX()
        {
            var fx = Child(root, "FX");
            void Make(string name, Material mat, System.Action<Area2Particles> setup)
            {
                var go = new GameObject(name); go.SetActive(false); go.transform.SetParent(fx, false);
                go.AddComponent<MeshFilter>(); var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
                var p = go.AddComponent<Area2Particles>(); setup(p); go.SetActive(true);
            }
            Make("Embers", mEmber, p => { p.shape = Area2Particles.Shape.Ring; p.center = new Vector3(0, -1.3f, 0); p.radius = 17.7f; p.thickness = 1.4f; p.capacity = 400; p.rate = 55; p.lifetime = new Vector2(4, 8); p.size = new Vector2(0.05f, 0.12f); p.velocityMin = new Vector3(-0.15f, 0.7f, -0.15f); p.velocityMax = new Vector3(0.15f, 1.8f, 0.15f); p.wander = 0.25f; p.seed = 3; });
            Make("ChannelHaze", mHaze, p => { p.shape = Area2Particles.Shape.Ring; p.center = new Vector3(0, -1.1f, 0); p.radius = 17.9f; p.thickness = 1.2f; p.capacity = 70; p.rate = 7; p.lifetime = new Vector2(7, 10); p.size = new Vector2(2.2f, 3.6f); p.grow = new Vector2(0.8f, 1.8f); p.velocityMin = new Vector3(0, 0.25f, 0); p.velocityMax = new Vector3(0, 0.55f, 0); p.wander = 0.15f; p.fadeIn = 0.3f; p.fadeOut = 0.5f; p.seed = 5; });
            Make("ChannelSparks", mSpark, p => { p.shape = Area2Particles.Shape.Ring; p.center = new Vector3(0, -1.2f, 0); p.radius = 17.7f; p.thickness = 1.0f; p.capacity = 120; p.rate = 0; p.burstInterval = 2.4f; p.burstCount = 14; p.lifetime = new Vector2(0.6f, 1.1f); p.size = new Vector2(0.035f, 0.07f); p.velocityMin = new Vector3(-1.2f, 2f, -1.2f); p.velocityMax = new Vector3(1.2f, 4f, 1.2f); p.gravity = 6; p.fadeIn = 0.02f; p.seed = 7; });
            Make("GateSparks", mSpark, p => { p.shape = Area2Particles.Shape.Box; p.center = new Vector3(0, 5.6f, -19.5f); p.boxSize = new Vector3(4, 0.2f, 0.5f); p.capacity = 80; p.rate = 0; p.burstInterval = 3.4f; p.burstCount = 18; p.lifetime = new Vector2(0.7f, 1.3f); p.size = new Vector2(0.03f, 0.06f); p.velocityMin = new Vector3(-1.5f, 0.5f, -0.2f); p.velocityMax = new Vector3(1.5f, 2.5f, 1.2f); p.gravity = 7; p.fadeIn = 0.02f; p.seed = 11; });
            int k = 0;
            foreach (var v in steamVents)
                Make("SteamVent", mSteam, p => { p.shape = Area2Particles.Shape.Point; p.center = v; p.capacity = 30; p.rate = 0.4f; p.burstInterval = 4.5f; p.burstCount = 6; p.lifetime = new Vector2(2.5f, 3.5f); p.size = new Vector2(0.6f, 1.1f); p.grow = new Vector2(0.7f, 3f); p.velocityMin = new Vector3(-0.3f, 1.2f, -0.3f); p.velocityMax = new Vector3(0.3f, 2.2f, 0.3f); p.wander = 0.3f; p.fadeIn = 0.1f; p.seed = 20 + k++; });
            foreach (var s in smokeStacks)
                Make("ChimneySmoke", mSmoke, p => { p.shape = Area2Particles.Shape.Point; p.center = s; p.capacity = 28; p.rate = 1.6f; p.lifetime = new Vector2(10, 14); p.size = new Vector2(2.5f, 4f); p.grow = new Vector2(0.8f, 2.6f); p.velocityMin = new Vector3(0.2f, 1.8f, -0.2f); p.velocityMax = new Vector3(0.6f, 2.8f, 0.2f); p.wander = 0.4f; p.fadeIn = 0.15f; p.fadeOut = 0.5f; p.seed = 40 + k++; });
            Make("SmokeBanks", mSmoke, p => { p.shape = Area2Particles.Shape.Box; p.center = new Vector3(0, -36, 0); p.boxSize = new Vector3(240, 8, 240); p.capacity = 70; p.rate = 3.5f; p.lifetime = new Vector2(18, 24); p.size = new Vector2(18, 34); p.grow = new Vector2(0.9f, 1.3f); p.velocityMin = new Vector3(-0.4f, 0, -0.4f); p.velocityMax = new Vector3(0.4f, 0.15f, 0.4f); p.fadeIn = 0.3f; p.fadeOut = 0.4f; p.seed = 90; });
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
                string shots = ShotsDir();
                var driver = Object.FindAnyObjectByType<BrassFoundryArena>();
                var cam = Camera.main;
                var report = new StringBuilder();
                driver.SetView(BrassFoundryArena.View.Gameplay); Particles(6);
                // lava motion: the same gameplay frame at clock 0 and 1 s; the channel must change, the floor must not
                BrassFoundryArena.SetTime(0); Capture(Path.Combine(shots, "unity-flow-a.png"));
                var fa = Capture(Path.Combine(shots, "unity-flow-a.png"));
                BrassFoundryArena.SetTime(1); var fb = Capture(Path.Combine(shots, "unity-flow-b.png"));
                report.AppendLine(FlowDiff(fa, fb, 1920, 1080, cam));
                report.AppendLine(moltenCoverage);
                BrassFoundryArena.SetTime(2.5f);
                report.AppendLine(CheckOcclusion());
                report.AppendLine(Stats());
                report.AppendLine("Texel density (px/m): " + string.Join(", ", texelDensity.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value:0}")));
                Capture(Path.Combine(shots, "unity-game.png"));
                driver.SetView(BrassFoundryArena.View.TopDown); Particles(0); Capture(Path.Combine(shots, "unity-top.png"), 1600, 1600);
                driver.SetView(BrassFoundryArena.View.Hero); Particles(0); Capture(Path.Combine(shots, "unity-hero.png"));
                // close-ups: a rim stretch, a far tower, the furnace gate
                void Shot(string file, Vector3 pos, Vector3 look, float fov) { cam.orthographic = false; cam.fieldOfView = fov; cam.transform.position = pos; cam.transform.LookAt(look); Particles(0); Capture(Path.Combine(shots, file)); }
                Shot("unity-close-rim.png", Polar(160 * Mathf.Deg2Rad, 23f, 6.5f), Polar(158 * Mathf.Deg2Rad, 15.6f, 0.4f), 40);
                Shot("unity-close-tower.png", Polar(292 * Mathf.Deg2Rad, 36f, 20f), Polar(315 * Mathf.Deg2Rad, 20f, 8f), 38);
                Shot("unity-close-gate.png", new Vector3(0, 6.5f, -7.5f), new Vector3(0, 3f, -19.5f), 42);
                // telegraph test: a coral-red disc on the floor (not saved into the scene)
                driver.SetView(BrassFoundryArena.View.Gameplay); Particles(0);
                var tele = new GameObject("TelegraphTest"); tele.transform.position = new Vector3(5.5f, 0.02f, -2.5f);
                var tg = new Geo(); Sector(tg, I, 0, 4, 0, TAU, 0, 0, 96, inner: false, outer: false, ends: false);
                var eg = new Geo(); Sector(eg, I, 3.85f, 4, 0, TAU, 0, 0.005f, 96, inner: false, outer: false, ends: false);
                foreach (var (g, c) in new[] { (tg, new Color(1, 0.29f, 0.12f, 0.35f)), (eg, new Color(1, 0.42f, 0.23f, 0.95f)) })
                {
                    var mesh = new Mesh(); mesh.SetVertices(g.V); mesh.SetNormals(g.N); mesh.SetUVs(0, g.U); mesh.SetTriangles(g.T, 0);
                    var go = new GameObject("Disc"); go.transform.SetParent(tele.transform, false); go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); mat.SetColor("_BaseColor", c);
                    mat.SetFloat("_Surface", 1); mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); mat.SetFloat("_ZWrite", 0); mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); mat.renderQueue = 3000;
                    go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                }
                Capture(Path.Combine(shots, "unity-game-tele.png"));
                Object.DestroyImmediate(tele);
                File.WriteAllText(Path.Combine(shots, "unity-report.txt"), report.ToString());
                Debug.Log(report.ToString());
            }
            catch (System.Exception e) { Debug.LogException(e); code = 1; }
            EditorApplication.Exit(code);
        }

        // Batch check of the run loop's copy (run RunBuild.BuildScene first): the run scene keeps the arena driver and the
        // MoltenFlow materials, and its lava moves between clock 0 and 1 s. Writes unity-runscene.txt.
        public static void BatchVerifyRunScene()
        {
            int code = 0;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                const string runScene = "Assets/LastBell/Run/Maps/Run_BrassFoundry.unity";
                var sb = new StringBuilder();
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(runScene) == null) sb.AppendLine("Run scene missing: " + runScene);
                else
                {
                    EditorSceneManager.OpenScene(runScene);
                    var driver = Object.FindAnyObjectByType<BrassFoundryArena>();
                    var molten = Object.FindObjectsByType<MeshRenderer>().Where(r => r.sharedMaterial && r.sharedMaterial.shader.name == "LastBell/Area2/MoltenFlow").ToList();
                    sb.AppendLine($"Run scene: driver {(driver ? "present" : "MISSING")}, {molten.Count} MoltenFlow renderers, Run Director {(GameObject.Find("Run Director") ? "present" : "missing")}");
                    var cam = Camera.main;
                    if (!cam) { cam = new GameObject("Check Camera").AddComponent<Camera>(); cam.tag = "MainCamera"; }
                    cam.orthographic = false; cam.fieldOfView = 44; cam.transform.position = new Vector3(0, 29, 22.5f); cam.transform.LookAt(new Vector3(0, 0, 1));
                    string shots = ShotsDir();
                    BrassFoundryArena.SetTime(0); Capture(Path.Combine(shots, "unity-run-a.png")); var a = Capture(Path.Combine(shots, "unity-run-a.png"));
                    BrassFoundryArena.SetTime(1); var b = Capture(Path.Combine(shots, "unity-run-b.png"));
                    sb.AppendLine(FlowDiff(a, b, 1920, 1080, cam).Replace("Lava flow check", "Run scene lava flow check"));
                }
                File.WriteAllText(Path.Combine(ShotsDir(), "unity-runscene.txt"), sb.ToString());
                Debug.Log(sb.ToString());
            }
            catch (System.Exception e) { Debug.LogException(e); code = 1; }
            EditorApplication.Exit(code);
        }

        static string ShotsDir()
        {
            var args = System.Environment.GetCommandLineArgs(); int si = System.Array.IndexOf(args, "-shots");
            string shots = si >= 0 && si + 1 < args.Length ? args[si + 1] : Path.GetFullPath(Path.Combine(Application.dataPath, "../../prototype/arena-3d/areas/area-2/shots"));
            Directory.CreateDirectory(shots); return shots;
        }

        // Steps every Area2Particles emitter (seconds = 0 only re-faces the quads to the current camera).
        static void Particles(float seconds) { foreach (var p in Object.FindObjectsByType<Area2Particles>()) p.Simulate(seconds); }

        // Mean absolute pixel difference (0–255) between two gameplay frames inside a channel mask (rays that reach the
        // molten surface at r 16.95–18.45) and a floor mask (rays that reach y = 0 at r <= 13).
        static string FlowDiff(Color32[] a, Color32[] b, int w, int h, Camera cam)
        {
            double ch = 0, fl = 0; int nc = 0, nf = 0; cam.aspect = (float)w / h;
            for (int y = 0; y < h; y += 2) for (int x = 0; x < w; x += 2)
            {
                var ray = cam.ViewportPointToRay(new Vector3((x + 0.5f) / w, (y + 0.5f) / h, 0));
                if (ray.direction.y >= -1e-4f) continue;
                int i = y * w + x; var ca = a[i]; var cb = b[i];
                float d = (Mathf.Abs(ca.r - cb.r) + Mathf.Abs(ca.g - cb.g) + Mathf.Abs(ca.b - cb.b)) / 3f;
                var p0 = ray.origin + ray.direction * ((0 - ray.origin.y) / ray.direction.y);
                if (new Vector2(p0.x, p0.z).magnitude <= 13f) { fl += d; nf++; continue; }
                var p1 = ray.origin + ray.direction * ((-1.41f - ray.origin.y) / ray.direction.y); float r1 = new Vector2(p1.x, p1.z).magnitude;
                if (r1 > 16.95f && r1 < 18.45f) { ch += d; nc++; }
            }
            double cm = nc > 0 ? ch / nc : 0, fm = nf > 0 ? fl / nf : 0; bool pass = cm > 4 && fm < 0.5;
            return $"Lava flow check (gameplay view, clock 0 vs 1 s): channel {cm:0.00}/255 over {nc} px, floor {fm:0.00}/255 over {nf} px -> {(pass ? "PASS" : "FAIL")} (need channel > 4, floor < 0.5)";
        }

        // Raycast from the gameplay camera to 64 points on r = 13.7 (30 cm inside the 0.29 m edge curb, which by its height
        // hides the last few cm at an oblique angle) and 64 on r = 10; anything but the floor in the way is a failure.
        static string CheckOcclusion()
        {
            var temp = new List<Collider>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.GetComponent<Collider>() || mf.GetComponent<Area2Particles>() || !mf.sharedMesh) continue;
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
            int lights = root.GetComponentsInChildren<Light>().Length; var mats = rs.SelectMany(r => r.sharedMaterials).Where(m => m).Distinct().ToList();
            var texs = new HashSet<Texture>(); foreach (var m in mats.Append(RenderSettings.skybox)) foreach (var id in m.GetTexturePropertyNameIDs()) { var t = m.GetTexture(id); if (t) texs.Add(t); }
            long bytes = texs.Sum(t => Profiler.GetRuntimeMemorySizeLong(t));
            return $"Stats: {rs.Length} mesh renderers (draw-call proxy, before batching), {mats.Count} materials, {tris:N0} triangles, {lights} lights, {texs.Count} textures using {bytes / (1024f * 1024f):0.0} MB (largest: " + string.Join(", ", texs.OrderByDescending(t => Profiler.GetRuntimeMemorySizeLong(t)).Take(6).Select(t => $"{t.name} {Profiler.GetRuntimeMemorySizeLong(t) / (1024f * 1024f):0.0}")) + ")";
        }

        public static Color32[] Capture(string file, int w = 1920, int h = 1080)
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
            var px = tex.GetPixels32();
            Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
            Debug.Log("Saved " + file);
            return px;
        }
    }
}
