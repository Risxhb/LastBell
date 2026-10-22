using System.Collections;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Bellows Draw (03-crucivar.bellows_draw.md). The boss is held at (0, 0, -1.5) by Setup; the safe band is 4.8 m from the
    // boss to 9.2 m from the centre. Checks the spawn/resolve ticks, the pull (1.5 m, no damage), still players at the wall,
    // at range and hugging the body, the escape at 4.2 m/s, N = 1/2/4/8, the Slag Rim part alone, the edge never stacking,
    // boss death mid-windup, and Easy/Hard.
    public sealed class BellowsDrawTest : CrucivarAbilityTest
    {
        public override string Id => "crucivar.bellows_draw";
        public override int Order => 30;

        static readonly Vector3 Band = new Vector3(0, 0, 6.5f);     // 8 m from the boss, 6.5 m from the centre
        static int Tk(float s, float scale = 1) => Sim.Ticks(s * scale);

        IEnumerator Cast(CrucivarV2AutoTest t, float after = 4.0f)
        {
            t.Begin(t.Boss.Bellows.Run());
            yield return t.WaitIdle();
            yield return t.Seconds(after);   // the edge outlives Run by 3.5 s
        }

        public override IEnumerator Run(CrucivarV2AutoTest t)
        {
            // ---- timing, solo, a still player in the band: never hit, pulled 1.5 m ----
            yield return t.Setup(1, Difficulty.Medium, new[] { Band });
            if (t.Boss == null) yield break;
            var main = t.Main; var start = main.transform.position; int hp0 = main.Hp;
            t.Begin(t.Boss.Bellows.Run());
            yield return t.Seconds(1.5f);
            yield return t.Shot("bellows_windup");
            yield return t.Until(() => t.Spawned("Slag Edge").Any(), 3, "slag edge spawned");
            yield return t.Seconds(0.45f);
            yield return t.Shot("bellows_inhale");
            yield return t.WaitIdle();
            var moved = Sim.Flat(start - main.transform.position);
            int edgeGone = -1;
            yield return t.Until(() => { if (edgeGone < 0 && t.Spawned("Slag Edge").Any() && !t.W.Hazards.Live.Any(h => h.Source == "Slag Edge")) edgeGone = t.W.Tick; return edgeGone >= 0; }, 8, "slag edge expired");

            var rim = t.Spawned("Slag Rim").FirstOrDefault(); var blast = t.Spawned("Bellows Blast").FirstOrDefault(); var edge = t.Spawned("Slag Edge").FirstOrDefault();
            bool all = rim != null && blast != null && edge != null;
            t.Check("rim, blast and edge each spawn once", all && t.Spawned("Slag Rim").Count() == 1 && t.Spawned("Bellows Blast").Count() == 1 && t.Spawned("Slag Edge").Count() == 1);
            if (all)
            {
                int t0 = rim.Tick;
                t.Check("Slag Rim: ring 9.5 m to the wall, 30 Ground, resolves at 2.0 s", rim.H.Shape == HazardShape.Ring && rim.H.Inner == 9.5f && rim.H.Radius >= CrucivarAbility.Rim
                    && rim.H.Damage == 30 && rim.H.Kind == HazardKind.Ground && rim.H.ResolveAt == Tk(2.0f), $"inner {rim.H.Inner} outer {rim.H.Radius} resolve {rim.H.ResolveAt}");
                t.Check("Blast spawns at 0.5 s and resolves at 3.5 s, r 4.5, 30 Ground, 1.5 m knockback",
                    Mathf.Abs(blast.Tick - t0 - Tk(0.5f)) <= 1 && blast.H.ResolveAt == Tk(3.0f) && blast.H.Radius == 4.5f && blast.H.Damage == 30 && blast.H.Knockback == 1.5f,
                    $"spawn +{blast.Tick - t0} resolve {blast.H.ResolveAt}");
                t.Check("Slag Edge burns 10/s from 2.0 s to 7.0 s, then expires", Mathf.Abs(edge.Tick - t0 - Tk(2.0f)) <= 1 && edge.H.BurnPerSecond == 10 && edge.H.ResolveAt == Tk(5.0f)
                    && Mathf.Abs(edgeGone - t0 - Tk(7.0f)) <= 4, $"spawn +{edge.Tick - t0}, gone +{edgeGone - t0}");
            }
            t.Check("solo, still in the safe band: never hit", !t.WasHit(main) && main.Hp == hp0, $"hp {hp0} -> {main.Hp}");
            t.Check("inhale pulls 1.5 m toward the boss, no damage", Mathf.Abs(moved.magnitude - 1.5f) <= 0.15f && Vector3.Dot(moved, Vector3.forward) > 0, $"moved {moved.magnitude:0.00} m");
            t.Check("never more than two damaging patterns (rim/edge + blast)", t.Spawns.Select(s => s.H.BurnPerSecond > 0 ? "Slag Rim" : s.Source).Distinct().Count() <= 2);

            // ---- still players: wall, ranged (10 m), hugging the body, and one in the band, N = 4 ----
            var wall = new Vector3(0, 0, 13.5f); var ranged = new Vector3(-10f, 0, -1.5f); var hug = new Vector3(0, 0, 0.6f);
            yield return t.Setup(4, Difficulty.Medium, new[] { wall, ranged, hug, Band });
            if (t.Boss == null) yield break;
            var hugStart = t.Party[2].transform.position;
            t.Begin(t.Boss.Bellows.Run());
            yield return t.Until(() => t.WasHit(t.Party[2], "Bellows Blast"), 5, "blast lands on the hugger");
            var pushed = Sim.Flat(t.Party[2].transform.position - hugStart).magnitude;
            yield return t.WaitIdle(); yield return t.Seconds(4.0f);
            t.Check("still at the wall: hit by the Slag Rim (30) and burned by the edge", t.HitsOn(t.Party[0], "Slag Rim").Count() == 1 && t.HitsOn(t.Party[0], "Slag Rim").First().Damage == 30
                && t.WasHit(t.Party[0], "Slag Edge"), $"rim {t.HitsOn(t.Party[0], "Slag Rim").Sum(h => h.Damage)}, edge {t.HitsOn(t.Party[0], "Slag Edge").Sum(h => h.Damage)}");
            t.Check("still at range (10 m from the centre, 8 m+ from the boss): hit by the Slag Rim", t.WasHit(t.Party[1], "Slag Rim"));
            t.Check("still hugging the body: hit by the Blast and knocked out", t.HitsOn(t.Party[2], "Bellows Blast").Count() == 1 && pushed >= 1.0f, $"pushed {pushed:0.00} m");
            t.Check("still in the band: untouched", !t.WasHit(t.Party[3]));
            t.Check("one hit per instance", t.OneHitPerInstance());
            int edgeDmg = t.HitsOn(t.Party[0], "Slag Edge").Sum(h => h.Damage);
            t.Check("edge burn at the wall is about 10/s for 5 s", edgeDmg >= 45 && edgeDmg <= 51, $"{edgeDmg}");

            // ---- escape at 4.2 m/s, walking only (no dash, no sprint): boss 2.9 m off centre, worst cases ----
            var bossAt = new Vector3(0, 0, -2.9f);
            yield return t.Setup(2, Difficulty.Medium, new[] { new Vector3(0, 0, 13.5f), new Vector3(0, 0, -5.0f) });
            if (t.Boss == null) yield break;
            t.Boss.transform.position = bossAt;
            float st0 = t.Party[0].Stamina + t.Party[1].Stamina;
            t.Begin(t.Boss.Bellows.Run());
            t.In(t.Party[0]).Go(new Vector3(0, 0, 7.0f), 4.2f);     // ranged at the wall walks in to the band
            t.In(t.Party[1]).Go(new Vector3(0, 0, -8.8f), 4.2f);    // melee on the far side of the body walks out
            yield return t.WaitIdle(); yield return t.Seconds(1f);
            t.In(t.Party[0]).Stop(); t.In(t.Party[1]).Stop();
            t.Check("escape at 4.2 m/s from the wall into the band: untouched", !t.WasHit(t.Party[0]), string.Join(",", t.HitsOn(t.Party[0]).Select(h => h.Source)));
            t.Check("escape at 4.2 m/s from the body (pulled back in, walks out again): untouched", !t.WasHit(t.Party[1]), string.Join(",", t.HitsOn(t.Party[1]).Select(h => h.Source)));
            t.Check("escape needs no stamina", t.Party[0].Stamina + t.Party[1].Stamina >= st0 - 0.01f);

            // ---- N = 2 / 4 / 8: the same rule for everyone; wall players hit, band players not ----
            foreach (int n in new[] { 2, 4, 8 })
            {
                var spots = Enumerable.Range(0, n).Select(i =>
                {
                    var d = Quaternion.Euler(0, 360f * i / n + 20, 0) * Vector3.forward;
                    return i % 2 == 0 ? d * 13.2f : new Vector3(0, 0, -1.5f) + d * 7f;
                }).ToArray();
                yield return t.Setup(n, Difficulty.Medium, spots);
                if (t.Boss == null) yield break;
                yield return Cast(t, 0.5f);
                bool ok = true;
                for (int i = 0; i < n; i++) ok &= i % 2 == 0 ? t.HitsOn(t.Party[i], "Slag Rim").Count() == 1 && t.HitsOn(t.Party[i], "Slag Rim").First().Damage == 30 : !t.WasHit(t.Party[i]);
                t.Check($"N = {n}: every wall player takes the same 30, every band player is untouched", ok && t.OneHitPerInstance());
            }

            // ---- the Slag Rim part alone: rim + edge, no inhale, no blast ----
            yield return t.Setup(2, Difficulty.Medium, new[] { new Vector3(13.2f, 0, 0), Band });
            if (t.Boss == null) yield break;
            var bandStart = t.Party[1].transform.position;
            t.Begin(RimOnly(t, false));
            yield return t.WaitIdle();
            t.Check("Slag Rim alone: rim + edge only, no blast, no pull", t.Spawned("Slag Rim").Count() == 1 && t.Spawned("Slag Edge").Count() == 1 && !t.Spawned("Bellows Blast").Any()
                && Sim.Flat(t.Party[1].transform.position - bandStart).magnitude < 0.05f && t.WasHit(t.Party[0], "Slag Rim"));

            // ---- two edges overlapping never stack: 10/s, not 20 ----
            yield return t.Setup(1, Difficulty.Medium, new[] { new Vector3(13.2f, 0, 0) });
            if (t.Boss == null) yield break;
            t.Begin(RimOnly(t, true));
            yield return t.WaitIdle();
            var burns = t.HitsOn(t.Main, "Slag Edge").ToList();
            int t1 = t.Spawned("Slag Edge").Skip(1).Select(s => s.Tick).DefaultIfEmpty(0).First();
            int both = burns.Where(b => b.Tick > t1 && b.Tick <= t1 + Sim.Hz).Sum(b => b.Damage);
            t.Check("two overlapping slag edges burn 10/s, not 20", t.Spawned("Slag Edge").Count() == 2 && both >= 9 && both <= 11, $"{both} in the overlapping second");

            // ---- boss death mid-windup cancels everything ----
            yield return t.Setup(2, Difficulty.Medium, new[] { new Vector3(0, 0, 13.5f), Band });
            if (t.Boss == null) yield break;
            var p1 = t.Party[1]; var p1At = p1.transform.position;
            t.Begin(t.Boss.Bellows.Run());
            yield return t.Seconds(1.5f);   // blast warning up, rim not resolved, inhale telegraph on
            int beforeKill = t.Hits.Count; int killTick = t.W.Tick;
            t.KillBoss();
            yield return t.Seconds(4f);
            t.Check("boss death mid-windup: no rim, blast or edge afterwards, no pull", t.Hits.Count == beforeKill && !t.Spawns.Any(s => s.Tick > killTick)
                && Sim.Flat(p1.transform.position - p1At).magnitude < 0.05f, $"hits after {t.Hits.Count - beforeKill}");

            // ---- Easy: every time x1.25; Hard: same times, x1.3 damage taken ----
            yield return t.Setup(1, Difficulty.Easy, new[] { new Vector3(0, 0, 13.5f) });
            if (t.Boss == null) yield break;
            yield return Cast(t, 5f);
            var er = t.Spawned("Slag Rim").FirstOrDefault(); var eb = t.Spawned("Bellows Blast").FirstOrDefault(); var ee = t.Spawned("Slag Edge").FirstOrDefault();
            t.Check("Easy: rim 2.5 s, blast 0.625 -> 4.375 s, edge to 8.75 s",
                er != null && eb != null && ee != null && er.H.ResolveAt == Tk(2.0f, 1.25f) && Mathf.Abs(eb.Tick - er.Tick - Tk(0.5f, 1.25f)) <= 1
                && eb.H.ResolveAt == Tk(3.5f, 1.25f) - Tk(0.5f, 1.25f) && ee.H.ResolveAt == Tk(7f, 1.25f) - Tk(2f, 1.25f),
                er == null ? "" : $"rim {er.H.ResolveAt}, blast +{eb?.Tick - er.Tick} res {eb?.H.ResolveAt}, edge {ee?.H.ResolveAt}");
            int easyDmg = t.HitsOn(t.Main, "Slag Rim").Sum(h => h.Damage);

            yield return t.Setup(1, Difficulty.Hard, new[] { new Vector3(0, 0, 13.5f) });
            if (t.Boss == null) yield break;
            yield return Cast(t, 0.5f);
            var hr = t.Spawned("Slag Rim").FirstOrDefault();
            int hardDmg = t.HitsOn(t.Main, "Slag Rim").Sum(h => h.Damage);
            t.Check("Hard: same times as Medium", hr != null && hr.H.ResolveAt == Tk(2.0f));
            t.Check("Hard: rim hit scaled by the global x1.3 (39)", hardDmg == Mathf.RoundToInt(30 * 1.3f), $"Easy {easyDmg}, Medium 30, Hard {hardDmg}");
        }

        // Slag Rim on its own as the phase plan pairs it; `twice` lays a second one 0.5 s after the first
        static System.Collections.Generic.IEnumerable<int> RimOnly(CrucivarV2AutoTest t, bool twice)
        {
            t.Boss.Bellows.SlagRim();
            if (twice) { yield return Sim.Ticks(0.5f); t.Boss.Bellows.SlagRim(); }
            yield return Sim.Ticks(4.0f);
        }
    }
}
