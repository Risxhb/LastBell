using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

namespace LastBell.Characters
{
    // Presentation test for the Bellkeeper boss (Resources/Characters/Bellkeeper.glb, built by
    // prototype/arena-3d/models/build-bellkeeper.ps1). Not gameplay: nothing is damaged and there are no phases or HP.
    // It runs the Phase 1 performance from the boss design (reference/LAST-BELL-BELLKEEPER-BOSS-DESIGN-v1.0.md §3-4):
    // Bell Sweep -> Hollow Toll -> Pendulum Charge -> Shattered Chime -> recovery, looping, at the design's timings on a
    // 60 Hz tick, so the animations and effects can be judged in the arena. The timeline only raises events;
    // BellkeeperFx draws them. Targets the Vanguard test character when it's in the scene.
    // Spawns itself on the clock dial when a scene with the key-art arena enters Play mode.
    // Keys: B pauses / resumes the attacks (idle when paused).
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

        enum Attack { Sweep, Toll, Charge, Chime, Recover }
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
        float orbit;

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
        Vector3 Target()
        {
            var v = FindAnyObjectByType<VanguardTestController>();
            if (v) return v.transform.position;
            // nobody to fight: walk an imaginary target around the dial so facings and lanes vary
            orbit += 47f;
            return Quaternion.Euler(0, orbit, 0) * new Vector3(0, 0, 6.5f);
        }
        float YawTo(Vector3 p) { var d = p - transform.position; d.y = 0; return d.sqrMagnitude < 0.01f ? transform.eulerAngles.y : Quaternion.LookRotation(d).eulerAngles.y; }
        void TurnTowards(float yaw, float maxDeg) =>
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.Euler(0, yaw, 0), maxDeg);

        void Begin(Attack a)
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
                    float len = Vector3.Distance(laneStart, laneEnd);
                    tele = fx.Lane(len + 1f, LaneWidth); tele.Place(laneStart, YawTo(laneEnd)); tele.Lock();   // lane locks at warning
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
                    if (tick == SweepWarn) { tele.Resolve(); fx.SweepResolve(transform.position, transform.eulerAngles.y, SweepRadius, SweepHalfAngle); Release(); }
                    if (tick >= SweepEnd) Next();
                    break;
                case Attack.Toll:
                    if (tick < TollWarn) { tele.Progress((float)tick / TollWarn); Windup(tick, TollWarn); }
                    if (tick == TollWarn) { tele.Resolve(); fx.TollResolve(transform.position, TollInner, TollOuter); Release(); }
                    if (tick >= TollEnd) Next();
                    break;
                case Attack.Charge:
                    if (tick < ChargeWarn) { TurnTowards(YawTo(laneEnd), TurnRate * 2 / Hz); tele.Progress((float)tick / ChargeWarn); Windup(tick, ChargeWarn); }
                    if (tick == ChargeWarn) { tele.Resolve(); fx.ChargeTrail(transform, true); Release(); }
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
                        if (k == ChimeWarn) { chimes[i].Resolve(); fx.ChimeResolve(chimePos[i], ChimeRadius); chimes[i] = null; Release(0.6f); }
                    }
                    if (tick < ChimeWarn) Windup(tick, ChimeWarn);
                    if (tick >= ChimeEnd) Next();
                    break;
                case Attack.Recover:
                    if (tick >= RecoverEnd) { glowTarget = 1; Next(); }
                    break;
            }
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
            if (kb != null && kb.bKey.wasPressedThisFrame)
            {
                running = !running;
                fx.ClearTelegraphs(); fx.ChargeTrail(transform, false);
                for (int i = 0; i < ChimeCount; i++) chimes[i] = null;
                if (!running) { Play("Idle"); glowTarget = 1; } else Begin(PhaseOne[step]);
            }
            if (running)
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
            GUI.Label(new Rect(12, 52, 900, 22), $"Bellkeeper test · Phase 1 loop · {(running ? attack.ToString() : "paused")} · B pause/resume");
        }
    }
}
