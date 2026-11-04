using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run.Shop
{
    public enum ItemTier { Common, Rare, Keystone }
    [System.Flags] public enum ItemTag { None = 0, Ring = 2, Glyph = 4, Party = 8 }

    // One run item (shop spec §11). Value is the item's number as written on the card; Tempered is its + version
    // (+50% of the effect, spec §4) or null when the item can't be tempered. Every effect lives in ItemHooks, keyed by Id.
    public sealed class ItemDef
    {
        public string Id, Name, Class;      // Class null = general (every class)
        public ItemTier Tier; public ItemTag Tags;
        public float Value; public float? Tempered;
        public float Value2, Tempered2;     // a second number on the card ({w}), for items with two effects
        public bool OfferedSolo = true;     // items with no solo value are never stocked solo (spec §3)
        public string PartnerClass;         // cross-class combo: stocked only when this class is in the party (spec §3)
        public bool Held;                   // accepted but never stocked: it can't do what its card says in the current build
        public string Effect, SoloNote;     // card text; SoloNote = the solo fallback, shown when playing alone
        public string TemperNote;           // what the + version adds, when it isn't just a bigger number
        public int Price => ItemCatalog.Price(Tier);
        public int Budget => Tier == ItemTier.Common ? 5 : Tier == ItemTier.Rare ? 8 : 12;   // spec §8, DPS points of 100
        public bool General => Class == null;
        public string Card(bool tempered)
        {
            float v = tempered && Tempered.HasValue ? Tempered.Value : Value, w = tempered ? Tempered2 : Value2;
            return Effect.Replace("{v}", Fmt(v)).Replace("{w}", Fmt(w)).Replace("{o}", Ordinal(Mathf.RoundToInt(v))) + (tempered && TemperNote != null ? " " + TemperNote : "");
        }
        static string Fmt(float v) => v == (int)v ? ((int)v).ToString() : v.ToString("0.##");
        static string Ordinal(int n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
    }

    // The 69-item pool (docs/design/items/CATALOGUE.md): slice 1 (15 items, run-shop-plan §3) plus the 54 accepted on
    // 2026-10-28; the Tollbreaker's 20 (accepted 2026-11-01) replaced the Vanguard's at the class swap. Prices are Chzy18's starting prices (spec §12.2). Held items are defined but never stocked.
    public static class ItemCatalog
    {
        public const int CommonPrice = 90, RarePrice = 150, KeystonePrice = 240, TemperPrice = 110;
        public const int RerollBase = 30, RerollStep = 15, KeystonesPerRun = 2;
        public static int Price(ItemTier t) => t == ItemTier.Common ? CommonPrice : t == ItemTier.Rare ? RarePrice : KeystonePrice;

        // ids (ItemHooks switches on these)
        public const string KeenEdge = "t.keen_edge", ShortFuse = "t.short_fuse", RivenIron = "t.riven_iron", RootedAxe = "t.rooted_axe";
        public const string HeavyBell = "c.heavy_bell", SwiftChorus = "c.swift_chorus", ChorusOfValour = "c.chorus_of_valour";
        public const string UnboundCast = "a.unbound_cast", LongSurge = "a.long_surge", PerpetualBeam = "a.perpetual_beam";
        public const string StrongBrew = "g.strong_brew", Sprinter = "g.sprinter", DeepBreath = "g.deep_breath", LoneBell = "g.lone_bell", SpareFlask = "g.spare_flask";
        // full pool
        public const string WideGrind = "t.wide_grind", LongGrind = "t.long_grind", QuickCatch = "t.quick_catch", HeavyHaft = "t.heavy_haft", IronPull = "t.iron_pull",
            BattleSpin = "t.battle_spin", CloseQuarters = "t.close_quarters", DeepSplit = "t.deep_split", TwinSplit = "t.twin_split", QuickGrind = "t.quick_grind",
            RingingReturn = "t.ringing_return", Backswing = "t.backswing", AnchorLine = "t.anchor_line", BerserkersPace = "t.berserkers_pace", WarCry = "t.war_cry", FrenziedHour = "t.frenzied_hour";
        public const string BrightNote = "c.bright_note", WideRefrain = "c.wide_refrain", LongRefrain = "c.long_refrain", WideChorus = "c.wide_chorus", LightStep = "c.light_step",
            GraceNote = "c.grace_note", Chord = "c.chord", ThirdEcho = "c.third_echo", Fortissimo = "c.fortissimo", Encore = "c.encore",
            NoteTrail = "c.note_trail", Overflow = "c.overflow", SharedRinging = "c.shared_ringing", StruckTogether = "c.struck_together", GreatBell = "c.great_bell", ChoirLeader = "c.choir_leader";
        public const string QuickCarving = "a.quick_carving", WideBeam = "a.wide_beam", SteadyHand = "a.steady_hand", SplitMissile = "a.split_missile", WideSigil = "a.wide_sigil",
            BlinkCharge = "a.blink_charge", FarBlink = "a.far_blink", GlassEdge = "a.glass_edge", FourthGlyph = "a.fourth_glyph", MissileVolley = "a.missile_volley", TwinSigil = "a.twin_sigil",
            HastyComet = "a.hasty_comet", GreatComet = "a.great_comet", Starfall = "a.starfall", SurgeEcho = "a.surge_echo", GlyphBrand = "a.glyph_brand", ResonantRune = "a.resonant_rune";
        public const string LongLungs = "g.long_lungs", SureFooting = "g.sure_footing", Formation = "g.formation", FourthChoice = "g.fourth_choice", LightFeet = "g.light_feet";

        public static readonly ItemDef[] All =
        {
            // ---- Tollbreaker (TB01, TB02, TB17 and TB20 took the old slice slots at the class swap, Chzy18 1 Nov) ----
            new ItemDef { Id = KeenEdge, Name = "Keen Edge", Class = "Tollbreaker", Tier = ItemTier.Common, Value = 20, Tempered = 30,
                Effect = "Twin Hurl's outward hit deals +{v}% damage." },
            new ItemDef { Id = ShortFuse, Name = "Short Fuse", Class = "Tollbreaker", Tier = ItemTier.Common, Value = 20, Tempered = 30,
                Effect = "Splitting Throw cooldown -{v}%." },
            new ItemDef { Id = RivenIron, Name = "Riven Iron", Class = "Tollbreaker", Tier = ItemTier.Rare, Value = 15, Tempered = 22.5f,
                Effect = "An axe Call of Iron sends through the boss cracks it for 4 s: your own Splitting Throw and Call of Iron hits on it deal +{v}%. Personal only." },
            new ItemDef { Id = RootedAxe, Name = "Rooted Axe", Class = "Tollbreaker", Tier = ItemTier.Keystone, Value = 25, Tempered = 30,
                Effect = "Splitting Throw's axe stays in the boss until Call of Iron, and the split lasts as long, but the bonus is +{v}% instead of +40% and Call of Iron's cooldown is 9 s." },
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

            // ======== full pool (Chzy18 accepted all 55 on 2026-10-28; docs/design/items/CATALOGUE.md, Items session) ========
            // ---- Tollbreaker (docs/proposals/tollbreaker-items-2026-10-31.md, accepted 1 Nov) ----
            new ItemDef { Id = WideGrind, Name = "Wide Grind", Class = "Tollbreaker", Tier = ItemTier.Common, Value = 30, Tempered = 45,
                Effect = "Grinding Toll's ring is {v}% wider." },
            new ItemDef { Id = LongGrind, Name = "Long Grind", Class = "Tollbreaker", Tier = ItemTier.Common, Value = 2.5f, Tempered = 2.8f,
                Effect = "Grinding Toll spins {v} s instead of 2 s." },
            new ItemDef { Id = QuickCatch, Name = "Quick Catch", Class = "Tollbreaker", Tier = ItemTier.Common, Value = 20, Tempered = 30,
                Effect = "Returning axes fly {v}% faster." },
            new ItemDef { Id = HeavyHaft, Name = "Heavy Haft", Class = "Tollbreaker", Tier = ItemTier.Common, Value = 20, Tempered = 30,
                Effect = "The held (lodging) Twin Hurl throw deals +{v}% damage." },
            new ItemDef { Id = IronPull, Name = "Iron Pull", Class = "Tollbreaker", Tier = ItemTier.Common, Value = 1.6f, Tempered = 2.4f, Value2 = 125, Tempered2 = 132,
                Effect = "Call of Iron deals +{v} per metre, and its cap per axe is {w} instead of 110." },
            new ItemDef { Id = BattleSpin, Name = "Battle Spin", Class = "Tollbreaker", Tier = ItemTier.Common, Value = 0, Tempered = 15,
                Effect = "After a dash, your next Twin Hurl tap hits like a held throw (30) without lodging.", TemperNote = "That hit also deals +15% damage." },
            new ItemDef { Id = CloseQuarters, Name = "Close Quarters", Class = "Tollbreaker", Tier = ItemTier.Common, Value = 12, Tempered = 18,
                Effect = "Twin Hurl hits deal +{v}% while you're within 4 m of the boss." },
            new ItemDef { Id = DeepSplit, Name = "Deep Split", Class = "Tollbreaker", Tier = ItemTier.Rare, Value = 8, Tempered = 9,
                Effect = "Splitting Throw's bonus lasts {v} s instead of 6 (the axe stays lodged to match)." },
            new ItemDef { Id = TwinSplit, Name = "Twin Split", Class = "Tollbreaker", Tier = ItemTier.Rare, Value = 50, Tempered = 75,
                Effect = "With both axes in hand, Splitting Throw throws both; the second lodges beside the first for {v}% damage. The bonus doesn't stack, and both hands are empty until Call of Iron." },
            new ItemDef { Id = QuickGrind, Name = "Quick Grind", Class = "Tollbreaker", Tier = ItemTier.Rare, Value = 0.3f, Tempered = 0.27f,
                Effect = "Grinding Toll hits every {v} s instead of 0.4 s." },
            new ItemDef { Id = RingingReturn, Name = "Ringing Return", Class = "Tollbreaker", Tier = ItemTier.Rare, Value = 2, Tempered = 3,
                Effect = "Each axe Call of Iron sends through the boss takes {v} s off Splitting Throw's cooldown." },
            new ItemDef { Id = Backswing, Name = "Backswing", Class = "Tollbreaker", Tier = ItemTier.Rare, Value = 6, Tempered = 7,
                Effect = "Returning Twin Hurl axes hit for {v} instead of 3." },
            new ItemDef { Id = AnchorLine, Name = "Anchor Line", Class = "Tollbreaker", Tier = ItemTier.Rare, Value = 20, Tempered = 30, Value2 = 12, Tempered2 = 12,
                Effect = "Floor-lodged axes last {w} s instead of 8, and Call of Iron from the floor deals +{v}%." },
            new ItemDef { Id = BerserkersPace, Name = "Berserker's Pace", Class = "Tollbreaker", Tier = ItemTier.Rare, Value = 2, Tempered = 3, Value2 = 10, Tempered2 = 15,
                Effect = "Each Twin Hurl hit thrown from within 4 m adds +{v}% damage for 3 s, up to +{w}%. Lost when you're hit." },
            new ItemDef { Id = WarCry, Name = "War Cry", Class = "Tollbreaker", Tier = ItemTier.Rare, Tags = ItemTag.Party, Value = 10, Tempered = 15,
                Effect = "Bell Frenzy gives allies within 6 m +{v}% damage while it lasts.", SoloNote = "Solo: gives it to you." },
            new ItemDef { Id = FrenziedHour, Name = "Frenzied Hour", Class = "Tollbreaker", Tier = ItemTier.Keystone, Value = 18, Tempered = 15,
                Effect = "Bell Frenzy cooldown 30 -> {v} s, but it lasts 3 s instead of 4 and the fan's side axes deal 35%." },
            // ---- Chimesinger ----
            new ItemDef { Id = BrightNote, Name = "Bright Note", Class = "Chimesinger", Tier = ItemTier.Common, Value = 10, Tempered = 15, Value2 = 15, Tempered2 = 22,
                Effect = "Resonant Note +{v}% damage and +{w}% projectile speed." },
            new ItemDef { Id = WideRefrain, Name = "Wide Refrain", Class = "Chimesinger", Tier = ItemTier.Common, Value = 25, Tempered = 37,
                Effect = "Harmonic Refrain cone +{v}% angle." },
            new ItemDef { Id = LongRefrain, Name = "Long Refrain", Class = "Chimesinger", Tier = ItemTier.Common, Value = 2, Tempered = 3,
                Effect = "Harmonic Refrain range +{v} m." },
            new ItemDef { Id = WideChorus, Name = "Wide Chorus", Class = "Chimesinger", Tier = ItemTier.Common, Value = 30, Tempered = 45,
                Effect = "Mending Chorus radius +{v}%." },
            new ItemDef { Id = LightStep, Name = "Light Step", Class = "Chimesinger", Tier = ItemTier.Common, Value = 24, Tempered = 21,
                Effect = "Harmonic Step costs {v} stamina instead of 30." },
            new ItemDef { Id = GraceNote, Name = "Grace Note", Class = "Chimesinger", Tier = ItemTier.Common, Value = 100, Tempered = 150,
                Effect = "After Harmonic Step, your next Resonant Note deals double damage.", TemperNote = "Tempered: +150% instead." },
            new ItemDef { Id = Chord, Name = "Chord", Class = "Chimesinger", Tier = ItemTier.Rare, Value = 4, Tempered = 3,
                Effect = "Every {o} Resonant Note splits into 3 aimed notes in a narrow spread. They don't home in." },
            new ItemDef { Id = ThirdEcho, Name = "Third Echo", Class = "Chimesinger", Tier = ItemTier.Rare, Value = 3, Tempered = null,
                Effect = "Echoes cap at {v} instead of 2." },
            new ItemDef { Id = Fortissimo, Name = "Fortissimo", Class = "Chimesinger", Tier = ItemTier.Rare, Value = 12, Tempered = 18,
                Effect = "Crescendo deals +{v}% damage for each echo it detonates." },
            new ItemDef { Id = Encore, Name = "Encore", Class = "Chimesinger", Tier = ItemTier.Rare, Value = 1, Tempered = null,
                Effect = "Crescendo leaves {v} echo on the bell." },
            new ItemDef { Id = NoteTrail, Name = "Note Trail", Class = "Chimesinger", Tier = ItemTier.Rare, Value = 3, Tempered = 4,
                Effect = "Harmonic Step ends by firing {v} notes toward the cursor." },
            new ItemDef { Id = Overflow, Name = "Overflow", Class = "Chimesinger", Tier = ItemTier.Rare, Value = 25, Tempered = 37,
                Effect = "Overheal from Mending Chorus adds to your next Crescendo: +1% per 2 HP, up to +{v}%." },
            new ItemDef { Id = SharedRinging, Name = "Shared Ringing", Class = "Chimesinger", Tier = ItemTier.Rare, Tags = ItemTag.Ring, Value = 5, Tempered = 4,
                Effect = "Every {o} ally hit on a ringing target adds an echo (the cap still applies).", SoloNote = "Solo: counts your Resonant Notes." },
            new ItemDef { Id = StruckTogether, Name = "Struck Together", Class = "Chimesinger", Tier = ItemTier.Rare, Tags = ItemTag.Ring, Value = 30, Tempered = 45,
                Effect = "An ally's heavy hit on a ringing target rings the bell once for {v}% of Bellstrike.", SoloNote = "Solo: every 6th Resonant Note does it." },
            new ItemDef { Id = GreatBell, Name = "Great Bell", Class = "Chimesinger", Tier = ItemTier.Keystone, Value = 30, Tempered = 15,
                Effect = "Only one bell can ring, but it's twice as large, echoes cap at 4, and Crescendo's cooldown is +{v}%." },
            new ItemDef { Id = ChoirLeader, Name = "Choir Leader", Class = "Chimesinger", Tier = ItemTier.Keystone, Value = 50, Tempered = 75,
                Effect = "Mending Chorus heals 25% less, but each cast also detonates bells as a {v}%-strength Crescendo." },
            // ---- Arcanist ----
            new ItemDef { Id = QuickCarving, Name = "Quick Carving", Class = "Arcanist", Tier = ItemTier.Common, Value = 20, Tempered = 30,
                Effect = "Disintegrate builds glyphs {v}% faster." },
            new ItemDef { Id = WideBeam, Name = "Wide Beam", Class = "Arcanist", Tier = ItemTier.Common, Value = 30, Tempered = 45,
                Effect = "Disintegrate width +{v}%." },
            new ItemDef { Id = SteadyHand, Name = "Steady Hand", Class = "Arcanist", Tier = ItemTier.Common, Value = 25, Tempered = 37,
                Effect = "Disintegrate turns {v}% faster while you steer it." },
            new ItemDef { Id = SplitMissile, Name = "Split Missile", Class = "Arcanist", Tier = ItemTier.Common, Value = 4, Tempered = null,
                Effect = "A full charge fires {v} missiles instead of 3." },
            new ItemDef { Id = WideSigil, Name = "Wide Sigil", Class = "Arcanist", Tier = ItemTier.Common, Value = 25, Tempered = 37,
                Effect = "Rune Sigil radius +{v}%." },
            new ItemDef { Id = BlinkCharge, Name = "Blink Charge", Class = "Arcanist", Tier = ItemTier.Common, Value = 0, Tempered = 15,
                Effect = "After Blink, your next Magic Missile is instantly fully charged.", TemperNote = "That volley deals +15% damage." },
            new ItemDef { Id = FarBlink, Name = "Far Blink", Class = "Arcanist", Tier = ItemTier.Common, Value = 0.6f, Tempered = 0.9f,
                Effect = "Blink distance +{v} m. No invincibility." },
            new ItemDef { Id = GlassEdge, Name = "Glass Edge", Class = "Arcanist", Tier = ItemTier.Common, Value = 12, Tempered = 18,
                Effect = "+{v}% damage and -8% max HP." },
            new ItemDef { Id = FourthGlyph, Name = "Fourth Glyph", Class = "Arcanist", Tier = ItemTier.Rare, Value = 4, Tempered = null,
                Effect = "Glyphs cap at {v}." },
            new ItemDef { Id = MissileVolley, Name = "Missile Volley", Class = "Arcanist", Tier = ItemTier.Rare, Value = 0, Tempered = 50,
                Effect = "Three quick taps fire a free 4th missile.", TemperNote = "The free missile deals +50% damage." },
            new ItemDef { Id = TwinSigil, Name = "Twin Sigil", Class = "Arcanist", Tier = ItemTier.Rare, Value = 2, Tempered = null,
                Effect = "A Rune Sigil cast with a glyph places {v} sigils in a short line." },
            new ItemDef { Id = HastyComet, Name = "Hasty Comet", Class = "Arcanist", Tier = ItemTier.Rare, Value = -15, Tempered = -7.5f,
                Effect = "Arcane Comet delay 3 -> 2.2 s, {v}% damage." },
            new ItemDef { Id = GreatComet, Name = "Great Comet", Class = "Arcanist", Tier = ItemTier.Rare, Value = 40, Tempered = 60,
                Effect = "Arcane Comet delay 3 -> 3.8 s, +{v}% damage and a larger impact." },
            new ItemDef { Id = Starfall, Name = "Starfall", Class = "Arcanist", Tier = ItemTier.Rare, Value = 40, Tempered = 50,
                Effect = "Arcane Comet lands as 3 impacts in a line along your aim, each {v}% of a Comet." },
            new ItemDef { Id = SurgeEcho, Name = "Surge Echo", Class = "Arcanist", Tier = ItemTier.Rare, Value = 1, Tempered = null,
                Effect = "When Arcane Surge ends, you get {v} glyph back." },
            new ItemDef { Id = GlyphBrand, Name = "Glyph Brand", Class = "Arcanist", Tier = ItemTier.Rare, Tags = ItemTag.Glyph, Value = 8, Tempered = 6,
                Effect = "Arcane Comet brands the boss for 5 s. Every {v} ally hits on it carve you 1 glyph.", SoloNote = "Solo: counts your missiles." },
            new ItemDef { Id = ResonantRune, Name = "Resonant Rune", Class = "Arcanist", Tier = ItemTier.Rare, Tags = ItemTag.Ring, Value = 1, Tempered = null,
                PartnerClass = "Chimesinger", OfferedSolo = false,
                Effect = "A Rune Sigil going off on a ringing target adds an echo to the bell. Offered only with a Chimesinger in the party." },
            // ---- general ----
            new ItemDef { Id = LongLungs, Name = "Long Lungs", Tier = ItemTier.Common, Value = 15, Tempered = 22,
                Effect = "+{v} max stamina." },
            new ItemDef { Id = SureFooting, Name = "Sure Footing", Tier = ItemTier.Common, Value = 30, Tempered = null,
                Effect = "Knockback taken -{v}%. The stun on hit stays." },
            new ItemDef { Id = Formation, Name = "Formation", Tier = ItemTier.Rare, Tags = ItemTag.Party, Value = 6, Tempered = 9, Value2 = 18, Tempered2 = 27,
                Effect = "+{v}% damage for each ally within 5 m, up to +{w}%.", SoloNote = "Solo: a flat +6% (+9% tempered)." },
            new ItemDef { Id = FourthChoice, Name = "Fourth Choice", Tier = ItemTier.Rare, Value = 7, Tempered = null,
                Effect = "Your shop stock has {v} cards instead of 6 for the rest of the run (one more class card)." },
            new ItemDef { Id = LightFeet, Name = "Light Feet", Tier = ItemTier.Common, Value = 10, Tempered = 15,
                Effect = "+{v}% move speed for 1 s after a dash." },
        };

        static Dictionary<string, ItemDef> byId;
        public static ItemDef Get(string id)
        {
            if (byId == null) byId = All.ToDictionary(i => i.Id);
            return id != null && byId.TryGetValue(id, out var d) ? d : null;
        }
    }
}
