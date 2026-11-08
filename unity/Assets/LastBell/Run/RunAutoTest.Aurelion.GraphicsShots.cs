using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // -lastbellGraphicsShots (with -lastbellAurelionGallery -lastbellAurelionOnly): the 8 Nov graphics pass (planet bodies,
    // tower posts and columns, Zenith glyphs, beams, Final Noon), overhead and game camera, each on the frame that shows it.
    public sealed partial class RunAutoTest
    {
        static bool GraphicsShots => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellGraphicsShots", StringComparison.OrdinalIgnoreCase));

        IEnumerator GraphicsShotsAll()
        {
            Note("---------- Aurelion graphics pass (8 Nov) ----------");
            AurelionArgs.SoloOverride = null;
            foreach (var cam in new[] { "", "_gamecam" })
            {
                RunDirector.ShotCamera = null; if (cam == "") RunDirector.ShotCamera = (new Vector3(0, 27, 17), new Vector3(0, 0, 1.5f)); galleryCam = "_gfx" + cam;
                yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(-3, 0, -4f) }, 2, Vector3.zero);
                if (V2Boss != null) { V2Boss.DebugSetPhaseV4(2); V2Boss.TestExtras = true; yield return V2Ticks(Sim.Ticks(1.5f)); yield return Shot("aurelion_errant_planets" + galleryCam); V2Boss.TestExtras = false; }
                yield return GalleryShot("meridian_towers", 1, b => b.Towers.Run(), Sim.Ticks(3.5f));
                yield return GalleryShot("zenith_marks", 1, b => b.Marks.Run(), 60, new Vector3(-6, 0, 0));
                yield return GalleryShot("zenith_marks_two_step", 2, b => b.Marks.Run(Sim.Ticks(6f)), 60, new Vector3(-6, 0, 0));
                yield return GalleryShot("meridian_lances_beam", 2, b => b.Lances.Run(4, 1, false), 0, null, 20, null, b => b.Lances.Duration(4, 1, false) + 3);
                yield return GalleryShot("hour_hands_beam", 2, b => b.Hands.Run(), 0, null, 20, null, b => b.Hands.Duration() + 3);
                yield return GalleryShot("meridian_recall_beam", 2, b => b.Lances.Run(4, 1, false).Concat(b.Recall.Run()), 0, null, 20, null, b => b.Lances.Duration(4, 1, false) + b.Recall.Duration + 3);
                yield return GalleryShot("final_noon_flash", 4, b => NoonAt(b, AurelionEncounter.EnrageS - 0.5f, Hold(Sim.Ticks(1.5f) - 1)), Sim.Ticks(0.8f), after: b => b.TestClock = false);
            }
            RunDirector.ShotCamera = null;
            yield return V2Teardown();
        }
    }
}
