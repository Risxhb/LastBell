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
    // Hard (5 Nov, done as recommended, review later): every spoke has a mirror, the opposite hand (its hour + 6), tracking and
    // resolving with it. Mirrors that land on an hour already struck are withdrawn on the lock, like shared hours, so each
    // player is still hit at most once per hour; with 4 players at most 8 of the 12 hours strike, so a gap always stays.
    public sealed class HourHands : AurelionAbility
    {
        public override string Id => "aurelion.hour_hands";
        public override string Name => "Hour Hands";

        public const float Width = 1.8f, Length = 28f, HourDeg = 30f, TrackS = 1.5f, StrikeS = 1.0f;
        public const int Damage = 30;
        public int LockTicks => T(TrackS);
        public int ResolveTicks => T(TrackS) + Win(StrikeS);   // v2.1: the strike window is 0.7 s on Hard


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
            Clip("HourHands", Duration() + 1);   // the glaive levelled like a clock hand, struck as the spokes resolve
            var party = Standing();
            var aim = Aimable();   // spokes through players not reviving (AUR-TARGET-OTHER, 7 Nov); the timing still follows everyone standing
            if (party.Count <= 1)
            {
                if (party.Count == 0) yield break;
                var p = aim.FirstOrDefault();
                if (p == null) { yield return Duration(); yield break; }
                Spoke(p, "I"); if (Hard) Spoke(p, "I", true);
                yield return LockTicks - 1;              // spoke I locks during this tick's hazard step
                Say(Name);
                var who = Aimable().FirstOrDefault();   // none if you are reviving by now: no spoke II
                if (who) { Spoke(who, "II"); if (Hard) Spoke(who, "II", true); }
                yield return ResolveTicks - 1;           // spoke II resolves this tick
                yield break;
            }

            var spokes = aim.Select(pl => Spoke(pl, null)).ToList();
            if (Hard) spokes.AddRange(aim.Select(pl => Spoke(pl, null, true)).ToList());   // mirrors after every own spoke: a shared hour keeps the own one
            yield return LockTicks;                      // every spoke locked on the previous tick: its hour is final
            Say(Name);
            // one spoke per hour: players sharing an hour share the first spoke on it
            foreach (var g in spokes.Where(h => !h.Cancelled).GroupBy(h => Mathf.RoundToInt(h.Yaw / HourDeg) % 12))
                foreach (var dup in g.Skip(1)) W.Hazards.Cancel(dup);
            yield return ResolveTicks - 1 - LockTicks;   // the spokes resolve this tick
        }

        Hazard Spoke(PlayerController target, string tag, bool mirror = false)
        {
            var from = BossPos; float turn = mirror ? 180f : 0f;
            var h = new Hazard
            {
                Source = (tag == null ? Name : Name + " " + tag) + (mirror ? " mirror" : ""), AttackId = Id,
                Shape = HazardShape.Lane, Kind = HazardKind.Strike,
                Origin = from, Yaw = Mathf.Repeat(HourOf(from, target.transform.position, Body.eulerAngles.y) + turn, 360f),
                Length = Length, Width = Width,
                LockAt = LockTicks, ResolveAt = ResolveTicks,
                Damage = Damage, Knockback = 1.5f, Stun = Sim.Ticks(0.25f),
            };
            h.Track = x =>
            {
                x.Origin = BossPos;
                if (target && !target.Down) x.Yaw = Mathf.Repeat(HourOf(x.Origin, target.transform.position, x.Yaw - turn) + turn, 360f);
            };
            // Telegraph style: the standard coral spoke from the start (a thin gold line would read as a friendly effect);
            // the shared lock flash marks the lock
            h.OnResolve = x => Beam(x);   // a light beam down the spoke as it strikes (8 Nov graphics pass)
            return Spawn(h);
        }
    }
}
