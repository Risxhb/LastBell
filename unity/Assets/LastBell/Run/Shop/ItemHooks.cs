using UnityEngine;

namespace LastBell.Run.Shop
{
    // Every item effect, in one place. Shared code (CombatWorld, PlayerController, the three kits) calls these with its
    // own number and gets it back unchanged when the player owns no matching item, or when no shop run exists
    // (practice, automated suites without -lastbellShop). Nothing here grants iframes, immunity, revives, lifesteal or
    // potion refills (design rules §2, §3; shop spec §8).
    public static class ItemHooks
    {
        public static ShopRun Run;   // the current run's shop state (RunShopFlow sets it; null = no items)

        const int BuffTicks = 240;   // Cracked Armour and Chorus of Valour: 4 s
        const float LoneBellRange = 8f;

        static PlayerBuild B(PlayerController p) => p != null ? p.Build : null;

        // The kit that owns an ability name (names come from the kits' DamageBoss / StrikeBoss calls). Co-op with two
        // players of one class needs a damage source on DamageBoss; until then the first player of that class owns it.
        static string ClassOf(string ability)
        {
            if (ability == null) return null;
            if (ability.StartsWith("SteelRhythm") || ability.StartsWith("SunderingBlow") || ability.StartsWith("LastWord")) return "Vanguard";
            if (ability.StartsWith("ResonantNote") || ability.StartsWith("Bellstrike") || ability.StartsWith("HarmonicRefrain") || ability.StartsWith("Crescendo")) return "Chimesinger";
            if (ability.StartsWith("Magic Missile") || ability.StartsWith("Disintegrate") || ability.StartsWith("Rune Sigil") || ability.StartsWith("Arcane Comet")) return "Arcanist";
            return null;
        }
        static PlayerController Owner(CombatWorld w, string ability)
        {
            var cls = ClassOf(ability); if (cls == null) return null;
            foreach (var p in w.Players) if (p.ClassName == cls) return p;
            return null;
        }
        static bool Heavy(string a) => a.StartsWith("LastWord") || a.StartsWith("Bellstrike") || a.StartsWith("Crescendo") || a.StartsWith("Rune Sigil") || a.StartsWith("Arcane Comet");

        // ---------------- damage (CombatWorld.DamageBoss) ----------------
        public static int Outgoing(CombatWorld w, int damage, string ability)
        {
            if (Run == null || damage <= 0) return damage;
            var p = Owner(w, ability); var b = B(p);
            float scale = 1, pct = 0;
            if (b != null)
            {
                if (ability == "SteelRhythm3") pct += b.V(ItemCatalog.TemperedEdge);
                if (ability == "Bellstrike") pct += b.V(ItemCatalog.HeavyBell);
                // runes cast without a glyph were computed at the kit's 35%; Unbound Cast / Perpetual Beam raise that share
                if (ability.EndsWith("(no glyph)"))
                {
                    float share = Mathf.Max(b.V(ItemCatalog.UnboundCast), b.V(ItemCatalog.PerpetualBeam));
                    if (share > 0) scale = share / 35f;
                }
                if (b.Has(ItemCatalog.LoneBell) && !AllyNear(w, p, LoneBellRange)) pct += b.V(ItemCatalog.LoneBell);
                if (b.ValourUntil > w.Tick) pct += b.ValourPct;
            }
            // Cracked Armour: everyone's heavy hits; solo, only the Vanguard's own Last Word
            if (Run.CrackUntil > w.Tick && Heavy(ability) && (!Run.Solo || ability.StartsWith("LastWord"))) pct += Run.CrackPct;
            if (scale == 1 && pct == 0) return damage;
            return Mathf.Max(1, Mathf.RoundToInt(damage * scale * (1 + pct / 100f)));
        }

        public static void Dealt(CombatWorld w, string ability, int dealt)
        {
            if (Run == null || dealt <= 0 || !ability.StartsWith("SunderingBlow")) return;
            var b = B(Owner(w, ability));
            if (b != null && b.Has(ItemCatalog.CrackedArmour)) { Run.CrackUntil = w.Tick + BuffTicks; Run.CrackPct = b.V(ItemCatalog.CrackedArmour); }
        }

        static bool AllyNear(CombatWorld w, PlayerController p, float range)
        {
            foreach (var o in w.Players)
                if (o != p && !o.Down && Sim.Flat(o.transform.position - p.transform.position).magnitude <= range) return true;
            return false;
        }

        // ---------------- kits ----------------
        // ability cooldowns, in ticks (Countdown: Last Word; Swift Chorus: Mending Chorus)
        public static int Cooldown(PlayerController p, string ability, int ticks)
        {
            var b = B(p); if (b == null) return ticks;
            float cut = ability == "LastWord" ? b.V(ItemCatalog.Countdown) : ability == "MendingChorus" ? b.V(ItemCatalog.SwiftChorus) : 0;
            return cut > 0 ? Mathf.RoundToInt(ticks * (1 - cut / 100f)) : ticks;
        }
        // Oathbound: Sundering Blow can't be empowered, and every Nth Opening resets Last Word (returns true to reset)
        public static bool SunderEmpowerable(PlayerController p) => !(B(p)?.Has(ItemCatalog.Oathbound) ?? false);
        public static bool OnOpening(PlayerController p)
        {
            var b = B(p); if (b == null || !b.Has(ItemCatalog.Oathbound)) return false;
            b.Openings++;
            return b.Openings % Mathf.Max(1, Mathf.RoundToInt(b.V(ItemCatalog.Oathbound))) == 0;
        }
        // Chorus of Valour: every ally the caster's Mending Chorus touches (the caster too; solo that's the fallback)
        public static void ChorusTouched(PlayerController caster, PlayerController ally)
        {
            var b = B(caster); var a = B(ally);
            if (b == null || a == null || !b.Has(ItemCatalog.ChorusOfValour)) return;
            a.ValourUntil = caster.World.Tick + BuffTicks; a.ValourPct = Mathf.Max(a.ValourPct, b.V(ItemCatalog.ChorusOfValour));
        }
        public static int SurgePerGlyph(PlayerController p, int ticks) { var b = B(p); return b != null && b.Has(ItemCatalog.LongSurge) ? Sim.Ticks(b.V(ItemCatalog.LongSurge)) : ticks; }
        public static int GlyphCap(PlayerController p, int cap) { var b = B(p); return b != null && b.Has(ItemCatalog.PerpetualBeam) ? Mathf.Min(cap, 2) : cap; }

        // ---------------- general (PlayerController) ----------------
        public static int PotionHeal(PlayerController p, int heal) { var b = B(p); return b != null && b.Has(ItemCatalog.StrongBrew) ? Mathf.RoundToInt(b.V(ItemCatalog.StrongBrew)) : heal; }
        public static int ExtraPotions(PlayerController p) { var b = B(p); return b != null ? Mathf.RoundToInt(b.V(ItemCatalog.SpareFlask)) : 0; }
        public static float SprintCost(PlayerController p, float cost) { var b = B(p); return b != null && b.Has(ItemCatalog.Sprinter) ? b.V(ItemCatalog.Sprinter) : cost; }
        public static float RegenDelay(PlayerController p, float delay) { var b = B(p); return b != null && b.Has(ItemCatalog.DeepBreath) ? b.V(ItemCatalog.DeepBreath) : delay; }
    }
}
