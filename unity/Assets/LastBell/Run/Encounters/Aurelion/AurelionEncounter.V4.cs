using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // ================ Aurelion ability set v4: mythic end-boss difficulty (abilities-v4/ABILITIES-v4.md, proposal) ================
    // Chzy18 2026-10-22: "as difficult as the end boss in a mythic raid" (L'ura, Dimensius, Ansurek). Every level; co-op
    // first; Aurelion's rule exceptions (gpt/08-design-rules.md): 3+ overlapping patterns, an enrage and scaling pressure,
    // fight-ending wipes. Still no one-shots, no heal, same health bar, always targetable.
    //   1 Perfect Order 100-70 %   Lances + Starfall > Towers (alone) > Ecliptic (3) > Zenith Collapse + Lances >
    //                              Sunspots (alone) > Zenith Marks + Lances > punish
    //   70 %: Total Eclipse (intermission: light wells, Corona Shield; unbroken in 25 s = wipe)
    //   2 Misalignment 70-40 %     Sunspots + Starfall > Towers + Zenith Marks > Ecliptic (4) + Lances >
    //                              Zenith Collapse + Lances > Orrery + Horizon + Starfall (Hard: Hour Hands) > punish
    //   40 %: second form
    //   3 Exposed Meridian 40-15 % Severance (25 s) > Cascading Lances + Starfall > Sunspots + Zenith Collapse >
    //                              Towers + Lances > Hour Hands (Hard) > punish
    //   4 Last Light 15-0 %        the floor falls every 15 s (wipe at 120 s) beside a rotation of everything, no punish
    // Zenith Collapse grows every cast (Solar Hunger). Final Noon wipes the party at 7:00 (warned at 6:30). Sunspot pools
    // last until the phase ends.
    public sealed partial class AurelionEncounter
    {
        public MeridianTowers Towers { get; private set; }
        public Sunspots Spots { get; private set; }
        public TotalEclipse Eclipse { get; private set; }
        public Severance Sever { get; private set; }
        public LastLight Light { get; private set; }
        void BindV4()
        {
            Towers = Register(new MeridianTowers());      // [ability: meridian_towers] (v4)
            Spots = Register(new Sunspots());             // [ability: sunspots] (v4)
            Eclipse = Register(new TotalEclipse());       // [ability: total_eclipse] (v4)
            Sever = Register(new Severance());            // [ability: severance] (v4)
            Light = Register(new LastLight());            // [ability: last_light] (v4)
        }

        public const float V4P2At = 0.70f, V4P3At = 0.40f, V4P4At = 0.15f, EnrageS = 420f, EnrageWarnS = 390f;
        public float[] PhaseMarks => AurelionArgs.V4 ? new[] { V4P2At, V4P3At, V4P4At } : new[] { V2P2At, V2P3At };

        // ---- fight-ending wipes, the Corona Shield, pools that last until the phase ends, the enrage clock ----
        public string WipeCause { get; private set; }
        public int ShieldHp { get; private set; }
        public int ShieldMax { get; private set; }
        public int FightTicks { get; private set; }
        public bool IntermissionDone { get; private set; }
        public int CollapseCasts { get; private set; }
        public readonly List<Hazard> PhasePools = new List<Hazard>();

        public void Wipe(string cause)
        {
            if (WipeCause != null || Dead) return;
            WipeCause = cause;
            RunHud.Banner(cause.ToUpper(), new Color(1f, 0.45f, 0.38f), 4f);
            foreach (var p in W.Players) if (p && !p.Down) p.ForceDown(cause);
        }
        public void RaiseShield(int hp) { ShieldHp = ShieldMax = Mathf.Max(0, hp); }
        protected override int Absorb(int damage)
        {
            if (ShieldHp <= 0) return damage;
            int a = Mathf.Min(ShieldHp, damage); ShieldHp -= a;
            return damage - a;
        }
        public void ClearPhasePools() { foreach (var h in PhasePools) W.Hazards.Cancel(h); PhasePools.Clear(); }

        public override string HudExtra
        {
            get
            {
                if (!AurelionArgs.V4) return null;
                int s = FightTicks / Sim.Hz;
                string clock = $"{s / 60}:{s % 60:00} / {(int)EnrageS / 60}:{(int)EnrageS % 60:00}";
                return ShieldHp > 0 ? $"CORONA SHIELD {ShieldHp}   ·   {clock}" : clock;
            }
        }

        // called every tick (StepAlways): the enrage clock runs while the v4 script runs (not while tests hold him)
        void V4Clock()
        {
            if (!AurelionArgs.V4 || (TestHold && !TestClock) || W == null || !W.CombatActive || Dead) return;
            FightTicks++;
            if (FightTicks == Sim.Ticks(EnrageWarnS)) RunHud.Banner("FINAL NOON IN 30 SECONDS", new Color(1f, 0.85f, 0.45f), 3f);
            if (FightTicks == Sim.Ticks(EnrageS)) Wipe("Final Noon — the enrage");
        }

        // ---- Solar Hunger: every Collapse in the fight is hungrier (disc -0.5 m, +5 damage, to a floor and a cap) ----
        public IEnumerable<int> HungryCollapse()
        {
            int c = CollapseCasts++;
            Zenith.HungerShrink = 0.5f * c;
            Zenith.HungerBonus = Mathf.Min(25, 5 + 5 * c);
            Zenith.HungerFloor = 3.0f + 0.5f * Mathf.Max(0, (W.Players.FindAll(p => p && !p.Down).Count + 1) / 2 - 1);
            foreach (var w in Zenith.Run()) yield return w;
        }
        public IEnumerable<int> CollapseWithLances(int beams)
        {
            foreach (var w in WalkToCentre()) yield return w;
            foreach (var w in Paired(HungryCollapse(), Zenith.Duration(), Lances.Run(beams, 1, false), Lances.Duration(beams, 1, false), Sim.Ticks(LancesAfterZenith))) yield return w;
        }

        IEnumerable<IEnumerable<int>> Plan4(int ph)
        {
            float g = GapV21;
            switch (ph)
            {
                case 1:
                    yield return Block("Meridian Lances + Starfall Mirror", LancesWithStarfall(3, 1, false)); yield return V2Gap(g);
                    yield return Block("Meridian Towers", Towers.Run()); yield return V2Gap(g);
                    yield return Block("Ecliptic Order", Ecliptic.Run(3)); yield return V2Gap(g);
                    yield return Block("Zenith Collapse + Meridian Lances", CollapseWithLances(3)); yield return V2Gap(g);
                    yield return Block("Sunspots", Spots.Run()); yield return V2Gap(g);
                    yield return Block("Zenith Marks + Meridian Lances", MarksWithLances(1));
                    break;
                case 2:
                    yield return Block("Sunspots + Starfall Mirror", Together(Spots.Run(), Sim.Ticks(0.5f), Starfall.Run())); yield return V2Gap(g);
                    yield return Block("Meridian Towers + Zenith Marks", TowersThenMark()); yield return V2Gap(g);
                    yield return Block("Ecliptic Order + Meridian Lances", Together(Ecliptic.Run(4), Sim.Ticks(1.9f), Lances.Run(3, 1, false))); yield return V2Gap(g);
                    yield return Block("Zenith Collapse + Meridian Lances", CollapseWithLances(3)); yield return V2Gap(g);
                    yield return Block("Orrery Closing + Horizon", OrreryBlock(2, false));
                    break;
                case 3:
                    yield return Block("Severance", Sever.Run()); yield return V2Gap(g);
                    yield return Block("Cascading Lances + Starfall Mirror", LancesWithStarfall(4, 2, true)); yield return V2Gap(g);
                    yield return Block("Sunspots + Zenith Collapse", SpotsThenCollapse()); yield return V2Gap(g);
                    yield return Block("Meridian Towers + Meridian Lances", Paired(Towers.Run(), Towers.Duration(), Lances.Run(4, 1, false), Lances.Duration(4, 1, false), Sim.Ticks(1.0f))); yield return V2Gap(g);
                    if (W.Difficulty == Difficulty.Hard) { yield return Block("Hour Hands", Hands.Run()); yield return V2Gap(g); }
                    yield return Punish();
                    break;
                default:
                    yield return Block("Last Light", Together(Light.Run(), 0, LastLightRotation()));
                    break;
            }
        }

        // Towers + Zenith Marks: the mark resolves 1.5 s after the last tower (on the same tick, a SUN mark beside tower
        // II left a 0.5 m sliver). The conflict stays: your tower may sit in the wrong zone, so you finish it and run.
        public IEnumerable<int> TowersThenMark() => Paired(Towers.Run(), Towers.Duration(), Marks.Run(), Marks.Duration(), Sim.Ticks(1.5f));

        IEnumerable<int> SpotsThenCollapse()
        {
            foreach (var w in WalkToCentre()) yield return w;
            foreach (var w in Paired(Spots.Run(), Spots.Duration(), HungryCollapse(), Zenith.Duration(), Sim.Ticks(1.0f))) yield return w;
        }

        // Phase 4: everything, up to four patterns with the falling floor, no punish (the burn)
        IEnumerable<int> LastLightRotation()
        {
            while (true)
            {
                foreach (var w in LancesWithStarfall(4, 2, true)) yield return w;
                yield return Sim.Ticks(GapV21);
                foreach (var w in SpotsThenCollapse()) yield return w;
                yield return Sim.Ticks(GapV21);
                foreach (var w in TowersThenMark()) yield return w;
                yield return Sim.Ticks(GapV21);
                var ecl = Ecliptic.Run(4);
                foreach (var w in W.Difficulty == Difficulty.Hard ? Together(ecl, Sim.Ticks(2.5f), Hands.Run()) : ecl) yield return w;
                yield return Sim.Ticks(GapV21);
            }
        }

        int V4PhaseFor() => Hp > MaxHp * V4P2At ? 1 : Hp > MaxHp * V4P3At ? 2 : Hp > MaxHp * V4P4At ? 3 : 4;
        public float V4NextPhaseFraction() => Hp > MaxHp * V4P2At ? V4P2At - 0.01f : Hp > MaxHp * V4P3At ? V4P3At - 0.01f : Hp > MaxHp * V4P4At ? V4P4At - 0.01f : 0.05f;

        IEnumerable<int> V4Script()
        {
            BindAbilities();
            if (Telemetry == null) { Telemetry = gameObject.AddComponent<AurelionTelemetry>(); Telemetry.Bind(this, W); }
            while (true)
            {
                if (TestHold)
                {
                    if (testBody == null) { Current = "test hold"; yield return 1; continue; }
                    var body = testBody; testRunning = true;
                    foreach (var w in body) yield return w;
                    testBody = null; testRunning = false;
                    continue;
                }
                string solo = AurelionArgs.Known(AurelionArgs.Solo) ? AurelionArgs.Solo : null;
                bool restart = false;
                foreach (var step in solo != null ? new[] { SoloBlock(solo), V2Gap(1.5f) } : Plan(phase))
                {
                    foreach (var w in step) yield return w;
                    int want = V4PhaseFor();
                    if (want > phase)
                    {
                        // 70 %: the Total Eclipse intermission first (once), then the change; a burst queues one change
                        if (!IntermissionDone && phase == 1)
                        {
                            IntermissionDone = true; ClearPhasePools();
                            foreach (var w in Block("Total Eclipse", Eclipse.Run())) yield return w;
                        }
                        phase = Mathf.Max(want, 2); ClearPhasePools();
                        foreach (var w in V4Shift()) yield return w;
                        restart = true; break;
                    }
                    if (TestHold || solo != (AurelionArgs.Known(AurelionArgs.Solo) ? AurelionArgs.Solo : null)) { restart = true; break; }
                }
                if (!restart) yield return 1;
            }
        }

        IEnumerable<int> V4Shift()
        {
            Transitions++;
            if (phase >= 3) SecondForm = true;
            Current = "phase shift";
            SetPhase(phase == 2 ? "Misalignment" : phase == 3 ? "Exposed Meridian" : "Last Light");
            pose = Pose.Neutral; glowTarget = 2.5f; Clip("PhaseShift", 90);
            for (int i = 0; i < 90; i++) { if (i == 45) { W.Fx.Crescendo(transform.position, true, 0, false); Release(1.2f); } yield return 1; }
            glowTarget = 1;
        }

        public bool TestClock;                                    // tests: run the enrage clock while held
        public void DebugSetFightSeconds(float s) => FightTicks = Sim.Ticks(s);

        public void DebugSetPhaseV4(int ph)
        {
            phase = Mathf.Clamp(ph, 1, 4); SecondForm = phase >= 3; IntermissionDone = phase >= 2;
            SetPhase(phase == 1 ? "Perfect Order" : phase == 2 ? "Misalignment" : phase == 3 ? "Exposed Meridian" : "Last Light");
            DebugSetHpFraction(phase == 1 ? 0.85f : phase == 2 ? 0.55f : phase == 3 ? 0.3f : 0.1f);
        }
    }
}
