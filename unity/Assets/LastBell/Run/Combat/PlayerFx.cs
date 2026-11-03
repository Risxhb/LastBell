using System;
using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // Player ability effects (docs/design/spell-vfx-pipeline.md). Kits call these on their authoritative ticks; nothing
    // here decides hits or timing. Rules kept from the guidelines:
    //  - floor-level player effects render below boss telegraphs (queue 3040 < telegraph 3050), so danger stays visible;
    //  - airborne arcs are short additive bands that clear within ~0.3 s;
    //  - state that matters (bell strength, echoes) also reads by shape/count, not colour alone.
    public sealed partial class PlayerFx : MonoBehaviour
    {
        public static readonly Color TollCyan = new Color(0.45f, 1.7f, 2.1f), SteelGold = new Color(2.0f, 1.45f, 0.6f),
            NoteGold = new Color(2.2f, 1.6f, 0.65f), Ivory = new Color(1.9f, 1.85f, 1.6f), Heal = new Color(0.6f, 1.9f, 1.1f),
            Ink = new Color(0.06f, 0.09f, 0.15f), Navy = new Color(0.12f, 0.18f, 0.42f), Violet = new Color(1.1f, 0.7f, 2.0f);
        static readonly int PMode = Shader.PropertyToID("_Mode"), PLow = Shader.PropertyToID("_ColorLow"), PHigh = Shader.PropertyToID("_ColorHigh"),
            PEdge = Shader.PropertyToID("_ColorEdge"), PProgress = Shader.PropertyToID("_Progress"), PWidth = Shader.PropertyToID("_Width"),
            PTail = Shader.PropertyToID("_Tail"), PDissolve = Shader.PropertyToID("_Dissolve"), PIntensity = Shader.PropertyToID("_Intensity"),
            PSrc = Shader.PropertyToID("_SrcBlend"), PDst = Shader.PropertyToID("_DstBlend");
        const int FloorQueue = 3040;

        Material add, addFloor, alphaFloor; Mesh cyl, disk;
        FxParticles sparks, motes;
        sealed class Burst { public GameObject go; public MeshFilter mf; public MeshRenderer mr; public MaterialPropertyBlock mpb = new MaterialPropertyBlock(); public bool owns; public float t, life; public Action<Burst, float> anim; public Transform follow; public Vector3 offset; public int id; }
        static int burstIds;
        readonly List<Burst> live = new List<Burst>(); readonly Stack<Burst> pool = new Stack<Burst>();

        // persistent indicators
        Transform bell; Transform[] echoPips = new Transform[2]; Transform strengthRing; Renderer bellBody;
        readonly Dictionary<PlayerController, TrailRenderer> trails = new Dictionary<PlayerController, TrailRenderer>();

        public void ResetAll()
        { if (LastBell.Run.Networking.LanFx.Record != null) LastBell.Run.Networking.LanFx.Record(LastBell.Run.Networking.LanFxTarget.Player, "ResetAll", new object[0]);
            for (int i = live.Count - 1; i >= 0; i--) { var b = live[i]; if (b.owns) Destroy(b.mf.sharedMesh); b.go.SetActive(false); pool.Push(b); }
            live.Clear();
            trails.Clear();
            if (bell) bell.gameObject.SetActive(false);
            ResetArcanist();
            sparks.Clear(); motes.Clear();
        }

        public static PlayerFx Create(Transform root)
        {
            var go = new GameObject("Player FX"); go.transform.SetParent(root, false);
            var f = go.AddComponent<PlayerFx>(); f.Init(); return f;
        }

        static Shader DissolveShader()
        {
            var s = Shader.Find("LastBell/FX/ColorDissolve");
            if (s == null) { var m = Resources.Load<Material>("Run/FX_ColorDissolve"); if (m) s = m.shader; }
            return s;
        }

        void Init()
        {
            var sh = DissolveShader();
            add = new Material(sh);
            addFloor = new Material(sh) { renderQueue = FloorQueue };
            alphaFloor = new Material(sh) { renderQueue = FloorQueue }; alphaFloor.SetFloat(PSrc, 1); alphaFloor.SetFloat(PDst, 10);
            cyl = FxMeshes.Cylinder(); disk = FxMeshes.Sector(0.05f, 1f, 180f, 40, true, "FX_MS_Disk");
            var sm = new Material(add); sm.SetFloat(PMode, 1); sm.SetColor(PLow, SteelGold); sm.SetColor(PHigh, Ivory); sm.SetFloat(PDissolve, 0);
            sparks = FxParticles.Create(transform, "Player sparks", sm, 500, 6f, 1.5f, 0.05f);
            var mm = new Material(add); mm.SetFloat(PMode, 1); mm.SetColor(PLow, NoteGold); mm.SetColor(PHigh, Ivory); mm.SetFloat(PDissolve, 0);
            motes = FxParticles.Create(transform, "Player motes", mm, 700, 0f, 2.2f, 0f);
            for (int i = 0; i < 16; i++) pool.Push(NewBurst());
        }

        Burst NewBurst()
        {
            var b = new Burst { go = new GameObject("FX burst") }; b.go.transform.SetParent(transform, false);
            b.mf = b.go.AddComponent<MeshFilter>(); b.mr = b.go.AddComponent<MeshRenderer>();
            b.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; b.mr.receiveShadows = false; b.go.SetActive(false); return b;
        }
        Burst Play(Mesh mesh, bool owns, Material mat, Vector3 pos, Quaternion rot, float life, Action<Burst, float> anim, float mode, Color low, Color high, Color edge, Transform follow = null)
        {
            var b = pool.Count > 0 ? pool.Pop() : NewBurst();
            b.mf.sharedMesh = mesh; b.owns = owns; b.mr.sharedMaterial = mat; b.follow = follow;
            if (follow) b.offset = pos - follow.position;
            b.go.transform.SetPositionAndRotation(pos, rot); b.go.transform.localScale = Vector3.one;
            b.mpb.Clear(); b.mpb.SetFloat(PMode, mode); b.mpb.SetColor(PLow, low); b.mpb.SetColor(PHigh, high); b.mpb.SetColor(PEdge, edge);
            b.mpb.SetFloat(PIntensity, 1); b.mpb.SetFloat(PWidth, 0.1f); b.mpb.SetFloat(PTail, 0.4f); b.mpb.SetFloat(PDissolve, 0); b.mpb.SetFloat(PProgress, 0.5f);
            b.id = ++burstIds; b.t = 0; b.life = life; b.anim = anim; b.go.SetActive(true); anim(b, 0); b.mr.SetPropertyBlock(b.mpb); live.Add(b); return b;
        }
        void Kill(Burst b, int id) { if (b == null || b.id != id) return; b.life = 0; }
        static float Ease(float u) => 1 - (1 - u) * (1 - u);

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var b = live[i]; b.t += dt;
                if (b.t >= b.life)
                {
                    if (b.owns) Destroy(b.mf.sharedMesh);
                    b.go.SetActive(false); live.RemoveAt(i); pool.Push(b); continue;
                }
                if (b.follow) b.go.transform.position = b.follow.position + b.offset;
                b.anim(b, b.life >= 9000 ? b.t : b.t / b.life); b.mr.SetPropertyBlock(b.mpb);
            }
        }

        void Spray(Vector3 pos, Vector3 dir, int n, float spread, float speed, float size, float life, FxParticles into = null)
        {
            into ??= sparks;
            for (int i = 0; i < n; i++)
            {
                var d = (dir + UnityEngine.Random.insideUnitSphere * spread).normalized;
                into.Emit(pos + UnityEngine.Random.insideUnitSphere * 0.15f, d * speed * UnityEngine.Random.Range(0.5f, 1.2f), size * UnityEngine.Random.Range(0.6f, 1.3f), life * UnityEngine.Random.Range(0.6f, 1.2f));
            }
        }

        // ------------------------------------------------------------ shared
        public void DashTrail(PlayerController p)
        { if (LastBell.Run.Networking.LanFx.Record != null) LastBell.Run.Networking.LanFx.Record(LastBell.Run.Networking.LanFxTarget.Player, "DashTrail", new object[] { p });
            if (p.ClassName == "Arcanist") return;   // Blink draws its own departure, thread and arrival (PlayerFx.Arcanist)
            if (!trails.TryGetValue(p, out var tr) || tr == null)
            {
                var go = new GameObject("Dash trail"); go.transform.SetParent(p.transform, false); go.transform.localPosition = new Vector3(0, 0.9f, 0);
                tr = go.AddComponent<TrailRenderer>(); tr.time = 0.22f; tr.minVertexDistance = 0.1f; tr.widthMultiplier = 0.9f;
                tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var m = new Material(add); m.SetFloat(PMode, 0); m.SetColor(PLow, Ink);
                m.SetColor(PHigh, p.ClassName == "Chimesinger" ? NoteGold : p.ClassName == "Arcanist" ? TimeTeal : TollCyan); m.SetFloat(PProgress, 0); m.SetFloat(PWidth, 0.05f); m.SetFloat(PTail, 0.6f); m.SetFloat(PDissolve, 0);
                tr.sharedMaterial = m; trails[p] = tr;
            }
            tr.Clear(); tr.emitting = true;
            StartCoroutine(StopTrail(tr));
        }
        System.Collections.IEnumerator StopTrail(TrailRenderer tr) { yield return new WaitForSeconds(0.22f); if (tr) tr.emitting = false; }

        // ------------------------------------------------------------ Chimesinger
        // Resonant Note: a small gold note with a short glittering tail (projectile drawn on the combat tick)
        public void NoteCast(Vector3 at, Vector3 dir) { if (LastBell.Run.Networking.LanFx.Record != null) LastBell.Run.Networking.LanFx.Record(LastBell.Run.Networking.LanFxTarget.Player, "NoteCast", new object[] { at, dir }); Spray(at, dir, 6, 0.4f, 3, 0.12f, 0.25f, motes); }
        public void ProjectileHead(Vector3 pos, Color c)
        { if (LastBell.Run.Networking.LanFx.Record != null) LastBell.Run.Networking.LanFx.Record(LastBell.Run.Networking.LanFxTarget.Player, "ProjectileHead", new object[] { pos, c });
            motes.Emit(pos, Vector3.zero, 0.42f, 0.06f);
            motes.Emit(pos + UnityEngine.Random.insideUnitSphere * 0.08f, UnityEngine.Random.insideUnitSphere * 0.3f, 0.16f, 0.3f);
        }
        public void NoteImpact(Vector3 pos, Color c) { if (LastBell.Run.Networking.LanFx.Record != null) LastBell.Run.Networking.LanFx.Record(LastBell.Run.Networking.LanFxTarget.Player, "NoteImpact", new object[] { pos, c }); Spray(pos, Vector3.up, 8, 1f, 3, 0.12f, 0.3f, motes); }
        public void NoteTrail(PlayerController p)
        { if (LastBell.Run.Networking.LanFx.Record != null) LastBell.Run.Networking.LanFx.Record(LastBell.Run.Networking.LanFxTarget.Player, "NoteTrail", new object[] { p });
            // Harmonic Step: fading golden notes along the path (the trail renderer is shared with every dash)
            for (int i = 0; i < 10; i++) motes.Emit(p.transform.position + UnityEngine.Random.insideUnitSphere * 0.4f + Vector3.up * 1f, Vector3.up * 0.5f, 0.2f, 0.7f);
        }

        // Bellstrike: a bright straight bolt to the target; a new bell drops in with one ring, a refresh rings twice
        // and tightens the strength ring.
        public void Bellstrike(Vector3 from, Vector3 to, bool hit, bool refresh)
        { if (LastBell.Run.Networking.LanFx.Record != null) LastBell.Run.Networking.LanFx.Record(LastBell.Run.Networking.LanFxTarget.Player, "Bellstrike", new object[] { from, to, hit, refresh });
            var d = Sim.Flat(to - from); float len = d.magnitude; if (len < 0.1f) return;
            var lane = FxMeshes.Quad(-0.12f, 0.12f, 0, len);
            Play(lane, true, add, from, Quaternion.LookRotation(d), 0.16f, (b, u) => { b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 1.4f); }, 2, Ink, NoteGold, Ivory);
            if (!hit) return;
            int rings = refresh ? 2 : 1;
            for (int k = 0; k < rings; k++)
            {
                float delay = k * 0.12f;
                Play(FxMeshes.Annulus(0.8f, 1f), true, add, to, Quaternion.identity, 0.4f + delay, (b, u) =>
                {
                    float tt = Mathf.Clamp01((u * (0.4f + delay) - delay) / 0.4f); float r = Mathf.Lerp(0.6f, 2.2f, Ease(tt));
                    b.go.transform.localScale = new Vector3(r, 1, r); b.mpb.SetFloat(PDissolve, tt <= 0 ? 1 : tt); b.mpb.SetFloat(PIntensity, 1.2f);
                }, 2, Ink, NoteGold, Ivory);
            }
            Spray(to, Vector3.up, refresh ? 22 : 12, 1f, 4, 0.14f, 0.4f, motes);
        }

        // The ringing bell above the boss. Size/ring encode strength (0-2), orbiting pips encode echoes (0-2),
        // the bell's glow dims as it nears expiry.
        public void Bell(BossEncounter boss, int strength, int echoes, float life01)
        { if (LastBell.Run.Networking.LanFx.Record != null) LastBell.Run.Networking.LanFx.Record(LastBell.Run.Networking.LanFxTarget.Player, "Bell", new object[] { boss, strength, echoes, life01 });
            if (boss == null || !boss.Solid)
            {
                if (bell) bell.gameObject.SetActive(false); return;
            }
            if (bell == null)
            {
                bell = new GameObject("Ringing bell").transform; bell.SetParent(transform, false);
                var body = Prim.Make(PrimitiveKind.Cone, bell, Vector3.zero, new Vector3(0.9f, 0.9f, 0.9f), new Color(0.95f, 0.75f, 0.3f), 0.5f);
                bellBody = body.GetComponent<Renderer>();
                Prim.Make(PrimitiveKind.Sphere, bell, new Vector3(0, -0.5f, 0), Vector3.one * 0.25f, new Color(1f, 0.85f, 0.4f), 0.8f);
                strengthRing = Prim.Make(PrimitiveKind.Cylinder, bell, new Vector3(0, -0.3f, 0), new Vector3(1.5f, 0.03f, 1.5f), new Color(0.4f, 0.9f, 1f), 1.2f);
                for (int i = 0; i < 2; i++) echoPips[i] = Prim.Make(PrimitiveKind.Sphere, bell, Vector3.zero, Vector3.one * 0.32f, new Color(0.5f, 1f, 1f), 1.4f);
            }
            bell.gameObject.SetActive(true);
            float s = 1 + 0.3f * strength;
            var cam = Camera.main; var toCam = cam ? Sim.Flat(-cam.transform.forward).normalized : Vector3.back;   // in front of the target, never off the top of the screen
            bell.position = boss.transform.position + toCam * (boss.BodyRadius + 0.5f) + Vector3.up * (boss.HitHeight + 1.0f + 0.15f * Mathf.Sin(Time.time * 3));
            bell.localScale = Vector3.one * s;
            bell.rotation = Quaternion.Euler(Mathf.Sin(Time.time * 7) * 10, 0, 0);
            strengthRing.gameObject.SetActive(strength > 0);
            strengthRing.localScale = new Vector3(1.2f + 0.4f * strength, 0.03f, 1.2f + 0.4f * strength);
            for (int i = 0; i < 2; i++)
            {
                echoPips[i].gameObject.SetActive(i < echoes);
                float a = Time.time * 2.5f + i * Mathf.PI;
                echoPips[i].localPosition = new Vector3(Mathf.Cos(a) * 1.1f, -0.2f, Mathf.Sin(a) * 1.1f);
            }
            Prim.SetGlow(bellBody, new Color(0.95f, 0.75f, 0.3f) * (0.15f + 0.6f * life01));
        }
        public void EchoAdded(BossEncounter boss, int echoes)
        { if (LastBell.Run.Networking.LanFx.Record != null) LastBell.Run.Networking.LanFx.Record(LastBell.Run.Networking.LanFxTarget.Player, "EchoAdded", new object[] { boss, echoes });
            if (boss == null) return;
            var at = boss.transform.position + Vector3.up * boss.MarkHeight;
            Spray(at, Vector3.up, 10 * echoes, 0.9f, 3, 0.18f, 0.5f, motes);
        }

        // Harmonic Refrain: a directional wave of three expanding gold arcs (ripples), clearly different from the bolt
        public void Refrain(PlayerController p, float range, float half, bool hit)
        { if (LastBell.Run.Networking.LanFx.Record != null) LastBell.Run.Networking.LanFx.Record(LastBell.Run.Networking.LanFxTarget.Player, "Refrain", new object[] { p, range, half, hit });
            float yaw = p.transform.eulerAngles.y;
            for (int k = 0; k < 3; k++)
            {
                float delay = k * 0.07f;
                Play(FxMeshes.Sector(range * 0.15f, range, half, 32), true, add, p.transform.position + Vector3.up * 0.9f, Quaternion.Euler(0, yaw, 0), 0.35f + delay, (b, u) =>
                {
                    float tt = Mathf.Clamp01((u * (0.35f + delay) - delay) / 0.35f);
                    b.mpb.SetFloat(PProgress, Mathf.Lerp(-0.1f, 1.15f, tt)); b.mpb.SetFloat(PWidth, 0.06f); b.mpb.SetFloat(PTail, 0.18f);
                    b.mpb.SetFloat(PDissolve, tt <= 0 ? 1 : Mathf.Clamp01((tt - 0.5f) / 0.5f)); b.mpb.SetFloat(PIntensity, 0.9f);
                }, 0, Ink, NoteGold, Ivory);
            }
        }

        // Crescendo: the bell collapses into a column and bursts; a full setup adds a second shock ring and more light
        public void Crescendo(Vector3 at, bool bellDetonated, int echoes, bool full)
        { if (LastBell.Run.Networking.LanFx.Record != null) LastBell.Run.Networking.LanFx.Record(LastBell.Run.Networking.LanFxTarget.Player, "Crescendo", new object[] { at, bellDetonated, echoes, full });
            var c = new Vector3(at.x, 0.05f, at.z);
            Play(cyl, false, add, c, Quaternion.identity, full ? 0.55f : 0.4f, (b, u) =>
            {
                float r = Mathf.Lerp(0.4f, bellDetonated ? 2.4f : 1.2f, Ease(u)); b.go.transform.localScale = new Vector3(r, Mathf.Lerp(5f, 1f, u), r);
                b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, full ? 1.6f : 1f);
            }, 2, Ink, NoteGold, Ivory);
            int rings = full ? 2 : 1;
            for (int k = 0; k < rings; k++)
            {
                float delay = k * 0.15f;
                Play(FxMeshes.Annulus(0.2f, 1f), true, alphaFloor, c, Quaternion.identity, 0.5f + delay, (b, u) =>
                {
                    float tt = Mathf.Clamp01((u * (0.5f + delay) - delay) / 0.5f); float r = Mathf.Lerp(0.5f, full ? 4.5f : 3f, Ease(tt));
                    b.go.transform.localScale = new Vector3(r, 1, r); b.mpb.SetFloat(PProgress, 1.1f * tt); b.mpb.SetFloat(PDissolve, tt <= 0 ? 1 : Mathf.Clamp01((tt - 0.4f) / 0.6f));
                }, 0, Ink, NoteGold, Ivory);
            }
            Spray(c + Vector3.up * 1.5f, Vector3.up, 18 + 14 * echoes + (full ? 20 : 0), 1.2f, 7, 0.18f, 0.6f, motes);
            if (full) CameraShake.Shake(0.3f, 0.3f);
        }

        // Mending Chorus: one immediate expanding ring of the heal radius + rising notes on everyone healed. No lingering
        // disc: the ring has cleared within 0.5 s (no sanctuary / shield implied).
        public void Chorus(Vector3 at, float radius)
        { if (LastBell.Run.Networking.LanFx.Record != null) LastBell.Run.Networking.LanFx.Record(LastBell.Run.Networking.LanFxTarget.Player, "Chorus", new object[] { at, radius });
            var c = new Vector3(at.x, 0.05f, at.z);
            Play(FxMeshes.Annulus(0.85f, 1f), true, addFloor, c, Quaternion.identity, 0.45f, (b, u) =>
            {
                float r = Mathf.Lerp(0.5f, radius, Ease(u)); b.go.transform.localScale = new Vector3(r, 1, r);
                b.mpb.SetFloat(PDissolve, Mathf.Clamp01((u - 0.5f) / 0.5f)); b.mpb.SetFloat(PIntensity, 1.1f);
            }, 2, Ink, Heal, Ivory);
            for (int i = 0; i < 24; i++)
            {
                var o = UnityEngine.Random.insideUnitCircle * 0.8f;
                motes.Emit(at + new Vector3(o.x, UnityEngine.Random.Range(0.2f, 1.4f), o.y), Vector3.up * UnityEngine.Random.Range(1.2f, 2.6f), 0.2f, 0.8f);
            }
        }
    }
}
