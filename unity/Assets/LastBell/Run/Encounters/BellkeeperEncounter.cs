using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // The final boss, shown with Bellkeeper 2.0 (Resources/Characters/Bellkeeper2.glb, from sheet 04-bellkeeper-v1.png,
    // built by prototype/arena-3d/models/build-bellkeeper2.ps1). The first model, Bellkeeper.glb ("Bellwarden"), stays
    // in the project; the runtime ID stays "boss.bellkeeper".
    // Schedule follows the encounter spec v0.3 (collab PR #16, docs/design/bellkeeper.md) at Steady Ticking: Codex's boss
    // packet (PR #46) for names and the Unfinished Hour, v0.2's numbers for everything else. Pending recommendations are
    // applied provisionally: D1 6.5 m Bell Sweep, D2 damage 30/25/30/15 (45 for the Final Toll's sweep), D7 echo damage =
    // the recorded attack's, HP-phase thresholds 70 % / 35 % queued to block boundaries, two damaging patterns at most.
    // Boss HP is shortened for the test build (see handoff).
    public sealed class BellkeeperEncounter : BossEncounter
    {
        public override string Id => "boss.bellkeeper";
        public override string DisplayName => "The Bellkeeper";
        public override string Lesson => "Everything you learned, combined";
        public override bool Placeholder => !presenter || !presenter.HasModel;

        public const string Phase1 = "Last Guardian", Phase2 = "Broken Hour", Phase3 = "Final Toll";
        public const string March = "Pendulum March", Echo = "The Unfinished Hour";

        const float SweepRadius = 6.5f, SweepHalf = 40f, TollInner = 2.6f, TollOuter = 6.8f, LaneWidth = 2.2f, Overshoot = 2.75f, ChimeRadius = 1.4f;
        const int SweepLock = 18, SweepResolve = 69, TollResolve = 72, ChargeResolve = 48, ChargeTravel = 18;
        const int ChimeFollow = 30, ChimeResolve = 78, ChimeGap = 24, Recovery = 120;
        const int EchoWarning = 90;   // spec v0.3 §3.1 (First Chime would use 100)
        const int SweepDmg = 30, TollDmg = 25, ChargeDmg = 30, ChimeDmg = 15, FinalSweepDmg = 45;

        CharacterPresenter presenter;
        int phase = 1, loop;

        // The Unfinished Hour: one stored footprint (never live) and its inactive outline.
        Hazard memory; LastBell.VFX.BellkeeperFx.Telegraph memoryMark;
        public bool HasMemory => memory != null;
        public Hazard LiveEcho { get; private set; }       // tests: the active echo, until it resolves
        public Hazard LastFinalSweep { get; private set; }  // tests: the Final Toll's paired sweep
        public Vector3 EchoBossPosition { get; private set; }
        public int EchoesResolved { get; private set; }

        void Awake() { MaxHp = 3600; BodyRadius = 1.4f; HitHeight = 2.2f; MarkHeight = 5.6f; }

        protected override void Build()
        {
            presenter = CharacterPresenter.Create(transform, "Bellkeeper2", 5f, 0.2f, 0, host =>   // Meshy metal map is near 1: too dark without reflections
            {
                Prim.Make(PrimitiveKind.Cone, host, new Vector3(0, 2.2f, 0), new Vector3(2.6f, 3.2f, 2.6f), new Color(0.6f, 0.45f, 0.2f));
                Prim.Make(PrimitiveKind.Sphere, host, new Vector3(0, 4.1f, 0.2f), Vector3.one * 0.7f, new Color(0.95f, 0.92f, 0.85f), 1f);
                return host;
            });
            PhaseName = Phase1;
        }

        int PhaseFor() => Hp > MaxHp * 0.70f ? 1 : Hp > MaxHp * 0.35f ? 2 : 3;

        // practice phase row: the shift's state without its 1.5 s animation (no stored memory, loop restarts, look snaps)
        public override float[] PhaseStarts => new[] { 1f, 0.70f, 0.35f };
        public override int PhaseNumber => phase;
        protected override void EnterPhase(int n)
        {
            Forget();
            phase = n; loop = 0; lookPhase = n - 1;
            PhaseName = n == 2 ? Phase2 : Phase3;
        }
        // a restarted script starts from phase 1 again; transitions then follow HP as usual
        public override void BeginCombat() { phase = 1; loop = 0; PhaseName = Phase1; base.BeginCombat(); }

        protected override IEnumerable<int> Script()
        {
            while (true)
            {
                // queued transition: only at a block boundary, never in the middle of a telegraph
                int want = PhaseFor();
                if (want != phase)
                {
                    phase = want; loop = 0;
                    foreach (var w in PhaseShift(phase == 2 ? Phase2 : Phase3)) yield return w;
                }
                if (phase == 1)
                {
                    // spec §4.1: the recorded attack rotates Sweep -> Toll -> March per loop
                    int rec = loop++ % 3;
                    foreach (var w in Approach(5.0f, 1.75f)) yield return w;
                    foreach (var w in Sweep(SweepDmg, rec == 0)) yield return w;
                    if (PhaseFor() != phase) continue;
                    foreach (var w in Approach(4.6f, 1.75f, 120)) yield return w;
                    foreach (var w in Toll(transform.position, rec == 1)) yield return w;
                    if (PhaseFor() != phase) continue;
                    foreach (var w in Charge(null, rec == 2)) yield return w;
                    if (PhaseFor() != phase) continue;
                    foreach (var w in Chimes()) yield return w;
                    foreach (var w in Vulnerable(54)) yield return w;
                    if (PhaseFor() != phase) continue;
                    foreach (var w in IsolatedEcho()) yield return w;
                    foreach (var w in WalkTo(Vector3.zero, 1.75f, 150)) yield return w;
                }
                else if (phase == 2)
                {
                    // spec §4.2: A -> B -> C -> echo; the recorded attack rotates B's Sweep -> A's Toll -> C's March
                    int rec = loop++ % 3;
                    foreach (var w in Pursuit(rec == 1)) yield return w; yield return 30;
                    if (PhaseFor() != phase) continue;
                    foreach (var w in Discord(rec == 0)) yield return w; yield return 30;
                    if (PhaseFor() != phase) continue;
                    foreach (var w in Reversal(rec == 2)) yield return w;
                    if (PhaseFor() != phase) continue;
                    foreach (var w in IsolatedEcho()) yield return w;
                }
                else
                {
                    foreach (var w in FinalToll()) yield return w;
                    foreach (var w in Pursuit(false)) yield return w;
                    yield return 40;
                }
            }
        }

        IEnumerable<int> PhaseShift(string name)
        {
            Forget();   // a memory recorded in the previous phase never carries over
            Current = "phase shift"; SetPhase(name);
            presenter.PlayOnce("PhaseShift", 1.5f);
            glowTarget = 3f;
            // the ring-out burst is gold, not the danger palette: a phase shift deals no damage
            for (int i = 0; i < 90; i++) { if (i == 45) { W.Fx.Crescendo(transform.position, true, 0, false); Release(1.5f); } yield return 1; }
            glowTarget = 1;
        }

        IEnumerable<int> Vulnerable(int ticks)
        {
            Current = "recovery"; presenter.PlayOnce("Vulnerable", ticks / (float)Sim.Hz); glowTarget = 0.3f;
            yield return ticks; glowTarget = 1;
        }

        // ---------------- The Unfinished Hour (spec v0.3 §3.1) ----------------

        // Store the resolved footprint of `src` and draw its inactive outline at the original world position.
        void Record(Hazard src)
        {
            Forget();
            memory = new Hazard
            {
                Source = Echo, Shape = src.Shape, Kind = src.Kind, Origin = src.Origin, Yaw = src.Yaw,
                Radius = src.Radius, Inner = src.Inner, HalfAngle = src.HalfAngle, Length = src.Length, Width = src.Width,
                Damage = src.Damage,   // D7: the recorded attack's damage
            };
            var fx = W.BossFx;
            memoryMark = memory.Shape switch
            {
                HazardShape.Cone => fx.Cone(memory.Radius, memory.HalfAngle),
                HazardShape.Ring => fx.Ring(memory.Inner, memory.Radius),
                HazardShape.Lane => fx.Lane(memory.Length, memory.Width),
                _ => fx.Circle(memory.Radius),
            };
            memoryMark.Memory(); memoryMark.Place(memory.Origin, memory.Yaw);
        }
        void Forget()
        {
            memoryMark?.Dismiss(); memoryMark = null; memory = null;
        }
        static void RecordOnResolve(Hazard h, System.Action<Hazard> record)
        {
            var show = h.OnResolve; h.OnResolve = hz => { show?.Invoke(hz); record(hz); };
        }

        // Turn the memory into a live hazard at its recorded position: a new instance with a full warning.
        Hazard StartEcho()
        {
            if (memory == null) return null;
            var m = memory; memoryMark?.Dismiss(); memoryMark = null; memory = null;
            var h = W.Hazards.Spawn(new Hazard
            {
                Source = Echo, Shape = m.Shape, Kind = m.Kind, Origin = m.Origin, Yaw = m.Yaw, Radius = m.Radius, Inner = m.Inner,
                HalfAngle = m.HalfAngle, Length = m.Length, Width = m.Width, LockAt = 0, ResolveAt = EchoWarning, Damage = m.Damage,
            });
            h.OnResolve = hz =>
            {
                switch (hz.Shape)
                {
                    case HazardShape.Cone: W.BossFx.SweepResolve(hz.Origin, hz.Yaw, hz.Radius, hz.HalfAngle); break;
                    case HazardShape.Ring: W.BossFx.TollResolve(hz.Origin, hz.Inner, hz.Radius); break;
                    default: W.BossFx.ChargeLand(hz.Origin + Sim.Dir(hz.Yaw) * (hz.Length - 1f)); break;
                }
                Release(); EchoesResolved++; if (LiveEcho == hz) LiveEcho = null;
            };
            LiveEcho = h; EchoBossPosition = transform.position;
            return h;
        }

        // Phases 1-2: the echo alone (nothing else warning), then the chest opens for the punish window.
        IEnumerable<int> IsolatedEcho()
        {
            if (memory == null) yield break;
            Current = Echo; presenter.PlayOnce("EchoTell", 1.53f);
            StartEcho();
            for (int i = 0; i < EchoWarning; i++) { Windup(i / (float)EchoWarning); yield return 1; }
            foreach (var w in Vulnerable(Recovery)) yield return w;
        }

        // ---------------- attacks ----------------

        IEnumerable<int> Sweep(int dmg, bool record = false)
        {
            Current = "Bell Sweep"; presenter.PlayOnce("BellSweep", 2.03f);
            var h = Cone("Bell Sweep", SweepRadius, SweepHalf, SweepLock, SweepResolve, dmg);
            h.OnResolve = hz => { W.BossFx.SweepResolve(hz.Origin, hz.Yaw, hz.Radius, hz.HalfAngle); Release(); };
            if (record) RecordOnResolve(h, Record);
            for (int i = 0; i < SweepResolve; i++) { Windup(i / (float)SweepResolve); yield return 1; }
            yield return 36;
        }

        Hazard TollAt(Vector3 at, bool record = false)
        {
            var h = Ring("Hollow Toll", at, TollInner, TollOuter, TollResolve, TollDmg);
            h.OnResolve = hz => { W.BossFx.TollResolve(hz.Origin, hz.Inner, hz.Radius); Release(); };
            if (record) RecordOnResolve(h, Record);
            return h;
        }
        IEnumerable<int> Toll(Vector3 at, bool record = false)
        {
            Current = "Hollow Toll"; presenter.PlayOnce("HollowToll", 2.03f);
            TollAt(at, record);
            for (int i = 0; i < TollResolve; i++) { Windup(i / (float)TollResolve); yield return 1; }
            yield return 40;
        }

        // the march itself; the caller decides what overlaps it
        IEnumerable<int> Charge(System.Action onLand = null, bool record = false)
        {
            Current = March; presenter.PlayOnce("PendulumCharge", 1.83f);
            var (start, end, yaw, len) = ChargeLane(Overshoot);
            var h = Lane(March, start, yaw, len, LaneWidth, ChargeResolve, ChargeDmg);
            h.OnResolve = hz => { W.BossFx.ChargeTrail(transform, true); Release(); };
            if (record) RecordOnResolve(h, Record);
            for (int i = 0; i < ChargeResolve; i++) { TurnTowards(yaw, 480); Windup(i / (float)ChargeResolve); yield return 1; }
            for (int i = 1; i <= ChargeTravel; i++) { transform.position = Vector3.Lerp(start, end, i / (float)ChargeTravel); yield return 1; }
            W.BossFx.ChargeTrail(transform, false); W.BossFx.ChargeLand(transform.position);
            onLand?.Invoke();
        }

        IEnumerable<int> Chimes(System.Action onLastLock = null)
        {
            Current = "Shattered Chime"; presenter.PlayOnce("ShatteredChime", 2.63f);
            TurnTowards(YawToTarget(), 9999);
            for (int i = 0; i < 3; i++)
            {
                var h = Marker("Shattered Chime", ChimeRadius, ChimeFollow, ChimeResolve, ChimeDmg);
                h.OnResolve = hz => { W.BossFx.ChimeResolve(hz.Origin, hz.Radius); Release(0.6f); };
                if (i < 2) yield return ChimeGap;
            }
            yield return ChimeFollow; onLastLock?.Invoke();
            yield return ChimeResolve - ChimeFollow + 10;
        }

        // Phase 2 blocks (spec §4.2)
        IEnumerable<int> Pursuit(bool recordToll)
        {
            // Pendulum March -> Hollow Toll at the landing point, warning on the landing tick
            foreach (var w in Charge(() => { presenter.PlayOnce("HollowToll", 2.03f); TollAt(transform.position, recordToll); })) yield return w;
            Current = "Hollow Toll";
            yield return TollResolve + 30;
        }
        IEnumerable<int> Discord(bool recordSweep)
        {
            // Shattered Chime -> Bell Sweep, which warns when the last marker locks
            foreach (var w in Chimes(() =>
            {
                var sweep = Cone("Bell Sweep", SweepRadius, SweepHalf, SweepLock, SweepResolve, SweepDmg);
                sweep.OnResolve = hz => { W.BossFx.SweepResolve(hz.Origin, hz.Yaw, hz.Radius, hz.HalfAngle); Release(); };
                if (recordSweep) RecordOnResolve(sweep, Record);
                presenter.PlayOnce("BellSweep", 2.03f);
            })) yield return w;
            yield return 30;
        }
        IEnumerable<int> Reversal(bool recordMarch)
        {
            // Hollow Toll -> Pendulum March, which warns when the Toll resolves; the echo warns 30 ticks after landing
            Current = "Hollow Toll"; presenter.PlayOnce("HollowToll", 2.03f);
            TollAt(transform.position);
            yield return TollResolve;
            foreach (var w in Charge(null, recordMarch)) yield return w;
            yield return 30;
        }

        // Phase 3: The Final Toll (spec v0.3 §4.3 schedule, ticks from the start of the sequence)
        IEnumerable<int> FinalToll()
        {
            Current = "The Final Toll";
            foreach (var w in Charge(null, true)) yield return w;              // 0..66, the lane is remembered at 48
            presenter.PlayOnce("HollowToll", 2.03f); TollAt(transform.position);   // warns 66, resolves 138
            yield return TollResolve;
            presenter.PlayOnce("ShatteredChime", 2.63f);
            for (int i = 0; i < 3; i++)                                         // 138 / 162 / 186, last resolves 264
            {
                var h = Marker("Shattered Chime", ChimeRadius, ChimeFollow, ChimeResolve, ChimeDmg);
                h.OnResolve = hz => { W.BossFx.ChimeResolve(hz.Origin, hz.Radius); Release(0.6f); };
                yield return ChimeGap;
            }
            yield return 265 - 138 - 3 * ChimeGap;                             // echo activates at 265
            Current = Echo; StartEcho(); presenter.PlayOnce("EchoTell", 1.53f);   // resolves 355
            yield return 286 - 265;                                             // sweep warns at 286
            Current = "The Final Toll";
            presenter.PlayOnce("BellSweep", 2.03f);
            var s = Cone("Bell Sweep (Final Toll)", SweepRadius, SweepHalf, SweepLock, SweepResolve, FinalSweepDmg);
            s.OnResolve = hz => { W.BossFx.SweepResolve(hz.Origin, hz.Yaw, hz.Radius, hz.HalfAngle); Release(1.5f); };
            LastFinalSweep = s;
            yield return SweepResolve;                                         // locks 304, resolves 355 with the echo
            foreach (var w in Vulnerable(Recovery)) yield return w;            // 2 s window to 475
        }

        protected override void StepAlways()
        {
            if (!presenter) return;
            if (Current == "approach" || Current == "reposition") presenter.TickBoss("Walk");
            else presenter.TickBoss("Idle");
        }

        // ---------------- phase looks (spec v0.3 §4, sheet 04-bellkeeper-v1.png) ----------------
        // Last Guardian: closed shell, compact rings. Broken Hour: shell halves parted, rings offset. Final Toll: chest
        // fully open, rings spread, brighter core. The chest also opens for every punish window and on defeat. The clips
        // keep the shell and ring offsets at rest, so this is layered on after the presenter evaluates each frame.
        static readonly float[] ShellOpen = { 0, 14, 42 }, ShellPart = { 0, 0.06f, 0.12f }, PhaseGlow = { 1, 1.5f, 2.2f };
        static readonly float[] RingOut = { 0, 0.18f, 0.4f }, RingUp = { 0, 0.04f, 0.2f }, RingTilt = { 0, 12, 25 }, BackRingUp = { 0, 0.1f, 0.25f };
        const float PunishOpen = 50, DefeatOpen = 70;
        Transform shellL, shellR, ringL, ringR, ringC; bool lookBound;
        public float ShellAngle { get; private set; }       // tests: current chest opening, degrees
        public float RingSpread { get; private set; }       // tests: current side-ring offset, metres
        public bool PhaseRigFound => shellL && shellR && ringL && ringR && ringC;
        float lookPhase;                                    // eased 0..2 between the phase looks

        void BindLook()
        {
            lookBound = true;
            if (!presenter || !presenter.Visual) return;
            foreach (var t in presenter.Visual.GetComponentsInChildren<Transform>(true))
            {
                switch (t.name)
                {
                    case "shell.L": shellL = t; break;
                    case "shell.R": shellR = t; break;
                    case "ring.L": ringL = t; break;
                    case "ring.R": ringR = t; break;
                    case "ring.C": ringC = t; break;
                }
            }
        }
        static float Lerp3(float[] v, float p) => p <= 1 ? Mathf.Lerp(v[0], v[1], p) : Mathf.Lerp(v[1], v[2], p - 1);

        void LateUpdate()
        {
            if (!lookBound) BindLook();
            if (!PhaseRigFound) return;
            float dt = Time.deltaTime;
            lookPhase = Mathf.MoveTowards(lookPhase, phase - 1, dt / 0.8f);    // the change plays across the 1.5 s shift
            float open = Dead ? DefeatOpen : Current == "recovery" ? Mathf.Max(PunishOpen, Lerp3(ShellOpen, lookPhase)) : Lerp3(ShellOpen, lookPhase);
            ShellAngle = Mathf.MoveTowards(ShellAngle, open, dt * (open > ShellAngle ? 120 : 60));
            RingSpread = Lerp3(RingOut, lookPhase);
            Vector3 up = transform.up, fwd = transform.forward, right = transform.right, c = transform.position;
            foreach (var shell in new[] { shellL, shellR })
            {
                // hinge at the bone head (the half's outer top corner): swing the inner edge forward and out
                var hinge = shell.position; var toAxis = Vector3.ProjectOnPlane(c - hinge, up);
                float s = Mathf.Sign(Vector3.Dot(Vector3.Cross(up, toAxis), fwd));
                shell.RotateAround(hinge, up, s * ShellAngle);
                shell.position += right * Mathf.Sign(Vector3.Dot(hinge - c, right)) * Lerp3(ShellPart, lookPhase);
            }
            foreach (var ring in new[] { ringL, ringR })
            {
                float side = Mathf.Sign(Vector3.Dot(ring.position - c, right));
                ring.RotateAround(ring.position, fwd, -side * Lerp3(RingTilt, lookPhase));   // tops lean outward
                ring.position += right * side * RingSpread + up * Lerp3(RingUp, lookPhase);
            }
            ringC.RotateAround(ringC.position, right, -0.4f * Lerp3(RingTilt, lookPhase));
            ringC.position += up * Lerp3(BackRingUp, lookPhase);
        }

        protected override void Update()
        {
            base.Update();
            if (presenter) presenter.GlowLevel = glowLevel * Lerp3(PhaseGlow, lookPhase);
            if (Dead && presenter && presenter.Visual)
            {
                // defeat: placeholder sink below the dial edge after the Defeat/Vulnerable pose
                defeatT += Time.deltaTime;
                if (defeatT > 1.2f) presenter.Sink = Mathf.MoveTowards(presenter.Sink, 1.5f, Time.deltaTime * 0.8f);
            }
        }
        float defeatT;
        protected override void OnDefeat()
        {
            Forget(); LiveEcho = null;   // the killing tick already cancelled live hazards; the memory goes with them
            glowTarget = 0.1f; glowLevel = 3f;
            presenter.PlayOnce("Defeat", 2.5f);
            W.Fx.Crescendo(transform.position, true, 2, true);   // gold release burst; coral is reserved for danger
        }
        void OnDestroy() { Forget(); }   // retry / practice restart: no outline survives the encounter
        public CharacterPresenter Presenter => presenter;
    }
}
