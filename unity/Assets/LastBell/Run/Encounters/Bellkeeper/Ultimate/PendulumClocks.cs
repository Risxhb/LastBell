using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Pendulum Clocks (`bellkeeper.ult_pendulum_clocks`), Bellkeeper Ultimate — proposal. Design: ultimate/ULTIMATE.md §3.1,
    // §5, §6; strat: GUIDE.md "Pendulum Clocks".
    // Cue (set by the plan, BellkeeperUltimate.cs): launch = numerals and hands appear (≥ 6 s before hour I). Variant "singles":
    // hour k strikes At[k] (I..IV); "pairs": I+II strike At[0], III+IV At[1] (solo: II and IV 2.0 s after I and III). Each
    // strike resolves exactly on its At tick.
    //
    // Each player gets an hour (ivory numeral) and a brass hand toward one of the four rim stations (UltimateState.StationDir,
    // NE/SE/SW/NW); the shared glyph shows the player's next hour. At the hour the player's own clock strikes a coral cone
    // from them toward that station: radius 6 m, half-angle 25°, Strike. It draws 3.0 s before, tracks its owner and locks
    // 1.5 s before. It must include the Bellkeeper's body or the hour cracks (Ult.CrackedHour: 30 to everyone, the owner deals
    // nothing for 20 s); anyone else inside takes 60. So the striker stands on the far side of the boss from the station,
    // within 7.4 m of his centre, with nobody between.
    // Hands: a seeded start station and turning direction; hour k's first hand is start + k·turn (solo circles the boss).
    // Players five to eight are each a second striker on one hour, hand at right angles to the first (never the same hand
    // within an hour). Paired hours (I+II, III+IV) use adjacent stations, so a Leaning Bell resolving with them leans toward
    // both (LeanDirAt, the bisector). With five or more in pairs, II (IV) takes the same two hands as I (III), swapped: a pair
    // tick holds only two adjacent hands, each held twice by two strikers side by side (Side ±1: 10-30° each side of the
    // line, at least ≈ 1.5 m apart), so the bisector lean spares all four (Ultimate session, 2026-10-23).
    // Hardened (proposal, recorded in the report): a striker who is down when their hour comes still cracks it.
    public sealed class PendulumClocks : UltimateMechanic
    {
        public override string Id => UltimateMechanics.PendulumClocks;
        public override string Name => "Pendulum Clocks";

        public const float Reach = 6f, HalfAngle = 25f;                     // the clock strike's cone
        public const int DrawTicks = 180, LockBefore = 90;                    // draws 3.0 s before, locks 1.5 s before
        public const int AllyDamage = 60;                                     // an ally inside your cone (Heavy)
        public const int SoloPairGap = 120;                                   // solo "pairs": II and IV 2.0 s after I and III
        // the bot's clock spot, inside the guide's (3.8-7.4 m from the boss, on the line through him)
        public const float SpotMin = 4.4f, SpotMax = 6.6f, SpotHint = 5.2f, SpotArc = 20f;
        // two strikers on one hand (pairs, 5+ players): each takes one half, 10-30° off the line, 4.4-6.6 m out (at least 20° =
        // ≈ 1.5 m apart; worst pair 6.6 m at +10° / 4.4 m at -10°: 38° off the cone's axis, outside its 25° + margin, and both
        // cones still cross the boss's body)
        public const float SharedMin = 4.4f, SharedMax = 6.6f, SharedHint = 5.2f, SharedArcIn = 10f, SharedArcOut = 30f;
        public const string Source = "Pendulum Clock", CrackSource = "Cracked Hour";

        // One clock hour of one player.
        public sealed class Strike
        {
            public PlayerController Owner;
            public int Hour;            // 1..4 (I..IV)
            public int Station;         // 0..3, UltimateState.StationDir
            public int Tick;            // world tick it strikes
            public int Side;            // 0 = alone on its hand; ±1 = shares it this tick: which half of the clock spot (side by side)
            public Hazard Cone;         // live from DrawTick
            public bool Resolved, StruckBoss, Cracked;
            public readonly List<PlayerController> Struck = new List<PlayerController>();
            public int DrawTick => Tick - DrawTicks;
            public int LockTick => Tick - LockBefore;
            internal int predTick = -1;
            internal readonly List<(Vector3 origin, float yaw)> pred = new List<(Vector3, float)>();
        }
        readonly List<Strike> strikes = new List<Strike>();
        public IReadOnlyList<Strike> Strikes => strikes;     // this cast's hours (tests)
        public int StartStation { get; private set; }
        public int Turn { get; private set; } = 1;          // +1 or -1 round the stations
        public int Cracks { get; private set; }             // hours that cracked this cast
        public int Beats { get; private set; }              // pendulum beats this cast (one per striking tick)

        public override UltCue DefaultCue() => new UltCue { Variant = "singles", At = new[] { 6f, 11f, 16f, 21f } };

        // -lastbellAbility ult_pendulum_clocks: loops the clocks with PracticeCue (tests set it; null = DefaultCue). Set here,
        // after the encounter's start has cleared NextCue.
        public static UltCue PracticeCue;
        public override IEnumerable<int> Practice()
        {
            NextCue = PracticeCue;
            Boss.Launch(this);
            while (Running) yield return 1;
            yield return 120;
        }

        // Leaning Bell pairing rule (§3.1): the direction a Leaning Bell resolving on worldTick must lean (toward the striking
        // hour's station(s): each hand once, summed and normalised = the bisector of a pair); null if no clock strikes on that
        // tick, or if the hands cancel out.
        public Vector3? LeanDirAt(int worldTick)
        {
            var sum = Vector3.zero; bool any = false; int seen = 0;
            foreach (var s in strikes)
                if (s.Tick == worldTick && (seen & (1 << s.Station)) == 0) { seen |= 1 << s.Station; sum += UltimateState.StationDir(s.Station); any = true; }
            if (!any || sum.magnitude < 0.3f) return null;
            return sum.normalized;
        }

        // ---------------- assignment ----------------
        // the world tick each hour I..IV (k = 0..3) strikes on, for this cue and party size
        int[] HourTicksFor(int n)
        {
            var at = new int[4];
            if (Cue.Variant == "pairs")
            {
                int a = TickAt(Cue.AtOr(0, 6f)), b = TickAt(Cue.AtOr(1, 12.5f));
                at[0] = a; at[2] = b;
                at[1] = n == 1 ? a + SoloPairGap : a; at[3] = n == 1 ? b + SoloPairGap : b;
            }
            else
                for (int k = 0; k < 4; k++) at[k] = TickAt(Cue.AtOr(k, 6f + 5f * k));
            return at;
        }

        void Assign()
        {
            strikes.Clear(); Cracks = 0; Beats = 0;
            var party = W.Players.Where(p => !p.Down).ToList();
            if (party.Count == 0) return;
            var rng = new System.Random(Cue.Seed * 31 + Casts * 7 + 17);
            for (int i = party.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (party[i], party[j]) = (party[j], party[i]); }   // seeded roles
            StartStation = rng.Next(4); Turn = rng.Next(2) == 0 ? 1 : -1;
            var at = HourTicksFor(party.Count);
            bool shareHands = Cue.Variant == "pairs" && party.Count >= 5;
            for (int k = 0; k < 4; k++)
            {
                int first = Mod4(StartStation + Turn * k), second = Mod4(first + (k % 2 == 0 ? -Turn : Turn));   // right angles
                // pairs with five or more: II (IV) takes the same two adjacent hands as I (III), swapped, so a pair tick holds
                // only two hands, each twice (the two strikers of a hand stand side by side) and a Leaning Bell can spare all four
                if (shareHands && k % 2 == 1) { int a = Mod4(StartStation + Turn * (k - 1)); first = Mod4(a - Turn); second = a; }
                strikes.Add(new Strike { Owner = party[k % Mathf.Min(4, party.Count)], Hour = k + 1, Station = first, Tick = at[k] });
                int extra = 4 + k;   // players five to eight: a second striker on hour k
                if (extra < party.Count) strikes.Add(new Strike { Owner = party[extra], Hour = k + 1, Station = second, Tick = at[k] });
            }
            strikes.Sort((a, b) => a.Tick != b.Tick ? a.Tick.CompareTo(b.Tick) : a.Hour.CompareTo(b.Hour));
            // a hand held twice on one tick: the earlier hour's striker takes the +side half of the clock spot, the later the -side
            foreach (var g in strikes.GroupBy(s => (s.Tick, s.Station)).Where(g => g.Count() == 2))
            { var two = g.OrderBy(s => s.Hour).ToList(); two[0].Side = 1; two[1].Side = -1; }
            foreach (var p in party) ShowNext(p);
        }
        static int Mod4(int i) => ((i % 4) + 4) % 4;

        // the glyph shows the player's next hour; the hand's station stays after the last one (the Stopped Hour reads it)
        void ShowNext(PlayerController p)
        {
            var next = strikes.FirstOrDefault(s => s.Owner == p && !s.Resolved);
            if (next != null) Ult.SetClock(p, next.Hour, next.Station, next.Tick);
            else Ult.ClearClock(p);
        }

        // ---------------- the cast ----------------
        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name);
            Assign();
            if (strikes.Count == 0) yield break;
            int end = strikes.Max(s => s.Tick);
            while (true)
            {
                foreach (var s in strikes)
                    if (s.Cone == null && Now >= s.DrawTick) Draw(s);
                if (Now >= end) break;
                yield return 1;
            }
            yield return 1;   // the last cones resolve on `end`, after this step
            foreach (var p in strikes.Select(s => s.Owner).Distinct()) if (p) Ult.ClearClock(p);
        }

        void Draw(Strike s)
        {
            int left = Mathf.Max(0, s.Tick - Now);
            var h = new Hazard
            {
                Source = Source, Shape = HazardShape.Cone, Kind = HazardKind.Strike, Radius = Reach, HalfAngle = HalfAngle,
                ResolveAt = UltShapes.ResolveIn(left), LockAt = Mathf.Max(0, left - LockBefore + 1), Damage = AllyDamage, HitOnResolve = false,
            };
            Aim(h, s);
            h.Track = hz => Aim(hz, s);   // last tracked on LockTick
            h.OnResolve = hz => Resolve(s, hz);
            s.Cone = Spawn(h);
        }
        // the cone sits on its owner and points at the hand's station (the rim point, so the line runs through a centred boss)
        static void Aim(Hazard h, Strike s)
        {
            if (!s.Owner) return;
            var from = Sim.Flat(s.Owner.transform.position);
            h.Origin = from; h.Yaw = YawToward(from, UltimateState.Station(s.Station));
        }
        static float YawToward(Vector3 from, Vector3 to)
        {
            var d = Sim.Flat(to - from); if (d.sqrMagnitude < 1e-4f) d = Vector3.forward;
            return Quaternion.LookRotation(d).eulerAngles.y;
        }

        void Resolve(Strike s, Hazard hz)
        {
            s.Resolved = true;
            Beat();
            if (s.Owner && !s.Owner.Down)
            {
                W.BossFx.SweepResolve(hz.Origin, hz.Yaw, hz.Radius, hz.HalfAngle);
                foreach (var p in W.Players.ToArray())
                {
                    if (p == s.Owner || p.Down || hz.Hit.Contains(p) || !hz.Contains(p.transform.position)) continue;
                    hz.Hit.Add(p); s.Struck.Add(p); p.ReceiveHit(hz);
                }
                s.StruckBoss = Boss.Solid && Includes(hz.Origin, hz.Yaw, Sim.Flat(Boss.transform.position), Boss.BodyRadius);
            }
            if (!s.StruckBoss) { s.Cracked = true; Cracks++; Ult.CrackedHour(s.Owner, CrackSource); }
            if (s.Owner) ShowNext(s.Owner);
        }

        // the pendulum in the chest beats once per hour and the halo turns a notch (stand-in: a flare; rig clip HourTick)
        int lastBeat = -1;
        void Beat()
        {
            if (lastBeat == Now) return;
            lastBeat = Now; Beats++;
            if (Boss.Presenter) Boss.Presenter.PlayOnce("HourTick", 0.25f);
            Boss.Flare(0.4f);
        }

        // ---------------- geometry ----------------
        // Does a clock cone from `origin` with `yaw` reach the boss's body (a disc of bodyRadius at bossPos)? slack > 0 asks
        // for that much margin: metres on the reach, slack x 10 degrees on the angle (the bot's safe version).
        public static bool Includes(Vector3 origin, float yaw, Vector3 bossPos, float bodyRadius, float slack = 0)
        {
            var to = Sim.Flat(bossPos - origin); float d = to.magnitude;
            if (d <= bodyRadius) return true;
            if (d > Reach + bodyRadius - slack) return false;
            float tangent = Mathf.Asin(Mathf.Clamp01(bodyRadius / d)) * Mathf.Rad2Deg;
            return Vector3.Angle(Sim.Dir(yaw), to) <= HalfAngle + tangent - slack * 10;
        }
        public static bool StrikesFrom(Vector3 origin, int station, Vector3 bossPos, float bodyRadius, float slack = 0) =>
            Includes(origin, YawToward(Sim.Flat(origin), UltimateState.Station(station)), bossPos, bodyRadius, slack);
        // Hazard.Contains for a clock cone, without making a Hazard
        static bool InCone(Vector3 origin, float yaw, Vector3 q)
        {
            var v = Sim.Flat(q - origin); float d = v.magnitude;
            if (d > Reach + Hazard.Margin) return false;
            if (d < 0.6f) return true;
            return Vector3.Angle(Sim.Dir(yaw), v) <= HalfAngle + Mathf.Rad2Deg * Mathf.Atan2(Hazard.Margin, d);
        }

        // The guide's clock spot for a station: the far side of the boss from it, `dist` from his centre (turned by turnDeg).
        public Vector3 Spot(int station, float dist = SpotHint, float turnDeg = 0)
        {
            var bp = Sim.Flat(Boss.transform.position);
            var away = Sim.Flat(bp - UltimateState.Station(station)); if (away.sqrMagnitude < 1e-4f) away = -UltimateState.StationDir(station);
            var q = bp + Quaternion.Euler(0, turnDeg, 0) * away.normalized * dist;
            float lim = CombatWorld.PlayerBound - 0.8f;
            return q.magnitude > lim ? q.normalized * lim : q;
        }
        // A striker's clock spot: the whole spot on the line (Side 0), or its own half when it shares the hand this tick.
        float ArcFrom(Strike s) => s.Side == 0 ? -SpotArc : s.Side > 0 ? SharedArcIn : -SharedArcOut;
        float ArcTo(Strike s) => s.Side == 0 ? SpotArc : s.Side > 0 ? SharedArcOut : -SharedArcIn;
        float RMin(Strike s) => s.Side == 0 ? SpotMin : SharedMin;
        float RMax(Strike s) => s.Side == 0 ? SpotMax : SharedMax;
        public Vector3 Hint(Strike s) => s.Side == 0 ? Spot(s.Station) : Spot(s.Station, SharedHint, s.Side * (SharedArcIn + SharedArcOut) / 2);
        public bool InSpot(Vector3 q, Strike s)
        {
            var bp = Sim.Flat(Boss.transform.position);
            var v = Sim.Flat(q - bp); float d = v.magnitude;
            if (d < RMin(s) || d > RMax(s)) return false;
            float a = Vector3.SignedAngle(Sim.Flat(bp - UltimateState.Station(s.Station)), v, Vector3.up);
            return a >= ArcFrom(s) && a <= ArcTo(s) && StrikesFrom(q, s.Station, bp, Boss.BodyRadius, 0.3f);
        }
        // Where a striker's cone can be: the live cone (tracking or locked) and, until it locks, any clock spot it may still take.
        bool InAnyCone(Strike s, Vector3 q)
        {
            if (s.Cone != null && !s.Cone.Cancelled && s.Cone.Contains(q)) return true;
            if (s.Cone != null && s.Cone.Locked) return false;
            if (s.predTick != Now)
            {
                s.predTick = Now; s.pred.Clear();
                foreach (float r in new[] { RMin(s), (RMin(s) + RMax(s)) / 2, RMax(s) })
                    foreach (float a in new[] { ArcFrom(s), (ArcFrom(s) + ArcTo(s)) / 2, ArcTo(s) })
                    {
                        var o = Spot(s.Station, r, a);
                        s.pred.Add((o, YawToward(o, UltimateState.Station(s.Station))));
                    }
            }
            foreach (var (o, yaw) in s.pred) if (InCone(o, yaw, q)) return true;
            return false;
        }

        // ---------------- the strat (GUIDE.md) ----------------
        // At your hour be on your clock spot when your cone locks (far side of the boss from your station, 3.8-7.4 m, the cone
        // through his body); at every other striker's hour stay out of their cone.
        public override void Demands(PlayerController p, List<BotDemand> into)
        {
            if (!Running || p.Down) return;
            foreach (var s in strikes)
            {
                if (s.Resolved || s.Tick < Now) continue;
                var st = s;
                if (s.Owner == p)
                    into.Add(new BotDemand
                    {
                        Tick = Mathf.Max(Now, s.LockTick), Since = LaunchTick, Ok = q => InSpot(q, st), Hint = Hint(s),
                        Memory = true, Why = $"clock {UltimateState.Roman[s.Hour]} {UltimateState.StationNames[s.Station]}", From = this,
                    });
                else if (s.Owner && !s.Owner.Down)
                    into.Add(new BotDemand
                    {
                        Tick = s.Tick, Since = LaunchTick, Ok = q => !InAnyCone(st, q), Hint = Spot(Mod4(s.Station + 2)),
                        Why = $"out of clock {UltimateState.Roman[s.Hour]}", From = this,
                    });
            }
        }

        public override void Cancel()
        {
            base.Cancel();
            strikes.Clear(); lastBeat = -1;
        }
    }
}
