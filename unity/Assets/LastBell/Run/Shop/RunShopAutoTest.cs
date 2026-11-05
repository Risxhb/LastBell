using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LastBell.Run.Shop
{
    // Shop acceptance test inside the real game (shop spec §11 "Autotest"; run-shop-plan §5):
    //   LastBell.exe -lastbellShopTest -lastbellOut <folder> [-lastbellSeed N]
    // Drives the RunDirector and the ShopRun through their public requests and a bot that sends ordinary PlayerInput.
    // Item effects are checked through the real kits wherever a press can show them; where the stock is random the test
    // grants the item to the build directly (marked "test setup"). Writes shop-autotest-report.txt and quits 0 / 1.
    public sealed class RunShopAutoTest : MonoBehaviour
    {
        public static bool Requested => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellShopTest", StringComparison.OrdinalIgnoreCase));
        static RunShopAutoTest active;
        public static void Launch()
        {
            if (active) return;
            var go = new GameObject("Run Shop AutoTest"); DontDestroyOnLoad(go);
            active = go.AddComponent<RunShopAutoTest>();
        }

        sealed class Bot : IInputSource
        {
            public Vector3 Aim; public bool[] press = new bool[5], hold = new bool[5]; public bool potion, dash;
            public PlayerInput Read(PlayerController who)
            {
                var r = new PlayerInput { Aim = Aim, Potion = potion, Dash = dash, P0 = press[0], P1 = press[1], P2 = press[2], P3 = press[3], P4 = press[4],
                    H0 = hold[0] || press[0], H1 = hold[1] || press[1], H2 = hold[2], H3 = hold[3], H4 = hold[4] };
                for (int i = 0; i < 5; i++) press[i] = false; potion = false; dash = false;
                return r;
            }
            public void Clear() { for (int i = 0; i < 5; i++) press[i] = hold[i] = false; potion = false; dash = false; }
        }

        RunDirector d; readonly Bot bot = new Bot();
        readonly List<string> lines = new List<string>(); int pass, fail, errors;
        readonly List<string> errorLines = new List<string>();
        string dir;

        void Start()
        {
            var args = Environment.GetCommandLineArgs(); int i = Array.FindIndex(args, a => a.Equals("-lastbellOut", StringComparison.OrdinalIgnoreCase));
            dir = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Application.persistentDataPath, "shop-autotest");
            Directory.CreateDirectory(dir);
            Application.logMessageReceived += OnLog;
            Application.runInBackground = true;
            StartCoroutine(Main());
        }
        void OnDestroy() { Application.logMessageReceived -= OnLog; }
        void OnLog(string msg, string stack, LogType t)
        {
            if (t == LogType.Error || t == LogType.Exception || t == LogType.Assert) { errors++; if (errorLines.Count < 30) errorLines.Add($"{t}: {msg}\n{stack}"); }
        }
        void Check(string name, bool ok, string detail = "")
        {
            if (ok) pass++; else fail++;
            var line = $"{(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : "  — " + detail)}";
            lines.Add(line); Debug.Log("[shoptest] " + line);
        }
        void Note(string s) { lines.Add("      " + s); Debug.Log("[shoptest] " + s); }
        IEnumerator Wait(float s) { float t = 0; while (t < s) { KeepAlive(); t += Time.deltaTime; yield return null; } }
        IEnumerator Until(Func<bool> c, float timeout, string what)
        {
            float t = 0; while (!c() && t < timeout) { KeepAlive(); t += Time.deltaTime; yield return null; }
            if (!c()) Note($"timeout waiting for {what} ({d?.State})");
        }
        // these checks are about items, not survival: top the player up after every hit (Aurelion v4 hits for up to 90)
        void KeepAlive() { if (d && d.Player && !d.Player.Down && d.Player.Hp < d.Player.MaxHealth) d.Player.DebugSetHp(d.Player.MaxHealth); }
        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, $"shop_{name}.png"), tex.EncodeToPNG()); Destroy(tex);
            Note("screenshot shop_" + name + ".png");
        }

        ShopRun Run => RunShopFlow.Instance ? RunShopFlow.Instance.Run : null;
        PlayerBuild Me => Run?.Slot(0);
        static void Grant(PlayerBuild b, string id, bool tempered = false)   // test setup only
        { if (!b.Has(id)) b.Items.Add(new PlayerBuild.Owned { Def = ItemCatalog.Get(id), Tempered = tempered }); }
        // Campaign runs open with each area's minibosses (no shop before them): clear them with a debug defeat (routing only,
        // no miniboss fight is tested) until the run stands at the next fight that has a shop.
        IEnumerator ToShop(string what)
        {
            for (int n = 0; n < 16 && d.State != RunState.Results && !Campaign.ShopBefore(d.Encounter); n++)
            {
                yield return Until(() => d.State == RunState.Ready || d.State == RunState.Combat, 10, what);
                if (Campaign.ShopBefore(d.Encounter)) break;
                if (d.State == RunState.Ready) { d.RequestReady(); yield return Until(() => d.State == RunState.Combat, 6, what + ": miniboss start"); }
                if (d.State == RunState.Combat) { int enc = d.Encounter; d.Boss.DebugDefeat(); yield return Until(() => d.Encounter != enc || d.State == RunState.Results, 10, what + ": past a miniboss"); }
            }
            yield return Until(() => d.State == RunState.Ready || d.State == RunState.Combat, 10, what);
        }
        int Card(Func<ItemDef, bool> want) { for (int i = 0; i < ShopRun.StockSize; i++) if (Me.Stock[i] != null && want(Me.Stock[i])) return i; return -1; }

        IEnumerator Main()
        {
            Note($"Last Bell shop autotest · {Application.version} · {DateTime.Now:yyyy-MM-dd HH:mm}");
            yield return Until(() => RunDirector.Instance && RunDirector.Instance.State == RunState.Menu, 30, "menu");
            d = RunDirector.Instance; d.InputOverride = bot;
            Check("shop is on for this suite", RunShopFlow.Enabled);
            // accepted icons (29 Oct): all but three items, which wait for a redraw and stay text-only
            // accepted icons (29 and 31 Oct; the Tollbreaker's 20 on 1 Nov): every item has one
            bool IconOk(ItemDef i) => RunShopFlow.ItemIcon(i, true) != null && RunShopFlow.ItemIcon(i, false) != null;
            Check("all 69 item icons load (64 and 128)", ItemCatalog.All.All(IconOk), $"{ItemCatalog.All.Count(IconOk)}");
            Check("no Vanguard items are left", !ItemCatalog.All.Any(i => i.Class == "Vanguard" || i.Id.StartsWith("v.")));
            Check("the pool has 69 items: 20 Tollbreaker, 19 Chimesinger, 20 Arcanist, 10 general; none held", ItemCatalog.All.Length == 69 && ItemCatalog.All.Count(i => i.General) == 10 && RunDirector.Classes.All(c => ItemCatalog.All.Count(i => i.Class == c) == (c == "Chimesinger" ? 19 : 20)) && !ItemCatalog.All.Any(i => i.Held) && !ItemCatalog.All.Any(i => i.Id == "c.twin_bells"));
            Check("Chimes coin and burst icons load", RunShopFlow.Coin != null && RunShopFlow.Burst != null && RunHud.Icon("chimes-128") != null && RunHud.Icon("chimes-burst-32") != null);
            RunShopFlow.SeedOverride = RunShopFlow.SeedOverride ?? 1234;
            RunShopFlow.BotBuys = false;
            yield return Wait(0.5f);

            ModelChecks();

            // ---------------- run 1: Tollbreaker, the whole run through every shop ----------------
            var order = RunDirector.CreateRunOrder(new System.Random(7));
            Check("StartRun accepted", d.StartRun("Tollbreaker", order));
            yield return null;
            Check("no shop before a miniboss", !RunShopFlow.Showing && !Campaign.ShopBefore(d.Encounter), $"{RunDirector.Bosses[d.Encounter].Name}");
            yield return ToShop("shop 1");
            Check("shop 1 opens before the first area boss", RunShopFlow.Showing && Run.Visit == 1 && d.Encounter == shops[0], $"visit {Run?.Visit}");
            Check("shop 1: 3 miniboss clears paid 100 + 20 + 20 each, no stand-in", Me.Chimes == 420 && Run.Ledger.Count == 9, $"{Me.Chimes}: {string.Join(" | ", Run.Ledger)}");
            Check("shop 1: no Keystones in stock", Me.Stock.All(s => s == null || s.Tier != ItemTier.Keystone));
            Check("shop 1: class cards are the player's class, general cards are general",
                Enumerable.Range(0, 3).All(i => Me.Stock[i] == null || Me.Stock[i].Class == "Tollbreaker") && Enumerable.Range(3, 2).All(i => Me.Stock[i] == null || Me.Stock[i].General));
            // a campaign order (since 4 Nov): each area's 3 minibosses, then its boss; the shops are before the area bosses and the Bellkeeper
            Check("shop 1: the special card is a Rare", Me.Stock[ShopRun.SpecialSlot] == null || Me.Stock[ShopRun.SpecialSlot].Tier == ItemTier.Rare);
            var shops = order.Where(Campaign.ShopBefore).ToArray();
            Check("player has the run build", d.Player.Build == Me && ItemHooks.Run == Run);
            yield return Shot("visit1");

            // buy, refusals
            int c = Card(x => x.Tier == ItemTier.Common);
            var bought = c >= 0 ? Me.Stock[c] : null;
            Check("buy a Common for 90", c >= 0 && Run.TryBuy(0, c, out _) && Me.Chimes == 210 && Me.Has(bought.Id), $"{Me.Chimes}");
            Check("a sold card can't be bought twice", !Run.TryBuy(0, c, out var why1), why1);
            // lock and reroll
            int lk = Enumerable.Range(0, 6).FirstOrDefault(i => Me.Stock[i] != null && i != c);
            var lockedDef = Me.Stock[lk];
            Check("lock is free", Run.TryLock(0, lk, out _) && Me.Chimes == 210);
            Check("one lock per visit", !Run.TryLock(0, (lk + 1) % 6, out var why2), why2);
            Check("reroll costs 30", Run.TryReroll(0, out _) && Me.Chimes == 180, $"{Me.Chimes}");
            Me.Chimes = 300;   // test setup: the old 300 start, so the price checks below keep their numbers
            Check("the locked card survives the reroll", Me.Stock[lk] == lockedDef);
            Check("the reroll refilled the sold slot", Me.Stock[c] != null && Me.Stock[c] != bought);
            Check("second reroll costs 45", Run.RerollPrice(Me) == 45 && Run.TryReroll(0, out _) && Me.Chimes == 135, $"{Me.Chimes}");
            Check("third reroll would cost 60", Run.RerollPrice(Me) == 60);
            Check("owned items are never offered again", Me.Stock.All(s => s == null || !Me.Has(s.Id)));
            // temper
            Check("temper costs 110 and upgrades once", Run.TryTemper(0, bought.Id, out _) && Me.Chimes == 25 && Me.Get(bought.Id).Tempered);
            Check("temper only once per item", !Run.TryTemper(0, bought.Id, out var why3), why3);
            int rare = Card(x => x.Tier == ItemTier.Rare); string why4 = null;
            Check("can't buy without the Chimes", rare >= 0 && !Run.TryBuy(0, rare, out why4) && why4 == "not enough Chimes", why4 ?? "no rare");
            Check("no chip-in solo", !Run.TryGift(0, 0, 10, out var why5), why5);
            yield return Shot("visit1_after");

            // ready: the shop closes and the boss starts with the build
            Check("Ready leaves the shop", RunShopFlow.Instance.Leave());
            yield return Until(() => d.State == RunState.Combat, 6, "combat 1");
            Check("boss 1 starts after the shop", d.State == RunState.Combat && d.Encounter == shops[0] && !Run.Open);
            Check("the build carries into the fight", d.Player.Build != null && d.Player.Build.Has(bought.Id));
            yield return TollbreakerHooks();
            var keepItems = Me.Items.ToList();
            yield return TollbreakerPool();
            Me.Items.Clear(); Me.Items.AddRange(keepItems); Me.ResetEncounter();

            // drink a potion in fight 1, so shop 2 has something to refill
            d.Player.DebugSetHp(40); bot.potion = true;
            { float tp = 0; while (d.Player.Potions == PlayerController.MaxPotions && tp < 1) { tp += Time.deltaTime; yield return null; } }
            int potsSpent = PlayerController.MaxPotions - d.Player.Potions;
            // clear boss 1 -> Chimes -> shop 2
            int before = Me.Chimes, earned1 = Me.Earned;
            d.Boss.DebugDefeat();
            yield return Until(() => d.State == RunState.Cleared, 4, "clear 1");
            yield return new WaitForSeconds(0.6f); yield return Shot("clear_popup");
            Check("+Chimes popup shows the area boss clear's pay", RunShopFlow.Instance.LastPopup == 190, $"+{RunShopFlow.Instance.LastPopup}");
            yield return Until(() => d.Encounter != shops[0], 8, "past boss 1");
            yield return ToShop("shop 2");
            int got = Me.Chimes - before;
            Check("boss clear 150 + 20 + 20, then area 2's 3 minibosses 140 each", got == 190 + 420, $"+{got}: {string.Join(" | ", Run.Ledger)}");
            Check("shop 2 opens before boss 2", RunShopFlow.Showing && Run.Visit == 2 && d.Encounter == shops[1]);
            Check("health refilled at the shop", d.Player.Hp == PlayerController.MaxHp);
            Check("potions refill before every area boss (gpt/08 §3), not only the last", potsSpent > 0 && d.Player.Potions == PlayerController.MaxPotions, $"spent {potsSpent}, now {d.Player.Potions}");
            Check("no Chimes for a fight twice: none paid while the shop is open", Me.Earned == earned1 + got);
            yield return Shot("visit2");
            // Keystone cap (test setup: two Keystones already counted)
            Me.Keystones = 2; Me.Chimes += 1000;
            Run.TryReroll(0, out _);
            Check("Keystone cap: none offered once 2 are owned", Me.Stock.All(s => s == null || s.Tier != ItemTier.Keystone));
            Me.Keystones = 0;
            RunShopFlow.Instance.Leave();
            yield return Until(() => d.State == RunState.Combat, 6, "combat 2");
            d.Boss.DebugDefeat();
            yield return Until(() => d.Encounter != shops[1], 8, "past boss 2");
            yield return ToShop("shop 3");
            Check("shop 3 opens before boss 3", Run.Visit == 3 && d.Encounter == shops[2]);
            RunShopFlow.Instance.Leave();
            yield return Until(() => d.State == RunState.Combat, 6, "combat 3");
            before = Me.Chimes;
            d.Boss.DebugDefeat();
            yield return Until(() => d.Encounter != shops[2], 8, "past boss 3");
            yield return ToShop("final shop");
            Check("final shop before the Bellkeeper", Run.FinalShop && Run.Visit == 4 && RunDirector.Bosses[d.Encounter].Final);
            Check("final shop pays no stand-in", Me.Chimes - before == 190, $"+{Me.Chimes - before}");
            Check("final shop's special card is Rare or Keystone", Me.Stock[ShopRun.SpecialSlot] == null || Me.Stock[ShopRun.SpecialSlot].Tier != ItemTier.Common);
            Check("potions refill before the final boss too", d.Player.Potions == PlayerController.MaxPotions);
            yield return Shot("final");
            // one screen (Chzy18, 29 Oct): worst case, a full party of 4 (3 allies) and a long build (test setup)
            var extra = new List<string>();
            foreach (var def in ItemCatalog.All.Where(i => (i.Class == "Tollbreaker" || i.General) && !Me.Has(i.Id))) { Grant(Me, def.Id); extra.Add(def.Id); }
            string[] classes = RunDirector.Classes;
            for (int j = 0; j < RunParty.MaxAllies; j++) { var s = Run.Add(classes[j % 3], testSlot: true); s.Ready = j % 2 == 0; s.Chimes = 100 + 40 * j; Grant(s, ItemCatalog.All.First(i => i.Class == classes[j % 3]).Id); Grant(s, ItemCatalog.StrongBrew); }
            yield return null; yield return Shot("coop_full");
            var panel = RunShopFlow.Instance.LastPanel;
            Note($"screen {Screen.width}x{Screen.height}, panel {panel}, build {Me.Items.Count} items");
            Check("one screen: 4 players and a long build fit 1080p with no scrolling", panel.yMin >= 0 && panel.yMax <= 1080 && panel.xMin >= 0 && panel.xMax <= UiTheme.VirtualWidth && Me.Items.Count >= 9, $"{panel}");
            Run.Slots.RemoveAll(s => s.TestSlot); Me.Items.RemoveAll(o => extra.Contains(o.Def.Id));
            yield return null; yield return Shot("final_solo");
            // Fourth Choice (test setup: owned): the next roll has 7 cards, the 7th a class card, still on one screen
            Grant(Me, ItemCatalog.FourthChoice); Run.TryReroll(0, out _);
            Check("Fourth Choice: 7 cards, the 7th a class card", Me.StockCount == 7 && Me.Stock[6] != null && Me.Stock[6].Class == "Tollbreaker", Me.Stock[6]?.Name ?? "none");
            yield return null; yield return Shot("seven_cards");
            Check("seven cards still fit one screen", RunShopFlow.Instance.LastPanel.yMax <= 1080 && RunShopFlow.Instance.LastPanel.xMax <= UiTheme.VirtualWidth);
            Me.Items.RemoveAll(o => o.Def.Id == ItemCatalog.FourthChoice);
            int items = Me.Items.Count;
            RunShopFlow.Instance.Leave();
            yield return Until(() => d.State == RunState.Combat, 6, "Bellkeeper");
            Check("build kept into the Bellkeeper", d.Player.Build != null && d.Player.Build.Items.Count == items);
            d.Boss.DebugDefeat();
            yield return Until(() => d.State == RunState.Results, 10, "results");
            Check("victory", d.Victory);
            Check("run end: every item and Chime is gone", Run == null && ItemHooks.Run == null && (d.Player == null || d.Player.Build == null));
            Check("retry accepted", d.RequestRetry());
            yield return ToShop("retry shop 1");
            Check("retry: fresh shop 1, 420 Chimes, no items", Run != null && Run.Visit == 1 && Me.Chimes == 420 && Me.Items.Count == 0, $"visit {Run?.Visit}, {Me?.Chimes}");
            d.SetPaused(true); d.ReturnToMenu(); yield return null;

            // ---------------- Chimesinger and Arcanist hooks ----------------
            yield return ClassRun("Chimesinger", Seq(ChimesingerHooks(), ChimesingerPool()));
            // a clean campaign win earns the bonus fight (no shop before it): clear it too (debug defeat, routing only)
            yield return Until(() => d.State == RunState.Results || d.State == RunState.Combat && d.Encounter == Campaign.BonusIndex, 10, "results or the bonus fight");
            if (d.State == RunState.Combat && d.Encounter == Campaign.BonusIndex)
            {
                Check("no shop before the bonus fight", !RunShopFlow.Showing && d.Player.Build != null && d.Player.Build.Items.Count == items);
                d.Boss.DebugDefeat();
            }
            yield return ClassRun("Arcanist", Seq(ArcanistHooks(), ArcanistPool(), Act(GeneralPool)));

            // ---------------- the shop bot ----------------
            RunShopFlow.BotBuys = true;
            Check("bot run starts", d.StartRun("Arcanist"));
            yield return Until(() => d.State == RunState.Combat, 8, "bot leaves shop 1");
            Check("bot bought and readied in shop 1", d.State == RunState.Combat && d.Player.Build != null && d.Player.Build.Items.Count >= 1 && d.Log.Any(l => l.StartsWith("shop bot: bought")),
                string.Join(" | ", d.Log.Where(l => l.StartsWith("shop bot"))));
            { int boss1 = d.Encounter; d.Boss.DebugDefeat(); yield return Until(() => d.Encounter != boss1, 8, "bot past boss 1"); }
            yield return ToShop("bot shop 2");
            yield return Until(() => d.State == RunState.Combat && Run.Visit == 2, 12, "bot passes shop 2");
            Check("bot passes shop 2 by itself", d.State == RunState.Combat && Run.Visit == 2);
            foreach (var l in d.Log.Where(l => l.StartsWith("shop bot"))) Note(l);
            RunShopFlow.BotBuys = false;
            yield return ToShop("bot shop 1");
            d.SetPaused(true); d.ReturnToMenu(); yield return null;

            yield return PracticeChecks();

            Check("no errors or exceptions logged", errors == 0, errors + " logged");
            Finish();
        }

        // One-area practice (spec §1): one shop before an area boss with the area's 300 stand-in Chimes; none for
        // minibosses or the Bellkeeper; practice never pays and a practice retry starts a fresh shop.
        IEnumerator PracticeChecks()
        {
            Check("practice shop only before area bosses", RunShopFlow.PracticeShopFor(RunDirector.AurelionPractice) && RunShopFlow.PracticeShopFor(RunDirector.CrucivarPractice) && RunShopFlow.PracticeShopFor(RunDirector.SylvaraPractice)
                && !RunShopFlow.PracticeShopFor(0) && !RunShopFlow.PracticeShopFor(RunDirector.TempoTyrantPractice) && !RunShopFlow.PracticeShopFor(RunDirector.DialWardenPractice));
            int spire = RunMaps.IndexOf(RunDirector.AurelionMap);
            d.StartPractice("Chimesinger", RunDirector.AurelionPractice);
            yield return Until(() => RunMaps.Current == spire && RunDirector.Instance && RunDirector.Instance != d && RunDirector.Instance.State == RunState.Ready, 40, "Aurelion practice");
            d = RunDirector.Instance;
            yield return new WaitForSeconds(1.5f);   // longer than the practice auto-start
            Check("Aurelion practice: the shop opens and holds the auto-start", d.State == RunState.Ready && d.Mode == RunMode.Practice && RunShopFlow.Showing, $"{d.State}");
            Check("practice shop: one visit, 300 stand-in Chimes", Run != null && Run.Visit == 1 && Run.TotalVisits == 1 && Me.Chimes == 300 && d.Player.Build == Me, $"{Me?.Chimes}");
            yield return Shot("practice");
            int c = Card(x => x.Tier == ItemTier.Common);
            Check("practice: buy works", c >= 0 && Run.TryBuy(0, c, out _));
            Check("practice: Ready starts the boss", RunShopFlow.Instance.Leave());
            yield return Until(() => d.State == RunState.Combat, 6, "practice combat");
            Check("practice: the build carries into the fight", d.State == RunState.Combat && d.Player.Build != null && d.Player.Build.Items.Count == 1);
            d.Boss.DebugDefeat();
            yield return Until(() => d.State == RunState.Results, 8, "practice results");
            Check("practice clear: no Chimes paid, items gone", d.Victory && Run == null && ItemHooks.Run == null);
            Check("practice retry: a fresh shop", d.RequestRetry() && Run != null && Me.Chimes == 300 && Me.Items.Count == 0);
            d.SetPaused(true); d.ReturnToMenu(); yield return null;
        }

        IEnumerator ClassRun(string cls, IEnumerator checks)
        {
            Check($"{cls} run starts", d.StartRun(cls));
            yield return null;
            RunShopFlow.Instance.Leave();
            yield return Until(() => d.State == RunState.Combat, 6, cls + " combat");
            yield return checks;
            d.SetPaused(true); d.ReturnToMenu(); yield return null;
        }

        // Pure model checks: seeds, co-op chip-in and its cap, party gating.
        void ModelChecks()
            yield return ToShop(cls + " shop 1");   // the hook checks run on the first area boss, not a miniboss
        {
            string Stock(ShopRun r) => string.Join(",", r.Slot(0).Stock.Select(s => s?.Id ?? "-"));
            var a = new ShopRun(42, 4); a.Add("Arcanist"); a.OpenVisit("x", false, true);
            var b = new ShopRun(42, 4); b.Add("Arcanist"); b.OpenVisit("x", false, true);
            Check("same seed, same stock", Stock(a) == Stock(b), Stock(a));
            var coop = new ShopRun(5, 4); coop.Add("Tollbreaker"); coop.Add("Chimesinger", testSlot: true);
            coop.OpenVisit("x", false, true);
            Check("co-op: everyone is paid the same", coop.Slots.All(s => s.Chimes == 300));
            Check("chip in 100", coop.TryGift(0, 1, 100, out _) && coop.Slot(0).Chimes == 200 && coop.Slot(1).Chimes == 400);
            Check("chip-in cap: 150 received per visit", !coop.TryGift(0, 1, 60, out var w1) && coop.TryGift(0, 1, 50, out _), w1);
            Check("can't give more than you have", !coop.TryGift(1, 0, 1000, out var w2), w2);
            coop.Close();
            Check("closed shop refuses requests", !coop.TryBuy(0, 0, out var w3) && w3 == "the shop is closed", w3);
            coop.OpenVisit("y", false, true);
            Check("the receive cap resets each visit", coop.TryGift(0, 1, 150, out _));
            Check("co-op ready check: the test slot is ready, the player isn't", !coop.AllReady && coop.SetReady(0, true) && coop.AllReady);
            Check("prices: 90 / 150 / 240, temper 110", ItemCatalog.Price(ItemTier.Common) == 90 && ItemCatalog.Price(ItemTier.Rare) == 150 && ItemCatalog.Price(ItemTier.Keystone) == 240 && ItemCatalog.TemperPrice == 110);
            Check("Spare Flask is untemperable (and Temper is never offered for no-Temper items)", !ItemCatalog.Get(ItemCatalog.SpareFlask).Tempered.HasValue, $"{ItemCatalog.All.Count(i => !i.Tempered.HasValue)}");
        }

        PlayerController P => d.Player;

        // the hit the Tollbreaker's damage hook reads (test setup: what his kit sets just before a real hit)
        TollbreakerKit.HitContext Hit(float releaseDistance = 9, bool ret = false, bool floor = false)
        {
            var h = ((TollbreakerKit)P.Kit).Hitting; h.ReleaseDistance = releaseDistance; h.Distance = releaseDistance; h.Return = ret; h.FromFloor = floor; h.Recall = floor;
            return h;
        }

        // the four slice items (they took the Vanguard's slice slots) and the slice's general items
        IEnumerator TollbreakerHooks()
        {
            var tk = (TollbreakerKit)P.Kit; var me = Me;
            Grant(me, ItemCatalog.KeenEdge, tempered: true); Grant(me, ItemCatalog.ShortFuse); Grant(me, ItemCatalog.RootedAxe);
            Grant(me, ItemCatalog.StrongBrew); Grant(me, ItemCatalog.SpareFlask); Grant(me, ItemCatalog.Sprinter); Grant(me, ItemCatalog.DeepBreath);
            Note("test setup: granted Keen Edge+, Short Fuse, Rooted Axe and four general items");
            int keen = Mathf.RoundToInt(14 * (1 + me.V(ItemCatalog.KeenEdge) / 100f));   // 18, or 17 if the run bought it plain
            Hit(); bool outward = ItemHooks.Outgoing(d.World, 14, "Twin Hurl") == keen;
            Hit(ret: true); bool back = ItemHooks.Outgoing(d.World, 3, "Twin Hurl") == 3;
            Check($"Keen Edge: Twin Hurl's outward hit 14 -> {keen}; the return hit stays 3", outward && back);
            float fuse = 600 * (1 - me.V(ItemCatalog.ShortFuse) / 100f);   // 480 ticks (8 s), or 420 if bought and tempered
            Check($"Short Fuse: Splitting Throw cooldown 10 s -> {fuse / 60:0.#} s", Close(ItemHooks.F(P, "t.split.cd", 600), fuse));
            Check("Rooted Axe: no boss timeout while split, bonus +25%, Call of Iron 9 s",
                ItemHooks.F(P, "t.rooted", 0) == 1 && Close(ItemHooks.F(P, "t.split.bonus", 1.4f), 1.25f) && ItemHooks.I(P, "t.call.cd", 360) == 540);
            // Rooted Axe through the kit: a Splitting Throw into the boss stays lodged past the 6 s boss timeout
            yield return Until(() => P.Stunned == 0 && tk.Cooldown01(2) <= 0 && tk.AxesInHand == 2, 6, "Splitting Throw ready");
            d.Boss.Halt(); d.World.ClearTransient(); NearBoss(3f);
            bot.Aim = d.Boss.transform.position; bot.press[2] = true;
            yield return Until(() => tk.SplitActive, 2, "the split");
            Check("Short Fuse: the real Splitting Throw starts its cooldown", tk.Cooldown01(2) > 0.9f, $"{tk.Cooldown01(2):0.00}");
            yield return Wait(6.5f);
            Check("Rooted Axe: the split and its axe are still on the boss after 6.5 s", tk.SplitActive && tk.AxesStuck >= 1, $"split {tk.SplitActive}, stuck {tk.AxesStuck}");
            tk.ResetAll();
            Check("Strong Brew / Sprinter / Deep Breath read through the controller hooks",
                ItemHooks.PotionHeal(P, 40) == 48 && Mathf.Approximately(ItemHooks.SprintCost(P, 25), 21) && Mathf.Approximately(ItemHooks.RegenDelay(P, 0.75f), 0.6f));
            // Strong Brew through a real drink
            d.World.ClearTransient();
            yield return Until(() => P.Stunned == 0, 2, "not stunned");
            int pots = P.Potions; P.DebugSetHp(40); bot.potion = true;
            float t = 0; while (P.Potions == pots && t < 1) { t += Time.deltaTime; yield return null; }
            Check("Strong Brew: a drink heals 48", P.Hp == 88 && P.Potions == pots - 1, $"hp {P.Hp}");
            P.RefreshHealth(refillPotions: true);
            Check("Spare Flask: 6 potions at a refill", P.Potions == PlayerController.MaxPotions + 1, $"{P.Potions}");
            // Riven Iron: an axe Call of Iron sends through the boss cracks it for him alone
            Grant(me, ItemCatalog.RivenIron);
            ItemHooks.Cast(P, "t.recall.hit", d.Boss.transform.position, Vector3.forward, 60, 0);
            Hit(); bool riven = ItemHooks.Outgoing(d.World, 100, "Call of Iron") == 115 && ItemHooks.Outgoing(d.World, 100, "Splitting Throw") == 115 && ItemHooks.Outgoing(d.World, 10, "Grinding Toll") == 10;
            Check("Riven Iron: his Call of Iron and Splitting Throw +15% for 4 s; Grinding Toll unchanged", riven && me.L.RivenUntil == d.World.Tick + 240);
            Check("Riven Iron is personal: another class's heavy hit gets nothing", ItemHooks.Outgoing(d.World, 40, "Bellstrike") == 40);
            // take the granted items back so the run's own shop checks stay clean
            string keep = me.Items[0].Def.Id; me.Items.RemoveAll(o => o.Def.Id != keep); me.ResetEncounter();
            P.RefreshHealth(refillPotions: true);
            P.DebugSetHp(PlayerController.MaxHp);
            d.Boss.BeginCombat();
        }

        IEnumerator ChimesingerHooks()
        {
            var ck = (ChimesingerKit)P.Kit;
            Grant(Me, ItemCatalog.HeavyBell); Grant(Me, ItemCatalog.SwiftChorus); Grant(Me, ItemCatalog.ChorusOfValour);
            Note("test setup: granted Heavy Bell, Swift Chorus, Chorus of Valour");
            Check("Heavy Bell: Bellstrike +20%", ItemHooks.Outgoing(d.World, 40, "Bellstrike") == 48);
            yield return Until(() => P.Stunned == 0, 2, "not stunned");
            bot.press[4] = true;
            yield return Until(() => ck.Cooldown01(4) > 0, 2, "Mending Chorus");
            Check("Swift Chorus: Mending Chorus cooldown 15 s -> 12.75 s through the kit", ck.Cooldown01(4) > 0.83f && ck.Cooldown01(4) <= 0.851f, $"{ck.Cooldown01(4):0.000} of 15 s");
            Check("Chorus of Valour solo: the caster gets +12% for 4 s", Me.L.ValourUntil > d.World.Tick && ItemHooks.Outgoing(d.World, 50, "ResonantNote") == 56, $"until {Me.L.ValourUntil}, now {d.World.Tick}");
            Grant(Me, ItemCatalog.LoneBell);
            Check("Lone Bell solo: +12% stacks additively with Valour", ItemHooks.Outgoing(d.World, 100, "ResonantNote") == 124);
        }

        IEnumerator ArcanistHooks()
        {
            var ak = (ArcanistKit)P.Kit;
            Grant(Me, ItemCatalog.LongSurge); Grant(Me, ItemCatalog.PerpetualBeam);
            Note("test setup: granted Long Surge, Perpetual Beam");
            Check("Perpetual Beam: Sigil without a glyph 35% -> 70%", ItemHooks.Outgoing(d.World, 19, "Rune Sigil (no glyph)") == 38 && ItemHooks.Outgoing(d.World, 55, "Rune Sigil") == 55);
            yield return Until(() => P.Stunned == 0, 2, "not stunned");
            ak.DebugSetGlyphs(2); bot.Aim = d.Boss.transform.position; bot.press[4] = true;
            yield return Until(() => ak.Surging, 2, "Surge");
            Check("Long Surge: 2 glyphs give 5 s of Surge through the kit", ak.Surging && ak.SurgeSeconds > 4.7f && ak.SurgeSeconds <= 5.01f, $"{ak.SurgeSeconds:0.00} s");
            yield return Until(() => !ak.Surging, 7, "Surge over");
            // Perpetual Beam's glyph cap through a real Disintegrate channel on the boss
            yield return Until(() => P.Stunned == 0 && ak.Cooldown01(1) <= 0, 9, "Disintegrate ready");
            float t = 0; bot.press[1] = true; bot.hold[1] = true;
            while (t < 2.6f) { bot.Aim = d.Boss.transform.position; KeepAlive(); t += Time.deltaTime; yield return null; }
            bot.Clear();
            Check("Perpetual Beam: Disintegrate builds at most 2 glyphs", ak.Glyphs <= 2 && ak.BeamDamage > 0, $"glyphs {ak.Glyphs}, beam {ak.BeamDamage}");
            Check("Unbound Cast and Perpetual Beam: the larger share wins", (Grant2(Me, ItemCatalog.UnboundCast) && ItemHooks.Outgoing(d.World, 35, "Arcane Comet (no glyph)") == 70));
        }
        static bool Grant2(PlayerBuild b, string id) { Grant(b, id); return true; }
        static IEnumerator Seq(params IEnumerator[] parts) { foreach (var p in parts) yield return p; }
        static IEnumerator Act(Action a) { a(); yield break; }

        // ================= full pool (55 items accepted 2026-10-28) =================
        // Every item's hook is checked with the item granted (test setup) on the real player; effects a key press can
        // show are also checked through the kit. Each class then fights 6 s holding its whole pool at once (smoke).
        bool Close(float a, float b, float eps = 0.02f) => Mathf.Abs(a - b) <= eps;
        void Only(params string[] ids) { Me.Items.Clear(); Me.ResetEncounter(); foreach (var id in ids) Grant(Me, id); }
        IEnumerator Ready() { bot.Clear(); P.Kit.ResetAll(); yield return Until(() => P.Stunned == 0 && !P.Dashing, 3, "player free"); }
        void NearBoss(float dist = 2.6f)   // test setup: stand beside the boss, facing it
        {
            var b = d.Boss.transform.position; var at = b + new Vector3(0, 0, d.Boss.BodyRadius + dist);
            P.transform.position = at; P.FaceNow(b); bot.Aim = b;
        }
        bool MeterHas(string ability) => d.World.RunMeter.ContainsKey(ability);
        IEnumerator Hold(int slot, float seconds) { float t = 0; bot.hold[slot] = true; while (t < seconds) { bot.Aim = d.Boss.transform.position; KeepAlive(); t += Time.deltaTime; yield return null; } bot.hold[slot] = false; }
        IEnumerator Press(int slot, float after) { bot.Aim = d.Boss.transform.position; bot.press[slot] = true; yield return Wait(after); }
        IEnumerator Smoke(string cls, Func<int, int> rotation)
        {
            Me.Items.Clear(); Me.ResetEncounter();
            foreach (var it in ItemCatalog.All.Where(x => (x.Class == cls || x.General) && !x.Held && x.OfferedSolo)) Grant(Me, it.Id, tempered: it.Tempered.HasValue);
            Note($"test setup: {cls} holds {Me.Items.Count} items, all tempered where they can be");
            yield return Ready(); NearBoss(cls == "Tollbreaker" ? 3f : 7f);
            d.Boss.Halt(); d.World.ClearTransient();   // a smoke test of items: no boss attacks (Aurelion v4 wipes force a down)
            int hp0 = d.Boss.Hp, err0 = errors; float t = 0; int frame = 0;
            while (t < 6f && d.State == RunState.Combat)
            {
                KeepAlive(); bot.Aim = d.Boss.transform.position;
                int s = rotation(frame++); if (s >= 0 && s < 5) { bot.press[s] = true; bot.hold[s] = true; } else if (s == 9) bot.dash = true;
                if (frame % 4 == 0) for (int i = 1; i < 5; i++) bot.hold[i] = false;
                t += Time.deltaTime; yield return null;
            }
            bot.Clear(); d.Boss.BeginCombat();
            Check($"{cls} with the whole pool: fights 6 s with no errors and damages the boss", errors == err0 && d.Boss.Hp < hp0, $"{hp0} -> {d.Boss.Hp}, errors +{errors - err0}");
            Me.Items.Clear(); Me.ResetEncounter();
        }

        IEnumerator TollbreakerPool()
        {
            var tk = (TollbreakerKit)P.Kit; d.Boss.Halt(); d.World.ClearTransient();
            Only(ItemCatalog.WideGrind); Check("Wide Grind: Grinding Toll's ring +30%", Close(ItemHooks.F(P, "t.toll.radius", 0.75f), 0.975f));
            Only(ItemCatalog.LongGrind); Check("Long Grind: Grinding Toll spins 2.5 s (2.8 s tempered)", ItemHooks.I(P, "t.toll.spin", 120) == 150 && Tempered(ItemCatalog.LongGrind, () => ItemHooks.I(P, "t.toll.spin", 120) == 168));
            Only(ItemCatalog.QuickCatch); Check("Quick Catch: returning axes 18 -> 21.6 m/s", Close(ItemHooks.F(P, "t.return.speed", 18), 21.6f));
            Only(ItemCatalog.HeavyHaft); Check("Heavy Haft: the held throw 30 -> 36", ItemHooks.Outgoing(d.World, 30, "Twin Hurl (held)") == 36 && ItemHooks.Outgoing(d.World, 14, "Twin Hurl") == 14);
            Only(ItemCatalog.IronPull); Check("Iron Pull: +1.6 per metre, cap 125 (+2.4 and 132 tempered)",
                Close(ItemHooks.F(P, "t.call.per_m", 8), 9.6f) && ItemHooks.I(P, "t.call.max", 110) == 125
                && Tempered(ItemCatalog.IronPull, () => Close(ItemHooks.F(P, "t.call.per_m", 8), 10.4f) && ItemHooks.I(P, "t.call.max", 110) == 132));
            Only(ItemCatalog.CloseQuarters);
            Hit(3); bool near = ItemHooks.Outgoing(d.World, 100, "Twin Hurl") == 112; Hit(6); bool far = ItemHooks.Outgoing(d.World, 100, "Twin Hurl") == 100;
            Check("Close Quarters: Twin Hurl +12% thrown within 4 m, nothing from 6 m", near && far);
            Only(ItemCatalog.DeepSplit); Check("Deep Split: the split and its boss lodge last 8 s", ItemHooks.I(P, "t.split.ticks", 360) == 480 && ItemHooks.I(P, "t.lodge.boss", 360) == 480);
            Only(ItemCatalog.QuickGrind); Check("Quick Grind: Grinding Toll hits every 0.3 s", ItemHooks.I(P, "t.toll.every", 24) == 18);
            Only(ItemCatalog.Backswing); Check("Backswing: Twin Hurl's return hit 3 -> 6; Grinding Toll's return stays 3", ItemHooks.I(P, "t.tap.back", 3) == 6 && ItemHooks.I(P, "t.toll.back", 3) == 3);
            Only(ItemCatalog.AnchorLine);
            Hit(floor: true); bool fromFloor = ItemHooks.Outgoing(d.World, 100, "Call of Iron") == 120; Hit(floor: false); bool fromBoss = ItemHooks.Outgoing(d.World, 100, "Call of Iron") == 100;
            Check("Anchor Line: floor axes last 12 s; Call of Iron from the floor +20%", ItemHooks.I(P, "t.lodge.floor", 480) == 720 && fromFloor && fromBoss);
            Only(ItemCatalog.BerserkersPace); Hit(2);
            var pace = new List<int>();
            for (int i = 0; i < 7; i++) { pace.Add(ItemHooks.Outgoing(d.World, 100, "Twin Hurl")); ItemHooks.Dealt(d.World, "Twin Hurl", 10); }
            ItemHooks.OnPlayerHit(P); int afterHit = ItemHooks.Outgoing(d.World, 100, "Twin Hurl");
            Check("Berserker's Pace: +2% per close hit up to +10%, lost when hit", string.Join(",", pace) == "100,102,104,106,108,110,110" && afterHit == 100, string.Join(",", pace) + " / " + afterHit);
            Only(ItemCatalog.WarCry); ItemHooks.Cast(P, "t.frenzy", P.transform.position, P.transform.forward, 240, 0);
            Check("War Cry solo: Bell Frenzy gives you +10% for its 4 s", Me.L.CryUntil == d.World.Tick + 240 && ItemHooks.Outgoing(d.World, 100, "Grinding Toll") == 110);
            Only(ItemCatalog.FrenziedHour); Check("Frenzied Hour: cooldown 18 s, lasts 3 s, side axes 35% (15 s tempered)",
                ItemHooks.I(P, "t.frenzy.cd", 1800) == 1080 && ItemHooks.I(P, "t.frenzy.ticks", 240) == 180 && Close(ItemHooks.F(P, "t.fan.side", 0.5f), 0.35f)
                && Tempered(ItemCatalog.FrenziedHour, () => ItemHooks.I(P, "t.frenzy.cd", 1800) == 900));
            // Battle Spin through the kit: a dash, then a tap that hits like the held throw
            Only(ItemCatalog.BattleSpin); yield return Ready(); NearBoss(3f);
            bot.dash = true; yield return Wait(0.5f); bot.Clear();
            yield return Press(0, 0.8f);
            Check("Battle Spin: after a dash the next tap is a battle-spin hit", MeterHas("Twin Hurl (battle spin)"), string.Join(",", d.World.RunMeter.Keys));
            // Twin Split and Ringing Return through the kit: both axes go, and a recall hit takes 2 s off the split's cooldown
            Only(ItemCatalog.TwinSplit, ItemCatalog.RingingReturn); yield return Ready(); NearBoss(3f);
            Check("Twin Split: the second axe deals 50%", ItemHooks.F(P, "t.twin_split", 0) == 50);
            yield return Press(2, 1.0f);
            Check("Twin Split: Splitting Throw throws both axes", tk.AxesInHand == 0 && tk.AxesStuck == 2 && tk.SplitActive, $"in hand {tk.AxesInHand}, stuck {tk.AxesStuck}");
            float c0 = tk.Cooldown01(2);
            ItemHooks.Cast(P, "t.recall.hit", d.Boss.transform.position, Vector3.forward, 60, 0);
            Check("Ringing Return: a recall hit takes 2 s off Splitting Throw", Close(c0 - tk.Cooldown01(2), 120 / 600f, 0.01f), $"{c0:0.000} -> {tk.Cooldown01(2):0.000}");
            yield return Press(3, 1.2f);
            Check("Twin Split: Call of Iron brings both axes back through the boss", tk.AxesInHand == 2 && MeterHas("Call of Iron"), $"in hand {tk.AxesInHand}");
            d.Boss.BeginCombat();
            yield return Smoke("Tollbreaker", f => f % 50 == 0 ? 2 : f % 90 == 45 ? 3 : f % 70 == 35 ? 1 : f % 61 == 0 ? 9 : f % 300 == 150 ? 4 : f % 6 == 0 ? 0 : -1);
        }

        IEnumerator ChimesingerPool()
        {
            var ck = (ChimesingerKit)P.Kit; d.Boss.Halt(); d.World.ClearTransient();
            Only(ItemCatalog.BrightNote); Check("Bright Note: +10% damage, +15% speed", ItemHooks.Outgoing(d.World, 100, "ResonantNote") == 110 && Close(ItemHooks.F(P, "c.note.speed", 22), 25.3f));
            Only(ItemCatalog.WideRefrain, ItemCatalog.LongRefrain); Check("Wide / Long Refrain: angle +25%, range +2 m", Close(ItemHooks.F(P, "c.refrain.half", 32), 40) && Close(ItemHooks.F(P, "c.refrain.range", 7.5f), 9.5f));
            Only(ItemCatalog.WideChorus); Check("Wide Chorus: radius +30%", Close(ItemHooks.F(P, "c.chorus.radius", 6), 7.8f));
            Only(ItemCatalog.LightStep); Check("Light Step: dash costs 24", Close(ItemHooks.F(P, "dash.cost", 30), 24));
            Only(ItemCatalog.GraceNote); ItemHooks.OnDash(P);
            Check("Grace Note: the next note after a dash deals double, once", ItemHooks.Note(P, Vector3.forward, 16, 22) == 32 && ItemHooks.Note(P, Vector3.forward, 16, 22) == 16);
            Only(ItemCatalog.Chord); int n0 = d.World.Projectiles.Count; for (int i = 0; i < 4; i++) ItemHooks.Note(P, Vector3.forward, 16, 22);
            Check("Chord: the 4th note adds 2 side notes", d.World.Projectiles.Count == n0 + 2, $"+{d.World.Projectiles.Count - n0}");
            Only(ItemCatalog.ThirdEcho); Check("Third Echo: echo cap 3", ItemHooks.I(P, "c.echo.cap", 2) == 3);
            Only(ItemCatalog.GreatBell); Check("Great Bell: echo cap 4, Crescendo cooldown +30%", ItemHooks.I(P, "c.echo.cap", 2) == 4 && ItemHooks.I(P, "c.cresc.cd", 300) == 390);
            Only(ItemCatalog.Fortissimo); Check("Fortissimo: +12% per echo", ItemHooks.Crescendo(P, 100, 2) == 124);
            Only(ItemCatalog.Overflow); P.DebugSetHp(P.MaxHealth); ItemHooks.ChorusTouched(P, P, 30);
            Check("Overflow: 30 overheal at full HP stores +15% for the next Crescendo, once", ItemHooks.Crescendo(P, 100, 0) == 115 && ItemHooks.Crescendo(P, 100, 0) == 100);
            Only(ItemCatalog.ChoirLeader); Check("Choir Leader: heal -25%, detonation at 50%", ItemHooks.I(P, "c.chorus.heal", 30) == 22 && Close(ItemHooks.F(P, "c.choir", 0), 0.5f));
            Only(ItemCatalog.NoteTrail); n0 = d.World.Projectiles.Count; ItemHooks.OnDashEnd(P);
            Check("Note Trail: a dash ends with 3 notes", d.World.Projectiles.Count == n0 + 3);
            // ring the bell for real, then the ringing items
            Only(ItemCatalog.SharedRinging, ItemCatalog.StruckTogether, ItemCatalog.Encore); yield return Ready();
            P.transform.position = new Vector3(0, 0, 6.5f); yield return Press(1, 0.6f);
            Check("Bellstrike rings the bell", ck.Bell);
            int e0 = ck.Echoes; for (int i = 0; i < 5; i++) ItemHooks.Dealt(d.World, "ResonantNote", 5);
            Check("Shared Ringing solo: every 5th own note adds an echo", ck.Echoes == e0 + 1, $"{e0} -> {ck.Echoes}");
            ItemHooks.Dealt(d.World, "ResonantNote", 5);
            Check("Struck Together solo: every 6th note rings the bell for 30% of Bellstrike", MeterHas("Bellstrike (struck together)"));
            yield return Press(3, 0.8f);
            Check("Encore: Crescendo leaves the bell ringing with 1 echo", ck.Bell && ck.Echoes == 1, $"bell {ck.Bell}, echoes {ck.Echoes}");
            d.Boss.BeginCombat();
            yield return Smoke("Chimesinger", f => f % 50 == 0 ? 1 : f % 70 == 0 ? 2 : f % 113 == 0 ? 3 : f % 200 == 0 ? 4 : f % 83 == 0 ? 9 : 0);
        }

        IEnumerator ArcanistPool()
        {
            var ak = (ArcanistKit)P.Kit; d.Boss.Halt(); d.World.ClearTransient();
            Only(ItemCatalog.QuickCarving, ItemCatalog.WideBeam, ItemCatalog.SteadyHand);
            Check("Quick Carving / Wide Beam / Steady Hand: steps 40 -> 33 ticks, width +30%, turn +25%", ItemHooks.I(P, "a.dis.step", 40) == 33 && Close(ItemHooks.F(P, "a.dis.width", 0.7f), 0.91f) && Close(ItemHooks.F(P, "a.dis.turn", 1.5f), 1.875f));
            Only(ItemCatalog.WideSigil, ItemCatalog.FarBlink); Check("Wide Sigil / Far Blink: radius +25%, Blink +0.6 m", Close(ItemHooks.F(P, "a.sigil.radius", 2.5f), 3.125f) && Close(ItemHooks.F(P, "dash.distance", 3), 3.6f));
            Only(ItemCatalog.GlassEdge); Check("Glass Edge: +8% damage, max HP 92", ItemHooks.Outgoing(d.World, 100, "Magic Missile") == 108 && P.MaxHealth == 92);
            Only(ItemCatalog.FourthGlyph); Check("Fourth Glyph: glyph cap 4", ItemHooks.GlyphCap(P, 3) == 4);
            Only(ItemCatalog.FourthGlyph, ItemCatalog.PerpetualBeam); Check("Perpetual Beam still caps at 2 with Fourth Glyph", ItemHooks.GlyphCap(P, 3) == 2);
            Only(ItemCatalog.HastyComet); Check("Hasty Comet: 3 -> 2.2 s, -15%", ItemHooks.I(P, "a.comet.fall", 180) == 132 && ItemHooks.Outgoing(d.World, 100, "Arcane Comet") == 85);
            Only(ItemCatalog.GreatComet); Check("Great Comet: 3 -> 3.8 s, +40%, larger", ItemHooks.I(P, "a.comet.fall", 180) == 228 && ItemHooks.Outgoing(d.World, 100, "Arcane Comet") == 140 && Close(ItemHooks.F(P, "a.comet.radius", 4), 5));
            Only(ItemCatalog.SurgeEcho); Check("Surge Echo: 1 glyph back", ItemHooks.I(P, "a.surge_end_glyphs", 0) == 1);
            Only(ItemCatalog.MissileVolley); bool t1 = ItemHooks.Tap(P), t2 = ItemHooks.Tap(P), t3 = ItemHooks.Tap(P);
            Check("Missile Volley: the 3rd quick tap earns a free missile", !t1 && !t2 && t3);
            Only(ItemCatalog.GlyphBrand); ak.DebugSetGlyphs(0); ItemHooks.Dealt(d.World, "Arcane Comet", 10);
            for (int i = 0; i < 8; i++) ItemHooks.Dealt(d.World, "Magic Missile", 1);
            Check("Glyph Brand solo: a branded boss gives a glyph per 8 missiles", ak.Glyphs == 1, $"{ak.Glyphs}");
            var rune = ItemCatalog.Get(ItemCatalog.ResonantRune); var duo = new ShopRun(9, 4); duo.Add("Arcanist"); duo.Add("Chimesinger", testSlot: true); duo.OpenVisit("x", false, true);
            Check("Resonant Rune: offered only with a Chimesinger in the party, never solo", !Run.CanOffer(Me, rune) && duo.CanOffer(duo.Slot(0), rune));
            // through the kit
            Only(ItemCatalog.Starfall); yield return Ready(); P.transform.position = new Vector3(0, 0, 6.5f);
            yield return Press(3, 0.6f);
            Check("Starfall: a Comet lands as 3 marks", ak.Comets.Count == 3, $"{ak.Comets.Count}");
            Only(ItemCatalog.TwinSigil); yield return Ready(); ak.DebugSetGlyphs(1);
            yield return Press(2, 0.5f);
            Check("Twin Sigil: a powered Sigil places 2", ak.Sigils.Count == 2, $"{ak.Sigils.Count}");
            Only(ItemCatalog.SplitMissile); yield return Ready();
            bool four = false; float t = 0; bot.hold[0] = true; bot.press[0] = true;
            while (t < 1.4f) { four |= ak.Charging && ak.Missiles == 4; t += Time.deltaTime; yield return null; }
            bot.Clear(); yield return Wait(0.5f);
            Check("Split Missile: a full charge summons and fires 4", four && ak.LastVolley == 4, $"last volley {ak.LastVolley}");
            d.Boss.BeginCombat();
            yield return Smoke("Arcanist", f => f % 140 == 0 ? 1 : f % 90 == 0 ? 2 : f % 170 == 0 ? 3 : f % 230 == 0 ? 4 : f % 77 == 0 ? 9 : 0);
        }

        void GeneralPool()
        {
            Only(ItemCatalog.LongLungs); Check("Long Lungs: +15 max stamina", Close(P.MaxStaminaNow, 115));
            Only(ItemCatalog.SureFooting); Check("Sure Footing: knockback -30%, no Temper", Close(ItemHooks.F(P, "knockback", 1), 0.7f) && !ItemCatalog.Get(ItemCatalog.SureFooting).Tempered.HasValue);
            Only(ItemCatalog.Formation); Check("Formation solo: a flat +6%", ItemHooks.Outgoing(d.World, 100, "Magic Missile") == 106);
            Only(ItemCatalog.LightFeet); ItemHooks.OnDashEnd(P); Check("Light Feet: +10% move speed after a dash", Close(ItemHooks.F(P, "move.speed", 1), 1.1f));
            Me.Items.Clear(); Me.ResetEncounter();
        }
        bool Tempered(string id, Func<bool> check) { var o = Me.Get(id); o.Tempered = true; bool ok = check(); o.Tempered = false; return ok; }



        void Finish()
        {
            lines.Insert(0, $"RESULT: {(fail == 0 ? "ALL PASSED" : "FAILURES")} · {pass} passed, {fail} failed · errors logged: {errors}");
            if (errorLines.Count > 0) { lines.Add(""); lines.Add("Errors:"); lines.AddRange(errorLines); }
            if (d) { lines.Add(""); lines.Add("Director log:"); lines.AddRange(d.Log.Select(l => "  " + l)); }
            File.WriteAllLines(Path.Combine(dir, "shop-autotest-report.txt"), lines);
            Debug.Log($"[shoptest] done: {pass} passed, {fail} failed");
            StartCoroutine(Quit());
        }
        IEnumerator Quit()
        {
            yield return new WaitForSeconds(0.5f);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            UnityEditor.EditorApplication.Exit(fail == 0 ? 0 : 1);
#else
            Application.Quit(fail == 0 ? 0 : 1);
#endif
        }
    }
}
