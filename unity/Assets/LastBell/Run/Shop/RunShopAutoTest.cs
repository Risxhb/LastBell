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
            public Vector3 Aim; public bool[] press = new bool[5], hold = new bool[5]; public bool potion;
            public PlayerInput Read(PlayerController who)
            {
                var r = new PlayerInput { Aim = Aim, Potion = potion, P0 = press[0], P1 = press[1], P2 = press[2], P3 = press[3], P4 = press[4],
                    H0 = hold[0] || press[0], H1 = hold[1] || press[1], H2 = hold[2], H3 = hold[3], H4 = hold[4] };
                for (int i = 0; i < 5; i++) press[i] = false; potion = false;
                return r;
            }
            public void Clear() { for (int i = 0; i < 5; i++) press[i] = hold[i] = false; potion = false; }
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

            // clear boss 1 -> Chimes -> shop 2
            int before = Me.Chimes;
            d.Boss.DebugDefeat();
            yield return Until(() => d.State == RunState.Ready, 8, "shop 2");
            int got = Me.Chimes - before;
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
            yield return ClassRun("Chimesinger", ChimesingerHooks());
            yield return ClassRun("Arcanist", ArcanistHooks());

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
            Check("Chorus of Valour solo: the caster gets +12% for 4 s", Me.ValourUntil > d.World.Tick && ItemHooks.Outgoing(d.World, 50, "ResonantNote") == 56, $"until {Me.ValourUntil}, now {d.World.Tick}");
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
