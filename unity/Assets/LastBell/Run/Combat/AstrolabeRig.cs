using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LastBell.Run
{
    // The Arcanist's astrolabe, driven in code on top of the baked clips. The model is the procedural remodel
    // (build-evidence/arcanist-orb-remodel-20261028/, prototype/arena-3d/models/blender/arcanist_astrolabe_*.py): node
    // 'Astrolabe' on his orbit bone is the pivot (the glass globe's centre), with child parts the code moves on their own:
    //   Astro_RingOuter (spins about the device's vertical) > Astro_RingMid (tilts on pins) > Astro_RingInner (turns on
    //   pins); Astro_Pendant and Astro_Charm1..3 (pivoted at their tops); Astro_Globe, Astro_Core, Astro_Disc, Astro_Frame,
    //   Astro_Finial (fixed). At all-zero ring angles the three rings are coplanar: the Disintegrate lens.
    //   Rings: at rest a slow armillary drift; the lens lines them up facing the beam and they tremble harder at each damage
    //     step; a broken or cancelled beam spins them apart; Surge lays the inner ring flat and tightens it onto the globe.
    //   Pendant and charms: damped pendulums, plumb at rest, swinging with the device's acceleration.
    //   Materials (astrolabe_brass, _glass, _gem, _core, _enamel) are set here absolutely by name; CharacterPresenter leaves
    //     them alone. Brass is a little rougher and less metallic for the far gameplay camera.
    //   Light: a lilac halo and a white core sprite at the globe facing whichever camera renders them, a fresnel rim shell on
    //     the glass, a small point light; core and gem emission rise for the lens, Surge (gold) and Blink.
    //   Motion of the whole device: the baked spot is only a target, followed by a critically damped spring in the body's
    //     frame. It sits a little forward at rest, beside his head, clear of his right arm and beside him on screen; it stays
    //     upright facing his heading (the beam during the lens) and leans with its acceleration.
    // Presentation only; gameplay never reads it. LateUpdate runs after the presenter's Update (clip evaluated).
    public sealed class AstrolabeRig : MonoBehaviour
    {
        // ---- tuning
        const float Stiff = 11f, LensStiff = 20f;   // spring frequency (rad/s)
        const float RestForward = 0.15f;            // m forward of the baked rest spot (it sat behind his shoulder)
        const float HeadClear = 0.38f, HeadAbove = 0.32f, ArmClear = 0.28f, HeadMax = 0.60f;   // at most 0.60 m from his head (outside the lens)
        const float RestYaw = 45f;                  // at rest it turns 45 deg, to the side that keeps its disc away from the gameplay camera
        const float ScreenGap = 0.60f;              // min sideways distance from his centre line, along the camera's right
        const float MaxLean = 14f;
        const float MidRest = 55f, InnerRest = 50f, OuterSpin = 22f, InnerSpin = 38f, MidSway = 8f;   // degrees, deg/s
        const float SurgeScale = 0.58f;             // the inner ring tightens onto the globe
        static readonly Color HaloIdle = new Color(0.85f, 0.55f, 1.6f), CoreWhite = new Color(1.6f, 1.75f, 2.0f), HaloGold = new Color(2.0f, 1.45f, 0.55f);
        static readonly Color RimColor = new Color(0.91f, 0.89f, 1.0f);
        static readonly Color CoreEmit = new Color(1f, 0.93f, 1f), CoreGold = new Color(1f, 0.78f, 0.38f);
        static readonly Color GlassEmit = new Color(0.06f, 0.12f, 0.42f), GemEmit = new Color(0.55f, 0.26f, 1.35f);
        static readonly Color LightIdle = new Color(0.80f, 0.70f, 1.0f), LightGold = new Color(1f, 0.82f, 0.5f);

        static readonly List<AstrolabeRig> live = new List<AstrolabeRig>();
        static bool hooked;

        Transform orbit, root, head, upperArm, foreArm, hand;
        Vector3 restLocalPos, restL; Quaternion restLocalRot, restRootRot;
        Vector3 globeLocal;                          // the glass globe's centre in this node's frame (0 for the remodel)
        CharacterPresenter presenter;
        // parts
        Transform ringO, ringM, ringI; float midSign = 1, innerSign = 1; Quaternion outerRest = Quaternion.identity;
        readonly List<Hanger> hangers = new List<Hanger>();
        sealed class Hanger { public Transform t; public Quaternion restRel; public float len, w; public Vector3 b, bv, prevPos, prevVel; public bool primed; }
        readonly List<Material> brass = new List<Material>(), glass = new List<Material>(), gem = new List<Material>(), coreMat = new List<Material>(), enamel = new List<Material>();
        // glow
        Transform halo, core, rim, surgeRing, shade; MeshRenderer haloR, coreR, rimR, surgeR, shadeR; MaterialPropertyBlock haloB, coreB, rimB, surgeB, shadeB; Light glowLight;
        // motion state
        Vector3 x, v, coreL; bool primed, wasVisible, visible = true; float roll, screenPush, screenSign = 1; Vector3 lean;
        float oA, iA, apartO, apartM, apartI, apartMid, apartIAngle, yawNow; Vector3 prevDrawn; bool drawnPrimed, snapped; Vector3 prevHand; bool hasPrevHand;
        // states pushed by PlayerFx
        float lensUntil = -9, lensW; Vector3 lensPoint, lensDir; int lensStep;
        float surgeCharge = -1, surgeLife = 0.3f, surgeActive, surgeW, charge; float pulse, pulseDecay = 8f, flash, flashAt = -9, surgeRingR = 0.2f, surgeRingA;

        // the glass globe, live (follows his root even before this frame's LateUpdate, e.g. right after a Blink)
        public Vector3 CorePos => root ? root.TransformPoint(coreL) : transform.position;

        public static AstrolabeRig Attach(GameObject model)
        {
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                if (t.name == "Astrolabe") return t.GetComponent<AstrolabeRig>() ?? t.gameObject.AddComponent<AstrolabeRig>();
            return null;
        }

        void Awake()
        {
            orbit = transform.parent; root = transform; while (root.parent && root.parent.GetComponent<CharacterPresenter>() == null) root = root.parent;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "head") head = t; else if (t.name == "upperarm.R") upperArm = t; else if (t.name == "forearm.R") foreArm = t; else if (t.name == "hand.R") hand = t;
            }
            restLocalPos = transform.localPosition; restLocalRot = transform.localRotation;
            restL = root.InverseTransformPoint(transform.position);   // bind pose: the clip graph has not evaluated yet
            restRootRot = Quaternion.Inverse(root.rotation) * transform.rotation;
            presenter = GetComponentInParent<CharacterPresenter>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Astro_RingOuter") ringO = t; else if (t.name == "Astro_RingMid") ringM = t; else if (t.name == "Astro_RingInner") ringI = t;
                else if (t.name == "Astro_Pendant") hangers.Add(new Hanger { t = t, len = 0.16f, w = 7.5f });
                else if (t.name.StartsWith("Astro_Charm")) hangers.Add(new Hanger { t = t, len = 0.03f, w = 16f });
            }
            // the old single-mesh astrolabe had its globe 3.8 cm below the pivot; the remodel's globe is the pivot
            globeLocal = ringO ? Vector3.zero : new Vector3(0.016f, -0.038f, 0f);
            // the authored rest tilts (mid 55 deg about X, inner 50 deg about Y) tell which way the importer's axes turn
            if (ringM) midSign = SignedAbout(ringM.localRotation, Vector3.right) < 0 ? -1 : 1;
            if (ringI) innerSign = SignedAbout(ringI.localRotation, Vector3.up) < 0 ? -1 : 1;
            if (ringO) outerRest = ringO.localRotation;
            foreach (var h in hangers) h.restRel = Quaternion.Inverse(transform.rotation) * h.t.rotation;
            coreL = root.InverseTransformPoint(transform.TransformPoint(globeLocal));
            // materials: the presenter's per-renderer instances, grouped by name; set absolutely (it no longer scales them)
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (!m) continue;
                    if (m.name.StartsWith("astrolabe_brass")) brass.Add(m);
                    else if (m.name.StartsWith("astrolabe_glass")) glass.Add(m);
                    else if (m.name.StartsWith("astrolabe_gem")) gem.Add(m);
                    else if (m.name.StartsWith("astrolabe_core")) coreMat.Add(m);
                    else if (m.name.StartsWith("astrolabe_enamel")) enamel.Add(m);
                }
            foreach (var m in brass) { Set(m, "baseColorFactor", Color.white); SetF(m, "metallicFactor", 0.9f); SetF(m, "roughnessFactor", 1.0f); }
            foreach (var m in glass) Set(m, "emissiveFactor", GlassEmit);
            BuildGlow();
            ApplyEmission(1);
            Debug.Log($"[astrolabe] rig on {root.name}: rest {restL:F3}, globe {coreL:F3}, parts {(ringO ? "remodel" : "old single mesh")}, rings {(ringO && ringM && ringI ? "ok" : "missing")}, hangers {hangers.Count}, materials brass {brass.Count} glass {glass.Count} gem {gem.Count} core {coreMat.Count}");
        }
        static float SignedAbout(Quaternion q, Vector3 axis)
        {
            q.ToAngleAxis(out float a, out Vector3 ax); if (a > 180) a -= 360;
            return Mathf.Abs(a) < 1e-3f ? 0 : a * Mathf.Sign(Vector3.Dot(ax, axis));
        }
        static void Set(Material m, string p, Color c) { if (m.HasProperty(p)) m.SetColor(p, c); }
        static void SetF(Material m, string p, float f) { if (m.HasProperty(p)) m.SetFloat(p, f); }

        // Registered for per-camera facing for its whole life, not just while enabled: the main menu switches off every
        // script on its posed heroes (ConfrontationMenu.Sanitize) and then re-shows all their renderers
        // (CharacterPresenter.SetHidden), so the glow parts must still be faced, sized and coloured there.
        void OnEnable()
        {
            if (!live.Contains(this)) live.Add(this);
            if (!hooked) { hooked = true; RenderPipelineManager.beginCameraRendering += (ctx, cam) => FaceAll(cam); Camera.onPreCull += FaceAll; }
        }
        void OnDestroy() => live.Remove(this);
        static void FaceAll(Camera cam) { if (cam) foreach (var r in live) if (r && r.gameObject.activeInHierarchy) r.Face(cam); }

        void BuildGlow()
        {
            var src = Resources.Load<Material>("Run/FX_ArcaneFlow");
            var sh = src ? src.shader : Shader.Find("LastBell/FX/ArcaneFlow");
            if (sh != null)
            {
                var add = new Material(sh) { name = "Astrolabe glow", renderQueue = 3061 };
                add.SetFloat("_Mode", 1); add.SetFloat("_NoiseAmt", 0); add.SetFloat("_Dissolve", 0); add.SetFloat("_SrcBlend", 1); add.SetFloat("_DstBlend", 1);
                var shell = new Material(sh) { name = "Astrolabe rim", renderQueue = 3001 };
                shell.SetFloat("_Mode", 4); shell.SetFloat("_Fresnel", 3f); shell.SetFloat("_NoiseAmt", 0); shell.SetFloat("_Dissolve", 0); shell.SetFloat("_SrcBlend", 1); shell.SetFloat("_DstBlend", 1); shell.SetFloat("_Cull", 2);
                var ring = new Material(sh) { name = "Astrolabe surge ring", renderQueue = 3061 };
                ring.SetFloat("_Mode", 3); ring.SetFloat("_NoiseAmt", 0.3f); ring.SetFloat("_Dissolve", 0); ring.SetFloat("_SrcBlend", 1); ring.SetFloat("_DstBlend", 1);
                var quad = Quad();
                var addTop = new Material(add) { name = "Astrolabe core sprite", renderQueue = 3100 };
                var shadeM = new Material(sh) { name = "Astrolabe shade", renderQueue = 3058 };
                shadeM.SetFloat("_Mode", 6); shadeM.SetFloat("_NoiseAmt", 0); shadeM.SetFloat("_Dissolve", 0); shadeM.SetFloat("_SrcBlend", 1); shadeM.SetFloat("_DstBlend", 10);
                (shade, shadeR) = Part("Astrolabe shade", quad, shadeM); SetWorldScale(shade, 0.001f); shadeB = new MaterialPropertyBlock();
                (halo, haloR) = Part("Astrolabe halo", quad, add); (core, coreR) = Part("Astrolabe core sprite", quad, addTop);
                if (ringO) { (rim, rimR) = Part("Astrolabe rim", Sphere(16, 24), shell); rim.localPosition = globeLocal; SetWorldScale(rim, 0.045f * 2 * 1.03f); }
                (surgeRing, surgeR) = Part("Astrolabe surge ring", DashRing(0.9f, 1f, 3, 24f), ring);
                haloB = new MaterialPropertyBlock(); coreB = new MaterialPropertyBlock(); rimB = new MaterialPropertyBlock(); surgeB = new MaterialPropertyBlock();
                // never drawn unplaced: tiny until the first camera faces them
                SetWorldScale(halo, 0.001f); SetWorldScale(core, 0.001f); SetWorldScale(surgeRing, 0.001f);
            }
            var lg = new GameObject("Astrolabe light"); lg.transform.SetParent(transform, false); lg.transform.localPosition = globeLocal;
            glowLight = lg.AddComponent<Light>(); glowLight.type = LightType.Point; glowLight.shadows = LightShadows.None;
            glowLight.range = 0.6f; glowLight.intensity = 0.1f; glowLight.color = LightIdle;
        }
        (Transform, MeshRenderer) Part(string name, Mesh mesh, Material m)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            return (go.transform, r);
        }
        static Mesh Quad()
        {
            var q = new Mesh { name = "Astrolabe sprite" };
            q.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
            q.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            q.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            q.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            q.triangles = new[] { 0, 2, 1, 1, 2, 3 }; q.RecalculateBounds(); return q;
        }
        // a sphere of radius 0.5 with normals (the rim shell's fresnel needs them)
        static Mesh Sphere(int lat, int lon)
        {
            var vs = new List<Vector3>(); var ns = new List<Vector3>(); var uv = new List<Vector2>(); var cs = new List<Color>(); var tr = new List<int>();
            for (int i = 0; i <= lat; i++)
                for (int j = 0; j <= lon; j++)
                {
                    float th = Mathf.PI * i / lat, ph = 2 * Mathf.PI * j / lon;
                    var n = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    vs.Add(n * 0.5f); ns.Add(n); uv.Add(new Vector2((float)j / lon, 1 - (float)i / lat)); cs.Add(Color.white);
                }
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    int a0 = i * (lon + 1) + j, b0 = a0 + lon + 1;
                    tr.AddRange(new[] { a0, a0 + 1, b0, a0 + 1, b0 + 1, b0 });
                }
            var m = new Mesh { name = "Astrolabe rim shell" }; m.SetVertices(vs); m.SetNormals(ns); m.SetUVs(0, uv); m.SetColors(cs); m.SetTriangles(tr, 0); m.RecalculateBounds(); return m;
        }
        // a ring of `dashes` arcs in the XY plane, U along each arc, V across the band (ArcaneFlow Beam mode)
        static Mesh DashRing(float inner, float outer, int dashes, float gapDeg)
        {
            var vs = new List<Vector3>(); var uv = new List<Vector2>(); var tr = new List<int>(); var cs = new List<Color>(); const int per = 16;
            float span = 360f / dashes;
            for (int k = 0; k < dashes; k++)
                for (int i = 0; i <= per; i++)
                {
                    float a = (k * span + gapDeg / 2 + (span - gapDeg) * i / per) * Mathf.Deg2Rad; var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                    vs.Add(d * inner); vs.Add(d * outer); uv.Add(new Vector2((float)i / per, 0)); uv.Add(new Vector2((float)i / per, 1)); cs.Add(Color.white); cs.Add(Color.white);
                    if (i < per) { int b = vs.Count - 2; tr.AddRange(new[] { b, b + 1, b + 2, b + 2, b + 1, b + 3 }); }
                }
            var m = new Mesh { name = "Astrolabe dash ring" }; m.SetVertices(vs); m.SetUVs(0, uv); m.SetColors(cs); m.SetTriangles(tr, 0); m.RecalculateBounds(); return m;
        }

        // ---------------------------------------------------------------- called by PlayerFx.Arcanist
        // Disintegrate: the globe hangs at `point`, the rings line up facing `dir`; call every frame (it lets go 0.12 s later)
        public void Lens(Vector3 point, Vector3 dir, int step) { lensUntil = Time.time + 0.12f; lensPoint = point; if (dir.sqrMagnitude > 1e-4f) lensDir = dir.normalized; if (step > lensStep) Pulse(0.6f, 6f); lensStep = step; }
        public void LensRelease() { lensUntil = -9; lensStep = 0; }
        // a broken or cancelled beam: the rings spin apart and settle back
        public void Apart(float strength = 1f) { apartO = 900 * strength; apartM = 80 * strength; apartI = -1100 * strength; }
        // Surge: charging over `seconds` (the inner ring lies flat and tightens onto the globe), then active until SurgeOff
        public void SurgeCharge(float seconds) { surgeCharge = Time.time; surgeLife = Mathf.Max(0.05f, seconds); }
        public void SurgeCancel() { surgeCharge = -1; Pulse(0.4f, 10f); Apart(0.5f); }
        public void SurgeOn() { surgeCharge = -1; surgeActive = 1; Pulse(1.0f, 5f); }
        public void SurgeOff() { surgeActive = 0; Pulse(0.4f, 6f); }
        // Blink arrival: one soft pulse (core and gems brighten, the halo blooms a little), 0.15 s
        public void Flash() { flash = 1; flashAt = Time.time; }
        public void Pulse(float amount, float decayPerSecond = 8f) { pulse = Mathf.Max(pulse, amount); pulseDecay = decayPerSecond; }

        // ---------------------------------------------------------------- per frame
        void LateUpdate()
        {
            if (!orbit || !root) return;
            float dt = Time.deltaTime; if (dt <= 0) return;
            visible = presenter ? !presenter.Hidden : true;
            var bakedW = orbit.TransformPoint(restLocalPos);
            var L = root.InverseTransformPoint(bakedW);

            float lensT = Time.time < lensUntil ? 1 : 0; lensW = Mathf.MoveTowards(lensW, lensT, dt / (lensT > 0 ? 0.12f : 0.2f));
            if (lensT == 0 && lensW == 0) lensStep = 0;
            charge = surgeCharge >= 0 ? Mathf.Clamp01((Time.time - surgeCharge) / surgeLife) : 0;
            surgeW = Mathf.MoveTowards(surgeW, Mathf.Max(charge, surgeActive), dt / 0.15f);

            // 1. target (the pivot) in the body's frame
            float nearRest = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.08f, 0.35f, (L - restL).magnitude));
            L.z += RestForward * nearRest;
            if (head)
            {
                var H = root.InverseTransformPoint(head.position) + Vector3.up * 0.08f;
                float high = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(H.y - 0.5f, H.y - 0.25f, L.y));
                L.x = Mathf.Max(L.x, Mathf.Lerp(L.x, H.x + HeadClear, high));
                L.y = Mathf.Min(L.y, H.y + HeadAbove);
            }
            L = ClearArm(L);
            if (hand) { prevHand = root.InverseTransformPoint(hand.position); hasPrevHand = true; }
            if (lensW > 0)
            {
                // the globe (not the pivot) goes to the lens point
                var lensPivot = root.InverseTransformPoint(lensPoint) - root.InverseTransformVector(LensFrame() * (restRootRot * Vector3.Scale(globeLocal, transform.lossyScale)));
                L = Vector3.Lerp(L, lensPivot, Mathf.SmoothStep(0, 1, lensW));
            }

            // 2. follow with a critically damped spring
            if (!primed || (visible && !wasVisible) || (L - x).sqrMagnitude > 1.2f * 1.2f) { x = L; v = Vector3.zero; primed = true; snapped = true; foreach (var hg in hangers) hg.primed = false; }
            float w = Mathf.Lerp(Stiff, LensStiff, lensW);
            int n = Mathf.CeilToInt(dt / 0.011f); float h = dt / n; Vector3 acc = Vector3.zero;
            for (int i = 0; i < n; i++) { acc = w * w * (L - x) - 2 * w * v; v += acc * h; x += v * h; }
            if (v.magnitude > 7f) v = v.normalized * 7f;
            wasVisible = visible;
            float free = 1 - Mathf.SmoothStep(0, 1, lensW);   // these hold outside the lens only
            if (head && free > 0)
            {
                var H = root.InverseTransformPoint(head.position);
                var dh = x - H; if (dh.magnitude > HeadMax) x = Vector3.Lerp(x, H + dh.normalized * HeadMax, free);
            }
            if (free > 0)
            {
                // the hand must never be inside the cage: a nudge on the spring state, at most 1.5 m/s
                var push = ClearArm(x) - x; push = Vector3.ClampMagnitude(push, 1.5f * dt) * free; x += push;
            }
            // the drawn position: beside him on screen, then within HeadMax of his head, then at most 6 cm per frame
            var drawn = ScreenSide(x, dt);
            if (head && free > 0)
            {
                var H2 = root.InverseTransformPoint(head.position);
                var dh2 = drawn - H2; if (dh2.magnitude > HeadMax) drawn = Vector3.Lerp(drawn, H2 + dh2.normalized * HeadMax, free);
            }
            if (drawnPrimed && !snapped && free > 0.5f) drawn = prevDrawn + Vector3.ClampMagnitude(drawn - prevDrawn, 0.06f * Mathf.Max(1f, dt * 30f));
            prevDrawn = drawn; drawnPrimed = true; snapped = false;

            // 3. orientation of the device: upright, facing his heading (or the beam), leaning with its acceleration
            var a = acc; a.y = 0; var leanT = Vector3.ClampMagnitude(Vector3.Cross(Vector3.up, a) * 0.5f, MaxLean);
            lean = Vector3.Lerp(lean, leanT, 1 - Mathf.Exp(-dt / 0.1f));
            roll = lensW * Mathf.Sin(Time.time * (14 + 8 * lensStep)) * (1 + 1.5f * lensStep);
            var leanW = root.rotation * lean;
            {
                var camM = Camera.main; float want = RestYaw;
                if (camM)
                {
                    var fr = LensFrame(); var toC = camM.transform.position - root.TransformPoint(x); toC.y = 0;
                    float dp = Vector3.Dot(fr * Quaternion.AngleAxis(RestYaw, Vector3.up) * Vector3.forward, toC.normalized);
                    float dm = Vector3.Dot(fr * Quaternion.AngleAxis(-RestYaw, Vector3.up) * Vector3.forward, toC.normalized);
                    want = dm < dp - 0.05f ? -RestYaw : dp < dm - 0.05f ? RestYaw : (yawNow >= 0 ? RestYaw : -RestYaw);
                }
                yawNow = Mathf.Lerp(yawNow, want, 1 - Mathf.Exp(-dt / 0.3f));
            }
            var rot = Quaternion.AngleAxis(leanW.magnitude, leanW.sqrMagnitude > 1e-6f ? leanW.normalized : Vector3.right)
                * LensFrame() * Quaternion.AngleAxis(yawNow * (1 - Mathf.SmoothStep(0, 1, lensW)), Vector3.up) * Quaternion.AngleAxis(roll, Vector3.forward) * restRootRot;
            transform.SetPositionAndRotation(root.TransformPoint(drawn), rot);
            coreL = root.InverseTransformPoint(transform.TransformPoint(globeLocal));

            // 4. the rings
            if (ringO && ringM && ringI) DriveRings(dt);
            // 5. pendant and charms
            foreach (var hg in hangers) Swing(hg, dt);

            // 6. light
            pulse = Mathf.MoveTowards(pulse, 0, dt * pulseDecay * Mathf.Max(0.3f, pulse));
            flash = Mathf.Clamp01(1 - (Time.time - flashAt) / 0.25f);
            float ringT = surgeCharge >= 0 ? Mathf.Lerp(0.20f, 0.075f, Mathf.SmoothStep(0, 1, charge)) : 0.075f;   // tightens with the inner ring
            surgeRingR = surgeCharge >= 0 || surgeActive > 0 ? ringT : Mathf.Lerp(surgeRingR, 0.2f, dt * 6);
            surgeRingA = Mathf.MoveTowards(surgeRingA, surgeCharge >= 0 || surgeActive > 0 ? 1 : 0, dt / 0.12f);
            if (glowLight)
            {
                glowLight.enabled = visible;
                glowLight.intensity = 0.1f * (1 + 0.5f * lensW * lensStep / 3f + 0.8f * surgeW + pulse) * (1 + 2f * flash);
                glowLight.color = Color.Lerp(LightIdle, LightGold, surgeW * 0.8f);
            }
            ApplyEmission(presenter ? presenter.GlowLevel : 1);
        }

        void DriveRings(float dt)
        {
            float t = Time.time;
            // apart impulses decay (tau about 0.18 s)
            float k = Mathf.Exp(-dt / 0.18f); apartO *= k; apartM *= k; apartI *= k;
            apartMid += apartM * dt; apartMid *= Mathf.Exp(-dt / 0.3f);
            oA += (OuterSpin + apartO) * dt; iA += (InnerSpin + apartI) * dt; apartIAngle += apartI * dt; apartIAngle *= Mathf.Exp(-dt / 0.35f);
            oA = Mathf.Repeat(oA, 360); iA = Mathf.Repeat(iA, 360);
            float lw = Mathf.SmoothStep(0, 1, lensW), sw = Mathf.SmoothStep(0, 1, surgeW);
            // the lens: all rings to zero (coplanar, facing the beam), trembling harder at each damage step
            float trem = lw * (1.5f + 2f * lensStep);
            float o = Mathf.LerpAngle(oA, 0, Mathf.Max(lw, sw));
            float m = Mathf.Lerp(MidRest + MidSway * Mathf.Sin(t * 0.6f), 0, Mathf.Max(lw, sw)) + apartMid + trem * Mathf.Sin(t * (10 + 6 * lensStep));
            float rock = 30f * Mathf.Sin(t * 0.55f) + apartIAngle;   // rocks about face-on instead of spinning
            float i = Mathf.LerpAngle(rock, 0, lw) + trem * Mathf.Sin(t * (13 + 6 * lensStep) + 1.3f);
            ringO.localRotation = outerRest * Quaternion.AngleAxis(o, Vector3.up);
            ringM.localRotation = Quaternion.AngleAxis(midSign * m, Vector3.right);
            var idleInner = Quaternion.AngleAxis(innerSign * i, Vector3.up);
            // Surge: the inner ring lies flat (parallel to the disc, whatever the outer ring's turn) and tightens on the globe
            var flat = Quaternion.Inverse(ringO.localRotation * ringM.localRotation);
            ringI.localRotation = Quaternion.Slerp(idleInner, flat, sw);
            ringI.localScale = Vector3.one * Mathf.Lerp(1f, SurgeScale, sw);
        }

        void Swing(Hanger hg, float dt)
        {
            // a damped pendulum: the bob's horizontal offset b (m) driven by the pivot's horizontal acceleration
            var pos = hg.t.position;
            if (!hg.primed) { hg.prevPos = pos; hg.prevVel = Vector3.zero; hg.b = hg.bv = Vector3.zero; hg.primed = true; }
            var vel = (pos - hg.prevPos) / dt; var a = (vel - hg.prevVel) / dt; hg.prevPos = pos; hg.prevVel = vel;
            a.y = 0; a = Vector3.ClampMagnitude(a, 40f);
            float w = hg.w, z = 0.14f; int n = Mathf.CeilToInt(dt / 0.008f); float h = dt / n;
            for (int s = 0; s < n; s++) { var acc = -w * w * hg.b - 2 * z * w * hg.bv - a; hg.bv += acc * h; hg.b += hg.bv * h; }
            hg.b = Vector3.ClampMagnitude(hg.b, hg.len * 0.6f);
            // plumb: the device's heading only (no lean, no ring spin), tipped toward the bob
            var f = transform.forward; f.y = 0; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            var yaw = Quaternion.LookRotation(f.normalized, Vector3.up);
            var tip = Quaternion.FromToRotation(Vector3.down, (hg.b + Vector3.down * hg.len).normalized);
            hg.t.rotation = tip * yaw * hg.restRel;
        }

        void ApplyEmission(float level)
        {
            // core: 3 at rest; the lens 3 -> 8 by damage step; Surge 5 while charging, 4 held, toward gold; Blink 6 for 0.15 s
            float c = 4.5f + lensW * (3.5f * lensStep / 3f) + surgeW * (charge > 0 ? 2f : 1f) + 2f * pulse;
            c = Mathf.Max(c, 8f * flash);
            if (surgeW > 0) c = Mathf.Lerp(c, Mathf.Min(c, 4f), surgeW);   // gold must not blow out to white
            var cc = Color.Lerp(CoreEmit, CoreGold, surgeW) * c * level;
            foreach (var m in coreMat) Set(m, "emissiveFactor", cc);
            foreach (var m in glass) Set(m, "emissiveFactor", GlassEmit * level);
            foreach (var m in gem) Set(m, "emissiveFactor", GemEmit * (1 + 1.5f * flash + 0.5f * surgeW) * level);
        }

        Quaternion LensFrame()
        {
            var fwd = root.forward; fwd.y = 0; if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            var aim = lensDir; aim.y = 0;
            if (lensW > 0 && aim.sqrMagnitude > 1e-4f) fwd = Vector3.Slerp(fwd.normalized, aim.normalized, Mathf.SmoothStep(0, 1, lensW));
            return Quaternion.LookRotation(fwd.normalized, Vector3.up);
        }

        // keep it beside him on screen: at least ScreenGap from his centre line along the camera's flat right
        Vector3 ScreenSide(Vector3 L, float dt)
        {
            var cam = Camera.main; float target = 0;
            if (cam && lensW < 0.99f)
            {
                var cr = cam.transform.right; cr.y = 0; cr.Normalize();
                var W = root.TransformPoint(L); var c = root.position; float sx = Vector3.Dot(W - c, cr);
                if (Mathf.Abs(sx) > 0.15f) screenSign = Mathf.Sign(sx);
                else { float facing = Vector3.Dot(root.forward, cr); if (Mathf.Abs(facing) > 0.5f) screenSign = -Mathf.Sign(facing); }   // side-on to the camera: go behind him, not over his chest
                if (Mathf.Abs(sx) < ScreenGap) target = screenSign * (ScreenGap - Mathf.Abs(sx));
                target *= 1 - lensW;
                screenPush = Mathf.Lerp(screenPush, target, 1 - Mathf.Exp(-dt / 0.08f));
                return root.InverseTransformPoint(W + cr * screenPush);
            }
            screenPush = Mathf.Lerp(screenPush, 0, 1 - Mathf.Exp(-dt / 0.25f));
            return L;
        }

        Vector3 ClearArm(Vector3 L)
        {
            if (!hand) return L;
            var off = primed ? coreL - x : Vector3.zero;   // keep the globe (not the pivot) clear
            var G = L + off;
            var hp = root.InverseTransformPoint(hand.position);
            var ep = foreArm ? root.InverseTransformPoint(foreArm.position) : hp;
            var tip = hp + (ep != hp ? (hp - ep).normalized * 0.12f : Vector3.zero);   // past the wrist to the fingertips
            // look 0.1 s ahead along the hand's motion so the spring starts moving before the hand arrives
            var ahead = hasPrevHand && Time.deltaTime > 0 ? (hp - prevHand) / Time.deltaTime * 0.1f : Vector3.zero;
            if (ahead.magnitude > 0.3f) ahead = ahead.normalized * 0.3f;
            G = PushCapsule(G, ep, tip, ArmClear);
            G = PushCapsule(G, ep + ahead, tip + ahead, ArmClear);
            G = PushCapsule(G, ep + ahead * 2, tip + ahead * 2, ArmClear);
            if (upperArm && foreArm) G = PushCapsule(G, root.InverseTransformPoint(upperArm.position), root.InverseTransformPoint(foreArm.position), ArmClear);
            return G - off;
        }
        static Vector3 PushCapsule(Vector3 p, Vector3 a, Vector3 b, float r)
        {
            var ab = b - a; float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0;
            var c = a + ab * t; var d = p - c; float m = d.magnitude;
            if (m >= r) return p;
            if (m < 1e-4f) d = Vector3.right; return c + d.normalized * r;
        }

        // ---------------------------------------------------------------- per camera: sprites face the camera rendering them
        void Face(Camera cam)
        {
            if (!halo) return;
            // switched off (menu poses): no LateUpdate, so read the presenter's hidden state here and keep the idle look
            bool on = enabled ? visible : (presenter ? !presenter.Hidden : true);
            if (!enabled) { if (glowLight) glowLight.enabled = on; ApplyEmission(presenter ? presenter.GlowLevel : 1); }
            var g = transform.TransformPoint(globeLocal); var cp = cam.transform.position;
            var toCam = (cp - g).normalized; var rot = cam.transform.rotation; float dist = Vector3.Distance(cp, g);
            // far = the gameplay camera: the glow sits a little further toward the camera (no flicker as rings pass) and is
            // brighter; the brass is a little rougher and less metallic there
            float far = Mathf.InverseLerp(6f, 9f, dist);
            float breathe = 1 + 0.05f * Mathf.Sin(Time.time * 2.6f);
            var haloCol = Color.Lerp(HaloIdle, HaloGold, surgeW * 0.85f);
            float lvl = 1 + lensW * (0.3f + 0.25f * lensStep) + surgeW * 0.6f + pulse;
            haloR.enabled = coreR.enabled = on; if (rimR) rimR.enabled = on; if (shadeR && !on) shadeR.enabled = false;
            var discFwd = transform.forward;   // the enamel disc is on the device's front
            float facing = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.3f, 0.7f, Vector3.Dot(discFwd, toCam)));
            float eye = Mathf.Lerp(1f, 0.2f, facing * (1 - lensW));   // the beam still fires from a bright core
            bool behind = root && Vector3.Dot(g - root.position, (root.position - cp).normalized) > 0.05f;   // past his body plane, away from the camera
            float front = (behind || facing > 0.5f) ? 0f : Mathf.Lerp(0.05f, 0.20f, far), boost = Mathf.Lerp(1f, 1.33f, far);
            float fl = flash * far;   // the Blink pulse is sized for the gameplay camera
            if (shadeR)
            {
                // a violet shade behind the glow, far only: the additive halo vanishes on the cream floor without it
                shadeR.enabled = on && far > 0.3f;
                shade.SetPositionAndRotation(g + toCam * Mathf.Max(0, front - 0.03f), rot); SetWorldScale(shade, 0.22f);
                var sc0 = new Color(0.29f, 0.21f, 0.66f); shadeB.SetColor("_ColorLow", sc0); shadeB.SetColor("_ColorHigh", sc0); shadeB.SetColor("_ColorEdge", sc0);
                shadeB.SetFloat("_Width", 0.5f); shadeB.SetFloat("_Intensity", 0.55f * far * eye); shadeR.SetPropertyBlock(shadeB);
            }
            halo.SetPositionAndRotation(g + toCam * Mathf.Max(0, front - 0.005f), rot); SetWorldScale(halo, Mathf.Lerp(Mathf.Lerp(0.10f, 0.16f, surgeW), 0.16f, far) * (1 + 0.15f * (lvl - 1)) * breathe * (1 + 0.8f * flash + 2f * fl));
            core.SetPositionAndRotation(g + toCam * front, rot); SetWorldScale(core, (0.06f + 0.02f * far + 0.015f * (lvl - 1)) * (1 + 0.3f * flash + 0.6f * fl));
            haloB.SetColor("_ColorLow", haloCol); haloB.SetColor("_ColorHigh", CoreWhite); haloB.SetColor("_ColorEdge", haloCol); haloB.SetFloat("_Intensity", Mathf.Lerp(Mathf.Lerp(0.35f, 0.6f, surgeW), 0.6f, far) * lvl * breathe * boost * (1 + 1f * flash + 2f * fl) * eye);
            var coreCol = Color.Lerp(CoreWhite, new Color(2.0f, 1.56f, 0.8f), surgeW); coreB.SetColor("_ColorLow", coreCol); coreB.SetColor("_ColorHigh", coreCol); coreB.SetColor("_ColorEdge", coreCol); coreB.SetFloat("_Intensity", 1.3f * Mathf.Min(lvl, 2f) * boost * eye);
            haloR.SetPropertyBlock(haloB); coreR.SetPropertyBlock(coreB);
            if (rimR)
            {
                rimB.SetColor("_ColorLow", RimColor); rimB.SetColor("_ColorHigh", RimColor); rimB.SetColor("_ColorEdge", RimColor); rimB.SetFloat("_Intensity", 1.0f);
                rimR.SetPropertyBlock(rimB);
            }
            foreach (var m in brass) { SetF(m, "metallicFactor", Mathf.Lerp(0.9f, 0.6f, far)); SetF(m, "roughnessFactor", Mathf.Lerp(1.0f, 1.3f, far)); Set(m, "baseColorFactor", Color.Lerp(Color.white, new Color(1.15f, 1.05f, 0.85f), far)); }   // far: lighter, warmer brass
            foreach (var m in enamel) Set(m, "baseColorFactor", new Color(2.2f, 2.2f, 2.1f, 1f));   // #0B1026 -> about #1A2350: not a black hole from the front
            if (surgeR)
            {
                surgeR.enabled = on && surgeRingA > 0.01f;
                surgeRing.SetPositionAndRotation(g + toCam * (front + 0.01f), rot * Quaternion.Euler(0, 0, Time.time * 90f)); SetWorldScale(surgeRing, surgeRingR);
                surgeB.SetColor("_ColorLow", new Color(1.8f, 1.2f, 0.45f)); surgeB.SetColor("_ColorHigh", new Color(2.2f, 1.8f, 1.0f)); surgeB.SetColor("_ColorEdge", HaloGold);
                surgeB.SetFloat("_Width", 0.6f); surgeB.SetFloat("_T", Time.time); surgeB.SetFloat("_Intensity", 1.2f * surgeRingA);
                surgeR.SetPropertyBlock(surgeB);
            }
        }
        void SetWorldScale(Transform t, float s)
        {
            var ps = transform.lossyScale; float m = Mathf.Max(1e-4f, (ps.x + ps.y + ps.z) / 3); t.localScale = Vector3.one * (s / m);
        }
    }
}
