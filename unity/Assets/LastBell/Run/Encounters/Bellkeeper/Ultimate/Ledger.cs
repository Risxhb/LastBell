using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // The Ledger (`bellkeeper.ult_ledger`), Bellkeeper Ultimate — proposal. SCAFFOLD STUB (Built = false): it only waits out its
    // cue so the Ultimate plan runs; its session replaces this file. Design: ultimate/ULTIMATE.md §3.3, §5.1, §5.6.
    // Cue (set by the plan, BellkeeperUltimate.cs): Variant "record" (P1 loop 1: Ult.Recording on, record the six strikes Ledger I..VI, then off); Variant "replay" (P4): At[0] = the first replayed strike (VI), then 1.5 s apart down to I. With no recording (practice from P4) it replays an authored clean ledger.
    public sealed class LedgerMechanic : UltimateMechanic
    {
        public override string Id => UltimateMechanics.Ledger;
        public override string Name => "The Ledger";
        public override bool Built => false;

        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name);
            yield return Mathf.Max(1, Sim.Ticks(Cue.At != null && Cue.At.Length > 0 ? Cue.At.Max() : 1f) + 1);
        }
    }
}
