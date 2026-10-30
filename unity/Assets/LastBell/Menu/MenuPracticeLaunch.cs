using System;
using System.Linq;
using LastBell.Run;
using UnityEngine;

namespace LastBell.Menu
{
    // Starts straight into one boss's practice, skipping the menus. For working on a boss:
    //   LastBell.exe -lastbellPractice tempo-tyrant            (Vanguard, the boss's own arena)
    //   LastBell.exe -lastbellPractice oracle -lastbellClass Arcanist
    //   LastBell.exe -lastbellPractice bellkeeper -lastbellPhase 3   (starts in that HP phase; default 1)
    // The boss is matched by name, ignoring case, spaces, dashes and the "(practice)/(area n)" suffix; a unique prefix or
    // word is enough ("tempo", "oracle", "aurelion"). Goes through the same path as Boss Rush > Launch Test Rush, so the
    // map loads, practice auto-starts and Esc > Main Menu returns to the menu. Unknown names log the valid ones.
    public static class MenuPracticeLaunch
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void FromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-lastbellPractice");
            if (at < 0) return;
            string wanted = at + 1 < args.Length ? Key(args[at + 1]) : "";
            int cls = Array.IndexOf(args, "-lastbellClass");
            string hero = cls >= 0 && cls + 1 < args.Length
                ? RunDirector.Classes.FirstOrDefault(c => c.Equals(args[cls + 1], StringComparison.OrdinalIgnoreCase)) : RunDirector.Classes[0];

            var matches = Enumerable.Range(0, RunDirector.Bosses.Length)
                .Where(i => wanted.Length > 0 && Key(RunDirector.Bosses[i].Name).Contains(wanted)).ToArray();
            string valid = string.Join(", ", RunDirector.Bosses.Select(b => b.Name));
            if (hero == null) { Debug.LogError($"-lastbellClass: unknown class. Use one of: {string.Join(", ", RunDirector.Classes)}"); return; }
            if (matches.Length != 1) { Debug.LogError($"-lastbellPractice: '{(at + 1 < args.Length ? args[at + 1] : "")}' matches {matches.Length} bosses. Use one of: {valid}"); return; }

            int encounter = matches[0];
            int pa = Array.IndexOf(args, "-lastbellPhase"), phase = 1;
            if (pa >= 0 && (pa + 1 >= args.Length || !int.TryParse(args[pa + 1], out phase) || phase < 1 || phase > RunDirector.Bosses[encounter].Phases))
            { Debug.LogError($"-lastbellPhase: {RunDirector.Bosses[encounter].Name} has phases 1-{RunDirector.Bosses[encounter].Phases}."); return; }
            int map = RunMaps.IndexOf(RunDirector.Bosses[encounter].Map ?? "test");
            if (!MenuLaunch.Test(hero, map, encounter, phase))
                Debug.LogError($"-lastbellPractice: could not start {RunDirector.Bosses[encounter].Name} on map {map}.");
            else Debug.Log($"-lastbellPractice: {hero} vs {RunDirector.Bosses[encounter].Name}, phase {phase}");
        }

        // "Tempo Tyrant (practice)" -> "tempotyrant"; "chimebound-oracle" -> "chimeboundoracle"
        static string Key(string s)
        {
            int paren = s.IndexOf('(');
            if (paren >= 0) s = s.Substring(0, paren);
            return new string(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        }
    }
}
