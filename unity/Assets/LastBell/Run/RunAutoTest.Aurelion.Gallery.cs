using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // -lastbellAurelionGallery (with -lastbellAurelionOnly): one overhead screenshot per Aurelion ability, mid-telegraph,
    // for the design docs. Medium, solo, boss at the centre; the player follows the perfect route (AllyBot Perfect).
    public sealed partial class RunAutoTest
    {
        static bool AurelionGallery => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellAurelionGallery", StringComparison.OrdinalIgnoreCase));

        // Two passes: the fixed overhead view for the design docs, then the real gameplay camera, where the telegraph style
        // (Reference/Telegraph-Style v2: contour widths, luminance, the Skyward motif) has to be judged.
        string galleryCam = "";
        IEnumerator AurelionGalleryShots()
        {
            if (SeveranceShots) { yield return SeveranceShotsAll(); yield break; }
            if (LastLightShots) { yield return LastLightShotsAll(); yield break; }
            Note("---------- Aurelion ability gallery ----------");
            AurelionArgs.SoloOverride = null;
            RunDirector.ShotCamera = (new Vector3(0, 27, 17), new Vector3(0, 0, 1.5f)); galleryCam = "";
            yield return GalleryPass();
            RunDirector.ShotCamera = null; galleryCam = "_gamecam";
            yield return GalleryPass();
            yield return V2Teardown();
            AurelionArgs.SetOverride = null;
        }

        IEnumerator GalleryPass()
        {
            AurelionArgs.SetOverride = "v3";
            yield return GalleryShot("meridian_lances", 2, b => b.Lances.Run(4, 1, false), 80);
            yield return GalleryShot("starfall_mirror", 1, b => b.Starfall.Run(), 100, new Vector3(-5, 0, 2));
            AurelionArgs.SetOverride = "v2";
            yield return GalleryShot("noon_shadow_v2_only", 1, b => b.Noon.Run(false), 120);
            AurelionArgs.SetOverride = "v3";
            yield return GalleryShot("orrery_closing", 2, b => b.Closing.Run(240), 100);
            yield return GalleryShot("orrery_horizon", 2, b => b.Closing.Run(240), 200);
            yield return GalleryShot("hour_hands", 2, b => b.Hands.Run(), 70);
            yield return GalleryShot("zenith_collapse", 1, b => b.Zenith.Run(), 110);
            yield return GalleryShot("ecliptic_order_1_numerals", 1, b => b.Ecliptic.Run(3), 70);
            yield return GalleryShot("ecliptic_order_2_strike", 1, b => b.Ecliptic.Run(3), Sim.Ticks(3.2f) - 8);
            yield return GalleryShot("zenith_marks_1_mark", 1, b => b.MarksWithLances(1), 60, new Vector3(-6, 0, 0));
            yield return GalleryShot("zenith_marks_2_verdict", 1, b => b.MarksWithLances(1), Sim.Ticks(3.0f) - 6);
            AurelionArgs.SetOverride = "v4";
            yield return GalleryShot("meridian_towers", 1, b => b.Towers.Run(), Sim.Ticks(3.0f));   // the bot stands in each tower (an empty one wipes)
            yield return GalleryShot("sunspots", 1, b => b.Spots.Run(), Sim.Ticks(2.0f));
            yield return GalleryShot("severance", 3, b => b.Sever.Run(), Sim.Ticks(5.0f), wait: 45);
            // the audit / readability pass (work/telegraph-audit-20261024, readability A1-A5): the states it named
            yield return GalleryShot("severance_split", 3, b => b.Sever.Run(), Sim.Ticks(1.8f), wait: 45);
            yield return GalleryShot("reunion", 3, b => b.Towers.RunReunion(), Sim.Ticks(1.5f));
            yield return GalleryShot("total_eclipse_umbra", 1, b => TotalEclipseFx.During(V2W, b.Eclipse, b.Eclipse.Run()), Sim.Ticks(4.0f), after: b => b.RaiseShield(0));
            yield return GalleryShot("last_light_warning", 1, b => b.Light.Run().Take(2), Sim.Ticks(LastLight.EveryS - LastLight.WarnS + 1.5f));
            yield return GalleryShot("last_light_burn", 1, b => b.Light.Run().Take(3), Sim.Ticks(LastLight.EveryS + 1.0f));
        }

        // still: the player stands there (no bot), so a following shape stays on screen
        IEnumerator GalleryShot(string name, int phase, Func<AurelionEncounter, System.Collections.Generic.IEnumerable<int>> body, int afterTicks, Vector3? still = null, float wait = 20, Action<AurelionEncounter> after = null)
        {
            yield return V2Setup(1, Difficulty.Medium, new[] { still ?? new Vector3(0, 0, 7f) }, phase, Vector3.zero);
            if (V2Boss == null) yield break;
            if (still == null) V3Bot(V2Main, BotMode.Perfect);
            V2Begin(body(V2Boss));
            yield return V2Ticks(afterTicks);
            V2Main.DebugSetHp(PlayerController.MaxHp);
            yield return Shot("aurelion_" + name + galleryCam);
            if (V2Boss != null) after?.Invoke(V2Boss);
            yield return V2WaitIdle(wait);
            V3Unbot();
        }
    }
}
