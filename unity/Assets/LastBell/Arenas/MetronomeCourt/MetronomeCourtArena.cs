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
    // reaches into the frame through (a) a tempo-tinted spot light behind its apex whose rod shadow sweeps across the
    // dial on every beat and (b) rim lanterns whose glow follows the rod's lean, flashing together on transition ticks.
    public sealed class MetronomeCourtArena : MonoBehaviour
    {
        public bool quiet;   // HazardSystem dims decoration while hazards are up (shared arena convention)

        public const float FloorRadius = 13.1f, DialInner = 12.4f, ParapetRadius = 13.5f, PostRadius = 14.4f, MetronomeZ = -19.5f, FrameHeight = 10f;
        static readonly Color Stone = new Color(0.52f, 0.53f, 0.55f), StoneWarm = new Color(0.56f, 0.54f, 0.51f), Grout = new Color(0.34f, 0.33f, 0.32f), StoneDark = new Color(0.27f, 0.26f, 0.27f),
            Ivory = new Color(0.78f, 0.74f, 0.65f), Brass = new Color(0.70f, 0.50f, 0.22f), BrassInlay = new Color(0.62f, 0.50f, 0.30f),
            Iron = new Color(0.11f, 0.11f, 0.13f), DialBand = new Color(0.19f, 0.19f, 0.21f), Teal = new Color(0.08f, 0.32f, 0.33f),
            Abyss = new Color(0.05f, 0.07f, 0.10f), CrackGlow = new Color(1.4f, 0.25f, 0.1f);
        public static readonly Color HasteTint = new Color(1f, 0.58f, 0.22f), NormalTint = new Color(0.28f, 0.92f, 0.82f);

        public Transform Rod { get; private set; }
        public Transform Weight { get; private set; }
        public Transform Escapement { get; private set; }
        public Transform Pawl { get; private set; }
        public Transform Frame { get; private set; }
        public Light RodShadowLight { get; private set; }
        public float RodAngle { get; private set; }
        public bool ShowingCracks { get; private set; }
        public bool Preview { get; private set; }
        public int LanternCount => lanterns.Count;
        public float LanternPulseX { get; private set; }
        public const float SwingDegrees = 26f, RodLength = 10.5f;   // the tip overshoots the apex by ~1.5 m, as in the reference

        readonly List<(Renderer r, float x)> lanterns = new List<(Renderer, float)>();
        Mesh shadowMesh; MeshRenderer shadowR; static Material shadowMat;
        public bool RodShadowOnFloor { get; private set; }
        public Vector3 RodShadowTip { get; private set; }
        Light keyLight; ColorAdjustments grade; float tempoBlend = 1;   // 1 = Haste look, 0 = Normal look
        Color baseEquator;
        public float TempoBlend => tempoBlend;
        readonly List<Renderer> cracks = new List<Renderer>(), seams = new List<Renderer>(), tempoLineRs = new List<Renderer>();
        Light metronomeLight; MaterialPropertyBlock mpb;
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
            return mats[key] = m;
        }
        static Material StoneM(Color c) => M(c, 0.18f);
        static Material BrassM => M(Brass, 0.55f, 0.85f);
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
        static Mesh Sector(float r0, float r1, float a0, float a1, float depth, int seg = 6, float gap = 0)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d); t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3); }
            bool full = a1 - a0 >= 359.9f;
            float lo(float r) => full || r < 1e-3f ? a0 : a0 + Mathf.Rad2Deg * Mathf.Asin(Mathf.Clamp(gap / r, -1, 1));
            float hi(float r) => full || r < 1e-3f ? a1 : a1 - Mathf.Rad2Deg * Mathf.Asin(Mathf.Clamp(gap / r, -1, 1));
            float ri = r0 > 0.01f ? r0 + gap : 0, ro = r1 - gap;
            Vector3 Pt(float r, float u, float y) { float a = Mathf.Lerp(lo(r), hi(r), u) * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r); }
            for (int s = 0; s < seg; s++)
            {
                float u0 = s / (float)seg, u1 = (s + 1) / (float)seg;
                Quad(Pt(ri, u0, 0), Pt(ri, u1, 0), Pt(ro, u1, 0), Pt(ro, u0, 0));                  // top
                Quad(Pt(ro, u0, 0), Pt(ro, u1, 0), Pt(ro, u1, -depth), Pt(ro, u0, -depth));        // outer
                if (ri > 0.01f) Quad(Pt(ri, u1, 0), Pt(ri, u0, 0), Pt(ri, u0, -depth), Pt(ri, u1, -depth));   // inner
            }
            if (!full)
            {
                Quad(Pt(ri, 0, 0), Pt(ro, 0, 0), Pt(ro, 0, -depth), Pt(ri, 0, -depth));
                Quad(Pt(ro, 1, 0), Pt(ri, 1, 0), Pt(ri, 1, -depth), Pt(ro, 1, -depth));
            }
            var m = new Mesh { name = "Sector" }; m.SetVertices(v); m.SetTriangles(t, 0);
            // winding check on the second top triangle (the first is degenerate when the sector reaches the centre)
            var tris = m.triangles; var n = Vector3.Cross(v[tris[4]] - v[tris[3]], v[tris[5]] - v[tris[3]]);
            if (n.y < 0) { for (int i = 0; i < tris.Length; i += 3) { int x = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = x; } m.triangles = tris; }
            m.RecalculateNormals(); m.RecalculateBounds(); return m;
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
            P(PrimitiveKind.Cylinder, stat, new Vector3(0, -0.36f, 0), new Vector3(FloorRadius * 2, 0.34f, FloorRadius * 2), StoneM(Grout), name: "Grout");
            P(PrimitiveKind.Cylinder, stat, new Vector3(0, -3.6f, 0), new Vector3(FloorRadius * 2 + 1.6f, 3.0f, FloorRadius * 2 + 1.6f), StoneM(StoneDark), name: "PlatformDrum");
            P(PrimitiveKind.Cone, stat, new Vector3(0, -10.5f, 0), new Vector3(FloorRadius * 2 + 1.6f, 7f, FloorRadius * 2 + 1.6f), StoneM(StoneDark), new Vector3(180, 0, 0), "PlatformKeel");
            // two shade families (cool grey-blue, warm grey) with +-10% value jitter, chosen by a fixed seed
            // one calm stone family at +-4% (the reference floor is a quiet grey with thin gold lines)
            var shades = new List<Material>();
            foreach (var k in new[] { 0.96f, 0.99f, 1.01f, 1.04f }) shades.Add(StoneM(Stone * k));
            var innerShades = new List<Material>();
            foreach (var k in new[] { 0.96f, 0.99f, 1.01f, 1.04f }) innerShades.Add(StoneM(Stone * k * 0.97f));
            const float gap = 0.025f;   // half-joint: joints are a constant 0.05 m everywhere
            Mesh(stat, Sector(0, 2.6f, 0, 360, 0.3f, 48, gap), StoneM(Stone), "FloorCentre");
            var bands = new[] { (2.6f, 6.6f, 0f, 24), (6.6f, 9.8f, 0f, 48), (9.8f, DialInner, 0f, 24) };
            foreach (var (r0, r1, off, count) in bands)
                for (int i = 0; i < count; i++)
                {
                    float span = 360f / count, a0 = off + i * span;
                    var family = r0 < 3f ? innerShades : shades;
                    Mesh(stat, Sector(r0, r1, a0, a0 + span, 0.3f, 6, gap), family[rng.Next(family.Count)], "Slab");
                }
            Mesh(stat, Sector(DialInner, FloorRadius, 0, 360, 0.3f, 96), StoneM(DialBand), "DialBand");
            // 12 continuous brass hour spokes laid inside the 30-degree joints through all bands, an inner ring and the
            // dial's inner/outer lines (all tangent where they are lines)
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f; var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                P(PrimitiveKind.Cube, stat, dir * 7.5f + Vector3.up * 0.004f, new Vector3(0.12f, 0.008f, 9.8f), BrassM, new Vector3(0, a, 0), "HourSpoke");
            }
            Ring(stat, 2.6f, 0.07f, 48, M(BrassInlay, 0.5f, 0.8f), "InnerRingInlay");
            Ring(stat, DialInner, 0.08f, 120, BrassM, "DialInnerLine");
            var tempoLine = new GameObject("TempoLine").transform; tempoLine.SetParent(root, false);
            Ring(tempoLine, DialInner - 0.16f, 0.05f, 120, M(HasteTint, 0.2f, 0, true), "TempoLineSeg");
            foreach (var r in tempoLine.GetComponentsInChildren<Renderer>()) tempoLineRs.Add(r);
            Ring(stat, FloorRadius - 0.06f, 0.1f, 120, BrassM, "DialOuterLine");
            for (int i = 0; i < 60; i++)
            {
                float a = i * 6f; bool hour = i % 5 == 0; var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                if (hour)
                {
                    P(PrimitiveKind.Cube, stat, dir * 12.75f + Vector3.up * 0.01f, new Vector3(0.62f, 0.02f, 0.42f), BrassM, new Vector3(0, a, 0), "HourPlaque");
                    P(PrimitiveKind.Cube, stat, dir * 12.75f + Vector3.up * 0.025f, new Vector3(0.18f, 0.02f, 0.18f), StoneM(DialBand), new Vector3(0, a + 45, 0), "HourPip");
                }
                else P(PrimitiveKind.Cube, stat, dir * 12.8f + Vector3.up * 0.006f, new Vector3(0.05f, 0.01f, 0.3f), M(BrassInlay, 0.5f, 0.8f), new Vector3(0, a, 0), "MinuteTick");
            }

            // ---- perimeter: continuous low parapet (camera side), taller parapet + posts + lanterns (far side) ----
            for (int i = 0; i < 48; i++)
            {
                float a = i * 7.5f + 3.75f; if (Mathf.Abs(Mathf.DeltaAngle(a, 0)) < 9f) continue;   // south bridge gap (+z)
                var dir = Quaternion.Euler(0, a, 0) * Vector3.forward; var at = dir * ParapetRadius;
                float ph = CameraSide(at) ? 0.4f : 0.84f;   // camera-side parapet stays low: never hides a player
                P(PrimitiveKind.Cube, stat, at + Vector3.up * ph / 2, new Vector3(1.78f, ph, 0.42f), StoneM(Ivory), new Vector3(0, a, 0), "ParapetSegment");
                P(PrimitiveKind.Cube, stat, at + Vector3.up * (ph + 0.035f), new Vector3(1.8f, 0.07f, 0.52f), BrassM, new Vector3(0, a, 0), "ParapetCap");
                P(PrimitiveKind.Cube, stat, at + dir * 0.22f + Vector3.up * 0.1f, new Vector3(1.8f, 0.2f, 0.06f), StoneM(StoneDark), new Vector3(0, a, 0), "ParapetPlinth");
            }
            for (int i = 0; i < 16; i++)
            {
                float a = i * 22.5f;
                if (Mathf.Abs(Mathf.DeltaAngle(a, 0)) < 5f || Mathf.Abs(Mathf.DeltaAngle(a, 180)) < 5f) continue;   // bridge gap; never in front of the metronome
                Post(stat, root, Quaternion.Euler(0, a, 0) * Vector3.forward * PostRadius, a);
            }
            foreach (var s in new[] { -1, 1 }) Post(stat, root, new Vector3(2.7f * s, 0, PostRadius), 0);

            // ---- south entry bridge (decorative approach; not part of the combat bound) ----
            P(PrimitiveKind.Cube, stat, new Vector3(0, -0.35f, 21f), new Vector3(4.6f, 0.7f, 15f), StoneM(Stone), name: "BridgeDeck");
            for (int s = -1; s <= 1; s += 2)
            {
                P(PrimitiveKind.Cube, stat, new Vector3(2.45f * s, 0.3f, 21f), new Vector3(0.3f, 0.6f, 15f), StoneM(Ivory), name: "BridgeRail");
                P(PrimitiveKind.Cube, stat, new Vector3(2.45f * s, 0.63f, 21f), new Vector3(0.36f, 0.06f, 15f), BrassM, name: "BridgeRailCap");
                for (int k = 0; k < 4; k++) P(PrimitiveKind.Cube, stat, new Vector3(2.45f * s, -3f, 15.5f + k * 4f), new Vector3(0.7f, 5f, 0.9f), StoneM(StoneDark), name: "BridgePier");
            }

            // ---- north plinth and the independent giant metronome ----
            var m = new GameObject("Metronome (arena object)").transform; m.SetParent(root, false); m.localPosition = new Vector3(0, 0, MetronomeZ);
            P(PrimitiveKind.Cube, stat, new Vector3(0, -2.2f, MetronomeZ), new Vector3(9f, 4.4f, 7f), StoneM(StoneDark), name: "Plinth");
            P(PrimitiveKind.Cube, stat, new Vector3(0, 0.05f, MetronomeZ), new Vector3(8.4f, 0.15f, 6.4f), BrassM, name: "PlinthTrim");
            for (int k = 0; k < 3; k++)
            {
                P(PrimitiveKind.Cube, stat, new Vector3(0, -0.3f - k * 0.6f, MetronomeZ + 3.8f + k * 0.7f), new Vector3(9f, 0.6f, 0.7f), StoneM(Stone), name: "PlinthStep");
                P(PrimitiveKind.Cube, stat, new Vector3(0, -0.02f - k * 0.6f, MetronomeZ + 4.12f + k * 0.7f), new Vector3(9f, 0.06f, 0.08f), M(Brass, 0.5f, 0.8f, true), name: "StepNosing");
            }
            for (int s = -1; s <= 1; s += 2)
            {
                P(PrimitiveKind.Cube, stat, new Vector3(4.2f * s, 2.6f, MetronomeZ), new Vector3(0.9f, 5.2f, 0.9f), StoneM(Ivory), name: "PlinthPylon");
                P(PrimitiveKind.Cube, stat, new Vector3(4.2f * s, 2.6f, MetronomeZ + 0.46f), new Vector3(0.5f, 3.8f, 0.04f), StoneM(StoneDark), name: "PylonPanel");
                P(PrimitiveKind.Cube, stat, new Vector3(4.2f * s, 5.3f, MetronomeZ), new Vector3(1.1f, 0.2f, 1.1f), BrassM, name: "PylonCap");
                P(PrimitiveKind.Cone, stat, new Vector3(4.2f * s, 6.1f, MetronomeZ), new Vector3(0.8f, 1.4f, 0.8f), IronM, name: "PylonSpire");
            }
            BuildMetronome(m);
            foreach (var sx in new[] { -1, 1 }) Lantern(root, new Vector3(3.4f * sx, 1.25f, MetronomeZ + 1.9f), 1.1f);

            // ---- distant gothic clocktowers, layered into the fog ----
            for (int i = 0; i < 22; i++)
            {
                float a = -150 + i * 300f / 21; if (Mathf.Abs(Mathf.DeltaAngle(a, 0)) < 25) continue;
                float r = 38 + (float)rng.NextDouble() * 26, h = 16 + (float)rng.NextDouble() * 22, w = 4 + (float)rng.NextDouble() * 2.5f;
                var pos = Quaternion.Euler(0, a + 180, 0) * Vector3.forward * r; pos.y = -14;
                Tower(stat, pos, h, w, rng);
            }
            P(PrimitiveKind.Cone, stat, new Vector3(0, -62, 0), new Vector3(300, 50, 300), M(Abyss, 0.1f), new Vector3(180, 0, 0), "AbyssBowl");
            P(PrimitiveKind.Cylinder, stat, new Vector3(0, -8.5f, 0), new Vector3(FloorRadius * 2 + 4f, 1.5f, FloorRadius * 2 + 4f), StoneM(StoneDark * 0.75f), name: "LowerTier");
            for (int i = 0; i < 7; i++)
            {
                float a = 200 + i * (320f / 7) % 320; if (Mathf.Abs(Mathf.DeltaAngle(a, 0)) < 40) continue;   // keep the south camera arc clear
                ArchBridge(stat, a, 34 + (i % 3) * 6f, rng);
            }

            StaticBatchingUtility.Combine(stat.gameObject);
            BuildRodShadow(root);
            BindWorldLighting();
        }

        // ---- the rod's projected shadow (presentation only): a soft dark tapered quad on the dial ----
        void BuildRodShadow(Transform root)
        {
            if (!shadowMat)
            {
                var src = Resources.Load<Material>("Run/FX_ColorDissolve");
                shadowMat = src ? new Material(src) : new Material(Shader.Find("LastBell/FX/ColorDissolve"));
                shadowMat.name = "MetronomeCourt_RodShadow";
                shadowMat.SetFloat("_SrcBlend", 1); shadowMat.SetFloat("_DstBlend", 10);   // premultiplied alpha: darkens
                shadowMat.SetFloat("_Mode", 0); shadowMat.SetColor("_ColorLow", new Color(0.01f, 0.012f, 0.02f)); shadowMat.SetColor("_ColorHigh", new Color(0.02f, 0.025f, 0.04f));
                shadowMat.SetFloat("_Progress", 1.2f); shadowMat.SetFloat("_Width", 0.1f); shadowMat.SetFloat("_Tail", 6f);
                shadowMat.SetFloat("_Dissolve", 0); shadowMat.SetFloat("_Soft", 0.3f); shadowMat.SetVector("_NoiseScale", new Vector4(2, 1, 0, 0));
                shadowMat.SetVector("_Scroll", Vector4.zero); shadowMat.SetFloat("_Intensity", 0.42f);
                shadowMat.renderQueue = 2990;   // under the telegraphs
            }
            var go = new GameObject("Rod shadow (projected)"); go.transform.SetParent(root, false);
            shadowMesh = new Mesh { name = "RodShadow" }; shadowMesh.MarkDynamic();
            shadowMesh.vertices = new Vector3[4]; shadowMesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0), new Vector2(1, 1) };
            shadowMesh.triangles = new[] { 0, 1, 2, 1, 3, 2 };
            go.AddComponent<MeshFilter>().sharedMesh = shadowMesh;
            shadowR = go.AddComponent<MeshRenderer>(); shadowR.sharedMaterial = shadowMat;
            shadowR.shadowCastingMode = ShadowCastingMode.Off; shadowR.receiveShadows = false;
        }

        void UpdateRodShadow()
        {
            if (!shadowMesh || !Rod || !RodShadowLight) return;
            var L = RodShadowLight.transform.position;
            var basePt = Rod.position + Rod.up * 3.0f; var tipPt = Rod.position + Rod.up * (RodLength + 0.6f);
            bool okB = Project(L, basePt, out var b0), okT = Project(L, tipPt, out var t0);
            RodShadowOnFloor = okB && okT;
            shadowR.enabled = RodShadowOnFloor;
            if (!RodShadowOnFloor) return;
            // clip to the floor disc
            var d = t0 - b0; float len = d.magnitude; if (len < 0.1f) { shadowR.enabled = false; return; }
            var dir = d / len;
            float enter = Mathf.Max(0, RayCircle(b0, dir, FloorRadius, true)), exit = Mathf.Min(len, RayCircle(b0, dir, FloorRadius, false));
            if (exit <= enter) { shadowR.enabled = RodShadowOnFloor = false; return; }
            var a = b0 + dir * enter; var c = b0 + dir * exit; RodShadowTip = c;
            var side = Vector3.Cross(Vector3.up, dir);
            float wa = Mathf.Lerp(0.4f, 1.0f, enter / len) * 0.5f, wc = Mathf.Lerp(0.4f, 1.0f, exit / len) * 0.5f;
            var y = Vector3.up * 0.03f;
            shadowMesh.vertices = new[] { a - side * wa + y, a + side * wa + y, c - side * wc + y, c + side * wc + y };
            shadowMesh.RecalculateBounds();
        }
        static bool Project(Vector3 light, Vector3 p, out Vector3 hit)
        {
            hit = default; var d = p - light; if (d.y >= -1e-3f) return false;
            float t = (0.03f - light.y) / d.y; hit = light + d * t; hit.y = 0; return true;
        }
        // distance along dir from o (on the floor plane) to the circle of radius r (entry or exit)
        static float RayCircle(Vector3 o, Vector3 dir, float r, bool entry)
        {
            var po = new Vector2(o.x, o.z); var dd = new Vector2(dir.x, dir.z);
            float b = Vector2.Dot(po, dd), c = po.sqrMagnitude - r * r, disc = b * b - c;
            if (disc < 0) return entry ? float.MaxValue : float.MinValue;
            float sq = Mathf.Sqrt(disc); return entry ? -b - sq : -b + sq;
        }

        // ---- tempo changes the world: key light colour, ambient and colour grade (runtime profile copy) ----
        void BindWorldLighting()
        {
            foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                if (l.type == LightType.Directional && (!keyLight || l.intensity > keyLight.intensity)) keyLight = l;
            var vol = FindAnyObjectByType<Volume>();
            if (vol && vol.sharedProfile) { var prof = vol.profile; prof.TryGet(out grade); }   // vol.profile: an instance, never the asset
            baseEquator = RenderSettings.ambientEquatorColor;
        }

        void UpdateWorldLighting(bool haste, bool frenzy)
        {
            tempoBlend = Mathf.MoveTowards(tempoBlend, haste ? 1 : 0, Time.deltaTime / 0.5f);
            var warm = new Color(1.0f, 0.93f, 0.84f); var cool = new Color(0.88f, 0.94f, 1.0f);
            if (keyLight) keyLight.color = Color.Lerp(cool, warm, tempoBlend);
            RenderSettings.ambientEquatorColor = baseEquator * Color.Lerp(new Color(0.96f, 0.99f, 1.03f), new Color(1.03f, 1.0f, 0.96f), tempoBlend);
            if (grade != null)
            {
                var filter = Color.Lerp(new Color(0.97f, 1.0f, 1.01f), new Color(1.02f, 0.99f, 0.95f), tempoBlend);
                if (frenzy) filter *= new Color(1.05f, 0.93f, 0.92f);
                grade.colorFilter.Override(filter);
            }
        }

        void Tower(Transform stat, Vector3 pos, float h, float w, System.Random rng)
        {
            float y = pos.y, seg = h / 3f;
            for (int k = 0; k < 3; k++)
            {
                float ww = w * (1 - 0.18f * k);
                P(PrimitiveKind.Cube, stat, new Vector3(pos.x, y + seg / 2, pos.z), new Vector3(ww, seg, ww), StoneM(StoneDark * (0.9f + 0.1f * k)), name: "TowerTier");
                P(PrimitiveKind.Cube, stat, new Vector3(pos.x, y + seg, pos.z), new Vector3(ww + 0.4f, 0.3f, ww + 0.4f), StoneM(StoneDark * 0.8f), name: "TowerCornice");
                // a row of thin lit window slits on the side facing the court
                var toward = -Sim.Flat(pos).normalized; var side = Vector3.Cross(Vector3.up, toward);
                if (rng.NextDouble() < 0.7)
                    for (int s = -1; s <= 1; s++)
                        P(PrimitiveKind.Cube, stat, new Vector3(pos.x, y + seg * 0.55f, pos.z) + toward * (ww / 2 + 0.02f) + side * (s * ww * 0.25f),
                            new Vector3(0.22f, seg * 0.35f, 0.05f), M(new Color(1f, 0.72f, 0.38f), 0.2f, 0, true), new Vector3(0, Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg, 0), "WindowSlit");
                if (rng.NextDouble() < 0.4)
                {
                    var arch = new Vector3(pos.x, y + seg * 0.62f, pos.z) + toward * (ww / 2 + 0.03f);
                    P(PrimitiveKind.Cube, stat, arch, new Vector3(0.9f, seg * 0.42f, 0.05f), M(new Color(1f, 0.62f, 0.3f), 0.2f, 0, true), new Vector3(0, Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg, 0), "ArchWindow");
                    P(PrimitiveKind.Cube, stat, arch + Vector3.up * (seg * 0.23f) + toward * 0.02f, new Vector3(0.6f, 0.6f, 0.05f), M(new Color(1f, 0.62f, 0.3f), 0.2f, 0, true), new Vector3(0, Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg, 45), "ArchCap");
                }
                y += seg;
            }
            P(PrimitiveKind.Cone, stat, new Vector3(pos.x, y + 4f, pos.z), new Vector3(w * 0.75f, 8f, w * 0.75f), IronM, name: "Spire");
            for (int b = 0; b < 4; b++)
            {
                var o = Quaternion.Euler(0, b * 90 + 45, 0) * Vector3.forward * (w * 0.62f);
                P(PrimitiveKind.Cone, stat, new Vector3(pos.x, y - seg * 0.2f, pos.z) + o, new Vector3(0.9f, 2.6f, 0.9f), IronM, name: "Pinnacle");
            }
        }

        // dark arch-bridge silhouette spanning tangentially at radius r: piers, half-cylinder arches, a deck, a few lit windows
        void ArchBridge(Transform stat, float angle, float r, System.Random rng)
        {
            var dir = Quaternion.Euler(0, angle, 0) * Vector3.forward; var tangent = Vector3.Cross(Vector3.up, dir);
            var centre = dir * r + Vector3.up * -6f; var col = new Color(0.10f, 0.12f, 0.16f);
            float span = 18f, yaw = Mathf.Atan2(tangent.x, tangent.z) * Mathf.Rad2Deg;
            P(PrimitiveKind.Cube, stat, centre + Vector3.up * 6f, new Vector3(span, 1.2f, 3f), M(col, 0.1f), new Vector3(0, yaw, 0), "BridgeDeck");
            for (int k = -1; k <= 1; k++)
            {
                P(PrimitiveKind.Cube, stat, centre + tangent * (k * span / 3), new Vector3(1.6f, 12f, 3f), M(col, 0.1f), new Vector3(0, yaw, 0), "BridgePier");
                if (k < 1) P(PrimitiveKind.Cylinder, stat, centre + tangent * ((k + 0.5f) * span / 3) + Vector3.up * 3.6f, new Vector3(span / 3 - 1.4f, 1.4f, span / 3 - 1.4f), M(col * 0.9f, 0.1f), new Vector3(0, yaw, 90), "BridgeArch");
            }
            if (rng.NextDouble() < 0.7)
                P(PrimitiveKind.Cube, stat, centre + Vector3.up * 6.9f - dir * 1.55f, new Vector3(0.5f, 0.6f, 0.05f), M(new Color(1f, 0.66f, 0.32f), 0.2f, 0, true), new Vector3(0, yaw, 0), "BridgeWindow");
        }

        void Ring(Transform parent, float r, float width, int segs, Material mat, string name)
        {
            float seg = 2 * Mathf.PI * r / segs * 1.03f;
            for (int i = 0; i < segs; i++)
            {
                float a = (i + 0.5f) * 360f / segs; var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                P(PrimitiveKind.Cube, parent, dir * r + Vector3.up * 0.005f, new Vector3(seg, 0.01f, width), mat, new Vector3(0, a, 0), name);   // yaw a: long axis tangent
            }
        }

        // The fixed gameplay camera sits south (+z) of the player looking north and down: anything tall between them
        // could hide a player at the wall, so camera-side perimeter pieces are low bollards with a lantern on top.
        static bool CameraSide(Vector3 at) => at.z > -4f;

        void Lantern(Transform root, Vector3 at, float scale)
        {
            var l = new GameObject("Lantern").transform; l.SetParent(root, false); l.localPosition = at;
            var core = P(PrimitiveKind.Sphere, l, Vector3.zero, Vector3.one * 0.26f * scale, M(new Color(1f, 0.7f, 0.3f), 0.2f, 0, true), name: "LanternCore");
            for (int k = 0; k < 4; k++)
                P(PrimitiveKind.Cube, l, Quaternion.Euler(0, k * 90 + 45, 0) * Vector3.forward * 0.17f * scale, new Vector3(0.04f, 0.44f, 0.04f) * scale, IronM, name: "LanternBar");
            P(PrimitiveKind.Cone, l, Vector3.up * 0.32f * scale, new Vector3(0.42f, 0.24f, 0.42f) * scale, IronM, name: "LanternCap");
            P(PrimitiveKind.Cube, l, Vector3.down * 0.24f * scale, new Vector3(0.36f, 0.05f, 0.36f) * scale, BrassM, name: "LanternBase");
            lanterns.Add((core.GetComponent<Renderer>(), at.x));
        }

        void Post(Transform stat, Transform root, Vector3 at, float yaw)
        {
            if (CameraSide(at))
            {
                P(PrimitiveKind.Cube, stat, at + Vector3.up * 0.2f, new Vector3(0.55f, 0.4f, 0.55f), StoneM(StoneDark), new Vector3(0, yaw, 0), "BollardBase");
                P(PrimitiveKind.Cube, stat, at + Vector3.up * 0.55f, new Vector3(0.42f, 0.3f, 0.42f), StoneM(Ivory), new Vector3(0, yaw + 45, 0), "BollardChamfer");
                P(PrimitiveKind.Cube, stat, at + Vector3.up * 0.73f, new Vector3(0.5f, 0.06f, 0.5f), BrassM, new Vector3(0, yaw, 0), "BollardCap");
                Lantern(root, at + Vector3.up * 1.0f, 0.75f);
                return;
            }
            P(PrimitiveKind.Cube, stat, at + Vector3.up * 0.3f, new Vector3(1.15f, 0.6f, 1.15f), StoneM(StoneDark), new Vector3(0, yaw, 0), "PostPlinth");
            P(PrimitiveKind.Cube, stat, at + Vector3.up * 1.9f, new Vector3(0.75f, 2.6f, 0.75f), StoneM(Ivory), new Vector3(0, yaw, 0), "PostShaft");
            P(PrimitiveKind.Cylinder, stat, at + Vector3.up * 0.75f, new Vector3(0.95f, 0.05f, 0.95f), BrassM, name: "PostCollar");
            P(PrimitiveKind.Cylinder, stat, at + Vector3.up * 2.9f, new Vector3(0.95f, 0.05f, 0.95f), BrassM, name: "PostCollar");
            P(PrimitiveKind.Cube, stat, at + Vector3.up * 3.3f, new Vector3(1.0f, 0.2f, 1.0f), BrassM, new Vector3(0, yaw, 0), "PostCornice");
            P(PrimitiveKind.Cone, stat, at + Vector3.up * 4.5f, new Vector3(0.55f, 1.2f, 0.55f), IronM, name: "PostSpire");
            // banner faces the court (and so the camera), hung from a brass rod
            var inward = -Sim.Flat(at).normalized; float faceYaw = Mathf.Atan2(inward.x, inward.z) * Mathf.Rad2Deg;
            P(PrimitiveKind.Cube, stat, at + inward * 0.42f + Vector3.up * 1.9f, new Vector3(0.62f, 1.9f, 0.04f), M(Teal, 0.15f), new Vector3(0, faceYaw, 0), "Banner");
            P(PrimitiveKind.Cube, stat, at + inward * 0.44f + Vector3.up * 2.87f, new Vector3(0.78f, 0.05f, 0.05f), BrassM, new Vector3(0, faceYaw, 0), "BannerRod");
            P(PrimitiveKind.Cube, stat, at + inward * 0.45f + Vector3.up * 1.9f, new Vector3(0.12f, 1.2f, 0.02f), BrassM, new Vector3(0, faceYaw, 0), "BannerEmblem");
            Lantern(root, at + Vector3.up * 3.75f, 1f);
        }

        void BuildMetronome(Transform m)
        {
            // dark iron back case (trapezoid), tapered open front frame with brass edge bevels and tempo-lit seams
            Frame = new GameObject("Frame").transform; Frame.SetParent(m, false);
            float baseHalf = 3.4f, topHalf = 0.6f, h = FrameHeight;
            Mesh(Frame, Trapezoid(baseHalf - 0.2f, topHalf - 0.05f, h - 0.1f, 0.8f), M(new Color(0.10f, 0.11f, 0.13f), 0.15f, 0f), "BackCase", new Vector3(0, 0.1f, -0.15f));
            Mesh(Frame, Trapezoid(baseHalf - 0.75f, topHalf * 0.4f, h - 1.2f, 0.05f), M(new Color(0.07f, 0.075f, 0.09f), 0.1f, 0f), "CaseRecess", new Vector3(0, 0.5f, 0.15f));
            for (int s = -1; s <= 1; s += 2)
            {
                var a = new Vector3(baseHalf * s, 0, 0.35f); var b = new Vector3(topHalf * s, h, 0.35f);
                var rot = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
                var stile = P(PrimitiveKind.Cube, Frame, (a + b) / 2, new Vector3(0.9f, (b - a).magnitude, 0.8f), IronM, name: "Stile"); stile.localRotation = rot;
                var trim = P(PrimitiveKind.Cube, Frame, (a + b) / 2 + new Vector3(-0.34f * s, 0, 0.41f), new Vector3(0.12f, (b - a).magnitude, 0.05f), BrassM, name: "StileBevel"); trim.localRotation = rot;
                var seam = P(PrimitiveKind.Cube, Frame, (a + b) / 2 + new Vector3(0.16f * s, 0, 0.42f), new Vector3(0.05f, (b - a).magnitude * 0.9f, 0.02f), M(HasteTint, 0.2f, 0, true), name: "StileSeam"); seam.localRotation = rot;
                seams.Add(seam.GetComponent<Renderer>());
            }
            // frenzy cracks: jagged three-segment zigzags across the case front (hidden until Broken Cadence)
            var crackPaths = new[] { new[] { new Vector2(-1.3f, 2.2f), new Vector2(-0.8f, 3.1f), new Vector2(-1.2f, 3.9f), new Vector2(-0.7f, 4.8f) },
                                     new[] { new Vector2(1.1f, 1.4f), new Vector2(0.6f, 2.3f), new Vector2(1.0f, 3.0f), new Vector2(0.5f, 3.8f) },
                                     new[] { new Vector2(0.2f, 6.2f), new Vector2(-0.25f, 6.9f), new Vector2(0.15f, 7.6f), new Vector2(-0.2f, 8.3f) } };
            foreach (var path in crackPaths)
                for (int k = 0; k < path.Length - 1; k++)
                {
                    var p0 = path[k]; var p1 = path[k + 1]; var d = p1 - p0;
                    var c = P(PrimitiveKind.Cube, Frame, new Vector3((p0.x + p1.x) / 2, (p0.y + p1.y) / 2, 0.22f), new Vector3(0.05f, d.magnitude + 0.04f, 0.02f), M(CrackGlow, 0.2f, 0, true),
                        new Vector3(0, 0, -Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg), "FrenzyCrack");
                    cracks.Add(c.GetComponent<Renderer>());
                }
            for (int k = 1; k <= 3; k++)
            {
                float y = h * k / 4f, half = Mathf.Lerp(baseHalf, topHalf, y / h);
                P(PrimitiveKind.Cube, Frame, new Vector3(0, y, 0.3f), new Vector3(half * 2, 0.2f, 0.3f), BrassM, name: "Brace");
            }
            P(PrimitiveKind.Cube, Frame, new Vector3(0, h + 0.3f, 0), new Vector3(1.7f, 0.6f, 1.2f), BrassM, name: "ApexCap");
            P(PrimitiveKind.Cone, Frame, new Vector3(0, h + 1.1f, 0), new Vector3(0.7f, 1.0f, 0.7f), IronM, name: "ApexFinial");
            P(PrimitiveKind.Cube, Frame, new Vector3(0, 0.3f, 0), new Vector3(baseHalf * 2 + 0.8f, 0.6f, 1.8f), IronM, name: "FrameBase");
            P(PrimitiveKind.Cube, Frame, new Vector3(0, 0.62f, 0.2f), new Vector3(baseHalf * 2 + 0.9f, 0.08f, 1.9f), BrassM, name: "FrameBaseTrim");
            for (int k = 0; k < 9; k++) P(PrimitiveKind.Cube, Frame, new Vector3(0, 3.0f + k * 0.65f, 0.2f), new Vector3(k % 2 == 0 ? 0.8f : 0.45f, 0.06f, 0.02f), BrassM, name: "ScaleMark");
            // escapement wheel (2.6 m, spokes and teeth) at the lower pivot; steps on each tick, with a pawl that clicks
            Escapement = new GameObject("EscapementWheel").transform; Escapement.SetParent(m, false); Escapement.localPosition = new Vector3(0, 1.6f, 0.45f);
            P(PrimitiveKind.Cylinder, Escapement, Vector3.zero, new Vector3(2.6f, 0.06f, 2.6f), BrassM, new Vector3(90, 0, 0), "WheelRim");
            P(PrimitiveKind.Cylinder, Escapement, new Vector3(0, 0, 0.02f), new Vector3(2.2f, 0.07f, 2.2f), IronM, new Vector3(90, 0, 0), "WheelWeb");
            for (int k = 0; k < 6; k++) P(PrimitiveKind.Cube, Escapement, new Vector3(0, 0, 0.06f), new Vector3(0.12f, 2.2f, 0.06f), BrassM, new Vector3(0, 0, k * 30), "Spoke");
            for (int k = 0; k < 15; k++) P(PrimitiveKind.Cube, Escapement, Quaternion.Euler(0, 0, k * 24) * Vector3.up * 1.36f, new Vector3(0.16f, 0.22f, 0.08f), BrassM, new Vector3(0, 0, k * 24 + 12), "Tooth");
            Pawl = new GameObject("Pawl").transform; Pawl.SetParent(m, false); Pawl.localPosition = new Vector3(1.35f, 3.1f, 0.5f);
            P(PrimitiveKind.Cube, Pawl, new Vector3(-0.35f, -0.35f, 0), new Vector3(0.14f, 1.0f, 0.1f), IronM, new Vector3(0, 0, 45), "PawlArm");
            // pivot, ONE rod and its sliding weight (separate objects for animation)
            var pivot = new GameObject("Pivot").transform; pivot.SetParent(m, false); pivot.localPosition = new Vector3(0, 1.6f, 0.75f);
            P(PrimitiveKind.Cylinder, pivot, Vector3.zero, new Vector3(0.6f, 0.25f, 0.6f), BrassM, new Vector3(90, 0, 0), "Axle");
            Rod = new GameObject("Rod").transform; Rod.SetParent(pivot, false);
            P(PrimitiveKind.Cube, Rod, new Vector3(0, RodLength / 2, 0), new Vector3(0.3f, RodLength, 0.2f), M(new Color(0.75f, 0.58f, 0.28f), 0.55f, 0.8f), name: "RodBar");
            P(PrimitiveKind.Cylinder, Rod, new Vector3(0, 0.45f, 0), new Vector3(0.42f, 0.35f, 0.42f), BrassM, name: "RodFerrule");
            P(PrimitiveKind.Cone, Rod, new Vector3(0, RodLength + 0.3f, 0), new Vector3(0.36f, 0.6f, 0.36f), BrassM, name: "RodTip");
            Weight = new GameObject("SlidingWeight").transform; Weight.SetParent(Rod, false); Weight.localPosition = new Vector3(0, 5.6f, 0);
            P(PrimitiveKind.Cube, Weight, Vector3.zero, new Vector3(1.2f, 1.4f, 0.5f), BrassM, name: "WeightBody");
            P(PrimitiveKind.Cube, Weight, new Vector3(0, 0, 0.26f), new Vector3(0.95f, 1.15f, 0.04f), IronM, name: "WeightFace");
            var slot = P(PrimitiveKind.Cube, Weight, new Vector3(0, 0, 0.29f), new Vector3(0.18f, 0.8f, 0.02f), M(HasteTint, 0.2f, 0, true), name: "WeightSlot");
            seams.Add(slot.GetComponent<Renderer>());
            var go = new GameObject("Metronome light"); go.transform.SetParent(m, false); go.transform.localPosition = new Vector3(0, 6, 4);
            metronomeLight = go.AddComponent<Light>(); metronomeLight.type = LightType.Point; metronomeLight.range = 18; metronomeLight.intensity = 6; metronomeLight.shadows = LightShadows.None;
            // rod-shadow light: above and just behind the apex, aimed across the court, so the swinging rod throws a long
            // tempo-tinted shadow over the dial that the fixed gameplay camera always sees
            var sl = new GameObject("Rod shadow light"); sl.transform.SetParent(m, false); sl.transform.localPosition = new Vector3(0, 14.5f, -3.0f);
            sl.transform.rotation = Quaternion.LookRotation(new Vector3(0, 0, 3f) - (m.position + sl.transform.localPosition));
            RodShadowLight = sl.AddComponent<Light>(); RodShadowLight.type = LightType.Spot; RodShadowLight.spotAngle = 58; RodShadowLight.innerSpotAngle = 30;
            RodShadowLight.range = 48; RodShadowLight.intensity = 140; RodShadowLight.shadows = LightShadows.Soft; RodShadowLight.shadowStrength = 0.92f;
            RodShadowLight.shadowNearPlane = 1.2f;
        }

        void Update()
        {
            var d = RunDirector.Instance;
            var boss = d != null ? d.Boss as TempoTyrantEncounter : null;
            bool fight = boss && !boss.Dead && d.State == RunState.Combat;
            float phase; bool haste; bool frenzy = boss && boss.Frenzy && !boss.Dead; bool accent = false; int beat;
            if (fight)
            {
                Preview = false;
                phase = boss.Clock.RodPhase; haste = boss.Clock.State == Tempo.Haste;
                beat = (int)System.Math.Floor(boss.Clock.Beat); accent = boss.Clock.Transitioning;
            }
            else
            {
                // readiness / menu preview: non-damaging Haste ticking, frozen while paused
                Preview = true; haste = true;
                if (d == null || !d.Paused) previewBeat += Time.deltaTime * 220 / 60.0;   // previews the opening Haste tempo
                phase = Mathf.Cos((float)(previewBeat % 2.0) * Mathf.PI);
                beat = (int)System.Math.Floor(previewBeat);
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

            var tint = haste ? HasteTint : NormalTint;
            float flicker = frenzy ? 0.75f + 0.25f * Mathf.PerlinNoise(Time.time * 9, 0.3f) : 1f;
            if (metronomeLight) { metronomeLight.color = frenzy ? Color.Lerp(tint, CrackGlow, 0.35f) : tint; metronomeLight.intensity = (haste ? 6f : 4f) * flicker * (1 + flash); }
            if (RodShadowLight) { RodShadowLight.color = tint; RodShadowLight.intensity = (haste ? 40f : 26f) * flicker * (quiet ? 0.8f : 1f) * (1 + 0.5f * flash); }
            if (mpb == null) mpb = new MaterialPropertyBlock();   // survives a script reload
            // lantern glow follows the rod's lean across the rim; all flash together on accented transition ticks
            LanternPulseX = phase * 14f;
            foreach (var (r, x) in lanterns)
            {
                if (!r) continue;
                float near = Mathf.Exp(-(x - LanternPulseX) * (x - LanternPulseX) / 6f);
                float k = (quiet ? 0.7f : 1f) * ((haste ? 1.3f : 1.0f) * (0.8f + 2.2f * near) + 2.5f * flash);
                mpb.SetColor(PBase, tint); mpb.SetColor(PEmission, tint * k); r.SetPropertyBlock(mpb);
            }
            var seamTint = frenzy && Mathf.PerlinNoise(Time.time * 7, 1.7f) > 0.55f ? CrackGlow : tint;
            mpb.SetColor(PBase, seamTint); mpb.SetColor(PEmission, seamTint * ((haste ? 1.5f : 0.8f) + 1.5f * flash));
            foreach (var r in seams) if (r) r.SetPropertyBlock(mpb);
            mpb.SetColor(PBase, tint); mpb.SetColor(PEmission, tint * ((haste ? 1.3f : 0.9f) + 1.2f * flash));
            foreach (var r in tempoLineRs) if (r) r.SetPropertyBlock(mpb);
            UpdateRodShadow();
            UpdateWorldLighting(haste, frenzy);
            ShowingCracks = frenzy;
            mpb.SetColor(PBase, CrackGlow); mpb.SetColor(PEmission, CrackGlow * (frenzy ? 1.6f * flicker : 0f));
            foreach (var r in cracks) if (r) { r.enabled = frenzy; r.SetPropertyBlock(mpb); }
        }
    }
}
