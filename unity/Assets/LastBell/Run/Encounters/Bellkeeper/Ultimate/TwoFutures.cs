using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Two Futures (`bellkeeper.ult_two_futures`), Bellkeeper Ultimate — proposal. SCAFFOLD STUB (Built = false): it only waits out its
    // cue so the Ultimate plan runs; its session replaces this file. Design: ultimate/ULTIMATE.md §3.5, §5.4, §5.7.
    // Cue (set by the plan, BellkeeperUltimate.cs): launch = the fingers start tracing. At[0] = the first interleaved step resolves; six steps 1.2 s apart (the plan puts a Heart Toll on the last).
    public sealed class TwoFutures : UltimateMechanic
    {
        public override string Id => UltimateMechanics.TwoFutures;
        public override string Name => "Two Futures";
        public override bool Built => false;

        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name);
            yield return Mathf.Max(1, Sim.Ticks(Cue.At != null && Cue.At.Length > 0 ? Cue.At.Max() : 1f) + 1);
        }
    }
}
