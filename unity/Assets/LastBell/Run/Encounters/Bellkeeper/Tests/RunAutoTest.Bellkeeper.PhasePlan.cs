using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Bellkeeper v2 phase plan checks (ABILITIES.md §5, owned by the Abilities session): the order of moves per phase, the
    // pairings' offsets, the 3.5 s punish window, queued harmless transitions (one per burst, no heal, boss targetable),
    // the Hard-only Unfinished Hour and the two-pattern cap. They read the encounter's PlanLog, so they hold whatever each
    // ability's own timings are.
    public sealed partial class RunAutoTest
    {
        int bkMaxPatterns;
        // steps the running plan until `until` holds, keeping every player alive; returns false on timeout
        bool BkRun(Func<bool> until, int maxTicks)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                if (until()) return true;
                d.World.Step(); BkTopUp();
                bkMaxPatterns = Mathf.Max(bkMaxPatterns, BkPatternsLive());
            }
            return until();
        }
        List<(int tick, string what)> BkLogSince(int tick) => bk.PlanLog.Where(e => e.tick >= tick).ToList();
        int BkTickOf(List<(int tick, string what)> log, string what, int nth = 0) { var m = log.Where(e => e.what == what).ToList(); return nth < m.Count ? m[nth].tick : -1; }

        IEnumerator BkPhasePlanChecks()
        {
            Note("Bellkeeper v2 phase plan (ABILITIES.md §5)");
            bkMaxPatterns = 0;

            // ---- phase 1: each move alone, then the punish window ----
            yield return BkStart(null);
            if (bk == null) yield break;
            int t0 = d.World.Tick; bk.BeginCombat();
            BkRun(() => bk.PunishWindows >= 1, 60 * 60);
            var log = BkLogSince(t0);
            var order = log.Select(e => e.what).Take(5).ToArray();
            Check("phase 1 runs Leaning Bell, Striking Order, Reflection, Toll Tide, then the punish window",
                  order.SequenceEqual(new[] { "launch leaning_bell", "launch striking_order", "launch reflection", "launch toll_tide", "punish" }), string.Join(" > ", order));
            int recovery = 0; bool quiet = true;
            for (int i = 0; i < BellkeeperEncounter.PunishTicks + 30 && bk.Current == "recovery"; i++) { recovery++; quiet &= d.World.Hazards.Count == 0; d.World.Step(); BkTopUp(); }
            Check("the punish window lasts 3.5 s with nothing live", Mathf.Abs(recovery - BellkeeperEncounter.PunishTicks) <= 1 && quiet, $"{recovery} ticks, quiet {quiet}");
            yield return Shot("bk_v2_plan_phase1");

            // ---- phase 2: queued transition, then Lean and Order ----
            bk.DebugSetHpFraction(0.69f);
            int hpAt = bk.Hp, t1 = d.World.Tick;
            BkRun(() => bk.PhaseName == BellkeeperEncounter.Phase2, 60 * 30);
            int shiftTick = d.World.Tick;
            Check("phase 2 is entered after the current move finishes", bk.PhaseName == BellkeeperEncounter.Phase2 && d.World.Hazards.Count == 0 && bk.RunningCount == 0);
            bool harmless = true, solid = true;
            for (int i = 0; i < 90; i++) { harmless &= d.World.Hazards.Count == 0; solid &= bk.Solid; d.World.Step(); BkTopUp(); }
            Check("the transition is harmless and the boss stays targetable", harmless && solid);
            Check("no heal and no new health bar on the transition", bk.Hp == hpAt && bk.MaxHp == BellkeeperEncounter.BaseHp, $"{hpAt} -> {bk.Hp}/{bk.MaxHp}");
            BkRun(() => BkLogSince(shiftTick).Count(e => e.what == "launch striking_order") >= 1, 60 * 10);
            log = BkLogSince(shiftTick);
            int lean = BkTickOf(log, "launch leaning_bell"), ord = BkTickOf(log, "launch striking_order");
            Check("phase 2 opens with Lean and Order: numerals 1.5 s into the cleave's warning",
                  lean >= 0 && ord - lean == BellkeeperEncounter.OrderAfterLean, $"lean {lean}, order {ord}");
            BkRun(() => bk.PunishWindows >= 2, 60 * 40);
            log = BkLogSince(shiftTick);
            var p2 = log.Select(e => e.what).Where(w => !w.StartsWith("phase")).Take(5).ToArray();
            Check("phase 2 runs Lean and Order, Reflection, Toll Tide, then the punish window",
                  p2.SequenceEqual(new[] { "launch leaning_bell", "launch striking_order", "launch reflection", "launch toll_tide", "punish" }), string.Join(" > ", p2));
            Check("Medium never casts The Unfinished Hour", !bk.PlanLog.Any(e => e.what == "launch unfinished_hour"));

            // ---- one burst across both thresholds: one transition, straight to the second form ----
            yield return BkStart(null);
            if (bk == null) yield break;
            bk.BeginCombat(); BkStep(5);
            bk.DebugSetHpFraction(0.30f); int t2 = d.World.Tick;
            BkRun(() => bk.PhaseName == BellkeeperEncounter.Phase3, 60 * 30);
            var shifts = BkLogSince(t2).Where(e => e.what.StartsWith("phase")).Select(e => e.what).ToArray();
            Check("a burst through 70 % and 35 % queues one transition, into the second form", bk.PhaseName == BellkeeperEncounter.Phase3 && shifts.SequenceEqual(new[] { "phase 3" }), string.Join(", ", shifts));
            int t3 = d.World.Tick;
            BkRun(() => bk.PunishWindows >= 1, 60 * 40);
            log = BkLogSince(t3);
            var p3 = log.Select(e => e.what).Where(w => !w.StartsWith("phase")).Take(5).ToArray();
            Check("phase 3 runs Lean and Order, then the Final Toll (tide + Reflection), then the punish window",
                  p3.SequenceEqual(new[] { "launch leaning_bell", "launch striking_order", "launch toll_tide", "launch reflection", "punish" }), string.Join(" > ", p3));
            int tide = BkTickOf(log, "launch toll_tide"), mir = BkTickOf(log, "launch reflection");
            Check("the Final Toll: Reflection warns 1.0 s into the tide", tide >= 0 && mir - tide == BellkeeperEncounter.MirrorAfterTide, $"tide {tide}, reflection {mir}");
            yield return Shot("bk_v2_plan_final_toll");

            // ---- Hard: The Unfinished Hour from phase 2, after each Leaning Bell that has a predecessor ----
            yield return BkStart(null, Difficulty.Hard);
            if (bk == null) yield break;
            var leanAb = bk.Ability(BellkeeperAbilities.LeaningBell); var echoAb = bk.Ability(BellkeeperAbilities.UnfinishedHour);
            bk.BeginCombat(); BkStep(5);
            BkRun(() => bk.PunishWindows >= 1, 60 * 60);
            Check("Hard phase 1 has no Unfinished Hour", !bk.PlanLog.Any(e => e.what == "launch unfinished_hour"));
            if (leanAb.Built && echoAb.Built)
            {
                bk.DebugSetHpFraction(0.69f);
                BkRun(() => bk.PhaseName == BellkeeperEncounter.Phase2, 60 * 30);
                int t4 = d.World.Tick;
                BkRun(() => bk.PunishWindows >= 3, 60 * 60);   // two phase-2 loops
                log = BkLogSince(t4);
                int echoes = log.Count(e => e.what == "launch unfinished_hour");
                Check("Hard phase 2: the second Leaning Bell brings The Unfinished Hour (the first has nothing to remember)",
                      echoes == 1 && BkTickOf(log, "launch unfinished_hour") > BkTickOf(log, "launch leaning_bell", 1), $"{echoes} echoes");
            }
            else Note("Hard Unfinished Hour pairing: skipped until Leaning Bell and The Unfinished Hour are built");

            Check("never more than two damaging patterns at once (whole plan)", bkMaxPatterns <= 2, $"max {bkMaxPatterns}");
        }
    }
}
