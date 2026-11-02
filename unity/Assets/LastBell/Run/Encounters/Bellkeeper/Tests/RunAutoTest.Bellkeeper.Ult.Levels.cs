using System;
using System.Collections;
using System.Linq;

namespace LastBell.Run
{
    // Ultimate v2 difficulty sweep (DIFFICULTY-PROPOSAL-2026-10-27.md, accepted by Chzy18 27 Oct): full attempts from P1 on
    // every level, Perfect solo for each class, a Perfect party of 4 and a Late party of 4. Measurement, not tuning.
    //   -lastbellBellkeeperUltimate -lastbellTestAbility bots_v2_levels [-lastbellUltLevels easy,medium,hard] [-lastbellUltRuns sv,sc,sa,p4,l4]
    // Targets (gpt/08 §5, Chzy18 27 Oct): every class clears solo on Easy, Medium AND Hard (Hard = the designed fight); the
    // Perfect party clears every level, about 6:00 on Hard. Late has no required result.
    public sealed partial class RunAutoTest
    {
        IEnumerator UltV2LevelSweep()
        {
            Note("---------- Ultimate v2: Easy / Medium / Hard sweep (Hard = the designed fight) ----------");
            string lv = AurelionArgs.Arg("-lastbellUltLevels"), rn = AurelionArgs.Arg("-lastbellUltRuns");
            var levels = new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard }.Where(l => lv == null || lv.ToLowerInvariant().Contains(l.ToString().ToLowerInvariant())).ToArray();
            var runs = new (string id, string label, int n, UltimateBot.Skill skill, string cls, bool mustWin)[]
            {
                ("sc", "solo Chimesinger", 1, UltimateBot.Skill.Perfect, "Chimesinger", true),
                ("sa", "solo Arcanist", 1, UltimateBot.Skill.Perfect, "Arcanist", true),
                ("p4", "party of 4", 4, UltimateBot.Skill.Perfect, "Arcanist", true),
                ("l4", "Late party of 4", 4, UltimateBot.Skill.Late, "Arcanist", false),
            }.Where(r => rn == null || rn.Split(',').Contains(r.id)).ToArray();
            OpenHour.LogSteer = GearFloor.Log = AurelionArgs.Arg("-lastbellUltSteerLog") != null;
            var table = new System.Text.StringBuilder();
            foreach (var level in levels)
                foreach (var r in runs)
                {
                    string label = $"v2 {level} {r.label}";
                    yield return UltRun(label, r.n, r.skill, null, 900, r.cls, level: level);
                    var f = ultLast;
                    string row = f == null ? "no fight" : $"{(f.Killed ? "KILL" : "wipe")} {f.Ticks / 60 / 60}:{f.Ticks / 60 % 60:00}" + (f.Killed ? "" : $" ({f.Cause}, boss {f.Hp01:P1})") + $" · deaths {f.Deaths} · potions {f.Potions}";
                    table.AppendLine($"   {level,-6} {r.label,-17} {row}");
                    // required: solo for every class and the Perfect party, on every level
                    if (r.mustWin) Check($"Ultimate v2 levels: {label} clears", f != null && f.Killed, f?.ToString());
                }
            Note("Ultimate v2 level sweep:\n" + table);
        }
    }
}
