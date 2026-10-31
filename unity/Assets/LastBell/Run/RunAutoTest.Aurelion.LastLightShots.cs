using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // -lastbellLastLightShots (with -lastbellAurelionGallery -lastbellAurelionOnly): Last Light's falling floor (LastLightFloorFx,
    // Chzy18 2026-10-29) overhead and from the gameplay camera: a slice trembling in its warning, one mid-fall, then the sky
    // below after 1, 4 and 6 slices. Checks only that the floor splits and is whole again once the phase ends.
    public sealed partial class RunAutoTest
    {
        static bool LastLightShots => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellLastLightShots", StringComparison.OrdinalIgnoreCase));

        IEnumerator LastLightShotsAll()
        {
            Note("---------- Last Light captures (the floor falls into the sky) ----------");
            AurelionArgs.SoloOverride = null;
            foreach (var cam in new[] { "", "_gamecam" })
            {
                RunDirector.ShotCamera = cam == "" ? (new Vector3(0, 27, 17), new Vector3(0, 0, 1.5f)) : ((Vector3, Vector3)?)null;
                yield return LastLightShotsFor(cam);
            }
            RunDirector.ShotCamera = null;
            yield return V2Teardown();
            AurelionArgs.SetOverride = null;
        }

        IEnumerator LastLightShotsFor(string cam)
        {
            AurelionArgs.SetOverride = "v4";
            yield return V2Setup(1, Difficulty.Medium, null, 4, Vector3.zero);
            if (V2Boss == null) yield break;
            V3Bot(V2Main, BotMode.Perfect);
            V2Begin(LastLightFloorFx.During(V2W, V2Boss.Light, V2Boss.Light.Run()));
            float fall1 = LastLight.EveryS, fall4 = 4 * LastLight.EveryS, fall6 = fall4 + 2 * LastLight.FastS;
            int at = 0;
            foreach (var (s, tag) in new[] { (fall1 - 1.0f, "1_warning"), (fall1 + 0.5f, "2_falling"), (fall1 + 2.5f, "3_one_fallen"), (fall4 + 2.5f, "4_four_fallen"), (fall6 + 2.5f, "5_six_fallen") })
            {
                int when = Sim.Ticks(s);
                yield return V2Ticks(when - at); at = when;
                V2Main.DebugSetHp(PlayerController.MaxHp);
                yield return Shot($"aurelion_last_light_floor_{tag}{cam}");
                if (tag == "1_warning") Check($"Last Light splits the floor into 8 slices{cam}", GameObject.FindObjectsByType<Transform>(FindObjectsSortMode.None).Count(t => t.name.StartsWith("Last Light slice")) == 8);
            }
            V3Unbot();
            yield return V2Teardown();
            var floor = GameObject.FindObjectsByType<Transform>(FindObjectsSortMode.None).FirstOrDefault(t => t.name == "FloorSurface");
            Check($"the floor is whole again after Last Light{cam}", floor && floor.GetComponent<Renderer>().enabled && !GameObject.FindObjectsByType<Transform>(FindObjectsSortMode.None).Any(t => t.name.StartsWith("Last Light slice")));
        }
    }
}
