using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // The Bellkeeper Ultimate (proposal, bosses/4-shattered-hour-bellkeeper/ultimate/ULTIMATE.md): ability set "ultimate",
    // chosen by the practice entry "Bellkeeper — Ultimate", -lastbellUltimate or -lastbellAbilitySet ultimate. Easy /
    // Medium / Hard keep v2; the Ultimate has one tuning (Medium numbers, ULTIMATE.md §2).
    //
    // Structure (§5): P1 Last Guardian -> T1 The Hour Stops -> P2 Broken Hour -> second form (55 %) -> P3 The Open Hour ->
    // T2 Thirteen Chimes -> P4 The Unfinished Hour -> P5 Final Toll. A phase repeats its loop until its HP threshold, then
    // the transition waits for the current moves to finish. HP can't fall below the running segment's floor (a phase can't
    // be skipped); the boss never heals. Push timers (§5) wipe the party if a phase's threshold isn't reached in time; P5
    // ends in the Thirteenth Hour's hard enrage at 78 s (the plan enforces it too, 0.5 s later, as a fallback).
    // Practice: -lastbellUltimatePhase N (1-5, or T1 / T2) starts there, practice only; a real attempt starts at P1.
    // The mechanics (Ultimate/<Mechanic>.cs) and the reworked v2 moves (Leaning Bell, Toll Tide, Reflection) are launched
    // by the timelines below with cues (UltCue) and per-cast tuning (CastTuning); times are seconds from the loop's start.
    public sealed partial class BellkeeperEncounter
    {
        public UltimateState Ult { get; private set; }
        // Placeholder until the bots measure DPS (ULTIMATE.md §9, Part 5): HP per phase = measured DPS x target length.
        public static int UltimateBaseHp = 30000;

        public enum UltSegment { P1, T1, P2, P3, T2, P4, P5 }
        public static readonly string[] SegmentNames =
            { "P1 · Last Guardian", "T1 · The Hour Stops", "P2 · Broken Hour", "P3 · The Open Hour", "T2 · Thirteen Chimes", "P4 · The Unfinished Hour", "P5 · Final Toll" };
        static readonly float[] SegStart = { 1f, 0.85f, 0.85f, 0.55f, 0.35f, 0.35f, 0.15f };   // practice start HP
        static readonly float[] SegFloor = { 0.85f, 0.55f, 0.55f, 0.35f, 0.15f, 0.15f, 0f };   // HP floor while it runs
        static readonly float[] SegEnd = { 0.85f, -1, 0.55f, 0.35f, -1, 0.15f, 0f };          // HP that ends it (-1 = timed)
        static readonly int[] SegLook = { 1, 1, 2, 3, 3, 3, 3 };                               // phase look: shell closed / parted / open chest
        public static float[] PushSeconds = { 130, 0, 150, 130, 0, 130, 78 };                   // push timer / enrage per segment, 0 = none
        public const float EnrageGrace = 0.5f;
        public static bool PushTimersOn = true;                                                 // tests switch them off for long checks

        public UltSegment Segment { get; private set; }
        public int SegmentStartTick { get; private set; } = -1;
        public int SegmentLoops { get; private set; }
        public bool UltPlanActive { get; private set; }
        public int SecondsInSegment => SegmentStartTick < 0 ? 0 : (W.Tick - SegmentStartTick) / Sim.Hz;
        public float PushLeft => SegmentStartTick < 0 || PushSeconds[(int)Segment] <= 0 ? -1 : PushSeconds[(int)Segment] - (W.Tick - SegmentStartTick) / (float)Sim.Hz;
        public readonly List<(int tick, UltSegment seg)> SegmentLog = new List<(int, UltSegment)>();
        bool pushFired;

        // practice start: -lastbellUltimatePhase 1..5 | T1 | T2 (tests set PhaseOverride)
        public static string PhaseOverride;
        public static UltSegment? RequestedSegment => ParseSegment(PhaseOverride ?? Arg("-lastbellUltimatePhase"));
        public static UltSegment? ParseSegment(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim().ToUpperInvariant();
            switch (s)
            {
                case "1": case "P1": return UltSegment.P1;
                case "T1": return UltSegment.T1;
                case "2": case "P2": return UltSegment.P2;
                case "3": case "P3": return UltSegment.P3;
                case "T2": return UltSegment.T2;
                case "4": case "P4": return UltSegment.P4;
                case "5": case "P5": return UltSegment.P5;
            }
            return null;
        }
        static bool IsPractice => RunDirector.Instance == null || RunDirector.Instance.Mode == RunMode.Practice;

        // ---------------- build ----------------
        LeaningBellAbility uLean; ReflectionAbility uMirror; TollTideAbility uTide;
        PendulumClocks mClocks; StoppedHour mStopped; LedgerMechanic mLedger; BackwardsBell mBackwards; TwoFutures mFutures;
        ConductorsHand mHand; TollingHour mTolling; ThirteenthHour mThirteenth; AreaQuotes mQuotes; FractureOfTime mFracture;
        public IReadOnlyList<UltimateMechanic> Mechanics { get; private set; } = Array.Empty<UltimateMechanic>();

        void BuildUltimate()
        {
            Ult = new UltimateState(this);
            var ult = UltimateMechanics.CreateAll();
            uLean = new LeaningBellAbility(); uMirror = new ReflectionAbility(); uTide = new TollTideAbility();
            abilities = new BellkeeperAbility[] { uLean, uMirror, uTide }.Concat(ult).ToArray();
            Mechanics = ult;
            foreach (var a in abilities) a.Bind(this);
            mClocks = Mech<PendulumClocks>(); mStopped = Mech<StoppedHour>(); mLedger = Mech<LedgerMechanic>(); mBackwards = Mech<BackwardsBell>();
            mFutures = Mech<TwoFutures>(); mHand = Mech<ConductorsHand>(); mTolling = Mech<TollingHour>(); mThirteenth = Mech<ThirteenthHour>();
            mQuotes = Mech<AreaQuotes>(); mFracture = Mech<FractureOfTime>();
            // one tuning (ULTIMATE.md §2): Easy x0.6 / Hard x1.3 don't apply to the Ultimate
            if (W.Difficulty != Difficulty.Medium) { Debug.Log($"Bellkeeper Ultimate: one tuning; the run's {RunDifficulty.Name(W.Difficulty)} becomes Medium for this fight"); W.Difficulty = Difficulty.Medium; }
            W.DealtScale = p => Ult != null && Ult.IsCracked(p) ? 0f : 1f;
            MaxHp = Hp = UltimateBaseHp;
            PhaseName = SegmentNames[0];
            var key = RequestedAbility;
            if (key != null)
            {
                PracticeAbility = Ability(key);
                if (PracticeAbility == null) Debug.LogWarning($"Bellkeeper Ultimate: -lastbellAbility {key} is not an Ultimate move ({string.Join(", ", Array.ConvertAll(abilities, a => a.Key))}); running the plan");
            }
            if (RequestedBots > W.Players.Count) ScriptedParty.AddStandIns(W, RequestedBots - W.Players.Count);
        }
        T Mech<T>() where T : UltimateMechanic => (T)Mechanics.First(m => m is T);
        public T Mechanic<T>() where T : UltimateMechanic => Mechanics.OfType<T>().FirstOrDefault();

        // the Ultimate tuning of the reworked v2 moves (ULTIMATE.md §4). `warn` = ticks from launch to the strike: the v2
        // moves spawn on their launch tick with ResolveAt = their warning, which strikes one tick early, so the warnings
        // go through UltShapes.ResolveIn (the Heart Toll's ResolveAt already counts its spawn tick).
        public static BellkeeperAbility.CastTuning LeanTuning(Vector3? dir = null, int warn = UltShapes.LeanTicks) => new BellkeeperAbility.CastTuning { WarnTicks = UltShapes.ResolveIn(warn), Damage = UltShapes.LeanDamage, Dir = dir };
        public static BellkeeperAbility.CastTuning TideTuning() => new BellkeeperAbility.CastTuning { WarnTicks = UltShapes.ResolveIn(UltShapes.TideTicks), SecondTicks = UltShapes.HeartTicks, Damage = UltShapes.StandardDamage, SecondDamage = UltShapes.StandardDamage };
        public static BellkeeperAbility.CastTuning HeartTuning(int warn = UltShapes.HeartTicks) => new BellkeeperAbility.CastTuning { SecondOnly = true, SecondTicks = warn, SecondDamage = UltShapes.StandardDamage };
        public static BellkeeperAbility.CastTuning MirrorTuning(int warn = 240) => new BellkeeperAbility.CastTuning { WarnTicks = UltShapes.ResolveIn(warn), Damage = UltShapes.StandardDamage };

        // Launch helpers for the plan and for mechanics that call other moves (the Conductor's Hand, Backwards Bell).
        public bool UltLaunch(UltimateMechanic m, UltCue cue = null)
        {
            if (m == null || Dead) return false;
            if (IsRunning(m)) { Debug.LogWarning($"Bellkeeper Ultimate: {m.Key} is still running; launch skipped ({cue})"); PlanLog.Add((W.Tick, "skip " + m.Key)); return false; }
            m.NextCue = cue; Launch(m); return true;
        }
        public bool UltMove(BellkeeperAbility a, BellkeeperAbility.CastTuning t)
        {
            if (a == null || Dead) return false;
            if (IsRunning(a)) { Debug.LogWarning($"Bellkeeper Ultimate: {a.Key} is still running; launch skipped"); PlanLog.Add((W.Tick, "skip " + a.Key)); return false; }
            a.NextTuning = t; Launch(a); return true;
        }
        public LeaningBellAbility UltLean => uLean;
        public TollTideAbility UltTide => uTide;
        public ReflectionAbility UltMirror => uMirror;

        // every hazard an ability spawns passes here (the ledger recorder listens; tests too)
        public event Action<BellkeeperAbility, Hazard> HazardSpawned;
        internal void NotifySpawned(BellkeeperAbility a, Hazard h) { Ult?.OnSpawned(a, h); HazardSpawned?.Invoke(a, h); }

        // ---------------- per tick ----------------
        void UltimateTick()
        {
            if (!UltPlanActive || Dead || SegmentStartTick < 0 || pushFired || !PushTimersOn) return;
            float push = PushSeconds[(int)Segment]; if (push <= 0) return;
            float grace = Segment == UltSegment.P5 ? EnrageGrace : 0;
            if (W.Tick - SegmentStartTick < Sim.Ticks(push + grace)) return;
            if (Segment != UltSegment.P5 && Hp <= Mathf.RoundToInt(MaxHp * SegEnd[(int)Segment])) return;   // reached in time
            pushFired = true;
            Ult.Wipe(Segment == UltSegment.P5 ? "The Thirteenth Hour" : "The hour runs out");
        }
        protected override int HpFloor => Set == AbilitySet.Ultimate && UltPlanActive ? Mathf.RoundToInt(MaxHp * SegFloor[(int)Segment]) : 0;
        bool Reached(UltSegment s) => Hp <= Mathf.RoundToInt(MaxHp * SegEnd[(int)s]);

        // ---------------- the plan ----------------
        IEnumerable<int> UltimatePlan()
        {
            UltPlanActive = true; pushFired = false;
            Ult.ResetAll();
            FitHpToParty();
            var start = IsPractice && RequestedSegment.HasValue ? RequestedSegment.Value : UltSegment.P1;
            if (start != UltSegment.P1) { Hp = Mathf.RoundToInt(MaxHp * SegStart[(int)start]); PlanLog.Add((W.Tick, "practice from " + start)); }
            for (var s = start; s <= UltSegment.P5; s++)
            {
                if (s == UltSegment.P3 && start < UltSegment.P3) foreach (var w in SecondForm()) yield return w;
                Begin(s);
                foreach (var w in RunSegment(s)) yield return w;
                // the transition waits for the current moves to finish (rules: no transition mid-telegraph)
                while (running.Count > 0 || W.Hazards.Count > 0) yield return 1;
            }
            while (true) yield return 60;   // P5 never ends by itself: the boss dies or the Thirteenth Hour wipes
        }

        void Begin(UltSegment s)
        {
            Segment = s; SegmentStartTick = W.Tick; SegmentLoops = 0; pushFired = false;
            SegmentLog.Add((W.Tick, s));
            PlanLog.Add((W.Tick, "segment " + s));
            phase = SegLook[(int)s];
            Ult.Reset();
            SetPhase(SegmentNames[(int)s]);
        }

        IEnumerable<int> RunSegment(UltSegment s)
        {
            switch (s)
            {
                case UltSegment.P1: do { foreach (var w in P1Loop()) yield return w; } while (!Reached(s)); break;
                case UltSegment.T1: foreach (var w in T1()) yield return w; break;
                case UltSegment.P2: do { foreach (var w in P2Loop()) yield return w; } while (!Reached(s)); break;
                case UltSegment.P3: do { foreach (var w in P3Loop()) yield return w; } while (!Reached(s)); break;
                case UltSegment.T2: foreach (var w in T2()) yield return w; break;
                case UltSegment.P4: do { foreach (var w in P4Loop()) yield return w; } while (!Reached(s)); break;
                default:
                    UltLaunch(mFracture, new UltCue { Phase = 5 });
                    UltLaunch(mThirteenth, new UltCue { Phase = 5, At = new[] { PushSeconds[(int)UltSegment.P5] } });
                    while (true) foreach (var w in P5Loop()) yield return w;
            }
        }

        // second form at 55 %: a harmless 2 s transformation, same bar, no heal
        IEnumerable<int> SecondForm()
        {
            Current = "second form"; PlanLog.Add((W.Tick, "second form"));
            if (presenter) presenter.PlayOnce("PhaseShift", 2f);
            glowTarget = 3f; phase = 3;
            for (int i = 0; i < 120; i++) { if (i == 60) { W.Fx.Crescendo(transform.position, true, 0, false); Release(1.5f); } yield return 1; }
            glowTarget = 1;
        }

        // Runs `events` at their times (seconds from now, each on its exact tick), then waits until `end` seconds.
        IEnumerable<int> Timeline(float end, params (float at, Action act)[] events)
        {
            int t0 = W.Tick;
            foreach (var (at, act) in events.OrderBy(e => e.at))
            {
                int due = t0 + Sim.Ticks(at);
                while (W.Tick < due) yield return 1;
                if (Dead) yield break;
                act();
            }
            while (W.Tick < t0 + Sim.Ticks(end)) yield return 1;
        }
        (float, Action) At(float t, UltimateMechanic m, UltCue cue) => (t, () => UltLaunch(m, cue));
        (float, Action) At(float t, BellkeeperAbility a, Func<BellkeeperAbility.CastTuning> tune) => (t, () => UltMove(a, tune()));
        UltCue C(int phaseNo, string variant, params float[] at) => new UltCue { Phase = phaseNo, Variant = variant, At = at, Seed = Seed() };
        int Seed() => Mathf.Abs((int)Segment * 7919 + SegmentLoops * 104729 + W.Players.Count * 31);

        IEnumerable<int> UltPunish(int ticks = PunishTicks)
        {
            PunishWindows++; PlanLog.Add((W.Tick, "punish"));
            foreach (var w in Vulnerable(ticks)) yield return w;
        }
        // the hit half's direction for a Leaning Bell that resolves with a clock hour: toward that hour's station(s)
        // (pairing rule, ULTIMATE.md §3.1), resolved on the launch tick (the clocks are already assigned)
        Vector3? LeanTowardHourAt(float secondsFromNow) => mClocks.LeanDirAt(W.Tick + Sim.Ticks(secondsFromNow));

        // §5.1 P1 Last Guardian (one loop 31 s): Tolling x2, clocks one hour at a time, the Ledger records the first loop.
        IEnumerable<int> P1Loop()
        {
            SegmentLoops++;
            bool first = SegmentLoops == 1;
            var events = new List<(float, Action)>
            {
                At(0, mTolling, C(1, "tolls", 0, 1)),
                At(3, mClocks, C(1, "singles", 9, 14, 19, 24)),                    // hours I..IV strike at 12, 17, 22, 27
                At(5, uLean, () => LeanTuning()),                                   // Ledger I (8.5)
                At(9, uTide, TideTuning),                                           // Ledger II tide (11.5), III Heart Toll (13.5)
                At(15, uMirror, () => MirrorTuning()),                              // Ledger IV (19)
                (18.5f, () => UltMove(uLean, LeanTuning(LeanTowardHourAt(3.5f)))),  // Ledger V with clock III (22): simultaneous
                At(25, uTide, () => HeartTuning()),                                 // Ledger VI with clock IV (27): simultaneous
            };
            if (first) events.Add(At(0, mLedger, C(1, "record")));
            foreach (var w in Timeline(27, events.ToArray())) yield return w;
            foreach (var w in UltPunish()) yield return w;
            yield return 30;
        }

        // §5.2 T1 The Hour Stops (25 s): the Stopped Hour alone, twice (glyph 0, stop 5-9, lash 9), then a punish.
        IEnumerable<int> T1()
        {
            foreach (var w in Timeline(21.5f, At(0, mStopped, C(0, "alone", 5, 9)), At(12, mStopped, C(0, "alone", 5, 9)))) yield return w;
            foreach (var w in UltPunish()) yield return w;
        }

        // §5.3 P2 Broken Hour (loop 34 s): the Conductor's Hand, clocks in pairs, Backwards Bell, Stopped Hour + Heart Toll.
        IEnumerable<int> P2Loop()
        {
            SegmentLoops++;
            foreach (var w in Timeline(30,
                At(0, mHand, C(2, "phrase", 7, 8.5f)),                 // phrase at 0, moves resolve 7.0 and 8.5
                At(6, mClocks, C(2, "pairs", 6, 12.5f)),               // I+II at 12, III+IV at 18.5 (with rewind A)
                At(8, mBackwards, C(2, "block", 3.5f, 5, 6.5f)),       // A 11.5, B 13, C 14.5; rewinds C 16.5, B 17.5, A 18.5
                At(20, mStopped, C(2, "lash", 5, 9)),                  // stop 25-29, lash 29
                At(27, uTide, () => HeartTuning())))                   // Heart Toll 29 with the lash: simultaneous
                yield return w;
            foreach (var w in UltPunish()) yield return w;
        }

        // §5.4 P3 The Open Hour (loop 32 s): Tolling x3, Two Futures (last step with a Heart Toll), clocks, and the
        // resume tick at 27 = Stilled lashes + Reflection images + clock IV (three rules on one tick).
        IEnumerable<int> P3Loop()
        {
            SegmentLoops++;
            foreach (var w in Timeline(28,
                At(0, mTolling, C(3, "tolls", 0, 1, 2)),
                At(3, mFutures, C(3, "futures", 7)),                   // traced 3-7.8, verdict 8.5, steps 10.0 .. 16.0
                At(14, uTide, () => HeartTuning()),                    // Heart Toll 16.0 with the last step
                At(12, mClocks, C(3, "singles", 6, 9, 12, 15)),        // hours at 18, 21, 24, 27
                At(18, mStopped, C(3, "lash", 5, 9)),                  // glyph 18, stop 23-27, resume 27
                At(23, uMirror, () => MirrorTuning())))                // images 27
                yield return w;
            foreach (var w in UltPunish()) yield return w;
        }

        // §5.5 T2 Thirteen Chimes (30 s): six tolls (72: enter above 72 HP), then one reworked quote of each area, then a punish.
        IEnumerable<int> T2()
        {
            foreach (var w in Timeline(25.5f,
                At(3, mTolling, C(0, "tolls", 0, 1, 2, 3, 4, 5)),
                At(10, mQuotes, C(0, "quotes", 4, 9, 14))))              // Spire 14, Foundry 19, Verdant 24 (resolve times)
                yield return w;
            foreach (var w in UltPunish()) yield return w;
        }

        // §5.6 P4 The Unfinished Hour (loop 54 s): the Ledger replays VI -> I three times, each with something new on top.
        IEnumerable<int> P4Loop()
        {
            SegmentLoops++;
            // (1) with clocks in pairs, striking 7.75 and 10.75 so they lock between replayed strikes (V at 5.5 would force a 60)
            foreach (var w in Timeline(13, At(0, mLedger, C(4, "replay", 4)), At(0, mClocks, C(4, "pairs", 7.75f, 10.75f)))) yield return w;
            foreach (var w in UltPunish()) yield return w;
            // (2) with a Stopped Hour whose resume (9.0) lands on Ledger III (the 4th replayed strike: 0.5 + 4 + 3 x 1.5 = 9.0 s)
            foreach (var w in Timeline(14, At(0, mStopped, C(4, "lash", 5, 9)), At(0.5f, mLedger, C(4, "replay", 4)))) yield return w;
            foreach (var w in UltPunish()) yield return w;
            // (3) with the Conductor's Hand phrase
            foreach (var w in Timeline(13, At(0, mHand, C(4, "phrase", 7, 9.5f)), At(0, mLedger, C(4, "replay", 4)))) yield return w;
            foreach (var w in UltPunish()) yield return w;
        }

        // §5.7 P5 Final Toll (loop 36 s, enrage at 78 s): the fissure burns, the great bell counts. Phrase -> Two Futures
        // with clock pairs on steps 2 and the resume tick -> Stopped Hour resuming with Reflection, Heart Toll and a clock pair
        // (four rules, one tick) -> Leaning Bell across the fissure -> punish.
        IEnumerable<int> P5Loop()
        {
            SegmentLoops++;
            foreach (var w in Timeline(31.5f,
                At(0, mHand, C(5, "phrase", 7, 8.5f)),
                At(9, mFutures, C(5, "futures", 7)),                   // steps 16.0, 17.2 ... 22.0
                At(10, mClocks, C(5, "pairs", 7.2f, 17)),              // pairs at 17.2 (with step 2) and 27 (the resume)
                At(18, mStopped, C(5, "lash", 5, 9)),                  // stop 23-27, resume 27
                At(23, uMirror, () => MirrorTuning()),                 // images 27
                At(25, uTide, () => HeartTuning()),                    // Heart Toll 27
                (28f, () => UltMove(uLean, LeanTuning(FissureLean()))))) // across the fissure, 31.5
                yield return w;
            foreach (var w in UltPunish()) yield return w;
        }
        // P5 Leaning Bells use the fissure as their line; the bell falls on the half where most of the party stands
        Vector3 FissureLean()
        {
            var n = UltimateState.FissureNormal; int plus = 0, minus = 0;
            foreach (var p in W.Players) { if (p.Down) continue; float s = Vector3.Dot(Sim.Flat(p.transform.position), n); if (s > 0) plus++; else minus++; }
            return minus > plus ? -n : n;
        }

        // ---------------- HUD ----------------
        public override string HudLine
        {
            get
            {
                if (Set != AbilitySet.Ultimate || !UltPlanActive || SegmentStartTick < 0) return null;
                float left = PushLeft;
                string push = left < 0 ? "" : Segment == UltSegment.P5 ? $"   ·   XIII in {Mathf.Max(0, left):0}s" : $"   ·   push {Mathf.Max(0, left) / 60:0}:{Mathf.Max(0, left) % 60:00}";
                return "ULTIMATE · " + SegmentNames[(int)Segment] + push;
            }
        }
        public override float[] HudMarks => Set == AbilitySet.Ultimate ? new[] { 0.85f, 0.55f, 0.35f, 0.15f } : null;
        public override float NextPhaseFraction() => Set == AbilitySet.Ultimate ? Mathf.Max(0.01f, SegEnd[(int)Segment] - 0.01f) : base.NextPhaseFraction();

        static GUIStyle glyphStyle;
        void OnGUI()
        {
            if (Set != AbilitySet.Ultimate || Ult == null || W == null || Dead) return;
            var cam = Camera.main; if (!cam) return;
            if (glyphStyle == null) glyphStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.96f, 0.93f, 0.84f) } };
            foreach (var p in W.Players)
            {
                var text = Ult.GlyphText(p, W.Tick); if (text == null) continue;
                var sp = cam.WorldToScreenPoint(p.transform.position + Vector3.up * 3.4f); if (sp.z <= 0) continue;
                GUI.Label(new Rect(sp.x - 90, Screen.height - sp.y - 12, 180, 24), text, glyphStyle);
            }
        }
    }
}
