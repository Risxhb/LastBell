using System;
using UnityEngine;

namespace LastBell.Run
{
    // How many bot allies fight beside the player (menu row, or  LastBell.exe -lastbellAllies 0..7). Chzy18 asked for
    // 1-7 allies on 2026-10-22 so co-op mechanics can be tested solo; 0 keeps the solo fight. Remembered between
    // sessions like the difficulty; a run copies it when it starts. The autotest starts at 0 and never saves.
    public static class RunParty
    {
        public const int MaxAllies = 7;
        const string PrefKey = "LastBell.Run.Allies";
        static bool loaded; static int selected;

        public static int Selected
        {
            get { Load(); return selected; }
            set { Load(); selected = Mathf.Clamp(value, 0, MaxAllies); if (Persist) try { PlayerPrefs.SetInt(PrefKey, selected); PlayerPrefs.Save(); } catch { } }
        }

        // Ally classes, spread across all three: start with the classes the player didn't pick, then cycle, so 7 allies
        // are 3 / 2 / 2 (with the player, the party is never one class unless there is a single ally).
        public static string[] AllyClasses(string playerClass, int allies)
        {
            var cls = RunDirector.Classes; int start = Mathf.Max(0, Array.IndexOf(cls, playerClass)) + 1;
            var r = new string[allies];
            for (int i = 0; i < allies; i++) r[i] = cls[(start + i) % cls.Length];
            return r;
        }

        static bool Persist => !RunAutoTest.Requested;

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            if (Persist) try { selected = Mathf.Clamp(PlayerPrefs.GetInt(PrefKey, 0), 0, MaxAllies); } catch { }
            var args = Environment.GetCommandLineArgs();
            int i = Array.FindIndex(args, a => a.Equals("-lastbellAllies", StringComparison.OrdinalIgnoreCase));
            if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int n)) selected = Mathf.Clamp(n, 0, MaxAllies);
        }
    }
}
