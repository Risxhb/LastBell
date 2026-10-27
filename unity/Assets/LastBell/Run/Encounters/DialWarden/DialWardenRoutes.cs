using System;
using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Pure rules for the Dial Warden (docs/bosses/dial-warden.md): pattern generators, the exact hit tests the encounter
    // uses, and a walking-route check. No Unity objects, so the acceptance test can run thousands of casts.
    //
    // Route check: the floor disc is a 0.5 m grid and time runs in 9-tick steps (a player walking at 80% speed covers
    // 0.72 m per step, one cell including diagonals). Working backwards from the end, a cell is safe in a step when no
    // hit lands on it (or on the neighbour it walks to) during that step and that neighbour is safe in the next step.
    // Every hit area is padded by 15 cm. The pattern is survivable when the set of safe starting cells is not empty and
    // every standable cell can walk to one of them in the lead time the players are given.
    public static class DialWardenRoutes
    {
        public static float Bound = 13.9f;                    // the floor players stand on (CombatWorld.PlayerBound)
        public const float BodyClear = 1.75f;                 // inside this a player is against his body: not standable
        public const float Cell = 0.5f, Walk = PlayerController.WalkSpeed * 0.8f;
        public const int StepTicks = 9;

        // ---------------- the dial ----------------
        public const float HourRadius = 10f, SafeRadius = 4f, PadRadius = 1.4f, HandHalfWidth = 0.6f;
        // hour 12 is north (-z, the top of the screen), hour 3 east (+x): clockwise as players see it
        public static float HourYaw(int hour) => 180f - 30f * hour;
        public static Vector3 HourPos(int hour) => Sim.Dir(HourYaw(hour)) * HourRadius;
        // the hand starts at 12; after `steps` hours it points at (clockwise) 12+steps or (counter-clockwise) 12-steps
        public static float HandYaw(float steps, bool clockwise) => 180f + (clockwise ? -30f : 30f) * steps;
        public static int HourAtStep(int step, bool clockwise) => ((clockwise ? step : 12 - step) % 12 + 12) % 12;

        // lit hours as lap steps (beats after the hand starts), 3..11, at least `spacing` apart
        public static List<int> LitSteps(System.Random r, int count, int spacing = 2)
        {
            count = Mathf.Clamp(count, 1, 4);
            for (int guard = 0; guard < 200; guard++)
            {
                var pool = new List<int> { 3, 4, 5, 6, 7, 8, 9, 10, 11 };
                var pick = new List<int>();
                while (pick.Count < count && pool.Count > 0)
                {
                    int s = pool[r.Next(pool.Count)]; pool.Remove(s);
                    bool ok = true; foreach (var q in pick) if (Mathf.Abs(q - s) < spacing) ok = false;
                    if (ok) pick.Add(s);
                }
                if (pick.Count == count) { pick.Sort(); return pick; }
            }
            var fallback = new List<int>(); for (int i = 0; i < count; i++) fallback.Add(3 + i * spacing); return fallback;
        }

        // does the hand, moving from yaw a to yaw b this tick, pass over p? (the band from `inner` out to the edge)
        public static bool HandCrosses(Vector3 p, float a, float b, float inner)
        {
            float d = new Vector2(p.x, p.z).magnitude;
            if (d < inner - Hazard.Margin) return false;
            float tol = Mathf.Rad2Deg * Mathf.Atan2(HandHalfWidth + Hazard.Margin, Mathf.Max(0.3f, d));
            float py = Sim.YawTo(Vector3.zero, p, 0);
            float sweep = Mathf.DeltaAngle(a, b);                 // signed travel this tick
            float off = Mathf.DeltaAngle(a, py);                  // where p sits relative to the start of the travel
            if (sweep < 0) { sweep = -sweep; off = -off; }
            return off >= -tol && off <= sweep + tol;
        }

        // ---------------- Constellation ----------------
        public const float StarRadius = 1.5f, BeamWidth = 1.2f, StarSpacing = 4f, StarArea = 10f, StarCentreClear = 3f;
        public struct Fire { public int Tick; public Vector3 A, B; public bool Silver; }

        public static List<Vector3> Stars(System.Random r, int n, List<Vector3> avoid, float spacing = StarSpacing)
        {
            for (int guard = 0; guard < 100; guard++)
            {
                var list = new List<Vector3>();
                for (int tries = 0; tries < 800 && list.Count < n; tries++)
                {
                    float a = (float)(r.NextDouble() * 360), d = Mathf.Sqrt((float)r.NextDouble()) * StarArea;
                    if (d < StarCentreClear) continue;
                    var p = Sim.Dir(a) * d; bool ok = true;
                    foreach (var q in list) if ((q - p).magnitude < spacing) ok = false;
                    if (avoid != null) foreach (var q in avoid) if ((q - p).magnitude < spacing * 0.85f) ok = false;
                    if (ok) list.Add(p);
                }
                if (list.Count == n) return list;
            }
            return null;
        }

        // one constellation fires in drawn order, one segment per beat; two fire on alternating beats, the second
        // (silver) in reverse order
        public static List<Fire> Fires(List<Vector3> gold, List<Vector3> silver, int beat)
        {
            var f = new List<Fire>();
            if (silver == null)
            {
                for (int k = 0; k + 1 < gold.Count; k++) f.Add(new Fire { Tick = k * beat, A = gold[k], B = gold[k + 1] });
                return f;
            }
            int segs = Mathf.Max(gold.Count, silver.Count) - 1;
            for (int k = 0; k < segs; k++)
            {
                if (k + 1 < gold.Count) f.Add(new Fire { Tick = 2 * k * beat, A = gold[k], B = gold[k + 1] });
                int j = silver.Count - 1 - k;
                if (j - 1 >= 0) f.Add(new Fire { Tick = (2 * k + 1) * beat, A = silver[j], B = silver[j - 1], Silver = true });
            }
            return f;
        }

        public static Hazard BeamShape(Fire f)
        {
            var d = Sim.Flat(f.B - f.A);
            return new Hazard { Shape = HazardShape.Lane, Origin = f.A, Yaw = Sim.YawTo(f.A, f.B, 0), Length = d.magnitude, Width = BeamWidth };
        }
        public static bool FireHits(Fire f, Vector3 p, Hazard beam = null)
        {
            if (Sim.Flat(p - f.A).magnitude <= StarRadius + Hazard.Margin || Sim.Flat(p - f.B).magnitude <= StarRadius + Hazard.Margin) return true;
            return (beam ?? BeamShape(f)).Contains(p);
        }

        // ---------------- Mainspring ----------------
        public const float WaveStart = 2f, WaveHalf = 0.6f, ImplosionRadius = 4.5f;
        public sealed class Wave
        {
            public int Start;                 // tick the wave leaves his body (relative to the timeline)
            public float[] OutGaps, InGaps;   // gap centres (yaw)
            public float GapHalf;             // half the gap angle; 0 = no gaps (Midnight)
            public float VOut, VIn;           // m/s
            public int ImplosionDelay;        // ticks after the wave reaches him
            public float Edge => Bound + Hazard.Margin;
            public int OutTicks => Mathf.CeilToInt((Edge - WaveStart) / VOut * Sim.Hz);
            public int InTicks => Mathf.CeilToInt((Edge - WaveStart) / VIn * Sim.Hz);
            public int Implosion => Start + OutTicks + InTicks + ImplosionDelay;
            public int End => Implosion;
            // ring radius at tick t (absolute), and whether it is on its way out; false when not travelling
            public bool At(int t, out float r, out bool outward)
            {
                int u = t - Start; r = 0; outward = true;
                if (u < 0 || u > OutTicks + InTicks) return false;
                if (u <= OutTicks) { r = WaveStart + VOut * u / Sim.Hz; return true; }
                outward = false; r = Edge - VIn * (u - OutTicks) / Sim.Hz; return true;
            }
        }

        public static bool InGap(float[] gaps, float half, Vector3 p, float pad = 0)
        {
            if (gaps == null || half <= 0) return false;
            float d = new Vector2(p.x, p.z).magnitude; if (d < 0.05f) return false;
            float room = half - Mathf.Rad2Deg * Mathf.Atan2(Hazard.Margin + pad, d);   // the whole body must fit inside the gap
            float py = Sim.YawTo(Vector3.zero, p, 0);
            foreach (var g in gaps) if (Mathf.Abs(Mathf.DeltaAngle(py, g)) <= room) return true;
            return false;
        }
        public static bool WaveHits(Wave w, int t, Vector3 p)
        {
            if (!w.At(t, out float r, out bool outward)) return false;
            float d = new Vector2(p.x, p.z).magnitude;
            if (Mathf.Abs(d - r) > WaveHalf + Hazard.Margin) return false;
            return !InGap(outward ? w.OutGaps : w.InGaps, w.GapHalf, p);
        }
        public static bool ImplosionHits(Wave w, Vector3 p) => new Vector2(p.x, p.z).magnitude <= ImplosionRadius + Hazard.Margin;

        public static float[] EvenGaps(System.Random r, int n, float jitter = 10f)
        {
            float a0 = (float)(r.NextDouble() * 360); var g = new float[n];
            for (int i = 0; i < n; i++) g[i] = a0 + i * 360f / n + (float)(r.NextDouble() * 2 - 1) * jitter;
            return g;
        }
        public static float[] Turned(float[] gaps, float by) { var g = new float[gaps.Length]; for (int i = 0; i < g.Length; i++) g[i] = gaps[i] + by; return g; }

        // ---------------- Tip the Scales ----------------
        public const float WeightRadius = 2.5f, WeightMin = 5f, WeightMax = 9f, PairMaxApart = 120f, MirrorMin = 4f;
        public static Vector3 WeightSpot(System.Random r, Vector3? previous)
        {
            for (int guard = 0; guard < 200; guard++)
            {
                float a = (float)(r.NextDouble() * 360), d = Mathf.Lerp(WeightMin, WeightMax, (float)r.NextDouble());
                var p = Sim.Dir(a) * d;
                if (previous.HasValue && Vector3.Angle(Sim.Flat(previous.Value), p) < 90f) continue;
                return p;
            }
            return Sim.Dir((float)(r.NextDouble() * 360)) * WeightMin;
        }
        public static Vector3 PairedSpot(System.Random r, Vector3 first)
        {
            for (int guard = 0; guard < 200; guard++)
            {
                var p = WeightSpot(r, null);
                float apart = Vector3.Angle(Sim.Flat(first), p);
                if (apart >= 45f && apart <= PairMaxApart && (p - first).magnitude >= 2 * WeightRadius + 1f) return p;
            }
            return Quaternion.Euler(0, 90, 0) * first;
        }
        public static Vector3 Mirror(Vector3 a, Vector3? b = null)
        {
            var m = b.HasValue ? -(Sim.Flat(a) + Sim.Flat(b.Value)) / 2 : -Sim.Flat(a);
            if (m.magnitude < MirrorMin) m = (m.sqrMagnitude > 1e-4f ? m.normalized : Vector3.Cross(Vector3.up, Sim.Flat(a)).normalized) * MirrorMin;
            return m;
        }
        public static Vector3 Centroid(IEnumerable<PlayerController> players, out int n)
        {
            var c = Vector3.zero; n = 0;
            foreach (var p in players) if (p && !p.Down) { c += Sim.Flat(p.transform.position); n++; }
            return n > 0 ? c / n : Vector3.zero;
        }

        // ---------------- route check ----------------
        public const float Pad = 0.15f;   // the check treats every hit area as this much larger: players move between cell centres

        // Everything that can hit a player in one timeline (ticks from the timeline start).
        public sealed class Timeline
        {
            public readonly List<Fire> Fires = new List<Fire>();
            public readonly List<Wave> Waves = new List<Wave>();
            readonly List<Hazard> beams = new List<Hazard>();
            public int End { get { int e = 0; foreach (var f in Fires) e = Mathf.Max(e, f.Tick); foreach (var w in Waves) e = Mathf.Max(e, w.End); return e; } }
            public void Prepare()
            {
                beams.Clear();
                foreach (var f in Fires)
                {
                    var b = BeamShape(f); b.Origin -= Sim.Dir(b.Yaw) * Pad; b.Length += 2 * Pad; b.Width += 2 * Pad; beams.Add(b);
                }
            }
            // is p hit at any tick in [t0, t1]?
            public bool HitStep(int t0, int t1, Vector3 p)
            {
                float d = new Vector2(p.x, p.z).magnitude;
                for (int i = 0; i < Fires.Count; i++)
                {
                    var f = Fires[i]; if (f.Tick < t0 || f.Tick > t1) continue;
                    float r = StarRadius + Hazard.Margin + Pad;
                    if (Sim.Flat(p - f.A).magnitude <= r || Sim.Flat(p - f.B).magnitude <= r || beams[i].Contains(p)) return true;
                }
                foreach (var w in Waves)
                {
                    if (w.Implosion >= t0 && w.Implosion <= t1 && d <= ImplosionRadius + Hazard.Margin + Pad) return true;
                    int a = Mathf.Max(t0, w.Start), b = Mathf.Min(t1, w.Start + w.OutTicks + w.InTicks);
                    if (a > b) continue;
                    if (BandHits(w, a, b, true, p, d) || BandHits(w, a, b, false, p, d)) return true;
                }
                return false;
            }
            // the band's sweep over ticks a..b in one direction of travel
            static bool BandHits(Wave w, int a, int b, bool outward, Vector3 p, float d)
            {
                int turn = w.Start + w.OutTicks;
                int lo = outward ? a : Mathf.Max(a, turn + 1), hi = outward ? Mathf.Min(b, turn) : b;
                if (lo > hi) return false;
                w.At(lo, out float r0, out _); w.At(hi, out float r1, out _);
                float pad = WaveHalf + Hazard.Margin + Pad;
                if (d < Mathf.Min(r0, r1) - pad || d > Mathf.Max(r0, r1) + pad) return false;
                return !InGap(outward ? w.OutGaps : w.InGaps, w.GapHalf, p, Pad);
            }
        }

        public struct RouteResult { public bool Ok; public int StartCells, Cells; public float FarthestWalk, Lead; }

        public sealed class Grid
        {
            public int N, Cells, Steps; public float O;
            public bool[] Stand; public Vector3[] Pos;
            public bool[][] Safe;   // per step: a player can be on this cell at the start of the step and live
        }

        // Backwards over 9-tick steps: a cell is safe in step s when nothing hits it during s and it has a neighbour
        // (or itself) that is also clear during s and safe in step s+1, so the walk from one to the other happens
        // entirely on clear ground.
        public static Grid Solve(Timeline tl)
        {
            tl.Prepare();
            int n = Mathf.CeilToInt(Bound / Cell) * 2 + 1; float o = -(n - 1) / 2f * Cell;
            var g = new Grid { N = n, O = o, Stand = new bool[n * n], Pos = new Vector3[n * n] };
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++)
            {
                var p = new Vector3(o + i * Cell, 0, o + j * Cell); float d = new Vector2(p.x, p.z).magnitude;
                int c = i * n + j; g.Pos[c] = p; g.Stand[c] = d <= Bound && d >= BodyClear; if (g.Stand[c]) g.Cells++;
            }
            int end = tl.End, steps = end / StepTicks + 1; g.Steps = steps; g.Safe = new bool[steps][];
            var clear = new bool[n * n];
            bool[] next = null;
            for (int s = steps - 1; s >= 0; s--)
            {
                int t0 = s * StepTicks, t1 = Mathf.Min(end, t0 + StepTicks - 1);
                for (int c = 0; c < clear.Length; c++) clear[c] = g.Stand[c] && !tl.HitStep(t0, t1, g.Pos[c]);
                var safe = new bool[n * n];
                for (int i = 0; i < n; i++) for (int j = 0; j < n; j++)
                {
                    int c = i * n + j; if (!clear[c]) continue;
                    if (next == null) { safe[c] = true; continue; }
                    for (int di = -1; di <= 1 && !safe[c]; di++) for (int dj = -1; dj <= 1 && !safe[c]; dj++)
                    {
                        int a = i + di, b = j + dj; if (a < 0 || b < 0 || a >= n || b >= n) continue;
                        int k = a * n + b; if (clear[k] && next[k]) safe[c] = true;
                    }
                }
                g.Safe[s] = safe; next = safe;
            }
            return g;
        }

        // lead: ticks the players have between seeing the whole pattern and its first hit
        public static RouteResult Check(Timeline tl, int leadTicks)
        {
            var g = Solve(tl); int n = g.N;
            var start = g.Safe[0]; var dist = new float[n * n]; int starts = 0;
            for (int c = 0; c < dist.Length; c++) { dist[c] = float.MaxValue; if (start[c]) { dist[c] = 0; starts++; } }
            // multi-source walking distance (8 neighbours), relaxed until stable: the grid is small
            bool changed = starts > 0;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < n; i++) for (int j = 0; j < n; j++)
                {
                    int c = i * n + j; if (!g.Stand[c]) continue;
                    for (int di = -1; di <= 1; di++) for (int dj = -1; dj <= 1; dj++)
                    {
                        int a = i + di, b = j + dj; if ((di == 0 && dj == 0) || a < 0 || b < 0 || a >= n || b >= n || !g.Stand[a * n + b]) continue;
                        float nd = dist[a * n + b] + Cell * (di != 0 && dj != 0 ? 1.41421f : 1f);
                        if (nd < dist[c] - 1e-4f) { dist[c] = nd; changed = true; }
                    }
                }
            }
            float far = 0; for (int c = 0; c < dist.Length; c++) if (g.Stand[c]) far = Mathf.Max(far, dist[c]);
            float lead = Walk * leadTicks / Sim.Hz;
            return new RouteResult { Ok = starts > 0 && far <= lead, StartCells = starts, Cells = g.Cells, FarthestWalk = far, Lead = lead };
        }

        // One surviving walk: the cell to be on at the start of each step, beginning at the safe start cell nearest
        // `from` and staying put whenever it can. Null when nothing is survivable.
        public static List<Vector3> Plan(Grid g, Vector3 from)
        {
            int n = g.N, best = -1; float bd = float.MaxValue;
            for (int c = 0; c < g.Pos.Length; c++) if (g.Safe[0][c]) { float d = Sim.Flat(g.Pos[c] - from).sqrMagnitude; if (d < bd) { bd = d; best = c; } }
            if (best < 0) return null;
            var path = new List<Vector3> { g.Pos[best] }; int cur = best;
            for (int s = 1; s < g.Steps; s++)
            {
                if (!g.Safe[s][cur])
                {
                    int i = cur / n, j = cur % n, pick = -1;
                    for (int di = -1; di <= 1 && pick < 0; di++) for (int dj = -1; dj <= 1 && pick < 0; dj++)
                    {
                        int a = i + di, b = j + dj; if (a < 0 || b < 0 || a >= n || b >= n) continue;
                        if (g.Safe[s][a * n + b]) pick = a * n + b;
                    }
                    if (pick < 0) return path;   // cannot happen when Safe[s-1][cur] held
                    cur = pick;
                }
                path.Add(g.Pos[cur]);
            }
            return path;
        }
    }
}
