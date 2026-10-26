using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Whole-route feasibility for The Tempo Tyrant's sequenced attacks, computed from the authoritative hazard builders
    // (Hazard.Contains, the same test the HazardSystem resolves with). For every sampled legal start position it walks a
    // greedy route: from where the player stands, the shortest path (around the boss body, inside the floor bound) to the
    // nearest point that is safe from the next strike, and must arrive before that strike resolves minus a reaction
    // allowance. Greedy routes are a witness, so a pass is conservative; failures are reported with their start point.
    //   speed: walk 6 m/s (no sprint, no dash, no immunity: none is required) unless stated otherwise.
    public static class TempoTyrantRoutes
    {
        public sealed class Result
        {
            public string Name; public int Starts, Feasible; public float WorstSlack = float.MaxValue; public Vector3 WorstStart, WorstBoss;
            public readonly List<string> Failures = new List<string>();
            public override string ToString() => $"{Name}: {Feasible}/{Starts} start positions feasible, worst slack {WorstSlack:0.00} s at start {WorstStart} (boss {WorstBoss})";
        }

        public const float Reaction = 0.3f;
        static Tempo TempoOf(float bpm) => bpm >= 190 ? Tempo.Haste : Tempo.Normal;
        static float BodyR => 1.4f + PlayerController.Radius;   // TempoTyrant body + player body (CombatWorld.Constrain)

        // path length from a to b avoiding the boss body circle (centre c, radius r)
        public static float PathLength(Vector3 a, Vector3 b, Vector3 c, float r)
        {
            var A = new Vector2(a.x - c.x, a.z - c.z); var B = new Vector2(b.x - c.x, b.z - c.z);
            var d = B - A; float len = d.magnitude; if (len < 1e-4f) return 0;
            float t = Mathf.Clamp01(-Vector2.Dot(A, d) / (len * len));
            if ((A + d * t).magnitude >= r - 1e-3f) return len;
            float la = Mathf.Max(A.magnitude, r), lb = Mathf.Max(B.magnitude, r);
            float ta = Mathf.Sqrt(Mathf.Max(0, la * la - r * r)), tb = Mathf.Sqrt(Mathf.Max(0, lb * lb - r * r));
            float arc = Vector2.Angle(A, B) * Mathf.Deg2Rad - Mathf.Acos(Mathf.Clamp(r / la, -1, 1)) - Mathf.Acos(Mathf.Clamp(r / lb, -1, 1));
            return ta + tb + r * Mathf.Max(0, arc);
        }

        static readonly List<Vector3> grid = new List<Vector3>();
        static float gridBound = -1;
        static List<Vector3> Grid(float bound)
        {
            if (Mathf.Approximately(gridBound, bound) && grid.Count > 0) return grid;
            grid.Clear(); gridBound = bound;
            for (float x = -bound; x <= bound; x += 0.4f)
                for (float z = -bound; z <= bound; z += 0.4f)
                    if (new Vector2(x, z).magnitude <= bound) grid.Add(new Vector3(x, 0, z));
            return grid;
        }

        // nearest (by path) point satisfying `safe`, or null
        static Vector3? Nearest(Vector3 from, Vector3 boss, float bound, System.Func<Vector3, bool> safe, out float dist)
        {
            dist = float.MaxValue; Vector3? best = null;
            foreach (var p in Grid(bound))
            {
                if (Sim.Flat(p - boss).magnitude < BodyR) continue;
                if (!safe(p)) continue;
                float L = PathLength(from, p, boss, BodyR);
                if (L < dist) { dist = L; best = p; }
            }
            return best;
        }

        static IEnumerable<Vector3> Starts(float bound, Vector3 boss, float step)
        {
            for (float x = -bound; x <= bound; x += step)
                for (float z = -bound; z <= bound; z += step)
                {
                    var p = new Vector3(x, 0, z);
                    if (new Vector2(x, z).magnitude > bound || Sim.Flat(p - boss).magnitude < BodyR) continue;
                    yield return p;
                }
            // wall-adjacent ring
            for (int i = 0; i < 24; i++) yield return Quaternion.Euler(0, i * 15, 0) * Vector3.forward * (bound - 0.05f);
        }

        // Each stage: a hazard that must be avoided, and the seconds available since the previous stage resolved.
        // Exact on a 0.6 m grid: computed backwards, a point is "viable" for stage k when it is safe from hazard k and
        // (unless last) some point viable for stage k+1 is reachable from it within that stage's time minus reaction.
        // A start is feasible when a stage-0-viable point is reachable in the first window.
        public static Result Sequence(string name, Vector3 boss, IList<(Hazard h, float seconds)> stages, float speed, float bound, float startStep = 1.5f) =>
            Sequence(name, boss, stages.Select(st => ((System.Func<Vector3, bool>)st.h.Contains, st.seconds)).ToList(), speed, bound, startStep);

        // A stage can be several hazards resolving together (the fissure and its mirror): unsafe if any contains the point.
        public static Result Sequence(string name, Vector3 boss, IList<(System.Func<Vector3, bool> h, float seconds)> stages, float speed, float bound, float startStep = 1.5f)
        {
            var r = new Result { Name = name };
            var pts = new List<Vector3>();
            for (float x = -bound; x <= bound; x += 0.6f)
                for (float z = -bound; z <= bound; z += 0.6f)
                {
                    var p = new Vector3(x, 0, z);
                    if (new Vector2(x, z).magnitude <= bound && Sim.Flat(p - boss).magnitude >= BodyR) pts.Add(p);
                }
            int n = pts.Count; bool[] next = null, after0 = null;
            for (int k = stages.Count - 1; k >= 0; k--)
            {
                if (k == 0) after0 = next;
                var cur = new bool[n]; var hit = stages[k].h;
                float budget = k + 1 < stages.Count ? (stages[k + 1].seconds - Reaction) * speed : 0;
                for (int i = 0; i < n; i++)
                {
                    if (hit(pts[i])) continue;
                    if (next == null) { cur[i] = true; continue; }
                    for (int j = 0; j < n && !cur[i]; j++)
                        if (next[j] && PathLength(pts[i], pts[j], boss, BodyR) <= budget) cur[i] = true;
                }
                next = cur;
                if (!next.Any(v => v)) { r.Failures.Add($"{name}: no viable point for stage {k + 1}"); break; }
            }
            foreach (var s0 in Starts(bound, boss, startStep))
            {
                r.Starts++;
                float best = float.MinValue;
                // the start itself can be the stage-0 position (no move needed) when it is safe and continues
                if (!stages[0].h(s0))
                {
                    bool cont = after0 == null;
                    float b1 = stages.Count > 1 ? (stages[1].seconds - Reaction) * speed : 0;
                    for (int j = 0; j < n && !cont; j++) if (after0[j] && PathLength(s0, pts[j], boss, BodyR) <= b1) cont = true;
                    if (cont) best = stages[0].seconds;
                }
                for (int j = 0; j < n; j++)
                {
                    if (!next[j]) continue;
                    float dist = PathLength(s0, pts[j], boss, BodyR);
                    float slack = stages[0].seconds - (dist / speed + (dist > 0.3f ? Reaction : 0));
                    if (slack > best) best = slack;
                }
                if (best >= 0) r.Feasible++;
                else r.Failures.Add($"{name}: start {s0} misses by {-best:0.00} s");
                if (best < r.WorstSlack) { r.WorstSlack = best; r.WorstStart = s0; r.WorstBoss = boss; }
            }
            return r;
        }

        public static Result Severance(Vector3 boss, float yaw, float bpm, float speed, float bound)
        {
            var stages = new List<(Hazard, float)>();
            for (int n = 1; n <= 3; n++)
            {
                var h = TempoTyrantEncounter.SeveranceStrike(n, boss, yaw, 1.4f);
                float beats = TempoTyrantEncounter.Of(n == 1 ? TempoTyrantEncounter.SevOpen : TempoTyrantEncounter.SevGap, TempoOf(bpm));
                stages.Add((h, TempoClock.TicksFor(beats, bpm) / (float)Sim.Hz));
            }
            return Sequence($"Severance boss {boss} yaw {yaw:0} @{bpm:0}", boss, stages, speed, bound);
        }

        public static Result Crown(Vector3 boss, float bpm, float speed, float bound)
        {
            var ring = new Hazard { Shape = HazardShape.Ring, Origin = boss, Inner = TempoTyrantEncounter.CrownPocket, Radius = TempoTyrantEncounter.SevReach };
            var inner = new Hazard { Shape = HazardShape.Circle, Origin = boss, Radius = TempoTyrantEncounter.CrownInner };
            var stages = new List<(Hazard, float)>
            {
                (ring, TempoClock.TicksFor(TempoTyrantEncounter.Of(TempoTyrantEncounter.CrownRing, TempoOf(bpm)), bpm) / (float)Sim.Hz),
                (inner, TempoClock.TicksFor(TempoTyrantEncounter.Of(TempoTyrantEncounter.CrownInnerWarn, TempoOf(bpm)), bpm) / (float)Sim.Hz),
            };
            return Sequence($"Hollow Crown boss {boss} @{bpm:0}", boss, stages, speed, bound);
        }

        // The chain: after the charge the fissure is warning while the Severance opening runs. Stage 0 = be off the
        // fissure when it erupts (fissureBeats after the 1-2-3 preview appears), then strike 1 leadBeats later.
        // mirrored: in Haste the fissure has a reflected twin (TempoTyrantEncounter.MirrorLane) erupting on the same tick
        public static Result Chain(Vector3 chargeStart, Vector3 boss, float yaw, float bpm, float speed, float bound, float fissureBeats, float leadBeats, bool mirrored = false)
        {
            var fissures = Fissures(chargeStart, boss, mirrored);
            var stages = new List<(System.Func<Vector3, bool>, float)> { ((System.Func<Vector3, bool>)(p => fissures.Any(f => f.Contains(p))), TempoClock.TicksFor(fissureBeats, bpm) / (float)Sim.Hz) };
            for (int n = 1; n <= 3; n++)
            {
                var h = TempoTyrantEncounter.SeveranceStrike(n, boss, yaw, 1.4f);
                float beats = n == 1 ? leadBeats : TempoTyrantEncounter.Of(TempoTyrantEncounter.SevGap, TempoOf(bpm));
                stages.Add(((System.Func<Vector3, bool>)h.Contains, TempoClock.TicksFor(beats, bpm) / (float)Sim.Hz));
            }
            return Sequence($"Chain fissure{(fissures.Count > 1 ? "+mirror" : "")} {chargeStart}->{boss} yaw {yaw:0} @{bpm:0} ({fissureBeats}+{leadBeats} beats)", boss, stages, speed, bound, 2f);
        }

        // the fissure lane from a charge, plus its mirror when requested and not skipped (same rule as the encounter)
        public static List<Hazard> Fissures(Vector3 chargeStart, Vector3 chargeEnd, bool mirrored)
        {
            float len = Sim.Flat(chargeEnd - chargeStart).magnitude, yaw = Sim.YawTo(chargeStart, chargeEnd, 0);
            var list = new List<Hazard> { new Hazard { Shape = HazardShape.Lane, Origin = chargeStart, Yaw = yaw, Length = len, Width = TempoTyrantEncounter.FissureWidth } };
            if (mirrored && TempoTyrantEncounter.MirrorUseful(yaw))
            {
                var (o, y) = TempoTyrantEncounter.MirrorLane(chargeStart, yaw);
                list.Add(new Hazard { Shape = HazardShape.Lane, Origin = o, Yaw = y, Length = len, Width = TempoTyrantEncounter.FissureWidth });
            }
            return list;
        }

        // Mirrored Wake on its own: after the charge ends, every start must get off both lanes within the fissure
        // warning alone (the dashed mirror preview, shown since the charge warning began, is not counted).
        public static Result MirroredWake(Vector3 chargeStart, Vector3 chargeEnd, float bpm, float speed, float bound)
        {
            var fissures = Fissures(chargeStart, chargeEnd, true);
            float secs = TempoClock.TicksFor(TempoTyrantEncounter.Of(TempoTyrantEncounter.FissureWarn, TempoOf(bpm)), bpm) / (float)Sim.Hz;
            var stages = new List<(System.Func<Vector3, bool>, float)> { ((System.Func<Vector3, bool>)(p => fissures.Any(f => f.Contains(p))), secs) };
            return Sequence($"Mirrored Wake {chargeStart}->{chargeEnd} @{bpm:0}", chargeEnd, stages, speed, bound);
        }
    }
}
