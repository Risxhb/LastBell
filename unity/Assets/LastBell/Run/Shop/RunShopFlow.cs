using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastBell.Run.Shop
{
    // The shop at each intermission (shop spec §1, §5; run-shop-plan §2). Sits next to the RunDirector and follows its
    // states, so the director only needed one change (a clear in a run goes to Ready for the next boss, not straight to
    // its Intro):
    //   Ready in a run   -> a fresh run gets a new ShopRun; the shop for the boss about to start opens (health refilled)
    //   Intro            -> the shop closes; per-encounter item state resets
    //   Cleared (run)    -> Chimes for the clear (+ party bonuses)
    //   Results / Menu   -> every item and Chime is gone (spec §7)
    // Also draws the stand-in shop screen (IMGUI in the HUD's UiTheme; a shop keeper or hub look comes later, §12.8).
    public sealed class RunShopFlow : MonoBehaviour
    {
        static bool Arg(string a) => Environment.GetCommandLineArgs().Any(x => x.Equals(a, StringComparison.OrdinalIgnoreCase));
        static int ArgInt(string a, int fallback)
        {
            var args = Environment.GetCommandLineArgs(); int i = Array.FindIndex(args, x => x.Equals(a, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int v) ? v : fallback;
        }
        // On in play. Automated suites (-lastbellOut) keep the old flow unless they ask for the shop, so every existing
        // suite runs exactly as before.
        public static bool Enabled = !Arg("-lastbellOut") || Arg("-lastbellShop") || Arg("-lastbellShopBot") || Arg("-lastbellShopTest");
        // -lastbellShopBot: a greedy buyer spends for slot 0 and readies up (playtest agent runs with items)
        public static bool BotBuys = Arg("-lastbellShopBot");
        public static int? SeedOverride = Arg("-lastbellSeed") ? ArgInt("-lastbellSeed", 0) : (int?)null;
        // -lastbellToll N: the run's Toll until the Toll ladder exists (4 stocks the Toll IV items, 7 the Toll VII ones too)
        public static int TollLevel = ArgInt("-lastbellToll", 0);

        public static RunShopFlow Instance { get; private set; }
        public static bool Showing => Instance && Instance.Run != null && Instance.Run.Open && Instance.d && Instance.d.State == RunState.Ready;
        public ShopRun Run { get; private set; }
        public string LastRefusal { get; private set; }
        public int LastPopup { get; private set; }   // the +Chimes popup shown on the last clear
        float popupAt = -99;
        // Chimes art (Codex, accepted by Chzy18 in the 29 Oct batch): bell-coin for the wallet and prices, coin burst for +Chimes
        public static Texture2D Coin => RunHud.Icon("chimes-64");
        public static Texture2D Burst => RunHud.Icon("chimes-burst-64");
        int runId = -1, openedFor = -1, openedAt; bool botPending;
        RunDirector d;

        void Awake()
        {
            Instance = this; d = GetComponent<RunDirector>(); d.StateChanged += OnState;
            if (RunShopAutoTest.Requested) RunShopAutoTest.Launch();
        }
        void OnDestroy() { if (d) d.StateChanged -= OnState; if (Instance == this) { Instance = null; ItemHooks.Run = null; } }

        void OnState(RunState s)
        {
            switch (s)
            {
                case RunState.Ready:
                    if (!Enabled || (d.Mode == RunMode.Practice && !PracticeShopFor(d.Encounter))) { EndRun(); break; }
                    if (Run == null || runId != d.RunsStarted) { if (!AdoptCarried()) BeginRun(); }
                    // campaign: minibosses have no shop (and a run arriving in a new arena waits at Ready); the wallet carries on
                    if (d.Mode == RunMode.Run && !Campaign.ShopBefore(d.Encounter)) break;
                    OpenFor(d.Encounter);
                    break;
                case RunState.Intro:
                    if (Run != null)
                    {
                        Run.Close();
                        foreach (var b in Run.Slots) b.ResetEncounter();
                        Run.BrandUntil = -1; Run.BrandOwner = null;
                        d.Player.RefreshHealth(refillPotions: false);   // the shop's refill, now at the bought max HP (Glass Edge)
                    }
                    break;
                case RunState.Cleared:
                    // practice and the tutorial never pay; a campaign miniboss pays 100 (spec §2), every other fight 150
                    if (Run != null && d.Mode == RunMode.Run && d.Boss != null && d.Encounter != RunDirector.TutorialPractice)
                    {
                        bool final = RunDirector.Bosses[d.Encounter].Final;
                        int seconds = (d.World.Tick - d.World.SegmentStartTick) / Sim.Hz;
                        int before = Run.Slot(0).Earned;
                        Run.PayClear(d.Boss.DisplayName.Split(',')[0].Trim(), final, d.World.Players.All(p => !p.Down), seconds, IsMiniboss(d.Encounter));
                        LastPopup = Run.Slot(0).Earned - before; popupAt = Time.unscaledTime;
                    }
                    break;
                case RunState.Results: case RunState.Menu:
                    EndRun();
                    break;
            }
        }

        // One-area practice gets one shop before that area's boss (spec §1): area bosses only. Minibosses have no shop, and
        // Ultimate practice will use the Standard Kit instead (spec §9).
        public static bool PracticeShopFor(int encounter) =>
            encounter >= 0 && encounter < RunDirector.Bosses.Length && RunDirector.Bosses[encounter].InRun && !RunDirector.Bosses[encounter].Final;

        void BeginRun()
        {
            runId = d.RunsStarted; openedFor = -1;
            Run = new ShopRun(SeedOverride ?? Guid.NewGuid().GetHashCode(), d.Mode == RunMode.Practice ? 1 : Math.Max(1, d.EncounterOrder.Count(Campaign.ShopBefore)));
            Run.Toll = TollLevel;
            d.Player.Build = Run.Add(d.ClassName);   // party bots / netcode add their slots here later
            ItemHooks.Run = Run;
            d.Log.Add($"shop: new run, seed {Run.Seed}");
        }
        // Campaign travel (RunDirector.TravelTo): the shop run (wallet, items) survives the scene load into the next arena
        // and the new arena's shop adopts it for the same run.
        static ShopRun carried; static int carriedRunId = -1;
        public void CarryForTravel() { carried = Run; carriedRunId = runId; if (d.Player) d.Player.Build = null; }
        public static void DropCarried() { carried = null; carriedRunId = -1; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearCarried() => DropCarried();
        bool AdoptCarried()
        {
            if (carried == null || carriedRunId != d.RunsStarted || d.Mode != RunMode.Run) { DropCarried(); return false; }
            Run = carried; runId = carriedRunId; openedFor = -1; DropCarried();
            Run.BrandUntil = -1; Run.BrandOwner = null;
            d.Player.Build = Run.Slot(0);
            ItemHooks.Run = Run;
            d.Log.Add($"shop: carried run, {Run.Slot(0).Chimes} Chimes, {Run.Slot(0).Items.Count} items");
            return true;
        }
        void EndRun()
        {
            if (Run == null) return;
            if (d.Player) d.Player.Build = null;
            Run = null; ItemHooks.Run = null; runId = -1; openedFor = -1;
        }

        static bool IsMiniboss(int encounter) => Campaign.Areas.Any(a => Array.IndexOf(a.MinibossIndices, encounter) >= 0);

        void OpenFor(int encounter)
        {
            if (openedFor == encounter + 1000 * d.RunsStarted) return;   // one visit per boss (Ready can't re-enter mid-visit)
            openedFor = encounter + 1000 * d.RunsStarted; openedAt = Time.frameCount;
            int pos = d.Mode == RunMode.Run ? Array.IndexOf(d.EncounterOrder, encounter) : 0;
            bool final = RunDirector.Bosses[encounter].Final;
            // health and potions refill at the shop before every boss after the first (gpt/08 §3: every run boss is an area boss)
            if (pos > 0) d.Player.RefreshHealth(refillPotions: true);
            // the 300 stand-in miniboss Chimes only where no minibosses were fought: practice, or a run order without them
            bool standIn = d.Mode == RunMode.Practice || !d.EncounterOrder.Any(IsMiniboss);
            Run.OpenVisit(RunDirector.Bosses[encounter].Name, final, areaShop: !final && standIn);
            LastRefusal = null;
            d.Log.Add($"shop: visit {Run.Visit} before {RunDirector.Bosses[encounter].Name}, {Run.Slot(0).Chimes} Chimes");
            botPending = BotBuys;   // the bot shops on the next frame, outside the director's state change
        }

        // Ready (button / Enter / bot): marks the slot ready; the shop closes and the boss starts once every slot is ready
        public bool Leave(int slot = 0)
        {
            if (!Showing) return false;
            Run.SetReady(slot, true);
            return Run.AllReady && d.RequestReady();
        }

        void Update()
        {
            if (!Showing) return;
            if (botPending && Time.frameCount > openedAt)
            {
                botPending = false;
                foreach (var line in RunShopBot.Shop(Run, 0)) d.Log.Add("shop bot: " + line);
                Leave(); return;
            }
            // co-op: the shop closes after 90 s whatever the ready state (spec §5.4); solo waits for you (§6)
            if (!Run.Solo && Time.frameCount > openedAt && d.StateTicks >= ShopRun.CoopTimerSeconds * Sim.Hz) d.RequestReady();
            var kb = Keyboard.current;
            var pad = Gamepad.current;   // R, or Y on a gamepad (InputHints)
            if (!d.Paused && (kb != null && kb.rKey.wasPressedThisFrame || pad != null && pad.buttonNorth.wasPressedThisFrame)) Do(Run.TryReroll(0, out var why), why);
        }

        void Do(bool ok, string why) { LastRefusal = ok ? null : why; }

        // ---------------------------------------------------------------- screen
        // The intermission in the main menu's language (UI sub-thread B): no panel or boxes. A light dim and the main menu's
        // side gradient (mirrored, dark on the left) behind two columns: the stock as a list (icon, tier, name, effect,
        // price, Buy / Lock text rows) under the header, and on the right the wallet, your build (Temper rows), allies
        // (Chip in rows, co-op) and the footer (Reroll, Ready as the larger main action). Keyboard / controller focus is an
        // ImguiFocus grid of two columns; focus opens on Ready and B returns to it. R rerolls; Enter / A confirms the
        // focused row (RunDirector no longer readies on Enter while the shop is open).
        GUIStyle ledgerSt, tierSt, nameSt, nextSt, effectSt, priceSt, walletSt, walletUnitSt, smallSt, smallRight, buildSt, allySt, buildRow, popupSt;
        int styleH = -1;
        static Color TierCol(ItemTier t) => t == ItemTier.Common ? UiTheme.IvoryMuted : t == ItemTier.Rare ? UiTheme.Gold : UiTheme.GoldBright;
        static string TierName(ItemTier t) => t == ItemTier.Common ? "COMMON" : t == ItemTier.Rare ? "RARE" : "KEYSTONE";
        readonly ImguiFocus focus = new ImguiFocus { Columns = 2 };
        int seen = -9, rowCount, readyRow = -1;
        static readonly LastBell.Run.HintLine KeyHints = new LastBell.Run.HintLine((HintAction.Confirm, "Select"), (HintAction.Reroll, "Reroll"), (HintAction.Pause, "Pause"));
        static readonly LastBell.Run.HintLine PadHints = new LastBell.Run.HintLine((HintAction.Confirm, "Select"), (HintAction.Reroll, "Reroll"), (HintAction.Back, "Back to Ready"));

        void Styles()
        {
            if (styleH == Screen.height && tierSt != null) return;
            styleH = Screen.height;
            tierSt = UiTheme.Style(UiTheme.SizeMicro, true, TextAnchor.MiddleLeft, UiTheme.IvoryMuted);
            nameSt = UiTheme.Style(UiTheme.SizeHeading, true, TextAnchor.MiddleLeft, UiTheme.Ivory);
            effectSt = UiTheme.Style(UiTheme.SizeNumeric, false, TextAnchor.UpperLeft, UiTheme.IvoryBody, true);
            nextSt = UiTheme.Style(UiTheme.SizeHeading, false, TextAnchor.MiddleLeft, UiTheme.IvoryBody);
            smallRight = UiTheme.Style(UiTheme.SizeLabel, false, TextAnchor.MiddleRight, UiTheme.IvoryMuted);
            buildRow = UiTheme.Style(18, true, TextAnchor.MiddleCenter, UiTheme.Ivory);
            priceSt = UiTheme.Style(UiTheme.SizeHeading, true, TextAnchor.MiddleRight, UiTheme.Gold);
            walletSt = UiTheme.Style(40, true, TextAnchor.MiddleLeft, UiTheme.GoldBright);
            walletUnitSt = UiTheme.Style(UiTheme.SizeLabel, true, TextAnchor.MiddleLeft, UiTheme.GoldLabel);
            smallSt = UiTheme.Style(UiTheme.SizeLabel, false, TextAnchor.UpperLeft, UiTheme.IvoryMuted, true);
            ledgerSt = UiTheme.Style(UiTheme.SizeLabel, false, TextAnchor.MiddleLeft, UiTheme.IvoryMuted); ledgerSt.clipping = TextClipping.Clip;
            buildSt = UiTheme.Style(UiTheme.SizeNumeric, true, TextAnchor.MiddleLeft, UiTheme.Ivory);
            allySt = UiTheme.Style(UiTheme.SizeNumeric, true, TextAnchor.MiddleLeft, UiTheme.Ivory);
            popupSt = UiTheme.Style(30, true, TextAnchor.MiddleLeft, UiTheme.GoldBright);
        }
        static Rect R(float x, float y, float w, float h) => UiTheme.R(x, y, w, h);
        // a focus row (registered in draw order); the label rests in RestLabel and turns GoldBright with the strip on focus
        bool Row(float x, float y, float w, float h, string label, bool enabled, GUIStyle st = null)
        {
            rowCount++;
            return UiImgui.Row(focus, new Rect(x, y, w, h), label, st ?? UiImgui.Styles.Row, enabled);
        }
        // an empty grid cell: keeps the two-column grid aligned (skipped by navigation, like a disabled row)
        void Gap() { rowCount++; focus.Register(false); }

        // Layout, virtual px. One screen, no scrolling or paging (Chzy18, 29 Oct): worst case a full 4-player party (3 allies)
        // and a long build still fit 1080.
        const float Top = 64, StockTop = 248, ColGap = 56, BuildW = 420, RowH = 38;
        static int Rows(int n, int perRow) => n <= 0 ? 0 : (n + perRow - 1) / perRow;

        void OnGUI()
        {
            ClearPopup();
            if (!Showing || d.Paused) return;   // Pause draws over the arena, not over the shop
            GUI.depth = -1;   // above the run HUD
            Styles();
            var s = UiImgui.Styles;
            bool fresh = RunHud.FreshFrame(ref seen);
            focus.Begin(); rowCount = 0;
            var me = Run.Slot(0);
            bool coop = !Run.Solo;
            var allies = Run.Slots.Where(o => o != me).ToList();

            // columns: content clamped to the centre 1920 on ultrawide; the stock takes what the build column leaves
            float vw = UiTheme.VirtualWidth, left = UiTheme.G6 + Mathf.Max(0, (vw - 1920) / 4);
            float total = Mathf.Min(1260, vw - left - UiTheme.G6), stockW = total - BuildW - ColGap, bx = left + stockW + ColGap;
            // dim + the main menu's side gradient, mirrored: solid ink under both columns, fading out over ~340 px past them
            // (the texture is dark over its first 57 % once mirrored), so the arena shows on the right
            UiTheme.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(UiTheme.ModalDim.r, UiTheme.ModalDim.g, UiTheme.ModalDim.b, UiTheme.ModalDim.a * 0.5f));
            const float fadeW = 800; float gx = left + total + UiTheme.G3 - 0.57f * fadeW;
            UiTheme.Fill(R(0, 0, gx + 1, UiTheme.VirtualHeight), new Color(UiTheme.GradientInk.r, UiTheme.GradientInk.g, UiTheme.GradientInk.b, 0.985f));
            UiImgui.SideGradient(new Rect(gx, 0, fadeW, UiTheme.VirtualHeight), false);

            // header: visit, Intermission, next boss; the run's earnings since the last shop
            string eb = d.Mode == RunMode.Practice ? "Practice shop" : Run.FinalShop ? "Final shop" : $"Shop {Run.Visit} of {Run.TotalVisits}";
            float y = UiImgui.Header(new Rect(left, Top, stockW, 0), eb, "Intermission") + 14;
            UiTheme.Label(R(left, y, stockW, 26), "Next:  " + Run.NextBoss, nextSt);
            y += 30;
            // one line, clipped: a long run of payouts never runs into the first card
            if (Run.Ledger.Count > 0) UiTheme.Label(R(left, y, stockW, 20), LedgerLine(stockW), ledgerSt);

            // wallet, top of the build column
            // a small header of its own, level with the screen's: eyebrow, then coin, amount and unit in one line
            UiTheme.Label(R(bx, Top, BuildW, 20), UiTheme.Spaced("Wallet"), s.Eyebrow);
            string wallet = me.Chimes.ToString(); float ww = walletSt.CalcSize(new GUIContent(wallet)).x / UiTheme.Scale, wx = bx + (Coin ? 50 : 0);
            if (Coin) GUI.DrawTexture(R(bx, Top + 32, 40, 40), Coin, ScaleMode.ScaleToFit, true);
            UiTheme.Label(R(wx, Top + 26, ww + 8, 52), wallet, walletSt);
            UiTheme.Label(R(wx + ww + 12, Top + 44, BuildW, 24), "CHIMES", walletUnitSt);
            UiTheme.Label(R(bx, Top + 84, BuildW, 20), $"Keystones {me.Keystones}/{ItemCatalog.KeystonesPerRun}  ·  earned this run {me.Earned}", smallSt, UiTheme.IvoryMicro);
            // stock: one entry per card, Buy / Lock in the grid's two columns
            int cards = me.StockCount;
            float avail = UiTheme.VirtualHeight - UiTheme.G6 - StockTop, ih = Mathf.Min(116, avail / cards);
            for (int i = 0; i < cards; i++) Entry(me, i, left, StockTop + i * ih, stockW, ih);
            float stockBottom = StockTop + cards * ih;

            // build column: your build, allies, footer
            float by = StockTop;
            UiTheme.Label(R(bx, by, BuildW, 20), UiTheme.Spaced("Your build"), s.Eyebrow);
            UiTheme.Label(R(bx, by, BuildW, 20), me.Items.Count == 0 ? "items you buy appear here" : (me.Items.Count == 1 ? "1 item" : $"{me.Items.Count} items") + (me.Items.Any(o => o.Def.Tempered.HasValue && !o.Tempered) ? "  ·  Temper strengthens one" : ""), smallRight);
            by += 30;
            // the list takes what the allies and the footer leave: rows shrink (34 -> 20 px) and the list goes to 2 columns past
            // 8 items, 3 if it still doesn't fit, so a full party and a long build stay on one screen (shop autotest)
            float listBottom = UiTheme.VirtualHeight - UiTheme.G6 - 56 - 70 - 10 - (coop ? 28 + allies.Count * 62 : 0) - 18, buildAvail = listBottom - by;
            int n = me.Items.Count, perCol = n > 8 ? 2 : 1;
            float chipH = n == 0 ? 34 : Mathf.Min(34, buildAvail / Rows(n, perCol));
            if (chipH < 20) { perCol = 3; chipH = Mathf.Min(34, buildAvail / Rows(n, perCol)); }
            chipH = Mathf.Max(chipH, 16);
            float chipW = (BuildW - (perCol - 1) * 16) / perCol, icon = Mathf.Min(26, chipH - 6);
            int k = 0;
            foreach (var o in me.Items.ToArray())
            {
                float ox = bx + (k % perCol) * (chipW + 16), oy = by + (k / perCol) * chipH; k++;
                bool temperable = o.Def.Tempered.HasValue && !o.Tempered;
                var ic = ItemIcon(o.Def, false); if (ic) GUI.DrawTexture(R(ox, oy + (chipH - icon) / 2, icon, icon), ic, ScaleMode.ScaleToFit, true);
                float tw = temperable ? (perCol == 1 ? 150 : perCol == 2 ? 72 : 52) : 0, nw = chipW - icon - 8 - tw - 4;
                UiTheme.Label(R(ox + icon + 8, oy, nw, chipH), Fit(o.Def.Name + (o.Tempered ? " +" : ""), nw), buildSt, o.Def.Tier == ItemTier.Common ? UiTheme.Ivory : TierCol(o.Def.Tier));
                if (temperable)
                {
                    if (Row(ox + chipW - tw, oy + 1, tw, chipH - 2, perCol == 1 ? $"Temper  {ItemCatalog.TemperPrice}" : $"+{ItemCatalog.TemperPrice}", me.Chimes >= ItemCatalog.TemperPrice, buildRow)) Do(Run.TryTemper(0, o.Def.Id, out var why), why);
                }
                else Gap();
                Gap();   // each build line is one grid row: down steps to the next item
            }
            by += Rows(me.Items.Count, perCol) * chipH + 18;

            if (coop)
            {
                UiTheme.Label(R(bx, by, BuildW, 20), UiTheme.Spaced("Allies"), s.Eyebrow);
                UiTheme.Label(R(bx, by, BuildW, 20), $"chip in: {ShopRun.GiftCapPerVisit} each per visit", smallRight);
                by += 28;
                foreach (var a in allies)
                {
                    UiTheme.Label(R(bx, by, BuildW - 130, 24), $"{a.ClassName}  ·  {a.Chimes} Chimes{(a.Ready ? "  ·  Ready" : "")}", allySt, a.Ready ? UiTheme.Gold : UiTheme.Ivory);
                    UiTheme.Label(R(bx, by + 24, BuildW - 130, 34), a.Items.Count == 0 ? "no items" : string.Join(", ", a.Items.Select(o => o.Def.Name + (o.Tempered ? "+" : ""))), smallSt);
                    if (Row(bx + BuildW - 126, by + 6, 126, 40, "Chip in 50", me.Chimes >= 50 && a.ReceivedThisVisit + 50 <= ShopRun.GiftCapPerVisit, buildRow)) Do(Run.TryGift(0, a.Slot, 50, out var why), why);
                    Gap();
                    F(bx, by + 58, BuildW, 1, UiTheme.RuleFaint);
                    by += 62;
                }
            }

            // footer, bottom of the build column: what happened / the rule, the co-op timer, Reroll and Ready
            float fy = UiTheme.VirtualHeight - UiTheme.G6 - 56, my = fy - 70;
            fy = Mathf.Max(fy, by + 70); my = fy - 70;
            string msg = LastRefusal ?? (me.LockUsed ? "Lock used this visit." : "Lock keeps one card through rerolls (one lock per visit, free).");
            UiTheme.Label(R(bx, my, BuildW, 22), msg, smallSt, LastRefusal != null ? UiTheme.Danger : UiTheme.IvoryMuted);
            UiTheme.Label(R(bx, my + 22, BuildW, 40), coop ? $"The shop closes when everyone is ready or after {ShopRun.CoopTimerSeconds} s: {Mathf.Max(0, ShopRun.CoopTimerSeconds - d.StateTicks / Sim.Hz)} s left." : "Unspent Chimes stay in your wallet. The shop waits for you.", smallSt, coop ? UiTheme.Gold : UiTheme.IvoryMicro);
            F(bx, fy - 6, BuildW, 1, UiTheme.RuleFaint);
            int rp = Run.RerollPrice(me);
            if (Row(bx, fy, BuildW * 0.44f, 56, $"Reroll  {rp}", me.Chimes >= rp)) Do(Run.TryReroll(0, out var why), why);
            readyRow = rowCount;
            if (Row(bx + BuildW * 0.44f, fy, BuildW * 0.56f, 56, me.Ready ? "Waiting for allies" : "Ready", !me.Ready, s.Primary)) Leave();
            UiTheme.Label(R(bx, fy + 60, BuildW, 22), InputHints.Last == InputHints.Device.Gamepad ? PadHints.Text : KeyHints.Text, RunHud.HintStyle);

            if (fresh) focus.Reset(readyRow);
            if (focus.Cancelled && !fresh) focus.Index = readyRow;   // B: back to Ready
            LastPanel = new Rect(left, Top, total, Mathf.Max(stockBottom, fy + 78) - Top);
        }

        // One stock entry: the item's 64 px icon, tier and tags, name and effect on the left; price and the Buy / Lock rows
        // on the right; a faint rule under it. A locked card carries a gold left bar; a sold one keeps its place.
        void Entry(PlayerBuild me, int i, float x, float y, float w, float h)
        {
            var def = me.Stock[i];
            bool locked = me.Locked == i;
            const float actW = 236;
            string slotName = i < ShopRun.ClassSlots || i == ShopRun.ExtraClassSlot ? me.ClassName : i < ShopRun.ClassSlots + ShopRun.GeneralSlots ? "General" : "Rare or Keystone";
            F(x, y + h - 1, w, 1, UiTheme.RuleFaint);
            if (def == null)
            {
                UiTheme.Label(R(x + 80, y + 10, w - 80, 18), (slotName + " card").ToUpperInvariant(), tierSt, UiTheme.IvoryMicro);
                UiTheme.Label(R(x + 80, y + 26, w - 80, 30), "Sold", nameSt, UiTheme.IvoryMuted);
                UiTheme.Label(R(x + 80, y + 58, w - 80, 20), "Bought this visit.", smallSt, UiTheme.IvoryMicro);
                Gap(); Gap(); return;
            }
            if (locked) F(x - UiTheme.G2, y + 8, 3, h - 16, UiTheme.Gold);
            var ic = ItemIcon(def, true);
            if (ic) GUI.DrawTexture(R(x, y + 10, 64, 64), ic, ScaleMode.ScaleToFit, true);
            float tx = x + 80, tw = w - 80 - actW - 16;
            string tags = TierName(def.Tier) + (def.Tags != ItemTag.None ? "  ·  " + def.Tags.ToString().ToUpperInvariant() : "") + (locked ? "  ·  LOCKED" : "");
            UiTheme.Label(R(tx, y + 8, tw, 16), tags, tierSt, TierCol(def.Tier));
            UiTheme.Label(R(tx, y + 24, tw, 30), def.Name, nameSt);
            string effect = def.Card(false) + (Run.Solo && def.SoloNote != null ? "  " + def.SoloNote : "");
            UiTheme.Label(R(tx, y + 56, tw, h - 60), effect, effectSt);
            // price and actions
            float ax = x + w - actW;
            UiTheme.Label(R(ax, y + 10, actW - 34, 30), def.Price.ToString(), priceSt);
            if (Coin) GUI.DrawTexture(R(ax + actW - 28, y + 13, 24, 24), Coin, ScaleMode.ScaleToFit, true);
            bool keyCap = def.Tier == ItemTier.Keystone && me.Keystones >= ItemCatalog.KeystonesPerRun;
            if (Row(ax, y + 46, actW * 0.5f, RowH, keyCap ? "Keystones full" : "Buy", me.Chimes >= def.Price && !keyCap, buildRow)) Do(Run.TryBuy(0, i, out var why), why);
            if (Row(ax + actW * 0.5f, y + 46, actW * 0.5f, RowH, locked ? "Locked" : "Lock", !me.LockUsed, buildRow)) Do(Run.TryLock(0, i, out var why), why);
        }
        // the payouts since the last shop on one line: as many as fit, then "+N more" (never cut mid-word)
        string ledgerText; int ledgerCount = -1; float ledgerWidth;
        string LedgerLine(float width)
        {
            if (ledgerCount == Run.Ledger.Count && ledgerWidth == width && ledgerText != null) return ledgerText;
            ledgerCount = Run.Ledger.Count; ledgerWidth = width;
            const string sep = "      ·      "; string line = "";
            for (int i = 0; i < Run.Ledger.Count; i++)
            {
                string next = line.Length == 0 ? Run.Ledger[i] : line + sep + Run.Ledger[i];
                string more = i + 1 < Run.Ledger.Count ? sep + $"+{Run.Ledger.Count - i - 1} more" : "";
                if (ledgerSt.CalcSize(new GUIContent(next + more)).x / UiTheme.Scale > width && line.Length > 0) { line += sep + $"+{Run.Ledger.Count - i} more"; break; }
                line = next;
            }
            return ledgerText = line;
        }
        // a build item's name cut to fit its column ("Berserker's Pa…"), cached per name and width
        readonly System.Collections.Generic.Dictionary<(string, int, int), string> fitCache = new System.Collections.Generic.Dictionary<(string, int, int), string>();
        string Fit(string name, float width)
        {
            var key = (name, Mathf.RoundToInt(width), Screen.height);
            if (fitCache.TryGetValue(key, out var hit)) return hit;
            if (fitCache.Count > 512) fitCache.Clear();
            float W(string t) => buildSt.CalcSize(new GUIContent(t)).x / UiTheme.Scale;
            string t2 = name;
            if (W(t2) > width) { while (t2.Length > 1 && W(t2 + "…") > width) t2 = t2.Substring(0, t2.Length - 1); t2 = t2.TrimEnd() + "…"; }
            return fitCache[key] = t2;
        }
        static void F(float x, float y, float w, float h, Color c) => UiTheme.Fill(UiTheme.R(x, y, w, h), c);
        public Rect LastPanel { get; private set; }   // virtual px; the autotest checks it fits one screen

        // accepted item icons (Codex, 29 Oct batch): Resources/Run/Icons/items/<id without class prefix>-64|128
        public static Texture2D ItemIcon(ItemDef d, bool large) => RunHud.Icon("items/" + d.Id.Substring(2).Replace('_', '-') + (large ? "-128" : "-64"));

        // +Chimes over the clear card: rises and fades over 2.6 s (HUD space only: no glow, no ground marks)
        void ClearPopup()
        {
            float t = Time.unscaledTime - popupAt;
            if (LastPopup <= 0 || t > 2.6f || !d || (d.State != RunState.Cleared && d.State != RunState.Ready)) return;
            if (Showing && t > 0.8f) return;   // the shop's own ledger takes over
            Styles();
            float a = Mathf.Clamp01(t / 0.25f) * (1 - Mathf.Clamp01((t - 2.0f) / 0.6f)), rise = 18 * Mathf.Clamp01(t / 2.6f);
            string text = $"+{LastPopup} CHIMES"; var st = popupSt;
            float w = st.CalcSize(new GUIContent(text)).x / UiTheme.Scale, cx = UiTheme.VirtualWidth / 2, y = 480 - rise;
            var keep = GUI.color; GUI.color = new Color(1, 1, 1, a);
            if (Burst) GUI.DrawTexture(R(cx - (w + 56) / 2, y - 4, 48, 48), Burst, ScaleMode.ScaleToFit, true);
            UiTheme.Label(R(cx - (w + 56) / 2 + 56, y, w + 8, 40), text, st, new Color(UiTheme.GoldBright.r, UiTheme.GoldBright.g, UiTheme.GoldBright.b, a));
            GUI.color = keep;
        }
    }
}
