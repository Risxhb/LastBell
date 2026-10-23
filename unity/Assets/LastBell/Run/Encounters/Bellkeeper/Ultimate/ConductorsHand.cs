using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // The Conductor's Hand (`bellkeeper.ult_conductors_hand`), Bellkeeper Ultimate — proposal. SCAFFOLD STUB (Built = false): it only waits out its
    // cue so the Ultimate plan runs; its session replaces this file. Design: ultimate/ULTIMATE.md §3.6, §5.3, §5.6, §5.7.
    // Cue (set by the plan, BellkeeperUltimate.cs): launch = the phrase is shown. At[0] = move 1 resolves, At[1] = move 2 resolves (real telegraphs only 2.0 s before each). It launches the moves itself (Boss.UltMove / UltLaunch / UltShapes).
    public sealed class ConductorsHand : UltimateMechanic
    {
        public override string Id => UltimateMechanics.ConductorsHand;
        public override string Name => "The Conductor's Hand";
        public override bool Built => false;

        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name);
            yield return Mathf.Max(1, Sim.Ticks(Cue.At != null && Cue.At.Length > 0 ? Cue.At.Max() : 1f) + 1);
        }
    }
}
