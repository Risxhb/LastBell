using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // The Silt Carillon's pure rules (docs/bosses/silt-carillon.md, a proposal): timings per difficulty, bell counts and
    // spots, sector yaws and the escape distances the acceptance test checks. No scene state, so the test can sweep them.
    public static class SiltCarillonRules
    {
        public const float Walk = 4.2f, Reaction = 0.3f;   // content rules §2: prototype walk, reaction before moving
        public static float Bound = 13.5f;                // Verdant Chimes player bound (set from the map at build)

        // ---------------- Silt Tide ----------------
        public const float TideInner = 7f;                  // High Tide: the ring from here to the edge floods
        public const float SectorHalf = 30f;                 // Crosscurrent: six 60-degree sectors, three flood at a time
        public static float TideWarnBeats(Difficulty lvl, bool phase2) => phase2 ? (lvl == Difficulty.Easy ? 5f : 4f) : (lvl == Difficulty.Easy ? 6f : 5f);
        public const float CrossMudBeats = 6f;               // each Crosscurrent set stays mud this long
        public const float HighTideMaxBeats = 16f;           // High Tide mud ebbs at the end of its attack, at most this long
        public const float BackwashBeats = 1.5f;             // Hard: drained ground strikes this long after it drains
        // the yaw of each sector of a set (set 0 or 1), from the cycle's base yaw
        public static float[] SectorYaws(float baseYaw, int set) => new[] { baseYaw + set * 60f, baseYaw + set * 60f + 120f, baseYaw + set * 60f + 240f };

        // walk needed: from the edge to inside the High Tide's dry disc (its border counts as inside the mud, Hazard.Margin)
        public static float TideEscape => Bound - (TideInner - Hazard.Margin) + 0.05f;
        // walk needed from a point to leave every sector of a Crosscurrent set (perpendicular to the nearest sector line)
        public static float CrossEscape(Vector3 p, float baseYaw, int set)
        {
            var v = Sim.Flat(p); float r = v.magnitude; if (r < 0.01f) return 0;
            float best = float.MaxValue;
            foreach (float yaw in SectorYaws(baseYaw, set))
            {
                float off = Mathf.Abs(Mathf.DeltaAngle(yaw, Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg));
                if (off > SectorHalf + 0.01f) return 0;   // already outside this sector, and sectors of a set never touch
                // to the nearest edge line of this sector, plus the margin
                float toEdge = r * Mathf.Sin((SectorHalf - off) * Mathf.Deg2Rad) + Hazard.Margin + 0.05f;
                best = Mathf.Min(best, toEdge);
            }
            return best == float.MaxValue ? 0 : best;
        }
        public static bool InSet(Vector3 p, float baseYaw, int set)
        {
            var v = Sim.Flat(p); if (v.magnitude < 0.6f) return true;
            foreach (float yaw in SectorYaws(baseYaw, set))
                if (Mathf.Abs(Mathf.DeltaAngle(yaw, Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg)) <= SectorHalf + Mathf.Rad2Deg * Mathf.Atan2(Hazard.Margin, v.magnitude)) return true;
            return false;
        }

        // ---------------- Sunken Bells ----------------
        public const int BellTotal = 60;
        public const float BellRadius = 1.6f, BellRingHigh = 4.5f, BellRingLow = 6f;
        // the bells toll this long after they surface (with the tide's warning): long enough for any player to reach the
        // farther bell from anywhere on the floor (the test checks it); the first set of the fight gets 3 beats more
        public static float BellTollBeats(Difficulty lvl, bool phase2, bool first) => (phase2 ? 10f : 8f) + (lvl == Difficulty.Easy ? 2f : 0f) + (first ? 3f : 0f);
        // how many players each bell wants: solo one bell I; 2 = I + I; 3 = II + I; 4 = II + II; more: halves
        public static int[] BellNeeds(int standing)
        {
            if (standing <= 1) return new[] { 1 };
            return new[] { (standing + 1) / 2, standing / 2 };
        }
        // High Tide: opposite sides of the tower at 4.5 m. Crosscurrent: the middles of two of the three dry sectors at 6 m.
        public static List<Vector3> BellSpots(bool phase2, float baseYaw, int count)
        {
            var list = new List<Vector3>();
            for (int i = 0; i < count; i++)
                list.Add(phase2 ? Sim.Dir(SectorYaws(baseYaw, 1)[i]) * BellRingLow : Sim.Dir(baseYaw + i * 180f) * BellRingHigh);
            return list;
        }
        public static int Share(int inside) => inside <= 0 ? 0 : Mathf.CeilToInt(BellTotal / (float)inside);

        // ---------------- Silt Fall ----------------
        public const float DropRadius = 1.8f, MoundBeats = 6f, FollowBeats = 1f;
        public const int DropsHigh = 4, DropsLow = 5;
        public static float DropLeadBeats(Difficulty lvl, bool phase2) => (phase2 ? 1.75f : 1.5f) + (lvl == Difficulty.Easy ? 0.5f : 0f);
        public static float DropEscape => DropRadius + Hazard.Margin;   // from the centre of a locked drop to outside it

        // ---------------- Drowned Peal ----------------
        public const float BandA = 4.5f, BandB = 9f;
        public static float PealFirstBeats(Difficulty lvl) => lvl == Difficulty.Easy ? 5f : 4f;
        public static float PealGapBeats(Difficulty lvl) => lvl == Difficulty.Easy ? 2f : 1.5f;
        // across a band border into the band that just landed: both shapes include the border +- Margin
        public static float PealCross => 2 * Hazard.Margin + 0.05f;

        // ---------------- walking time ----------------
        public static float Reach(float beats, float bpm) => Walk * Mathf.Max(0, beats * 60f / bpm - Reaction);
    }
}
