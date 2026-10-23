using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Backwards Bell (`bellkeeper.ult_backwards_bell`), Bellkeeper Ultimate — proposal. SCAFFOLD STUB (Built = false): it only waits out its
    // cue so the Ultimate plan runs; its session replaces this file. Design: ultimate/ULTIMATE.md §3.4, §5.3.
    // Cue (set by the plan, BellkeeperUltimate.cs): Variant "block": At = A, B, C resolve times (Leaning Bell, Heart Toll, Toll Tide, built from UltShapes); rewinds resolve C, B, A at C + 2.0, + 3.0, + 4.0 s.
    public sealed class BackwardsBell : UltimateMechanic
    {
        public override string Id => UltimateMechanics.BackwardsBell;
        public override string Name => "Backwards Bell";
        public override bool Built => false;

        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name);
            yield return Mathf.Max(1, Sim.Ticks(Cue.At != null && Cue.At.Length > 0 ? Cue.At.Max() : 1f) + 1);
        }
    }
}
