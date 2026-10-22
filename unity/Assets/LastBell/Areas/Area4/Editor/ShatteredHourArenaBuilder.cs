using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LastBell.Areas.Area4.EditorTools
{
    // Last Bell > Areas > Area 4 – Shattered Hour: builds (once) and opens The Throne of the Final Toll, the final-boss
    // arena, from prototype/arena-3d/areas/area-4/SHATTERED-HOUR-UNITY-PROMPT.md, its top-down plan and the detail pass
    // (SHATTERED-HOUR-DETAIL-PASS-PROMPT.md, gpt/images/10-arena-shattered-hour.png). Everything is generated here:
    // bevelled meshes, profile sweeps, painted trim sheets with normal maps, the floor, materials, the scene and the two
    // prefab roots. Static groups are merged into a few meshes at the end to keep draw calls low. Environment only: the
    // fissure and the four rim stations are decoration, no boss, no hazards.
    // Axes: north (the bell) is −Z, south (the entrance, the camera side) is +Z. The gameplay camera faces north, so
    // screen-right ("east" on the plan) is world −X; P(deg, r) places things by plan compass degrees.
    public static class ShatteredHourArenaBuilder
    {
        const string Dir = "Assets/LastBell/Areas/Area4", Gen = Dir + "/Generated", PrefabDir = Dir + "/Prefabs", ShotDir = Dir + "/Shots";
        const string ScenePath = Dir + "/ShatteredHour_FinalArena.unity";
        const float R = 13.5f, RimOut = 15.1f, RimH = 0.9f, CapY = 1.0f, FloorH = 13.6f, Trim = 1 / 2.4f;
        // half-widths of the parapet gaps, in plan degrees
        static readonly float GapN = Mathf.Asin(1.5f / R) * Mathf.Rad2Deg, GapS = Mathf.Asin(2f / R) * Mathf.Rad2Deg, GapEW = Mathf.Asin(1.75f / R) * Mathf.Rad2Deg, GapSt = 8.6f;

        [MenuItem("Last Bell/Areas/Area 4 – Shattered Hour", priority = 40)]
        static void Open()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) Build();
            EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Last Bell/Areas/Area 4 – Shattered Hour (Rebuild)", priority = 41)]
        static void Rebuild()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(); EditorSceneManager.OpenScene(ScenePath);
        }

        // ---------------------------------------------------------------- helpers
        static System.Random rng;
        static float Rn() => (float)rng.NextDouble();
        static float Rr(float a, float b) => a + (b - a) * Rn();
        static Color Hex(uint rgb, float k = 1) => new Color(((rgb >> 16) & 255) / 255f * k, ((rgb >> 8) & 255) / 255f * k, (rgb & 255) / 255f * k);
        static Vector3 Out(float deg) { float a = deg * Mathf.Deg2Rad; return new Vector3(-Mathf.Sin(a), 0, -Mathf.Cos(a)); }
        static Vector3 P(float deg, float r, float y = 0) => Out(deg) * r + Vector3.up * y;
        static Quaternion Facing(float deg) => Quaternion.LookRotation(Out(deg)); // local +Z points outward
        static float Th(float deg) => 270f - deg;                                  // plan degrees → Unity polar angle (x = cos, z = sin)
        static Vector3 Polar(float unityDeg, float r, float y = 0) { float a = unityDeg * Mathf.Deg2Rad; return new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r); }

        static T Save<T>(T asset, string file) where T : Object
        {
            string path = Gen + "/" + file; AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(asset, path); return asset;
        }
        // Meshes stay in memory while building; only the ones the finished scene still references are saved (PersistMeshes).
        static readonly Dictionary<string, int> names = new Dictionary<string, int>();
        static void PersistMeshes(params Transform[] roots)
        {
            var done = new HashSet<Mesh>();
            foreach (var root in roots)
            {
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) Persist(mf.sharedMesh, done);
                foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true)) Persist(mc.sharedMesh, done);
            }
        }
        static void Persist(Mesh m, HashSet<Mesh> done)
        {
            if (!m || done.Contains(m) || AssetDatabase.Contains(m)) return;
            done.Add(m); names.TryGetValue(m.name, out int n); names[m.name] = n + 1;
            Save(m, (n == 0 ? m.name : m.name + "_" + n) + ".asset");
        }

        sealed class MB
        {
            readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); readonly List<Vector2> u = new List<Vector2>(); readonly List<int> t = new List<int>();
            public float uvScale = 1;
            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 hint, Vector2? ua = null, Vector2? ub = null, Vector2? uc = null)
            {
                var nn = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(nn, hint) < 0) { (b, c) = (c, b); (ub, uc) = (uc, ub); nn = -nn; }
                nn.Normalize(); int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); n.Add(nn); n.Add(nn); n.Add(nn);
                u.Add(ua.HasValue ? ua.Value * uvScale : UV(a, nn)); u.Add(ub.HasValue ? ub.Value * uvScale : UV(b, nn)); u.Add(uc.HasValue ? uc.Value * uvScale : UV(c, nn));
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 hint, Vector2[] uv = null)
            {
                if (uv == null) { Tri(a, b, c, hint); Tri(a, c, d, hint); }
                else { Tri(a, b, c, hint, uv[0], uv[1], uv[2]); Tri(a, c, d, hint, uv[0], uv[2], uv[3]); }
            }
            Vector2 UV(Vector3 p, Vector3 nn)
            {
                if (Mathf.Abs(nn.y) > 0.7f) return new Vector2(p.x, p.z) * uvScale;
                var tg = new Vector3(-nn.z, 0, nn.x).normalized; return new Vector2(Vector3.Dot(p, tg), p.y) * uvScale;
            }
            public Mesh Build(string name)
            {
                var m = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, u); m.SetTriangles(t, 0); m.RecalculateBounds(); m.RecalculateTangents();
                return m;
            }
        }

        // Box with chamfered edges (2–4.5 cm) and metric UVs, cached by size; this is what every "block" is built from.
        static readonly Dictionary<string, Mesh> boxCache = new Dictionary<string, Mesh>();
        static Mesh BevelBox(Vector3 s)
        {
            s = new Vector3(Mathf.Max(0.02f, Mathf.Abs(s.x)), Mathf.Max(0.02f, Mathf.Abs(s.y)), Mathf.Max(0.02f, Mathf.Abs(s.z)));
            string key = $"{s.x:F2}_{s.y:F2}_{s.z:F2}"; if (boxCache.TryGetValue(key, out var cached)) return cached;
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2, c = Mathf.Min(0.045f, 0.22f * Mathf.Min(hx, Mathf.Min(hy, hz)));
            var mb = new MB { uvScale = Trim }; int[] S = { -1, 1 };
            Vector3 PX(int x, int y, int z) => new Vector3(x * hx, y * (hy - c), z * (hz - c));
            Vector3 PY(int x, int y, int z) => new Vector3(x * (hx - c), y * hy, z * (hz - c));
            Vector3 PZ(int x, int y, int z) => new Vector3(x * (hx - c), y * (hy - c), z * hz);
            foreach (var a in S)
            {
                mb.Quad(PX(a, 1, 1), PX(a, 1, -1), PX(a, -1, -1), PX(a, -1, 1), new Vector3(a, 0, 0));
                mb.Quad(PY(1, a, 1), PY(1, a, -1), PY(-1, a, -1), PY(-1, a, 1), new Vector3(0, a, 0));
                mb.Quad(PZ(1, 1, a), PZ(1, -1, a), PZ(-1, -1, a), PZ(-1, 1, a), new Vector3(0, 0, a));
                foreach (var b in S)
                {
                    mb.Quad(PY(-1, a, b), PY(1, a, b), PZ(1, a, b), PZ(-1, a, b), new Vector3(0, a, b));
                    mb.Quad(PX(a, -1, b), PX(a, 1, b), PZ(a, 1, b), PZ(a, -1, b), new Vector3(a, 0, b));
                    mb.Quad(PX(a, b, -1), PX(a, b, 1), PY(a, b, 1), PY(a, b, -1), new Vector3(a, b, 0));
                    foreach (var d in S) mb.Tri(PX(a, b, d), PY(a, b, d), PZ(a, b, d), new Vector3(a, b, d));
                }
            }
            var m = mb.Build("BevelBox_" + key); boxCache[key] = m; return m;
        }

        // Annular prism between radii r0..r1 and heights y0..y1, Unity polar angles t0..t1, with arc-length UVs.
        static Mesh ArcPrism(string name, float r0, float r1, float y0, float y1, float t0, float t1, bool ends = true, float uvs = Trim, float step = 2f, bool inner = true)
        {
            var mb = new MB { uvScale = uvs }; int n = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(t1 - t0) / step));
            Vector3 Y0 = Vector3.up * y0, Y1 = Vector3.up * y1;
            for (int i = 0; i < n; i++)
            {
                float ta = Mathf.Lerp(t0, t1, i / (float)n), tb = Mathf.Lerp(t0, t1, (i + 1) / (float)n);
                Vector3 da = Polar(ta, 1), db = Polar(tb, 1), mid = (da + db).normalized;
                float sa0 = ta * Mathf.Deg2Rad * r0, sb0 = tb * Mathf.Deg2Rad * r0, sa1 = ta * Mathf.Deg2Rad * r1, sb1 = tb * Mathf.Deg2Rad * r1;
                mb.Quad(da * r0 + Y1, db * r0 + Y1, db * r1 + Y1, da * r1 + Y1, Vector3.up);
                if (inner) mb.Quad(da * r0 + Y0, db * r0 + Y0, db * r0 + Y1, da * r0 + Y1, -mid, new[] { new Vector2(sa0, y0), new Vector2(sb0, y0), new Vector2(sb0, y1), new Vector2(sa0, y1) });
                mb.Quad(da * r1 + Y0, db * r1 + Y0, db * r1 + Y1, da * r1 + Y1, mid, new[] { new Vector2(sa1, y0), new Vector2(sb1, y0), new Vector2(sb1, y1), new Vector2(sa1, y1) });
            }
            if (ends)
                foreach (var (tt, s) in new[] { (t0, -1f), (t1, 1f) })
                {
                    float a = tt * Mathf.Deg2Rad; var d = Polar(tt, 1); var tan = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a)) * s * Mathf.Sign(t1 - t0);
                    mb.Quad(d * r0 + Y0, d * r1 + Y0, d * r1 + Y1, d * r0 + Y1, tan);
                }
            return mb.Build(name);
        }

        // Sweeps a moulding profile (absolute radius, height), listed inner-bottom → over the top → outer-bottom, around
        // Unity polar angles t0..t1. Faceted across the profile (2–3 tone bands), smooth enough along the ring.
        static Mesh SweepRing(string name, Vector2[] prof, float t0, float t1, float step = 1.5f, bool ends = true)
        {
            var mb = new MB { uvScale = Trim }; int n = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(t1 - t0) / step));
            var acc = new float[prof.Length]; for (int j = 1; j < prof.Length; j++) acc[j] = acc[j - 1] + (prof[j] - prof[j - 1]).magnitude;
            for (int i = 0; i < n; i++)
            {
                float ta = Mathf.Lerp(t0, t1, i / (float)n), tb = Mathf.Lerp(t0, t1, (i + 1) / (float)n);
                Vector3 da = Polar(ta, 1), db = Polar(tb, 1), mid = (da + db).normalized;
                for (int j = 0; j + 1 < prof.Length; j++)
                {
                    Vector2 p0 = prof[j], p1 = prof[j + 1], d2 = p1 - p0; var n2 = new Vector2(-d2.y, d2.x);
                    var hint = mid * n2.x + Vector3.up * n2.y; float sa = ta * Mathf.Deg2Rad * 14, sb = tb * Mathf.Deg2Rad * 14;
                    mb.Quad(da * p0.x + Vector3.up * p0.y, db * p0.x + Vector3.up * p0.y, db * p1.x + Vector3.up * p1.y, da * p1.x + Vector3.up * p1.y, hint,
                        new[] { new Vector2(sa, acc[j]), new Vector2(sb, acc[j]), new Vector2(sb, acc[j + 1]), new Vector2(sa, acc[j + 1]) });
                }
            }
            if (ends)
            {
                var cen = prof.Aggregate(Vector2.zero, (a, b) => a + b) / prof.Length;
                foreach (var (tt, s) in new[] { (t0, -1f), (t1, 1f) })
                {
                    float a = tt * Mathf.Deg2Rad; var d = Polar(tt, 1); var tan = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a)) * s * Mathf.Sign(t1 - t0);
                    var c3 = d * cen.x + Vector3.up * cen.y;
                    for (int j = 0; j + 1 < prof.Length; j++) mb.Tri(c3, d * prof[j].x + Vector3.up * prof[j].y, d * prof[j + 1].x + Vector3.up * prof[j + 1].y, tan);
                    mb.Tri(c3, d * prof[prof.Length - 1].x + Vector3.up * prof[prof.Length - 1].y, d * prof[0].x + Vector3.up * prof[0].y, tan);
                }
            }
            return mb.Build(name);
        }

        // Flat disc, UVs mapped to plan coordinates over [-h, h] (u = screen-right = −x, v = north = −z).
        static Mesh Disc(string name, float r, int seg, float h)
        {
            var v = new List<Vector3> { Vector3.zero }; var uv = new List<Vector2> { new Vector2(0.5f, 0.5f) }; var t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2; var p = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                v.Add(p); uv.Add(new Vector2(-p.x / (2 * h) + 0.5f, -p.z / (2 * h) + 0.5f));
                if (i < seg) { t.Add(0); t.Add(i + 2); t.Add(i + 1); }
            }
            var m = new Mesh { name = name }; m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.SetNormals(Enumerable.Repeat(Vector3.up, v.Count).ToList()); m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }

        // Torus around local Y (ring in the XZ plane), optional arc from startDeg spanning arcDeg (Unity polar angles).
        static Mesh Torus(string name, float rad, float tube, int segU, int segV, float arcDeg = 360, float startDeg = 0, Vector3? stretch = null)
        {
            var v = new List<Vector3>(); var nrm = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            var s = stretch ?? Vector3.one;
            for (int i = 0; i <= segU; i++)
            {
                float a = (startDeg + arcDeg * i / segU) * Mathf.Deg2Rad; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                for (int j = 0; j <= segV; j++)
                {
                    float b = j / (float)segV * Mathf.PI * 2; var nn = d * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                    v.Add(Vector3.Scale(d * rad + nn * tube, s)); nrm.Add(Vector3.Scale(nn, new Vector3(1 / s.x, 1 / s.y, 1 / s.z)).normalized);
                    uv.Add(new Vector2(i / (float)segU * rad * Mathf.Deg2Rad * arcDeg * Trim, j / (float)segV));
                }
            }
            for (int i = 0; i < segU; i++) for (int j = 0; j < segV; j++)
                {
                    int a = i * (segV + 1) + j, b = a + segV + 1;
                    Wind(t, v, nrm, a, b, a + 1); Wind(t, v, nrm, b, b + 1, a + 1);
                }
            var m = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(v); m.SetNormals(nrm); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }
        static void Wind(List<int> t, List<Vector3> v, List<Vector3> n, int a, int b, int c)
        {
            var cr = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (Vector3.Dot(cr, n[a] + n[b] + n[c]) < 0) { t.Add(a); t.Add(c); t.Add(b); } else { t.Add(a); t.Add(b); t.Add(c); }
        }

        // Lathe around Y: profile points (radius, height); phi measured from +Z (x = sin, z = cos), so phi = 0 faces south.
        // gapHalf(j) gives each profile row its own break half-angle around phi = 0 (0 = closed), which makes a jagged break.
        // uv.x runs 0..1 across the covered angle, uv.y 0..1 down the profile. Outer normals are forced outward.
        static Mesh Lathe(string name, Vector2[] prof, int seg, System.Func<int, float> gapHalf = null)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>(); int k = prof.Length;
            for (int i = 0; i <= seg; i++)
                for (int j = 0; j < k; j++)
                {
                    float g = gapHalf?.Invoke(j) ?? 0, ph = g + (Mathf.PI * 2 - 2 * g) * i / seg;
                    v.Add(new Vector3(Mathf.Sin(ph) * prof[j].x, prof[j].y, Mathf.Cos(ph) * prof[j].x)); uv.Add(new Vector2(i / (float)seg, j / (k - 1f)));
                }
            for (int i = 0; i < seg; i++) for (int j = 0; j < k - 1; j++)
                {
                    int a = i * k + j, b = a + k;
                    t.Add(a); t.Add(b); t.Add(a + 1); t.Add(b); t.Add(b + 1); t.Add(a + 1);
                }
            var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 }; m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateNormals();
            { var nn = m.normals; int vi = (seg / 2) * k + Mathf.Min(k / 4, k - 1); var radial = new Vector3(v[vi].x, 0, v[vi].z);
              if (Vector3.Dot(nn[vi], radial) < 0) { for (int q = 0; q < t.Count; q += 3) (t[q + 1], t[q + 2]) = (t[q + 2], t[q + 1]); m.SetTriangles(t, 0); m.RecalculateNormals(); } }
            m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }

        // n-sided pyramid / cone, base at y = 0, apex at y = h (negative h points down).
        static Mesh Pyramid(string name, float baseR, float h, int sides = 4, float rot = 45, float jitter = 0)
        {
            var mb = new MB { uvScale = Trim }; var apex = Vector3.up * h; var ring = new Vector3[sides];
            for (int i = 0; i < sides; i++) { float a = (rot + i * 360f / sides) * Mathf.Deg2Rad, j = 1 + (jitter > 0 ? Rr(-jitter, jitter) : 0); ring[i] = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * baseR * j; }
            for (int i = 0; i < sides; i++)
            {
                var a = ring[i]; var b = ring[(i + 1) % sides]; var mid = (a + b) * 0.5f;
                mb.Tri(a, b, apex, new Vector3(mid.x, 0, mid.z).normalized + Vector3.up * Mathf.Sign(h) * 0.2f);
                mb.Tri(Vector3.zero, b, a, Vector3.down * Mathf.Sign(h));
            }
            return mb.Build(name);
        }

        static Mesh Rock(string name, float jitter)
        {
            float g = (1 + Mathf.Sqrt(5)) / 2;
            var p = new List<Vector3> { new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0), new Vector3(0, -1, g), new Vector3(0, 1, g),
                new Vector3(0, -1, -g), new Vector3(0, 1, -g), new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1) };
            for (int i = 0; i < p.Count; i++) p[i] = p[i].normalized * (1 + Rr(-jitter, jitter));
            int[] f = { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
            var mb = new MB { uvScale = 0.5f }; var sq = new Vector3(1, Rr(0.55f, 0.9f), 1);
            for (int i = 0; i < f.Length; i += 3) { Vector3 a = Vector3.Scale(p[f[i]], sq), b = Vector3.Scale(p[f[i + 1]], sq), c = Vector3.Scale(p[f[i + 2]], sq); mb.Tri(a, b, c, a + b + c); }
            return mb.Build(name);
        }

        // Inverted jagged spire: rings from radius r at y = 0 down to a point at y = -depth.
        static Mesh Spire(string name, float r, float depth, int sides, int rings, float jitter)
        {
            var mb = new MB { uvScale = 0.25f }; var pts = new Vector3[rings + 1][];
            for (int k = 0; k <= rings; k++)
            {
                float f = k / (float)rings, rr = r * (1 - f) * (1 - f * 0.15f); pts[k] = new Vector3[sides];
                for (int i = 0; i < sides; i++)
                {
                    float a = i * Mathf.PI * 2 / sides + (k % 2) * 0.2f, j = k == 0 ? 1 : 1 + Rr(-jitter, jitter);
                    pts[k][i] = new Vector3(Mathf.Cos(a) * rr * j, -depth * f + (k > 0 && k < rings ? Rr(-0.6f, 0.6f) : 0), Mathf.Sin(a) * rr * j);
                }
            }
            for (int k = 0; k < rings; k++) for (int i = 0; i < sides; i++)
                {
                    int i2 = (i + 1) % sides; var mid = (pts[k][i] + pts[k][i2]) * 0.5f; var hint = new Vector3(mid.x, 0, mid.z);
                    mb.Quad(pts[k][i], pts[k][i2], pts[k + 1][i2], pts[k + 1][i], hint);
                }
            for (int i = 0; i < sides; i++) mb.Tri(Vector3.zero, pts[0][i], pts[0][(i + 1) % sides], Vector3.up);
            return mb.Build(name);
        }

        // Banner cloth with two gentle vertical folds (double-sided material), centred on its origin, facing ±Z.
        static Mesh BannerMesh(float w, float h)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>(); const int cols = 8, rows = 3;
            for (int y = 0; y <= rows; y++) for (int x = 0; x <= cols; x++)
                {
                    float fx = x / (float)cols, fy = y / (float)rows;
                    v.Add(new Vector3((fx - 0.5f) * w, (fy - 0.5f) * h, 0.07f * Mathf.Sin(fx * Mathf.PI * 4) * (0.6f + 0.4f * (1 - fy)))); uv.Add(new Vector2(fx, fy));
                }
            for (int y = 0; y < rows; y++) for (int x = 0; x < cols; x++) { int a = y * (cols + 1) + x, b = a + cols + 1; t.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 }); }
            var m = new Mesh { name = "Banner" }; m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }

        static GameObject Group(string name, Transform parent, Vector3 pos = default, Quaternion? rot = null)
        {
            var g = new GameObject(name); g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localRotation = rot ?? Quaternion.identity; return g;
        }
        static GameObject Prim(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material mat, Vector3 euler = default, string name = null)
        {
            var g = GameObject.CreatePrimitive(type); Object.DestroyImmediate(g.GetComponent<Collider>());
            g.name = name ?? type.ToString(); g.transform.SetParent(parent, false);
            g.transform.localPosition = pos; g.transform.localEulerAngles = euler; g.transform.localScale = scale;
            g.GetComponent<MeshRenderer>().sharedMaterial = mat; return g;
        }
        static GameObject Box(Transform parent, Vector3 pos, Vector3 size, Material mat, Vector3 euler = default, string name = "Block")
            => MeshGO(name, BevelBox(size), parent, pos, Quaternion.Euler(euler), mat);
        static GameObject MeshGO(string name, Mesh mesh, Transform parent, Vector3 pos, Quaternion rot, params Material[] mats)
        {
            var g = Group(name, parent, pos, rot);
            g.AddComponent<MeshFilter>().sharedMesh = mesh; g.AddComponent<MeshRenderer>().sharedMaterials = mats; return g;
        }
        static GameObject MeshGO(string name, Mesh mesh, Transform parent, Vector3 pos, Material mat) => MeshGO(name, mesh, parent, pos, Quaternion.identity, mat);

        // Merges a temporary hierarchy into one mesh with one sub-mesh per material, then deletes the hierarchy.
        static Mesh Combine(GameObject root, Material[] mats, string name)
        {
            var m = MergeRenderers(root.transform, root.GetComponentsInChildren<MeshRenderer>().ToList(), ref mats, name);
            Object.DestroyImmediate(root); return m;
        }
        static Mesh MergeRenderers(Transform space, List<MeshRenderer> rends, ref Material[] mats, string name)
        {
            if (mats == null) mats = rends.SelectMany(r => r.sharedMaterials).Where(x => x).Distinct().ToArray();
            var lists = mats.Select(_ => new List<CombineInstance>()).ToArray(); var w2l = space.worldToLocalMatrix;
            foreach (var r in rends)
            {
                var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue; var sm = r.sharedMaterials;
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
                {
                    int k = System.Array.IndexOf(mats, sm[Mathf.Min(s, sm.Length - 1)]); if (k < 0) k = 0;
                    lists[k].Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = s, transform = w2l * r.transform.localToWorldMatrix });
                }
            }
            var keep = Enumerable.Range(0, mats.Length).Where(i => lists[i].Count > 0).ToArray();
            var all = mats; mats = keep.Select(i => all[i]).ToArray();
            var subs = keep.Select(i => { var m = new Mesh { indexFormat = IndexFormat.UInt32 }; m.CombineMeshes(lists[i].ToArray(), true, true); return new CombineInstance { mesh = m, transform = Matrix4x4.identity }; }).ToArray();
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 }; mesh.CombineMeshes(subs, false, false);
            mesh.RecalculateBounds(); return mesh;
        }

        // Replaces every static renderer under `group` (except fade targets, skipped subtrees and lights) with one merged mesh.
        static GameObject Consolidate(Transform group, string name, System.Func<Transform, bool> skip = null, bool insideFade = false)
        {
            foreach (var l in group.GetComponentsInChildren<Light>(true)) if (l.transform != group) l.transform.SetParent(group, true);
            var rends = group.GetComponentsInChildren<MeshRenderer>().Where(r => !(skip?.Invoke(r.transform) ?? false) && (insideFade || !fadeTargets.Any(f => r.transform.IsChildOf(f.transform)))).ToList();
            if (rends.Count < 2) return null;
            Material[] mats = null; var mesh = MergeRenderers(group, rends, ref mats, name);
            foreach (var r in rends) { var go = r.gameObject; var mf = go.GetComponent<MeshFilter>(); Object.DestroyImmediate(r); if (mf) Object.DestroyImmediate(mf); }
            Prune(group);
            var g = MeshGO(name, mesh, group, Vector3.zero, Quaternion.identity, mats);
            return g;
        }
        static void Prune(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var c = t.GetChild(i); Prune(c);
                if (c.childCount == 0 && c.GetComponents<Component>().Length == 1) Object.DestroyImmediate(c.gameObject);
            }
        }

        // Equilateral pointed arch from bevelled segments: springs at ±span/2 at height springY (local XY plane).
        static void PointedArch(Transform parent, float span, float springY, float thick, float depth, Material mat, int segs = 6, float keep = 1)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                var c = new Vector2(-side * span / 2, springY);
                for (int i = 0; i < segs; i++)
                {
                    if ((i + 1) / (float)segs > keep) break;
                    // left arc (centred on the right spring) sweeps 180° → 120°, right arc 0° → 60°: both rise to the apex
                    float a0 = side < 0 ? 180 - 60f * i / segs : 60f * i / segs, a1 = side < 0 ? 180 - 60f * (i + 1) / segs : 60f * (i + 1) / segs;
                    var p0 = c + span * new Vector2(Mathf.Cos(a0 * Mathf.Deg2Rad), Mathf.Sin(a0 * Mathf.Deg2Rad));
                    var p1 = c + span * new Vector2(Mathf.Cos(a1 * Mathf.Deg2Rad), Mathf.Sin(a1 * Mathf.Deg2Rad));
                    var mid = (p0 + p1) / 2; var d = p1 - p0;
                    Box(parent, new Vector3(mid.x, mid.y, 0), new Vector3(d.magnitude + thick * 0.4f, thick, depth), mat, new Vector3(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg), "ArchSeg");
                }
            }
        }
        // Small tracery: a mullion and a rose ring in a pointed opening (local XY plane, facing +Z).
        static void Tracery(Transform parent, float span, float springY, float z, Material mat)
        {
            Box(parent, new Vector3(0, springY * 0.55f, z), new Vector3(0.06f, springY * 1.1f, 0.07f), mat, default, "Mullion");
            MeshGO("Rose", Torus("TraceryRose", span * 0.18f, 0.025f, 24, 6), parent, new Vector3(0, springY + span * 0.42f, z), Quaternion.Euler(90, 0, 0), mat);
        }

        // ---------------------------------------------------------------- materials
        static Shader lit;
        static Material Lit(string name, Color c, float metal = 0, float smooth = 0.2f, Color? emis = null, bool dbl = false, Texture2D map = null, Texture2D bump = null, float bumpScale = 1)
        {
            var m = new Material(lit) { name = name, enableInstancing = true };
            m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
            if (map) m.SetTexture("_BaseMap", map);
            if (bump) { m.SetTexture("_BumpMap", bump); m.SetFloat("_BumpScale", bumpScale); m.EnableKeyword("_NORMALMAP"); }
            if (emis.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emis.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; }
            if (dbl) { m.SetFloat("_Cull", 0); m.doubleSidedGI = true; }
            return Save(m, name + ".mat");
        }
        static void MakeTransparent(Material m, bool unlit = false)
        {
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0); m.SetFloat("_BlendModePreserveSpecular", 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent; m.SetShaderPassEnabled("DepthOnly", false); m.SetShaderPassEnabled("ShadowCaster", unlit);
        }
        static Material FadedTwin(Material src)
        {
            var m = new Material(src) { name = src.name + "_Fade" }; MakeTransparent(m); return Save(m, m.name + ".mat");
        }

        static Material mAstroBrass, mIvory, mFloor, mStone, mStoneCap, mStoneDark, mDeck, mBrass, mBrassPolished, mIron, mBronze, mBronzePlain, mCrack, mGroove, mBellGlow, mFlame, mGlass,
            mRock, mRockLight, mBanner, mStation, mPedestal, mWindow, mWindowGlow, mVortex, mShaft, mCone, mSky;
        static Color floorEmission, bellGlow;

        // ---------------------------------------------------------------- textures
        const int FN = 4096;
        static float Hash(int x, int y, int s)
        {
            unchecked { uint h = (uint)(x * 374761393 + y * 668265263 + s * 1442695041); h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16; return (h & 0xFFFFFF) / 16777216f; }
        }
        static float VNoise(float x, float y, int s = 0)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y); float xf = x - xi, yf = y - yi, u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
            float a = Hash(xi, yi, s), b = Hash(xi + 1, yi, s), c = Hash(xi, yi + 1, s), d = Hash(xi + 1, yi + 1, s);
            return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
        }
        // tileable value noise over a period (in noise cells)
        static float TNoise(float x, float y, int period, int s = 0)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y); float xf = x - xi, yf = y - yi, u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
            int x0 = ((xi % period) + period) % period, y0 = ((yi % period) + period) % period, x1 = (x0 + 1) % period, y1 = (y0 + 1) % period;
            float a = Hash(x0, y0, s), b = Hash(x1, y0, s), c = Hash(x0, y1, s), d = Hash(x1, y1, s);
            return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
        }
        static float Fbm(float x, float y) => VNoise(x, y) * 0.55f + VNoise(x * 2.1f, y * 2.1f, 1) * 0.3f + VNoise(x * 4.3f, y * 4.3f, 2) * 0.15f;
        static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }

        static Texture2D WritePng(string file, Color32[] px, int w, int h, bool alpha = false)
        {
            var tex = new Texture2D(w, h, alpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, false); tex.SetPixels32(px); tex.Apply();
            string path = Gen + "/" + file; File.WriteAllBytes(Path.GetFullPath(path), tex.EncodeToPNG()); Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static Color32 C32(Color c) => new Color32((byte)Mathf.Clamp(c.r * 255, 0, 255), (byte)Mathf.Clamp(c.g * 255, 0, 255), (byte)Mathf.Clamp(c.b * 255, 0, 255), (byte)Mathf.Clamp(c.a * 255, 0, 255));
        static Color32 G32(float h) { byte b = (byte)Mathf.Clamp(h * 255, 0, 255); return new Color32(b, b, b, 255); }

        // Stone trim sheet: 2.4 m tile, four 0.6 m courses of staggered chamfered blocks with chips, joint AO and broad value shifts.
        static (Texture2D, Texture2D) PaintStoneTrim()
        {
            const int N = 1024, row = 256; var alb = new Color32[N * N]; var hgt = new Color32[N * N]; var r = new System.Random(3);
            var cuts = new List<int>[4];
            for (int k = 0; k < 4; k++) { cuts[k] = new List<int>(); int x = r.Next(200); while (cuts[k].Count == 0 || x < N + cuts[k][0]) { cuts[k].Add(x); x += 220 + r.Next(190); } }
            Color a = Hex(0x2B2A3A), b = Hex(0x3C3A50), mortar = Hex(0x17161F);
            Parallel.For(0, N, y =>
            {
                int k = y / row; float fy = y % row; var cs = cuts[k];
                for (int x = 0; x < N; x++)
                {
                    int xx = x < cs[0] ? x + N : x, bi = 0; while (bi + 1 < cs.Count && xx >= cs[bi + 1]) bi++;
                    float l = xx - cs[bi], rr = (bi + 1 < cs.Count ? cs[bi + 1] : cs[0] + N) - xx, e = Mathf.Min(Mathf.Min(fy, row - fy), Mathf.Min(l, rr));
                    int id = k * 100 + (bi % cs.Count); float t = Hash(id, 7, 3), n = TNoise(x / 64f, y / 64f, 16, 4), f = TNoise(x / 9f, y / 9f, 114, 5);
                    var c = Color.Lerp(a, b, t * 0.55f + n * 0.3f) * (0.94f + 0.12f * f);
                    c *= 1 + 0.06f * ((l / (l + rr)) - 0.5f);                                  // broad planar shift across each block
                    float h = 0.55f + 0.45f * Smooth(2, 14, e) + (n - 0.5f) * 0.08f;
                    if (e < 14) { bool lit = fy > row - 14 || l < 14; c *= lit ? 1.16f : 0.8f; }  // chamfer catches the light top-left
                    float chip = TNoise(x / 20f + id, y / 20f, 51, 6);
                    if (chip < 0.12f && e < 40) { c *= 0.82f; h -= 0.15f; }
                    if (e < 4) { c = Color.Lerp(mortar, c, e / 8f); h = 0.1f; }
                    else if (e < 18) c *= Mathf.Lerp(0.82f, 1, (e - 4) / 14f);                  // soft AO into the joints
                    c.a = 1; alb[y * N + x] = C32(c); hgt[y * N + x] = G32(h);
                }
            });
            return (WritePng("StoneTrim.png", alb, N, N), WritePng("StoneTrimHeight.png", hgt, N, N));
        }

        static Texture2D PaintBrass()
        {
            const int N = 512; var px = new Color32[N * N]; Color baseC = Color.white, umber = new Color(0.62f, 0.56f, 0.42f);
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                {
                    float m = TNoise(x / 64f, y / 64f, 8, 8), s = TNoise(x / 4f, y / 90f, 128, 9);
                    var c = Color.Lerp(baseC, umber, Smooth(0.45f, 0.8f, m) * 0.25f) * (0.95f + 0.06f * s); c.a = 1; px[y * N + x] = C32(c);
                }
            return WritePng("BrassBreakup.png", px, N, N);
        }

        static (Texture2D, Texture2D) PaintStrata()
        {
            const int N = 512; var alb = new Color32[N * N]; var hgt = new Color32[N * N];
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                {
                    float warp = TNoise(x / 64f, y / 64f, 8, 10) * 24, band = TNoise(0.5f, (y + warp) / 22f, 23, 11), f = TNoise(x / 12f, y / 12f, 42, 12);
                    float v = 0.78f + 0.4f * band + 0.08f * f; var c = new Color(v, v * 0.97f, v * 1.04f); c.a = 1;
                    alb[y * N + x] = C32(c); hgt[y * N + x] = G32(0.5f + 0.35f * band + 0.1f * f);
                }
            return (WritePng("RockStrata.png", alb, N, N), WritePng("RockStrataHeight.png", hgt, N, N));
        }

        // Fissure polylines in plan coordinates (u = screen-right, v = north) with per-point width.
        static List<(Vector2 p, float w)[]> Fissure()
        {
            var r = new System.Random(19); float F() => (float)r.NextDouble();
            var lines = new List<(Vector2, float)[]>();
            (Vector2, float)[] Walk(Vector2 from, Vector2 to, float w0, float w1)
            {
                // irregular crack: the heading drifts and occasionally kinks hard, steps vary 0.12–0.55 m, width wanders 0.12–0.22 m
                var pts = new List<(Vector2, float)> { (from, w0) }; var p = from; float total = (to - from).magnitude, drift = 0, seed = F() * 50;
                for (int guard = 0; guard < 300 && (to - p).magnitude > 0.4f; guard++)
                {
                    drift = drift * 0.6f + (F() * 2 - 1) * 0.5f; if (F() < 0.15f) drift += (F() < 0.5f ? 1 : -1) * 0.9f;
                    var dir = (to - p).normalized; float ang = Mathf.Clamp(drift, -1.1f, 1.1f), step = 0.12f + F() * F() * 0.45f;
                    dir = new Vector2(dir.x * Mathf.Cos(ang) - dir.y * Mathf.Sin(ang), dir.x * Mathf.Sin(ang) + dir.y * Mathf.Cos(ang));
                    p += dir * step; float f = 1 - (to - p).magnitude / total, wn = VNoise(seed + f * 9, 0.5f, 21);
                    pts.Add((p, Mathf.Lerp(w0, w1, f) * (0.75f + 0.55f * wn)));
                }
                pts.Add((to, w1)); return pts.ToArray();
            }
            Vector2 nw = new Vector2(-1, 1).normalized, se = -nw;
            var a = Walk(nw * 12.3f, nw * 3.2f + new Vector2(0.25f, 0.1f), 0.12f, 0.17f);
            var b = Walk(se * 3.2f + new Vector2(-0.1f, -0.25f), se * 12.3f, 0.17f, 0.12f);
            lines.Add(a); lines.Add(b);
            foreach (var main in new[] { a, b })
                for (int k = 0; k < 2; k++) // two short hairline branches each, tapering to nothing
                {
                    var (p0, w) = main[4 + (int)(F() * (main.Length - 9))];
                    var mdir = (main[main.Length - 1].Item1 - main[0].Item1).normalized; float side = k == 0 ? 1 : -1;
                    var dir = new Vector2(-mdir.y, mdir.x) * side * 0.7f + mdir * 0.7f; float len = 0.5f + F() * 0.5f;
                    var br = Walk(p0, p0 + dir.normalized * len, 0.08f, 0.08f); for (int q = 0; q < br.Length; q++) br[q].Item2 = Mathf.Lerp(w * 0.45f, 0.015f, q / (br.Length - 1f));
                    lines.Add(br);
                }
            return lines;
        }

        // Precomputed floor slab layout: polar rings of slabs inside r 7.5, running-bond rows outside.
        static readonly float[] polarRows = { 3.2f, 4.25f, 5.35f, 6.45f, 7.5f };
        static List<float>[] polarCuts;
        static void LayoutSlabs()
        {
            var r = new System.Random(77); float F() => (float)r.NextDouble();
            polarCuts = new List<float>[polarRows.Length - 1];
            for (int k = 0; k + 1 < polarRows.Length; k++)
            {
                float rm = (polarRows[k] + polarRows[k + 1]) / 2, circ = Mathf.PI * 2 * rm; var lens = new List<float>(); float s = 0;
                while (s < circ - 1.4f) { float l = 1.5f + F(); lens.Add(l); s += l; }
                float scale = Mathf.PI * 2 / s, off = F() * Mathf.PI * 2, acc = off; polarCuts[k] = new List<float>();
                foreach (var l in lens) { polarCuts[k].Add(Mathf.Repeat(acc, Mathf.PI * 2)); acc += l * scale; }
                polarCuts[k].Sort();
            }
            // outside r 7.5: irregular slabs from recursive splits (1.25–2.5 m, mixed long and short), looked up through a 0.1 m grid
            slabRects = new List<Rect>(); var stack = new Stack<Rect>(); float ext = FloorH + 1; stack.Push(new Rect(-ext, -ext, 2 * ext, 2 * ext));
            while (stack.Count > 0)
            {
                var q = stack.Pop(); float limit = 1.9f + 0.6f * F();
                if (q.width <= limit && q.height <= limit) { slabRects.Add(q); continue; }
                if (Mathf.Min(q.width, q.height) <= 1.6f && Mathf.Max(q.width, q.height) <= 3.2f && F() < 0.4f) { slabRects.Add(q); continue; } // some long slabs
                bool splitX = q.width > limit && (q.height <= limit || F() < q.width / (q.width + q.height));
                float len = splitX ? q.width : q.height, cut = len < 3f ? len / 2 : Mathf.Clamp(len * (0.3f + 0.4f * F()), 1.5f, len - 1.5f);
                if (splitX) { stack.Push(new Rect(q.xMin, q.yMin, cut, q.height)); stack.Push(new Rect(q.xMin + cut, q.yMin, q.width - cut, q.height)); }
                else { stack.Push(new Rect(q.xMin, q.yMin, q.width, cut)); stack.Push(new Rect(q.xMin, q.yMin + cut, q.width, q.height - cut)); }
            }
            int gn = Mathf.CeilToInt(2 * ext / SlabCell) + 1; slabGrid = new int[gn, gn];
            for (int i = 0; i < slabRects.Count; i++)
            {
                var q = slabRects[i];
                for (int gx = Mathf.FloorToInt((q.xMin + ext) / SlabCell); gx < Mathf.CeilToInt((q.xMax + ext) / SlabCell); gx++)
                    for (int gy = Mathf.FloorToInt((q.yMin + ext) / SlabCell); gy < Mathf.CeilToInt((q.yMax + ext) / SlabCell); gy++)
                        if (gx >= 0 && gy >= 0 && gx < gn && gy < gn) slabGrid[gx, gy] = i;
            }
        }
        static List<Rect> slabRects; static int[,] slabGrid; const float SlabCell = 0.1f;
        static int SlabAt(float u, float v)
        {
            float ext = FloorH + 1; int gx = Mathf.Clamp((int)((u + ext) / SlabCell), 0, slabGrid.GetLength(0) - 1), gy = Mathf.Clamp((int)((v + ext) / SlabCell), 0, slabGrid.GetLength(1) - 1);
            int id = slabGrid[gx, gy]; var q = slabRects[id];
            if (u < q.xMin || u > q.xMax || v < q.yMin || v > q.yMax)  // grid cell straddles a seam: check its neighbours
                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
                    {
                        int nx = Mathf.Clamp(gx + dx, 0, slabGrid.GetLength(0) - 1), ny = Mathf.Clamp(gy + dy, 0, slabGrid.GetLength(1) - 1), j = slabGrid[nx, ny];
                        var s = slabRects[j]; if (u >= s.xMin && u <= s.xMax && v >= s.yMin && v <= s.yMax) return j;
                    }
            return id;
        }

        static (Texture2D albedo, Texture2D height, Texture2D emission) PaintFloor()
        {
            LayoutSlabs();
            int N = FN; float H = FloorH, px = 2 * H / N;
            var alb = new Color32[N * N]; var hgt = new Color32[N * N]; var emi = new Color32[N * N];
            Color c1 = Hex(0x1E2433), c2 = Hex(0x2A3142), cBlue = Hex(0x252D45), grout = Hex(0x0E1018), brass = Hex(0xB8904E), plate = Hex(0x1A2036), rimStone = Hex(0x1A1E2B);
            Color gold = new Color(1f, 0.72f, 0.4f);
            Parallel.For(0, N, py =>
            {
                float v = -H + (py + 0.5f) * px;
                for (int ix = 0; ix < N; ix++)
                {
                    float u = -H + (ix + 0.5f) * px, r = Mathf.Sqrt(u * u + v * v), ang = Mathf.Atan2(v, u);
                    int o = py * N + ix;
                    // --- stone: large quiet slabs
                    float edge; int id0, id1; Color c;
                    float n = Fbm(u * 0.45f, v * 0.45f), fine = VNoise(u * 6, v * 6, 3), drift = Fbm(u * 0.12f + 7, v * 0.12f);
                    if (r > 12.6f)
                    {
                        float seg = Mathf.PI * 2 / 48, a = ang + Mathf.PI, k = Mathf.Floor(a / seg), fa = a - k * seg;
                        edge = Mathf.Min(Mathf.Min(r - 12.6f, 13.6f - r), Mathf.Min(fa * r, (seg - fa) * r)); id0 = (int)k; id1 = 99;
                        c = rimStone * (0.92f + 0.16f * Hash(id0, 5, 2));
                    }
                    else if (r < 3.2f)
                    {
                        if (r < 2.6f) { edge = 1; id0 = 0; id1 = 0; c = plate; }
                        else { float seg = Mathf.PI * 2 / 24, a = ang + Mathf.PI, k = Mathf.Floor(a / seg), fa = a - k * seg; edge = Mathf.Min(Mathf.Min(r - 2.6f, 3.2f - r), Mathf.Min(fa * r, (seg - fa) * r)); id0 = (int)k; id1 = 77; c = Hex(0x20263A); }
                    }
                    else
                    {
                        if (r < 7.5f)
                        {
                            int k = 0; while (k + 2 < polarRows.Length && r >= polarRows[k + 1]) k++;
                            float a = ang + Mathf.PI; var cuts = polarCuts[k]; int bi = cuts.Count - 1; for (int q = 0; q < cuts.Count; q++) if (a >= cuts[q]) bi = q;
                            float aStart = cuts[bi], aEnd = bi + 1 < cuts.Count ? cuts[bi + 1] : cuts[0] + Mathf.PI * 2; if (a < aStart) a += Mathf.PI * 2;
                            edge = Mathf.Min(Mathf.Min(r - polarRows[k], polarRows[k + 1] - r), Mathf.Min((a - aStart) * r, (aEnd - a) * r)); id0 = 500 + k; id1 = bi;
                        }
                        else
                        {
                            int si = SlabAt(u, v); var slab = slabRects[si];
                            edge = Mathf.Min(Mathf.Min(v - slab.yMin, slab.yMax - v), Mathf.Min(u - slab.xMin, slab.xMax - u)); edge = Mathf.Min(edge, Mathf.Abs(r - 7.5f)); id0 = si; id1 = 3;
                        }
                        float t = Hash(id0, id1, 7); c = Color.Lerp(c1, c2, 0.3f + 0.4f * t); if (Hash(id0, id1, 9) < 0.28f) c = Color.Lerp(c, cBlue, 0.85f);
                        c *= 0.89f + 0.22f * Hash(id0, id1, 13);  // ±11 % per slab
                    }
                    c *= 0.9f * (0.92f + 0.16f * drift) * (0.95f + 0.1f * n); c *= 0.97f + 0.06f * fine;
                    float hh = 0.66f + 0.34f * Smooth(0.008f, 0.05f, edge) + (n - 0.5f) * 0.06f;
                    // painted edge wear: worn-pale chips and darker nicks along slab edges
                    float wear = VNoise(u * 9 + id0, v * 9 + id1, 14);
                    if (edge < 0.07f && edge > 0.01f) { if (wear > 0.7f) c *= 1.12f; else if (wear < 0.18f) { c *= 0.86f; hh -= 0.08f; } }
                    if (fine > 0.91f) c *= 1.04f;
                    if (edge < 0.01f) { c = Color.Lerp(grout, c, 0.74f); hh = 0.5f; }
                    // --- brass inlay: flush, bevelled, with a darker engraved centre line on the wider pieces
                    float ink = 0, em = 0, engr = 0;
                    void Ink(float d, float hw, float alpha, float e)
                    {
                        float cov = Mathf.Clamp01((hw - d) / px + 0.5f) * alpha; if (cov <= 0) return;
                        if (cov > ink) ink = cov; if (cov * e > em) em = cov * e;
                        if (hw >= 0.024f && d < hw * 0.3f) engr = Mathf.Max(engr, cov);
                    }
                    foreach (var (rr, hw, al, e) in Rings) if (Mathf.Abs(r - rr) < hw + px) Ink(Mathf.Abs(r - rr), hw, al, e);
                    if (r < 0.34f) Ink(r, 0.32f, 1, 0.14f);
                    if (r > 1f && r < 2.6f)
                    {
                        float seg = Mathf.PI * 2 / 24, k = Mathf.Round(ang / seg), da = ang - k * seg; bool lng = ((int)k & 1) == 0;
                        float len = lng ? 1.6f : 1.0f, f = (r - 1f) / len;
                        if (f < 1) Ink(Mathf.Abs(Mathf.Sin(da)) * r, 0.09f * (1 - f), 0.85f, 0.14f);
                        if (lng) { float along = Mathf.Abs(r - 2.42f), across = Mathf.Abs(Mathf.Sin(da)) * r; Ink(along * 1.5f + across, 0.1f, 0.95f, 0.14f); } // fleur tip
                    }
                    if (r > 2.68f && r < 3.12f) { float seg = Mathf.PI * 2 / 72, k = Mathf.Round(ang / seg); Ink(Mathf.Abs(ang - k * seg) * r, 0.012f, 0.9f, 0.12f); } // micro-ticks
                    if (r > 3.2f && r < 12.6f) Ink(Mathf.Min(Mathf.Abs(u), Mathf.Abs(v)), 0.02f, 0.8f, 0.078f);
                    for (int q = 0; q < 4; q++)
                    {
                        float qa = q * Mathf.PI / 2; float cxu = Mathf.Cos(qa), cxv = Mathf.Sin(qa);
                        float du = u - cxu * 10.2f, dv = v - cxv * 10.2f, sr = Mathf.Sqrt(du * du + dv * dv);
                        if (sr < 0.48f) { float sa = Mathf.Atan2(dv, du); float lim = 0.42f * (0.3f + 0.7f * Mathf.Pow(Mathf.Abs(Mathf.Cos(sa * 4)), 6) * (0.7f + 0.3f * Mathf.Abs(Mathf.Cos(sa * 2)))); Ink(sr, lim, 1f, 0.094f); }
                        du = u - cxu * 6.5f; dv = v - cxv * 6.5f; float man = Mathf.Abs(du) + Mathf.Abs(dv);
                        if (man < 0.28f) Ink(man, 0.22f, 0.85f, 0.078f);
                        float da2 = qa + Mathf.PI / 4; float ou = Mathf.Cos(da2), ov = Mathf.Sin(da2);
                        float lu = u - ou * 11.4f, lv = v - ov * 11.4f, along2 = lu * ou + lv * ov, across2 = Mathf.Abs(-lu * ov + lv * ou);
                        if (along2 > -0.26f && along2 < 0.32f) Ink(across2, 0.29f * (0.32f - along2) / 0.58f, 1f, 0.078f);
                    }
                    if (r > 12.65f && r < 13.38f)
                    {
                        float seg = Mathf.PI * 2 / 48, k = Mathf.Round(ang / seg), d = Mathf.Abs(ang - k * seg) * r; bool lng = ((int)Mathf.Repeat(k, 48) % 4) == 0;
                        if (r < (lng ? 13.34f : 13.0f)) Ink(d, lng ? 0.05f : 0.034f, 1f, 0.078f);
                    }
                    var bc = brass * (0.9f + 0.2f * fine) * (1.05f - 0.1f * engr); c = Color.Lerp(c, bc, ink); c = Color.Lerp(c, brass * 0.42f, engr * 0.75f);
                    hh += ink * 0.06f - engr * 0.08f;
                    c.a = 1; alb[o] = C32(c); hgt[o] = G32(hh);
                    emi[o] = C32(new Color(gold.r * em, gold.g * em, gold.b * em, 1));
                }
            });
            // sparse hairline cracks
            var cr = new System.Random(5);
            for (int i = 0; i < 70; i++)
            {
                float x = ((float)cr.NextDouble() * 2 - 1) * 12.5f, y = ((float)cr.NextDouble() * 2 - 1) * 12.5f, a = (float)cr.NextDouble() * 6.28f;
                int steps = 6 + cr.Next(10);
                for (int s = 0; s < steps * 6; s++)
                {
                    if (s % 6 == 0) a += ((float)cr.NextDouble() - 0.5f) * 1.2f;
                    x += Mathf.Cos(a) * 0.04f; y += Mathf.Sin(a) * 0.04f; if (x * x + y * y > 12.5f * 12.5f) break;
                    int ix = (int)((x + H) / px), iy = (int)((y + H) / px), o = iy * N + ix; if (o < 0 || o >= N * N) continue;
                    var k = alb[o]; alb[o] = new Color32((byte)(k.r * 0.7f), (byte)(k.g * 0.7f), (byte)(k.b * 0.72f), 255); hgt[o] = G32(hgt[o].r / 255f * 0.75f);
                }
            }
            // the decorative fissure: signed distance to the crack edge, then charred lip, gold body, ivory core
            var sd = new float[N * N]; for (int i = 0; i < sd.Length; i++) sd[i] = 99;
            int minX = N, minY = N, maxX = 0, maxY = 0; const float lip = 0.15f; var lines = Fissure();
            foreach (var line in lines)
                for (int i = 0; i + 1 < line.Length; i++)
                {
                    var (a, wa) = line[i]; var (b, wb) = line[i + 1]; float pad = Mathf.Max(wa, wb) / 2 + lip + 0.02f;
                    int x0 = (int)((Mathf.Min(a.x, b.x) - pad + H) / px), x1 = (int)((Mathf.Max(a.x, b.x) + pad + H) / px), y0 = (int)((Mathf.Min(a.y, b.y) - pad + H) / px), y1 = (int)((Mathf.Max(a.y, b.y) + pad + H) / px);
                    minX = Mathf.Min(minX, x0); minY = Mathf.Min(minY, y0); maxX = Mathf.Max(maxX, x1); maxY = Mathf.Max(maxY, y1);
                    var ab = b - a; float l2 = Mathf.Max(1e-6f, ab.sqrMagnitude);
                    for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                        {
                            var p = new Vector2(-H + (x + 0.5f) * px, -H + (y + 0.5f) * px); float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
                            float d = (p - (a + ab * t)).magnitude - Mathf.Lerp(wa, wb, t) / 2; int o = y * N + x; if (o >= 0 && o < sd.Length && d < sd[o]) sd[o] = d;
                        }
                }
            Color lipC = Hex(0x0A0816), body = Hex(0x8A5A22), core = Hex(0xD9A050); // dark albedo: the colour comes from emission, so the lavender key light cannot tint it pink
            for (int y = Mathf.Max(0, minY); y <= Mathf.Min(N - 1, maxY); y++) for (int x = Mathf.Max(0, minX); x <= Mathf.Min(N - 1, maxX); x++)
                {
                    int o = y * N + x; float d = sd[o]; if (d > lip) continue;
                    Color c = alb[o]; float e = 0, hh = hgt[o].r / 255f;
                    if (d > 0) { float t = Smooth(0, lip, d); c = Color.Lerp(lipC, c, t * t); hh *= Mathf.Lerp(0.3f, 1, t); }
                    else if (d > -0.05f) { float t = Mathf.Clamp01(-d / 0.05f); c = Color.Lerp(body, core, t * 0.35f); e = Mathf.Lerp(0.2f, 0.6f, t); hh = 0.05f; }
                    else { c = core; e = 1; hh = 0.02f; }
                    alb[o] = C32(c); hgt[o] = G32(hh);
                    var ec = e >= 1 ? new Color(1, 0.86f, 0.6f) * 0.8f : new Color(gold.r * e, gold.g * e, gold.b * e); var old = emi[o];
                    emi[o] = C32(new Color(Mathf.Max(ec.r, old.r / 255f), Mathf.Max(ec.g, old.g / 255f), Mathf.Max(ec.b, old.b / 255f), 1));
                }
            // tiny broken stone chips along the crack lips
            var chr = new System.Random(23);
            foreach (var line in lines.Take(2))
                for (int k = 0; k < 14; k++)
                {
                    var (p, w) = line[chr.Next(1, line.Length - 1)]; float side = chr.Next(2) == 0 ? 1 : -1, ang = (float)chr.NextDouble() * 6.28f;
                    var c0 = p + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (w / 2 + 0.06f + (float)chr.NextDouble() * 0.06f) * side; float rad = 0.03f + (float)chr.NextDouble() * 0.035f;
                    bool pale = chr.Next(2) == 0;
                    for (int y = (int)((c0.y - rad + H) / px); y <= (int)((c0.y + rad + H) / px); y++) for (int x = (int)((c0.x - rad + H) / px); x <= (int)((c0.x + rad + H) / px); x++)
                        {
                            var q = new Vector2(-H + (x + 0.5f) * px, -H + (y + 0.5f) * px) - c0; float a2 = Mathf.Atan2(q.y, q.x);
                            if (q.magnitude > rad * (0.65f + 0.35f * Mathf.Abs(Mathf.Sin(a2 * 2.5f + k)))) continue; int o = y * N + x; if (o < 0 || o >= N * N) continue;
                            var kk = alb[o]; float m = pale ? 1.25f : 0.6f; alb[o] = new Color32((byte)Mathf.Min(255, kk.r * m), (byte)Mathf.Min(255, kk.g * m), (byte)Mathf.Min(255, kk.b * m), 255); hgt[o] = G32(pale ? 0.75f : 0.3f);
                        }
                }
            return (WritePng("FloorAlbedo.png", alb, N, N), WritePng("FloorHeight.png", hgt, N, N), WritePng("FloorEmission.png", emi, N, N));
        }
        // (radius, half width, alpha, emissive share of the 2.4 HDR fissure intensity)
        static readonly (float, float, float, float)[] Rings = { (0.6f, 0.025f, 1, 0.14f), (2.6f, 0.03f, 1, 0.14f), (3.2f, 0.035f, 1, 0.14f),
            (5.0f, 0.016f, 0.7f, 0.078f), (7.5f, 0.016f, 0.7f, 0.078f), (10.2f, 0.018f, 0.8f, 0.078f), (12.6f, 0.025f, 0.9f, 0.078f), (13.42f, 0.025f, 0.9f, 0.078f) };

        // Rim-station dial: 2048² engraved face (24 ticks, 8 rune glyphs, a ring of small numerals) plus its height map.
        static (Texture2D, Texture2D) PaintStation()
        {
            const int N = 2048; var alb = new Color32[N * N]; var hgt = new Color32[N * N]; Color bg = Hex(0x1A2142), brass = Hex(0xB88A48);
            var runes = new List<(Vector2, Vector2)>[8]; var gr = new System.Random(31);
            for (int g = 0; g < 8; g++) { runes[g] = new List<(Vector2, Vector2)> { (new Vector2(0, -1), new Vector2(0, 1)) }; for (int s = 0; s < 2; s++) { var a = new Vector2((float)gr.NextDouble() * 2 - 1, (float)gr.NextDouble() * 2 - 1); runes[g].Add((a, new Vector2(a.x > 0 ? -0.2f : 0.2f, (float)gr.NextDouble() * 2 - 1))); } }
            float Seg(Vector2 p, Vector2 a, Vector2 b) { var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude)); return (p - (a + ab * t)).magnitude; }
            var ringDefs = new[] { (0.95f, 0.02f), (0.9f, 0.006f), (0.78f, 0.01f), (0.55f, 0.012f), (0.42f, 0.006f), (0.3f, 0.01f) };
            Parallel.For(0, N, y =>
            {
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2 - 1, v = (y + 0.5f) / N * 2 - 1, r = Mathf.Sqrt(u * u + v * v), a = Mathf.Atan2(v, u), pw = 2f / N, ink = 0;
                    void I(float d, float hw) { ink = Mathf.Max(ink, Mathf.Clamp01((hw - d) / pw + 0.5f)); }
                    foreach (var (rr, hw) in ringDefs) I(Mathf.Abs(r - rr), hw);
                    if (r > 0.8f && r < 0.89f) { float seg = Mathf.PI * 2 / 24, k = Mathf.Round(a / seg); if (r < (((int)k & 1) == 0 ? 0.89f : 0.85f)) I(Mathf.Abs(a - k * seg) * r, 0.006f); }
                    for (int i = 0; i < 8; i++)
                    {
                        float ra = i * Mathf.PI / 4 + Mathf.PI / 8; var c = new Vector2(Mathf.Cos(ra), Mathf.Sin(ra)) * 0.665f; var q = new Vector2(u, v) - c;
                        if (q.magnitude > 0.08f) continue;
                        float rot = -ra + Mathf.PI / 2; var lq = new Vector2(q.x * Mathf.Cos(rot) - q.y * Mathf.Sin(rot), q.x * Mathf.Sin(rot) + q.y * Mathf.Cos(rot)) / 0.06f;
                        // astronomical marks instead of runes: small bell, crescent, four-point star, ringed dot
                        float L = lq.magnitude, gw = 0.1f;
                        switch (i % 4)
                        {
                            case 0: if (lq.y > -0.3f) I(Mathf.Abs((lq - new Vector2(0, -0.3f)).magnitude - 0.7f) * 0.06f, gw * 0.06f); I(Seg(lq, new Vector2(-0.75f, -0.35f), new Vector2(0.75f, -0.35f)) * 0.06f, gw * 0.06f); I((lq - new Vector2(0, -0.55f)).magnitude * 0.06f, 0.15f * 0.06f); break;
                            case 1: if ((lq - new Vector2(0.35f, 0.1f)).magnitude > 0.6f) I(Mathf.Abs(L - 0.7f) * 0.06f, gw * 1.6f * 0.06f); break;
                            case 2: for (int s = 0; s < 4; s++) { float sa = s * Mathf.PI / 4, ln = s % 2 == 0 ? 0.85f : 0.45f; var e = new Vector2(Mathf.Cos(sa), Mathf.Sin(sa)) * ln; I(Seg(lq, -e, e) * 0.06f, gw * 0.06f); } break;
                            default: I(Mathf.Abs(L - 0.65f) * 0.06f, gw * 0.06f); I(L * 0.06f, 0.18f * 0.06f); break;
                        }
                    }
                    if (r > 0.44f && r < 0.53f)
                    {
                        float seg = Mathf.PI * 2 / 12, k = Mathf.Round(a / seg), da = a - k * seg; int num = (int)Mathf.Repeat(k, 12) % 4 + 1;
                        for (int b = 0; b < num; b++) { float off = (b - (num - 1) / 2f) * 0.012f; I(Mathf.Abs(Mathf.Sin(da) * r - off), 0.003f); }
                    }
                    var col = Color.Lerp(bg * (0.85f + 0.3f * Fbm(u * 6, v * 6)), brass, ink); col.a = 1; alb[y * N + x] = C32(col);
                    hgt[y * N + x] = G32(0.7f - ink * 0.35f + (r > 0.95f ? 0.15f : 0));
                }
            });
            return (WritePng("StationDial.png", alb, N, N), WritePng("StationDialHeight.png", hgt, N, N));
        }

        static Texture2D PaintBanner()
        {
            const int W = 256, Hh = 640; var px = new Color32[W * Hh]; Color red = Hex(0x6E1A22), gold = Hex(0xC9A25A);
            for (int y = 0; y < Hh; y++) for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W, v = (y + 0.5f) / Hh; // v = 0 bottom
                    bool cut = v < 0.12f && v < 0.02f + Mathf.Abs(u - 0.5f) * 0.4f;            // swallowtail
                    var c = red * (0.85f + 0.25f * Fbm(u * 8, v * 20)); float ink = 0;
                    if (u < 0.06f || u > 0.94f || v > 0.95f) ink = 1;
                    float cu = (u - 0.5f) * W, cv = (v - 0.6f) * Hh, r = Mathf.Sqrt(cu * cu + cv * cv), a = Mathf.Atan2(cv, cu);
                    if (Mathf.Abs(r - 52) < 4 || r < 16) ink = 1;
                    if (r > 60 && r < 92) { float seg = Mathf.PI * 2 / 16, k = Mathf.Round(a / seg); if (Mathf.Abs(Mathf.Sin(a - k * seg)) * r < 5 * (1 - (r - 60) / 32)) ink = 1; }
                    if (Mathf.Abs(v - 0.28f) < 0.006f && u > 0.2f && u < 0.8f) ink = 1;
                    c = Color.Lerp(c, gold, ink); c.a = cut ? 0 : 1; px[y * W + x] = C32(c);
                }
            return WritePng("Banner.png", px, W, Hh, true);
        }

        // Bell bronze over the lathe UVs (u around, v down the profile): streaked aged bronze, darker interior, worn pale
        // lip and break edges, engraved bands with numerals and astronomical glyphs at the three band heights.
        static (Texture2D, Texture2D) PaintBell(Vector2[] prof, int lipIdx, float[] bandY)
        {
            const int W = 2048, Hh = 1024; var alb = new Color32[W * Hh]; var hgt = new Color32[W * Hh]; int K = prof.Length;
            var bandRows = bandY.Select(by => { int best = 0; for (int j = 0; j <= lipIdx; j++) if (Mathf.Abs(prof[j].y - by) < Mathf.Abs(prof[best].y - by)) best = j; return best / (K - 1f); }).ToArray();
            Color bronze = Hex(0x5E4A30), worn = Hex(0x8C6A3E), gold = Hex(0xB89050);
            Parallel.For(0, Hh, y =>
            {
                float v = (y + 0.5f) / Hh; // texture row y samples uv.y = v (0 = top of the profile)
                float j = v * (K - 1); bool inside = j > lipIdx + 1.2f;
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W, n = Fbm(u * 30, v * 10), streak = VNoise(u * 140, v * 3, 15);
                    var c = bronze * (0.78f + 0.4f * n) * (0.82f + 0.32f * streak); float h = 0.5f + 0.1f * n;
                    if (inside) c *= 0.55f;
                    float lipD = Mathf.Abs(j - lipIdx), brk = Mathf.Min(u, 1 - u);
                    if (lipD < 1.6f) c = Color.Lerp(c, worn, (1 - lipD / 1.6f) * 0.85f);
                    if (brk < 0.012f) { c = Color.Lerp(c, worn, (1 - brk / 0.012f) * 0.7f); h -= 0.1f; }
                    foreach (var br in bandRows)
                    {
                        float dv = Mathf.Abs(v - br) * Hh; if (dv > 14 && dv < 34) { c *= 0.78f; continue; } if (dv > 14) continue;
                        c = Color.Lerp(c, gold * (0.8f + 0.2f * n), 0.55f); h = 0.62f;
                        float cell = u * 48, fc = cell - Mathf.Floor(cell); int idx = (int)cell; float gx = (fc - 0.5f) * 30, gm = new Vector2(gx, dv).magnitude;
                        bool glyph = idx % 4 == 0 ? (Mathf.Abs(fc - 0.5f) < 0.16f && dv < 9) : (idx % 2 == 0 ? gm < 6.5f && gm > 3.5f : Mathf.Abs(fc - 0.5f) < 0.06f && dv < 7);
                        if (glyph) { c = gold * 1.25f; h = 0.85f; }
                        if (dv > 12) { c *= 0.7f; h = 0.4f; }
                    }
                    c.a = 1; alb[y * W + x] = C32(c); hgt[y * W + x] = G32(h);
                }
            });
            return (WritePng("BellBronze.png", alb, W, Hh), WritePng("BellBronzeHeight.png", hgt, W, Hh));
        }

        // ---------------------------------------------------------------- build
        static readonly List<Transform> spinners = new List<Transform>(), bobbers = new List<Transform>(), swingers = new List<Transform>();
        static readonly List<float> spinSpeeds = new List<float>();
        static readonly List<GameObject> fadeTargets = new List<GameObject>();

        public static void Build()
        {
            rng = new System.Random(41); names.Clear(); boxCache.Clear(); spinners.Clear(); bobbers.Clear(); swingers.Clear(); spinSpeeds.Clear(); fadeTargets.Clear();
            if (AssetDatabase.IsValidFolder(Gen)) AssetDatabase.DeleteAsset(Gen);
            AssetDatabase.CreateFolder(Dir, "Generated");
            if (!AssetDatabase.IsValidFolder(PrefabDir)) AssetDatabase.CreateFolder(Dir, "Prefabs");
            if (!AssetDatabase.IsValidFolder(ShotDir)) AssetDatabase.CreateFolder(Dir, "Shots");
            lit = Shader.Find("Universal Render Pipeline/Lit");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            MakeMaterials();

            var arena = new GameObject("LB_ENV_ShatteredHour_Arena").transform;
            BuildFloor(arena); BuildBoundary(arena); BuildRim(arena); BuildStations(arena); BuildEntrances(arena);
            var rings = Group("ClockRings", arena).transform; BuildClockRings(rings);
            BuildVoid(arena);
            var bell = new GameObject("LB_ENV_FinalToll_BrokenBell").transform; var (bellLight, shard) = BuildBell(bell);

            // occluder fade on the camera-side rim pieces
            foreach (var g in fadeTargets) Consolidate(g.transform, g.name + "Merged", null, true);
            var fades = new List<ShatteredHourOccluderFade>();
            Material[] op = { mStone, mStoneCap, mBrass, mBrassPolished, mBanner }, fd = op.Select(FadedTwin).ToArray();
            foreach (var g in fadeTargets) { var f = g.AddComponent<ShatteredHourOccluderFade>(); f.opaque = op; f.faded = fd; fades.Add(f); }

            // merge static groups into a few meshes (draw calls), then mark them static
            ConsolidateStatic(arena, bell, shard);
            foreach (var name in new[] { "Floor", "Rim", "RitualStations", "Entrances" })
                foreach (var t in arena.Find(name).GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);

            var lighting = new GameObject("Lighting").transform; BuildLighting(lighting);
            var cams = new GameObject("Cameras"); var camGo = new GameObject("Main Camera") { tag = "MainCamera" }; camGo.transform.SetParent(cams.transform, false);
            var cam = camGo.AddComponent<Camera>(); cam.nearClipPlane = 0.3f; cam.farClipPlane = 1200; cam.clearFlags = CameraClearFlags.Skybox; cam.allowHDR = true;
            var cd = cam.GetUniversalAdditionalCameraData(); cd.renderPostProcessing = true; cd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; cd.antialiasingQuality = AntialiasingQuality.High;
            var drv = cams.AddComponent<ShatteredHourArena>();
            drv.viewCamera = cam; drv.floorMaterial = mFloor; drv.floorEmission = floorEmission; drv.bellGlowMaterial = mBellGlow; drv.bellGlow = bellGlow;
            drv.bellLight = bellLight; drv.bellLightIntensity = bellLight.intensity; drv.shard = shard;
            drv.spinners = spinners.ToArray(); drv.spinSpeeds = spinSpeeds.ToArray(); drv.bobbers = bobbers.ToArray(); drv.swingers = swingers.ToArray(); drv.fades = fades.ToArray();
            drv.SetView(ShatteredHourArena.View.Gameplay);

            PersistMeshes(arena, bell);
            PrefabUtility.SaveAsPrefabAssetAndConnect(arena.gameObject, PrefabDir + "/LB_ENV_ShatteredHour_Arena.prefab", InteractionMode.AutomatedAction);
            PrefabUtility.SaveAsPrefabAssetAndConnect(bell.gameObject, PrefabDir + "/LB_ENV_FinalToll_BrokenBell.prefab", InteractionMode.AutomatedAction);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
        }

        static void ConsolidateStatic(Transform arena, Transform bell, Transform shard)
        {
            Transform F(string path) => arena.Find(path);
            Consolidate(F("Rim/Parapet"), "ParapetMerged");
            Consolidate(F("Rim/Drum"), "DrumMerged");
            Consolidate(F("Rim/Pinnacles"), "PinnaclesMerged");
            Consolidate(F("Rim/Lanterns"), "LanternsMerged");
            Consolidate(F("RitualStations"), "StationsMerged");
            foreach (var e in new[] { "Entrance_South", "Stair_North", "Bridge_East", "Bridge_West" }) Consolidate(F("Entrances/" + e), e + "Merged");
            foreach (Transform c in F("Void/Chains").Cast<Transform>().ToArray()) Consolidate(c, c.name + "Merged");
            foreach (Transform c in F("Void/Debris").Cast<Transform>().ToArray()) Consolidate(c, c.name + "Merged");
            foreach (Transform c in F("Void/FloatingSpires").Cast<Transform>().ToArray()) Consolidate(c, c.name + "Merged");
            foreach (Transform c in F("Void/HangingBells").Cast<Transform>().ToArray()) Consolidate(c, "HangingBellMerged");
            foreach (Transform c in bell.Cast<Transform>().ToArray()) if (c.name.StartsWith("BellChain")) Consolidate(c, c.name + "Merged");
            Consolidate(bell.Find("Bell"), "BellMerged", t => t == shard || t.IsChildOf(shard));
            foreach (var s in spinners) if (s) Consolidate(s, s.name + "Merged");
        }

        static void MakeMaterials()
        {
            var (alb, hgt, emi) = PaintFloor();
            floorEmission = new Color(1f, 0.72f, 0.36f) * 1.25f;
            mFloor = new Material(lit) { name = "Floor" };
            mFloor.SetTexture("_BaseMap", alb); mFloor.SetColor("_BaseColor", Color.white); mFloor.SetFloat("_Smoothness", 0.26f); mFloor.SetFloat("_Metallic", 0.05f);
            mFloor.SetTexture("_BumpMap", hgt); mFloor.SetFloat("_BumpScale", 1); mFloor.EnableKeyword("_NORMALMAP");
            mFloor.SetTexture("_EmissionMap", emi); mFloor.SetColor("_EmissionColor", floorEmission); mFloor.EnableKeyword("_EMISSION");
            mFloor.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; Save(mFloor, "Floor.mat");

            var (stone, stoneN) = PaintStoneTrim(); var brassTex = PaintBrass(); var (strata, strataN) = PaintStrata();
            mStone = Lit("StoneTrim", Color.white, 0, 0.14f, map: stone, bump: stoneN);
            mStoneCap = Lit("StoneCap", new Color(1.12f, 1.12f, 1.15f), 0, 0.18f, map: stone, bump: stoneN, bumpScale: 0.6f);
            mStoneDark = Lit("StoneDark", new Color(0.55f, 0.53f, 0.62f), 0, 0.12f, map: stone, bump: stoneN);
            mDeck = Lit("DeckStone", new Color(1.2f, 1.22f, 1.3f), 0, 0.18f, map: stone, bump: stoneN, bumpScale: 0.7f);
            mBrass = Lit("AgedBrass", Hex(0xA8844A), 0.6f, 0.45f, map: brassTex);
            mBrassPolished = Lit("PolishedBrass", Hex(0xC9A25A), 0.7f, 0.45f, map: brassTex); mAstroBrass = Lit("AstrolabeBrass", Hex(0xC38D46), 0.5f, 0.45f, Hex(0x2A1806), map: brassTex);
            mIron = Lit("Iron", Hex(0x2A2440), 0.35f, 0.3f); mIvory = Lit("PorcelainIvory", Hex(0xF4E9D1, 0.85f), 0, 0.5f);
            bellGlow = new Color(1f, 0.55f, 0.2f) * 1.1f;
            mCrack = Lit("BellCrackGlow", Color.black, 0, 0.5f, new Color(1f, 0.55f, 0.18f) * 3.0f);
            mGroove = Lit("BellCrackGroove", Hex(0x3A2614), 0.3f, 0.2f);
            mBellGlow = Lit("BellInteriorGlow", Hex(0x2A1A0A), 0, 0.3f, bellGlow, dbl: true);
            { var gp = new Color32[4 * 64]; for (int y = 0; y < 64; y++) { float f = Mathf.Pow(1 - y / 63f, 2.2f); for (int x = 0; x < 4; x++) gp[y * 4 + x] = C32(new Color(f, f, f, 1)); }
              mBellGlow.SetTexture("_EmissionMap", WritePng("BellGlowGradient.png", gp, 4, 64)); } // bright at the lip, falling off inside
            mFlame = Lit("LanternFlame", Color.black, 0, 0.5f, new Color(1f, 0.52f, 0.16f) * 2.0f);
            mGlass = Lit("LanternGlass", Hex(0x2A1A10), 0, 0.8f, new Color(1f, 0.62f, 0.26f) * 0.65f);
            mRock = Lit("VoidRock", Hex(0x2C2842), 0, 0.08f, map: strata, bump: strataN);
            mRockLight = Lit("VoidRockLight", Hex(0x3A3454), 0, 0.08f, map: strata, bump: strataN);
            mPedestal = Lit("Pedestal", Hex(0x23222F), 0, 0.25f);
            mWindow = Lit("DrumWindowDark", Hex(0x0E0A12), 0, 0.2f);
            mWindowGlow = Lit("DrumWindowGlow", Hex(0x0E0A12), 0, 0.2f, new Color(1f, 0.5f, 0.18f) * 0.15f);
            mBanner = Lit("CrimsonBanner", new Color(1.35f, 1.15f, 1.15f), 0, 0.15f, dbl: true, map: PaintBanner());
            mBanner.SetFloat("_AlphaClip", 1); mBanner.SetFloat("_Cutoff", 0.5f); mBanner.EnableKeyword("_ALPHATEST_ON"); mBanner.renderQueue = (int)RenderQueue.AlphaTest;
            var (st, stN) = PaintStation();
            mStation = Lit("StationDial", Color.white, 0.3f, 0.45f, new Color(1f, 0.72f, 0.4f) * 0.1f, map: st, bump: stN); mStation.SetTexture("_EmissionMap", st);
            mVortex = Save(new Material(Shader.Find("LastBell/Area4/ShatteredVortex")) { name = "Vortex" }, "Vortex.mat"); mVortex.SetFloat("_Opacity", 0.8f); mVortex.SetColor("_ArmA", new Color(0.32f, 0.16f, 0.66f)); mVortex.SetColor("_ArmB", new Color(0.4f, 0.5f, 1.1f)); mVortex.SetFloat("_CoreStrength", 0.35f);
            mShaft = Save(new Material(Shader.Find("LastBell/Area4/ShatteredShaft")) { name = "LightShaft" }, "LightShaft.mat"); mShaft.SetFloat("_Alpha", 0.16f); mShaft.SetColor("_Color", new Color(1f, 0.74f, 0.4f));
            mCone = Save(new Material(Shader.Find("LastBell/Area4/ShatteredShaft")) { name = "BellCone" }, "BellCone.mat"); mCone.SetFloat("_Alpha", 0.03f); mCone.SetFloat("_Fade", 1);
            mSky = Save(new Material(Shader.Find("LastBell/Area4/ShatteredVoidSky")) { name = "VoidSky" }, "VoidSky.mat");
        }

        static void BuildFloor(Transform arena)
        {
            var g = Group("Floor", arena);
            var disc = Disc("FloorDisc", R, 192, FloorH);
            var f = MeshGO("FloorDisc", disc, g.transform, Vector3.zero, mFloor); f.AddComponent<MeshCollider>().sharedMesh = disc;
            f.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            Prim(PrimitiveType.Cylinder, g.transform, new Vector3(0, 0.02f, 0), new Vector3(0.5f, 0.02f, 0.5f), mBrassPolished, default, "CentreBoss");
        }

        static void BuildBoundary(Transform arena)
        {
            var g = Group("Boundary", arena);
            for (int i = 0; i < 64; i++)
            {
                float a = i / 64f * Mathf.PI * 2; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var b = Group("Wall_" + i, g.transform, d * (R + 0.15f) + Vector3.up * 1.5f, Quaternion.LookRotation(d));
                b.AddComponent<BoxCollider>().size = new Vector3(2 * Mathf.PI * (R + 0.15f) / 64 + 0.1f, 3, 0.3f);
            }
        }

        static readonly List<(float center, float half)> Gaps = new List<(float, float)>();

        // Gothic cap moulding (absolute radius, height): inner bead, rounded top, outer drip edge.
        static readonly Vector2[] CapProfile = {
            new Vector2(R - 0.01f, RimH - 0.08f), new Vector2(R - 0.05f, RimH - 0.04f), new Vector2(R - 0.05f, RimH + 0.02f), new Vector2(R + 0.02f, RimH + 0.08f),
            new Vector2(R + 0.25f, CapY + 0.02f), new Vector2(RimOut - 0.25f, CapY + 0.02f), new Vector2(RimOut, RimH + 0.06f), new Vector2(RimOut + 0.14f, RimH - 0.01f),
            new Vector2(RimOut + 0.14f, RimH - 0.07f), new Vector2(RimOut + 0.04f, RimH - 0.09f) };

        static void BuildRim(Transform arena)
        {
            var g = Group("Rim", arena).transform;
            Gaps.Clear(); Gaps.Add((0, GapN)); Gaps.Add((180, GapS)); Gaps.Add((90, GapEW)); Gaps.Add((270, GapEW));
            foreach (var d in new[] { 45f, 135f, 225f, 315f }) Gaps.Add((d, GapSt));
            var edges = Gaps.SelectMany(x => new[] { (Mathf.Repeat(x.center - x.half, 360), false), (Mathf.Repeat(x.center + x.half, 360), true) }).OrderBy(e => e.Item1).ToList();
            var walls = Group("Parapet", g).transform; var arcade = new GameObject("tmpArcade");
            for (int i = 0; i < edges.Count; i++)
            {
                if (!edges[i].Item2) continue; float d0 = edges[i].Item1, d1 = edges[(i + 1) % edges.Count].Item1; if (d1 < d0) d1 += 360;
                float t0 = Th(d1), t1 = Th(d0); var seg = Group("Run_" + i, walls).transform; bool south = (d0 + d1) / 2 > 100 && (d0 + d1) / 2 < 260;
                int segs = Mathf.CeilToInt((t1 - t0) / 1.5f);
                MeshGO("Wall", ArcPrism("ParapetWall", R, RimOut, -0.45f, RimH - 0.08f, t0, t1), seg, Vector3.zero, mStone);
                MeshGO("Cap", SweepRing("ParapetCap", CapProfile, t0, t1), seg, Vector3.zero, mStoneCap);
                MeshGO("CapBead", Torus("CapBead", R - 0.045f, 0.03f, segs, 6, t1 - t0, t0), seg, new Vector3(0, RimH, 0), mBrass);
                MeshGO("Band", Torus("InnerBand", R - 0.005f, 0.035f, segs, 6, t1 - t0, t0), seg, new Vector3(0, 0.58f, 0), mBrass);

                // blind arcading on the outer face, 0.6 m arches every 2.2 m
                float L = RimOut * (t1 - t0) * Mathf.Deg2Rad; int n = Mathf.FloorToInt(L / 2.2f);
                for (int k = 0; k < n; k++)
                {
                    float th = t0 + (k + 0.5f) / n * (t1 - t0); var a = Group("Arch", arcade.transform, Polar(th, RimOut + 0.02f, -0.3f), Quaternion.LookRotation(Polar(th, 1))).transform;
                    foreach (var x in new[] { -0.3f, 0.3f }) Box(a, new Vector3(x, -0.02f, 0), new Vector3(0.08f, 0.52f, 0.07f), mStoneCap);
                    PointedArch(a, 0.6f, 0.24f, 0.07f, 0.07f, mStoneCap, 4);
                    Box(a, new Vector3(0, 0.15f, -0.01f), new Vector3(0.52f, 0.62f, 0.02f), mStoneDark, default, "Recess");
                }
            }
            MeshGO("BlindArcade", Combine(arcade, new[] { mStoneCap, mStoneDark }, "BlindArcade"), walls, Vector3.zero, Quaternion.identity, mStoneCap, mStoneDark);
            // drum: buttresses with set-offs and sloped caps, tracery windows (every third glows faintly), brass band with studs
            var drum = Group("Drum", g).transform;
            MeshGO("DrumWall", ArcPrism("DrumWall", RimOut - 0.4f, RimOut, -4.4f, -0.45f, 0, 360, false, Trim, 2, false), drum, Vector3.zero, mStone);
            MeshGO("DrumBand", Torus("DrumBand", RimOut + 0.04f, 0.09f, 192, 8), drum, new Vector3(0, -0.52f, 0), mBrass);
            for (int i = 0; i < 48; i++) Box(drum, Polar(i * 7.5f, RimOut + 0.12f, -0.52f), new Vector3(0.12f, 0.22f, 0.08f), mBrassPolished, new Vector3(0, 90 - i * 7.5f, 0), "Stud");
            for (int i = 0; i < 24; i++)
            {
                float deg = i * 15; var b = Group("Buttress_" + i, drum, P(deg, RimOut + 0.3f, -2.4f), Facing(deg)).transform;
                Box(b, new Vector3(0, 0.3f, 0), new Vector3(0.9f, 3.4f, 0.6f), mStone);
                Box(b, new Vector3(0, -1.0f, 0.18f), new Vector3(0.8f, 1.2f, 0.9f), mStone);
                Box(b, new Vector3(0, -1.75f, 0.3f), new Vector3(0.7f, 0.5f, 1.1f), mStone);
                Box(b, new Vector3(0, -0.35f, 0.42f), new Vector3(0.84f, 0.12f, 0.62f), mStoneCap, new Vector3(35, 0, 0), "SetOffCap");
                Box(b, new Vector3(0, 2.0f, 0.12f), new Vector3(0.95f, 0.12f, 0.5f), mStoneCap, new Vector3(30, 0, 0), "SlopedCap");
                var w = Group("Window_" + i, drum, P(deg + 7.5f, RimOut + 0.04f, -2.7f), Facing(deg + 7.5f)).transform;
                Box(w, new Vector3(0, 0.25f, -0.02f), new Vector3(1.0f, 1.9f, 0.08f), i % 3 == 0 ? mWindowGlow : mWindow, default, "Recess");
                foreach (var x in new[] { -0.52f, 0.52f }) Box(w, new Vector3(x, 0.15f, 0.03f), new Vector3(0.12f, 1.5f, 0.14f), mStoneCap, default, "Jamb");
                var arch = Group("Arch", w, new Vector3(0, 0, 0.03f)).transform; PointedArch(arch, 1.04f, 0.9f, 0.12f, 0.14f, mStoneCap, 4);
                Tracery(w, 1.0f, 0.9f, 0.05f, mBrass);
                Box(w, new Vector3(0, -0.62f, 0.08f), new Vector3(1.2f, 0.1f, 0.2f), mStoneCap, default, "Sill");
            }
            MeshGO("RockSpire", Spire("RockSpire", RimOut - 0.2f, 18, 18, 6, 0.16f), g, new Vector3(0, -4.4f, 0), mRock);
            // pinnacles (three heights), in pairs either side of each gap, plus four between
            var pin = new Dictionary<int, Mesh>(); foreach (int h in new[] { 25, 35, 45 }) pin[h] = PinnacleMesh(h / 10f);
            var spots = new List<(float deg, int h)>();
            foreach (var (c, half) in Gaps) { int h = c == 180 ? 25 : (c % 90 == 0 ? 35 : 45); spots.Add((c - half - 2.5f, h)); spots.Add((c + half + 2.5f, h)); }
            foreach (var d in new[] { 22.5f, 157.5f, 202.5f, 337.5f }) spots.Add((d, d > 90 && d < 270 ? 25 : 35));
            var pins = Group("Pinnacles", g).transform;
            foreach (var (deg, h) in spots)
            {
                var p = MeshGO("Pinnacle", pin[h], pins, P(deg, 14.62f, CapY), Facing(deg), mStone, mBrass, mBrassPolished);
                float dd = Mathf.Repeat(deg, 360); if (dd > 100 && dd < 260) fadeTargets.Add(p);
            }
            // lanterns on the cap, outside each pinnacle pair; lights on every other one
            var lan = LanternMesh(); var lans = Group("Lanterns", g).transform; int li = 0;
            foreach (var (c, half) in Gaps)
                foreach (var s in new[] { -1, 1 })
                {
                    float deg = c + s * (half + 6.5f); var l = MeshGO("Lantern", lan, lans, P(deg, 14.75f, CapY), Facing(deg), mIron, mGlass, mFlame); l.transform.localScale = Vector3.one * 1.6f;
                    if ((li++ & 1) == 0)
                    {
                        var lg = Group("Light", l.transform, new Vector3(0, 0.55f, 0)).AddComponent<Light>();
                        lg.type = LightType.Point; lg.color = Hex(0xFFB860); lg.range = 5; lg.intensity = 3.2f; lg.shadows = LightShadows.None;
                    }
                }
        }

        static Mesh PinnacleMesh(float h)
        {
            var root = new GameObject("tmp"); var t = root.transform; float shaft = h - 2.2f;
            Box(t, new Vector3(0, 0.175f, 0), new Vector3(0.9f, 0.35f, 0.9f), mStone);
            Box(t, new Vector3(0, 0.41f, 0), new Vector3(0.74f, 0.12f, 0.74f), mStone);
            Box(t, new Vector3(0, 0.47f + shaft / 2, 0), new Vector3(0.56f, shaft, 0.56f), mStone);
            for (int i = 0; i < 4; i++) { float a = (i * 90 + 45) * Mathf.Deg2Rad; Box(t, new Vector3(Mathf.Cos(a) * 0.32f, 0.47f + shaft * 0.45f, Mathf.Sin(a) * 0.32f), new Vector3(0.16f, shaft * 0.9f, 0.16f), mStone, new Vector3(0, -i * 90 - 45, 0), "Rib"); }
            float top = 0.47f + shaft; var gab = Pyramid("Gablet", 0.24f, 0.42f, 4, 45); var leaf = Pyramid("CrocketLeaf", 0.05f, 0.15f, 3, 90);
            for (int i = 0; i < 4; i++) { var gg = MeshGO("Gablet", gab, t, Quaternion.Euler(0, i * 90, 0) * new Vector3(0, top - 0.02f, 0.27f), Quaternion.Euler(0, i * 90, 0), mStone); gg.transform.localScale = new Vector3(1, 1, 0.22f); }
            Box(t, new Vector3(0, top + 0.05f, 0), new Vector3(0.64f, 0.1f, 0.64f), mBrass, default, "Band");
            MeshGO("Spire", Pyramid("PinSpire", 0.34f, 1.9f, 8, 22.5f), t, new Vector3(0, top + 0.1f, 0), mStone);
            for (int i = 0; i < 4; i++) for (int k = 1; k <= 3; k++)
                {
                    float a = (i * 90 + 45) * Mathf.Deg2Rad, f = k / 4f, rr = 0.34f * (1 - f) + 0.04f;
                    MeshGO("Crocket", leaf, t, new Vector3(Mathf.Cos(a) * rr, top + 0.1f + 1.9f * f, Mathf.Sin(a) * rr), Quaternion.Euler(0, 90 - (i * 90 + 45), 0) * Quaternion.Euler(-60, 0, 0), mBrass);
                }
            Prim(PrimitiveType.Sphere, t, new Vector3(0, top + 2.04f, 0), Vector3.one * 0.2f, mBrassPolished, default, "Orb");
            Box(t, new Vector3(0, top + 2.3f, 0), new Vector3(0.05f, 0.34f, 0.05f), mBrassPolished, default, "Cross");
            Box(t, new Vector3(0, top + 2.36f, 0), new Vector3(0.2f, 0.05f, 0.05f), mBrassPolished, default, "CrossBar");
            return Combine(root, new[] { mStone, mBrass, mBrassPolished }, "Pinnacle_" + (int)(h * 10));
        }

        static Mesh SmallPostMesh()
        {
            var root = new GameObject("tmp"); var t = root.transform;
            Box(t, new Vector3(0, 0.1f, 0), new Vector3(0.34f, 0.2f, 0.34f), mStone);
            Box(t, new Vector3(0, 0.45f, 0), new Vector3(0.24f, 0.5f, 0.24f), mStone);
            Box(t, new Vector3(0, 0.72f, 0), new Vector3(0.3f, 0.05f, 0.3f), mBrass);
            MeshGO("Spire", Pyramid("PostSpire", 0.16f, 0.55f, 8, 22.5f), t, new Vector3(0, 0.74f, 0), mStone);
            Prim(PrimitiveType.Sphere, t, new Vector3(0, 1.32f, 0), Vector3.one * 0.09f, mBrassPolished);
            return Combine(root, new[] { mStone, mBrass, mBrassPolished }, "SmallPost");
        }

        static Mesh LanternMesh()
        {
            var root = new GameObject("tmp"); var t = root.transform;
            Box(t, new Vector3(0, 0.22f, 0), new Vector3(0.07f, 0.44f, 0.07f), mIron);
            Box(t, new Vector3(0, 0.45f, 0), new Vector3(0.3f, 0.04f, 0.3f), mIron);
            for (int i = 0; i < 4; i++) { float a = (i * 90 + 45) * Mathf.Deg2Rad; Box(t, new Vector3(Mathf.Cos(a) * 0.13f, 0.65f, Mathf.Sin(a) * 0.13f), new Vector3(0.035f, 0.38f, 0.035f), mIron); }
            for (int i = 0; i < 4; i++) Box(t, Quaternion.Euler(0, i * 90, 0) * new Vector3(0, 0.65f, 0.1f), new Vector3(0.17f, 0.32f, 0.015f), mGlass, new Vector3(0, i * 90, 0), "Pane");
            Box(t, new Vector3(0, 0.63f, 0), new Vector3(0.11f, 0.26f, 0.11f), mFlame, default, "Flame");
            MeshGO("Roof", Pyramid("LanternRoof", 0.23f, 0.2f), t, new Vector3(0, 0.85f, 0), mIron);
            MeshGO("RoofSpire", Pyramid("LanternSpire", 0.05f, 0.18f, 4), t, new Vector3(0, 1.03f, 0), mIron);
            MeshGO("Handle", Torus("LanternHandle", 0.06f, 0.012f, 16, 4), t, new Vector3(0, 1.24f, 0), Quaternion.Euler(90, 0, 0), mIron);
            return Combine(root, new[] { mIron, mGlass, mFlame }, "Lantern");
        }

        static void BuildStations(Transform arena)
        {
            var g = Group("RitualStations", arena).transform;
            var dial = Disc("StationDial", 1.9f, 128, 1.9f);
            var ringA = Torus("StationRingOuter", 1.92f, 0.07f, 128, 10); var ringB = Torus("StationRingInner", 1.6f, 0.035f, 128, 8);
            var bulge = ArcPrism("StationBulge", 1.95f, 2.5f, -0.45f, RimH - 0.08f, -100, 100, true, Trim, 4);
            var bulgeCap = SweepRing("StationBulgeCap", new[] { new Vector2(1.93f, RimH - 0.08f), new Vector2(1.9f, RimH + 0.02f), new Vector2(2.0f, CapY + 0.02f), new Vector2(2.45f, CapY + 0.02f), new Vector2(2.6f, RimH - 0.01f), new Vector2(2.6f, RimH - 0.08f) }, -100, 100, 3);
            var gate = GateMesh(); var post = SmallPostMesh(); var fin = Pyramid("BossFinial", 0.07f, 0.22f, 8, 22.5f);
            string[] labels = { "NE", "SE", "SW", "NW" };
            for (int i = 0; i < 4; i++)
            {
                float deg = 45 + i * 90; var s = Group("RitualStation_" + labels[i], g, P(deg, 13.2f), Facing(deg)).transform;
                MeshGO("Dial", dial, s, new Vector3(0, 0.02f, 0), Quaternion.Euler(0, 180, 0), mStation);
                MeshGO("RingOuter", ringA, s, new Vector3(0, 0.02f, 0), mBrass);
                MeshGO("RingInner", ringB, s, new Vector3(0, 0.025f, 0), mBrassPolished);
                Prim(PrimitiveType.Cylinder, s, new Vector3(0, -0.24f, 0), new Vector3(3.9f, 0.23f, 3.9f), mPedestal, default, "Plinth");
                Prim(PrimitiveType.Cylinder, s, new Vector3(0, -0.62f, 0), new Vector3(4.5f, 0.18f, 4.5f), mPedestal, default, "PlinthStep");
                Prim(PrimitiveType.Sphere, s, new Vector3(0, 0, 0), new Vector3(1f, 0.5f, 1f), mBrassPolished, default, "DomedBoss");
                MeshGO("Finial", fin, s, new Vector3(0, 0.24f, 0), mBrassPolished);
                MeshGO("Bulge", bulge, s, Vector3.zero, Quaternion.Euler(0, -90, 0), mStone);  // wraps the outward half (+Z local)
                MeshGO("BulgeCap", bulgeCap, s, Vector3.zero, Quaternion.Euler(0, -90, 0), mStoneCap);
                foreach (var a in new[] { -60f, -20f, 20f, 60f }) MeshGO("Post", post, s, Quaternion.Euler(0, a, 0) * new Vector3(0, CapY, 2.25f), Quaternion.Euler(0, a, 0), mStone, mBrass, mBrassPolished);
                MeshGO("Gate", gate, s, new Vector3(0, CapY, 2.35f), Quaternion.identity, mStone, mBrass);

            }
        }

        static Mesh GateMesh()
        {
            var root = new GameObject("tmp"); var t = root.transform;
            foreach (var x in new[] { -0.95f, 0.95f }) { Box(t, new Vector3(x, 0.9f, 0), new Vector3(0.24f, 1.8f, 0.24f), mStone); MeshGO("Fin", Pyramid("GateFin", 0.14f, 0.5f, 8, 22.5f), t, new Vector3(x, 1.8f, 0), mStone); }
            PointedArch(t, 1.9f, 1.8f, 0.14f, 0.2f, mBrass, 5);
            Tracery(t, 1.9f, 1.8f, 0, mBrass);
            Prim(PrimitiveType.Sphere, t, new Vector3(0, 1.8f + 1.65f + 0.1f, 0), Vector3.one * 0.18f, mBrass);
            return Combine(root, new[] { mStone, mBrass }, "StationGate");
        }

        static void BuildEntrances(Transform arena)
        {
            var g = Group("Entrances", arena).transform; var pinS = PinnacleMesh(2.4f); var post = SmallPostMesh(); var bannerBig = BannerMesh(1.3f, 4.0f); var bannerSmall = BannerMesh(0.95f, 3.2f);
            // South: grand stair down to a long causeway, flanked by gatehouses hung with crimson banners
            var s = Group("Entrance_South", g, Vector3.zero, Facing(180)).transform;
            for (int i = 0; i < 6; i++)
            {
                float top = -0.2f * (i + 1), z = RimOut - 0.3f + 0.5f * i;
                Box(s, new Vector3(0, (top - 3) / 2, z + 0.25f), new Vector3(4.0f, 3 + top, 0.5f), mDeck, default, "Tread");
                Box(s, new Vector3(0, top + 0.01f, z + 0.04f), new Vector3(4.0f, 0.04f, 0.08f), mBrass, default, "Nosing");
            }
            foreach (var x in new[] { -2.12f, 2.12f }) Box(s, new Vector3(x, -0.85f, RimOut + 1.2f), new Vector3(0.28f, 0.7f, 3.4f), mStone, new Vector3(-21.8f, 0, 0), "Stringer");
            float cz0 = RimOut + 2.7f, clen = 26;
            Box(s, new Vector3(0, -1.7f, cz0 + clen / 2), new Vector3(4.0f, 1.0f, clen), mDeck, default, "Causeway");
            Box(s, new Vector3(0, -1.19f, cz0 + clen / 2), new Vector3(0.08f, 0.02f, clen), mBrass, default, "CausewayInlay");
            foreach (var x in new[] { -2.25f, 2.25f })
            {
                Box(s, new Vector3(x, -0.78f, cz0 + clen / 2 - 1.5f), new Vector3(0.45f, 0.84f, clen + 3), mStone, default, "CausewayWall");
                Box(s, new Vector3(x, -0.32f, cz0 + clen / 2 - 1.5f), new Vector3(0.58f, 0.1f, clen + 3), mStoneCap, default, "WallCoping");
                Box(s, new Vector3(x - Mathf.Sign(x) * 0.27f, -0.42f, cz0 + clen / 2 - 1.5f), new Vector3(0.05f, 0.04f, clen + 3), mBrass, default, "WallTrim");
                for (int k = 0; k < 6; k++)
                {
                    Box(s, new Vector3(x * 1.18f, -1.2f, cz0 + 2 + k * 4.2f), new Vector3(0.6f, 1.8f, 0.7f), mStone, default, "Buttress");
                    MeshGO("Pinnacle", post, s, new Vector3(x, -0.27f, cz0 + 2 + k * 4.2f), Quaternion.identity, mStone, mBrass, mBrassPolished);
                }
                var gh = Group(x < 0 ? "Gatehouse_A" : "Gatehouse_B", s, new Vector3(x * 1.35f, 0, RimOut + 1.6f)).transform;
                Box(gh, new Vector3(0, -0.4f, 0), new Vector3(1.4f, 4.4f, 3.4f), mStone, default, "GatehouseWall");
                Box(gh, new Vector3(0, 1.88f, 0), new Vector3(1.6f, 0.12f, 3.6f), mBrass, default, "GatehouseTrim");
                Box(gh, new Vector3(0, 2.0f, 0), new Vector3(1.5f, 0.14f, 3.5f), mStoneCap, default, "GatehouseCoping");
                foreach (var zz in new[] { -1.1f, 1.1f }) MeshGO("Spire", pinS, gh, new Vector3(0, 2.05f, zz), Quaternion.identity, mStone, mBrass, mBrassPolished);
                // blind tracery on the outer face
                for (int k = 0; k < 2; k++)
                {
                    var bt = Group("BlindTracery", gh, new Vector3(-Mathf.Sign(x) * 0.71f, -0.4f, -0.8f + k * 1.6f), Quaternion.Euler(0, -Mathf.Sign(x) * 90, 0)).transform;
                    foreach (var xx in new[] { -0.45f, 0.45f }) Box(bt, new Vector3(xx, -0.3f, 0), new Vector3(0.09f, 1.6f, 0.08f), mStoneCap);
                    PointedArch(bt, 0.9f, 0.5f, 0.09f, 0.08f, mStoneCap, 4); Tracery(bt, 0.9f, 0.5f, 0.02f, mStoneCap);
                }
                MeshGO("Banner", bannerBig, gh, new Vector3(0, -0.25f, 1.76f), Quaternion.Euler(0, 180, 0), mBanner);
                Box(gh, new Vector3(0, 1.8f, 1.8f), new Vector3(1.45f, 0.06f, 0.06f), mBrassPolished, default, "BannerRod");
                fadeTargets.Add(gh.gameObject);
                var py = Group("Pylon", s, new Vector3(x * 1.55f, 0, cz0 + 5.5f)).transform;
                Box(py, new Vector3(0, -0.6f, 0), new Vector3(1.0f, 3.6f, 1.0f), mStone, default, "PylonWall");
                Box(py, new Vector3(0, 1.22f, 0), new Vector3(1.12f, 0.1f, 1.12f), mBrass, default, "PylonTrim");
                MeshGO("Spire", Pyramid("PylonSpire", 0.45f, 2.2f, 8, 22.5f), py, new Vector3(0, 1.27f, 0), mStone);
                MeshGO("Banner", bannerSmall, py, new Vector3(0, -0.25f, 0.55f), Quaternion.Euler(0, 180, 0), mBanner);
                Box(py, new Vector3(0, 1.35f, 0.58f), new Vector3(1.05f, 0.05f, 0.05f), mBrassPolished, default, "BannerRod");
            }
            for (int k = 0; k < 3; k++) MeshGO("Support", Spire("CausewaySupport", 1.6f, 14, 6, 3, 0.15f), s, new Vector3(0, -2.2f, cz0 + 6 + k * 8), mRock);
            // North: three steps to a dark pedestal landing with an engraved brass ring (the tilted bell's lip clears it)
            var n = Group("Stair_North", g, Vector3.zero, Facing(0)).transform;
            for (int i = 0; i < 3; i++)
            {
                float top = 0.2f * (i + 1), z = RimOut - 0.3f + 0.3f * i;
                Box(n, new Vector3(0, (top - 2) / 2, z + 0.15f), new Vector3(3.0f, 2 + top, 0.3f), mDeck, default, "Tread");
                Box(n, new Vector3(0, top + 0.01f, z + 0.04f), new Vector3(3.0f, 0.04f, 0.08f), mBrass, default, "Nosing");
            }
            float lz = RimOut - 0.3f + 0.9f + 0.75f;
            Box(n, new Vector3(0, 0.6f - 1.5f, lz), new Vector3(4, 3, 1.5f), mPedestal, default, "Landing");
            Box(n, new Vector3(0, 0.6f, lz), new Vector3(4.1f, 0.06f, 1.6f), mBrass, default, "LandingTrim");
            MeshGO("LandingRing", Torus("LandingRing", 0.6f, 0.03f, 48, 6), n, new Vector3(0, 0.64f, lz), mBrassPolished);
            foreach (var x in new[] { -1.65f, 1.65f }) { Box(n, new Vector3(x, 0.45f, RimOut + 0.55f), new Vector3(0.3f, 0.9f, 1.6f), mStone, default, "SideWall"); Box(n, new Vector3(x, 0.93f, RimOut + 0.55f), new Vector3(0.38f, 0.08f, 1.7f), mStoneCap, default, "SideCoping"); }
            MeshGO("Support", Spire("LandingSupport", 2.6f, 12, 6, 3, 0.15f), n, new Vector3(0, -2.4f, lz), mRock);
            // East / West: flagstone bridges with moulded walls, corbels and an under-arch, ending at a ruined tracery gateway
            foreach (var (deg, label) in new[] { (90f, "Bridge_East"), (270f, "Bridge_West") })
            {
                var b = Group(label, g, Vector3.zero, Facing(deg)).transform; float bz = RimOut - 0.3f + 3.5f;
                Box(b, new Vector3(0, -0.32f, bz), new Vector3(3.5f, 0.6f, 7), mDeck, default, "Deck");
                Box(b, new Vector3(0, -0.005f, bz), new Vector3(0.06f, 0.02f, 7), mBrass, default, "DeckInlay");
                foreach (var x in new[] { -1.9f, 1.9f })
                {
                    Box(b, new Vector3(x, 0.15f, bz), new Vector3(0.3f, 0.6f, 7), mStone, default, "SideWall");
                    Box(b, new Vector3(x, 0.49f, bz), new Vector3(0.4f, 0.08f, 7), mStoneCap, default, "Coping");
                    for (int k = 0; k < 4; k++) MeshGO("Post", post, b, new Vector3(x, 0.53f, bz - 3 + k * 2), Quaternion.identity, mStone, mBrass, mBrassPolished);
                    for (int k = 0; k < 5; k++) { Box(b, new Vector3(x * 0.95f, -0.75f, bz - 3 + k * 1.5f), new Vector3(0.4f, 0.3f, 0.4f), mStone, default, "Corbel"); Box(b, new Vector3(x * 0.92f, -1.0f, bz - 3 + k * 1.5f), new Vector3(0.28f, 0.25f, 0.28f), mStone, default, "Corbel"); }
                }
                var ua = Group("UnderArch", b, new Vector3(0, -2.8f, bz), Quaternion.Euler(0, 90, 0)).transform; PointedArch(ua, 3.4f, -1.4f, 0.4f, 3.0f, mStoneDark, 6);
                for (int k = 0; k < 4; k++) Box(b, new Vector3(-1.2f + k * 0.8f, -0.32f - Rn() * 0.4f, bz + 3.5f + 0.3f + Rn() * 0.9f), new Vector3(0.75f, 0.55f, Rr(0.5f, 1.4f)), mDeck, new Vector3(Rr(-12, 12), Rr(-10, 10), Rr(-12, 12)), "BrokenDeck");
                for (int k = 0; k < 6; k++) Box(b, new Vector3(Rr(-1.6f, 1.6f), Rr(-4f, -1f), bz + 4.5f + Rn() * 2.5f), Vector3.one * Rr(0.3f, 0.8f), mDeck, new Vector3(Rn() * 90, Rn() * 90, Rn() * 90), "FallingRubble");
                MeshGO("Support", Spire("BridgeSupport", 1.4f, 10, 6, 3, 0.2f), b, new Vector3(0, -0.6f, bz), mRock);
                var gate = Group("RuinedGate", b, new Vector3(0, 0, bz + 2.6f)).transform;
                foreach (var x in new[] { -1.95f, 1.95f })
                {
                    float h = x < 0 ? 4.5f : 3.2f; Box(gate, new Vector3(x, h / 2, 0), new Vector3(0.7f, h, 0.7f), mStone, default, "Column");
                    foreach (var zz in new[] { -0.4f, 0.4f }) Box(gate, new Vector3(x, h / 2, zz), new Vector3(0.22f, h, 0.22f), mStone, default, "Shaft");
                    Box(gate, new Vector3(x, h + 0.1f, 0), new Vector3(0.9f, 0.2f, 0.9f), mStoneCap, default, "Capital"); Box(gate, new Vector3(x, h - 0.05f, 0), new Vector3(0.78f, 0.06f, 0.78f), mBrass, default, "CapitalBand");
                }
                var arch = Group("Arch", gate, new Vector3(0, 0, 0)).transform; PointedArch(arch, 3.9f, 4.5f, 0.45f, 0.6f, mStone, 6, 0.55f);
                MeshGO("RoseFragment", Torus("GateRoseFragment", 0.7f, 0.06f, 24, 6, 160, 200), gate, new Vector3(-0.6f, 5.2f, 0), Quaternion.Euler(90, 0, 0), mStoneCap);
                Box(gate, new Vector3(-0.9f, 4.7f, 0), new Vector3(0.08f, 1.0f, 0.1f), mStoneCap, new Vector3(0, 0, 8), "MullionFragment");
                for (int k = 0; k < 4; k++) Box(gate, new Vector3(Rr(0.6f, 1.8f), Rr(0.1f, 0.3f), Rr(-0.6f, 0.6f)), Vector3.one * Rr(0.3f, 0.6f), mStone, new Vector3(Rn() * 90, Rn() * 90, Rn() * 90), "Rubble");
            }
        }

        // Solid clock band: brass annulus with a rolled edge, inner bead, degree scale, twelve numeral plates, optional spokes.
        static Transform ClockRing(Transform parent, string name, float rad, float band, float arc = 360, float start = 0, int spokes = 0)
        {
            var g = Group(name, parent).transform; float t0 = start, t1 = start + arc;
            MeshGO("Band", ArcPrism(name + "_Band", rad - band, rad, -0.12f, 0.12f, t0, t1, arc < 360, Trim, 1.5f), g, Vector3.zero, mBrass);
            MeshGO("Edge", Torus(name + "_Edge", rad, 0.16f, 192, 8, arc, start), g, Vector3.zero, mBrassPolished);
            MeshGO("Bead", Torus(name + "_Bead", rad - band - 0.25f, 0.07f, 192, 6, arc, start), g, Vector3.zero, mBrass);
            for (int i = 0; i < 72; i++)
            {
                float a = start + i * 5; if (a > t1 - 1) break;
                Box(g, Polar(a, rad - 0.12f, 0.13f), new Vector3(0.04f, 0.03f, i % 6 == 0 ? 0.22f : 0.12f), mIron, new Vector3(0, 90 - a, 0), "Degree");
            }
            for (int i = 0; i < 12; i++)
            {
                float a = start + 15 + i * 30; if (a > t1 - 5) break;
                Box(g, Polar(a, rad - band / 2 - 0.05f), new Vector3(band * (i % 3 == 0 ? 0.5f : 0.3f), 0.28f, band * 0.5f), mIvory, new Vector3(0, 90 - a, 0), "NumeralPlate");
                Box(g, Polar(a, rad - band / 2 - 0.05f, 0.16f), new Vector3(band * 0.16f, 0.04f, band * 0.5f), mBrassPolished, new Vector3(0, 90 - a, 0), "Numeral");
                if (i % 3 == 0) Box(g, Polar(a + 1.6f, rad - band / 2 - 0.05f, 0.16f), new Vector3(band * 0.16f, 0.04f, band * 0.5f), mBrassPolished, new Vector3(0, 90 - a, 0), "Numeral");
            }
            for (int i = 0; i < spokes; i++)
            {
                float a = start + (i + 0.5f) * arc / spokes; Box(g, Polar(a, (rad - band) / 2), new Vector3(0.42f, 0.4f, rad - band), mBrass, new Vector3(0, 90 - a, 0), "Spoke");
            }
            return g;
        }

        static void BuildClockRings(Transform g)
        {
            void Spin(Transform t, float s) { spinners.Add(t); spinSpeeds.Add(s); }
            var right = Group("RightPivot", g, P(76, 47, -12), Quaternion.Euler(72, -25, -10)).transform; Spin(ClockRing(right, "Right", 10, 1.4f, 290, 200, 3), -1.6f);
            var low = Group("LowPivot", g, P(298, 50, -24), Quaternion.Euler(35, 40, 0)).transform; Spin(ClockRing(low, "Low", 8, 1.2f, 300, 30, 3), 2.2f);
        }

        static void BuildVoid(Transform arena)
        {
            var g = Group("Void", arena).transform;
            var vortex = MeshGO("Vortex", Disc("VortexDisc", 160, 128, 160), g, new Vector3(0, -60, 0), mVortex);
            vortex.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            bool CamSide(Vector3 p) => p.z > 12 && Mathf.Abs(p.x) < 22 && p.y > -14;
            // floating gothic arcades: three modules, fewer and larger, pushed back from the gameplay frame's top corners
            var arcades = new[] { ArcadeMesh(2, 0), ArcadeMesh(3, 1), ArcadeMesh(4, 2) };
            var ruins = Group("Ruins", g).transform;
            var places = new List<(float deg, float r, float y, int v, float s)> { (322, 60, -22, 2, 1.3f), (38, 61, -23, 2, 1.3f), (90, 38, -4, 1, 1.2f), (270, 38, -6, 1, 1.15f),
                (240, 42, -14, 0, 1.0f), (120, 41, -16, 0, 1.0f), (0, 48, -12, 1, 1.25f), (200, 40, -22, 2, 0.9f), (160, 40, -24, 1, 0.9f), (345, 52, -8, 0, 1.0f) };
            foreach (var (deg, r, y, v, s) in places)
            {
                var p = P(deg, r, y); if (CamSide(p)) continue;
                var a = MeshGO("Arcade", arcades[v], ruins, p, Quaternion.LookRotation(-Out(deg)) * Quaternion.Euler(Rr(-18, 18), Rr(-20, 20), Rr(-10, 10)), mStoneDark, mBrass, mStoneCap);
                a.transform.localScale = Vector3.one * s; bobbers.Add(a.transform);
            }
            // inverted spire chunks
            var chunkMeshes = Enumerable.Range(0, 4).Select(i => Spire("FloatingSpire", Rr(1.2f, 2.6f), Rr(4, 10), 6 + i, 4, 0.25f)).ToArray();
            var spires = Group("FloatingSpires", g).transform; var spireClusters = Enumerable.Range(0, 3).Select(i => Group("DebrisClusterSpires_" + i, spires).transform).ToArray(); foreach (var c in spireClusters) bobbers.Add(c);
            for (int i = 0; i < 24; i++)
            {
                float deg = Rr(0, 360), r = Rr(20, 55); var p = P(deg, r, Rr(-30, 6)); if (CamSide(p)) continue;
                var c = MeshGO("Spire", chunkMeshes[i % 4], spireClusters[i % 3], p, Quaternion.Euler(Rr(-12, 12), Rn() * 360, Rr(-12, 12)), mRock);
                if (i % 2 == 0) Box(c.transform, new Vector3(Rr(-0.5f, 0.5f), 0.4f, Rr(-0.5f, 0.5f)), new Vector3(Rr(0.8f, 1.6f), Rr(0.6f, 1.4f), Rr(0.8f, 1.6f)), mStoneDark, new Vector3(0, Rn() * 90, 0), "Block");
            }
            // debris in ten drifting clusters (one merged mesh each)
            var rocks = Enumerable.Range(0, 8).Select(i => Rock("Rock", 0.25f)).ToArray(); var deb = Group("Debris", g).transform;
            var clusters = Enumerable.Range(0, 10).Select(i => Group("DebrisCluster_" + i, deb).transform).ToArray(); foreach (var c in clusters) bobbers.Add(c);
            for (int i = 0; i < 200; i++)
            {
                float deg = Rr(0, 360), r = 18 + Mathf.Pow(Rn(), 0.8f) * 52; var p = P(deg, r, Rr(-45, 12)); if (CamSide(p) && p.y > -6) continue;
                float s = 0.25f + Mathf.Pow(Rn(), 3) * 2.4f;
                var k = MeshGO("Rock", rocks[i % 8], clusters[i % 10], p, Quaternion.Euler(Rn() * 360, Rn() * 360, Rn() * 360), Rn() < 0.3f ? mRockLight : mRock);
                k.transform.localScale = Vector3.one * s;
            }
            // diagonal chains crossing the frame corners (oval links)
            var link = Torus("ChainLink", 0.26f, 0.075f, 20, 8, 360, 0, new Vector3(1, 1, 1.55f));
            var chains = Group("Chains", g).transform;
            Chain(chains, "Chain_TopLeft", new Vector3(44, -3, -6), new Vector3(14, -5, -42), 8, 1.2f, link);
            Chain(chains, "Chain_TopRight", new Vector3(-44, -3, -6), new Vector3(-14, -5, -42), 8, 1.2f, link);
            Chain(chains, "Chain_BottomLeft", new Vector3(30, -2, 6), new Vector3(16, -9, 30), 5, 1.2f, link);
            Chain(chains, "Chain_BottomRight", new Vector3(-30, -2, 6), new Vector3(-16, -9, 30), 5, 1.2f, link);
            // hanging bells (small versions of the hero bell, with clapper and lip band)
            mBronzePlain = Lit("HangingBellBronze", Hex(0x6A5236), 0.55f, 0.32f, dbl: true);
            var lowLink = Torus("ChainLinkLow", 0.26f, 0.075f, 10, 5, 360, 0, new Vector3(1, 1, 1.55f)); var small = Lathe("HangingBell", Densify(BellProfile(0.32f), 2), 48); var hb = Group("HangingBells", g).transform; var band = Torus("HangingBellBand", 2.2f, 0.07f, 48, 6);
            foreach (var (deg, r, y) in new[] { (296f, 42f, -9f), (324f, 47f, -7f), (36f, 46f, -8f), (66f, 41f, -10f), (252f, 27f, -1f), (108f, 27f, 0f), (348f, 22f, 7f) })
            {
                var pivot = Group("HangingBell", hb, P(deg, r, y + 30)).transform; swingers.Add(pivot);
                Chain(pivot, "BellChain", new Vector3(0, -31f, 0), new Vector3(0, 0, 0), 0.01f, 0.9f, lowLink);
                float sc = Rr(0.6f, 1.0f); var b = MeshGO("Bell", small, pivot, new Vector3(0, -30 - 2.7f * sc, 0), mBronze); MeshGO("Crown", Torus("HangingBellCrown", 0.4f, 0.12f, 16, 6), b.transform, new Vector3(0, 3.6f, 0), Quaternion.Euler(90, 0, 0), mBronze); b.transform.localScale = Vector3.one * sc;
                MeshGO("Band", band, b.transform, new Vector3(0, 0.05f, 0), mBrass);
                Prim(PrimitiveType.Sphere, b.transform, new Vector3(0, 0.3f, 0), Vector3.one * 0.7f, mIron, default, "Clapper");
            }
            // faint warm light shafts behind the bell
            var shafts = Group("LightShafts", g).transform;
            foreach (var (deg, r, w) in new[] { (346f, 46f, 3f), (12f, 48f, 2.4f), (0f, 52f, 3.4f) })
            {
                var p = P(deg, r, -10); var toCam = new Vector3(0, 0, 22.5f) - p; toCam.y = 0;
                var q = Prim(PrimitiveType.Quad, shafts, p, new Vector3(w, 90, 1), mShaft, default, "Shaft"); q.transform.rotation = Quaternion.LookRotation(-toCam);
                q.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        // Gothic arcade module: clustered columns, stepped bases, brass-banded capitals, pointed arches with an inner order,
        // a moulded entablature, a broken end (variants 1–2) and a rock underside.
        static Mesh ArcadeMesh(int arches, int variant)
        {
            var root = new GameObject("tmp"); var t = root.transform; float span = 3.4f, colH = 7f, w = arches * span;
            for (int i = 0; i <= arches; i++)
            {
                float x = -w / 2 + i * span; bool broken = variant > 0 && i == arches;
                float h = broken ? colH * 0.5f : colH;
                Box(t, new Vector3(x, h / 2, 0), new Vector3(0.7f, h, 0.7f), mStoneDark, default, "Core");
                foreach (var (sx, sz) in new[] { (0.42f, 0f), (-0.42f, 0f), (0f, 0.42f), (0f, -0.42f) }) Box(t, new Vector3(x + sx, h / 2, sz), new Vector3(0.22f, h, 0.22f), mStoneDark, default, "Shaft");
                Box(t, new Vector3(x, 0.18f, 0), new Vector3(1.35f, 0.36f, 1.35f), mStoneCap, default, "Base"); Box(t, new Vector3(x, 0.48f, 0), new Vector3(1.12f, 0.24f, 1.12f), mStoneCap, default, "Base2");
                if (!broken) { Box(t, new Vector3(x, colH - 0.08f, 0), new Vector3(1.12f, 0.1f, 1.12f), mBrass, default, "CapitalBand"); Box(t, new Vector3(x, colH + 0.15f, 0), new Vector3(1.3f, 0.3f, 1.3f), mStoneCap, default, "Capital"); }
                else for (int k = 0; k < 3; k++) Box(t, new Vector3(x + Rr(-0.4f, 0.4f), h + 0.2f + k * 0.15f, Rr(-0.3f, 0.3f)), new Vector3(Rr(0.4f, 0.8f), 0.4f, Rr(0.4f, 0.8f)), mStoneDark, new Vector3(Rr(-20, 20), Rr(0, 90), Rr(-20, 20)), "BrokenTop");
                bool skip = broken || (variant == 2 && i == arches - 1);
                if (i < arches && !skip)
                {
                    var a = Group("Arch", t, new Vector3(x + span / 2, 0, 0)).transform;
                    PointedArch(a, span - 0.9f, colH + 0.3f, 0.5f, 0.9f, mStoneDark, 6);
                    var inner = Group("Inner", a, new Vector3(0, 0, 0.1f)).transform; PointedArch(inner, span - 1.4f, colH + 0.3f, 0.16f, 1.0f, mStoneCap, 6);
                    Box(a, new Vector3(0, colH + 0.3f + (span - 0.9f) * 0.87f + 0.2f, 0), new Vector3(span * 0.55f, 0.6f, 0.85f), mStoneDark, default, "Spandrel");
                }
            }
            float eh = colH + 0.3f + (span - 0.9f) * 0.87f + 0.75f, ew = variant == 2 ? w * 0.6f : w + 0.8f, ex = variant == 2 ? -w * 0.2f : 0;
            Box(t, new Vector3(ex, eh, 0), new Vector3(ew, 0.6f, 1.05f), mStoneDark, default, "Entablature");
            Box(t, new Vector3(ex, eh - 0.34f, 0), new Vector3(ew + 0.05f, 0.08f, 1.1f), mBrass, default, "StringCourse");
            Box(t, new Vector3(ex, eh + 0.4f, 0), new Vector3(ew + 0.3f, 0.22f, 1.3f), mStoneCap, default, "Cornice");
            Box(t, new Vector3(0, -0.6f, 0), new Vector3(w + 1.4f, 1.2f, 1.8f), mStoneDark, default, "Foundation");
            MeshGO("Under", Spire("ArcadeUnder", 1.4f, 5, 6, 3, 0.2f), t, new Vector3(0, -1.2f, 0), mStoneDark);
            return Combine(root, new[] { mStoneDark, mBrass, mStoneCap }, "Arcade_" + arches);
        }

        static void Chain(Transform parent, string name, Vector3 a, Vector3 b, float sag, float linkLen, Mesh link)
        {
            var g = Group(name, parent).transform; float len = (b - a).magnitude * 1.1f; int n = Mathf.CeilToInt(len / (linkLen * 0.78f));
            Vector3 At(float t) => Vector3.Lerp(a, b, t) - Vector3.up * sag * 4 * t * (1 - t);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1); var p = At(t); var tan = (At(Mathf.Min(1, t + 0.01f)) - At(Mathf.Max(0, t - 0.01f))).normalized;
                var l = MeshGO("Link", link, g, p, Quaternion.LookRotation(tan, Mathf.Abs(tan.y) > 0.9f ? Vector3.forward : Vector3.up) * Quaternion.Euler(0, 0, (i & 1) * 90), mIron);
                l.transform.localScale = Vector3.one * linkLen / 0.82f;
            }
        }

        static Vector2[] BellProfile(float k)
        {
            var p = new[] { (0f, 10.4f), (1.6f, 10.3f), (2.7f, 9.8f), (3.3f, 8.8f), (3.5f, 7.2f), (3.6f, 5.4f), (3.95f, 3.7f), (4.6f, 2.2f), (5.6f, 1.0f), (6.6f, 0.25f), (6.9f, 0f), (6.7f, -0.12f),
                (6.0f, 0.15f), (5.0f, 0.9f), (4.1f, 2.1f), (3.45f, 3.7f), (3.1f, 5.6f), (2.95f, 7.4f), (2.6f, 8.9f), (1.6f, 9.5f), (0f, 9.65f) };
            return p.Select(q => new Vector2(q.Item1, q.Item2) * k).ToArray();
        }
        // Catmull-Rom subdivision: `sub` points per original segment.
        static Vector2[] Densify(Vector2[] p, int sub)
        {
            var o = new List<Vector2>();
            for (int i = 0; i + 1 < p.Length; i++)
            {
                Vector2 p0 = p[Mathf.Max(0, i - 1)], p1 = p[i], p2 = p[i + 1], p3 = p[Mathf.Min(p.Length - 1, i + 2)];
                for (int s = 0; s < sub; s++)
                {
                    float t = s / (float)sub, t2 = t * t, t3 = t2 * t;
                    o.Add(0.5f * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3));
                }
            }
            o.Add(p[p.Length - 1]); return o.ToArray();
        }
        static float BellRadiusAt(Vector2[] prof, int lipIdx, float y)
        {
            for (int i = 0; i < lipIdx; i++) if (y <= prof[i].y && y >= prof[i + 1].y) return Mathf.Lerp(prof[i + 1].x, prof[i].x, (y - prof[i + 1].y) / Mathf.Max(1e-4f, prof[i].y - prof[i + 1].y));
            return prof[lipIdx].x;
        }

        // Closes both jagged edges of the bell's break with a wall face (outer profile row j joined to inner row K-1-j).
        static Mesh BreakFaces(Vector2[] prof, int lipIdx, float[] gaps)
        {
            var mb = new MB { uvScale = Trim }; int K = prof.Length;
            Vector3 At(int j, float s) { float ph = s * gaps[j]; return new Vector3(Mathf.Sin(ph) * prof[j].x, prof[j].y, Mathf.Cos(ph) * prof[j].x); }
            foreach (var s in new[] { 1f, -1f })
                for (int j = 0; j < lipIdx; j++)
                {
                    int i0 = K - 1 - j, i1 = K - 2 - j; float ph = s * gaps[j];
                    var hint = -s * new Vector3(Mathf.Cos(ph), 0, -Mathf.Sin(ph));
                    mb.Quad(At(j, s), At(j + 1, s), At(i1, s), At(i0, s), hint);
                }
            return mb.Build("BellBreakFaces");
        }

        static (Light, Transform) BuildBell(Transform root)
        {
            const float k = 5.5f / 6.9f, gapHalf = 15 * Mathf.Deg2Rad;  // mouth radius 5.5 m, height ≈ 8.3 m, 30° break
            var prof = Densify(BellProfile(k), 2); int lipIdx = 20; float[] bandY = { 0.12f, 2.72f, 6.08f };
            var (bt, btN) = PaintBell(prof, lipIdx, bandY);
            mBronze = Lit("BellBronze", Color.white, 0.6f, 0.35f, dbl: true, map: bt, bump: btN);
            // −45° tilt: the mouth turns toward the camera, so the gameplay frame shows the glowing lip and cracks
            var bell = Group("Bell", root, new Vector3(0, 1.9f, -16.5f), Quaternion.Euler(-45, 0, 0)).transform;
            var jag = new System.Random(8);
            var gaps = Enumerable.Range(0, prof.Length).Select(j => gapHalf * ((j % 3 == 0 ? 1.3f : 0.8f) + 0.35f * (float)jag.NextDouble()) * (j <= lipIdx ? 1 + 0.35f * j / lipIdx : 1.3f)).ToArray();
            MeshGO("Body", Lathe("BellBody", prof, 128, j => gaps[j]), bell, Vector3.zero, mBronze);
            MeshGO("BreakFaces", BreakFaces(prof, lipIdx, gaps), bell, Vector3.zero, mBronze); // wall thickness on both jagged edges of the break
            var inner = prof.Skip(lipIdx + 2).Take(14).Select(p => new Vector2(p.x * 0.96f, p.y + 0.02f)).ToArray();
            MeshGO("InteriorGlow", Lathe("BellInterior", inner, 64), bell, Vector3.zero, mBellGlow).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            // gold bands with raised numeral studs, broken at the gap (gap centred on +Z = Unity polar 90°)
            foreach (var y in bandY)
            {
                float r = BellRadiusAt(prof, lipIdx, y) + 0.04f;
                MeshGO("Band", Torus("BellBand", r, 0.12f, 128, 8, 360 - 44, 90 + 22), bell, new Vector3(0, y, 0), mBrass);
                for (int i = 0; i < 12; i++)
                {
                    float a = 90 + 22 + 12 + i * (316f - 24) / 11; var d = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), 0, Mathf.Sin(a * Mathf.Deg2Rad));
                    Box(bell, d * (r + 0.08f) + Vector3.up * y, new Vector3(0.2f, 0.26f, 0.1f), mBrassPolished, new Vector3(0, -a + 90, 0), "Numeral");
                }
            }
            // fine branching cracks from the break: a dark groove with a thinner emissive core, tapering toward the tips
            void Crack(float ph, float y, float w0, int steps, int depth, float side)
            {
                Vector3 last = Vector3.zero;
                for (int s = 0; s < steps; s++)
                {
                    float ry = BellRadiusAt(prof, lipIdx, y) + 0.025f; var p = new Vector3(Mathf.Sin(ph) * ry, y, Mathf.Cos(ph) * ry);
                    if (s > 0)
                    {
                        var d = p - last; float w = Mathf.Lerp(w0, 0.012f, s / (float)steps); var rot = Quaternion.LookRotation(d).eulerAngles;
                        Box(bell, (p + last) / 2, new Vector3(w * 1.9f, w * 1.2f, d.magnitude + 0.02f), mGroove, rot, "Groove");
                        Box(bell, (p + last) / 2 + p.normalized * 0.01f, new Vector3(w * 0.55f, w * 1.2f, d.magnitude + 0.02f), mCrack, rot, "Crack");
                        if (depth > 0 && s == steps / 2) Crack(ph + side * 0.12f, y, w * 0.7f, 4, depth - 1, side);
                    }
                    last = p; y += 0.3f + (float)jag.NextDouble() * 0.35f; ph += side * (0.02f + (float)jag.NextDouble() * 0.06f) + ((float)jag.NextDouble() - 0.5f) * 0.12f; if (y > 7.6f) break;
                }
            }
            for (int c = 0; c < 6; c++) { float side = c % 2 == 0 ? 1 : -1; Crack(side * (gapHalf * 1.9f + c * 0.12f), 0.2f + c * 0.5f, 0.05f, 9, 1, side); }
            // two broken shards drifting off the gap
            var shard = MeshGO("Shard", Lathe("BellShard", prof.Skip(8).Take(24).ToArray(), 6, j => Mathf.PI - 0.22f - 0.05f * (j % 3)), bell, new Vector3(-6.2f, 2.2f, 2.6f), Quaternion.Euler(25, 35, -20), mBronze);
            shard.transform.localScale = Vector3.one * 0.75f;
            MeshGO("Shard2", Lathe("BellShard2", prof.Skip(14).Take(14).ToArray(), 4, j => Mathf.PI - 0.12f - 0.04f * (j % 2)), shard.transform, new Vector3(2.4f, -1.2f, 1.4f), Quaternion.Euler(-30, 60, 10), mBronze);
            // cast crown with a yoke beam, clapper, and two heavy chains rising out of frame
            float topY = prof[0].y;
            MeshGO("Crown", Torus("BellCrown", 0.75f, 0.24f, 32, 10), bell, new Vector3(0, topY + 0.7f, 0), Quaternion.Euler(90, 0, 0), mBronze);
            Box(bell, new Vector3(0, topY + 1.55f, 0), new Vector3(3.4f, 0.45f, 0.55f), mIron, default, "Yoke");
            foreach (var x in new[] { -0.55f, 0.55f }) Box(bell, new Vector3(x, topY + 1.0f, 0), new Vector3(0.14f, 1.0f, 0.4f), mIron, default, "Strap");
            Box(bell, new Vector3(0, topY * 0.55f, 0), new Vector3(0.18f, topY * 0.8f, 0.18f), mIron, default, "ClapperRod");
            Prim(PrimitiveType.Sphere, bell, new Vector3(0, 1.0f, 0), Vector3.one * 1.1f, mIron, default, "Clapper");
            var link = Torus("BellChainLink", 0.26f, 0.075f, 20, 8, 360, 0, new Vector3(1, 1, 1.55f));
            foreach (var x in new[] { -1.5f, 1.5f })
            {
                var top = bell.TransformPoint(new Vector3(x * 2.4f, topY + 40, 0)); var bot = bell.TransformPoint(new Vector3(x, topY + 1.6f, 0));
                Chain(root, x < 0 ? "BellChain_L" : "BellChain_R", bot, top, 0.01f, 0.8f, link);
            }
            // interior light (rakes the north stair) and a faint cone below the mouth
            var lg = Group("InteriorLight", bell, new Vector3(0, -4.5f, 1.5f)).AddComponent<Light>();
            lg.type = LightType.Point; lg.color = Hex(0xFFB060); lg.range = 24; lg.intensity = 4.5f; lg.shadows = LightShadows.None;
            var cone = MeshGO("LightCone", ConeShell(5.2f, 7.5f, 6), bell, new Vector3(0, -6, 0), mCone); cone.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            // astrolabe behind the bell: band with degree scale, rete (eccentric rings + star pointers), hub and pointer
            var astro = Group("AstrolabePivot", root, new Vector3(0, 2, -30), Quaternion.Euler(90, 0, 0)).transform;
            var ring = ClockRing(astro, "Astrolabe", 14, 2.4f, 290, 110, 0); foreach (var mr in ring.GetComponentsInChildren<MeshRenderer>()) if (mr.sharedMaterial == mBrass) mr.sharedMaterial = mAstroBrass;
            MeshGO("NumeralRing", Torus("AstroNumeralRing", 9.6f, 0.22f, 192, 6), ring, Vector3.zero, mBrass);
            MeshGO("ReteA", Torus("AstroReteA", 6.2f, 0.22f, 128, 6), ring, new Vector3(1.8f, 0.05f, 0.6f), mBrassPolished);
            MeshGO("ReteB", Torus("AstroReteB", 4.0f, 0.18f, 96, 6), ring, new Vector3(-1.4f, 0.08f, -0.9f), mBrass);
            for (int i = 0; i < 4; i++) MeshGO("ReteArm", Torus("AstroReteArm", 5.2f, 0.2f, 48, 6, 75, i * 90), ring, Polar(i * 90 + 200, 2.6f, 0.1f), mBrassPolished); // curved rete arms
            for (int i = 0; i < 4; i++) Box(ring, Polar(i * 90 + 20, 5.3f), new Vector3(0.5f, 0.45f, 8.0f), mBrass, new Vector3(0, 70 - i * 90, 0), "Spoke");
            var star = Pyramid("AstroStar", 0.18f, 0.9f, 3, 0);
            for (int i = 0; i < 9; i++) { float a = i * 40 + 13, rr = 3.5f + (i % 3) * 2.2f; MeshGO("Star", star, ring, Polar(a, rr, 0.15f), Quaternion.Euler(0, 90 - a, 0) * Quaternion.Euler(90, 0, 0), mBrassPolished); }
            Prim(PrimitiveType.Cylinder, ring, Vector3.zero, new Vector3(3, 0.3f, 3), mBrassPolished, default, "Hub");
            Box(ring, Polar(60, 4.5f, 0.3f), new Vector3(0.3f, 0.12f, 9.2f), mIron, new Vector3(0, 30, 0), "Pointer");
            spinners.Add(ring); spinSpeeds.Add(1.5f);
            var frags = Group("AstrolabeFragments", astro).transform;
            for (int i = 0; i < 4; i++) { var fr = Box(frags, Polar(40 + i * 6, 14.3f + i * 0.5f, Rr(-0.6f, 0.6f)), new Vector3(Rr(0.5f, 1.0f), 0.24f, Rr(0.8f, 1.6f)), mBrass, new Vector3(Rr(-30, 30), Rr(0, 90), Rr(-30, 30)), "Fragment"); bobbers.Add(fr.transform); }
            return (lg, shard.transform);
        }

        // Open cone shell (top radius r0 at y = h, bottom radius r1 at y = 0) with uv.y = 1 at the top, for the additive shaft.
        static Mesh ConeShell(float r0, float r1, float h)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>(); const int seg = 32;
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(d * r1); uv.Add(new Vector2(i / (float)seg * 6 % 1, 0)); v.Add(d * r0 + Vector3.up * h); uv.Add(new Vector2(i / (float)seg * 6 % 1, 1));
                if (i < seg) { int b = i * 2; t.AddRange(new[] { b, b + 1, b + 2, b + 1, b + 3, b + 2 }); }
            }
            var m = new Mesh { name = "BellCone" }; m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static void BuildLighting(Transform g)
        {
            var sun = Group("MoonLight", g).AddComponent<Light>(); sun.type = LightType.Directional; sun.color = Hex(0xF2E6DA); sun.intensity = 1.2f;
            sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.8f;
            var from = (Out(315) * Mathf.Cos(55 * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(55 * Mathf.Deg2Rad)).normalized;
            sun.transform.rotation = Quaternion.LookRotation(-from);
            // cool fill from the camera side and a cool rim from behind, so dark architecture separates from the void
            var fill = Group("CoolFill", g).AddComponent<Light>(); fill.type = LightType.Directional; fill.color = Hex(0x6F7CFF); fill.intensity = 0.45f; fill.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.LookRotation(-(Out(150) * 0.6f + Vector3.up * 0.8f).normalized);
            var rim = Group("CoolRim", g).AddComponent<Light>(); rim.type = LightType.Directional; rim.color = Hex(0x8C9BFF); rim.intensity = 0.85f; rim.shadows = LightShadows.None;
            rim.transform.rotation = Quaternion.LookRotation(-(Out(10) * 0.85f + Vector3.up * 0.5f).normalized);
            RenderSettings.sun = sun;
            RenderSettings.skybox = mSky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex(0x6E4FC2, 0.3f); RenderSettings.ambientEquatorColor = Hex(0x2B2358, 0.45f); RenderSettings.ambientGroundColor = Hex(0x0E0A1E);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = 0.013f; RenderSettings.fogColor = Hex(0x3B3270);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox; RenderSettings.reflectionIntensity = 0.5f; // the large realtime probe below covers the set
            var vol = Group("GlobalVolume", g).AddComponent<Volume>(); vol.isGlobal = true; vol.sharedProfile = MakeProfile();
            var probe = Group("FloorReflectionProbe", g, new Vector3(0, 3, 0)).AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime; probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting; probe.size = new Vector3(220, 140, 220); probe.boxProjection = false; probe.importance = 1; probe.intensity = 1;
        }

        static VolumeProfile MakeProfile()
        {
            string path = Gen + "/ShatteredHourVolume.asset"; AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, path);
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true); bloom.threshold.Override(1f); bloom.intensity.Override(0.55f); bloom.scatter.Override(0.6f);
            var col = profile.Add<ColorAdjustments>(true); col.postExposure.Override(0.1f); col.contrast.Override(12f); col.saturation.Override(5f);
            var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.25f); vig.color.Override(Hex(0x0A061C));
            profile.Add<WhiteBalance>(true).temperature.Override(-3f); // -8 turned the gold inlay pink
            foreach (var c in profile.components) { c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy; AssetDatabase.AddObjectToAsset(c, profile); }
            AssetDatabase.SaveAssets(); return profile;
        }

        // ---------------------------------------------------------------- batch: build, then the review screenshots + stats
        // Unity.exe -batchmode -projectPath unity -executeMethod LastBell.Areas.Area4.EditorTools.ShatteredHourArenaBuilder.BatchBuildAndCapture
        public static void BatchBuildAndCapture()
        {
            int code = 0;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                Build(); EditorSceneManager.OpenScene(ScenePath);
                CaptureAll();
            }
            catch (System.Exception e) { Debug.LogException(e); code = 1; }
            EditorApplication.Exit(code);
        }

        static void Shot(string shots, string file) { Capture(Path.Combine(shots, file)); Capture(Path.Combine(shots, file)); } // warm-up frame first

        public static void CaptureAll()
        {
            string shots = Path.GetFullPath(ShotDir); Directory.CreateDirectory(shots);
            var drv = Object.FindFirstObjectByType<ShatteredHourArena>(); var probe = Object.FindFirstObjectByType<ReflectionProbe>(); var cam = Camera.main;
            if (probe) probe.RenderProbe();
            drv.SetView(ShatteredHourArena.View.Gameplay); Shot(shots, "gameplay.png");
            File.WriteAllText(Path.Combine(shots, "stats.txt"), Stats(cam));
            drv.SetView(ShatteredHourArena.View.Establishing); Shot(shots, "establishing.png");
            drv.SetView(ShatteredHourArena.View.TopDown); if (probe) probe.RenderProbe(); Shot(shots, "topdown.png");
            void Pose(Vector3 pos, Vector3 look, float fov) { cam.orthographic = false; cam.fieldOfView = fov; cam.transform.position = pos; cam.transform.LookAt(look); }
            Pose(new Vector3(0, 9, 40), new Vector3(0, 0, 13), 50); Shot(shots, "south-entrance.png");
            Pose(new Vector3(7.5f, 6.5f, -5f), new Vector3(0, 4.5f, -17.5f), 45); Shot(shots, "bell-closeup.png");
            Pose(new Vector3(-5.2f, 3.4f, -5.2f), new Vector3(-9.6f, 0.4f, -9.6f), 45); Shot(shots, "station-closeup.png");
            drv.SetView(ShatteredHourArena.View.Gameplay);
            // telegraph readability test: a coral 4 m circle at ~60 % opacity
            var tm = new Material(Shader.Find("Universal Render Pipeline/Unlit")); tm.SetColor("_BaseColor", new Color(1f, 0.42f, 0.35f, 0.6f)); MakeTransparent(tm, true);
            var disc = new Mesh(); { var v = new List<Vector3> { Vector3.zero }; var t = new List<int>(); for (int i = 0; i <= 64; i++) { float a = i / 64f * Mathf.PI * 2; v.Add(new Vector3(Mathf.Cos(a) * 2, 0, Mathf.Sin(a) * 2)); if (i < 64) { t.Add(0); t.Add(i + 2); t.Add(i + 1); } } disc.SetVertices(v); disc.SetTriangles(t, 0); disc.RecalculateNormals(); }
            var tele = new GameObject("TelegraphTest"); tele.transform.position = new Vector3(5, 0.02f, 3); tele.AddComponent<MeshFilter>().sharedMesh = disc; tele.AddComponent<MeshRenderer>().sharedMaterial = tm;
            Shot(shots, "telegraph-test.png"); Object.DestroyImmediate(tele);
            // eight players at r 11
            var pm = new Material(lit); pm.SetColor("_BaseColor", Hex(0x3C6BE0)); var caps = new List<GameObject>();
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2 + 0.2f; var c = Prim(PrimitiveType.Capsule, null, new Vector3(Mathf.Cos(a) * 11, 0.9f, Mathf.Sin(a) * 11), new Vector3(0.6f, 0.9f, 0.6f), pm, default, "TestPlayer");
                caps.Add(c); ShatteredHourOccluderFade.Players.Add(c.transform);
            }
            foreach (var f in Object.FindObjectsByType<ShatteredHourOccluderFade>(FindObjectsSortMode.None)) f.Tick(cam, 10);
            Shot(shots, "eight-players.png");
            ShatteredHourOccluderFade.Players.Clear(); foreach (var f in Object.FindObjectsByType<ShatteredHourOccluderFade>(FindObjectsSortMode.None)) f.Tick(cam, 10);
            foreach (var c in caps) Object.DestroyImmediate(c);
        }

        // Triangles and draw calls (renderer sub-meshes inside the gameplay frustum, before any batching), and lights.
        static string Stats(Camera cam)
        {
            var planes = GeometryUtility.CalculateFrustumPlanes(cam); long tris = 0, allTris = 0; int draws = 0, rends = 0, allDraws = 0;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                var mf = r.GetComponent<MeshFilter>(); if (!r.enabled || !mf || !mf.sharedMesh) continue; var m = mf.sharedMesh; long t = 0;
                for (int s = 0; s < m.subMeshCount; s++) t += m.GetIndexCount(s) / 3;
                allTris += t; allDraws += m.subMeshCount;
                if (!GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
                tris += t; draws += m.subMeshCount; rends++;
            }
            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            int points = lights.Count(l => l.type == LightType.Point), shadowed = lights.Count(l => l.shadows != LightShadows.None);
            return $"Gameplay camera (frustum-culled, before batching)\ntriangles: {tris:N0}\ndraw calls (renderer sub-meshes): {draws}\nrenderers: {rends}\n" +
                   $"Whole scene\ntriangles: {allTris:N0}\ndraw calls: {allDraws}\nlights: {lights.Length} (point {points}, shadow-casting {shadowed})\n";
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

    // Import settings for the generated textures: height maps become normal maps; big maps keep their size.
    sealed class ShatteredHourTextureImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/LastBell/Areas/Area4/Generated/")) return;
            var t = (TextureImporter)assetImporter; t.maxTextureSize = 2048; t.mipmapEnabled = true; t.anisoLevel = 8;
            if (assetPath.EndsWith("FloorAlbedo.png")) t.maxTextureSize = 4096;
            float scale = assetPath.EndsWith("FloorHeight.png") ? 0.015f : assetPath.EndsWith("StoneTrimHeight.png") ? 0.05f : assetPath.EndsWith("RockStrataHeight.png") ? 0.04f : 0.03f;
            if (assetPath.EndsWith("Height.png")) { t.textureType = TextureImporterType.NormalMap; t.convertToNormalmap = true; t.heightmapScale = scale; t.normalmapFilter = TextureImporterNormalFilter.Sobel; }
            if (assetPath.EndsWith("Banner.png")) t.alphaIsTransparency = true;
            if (assetPath.Contains("StoneTrim") || assetPath.Contains("Brass") || assetPath.Contains("Strata")) t.wrapMode = TextureWrapMode.Repeat;
        }
    }
}
