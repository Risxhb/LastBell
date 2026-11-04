using System;
using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Scene-independent rules of the Vesper Seedkeeper (docs/bosses/vesper-seedkeeper.md, a proposal): where stakes go,
    // who carries seeds, where a seed is planted, how many a bloom holds, and the walking checks the autotest runs.
    public static class VesperSeedkeeperRules
    {
        public static float Bound = 13.5f;                 // Verdant Chimes play bound (RunMaps)
        public const float StakeRing = 10.5f, StakeRadius = 1.6f, StakePartRadius = 0.6f, CordWidth = 1.4f, CordGap = 0.8f;
        public const float SeedRadius = 2.4f, BloomRadius = 2.4f, BloomMinFromBoss = 7.2f, UmbraRadius = 4.0f;
        public const float TideRadius = 6.0f, LateBloomRadius = 2.8f, TideDrift = 3.0f;
        public const float SlowWalk = 4.2f, DesignWalk = 6f, Reaction = 0.3f;

        public static int Cords(int standing) => Mathf.Clamp(standing, 1, 4);
        public static int Seeds(int standing) => standing >= 3 ? 2 : 1;
        // players per seed (and per bloom), first carrier first: solo [1], 2 [2], 3 [2, 1] (a pair and a lone carrier), 4 [2, 2]
        public static int[] Shares(int standing) => standing >= 4 ? new[] { 2, 2 } : standing == 3 ? new[] { 2, 1 } : new[] { Mathf.Clamp(standing, 1, 2) };
        // a seed's damage: 80 split by a pair, 40 on a lone carrier (solo, or the 3-player lone seed)
        public static int SeedTotal(int share) => share <= 1 ? 40 : 80;

        // stake angles (degrees, Sim.Dir convention); wave 2 sits halfway between the first ones
        public static List<float> StakeAngles(int n, float offset, bool secondWave)
        {
            var list = new List<float>();
            float step = 360f / n, start = offset + (secondWave ? step / 2 : 0);
            for (int i = 0; i < n; i++) list.Add(Mathf.Repeat(start + i * step, 360f));
            return list;
        }
        public static Vector3 StakeAt(float angle) => Sim.Dir(angle) * StakeRing;

        // gap between the two waves: the walk from a stake to its neighbour of the other wave at 4.2 m/s, plus 0.7 s
        public static float WaveGapSeconds(int n)
        {
            float chord = 2f * StakeRing * Mathf.Sin(Mathf.Deg2Rad * 90f / Mathf.Max(1, n));
            float stand = StakeRadius - 0.45f;   // stepping into the stake's edge is enough
            return Mathf.Max(0, chord - 2 * stand) / SlowWalk + 0.7f;
        }

        // the cord lane: from the vessel to just short of the stake circle
        public static (float yaw, float length) CordLane(Vector3 boss, Vector3 stake)
        {
            var v = Sim.Flat(stake - boss);
            return (Sim.YawTo(boss, stake, 0), Mathf.Max(1f, v.magnitude - StakeRadius - CordGap));
        }

        // where a landed seed is planted: pushed out of the vessel's shadow, kept inside the floor
        public static Vector3 PlantSpot(Vector3 landed, Vector3 boss)
        {
            var b = Sim.Flat(boss); var v = Sim.Flat(landed) - b;
            if (v.magnitude < BloomMinFromBoss)
            {
                var dir = v.sqrMagnitude > 1e-4f ? v.normalized : (b.sqrMagnitude > 1e-4f ? -b.normalized : Vector3.forward);
                v = dir * BloomMinFromBoss;
            }
            var p = b + v; float lim = Bound - BloomRadius - 0.3f;
            if (p.magnitude > lim) p = p.normalized * lim;
            // the clamp can pull it back toward the vessel; slide it round the rim until it clears the shadow
            for (int i = 0; i < 36 && Sim.Flat(p - b).magnitude < BloomMinFromBoss; i++)
                p = Quaternion.Euler(0, 10f, 0) * p;
            return p;
        }

        // the vessel's Dusk Tide spot: toward the party, at most TideDrift from the centre
        public static Vector3 TideSpot(IList<Vector3> players)
        {
            if (players == null || players.Count == 0) return Vector3.zero;
            var c = Vector3.zero; foreach (var p in players) c += Sim.Flat(p); c /= players.Count;
            return c.magnitude > TideDrift ? c.normalized * TideDrift : c;
        }

        // ---------------- walking checks (autotest) ----------------
        // seconds a walker at `speed` needs to be at `target` within `slack` (a 0.3 s reaction first)
        public static float WalkTime(Vector3 from, Vector3 target, float slack, float speed) =>
            Reaction + Mathf.Max(0, Sim.Flat(target - from).magnitude - slack) / speed;

        // the worst start on the floor to reach some stake of an n-stake set, assuming the party takes the nearest ones
        public static float WorstStakeWalk(int n, float offset, float speed)
        {
            var stakes = StakeAngles(n, offset, false); float worst = 0;
            for (float x = -Bound; x <= Bound; x += 1f)
                for (float z = -Bound; z <= Bound; z += 1f)
                {
                    var p = new Vector3(x, 0, z); if (p.magnitude > Bound - 0.3f || p.magnitude < 1.8f) continue;
                    // the worst case for a party: any player may be sent to the farthest stake (assignment by nearest)
                    foreach (var a in stakes) worst = Mathf.Max(worst, WalkTime(p, StakeAt(a), StakeRadius - 0.45f, speed));
                }
            return worst;
        }

        // Dusk Tide: in (inside TideRadius - margin) by the first hit, then out by the second, from the worst start
        public static bool TideSurvivable(Vector3 boss, float firstSec, float secondSec, bool reversed, float speed, out string why)
        {
            why = "";
            float inR = TideRadius - Hazard.Margin - 0.15f, outR = TideRadius + Hazard.Margin + 0.15f, body = 1.3f + 0.45f;
            for (float x = -Bound; x <= Bound; x += 1f)
                for (float z = -Bound; z <= Bound; z += 1f)
                {
                    var p = new Vector3(x, 0, z); if (p.magnitude > Bound - 0.3f) continue;
                    float d = Sim.Flat(p - boss).magnitude; if (d < body) continue;
                    // first shape: walk to the safe side; second: from that side's boundary to the other side
                    float first = reversed ? Mathf.Max(0, outR - d) : Mathf.Max(0, d - inR);
                    float t1 = (first > 0 ? Reaction : 0) + first / speed;
                    float t2 = Reaction + (outR - inR) / speed;
                    if (t1 > firstSec) { why = $"from ({x},{z}) first {t1:0.00}s > {firstSec:0.00}s"; return false; }
                    if (t2 > secondSec) { why = $"second {t2:0.00}s > {secondSec:0.00}s"; return false; }
                }
            return true;
        }
    }
}
