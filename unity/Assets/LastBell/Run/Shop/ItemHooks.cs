using System.Collections.Generic;
using UnityEngine;
using static LastBell.Run.Shop.ItemCatalog;

namespace LastBell.Run.Shop
{
    // Every item effect, in one place. Shared code (CombatWorld, PlayerController, the three kits) calls these with its
    // own number and gets it back unchanged when the player owns no matching item, or when no shop run exists
    // (practice of a miniboss, automated suites without -lastbellShop). Nothing here grants iframes, immunity, revives,
    // lifesteal or potion refills (design rules §2, §3; shop spec §8). Item-made hits reuse the kits' own looks.
    public static class ItemHooks
    {
        public static ShopRun Run;   // the current run's shop state (RunShopFlow sets it; null = no items)

        const int BuffTicks = 240;   // Riven Iron, Chorus of Valour: 4 s
        const int PaceTicks = 180;   // Berserker's Pace: 3 s
        const float LoneBellRange = 8f, FormationRange = 5f, WarCryRange = 6f, CloseRange = 4f;
        const int MaxHpCapPct = 20;  // shop spec §8: items give at most +20% max HP
        const float KnockbackCapPct = 30;   // shop spec §8: knockback reduction capped at -30%
        const float DashCost = 30, SprintBase = 25, ShortSplitShare = 4f / 6f;   // Leaden Step, Steady Breath, Short Split
        public const int PatientGap = 120;  // Patient Bell: 2 s without a hit on the boss
        public const int LingerTicks = 120; // Lingering Rune: the rune stays 2 s more

        static PlayerBuild B(PlayerController p) => p != null ? p.Build : null;
        static int Tick(PlayerController p) => p.World.Tick;

        // The kit that owns an ability name (names come from the kits' DamageBoss / StrikeBoss calls). Co-op with two
        // players of one class needs a damage source on DamageBoss; until then the first player of that class owns it.
        static string ClassOf(string ability)
        {
            if (ability == null) return null;
            if (ability.StartsWith("Twin Hurl") || ability.StartsWith("Grinding Toll") || ability.StartsWith("Splitting Throw") || ability.StartsWith("Call of Iron")) return "Tollbreaker";
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
        static bool Heavy(string a) => a.StartsWith("Splitting Throw") || a == "Twin Hurl (held)" || a.StartsWith("Bellstrike") || a.StartsWith("Crescendo") || a.StartsWith("Rune Sigil") || a.StartsWith("Arcane Comet");

        // ---------------- numbers ----------------
        // A shared number for one player. Keys name the number at its point of use; the default is the caller's own.
        public static int I(PlayerController p, string key, int v) => Mathf.RoundToInt(F(p, key, v));
        public static float F(PlayerController p, string key, float v)
        {
            var b = B(p); if (b == null) return v;
            switch (key)
            {
                // controller (every class)
                case "dash.distance": return v + b.V(FarBlink);
                case "dash.cost": return (b.Has(LightStep) ? b.V(LightStep) : v) + (b.Has(LeadenStep) ? b.V(LeadenStep) - DashCost : 0);   // Leaden Step adds on top
                case "stamina.max": return v + b.V(LongLungs);
                case "stamina.regen": return v * (1 + b.V(SteadyBreath) / 100f);
                case "knockback": return v * (1 - Mathf.Min(KnockbackCapPct, b.V(SureFooting)) / 100f);
                case "move.speed": return b.L.LightFeetUntil > Tick(p) ? v * (1 + b.V(LightFeet) / 100f) : v;
                // Tollbreaker (TollbreakerKit reads these "t.*" keys)
                case "t.split.cd": return v * (1 - b.V(ShortFuse) / 100f);
                case "t.toll.radius": return v * (1 + b.V(WideGrind) / 100f);
                case "t.toll.spin": return b.Has(LongGrind) ? Sim.Ticks(b.V(LongGrind)) : v;
                case "t.toll.every": return b.Has(QuickGrind) ? Sim.Ticks(b.V(QuickGrind)) : v;
                case "t.return.speed": return v * (1 + b.V(QuickCatch) / 100f);
                case "t.tap.back": return b.Has(Backswing) ? b.V(Backswing) : v;
                case "t.call.per_m": return v + b.V(IronPull);
                case "t.call.max": return b.Has(IronPull) ? b.V2(IronPull) : v;
                case "t.call.cd": return b.Has(RootedAxe) ? Sim.Ticks(9) : v;
                // Short Split: the split and its boss lodge last 4 s instead of 6 (two thirds of whatever Deep Split makes them)
                case "t.split.ticks": return (b.Has(DeepSplit) ? Sim.Ticks(b.V(DeepSplit)) : v) * (b.Has(ShortSplit) ? ShortSplitShare : 1);
                case "t.lodge.boss": return (b.Has(DeepSplit) ? Mathf.Max(v, Sim.Ticks(b.V(DeepSplit))) : v) * (b.Has(ShortSplit) ? ShortSplitShare : 1);
                case "t.held.range": return b.Has(LongToss) ? b.V2(LongToss) : v;
                case "t.held.damage": return b.Has(LongToss) ? b.V(LongToss) : v;
                case "t.lodge.floor": return b.Has(AnchorLine) ? Sim.Ticks(b.V2(AnchorLine)) : v;
                case "t.frenzy.cd": return b.Has(FrenziedHour) ? Sim.Ticks(b.V(FrenziedHour)) : v;
                case "t.frenzy.ticks": return b.Has(FrenziedHour) ? Sim.Ticks(3) : v;
                case "t.fan.side": return b.Has(FrenziedHour) ? 0.35f : v;
                case "t.split.bonus": return (b.Has(RootedAxe) ? 1 + b.V(RootedAxe) / 100f : v) + (b.Has(ShortSplit) ? (b.V(ShortSplit) - 40) / 100f : 0);
                case "t.twin_split": return b.Has(TwinSplit) ? b.V(TwinSplit) : v;
                case "t.rooted": return b.Has(RootedAxe) ? 1 : v;
                case "t.battle_spin": return b.Has(BattleSpin) ? 1 : v;
                // Chimesinger
                case "c.note.speed": return v * (1 + b.V2(BrightNote) / 100f) * (1 - b.V2(LowNote) / 100f);
                case "c.strike.recharge": return v * (1 + b.V2(DoubleToll) / 100f);
                case "c.double_toll": return b.V(DoubleToll) / 100f;
                case "c.refrain.half": return v * (1 + b.V(WideRefrain) / 100f);
                case "c.refrain.range": return v + b.V(LongRefrain);
                case "c.chorus.radius": return v * (1 + b.V(WideChorus) / 100f);
                case "c.chorus.heal": return b.Has(ChoirLeader) ? v * 0.75f : v;
                case "c.choir": return b.Has(ChoirLeader) ? b.V(ChoirLeader) / 100f : v;
                case "c.echo.cap": return b.Has(GreatBell) ? 4 : b.Has(ThirdEcho) ? b.V(ThirdEcho) : v;
                case "c.cresc.cd": return b.Has(GreatBell) ? v * (1 + b.V(GreatBell) / 100f) : v;
                case "c.encore": return b.Has(Encore) ? b.V(Encore) : v;
                // Arcanist
                case "a.dis.step": return v / (1 + b.V(QuickCarving) / 100f);
                case "a.dis.width": return v * (1 + b.V(WideBeam) / 100f);
                case "a.dis.turn": return v * (1 + b.V(SteadyHand) / 100f);
                case "a.missile.cap": return b.Has(SplitMissile) ? b.V(SplitMissile) : v;
                case "a.blink.ready": return b.L.BlinkReady ? 1 : v;
                case "a.sigil.radius": return v * (1 + b.V(WideSigil) / 100f);
                case "a.sigil.count": return b.Has(TwinSigil) ? b.V(TwinSigil) : v;
                case "a.comet.fall": { float r = 1; if (b.Has(HastyComet)) r *= 2.2f / 3f; if (b.Has(GreatComet)) r *= 3.8f / 3f; return v * r; }
                case "a.comet.radius": return b.Has(GreatComet) ? v * 1.25f : v;
                case "a.starfall": return b.Has(Starfall) ? b.V(Starfall) / 100f : v;
                case "a.surge_end_glyphs": return v + b.V(SurgeEcho);
                case "a.dis.carve": return b.Has(Overchannel) ? b.V2(Overchannel) : v;
                case "a.glyph.fade": return b.Has(Overchannel) ? Sim.Ticks(b.V(Overchannel)) : v;   // 0 = glyphs never fade
                case "a.sigil.arm": return b.Has(LingeringRune) ? v * 1.5f : v;                    // 0.8 -> 1.2 s (Surge's 0.4 -> 0.6 s)
                case "a.sigil.linger": return b.V(LingeringRune) / 100f;
            }
            return v;
        }

        public static int MaxHp(PlayerController p, int baseHp)
        {
            var b = B(p); if (b == null) return baseHp;
            float pct = -(b.Has(GlassEdge) ? 8 : 0);   // no Tollbreaker item gives max HP (catalogue rule check)
            return Mathf.RoundToInt(baseHp * (1 + pct / 100f));
        }

        // ---------------- damage (CombatWorld.DamageBoss) ----------------
        public static int Outgoing(CombatWorld w, int damage, string ability)
        {
            if (Run == null || damage <= 0 || ability == null) return damage;
            var p = Owner(w, ability); var b = B(p);
            float scale = 1, pct = 0;
            if (b != null)
            {
                var L = b.L; int now = w.Tick;
                if (p.Kit is TollbreakerKit tk)
                {
                    var h = tk.Hitting; bool hurl = ability.StartsWith("Twin Hurl") && ability != "Twin Hurl (fan)";   // the fan's projectiles carry no hit context
                    if (ability == "Twin Hurl" && !h.Return) pct += b.V(KeenEdge);
                    if (ability == "Twin Hurl (held)") pct += b.V(HeavyHaft);
                    if (ability == "Twin Hurl (battle spin)") pct += b.V(BattleSpin);
                    if (hurl && h.ReleaseDistance <= CloseRange) pct += b.V(CloseQuarters);
                    if (ability == "Call of Iron" && h.FromFloor) pct += b.V(AnchorLine);
                    if (L.RivenUntil > now && (ability == "Call of Iron" || ability.StartsWith("Splitting Throw"))) pct += b.V(RivenIron);
                    if (hurl && b.Has(BerserkersPace) && h.ReleaseDistance <= CloseRange)
                    {
                        if (L.PaceUntil <= now) L.PaceStacks = 0;   // the stacks fall off 3 s after the last close hit
                        L.PaceNext = true;                       // Dealt adds the stack once the hit lands
                    }
                    if (L.PaceUntil > now) pct += Mathf.Min(b.V2(BerserkersPace), b.V(BerserkersPace) * L.PaceStacks);
                }
                if (ability == "Bellstrike") pct += b.V(HeavyBell);
                if (ability.StartsWith("ResonantNote")) pct += b.V(BrightNote) + b.V(LowNote);
                if (ability.StartsWith("Arcane Comet")) pct += b.V(HastyComet) + b.V(GreatComet);
                if (ability == "Magic Missile (charged)" && L.VolleyBoostUntil > now) pct += b.V(BlinkCharge);
                if (ability == "Magic Missile (volley)") pct += b.V(MissileVolley);
                if (p.ClassName == "Arcanist") pct += b.V(GlassEdge);
                // runes cast without a glyph were computed at the kit's 35%; Unbound Cast / Perpetual Beam raise that share
                if (ability.EndsWith("(no glyph)"))
                {
                    float share = Mathf.Max(b.V(UnboundCast), b.V(PerpetualBeam));
                    if (share > 0) scale = share / 35f;
                }
                if (b.Has(LoneBell) && AlliesNear(w, p, LoneBellRange) == 0) pct += b.V(LoneBell);
                if (b.Has(Formation)) pct += Run.Solo ? b.V(Formation) : Mathf.Min(b.V2(Formation), b.V(Formation) * AlliesNear(w, p, FormationRange));
                pct += b.V2(LeadenStep);
                // Patient Bell: the first hit after a 2 s gap is the big one; every hit restarts the gap
                if (b.Has(PatientBell)) { pct += now - L.LastBossHit >= PatientGap ? b.V(PatientBell) : -b.V2(PatientBell); L.LastBossHit = now; }
                if (b.Has(LateBloom) && w.Boss != null) pct += w.Boss.PhaseNumber <= 1 ? -b.V2(LateBloom) : b.V(LateBloom);
                if (L.ValourUntil > now) pct += L.ValourPct;
                if (L.CryUntil > now) pct += L.CryPct;
            }
            if (scale == 1 && pct == 0) return damage;
            return Mathf.Max(1, Mathf.RoundToInt(damage * scale * (1 + pct / 100f)));
        }

        static bool inDealt;   // item hits made from Dealt don't trigger Dealt again
        public static void Dealt(CombatWorld w, string ability, int dealt)
        {
            if (Run == null || dealt <= 0 || ability == null || inDealt) return;
            inDealt = true;
            try
            {
                var owner = Owner(w, ability); var b = B(owner); int now = w.Tick;
                if (b != null)
                {
                    // Berserker's Pace: a close Twin Hurl hit landed (Outgoing marked it); the 3 s restart with each stack
                    if (b.L.PaceNext) { b.L.PaceNext = false; b.L.PaceStacks++; b.L.PaceUntil = now + PaceTicks; }
                    if (ability.StartsWith("Arcane Comet") && b.Has(GlyphBrand)) { Run.BrandUntil = now + 300; Run.BrandOwner = owner; Run.BrandHits = 0; }
                    if (ability.StartsWith("Rune Sigil") && b.Has(ResonantRune))
                        foreach (var c in w.Players) if (c.Kit is ChimesingerKit ck && ck.Bell) ck.ItemAddEcho();
                }
                // ringing-bell items on every Chimesinger
                foreach (var c in w.Players)
                {
                    if (!(c.Kit is ChimesingerKit ck) || !ck.Bell || c.Build == null) continue;
                    var cb = c.Build; bool ally = owner != null && owner != c, ownNote = owner == c && ability.StartsWith("ResonantNote");
                    if (cb.Has(SharedRinging) && (Run.Solo ? ownNote : ally) && ++cb.L.RingHits % Mathf.Max(1, Mathf.RoundToInt(cb.V(SharedRinging))) == 0) ck.ItemAddEcho();
                    if (cb.Has(StruckTogether) && (Run.Solo ? ownNote && ++cb.L.StruckNotes % 6 == 0 : ally && Heavy(ability)) && w.Boss != null)
                        w.DamageBoss(Mathf.RoundToInt(ChimesingerKit.BaseStrikeDamage * cb.V(StruckTogether) / 100f), w.Boss.transform.position + Vector3.up * w.Boss.HitHeight, "Bellstrike (struck together)");
                }
                // Glyph Brand: hits on the branded boss carve the Arcanist a glyph (solo: his own missiles)
                var bo = Run.BrandOwner;
                if (Run.BrandUntil > now && bo != null && bo.Kit is ArcanistKit ak && bo.Build != null)
                {
                    bool counts = Run.Solo ? owner == bo && ability.StartsWith("Magic Missile") : owner != null && owner != bo;
                    if (counts && ++Run.BrandHits % Mathf.Max(1, Mathf.RoundToInt(bo.Build.V(GlyphBrand))) == 0) ak.ItemGainGlyph();
                }
            }
            finally { inDealt = false; }
        }

        static int AlliesNear(CombatWorld w, PlayerController p, float range)
        {
            int n = 0;
            foreach (var o in w.Players)
                if (o != p && !o.Down && Sim.Flat(o.transform.position - p.transform.position).magnitude <= range) n++;
            return n;
        }

        // ---------------- events ----------------
        public static void OnDash(PlayerController p)
        {
            var b = B(p); if (b == null) return;
            b.L.GraceReady = b.Has(GraceNote); b.L.BlinkReady = b.Has(BlinkCharge); b.L.LastDashAt = Tick(p);
        }
        public static void OnDashEnd(PlayerController p)
        {
            var b = B(p); if (b == null) return;
            if (b.Has(LightFeet)) b.L.LightFeetUntil = Tick(p) + Sim.Hz;
            if (b.Has(NoteTrail))
            {
                var dir = Sim.Flat(p.Last.Aim - p.transform.position); dir = dir.sqrMagnitude > 0.1f ? dir.normalized : p.transform.forward;
                int n = Mathf.RoundToInt(b.V(NoteTrail));
                for (int i = 0; i < n; i++) FireNote(p, Quaternion.Euler(0, (i - (n - 1) / 2f) * 6f, 0) * dir, ChimesingerKit.BaseNoteDamage, ChimesingerKit.BaseNoteSpeed, "ResonantNote (trail)");
            }
        }
        public static void OnPlayerHit(PlayerController p)
        {
            var b = B(p); if (b == null) return;
            b.L.PaceStacks = 0; b.L.PaceUntil = -1; b.L.PaceNext = false;   // Berserker's Pace is lost when you're hit
        }

        // Chimesinger: a Resonant Note leaving the staff. Returns its damage (Grace Note) and fires Chord's side notes.
        public static int Note(PlayerController p, Vector3 dir, int damage, float speed)
        {
            var b = B(p); if (b == null) return damage;
            int dmg = damage;
            if (b.L.GraceReady) { b.L.GraceReady = false; dmg = Mathf.RoundToInt(damage * (1 + b.V(GraceNote) / 100f)); }
            if (b.Has(Chord) && ++b.L.Notes % Mathf.Max(1, Mathf.RoundToInt(b.V(Chord))) == 0)
                for (int s = -1; s <= 1; s += 2) FireNote(p, Quaternion.Euler(0, s * 7f, 0) * dir, damage, speed, "ResonantNote (chord)");
            return dmg;
        }
        static void FireNote(PlayerController p, Vector3 dir, int damage, float speed, string ability)
        {
            var start = p.transform.position + dir * 0.8f + Vector3.up * 1.3f;
            p.World.Fx.NoteCast(start, dir);
            p.World.Projectiles.Fire(new ProjectileSystem.Projectile { Pos = start, Vel = dir * speed, Range = 15f, Radius = 0.35f, Damage = damage, Ability = ability, Color = PlayerFx.NoteGold });
        }

        // Chimesinger: Crescendo damage before it lands (Fortissimo per echo, Overflow's stored bonus)
        public static int Crescendo(PlayerController p, int damage, int echoes)
        {
            var b = B(p); if (b == null) return damage;
            float pct = b.V(Fortissimo) * echoes + b.L.Overflow; b.L.Overflow = 0;
            return pct > 0 ? Mathf.RoundToInt(damage * (1 + pct / 100f)) : damage;
        }

        // Arcanist: a tap missile fired; true when Missile Volley's third quick tap earns a free missile
        public static bool Tap(PlayerController p)
        {
            var b = B(p); if (b == null || !b.Has(MissileVolley)) return false;
            int now = Tick(p); var q = b.L.Taps;
            q.Add(now); q.RemoveAll(t => now - t > Sim.Hz);   // "quick": three taps within 1 s
            if (q.Count < 3) return false;
            q.Clear(); return true;
        }

        // A kit event (Blink Charge's volley; the Tollbreaker's recall hit and Bell Frenzy). a/bw are the kit's own numbers
        // for it: the recall hit's damage and 1 when it came from the floor, or Bell Frenzy's length in ticks.
        public static void Cast(PlayerController p, string key, Vector3 origin, Vector3 dir, float a, float bw)
        {
            var b = B(p); if (b == null) return;
            switch (key)
            {
                case "t.recall.hit":   // an axe Call of Iron sent through the boss
                    if (b.Has(RingingReturn) && p.Kit is TollbreakerKit tk) tk.ReduceCooldown(2, Sim.Ticks(b.V(RingingReturn)));
                    if (b.Has(RivenIron)) b.L.RivenUntil = Tick(p) + BuffTicks;   // personal: only his own hits read it
                    break;
                case "t.frenzy":   // War Cry: allies within 6 m for Bell Frenzy's length (solo: himself)
                    if (b.Has(WarCry))
                        foreach (var o in p.World.Players)
                            if ((o != p || Run.Solo) && !o.Down && o.Build != null && Sim.Flat(o.transform.position - p.transform.position).magnitude <= WarCryRange)
                            { o.Build.L.CryUntil = Tick(p) + Mathf.RoundToInt(a); o.Build.L.CryPct = Mathf.Max(o.Build.L.CryPct, b.V(WarCry)); }
                    break;
                case "a.volley":
                    if (b.L.BlinkReady) { b.L.BlinkReady = false; b.L.VolleyBoostUntil = Tick(p) + 30; }
                    break;
            }
        }

        // ---------------- kits ----------------
        // ability cooldowns, in ticks (Swift Chorus: Mending Chorus; Short Fuse goes through "t.split.cd")
        public static int Cooldown(PlayerController p, string ability, int ticks)
        {
            var b = B(p); if (b == null) return ticks;
            float cut = ability == "MendingChorus" ? b.V(SwiftChorus) : 0;
            return cut > 0 ? Mathf.RoundToInt(ticks * (1 - cut / 100f)) : ticks;
        }
        // Mending Chorus touching an ally (the caster too), before the heal: Chorus of Valour, and Overflow's overheal
        public static void ChorusTouched(PlayerController caster, PlayerController ally, int heal)
        {
            var b = B(caster); var a = B(ally); if (b == null) return;
            if (a != null && b.Has(ChorusOfValour)) { a.L.ValourUntil = caster.World.Tick + BuffTicks; a.L.ValourPct = Mathf.Max(a.L.ValourPct, b.V(ChorusOfValour)); }
            if (ally == caster && b.Has(Overflow))
            {
                int over = heal - (ally.MaxHealth - ally.Hp);
                if (over > 0) b.L.Overflow = Mathf.Min(b.V(Overflow), b.L.Overflow + over / 2f);
            }
        }
        public static int SurgePerGlyph(PlayerController p, int ticks) { var b = B(p); return b != null && b.Has(LongSurge) ? Sim.Ticks(b.V(LongSurge)) : ticks; }
        public static int GlyphCap(PlayerController p, int cap)
        {
            var b = B(p); if (b == null) return cap;
            int c = b.Has(FourthGlyph) ? Mathf.RoundToInt(b.V(FourthGlyph)) : cap;
            return b.Has(PerpetualBeam) ? Mathf.Min(c, 2) : c;
        }

        // ---------------- general (PlayerController) ----------------
        public static int PotionHeal(PlayerController p, int heal) { var b = B(p); return b != null && b.Has(StrongBrew) ? Mathf.RoundToInt(b.V(StrongBrew)) : heal; }
        public static int ExtraPotions(PlayerController p) { var b = B(p); return b != null ? Mathf.RoundToInt(b.V(SpareFlask)) : 0; }
        public static float SprintCost(PlayerController p, float cost)
        {
            var b = B(p); if (b == null) return cost;
            return (b.Has(Sprinter) ? b.V(Sprinter) : cost) + (b.Has(SteadyBreath) ? b.V2(SteadyBreath) - SprintBase : 0);   // Steady Breath adds on top
        }
        public static float RegenDelay(PlayerController p, float delay) { var b = B(p); return b != null && b.Has(DeepBreath) ? b.V(DeepBreath) : delay; }
    }
}
