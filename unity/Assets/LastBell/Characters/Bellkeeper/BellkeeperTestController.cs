using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

namespace LastBell.Characters
{
    // Test fight for the Bellkeeper boss (Resources/Characters/Bellkeeper.glb, built by
    // prototype/arena-3d/models/build-bellkeeper.ps1). Not the real encounter: there are no phases yet.
    // It runs the Phase 1 performance from the boss design (reference/LAST-BELL-BELLKEEPER-BOSS-DESIGN-v1.0.md §3-4):
    // Bell Sweep -> Hollow Toll -> Pendulum Charge -> Shattered Chime -> recovery, looping, at the design's timings on a
    // 60 Hz tick, walking into range before the sweep and the toll. Each attack hits the player (PlayerVitals) if they're
    // inside its locked geometry on the resolve tick, for the design's share of max HP. The boss has 1800 HP (the
    // browser prototype's standard). The timeline only raises events; BellkeeperFx draws them.
    // Targets the Vanguard test character when it's in the scene.
    // Spawns itself on the clock dial when a scene with the key-art arena enters Play mode.
    // Keys: B pauses / resumes the attacks (idle when paused), R resets the fight.
    public sealed class BellkeeperTestController : MonoBehaviour
    {
        const float GlowBoost = 5f;   // glTFast drops KHR_materials_emissive_strength (3 in the GLB); a bit more reads better
        const float MetalScale = 0.55f;
        const int Hz = 60;
        const float Fade = 0.12f, ArenaRadius = 9.5f, TurnRate = 240f;

        // design §3 starting targets, in metres and ticks
        const float SweepRadius = 8f, SweepHalfAngle = 40f, TollInner = 2.6f, TollOuter = 6.8f;
        const float LaneWidth = 2.2f, ChargeOvershoot = 2.75f, ChimeRadius = 1.4f;
        const int SweepWarn = 57, SweepTrack = 18, SweepEnd = 105;
        const int TollWarn = 66, TollEnd = 120;
        const int ChargeWarn = 48, ChargeTravel = 18, ChargeEnd = 108;
        const int ChimeWarn = 72, ChimeTrack = 30, ChimeGap = 24, ChimeCount = 3, ChimeEnd = 156;
        const int RecoverEnd = 54;
        public const int MaxHp = 1800;
        public const float BodyRadius = 1.4f;
        const int SweepDamage = 50, TollDamage = 45, ChargeDamage = 50, ChimeDamage = 25;   // % of player max HP (§3)
        const float HitMargin = 0.3f;                     // player body radius: touching the border counts
        const float WalkSpeed = 1.75f, WalkTurn = 120f;   // matches the Walk clip's stride (1.4 m per 0.8 s step)
        const int WalkMax = 180;                          // give up approaching after 3 s

        enum Attack { Sweep, Toll, Charge, Chime, Recover, Walk }
        static readonly Attack[] PhaseOne = { Attack.Sweep, Attack.Toll, Attack.Charge, Attack.Chime, Attack.Recover };

        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClip[] clips;
        readonly Dictionary<string, int> index = new Dictionary<string, int>();
        float[] times, weights;
        int current = -1;

        BellkeeperFx fx;
        bool running = true;
        float acc;
        int step, tick;
        Attack attack;
        BellkeeperFx.Telegraph tele;
        readonly BellkeeperFx.Telegraph[] chimes = new BellkeeperFx.Telegraph[ChimeCount];
        readonly Vector3[] chimePos = new Vector3[ChimeCount];
        Vector3 laneStart, laneEnd;
        float laneYaw, laneLength, orbit;
        Attack pending;

        public int Hp { get; private set; } = MaxHp;
        public bool Dead { get; private set; }
        PlayerVitals player;
        EncounterHud hud;

        readonly List<(Material m, Color baseEmission)> glow = new List<(Material, Color)>();
        float glowLevel = 1, glowTarget = 1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn()
        {
            if (FindAnyObjectByType<LastBell.Arenas.KeyArtArena>() == null || FindAnyObjectByType<BellkeeperTestController>() != null) return;
            var prefab = Resources.Load<GameObject>("Characters/Bellkeeper");
            if (prefab == null) { Debug.LogWarning("Bellkeeper test: Resources/Characters/Bellkeeper.glb not found"); return; }
            var go = Instantiate(prefab, Vector3.zero, Quaternion.identity);
            go.name = "Bellkeeper (test)";
            go.AddComponent<BellkeeperTestController>().Init(Resources.LoadAll<AnimationClip>("Characters/Bellkeeper"));
        }

        void Init(AnimationClip[] loaded)
        {
            foreach (var r in GetComponentsInChildren<Renderer>())
                foreach (var m in r.materials)
                {
                    if (m.HasProperty("emissiveFactor"))
                    {
                        m.SetColor("emissiveFactor", m.GetColor("emissiveFactor") * GlowBoost);
                        glow.Add((m, m.GetColor("emissiveFactor")));
                    }
                    // Meshy bakes the brass almost fully metallic, so it mirrors the arena's violet sky; keep it warm
                    if (m.HasProperty("metallicFactor")) m.SetFloat("metallicFactor", m.GetFloat("metallicFactor") * MetalScale);
                }
            clips = loaded;
            var animator = GetComponentInChildren<Animator>();
            if (animator == null) animator = gameObject.AddComponent<Animator>();
            animator.applyRootMotion = false;
            graph = PlayableGraph.Create("Bellkeeper");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, clips.Length);
            for (int i = 0; i < clips.Length; i++)
            {
                index[clips[i].name] = i;
                var p = AnimationClipPlayable.Create(graph, clips[i]);
                p.SetApplyFootIK(false);
                graph.Connect(p, 0, mixer, i);
            }
            AnimationPlayableOutput.Create(graph, "Bellkeeper", animator).SetSourcePlayable(mixer);
            times = new float[clips.Length]; weights = new float[clips.Length];
            graph.Play();
            Play("Idle");
            fx = BellkeeperFx.Create();
            hud = new GameObject("Encounter HUD").AddComponent<EncounterHud>(); hud.boss = this;
            Begin(PhaseOne[0]);
            Debug.Log($"Bellkeeper test: {clips.Length} clips ({string.Join(", ", index.Keys)}), B pauses the attacks");
        }

        void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }

        // ---------------- animation ----------------
        void Play(string name, bool restart = true)
        {
            if (!index.TryGetValue(name, out int i)) return;
            if (i == current && !restart) return;
            current = i; times[i] = 0;
        }
        static bool Loops(string name) => name == "Idle" || name == "Vulnerable";
        void Animate(float dt)
        {
            for (int i = 0; i < clips.Length; i++)
            {
                weights[i] = Mathf.MoveTowards(weights[i], i == current ? 1 : 0, dt / Fade);
                if (weights[i] <= 0) continue;
                float len = clips[i].length;
                times[i] += dt;
                mixer.GetInput(i).SetTime(Loops(clips[i].name) ? Mathf.Repeat(times[i], len) : Mathf.Min(times[i], len));
            }
            float sum = 0; foreach (var w in weights) sum += w;
            for (int i = 0; i < clips.Length; i++) mixer.SetInputWeight(i, sum > 0 ? weights[i] / sum : 0);
            graph.Evaluate(0);
        }

        // ---------------- timeline (one Step per 60 Hz tick) ----------------
        void FindPlayer()
        {
            if (player != null) return;
            var v = FindAnyObjectByType<VanguardTestController>(); if (!v) return;
            player = v.GetComponent<PlayerVitals>();
            if (player == null) player = v.gameObject.AddComponent<PlayerVitals>();
            player.Bind(this, fx); hud.player = player;
        }
        Vector3 Target()
        {
            if (player) return player.transform.position;
            // nobody to fight: walk an imaginary target around the dial so facings and lanes vary
            orbit += 47f;
            return Quaternion.Euler(0, orbit, 0) * new Vector3(0, 0, 6.5f);
        }
        float YawTo(Vector3 p) { var d = p - transform.position; d.y = 0; return d.sqrMagnitude < 0.01f ? transform.eulerAngles.y : Quaternion.LookRotation(d).eulerAngles.y; }
        void TurnTowards(float yaw, float maxDeg) =>
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.Euler(0, yaw, 0), maxDeg);

        // walk into range first when the attack needs it (the sweep's 8 m cone, the toll's 2.6-6.8 m ring)
        void Begin(Attack a)
        {
            float want = Approach(a);
            if (want > 0 && Flat(Target() - transform.position).magnitude > want) { attack = Attack.Walk; pending = a; tick = 0; tele = null; Play("Walk"); return; }
            StartAttack(a);
        }
        static float Approach(Attack a) => a == Attack.Sweep ? 5.5f : a == Attack.Toll ? 4.6f : -1;
        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        void StartAttack(Attack a)
        {
            attack = a; tick = 0; tele = null;
            switch (a)
            {
                case Attack.Sweep:
                    Play("BellSweep");
                    tele = fx.Cone(SweepRadius, SweepHalfAngle);
                    break;
                case Attack.Toll:
                    Play("HollowToll");
                    tele = fx.Ring(TollInner, TollOuter); tele.Place(transform.position, 0); tele.Lock();
                    break;
                case Attack.Charge:
                {
                    Play("PendulumCharge");
                    laneStart = transform.position;
                    var target = Target(); var dir = target - laneStart; dir.y = 0;
                    if (dir.sqrMagnitude < 1) dir = transform.forward;
                    dir.Normalize();
                    laneEnd = ClampToArena(laneStart, target + dir * ChargeOvershoot);
                    laneLength = Vector3.Distance(laneStart, laneEnd) + 1f; laneYaw = YawTo(laneEnd);
                    tele = fx.Lane(laneLength, LaneWidth); tele.Place(laneStart, laneYaw); tele.Lock();   // lane locks at warning
                    break;
                }
                case Attack.Chime:
                    Play("ShatteredChime");
                    break;
                case Attack.Recover:
                    Play("Vulnerable");
                    glowTarget = 0.3f;
                    break;
            }
        }

        // the lane stops short of the dais edge so the boss lands on the floor
        static Vector3 ClampToArena(Vector3 from, Vector3 to)
        {
            if (new Vector2(to.x, to.z).magnitude <= ArenaRadius) return to;
            var d = to - from; float lo = 0, hi = 1;
            for (int i = 0; i < 20; i++) { float m = (lo + hi) / 2; var p = from + d * m; if (new Vector2(p.x, p.z).magnitude <= ArenaRadius) lo = m; else hi = m; }
            return from + d * lo;
        }

        void Step()
        {
            tick++;
            switch (attack)
            {
                case Attack.Sweep:
                    if (tick <= SweepTrack) TurnTowards(YawTo(Target()), TurnRate / Hz);   // tracks only in the first 0.30 s
                    if (tick == SweepTrack) tele.Lock();
                    if (tick < SweepWarn) { tele.Place(transform.position, transform.eulerAngles.y); tele.Progress((float)tick / SweepWarn); Windup(tick, SweepWarn); }
                    if (tick == SweepWarn) { tele.Resolve(); fx.SweepResolve(transform.position, transform.eulerAngles.y, SweepRadius, SweepHalfAngle); Release();
                        if (player && InCone(Flat(player.transform.position - transform.position))) player.TakeHit(SweepDamage, "Bell Sweep"); }
                    if (tick >= SweepEnd) Next();
                    break;
                case Attack.Toll:
                    if (tick < TollWarn) { tele.Progress((float)tick / TollWarn); Windup(tick, TollWarn); }
                    if (tick == TollWarn) { tele.Resolve(); fx.TollResolve(transform.position, TollInner, TollOuter); Release();
                        if (player)
                        {
                            float d = Flat(player.transform.position - transform.position).magnitude;
                            if (d >= TollInner - HitMargin && d <= TollOuter + HitMargin) player.TakeHit(TollDamage, "Hollow Toll");
                        }
                    }
                    if (tick >= TollEnd) Next();
                    break;
                case Attack.Charge:
                    if (tick < ChargeWarn) { TurnTowards(YawTo(laneEnd), TurnRate * 2 / Hz); tele.Progress((float)tick / ChargeWarn); Windup(tick, ChargeWarn); }
                    if (tick == ChargeWarn)
                    {
                        tele.Resolve(); fx.ChargeTrail(transform, true); Release();
                        // the hit resolves once over the whole locked lane; the travel after it is presentation
                        if (player)
                        {
                            var l = Quaternion.Euler(0, -laneYaw, 0) * Flat(player.transform.position - laneStart);
                            if (Mathf.Abs(l.x) <= LaneWidth / 2 + HitMargin && l.z >= -HitMargin && l.z <= laneLength + HitMargin) player.TakeHit(ChargeDamage, "Pendulum Charge");
                        }
                    }
                    if (tick > ChargeWarn && tick <= ChargeWarn + ChargeTravel)
                    {
                        float u = (float)(tick - ChargeWarn) / ChargeTravel;
                        transform.position = Vector3.Lerp(laneStart, laneEnd, u);
                    }
                    if (tick == ChargeWarn + ChargeTravel) { fx.ChargeTrail(transform, false); fx.ChargeLand(transform.position); }
                    if (tick >= ChargeEnd) Next();
                    break;
                case Attack.Chime:
                    if (tick == 1) TurnTowards(YawTo(Target()), 360);
                    for (int i = 0; i < ChimeCount; i++)
                    {
                        int k = tick - 1 - i * ChimeGap;                   // ticks since marker i appeared
                        if (k == 0) { chimes[i] = fx.Circle(ChimeRadius); chimePos[i] = Target(); }
                        if (chimes[i] == null || k < 0) continue;
                        if (k <= ChimeTrack) chimePos[i] = Vector3.MoveTowards(chimePos[i], Target(), 6f / Hz);   // follows, then locks
                        if (k == ChimeTrack) chimes[i].Lock();
                        if (k < ChimeWarn) { chimes[i].Place(chimePos[i], 0); chimes[i].Progress((float)k / ChimeWarn); }
                        if (k == ChimeWarn) { chimes[i].Resolve(); fx.ChimeResolve(chimePos[i], ChimeRadius); Release(0.6f);
                            if (player && Flat(player.transform.position - chimePos[i]).magnitude <= ChimeRadius + HitMargin) player.TakeHit(ChimeDamage, "Shattered Chime");
                            chimes[i] = null;
                        }
                    }
                    if (tick < ChimeWarn) Windup(tick, ChimeWarn);
                    if (tick >= ChimeEnd) Next();
                    break;
                case Attack.Recover:
                    if (tick >= RecoverEnd) { glowTarget = 1; Next(); }
                    break;
                case Attack.Walk:
                {
                    var to = Flat(Target() - transform.position);
                    TurnTowards(YawTo(Target()), WalkTurn / Hz);
                    if (Vector3.Angle(transform.forward, to) < 60)
                    {
                        var p = transform.position + transform.forward * WalkSpeed / Hz;
                        if (new Vector2(p.x, p.z).magnitude <= ArenaRadius) transform.position = p;
                    }
                    if (to.magnitude <= Approach(pending) || tick >= WalkMax) StartAttack(pending);
                    break;
                }
            }
        }
        bool InCone(Vector3 v)
        {
            float d = v.magnitude;
            if (d > SweepRadius + HitMargin) return false;
            if (d < BodyRadius) return true;
            return Vector3.Angle(transform.forward, v) <= SweepHalfAngle + Mathf.Rad2Deg * Mathf.Atan2(HitMargin, d);
        }

        public void TakeHit(int damage, Vector3 point)
        {
            if (Dead) return;
            Hp = Mathf.Max(0, Hp - damage);
            EncounterHud.Float(point + Vector3.up * 0.6f, damage.ToString(), new Color(1f, 0.86f, 0.45f));
            fx.BossHit(point); glowLevel = Mathf.Max(glowLevel, 2.2f);
            if (Hp == 0) Die();
        }
        void Die()
        {
            Dead = true; running = false;
            ClearAttack();
            Play("Vulnerable"); glowTarget = 0.1f;
            EncounterHud.Banner("The Bellkeeper falls silent. R to reset");
        }
        void ClearAttack()
        {
            fx.ClearTelegraphs(); fx.ChargeTrail(transform, false);
            for (int i = 0; i < ChimeCount; i++) chimes[i] = null;
        }
        void ResetFight()
        {
            Hp = MaxHp; Dead = false; EncounterHud.ClearBanner();
            RestartLoop(); if (player) player.ResetVitals();
        }
        void Next() { step = (step + 1) % PhaseOne.Length; Begin(PhaseOne[step]); }

        // back to the dial centre and the first attack (review recordings start here)
        public void RestartLoop()
        {
            fx.ClearTelegraphs(); fx.ChargeTrail(transform, false);
            for (int i = 0; i < ChimeCount; i++) chimes[i] = null;
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            running = true; acc = 0; step = 0; glowTarget = glowLevel = 1; Begin(PhaseOne[0]);
        }

        // the chest core and mask light build up through a windup and flare on the resolve tick
        void Windup(int k, int warn) => glowTarget = 1 + 1.6f * k / warn;
        void Release(float flare = 1) { glowLevel = 1 + 3.5f * flare; glowTarget = 1; }

        void Update()
        {
            if (!graph.IsValid()) return;
            var kb = Keyboard.current;
            FindPlayer();
            if (kb != null && kb.rKey.wasPressedThisFrame) ResetFight();
            if (kb != null && kb.bKey.wasPressedThisFrame && !Dead)
            {
                running = !running;
                fx.ClearTelegraphs(); fx.ChargeTrail(transform, false);
                for (int i = 0; i < ChimeCount; i++) chimes[i] = null;
                if (!running) { Play("Idle"); glowTarget = 1; } else Begin(PhaseOne[step]);
            }
            // the player is down: stop attacking and wait for a reset
            bool playerDown = player && player.Down;
            if (playerDown && !Dead && attack != Attack.Recover) { ClearAttack(); Play("Idle", false); glowTarget = 1; acc = 0; attack = Attack.Recover; tick = 0; }
            if (running && !playerDown)
            {
                acc += Time.deltaTime;
                while (acc >= 1f / Hz) { acc -= 1f / Hz; Step(); }
            }
            glowLevel = Mathf.MoveTowards(glowLevel, glowTarget, Time.deltaTime * (glowLevel > glowTarget ? 6f : 3f));
            foreach (var (m, c) in glow) m.SetColor("emissiveFactor", c * glowLevel);
            Animate(Time.deltaTime);
        }

        void OnGUI()
        {
            GUI.Label(new Rect(12, 72, 900, 22), $"Bellkeeper test · Phase 1 loop · {(Dead ? "silenced" : running ? attack.ToString() : "paused")} · B pause · R reset · F potion");
        }
    }
}
