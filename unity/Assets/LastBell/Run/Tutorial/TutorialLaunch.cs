using System;
using System.Linq;
using LastBell.Menu;
using UnityEngine;

namespace LastBell.Run
{
    // LastBell.exe -lastbellTutorial [-lastbellClass Arcanist|Tollbreaker|Chimesinger]: skips the menus and starts the tutorial
    // (the "Tutorial (Tempo Tyrant)" practice entry on Metronome Court). Class defaults to the Arcanist, the only class with
    // a guided section so far. Same path as Boss Rush > Tutorial > Launch, so Esc > Main Menu returns to the menu.
    public static class TutorialLaunch
    {
        public static bool Requested => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellTutorial", StringComparison.OrdinalIgnoreCase));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void FromCommandLine()
        {
            if (!Requested || TutorialAutoTest.Requested) return;
            var args = Environment.GetCommandLineArgs();
            int cls = Array.IndexOf(args, "-lastbellClass");
            string hero = cls >= 0 && cls + 1 < args.Length
                ? RunDirector.Classes.FirstOrDefault(c => c.Equals(args[cls + 1], StringComparison.OrdinalIgnoreCase)) : "Arcanist";
            if (hero == null) { Debug.LogError($"-lastbellClass: unknown class. Use one of: {string.Join(", ", RunDirector.Classes)}"); return; }
            int enc = RunDirector.TutorialPractice;
            int map = RunMaps.IndexOf(RunDirector.TempoTyrantMap);
            if (enc < 0 || !MenuLaunch.Test(hero, map, enc)) Debug.LogError("-lastbellTutorial: could not start the tutorial.");
            else Debug.Log($"-lastbellTutorial: {hero}");
        }
    }
}
