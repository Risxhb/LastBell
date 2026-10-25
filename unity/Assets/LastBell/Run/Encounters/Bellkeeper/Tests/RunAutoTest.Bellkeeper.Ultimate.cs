using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Bellkeeper Ultimate (proposal) acceptance checks, inside the real game like the rest of RunAutoTest.
    //   LastBell.exe -lastbellAutotest -lastbellBellkeeperUltimate [-lastbellTestAbility <key>] -lastbellOut <folder>
    // -lastbellBellkeeperUltimate runs only this section; -lastbellTestAbility narrows it to one mechanic's checks
    // (ult_pendulum_clocks ... ult_fracture), "scaffold" or "plan" (the Ultimate session's), or "bots" (Part 5: the strat
    // bots' full clears and the imperfect bots' wipes; long, never part of the default run). Each mechanic's checks live in
    // RunAutoTest.Bellkeeper.Ult.<Mechanic>.cs, owned by that mechanic's session; this file is the shared harness.
    //
    // Harness: UltStart(...) leaves an Ultimate fight on the Throne paused at combat start, boss halted, player at full HP
    // (like BkStart). Restart the plan with bk.BeginCombat(), step exact ticks with BkStep / UltStepAlive, read hits in
    // bkHits. UltTick0() = the tick the running segment began. Overrides are cleared by UltLeave().
    public sealed partial class RunAutoTest
    {
        static bool BellkeeperUltimateOnly => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellBellkeeperUltimate", StringComparison.OrdinalIgnoreCase));
        static bool UltWants(string idOrPart) => BkFilter == null ? !idOrPart.StartsWith("bots") : BkFilter == BellkeeperAbilities.Normalize(idOrPart);

        IEnumerator BellkeeperUltimateChecks()
        {
            Note("---------- Bellkeeper Ultimate (proposal) ----------");
            // the v1 checks play the v1 fight; the v2 slice checks play v2
            BellkeeperEncounter.V2Override = BkFilter != null && (BkFilter.StartsWith("bots_p1v2") || BkFilter.StartsWith("bots_p2v2") || BkFilter.StartsWith("bots_p3v2"));
            if (UltWants("scaffold")) yield return UltScaffoldChecks();
            if (UltWants(UltimateMechanics.PendulumClocks)) yield return UltPendulumClocksChecks();
            if (UltWants(UltimateMechanics.StoppedHour)) yield return UltStoppedHourChecks();
            if (UltWants(UltimateMechanics.Ledger)) yield return UltLedgerChecks();
            if (UltWants(UltimateMechanics.BackwardsBell)) yield return UltBackwardsBellChecks();
            if (UltWants(UltimateMechanics.TwoFutures)) yield return UltTwoFuturesChecks();
            if (UltWants(UltimateMechanics.ConductorsHand)) yield return UltConductorsHandChecks();
            if (UltWants(UltimateMechanics.TollingHour) || UltWants(UltimateMechanics.ThirteenthHour) || UltWants(UltimateMechanics.AreaQuotes)) yield return UltTollingHourChecks();
            if (UltWants(UltimateMechanics.Fracture)) yield return UltFractureChecks();
            if (UltWants("plan")) yield return UltPlanChecks();
            if (UltWants("bots_dps")) yield return UltDpsChecks();
            if (UltWants("bots")) yield return UltBotChecks();
            if (UltWants("bots_video")) yield return UltDemoVideo();
            if (UltWants("bots_attempts")) yield return UltAttempts();
            if (UltWants("bots_watch")) yield return UltWatchAttempts();
            if (UltWants("bots_p1v2")) yield return UltP1V2Checks();
            if (UltWants("bots_p1v2_video")) yield return UltP1V2Video();
            if (UltWants("bots_p1v2_watch")) yield return UltP1V2Watch();
            if (UltWants("bots_p2v2")) yield return UltP2V2Checks();
            if (UltWants("bots_p2v2_8")) yield return UltP2V2Eight();
            if (UltWants("bots_p3v2")) yield return UltP3V2Checks();
            if (UltWants("bots_p3v2_dbg")) yield return UltP3V2Dbg();
            if (UltWants("bots_p3v2_video")) yield return UltP1V2Video(false, UltimateBot.Skill.Perfect, "perfect", "P3");
            if (UltWants("bots_p3v2_watch")) yield return UltP1V2Watch("P3");
            if (UltWants("bots_p2v2_video")) yield return UltP1V2Video(false, UltimateBot.Skill.Perfect, "perfect", "P2");
            if (UltWants("bots_p2v2_watch")) yield return UltP1V2Watch("P2");
            yield return UltLeave();
        }

        // ---------------- harness ----------------
        IEnumerator UltStart(string ability = null, string segment = null, string cls = "Vanguard")
        {
            BellkeeperEncounter.SetOverride = BellkeeperEncounter.AbilitySet.Ultimate;
            BellkeeperEncounter.AbilityOverride = ability ?? "";
            BellkeeperEncounter.PhaseOverride = segment;
            if (d.State != RunState.Menu) { d.SetPaused(true); d.ReturnToMenu(); yield return null; }
            d.SelectDifficulty(Difficulty.Medium);
            int m = RunMaps.IndexOf(BellkeeperEncounter.ThroneMap);
            bool switching = m != RunMaps.Current;
            if (switching) directorLog.AddRange(d.Log);
            d.StartPractice(cls, RunDirector.BellkeeperUltimatePractice);
            if (switching) yield return BkAdopt(m, RunState.Ready);
            d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 4, "Bellkeeper Ultimate combat");
            bk = d.Boss as BellkeeperEncounter;
            d.SetPaused(true); bot.Clear();
            if (bk == null || bk.Set != BellkeeperEncounter.AbilitySet.Ultimate) { Check("Bellkeeper Ultimate fight started", false, d.Boss ? d.Boss.GetType().Name : "no boss"); bk = null; yield break; }
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
            BellkeeperEncounter.V2Override = null;
            BellkeeperEncounter.PushSeconds = (float[])BellkeeperEncounter.DefaultPush.Clone();
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
            BellkeeperEncounter.SetOverride = null; BellkeeperEncounter.AbilityOverride = null;
            int ue = RunDirector.BellkeeperUltimatePractice;
            Check("Ultimate: a separate practice entry \"Bellkeeper — Ultimate\" on the Throne, not in the run",
                  ue > 0 && RunDirector.Bosses[ue].Name == "Bellkeeper — Ultimate" && !RunDirector.Bosses[ue].InRun && RunDirector.Bosses[ue].Map == BellkeeperEncounter.ThroneMap
                  && !RunDirector.RunOrder.Contains(ue), ue < 0 ? "missing" : RunDirector.Bosses[ue].Map);
            Check("Ultimate: the normal Bellkeeper entry still defaults to v2", BellkeeperEncounter.RequestedSet == BellkeeperEncounter.AbilitySet.V2 && RunDirector.Bosses[0].Map == BellkeeperEncounter.ThroneMap);
            Check("Ultimate: ids map to keys (ultimate.ledger = ult_ledger)",
                  BellkeeperAbilities.Normalize("ultimate.ledger") == "ult_ledger" && BellkeeperAbilities.Normalize("bellkeeper.ult_pendulum_clocks") == "ult_pendulum_clocks"
                  && BellkeeperAbilities.Normalize("ult-two-futures") == "ult_two_futures");
            Check("Ultimate: practice phase names parse (1-5, P3, T1, t2)",
                  BellkeeperEncounter.ParseSegment("1") == BellkeeperEncounter.UltSegment.P1 && BellkeeperEncounter.ParseSegment("P3") == BellkeeperEncounter.UltSegment.P3
                  && BellkeeperEncounter.ParseSegment("T1") == BellkeeperEncounter.UltSegment.T1 && BellkeeperEncounter.ParseSegment("t2") == BellkeeperEncounter.UltSegment.T2
                  && BellkeeperEncounter.ParseSegment("5") == BellkeeperEncounter.UltSegment.P5 && BellkeeperEncounter.ParseSegment("x") == null);

            yield return UltStart();
            if (bk == null) yield break;
            var keys = bk.Abilities.Select(a => a.Key).ToList();
            Check("Ultimate: builds the reworked v2 moves and the fourteen mechanics (ten v1, four v2), unique keys",
                  keys.Count == 17 && keys.Distinct().Count() == 17 && keys.Contains("leaning_bell") && keys.Contains("toll_tide") && keys.Contains("reflection")
                  && !keys.Contains("striking_order") && UltimateMechanics.Ids.All(id => keys.Contains(BellkeeperAbilities.Normalize(id))), string.Join(", ", keys));
            Note("Ultimate mechanics built: " + string.Join(", ", bk.Mechanics.Select(m => m.Key + (m.Built ? "" : " (stub)"))));
            Check("Ultimate: one tuning (the world runs at Medium)", d.World.Difficulty == Difficulty.Medium);
            Check("Ultimate: the Bellkeeper uses his model on the Throne", bk.Presenter != null && bk.Presenter.HasModel && RunMaps.Current == RunMaps.IndexOf(BellkeeperEncounter.ThroneMap), RunMaps.CurrentName);

            // practice hooks: each mechanic loops alone
            foreach (var id in UltimateMechanics.Ids)
            {
                yield return UltStart(id);
                if (bk == null) yield break;
                var a = bk.Ability(id);
                bk.BeginCombat();
                int t = UltStepAliveUntil(() => a.Casts > 0, 300);
                bool alone = bk.Abilities.All(x => x == a || x.Casts == 0) || !a.Built;   // a built mechanic may call partners
                Check($"Ultimate practice hook -lastbellAbility {a.Key}: fires {a.Name}", bk.PracticeAbility == a && t >= 0 && alone, $"cast after {t} ticks");
            }

            // shared systems
            yield return UltStart();
            if (bk == null) yield break;
            var ult = bk.Ult; var me = d.Player; var clocks = bk.Mechanic<PendulumClocks>();
            // Stilled: can't move for its window, moves again after
            BkPlace(me, new Vector3(0, 0, 6)); bot.MoveWorld = Vector3.right;
            ult.Still(me, d.World.Tick + 1, 60, 30);
            BkStep(31); var p0 = me.transform.position; BkStep(25);
            Check("Stilled: the player can't move during the stop", Sim.Flat(me.transform.position - p0).magnitude < 0.01f && ult.IsStilled(me), $"{Sim.Flat(me.transform.position - p0).magnitude:0.00} m");
            BkStep(15);
            Check("Stilled: movement comes back when the stop ends", Sim.Flat(me.transform.position - p0).magnitude > 0.5f && me.MovementEnabled);
            bot.Clear();
            // Cracked Hour: 30 to everyone; the failing player deals no damage for 20 s
            var ally = BkAdd(new Vector3(4, 0, 4));
            int hpA = me.Hp, hpB = ally.Hp;
            ult.CrackedHour(me);
            Check("Cracked Hour: 30 to every player", hpA - me.Hp == 30 && hpB - ally.Hp == 30, $"{hpA - me.Hp}, {hpB - ally.Hp}");
            d.World.Actor = me; int dealt = d.World.DamageBoss(100, bk.transform.position, "probe"); d.World.Actor = ally; int dealtAlly = d.World.DamageBoss(100, bk.transform.position, "probe"); d.World.Actor = null;
            Check("Cracked Hour: the failing player deals 0, others full", dealt == 0 && dealtAlly == 100, $"{dealt} / {dealtAlly}");
            BkStep(UltimateState.CrackedTicks + 2);
            d.World.Actor = me; dealt = d.World.DamageBoss(100, bk.transform.position, "probe"); d.World.Actor = null;
            Check("Cracked Hour: damage returns after 20 s", dealt == 100 && !ult.IsCracked(me));
            me.DebugSetHp(PlayerController.MaxHp); ally.DebugSetHp(PlayerController.MaxHp);
            // first-body lanes from station 0 (NE)
            var s0 = UltimateState.StationDir(0);
            BkPlace(me, -s0 * 5); BkPlace(ally, -s0 * 5 + Vector3.Cross(Vector3.up, s0) * 6);
            bkHits.Clear();
            ult.FirstBodyLane(clocks, "Lane probe", UltimateState.Station(0), me.transform.position, 1.6f, 3, p => p == me ? UltimateState.Lethal : 60);
            BkStep(4);
            Check("first-body lane: the boss's body between station and player stops it", bkHits.Count == 0 && !me.Down, string.Join(", ", bkHits.Select(h => h.Source)));
            BkPlace(me, s0 * 5); BkPlace(ally, s0 * 9);
            ult.FirstBodyLane(clocks, "Lane probe", UltimateState.Station(0), me.transform.position, 1.6f, 3, p => p == me ? UltimateState.Lethal : 60);
            BkStep(4);
            Check("first-body lane: an ally in front takes 60 and saves the player", BkHitsOn(ally, "Lane probe") == 1 && bkHits.First(h => h.Player == ally).Damage == 60 && BkHitsOn(me, "Lane probe") == 0);
            ally.DebugSetHp(PlayerController.MaxHp); BkPlace(ally, new Vector3(-4, 0, 6) - s0 * 4);
            ult.FirstBodyLane(clocks, "Lane probe", UltimateState.Station(0), me.transform.position, 1.6f, 3, p => p == me ? UltimateState.Lethal : 60);
            BkStep(4);
            Check("first-body lane: nothing in front = the one-shot reaches the player", me.Down && BkHitsOn(me, "Lane probe") == 1);
            yield return UltStart();
            if (bk == null) yield break;
            ult = bk.Ult; me = d.Player;
            // the ledger recorder: a resolved Leaning Bell is stored as it struck and copies exactly
            UltManual();
            ult.Recording = true;
            BkPlace(me, new Vector3(0, 0, 6));
            bk.UltMove(bk.UltLean, BellkeeperEncounter.LeanTuning(Vector3.left));
            UltStepAliveUntil(() => ult.Recorded.Count > 0, 400);
            ult.Recording = false;
            var f = ult.Recorded.FirstOrDefault();
            var copy = f?.ToHazard("copy", 10, 60);
            Check("ledger recorder: stores the resolved Leaning Bell half (Ultimate: 50 damage)", f != null && f.Source == "Leaning Bell" && f.Damage == UltShapes.LeanDamage, f == null ? "none" : f.Source + " " + f.Damage);
            Check("ledger recorder: a copy covers exactly the recorded half", copy != null && copy.Contains(new Vector3(-5, 0, 0)) && !copy.Contains(new Vector3(5, 0, 0)));
            // wipes: solo and four players
            var extras = new List<PlayerController> { BkAdd(new Vector3(3, 0, 3)), BkAdd(new Vector3(-3, 0, 3)), BkAdd(new Vector3(0, 0, -5)) };
            ult.Wipe("Wipe probe");
            Check("wipe: every player goes down (N = 4)", d.World.Players.All(p => p.Down) && ult.WipeCause == "Wipe probe");
            yield return UltStart();
            if (bk == null) yield break;
            bk.Ult.Wipe("Wipe probe");
            Check("wipe: solo goes down too", d.Player.Down);

            // boss death mid-plan cancels hazards, statuses and the root
            yield return UltStart();
            if (bk == null) yield break;
            bk.BeginCombat(); UltStepAliveUntil(() => d.World.Hazards.Count > 0, 600);
            bk.Ult.Still(d.Player, d.World.Tick + 1, 600, 0); BkStep(3);
            int pending = d.World.Hazards.Count; bool wasStill = !d.Player.MovementEnabled;
            bk.DebugDefeat();
            Check("Ultimate: boss death cancels pending hazards and statuses, and frees a Stilled player",
                  pending > 0 && wasStill && d.World.Hazards.Count == 0 && d.Player.MovementEnabled && !bk.Ult.All.Any(), $"{pending} pending, stilled {wasStill}");
            Check("Ultimate: boss death resets the damage hook", d.World.DealtScale == null || d.World.DealtScale(d.Player) == 1);
        }

        // ---------------- plan checks (the Ultimate session) ----------------
        IEnumerator UltPlanChecks()
        {
            yield return UltStart();
            if (bk == null) yield break;
            BellkeeperEncounter.PushTimersOn = false;
            bk.BeginCombat(); UltStepAlive(2);
            int t0 = UltTick0();
            Check("plan: a real attempt starts in P1 at full HP", bk.Segment == BellkeeperEncounter.UltSegment.P1 && bk.Hp == bk.MaxHp && bk.PhaseName == BellkeeperEncounter.SegmentNames[0], bk.PhaseName);
            UltStepAlive(Sim.Ticks(27.2f));
            var want = new (string key, float at)[] { ("ult_tolling_hour", 0), ("ult_ledger", 0), ("ult_pendulum_clocks", 3), ("leaning_bell", 5), ("toll_tide", 9), ("reflection", 15), ("leaning_bell", 18.5f), ("toll_tide", 25) };
            var seen = new Dictionary<string, int>();
            bool timed = true; var detail = new List<string>();
            foreach (var (key, at) in want)
            {
                seen.TryGetValue(key, out int n); seen[key] = n + 1;
                int t = UltLaunchTick(key, t0, n);
                if (t - t0 != Sim.Ticks(at)) { timed = false; detail.Add($"{key}#{n} at {(t < 0 ? -1 : (t - t0) / 60f):0.00}s"); }
            }
            Check("plan: P1 launches each move on its §5.1 tick", timed, detail.Count == 0 ? "all on time" : string.Join(", ", detail));
            Check("plan: P1 ends its loop with the punish window at 27 s", bk.PlanLog.Any(e => e.what == "punish" && e.tick - t0 == Sim.Ticks(27)), string.Join(", ", bk.PlanLog.Where(e => e.what == "punish").Select(e => (e.tick - t0) / 60f)));
            // HP floor: damage can't skip the phase
            int floor = Mathf.RoundToInt(bk.MaxHp * 0.85f);
            d.World.DamageBoss(bk.MaxHp, bk.transform.position, "probe");
            Check("plan: P1 holds the boss at 85 % until its transition", bk.Hp == floor, $"{bk.Hp} / {bk.MaxHp}");
            int t1 = UltStepAliveUntil(() => bk.Segment == BellkeeperEncounter.UltSegment.T1, Sim.Ticks(40));
            Check("plan: T1 follows at the end of the P1 loop", t1 >= 0 && bk.Phase == 1);
            int tt1 = UltTick0();
            int t2 = UltStepAliveUntil(() => bk.Segment == BellkeeperEncounter.UltSegment.P2, Sim.Ticks(40));
            Check("plan: T1 lasts 25 s, two Stopped Hours, then P2 (parted shell)", t2 >= 0 && Mathf.Abs((UltTick0() - tt1) / 60f - 25) < 1.2f && UltLaunchTick("ult_stopped_hour", tt1, 1) - tt1 == Sim.Ticks(12) && bk.Phase == 2,
                  $"{(UltTick0() - tt1) / 60f:0.0}s");
            d.World.DamageBoss(bk.MaxHp, bk.transform.position, "probe");
            Check("plan: P2 holds at 55 %", bk.Hp == Mathf.RoundToInt(bk.MaxHp * 0.55f), $"{bk.Hp}");
            int tp2 = UltTick0();
            int t3 = UltStepAliveUntil(() => bk.Segment == BellkeeperEncounter.UltSegment.P3, Sim.Ticks(45));
            int form = bk.PlanLog.FindIndex(e => e.what == "second form" && e.tick >= tp2);
            Check("plan: the second form plays at 55 %, then P3 (open chest)", t3 >= 0 && form >= 0 && bk.PlanLog[form].tick < UltTick0() && bk.Phase == 3);
            Check("plan: P2's loop launched the Hand, clocks, Backwards Bell, Stopped Hour and Heart Toll on time",
                  UltLaunchTick("ult_conductors_hand", tp2) - tp2 == 0 && UltLaunchTick("ult_pendulum_clocks", tp2) - tp2 == Sim.Ticks(6) && UltLaunchTick("ult_backwards_bell", tp2) - tp2 == Sim.Ticks(8)
                  && UltLaunchTick("ult_stopped_hour", tp2) - tp2 == Sim.Ticks(20) && UltLaunchTick("toll_tide", tp2) - tp2 == Sim.Ticks(27));
            d.World.DamageBoss(bk.MaxHp, bk.transform.position, "probe");
            int tp3 = UltTick0();
            UltStepAliveUntil(() => bk.Segment == BellkeeperEncounter.UltSegment.T2, Sim.Ticks(45));
            Check("plan: P3 -> T2 at 35 %", bk.Segment == BellkeeperEncounter.UltSegment.T2 && UltLaunchTick("ult_two_futures", tp3) - tp3 == Sim.Ticks(3) && UltLaunchTick("reflection", tp3) - tp3 == Sim.Ticks(23));
            int tt2 = UltTick0();
            UltStepAliveUntil(() => bk.Segment == BellkeeperEncounter.UltSegment.P4, Sim.Ticks(45));
            Check("plan: T2 (six tolls at 3 s, the quotes at 10 s) -> P4", bk.Segment == BellkeeperEncounter.UltSegment.P4 && UltLaunchTick("ult_tolling_hour", tt2) - tt2 == Sim.Ticks(3) && UltLaunchTick("ult_area_quotes", tt2) - tt2 == Sim.Ticks(10));
            d.World.DamageBoss(bk.MaxHp, bk.transform.position, "probe");
            int tp4 = UltTick0();
            UltStepAliveUntil(() => bk.Segment == BellkeeperEncounter.UltSegment.P5, Sim.Ticks(70));
            Check("plan: P4 replays the Ledger three times, then P5", bk.Segment == BellkeeperEncounter.UltSegment.P5 && UltLaunchTick("ult_ledger", tp4, 2) > 0);
            UltStepAlive(3);
            Check("plan: P5 lights the fissure and starts the Thirteenth Hour", bk.Mechanic<FractureOfTime>().Running && bk.Mechanic<ThirteenthHour>().Running);
            Check("plan: the boss never healed and stayed on one bar", bk.Hp <= Mathf.RoundToInt(bk.MaxHp * 0.15f) && bk.MaxHp == Mathf.RoundToInt(BellkeeperEncounter.UltimateBaseHp * BellkeeperEncounter.UltimateSoloScale));
            bk.DebugDefeat();
            Check("plan: death in P5 cancels everything", d.World.Hazards.Count == 0 && !bk.Mechanics.Any(m => m.Running) && bk.Dead);

            // practice from a phase
            foreach (var (arg, seg, hp, look) in new[] { ("P3", BellkeeperEncounter.UltSegment.P3, 0.55f, 3), ("T2", BellkeeperEncounter.UltSegment.T2, 0.35f, 3), ("2", BellkeeperEncounter.UltSegment.P2, 0.85f, 2), ("5", BellkeeperEncounter.UltSegment.P5, 0.15f, 3) })
            {
                yield return UltStart(null, arg);
                if (bk == null) yield break;
                bk.BeginCombat(); UltStepAlive(3);
                Check($"practice -lastbellUltimatePhase {arg}: starts in {seg} at {hp:P0} with its look", bk.Segment == seg && Mathf.Abs(bk.Hp - bk.MaxHp * hp) < 2 && bk.Phase == look, $"{bk.Segment} {bk.Hp}/{bk.MaxHp} look {bk.Phase}");
            }
            // a real attempt ignores the practice phase: a run with the Ultimate set starts at P1
            BellkeeperEncounter.SetOverride = BellkeeperEncounter.AbilitySet.Ultimate; BellkeeperEncounter.PhaseOverride = "P4";
            if (d.State != RunState.Menu) { d.SetPaused(true); d.ReturnToMenu(); yield return null; }
            d.StartRun("Vanguard"); d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 4, "Ultimate run combat");
            bk = d.Boss as BellkeeperEncounter;
            yield return null; yield return null;
            Check("a real attempt always starts at P1 (-lastbellUltimatePhase is practice only)", bk != null && bk.Set == BellkeeperEncounter.AbilitySet.Ultimate && bk.Segment == BellkeeperEncounter.UltSegment.P1);
            d.SetPaused(true);

            // push timers: wipe exactly at the push time if the threshold isn't reached, never if it is
            yield return UltStart();
            if (bk == null) yield break;
            BellkeeperEncounter.PushSeconds = new float[] { 4, 0, 150, 130, 0, 130, 2 };
            BellkeeperEncounter.PushTimersOn = true;
            bk.BeginCombat(); UltStepAlive(2); t0 = UltTick0();
            var extra = BkAdd(new Vector3(4, 0, 4));
            int tw = UltStepAliveUntil(() => bk.Ult.Wipes > 0, Sim.Ticks(6));
            Check("push timer: P1 not at 85 % by its push time wipes the party on that tick", tw >= 0 && bk.Ult.WipeCause == "The hour runs out" && d.World.Players.All(p => p.Down)
                  && bk.PlanLog.Any(e => e.what.StartsWith("wipe") && e.tick - t0 == Sim.Ticks(4)), $"wipe at {(bk.PlanLog.FirstOrDefault(e => e.what.StartsWith("wipe")).tick - t0) / 60f:0.00}s");
            yield return UltStart();
            if (bk == null) yield break;
            bk.BeginCombat(); UltStepAlive(2);
            d.World.DamageBoss(bk.MaxHp, bk.transform.position, "probe");
            UltStepAlive(Sim.Ticks(6));
            Check("push timer: never fires once the threshold is reached", bk.Ult.Wipes == 0 && !d.Player.Down);
            yield return UltStart(null, "P5");
            if (bk == null) yield break;
            bk.BeginCombat(); UltStepAlive(2); t0 = UltTick0();
            UltStepAliveUntil(() => bk.Ult.Wipes > 0 || d.Player.Down, Sim.Ticks(4));
            Check("P5 hard enrage: the party is wiped at the enrage (78 s; here shortened to 2 s)", d.Player.Down && (bk.Mechanic<ThirteenthHour>().Built || (bk.Ult.WipeCause == "The Thirteenth Hour" && bk.PlanLog.Any(e => e.what.StartsWith("wipe") && e.tick - t0 == Sim.Ticks(2 + BellkeeperEncounter.EnrageGrace)))));
            BellkeeperEncounter.PushSeconds = (float[])BellkeeperEncounter.DefaultPush.Clone();
            BellkeeperEncounter.PushTimersOn = true;

            // the strat bot's basics: it leaves a Leaning Bell at the 4.2 m/s proof speed, from the hit half's wall
            yield return UltStart();
            if (bk == null) yield break;
            var sb = UltimateParty.Drive(d.Player, 0, 1); sb.Speed = 4.2f; sb.Attacks = false;
            BkPlace(d.Player, new Vector3(-12.5f, 0, 0)); bkHits.Clear();
            bk.UltMove(bk.UltLean, BellkeeperEncounter.LeanTuning(Vector3.left));
            BkStep(UltShapes.LeanTicks + 5);
            Check("strat bot: leaves a Leaning Bell from the far wall at 4.2 m/s (walk + one dash)", BkHitsOn(d.Player, "Leaning Bell") == 0 && !d.Player.Down, $"at x {d.Player.transform.position.x:0.0} · {sb.DebugLine}");
            d.Player.Input = bot;
        }
    }
}
