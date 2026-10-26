using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // 05 Hour Hands (aurelion.hour_hands), Hard only, brief abilities-v2/05-hour_hands.md (proposal).
    // Each standing player gets a 1.8 m × 28 m clock-hand spoke (Lane, Strike) from his feet through their hour: their
    // angle round him snapped to 30°. A spoke tracks its player's hour for 1.5 s, locks, and resolves 1.0 s later
    // (0.7 s on Hard, v2.1 §2).
    // Players who end the track on the same hour share one spoke (the duplicates are withdrawn on the lock), so each is
    // hit once. Solo: spoke II spawns on spoke I's lock tick and tracks your new hour. Run returns on the tick the last
    // spoke resolves. The phase plan only calls it on Hard (practice fires it on any level; Easy stretches it by 1.25).
    // Look: thin gold while it tracks, coral/amber with the lock flash when it locks.
    public sealed class HourHands : AurelionAbility
    {
        public override string Id => "aurelion.hour_hands";
        public override string Name => "Hour Hands";

        public const float Width = 1.8f, Length = 28f, HourDeg = 30f, TrackS = 1.5f, StrikeS = 1.0f;
        public const int Damage = 30;
        public int LockTicks => T(TrackS);
        public int ResolveTicks => T(TrackS) + Win(StrikeS);   // v2.1: the strike window is 0.7 s on Hard

        static readonly Color TrackFill = new Color(1.9f, 1.35f, 0.55f) * 0.35f, TrackBorder = new Color(1.9f, 1.35f, 0.55f);
        static readonly Color LockFill = new Color(1.35f, 0.42f, 0.32f), LockBorder = new Color(2.2f, 1.45f, 0.65f);

        // the hour (yaw snapped to 30°) of `at` round `centre`; on top of him it keeps `fallback`
        public static float HourOf(Vector3 centre, Vector3 at, float fallback)
        {
            float yaw = Sim.YawTo(centre, at, fallback);
            return Mathf.Repeat(Mathf.Round(yaw / HourDeg) * HourDeg, 360f);
        }

        // ticks from Run's first tick to the last spoke's resolve tick (= when Run returns).
        // CONTRACT: keep it equal to Run's real timing when the windows change.
        public int Duration() => Standing().Count <= 1 ? LockTicks - 1 + ResolveTicks - 1 : ResolveTicks - 1;

        public IEnumerable<int> Run()
        {
            Say(Name);
            var party = Standing();
            if (party.Count <= 1)
            {
                var p = party.FirstOrDefault();
                if (p == null) yield break;
                Spoke(p, "I");
                yield return LockTicks - 1;              // spoke I locks during this tick's hazard step
                Say(Name);
                var who = Standing().FirstOrDefault() ?? p;
                Spoke(who, "II");
                yield return ResolveTicks - 1;           // spoke II resolves this tick
                yield break;
            }

            var spokes = party.Select(pl => Spoke(pl, null)).ToList();
            yield return LockTicks;                      // every spoke locked on the previous tick: its hour is final
            Say(Name);
            // one spoke per hour: players sharing an hour share the first spoke on it
            foreach (var g in spokes.Where(h => !h.Cancelled).GroupBy(h => Mathf.RoundToInt(h.Yaw / HourDeg) % 12))
                foreach (var dup in g.Skip(1)) W.Hazards.Cancel(dup);
            yield return ResolveTicks - 1 - LockTicks;   // the spokes resolve this tick
        }

        Hazard Spoke(PlayerController target, string tag)
        {
            var from = BossPos;
            var h = new Hazard
            {
                Source = tag == null ? Name : Name + " " + tag,
                Shape = HazardShape.Lane, Kind = HazardKind.Strike,
                Origin = from, Yaw = HourOf(from, target.transform.position, Body.eulerAngles.y),
                Length = Length, Width = Width,
                LockAt = LockTicks, ResolveAt = ResolveTicks,
                Damage = Damage, Knockback = 1.5f, Stun = Sim.Ticks(0.25f),
            };
            h.Track = x =>
            {
                x.Origin = BossPos;
                if (target && !target.Down) x.Yaw = HourOf(x.Origin, target.transform.position, x.Yaw);
            };
            h = Spawn(h);
            h.Tele.Tint(TrackFill, TrackBorder);
            bool snapped = false;
            h.Track += x => { if (!snapped && x.Age + 1 >= x.LockAt) { snapped = true; x.Tele.Tint(LockFill, LockBorder); } };
            return h;
        }
    }
}
