using System.Collections.Generic;
using LastBell.Run;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LastBell.Arenas.MetronomeCourt
{
    // The Metronome Court (collab/docs/bosses/tempo-tyrant.md): a circular raised stone platform, 25 m playable diameter
    // (RunMaps bound 12.5 m), uninterrupted flat combat floor (paved wedges, thin brass radial inlays, a dark clock-dial
    // border with brass hour plaques), a south entry bridge, perimeter parapet/posts/lanterns outside the movement bound,
    // tapered gothic towers in the fog, and an independent giant mechanical metronome (~10 m frame) on its own plinth
    // beyond the north edge. Everything is built from modular code parts at load with shared cached materials, so the
    // scene file stays small and nothing accumulates between attempts.
    //
    // Layout convention shared with every run map: centre origin, floor at y = 0, entry south at +z (camera side),
    // north = -z. The metronome is presentation only: its rod, escapement, pawl, light, lantern chase and cracks are derived
    // every frame from the active Tempo Tyrant's TempoClock (or a non-damaging Haste preview when no fight is running).
    // One physical rod. Because the fixed gameplay camera cannot frame anything north of the floor edge, the metronome
    // reaches into the frame through (a) a tempo-tinted spot light behind its apex that washes the court in the tempo
    // colour (no rod shadow on the floor: lighting alone signals Haste/Normal) and (b) rim lanterns whose glow follows the rod's lean, flashing together on transition ticks.
    public sealed partial class MetronomeCourtArena : MonoBehaviour
    {
        public bool quiet;   // HazardSystem dims decoration while hazards are up (shared arena convention)

        public const float TempoLineRadius = 12.1f;
        public const float FloorRadius = 13.1f, DialInner = 12.4f, ParapetRadius = 13.5f, PostRadius = 14.4f, MetronomeZ = -19.5f, FrameHeight = 10f;
        static readonly Color Stone = new Color(0.52f, 0.53f, 0.55f), StoneWarm = new Color(0.56f, 0.54f, 0.51f), Grout = new Color(0.34f, 0.33f, 0.32f), StoneDark = new Color(0.30f, 0.30f, 0.35f),   // pass 3: toward the brief's blue-violet recess, so its courses read
            // October 27 (art pass): inlays at about half saturation; charcoal iron lifted to the brief's readable navy (#252C3D)
            Ivory = new Color(0.64f, 0.615f, 0.565f), Brass = new Color(0.78f, 0.58f, 0.28f), BrassInlay = new Color(0.44f, 0.40f, 0.33f),
            Iron = new Color(0.145f, 0.17f, 0.24f), DialBand = new Color(0.19f, 0.19f, 0.21f), Teal = new Color(0.08f, 0.32f, 0.33f),
            Abyss = new Color(0.05f, 0.07f, 0.10f), BannerTeal = new Color(0.06f, 0.26f, 0.28f), FloorStone = new Color(0.45f, 0.46f, 0.48f), FloorGrout = new Color(0.38f, 0.39f, 0.43f), CrackGlow = new Color(1.4f, 0.25f, 0.1f);
        public static readonly Color HasteTint = new Color(1f, 0.58f, 0.22f), NormalTint = new Color(0.28f, 0.92f, 0.82f);
        // lights use a paler gold in Haste so the wash never muddies the red-orange warnings; emissives keep HasteTint
        public static readonly Color HasteLightTint = new Color(1f, 0.78f, 0.42f);
        // and a cool blue in Normal: the teal emissive tint times brass albedo came out olive/lime
        // pass 4 (critic: "the Normal tempo still turns the whole scene blue"): a paler, less saturated cool, so the stone keeps
        // its identity; the tempo itself lives on the dashed ring, the lanterns, the metronome seams and the HUD
        public static readonly Color NormalLightTint = new Color(0.80f, 0.85f, 0.93f);
        // review-capture switch (editor art captures only): forces the preview tempo; null in the game
        public static bool? ReviewHaste;
        public const float FogStart = 47f, FogEnd = 100f;   // the far floor edge is ~46 m from the gameplay camera: fog never touches the court

        public Transform Rod { get; private set; }
        public Transform Weight { get; private set; }
        public Transform Escapement { get; private set; }
        public Transform Pawl { get; private set; }
        public Transform Frame { get; private set; }
        public Light TempoSpotLight { get; private set; }
        public Light TempoRimLight { get; private set; }   // shadowless, from the north: edge-lights everyone in the tempo colour
        public float BeatPulse { get; private set; }        // 0..1, peaks 0.03 s after each beat and decays over ~0.12 s
        public Color LightTint { get; private set; }
        public bool SkyActive => skyMat && RenderSettings.skybox == skyMat;
        public int LanternPoolCount => pools.Count;
        public float RodAngle { get; private set; }
        public bool ShowingCracks { get; private set; }
        public bool Preview { get; private set; }
        public int LanternCount => lanterns.Count;
        public float LanternPulseX { get; private set; }
        public const float SwingDegrees = 26f, RodLength = 10.5f;
        public const float WeightHaste = 4.4f, WeightNormal = 7.4f;   // the weight IS the tempo: low = fast, high = slow
        public float WeightHeight { get; private set; } = WeightHaste;
        readonly List<Renderer> chips = new List<Renderer>();   // the tip overshoots the apex by ~1.5 m, as in the reference

        readonly List<(Renderer r, float x)> lanterns = new List<(Renderer, float)>(), pools = new List<(Renderer, float)>();
        static Material poolMat, skyMat;
        static readonly int PColorHigh = Shader.PropertyToID("_ColorHigh");
        Light keyLight; ColorAdjustments grade; float tempoBlend = 1;   // 1 = Haste look, 0 = Normal look
        Color baseEquator;
        public float TempoBlend => tempoBlend;
        readonly List<Renderer> cracks = new List<Renderer>(), seams = new List<Renderer>(), tempoLineRs = new List<Renderer>();
        Light metronomeLight; MaterialPropertyBlock mpb;
        MaterialPropertyBlock poolMpb;
        ReflectionProbe probe; bool probeRendered;
        public bool ProbeRendered => probeRendered;
        double previewBeat; int lastBeat; float escapement, escShown, pawlKick, flash;
        static readonly int PEmission = Shader.PropertyToID("_EmissionColor"), PBase = Shader.PropertyToID("_BaseColor");
        static readonly Dictionary<int, Material> mats = new Dictionary<int, Material>();

        void Awake() { Build(); }

        // shared cached materials (per colour / finish), never instanced per object
        static Material M(Color c, float smooth = 0.2f, float metal = 0f, bool glow = false)
        {
            int key = Mathf.RoundToInt(c.r * 255) | Mathf.RoundToInt(c.g * 255) << 8 | Mathf.RoundToInt(c.b * 255) << 16 |
                      Mathf.RoundToInt(smooth * 15) << 24 | (metal > 0.5f ? 1 : 0) << 28 | (glow ? 1 : 0) << 29;
            if (mats.TryGetValue(key, out var m) && m) return m;
            m = Prim.Material(c, glow ? 2f : 0); m.name = "MetronomeCourt_" + key;
            m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
            ApplySurface(m, c, metal, glow);   // October 27: painted surface kit (Surfaces.cs)
            return mats[key] = m;
        }
        static Material StoneM(Color c) => M(c, 0.18f);

        // ---- floor stone: shared tiling detail (albedo grain + a faint normal), one material per shade ----
        static Texture2D floorAlbedo, floorNormal;
        static readonly Dictionary<int, Material> floorMats = new Dictionary<int, Material>();
        public const float FloorTile = 2.5f;   // metres per detail tile (planar UVs from Sector)
        static Material FloorM(Color c, float smooth = 0.18f)
        {
            int key = Mathf.RoundToInt(c.r * 255) | Mathf.RoundToInt(c.g * 255) << 8 | Mathf.RoundToInt(c.b * 255) << 16 | Mathf.RoundToInt(smooth * 15) << 24;
            if (floorMats.TryGetValue(key, out var m) && m) return m;
            BuildFloorDetail();
            m = Prim.Material(c); m.name = "MetronomeCourt_Floor_" + key;
            m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0f);
            m.SetTexture("_BaseMap", floorAlbedo); m.SetTexture("_MainTex", floorAlbedo);
            // no normal map (October 25, 2026): even the gentle blurred one sparkled on the slabs as the camera moved
            m.SetFloat("_Smoothness", Mathf.Min(smooth, 0.12f));
            return floorMats[key] = m;
        }
        public static bool FloorDetailReady => floorAlbedo && floorNormal;

        // tileable value-noise fbm: albedo around 1.0 +-6% with a few darker pits; the normal comes from the same height
        static void BuildFloorDetail()
        {
            if (floorAlbedo && floorNormal) return;
            const int S = 256;
            var h = new float[S * S];
            float Hash(int x, int y, int p) { x = ((x % p) + p) % p; y = ((y % p) + p) % p; uint n = (uint)(x * 374761393 + y * 668265263 + p * 1442695041); n = (n ^ (n >> 13)) * 1274126177u; return ((n ^ (n >> 16)) & 0xffff) / 65535f; }
            float Noise(float x, float y, int p)
            {
                int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y); float fx = x - xi, fy = y - yi; fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
                return Mathf.Lerp(Mathf.Lerp(Hash(xi, yi, p), Hash(xi + 1, yi, p), fx), Mathf.Lerp(Hash(xi, yi + 1, p), Hash(xi + 1, yi + 1, p), fx), fy);
            }
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = x / (float)S, v = y / (float)S, n = 0, amp = 0.5f, sum = 0;
                    for (int o = 0, p = 8; o < 3; o++, p *= 2) { n += Noise(u * p, v * p, p) * amp; sum += amp; amp *= 0.5f; }   // no finest octave: it only aliases at gameplay distance
                    n /= sum;
                    float pit = Noise(u * 16, v * 16, 16); if (pit > 0.86f) n -= (pit - 0.86f) * 1.25f;   // sparse pits
                    h[y * S + x] = n;
                }
            // the normal map comes from a 5x5 box-blurred height, so it carries only broad undulation (no specular sparkle)
            var hb = new float[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float acc = 0; for (int j = -2; j <= 2; j++) for (int i = -2; i <= 2; i++) acc += h[((y + j + S) % S) * S + (x + i + S) % S];
                    hb[y * S + x] = acc / 25f;
                }
            var alb = new Color32[S * S]; var nrm = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float n = h[y * S + x];
                    byte a = (byte)Mathf.Clamp(Mathf.RoundToInt((0.98f + (n - 0.5f) * 0.12f) * 255), 0, 255);
                    alb[y * S + x] = new Color32(a, a, a, 255);
                    float dx = (hb[y * S + (x + 2) % S] - hb[y * S + (x + S - 2) % S]) * 1.5f, dy = (hb[((y + 2) % S) * S + x] - hb[((y + S - 2) % S) * S + x]) * 1.5f;   // from the blurred height
                    var nv = new Vector3(-dx, -dy, 1).normalized;
                    // AG packing (URP UnpackNormalRGorAG): r = 1, g = y, a = x
                    nrm[y * S + x] = new Color32(255, (byte)Mathf.RoundToInt((nv.y * 0.5f + 0.5f) * 255), 0, (byte)Mathf.RoundToInt((nv.x * 0.5f + 0.5f) * 255));
                }
            floorAlbedo = new Texture2D(S, S, TextureFormat.RGBA32, true, false) { name = "MetronomeCourt_FloorAlbedo", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            floorAlbedo.SetPixels32(alb); floorAlbedo.Apply(true, Application.isPlaying);   // readable in the editor, for the menu's baked copy
            floorNormal = new Texture2D(S, S, TextureFormat.RGBA32, true, true) { name = "MetronomeCourt_FloorNormal", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            floorNormal.SetPixels32(nrm); floorNormal.Apply(true, true);
        }
        static Material BrassM => M(Brass, 0.7f, 0.85f);
        static Material FloorBrassM => M(Brass * 0.62f, 0.3f, 0.4f);   // October 27 pass 3: aged, quieter medallion and plaques   // inlays flush with the floor: low gloss (no specular crawl)
        static Material IronM => M(Iron, 0.35f, 0.6f);

        Transform P(PrimitiveKind k, Transform parent, Vector3 pos, Vector3 scale, Material mat, Vector3? euler = null, string name = null)
        {
            var go = new GameObject(name ?? k.ToString());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = scale; go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            go.AddComponent<MeshFilter>().sharedMesh = Prim.MeshOf(k);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }
        Transform Mesh(Transform parent, Mesh mesh, Material mat, string name, Vector3 pos = default)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        // flat-topped annular sector block (top at y = 0, `depth` deep), angles in degrees clockwise from +z. `gap` is a
        // half-joint inset applied as a parallel offset (constant width at every radius), not a constant angle.
        static Mesh Sector(float r0, float r1, float a0, float a1, float depth, int seg = 6, float gap = 0, float bevel = 0, int uvRot = 0, Vector2 uvOff = default)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d); t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3); }
            bool full = a1 - a0 >= 359.9f;
            float lo(float r, float g) => full || r < 1e-3f ? a0 : a0 + Mathf.Rad2Deg * Mathf.Asin(Mathf.Clamp(g / r, -1, 1));
            float hi(float r, float g) => full || r < 1e-3f ? a1 : a1 - Mathf.Rad2Deg * Mathf.Asin(Mathf.Clamp(g / r, -1, 1));
            float ri = r0 > 0.01f ? r0 + gap : 0, ro = r1 - gap;
            // the top face is inset by `bevel` and a chamfer drops bevel/2 to the slab edge, so every slab edge catches light
            float tb = bevel, ye = -bevel * 0.5f;
            float rti = ri > 0.01f ? ri + tb : 0, rto = ro - tb, gt = full ? gap : gap + tb;
            Vector3 Pt(float r, float u, float y, float g) { float a = Mathf.Lerp(lo(r, g), hi(r, g), u) * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r); }
            for (int s = 0; s < seg; s++)
            {
                float u0 = s / (float)seg, u1 = (s + 1) / (float)seg;
                Quad(Pt(rti, u0, 0, gt), Pt(rti, u1, 0, gt), Pt(rto, u1, 0, gt), Pt(rto, u0, 0, gt));            // top
                if (bevel > 0)
                {
                    Quad(Pt(rto, u0, 0, gt), Pt(rto, u1, 0, gt), Pt(ro, u1, ye, gap), Pt(ro, u0, ye, gap));       // outer chamfer
                    if (ri > 0.01f) Quad(Pt(rti, u1, 0, gt), Pt(rti, u0, 0, gt), Pt(ri, u0, ye, gap), Pt(ri, u1, ye, gap));
                }
                Quad(Pt(ro, u0, ye, gap), Pt(ro, u1, ye, gap), Pt(ro, u1, -depth, gap), Pt(ro, u0, -depth, gap));        // outer
                if (ri > 0.01f) Quad(Pt(ri, u1, ye, gap), Pt(ri, u0, ye, gap), Pt(ri, u0, -depth, gap), Pt(ri, u1, -depth, gap));   // inner
            }
            if (!full)
            {
                if (bevel > 0)
                {
                    Quad(Pt(rti, 0, 0, gt), Pt(rto, 0, 0, gt), Pt(ro, 0, ye, gap), Pt(ri, 0, ye, gap));             // side chamfers
                    Quad(Pt(rto, 1, 0, gt), Pt(rti, 1, 0, gt), Pt(ri, 1, ye, gap), Pt(ro, 1, ye, gap));
                }
                Quad(Pt(ri, 0, ye, gap), Pt(ro, 0, ye, gap), Pt(ro, 0, -depth, gap), Pt(ri, 0, -depth, gap));
                Quad(Pt(ro, 1, ye, gap), Pt(ri, 1, ye, gap), Pt(ri, 1, -depth, gap), Pt(ro, 1, -depth, gap));
            }
            var m = new Mesh { name = "Sector" }; m.SetVertices(v); m.SetTriangles(t, 0);
            // planar detail UVs, rotated by a quarter turn and offset per slab so the grain breaks at every joint
            var uv = new Vector2[v.Count];
            for (int i = 0; i < v.Count; i++)
            {
                var q = new Vector2(v[i].x, v[i].z);
                for (int k = 0; k < (uvRot & 3); k++) q = new Vector2(-q.y, q.x);
                uv[i] = q / FloorTile + uvOff;
            }
            m.uv = uv;
            // winding check on the second top triangle (the first is degenerate when the sector reaches the centre)
            var tris = m.triangles; var n = Vector3.Cross(v[tris[4]] - v[tris[3]], v[tris[5]] - v[tris[3]]);
            if (n.y < 0) { for (int i = 0; i < tris.Length; i += 3) { int x = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = x; } m.triangles = tris; }
            m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds(); return m;
        }

        // rectangular frustum, base centred at the origin on y = 0: bottom half-extents (bx, bz), top (tx, tz), height h.
        // Flat-shaded (each face has its own vertices). A top of ~0 makes a pyramid.
        static readonly Dictionary<string, Mesh> frusta = new Dictionary<string, Mesh>();
        static Mesh Frustum(float bx, float bz, float tx, float tz, float h)
        {
            string key = $"{bx:0.###}|{bz:0.###}|{tx:0.###}|{tz:0.###}|{h:0.###}";
            if (frusta.TryGetValue(key, out var cached) && cached) return cached;
            var b = new[] { new Vector3(-bx, 0, -bz), new Vector3(bx, 0, -bz), new Vector3(bx, 0, bz), new Vector3(-bx, 0, bz) };
            var t = new[] { new Vector3(-tx, h, -tz), new Vector3(tx, h, -tz), new Vector3(tx, h, tz), new Vector3(-tx, h, tz) };
            var v = new List<Vector3>(); var tri = new List<int>();
            void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3) { int i = v.Count; v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3); tri.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 }); }
            Quad(t[0], t[3], t[2], t[1]);                         // top (faces up)
            Quad(b[0], b[1], b[2], b[3]);                         // bottom (faces down)
            for (int i = 0; i < 4; i++) { int j = (i + 1) % 4; Quad(b[i], t[i], t[j], b[j]); }   // sides (face outward)
            var m = new Mesh { name = "Frustum" }; m.SetVertices(v); m.SetTriangles(tri, 0);
            // make every face point away from the centre, whatever winding the quads came out with
            var tris = m.triangles; var centre = new Vector3(0, h / 2, 0);
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = v[tris[i]], c1 = v[tris[i + 1]], c2 = v[tris[i + 2]];
                var n = Vector3.Cross(c1 - a, c2 - a);
                if (n.sqrMagnitude > 1e-10f && Vector3.Dot(n, (a + c1 + c2) / 3 - centre) < 0) { int x = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = x; }
            }
            m.triangles = tris; m.RecalculateNormals(); m.RecalculateBounds();
            return frusta[key] = m;
        }

        // trapezoid prism in the XY plane (base at y = 0), centred on x, depth along z
        static Mesh Trapezoid(float baseHalf, float topHalf, float h, float depth)
        {
            var p = new[] { new Vector3(-baseHalf, 0, 0), new Vector3(baseHalf, 0, 0), new Vector3(topHalf, h, 0), new Vector3(-topHalf, h, 0) };
            var v = new List<Vector3>(); var t = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d); t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2); }
            var dz = new Vector3(0, 0, -depth);
            Quad(p[0], p[1], p[2], p[3]);                       // front (+z side faces the court)
            Quad(p[1] + dz, p[0] + dz, p[3] + dz, p[2] + dz);   // back
            for (int i = 0; i < 4; i++) { var a = p[i]; var b = p[(i + 1) % 4]; Quad(b, a, a + dz, b + dz); }
            var m = new Mesh { name = "Trapezoid" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }

        void Build()
        {
            if (transform.Find("Generated")) return;
            mpb = new MaterialPropertyBlock();
            var root = new GameObject("Generated").transform; root.SetParent(transform, false);
            var stat = new GameObject("Static").transform; stat.SetParent(root, false);
            var rng = new System.Random(23);

            // ---- combat floor: grout disc, three bands of paved wedges, dark dial border ----
            P(PrimitiveKind.Cylinder, stat, new Vector3(0, -0.36f, 0), new Vector3(FloorRadius * 2, 0.34f, FloorRadius * 2), StoneM(FloorGrout), name: "Grout");
            P(PrimitiveKind.Cylinder, stat, new Vector3(0, -3.6f, 0), new Vector3(FloorRadius * 2 + 1.6f, 3.0f, FloorRadius * 2 + 1.6f), StoneM(StoneDark), name: "PlatformDrum");
            // gothic bays round the drum under the court (pointed dark recesses) and four teal banner drops
            for (int i = 0; i < 16; i++)
            {
                float a = i * 22.5f + 11.25f; if (Mathf.Abs(Mathf.DeltaAngle(a, 0)) < 12f) continue;   // the bridge piers own the south face
                var dirB = Quaternion.Euler(0, a, 0) * Vector3.forward; var at = dirB * (FloorRadius + 0.82f);
                // October 27 pass 2: deeper, taller pointed bays framed by stone pilasters and a sill
                P(PrimitiveKind.Cube, stat, at + Vector3.up * -3.7f, new Vector3(1.7f, 3.0f, 0.06f), StoneM(StoneDark * 0.4f), new Vector3(0, a, 0), "DrumBay");
                P(PrimitiveKind.Cube, stat, at + Vector3.up * -2.2f, new Vector3(1.2f, 1.2f, 0.06f), StoneM(StoneDark * 0.4f), new Vector3(0, a, 45), "DrumBayPoint");
                var tanB = Quaternion.Euler(0, a, 0) * Vector3.right;
                foreach (var sx in new[] { -1f, 1f })
                    P(PrimitiveKind.Cube, stat, at + tanB * (sx * 1.12f) + dirB * 0.12f + Vector3.up * -3.3f, new Vector3(0.38f, 4.4f, 0.3f), StoneM(StoneDark * 1.45f), new Vector3(0, a, 0), "DrumPilaster");
                P(PrimitiveKind.Cube, stat, at + dirB * 0.1f + Vector3.up * -5.3f, new Vector3(2.7f, 0.3f, 0.3f), StoneM(StoneDark * 1.45f), new Vector3(0, a, 0), "DrumBaySill");
                P(PrimitiveKind.Cube, stat, at + dirB * 0.02f + Vector3.up * -1.35f, new Vector3(1.7f, 0.12f, 0.06f), M(Brass * 0.6f, 0.45f, 0.8f), new Vector3(0, a, 0), "DrumBayLintel");
                if (i % 4 == 2)
                    P(PrimitiveKind.Cube, stat, at + dirB * 0.08f + Vector3.up * -3.0f, new Vector3(0.7f, 2.6f, 0.03f), M(BannerTeal, 0.3f), new Vector3(0, a, 0), "DrumBanner");
                var bayFace = Quaternion.LookRotation(dirB);
                if (i == 12)
                {   // pass 4 (final critic: "no gear housing"): one west bay opens on part of the drum's iron gear train
                    var gc = at + dirB * 0.14f + Vector3.up * -4.2f; var gearIron = M(Iron * 1.3f, 0.35f, 0.6f);
                    Mesh(stat, Sector(0.6f, 0.82f, 0, 360, 0.14f, 32), gearIron, "HousingGearRim", gc).localRotation = bayFace * Quaternion.Euler(90, 0, 0);
                    for (int k = 0; k < 12; k++)
                        P(PrimitiveKind.Cube, stat, gc + bayFace * (Quaternion.Euler(0, 0, k * 30f) * Vector3.up * 0.9f), new Vector3(0.17f, 0.2f, 0.14f), gearIron, (bayFace * Quaternion.Euler(0, 0, k * 30f)).eulerAngles, "HousingGearTooth");
                    foreach (var sa in new[] { 45f, 135f })
                        P(PrimitiveKind.Cube, stat, gc, new Vector3(0.1f, 1.4f, 0.1f), gearIron, (bayFace * Quaternion.Euler(0, 0, sa)).eulerAngles, "HousingGearSpoke");
                    P(PrimitiveKind.Cylinder, stat, gc + dirB * 0.05f, new Vector3(0.32f, 0.08f, 0.32f), M(Brass * 0.5f, 0.4f, 0.8f), (bayFace * Quaternion.Euler(90, 0, 0)).eulerAngles, "HousingGearHub");
                }
                if (i == 13)
                {   // pass 4 (final critic: "no maintenance doors"): an iron door with brass straps in the next bay
                    P(PrimitiveKind.Cube, stat, at + dirB * 0.06f + Vector3.up * -4.05f, new Vector3(1.05f, 2.2f, 0.05f), M(Iron * 1.25f, 0.35f, 0.6f), new Vector3(0, a, 0), "MaintenanceDoor");
                    foreach (var hy in new[] { -4.75f, -3.4f })
                        P(PrimitiveKind.Cube, stat, at + dirB * 0.09f + Vector3.up * hy, new Vector3(1.1f, 0.09f, 0.03f), M(Brass * 0.5f, 0.4f, 0.8f), new Vector3(0, a, 0), "DoorStrap");
                }
            }
            // rim ledge under the parapet and posts (top just below the floor so it never z-fights the dial band)
            // October 26, 2026: the ledge top used to sit at -0.02, the same height as the grout disc's top that fills
            // every joint, so the two fought inside the joints and the joints seemed to shift while moving. It now sits at
            // -0.06, clear of the grout.
            P(PrimitiveKind.Cylinder, stat, new Vector3(0, -1.11f, 0), new Vector3(29.9f, 1.05f, 29.9f), StoneM(StoneDark * 1.1f), name: "RimLedge");   // cylinder: scale.y = half-height; top at -0.06
            P(PrimitiveKind.Cylinder, stat, new Vector3(0, -0.16f, 0), new Vector3(30.1f, 0.08f, 30.1f), M(Brass * 0.45f, 0.4f, 0.8f), name: "RimLedgeTrim");   // a band on the ledge edge, below its top (no shared surface)
            P(PrimitiveKind.Cone, stat, new Vector3(0, -10.5f, 0), new Vector3(FloorRadius * 2 + 1.6f, 7f, FloorRadius * 2 + 1.6f), StoneM(StoneDark), new Vector3(180, 0, 0), "PlatformKeel");
            // one calm stone family with a wider +-7% jitter on a darker base (keeps telegraph contrast), a shared grain
            // detail, bevelled slab edges, and 1 in 12 slabs weathered (darker, with a damp sheen)
            // October 27: one cool limestone family; a narrower jitter so the disc stops reading as alternating pie wedges
            var jitter = new[] { 0.955f, 0.98f, 1.0f, 1.02f, 1.045f };
            // October 27 pass 2: wear gathers toward the rim (outer band only: a slightly darker family and more worn
            // slabs, chosen by slab index so the shared random sequence, and every later placement, is unchanged)
            Material Shade(bool inner, int outerIndex = -1)
            {
                float k = inner ? 0.97f : 1f;
                if (rng.Next(12) == 0) return FloorM(FloorStone * 0.95f * k, 0.18f);
                var m = FloorM(FloorStone * jitter[rng.Next(jitter.Length)] * k);
                // pass 4 (final critic: wear should gather at the rim as lighter, warmer pigment): the outer band leans warm and
                // its worn slabs are a step lighter; the middle band is unchanged, so the wear fades out by mid-radius
                var worn = Color.Lerp(FloorStone, StoneWarm, 0.3f);
                // (pass-4 critic: the separately lighter worn slabs read as hard-edged patchwork, so they now differ by 2 % only)
                if (outerIndex >= 0) m = (outerIndex * 7) % 5 == 1 ? FloorM(Color.Lerp(FloorStone, StoneWarm, 0.45f) * 1.02f, 0.18f) : FloorM(worn * (outerIndex % 2 == 0 ? 0.99f : 1.005f));
                return m;
            }
            const float gap = 0.025f, bevel = 0.04f;   // 4 cm: wide enough to span pixels (a 2 cm facet crawled when moving)   // half-joint: joints are a constant 0.05 m everywhere
            Mesh(stat, Sector(0, 2.6f, 0, 360, 0.3f, 48, gap, bevel), FloorM(FloorStone * 0.97f), "FloorCentre");
            // middle band 36 slabs (10 degrees): fewer joints than 48, still meets the 30-degree spokes, and staggers
            // against the 24-slab inner and outer bands like real paving
            var bands = new[] { (2.6f, 6.6f, 0f, 24), (6.6f, 9.8f, 0f, 36), (9.8f, DialInner, 0f, 24) };
            foreach (var (r0, r1, off, count) in bands)
                for (int i = 0; i < count; i++)
                {
                    float span = 360f / count, a0 = off + i * span;
                    Mesh(stat, Sector(r0, r1, a0, a0 + span, 0.3f, 6, gap, bevel, rng.Next(4), new Vector2((float)rng.NextDouble(), (float)rng.NextDouble())), Shade(r0 < 3f, r0 > 9f ? i : -1), "Slab");
                }
            // hairline cracks removed (October 25, 2026): 2.5 cm strips are sub-pixel at gameplay distance and crawled when
            // the camera moved. The random draws they used are kept so every later random choice stays the same.
            for (int c = 0; c < 6; c++)
            {
                rng.Next(36); rng.NextDouble(); rng.NextDouble();
                for (int k = 0; k < 3; k++) rng.NextDouble();
            }
            Mesh(stat, Sector(DialInner, FloorRadius, 0, 360, 0.3f, 96), StoneM(DialBand), "DialBand");
            // inlays stack explicitly so nothing z-fights at crossings: spokes top 0.006, rings top 0.012, plaques 0.02+
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f; var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                P(PrimitiveKind.Cube, stat, dir * 7.5f + Vector3.up * 0.003f, new Vector3(0.12f, 0.006f, 9.8f), M(BrassInlay, 0.3f, 0.4f), new Vector3(0, a, 0), "HourSpoke");   // flat strips: low gloss so they don't mirror the sky
            }
            Ring(stat, 2.6f, 0.08f, 48, M(BrassInlay, 0.3f, 0.4f), "InnerRingInlay");
            // centre medallion: an inner brass ring with 12 notches
            Ring(stat, 1.6f, 0.08f, 36, FloorBrassM, "CentreRing");
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f + 15f; var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                P(PrimitiveKind.Cube, stat, dir * 1.45f + Vector3.up * 0.006f, new Vector3(0.08f, 0.012f, 0.22f), FloorBrassM, new Vector3(0, a, 0), "CentreNotch");
            }
            Ring(stat, DialInner, 0.1f, 120, M(Brass * 0.55f, 0.3f, 0.4f), "DialInnerLine");   // dimmer: three bright rim bands competed with warnings
            var tempoLine = new GameObject("TempoLine").transform; tempoLine.SetParent(root, false);
            SegmentedRing(tempoLine, TempoLineRadius, 0.14f, 120, M(HasteTint, 0.2f, 0, true), "TempoLineSeg");
            foreach (var r in tempoLine.GetComponentsInChildren<Renderer>()) tempoLineRs.Add(r);
            Ring(stat, FloorRadius - 0.06f, 0.1f, 120, M(Brass * 0.55f, 0.3f, 0.4f), "DialOuterLine");
            var glyph = StoneM(DialBand * 0.8f);
            for (int i = 0; i < 60; i++)
            {
                float a = i * 6f; bool hour = i % 5 == 0; var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                if (hour)
                {
                    P(PrimitiveKind.Cube, stat, dir * 12.75f + Vector3.up * 0.01f, new Vector3(0.62f, 0.02f, 0.42f), FloorBrassM, new Vector3(0, a, 0), "HourPlaque");
                    // Roman numeral in dark strips, XII at the north (-z), tops pointing away from the centre
                    int hourNum = Mathf.RoundToInt(Mathf.Repeat(a - 180f, 360f) / 30f); if (hourNum == 0) hourNum = 12;
                    Numeral(stat, dir * 12.75f + Vector3.up * 0.024f, dir.z > 0.1f ? a + 180f : a, Roman(hourNum), glyph);   // south half: tops face the centre, upright to the camera
                }
                else P(PrimitiveKind.Cube, stat, dir * 12.8f + Vector3.up * 0.006f, new Vector3(0.07f, 0.01f, 0.3f), M(BrassInlay, 0.3f, 0.4f), new Vector3(0, a, 0), "MinuteTick");
            }

            // ---- perimeter: continuous low parapet (camera side), taller parapet + posts + lanterns (far side) ----
            for (int i = 0; i < 48; i++)
            {
                float a = i * 7.5f + 3.75f; if (Mathf.Abs(Mathf.DeltaAngle(a, 0)) < 9f) continue;   // south bridge gap (+z)
                var dir = Quaternion.Euler(0, a, 0) * Vector3.forward; var at = dir * ParapetRadius;
                bool camSide = CameraSide(at);
                float ph = camSide ? 0.4f : 0.84f;   // camera-side parapet stays low: never hides a player
                var inward = -dir; var yawE = new Vector3(0, a, 0);
                P(PrimitiveKind.Cube, stat, at + dir * 0.22f + Vector3.up * 0.1f, new Vector3(1.8f, 0.2f, 0.06f), StoneM(StoneDark), yawE, "ParapetPlinth");
                if (camSide)
                {
                    P(PrimitiveKind.Cube, stat, at + Vector3.up * ph / 2, new Vector3(1.78f, ph, 0.42f), StoneM(Ivory), yawE, "ParapetSegment");
                    // chamfered brass cap so the rim edge reads from the high camera
                    Mesh(stat, Frustum(0.91f, 0.22f, 0.86f, 0.17f, 0.06f), StoneM(Ivory * 0.88f), "ParapetCap", at + Vector3.up * ph).localRotation = Quaternion.Euler(yawE);   // lit stone, not a gold band
                    // a recessed dark panel on the outer face (the side the south camera actually sees)
                    P(PrimitiveKind.Cube, stat, at + dir * 0.215f + Vector3.up * 0.22f, new Vector3(1.4f, 0.18f, 0.01f), StoneM(StoneDark * 0.9f), yawE, "ParapetPanel");
                }
                else
                {
                    // far side: a balustrade (solid base, five balusters, top rail) instead of a blank band
                    P(PrimitiveKind.Cube, stat, at + Vector3.up * 0.125f, new Vector3(1.78f, 0.25f, 0.42f), StoneM(Ivory), yawE, "ParapetBase");
                    P(PrimitiveKind.Cube, stat, at + inward * 0.215f + Vector3.up * 0.13f, new Vector3(1.4f, 0.12f, 0.01f), StoneM(StoneDark * 0.9f), yawE, "ParapetPanel");
                    var tangent = Quaternion.Euler(0, a, 0) * Vector3.right;
                    for (int b = 0; b < 5; b++)
                    {
                        var bp = at + tangent * ((b - 2) * 0.34f) + Vector3.up * 0.25f;
                        Mesh(stat, Frustum(0.06f, 0.06f, 0.045f, 0.045f, 0.5f), StoneM(Ivory), "Baluster", bp).localRotation = Quaternion.Euler(yawE);
                    }
                    P(PrimitiveKind.Cube, stat, at + Vector3.up * 0.795f, new Vector3(1.78f, 0.09f, 0.3f), StoneM(Ivory), yawE, "ParapetRail");
                    // brass string course along the rail and the cap on top
                    P(PrimitiveKind.Cube, stat, at + inward * 0.155f + Vector3.up * 0.76f, new Vector3(1.78f, 0.06f, 0.01f), BrassM, yawE, "StringCourse");
                    Mesh(stat, Frustum(0.91f, 0.2f, 0.87f, 0.15f, 0.06f), StoneM(Ivory * 0.88f), "ParapetCap", at + Vector3.up * 0.84f).localRotation = Quaternion.Euler(yawE);
                }
            }
            for (int i = 0; i < 16; i++)
            {
                float a = i * 22.5f;
                if (Mathf.Abs(Mathf.DeltaAngle(a, 0)) < 5f || Mathf.Abs(Mathf.DeltaAngle(a, 180)) < 5f) continue;   // bridge gap; never in front of the metronome
                Post(stat, root, Quaternion.Euler(0, a, 0) * Vector3.forward * PostRadius, a);
            }
            foreach (var s in new[] { -1, 1 }) Post(stat, root, new Vector3(3.15f * s, 0, PostRadius), 0);   // clear of the bridge newels

            // ---- south entry bridge (decorative approach; not part of the combat bound) ----
            // deck 5.3 m wide (the curb and rail now sit on it, no overhang): a grout bed, an ivory threshold sill at the court,
            // an ivory border course inside each curb, running-bond paving with a brass cross-joint every five rows
            P(PrimitiveKind.Cube, stat, new Vector3(0, -0.41f, 21f), new Vector3(5.3f, 0.7f, 15f), StoneM(FloorGrout), name: "BridgeDeck");
            P(PrimitiveKind.Cube, stat, new Vector3(0, 0.04f, 13.8f), new Vector3(4.6f, 0.08f, 0.45f), StoneM(Ivory), name: "BridgeSill");
            P(PrimitiveKind.Cube, stat, new Vector3(0, 0.03f, 14.04f), new Vector3(4.6f, 0.06f, 0.03f), BrassM, name: "BridgeSillNosing");
            const float paveZ0 = 14.08f, pave = 1.15f, paveHalf = 2.03f;
            for (int row = 0; row < 12; row++)
            {
                float pz = paveZ0 + pave * 0.5f + row * pave, off = row % 2 == 0 ? 0f : pave * 0.5f;   // running bond
                for (float x0 = -paveHalf - off; x0 < paveHalf; x0 += pave)
                {
                    float l = Mathf.Max(x0, -paveHalf), r = Mathf.Min(x0 + pave, paveHalf);
                    if (r - l < 0.2f) continue;
                    int pick = Mathf.Abs(Mathf.RoundToInt(x0 * 7 + row * 3)) % 4;
                    // October 27 pass 3: textured paving (surface kit) with a warmer, worn centre path
                    var paveC = Mathf.Abs((l + r) / 2) < 1.2f ? new Color(0.53f, 0.50f, 0.46f) : new Color(0.46f, 0.46f, 0.47f);
                    // pass 4 (critic: "blocky, hard-stepped patches"): a soft paving texture, gentler value steps between slabs
                    var slab = row % 5 == 3 && pick == 1 ? PavingM(paveC * 0.9f) : PavingM(paveC * new[] { 0.96f, 1.0f, 1.03f, 0.985f }[pick]);
                    P(PrimitiveKind.Cube, stat, new Vector3((l + r) / 2, -0.05f, pz), new Vector3(r - l - 0.05f, 0.1f, pave - 0.05f), slab, name: "BridgeSlab");
                }
                if (row % 5 == 4) P(PrimitiveKind.Cube, stat, new Vector3(0, 0.003f, pz + pave * 0.5f), new Vector3(paveHalf * 2, 0.006f, 0.04f), M(BrassInlay, 0.45f, 0.8f), name: "BridgeBrassJoint");
            }
            P(PrimitiveKind.Cube, stat, new Vector3(0, -0.05f, 28.22f), new Vector3(4.06f, 0.1f, 0.5f), StoneM(Ivory * 0.92f), name: "BridgeEndCourse");
            for (int s = -1; s <= 1; s += 2)
            {
                float rx = 2.45f * s;
                P(PrimitiveKind.Cube, stat, new Vector3(2.155f * s, -0.04f, 21.3f), new Vector3(0.25f, 0.12f, 14.4f), StoneM(Ivory * 0.92f), name: "BridgeBorder");
                // balustrade: curb from z 14.5 (clear of the court-mouth bollards), 24 balusters, a slim rail and a darker brass cap
                P(PrimitiveKind.Cube, stat, new Vector3(rx, 0.06f, 21.4f), new Vector3(0.34f, 0.12f, 13.8f), StoneM(Ivory * 0.92f), name: "BridgeCurb");
                for (int k = 0; k < 24; k++)
                {
                    float bz = 14.85f + (k + 0.5f) * (12.6f / 24f);
                    Mesh(stat, Frustum(0.06f, 0.06f, 0.045f, 0.045f, 0.42f), StoneM(Ivory), "BridgeBaluster", new Vector3(rx, 0.12f, bz));
                }
                P(PrimitiveKind.Cube, stat, new Vector3(rx, 0.58f, 21.4f), new Vector3(0.16f, 0.08f, 13.8f), StoneM(Ivory), name: "BridgeRail");
                P(PrimitiveKind.Cube, stat, new Vector3(rx, 0.635f, 21.4f), new Vector3(0.18f, 0.03f, 13.8f), M(Brass * 0.7f, 0.5f, 0.8f), name: "BridgeRailCap");
                // a low newel ends each rail at the court (under 0.8 m: never hides a player)
                // pass 6 (Risxhb: the brass post caps "look basic"): each post gets a plinth course, a sunk panel on its two
                // long faces, an ivory cornice slab that overhangs the shaft, and a stepped brass cap. The rail posts also carry
                // a brass ball finial; the newel at the court mouth stays under 0.8 m, so it has no ball.
                var capBrass = M(Brass * 0.6f, 0.4f, 0.8f); var sunk = StoneM(Ivory * 0.78f);
                void PostKit(Vector3 baseAt, float half, float height, bool ball, string nm)
                {
                    P(PrimitiveKind.Cube, stat, baseAt + Vector3.up * height / 2, new Vector3(half * 2, height, half * 2), StoneM(Ivory), name: nm);
                    P(PrimitiveKind.Cube, stat, baseAt + Vector3.up * 0.06f, new Vector3(half * 2 + 0.08f, 0.12f, half * 2 + 0.08f), StoneM(Ivory * 0.9f), name: nm + "Plinth");
                    foreach (var sx in new[] { -1f, 1f })
                        P(PrimitiveKind.Cube, stat, baseAt + new Vector3(sx * (half + 0.004f), height * 0.52f, 0), new Vector3(0.02f, height * 0.5f, half * 1.2f), sunk, name: nm + "Panel");
                    Mesh(stat, Frustum(half + 0.05f, half + 0.05f, half + 0.05f, half + 0.05f, 0.05f), StoneM(Ivory * 0.92f), nm + "Cornice", baseAt + Vector3.up * height);
                    Mesh(stat, Frustum(half + 0.01f, half + 0.01f, half - 0.03f, half - 0.03f, 0.035f), capBrass, nm + "CapBand", baseAt + Vector3.up * (height + 0.05f));
                    Mesh(stat, Frustum(half - 0.03f, half - 0.03f, 0.05f, 0.05f, ball ? 0.13f : 0.07f), capBrass, nm + "Cap", baseAt + Vector3.up * (height + 0.085f));
                    if (ball) P(PrimitiveKind.Sphere, stat, baseAt + Vector3.up * (height + 0.27f), Vector3.one * 0.13f, capBrass, name: nm + "Ball");
                }
                PostKit(new Vector3(rx, 0, 14.55f), 0.25f, 0.62f, false, "BridgeNewel");
                for (int k = 1; k < 4; k++)
                    PostKit(new Vector3(rx, 0, 13.9f + k * 3.75f), 0.21f, 0.66f, true, "BridgePost");
                // piers with stone arches on the outer faces, and a dark recessed spandrel wall behind them so they silhouette
                // October 27: piers descend into the cloud billows instead of stopping in mid-air
                for (int k = 0; k < 4; k++) P(PrimitiveKind.Cube, stat, new Vector3(rx, -7.5f, 15.5f + k * 4f), new Vector3(0.7f, 14f, 0.9f), StoneM(StoneDark), name: "BridgePier");
                P(PrimitiveKind.Cube, stat, new Vector3(2.15f * s, -1.63f, 21.5f), new Vector3(0.25f, 1.74f, 12f), StoneM(StoneDark * 0.6f), name: "BridgeSpandrelWall");
                for (int k = 0; k < 3; k++)
                {
                    float cz = 17.5f + k * 4f, rad = 1.55f, springY = -2.5f;
                    for (int v = 0; v < 7; v++)
                    {
                        float ang = Mathf.Lerp(-90f, 90f, (v + 0.5f) / 7f);
                        var off = new Vector3(0, Mathf.Cos(ang * Mathf.Deg2Rad) * rad, Mathf.Sin(ang * Mathf.Deg2Rad) * rad);
                        P(PrimitiveKind.Cube, stat, new Vector3(rx, springY, cz) + off, new Vector3(0.72f, 0.34f, 0.72f), StoneM(v == 3 ? Brass * 0.8f : StoneDark * 1.15f), new Vector3(-ang, 0, 0), "BridgeVoussoir");
                    }
                }
            }
            // lanterns at the far end of the bridge (bollard kit)
            foreach (var sx in new[] { -1, 1 }) Post(stat, root, new Vector3(2.45f * sx, 0, 28.1f), 0);

            // ---- north plinth and the independent giant metronome ----
            var m = new GameObject("Metronome (arena object)").transform; m.SetParent(root, false); m.localPosition = new Vector3(0, 0, MetronomeZ);
            P(PrimitiveKind.Cube, stat, new Vector3(0, -2.2f, MetronomeZ), new Vector3(9f, 4.4f, 7f), StoneM(StoneDark), name: "Plinth");
            // October 27: a stone cap with a narrow brass edge (was a full brass plate that blew out under the tempo light),
            // and two stepped foundation courses that carry the plinth down into the cloud sea
            P(PrimitiveKind.Cube, stat, new Vector3(0, 0.05f, MetronomeZ), new Vector3(8.4f, 0.15f, 6.4f), StoneM(Stone * 0.82f), name: "PlinthTrim");
            P(PrimitiveKind.Cube, stat, new Vector3(0, -0.5f, MetronomeZ), new Vector3(9.6f, 0.45f, 7.6f), StoneM(Stone * 0.62f), name: "PlinthCourse");
            P(PrimitiveKind.Cube, stat, new Vector3(0, -1.15f, MetronomeZ), new Vector3(10.3f, 0.5f, 8.3f), StoneM(Stone * 0.52f), name: "PlinthCourse");   // pass 3: one more step   // pass 2: a stepped course under the cap
            P(PrimitiveKind.Cube, stat, new Vector3(0, 0.0f, MetronomeZ), new Vector3(8.6f, 0.12f, 6.6f), M(Brass * 0.5f, 0.4f, 0.8f), name: "PlinthEdge");
            P(PrimitiveKind.Cube, stat, new Vector3(0, -5.2f, MetronomeZ - 0.4f), new Vector3(10.2f, 1.6f, 8.2f), StoneM(StoneDark * 1.1f), name: "PlinthFoundation");
            P(PrimitiveKind.Cube, stat, new Vector3(0, -7.6f, MetronomeZ - 0.8f), new Vector3(11.4f, 3.2f, 9.4f), StoneM(StoneDark * 0.95f), name: "PlinthFoundation");
            for (int k = 0; k < 3; k++)
            {
                P(PrimitiveKind.Cube, stat, new Vector3(0, -0.3f - k * 0.6f, MetronomeZ + 3.8f + k * 0.7f), new Vector3(9f, 0.6f, 0.7f), StoneM(Stone), name: "PlinthStep");
                P(PrimitiveKind.Cube, stat, new Vector3(0, -0.02f - k * 0.6f, MetronomeZ + 4.12f + k * 0.7f), new Vector3(9f, 0.06f, 0.08f), M(Brass * 0.6f, 0.5f, 0.8f), name: "StepNosing");   // no emission (it blew the plinth out)
            }
            foreach (var sx in new[] { -1, 1 }) Post(stat, root, new Vector3(4.2f * sx, 0, MetronomeZ - 2.1f), 0);   // plinth pylons: the rim's post kit, behind the frame base
            BuildMetronome(m);
            foreach (var sx in new[] { -1, 1 }) Lantern(root, new Vector3(3.4f * sx, 1.25f, MetronomeZ + 1.9f), 1.1f);

            // ---- distant gothic clocktowers, layered into the fog ----
            for (int i = 0; i < 22; i++)
            {
                float a = -150 + i * 300f / 21 + ((i / 3) % 2 == 0 ? -7f : 7f); if (Mathf.Abs(Mathf.DeltaAngle(a, 0)) < 25) continue;   // loose clusters
                bool nearLayer = i % 7 == 3;   // a few dark towers at 41-45 m give parallax against the fogged ones
                float r = nearLayer ? 41 + (float)rng.NextDouble() * 4 : 50 + (float)rng.NextDouble() * 24;
                float h = (16 + (float)rng.NextDouble() * 22) * (0.65f + (float)rng.NextDouble() * 0.7f), w = 4 + (float)rng.NextDouble() * 2.5f;
                var pos = Quaternion.Euler(0, a + 180, 0) * Vector3.forward * r; pos.y = -14;
                Tower(stat, pos, h, w, rng, i % 3 == 0);
            }
            {   // a bell tower in the iso frame: north-east, about 55 m, behind the metronome
                var bellPos = new Vector3(24f, -14f, -50f);
                Tower(stat, bellPos, 30f, 6f, rng, true);
            }
            P(PrimitiveKind.Cone, stat, new Vector3(0, -62, 0), new Vector3(300, 50, 300), M(Abyss, 0.1f), new Vector3(180, 0, 0), "AbyssBowl");
            P(PrimitiveKind.Cylinder, stat, new Vector3(0, -8.5f, 0), new Vector3(FloorRadius * 2 + 4f, 1.5f, FloorRadius * 2 + 4f), StoneM(StoneDark * 0.75f), name: "LowerTier");
            for (int i = 0; i < 7; i++)
            {
                float a = 200 + i * (320f / 7) % 320; if (Mathf.Abs(Mathf.DeltaAngle(a, 0)) < 40) continue;   // keep the south camera arc clear
                ArchBridge(stat, a, 34 + (i % 3) * 6f, rng);
            }

            BuildRisingStructures(stat);   // October 27: masonry rising out of the cloud sea (Exterior.cs)
            BakeSurfaceUVs(root);          // October 27: world-scale UVs for the painted surface kit, before batching
            StaticBatchingUtility.Combine(stat.gameObject);
            BuildHaze(root);
            var rim = new GameObject("Tempo rim light"); rim.transform.SetParent(root, false);
            rim.transform.rotation = Quaternion.Euler(25, 0, 0);   // from the north, 25 degrees up, shining south toward the camera
            TempoRimLight = rim.AddComponent<Light>(); TempoRimLight.type = LightType.Directional; TempoRimLight.shadows = LightShadows.None;
            TempoRimLight.intensity = 0.5f; TempoRimLight.color = HasteLightTint;
            // one realtime reflection probe, rendered once, so brass and metal reflect the dusk sky instead of reading khaki
            var pg = new GameObject("Reflection probe (court)"); pg.transform.SetParent(root, false); pg.transform.localPosition = new Vector3(0, 4f, 0);
            probe = pg.AddComponent<ReflectionProbe>(); probe.mode = ReflectionProbeMode.Realtime; probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing; probe.size = new Vector3(90, 40, 90); probe.resolution = 128; probe.importance = 1;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox; probe.shadowDistance = 0;
            BuildAmbience(root);
            BakeSurfaceUVs(root);   // gears and drifting masonry
            BindWorldLighting();
        }

        // ---- tempo changes the world: key light colour, ambient and colour grade (runtime profile copy) ----
        void BindWorldLighting()
        {
            foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                if (l.type == LightType.Directional && (!keyLight || l.intensity > keyLight.intensity)) keyLight = l;
            var vol = FindAnyObjectByType<Volume>();
            if (vol && vol.sharedProfile) { var prof = vol.profile; prof.TryGet(out grade); }   // vol.profile: an instance, never the asset
            baseEquator = RenderSettings.ambientEquatorColor;
            // dusk: blue-violet zenith down to a warm horizon band (no fog on the sky), and linear fog that starts beyond the
            // far floor edge so it softens the backdrop and fills the void without touching the court
            if (!skyMat) { var sh = Resources.Load<Shader>("MetronomeCourtSky"); if (sh) skyMat = new Material(sh) { name = "MetronomeCourt_Sky" }; }
            if (skyMat)
            {
                // October 27: a paler blue-violet opening behind the metronome apex, so the iron frame separates from the sky
                skyMat.SetColor("_Mid", new Color(0.19f, 0.19f, 0.31f)); skyMat.SetColor("_Horizon", new Color(0.36f, 0.30f, 0.40f));
                skyMat.SetColor("_Zenith", new Color(0.07f, 0.08f, 0.16f));
                RenderSettings.skybox = skyMat;
            }
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogStartDistance = FogStart; RenderSettings.fogEndDistance = FogEnd;
            RenderSettings.fogColor = new Color(0.13f, 0.14f, 0.22f);
        }

        void UpdateWorldLighting(bool haste, bool frenzy)
        {
            tempoBlend = Mathf.MoveTowards(tempoBlend, haste ? 1 : 0, Time.deltaTime / 0.15f);   // fast, so the world never disagrees with the HUD's tempo for long
            // October 27: a narrower warm/cool swing so the stone keeps its identity in both tempos (brief section 12)
            // pass 4 (final critic: "the Normal tempo still turns the whole scene blue"): the key, ambient and grade swings
            // multiplied into a +-25% red/blue swing on the stone (measured on the floor: hue 27 sat 0.14 in Haste, hue 216
            // sat 0.25 in Normal). Each is now a few percent, so the stone holds one cool grey and the tempo lives on the
            // ring, the lanterns, the metronome seams and the HUD
            var warm = new Color(1.0f, 0.98f, 0.95f); var cool = new Color(1.0f, 1.0f, 1.0f);   // the dusk base light is already cool
            if (keyLight) keyLight.color = Color.Lerp(cool, warm, tempoBlend);
            RenderSettings.ambientEquatorColor = baseEquator * Color.Lerp(new Color(1.01f, 1.0f, 0.99f), new Color(1.01f, 1.0f, 0.99f), tempoBlend);
            if (grade != null)
            {
                var filter = Color.Lerp(new Color(1.01f, 1.0f, 0.985f), new Color(1.01f, 1.0f, 0.98f), tempoBlend);
                if (frenzy) filter *= new Color(1.02f, 0.97f, 0.97f);   // frenzy lives on cracks and flicker, not a pink floor
                grade.colorFilter.Override(filter);
            }
        }

        static readonly Color WindowGlow = new Color(0.5f, 0.36f, 0.19f), BellGlow = new Color(0.93f, 0.6f, 0.3f);   // windows at half the old emission, bell 1.5x
        void Tower(Transform stat, Vector3 pos, float h, float w, System.Random rng, bool bell)
        {
            float y = pos.y, seg = h / 3f;
            var toward = -Sim.Flat(pos).normalized; var side = Vector3.Cross(Vector3.up, toward);
            float faceYaw = Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg;
            var winM = M(Sim.Flat(pos).magnitude > 55f ? WindowGlow * 1.5f : WindowGlow, 0.2f, 0, true);   // far towers: brighter so the fog doesn't erase them
            // October 27: atmospheric recession, near towers dark stone, far ones dissolving toward dusty indigo/violet
            float far = Mathf.Round(Mathf.InverseLerp(40f, 76f, Sim.Flat(pos).magnitude) * 3f) / 3f;   // four steps: shared materials
            var StoneDark = Color.Lerp(MetronomeCourtArena.StoneDark, new Color(0.30f, 0.30f, 0.42f), far);
            var IronM = M(Color.Lerp(Iron, new Color(0.21f, 0.21f, 0.31f), far), 0.35f, 0.6f);
            for (int k = 0; k < 3; k++)
            {
                float ww = w * (1 - 0.18f * k);
                Mesh(stat, Frustum(ww * 0.5f, ww * 0.5f, ww * 0.45f, ww * 0.45f, seg), StoneM(StoneDark * (0.9f + 0.1f * k)), "TowerTier", new Vector3(pos.x, y, pos.z));   // October 27: tapered tiers, not prisms
                P(PrimitiveKind.Cube, stat, new Vector3(pos.x, y + seg, pos.z), new Vector3(ww + 0.4f, 0.3f, ww + 0.4f), StoneM(StoneDark * 0.8f), name: "TowerCornice");
                // corner buttresses break the box into a gothic mass
                for (int c = 0; c < 4; c++)
                {
                    var o = Quaternion.Euler(0, c * 90 + 45, 0) * Vector3.forward * (ww * 0.68f);
                    Mesh(stat, Frustum(0.32f, 0.32f, 0.12f, 0.12f, seg * 0.9f), StoneM(StoneDark * 0.85f), "Buttress", new Vector3(pos.x, y, pos.z) + o);
                }
                // pointed-arch slit windows (a narrow slit plus a rotated cap) on the side facing the court
                if (k < 2 || !bell)
                    if (rng.NextDouble() < 0.7)
                        for (int sx = -1; sx <= 1; sx += 2)
                        {
                            var wp = new Vector3(pos.x, y + seg * 0.5f, pos.z) + toward * (ww / 2 + 0.02f) + side * (sx * ww * 0.22f);
                            P(PrimitiveKind.Cube, stat, wp, new Vector3(0.18f, seg * 0.32f, 0.05f), winM, new Vector3(0, faceYaw, 0), "WindowSlit");
                            P(PrimitiveKind.Cube, stat, wp + Vector3.up * seg * 0.16f, new Vector3(0.13f, 0.13f, 0.05f), winM, new Vector3(0, faceYaw, 45), "WindowPoint");
                        }
                // the bell opening: a tall lit pointed arch on the top tier of 1 in 3 towers (as in the reference)
                if (k == 2 && bell)
                {
                    var arch = new Vector3(pos.x, y + seg * 0.55f, pos.z) + toward * (ww / 2 + 0.03f);
                    P(PrimitiveKind.Cube, stat, arch, new Vector3(ww * 0.36f, seg * 0.44f, 0.05f), M(BellGlow, 0.2f, 0, true), new Vector3(0, faceYaw, 0), "BellOpening");
                    P(PrimitiveKind.Cube, stat, arch + Vector3.up * (seg * 0.22f) + toward * 0.01f, new Vector3(ww * 0.255f, ww * 0.255f, 0.05f), M(BellGlow, 0.2f, 0, true), new Vector3(0, faceYaw, 45), "BellArchPoint");
                    P(PrimitiveKind.Cube, stat, arch + Vector3.down * (seg * 0.05f) + toward * 0.04f, new Vector3(ww * 0.2f, ww * 0.2f, 0.05f), IronM, new Vector3(0, faceYaw, 0), "BellSilhouette");
                }
                y += seg;
            }
            // a spire 2.2x taller, with four pinnacles at its base and four more at half height
            Mesh(stat, Frustum(w * 0.42f, w * 0.42f, 0.04f, 0.04f, 17.6f), IronM, "Spire", new Vector3(pos.x, y + 0.15f, pos.z));   // four-sided spire, as the flank belfries
            for (int b = 0; b < 4; b++)
            {
                var o = Quaternion.Euler(0, b * 90 + 45, 0) * Vector3.forward * (w * 0.62f);
                P(PrimitiveKind.Cone, stat, new Vector3(pos.x, y - seg * 0.2f, pos.z) + o, new Vector3(0.9f, 2.6f, 0.9f), IronM, name: "Pinnacle");
                var o2 = Quaternion.Euler(0, b * 90, 0) * Vector3.forward * (w * 0.5f);
                P(PrimitiveKind.Cone, stat, new Vector3(pos.x, pos.y + h * 0.5f + 1.3f, pos.z) + o2, new Vector3(0.6f, 2.0f, 0.6f), IronM, name: "Pinnacle");
            }
        }

        // dark arch-bridge silhouette spanning tangentially at radius r: piers, half-cylinder arches, a deck, a few lit windows
        void ArchBridge(Transform stat, float angle, float r, System.Random rng)
        {
            var radial = Quaternion.Euler(0, angle, 0) * Vector3.forward;
            var dir = Quaternion.Euler(0, angle + 30f, 0) * Vector3.forward; var tangent = Vector3.Cross(Vector3.up, dir);   // 30 degrees off tangent
            var centre = radial * r + Vector3.up * -6f; var col = r < 47f ? new Color(0.16f, 0.17f, 0.25f) : new Color(0.12f, 0.13f, 0.19f);
            float span = 18f, yaw = Mathf.Atan2(tangent.x, tangent.z) * Mathf.Rad2Deg;
            P(PrimitiveKind.Cube, stat, centre + Vector3.up * 6f, new Vector3(span, 1.2f, 3f), M(col, 0.1f), new Vector3(0, yaw, 0), "BridgeDeck");
            for (int k = -1; k <= 1; k++)
            {
                P(PrimitiveKind.Cube, stat, centre + tangent * (k * span / 3) + Vector3.up * -5f, new Vector3(1.6f, 22f, 3f), M(col, 0.1f), new Vector3(0, yaw, 0), "BridgePier");   // pass 3: runs down into the cloud (was 12 m, ended in the void)
                if (k < 1)
                {
                    var mid = centre + tangent * ((k + 0.5f) * span / 3);
                    float rad = (span / 3 - 1.6f) / 2f, springY = 5.4f - rad;   // crown just under the deck
                    var frame = Quaternion.LookRotation(dir, Vector3.up);       // local x = tangent, y = up, z = radial
                    for (int v = 0; v < 7; v++)
                    {
                        float ang = Mathf.Lerp(-90f, 90f, (v + 0.5f) / 7f);
                        var off = tangent * (Mathf.Sin(ang * Mathf.Deg2Rad) * rad) + Vector3.up * (springY + Mathf.Cos(ang * Mathf.Deg2Rad) * rad);
                        var vs = P(PrimitiveKind.Cube, stat, mid + off, new Vector3(1.0f, 0.55f, 3f), M(col * 0.95f, 0.1f), null, "BridgeVoussoir");
                        vs.localRotation = frame * Quaternion.Euler(0, 0, -ang);
                    }
                    foreach (var sx in new[] { -1, 1 })   // spandrels fill the haunches up to the deck
                    {
                        var sp = P(PrimitiveKind.Cube, stat, mid + tangent * (sx * rad * 0.95f) + Vector3.up * (springY + rad * 0.85f), new Vector3(1.4f, rad * 0.7f, 3f), M(col, 0.1f), null, "BridgeSpandrel");
                        sp.localRotation = frame;
                    }
                }
            }
            P(PrimitiveKind.Cube, stat, centre + Vector3.up * 6.9f, new Vector3(span, 0.6f, 0.3f), M(col * 1.1f, 0.1f), new Vector3(0, yaw, 0), "BridgeParapet");
            for (int k = -1; k <= 1; k++)
                P(PrimitiveKind.Cube, stat, centre + tangent * (k * span / 3 + 1.5f) + Vector3.up * 6.9f - dir * 0.16f, new Vector3(0.45f, 0.4f, 0.05f), M(WindowGlow * 1.3f, 0.2f, 0, true), new Vector3(0, yaw, 0), "BridgeWindow");
        }

        static string Roman(int n) => new[] { "", "I", "II", "III", "IIII", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII" }[n];

        // a clock numeral of thin strips on an hour plaque (I = one stroke, V and X = two tilted strokes)
        void Numeral(Transform parent, Vector3 at, float yaw, string text, Material mat)
        {
            var tangent = Quaternion.Euler(0, yaw, 0) * Vector3.right; const float hgt = 0.28f, bar = 0.035f;
            float total = 0; foreach (var ch in text) total += ch == 'I' ? 0.06f : 0.15f;
            float x = -total / 2;
            foreach (var ch in text)
            {
                float w = ch == 'I' ? 0.06f : 0.15f; var centre = at + tangent * (x + w / 2);
                if (ch == 'I') P(PrimitiveKind.Cube, parent, centre, new Vector3(bar, 0.004f, hgt), mat, new Vector3(0, yaw, 0), "NumeralStroke");
                else
                {
                    float tilt = ch == 'V' ? 14f : 24f, off = ch == 'V' ? w * 0.22f : 0f;
                    P(PrimitiveKind.Cube, parent, centre - tangent * off, new Vector3(bar, 0.004f, hgt), mat, new Vector3(0, yaw - tilt, 0), "NumeralStroke");
                    P(PrimitiveKind.Cube, parent, centre + tangent * off, new Vector3(bar, 0.004f, hgt), mat, new Vector3(0, yaw + tilt, 0), "NumeralStroke");
                }
                x += w;
            }
        }

        // A floor ring inlay as ONE annulus mesh, top at 0.012. (It used to be short cubes overlapping by 3% at the same
        // height: every joint z-fought and flickered as the camera moved. Risxhb's floor flicker report, October 25, 2026.)
        void Ring(Transform parent, float r, float width, int segs, Material mat, string name)
        {
            Mesh(parent, Sector(r - width / 2, r + width / 2, 0, 360, 0.012f, segs * 2), mat, name, Vector3.up * 0.012f);
        }
        // The tempo line needs one renderer per segment (its chase lights them one by one). Neighbouring segments
        // overlap slightly so no gap shows, so they alternate between two heights and never share a surface.
        void SegmentedRing(Transform parent, float r, float width, int segs, Material mat, string name)
        {
            float seg = 2 * Mathf.PI * r / segs * (name == "TempoLineSeg" ? 0.62f : 1.03f);   // October 27 pass 2: the tempo ring is dashed
            for (int i = 0; i < segs; i++)
            {
                float a = (i + 0.5f) * 360f / segs; var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                float h = i % 2 == 0 ? 0.012f : 0.015f;
                P(PrimitiveKind.Cube, parent, dir * r + Vector3.up * (h / 2), new Vector3(seg, h, width), mat, new Vector3(0, a, 0), name);   // yaw a: long axis tangent
            }
        }

        // The fixed gameplay camera sits south (+z) of the player looking north and down: anything tall between them
        // could hide a player at the wall, so camera-side perimeter pieces are low bollards with a lantern on top.
        static bool CameraSide(Vector3 at) => at.z > -4f;

        void Lantern(Transform root, Vector3 at, float scale, bool pool = false)
        {
            var l = new GameObject("Lantern").transform; l.SetParent(root, false); l.localPosition = at;
            var core = P(PrimitiveKind.Sphere, l, Vector3.zero, Vector3.one * 0.38f * scale, M(new Color(1f, 0.7f, 0.3f), 0.2f, 0, true), name: "LanternCore");
            for (int k = 0; k < 4; k++)
                P(PrimitiveKind.Cube, l, Quaternion.Euler(0, k * 90 + 45, 0) * Vector3.forward * 0.2f * scale, new Vector3(0.03f, 0.44f, 0.03f) * scale, IronM, name: "LanternBar");
            Mesh(l, Frustum(0.15f * scale, 0.15f * scale, 0.012f * scale, 0.012f * scale, 0.18f * scale), IronM, "LanternCap", Vector3.up * 0.26f * scale);
            P(PrimitiveKind.Cube, l, Vector3.up * 0.235f * scale, new Vector3(0.36f, 0.035f, 0.36f) * scale, IronM, name: "LanternDripBand");   // pass 6
            P(PrimitiveKind.Sphere, l, Vector3.up * 0.48f * scale, Vector3.one * 0.07f * scale, BrassM, name: "LanternFinial");
            var glowRing = P(PrimitiveKind.Cube, l, Vector3.up * 0.25f * scale, new Vector3(0.34f, 0.02f, 0.34f) * scale, M(new Color(1f, 0.7f, 0.3f), 0.2f, 0, true), name: "LanternGlowRing");
            lanterns.Add((glowRing.GetComponent<Renderer>(), at.x));
            P(PrimitiveKind.Cube, l, Vector3.down * 0.24f * scale, new Vector3(0.4f, 0.05f, 0.4f) * scale, BrassM, name: "LanternBase");
            lanterns.Add((core.GetComponent<Renderer>(), at.x));
            if (pool) LanternPool(root, at);
        }

        // depth for the gameplay void: a soft disc of fog colour 6 m under the rim, fading outward, so the drop beside the
        // court reads as haze instead of flat black
        void BuildHaze(Transform root)
        {
            var src = Resources.Load<Material>("Run/FX_ColorDissolve");
            var mat = src ? new Material(src) : new Material(Shader.Find("LastBell/FX/ColorDissolve"));
            mat.name = "MetronomeCourt_Haze";
            mat.SetFloat("_SrcBlend", 1); mat.SetFloat("_DstBlend", 10);   // premultiplied alpha over
            mat.SetFloat("_Mode", 1); var fog = new Color(0.16f, 0.17f, 0.26f); mat.SetColor("_ColorLow", fog); mat.SetColor(PColorHigh, fog);
            mat.SetFloat("_Dissolve", 0); mat.SetFloat("_Soft", 1f); mat.SetVector("_NoiseScale", Vector4.zero); mat.SetVector("_Scroll", Vector4.zero);
            mat.SetFloat("_Intensity", 1.4f); mat.renderQueue = 2980;
            // October 27 (Risxhb): the flat cloud decks and the haze disc read as an empty lavender plane; a menu-like sea
            // of soft cloud puffs replaces them (Exterior.cs). The old decks remain as the fallback if its shader is missing.
            BuildCloudSea(root);
            if (CloudPuffCount > 0) return;
            foreach (var (cy, ch, ci) in new[] { (-12f, 110f, 0.6f), (-26f, 150f, 0.75f) })   // a cloud deck: two broad layers, lighter than the fog
            {
                var deckM = new Material(mat) { name = "MetronomeCourt_CloudDeck" };
                var cloud = new Color(0.22f, 0.22f, 0.32f); deckM.SetColor("_ColorLow", cloud * 0.7f); deckM.SetColor(PColorHigh, cloud); deckM.SetFloat("_Intensity", ci); deckM.renderQueue = 2970;
                deckM.SetVector("_NoiseScale", new Vector4(6, 6, 0, 0)); deckM.SetVector("_Scroll", new Vector4(0.004f, 0.002f, 0, 0));   // breakup
                var dg = new GameObject("Cloud deck"); dg.transform.SetParent(root, false); dg.transform.localPosition = new Vector3(0, cy, 0);
                var dm = new Mesh { name = "CloudQuad" };
                dm.vertices = new[] { new Vector3(-ch, 0, -ch), new Vector3(-ch, 0, ch), new Vector3(ch, 0, -ch), new Vector3(ch, 0, ch) };
                dm.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0), new Vector2(1, 1) };
                dm.triangles = new[] { 0, 1, 2, 1, 3, 2 }; dm.RecalculateBounds();
                dg.AddComponent<MeshFilter>().sharedMesh = dm;
                var dr = dg.AddComponent<MeshRenderer>(); dr.sharedMaterial = deckM; dr.shadowCastingMode = ShadowCastingMode.Off; dr.receiveShadows = false;
            }
            var go = new GameObject("Haze (under the rim)"); go.transform.SetParent(root, false); go.transform.localPosition = new Vector3(0, -6f, 0);
            const float h = 70f;
            var mesh = new Mesh { name = "HazeQuad" };
            mesh.vertices = new[] { new Vector3(-h, 0, -h), new Vector3(-h, 0, h), new Vector3(h, 0, -h), new Vector3(h, 0, h) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0), new Vector2(1, 1) };
            mesh.triangles = new[] { 0, 1, 2, 1, 3, 2 }; mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        }

        // ---------------- ambience (presentation only, outside the play area, nothing casts shadows) ----------------
        // giant clock gears in the northern sky (turn faster in Haste), broken masonry drifting in the void around and
        // below the court, a far fog-coloured city layer, cloud banks drifting past the rim, and faint dust/ember motes
        readonly List<(Transform t, float speed)> gears = new List<(Transform, float)>();
        readonly List<(Transform t, Vector3 home, float phase, Vector3 spin)> drifters = new List<(Transform, Vector3, float, Vector3)>();
        readonly List<(Transform t, float radius, float angle, float y, float speed)> cloudBanks = new List<(Transform, float, float, float, float)>();
        Mesh moteMesh; Vector3[] motePos, moteVel; Vector3[] moteVerts; Color[] moteCols; float gearSpeed = 1f;
        public int GearCount => gears.Count;
        public int DrifterCount => drifters.Count;
        public float GearAngle { get; private set; }   // degrees the first sky gear has turned (accumulated)
        const int Motes = 60;

        void BuildAmbience(Transform root)
        {
            var arng = new System.Random(97);
            var gearM = M(new Color(0.17f, 0.18f, 0.26f), 0.35f, 0.6f);   // October 27: mid-distance iron, not a black wall
            var gearTrim = M(Brass * 0.35f, 0.4f, 0.8f);
            // October 27 (critic: a wall of solid gears crowded the landmark): openwork gears (rim, spokes, hub; the sky shows
            // through) moved off the metronome's sky opening. One master gear on the west flank, half sunk in the cloud sea,
            // a smaller partner above it, and a third low in the east cloud. Each faces the court.
            foreach (var (pos, radius, teeth, dir) in new[] { (new Vector3(-33f, -4f, -24f), 13f, 30, 0.5f), (new Vector3(-44f, -7f, -8f), 7f, 18, -1.2f), (new Vector3(36f, -9f, -14f), 8f, 20, 0.8f) })
            {
                var g = new GameObject("Sky gear").transform; g.SetParent(root, false); g.localPosition = pos;
                g.localRotation = Quaternion.LookRotation(-Sim.Flat(pos).normalized, Vector3.up);
                var spin = new GameObject("Spin").transform; spin.SetParent(g, false);
                Mesh(spin, Sector(radius * 0.8f, radius, 0, 360, 0.7f, 72), gearM, "GearRim").localRotation = Quaternion.Euler(90, 0, 0);
                P(PrimitiveKind.Cylinder, spin, new Vector3(0, 0, -0.35f), new Vector3(radius * 0.3f, 0.5f, radius * 0.3f), gearTrim, new Vector3(90, 0, 0), "GearHub");
                for (int k = 0; k < teeth; k++)
                {
                    float a = k * 360f / teeth;
                    P(PrimitiveKind.Cube, spin, Quaternion.Euler(0, 0, a) * Vector3.up * (radius + 0.6f), new Vector3(radius * 0.14f, 1.4f, 0.6f), gearM, new Vector3(0, 0, a), "GearTooth");
                }
                for (int k = 0; k < 3; k++)   // six broad spokes (three bars through the hub) with wide gaps for the sky
                    P(PrimitiveKind.Cube, spin, new Vector3(0, 0, -0.35f), new Vector3(radius * 0.13f, radius * 1.7f, 0.5f), gearM, new Vector3(0, 0, k * 60f), "GearSpoke");
                gears.Add((spin, dir));
            }

            // floating broken masonry: below the rim (y -2.5 .. -12), never above the court, darker than the floor
            var chunkStone = StoneM(StoneDark * 0.8f);   // darker than the fog: parallax, never competes
            for (int i = 0; i < 14; i++)
            {
                float a = 40f + i * (280f / 13f) + (float)arng.NextDouble() * 10f;   // all round except straight north (behind the metronome)
                if (Mathf.Abs(Mathf.DeltaAngle(a + 180f, 0f)) < 22f) a += 44f;   // keep clear of the south bridge
                float r = 18f + (float)arng.NextDouble() * 16f, y = -14f - (float)arng.NextDouble() * 6f;   // October 27: debris half-sunk in the cloud sea, not loose in the void
                if (r < 26f) y = Mathf.Min(y, -14f);   // near pieces sink well below the rim: they never reach into gameplay frames
                var home = Quaternion.Euler(0, a + 180f, 0) * Vector3.forward * r + Vector3.up * y;
                var t = new GameObject("Drifting masonry").transform; t.SetParent(root, false); t.localPosition = home;
                t.localRotation = Quaternion.Euler((float)arng.NextDouble() * 40 - 20, (float)arng.NextDouble() * 360, (float)arng.NextDouble() * 40 - 20);
                int kind = i % 5;
                if (kind == 0)
                {   // pass 4 (critic: "a stray brass disc"): a broken column drum with its capital, not a brass-rimmed clock face
                    P(PrimitiveKind.Cylinder, t, Vector3.zero, new Vector3(1.5f, 1.1f, 1.5f), chunkStone, null, "ColumnDrum");
                    P(PrimitiveKind.Cube, t, Vector3.up * 1.25f, new Vector3(2.0f, 0.4f, 2.0f), chunkStone, null, "ColumnCapital");
                }
                else if (kind == 1)
                {   // a snapped spire
                    P(PrimitiveKind.Cube, t, Vector3.zero, new Vector3(1.6f, 2.4f, 1.6f), chunkStone, null, "SpireBase");
                    P(PrimitiveKind.Cone, t, Vector3.up * 2.4f, new Vector3(1.4f, 3.0f, 1.4f), IronM, new Vector3(0, 0, 18), "SpireTip");
                }
                else
                {   // paving slabs and broken blocks
                    float w = 1.2f + (float)arng.NextDouble() * 1.6f;
                    P(PrimitiveKind.Cube, t, Vector3.zero, new Vector3(w, 0.35f + (float)arng.NextDouble() * 0.5f, w * 0.7f), kind == 2 ? FloorM(FloorStone * 0.7f) : chunkStone, null, "Slab");
                    if (arng.NextDouble() < 0.5) P(PrimitiveKind.Cube, t, new Vector3(w * 0.3f, -0.4f, 0.1f), new Vector3(w * 0.5f, 0.5f, w * 0.4f), chunkStone, new Vector3(0, 25, 0), "Chunk");
                }
                foreach (var mr in t.GetComponentsInChildren<MeshRenderer>()) { mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; }
                drifters.Add((t, home, (float)arng.NextDouble() * 6.28f, new Vector3((float)arng.NextDouble() * 4 - 2, (float)arng.NextDouble() * 6 - 3, (float)arng.NextDouble() * 4 - 2)));
            }

            // far city layer: flat fog-coloured silhouettes at 90-140 m (the fog swallows them into a second skyline)
            var farM = M(new Color(0.11f, 0.12f, 0.18f), 0.1f); var farWin = M(new Color(0.3f, 0.2f, 0.1f), 0.2f, 0, true);
            var far = new GameObject("Far city").transform; far.SetParent(root, false);
            for (int i = 0; i < 26; i++)
            {
                float a = -160f + i * 320f / 25f; if (Mathf.Abs(Mathf.DeltaAngle(a, 0)) < 30) continue;   // keep the south (camera) arc clear
                float r = 95f + (float)arng.NextDouble() * 45f, h = 18f + (float)arng.NextDouble() * 34f, w = 5f + (float)arng.NextDouble() * 6f;
                var pos = Quaternion.Euler(0, a + 180f, 0) * Vector3.forward * r; pos.y = -20f;
                var yaw = new Vector3(0, a, 0);
                P(PrimitiveKind.Cube, far, pos + Vector3.up * h / 2, new Vector3(w, h, 1.5f), farM, yaw, "FarTower");
                P(PrimitiveKind.Cone, far, pos + Vector3.up * (h + 5f), new Vector3(w * 0.7f, 10f, 1.5f), farM, yaw, "FarSpire");
                if (i % 3 == 0) P(PrimitiveKind.Cube, far, pos + Vector3.up * h * 0.7f - Sim.Flat(pos).normalized * 0.8f, new Vector3(0.5f, 1.2f, 0.1f), farWin, yaw, "FarWindow");
            }
            BakeSurfaceUVs(far); StaticBatchingUtility.Combine(far.gameObject);

            // cloud banks: broad soft sheets drifting slowly round the court just under the rim
            var srcM = Resources.Load<Material>("Run/FX_ColorDissolve");
            var bankM = srcM ? new Material(srcM) : new Material(Shader.Find("LastBell/FX/ColorDissolve"));
            bankM.name = "MetronomeCourt_CloudBank"; var cloudC = new Color(0.21f, 0.21f, 0.30f);   // lighter than the void or it vanishes
            bankM.SetFloat("_SrcBlend", 1); bankM.SetFloat("_DstBlend", 10); bankM.SetFloat("_Mode", 1);
            bankM.SetColor("_ColorLow", cloudC); bankM.SetColor(PColorHigh, cloudC); bankM.SetFloat("_Dissolve", 0); bankM.SetFloat("_Soft", 1f);
            bankM.SetVector("_NoiseScale", new Vector4(3, 3, 0, 0)); bankM.SetVector("_Scroll", new Vector4(0.02f, 0.01f, 0, 0)); bankM.SetFloat("_Intensity", 1.0f); bankM.renderQueue = 2975;
            for (int i = 0; i < 9; i++)
            {
                float rad = 24f + (float)arng.NextDouble() * 22f, ang = i * 40f + (float)arng.NextDouble() * 20f, y = -6.5f - (float)arng.NextDouble() * 4f;   // October 27: lower, among the rising masonry
                float sz = 14f + (float)arng.NextDouble() * 12f;
                var go = new GameObject("Cloud bank"); go.transform.SetParent(root, false);
                // placed at its orbit position now, not only from Update (October 26, 2026): the menu's baked copy of the
                // court has no arena script, and the banks were left stacked at the centre, fighting the floor slabs
                go.transform.localPosition = Quaternion.Euler(0, ang, 0) * Vector3.forward * rad + Vector3.up * y;
                go.transform.localRotation = Quaternion.Euler(0, ang, 0);
                var puffs = CloudMaterial();   // October 27: a drifting cluster of soft puffs instead of a flat sheet
                go.AddComponent<MeshFilter>().sharedMesh = puffs ? BankMesh(sz * 0.6f, new System.Random(1000 + i)) : FlatQuad(sz, sz * 0.55f);
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = puffs ? puffs : bankM; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
                cloudBanks.Add((go.transform, rad, ang, y, (i % 2 == 0 ? 1f : 0.7f) * (0.6f + (float)arng.NextDouble() * 0.5f)));
            }

            // dust and ember motes: one dynamic mesh of small camera-facing quads around the court, additive and faint
            var moteM = srcM ? new Material(srcM) : new Material(Shader.Find("LastBell/FX/ColorDissolve"));
            moteM.name = "MetronomeCourt_Motes"; moteM.SetFloat("_SrcBlend", 1); moteM.SetFloat("_DstBlend", 1); moteM.SetFloat("_Mode", 1);
            moteM.SetColor("_ColorLow", Color.black); moteM.SetColor(PColorHigh, Color.white); moteM.SetFloat("_Dissolve", 0); moteM.SetFloat("_Soft", 1f);
            moteM.SetVector("_NoiseScale", Vector4.zero); moteM.SetVector("_Scroll", Vector4.zero); moteM.SetFloat("_Intensity", 1f); moteM.renderQueue = 2985;
            motePos = new Vector3[Motes]; moteVel = new Vector3[Motes]; moteVerts = new Vector3[Motes * 4]; moteCols = new Color[Motes * 4];
            var uvs = new Vector2[Motes * 4]; var tris = new int[Motes * 6];
            for (int i = 0; i < Motes; i++)
            {
                motePos[i] = RandomMote(arng);
                moteVel[i] = new Vector3((float)arng.NextDouble() - 0.5f, 0.15f + (float)arng.NextDouble() * 0.35f, (float)arng.NextDouble() - 0.5f) * 0.6f;
                uvs[i * 4] = new Vector2(0, 0); uvs[i * 4 + 1] = new Vector2(0, 1); uvs[i * 4 + 2] = new Vector2(1, 0); uvs[i * 4 + 3] = new Vector2(1, 1);
                int b = i * 4, t6 = i * 6; tris[t6] = b; tris[t6 + 1] = b + 1; tris[t6 + 2] = b + 2; tris[t6 + 3] = b + 1; tris[t6 + 4] = b + 3; tris[t6 + 5] = b + 2;
            }
            moteMesh = new Mesh { name = "Motes" }; moteMesh.MarkDynamic();
            moteMesh.vertices = moteVerts; moteMesh.uv = uvs; moteMesh.colors = moteCols; moteMesh.triangles = tris;
            moteMesh.bounds = new Bounds(Vector3.up * 3f, new Vector3(60, 20, 60));
            var mg = new GameObject("Motes"); mg.transform.SetParent(root, false);
            mg.AddComponent<MeshFilter>().sharedMesh = moteMesh;
            var mmr = mg.AddComponent<MeshRenderer>(); mmr.sharedMaterial = moteM; mmr.shadowCastingMode = ShadowCastingMode.Off; mmr.receiveShadows = false;
        }

        static Vector3 RandomMote(System.Random r) =>
            Quaternion.Euler(0, (float)r.NextDouble() * 360f, 0) * Vector3.forward * (4f + (float)r.NextDouble() * 22f) + Vector3.up * (0.5f + (float)r.NextDouble() * 7f);

        static Mesh FlatQuad(float hx, float hz)
        {
            var m = new Mesh { name = "FlatQuad" };
            m.vertices = new[] { new Vector3(-hx, 0, -hz), new Vector3(-hx, 0, hz), new Vector3(hx, 0, -hz), new Vector3(hx, 0, hz) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0), new Vector2(1, 1) };
            m.triangles = new[] { 0, 1, 2, 1, 3, 2 }; m.RecalculateBounds(); return m;
        }

        float moteSeed;
        void UpdateAmbience(bool haste, bool paused)
        {
            float dt = paused ? 0f : Time.deltaTime;
            gearSpeed = Mathf.MoveTowards(gearSpeed, haste ? 1f : 0.5f, Time.deltaTime);   // Haste turns the sky's gears twice as fast
            foreach (var (t, speed) in gears) if (t) t.Rotate(0, 0, 3.2f * speed * gearSpeed * dt, Space.Self);
            if (gears.Count > 0) GearAngle += 3.2f * gears[0].speed * gearSpeed * dt;
            float tt = Time.time;
            foreach (var (t, home, phase, spin) in drifters)
                if (t) { t.localPosition = home + new Vector3(0, Mathf.Sin(tt * 0.35f + phase) * 0.45f, Mathf.Cos(tt * 0.2f + phase) * 0.3f); t.Rotate(spin * dt, Space.Self); }
            for (int i = 0; i < cloudBanks.Count; i++)
            {
                var c = cloudBanks[i]; if (!c.t) continue;
                c.angle += c.speed * dt * (haste ? 1.4f : 1f); cloudBanks[i] = c;
                c.t.localPosition = Quaternion.Euler(0, c.angle, 0) * Vector3.forward * c.radius + Vector3.up * c.y;
                c.t.localRotation = Quaternion.Euler(0, c.angle, 0);
            }
            if (moteMesh == null) return;
            var cam = Camera.main; if (!cam) return;
            Vector3 right = cam.transform.right, up = cam.transform.up;
            float density = haste ? 1f : 0.35f;   // Haste: all motes, Normal: about a third
            var dust = new Color(0.8f, 0.72f, 0.55f);   // desaturated grey-gold: teal and amber belong to the tempo states and warnings
            for (int i = 0; i < Motes; i++)
            {
                motePos[i] += moteVel[i] * dt * (haste ? 1.5f : 1f);
                if (motePos[i].y > 9f || Sim.Flat(motePos[i]).magnitude > 27f) { moteSeed += 1.37f; var rr = new System.Random(i * 7919 + (int)(moteSeed * 100)); motePos[i] = RandomMote(rr); motePos[i].y = 0.3f; }
                bool shown = i < Motes * density;
                float fade = Mathf.Clamp01(motePos[i].y / 1.5f) * Mathf.Clamp01((9f - motePos[i].y) / 2f);
                float twinkle = 0.6f + 0.4f * Mathf.Sin(tt * 2.3f + i);
                float size = 0.05f + 0.05f * ((i * 37) % 10) / 10f;
                var col = dust * (shown ? 0.3f * fade * twinkle * (quiet ? 0.5f : 1f) : 0f);
                var p0 = motePos[i]; var rx = right * size; var uy = up * size;
                int b = i * 4;
                moteVerts[b] = p0 - rx - uy; moteVerts[b + 1] = p0 - rx + uy; moteVerts[b + 2] = p0 + rx - uy; moteVerts[b + 3] = p0 + rx + uy;
                moteCols[b] = moteCols[b + 1] = moteCols[b + 2] = moteCols[b + 3] = col;
            }
            moteMesh.vertices = moteVerts; moteMesh.colors = moteCols;
        }

        // a warm pool of light on the floor under a far-side lantern: one additive soft disc, not a real light
        void LanternPool(Transform root, Vector3 at)
        {
            if (!poolMat)
            {
                var src = Resources.Load<Material>("Run/FX_ColorDissolve");
                poolMat = src ? new Material(src) : new Material(Shader.Find("LastBell/FX/ColorDissolve"));
                poolMat.name = "MetronomeCourt_LanternPool";
                poolMat.SetFloat("_SrcBlend", 1); poolMat.SetFloat("_DstBlend", 1);   // additive
                poolMat.SetFloat("_Mode", 1); poolMat.SetColor("_ColorLow", Color.black); poolMat.SetColor(PColorHigh, HasteTint);
                poolMat.SetFloat("_Dissolve", 0); poolMat.SetFloat("_Soft", 1f); poolMat.SetVector("_NoiseScale", Vector4.zero);
                poolMat.SetVector("_Scroll", Vector4.zero); poolMat.SetFloat("_Intensity", 0.35f);
                poolMat.renderQueue = 2990;   // under the telegraphs
            }
            var inward = -Sim.Flat(at).normalized;
            var go = new GameObject("LanternPool"); go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(at.x, 0.02f, at.z) + inward * 1.6f;
            var mesh = new Mesh { name = "PoolQuad" };
            const float h = 0.9f;   // half-extent: a 1.8 m pool
            mesh.vertices = new[] { new Vector3(-h, 0, -h), new Vector3(-h, 0, h), new Vector3(h, 0, -h), new Vector3(h, 0, h) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0), new Vector2(1, 1) };
            mesh.triangles = new[] { 0, 1, 2, 1, 3, 2 }; mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = poolMat;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            pools.Add((r, at.x));
        }

        void Post(Transform stat, Transform root, Vector3 at, float yaw)
        {
            if (CameraSide(at))
            {
                // pass 6 (Risxhb: the bollard "looks basic"): a moulded post. A chamfered dark plinth and an ivory base moulding,
                // a tapered shaft with a sunk panel and brass boss on its court and outer faces, a brass band that hugs the
                // taper (the flat plate read as a thin line), a flared capital and abacus, and a chamfered brass cap under the
                // lantern. Same height as before (under 0.8 m to the lantern): the near side stays low.
                var by = Quaternion.Euler(0, yaw, 0); var agedB = M(Brass * 0.6f, 0.4f, 0.8f); var panelM = StoneM(StoneDark * 0.85f);
                Mesh(stat, Frustum(0.29f, 0.29f, 0.26f, 0.26f, 0.07f), StoneM(StoneDark), "BollardBase", at).localRotation = by;
                Mesh(stat, Frustum(0.25f, 0.25f, 0.2f, 0.2f, 0.06f), StoneM(Ivory * 0.92f), "BollardBaseMould", at + Vector3.up * 0.07f).localRotation = by;
                Mesh(stat, Frustum(0.2f, 0.2f, 0.16f, 0.16f, 0.5f), StoneM(Ivory), "BollardShaft", at + Vector3.up * 0.13f).localRotation = by;
                foreach (var face in new[] { -1f, 1f })
                {
                    var fd = by * Vector3.forward * face;   // court (-1) and outer (+1) faces
                    P(PrimitiveKind.Cube, stat, at + Vector3.up * 0.36f + fd * 0.178f, new Vector3(0.2f, 0.27f, 0.02f), panelM, new Vector3(-4.6f * face, yaw, 0), "BollardPanel");
                    P(PrimitiveKind.Cube, stat, at + Vector3.up * 0.36f + fd * 0.19f, new Vector3(0.07f, 0.07f, 0.025f), agedB, new Vector3(-4.6f * face, yaw, 45f), "BollardBoss");
                }
                Mesh(stat, Frustum(0.2f, 0.2f, 0.195f, 0.195f, 0.05f), agedB, "BollardCollar", at + Vector3.up * 0.53f).localRotation = by;
                Mesh(stat, Frustum(0.16f, 0.16f, 0.21f, 0.21f, 0.06f), StoneM(Ivory), "BollardCapital", at + Vector3.up * 0.63f).localRotation = by;
                Mesh(stat, Frustum(0.22f, 0.22f, 0.22f, 0.22f, 0.035f), StoneM(Ivory * 0.92f), "BollardAbacus", at + Vector3.up * 0.69f).localRotation = by;
                Mesh(stat, Frustum(0.2f, 0.2f, 0.15f, 0.15f, 0.05f), agedB, "BollardCap", at + Vector3.up * 0.725f).localRotation = by;
                Lantern(root, at + Vector3.up * 0.96f, 0.75f);   // the lantern base sits on the bollard cap
                return;
            }
            P(PrimitiveKind.Cube, stat, at + Vector3.up * 0.3f, new Vector3(1.15f, 0.6f, 1.15f), StoneM(StoneDark), new Vector3(0, yaw, 0), "PostPlinth");
            P(PrimitiveKind.Cube, stat, at + Vector3.up * 1.9f, new Vector3(0.75f, 2.6f, 0.75f), StoneM(Ivory), new Vector3(0, yaw, 0), "PostShaft");
            var agedBrass = M(Brass * 0.6f, 0.4f, 0.8f);   // pass 4 (critic: "yellow-lit caps", the hottest value near warnings)
            P(PrimitiveKind.Cylinder, stat, at + Vector3.up * 0.75f, new Vector3(0.95f, 0.05f, 0.95f), agedBrass, name: "PostCollar");
            P(PrimitiveKind.Cylinder, stat, at + Vector3.up * 2.9f, new Vector3(0.95f, 0.05f, 0.95f), agedBrass, name: "PostCollar");
            P(PrimitiveKind.Cube, stat, at + Vector3.up * 3.3f, new Vector3(1.0f, 0.2f, 1.0f), agedBrass, new Vector3(0, yaw, 0), "PostCornice");
            P(PrimitiveKind.Cone, stat, at + Vector3.up * 4.5f, new Vector3(0.55f, 1.2f, 0.55f), IronM, name: "PostSpire");
            // banner faces the court (and so the camera), hung from a brass rod
            var inward = -Sim.Flat(at).normalized; float faceYaw = Mathf.Atan2(inward.x, inward.z) * Mathf.Rad2Deg;
            var cloth = M(BannerTeal, 0.3f); var tilt = new Vector3(3f, faceYaw, 0);
            var side = Quaternion.Euler(0, faceYaw, 0) * Vector3.right;
            // pass 4 (critic: "every banner is the same length"): three lengths round the court, all hung from the same rod
            float bannerLen = new[] { 1.6f, 1.25f, 1.9f }[Mathf.Abs(Mathf.RoundToInt(yaw / 10f)) % 3];
            P(PrimitiveKind.Cube, stat, at + inward * 0.42f + Vector3.up * (2.85f - bannerLen / 2), new Vector3(0.5f, bannerLen, 0.03f), cloth, tilt, "Banner");
            // swallowtail: two flaps hanging from the body's lower edge, splayed outward so a V-notch opens between them
            foreach (var sx in new[] { -1, 1 })
                P(PrimitiveKind.Cube, stat, at + inward * 0.415f + side * (0.13f * sx) + Vector3.up * (2.85f - bannerLen - 0.18f), new Vector3(0.24f, 0.36f, 0.03f), cloth, new Vector3(3f, faceYaw, sx * 14f), "BannerTail");
            P(PrimitiveKind.Cube, stat, at + inward * 0.44f + Vector3.up * 2.87f, new Vector3(0.78f, 0.05f, 0.05f), BrassM, new Vector3(0, faceYaw, 0), "BannerRod");
            P(PrimitiveKind.Cube, stat, at + inward * 0.455f + Vector3.up * 2.15f, new Vector3(0.22f, 0.22f, 0.02f), BrassM, new Vector3(3f, faceYaw, 45f), "BannerEmblem");
            Lantern(root, at + Vector3.up * 3.75f, 1f, pool: true);
        }

        void BuildMetronome(Transform m)
        {
            // October 27: stiles 1.3 m wide (were 0.9) so the frame holds its silhouette at thumbnail size
            // dark iron back case (trapezoid), tapered open front frame with brass edge bevels and tempo-lit seams
            Frame = new GameObject("Frame").transform; Frame.SetParent(m, false);
            float baseHalf = 3.4f, topHalf = 0.6f, h = FrameHeight;
            Mesh(Frame, Trapezoid(baseHalf - 0.2f, topHalf - 0.05f, h - 0.1f, 0.8f), M(new Color(0.10f, 0.11f, 0.13f), 0.15f, 0f), "BackCase", new Vector3(0, 0.1f, -0.15f));
            Mesh(Frame, Trapezoid(baseHalf - 0.75f, topHalf * 0.4f, h - 1.2f, 0.05f), M(new Color(0.07f, 0.075f, 0.09f), 0.1f, 0f), "CaseRecess", new Vector3(0, 0.5f, 0.15f));
            for (int s = -1; s <= 1; s += 2)
            {
                var a = new Vector3(baseHalf * s, 0, 0.35f); var b = new Vector3(topHalf * s, h, 0.35f);
                var rot = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
                var stile = P(PrimitiveKind.Cube, Frame, (a + b) / 2, new Vector3(1.6f, (b - a).magnitude, 0.8f), IronM, name: "Stile"); stile.localRotation = rot;
                var trim = P(PrimitiveKind.Cube, Frame, (a + b) / 2 + new Vector3(-0.64f * s, 0, 0.41f), new Vector3(0.3f, (b - a).magnitude, 0.05f), M(Brass * 0.7f, 0.4f, 0.8f), name: "StileBevel"); trim.localRotation = rot;   // pass 4: aged, low-gloss brass (critic: an orange bloom inside the case)
                // tempo seam: the middle 60% of the stile only, so it accents rather than out-glowing everything
                var seam = P(PrimitiveKind.Cube, Frame, (a + b) / 2 + new Vector3(0.16f * s, 0, 0.42f), new Vector3(0.1f, (b - a).magnitude * 0.6f, 0.02f), M(HasteTint, 0.2f, 0, true), name: "StileSeam"); seam.localRotation = rot;   // pass 4: a broader, dimmer seam (critic: thin emissive lines)
                seams.Add(seam.GetComponent<Renderer>());
                // rivet row down each stile face, and a triangular gusset at each base corner
                for (int k = 0; k < 10; k++)
                {
                    var rp = Vector3.Lerp(a, b, (k + 0.5f) / 10f) + new Vector3(-0.42f * s, 0, 0.41f);
                    P(PrimitiveKind.Cube, Frame, rp, new Vector3(0.08f, 0.08f, 0.05f), M(Brass * 0.85f, 0.5f, 0.8f), new Vector3(0, 0, 45), "Rivet");
                }
                Mesh(Frame, Frustum(0.7f, 0.15f, 0.03f, 0.15f, 1.1f), IronM, "Gusset", new Vector3((baseHalf - 1.0f) * s, 0.6f, 0.5f));
            }
            // frenzy cracks: 7 branching cracks that start at the stile/brace joints and the base corners and run into the
            // case, plus dark chips knocked out of the bevel (all hidden until Broken Cadence)
            var crng = new System.Random(71);
            var starts = new List<Vector2>();
            foreach (var by in new[] { h / 3f, 2f * h / 3f }) { float bh = Mathf.Lerp(baseHalf, topHalf, by / h) - 0.45f; starts.Add(new Vector2(-bh, by)); starts.Add(new Vector2(bh, by)); }
            starts.Add(new Vector2(-(baseHalf - 1.1f), 2.4f)); starts.Add(new Vector2(baseHalf - 1.1f, 2.4f)); starts.Add(new Vector2(0, h - 1.4f));
            var crackM = M(CrackGlow, 0.2f, 0, true);
            void CrackSeg(Vector2 p0, Vector2 p1)
            {
                var d = p1 - p0;
                var c = P(PrimitiveKind.Cube, Frame, new Vector3((p0.x + p1.x) / 2, (p0.y + p1.y) / 2, 0.52f), new Vector3(0.07f, d.magnitude + 0.04f, 0.02f), crackM,
                    new Vector3(0, 0, -Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg), "FrenzyCrack");
                cracks.Add(c.GetComponent<Renderer>());
            }
            foreach (var st in starts)
            {
                var pt = st; var inward = new Vector2(-Mathf.Sign(st.x == 0 ? 1 : st.x), st.x == 0 ? -1 : 0).normalized;
                if (st.x == 0) inward = Vector2.down;
                for (int k = 0; k < 3; k++)
                {
                    float jit = ((float)crng.NextDouble() - 0.5f) * 1.2f;
                    var next = pt + (inward * 0.55f + new Vector2(0, 0.25f) + new Vector2(-inward.y, inward.x) * jit) * (0.9f - 0.15f * k);
                    CrackSeg(pt, next);
                    if (k == 1) CrackSeg(next, next + new Vector2(-inward.y, inward.x) * 0.35f * (jit > 0 ? 1 : -1) + new Vector2(0, -0.2f));   // branch
                    pt = next;
                }
            }
            for (int k = 0; k < 3; k++)
            {
                float t = 0.25f + 0.25f * k; int sx = k % 2 == 0 ? -1 : 1;
                var cp = Vector3.Lerp(new Vector3(baseHalf * sx, 0, 0.35f), new Vector3(topHalf * sx, h, 0.35f), t) + new Vector3(-0.64f * sx, 0, 0.39f);   // recessed into the bevel face
                var chip = P(PrimitiveKind.Cube, Frame, cp, new Vector3(0.25f, 0.3f, 0.03f), M(new Color(0.04f, 0.04f, 0.05f), 0.1f), new Vector3(0, 0, 30 * sx), "BevelChip");
                chips.Add(chip.GetComponent<Renderer>()); chip.GetComponent<Renderer>().enabled = false;
            }
            // two braces (not three) and a brass scale plate on the centreline behind the rod, 18 ticks
            foreach (var y in new[] { h * 0.22f, h * 0.9f })   // braces clear of the scale plate (2.6-8.6 m), so no cross reads
            {
                float half = Mathf.Lerp(baseHalf, topHalf, y / h);
                P(PrimitiveKind.Cube, Frame, new Vector3(0, y, 0.3f), new Vector3(half * 2, 0.2f, 0.3f), M(Brass * 0.55f, 0.4f, 0.8f), name: "Brace");   // pass 3: was the hot bar behind the escapement
            }
            P(PrimitiveKind.Cube, Frame, new Vector3(0, h + 0.3f, 0), new Vector3(1.7f, 0.6f, 1.2f), BrassM, name: "ApexCap");
            // October 27 pass 2: a stepped iron crown and a taller finial give the top a silhouette at thumbnail size
            Mesh(Frame, Frustum(1.15f, 0.8f, 0.45f, 0.35f, 1.3f), IronM, "ApexCrown", new Vector3(0, h + 0.6f, 0));
            P(PrimitiveKind.Cylinder, Frame, new Vector3(0, h + 1.95f, 0), new Vector3(0.75f, 0.06f, 0.75f), M(Brass * 0.7f, 0.5f, 0.8f), name: "ApexCollar");
            P(PrimitiveKind.Cone, Frame, new Vector3(0, h + 2.9f, 0), new Vector3(0.5f, 1.9f, 0.5f), IronM, name: "ApexFinial");
            P(PrimitiveKind.Cube, Frame, new Vector3(0, 0.3f, 0), new Vector3(baseHalf * 2 + 0.8f, 0.6f, 1.8f), IronM, name: "FrameBase");
            P(PrimitiveKind.Cube, Frame, new Vector3(0, 0.62f, 0.2f), new Vector3(baseHalf * 2 + 0.9f, 0.08f, 1.9f), M(Brass * 0.55f, 0.4f, 0.8f), name: "FrameBaseTrim");   // October 27: was a hot strip in Haste
            P(PrimitiveKind.Cube, Frame, new Vector3(0, 5.6f, 0.2f), new Vector3(0.35f, 6.0f, 0.03f), M(Brass * 0.8f, 0.5f, 0.4f), name: "ScalePlate");
            for (int k = 0; k < 18; k++) P(PrimitiveKind.Cube, Frame, new Vector3(0, 2.75f + k * (5.7f / 17f), 0.225f), new Vector3(k % 3 == 0 ? 0.3f : 0.17f, 0.04f, 0.02f), IronM, name: "ScaleMark");
            // escapement wheel (2.6 m, spokes and teeth) at the lower pivot; steps on each tick, with a pawl that clicks
            Escapement = new GameObject("EscapementWheel").transform; Escapement.SetParent(m, false); Escapement.localPosition = new Vector3(0, 1.6f, 0.45f);
            P(PrimitiveKind.Cylinder, Escapement, Vector3.zero, new Vector3(2.6f, 0.06f, 2.6f), BrassM, new Vector3(90, 0, 0), "WheelRim");
            P(PrimitiveKind.Cylinder, Escapement, new Vector3(0, 0, 0.02f), new Vector3(2.2f, 0.07f, 2.2f), IronM, new Vector3(90, 0, 0), "WheelWeb");
            for (int k = 0; k < 6; k++) P(PrimitiveKind.Cube, Escapement, new Vector3(0, 0, 0.06f), new Vector3(0.12f, 2.2f, 0.06f), BrassM, new Vector3(0, 0, k * 30), "Spoke");
            for (int k = 0; k < 15; k++) P(PrimitiveKind.Cube, Escapement, Quaternion.Euler(0, 0, k * 24) * Vector3.up * 1.36f, new Vector3(0.16f, 0.22f, 0.08f), BrassM, new Vector3(0, 0, k * 24 + 12), "Tooth");
            Pawl = new GameObject("Pawl").transform; Pawl.SetParent(m, false); Pawl.localPosition = new Vector3(1.7f, 3.5f, 0.5f);
            P(PrimitiveKind.Cube, Pawl, new Vector3(-0.39f, -0.39f, 0), new Vector3(0.22f, 1.15f, 0.12f), IronM, new Vector3(0, 0, -45), "PawlArm");   // runs from the boss to the tooth circle
            P(PrimitiveKind.Cylinder, Pawl, Vector3.zero, new Vector3(0.45f, 0.08f, 0.45f), BrassM, new Vector3(90, 0, 0), "PawlBoss");
            P(PrimitiveKind.Cube, m, new Vector3(2.2f, 3.5f, 0.45f), new Vector3(1.0f, 0.16f, 0.1f), BrassM, name: "PawlBracket");
            // pivot, ONE rod and its sliding weight (separate objects for animation)
            var pivot = new GameObject("Pivot").transform; pivot.SetParent(m, false); pivot.localPosition = new Vector3(0, 1.6f, 1.15f);   // in front of the stile faces (z 0.8): no clipping at 26 degrees
            P(PrimitiveKind.Cylinder, pivot, Vector3.zero, new Vector3(0.6f, 0.25f, 0.6f), BrassM, new Vector3(90, 0, 0), "Axle");
            Rod = new GameObject("Rod").transform; Rod.SetParent(pivot, false);
            P(PrimitiveKind.Cube, Rod, new Vector3(0, RodLength / 2, 0), new Vector3(0.3f, RodLength, 0.2f), M(new Color(0.75f, 0.58f, 0.28f), 0.55f, 0.8f), name: "RodBar");
            P(PrimitiveKind.Cylinder, Rod, new Vector3(0, 0.45f, 0), new Vector3(0.42f, 0.35f, 0.42f), BrassM, name: "RodFerrule");
            P(PrimitiveKind.Cone, Rod, new Vector3(0, RodLength + 0.3f, 0), new Vector3(0.36f, 0.6f, 0.36f), BrassM, name: "RodTip");
            Weight = new GameObject("SlidingWeight").transform; Weight.SetParent(Rod, false); Weight.localPosition = new Vector3(0, WeightHaste, 0);
            P(PrimitiveKind.Cube, Weight, Vector3.zero, new Vector3(0.8f, 1.2f, 0.5f), BrassM, name: "WeightBody");
            Mesh(Weight, Frustum(0.4f, 0.25f, 0.24f, 0.25f, 0.2f), BrassM, "WeightBevelTop", new Vector3(0, 0.6f, 0));
            Mesh(Weight, Frustum(0.4f, 0.25f, 0.24f, 0.25f, 0.2f), BrassM, "WeightBevelBottom", new Vector3(0, -0.6f, 0)).localRotation = Quaternion.Euler(180, 0, 0);
            P(PrimitiveKind.Cube, Weight, new Vector3(0, 0, 0.26f), new Vector3(0.6f, 0.98f, 0.04f), IronM, name: "WeightFace");
            var slot = P(PrimitiveKind.Cube, Weight, new Vector3(0, 0, 0.29f), new Vector3(0.26f, 0.26f, 0.02f), M(HasteTint, 0.2f, 0, true), new Vector3(0, 0, 45), "WeightSlot");
            seams.Add(slot.GetComponent<Renderer>());
            var go = new GameObject("Metronome light"); go.transform.SetParent(m, false); go.transform.localPosition = new Vector3(0, 6, 6.5f);   // pass 4: forward of the case, so the brass legs are lit, not glowing
            metronomeLight = go.AddComponent<Light>(); metronomeLight.type = LightType.Point; metronomeLight.range = 18; metronomeLight.intensity = 6; metronomeLight.shadows = LightShadows.None;
            // tempo spot light: above and just behind the apex, aimed across the court, washing the dial in the tempo
            // colour. It casts no shadows, so the metronome leaves no shadow on the arena floor.
            var sl = new GameObject("Tempo spot light"); sl.transform.SetParent(m, false); sl.transform.localPosition = new Vector3(0, 20f, -2.0f);   // high enough that the plinth top doesn't blow out
            sl.transform.rotation = Quaternion.LookRotation(Vector3.zero - sl.transform.position);   // aimed at the court centre
            TempoSpotLight = sl.AddComponent<Light>(); TempoSpotLight.type = LightType.Spot; TempoSpotLight.spotAngle = 85; TempoSpotLight.innerSpotAngle = 50;   // wide: the whole court reads the tempo
            TempoSpotLight.range = 48; TempoSpotLight.intensity = 140; TempoSpotLight.shadows = LightShadows.None;
        }

        void Update()
        {
            var d = RunDirector.Instance;
            // the metronome follows whichever tempo boss is fighting here (Tempo Tyrant or Chimebound Oracle)
            var anyBoss = d != null ? d.Boss : null;
            var tyrant = anyBoss as TempoTyrantEncounter; var oracle = anyBoss as ChimeboundOracleEncounter;
            TempoClock clock = tyrant ? tyrant.Clock : oracle ? oracle.Clock : null;
            BossEncounter boss = clock != null ? anyBoss : null;
            bool fight = boss && !boss.Dead && d.State == RunState.Combat;
            float phase; bool haste; bool frenzy = boss && !boss.Dead && (tyrant ? tyrant.Frenzy : oracle.Enraged); bool accent = false; int beat; double beatPos; float beatSec;
            if (fight)
            {
                Preview = false;
                phase = clock.RodPhase; haste = clock.State == Tempo.Haste;
                beatPos = clock.Beat; beatSec = clock.BeatSeconds;
                beat = (int)System.Math.Floor(clock.Beat); accent = clock.Transitioning;
            }
            else
            {
                // readiness / menu preview: non-damaging ticking at the opening tempo, frozen while paused
                // (the Tyrant opens in Haste at 220 BPM; the Oracle opens in Normal at 140 BPM)
                bool oracleNext = d != null && d.Encounter == RunDirector.ChimeboundOraclePractice;
                float previewBpm = oracleNext ? ChimeboundOracleEncounter.NormalBpm : 220f;
                Preview = true; haste = ReviewHaste ?? !oracleNext;
                if (d == null || !d.Paused) previewBeat += Time.deltaTime * previewBpm / 60.0;
                phase = Mathf.Cos((float)(previewBeat % 2.0) * Mathf.PI);
                beat = (int)System.Math.Floor(previewBeat); beatPos = previewBeat; beatSec = 60f / previewBpm;
                if (boss && boss.Dead) phase = 0;
            }
            if (beat != lastBeat) { lastBeat = beat; escapement -= 24; pawlKick = 1; if (accent) flash = 1; }
            RodAngle = phase * SwingDegrees;
            if (Rod) Rod.localRotation = Quaternion.Euler(0, 0, RodAngle);
            escShown = Mathf.MoveTowards(escShown, escapement, Time.deltaTime * 600);
            if (Escapement) Escapement.localRotation = Quaternion.Euler(0, 0, escShown);
            pawlKick = Mathf.MoveTowards(pawlKick, 0, Time.deltaTime * 8);
            if (Pawl) Pawl.localRotation = Quaternion.Euler(0, 0, 14 * pawlKick);
            flash = Mathf.MoveTowards(flash, 0, Time.deltaTime * 3);
            // the sliding weight eases to its tempo position over about the three transition ticks (frozen while paused)
            bool wHaste = fight && clock.Transitioning ? clock.Next == Tempo.Haste : haste;   // starts sliding on the accented ticks
            float wTarget = wHaste ? WeightHaste : WeightNormal;
            if (d == null || !d.Paused) WeightHeight = Mathf.MoveTowards(WeightHeight, wTarget, Time.deltaTime * 3.0f * (0.35f + Mathf.Abs(WeightHeight - wTarget) / 3f));
            if (Weight) Weight.localPosition = new Vector3(0, WeightHeight, 0);

            var tint = haste ? HasteTint : NormalTint;
            // beat pulse: a 0.03 s attack and a 0.12 s decay after every beat. Nervous flicker at 220 BPM, slow breath at
            // 165 BPM, so the tempo reads from motion as well as colour, anywhere on the court. Frozen while paused.
            float sinceBeat = (float)(beatPos - System.Math.Floor(beatPos)) * beatSec;
            BeatPulse = sinceBeat < 0.03f ? sinceBeat / 0.03f : Mathf.Exp(-(sinceBeat - 0.03f) / 0.12f);
            if (boss && boss.Dead) BeatPulse = 0;
            LightTint = Color.Lerp(NormalLightTint, HasteLightTint, tempoBlend);
            float flicker = frenzy ? 0.75f + 0.25f * Mathf.PerlinNoise(Time.time * 9, 0.3f) : 1f;
            // Floor light is steady (Risxhb, October 26, 2026): the per-beat pulse and the enrage noise on the lights that
            // cover the floor read as heavy shimmer (measured: whole-floor flicker 4.9% live vs 2.3% with them off). The
            // tempo still reads from the light colour, the rod and the metronome's own glow, which keeps a soft flash.
            if (metronomeLight) { metronomeLight.color = frenzy ? Color.Lerp(tint, CrackGlow, 0.35f) : tint; metronomeLight.intensity = (haste ? 3.6f : 2.6f) * (1 + 0.25f * flash); }
            if (TempoSpotLight) { TempoSpotLight.color = LightTint; TempoSpotLight.intensity = Mathf.Lerp(38f, 48f, tempoBlend) * (quiet ? 0.6f : 1f); }   // pass 4: Normal 38 (was 18) with the paler tint, Haste 48 (was 60): the floor keeps its brightness and stone colour in both tempos   // October 27: softer wash (was 80-95): the floor read warm brown in Haste, slate in Normal
            if (TempoRimLight) { TempoRimLight.color = LightTint; TempoRimLight.intensity = Mathf.Lerp(0.16f, 0.38f, tempoBlend) * (quiet ? 0.75f : 1f); }   // pass 4: 0.16 / 0.38 (were 0.22 / 0.45), so neither tempo recolours the stone
            _ = flicker;   // the enrage noise now shows on the cracks and the boss only
            if (mpb == null) mpb = new MaterialPropertyBlock();   // survives a script reload
            // lantern glow follows the rod's lean across the rim; all flash together on accented transition ticks
            LanternPulseX = phase * 14f;
            foreach (var (r, x) in lanterns)
            {
                if (!r) continue;
                float near = Mathf.Exp(-(x - LanternPulseX) * (x - LanternPulseX) / 6f);
                float k = (quiet ? 0.7f : 1f) * ((haste ? 1.3f : 1.0f) * (0.8f + 2.2f * near) * (1 + 0.3f * BeatPulse) + 2.5f * flash);
                // soft-capped (never clips to white) with the flicker, the hazard dim and the tick flash applied after the cap
                float raw = (haste ? 1.3f : 1.0f) * (0.8f + 2.2f * near) * (1 + 0.3f * BeatPulse);
                float e = 1.15f * Mathf.Clamp01(raw / 3.5f) * (0.92f + 0.16f * Mathf.PerlinNoise(x * 1.7f, Time.time * 3f));
                e = e * (quiet ? 0.6f : 1f) + 0.9f * flash;   // pass 3: lantern glass no longer white-hot
                mpb.SetColor(PBase, tint * 0.5f); mpb.SetColor(PEmission, tint * e); r.SetPropertyBlock(mpb);
            }
            if (poolMpb == null) poolMpb = new MaterialPropertyBlock();
            foreach (var (r, x) in pools)
            {
                if (!r) continue;
                float near = Mathf.Exp(-(x - LanternPulseX) * (x - LanternPulseX) / 6f);
                float k = (quiet ? 0.6f : 1f) * (0.7f + 0.6f * near) * (1 + 0.06f * BeatPulse + 0.25f * flash);   // gentle: floor glow, not a strobe
                poolMpb.SetColor(PColorHigh, LightTint * k); r.SetPropertyBlock(poolMpb);
            }
            var seamTint = frenzy && Mathf.PerlinNoise(Time.time * 7, 1.7f) > 0.55f ? CrackGlow : tint;
            mpb.SetColor(PBase, seamTint); mpb.SetColor(PEmission, seamTint * ((haste ? 0.6f : 0.45f) + 0.3f * flash));
            foreach (var r in seams) if (r) r.SetPropertyBlock(mpb);
            // tempo line: brighter than the brass beside it, with a bright ~30-degree segment chasing once around per beat
            // the bright segment ticks one hour per beat (a clock hand, a lap every 12 beats) and decays over a quarter beat;
            // while hazards are up the line drops to a dim base with no chase or flash, so warnings stay the brightest thing
            float hourA = 180f + 30f * (float)(System.Math.Floor(beatPos) % 12), beatFrac = (float)(beatPos - System.Math.Floor(beatPos));
            float tick = Mathf.Clamp01(1f - beatFrac / 0.25f) * (boss && boss.Dead ? 0 : 1);
            // October 27: dimmer tempo ring (was 1.4/1.0, chase 1.6): it sat on warning edges as a bright orange band
            float lineBase = quiet ? 0.2f : (haste ? 0.45f : 0.4f) + 0.5f * flash;
            for (int i = 0; i < tempoLineRs.Count; i++)
            {
                var r = tempoLineRs[i]; if (!r) continue;
                float segA = (i + 0.5f) * 360f / tempoLineRs.Count, dA = Mathf.Abs(Mathf.DeltaAngle(segA, hourA));
                float chase = quiet ? 0 : (dA < 15f ? 1f - dA / 15f : 0f) * tick;
                var ringTint = haste ? HasteLightTint : NormalTint;   // pale gold, kept apart from the red-orange warnings
                mpb.SetColor(PBase, ringTint * 0.55f); mpb.SetColor(PEmission, ringTint * Mathf.Min(1.4f, lineBase + 0.8f * chase));
                r.SetPropertyBlock(mpb);
            }
            if (probe && !probeRendered) { probe.RenderProbe(); probeRendered = true; }
            var cam = Camera.main;   // the scene camera clears to a solid colour; show the dusk sky instead
            if (cam && SkyActive && cam.clearFlags != CameraClearFlags.Skybox) cam.clearFlags = CameraClearFlags.Skybox;
            UpdateAmbience(haste, d != null && d.Paused);
            UpdateWorldLighting(haste, frenzy);
            ShowingCracks = frenzy;
            mpb.SetColor(PBase, CrackGlow); mpb.SetColor(PEmission, CrackGlow * (frenzy ? 1.6f * flicker : 0f));
            foreach (var r in cracks) if (r) { r.enabled = frenzy; r.SetPropertyBlock(mpb); }
            foreach (var r in chips) if (r) r.enabled = frenzy;
        }
    }
}
