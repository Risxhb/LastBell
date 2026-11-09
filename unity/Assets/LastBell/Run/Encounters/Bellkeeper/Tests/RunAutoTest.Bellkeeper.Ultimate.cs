using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Bellkeeper Ultimate (proposal) acceptance checks, inside the real game like the rest of RunAutoTest.
    //   LastBell.exe -lastbellAutotest -lastbellBellkeeperUltimate [-lastbellTestAbility <key>] -lastbellOut <folder>
    // -lastbellBellkeeperUltimate runs only this section; -lastbellTestAbility narrows it to one part: "scaffold", "v2_fight",
    // or a "bots_*" run (the phase slices, the level and seed sweeps, videos; long, never part of the default run). The phase
    // checks live in RunAutoTest.Bellkeeper.Ult.<Part>.cs; this file is the entry and the harness.
    //
    // Harness: UltStart(...) leaves an Ultimate fight on the Throne paused at combat start, boss halted, player at full HP
    // Restart the plan with bk.BeginCombat(), step exact ticks with BkStep / UltStepAlive, read hits in
    // bkHits. UltTick0() = the tick the running segment began. Overrides are cleared by UltLeave().
    public sealed partial class RunAutoTest
    {
        static bool BellkeeperUltimateOnly => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellBellkeeperUltimate", StringComparison.OrdinalIgnoreCase));
        static bool UltWants(string idOrPart) => BkFilter == null ? !idOrPart.StartsWith("bots") : BkFilter == BellkeeperAbilities.Normalize(idOrPart);

        IEnumerator BellkeeperUltimateChecks()
        {
            Note("---------- Bellkeeper Ultimate (proposal) ----------");
            if (UltWants("scaffold")) yield return UltScaffoldChecks();
            if (UltWants("bots_v2_levels")) yield return UltV2LevelSweep();
            if (UltWants("bots_v2_seeds")) yield return UltV2SeedSweep();
            if (UltWants("v2_fight")) yield return UltV2FightChecks();
            if (UltWants("bots_revive")) yield return UltReviveChecks();
            if (BkFilter == "ult_shots") yield return UltShots();   // asked for only (screenshots)
            if (UltWants("bots_p1v2")) yield return UltP1V2Checks();
            if (UltWants("bots_p1v2_video")) yield return UltP1V2Video();
            if (UltWants("bots_p1v2_watch")) yield return UltP1V2Watch();
            if (UltWants("bots_p2v2")) yield return UltP2V2Checks();
            if (UltWants("bots_p2v2_8")) yield return UltP2V2Eight();
            if (UltWants("bots_p3v2")) yield return UltP3V2Checks();
            if (UltWants("bots_p3v2_dbg")) yield return UltP3V2Dbg();
            if (UltWants("bots_p4v2")) yield return UltP4V2Checks();
            if (UltWants("bots_p4v2_video")) yield return UltP1V2Video(false, UltimateBot.Skill.Perfect, "perfect", "P4");
            // art: just The Floor Breaks (20 s), from above and from the game's chase camera
            if (UltWants("bots_p4v2_break")) { yield return UltP1V2Video(false, UltimateBot.Skill.Perfect, "break", "P4", 20); yield return UltP1V2Video(false, UltimateBot.Skill.Perfect, "breakchase", "P4", 20, true); }
            if (UltWants("bots_p4v2_watch")) yield return UltP1V2Watch("P4");
            if (UltWants("bots_p5v2")) yield return UltP5V2Checks();
            if (UltWants("bots_p5v2_video")) yield return UltP1V2Video(false, UltimateBot.Skill.Perfect, "perfect", "P5");
            if (UltWants("bots_p5v2_chase")) yield return UltP1V2Video(false, UltimateBot.Skill.Perfect, "chase", "P5", 66, true);   // art: what a player sees
            if (UltWants("bots_p5v2_watch")) yield return UltP1V2Watch("P5");
            if (UltWants("bots_p3v2_video")) yield return UltP1V2Video(false, UltimateBot.Skill.Perfect, "perfect", "P3");
            if (UltWants("bots_p3v2_watch")) yield return UltP1V2Watch("P3");
            if (UltWants("bots_p2v2_video")) yield return UltP1V2Video(false, UltimateBot.Skill.Perfect, "perfect", "P2");
            if (UltWants("bots_p2v2_watch")) yield return UltP1V2Watch("P2");
            yield return UltLeave();
        }

        // ---------------- harness ----------------
        // level: Hard by default, the Ultimate's designed numbers (gpt/08 §5, 27 Oct); every exact-damage and timing check is written for them
        IEnumerator UltStart(string ability = null, string segment = null, string cls = "Tollbreaker", Difficulty level = Difficulty.Hard)
        {
            BellkeeperEncounter.AbilityOverride = ability ?? "";
            BellkeeperEncounter.PhaseOverride = segment;
            if (d.State != RunState.Menu) { d.SetPaused(true); d.ReturnToMenu(); yield return null; }
            d.SelectDifficulty(level);
            int m = RunMaps.IndexOf(BellkeeperEncounter.ThroneMap);
            bool switching = m != RunMaps.Current;
            if (switching) directorLog.AddRange(d.Log);
            d.StartPractice(cls, RunDirector.BellkeeperUltimatePractice);
            if (switching) yield return BkAdopt(m, RunState.Ready);
            d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 4, "Bellkeeper Ultimate combat");
            bk = d.Boss as BellkeeperEncounter;
            d.SetPaused(true); bot.Clear();
            if (bk == null) { Check("Bellkeeper Ultimate fight started", false, d.Boss ? d.Boss.GetType().Name : "no boss"); bk = null; yield break; }
            bk.Halt(); d.World.ClearTransient();
            bk.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            d.Player.Input = bot; d.Player.DebugSetHp(PlayerController.MaxHp);
            BkPlace(d.Player, new Vector3(0, 0, 6));
            bkHits.Clear();
            d.World.PlayerHit += (p, h, dmg) => bkHits.Add(new BkHit { Tick = d.World.Tick, Player = p, Source = h.Source, HazardId = h.Id, Damage = dmg });
            yield return null;
        }
        IEnumerator UltLeave()
        {
            BellkeeperEncounter.PhaseOverride = null;
            BellkeeperEncounter.PushTimersOn = true;
            BellkeeperEncounter.V2StopAfterP4 = false;
            yield return BkLeave();
        }
        // step, keeping every standing player at full health (checks of timing, not of survival)
        void UltStepAlive(int ticks) { for (int i = 0; i < ticks; i++) { d.World.Step(); foreach (var p in d.World.Players) if (!p.Down && p.Hp < PlayerController.MaxHp) p.DebugSetHp(PlayerController.MaxHp); } }
        int UltStepAliveUntil(Func<bool> cond, int maxTicks)
        {
            for (int i = 0; i <= maxTicks; i++) { if (cond()) return i; if (i < maxTicks) UltStepAlive(1); }
            return -1;
        }
        int UltTick0() => bk.SegmentStartTick;
        // Runs the scheduler with no plan (bk.ManualPlan): moves you launch afterwards advance every tick, as in the fight.
        // Without this, a halted boss only runs a launched move's first tick.
        void UltManual() { bk.ManualPlan = true; bk.BeginCombat(); BkStep(1); }
        // world tick of the n-th (0-based) "launch <key>" in the plan log at or after `from`, or -1
        int UltLaunchTick(string key, int from, int n = 0)
        {
            key = BellkeeperAbilities.Normalize(key);
            foreach (var (t, what) in bk.PlanLog) if (t >= from && what == "launch " + key && n-- == 0) return t;
            return -1;
        }

        // ---------------- scaffold checks (the Ultimate session) ----------------
        IEnumerator UltScaffoldChecks()
        {
            BellkeeperEncounter.AbilityOverride = null;
            BossAttackIconChecks();   // the catalog carries the six Ultimate family icons (8 Nov)
            int ue = RunDirector.BellkeeperUltimatePractice;
            Check("Ultimate: the run's final entry \"Bellkeeper\" is the Ultimate (practice on the Throne, last in every run)",
                  ue >= 0 && RunDirector.Bosses[ue].Name == "Bellkeeper" && RunDirector.Bosses[ue].InRun && RunDirector.Bosses[ue].Map == BellkeeperEncounter.ThroneMap
                  && RunDirector.RunOrder.Last() == ue && RunDirector.Bosses.Count(b => b.Name.StartsWith("Bellkeeper")) == 1, ue < 0 ? "missing" : RunDirector.Bosses[ue].Map);
            Check("Ultimate: ids map to keys (ultimate.open_hour = ult_open_hour)",
                  BellkeeperAbilities.Normalize("ultimate.open_hour") == "ult_open_hour" && BellkeeperAbilities.Normalize("bellkeeper.ult_gear_floor") == "ult_gear_floor"
                  && BellkeeperAbilities.Normalize("ult-final-toll") == "ult_final_toll");
            Check("Ultimate: practice phase names parse (1-5, P3, p4)",
                  BellkeeperEncounter.ParseSegment("1") == BellkeeperEncounter.UltSegment.P1 && BellkeeperEncounter.ParseSegment("P3") == BellkeeperEncounter.UltSegment.P3
                  && BellkeeperEncounter.ParseSegment("p4") == BellkeeperEncounter.UltSegment.P4 && BellkeeperEncounter.ParseSegment("T1") == null
                  && BellkeeperEncounter.ParseSegment("5") == BellkeeperEncounter.UltSegment.P5 && BellkeeperEncounter.ParseSegment("x") == null);

            yield return UltStart();
            if (bk == null) yield break;
            var keys = bk.Abilities.Select(a => a.Key).ToList();
            Check("Ultimate: builds the six mechanics of the fight, unique keys",
                  keys.Count == 6 && keys.Distinct().Count() == 6 && UltimateMechanics.Ids.All(id => keys.Contains(BellkeeperAbilities.Normalize(id))), string.Join(", ", keys));
            Note("Ultimate mechanics built: " + string.Join(", ", bk.Mechanics.Select(m => m.Key + (m.Built ? "" : " (stub)"))));
            // Hard is the Ultimate's baseline (gpt/08 §5, Chzy18 27 Oct): its own table, not the run-wide x0.6 / x1 / x1.3
            Check("Ultimate: Hard plays at Hard, the designed numbers (damage taken x1)", d.World.Difficulty == Difficulty.Hard && Mathf.Approximately(d.World.DamageTakenScale, 1f));
            Check("Ultimate: the Bellkeeper uses his model on the Throne", bk.Presenter != null && bk.Presenter.HasModel && RunMaps.Current == RunMaps.IndexOf(BellkeeperEncounter.ThroneMap), RunMaps.CurrentName);
            // the run's level applies (gpt/08, Chzy18 25 Oct: Easy/Medium/Hard like every boss): damage taken scales, one-shots stay one-shots
            foreach (var lv in new[] { Difficulty.Easy, Difficulty.Medium })
            {
                yield return UltStart(level: lv);
                if (bk == null) yield break;
                float want = BellkeeperEncounter.UltDamageTaken[(int)lv];
                Check($"Ultimate: {RunDifficulty.Name(lv)} plays at {RunDifficulty.Name(lv)} (damage taken x{want:0.##}; a one-shot still kills)", d.World.Difficulty == lv && Mathf.Approximately(d.World.DamageTakenScale, want) && Mathf.RoundToInt(UltimateState.Lethal * want) >= PlayerController.MaxHp,
                      $"{RunDifficulty.Name(d.World.Difficulty)} x{d.World.DamageTakenScale:0.##}");
            }

            // practice hooks: each mechanic of the current (v2) fight loops alone
            foreach (var id in new[] { UltimateMechanics.EscapementRelay, UltimateMechanics.ChimeDebts, UltimateMechanics.BrokenHour, UltimateMechanics.OpenHour, UltimateMechanics.GearFloor, UltimateMechanics.FinalToll })
            {
                yield return UltStart(id);
                if (bk == null) yield break;
                var a = bk.Ability(id);
                bk.BeginCombat();
                int t = UltStepAliveUntil(() => a.Casts > 0, 300);
                bool alone = bk.Abilities.All(x => x == a || x.Casts == 0) || !a.Built;   // a built mechanic may call partners
                Check($"Ultimate practice hook -lastbellAbility {a.Key}: fires {a.Name}", bk.PracticeAbility == a && t >= 0 && alone, $"cast after {t} ticks");
                // attack icons (Codex, 8 Nov): each mechanic family has its icon, and its hazards carry its ID for the death recap
                UltStepAliveUntil(() => a.Spawned.Count > 0, Sim.Ticks(20f));   // the first hazard of the cast (the Open Hour has none: its bullets hit directly, with its ID)
                Check($"Ultimate attack icon: {a.Name} has its family icon and stamps its hazards", BossAttackIcons.Icon(a.Id) != null && (a.Spawned.Count > 0 || a is OpenHour) && a.Spawned.All(h => h.AttackId == a.Id),
                      $"{BossAttackIcons.Get(a.Id)?.Label ?? "not in catalog"} · {a.Spawned.Count} hazards");
            }

            // shared systems the current fight uses: wipes (solo and four players)
            yield return UltStart();
            if (bk == null) yield break;
            var ult = bk.Ult;
            var extras = new List<PlayerController> { BkAdd(new Vector3(3, 0, 3)), BkAdd(new Vector3(-3, 0, 3)), BkAdd(new Vector3(0, 0, -5)) };
            ult.Wipe("Wipe probe");
            Check("wipe: every player goes down (N = 4)", d.World.Players.All(p => p.Down) && ult.WipeCause == "Wipe probe");
            yield return UltStart();
            if (bk == null) yield break;
            bk.Ult.Wipe("Wipe probe");
            Check("wipe: solo goes down too", d.Player.Down);
            // a mechanic's wipe keeps its family for the death recap icon; a global cause (the push timer) stays text
            yield return UltStart();
            if (bk == null) yield break;
            bk.Ult.Wipe("Wipe probe", UltimateMechanics.GearFloor);
            Check("wipe: a mechanic's wipe shows its attack icon in the death recap", d.World.TakenAttackIds.Count > 0 && d.World.TakenAttackIds[d.World.TakenAttackIds.Count - 1] == UltimateMechanics.GearFloor);

            // boss death mid-plan cancels hazards and statuses
            yield return UltStart();
            if (bk == null) yield break;
            bk.BeginCombat(); BkStep(1);
            d.World.Hazards.Spawn(new Hazard { Source = "Death probe", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = new Vector3(0, 0, 9), Radius = 1, ResolveAt = 600, Damage = 10 });
            bk.Ult.Of(d.Player).Note = "probe";
            int pending = d.World.Hazards.Count;
            bk.DebugDefeat();
            Check("Ultimate: boss death cancels pending hazards and statuses",
                  pending > 0 && d.World.Hazards.Count == 0 && !bk.Ult.All.Any(), $"{pending} pending");
        }

    }
}
