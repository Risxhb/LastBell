using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Thirteen Chimes: the area quotes (`bellkeeper.ult_area_quotes`), Bellkeeper Ultimate — proposal. SCAFFOLD STUB (Built = false): it only waits out its
    // cue so the Ultimate plan runs; its session replaces this file. Design: ultimate/ULTIMATE.md §5.5 (T2).
    // Cue (set by the plan, BellkeeperUltimate.cs): Variant "quotes": At = Spire (sundial shadow cone), Foundry (bellows knockback), Verdant (root circles) resolve times, each alone.
    public sealed class AreaQuotes : UltimateMechanic
    {
        public override string Id => UltimateMechanics.AreaQuotes;
        public override string Name => "Thirteen Chimes: the area quotes";
        public override bool Built => false;

        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name);
            yield return Mathf.Max(1, Sim.Ticks(Cue.At != null && Cue.At.Length > 0 ? Cue.At.Max() : 1f) + 1);
        }
    }
}
