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
        // ---- the five abilities: one marked registration line each (ability sessions own only their own line) ----
        public MeridianLances Lances { get; private set; }
        public StarfallMirror Starfall { get; private set; }
        public NoonShadow Noon { get; private set; }
        public OrreryClosing Closing { get; private set; }
        public HourHands Hands { get; private set; }
        readonly List<AurelionAbility> abilities = new List<AurelionAbility>();

        void BindAbilities()
        {
            if (abilities.Count > 0) return;
            Lances = Register(new MeridianLances());      // [ability: meridian_lances]
            Starfall = Register(new StarfallMirror());    // [ability: starfall_mirror]
            Noon = Register(new NoonShadow());            // [ability: noon_shadow]
            Closing = Register(new OrreryClosing());      // [ability: orrery_closing]
            Hands = Register(new HourHands());            // [ability: hour_hands]
        }
        T Register<T>(T a) where T : AurelionAbility { a.Bind(this, W); abilities.Add(a); return a; }
        public IReadOnlyList<AurelionAbility> Abilities => abilities;

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

        // Orrery Closing, then (Phase 2+) the Horizon with Starfall inside, Hour Hands on Hard, then the punish.
        // grand = Phase 3's Grand Orrery: Starfall is cast 0.8 s into the closing instead of at the Horizon's start.
        public IEnumerable<int> OrreryBlock(int ph, bool grand)
        {
            pose = Pose.Planted;
            int horizon = ph >= 2 ? HorizonFor() : 0;
            if (grand) foreach (var w in Together(Closing.Run(horizon), Sim.Ticks(StarfallIntoOrrery * Starfall.WarnScale), Starfall.Run())) yield return w;
            else
            {
                foreach (var w in Closing.Run(horizon)) yield return w;
                if (ph >= 2) foreach (var w in Starfall.Run()) yield return w;
            }
            if (ph >= 2 && W.Difficulty == Difficulty.Hard) foreach (var w in Hands.Run()) yield return w;
            foreach (var w in WaitHorizon()) yield return w;
            foreach (var w in Punish()) yield return w;
        }

        public IEnumerable<int> NoonWithStarfall() =>
            Together(Noon.Run(false), Sim.Ticks(StarfallIntoNoon * Noon.WarnScale), Starfall.Run());

        IEnumerable<IEnumerable<int>> Plan(int ph)
        {
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
                default: return Block("Hour Hands", Hands.Run());
            }
        }

        // test hook: put him in a phase (and its form) without a transition, with HP inside that phase
        public void DebugSetPhase(int ph)
        {
            phase = Mathf.Clamp(ph, 1, 3); SecondForm = phase >= 3;
            PhaseName = phase == 1 ? "Perfect Order" : phase == 2 ? "Misalignment" : "Exposed Meridian";
            DebugSetHpFraction(phase == 1 ? 0.9f : phase == 2 ? 0.5f : 0.2f);
        }

        int V2PhaseFor() => Hp > MaxHp * V2P2At ? 1 : Hp > MaxHp * V2P3At ? 2 : 3;

        IEnumerable<int> V2Script()
        {
            BindAbilities();
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
