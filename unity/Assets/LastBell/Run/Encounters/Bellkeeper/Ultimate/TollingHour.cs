using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Tolling Hour (`bellkeeper.ult_tolling_hour`), Bellkeeper Ultimate — proposal. SCAFFOLD STUB (Built = false): it only waits out its
    // cue so the Ultimate plan runs; its session replaces this file. Design: ultimate/ULTIMATE.md §3.7, §5.
    // Cue (set by the plan, BellkeeperUltimate.cs): Variant "tolls": one unavoidable 12-damage raidwide per At entry (seconds after launch; 1.0 s apart in the plan).
    public sealed class TollingHour : UltimateMechanic
    {
        public override string Id => UltimateMechanics.TollingHour;
        public override string Name => "Tolling Hour";
        public override bool Built => false;

        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name);
            yield return Mathf.Max(1, Sim.Ticks(Cue.At != null && Cue.At.Length > 0 ? Cue.At.Max() : 1f) + 1);
        }
    }
}
