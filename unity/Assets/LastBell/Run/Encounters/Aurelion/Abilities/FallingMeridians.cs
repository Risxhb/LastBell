using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Falling Meridians (aurelion.falling_meridians), v4 Phase 3 (Exposed Meridian), accepted 2 Nov (Chzy18: "make it harder
    // by giving the players something to do/dodge"). Every 3.5 s (5 s until 3 Nov) each standing player gets a 2.5 m circle at their feet, locked
    // where they stood, landing 1.2 s later (Easy 1.5 s, Hard 1.0 s): 30 damage, so you keep moving. One per player, so it
    // reads the same for 1-4 players. The encounter calls Step every tick over the Phase 3 blocks, not during Severance.
    // Hard (5 Nov, done as recommended, review later): an echo 0.6 s after each wave, a second circle where each player is
    // by then (same size, warning and damage), so stepping out once is not enough: keep moving until both have landed.
    public sealed class FallingMeridians : AurelionAbility
    {
        public override string Id => "aurelion.falling_meridians";
        public override string Name => "Falling Meridians";

        public const float Radius = 2.5f, EveryS = 3.5f, WarnS = 1.2f, WarnEasyS = 1.5f, WarnHardS = 1.0f, EchoS = 0.6f;
        public const int Damage = 30;
        public int Warn => Sim.Ticks(Easy ? WarnEasyS : Hard ? WarnHardS : WarnS);
        public int Casts { get; private set; }
        public int Echoes { get; private set; }   // test evidence: Hard echo waves
        public readonly List<Hazard> Last = new List<Hazard>();   // test evidence: the latest wave
        int clock; bool echo;

        public void Reset() { clock = 0; echo = false; }
        public void Step()
        {
            ++clock;
            if (echo && clock == Sim.Ticks(EchoS)) { echo = false; Echoes++; Wave(Name + " echo"); }
            if (clock < Sim.Ticks(EveryS)) return;
            clock = 0; Casts++; Last.Clear(); Boss.PlayClipIfIdle("MeridianFlick", Sim.Ticks(0.8f));
            foreach (var h in Wave(Name)) Last.Add(h);
            echo = Hard;
        }

        // one circle at each standing player's feet. Not aimed at a player who is reviving an ally (revive rule, 4 Nov: "No
        // mechanic aims at them"; Codex Hard review AUR-TARGET, 6 Nov). Not immunity: a reviver standing in someone
        // else's circle is still hit.
        List<Hazard> Wave(string source)
        {
            var made = new List<Hazard>();
            var players = W.Players;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (!p || p.Down || p.Reviving != null) continue;
                var h = Spawn(new Hazard
                {
                    Source = source, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = Sim.Flat(p.transform.position),
                    Radius = Radius, LockAt = 0, ResolveAt = Warn, Damage = Damage, Knockback = 0, Stun = 0,
                });
                h.OnResolve = hz => W.BossFx.ChimeResolve(hz.Origin, hz.Radius);
                made.Add(h);
            }
            return made;
        }
        public override void Abort() => Reset();
    }
}
