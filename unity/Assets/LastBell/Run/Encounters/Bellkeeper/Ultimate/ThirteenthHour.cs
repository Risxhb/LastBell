using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // The Thirteenth Hour (`bellkeeper.ult_thirteenth_hour`), Bellkeeper Ultimate — proposal. SCAFFOLD STUB (Built = false): it only waits out its
    // cue so the Ultimate plan runs; its session replaces this file. Design: ultimate/ULTIMATE.md §3.7, §5.7.
    // Cue (set by the plan, BellkeeperUltimate.cs): P5 start. At[0] = the XIII toll (78 s, hard enrage: 999 to everyone). The great bell counts I..XIII, one 6-damage toll every 6 s. Runs until XIII or the boss dies.
    public sealed class ThirteenthHour : UltimateMechanic
    {
        public override string Id => UltimateMechanics.ThirteenthHour;
        public override string Name => "The Thirteenth Hour";
        public override bool Built => false;

        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name);
            yield return Mathf.Max(1, Sim.Ticks(Cue.At != null && Cue.At.Length > 0 ? Cue.At.Max() : 1f) + 1);
        }
    }
}
