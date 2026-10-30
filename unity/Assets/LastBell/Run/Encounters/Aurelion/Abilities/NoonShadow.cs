using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // 03 Noon Shadow (aurelion.noon_shadow), brief abilities-v2/03-noon_shadow.md (proposal).
    // Run(afternoon) floods half the dais with noon light: a Ground cone from the arena centre, half-angle 90°, so the
    // dividing line runs straight through the compass star. The lit half is the one holding the standing players' mean;
    // with the mean within 1 m of the centre (2 players on opposite sides) it is the half holding the farthest player.
    // The side is picked and locked on the warning's first tick and resolves 3.8 s later (45, no knockback, 0.25 s stun).
    // Afternoon (afternoon = Phase 3, or always when solo): the other half warns on Noon's resolve tick and resolves
    // 3.8 s after that. Easy stretches both windows by 1.25, Hard shortens them to 2.66 s (x0.7, v2.1 §2: sprint from the
    // far edge), through Win. Returns on the tick the last half resolves, Duration(afternoon) ticks after its first tick.
    // The cone reaches Rim + 0.5 m instead of the brief's 28 m: every point a player can stand on is covered either way
    // (players are held inside the round 13.9 m bound) and the light stays on the dais instead of spilling into the sky.
    // The cone's 0.6 m core and its 0.3 m margin make the line itself unsafe: step at least 0.3 m past it.
    public sealed class NoonShadow : AurelionAbility
    {
        public override string Id => "aurelion.noon_shadow";
        public override string Name => "Noon Shadow";

        public const float WindowS = 3.8f, MeanDeadZone = 1f, ReachPastRim = 0.5f;
        public const int Damage = 45, Stun = 15;   // 0.25 s stun, no knockback


        // test evidence: the last run's halves (Noon, then Afternoon), the yaw each one lit, and their spawn / resolve ticks
        public readonly List<Hazard> Halves = new List<Hazard>();
        public readonly List<float> LitYaws = new List<float>();
        public readonly List<int> SpawnTicks = new List<int>();
        public readonly List<int> ResolveTicks = new List<int>();
        public float Reach => Rim + ReachPastRim;

        public IEnumerable<int> Run(bool afternoon)
        {
            Say(Name);
            Halves.Clear(); LitYaws.Clear(); SpawnTicks.Clear(); ResolveTicks.Clear();
            bool twice = afternoon || N <= 1;
            int window = Win(WindowS);
            Glow(1.4f); Clip("MeridianAlignment", twice ? 2 * window : window);

            float yaw = PickYaw();
            Half(yaw, window);
            yield return window - 1;                                  // Noon's resolve tick
            if (!twice) yield break;

            Say("Afternoon");
            Half(yaw + 180f, window);
            yield return window - 1;                                  // Afternoon's resolve tick
        }

        // ticks from Run's first tick to its last resolve tick (= when Run returns); the v2.1 plan lines pairs up with it.
        // CONTRACT: keep it equal to Run's real timing when the windows change.
        public int Duration(bool afternoon) => (afternoon || N <= 1 ? 2 : 1) * (Win(WindowS) - 1);

        // the half holding the standing players' mean; near the centre, the half holding the farthest player
        public float PickYaw()
        {
            var party = Standing();
            if (party.Count == 0) return Body.eulerAngles.y;
            var mean = Vector3.zero;
            foreach (var p in party) mean += Sim.Flat(p.transform.position);
            mean /= party.Count;
            var to = mean - Centre;
            if (to.magnitude < MeanDeadZone) to = Sim.Flat(Farthest(Centre).transform.position) - Centre;
            return Mathf.Repeat(Sim.YawTo(Centre, Centre + to, Body.eulerAngles.y), 360f);
        }

        Hazard Half(float yaw, int resolveAt)
        {
            yaw = Mathf.Repeat(yaw, 360f);
            var h = new Hazard
            {
                Source = Name, Shape = HazardShape.Cone, Kind = HazardKind.Ground,
                Origin = Centre, Yaw = yaw, HalfAngle = 90f, Radius = Reach,
                LockAt = 0, ResolveAt = resolveAt, Damage = Damage, Knockback = 0, Stun = Stun,
            };
            // Telegraph style (Reference/Telegraph-Style v2): the standard coral half with the Skyward rays, its own plain
            // border is the dividing line (no separate gold gnomon line over the outline or across the safe half)
            h.OnResolve = hz => { ResolveTicks.Add(W.Tick); Flare(1.2f); };
            Spawn(h);
            Halves.Add(h); LitYaws.Add(yaw); SpawnTicks.Add(W.Tick);
            return h;
        }
    }
}
