using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // Arcanist effects, arcane redesign (docs/design/arcanist-fx-prompts.md, E1-E6), round 3 after two critiques.
    // ArcanistKit calls these on its authoritative ticks; nothing here decides hits or timing. Built from small code
    // meshes (crossed darts, dashed rings, line strokes, tapered ribbons, a helix, quads, a sphere) drawn with
    // LastBell/FX/ArcaneFlow (flowing noise, erosion with a glowing edge) through the shared burst pool, a few pooled
    // trails and short real lights. Tint lives in the colour; HDR values stay moderate so tonemapping keeps the hue.
    public sealed partial class PlayerFx
    {
        // palette (HDR), kept moderate so violet survives tonemapping instead of clipping to white
        public static readonly Color ArcLilac = new Color(0.75f, 0.52f, 1.75f), HotWhite = new Color(1.7f, 1.45f, 2.0f),
            Ultraviolet = new Color(0.55f, 0.18f, 1.5f), UVCore = new Color(1.3f, 0.95f, 2.0f), BeamCore = new Color(0.95f, 0.6f, 2.05f), BeamHot = new Color(1.4f, 1.05f, 2.25f), Void = new Color(0.05f, 0.01f, 0.10f),
            AshMagenta = new Color(1.6f, 0.35f, 1.2f), RuneViolet = new Color(0.7f, 0.38f, 1.6f), ArmedGold = new Color(1.8f, 1.35f, 0.7f),
            StarSilver = new Color(1.45f, 1.45f, 1.7f), StarEdge = new Color(0.8f, 0.45f, 1.6f), CometViolet = new Color(0.75f, 0.38f, 1.7f),
            DeepViolet = new Color(0.22f, 0.08f, 0.45f), BurntViolet = new Color(0.16f, 0.1f, 0.24f), BurntGrey = new Color(0.35f, 0.25f, 0.5f),
            FaintInk = new Color(0.4f, 0.32f, 0.6f), FaintHot = new Color(0.72f, 0.64f, 0.95f), DeepGold = new Color(1.25f, 0.8f, 0.3f),
            CometDeep = new Color(0.42f, 0.25f, 1.6f), CometPale = new Color(0.85f, 0.7f, 2.1f),   // comet impact violets (bluer, so they don't tone-map pink)
            SurgeInk = new Color(1.5f, 1.05f, 0.5f), SigilArmed = new Color(2.0f, 1.5f, 0.6f),
            BlinkLow = new Color(0.5f, 0.3f, 1.45f), BlinkHigh = new Color(1.0f, 0.8f, 1.95f), GlyphHot = new Color(1.0f, 0.62f, 2.0f), EdgeGold = new Color(1.1f, 0.7f, 0.18f), EdgeGoldHot = new Color(1.35f, 0.9f, 0.3f), SurgeDart = new Color(1.25f, 0.8f, 1.05f),
            FireLow = new Color(0.7f, 0.25f, 1.6f), FireHigh = new Color(1.9f, 1.05f, 2.1f),
            MeteorChar = new Color(0.085f, 0.075f, 0.08f), Molten = new Color(1.1f, 0.7f, 2.4f), WhiteHot = new Color(1.7f, 1.45f, 2.5f);   // the meteor's stone
        // kept for older callers (menu battle bolts)
        public static readonly Color TimeTeal = new Color(0.45f, 1.9f, 1.75f);

        static readonly int PT = Shader.PropertyToID("_T"), PNoiseAmt = Shader.PropertyToID("_NoiseAmt"), PFresnel = Shader.PropertyToID("_Fresnel"),
            PSoft = Shader.PropertyToID("_Soft"), PEdgeGlow = Shader.PropertyToID("_EdgeGlow"), PNoiseScale = Shader.PropertyToID("_NoiseScale"),
            PFlow = Shader.PropertyToID("_Flow"), PClipY = Shader.PropertyToID("_ClipY"), PCull = Shader.PropertyToID("_Cull"), PHeatDir = Shader.PropertyToID("_HeatDir");
        const float MBand = 0, MDot = 1, MStroke = 2, MBeam = 3, MShell = 4, MFlake = 5, MShadow = 6, MDart = 7, MPillar = 8, MRock = 9;

        bool arcReady;
        Material arcAdd, arcAddFloor, arcAlpha, arcAlphaFloor, arcAlphaSolid, arcAlphaFloorTop, arcOver, arcOverEdge, arcAlphaUnder, ashMat, arcTrailMat, silverTrailMat, goldTrailMat;
        FxParticles arcMotes, arcSparks, ashFx, embers, flameFx;
        Mesh sphereMesh, dartMesh, shardMesh, beamMesh, billboard, taperMesh, helixMesh, colShell;
        readonly List<TrailRenderer> trailPool = new List<TrailRenderer>();
        Light[] arcLights; float[] lightLeft, lightLife, lightPeak;

        sealed class Handle { public Burst b; public int id; public bool Alive(PlayerFx f) => b != null && b.id == id && f.live.Contains(b); }
        sealed class ArcState
        {
            public Transform hand, astrolabe; public AstrolabeRig rig; public SkinnedMeshRenderer[] skins;
            public Handle glyph; public float glyphFill, glyphPulse = -9; public readonly Handle[] darts = new Handle[5];
            public readonly List<Handle> glyphArcs = new List<Handle>();   // the charge ring's arcs, one per dart
            public readonly List<Handle> lens = new List<Handle>(); public readonly List<Handle> beam = new List<Handle>();
            public Vector3 beamFrom, beamEnd; public int beamStep; public bool beamHit; public float beamStart, ashAcc, ashTail = -9, stepPulse = -9, shedAcc, shakeAt, debrisAt;
            // glyphs: the socket ring, one set of parts per glyph, when each finished arriving; the orbit angle; the Surge cast pull
            public readonly List<Handle> sockets = new List<Handle>(); public readonly Handle[][] glyphOrbs = new Handle[ArcanistKit.MaxGlyphs][];
            public readonly float[] glyphBorn = new float[ArcanistKit.MaxGlyphs]; public float orbit, surgePull = -1, surgeSnap = -9, snapFrom, surgeCastLife = 0.3f, fullAt = -9;
            public float rimNext;
            public readonly List<Handle> surgeCast = new List<Handle>(); public readonly List<Handle> surge = new List<Handle>(); public float surgeEnding = -1;
            public Handle thread; public Vector3 blinkFrom; public List<Handle> departShell;
        }
        readonly Dictionary<PlayerController, ArcState> arc = new Dictionary<PlayerController, ArcState>();
        sealed class MarkFx { public readonly List<Handle> parts = new List<Handle>(); public Vector3 pos, from; public float start, life; public Transform comet; public float emberAcc, smokeAcc, fireAcc; public List<(Vector3 a, Vector3 b)> strokes; public List<Mesh> glyphMeshes; public List<float> rings; public List<(Vector3 at, Quaternion rot)> glyphPose; public float ringYaw, glyphSize, line, window; public bool weak; public int runeFrom, runeTo; public float snuffAt = -1; }
        readonly Dictionary<int, MarkFx> marks = new Dictionary<int, MarkFx>();

        // ------------------------------------------------------------------ setup
        static Shader ArcShader()
        {
            var s = Shader.Find("LastBell/FX/ArcaneFlow");
            if (s == null) { var m = Resources.Load<Material>("Run/FX_ArcaneFlow"); if (m) s = m.shader; }
            return s != null ? s : DissolveShader();
        }
        void EnsureArc()
        {
            if (arcReady) return; arcReady = true;
            var sh = ArcShader();
            arcAdd = new Material(sh);
            arcAddFloor = new Material(sh) { renderQueue = FloorQueue };
            // alpha-blended dark layers (Disintegrate sheath, scorches) draw before the additive light, never over it
            arcAlpha = new Material(sh) { renderQueue = 3058 }; arcAlpha.SetFloat(PSrc, 1); arcAlpha.SetFloat(PDst, 10);
            arcAlphaFloor = new Material(sh) { renderQueue = FloorQueue }; arcAlphaFloor.SetFloat(PSrc, 1); arcAlphaFloor.SetFloat(PDst, 10);
            // a solid body: front faces only, so its back faces never draw over its front (no seam)
            arcAlphaSolid = new Material(arcAlpha) { renderQueue = 3058 }; arcAlphaSolid.SetFloat(PCull, 2);
            // a floor darkening drawn after the floor runes (still under boss telegraphs at 3050+)
            arcAlphaFloorTop = new Material(arcAlphaFloor) { renderQueue = FloorQueue + 4 };
            // a premultiplied-alpha layer drawn after the additive light: a glyph's violet core covers its own gold edge
            // stroke instead of adding to it (no white clipping)
            arcOver = new Material(sh) { renderQueue = 3062 }; arcOver.SetFloat(PSrc, 1); arcOver.SetFloat(PDst, 10);
            arcAlphaUnder = new Material(arcAlphaFloor) { renderQueue = FloorQueue - 1 };
            // a glyph's gold outline: drawn solid (not added) just before its violet core, so it stays gold instead of blooming white
            arcOverEdge = new Material(arcOver) { renderQueue = 3061 };
            arcMotes = FxParticles.Create(transform, "Arcanist motes", DotMat(sh, ArcLilac, HotWhite), 600, 0f, 2.5f, 0f);
            arcSparks = FxParticles.Create(transform, "Arcanist sparks", DotMat(sh, ArcLilac, HotWhite), 400, 5f, 1.6f, 0.06f);
            embers = FxParticles.Create(transform, "Arcanist embers", DotMat(sh, AshMagenta, new Color(1.9f, 0.8f, 1.6f)), 200, 2f, 1.2f, 0.05f);
            // violet fire puffs: soft noisy sprites that rise a little and stretch along their motion (the meteor's flames)
            var fireMat = DotMat(sh, FireLow, FireHigh); fireMat.SetFloat(PNoiseAmt, 0.7f); fireMat.SetVector(PNoiseScale, new Vector4(2.5f, 2.5f, 0, 0)); fireMat.SetFloat(PSoft, 0.45f);
            flameFx = FxParticles.Create(transform, "Arcanist flames", fireMat, 500, -1.5f, 2.2f, 0.06f);
            ashMat = new Material(sh); ashMat.SetFloat(PSrc, 1); ashMat.SetFloat(PDst, 10); ashMat.SetFloat(PMode, MFlake);
            ashMat.SetColor(PLow, new Color(0.06f, 0.03f, 0.08f)); ashMat.SetColor(PHigh, new Color(0.1f, 0.05f, 0.13f)); ashMat.SetColor(PEdge, new Color(1.4f, 0.3f, 1.0f));
            ashMat.SetFloat(PNoiseAmt, 0.6f); ashMat.SetVector(PNoiseScale, new Vector4(1.2f, 1.2f, 0, 0)); ashMat.SetFloat(PSoft, 0.05f); ashMat.SetFloat(PEdgeGlow, 0.5f);
            ashMat.SetFloat(PDissolve, 0.18f);   // a ragged dark flake with a thin glowing erosion edge
            ashFx = FxParticles.Create(transform, "Arcanist ash", ashMat, 300, 4.5f, 0.5f, 0f);
            arcTrailMat = new Material(sh); arcTrailMat.SetFloat(PMode, MBeam); arcTrailMat.SetFloat(PWidth, 0.35f); arcTrailMat.SetColor(PLow, ArcLilac); arcTrailMat.SetColor(PHigh, HotWhite);
            arcTrailMat.SetVector(PFlow, new Vector4(2.5f, 0, 1.2f, 0.4f)); arcTrailMat.SetFloat(PIntensity, 0.85f);
            silverTrailMat = new Material(arcTrailMat); silverTrailMat.SetColor(PLow, StarEdge); silverTrailMat.SetColor(PHigh, StarSilver);
            goldTrailMat = new Material(arcTrailMat); goldTrailMat.SetColor(PLow, Color.Lerp(ArcLilac, ArmedGold, 0.5f)); goldTrailMat.SetColor(PHigh, ArmedGold);
            sphereMesh = UvSphere(18, 28);   // built in code: CreatePrimitive drags in a SphereCollider, which Web builds strip
            dartMesh = CrossedDart(0.2f, 0.62f); shardMesh = CrossedDart(0.12f, 0.42f); beamMesh = AxisQuad(0, 1, 1); billboard = AxisQuad(-0.5f, 0.5f, 1);
            taperMesh = Taper(); helixMesh = Helix(16f, 24, 0.012f); colShell = TaperedTube(1.2f, 0.8f, 3f, 48);
            arcLights = new Light[3]; lightLeft = new float[3]; lightLife = new float[3]; lightPeak = new float[3];
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("Arcanist light " + i); go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>(); l.type = LightType.Point; l.shadows = LightShadows.None; l.enabled = false; arcLights[i] = l;
            }
        }
        static Material DotMat(Shader sh, Color low, Color high)
        {
            var m = new Material(sh); m.SetFloat(PMode, MDot); m.SetColor(PLow, low); m.SetColor(PHigh, high); m.SetFloat(PNoiseAmt, 0); return m;
        }

        // a sphere of radius 0.5 (like Unity's primitive) with normals and UVs
        static Mesh UvSphere(int lat, int lon)
        {
            var v = new Vector3[(lat + 1) * (lon + 1)]; var n = new Vector3[v.Length]; var uv = new Vector2[v.Length]; var t = new int[lat * lon * 6];
            for (int i = 0; i <= lat; i++)
                for (int j = 0; j <= lon; j++)
                {
                    float th = Mathf.PI * i / lat, ph = 2 * Mathf.PI * j / lon; int k = i * (lon + 1) + j;
                    n[k] = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph)); v[k] = n[k] * 0.5f;
                    uv[k] = new Vector2((float)j / lon, 1 - (float)i / lat);
                }
            int q = 0;
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    int a0 = i * (lon + 1) + j, b0 = a0 + lon + 1;
                    t[q++] = a0; t[q++] = a0 + 1; t[q++] = b0; t[q++] = a0 + 1; t[q++] = b0 + 1; t[q++] = b0;
                }
            var m = new Mesh { name = "FX_MS_Sphere", vertices = v, normals = n, uv = uv, triangles = t }; m.RecalculateBounds(); return m;
        }
        // a flat quad in local XZ: U along +Z from z0 to z1 (scaled by len), V across X (width 1)
        static Mesh AxisQuad(float z0, float z1, float len)
        {
            var m = new Mesh { name = "FX_MS_ArcQuad" };
            m.vertices = new[] { new Vector3(-0.5f, 0, z0 * len), new Vector3(0.5f, 0, z0 * len), new Vector3(-0.5f, 0, z1 * len), new Vector3(0.5f, 0, z1 * len) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0), new Vector2(1, 1) };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.triangles = new[] { 0, 2, 1, 1, 2, 3 }; m.RecalculateBounds(); return m;
        }
        // a dart from tail (U 0) to tip (U 1), widest at 70 %: two crossed quads so it reads from any side
        static Mesh CrossedDart(float width, float length)
        {
            var m = new Mesh { name = "FX_MS_Dart" };
            float z0 = -length * 0.7f, z1 = length * 0.3f, w = width / 2;
            m.vertices = new[] { new Vector3(-w, 0, z0), new Vector3(w, 0, z0), new Vector3(-w, 0, z1), new Vector3(w, 0, z1),
                                 new Vector3(0, -w, z0), new Vector3(0, w, z0), new Vector3(0, -w, z1), new Vector3(0, w, z1) };
            var uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0), new Vector2(1, 1) };
            m.uv = new[] { uv[0], uv[1], uv[2], uv[3], uv[0], uv[1], uv[2], uv[3] };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.right, Vector3.right, Vector3.right, Vector3.right };
            m.triangles = new[] { 0, 2, 1, 1, 2, 3, 4, 6, 5, 5, 6, 7 }; m.RecalculateBounds(); return m;
        }
        // a ribbon along +Z from a point (z 0) to full width 1 (z 1): tapered tails
        static Mesh Taper()
        {
            var m = new Mesh { name = "FX_MS_Taper" };
            m.vertices = new[] { new Vector3(-0.02f, 0, 0), new Vector3(0.02f, 0, 0), new Vector3(-0.5f, 0, 1), new Vector3(0.5f, 0, 1) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0), new Vector2(1, 1) };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.triangles = new[] { 0, 2, 1, 1, 2, 3 }; m.RecalculateBounds(); return m;
        }
        // a thin ribbon wound around the Z axis (radius 1, scale it), `turns` turns over `length` metres
        static Mesh Helix(float length, int turns, float width)
        {
            int n = turns * 16; var v = new Vector3[(n + 1) * 2]; var uv = new Vector2[v.Length]; var t = new int[n * 6];
            for (int i = 0; i <= n; i++)
            {
                float f = (float)i / n, a = f * turns * Mathf.PI * 2; var r = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                var tang = new Vector3(-Mathf.Sin(a), Mathf.Cos(a), 0); var half = tang * (width / 2 * 30);
                v[i * 2] = r + new Vector3(0, 0, f * length) - half; v[i * 2 + 1] = r + new Vector3(0, 0, f * length) + half;
                uv[i * 2] = new Vector2(f, 0); uv[i * 2 + 1] = new Vector2(f, 1);
                if (i < n) { int k = i * 6, b = i * 2; t[k] = b; t[k + 1] = b + 1; t[k + 2] = b + 2; t[k + 3] = b + 1; t[k + 4] = b + 3; t[k + 5] = b + 2; }
            }
            var m = new Mesh { name = "FX_MS_Helix", vertices = v, uv = uv, triangles = t }; m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }
        // an open tube from radius r0 at the floor to r1 at height h, with outward normals (for rim-lit shells)
        static Mesh TaperedTube(float r0, float r1, float h, int seg)
        {
            var v = new Vector3[(seg + 1) * 2]; var n = new Vector3[v.Length]; var uv = new Vector2[v.Length]; var t = new int[seg * 6];
            for (int i = 0; i <= seg; i++)
            {
                float f = (float)i / seg, a = f * Mathf.PI * 2; var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                v[i * 2] = d * r0; v[i * 2 + 1] = d * r1 + Vector3.up * h; n[i * 2] = n[i * 2 + 1] = d;
                uv[i * 2] = new Vector2(f, 0); uv[i * 2 + 1] = new Vector2(f, 1);
                if (i < seg) { int k = i * 6, b = i * 2; t[k] = b; t[k + 1] = b + 1; t[k + 2] = b + 2; t[k + 3] = b + 1; t[k + 4] = b + 3; t[k + 5] = b + 2; }
            }
            var m = new Mesh { name = "FX_MS_TaperedTube", vertices = v, normals = n, uv = uv, triangles = t }; m.RecalculateBounds(); return m;
        }
        // a line stroke in the XZ plane from a to b (local), U along it
        static Mesh LineQuad(Vector3 a, Vector3 b, float w)
        {
            var d = (b - a); var n = Vector3.Cross(Vector3.up, d.normalized) * (w / 2);
            var m = new Mesh { name = "FX_MS_Line" };
            m.vertices = new[] { a - n, a + n, b - n, b + n };
            m.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0), new Vector2(1, 1) };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.triangles = new[] { 0, 2, 1, 1, 2, 3 }; m.RecalculateBounds(); return m;
        }
        // a jagged crack of `segs` segments from `start` metres out along dir, narrowing toward its tip
        static Mesh Crack(Vector3 dir, float start, float len, int segs, float w)
        {
            var pts = new List<Vector3> { dir * start }; var side = Vector3.Cross(Vector3.up, dir);
            for (int i = 1; i <= segs; i++) pts.Add(dir * (start + len * i / segs) + side * Random.Range(-0.25f, 0.25f) * (i < segs ? 1 : 0.4f));
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int i = 0; i < pts.Count; i++)
            {
                var d = (i < pts.Count - 1 ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1]).normalized; var n = Vector3.Cross(Vector3.up, d) * (w / 2 * (1 - 0.6f * i / (pts.Count - 1)));
                v.Add(pts[i] - n); v.Add(pts[i] + n); float f = (float)i / (pts.Count - 1); uv.Add(new Vector2(f, 0)); uv.Add(new Vector2(f, 1));
                if (i < pts.Count - 1) { int b = i * 2; t.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 }); }
            }
            var m = new Mesh { name = "FX_MS_Crack" }; m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
        }
        // full ring with U around and V across the band
        static Mesh RingU(float inner, float outer, int seg = 96) => FxMeshes.Sector(inner, outer, 180f, seg, false, "FX_MS_RingU");
        // a ring broken into `dashes` arcs with small gaps (reads as a glyph, and its spin is visible)
        static Mesh DashedRing(float inner, float outer, int dashes, float gapDeg, int segPerDash = 16)
        {
            var parts = new CombineInstance[dashes]; float span = 360f / dashes;
            for (int k = 0; k < dashes; k++)
            {
                var arcM = FxMeshes.Sector(inner, outer, (span - gapDeg) / 2, segPerDash, false, "FX_MS_Dash");
                parts[k] = new CombineInstance { mesh = arcM, transform = Matrix4x4.Rotate(Quaternion.Euler(0, k * span, 0)) };
            }
            var m = new Mesh { name = "FX_MS_DashedRing" }; m.CombineMeshes(parts, true, true);
            foreach (var p in parts) Destroy(p.mesh);
            return m;
        }

        // the shared burst pool with ArcaneFlow defaults and the effect's own time in _T
        Burst Arc(Mesh mesh, bool owns, Material mat, Vector3 pos, Quaternion rot, float life, System.Action<Burst, float> anim, float mode, Color low, Color high, Color edge)
            => Play(mesh, owns, mat, pos, rot, life, (b, u) => { b.mpb.SetFloat(PT, b.t); b.mpb.SetFloat(PNoiseAmt, 0.4f); anim(b, u); }, mode, low, high, edge);
        Handle H(Burst b) => new Handle { b = b, id = b.id };
        void KillH(Handle h) { if (h != null) Kill(h.b, h.id); }
        System.Collections.IEnumerator KillAfter(Handle h, float s) { yield return new WaitForSeconds(s); KillH(h); }

        static Camera Cam => Camera.main;
        static Quaternion FaceCam(Vector3 pos)
        {
            var c = Cam; if (!c) return Quaternion.identity;
            return Quaternion.LookRotation(c.transform.up, -c.transform.forward);   // local Y toward the camera, X = screen right
        }
        static Quaternion AlongFacingCam(Vector3 pos, Vector3 dir)
        {
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
            var c = Cam; var toCam = c ? c.transform.position - pos : Vector3.up;
            var up = Vector3.ProjectOnPlane(toCam, dir); if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
            return Quaternion.LookRotation(dir.normalized, up.normalized);
        }
        static Quaternion Facing(Vector3 normal) => Quaternion.FromToRotation(Vector3.up, normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up);

        ArcState St(PlayerController p)
        {
            EnsureArc();
            if (!arc.TryGetValue(p, out var s))
            {
                s = new ArcState(); arc[p] = s;
                var vis = p.Presenter ? p.Presenter.Visual : null;
                if (vis)
                {
                    foreach (var t in vis.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name == "hand.R") s.hand = t;
                        if (t.name == "Astrolabe") { s.astrolabe = t; s.rig = t.GetComponent<AstrolabeRig>(); }
                    }
                    s.skins = vis.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                }
            }
            return s;
        }
        // the menu's showcase battle passes poses without a presenter, so hide/show go through these
        static void Hide(PlayerController p, bool on) { if (p && p.Presenter) p.Presenter.SetHidden(on); }
        static bool IsHidden(PlayerController p) => p && p.Presenter && p.Presenter.Hidden;
        Vector3 HandPos(PlayerController p) { var s = St(p); return s.hand && !IsHidden(p) ? s.hand.position + p.transform.forward * 0.08f : p.transform.position + ArcanistKit.HandOffset(p.transform); }
        Vector3 AstroPos(PlayerController p) { var s = St(p); return s.rig && !IsHidden(p) ? s.rig.CorePos : s.astrolabe && !IsHidden(p) ? s.astrolabe.position : p.transform.position + Vector3.up * 1.6f + p.transform.right * 0.5f - p.transform.forward * 0.1f; }
        // where the beam is drawn from: the astrolabe's globe (it hangs at the lens point), else 0.42 m past the palm
        Vector3 BeamRoot(PlayerController p, ArcState s, Vector3 dir) => s.rig && !IsHidden(p) ? s.rig.CorePos : HandPos(p) + dir * 0.42f;
        static Vector3 AimOf(PlayerController p) { var d = Sim.Flat(p.Last.Aim - p.transform.position); return d.sqrMagnitude > 0.1f ? d.normalized : p.transform.forward; }
        ArcanistKit Arcanist(PlayerController p) => p ? p.Kit as ArcanistKit : null;

        void ArcLight(Vector3 pos, Color c, float intensity, float range, float life)
        {
            EnsureArc(); int k = 0;
            for (int i = 1; i < arcLights.Length; i++) if (lightLeft[i] < lightLeft[k]) k = i;
            var l = arcLights[k]; l.transform.position = pos; l.color = new Color(Mathf.Clamp01(c.r / 2f), Mathf.Clamp01(c.g / 2f), Mathf.Clamp01(c.b / 2f));
            l.range = range; l.intensity = intensity; l.enabled = true; lightLeft[k] = lightLife[k] = life; lightPeak[k] = intensity;
        }

        // a pooled trail placed at `at` before it is shown, with its history cleared, so it never joins its last use
        TrailRenderer TakeTrail(float time, float width, Vector3 at, Material mat = null)
        {
            EnsureArc(); TrailRenderer tr = null;
            for (int i = 0; i < trailPool.Count; i++) if (trailPool[i] && !trailPool[i].gameObject.activeSelf) { tr = trailPool[i]; break; }
            if (tr == null)
            {
                var go = new GameObject("Arcanist trail"); go.transform.SetParent(transform, false); go.SetActive(false);
                tr = go.AddComponent<TrailRenderer>(); tr.minVertexDistance = 0.04f; tr.sharedMaterial = arcTrailMat;
                tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; tr.receiveShadows = false; tr.textureMode = LineTextureMode.Stretch;
                var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
                tr.colorGradient = g; trailPool.Add(tr);
            }
            tr.sharedMaterial = mat ? mat : arcTrailMat; tr.transform.position = at;
            tr.time = time; tr.widthMultiplier = width; tr.widthCurve = AnimationCurve.Linear(0, 1, 1, 0);
            tr.Clear(); tr.gameObject.SetActive(true); tr.Clear(); tr.emitting = true; return tr;
        }
        System.Collections.IEnumerator ReleaseTrail(TrailRenderer tr, float after)
        {
            if (!tr) yield break; tr.emitting = false; yield return new WaitForSeconds(after + 0.05f);
            if (tr) { tr.Clear(); tr.gameObject.SetActive(false); }
        }

        void ResetArcanist()
        {
            if (!arcReady) return;
            arc.Clear(); marks.Clear();
            foreach (var t in trailPool) if (t) { t.Clear(); t.gameObject.SetActive(false); }
            for (int i = 0; i < arcLights.Length; i++) { arcLights[i].enabled = false; lightLeft[i] = 0; }
            arcMotes.Clear(); arcSparks.Clear(); ashFx.Clear(); embers.Clear(); flameFx.Clear();
        }
        public void ArcanistReset(PlayerController p)
        {
            if (!arcReady) return;
            if (arc.TryGetValue(p, out var s))
            {
                KillH(s.glyph); foreach (var h in s.glyphArcs) KillH(h); foreach (var d in s.darts) KillH(d);
                foreach (var h in s.lens) KillH(h); foreach (var h in s.beam) KillH(h); foreach (var h in s.sockets) KillH(h); foreach (var h in s.surgeCast) KillH(h); foreach (var h in s.surge) KillH(h);
                for (int i = 0; i < s.glyphOrbs.Length; i++) KillGlyph(s, i);
                KillH(s.thread); if (s.rig) { s.rig.LensRelease(); s.rig.SurgeOff(); } arc.Remove(p);
            }
            foreach (var m in marks.Values) { foreach (var h in m.parts) KillH(h); if (m.comet) Destroy(m.comet.gameObject); }
            marks.Clear();
        }

        void LateUpdate()
        {
            if (!arcReady) return;
            float dt = Time.deltaTime;
            arcTrailMat.SetFloat(PT, Time.time); silverTrailMat.SetFloat(PT, Time.time); goldTrailMat.SetFloat(PT, Time.time);
            for (int i = 0; i < arcLights.Length; i++)
            {
                if (lightLeft[i] <= 0) { arcLights[i].enabled = false; continue; }
                lightLeft[i] -= dt; float u = 1 - Mathf.Clamp01(lightLeft[i] / lightLife[i]);
                arcLights[i].intensity = lightPeak[i] * (1 - u) * (1 - u);
            }
            // Disintegrate: energy stripping off the whole beam, harder at each step; at max charge a constant shake and
            // dark debris tearing off the boss
            foreach (var kv in arc)
            {
                var s = kv.Value; if (s.beam.Count == 0) continue;
                var bd = s.beamEnd - s.beamFrom; if (bd.sqrMagnitude < 0.01f) continue; var bn = bd.normalized;
                s.shedAcc += dt * (s.beamStep == 1 ? 25 : s.beamStep == 2 ? 55 : 130);
                while (s.shedAcc >= 1)
                {
                    s.shedAcc -= 1; var at = s.beamFrom + bd * Random.Range(0.08f, 1f);
                    var perp = Vector3.ProjectOnPlane(Random.onUnitSphere, bn).normalized;
                    var v = perp * Random.Range(1.5f, 2.5f + 1.5f * s.beamStep) + bn * Random.Range(1f, 4f);
                    if (Random.value < 0.5f) embers.Emit(at, v, 0.05f + 0.02f * s.beamStep, Random.Range(0.15f, 0.35f));
                    else arcMotes.Emit(at, v * 0.6f, 0.06f + 0.02f * s.beamStep, Random.Range(0.2f, 0.4f));
                }
                if (s.beamStep == 3)
                {
                    if (Time.time >= s.shakeAt) { s.shakeAt = Time.time + 0.1f; CameraShake.Shake(0.07f, 0.12f); }
                    if (s.beamHit && Time.time >= s.debrisAt)
                    {
                        s.debrisAt = Time.time + 0.06f; var from = s.beamEnd; var v = (-bn + Random.insideUnitSphere * 0.9f + Vector3.up * 0.6f).normalized * Random.Range(3f, 6f);
                        float size = Random.Range(0.18f, 0.32f); var spin = Random.Range(-720f, 720f);
                        Arc(shardMesh, false, arcAlpha, from, Quaternion.LookRotation(v), 0.6f, (b, u) =>
                        {
                            float t = u * 0.6f; b.go.transform.position = from + v * t + Vector3.down * 4.9f * t * t;
                            b.go.transform.rotation = Quaternion.LookRotation(v) * Quaternion.Euler(spin * t, spin * 0.5f * t, 0); b.go.transform.localScale = new Vector3(size * 3, size * 3, size * 1.4f);
                            b.mpb.SetFloat(PDissolve, Mathf.Clamp01((u - 0.3f) / 0.7f)); b.mpb.SetFloat(PIntensity, 0.95f); b.mpb.SetFloat(PEdgeGlow, 1.6f);
                        }, MDart, new Color(0.05f, 0.02f, 0.07f), new Color(0.12f, 0.05f, 0.15f), AshMagenta);
                    }
                }
            }
            // Disintegrate: ash flakes and magenta embers where the beam meets the boss (and for 0.3 s after it ends)
            foreach (var kv in arc)
            {
                var s = kv.Value; bool on = (s.beam.Count > 0 && s.beamHit) || Time.time < s.ashTail; if (!on) continue;
                s.ashAcc += dt * (s.beamStep == 1 ? 16 : s.beamStep == 2 ? 24 : 40);
                var at = ContactFront(s); var back = (s.beamFrom - s.beamEnd); back.y = 0; back = back.sqrMagnitude > 1e-4f ? back.normalized : Vector3.back;
                var side = Vector3.Cross(Vector3.up, back);
                while (s.ashAcc >= 1)
                {
                    s.ashAcc -= 1;
                    var v = back * Random.Range(0.8f, 1.5f) + side * Random.Range(-1.0f, 1.0f) + Vector3.up * Random.Range(0.2f, 0.7f);
                    ashFx.Emit(at + Random.insideUnitSphere * 0.25f, v, Random.Range(0.15f, 0.24f), Random.Range(0.8f, 1.2f));
                    if (Random.value < 0.2f) arcSparks.Emit(at, back * 3.5f + Random.insideUnitSphere * 2.5f + Vector3.up, 0.05f, 0.14f);
                }
            }
            // comets in the sky: parked out of view, then a steep plunge over the last `window` seconds that speeds up all the
            // way (t^2.6); embers peel off sideways while it falls
            foreach (var m in marks.Values)
            {
                if (!m.comet) continue;
                float left = m.life - (Time.time - m.start), k = Mathf.Clamp01(1 - left / Mathf.Max(0.01f, m.window));
                var end = m.pos + Vector3.up * 0.9f;
                m.comet.position = Vector3.LerpUnclamped(m.from, end, Mathf.Pow(k, 2.6f));
                if (k <= 0) continue;
                float ws = m.weak ? 0.65f : 1f;
                m.emberAcc += dt * 50;
                var fd = (end - m.from).normalized;
                while (m.emberAcc >= 1)
                {
                    m.emberAcc -= 1; var side = Vector3.Cross(fd, Vector3.up).normalized * (Random.value < 0.5f ? -1 : 1);
                    embers.Emit(m.comet.position - fd * 1.2f * ws + Random.onUnitSphere * 0.9f * ws, side * Random.Range(2f, 4f) - fd * 4f + Vector3.up, Random.Range(0.14f, 0.24f), Random.Range(0.3f, 0.5f));
                }
                // fire: soft violet puffs torn off the back half of the rock, streaming back along the fall and burning out fast
                m.fireAcc += dt * 90;
                while (m.fireAcc >= 1)
                {
                    m.fireAcc -= 1; var o = Random.onUnitSphere; if (Vector3.Dot(o, fd) > 0.2f) o -= fd * 1.2f;
                    flameFx.Emit(m.comet.position + o.normalized * 1.05f * ws, -fd * Random.Range(5f, 10f) + o * 1.5f, Random.Range(0.8f, 1.5f) * ws, Random.Range(0.18f, 0.3f));
                }
                // dark smoke left hanging along the path, lit violet in the middle by the fire
                m.smokeAcc += dt * 14;
                while (m.smokeAcc >= 1) { m.smokeAcc -= 1; Smoke(m.comet.position - fd * 2.6f * ws + Random.insideUnitSphere * 0.7f, Random.insideUnitSphere * 0.8f - fd * 1.5f, Random.Range(1.2f, 1.6f) * ws, Random.Range(2.4f, 3.0f) * ws, Random.Range(0.5f, 0.65f), 0.85f); }
            }
        }

        // ================================================================== E1 Magic Missile
        public void MissileTapCast(PlayerController p, Vector3 from, Vector3 dir)
        {
            EnsureArc(); var at = HandPos(p);
            // a 0.25 m glyph disc at the palm and a short muzzle streak along the shot
            Arc(RingU(0.085f, 0.095f, 32), true, arcAdd, at, FaceCam(at), 0.1f, (b, u) =>
            {
                b.go.transform.localScale = Vector3.one * Mathf.Lerp(0.8f, 1.4f, Ease(u)); b.mpb.SetFloat(PDissolve, u * u); b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PIntensity, 1.3f);
            }, MBeam, ArcLilac, HotWhite, ArcLilac);
            Arc(TickRing(0.1f, 0.14f, 3, 0.02f), true, arcAdd, at, FaceCam(at) * Quaternion.Euler(0, Random.value * 120, 0), 0.1f, (b, u) =>
            {
                b.go.transform.localScale = Vector3.one * Mathf.Lerp(0.8f, 1.3f, Ease(u)); b.mpb.SetFloat(PProgress, 1.1f); b.mpb.SetFloat(PWidth, 0.07f); b.mpb.SetFloat(PDissolve, u * u); b.mpb.SetFloat(PIntensity, 1.3f);
            }, MStroke, ArcLilac, HotWhite, ArcLilac);
            Arc(beamMesh, false, arcAdd, at, AlongFacingCam(at, dir), 0.1f, (b, u) =>
            {
                b.go.transform.localScale = new Vector3(0.05f, 1, 0.3f * (1 - 0.5f * u)); b.mpb.SetFloat(PWidth, 0.4f); b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 1.1f);
            }, MBeam, ArcLilac, ArcLilac * 1.3f, AshMagenta);
            for (int i = 0; i < 4; i++) arcMotes.Emit(at, dir * 1.5f + Random.insideUnitSphere * 0.8f, 0.08f, 0.18f);
        }

        // the glyph ring at the casting hand (0.36 m): one arc per dart (3, or 5 in Surge), each lighting as its dart is
        // summoned, the newest with a pulse; the unlit arcs stay faint so the ring visibly fills by thirds
        public void MissileChargeStart(PlayerController p)
        {
            var s = St(p); KillH(s.glyph); s.glyph = null; foreach (var h in s.glyphArcs) KillH(h); s.glyphArcs.Clear(); s.glyphFill = 0; s.glyphPulse = -9;
            var kit = Arcanist(p); int cap = kit != null ? kit.MissileCap : 3; bool gold = kit != null && kit.Surging;
            float span = 360f / cap, gap = cap > 3 ? 16 : 22;
            for (int k = 0; k < cap; k++)
            {
                int kk = k;
                var b = Arc(FxMeshes.Sector(0.19f, 0.225f, (span - gap) / 2, 14, false, "FX_MS_GlyphArc"), true, arcAdd, HandPos(p), Quaternion.identity, 9999, (bb, time) =>
                {
                    if (!p) return;
                    int lit = Mathf.RoundToInt(s.glyphFill * cap); bool on = kk < lit, newest = kk == lit - 1;
                    float pulse = newest ? Mathf.Exp(-(Time.time - s.glyphPulse) * 14) : 0;
                    var hp = HandPos(p) + Vector3.up * 0.05f; bb.go.transform.SetPositionAndRotation(hp, FaceCam(hp) * Quaternion.Euler(0, 180 - time * 40 + kk * span, 0));
                    bb.go.transform.localScale = Vector3.one * Ease(Mathf.Clamp01(time / 0.1f)) * (1 + 0.3f * pulse);
                    bb.mpb.SetFloat(PWidth, 0.55f);
                    bb.mpb.SetFloat(PIntensity, on ? 1.4f + 0.9f * pulse : 0.3f);
                }, MBeam, gold ? DeepGold : ArcLilac, gold ? ArmedGold : HotWhite, AshMagenta);
                s.glyphArcs.Add(H(b));
            }
            s.glyphArcs.Add(H(Arc(RingU(0.195f, 0.205f, 48), true, arcAdd, HandPos(p), Quaternion.identity, 9999, (bb, time) =>
            {
                if (!p) return; var hp = HandPos(p) + Vector3.up * 0.05f; bb.go.transform.SetPositionAndRotation(hp, FaceCam(hp));
                bb.go.transform.localScale = Vector3.one * Ease(Mathf.Clamp01(time / 0.1f)); bb.mpb.SetFloat(PWidth, 0.6f); bb.mpb.SetFloat(PIntensity, 0.3f);
            }, MBeam, ArcLilac, ArcLilac, AshMagenta)));
        }

        public void MissileSummon(PlayerController p, int n)
        {
            var s = St(p); var kit = Arcanist(p); int cap = kit != null ? kit.MissileCap : 3; s.glyphFill = n / (float)cap; s.glyphPulse = Time.time;
            int i = n - 1; if (i < 0 || i >= s.darts.Length) return;
            KillH(s.darts[i]);
            var slot = HoverSlot(p, i, cap);
            for (int k = 0; k < 10; k++) { var off = Random.onUnitSphere * 0.6f; arcMotes.Emit(slot + off, -off / 0.12f, 0.07f, 0.12f); }
            int idx = i; bool gold = cap > 3;
            var b = Arc(dartMesh, false, arcOver, slot, Quaternion.identity, 9999, (bb, time) =>
            {
                if (!p) return;
                var pos = HoverSlot(p, idx, cap) + Vector3.up * 0.03f * Mathf.Sin(time * 5 + idx * 2.1f);
                var dir = (p.Last.Aim + Vector3.up * 1.2f - pos);
                bb.go.transform.SetPositionAndRotation(pos, AlongFacingCam(pos, dir.sqrMagnitude > 1e-4f ? dir : Vector3.forward));
                bb.go.transform.localScale = Vector3.one * 0.62f * Ease(Mathf.Clamp01(time / 0.12f));
                bb.mpb.SetFloat(PIntensity, 1.0f); bb.mpb.SetFloat(PNoiseAmt, 0f);
            }, MDart, gold ? SurgeDart : ArcLilac, gold ? ArmedGold : HotWhite, AshMagenta);
            s.darts[i] = H(b);
            ArcLight(slot, ArcLilac, 0.5f, 2.5f, 0.12f);
        }

        // where a summoned dart hovers: its kit slot (where it launches from) pushed 0.3 m back and spread 1.4x wider, so the
        // fan sits behind his shoulder clear of the astrolabe; the launch jump of about 0.3 m is hidden by the flight speed
        Vector3 HoverSlot(PlayerController p, int i, int cap)
        {
            var t = p.transform; float f = cap <= 1 ? 0 : i / (cap - 1f) - 0.5f;   // -0.5 .. 0.5 across the fan
            float a = f * (cap > 3 ? 90f : 60f) * Mathf.Deg2Rad, r = cap > 3 ? 0.5f : 0.45f;
            // a 60 degree arc (90 in Surge) behind his right shoulder, just behind and around the astrolabe
            return t.position + Vector3.up * (1.6f + r * Mathf.Cos(a)) + t.right * (0.55f + r * Mathf.Sin(a)) - t.forward * 0.5f;
        }

        public void MissileChargeCancel(PlayerController p)
        {
            var s = St(p); var hand = HandPos(p);
            for (int i = 0; i < s.darts.Length; i++)
            {
                var d = s.darts[i]; if (d == null || !d.Alive(this)) continue;
                var from = d.b.go.transform.position; var rot = d.b.go.transform.rotation; KillH(d); s.darts[i] = null;
                // the collapse plays where the darts were, so it still reads beside a Blink
                var inward = hand - from;
                Arc(dartMesh, false, arcOver, from, rot, 0.25f, (bb, u) =>
                {
                    bb.go.transform.position = from + inward * Ease(Mathf.Clamp01(u / 0.7f)); bb.go.transform.localScale = Vector3.one * 0.62f * (1 - u * u);
                    bb.mpb.SetFloat(PDissolve, u * 0.5f); bb.mpb.SetFloat(PIntensity, 1.0f);
                }, MDart, ArcLilac, HotWhite, AshMagenta);
                for (int k = 0; k < 2; k++) arcMotes.Emit(from, inward / 0.15f + Random.insideUnitSphere * 0.3f, 0.06f, 0.15f);
            }
            foreach (var h in s.glyphArcs)
            {
                if (!h.Alive(this)) continue;
                var at = h.b.go.transform.position; var rot = h.b.go.transform.rotation; var mesh = h.b.mf.sharedMesh; h.b.owns = false; KillH(h);
                var outward = rot * Vector3.forward * 0.15f; float tw = Random.Range(-40f, 40f);
                Arc(mesh, true, arcOver, at, rot, 0.25f, (bb, u) =>
                {
                    bb.go.transform.position = at + outward * u + Vector3.down * 0.35f * u * u; bb.go.transform.rotation = rot * Quaternion.Euler(u * 30, 0, u * tw);
                    bb.mpb.SetFloat(PDissolve, u); bb.mpb.SetFloat(PWidth, 0.55f); bb.mpb.SetFloat(PIntensity, 1.5f);
                }, MBeam, ArcLilac, HotWhite, ArcLilac);
            }
            s.glyphArcs.Clear();
            s.glyph = null;
        }

        public void MissileVolley(PlayerController p, int n)
        {
            var s = St(p); var at = HandPos(p);
            // the ring flashes full, then erodes; one thin ring is thrown out from it
            foreach (var h in s.glyphArcs)
            {
                if (!h.Alive(this)) continue; var gb = h.b; float t0 = gb.t;
                gb.life = gb.t + 0.14f; gb.anim = (b, u) => { float k = Mathf.Clamp01((b.t - t0) / 0.14f); b.mpb.SetFloat(PIntensity, 1.8f * (1 - k)); b.mpb.SetFloat(PDissolve, k * k); };
            }
            s.glyphArcs.Clear(); s.glyph = null;
            Arc(RingU(0.17f, 0.19f, 64), true, arcAdd, at, Facing(AimOf(p)), 0.18f, (b, u) =>
            {
                b.go.transform.localScale = Vector3.one * (1 + 2.6f * Ease(u)); b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PIntensity, 1.1f);
            }, MBeam, ArcLilac, HotWhite, AshMagenta);
            ArcLight(at, ArcLilac, 1.2f, 4f, 0.15f);
        }
        // a hovering dart hands over to its projectile (the darts launch 0.05 s apart)
        readonly Vector3[] launchFrom = new Vector3[5]; readonly float[] launchAt = { -9, -9, -9, -9, -9 };
        public void MissileLaunch(PlayerController p, int i)
        {
            var s = St(p); if (i < 0 || i >= s.darts.Length) return;
            if (s.darts[i] != null && s.darts[i].Alive(this)) { launchFrom[i] = s.darts[i].b.go.transform.position; launchAt[i] = Time.time; }
            KillH(s.darts[i]); s.darts[i] = null;
        }

        // a missile in flight: the dart drawn between ticks (extrapolated from the last tick), two short weaving ribbons
        public void Missile(ProjectileSystem.Projectile pr, bool charged, int index = 1, bool surged = false)
        {
            EnsureArc();
            float tickAt = Time.time; Vector3 lastPos = pr.Pos;
            float phase = Random.value * 6.28f;
            // a charged dart leaves from where it hovered: the gap to its launch line closes over 0.1 s
            var kick = charged && index >= 0 && index < 5 && Time.time - launchAt[index] < 0.05f ? launchFrom[index] - pr.Pos : Vector3.zero;
            var b = Arc(dartMesh, false, arcOver, pr.Pos, Quaternion.LookRotation(pr.Vel), 9999, (bb, time) =>
            {
                float since = Mathf.Min(Time.time - tickAt, Sim.Dt * 1.5f);
                var pos = lastPos + pr.Vel * since + kick * (1 - Ease(Mathf.Clamp01(time / 0.1f)));
                var side = Vector3.Cross(Vector3.up, pr.Vel.normalized); var weave = side * 0.05f * Mathf.Sin(time * 56.5f + phase);   // about 9 Hz
                bb.go.transform.SetPositionAndRotation(pos + weave, Quaternion.LookRotation(pr.Vel) * Quaternion.Euler(0, 0, time * 360));
                bb.go.transform.localScale = Vector3.one * 1.15f;
                bb.mpb.SetFloat(PIntensity, 1.1f); bb.mpb.SetFloat(PNoiseAmt, 0f);
            }, MDart, surged ? SurgeDart : ArcLilac, surged ? ArmedGold : HotWhite, AshMagenta);   // empowered by Arcane Surge: a gold rim, gold tip, gold trails
            var h = H(b);
            float tt = (charged ? 0.03f : 0.024f) * (surged ? 1.8f : 1f);
            var t1 = TakeTrail(tt * 1.3f, surged ? 0.18f : 0.14f, pr.Pos, surged ? goldTrailMat : null); var t2 = TakeTrail(tt * 1.05f, 0.09f, pr.Pos + Vector3.up * 0.04f, surged ? goldTrailMat : null);
            pr.OnMove = q =>
            {
                tickAt = Time.time; lastPos = q.Pos;
                var side = Vector3.Cross(Vector3.up, q.Vel.normalized);
                float w = Mathf.Sin(Time.time * 56 + phase) * 0.06f;
                if (t1) t1.transform.position = q.Pos + side * w; if (t2) t2.transform.position = q.Pos - side * w + Vector3.up * 0.03f;
                if (Random.value < 0.35f) arcMotes.Emit(q.Pos - q.Vel.normalized * 0.2f, Random.insideUnitSphere * 0.3f, 0.05f, 0.25f);
            };
            pr.OnEnd = (q, hit, at) =>
            {
                KillH(h);
                StartCoroutine(ReleaseTrail(t1, tt * 1.3f)); StartCoroutine(ReleaseTrail(t2, tt * 1.05f));
                // charged hits land 0.2 m apart across the boss (and toward the caster), so three pops read as three
                var side = Vector3.Cross(Vector3.up, q.Vel.normalized);
                // charged hits after the first are 75 % size and spread 0.3 m apart, so three to five pops read as separate
                if (hit) MissileImpact(at + (charged ? side * (index - 1) * 0.3f + Vector3.up * 0.15f * (index % 2) : Vector3.zero) - q.Vel.normalized * 0.3f, q.Vel, charged && index > 0 ? 0.75f : 1f, surged);
                else MissileFizzle(q.Pos, q.Vel);
            };
        }

        // contact (0.18 s): a small bright core, six thin rays (0.3 m) from a small gap, one thin ring opening to 0.9 m, a
        // tiny rune flashing at the centre, four rune shards thrown out; gold edges when surged
        void MissileImpact(Vector3 at, Vector3 vel, float sc = 1, bool surged = false)
        {
            var rot = FaceCam(at); var back = -vel.normalized; var hot = surged ? ArmedGold : ArcLilac * 1.3f;
            Arc(billboard, false, arcAdd, at, rot, 0.06f, (b, u) =>
            {
                b.go.transform.localScale = Vector3.one * 0.55f * sc; b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 1.0f);
            }, MDot, ArcLilac, HotWhite, AshMagenta);
            for (int k = 0; k < 2; k++)
            {
                var r = rot * Quaternion.Euler(0, 45 + 90 * k, 0);
                Arc(beamMesh, false, arcAdd, at - r * Vector3.forward * 0.13f * sc, r, 0.07f, (b, u) =>
                {
                    b.go.transform.localScale = new Vector3(0.05f * sc, 1, 0.26f * sc); b.mpb.SetFloat(PWidth, 0.35f); b.mpb.SetFloat(PIntensity, 1.4f * (1 - u));
                }, MBeam, HotWhite, HotWhite, HotWhite);
            }
            for (int k = 0; k < 6; k++)
            {
                var r = rot * Quaternion.Euler(0, k * 60 + Random.Range(-10f, 10f), 0);
                Arc(beamMesh, false, arcAdd, at + r * Vector3.forward * 0.06f * sc, r, 0.16f, (b, u) =>
                {
                    float t = u * 0.16f; b.go.transform.localScale = new Vector3(0.075f * sc, 1, 0.3f * sc * Mathf.Lerp(0.4f, 1f, Ease(Mathf.Clamp01(t / 0.04f))));
                    b.mpb.SetFloat(PWidth, 0.45f); b.mpb.SetFloat(PDissolve, Mathf.Clamp01((t - 0.08f) / 0.08f)); b.mpb.SetFloat(PIntensity, 1.1f);
                }, MBeam, ArcLilac, hot, AshMagenta);
            }
            Arc(RingU(0.43f, 0.45f, 64), true, arcAdd, at, rot, 0.18f, (b, u) =>
            {
                b.go.transform.localScale = Vector3.one * sc * Mathf.Lerp(0.35f, 1f, Ease(Mathf.Clamp01(u / 0.6f)));
                b.mpb.SetFloat(PWidth, 0.7f); b.mpb.SetFloat(PDissolve, Mathf.Clamp01((u - 0.45f) / 0.55f)); b.mpb.SetFloat(PIntensity, 1.1f);
            }, MBeam, surged ? DeepGold : ArcLilac, hot, AshMagenta);
            var glyph = Glyph(0.2f * sc, 0.03f, out var glyphGlow); Destroy(glyphGlow);
            Arc(glyph, true, arcAdd, at, rot, 0.14f, (b, u) =>
            {
                b.go.transform.localScale = Vector3.one * Mathf.Lerp(0.7f, 1.1f, Ease(u));
                b.mpb.SetFloat(PProgress, 1.1f); b.mpb.SetFloat(PWidth, 0.07f); b.mpb.SetFloat(PDissolve, Mathf.Clamp01((u - 0.35f) / 0.65f)); b.mpb.SetFloat(PIntensity, 1.2f);
            }, MStroke, ArcLilac, hot, AshMagenta);
            for (int k = 0; k < 6; k++)
            {
                float a = k * 60 * Mathf.Deg2Rad; var d = rot * new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                arcMotes.Emit(at + d * 0.3f * sc, d * Random.Range(1.2f, 2f), 0.05f, 0.2f);
            }
            // rune shards: tiny glyph strokes thrown 0.4-0.6 m out and falling
            for (int k = 0; k < 4; k++)
            {
                var v = (back + Random.insideUnitSphere * 0.9f + Vector3.up * 0.6f).normalized * Random.Range(2.2f, 3.2f); var spin = Random.Range(-720f, 720f);
                var piece = LineQuad(new Vector3(0, 0, -0.06f), new Vector3(0, 0, 0.06f), 0.03f);
                Arc(piece, true, arcAdd, at, rot, 0.24f, (b, u) =>
                {
                    float t = u * 0.24f; var q = at + v * t + Vector3.down * 4.9f * t * t;
                    b.go.transform.SetPositionAndRotation(q, FaceCam(q) * Quaternion.Euler(0, spin * t, 0));
                    b.mpb.SetFloat(PProgress, 1.1f); b.mpb.SetFloat(PWidth, 0.07f); b.mpb.SetFloat(PDissolve, Mathf.Clamp01((u - 0.5f) / 0.5f)); b.mpb.SetFloat(PIntensity, 1.4f);
                }, MStroke, ArcLilac, hot, AshMagenta);
            }
            ArcLight(at, ArcLilac, 1.2f * sc, 3f, 0.1f);
        }
        void MissileFizzle(Vector3 at, Vector3 vel)
        {
            Arc(dartMesh, false, arcAdd, at, Quaternion.LookRotation(vel), 0.15f, (b, u) =>
            {
                b.go.transform.localScale = Vector3.one * (1 - 0.7f * u); b.mpb.SetFloat(PDissolve, u);
            }, MDart, DeepViolet, ArcLilac, AshMagenta);
        }

        // ================================================================== E2 Disintegrate
        // widths per damage step: step 3 (max charge) is a much heavier beam
        // the contact drawn on the boss's armour: the beam end is on his drawn model (SurfaceEnd), so the scorch and ash sit
        // a hair off it toward the caster and 0.3 m toward the camera, which keeps them out of the armour without moving
        // them on screen
        Vector3 ContactFront(ArcState s)
        {
            var back = s.beamFrom - s.beamEnd; back.y = 0; back = back.sqrMagnitude > 1e-4f ? back.normalized : Vector3.back;
            return s.beamEnd + back * 0.06f - (Cam ? Cam.transform.forward * 0.3f : Vector3.zero);
        }

        // The visible contact. The gameplay hit test uses the boss's round hit body, which can sit a metre in front of his
        // armour, so the drawn beam stopped in mid-air. Presentation only: trace the beam's line onto his drawn model (a
        // pose snapshot refreshed 10 times a second) and end the beam there. Damage, hits and damage numbers are unchanged.
        sealed class BossSurface
        {
            public BossEncounter boss; public float bakedAt = -9;
            public readonly List<Piece> pieces = new List<Piece>();
            public sealed class Piece { public Renderer r; public bool skinned; public Mesh baked; public readonly List<Vector3> v = new List<Vector3>(); public int[] t; public Matrix4x4 toBoss; public Bounds box; }
        }
        BossSurface bossSurface;
        BossSurface SurfaceOf(Vector3 near)
        {
            var bs = bossSurface;
            if (bs == null || !bs.boss || !bs.boss.Solid)
            {
                BossEncounter best = null; float bd = float.MaxValue;
                foreach (var b in UnityEngine.Object.FindObjectsByType<BossEncounter>(FindObjectsSortMode.None))
                {
                    if (!b.Solid) continue; float d = (b.transform.position - near).sqrMagnitude;
                    if (d < bd) { bd = d; best = b; }
                }
                if (bs != null) foreach (var pc in bs.pieces) if (pc.baked) Destroy(pc.baked);
                bossSurface = null; if (!best) return null;
                bs = bossSurface = new BossSurface { boss = best };
                foreach (var r in best.GetComponentsInChildren<Renderer>(true))
                    if (r is SkinnedMeshRenderer || (r is MeshRenderer && r.GetComponent<MeshFilter>())) bs.pieces.Add(new BossSurface.Piece { r = r, skinned = r is SkinnedMeshRenderer });
            }
            if (Time.time - bs.bakedAt >= 0.1f) { bs.bakedAt = Time.time; BakeSurface(bs); }
            return bs;
        }
        // only his solid, visible body counts: hidden parts, glows and other see-through layers are skipped
        static void BakeSurface(BossSurface bs)
        {
            var w2b = bs.boss.transform.worldToLocalMatrix;
            foreach (var pc in bs.pieces)
            {
                var r = pc.r; var m = r ? r.sharedMaterial : null;
                if (!r || !r.enabled || !r.gameObject.activeInHierarchy || !m || m.renderQueue >= 2450) { pc.v.Clear(); continue; }
                try
                {
                    if (pc.skinned)
                    {
                        var sk = (SkinnedMeshRenderer)r; if (!sk.sharedMesh) { pc.v.Clear(); continue; }
                        if (!pc.baked) pc.baked = new Mesh { name = "FX_MS_BossSurface" };
                        sk.BakeMesh(pc.baked, true); pc.baked.GetVertices(pc.v);
                        if (pc.t == null) pc.t = pc.baked.triangles;
                        pc.baked.RecalculateBounds(); pc.box = pc.baked.bounds;
                        pc.toBoss = w2b * Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one);
                    }
                    else
                    {
                        var mesh = r.GetComponent<MeshFilter>().sharedMesh; if (!mesh || !mesh.isReadable) { pc.v.Clear(); continue; }
                        if (pc.t == null) { mesh.GetVertices(pc.v); pc.t = mesh.triangles; pc.box = mesh.bounds; }
                        pc.toBoss = w2b * r.transform.localToWorldMatrix;
                    }
                    pc.box.Expand(0.05f);
                }
                catch (System.Exception) { pc.v.Clear(); pc.t = null; }
            }
        }
        // the nearest point where o + d*t (d a unit vector, 0.05 < t < maxT) meets his drawn model, either side of a face
        static bool TraceSurface(BossSurface bs, Vector3 o, Vector3 d, float maxT, out float hitT)
        {
            hitT = maxT; bool any = false; var b2w = bs.boss.transform.localToWorldMatrix;
            foreach (var pc in bs.pieces)
            {
                if (pc.v.Count == 0 || pc.t == null) continue;
                var inv = (b2w * pc.toBoss).inverse; var lo = inv.MultiplyPoint3x4(o); var ld = inv.MultiplyVector(d);
                if (ld.sqrMagnitude < 1e-12f || !pc.box.IntersectRay(new Ray(lo, ld))) continue;
                var v = pc.v; var t = pc.t; int n = v.Count;
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    int i0 = t[i], i1 = t[i + 1], i2 = t[i + 2]; if (i0 >= n || i1 >= n || i2 >= n) continue;
                    var a = v[i0]; var e1 = v[i1] - a; var e2 = v[i2] - a;
                    var pv = Vector3.Cross(ld, e2); float det = Vector3.Dot(e1, pv); if (det > -1e-10f && det < 1e-10f) continue;
                    float id = 1 / det; var tv = lo - a; float u = Vector3.Dot(tv, pv) * id; if (u < 0 || u > 1) continue;
                    var qv = Vector3.Cross(tv, e1); float w = Vector3.Dot(ld, qv) * id; if (w < 0 || u + w > 1) continue;
                    float tt = Vector3.Dot(e2, qv) * id; if (tt > 0.05f && tt < hitT) { hitT = tt; any = true; }
                }
            }
            return any;
        }
        // where the drawn beam ends: its own line onto his model; if that slips past him (between the legs, over a
        // shoulder), toward his centre line at the contact height, then a little above and below; else the hit-body point
        Vector3 SurfaceEnd(Vector3 from, Vector3 end)
        {
            var bs = SurfaceOf(end); if (bs == null) return end;
            var bp = bs.boss.transform.position; float reach = bs.boss.BodyRadius * 2 + 0.6f;
            for (int k = 0; k < 4; k++)
            {
                var aim = k == 0 ? end : new Vector3(bp.x, end.y + (k == 2 ? 0.4f : k == 3 ? -0.4f : 0), bp.z);
                var d = aim - from; float len = d.magnitude; if (len < 0.1f) continue; d /= len;
                if (TraceSurface(bs, from, d, len + reach, out float t)) return from + d * t;
            }
            return end;
        }
        static float SheathW(int step) => step == 1 ? 0.22f : step == 2 ? 0.32f : 0.46f;
        static float CoreW(int step) => step == 1 ? 0.035f : step == 2 ? 0.05f : 0.075f;
        public void BeamWindup(PlayerController p, int ticks)
        {
            var s = St(p); foreach (var h in s.lens) KillH(h); s.lens.Clear();
            float life = ticks / (float)Sim.Hz;
            for (int k = 0; k < 3; k++)
            {
                int ring = k;
                var mesh = ring == 0 ? DashedRing(0.315f, 0.34f, 4, 14) : ring == 1 ? DashedRing(0.235f, 0.255f, 4, 18) : DashedRing(0.06f, 0.12f, 3, 40, 6);
                var b = Arc(mesh, true, arcAdd, HandPos(p), Quaternion.identity, 9999, (bb, time) =>
                {
                    if (!p) return;
                    var kk = Arcanist(p); bool beam = kk != null && kk.BeamActive;
                    var aim = beam ? kk.BeamDir : AimOf(p); int step = beam ? kk.BeamStep : 0;
                    var at = HandPos(p) + aim * 0.42f;
                    // the astrolabe is the lens: it hangs at the lens point facing the beam and the rings form around it
                    if (s.rig) { if (ring == 0) s.rig.Lens(at, aim, step); at = s.rig.CorePos; }
                    float grow = Ease(Mathf.Clamp01(time / life));
                    float rate = (ring == 1 ? -540f : ring == 0 ? 360f : 200f) * (1 + 0.5f * step);
                    bb.go.transform.SetPositionAndRotation(at, Facing(aim) * Quaternion.Euler(0, rate * time, 0));
                    float pulse = Mathf.Exp(-(Time.time - s.stepPulse) * 14);
                    bb.go.transform.localScale = Vector3.one * grow * (1 + 0.18f * pulse);
                    bb.mpb.SetFloat(PWidth, 0.6f); bb.mpb.SetFloat(PIntensity, 1.3f * (0.8f + 0.12f * step + 0.6f * pulse));   // +30%: faint at gameplay distance
                }, MBeam, Ultraviolet, UVCore, AshMagenta);
                s.lens.Add(H(b));
            }
            var hand = HandPos(p);
            for (int k = 0; k < 12; k++) { var off = Random.onUnitSphere * 0.65f; arcMotes.Emit(hand + off, -off / (life * 0.9f), 0.12f, life * 0.9f); }
        }

        public void BeamStart(PlayerController p)
        {
            var s = St(p); foreach (var h in s.beam) KillH(h); s.beam.Clear();
            s.beamStart = Time.time; s.ashAcc = 0; s.shedAcc = 0; s.beamStep = 1;
            // layers: 0 void sheath (dark, alpha), 1-2 ultraviolet rim lines on the sheath's edges, 3 bright core.
            // Turbulent, not tidy: the sheath's edges are eaten by fast noise, the core flickers in width, and at max charge
            // the whole beam is far heavier.
            for (int k = 0; k < 4; k++)
            {
                int layer = k;
                var mat = layer == 0 ? arcAlpha : arcAdd;
                Color low = layer == 0 ? Void : layer == 3 ? BeamCore : Ultraviolet, high = layer == 0 ? Void : layer == 3 ? BeamHot : new Color(0.9f, 0.4f, 2.0f);
                var b = Arc(beamMesh, false, mat, s.beamFrom, Quaternion.identity, 9999, (bb, time) =>
                {
                    if (!p) return;
                    var dir = (s.beamEnd - s.beamFrom).normalized; var from = BeamRoot(p, s, dir); var d = s.beamEnd - from; float len = Mathf.Max(0.1f, d.magnitude);
                    int step = s.beamStep; float sheath = SheathW(step), core = CoreW(step);
                    float snap = Ease(Mathf.Clamp01(time / 0.035f));
                    float pulse = Mathf.Exp(-(Time.time - s.stepPulse) * 10);   // a surge down the beam at each step
                    float jitter = step == 3 ? Random.Range(0.85f, 1.2f) : step == 2 ? Random.Range(0.93f, 1.08f) : 1;
                    var rot = AlongFacingCam(from, d); var pos = from;
                    if (layer == 1 || layer == 2) pos += rot * Vector3.right * (layer == 1 ? -1 : 1) * sheath * 0.5f * jitter;
                    bb.go.transform.SetPositionAndRotation(pos, rot);
                    float w = layer == 0 ? sheath : layer == 3 ? core * 2.4f : 0.045f + 0.015f * step;
                    bb.go.transform.localScale = new Vector3(w * jitter * (1 + 0.6f * pulse), 1, len * snap);
                    bb.mpb.SetFloat(PWidth, layer == 0 ? 0.95f : layer == 3 ? 0.4f : 0.5f);
                    // the void sheath is near-opaque so the beam reads dark ("unmaking"); the core is a thin violet-white thread
                    bb.mpb.SetFloat(PIntensity, layer == 0 ? 1.0f : layer == 3 ? (1.3f + 0.15f * step) + 0.55f * pulse : (0.28f + 0.08f * step) + 0.3f * pulse);
                    float flow = 2.2f + 1.6f * step;
                    bb.mpb.SetVector(PFlow, new Vector4(-flow, 0.3f * step, -flow * 0.6f, -0.4f * step)); bb.mpb.SetVector(PNoiseScale, new Vector4(Mathf.Max(1.5f, len * (0.35f + 0.25f * step)), 3 + step, 0, 0));
                    bb.mpb.SetFloat(PNoiseAmt, layer == 3 ? 0.2f + 0.1f * step : 0.35f + 0.15f * step);
                }, MBeam, low, high, AshMagenta);
                s.beam.Add(H(b));
            }
            // two thin ultraviolet strands twisting around the beam (smooth helices, front half bright, back half dim); they
            // wind tighter and turn faster at each step
            for (int k = 0; k < 2; k++)
            {
                int idx = k; var mesh = new Mesh { name = "FX_MS_BeamStrand" }; mesh.MarkDynamic();
                var b = Arc(mesh, true, arcAdd, Vector3.zero, Quaternion.identity, 9999, (bb, time) =>
                {
                    if (!p) return;
                    bb.go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    int step = s.beamStep; var dir = (s.beamEnd - s.beamFrom).normalized;
                    float snap = Ease(Mathf.Clamp01(time / 0.035f)); var from = BeamRoot(p, s, dir);
                    HelixStrand(mesh, from, Vector3.Lerp(from, s.beamEnd, snap), SheathW(step) * 0.5f + 0.02f, 1.25f - 0.1f * step, idx * Mathf.PI * 0.85f - time * (9 + 4 * step), 0.016f + 0.003f * step);
                    bb.mpb.SetFloat(PWidth, 0.5f); bb.mpb.SetFloat(PIntensity, 0.85f + 0.12f * step); bb.mpb.SetFloat(PNoiseAmt, 0.2f);
                }, MBeam, Ultraviolet, new Color(1.0f, 0.55f, 2.1f), AshMagenta);
                s.beam.Add(H(b));
            }
            // contact scorch (dark, alpha) that follows the contact point while the beam hits
            var sc = Arc(billboard, false, arcAlpha, s.beamEnd, Quaternion.identity, 9999, (bb, time) =>
            {
                var q = ContactFront(s); bb.go.transform.SetPositionAndRotation(q, FaceCam(q) * Quaternion.Euler(0, time * 40, 0));
                bb.go.transform.localScale = Vector3.one * (s.beamHit ? 0.32f + 0.08f * s.beamStep : 0.001f);
                bb.mpb.SetFloat(PWidth, 0.45f); bb.mpb.SetFloat(PIntensity, 0.95f); bb.mpb.SetFloat(PNoiseAmt, 0.85f); bb.mpb.SetVector(PNoiseScale, new Vector4(5, 5, 0, 0)); bb.mpb.SetFloat(PDissolve, 0.25f); bb.mpb.SetFloat(PSoft, 0.3f);
            }, MShadow, new Color(0.04f, 0.0f, 0.07f), Void, new Color(0.45f, 0.1f, 0.6f));
            s.beam.Add(H(sc));
            var hand = HandPos(p);
            Arc(RingU(0.55f, 0.6f, 48), true, arcAdd, hand + AimOf(p) * 0.42f, Facing(AimOf(p)), 0.15f, (b, u) =>
            {
                b.go.transform.localScale = Vector3.one * Mathf.Lerp(0.3f, 1.2f, Ease(u)); b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 1.4f);
            }, MBeam, Ultraviolet, UVCore, AshMagenta);
            ArcLight(hand, Ultraviolet, 1.4f, 4f, 0.2f);
            CameraShake.Shake(0.08f, 0.12f);
        }
        // a jagged camera-facing line from a to b, offsets tapering to nothing at both ends, written in world space
        static void JaggedArc(Mesh mesh, Vector3 a, Vector3 b, float amp, float width)
        {
            const int N = 16; var d = b - a; if (d.sqrMagnitude < 1e-4f) return; var dn = d.normalized;
            var cam = Cam; var toCam = cam ? (cam.transform.position - (a + b) * 0.5f).normalized : Vector3.up;
            var side = Vector3.Cross(dn, toCam).normalized; var up = Vector3.Cross(side, dn).normalized;
            var v = new Vector3[(N + 1) * 2]; var uv = new Vector2[v.Length]; var t = new int[N * 6];
            for (int i = 0; i <= N; i++)
            {
                float f = (float)i / N, taper = Mathf.Sin(f * Mathf.PI);
                var pt = a + d * f + (side * Random.Range(-1f, 1f) + up * Random.Range(-1f, 1f) * 0.6f) * amp * taper;
                v[i * 2] = pt - side * width * 0.5f; v[i * 2 + 1] = pt + side * width * 0.5f;
                uv[i * 2] = new Vector2(f, 0); uv[i * 2 + 1] = new Vector2(f, 1);
                if (i < N) { int k = i * 6, q = i * 2; t[k] = q; t[k + 1] = q + 2; t[k + 2] = q + 1; t[k + 3] = q + 1; t[k + 4] = q + 2; t[k + 5] = q + 3; }
            }
            mesh.Clear(); mesh.vertices = v; mesh.uv = uv; mesh.triangles = t; mesh.RecalculateBounds();
        }

        // a thin camera-facing ribbon wound around the line a -> b (radius, pitch in metres per turn), tapering in at both ends;
        // vertex colour dims the half of each turn that passes behind the beam, so it reads as twisting
        static void HelixStrand(Mesh mesh, Vector3 a, Vector3 b, float radius, float pitch, float phase, float width)
        {
            var d = b - a; float len = d.magnitude; if (len < 0.05f) { mesh.Clear(); return; }
            var dn = d / len; var cam = Cam; var toCam = cam ? (cam.transform.position - (a + b) * 0.5f).normalized : Vector3.up;
            var side = Vector3.Cross(dn, toCam); if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(dn, Vector3.up); side.Normalize();
            var up = Vector3.Cross(side, dn).normalized;
            int n = Mathf.Clamp(Mathf.CeilToInt(len / 0.07f), 8, 260);
            var pts = new Vector3[n + 1]; var fr = new float[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float f = (float)i / n, ang = phase + f * len / pitch * Mathf.PI * 2;
                float taper = Mathf.Clamp01(f * len / 0.35f) * Mathf.Clamp01((1 - f) * len / 0.2f);
                pts[i] = a + d * f + (side * Mathf.Cos(ang) + up * Mathf.Sin(ang)) * radius * taper; fr[i] = Mathf.Sin(ang);
            }
            var v = new Vector3[(n + 1) * 2]; var uv = new Vector2[v.Length]; var col = new Color[v.Length]; var t = new int[n * 6];
            for (int i = 0; i <= n; i++)
            {
                var tan = (pts[Mathf.Min(n, i + 1)] - pts[Mathf.Max(0, i - 1)]).normalized; var w = Vector3.Cross(tan, toCam).normalized * width * 0.5f;
                v[i * 2] = pts[i] - w; v[i * 2 + 1] = pts[i] + w; float f = (float)i / n; uv[i * 2] = new Vector2(f, 0); uv[i * 2 + 1] = new Vector2(f, 1);
                float lit = Mathf.Lerp(0.3f, 1f, 0.5f + 0.5f * fr[i]); col[i * 2] = col[i * 2 + 1] = new Color(lit, lit, lit, lit);
                if (i < n) { int k = i * 6, q = i * 2; t[k] = q; t[k + 1] = q + 2; t[k + 2] = q + 1; t[k + 3] = q + 1; t[k + 4] = q + 2; t[k + 5] = q + 3; }
            }
            mesh.Clear(); mesh.vertices = v; mesh.uv = uv; mesh.colors = col; mesh.triangles = t; mesh.RecalculateBounds();
        }

        public void Beam(PlayerController p, Vector3 from, Vector3 dir, Vector3 end, int step, bool hit)
        {
            var s = St(p);
            if (hit) end = SurfaceEnd(from, end);   // drawn on his armour, not his hit body (presentation only)
            if (step > s.beamStep && s.beam.Count > 0)
            {
                s.stepPulse = Time.time; var lensAt = s.rig ? s.rig.CorePos : HandPos(p) + dir * 0.42f;
                bool max = step == 3;
                Arc(RingU(0.47f, 0.5f, 48), true, arcAdd, lensAt, Facing(dir), max ? 0.2f : 0.12f, (b, u) =>
                {
                    b.go.transform.localScale = Vector3.one * Mathf.Lerp(0.8f, max ? 2.4f : 1.4f, Ease(u)); b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, max ? 1.6f : 1.2f);
                }, MBeam, AshMagenta, UVCore, AshMagenta);
                ArcLight(lensAt, Ultraviolet, max ? 2.4f : 1.4f, max ? 6f : 4f, 0.2f);
                CameraShake.Shake(max ? 0.28f : 0.12f, max ? 0.22f : 0.14f);
                if (hit)
                {
                    // a burst at the contact as the beam surges
                    for (int k = 0; k < (max ? 30 : 14); k++) embers.Emit(end, (Random.insideUnitSphere + (from - end).normalized * 0.8f) * (max ? 7f : 4f), 0.09f, 0.3f);
                    var cf = ContactFront(s);
                    Arc(billboard, false, arcAdd, cf, FaceCam(cf), 0.1f, (b, u) =>
                    {
                        b.go.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, max ? 1.4f : 1.0f, u); b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 0.8f);
                    }, MDot, Ultraviolet, new Color(0.9f, 0.4f, 2.0f), AshMagenta);
                }
            }
            s.beamFrom = from; s.beamEnd = end; s.beamStep = step; s.beamHit = hit;
        }
        public void BeamEnd(PlayerController p, bool broken)
        {
            var s = St(p);
            var from = HandPos(p); var to = s.beamEnd; var d = to - from; float len = d.magnitude;
            foreach (var h in s.beam) KillH(h); s.beam.Clear();
            if (s.beamHit) s.ashTail = Time.time + 0.3f;
            if (!broken)
            {
                // the sheath shrinks away and the core thins to a 0.015 m thread over 0.15 s, then cuts
                float w0 = CoreW(s.beamStep) * 2.2f, sh0 = SheathW(s.beamStep);
                var endRot = AlongFacingCam(from, d);
                Arc(beamMesh, false, arcAlpha, from, endRot, 0.18f, (b, u) =>
                {
                    b.go.transform.localScale = new Vector3(Mathf.Lerp(sh0, 0.001f, Ease(u)), 1, len); b.mpb.SetFloat(PWidth, 0.95f); b.mpb.SetFloat(PIntensity, 1.0f * (1 - u));
                }, MBeam, Void, Void, AshMagenta);
                Arc(beamMesh, false, arcAdd, from, endRot, 0.21f, (b, u) =>
                {
                    b.go.transform.localScale = new Vector3(Mathf.Lerp(w0, 0.03f, Ease(Mathf.Clamp01(u / 0.88f))), 1, len); b.mpb.SetFloat(PWidth, 0.45f);
                    b.mpb.SetFloat(PIntensity, u < 0.88f ? 1.1f : 0);
                }, MBeam, BeamCore, BeamHot, AshMagenta);
            }
            else
            {
                // broken: the whole beam (sheath and core) stutters twice over 0.13 s, then breaks into five pieces that
                // keep their width and erode where they were over 0.2 s
                float sh0 = SheathW(s.beamStep), c0 = CoreW(s.beamStep) * 2.2f;
                System.Func<float, float> stutter = t => t < 0.033f ? 1 : t < 0.066f ? 0.2f : t < 0.1f ? 1 : t < 0.133f ? 0.2f : 1;
                for (int k = 0; k < 5; k++)
                {
                    float a = k / 5f + (k == 0 ? 0 : 0.02f), bb2 = (k + 1) / 5f - 0.02f; var p0 = from + d * a; float seg = len * (bb2 - a);
                    for (int layer = 0; layer < 2; layer++)
                    {
                        bool sheathLayer = layer == 0; float full = k == 0 ? 0 : 1;
                        Arc(beamMesh, false, sheathLayer ? arcAlpha : arcAdd, p0, AlongFacingCam(p0, d), 0.34f, (b, u) =>
                        {
                            float t = u * 0.34f, broke = t < 0.133f ? 0 : Mathf.Clamp01((t - 0.133f) / 0.2f);
                            // before the break the pieces join into the whole beam; after it the gaps open
                            float l = t < 0.133f ? seg + len * 0.04f * full : seg * (1 - 0.25f * broke);
                            b.go.transform.localScale = new Vector3((sheathLayer ? sh0 : c0) * (1 - 0.3f * broke), 1, l);
                            b.mpb.SetFloat(PWidth, sheathLayer ? 0.75f : 0.45f); b.mpb.SetFloat(PDissolve, broke);
                            b.mpb.SetFloat(PIntensity, (sheathLayer ? 1.0f : 1.1f) * stutter(t));
                        }, MBeam, sheathLayer ? Void : BeamCore, sheathLayer ? Void : BeamHot, AshMagenta);
                    }
                }
            }
            LensApart(p, broken);
        }
        public void BeamCancel(PlayerController p)
        {
            LensApart(p, true); var hand = HandPos(p);
            for (int k = 0; k < 12; k++) arcSparks.Emit(hand, Random.onUnitSphere * 2.5f, 0.05f, 0.25f);
        }
        void LensApart(PlayerController p, bool thrown)
        {
            var s = St(p);
            if (s.rig) { s.rig.LensRelease(); if (thrown) { s.rig.Pulse(0.5f, 9f); s.rig.Apart(); } }
            for (int i = 0; i < s.lens.Count; i++)
            {
                var h = s.lens[i]; if (!h.Alive(this)) continue;
                var at = h.b.go.transform.position; var rot = h.b.go.transform.rotation; var mesh = h.b.mf.sharedMesh; var sc = h.b.go.transform.localScale;
                h.b.owns = false; KillH(h);
                var axis = rot * Vector3.up; float dirSign = i == 0 ? 1 : i == 1 ? -1 : 0.4f;
                var side = rot * (i == 1 ? Vector3.left : Vector3.right); int ii = i;   // in-plane drift, opposite for the two big rings
                // thrown (broken / cancelled): fly apart over 0.25 s (about +0.4 m wider, 1440 deg/s, drifting in opposite
                // directions) and fade; a normal end: shrink back into the astrolabe over 0.2 s
                Arc(mesh, true, arcAdd, at, rot, thrown ? 0.25f : 0.2f, (b, u) =>
                {
                    float t = u * (thrown ? 0.25f : 0.2f);
                    b.go.transform.position = thrown ? at + (axis * dirSign * 1.6f + side * (ii == 2 ? 0f : 1.2f)) * t : Vector3.Lerp(at, p ? AstroPos(p) : at, Ease(u));
                    b.go.transform.localScale = thrown ? sc * (1 + 1.2f * Ease(u)) : sc * (1 - u);
                    b.go.transform.Rotate(0, Time.deltaTime * (thrown ? 1440 : 720) * (ii == 1 ? -1 : 1), 0, Space.Self);
                    b.mpb.SetFloat(PDissolve, thrown ? u : u * 0.5f); b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PIntensity, thrown ? 1.1f : 0.8f);
                }, MBeam, Ultraviolet, UVCore, AshMagenta);
            }
            s.lens.Clear();
        }

        // ================================================================== rune drawing (Rune Sigil, Arcane Comet mark)
        // A magic circle drawn stroke by stroke over `life` seconds: an outer ring traced clockwise over the whole time (the
        // timer, with a hot head), a second ring traced the other way, a band of procedural glyphs between them, an inner
        // ring, a hexagram, six spokes, a centre circle and a centre star. Every stroke is a bright core line over a soft
        // wide glow, with energy flowing along it (scrolling noise). Before it completes it arms from the centre out in
        // gold. Line meshes are kept on the mark so the burnt rune can be left behind.
        void DrawRune(MarkFx m, Vector3 c, float R, float life, int glyphCount, float line, Color ink, Color hot, Color armedCol, bool powered = true, float powerAt = 0)
        {
            // a powered rune is drawn faint until its glyph lands (powerAt seconds in), then lights to full over 0.1 s
            float Pw(float time) => !powered ? 0 : powerAt <= 0 ? 1 : Mathf.Clamp01((time - powerAt) / 0.1f);
            float Land(float time) => !powered || powerAt <= 0 || time < powerAt ? 0 : 1 - Mathf.Clamp01((time - powerAt) / 0.3f);   // gold edges for 0.3 s after the glyph lands
            Color Ink(float time) => Color.Lerp(FaintInk, ink, Pw(time)); Color Hot(float time) => Color.Lerp(Color.Lerp(FaintHot, hot, Pw(time)), ArmedGold, Land(time));
            float Dim(float time) => !powered ? 0.95f : Mathf.Lerp(0.55f, 1f, Pw(time));
            // cast without a glyph: a faint grey-violet sketch at under half brightness, no glyph band, no hexagram, no gold
            if (!powered) { ink = FaintInk; hot = FaintHot; armedCol = FaintHot; glyphCount = 0; m.weak = true; line *= 1.3f; }
            m.strokes = new List<(Vector3, Vector3)>(); m.rings = new List<float>(); m.glyphMeshes = new List<Mesh>(); m.glyphPose = new List<(Vector3, Quaternion)>(); m.line = line;
            // armBefore: seconds before the end when this part turns gold (centre first)
            void Part(Mesh core, Mesh glow, Quaternion rot, Vector3 scale, float t0, float t1, float armBefore, float glowScale = 1)
            {
                if (glow != null)
                {
                    var ub = Arc(Instantiate(glow), true, arcAlphaUnder, c + Vector3.up * 0.001f, rot, 9999, (bb, time) =>
                    {
                        float u = time / life, prog = Mathf.Clamp01((u - t0) / Mathf.Max(0.001f, t1 - t0)); float armed = Mathf.Clamp01((time - (life - armBefore)) / 0.05f);
                        bb.mpb.SetFloat(PProgress, prog * 1.03f); bb.mpb.SetFloat(PWidth, 0.3f); bb.mpb.SetFloat(PNoiseAmt, 0.1f);
                        bb.mpb.SetFloat(PIntensity, (m.snuffAt > 0 ? 0 : 1) * (0.3f + 0.35f * armed));
                    }, MStroke, Void, Void, Void);
                    ub.go.transform.localScale = scale; m.parts.Add(H(ub));
                }
                for (int g = 0; g < 2; g++)
                {
                    bool isGlow = g == 1; var mesh = isGlow ? glow : core; if (mesh == null) continue;
                    var b = Arc(mesh, true, arcAddFloor, c + Vector3.up * (isGlow ? 0.002f : 0.004f), rot, 9999, (bb, time) =>
                    {
                        float u = time / life, prog = Mathf.Clamp01((u - t0) / Mathf.Max(0.001f, t1 - t0));
                        float armed = Mathf.Clamp01((time - (life - armBefore)) / 0.05f);
                        bb.mpb.SetFloat(PProgress, prog * 1.03f); bb.mpb.SetFloat(PWidth, isGlow ? 0.2f : 0.07f);
                        float sn = m.snuffAt > 0 ? Mathf.Clamp01((Time.time - m.snuffAt) / 0.12f) : 0;
                        bb.mpb.SetColor(PLow, Color.Lerp(Color.Lerp(Ink(time), armedCol, armed), BurntGrey, sn)); bb.mpb.SetColor(PHigh, Color.Lerp(Color.Lerp(Hot(time), armedCol, armed), BurntGrey, sn));
                        float breathe = 0.9f + 0.1f * Mathf.Sin(time * 9 + t0 * 20);
                        bb.mpb.SetFloat(PDissolve, sn * 0.9f);
                        bb.mpb.SetFloat(PIntensity, (1 - 0.7f * sn) * Dim(time) * (isGlow ? (0.2f - 0.1f * armed) * glowScale * (1 - sn) : (1.25f + 0.2f * armed) * breathe));   // armed: gold lines, not a filled glow
                        bb.mpb.SetFloat(PNoiseAmt, isGlow ? 0.35f : 0.5f);
                        bb.mpb.SetVector(PFlow, new Vector4(-2.4f, 0, -1.1f, 0.3f)); bb.mpb.SetVector(PNoiseScale, new Vector4(7, 2, 0, 0));
                    }, MStroke, ink, hot, AshMagenta);
                    b.go.transform.localScale = scale;
                    m.parts.Add(H(b));
                }
            }
            void Ring(float r, float w, float t0, float t1, float armBefore, bool clockwise, float yaw)
            {
                m.rings.Add(r);
                Part(RingU(r - w, r, 160), RingU(r - w * 1.9f, r + w * 0.9f, 160), Quaternion.Euler(0, yaw, 0), new Vector3(clockwise ? -1 : 1, 1, 1), t0, t1, armBefore);
            }
            void Line(Vector3 a, Vector3 b, float w, float t0, float t1, float armBefore)
            {
                m.strokes.Add((a, b)); Part(LineQuad(a, b, w), LineQuad(a, b, w * 2.8f), Quaternion.identity, Vector3.one, t0, t1, armBefore, 0.8f);
            }
            float arm = Mathf.Clamp(life * 0.18f, 0.12f, 0.2f);   // the gold wave runs over the last 18 % (0.12-0.2 s, so it shows even in Surge)
            if (!powered)
                m.parts.Add(H(Arc(RingU(R - line, R, 160), true, arcAddFloor, c + Vector3.up * 0.003f, Quaternion.identity, 9999, (b, time) =>
                {
                    b.mpb.SetFloat(PProgress, 1.1f); b.mpb.SetFloat(PWidth, 0.07f); b.mpb.SetFloat(PIntensity, m.snuffAt > 0 ? 0 : 0.18f);
                }, MStroke, FaintInk, FaintInk, FaintInk)));
            Ring(R, line * 0.95f, 0f, 0.97f, arm * 0.25f, true, m.ringYaw);
            Ring(R * 0.9f, line * 0.6f, 0.04f, 0.7f, arm * 0.35f, false, m.ringYaw + 90);
            Ring(R * 0.68f, line * 0.75f, 0.1f, 0.6f, arm * 0.55f, true, m.ringYaw + 45);
            Ring(R * 0.3f, line * 0.75f, 0.4f, 0.75f, arm * 0.95f, false, m.ringYaw + 10);
            // hexagram inscribed in the inner ring
            for (int k = 0; k < (powered ? 6 : 0); k++)
            {
                int tri = k % 2; float a0 = (tri * 60 + (k / 2) * 120) * Mathf.Deg2Rad, a1 = a0 + 120 * Mathf.Deg2Rad; float r = R * 0.68f;
                var A = new Vector3(Mathf.Sin(a0), 0, Mathf.Cos(a0)) * r; var B = new Vector3(Mathf.Sin(a1), 0, Mathf.Cos(a1)) * r;
                float t0 = 0.18f + 0.06f * k; Line(A, B, line, t0, t0 + 0.14f, arm * 0.75f);
            }
            // six spokes from the hexagram's points out to the second ring
            for (int k = 0; k < 6; k++)
            {
                float a = k * 60 * Mathf.Deg2Rad; var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                float t0 = 0.45f + 0.025f * k; Line(d * R * 0.68f, d * R * 0.9f, line * 0.8f, t0, t0 + 0.08f, arm * 0.45f);
            }
            // centre star
            for (int k = 0; k < 4; k++)
            {
                float a = (k * 45 + 22.5f) * Mathf.Deg2Rad; var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * R * 0.27f;
                float t0 = 0.6f + 0.03f * k; Line(-d, d, line * 0.8f, t0, t0 + 0.08f, arm);
            }
            // the glyph band between the outer two rings and the inner ring: procedural 3-4 stroke runes on a 3x3 grid
            float gr = R * 0.79f, gs = R * 0.13f;
            for (int i = 0; i < glyphCount; i++)
            {
                float a = (i + 0.5f) / glyphCount * Mathf.PI * 2; var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                var core = Glyph(gs, line * 0.75f, out var glow);
                var rot = Quaternion.LookRotation(d); float t0 = 0.2f + 0.5f * i / glyphCount;
                m.glyphMeshes.Add(core); m.glyphPose.Add((c + d * gr, rot)); m.glyphSize = gs;
                PartAt(core, glow, c + d * gr, rot, t0, t0 + 0.07f, arm * 0.6f);
            }
            void PartAt(Mesh core, Mesh glow, Vector3 at, Quaternion rot, float t0, float t1, float armBefore)
            {
                for (int g = 0; g < 2; g++)
                {
                    bool isGlow = g == 1; var mesh = isGlow ? glow : core;
                    var b = Arc(mesh, true, arcAddFloor, at + Vector3.up * (isGlow ? 0.002f : 0.004f), rot, 9999, (bb, time) =>
                    {
                        float u = time / life, prog = Mathf.Clamp01((u - t0) / (t1 - t0)); float armed = Mathf.Clamp01((time - (life - armBefore)) / 0.05f);
                        bb.mpb.SetFloat(PProgress, prog * 1.03f); bb.mpb.SetFloat(PWidth, 0.07f);
                        float sn = m.snuffAt > 0 ? Mathf.Clamp01((Time.time - m.snuffAt) / 0.12f) : 0;
                        bb.mpb.SetColor(PLow, Color.Lerp(Color.Lerp(Ink(time), armedCol, armed), BurntGrey, sn)); bb.mpb.SetColor(PHigh, Color.Lerp(Color.Lerp(Hot(time), armedCol, armed), BurntGrey, sn));
                        bb.mpb.SetFloat(PDissolve, sn * 0.9f);
                        bb.mpb.SetFloat(PIntensity, (1 - 0.7f * sn) * Dim(time) * (isGlow ? (0.26f - 0.12f * armed) * (1 - sn) : 1.2f + 0.15f * armed)); bb.mpb.SetFloat(PNoiseAmt, 0.4f);
                        bb.mpb.SetVector(PFlow, new Vector4(-2.4f, 0, -1.1f, 0.3f)); bb.mpb.SetVector(PNoiseScale, new Vector4(4, 2, 0, 0));
                    }, MStroke, ink, hot, AshMagenta);
                    m.parts.Add(H(b));
                }
            }
            // the outer ring's advancing head: a hot spark shedding motes (the countdown at gameplay distance)
            m.parts.Add(H(Arc(billboard, false, arcAdd, c, Quaternion.identity, 9999, (b, time) =>
            {
                float u = time / life; float a = -(Mathf.Min(u, 0.97f) / 0.97f) * Mathf.PI * 2 + (180 - m.ringYaw) * Mathf.Deg2Rad;
                var h = c + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * (R - line * 0.7f) + Vector3.up * 0.06f;
                b.go.transform.SetPositionAndRotation(h, FaceCam(h)); b.go.transform.localScale = Vector3.one * Mathf.Max(0.7f, R * 0.2f) * (0.9f + 0.15f * Mathf.Sin(time * 40));
                b.mpb.SetFloat(PIntensity, u < 0.97f && m.snuffAt < 0 ? 1.5f * Dim(time) : 0);
                if (u < 0.97f && Random.value < 0.8f) arcMotes.Emit(h, Vector3.up * 1.5f + Random.insideUnitSphere * 0.25f, 0.11f, 0.4f);
                // once armed, motes rise from all over the circle
                if (time > life - arm && Random.value < 0.9f) { var rc = Random.insideUnitCircle.normalized; var q = c + new Vector3(rc.x, 0, rc.y) * R * Random.Range(0.3f, 1f); arcMotes.Emit(q, Vector3.up * 2.2f, 0.11f, 0.4f); }
            }, MDot, Color.Lerp(ink, HotWhite, 0.5f), HotWhite, AshMagenta)));
            // the head's tail: a short bright arc just behind the head along the ring
            m.parts.Add(H(Arc(RingU(R - line * 1.3f, R + line * 0.2f, 24), true, arcAddFloor, c + Vector3.up * 0.006f, Quaternion.identity, 9999, (b, time) =>
            {
                float u = time / life, a = (Mathf.Min(u, 0.97f) / 0.97f) * 360f;
                // RingU spans the full circle along U; show only the last 30 degrees behind the head
                b.go.transform.rotation = Quaternion.Euler(0, m.ringYaw, 0); b.go.transform.localScale = new Vector3(-1, 1, 1);
                b.mpb.SetFloat(PProgress, a / 360f * 1.0f); b.mpb.SetFloat(PTail, 0.025f); b.mpb.SetFloat(PWidth, 0.004f);
                b.mpb.SetFloat(PIntensity, u < 0.97f && m.snuffAt < 0 ? 1.6f * Dim(time) : 0);
            }, MBand, ink, HotWhite, AshMagenta)));
        }
        // `count` short radial ticks between radii r0 and r1, as one mesh
        static Mesh TickRing(float r0, float r1, int count, float w)
        {
            var parts = new CombineInstance[count];
            for (int k = 0; k < count; k++)
            {
                float a = k * Mathf.PI * 2 / count; var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                parts[k] = new CombineInstance { mesh = LineQuad(d * r0, d * r1, w), transform = Matrix4x4.identity };
            }
            var m = new Mesh { name = "FX_MS_Ticks" }; m.CombineMeshes(parts, true, true);
            foreach (var ci in parts) Destroy(ci.mesh);
            return m;
        }
        // a rune glyph: 3-4 strokes between points of a 3x3 grid (size g), as one mesh, plus its soft glow
        static Mesh Glyph(float g, float w, out Mesh glow) { var m = GlyphSet(g, w, out glow, out var e); Destroy(e); return m; }
        // a held-glyph rune, g tall, in local XZ with +Z as "up" (FaceCam turns local Z to screen up): a stem, a hook off its
        // top, a cross-tick and sometimes a foot, so none reads as an arrow; core, soft glow and a wider (gold) edge stroke
        static Mesh RuneGlyph(float g, float w, out Mesh glow, out Mesh edge, float edgeW = 2.1f)
        {
            float h = g * 0.5f, side = Random.value < 0.5f ? -1 : 1;
            var segs = new List<(Vector3, Vector3)>();
            float sx = Random.Range(-0.06f, 0.06f) * g;
            segs.Add((new Vector3(sx, 0, -h), new Vector3(sx, 0, h)));                                         // stem
            segs.Add((new Vector3(sx, 0, h), new Vector3(sx + side * g * 0.32f, 0, h - g * Random.Range(0.18f, 0.3f))));   // hook
            float ty = Random.Range(-0.2f, 0.1f) * g, tilt = Random.Range(-0.12f, 0.12f) * g;
            segs.Add((new Vector3(sx - g * 0.26f, 0, ty - tilt), new Vector3(sx + g * 0.26f, 0, ty + tilt)));      // cross-tick
            if (Random.value < 0.6f) segs.Add((new Vector3(sx, 0, -h), new Vector3(sx - side * g * 0.22f, 0, -h + g * 0.16f)));   // foot
            // each stroke's U runs over its own share of 0..1, so a Stroke progress of 0 -> 1 draws them one after another
            Mesh Build(float width, string name)
            {
                var ci = new CombineInstance[segs.Count];
                for (int k = 0; k < segs.Count; k++)
                {
                    var q = LineQuad(segs[k].Item1, segs[k].Item2, width); var uv = q.uv;
                    for (int j = 0; j < uv.Length; j++) uv[j].x = (k + uv[j].x) / segs.Count;
                    q.uv = uv; ci[k] = new CombineInstance { mesh = q, transform = Matrix4x4.identity };
                }
                var m = new Mesh { name = name }; m.CombineMeshes(ci, true, true); foreach (var c in ci) Destroy(c.mesh); return m;
            }
            glow = Build(w * 3.2f, "FX_MS_RuneGlow"); edge = Build(w * edgeW, "FX_MS_RuneEdge");
            return Build(w, "FX_MS_Rune");
        }
        // the same rune as core, soft glow and a wider edge stroke (drawn gold behind the violet core)
        static Mesh GlyphSet(float g, float w, out Mesh glow, out Mesh edge)
        {
            var pts = new List<Vector3>(); for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++) pts.Add(new Vector3(x, 0, z) * g * 0.5f);
            int n = Random.Range(3, 5); var core = new CombineInstance[n]; var halo = new CombineInstance[n]; var rim = new CombineInstance[n]; int last = Random.Range(0, 9);
            for (int k = 0; k < n; k++)
            {
                int next; do { next = Random.Range(0, 9); } while (next == last || (pts[next] - pts[last]).magnitude > g * 0.75f);
                core[k] = new CombineInstance { mesh = LineQuad(pts[last], pts[next], w), transform = Matrix4x4.identity };
                halo[k] = new CombineInstance { mesh = LineQuad(pts[last], pts[next], w * 4f), transform = Matrix4x4.identity };
                rim[k] = new CombineInstance { mesh = LineQuad(pts[last], pts[next], w * 2.4f), transform = Matrix4x4.identity };
                last = Random.value < 0.7f ? next : Random.Range(0, 9);
            }
            var m = new Mesh { name = "FX_MS_Glyph" }; m.CombineMeshes(core, true, true);
            glow = new Mesh { name = "FX_MS_GlyphGlow" }; glow.CombineMeshes(halo, true, true);
            edge = new Mesh { name = "FX_MS_GlyphEdge" }; edge.CombineMeshes(rim, true, true);
            foreach (var ci in core) Destroy(ci.mesh); foreach (var ci in halo) Destroy(ci.mesh); foreach (var ci in rim) Destroy(ci.mesh);
            return m;
        }
        // the burnt rune left on the floor after it fires: dim violet-grey lines that erode away
        void BurntRune(MarkFx m, Vector3 c, float life, float line, float kb = 1, bool flash = true, float hold = 0)
        {
            System.Action<Burst, float> burnt = (b, u) => { float t = u * life; b.mpb.SetFloat(PEdgeGlow, 0f); b.mpb.SetFloat(PProgress, 1.1f); b.mpb.SetFloat(PWidth, 0.07f); b.mpb.SetFloat(PDissolve, 0.05f + 0.9f * Mathf.Clamp01((t - hold) / (life - hold))); b.mpb.SetFloat(PIntensity, 0.55f); };
            var ash = Color.Lerp(BurntGrey, RuneViolet, 0.35f);
            System.Action<Burst, float> ember = (b, u) =>
            {
                float t = u * life, lit = flash && t < 0.06f ? 1 : 0; b.mpb.SetFloat(PEdgeGlow, 0f);
                if (!flash)
                {
                    float sn = Mathf.Clamp01(t / 0.12f);
                    b.mpb.SetColor(PLow, Color.Lerp(CometViolet, ash, sn)); b.mpb.SetColor(PHigh, Color.Lerp(CometPale, RuneViolet, sn));
                    b.mpb.SetFloat(PProgress, 1.1f); b.mpb.SetFloat(PWidth, 0.07f); b.mpb.SetFloat(PDissolve, Mathf.Clamp01((t - 0.12f) / (life - 0.12f)));
                    b.mpb.SetFloat(PIntensity, Mathf.Lerp(1.0f, 0.3f, sn) * Mathf.Max(0.6f, kb)); return;
                }
                b.mpb.SetColor(PLow, lit > 0 ? RuneViolet : ash); b.mpb.SetColor(PHigh, lit > 0 ? ArmedGold : RuneViolet);
                b.mpb.SetFloat(PProgress, 1.1f); b.mpb.SetFloat(PWidth, 0.07f); b.mpb.SetFloat(PDissolve, Mathf.Clamp01((t - Mathf.Max(0.06f, hold)) / (life - Mathf.Max(0.06f, hold))));
                if (flash && lit <= 0) { b.mpb.SetColor(PLow, new Color(0.45f, 0.35f, 0.6f)); b.mpb.SetColor(PHigh, new Color(0.6f, 0.45f, 0.95f)); }
                b.mpb.SetFloat(PIntensity, (lit > 0 ? 1.4f : flash ? Mathf.Lerp(1.0f, 0.6f, Mathf.Clamp01(t / 0.3f)) : 0.35f) * Mathf.Max(0.6f, kb));
            };
            if (m.rings != null) foreach (var r in m.rings)
                {
                    Arc(RingU(r - line * 1.2f, r, 128), true, arcAlphaFloor, c, Quaternion.identity, life, burnt, MStroke, BurntGrey, BurntGrey, RuneViolet);
                    Arc(RingU(r - line * 0.7f, r - line * 0.3f, 128), true, arcAddFloor, c, Quaternion.identity, life, ember, MStroke, RuneViolet, RuneViolet, AshMagenta);
                }
            if (m.strokes != null) foreach (var (a, bb) in m.strokes)
                {
                    Arc(LineQuad(a, bb, line * 1.2f), true, arcAlphaFloor, c, Quaternion.identity, life, burnt, MStroke, BurntGrey, BurntGrey, RuneViolet);
                    Arc(LineQuad(a, bb, line * 0.75f), true, arcAddFloor, c, Quaternion.identity, life, ember, MStroke, RuneViolet, RuneViolet, AshMagenta);
                }
        }

        // ================================================================== E3 Rune Sigil
        public void SigilPlace(PlayerController p, int id, Vector3 pos, int armTicks, bool powered = true)
        {
            EnsureArc();
            float life = armTicks / (float)Sim.Hz, hold = life + 0.12f; float R = ArcanistKit.SigilRadius;
            var m = new MarkFx { pos = new Vector3(pos.x, 0.04f, pos.z), start = Time.time, life = life, ringYaw = Random.value * 360 };
            marks[id] = m;
            var kit = Arcanist(p); bool surging = kit != null && kit.Surging;
            DrawRune(m, m.pos, R, life, 10, 0.07f, surging ? SurgeInk * 0.8f : RuneViolet, surging ? ArmedGold : HotWhite, SigilArmed, powered,
                powered && kit != null && !surging ? GlyphFly + 0.02f : 0);
            foreach (var hp in m.parts) StartCoroutine(KillAfter(hp, hold));
            Arc(RingU(0.1f, 0.14f, 24), true, arcAdd, HandPos(p), Facing(Vector3.up), 0.2f, (b, u) =>
            {
                b.go.transform.localScale = Vector3.one * (1 + u); b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PDissolve, u);
            }, MBeam, RuneViolet, HotWhite, AshMagenta);
        }

        public void SigilExplode(int id, Vector3 pos, bool hit)
        {
            if (!marks.TryGetValue(id, out var m)) return;
            marks.Remove(id); foreach (var h in m.parts) KillH(h);
            var c = new Vector3(pos.x, 0.04f, pos.z); float R = ArcanistKit.SigilRadius;
            float k = m.weak ? 0.55f : 1f, kb = m.weak ? 0.5f : 1f;   // cast without a glyph: about half the size and brightness
            // the rune stays on the floor through the blast: its lines flash, then burn down to dim violet-grey
            BurntRune(m, c, 1.0f, 0.07f, kb, true, 0.3f);
            // the burst: a column of flowing violet light (3 m) that punches past full width (0.5 -> 1.08 -> 1.0 in 0.1 s): a
            // soft pillar with vertical strands, bright at the floor and fading up, frayed by noise and eaten from the top
            // down with a gold edge, over a faint rim shell; gone by 0.32 s
            for (int layer = 0; layer < 1; layer++)
            {
                bool pillar = layer == 0;
                Arc(colShell, false, arcAdd, c, Quaternion.identity, 0.32f, (b, u) =>
                {
                    float t = u * 0.32f;
                    float w = t < 0.05f ? Mathf.Lerp(0.5f, 1.08f, Ease(t / 0.05f)) : Mathf.Lerp(1.08f, 0.82f, Ease(Mathf.Clamp01((t - 0.05f) / 0.27f)));
                    b.go.transform.localScale = new Vector3(w * k, Mathf.Lerp(0.55f, 1f, Ease(Mathf.Clamp01(t / 0.06f))) * k, w * k);
                    b.mpb.SetFloat(PFresnel, 1.4f); b.mpb.SetFloat(PEdgeGlow, 1.4f); b.mpb.SetFloat(PSoft, 0.25f);
                    b.mpb.SetFloat(PClipY, c.y + 3.2f * k * (1 - 0.85f * Ease(Mathf.Clamp01((t - 0.08f) / 0.24f))));
                    b.mpb.SetFloat(PDissolve, 0.22f + 0.7f * Mathf.Clamp01((t - 0.06f) / 0.26f));
                    b.mpb.SetFloat(PIntensity, (pillar ? 0.62f : 0.22f) * kb); b.mpb.SetVector(PFlow, new Vector4(0, -5f, 0.6f, -3f)); b.mpb.SetFloat(PNoiseAmt, 1f);
                    b.mpb.SetVector(PNoiseScale, new Vector4(pillar ? 30 : 16, 2, 0, 0));
                }, pillar ? MPillar : MShell, RuneViolet, m.weak ? FaintHot : ArcLilac, m.weak ? FaintHot : ArmedGold);
            }
            // the punch on the detonation frame: a white-hot core (0.3 m x 1.5 m, 0.1 s), a flash, real light
            Arc(cyl, false, arcAdd, c, Quaternion.identity, 0.1f, (b, u) =>
            {
                b.go.transform.localScale = new Vector3(0.6f * k * (1 - 0.4f * u), 1.5f * k, 0.6f * k * (1 - 0.4f * u)); b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 1.5f * kb);
            }, MPillar, ArcLilac, HotWhite, AshMagenta);
            Arc(billboard, false, arcAdd, c + Vector3.up * 0.6f, FaceCam(c), 0.1f, (b, u) =>
            {
                b.go.transform.localScale = Vector3.one * Mathf.Lerp(2.2f, 2.8f, u) * k; b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 1.4f * kb);
            }, MDot, RuneViolet, HotWhite, AshMagenta);
            ArcLight(c + Vector3.up, RuneViolet, 3f * kb, 7f * k, 0.15f);
            // two thin rings out to the rune's edge and past it, from the same frame
            for (int j = 0; j < 1; j++)
            {
                float delay = 0, far = R * (m.weak ? 0.6f : 1f), life = 0.24f;
                Arc(RingU(0.975f, 1f, 128), true, arcAddFloor, c, Quaternion.identity, life, (b, u) =>
                {
                    float t = Mathf.Clamp01((u * life - delay) / 0.24f); float r = Mathf.Lerp(0.6f * R * k, far, Ease(t)); b.go.transform.localScale = new Vector3(r, 1, r);
                    b.mpb.SetFloat(PProgress, 1.1f); b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PIntensity, t <= 0 ? 0 : 2.0f * kb * (1 - 0.75f * t));
                }, MStroke, RuneViolet, j == 0 ? ArmedGold : ArcLilac, AshMagenta);
            }
            // pieces of the rune's strokes thrown out and up: straight and curved pieces (0.25-0.4 m), spinning in the view
            // plane, landing within about 1.5x the radius and burnt away before they land
            int frags = m.weak ? 4 : 8;
            for (int j = 0; j < frags; j++)
            {
                float a = (j + Random.Range(-0.3f, 0.3f)) * Mathf.PI * 2 / frags; var outDir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                var start = c + outDir * R * 0.5f * k + Vector3.up * 0.25f; var v = outDir * Random.Range(2.5f, 4f) * k + Vector3.up * Random.Range(4f, 5.5f) * k;
                float spin = Random.Range(360f, 720f) * (Random.value < 0.5f ? -1 : 1), len = Random.Range(0.25f, 0.4f) * Mathf.Max(0.75f, k);
                var piece = j % 2 == 0 ? LineQuad(new Vector3(0, 0, -len / 2), new Vector3(0, 0, len / 2), 0.05f) : FxMeshes.Sector(len * 1.1f - 0.05f, len * 1.1f, 28f, 8, false, "FX_MS_RunePiece");
                Arc(piece, true, arcAdd, start, Quaternion.identity, 0.45f, (b, u) =>
                {
                    float t = u * 0.45f; var q = start + v * t + Vector3.down * 11f * t * t;
                    b.go.transform.SetPositionAndRotation(q, FaceCam(q) * Quaternion.Euler(0, spin * t, 0));
                    b.mpb.SetColor(PLow, Color.Lerp(m.weak ? FaintHot : RuneViolet, RuneViolet, u)); b.mpb.SetColor(PHigh, Color.Lerp(m.weak ? FaintHot : ArmedGold, ArcLilac, u));
                    b.mpb.SetFloat(PProgress, 1.1f); b.mpb.SetFloat(PWidth, 0.07f); b.mpb.SetFloat(PDissolve, Mathf.Clamp01((t - 0.12f) / 0.33f)); b.mpb.SetFloat(PEdgeGlow, 1.2f);
                    b.mpb.SetFloat(PIntensity, 0.95f * Mathf.Max(0.6f, kb));
                }, MStroke, RuneViolet, ArcLilac, ArmedGold);
            }
            for (int j = 0; j < (m.weak ? 6 : 16); j++) arcSparks.Emit(c + Vector3.up * 0.4f, (Random.insideUnitSphere + Vector3.up * 0.8f) * 7f * k, 0.07f, 0.3f);
            CameraShake.Shake(m.weak ? 0.05f : 0.12f, 0.15f);
        }
        public void SigilFade(int id) => FadeMark(id);
        void FadeMark(int id)
        {
            if (!marks.TryGetValue(id, out var m)) return;
            marks.Remove(id);
            foreach (var h in m.parts)
            {
                if (!h.Alive(this)) continue;
                var b0 = h.b; float t0 = b0.t;
                b0.life = b0.t + 0.3f; b0.anim = (b, u) => { b.mpb.SetFloat(PDissolve, Mathf.Clamp01((b.t - t0) / 0.3f)); };
            }
            if (m.comet) Destroy(m.comet.gameObject);
        }

        // ================================================================== E7 Glyphs
        // Disintegrate carves glyphs (max 3): small violet runes orbiting the Arcanist's waist on a faint socket ring.
        // Rune Sigil and Arcane Comet each spend one (it flies into the rune); Arcane Surge spends them all.
        // where glyph i sits: on the socket ring, lifted a little above it; pulled into the astrolabe while Surge is cast
        Vector3 GlyphSocket(PlayerController p, ArcState s, int i)
        {
            float a = (s.orbit + i * 120f) * Mathf.Deg2Rad;
            var at = p.transform.position + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * 0.85f + Vector3.up * (1.35f + 0.03f * Mathf.Sin(Time.time * 2.4f + i * 2.1f));
            float pull = s.surgePull >= 0 ? Ease(Mathf.Clamp01((Time.time - s.surgePull) / s.surgeCastLife))
                : s.snapFrom * (1 - Ease(Mathf.Clamp01((Time.time - s.surgeSnap) / 0.09f)));
            return pull > 0 ? Vector3.Lerp(at, AstroPos(p), pull) : at;
        }
        void KillGlyph(ArcState s, int i)
        {
            if (s.glyphOrbs[i] == null) return;
            foreach (var h in s.glyphOrbs[i]) KillH(h); s.glyphOrbs[i] = null;
        }
        static float GlyphSpeed(int g) => g <= 0 ? 12 : g == 1 ? 20 : g == 2 ? 35 : 50;
        const float GlyphFly = 0.25f;   // a spent glyph's flight into its rune

        // called by the kit every tick: makes sure the socket ring exists (idempotent)
        public void ArcanistTick(PlayerController p)
        {
            if (!p) return;
            var s = St(p);
            if (s.sockets.Count > 0 && s.sockets[0].Alive(this)) return;
            foreach (var h in s.sockets) KillH(h); s.sockets.Clear();
            // a thin dashed ring at the waist with gaps at the three sockets, and a tick under each socket; it turns faster
            // with each glyph held and is barely there when empty
            for (int part = 0; part < 2; part++)
            {
                int pp = part;
                Mesh mesh = pp == 0 ? DashedRing(0.835f, 0.855f, 3, 26, 32) : TickRing(0.77f, 0.93f, 3, 0.05f);
                var b = Arc(mesh, true, arcAdd, p.transform.position, Quaternion.identity, 9999, (bb, time) =>
                {
                    if (!p) return; var kit = Arcanist(p); int g = kit != null ? kit.Glyphs : 0;
                    if (pp == 0) s.orbit += Time.deltaTime * GlyphSpeed(g);
                    bb.go.transform.SetPositionAndRotation(p.transform.position + Vector3.up * 1.25f, Quaternion.Euler(0, s.orbit + (pp == 0 ? 60 : 0), 0));
                    bb.mpb.SetColor(PHigh, g >= ArcanistKit.MaxGlyphs ? ArmedGold : ArcLilac);   // full: the ring takes a gold tint
                    bool off = IsHidden(p) || (kit != null && kit.Surging);
                    float full = g >= ArcanistKit.MaxGlyphs ? 0.5f + 0.5f * Mathf.Sin(Time.time * 6f) : 0;
                    float lvl = off ? 0 : (pp == 0 ? 0.14f + 0.1f * g : 0.4f + 0.15f * g) * (1 + 0.3f * full);
                    if (pp == 0) bb.mpb.SetFloat(PWidth, 0.6f); else { bb.mpb.SetFloat(PProgress, 1.1f); bb.mpb.SetFloat(PWidth, 0.07f); }
                    bb.mpb.SetFloat(PIntensity, lvl);
                }, pp == 0 ? MBeam : MStroke, RuneViolet, ArcLilac, AshMagenta);
                s.sockets.Add(H(b));
            }
        }

        public void GlyphGain(PlayerController p, int count, Vector3 from)
        {
            if (!p) return;
            var s = St(p); int i = count - 1; if (i < 0 || i >= ArcanistKit.MaxGlyphs) return;
            ArcanistTick(p); KillGlyph(s, i);
            const float race = 0.22f, draw = 0.15f;
            s.glyphBorn[i] = Time.time + race;
            // a gold-white spark races back along the beam from the boss to the empty socket, drawn in front of the beam
            var start = from; var tr = TakeTrail(0.12f, 0.12f, start, goldTrailMat);
            Arc(billboard, false, arcAdd, start, Quaternion.identity, race, (b, u) =>
            {
                if (!p) return; var q = Vector3.Lerp(start, GlyphSocket(p, s, i), u * u * (3 - 2 * u)) + Vector3.up * 0.25f * Mathf.Sin(u * Mathf.PI);
                if (Cam) q -= Cam.transform.forward * 0.35f;
                b.go.transform.SetPositionAndRotation(q, FaceCam(q)); b.go.transform.localScale = Vector3.one * 0.32f; b.mpb.SetFloat(PIntensity, 1.15f);
                if (tr) tr.transform.position = q;
                if (Random.value < 0.6f) arcMotes.Emit(q, Random.insideUnitSphere * 0.4f, 0.06f, 0.2f);
            }, MDot, ArmedGold, HotWhite, AshMagenta);
            StartCoroutine(ReleaseTrailAfter(tr, race, 0.12f));
            // the glyph: a 0.28 m violet rune with a gold edge, drawn stroke by stroke as the spark lands, a soft glow behind
            var rs = Random.state; Random.InitState(7919 * (i + 3));   // each socket keeps its own rune shape
            var core = RuneGlyph(0.34f, 0.028f, out var glow, out var edge, 1.65f);
            Random.state = rs;
            var parts = new Handle[4];
            for (int k = 0; k < 4; k++)
            {
                int part = k; Mesh mesh = part == 0 ? core : part == 1 ? glow : part == 2 ? billboard : edge;
                var b = Arc(mesh, part != 2, part == 0 ? arcOver : part == 3 ? arcOverEdge : arcAdd, GlyphSocket(p, s, i), Quaternion.identity, 9999, (bb, time) =>
                {
                    if (!p) return; var kit = Arcanist(p); int g = kit != null ? kit.Glyphs : 0;
                    var q = GlyphSocket(p, s, i); float since = Time.time - s.glyphBorn[i];
                    bool show = g > i && since >= 0 && (!IsHidden(p) || Time.time - s.surgeSnap < 0.2f);   // snapping back from a cancelled Surge stays visible
                    float prog = Mathf.Clamp01(since / draw), flash = since > draw ? Mathf.Exp(-(since - draw) * 9) : 0;
                    float full = g >= ArcanistKit.MaxGlyphs ? 0.5f + 0.5f * Mathf.Sin(Time.time * 5f) : 0;   // all three pulse together (0.8 Hz)
                    float gold = Mathf.Exp(-Mathf.Max(0, Time.time - s.fullAt) * 7);                           // the third glyph: a gold flare on all three
                    bb.go.transform.SetPositionAndRotation(q + (part == 2 && Cam ? -Cam.transform.forward * 0.02f : Vector3.zero), FaceCam(q) * Quaternion.Euler(0, 8 * Mathf.Sin(Time.time * 1.7f + i), 0));
                    if (part == 2) { bb.go.transform.localScale = Vector3.one * (0.55f + 0.2f * flash) * (1 + 0.35f * full); bb.mpb.SetFloat(PIntensity, show ? (0.16f + 0.22f * full + 0.5f * flash) * prog : 0); return; }
                    bb.go.transform.localScale = Vector3.one * (1 + 0.2f * full);
                    bb.mpb.SetFloat(PProgress, prog * 1.03f); bb.mpb.SetFloat(PWidth, part == 1 ? 0.2f : 0.09f);
                    bb.mpb.SetFloat(PIntensity, !show ? 0 : part == 0 ? 1.0f + 0.35f * full : part == 3 ? 1.0f + 0.35f * full + 0.3f * flash + 0.4f * gold : 0.08f + 0.08f * full);
                    bb.mpb.SetVector(PFlow, new Vector4(-2.4f, 0, -1.1f, 0.3f));
                }, part == 2 ? MDot : MStroke, part == 2 ? new Color(0.75f, 0.45f, 0.9f) : part == 3 ? EdgeGold : RuneViolet, part == 3 ? EdgeGoldHot : GlyphHot, AshMagenta);
                parts[k] = H(b);
            }
            s.glyphOrbs[i] = parts;
            StartCoroutine(GlyphArrive(p, s, i, race, count >= ArcanistKit.MaxGlyphs));
        }
        System.Collections.IEnumerator ReleaseTrailAfter(TrailRenderer tr, float after, float fade)
        {
            yield return new WaitForSeconds(after); yield return ReleaseTrail(tr, fade);
        }
        // the glyph finishing: a ring flashes out from it, motes; the third glyph also flashes the whole socket ring gold
        System.Collections.IEnumerator GlyphArrive(PlayerController p, ArcState s, int i, float after, bool full)
        {
            yield return new WaitForSeconds(after); if (!p) yield break;
            var q = GlyphSocket(p, s, i);
            Arc(RingU(0.15f, 0.18f, 40), true, arcAdd, q, FaceCam(q), 0.22f, (b, u) =>
            {
                if (!p) return; var at = GlyphSocket(p, s, i); b.go.transform.SetPositionAndRotation(at, FaceCam(at));
                b.go.transform.localScale = Vector3.one * Mathf.Lerp(0.7f, 1.3f, Ease(u)); b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 1.4f);
            }, MBeam, new Color(0.5f, 0.3f, 1.6f), EdgeGold, EdgeGold);
            for (int k = 0; k < 8; k++) arcMotes.Emit(q, Random.onUnitSphere * 1.5f, 0.1f, 0.3f);
            ArcLight(q, ArcLilac, 0.6f, 2.5f, 0.15f);
            if (!full) yield break;
            // full: the three glyphs flare gold together (no ring)
            s.fullAt = Time.time;
            for (int j = 0; j < ArcanistKit.MaxGlyphs; j++) { var gq = GlyphSocket(p, s, j); for (int k = 0; k < 4; k++) arcMotes.Emit(gq, Random.onUnitSphere * 0.8f + Vector3.up * 0.6f, 0.06f, 0.35f); }
        }

        // a glyph spent on a rune: it leaves its socket and arcs into the rune's centre, landing with a gold flash
        public void GlyphSpend(PlayerController p, int remaining, Vector3 target)
        {
            if (!p) return;
            var s = St(p); int i = remaining; if (i < 0 || i >= ArcanistKit.MaxGlyphs) return;
            var from = s.glyphOrbs[i] != null && s.glyphOrbs[i][0].Alive(this) ? s.glyphOrbs[i][0].b.go.transform.position : GlyphSocket(p, s, i);
            KillGlyph(s, i);
            // the socket it left flashes once and goes dark
            Arc(RingU(0.85f, 1f, 32), true, arcAdd, from, FaceCam(from), 0.22f, (b, u) =>
            {
                if (!p) return; var q = GlyphSocket(p, s, i); b.go.transform.SetPositionAndRotation(q, FaceCam(q));
                b.go.transform.localScale = Vector3.one * Mathf.Lerp(0.2f, 0.1f, u); b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PIntensity, u < 0.25f ? 1.2f : 0.5f * (1 - u));
            }, MBeam, RuneViolet, ArcLilac, AshMagenta);
            for (int k = 0; k < 4; k++) arcMotes.Emit(from, Random.onUnitSphere * 0.6f + Vector3.down * 0.3f, 0.05f, 0.25f);
            const float fly = GlyphFly;
            var tgt = new Vector3(target.x, 0.15f, target.z);
            var core = RuneGlyph(0.34f, 0.028f, out var glow, out var edge); Destroy(glow);
            var tr = TakeTrail(0.2f, 0.16f, from);
            float h = Mathf.Max(1.6f, Sim.Flat(tgt - from).magnitude * 0.35f);
            for (int part = 0; part < 2; part++)
            {
                int pp = part;
                Arc(pp == 0 ? core : edge, true, pp == 0 ? arcOver : arcOverEdge, from, FaceCam(from), fly, (b, u) =>
                {
                    var q = Vector3.Lerp(from, tgt, u) + Vector3.up * h * Mathf.Sin(u * Mathf.PI);
                    b.go.transform.SetPositionAndRotation(q, FaceCam(q) * Quaternion.Euler(0, u * 200, 0)); b.go.transform.localScale = Vector3.one * (1 + 0.8f * u);
                    b.mpb.SetFloat(PProgress, 1.1f); b.mpb.SetFloat(PWidth, 0.09f); b.mpb.SetFloat(PIntensity, pp == 0 ? 1.3f : 1.2f);
                    if (pp == 0 && tr) tr.transform.position = q;
                    if (pp == 0 && Random.value < 0.5f) arcMotes.Emit(q, Random.insideUnitSphere * 0.3f, 0.06f, 0.25f);
                }, MStroke, pp == 0 ? RuneViolet : EdgeGold, pp == 0 ? GlyphHot : EdgeGoldHot, AshMagenta);
            }
            StartCoroutine(ReleaseTrailAfter(tr, fly, 0.12f));
            StartCoroutine(GlyphLand(tgt, fly));
        }
        System.Collections.IEnumerator GlyphLand(Vector3 at, float after)
        {
            yield return new WaitForSeconds(after);
            var c = new Vector3(at.x, 0.06f, at.z);
            // a small 4-point star (violet with gold points) standing up at the centre, and one thin gold ring opening to 0.8 m
            for (int k = 0; k < 2; k++)
            {
                var q = c + Vector3.up * 0.5f; var r = FaceCam(q) * Quaternion.Euler(0, k * 90 + 45, 0);
                Arc(beamMesh, false, arcAdd, q, r, 0.16f, (b, u) =>
                {
                    b.go.transform.position = q - b.go.transform.forward * 0.45f * Mathf.Lerp(0.6f, 1f, Ease(u));
                    b.go.transform.localScale = new Vector3(0.11f, 1, 1.2f * Mathf.Lerp(0.6f, 1f, Ease(u))); b.mpb.SetFloat(PWidth, 0.35f); b.mpb.SetFloat(PDissolve, u * u); b.mpb.SetFloat(PIntensity, 1.4f);
                }, MBeam, RuneViolet, ArmedGold, AshMagenta);
            }
            Arc(RingU(0.27f, 0.3f, 48), true, arcAdd, c + Vector3.up * 0.5f, FaceCam(c), 0.2f, (b, u) =>
            {
                var q = c + Vector3.up * 0.5f; b.go.transform.SetPositionAndRotation(q, FaceCam(q));
                b.go.transform.localScale = Vector3.one * Mathf.Lerp(0.5f, 1.1f, Ease(u)); b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 1.3f);
            }, MBeam, new Color(0.5f, 0.3f, 1.6f), GlyphHot, EdgeGold);
            Arc(RingU(0.95f, 1f, 64), true, arcAddFloor, c, Quaternion.identity, 0.22f, (b, u) =>
            {
                float r = Mathf.Lerp(0.2f, 0.8f, Ease(u)); b.go.transform.localScale = new Vector3(r, 1, r);
                b.mpb.SetFloat(PProgress, 1.1f); b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PIntensity, 1.5f * (1 - u));
            }, MStroke, ArmedGold, SigilArmed, AshMagenta);
            for (int k = 0; k < 10; k++) arcSparks.Emit(c + Vector3.up * 0.2f, (Random.insideUnitSphere + Vector3.up) * 3f, 0.05f, 0.25f);
            ArcLight(c + Vector3.up * 0.5f, ArmedGold, 1.0f, 3f, 0.15f);
        }

        // ================================================================== E8 Arcane Surge
        // Cast (0.3 s): the glyphs are pulled into the astrolabe. Release: a column and a double shockwave, both sized by
        // the glyphs spent. Active (2 s per glyph): a gold foot ring, a chest ring that empties as the timer, a crown of the
        // spent glyphs, a faint rim of light on the body, rising motes; the last second flickers; the end folds inward.
        public void SurgeCharge(PlayerController p, int glyphs, int ticks)
        {
            if (!p) return;
            var s = St(p); float life = ticks / (float)Sim.Hz; s.surgeCastLife = life * 0.85f; s.surgePull = Time.time; s.snapFrom = 0;
            if (s.rig) s.rig.SurgeCharge(life);   // the core brightens toward gold and the cage spins up as the glyphs arrive
            foreach (var h in s.surgeCast) KillH(h); s.surgeCast.Clear();
            // a ring at the astrolabe tightening onto it while a dashed ring spins up
            s.surgeCast.Add(H(Arc(RingU(0.92f, 1f, 64), true, arcAdd, AstroPos(p), Quaternion.identity, 9999, (b, time) =>
            {
                if (!p) return; float u = Mathf.Clamp01(time / life); var q = AstroPos(p);
                b.go.transform.SetPositionAndRotation(q, FaceCam(q)); b.go.transform.localScale = Vector3.one * Mathf.Lerp(0.95f, 0.18f, Ease(u));
                b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PIntensity, 0.8f + 0.7f * u);
            }, MBeam, ArcLilac, ArmedGold, AshMagenta)));
            s.surgeCast.Add(H(Arc(DashedRing(0.3f, 0.33f, 3, 30, 16), true, arcAdd, AstroPos(p), Quaternion.identity, 9999, (b, time) =>
            {
                if (!p) return; float u = Mathf.Clamp01(time / life); var q = AstroPos(p);
                b.go.transform.SetPositionAndRotation(q, FaceCam(q) * Quaternion.Euler(0, time * (300 + 900 * u), 0)); b.go.transform.localScale = Vector3.one * (1 + 0.4f * u);
                b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PIntensity, 0.7f + 0.6f * u);
            }, MBeam, ArcLilac, ArmedGold, AshMagenta)));
            s.surgeCast.Add(H(Arc(billboard, false, arcAdd, AstroPos(p), Quaternion.identity, 9999, (b, time) =>
            {
                if (!p) return; float u = Mathf.Clamp01(time / life); var q = AstroPos(p);
                b.go.transform.SetPositionAndRotation(q, FaceCam(q)); b.go.transform.localScale = Vector3.one * Mathf.Lerp(0.3f, 0.9f, Ease(u)); b.mpb.SetFloat(PIntensity, 0.6f + 0.9f * u);
            }, MDot, ArmedGold, HotWhite, AshMagenta)));
            var a = AstroPos(p);
            for (int k = 0; k < 14; k++) { var off = Random.onUnitSphere * 0.9f; arcMotes.Emit(a + off, -off / life, 0.06f, life * 0.95f); }
        }
        // a small gold ring at a socket as its glyph snaps home
        System.Collections.IEnumerator SnapTick(PlayerController p, ArcState s, int i, float after)
        {
            yield return new WaitForSeconds(after); if (!p) yield break;
            Arc(RingU(0.85f, 1f, 32), true, arcAdd, GlyphSocket(p, s, i), Quaternion.identity, 0.12f, (b, u) =>
            {
                if (!p) return; var q = GlyphSocket(p, s, i); b.go.transform.SetPositionAndRotation(q, FaceCam(q));
                b.go.transform.localScale = Vector3.one * Mathf.Lerp(0.1f, 0.26f, Ease(u)); b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 1.5f);
            }, MBeam, RuneViolet, ArmedGold, AshMagenta);
            var sq = GlyphSocket(p, s, i); for (int k = 0; k < 4; k++) { var off = Random.onUnitSphere * 0.3f; arcMotes.Emit(sq + off, -off / 0.1f, 0.06f, 0.1f); }
        }
        public void SurgeCancel(PlayerController p)
        {
            if (!p) return;
            var s = St(p);
            s.snapFrom = s.surgePull >= 0 ? Ease(Mathf.Clamp01((Time.time - s.surgePull) / s.surgeCastLife)) : 0; s.surgeSnap = Time.time; s.surgePull = -1;
            if (s.rig) s.rig.SurgeCancel();
            for (int i = 0; i < ArcanistKit.MaxGlyphs; i++) if (s.glyphOrbs[i] != null) StartCoroutine(SnapTick(p, s, i, 0.09f));
            foreach (var h in s.surgeCast)
            {
                if (!h.Alive(this)) continue; var b0 = h.b; float t0 = b0.t; var sc = b0.go.transform.localScale; var off = b0.go.transform.position - AstroPos(p);
                b0.life = b0.t + 0.05f; b0.anim = (b, u) => { float k = Mathf.Clamp01((b.t - t0) / 0.05f); if (p) b.go.transform.position = AstroPos(p) + off; b.go.transform.localScale = sc * (1 + 0.4f * k); b.mpb.SetFloat(PDissolve, k); };   // follows him
            }
            s.surgeCast.Clear();
            var a = AstroPos(p); for (int k = 0; k < 10; k++) arcMotes.Emit(a, Random.onUnitSphere * 1.8f, 0.05f, 0.2f);
        }

        public void SurgeStart(PlayerController p, int glyphs, int ticks)
        {
            if (!p) return;
            var s = St(p);
            foreach (var h in s.surgeCast) KillH(h); s.surgeCast.Clear(); s.surgePull = -1; s.snapFrom = 0;
            if (s.rig) s.rig.SurgeOn();   // empowered: the core holds a brighter gold-violet glow until SurgeEnd
            for (int i = 0; i < s.glyphOrbs.Length; i++) KillGlyph(s, i);
            foreach (var h in s.surge) KillH(h); s.surge.Clear(); s.surgeEnding = -1;
            var c = p.transform.position; float g = Mathf.Max(1, glyphs), wide = 0.85f + 0.1f * g;
            // release: a see-through tapered column of flowing violet light with gold lips, eaten from the top down,
            // and a double shockwave; both bigger with each glyph spent
            for (int layer = 0; layer < 2; layer++)
            {
                bool pillar = layer == 0;
                Arc(colShell, false, arcAdd, c, Quaternion.identity, 0.25f, (b, u) =>
                {
                    float t = u * 0.25f; float rise = Ease(Mathf.Clamp01(t / 0.05f)); var at = p ? p.transform.position : c;
                    b.go.transform.position = at; b.go.transform.localScale = new Vector3(0.55f * wide, rise * 1.25f, 0.55f * wide);
                    b.mpb.SetFloat(PFresnel, 1.3f); b.mpb.SetFloat(PClipY, 99);
                    b.mpb.SetFloat(PIntensity, (pillar ? 0.62f : 0.2f) * (1 - Mathf.Clamp01((t - 0.15f) / 0.1f))); b.mpb.SetVector(PFlow, new Vector4(0, -4.5f, 0.6f, -2.8f)); b.mpb.SetFloat(PNoiseAmt, 1f);
                    b.mpb.SetVector(PNoiseScale, new Vector4(pillar ? 30 : 14, 2, 0, 0)); b.mpb.SetFloat(PEdgeGlow, 1.5f); b.mpb.SetFloat(PSoft, 0.25f);
                    b.mpb.SetFloat(PDissolve, 0.2f + 0.75f * Mathf.Clamp01((t - 0.06f) / 0.19f));   // streaky from the start, frays all over with a gold edge
                }, pillar ? MPillar : MShell, RuneViolet, new Color(1.0f, 0.55f, 2.1f), ArmedGold);
            }
            for (int rb = 0; rb < 3; rb++)
            {
                float ang = rb * 120f + Random.Range(-20f, 20f);
                Arc(beamMesh, false, arcAdd, c, Quaternion.identity, 0.22f, (b, u) =>
                {
                    var at = p ? p.transform.position : c; var d = Quaternion.Euler(0, ang + u * 60f, 0) * Vector3.forward * 0.6f * wide;
                    var q = at + d; float h = 3.4f * Ease(Mathf.Clamp01(u / 0.25f));
                    b.go.transform.SetPositionAndRotation(q, AlongFacingCam(q, Vector3.up)); b.go.transform.localScale = new Vector3(0.07f, 1, h);
                    b.mpb.SetFloat(PWidth, 0.4f); b.mpb.SetFloat(PDissolve, Mathf.Clamp01((u - 0.4f) / 0.6f)); b.mpb.SetFloat(PIntensity, 1.2f);
                    b.mpb.SetVector(PFlow, new Vector4(-4f, 0, -2f, 0)); b.mpb.SetFloat(PNoiseAmt, 0.6f);
                }, MBeam, DeepGold, ArmedGold, ArmedGold);
            }
            for (int lip = 0; lip < 1; lip++)
            {
                float y = 0.05f, r = 0.66f * wide;
                Arc(RingU(r - 0.04f, r, 64), true, lip == 0 ? arcAddFloor : arcAdd, c + Vector3.up * y, Quaternion.identity, 0.3f, (b, u) =>
                {
                    if (p) b.go.transform.position = p.transform.position + Vector3.up * y;
                    b.go.transform.localScale = Vector3.one * Mathf.Lerp(0.8f, 1.15f, Ease(u)); b.mpb.SetFloat(PProgress, 1.1f); b.mpb.SetFloat(PWidth, 0.6f);
                    b.mpb.SetFloat(PIntensity, 1.5f * (1 - u));
                }, MStroke, ArmedGold, HotWhite, AshMagenta);
            }
            for (int k = 0; k < 2; k++)
            {
                float delay = k * 0.06f, far = 1.25f + 0.75f * g + 0.3f * k * (g / 3f);
                Arc(RingU(0.985f, 1f, 128), true, arcAddFloor, c + Vector3.up * 0.03f, Quaternion.identity, 0.35f + delay, (b, u) =>
                {
                    float t = Mathf.Clamp01((u * (0.35f + delay) - delay) / 0.35f); float r = Mathf.Lerp(0.5f + 0.4f * k, far, Ease(t)); b.go.transform.localScale = new Vector3(r, 1, r);
                    b.mpb.SetFloat(PProgress, 1.1f); b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PIntensity, t <= 0 ? 0 : (k == 0 ? 2.2f : 1.7f) * (1 - 0.75f * t));
                }, MStroke, k == 0 ? DeepGold : RuneViolet, k == 0 ? ArmedGold : ArcLilac, AshMagenta);
            }
            for (int k = 0; k < 10 + 6 * glyphs; k++) arcSparks.Emit(c + Vector3.up * Random.Range(0.3f, 1.6f), (Random.insideUnitSphere * 0.8f + Vector3.up * 1.2f) * Random.Range(3f, 6f), 0.06f, 0.4f);
            ArcLight(c + Vector3.up, ArmedGold, 1.4f + 0.5f * g, 6f, 0.28f);
            CameraShake.Shake(0.05f + 0.03f * g, 0.16f);

            // the active state; every part follows him, hides with him during Blink, strobes in the last second and folds
            // into his chest at the end
            float Vis(float time, out float fold)
            {
                fold = s.surgeEnding < 0 ? 0 : Mathf.Clamp01((Time.time - s.surgeEnding) / 0.22f);
                var kit = Arcanist(p); float left = kit != null && kit.Surging ? kit.SurgeSeconds : 0;
                if (IsHidden(p)) return 0;
                float grow = Ease(Mathf.Clamp01(time / 0.2f));
                float flick = 1;
                if (left > 0 && left < 1f) flick = Mathf.Cos(time * 7f * Mathf.PI * 2) > -0.2f ? 1f : 0.2f;
                return grow * flick * (1 - fold * 0.6f);
            }
            float Spin(float time) { var kit = Arcanist(p); return kit != null && kit.Surging && kit.SurgeSeconds < 1f ? 0.3f : 1f; }
            // foot ring: dashed violet on the floor, turning, with a thin inner line; motes rise off it
            float footYaw = 0;
            s.surge.Add(H(Arc(DashedRing(1.04f, 1.1f, 6, 9, 20), true, arcAddFloor, c, Quaternion.identity, 9999, (b, time) =>
            {
                if (!p) return; float v = Vis(time, out var fold); var q = p.transform.position + Vector3.up * 0.04f;
                footYaw += Time.deltaTime * 40 * Spin(time);
                b.go.transform.SetPositionAndRotation(q, Quaternion.Euler(0, footYaw, 0)); float sc = Mathf.Lerp(1f, 0.1f, Ease(fold)); b.go.transform.localScale = new Vector3(sc, 1, sc);
                b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PIntensity, 0.9f * v);
                if (v > 0.5f && Random.value < Time.deltaTime * 45) { float a = Random.value * Mathf.PI * 2; arcMotes.Emit(q + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * 1.05f, Vector3.up * Random.Range(1.2f, 2f), 0.08f, 0.6f); }
                if (v > 0.5f && Random.value < Time.deltaTime * 28) { var r2 = Random.insideUnitCircle * 0.4f; arcSparks.Emit(q + new Vector3(r2.x, Random.Range(0.3f, 1.6f), r2.y), Vector3.up * Random.Range(0.8f, 1.4f), 0.06f, 0.8f); }
                if (v > 0.2f && Time.time >= s.rimNext) { s.rimNext = Time.time + 0.1f; BakeShell(s, 0.16f, 0.5f * v, false, true, 0, RuneViolet, ArmedGold); }
            }, MBeam, DeepGold, ArmedGold, AshMagenta)));
            s.surge.Add(H(Arc(RingU(0.9f, 0.925f, 96), true, arcAddFloor, c, Quaternion.identity, 9999, (b, time) =>
            {
                if (!p) return; float v = Vis(time, out var fold); b.go.transform.position = p.transform.position + Vector3.up * 0.035f;
                float sc = Mathf.Lerp(1f, 0.1f, Ease(fold)); b.go.transform.localScale = new Vector3(sc, 1, sc);
                b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PIntensity, 0.55f * v);
            }, MBeam, RuneViolet, ArcLilac, AshMagenta)));
            // chest ring: the timer, a gold arc that empties around him, with a bright head at its end over a faint full ring
            const float chestR = 1.0f;
            Quaternion ChestRot() => Quaternion.Euler(0, p.transform.eulerAngles.y + 180, 0);
            s.surge.Add(H(Arc(RingU(chestR - 0.02f, chestR + 0.02f, 128), true, arcAdd, c, Quaternion.identity, 9999, (b, time) =>
            {
                if (!p) return; float v = Vis(time, out var fold);
                b.go.transform.SetPositionAndRotation(p.transform.position + Vector3.up * 1.3f, ChestRot());
                float sc = Mathf.Lerp(1f, 0.1f, Ease(fold)); b.go.transform.localScale = new Vector3(sc, 1, sc);
                var kk = Arcanist(p); bool last = kk != null && kk.Surging && kk.SurgeSeconds < 1f;
                b.mpb.SetFloat(PProgress, 1.1f); b.mpb.SetFloat(PWidth, 0.07f); b.mpb.SetFloat(PIntensity, (last ? 0.3f : 0.14f) * v);
            }, MStroke, RuneViolet, ArcLilac, AshMagenta)));
            s.surge.Add(H(Arc(RingU(chestR - 0.02f, chestR + 0.02f, 128), true, arcAdd, c, Quaternion.identity, 9999, (b, time) =>
            {
                if (!p) return; float v = Vis(time, out var fold); var kit = Arcanist(p);
                b.go.transform.SetPositionAndRotation(p.transform.position + Vector3.up * 1.3f, ChestRot());
                float sc = Mathf.Lerp(1f, 0.1f, Ease(fold)); b.go.transform.localScale = new Vector3(sc, 1, sc);
                b.mpb.SetFloat(PProgress, TimerArc(kit) * 1.01f); b.mpb.SetFloat(PWidth, 0.07f); b.mpb.SetFloat(PIntensity, 1.4f * v);
                b.mpb.SetVector(PFlow, new Vector4(-2.4f, 0, -1.1f, 0.3f));
            }, MStroke, DeepGold, ArmedGold, AshMagenta)));
            float tickYaw = 0;
            s.surge.Add(H(Arc(TickRing(chestR - 0.08f, chestR + 0.08f, 8, 0.03f), true, arcAdd, c, Quaternion.identity, 9999, (b, time) =>
            {
                if (!p) return; float v = Vis(time, out var fold); tickYaw -= Time.deltaTime * 35 * Spin(time);
                b.go.transform.SetPositionAndRotation(p.transform.position + Vector3.up * 1.3f, Quaternion.Euler(0, tickYaw, 0));
                float sc = Mathf.Lerp(1f, 0.1f, Ease(fold)); b.go.transform.localScale = new Vector3(sc, 1, sc);
                b.mpb.SetFloat(PProgress, 1.1f); b.mpb.SetFloat(PWidth, 0.07f); b.mpb.SetFloat(PIntensity, 0.9f * v);
            }, MStroke, DeepGold, ArmedGold, AshMagenta)));
            s.surge.Add(H(Arc(billboard, false, arcAdd, c, Quaternion.identity, 9999, (b, time) =>
            {
                if (!p) return; float v = Vis(time, out var fold); var kit = Arcanist(p); float prog = TimerArc(kit);
                float a = (180 - 360 * prog) * Mathf.Deg2Rad;   // RingU runs from +180° to -180° along U
                var q = p.transform.position + Vector3.up * 1.3f + ChestRot() * new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * chestR * (1 - 0.9f * Ease(fold));
                b.go.transform.SetPositionAndRotation(q, FaceCam(q)); b.go.transform.localScale = Vector3.one * 0.2f; b.mpb.SetFloat(PIntensity, prog > 0 ? 0.8f * v : 0);   // a small spark at the arc's end
            }, MDot, ArmedGold, HotWhite, AshMagenta)));
            // a crown of the spent glyphs above his head: violet runes with gold edges and a soft halo, turning
            int crown = glyphs * 2;
            for (int j = 0; j < crown; j++)
            {
                int jj = j; var gm = RuneGlyph(0.22f, 0.022f, out var gg, out var ge, 1.65f); Destroy(gg);
                for (int part = 0; part < 3; part++)
                {
                    int pp = part; Mesh mesh = pp == 0 ? gm : pp == 1 ? ge : billboard;
                    s.surge.Add(H(Arc(mesh, pp < 2, pp == 0 ? arcOver : pp == 1 ? arcOverEdge : arcAdd, c, Quaternion.identity, 9999, (b, time) =>
                    {
                        if (!p) return; float v = Vis(time, out var fold);
                        float a = (time * 60 * Spin(time) + jj * 360f / crown) * Mathf.Deg2Rad; float r = 0.42f * (1 - fold);
                        var q = p.transform.position + Vector3.up * Mathf.Lerp(2.0f, 1.3f, Ease(fold)) + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * r;
                        float grow = Ease(Mathf.Clamp01(time / 0.25f));
                        b.go.transform.SetPositionAndRotation(q + (pp == 2 && Cam ? Cam.transform.forward * 0.03f : Vector3.zero), FaceCam(q));
                        if (pp == 2) { b.go.transform.localScale = Vector3.one * 0.32f * grow; b.mpb.SetFloat(PIntensity, 0.2f * v); return; }
                        b.go.transform.localScale = Vector3.one * grow;
                        b.mpb.SetFloat(PProgress, 1.1f); b.mpb.SetFloat(PWidth, 0.09f); b.mpb.SetFloat(PIntensity, (pp == 0 ? 1.0f : 1.0f) * v);
                    }, pp == 2 ? MDot : MStroke, pp == 1 ? EdgeGold : RuneViolet, pp == 1 ? EdgeGoldHot : GlyphHot, AshMagenta)));
                }
            }
        }

        // the chest timer's arc: empties over all but the last second, then holds a 12 % sliver that flickers out
        static float TimerArc(ArcanistKit kit)
        {
            if (kit == null || !kit.Surging) return 0;
            float left = kit.SurgeSeconds, total = left / Mathf.Max(0.001f, kit.SurgeLeft01);
            return left <= 1f ? 0.12f : Mathf.Lerp(0.12f, 1f, (left - 1f) / Mathf.Max(0.01f, total - 1f));
        }

        public void SurgeEnd(PlayerController p)
        {
            if (!p || !arc.TryGetValue(p, out var s)) return;
            if (s.rig) s.rig.SurgeOff();
            if (s.surge.Count == 0) return;
            s.surgeEnding = Time.time;
            var hs = new List<Handle>(s.surge); s.surge.Clear();
            foreach (var h in hs) StartCoroutine(KillAfter(h, 0.23f));
            var c = p.transform.position + Vector3.up * 1.3f;
            for (int k = 0; k < 16; k++) { var off = Random.onUnitSphere * 1.1f; arcMotes.Emit(c + off, -off / 0.22f, 0.06f, 0.22f); }
            StartCoroutine(SurgeGlint(p, 0.22f));
        }
        // the end: one thin gold ring opening at his chest once everything has folded in
        System.Collections.IEnumerator SurgeGlint(PlayerController p, float after)
        {
            yield return new WaitForSeconds(after); if (!p) yield break;
            Arc(RingU(0.9f, 1f, 64), true, arcAdd, p.transform.position + Vector3.up * 1.3f, Quaternion.identity, 0.15f, (b, u) =>
            {
                if (!p) return; b.go.transform.position = p.transform.position + Vector3.up * 1.3f;
                float r = Mathf.Lerp(0.6f, 0.05f, Ease(u)); b.go.transform.localScale = new Vector3(r, 1, r);
                b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PIntensity, 1.0f);
            }, MBeam, RuneViolet, ArmedGold, AshMagenta);
            yield return new WaitForSeconds(0.14f); if (!p) yield break;
            var g = p.transform.position + Vector3.up * 1.3f;
            Arc(billboard, false, arcAdd, g, FaceCam(g), 0.2f, (b, u) =>
            {
                if (!p) return; var q = p.transform.position + Vector3.up * 1.3f - (Cam ? Cam.transform.forward * 0.3f : Vector3.zero);
                b.go.transform.SetPositionAndRotation(q, FaceCam(q)); b.go.transform.localScale = Vector3.one * Mathf.Lerp(0.3f, 0.12f, u); b.mpb.SetFloat(PIntensity, 1.3f * (1 - u));
            }, MDot, ArmedGold, new Color(2.2f, 1.9f, 1.4f), AshMagenta);
        }

        // ================================================================== E5 Arcane Comet
        // The mark is a large rune drawn over the whole fall (its outer ring is the timer); the comet itself starts far
        // up out of view and slams down in the last part of the fall, landing as the rune completes.
        public void CometMark(PlayerController p, int id, Vector3 pos, int fallTicks, bool powered = true)
        {
            EnsureArc();
            float life = fallTicks / (float)Sim.Hz; float R = ArcanistKit.CometRadius, C = ArcanistKit.CometCore;
            var c = new Vector3(pos.x, 0.045f, pos.z);
            // it comes in diagonally from the side of the screen away from the mark (and a little toward the camera), high
            // up: off screen at first, then a meteor streaking across the view and crashing into the mark
            var m = new MarkFx { pos = c, start = Time.time, life = life, from = CometStart(c), ringYaw = Random.value * 360 }; marks[id] = m;
            // ground shadow: wide and faint, tightening (4 m -> 1.33 m) and darkening as the comet comes in, with a thin violet rim
            // that pulses in the last 0.3 s so the landing reads even on a dark floor
            float ShadowE() => Mathf.Clamp01(1 - (m.life - (Time.time - m.start)) / Mathf.Max(0.01f, m.window * 1.6f));
            m.parts.Add(H(Arc(billboard, false, arcAlphaFloorTop, c + Vector3.up * 0.008f, Quaternion.identity, life, (b, u) =>
            {
                float e = ShadowE(); float r = Mathf.Lerp(4f, 1.33f, e * e);
                b.go.transform.localScale = new Vector3(r * 2, 1, r * 2);
                b.mpb.SetFloat(PWidth, Mathf.Lerp(0.85f, 0.55f, e)); b.mpb.SetFloat(PIntensity, e <= 0 ? 0 : Mathf.Lerp(0.15f, m.weak ? 0.38f : 0.55f, e));
            }, MShadow, new Color(0.01f, 0.0f, 0.03f), Void, CometViolet)));
            m.parts.Add(H(Arc(RingU(0.96f, 1f, 96), true, arcAddFloor, c + Vector3.up * 0.01f, Quaternion.identity, life, (b, u) =>
            {
                float e = ShadowE(), left = m.life - (Time.time - m.start); float r = Mathf.Lerp(4f, 1.33f, e * e);
                b.go.transform.localScale = new Vector3(r, 1, r);
                float pulse = left < 0.3f ? 0.6f + 0.5f * Mathf.Abs(Mathf.Sin(left * 40f)) : 0.3f * e;
                b.mpb.SetFloat(PProgress, 1.1f); b.mpb.SetFloat(PWidth, 0.07f); b.mpb.SetFloat(PIntensity, e <= 0 ? 0 : pulse);
            }, MStroke, CometDeep, CometPale, CometDeep)));
            var kit = Arcanist(p); bool surging = kit != null && kit.Surging;
            m.runeFrom = m.parts.Count;
            DrawRune(m, c, R, life, 14, 0.09f, surging ? SurgeInk : CometViolet, surging ? ArmedGold : HotWhite, new Color(0.5f, 0.38f, 1.0f), powered,
                powered && kit != null && !surging ? GlyphFly + 0.02f : 0);
            m.runeTo = m.parts.Count;
            float ws = powered ? 1f : 0.65f;   // cast without a glyph: a smaller meteor for a smaller strike
            // the comet: a faceted charred rock with molten violet cracks and a small white-hot cap on its leading face (Rock
            // shading), a hot bow glow just ahead of it, a faint halo, a two-layer tail, and (LateUpdate) fire puffs shed from its
            // surface that trail into ragged flames, embers and a smoke trail. It waits just above the view and slams down in the last 1.1 s of the fall,
            // stretching 1.4x along its path just before contact. No smooth glowing shells: those read as a jelly ball.
            var cometGo = new GameObject("Arcane comet").transform; cometGo.SetParent(transform, false); cometGo.position = m.from; m.comet = cometGo;
            m.window = Mathf.Min(1.1f, life * 0.45f);
            var fall = (c - m.from).normalized; if (rockMesh == null) rockMesh = Rock(11, 16, 0.42f);
            float Stretch() { float left = m.life - (Time.time - m.start); return left < 0.12f ? Mathf.Lerp(1.4f, 1f, Mathf.Clamp01(left / 0.12f)) : 1f; }
            float Thin() => 1f - 0.15f * (Stretch() - 1f) / 0.4f;
            Quaternion Spin(float t) => Quaternion.LookRotation(fall) * Quaternion.Euler(t * 25, 0, t * 140);
            m.parts.Add(H(Arc(rockMesh, false, arcAlphaSolid, m.from, Quaternion.identity, life, (b, u) =>
            {
                if (!cometGo) return; b.go.transform.SetPositionAndRotation(cometGo.position, Spin(b.t)); b.go.transform.localScale = new Vector3(2.6f * Thin(), 2.6f * Thin(), 2.6f * Stretch()) * ws;
                b.mpb.SetFloat(PIntensity, 1.5f); b.mpb.SetVector(PNoiseScale, new Vector4(2.4f, 0, 0, 0)); b.mpb.SetFloat(PWidth, 0.08f); b.mpb.SetFloat(PEdgeGlow, 0.45f);
                b.mpb.SetVector(PFlow, new Vector4(0.12f, 0.3f, 0.08f, 0)); b.mpb.SetVector(PHeatDir, fall);
            }, MRock, MeteorChar, Molten, WhiteHot)));
            // flame licks: rooted on the leading half of the rock, swept back along the fall, short and fast-flickering, white-hot
            // at the stone fading to violet and burning away at their tips
            for (int f = 0; f < 11; f++)
            {
                float ang = f * (360f / 11) + Random.Range(-12f, 12f), baseLen = Random.Range(1.7f, 2.7f) * ws, wid = Random.Range(0.95f, 1.4f) * ws, ph = Random.value * 10;
                float ahead = Random.Range(0.1f, 0.6f), rr = Random.Range(0.95f, 1.2f);
                m.parts.Add(H(Arc(taperMesh, false, arcAdd, m.from, Quaternion.identity, life, (b, u) =>
                {
                    if (!cometGo) return;
                    // rooted on the rock's outline as the camera sees it, on its sides and back only, so the flames stream away
                    // from the stone instead of across its face
                    var toCam = Cam ? (Cam.transform.position - cometGo.position).normalized : Vector3.up;
                    var backS = Vector3.ProjectOnPlane(-fall, toCam); backS = backS.sqrMagnitude > 1e-4f ? backS.normalized : Vector3.up;
                    var sideS = Vector3.Cross(toCam, backS).normalized;
                    float a = (ang / 360f - 0.5f) * 250f * Mathf.Deg2Rad;   // -125..125 degrees either side of the back
                    var off = backS * Mathf.Cos(a) + sideS * Mathf.Sin(a);
                    var root = cometGo.position + off * (rr + 0.15f) * ws - fall * ahead * 0.5f; var back = (backS * 1.2f + off * 0.5f).normalized;
                    float fl = Mathf.PerlinNoise(b.t * 14f + ph, ph), len = baseLen * (0.6f + 0.6f * fl) * Stretch();
                    var tip = root + back * len;
                    b.go.transform.SetPositionAndRotation(tip, AlongFacingCam(tip, -back)); b.go.transform.localScale = new Vector3(wid * (0.8f + 0.3f * fl), 1, len);
                    b.mpb.SetFloat(PProgress, 1f); b.mpb.SetFloat(PTail, 0.35f); b.mpb.SetFloat(PWidth, 0.06f); b.mpb.SetFloat(PIntensity, 1.25f);
                    b.mpb.SetFloat(PDissolve, 0.04f); b.mpb.SetFloat(PSoft, 0.7f); b.mpb.SetFloat(PEdgeGlow, 0f); b.mpb.SetFloat(PNoiseAmt, 0.75f);
                    b.mpb.SetVector(PNoiseScale, new Vector4(2.5f, 3f, 0, 0)); b.mpb.SetVector(PFlow, new Vector4(-9f, 0.8f, -5f, -1.2f));
                }, MBand, new Color(0.8f, 0.3f, 1.6f), new Color(2.2f, 1.2f, 2.0f), CometViolet)));
            }
            // a soft fire plume streaming back behind the rock (6 m), flame-coloured near it, burning away along its length
            {
                float ph = Random.value * 10;
                m.parts.Add(H(Arc(taperMesh, false, arcAdd, m.from, Quaternion.identity, life, (b, u) =>
                {
                    if (!cometGo) return; float len = 6.5f * ws * (0.9f + 0.2f * Mathf.PerlinNoise(b.t * 9f, ph)); var head = cometGo.position - fall * 1.1f * ws; var tail = head - fall * len;
                    b.go.transform.SetPositionAndRotation(tail, AlongFacingCam(tail, fall)); b.go.transform.localScale = new Vector3(2.8f * ws, 1, len);
                    b.mpb.SetFloat(PProgress, 1f); b.mpb.SetFloat(PTail, 0.3f); b.mpb.SetFloat(PWidth, 0.05f); b.mpb.SetFloat(PIntensity, 0.6f);
                    b.mpb.SetFloat(PDissolve, 0.05f); b.mpb.SetFloat(PSoft, 0.6f); b.mpb.SetFloat(PEdgeGlow, 0f); b.mpb.SetFloat(PNoiseAmt, 0.8f);
                    b.mpb.SetVector(PNoiseScale, new Vector4(4f, 2.5f, 0, 0)); b.mpb.SetVector(PFlow, new Vector4(-8f, 0.5f, -4.5f, -0.8f));
                }, MBand, new Color(0.8f, 0.3f, 1.6f), new Color(2.2f, 1.2f, 2.0f), CometViolet)));
            }
            // the bow glow: compressed hot air just ahead of the leading face
            m.parts.Add(H(Arc(billboard, false, arcAdd, m.from, Quaternion.identity, life, (b, u) =>
            {
                if (!cometGo) return; var q = cometGo.position + fall * 1.5f * Stretch(); b.go.transform.SetPositionAndRotation(q, FaceCam(q));
                b.go.transform.localScale = Vector3.one * (1.6f + 0.15f * Mathf.Sin(b.t * 31)) * ws; b.mpb.SetFloat(PIntensity, 0.9f); b.mpb.SetFloat(PNoiseAmt, 0.3f);
            }, MDot, CometViolet, WhiteHot, CometDeep)));
            m.parts.Add(H(Arc(billboard, false, arcAdd, m.from, Quaternion.identity, life, (b, u) =>
            {
                if (!cometGo) return; b.go.transform.SetPositionAndRotation(cometGo.position, FaceCam(cometGo.position)); b.go.transform.localScale = Vector3.one * 5f; b.mpb.SetFloat(PIntensity, 0.07f);
            }, MDot, DeepViolet, CometDeep, CometDeep)));
            for (int layer = 0; layer < 2; layer++)
            {
                float len = (layer == 0 ? 10f : 5f) * ws, wid = (layer == 0 ? 1.4f : 0.45f) * ws, inten = layer == 0 ? 0.35f : 0.45f;
                m.parts.Add(H(Arc(taperMesh, false, arcAdd, m.from, Quaternion.identity, life, (b, u) =>
                {
                    if (!cometGo) return; var head = cometGo.position - fall * 0.8f; var tail = head - fall * len;
                    b.go.transform.SetPositionAndRotation(tail, AlongFacingCam(tail, fall)); b.go.transform.localScale = new Vector3(wid, 1, len);
                    b.mpb.SetFloat(PWidth, layer == 0 ? 0.5f : 0.3f); b.mpb.SetFloat(PIntensity, inten);
                    b.mpb.SetVector(PFlow, new Vector4(-6f, 0, -3f, 0.8f)); b.mpb.SetFloat(PNoiseAmt, 0.6f); b.mpb.SetVector(PNoiseScale, new Vector4(10, 2.5f, 0, 0));
                }, MBeam, layer == 0 ? CometViolet : CometPale, layer == 0 ? CometPale : WhiteHot, CometDeep)));
            }
            Arc(RingU(0.15f, 0.2f, 24), true, arcAdd, HandPos(p) + Vector3.up * 0.3f, Facing(Vector3.up), 0.25f, (b, u) =>
            {
                b.go.transform.localScale = Vector3.one * (1 + 2 * u); b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 1.1f);
            }, MBeam, CometViolet, HotWhite, AshMagenta);
        }

        // where the comet starts: at about the mark's own distance from the camera (so it doesn't shrink away as it falls), about
        // 50 degrees steep, leaning in from the side of the view with more room, raised until it is just outside the view; it
        // crosses a good part of the screen before it slams down onto the mark (the boss usually sits high on screen)
        static Vector3 CometStart(Vector3 mark)
        {
            var cam = Cam; if (!cam) return mark + Vector3.up * 24f + Vector3.right * 8f;
            var right = Sim.Flat(cam.transform.right).normalized; var far = Sim.Flat(cam.transform.forward).normalized;
            float side = cam.WorldToViewportPoint(mark).x > 0.5f ? -1 : 1;   // lean in from the side with more room
            for (float h = 8f; h <= 60f; h += 1f)
            {
                var at = mark + Vector3.up * h + right * side * h * 0.85f + far * h * 0.1f; var vp = cam.WorldToViewportPoint(at);
                if (vp.z < 0 || vp.y > 1.1f || vp.x < -0.1f || vp.x > 1.1f) return at;
            }
            return mark + Vector3.up * 60f + right * side * 18f;
        }

        Mesh rockMesh; Mesh[] fragMeshes;
        // a lumpy faceted rock of radius about 0.5: a sphere pushed in and out by smooth noise, then split per triangle so every
        // facet has its own flat normal (reads as broken stone, not a smooth glossy ball)
        static Mesh Rock(int lat, int lon, float lump)
        {
            var sm = UvSphere(lat, lon); var v = sm.vertices; var tri = sm.triangles; float sx = Random.value * 10, sy = Random.value * 10;
            for (int i = 0; i < v.Length; i++)
            {
                var d = v[i].normalized;
                float n = Mathf.PerlinNoise(d.x * 1.6f + sx, d.y * 1.6f + d.z * 1.3f + sy), n2 = Mathf.PerlinNoise(d.z * 3.4f + sy, d.x * 3.1f + d.y * 1.2f + sx);
                v[i] = d * 0.5f * (1f - lump * 0.6f + lump * n + lump * 0.4f * n2);
            }
            var fv = new Vector3[tri.Length]; var ft = new int[tri.Length]; var fuv = new Vector2[tri.Length];
            for (int i = 0; i < tri.Length; i++) { fv[i] = v[tri[i]]; ft[i] = i; fuv[i] = new Vector2(0.5f, 0.5f); }
            Destroy(sm);
            var m = new Mesh { name = "FX_MS_Rock", vertices = fv, uv = fuv, triangles = ft }; m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }
        System.Collections.IEnumerator SmokeLater(float delay, Vector3 at, float size)
        {
            yield return new WaitForSeconds(delay); Smoke(at, Vector3.up * 0.6f, size * 1.4f, size * 3.2f, 0.6f, 0.45f);
        }
        // a puff of smoke: alpha-blended, grows and thins out; lit violet in the middle
        void Smoke(Vector3 at, Vector3 vel, float size0, float size1, float life, float alpha)
        {
            var spin = Random.Range(-60f, 60f);
            Arc(billboard, false, arcAlpha, at, FaceCam(at), life, (b, u) =>
            {
                float t = u * life; var q = at + vel * t; b.go.transform.SetPositionAndRotation(q, FaceCam(q) * Quaternion.Euler(0, spin * t, 0));
                b.go.transform.localScale = Vector3.one * Mathf.Lerp(size0, size1, 1 - (1 - u) * (1 - u));
                b.mpb.SetFloat(PNoiseAmt, 0.85f); b.mpb.SetVector(PNoiseScale, new Vector4(2.5f, 2.5f, 0, 0)); b.mpb.SetVector(PFlow, new Vector4(0.3f, 0.4f, -0.2f, 0.3f)); b.mpb.SetFloat(PSoft, 0.7f);
                b.mpb.SetFloat(PDissolve, 0.08f + 0.3f * u); b.mpb.SetFloat(PEdgeGlow, 0f);
                b.mpb.SetFloat(PIntensity, alpha * Mathf.Clamp01(u / 0.15f) * (1 - u));
            }, MDot, new Color(0.12f, 0.09f, 0.15f), new Color(0.26f, 0.2f, 0.32f), CometDeep);
        }

        public void CometImpact(int id, Vector3 pos, bool hit, bool bull)
        {
            if (!marks.TryGetValue(id, out var m)) return;
            marks.Remove(id);
            m.snuffAt = Time.time;
            for (int i = 0; i < m.parts.Count; i++)
            {
                var h = m.parts[i];
                if (i < m.runeFrom || i >= m.runeTo || !h.Alive(this)) { KillH(h); continue; }
                h.b.life = h.b.t + 0.13f;   // the rune's own anims dim it to ash and erode it over 0.12 s
            }
            if (m.comet) Destroy(m.comet.gameObject);
            var c = new Vector3(pos.x, 0.05f, pos.z); float R = ArcanistKit.CometRadius;
            if (m.rings != null) foreach (var rr in m.rings)
                    for (int j = 0; j < 4; j++)
                    {
                        float a = Random.value * Mathf.PI * 2; var q = c + new Vector3(Mathf.Sin(a), 0.05f, Mathf.Cos(a)) * rr;
                        ashFx.Emit(q, Vector3.up * Random.Range(0.3f, 0.6f), Random.Range(0.07f, 0.11f), Random.Range(0.5f, 0.7f));
                    }
            float k = m.weak ? 0.6f : 1f, kb = m.weak ? 0.5f : 1f;   // cast without a glyph: a smaller strike
            var hot = new Color(1.1f, 0.75f, 2.2f);   // the impact's hottest violet; white only at the very core
            // the scar, drawn under the blast: the rune is snuffed out (it goes straight to ash and erodes; it does not explode
            // itself, the meteor does), a broken-edged crater, jagged seams; all of it holds for 0.3 s, then erodes away
            BurntRune(m, c, 1.6f, 0.09f, kb, false);
            Arc(billboard, false, arcAlphaFloor, c, Quaternion.identity, 1.8f, (b, u) =>
            {
                float t = u * 1.8f; b.go.transform.localScale = new Vector3(3.2f * k, 1, 3.2f * k);
                b.mpb.SetFloat(PWidth, 0.55f); b.mpb.SetFloat(PNoiseAmt, 0.55f); b.mpb.SetVector(PNoiseScale, new Vector4(5, 5, 0, 0));
                b.mpb.SetFloat(PIntensity, 0.85f * (1 - Ease(Mathf.Clamp01((t - 0.3f) / 1.5f))));
            }, MShadow, new Color(0.03f, 0.01f, 0.05f), Void, CometViolet);
            for (int j = 0; j < 7; j++)
            {
                float a = (j * 360f / 7 + Random.Range(-14f, 14f)) * Mathf.Deg2Rad; var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                var seam = JaggedCrack(dir, 0.35f, Random.Range(1.6f, 2.4f) * k, 0.13f, 8f, 16f);
                Arc(seam, true, arcAddFloor, c, Quaternion.identity, 1.8f, (b, u) =>
                {
                    float t = u * 1.8f;
                    b.mpb.SetFloat(PProgress, Mathf.Lerp(0.15f, 1.1f, Ease(Mathf.Clamp01(t / 0.12f)))); b.mpb.SetFloat(PWidth, 0.08f);
                    b.mpb.SetFloat(PDissolve, Mathf.Clamp01((t - 0.4f) / 1.4f)); b.mpb.SetFloat(PIntensity, (1.5f + 1.0f * Mathf.Clamp01(1 - t / 0.5f)) * kb);
                }, MStroke, new Color(0.55f, 0.3f, 1.8f), new Color(0.9f, 0.62f, 2.3f), CometDeep);
            }
            // contact: a small white-hot core at ground zero inside a short violet halo
            Arc(billboard, false, arcAdd, c + Vector3.up * 0.6f, FaceCam(c), 0.12f, (b, u) =>
            {
                b.go.transform.localScale = Vector3.one * Mathf.Lerp(1.8f, 1.2f, u) * k; b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 1.7f * kb);
            }, MDot, hot, new Color(2.4f, 2.2f, 2.6f), CometViolet);
            Arc(billboard, false, arcAdd, c + Vector3.up * 1f, FaceCam(c), 0.2f, (b, u) =>
            {
                b.go.transform.localScale = Vector3.one * Mathf.Lerp(4f, 5.5f, Ease(u)) * k; b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 0.9f * kb * (1 - u));
            }, MDot, DeepViolet, CometViolet, CometViolet);
            // the fireball: the meteor's fire bursts out of the crater as flame tongues thrown up and out, gone in about 0.3 s
            for (int j = 0; j < (m.weak ? 8 : 14); j++)
            {
                var d = Quaternion.Euler(0, j * (360f / (m.weak ? 8 : 14)) + Random.Range(-12f, 12f), 0) * Vector3.forward;
                var dirF = (d + Vector3.up * Random.Range(0.5f, 1.6f)).normalized; float len = Random.Range(3.0f, 4.6f) * k, wid = Random.Range(1.5f, 2.2f) * k, fl = Random.Range(0.28f, 0.36f);
                var root = c + Vector3.up * 0.3f + d * 0.4f * k;
                Arc(taperMesh, false, arcAdd, root, Quaternion.identity, fl, (b, u) =>
                {
                    float g = Ease(Mathf.Clamp01(u / 0.4f)), l = Mathf.Max(0.05f, len * g); var tip = root + dirF * l;
                    b.go.transform.SetPositionAndRotation(tip, AlongFacingCam(tip, -dirF)); b.go.transform.localScale = new Vector3(wid, 1, l);
                    b.mpb.SetFloat(PProgress, 1f); b.mpb.SetFloat(PTail, 0.4f); b.mpb.SetFloat(PWidth, 0.06f); b.mpb.SetFloat(PIntensity, 1.6f * kb);
                    b.mpb.SetFloat(PDissolve, 0.05f + 0.9f * u * u); b.mpb.SetFloat(PSoft, 0.6f); b.mpb.SetFloat(PEdgeGlow, 0f); b.mpb.SetFloat(PNoiseAmt, 0.8f);
                    b.mpb.SetVector(PNoiseScale, new Vector4(2.5f, 3f, 0, 0)); b.mpb.SetVector(PFlow, new Vector4(-6f, 0.8f, -3.5f, -1.2f));
                }, MBand, new Color(0.8f, 0.3f, 1.6f), new Color(2.2f, 1.2f, 2.0f), CometViolet);
            }
            // a soft fire bloom that rises about 1 m and burns out by 0.35 s
            Arc(billboard, false, arcAdd, c + Vector3.up * 0.8f, FaceCam(c), 0.35f, (b, u) =>
            {
                var q = c + Vector3.up * (0.9f + 1.2f * Ease(u)) * k; b.go.transform.SetPositionAndRotation(q, FaceCam(q));
                b.go.transform.localScale = Vector3.one * Mathf.Lerp(2.8f, 4.6f, Ease(u)) * k;
                b.mpb.SetFloat(PNoiseAmt, 0.7f); b.mpb.SetVector(PNoiseScale, new Vector4(3, 3, 0, 0)); b.mpb.SetVector(PFlow, new Vector4(0.5f, -2f, -0.4f, -1.4f));
                b.mpb.SetFloat(PDissolve, u < 0.3f ? 0 : (u - 0.3f) * (u - 0.3f) * 2f); b.mpb.SetFloat(PSoft, 0.5f); b.mpb.SetFloat(PEdgeGlow, 0f); b.mpb.SetFloat(PIntensity, 1.4f * kb);
            }, MDot, new Color(0.8f, 0.3f, 1.6f), new Color(2.2f, 1.2f, 2.0f), CometViolet);
            for (int j = 0; j < (m.weak ? 24 : 48); j++)
            {
                var d = Random.onUnitSphere; d.y = Mathf.Abs(d.y) * 0.8f + 0.2f;
                flameFx.Emit(c + Vector3.up * 0.5f + d * 0.5f * k, d * Random.Range(4f, 8f) * k, Random.Range(1.0f, 1.8f) * k, Random.Range(0.25f, 0.4f));
            }
            for (int j = 0; j < (m.weak ? 12 : 24); j++)
                embers.Emit(c + Vector3.up * 0.6f, (Random.insideUnitSphere * 0.8f + Vector3.up) * Random.Range(4f, 8f) * k, Random.Range(0.1f, 0.18f), Random.Range(0.5f, 0.9f));
            // then a dark smoke plume rolls up and out of it
            for (int j = 0; j < (m.weak ? 6 : 12); j++)
            {
                var d = Quaternion.Euler(0, j * (m.weak ? 60f : 30f) + Random.Range(-12f, 12f), 0) * Vector3.forward;
                Smoke(c + d * 1.0f * k + Vector3.up * 0.4f, (d * Random.Range(1.2f, 2.0f) + Vector3.up * Random.Range(1.4f, 1.9f)) * k, 1.2f * k, 3.4f * k, Random.Range(0.8f, 1.0f), 1.1f);
            }
            // two thin shockwaves racing out past the rune
            for (int j = 0; j < 2; j++)
            {
                float delay = j * 0.08f, far = (R + 2f + j * 1.5f) * k, life = 0.42f + delay;
                Arc(RingU(0.985f, 1f, 160), true, arcAddFloor, c, Quaternion.identity, life, (b, u) =>
                {
                    float t = Mathf.Clamp01((u * life - delay) / 0.42f); float r = Mathf.Lerp(0.8f, far, Ease(t)); b.go.transform.localScale = new Vector3(r, 1, r);
                    b.mpb.SetFloat(PProgress, 1.1f); b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PDissolve, Mathf.Clamp01((t - 0.4f) / 0.6f)); b.mpb.SetFloat(PNoiseAmt, 0.6f); b.mpb.SetFloat(PIntensity, t <= 0 ? 0 : 0.8f * kb);
                }, MStroke, new Color(0.6f, 0.55f, 0.8f), new Color(1.1f, 1.0f, 1.4f), new Color(0.6f, 0.55f, 0.8f));
            }
            // the meteor itself breaks up: charred fragments with glowing cracks thrown out and up, tumbling under gravity,
            // landing within about 4 m and burning away after they land
            if (fragMeshes == null) { fragMeshes = new Mesh[3]; for (int j = 0; j < 3; j++) fragMeshes[j] = Rock(4, 5, 0.9f); }
            int chunks = m.weak ? 6 : 11;
            for (int j = 0; j < chunks; j++)
            {
                var dir = Quaternion.Euler(0, Random.value * 360f, 0) * Vector3.forward;
                var v = dir * Random.Range(1.6f, 5.0f) * k + Vector3.up * Random.Range(2.5f, 5.5f); const float g = 13f; var start = c + dir * 0.6f * k + Vector3.up * 0.6f;
                float size = Random.Range(0.22f, 0.46f) * k, land = (v.y + Mathf.Sqrt(v.y * v.y + 2 * g * 0.5f)) / g; var spinAxis = Random.onUnitSphere; float spin = Random.Range(360f, 900f);
                var rot0 = Random.rotation; var shape = new Vector3(Random.Range(0.8f, 1.3f), Random.Range(0.6f, 1f), Random.Range(0.9f, 1.4f)); var mesh = fragMeshes[j % 3];
                Arc(mesh, false, arcAlphaSolid, start, rot0, land + 0.45f, (b, u) =>
                {
                    float t = u * (land + 0.45f), tf = Mathf.Min(t, land); var at = start + v * tf + Vector3.down * 0.5f * g * tf * tf; at.y = Mathf.Max(at.y, size * 0.3f);
                    if (t > land) { float tb = t - land; at += Sim.Flat(v) * 0.15f * tb + Vector3.up * Mathf.Max(0, 1.6f * tb - 8f * tb * tb); }   // a small bounce
                    var vel = v + Vector3.down * g * tf;
                    b.go.transform.position = at; b.go.transform.rotation = Quaternion.AngleAxis(spin * tf, spinAxis) * rot0; b.go.transform.localScale = shape * size;
                    b.mpb.SetVector(PNoiseScale, new Vector4(3f, 0, 0, 0)); b.mpb.SetFloat(PWidth, 0.05f); b.mpb.SetFloat(PEdgeGlow, t < land ? 0.25f : 0.1f);
                    b.mpb.SetVector(PHeatDir, t < land ? vel : Vector3.up); b.mpb.SetFloat(PSoft, 0.15f);
                    b.mpb.SetFloat(PDissolve, Mathf.Clamp01((t - land) / 0.45f)); b.mpb.SetFloat(PIntensity, 1.5f);
                }, MRock, MeteorChar, Molten, WhiteHot);
                // a smoke wisp from each fragment
                for (int w = 0; w < 3; w++) { float tw = land * (0.2f + 0.3f * w); var pw = start + v * tw + Vector3.down * 0.5f * g * tw * tw; StartCoroutine(SmokeLater(tw, pw, size)); }
                // a short glowing streak behind each fragment while it flies
                Arc(taperMesh, false, arcAdd, start, Quaternion.identity, land, (b, u) =>
                {
                    float tf = u * land; var at = start + v * tf + Vector3.down * 0.5f * g * tf * tf; var vel = v + Vector3.down * g * tf; float len = Mathf.Min(0.6f, vel.magnitude * 0.1f);
                    var tail = at - vel.normalized * len; b.go.transform.SetPositionAndRotation(tail, AlongFacingCam(tail, vel)); b.go.transform.localScale = new Vector3(size * 0.9f, 1, len);
                    b.mpb.SetFloat(PWidth, 0.35f); b.mpb.SetFloat(PIntensity, 0.55f * (1 - u * 0.6f));
                }, MBeam, CometDeep, Molten, CometDeep);
            }
            // a few embers rising from the crater
            for (int j = 0; j < 10; j++)
            {
                var off = Random.insideUnitCircle * 1.6f * k;
                embers.Emit(c + new Vector3(off.x, 0.2f, off.y), Vector3.up * Random.Range(0.6f, 1.0f) + new Vector3(off.x, 0, off.y) * 0.1f, 0.09f, Random.Range(1.0f, 1.4f));
            }
            StartCoroutine(CraterEmbers(c, k));
            for (int j = 0; j < 20; j++) arcSparks.Emit(c + Vector3.up * 0.6f, (Random.insideUnitSphere + Vector3.up) * 7f * k, 0.08f, 0.4f);
            ArcLight(c + Vector3.up * 0.6f, CometViolet, (bull ? 16f : 13f) * kb, 8f * k, 0.3f);
            CameraShake.Shake(m.weak ? 0.3f : bull ? 0.8f : 0.55f, bull ? 0.45f : 0.38f);
        }
        public void CometFade(int id) => FadeMark(id);
        // a few more embers lifting off the seams over the next second
        System.Collections.IEnumerator CraterEmbers(Vector3 c, float k)
        {
            for (int i = 0; i < 6; i++)
            {
                yield return new WaitForSeconds(0.15f);
                var off = Random.insideUnitCircle * 1.8f * k; embers.Emit(c + new Vector3(off.x, 0.15f, off.y), Vector3.up * Random.Range(0.5f, 0.9f), 0.08f, Random.Range(1.0f, 1.3f));
            }
        }

        Mesh crystalMesh;
        // a faceted crystal: a six-sided bipyramid 1 m long along Z, flat-shaded so its facets catch the rim light
        static Mesh Crystal()
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            var top = new Vector3(0, 0, 0.5f); var bot = new Vector3(0, 0, -0.5f);
            void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                var nn = Vector3.Cross(b - a, c - a).normalized; int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); n.Add(nn); n.Add(nn); n.Add(nn); uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(0.5f, 1));
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }
            for (int i = 0; i < 6; i++)
            {
                float a0 = i * Mathf.PI / 3, a1 = (i + 1) * Mathf.PI / 3;
                var p0 = new Vector3(Mathf.Cos(a0) * 0.28f, Mathf.Sin(a0) * 0.28f, 0.08f); var p1 = new Vector3(Mathf.Cos(a1) * 0.28f, Mathf.Sin(a1) * 0.28f, 0.08f);
                Tri(top, p0, p1); Tri(bot, p1, p0);
            }
            var m = new Mesh { name = "FX_MS_Crystal" }; m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
        }
        // a jagged crack: 4 segments kinking 15-30° alternately, narrowing toward its tip
        static Mesh JaggedCrack(Vector3 dir, float start, float len, float w, float kinkMin = 15f, float kinkMax = 30f)
        {
            var pts = new List<Vector3> { dir * start }; var head = dir; float sign = Random.value < 0.5f ? 1 : -1;
            for (int i = 1; i <= 4; i++) { head = Quaternion.Euler(0, sign * Random.Range(kinkMin, kinkMax), 0) * (i == 1 ? dir : head); sign = -sign; pts.Add(pts[i - 1] + head * len / 4); }
            var vs = new List<Vector3>(); var uv = new List<Vector2>(); var tr = new List<int>();
            for (int i = 0; i < pts.Count; i++)
            {
                var d = (i < pts.Count - 1 ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1]).normalized; var nn = Vector3.Cross(Vector3.up, d) * (w / 2 * (1 - 0.65f * i / (pts.Count - 1)));
                vs.Add(pts[i] - nn); vs.Add(pts[i] + nn); float f = (float)i / (pts.Count - 1); uv.Add(new Vector2(f, 0)); uv.Add(new Vector2(f, 1));
                if (i < pts.Count - 1) { int b = i * 2; tr.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 }); }
            }
            var m = new Mesh { name = "FX_MS_JaggedCrack" }; m.SetVertices(vs); m.SetUVs(0, uv); m.SetTriangles(tr, 0); m.RecalculateBounds(); return m;
        }

        // ================================================================== E6 Blink
        public void BlinkOut(PlayerController p)
        {
            var s = St(p); var at = p.transform.position; s.blinkFrom = at;
            Arc(DashedRing(0.5f, 0.54f, 6, 14, 10), true, arcAddFloor, new Vector3(at.x, 0.05f, at.z), Quaternion.Euler(0, Random.value * 60, 0), 0.15f, (b, u) =>
            {
                b.go.transform.localScale = Vector3.one * Mathf.Lerp(0.5f, 1.15f, Ease(u)); b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 1.2f);
            }, MBeam, BlinkLow, BlinkHigh, BlinkLow);
            // the departure silhouette: a snapshot of his current pose as a hollow rim-lit violet shell, gone in 0.15 s
            s.departShell = BakeShell(s, 0.15f, 0.9f, true, false, 0, BlinkLow, BlinkHigh);
            var chest = at + Vector3.up * 1.1f;
            for (int k = 0; k < 14; k++) arcMotes.Emit(chest + Random.insideUnitSphere * 0.45f, Random.insideUnitSphere * 1.2f + Vector3.up * 0.4f, 0.1f, Random.Range(0.2f, 0.25f));
            // four angular shards thrown 0.6-0.9 m outward and up
            for (int k = 0; k < 4; k++)
            {
                var dir = Quaternion.Euler(0, k * 90 + Random.Range(-25f, 25f), 0) * Vector3.forward; var v = (dir * Random.Range(2.4f, 3.6f) + Vector3.up * 1.5f); var start = chest;
                var spin = Random.Range(-720f, 720f);
                Arc(dartMesh, false, arcAdd, start, Quaternion.LookRotation(v), 0.25f, (b, u) =>
                {
                    float t = u * 0.25f; b.go.transform.position = start + v * t + Vector3.down * 4.9f * t * t; b.go.transform.rotation = Quaternion.LookRotation(v) * Quaternion.Euler(spin * t, 0, 0);
                    b.go.transform.localScale = new Vector3(0.6f, 0.6f, 0.28f); b.mpb.SetFloat(PDissolve, 0.15f + 0.85f * Mathf.Clamp01((u - 0.4f) / 0.6f)); b.mpb.SetFloat(PEdgeGlow, 1.4f); b.mpb.SetFloat(PIntensity, 1.0f);
                }, MDart, new Color(0.3f, 0.15f, 0.9f), BlinkHigh, BlinkHigh);
            }
            KillH(s.thread);
            // the travel thread: thin, tapered (widest where he left), faint, with motes running along it toward him
            s.thread = H(Arc(beamMesh, false, arcAdd, chest, Quaternion.identity, 9999, (b, time) =>
            {
                if (!p) return; var to = s.blinkFrom + Vector3.up * 0.6f; var from = p.transform.position + Vector3.up * 0.6f; var d = to - from;
                if (d.sqrMagnitude < 1e-4f) { b.go.transform.localScale = Vector3.zero; return; }
                b.go.transform.SetPositionAndRotation(from, AlongFacingCam(from, d)); b.go.transform.localScale = new Vector3(0.025f, 1, d.magnitude);
                b.mpb.SetFloat(PWidth, 0.5f); b.mpb.SetFloat(PIntensity, 0.5f);
                if (Random.value < 0.5f) arcMotes.Emit(to - d * Random.value * 0.8f, -d.normalized * 18f, 0.05f, 0.08f);
            }, MBeam, BlinkLow, BlinkHigh, BlinkLow));
        }
        // a snapshot of his current pose as a rim-lit lilac shell. erode: eaten away (departure); follow: stays on his drawn
        // body (arrival, blocked); rebuild > 0: drawn only up to a height that rises from his feet over `rebuild` seconds
        List<Handle> BakeShell(ArcState s, float life, float intensity, bool erode, bool follow = false, float rebuild = 0, Color? low = null, Color? high = null)
        {
            var made = new List<Handle>();
            if (s.skins == null) return made;
            foreach (var sk in s.skins)
            {
                if (!sk) continue;
                var mesh = new Mesh(); sk.BakeMesh(mesh, true);
                var tf = sk.transform; float feet = tf.position.y;
                foreach (var t in tf.root.GetComponentsInChildren<PlayerController>(true)) { feet = t.transform.position.y; break; }
                made.Add(H(Arc(mesh, true, arcAdd, tf.position, tf.rotation, life, (b, u) =>
                {
                    if (follow && tf) b.go.transform.SetPositionAndRotation(tf.position, tf.rotation);
                    b.mpb.SetFloat(PFresnel, 2.5f); b.mpb.SetFloat(PDissolve, erode ? Mathf.Clamp01(u * 1.05f) : 0);
                    // a rebuild shell fades out in the last 0.05 s once the cut has passed his head
                    float tail = rebuild > 0 ? Mathf.Clamp01((life - u * life) / 0.05f) : 1;
                    b.mpb.SetFloat(PIntensity, intensity * (erode || rebuild > 0 ? tail : 1 - u));
                    b.mpb.SetVector(PNoiseScale, new Vector4(14, 14, 0, 0)); b.mpb.SetFloat(PNoiseAmt, erode ? 0.6f : 0.2f); b.mpb.SetFloat(PSoft, 0.08f);
                    if (rebuild > 0) b.mpb.SetFloat(PClipY, feet - 0.05f + 1.95f * Ease(Mathf.Clamp01(u * life / rebuild)));
                }, MShell, low ?? ArcLilac, high ?? HotWhite, high ?? AshMagenta)));
            }
            return made;
        }

        public void BlinkIn(PlayerController p)
        {
            var s = St(p); var at = p.transform.position;
            if (s.thread != null && s.thread.Alive(this))
            {
                var b0 = s.thread.b; float t0 = b0.t;
                b0.life = b0.t + 0.05f; b0.anim = (b, u) => { b.mpb.SetFloat(PDissolve, Mathf.Clamp01((b.t - t0) / 0.05f)); };
            }
            s.thread = null;
            var chest = at + Vector3.up * 1.0f;
            for (int k = 0; k < 14; k++) { var off = new Vector3(Random.Range(-0.8f, 0.8f), Random.Range(-0.9f, 0.7f), Random.Range(-0.8f, 0.8f)); arcMotes.Emit(chest + off, -off / 0.08f, 0.08f, 0.08f); }
            // rebuilt from the feet up: his body stays hidden 0.12 s while a lilac shell of him is drawn up to a rising cut
            // with a bright band at the cut, then the body shows and the shell fades on it. Everything follows his drawn spot.
            Hide(p, true);   // same tick the kit showed him: the body is never drawn before the rebuild
            StartCoroutine(Rebuild(p, s, 0.16f));
            // a crisp thin violet floor ring at his feet, expanding 0.3 -> 0.9 m over 0.15 s
            Arc(RingU(0.965f, 1f, 64), true, arcAddFloor, new Vector3(at.x, 0.05f, at.z), Quaternion.identity, 0.18f, (b, u) =>
            {
                if (p) { var pp = p.transform.position; b.go.transform.position = new Vector3(pp.x, 0.05f, pp.z); }
                float r = Mathf.Lerp(0.3f, 0.6f, Ease(Mathf.Clamp01(u / 0.6f))); b.go.transform.localScale = new Vector3(r, 1, r);
                b.mpb.SetFloat(PProgress, 1.1f); b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PDissolve, u * u); b.mpb.SetFloat(PIntensity, 1.6f);
            }, MStroke, BlinkLow, BlinkHigh, BlinkLow);
            StartCoroutine(AstroPulse(p, 0.15f));
            ArcLight(chest, ArcLilac, 0.9f, 3f, 0.15f);
        }
        // one astrolabe pulse: a small ring at the astrolabe (0.25 m), 0.1 s, once the rebuild band passes his shoulder
        System.Collections.IEnumerator AstroPulse(PlayerController p, float after)
        {
            yield return new WaitForSeconds(after); if (!p) yield break;
            var a = AstroPos(p); var rs = St(p).rig; if (rs) rs.Flash();
            Arc(RingU(0.23f, 0.25f, 32), true, arcAdd, a, FaceCam(a), 0.1f, (b, u) =>
            {
                if (!p) return; var q = AstroPos(p); b.go.transform.SetPositionAndRotation(q, FaceCam(q)); b.go.transform.localScale = Vector3.one * Mathf.Lerp(0.8f, 1.25f, u);
                b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 0.8f);   // one soft pulse
            }, MBeam, ArcLilac, HotWhite, AshMagenta);
        }

        System.Collections.IEnumerator Rebuild(PlayerController p, ArcState s, float seconds)
        {
            if (!p) yield break;
            yield return new WaitForEndOfFrame();   // bake the pose he is drawn in this frame
            if (!p) yield break;
            Hide(p, true);
            BakeShell(s, seconds + 0.05f, 0.9f, false, follow: true, rebuild: seconds, low: BlinkLow, high: BlinkHigh);
            yield return new WaitForSeconds(seconds);
            if (p && !(p.Kit is ArcanistKit k && k.Blinking)) Hide(p, false);
        }

        public void BlinkBlocked(PlayerController p)
        {
            var s = St(p);
            if (s.thread != null) { KillH(s.thread); s.thread = null; }
            // the departure silhouette goes at once, so it never stands beside his body
            if (s.departShell != null) foreach (var h in s.departShell) if (h.Alive(this)) { var b0 = h.b; float t0 = b0.t; b0.life = b0.t + 0.04f; }
            StartCoroutine(Flicker(p, s));
        }
        // stunned mid-blink: two flickers where the "off" frames show his lilac shell at 60 % (never nothing), then 8 motes
        // snap in to his chest and one small rim pulse
        System.Collections.IEnumerator Flicker(PlayerController p, ArcState s)
        {
            for (int k = 0; k < 2; k++)
            {
                if (!p) yield break; yield return new WaitForEndOfFrame();
                Hide(p, true); BakeShell(s, 0.05f, 0.6f, false, follow: true, low: BlinkLow, high: BlinkHigh);
                yield return new WaitForSeconds(0.05f);
                if (!p) yield break; Hide(p, false); yield return new WaitForSeconds(0.05f);
            }
            if (!p) yield break;
            var chest = p.transform.position + Vector3.up * 1.0f;
            for (int k = 0; k < 6; k++) { var off = Random.onUnitSphere * 0.7f; arcMotes.Emit(chest + off, -off / 0.08f, 0.08f, 0.08f); }
        }
    }
}
