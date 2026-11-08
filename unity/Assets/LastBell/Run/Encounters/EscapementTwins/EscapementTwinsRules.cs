using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Pure numbers and geometry for the Escapement Twins (docs/bosses/escapement-twins.md), with no scene state, so the
    // autotest can check the escape timing of every warning from the worst start positions.
    public static class EscapementTwinsRules
    {
        // tempo
        public const float BpmHigh = 100f, BpmLow = 120f;
        public static int BeatTicks(bool phase2) => Mathf.RoundToInt(60f * Sim.Hz / (phase2 ? BpmLow : BpmHigh));

        // layout: the twins stand Spacing either side of the centre on one axis
        public const float Spacing = 6f, TurnPerAbility = 45f;
        public static Vector3 TickAt(float axis) => -Sim.Dir(axis) * Spacing;
        public static Vector3 TockAt(float axis) => Sim.Dir(axis) * Spacing;

        // 1. Pallet Lock
        public const float DiscRadius = 4.5f;
        public const int LockDmg = 50;
        public static int FirstLockBeats(bool phase2, Difficulty d) => (phase2 ? 9 : 8) + (d == Difficulty.Easy ? 2 : 0);
        public static int SwapLockBeats(bool phase2, Difficulty d) => (phase2 ? 7 : 6) + (d == Difficulty.Easy ? 2 : 0);
        public static int Locks(bool phase2) => phase2 ? 3 : 2;
        // marks: the half nearer Tick (rounded up) wears brass (0), the rest cobalt (1); solo is brass
        public static int BrassCount(int standing) => (standing + 1) / 2;

        // 2. Escape Wheel
        public const int Spokes = 6, SpokeDmg = 40;
        public const float SpokeHalf = 10f, SpokeReach = 30f;
        public static int Volleys(bool phase2) => phase2 ? 8 : 6;
        public static float VolleySeconds(int volley, bool phase2, Difficulty d) =>
            d == Difficulty.Easy || volley == 0 ? 2.0f : phase2 ? 1.3f : 1.5f;

        // 3. Counterpoise
        public const float SoakRadius = 3f, SoakOut = 10f, SoakSideOut = 8f;
        public const int SoakDmg = 75, SoloSoakDmg = 70;   // a lone soaker survives from full HP even on Hard (x1.3)
        public static int SoakBeats(bool phase2, Difficulty d) => (phase2 ? 8 : 7) + (d == Difficulty.Easy ? 2 : 0);
        public const int SecondSetBeats = 7;
        public static int Sets(bool phase2) => phase2 ? 2 : 1;
        // circle centres and the players each wants, for one set; solo: one circle in the middle
        public static List<(Vector3 at, int need)> SoakSpots(float axis, int set, int standing)
        {
            var list = new List<(Vector3, int)>();
            if (standing <= 1) { list.Add((Vector3.zero, 1)); return list; }
            float yaw = set == 0 ? axis : axis + 90f, out_ = set == 0 ? SoakOut : SoakSideOut;
            int brass = BrassCount(standing);
            list.Add((-Sim.Dir(yaw) * out_, brass));
            list.Add((Sim.Dir(yaw) * out_, standing - brass));
            return list;
        }

        // 4. Lock and Release
        public const float LaneWidth = 3f, ShockInner = 7f, LandRadius = 2.5f, RecoilWidth = 2.5f;
        public const int LaneDmg = 60, ShockDmg = 55, LandDmg = 45, RecoilDmg = 45;
        public static int LaneBeats(Difficulty d) => 4 + (d == Difficulty.Easy ? 1 : 0);
        public const int ShockAfterBeats = 2, LeapBeats = 2;
        public const float PunishSeconds = 3.5f;

        public const int ShiftTicks = 120;
        public const float WalkSpeed = 3.0f;

        // the content-rules escape check: walk at 4.2 m/s after a 0.3 s reaction
        public const float CheckWalk = 4.2f, Reaction = 0.3f;
        public static float NeedSeconds(float metres) => metres / CheckWalk + Reaction;

        // worst walks (metres) for each warning, measured on a disc floor of radius bound
        public static float FirstLockWalk(float bound) => bound + Spacing - (DiscRadius - Hazard.Margin);      // far edge of the floor to inside the disc
        // the far side of one disc (2 x Spacing + DiscRadius from the other twin) to inside the other disc
        public static float SwapLockWalk() => 2 * Spacing + DiscRadius - (DiscRadius - Hazard.Margin);
        public static float SpokeWalk(float bound) => (bound + Spacing) * Mathf.Sin(SpokeHalf * Mathf.Deg2Rad) + Hazard.Margin;
        public static float SoakWalk(float bound) => Mathf.Sqrt(bound * bound + SoakOut * SoakOut) - (SoakRadius - Hazard.Margin);   // floor edge on the other axis into a circle
        public static float SecondSetWalk() => Mathf.Sqrt(SoakOut * SoakOut + SoakSideOut * SoakSideOut);              // far side of one circle to inside the next
        public static float LaneWalk() => LaneWidth / 2 + Hazard.Margin;
        public static float ShockWalk(float bound) => bound - (ShockInner - Hazard.Margin);
        public static float LandWalk() => LandRadius + Hazard.Margin;
    }
}
