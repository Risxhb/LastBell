using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // 02 Starfall Mirror (aurelion.starfall_mirror), brief abilities-v2/02-starfall_mirror.md (proposal).
    // A star (Ground circle, 2.2 m; 1.8 m at N >= 5) follows each standing player for 2.0 s, locks, and resolves 1.1 s
    // later. Its reflection through the arena centre, (-x, -z), is a second circle of the same size that tracks and locks
    // with it. 30 per circle, no knockback, 0.25 s stun. Solo: star II (and its mirror) is called on star I's lock and
    // runs the same 2.0 + 1.1 s. Run returns on the tick the last star resolves; it never assumes it runs alone (the
    // phase plan starts it inside Noon Shadow, Orrery Closing and the Horizon).
    // Ticks (Medium) are counted from the cast, as hazard Age: lock 120, resolve 186; solo star II spawns at 120, locks
    // at 240, resolves at 306. Easy stretches the follow and the window by 1.25. Hard (v2.1 §2) keeps the 2.0 s follow and
    // cuts the window x0.7 to 0.77 s (resolve 166; solo 286), except Run(fullWindow: true), the Lances pairing (v2.1 §3),
    // which keeps the full 1.1 s.
    public sealed class StarfallMirror : AurelionAbility
    {
        public override string Id => "aurelion.starfall_mirror";
        public override string Name => "Starfall Mirror";

        public const float FollowS = 2.0f, WindowS = 1.1f, StunS = 0.25f;
        public const float Radius = 2.2f, RadiusCrowd = 1.8f;      // N <= 4 / N >= 5, so 16 circles fit
        public const int CrowdFrom = 5, Dmg = 30;
        public const string MirrorTag = " (reflection)", SecondTag = " II";

        public static float RadiusFor(int n) => n >= CrowdFrom ? RadiusCrowd : Radius;
        public static Vector3 Reflect(Vector3 p) => new Vector3(-p.x, 0, -p.z);   // through the arena centre (the compass star)
        public int FollowTicks => T(FollowS);
        public int WindowTicks(bool fullWindow = false) => fullWindow ? T(WindowS) : Win(WindowS);
        public int ResolveTicks(bool fullWindow = false) => FollowTicks + WindowTicks(fullWindow);

        // one called star: its owner, the star on them and its reflection (test evidence and presentation)
        public sealed class Star { public PlayerController Who; public Hazard Main, Mirror; public int Index; }
        public readonly List<Star> Stars = new List<Star>();       // every star of the latest cast, star I first
        public int Casts { get; private set; }

        // ticks from Run's first tick to the last star's resolve tick; the v2.1 plan lines pairs up with it.
        // CONTRACT: keep it equal to Run's real timing when the windows change. fullWindow: v2.1 §3, paired with Meridian
        // Lances the star keeps its full 1.1 s window on Hard (Run and Duration both use WindowTicks).
        public int Duration(bool fullWindow = false) => (Standing().Count <= 1 ? FollowTicks : 0) + ResolveTicks(fullWindow) - 1;

        public IEnumerable<int> Run(bool fullWindow = false)
        {
            Say(Name); Casts++; Stars.Clear();
            var party = Standing();
            int follow = FollowTicks, resolve = ResolveTicks(fullWindow);
            float r = RadiusFor(party.Count);
            bool solo = party.Count <= 1;
            Clip("ZenithPins", solo ? follow + resolve : resolve);   // halo arcs tilt and point at every player
            foreach (var p in party) Call(p, r, follow, resolve, 1);
            for (int i = 0; i < follow; i++) { Glow(i / (float)follow); yield return 1; }
            Flare(0.5f);
            if (solo)
            {
                // star II hunts you from star I's lock: leave, avoid your reflection, leave again
                foreach (var p in Standing()) Call(p, r, follow, resolve, 2);
                for (int i = 0; i < follow; i++) { Glow(i / (float)follow); yield return 1; }
                Flare(0.5f);
            }
            yield return resolve - follow;
            Glow(0);
        }

        void Call(PlayerController who, float r, int follow, int resolve, int index)
        {
            string tag = index == 2 ? SecondTag : "";
            var at = Sim.Flat(who.transform.position);
            var star = new Hazard
            {
                Source = Name + tag, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Radius = r, Origin = at,
                LockAt = follow, ResolveAt = resolve, Damage = Dmg, Knockback = 0, Stun = Sim.Ticks(StunS),
            };
            var mirror = new Hazard
            {
                Source = Name + tag + MirrorTag, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Radius = r, Origin = Reflect(at),
                LockAt = follow, ResolveAt = resolve, Damage = Dmg, Knockback = 0, Stun = Sim.Ticks(StunS),
            };
            var s = new Star { Who = who, Main = star, Mirror = mirror, Index = index };
            // the star follows its player until the lock; a recipient who goes down has both circles withdrawn
            star.Track = hz =>
            {
                if (!who || who.Down) { W.Hazards.Cancel(hz); W.Hazards.Cancel(mirror); return; }
                hz.Origin = Sim.Flat(who.transform.position);
            };
            // set before Spawn so a test's Spawned hook sees it; the mirror follows its star (the star tracks first each tick)
            mirror.Track = hz => { hz.Origin = Reflect(star.Origin); };
            star.OnResolve = hz => W.BossFx.ChimeResolve(hz.Origin, hz.Radius);
            mirror.OnResolve = hz => W.BossFx.ChimeResolve(hz.Origin, hz.Radius);
            // Telegraph style: both circles are the standard coral hit shape with the Skyward rays and nothing else (the
            // old porcelain after-image ring sat outside the hit shape and was dotted, which reads as a friendly effect)
            Spawn(star); Spawn(mirror);
            Stars.Add(s);
        }
    }
}
