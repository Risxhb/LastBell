using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LastBell.Menu;
using UnityEngine;

namespace LastBell.Run
{
    // A whole run played by bots, through Start Run's own launch path (overview request for Chzy18, 8 Nov):
    //   LastBell.exe -batchmode -lastbellFullRun -lastbellShopBot -lastbellSeed 1 -lastbellOut <folder>
    // Hard, a party of 4 (the player is a Perfect bot too, the Bellkeeper's own strat bot in the final fight), the shop before
    // each main boss on (the greedy shop bot buys for the player). The four main bosses are fought for real, with no top-ups.
    // The run loop always puts the minibosses in a campaign, so they and the Hard win's bonus fight are ended with the dev defeat.
    // Between fights it checks the flow: arenas and scene loads, health refilled each fight, potions refilled before main
    // bosses and carried otherwise, nobody still down at the next fight, the shop opening with stock, and no errors.
    // A wipe ends the run (Results), so it Retries: the fresh run skips the bosses already beaten and fights the wiped one
    // again, up to -lastbellFullRunTries tries each (default 2); after that the boss is skipped so the rest of the run is still seen.
    // Writes fullrun-report.txt (checks, then one line per fight); exits 0 when every check passed.
    public sealed class FullRunAutoTest : MonoBehaviour
    {
        static string[] Args => Environment.GetCommandLineArgs();
        static bool Arg(string a) => Args.Any(x => x.Equals(a, StringComparison.OrdinalIgnoreCase));
        static string ArgValue(string a) { var v = Args; int i = Array.FindIndex(v, x => x.Equals(a, StringComparison.OrdinalIgnoreCase)); return i >= 0 && i + 1 < v.Length ? v[i + 1] : null; }
        public static bool Requested => Arg("-lastbellFullRun");
        static string OutDir => ArgValue("-lastbellOut") ?? Path.Combine(Application.persistentDataPath, "fullrun-test");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Requested || FindAnyObjectByType<FullRunAutoTest>()) return;
            var go = new GameObject("Full run autotest"); DontDestroyOnLoad(go); go.AddComponent<FullRunAutoTest>();
        }

        const int Allies = 3;
        const float FightLimit = 1200f;   // game seconds before a real fight counts as stuck
        readonly List<string> lines = new List<string>(), fights = new List<string>();
        int pass, fail, errors, seed, tries; string dir, cls;
        RunDirector d;
        float wall0;

        void Start()
        {
            dir = OutDir; Directory.CreateDirectory(dir);
            seed = int.TryParse(ArgValue("-lastbellSeed"), out int s) ? s : 1;
            tries = int.TryParse(ArgValue("-lastbellFullRunTries"), out int n) ? Mathf.Max(1, n) : 2;
            cls = ArgValue("-lastbellFullRunClass") ?? "Tollbreaker";
            Application.logMessageReceived += OnLog;
            Application.runInBackground = true;
            StartCoroutine(Main());
        }
        void OnDestroy() { Application.logMessageReceived -= OnLog; }
        void OnLog(string msg, string stack, LogType t) { if ((t == LogType.Exception || t == LogType.Error) && !(stack ?? "").Contains("UnityEditor.")) { errors++; if (errors <= 40) lines.Add("      LOG " + t + ": " + msg); } }
        void Check(string name, bool ok, string detail = "")
        {
            if (ok) pass++; else fail++;
            var line = $"{(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : "  — " + detail)}";
            lines.Add(line); Debug.Log("[fullrun-test] " + line);
        }
        void Note(string s) { lines.Add("      " + s); Debug.Log("[fullrun-test] " + s); }
        // waits for cond, re-reading the director (each arena's scene has its own); false on timeout
        IEnumerator Until(Func<bool> cond, float seconds, string what, bool[] ok = null)
        {
            float t = 0;
            while (t < seconds)
            {
                d = RunDirector.Instance; if (d && cond()) { if (ok != null) ok[0] = true; yield break; }
                t += Time.unscaledDeltaTime; yield return null;
            }
            if (ok != null) ok[0] = false;
            Check("in time: " + what, false, $"{seconds}s, state {(d ? d.State.ToString() : "no director")}");
        }
        static string MapId => RunMaps.Current >= 0 ? RunMaps.All[RunMaps.Current].Id : "?";
        static string Short(int enc) => RunDirector.Bosses[enc].Name.Split('(')[0].Trim();
        static bool IsMain(int enc) => Campaign.IsAreaBoss(enc) || enc == Campaign.FinalIndex;
        IEnumerable<PlayerController> Party => d && d.World != null ? d.World.Players.Where(p => p) : Enumerable.Empty<PlayerController>();

        // the player's bot: the Bellkeeper's own strat bot in the final fight (found by name: it ships with the Ultimate's
        // branch), the Perfect ally bot with attacks elsewhere
        IInputSource DrivePlayer(int enc)
        {
            int count = d.World.Players.Count;
            if (enc == Campaign.FinalIndex)
            {
                var party = Type.GetType("LastBell.Run.UltimateParty"); var skill = Type.GetType("LastBell.Run.UltimateBot+Skill");
                var drive = party?.GetMethod("Drive");
                if (drive != null && skill != null)
                {
                    var bot = drive.Invoke(null, new object[] { d.Player, 0, count, Enum.Parse(skill, "Perfect") }) as IInputSource;
                    if (bot != null) { d.InputOverride = bot; return bot; }
                }
                Note("final fight: the Ultimate's strat bot isn't in this build, the player uses the ally bot");
            }
            var me = new AllyBot(count - 1, count, BotMode.Perfect);
            d.InputOverride = me; d.Player.Input = me; return me;
        }

        IEnumerator Main()
        {
            wall0 = Time.realtimeSinceStartup;
            yield return new WaitForSeconds(1.5f);
            UnityEngine.Random.InitState(seed);
            Campaign.Travel = true; Campaign.TutorialDone = true;
            RunDifficulty.Selected = Difficulty.Hard; RunParty.Selected = Allies;
            AllyBot.AllyMode = BotMode.Perfect; AllyBot.Attacks = true;
            Note($"full run: {cls}, Hard, {Allies} allies, seed {seed}, up to {tries} tries per main boss, shop {(Shop.RunShopFlow.Enabled ? "on" : "OFF")}{(Shop.RunShopFlow.BotBuys ? " (bot buys)" : "")}");
            Check("shop is on for the run (-lastbellShopBot)", Shop.RunShopFlow.Enabled && Shop.RunShopFlow.BotBuys);
            Check("Start Run's launch accepted", MenuLaunch.Normal(cls));

            var beaten = new HashSet<int>(); var wipes = new Dictionary<int, int>(); var skipped = new HashSet<int>();
            int attempt = 0; bool victory = false;
            while (attempt < 12)
            {
                attempt++;
                var ok = new bool[1];
                yield return Until(() => d.State == RunState.Ready, 90, $"run {attempt}: first Ready", ok);
                if (!ok[0]) break;
                var order = (int[])d.EncounterOrder.Clone();
                Note($"run {attempt}: order " + string.Join(" > ", order.Select(Short)));
                Check($"run {attempt}: Hard, party of {Allies + 1}, no tutorial", d.Difficulty == Difficulty.Hard && d.World.Players.Count == Allies + 1 && order[0] != RunDirector.TutorialPractice,
                    $"{d.Difficulty}, {d.World.Players.Count} players, first {Short(order[0])}");
                int runs = d.RunsStarted; bool wiped = false;
                int[] carried = null; float leftAt = -1; string prevMap = null;
                for (int pos = 0; pos < order.Length; pos++)
                {
                    int enc = order[pos]; string tag = $"run {attempt} #{pos + 1} {Short(enc)}";
                    yield return Until(() => (d.State == RunState.Ready || d.State == RunState.Intro || d.State == RunState.Combat) && d.Encounter == enc && d.RunsStarted == runs, 120, "reach " + tag, ok);
                    if (!ok[0]) { Finish(victory); yield break; }
                    float load = leftAt >= 0 ? Time.realtimeSinceStartup - leftAt : 0;
                    bool travelled = prevMap != null && prevMap != MapId; prevMap = MapId;
                    Check($"{tag}: on {Campaign.MapFor(enc)}", MapId == Campaign.MapFor(enc), MapId);
                    if (travelled) Note($"{tag}: arena load {load:0.0} s (wall)");

                    // ---- Ready: the shop before main bosses ----
                    if (d.State == RunState.Ready)
                    {
                        bool shop = Shop.RunShopFlow.Showing, wantShop = Campaign.ShopBefore(enc);
                        Check($"{tag}: shop {(wantShop ? "opens" : "stays closed")}", shop == wantShop);
                        if (shop)
                        {
                            var run = Shop.RunShopFlow.Instance.Run; var me = run.Slot(0);
                            int stock = me.Stock.Take(me.StockCount).Count(x => x != null); int chimes = me.Chimes, owned = me.Items.Count;
                            Check($"{tag}: shop has stock for the player", stock > 0, $"{stock} cards, {chimes} Chimes (bot allies have no shop slot: {run.Slots.Count} slot)");
                            yield return Until(() => !Shop.RunShopFlow.Showing || d.State != RunState.Ready, 200, tag + " shop closes");
                            Note($"{tag}: shop visit {run.Visit}: {chimes} Chimes -> {me.Chimes}, items {owned} -> {me.Items.Count}: {string.Join(", ", me.Items.Select(o => o.Def.Name))}");
                        }
                        if (d.State == RunState.Ready) d.RequestReady();
                    }
                    yield return Until(() => d.State == RunState.Combat && d.Boss, 60, "combat " + tag, ok);
                    if (!ok[0]) { Finish(victory); yield break; }

                    // ---- between-fight state at the pull ----
                    var party = Party.ToList();
                    Check($"{tag}: party of {Allies + 1}, nobody down", party.Count == Allies + 1 && party.All(p => !p.Down && !p.Dead), string.Join(" ", party.Select(p => p.Down ? "down" : p.Dead ? "dead" : "up")));
                    if (pos > 0) Check($"{tag}: health refilled", party.All(p => p.Hp == p.MaxHealth), string.Join(" ", party.Select(p => $"{p.Hp}/{p.MaxHealth}")));
                    if (Campaign.RefillsPotions(enc)) Check($"{tag}: potions refilled", party.All(p => p.Potions == PlayerController.MaxPotions + Shop.ItemHooks.ExtraPotions(p)), string.Join(" ", party.Select(p => p.Potions)) + " (Spare Flask adds 1)");
                    else if (carried != null) Check($"{tag}: potions carried over", party.Select(p => p.Potions).SequenceEqual(carried), $"{string.Join(" ", carried)} -> {string.Join(" ", party.Select(p => p.Potions))}");

                    // ---- the fight ----
                    bool real = IsMain(enc) && !beaten.Contains(enc) && !skipped.Contains(enc);
                    var b = d.Boss; float t0 = Time.time, w0 = Time.realtimeSinceStartup;
                    int[] drunk0 = party.Select(p => p.PotionsDrunk).ToArray(); int downs = 0; var wasDown = party.Select(p => p.Down).ToArray();
                    if (real)
                    {
                        DrivePlayer(enc);
                        while (d.State == RunState.Combat && Time.time - t0 < FightLimit)
                        {
                            for (int i = 0; i < party.Count; i++) { bool dn = party[i] && party[i].Down; if (dn && !wasDown[i]) downs++; wasDown[i] = dn; }
                            yield return null;
                        }
                        if (d.State == RunState.Combat) { Check($"{tag}: fight ends within {FightLimit:0} s", false, $"boss {b.Hp}/{b.MaxHp}, phase {b.PhaseNumber}"); if (b.PhaseNumber < b.PhaseCount) b.StartAtPhase(b.PhaseCount); b.DebugDefeat(); }
                    }
                    else
                    {
                        if (b.PhaseNumber < b.PhaseCount) b.StartAtPhase(b.PhaseCount);   // a miniboss holds at 50 % until its shift
                        b.DebugDefeat();
                    }
                    yield return Until(() => d.State != RunState.Combat && d.State != RunState.Downed, 30, "end of " + tag);   // a wipe goes Combat -> Downed -> Results
                    float gs = Time.time - t0, ws = Time.realtimeSinceStartup - w0;
                    party = Party.ToList();
                    int used = party.Select((p, i) => i < drunk0.Length ? p.PotionsDrunk - drunk0[i] : 0).Sum(), revives = party.Sum(p => p.Revives);
                    int downAtEnd = party.Count(p => p.Down || p.Dead);
                    string kind = enc == Campaign.BonusIndex ? "skipped (bonus)" : !IsMain(enc) ? "skipped (miniboss)" : !real ? "skipped (beaten or out of tries)" : null;
                    if (d.State == RunState.Results && !d.Victory)
                    {
                        wipes[enc] = (wipes.TryGetValue(enc, out int w) ? w : 0) + 1;
                        string cause = d.World != null ? d.World.LastDamageSource : "?";
                        fights.Add($"{tag} | WIPE | {gs:0} s game, {ws:0} s wall | boss {b.Hp}/{b.MaxHp} ({100f * b.Hp / Mathf.Max(1, b.MaxHp):0}%), phase {b.PhaseNumber}/{b.PhaseCount} | downs {downs}, revives {revives}, potions {used} | last hit: {cause}");
                        Note($"{tag}: wipe {wipes[enc]} of {tries} at {gs:0} s, boss {100f * b.Hp / Mathf.Max(1, b.MaxHp):0}% in phase {b.PhaseNumber}, last hit {cause}");
                        if (wipes[enc] >= tries) { skipped.Add(enc); Note($"{tag}: out of tries, skipped in the next run"); }
                        wiped = true; break;
                    }
                    if (real) beaten.Add(enc);
                    fights.Add(kind != null ? $"{tag} | {kind}" :
                        $"{tag} | CLEAR | {gs:0} s game, {ws:0} s wall | downs {downs}, revives {revives}, potions {used}, down at the end {downAtEnd}");
                    if (real) Note($"{tag}: cleared in {gs:0} s ({ws:0} s wall), {downs} downs, {revives} revives, {used} potions, {downAtEnd} still down at the end");
                    carried = party.Select(p => p.Potions).ToArray(); leftAt = Time.realtimeSinceStartup;
                    if (enc == Campaign.FinalIndex && d.EncounterOrder.Length > order.Length) { order = (int[])d.EncounterOrder.Clone(); Note("Hard win: the bonus fight was added"); }
                    if (d.State == RunState.Results) break;
                }
                if (wiped)
                {
                    Check($"run {attempt}: Retry accepted after the wipe", d.State == RunState.Results && d.RequestRetry());
                    yield return Until(() => d.State == RunState.Ready && d.RunsStarted == runs + 1, 120, "fresh run after the wipe", ok);
                    if (!ok[0]) break;
                    continue;
                }
                yield return Until(() => d.State == RunState.Results, 30, $"run {attempt}: results");
                victory = d && d.State == RunState.Results && d.Victory;
                Check($"run {attempt}: ends in victory", victory, d ? $"state {d.State}, cleared {d.EncountersCleared}" : "no director");
                break;
            }
            foreach (var kv in wipes) Note($"{Short(kv.Key)}: {kv.Value} wipe(s){(skipped.Contains(kv.Key) ? ", then skipped" : beaten.Contains(kv.Key) ? ", then beaten" : "")}");
            Note($"main bosses beaten for real: {string.Join(", ", beaten.Select(Short))}{(skipped.Count > 0 ? "; skipped after wipes: " + string.Join(", ", skipped.Select(Short)) : "")}");
            Finish(victory);
        }

        void Finish(bool victory)
        {
            Check("no errors or exceptions logged", errors == 0, errors + " logged");
            float total = Time.realtimeSinceStartup - wall0;
            lines.Insert(0, $"Full run autotest — {pass} passed, {fail} failed · {(victory ? "victory" : "no victory")} · {total / 60f:0.0} min wall · seed {seed}  ({DateTime.Now:yyyy-MM-dd HH:mm})");
            lines.Add(""); lines.Add("Fights (game seconds; wall seconds in batch mode):"); lines.AddRange(fights.Select(f => "  " + f));
            if (d) { lines.Add(""); lines.Add("Director log (last 60):"); lines.AddRange(d.Log.Skip(Math.Max(0, d.Log.Count - 60)).Select(l => "  " + l)); }
            File.WriteAllLines(Path.Combine(dir, "fullrun-report.txt"), lines);
            Debug.Log($"[fullrun-test] RESULT {pass} passed, {fail} failed");
            Application.Quit(fail == 0 ? 0 : 1);
        }
    }
}
