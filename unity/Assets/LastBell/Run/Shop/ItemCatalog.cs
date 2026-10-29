using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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

    // The 70-item pool (docs/design/items/CATALOGUE.md): slice 1 (15 items, run-shop-plan §3) plus the 55 accepted on
    // 2026-10-28. Prices are Chzy18's starting prices (spec §12.2). Held items are defined but never stocked.
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
        // full pool
        public const string QuickHands = "v.quick_hands", WideArc = "v.wide_arc", LongReach = "v.long_reach", Grit = "v.grit", ClockworkStep = "v.clockwork_step",
            SteadyStance = "v.steady_stance", Momentum = "v.momentum", DoubleOpening = "v.double_opening", PendulumCut = "v.pendulum_cut", HeavyHour = "v.heavy_hour",
            QuickVerdict = "v.quick_verdict", IronEcho = "v.iron_echo", SecondStep = "v.second_step", RhythmKeeper = "v.rhythm_keeper", Rally = "v.rally", TollOfSteel = "v.toll_of_steel";
        public const string BrightNote = "c.bright_note", WideRefrain = "c.wide_refrain", LongRefrain = "c.long_refrain", WideChorus = "c.wide_chorus", LightStep = "c.light_step",
            GraceNote = "c.grace_note", Chord = "c.chord", TwinBells = "c.twin_bells", ThirdEcho = "c.third_echo", Fortissimo = "c.fortissimo", Encore = "c.encore",
            NoteTrail = "c.note_trail", Overflow = "c.overflow", SharedRinging = "c.shared_ringing", StruckTogether = "c.struck_together", GreatBell = "c.great_bell", ChoirLeader = "c.choir_leader";
        public const string QuickCarving = "a.quick_carving", WideBeam = "a.wide_beam", SteadyHand = "a.steady_hand", SplitMissile = "a.split_missile", WideSigil = "a.wide_sigil",
            BlinkCharge = "a.blink_charge", FarBlink = "a.far_blink", GlassEdge = "a.glass_edge", FourthGlyph = "a.fourth_glyph", MissileVolley = "a.missile_volley", TwinSigil = "a.twin_sigil",
            HastyComet = "a.hasty_comet", GreatComet = "a.great_comet", Starfall = "a.starfall", SurgeEcho = "a.surge_echo", GlyphBrand = "a.glyph_brand", ResonantRune = "a.resonant_rune";
        public const string LongLungs = "g.long_lungs", SureFooting = "g.sure_footing", Formation = "g.formation", FourthChoice = "g.fourth_choice", LightFeet = "g.light_feet";

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
                Effect = "Sundering Blow can't be empowered. Every {v} Openings you earn reset Last Word's cooldown." },
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
            // ---- Vanguard ----
            new ItemDef { Id = QuickHands, Name = "Quick Hands", Class = "Vanguard", Tier = ItemTier.Common, Value = 0.3f, Tempered = 0.45f,
                Effect = "The combo window between Steel Rhythm hits is {v} s longer, so you can step between hits." },
            new ItemDef { Id = WideArc, Name = "Wide Arc", Class = "Vanguard", Tier = ItemTier.Common, Value = 30, Tempered = 45,
                Effect = "Steel Rhythm hit 3 cleaves a {v}% wider arc." },
            new ItemDef { Id = LongReach, Name = "Long Reach", Class = "Vanguard", Tier = ItemTier.Common, Value = 1.5f, Tempered = 2.25f,
                Effect = "Sundering Blow range +{v} m." },
            new ItemDef { Id = Grit, Name = "Grit", Class = "Vanguard", Tier = ItemTier.Common, Value = 8, Tempered = 12,
                Effect = "+{v}% max HP (counts toward the +20% cap)." },
            new ItemDef { Id = ClockworkStep, Name = "Clockwork Step", Class = "Vanguard", Tier = ItemTier.Common, Value = 3.6f, Tempered = 3.9f,
                Effect = "Clockstep distance 3 -> {v} m. No invincibility." },
            new ItemDef { Id = SteadyStance, Name = "Steady Stance", Class = "Vanguard", Tier = ItemTier.Common, Value = 50, Tempered = 75,
                Effect = "Brace's movement slow is cut by {v}%. Mitigation is unchanged." },
            new ItemDef { Id = Momentum, Name = "Momentum", Class = "Vanguard", Tier = ItemTier.Common, Value = 0, Tempered = 15,
                Effect = "After Clockstep, your next Steel Rhythm hit counts as hit 3.", TemperNote = "That hit also deals +15% damage." },
            new ItemDef { Id = DoubleOpening, Name = "Double Opening", Class = "Vanguard", Tier = ItemTier.Rare, Value = 0, Tempered = 15,
                Effect = "Opening stacks to 2. Sundering Blow spends both for a wider split impact.", TemperNote = "A double-spent Sundering Blow deals +15% damage." },
            new ItemDef { Id = PendulumCut, Name = "Pendulum Cut", Class = "Vanguard", Tier = ItemTier.Rare, Value = 1, Tempered = 1.5f,
                Effect = "Sundering Blow's line keeps cutting for {v} s." },
            new ItemDef { Id = HeavyHour, Name = "Heavy Hour", Class = "Vanguard", Tier = ItemTier.Rare, Value = 45, Tempered = 67,
                Effect = "Last Word windup 0.8 -> 1.1 s, +{v}% damage and a wider fan." },
            new ItemDef { Id = QuickVerdict, Name = "Quick Verdict", Class = "Vanguard", Tier = ItemTier.Rare, Value = -20, Tempered = -10,
                Effect = "Last Word windup 0.8 -> 0.5 s, {v}% damage." },
            new ItemDef { Id = IronEcho, Name = "Iron Echo", Class = "Vanguard", Tier = ItemTier.Rare, Value = 50, Tempered = 75,
                Effect = "Last Word's fan repeats 0.6 s later for {v}% damage." },
            new ItemDef { Id = SecondStep, Name = "Second Step", Class = "Vanguard", Tier = ItemTier.Rare, Value = 45, Tempered = 38, Held = true,
                Effect = "Clockstep can be used twice within 1.5 s. The second one costs {v} stamina. No invincibility." },
            new ItemDef { Id = RhythmKeeper, Name = "Rhythm Keeper", Class = "Vanguard", Tier = ItemTier.Rare, Value = 10, Tempered = 15,
                Effect = "Three full Steel Rhythm combos in a row without being hit: +{v}% damage until you're hit." },
            new ItemDef { Id = Rally, Name = "Rally", Class = "Vanguard", Tier = ItemTier.Rare, Tags = ItemTag.Party, Value = 10, Tempered = 15,
                Effect = "A Last Word hit gives allies within 6 m +{v}% damage for 4 s.", SoloNote = "Solo: gives it to you." },
            new ItemDef { Id = TollOfSteel, Name = "Toll of Steel", Class = "Vanguard", Tier = ItemTier.Keystone, Value = 40, Tempered = 60,
                Effect = "Steel Rhythm becomes 4 hits, and hit 4 sends a small Last Word fan ({v}%). Opening now comes from hit 4, not hit 3." },
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
            new ItemDef { Id = TwinBells, Name = "Twin Bells", Class = "Chimesinger", Tier = ItemTier.Rare, Value = 2, Tempered = null, Held = true,
                Effect = "Two bells can ring at once, for example on the boss and on a part it spawned." },
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
