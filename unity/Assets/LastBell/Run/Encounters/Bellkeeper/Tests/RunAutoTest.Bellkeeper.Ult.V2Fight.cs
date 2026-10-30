using System.Collections;
using System.Linq;

namespace LastBell.Run
{
    // The v2 fight in the default Ultimate run (-lastbellBellkeeperUltimate with no filter, or -lastbellTestAbility v2_fight).
    // Before 27 Oct the default run only played v1 (V2Override was false unless the filter was a bots_pNv2 key), so the
    // run-loop and PR counts never covered the fight we ship. This plays it: one full attempt from P1 on Hard (the designed
    // numbers, gpt/08 §5) with the Perfect party of 4, then each phase's own slice checks.
    public sealed partial class RunAutoTest
    {
        IEnumerator UltV2FightChecks()
        {
            Note("---------- Ultimate v2: the fight as shipped (default run) ----------");
            BellkeeperEncounter.V2Override = true; BellkeeperEncounter.V2StopAfterP4 = false;
            yield return UltRun("v2 full fight · party of 4 · Perfect · Hard", 4, UltimateBot.Skill.Perfect, null, 900, "Vanguard", level: Difficulty.Hard);
            var f = ultLast;
            var segs = f == null ? new BellkeeperEncounter.UltSegment[0] : f.Segments.Select(s => s.seg).ToArray();
            var want = new[] { BellkeeperEncounter.UltSegment.P1, BellkeeperEncounter.UltSegment.P2, BellkeeperEncounter.UltSegment.P3, BellkeeperEncounter.UltSegment.P4, BellkeeperEncounter.UltSegment.P5 };
            Check("Ultimate v2: the default run plays v2 (P1 → P2 → P3 → P4 → P5, no v1 transitions T1/T2)",
                  BellkeeperEncounter.UltV2 && segs.SequenceEqual(want), string.Join(" ", segs));
            Check("Ultimate v2: the Perfect party of 4 clears Hard from P1 with no deaths", f != null && f.Killed && f.Deaths == 0, f?.ToString());
            if (f != null) Note($"   v2 Hard party clear time {f.Ticks / 60 / 60}:{f.Ticks / 60 % 60:00} (target about 6:00)");

            // each phase's own checks (they also run alone as bots_p1v2 ... bots_p5v2); P4 last, since it stops attempts after P4
            BellkeeperEncounter.V2Override = true; yield return UltP1V2Checks();
            BellkeeperEncounter.V2Override = true; yield return UltP2V2Checks();
            BellkeeperEncounter.V2Override = true; yield return UltP3V2Checks();
            BellkeeperEncounter.V2Override = true; yield return UltP5V2Checks();
            BellkeeperEncounter.V2Override = true; yield return UltP4V2Checks();
            BellkeeperEncounter.V2StopAfterP4 = false;
        }
    }
}
