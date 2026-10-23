using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // The Stopped Hour (`bellkeeper.ult_stopped_hour`), Bellkeeper Ultimate — proposal. SCAFFOLD STUB (Built = false): it only waits out its
    // cue so the Ultimate plan runs; its session replaces this file. Design: ultimate/ULTIMATE.md §3.2, §5, §6.
    // Cue (set by the plan, BellkeeperUltimate.cs): launch = the cracked-hourglass glyph. At[0] = the stop starts (Stilled), At[1] = time resumes and the chain lanes lash (default 5, 9). Variant "alone" (T1) or "lash" (with partners on the resume tick). Lanes come from Ult.StationOf(p) (assign a station when a player has none).
    public sealed class StoppedHour : UltimateMechanic
    {
        public override string Id => UltimateMechanics.StoppedHour;
        public override string Name => "The Stopped Hour";
        public override bool Built => false;

        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name);
            yield return Mathf.Max(1, Sim.Ticks(Cue.At != null && Cue.At.Length > 0 ? Cue.At.Max() : 1f) + 1);
        }
    }
}
