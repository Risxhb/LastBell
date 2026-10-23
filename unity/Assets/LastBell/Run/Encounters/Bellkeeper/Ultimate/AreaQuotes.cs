using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // Thirteen Chimes: the area quotes (`bellkeeper.ult_area_quotes`), Bellkeeper Ultimate — proposal, built 2026-10-23,
    // not human-approved. Design: ultimate/ULTIMATE.md §5.5 (T2, "the final exam"): one quote of each earlier area,
    // reworked into the Bellkeeper's clockwork, each alone. Numbers per ULTIMATE.md §2 (standard 40, heavy 60).
    // Cue: Variant "quotes": At = Spire, Foundry, Verdant resolve times (seconds after launch; plan 4, 9, 14 = T2 14, 19, 24).
    //
    // Spire · The Sundial (Aurelion's light, inverted to shadow; none of Aurelion's own rules): from launch the boss turns
    //   toward the SUN, the player farthest from him at launch (an ivory "SUN" glyph), at 90°/s. His shadow is a Ground cone
    //   from his centre along his facing, 30° half-angle, to the wall. It follows his facing for 3.0 s, locks 1.0 s before
    //   it falls (At[0]), 60. The sun baits it close (4-6 m) and steps out of the wedge after the lock; everyone else stays off
    //   the boss -> sun line. Punishes range: the bait is whoever stands farthest.
    // Foundry · The Bellows (the Foundry's furnace breath): from the Spire's fall the boss's chest draws breath. Coral: the
    //   forge core (r 2.6 on the boss, 60) and the tide line (a ring from 7.0 m on the boss out past the wall, 60). Ivory: the
    //   bellows' reach (r 7.0). 0.2 s before At[1] the core fires and the bellows blows everyone within 7 m exactly 3.0 m
    //   straight out from the boss (1 damage, the floor of any hit; stunned 0.25 s); on At[1] the tide line strikes. So:
    //   stand 2.9-3.7 m from the boss centre (a brass guide ring shows the band) and land at 5.9-6.7 m, inside the tide line.
    //   Hugging melee is in the core, ranged is in the tide.
    // Verdant · The Root Hours (the Verdant chimes' roots): three root circles r 11.4 on hour marks 7 m out, four hours apart
    //   (seeded: I/V/IX .. IV/VIII/XII), bloom 1.0 s apart (the last on At[2]); each shows 3.0 s before it blooms. Their union
    //   covers the whole floor. A bloom deals 40 and roots (Stilled 1.5 s), so a rooted player usually eats the next bloom too
    //   (two or three = 80-120: the chain is the one-shot). Route: stand just outside the first circle at its seam with a
    //   neighbour, then step 2-3 m into the first circle (now spent, outside the other two) for the last two blooms.
    // N = 1/2/4/8: identical (shared shapes). The perfect bot's strat (GUIDE.md, written for this build): Demands().
    public sealed class AreaQuotes : UltimateMechanic
    {
        public override string Id => UltimateMechanics.AreaQuotes;
        public override string Name => "Thirteen Chimes: the area quotes";

        public const string SpireSource = "The Sundial", CoreSource = "Forge Core", BellowsSource = "The Bellows",
            TideSource = "Tide Line", BloomSource = "Root Hour";
        // Spire
        public const float ShadowHalfAngle = 30f, ShadowReach = 30f, ShadowTurn = 90f;
        public const int ShadowLockTicks = 60, ShadowDamage = UltShapes.HeavyDamage;
        // Foundry
        public const float CoreRadius = 2.6f, TideInner = TollTideAbility.TideInner, TideReach = 40f, Push = 3.0f;
        public const int BellowsLead = 12, CoreDamage = UltShapes.HeavyDamage, TideDamage = UltShapes.HeavyDamage;
        // Verdant
        public const float HourMark = 7.0f, BloomRadius = 11.4f;
        public const int BloomWarnTicks = 180, BloomGapTicks = 60, BloomDamage = UltShapes.StandardDamage, RootTicks = 90;

        // read by tests and Demands (null / -1 until that quote starts)
        public PlayerController Sun { get; private set; }
        public Hazard Shadow { get; private set; }
        public Hazard Core { get; private set; }
        public Hazard Bellows { get; private set; }
        public Hazard Tide { get; private set; }
        public Vector3 ForgeOrigin { get; private set; }
        public int BellowsTick { get; private set; } = -1;
        public int TideTick { get; private set; } = -1;
        public int FoundryFrom { get; private set; } = -1;
        public readonly int[] BloomHours = new int[3];       // bloom order, hours 1..12
        public readonly int[] BloomTicks = { -1, -1, -1 };
        public readonly Hazard[] Blooms = new Hazard[3];
        public int Rooted { get; private set; }              // root applications this cast (tests)
        BellkeeperFx.Telegraph guide;

        public override UltCue DefaultCue() => new UltCue { Variant = "quotes", At = new[] { 4f, 9f, 14f } };

        static Vector3 PlanDir(float deg) { float a = deg * Mathf.Deg2Rad; return new Vector3(-Mathf.Sin(a), 0, -Mathf.Cos(a)); }   // north −Z, east −X
        public static Vector3 HourCentre(int hour) => PlanDir(30f * hour) * HourMark;
        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

        public override IEnumerable<int> Cast()
        {
            Reset();
            int seed = Cue.Seed != 0 ? Cue.Seed : Casts * 7 + 3;
            int spireAt = TickAt(Cue.AtOr(0, 4)), foundryAt = TickAt(Cue.AtOr(1, 9)), verdantAt = TickAt(Cue.AtOr(2, 14));

            // ---------------- Spire: the sundial shadow ----------------
            Boss.ShowAttack("Thirteen Chimes · The Sundial");
            if (Boss.Presenter && !Boss.Presenter.PlayOnce("SundialTurn", (spireAt - Now) / (float)Sim.Hz)) Boss.Presenter.PlayOnce("BellSweep", 2.03f);
            Sun = Farthest();
            if (Sun) Ult.Of(Sun).Note = "SUN";
            int n = Mathf.Max(1, spireAt - Now), rAt = UltShapes.ResolveIn(n);
            Shadow = Spawn(new Hazard
            {
                Source = SpireSource, Shape = HazardShape.Cone, Kind = HazardKind.Ground, Origin = Flat(Boss.transform.position), Yaw = Boss.transform.eulerAngles.y,
                Radius = ShadowReach, HalfAngle = ShadowHalfAngle, LockAt = Mathf.Max(1, rAt - ShadowLockTicks), ResolveAt = rAt, Damage = ShadowDamage,
                Knockback = 1.5f, Stun = 15,
                Track = h => { h.Origin = Flat(Boss.transform.position); h.Yaw = Boss.transform.eulerAngles.y; },
            });
            Shadow.OnResolve = h => { W.BossFx.SweepResolve(h.Origin, h.Yaw, h.Radius, h.HalfAngle); ClearSun(); Boss.Flare(1.5f); };
            while (Now <= spireAt)
            {
                if (Sun && Sun.Down) { ClearSun(); Sun = Farthest(); if (Sun) Ult.Of(Sun).Note = "SUN"; }
                if (Sun && !Shadow.Locked) Boss.Face(Sim.YawTo(Boss.transform.position, Sun.transform.position, Boss.transform.eulerAngles.y), ShadowTurn);
                yield return 1;
            }

            // ---------------- Foundry: the bellows ----------------
            Boss.ShowAttack("Thirteen Chimes · The Bellows");
            FoundryFrom = Now; BellowsTick = foundryAt - BellowsLead; TideTick = foundryAt;
            ForgeOrigin = Flat(Boss.transform.position);
            if (Boss.Presenter && !Boss.Presenter.PlayOnce("ForgeBreath", (foundryAt - Now) / (float)Sim.Hz)) Boss.Presenter.PlayOnce("ShatteredChime", 2.63f);
            int bAt = UltShapes.ResolveIn(Mathf.Max(0, BellowsTick - Now)), tAt = UltShapes.ResolveIn(Mathf.Max(0, TideTick - Now));
            // the bellows and the core push the same 3 m from the same point, so their resolve order doesn't matter. The
            // bellows deals its own hits (HitOnResolve false): it is a push, not a place to avoid, so the bot reads the band
            // rule in Demands() instead of a generic "leave the circle"
            Bellows = Spawn(new Hazard
            {
                Source = BellowsSource, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = ForgeOrigin, Radius = TideInner,
                LockAt = 0, ResolveAt = bAt, Damage = 0, Knockback = Push, Stun = 15, HitOnResolve = false,
            });
            Bellows.Tele?.Memory();   // not a strike: the ivory reach of the breath
            Bellows.OnResolve = h =>
            {
                foreach (var p in W.Players.ToArray()) if (!p.Down && !h.Hit.Contains(p) && h.Contains(p.transform.position)) { h.Hit.Add(p); p.ReceiveHit(h); }
                W.BossFx.TollResolve(h.Origin, 0.6f, h.Radius); Boss.Flare(2f); guide?.Dismiss(); guide = null;
            };
            Core = Spawn(new Hazard
            {
                Source = CoreSource, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = ForgeOrigin, Radius = CoreRadius,
                LockAt = 0, ResolveAt = bAt, Damage = CoreDamage, Knockback = Push, Stun = 15,
            });
            Core.OnResolve = h => W.BossFx.ChimeResolve(h.Origin, h.Radius);
            Tide = Spawn(new Hazard
            {
                Source = TideSource, Shape = HazardShape.Ring, Kind = HazardKind.Ground, Origin = ForgeOrigin, Inner = TideInner, Radius = TideReach,
                LockAt = 0, ResolveAt = tAt, Damage = TideDamage, Knockback = 0, Stun = 0,
            });
            Tide.OnResolve = h => W.BossFx.TollResolve(h.Origin, h.Inner, CombatWorld.PlayerBound + 1f);
            guide = W.BossFx.Ring(CoreRadius + Hazard.Margin, TideInner - Push - Hazard.Margin);   // the landing band: brass, never coral
            guide.Tint(new Color(0.82f, 0.64f, 0.3f) * 0.6f, new Color(1.4f, 1.1f, 0.5f));
            guide.Place(ForgeOrigin, 0);
            while (Now <= TideTick) { Boss.Glow((Now - FoundryFrom) / (float)Mathf.Max(1, TideTick - FoundryFrom)); yield return 1; }

            // ---------------- Verdant: the root hours ----------------
            Boss.ShowAttack("Thirteen Chimes · The Root Hours");
            int h0 = 1 + seed % 4, first = seed / 4 % 3, step = seed / 12 % 2 == 0 ? 1 : 2;   // step 2 = counter-clockwise
            for (int k = 0; k < 3; k++)
            {
                BloomHours[k] = h0 + 4 * ((first + step * k) % 3);
                BloomTicks[k] = verdantAt - BloomGapTicks * (2 - k);
            }
            if (Boss.Presenter && !Boss.Presenter.PlayOnce("RootBloom", (verdantAt - Now) / (float)Sim.Hz)) Boss.Presenter.PlayOnce("HollowToll", 2.03f);
            for (int k = 0; k < 3; k++)
            {
                int spawnAt = Mathf.Max(Now, BloomTicks[k] - BloomWarnTicks);
                while (Now < spawnAt) yield return 1;
                Blooms[k] = Spawn(new Hazard
                {
                    Source = BloomSource, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = HourCentre(BloomHours[k]), Radius = BloomRadius,
                    LockAt = 0, ResolveAt = UltShapes.ResolveIn(Mathf.Max(0, BloomTicks[k] - Now)), Damage = BloomDamage, Knockback = 0, Stun = 0,
                });
                Blooms[k].OnResolve = hz =>
                {
                    W.BossFx.ChimeResolve(hz.Origin, 2.5f);
                    foreach (var p in hz.Hit) if (!p.Down) { Ult.Still(p, W.Tick, RootTicks, 0); Rooted++; }
                };
            }
            while (Now <= verdantAt) yield return 1;
        }

        PlayerController Farthest()
        {
            PlayerController best = null; float bd = -1; var b = Flat(Boss.transform.position);
            foreach (var p in W.Players) { if (p.Down) continue; float d = (Flat(p.transform.position) - b).sqrMagnitude; if (d > bd) { bd = d; best = p; } }
            return best;
        }

        // ---------------- the perfect bot's strat ----------------
        public override void Demands(PlayerController p, List<BotDemand> into)
        {
            if (!Running) return;
            var pos = Flat(p.transform.position);
            // Spire: the sun keeps the bait short (4-6 m from the boss) until the lock; leaving the locked wedge is the live
            // cone's own rule (visible)
            if (Shadow != null && !Shadow.Locked && p == Sun)
            {
                var b = Flat(Boss.transform.position); float lo = Boss.BodyRadius + PlayerController.Radius + 0.5f;
                int lockTick = LaunchTick + Shadow.LockAt - 1;
                var away = pos - b; if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
                into.Add(new BotDemand { Tick = lockTick, Since = LaunchTick, Ok = q => { float d = (Flat(q) - b).magnitude; return d >= lo && d <= 6f; }, Hint = b + away.normalized * 4.5f, Why = "Sundial: bait close", From = this });
            }
            // Foundry: stand in the band so the bellows lands you inside the tide line (a read: you must know the push)
            if (Bellows != null && BellowsTick >= Now)
            {
                var o = ForgeOrigin; float lo = CoreRadius + Hazard.Margin + 0.1f, hi = TideInner - Push - Hazard.Margin - 0.1f;
                var away = pos - o; if (away.sqrMagnitude < 0.01f) away = Vector3.back;
                into.Add(new BotDemand { Tick = BellowsTick, Since = FoundryFrom, Ok = q => { float d = (Flat(q) - o).magnitude; return d > lo && d < hi; }, Hint = o + away.normalized * (lo + hi) / 2, Memory = true, Why = "Bellows: the band", From = this });
            }
            // Verdant: just outside the first circle at a seam, then into the first circle (spent) for the other two blooms
            if (Blooms[0] != null && BloomTicks[2] >= Now)
            {
                float aA = 30f * BloomHours[0];
                float r = CombatWorld.PlayerBound - 0.5f, edge = SeamAngle(r, 0.15f);
                Vector3 Start(int s) => PlanDir(aA + s * (edge + 0.8f)) * r;
                Vector3 Inside(int s) => PlanDir(aA + s * (120f - edge - 1.0f)) * r;
                int side = (Start(1) - pos).sqrMagnitude <= (Start(-1) - pos).sqrMagnitude ? 1 : -1;
                var a = Blooms[0];
                if (BloomTicks[0] >= Now)
                    into.Add(new BotDemand { Tick = BloomTicks[0], Since = BloomTicks[0] - BloomWarnTicks, Ok = q => !a.Contains(q), Hint = Start(side), Why = "Root hours: seam", From = this });
                for (int k = 1; k < 3; k++)
                {
                    if (BloomTicks[k] < Now) continue;
                    var hk = Blooms[k]; var ck = HourCentre(BloomHours[k]);
                    into.Add(new BotDemand { Tick = BloomTicks[k], Since = BloomTicks[0] - BloomWarnTicks, Ok = q => hk != null ? !hk.Contains(q) : (Flat(q) - ck).magnitude > BloomRadius + Hazard.Margin, Hint = Inside(side), Why = "Root hours: into the first", From = this });
                }
            }
        }
        // the plan angle (from an hour mark's direction) at radius r where a root circle's edge (plus margin and safety) lies
        static float SeamAngle(float r, float safety)
        {
            float R = BloomRadius + Hazard.Margin + safety, c = HourMark;
            return Mathf.Acos(Mathf.Clamp((c * c + r * r - R * R) / (2 * c * r), -1, 1)) * Mathf.Rad2Deg;
        }

        // drop the SUN glyph without creating a status (the encounter may already have reset them)
        void ClearSun()
        {
            if (!Sun || Ult == null) return;
            foreach (var kv in Ult.All) if (kv.Key == Sun) { kv.Value.Note = null; break; }
        }

        void Reset()
        {
            ClearSun();
            Sun = null; Shadow = Core = Bellows = Tide = null;
            BellowsTick = TideTick = FoundryFrom = -1;
            for (int k = 0; k < 3; k++) { Blooms[k] = null; BloomTicks[k] = -1; }
            Rooted = 0;
            guide?.Dismiss(); guide = null;
        }

        public override void Cancel()
        {
            Reset();
            base.Cancel();
        }
    }
}
