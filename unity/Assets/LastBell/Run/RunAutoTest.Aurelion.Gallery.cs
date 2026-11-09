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
        static bool DevourShots => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellDevourShots", StringComparison.OrdinalIgnoreCase));
        static bool ExtrasShots => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellExtrasShots", StringComparison.OrdinalIgnoreCase));

        // Two passes: the fixed overhead view for the design docs, then the real gameplay camera, where the telegraph style
        // (Reference/Telegraph-Style v2: contour widths, luminance, the Skyward motif) has to be judged.
        string galleryCam = "";
        IEnumerator AurelionGalleryShots()
        {
            if (SeveranceShots) { yield return SeveranceShotsAll(); yield break; }
            if (LastLightShots) { yield return LastLightShotsAll(); yield break; }
            if (GraphicsShots) { yield return GraphicsShotsAll(); yield break; }
            if (ExtrasShots)   // -lastbellExtrasShots: Errant Planets (Phase 2) and Falling Meridians (Phase 3), game camera
            {
                AurelionArgs.SoloOverride = null; RunDirector.ShotCamera = null; galleryCam = "_gamecam";
                yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(-3, 0, -4f) }, 2, Vector3.zero);
                if (V2Boss != null) { V2Boss.DebugSetPhaseV4(2); V2Boss.TestExtras = true; yield return V2Ticks(Sim.Ticks(1.5f)); yield return Shot("aurelion_errant_planets" + galleryCam); V2Boss.TestExtras = false; }
                yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(-3, 0, -4f) }, 3, Vector3.zero);
                if (V2Boss != null) { V2Boss.DebugSetPhaseV4(3); V2Boss.Meridians.Reset(); V2Boss.TestExtras = true; yield return V2Ticks(Sim.Ticks(FallingMeridians.EveryS) + Sim.Ticks(0.6f)); yield return Shot("aurelion_falling_meridians" + galleryCam); V2Boss.TestExtras = false; }
                yield return V2Teardown(); yield break;
            }
            if (DevourShots)   // -lastbellDevourShots: only Devour, 0.8 s into the pull, both cameras (the player 8 m out, standing still)
            {
                AurelionArgs.SoloOverride = null;
                foreach (var cam in new[] { "", "_gamecam" })
                {
                    RunDirector.ShotCamera = null; if (cam == "") RunDirector.ShotCamera = (new Vector3(0, 27, 17), new Vector3(0, 0, 1.5f)); galleryCam = cam;
                    yield return GalleryShot("devour", 1, b => AurelionEncounter.Together(b.HungryCollapse(), 0, new[] { Sim.Ticks(3f) }), Sim.Ticks(2.55f + 0.8f), new Vector3(-8, 0, 0));   // Medium Collapse 2.55 s, then 0.8 s of pull
                }
                RunDirector.ShotCamera = null;
                yield return V2Teardown(); yield break;
            }
            Note("---------- Aurelion ability gallery ----------");
            AurelionArgs.SoloOverride = null;
            RunDirector.ShotCamera = (new Vector3(0, 27, 17), new Vector3(0, 0, 1.5f)); galleryCam = "";
            yield return GalleryPass();
            RunDirector.ShotCamera = null; galleryCam = "_gamecam";
            yield return GalleryPass();
            yield return V2Teardown();
        }

        IEnumerator GalleryPass()
        {
            yield return GalleryShot("meridian_lances", 2, b => b.Lances.Run(4, 1, false), 80);
            yield return GalleryShot("starfall_mirror", 1, b => b.Starfall.Run(), 100, new Vector3(-5, 0, 2));
            yield return GalleryShot("orrery_closing", 2, b => b.Closing.Run(240), 100);
            yield return GalleryShot("orrery_horizon", 2, b => b.Closing.Run(240), 200);
            yield return GalleryShot("hour_hands", 2, b => b.Hands.Run(), 70);
            yield return GalleryShot("zenith_collapse", 1, b => b.Zenith.Run(), 110);
            yield return GalleryShot("ecliptic_order_1_numerals", 1, b => b.Ecliptic.Run(3), 70);
            yield return GalleryShot("ecliptic_order_2_strike", 1, b => b.Ecliptic.Run(3), Sim.Ticks(3.2f) - 8);
            yield return GalleryShot("zenith_marks_1_mark", 1, b => b.MarksWithLances(1), 60, new Vector3(-6, 0, 0));
            yield return GalleryShot("zenith_marks_2_verdict", 1, b => b.MarksWithLances(1), Sim.Ticks(3.0f) - 6);
            yield return GalleryShot("meridian_towers", 1, b => b.Towers.Run(), Sim.Ticks(3.0f));   // the bot stands in each tower (an empty one wipes)
            yield return GalleryShot("sunspots", 1, b => b.Spots.Run(), Sim.Ticks(2.0f));
            yield return GalleryShot("severance", 3, b => b.Sever.Run(), Sim.Ticks(5.0f), wait: 45);
            // the audit / readability pass (work/telegraph-audit-20261024, readability A1-A5): the states it named
            yield return GalleryShot("severance_split", 3, b => b.Sever.Run(), Sim.Ticks(1.8f), wait: 45);
            yield return GalleryShot("reunion", 3, b => b.Towers.RunReunion(), Sim.Ticks(1.5f));
            yield return GalleryShot("total_eclipse_umbra", 1, b => TotalEclipseFx.During(V2W, b.Eclipse, b.Eclipse.Run()), Sim.Ticks(4.0f), after: b => b.RaiseShield(0));
            yield return GalleryShot("last_light_warning", 1, b => b.Light.Run().Take(2), Sim.Ticks(LastLight.EveryS - LastLight.WarnS + 1.5f));
            yield return GalleryShot("last_light_burn", 1, b => b.Light.Run().Take(3), Sim.Ticks(LastLight.EveryS + 1.0f));
            // the fall (proposal, 1 Nov): standing on the first slice as its warning ends, then landed on the nearest floor
            yield return GalleryShot("last_light_fall_1_standing", 4, b => LastLightFloorFx.During(V2W, b.Light, b.Light.Run().Take(3)), Sim.Ticks(LastLight.EveryS - 0.5f), new Vector3(0.5f, 0, 6f));
            yield return GalleryShot("last_light_fall_2_landed", 4, b => LastLightFloorFx.During(V2W, b.Light, b.Light.Run().Take(3)), Sim.Ticks(LastLight.EveryS + 0.35f), new Vector3(0.5f, 0, 6f));
            // A16 (telegraph audit): Final Noon, the hard enrage: its 30 s warning banner, then the wipe, over Last Light
            yield return GalleryShot("final_noon_warning", 4, b => NoonAt(b, AurelionEncounter.EnrageWarnS - 0.5f, LastLightFloorFx.During(V2W, b.Light, b.Light.Run().Take(2))), Sim.Ticks(1.0f), after: b => b.TestClock = false);
            yield return GalleryShot("final_noon_wipe", 4, b => NoonAt(b, AurelionEncounter.EnrageS - 0.5f, LastLightFloorFx.During(V2W, b.Light, Hold(Sim.Ticks(1.5f) - 1))), Sim.Ticks(1.5f), after: b => b.TestClock = false);   // ends on the shot: the wipe sends the run to Results
        }

        static System.Collections.Generic.IEnumerable<int> Hold(int ticks) { yield return ticks; }
        // runs the enrage clock from s seconds while the body plays (the gallery holds the boss, so the clock would stop)
        static System.Collections.Generic.IEnumerable<int> NoonAt(AurelionEncounter b, float s, System.Collections.Generic.IEnumerable<int> body)
        {
            b.TestClock = true; b.DebugSetFightSeconds(s);
            foreach (var w in body) yield return w;
        }

        // still: the player stands there (no bot), so a following shape stays on screen
        IEnumerator GalleryShot(string name, int phase, Func<AurelionEncounter, System.Collections.Generic.IEnumerable<int>> body, int afterTicks, Vector3? still = null, float wait = 20, Action<AurelionEncounter> after = null, Func<AurelionEncounter, int> at = null)
        {
            yield return V2Setup(1, Difficulty.Medium, new[] { still ?? new Vector3(0, 0, 7f) }, phase, Vector3.zero);
            if (V2Boss == null) yield break;
            if (still == null) V3Bot(V2Main, BotMode.Perfect);
            if (at != null) afterTicks = at(V2Boss);   // the shot tick read from the boss (resolve frames, 8 Nov graphics pass)
            V2Begin(body(V2Boss));
            yield return V2Ticks(afterTicks);
            V2Main.DebugSetHp(PlayerController.MaxHp);
            yield return Shot("aurelion_" + name + galleryCam);
            if (V2Boss != null) after?.Invoke(V2Boss);
            if (d.State == RunState.Combat) yield return V2WaitIdle(wait);   // a wipe shot (Final Noon) has already sent the run to Results
            V3Unbot();
        }
    }
}
