using System;
using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // The Pendulum Sentinel's pure rules: shapes, placements, timings and the walking-route checks the acceptance test
    // runs over every start position. No scene state; the encounter and the test both call these.
    public static class PendulumSentinelRules
    {
        public static float Bound = 13.9f;                      // player bound of the arena (set from RunMaps by the encounter)
        public const float BodyRadius = 1.3f;
        public static readonly float[] Arcs = { 180f, 150f, 120f, 90f };  // Shortening Swing, swing by swing (widened 27 Oct)
        public const float WeightRadius = 4f, WeightDist = 8f, WeightDistSolo = 5.5f;
        public const float MarkMin = 6f, MarkMax = 10f;
        public const float StrokeWidth = 5.5f, RearRadius = 9f;   // widened 27 Oct (were 4 and 7)
        public const float Reaction = 0.3f;                     // seconds before a player starts to move (content rules §2 checks)
        public const float CheckSpeed = 4.2f;                   // the slower of the two escape-check speeds (content rules §2)
        public const float LungeStop = 3f;                      // he lunges until this far from his target

        // ---------------- Shortening Swing ----------------
        // He lunges at his target first, then swing 1 (a half-arena arc) gets exactly as long as the worst-placed player
        // inside it needs to walk out at 4.2 m/s, rounded up to a half beat, never under the floor. Swings 2-4 are fixed
        // short side-steps, tuned so every start position survives the whole combo at 4.2 and 6 m/s.
        public static float FirstFloorBeats(Difficulty d) => d == Difficulty.Easy ? 2.5f : 1.5f;
        public static float[] SwingRestBeats(Difficulty d, bool phase2)
        {
            var b = phase2 ? new[] { 2f, 2f, 1.75f } : new[] { 1.75f, 1.5f, 1.5f };
            if (d == Difficulty.Easy) for (int i = 0; i < b.Length; i++) b[i] += 1f;
            return b;
        }
        public static float[] SwingWarnBeats(Difficulty d, bool phase2, float firstBeats)
        {
            var rest = SwingRestBeats(d, phase2);
            return new[] { firstBeats, rest[0], rest[1], rest[2] };
        }
        // where his lunge ends: toward the target until LungeStop from them, never past the boss bound
        public static Vector3 LungeEnd(Vector3 boss, Vector3 target)
        {
            var v = Sim.Flat(target - boss); float d = v.magnitude;
            if (d < 1e-3f) return Sim.Flat(boss);
            var q = Sim.Flat(boss) + v / d * Mathf.Max(0, d - LungeStop);
            if (q.magnitude > CombatWorld.BossBound) q = q.normalized * CombatWorld.BossBound;
            return q;
        }
        // swing 1's warning: the longest walk out of the 180-degree arc among the listed players, at 4.2 m/s after the
        // reaction, plus a quarter second, rounded up to a half beat; Easy adds a beat
        public static float FirstSwingBeats(Vector3 boss, float yaw, IEnumerable<Vector3> players, float bpm, Difficulty d)
        {
            float need = 0;
            foreach (var p in players)
                if (ConeContains(boss, yaw, 90, 99, p)) need = Mathf.Max(need, Sim.Flat(EscapePoint(boss, yaw, 90, p) - p).magnitude);
            float beats = Mathf.Ceil((need / CheckSpeed + Reaction + 0.25f) / (60f / bpm) * 2f) / 2f;
            if (d == Difficulty.Easy) beats += 1f;
            return Mathf.Max(FirstFloorBeats(d), beats);
        }
        public static int[] ToTicks(float[] beats, int beatTicks)
        {
            var t = new int[beats.Length];
            for (int i = 0; i < beats.Length; i++) t[i] = Mathf.Max(1, Mathf.RoundToInt(beats[i] * beatTicks));
            return t;
        }

        // Lengths of the chained swing clips: clip k passes the middle (the strike) exactly on swing k's hit, and starts
        // the tick clip k-1 ends, so the pendulum never jumps. With warnings w (ticks): D0/2 + ... works out to
        // D(k+1) = 2 w(k+1) - D(k); D0 is the middle of the range that keeps every clip at least `min` ticks long.
        public static int[] SwingClipTicks(int[] warns, int min = 6)
        {
            int n = warns.Length; var a = new double[n]; var s = new double[n];
            a[0] = 0; s[0] = 1;
            for (int k = 1; k < n; k++) { a[k] = 2.0 * warns[k] - a[k - 1]; s[k] = -s[k - 1]; }
            double lo = min, hi = 2.0 * warns[0];   // clip 0 must start after the cast does
            for (int k = 0; k < n; k++)
            {
                if (s[k] > 0) lo = Math.Max(lo, (min - a[k]) / s[k]);
                else hi = Math.Min(hi, (a[k] - min) / -s[k]);
            }
            double d0 = lo <= hi ? (lo + hi) / 2 : lo;
            var d = new int[n];
            for (int k = 0; k < n; k++) d[k] = Mathf.Max(1, (int)Math.Round(a[k] + s[k] * d0));
            return d;
        }

        public static bool ConeContains(Vector3 origin, float yaw, float half, float radius, Vector3 p)
        {
            var v = Sim.Flat(p - origin); float d = v.magnitude;
            if (d > radius + Hazard.Margin) return false;
            if (d < 0.6f) return true;
            return Vector3.Angle(Sim.Dir(yaw), v) <= half + Mathf.Rad2Deg * Mathf.Atan2(Hazard.Margin, d);
        }

        // nearest place a player at p can stand outside a cone aimed from `boss` (clear of its border and the boss body)
        public static Vector3 EscapePoint(Vector3 boss, float yaw, float half, Vector3 p, float clear = 0.45f)
        {
            var v = Sim.Flat(p - boss); var axis = Sim.Dir(yaw);
            float body = BodyRadius + PlayerController.Radius + 0.2f;
            Vector3 best = p; float bestD = float.MaxValue;
            foreach (float sgn in new[] { -1f, 1f })
            {
                // the border ray on this side, and the outward normal off it
                var edge = Sim.Dir(yaw + sgn * half); var normal = Sim.Dir(yaw + sgn * (half + 90f));
                float along = Mathf.Max(body, Vector3.Dot(v, edge));
                var foot = boss + edge * along + normal * (clear + Hazard.Margin);
                if (Sim.Flat(foot - boss).magnitude < body) foot = boss + Sim.Flat(foot - boss).normalized * body;
                float dd = Sim.Flat(foot - p).magnitude;
                if (dd < bestD) { bestD = dd; best = foot; }
            }
            return Clamp(best);
        }

        public static Vector3 Clamp(Vector3 p, float margin = 0.2f)
        {
            p.y = 0; var f = new Vector2(p.x, p.z); float m = Bound - margin;
            if (f.magnitude > m) { f = f.normalized * m; p.x = f.x; p.z = f.y; }
            return p;
        }

        // A walking player who reads each swing and steps to its nearest safe spot: does every swing of the combo leave
        // time to get there? The target is the walker (solo, the worst case: every arc is aimed at them).
        public static bool SwingComboSurvivable(Vector3 boss, Vector3 start, float[] warnBeats, float bpm, float speed, bool backswing, out string why)
        {
            why = ""; var p = start; float beatSec = 60f / bpm;
            for (int k = 0; k < Arcs.Length; k++)
            {
                float yaw = Sim.YawTo(boss, p, 0), half = Arcs[k] / 2;
                if (!ConeContains(boss, yaw, half, 99, p)) continue;
                var safe = EscapePoint(boss, yaw, half, p);
                if (backswing && k >= 2 && ConeContains(boss, yaw + 180, half, 99, safe)) { why = $"swing {k + 1}: safe spot is in the backswing"; return false; }
                if (ConeContains(boss, yaw, half, 99, safe)) { why = $"swing {k + 1}: no safe spot outside the arc"; return false; }
                float need = Sim.Flat(safe - p).magnitude, have = speed * Mathf.Max(0, warnBeats[k] * beatSec - Reaction);
                if (need > have) { why = $"swing {k + 1}: needs {need:0.0} m, has {have:0.0} m"; return false; }
                p = safe;
            }
            return true;
        }

        // ---------------- Counterweight Balance ----------------
        // three landing spots, one along each arm (front right, front left, back), clamped inside the floor
        public static List<Vector3> WeightSpots(Vector3 from, float yaw, bool solo)
        {
            var list = new List<Vector3>(); float dist = solo ? WeightDistSolo : WeightDist;
            foreach (float off in new[] { -60f, 60f, 180f })
            {
                var p = Sim.Flat(from) + Sim.Dir(yaw + off) * dist;
                list.Add(Clamp(p, WeightRadius + 0.3f));
            }
            return list;
        }
        // landing delays: all three together with 3+ players; the front two then the back one with 2; one by one solo
        public const int WeightGapBeats = 2;
        public static int[] WeightDelays(int standing, int warn, int beatTicks)
        {
            int gap = WeightGapBeats * beatTicks;
            if (standing <= 1) return new[] { warn, warn + gap, warn + 2 * gap };
            if (standing == 2) return new[] { warn, warn, warn + gap };
            return new[] { warn, warn, warn };
        }
        public static int Share(int total, int catchers) => catchers <= 0 ? 0 : Mathf.CeilToInt(total / (float)catchers);
        // the weight's landing warning: long enough to walk out from under its centre at 4.2 m/s (P2 needs 3 beats at 112)
        public static float WeightWarnBeats(Difficulty d, bool phase2) => (phase2 ? 3f : 2.5f) + (d == Difficulty.Easy ? 1f : 0f);
        // the walk out from a weight's centre: past its border, its margin and the player's body
        public static float WeightEscape => WeightRadius + Hazard.Margin + PlayerController.Radius;
        // after landing an unclaimed weight lies on the floor as a soak for this long; then it bursts into bouncing orbs
        public static float SoakBeats(Difficulty d) => d == Difficulty.Easy ? 4f : 3f;
        public const int BouncesMin = 3, BouncesMax = 4;
        // an orb hitting the floor's edge reflects off it (angle in = angle out)
        public static Vector3 Reflect(Vector3 vel, Vector3 pos) { var n = Sim.Flat(pos).normalized; return vel - 2f * Vector3.Dot(vel, n) * n; }

        // ---------------- Plumb Line ----------------
        // four small marks (room for 8): with only three, the worst floor spot is 12 m from a mark, too far for the short
        // countdown; with four 90 degrees apart it is 9 m
        public static int MarkCount(int players) => Mathf.Max(4, (players + 1) / 2);
        // marks 6-10 m from the centre, spread evenly round it (+-15 degrees for 3), so at least 90 degrees apart up to 4 marks
        public static List<Vector3> PlumbMarks(System.Random r, int count, Vector3 centre)
        {
            var list = new List<Vector3>(); float a0 = (float)(r.NextDouble() * 360.0), step = 360f / count;
            for (int i = 0; i < count; i++)
            {
                float jitter = step >= 120f ? (float)(r.NextDouble() * 30.0 - 15.0) : 0f;   // 3 marks: 90-150 apart; 4: exactly 90
                float a = a0 + i * step + jitter;
                float d = MarkMin + (float)r.NextDouble() * (MarkMax - MarkMin);
                list.Add(Clamp(Sim.Flat(centre) + Sim.Dir(a) * d, 2.6f));
            }
            return list;
        }
        public static Vector3 Drift(Vector3 mark, Vector3 centre, float deg) => Sim.Flat(centre) + Quaternion.Euler(0, deg, 0) * Sim.Flat(mark - centre);
        public static int MarkIndex(List<Vector3> marks, float radius, Vector3 p)
        {
            for (int i = 0; i < marks.Count; i++) if (Sim.Flat(p - marks[i]).magnitude <= radius + Hazard.Margin) return i;
            return -1;
        }
        // can a walker anywhere on the floor reach a mark's final spot in time?
        public static float WorstReach(List<Vector3> finalMarks, float radius)
        {
            float worst = 0;
            for (float x = -Bound; x <= Bound; x += 1f)
                for (float z = -Bound; z <= Bound; z += 1f)
                {
                    var p = new Vector3(x, 0, z); if (new Vector2(x, z).magnitude > Bound || new Vector2(x, z).magnitude < BodyRadius + 0.5f) continue;
                    float best = float.MaxValue;
                    foreach (var m in finalMarks) best = Mathf.Min(best, Mathf.Max(0, Sim.Flat(m - p).magnitude - (radius - 0.4f)));
                    worst = Mathf.Max(worst, best);
                }
            return worst;
        }

        // ---------------- Return Stroke ----------------
        // the lane runs from him to the floor's edge; his body stops at the boss bound (content rules §7: inside 9.5 m)
        public static (Vector3 dir, float yaw, float length, Vector3 end) StrokeLane(Vector3 from, Vector3 target, Vector3 fallbackDir)
        {
            var dir = Sim.Flat(target - from); if (dir.sqrMagnitude < 1) dir = Sim.Flat(fallbackDir); dir.Normalize();
            float len = EdgeDistance(from, dir, Bound) + 0.5f;
            var end = Sim.Flat(from) + dir * EdgeDistance(from, dir, CombatWorld.BossBound);
            return (dir, Sim.YawTo(from, from + dir, 0), len, end);
        }
        public static float EdgeDistance(Vector3 from, Vector3 dir, float radius)
        {
            var p = new Vector2(from.x, from.z); var d = new Vector2(dir.x, dir.z).normalized;
            float b = Vector2.Dot(p, d), c = p.sqrMagnitude - radius * radius;
            return Mathf.Max(0, -b + Mathf.Sqrt(Mathf.Max(0, b * b - c)));
        }
        public static bool LaneContains(Vector3 from, float yaw, float length, float width, Vector3 p)
        {
            var l = Quaternion.Euler(0, -yaw, 0) * Sim.Flat(p - from);
            return Mathf.Abs(l.x) <= width / 2 + Hazard.Margin && l.z >= -Hazard.Margin && l.z <= length + Hazard.Margin;
        }
        // the cleave behind him after he lands: a half-circle round where he stopped, on the side he came from
        public static bool CleaveContains(Vector3 landing, float yaw, Vector3 p) => ConeContains(landing, yaw + 180, 90, RearRadius, p);

        // Stroke timings in beats: lane shown, the charge itself, and the cleave's delay after he lands.
        public static (float show, float dash, float cleave) StrokeBeats(Difficulty d, bool phase2)
        {
            float show = phase2 ? 2.5f : 2f, dash = 0.75f, cleave = 0.75f;   // the wider lane needs 2 / 2.5 beats to walk out of
            if (d == Difficulty.Easy) { show += 1f; cleave += 0.5f; }
            return (show, dash, cleave);
        }

        sealed class Danger { public bool Lane; public Vector3 From; public float Yaw, Length; public float At; }
        static bool Inside(Danger h, Vector3 p) => h.Lane ? LaneContains(h.From, h.Yaw, h.Length, StrokeWidth + 0.3f, p) : CleaveContains(h.From, h.Yaw, p);

        // A walker who is the target of every stroke (solo, the worst case): at each lane's warning they pick the nearest
        // spot that, walking straight there after the reaction, keeps them out of the lane when it fires (counted from the
        // end of its warning, the earliest his body can reach anyone), out of this stroke's cleave, and out of the last
        // stroke's cleave if it is still pending. Strokes chain: each new lane starts the moment he lands.
        public static bool StrokeChainSurvivable(Vector3 boss, Vector3 start, int count, float bpm, float speed, Difficulty d, out string why)
        {
            why = ""; var (show, dash, cleave) = StrokeBeats(d, bpm > 100f);
            float bs = 60f / bpm, t = 0; var p = Sim.Flat(start); var from = Sim.Flat(boss);
            Danger pending = null;
            for (int k = 0; k < count; k++)
            {
                var (dir, yaw, len, end) = StrokeLane(from, p, Vector3.forward);
                float land = t + (show + dash) * bs;
                var list = new List<Danger> { new Danger { Lane = true, From = from, Yaw = yaw, Length = len, At = t + show * bs },
                                              new Danger { From = end, Yaw = yaw, At = land + cleave * bs } };
                if (pending != null && pending.At > t) list.Add(pending);
                if (!WalkClear(p, t, list, speed, from, out var q)) { why = $"stroke {k + 1} from ({p.x:0.0},{p.z:0.0})"; return false; }
                p = q; pending = list[1]; from = end; t = land;
            }
            return true;
        }
        static bool WalkClear(Vector3 p, float t0, List<Danger> list, float speed, Vector3 body, out Vector3 best)
        {
            best = p; float bestD = float.MaxValue;
            for (float a = 0; a < 360; a += 6)
                for (float r = 0; r < 16f; r += 0.25f)
                {
                    if (r >= bestD) break;
                    var q = Clamp(p + Sim.Dir(a) * r);
                    if (Sim.Flat(q - body).magnitude < BodyRadius + PlayerController.Radius) continue;
                    float dist = Sim.Flat(q - p).magnitude; bool ok = true;
                    foreach (var h in list)
                    {
                        float walked = Mathf.Max(0, h.At - (t0 + Reaction)) * speed;
                        var at = walked >= dist ? q : p + (q - p) * (walked / Mathf.Max(dist, 1e-4f));
                        if (Inside(h, at)) { ok = false; break; }
                    }
                    if (ok) { if (dist < bestD) { bestD = dist; best = q; } break; }
                }
            return bestD < float.MaxValue;
        }
    }
}
