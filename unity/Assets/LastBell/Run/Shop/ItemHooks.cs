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

        const int BuffTicks = 240;   // Cracked Armour, Chorus of Valour, Rally: 4 s
        const float LoneBellRange = 8f, FormationRange = 5f, RallyRange = 6f;
        const int MaxHpCapPct = 20;  // shop spec §8: items give at most +20% max HP
        const float KnockbackCapPct = 30;   // shop spec §8: knockback reduction capped at -30%

        static PlayerBuild B(PlayerController p) => p != null ? p.Build : null;
        static int Tick(PlayerController p) => p.World.Tick;

        // The kit that owns an ability name (names come from the kits' DamageBoss / StrikeBoss calls). Co-op with two
        // players of one class needs a damage source on DamageBoss; until then the first player of that class owns it.
        static string ClassOf(string ability)
        {
            if (ability == null) return null;
            if (ability.StartsWith("SteelRhythm") || ability.StartsWith("SunderingBlow") || ability.StartsWith("LastWord") || ability.StartsWith("Pendulum Cut")) return "Vanguard";
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

        // ---------------- numbers ----------------
        // A shared number for one player. Keys name the number at its point of use; the default is the caller's own.
        public static int I(PlayerController p, string key, int v) => Mathf.RoundToInt(F(p, key, v));
        public static float F(PlayerController p, string key, float v)
        {
            var b = B(p); if (b == null) return v;
            switch (key)
            {
                // controller (every class)
                case "dash.distance": { float d = v; if (b.Has(ClockworkStep)) d = Mathf.Max(d, b.V(ClockworkStep)); return d + b.V(FarBlink); }
                case "dash.cost":
                {
                    float c = b.Has(LightStep) ? b.V(LightStep) : v;
                    if (b.Has(SecondStep) && Tick(p) - b.L.LastDashAt <= Sim.Ticks(1.5f)) c = Mathf.Min(c, b.V(SecondStep));   // Second Step: a quick second dash is cheaper (no iframes)
                    return c;
                }
                case "stamina.max": return v + b.V(LongLungs);
                case "knockback": return v * (1 - Mathf.Min(KnockbackCapPct, b.V(SureFooting)) / 100f);
                case "move.speed": return b.L.LightFeetUntil > Tick(p) ? v * (1 + b.V(LightFeet) / 100f) : v;
                // Vanguard
                case "v.combo.window": return v + Sim.Ticks(b.V(QuickHands));
                case "v.hit3.arc": return v * (1 + b.V(WideArc) / 100f);
                case "v.sunder.reach": return v + b.V(LongReach);
                case "v.sunder.double_arc": return v * 1.6f;   // Double Opening's wider split impact
                case "v.brace.move": return b.Has(SteadyStance) ? 1 - (1 - v) * (1 - b.V(SteadyStance) / 100f) : v;
                case "v.opening.stacks": return b.Has(DoubleOpening) ? 2 : v;
                case "v.ult.windup": { float t = v; if (b.Has(HeavyHour)) t += Sim.Ticks(0.3f); if (b.Has(QuickVerdict)) t -= Sim.Ticks(0.3f); return t; }
                case "v.ult.fan": return b.Has(HeavyHour) ? v * 1.4f : v;
                case "v.combo.length": return b.Has(TollOfSteel) ? 4 : v;
                // Chimesinger
                case "c.note.speed": return v * (1 + b.V2(BrightNote) / 100f);
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
            }
            return v;
        }

        public static int MaxHp(PlayerController p, int baseHp)
        {
            var b = B(p); if (b == null) return baseHp;
            float pct = Mathf.Min(MaxHpCapPct, b.V(Grit)) - (b.Has(GlassEdge) ? 8 : 0);
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
                if (ability == "SteelRhythm3") pct += b.V(TemperedEdge);
                if (ability.StartsWith("SteelRhythm") && L.MomentumHit) { L.MomentumHit = false; pct += b.V(Momentum); }
                if (ability == "SunderingBlow++") pct += b.V(DoubleOpening);
                if (ability.StartsWith("LastWord")) pct += b.V(HeavyHour) + b.V(QuickVerdict);
                if (ability == "Bellstrike") pct += b.V(HeavyBell);
                if (ability.StartsWith("ResonantNote")) pct += b.V(BrightNote);
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
                if (L.RhythmOn) pct += b.V(RhythmKeeper);
                if (L.ValourUntil > now) pct += L.ValourPct;
                if (L.RallyUntil > now) pct += L.RallyPct;
            }
            // Cracked Armour: everyone's heavy hits; solo, only the Vanguard's own Last Word
            if (Run.CrackUntil > w.Tick && Heavy(ability) && (!Run.Solo || ability.StartsWith("LastWord"))) pct += Run.CrackPct;
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
                    if (ability.StartsWith("SunderingBlow") && b.Has(CrackedArmour)) { Run.CrackUntil = now + BuffTicks; Run.CrackPct = b.V(CrackedArmour); }
                    // Rhythm Keeper: the combo's last hit landing counts a full combo; three in a row without being hit
                    if (b.Has(RhythmKeeper) && ability == "SteelRhythm" + I(owner, "v.combo.length", 3) && ++b.L.RhythmCombos >= 3) b.L.RhythmOn = true;
                    if (ability == "LastWord" && b.Has(Rally))
                        foreach (var a in w.Players)
                            if (!a.Down && a.Build != null && Sim.Flat(a.transform.position - owner.transform.position).magnitude <= RallyRange)
                            { a.Build.L.RallyUntil = now + BuffTicks; a.Build.L.RallyPct = Mathf.Max(a.Build.L.RallyPct, b.V(Rally)); }
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
            b.L.MomentumReady = b.Has(Momentum); b.L.GraceReady = b.Has(GraceNote); b.L.BlinkReady = b.Has(BlinkCharge); b.L.LastDashAt = Tick(p);
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
            b.L.RhythmCombos = 0; b.L.RhythmOn = false;
        }

        // Vanguard: Momentum turns the next basic into hit 3 (consumed here)
        public static bool TakeMomentum(PlayerController p)
        {
            var b = B(p); if (b == null || !b.L.MomentumReady) return false;
            b.L.MomentumReady = false; b.L.MomentumHit = true; return true;
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

        // A kit ability released (Pendulum Cut, Iron Echo, Toll of Steel's fan, Blink Charge's volley). a/b are the
        // kit's own numbers for that ability (reach and arc, or wave damage and width).
        public static void Cast(PlayerController p, string key, Vector3 origin, Vector3 dir, float a, float bw)
        {
            var b = B(p); if (b == null) return;
            switch (key)
            {
                case "v.sunder":
                    if (b.Has(PendulumCut)) effects.Add(new Effect { Owner = p, Pos = origin, Dir = dir, A = a, B = bw, Left = Sim.Ticks(b.V(PendulumCut)), Every = 15, Damage = 10, Ability = "Pendulum Cut" });
                    break;
                case "v.lastword":
                    if (b.Has(IronEcho)) effects.Add(new Effect { Owner = p, Wave = true, Pos = origin, Dir = dir, B = bw, Delay = Sim.Ticks(0.6f), Damage = Mathf.RoundToInt(a * b.V(IronEcho) / 100f), Ability = "LastWordWave (echo)" });
                    break;
                case "v.hit4":
                    if (b.Has(TollOfSteel)) effects.Add(new Effect { Owner = p, Wave = true, Pos = origin, Dir = dir, B = bw * 0.7f, Damage = Mathf.RoundToInt(a * b.V(TollOfSteel) / 100f), Ability = "SteelRhythm4 fan" });
                    break;
                case "a.volley":
                    if (b.L.BlinkReady) { b.L.BlinkReady = false; b.L.VolleyBoostUntil = Tick(p) + 30; }
                    break;
            }
        }

        // ---------------- timed item hits (CombatWorld.Step, combat ticks only) ----------------
        sealed class Effect
        {
            public PlayerController Owner; public bool Wave, Hit; public Vector3 Pos, Dir; public float A, B, Travel;
            public int Left, Every, Delay, Damage, Age; public string Ability;
        }
        static readonly List<Effect> effects = new List<Effect>();
        const float WaveSpeed = 18f, WaveRange = 8f;   // the Last Word shockwave's own numbers
        public static void Step(CombatWorld w)
        {
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                var e = effects[i];
                if (e.Owner == null || e.Owner.World != w) { effects.RemoveAt(i); continue; }
                if (e.Delay > 0) { e.Delay--; continue; }
                var boss = w.Boss;
                if (e.Wave)
                {
                    float d = WaveSpeed * Sim.Dt; e.Pos += e.Dir * d; e.Travel += d;
                    w.Fx.Shockwave(e.Pos, e.Dir, e.B);
                    if (!e.Hit && boss != null && boss.Solid && Sim.Flat(boss.transform.position - e.Pos).magnitude <= boss.BodyRadius + e.B / 2)
                    {
                        e.Hit = true;
                        w.DamageBoss(e.Damage, boss.transform.position - Sim.Flat(boss.transform.position - e.Pos).normalized * boss.BodyRadius + Vector3.up * boss.HitHeight, e.Ability);
                    }
                    if (e.Travel >= WaveRange) effects.RemoveAt(i);
                }
                else
                {
                    if (++e.Age % e.Every == 0) w.StrikeBoss(e.Owner, e.Pos, e.Dir, e.A, e.B, e.Damage, e.Ability);
                    if (e.Age >= e.Left) effects.RemoveAt(i);
                }
            }
        }
        public static void ClearEffects() { effects.Clear(); }

        // ---------------- kits ----------------
        // ability cooldowns, in ticks (Countdown: Last Word; Swift Chorus: Mending Chorus)
        public static int Cooldown(PlayerController p, string ability, int ticks)
        {
            var b = B(p); if (b == null) return ticks;
            float cut = ability == "LastWord" ? b.V(Countdown) : ability == "MendingChorus" ? b.V(SwiftChorus) : 0;
            return cut > 0 ? Mathf.RoundToInt(ticks * (1 - cut / 100f)) : ticks;
        }
        // Oathbound: Sundering Blow can't be empowered, and every Nth Opening resets Last Word (returns true to reset)
        public static bool SunderEmpowerable(PlayerController p) => !(B(p)?.Has(Oathbound) ?? false);
        public static bool OnOpening(PlayerController p)
        {
            var b = B(p); if (b == null || !b.Has(Oathbound)) return false;
            b.L.Openings++;
            return b.L.Openings % Mathf.Max(1, Mathf.RoundToInt(b.V(Oathbound))) == 0;
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
        public static float SprintCost(PlayerController p, float cost) { var b = B(p); return b != null && b.Has(Sprinter) ? b.V(Sprinter) : cost; }
        public static float RegenDelay(PlayerController p, float delay) { var b = B(p); return b != null && b.Has(DeepBreath) ? b.V(DeepBreath) : delay; }
    }
}
