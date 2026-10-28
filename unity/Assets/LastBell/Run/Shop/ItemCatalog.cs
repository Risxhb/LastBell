using System.Collections.Generic;
using System.Linq;

namespace LastBell.Run.Shop
{
    public enum ItemTier { Common, Rare, Keystone }
    [System.Flags] public enum ItemTag { None = 0, Crack = 1, Ring = 2, Glyph = 4, Party = 8 }

    // One run item (shop spec §11). Value is the item's number as written on the card; Tempered is its + version
    // (+50% of the effect, spec §4) or null when the item can't be tempered. Every effect lives in ItemHooks, keyed by Id.
    public sealed class ItemDef
    {
        public string Id, Name, Class;      // Class null = general (every class)
        public ItemTier Tier; public ItemTag Tags;
        public float Value; public float? Tempered;
        public bool OfferedSolo = true;     // items with no solo value are never stocked solo (spec §3)
        public string Effect, SoloNote;     // card text; SoloNote = the solo fallback, shown when playing alone
        public int Price => ItemCatalog.Price(Tier);
        public int Budget => Tier == ItemTier.Common ? 5 : Tier == ItemTier.Rare ? 8 : 12;   // spec §8, DPS points of 100
        public bool General => Class == null;
        public string Card(bool tempered) => Effect.Replace("{v}", Fmt(tempered && Tempered.HasValue ? Tempered.Value : Value));
        static string Fmt(float v) => v == (int)v ? ((int)v).ToString() : v.ToString("0.##");
    }

    // Slice 1 of the pool (docs/design/run-shop-plan.md §3): items from meta-loot-and-toll §1.5-1.8 whose effects
    // need only the hooks listed there. Prices are Chzy18's starting prices (spec §12.2).
    public static class ItemCatalog
    {
        public const int CommonPrice = 90, RarePrice = 150, KeystonePrice = 240, TemperPrice = 110;
        public const int RerollBase = 30, RerollStep = 15, KeystonesPerRun = 2;
        public static int Price(ItemTier t) => t == ItemTier.Common ? CommonPrice : t == ItemTier.Rare ? RarePrice : KeystonePrice;

        // ids (ItemHooks switches on these)
        public const string TemperedEdge = "v.tempered_edge", Countdown = "v.countdown", CrackedArmour = "v.cracked_armour", Oathbound = "v.oathbound";
        public const string HeavyBell = "c.heavy_bell", SwiftChorus = "c.swift_chorus", ChorusOfValour = "c.chorus_of_valour";
        public const string UnboundCast = "a.unbound_cast", LongSurge = "a.long_surge", PerpetualBeam = "a.perpetual_beam";
        public const string StrongBrew = "g.strong_brew", Sprinter = "g.sprinter", DeepBreath = "g.deep_breath", LoneBell = "g.lone_bell", SpareFlask = "g.spare_flask";

        public static readonly ItemDef[] All =
        {
            // ---- Vanguard ----
            new ItemDef { Id = TemperedEdge, Name = "Tempered Edge", Class = "Vanguard", Tier = ItemTier.Common, Value = 20, Tempered = 30,
                Effect = "Steel Rhythm hit 3 deals +{v}% damage." },
            new ItemDef { Id = Countdown, Name = "Countdown", Class = "Vanguard", Tier = ItemTier.Common, Value = 20, Tempered = 30,
                Effect = "Last Word cooldown -{v}%." },
            new ItemDef { Id = CrackedArmour, Name = "Cracked Armour", Class = "Vanguard", Tier = ItemTier.Rare, Tags = ItemTag.Crack, Value = 15, Tempered = 22.5f,
                Effect = "Sundering Blow cracks the boss for 4 s. Everyone's heavy hits on it deal +{v}%: Last Word, Bellstrike, Crescendo, Rune Sigil, Arcane Comet.",
                SoloNote = "Solo: applies to your own Last Word." },
            new ItemDef { Id = Oathbound, Name = "Oathbound", Class = "Vanguard", Tier = ItemTier.Keystone, Value = 3, Tempered = 2,
                Effect = "Sundering Blow can't be empowered. Every {v}th Opening you earn resets Last Word's cooldown." },
            // ---- Chimesinger ----
            new ItemDef { Id = HeavyBell, Name = "Heavy Bell", Class = "Chimesinger", Tier = ItemTier.Common, Value = 20, Tempered = 30,
                Effect = "Bellstrike deals +{v}% damage." },
            new ItemDef { Id = SwiftChorus, Name = "Swift Chorus", Class = "Chimesinger", Tier = ItemTier.Common, Value = 15, Tempered = 22.5f,
                Effect = "Mending Chorus cooldown -{v}%." },
            new ItemDef { Id = ChorusOfValour, Name = "Chorus of Valour", Class = "Chimesinger", Tier = ItemTier.Rare, Tags = ItemTag.Party, Value = 12, Tempered = 18,
                Effect = "Allies touched by Mending Chorus deal +{v}% damage for 4 s.", SoloNote = "Solo: gives it to you." },
            // ---- Arcanist (Risx's glyph kit) ----
            new ItemDef { Id = UnboundCast, Name = "Unbound Cast", Class = "Arcanist", Tier = ItemTier.Common, Value = 50, Tempered = 57.5f,
                Effect = "Rune Sigil and Arcane Comet cast without a glyph deal {v}% instead of 35%." },
            new ItemDef { Id = LongSurge, Name = "Long Surge", Class = "Arcanist", Tier = ItemTier.Rare, Value = 2.5f, Tempered = 2.75f,
                Effect = "Arcane Surge lasts {v} s per glyph instead of 2 s." },
            new ItemDef { Id = PerpetualBeam, Name = "Perpetual Beam", Class = "Arcanist", Tier = ItemTier.Keystone, Value = 70, Tempered = 87.5f,
                Effect = "Disintegrate can't build more than 2 glyphs, but Rune Sigil and Arcane Comet cast without a glyph deal {v}%." },
            // ---- general ----
            new ItemDef { Id = StrongBrew, Name = "Strong Brew", Tier = ItemTier.Common, Value = 48, Tempered = 52,
                Effect = "Potions heal {v} instead of 40. The carry-over rule is unchanged." },
            new ItemDef { Id = Sprinter, Name = "Sprinter", Tier = ItemTier.Common, Value = 21, Tempered = 19,
                Effect = "Sprint drains {v}/s instead of 25/s." },
            new ItemDef { Id = DeepBreath, Name = "Deep Breath", Tier = ItemTier.Common, Value = 0.6f, Tempered = 0.53f,
                Effect = "Stamina starts regenerating {v} s after use instead of 0.75 s." },
            new ItemDef { Id = LoneBell, Name = "Lone Bell", Tier = ItemTier.Rare, Value = 15, Tempered = 22.5f,
                Effect = "+{v}% damage while no ally is within 8 m." },
            new ItemDef { Id = SpareFlask, Name = "Spare Flask", Tier = ItemTier.Rare, Value = 1, Tempered = null,
                Effect = "+{v} potion at each refill. Potions still carry over between minibosses." },
        };

        static Dictionary<string, ItemDef> byId;
        public static ItemDef Get(string id)
        {
            if (byId == null) byId = All.ToDictionary(i => i.Id);
            return id != null && byId.TryGetValue(id, out var d) ? d : null;
        }
    }
}
