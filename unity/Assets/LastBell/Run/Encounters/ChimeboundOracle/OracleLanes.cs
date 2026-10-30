using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Toll Lanes patterns for the Chimebound Oracle, as plain data: which lanes fire on which beat. Pure C#, so the
    // encounter, the acceptance test and the fairness check all use the same numbers.
    //
    // The floor is split into 8 north-south lanes (x bands). Lane 0 is the far left on the fixed camera, lane 7 the
    // far right. A pattern is a list of "slots": each slot fires one or two lanes on a beat offset from the cast start.
    //
    //   Rounds          above 50%: lanes 0..7 in order, one every 2 beats.
    //   Odds & Evens    above 50%: lanes 1,3,5,7 together, then 0,2,4,6 four beats later.
    //   Called Changes  below 50%: a new order every cast, one slot per beat, two slots carry a pair. Rules:
    //                     - no two neighbouring lanes in consecutive slots, and a pair never holds neighbours;
    //                     - the two pair slots are never back to back (at most 2 runes in their last half second);
    //                     - from every lane, a player who walks at 80% speed and reacts 0.25 s late survives the cast
    //                       (and its Hard echo), checked by Survivable below. A dash is never needed.
    public static class OracleLanes
    {
        public const int Count = 8;
        public static float Bound = 13.9f;                       // the arena's play bound, set from CombatWorld.PlayerBound at spawn
        public static float Width => 2 * Bound / Count;            // 3.475 m on the Skyward Spire (13.9), 3.125 m on the Metronome Court (12.5)
        public static float CentreX(int lane) => -Bound + Width * (lane + 0.5f);
        public static int LaneOf(float x) => Mathf.Clamp(Mathf.FloorToInt((x + Bound) / Width), 0, Count - 1);

        public struct Slot { public int Beat; public int A, B; public Slot(int beat, int a, int b = -1) { Beat = beat; A = a; B = b; } }

        public static List<Slot> Rounds()
        {
            var s = new List<Slot>();
            for (int i = 0; i < Count; i++) s.Add(new Slot(2 * i, i));
            return s;
        }

        // two slots of four lanes each, expressed as pairs on the same beat
        public static List<Slot> OddsEvens() => new List<Slot>
        {
            new Slot(0, 1, 3), new Slot(0, 5, 7),
            new Slot(4, 0, 2), new Slot(4, 4, 6),
        };

        // Lanes firing per slot index, for the order rules (OddsEvens lists two pairs per beat on purpose).
        public static List<int>[] ByBeat(List<Slot> slots, out int beats)
        {
            beats = 0; foreach (var s in slots) beats = Mathf.Max(beats, s.Beat + 1);
            var by = new List<int>[beats];
            for (int i = 0; i < beats; i++) by[i] = new List<int>();
            foreach (var s in slots) { by[s.Beat].Add(s.A); if (s.B >= 0) by[s.Beat].Add(s.B); }
            return by;
        }

        public const int CalledSlots = 6;   // 8 lanes in 6 beats: four singles and two pairs

        public static List<Slot> CalledChanges(System.Random rng, float bpm, float warnBeats, float echoBeats, out int tries)
        {
            for (tries = 1; tries <= 400; tries++)
            {
                var order = new List<int>(); for (int i = 0; i < Count; i++) order.Add(i);
                for (int i = order.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
                // pick which two of the six slots carry a pair: never back to back
                int p1 = rng.Next(CalledSlots), p2;
                do p2 = rng.Next(CalledSlots); while (p2 == p1 || Mathf.Abs(p2 - p1) == 1);
                var slots = new List<Slot>(); int k = 0;
                for (int s = 0; s < CalledSlots; s++)
                {
                    if (s == p1 || s == p2) { slots.Add(new Slot(s, order[k], order[k + 1])); k += 2; }
                    else slots.Add(new Slot(s, order[k++]));
                }
                if (OrderRulesHold(slots) && Survivable(slots, bpm, warnBeats, echoBeats)) return slots;
            }
            tries = -1;
            return SafeFallback();
        }

        // A fixed Called Changes order that passes every rule (used only if 400 shuffles all fail).
        public static List<Slot> SafeFallback() => new List<Slot>
        {
            new Slot(0, 0), new Slot(1, 4, 7), new Slot(2, 2), new Slot(3, 6), new Slot(4, 1, 3), new Slot(5, 5),
        };

        public static bool OrderRulesHold(List<Slot> slots)
        {
            var by = ByBeat(slots, out int beats);
            for (int b = 0; b < beats; b++)
            {
                var cur = by[b];
                if (cur.Count > 2) return false;
                if (cur.Count == 2 && Mathf.Abs(cur[0] - cur[1]) == 1) return false;
                if (b > 0) foreach (var x in cur) foreach (var y in by[b - 1]) if (Mathf.Abs(x - y) == 1) return false;
                if (b > 0 && cur.Count == 2 && by[b - 1].Count == 2) return false;
            }
            var seen = new HashSet<int>();
            foreach (var s in slots) { if (!seen.Add(s.A)) return false; if (s.B >= 0 && !seen.Add(s.B)) return false; }
            return seen.Count == Count;
        }

        // ---------------- fairness: can a walking player survive from every start lane? ----------------
        public const float WalkFactor = 0.8f;       // the check walks at 80% of PlayerController.WalkSpeed
        public const int ReactTicks = 15;           // and starts moving 0.25 s after the runes appear
        public const int CascadeTicks = 9;          // a lane's runes strike far-to-near over 9 ticks (0.15 s)
        const float Cell = 0.05f, BodyMargin = Hazard.Margin;

        // Danger windows in ticks from the cast start: (lane, first tick, last tick). The first strike lands at
        // warnBeats + slot beat; the Hard echo repeats the lane echoBeats later (echoBeats <= 0: no echo).
        public static List<(int lane, int from, int to)> Windows(List<Slot> slots, float bpm, float warnBeats, float echoBeats)
        {
            var w = new List<(int, int, int)>();
            foreach (var s in slots)
                foreach (var lane in s.B >= 0 ? new[] { s.A, s.B } : new[] { s.A })
                {
                    int t = TempoClock.TicksFor(warnBeats + s.Beat, bpm);
                    w.Add((lane, t, t + CascadeTicks));
                    if (echoBeats > 0) { int e = t + TempoClock.TicksFor(echoBeats, bpm); w.Add((lane, e, e + CascadeTicks)); }
                }
            return w;
        }

        public static bool Survivable(List<Slot> slots, float bpm, float warnBeats, float echoBeats)
        {
            for (int start = 0; start < Count; start++)
                if (!SurvivableFrom(CentreX(start), slots, bpm, warnBeats, echoBeats)) return false;
            return true;
        }

        // 1D reachability over x (the lanes span the whole floor north-south, so only x matters at the court's middle).
        public static bool SurvivableFrom(float startX, List<Slot> slots, float bpm, float warnBeats, float echoBeats)
        {
            var win = Windows(slots, bpm, warnBeats, echoBeats);
            int end = 0; foreach (var x in win) end = Mathf.Max(end, x.to + 1);
            int n = Mathf.CeilToInt(2 * Bound / Cell) + 1;
            var reach = new bool[n]; var next = new bool[n];
            reach[Mathf.Clamp(Mathf.RoundToInt((startX + Bound) / Cell), 0, n - 1)] = true;
            float step = PlayerController.WalkSpeed * WalkFactor * Sim.Dt, carry = 0;
            for (int t = 1; t <= end; t++)
            {
                bool moving = t > ReactTicks;
                int spread = 0;
                if (moving) { carry += step; spread = Mathf.FloorToInt(carry / Cell); carry -= spread * Cell; }
                System.Array.Clear(next, 0, n);
                for (int i = 0; i < n; i++)
                {
                    if (!reach[i]) continue;
                    int lo = moving ? Mathf.Max(0, i - spread) : i, hi = moving ? Mathf.Min(n - 1, i + spread) : i;
                    for (int j = lo; j <= hi; j++) next[j] = true;
                }
                foreach (var x in win)
                {
                    if (t < x.from || t > x.to) continue;
                    float a = CentreX(x.lane) - Width / 2 - BodyMargin, b = CentreX(x.lane) + Width / 2 + BodyMargin;
                    int ia = Mathf.Max(0, Mathf.CeilToInt((a + Bound) / Cell)), ib = Mathf.Min(n - 1, Mathf.FloorToInt((b + Bound) / Cell));
                    for (int j = ia; j <= ib; j++) next[j] = false;
                }
                bool any = false;
                for (int i = 0; i < n; i++) { reach[i] = next[i]; any |= next[i]; }
                if (!any) return false;
            }
            return true;
        }
    }
}
