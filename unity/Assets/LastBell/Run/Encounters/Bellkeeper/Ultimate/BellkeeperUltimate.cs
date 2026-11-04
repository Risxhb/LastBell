using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // The Bellkeeper Ultimate (proposal; specs in bosses/4-shattered-hour-bellkeeper/ultimate/: P1-v2-LOCKED.md, P2-v2-LOCKED.md, P5-v2-PROPOSAL.md): the run's final
    // boss (RunDirector's final entry spawns it at the run's level). The only Bellkeeper fight: the archived v1 fight and the
    // v1 Ultimate plan were deleted (Chzy18, 1 Nov).
    //
    // Structure: P1 Last Guardian (Escapement Relay + Chime Debts) -> P2 Broken Hour -> second form -> P3 The Open Hour ->
    // P4 The Gear Floor -> P5 Final Toll. A phase runs until its HP threshold (and its minimum length), then the transition
    // waits for the current moves to finish. HP can't fall below the running phase's floor (a phase can't be skipped); the
    // boss never heals. Push timers wipe the party if a phase's threshold isn't reached in time; P5 ends at toll XIII
    // (FinalToll). Practice: -lastbellUltimatePhase N (1-5) starts there, practice only; a real attempt starts at P1.
    public sealed partial class BellkeeperEncounter
    {
        public UltimateState Ult { get; private set; }
        public static int UltimateBaseHp = DefaultBaseHp;
        // Set from the Part 5 bot sweeps (clears-par-6)
        public const int DefaultBaseHp = 12000;
        // solo gets a smaller bar: one player carries every duty. 0.25 since 28 Oct (was 0.65): v2 solo must clear with every class on
        // every level (gpt/08 §5), and a solo Vanguard reaches the boss for well under half a second of each 5 s P1 cycle (the plate is
        // 7 m out every other beat), so at 0.65 it ran out of P1's time at 92 %; 0.22 cleared P1 and P2 on their minimum lengths. Phase
        // minimums keep a ranged solo near 6 min. Solo only: parties are unchanged. -lastbellUltSolo overrides
        public static float UltimateSoloScale = float.TryParse(Arg("-lastbellUltSolo"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ss) ? ss : 0.25f;

        public enum UltSegment { P1, P2, P3, P4, P5 }
        public static readonly string[] SegmentNames =
            { "P1 · Last Guardian", "P2 · Broken Hour", "P3 · The Open Hour", "P4 · The Unfinished Hour", "P5 · Final Toll" };
        static readonly float[] SegStart = { 1f, 0.85f, 0.70f, 0.55f, 0.35f };   // practice start HP
        static readonly float[] SegEnd = { 0.85f, 0.70f, 0.55f, 0.35f, 0f };     // HP that ends it, and its floor while it runs
        static readonly int[] SegLook = { 1, 2, 3, 3, 3 };                       // phase look: shell closed / parted / open chest
        static readonly float[] PushBase = { 150, 150, 150, 180, 0 };            // push timer per phase (s, Hard), 0 = none (P5: toll XIII)
        public const int V2P1MinLoops = 2;          // P1 shows two debt rounds before it can end (72 s), whatever the DPS
        public const float V2P2MinSeconds = 60;     // P2: at least 60 s (20 beats)
        public const float V2P3MinSeconds = 60;     // P3: at least one 60 s pattern
        public const float V2P4MinSeconds = 60;     // P4: at least 8 slams
        public static bool PushTimersOn = true;     // tests switch them off for long checks
        public static bool V2StopAfterP4;           // tests: P4 checks end at P4
        // P5 Final Toll (proposal, Chzy18 25 Oct: "Bell resonates x2", then "a mix of 2 and 3"): inside the bell hits ring it
        // (x V2P5Resonance), from toll IX only the Bellkeeper that rang last takes damage, and the last stand counts x4
        public const float V2P5Resonance = 1.25f;

        public UltSegment Segment { get; private set; }
        public int SegmentStartTick { get; private set; } = -1;
        public int SegmentLoops { get; private set; }
        public bool UltPlanActive { get; private set; }
        public int SecondsInSegment => SegmentStartTick < 0 ? 0 : (W.Tick - SegmentStartTick) / Sim.Hz;
        public float PushLeft => SegmentStartTick < 0 || PushFor(Segment) <= 0 ? -1 : PushFor(Segment) - (W.Tick - SegmentStartTick) / (float)Sim.Hz;
        public readonly List<(int tick, UltSegment seg)> SegmentLog = new List<(int, UltSegment)>();
        bool pushFired;

        // practice start: -lastbellUltimatePhase 1..5 (tests set PhaseOverride)
        public static string PhaseOverride;
        public static UltSegment? RequestedSegment => ParseSegment(PhaseOverride ?? Arg("-lastbellUltimatePhase"));
        public static UltSegment? ParseSegment(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim().ToUpperInvariant();
            switch (s)
            {
                case "1": case "P1": return UltSegment.P1;
                case "2": case "P2": return UltSegment.P2;
                case "3": case "P3": return UltSegment.P3;
                case "4": case "P4": return UltSegment.P4;
                case "5": case "P5": return UltSegment.P5;
            }
            return null;
        }
        static bool IsPractice => RunDirector.Instance == null || RunDirector.Instance.Mode == RunMode.Practice;

        // ---------------- build ----------------
        EscapementRelay mRelay; ChimeDebts mDebts; BrokenHour mBroken; OpenHour mOpen; GearFloor mGear; FinalToll mFinal;
        public IReadOnlyList<UltimateMechanic> Mechanics { get; private set; } = Array.Empty<UltimateMechanic>();

        void BuildUltimate()
        {
            Ult = new UltimateState(this);
            var ult = UltimateMechanics.CreateAll();
            abilities = ult.ToArray<BellkeeperAbility>();
            Mechanics = ult;
            foreach (var a in abilities) a.Bind(this);
            mRelay = Mech<EscapementRelay>(); mDebts = Mech<ChimeDebts>(); mBroken = Mech<BrokenHour>(); mOpen = Mech<OpenHour>(); mGear = Mech<GearFloor>(); mFinal = Mech<FinalToll>();
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

        public bool UltLaunch(UltimateMechanic m, UltCue cue = null)
        {
            if (m == null || Dead) return false;
            if (IsRunning(m)) { Debug.LogWarning($"Bellkeeper Ultimate: {m.Key} is still running; launch skipped ({cue})"); PlanLog.Add((W.Tick, "skip " + m.Key)); return false; }
            m.NextCue = cue; Launch(m); return true;
        }

        // every hazard an ability spawns passes here (tests listen)
        public event Action<BellkeeperAbility, Hazard> HazardSpawned;
        internal void NotifySpawned(BellkeeperAbility a, Hazard h) { HazardSpawned?.Invoke(a, h); }

        // ---------------- per tick ----------------
        void UltimateTick()
        {
            if (!UltPlanActive || Dead || SegmentStartTick < 0 || pushFired || !PushTimersOn) return;
            float push = PushFor(Segment); if (push <= 0) return;
            if (W.Tick - SegmentStartTick < Sim.Ticks(push)) return;
            if (Reached(Segment)) return;   // reached in time
            pushFired = true;
            Ult.Wipe("The hour runs out");
        }
        float PushFor(UltSegment s) => UltTimerFor(W.Difficulty) * PushBase[(int)s];
        protected override int HpFloor => UltPlanActive ? Mathf.RoundToInt(MaxHp * SegEnd[(int)Segment]) : 0;
        bool Reached(UltSegment s) => Hp <= Mathf.RoundToInt(MaxHp * SegEnd[(int)s]);

        // ---------------- the plan ----------------
        IEnumerable<int> UltimatePlan()
        {
            UltPlanActive = true; pushFired = false;
            Ult.ResetAll(); mRelay.ResetBeats(); mBroken.ResetBeats(); mOpen.ResetCounts(); mGear.ResetCounts(); mFinal.ResetCounts();
            FitHpToParty();
            var start = pickedSegment ?? (IsPractice && RequestedSegment.HasValue ? RequestedSegment.Value : UltSegment.P1);
            if (pickedSegment.HasValue) PlanLog.Add((W.Tick, "practice phase row from " + start));   // UltEnterPhase set the HP
            else if (start != UltSegment.P1) { Hp = Mathf.RoundToInt(MaxHp * SegStart[(int)start]); PlanLog.Add((W.Tick, "practice from " + start)); }
            for (var s = start; s <= UltSegment.P5; s++)
            {
                if (s == UltSegment.P3 && start < UltSegment.P3) foreach (var w in SecondForm()) yield return w;
                Begin(s);
                foreach (var w in RunSegment(s)) yield return w;
                // the transition waits for the current moves to finish (rules: no transition mid-telegraph)
                while (running.Count > 0 || W.Hazards.Count > 0) yield return 1;
                // tests of P4 alone: clearing P4 ends the attempt (a win) instead of going on into P5
                if (s == UltSegment.P4 && V2StopAfterP4) { PlanLog.Add((W.Tick, "v2: P4 cleared")); DebugDefeat(); while (true) yield return 60; }
            }
            while (true) yield return 60;   // P5 never ends by itself: the boss dies or toll XIII wipes
        }

        static readonly Unity.Profiling.ProfilerMarker BeginMarker = new Unity.Profiling.ProfilerMarker("Ultimate.Segment.Begin");
        void Begin(UltSegment s)
        {
            using var mark = BeginMarker.Auto();   // the phase change: name, look, HUD
            Segment = s; SegmentStartTick = W.Tick; SegmentLoops = 0; pushFired = false;
            SegmentLog.Add((W.Tick, s));
            PlanLog.Add((W.Tick, "segment " + s));
            phase = SegLook[(int)s];
            Ult.Reset();
            if (PhaseName != SegmentNames[(int)s]) SetPhase(SegmentNames[(int)s]);   // the opening segment (P1, or the picked phase) already shows its name: no phase change
        }

        // ---------------- practice phase row (Test Rush "Start Phase", -lastbellPractice ... -lastbellPhase N) ----------------
        // Phases 1-5 are P1-P5. The director calls StartAtPhase at spawn and again after BeginCombat, so this is idempotent.
        UltSegment? pickedSegment;   // the plan starts here and keeps the HP set below
        float[] UltPhaseStarts => SegStart;
        int UltPhaseNumber => (int)Segment + 1;
        void UltEnterPhase(int n)
        {
            var s = (UltSegment)(n - 1);
            // fit the bar to the party first (FitHpToParty only touches an untouched boss), then one HP under the threshold:
            // `Hp > MaxHp * 0.70f` can read exactly 70 % as the phase above
            Hp = MaxHp; FitHpToParty();
            Hp = Mathf.Clamp(Mathf.FloorToInt(MaxHp * SegStart[(int)s]) - 1, 1, MaxHp);
            pickedSegment = s; Segment = s;
            phase = SegLook[(int)s]; lookPhase = phase - 1;
            PhaseName = SegmentNames[(int)s];
        }

        IEnumerable<int> RunSegment(UltSegment s)
        {
            switch (s)
            {
                case UltSegment.P1:
                    do { foreach (var w in P1Loop()) yield return w; } while (!Reached(s) || SegmentLoops < V2P1MinLoops);
                    mRelay.StopAfterBeat();
                    break;
                case UltSegment.P2:
                    UltLaunch(mBroken, C(2, "p2", 6));   // first beat 6 s in, then every 3 s
                    while (!Reached(s) || SecondsInSegment < V2P2MinSeconds) yield return 30;
                    mBroken.StopAfterBeat();
                    break;
                case UltSegment.P3:
                    UltLaunch(mOpen, C(3, "p3"));   // the 60 s bullet-hell pattern, repeated
                    while (!Reached(s) || SecondsInSegment < V2P3MinSeconds) yield return 30;
                    mOpen.StopAtLoopEnd();
                    break;
                case UltSegment.P4:
                    UltLaunch(mGear, C(4, "break"));   // The Floor Breaks (15 s), then the gear floor: a slam every 7.5 s
                    while (!Reached(s) || SecondsInSegment < V2P4MinSeconds + GearFloor.BreakLen) yield return 30;
                    mGear.GoWild();                     // The Train Runs Wild (15 s after the next slam), then the great bell falls
                    mFinal.PrebakeEcho();                // P5's Echo is copied from the boss while the train runs wild
                    while (mGear.Running) { mFinal.StepEchoBake(); yield return 1; }
                    break;
                default:
                    UltLaunch(mFinal, C(5, "p5"));   // Final Toll: the boss dies, or toll XIII wipes (the mechanic does it)
                    while (true) yield return 60;
            }
        }

        // P1 Last Guardian (loop 36 s = 12 beats): the Escapement Relay runs through the whole phase (first beat 6 s after the
        // pull, then every 3 s); each loop one round of Chime Debts is paid on beats 18, 21, 24, 27 (hours I..IV) and detonates
        // 8 s later. No boss attacks: every job is the relay and the debts, and attacking happens during them.
        IEnumerable<int> P1Loop()
        {
            SegmentLoops++;
            if (!IsRunning(mRelay)) UltLaunch(mRelay, C(1, "p1", 6));
            foreach (var w in Timeline(36, (12f, (Action)(() => UltLaunch(mDebts, C(1, "p1", 6, 9, 12, 15)))))) yield return w;
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
        UltCue C(int phaseNo, string variant, params float[] at) => new UltCue { Phase = phaseNo, Variant = variant, At = at, Seed = Seed() };
        int Seed() => Mathf.Abs((int)Segment * 7919 + SegmentLoops * 104729 + W.Players.Count * 31 + SeedOffset * 15485863);
        // -lastbellSeed N: shifts every seeded choice of the attempt, so batches of bot clears play different attempts (0 = the default)
        public static int SeedOffset = int.TryParse(Arg("-lastbellSeed"), out int s) ? s : 0;

        public GearFloor UltGear => mGear;
        public FinalToll UltFinal => mFinal;
        public OpenHour UltOpen => mOpen;
        public BrokenHour UltBroken => mBroken;
        public EscapementRelay UltRelay => mRelay;
        protected override float DamageTakenScale(Vector3 contact) => Segment == UltSegment.P5 && mFinal != null && mFinal.Running ? mFinal.DamageScale(contact) : 1f;
        // P4's gear cores are extra bodies: solid, and a hit on one damages the boss
        public override int ExtraBodies => mGear != null && mGear.Running ? 4 : mFinal != null && mFinal.EchoBody(out _, out _) ? 1 : 0;
        public override void ExtraBody(int i, out Vector3 center, out float radius)
        {
            if (mGear != null && mGear.Running) { center = GearFloor.Core(i); radius = mGear.CoreGone(i) ? 0 : GearFloor.HubR; return; }
            if (mFinal != null && mFinal.EchoBody(out center, out radius)) return;
            center = transform.position; radius = 0;
        }

        // ---------------- HUD ----------------
        public override string HudLine
        {
            get
            {
                if (!UltPlanActive || SegmentStartTick < 0) return null;
                float left = PushLeft;
                // the phase line above already names the segment (PhaseName): only the push timer and my job here (was "ULTIMATE · P4 ·
                // The Unfinished Hour · push", a repeat of the line above it, performance agent's screenshots, 2 Nov)
                string push = left < 0 ? null : $"push {Mathf.Max(0, left) / 60:0}:{Mathf.Max(0, left) % 60:00}";
                var relay = mRelay != null && W.Players.Count > 0 ? mRelay.HudText(W.Players[0]) : null;
                if (relay == null && mBroken != null && W.Players.Count > 0) relay = mBroken.HudText(W.Players[0]);
                if (relay == null && mOpen != null && W.Players.Count > 0) relay = mOpen.HudText(W.Players[0]);
                if (relay == null && mGear != null && W.Players.Count > 0) relay = mGear.HudText(W.Players[0]);
                if (relay == null && mFinal != null && W.Players.Count > 0) relay = mFinal.HudText(W.Players[0]);
                return push == null ? relay : relay == null ? push : push + "\n" + relay;
            }
        }
        // the four phase thresholds on the bar (was the v1 plan's 85 / 55 / 35 / 15 % until 1 Nov)
        public override float[] HudMarks => new[] { SegEnd[0], SegEnd[1], SegEnd[2], SegEnd[3] };
        public override float NextPhaseFraction() => Mathf.Max(0.01f, SegEnd[(int)Segment] - 0.01f);

        static GUIStyle glyphStyle;
        // The phases' labels share the skin's bold font at these sizes; P2 is the first to use → ✓ –. Measuring them once on the
        // first frame loads the fonts and their OpenType tables then (performance agent, 2 Nov: a 79 ms font load at the P1 -> P2 change).
        // Every printable ASCII character plus the symbols, bold and regular: the first debt timer ("DEBT 6.0s": the '.') still
        // loaded font tables at 18.6 s and 21.1 s with the shorter string (performance agent, 2 Nov).
        // Measuring alone never puts the glyphs on the font's texture: P4's slam 6 still grew and re-uploaded it mid-fight (10 ms
        // GUIStyle.GetMeshInfo, then a 29 ms swap-chain wait; performance agent, 3 Nov). So each size and style is also rasterized,
        // one per repaint from the encounter's first GUI frame (the intro), so no single frame pays for all of them.
        // That texture is only the old path: IMGUI lays text out through TextCore, which loads a face's tables and glyphs on the first
        // real draw (slam 6 still 23 ms, P2 +10 s a 13 ms GPOS load; performance agent, 3 Nov). So each step also draws the
        // whole set once with GUI.Label, invisible.
        static readonly int[] warmSizes = { 15, 16, 17, 20, 22, 24, 32, 34, 40, 72 };
        // ...then the run HUD's Inter faces once each: it shows HudLine and the attack names in them (a face loads its tables once). Each
        // face takes three frames, measure, texture, draw: in one frame each was a 28 ms intro frame (performance agent, 3 Nov)
        const int WarmSteps = 10 * 2 + 2 * 3;
        internal static bool FontsWarmed => warmStep >= WarmSteps;
        static string warmChars; static int warmStep;
        internal static void WarmFontStep()
        {
            if (warmChars == null)
            {
                var sb = new System.Text.StringBuilder("→ ✓ – — − ✕ · ° ≥ ");
                for (char ch = ' '; ch <= '~'; ch++) sb.Append(ch);
                warmChars = sb.ToString();
            }
            var s = new GUIStyle(GUI.skin.label);
            int part = -1;   // -1: all three parts in this frame
            if (warmStep >= warmSizes.Length * 2) { int i = warmStep - warmSizes.Length * 2; s.font = i / 3 == 0 ? UiTheme.Regular : UiTheme.SemiBold; s.fontStyle = FontStyle.Normal; s.fontSize = 20; part = i % 3; }
            else { s.fontStyle = warmStep % 2 == 0 ? FontStyle.Bold : FontStyle.Normal; s.fontSize = warmSizes[warmStep / 2]; }
            if (part <= 0) s.CalcSize(new GUIContent(warmChars));
            var font = s.font != null ? s.font : GUI.skin.font;
            if ((part < 0 || part == 1) && font != null && font.dynamic) font.RequestCharactersInTexture(warmChars, s.fontSize, s.fontStyle);
            s.wordWrap = false; s.clipping = TextClipping.Overflow; s.normal.textColor = new Color(1, 1, 1, 0);
            if (part < 0 || part == 2) GUI.Label(new Rect(0, 0, Screen.width, s.fontSize * 1.5f), warmChars, s);
            warmStep++;
        }
        // -lastbellFontLog: every font texture rebuild, with the fight time (where a texture still grows mid-fight)
        static bool fontLogOn;
        void LogFontRebuilds()
        {
            if (fontLogOn || Arg("-lastbellFontLog") == null) return;
            fontLogOn = true; Font.textureRebuilt += f => Debug.Log($"[font] texture rebuilt: {f.name} · frame {Time.frameCount} · tick {(W != null ? W.Tick : -1)} · segment {Segment}");
        }
        void OnGUI()
        {
            LogFontRebuilds();
            if (Ult != null && !readyWarmer && warmStep < WarmSteps && Event.current.type == EventType.Repaint) WarmFontStep();   // the Ready-screen helper steps instead while it runs
            if (Ult == null || W == null || Dead) return;
            var cam = Camera.main; if (!cam) return;
            if (fontLogOn) { TimedGui(cam); return; }
            mRelay?.DrawGui(cam);
            mBroken?.DrawGui(cam);
            mOpen?.DrawGui(cam);
            mGear?.DrawGui(cam);
            mFinal?.DrawGui(cam);
            DrawGlyphs(cam);
        }
        // -lastbellFontLog: each phase's labels timed; a draw over 2 ms is logged (a face or atlas loaded mid-fight)
        readonly System.Diagnostics.Stopwatch guiWatch = new System.Diagnostics.Stopwatch();
        void TimedGui(Camera cam)
        {
            void Timed(string what, Action draw)
            {
                guiWatch.Restart(); draw(); double ms = guiWatch.Elapsed.TotalMilliseconds;
                if (ms > 2) Debug.Log($"[font] slow GUI: {what} {ms:0.0} ms · {Event.current.type} · frame {Time.frameCount} · tick {W.Tick} · segment {Segment}");
            }
            Timed("relay", () => mRelay?.DrawGui(cam)); Timed("broken", () => mBroken?.DrawGui(cam)); Timed("open", () => mOpen?.DrawGui(cam));
            Timed("gear", () => mGear?.DrawGui(cam)); Timed("final", () => mFinal?.DrawGui(cam)); Timed("glyphs", () => DrawGlyphs(cam));
        }
        void DrawGlyphs(Camera cam)
        {
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
