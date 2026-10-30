using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // NEW in v3 (ABILITIES-v3.md), filler: Anvil Sweep (crucivar.anvil_sweep). The RIGHT hammer sweeps a 120 deg cone
    // (half 60), reach 5.5 m from the boss centre, at the NEAREST standing player: the boss turns to follow until 0.8 s,
    // then the cone locks and lands at 2.0 s. 35, Strike (Brace helps from the front), 1.5 m knockback. Melee pressure;
    // the phase plan alternates it with Slag Spit between abilities. Easy stretches the times by 1.25.
    public sealed class AnvilSweep : CrucivarAbility
    {
        public override string Id => "crucivar.anvil_sweep";

        public const float Reach = 5.5f, HalfAngle = 60f, TrackUntil = 0.8f, ResolveAt = 2.0f, Knock = 1.5f, TurnRate = 300f;
        public const int Damage = 35;
        public Hazard Last { get; private set; }

        public IEnumerable<int> Run()
        {
            Boss.SetCurrent("Anvil Sweep");
            var target = W.NearestStanding(Boss.transform.position);
            if (target != null) Boss.transform.rotation = Quaternion.Euler(0, Sim.YawTo(Boss.transform.position, target.transform.position, Boss.transform.eulerAngles.y), 0);
            int resolve = T(ResolveAt);
            var h = Last = new Hazard
            {
                Source = "Anvil Sweep", Shape = HazardShape.Cone, Kind = HazardKind.Strike, Origin = Flat(Boss.transform.position),
                Yaw = Boss.transform.eulerAngles.y, Radius = Reach, HalfAngle = HalfAngle, LockAt = T(TrackUntil), ResolveAt = resolve,
                Damage = Damage, Knockback = Knock,
            };
            h.Track = hz =>
            {
                var t = W.NearestStanding(Boss.transform.position); if (t == null) return;
                var want = Quaternion.Euler(0, Sim.YawTo(Boss.transform.position, t.transform.position, Boss.transform.eulerAngles.y), 0);
                Boss.transform.rotation = Quaternion.RotateTowards(Boss.transform.rotation, want, TurnRate * Sim.Dt);
                hz.Origin = Flat(Boss.transform.position); hz.Yaw = Boss.transform.eulerAngles.y;
            };
            h.OnResolve = hz => { W.BossFx.SteamRelease(hz.Origin, hz.Yaw, hz.Radius, hz.HalfAngle); Boss.Flare(1f); };
            W.Hazards.Spawn(h);
            Boss.Presenter.PlayOnce("Diebreaker", resolve / (float)Sim.Hz);
            for (int i = 0; i < resolve; i++) { Boss.Glow(i / (float)resolve); yield return 1; }
        }
    }
}
