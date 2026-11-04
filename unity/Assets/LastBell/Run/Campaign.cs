using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // The campaign behind Start Run (Risx, 4 Nov 2026): the tutorial first (first run only), then one arena at a time,
    // each a cluster of that arena's three minibosses (shuffled) followed by its area boss, then the Bellkeeper alone in
    // the final arena. Beating the Bellkeeper on Hard adds a bonus fight against the Overclocked Tempo Tyrant on
    // Metronome Court and unlocks his Boss Rush card. Docs: docs/campaign-loop.md.
    //
    //   Tutorial (Metronome Court) -> Skyward Spire: 3 minibosses -> Aurelion -> Brass Foundry: 3 -> Crucivar
    //   -> Verdant Chimes: 3 -> Sylvara -> Shattered Hour: Bellkeeper -> (Hard only) Metronome Court: Overclocked Tempo Tyrant
    //
    // The run travels: when the next fight belongs to another arena, the director carries the run (Carry) across the scene
    // load and the new arena's director picks it up. Automated suites (-lastbellOut) keep the whole run in the arena it
    // started in and skip the tutorial, unless they ask for the campaign test (-lastbellCampaignTest).
    public static class Campaign
    {
        public sealed class Area
        {
            public readonly string Name, Map, Boss; public readonly string[] Minibosses;
            public Area(string name, string map, string boss, params string[] minibosses) { Name = name; Map = map; Boss = boss; Minibosses = minibosses; }
            public int BossIndex => Find(Boss);
            public int[] MinibossIndices => Minibosses.Select(Find).Where(i => i >= 0).ToArray();
        }

        // In area order (the "area n" notes in RunDirector.Bosses). Entries are RunDirector.Bosses name prefixes.
        public static readonly Area[] Areas =
        {
            new Area("Skyward Spire", RunDirector.AurelionMap, "Aurelion", "Chimebound Oracle", "Dial Warden", "Pendulum Sentinel"),
            new Area("Brass Foundry", RunDirector.CrucivarMap, "Crucivar", "Escapement Twins", "Rivet Cantor", "Crucible Skater"),
            new Area("Verdant Chimes", RunDirector.SylvaraMap, "Sylvara", "Briar Metronome", "Silt Carillon", "Vesper Seedkeeper"),
        };
        public const string FinalMap = "shattered-hour", FinalArea = "Shattered Hour";
        public const string BonusName = "Overclocked Tempo Tyrant (bonus)";
        public const int TutorialArea = -1, FinalAreaIndex = 100, BonusArea = 101, OtherArea = 102;

        static int Find(string prefix) => Array.FindIndex(RunDirector.Bosses, b => b.Name.StartsWith(prefix, StringComparison.Ordinal));
        public static int FinalIndex => Array.FindIndex(RunDirector.Bosses, b => b.Final);
        public static int BonusIndex => Array.FindIndex(RunDirector.Bosses, b => b.Name == BonusName);

        static bool Arg(string a) => Environment.GetCommandLineArgs().Any(x => x.Equals(a, StringComparison.OrdinalIgnoreCase));
        static readonly bool Automated = Arg("-lastbellOut");
        public static readonly bool TestRequested = Arg("-lastbellCampaignTest");
        // move between arenas (off in the other automated suites: they hold the director across the whole run)
        public static bool Travel = !Automated || TestRequested;

        // ---------------- order ----------------
        // Tutorial (optional), then each area's minibosses in a random order followed by its boss, then the final boss.
        public static int[] CreateOrder(System.Random random, bool tutorial)
        {
            random = random ?? new System.Random(Guid.NewGuid().GetHashCode());
            var order = new List<int>();
            if (tutorial && RunDirector.TutorialPractice >= 0) order.Add(RunDirector.TutorialPractice);
            foreach (var a in Areas)
            {
                var mini = a.MinibossIndices;
                for (int i = mini.Length - 1; i > 0; i--) { int j = random.Next(i + 1); (mini[i], mini[j]) = (mini[j], mini[i]); }
                order.AddRange(mini);
                if (a.BossIndex >= 0) order.Add(a.BossIndex);
            }
            order.Add(FinalIndex);
            return order.ToArray();
        }

        // A start order is valid when it is CreateOrder's shape: optional tutorial, each area's minibosses in any order then
        // its boss, then the final boss. (The bonus is only ever appended during a run.)
        public static bool Valid(int[] order)
        {
            if (order == null || order.Length == 0) return false;
            int at = 0;
            if (order[0] == RunDirector.TutorialPractice) at = 1;
            foreach (var a in Areas)
            {
                var mini = a.MinibossIndices;
                if (at + mini.Length + 1 > order.Length) return false;
                if (!order.Skip(at).Take(mini.Length).OrderBy(i => i).SequenceEqual(mini.OrderBy(i => i))) return false;
                at += mini.Length;
                if (order[at++] != a.BossIndex) return false;
            }
            return at == order.Length - 1 && order[at] == FinalIndex;
        }

        // ---------------- where each fight belongs ----------------
        public static int AreaOf(int encounter)
        {
            if (encounter < 0) return OtherArea;
            if (encounter == RunDirector.TutorialPractice) return TutorialArea;
            if (encounter == FinalIndex) return FinalAreaIndex;
            if (encounter == BonusIndex) return BonusArea;
            for (int a = 0; a < Areas.Length; a++)
                if (Areas[a].BossIndex == encounter || Array.IndexOf(Areas[a].MinibossIndices, encounter) >= 0) return a;
            return OtherArea;
        }
        public static bool IsAreaBoss(int encounter) => Areas.Any(a => a.BossIndex == encounter);
        public static string AreaName(int encounter)
        {
            int a = AreaOf(encounter);
            return a >= 0 && a < Areas.Length ? Areas[a].Name : a == TutorialArea ? "Tutorial" : a == FinalAreaIndex ? FinalArea : a == BonusArea ? "Bonus" : RunMaps.CurrentName;
        }
        // the run map id this fight is played on
        public static string MapFor(int encounter)
        {
            int a = AreaOf(encounter);
            if (a >= 0 && a < Areas.Length) return Areas[a].Map;
            if (a == FinalAreaIndex) { int m = RunMaps.IndexOf(FinalMap); return RunMaps.InBuild(m) ? FinalMap : "test"; }
            if (a == TutorialArea || a == BonusArea) return RunDirector.TempoTyrantMap;
            return encounter >= 0 && encounter < RunDirector.Bosses.Length ? RunDirector.Bosses[encounter].Map ?? "test" : "test";
        }
        // the intermission shop opens before each area boss and the final boss (the core loop: minibosses -> shop -> boss)
        public static bool ShopBefore(int encounter) => IsAreaBoss(encounter) || encounter == FinalIndex;
        // potions refill with health before an area boss and the final boss; minibosses refill health only
        public static bool RefillsPotions(int encounter) => ShopBefore(encounter);

        // the contiguous stretch of the order that shares position pos's area (the HUD track shows one arena at a time)
        public static (int start, int end) Cluster(int[] order, int pos)
        {
            if (order == null || order.Length == 0) return (0, -1);
            pos = Mathf.Clamp(pos, 0, order.Length - 1); int area = AreaOf(order[pos]), s = pos, e = pos;
            while (s > 0 && AreaOf(order[s - 1]) == area) s--;
            while (e < order.Length - 1 && AreaOf(order[e + 1]) == area) e++;
            return (s, e);
        }
        // "Skyward Spire · Miniboss 2 of 3", "Skyward Spire · Area boss", "Final boss", "Bonus fight", "Tutorial"
        public static string StageLabel(int[] order, int pos)
        {
            if (order == null || pos < 0 || pos >= order.Length) return "";
            int enc = order[pos], area = AreaOf(enc);
            if (area == TutorialArea) return "Tutorial";
            if (area == FinalAreaIndex) return "Final boss";
            if (area == BonusArea) return "Bonus fight";
            var (s, e) = Cluster(order, pos);
            if (IsAreaBoss(enc)) return AreaName(enc) + " · Area boss";
            int minis = Enumerable.Range(s, e - s + 1).Count(i => !IsAreaBoss(order[i]));
            return $"{AreaName(enc)} · Miniboss {pos - s + 1} of {minis}";
        }

        // ---------------- saved progress ----------------
        // Saved between sessions in normal play. Automated suites never read or write the saved values: the tutorial counts
        // as not done and the Overclocked card as unlocked (so every Boss Rush card stays launchable in the menu tests).
        const string TutorialKey = "LastBell.Campaign.TutorialDone", OverclockedKey = "LastBell.Campaign.OverclockedUnlocked";
        static bool sessionTutorial, sessionOverclocked = Automated;
        public static bool TutorialDone
        {
            get { if (Automated) return sessionTutorial; try { return PlayerPrefs.GetInt(TutorialKey, 0) == 1; } catch { return sessionTutorial; } }
            set { sessionTutorial = value; if (!Automated) try { PlayerPrefs.SetInt(TutorialKey, value ? 1 : 0); PlayerPrefs.Save(); } catch { } }
        }
        public static bool OverclockedUnlocked
        {
            get { if (Automated) return sessionOverclocked; try { return PlayerPrefs.GetInt(OverclockedKey, 0) == 1; } catch { return sessionOverclocked; } }
            set { sessionOverclocked = value; if (!Automated) try { PlayerPrefs.SetInt(OverclockedKey, value ? 1 : 0); PlayerPrefs.Save(); } catch { } }
        }
        // the first run starts with the tutorial; later runs start in Skyward Spire. The other automated suites never get it.
        public static bool TutorialFirst => !TutorialDone && (!Automated || TestRequested);
        public const string LockedReason = "Beat the Bellkeeper on Hard to unlock.";

        // ---------------- travel ----------------
        // The run state that survives the scene load into the next arena (RunDirector.TravelTo / Start).
        public sealed class Carry
        {
            public string ClassName; public Difficulty Difficulty; public int AllyCount;
            public int[] Order; public int Next;
            public int RunsStarted, RunTicks, EncountersCleared;
            public bool Fresh;              // a Retry that starts a fresh run in another arena
            public int Potions = -1; public int[] AllyPotions;
            public string Scene;            // the run scene this carry is for
        }
        public static Carry Pending;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Clear() { Pending = null; }
    }
}
