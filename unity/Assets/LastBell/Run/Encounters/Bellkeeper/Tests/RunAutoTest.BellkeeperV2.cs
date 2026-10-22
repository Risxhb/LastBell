using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Bellkeeper ability set v2 (proposal) acceptance checks, inside the real game like the rest of RunAutoTest.
    //   LastBell.exe -lastbellAutotest -lastbellBellkeeperV2 [-lastbellTestAbility <key>] -lastbellOut <folder>
    // -lastbellBellkeeperV2 runs only this section (fast loop for the ability sessions); -lastbellTestAbility narrows it to
    // one ability's checks ("scaffold" and "plan" name the shared parts). Each ability's checks live in their own file
    // (RunAutoTest.Bellkeeper.<Ability>.cs, owned by that ability's session); this file is the shared harness.
    //
    // Harness: BkStart(...) leaves a v2 Bellkeeper fight paused at combat start with the boss halted and the player at full
    // HP. The test then places players (BkPlace / BkAdd for scripted extras), restarts the boss with bk.BeginCombat() and
    // advances the world in exact combat ticks with BkStep / BkStepUntil. Every hit is recorded in bkHits.
    public sealed partial class RunAutoTest
    {
        static bool BellkeeperV2Only => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellBellkeeperV2", StringComparison.OrdinalIgnoreCase));
        static string BkFilter
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.FindIndex(args, a => a.Equals("-lastbellTestAbility", StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i + 1 < args.Length ? BellkeeperAbilities.Normalize(args[i + 1]) : null;
            }
        }
        static bool BkWants(string idOrPart) => BkFilter == null || BkFilter == BellkeeperAbilities.Normalize(idOrPart);

        BellkeeperEncounter bk;
        public struct BkHit { public int Tick; public PlayerController Player; public string Source; public int HazardId; public int Damage; }
        readonly List<BkHit> bkHits = new List<BkHit>();

        IEnumerator BellkeeperV2Checks()
        {
            Note("---------- Bellkeeper ability set v2 (proposal) ----------");
            if (BkWants("scaffold")) yield return BkScaffoldChecks();
            if (BkWants(BellkeeperAbilities.LeaningBell)) yield return LeaningBellChecks();
            if (BkWants(BellkeeperAbilities.StrikingOrder)) yield return StrikingOrderChecks();
            if (BkWants(BellkeeperAbilities.Reflection)) yield return ReflectionChecks();
            if (BkWants(BellkeeperAbilities.TollTide)) yield return TollTideChecks();
            if (BkWants(BellkeeperAbilities.UnfinishedHour)) yield return UnfinishedHourChecks();
            if (BkWants("plan")) yield return BkPhasePlanChecks();
            yield return BkLeave();
        }

        // ---------------- harness ----------------

        // A v2 fight (or v1 with set: V1), paused at combat start. ability = practice key (null: the phase plan).
        // testMap: fight on the key-art test map (a run there; practice always loads the Throne).
        IEnumerator BkStart(string ability, Difficulty level = Difficulty.Medium, bool testMap = false,
                            BellkeeperEncounter.AbilitySet set = BellkeeperEncounter.AbilitySet.V2)
        {
            BellkeeperEncounter.SetOverride = set;
            BellkeeperEncounter.AbilityOverride = ability ?? "";
            if (d.State != RunState.Menu) { d.SetPaused(true); d.ReturnToMenu(); yield return null; }
            d.SelectDifficulty(level);
            if (testMap)
            {
                int t = RunMaps.IndexOf("test");
                if (RunMaps.Current != t) { directorLog.AddRange(d.Log); d.SelectMap(t); yield return BkAdopt(t, RunState.Menu); }
                d.StartRun("Vanguard");
            }
            else
            {
                var want = RunDirector.Bosses[0].Map; int m = want == null ? RunMaps.Current : RunMaps.IndexOf(want);
                bool switching = m != RunMaps.Current;
                if (switching) directorLog.AddRange(d.Log);
                d.StartPractice("Vanguard", 0);
                if (switching) yield return BkAdopt(m, RunState.Ready);
            }
            d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 4, "Bellkeeper v2 combat");
            bk = d.Boss as BellkeeperEncounter;
            d.SetPaused(true); bot.Clear();
            if (bk == null) { Check("Bellkeeper v2 fight started", false, d.Boss ? d.Boss.GetType().Name : "no boss"); yield break; }
            bk.Halt(); d.World.ClearTransient();
            bk.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            d.Player.Input = bot; d.Player.DebugSetHp(PlayerController.MaxHp);
            bkHits.Clear();
            d.World.PlayerHit += (p, h, dmg) => bkHits.Add(new BkHit { Tick = d.World.Tick, Player = p, Source = h.Source, HazardId = h.Id, Damage = dmg });
            yield return null;
        }
        IEnumerator BkAdopt(int map, RunState state)
        {
            yield return Until(() => RunMaps.Current == map && RunDirector.Instance && RunDirector.Instance.State == state, 30, "map loaded: " + RunMaps.All[map].Name);
            if (RunDirector.Instance != d) { d = RunDirector.Instance; d.InputOverride = bot; bot.Clear(); }
            if (d.Player) d.Player.Input = bot;   // the new scene built the player before the bot was attached
        }
        // Back to the menu with the set and practice overrides cleared (the rest of the suite pins v1 again itself).
        IEnumerator BkLeave()
        {
            if (d.State != RunState.Menu) { d.SetPaused(true); d.ReturnToMenu(); yield return null; }
            BellkeeperEncounter.SetOverride = null; BellkeeperEncounter.AbilityOverride = null;
            d.SelectDifficulty(Difficulty.Medium);
            bk = null; bot.Clear();
        }

        void BkStep(int ticks) { for (int i = 0; i < ticks; i++) d.World.Step(); }
        // steps until cond holds (checked before each tick); returns the ticks taken, or -1 if it never held
        int BkStepUntil(Func<bool> cond, int maxTicks)
        {
            for (int i = 0; i <= maxTicks; i++) { if (cond()) return i; if (i < maxTicks) d.World.Step(); }
            return -1;
        }
        PlayerController BkAdd(Vector3 pos, string cls = "Vanguard") => ScriptedParty.Add(d.World, pos, cls);
        ScriptedPlayer BkScript(PlayerController p) => ScriptedParty.Script(p);
        static void BkPlace(PlayerController p, Vector3 pos) { p.transform.position = new Vector3(pos.x, 0, pos.z); }
        int BkHitsOn(PlayerController p, string source = null) => bkHits.Count(h => h.Player == p && (source == null || h.Source == source));
        int BkPatternsLive() => d.World.Hazards.Live.Select(h => h.Source).Distinct().Count();
        void BkTopUp() { foreach (var p in d.World.Players) if (!p.Down && p.Hp < 60) p.DebugSetHp(PlayerController.MaxHp); }
        // farthest point of the floor from `from`, along `dir` (circle or square bound)
        static Vector3 BkEdge(Vector3 dir, float inset = 0.05f)
        {
            dir = Sim.Flat(dir).normalized; float b = CombatWorld.PlayerBound - inset;
            if (!CombatWorld.SquareBound) return dir * b;
            float k = b / Mathf.Max(Mathf.Abs(dir.x), Mathf.Abs(dir.z)); return dir * k;
        }
        // A short real-time clip of the current boss script (the director runs, the player is kept alive).
        IEnumerator BkClip(string name, float seconds)
        {
            bk.BeginCombat(); d.SetPaused(false);
            StartCoroutine(Capture(name, Mathf.RoundToInt(seconds * 60)));
            float t = 0; while (t < seconds) { BkTopUp(); t += Time.deltaTime; yield return null; }
            yield return null;
            d.SetPaused(true); bk.Halt(); d.World.ClearTransient();
        }

        // ---------------- scaffold checks (the Abilities session) ----------------
        IEnumerator BkScaffoldChecks()
        {
            BellkeeperEncounter.SetOverride = null; BellkeeperEncounter.AbilityOverride = null;
            Check("v2: the default ability set is v2 (no -lastbellAbilitySet given)", BellkeeperEncounter.RequestedSet == BellkeeperEncounter.AbilitySet.V2);
            Check("v2: the Bellkeeper's practice slot loads the Shattered Hour map", RunDirector.Bosses[0].Map == BellkeeperEncounter.ThroneMap, RunDirector.Bosses[0].Map ?? "null");
            BellkeeperEncounter.SetOverride = BellkeeperEncounter.AbilitySet.V1;
            Check("v1: practice keeps the current map, as before", RunDirector.Bosses[0].Map == null);
            var all = BellkeeperAbilities.CreateAll();
            Check("v2: five abilities with unique ids and keys", all.Length == 5 && all.Select(a => a.Id).Distinct().Count() == 5 && all.Select(a => a.Key).Distinct().Count() == 5
                  && all.Select(a => a.Id).SequenceEqual(BellkeeperAbilities.Ids), string.Join(", ", all.Select(a => a.Key)));
            Check("v2: ability keys accept ids, dashes, names and numbers",
                  BellkeeperAbilities.Normalize("Leaning-Bell") == "leaning_bell" && BellkeeperAbilities.Normalize("bellkeeper.toll_tide") == "toll_tide"
                  && BellkeeperAbilities.Normalize("05") == "unfinished_hour" && BellkeeperAbilities.Normalize("Reflection of the Hour") == "reflection");
            Note("v2 abilities built: " + string.Join(", ", all.Select(a => a.Key + (a.Built ? "" : " (stub)"))));

            // the practice hook: each key loops that ability alone
            foreach (var id in BellkeeperAbilities.Ids)
            {
                yield return BkStart(id);
                if (bk == null) yield break;
                var a = bk.Ability(id);
                bk.BeginCombat();
                int t = BkStepUntil(() => a.Casts > 0, 300);
                bool alone = bk.Abilities.All(x => x == a || x.Casts == 0 || (a.Key == "unfinished_hour" && x.Key == "leaning_bell"));
                Check($"v2 practice hook -lastbellAbility {a.Key}: fires {a.Name} alone", bk.PracticeAbility == a && t >= 0 && alone, $"cast after {t} ticks");
            }

            // scripted extra players and boss HP scaling with the party
            yield return BkStart(null);
            if (bk == null) yield break;
            var allies = new List<PlayerController>();
            for (int i = 0; i < 3; i++) allies.Add(BkAdd(new Vector3(-6 + 4 * i, 0, -5)));
            bk.BeginCombat(); BkStep(2);
            Check("scripted party: N = 4 players in the fight", d.World.Players.Count == 4 && bk.PartySize == 4);
            Check("v2: boss HP scales with the party, 3600 × (1 + 0.7 × (N − 1))", bk.MaxHp == 11160 && bk.Hp == bk.MaxHp, $"{bk.Hp}/{bk.MaxHp}");
            bk.Halt(); d.World.ClearTransient();
            var s = BkScript(allies[0]); var from = allies[0].transform.position;
            s.Goal = from + Vector3.right * 10; BkStep(60);
            float walked = Sim.Flat(allies[0].transform.position - from).magnitude;
            Check("scripted player walks at 4.2 m/s", Mathf.Abs(walked - 4.2f) < 0.15f, $"{walked:0.00} m in 1 s");
            float st0 = allies[0].Stamina; s.DashNext = true; BkStep(14);
            Check("scripted player dashes and spends stamina", st0 - allies[0].Stamina >= PlayerController.DashCost - 1, $"stamina {st0:0} -> {allies[0].Stamina:0}");
            ScriptedParty.RemoveExtras(d.World, d.Player);
            Check("scripted party: back to solo", d.World.Players.Count == 1);

            // v1 is still reachable and runs the v0.3 moves
            yield return BkStart(null, set: BellkeeperEncounter.AbilitySet.V1);
            if (bk == null) yield break;
            bk.BeginCombat(); var seen = new HashSet<string>();
            for (int i = 0; i < 420; i++) { d.World.Step(); BkTopUp(); seen.Add(bk.Current); }
            Check("v1 (-lastbellAbilitySet v1) still runs the v0.3 set", bk.Set == BellkeeperEncounter.AbilitySet.V1 && bk.Abilities.Count == 0 && seen.Contains("Bell Sweep"), string.Join(", ", seen));
        }
    }
}
