using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastBell.Run
{
    // ================ Aurelion ability set v2 (abilities-v2/ABILITIES.md, proposal) ================
    // The default set; -lastbellAbilitySet v1 keeps the packet's five attacks (AurelionEncounter.cs) for comparison.
    // Phase plan (Medium, seconds; Easy stretches the warnings inside each ability, never the gaps or the punish):
    //   1 Perfect Order 100–65 %   Lances (3 beams, 1 volley) > 1.0 · Starfall > 1.0 · Noon Shadow > 1.0 ·
    //                              Orrery Closing (no Horizon) > punish 3.5
    //   65 %: Misalignment (1.5 s, harmless, queued after the current block, same HP bar; form 1, cracked shoulder)
    //   2 Misalignment 65–30 %     Lances (4 beams, 2 volleys) > 0.6 · Noon Shadow + Starfall (0.7 s in) > 0.6 ·
    //                              Lances (4 beams, 1 volley) > 0.6 · Orrery Closing > Horizon 4 s + Starfall > punish 3.5
    //   30 %: Exposed Meridian, the SECOND FORM (1.5 s, harmless): chest shutters open, halo arcs spread, pendulum glows
    //   3 Exposed Meridian 30–0 %  Cascading Lances (4 beams, volley 2 rotated 22.5°) > 0.6 · Noon and Afternoon > 0.6 ·
    //                              Grand Orrery (Orrery Closing + Starfall 0.8 s in) > rest of the Horizon > punish 3.5
    // Hard adds Hour Hands inside each Horizon (Phases 2 and 3), warning on the tick that Horizon's Starfall resolves;
    // the Horizon stretches to 6 s. At most two damaging patterns are live at once (Horizon + Starfall, then Horizon +
    // Hour Hands). A burst through 65 % and 30 % plays one transition. The boss stays where he is between blocks
    // (open question: drift toward the party?).
    public sealed partial class AurelionEncounter
    {
        // ---- the six abilities: one marked registration line each (ability sessions own only their own line) ----
        public MeridianLances Lances { get; private set; }
        public StarfallMirror Starfall { get; private set; }
        public NoonShadow Noon { get; private set; }
        public OrreryClosing Closing { get; private set; }
        public HourHands Hands { get; private set; }
        public ZenithCollapse Zenith { get; private set; }
        public EclipticOrder Ecliptic { get; private set; }
        public ZenithMarks Marks { get; private set; }
        readonly List<AurelionAbility> abilities = new List<AurelionAbility>();

        void BindAbilities()
        {
            if (abilities.Count > 0) return;
            Lances = Register(new MeridianLances());      // [ability: meridian_lances]
            Starfall = Register(new StarfallMirror());    // [ability: starfall_mirror]
            Noon = Register(new NoonShadow());            // [ability: noon_shadow]
            Closing = Register(new OrreryClosing());      // [ability: orrery_closing]
            Hands = Register(new HourHands());            // [ability: hour_hands]
            Zenith = Register(new ZenithCollapse());      // [ability: zenith_collapse] (v2.1)
            Ecliptic = Register(new EclipticOrder());     // [ability: ecliptic_order] (v3)
            Marks = Register(new ZenithMarks());          // [ability: zenith_marks] (v3)
            BindV4();                                     // v4 abilities (AurelionEncounter.V4.cs)
        }
        T Register<T>(T a) where T : AurelionAbility { a.Bind(this, W); abilities.Add(a); return a; }
        public IReadOnlyList<AurelionAbility> Abilities => abilities;
        public AurelionTelemetry Telemetry { get; private set; }   // Step 0 telemetry (hits, margins, phases, potions)

        // tests: one pass of a phase's plan (the current set), without transitions
        public IEnumerable<int> TestPhasePlan(int ph) { foreach (var b in Plan(ph)) foreach (var w in b) yield return w; }

        // ---- v2 numbers (ABILITIES.md §3) ----
        public const float GapP1 = 1.0f, Gap = 0.6f, PunishS = 3.5f, ShiftS = 1.5f;
        public const float StarfallIntoNoon = 0.7f, StarfallIntoOrrery = 0.8f;
        public const int HorizonTicks = 240, HorizonTicksHard = 360;
        public const float V2P2At = 0.65f, V2P3At = 0.30f;

        // ---- state the abilities and tests read ----
        public CombatWorld World => W;
        public bool SecondForm { get; private set; }
        public int Transitions { get; private set; }
        public int Punishes { get; private set; }
        public int ShortestPunish { get; private set; } = int.MaxValue;   // test evidence, in ticks (measured on the script, not per frame)
        public int PunishHazardTicks { get; private set; }               // test evidence: ticks of a punish with any hazard live
        public int Blocks { get; private set; }                    // phase-plan blocks started
        public int V2MaxLive { get; private set; }                 // test evidence: hazards live at once
        public void SetCurrent(string name) => Current = name;
        public void Glow(float k) => Windup(k);
        public void Flare(float f = 1) => Release(f);
        public void PlayClip(string name, int ticks) => Clip(name, ticks);
        public Hazard Track(Hazard h) { V2MaxLive = Mathf.Max(V2MaxLive, W.Hazards.Count); return h; }
        public int HorizonFor() => W.Difficulty == Difficulty.Hard ? HorizonTicksHard : HorizonTicks;
        // the block log (test evidence): (phase, block name, world tick)
        public readonly List<(int phase, string block, int tick)> BlockLog = new List<(int, string, int)>();

        // ---- tests: hold the schedule and run only what TestRun hands over ----
        public bool TestHold;
        IEnumerable<int> testBody; bool testRunning;
        public void TestRun(IEnumerable<int> body) => testBody = body;
        public bool TestBusy => testBody != null || testRunning;

        // Runs two tick scripts side by side, b starting delayB ticks after a; ends on the tick the later one ends.
        public static IEnumerable<int> Together(IEnumerable<int> a, int delayB, IEnumerable<int> b)
        {
            var ea = a.GetEnumerator(); var eb = b.GetEnumerator();
            int wa = 0, wb = Mathf.Max(0, delayB); bool la = true, lb = true;
            while (la || lb)
            {
                if (la) { if (wa > 0) wa--; else if (ea.MoveNext()) wa = Mathf.Max(1, ea.Current) - 1; else la = false; }
                if (lb) { if (wb > 0) wb--; else if (eb.MoveNext()) wb = Mathf.Max(1, eb.Current) - 1; else lb = false; }
                if (la || lb) yield return 1;
            }
        }

        // ---- blocks ----
        IEnumerable<int> Block(string name, IEnumerable<int> body)
        {
            Blocks++; BlockLog.Add((phase, name, W.Tick));
            foreach (var w in body) yield return w;
        }
        IEnumerable<int> V2Gap(float s)
        {
            Current = "recovery"; glowTarget = 0.6f;
            yield return Sim.Ticks(s);
            glowTarget = 1;
        }
        // the major punish window: no hazards, glaive planted, chest open, pendulum exposed (v1's OpenChest pose and clip)
        public IEnumerable<int> Punish()
        {
            Punishes++; BlockLog.Add((phase, "punish", W.Tick));
            int t = Sim.Ticks(PunishS);
            Current = "open chest"; pose = Pose.Exposed; glowTarget = 0.3f; Clip("OpenChest", t);
            int start = W.Tick;
            // the script steps before the hazards each tick: one that ends on this very tick (a spoke resolving as the
            // punish starts) is not live during the punish
            for (int i = 0; i < t; i++)
            {
                foreach (var h in W.Hazards.Live) if (h.Age + 1 < h.ResolveAt + Mathf.Max(0, h.ActiveTicks - 1)) { PunishHazardTicks++; break; }
                yield return 1;
            }
            ShortestPunish = Mathf.Min(ShortestPunish, W.Tick - start);
            glowTarget = 1; pose = Pose.Neutral;
        }
        IEnumerable<int> WaitHorizon()
        {
            while (Closing.HorizonLive) { Current = "Horizon"; yield return 1; }
        }

        // Orrery Closing, then (Phase 2+; v2.1 every phase) the Horizon with Starfall inside, Hour Hands on Hard (Phase 2+),
        // then the punish. grand = Phase 3's Grand Orrery: Starfall is cast 0.8 s into the closing instead of at the
        // Horizon's start.
        public IEnumerable<int> OrreryBlock(int ph, bool grand, bool horizonAlways = false)
        {
            pose = Pose.Planted;
            bool withHorizon = ph >= 2 || horizonAlways;
            int horizon = withHorizon ? HorizonFor() : 0;
            if (grand) foreach (var w in Together(Closing.Run(horizon), Sim.Ticks(StarfallIntoOrrery * Starfall.WarnScale), Starfall.Run())) yield return w;
            else
            {
                foreach (var w in Closing.Run(horizon)) yield return w;
                if (withHorizon) foreach (var w in Starfall.Run()) yield return w;
            }
            if (ph >= 2 && (W.Difficulty == Difficulty.Hard || AurelionArgs.V4)) foreach (var w in Hands.Run()) yield return w;   // v4: every level
            foreach (var w in WaitHorizon()) yield return w;
            foreach (var w in Punish()) yield return w;
        }

        public IEnumerable<int> NoonWithStarfall() =>
            Together(Noon.Run(false), Sim.Ticks(StarfallIntoNoon * Noon.WarnScale), Starfall.Run());

        // ================ v2.1 (abilities-v2/ABILITIES-v2.1.md, proposal): Medium and Hard; Easy keeps the v2 plan ================
        //   1 Perfect Order     Lances (3) + Starfall > Noon + Starfall > Zenith Collapse + Lances (3) >
        //                       Orrery Closing > Horizon + Starfall > punish 3.5
        //   2 Misalignment      Lances (4, 2 volleys) + Starfall > Noon + Starfall > Zenith Collapse + Lances (3) >
        //                       Orrery Closing > Horizon + Starfall (Hard: then Hour Hands) > punish 3.5
        //   3 Exposed Meridian  Cascading Lances + Starfall > Noon and Afternoon + Starfall (on Afternoon) >
        //                       Zenith Collapse + Lances (4) > Grand Orrery > Horizon (Hard: Hour Hands) > punish 3.5
        // Gaps 0.3 s. Pairs are timed by resolve: Lances resolves 0.2 s before its Starfall (which keeps its full window on
        // Hard), Starfall with Noon's (last) half, and Lances 0.3 s after Zenith Collapse. Before Zenith Collapse he walks
        // to the arena centre (he never leaves it afterwards, so only the first one walks; the spec's Starfall during the
        // walk is left out: it would make three patterns with the Collapse pair).
        public bool V21 => W.Difficulty != Difficulty.Easy;
        public const float GapV21 = 0.3f, LancesBeforeStar = 0.2f, LancesAfterZenith = 0.3f, CentreSpeed = 2.2f;

        // a and b side by side, timed so b's last resolve lands bAfterA ticks after a's (either may start first)
        public static IEnumerable<int> Paired(IEnumerable<int> a, int aResolve, IEnumerable<int> b, int bResolve, int bAfterA)
        {
            int d = aResolve + bAfterA - bResolve;
            return d >= 0 ? Together(a, d, b) : Together(b, -d, a);
        }
        public IEnumerable<int> WalkToCentre() => WalkTo(Vector3.zero, CentreSpeed, Sim.Ticks(4.5f));
        public IEnumerable<int> ZenithBlock(int beams)
        {
            foreach (var w in WalkToCentre()) yield return w;
            foreach (var w in Paired(Zenith.Run(), Zenith.Duration(), Lances.Run(beams, 1, false), Lances.Duration(beams, 1, false), Sim.Ticks(LancesAfterZenith))) yield return w;
        }
        public IEnumerable<int> LancesWithStarfall(int beams, int volleys, bool cascade) =>
            Paired(Lances.Run(beams, volleys, cascade), Lances.Duration(beams, volleys, cascade), Starfall.Run(true), Starfall.Duration(true), Sim.Ticks(LancesBeforeStar));
        public IEnumerable<int> NoonWithStarfall21(bool afternoon) =>
            Paired(Noon.Run(afternoon), Noon.Duration(afternoon), Starfall.Run(), Starfall.Duration(), 0);

        IEnumerable<IEnumerable<int>> Plan21(int ph)
        {
            switch (ph)
            {
                case 1:
                    yield return Block("Meridian Lances + Starfall Mirror", LancesWithStarfall(3, 1, false)); yield return V2Gap(GapV21);
                    yield return Block("Noon Shadow + Starfall Mirror", NoonWithStarfall21(false)); yield return V2Gap(GapV21);
                    yield return Block("Zenith Collapse + Meridian Lances", ZenithBlock(3)); yield return V2Gap(GapV21);
                    yield return Block("Orrery Closing + Horizon", OrreryBlock(1, false, true));
                    break;
                case 2:
                    yield return Block("Meridian Lances + Starfall Mirror", LancesWithStarfall(4, 2, false)); yield return V2Gap(GapV21);
                    yield return Block("Noon Shadow + Starfall Mirror", NoonWithStarfall21(false)); yield return V2Gap(GapV21);
                    yield return Block("Zenith Collapse + Meridian Lances", ZenithBlock(3)); yield return V2Gap(GapV21);
                    yield return Block("Orrery Closing + Horizon", OrreryBlock(2, false));
                    break;
                default:
                    yield return Block("Cascading Lances + Starfall Mirror", LancesWithStarfall(4, 2, true)); yield return V2Gap(GapV21);
                    yield return Block("Noon and Afternoon + Starfall Mirror", NoonWithStarfall21(true)); yield return V2Gap(GapV21);
                    yield return Block("Zenith Collapse + Meridian Lances", ZenithBlock(4)); yield return V2Gap(GapV21);
                    yield return Block("Grand Orrery", OrreryBlock(3, true));
                    break;
            }
        }

        // ================ v3, Milestone A interim loop (abilities-v3/ABILITIES-v3.md, proposal; -lastbellAbilitySet v3) ================
        // The v2.1 plan with its two pure "step out" slots replaced; every other block unchanged:
        //   1 Perfect Order     Lances (3) + Starfall > Ecliptic Order (3 anchors, alone) > Zenith Collapse + Lances (3) >
        //                       Zenith Marks + Lances (3) > punish 3.5
        //   2 Misalignment      Lances (4, 2 volleys) + Starfall > Ecliptic Order (4) > Zenith Collapse + Lances (3) >
        //                       Zenith Marks (two-step) + Lances (3, 2 volleys) > punish 3.5
        //   3 Exposed Meridian  Cascading Lances + Starfall > Ecliptic Order (4) > Zenith Collapse + Lances (4) >
        //                       Grand Orrery > Horizon (Hard: Hour Hands) > punish 3.5
        // Noon Shadow's slot is Ecliptic Order; the plain Orrery Closing's (Phases 1–2) is Zenith Marks, resolving on the
        // Lances' last volley. Easy uses the same plan with its own long windows (anchors stay lit, 4 s marks).
        public IEnumerable<int> MarksWithLances(int ph)
        {
            // 3 beams: inside the 5.5 m shade, 4 beams (eight half-lanes 45 degrees apart) would leave almost no gap
            int beams = 3, volleys = ph == 1 ? 1 : 2;
            foreach (var w in WalkToCentre()) yield return w;   // SHADE must be reachable from any rim spot (9.7 m in 3.0 s)
            if (volleys == 1) foreach (var w in Paired(Lances.Run(beams, 1, false), Lances.Duration(beams, 1, false), Marks.Run(), Marks.Duration(), 0)) yield return w;
            else
            {
                // Misalignment: a two-step read, one step per volley ("SHADE > SUN"): step 0 resolves with volley 1, step 1 with volley 2
                int v1 = Lances.Duration(beams, 1, false), full = Lances.Duration(beams, 2, false), d = Marks.Duration() - v1;
                foreach (var w in Together(Marks.Run(d + full), d, Lances.Run(beams, 2, false))) yield return w;
            }
            foreach (var w in Punish()) yield return w;
        }

        IEnumerable<IEnumerable<int>> Plan3(int ph)
        {
            switch (ph)
            {
                case 1:
                    yield return Block("Meridian Lances + Starfall Mirror", LancesWithStarfall(3, 1, false)); yield return V2Gap(GapV21);
                    yield return Block("Ecliptic Order", Ecliptic.Run(3)); yield return V2Gap(GapV21);
                    yield return Block("Zenith Collapse + Meridian Lances", ZenithBlock(3)); yield return V2Gap(GapV21);
                    yield return Block("Zenith Marks + Meridian Lances", MarksWithLances(1));
                    break;
                case 2:
                    yield return Block("Meridian Lances + Starfall Mirror", LancesWithStarfall(4, 2, false)); yield return V2Gap(GapV21);
                    yield return Block("Ecliptic Order", Ecliptic.Run(4)); yield return V2Gap(GapV21);
                    yield return Block("Zenith Collapse + Meridian Lances", ZenithBlock(3)); yield return V2Gap(GapV21);
                    yield return Block("Zenith Marks + Meridian Lances", MarksWithLances(2));
                    break;
                default:
                    yield return Block("Cascading Lances + Starfall Mirror", LancesWithStarfall(4, 2, true)); yield return V2Gap(GapV21);
                    yield return Block("Ecliptic Order", Ecliptic.Run(4)); yield return V2Gap(GapV21);
                    yield return Block("Zenith Collapse + Meridian Lances", ZenithBlock(4)); yield return V2Gap(GapV21);
                    yield return Block("Grand Orrery", OrreryBlock(3, true));
                    break;
            }
        }

        IEnumerable<IEnumerable<int>> Plan(int ph)
        {
            if (AurelionArgs.V4) { foreach (var b in Plan4(ph)) yield return b; yield break; }
            if (AurelionArgs.V3) { foreach (var b in Plan3(ph)) yield return b; yield break; }
            if (V21) { foreach (var b in Plan21(ph)) yield return b; yield break; }
            switch (ph)
            {
                case 1:
                    yield return Block("Meridian Lances", Lances.Run(3, 1, false)); yield return V2Gap(GapP1);
                    yield return Block("Starfall Mirror", Starfall.Run()); yield return V2Gap(GapP1);
                    yield return Block("Noon Shadow", Noon.Run(false)); yield return V2Gap(GapP1);
                    yield return Block("Orrery Closing", OrreryBlock(1, false));
                    break;
                case 2:
                    yield return Block("Meridian Lances", Lances.Run(4, 2, false)); yield return V2Gap(Gap);
                    yield return Block("Noon Shadow + Starfall Mirror", NoonWithStarfall()); yield return V2Gap(Gap);
                    yield return Block("Meridian Lances", Lances.Run(4, 1, false)); yield return V2Gap(Gap);
                    yield return Block("Orrery Closing + Horizon", OrreryBlock(2, false));
                    break;
                default:
                    yield return Block("Cascading Lances", Lances.Run(4, 2, true)); yield return V2Gap(Gap);
                    yield return Block("Noon and Afternoon", Noon.Run(true)); yield return V2Gap(Gap);
                    yield return Block("Grand Orrery", OrreryBlock(3, true));
                    break;
            }
        }

        // one v2 ability on its own, on loop (-lastbellAbility <id> or F6); its phase variant follows the HP phase
        public IEnumerable<int> SoloBlock(string id)
        {
            switch (id)
            {
                case "aurelion.meridian_lances": return Block("Meridian Lances", Lances.Run(phase == 1 ? 3 : 4, phase == 1 ? 1 : 2, phase == 3));
                case "aurelion.starfall_mirror": return Block("Starfall Mirror", Starfall.Run());
                case "aurelion.noon_shadow": return Block("Noon Shadow", Noon.Run(phase == 3));
                case "aurelion.orrery_closing": return Block("Orrery Closing", OrreryBlock(phase, phase == 3));
                case "aurelion.zenith_collapse": return Block("Zenith Collapse", ZenithSolo());
                case "aurelion.ecliptic_order": return Block("Ecliptic Order", Ecliptic.Run(phase == 1 ? 3 : 4));
                case "aurelion.zenith_marks": return Block("Zenith Marks + Meridian Lances", MarksWithLances(phase));
                case "aurelion.meridian_towers": return Block("Meridian Towers", Towers.Run());
                case "aurelion.sunspots": return Block("Sunspots", Spots.Run());
                case "aurelion.total_eclipse": return Block("Total Eclipse", TotalEclipseFx.During(W, Eclipse, Eclipse.Run()));
                case "aurelion.severance": return Block("Severance", Sever.Run());
                case "aurelion.last_light": return Block("Last Light", Together(Light.Run(), 0, LastLightRotation()));
                default: return Block("Hour Hands", Hands.Run());
            }
        }

        // test hook: put him in a phase (and its form) without a transition, with HP inside that phase
        public void DebugSetPhase(int ph)
        {
            if (AurelionArgs.V4) { DebugSetPhaseV4(ph); return; }
            phase = Mathf.Clamp(ph, 1, 3); SecondForm = phase >= 3;
            SetPhase(phase == 1 ? "Perfect Order" : phase == 2 ? "Misalignment" : "Exposed Meridian");   // fires PhaseChanged (telemetry)
            DebugSetHpFraction(phase == 1 ? 0.9f : phase == 2 ? 0.5f : 0.2f);
        }

        IEnumerable<int> ZenithSolo()
        {
            foreach (var w in WalkToCentre()) yield return w;
            foreach (var w in Zenith.Run()) yield return w;
        }

        int V2PhaseFor() => Hp > MaxHp * V2P2At ? 1 : Hp > MaxHp * V2P3At ? 2 : 3;

        IEnumerable<int> V2Script()
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
                    // a transition waits for the current block to finish; two thresholds in one burst queue one change
                    int want = V2PhaseFor();
                    if (want > phase) { phase = want; foreach (var w in V2Shift()) yield return w; restart = true; break; }
                    if (TestHold || solo != (AurelionArgs.Known(AurelionArgs.Solo) ? AurelionArgs.Solo : null)) { restart = true; break; }
                }
                if (!restart) yield return 1;
            }
        }

        // harmless 1.5 s transformation, no heal, same HP bar; the boss stays targetable. Exposed Meridian is the second form.
        IEnumerable<int> V2Shift()
        {
            Transitions++;
            if (phase >= 3) SecondForm = true;
            foreach (var w in PhaseShift()) yield return w;
        }

        // ---- dev key (practice): F6 cycles the single-ability loop off > lances > starfall > noon > orrery > hour hands > off ----
        void V2DevKeys()
        {
            var kb = Keyboard.current;
            if (kb == null || W == null || !W.CombatActive || Dead || AurelionArgs.V1 || !kb.f6Key.wasPressedThisFrame) return;
            int i = Array.IndexOf(AurelionArgs.Ids, AurelionArgs.Solo);
            AurelionArgs.SoloOverride = i + 1 < AurelionArgs.Ids.Length ? AurelionArgs.Ids[i + 1] : "";
            RunHud.Float(transform.position + Vector3.up * (MarkHeight + 0.6f), AurelionArgs.Known(AurelionArgs.Solo) ? "practice: " + AurelionArgs.Solo : "practice: full phase plan", new Color(1f, 0.86f, 0.45f));
        }
    }
}
