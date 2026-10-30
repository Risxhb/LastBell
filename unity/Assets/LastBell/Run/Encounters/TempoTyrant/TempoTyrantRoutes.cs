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
                float budget = k + 1 < stages.Count ? Mathf.Max(0, (stages[k + 1].seconds - Reaction) * speed) : 0;   // staying put needs no reaction
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

        // ================= Oct 28 ultimate rework: timelines (mirrors tools/tempo-tyrant/routes_check.py) =================
        // A timeline is a list of hazard events on exact encounter ticks, built from the encounter's own hazard builders
        // and windows. Events on the same tick are one stage. Each stage carries where the boss stands (his body blocks
        // paths and spots); "react" stages cost the 0.3 s reaction before a player moves (a predictable next step, like a
        // spiral step or the end of a charge already on the floor, does not). Standing still never needs a reaction.
        // Speeds: Walk 6 m/s (Normal), Stamina = sprint 9 m/s with PlayerController stamina simulated along a
        // minimal-distance witness route from every start (starts full; drain 25/s, regen 20/s after 0.75 s) (Haste).
        public enum Mode { Walk, Sprint, Stamina }

        public struct Ev { public int Tick; public System.Func<Vector3, bool> Hit; public Vector3 Boss; public bool React; public string Label; }

        public sealed class Timeline
        {
            public readonly Tempo Tempo; public readonly float Bpm; public readonly bool Cadence;
            public readonly List<Ev> Events = new List<Ev>();
            public Timeline(Tempo t, bool cadence) { var c = new TempoClock(); Tempo = t; Bpm = t == Tempo.Haste ? c.HasteBpm : c.NormalBpm; Cadence = cadence; }
            public int B((float H, float N) ordinary, (float H, float N) cad) => TempoClock.TicksFor(TempoTyrantEncounter.Of(Cadence ? cad : ordinary, Tempo), Bpm);
            public int Beats(float beats) => TempoClock.TicksFor(beats, Bpm);
            public void Add(int t, System.Func<Vector3, bool> h, Vector3 boss, string label, bool react = true) =>
                Events.Add(new Ev { Tick = t, Hit = h, Boss = Sim.Flat(boss), React = react, Label = label });

            // Threefold Severance planted at boss/yaw at tick T; open > 0 overrides the opening (ticks)
            public (int handoff, int end) Severance(int T, Vector3 boss, float yaw, int open = -1)
            {
                int o = open > 0 ? open : B(TempoTyrantEncounter.SevOpen, TempoTyrantEncounter.Cadence.SevOpen), g = B(TempoTyrantEncounter.SevGap, TempoTyrantEncounter.Cadence.SevGap);
                for (int n = 1; n <= 3; n++) Add(T + o + (n - 1) * g, TempoTyrantEncounter.SeveranceStrike(n, boss, yaw, 1.4f).Contains, boss, "sev" + n);
                return (T + o + g, T + o + 2 * g);
            }

            public (int handoff, int end) Crown(int T, Vector3 c, int ring = -1)
            {
                int r = ring > 0 ? ring : B(TempoTyrantEncounter.CrownRing, TempoTyrantEncounter.Cadence.CrownRing), i = B(TempoTyrantEncounter.CrownInnerWarn, TempoTyrantEncounter.Cadence.CrownInner);
                Add(T + r, new Hazard { Shape = HazardShape.Ring, Origin = c, Inner = TempoTyrantEncounter.CrownPocket, Radius = TempoTyrantEncounter.SevReach }.Contains, c, "ring");
                Add(T + r + i, new Hazard { Shape = HazardShape.Circle, Origin = c, Radius = TempoTyrantEncounter.CrownInner }.Contains, c, "inner");
                return (T + r, T + r + i);
            }

            // charges: (yaw toward the target, distance to the target), as TempoTyrantEncounter.Charge / WakePath
            public (int handoff, int lastFissure, Vector3 end) Wake(int T, Vector3 start, IList<(float yaw, float dist)> charges, int firstWarn = -1)
            {
                int t = T, lastF = T; var pos = Sim.Flat(start);
                int bs = B(TempoTyrantEncounter.Backswing, TempoTyrantEncounter.Cadence.Backswing), fw = B(TempoTyrantEncounter.FissureWarn, TempoTyrantEncounter.Cadence.FissureWarn);
                for (int k = 0; k < charges.Count; k++)
                {
                    int warn = k == 0 ? (firstWarn > 0 ? firstWarn : B(TempoTyrantEncounter.WakeWarn, TempoTyrantEncounter.Cadence.WakeWarn)) : B(TempoTyrantEncounter.WakeWarn2, TempoTyrantEncounter.Cadence.WakeWarn2);
                    float travel = Mathf.Clamp(charges[k].dist + TempoTyrantEncounter.WakeOvershoot, TempoTyrantEncounter.WakeMinTravel, TempoTyrantEncounter.WakeMaxTravel);
                    var end = TempoTyrantEncounter.ClampInside(pos, pos + Sim.Dir(charges[k].yaw) * travel, TempoTyrantEncounter.OwnBound);
                    float len = Sim.Flat(end - pos).magnitude, y = len > 1e-3f ? Sim.YawTo(pos, end, charges[k].yaw) : charges[k].yaw;
                    var lane = new Hazard { Shape = HazardShape.Lane, Origin = pos, Yaw = y, Length = len + 1.4f + TempoTyrantEncounter.WakeReach, Width = TempoTyrantEncounter.WakeWidth };
                    Add(t + warn, lane.Contains, pos, $"charge{k + 1}");
                    int arrive = t + warn + TempoTyrantEncounter.WakeTravelTicks;
                    Add(arrive, lane.Contains, end, $"charge{k + 1} end", false);
                    Add(arrive + bs, TempoTyrantEncounter.BackswingHazard(end, y).Contains, end, $"backswing{k + 1}");
                    var fis = Fissures(pos, end, Tempo == Tempo.Haste);
                    Add(arrive + fw, p => fis.Any(f => f.Contains(p)), end, $"fissure{k + 1}");
                    lastF = arrive + fw; t = arrive + bs; pos = end;
                }
                return (t, lastF, pos);
            }

            // Spiral Slices from the centre: first safe slice, spiral direction; indicate > 0 overrides the indicate (ticks)
            public (int handoff, int end) Slices(int T, int safe, int dir, int indicate = -1)
            {
                int t = T + (indicate > 0 ? indicate : B(TempoTyrantEncounter.SliceIndicate, TempoTyrantEncounter.Cadence.SliceIndicate));
                int step = B(TempoTyrantEncounter.SliceStep, TempoTyrantEncounter.Cadence.SliceStep), s = safe;
                for (int k = 0; k < TempoTyrantEncounter.SpiralSteps; k++)
                {
                    t += k == 0 ? B(TempoTyrantEncounter.SliceFirst, TempoTyrantEncounter.Cadence.SliceFirst) : step;
                    var set = Enumerable.Range(1, TempoTyrantEncounter.SliceCount - 1).Select(i => TempoTyrantEncounter.Wrap(s + i)).ToList();
                    Add(t, SliceSet(set), Vector3.zero, $"spiral{k + 1}", k == 0);
                    if (k < TempoTyrantEncounter.SpiralSteps - 1) s = TempoTyrantEncounter.Wrap(s + dir);
                }
                int par = s % 2;
                var a = Enumerable.Range(0, TempoTyrantEncounter.SliceCount).Where(i => i % 2 == par).ToList();
                var b = Enumerable.Range(0, TempoTyrantEncounter.SliceCount).Where(i => i % 2 != par).ToList();
                t += B(TempoTyrantEncounter.AltWarn, TempoTyrantEncounter.Cadence.AltWarn); Add(t, SliceSet(a), Vector3.zero, "every-other A"); int handoff = t;
                t += B(TempoTyrantEncounter.AltGap, TempoTyrantEncounter.Cadence.AltGap); Add(t, SliceSet(b), Vector3.zero, "every-other B");
                return (handoff, t);
            }

            // CentreUp from pos: ticks walking (1.6x his walk, WalkTo-style stop within 0.3 m) and where he plants
            public (int ticks, Vector3 plant) CentreWalk(Vector3 pos)
            {
                pos = Sim.Flat(pos);
                if (TempoTyrantEncounter.ArenaCentred(pos)) return (0, pos);
                var dest = pos.normalized * (TempoTyrantEncounter.CentreGate * 0.4f);
                float sp = (Tempo == Tempo.Haste ? TempoTyrantEncounter.HasteWalk : TempoTyrantEncounter.NormalWalk) * 1.6f;
                float d = (dest - pos).magnitude;
                return (Mathf.CeilToInt((d - 0.3f) / sp * Sim.Hz), dest + (pos - dest).normalized * 0.3f);
            }
        }

        static System.Func<Vector3, bool> SliceSet(List<int> idx)
        {
            var hs = idx.Select(i => TempoTyrantEncounter.SliceHazard(i, "", 0)).ToList();
            return p => hs.Any(h => h.Contains(p));
        }

        // ---------------- solver ----------------
        static Vector3[] tgrid; static float tgridBound = -1;
        static readonly Dictionary<Vector2Int, float[]> matrices = new Dictionary<Vector2Int, float[]>();
        static Vector3[] TGrid(float bound)
        {
            if (tgrid != null && Mathf.Approximately(tgridBound, bound)) return tgrid;
            var l = new List<Vector3>();
            for (float x = -bound; x <= bound + 1e-4f; x += 0.6f)
                for (float z = -bound; z <= bound + 1e-4f; z += 0.6f)
                    if (new Vector2(x, z).magnitude <= bound) l.Add(new Vector3(x, 0, z));
            tgrid = l.ToArray(); tgridBound = bound; matrices.Clear();
            return tgrid;
        }
        static float[] Matrix(Vector3 boss)
        {
            var key = new Vector2Int(Mathf.RoundToInt(boss.x * 100), Mathf.RoundToInt(boss.z * 100));
            if (matrices.TryGetValue(key, out var m)) return m;
            if (matrices.Count >= 6) matrices.Clear();   // ~7 MB each
            int n = tgrid.Length; m = new float[n * n];
            for (int i = 0; i < n; i++)
                for (int j = i; j < n; j++) { float v = PathLength(tgrid[i], tgrid[j], boss, BodyR); m[i * n + j] = v; m[j * n + i] = v; }
            matrices[key] = m; return m;
        }
        public static void ReleaseCaches() { matrices.Clear(); tgrid = null; tgridBound = -1; }

        const float StamMax = 100, StamDrain = 25, StamRegen = 20, StamDelay = 0.75f;
        // stamina along one witness route: per window [reaction idle][sprint only what a 6 m/s walk can't cover][walk/idle]
        static bool StaminaOk(List<float> dists, List<float> windows, List<float> reacts)
        {
            float stam = StamMax, since = 99;
            void Rest(float L) { float regen = Mathf.Max(0, L - Mathf.Max(0, StamDelay - since)); stam = Mathf.Min(StamMax, stam + StamRegen * regen); since += L; }
            for (int k = 0; k < dists.Count; k++)
            {
                float weff = Mathf.Max(0, windows[k] - reacts[k]);
                float s = Mathf.Max(0, (dists[k] - PlayerController.WalkSpeed * weff) / (PlayerController.SprintSpeed - PlayerController.WalkSpeed));
                if (s > weff + 1e-4f) return false;
                Rest(reacts[k]);
                if (s > 0) { if (StamDrain * s > stam + 1e-4f) return false; stam -= StamDrain * s; since = 0; }
                Rest(weff - s);
            }
            return true;
        }

        public static Result Solve(string name, Timeline tl, Mode mode, float bound, float startStep = 1.5f)
        {
            var r = new Result { Name = name };
            var pts = TGrid(bound); int n = pts.Length;
            // merge same-tick events into stages
            var evs = tl.Events.OrderBy(e => e.Tick).ToList();
            var st = new List<Ev>();
            foreach (var e in evs)
            {
                if (st.Count > 0 && st[st.Count - 1].Tick == e.Tick)
                {
                    var p = st[st.Count - 1]; var f0 = p.Hit; var f1 = e.Hit;
                    st[st.Count - 1] = new Ev { Tick = p.Tick, Hit = q => f0(q) || f1(q), Boss = p.Boss, React = p.React || e.React, Label = p.Label + "+" + e.Label };
                }
                else st.Add(e);
            }
            float speed = mode == Mode.Walk ? PlayerController.WalkSpeed : PlayerController.SprintSpeed;
            int S = st.Count; var V = new bool[S][]; var budget = new float[S];
            for (int k = S - 1; k >= 0; k--)
            {
                var cur = new bool[n];
                for (int i = 0; i < n; i++) cur[i] = Sim.Flat(pts[i] - st[k].Boss).magnitude >= BodyR && !st[k].Hit(pts[i]);
                if (k + 1 < S)
                {
                    budget[k + 1] = Mathf.Max(0, ((st[k + 1].Tick - st[k].Tick) / (float)Sim.Hz - (st[k + 1].React ? Reaction : 0)) * speed);   // staying put needs no reaction
                    var M = Matrix(st[k + 1].Boss); var nx = V[k + 1]; float b = budget[k + 1] + 1e-4f;
                    var nxIdx = Enumerable.Range(0, n).Where(j => nx[j]).ToArray();
                    for (int i = 0; i < n; i++)
                    {
                        if (!cur[i]) continue;
                        bool ok = false; int row = i * n;
                        foreach (var j in nxIdx) if (M[row + j] <= b) { ok = true; break; }
                        cur[i] = ok;
                    }
                }
                V[k] = cur;
                if (!cur.Any(v => v)) { r.Failures.Add($"{name}: no route at stage {st[k].Label}"); r.WorstSlack = -99; r.Starts = 1; return r; }
            }
            var b0 = st[0].Boss; float t0 = st[0].Tick / (float)Sim.Hz;
            var v0 = Enumerable.Range(0, n).Where(j => V[0][j]).ToArray();
            foreach (var s0 in Starts(bound, b0, startStep))
            {
                r.Starts++;
                float best = float.MinValue; int bestJ = -1; float bestD = float.MaxValue;
                foreach (var j in v0)
                {
                    float d = PathLength(s0, pts[j], b0, BodyR);
                    float slack = t0 - (d / speed + (d > 0.3f ? Reaction : 0));
                    if (slack > best) best = slack;
                    if (slack >= 0 && d < bestD) { bestD = d; bestJ = j; }   // witness: the nearest feasible first spot
                }
                bool ok = best >= 0;
                if (ok && mode == Mode.Stamina)
                {
                    var dists = new List<float> { bestD }; var wins = new List<float> { t0 }; var reacts = new List<float> { bestD > 0.3f ? Reaction : 0 };
                    int j = bestJ;
                    for (int k = 1; k < S; k++)
                    {
                        var M = Matrix(st[k].Boss); float bd = float.MaxValue; int bj = -1; int row = j * n;
                        for (int q = 0; q < n; q++) if (V[k][q] && M[row + q] <= budget[k] + 1e-4f && M[row + q] < bd) { bd = M[row + q]; bj = q; }
                        if (bj < 0) { ok = false; break; }
                        dists.Add(bd); wins.Add((st[k].Tick - st[k - 1].Tick) / (float)Sim.Hz); reacts.Add(st[k].React ? Reaction : 0);
                        j = bj;
                    }
                    if (ok && !StaminaOk(dists, wins, reacts)) { ok = false; best = Mathf.Min(best, -0.01f); r.Failures.Add($"{name}: start {s0} runs out of stamina"); }
                }
                if (ok) r.Feasible++;
                else if (best < 0) r.Failures.Add($"{name}: start {s0} misses by {-best:0.00} s");
                if (best < r.WorstSlack) { r.WorstSlack = best; r.WorstStart = s0; r.WorstBoss = b0; }
            }
            return r;
        }

        // ---------------- the Oct 28 checks (sampled geometry; routes_check.py runs the full sweep) ----------------
        public static readonly Vector3[] SevBosses = { Vector3.zero, new Vector3(1.5f, 0, 0), new Vector3(0, 0, 1.5f), new Vector3(-1.06f, 0, -1.06f), new Vector3(1.06f, 0, 1.06f) };
        public static Mode ModeFor(Tempo t) => t == Tempo.Haste ? Mode.Stamina : Mode.Walk;

        // charges from the centre: charge 1 toward a target at `dist`, then (Haste) charge 2 re-targeting from the landing
        // point: rel 180 = straight back through the centre
        public static List<List<(float yaw, float dist)>> WakeCharges(Tempo t, bool quick)
        {
            var list = new List<List<(float, float)>>();
            int charges = t == Tempo.Haste ? TempoTyrantEncounter.HasteCharges : TempoTyrantEncounter.NormalCharges;
            float[] rels = quick ? new[] { 180f, 135f } : new[] { 180f, 135f, 225f, 100f, 260f };
            for (int y = 0; y < 360; y += quick ? 60 : 30)
                foreach (float dist in quick ? new[] { 13f } : new[] { 2f, 13f })
                {
                    if (charges < 2) { list.Add(new List<(float, float)> { (y, dist) }); continue; }
                    float travel = Mathf.Clamp(dist + TempoTyrantEncounter.WakeOvershoot, TempoTyrantEncounter.WakeMinTravel, TempoTyrantEncounter.WakeMaxTravel);
                    var end = TempoTyrantEncounter.ClampInside(Vector3.zero, Sim.Dir(y) * travel, TempoTyrantEncounter.OwnBound);
                    float back = Sim.YawTo(end, Vector3.zero, y + 180);
                    foreach (var rel in rels) list.Add(new List<(float, float)> { (y, dist), (Mathf.Repeat(back + rel - 180, 360), 13f) });
                }
            return list;
        }

        static Result Merge(string name, IEnumerable<Result> rs)
        {
            var m = new Result { Name = name };
            foreach (var r in rs)
            {
                m.Starts += r.Starts; m.Feasible += r.Feasible; m.Failures.AddRange(r.Failures.Take(2));
                if (r.WorstSlack < m.WorstSlack) { m.WorstSlack = r.WorstSlack; m.WorstStart = r.WorstStart; m.WorstBoss = r.WorstBoss; }
            }
            return m;
        }

        // Section 1: the reworked Sundering Wake in ordinary blocks (charge, backswing, fissure (+mirror), charge again)
        public static Result WakeRework(Tempo t, float bound, bool quick = true) =>
            Merge($"Wake rework ({t})", WakeCharges(t, quick).Select(ch => { var tl = new Timeline(t, false); tl.Wake(0, Vector3.zero, ch); return Solve("wake", tl, ModeFor(t), bound); }));

        static IEnumerable<object> Geometries(TempoTyrantEncounter.Atk a, Tempo t, bool quick, (TempoTyrantEncounter.Atk atk, Vector3 boss, float yaw)? after = null)
        {
            switch (a)
            {
                case TempoTyrantEncounter.Atk.Severance:
                    foreach (var b in after == null && !quick ? SevBosses : new[] { Vector3.zero })
                        for (int y = 0; y < 360; y += quick ? 90 : 45) yield return (b, (float)y);
                    break;
                case TempoTyrantEncounter.Atk.Crown: yield return null; break;
                case TempoTyrantEncounter.Atk.Wake: foreach (var ch in WakeCharges(t, true)) yield return ch; break;
                default:
                    if (after.HasValue && after.Value.atk == TempoTyrantEncounter.Atk.Severance)
                    {
                        int s = TempoTyrantEncounter.BehindSlice(after.Value.boss, after.Value.yaw);
                        yield return (s, 1); yield return (s, -1);
                    }
                    else for (int s = 0; s < 12; s += quick ? 3 : 1) { yield return (s, 1); yield return (s, -1); }
                    break;
            }
        }

        // appends an attack; returns its handoff tick and what a following attack needs (Wake: last fissure + landing)
        static int Append(Timeline tl, TempoTyrantEncounter.Atk a, int T, object g, int first, out int lastFissure, out Vector3 end)
        {
            lastFissure = -1; end = Vector3.zero;
            switch (a)
            {
                case TempoTyrantEncounter.Atk.Severance: { var (b, y) = ((Vector3, float))g; return tl.Severance(T, b, y, first).handoff; }
                case TempoTyrantEncounter.Atk.Crown: return tl.Crown(T, Vector3.zero, first).handoff;
                case TempoTyrantEncounter.Atk.Wake: { var w = tl.Wake(T, Vector3.zero, (List<(float, float)>)g, first); lastFissure = w.lastFissure; end = w.end; return w.handoff; }
                default: { var (s, d) = ((int, int))g; return tl.Slices(T, s, d, first).handoff; }
            }
        }

        // Section 3: one Broken Cadence attack on its own (it can open a chain), cadence windows
        public static Result CadenceAlone(TempoTyrantEncounter.Atk a, Tempo t, float bound, bool quick = true) =>
            Merge($"Broken Cadence {a} alone ({t})", Geometries(a, t, quick).Select(g =>
            {
                var tl = new Timeline(t, true); Append(tl, a, 0, g, -1, out _, out _); return Solve(a.ToString(), tl, ModeFor(t), bound);
            }));

        // Section 3: a pairing: the first attack in full, the second starting at its handoff with the pairing's join window
        public static Result CadencePair(TempoTyrantEncounter.Atk a, TempoTyrantEncounter.Atk b, Tempo t, float bound, bool quick = true)
        {
            var results = new List<Result>();
            float joinBeats = TempoTyrantEncounter.Of(TempoTyrantEncounter.Cadence.Join(a, b), t);
            foreach (var ga in Geometries(a, t, quick))
            {
                (TempoTyrantEncounter.Atk, Vector3, float)? after = null;
                if (a == TempoTyrantEncounter.Atk.Severance) { var (bp, by) = ((Vector3, float))ga; after = (a, bp, by); }
                foreach (var gb in Geometries(b, t, true, after))
                {
                    var tl = new Timeline(t, true);
                    int h = Append(tl, a, 0, ga, -1, out int lastF, out Vector3 end);
                    int join = tl.Beats(joinBeats);
                    if (a == TempoTyrantEncounter.Atk.Wake && b == TempoTyrantEncounter.Atk.Severance)
                    {
                        var (walk, plant) = tl.CentreWalk(end); int tp = h + walk; var (_, y) = ((Vector3, float))gb;
                        int open = Mathf.Max(join, lastF - tp + tl.B((TempoTyrantEncounter.FissureLeadBeats, TempoTyrantEncounter.FissureLeadBeats), TempoTyrantEncounter.Cadence.FissureLead));
                        tl.Severance(tp, plant, y, open);
                    }
                    else Append(tl, b, h, gb, join, out _, out _);
                    results.Add(Solve($"{a}->{b}", tl, ModeFor(t), bound, 2f));
                }
            }
            return Merge($"Broken Cadence {a} -> {b} ({t}, join {joinBeats} beats)", results);
        }
    }
}
