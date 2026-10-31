using System.Collections;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // The Great Casting (brief 04): pour and march timing, the pour's tracking and lock, the line split for N = 1/2/4/8
    // (no wipe when empty), still players on a spoke at the edge, at 8.5 m and next to the boss, the escapes at 4.2 m/s
    // (join the pour from the far side, leave an inner stamp, leave a rim stamp), the solo dodge, the phase variants,
    // Easy/Hard, one hit per instance, and the boss dying mid-windup.
    public sealed class GreatCastingTest : CrucivarAbilityTest
    {
        public override string Id => "crucivar.great_casting";

        static int Tk(float s, Difficulty d = Difficulty.Medium) => Sim.Ticks(s * (d == Difficulty.Easy ? 1.25f : 1f));

        static Vector3 OnSpoke(Vector3 boss, float pourYaw, int spoke, float r)
        {
            GreatCasting.StampCentre(boss, GreatCasting.SpokeYaw(pourYaw, spoke), r, out var c);
            return c;
        }

        public override IEnumerator Run(CrucivarV2AutoTest t)
        {
            // ---- 1. solo, phase 3, a still target: timing, solo 40, the pour channel is a gap ----
            yield return t.Setup(1, Difficulty.Medium, at: new[] { new Vector3(0, 0, 10) });
            var boss = t.Boss; if (boss == null) yield break;
            var b0 = Sim.Flat(boss.transform.position);
            t.Note($"boss at {b0}, rim {CrucivarAbility.Rim:0.0} m");
            t.Begin(boss.Casting.Run(6));
            yield return t.Ticks(Tk(3.3f));
            yield return t.Shot("great_casting_march");
            yield return t.WaitIdle();
            var pourRec = t.Spawned("Pour").FirstOrDefault();
            t.Check("one pour spawned", t.Spawned("Pour").Count() == 1);
            if (pourRec == null) yield break;
            int s0 = pourRec.Tick; var pour = pourRec.H;
            t.Check("pour: lane 2.6 m, Ground, lock 1.5 s, resolve 3.5 s", pour.Shape == HazardShape.Lane && Mathf.Approximately(pour.Width, 2.6f) &&
                pour.Kind == HazardKind.Ground && pour.LockAt == Tk(1.5f) && pour.ResolveAt == Tk(3.5f), $"lock {pour.LockAt} resolve {pour.ResolveAt}");
            float wall = (Sim.Flat(pour.Origin) + Sim.Dir(pour.Yaw) * pour.Length).magnitude;
            t.Check("pour runs from the boss to the wall", Vector3.Distance(Sim.Flat(pour.Origin), b0) < 0.05f && wall >= CrucivarAbility.Rim, $"end at r {wall:0.00}");
            var stamps = t.Spawned("Stamp").ToList();
            t.Check("phase 3: 30 stamps (6 spokes x 5 rings), r 1.2, 18 each, Ground", stamps.Count == 30 &&
                stamps.All(s => Mathf.Approximately(s.H.Radius, 1.2f) && s.H.Damage == 18 && s.H.Kind == HazardKind.Ground), $"{stamps.Count}");
            t.Check("every stamp telegraph appears at 3.0 s", stamps.All(s => s.Tick - s0 == Tk(3.0f)), string.Join(",", stamps.Select(s => s.Tick - s0).Distinct()));
            bool rimFirst = true;
            for (int ring = 0; ring < 5; ring++)
            {
                var row = stamps.Where(s => s.Source == "Stamp " + (ring + 1)).ToList();
                int want = Tk(5.0f + 0.5f * ring);
                rimFirst &= row.Count == 6 && row.All(s => s.Tick - s0 + s.H.ResolveAt == want && Mathf.Abs(Sim.Flat(s.H.Origin).magnitude - GreatCasting.Rings[ring]) < 0.05f);
            }
            t.Check("stamps on rings 12.5..4.5 m, resolving rim-first at 5.0/5.5/6.0/6.5/7.0 s", rimFirst);
            float minGap = stamps.Min(s => Mathf.Abs(Mathf.DeltaAngle(pour.Yaw, Sim.YawTo(pour.Origin, s.H.Origin, 0))));
            t.Check("spokes are 30 deg off the pour", Mathf.Abs(minGap - 30f) < 0.5f, $"{minGap:0.0}");
            var p = t.Main;
            var ph = t.HitsOn(p, "Pour").ToList();
            t.Check("solo: the still target takes the pour as a 40 lane", ph.Count == 1 && ph[0].Damage == 40, ph.Count > 0 ? ph[0].Damage.ToString() : "no hit");
            if (ph.Count == 1) t.Note($"pour hit at +{ph[0].Tick - s0} ticks (resolve {Tk(3.5f)})");
            t.Check("solo: standing in the cooled pour channel, no stamp hits", !t.WasHit(p, "Stamp"));

            // ---- 2. solo: the pour tracks until lock, then the target sidesteps it at 4.2 m/s ----
            yield return t.Setup(1, Difficulty.Medium, at: new[] { new Vector3(0, 0, 10) });
            boss = t.Boss; if (boss == null) yield break;
            p = t.Main;
            t.Begin(boss.Casting.Run(0));
            t.In(p).Go(new Vector3(3, 0, 10));
            yield return t.Ticks(Tk(1.5f) + 1);
            var tracked = boss.Casting.Pour;
            float aimErr = Mathf.Abs(Mathf.DeltaAngle(tracked.Yaw, Sim.YawTo(tracked.Origin, p.transform.position, 0)));
            t.Check("the pour tracked the target until the lock", tracked.Locked && aimErr < 4f, $"{aimErr:0.0} deg off");
            var side = Quaternion.Euler(0, tracked.Yaw, 0) * Vector3.right;
            t.In(p).Go(p.transform.position + side * 2.1f);
            yield return t.WaitIdle();
            t.Check("solo: a 4.2 m/s sidestep after the lock dodges the pour", !t.WasHit(p, "Pour"));
            t.Check("phase 1 (spokes 0): no stamps", !t.Spawned("Stamp").Any());

            // ---- 3. still players on spokes: edge r 13, ranged r 8.5, melee r 4.5; the lone target takes 80 ----
            {
                var bz = new Vector3(0, 0, -1.5f);
                var far = new Vector3(0, 0, 13.2f);
                float yaw = Sim.YawTo(bz, far, 0);
                var edge = OnSpoke(bz, yaw, 0, 13.0f);
                var ranged = OnSpoke(bz, yaw, 4, 8.5f);
                var melee = OnSpoke(bz, yaw, 1, 4.5f);
                yield return t.Setup(4, Difficulty.Medium, at: new[] { far, edge, ranged, melee });
                boss = t.Boss; if (boss == null) yield break;
                t.Note($"edge {edge.magnitude:0.0} m, ranged {ranged.magnitude:0.0} m, melee {Vector3.Distance(melee, bz) - boss.BodyRadius:0.0} m from the body");
                yield return t.Fire(boss.Casting.Run(6));
                var P = t.Party;
                var lone = t.HitsOn(P[0], "Pour").ToList();
                t.Check("co-op: the target alone in the pour takes all 80 (and survives from full)", lone.Count == 1 && lone[0].Damage == 80 && !P[0].Down,
                    lone.Count > 0 ? lone[0].Damage.ToString() : "no hit");
                t.Check("still player on a spoke at the edge (r 13) is hit by the first row", t.HitsOn(P[1], "Stamp 1").Count() == 1);
                t.Check("still ranged player on a spoke at 8.5 m is hit", t.HitsOn(P[2], "Stamp 3").Count() == 1);
                t.Check("still melee player on a spoke at r 4.5 is hit by the last row (7.0 s)", t.HitsOn(P[3], "Stamp 5").Count() == 1);
                t.Check("nobody on a spoke takes the pour", !P.Skip(1).Any(q => t.WasHit(q, "Pour")));
                t.Check("one hit per instance", t.OneHitPerInstance());
            }

            // ---- 4. escapes at 4.2 m/s: join the pour from the far side; leave an r 4.5 stamp; leave a rim stamp ----
            {
                var bz = new Vector3(0, 0, -1.5f);
                var far = new Vector3(0, 0, 13.2f);
                float yaw = Sim.YawTo(bz, far, 0);
                var join = new Vector3(-0.6f, 0, 1.0f);                         // in the lane, just past the body
                var farSide = join + Quaternion.Euler(0, -110, 0) * Vector3.forward * 12.1f;   // 12.1 m away, behind and left of the boss
                var inner = OnSpoke(bz, yaw, 1, 4.5f);
                var innerOut = OnSpoke(bz, yaw - 30, 1, 4.5f);                 // the gap between spokes 0 and 1 on the same ring
                var rim = OnSpoke(bz, yaw, 4, 12.5f);
                var rimOut = rim + Quaternion.Euler(0, GreatCasting.SpokeYaw(yaw, 4) + 90, 0) * Vector3.forward * 2.0f;
                yield return t.Setup(4, Difficulty.Medium, at: new[] { far, farSide, inner, rim });
                boss = t.Boss; if (boss == null) yield break;
                var P = t.Party;
                t.Note($"far-side joiner starts {Vector3.Distance(farSide, join):0.0} m from the lane, {Vector3.Distance(farSide, bz):0.0} m from the boss");
                t.Begin(boss.Casting.Run(6));
                t.In(P[1]).Go(join);
                yield return t.Ticks(Tk(3.0f));
                t.In(P[2]).Go(innerOut);
                t.In(P[3]).Go(rimOut);
                yield return t.WaitIdle();
                var j0 = t.HitsOn(P[0], "Pour").ToList(); var j1 = t.HitsOn(P[1], "Pour").ToList();
                t.Check("join the pour from the far side at 4.2: both split it 40/40", j0.Count == 1 && j1.Count == 1 && j0[0].Damage == 40 && j1[0].Damage == 40,
                    $"{(j0.Count > 0 ? j0[0].Damage : 0)}/{(j1.Count > 0 ? j1[0].Damage : 0)}");
                t.Check("joiner then stands in the cooled channel: no stamp hits", !t.WasHit(P[1], "Stamp"));
                t.Check("leave an r 4.5 stamp into the gap at 4.2 m/s: no hit", !t.WasHit(P[2]), string.Join(",", t.HitsOn(P[2]).Select(h => h.Source)));
                t.Check("leave a rim stamp at 4.2 m/s (2.0 s warning): no hit", !t.WasHit(P[3]), string.Join(",", t.HitsOn(P[3]).Select(h => h.Source)));
                t.Check("walking costs no stamina (finite stamina untouched)", P.All(q => q.Stamina > 0));
            }

            // ---- 5. line split for N = 2 / 4 / 8, a partial stack, and an empty pour (no wipe) ----
            foreach (int n in new[] { 2, 4, 8 })
            {
                var at = Enumerable.Range(0, n).Select(i => i == 0 ? new Vector3(0, 0, 12.5f) : new Vector3((i % 2 == 0 ? 0.4f : -0.4f), 0, 2.5f + i * 1.1f)).ToArray();
                yield return t.Setup(n, Difficulty.Medium, at: at);
                boss = t.Boss; if (boss == null) yield break;
                yield return t.Fire(boss.Casting.Run(0));
                int each = Mathf.CeilToInt(80f / n);
                var hs = t.Hits.Where(h => h.Source == "Pour").ToList();
                t.Check($"N={n}: all {n} in the lane each take 80/{n} = {each}", hs.Count == n && hs.All(h => h.Damage == each) && t.Party.All(q => !q.Down),
                    string.Join(",", hs.Select(h => h.Damage)));
            }
            {
                var at = new[] { new Vector3(0, 0, 12.5f), new Vector3(0.3f, 0, 6f), new Vector3(6f, 0, 4f), new Vector3(-6f, 0, 4f) };
                yield return t.Setup(4, Difficulty.Medium, at: at);
                boss = t.Boss; if (boss == null) yield break;
                yield return t.Fire(boss.Casting.Run(0));
                var hs = t.Hits.Where(h => h.Source == "Pour").ToList();
                t.Check("N=4, 2 inside: 40 each, the two outside untouched", hs.Count == 2 && hs.All(h => h.Damage == 40 && (h.P == t.Party[0] || h.P == t.Party[1])),
                    string.Join(",", hs.Select(h => h.Damage)));
            }
            {
                yield return t.Setup(2, Difficulty.Medium, at: new[] { new Vector3(0, 0, 12.5f), new Vector3(6f, 0, 4f) });
                boss = t.Boss; if (boss == null) yield break;
                var P = t.Party;
                t.Begin(boss.Casting.Run(0));
                yield return t.Ticks(Tk(1.5f) + 1);
                var lane = boss.Casting.Pour;
                t.In(P[0]).Go(P[0].transform.position + Quaternion.Euler(0, lane.Yaw, 0) * Vector3.right * 2.4f);
                yield return t.WaitIdle();
                t.Check("empty pour: no damage and no wipe (line stack, not a soak)", !t.Hits.Any() && P.All(q => !q.Down));
            }

            // ---- 6. phase 2: pour + 3 spokes, every other one ----
            yield return t.Setup(1, Difficulty.Medium, at: new[] { new Vector3(0, 0, 10) });
            boss = t.Boss; if (boss == null) yield break;
            yield return t.Fire(boss.Casting.Run(3));
            {
                var st = t.Spawned("Stamp").ToList(); var lane = boss.Casting.Pour;
                var yaws = st.Where(s => s.Source == "Stamp 1").Select(s => Mathf.Repeat(Mathf.DeltaAngle(lane.Yaw, Sim.YawTo(lane.Origin, s.H.Origin, 0)), 360)).OrderBy(a => a).ToList();
                t.Check("phase 2: 15 stamps on 3 spokes 120 deg apart", st.Count == 15 && yaws.Count == 3 && Mathf.Abs(yaws[1] - yaws[0] - 120) < 1 && Mathf.Abs(yaws[2] - yaws[1] - 120) < 1,
                    string.Join(",", yaws.Select(a => a.ToString("0"))));
            }

            // ---- 7. Easy x1.25 on all times; Hard same times, more damage; at most two patterns at once ----
            yield return t.Setup(2, Difficulty.Easy, at: new[] { new Vector3(0, 0, 12.5f), new Vector3(0.4f, 0, 5f) });
            boss = t.Boss; if (boss == null) yield break;
            yield return t.Fire(boss.Casting.Run(6));
            {
                var d = Difficulty.Easy; var pr = t.Spawned("Pour").First(); var st = t.Spawned("Stamp").ToList();
                bool ok = pr.H.LockAt == Tk(1.5f, d) && pr.H.ResolveAt == Tk(3.5f, d) && st.All(s => s.Tick - pr.Tick == Tk(3.0f, d))
                    && Enumerable.Range(0, 5).All(r => st.Where(s => s.Source == "Stamp " + (r + 1)).All(s => s.Tick - pr.Tick + s.H.ResolveAt == Tk(5.0f + 0.5f * r, d)));
                t.Check("Easy: lock 1.875, resolve 4.375, stamps at 3.75, rows 6.25..8.75 s", ok);
                t.Note("Easy pour shares: " + string.Join(",", t.Hits.Where(h => h.Source == "Pour").Select(h => h.Damage)));
            }
            yield return t.Setup(2, Difficulty.Hard, at: new[] { new Vector3(0, 0, 12.5f), new Vector3(0.4f, 0, 5f) });
            boss = t.Boss; if (boss == null) yield break;
            yield return t.Fire(boss.Casting.Run(6));
            {
                var pr = t.Spawned("Pour").First(); var hs = t.Hits.Where(h => h.Source == "Pour").ToList();
                t.Check("Hard: same times as Medium", pr.H.LockAt == Tk(1.5f) && pr.H.ResolveAt == Tk(3.5f));
                t.Check("Hard: the 40 share lands as 52 (global x1.3)", hs.Count == 2 && hs.All(h => h.Damage == 52), string.Join(",", hs.Select(h => h.Damage)));
                int max = 0;
                foreach (var s in t.Spawns)
                    max = Mathf.Max(max, t.Spawns.Where(o => o.Tick <= s.Tick && s.Tick < o.Tick + o.H.ResolveAt).Select(o => o.Source.StartsWith("Pour") ? "Pour" : "March").Distinct().Count());
                t.Check("never more than two damaging patterns at once", max <= 2, $"{max}");
            }

            // ---- 8. boss death mid-windup cancels everything (last: the practice ends) ----
            yield return t.Setup(1, Difficulty.Medium, at: new[] { new Vector3(0, 0, 10) });
            boss = t.Boss; if (boss == null) yield break;
            t.Begin(boss.Casting.Run(6));
            yield return t.Ticks(Tk(3.2f));
            int liveBefore = boss.World.Hazards.Count;
            int killTick = t.W.Tick;
            t.KillBoss();
            yield return t.Seconds(5f);
            t.Check("boss death mid-windup: no hit afterwards, no hazard left", liveBefore > 1 && !t.Hits.Any(h => h.Tick >= killTick) && (t.W == null || t.W.Hazards.Count == 0),
                $"live before {liveBefore}");
            t.Check("one hit per instance (whole test)", t.OneHitPerInstance());
        }
    }
}
