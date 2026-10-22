using System.Collections;

namespace LastBell.Run
{
    // Striking Order (Bellkeeper ability set v2) acceptance checks — scaffold stub, owned by the Striking Order ability session.
    // Brief: bosses/4-shattered-hour-bellkeeper/abilities-v2/02-striking-order.md ("Autotest" under Build notes). Harness: BkStart,
    // BkAdd, BkStep... in RunAutoTest.BellkeeperV2.cs. Run alone with -lastbellBellkeeperV2 -lastbellTestAbility <key>.
    public sealed partial class RunAutoTest
    {
        IEnumerator StrikingOrderChecks()
        {
            Note("Striking Order: no checks yet (ability not built)");
            yield break;
        }
    }
}
