using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Fracture of Time (`bellkeeper.ult_fracture`), Bellkeeper Ultimate — proposal. SCAFFOLD STUB (Built = false): it only waits out its
    // cue so the Ultimate plan runs; its session replaces this file. Design: ultimate/ULTIMATE.md §3.8, §5.7.
    // Cue (set by the plan, BellkeeperUltimate.cs): P5 start: the gold fissure (UltimateState.FissureDir through the centre, 1.4 m wide) burns 30/s until the boss dies. No At.
    public sealed class FractureOfTime : UltimateMechanic
    {
        public override string Id => UltimateMechanics.Fracture;
        public override string Name => "Fracture of Time";
        public override bool Built => false;

        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name);
            while (true) yield return 60;
        }
    }
}
