using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // The Unfinished Hour (Bellkeeper ability set v2, Hard only) acceptance checks, owned by The Unfinished Hour ability
    // session. Brief: bosses/4-shattered-hour-bellkeeper/abilities-v2/05-unfinished-hour.md ("Autotest" under Build notes).
    // Harness: RunAutoTest.BellkeeperV2.cs. Run alone with -lastbellBellkeeperV2 -lastbellTestAbility unfinished_hour.
    //
    // Most checks feed the encounter stand-in Leaning Bell halves through the real hook (Boss.RememberLean, called from a
    // hazard's OnResolve exactly as 01 does), so they hold whatever state Leaning Bell's own branch is in. The last check
    // runs the practice loop with the real Leaning Bell once it is built.
    public sealed partial class RunAutoTest
    {
        UnfinishedHourAbility uh;
        const string UhName = "The Unfinished Hour";

        // Leaning Bell's locked half (01: a cone from the arena centre, 90° half-angle, reaching the wall).
        static Hazard UhHalf(float yaw) => new Hazard
        {
            Source = "Leaning Bell (test stand-in)", Shape = HazardShape.Cone, Kind = HazardKind.Strike, Origin = Vector3.zero,
            Yaw = yaw, Radius = 19f, HalfAngle = 90f, Damage = 40, LockAt = 0,
        };
        // A live stand-in Leaning Bell that resolves in `warn` ticks and remembers itself on resolve, as 01 does.
        Hazard UhLean(float yaw, int warn, Action<int> resolved = null)
        {
            var h = UhHalf(yaw); h.ResolveAt = warn;
            h.OnResolve = hz => { resolved?.Invoke(d.World.Tick); bk.RememberLean(hz); };
            return d.World.Hazards.Spawn(h);
        }
        // Practice of this move with its loop held: the scheduler runs, the echo is armed as in Hard phase 2+, and the test
        // feeds the halves. The boss stands where a Leaning Bell leaves it (within 3 m of the centre).
        IEnumerator UhStart(Difficulty level = Difficulty.Hard, bool testMap = false)
        {
            yield return BkStart("unfinished_hour", level, testMap);
            if (bk == null) yield break;
            uh = bk.Ability<UnfinishedHourAbility>();
            uh.PracticeHold = true;
            bk.transform.position = new Vector3(0, 0, -2.5f);
            bk.BeginCombat(); BkStep(1);
            bk.EchoArmed = true;
        }
        int UhHits(PlayerController p) => BkHitsOn(p, UhName);
        int UhDamage => Mathf.RoundToInt(UnfinishedHourAbility.Damage * d.World.DamageTakenScale);

        IEnumerator UnfinishedHourChecks()
        {
            Note("The Unfinished Hour (Hard only, proposal)");

            // ---- Hard only ----
            foreach (var lv in new[] { Difficulty.Easy, Difficulty.Medium })
            {
                yield return UhStart(lv);
                if (bk == null) yield break;
                bk.RememberLean(UhHalf(90)); bk.RememberLean(UhHalf(270)); BkStep(3);
                Check($"The Unfinished Hour: absent on {lv} (even when armed: no hazard, no memory outline)",
                      uh.Spawned.Count == 0 && d.World.Hazards.Count == 0 && uh.MemoryShown == null, $"casts {uh.Casts}, spawned {uh.Spawned.Count}");
            }

            // ---- Hard: the previous half replays in world space, 3.5 s after the current Leaning Bell resolves ----
            yield return UhStart();
            if (bk == null) yield break;
            Check("The Unfinished Hour: warning is 3.5 s on the Throne (map-adaptive rule)", UnfinishedHourAbility.WarningTicks() == 210, $"{UnfinishedHourAbility.WarningTicks()} ticks");
            var A = UhHalf(90);   // the remembered half: +x
            bk.RememberLean(A);
            Check("The Unfinished Hour: the remembered half shows as the dashed outline, harmless", uh.MemoryShown == A && d.World.Hazards.Count == 0);
            var hug = BkAdd(new Vector3(1.2f, 0, -1.0f));     // hugging the boss, in the remembered half
            var mid = BkAdd(new Vector3(8.5f, 0, 2f));        // long range
            var wall = BkAdd(BkEdge(new Vector3(1, 0, 0.3f)));// at the wall
            var other = BkAdd(new Vector3(-6, 0, 3));         // the other half (where the current lean strikes)
            BkPlace(d.Player, new Vector3(-4, 0, -6));
            int leanResolved = -1;
            var B = UhLean(270, 210, t => leanResolved = t);  // the current Leaning Bell, on the -x half
            bk.transform.rotation = Quaternion.Euler(0, 200, 0);   // the boss faces elsewhere: the echo must not follow it
            int waited = BkStepUntil(() => uh.Spawned.Count > 0, 400);
            var e = uh.Spawned.FirstOrDefault();
            Check("The Unfinished Hour: launches on the tick after the current Leaning Bell resolves",
                  e != null && leanResolved >= 0 && uh.LastCastTick == leanResolved + 1, $"lean resolved {leanResolved}, cast {uh.LastCastTick}");
            Check("The Unfinished Hour: replays the previous half (not the current one) at its world position and orientation",
                  e != null && e.Shape == HazardShape.Cone && e.Origin == A.Origin && Mathf.Approximately(e.Yaw, A.Yaw) && e.HalfAngle == 90 && e.Radius == A.Radius && e.Kind == HazardKind.Strike,
                  e == null ? "no echo" : $"yaw {e.Yaw} (A {A.Yaw}, B {B.Yaw}), origin {e.Origin}, boss yaw {bk.transform.eulerAngles.y:0}");
            Check("The Unfinished Hour: the outline turns into the live warning with the porcelain after-image",
                  uh.MemoryShown == null && uh.AfterImageShown && uh.Live == e);
            Check("The Unfinished Hour: starts only after the Leaning Bell is gone (at most two damaging patterns)",
                  !d.World.Hazards.Live.Contains(B) && BkPatternsLive() <= 2, $"{BkPatternsLive()} live");
            BkStepUntil(() => uh.Resolves > 0, 300);
            Check("The Unfinished Hour: resolves 3.5 s after the Leaning Bell's resolve", uh.LastResolveTick == leanResolved + 210,
                  $"lean {leanResolved}, echo {uh.LastResolveTick}");
            int dmg = bkHits.Where(h => h.Source == UhName).Select(h => h.Damage).DefaultIfEmpty(0).Max();
            Check("The Unfinished Hour: a still player hugging the boss in that half is hit", UhHits(hug) == 1);
            Check("The Unfinished Hour: a still player at 8 m+ in that half is hit", UhHits(mid) == 1);
            Check("The Unfinished Hour: a still player at the wall in that half is hit", UhHits(wall) == 1);
            Check("The Unfinished Hour: the other half is safe from the echo", UhHits(other) == 0 && UhHits(d.Player) == 0);
            Check("The Unfinished Hour: 40 at Medium, ×1.3 on Hard", dmg == UhDamage && UhDamage == 52, $"{dmg}");
            BkStep(60);
            Check("The Unfinished Hour: one hit per instance", UhHits(hug) == 1 && UhHits(mid) == 1 && UhHits(wall) == 1 && uh.Spawned.Count == 1);
            Check("The Unfinished Hour: afterwards the half just struck is the one memory (dashed outline), the after-image is gone",
                  !uh.Running && uh.MemoryShown == B && !uh.AfterImageShown && bk.LastLean == B, $"memory {(uh.MemoryShown == null ? "none" : uh.MemoryShown.Yaw.ToString())}");

            // ---- escape: the far wall of the remembered half, 4.2 m/s + one dash, 0.6 s reaction ----
            yield return UhEscape(false);
            // ---- cross, then cross back: two crossings with finite stamina ----
            yield return UhCrossBack();
            // ---- the square test map: 4.4 s, the corner is the longest run ----
            yield return UhEscape(true);

            // ---- at most two damaging patterns: the echo with Striking Order (as in Lean and Order) ----
            yield return UhStart();
            if (bk == null) yield break;
            for (int i = 0; i < 3; i++) BkAdd(new Vector3(-5 + 5 * i, 0, 5));
            bk.RememberLean(UhHalf(90));
            UhLean(270, 210);
            var order = bk.Ability(BellkeeperAbilities.StrikingOrder);
            int maxLive = 0;
            for (int i = 0; i < 700; i++)
            {
                if (i == BellkeeperEncounter.OrderAfterLean) bk.Launch(order);
                d.World.Step(); BkTopUp(); maxLive = Mathf.Max(maxLive, BkPatternsLive());
            }
            Check("The Unfinished Hour: never more than two live damaging patterns (echo + Striking Order)", maxLive <= 2 && uh.Resolves == 1,
                  $"max {maxLive}, Striking Order {(order.Built ? "built" : "stub")}");

            // ---- boss death mid-windup cancels it ----
            yield return UhStart();
            if (bk == null) yield break;
            BkPlace(d.Player, new Vector3(6, 0, 0));
            bk.RememberLean(UhHalf(90)); bk.RememberLean(UhHalf(270));
            BkStep(100);
            var dying = uh.Live;
            bk.DebugDefeat();
            BkStep(200);
            Check("The Unfinished Hour: boss death mid-windup cancels it, and the memory is erased",
                  dying != null && !d.World.Hazards.Live.Contains(dying) && UhHits(d.Player) == 0 && uh.Resolves == 0
                  && !uh.AfterImageShown && uh.MemoryShown == null && bk.LastLean == null && bk.PreviousLean == null);

            // ---- memory cleared on practice restart and on retry ----
            yield return UhStart();
            if (bk == null) yield break;
            bk.EchoArmed = false;
            bk.RememberLean(UhHalf(90));
            bool shown = uh.MemoryShown != null;
            bk.BeginCombat(); BkStep(1);
            Check("The Unfinished Hour: a practice restart erases the memory", shown && uh.MemoryShown == null && bk.LastLean == null);
            bk.EchoArmed = true;
            bk.RememberLean(UhHalf(0)); bk.RememberLean(UhHalf(180)); BkStep(60);
            var oldUh = uh; var oldEcho = uh.Live;
            d.SetPaused(true); d.RequestRetry(); d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 4, "Bellkeeper retry combat");
            var nb = d.Boss as BellkeeperEncounter;
            Check("The Unfinished Hour: retry erases the memory and the live echo",
                  oldEcho != null && nb != null && nb != bk && nb.LastLean == null && nb.PreviousLean == null && !oldUh.AfterImageShown && oldUh.MemoryShown == null
                  && !d.World.Hazards.Live.Any(h => h.Source == UhName));
            d.SetPaused(true);

            // ---- memory cleared on phase change (phase plan, Hard) ----
            yield return BkStart(null, Difficulty.Hard);
            if (bk == null) yield break;
            uh = bk.Ability<UnfinishedHourAbility>();
            bk.BeginCombat(); BkStep(2);
            bk.RememberLean(UhHalf(90));
            Check("The Unfinished Hour: Hard phase 1 draws no memory (phase 1 never replays)", uh.MemoryShown == null && bk.LastLean != null);
            bk.DebugSetHpFraction(0.69f);
            BkRun(() => bk.PhaseName == BellkeeperEncounter.Phase2, 60 * 40);
            Check("The Unfinished Hour: a phase change erases the memory", bk.PhaseName == BellkeeperEncounter.Phase2 && bk.LastLean == null && bk.PreviousLean == null && uh.MemoryShown == null,
                  $"{bk.PhaseName}");

            // ---- the real practice hook: -lastbellAbility unfinished_hour = two Leaning Bells, then the echo ----
            yield return BkStart("unfinished_hour", Difficulty.Hard);
            if (bk == null) yield break;
            uh = bk.Ability<UnfinishedHourAbility>(); var lean = bk.Ability(BellkeeperAbilities.LeaningBell);
            Check("The Unfinished Hour: practice key unfinished_hour loops this move", bk.PracticeAbility == uh);
            if (lean.Built)
            {
                bk.BeginCombat();
                BkRun(() => uh.Spawned.Count > 0, 60 * 30);
                var echo = uh.Spawned.FirstOrDefault(); var halves = lean.Spawned;
                Hazard first = halves.Count >= 2 ? halves[halves.Count - 2] : null;
                Check("The Unfinished Hour practice: two Leaning Bells, then the echo replays the first one's half",
                      echo != null && lean.Casts == 2 && first != null && Mathf.Approximately(echo.Yaw, first.Yaw) && echo.Origin == first.Origin,
                      echo == null ? $"no echo, {lean.Casts} leans" : $"echo yaw {echo.Yaw}, leans {string.Join(", ", halves.Select(h => h.Yaw.ToString("0")))}");
                BkRun(() => uh.Resolves > 0, 400);
                yield return BkClip("bk_unfinished_hour_practice", 14f);
            }
            else
            {
                Note("The Unfinished Hour practice with the real Leaning Bell: skipped until Leaning Bell is built (its stub never calls RememberLean)");
                yield return UhClip();
            }
        }

        // A still-to-move player at the longest run in the remembered half: reacts 0.6 s after the echo appears, then one
        // dash and a 4.2 m/s walk across the line, past the boss body.
        IEnumerator UhEscape(bool testMap)
        {
            yield return UhStart(Difficulty.Hard, testMap);
            if (bk == null) yield break;
            float yaw = testMap ? 45 : 90;                          // test map: a diagonal line, so a corner is the far point
            var n = Sim.Dir(yaw); var l = Sim.Dir(yaw + 90);
            bk.transform.position = -l * 2.5f;                       // within 3 m of the centre, beside the route
            var start = BkEdge(n);
            var runner = BkAdd(start);
            BkPlace(d.Player, -n * 5 - l * 5);
            var s = BkScript(runner); s.Speed = 4.2f;
            bk.RememberLean(UhHalf(yaw));
            UhLean(yaw + 180, 210);                                  // the current lean strikes the other half: the runner waits
            BkStepUntil(() => uh.Spawned.Count > 0, 400);
            var echo = uh.Live; int warn = UnfinishedHourAbility.WarningTicks();
            BkStep(Sim.Ticks(0.6f));
            var goal = -n * 0.4f + l * 1.2f; s.Goal = goal; s.DashNext = true;
            float minSt = runner.Stamina;
            int ticks = 0;
            while (uh.Resolves == 0 && ticks < 600) { d.World.Step(); ticks++; minSt = Mathf.Min(minSt, runner.Stamina); }
            float dist = Sim.Flat(goal - start).magnitude;
            string map = testMap ? "test map corner, 4.4 s" : "Throne wall, 3.5 s";
            Check($"The Unfinished Hour: escape from the far side of the remembered half at 4.2 m/s + one dash ({map})",
                  echo != null && UhHits(runner) == 0 && minSt >= 0 && warn == (testMap ? 264 : 210),
                  $"{dist:0.00} m route, warning {warn} ticks, stamina min {minSt:0}, hits {UhHits(runner)}");
        }

        // Cross away from the current Leaning Bell into the remembered half, then cross back when it replays (05: "cross,
        // then cross back"), each with a dash, from the wall: stamina must cover both.
        IEnumerator UhCrossBack()
        {
            yield return UhStart();
            if (bk == null) yield break;
            var runner = BkAdd(BkEdge(Vector3.left));               // far wall of the current lean's half (-x)
            BkPlace(d.Player, new Vector3(0, 0, 8));
            var s = BkScript(runner); s.Speed = 4.2f;
            bk.RememberLean(UhHalf(90));                             // remembered: +x
            var lean = UhLean(270, 210);                             // current: -x
            BkStep(Sim.Ticks(0.6f));
            s.Goal = new Vector3(1.0f, 0, 1.6f); s.DashNext = true;
            float minSt = runner.Stamina, before2 = 0, after2 = 0;
            BkStepUntil(() => uh.Spawned.Count > 0, 400);
            for (int i = 0; i < 400 && uh.Resolves == 0; i++)
            {
                if (i == Sim.Ticks(0.6f)) { s.Goal = new Vector3(-1.0f, 0, 1.6f); s.DashNext = true; before2 = runner.Stamina; }
                if (i == Sim.Ticks(0.6f) + 14) after2 = runner.Stamina;
                d.World.Step(); minSt = Mathf.Min(minSt, runner.Stamina);
            }
            Check("The Unfinished Hour: cross, then cross back, a dash each time, with finite stamina",
                  BkHitsOn(runner) == 0 && uh.Resolves == 1 && before2 - after2 >= PlayerController.DashCost - 1 && minSt >= 0,
                  $"hits {BkHitsOn(runner)}, second dash {before2:0} -> {after2:0}, stamina min {minSt:0}");
        }

        // Real-time clip of the echo: a stand-in Leaning Bell on one half, then the remembered half replaying, with the
        // camera's player and a scripted ally crossing, then crossing back (each steps out of a half about to land).
        // Screenshots come from here: telegraphs only fade in while the game runs.
        IEnumerator UhClip()
        {
            yield return UhStart();
            if (bk == null) yield break;
            Func<PlayerController, Vector3> steer = me =>
            {
                foreach (var h in d.World.Hazards.Live)
                    if (h.Shape == HazardShape.Cone && h.Contains(me.transform.position) && h.ResolveAt - h.Age < 150) return -Sim.Dir(h.Yaw);
                return Vector3.zero;
            };
            BkPlace(d.Player, new Vector3(-3, 0, 5)); BkScript(d.Player).Steer = steer;
            var p = BkAdd(new Vector3(-7, 0, 1)); BkScript(p).Steer = steer;
            bk.RememberLean(UhHalf(90));
            UhLean(270, 210);
            d.SetPaused(false);
            StartCoroutine(Capture("bk_unfinished_hour", 9 * 60));
            float t = 0; bool memShot = false, echoShot = false;
            while (t < 9f)
            {
                BkTopUp(); t += Time.deltaTime;
                if (!memShot && t >= 1.5f) { memShot = true; yield return Shot("bk_unfinished_hour_memory_outline"); continue; }
                if (!echoShot && t >= 5.8f) { echoShot = true; yield return Shot("bk_unfinished_hour_warning"); continue; }
                yield return null;
            }
            yield return null;
            d.SetPaused(true); bk.Halt(); d.World.ClearTransient();
        }
    }
}
