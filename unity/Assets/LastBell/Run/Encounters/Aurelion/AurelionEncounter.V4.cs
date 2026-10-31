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
    //   4 Last Light 15-0 %        the floor falls every 15 s (wipe at 120 s) beside Lances + Starfall,
    //                              Marks + Lances, Hour Hands; no punish, no Towers, Ecliptic, Collapse or Sunspots (they would burn)
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

        public const float V4P2At = 0.70f, V4P3At = 0.40f, V4P4At = 0.20f, EnrageS = 390f, EnrageWarnS = 360f;   // Final Noon at 6:30
        public float[] PhaseMarks => AurelionArgs.V4 ? new[] { V4P2At, V4P3At, V4P4At } : new[] { V2P2At, V2P3At };

        // ---- fight-ending wipes, the Corona Shield, pools that last until the phase ends, the enrage clock ----
        public string WipeCause { get; private set; }
        public int ShieldHp { get; private set; }
        public int ShieldMax { get; private set; }
        public int FightTicks { get; private set; }
        public bool IntermissionDone { get; private set; }
        public int CollapseCasts { get; private set; }
        int Party => W.Players.FindAll(p => p && !p.Down).Count;
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
            Zenith.HungerBonus = Mathf.Min(15, 5 + 5 * c);   // 50, 55, 60 cap (78 on Hard): the hardest hit, never near a one-shot
            Zenith.HungerFloor = 3.0f + 0.5f * Mathf.Max(0, (W.Players.FindAll(p => p && !p.Down).Count + 1) / 2 - 1);
            foreach (var w in Zenith.Run()) yield return w;
        }
        public IEnumerable<int> CollapseWithLances(int beams)
        {
            foreach (var w in WalkToCentre()) yield return w;
            // 3 beams, 1.2 s after the Collapse: inside the hungry disc beams 45 degrees apart leave no gap, so the answer is to
            // step out to where the gaps open (5 m) once the Collapse has landed
            beams = 3;
            foreach (var w in Paired(HungryCollapse(), Zenith.Duration(), Lances.Run(beams, 1, false), Lances.Duration(beams, 1, false), Sim.Ticks(1.2f))) yield return w;
        }

        IEnumerable<IEnumerable<int>> Plan4(int ph)
        {
            float g = 0.1f;   // almost no breathing room between blocks
            switch (ph)
            {
                case 1:
                    // hard from the pull (Chzy18 2026-10-22): every block paired, memory from the opener. The first towers come with
                    // only Starfall (the settled rule: a fight-ending mechanic is shown once before the fight stacks on it)
                    yield return Block("Ecliptic Order + Meridian Lances", EclipticWithLances()); yield return V2Gap(g);
                    yield return Block("Meridian Towers + Starfall Mirror", Paired(Towers.Run(), Towers.Duration(), Starfall.Run(true), Starfall.Duration(true), Sim.Ticks(1.0f))); yield return V2Gap(g);
                    yield return Block("Sunspots + Starfall Mirror", Together(Spots.Run(), Sim.Ticks(0.5f), Starfall.Run(true))); yield return V2Gap(g);
                    yield return Block("Zenith Collapse + Meridian Lances", CollapseWithLances(3)); yield return V2Gap(g);
                    yield return Block("Meridian Towers + Zenith Marks", TowersThenMark()); yield return V2Gap(g);
                    yield return Block("Zenith Marks + Meridian Lances", MarksWithLances(2));
                    break;
                case 2:
                    // the whole fight harder (Chzy18 2026-10-22): three patterns per block
                    yield return Block("Sunspots + Starfall Mirror + Meridian Lances", Together(Together(Spots.Run(), Sim.Ticks(0.5f), Starfall.Run(true)), Sim.Ticks(2.0f), Lances.Run(3, 1, false))); yield return V2Gap(g);
                    yield return Block("Meridian Towers + Zenith Marks + Starfall Mirror", Paired(TowersThenMark(), Towers.Duration(), Starfall.Run(true), Starfall.Duration(true), Sim.Ticks(1.0f))); yield return V2Gap(g);
                    // co-op: no Starfall here (everyone shares the ring, so 4 stars and 4 mirrors would land on it: impossible, not hard)
                    if (Party == 1) yield return Block("Ecliptic Order + Meridian Lances + Starfall Mirror", Together(EclipticWithLances(), Sim.Ticks(5.0f), Starfall.Run(true)));
                    else yield return Block("Ecliptic Order + Meridian Lances", EclipticWithLances());
                    yield return V2Gap(g);
                    yield return Block("Zenith Collapse + Meridian Lances", CollapseWithLances(4)); yield return V2Gap(g);
                    yield return Block("Orrery Closing + Horizon + Hour Hands", OrreryBlock(2, false));
                    break;
                case 3:
                    yield return Block("Severance", Sever.Run()); yield return V2Gap(g);
                    yield return Block("Cascading Lances + Starfall Mirror + Hour Hands", Together(LancesWithStarfall(4, 2, true), Sim.Ticks(1.0f), Hands.Run())); yield return V2Gap(g);
                    yield return Block("Sunspots + Zenith Collapse", SpotsThenCollapse()); yield return V2Gap(g);
                    yield return Block("Meridian Towers + Meridian Lances + Starfall Mirror", Paired(Paired(Towers.Run(), Towers.Duration(), Lances.Run(4, 1, false), Lances.Duration(4, 1, false), Sim.Ticks(1.0f)), Towers.Duration(), Starfall.Run(true), Starfall.Duration(true), Sim.Ticks(1.6f))); yield return V2Gap(g);
                    yield return Block("Zenith Marks + Meridian Lances", MarksWithLances(2));   // two-step, then the punish
                    break;
                default:
                    yield return Block("Last Light", Together(LastLightFloorFx.During(W, Light, Light.Run()), 0, LastLightRotation()));
                    break;
            }
        }

        // Towers + Zenith Marks: the mark resolves 1.5 s after the last tower (on the same tick, a SUN mark beside tower
        // II left a 0.5 m sliver). The conflict stays: your tower may sit in the wrong zone, so you finish it and run.
        public IEnumerable<int> TowersThenMark() => Paired(Towers.Run(), Towers.Duration(), Marks.Run(), Marks.Duration(), Sim.Ticks(1.5f));

        // Ecliptic Order (4 rings) + Lances: the beams land 0.2 s before ring II, while you stand at a ring and can side-step
        // inside it (crossing between rings goes past the centre, where every beam meets: no way out on Hard)
        public IEnumerable<int> EclipticWithLances()
        {
            foreach (var w in WalkToCentre()) yield return w;
            int ringII = Ecliptic.StrikeTick(1) - 1, last = Ecliptic.Duration(4);
            foreach (var w in Paired(Ecliptic.Run(4), last, Lances.Run(3, 1, false), Lances.Duration(3, 1, false), ringII - Sim.Ticks(0.2f) - last)) yield return w;
        }

        IEnumerable<int> SpotsThenCollapse()
        {
            foreach (var w in WalkToCentre()) yield return w;
            foreach (var w in Paired(Spots.Run(), Spots.Duration(), HungryCollapse(), Zenith.Duration(), Sim.Ticks(2.5f))) yield return w;   // 2.5 s: rim to his feet (8 m) after the last blast
        }

        // Phase 4: up to four patterns with the falling floor, no punish (the burn). No Towers or Ecliptic Order here: their
        // spots would land on floor that has already burned (a perfect player would have to stand in fire), no Collapse (its
        // disc is the centre every slice burns), no Sunspots (their pools on the last floor)
        IEnumerable<int> LastLightRotation()
        {
            while (true)
            {
                foreach (var w in LancesWithStarfall(4, 2, true)) yield return w;
                yield return Sim.Ticks(GapV21);
                foreach (var w in Paired(Lances.Run(3, 1, false), Lances.Duration(3, 1, false), Marks.Run(), Marks.Duration(), 0)) yield return w;
                yield return Sim.Ticks(GapV21);
                foreach (var w in Together(Hands.Run(), Sim.Ticks(0.5f), Starfall.Run(true))) yield return w;   // Hour Hands on every level now
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
                            foreach (var w in Block("Total Eclipse", TotalEclipseFx.During(W, Eclipse, Eclipse.Run()))) yield return w;
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
            if (phase >= 4) foreach (var h in Spots.Pools) W.Hazards.Cancel(h);   // Last Light starts on clean floor: only the slices burn
            if (phase >= 3) SecondForm = true;
            Current = "phase shift";
            SetPhase(phase == 2 ? "Misalignment" : phase == 3 ? "Exposed Meridian" : "Last Light");
            pose = Pose.Neutral; glowTarget = 2.5f; Clip("PhaseShift", 90);
            for (int i = 0; i < 90; i++) { if (i == 45) { W.Fx.Crescendo(transform.position, true, 0, false); Release(1.2f); } yield return 1; }
            glowTarget = 1;
        }

        // tests: a fresh fight on the same boss (Solar Hunger count, the intermission, the clock)
        public void DebugResetFight() { CollapseCasts = 0; IntermissionDone = false; FightTicks = 0; ClearPhasePools(); }
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
