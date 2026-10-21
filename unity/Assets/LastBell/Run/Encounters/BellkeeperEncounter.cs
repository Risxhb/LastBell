using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // The final boss. "Bellwarden" in the overnight request = this Bellkeeper: the Meshy source is
    // Meshy_AI_Bellwarden_*.glb and it builds Resources/Characters/Bellkeeper.glb, so the runtime ID stays "bellkeeper".
    // Schedule follows the encounter spec v0.2 (collab PR #16, docs/design/bellkeeper.md) at Steady Ticking, with its
    // pending recommendations applied provisionally: D1 6.5 m Bell Sweep, D2 damage 30/25/30/15 (45 for the Final
    // Toll's sweep), HP-phase thresholds 70 % / 35 % queued to block boundaries, two damaging patterns at most.
    // Boss HP is shortened for the test build (see handoff).
    public sealed class BellkeeperEncounter : BossEncounter
    {
        public override string Id => "boss.bellkeeper";
        public override string DisplayName => "The Bellkeeper";
        public override string Lesson => "Everything you learned, combined";
        public override bool Placeholder => !presenter || !presenter.HasModel;

        const float SweepRadius = 6.5f, SweepHalf = 40f, TollInner = 2.6f, TollOuter = 6.8f, LaneWidth = 2.2f, Overshoot = 2.75f, ChimeRadius = 1.4f;
        const int SweepLock = 18, SweepResolve = 69, TollResolve = 72, ChargeResolve = 48, ChargeTravel = 18;
        const int ChimeFollow = 30, ChimeResolve = 78, ChimeGap = 24, Recovery = 120;
        const int SweepDmg = 30, TollDmg = 25, ChargeDmg = 30, ChimeDmg = 15, FinalSweepDmg = 45;

        CharacterPresenter presenter;
        int phase = 1;

        void Awake() { MaxHp = 3600; BodyRadius = 1.4f; HitHeight = 2.2f; MarkHeight = 5.6f; }

        protected override void Build()
        {
            presenter = CharacterPresenter.Create(transform, "Bellkeeper", 5f, 0.55f, 0, host =>
            {
                Prim.Make(PrimitiveKind.Cone, host, new Vector3(0, 2.2f, 0), new Vector3(2.6f, 3.2f, 2.6f), new Color(0.6f, 0.45f, 0.2f));
                Prim.Make(PrimitiveKind.Sphere, host, new Vector3(0, 4.1f, 0.2f), Vector3.one * 0.7f, new Color(0.95f, 0.92f, 0.85f), 1f);
                return host;
            });
            PhaseName = "The First Toll";
        }

        int PhaseFor() => Hp > MaxHp * 0.70f ? 1 : Hp > MaxHp * 0.35f ? 2 : 3;

        protected override IEnumerable<int> Script()
        {
            while (true)
            {
                // queued transition: only at a block boundary, never in the middle of a telegraph
                int want = PhaseFor();
                if (want != phase)
                {
                    phase = want;
                    foreach (var w in PhaseShift(phase == 2 ? "The Dissonance" : "The Final Toll")) yield return w;
                }
                if (phase == 1)
                {
                    foreach (var w in Approach(5.0f, 1.75f)) yield return w;
                    foreach (var w in Sweep(SweepDmg)) yield return w;
                    if (PhaseFor() != phase) continue;
                    foreach (var w in Approach(4.6f, 1.75f, 120)) yield return w;
                    foreach (var w in Toll(transform.position)) yield return w;
                    if (PhaseFor() != phase) continue;
                    foreach (var w in Charge()) yield return w;
                    if (PhaseFor() != phase) continue;
                    foreach (var w in Chimes()) yield return w;
                    foreach (var w in Vulnerable(54)) yield return w;
                    foreach (var w in WalkTo(Vector3.zero, 1.75f, 150)) yield return w;
                }
                else if (phase == 2)
                {
                    foreach (var w in Pursuit()) yield return w; yield return 30;
                    if (PhaseFor() != phase) continue;
                    foreach (var w in Discord()) yield return w; yield return 30;
                    if (PhaseFor() != phase) continue;
                    foreach (var w in Reversal()) yield return w;
                    foreach (var w in Vulnerable(60)) yield return w;
                }
                else
                {
                    foreach (var w in FinalToll()) yield return w;
                    foreach (var w in Pursuit()) yield return w;
                    yield return 40;
                }
            }
        }

        IEnumerable<int> PhaseShift(string name)
        {
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

        IEnumerable<int> Sweep(int dmg)
        {
            Current = "Bell Sweep"; presenter.PlayOnce("BellSweep", 2.03f);
            var h = Cone("Bell Sweep", SweepRadius, SweepHalf, SweepLock, SweepResolve, dmg);
            h.OnResolve = hz => { W.BossFx.SweepResolve(hz.Origin, hz.Yaw, hz.Radius, hz.HalfAngle); Release(); };
            for (int i = 0; i < SweepResolve; i++) { Windup(i / (float)SweepResolve); yield return 1; }
            yield return 36;
        }

        Hazard TollAt(Vector3 at)
        {
            var h = Ring("Hollow Toll", at, TollInner, TollOuter, TollResolve, TollDmg);
            h.OnResolve = hz => { W.BossFx.TollResolve(hz.Origin, hz.Inner, hz.Radius); Release(); };
            return h;
        }
        IEnumerable<int> Toll(Vector3 at)
        {
            Current = "Hollow Toll"; presenter.PlayOnce("HollowToll", 2.03f);
            TollAt(at);
            for (int i = 0; i < TollResolve; i++) { Windup(i / (float)TollResolve); yield return 1; }
            yield return 40;
        }

        // the charge itself; the caller decides what overlaps it
        IEnumerable<int> Charge(System.Action onLand = null)
        {
            Current = "Pendulum Charge"; presenter.PlayOnce("PendulumCharge", 1.83f);
            var (start, end, yaw, len) = ChargeLane(Overshoot);
            var h = Lane("Pendulum Charge", start, yaw, len, LaneWidth, ChargeResolve, ChargeDmg);
            h.OnResolve = hz => { W.BossFx.ChargeTrail(transform, true); Release(); };
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
        IEnumerable<int> Pursuit()
        {
            // Pendulum Charge -> Hollow Toll at the landing point, warning on the landing tick
            foreach (var w in Charge(() => { presenter.PlayOnce("HollowToll", 2.03f); TollAt(transform.position); })) yield return w;
            Current = "Hollow Toll";
            yield return TollResolve + 30;
        }
        IEnumerable<int> Discord()
        {
            // Shattered Chime -> Bell Sweep, which warns when the last marker locks
            Hazard sweep = null;
            foreach (var w in Chimes(() => { sweep = Cone("Bell Sweep", SweepRadius, SweepHalf, SweepLock, SweepResolve, SweepDmg); sweep.OnResolve = hz => { W.BossFx.SweepResolve(hz.Origin, hz.Yaw, hz.Radius, hz.HalfAngle); Release(); }; presenter.PlayOnce("BellSweep", 2.03f); })) yield return w;
            yield return 30;
        }
        IEnumerable<int> Reversal()
        {
            // Hollow Toll -> Pendulum Charge, which warns when the Toll resolves
            Current = "Hollow Toll"; presenter.PlayOnce("HollowToll", 2.03f);
            TollAt(transform.position);
            yield return TollResolve;
            foreach (var w in Charge()) yield return w;
            yield return 30;
        }

        // Phase 3: The Final Toll (spec §4.3 schedule, ticks from the start of the sequence)
        IEnumerable<int> FinalToll()
        {
            Current = "The Final Toll";
            foreach (var w in Charge()) yield return w;                      // 0..66
            presenter.PlayOnce("HollowToll", 2.03f); TollAt(transform.position);   // warns 66, resolves 138
            yield return TollResolve;
            presenter.PlayOnce("ShatteredChime", 2.63f);
            for (int i = 0; i < 3; i++)                                         // 138 / 162 / 186
            {
                var h = Marker("Shattered Chime", ChimeRadius, ChimeFollow, ChimeResolve, ChimeDmg);
                h.OnResolve = hz => { W.BossFx.ChimeResolve(hz.Origin, hz.Radius); Release(0.6f); };
                yield return ChimeGap;
            }
            yield return 240 - 138 - 3 * ChimeGap;                             // sweep warns at 240
            presenter.PlayOnce("BellSweep", 2.03f);
            var s = Cone("Bell Sweep (Final Toll)", SweepRadius, SweepHalf, SweepLock, SweepResolve, FinalSweepDmg);
            s.OnResolve = hz => { W.BossFx.SweepResolve(hz.Origin, hz.Yaw, hz.Radius, hz.HalfAngle); Release(1.5f); };
            yield return SweepResolve;                                         // resolves 309
            foreach (var w in Vulnerable(Recovery)) yield return w;            // 2 s window to 429
        }

        protected override void StepAlways()
        {
            if (!presenter) return;
            if (Current == "approach" || Current == "reposition") presenter.TickBoss("Walk");
            else presenter.TickBoss("Idle");
        }

        protected override void Update()
        {
            base.Update();
            if (presenter) presenter.GlowLevel = glowLevel;
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
            glowTarget = 0.1f; glowLevel = 3f;
            presenter.PlayOnce("Defeat", 2.5f);
            W.Fx.Crescendo(transform.position, true, 2, true);   // gold release burst; coral is reserved for danger
        }
        public CharacterPresenter Presenter => presenter;
    }
}
