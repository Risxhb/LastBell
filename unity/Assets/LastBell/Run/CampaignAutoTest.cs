using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LastBell.Menu;
using UnityEngine;

namespace LastBell.Run
{
    // The whole campaign end to end in the real game, through Start Run's own launch path:
    //   LastBell.exe -lastbellCampaignTest -lastbellShopBot -lastbellOut <folder>
    // Run 1 (Hard, Arcanist): tutorial on Metronome Court -> each arena's minibosses and boss -> Bellkeeper in the
    // Shattered Hour -> the Overclocked Tempo Tyrant bonus (lost on purpose: the run stays won) -> Retry starts a fresh run
    // in Skyward Spire. Run 2 (Medium, no travel): a Bellkeeper win adds no bonus. Fights are ended with the dev defeat;
    // the checks are about the flow: order, arenas, shop visits, carried potions and Chimes, unlocks, the bonus boss.
    // Writes campaign-report.txt and screenshots; exits 0 when every check passed.
    public sealed class CampaignAutoTest : MonoBehaviour
    {
        public static bool Requested => Campaign.TestRequested;
        static string OutDir
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.FindIndex(args, a => a.Equals("-lastbellOut", StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Application.persistentDataPath, "campaign-test");
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Requested || FindAnyObjectByType<CampaignAutoTest>()) return;
            var go = new GameObject("Campaign autotest"); DontDestroyOnLoad(go); go.AddComponent<CampaignAutoTest>();
        }

        readonly List<string> lines = new List<string>();
        int pass, fail, errors, shots; string dir;
        RunDirector d;

        void Start()
        {
            dir = OutDir; Directory.CreateDirectory(dir);
            Application.logMessageReceived += OnLog;
            Application.runInBackground = true;
            StartCoroutine(Main());
        }
        void OnDestroy() { Application.logMessageReceived -= OnLog; }
        void OnLog(string msg, string stack, LogType t) { if ((t == LogType.Exception || t == LogType.Error) && !(stack ?? "").Contains("UnityEditor.")) { errors++; lines.Add("      LOG " + t + ": " + msg); } }
        void Check(string name, bool ok, string detail = "")
        {
            if (ok) pass++; else fail++;
            var line = $"{(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : "  — " + detail)}";
            lines.Add(line); Debug.Log("[campaign-test] " + line);
        }
        void Note(string s) { lines.Add("      " + s); Debug.Log("[campaign-test] " + s); }
        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, $"{++shots:00}_{name}.png"), tex.EncodeToPNG()); Destroy(tex);
        }
        // waits for cond (re-reading the director, which a map load replaces); false on timeout
        IEnumerator Until(Func<bool> cond, float seconds, string what)
        {
            float t = 0;
            while (t < seconds)
            {
                d = RunDirector.Instance; if (d && cond()) yield break;
                if (d && d.Player && !d.Player.Down) d.Player.DebugSetHp(PlayerController.MaxHp);   // the flow is under test, not survival
                t += Time.unscaledDeltaTime; yield return null;
            }
            Check("in time: " + what, false, $"{seconds}s, state {(d ? d.State.ToString() : "no director")}");
        }
        static string MapId => RunMaps.Current >= 0 ? RunMaps.All[RunMaps.Current].Id : "?";
        static string Short(int enc) => RunDirector.Bosses[enc].Name.Split('(')[0].Trim();

        IEnumerator Main()
        {
            yield return new WaitForSeconds(1.5f);
            StaticChecks();

            // ---------- run 1: Hard, the full campaign with travel ----------
            Campaign.OverclockedUnlocked = false;
            int court = RunMaps.IndexOf(RunDirector.TempoTyrantMap);
            Check("Overclocked Boss Rush card is locked before the Hard win", TestRushSetup.Incompatibility(court, Campaign.BonusIndex) == Campaign.LockedReason);
            RunDifficulty.Selected = Difficulty.Hard;
            Check("Start Run is enabled on the menu", ConfrontationMenu.StartRunEnabled);
            Check("Start Run's launch accepted", MenuLaunch.Normal("Arcanist"));
            yield return Until(() => d.State == RunState.Ready, 40, "first Ready");
            if (!d) { Finish(); yield break; }
            var order = (int[])d.EncounterOrder.Clone();
            Note("order: " + string.Join(" > ", order.Select(Short)));
            Check("first run starts with the tutorial on Metronome Court", order[0] == RunDirector.TutorialPractice && MapId == RunDirector.TempoTyrantMap && d.Encounter == order[0] && d.Mode == RunMode.Run);
            Check("the run order is a campaign: 1 + 3 x (3 + 1) + Bellkeeper", Campaign.Valid(order) && order.Length == 14, order.Length.ToString());
            Check("the run copied Hard", d.Difficulty == Difficulty.Hard);
            yield return Shot("ready-tutorial");
            int runs = d.RunsStarted;

            int arrivals = 0, shopVisits = 0; int potionsBefore = -1, chimesBefore = -1, itemsBefore = -1;
            for (int pos = 0; pos < order.Length; pos++)
            {
                int enc = order[pos]; string tag = $"{pos}:{Short(enc)}";
                yield return Until(() => d.State == RunState.Ready || d.State == RunState.Intro || d.State == RunState.Combat, 40, "reach fight " + tag);
                if (!d) break;
                Check("fight " + tag + " is on " + Campaign.MapFor(enc), d.Encounter == enc && MapId == Campaign.MapFor(enc), $"enc {Short(d.Encounter)}, map {MapId}");
                Check("same run carried " + tag, d.RunsStarted == runs && d.EncounterOrder.SequenceEqual(order.Take(d.EncounterOrder.Length)) && d.EncountersCleared == pos, $"runs {d.RunsStarted}, cleared {d.EncountersCleared}");
                if (d.State == RunState.Ready)
                {
                    bool shop = Shop.RunShopFlow.Showing;
                    if (shop) shopVisits++;
                    Check("shop " + (Campaign.ShopBefore(enc) ? "opens" : "stays closed") + " before " + tag, shop == (Campaign.ShopBefore(enc) && Shop.RunShopFlow.Enabled));
                    if (potionsBefore >= 0 && !Campaign.ShopBefore(enc)) { Check("potions carried into the new arena " + tag, d.Player.Potions == potionsBefore, $"{potionsBefore} -> {d.Player.Potions}"); arrivals++; }
                    if (chimesBefore >= 0 && Shop.RunShopFlow.Instance && Shop.RunShopFlow.Instance.Run != null)
                    {
                        var me = Shop.RunShopFlow.Instance.Run.Slot(0);
                        Check("Chimes and items carried into the new arena " + tag, me.Chimes >= chimesBefore && me.Items.Count == itemsBefore || Campaign.ShopBefore(enc), $"chimes {chimesBefore} -> {me.Chimes}, items {itemsBefore} -> {me.Items.Count}");
                    }
                    potionsBefore = chimesBefore = itemsBefore = -1;
                    if (pos == 1 || enc == Campaign.FinalIndex) yield return Shot("ready-" + Short(enc).Replace(' ', '-').ToLowerInvariant());
                    if (!shop) d.RequestReady();
                }
                yield return Until(() => d.State == RunState.Combat && d.Boss, 30, "combat " + tag);
                if (!d || !d.Boss) break;
                if (pos == 1 && d.Player) Check("potions carried across the tutorial travel (3 set before it)", d.Player.Potions == 3, d.Player.Potions.ToString());
                if (pos > 0 && pos < order.Length - 1 && !Campaign.RefillsPotions(enc) && !Campaign.RefillsPotions(order[pos - 1]) && AreaSame(order[pos - 1], enc))
                    Check("potions carry over between minibosses " + tag, d.Player.Potions == 2, d.Player.Potions.ToString());
                if (Campaign.RefillsPotions(enc)) Check("potions refilled before " + tag, d.Player.Potions >= PlayerController.MaxPotions, d.Player.Potions.ToString());
                if (pos == 2 || Campaign.IsAreaBoss(enc)) yield return Shot("combat-" + Short(enc).Replace(' ', '-').ToLowerInvariant());
                // setup for the next checks: spend potions so a refill or a carry shows
                if (Campaign.AreaOf(enc) == Campaign.TutorialArea) d.Player.CarryPotions(3); else d.Player.CarryPotions(2);
                d.Boss.DebugDefeat();
                yield return Until(() => d.State == RunState.Cleared, 5, "cleared " + tag);
                if (enc == RunDirector.TutorialPractice) Check("tutorial marked done", Campaign.TutorialDone);
                if (enc == Campaign.FinalIndex)
                {
                    Check("Hard Bellkeeper win unlocks the Overclocked card", Campaign.OverclockedUnlocked && TestRushSetup.Incompatibility(court, Campaign.BonusIndex) == null);
                    Check("Hard Bellkeeper win adds the bonus fight", d.EncounterOrder.Length == order.Length + 1 && d.EncounterOrder.Last() == Campaign.BonusIndex);
                    order = (int[])d.EncounterOrder.Clone();
                    yield return new WaitForSeconds(0.8f); yield return Shot("cleared-bellkeeper-hard");
                }
                if (pos < order.Length - 1 && !AreaSame(enc, order[pos + 1]))
                {
                    potionsBefore = d.Player.Potions;
                    var run = Shop.RunShopFlow.Instance ? Shop.RunShopFlow.Instance.Run : null;
                    if (run != null) { chimesBefore = run.Slot(0).Chimes; itemsBefore = run.Slot(0).Items.Count; }
                }
                if (pos == order.Length - 1) break;
                if (pos + 1 < order.Length && order[pos + 1] == Campaign.BonusIndex) { pos++; break; }
            }
            Note($"shop visits seen {shopVisits}, arena arrivals checked {arrivals}");

            // ---------- the bonus fight ----------
            yield return Until(() => d.State == RunState.Ready && d.Encounter == Campaign.BonusIndex, 40, "bonus Ready");
            if (d && d.Encounter == Campaign.BonusIndex)
            {
                Check("bonus fight is on Metronome Court", MapId == RunDirector.TempoTyrantMap);
                d.RequestReady();
                yield return Until(() => d.State == RunState.Combat && d.Boss, 10, "bonus combat");
                var tt = d.Boss as TempoTyrantEncounter;
                Check("bonus boss is the Overclocked Tempo Tyrant", tt != null && tt.Overclocked && tt.MaxHp == TempoTyrantEncounter.OverclockedHp && tt.DisplayName.Contains("Overclocked"), tt ? $"{tt.DisplayName}, {tt.MaxHp} HP" : "none");
                yield return Shot("bonus-overclocked");
                if (tt != null)
                {
                    tt.DebugSetHpFraction(0.45f);
                    float t = 0; while (t < 30 && d.State == RunState.Combat && !tt.Frenzy) { if (d.Player) d.Player.DebugSetHp(PlayerController.MaxHp); t += Time.deltaTime; yield return null; }
                    Check("Overclocked: Broken Cadence below 50%", tt.Frenzy, $"hp {tt.Hp}/{tt.MaxHp} after {t:0.0}s");
                    yield return new WaitForSeconds(1.5f); yield return Shot("bonus-broken-cadence");
                    d.Player.ForceDown("Campaign test: bonus loss");
                }
                yield return Until(() => d.State == RunState.Results, 10, "bonus results");
                Check("losing the bonus still ends the run in victory", d.State == RunState.Results && d.Victory && d.BonusLost);
                yield return new WaitForSeconds(0.6f); yield return Shot("results-bonus-lost");

                // ---------- retry: a fresh run in Skyward Spire ----------
                Check("Retry accepted", d.RequestRetry());
                yield return Until(() => d.State == RunState.Ready && d.RunsStarted == runs + 1, 40, "fresh run Ready");
                var fresh = d.EncounterOrder;
                Check("Retry travels to Skyward Spire for a fresh run", MapId == RunDirector.AurelionMap && d.Encounter == fresh[0] && d.EncountersCleared == 0 && d.RunTicks == 0);
                Check("the fresh run has no tutorial and no bonus", Campaign.Valid(fresh) && fresh.Length == 13 && !fresh.Contains(Campaign.BonusIndex));
                Check("fresh run: full potions", d.Player.Potions == PlayerController.MaxPotions, d.Player.Potions.ToString());
                d.SetPaused(true); d.ReturnToMenu(); yield return null;
            }

            // ---------- run 2: Medium, no travel: no bonus ----------
            // Return to menu reloads the main menu (MenuReturnBridge); the second run goes through Start Run again
            float wait = 0; while (wait < 30 && (RunDirector.Instance || !FindAnyObjectByType<ConfrontationMenu>())) { wait += Time.unscaledDeltaTime; yield return null; }
            Check("Main Menu after the run", !RunDirector.Instance && FindAnyObjectByType<ConfrontationMenu>());
            yield return new WaitForSeconds(1f);
            Campaign.Travel = false; RunDifficulty.Selected = Difficulty.Medium;
            Check("Medium run accepted", MenuLaunch.Normal("Tollbreaker"));
            yield return Until(() => d.State == RunState.Ready, 40, "medium Ready");
            if (!d) { Finish(); yield break; }
            var order2 = (int[])d.EncounterOrder.Clone();
            Check("second run has no tutorial", order2[0] != RunDirector.TutorialPractice && order2.Length == 13);
            d.RequestReady();
            for (int pos = 0; pos < order2.Length; pos++)
            {
                if (d.State == RunState.Ready) d.RequestReady();
                yield return Until(() => d.State == RunState.Combat && d.Boss && d.Encounter == order2[pos], 12, "medium fight " + pos);
                if (!d.Boss) break;
                d.Boss.DebugDefeat();
                yield return Until(() => d.State != RunState.Combat, 5, "medium cleared " + pos);
            }
            yield return Until(() => d.State == RunState.Results, 10, "medium results");
            Check("Medium Bellkeeper win: victory, no bonus added", d.State == RunState.Results && d.Victory && !d.BonusLost && d.EncounterOrder.Length == 13 && d.EncountersCleared == 13);
            yield return new WaitForSeconds(0.6f); yield return Shot("results-medium");
            Campaign.Travel = true;

            Check("no errors or exceptions logged", errors == 0, errors + " logged");
            Finish();
        }

        static bool AreaSame(int a, int b) => Campaign.AreaOf(a) == Campaign.AreaOf(b);

        void StaticChecks()
        {
            Check("bonus entry registered, practice only, on Metronome Court", Campaign.BonusIndex >= 0 && !RunDirector.Bosses[Campaign.BonusIndex].InRun && RunDirector.Bosses[Campaign.BonusIndex].Map == RunDirector.TempoTyrantMap);
            Check("Tempo Tyrant practice still resolves to the Tyrant", RunDirector.Bosses[RunDirector.TempoTyrantPractice].Name.StartsWith("Tempo Tyrant"));
            Check("legacy run order unchanged (4 area/final bosses)", RunDirector.RunOrder.Length == 4);
            bool allOk = true; var seen = new HashSet<string>();
            for (int seed = 0; seed < 64; seed++)
            {
                var o = Campaign.CreateOrder(new System.Random(seed), true);
                allOk &= Campaign.Valid(o) && o.Length == 14 && o[0] == RunDirector.TutorialPractice && o.Last() == Campaign.FinalIndex;
                seen.Add(string.Join(",", o));
            }
            Check("64 sampled campaigns are valid: tutorial, areas, Bellkeeper", allOk);
            Check("miniboss order is shuffled", seen.Count > 20, seen.Count + " distinct");
            var sample = Campaign.CreateOrder(new System.Random(3), false);
            Check("without the tutorial: 13 fights", sample.Length == 13 && Campaign.Valid(sample));
            var swapped = (int[])sample.Clone(); (swapped[2], swapped[3]) = (swapped[3], swapped[2]);
            Check("an area boss before its minibosses is rejected", !Campaign.Valid(swapped));
            foreach (var a in Campaign.Areas)
                Check($"{a.Name}: 3 minibosses and its boss on {a.Map}", a.MinibossIndices.Length == 3 && a.BossIndex >= 0 &&
                    a.MinibossIndices.All(i => Campaign.MapFor(i) == a.Map) && Campaign.MapFor(a.BossIndex) == a.Map && RunMaps.InBuild(RunMaps.IndexOf(a.Map)));
            Check("Bellkeeper is fought in the Shattered Hour", Campaign.MapFor(Campaign.FinalIndex) == Campaign.FinalMap);
            Check("shop before area bosses and the Bellkeeper only", Campaign.Areas.All(a => Campaign.ShopBefore(a.BossIndex) && a.MinibossIndices.All(i => !Campaign.ShopBefore(i))) && Campaign.ShopBefore(Campaign.FinalIndex));
            Check("stage labels", Campaign.StageLabel(sample, 0).EndsWith("Miniboss 1 of 3") && Campaign.StageLabel(sample, 3) == "Skyward Spire · Area boss" && Campaign.StageLabel(sample, 12) == "Final boss",
                $"{Campaign.StageLabel(sample, 0)} | {Campaign.StageLabel(sample, 3)} | {Campaign.StageLabel(sample, 12)}");
        }

        void Finish()
        {
            lines.Insert(0, $"Campaign autotest — {pass} passed, {fail} failed  ({DateTime.Now:yyyy-MM-dd HH:mm})");
            File.WriteAllLines(Path.Combine(dir, "campaign-report.txt"), lines);
            Debug.Log($"[campaign-test] RESULT {pass} passed, {fail} failed");
            Application.Quit(fail == 0 ? 0 : 1);
        }
    }
}
