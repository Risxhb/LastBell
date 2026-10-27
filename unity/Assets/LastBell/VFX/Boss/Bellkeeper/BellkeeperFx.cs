using System;
using System.Collections.Generic;
using UnityEngine;

namespace LastBell.VFX
{
    // Presentation for the Bellkeeper's attacks (docs/design/spell-vfx-pipeline.md). The encounter timeline calls these
    // on its own ticks; nothing here decides timing, targets or hits. Telegraphs take their geometry from the numbers
    // the attack resolves with. Resolve effects use the boss danger palette (Deep Ink -> Ember Coral, amber/white edge).
    public sealed partial class BellkeeperFx : MonoBehaviour
    {
        public static bool NoFlashes, NoScreenshake;   // accessibility (§10)

        static readonly Color Coral = new Color(1.35f, 0.42f, 0.32f), Amber = new Color(2.2f, 1.45f, 0.65f),
            Ink = new Color(0.063f, 0.094f, 0.145f), Gold = new Color(1.9f, 1.35f, 0.55f),
            Brass = new Color(0.76f, 0.55f, 0.27f), Teal = new Color(0.55f, 1.7f, 1.35f), Ivory = new Color(1.9f, 1.8f, 1.5f);
        static readonly int PShape = Shader.PropertyToID("_Shape"), PRadius = Shader.PropertyToID("_Radius"),
            PInner = Shader.PropertyToID("_Inner"), PHalf = Shader.PropertyToID("_HalfAngle"),
            PProgress = Shader.PropertyToID("_Progress"), PLock = Shader.PropertyToID("_Lock"),
            PFlash = Shader.PropertyToID("_Flash"), PFade = Shader.PropertyToID("_Fade"),
            PMode = Shader.PropertyToID("_Mode"), PLow = Shader.PropertyToID("_ColorLow"),
            PHigh = Shader.PropertyToID("_ColorHigh"), PEdge = Shader.PropertyToID("_ColorEdge"),
            PWidth = Shader.PropertyToID("_Width"), PTail = Shader.PropertyToID("_Tail"),
            PDissolve = Shader.PropertyToID("_Dissolve"), PIntensity = Shader.PropertyToID("_Intensity"),
            PSrc = Shader.PropertyToID("_SrcBlend"), PDst = Shader.PropertyToID("_DstBlend");
        const float Floor = 0.04f;   // telegraphs sit just above the dial's relief

        Material telegraphMat, addMat, alphaMat;
        Mesh cylinder;
        FxParticles sparks, dust, motes;
        readonly List<Telegraph> telegraphs = new List<Telegraph>();
        readonly List<Burst> bursts = new List<Burst>();
        readonly Stack<Burst> burstPool = new Stack<Burst>();
        TrailRenderer trail;

        public static BellkeeperFx Create()
        {
            var go = new GameObject("Bellkeeper FX");
            var fx = go.AddComponent<BellkeeperFx>(); fx.Init(); return fx;
        }

        void Init()
        {
            telegraphMat = new Material(Shader.Find("LastBell/FX/Telegraph"));
            var dissolve = Shader.Find("LastBell/FX/ColorDissolve");
            addMat = new Material(dissolve);
            alphaMat = new Material(dissolve); alphaMat.SetFloat(PSrc, 1); alphaMat.SetFloat(PDst, 10);   // premultiplied
            cylinder = FxMeshes.Cylinder();
            sparks = MakeParticles("Sparks", addMat, 1, Gold, Amber, 0.6f);
            dust = MakeParticles("Dust", alphaMat, 1, new Color(0.30f, 0.24f, 0.36f), new Color(0.62f, 0.52f, 0.66f), 0f);
            motes = MakeParticles("Motes", addMat, 1, Brass, Teal, 0f);
            for (int i = 0; i < 12; i++) burstPool.Push(NewBurst());   // warm pool: no instantiation mid-fight (§11)
            CameraShake.Ensure();
        }

        FxParticles MakeParticles(string name, Material baseMat, float mode, Color low, Color high, float gravity)
        {
            var m = new Material(baseMat); m.SetFloat(PMode, mode); m.SetColor(PLow, low); m.SetColor(PHigh, high); m.SetFloat(PDissolve, 0);
            return FxParticles.Create(transform, name, m, 600, gravity * 9.81f, gravity > 0 ? 1.5f : 2.5f, gravity > 0 ? 0.06f : 0);
        }

        // ---------------- telegraphs ----------------
        public sealed class Telegraph
        {
            internal GameObject go; internal MeshRenderer mr; internal MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            internal float flash = -1, lockFlash, maxFade = 1;
            internal bool locked;
            public float FlashScale = 1;   // per-telegraph resolve-flash strength (1 = shared default); presentation only
            // opt-in: on resolve the fill drops at once to a faint desaturated ivory and clears within 0.15 s, and the
            // telegraph sorts under live ones, so a spent area never looks more dangerous than the next live warning
            public bool SpentFast;
            internal bool spentTinted;
            public void Place(Vector3 origin, float yaw) { go.transform.SetPositionAndRotation(new Vector3(origin.x, Floor, origin.z), Quaternion.Euler(0, yaw, 0)); }
            public void Progress(float p) { mpb.SetFloat(PProgress, Mathf.Clamp01(p)); }
            public void Lock() { if (!locked) { locked = true; lockFlash = 1; } }
            public void Resolve() { flash = 0; }
            public void Expire() { if (flash < 0) flash = 0.1f; }   // fades out with no damage flash (cooled slag)
            public void Tint(Color fill, Color border) { mpb.SetColor(PFill, fill); mpb.SetColor(PBorder, border); }
            // Unfinished Hour inactive outline: dashed, unfilled, low contrast. Presentation only, never resolves.
            public void Memory() { mpb.SetFloat(PMemory, 1); Tint(new Color(0.35f, 0.38f, 0.45f), new Color(0.95f, 0.9f, 0.78f)); maxFade = 0.55f; }
            // removed without a resolve flash (the memory turning into its live warning, or being erased)
            public void Dismiss() { if (flash < 0) flash = 0.2f; }
            // draw only over the floor disc (radius 0 = no clip); presentation only, the hazard owns the hit test
            public void Clip(Vector3 centre, float radius) { mpb.SetFloat(PClipRadius, radius); mpb.SetVector(PClipCentre, new Vector4(centre.x, centre.z, 0, 0)); }
        }
        static readonly int PFill = Shader.PropertyToID("_ColorFill"), PBorder = Shader.PropertyToID("_ColorBorder"),
            PMemory = Shader.PropertyToID("_Memory"), PClipRadius = Shader.PropertyToID("_ClipRadius"), PClipCentre = Shader.PropertyToID("_ClipCentre");

        Telegraph NewTelegraph(int shape, float radius, float inner, float half, Mesh quad)
        {
            var t = new Telegraph { go = new GameObject("Telegraph") };
            t.go.transform.SetParent(transform, false);
            t.go.AddComponent<MeshFilter>().sharedMesh = quad;
            t.mr = t.go.AddComponent<MeshRenderer>(); t.mr.sharedMaterial = telegraphMat;
            t.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; t.mr.receiveShadows = false;
            t.mpb.SetFloat(PShape, shape); t.mpb.SetFloat(PRadius, radius); t.mpb.SetFloat(PInner, inner); t.mpb.SetFloat(PHalf, half);
            t.mpb.SetFloat(PProgress, 0); t.mpb.SetFloat(PLock, 0); t.mpb.SetFloat(PFlash, 0); t.mpb.SetFloat(PFade, 0);
            t.mr.SetPropertyBlock(t.mpb); telegraphs.Add(t); return t;
        }
        static float Pad(float r) => r + 0.3f;
        public Telegraph Cone(float radius, float halfAngle)
        {
            float w = Pad(radius) * Mathf.Sin(Mathf.Min(90, halfAngle) * Mathf.Deg2Rad);
            return NewTelegraph(2, radius, 0, halfAngle, FxMeshes.Quad(-w, w, -0.3f, Pad(radius)));
        }
        public Telegraph Ring(float inner, float outer) => NewTelegraph(1, outer, inner, 0, FxMeshes.Quad(-Pad(outer), Pad(outer), -Pad(outer), Pad(outer)));
        public Telegraph Circle(float radius) => NewTelegraph(0, radius, 0, 0, FxMeshes.Quad(-Pad(radius), Pad(radius), -Pad(radius), Pad(radius)));
        public Telegraph Lane(float length, float width) => NewTelegraph(3, length, width, 0, FxMeshes.Quad(-width / 2 - 0.3f, width / 2 + 0.3f, -0.3f, length + 0.3f));

        // Immediate reset between runs: no telegraph, burst, trail or particle survives a retry.
        public void ResetAll()
        {
            foreach (var t in telegraphs) { Destroy(t.go.GetComponent<MeshFilter>().sharedMesh); Destroy(t.go); }
            telegraphs.Clear();
            for (int i = bursts.Count - 1; i >= 0; i--) { var bb = bursts[i]; if (bb.ownsMesh) Destroy(bb.mf.sharedMesh); bb.go.SetActive(false); burstPool.Push(bb); }
            bursts.Clear();
            if (trail) { trail.emitting = false; trail.transform.SetParent(transform, false); trail.Clear(); }
            sparks.Clear(); dust.Clear(); motes.Clear();
        }

        public void ClearTelegraphs() { foreach (var t in telegraphs) if (t.flash < 0) t.flash = 0.1f; }

        void UpdateTelegraphs(float dt)
        {
            for (int i = telegraphs.Count - 1; i >= 0; i--)
            {
                var t = telegraphs[i];
                float fade = t.mpb.GetFloat(PFade);
                if (t.flash < 0) fade = Mathf.MoveTowards(fade, t.maxFade, dt / 0.08f);
                t.lockFlash = Mathf.MoveTowards(t.lockFlash, 0, dt / 0.25f);
                t.mpb.SetFloat(PLock, t.locked ? 0.5f + t.lockFlash : 0);
                if (t.flash >= 0 && t.SpentFast)
                {
                    t.flash += dt;
                    if (!t.spentTinted) { t.spentTinted = true; t.Tint(new Color(0.55f, 0.52f, 0.46f), new Color(1.1f, 1.05f, 0.95f)); t.mr.sortingOrder = -1; }
                    t.mpb.SetFloat(PFlash, 0);
                    fade = Mathf.Clamp01(0.25f * (1 - t.flash / 0.15f));
                    if (t.flash > 0.15f) { Destroy(t.go.GetComponent<MeshFilter>().sharedMesh); Destroy(t.go); telegraphs.RemoveAt(i); continue; }
                    t.mpb.SetFloat(PFade, fade); t.mr.SetPropertyBlock(t.mpb); continue;
                }
                if (t.flash >= 0)
                {
                    // one short flash on the resolve tick, then clear within 0.2 s so the next warning is visible (§7.4)
                    t.flash += dt;
                    t.mpb.SetFloat(PFlash, (NoFlashes ? 0.35f : Mathf.Clamp01(1 - t.flash / 0.12f)) * t.FlashScale);
                    fade = Mathf.Clamp01(1 - (t.flash - 0.05f) / 0.15f);
                    if (t.flash > 0.2f) { Destroy(t.go.GetComponent<MeshFilter>().sharedMesh); Destroy(t.go); telegraphs.RemoveAt(i); continue; }
                }
                t.mpb.SetFloat(PFade, fade); t.mr.SetPropertyBlock(t.mpb);
            }
        }

        // ---------------- resolve effects ----------------
        sealed class Burst
        {
            public GameObject go; public MeshFilter mf; public MeshRenderer mr; public MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            public bool ownsMesh; public float t, life; public Action<Burst, float> anim;
        }
        Burst NewBurst()
        {
            var b = new Burst { go = new GameObject("Burst") }; b.go.transform.SetParent(transform, false);
            b.mf = b.go.AddComponent<MeshFilter>(); b.mr = b.go.AddComponent<MeshRenderer>();
            b.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; b.go.SetActive(false); return b;
        }
        Burst Play(Mesh mesh, bool ownsMesh, Material mat, Vector3 pos, float yaw, float life, Action<Burst, float> anim,
                   float mode, Color low, Color high, Color edge)
        {
            var b = burstPool.Count > 0 ? burstPool.Pop() : NewBurst();
            b.mf.sharedMesh = mesh; b.ownsMesh = ownsMesh; b.mr.sharedMaterial = mat;
            b.go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0)); b.go.transform.localScale = Vector3.one;
            b.mpb.Clear(); b.mpb.SetFloat(PMode, mode); b.mpb.SetColor(PLow, low); b.mpb.SetColor(PHigh, high); b.mpb.SetColor(PEdge, edge);
            b.mpb.SetFloat(PIntensity, 1); b.t = 0; b.life = life; b.anim = anim; b.go.SetActive(true);
            anim(b, 0); b.mr.SetPropertyBlock(b.mpb); bursts.Add(b); return b;
        }
        void UpdateBursts(float dt)
        {
            for (int i = bursts.Count - 1; i >= 0; i--)
            {
                var b = bursts[i]; b.t += dt;
                if (b.t >= b.life)
                {
                    if (b.ownsMesh) Destroy(b.mf.sharedMesh);
                    b.go.SetActive(false); bursts.RemoveAt(i); burstPool.Push(b); continue;
                }
                b.anim(b, b.t / b.life); b.mr.SetPropertyBlock(b.mpb);
            }
        }
        static float Ease(float u) => 1 - (1 - u) * (1 - u);

        void Sparks(Vector3 pos, Vector3 vel, int count, float spread, float speed, float size, float life)
        {
            for (int i = 0; i < count; i++)
            {
                var dir = (vel + UnityEngine.Random.insideUnitSphere * spread).normalized;
                sparks.Emit(pos + UnityEngine.Random.insideUnitSphere * 0.2f, dir * speed * UnityEngine.Random.Range(0.5f, 1.2f),
                            size * UnityEngine.Random.Range(0.6f, 1.3f), life * UnityEngine.Random.Range(0.6f, 1.2f));
            }
        }
        void Dust(Vector3 pos, float radius, int count, float rise)
        {
            for (int i = 0; i < count; i++)
            {
                float a = UnityEngine.Random.value * Mathf.PI * 2; var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                dust.Emit(pos + d * radius * UnityEngine.Random.Range(0.85f, 1.05f) + Vector3.up * 0.2f,
                          d * UnityEngine.Random.Range(0.5f, 1.6f) + Vector3.up * rise * UnityEngine.Random.Range(0.4f, 1f),
                          UnityEngine.Random.Range(0.8f, 1.6f), UnityEngine.Random.Range(0.6f, 1.1f), 0.55f);
            }
        }

        // Bell Sweep: a bright arc front sweeps across the locked cone from the boss's right edge to its left, then erodes.
        public void SweepResolve(Vector3 origin, float yaw, float radius, float halfAngle)
        {
            var mesh = FxMeshes.Sector(0.8f, radius, halfAngle);
            Play(mesh, true, alphaMat, origin + Vector3.up * 0.08f, yaw, 0.42f, (b, u) =>
            {
                b.mpb.SetFloat(PProgress, Mathf.Lerp(-0.1f, 1.15f, Ease(Mathf.Clamp01(u / 0.4f))));
                b.mpb.SetFloat(PWidth, 0.1f); b.mpb.SetFloat(PTail, 0.35f);
                b.mpb.SetFloat(PDissolve, Mathf.Clamp01((u - 0.3f) / 0.7f));
            }, 0, Ink, Coral, Amber);
            var rot = Quaternion.Euler(0, yaw, 0);
            for (int i = 0; i < 9; i++)
            {
                float a = Mathf.Lerp(halfAngle, -halfAngle, i / 8f) * Mathf.Deg2Rad;
                var d = rot * new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                Sparks(origin + d * radius * 0.7f + Vector3.up * 0.4f, d + Vector3.up * 0.6f, 6, 0.5f, 7, 0.18f, 0.5f);
            }
            CameraShake.Shake(0.18f, 0.25f);
        }

        // Hollow Toll: a shock front runs out through the annulus with a short rising wall, dust kicks up at the rim.
        public void TollResolve(Vector3 centre, float inner, float outer)
        {
            var c = new Vector3(centre.x, 0.06f, centre.z);
            Play(FxMeshes.Annulus(inner, outer), true, alphaMat, c, 0, 0.5f, (b, u) =>
            {
                b.mpb.SetFloat(PProgress, Mathf.Lerp(0f, 1.1f, Ease(Mathf.Clamp01(u / 0.4f))));
                b.mpb.SetFloat(PWidth, 0.12f); b.mpb.SetFloat(PTail, 0.4f);
                b.mpb.SetFloat(PDissolve, Mathf.Clamp01((u - 0.35f) / 0.65f));
            }, 0, Ink, Coral, Amber);
            Play(cylinder, false, addMat, c, 0, 0.45f, (b, u) =>
            {
                float r = Mathf.Lerp(inner, outer, Ease(Mathf.Clamp01(u / 0.45f)));
                b.go.transform.localScale = new Vector3(r, Mathf.Lerp(1.1f, 0.3f, u), r);
                b.mpb.SetFloat(PDissolve, u * 0.9f); b.mpb.SetFloat(PIntensity, 0.45f);
            }, 2, Ink, Coral, Amber);
            Dust(c, outer, 36, 1.2f);
            CameraShake.Shake(0.28f, 0.35f);
        }

        // Pendulum Charge: a flat streak follows the boss down the lane; the landing stomps a ring and dust.
        public void ChargeTrail(Transform boss, bool on)
        {
            if (trail == null)
            {
                var go = new GameObject("Charge trail"); go.transform.SetParent(transform, false);
                trail = go.AddComponent<TrailRenderer>();
                trail.alignment = LineAlignment.TransformZ; trail.textureMode = LineTextureMode.Stretch;
                trail.time = 0.35f; trail.minVertexDistance = 0.2f; trail.widthMultiplier = 2.2f;
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var m = new Material(addMat); m.SetFloat(PMode, 0); m.SetColor(PLow, Ink); m.SetColor(PHigh, Coral);
                m.SetFloat(PProgress, 0); m.SetFloat(PWidth, 0.05f); m.SetFloat(PTail, 0.5f); m.SetFloat(PDissolve, 0);
                trail.sharedMaterial = m;
            }
            trail.transform.SetParent(on ? boss : transform, true);
            trail.transform.localPosition = on ? new Vector3(0, 0.08f, 0) : trail.transform.localPosition;
            trail.transform.rotation = Quaternion.Euler(90, 0, 0);   // lie flat: TransformZ alignment faces the trail up
            if (on) trail.Clear();
            trail.emitting = on;
        }
        public void ChargeLand(Vector3 pos)
        {
            var c = new Vector3(pos.x, 0.06f, pos.z);
            Play(FxMeshes.Annulus(0.6f, 3.2f), true, alphaMat, c, 0, 0.4f, (b, u) =>
            {
                b.mpb.SetFloat(PProgress, Mathf.Lerp(0f, 1.1f, Ease(Mathf.Clamp01(u / 0.35f))));
                b.mpb.SetFloat(PWidth, 0.15f); b.mpb.SetFloat(PTail, 0.3f); b.mpb.SetFloat(PDissolve, Mathf.Clamp01((u - 0.3f) / 0.7f));
            }, 0, Ink, Coral, Amber);
            Dust(c, 1.6f, 28, 0.8f);
            Sparks(c + Vector3.up * 0.3f, Vector3.up, 20, 1.2f, 5, 0.15f, 0.45f);
            CameraShake.Shake(0.3f, 0.3f);
        }

        // Shattered Chime: a column of resonant light drops into the marker and shatters outward.
        public void ChimeResolve(Vector3 pos, float radius)
        {
            var c = new Vector3(pos.x, 0.06f, pos.z);
            Play(cylinder, false, addMat, c, 0, 0.35f, (b, u) =>
            {
                float r = radius * Mathf.Lerp(0.35f, 0.15f, u);
                b.go.transform.localScale = new Vector3(r, Mathf.Lerp(7f, 4f, u), r);
                b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 0.9f);
            }, 2, Ink, Gold, Amber);
            Play(FxMeshes.Annulus(0.1f, radius), true, alphaMat, c, 0, 0.35f, (b, u) =>
            {
                b.mpb.SetFloat(PProgress, Mathf.Lerp(0f, 1.1f, Ease(Mathf.Clamp01(u / 0.3f))));
                b.mpb.SetFloat(PWidth, 0.15f); b.mpb.SetFloat(PTail, 0.5f); b.mpb.SetFloat(PDissolve, Mathf.Clamp01((u - 0.25f) / 0.75f));
            }, 0, Ink, Coral, Amber);
            Sparks(c + Vector3.up * 0.5f, Vector3.up * 0.8f, 26, 1.0f, 6, 0.16f, 0.5f);
            CameraShake.Shake(0.12f, 0.18f);
        }

        // A hit on the boss (impact table entry: melee on brass): gold sparks thrown back toward the attacker and a flash.
        public void BossHit(Vector3 point)
        {
            sparks.Emit(point, Vector3.zero, 1.1f, 0.1f);
            var back = Camera.main ? -Camera.main.transform.forward : Vector3.up;
            Sparks(point, (back + Vector3.up * 0.5f).normalized, 12, 0.8f, 6, 0.12f, 0.35f);
        }

        // The player is hit by a boss attack: a coral burst at their chest and a shake.
        public void PlayerHit(Vector3 feet)
        {
            var c = feet + Vector3.up * 1.0f;
            Play(cylinder, false, addMat, new Vector3(feet.x, 0.06f, feet.z), 0, 0.25f, (b, u) =>
            {
                float rr = Mathf.Lerp(0.4f, 1.3f, Ease(u)); b.go.transform.localScale = new Vector3(rr, 1.6f * (1 - u), rr);
                b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 0.8f);
            }, 2, Ink, Coral, Amber);
            Sparks(c, Vector3.up, 16, 1.2f, 5, 0.12f, 0.35f);
            CameraShake.Shake(0.25f, 0.25f);
        }

        // Potion: rising teal-gold rings around the player and motes drifting up (heal language: pulses, never a filled disc).
        public void PotionHeal(Transform who)
        {
            var p = who.position;
            for (int k = 0; k < 3; k++)
            {
                float delay = k * 0.12f;
                Play(cylinder, false, addMat, new Vector3(p.x, 0.05f, p.z), 0, 0.7f + delay, (b, u) =>
                {
                    float tt = Mathf.Clamp01((u * (0.7f + delay) - delay) / 0.7f);
                    b.go.transform.position = new Vector3(who.position.x, 0.05f + 1.8f * Ease(tt), who.position.z);
                    b.go.transform.localScale = new Vector3(0.75f - 0.2f * tt, 0.25f, 0.75f - 0.2f * tt);
                    b.mpb.SetFloat(PDissolve, tt <= 0 ? 1 : tt * 0.9f); b.mpb.SetFloat(PIntensity, 0.9f);
                }, 2, Brass, Teal, Ivory);
            }
            for (int i = 0; i < 18; i++)
            {
                var o = UnityEngine.Random.insideUnitCircle * 0.7f;
                motes.Emit(p + new Vector3(o.x, UnityEngine.Random.Range(0.1f, 1.2f), o.y), Vector3.up * UnityEngine.Random.Range(1.2f, 2.4f), UnityEngine.Random.Range(0.12f, 0.22f), UnityEngine.Random.Range(0.6f, 1.0f));
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            UpdateTelegraphs(dt); UpdateBursts(dt);
        }
    }

    // Minimal camera shake with the Cinemachine Impulse shape of API (§4) until the camera rig uses Cinemachine.
    // Applied after every other LateUpdate and removed once the camera has rendered, so follow cameras never see it.
    [DefaultExecutionOrder(10000)]
    public sealed class CameraShake : MonoBehaviour
    {
        static CameraShake inst;
        float amp, time, dur; Vector3 applied; Camera cam;
        public static void Ensure() { if (inst == null) inst = new GameObject("Camera shake").AddComponent<CameraShake>(); }
        public static void Shake(float amplitude, float duration)
        {
            if (BellkeeperFx.NoScreenshake) return; Ensure();
            if (amplitude >= inst.amp * (1 - inst.time / Mathf.Max(0.01f, inst.dur))) { inst.amp = amplitude; inst.dur = duration; inst.time = 0; }
        }
        void OnEnable() => UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += Restore;
        void OnDisable() => UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= Restore;
        void Restore(UnityEngine.Rendering.ScriptableRenderContext ctx, Camera c)
        {
            if (c != cam || applied == Vector3.zero) return;
            c.transform.position -= applied; applied = Vector3.zero;
        }
        void LateUpdate()
        {
            cam = Camera.main; if (!cam) return;
            if (time < dur)
            {
                time += Time.deltaTime; float k = amp * (1 - time / dur);
                applied = (cam.transform.right * (Mathf.PerlinNoise(time * 40, 0) - 0.5f) + cam.transform.up * (Mathf.PerlinNoise(0, time * 40) - 0.5f)) * 2 * k;
                cam.transform.position += applied;
            }
        }
    }
}
