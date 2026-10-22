using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // 06 Zenith Collapse (aurelion.zenith_collapse), all difficulties, spec abilities-v2/ABILITIES-v2.1.md §1 (proposal).
    // The Regent stands at the meridian (the phase plan walks him to the arena centre first) and the whole sky falls on
    // the dais except a disc of shade at his feet: a Ground annulus on the boss, inner radius 4.5 m (N <= 2), 5.5 m
    // (N 3-4) or 6.5 m (N >= 5), 5.0 m at N <= 2 in Phase 3, outer 30 m (past the bound). Locked at once, resolves 3.0 s
    // later (Hard 2.4 s, Easy 3.75 s). 45 damage, no knockback, 0.25 s stun. The "come close" move: ranged must run in;
    // its layer partner, Meridian Lances (resolving 0.3 s after it), makes the disc deadly for anyone standing still.
    // Run returns on the resolve tick. Look (stand-in): the shared coral annulus telegraph with its crisp inner edge;
    // the inward-sliding orrery decor rings are not built yet.
    public sealed class ZenithCollapse : AurelionAbility
    {
        public override string Id => "aurelion.zenith_collapse";
        public override string Name => "Zenith Collapse";

        public const float Outer = 30f, WindowS = 3.0f, HardWindowS = 2.4f, StunS = 0.25f;
        public const int Damage = 45;

        public static float SafeRadius(int n, int phase) => n <= 2 ? (phase >= 3 ? 5.0f : 4.5f) : n <= 4 ? 5.5f : 6.5f;
        public int WindowTicks => Sim.Ticks(Hard ? HardWindowS : WindowS * WarnScale);
        // ticks from Run's first tick to its resolve tick (= when Run returns)
        public int Duration() => WindowTicks - 1;

        public Hazard Last { get; private set; }   // test evidence

        public IEnumerable<int> Run()
        {
            Say(Name);
            int win = WindowTicks;
            Clip("MeridianAlignment", win);   // stand-in: glaive lifted straight up
            Last = Spawn(new Hazard
            {
                Source = Name, Shape = HazardShape.Ring, Kind = HazardKind.Ground, Origin = BossPos,
                Inner = SafeRadius(N, Phase), Radius = Outer, LockAt = 0, ResolveAt = win,
                Damage = Damage, Knockback = 0, Stun = Sim.Ticks(StunS),
            });
            Last.OnResolve = hz => { W.BossFx.TollResolve(hz.Origin, hz.Inner, Rim); Flare(1.4f); };
            for (int i = 0; i < win - 1; i++) { Glow(i / (float)win); yield return 1; }
        }
    }
}
