using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // NEW in v3 (ABILITIES-v3.md), filler: Slag Spit (crucivar.slag_spit). The LEFT crucible flicks molten slag at the
    // FARTHEST standing player: a circle r 2.5 m that follows them until 0.8 s, then locks and lands at 2.0 s. 30, Ground,
    // 1 m knockback. With 4-7 players it targets the two farthest, with 8 the three farthest, each with its own circle.
    // Ranged pressure; alternates with Anvil Sweep. Easy stretches the times by 1.25.
    public sealed class SlagSpit : CrucivarAbility
    {
        public override string Id => "crucivar.slag_spit";

        public const float Radius = 2.5f, TrackUntil = 0.8f, ResolveAt = 2.0f, Knock = 1f;
        public const int Damage = 30;
        public readonly List<Hazard> Circles = new List<Hazard>();
        public static int Targets(int n) => n >= 8 ? 3 : n >= 4 ? 2 : 1;

        public IEnumerable<int> Run()
        {
            Boss.SetCurrent("Slag Spit");
            Circles.Clear();
            var boss = Flat(Boss.transform.position);
            var standing = Standing();
            if (standing.Count == 0) yield break;
            var targets = standing.OrderByDescending(p => Flat(p.transform.position - boss).magnitude).Take(Targets(standing.Count)).ToList();
            int resolve = T(ResolveAt);
            foreach (var who in targets)
            {
                var h = new Hazard
                {
                    Source = "Slag Spit", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = Bounded(who.transform.position),
                    Radius = Radius, LockAt = T(TrackUntil), ResolveAt = resolve, Damage = Damage, Knockback = Knock,
                };
                h.Track = hz => { if (!who.Down) hz.Origin = Bounded(who.transform.position); };
                h.OnResolve = hz => W.BossFx.SlagSplash(hz.Origin, hz.Radius);
                Circles.Add(W.Hazards.Spawn(h));
            }
            Boss.Rig.Pour(true);
            Boss.Presenter.PlayOnce("SlagCast", resolve / (float)Sim.Hz);
            for (int i = 0; i < resolve; i++) { Boss.Glow(0.6f * i / resolve); yield return 1; }
            Boss.Rig.Pour(false);
        }

        // a circle centre stays on the floor
        static Vector3 Bounded(Vector3 p)
        {
            p = Flat(p); float max = Rim;
            return p.magnitude > max ? p.normalized * max : p;
        }
    }
}
