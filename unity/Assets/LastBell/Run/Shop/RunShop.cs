using System;
using System.Collections.Generic;
using System.Linq;

namespace LastBell.Run.Shop
{
    // One player slot's run build (shop spec §11 "Run state"): wallet, owned items, Keystone count and this visit's stock.
    // Lives for one run; the director's fresh run builds a new ShopRun, so nothing carries over between runs.
    public sealed class PlayerBuild
    {
        public sealed class Owned
        {
            public ItemDef Def; public bool Tempered;
            public float Value => Tempered && Def.Tempered.HasValue ? Def.Tempered.Value : Def.Value;
            public float Value2 => Tempered && Def.Tempered.HasValue ? Def.Tempered2 : Def.Value2;
        }
        // per-encounter item state (ItemHooks), rebuilt at every encounter start: class state resets (design rules §3)
        public sealed class Live
        {
            public int Openings, ValourUntil = -1, RallyUntil = -1, LightFeetUntil = -1, VolleyBoostUntil = -1;
            public float ValourPct, RallyPct, Overflow;
            public bool MomentumReady, MomentumHit, GraceReady, BlinkReady, RhythmOn;
            public int RhythmCombos, Notes, RingHits, StruckNotes;
            public readonly List<int> Taps = new List<int>();
        }
        public Live L { get; private set; } = new Live();

        public readonly int Slot; public readonly string ClassName;
        public readonly bool TestSlot;            // autotest-only slot: wallet and build, no body (co-op checks before netcode)
        public int Chimes, Earned, ReceivedThisVisit, Keystones;
        public readonly List<Owned> Items = new List<Owned>();
        // this visit's stock: 6 cards (3 class, 2 general, 1 Rare-or-Keystone), null = sold or nothing left to offer
        public readonly ItemDef[] Stock = new ItemDef[ShopRun.MaxStock];
        public int StockCount => Has(ItemCatalog.FourthChoice) ? ShopRun.MaxStock : ShopRun.StockSize;   // Fourth Choice: +1 class card
        public int Locked = -1; public bool LockUsed; public int Rerolls; public bool Ready;

        public PlayerBuild(int slot, string className, bool testSlot = false) { Slot = slot; ClassName = className; TestSlot = testSlot; }
        public Owned Get(string id) { foreach (var o in Items) if (o.Def.Id == id) return o; return null; }
        public bool Has(string id) => Get(id) != null;
        public float V(string id) => Get(id)?.Value ?? 0;
        public float V2(string id) => Get(id)?.Value2 ?? 0;
        public void ResetEncounter() { L = new Live(); }
    }

    // The run's shop state and its host checks (spec §5.5): every change is a Try* request naming the requesting slot,
    // validated here against that slot's own wallet and stock. The screen and the bot only call these, so a networked
    // client later sends the same requests and only the host runs them.
    public sealed class ShopRun
    {
        public const int StockSize = 6, MaxStock = 7, ClassSlots = 3, GeneralSlots = 2, SpecialSlot = 5, ExtraClassSlot = 6;
        public const int AreaBossPay = 150, MinibossPay = 100, BonusPay = 20, GiftCapPerVisit = 150, CoopTimerSeconds = 90;
        // accepted by Chzy18 2026-10-28 (run-shop-plan §1): pay the area's 3 missing miniboss clears at its shop until minibosses join the run
        public static bool StandInMinibossPay = true;
        // accepted by Chzy18 2026-10-28 (run-shop-plan §1): par times for the "cleared under par" bonus, seconds
        public const int AreaBossPar = 240, FinalBossPar = 360, MinibossPar = 150;

        public readonly int Seed;
        public readonly List<PlayerBuild> Slots = new List<PlayerBuild>();
        public int Visit { get; private set; }            // 1-based; 0 before the first shop
        public int TotalVisits { get; private set; }
        public bool Open { get; private set; }
        public bool FinalShop { get; private set; }
        public string NextBoss { get; private set; }
        public int CrackUntil = -1; public float CrackPct; // Cracked Armour, boss-wide
        public int BrandUntil = -1, BrandHits; public PlayerController BrandOwner;   // Glyph Brand, boss-wide
        public readonly List<string> Ledger = new List<string>();   // earnings since the last shop, shown on the screen
        public readonly List<string> Log = new List<string>();      // every accepted request (tests, playtest reports)

        public ShopRun(int seed, int totalVisits) { Seed = seed; TotalVisits = totalVisits; }
        public bool Solo => Slots.Count <= 1;
        public PlayerBuild Add(string className, bool testSlot = false) { var b = new PlayerBuild(Slots.Count, className, testSlot); Slots.Add(b); return b; }
        public PlayerBuild Slot(int i) => i >= 0 && i < Slots.Count ? Slots[i] : null;
        public bool ClassInParty(string cls) => Slots.Any(s => s.ClassName == cls);

        // ---------------- earning (spec §2) ----------------
        public void Pay(int amount, string label)
        {
            if (amount <= 0) return;
            foreach (var s in Slots) { s.Chimes += amount; s.Earned += amount; }
            Ledger.Add($"{label}  +{amount}");
            Log.Add($"pay {amount} ({label})");
        }
        public void PayClear(string boss, bool final, bool nobodyDown, int seconds)
        {
            Pay(AreaBossPay, boss + " cleared");
            if (nobodyDown) Pay(BonusPay, "Nobody went down");
            if (seconds <= (final ? FinalBossPar : AreaBossPar)) Pay(BonusPay, "Under par time");
        }

        // ---------------- visits ----------------
        public void OpenVisit(string nextBoss, bool final, bool areaShop)
        {
            Visit++; Open = true; FinalShop = final; NextBoss = nextBoss;
            if (areaShop && StandInMinibossPay) Pay(3 * MinibossPay, "Area minibosses (stand-in)");
            foreach (var s in Slots) { s.ReceivedThisVisit = 0; s.Rerolls = 0; s.Locked = -1; s.LockUsed = false; s.Ready = s.TestSlot; Roll(s, keepLocked: false); }
            Log.Add($"open visit {Visit}{(final ? " (final)" : "")} before {nextBoss}");
        }
        public void Close()
        {
            if (!Open) return;
            Open = false; Ledger.Clear();
            Log.Add($"close visit {Visit}");
        }
        public bool AllReady => Slots.All(s => s.Ready);

        // ---------------- stock (spec §3) ----------------
        bool Offerable(PlayerBuild s, ItemDef d) => CanOffer(s, d) && Array.IndexOf(s.Stock, d) < 0;
        // whether this item may ever be stocked for this player now (held, owned, solo value, partner class, own class)
        public bool CanOffer(PlayerBuild s, ItemDef d) =>
            !d.Held && !s.Has(d.Id) && (d.OfferedSolo || !Solo) &&
            (d.PartnerClass == null || ClassInParty(d.PartnerClass)) &&
            (d.Class == null || d.Class == s.ClassName);
        bool KeystoneAllowed(PlayerBuild s) => Visit >= 2 && s.Keystones < ItemCatalog.KeystonesPerRun;

        void Roll(PlayerBuild s, bool keepLocked)
        {
            // seeded from run seed + slot + visit + reroll count, so a seeded run gives everyone the same shops
            var rng = new Random(unchecked(Seed * 73856093 ^ (s.Slot + 1) * 19349663 ^ Visit * 83492791 ^ (s.Rerolls + 1) * 50331653));
            for (int i = 0; i < MaxStock; i++) if (!(keepLocked && i == s.Locked)) s.Stock[i] = null;
            for (int i = 0; i < s.StockCount; i++)
            {
                if (keepLocked && i == s.Locked) continue;
                IEnumerable<ItemDef> pool;
                if (i < ClassSlots || i == ExtraClassSlot) pool = ItemCatalog.All.Where(d => d.Class == s.ClassName && d.Tier != ItemTier.Keystone);
                else if (i < ClassSlots + GeneralSlots) pool = ItemCatalog.All.Where(d => d.General && d.Tier != ItemTier.Keystone);
                else pool = ItemCatalog.All.Where(d => d.Tier == ItemTier.Rare || (d.Tier == ItemTier.Keystone && KeystoneAllowed(s)));
                var c = pool.Where(d => Offerable(s, d)).ToList();
                if (c.Count == 0) continue;
                // class and general cards lean Common (65/35); the final shop's special slot leans Keystone when allowed
                ItemTier want = i == SpecialSlot ? (FinalShop && KeystoneAllowed(s) || rng.NextDouble() < 0.4 && KeystoneAllowed(s) ? ItemTier.Keystone : ItemTier.Rare)
                                                 : rng.NextDouble() < 0.65 ? ItemTier.Common : ItemTier.Rare;
                var tiered = c.Where(d => d.Tier == want).ToList();
                if (tiered.Count > 0) c = tiered;
                s.Stock[i] = c[rng.Next(c.Count)];
            }
        }

        // ---------------- host-checked requests (spec §4, §5) ----------------
        public int RerollPrice(PlayerBuild s) => ItemCatalog.RerollBase + ItemCatalog.RerollStep * s.Rerolls;

        public bool TryBuy(int slot, int card, out string why)
        {
            var s = Slot(slot); why = Check(s);
            if (why == null && (card < 0 || card >= s.StockCount || s.Stock[card] == null)) why = "not in stock";
            var d = why == null ? s.Stock[card] : null;
            if (why == null && s.Has(d.Id)) why = "already owned";
            if (why == null && d.Tier == ItemTier.Keystone && s.Keystones >= ItemCatalog.KeystonesPerRun) why = "Keystone limit (2 per run)";
            if (why == null && s.Chimes < d.Price) why = "not enough Chimes";
            if (why != null) return false;
            s.Chimes -= d.Price; s.Items.Add(new PlayerBuild.Owned { Def = d });
            if (d.Tier == ItemTier.Keystone) s.Keystones++;
            s.Stock[card] = null; if (s.Locked == card) s.Locked = -1;
            Log.Add($"slot {slot} buy {d.Id} for {d.Price}");
            return true;
        }

        public bool TryTemper(int slot, string id, out string why)
        {
            var s = Slot(slot); why = Check(s);
            var o = why == null ? s.Get(id) : null;
            if (why == null && o == null) why = "not owned";
            if (why == null && o.Tempered) why = "already tempered";
            if (why == null && !o.Def.Tempered.HasValue) why = "can't be tempered";
            if (why == null && s.Chimes < ItemCatalog.TemperPrice) why = "not enough Chimes";
            if (why != null) return false;
            s.Chimes -= ItemCatalog.TemperPrice; o.Tempered = true;
            Log.Add($"slot {slot} temper {id} for {ItemCatalog.TemperPrice}");
            return true;
        }

        public bool TryReroll(int slot, out string why)
        {
            var s = Slot(slot); why = Check(s);
            if (why == null && s.Chimes < RerollPrice(s)) why = "not enough Chimes";
            if (why != null) return false;
            int price = RerollPrice(s);
            s.Chimes -= price; s.Rerolls++;
            Roll(s, keepLocked: true);
            Log.Add($"slot {slot} reroll for {price}");
            return true;
        }

        // free; one lock per visit, and the locked card survives rerolls
        public bool TryLock(int slot, int card, out string why)
        {
            var s = Slot(slot); why = Check(s);
            if (why == null && (card < 0 || card >= s.StockCount || s.Stock[card] == null)) why = "not in stock";
            if (why == null && s.LockUsed) why = "one lock per visit";
            if (why != null) return false;
            s.Locked = card; s.LockUsed = true;
            Log.Add($"slot {slot} lock card {card} ({s.Stock[card].Id})");
            return true;
        }

        // chip in (spec §5.3): each player receives at most 150 per visit; none solo (§6)
        public bool TryGift(int from, int to, int amount, out string why)
        {
            var a = Slot(from); var b = Slot(to); why = Check(a);
            if (why == null && (b == null || b == a)) why = "no such ally";
            if (why == null && Solo) why = "no chip-in solo";
            if (why == null && amount <= 0) why = "nothing to give";
            if (why == null && a.Chimes < amount) why = "not enough Chimes";
            if (why == null && b.ReceivedThisVisit + amount > GiftCapPerVisit) why = $"ally can receive {GiftCapPerVisit - b.ReceivedThisVisit} more this visit";
            if (why != null) return false;
            a.Chimes -= amount; b.Chimes += amount; b.ReceivedThisVisit += amount;
            Log.Add($"slot {from} gift {amount} to slot {to}");
            return true;
        }

        public bool SetReady(int slot, bool ready)
        {
            var s = Slot(slot); if (Check(s) != null) return false;
            s.Ready = ready; Log.Add($"slot {slot} ready {ready}");
            return true;
        }

        string Check(PlayerBuild s) => !Open ? "the shop is closed" : s == null ? "no such player" : null;
    }
}
