using System;
using System.Collections;
using System.Linq;

namespace LastBell.Run
{
    // Aurelion (Skyward Spire area boss) acceptance checks: only the current fight, v4, and the shared systems it uses
    // (Chzy18, 1 Nov: no tests on a boss version that isn't current). The v1 practice section, the v2 per-ability sections and
    // the v3 Milestone A checks were retired with it. The checks are in RunAutoTest.Aurelion.V4.cs on the harness in
    // RunAutoTest.Aurelion.V2.cs. `-lastbellAurelionOnly` runs only this section.
    public sealed partial class RunAutoTest
    {
        static bool AurelionOnly => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellAurelionOnly", StringComparison.OrdinalIgnoreCase));

        IEnumerator AurelionChecks()
        {
            if (AurelionGallery) { yield return AurelionGalleryShots(); yield break; }
            if (AurelionDemo) { yield return AurelionDemoFight(); yield break; }
            if (AurelionV4Live) { yield return AurelionV4LiveFight(); yield break; }
            if (d.State != RunState.Menu) { d.SetPaused(true); d.ReturnToMenu(); yield return null; }   // the earlier section may end on Ready
            yield return AurelionV4Checks(); yield return V2Teardown();
            AurelionArgs.SoloOverride = null;
        }
    }
}
