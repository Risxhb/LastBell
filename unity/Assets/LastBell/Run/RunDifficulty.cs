using System;
using UnityEngine;

namespace LastBell.Run
{
    public enum Difficulty { Easy, Medium, Hard }

    // The player's difficulty choice (menu row, or  LastBell.exe -lastbellDifficulty easy|medium|hard).
    // Chzy18 asked for three levels on 2026-10-22; Medium is the baseline every boss is authored for.
    // The selection is remembered between sessions; a run copies it when it starts, so it can't change mid-fight.
    //
    // What a level changes today: only the damage players take from boss attacks (applied once, at the hit, so echoes
    // and repeats aren't scaled twice). The design rules ask for harder levels to change mechanics, not just numbers,
    // so boss sessions can read CombatWorld.Difficulty to author their own per-level timings or an added Hard mechanic.
    public static class RunDifficulty
    {
        public static readonly string[] Names = { "Easy", "Medium", "Hard" };
        public static readonly string[] Notes =
        {
            "Boss attacks deal 60% damage. Same attacks and timings.",
            "The intended fight: every boss as designed.",
            "Boss attacks deal 130% damage. Same attacks and timings.",
        };
        const string PrefKey = "LastBell.Run.Difficulty";

        static bool loaded; static Difficulty selected = Difficulty.Medium;

        public static Difficulty Selected
        {
            get { Load(); return selected; }
            set { Load(); selected = value; if (Persist) try { PlayerPrefs.SetInt(PrefKey, (int)value); PlayerPrefs.Save(); } catch { } }
        }

        public static string Name(Difficulty d) => Names[(int)d];

        // multiplier on boss damage to players (provisional tuning)
        public static float DamageTakenScale(Difficulty d) => d == Difficulty.Easy ? 0.6f : d == Difficulty.Hard ? 1.3f : 1f;

        public static Difficulty? Parse(string s)
        {
            for (int i = 0; i < Names.Length; i++)
                if (string.Equals(s, Names[i], StringComparison.OrdinalIgnoreCase)) return (Difficulty)i;
            return null;
        }

        // the autotest starts at Medium and never overwrites the player's saved choice
        static bool Persist => !RunAutoTest.Requested;

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            if (Persist) try { int v = PlayerPrefs.GetInt(PrefKey, (int)Difficulty.Medium); if (v >= 0 && v < Names.Length) selected = (Difficulty)v; } catch { }
            // the command line wins over the saved choice, and isn't saved
            var args = Environment.GetCommandLineArgs();
            int i = Array.FindIndex(args, a => a.Equals("-lastbellDifficulty", StringComparison.OrdinalIgnoreCase));
            if (i >= 0 && i + 1 < args.Length && Parse(args[i + 1]) is Difficulty d) selected = d;
        }
    }
}
