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
    // arena, from prototype/arena-3d/areas/area-4/SHATTERED-HOUR-UNITY-PROMPT.md and its top-down plan
    // (ref-topdown-layout.webp). Everything is generated here: meshes, painted floor textures, materials, the scene and
    // the two prefab roots. Environment only: the fissure and the four rim stations are decoration, no boss, no hazards.
    // Axes: north (the bell) is −Z, south (the entrance, the camera side) is +Z. The gameplay camera faces north, so
    // screen-right ("east" on the plan) is world −X; P(deg, r) places things by plan compass degrees.
    public static class ShatteredHourArenaBuilder
    {
        const string Dir = "Assets/LastBell/Areas/Area4", Gen = Dir + "/Generated", PrefabDir = Dir + "/Prefabs", ShotDir = Dir + "/Shots";
        const string ScenePath = Dir + "/ShatteredHour_FinalArena.unity";
        const float R = 13.5f, RimOut = 15.1f, RimH = 0.9f, CapY = 0.98f, FloorH = 13.6f;
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

        static readonly Dictionary<string, int> names = new Dictionary<string, int>();
        static T Save<T>(T asset, string file) where T : Object
        {
            string path = Gen + "/" + file; AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(asset, path); return asset;
        }
        static Mesh SaveMesh(Mesh m)
        {
            names.TryGetValue(m.name, out int n); names[m.name] = n + 1;
            return Save(m, (n == 0 ? m.name : m.name + "_" + n) + ".asset");
        }

        sealed class MB
        {
            readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); readonly List<Vector2> u = new List<Vector2>(); readonly List<int> t = new List<int>();
            public float uvScale = 1;
            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 hint)
            {
                var nn = Vector3.Cross(b - a, c - a); if (Vector3.Dot(nn, hint) < 0) { (b, c) = (c, b); nn = -nn; }
                nn.Normalize(); int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); n.Add(nn); n.Add(nn); n.Add(nn); u.Add(UV(a, nn)); u.Add(UV(b, nn)); u.Add(UV(c, nn));
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 hint) { Tri(a, b, c, hint); Tri(a, c, d, hint); }
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

        // Annular prism between radii r0..r1 and heights y0..y1, Unity polar angles t0..t1 (degrees).
        static Mesh ArcPrism(string name, float r0, float r1, float y0, float y1, float t0, float t1, bool ends = true, float uvs = 1, float step = 2f, bool inner = true)
        {
            var mb = new MB { uvScale = uvs }; int n = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(t1 - t0) / step));
            Vector3 Y0 = Vector3.up * y0, Y1 = Vector3.up * y1;
            Vector3 D(float deg) { float a = deg * Mathf.Deg2Rad; return new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); }
            for (int i = 0; i < n; i++)
            {
                Vector3 da = D(Mathf.Lerp(t0, t1, i / (float)n)), db = D(Mathf.Lerp(t0, t1, (i + 1) / (float)n)), mid = (da + db).normalized;
                mb.Quad(da * r0 + Y1, db * r0 + Y1, db * r1 + Y1, da * r1 + Y1, Vector3.up);
                if (inner) mb.Quad(da * r0 + Y0, db * r0 + Y0, db * r0 + Y1, da * r0 + Y1, -mid);
                mb.Quad(da * r1 + Y0, db * r1 + Y0, db * r1 + Y1, da * r1 + Y1, mid);
            }
            if (ends)
                foreach (var (tt, s) in new[] { (t0, -1f), (t1, 1f) })
                {
                    float a = tt * Mathf.Deg2Rad; var d = D(tt); var tan = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a)) * s * Mathf.Sign(t1 - t0);
                    mb.Quad(d * r0 + Y0, d * r1 + Y0, d * r1 + Y1, d * r0 + Y1, tan);
                }
            return SaveMesh(mb.Build(name));
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
            return SaveMesh(m);
        }

        // Torus around local Y (ring in the XZ plane), optional arc from startDeg spanning arcDeg.
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
                    uv.Add(new Vector2(i / (float)segU, j / (float)segV));
                }
            }
            for (int i = 0; i < segU; i++) for (int j = 0; j < segV; j++)
                {
                    int a = i * (segV + 1) + j, b = a + segV + 1;
                    Wind(t, v, nrm, a, b, a + 1); Wind(t, v, nrm, b, b + 1, a + 1);
                }
            var m = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(v); m.SetNormals(nrm); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds(); m.RecalculateTangents();
            return SaveMesh(m);
        }
        static void Wind(List<int> t, List<Vector3> v, List<Vector3> n, int a, int b, int c)
        {
            var cr = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (Vector3.Dot(cr, n[a] + n[b] + n[c]) < 0) { t.Add(a); t.Add(c); t.Add(b); } else { t.Add(a); t.Add(b); t.Add(c); }
        }

        // Lathe around Y: profile points (radius, height), phi measured from +Z (x = sin, z = cos), so phi = 0 faces south.
        static Mesh Lathe(string name, Vector2[] prof, int seg, float phiStart, float phiLen)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float ph = phiStart + phiLen * i / seg;
                for (int j = 0; j < prof.Length; j++) { v.Add(new Vector3(Mathf.Sin(ph) * prof[j].x, prof[j].y, Mathf.Cos(ph) * prof[j].x)); uv.Add(new Vector2(i / (float)seg, j / (prof.Length - 1f))); }
            }
            int k = prof.Length;
            for (int i = 0; i < seg; i++) for (int j = 0; j < k - 1; j++)
                {
                    int a = i * k + j, b = a + k;
                    t.Add(a); t.Add(b); t.Add(a + 1); t.Add(b); t.Add(b + 1); t.Add(a + 1);
                }
            var m = new Mesh { name = name }; m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateNormals();
            { var nn = m.normals; int j = Mathf.Min(5, k - 1), vi = (seg / 2) * k + j; var radial = new Vector3(v[vi].x, 0, v[vi].z);
              if (Vector3.Dot(nn[vi], radial) < 0) { for (int q = 0; q < t.Count; q += 3) (t[q + 1], t[q + 2]) = (t[q + 2], t[q + 1]); m.SetTriangles(t, 0); m.RecalculateNormals(); } }
            m.RecalculateBounds(); m.RecalculateTangents();
            return SaveMesh(m);
        }

        // n-sided pyramid / cone, base at y = 0, apex at y = h (negative h points down).
        static Mesh Pyramid(string name, float baseR, float h, int sides = 4, float rot = 45, float jitter = 0)
        {
            var mb = new MB(); var apex = Vector3.up * h; var ring = new Vector3[sides];
            for (int i = 0; i < sides; i++) { float a = (rot + i * 360f / sides) * Mathf.Deg2Rad, j = 1 + (jitter > 0 ? Rr(-jitter, jitter) : 0); ring[i] = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * baseR * j; }
            for (int i = 0; i < sides; i++)
            {
                var a = ring[i]; var b = ring[(i + 1) % sides]; var mid = (a + b) * 0.5f;
                mb.Tri(a, b, apex, new Vector3(mid.x, 0, mid.z).normalized + Vector3.up * Mathf.Sign(h) * 0.2f);
                mb.Tri(Vector3.zero, b, a, Vector3.down * Mathf.Sign(h));
            }
            return SaveMesh(mb.Build(name));
        }

        static Mesh Rock(string name, float jitter)
        {
            float g = (1 + Mathf.Sqrt(5)) / 2;
            var p = new List<Vector3> { new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0), new Vector3(0, -1, g), new Vector3(0, 1, g),
                new Vector3(0, -1, -g), new Vector3(0, 1, -g), new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1) };
            for (int i = 0; i < p.Count; i++) p[i] = p[i].normalized * (1 + Rr(-jitter, jitter));
            int[] f = { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
            var mb = new MB(); var sq = new Vector3(1, Rr(0.55f, 0.9f), 1);
            for (int i = 0; i < f.Length; i += 3) { Vector3 a = Vector3.Scale(p[f[i]], sq), b = Vector3.Scale(p[f[i + 1]], sq), c = Vector3.Scale(p[f[i + 2]], sq); mb.Tri(a, b, c, a + b + c); }
            return SaveMesh(mb.Build(name));
        }

        // Inverted jagged spire: rings from radius r at y = 0 down to a point at y = -depth.
        static Mesh Spire(string name, float r, float depth, int sides, int rings, float jitter)
        {
            var mb = new MB(); var pts = new Vector3[rings + 1][];
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
            return SaveMesh(mb.Build(name));
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
        static GameObject Box(Transform parent, Vector3 pos, Vector3 scale, Material mat, Vector3 euler = default, string name = "Block") => Prim(PrimitiveType.Cube, parent, pos, scale, mat, euler, name);
        static GameObject MeshGO(string name, Mesh mesh, Transform parent, Vector3 pos, Quaternion rot, params Material[] mats)
        {
            var g = Group(name, parent, pos, rot);
            g.AddComponent<MeshFilter>().sharedMesh = mesh; g.AddComponent<MeshRenderer>().sharedMaterials = mats; return g;
        }
        static GameObject MeshGO(string name, Mesh mesh, Transform parent, Vector3 pos, Material mat) => MeshGO(name, mesh, parent, pos, Quaternion.identity, mat);

        // Merges a temporary hierarchy into one mesh with one sub-mesh per material, then deletes the hierarchy.
        static Mesh Combine(GameObject root, Material[] mats, string name)
        {
            var lists = mats.Select(_ => new List<CombineInstance>()).ToArray();
            var w2l = root.transform.worldToLocalMatrix;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                var mr = mf.GetComponent<MeshRenderer>(); int k = System.Array.IndexOf(mats, mr.sharedMaterial); if (k < 0) k = 0;
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) lists[k].Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = s, transform = w2l * mf.transform.localToWorldMatrix });
            }
            var subs = lists.Select(l => { var m = new Mesh { indexFormat = IndexFormat.UInt32 }; m.CombineMeshes(l.ToArray(), true, true); return new CombineInstance { mesh = m, transform = Matrix4x4.identity }; }).ToArray();
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 }; mesh.CombineMeshes(subs, false, false);
            mesh.RecalculateBounds(); Object.DestroyImmediate(root);
            return SaveMesh(mesh);
        }

        // Equilateral pointed arch from cube segments: springs at ±span/2 at height springY (local XY plane).
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

        // ---------------------------------------------------------------- materials
        static Shader lit;
        static Material Lit(string name, Color c, float metal = 0, float smooth = 0.2f, Color? emis = null, bool dbl = false, Texture2D map = null)
        {
            var m = new Material(lit) { name = name, enableInstancing = true };
            m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
            if (map) m.SetTexture("_BaseMap", map);
            if (emis.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emis.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; }
            if (dbl) { m.SetFloat("_Cull", 0); m.doubleSidedGI = true; }
            return Save(m, name + ".mat");
        }
        static void MakeTransparent(Material m, bool unlit = false)
        {
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent; m.SetShaderPassEnabled("DepthOnly", false); m.SetShaderPassEnabled("ShadowCaster", unlit);
        }
        static Material FadedTwin(Material src)
        {
            var m = new Material(src) { name = src.name + "_Fade" }; MakeTransparent(m); return Save(m, m.name + ".mat");
        }

        static Material mWindow, mFloor, mSlate, mSlateBrick, mSlateCap, mDeck, mBrass, mBrassPolished, mIron, mBronze, mCrack, mBellGlow, mFlame,
            mRock, mRockLight, mVoidStone, mBanner, mStation, mPedestal, mVortex, mShaft, mCone, mSky;
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
        static float Fbm(float x, float y) => VNoise(x, y) * 0.55f + VNoise(x * 2.1f, y * 2.1f, 1) * 0.3f + VNoise(x * 4.3f, y * 4.3f, 2) * 0.15f;
        static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }
        static Color Lerp3(Color a, Color b, float t) => Color.LerpUnclamped(a, b, t);

        static Texture2D WritePng(string file, Color32[] px, int w, int h, bool alpha = false)
        {
            var tex = new Texture2D(w, h, alpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, false); tex.SetPixels32(px); tex.Apply();
            string path = Gen + "/" + file; File.WriteAllBytes(Path.GetFullPath(path), tex.EncodeToPNG()); Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static Color32 C32(Color c) => new Color32((byte)Mathf.Clamp(c.r * 255, 0, 255), (byte)Mathf.Clamp(c.g * 255, 0, 255), (byte)Mathf.Clamp(c.b * 255, 0, 255), (byte)Mathf.Clamp(c.a * 255, 0, 255));

        // Fissure polylines in plan coordinates (u = screen-right, v = north) with per-point width.
        static List<(Vector2 p, float w)[]> Fissure()
        {
            var r = new System.Random(19); float F() => (float)r.NextDouble();
            var lines = new List<(Vector2, float)[]>();
            (Vector2, float)[] Walk(Vector2 from, Vector2 to, float w0, float w1)
            {
                var pts = new List<(Vector2, float)> { (from, w0) }; var p = from; float total = (to - from).magnitude;
                for (int guard = 0; guard < 200 && (to - p).magnitude > 0.5f; guard++)
                {
                    var dir = (to - p).normalized; float ang = (F() * 2 - 1) * 25 * Mathf.Deg2Rad, step = 0.3f + F() * 0.3f;
                    dir = new Vector2(dir.x * Mathf.Cos(ang) - dir.y * Mathf.Sin(ang), dir.x * Mathf.Sin(ang) + dir.y * Mathf.Cos(ang));
                    p += dir * step; float f = 1 - (to - p).magnitude / total; pts.Add((p, Mathf.Lerp(w0, w1, f) * (0.85f + 0.3f * F())));
                }
                pts.Add((to, w1)); return pts.ToArray();
            }
            Vector2 nw = new Vector2(-1, 1).normalized, se = -nw;
            var a = Walk(nw * 12.3f, nw * 3.2f + new Vector2(0.25f, 0.1f), 0.14f, 0.22f);
            var b = Walk(se * 3.2f + new Vector2(-0.1f, -0.25f), se * 12.3f, 0.22f, 0.13f);
            lines.Add(a); lines.Add(b);
            foreach (var main in new[] { a, b })
                for (int k = 0; k < 3; k++)
                {
                    var (p0, w) = main[2 + (int)(F() * (main.Length - 5))];
                    var mdir = (main[main.Length - 1].Item1 - main[0].Item1).normalized; float side = F() < 0.5f ? 1 : -1;
                    var dir = new Vector2(-mdir.y, mdir.x) * side * 0.8f + mdir * 0.6f; float len = 0.8f + F() * 0.7f;
                    lines.Add(Walk(p0, p0 + dir.normalized * len, w * 0.55f, 0.03f));
                }
            return lines;
        }

        static (Texture2D albedo, Texture2D height, Texture2D emission) PaintFloor()
        {
            int N = FN; float H = FloorH, px = 2 * H / N;
            var alb = new Color32[N * N]; var hgt = new Color32[N * N]; var emi = new Color32[N * N];
            Color c1 = Hex(0x1E2433), c2 = Hex(0x2A3142), cBlue = Hex(0x252D45), grout = Hex(0x0E1018), brass = Hex(0xB8904E), plate = Hex(0x1A2036), rimStone = Hex(0x1A1E2B);
            Color gold = new Color(1f, 0.72f, 0.4f);
            float Cov(float dist, float half) => Mathf.Clamp01((half - dist) / px + 0.5f);
            Parallel.For(0, N, py =>
            {
                float v = -H + (py + 0.5f) * px;
                for (int ix = 0; ix < N; ix++)
                {
                    float u = -H + (ix + 0.5f) * px, r = Mathf.Sqrt(u * u + v * v), ang = Mathf.Atan2(v, u);
                    int o = py * N + ix;
                    // --- stone: irregular voronoi flagstones; a ring of rim slabs; a smooth plate under the medallion
                    float edge; int id0, id1; Color c;
                    float n = Fbm(u * 0.45f, v * 0.45f), fine = VNoise(u * 6, v * 6, 3);
                    if (r > 12.6f)
                    {
                        float seg = Mathf.PI * 2 / 48, a = ang + Mathf.PI, k = Mathf.Floor(a / seg), fa = a - k * seg;
                        edge = Mathf.Min(Mathf.Min(r - 12.6f, 13.6f - r), Mathf.Min(fa * r, (seg - fa) * r)); id0 = (int)k; id1 = 99;
                        c = rimStone * (0.9f + 0.2f * Hash(id0, 5, 2));
                    }
                    else if (r < 3.2f)
                    {
                        if (r < 2.6f) { edge = 1; id0 = 0; id1 = 0; c = plate; }
                        else { float seg = Mathf.PI * 2 / 24, a = ang + Mathf.PI, k = Mathf.Floor(a / seg), fa = a - k * seg; edge = Mathf.Min(Mathf.Min(r - 2.6f, 3.2f - r), Mathf.Min(fa * r, (seg - fa) * r)); id0 = (int)k; id1 = 77; c = Hex(0x20263A); }
                    }
                    else
                    {
                        // inside r 7.5 the slabs run radially (voronoi in polar space, seamless around), outside they are free
                        bool polar = r < 7.5f; const int PN = 17; float pS = Mathf.PI * 2 * 5.4f / PN, S = polar ? pS : 2.7f;
                        float qu = polar ? (ang + Mathf.PI) * 5.4f : u, qv = polar ? r * 1.15f : v;
                        int gx = Mathf.FloorToInt(qu / S), gy = Mathf.FloorToInt(qv / S); float f1 = 99, f2 = 99; id0 = id1 = 0;
                        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                            {
                                int cx = gx + dx, cy = gy + dy, hx = polar ? (int)Mathf.Repeat(cx, PN) : cx;
                                float sx = (cx + 0.05f + 0.9f * Hash(hx, cy, polar ? 21 : 11)) * S, sy = (cy + 0.05f + 0.9f * Hash(hx, cy, polar ? 22 : 12)) * S;
                                float d = Mathf.Sqrt((qu - sx) * (qu - sx) + (qv - sy) * (qv - sy));
                                if (d < f1) { f2 = f1; f1 = d; id0 = hx + (polar ? 500 : 0); id1 = cy; } else if (d < f2) f2 = d;
                            }
                        edge = (f2 - f1) * 0.5f; if (polar) edge = Mathf.Min(edge, Mathf.Min(Mathf.Abs(r - 7.5f), Mathf.Abs(r - 3.2f)));
                        float t = Hash(id0, id1, 7); c = Color.Lerp(c1, c2, 0.35f + 0.3f * t); if (Hash(id0, id1, 9) < 0.25f) c = Color.Lerp(c, cBlue, 0.6f);
                        c *= 0.94f + 0.12f * Hash(id0, id1, 13);  // ±6 % per slab
                    }
                    c *= 0.88f * (0.9f + 0.2f * n); { float gy = (c.r + c.g + c.b) / 3; c = Color.Lerp(new Color(gy, gy, gy), c, 0.85f); } c *= 0.96f + 0.08f * fine;
                    float hh = 0.62f + 0.38f * Smooth(0.01f, 0.06f, edge) + (n - 0.5f) * 0.1f;
                    if (edge < 0.04f) c *= Mathf.Lerp(0.88f, 1, Smooth(0.01f, 0.04f, edge));
                    float chip = VNoise(u * 2.3f + id0 * 9.1f, v * 2.3f + id1 * 3.7f, 5);
                    if (chip < 0.07f && r > 2.6f) { c *= 0.88f; hh -= 0.08f; }
                    if (fine > 0.9f) c *= 1.05f;
                    if (edge < 0.007f) { c = Color.Lerp(grout, c, 0.6f); hh = 0.4f; }
                    // --- brass inlay
                    float ink = 0, em = 0;
                    void Ink(float cov, float alpha, float e) { cov *= alpha; if (cov > ink) ink = cov; if (cov * e > em) em = cov * e; }
                    foreach (var (rr, hw, al, e) in Rings) if (Mathf.Abs(r - rr) < hw + px) Ink(Cov(Mathf.Abs(r - rr), hw), al, e);
                    if (r < 0.34f) Ink(Cov(r, 0.32f), 1, 0.14f);
                    if (r > 1f && r < 2.6f)
                    {
                        float seg = Mathf.PI * 2 / 24, k = Mathf.Round(ang / seg), da = ang - k * seg; bool lng = ((int)k & 1) == 0;
                        float len = lng ? 1.6f : 1.0f, f = (r - 1f) / len;
                        if (f < 1) Ink(Cov(Mathf.Abs(Mathf.Sin(da)) * r, 0.09f * (1 - f)), 0.85f, 0.14f);
                    }
                    if (r > 3.2f && r < 12.6f) Ink(Cov(Mathf.Min(Mathf.Abs(u), Mathf.Abs(v)), 0.02f), 0.8f, 0.078f);
                    for (int q = 0; q < 4; q++)
                    {
                        float qa = q * Mathf.PI / 2; float cxu = Mathf.Cos(qa), cxv = Mathf.Sin(qa);
                        float du = u - cxu * 10.2f, dv = v - cxv * 10.2f, sr = Mathf.Sqrt(du * du + dv * dv);
                        if (sr < 0.36f) { float sa = Mathf.Atan2(dv, du); float lim = 0.32f * (0.3f + 0.7f * Mathf.Pow(Mathf.Abs(Mathf.Cos(sa * 4)), 6) * (0.7f + 0.3f * Mathf.Abs(Mathf.Cos(sa * 2)))); Ink(Cov(sr, lim), 1f, 0.094f); }
                        du = u - cxu * 6.5f; dv = v - cxv * 6.5f; float man = Mathf.Abs(du) + Mathf.Abs(dv);
                        if (man < 0.22f) Ink(Cov(man, 0.17f), 0.8f, 0.078f);
                        float da2 = qa + Mathf.PI / 4; float ou = Mathf.Cos(da2), ov = Mathf.Sin(da2);
                        float lu = u - ou * 11.4f, lv = v - ov * 11.4f, along = lu * ou + lv * ov, across = Mathf.Abs(-lu * ov + lv * ou);
                        if (along > -0.2f && along < 0.25f) Ink(Cov(across, 0.22f * (0.25f - along) / 0.45f), 1f, 0.078f);
                    }
                    if (r > 12.65f && r < 13.35f)
                    {
                        float seg = Mathf.PI * 2 / 48, k = Mathf.Round(ang / seg), d = Mathf.Abs(ang - k * seg) * r; bool lng = ((int)Mathf.Repeat(k, 48) % 4) == 0;
                        if (r < (lng ? 13.3f : 12.95f)) Ink(Cov(d, lng ? 0.045f : 0.03f), 1f, 0.078f);
                    }
                    var bc = brass * (0.88f + 0.24f * fine); c = Color.Lerp(c, bc, ink); hh += ink * 0.08f;
                    c.a = 1; alb[o] = C32(c);
                    byte hb = (byte)Mathf.Clamp(hh * 255, 0, 255); hgt[o] = new Color32(hb, hb, hb, 255);
                    emi[o] = C32(new Color(gold.r * em, gold.g * em, gold.b * em, 1));
                }
            });
            // hairline cracks
            var cr = new System.Random(5);
            for (int i = 0; i < 120; i++)
            {
                float x = ((float)cr.NextDouble() * 2 - 1) * 12.5f, y = ((float)cr.NextDouble() * 2 - 1) * 12.5f, a = (float)cr.NextDouble() * 6.28f;
                int steps = 6 + cr.Next(10);
                for (int s = 0; s < steps * 6; s++)
                {
                    if (s % 6 == 0) a += ((float)cr.NextDouble() - 0.5f) * 1.2f;
                    x += Mathf.Cos(a) * 0.04f; y += Mathf.Sin(a) * 0.04f; if (x * x + y * y > 12.5f * 12.5f) break;
                    int ix = (int)((x + H) / px), iy = (int)((y + H) / px), o = iy * N + ix; if (o < 0 || o >= N * N) continue;
                    var k = alb[o]; alb[o] = new Color32((byte)(k.r * 0.6f), (byte)(k.g * 0.6f), (byte)(k.b * 0.62f), 255); var hk = hgt[o]; hgt[o] = new Color32((byte)(hk.r * 0.7f), (byte)(hk.r * 0.7f), (byte)(hk.r * 0.7f), 255);
                }
            }
            // the decorative fissure: signed distance to the crack edge, then charred lip, gold body, ivory core
            var sd = new float[N * N]; for (int i = 0; i < sd.Length; i++) sd[i] = 99;
            int minX = N, minY = N, maxX = 0, maxY = 0; const float lip = 0.15f;
            foreach (var line in Fissure())
                for (int i = 0; i + 1 < line.Length; i++)
                {
                    var (a, wa) = line[i]; var (b, wb) = line[i + 1]; float pad = Mathf.Max(wa, wb) / 2 + lip + 0.02f;
                    int x0 = (int)((Mathf.Min(a.x, b.x) - pad + H) / px), x1 = (int)((Mathf.Max(a.x, b.x) + pad + H) / px), y0 = (int)((Mathf.Min(a.y, b.y) - pad + H) / px), y1 = (int)((Mathf.Max(a.y, b.y) + pad + H) / px);
                    minX = Mathf.Min(minX, x0); minY = Mathf.Min(minY, y0); maxX = Mathf.Max(maxX, x1); maxY = Mathf.Max(maxY, y1);
                    var ab = b - a; float l2 = Mathf.Max(1e-6f, ab.sqrMagnitude);
                    for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                        {
                            var p = new Vector2(-H + (x + 0.5f) * px, -H + (y + 0.5f) * px); float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
                            float d = (p - (a + ab * t)).magnitude - Mathf.Lerp(wa, wb, t) / 2; int o = y * N + x; if (d < sd[o]) sd[o] = d;
                        }
                }
            Color lipC = Hex(0x0A0816), body = Hex(0x8A5A22), core = Hex(0xD9A050); // dark albedo: the colour comes from emission, so the lavender key light cannot tint it pink
            for (int y = Mathf.Max(0, minY); y <= Mathf.Min(N - 1, maxY); y++) for (int x = Mathf.Max(0, minX); x <= Mathf.Min(N - 1, maxX); x++)
                {
                    int o = y * N + x; float d = sd[o]; if (d > lip) continue;
                    Color c = alb[o]; float e = 0, hh = hgt[o].r / 255f;
                    if (d > 0) { float t = Smooth(0, lip, d); c = Color.Lerp(lipC, c, t * t); hh *= Mathf.Lerp(0.3f, 1, t); }
                    else if (d > -0.06f) { float t = Mathf.Clamp01(-d / 0.06f); c = Color.Lerp(body, core, t * 0.35f); e = Mathf.Lerp(0.2f, 0.6f, t); hh = 0.05f; }
                    else { c = core; e = 1; hh = 0.02f; }
                    alb[o] = C32(c); byte hb = (byte)(hh * 255); hgt[o] = new Color32(hb, hb, hb, 255);
                    var ec = e >= 1 ? new Color(1, 0.66f, 0.3f) * 0.8f : new Color(gold.r * e, gold.g * e, gold.b * e); var old = emi[o];
                    emi[o] = C32(new Color(Mathf.Max(ec.r, old.r / 255f), Mathf.Max(ec.g, old.g / 255f), Mathf.Max(ec.b, old.b / 255f), 1));
                }
            return (WritePng("FloorAlbedo.png", alb, N, N), WritePng("FloorHeight.png", hgt, N, N), WritePng("FloorEmission.png", emi, N, N));
        }
        // (radius, half width, alpha, emissive share of the 2.0 HDR fissure intensity)
        static readonly (float, float, float, float)[] Rings = { (0.6f, 0.025f, 1, 0.14f), (2.6f, 0.03f, 1, 0.14f), (3.2f, 0.035f, 1, 0.14f),
            (5.0f, 0.016f, 0.7f, 0.078f), (7.5f, 0.016f, 0.7f, 0.078f), (10.2f, 0.018f, 0.8f, 0.078f), (12.6f, 0.025f, 0.9f, 0.078f), (13.42f, 0.025f, 0.9f, 0.078f) };

        static Texture2D PaintStation()
        {
            const int N = 1024; var px = new Color32[N * N]; Color bg = Hex(0x1A2142), brass = Hex(0xB88A48);
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2 - 1, v = (y + 0.5f) / N * 2 - 1, r = Mathf.Sqrt(u * u + v * v), a = Mathf.Atan2(v, u), pw = 2f / N, ink = 0;
                    void I(float d, float hw) { ink = Mathf.Max(ink, Mathf.Clamp01((hw - d) / pw + 0.5f)); }
                    foreach (var (rr, hw) in new[] { (0.95f, 0.025f), (0.78f, 0.012f), (0.55f, 0.016f), (0.3f, 0.01f) }) I(Mathf.Abs(r - rr), hw);
                    if (r > 0.8f && r < 0.92f) { float seg = Mathf.PI * 2 / 24, k = Mathf.Round(a / seg); if (r < (((int)k & 1) == 0 ? 0.92f : 0.86f)) I(Mathf.Abs(a - k * seg) * r, 0.008f); }
                    for (int i = 0; i < 8; i++) { float ra = i * Mathf.PI / 4; I(new Vector2(u - Mathf.Cos(ra) * 0.66f, v - Mathf.Sin(ra) * 0.66f).magnitude, 0.03f); }
                    if (r > 0.32f && r < 0.53f) { float seg = Mathf.PI * 2 / 12, k = Mathf.Round(a / seg); I(Mathf.Abs(Mathf.Sin(a - k * seg)) * r, 0.006f); }
                    var c = Color.Lerp(bg * (0.85f + 0.3f * Fbm(u * 6, v * 6)), brass, ink); c.a = 1; px[y * N + x] = C32(c);
                }
            return WritePng("StationDial.png", px, N, N);
        }

        static Texture2D PaintBanner()
        {
            const int W = 256, Hh = 640; var px = new Color32[W * Hh]; Color red = Hex(0x6E1A22), gold = Hex(0xC9A25A);
            for (int y = 0; y < Hh; y++) for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W, v = (y + 0.5f) / Hh; // v = 0 bottom
                    float tail = Mathf.Abs(u - 0.5f) * 0.22f; bool cut = v < 0.12f - tail * 0 && v < 0.12f && v < 0.02f + Mathf.Abs(u - 0.5f) * 0.4f;
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

        static Texture2D PaintBrick()
        {
            const int N = 512; var px = new Color32[N * N]; Color mortar = Hex(0x14131C);
            for (int y = 0; y < N; y++)
            {
                int row = y / 64; float off = (row & 1) * 64;
                for (int x = 0; x < N; x++)
                {
                    float bx = (x + off) % 128, by = y % 64; int col = (int)((x + off) / 128);
                    float e = Mathf.Min(Mathf.Min(bx, 128 - bx), Mathf.Min(by, 64 - by));
                    float t = Hash(col, row, 3), n = VNoise(x / 24f, y / 24f, 4);
                    var c = Color.Lerp(Hex(0x2B2A3A), Hex(0x3A384E), t * 0.7f + n * 0.3f);
                    if (e < 6) c *= Mathf.Lerp(0.7f, 1, e / 6f); if (by < 9 && e >= 3) c *= 1.12f;
                    if (e < 3) c = mortar; c.a = 1; px[y * N + x] = C32(c);
                }
            }
            return WritePng("SlateBrick.png", px, N, N);
        }

        // ---------------------------------------------------------------- build
        static readonly List<Transform> spinners = new List<Transform>(), bobbers = new List<Transform>(), swingers = new List<Transform>();
        static readonly List<float> spinSpeeds = new List<float>();
        static readonly List<GameObject> fadeTargets = new List<GameObject>();

        public static void Build()
        {
            rng = new System.Random(41); names.Clear(); spinners.Clear(); bobbers.Clear(); swingers.Clear(); spinSpeeds.Clear(); fadeTargets.Clear();
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

            foreach (var name in new[] { "Floor", "Rim", "RitualStations", "Entrances" })
                foreach (var t in arena.Find(name).GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);

            // occluder fade on the camera-side rim pieces
            var fades = new List<ShatteredHourOccluderFade>();
            Material[] op = { mSlate, mSlateBrick, mSlateCap, mBrass, mBanner }, fd = op.Select(FadedTwin).ToArray();
            foreach (var g in fadeTargets) { var f = g.AddComponent<ShatteredHourOccluderFade>(); f.opaque = op; f.faded = fd; fades.Add(f); }

            var lighting = new GameObject("Lighting").transform; BuildLighting(lighting);
            var cams = new GameObject("Cameras"); var camGo = new GameObject("Main Camera") { tag = "MainCamera" }; camGo.transform.SetParent(cams.transform, false);
            var cam = camGo.AddComponent<Camera>(); cam.nearClipPlane = 0.3f; cam.farClipPlane = 1200; cam.clearFlags = CameraClearFlags.Skybox; cam.allowHDR = true;
            var cd = cam.GetUniversalAdditionalCameraData(); cd.renderPostProcessing = true; cd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; cd.antialiasingQuality = AntialiasingQuality.High;
            var drv = cams.AddComponent<ShatteredHourArena>();
            drv.viewCamera = cam; drv.floorMaterial = mFloor; drv.floorEmission = floorEmission; drv.bellGlowMaterial = mBellGlow; drv.bellGlow = bellGlow;
            drv.bellLight = bellLight; drv.bellLightIntensity = bellLight.intensity; drv.shard = shard;
            drv.spinners = spinners.ToArray(); drv.spinSpeeds = spinSpeeds.ToArray(); drv.bobbers = bobbers.ToArray(); drv.swingers = swingers.ToArray(); drv.fades = fades.ToArray();
            drv.SetView(ShatteredHourArena.View.Gameplay);

            PrefabUtility.SaveAsPrefabAssetAndConnect(arena.gameObject, PrefabDir + "/LB_ENV_ShatteredHour_Arena.prefab", InteractionMode.AutomatedAction);
            PrefabUtility.SaveAsPrefabAssetAndConnect(bell.gameObject, PrefabDir + "/LB_ENV_FinalToll_BrokenBell.prefab", InteractionMode.AutomatedAction);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
        }

        static void MakeMaterials()
        {
            var (alb, hgt, emi) = PaintFloor();
            floorEmission = new Color(1f, 0.6f, 0.24f) * 2.4f;
            mFloor = new Material(lit) { name = "Floor" };
            mFloor.SetTexture("_BaseMap", alb); mFloor.SetColor("_BaseColor", Color.white); mFloor.SetFloat("_Smoothness", 0.28f); mFloor.SetFloat("_Metallic", 0.05f);
            mFloor.SetTexture("_BumpMap", hgt); mFloor.SetFloat("_BumpScale", 1); mFloor.EnableKeyword("_NORMALMAP");
            mFloor.SetTexture("_EmissionMap", emi); mFloor.SetColor("_EmissionColor", floorEmission); mFloor.EnableKeyword("_EMISSION");
            mFloor.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; Save(mFloor, "Floor.mat");

            var brick = PaintBrick();
            mSlate = Lit("SlateStone", Hex(0x262533), 0, 0.12f);
            mSlateBrick = Lit("SlateBrick", Color.white, 0, 0.15f, map: brick);
            mSlateCap = Lit("SlateCap", Hex(0x2B2A3A), 0, 0.18f);
            mDeck = Lit("BridgeDeck", Hex(0x3A3E52), 0, 0.2f);
            mBrass = Lit("AgedBrass", Hex(0xA8844A), 0.6f, 0.42f);
            mBrassPolished = Lit("PolishedBrass", Hex(0xC9A25A), 0.75f, 0.6f);
            mIron = Lit("Iron", Hex(0x2A2440), 0.35f, 0.3f);
            bellGlow = Hex(0xFFA850) * 1.5f;
            mBronze = Lit("BellBronze", Hex(0x5E4A30), 0.65f, 0.35f, dbl: true);
            mCrack = Lit("BellCrackGlow", Color.black, 0, 0.5f, new Color(1f, 0.6f, 0.22f) * 2.5f);
            mBellGlow = Lit("BellInteriorGlow", Hex(0x2A1A0A), 0, 0.3f, bellGlow, dbl: true);
            { var gp = new Color32[4 * 64]; for (int y = 0; y < 64; y++) { float f = Mathf.Pow(1 - y / 63f, 2.2f); for (int x = 0; x < 4; x++) gp[y * 4 + x] = C32(new Color(f, f, f, 1)); }
              mBellGlow.SetTexture("_EmissionMap", WritePng("BellGlowGradient.png", gp, 4, 64)); } // bright at the lip, falling off inside
            mFlame = Lit("LanternFlame", Color.black, 0, 0.5f, new Color(1f, 0.55f, 0.2f) * 2.8f);
            mRock = Lit("VoidRock", Hex(0x231E33), 0, 0.1f);
            mRockLight = Lit("VoidRockLight", Hex(0x2E2840), 0, 0.08f);
            mVoidStone = Lit("VoidStone", Hex(0x221F33), 0, 0.12f);
            mPedestal = Lit("Pedestal", Hex(0x23222F), 0, 0.25f); mWindow = Lit("DrumWindow", Hex(0x0E0A12), 0, 0.2f, new Color(1f, 0.5f, 0.18f) * 0.35f);
            mBanner = Lit("CrimsonBanner", new Color(1.35f, 1.15f, 1.15f), 0, 0.15f, dbl: true, map: PaintBanner());
            mBanner.SetFloat("_AlphaClip", 1); mBanner.SetFloat("_Cutoff", 0.5f); mBanner.EnableKeyword("_ALPHATEST_ON"); mBanner.renderQueue = (int)RenderQueue.AlphaTest;
            var st = PaintStation();
            mStation = Lit("StationDial", Color.white, 0.3f, 0.45f, new Color(1f, 0.72f, 0.4f) * 0.1f, map: st); mStation.SetTexture("_EmissionMap", st);
            mVortex = Save(new Material(Shader.Find("LastBell/Area4/ShatteredVortex")) { name = "Vortex" }, "Vortex.mat");
            mShaft = Save(new Material(Shader.Find("LastBell/Area4/ShatteredShaft")) { name = "LightShaft" }, "LightShaft.mat"); mShaft.SetFloat("_Alpha", 0.08f); mShaft.SetColor("_Color", new Color(1f, 0.85f, 0.6f));
            mCone = Save(new Material(Shader.Find("LastBell/Area4/ShatteredShaft")) { name = "BellCone" }, "BellCone.mat"); mCone.SetFloat("_Alpha", 0.025f); mCone.SetFloat("_Fade", 1);
            mSky = Save(new Material(Shader.Find("LastBell/Area4/ShatteredVoidSky")) { name = "VoidSky" }, "VoidSky.mat");
        }

        static void BuildFloor(Transform arena)
        {
            var g = Group("Floor", arena);
            var disc = Disc("FloorDisc", R, 128, FloorH);
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
        static bool InGap(float deg, float pad = 0)
        {
            foreach (var (c, h) in Gaps) if (Mathf.Abs(Mathf.DeltaAngle(deg, c)) < h + pad) return true;
            return false;
        }

        static void BuildRim(Transform arena)
        {
            var g = Group("Rim", arena).transform;
            Gaps.Clear(); Gaps.Add((0, GapN)); Gaps.Add((180, GapS)); Gaps.Add((90, GapEW)); Gaps.Add((270, GapEW));
            foreach (var d in new[] { 45f, 135f, 225f, 315f }) Gaps.Add((d, GapSt));
            // parapet runs between the gaps (plan degrees, clockwise)
            var edges = Gaps.SelectMany(x => new[] { (Mathf.Repeat(x.center - x.half, 360), false), (Mathf.Repeat(x.center + x.half, 360), true) }).OrderBy(e => e.Item1).ToList();
            var walls = Group("Parapet", g).transform;
            for (int i = 0; i < edges.Count; i++)
            {
                if (!edges[i].Item2) continue; float d0 = edges[i].Item1, d1 = edges[(i + 1) % edges.Count].Item1; if (d1 < d0) d1 += 360;
                float t0 = Th(d1), t1 = Th(d0); var seg = Group("Run_" + i, walls).transform; bool south = (d0 + d1) / 2 > 100 && (d0 + d1) / 2 < 260;
                MeshGO("Wall", ArcPrism("ParapetWall", R, RimOut, -0.45f, RimH, t0, t1, true, 1 / 2.4f), seg, Vector3.zero, mSlateBrick);
                MeshGO("Cap", ArcPrism("ParapetCap", R + 0.12f, RimOut + 0.1f, RimH, CapY, t0, t1, true, 1, 2), seg, Vector3.zero, mSlateCap);
                MeshGO("CapTrim", ArcPrism("CapTrim", R - 0.08f, R + 0.1f, RimH - 0.02f, CapY + 0.03f, t0, t1, true, 1, 2), seg, Vector3.zero, mBrass);
                MeshGO("Band", ArcPrism("InnerBand", R - 0.05f, R + 0.01f, 0.55f, 0.62f, t0, t1, true, 1, 2), seg, Vector3.zero, mBrass);
                if (south) fadeTargets.Add(seg.gameObject);
            }
            // drum below the rim: outer face, buttresses, dark arched recesses, brass band; then the rock spire
            var drum = Group("Drum", g).transform;
            MeshGO("DrumWall", ArcPrism("DrumWall", RimOut - 0.4f, RimOut, -4.4f, -0.45f, 0, 360, false, 1 / 2.4f, 2, false), drum, Vector3.zero, mSlateBrick);
            MeshGO("DrumBand", Torus("DrumBand", RimOut + 0.04f, 0.1f, 160, 6), drum, new Vector3(0, -0.5f, 0), mBrass);
            for (int i = 0; i < 24; i++)
            {
                float deg = i * 15; var q = Facing(deg);
                var b = Group("Buttress_" + i, drum, P(deg, RimOut + 0.3f, -2.4f), q).transform;
                Box(b, Vector3.zero, new Vector3(0.8f, 4.0f, 0.7f), mSlate);
                Box(b, new Vector3(0, -2.2f, 0.15f), new Vector3(0.6f, 0.6f, 0.6f), mSlate, new Vector3(45, 0, 0));
                var w = Group("Window_" + i, drum, P(deg + 7.5f, RimOut + 0.02f, -2.4f), Facing(deg + 7.5f)).transform;
                Box(w, new Vector3(0, -0.2f, 0), new Vector3(0.9f, 1.6f, 0.08f), i % 3 == 0 ? mWindow : mPedestal);
                PointedArch(w, 0.9f, 0.6f, 0.12f, 0.12f, mSlateCap, 4);
            }
            var spire = MeshGO("RockSpire", Spire("RockSpire", RimOut - 0.2f, 18, 14, 5, 0.18f), g, new Vector3(0, -4.4f, 0), mRock);
            // pinnacles (three heights), in pairs either side of each gap, plus four between
            var pin = new Dictionary<int, Mesh>(); foreach (int h in new[] { 25, 35, 45 }) pin[h] = PinnacleMesh(h / 10f);
            var spots = new List<(float deg, int h)>();
            foreach (var (c, half) in Gaps) { int h = c == 180 ? 25 : (c % 90 == 0 ? 35 : 45); spots.Add((c - half - 2.5f, h)); spots.Add((c + half + 2.5f, h)); }
            foreach (var d in new[] { 22.5f, 157.5f, 202.5f, 337.5f }) spots.Add((d, d > 90 && d < 270 ? 25 : 35));
            var pins = Group("Pinnacles", g).transform;
            foreach (var (deg, h) in spots)
            {
                var p = MeshGO("Pinnacle", pin[h], pins, P(deg, 14.62f, CapY), Facing(deg), mSlate, mBrass);
                float dd = Mathf.Repeat(deg, 360); if (dd > 100 && dd < 260) fadeTargets.Add(p);
            }
            // lanterns on the cap, outside each pinnacle pair; lights on every other one
            var lan = LanternMesh(); var lans = Group("Lanterns", g).transform; int li = 0;
            foreach (var (c, half) in Gaps)
                foreach (var s in new[] { -1, 1 })
                {
                    float deg = c + s * (half + 6.5f); var l = MeshGO("Lantern", lan, lans, P(deg, 14.75f, CapY), Facing(deg), mIron, mFlame);
                    if ((li++ & 1) == 0)
                    {
                        var lg = Group("Light", l.transform, new Vector3(0, 0.5f, 0)).AddComponent<Light>();
                        lg.type = LightType.Point; lg.color = Hex(0xFFB860); lg.range = 5; lg.intensity = 2.4f; lg.shadows = LightShadows.None;
                    }
                }
        }

        static Mesh PinnacleMesh(float h)
        {
            var root = new GameObject("tmp"); var t = root.transform; float shaft = h - 1.6f;
            Box(t, new Vector3(0, 0.175f, 0), new Vector3(0.85f, 0.35f, 0.85f), mSlate);
            Box(t, new Vector3(0, 0.35f + shaft / 2, 0), new Vector3(0.6f, shaft, 0.6f), mSlate);
            for (int i = 0; i < 4; i++) Box(t, new Vector3(0, 0.35f + shaft / 2, 0), new Vector3(0.68f, shaft * 0.7f, 0.18f), mSlate, new Vector3(0, i * 90 + 45, 0)); // buttress ribs
            Box(t, new Vector3(0, 0.35f + shaft, 0), new Vector3(0.7f, 0.1f, 0.7f), mBrass);
            float top = 0.4f + shaft; var sp = Pyramid("PinSpire", 0.36f, 1.8f); var mini = Pyramid("PinMini", 0.1f, 0.55f);
            MeshGO("Spire", sp, t, new Vector3(0, top, 0), mSlate);
            for (int i = 0; i < 4; i++) { float a = (i * 90 + 45) * Mathf.Deg2Rad; MeshGO("Mini", mini, t, new Vector3(Mathf.Cos(a) * 0.33f, top, Mathf.Sin(a) * 0.33f), mSlate); }
            for (int i = 0; i < 4; i++) for (int k = 1; k <= 2; k++)
                {
                    float a = (i * 90 + 45) * Mathf.Deg2Rad, f = k / 3f;
                    Box(t, new Vector3(Mathf.Cos(a) * 0.36f * (1 - f), top + 1.8f * f, Mathf.Sin(a) * 0.36f * (1 - f)), Vector3.one * 0.11f, mBrass, new Vector3(0, i * 90, 45));
                }
            Prim(PrimitiveType.Sphere, t, new Vector3(0, top + 1.86f, 0), Vector3.one * 0.24f, mBrassPolished);
            return Combine(root, new[] { mSlate, mBrass }, "Pinnacle_" + (int)(h * 10));
        }

        static Mesh LanternMesh()
        {
            var root = new GameObject("tmp"); var t = root.transform;
            Box(t, new Vector3(0, 0.22f, 0), new Vector3(0.07f, 0.44f, 0.07f), mIron);
            for (int i = 0; i < 4; i++) { float a = (i * 90 + 45) * Mathf.Deg2Rad; Box(t, new Vector3(Mathf.Cos(a) * 0.11f, 0.62f, Mathf.Sin(a) * 0.11f), new Vector3(0.035f, 0.34f, 0.035f), mIron); }
            Box(t, new Vector3(0, 0.45f, 0), new Vector3(0.26f, 0.03f, 0.26f), mIron);
            MeshGO("Roof", Pyramid("LanternRoof", 0.2f, 0.2f), t, new Vector3(0, 0.79f, 0), mIron);
            Box(t, new Vector3(0, 0.62f, 0), new Vector3(0.2f, 0.3f, 0.2f), mFlame);
            return Combine(root, new[] { mIron, mFlame }, "Lantern");
        }

        static void BuildStations(Transform arena)
        {
            var g = Group("RitualStations", arena).transform;
            var dial = Disc("StationDial", 1.9f, 96, 1.9f);
            var ringM = Torus("StationRing", 1.9f, 0.06f, 96, 8);
            var bulge = ArcPrism("StationBulge", 1.95f, 2.5f, -0.45f, RimH, -100, 100, true, 1 / 2.4f, 5);
            var bulgeCap = ArcPrism("StationBulgeCap", 1.9f, 2.6f, RimH, CapY, -100, 100, true, 1, 5);
            var gate = GateMesh();
            string[] labels = { "NE", "SE", "SW", "NW" };
            for (int i = 0; i < 4; i++)
            {
                float deg = 45 + i * 90; var s = Group("RitualStation_" + labels[i], g, P(deg, 13.2f), Facing(deg)).transform;
                MeshGO("Dial", dial, s, new Vector3(0, 0.02f, 0), Quaternion.Euler(0, 180, 0), mStation);
                MeshGO("Ring", ringM, s, new Vector3(0, 0.02f, 0), mBrass);
                Prim(PrimitiveType.Cylinder, s, new Vector3(0, -0.24f, 0), new Vector3(3.9f, 0.23f, 3.9f), mPedestal, default, "Plinth");
                var boss = Prim(PrimitiveType.Sphere, s, new Vector3(0, 0, 0), new Vector3(1f, 0.5f, 1f), mBrassPolished, default, "DomedBoss");
                MeshGO("Bulge", bulge, s, Vector3.zero, Quaternion.Euler(0, -90, 0), mSlateBrick);  // wraps the outward half (+Z local)
                MeshGO("BulgeCap", bulgeCap, s, Vector3.zero, Quaternion.Euler(0, -90, 0), mSlateCap);
                MeshGO("Gate", gate, s, new Vector3(0, CapY, 2.3f), Quaternion.identity, mSlate, mBrass);
                if (deg > 100 && deg < 260) fadeTargets.Add(s.gameObject);
            }
        }

        static Mesh GateMesh()
        {
            var root = new GameObject("tmp"); var t = root.transform;
            foreach (var x in new[] { -0.95f, 0.95f }) { Box(t, new Vector3(x, 0.9f, 0), new Vector3(0.24f, 1.8f, 0.24f), mSlate); MeshGO("Fin", Pyramid("GateFin", 0.14f, 0.5f), t, new Vector3(x, 1.8f, 0), mSlate); }
            PointedArch(t, 1.9f, 1.8f, 0.14f, 0.2f, mBrass, 5);
            Prim(PrimitiveType.Sphere, t, new Vector3(0, 1.8f + 1.65f + 0.1f, 0), Vector3.one * 0.18f, mBrass);
            return Combine(root, new[] { mSlate, mBrass }, "StationGate");
        }

        static void BuildEntrances(Transform arena)
        {
            var g = Group("Entrances", arena).transform;
            // South: grand stair down to a long causeway, flanked by gatehouses hung with crimson banners
            var s = Group("Entrance_South", g, Vector3.zero, Facing(180)).transform;
            for (int i = 0; i < 6; i++)
            {
                float top = -0.2f * (i + 1), z = RimOut - 0.3f + 0.5f * i;
                Box(s, new Vector3(0, (top - 3) / 2, z + 0.25f), new Vector3(4.0f, 3 + top, 0.5f), mDeck, default, "Step");
                Box(s, new Vector3(0, top + 0.01f, z + 0.04f), new Vector3(4.0f, 0.04f, 0.08f), mBrass, default, "Nosing");
            }
            float cz0 = RimOut + 2.7f, clen = 26;
            Box(s, new Vector3(0, -1.7f, cz0 + clen / 2), new Vector3(4.0f, 1.0f, clen), mDeck, default, "Causeway");
            Box(s, new Vector3(0, -1.19f, cz0 + clen / 2), new Vector3(0.08f, 0.02f, clen), mBrass, default, "CausewayInlay");
            for (int k = 1; k < 20; k++) Box(s, new Vector3(0, -1.195f, cz0 + k * 1.3f), new Vector3(3.9f, 0.02f, 0.05f), mPedestal, default, "Joint");
            foreach (var x in new[] { -2.25f, 2.25f })
            {
                Box(s, new Vector3(x, -0.75f, cz0 + clen / 2 - 1.5f), new Vector3(0.45f, 0.9f, clen + 3), mSlate, default, "CausewayWall");
                Box(s, new Vector3(x, -0.28f, cz0 + clen / 2 - 1.5f), new Vector3(0.14f, 0.05f, clen + 3), mBrass, default, "WallTrim");
                for (int k = 0; k < 6; k++) Box(s, new Vector3(x * 1.18f, -1.2f, cz0 + 2 + k * 4.2f), new Vector3(0.6f, 1.8f, 0.7f), mSlate, default, "Buttress");
                var gh = Group(x < 0 ? "Gatehouse_A" : "Gatehouse_B", s, new Vector3(x * 1.35f, 0, RimOut + 1.6f)).transform;
                Box(gh, new Vector3(0, -0.4f, 0), new Vector3(1.4f, 4.4f, 3.4f), mSlateBrick, default, "GatehouseWall");
                Box(gh, new Vector3(0, 1.88f, 0), new Vector3(1.6f, 0.12f, 3.6f), mBrass, default, "GatehouseTrim");
                MeshGO("Spire", Pyramid("GatehouseSpire", 0.5f, 3.0f), gh, new Vector3(0, 1.94f, -1.1f), mSlate);
                MeshGO("Spire", Pyramid("GatehouseSpire", 0.5f, 3.0f), gh, new Vector3(0, 1.94f, 1.1f), mSlate);
                Prim(PrimitiveType.Quad, gh, new Vector3(0, -0.2f, 1.73f), new Vector3(1.3f, 4.0f, 1), mBanner, new Vector3(0, 180, 0), "Banner");
                Box(gh, new Vector3(0, 1.78f, 1.76f), new Vector3(1.35f, 0.07f, 0.07f), mBrass, default, "BannerRod");
                fadeTargets.Add(gh.gameObject);
                // a second, taller pylon further out with its own banner, as on the plan
                var py = Group("Pylon", s, new Vector3(x * 1.55f, 0, cz0 + 5.5f)).transform;
                Box(py, new Vector3(0, -0.6f, 0), new Vector3(1.0f, 3.6f, 1.0f), mSlateBrick, default, "PylonWall");
                MeshGO("Spire", Pyramid("PylonSpire", 0.45f, 2.2f), py, new Vector3(0, 1.2f, 0), mSlate);
                Prim(PrimitiveType.Quad, py, new Vector3(0, -0.2f, 0.52f), new Vector3(0.95f, 3.2f, 1), mBanner, new Vector3(0, 180, 0), "Banner");
            }
            for (int k = 0; k < 3; k++) MeshGO("Support", Spire("CausewaySupport", 1.6f, 14, 6, 3, 0.15f), s, new Vector3(0, -2.2f, cz0 + 6 + k * 8), mRock);
            // North: stair up to a landing under the bell (six steps to 1.2 m so the tilted bell's far lip clears it)
            var n = Group("Stair_North", g, Vector3.zero, Facing(0)).transform;
            for (int i = 0; i < 6; i++)
            {
                float top = 0.2f * (i + 1), z = RimOut - 0.3f + 0.3f * i;
                Box(n, new Vector3(0, (top - 2) / 2, z + 0.15f), new Vector3(3.0f, 2 + top, 0.3f), mDeck, default, "Step");
                Box(n, new Vector3(0, top + 0.01f, z + 0.04f), new Vector3(3.0f, 0.04f, 0.08f), mBrass, default, "Nosing");
            }
            float lz = RimOut - 0.3f + 1.8f + 1.5f;
            Box(n, new Vector3(0, 1.2f - 1.5f, lz), new Vector3(4, 3, 3), mPedestal, default, "Landing");
            Box(n, new Vector3(0, 1.2f, lz), new Vector3(4.1f, 0.06f, 3.1f), mBrass, default, "LandingTrim");
            Box(n, new Vector3(0, 1.23f, lz), new Vector3(3.6f, 0.04f, 2.6f), mDeck, default, "LandingTop");
            foreach (var x in new[] { -1.65f, 1.65f }) Box(n, new Vector3(x, 0.7f, RimOut + 0.75f), new Vector3(0.3f, 1.3f, 2.0f), mSlate, new Vector3(-33, 0, 0), "StairRail");
            MeshGO("Support", Spire("LandingSupport", 2.6f, 12, 6, 3, 0.15f), n, new Vector3(0, -1.8f, lz), mRock);
            // East / West: bridges ending at ruined pointed-arch gateways, the far end broken off into the void
            foreach (var (deg, label) in new[] { (90f, "Bridge_East"), (270f, "Bridge_West") })
            {
                var b = Group(label, g, Vector3.zero, Facing(deg)).transform; float bz = RimOut - 0.3f + 3.5f;
                Box(b, new Vector3(0, -0.32f, bz), new Vector3(3.5f, 0.6f, 7), mDeck, default, "Deck");
                Box(b, new Vector3(0, -0.015f, bz), new Vector3(0.05f, 0.02f, 7), mPedestal, default, "Joint"); for (int k = 0; k < 6; k++) foreach (var jx in new[] { -0.8f, 0.8f }) Box(b, new Vector3(jx, -0.015f, bz - 3.5f + k * 1.2f + (jx > 0 ? 0.6f : 0) + 0.6f), new Vector3(1.55f, 0.02f, 0.05f), mPedestal, default, "Joint");
                foreach (var x in new[] { -1.9f, 1.9f })
                {
                    Box(b, new Vector3(x, 0.15f, bz), new Vector3(0.3f, 0.6f, 7), mSlate, default, "SideWall");
                    Box(b, new Vector3(x, 0.47f, bz), new Vector3(0.36f, 0.05f, 7), mBrass, default, "WallTrim");
                    for (int k = 0; k < 4; k++) Box(b, new Vector3(x, 0.45f, bz - 3 + k * 2), new Vector3(0.45f, 0.9f, 0.45f), mSlate, default, "Post");
                }
                // broken far edge: ragged deck chunks and falling rubble
                for (int k = 0; k < 4; k++) Box(b, new Vector3(-1.2f + k * 0.8f, -0.32f - Rn() * 0.4f, bz + 3.5f + 0.3f + Rn() * 0.9f), new Vector3(0.75f, 0.55f, Rr(0.5f, 1.4f)), mDeck, new Vector3(Rr(-12, 12), Rr(-10, 10), Rr(-12, 12)), "BrokenDeck");
                for (int k = 0; k < 6; k++) Box(b, new Vector3(Rr(-1.6f, 1.6f), Rr(-4f, -1f), bz + 4.5f + Rn() * 2.5f), Vector3.one * Rr(0.3f, 0.8f), mDeck, new Vector3(Rn() * 90, Rn() * 90, Rn() * 90), "FallingRubble");
                MeshGO("Support", Spire("BridgeSupport", 1.4f, 10, 6, 3, 0.2f), b, new Vector3(0, -0.6f, bz), mRock);
                var gate = Group("RuinedGate", b, new Vector3(0, 0, bz + 2.6f)).transform;
                foreach (var x in new[] { -1.95f, 1.95f }) Box(gate, new Vector3(x, x < 0 ? 2.25f : 1.6f, 0), new Vector3(0.7f, x < 0 ? 4.5f : 3.2f, 0.7f), mSlate, default, "Column");
                var arch = Group("Arch", gate, new Vector3(0, 0, 0)).transform; PointedArch(arch, 3.9f, 4.5f, 0.45f, 0.6f, mSlate, 6, 0.55f);
                for (int k = 0; k < 4; k++) Prim(PrimitiveType.Cube, gate, new Vector3(Rr(0.6f, 1.8f), Rr(0.1f, 0.3f), Rr(-0.6f, 0.6f)), Vector3.one * Rr(0.3f, 0.6f), mSlate, new Vector3(Rn() * 90, Rn() * 90, Rn() * 90), "Rubble");
            }
        }

        // A solid flat clock band (no rungs): brass annulus with a rolled outer edge, a thin inner bead, twelve raised
        // numeral plates on the band face and optional spokes. Built around local Y; Unity polar angles start..start+arc.
        static Transform ClockRing(Transform parent, string name, float rad, float band, float arc = 360, float start = 0, int spokes = 0)
        {
            var g = Group(name, parent).transform; float t0 = start, t1 = start + arc;
            MeshGO("Band", ArcPrism(name + "_Band", rad - band, rad, -0.12f, 0.12f, t0, t1, arc < 360, 1, 2), g, Vector3.zero, mBrass);
            MeshGO("Edge", Torus(name + "_Edge", rad, 0.16f, 160, 8, arc, start), g, Vector3.zero, mBrassPolished);
            MeshGO("Bead", Torus(name + "_Bead", rad - band - 0.25f, 0.07f, 160, 6, arc, start), g, Vector3.zero, mBrass);
            for (int i = 0; i < 12; i++)
            {
                float a = start + 15 + i * 30; if (a > t1 - 5) break; var d = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), 0, Mathf.Sin(a * Mathf.Deg2Rad));
                Box(g, d * (rad - band / 2), new Vector3(band * (i % 3 == 0 ? 0.9f : 0.5f), 0.32f, band * 0.75f), mIron, new Vector3(0, -a + 90, 0), "NumeralPlate");
                if (i % 3 == 0) Box(g, d * (rad - band / 2) + Vector3.up * 0.17f, new Vector3(band * 0.18f, 0.04f, band * 0.6f), mBrassPolished, new Vector3(0, -a + 90, 0), "Numeral");
            }
            for (int i = 0; i < spokes; i++)
            {
                float a = start + (i + 0.5f) * arc / spokes; var d = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), 0, Mathf.Sin(a * Mathf.Deg2Rad));
                Box(g, d * (rad - band) / 2, new Vector3(0.22f, 0.22f, rad - band), mBrass, new Vector3(0, -a + 90, 0), "Spoke");
            }
            return g;
        }

        static void BuildClockRings(Transform g)
        {
            void Spin(Transform t, float s) { spinners.Add(t); spinSpeeds.Add(s); }
            var right = Group("RightPivot", g, P(70, 36, -3), Quaternion.Euler(72, -25, -10)).transform; Spin(ClockRing(right, "Right", 10, 0.7f, 290, 200, 6), -1.6f);
            var low = Group("LowPivot", g, P(300, 34, -14), Quaternion.Euler(35, 40, 0)).transform; Spin(ClockRing(low, "Low", 8, 0.6f, 300, 30, 4), 2.2f);
        }

        static void BuildVoid(Transform arena)
        {
            var g = Group("Void", arena).transform;
            var vortex = MeshGO("Vortex", Disc("VortexDisc", 160, 96, 160), g, new Vector3(0, -75, 0), mVortex);
            vortex.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            bool CamSide(Vector3 p) => p.z > 12 && Mathf.Abs(p.x) < 22 && p.y > -14;
            // floating gothic arcades: three variants, tilted, plus big ones at the plan's corners
            var arcades = new[] { ArcadeMesh(2, 0), ArcadeMesh(3, 1), ArcadeMesh(4, 2) };
            var ruins = Group("Ruins", g).transform;
            var places = new List<(float deg, float r, float y, int v, float s)> { (318, 40, -5, 2, 1.25f), (42, 41, -6, 2, 1.25f), (88, 33, -2, 1, 1.15f), (272, 33, -4, 1, 1.1f) };
            for (int i = 0; i < 12; i++) places.Add((Rr(0, 360), Rr(24, 42), Rr(-20, 10), rng.Next(3), Rr(0.7f, 1.1f)));
            foreach (var (deg, r, y, v, s) in places)
            {
                var p = P(deg, r, y); if (CamSide(p)) continue;
                var a = MeshGO("Arcade", arcades[v], ruins, p, Quaternion.LookRotation(-Out(deg)) * Quaternion.Euler(Rr(-25, 25), Rr(-20, 20), Rr(-12, 12)), mVoidStone, mBrass);
                a.transform.localScale = Vector3.one * s; bobbers.Add(a.transform);
            }
            // inverted spire chunks
            var chunkMeshes = Enumerable.Range(0, 4).Select(i => Spire("FloatingSpire", Rr(1.2f, 2.6f), Rr(4, 10), 5 + i, 3, 0.25f)).ToArray();
            var spires = Group("FloatingSpires", g).transform;
            for (int i = 0; i < 30; i++)
            {
                float deg = Rr(0, 360), r = Rr(18, 55); var p = P(deg, r, Rr(-30, 8)); if (CamSide(p)) continue;
                var c = MeshGO("Spire", chunkMeshes[i % 4], spires, p, Quaternion.Euler(Rr(-12, 12), Rn() * 360, Rr(-12, 12)), mRock); bobbers.Add(c.transform);
                if (i % 2 == 0) Box(c.transform, new Vector3(Rr(-0.5f, 0.5f), 0.4f, Rr(-0.5f, 0.5f)), new Vector3(Rr(0.8f, 1.6f), Rr(0.6f, 1.4f), Rr(0.8f, 1.6f)), mVoidStone, new Vector3(0, Rn() * 90, 0), "Block");
            }
            // debris
            var rocks = Enumerable.Range(0, 8).Select(i => Rock("Rock", 0.25f)).ToArray(); var deb = Group("Debris", g).transform;
            for (int i = 0; i < 200; i++)
            {
                float deg = Rr(0, 360), r = 17 + Mathf.Pow(Rn(), 0.8f) * 53; var p = P(deg, r, Rr(-45, 15)); if (CamSide(p) && p.y > -6) continue;
                float s = 0.25f + Mathf.Pow(Rn(), 3) * 2.4f;
                var k = MeshGO("Rock", rocks[i % 8], deb, p, Quaternion.Euler(Rn() * 360, Rn() * 360, Rn() * 360), Rn() < 0.3f ? mRockLight : mRock);
                k.transform.localScale = Vector3.one * s; if (s < 1) k.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off; bobbers.Add(k.transform);
            }
            // diagonal chains crossing the frame corners
            var link = Torus("ChainLink", 0.26f, 0.075f, 20, 8, 360, 0, new Vector3(1, 1, 1.55f));
            var chains = Group("Chains", g).transform;
            Chain(chains, "Chain_TopLeft", new Vector3(42, 4, -4), new Vector3(12, 1, -38), 8, 1.2f, link);
            Chain(chains, "Chain_TopRight", new Vector3(-42, 4, -4), new Vector3(-12, 1, -38), 8, 1.2f, link);
            Chain(chains, "Chain_BottomLeft", new Vector3(30, -2, 6), new Vector3(16, -9, 30), 5, 1.2f, link);
            Chain(chains, "Chain_BottomRight", new Vector3(-30, -2, 6), new Vector3(-16, -9, 30), 5, 1.2f, link);
            // hanging bells on chains to the sides and back
            var small = Lathe("HangingBell", BellProfile(0.32f), 32, 0, Mathf.PI * 2); var hb = Group("HangingBells", g).transform;
            foreach (var (deg, r, y) in new[] { (298f, 32f, -3f), (322f, 37f, 0f), (38f, 36f, -1f), (64f, 31f, -4f), (252f, 27f, -1f), (108f, 27f, 0f), (348f, 22f, 7f) })
            {
                var pivot = Group("HangingBell", hb, P(deg, r, y + 30)).transform; swingers.Add(pivot);
                Prim(PrimitiveType.Cylinder, pivot, new Vector3(0, -15, 0), new Vector3(0.12f, 15, 0.12f), mIron, default, "Rope");
                float sc = Rr(0.6f, 1.0f); var b = MeshGO("Bell", small, pivot, new Vector3(0, -30 - 2.7f * sc, 0), mBronze); b.transform.localScale = Vector3.one * sc;
                MeshGO("Band", Torus("HangingBellBand", 2.2f, 0.07f, 32, 6), b.transform, new Vector3(0, 0.05f, 0), mBrass);
            }
            // faint warm light shafts behind the arena
            var shafts = Group("LightShafts", g).transform;
            foreach (var (deg, r, w) in new[] { (346f, 46f, 3f), (12f, 48f, 2.4f), (0f, 52f, 3.4f) })
            {
                var p = P(deg, r, -10); var toCam = new Vector3(0, 0, 22.5f) - p; toCam.y = 0;
                var q = Prim(PrimitiveType.Quad, shafts, p, new Vector3(w, 90, 1), mShaft, default, "Shaft"); q.transform.rotation = Quaternion.LookRotation(-toCam);
                q.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        static Mesh ArcadeMesh(int arches, int variant)
        {
            var root = new GameObject("tmp"); var t = root.transform; float span = 3.2f, colH = 6.5f, w = arches * span;
            for (int i = 0; i <= arches; i++)
            {
                float x = -w / 2 + i * span; bool broken = variant > 0 && i == arches && variant != 1;
                float h = broken ? colH * 0.55f : colH;
                Box(t, new Vector3(x, h / 2, 0), new Vector3(0.8f, h, 0.9f), mVoidStone, default, "Column");
                Box(t, new Vector3(x, 0.2f, 0), new Vector3(1.1f, 0.4f, 1.1f), mBrass, default, "Base");
                if (!broken) Box(t, new Vector3(x, colH + 0.1f, 0), new Vector3(1.0f, 0.25f, 1.0f), mBrass, default, "Capital");
                if (i < arches && !(broken || (variant > 0 && i == arches - 1 && variant == 2)))
                { var a = Group("Arch", t, new Vector3(x + span / 2, 0, 0)).transform; PointedArch(a, span - 0.8f, colH + 0.2f, 0.4f, 0.8f, mVoidStone, 5); }
            }
            Box(t, new Vector3(variant == 2 ? -span * 0.6f : 0, colH + 2.6f, 0), new Vector3(variant == 2 ? w * 0.6f : w + 0.8f, 0.7f, 1.0f), mVoidStone, default, "Entablature");
            Box(t, new Vector3(0, -0.6f, 0), new Vector3(w + 1.2f, 1.2f, 1.6f), mVoidStone, default, "Foundation");
            MeshGO("Under", Spire("ArcadeUnder", 1.2f, 4, 5, 2, 0.2f), t, new Vector3(0, -1.2f, 0), mVoidStone);
            return Combine(root, new[] { mVoidStone, mBrass }, "Arcade_" + arches);
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
        static float BellRadiusAt(Vector2[] prof, float y) // outer surface (first 11 points run top → lip)
        {
            for (int i = 0; i < 10; i++) if (y <= prof[i].y && y >= prof[i + 1].y) return Mathf.Lerp(prof[i + 1].x, prof[i].x, (y - prof[i + 1].y) / Mathf.Max(1e-4f, prof[i].y - prof[i + 1].y));
            return prof[10].x;
        }

        static (Light, Transform) BuildBell(Transform root)
        {
            const float k = 5.5f / 6.9f, gap = 30 * Mathf.Deg2Rad;  // mouth radius 5.5 m, height ≈ 8.3 m
            var prof = BellProfile(k);
            var bell = Group("Bell", root, new Vector3(0, 3.4f, -16.5f), Quaternion.Euler(-32, 0, 0)).transform; // mouth tilted toward the arena
            MeshGO("Body", Lathe("BellBody", prof, 72, gap / 2, Mathf.PI * 2 - gap), bell, Vector3.zero, mBronze);
            var inner = prof.Skip(11).Take(5).Select(p => new Vector2(p.x * 0.96f, p.y + 0.02f)).ToArray(); // glow only around the mouth; higher inside stays dark bronze
            MeshGO("InteriorGlow", Lathe("BellInterior", inner, 48, 0, Mathf.PI * 2), bell, Vector3.zero, mBellGlow).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            // gold bands with clock-numeral studs, each broken at the gap (gap centred on +Z = Unity polar 90°)
            foreach (var y in new[] { 0.12f, 2.72f, 6.08f })
            {
                float r = BellRadiusAt(prof, y) + 0.04f;
                MeshGO("Band", Torus("BellBand", r, 0.12f, 96, 8, 360 - 30, 90 + 15), bell, new Vector3(0, y, 0), mBrass);
                for (int i = 0; i < 12; i++)
                {
                    float a = 90 + 15 + 12 + i * (330f - 24) / 11; var d = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), 0, Mathf.Sin(a * Mathf.Deg2Rad));
                    Box(bell, d * (r + 0.08f) + Vector3.up * y, new Vector3(0.12f, 0.36f, 0.08f), mBrassPolished, new Vector3(0, -a + 90, 0), "Numeral");
                }
            }
            // glowing hairline cracks running up from the gap edges
            for (int c = 0; c < 6; c++)
            {
                float ph = (c % 2 == 0 ? 1 : -1) * (gap / 2 + Rr(0.02f, 0.5f)), y = Rr(0.15f, 1.0f); Vector3 last = Vector3.zero;
                for (int s = 0; s < 8; s++)
                {
                    float ry = BellRadiusAt(prof, y) + 0.03f; var p = new Vector3(Mathf.Sin(ph) * ry, y, Mathf.Cos(ph) * ry);
                    if (s > 0) { var d = p - last; Box(bell, (p + last) / 2, new Vector3(0.035f, 0.035f, d.magnitude + 0.02f), mCrack, Quaternion.LookRotation(d).eulerAngles, "Crack"); }
                    last = p; y += Rr(0.5f, 0.9f); ph += Rr(-0.12f, 0.12f); if (y > 7.6f) break;
                }
            }
            // broken shard drifting 2 m off the gap
            var shard = MeshGO("Shard", Lathe("BellShard", prof.Take(11).Concat(prof.Skip(11)).Skip(4).Take(13).ToArray(), 8, -0.25f, 0.5f), bell, new Vector3(-6.2f, 2.2f, 2.6f), Quaternion.Euler(25, 35, -20), mBronze);
            shard.transform.localScale = Vector3.one * 0.75f;
            // crown loop and two huge chains rising out of frame
            float topY = prof[0].y;
            MeshGO("Crown", Torus("BellCrown", 0.75f, 0.24f, 32, 10), bell, new Vector3(0, topY + 0.7f, 0), Quaternion.Euler(90, 0, 0), mBronze);
            var link = Torus("BellChainLink", 0.26f, 0.075f, 20, 8, 360, 0, new Vector3(1, 1, 1.55f));
            foreach (var x in new[] { -0.6f, 0.6f })
            {
                var top = bell.TransformPoint(new Vector3(x * 3, topY + 40, 0)); var bot = bell.TransformPoint(new Vector3(x, topY + 1.2f, 0));
                Chain(root, x < 0 ? "BellChain_L" : "BellChain_R", bot, top, 0.01f, 0.8f, link);
            }
            // interior light and faint cone below the mouth
            var lg = Group("InteriorLight", bell, new Vector3(0, 2.2f, 0)).AddComponent<Light>();
            lg.type = LightType.Point; lg.color = Hex(0xFFB060); lg.range = 24; lg.intensity = 10; lg.shadows = LightShadows.None;
            var cone = MeshGO("LightCone", ConeShell(5.2f, 7.5f, 6), bell, new Vector3(0, -6, 0), mCone); cone.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            // astrolabe behind the bell
            var astro = Group("AstrolabePivot", root, new Vector3(0, 2, -30), Quaternion.Euler(90, 0, 0)).transform;
            var ring = ClockRing(astro, "Astrolabe", 14, 1.4f, 290, 110, 0);
            MeshGO("NumeralRing", Torus("AstroNumeralRing", 10.2f, 0.18f, 160, 6), ring, Vector3.zero, mBrass);
            for (int i = 0; i < 8; i++) Box(ring, new Vector3(Mathf.Cos(i * Mathf.PI / 4), 0, Mathf.Sin(i * Mathf.PI / 4)) * 5.8f, new Vector3(0.3f, 0.3f, 8.8f), mBrass, new Vector3(0, -i * 45 + 90, 0), "Spoke");
            Prim(PrimitiveType.Cylinder, ring, Vector3.zero, new Vector3(3, 0.3f, 3), mBrassPolished, default, "Hub");
            spinners.Add(ring); spinSpeeds.Add(1.5f);
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
            return SaveMesh(m);
        }

        static void BuildLighting(Transform g)
        {
            var sun = Group("MoonLight", g).AddComponent<Light>(); sun.type = LightType.Directional; sun.color = Hex(0xC8B8FF); sun.intensity = 1.1f;
            sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.85f;
            var from = (Out(315) * Mathf.Cos(55 * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(55 * Mathf.Deg2Rad)).normalized;
            sun.transform.rotation = Quaternion.LookRotation(-from);
            RenderSettings.sun = sun;
            RenderSettings.skybox = mSky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex(0x6E4FC2, 0.7f); RenderSettings.ambientEquatorColor = Hex(0x2B2358, 0.8f); RenderSettings.ambientGroundColor = Hex(0x0E0A1E);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = 0.016f; RenderSettings.fogColor = Hex(0x2A1B55);
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
            var bloom = profile.Add<Bloom>(true); bloom.threshold.Override(1f); bloom.intensity.Override(0.6f); bloom.scatter.Override(0.6f);
            var col = profile.Add<ColorAdjustments>(true); col.postExposure.Override(0.1f); col.contrast.Override(10f); col.saturation.Override(5f);
            var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.25f); vig.color.Override(Hex(0x0A061C));
            profile.Add<WhiteBalance>(true).temperature.Override(-3f); // -8 turned the gold inlay pink
            foreach (var c in profile.components) { c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy; AssetDatabase.AddObjectToAsset(c, profile); }
            AssetDatabase.SaveAssets(); return profile;
        }

        // ---------------------------------------------------------------- batch: build, then the five review screenshots
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

        public static void CaptureAll()
        {
            string shots = Path.GetFullPath(ShotDir); Directory.CreateDirectory(shots);
            var drv = Object.FindFirstObjectByType<ShatteredHourArena>(); var probe = Object.FindFirstObjectByType<ReflectionProbe>();
            if (probe) probe.RenderProbe();
            drv.SetView(ShatteredHourArena.View.Gameplay); Capture(Path.Combine(shots, "gameplay.png")); Capture(Path.Combine(shots, "gameplay.png"));
            drv.SetView(ShatteredHourArena.View.Establishing); Capture(Path.Combine(shots, "establishing.png")); Capture(Path.Combine(shots, "establishing.png"));
            drv.SetView(ShatteredHourArena.View.TopDown); if (probe) probe.RenderProbe(); Capture(Path.Combine(shots, "topdown.png")); Capture(Path.Combine(shots, "topdown.png"));
            { var ct = Camera.main.transform; Camera.main.orthographic = false; Camera.main.fieldOfView = 50; ct.position = new Vector3(0, 9, 40); ct.LookAt(new Vector3(0, 0, 13)); Capture(Path.Combine(shots, "south-entrance.png")); }
            drv.SetView(ShatteredHourArena.View.Gameplay);
            // telegraph readability test: a coral 4 m circle at ~60 % opacity
            var tm = new Material(Shader.Find("Universal Render Pipeline/Unlit")); tm.SetColor("_BaseColor", new Color(1f, 0.42f, 0.35f, 0.6f)); MakeTransparent(tm, true);
            var disc = new Mesh(); { var v = new List<Vector3> { Vector3.zero }; var t = new List<int>(); for (int i = 0; i <= 64; i++) { float a = i / 64f * Mathf.PI * 2; v.Add(new Vector3(Mathf.Cos(a) * 2, 0, Mathf.Sin(a) * 2)); if (i < 64) { t.Add(0); t.Add(i + 2); t.Add(i + 1); } } disc.SetVertices(v); disc.SetTriangles(t, 0); disc.RecalculateNormals(); }
            var tele = new GameObject("TelegraphTest"); tele.transform.position = new Vector3(5, 0.02f, 3); tele.AddComponent<MeshFilter>().sharedMesh = disc; tele.AddComponent<MeshRenderer>().sharedMaterial = tm;
            Capture(Path.Combine(shots, "telegraph-test.png")); Object.DestroyImmediate(tele);
            // eight players at r 11
            var pm = new Material(lit); pm.SetColor("_BaseColor", Hex(0x3C6BE0)); var caps = new List<GameObject>();
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2 + 0.2f; var c = Prim(PrimitiveType.Capsule, null, new Vector3(Mathf.Cos(a) * 11, 0.9f, Mathf.Sin(a) * 11), new Vector3(0.6f, 0.9f, 0.6f), pm, default, "TestPlayer");
                caps.Add(c); ShatteredHourOccluderFade.Players.Add(c.transform);
            }
            var cam = Camera.main; foreach (var f in Object.FindObjectsByType<ShatteredHourOccluderFade>(FindObjectsSortMode.None)) f.Tick(cam, 10);
            Capture(Path.Combine(shots, "eight-players.png"));
            ShatteredHourOccluderFade.Players.Clear(); foreach (var f in Object.FindObjectsByType<ShatteredHourOccluderFade>(FindObjectsSortMode.None)) f.Tick(cam, 10);
            foreach (var c in caps) Object.DestroyImmediate(c);
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

    // Import settings for the generated textures: the floor height map becomes a normal map; big maps keep their size.
    sealed class ShatteredHourTextureImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/LastBell/Areas/Area4/Generated/")) return;
            var t = (TextureImporter)assetImporter; t.maxTextureSize = 2048; t.mipmapEnabled = true; t.anisoLevel = 8;
            if (assetPath.EndsWith("FloorAlbedo.png")) t.maxTextureSize = 4096;
            if (assetPath.EndsWith("FloorHeight.png")) { t.textureType = TextureImporterType.NormalMap; t.convertToNormalmap = true; t.heightmapScale = 0.02f; t.normalmapFilter = TextureImporterNormalFilter.Sobel; }
            if (assetPath.EndsWith("Banner.png")) t.alphaIsTransparency = true;
            if (assetPath.EndsWith("SlateBrick.png")) t.wrapMode = TextureWrapMode.Repeat;
        }
    }
}
