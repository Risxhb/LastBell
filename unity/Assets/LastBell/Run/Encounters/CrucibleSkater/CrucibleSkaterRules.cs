using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Pure numbers and geometry for the Crucible Skater (docs/bosses/crucible-skater.md, a proposal), shared by the encounter
    // and its autotest. Seconds are Medium; Easy stretches every warning by 1.25 (never a skate speed or a trail's life).
    public static class CrucibleSkaterRules
    {
        public static float Bound = 13.7f;                 // the player bound of the map in use (Brass Foundry 13.7 m)
        public const float BossBound = CombatWorld.BossBound;
        public const float Reaction = 0.3f;                // the escape check: a 0.3 s reaction, then walking
        public static readonly float[] Speeds = { 4.2f, 6f };   // prototype walk and design walk
        public const float BodyRadius = 0.9f;

        // ---- 1. Figure Carve ----
        public const float LaneWidth = 2.4f, TrailWidth = 1.6f, Overshoot = 2f, SkateSpeed = 16f, LaneLength = 30f;
        public const float CarveTrack = 1.5f, CarveCrouch = 0.9f, TrailLife = 7f, TrailLifeP2 = 9f, RetraceWarn = 1.0f;
        public const int CarveDmg = 40, TrailBurn = 20;
        // ---- 2. Body Check ----
        public const float RingRadius = 2.0f, CheckWidth = 2.6f, ChargeSpeed = 22f;
        public const float CheckLock = 1.7f, CheckResolve = 2.5f, StuckSeconds = 3.5f;
        public const int BlockTotal = 60, ChargeDmg = 55;
        // ---- 3. Crucible Toss ----
        public const float PourRing = 5.4f, PourRadius = 2.4f, PourLand = 5f, PourGapSolo = 3.5f;
        public const int PourTotal = 70;
        public static readonly float[] PourLayouts = { 0f, 45f, 90f, 135f };
        // ---- 4. Rink Lap -> Pirouette ----
        public const float BandInner = 8.5f, LapHalfArc = 20f, PirouetteRadius = 6f;
        public const float LapWarn = 2f, LapTime = 3f, PirouetteWarn = 2f, ToCentreTime = 0.6f, DizzySeconds = 3.5f;
        public const int LapDmg = 45, PirouetteDmg = 50;
        // ---- phases ----
        public const float ShiftSeconds = 2f, GapSeconds = 1f;

        public static float Stretch(Difficulty l) => l == Difficulty.Easy ? 1.25f : 1f;
        public static int Warn(float seconds, Difficulty l) => Mathf.Max(1, Mathf.RoundToInt(seconds * Stretch(l) * Sim.Hz));
        public static int Ticks(float seconds) => Mathf.Max(1, Mathf.RoundToInt(seconds * Sim.Hz));

        // Figure Carve: which numbered player (0 = I, the farthest) each leg goes to.
        //   solo I-I-I (each leg re-aimed), 2p I-II-I, 3p I-II-III, 4p I-II-III-IV; phase 2 adds one leg (solo 4, 2p I-II-I-II,
        //   3+ back to I)
        public static int[] CarveOrder(int n, bool phase2)
        {
            var legs = new List<int>();
            if (n <= 1) { int c = phase2 ? 4 : 3; for (int i = 0; i < c; i++) legs.Add(0); return legs.ToArray(); }
            for (int i = 0; i < n; i++) legs.Add(i);
            if (n == 2) legs.Add(0);
            if (phase2) legs.Add(n == 2 ? 1 : 0);
            return legs.ToArray();
        }

        // where a leg ends: through the waypoint and 2 m past it, held inside the boss bound
        public static Vector3 LegStop(Vector3 from, Vector3 waypoint, Vector3 fallback)
        {
            var dir = LegDir(from, waypoint, fallback);
            return ClampToBound(from, Sim.Flat(waypoint) + dir * Overshoot);
        }
        public static Vector3 LegDir(Vector3 from, Vector3 waypoint, Vector3 fallback)
        {
            var d = Sim.Flat(waypoint - from);
            if (d.magnitude < 0.5f) d = Sim.Flat(from).magnitude > 0.5f ? -Sim.Flat(from) : Sim.Flat(fallback);
            if (d.sqrMagnitude < 1e-4f) d = Vector3.forward;
            return d.normalized;
        }

        public static Vector3 ClampToBound(Vector3 from, Vector3 to)
        {
            from.y = 0; to.y = 0;
            if (new Vector2(to.x, to.z).magnitude <= BossBound) return to;
            if (new Vector2(from.x, from.z).magnitude > BossBound) from = Sim.Flat(from).normalized * BossBound;
            var d = to - from; float lo = 0, hi = 1;
            for (int i = 0; i < 24; i++) { float m = (lo + hi) / 2; var p = from + d * m; if (new Vector2(p.x, p.z).magnitude <= BossBound) lo = m; else hi = m; }
            return from + d * lo;
        }

        // Crucible Toss: soakers needed per pour, and whether the pours land one after the other (solo)
        public static int[] PourCounts(int n)
        {
            if (n <= 1) return new[] { 1, 1 };
            int a = (n + 1) / 2, b = n - a;
            return new[] { a, Mathf.Max(1, b) };
        }
        public static bool PoursInSequence(int n) => n <= 1;
        public static List<Vector3> PourSpots(float layoutYaw) => new List<Vector3> { Sim.Dir(layoutYaw) * PourRing, Sim.Dir(layoutYaw + 180f) * PourRing };
        public static int Share(int total, int inside) => inside <= 0 ? 0 : Mathf.CeilToInt(total / (float)inside);
        public static int BlockersNeeded(int n) => n >= 4 ? 2 : 1;

        // ---------------- escape checks (content rules §2: 4.2 and 6 m/s after a 0.3 s reaction) ----------------
        static float Walk(float seconds, float speed) => Mathf.Max(0, seconds - Reaction) * speed;
        static float SideStep(float width) => width / 2 + Hazard.Margin + 0.02f;

        // Figure Carve: from the middle of a lane at its lock, the skater's body arrives no sooner than the crouch
        public static bool CarveEscape(Difficulty l, float speed, out float have, out float need)
        { have = Walk(CarveCrouch * Stretch(l), speed); need = SideStep(LaneWidth); return have >= need; }
        // Retrace (Hard): each reversed segment is shown at least 1.0 s before the body passes
        public static bool RetraceEscape(float speed, out float have, out float need)
        { have = Walk(RetraceWarn, speed); need = SideStep(LaneWidth); return have >= need; }
        // Body Check: the quarry steps out of the charge lane between its lock and the charge
        public static bool CheckEscape(Difficulty l, float speed, out float have, out float need)
        { have = Walk((CheckResolve - CheckLock) * Stretch(l), speed); need = SideStep(CheckWidth); return have >= need; }
        // Body Check: a blocker reaches the ring from 8.5 m out (a ranged player's hold) before the charge
        public static bool BlockReach(Difficulty l, float speed, out float have, out float need)
        { have = Walk(CheckResolve * Stretch(l), speed); need = 8.5f + BodyRadius - (BodyRadius + 0.1f + RingRadius) - RingRadius + 0.4f; return have >= need; }
        // Crucible Toss: from anywhere on the floor into the farther pour (2+1 / 2+2 assignments can send you there)
        public static bool PourReach(Difficulty l, float speed, out float have, out float need)
        { have = Walk(PourLand * Stretch(l), speed); need = Bound + PourRing - PourRadius + 0.4f; return have >= need; }
        // Crucible Toss solo: from the far side of the first pour into the second before it lands
        public static bool PourRelay(Difficulty l, float speed, out float have, out float need)
        { have = Walk(PourGapSolo * Stretch(l), speed); need = 2 * PourRing + 0.4f; return have >= need; }
        // Rink Lap: from the wall to inside the band before the lap can reach you
        public static bool LapEscape(Difficulty l, float speed, out float have, out float need)
        { have = Walk(LapWarn * Stretch(l), speed); need = Bound - (BandInner - Hazard.Margin) + 0.02f; return have >= need; }
        // Pirouette: from the centre out of the circle, and from inside a pour that lands late on Easy
        public static bool PirouetteEscape(Difficulty l, float speed, out float have, out float need)
        {
            float late = Mathf.Max(0, PourLand * Stretch(l) - (LapWarn * Stretch(l) + LapTime));   // Easy: the pours land after the lap
            have = Walk(PirouetteWarn * Stretch(l) - late, speed);
            need = late > 0 ? PirouetteRadius + Hazard.Margin - (PourRing - PourRadius) + 0.02f : PirouetteRadius + Hazard.Margin + 0.02f;
            return have >= need;
        }
        // the pours sit wholly inside the band's safe disc, so Toss + Lap never overlap
        public static bool PoursInsideBand() => PourRing + PourRadius + Hazard.Margin < BandInner - Hazard.Margin;
    }
}
