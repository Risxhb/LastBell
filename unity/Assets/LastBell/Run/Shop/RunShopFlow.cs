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
                    if (!Enabled || d.Mode != RunMode.Run) { EndRun(); break; }
                    if (Run == null || runId != d.RunsStarted) BeginRun();
                    OpenFor(d.Encounter);
                    break;
                case RunState.Intro:
                    if (Run != null)
                    {
                        Run.Close();
                        foreach (var b in Run.Slots) b.ResetEncounter();
                        Run.CrackUntil = -1;
                    }
                    break;
                case RunState.Cleared:
                    if (Run != null && d.Mode == RunMode.Run && d.Boss != null)
                    {
                        bool final = RunDirector.Bosses[d.Encounter].Final;
                        int seconds = (d.World.Tick - d.World.SegmentStartTick) / Sim.Hz;
                        Run.PayClear(d.Boss.DisplayName.Split(',')[0].Trim(), final, d.World.Players.All(p => !p.Down), seconds);
                    }
                    break;
                case RunState.Results: case RunState.Menu:
                    EndRun();
                    break;
            }
        }

        void BeginRun()
        {
            runId = d.RunsStarted; openedFor = -1;
            Run = new ShopRun(SeedOverride ?? Guid.NewGuid().GetHashCode(), d.EncounterOrder.Length);
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
            int pos = Array.IndexOf(d.EncounterOrder, encounter);
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
        GUIStyle eyebrow, title, body, small, smallC, price, chimes, btn, btnP, tierSt;
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

        void OnGUI()
        {
            if (!Showing) return;
            GUI.depth = -1;   // above the run HUD
            Styles();
            var me = Run.Slot(0);
            float vw = UiTheme.VirtualWidth, W = Mathf.Min(1560, vw - 64), x0 = (vw - W) / 2, y0 = 96, pad = 24;
            bool coop = !Run.Solo;
            float H = 760 + (coop ? 120 : 0);
            UiTheme.Frame(R(x0, y0, W, H), UiTheme.InkDeep);
            float x = x0 + pad, y = y0 + pad, iw = W - 2 * pad;

            // header: visit, next boss, wallet
            UiTheme.Label(R(x, y, iw, 20), UiTheme.Spaced(Run.FinalShop ? "Final shop" : $"Shop {Run.Visit} of {Run.TotalVisits}"), eyebrow);
            UiTheme.Label(R(x, y + 22, iw - 320, 40), "INTERMISSION  ·  next: " + Run.NextBoss, title);
            UiTheme.Label(R(x + iw - 320, y + 8, 320, 40), $"{me.Chimes}  CHIMES", chimes);
            UiTheme.Label(R(x + iw - 320, y + 46, 320, 18), $"Keystones {me.Keystones}/{ItemCatalog.KeystonesPerRun}  ·  earned this run {me.Earned}", UiTheme.Style(13, false, TextAnchor.MiddleRight, UiTheme.IvoryMicro));
            y += 72;
            if (Run.Ledger.Count > 0) { UiTheme.Label(R(x, y, iw, 20), string.Join("     ", Run.Ledger), small); }
            y += 30;

            // six cards
            const float gap = 14; float cw = (iw - 5 * gap) / 6, ch = 330;
            for (int i = 0; i < ShopRun.StockSize; i++) Card(me, i, x + i * (cw + gap), y, cw, ch);
            y += ch + 20;

            // owned items with Temper
            UiTheme.Label(R(x, y, iw, 20), UiTheme.Spaced("Your build") + (me.Items.Count == 0 ? "      nothing yet" : ""), eyebrow);
            y += 26;
            float ox = x;
            foreach (var o in me.Items.ToArray())
            {
                string label = o.Def.Name + (o.Tempered ? " +" : "");
                float w = Mathf.Max(150, body.CalcSize(new GUIContent(label)).x / UiTheme.Scale + 24);
                if (ox + w + 130 > x + iw) { ox = x; y += 44; }
                UiTheme.Frame(R(ox, y, w, 36), UiTheme.Ink, false, false);
                UiTheme.Label(R(ox + 12, y + 8, w - 16, 22), label, UiTheme.Style(15, true, TextAnchor.UpperLeft, TierCol(o.Def.Tier)));
                bool can = !o.Tempered && o.Def.Tempered.HasValue && me.Chimes >= ItemCatalog.TemperPrice;
                if (o.Def.Tempered.HasValue && !o.Tempered && Button(ox + w + 6, y, 118, 36, $"Temper {ItemCatalog.TemperPrice}", can)) Do(Run.TryTemper(0, o.Def.Id, out var why), why);
                ox += w + (o.Def.Tempered.HasValue && !o.Tempered ? 130 : 12);
            }
            y += 56;

            // allies (co-op only): build, wallet, chip in
            if (coop)
            {
                UiTheme.Label(R(x, y, iw, 20), UiTheme.Spaced("Allies") + $"      chip in: each ally can receive {ShopRun.GiftCapPerVisit} per visit", eyebrow);
                y += 26; float ax = x;
                foreach (var a in Run.Slots.Where(s => s != me))
                {
                    UiTheme.Frame(R(ax, y, 360, 70), UiTheme.Ink, false, false);
                    UiTheme.Label(R(ax + 12, y + 6, 340, 20), $"{a.ClassName}  ·  {a.Chimes} Chimes{(a.Ready ? "  ·  READY" : "")}", UiTheme.Style(15, true, TextAnchor.UpperLeft, UiTheme.Ivory));
                    UiTheme.Label(R(ax + 12, y + 30, 230, 36), a.Items.Count == 0 ? "no items" : string.Join(", ", a.Items.Select(o => o.Def.Name + (o.Tempered ? "+" : ""))), small);
                    if (Button(ax + 250, y + 26, 100, 36, "Chip in 50", me.Chimes >= 50 && a.ReceivedThisVisit + 50 <= ShopRun.GiftCapPerVisit)) Do(Run.TryGift(0, a.Slot, 50, out var why), why);
                    ax += 372;
                }
                y += 90;
            }

            // footer: reroll, refusal, ready
            float fy = y0 + H - pad - 48;
            int rp = Run.RerollPrice(me);
            if (Button(x, fy, 200, 48, $"Reroll {rp}  (R)", me.Chimes >= rp)) Do(Run.TryReroll(0, out var why), why);
            UiTheme.Label(R(x + 220, fy + 4, iw - 520, 20), LastRefusal ?? (me.LockUsed ? "Lock used this visit." : "Lock keeps one card through rerolls (one lock per visit, free)."), LastRefusal != null ? UiTheme.Style(15, true, TextAnchor.UpperLeft, UiTheme.Danger) : small);
            UiTheme.Label(R(x + 220, fy + 26, iw - 520, 20), coop ? $"The shop closes when everyone is ready or after {ShopRun.CoopTimerSeconds} s ({Mathf.Max(0, ShopRun.CoopTimerSeconds - d.StateTicks / Sim.Hz)} s)." : "Unspent Chimes stay in your wallet. The shop waits for you.", small);
            if (Button(x + iw - 280, fy, 280, 48, me.Ready ? "WAITING FOR ALLIES" : "READY  (Enter)", !me.Ready, btnP)) Leave();
        }

        void Card(PlayerBuild me, int i, float x, float y, float w, float h)
        {
            var def = me.Stock[i];
            bool locked = me.Locked == i;
            UiTheme.Frame(R(x, y, w, h), locked ? UiTheme.FocusFill : UiTheme.Ink, i == ShopRun.SpecialSlot, false);
            float px = x + 14, pw = w - 28, cy = y + 14;
            string slotName = i < ShopRun.ClassSlots ? me.ClassName : i < ShopRun.ClassSlots + ShopRun.GeneralSlots ? "General" : "Rare or Keystone";
            if (def == null)
            {
                UiTheme.Label(R(px, cy, pw, 18), UiTheme.Spaced(slotName), UiTheme.Style(12, true, TextAnchor.UpperLeft, UiTheme.IvoryMicro));
                UiTheme.Label(R(x, y + h / 2 - 10, w, 20), "sold", smallC);
                return;
            }
            UiTheme.Label(R(px, cy, pw, 18), TierName(def.Tier) + (def.Tags != ItemTag.None ? "  ·  " + def.Tags.ToString().ToUpperInvariant() : ""), UiTheme.Style(13, true, TextAnchor.UpperLeft, TierCol(def.Tier)));
            cy += 22;
            UiTheme.Label(R(px, cy, pw, 50), def.Name, UiTheme.Style(20, true, TextAnchor.UpperLeft, UiTheme.Ivory, true));
            cy += 54;
            UiTheme.Label(R(px, cy, pw, 120), def.Card(false), body);
            cy += 124;
            if (Run.Solo && def.SoloNote != null) UiTheme.Label(R(px, cy, pw, 36), def.SoloNote, small);
            float by = y + h - 14 - 40;
            UiTheme.Label(R(px, by - 30, pw, 24), $"{def.Price} Chimes", price);
            bool keyCap = def.Tier == ItemTier.Keystone && me.Keystones >= ItemCatalog.KeystonesPerRun;
            if (Button(px, by, pw * 0.62f, 40, "Buy", me.Chimes >= def.Price && !keyCap)) Do(Run.TryBuy(0, i, out var why), why);
            if (Button(px + pw * 0.66f, by, pw * 0.34f, 40, locked ? "Locked" : "Lock", !me.LockUsed)) Do(Run.TryLock(0, i, out var why), why);
        }
    }
}
