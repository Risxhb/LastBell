using System;
using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Pure, scene-free rules for the Briar Metronome (docs/bosses/briar-metronome.md): seasons, the cut chain, pens and
    // the walking checks the acceptance test runs. The boss always fights from the arena centre.
    public static class BriarMetronomeRules
    {
        public enum Cut { Out, In, Across }

        // ---------------- geometry (Medium) ----------------
        public const float OutRadius = 6f, InInner = 3.5f, AcrossHalf = 45f, Reach = 30f;
        public const float CollarRadius = 3.5f;
        public const float HedgeStart = 4f, HedgeWidth = 1.6f, SectionLength = 3f;
        public const float GraftRing = 8.5f, GraftBody = 0.9f, OwnerReach = 3.5f, BloomRadius = 6f;
        public const float SeedRadius = 4f, BrambleRadius = 2.2f, WeightRadius = 5f, WeightFollow = 6f;
        public const float InnerPath = 2.5f;          // bots walk between pens round the boss at this radius
        public const float BodyRadius = 1.3f;
        public const float Reaction = 0.3f, SlowWalk = 4.2f;
        public static float Bound = 13.5f;            // the floor (Verdant Chimes); the encounter sets it from the live map

        // ---------------- seasons ----------------
        // Season s (0 = I Spring .. 3 = IV Winter) belongs to this player index (party order). Docs table "Seasons".
        public static int OwnerIndex(int season, int players)
        {
            switch (players)
            {
                case 1: return 0;
                case 2: return season % 2;                      // I+III, II+IV
                case 3: return season == 3 ? 0 : season;        // I+IV, II, III
                default: return season % Mathf.Max(1, players); // 4 (and more): one each, then round again
            }
        }
        public static int[] GraftSeasons(int players) => players <= 1 ? new[] { 0, 2 } : new[] { 0, 1, 2, 3 };
        public static string Numeral(int season) => season switch { 0 => "I", 1 => "II", 2 => "III", _ => "IV" };

        // ---------------- the cut chain ----------------
        // random, never Across twice in a row, at least one In and one Out, and (when allowed) at least one Across
        public static List<Cut> Chain(System.Random rng, int count, bool across)
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                var c = new List<Cut>();
                for (int i = 0; i < count; i++)
                {
                    var options = new List<Cut> { Cut.Out, Cut.In };
                    if (across && i > 0 && c[i - 1] != Cut.Across) options.Add(Cut.Across);
                    c.Add(options[rng.Next(options.Count)]);
                }
                if (c.Contains(Cut.Out) && c.Contains(Cut.In) && (!across || c.Contains(Cut.Across))) return c;
            }
            var f = new List<Cut>(); for (int i = 0; i < count; i++) f.Add(i % 2 == 0 ? Cut.In : Cut.Out);
            if (across && count > 2) f[count - 1] = Cut.Across;
            return f;
        }

        // the hit test of one cut, exactly as the hazard draws it (Hazard.Contains, margin included)
        public static Hazard CutShape(Cut c, Vector3 boss, float acrossYaw)
        {
            switch (c)
            {
                case Cut.Out: return new Hazard { Shape = HazardShape.Circle, Kind = HazardKind.Strike, Origin = boss, Radius = OutRadius };
                case Cut.In: return new Hazard { Shape = HazardShape.Ring, Kind = HazardKind.Ground, Origin = boss, Inner = InInner, Radius = Reach };
                default: return new Hazard { Shape = HazardShape.Cone, Kind = HazardKind.Strike, Origin = boss, Yaw = acrossYaw, Radius = Reach, HalfAngle = AcrossHalf };
            }
        }

        public static IEnumerable<Vector3> Floor(float step)
        {
            for (float x = -Bound; x <= Bound; x += step)
                for (float z = -Bound; z <= Bound; z += step)
                {
                    var p = new Vector3(x, 0, z);
                    if (p.magnitude <= Bound - 0.05f && p.magnitude >= BodyRadius + 0.45f) yield return p;
                }
        }

        // how far p must walk to be clear of the cut (analytic, used only to pick a good spot; landing is checked with Contains)
        public static float DistToSafe(Cut c, float yaw, Vector3 p)
        {
            float r = Sim.Flat(p).magnitude;
            switch (c)
            {
                case Cut.Out: return Mathf.Max(0, OutRadius + Hazard.Margin + 0.05f - r);
                case Cut.In: return Mathf.Max(0, r - (InInner - Hazard.Margin - 0.05f));
                default:
                    float off = Mathf.Abs(Mathf.DeltaAngle(yaw, Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg));
                    if (off >= AcrossHalf + 4f) return 0;
                    return r * Mathf.Sin(Mathf.Deg2Rad * Mathf.Min(90f, AcrossHalf - off + 4f)) + Hazard.Margin;
            }
        }

        // One walker against a whole chain with the lookahead the fight gives (the current cut and the next are shown).
        // Each cut k lands firstBeats + k x spacing beats in; the walker reacts (0.3 s), then walks at `speed` to a spot the
        // landing cut misses, choosing the one closest to safety from the next cut. Across aims at the walker when it is
        // drawn (two cuts ahead; worst case: the walker is the farthest player). Returns false with the failing step.
        public static bool ChainSurvivable(IList<Cut> chain, Vector3 start, float bpm, float spacingBeats, float firstBeats, float speed, out string why, float grid = 0.75f)
        {
            why = ""; int n = chain.Count; float beat = 60f / bpm;
            var land = new float[n]; for (int k = 0; k < n; k++) land[k] = (firstBeats + k * spacingBeats) * beat;
            var pos = new Vector3[n]; var yaw = new float[n];
            Vector3 x = Sim.Flat(start); float now = 0;
            float YawAt(int k) { var at = k < 2 ? Sim.Flat(start) : pos[k - 2]; return at.sqrMagnitude < 1e-4f ? 0 : Mathf.Atan2(at.x, at.z) * Mathf.Rad2Deg; }
            for (int k = 0; k < n; k++)
            {
                yaw[k] = YawAt(k);
                if (k + 1 < n) yaw[k + 1] = YawAt(k + 1);
                var h = CutShape(chain[k], Vector3.zero, yaw[k]);
                float reach = speed * Mathf.Max(0, land[k] - now - Reaction);
                Vector3? best = null; float bs = float.MaxValue, bd = float.MaxValue;
                foreach (var g in Floor(grid))
                {
                    float d = Sim.Flat(g - x).magnitude;
                    if (d > reach || h.Contains(g)) continue;
                    float score = k + 1 < n ? DistToSafe(chain[k + 1], yaw[k + 1], g) : 0;
                    if (score < bs - 1e-3f || (Mathf.Abs(score - bs) <= 1e-3f && d < bd)) { bs = score; bd = d; best = g; }
                }
                if (best == null) { why = $"cut {k + 1} ({chain[k]}) from ({x.x:0.0},{x.z:0.0}): no safe spot within {reach:0.0} m"; return false; }
                x = best.Value; pos[k] = x; now = land[k];
            }
            return true;
        }
        public static float Budget(float beats, float bpm, float speed = SlowWalk) => speed * (beats * 60f / bpm - Reaction);

        // ---------------- pens and hedges ----------------
        public static float SpokeYaw(float spoke0, int k) => spoke0 + 90f * k;
        public static float PenYaw(float spoke0, int pen) => spoke0 + 45f + 90f * pen;
        public static Vector3 GraftSpot(Vector3 boss, float spoke0, int pen) => Sim.Flat(boss) + Sim.Dir(PenYaw(spoke0, pen)) * GraftRing;
        public static int PenOf(Vector3 boss, float spoke0, Vector3 p)
        {
            var v = Sim.Flat(p - boss); if (v.sqrMagnitude < 1e-4f) return 0;
            float a = Mathf.Repeat(Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg - spoke0, 360f);
            return Mathf.Clamp(Mathf.FloorToInt(a / 90f), 0, 3);
        }
        // the hedge sections of one spoke: (start distance from the boss, length)
        public static List<(float from, float length)> Sections(float bound)
        {
            var list = new List<(float, float)>();
            float end = bound + 0.6f;
            for (float r = HedgeStart; r < end - 0.05f; r += SectionLength) list.Add((r, Mathf.Min(SectionLength, end - r)));
            return list;
        }
        // the next waypoint for a player walking to a stand spot in pen `toPen` while the hedges stand: radially in to the
        // inner path, round it, then out. Returns the stand spot itself when already in that pen.
        public static Vector3 Waypoint(Vector3 boss, float spoke0, Vector3 from, Vector3 stand, int toPen)
        {
            boss = Sim.Flat(boss); from = Sim.Flat(from);
            if (PenOf(boss, spoke0, from) == toPen) return stand;
            float r = Sim.Flat(from - boss).magnitude;
            float myYaw = Mathf.Atan2(from.x - boss.x, from.z - boss.z) * Mathf.Rad2Deg;
            // in toward the middle of your own pen, not along your own angle: beside a hedge, your own angle converges on it
            if (r > InnerPath + 0.6f) return boss + Sim.Dir(PenYaw(spoke0, PenOf(boss, spoke0, from))) * InnerPath;
            float goal = PenYaw(spoke0, toPen), delta = Mathf.DeltaAngle(myYaw, goal);
            return boss + Sim.Dir(myYaw + Mathf.Clamp(delta, -40f, 40f)) * InnerPath;
        }
    }
}
