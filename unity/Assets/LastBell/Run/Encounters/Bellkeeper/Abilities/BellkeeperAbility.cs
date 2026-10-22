using System;
using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // One move of the Bellkeeper's ability set v2 (proposal, bosses/4-shattered-hour-bellkeeper/abilities-v2/ABILITIES.md).
    // Each ability lives in its own file in this folder and owns its hazards, presentation and per-level numbers; the
    // encounter (BellkeeperEncounter, v2 set) only decides *when* each one starts, so two abilities can overlap.
    //
    // Timeline contract: Cast() is a tick coroutine like BossEncounter.Script(): `yield return n` waits n combat ticks.
    // Tick 0 is the warning's first tick (the encounter steps a launched ability once on the tick it is launched). Cast()
    // ends after the ability's own short recovery (ABILITIES.md §5); punish windows belong to the encounter. Movement
    // the boss makes before warning (Leaning Bell's walk to the centre) goes in Setup(), which the encounter runs first.
    // Hazards go through Spawn() so tests can see them. Boss death needs nothing here: the killing tick cancels every
    // live hazard (BossEncounter.Die), drops the encounter's script and calls Cancel() on every ability.
    public abstract class BellkeeperAbility
    {
        public abstract string Id { get; }                  // runtime ID, e.g. "bellkeeper.leaning_bell"
        public abstract string Name { get; }                // HUD: the attack in progress
        public string Key => BellkeeperAbilities.Normalize(Id);   // "leaning_bell": -lastbellAbility <key>
        public virtual bool Built => true;                  // false while the file is the scaffold stub

        public BellkeeperEncounter Boss { get; private set; }
        protected CombatWorld W => Boss.World;
        protected Difficulty Level => W.Difficulty;

        public readonly List<Hazard> Spawned = new List<Hazard>();   // every hazard this ability made since the last reset (tests)
        public int Casts { get; private set; }
        public int LastCastTick { get; private set; } = -1;          // World.Tick of the latest launch (tests)
        public bool Running => Boss != null && Boss.IsRunning(this);

        internal void Bind(BellkeeperEncounter boss) { Boss = boss; }
        internal void Launched() { Casts++; LastCastTick = W.Tick; }

        // Boss movement before the warning starts (run by the encounter's plan, not overlapped).
        public virtual IEnumerable<int> Setup() { yield break; }
        // The ability itself, from the warning's first tick to the end of its own recovery.
        public abstract IEnumerable<int> Cast();
        // -lastbellAbility <key>: the practice loop for this ability alone. Default: Setup, Cast, a 2 s gap, repeat.
        public virtual IEnumerable<int> Practice()
        {
            foreach (var w in Setup()) yield return w;
            Boss.Launch(this);
            while (Running) yield return 1;
            yield return 120;
        }
        // Encounter (re)start, halt, phase change, defeat or teardown: drop references and any presentation the ability
        // made itself (numerals, after-images). Its hazards are already gone or about to be cleared by the encounter.
        public virtual void Cancel() { Spawned.Clear(); }

        protected Hazard Spawn(Hazard h) { W.Hazards.Spawn(h); Spawned.Add(h); return h; }
        protected int StandingCount { get { int n = 0; foreach (var p in W.Players) if (!p.Down) n++; return n; } }
    }

    // The v2 set: one instance of each per encounter, in ABILITIES.md order.
    public static class BellkeeperAbilities
    {
        public const string LeaningBell = "bellkeeper.leaning_bell", StrikingOrder = "bellkeeper.striking_order",
            Reflection = "bellkeeper.reflection", TollTide = "bellkeeper.toll_tide", UnfinishedHour = "bellkeeper.unfinished_hour";
        public static readonly string[] Ids = { LeaningBell, StrikingOrder, Reflection, TollTide, UnfinishedHour };

        public static BellkeeperAbility[] CreateAll() => new BellkeeperAbility[]
        {
            new LeaningBellAbility(), new StrikingOrderAbility(), new ReflectionAbility(), new TollTideAbility(), new UnfinishedHourAbility(),
        };

        // "bellkeeper.leaning_bell", "leaning_bell", "leaning-bell", "Leaning Bell" and "01" all name the same ability.
        public static string Normalize(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            var k = key.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
            if (k.StartsWith("bellkeeper.")) k = k.Substring("bellkeeper.".Length);
            if (int.TryParse(k, out int n) && n >= 1 && n <= Ids.Length) k = Ids[n - 1].Substring("bellkeeper.".Length);
            if (k.StartsWith("the_")) k = k.Substring(4);
            if (k == "reflection_of_the_hour") k = "reflection";
            if (k == "last_toll_tide") k = "toll_tide";
            return k;
        }
    }
}
