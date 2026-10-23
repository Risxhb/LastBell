using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Pendulum Clocks (`bellkeeper.ult_pendulum_clocks`), Bellkeeper Ultimate — proposal. SCAFFOLD STUB (Built = false): it only waits out its
    // cue so the Ultimate plan runs; its session replaces this file. Design: ultimate/ULTIMATE.md §3.1, §5, §6.
    // Cue (set by the plan, BellkeeperUltimate.cs): launch = numerals and hands appear (≥ 6 s before hour I). Variant "singles": hour k strikes At[k] (I..IV); "pairs": I+II strike At[0], III+IV At[1] (solo: II and IV 2.0 s after I and III). Each strike resolves exactly on its At tick.
    public sealed class PendulumClocks : UltimateMechanic
    {
        public override string Id => UltimateMechanics.PendulumClocks;
        public override string Name => "Pendulum Clocks";
        public override bool Built => false;
        // Leaning Bell pairing rule (§3.1): the direction a Leaning Bell resolving on worldTick must lean (toward the striking
        // hour's station(s), summed and normalised); null if no clock strikes on that tick. The plan asks on the launch tick.
        public Vector3? LeanDirAt(int worldTick) => null;

        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name);
            yield return Mathf.Max(1, Sim.Ticks(Cue.At != null && Cue.At.Length > 0 ? Cue.At.Max() : 1f) + 1);
        }
    }
}
