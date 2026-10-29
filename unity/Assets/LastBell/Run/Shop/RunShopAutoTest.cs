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
        // these checks are about items, not survival: top the player up while a boss is live
        void KeepAlive() { if (d && d.Player && !d.Player.Down && d.Player.Hp < 60) d.Player.DebugSetHp(PlayerController.MaxHp); }
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
        int Card(Func<ItemDef, bool> want) { for (int i = 0; i < ShopRun.StockSize; i++) if (Me.Stock[i] != null && want(Me.Stock[i])) return i; return -1; }

        IEnumerator Main()
        {
            Note($"Last Bell shop autotest · {Application.version} · {DateTime.Now:yyyy-MM-dd HH:mm}");
            yield return Until(() => RunDirector.Instance && RunDirector.Instance.State == RunState.Menu, 30, "menu");
            d = RunDirector.Instance; d.InputOverride = bot;
            Check("shop is on for this suite", RunShopFlow.Enabled);
            var slice = new[] { ItemCatalog.TemperedEdge, ItemCatalog.Countdown, ItemCatalog.CrackedArmour, ItemCatalog.Oathbound, ItemCatalog.HeavyBell, ItemCatalog.SwiftChorus, ItemCatalog.ChorusOfValour,
                ItemCatalog.UnboundCast, ItemCatalog.LongSurge, ItemCatalog.PerpetualBeam, ItemCatalog.StrongBrew, ItemCatalog.Sprinter, ItemCatalog.DeepBreath, ItemCatalog.LoneBell, ItemCatalog.SpareFlask };
            Check("the 15 slice item icons load (64 and 128)", slice.All(id => RunShopFlow.ItemIcon(ItemCatalog.Get(id), true) != null && RunShopFlow.ItemIcon(ItemCatalog.Get(id), false) != null));
            Check("the pool has 69 items: 20 Vanguard, 19 Chimesinger, 20 Arcanist, 10 general; none held", ItemCatalog.All.Length == 69 && ItemCatalog.All.Count(i => i.General) == 10 && RunDirector.Classes.All(c => ItemCatalog.All.Count(i => i.Class == c) == (c == "Chimesinger" ? 19 : 20)) && !ItemCatalog.All.Any(i => i.Held) && !ItemCatalog.All.Any(i => i.Id == "c.twin_bells"));
            Check("Chimes coin and burst icons load", RunShopFlow.Coin != null && RunShopFlow.Burst != null && RunHud.Icon("chimes-128") != null && RunHud.Icon("chimes-burst-32") != null);
            RunShopFlow.SeedOverride = RunShopFlow.SeedOverride ?? 1234;
            RunShopFlow.BotBuys = false;
            yield return Wait(0.5f);

            ModelChecks();

            // ---------------- run 1: Vanguard, the whole run through every shop ----------------
            var order = RunDirector.CreateRunOrder(new System.Random(7));
            Check("StartRun accepted", d.StartRun("Vanguard", order));
            yield return null;
            Check("shop 1 opens at the first preparation", RunShopFlow.Showing && Run.Visit == 1 && d.Encounter == order[0], $"visit {Run?.Visit}");
            Check("shop 1: 300 stand-in miniboss Chimes, nothing else", Me.Chimes == 300 && Run.Ledger.Count == 1, $"{Me.Chimes}");
            Check("shop 1: no Keystones in stock", Me.Stock.All(s => s == null || s.Tier != ItemTier.Keystone));
            Check("shop 1: class cards are the player's class, general cards are general",
                Enumerable.Range(0, 3).All(i => Me.Stock[i] == null || Me.Stock[i].Class == "Vanguard") && Enumerable.Range(3, 2).All(i => Me.Stock[i] == null || Me.Stock[i].General));
            Check("shop 1: the special card is a Rare", Me.Stock[ShopRun.SpecialSlot] == null || Me.Stock[ShopRun.SpecialSlot].Tier == ItemTier.Rare);
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
            Check("boss 1 starts after the shop", d.State == RunState.Combat && d.Encounter == order[0] && !Run.Open);
            Check("the build carries into the fight", d.Player.Build != null && d.Player.Build.Has(bought.Id));
            yield return VanguardHooks();
            var keepItems = Me.Items.ToList(); Run.CrackUntil = -1;
            yield return VanguardPool();
            Me.Items.Clear(); Me.Items.AddRange(keepItems); Me.ResetEncounter();

            // clear boss 1 -> Chimes -> shop 2
            int before = Me.Chimes;
            d.Boss.DebugDefeat();
            yield return Until(() => d.State == RunState.Cleared, 4, "clear 1");
            yield return new WaitForSeconds(0.6f); yield return Shot("clear_popup");
            yield return Until(() => d.State == RunState.Ready, 8, "shop 2");
            int got = Me.Chimes - before;
            Check("+Chimes popup shows the clear's pay (without the stand-in)", RunShopFlow.Instance.LastPopup == 190, $"+{RunShopFlow.Instance.LastPopup}");
            Check("clear pays 150 + nobody down 20 + under par 20 + stand-in 300", got == 150 + 20 + 20 + 300, $"+{got}: {string.Join(" | ", Run.Ledger)}");
            Check("shop 2 opens before boss 2", RunShopFlow.Showing && Run.Visit == 2 && d.Encounter == order[1]);
            Check("health refilled at the shop", d.Player.Hp == PlayerController.MaxHp);
            Check("no Chimes for a fight twice: none paid while the shop is open", Me.Earned == 300 + got);
            yield return Shot("visit2");
            // Keystone cap (test setup: two Keystones already counted)
            Me.Keystones = 2; Me.Chimes += 1000;
            Run.TryReroll(0, out _);
            Check("Keystone cap: none offered once 2 are owned", Me.Stock.All(s => s == null || s.Tier != ItemTier.Keystone));
            Me.Keystones = 0;
            RunShopFlow.Instance.Leave();
            yield return Until(() => d.State == RunState.Combat, 6, "combat 2");
            d.Boss.DebugDefeat();
            yield return Until(() => d.State == RunState.Ready, 8, "shop 3");
            Check("shop 3 opens before boss 3", Run.Visit == 3 && d.Encounter == order[2]);
            RunShopFlow.Instance.Leave();
            yield return Until(() => d.State == RunState.Combat, 6, "combat 3");
            before = Me.Chimes;
            d.Boss.DebugDefeat();
            yield return Until(() => d.State == RunState.Ready, 8, "final shop");
            Check("final shop before the Bellkeeper", Run.FinalShop && Run.Visit == 4 && RunDirector.Bosses[d.Encounter].Final);
            Check("final shop pays no stand-in", Me.Chimes - before == 190, $"+{Me.Chimes - before}");
            Check("final shop's special card is Rare or Keystone", Me.Stock[ShopRun.SpecialSlot] == null || Me.Stock[ShopRun.SpecialSlot].Tier != ItemTier.Common);
            Check("potions refill before the final boss (rule unchanged)", d.Player.Potions == PlayerController.MaxPotions);
            yield return Shot("final");
            // one screen (Chzy18, 29 Oct): worst case, a full party of 8 (7 allies) and a long build (test setup)
            var extra = new List<string>();
            foreach (var def in ItemCatalog.All.Where(i => (i.Class == "Vanguard" || i.General) && !Me.Has(i.Id))) { Grant(Me, def.Id); extra.Add(def.Id); }
            string[] classes = RunDirector.Classes;
            for (int j = 0; j < 7; j++) { var s = Run.Add(classes[j % 3], testSlot: true); s.Ready = j % 2 == 0; s.Chimes = 100 + 40 * j; Grant(s, ItemCatalog.All.First(i => i.Class == classes[j % 3]).Id); Grant(s, ItemCatalog.StrongBrew); }
            yield return null; yield return Shot("coop_full");
            var panel = RunShopFlow.Instance.LastPanel;
            Note($"screen {Screen.width}x{Screen.height}, panel {panel}, build {Me.Items.Count} items");
            Check("one screen: 8 players and a long build fit 1080p with no scrolling", panel.yMin >= 0 && panel.yMax <= 1080 && panel.xMin >= 0 && panel.xMax <= UiTheme.VirtualWidth && Me.Items.Count >= 9, $"{panel}");
            Run.Slots.RemoveAll(s => s.TestSlot); Me.Items.RemoveAll(o => extra.Contains(o.Def.Id));
            yield return null; yield return Shot("final_solo");
            // Fourth Choice (test setup: owned): the next roll has 7 cards, the 7th a class card, still on one screen
            Grant(Me, ItemCatalog.FourthChoice); Run.TryReroll(0, out _);
            Check("Fourth Choice: 7 cards, the 7th a class card", Me.StockCount == 7 && Me.Stock[6] != null && Me.Stock[6].Class == "Vanguard", Me.Stock[6]?.Name ?? "none");
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
            Check("retry: fresh shop 1, 300 Chimes, no items", d.RequestRetry() && Run != null && Run.Visit == 1 && Me.Chimes == 300 && Me.Items.Count == 0);
            d.SetPaused(true); d.ReturnToMenu(); yield return null;

            // ---------------- Chimesinger and Arcanist hooks ----------------
            yield return ClassRun("Chimesinger", Seq(ChimesingerHooks(), ChimesingerPool()));
            yield return ClassRun("Arcanist", Seq(ArcanistHooks(), ArcanistPool(), Act(GeneralPool)));

            // ---------------- the shop bot ----------------
            RunShopFlow.BotBuys = true;
            Check("bot run starts", d.StartRun("Arcanist"));
            yield return Until(() => d.State == RunState.Combat, 8, "bot leaves shop 1");
            Check("bot bought and readied in shop 1", d.State == RunState.Combat && d.Player.Build != null && d.Player.Build.Items.Count >= 1 && d.Log.Any(l => l.StartsWith("shop bot: bought")),
                string.Join(" | ", d.Log.Where(l => l.StartsWith("shop bot"))));
            d.Boss.DebugDefeat();
            yield return Until(() => d.State == RunState.Combat && Run.Visit == 2, 12, "bot passes shop 2");
            Check("bot passes shop 2 by itself", d.State == RunState.Combat && Run.Visit == 2);
            foreach (var l in d.Log.Where(l => l.StartsWith("shop bot"))) Note(l);
            RunShopFlow.BotBuys = false;
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
        {
            string Stock(ShopRun r) => string.Join(",", r.Slot(0).Stock.Select(s => s?.Id ?? "-"));
            var a = new ShopRun(42, 4); a.Add("Arcanist"); a.OpenVisit("x", false, true);
            var b = new ShopRun(42, 4); b.Add("Arcanist"); b.OpenVisit("x", false, true);
            Check("same seed, same stock", Stock(a) == Stock(b), Stock(a));
            var coop = new ShopRun(5, 4); coop.Add("Vanguard"); coop.Add("Chimesinger", testSlot: true);
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
            Check("every item has a + value or is marked untemperable, and Spare Flask is untemperable", ItemCatalog.All.All(i => i.Tempered.HasValue || i.Id == ItemCatalog.SpareFlask));
        }

        PlayerController P => d.Player;

        IEnumerator VanguardHooks()
        {
            var vk = (VanguardKit2)P.Kit; var me = Me;
            Grant(me, ItemCatalog.TemperedEdge, tempered: true); Grant(me, ItemCatalog.Countdown); Grant(me, ItemCatalog.Oathbound);
            Grant(me, ItemCatalog.StrongBrew); Grant(me, ItemCatalog.SpareFlask); Grant(me, ItemCatalog.Sprinter); Grant(me, ItemCatalog.DeepBreath);
            Note("test setup: granted Tempered Edge+, Countdown, Oathbound and four general items");
            Check("Tempered Edge+: hit 3 deals +30%", ItemHooks.Outgoing(d.World, 42, "SteelRhythm3") == 55 && ItemHooks.Outgoing(d.World, 22, "SteelRhythm1") == 22);
            Check("Oathbound: Sundering Blow can't be empowered", !ItemHooks.SunderEmpowerable(P));
            bool r1 = ItemHooks.OnOpening(P), r2 = ItemHooks.OnOpening(P), r3 = ItemHooks.OnOpening(P);
            Check("Oathbound: every 3rd Opening resets Last Word", !r1 && !r2 && r3);
            me.ResetEncounter();
            // Countdown through the real kit: press 3 (Last Word) and read the cooldown it set
            yield return Until(() => P.Stunned == 0, 2, "not stunned");
            bot.Aim = d.Boss.transform.position; bot.press[2] = true;
            yield return Until(() => vk.UltRemaining > 0, 3, "Last Word cast");
            float want = 45f * (1 - Me.V(ItemCatalog.Countdown) / 100f);   // 36 s, or 31.5 s if the run bought and tempered it
            Check($"Countdown: Last Word cooldown 45 s -> {want:0.#} s through the kit", vk.UltRemaining > want - 1f && vk.UltRemaining <= want + 0.01f, $"{vk.UltRemaining:0.0} s");
            Check("Strong Brew / Sprinter / Deep Breath read through the controller hooks",
                ItemHooks.PotionHeal(P, 40) == 48 && Mathf.Approximately(ItemHooks.SprintCost(P, 25), 21) && Mathf.Approximately(ItemHooks.RegenDelay(P, 0.75f), 0.6f));
            // Strong Brew through a real drink
            d.Boss.Halt(); d.World.ClearTransient();
            yield return Until(() => P.Stunned == 0, 2, "not stunned");
            int pots = P.Potions; P.DebugSetHp(40); bot.potion = true;
            float t = 0; while (P.Potions == pots && t < 1) { t += Time.deltaTime; yield return null; }
            Check("Strong Brew: a drink heals 48", P.Hp == 88 && P.Potions == pots - 1, $"hp {P.Hp}");
            P.RefreshHealth(refillPotions: true);
            Check("Spare Flask: 6 potions at a refill", P.Potions == PlayerController.MaxPotions + 1, $"{P.Potions}");
            // Cracked Armour: the Vanguard's Sundering Blow cracks; solo, his Last Word gains +15%, others don't
            Grant(me, ItemCatalog.CrackedArmour);
            ItemHooks.Dealt(d.World, "SunderingBlow", 70);
            Check("Cracked Armour solo: Last Word +15%, the crack lasts 4 s", ItemHooks.Outgoing(d.World, 140, "LastWord") == 161 && Run.CrackUntil == d.World.Tick + 240);
            // take the granted items back so the run's own shop checks stay clean
            string keep = me.Items[0].Def.Id; me.Items.RemoveAll(o => o.Def.Id != keep);
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
            Check("Lone Bell solo: +15% stacks additively with Valour", ItemHooks.Outgoing(d.World, 100, "ResonantNote") == 127);
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
            yield return Ready(); NearBoss(cls == "Vanguard" ? 2.4f : 7f);
            int hp0 = d.Boss.Hp, err0 = errors; float t = 0; int frame = 0;
            while (t < 6f && d.State == RunState.Combat)
            {
                KeepAlive(); bot.Aim = d.Boss.transform.position;
                int s = rotation(frame++); if (s >= 0 && s < 5) { bot.press[s] = true; bot.hold[s] = true; } else if (s == 9) bot.dash = true;
                if (frame % 4 == 0) for (int i = 1; i < 5; i++) bot.hold[i] = false;
                t += Time.deltaTime; yield return null;
            }
            bot.Clear();
            Check($"{cls} with the whole pool: fights 6 s with no errors and damages the boss", errors == err0 && d.Boss.Hp < hp0, $"{hp0} -> {d.Boss.Hp}, errors +{errors - err0}");
            Me.Items.Clear(); Me.ResetEncounter();
        }

        IEnumerator VanguardPool()
        {
            var vk = (VanguardKit2)P.Kit; d.Boss.Halt(); d.World.ClearTransient();
            Only(ItemCatalog.QuickHands); Check("Quick Hands: combo window +0.3 s", ItemHooks.I(P, "v.combo.window", 36) == 54);
            Only(ItemCatalog.WideArc); Check("Wide Arc: hit 3 arc +30%", Close(ItemHooks.F(P, "v.hit3.arc", 170), 221));
            Only(ItemCatalog.LongReach); Check("Long Reach: Sundering Blow +1.5 m", Close(ItemHooks.F(P, "v.sunder.reach", 3.3f), 4.8f));
            Only(ItemCatalog.Grit); Check("Grit: max HP 108 (Grit+ 112, under the +20% cap)", P.MaxHealth == 108 && Tempered(ItemCatalog.Grit, () => P.MaxHealth == 112));
            Only(ItemCatalog.SteadyStance); Check("Steady Stance: Brace slow halved (0.35 -> 0.675 speed)", Close(ItemHooks.F(P, "v.brace.move", 0.35f), 0.675f));
            Only(ItemCatalog.DoubleOpening); Check("Double Opening: Opening stacks to 2", ItemHooks.I(P, "v.opening.stacks", 1) == 2);
            Only(ItemCatalog.HeavyHour); Check("Heavy Hour: windup 0.8 -> 1.1 s, +45%, wider fan", ItemHooks.I(P, "v.ult.windup", 48) == 66 && Close(ItemHooks.F(P, "v.ult.fan", 2.2f), 3.08f) && ItemHooks.Outgoing(d.World, 100, "LastWord") == 145);
            Only(ItemCatalog.QuickVerdict); Check("Quick Verdict: windup 0.5 s, -20%", ItemHooks.I(P, "v.ult.windup", 48) == 30 && ItemHooks.Outgoing(d.World, 100, "LastWord") == 80);
            Only(ItemCatalog.Momentum); ItemHooks.OnDash(P);
            Check("Momentum: a dash makes the next basic hit 3, once", ItemHooks.TakeMomentum(P) && !ItemHooks.TakeMomentum(P));
            Only(ItemCatalog.RhythmKeeper); for (int i = 0; i < 3; i++) ItemHooks.Dealt(d.World, "SteelRhythm3", 10);
            bool on = ItemHooks.Outgoing(d.World, 100, "SteelRhythm1") == 110; ItemHooks.OnPlayerHit(P);
            Check("Rhythm Keeper: 3 full combos give +10% until hit", on && ItemHooks.Outgoing(d.World, 100, "SteelRhythm1") == 100);
            Only(ItemCatalog.Rally); ItemHooks.Dealt(d.World, "LastWord", 10);
            Check("Rally solo: a Last Word hit gives you +10% for 4 s", ItemHooks.Outgoing(d.World, 100, "SteelRhythm1") == 110);
            Only(ItemCatalog.SecondStep); Me.ResetEncounter();
            bool first = ItemHooks.F(P, "dash.cost", 30) == 30; ItemHooks.OnDash(P);
            Check("Second Step: the first dash costs 30, a dash within 1.5 s costs 20", first && ItemHooks.F(P, "dash.cost", 30) == 20);
            Check("Second Step tempered: 15", Tempered(ItemCatalog.SecondStep, () => ItemHooks.F(P, "dash.cost", 30) == 15));
            Me.L.LastDashAt = d.World.Tick - Sim.Ticks(1.6f); Check("Second Step: after 1.5 s the dash costs 30 again", ItemHooks.F(P, "dash.cost", 30) == 30);
            Check("Second Step is no longer held", !ItemCatalog.Get(ItemCatalog.SecondStep).Held);
            // Clockwork Step through a real dash
            Only(ItemCatalog.ClockworkStep); yield return Ready();
            P.transform.position = new Vector3(-4, 0, 6.5f); P.transform.rotation = Quaternion.Euler(0, 90, 0);
            var p0 = P.transform.position; bot.dash = true; yield return Wait(0.5f);
            Check("Clockwork Step: a real dash travels 3.6 m", Close(Sim.Flat(P.transform.position - p0).magnitude, 3.6f, 0.15f), $"{Sim.Flat(P.transform.position - p0).magnitude:0.00} m");
            // Toll of Steel, Pendulum Cut and Iron Echo through the kit, next to the boss
            Only(ItemCatalog.TollOfSteel, ItemCatalog.PendulumCut, ItemCatalog.IronEcho); yield return Ready(); NearBoss();
            yield return Hold(0, 2.5f); bot.Clear(); yield return Wait(0.6f);
            Check("Toll of Steel: Steel Rhythm reaches hit 4 and sends its fan", MeterHas("SteelRhythm4") && MeterHas("SteelRhythm4 fan"), string.Join(",", d.World.RunMeter.Keys));
            NearBoss(); yield return Press(1, 1.8f);
            Check("Pendulum Cut: Sundering Blow keeps cutting", MeterHas("Pendulum Cut"));
            NearBoss(); yield return Press(2, 2.4f);
            Check("Iron Echo: Last Word's fan repeats", MeterHas("LastWordWave (echo)"), string.Join(",", d.World.RunMeter.Keys));
            d.Boss.BeginCombat();
            yield return Smoke("Vanguard", f => f % 40 == 0 ? 1 : f % 97 == 0 ? 2 : f % 61 == 0 ? 9 : f % 151 == 0 ? 3 : 0);
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
            Only(ItemCatalog.GlassEdge); Check("Glass Edge: +12% damage, max HP 92", ItemHooks.Outgoing(d.World, 100, "Magic Missile") == 112 && P.MaxHealth == 92);
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
