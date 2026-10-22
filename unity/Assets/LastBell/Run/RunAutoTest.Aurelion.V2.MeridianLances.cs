using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Meridian Lances (aurelion.meridian_lances) checks, owned by the Meridian Lances session; called once from AurelionV2Checks
    // (RunAutoTest.Aurelion.V2.cs). Brief: abilities-v2/01-meridian_lances.md, "Escape-check plan" and the eight autotest checks.
    // Harness: V2Setup(n, level, positions, phase, bossAt), V2In(p).Go/Walk/Dash/Stop, V2Fire / V2Begin + V2WaitIdle on
    // V2Boss.<Ability>.Run(...), V2Hits / V2Spawns, V2OneHitPerInstance(), V2KillBoss(), V2Check(name, ok, detail).
    public sealed partial class RunAutoTest
    {
        IEnumerator V2MeridianLances()
        {
            Note("Meridian Lances: no checks yet (stub)");
            yield break;
        }
    }
}
