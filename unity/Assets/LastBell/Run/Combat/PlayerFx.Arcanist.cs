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
        public static readonly Color ArcLilac = new Color(0.85f, 0.5f, 1.6f), HotWhite = new Color(1.7f, 1.45f, 2.0f),
            Ultraviolet = new Color(0.55f, 0.18f, 1.5f), UVCore = new Color(1.3f, 0.95f, 2.0f), Void = new Color(0.05f, 0.01f, 0.10f),
            AshMagenta = new Color(1.6f, 0.35f, 1.2f), RuneViolet = new Color(0.7f, 0.38f, 1.6f), ArmedGold = new Color(1.8f, 1.35f, 0.7f),
            StarSilver = new Color(1.45f, 1.45f, 1.7f), StarEdge = new Color(0.8f, 0.45f, 1.6f), CometViolet = new Color(0.75f, 0.38f, 1.7f),
            DeepViolet = new Color(0.22f, 0.08f, 0.45f), BurntViolet = new Color(0.16f, 0.1f, 0.24f), BurntGrey = new Color(0.35f, 0.25f, 0.5f);
        // kept for older callers (menu battle bolts)
        public static readonly Color TimeTeal = new Color(0.45f, 1.9f, 1.75f);

        static readonly int PT = Shader.PropertyToID("_T"), PNoiseAmt = Shader.PropertyToID("_NoiseAmt"), PFresnel = Shader.PropertyToID("_Fresnel"),
            PSoft = Shader.PropertyToID("_Soft"), PEdgeGlow = Shader.PropertyToID("_EdgeGlow"), PNoiseScale = Shader.PropertyToID("_NoiseScale"),
            PFlow = Shader.PropertyToID("_Flow"), PClipY = Shader.PropertyToID("_ClipY");
        const float MBand = 0, MDot = 1, MStroke = 2, MBeam = 3, MShell = 4, MFlake = 5, MShadow = 6, MDart = 7, MPillar = 8;

        bool arcReady;
        Material arcAdd, arcAddFloor, arcAlpha, arcAlphaFloor, ashMat, arcTrailMat, silverTrailMat;
        FxParticles arcMotes, arcSparks, ashFx, embers;
        Mesh sphereMesh, dartMesh, shardMesh, beamMesh, billboard, taperMesh, helixMesh, colShell;
        readonly List<TrailRenderer> trailPool = new List<TrailRenderer>();
        Light[] arcLights; float[] lightLeft, lightLife, lightPeak;

        sealed class Handle { public Burst b; public int id; public bool Alive(PlayerFx f) => b != null && b.id == id && f.live.Contains(b); }
        sealed class ArcState
        {
            public Transform hand, astrolabe; public SkinnedMeshRenderer[] skins;
            public Handle glyph; public float glyphFill, glyphPulse = -9; public readonly Handle[] darts = new Handle[3];
            public readonly List<Handle> lens = new List<Handle>(); public readonly List<Handle> beam = new List<Handle>();
            public Vector3 beamFrom, beamEnd; public int beamStep; public bool beamHit; public float beamStart, ashAcc, ashTail = -9, stepPulse = -9, shedAcc, shakeAt, debrisAt;
            public readonly List<Handle> orbCharge = new List<Handle>(); public readonly List<Handle> orb = new List<Handle>();
            public float orbStart = -1, orbEnding = -1; public Vector3 orbLift;
            public Handle thread; public Vector3 blinkFrom;
        }
        readonly Dictionary<PlayerController, ArcState> arc = new Dictionary<PlayerController, ArcState>();
        sealed class MarkFx { public readonly List<Handle> parts = new List<Handle>(); public Vector3 pos, from; public float start, life; public Transform comet; public float emberAcc; public List<(Vector3 a, Vector3 b)> strokes; public List<Mesh> glyphMeshes; public List<float> rings; public List<(Vector3 at, Quaternion rot)> glyphPose; public float ringYaw, glyphSize, line; }
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
            arcMotes = FxParticles.Create(transform, "Arcanist motes", DotMat(sh, ArcLilac, HotWhite), 600, 0f, 2.5f, 0f);
            arcSparks = FxParticles.Create(transform, "Arcanist sparks", DotMat(sh, ArcLilac, HotWhite), 400, 5f, 1.6f, 0.06f);
            embers = FxParticles.Create(transform, "Arcanist embers", DotMat(sh, AshMagenta, new Color(1.9f, 0.8f, 1.6f)), 200, 2f, 1.2f, 0.05f);
            ashMat = new Material(sh); ashMat.SetFloat(PSrc, 1); ashMat.SetFloat(PDst, 10); ashMat.SetFloat(PMode, MFlake);
            ashMat.SetColor(PLow, new Color(0.05f, 0.02f, 0.06f)); ashMat.SetColor(PHigh, new Color(0.10f, 0.05f, 0.12f)); ashMat.SetColor(PEdge, AshMagenta);
            ashMat.SetFloat(PNoiseAmt, 0.7f); ashMat.SetVector(PNoiseScale, new Vector4(3, 3, 0, 0)); ashMat.SetFloat(PSoft, 0.1f); ashMat.SetFloat(PEdgeGlow, 1.6f);
            ashMat.SetFloat(PDissolve, 0.2f);   // the glowing erosion edge shows from the start
            ashFx = FxParticles.Create(transform, "Arcanist ash", ashMat, 300, 1.5f, 0.4f, 0f);
            arcTrailMat = new Material(sh); arcTrailMat.SetFloat(PMode, MBeam); arcTrailMat.SetFloat(PWidth, 0.35f); arcTrailMat.SetColor(PLow, ArcLilac); arcTrailMat.SetColor(PHigh, HotWhite);
            arcTrailMat.SetVector(PFlow, new Vector4(2.5f, 0, 1.2f, 0.4f)); arcTrailMat.SetFloat(PIntensity, 0.85f);
            silverTrailMat = new Material(arcTrailMat); silverTrailMat.SetColor(PLow, StarEdge); silverTrailMat.SetColor(PHigh, StarSilver);
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere); sphereMesh = tmp.GetComponent<MeshFilter>().sharedMesh; Destroy(tmp);
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
                        if (t.name == "Astrolabe") s.astrolabe = t;
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
        Vector3 AstroPos(PlayerController p) { var s = St(p); return s.astrolabe ? s.astrolabe.position : p.transform.position + Vector3.up * 1.6f + p.transform.right * 0.5f - p.transform.forward * 0.1f; }
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
            arcMotes.Clear(); arcSparks.Clear(); ashFx.Clear(); embers.Clear();
        }
        public void ArcanistReset(PlayerController p)
        {
            if (!arcReady) return;
            if (arc.TryGetValue(p, out var s))
            {
                KillH(s.glyph); foreach (var d in s.darts) KillH(d);
                foreach (var h in s.lens) KillH(h); foreach (var h in s.beam) KillH(h); foreach (var h in s.orbCharge) KillH(h); foreach (var h in s.orb) KillH(h);
                KillH(s.thread); arc.Remove(p);
            }
            foreach (var m in marks.Values) { foreach (var h in m.parts) KillH(h); if (m.comet) Destroy(m.comet.gameObject); }
            marks.Clear();
        }

        void LateUpdate()
        {
            if (!arcReady) return;
            float dt = Time.deltaTime;
            arcTrailMat.SetFloat(PT, Time.time); silverTrailMat.SetFloat(PT, Time.time);
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
                s.ashAcc += dt * (s.beamStep == 1 ? 12 : s.beamStep == 2 ? 20 : 40);
                var at = s.beamEnd; var back = (s.beamFrom - s.beamEnd); back.y = 0; back = back.sqrMagnitude > 1e-4f ? back.normalized : Vector3.back;
                while (s.ashAcc >= 1)
                {
                    s.ashAcc -= 1;
                    var v = back * Random.Range(0.6f, 1.0f) + Random.insideUnitSphere * 0.45f + Vector3.up * 0.4f;
                    ashFx.Emit(at + Random.insideUnitSphere * 0.2f, v, Random.Range(0.22f, 0.3f), Random.Range(0.8f, 1.2f));
                    if (Random.value < 0.14f) embers.Emit(at, back * 2.5f + Random.insideUnitSphere * 1.5f + Vector3.up, 0.05f, 0.12f);
                }
            }
            // comets in the sky: fall between the ticks with ease-in; embers peel off sideways
            foreach (var m in marks.Values)
            {
                if (!m.comet) continue;
                // a near-steady plunge (slightly accelerating): out of view early on, then a huge meteor crashing down
                // across the screen through the last ~0.7 s
                float u = Mathf.Clamp01((Time.time - m.start) / m.life); float left = Mathf.Pow(1 - u, 1.15f);
                m.comet.position = Vector3.Lerp(m.pos + Vector3.up * 2.2f, m.from, left);
                m.emberAcc += dt * 50;
                while (m.emberAcc >= 1)
                {
                    m.emberAcc -= 1; var side = Vector3.Cross((m.pos - m.from).normalized, Vector3.up) * (Random.value < 0.5f ? -1 : 1);
                    embers.Emit(m.comet.position + Random.insideUnitSphere * 2f, side * Random.Range(3f, 7f) + Vector3.up * 1.5f, 0.3f, 0.7f);
                }
            }
        }

        // ================================================================== E1 Magic Missile
        public void MissileTapCast(PlayerController p, Vector3 from, Vector3 dir)
        {
            EnsureArc(); var at = HandPos(p);
            // a 0.25 m glyph disc at the palm and a short muzzle streak along the shot
            Arc(DashedRing(0.17f, 0.25f, 3, 26, 10), true, arcAdd, at, Facing(dir), 0.12f, (b, u) =>
            {
                b.go.transform.localScale = Vector3.one * Mathf.Lerp(0.8f, 1.3f, Ease(u)); b.go.transform.Rotate(0, Time.deltaTime * 300, 0, Space.Self);
                b.mpb.SetFloat(PDissolve, u * u); b.mpb.SetFloat(PWidth, 0.7f); b.mpb.SetFloat(PIntensity, 1.15f);
            }, MBeam, ArcLilac, ArcLilac * 1.25f, AshMagenta);
            Arc(beamMesh, false, arcAdd, at, AlongFacingCam(at, dir), 0.1f, (b, u) =>
            {
                b.go.transform.localScale = new Vector3(0.05f, 1, 0.3f * (1 - 0.5f * u)); b.mpb.SetFloat(PWidth, 0.4f); b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 1.1f);
            }, MBeam, ArcLilac, ArcLilac * 1.3f, AshMagenta);
            for (int i = 0; i < 4; i++) arcMotes.Emit(at, dir * 1.5f + Random.insideUnitSphere * 0.8f, 0.08f, 0.18f);
        }

        // the glyph ring at the casting hand: three arcs that brighten as each dart is summoned, with a pulse per dart
        public void MissileChargeStart(PlayerController p)
        {
            var s = St(p); KillH(s.glyph); s.glyphFill = 0; s.glyphPulse = -9;
            var b = Arc(DashedRing(0.3f, 0.335f, 3, 16, 24), true, arcAdd, HandPos(p), Quaternion.identity, 9999, (bb, time) =>
            {
                if (!p) return;
                bb.go.transform.SetPositionAndRotation(HandPos(p) + Vector3.up * 0.05f, Facing(AimOf(p)) * Quaternion.Euler(0, 180 - time * 30, 0));
                float pulse = Mathf.Exp(-(Time.time - s.glyphPulse) * 18);
                bb.go.transform.localScale = Vector3.one * (1 + 0.15f * pulse);
                bb.mpb.SetFloat(PWidth, 0.6f);
                bb.mpb.SetFloat(PIntensity, 0.35f + 0.65f * Mathf.Clamp01(s.glyphFill) + 0.5f * pulse);
            }, MBeam, ArcLilac, HotWhite, AshMagenta);
            s.glyph = H(b);
        }

        public void MissileSummon(PlayerController p, int n)
        {
            var s = St(p); s.glyphFill = n / 3f; s.glyphPulse = Time.time;
            int i = n - 1; if (i < 0 || i > 2) return;
            KillH(s.darts[i]);
            var slot = p.transform.position + ArcanistKit.MissileSlot(p.transform, i);
            for (int k = 0; k < 10; k++) { var off = Random.onUnitSphere * 0.6f; arcMotes.Emit(slot + off, -off / 0.12f, 0.07f, 0.12f); }
            int idx = i;
            var b = Arc(dartMesh, false, arcAdd, slot, Quaternion.identity, 9999, (bb, time) =>
            {
                if (!p) return;
                var pos = p.transform.position + ArcanistKit.MissileSlot(p.transform, idx) + Vector3.up * 0.03f * Mathf.Sin(time * 5 + idx * 2.1f);
                var dir = (p.Last.Aim + Vector3.up * 1.2f - pos);
                bb.go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir.sqrMagnitude > 1e-4f ? dir : Vector3.forward) * Quaternion.Euler(0, 0, time * 90));
                bb.go.transform.localScale = Vector3.one * Ease(Mathf.Clamp01(time / 0.12f));
                bb.mpb.SetFloat(PIntensity, 0.85f); bb.mpb.SetFloat(PNoiseAmt, 0.08f);
            }, MDart, ArcLilac, HotWhite, AshMagenta);
            s.darts[i] = H(b);
            ArcLight(slot, ArcLilac, 0.5f, 2.5f, 0.12f);
        }

        public void MissileChargeCancel(PlayerController p)
        {
            var s = St(p); var hand = HandPos(p);
            for (int i = 0; i < 3; i++)
            {
                var d = s.darts[i]; if (d == null || !d.Alive(this)) continue;
                var from = d.b.go.transform.position; var rot = d.b.go.transform.rotation; KillH(d); s.darts[i] = null;
                // the collapse plays where the darts were, so it still reads beside a Blink
                var inward = Vector3.ClampMagnitude(hand - from, 0.4f);
                Arc(dartMesh, false, arcAdd, from, rot, 0.25f, (bb, u) =>
                {
                    bb.go.transform.position = from + inward * Ease(u); bb.go.transform.localScale = Vector3.one * (1 - u); bb.mpb.SetFloat(PDissolve, u * 0.5f); bb.mpb.SetFloat(PIntensity, 1.1f);
                }, MDart, ArcLilac, ArcLilac * 1.3f, AshMagenta);
                for (int k = 0; k < 2; k++) arcMotes.Emit(from, inward / 0.25f + Random.insideUnitSphere * 0.3f, 0.07f, 0.25f);
            }
            if (s.glyph != null && s.glyph.Alive(this))
            {
                var at = s.glyph.b.go.transform.position; var rot = s.glyph.b.go.transform.rotation; KillH(s.glyph);
                for (int k = 0; k < 3; k++)
                {
                    var piece = FxMeshes.Sector(0.3f, 0.335f, 52f, 16, false, "FX_MS_GlyphPiece"); var r = rot * Quaternion.Euler(0, k * 120, 0);
                    var outward = r * Vector3.forward * 0.15f;
                    Arc(piece, true, arcAdd, at, r, 0.25f, (bb, u) =>
                    {
                        bb.go.transform.position = at + outward * u + Vector3.down * 0.3f * u * u; bb.go.transform.rotation = r * Quaternion.Euler(u * 25, 0, u * 15);
                        bb.mpb.SetFloat(PDissolve, u); bb.mpb.SetFloat(PWidth, 0.6f); bb.mpb.SetFloat(PIntensity, 0.8f);
                    }, MBeam, DeepViolet, ArcLilac, AshMagenta);
                }
            }
            s.glyph = null;
        }

        public void MissileVolley(PlayerController p, int n)
        {
            var s = St(p); var at = HandPos(p);
            if (s.glyph != null && s.glyph.Alive(this)) { var gb = s.glyph.b; float t0 = gb.t; gb.life = gb.t + 0.12f; gb.anim = (b, u) => { b.mpb.SetFloat(PDissolve, Mathf.Clamp01((b.t - t0) / 0.12f)); }; }
            s.glyph = null;
            Arc(RingU(0.3f, 0.34f, 64), true, arcAdd, at, Facing(AimOf(p)), 0.18f, (b, u) =>
            {
                b.go.transform.localScale = Vector3.one * (1 + 1.6f * Ease(u)); b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PIntensity, 1.1f);
            }, MBeam, ArcLilac, HotWhite, AshMagenta);
            ArcLight(at, ArcLilac, 1.2f, 4f, 0.15f);
        }
        // a hovering dart hands over to its projectile (the darts launch 0.05 s apart)
        public void MissileLaunch(PlayerController p, int i) { var s = St(p); if (i >= 0 && i < 3) { KillH(s.darts[i]); s.darts[i] = null; } }

        // a missile in flight: the dart drawn between ticks (extrapolated from the last tick), two short weaving ribbons
        public void Missile(ProjectileSystem.Projectile pr, bool charged, int index = 1)
        {
            EnsureArc();
            float tickAt = Time.time; Vector3 lastPos = pr.Pos;
            float phase = Random.value * 6.28f;
            var b = Arc(dartMesh, false, arcAdd, pr.Pos, Quaternion.LookRotation(pr.Vel), 9999, (bb, time) =>
            {
                float since = Mathf.Min(Time.time - tickAt, Sim.Dt * 1.5f);
                var pos = lastPos + pr.Vel * since;
                var side = Vector3.Cross(Vector3.up, pr.Vel.normalized); var weave = side * 0.05f * Mathf.Sin(time * 56.5f + phase);   // about 9 Hz
                bb.go.transform.SetPositionAndRotation(pos + weave, Quaternion.LookRotation(pr.Vel) * Quaternion.Euler(0, 0, time * 360));
                bb.go.transform.localScale = Vector3.one * 1.15f;
                bb.mpb.SetFloat(PIntensity, 1.0f); bb.mpb.SetFloat(PNoiseAmt, 0.08f);
            }, MDart, ArcLilac, HotWhite, AshMagenta);
            var h = H(b);
            float tt = charged ? 0.03f : 0.024f;
            var t1 = TakeTrail(tt * 1.3f, 0.14f, pr.Pos); var t2 = TakeTrail(tt * 1.05f, 0.09f, pr.Pos + Vector3.up * 0.04f);
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
                if (hit) MissileImpact(at + (charged ? side * (index - 1) * 0.2f : Vector3.zero) - q.Vel.normalized * 0.3f, q.Vel); else MissileFizzle(q.Pos, q.Vel);
            };
        }

        void MissileImpact(Vector3 at, Vector3 vel)
        {
            var rot = FaceCam(at); var back = -vel.normalized;
            // a small lilac core (0.2 m, 0.06 s), six crisp lilac rays held 0.18 s, one thin ring, four rune shards
            Arc(billboard, false, arcAdd, at, rot, 0.06f, (b, u) =>
            {
                b.go.transform.localScale = Vector3.one * 0.6f; b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 1.0f);
            }, MDot, ArcLilac, ArcLilac * 1.25f, AshMagenta);
            for (int k = 0; k < 6; k++)
            {
                var r = rot * Quaternion.Euler(0, k * 60 + Random.Range(-10f, 10f), 0);
                Arc(beamMesh, false, arcAdd, at + r * Vector3.forward * 0.06f, r, 0.26f, (b, u) =>
                {
                    float t = u * 0.26f; b.go.transform.localScale = new Vector3(0.05f, 1, 0.42f * Mathf.Lerp(0.4f, 1f, Ease(Mathf.Clamp01(t / 0.05f))));
                    b.mpb.SetFloat(PWidth, 0.4f); b.mpb.SetFloat(PDissolve, Mathf.Clamp01((t - 0.18f) / 0.08f)); b.mpb.SetFloat(PIntensity, 1.15f);
                }, MBeam, ArcLilac, ArcLilac * 1.35f, AshMagenta);
            }
            // a small magic circle bursts open on the hit: a dashed outer ring and a thin inner ring turning against
            // each other, six ticks between them, a rune glyph flashing at the centre, then it all erodes
            float spinDir = Random.value < 0.5f ? 1 : -1;
            Arc(DashedRing(0.52f, 0.58f, 6, 16, 12), true, arcAdd, at, rot, 0.3f, (b, u) =>
            {
                b.go.transform.rotation = rot * Quaternion.Euler(0, spinDir * 220 * u * 0.3f, 0);
                b.go.transform.localScale = Vector3.one * Mathf.Lerp(0.35f, 1.05f, Ease(Mathf.Clamp01(u / 0.6f)));
                b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PDissolve, Mathf.Clamp01((u - 0.45f) / 0.55f)); b.mpb.SetFloat(PIntensity, 1.1f);
            }, MBeam, ArcLilac, ArcLilac * 1.3f, AshMagenta);
            Arc(RingU(0.33f, 0.35f, 48), true, arcAdd, at, rot, 0.26f, (b, u) =>
            {
                b.go.transform.rotation = rot * Quaternion.Euler(0, -spinDir * 300 * u * 0.26f, 0);
                b.go.transform.localScale = Vector3.one * Mathf.Lerp(0.5f, 1f, Ease(Mathf.Clamp01(u / 0.5f)));
                b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PDissolve, Mathf.Clamp01((u - 0.4f) / 0.6f)); b.mpb.SetFloat(PIntensity, 1.0f);
            }, MBeam, ArcLilac, ArcLilac * 1.3f, AshMagenta);
            Arc(TickRing(0.39f, 0.48f, 6, 0.035f), true, arcAdd, at, rot, 0.3f, (b, u) =>
            {
                b.go.transform.rotation = rot * Quaternion.Euler(0, spinDir * 220 * u * 0.3f + 30, 0);
                b.go.transform.localScale = Vector3.one * Mathf.Lerp(0.4f, 1.05f, Ease(Mathf.Clamp01(u / 0.6f)));
                b.mpb.SetFloat(PProgress, 1.1f); b.mpb.SetFloat(PWidth, 0.07f); b.mpb.SetFloat(PDissolve, Mathf.Clamp01((u - 0.45f) / 0.55f)); b.mpb.SetFloat(PIntensity, 1.15f);
            }, MStroke, ArcLilac, HotWhite, AshMagenta);
            var glyph = Glyph(0.26f, 0.035f, out var glyphGlow); Destroy(glyphGlow);
            Arc(glyph, true, arcAdd, at, rot, 0.2f, (b, u) =>
            {
                b.go.transform.localScale = Vector3.one * Mathf.Lerp(0.7f, 1.15f, Ease(u));
                b.mpb.SetFloat(PProgress, 1.1f); b.mpb.SetFloat(PWidth, 0.07f); b.mpb.SetFloat(PDissolve, Mathf.Clamp01((u - 0.35f) / 0.65f)); b.mpb.SetFloat(PIntensity, 1.3f);
            }, MStroke, ArcLilac, HotWhite, AshMagenta);
            for (int k = 0; k < 8; k++)
            {
                float a = k * 45 * Mathf.Deg2Rad; var d = rot * new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                arcMotes.Emit(at + d * 0.5f, d * Random.Range(1.2f, 2f), 0.06f, 0.25f);
            }
            for (int k = 0; k < 4; k++)
            {
                var v = (back + Random.insideUnitSphere * 0.9f + Vector3.up * 0.5f).normalized * Random.Range(2.5f, 4f); var spin = Random.Range(-720f, 720f);
                Arc(dartMesh, false, arcAdd, at, Quaternion.LookRotation(v), 0.3f, (b, u) =>
                {
                    float t = u * 0.3f; b.go.transform.position = at + v * t + Vector3.down * 4.9f * t * t;
                    b.go.transform.rotation = Quaternion.LookRotation(v) * Quaternion.Euler(spin * t, 0, 0); b.go.transform.localScale = Vector3.one * 0.25f;
                    b.mpb.SetFloat(PDissolve, Mathf.Clamp01((u - 0.4f) / 0.6f)); b.mpb.SetFloat(PIntensity, 1f);
                }, MDart, ArcLilac, HotWhite, AshMagenta);
            }
            ArcLight(at, ArcLilac, 1.3f, 3.5f, 0.1f);
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
        static float SheathW(int step) => step == 1 ? 0.22f : step == 2 ? 0.34f : 0.62f;
        static float CoreW(int step) => step == 1 ? 0.05f : step == 2 ? 0.09f : 0.2f;
        public void BeamWindup(PlayerController p, int ticks)
        {
            var s = St(p); foreach (var h in s.lens) KillH(h); s.lens.Clear();
            float life = ticks / (float)Sim.Hz;
            for (int k = 0; k < 3; k++)
            {
                int ring = k;
                var mesh = ring == 0 ? DashedRing(0.375f, 0.40f, 4, 14) : ring == 1 ? DashedRing(0.255f, 0.28f, 4, 18) : DashedRing(0.06f, 0.12f, 3, 40, 6);
                var b = Arc(mesh, true, arcAdd, HandPos(p), Quaternion.identity, 9999, (bb, time) =>
                {
                    if (!p) return;
                    var kk = Arcanist(p); bool beam = kk != null && kk.BeamActive;
                    var aim = beam ? kk.BeamDir : AimOf(p); int step = beam ? kk.BeamStep : 0;
                    var at = HandPos(p) + aim * 0.42f;
                    float grow = Ease(Mathf.Clamp01(time / life));
                    float rate = (ring == 1 ? -540f : ring == 0 ? 360f : 200f) * (1 + 0.5f * step);
                    bb.go.transform.SetPositionAndRotation(at, Facing(aim) * Quaternion.Euler(0, rate * time, 0));
                    float pulse = Mathf.Exp(-(Time.time - s.stepPulse) * 14);
                    bb.go.transform.localScale = Vector3.one * grow * (1 + 0.18f * pulse);
                    bb.mpb.SetFloat(PWidth, 0.6f); bb.mpb.SetFloat(PIntensity, 0.8f + 0.12f * step + 0.6f * pulse);
                }, MBeam, Ultraviolet, UVCore, AshMagenta);
                s.lens.Add(H(b));
            }
            var hand = HandPos(p);
            for (int k = 0; k < 12; k++) { var off = Random.onUnitSphere * 0.7f; arcSparks.Emit(hand + off, -off / life * 0.9f, 0.04f, life * 0.9f); }
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
                Color low = layer == 0 ? Void : layer == 3 ? UVCore : Ultraviolet, high = layer == 0 ? Void : layer == 3 ? new Color(1.6f, 1.25f, 2.3f) : UVCore;
                var b = Arc(beamMesh, false, mat, s.beamFrom, Quaternion.identity, 9999, (bb, time) =>
                {
                    if (!p) return;
                    var dir = (s.beamEnd - s.beamFrom).normalized; var from = HandPos(p) + dir * 0.42f; var d = s.beamEnd - from; float len = Mathf.Max(0.1f, d.magnitude);
                    int step = s.beamStep; float sheath = SheathW(step), core = CoreW(step);
                    float snap = Ease(Mathf.Clamp01(time / 0.035f));
                    float pulse = Mathf.Exp(-(Time.time - s.stepPulse) * 10);   // a surge down the beam at each step
                    float jitter = step == 3 ? Random.Range(0.85f, 1.2f) : step == 2 ? Random.Range(0.93f, 1.08f) : 1;
                    var rot = AlongFacingCam(from, d); var pos = from;
                    if (layer == 1 || layer == 2) pos += rot * Vector3.right * (layer == 1 ? -1 : 1) * sheath * 0.5f * jitter;
                    bb.go.transform.SetPositionAndRotation(pos, rot);
                    float w = layer == 0 ? sheath : layer == 3 ? core * 2.2f : 0.07f + 0.04f * (step - 1);
                    bb.go.transform.localScale = new Vector3(w * jitter * (1 + 0.6f * pulse), 1, len * snap);
                    bb.mpb.SetFloat(PWidth, layer == 0 ? 0.75f : layer == 3 ? 0.45f : 0.6f);
                    bb.mpb.SetFloat(PIntensity, layer == 0 ? 0.88f : layer == 3 ? (1.3f + 0.25f * step) + 0.8f * pulse : (0.5f + 0.2f * step) + 0.4f * pulse);
                    float flow = 2.2f + 1.6f * step;
                    bb.mpb.SetVector(PFlow, new Vector4(-flow, 0.3f * step, -flow * 0.6f, -0.4f * step)); bb.mpb.SetVector(PNoiseScale, new Vector4(Mathf.Max(1.5f, len * (0.35f + 0.25f * step)), 3 + step, 0, 0));
                    bb.mpb.SetFloat(PNoiseAmt, layer == 3 ? 0.2f + 0.1f * step : 0.35f + 0.15f * step);
                }, MBeam, low, high, AshMagenta);
                s.beam.Add(H(b));
            }
            // crackling arcs: jagged lightning along the beam that re-forms every 0.045 s, more and wilder at each step
            for (int k = 0; k < 3; k++)
            {
                int idx = k; var mesh = new Mesh { name = "FX_MS_BeamArc" }; mesh.MarkDynamic(); float next = 0;
                var b = Arc(mesh, true, arcAdd, Vector3.zero, Quaternion.identity, 9999, (bb, time) =>
                {
                    if (!p) return;
                    bb.go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    int step = s.beamStep; bool on = idx < step;
                    if (time >= next) { next = time + 0.045f; var dir = (s.beamEnd - s.beamFrom).normalized; JaggedArc(mesh, HandPos(p) + dir * 0.42f, s.beamEnd, (0.06f + 0.08f * step) * (step == 3 ? 1.6f : 1), 0.025f + 0.012f * step); }
                    bb.mpb.SetFloat(PProgress, 1.1f); bb.mpb.SetFloat(PWidth, 0.07f);
                    bb.mpb.SetFloat(PIntensity, on ? 1.1f + Random.Range(0f, 0.7f) : 0); bb.mpb.SetFloat(PNoiseAmt, 0.2f);
                }, MStroke, UVCore, new Color(1.7f, 1.4f, 2.4f), AshMagenta);
                s.beam.Add(H(b));
            }
            // contact scorch (dark, alpha) that follows the contact point while the beam hits
            var sc = Arc(billboard, false, arcAlpha, s.beamEnd, Quaternion.identity, 9999, (bb, time) =>
            {
                bb.go.transform.SetPositionAndRotation(s.beamEnd, FaceCam(s.beamEnd));
                bb.go.transform.localScale = Vector3.one * (s.beamHit ? 0.55f + 0.25f * s.beamStep : 0.001f);
                bb.mpb.SetFloat(PWidth, 0.95f); bb.mpb.SetFloat(PIntensity, 0.65f);
            }, MShadow, new Color(0.03f, 0.0f, 0.06f), Void, Ultraviolet);
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

        public void Beam(PlayerController p, Vector3 from, Vector3 dir, Vector3 end, int step, bool hit)
        {
            var s = St(p);
            if (step > s.beamStep && s.beam.Count > 0)
            {
                s.stepPulse = Time.time; var lensAt = HandPos(p) + dir * 0.42f;
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
                    Arc(billboard, false, arcAdd, end, FaceCam(end), 0.12f, (b, u) =>
                    {
                        b.go.transform.localScale = Vector3.one * Mathf.Lerp(0.8f, max ? 2.4f : 1.4f, u); b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 1.1f);
                    }, MDot, Ultraviolet, UVCore, AshMagenta);
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
                Arc(beamMesh, false, arcAlpha, from, endRot, 0.15f, (b, u) =>
                {
                    b.go.transform.localScale = new Vector3(Mathf.Lerp(sh0, 0.001f, Ease(u)), 1, len); b.mpb.SetFloat(PWidth, 0.75f); b.mpb.SetFloat(PIntensity, 0.85f * (1 - u));
                }, MBeam, Void, Void, AshMagenta);
                Arc(beamMesh, false, arcAdd, from, endRot, 0.17f, (b, u) =>
                {
                    b.go.transform.localScale = new Vector3(Mathf.Lerp(w0, 0.03f, Ease(Mathf.Clamp01(u / 0.88f))), 1, len); b.mpb.SetFloat(PWidth, 0.45f);
                    b.mpb.SetFloat(PIntensity, u < 0.88f ? 1.3f : 0);
                }, MBeam, UVCore, new Color(1.6f, 1.25f, 2.3f), AshMagenta);
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
                            b.mpb.SetFloat(PIntensity, (sheathLayer ? 0.85f : 1.35f) * stutter(t));
                        }, MBeam, sheathLayer ? Void : UVCore, sheathLayer ? Void : new Color(1.6f, 1.25f, 2.3f), AshMagenta);
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
            for (int i = 0; i < s.lens.Count; i++)
            {
                var h = s.lens[i]; if (!h.Alive(this)) continue;
                var at = h.b.go.transform.position; var rot = h.b.go.transform.rotation; var mesh = h.b.mf.sharedMesh; var sc = h.b.go.transform.localScale;
                h.b.owns = false; KillH(h);
                var axis = rot * Vector3.up; float dirSign = i == 0 ? 1 : i == 1 ? -1 : 0.4f;
                // thrown (broken / cancelled): spin outward 1 -> 1.8x at 720 deg/s and fade; a normal end: shrink back
                // into the astrolabe at the shoulder over 0.2 s
                Arc(mesh, true, arcAdd, at, rot, thrown ? 0.28f : 0.2f, (b, u) =>
                {
                    float t = u * (thrown ? 0.28f : 0.2f);
                    b.go.transform.position = thrown ? at + axis * dirSign * 2.5f * t : Vector3.Lerp(at, p ? AstroPos(p) : at, Ease(u));
                    b.go.transform.localScale = thrown ? sc * (1 + 0.8f * u) : sc * (1 - u);
                    b.go.transform.Rotate(0, Time.deltaTime * 720, 0, Space.Self);
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
        void DrawRune(MarkFx m, Vector3 c, float R, float life, int glyphCount, float line, Color ink, Color hot, Color armedCol)
        {
            m.strokes = new List<(Vector3, Vector3)>(); m.rings = new List<float>(); m.glyphMeshes = new List<Mesh>(); m.glyphPose = new List<(Vector3, Quaternion)>(); m.line = line;
            // armBefore: seconds before the end when this part turns gold (centre first)
            void Part(Mesh core, Mesh glow, Quaternion rot, Vector3 scale, float t0, float t1, float armBefore, float glowScale = 1)
            {
                for (int g = 0; g < 2; g++)
                {
                    bool isGlow = g == 1; var mesh = isGlow ? glow : core; if (mesh == null) continue;
                    var b = Arc(mesh, true, arcAddFloor, c + Vector3.up * (isGlow ? 0.002f : 0.004f), rot, 9999, (bb, time) =>
                    {
                        float u = time / life, prog = Mathf.Clamp01((u - t0) / Mathf.Max(0.001f, t1 - t0));
                        float armed = Mathf.Clamp01((time - (life - armBefore)) / 0.05f);
                        bb.mpb.SetFloat(PProgress, prog * 1.03f); bb.mpb.SetFloat(PWidth, isGlow ? 0.2f : 0.07f);
                        bb.mpb.SetColor(PLow, Color.Lerp(ink, armedCol, armed)); bb.mpb.SetColor(PHigh, Color.Lerp(hot, armedCol, armed));
                        float breathe = 0.9f + 0.1f * Mathf.Sin(time * 9 + t0 * 20);
                        bb.mpb.SetFloat(PIntensity, isGlow ? (0.28f + 0.04f * armed) * glowScale : (1.15f + 0.15f * armed) * breathe);   // armed: gold lines, not a filled glow
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
                Part(RingU(r - w, r, 160), RingU(r - w * 3.5f, r + w * 2.5f, 160), Quaternion.Euler(0, yaw, 0), new Vector3(clockwise ? -1 : 1, 1, 1), t0, t1, armBefore);
            }
            void Line(Vector3 a, Vector3 b, float w, float t0, float t1, float armBefore)
            {
                m.strokes.Add((a, b)); Part(LineQuad(a, b, w), LineQuad(a, b, w * 4f), Quaternion.identity, Vector3.one, t0, t1, armBefore, 0.8f);
            }
            float arm = Mathf.Min(0.2f, life * 0.18f);   // the gold wave runs over the last 18 % (at most 0.2 s)
            Ring(R, line * 1.4f, 0f, 0.97f, arm * 0.25f, true, m.ringYaw);
            Ring(R * 0.9f, line * 0.8f, 0.04f, 0.7f, arm * 0.35f, false, m.ringYaw + 90);
            Ring(R * 0.68f, line, 0.1f, 0.6f, arm * 0.55f, true, m.ringYaw + 45);
            Ring(R * 0.3f, line, 0.4f, 0.75f, arm * 0.95f, false, m.ringYaw + 10);
            // hexagram inscribed in the inner ring
            for (int k = 0; k < 6; k++)
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
                        bb.mpb.SetColor(PLow, Color.Lerp(ink, armedCol, armed)); bb.mpb.SetColor(PHigh, Color.Lerp(hot, armedCol, armed));
                        bb.mpb.SetFloat(PIntensity, isGlow ? 0.26f + 0.03f * armed : 1.2f + 0.15f * armed); bb.mpb.SetFloat(PNoiseAmt, 0.4f);
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
                b.go.transform.SetPositionAndRotation(h, FaceCam(h)); b.go.transform.localScale = Vector3.one * Mathf.Max(0.5f, R * 0.18f);
                b.mpb.SetFloat(PIntensity, u < 0.97f ? 1.2f : 0);
                if (u < 0.97f && Random.value < 0.6f) arcMotes.Emit(h, Vector3.up * 1.4f + Random.insideUnitSphere * 0.25f, 0.1f, 0.45f);
                // once armed, motes rise from all over the circle
                if (time > life - arm && Random.value < 0.9f) { var rc = Random.insideUnitCircle.normalized; var q = c + new Vector3(rc.x, 0, rc.y) * R * Random.Range(0.3f, 1f); arcMotes.Emit(q, Vector3.up * 2.2f, 0.11f, 0.4f); }
            }, MDot, ink, hot, AshMagenta)));
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
        static Mesh Glyph(float g, float w, out Mesh glow)
        {
            var pts = new List<Vector3>(); for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++) pts.Add(new Vector3(x, 0, z) * g * 0.5f);
            int n = Random.Range(3, 5); var core = new CombineInstance[n]; var halo = new CombineInstance[n]; int last = Random.Range(0, 9);
            for (int k = 0; k < n; k++)
            {
                int next; do { next = Random.Range(0, 9); } while (next == last || (pts[next] - pts[last]).magnitude > g * 0.75f);
                core[k] = new CombineInstance { mesh = LineQuad(pts[last], pts[next], w), transform = Matrix4x4.identity };
                halo[k] = new CombineInstance { mesh = LineQuad(pts[last], pts[next], w * 4f), transform = Matrix4x4.identity };
                last = Random.value < 0.7f ? next : Random.Range(0, 9);
            }
            var m = new Mesh { name = "FX_MS_Glyph" }; m.CombineMeshes(core, true, true);
            glow = new Mesh { name = "FX_MS_GlyphGlow" }; glow.CombineMeshes(halo, true, true);
            foreach (var ci in core) Destroy(ci.mesh); foreach (var ci in halo) Destroy(ci.mesh);
            return m;
        }
        // the rune itself detonates: every line flashes white-gold, the rings blast outward at different speeds, light
        // pillars sweep up from the glyphs around the circle, the glyphs are thrown up spinning, and the lines shatter
        void RuneDetonate(MarkFx m, Vector3 c, float R)
        {
            float line = m.line > 0 ? m.line : 0.08f;
            var flashCol = new Color(2.2f, 1.9f, 1.3f);
            if (m.rings != null)
                for (int i = 0; i < m.rings.Count; i++)
                {
                    float r = m.rings[i], grow = 1.25f + 0.25f * (m.rings.Count - i) / m.rings.Count, life = 0.32f + 0.05f * i;
                    // the ring flashes in place, then blasts outward and erodes
                    Arc(RingU(r - line * 1.6f, r, 160), true, arcAddFloor, c + Vector3.up * 0.01f, Quaternion.Euler(0, Random.value * 360, 0), life, (b, u) =>
                    {
                        float sc = Mathf.Lerp(1f, grow, Ease(Mathf.Clamp01((u - 0.15f) / 0.85f))); b.go.transform.localScale = new Vector3(sc, 1, sc);
                        b.mpb.SetFloat(PProgress, 1.1f); b.mpb.SetFloat(PWidth, 0.07f); b.mpb.SetFloat(PDissolve, Mathf.Clamp01((u - 0.25f) / 0.75f));
                        b.mpb.SetFloat(PIntensity, u < 0.15f ? 1.8f : 1.3f); b.mpb.SetFloat(PNoiseAmt, 0.6f);
                    }, MStroke, ArmedGold, flashCol, AshMagenta);
                }
            // light pillars sweeping round the circle from each glyph
            if (m.glyphPose != null)
                for (int i = 0; i < m.glyphPose.Count; i++)
                {
                    var (at, rot) = m.glyphPose[i]; float delay = 0.012f * i, life = 0.34f + delay; var foot = new Vector3(at.x, c.y, at.z);
                    Arc(cyl, false, arcAdd, foot, Quaternion.identity, life, (b, u) =>
                    {
                        float t = Mathf.Clamp01((u * life - delay) / 0.34f);
                        b.go.transform.localScale = new Vector3(0.22f * (1 - 0.5f * t), (t <= 0 ? 0.01f : 3.2f * Ease(Mathf.Clamp01(t / 0.25f))), 0.22f * (1 - 0.5f * t));
                        b.mpb.SetFloat(PDissolve, Mathf.Clamp01((t - 0.3f) / 0.7f)); b.mpb.SetFloat(PIntensity, t <= 0 ? 0 : 1.2f);
                    }, MPillar, CometViolet, flashCol, AshMagenta);
                    // the glyph itself thrown up and out, spinning
                    var g = Glyph(m.glyphSize > 0 ? m.glyphSize : 0.5f, line * 0.8f, out var gg); Destroy(gg);
                    var outDir = Sim.Flat(at - c).normalized; var v = outDir * Random.Range(2.5f, 4.5f) + Vector3.up * Random.Range(5f, 7.5f); var spin = Random.Range(-540f, 540f);
                    Arc(g, true, arcAdd, at, rot, 0.75f, (b, u) =>
                    {
                        float t = u * 0.75f; b.go.transform.position = at + v * t + Vector3.down * 4.9f * t * t;
                        b.go.transform.rotation = rot * Quaternion.Euler(spin * t, spin * 0.6f * t, 0);
                        b.mpb.SetFloat(PProgress, 1.1f); b.mpb.SetFloat(PWidth, 0.07f); b.mpb.SetFloat(PDissolve, Mathf.Clamp01((t - 0.2f) / 0.55f)); b.mpb.SetFloat(PIntensity, 1.4f);
                    }, MStroke, ArmedGold, flashCol, AshMagenta);
                }
            // every straight line shatters: its two halves fly apart and up
            if (m.strokes != null)
                foreach (var (a0, b0) in m.strokes)
                {
                    var mid = (a0 + b0) * 0.5f;
                    for (int h = 0; h < 2; h++)
                    {
                        var A = h == 0 ? a0 : mid; var B = h == 0 ? mid : b0; var centre = c + (A + B) * 0.5f;
                        var half = LineQuad(A - (A + B) * 0.5f, B - (A + B) * 0.5f, line);
                        var v = (Sim.Flat(centre - c).normalized * Random.Range(1.5f, 3.5f) + Vector3.up * Random.Range(3f, 5.5f));
                        var spin = Random.Range(-360f, 360f);
                        Arc(half, true, arcAdd, centre, Quaternion.identity, 0.6f, (b, u) =>
                        {
                            float t = u * 0.6f; b.go.transform.position = centre + v * t + Vector3.down * 4.9f * t * t;
                            b.go.transform.rotation = Quaternion.Euler(spin * t, spin * 0.4f * t, 0);
                            b.mpb.SetFloat(PProgress, 1.1f); b.mpb.SetFloat(PWidth, 0.07f); b.mpb.SetFloat(PDissolve, Mathf.Clamp01((t - 0.12f) / 0.48f)); b.mpb.SetFloat(PIntensity, 1.35f);
                        }, MStroke, ArmedGold, flashCol, AshMagenta);
                    }
                }
        }
        // the burnt rune left on the floor after it fires: dim violet-grey lines that erode away
        void BurntRune(MarkFx m, Vector3 c, float life, float line)
        {
            System.Action<Burst, float> burnt = (b, u) => { b.mpb.SetFloat(PProgress, 1.1f); b.mpb.SetFloat(PWidth, 0.07f); b.mpb.SetFloat(PDissolve, 0.05f + u * 0.9f); b.mpb.SetFloat(PIntensity, 0.42f); };
            System.Action<Burst, float> ember = (b, u) => { b.mpb.SetFloat(PProgress, 1.1f); b.mpb.SetFloat(PWidth, 0.07f); b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 0.35f); };
            if (m.rings != null) foreach (var r in m.rings)
                {
                    Arc(RingU(r - line * 1.2f, r, 128), true, arcAlphaFloor, c, Quaternion.identity, life, burnt, MStroke, BurntGrey, BurntGrey, RuneViolet);
                    Arc(RingU(r - line * 0.7f, r - line * 0.3f, 128), true, arcAddFloor, c, Quaternion.identity, life, ember, MStroke, RuneViolet, RuneViolet, AshMagenta);
                }
            if (m.strokes != null) foreach (var (a, bb) in m.strokes)
                {
                    Arc(LineQuad(a, bb, line * 1.2f), true, arcAlphaFloor, c, Quaternion.identity, life, burnt, MStroke, BurntGrey, BurntGrey, RuneViolet);
                    Arc(LineQuad(a, bb, line * 0.5f), true, arcAddFloor, c, Quaternion.identity, life, ember, MStroke, RuneViolet, RuneViolet, AshMagenta);
                }
        }

        // ================================================================== E3 Rune Sigil
        public void SigilPlace(PlayerController p, int id, Vector3 pos, int armTicks)
        {
            EnsureArc();
            float life = armTicks / (float)Sim.Hz, hold = life + 0.12f; float R = ArcanistKit.SigilRadius;
            var m = new MarkFx { pos = new Vector3(pos.x, 0.04f, pos.z), start = Time.time, life = life, ringYaw = Random.value * 360 };
            marks[id] = m;
            DrawRune(m, m.pos, R, life, 10, 0.07f, RuneViolet, HotWhite, ArmedGold);
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
            // the column: a see-through tapered shell, bright at its rim, eaten from the top down; a hot core, a flash
            Arc(colShell, false, arcAdd, c, Quaternion.identity, 0.38f, (b, u) =>
            {
                float t = u * 0.38f; float rise = Ease(Mathf.Clamp01(t / 0.05f));
                b.go.transform.localScale = new Vector3(1.15f, rise * 1.5f, 1.15f);
                b.mpb.SetFloat(PFresnel, 1.4f); b.mpb.SetFloat(PClipY, c.y + 4.8f * (1 - Mathf.Clamp01((t - 0.08f) / 0.3f)));
                b.mpb.SetFloat(PIntensity, 0.95f); b.mpb.SetVector(PFlow, new Vector4(0, -3.5f, 0.4f, -2.2f)); b.mpb.SetFloat(PNoiseAmt, 0.6f);
                b.mpb.SetVector(PNoiseScale, new Vector4(10, 3, 0, 0));
            }, MShell, RuneViolet, RuneViolet * 1.4f, AshMagenta);
            Arc(cyl, false, arcAdd, c, Quaternion.identity, 0.14f, (b, u) =>
            {
                b.go.transform.localScale = new Vector3(0.45f, 4f, 0.45f); b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 1.3f);
            }, MPillar, RuneViolet, HotWhite, AshMagenta);
            Arc(billboard, false, arcAdd, c + Vector3.up * 1f, FaceCam(c), 0.1f, (b, u) =>
            {
                b.go.transform.localScale = Vector3.one * Mathf.Lerp(3f, 3.6f, u); b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 1.3f);
            }, MDot, RuneViolet, HotWhite, AshMagenta);
            // two rings racing out to the rune's edge and past it
            for (int k = 0; k < 2; k++)
            {
                float delay = k * 0.06f, far = R + 0.6f * k;
                Arc(RingU(0.97f, 1f, 96), true, arcAddFloor, c, Quaternion.identity, 0.3f + delay, (b, u) =>
                {
                    float t = Mathf.Clamp01((u * (0.3f + delay) - delay) / 0.3f); float r = Mathf.Lerp(0.4f, far, Ease(t)); b.go.transform.localScale = new Vector3(r, 1, r);
                    b.mpb.SetFloat(PProgress, 1.1f); b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PIntensity, t <= 0 ? 0 : 1.8f * (1 - 0.7f * t));
                }, MStroke, RuneViolet, RuneViolet * 1.4f, AshMagenta);
            }
            for (int k = 0; k < 12; k++)
            {
                float a = (k * 30 + Random.Range(-12f, 12f)) * Mathf.Deg2Rad; var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                float el = Random.Range(45f, 65f) * Mathf.Deg2Rad, sp = Random.Range(5f, 7f);
                var v = dir * Mathf.Cos(el) * sp + Vector3.up * Mathf.Sin(el) * sp; var spin = Random.Range(-540f, 540f); var start = c + dir * 1.0f + Vector3.up * 0.3f;
                Arc(LineQuad(new Vector3(0, 0, -0.22f), new Vector3(0, 0, 0.22f), 0.11f), true, arcAdd, start, Quaternion.LookRotation(dir), 0.7f, (b, u) =>
                {
                    float t = u * 0.7f; b.go.transform.position = start + v * t + Vector3.down * 4.9f * t * t;
                    b.go.transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(spin * t, 0, spin * 0.5f * t);
                    b.mpb.SetFloat(PProgress, 1.1f); b.mpb.SetFloat(PWidth, 0.07f); b.mpb.SetFloat(PDissolve, Mathf.Clamp01((t - 0.15f) / 0.55f)); b.mpb.SetFloat(PIntensity, 1.35f);
                }, MStroke, RuneViolet, ArmedGold, AshMagenta);
            }
            for (int k = 0; k < 24; k++) arcSparks.Emit(c + Vector3.up * 0.5f, (Random.insideUnitSphere + Vector3.up * 0.8f) * 7f, 0.07f, 0.35f);
            ArcLight(c + Vector3.up, RuneViolet, hit ? 2.6f : 2f, 7f, 0.22f);
            CameraShake.Shake(0.12f, 0.15f);
            BurntRune(m, c, 0.9f, 0.07f);
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

        // ================================================================== E4 Astral Orb
        public void OrbCharge(PlayerController p, int ticks)
        {
            var s = St(p); foreach (var h in s.orbCharge) KillH(h); s.orbCharge.Clear();
            float life = ticks / (float)Sim.Hz;
            for (int k = 0; k < 2; k++)
            {
                int ring = k;
                var b = Arc(DashedRing(0.2f, 0.218f, 4, 12, 12), true, arcAdd, AstroPos(p), Quaternion.identity, 9999, (bb, time) =>
                {
                    if (!p) return; float u = Mathf.Clamp01(time / life);
                    bb.go.transform.SetPositionAndRotation(AstroPos(p), Quaternion.Euler(ring == 0 ? 70 : -40, time * (200 + 900 * u) * (ring == 0 ? 1 : -1), ring == 0 ? 15 : 60));
                    bb.go.transform.localScale = Vector3.one * (1 + 0.5f * Ease(u)); bb.mpb.SetFloat(PWidth, 0.6f); bb.mpb.SetFloat(PIntensity, 0.6f + 0.3f * u);
                }, MBeam, StarEdge, StarSilver, AshMagenta);
                s.orbCharge.Add(H(b));
            }
            // the progress ring tightens onto the growing seed (the timer); brighter than the spread rings
            var pr = Arc(RingU(0.94f, 1f, 64), true, arcAdd, AstroPos(p), Quaternion.identity, 9999, (bb, time) =>
            {
                if (!p) return; float u = Mathf.Clamp01(time / life);
                bb.go.transform.SetPositionAndRotation(AstroPos(p), FaceCam(AstroPos(p)));
                bb.go.transform.localScale = Vector3.one * Mathf.Lerp(0.5f, 0.18f, Mathf.Clamp01(u * 1.08f)); bb.mpb.SetFloat(PWidth, 0.6f); bb.mpb.SetFloat(PIntensity, 1.2f);
            }, MBeam, StarEdge, StarSilver, AshMagenta);
            s.orbCharge.Add(H(pr));
            // the seed: the orb itself growing to 40 % at the astrolabe, fed by the motes
            var seed = Arc(billboard, false, arcAdd, AstroPos(p), Quaternion.identity, 9999, (bb, time) =>
            {
                if (!p) return; float u = Mathf.Clamp01(time / life); var q = AstroPos(p);
                bb.go.transform.SetPositionAndRotation(q, FaceCam(q)); bb.go.transform.localScale = Vector3.one * 0.25f * 0.4f * Ease(u) * 2.2f; bb.mpb.SetFloat(PIntensity, 1.1f);
            }, MDot, StarSilver, HotWhite, AshMagenta);
            s.orbCharge.Add(H(seed));
            var a = AstroPos(p);
            for (int k = 0; k < 16; k++) { var off = Random.onUnitSphere * 0.8f; arcMotes.Emit(a + off, -off / life * 1.1f, 0.06f, life * 0.85f); }
        }
        public void OrbCancel(PlayerController p)
        {
            var s = St(p); var a = AstroPos(p);
            for (int i = 0; i < s.orbCharge.Count; i++)
            {
                var h = s.orbCharge[i]; if (!h.Alive(this)) continue;
                var sc = h.b.go.transform.localScale; var rot = h.b.go.transform.rotation; var mesh = h.b.mf.sharedMesh; bool own = h.b.owns; h.b.owns = false;
                bool progress = i == 2, seedPart = i == 3; KillH(h);
                if (seedPart) { if (own) Destroy(mesh); continue; }
                // the progress ring snaps open to at most 0.8 m and fades; the spread rings settle back to rest
                Arc(mesh, own, arcAdd, a, rot, progress ? 0.15f : 0.2f, (b, u) =>
                {
                    b.go.transform.localScale = progress ? Vector3.one * Mathf.Lerp(sc.x, 0.8f, Ease(u)) : Vector3.Lerp(sc, Vector3.one, u);
                    b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PIntensity, 0.55f);
                }, MBeam, StarEdge, StarSilver, AshMagenta);
            }
            s.orbCharge.Clear();
            for (int k = 0; k < 16; k++) arcMotes.Emit(a, Random.onUnitSphere * 2f, 0.05f, 0.2f);
        }

        public void OrbRelease(PlayerController p)
        {
            var s = St(p); foreach (var h in s.orbCharge) KillH(h); s.orbCharge.Clear(); foreach (var h in s.orb) KillH(h); s.orb.Clear();
            var a = AstroPos(p); s.orbStart = Time.time; s.orbEnding = -1; s.orbLift = a - p.transform.position;
            Arc(RingU(0.46f, 0.5f, 48), true, arcAdd, a, FaceCam(a), 0.15f, (b, u) =>
            {
                b.go.transform.localScale = Vector3.one * Mathf.Lerp(0.3f, 1f, Ease(u)); b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PIntensity, 1.0f);
            }, MBeam, StarEdge, StarSilver, AshMagenta);
            for (int k = 0; k < 8; k++) arcSparks.Emit(a, Random.onUnitSphere * 2.2f + Vector3.up * 0.5f, 0.04f, 0.15f);
            ArcLight(a, StarSilver, 1.0f, 3.5f, 0.18f);
            // the orb: a small bright core (0.14 m), a rim-lit violet shell (0.28 m), two crossing dashed rings (0.22 m)
            for (int k = 0; k < 4; k++)
            {
                int part = k;
                Mesh mesh = part == 0 ? billboard : part == 1 ? sphereMesh : RingU(part == 2 ? 0.285f : 0.245f, part == 2 ? 0.3f : 0.26f, 64);
                var b = Arc(mesh, part >= 2, arcAdd, a, Quaternion.identity, 9999, (bb, time) =>
                {
                    if (!p) return;
                    var kit = Arcanist(p); float left = kit != null ? kit.OrbLeft01 * 8f : 0;
                    float lift = Ease(Mathf.Clamp01(time / 0.15f));
                    var spot = p.transform.position + Vector3.Lerp(s.orbLift, ArcanistKit.OrbOffset(p.transform), lift) + Vector3.up * 0.04f * Mathf.Sin(time * 2.2f);
                    // contract to 85 % over the 0.12 s before each shot (every 1 s from 0.5 s), then pulse to 110 % for 0.06 s
                    float ph = Mathf.Repeat(time - 0.5f, 1f); float pulse = 1;
                    if (time > 0.38f && ph > 0.88f) pulse = 1 - 0.15f * Ease((ph - 0.88f) / 0.12f);
                    else if (time > 0.5f && ph < 0.06f) pulse = 0.85f + 0.25f * Mathf.Sin(ph / 0.06f * Mathf.PI * 0.5f);
                    else if (time > 0.5f && ph < 0.12f) pulse = 1.1f - 0.1f * ((ph - 0.06f) / 0.06f);
                    // last 1.5 s: blink down to 30 % for 0.05 s, the blinks closing from every 0.4 s to every 0.1 s; rings slow
                    float flick = 1;
                    if (left < 1.5f && left > 0) { float period = Mathf.Lerp(0.1f, 0.4f, left / 1.5f); if (Mathf.Repeat(time, period) < 0.05f) flick = 0.3f; }
                    // ending: drift back into the astrolabe and shrink to nothing over 0.3 s (rings fade in the first 0.2 s)
                    float end = s.orbEnding < 0 ? 0 : Mathf.Clamp01((Time.time - s.orbEnding) / 0.3f);
                    if (end > 0) spot = Vector3.Lerp(spot, AstroPos(p), Ease(end));
                    float grow = Mathf.Lerp(0.4f, 1f, lift) * (1 - end * end);
                    float size = part == 0 ? 0.22f : part == 1 ? 0.28f : 1f;   // the core billboard's soft dot covers about half its quad
                    float spinRate = left < 1.5f ? 32 : 108;   // 0.3 rev/s, slowing to 30 % in the last 1.5 s
                    var rot = part == 0 ? FaceCam(spot) : part == 1 ? Quaternion.identity : Quaternion.Euler(part == 2 ? 70 : 35, time * spinRate * (part == 2 ? 1 : -1), part == 2 ? 10 : -25);
                    bb.go.transform.SetPositionAndRotation(spot, rot); bb.go.transform.localScale = Vector3.one * size * pulse * grow;
                    float ringFade = part >= 2 ? 1 - Mathf.Clamp01(end / 0.67f) : 1;
                    bb.mpb.SetFloat(PIntensity, (part == 0 ? 0.8f : part == 1 ? 0.75f : 1.2f) * flick * ringFade);
                    if (part >= 2) bb.mpb.SetFloat(PWidth, 0.6f);
                    if (part == 1) { bb.mpb.SetFloat(PFresnel, 2.4f); bb.mpb.SetFloat(PNoiseAmt, 0.35f); }
                }, part == 0 ? MDot : part == 1 ? MShell : MBeam, part == 0 ? StarSilver * 0.8f : part == 1 ? StarEdge * 1.3f : StarEdge, part == 0 ? new Color(1.6f, 1.6f, 2.0f) : StarSilver, AshMagenta);
                s.orb.Add(H(b));
            }
        }

        public void OrbShot(PlayerController p, ProjectileSystem.Projectile pr)
        {
            EnsureArc();
            float tickAt = Time.time; Vector3 lastPos = pr.Pos;
            var b = Arc(billboard, false, arcAdd, pr.Pos, Quaternion.identity, 9999, (bb, time) =>
            {
                var pos = lastPos + pr.Vel * Mathf.Min(Time.time - tickAt, Sim.Dt * 1.5f);
                bb.go.transform.SetPositionAndRotation(pos, FaceCam(pos)); bb.go.transform.localScale = Vector3.one * 0.17f; bb.mpb.SetFloat(PIntensity, 1.2f);
            }, MDot, StarSilver, new Color(1.6f, 1.6f, 2.0f), AshMagenta);
            var h = H(b); var tr = TakeTrail(0.014f, 0.05f, pr.Pos, silverTrailMat);
            pr.OnMove = q => { tickAt = Time.time; lastPos = q.Pos; if (tr) tr.transform.position = q.Pos; };
            pr.OnEnd = (q, hit, at) =>
            {
                KillH(h); StartCoroutine(ReleaseTrail(tr, 0.014f));
                if (!hit) return;
                // a crisp 4-point star, 0.3 m, 0.12 s: silver core, violet tips; two tiny sparks
                var rot = FaceCam(at);
                for (int k = 0; k < 4; k++)
                {
                    var r = rot * Quaternion.Euler(0, k * 90 + 45, 0);
                    Arc(beamMesh, false, arcAdd, at, r, 0.12f, (bb, u) =>
                    {
                        bb.go.transform.localScale = new Vector3(0.04f, 1, 0.15f * Mathf.Lerp(0.6f, 1, Ease(u))); bb.mpb.SetFloat(PWidth, 0.4f); bb.mpb.SetFloat(PDissolve, u); bb.mpb.SetFloat(PIntensity, 1.6f);
                    }, MBeam, StarEdge, StarSilver, AshMagenta);
                }
                for (int k = 0; k < 2; k++) arcSparks.Emit(at, Random.onUnitSphere * 2f, 0.03f, 0.12f);
            };
        }

        public void OrbEnd(PlayerController p)
        {
            var s = St(p); if (s.orb.Count == 0) return;
            s.orbEnding = Time.time;
            var handles = new List<Handle>(s.orb); s.orb.Clear();
            foreach (var h in handles) StartCoroutine(KillAfter(h, 0.3f));
            StartCoroutine(OrbGlint(p, 0.27f));
        }
        System.Collections.IEnumerator OrbGlint(PlayerController p, float after)
        {
            yield return new WaitForSeconds(after); if (!p) yield break;
            var a = AstroPos(p);
            Arc(billboard, false, arcAdd, a, FaceCam(a), 0.1f, (b, u) =>
            {
                if (!p) return; var q = AstroPos(p); b.go.transform.SetPositionAndRotation(q, FaceCam(q));
                b.go.transform.localScale = Vector3.one * Mathf.Lerp(0.3f, 0.12f, u); b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 1.0f);
            }, MDot, StarEdge, StarSilver, AshMagenta);
        }

        // ================================================================== E5 Arcane Comet
        // The mark is a large rune drawn over the whole fall (its outer ring is the timer); the comet itself starts far
        // up out of view and slams down in the last part of the fall, landing as the rune completes.
        public void CometMark(PlayerController p, int id, Vector3 pos, int fallTicks)
        {
            EnsureArc();
            float life = fallTicks / (float)Sim.Hz; float R = ArcanistKit.CometRadius, C = ArcanistKit.CometCore;
            var c = new Vector3(pos.x, 0.045f, pos.z);
            // it comes in diagonally from the side of the screen away from the mark (and a little toward the camera), high
            // up: off screen at first, then a meteor streaking across the view and crashing into the mark
            var m = new MarkFx { pos = c, start = Time.time, life = life, from = CometStart(c), ringYaw = Random.value * 360 }; marks[id] = m;
            // ground shadow: wide and faint, tightening and darkening as the comet nears
            m.parts.Add(H(Arc(billboard, false, arcAlphaFloor, c + Vector3.up * 0.003f, Quaternion.identity, life, (b, u) =>
            {
                float e = u * u * u; float r = Mathf.Lerp(R * 1.2f, C * 1.2f, e);
                b.go.transform.localScale = new Vector3(r * 2, 1, r * 2);
                b.mpb.SetFloat(PWidth, Mathf.Lerp(0.85f, 0.4f, e)); b.mpb.SetFloat(PIntensity, Mathf.Lerp(0.1f, 0.85f, e));
            }, MShadow, new Color(0.02f, 0.0f, 0.05f), Void, CometViolet)));
            DrawRune(m, c, R, life, 14, 0.09f, CometViolet, HotWhite, ArmedGold);
            // the comet: a big blazing violet meteor (a flowing shell inside a ragged flame shell, a white-hot core, a wide
            // halo) with a long, wide tail and a stream of embers (moved in LateUpdate)
            var cometGo = new GameObject("Arcane comet").transform; cometGo.SetParent(transform, false); cometGo.position = m.from; m.comet = cometGo;
            var fall = (c - m.from).normalized;
            m.parts.Add(H(Arc(sphereMesh, false, arcAdd, m.from, Quaternion.identity, life, (b, u) =>
            {
                if (!cometGo) return; b.go.transform.position = cometGo.position; b.go.transform.localScale = Vector3.one * 4.4f;
                b.mpb.SetFloat(PFresnel, 1.1f); b.mpb.SetFloat(PIntensity, 1.1f); b.mpb.SetVector(PFlow, new Vector4(0, 4, 1.5f, 3)); b.mpb.SetFloat(PNoiseAmt, 0.65f);
            }, MShell, CometViolet, HotWhite, AshMagenta)));
            m.parts.Add(H(Arc(sphereMesh, false, arcAdd, m.from, Quaternion.identity, life, (b, u) =>
            {
                if (!cometGo) return; b.go.transform.position = cometGo.position; b.go.transform.rotation = Quaternion.LookRotation(fall) * Quaternion.Euler(0, 0, b.t * 200);
                b.go.transform.localScale = new Vector3(5.6f, 5.6f, 7.6f);
                b.mpb.SetFloat(PFresnel, 1.2f); b.mpb.SetFloat(PIntensity, 0.6f); b.mpb.SetFloat(PDissolve, 0.3f + 0.1f * Mathf.Sin(b.t * 23));
                b.mpb.SetVector(PFlow, new Vector4(0, 7, 2, 5)); b.mpb.SetFloat(PNoiseAmt, 0.85f); b.mpb.SetVector(PNoiseScale, new Vector4(6, 6, 0, 0));
            }, MShell, DeepViolet, CometViolet, AshMagenta)));
            m.parts.Add(H(Arc(billboard, false, arcAdd, m.from, Quaternion.identity, life, (b, u) =>
            {
                if (!cometGo) return; b.go.transform.SetPositionAndRotation(cometGo.position, FaceCam(cometGo.position)); b.go.transform.localScale = Vector3.one * 3.4f; b.mpb.SetFloat(PIntensity, 1.4f);
            }, MDot, HotWhite, new Color(2.2f, 2.0f, 2.4f), AshMagenta)));
            m.parts.Add(H(Arc(billboard, false, arcAdd, m.from, Quaternion.identity, life, (b, u) =>
            {
                if (!cometGo) return; b.go.transform.SetPositionAndRotation(cometGo.position, FaceCam(cometGo.position)); b.go.transform.localScale = Vector3.one * 6.5f; b.mpb.SetFloat(PIntensity, 0.3f);
            }, MDot, DeepViolet, CometViolet, AshMagenta)));
            m.parts.Add(H(Arc(taperMesh, false, arcAdd, m.from, Quaternion.identity, life, (b, u) =>
            {
                if (!cometGo) return; var head = cometGo.position; var tail = head - fall * 26f;
                b.go.transform.SetPositionAndRotation(tail, AlongFacingCam(tail, fall)); b.go.transform.localScale = new Vector3(5.5f, 1, 26f);
                b.mpb.SetFloat(PProgress, 1f); b.mpb.SetFloat(PTail, 0.5f); b.mpb.SetFloat(PWidth, 0.06f); b.mpb.SetFloat(PIntensity, 1.15f);
                b.mpb.SetVector(PFlow, new Vector4(-5f, 0, -2.5f, 0.8f)); b.mpb.SetFloat(PNoiseAmt, 0.7f); b.mpb.SetVector(PNoiseScale, new Vector4(10, 2.5f, 0, 0));
            }, MBand, CometViolet, HotWhite, AshMagenta)));
            Arc(RingU(0.15f, 0.2f, 24), true, arcAdd, HandPos(p) + Vector3.up * 0.3f, Facing(Vector3.up), 0.25f, (b, u) =>
            {
                b.go.transform.localScale = Vector3.one * (1 + 2 * u); b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 1.1f);
            }, MBeam, CometViolet, HotWhite, AshMagenta);
        }

        // where the comet starts, chosen in the gameplay camera's view: through a point just past the screen edge on the far
        // side from the mark (a little above it), 16 m above the floor, then pushed 2.5x further out, so it is off screen
        // at first and streaks in from the side of the view for about the last second, wherever the mark is on screen
        static Vector3 CometStart(Vector3 mark)
        {
            var cam = Cam; if (!cam) return mark + Vector3.up * 40f + Vector3.right * 30f;
            var sp = cam.WorldToViewportPoint(mark);
            var ray = cam.ViewportPointToRay(new Vector3(sp.x > 0.5f ? -0.05f : 1.05f, Mathf.Min(0.85f, sp.y + 0.2f), 0));
            float t = ray.direction.y < -0.05f ? (16f - ray.origin.y) / ray.direction.y : 30f;
            var edge = ray.origin + ray.direction * Mathf.Max(5f, t);
            return mark + (edge - mark) * 3.2f;
        }

        public void CometImpact(int id, Vector3 pos, bool hit, bool bull)
        {
            if (!marks.TryGetValue(id, out var m)) return;
            marks.Remove(id); foreach (var h in m.parts) KillH(h);
            if (m.comet) Destroy(m.comet.gameObject);
            var c = new Vector3(pos.x, 0.05f, pos.z); float R = ArcanistKit.CometRadius;
            // contact: the meteor itself squashes flat into the floor for an instant
            Arc(sphereMesh, false, arcAdd, c + Vector3.up * 1.2f, Quaternion.identity, 0.12f, (b, u) =>
            {
                b.go.transform.localScale = new Vector3(Mathf.Lerp(4.4f, 9f, Ease(u)), Mathf.Lerp(3.2f, 0.6f, Ease(u)), Mathf.Lerp(4.4f, 9f, Ease(u)));
                b.go.transform.position = c + Vector3.up * Mathf.Lerp(1.6f, 0.3f, u);
                b.mpb.SetFloat(PFresnel, 1.1f); b.mpb.SetFloat(PDissolve, Mathf.Clamp01((u - 0.4f) / 0.6f)); b.mpb.SetFloat(PIntensity, 1.3f); b.mpb.SetFloat(PNoiseAmt, 0.6f);
            }, MShell, CometViolet, HotWhite, AshMagenta);
            // the slam: a huge flash, a tall column, two shockwaves racing past the rune, a dust ring, a hail of crystal
            // shards, a deep crater with long glowing cracks, a strong light and a hard camera shake
            Arc(billboard, false, arcAdd, c + Vector3.up * 1.2f, FaceCam(c), 0.14f, (b, u) =>
            {
                b.go.transform.localScale = Vector3.one * Mathf.Lerp(8f, 9.5f, u); b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 1.6f);
            }, MDot, CometViolet, HotWhite, AshMagenta);
            Arc(beamMesh, false, arcAdd, c, Quaternion.identity, 0.28f, (b, u) =>
            {
                var cam2 = Cam; var toCam = cam2 ? Sim.Flat(cam2.transform.position - c).normalized : Vector3.back;
                b.go.transform.SetPositionAndRotation(c, Quaternion.LookRotation(Vector3.up, toCam)); b.go.transform.localScale = new Vector3(1.6f * (1 - 0.5f * u), 1, 10f * Mathf.Lerp(0.6f, 1f, Ease(u)));
                b.mpb.SetFloat(PWidth, 0.5f); b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 1.4f); b.mpb.SetVector(PFlow, new Vector4(-5, 0, -2.5f, 0.5f));
            }, MBeam, CometViolet, HotWhite, AshMagenta);
            Arc(colShell, false, arcAdd, c, Quaternion.identity, 0.35f, (b, u) =>
            {
                float t = u * 0.35f; b.go.transform.localScale = new Vector3(1.6f + 1.2f * Ease(u), Ease(Mathf.Clamp01(t / 0.05f)) * 1.3f, 1.6f + 1.2f * Ease(u));
                b.mpb.SetFloat(PFresnel, 1.3f); b.mpb.SetFloat(PDissolve, Mathf.Clamp01((t - 0.06f) / 0.29f)); b.mpb.SetFloat(PIntensity, 1.0f); b.mpb.SetFloat(PNoiseAmt, 0.6f);
            }, MShell, CometViolet, HotWhite, AshMagenta);
            for (int k = 0; k < 2; k++)
            {
                float delay = k * 0.08f, far = R + 2f + k * 1.5f, life = 0.42f + delay;
                Arc(RingU(0.94f, 1f, 160), true, arcAddFloor, c, Quaternion.identity, life, (b, u) =>
                {
                    float t = Mathf.Clamp01((u * life - delay) / 0.42f); float r = Mathf.Lerp(0.8f, far, Ease(t)); b.go.transform.localScale = new Vector3(r, 1, r);
                    b.mpb.SetFloat(PProgress, 1.1f); b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PDissolve, Mathf.Clamp01((t - 0.55f) / 0.45f)); b.mpb.SetFloat(PIntensity, t <= 0 ? 0 : 1.7f);
                }, MStroke, CometViolet, HotWhite, AshMagenta);
            }
            // dust ring rolling outward along the floor
            for (int k = 0; k < 48; k++)
            {
                float a = k / 48f * Mathf.PI * 2; var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                arcMotes.Emit(c + d * 1.2f + Vector3.up * 0.25f, d * Random.Range(7f, 10f) + Vector3.up * Random.Range(0.3f, 1.2f), 0.3f, Random.Range(0.4f, 0.6f));
            }
            for (int k = 0; k < 22; k++)
            {
                var dir = Quaternion.Euler(0, k * (360f / 22) + Random.Range(-8f, 8f), 0) * Vector3.forward;
                var v = dir * Random.Range(4.5f, 8f) + Vector3.up * Random.Range(5f, 9f); var start = c + dir * 0.6f + Vector3.up * 0.4f; var spin = Random.Range(-720f, 720f);
                float size = Random.Range(0.5f, 0.95f); float land = (v.y + Mathf.Sqrt(v.y * v.y + 2 * 9.8f * 0.4f)) / 9.8f;
                Arc(shardMesh, false, arcAdd, start, Quaternion.LookRotation(dir), land + 0.4f, (b, u) =>
                {
                    float t = Mathf.Min(u * (land + 0.4f), land); var at = start + v * t + Vector3.down * 4.9f * t * t; if (at.y < 0.06f) at.y = 0.06f;
                    b.go.transform.position = at; if (t < land) b.go.transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(spin * t, spin * 0.3f * t, 0);
                    b.go.transform.localScale = new Vector3(size * 2.4f, size * 2.4f, size);
                    b.mpb.SetFloat(PDissolve, Mathf.Clamp01((u * (land + 0.4f) - land) / 0.4f)); b.mpb.SetFloat(PIntensity, 1.2f);
                }, MDart, CometViolet, HotWhite, AshMagenta);
            }
            // the crater: a dark scorch (3 m radius) under eight long cracks that hold bright, then erode over 2 s
            Arc(billboard, false, arcAlphaFloor, c, Quaternion.identity, 2.4f, (b, u) =>
            {
                b.go.transform.localScale = new Vector3(6f, 1, 6f); b.mpb.SetFloat(PWidth, 0.55f); b.mpb.SetFloat(PIntensity, 0.9f * (1 - u * u));
            }, MShadow, new Color(0.03f, 0.01f, 0.05f), Void, CometViolet);
            for (int k = 0; k < 8; k++)
            {
                float a = (k * 45 + Random.Range(-14f, 14f)) * Mathf.Deg2Rad; var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                var seam = Crack(dir, 0.5f, Random.Range(2.8f, 4.2f), 4, 0.12f);
                Arc(seam, true, arcAddFloor, c, Quaternion.identity, 2.4f, (b, u) =>
                {
                    float t = u * 2.4f;
                    b.mpb.SetFloat(PProgress, Mathf.Lerp(0.15f, 1.1f, Ease(Mathf.Clamp01(t / 0.12f)))); b.mpb.SetFloat(PWidth, 0.08f);
                    b.mpb.SetFloat(PDissolve, Mathf.Clamp01((t - 0.4f) / 2f)); b.mpb.SetFloat(PIntensity, 1.3f);
                }, MStroke, CometViolet, HotWhite, AshMagenta);
            }
            for (int k = 0; k < 12; k++) embers.Emit(c + Random.insideUnitSphere * 2f + Vector3.up * 0.2f, Vector3.up * Random.Range(0.8f, 1.6f), 0.1f, Random.Range(0.9f, 1.6f));
            for (int k = 0; k < 40; k++) arcSparks.Emit(c + Vector3.up * 0.6f, (Random.insideUnitSphere + Vector3.up) * 10f, 0.1f, 0.5f);
            ArcLight(c + Vector3.up * 1.5f, CometViolet, bull ? 5f : 4f, 14f, 0.35f);
            CameraShake.Shake(bull ? 0.8f : 0.55f, bull ? 0.45f : 0.38f);
            RuneDetonate(m, c, R);
            BurntRune(m, c, 1.4f, 0.09f);
        }
        public void CometFade(int id) => FadeMark(id);

        // ================================================================== E6 Blink
        public void BlinkOut(PlayerController p)
        {
            var s = St(p); var at = p.transform.position; s.blinkFrom = at;
            Arc(DashedRing(0.5f, 0.54f, 6, 14, 10), true, arcAddFloor, new Vector3(at.x, 0.05f, at.z), Quaternion.Euler(0, Random.value * 60, 0), 0.15f, (b, u) =>
            {
                b.go.transform.localScale = Vector3.one * Mathf.Lerp(0.5f, 1.15f, Ease(u)); b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 1.1f);
            }, MBeam, ArcLilac, HotWhite, AshMagenta);
            // the departure silhouette: a snapshot of his current pose as a hollow rim-lit lilac shell, gone in 0.15 s
            BakeShell(s, 0.15f, 1.0f, true);
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
                    b.go.transform.localScale = new Vector3(1.2f, 1.2f, 0.35f); b.mpb.SetFloat(PDissolve, Mathf.Clamp01((u - 0.5f) / 0.5f)); b.mpb.SetFloat(PIntensity, 1.1f);
                }, MDart, ArcLilac, ArcLilac * 1.3f, AshMagenta);
            }
            KillH(s.thread);
            // the travel thread: thin, tapered (widest where he left), faint, with motes running along it toward him
            s.thread = H(Arc(taperMesh, false, arcAdd, chest, Quaternion.identity, 9999, (b, time) =>
            {
                if (!p) return; var to = s.blinkFrom + Vector3.up * 1.0f; var from = p.transform.position + Vector3.up * 1.0f; var d = to - from;
                if (d.sqrMagnitude < 1e-4f) { b.go.transform.localScale = Vector3.zero; return; }
                b.go.transform.SetPositionAndRotation(from, AlongFacingCam(from, d)); b.go.transform.localScale = new Vector3(0.07f, 1, d.magnitude);
                b.mpb.SetFloat(PWidth, 0.8f); b.mpb.SetFloat(PIntensity, 1.2f);
                if (Random.value < 0.7f) arcMotes.Emit(to - d * Random.value * 0.6f, -d.normalized * 30f, 0.07f, 0.06f);
            }, MBeam, ArcLilac, HotWhite, AshMagenta));
        }
        // a snapshot of his current pose as a rim-lit lilac shell. erode: eaten away (departure); follow: stays on his drawn
        // body (arrival, blocked); rebuild > 0: drawn only up to a height that rises from his feet over `rebuild` seconds
        void BakeShell(ArcState s, float life, float intensity, bool erode, bool follow = false, float rebuild = 0)
        {
            if (s.skins == null) return;
            foreach (var sk in s.skins)
            {
                if (!sk) continue;
                var mesh = new Mesh(); sk.BakeMesh(mesh, true);
                var tf = sk.transform; float feet = tf.position.y;
                foreach (var t in tf.root.GetComponentsInChildren<PlayerController>(true)) { feet = t.transform.position.y; break; }
                Arc(mesh, true, arcAdd, tf.position, tf.rotation, life, (b, u) =>
                {
                    if (follow && tf) b.go.transform.SetPositionAndRotation(tf.position, tf.rotation);
                    b.mpb.SetFloat(PFresnel, 2.5f); b.mpb.SetFloat(PDissolve, erode ? Mathf.Clamp01(u * 1.05f) : 0);
                    b.mpb.SetFloat(PIntensity, intensity * (erode || rebuild > 0 ? 1 : 1 - u));
                    b.mpb.SetVector(PNoiseScale, new Vector4(14, 14, 0, 0)); b.mpb.SetFloat(PNoiseAmt, erode ? 0.6f : 0.2f); b.mpb.SetFloat(PSoft, 0.08f);
                    if (rebuild > 0) b.mpb.SetFloat(PClipY, (follow && tf ? tf.position.y : feet) - 0.05f + 1.95f * Mathf.Clamp01(u * life / rebuild));
                }, MShell, ArcLilac, HotWhite, AshMagenta);
            }
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
            for (int k = 0; k < 12; k++) { var off = Random.onUnitSphere * 0.8f; arcMotes.Emit(chest + off, -off / 0.1f, 0.09f, 0.1f); }
            // rebuilt from the feet up: his body stays hidden 0.12 s while a lilac shell of him is drawn up to a rising cut
            // with a bright band at the cut, then the body shows and the shell fades on it. Everything follows his drawn spot.
            Hide(p, true);   // same tick the kit showed him: the body is never drawn before the rebuild
            StartCoroutine(Rebuild(p, s, 0.12f));
            // a crisp thin violet floor ring at his feet, expanding 0.3 -> 0.9 m over 0.15 s
            Arc(RingU(0.965f, 1f, 64), true, arcAddFloor, new Vector3(at.x, 0.05f, at.z), Quaternion.identity, 0.18f, (b, u) =>
            {
                if (p) { var pp = p.transform.position; b.go.transform.position = new Vector3(pp.x, 0.05f, pp.z); }
                float r = Mathf.Lerp(0.3f, 0.9f, Ease(Mathf.Clamp01(u / 0.83f))); b.go.transform.localScale = new Vector3(r, 1, r);
                b.mpb.SetFloat(PProgress, 1.1f); b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PDissolve, u * u); b.mpb.SetFloat(PIntensity, 1.6f);
            }, MStroke, new Color(1.3f, 0.7f, 2.2f) * 0.75f, new Color(1.3f, 0.7f, 2.2f) * 0.9f, AshMagenta);
            StartCoroutine(AstroPulse(p, 0.09f));
            ArcLight(chest, ArcLilac, 0.9f, 3f, 0.15f);
        }
        // one astrolabe pulse: a small ring at the astrolabe (0.25 m), 0.1 s, once the rebuild band passes his shoulder
        System.Collections.IEnumerator AstroPulse(PlayerController p, float after)
        {
            yield return new WaitForSeconds(after); if (!p) yield break;
            var a = AstroPos(p);
            Arc(RingU(0.23f, 0.25f, 32), true, arcAdd, a, FaceCam(a), 0.1f, (b, u) =>
            {
                if (!p) return; var q = AstroPos(p); b.go.transform.SetPositionAndRotation(q, FaceCam(q)); b.go.transform.localScale = Vector3.one * Mathf.Lerp(0.8f, 1.25f, u);
                b.mpb.SetFloat(PWidth, 0.6f); b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 1.2f);
            }, MBeam, ArcLilac, HotWhite, AshMagenta);
        }

        System.Collections.IEnumerator Rebuild(PlayerController p, ArcState s, float seconds)
        {
            if (!p) yield break;
            yield return new WaitForEndOfFrame();   // bake the pose he is drawn in this frame
            if (!p) yield break;
            Hide(p, true);
            BakeShell(s, seconds + 0.12f, 1.1f, false, follow: true, rebuild: seconds);
            yield return new WaitForSeconds(seconds);
            if (p && !(p.Kit is ArcanistKit k && k.Blinking)) Hide(p, false);
        }

        public void BlinkBlocked(PlayerController p)
        {
            var s = St(p);
            if (s.thread != null) { KillH(s.thread); s.thread = null; }
            StartCoroutine(Flicker(p, s));
        }
        // stunned mid-blink: two flickers where the "off" frames show his lilac shell at 60 % (never nothing), then 8 motes
        // snap in to his chest and one small rim pulse
        System.Collections.IEnumerator Flicker(PlayerController p, ArcState s)
        {
            for (int k = 0; k < 2; k++)
            {
                if (!p) yield break; yield return new WaitForEndOfFrame();
                Hide(p, true); BakeShell(s, 0.05f, 0.6f, false, follow: true);
                yield return new WaitForSeconds(0.05f);
                if (!p) yield break; Hide(p, false); yield return new WaitForSeconds(0.05f);
            }
            if (!p) yield break;
            var chest = p.transform.position + Vector3.up * 1.0f;
            for (int k = 0; k < 8; k++) { var off = Random.onUnitSphere * 0.7f; arcMotes.Emit(chest + off, -off / 0.1f, 0.09f, 0.1f); }
            yield return new WaitForEndOfFrame(); if (p) BakeShell(s, 0.12f, 0.8f, false, follow: true);
        }
    }
}
