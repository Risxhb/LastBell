using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastBell.Run
{
    // ================ Aurelion's ability plumbing (from ability sets v2-v3, kept for v4) ================
    // v1-v3 were retired on 1 Nov (Chzy18) and their phase plans removed on 2 Nov. What stays here is shared by the v4
    // fight (AurelionEncounter.V4.cs): ability registration, blocks and gaps, the open-chest punish, the Orrery block with
    // its Horizon, the paired-timing helpers, the single-ability practice loop (F6 / -lastbellAbility) and the test hooks.
    public sealed partial class AurelionEncounter
    {
        // ---- the abilities: one marked registration line each (ability sessions own only their own line) ----
        public MeridianLances Lances { get; private set; }
        public StarfallMirror Starfall { get; private set; }
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
            Closing = Register(new OrreryClosing());      // [ability: orrery_closing]
            Hands = Register(new HourHands());            // [ability: hour_hands]
            Zenith = Register(new ZenithCollapse());      // [ability: zenith_collapse]
            Ecliptic = Register(new EclipticOrder());     // [ability: ecliptic_order]
            Marks = Register(new ZenithMarks());          // [ability: zenith_marks]
            BindV4();                                     // v4 abilities (AurelionEncounter.V4.cs)
        }
        T Register<T>(T a) where T : AurelionAbility { a.Bind(this, W); abilities.Add(a); return a; }
        public IReadOnlyList<AurelionAbility> Abilities => abilities;
        public AurelionTelemetry Telemetry { get; private set; }   // Step 0 telemetry (hits, margins, phases, potions)

        // tests: one pass of a phase's plan, without transitions
        public IEnumerable<int> TestPhasePlan(int ph) { foreach (var b in Plan(ph)) foreach (var w in b) yield return w; }

        // ---- numbers ----
        public const float PunishS = 3.5f, StarfallIntoOrrery = 0.8f;
        public const int HorizonTicks = 240, HorizonTicksHard = 360;
        public const float GapV21 = 0.3f, LancesBeforeStar = 0.2f, CentreSpeed = 2.2f;

        // ---- state the abilities and tests read ----
        public CombatWorld World => W;
        public bool SecondForm { get; private set; }
        public int Transitions { get; private set; }
        public int Blocks { get; private set; }                    // phase-plan blocks started
        public void SetCurrent(string name) => Current = name;
        public void Glow(float k) => Windup(k);
        public void Flare(float f = 1) => Release(f);
        public void PlayClip(string name, int ticks) => Clip(name, ticks);
        // the passive Phase 2-3 gestures (planets, falling circles) never cut off an attack wind-up
        public void PlayClipIfIdle(string name, int ticks) { if (presenter && !presenter.Busy) Clip(name, ticks); }
        public Hazard Track(Hazard h) => h;
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
            AurelionTimeline.Note($"phase {phase} block {name}");
            foreach (var w in body) yield return w;
        }
        IEnumerable<int> V2Gap(float s)
        {
            Current = "recovery"; glowTarget = 0.6f;
            yield return Sim.Ticks(s);
            glowTarget = 1;
        }
        // the major punish window: no hazards, glaive planted, chest open, pendulum exposed (the OpenChest pose and clip)
        public IEnumerable<int> Punish()
        {
            BlockLog.Add((phase, "punish", W.Tick));
            int t = Sim.Ticks(PunishS);
            Current = "open chest"; pose = Pose.Exposed; glowTarget = 0.3f; Clip("OpenChest", t);
            yield return t;
            glowTarget = 1; pose = Pose.Neutral;
        }
        IEnumerable<int> WaitHorizon()
        {
            while (Closing.HorizonLive) { Current = "Horizon"; yield return 1; }
        }

        // Orrery Closing, then (Phase 2+) the Horizon with Starfall and Hour Hands inside, then the punish. grand = the
        // Grand Orrery: Starfall is cast 0.8 s into the closing instead of at the Horizon's start.
        public IEnumerable<int> OrreryBlock(int ph, bool grand)
        {
            pose = Pose.Planted;
            bool withHorizon = ph >= 2;
            int horizon = withHorizon ? HorizonFor() : 0;
            if (grand) foreach (var w in Together(Closing.Run(horizon), Sim.Ticks(StarfallIntoOrrery * Starfall.WarnScale), Starfall.Run())) yield return w;
            else
            {
                foreach (var w in Closing.Run(horizon)) yield return w;
                if (withHorizon) foreach (var w in Starfall.Run()) yield return w;
            }
            if (ph >= 2) foreach (var w in Hands.Run()) yield return w;
            foreach (var w in WaitHorizon()) yield return w;
            foreach (var w in Punish()) yield return w;
        }

        // a and b side by side, timed so b's last resolve lands bAfterA ticks after a's (either may start first)
        public static IEnumerable<int> Paired(IEnumerable<int> a, int aResolve, IEnumerable<int> b, int bResolve, int bAfterA)
        {
            int d = aResolve + bAfterA - bResolve;
            return d >= 0 ? Together(a, d, b) : Together(b, -d, a);
        }
        public IEnumerable<int> WalkToCentre() => WalkTo(Vector3.zero, CentreSpeed, Sim.Ticks(4.5f));
        // Lances resolve 0.2 s before their Starfall (which keeps its full window on Hard)
        public IEnumerable<int> LancesWithStarfall(int beams, int volleys, bool cascade) =>
            Paired(Lances.Run(beams, volleys, cascade), Lances.Duration(beams, volleys, cascade), Starfall.Run(true), Starfall.Duration(true), Sim.Ticks(LancesBeforeStar));

        // Zenith Marks resolving on the Lances' last volley, then the punish. ph 1: one volley; later: a two-step read, one
        // step per volley ("SHADE > SUN")
        public IEnumerable<int> MarksWithLances(int ph)
        {
            // 3 beams: inside the 5.5 m shade, 4 beams (eight half-lanes 45 degrees apart) would leave almost no gap
            int beams = 3, volleys = ph == 1 ? 1 : 2;
            foreach (var w in WalkToCentre()) yield return w;   // SHADE must be reachable from any rim spot (9.7 m in 3.0 s)
            if (volleys == 1) foreach (var w in Paired(Lances.Run(beams, 1, false), Lances.Duration(beams, 1, false), Marks.Run(), Marks.Duration(), 0)) yield return w;
            else
            {
                // step 0 resolves with volley 1, step 1 with volley 2
                int v1 = Lances.Duration(beams, 1, false), full = Lances.Duration(beams, 2, false), d = Marks.Duration() - v1;
                foreach (var w in Together(Marks.Run(d + full), d, Lances.Run(beams, 2, false))) yield return w;
            }
            foreach (var w in Punish()) yield return w;
        }

        IEnumerable<IEnumerable<int>> Plan(int ph) => Plan4(ph);

        // one ability on its own, on loop (-lastbellAbility <id> or F6); its phase variant follows the HP phase
        public IEnumerable<int> SoloBlock(string id)
        {
            switch (id)
            {
                case "aurelion.meridian_lances": return Block("Meridian Lances", Lances.Run(phase == 1 ? 3 : 4, phase == 1 ? 1 : 2, phase == 3));
                case "aurelion.starfall_mirror": return Block("Starfall Mirror", Starfall.Run());
                case "aurelion.orrery_closing": return Block("Orrery Closing", OrreryBlock(phase, phase == 3));
                case "aurelion.zenith_collapse": return Block("Zenith Collapse", ZenithSolo());
                case "aurelion.ecliptic_order": return Block("Ecliptic Order", Ecliptic.Run(phase == 1 ? 3 : 4));
                case "aurelion.zenith_marks": return Block("Zenith Marks + Meridian Lances", MarksWithLances(phase));
                case "aurelion.meridian_towers": return Block("Meridian Towers", Towers.Run());
                case "aurelion.sunspots": return Block("Sunspots", Spots.Run());
                case "aurelion.total_eclipse": return Block("Total Eclipse", TotalEclipseFx.During(W, Eclipse, Eclipse.Run()));
                case "aurelion.severance": return Block("Severance", Sever.Run());
                case "aurelion.last_light": return Block("Last Light", Together(LastLightFloorFx.During(W, Light, Light.Run()), 0, LastLightRotation()));
                case "aurelion.meridian_recall": return Block("Meridian Lances + Meridian Recall", LancesThenRecall());
                default: return Block("Hour Hands", Hands.Run());
            }
        }

        // test hook: put him in a phase (and its form) without a transition, with HP inside that phase
        public void DebugSetPhase(int ph) => DebugSetPhaseV4(ph);

        IEnumerable<int> ZenithSolo()
        {
            foreach (var w in WalkToCentre()) yield return w;
            foreach (var w in Zenith.Run()) yield return w;
        }

        // practice phase row: DebugSetPhaseV4's state with HP at the top of the band
        public override float[] PhaseStarts => new[] { 1f, V4P2At, V4P3At, V4P4At };
        public override int PhaseNumber => phase;
        protected override void EnterPhase(int n)
        {
            phase = n; SecondForm = n >= 3; IntermissionDone = true;   // a fresh boss: no pools yet
            SetPhase(n == 2 ? "Misalignment" : n == 3 ? "Exposed Meridian" : "Last Light");
        }

        // ---- dev key (practice): F6 cycles the single-ability loop off > each ability in turn > off ----
        void V2DevKeys()
        {
            var kb = Keyboard.current;
            if (kb == null || W == null || !W.CombatActive || Dead || !kb.f6Key.wasPressedThisFrame) return;
            int i = Array.IndexOf(AurelionArgs.Ids, AurelionArgs.Solo);
            AurelionArgs.SoloOverride = i + 1 < AurelionArgs.Ids.Length ? AurelionArgs.Ids[i + 1] : "";
            RunHud.Float(transform.position + Vector3.up * (MarkHeight + 0.6f), AurelionArgs.Known(AurelionArgs.Solo) ? "practice: " + AurelionArgs.Solo : "practice: full phase plan", new Color(1f, 0.86f, 0.45f));
        }
    }
}
