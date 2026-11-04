using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Falling Meridians (aurelion.falling_meridians), v4 Phase 3 (Exposed Meridian), accepted 2 Nov (Chzy18: "make it harder
    // by giving the players something to do/dodge"). Every 3.5 s (5 s until 3 Nov) each standing player gets a 2.5 m circle at their feet, locked
    // where they stood, landing 1.2 s later (Easy 1.5 s, Hard 1.0 s): 30 damage, so you keep moving. One per player, so it
    // reads the same for 1-4 players. The encounter calls Step every tick over the Phase 3 blocks, not during Severance.
    public sealed class FallingMeridians : AurelionAbility
    {
        public override string Id => "aurelion.falling_meridians";
        public override string Name => "Falling Meridians";

        public const float Radius = 2.5f, EveryS = 3.5f, WarnS = 1.2f, WarnEasyS = 1.5f, WarnHardS = 1.0f;
        public const int Damage = 30;
        public int Warn => Sim.Ticks(Easy ? WarnEasyS : Hard ? WarnHardS : WarnS);
        public int Casts { get; private set; }
        public readonly List<Hazard> Last = new List<Hazard>();   // test evidence: the latest wave
        int clock;

        public void Reset() => clock = 0;
        public void Step()
        {
            if (++clock < Sim.Ticks(EveryS)) return;
            clock = 0; Casts++; Last.Clear();
            var players = W.Players;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (!p || p.Down) continue;
                var h = Spawn(new Hazard
                {
                    Source = Name, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = Sim.Flat(p.transform.position),
                    Radius = Radius, LockAt = 0, ResolveAt = Warn, Damage = Damage, Knockback = 0, Stun = 0,
                });
                h.OnResolve = hz => W.BossFx.ChimeResolve(hz.Origin, hz.Radius);
                Last.Add(h);
            }
        }
        public override void Abort() => clock = 0;
    }
}
