using System;
using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // The Bellkeeper's scheduler: it runs the Ultimate's mechanics side by side (the plan launches them, each steps every
    // tick), plus the practice hook and the test switches. The plan is in Ultimate/BellkeeperUltimate.cs, the mechanics in
    // Ultimate/<Mechanic>.cs. (The v1 fight and the normal v2 set were deleted, Chzy18 1 Nov: the Ultimate is the only fight.)
    public sealed partial class BellkeeperEncounter
    {
        // ---------------- spawn and practice hook ----------------
        // Tests set AbilityOverride; otherwise -lastbellAbility <key> loops one mechanic alone, -lastbellBots <N> practises
        // with N-1 scripted stand-ins.
        public static string AbilityOverride;
        public static BossEncounter SpawnUltimate(CombatWorld w) => Spawn<BellkeeperEncounter>(w, Vector3.zero, 0);
        public static string RequestedAbility => BellkeeperAbilities.Normalize(AbilityOverride ?? Arg("-lastbellAbility"));
        public static int RequestedBots => int.TryParse(Arg("-lastbellBots"), out int n) ? Mathf.Clamp(n, 1, 4) : 1;
        // the Ultimate is fought on the Throne of the Final Toll
        public const string ThroneMap = "shattered-hour";
        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.FindIndex(a, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        public BellkeeperAbility PracticeAbility { get; private set; }   // non-null: -lastbellAbility loops this move alone
        BellkeeperAbility[] abilities = Array.Empty<BellkeeperAbility>();
        public IReadOnlyList<BellkeeperAbility> Abilities => abilities;
        public BellkeeperAbility Ability(string key) { key = BellkeeperAbilities.Normalize(key); return Array.Find(abilities, a => a.Key == key); }
        public T Ability<T>() where T : BellkeeperAbility => (T)Array.Find(abilities, a => a is T);

        // ---------------- facade for the mechanics ----------------
        public CombatWorld World => W;
        public int Phase => phase;
        public int PartySize => W.Players.Count;
        public void ShowAttack(string name) { Current = name; }
        public void Glow(float windup01) { Windup(windup01); }
        public void Flare(float flare = 1) { Release(flare); }
        public void Face(float yaw, float degPerSec = 9999) { TurnTowards(yaw, degPerSec); }
        public Vector3 TargetPosition => Target();
        public IEnumerable<int> Walk(Vector3 dest, float speed, int maxTicks = 180) => WalkTo(dest, speed, maxTicks);

        // ---------------- scheduler ----------------
        sealed class Live { public BellkeeperAbility A; public IEnumerator<int> E; public int Wait; }
        readonly List<Live> running = new List<Live>(), stepping = new List<Live>();
        public bool IsRunning(BellkeeperAbility a) { foreach (var r in running) if (r.A == a) return true; return false; }
        public int RunningCount => running.Count;
        public readonly List<(int tick, string what)> PlanLog = new List<(int, string)>();   // tests: launches, transitions, wipes

        // Starts an ability's Cast now: its first segment runs on this tick, so tick 0 of the ability is this tick.
        static readonly Unity.Profiling.ProfilerMarker LaunchMarker = new Unity.Profiling.ProfilerMarker("Ultimate.Launch");
        public void Launch(BellkeeperAbility a)
        {
            using var mark = LaunchMarker.Auto();   // a cast's first step runs here (its Build)
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
            running.Clear();
            foreach (var a in abilities) { a.Cancel(); a.NextTuning = null; if (a is UltimateMechanic m) m.NextCue = null; }
            Ult?.Reset(); UltPlanActive = false;
        }
        protected override void OnHalt() { StopAbilities(); }

        // One script step per tick: the plan, then every ability that was already running.
        protected override IEnumerable<int> Script()
        {
            StopAbilities();
            if (!TestIdle) FitHpToParty();   // the idle test target keeps its bar (a dummy for the run-loop and ally checks)
            if (!TestIdle && PracticeAbility == null && Mechanics != null) foreach (var m in Mechanics) m.Prebuild();
            IEnumerator<int> plan = null; int wait = 0;
            while (true)
            {
                UltimateTick();
                stepping.Clear(); stepping.AddRange(running);
                if (wait > 0) wait--;
                else
                {
                    if (plan == null || !plan.MoveNext()) { plan = (ManualPlan || TestIdle ? IdlePlan() : PracticeAbility != null ? PracticeAbility.Practice() : UltimatePlan()).GetEnumerator(); plan.MoveNext(); }
                    wait = Mathf.Max(1, plan.Current) - 1;
                }
                foreach (var r in stepping) if (running.Contains(r) && !Advance(r)) running.Remove(r);
                yield return 1;
            }
        }

        // Tests: with ManualPlan set before BeginCombat(), the scheduler runs (launched moves advance every tick) but no
        // plan launches anything: the test launches moves itself (UltLaunch).
        public bool ManualPlan;
        // Tests: the run-loop checks use the Ultimate as a passive final target (class kits, potions, results): no plan, so no
        // phase floors, push timers or wipes, and the run-wide damage scale.
        public static bool TestIdle;
        static IEnumerable<int> IdlePlan() { while (true) yield return 60; }

        // spec v0.3.1 D6: boss HP scales with the party (1-4 fighters); damage never does. Only while the boss is untouched.
        void FitHpToParty()
        {
            int n = 0; foreach (var p in W.Players) if (!(p.Input is AllyBot) || AllyBot.Attacks) n++;   // passive menu allies don't count
            n = Mathf.Clamp(n, 1, 4);
            int want = Mathf.RoundToInt(UltimateBaseHp * (n == 1 ? UltimateSoloScale : 1) * UltHpFor(W.Difficulty) * (1 + 0.7f * (n - 1)));
            if (want == MaxHp || Hp != MaxHp) return;
            MaxHp = want; Hp = want;
        }
    }
}
