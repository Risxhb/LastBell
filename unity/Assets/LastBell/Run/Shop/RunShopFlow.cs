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
                    if (Run == null || runId != d.RunsStarted) BeginRun();
                    OpenFor(d.Encounter);
                    break;
                case RunState.Intro:
                    if (Run != null)
                    {
                        Run.Close();
                        foreach (var b in Run.Slots) b.ResetEncounter();
                        Run.CrackUntil = -1; Run.BrandUntil = -1; Run.BrandOwner = null;
                        ItemHooks.ClearEffects();
                        d.Player.RefreshHealth(refillPotions: false);   // the shop's refill, now at the bought max HP (Grit, Glass Edge)
                    }
                    break;
                case RunState.Cleared:
                    if (Run != null && d.Mode == RunMode.Run && d.Boss != null)   // practice never pays
                    {
                        bool final = RunDirector.Bosses[d.Encounter].Final;
                        int seconds = (d.World.Tick - d.World.SegmentStartTick) / Sim.Hz;
                        int before = Run.Slot(0).Earned;
                        Run.PayClear(d.Boss.DisplayName.Split(',')[0].Trim(), final, d.World.Players.All(p => !p.Down), seconds);
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
            Run = new ShopRun(SeedOverride ?? Guid.NewGuid().GetHashCode(), d.Mode == RunMode.Practice ? 1 : d.EncounterOrder.Length);
            d.Player.Build = Run.Add(d.ClassName);   // party bots / netcode add their slots here later
            ItemHooks.Run = Run;
            d.Log.Add($"shop: new run, seed {Run.Seed}");
        }
        void EndRun()
        {
            if (Run == null) return;
            if (d.Player) d.Player.Build = null;
            Run = null; ItemHooks.Run = null; runId = -1; openedFor = -1;
        }

        void OpenFor(int encounter)
        {
            if (openedFor == encounter + 1000 * d.RunsStarted) return;   // one visit per boss (Ready can't re-enter mid-visit)
            openedFor = encounter + 1000 * d.RunsStarted; openedAt = Time.frameCount;
            int pos = d.Mode == RunMode.Run ? Array.IndexOf(d.EncounterOrder, encounter) : 0;
            bool final = RunDirector.Bosses[encounter].Final;
            // health refills at the shop (as before: every boss after the first); potions follow the existing rule
            if (pos > 0) d.Player.RefreshHealth(refillPotions: pos == d.EncounterOrder.Length - 1);
            Run.OpenVisit(RunDirector.Bosses[encounter].Name, final, areaShop: !final);
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
            if (kb != null && kb.rKey.wasPressedThisFrame) Do(Run.TryReroll(0, out var why), why);
        }

        void Do(bool ok, string why) { LastRefusal = ok ? null : why; }

        // ---------------------------------------------------------------- screen
        GUIStyle eyebrow, title, body, small, smallC, smallLow, price, chimes, btn, btnP, tierSt;
        int styleH = -1;
        static readonly Color RareCol = new Color(0.60f, 0.74f, 1.00f), KeyCol = UiTheme.GoldBright;
        static Color TierCol(ItemTier t) => t == ItemTier.Common ? UiTheme.IvoryBody : t == ItemTier.Rare ? RareCol : KeyCol;
        static string TierName(ItemTier t) => t == ItemTier.Common ? "COMMON" : t == ItemTier.Rare ? "RARE" : "KEYSTONE";

        void Styles()
        {
            if (styleH == Screen.height && eyebrow != null) return;
            styleH = Screen.height;
            eyebrow = UiTheme.Style(14, true, TextAnchor.MiddleLeft, UiTheme.GoldLabel);
            title = UiTheme.Style(30, true, TextAnchor.MiddleLeft, UiTheme.Ivory);
            body = UiTheme.Style(16, false, TextAnchor.UpperLeft, UiTheme.IvoryBody, true);
            small = UiTheme.Style(14, false, TextAnchor.UpperLeft, UiTheme.IvoryMuted, true);
            smallC = UiTheme.Style(14, false, TextAnchor.MiddleCenter, UiTheme.IvoryMuted);
            smallLow = UiTheme.Style(14, false, TextAnchor.LowerLeft, UiTheme.IvoryMuted, true);
            price = UiTheme.Style(18, true, TextAnchor.MiddleLeft, UiTheme.Gold);
            chimes = UiTheme.Style(28, true, TextAnchor.MiddleRight, UiTheme.GoldBright);
            tierSt = UiTheme.Style(13, true, TextAnchor.MiddleLeft, UiTheme.IvoryBody);
            btn = UiTheme.ButtonStyle(15); btnP = UiTheme.ButtonStyle(18, true);
        }
        static Rect R(float x, float y, float w, float h) => UiTheme.R(x, y, w, h);
        bool Button(float x, float y, float w, float h, string text, bool enabled, GUIStyle st = null)
        {
            bool keep = GUI.enabled; GUI.enabled = keep && enabled;
            bool hit = GUI.Button(R(x, y, w, h), text, st ?? btn);
            GUI.enabled = keep; return hit;
        }

        // One screen, no scrolling or paging (Chzy18, 29 Oct): the panel height follows the content, worst case a full
        // 8-player party (7 allies) and a long build, which still fits 1080 virtual px.
        const float Pad = 24, HeadH = 102, CardH = 320, CardGap = 14, ChipW = 290, ChipH = 36, ChipGap = 8, AllyW = 368, AllyH = 64, FootH = 48;
        static int Rows(int n, int perRow) => n <= 0 ? 1 : (n + perRow - 1) / perRow;

        void OnGUI()
        {
            ClearPopup();
            if (!Showing) return;
            GUI.depth = -1;   // above the run HUD
            Styles();
            var me = Run.Slot(0);
            bool coop = !Run.Solo;
            var allies = Run.Slots.Where(s => s != me).ToList();
            float vw = UiTheme.VirtualWidth, W = Mathf.Min(1560, vw - 64), iw = W - 2 * Pad;
            int chipsPerRow = Mathf.Max(1, (int)((iw + ChipGap) / (ChipW + ChipGap)));
            float chipW = (iw - (chipsPerRow - 1) * ChipGap) / chipsPerRow;   // chips stretch to fill the row
            int alliesPerRow = Mathf.Max(1, (int)((iw + 12) / (AllyW + 12)));
            float buildH = 26 + Rows(me.Items.Count, chipsPerRow) * (ChipH + ChipGap) + 12;
            float allyH = coop ? 26 + Rows(allies.Count, alliesPerRow) * (AllyH + 10) + 8 : 0;
            float H = Pad + HeadH + CardH + 20 + buildH + allyH + FootH + Pad;
            float x0 = (vw - W) / 2, y0 = Mathf.Max(16, (1080 - H) / 2);
            UiTheme.Frame(R(x0, y0, W, H), UiTheme.InkDeep);
            float x = x0 + Pad, y = y0 + Pad;

            // header: visit, next boss, wallet, this visit's earnings
            UiTheme.Label(R(x, y, iw, 20), UiTheme.Spaced(d.Mode == RunMode.Practice ? "Practice shop" : Run.FinalShop ? "Final shop" : $"Shop {Run.Visit} of {Run.TotalVisits}"), eyebrow);
            UiTheme.Label(R(x, y + 22, iw - 340, 40), "INTERMISSION  ·  next: " + Run.NextBoss, title);
            string wallet = $"{me.Chimes}  CHIMES"; float tw = chimes.CalcSize(new GUIContent(wallet)).x / UiTheme.Scale;
            UiTheme.Label(R(x + iw - 320, y + 8, 320, 40), wallet, chimes);
            if (Coin) GUI.DrawTexture(R(x + iw - tw - 50, y + 8, 40, 40), Coin, ScaleMode.ScaleToFit, true);
            UiTheme.Label(R(x + iw - 340, y + 46, 340, 18), $"Keystones {me.Keystones}/{ItemCatalog.KeystonesPerRun}  ·  earned this run {me.Earned}", UiTheme.Style(13, false, TextAnchor.MiddleRight, UiTheme.IvoryMicro));
            if (Run.Ledger.Count > 0) UiTheme.Label(R(x, y + 72, iw, 20), string.Join("     ", Run.Ledger), small);
            y += HeadH;

            // six cards
            int cards = me.StockCount; float cw = (iw - (cards - 1) * CardGap) / cards;   // 6 cards, 7 with Fourth Choice
            for (int i = 0; i < cards; i++) Card(me, i, x + i * (cw + CardGap), y, cw, CardH);
            y += CardH + 20;

            // owned items with Temper (chips wrapping in rows)
            UiTheme.Label(R(x, y, iw, 20), UiTheme.Spaced("Your build") + (me.Items.Count == 0 ? "      nothing yet" : $"      {me.Items.Count} items"), eyebrow);
            float cy = y + 26; int k = 0;
            foreach (var o in me.Items.ToArray())
            {
                float ox = x + (k % chipsPerRow) * (chipW + ChipGap), oy = cy + (k / chipsPerRow) * (ChipH + ChipGap); k++;
                bool temperable = o.Def.Tempered.HasValue && !o.Tempered;
                UiTheme.Frame(R(ox, oy, chipW, ChipH), UiTheme.Ink, false, false);
                var ic = ItemIcon(o.Def, false); if (ic) GUI.DrawTexture(R(ox + 4, oy + 4, 28, 28), ic, ScaleMode.ScaleToFit, true);
                UiTheme.Label(R(ox + 38, oy + 9, chipW - 38 - (temperable ? 112 : 8), 22), o.Def.Name + (o.Tempered ? " +" : ""), UiTheme.Style(15, true, TextAnchor.UpperLeft, TierCol(o.Def.Tier)));
                if (temperable && Button(ox + chipW - 108, oy + 3, 104, ChipH - 6, $"Temper {ItemCatalog.TemperPrice}", me.Chimes >= ItemCatalog.TemperPrice)) Do(Run.TryTemper(0, o.Def.Id, out var why), why);
            }
            y += buildH;

            // allies (co-op only): build, wallet, ready, chip in
            if (coop)
            {
                UiTheme.Label(R(x, y, iw, 20), UiTheme.Spaced("Allies") + $"      chip in: each ally can receive {ShopRun.GiftCapPerVisit} per visit", eyebrow);
                float ay0 = y + 26; int n = 0;
                foreach (var a in allies)
                {
                    float ax = x + (n % alliesPerRow) * (AllyW + 12), ay = ay0 + (n / alliesPerRow) * (AllyH + 10); n++;
                    UiTheme.Frame(R(ax, ay, AllyW, AllyH), UiTheme.Ink, false, false);
                    UiTheme.Label(R(ax + 12, ay + 6, AllyW - 24, 20), $"{a.ClassName}  ·  {a.Chimes} Chimes  ·  got {a.ReceivedThisVisit}/{ShopRun.GiftCapPerVisit}{(a.Ready ? "  ·  READY" : "")}", UiTheme.Style(14, true, TextAnchor.UpperLeft, UiTheme.Ivory));
                    UiTheme.Label(R(ax + 12, ay + 30, AllyW - 136, 30), a.Items.Count == 0 ? "no items" : string.Join(", ", a.Items.Select(o => o.Def.Name + (o.Tempered ? "+" : ""))), small);
                    if (Button(ax + AllyW - 116, ay + 28, 104, 30, "Chip in 50", me.Chimes >= 50 && a.ReceivedThisVisit + 50 <= ShopRun.GiftCapPerVisit)) Do(Run.TryGift(0, a.Slot, 50, out var why), why);
                }
                y += allyH;
            }

            // footer: reroll, refusal / hints, ready timer, ready
            float fy = y0 + H - Pad - FootH;
            int rp = Run.RerollPrice(me);
            if (Button(x, fy, 200, FootH, $"Reroll {rp}  (R)", me.Chimes >= rp)) Do(Run.TryReroll(0, out var why), why);
            UiTheme.Label(R(x + 220, fy + 4, iw - 520, 20), LastRefusal ?? (me.LockUsed ? "Lock used this visit." : "Lock keeps one card through rerolls (one lock per visit, free)."), LastRefusal != null ? UiTheme.Style(15, true, TextAnchor.UpperLeft, UiTheme.Danger) : small);
            UiTheme.Label(R(x + 220, fy + 26, iw - 520, 20), coop ? $"The shop closes when everyone is ready or after {ShopRun.CoopTimerSeconds} s: {Mathf.Max(0, ShopRun.CoopTimerSeconds - d.StateTicks / Sim.Hz)} s left." : "Unspent Chimes stay in your wallet. The shop waits for you.", coop ? UiTheme.Style(14, true, TextAnchor.UpperLeft, UiTheme.Gold) : small);
            if (Button(x + iw - 280, fy, 280, FootH, me.Ready ? "WAITING FOR ALLIES" : "READY  (Enter)", !me.Ready, btnP)) Leave();
            LastPanel = new Rect(x0, y0, W, H);
        }
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
            string text = $"+{LastPopup} CHIMES"; var st = UiTheme.Style(30, true, TextAnchor.MiddleLeft, UiTheme.GoldBright);
            float w = st.CalcSize(new GUIContent(text)).x / UiTheme.Scale, cx = UiTheme.VirtualWidth / 2, y = 480 - rise;
            var keep = GUI.color; GUI.color = new Color(1, 1, 1, a);
            if (Burst) GUI.DrawTexture(R(cx - (w + 56) / 2, y - 4, 48, 48), Burst, ScaleMode.ScaleToFit, true);
            UiTheme.Label(R(cx - (w + 56) / 2 + 56, y, w + 8, 40), text, st, new Color(UiTheme.GoldBright.r, UiTheme.GoldBright.g, UiTheme.GoldBright.b, a));
            GUI.color = keep;
        }

        void Card(PlayerBuild me, int i, float x, float y, float w, float h)
        {
            var def = me.Stock[i];
            bool locked = me.Locked == i;
            UiTheme.Frame(R(x, y, w, h), locked ? UiTheme.FocusFill : UiTheme.Ink, i == ShopRun.SpecialSlot, false);
            float px = x + 14, pw = w - 28, cy = y + 14;
            string slotName = i < ShopRun.ClassSlots || i == ShopRun.ExtraClassSlot ? me.ClassName : i < ShopRun.ClassSlots + ShopRun.GeneralSlots ? "General" : "Rare or Keystone";
            if (def == null)
            {
                UiTheme.Label(R(px, cy, pw, 18), UiTheme.Spaced(slotName), UiTheme.Style(12, true, TextAnchor.UpperLeft, UiTheme.IvoryMicro));
                UiTheme.Label(R(x, y + h / 2 - 10, w, 20), "sold", smallC);
                return;
            }
            // icon on the left, tier and name beside it
            var ic = ItemIcon(def, true); float tx = px + (ic ? 74 : 0);
            if (ic) GUI.DrawTexture(R(px, cy, 64, 64), ic, ScaleMode.ScaleToFit, true);
            UiTheme.Label(R(tx, cy, px + pw - tx, 18), TierName(def.Tier) + (def.Tags != ItemTag.None ? "  ·  " + def.Tags.ToString().ToUpperInvariant() : ""), UiTheme.Style(13, true, TextAnchor.UpperLeft, TierCol(def.Tier)));
            UiTheme.Label(R(tx, cy + 20, px + pw - tx, 48), def.Name, UiTheme.Style(19, true, TextAnchor.UpperLeft, UiTheme.Ivory, true));
            cy += 76;
            UiTheme.Label(R(px, cy, pw, 130), def.Card(false), body);
            cy += 134;
            float by = y + h - 14 - 40;
            if (Run.Solo && def.SoloNote != null) UiTheme.Label(R(px, by - 34 - 40, pw, 40), def.SoloNote, smallLow);   // sits just above the price, however narrow the card
            if (Coin) GUI.DrawTexture(R(px, by - 30, 24, 24), Coin, ScaleMode.ScaleToFit, true);
            UiTheme.Label(R(px + (Coin ? 30 : 0), by - 30, pw, 24), $"{def.Price} Chimes", price);
            bool keyCap = def.Tier == ItemTier.Keystone && me.Keystones >= ItemCatalog.KeystonesPerRun;
            if (Button(px, by, pw * 0.62f, 40, "Buy", me.Chimes >= def.Price && !keyCap)) Do(Run.TryBuy(0, i, out var why), why);
            if (Button(px + pw * 0.66f, by, pw * 0.34f, 40, locked ? "Locked" : "Lock", !me.LockUsed)) Do(Run.TryLock(0, i, out var why), why);
        }
    }
}
