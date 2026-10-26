using System.Collections;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Leaning Bell (Bellkeeper ability set v2) acceptance checks, owned by the Leaning Bell ability session.
    // Brief: bosses/4-shattered-hour-bellkeeper/abilities-v2/01-leaning-bell.md ("Autotest" under Build notes). Harness: BkStart,
    // BkAdd, BkStep... in RunAutoTest.BellkeeperV2.cs. Run alone with -lastbellBellkeeperV2 -lastbellTestAbility leaning_bell.
    // Tick convention: the cast tick is warning tick 0, so a W-tick warning resolves on tick cast + W - 1.
    public sealed partial class RunAutoTest
    {
        const string LbKey = "leaning_bell";
        LeaningBellAbility LbAbility => bk.Ability<LeaningBellAbility>();

        // Restarts the boss (practice loop: walk if needed, then cast) and steps until the cast starts. Returns its tick or -1.
        int LbCast(int maxTicks = 300)
        {
            var a = LbAbility; int from = d.World.Tick;
            bk.BeginCombat();
            return BkStepUntil(() => a.LastCastTick > from && a.Half != null, maxTicks) < 0 ? -1 : a.LastCastTick;
        }
        int LbHitTick(PlayerController p, Hazard h) { var hit = bkHits.FirstOrDefault(x => x.Player == p && x.HazardId == h.Id); return hit.Player == p ? hit.Tick : -1; }
        bool LbHitBy(PlayerController p, Hazard h) => bkHits.Any(x => x.Player == p && x.HazardId == h.Id);
        static string LbV(Vector3 v) => $"({v.x:0.0}, {v.z:0.0})";

        IEnumerator LeaningBellChecks()
        {
            Note("---- Leaning Bell (proposal) ----");

            // ---------- timing, shape, still players next to the boss / at long range / at the wall (N = 4, Throne) ----------
            yield return BkStart(LbKey);
            if (bk == null) yield break;
            var a = LbAbility;
            Check("Leaning Bell: built (not the scaffold stub)", a != null && a.Built);
            Check("Leaning Bell: practice hook -lastbellAbility leaning_bell loops it", bk.PracticeAbility == a);
            Check("Leaning Bell: Throne is a 13.0 m circle", !CombatWorld.SquareBound && Mathf.Abs(CombatWorld.PlayerBound - 13f) < 0.01f, $"{CombatWorld.PlayerBound} square={CombatWorld.SquareBound}");
            var hug = BkAdd(new Vector3(2.3f, 0, 0.3f));          // next to the boss (body 1.4 m), the boss's target
            var far = BkAdd(new Vector3(8.5f, 0, -3f));           // long range, 9 m
            var safe = BkAdd(new Vector3(-6f, 0, 2f));            // other side of the line
            BkPlace(d.Player, BkEdge(Vector3.right));             // at the wall, 12.95 m
            int t0 = LbCast();
            var h = a.Half;
            Check("Leaning Bell: spawns on the cast tick (warning tick 0)", t0 >= 0 && a.Spawned.Count == 1 && h != null && h.Age <= 1, $"cast tick {t0}, spawned {a.Spawned.Count}");
            if (h == null) yield break;
            Check("Leaning Bell: one Strike cone from the arena centre, half-angle 90, past the wall",
                  h.Shape == HazardShape.Cone && h.Kind == HazardKind.Strike && Sim.Flat(h.Origin).magnitude < 0.01f && h.HalfAngle == 90 && h.Radius >= CombatWorld.PlayerBound + 0.3f,
                  $"origin {LbV(h.Origin)}, r {h.Radius:0.0}, half {h.HalfAngle}");
            Check("Leaning Bell: Medium damage 40, knockback 1.5 m from the centre, stun 15", h.Damage == 40 && Mathf.Approximately(h.Knockback, 1.5f) && h.Stun == 15);
            Check("Leaning Bell: Throne warning 3.5 s (210 ticks) at Medium", a.WarningTicks == 210 && h.ResolveAt == 210, $"{a.WarningTicks} ticks");
            Check("Leaning Bell: majority side (3 vs 1), toward the target", a.CountHit == 3 && a.CountSafe == 1 && Vector3.Dot(a.LeanDir, Vector3.right) > 0.95f, $"lean {LbV(a.LeanDir)}, {a.CountHit} vs {a.CountSafe}");
            yield return Shot("bk_leaning_bell_warning_start");
            BkStep(150);
            float yaw0 = h.Yaw;
            yield return Shot("bk_leaning_bell_warning");
            if (bk.Presenter && bk.Presenter.Visual)
                Check("Leaning Bell: the shell visibly tilts toward the hit half during the windup", a.Fx && a.Fx.Tilt > 12f, a.Fx ? $"{a.Fx.Tilt:0.0}°" : "no fx");
            BkStep(80);
            int resolveTick = t0 + a.WarningTicks - 1;
            Check("Leaning Bell: resolves at cast + warning − 1 (warning ticks 0..209)", h.Age >= h.ResolveAt && LbHitTick(hug, h) == resolveTick, $"hit tick {LbHitTick(hug, h)}, expected {resolveTick}");
            Check("Leaning Bell: a still player hugging the boss is hit", LbHitBy(hug, h));
            Check("Leaning Bell: a still player at long range (9 m) is hit", LbHitBy(far, h));
            Check("Leaning Bell: a still player at the wall (12.95 m) is hit", LbHitBy(d.Player, h));
            Check("Leaning Bell: the player across the line is not hit", !LbHitBy(safe, h));
            Check("Leaning Bell: hits deal 40 at Medium", bkHits.Where(x => x.HazardId == h.Id).All(x => x.Damage == 40), string.Join(",", bkHits.Where(x => x.HazardId == h.Id).Select(x => x.Damage)));
            Check("Leaning Bell: knockback pushes away from the centre", Sim.Flat(hug.transform.position).magnitude > 3.3f, $"{Sim.Flat(hug.transform.position).magnitude:0.00} m from the centre");
            Check("Leaning Bell: remembered for The Unfinished Hour on resolve", bk.LastLean == h);
            BkStep(Mathf.Max(0, t0 + a.WarningTicks + LeaningBellAbility.RecoveryTicks - d.World.Tick - 1));
            bool runningBefore = a.Running; BkStep(1);
            Check("Leaning Bell: ends after a 1.0 s recovery", runningBefore && !a.Running, $"running at end-1: {runningBefore}, at end: {a.Running} (tick {d.World.Tick - t0} after cast)");
            Check("Leaning Bell: presentation removed after the recovery", a.Fx == null);
            Check("Leaning Bell: one hit per instance per player", d.World.Players.All(p => bkHits.Count(x => x.Player == p && x.HazardId == h.Id) <= 1));
            Check("Leaning Bell: at most one damaging pattern of its own", a.Spawned.Count == 1);
            // practice loops: a second cast, nothing else cast, and the previous lean is remembered
            BkTopUp();
            int t1 = BkStepUntil(() => a.Half != h, 400);
            var h2 = a.Half;
            Check("Leaning Bell: practice loop casts again, alone", t1 >= 0 && h2 != null && bk.Abilities.All(x => x == a || x.Casts == 0), $"next cast after {t1} ticks");
            if (h2 != null) { BkStep(h2.ResolveAt + 2); Check("Leaning Bell: previous lean kept for The Unfinished Hour", bk.LastLean == h2 && bk.PreviousLean == h); }
            ScriptedParty.RemoveExtras(d.World, d.Player);

            // ---------- walk back to within 3 m of the centre before the warning ----------
            yield return BkStart(LbKey);
            if (bk == null) yield break;
            a = LbAbility;
            bk.transform.position = new Vector3(6, 0, 0);
            BkPlace(d.Player, new Vector3(-7, 0, 0));
            int from = d.World.Tick;
            t0 = LbCast(400);
            float bossR = Sim.Flat(bk.transform.position).magnitude;
            Check("Leaning Bell: walks to within 3 m of the centre first (1.75 m/s, ≤ 2 s)", t0 >= 0 && bossR <= 3.05f && t0 - from <= LeaningBellAbility.WalkCapTicks + 3,
                  $"boss {bossR:0.00} m from the centre, cast {t0 - from} ticks after restart");

            // ---------- side rules: solo, N = 2 tie, N = 4 majority away from the target, N = 8 ----------
            // solo: your side; the line locks at warning (stepping across afterwards is the escape)
            yield return BkStart(LbKey);
            if (bk == null) yield break;
            a = LbAbility;
            BkPlace(d.Player, new Vector3(-5, 0, 5));
            t0 = LbCast(); h = a.Half;
            var soloDir = new Vector3(-1, 0, 1).normalized;
            Check("Leaning Bell solo: leans toward you", h != null && Vector3.Dot(a.LeanDir, soloDir) > 0.99f && a.CountHit == 1, h != null ? $"lean {LbV(a.LeanDir)}" : "no cast");
            if (h != null)
            {
                yaw0 = h.Yaw; BkStep(20);
                BkPlace(d.Player, new Vector3(5, 0, -5));        // the player crosses; the line must not follow
                BkStep(a.WarningTicks);
                Check("Leaning Bell: line and side locked at warning (never follow)", Mathf.Approximately(h.Yaw, yaw0) && Sim.Flat(h.Origin).magnitude < 0.01f && !LbHitBy(d.Player, h), $"yaw {yaw0:0} -> {h.Yaw:0}");
            }

            // N = 2, one each side: tie -> the target's side
            yield return BkStart(LbKey);
            if (bk == null) yield break;
            a = LbAbility;
            BkPlace(d.Player, new Vector3(3, 0, 0));
            var other = BkAdd(new Vector3(-8, 0, 0));
            t0 = LbCast(); h = a.Half;
            if (h != null) BkStep(a.WarningTicks + 2);
            Check("Leaning Bell N=2: a 1–1 tie goes to the target's side", h != null && Vector3.Dot(a.LeanDir, Vector3.right) > 0.99f && LbHitBy(d.Player, h) && !LbHitBy(other, h),
                  h != null ? $"lean {LbV(a.LeanDir)}, {a.CountHit} vs {a.CountSafe}" : "no cast");
            ScriptedParty.RemoveExtras(d.World, d.Player);

            // N = 4: the target alone on one side, three on the other -> the three
            yield return BkStart(LbKey);
            if (bk == null) yield break;
            a = LbAbility;
            BkPlace(d.Player, new Vector3(2.5f, 0, 0));
            var three = new[] { BkAdd(new Vector3(-5, 0, 3)), BkAdd(new Vector3(-7, 0, -2)), BkAdd(new Vector3(-10, 0, 0)) };
            t0 = LbCast(); h = a.Half;
            if (h != null) BkStep(a.WarningTicks + 2);
            Check("Leaning Bell N=4: the half with more players (3 vs 1), even away from the target", h != null && Vector3.Dot(a.LeanDir, Vector3.left) > 0.99f
                  && three.All(p => LbHitBy(p, h)) && !LbHitBy(d.Player, h), h != null ? $"lean {LbV(a.LeanDir)}, {a.CountHit} vs {a.CountSafe}" : "no cast");
            ScriptedParty.RemoveExtras(d.World, d.Player);

            // N = 8: 3 north (the target among them), 5 south -> south; damage never scales with N
            yield return BkStart(LbKey);
            if (bk == null) yield break;
            a = LbAbility;
            BkPlace(d.Player, new Vector3(0, 0, 2.5f));
            var north = new[] { BkAdd(new Vector3(3, 0, 6)), BkAdd(new Vector3(-4, 0, 8)) };
            var south = new[] { BkAdd(new Vector3(0, 0, -5)), BkAdd(new Vector3(4, 0, -7)), BkAdd(new Vector3(-4, 0, -7)), BkAdd(new Vector3(6, 0, -3)), BkAdd(new Vector3(-9, 0, -6)) };
            t0 = LbCast(); h = a.Half;
            if (h != null) BkStep(a.WarningTicks + 2);
            Check("Leaning Bell N=8: 5 vs 3 -> the five are hit, the three are not", h != null && d.World.Players.Count == 8 && Vector3.Dot(a.LeanDir, Vector3.back) > 0.99f
                  && south.All(p => LbHitBy(p, h)) && !north.Any(p => LbHitBy(p, h)) && !LbHitBy(d.Player, h), h != null ? $"lean {LbV(a.LeanDir)}, {a.CountHit} vs {a.CountSafe}" : "no cast");
            Check("Leaning Bell N=8: damage stays 40 (never scales with N)", h != null && bkHits.Where(x => x.HazardId == h.Id).All(x => x.Damage == 40));
            Check("Leaning Bell N=8: at most two damaging patterns live", BkPatternsLive() <= 2);
            ScriptedParty.RemoveExtras(d.World, d.Player);

            // ---------- escape routes from the far wall (finite stamina, no iframes) ----------
            yield return LbEscape("Throne, wall, 4.2 m/s + one dash", 4.2f, true, false, true);
            yield return LbEscape("Throne, wall, 4.2 m/s walk only (needs the dash: hit)", 4.2f, false, false, false);
            yield return LbEscape("Throne, wall, 6 m/s walk", 6f, false, false, true);

            // ---------- boss death mid-windup cancels it ----------
            yield return BkStart(LbKey);
            if (bk == null) yield break;
            a = LbAbility;
            BkPlace(d.Player, new Vector3(5, 0, 0));
            t0 = LbCast(); h = a.Half;
            BkStep(100);
            bk.DebugDefeat();
            bool gone = h != null && !d.World.Hazards.Live.Contains(h);
            BkStep(200);
            Check("Leaning Bell: boss death mid-windup cancels it on the killing tick", h != null && gone && !LbHitBy(d.Player, h) && !a.Running && a.Fx == null && bk.LastLean == null,
                  $"cancelled {gone}, hits {(h != null ? bkHits.Count(x => x.HazardId == h.Id) : -1)}");

            // ---------- Easy / Hard ----------
            yield return BkStart(LbKey, Difficulty.Easy);
            if (bk == null) yield break;
            a = LbAbility;
            BkPlace(d.Player, new Vector3(5, 0, 0));
            t0 = LbCast(); h = a.Half;
            if (h != null) BkStep(a.WarningTicks + 2);
            Check("Leaning Bell Easy: warning ×1.25 = 4.4 s on the Throne (264 ticks), damage 40 × 0.6", h != null && a.WarningTicks == 264 && LbHitTick(d.Player, h) == t0 + 263
                  && bkHits.Any(x => x.HazardId == h.Id && x.Damage == 24), h != null ? $"{a.WarningTicks} ticks, dmg {string.Join(",", bkHits.Where(x => x.HazardId == h.Id).Select(x => x.Damage))}" : "no cast");

            yield return BkStart(LbKey, Difficulty.Hard);
            if (bk == null) yield break;
            a = LbAbility;
            BkPlace(d.Player, new Vector3(5, 0, 0));
            t0 = LbCast(); h = a.Half;
            if (h != null) BkStep(a.WarningTicks + 2);
            Check("Leaning Bell Hard: same 3.5 s warning (no speed-up), damage 40 × 1.3", h != null && a.WarningTicks == 210 && bkHits.Any(x => x.HazardId == h.Id && x.Damage == 52),
                  h != null ? $"{a.WarningTicks} ticks, dmg {string.Join(",", bkHits.Where(x => x.HazardId == h.Id).Select(x => x.Damage))}" : "no cast");

            // ---------- clip: N = 4 on the Throne, the real practice loop in real time ----------
            yield return BkStart(LbKey);
            if (bk == null) yield break;
            BkPlace(d.Player, new Vector3(4, 0, -6));
            BkAdd(new Vector3(-3, 0, -8)); BkAdd(new Vector3(7, 0, 4)); BkAdd(new Vector3(-8, 0, 5));
            yield return BkClip("bk_leaning_bell", 5.5f);
            ScriptedParty.RemoveExtras(d.World, d.Player);

            // ---------- the square test map: adaptive warning 4.4 s (Easy 5.5 s) and the corner escape ----------
            yield return BkStart(LbKey, Difficulty.Medium, testMap: true);
            if (bk == null) yield break;
            a = LbAbility;
            Check("Leaning Bell: test map is the 12.5 m square", CombatWorld.SquareBound && Mathf.Abs(CombatWorld.PlayerBound - 12.5f) < 0.01f, $"{CombatWorld.PlayerBound} square={CombatWorld.SquareBound}");
            BkPlace(d.Player, BkEdge(new Vector3(1, 0, 1)));
            t0 = LbCast(); h = a.Half;
            if (h != null) BkStep(a.WarningTicks + 2);
            Check("Leaning Bell test map: warning 4.4 s (264 ticks); a still player in the corner is hit", h != null && a.WarningTicks == 264 && LbHitBy(d.Player, h),
                  h != null ? $"{a.WarningTicks} ticks, corner {LbV(BkEdge(new Vector3(1, 0, 1)))}" : "no cast");
            Check("Leaning Bell test map: Easy warning 5.5 s (330 ticks)", LeaningBellAbility.WarningTicksFor(Difficulty.Easy) == 330, $"{LeaningBellAbility.WarningTicksFor(Difficulty.Easy)}");
            yield return LbEscape("test map, corner, 4.2 m/s + one dash", 4.2f, true, true, true);
            yield return Shot("bk_leaning_bell_testmap");
        }

        // Solo player at the far wall of the hit half reacts after 0.6 s and walks (and dashes) across the line, routed past
        // the boss (which stands 2.5 m off the lean axis, inside its 3 m cast range). Asserts escape / hit and stamina.
        IEnumerator LbEscape(string label, float speed, bool dash, bool testMap, bool expectEscape)
        {
            yield return BkStart(LbKey, Difficulty.Medium, testMap);
            if (bk == null) yield break;
            var a = LbAbility;
            var axis = testMap ? new Vector3(1, 0, 1).normalized : Vector3.right;
            var lat = Vector3.Cross(axis, Vector3.up).normalized;          // the boss stands on -lat, the route goes on +lat
            bk.transform.position = -lat * 2.5f;
            var start = BkEdge(axis);
            BkPlace(d.Player, start);
            int t0 = LbCast(); var h = a.Half;
            if (h == null) { Check($"Leaning Bell escape ({label})", false, "no cast"); yield break; }
            var s = BkScript(d.Player);
            var goal = axis * -0.35f + lat * 0.8f;
            BkStep(36);                                                       // 0.6 s reaction
            s.Speed = speed; s.Goal = goal; s.DashNext = dash;
            float st0 = d.Player.Stamina, stMin = st0; int crossed = -1;
            for (int i = 36; i < a.WarningTicks + 2; i++)
            {
                BkStep(1); stMin = Mathf.Min(stMin, d.Player.Stamina);
                if (crossed < 0 && !h.Contains(d.Player.transform.position)) crossed = d.World.Tick - t0;
            }
            bool hit = LbHitBy(d.Player, h);
            Check($"Leaning Bell escape: {label}", expectEscape ? !hit : hit,
                  $"{Vector3.Distance(start, goal):0.00} m route, {(crossed >= 0 ? $"out of the shape at warning tick {crossed} / {a.WarningTicks - 1}" : "never out")}, " +
                  $"stamina {st0:0} -> min {stMin:0}, hit {hit}");
            if (dash) Check($"Leaning Bell escape: {label}: finite stamina (one dash spent, never below 0)", st0 - stMin >= PlayerController.DashCost - 1 && stMin >= 0, $"{st0:0} -> {stMin:0}");
        }
    }
}
