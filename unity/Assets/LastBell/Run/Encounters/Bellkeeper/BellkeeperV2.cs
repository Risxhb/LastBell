using System;
using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // The Bellkeeper's ability set v2 (proposal, bosses/4-shattered-hour-bellkeeper/abilities-v2/ABILITIES.md): the set
    // switch, the scheduler that runs abilities side by side, the phase plan (§5) and the hooks the abilities and the
    // autotest use. The moves themselves are in Bellkeeper/Abilities/, one file each.
    public sealed partial class BellkeeperEncounter
    {
        // ---------------- set switch and practice hook ----------------
        public enum AbilitySet { V1, V2, Ultimate }
        // Tests set these; otherwise the command line decides: -lastbellAbilitySet v1|v2|ultimate (default v2),
        // -lastbellUltimate (= ultimate), -lastbellAbility <key>, -lastbellBots <N> (practice with N-1 scripted stand-ins).
        // The practice entry "Bellkeeper — Ultimate" spawns the Ultimate whatever the switches say (SpawnUltimate).
        public static AbilitySet? SetOverride;
        public static string AbilityOverride;
        static bool spawningUltimate;
        public static AbilitySet RequestedSet
        {
            get
            {
                if (SetOverride.HasValue) return SetOverride.Value;
                if (spawningUltimate) return AbilitySet.Ultimate;
                var s = Arg("-lastbellAbilitySet")?.Trim();
                if (string.Equals(s, "ultimate", StringComparison.OrdinalIgnoreCase) || HasArg("-lastbellUltimate")) return AbilitySet.Ultimate;
                return string.Equals(s, "v1", StringComparison.OrdinalIgnoreCase) ? AbilitySet.V1 : AbilitySet.V2;
            }
        }
        public static BossEncounter SpawnUltimate(CombatWorld w)
        {
            spawningUltimate = true;
            try { return Spawn<BellkeeperEncounter>(w, Vector3.zero, 0); }
            finally { spawningUltimate = false; }
        }
        static bool HasArg(string name) => Array.Exists(Environment.GetCommandLineArgs(), x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
        public static string RequestedAbility => BellkeeperAbilities.Normalize(AbilityOverride ?? Arg("-lastbellAbility"));
        public static int RequestedBots => int.TryParse(Arg("-lastbellBots"), out int n) ? Mathf.Clamp(n, 1, 8) : 1;
        // ABILITIES.md open question 1 (provisional): v2 practice is fought on the Throne of the Final Toll.
        public const string ThroneMap = "shattered-hour";
        public static string PracticeMap => RequestedSet != AbilitySet.V1 ? ThroneMap : null;
        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.FindIndex(a, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        public AbilitySet Set { get; private set; }                 // fixed when the encounter is built
        public BellkeeperAbility PracticeAbility { get; private set; }   // non-null: -lastbellAbility loops this move alone
        BellkeeperAbility[] abilities = Array.Empty<BellkeeperAbility>();
        public IReadOnlyList<BellkeeperAbility> Abilities => abilities;
        public BellkeeperAbility Ability(string key) { key = BellkeeperAbilities.Normalize(key); return Array.Find(abilities, a => a.Key == key); }
        public T Ability<T>() where T : BellkeeperAbility => (T)Array.Find(abilities, a => a is T);
        BellkeeperAbility abLean, abOrder, abMirror, abTide, abEcho;

        void BuildV2()
        {
            Set = RequestedSet;
            if (Set == AbilitySet.V1) return;
            if (Set == AbilitySet.Ultimate) { BuildUltimate(); return; }
            abilities = BellkeeperAbilities.CreateAll();
            foreach (var a in abilities) a.Bind(this);
            abLean = Ability(BellkeeperAbilities.LeaningBell); abOrder = Ability(BellkeeperAbilities.StrikingOrder);
            abMirror = Ability(BellkeeperAbilities.Reflection); abTide = Ability(BellkeeperAbilities.TollTide); abEcho = Ability(BellkeeperAbilities.UnfinishedHour);
            var key = RequestedAbility;
            if (key != null)
            {
                PracticeAbility = Ability(key);
                if (PracticeAbility == null) Debug.LogWarning($"Bellkeeper: -lastbellAbility {key} is not a v2 ability ({string.Join(", ", Array.ConvertAll(abilities, a => a.Key))}); running the phase plan");
            }
            if (RequestedBots > W.Players.Count) ScriptedParty.AddStandIns(W, RequestedBots - W.Players.Count);
        }

        // ---------------- facade for the abilities ----------------
        public CombatWorld World => W;
        public int Phase => phase;
        public int PartySize => W.Players.Count;
        public void ShowAttack(string name) { Current = name; }
        public void Glow(float windup01) { Windup(windup01); }
        public void Flare(float flare = 1) { Release(flare); }
        public void Face(float yaw, float degPerSec = 9999) { TurnTowards(yaw, degPerSec); }
        public Vector3 TargetPosition => Target();
        public IEnumerable<int> Walk(Vector3 dest, float speed, int maxTicks = 180) => WalkTo(dest, speed, maxTicks);

        // Leaning Bell -> The Unfinished Hour: Leaning Bell calls RememberLean with its locked half when it resolves; the
        // Unfinished Hour replays PreviousLean. Cleared on phase change, defeat, halt and restart.
        public Hazard LastLean { get; private set; }
        public Hazard PreviousLean { get; private set; }
        public event Action<Hazard> LeanRemembered;
        public event Action LeansForgotten;
        public bool EchoArmed { get; set; }   // the plan arms it around Lean and Order on Hard from phase 2
        bool pendingEcho;
        public void RememberLean(Hazard half)
        {
            PreviousLean = LastLean; LastLean = half;
            LeanRemembered?.Invoke(half);
            if (EchoArmed && PreviousLean != null) pendingEcho = true;   // the echo starts warning on the next tick
        }
        void ForgetLeans()
        {
            if (LastLean == null && PreviousLean == null) return;
            LastLean = PreviousLean = null; LeansForgotten?.Invoke();
        }

        // ---------------- scheduler ----------------
        sealed class Live { public BellkeeperAbility A; public IEnumerator<int> E; public int Wait; }
        readonly List<Live> running = new List<Live>(), stepping = new List<Live>();
        public bool IsRunning(BellkeeperAbility a) { foreach (var r in running) if (r.A == a) return true; return false; }
        public int RunningCount => running.Count;

        // Starts an ability's Cast now: its first segment runs on this tick, so tick 0 of the ability is this tick.
        public void Launch(BellkeeperAbility a)
        {
            if (a == null || Dead) return;
            if (a is UltimateMechanic m) m.TakeCue();
            a.Launched();
            PlanLog.Add((W.Tick, "launch " + a.Key));
            var r = new Live { A = a, E = a.Cast().GetEnumerator() };
            running.Add(r);
            if (!Advance(r)) running.Remove(r);
        }
        static bool Advance(Live r)
        {
            if (r.Wait > 0) { r.Wait--; return true; }
            if (!r.E.MoveNext()) return false;
            r.Wait = Mathf.Max(1, r.E.Current) - 1;
            return true;
        }
        void StopAbilities()
        {
            running.Clear(); pendingEcho = false; EchoArmed = false;
            foreach (var a in abilities) { a.Cancel(); a.NextTuning = null; if (a is UltimateMechanic m) m.NextCue = null; }
            Ult?.Reset(); UltPlanActive = false;
            ForgetLeans();
        }
        protected override void OnHalt() { StopAbilities(); }

        // One script step per tick: the plan, then every ability that was already running.
        IEnumerable<int> ScriptV2()
        {
            StopAbilities();
            FitHpToParty();
            IEnumerator<int> plan = null; int wait = 0;
            while (true)
            {
                if (Set == AbilitySet.Ultimate) UltimateTick();
                stepping.Clear(); stepping.AddRange(running);
                if (pendingEcho) { pendingEcho = false; if (EchoArmed) Launch(abEcho); }
                if (wait > 0) wait--;
                else
                {
                    if (plan == null || !plan.MoveNext()) { plan = (PracticeAbility != null ? PracticeAbility.Practice() : Set == AbilitySet.Ultimate ? UltimatePlan() : PhasePlan()).GetEnumerator(); plan.MoveNext(); }
                    wait = Mathf.Max(1, plan.Current) - 1;
                }
                foreach (var r in stepping) if (running.Contains(r) && !Advance(r)) running.Remove(r);
                yield return 1;
            }
        }

        // spec v0.3.1 D6: boss HP scales with the party; damage never does. Only while the boss is untouched.
        public const int BaseHp = 3600;
        void FitHpToParty()
        {
            int n = Mathf.Max(1, W.Players.Count), want = Mathf.RoundToInt((Set == AbilitySet.Ultimate ? UltimateBaseHp : BaseHp) * (1 + 0.7f * (n - 1)));
            if (want == MaxHp || Hp != MaxHp) return;
            MaxHp = want; Hp = want;
        }

        // ---------------- phase plan (ABILITIES.md §5, Medium) ----------------
        public const int PunishTicks = 210;          // the 3.5 s major punish window after every Toll Tide
        public const int OrderAfterLean = 90;        // Lean and Order: numerals 1.5 s into the cleave's warning
        public const int MirrorAfterTide = 60;       // The Final Toll: Reflection warns 1.0 s into the tide
        public readonly List<(int tick, string what)> PlanLog = new List<(int, string)>();   // tests: launches, punish windows, transitions
        public int PunishWindows { get; private set; }

        IEnumerable<int> PhasePlan()
        {
            while (true)
            {
                int want = PhaseFor();
                if (want != phase)
                {
                    // queued: the current attack has finished; only one transition however many thresholds were crossed
                    while (running.Count > 0 || W.Hazards.Count > 0) yield return 1;
                    phase = want; loop = 0;
                    ForgetLeans();   // The Unfinished Hour remembers nothing across a phase change (05 brief)
                    PlanLog.Add((W.Tick, "phase " + phase));
                    foreach (var w in PhaseShift(phase == 2 ? Phase2 : Phase3)) yield return w;
                }
                loop++;
                if (phase == 1)
                {
                    // Last Guardian: each move alone, to learn it
                    foreach (var w in Block(abLean)) yield return w; if (PhaseFor() != phase) continue;
                    foreach (var w in Block(abOrder)) yield return w; if (PhaseFor() != phase) continue;
                    foreach (var w in Block(abMirror)) yield return w; if (PhaseFor() != phase) continue;
                    foreach (var w in Block(abTide)) yield return w;
                    foreach (var w in Punish()) yield return w;
                }
                else
                {
                    // Broken Hour and Final Toll open with Lean and Order (where + apart); Hard adds the Unfinished Hour
                    foreach (var w in LeanAndOrder()) yield return w; if (PhaseFor() != phase) continue;
                    if (phase == 2)
                    {
                        foreach (var w in Block(abMirror)) yield return w; if (PhaseFor() != phase) continue;
                        foreach (var w in Block(abTide)) yield return w;
                        foreach (var w in Punish()) yield return w;
                    }
                    else foreach (var w in FinalTollV2()) yield return w;
                }
            }
        }

        IEnumerable<int> Block(BellkeeperAbility a)
        {
            foreach (var w in a.Setup()) yield return w;
            Launch(a);
            while (IsRunning(a)) yield return 1;
        }

        IEnumerable<int> LeanAndOrder()
        {
            foreach (var w in abLean.Setup()) yield return w;
            EchoArmed = phase >= 2 && W.Difficulty == Difficulty.Hard;
            Launch(abLean);
            yield return OrderAfterLean;
            Launch(abOrder);
            while (running.Count > 0 || pendingEcho) yield return 1;
            EchoArmed = false;
        }

        // The Final Toll: the tide with Reflection (images lock as the tide breaks, resolve with the Heart Toll), then the
        // punish window while Reflection finishes its recovery.
        IEnumerable<int> FinalTollV2()
        {
            foreach (var w in abTide.Setup()) yield return w;
            Launch(abTide);
            yield return MirrorAfterTide;
            Launch(abMirror);
            while (IsRunning(abTide)) yield return 1;
            foreach (var w in Punish()) yield return w;
            while (running.Count > 0) yield return 1;
        }

        IEnumerable<int> Punish()
        {
            PunishWindows++; PlanLog.Add((W.Tick, "punish"));
            foreach (var w in Vulnerable(PunishTicks)) yield return w;
        }
    }
}
