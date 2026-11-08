using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using LastBell.Run.Presentation;

namespace LastBell.Run
{
    // Presentation lane suites (8 Nov 2026). Each piece has its own flag:
    //   LastBell.exe -lastbellAutotest -lastbellHitFeelOnly -lastbellOut <dir>
    //     damage numbers, hit-stop and the hit vignette: they show, merge, cap and never stack; nothing in them allocates per
    //     frame; and each main boss's fight gives the same tick results with the feature on as with the old look
    //   LastBell.exe -lastbellAutotest -lastbellBossMomentsOnly -lastbellOut <dir>   (RunAutoTest.BossMoments.cs)
    // Screenshots (before/after) are taken only in a windowed run; -batchmode skips them and still runs every check.
    public sealed partial class RunAutoTest
    {
        static bool HitFeelOnly => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellHitFeelOnly", StringComparison.OrdinalIgnoreCase));
        const int SameTicks = 1200;   // 20 s of each main boss per pass

        IEnumerator HitFeelChecks()
        {
            Note("---------- hit feel (Presentation lane) ----------");
            float t0 = Time.realtimeSinceStartup;
            Check("hit feel: on by default (settings stub)", PresentationSettings.DamageNumbers && PresentationSettings.HitStop && PresentationSettings.HitFlash);
            Check("hit feel: the HitFeel component rides on the director", d.GetComponent<HitFeel>() != null);

            // the allocation counter itself works here (Mono reports per-thread allocations); otherwise the check below is void
            long probe0 = GC.GetAllocatedBytesForCurrentThread(); var junk = new byte[4096]; long probe = GC.GetAllocatedBytesForCurrentThread() - probe0;
            bool counter = probe >= 4096 && junk.Length > 0;
            Check("hit feel: the allocation counter sees a 4 KB allocation", counter, $"{probe} bytes");

            HitStopUnitChecks();

            // every main boss: on, on again, then the old look; the tick results must match
            for (int e = 0; e < RunDirector.Bosses.Length; e++)
            {
                if (!RunDirector.Bosses[e].InRun) continue;
                string cls = RunDirector.Classes[e % RunDirector.Classes.Length], tag = "hit feel: " + LastBell.Menu.TestRushSetup.CardName(e);
                var a = new ulong[SameTicks]; var b = new ulong[SameTicks]; var c = new ulong[SameTicks];
                int shownA = 0, stopsA = 0, stopsC = 0, shownC = 0; long alloc = 0; int frames = 0;
                yield return SamePass(e, cls, a, true, measure: counter, shots: e == RunDirector.AurelionPractice ? "after" : null);
                shownA = HitFeel.NumbersShown; stopsA = HitFeel.Stops; alloc = HitFeel.AllocBytes; frames = HitFeel.MeasuredFrames;
                yield return SamePass(e, cls, b, true, false, null);
                yield return SamePass(e, cls, c, false, false, e == RunDirector.AurelionPractice ? "before" : null);
                shownC = HitFeel.NumbersShown; stopsC = HitFeel.Stops;
                int ab = Diverge(a, b), ac = Diverge(a, c);
                Note($"{tag}: hashes over {SameTicks} ticks: on/on diverge at {(ab < 0 ? "never" : ab.ToString())}, on/old at {(ac < 0 ? "never" : ac.ToString())}");
                Check($"{tag}: the feature shows numbers and hit-stops in a real fight", shownA > 0 && stopsA > 0, $"{shownA} numbers, {stopsA} stops");
                Check($"{tag}: the old look shows none of them", shownC == 0 && stopsC == 0, $"{shownC} numbers, {stopsC} stops");
                Check($"{tag}: the fight is deterministic run to run (baseline for the next check)", ab < 0, ab < 0 ? "" : $"first differs at tick {ab}");
                Check($"{tag}: tick results identical with the feature and with the old look", ac < 0 || (ab >= 0 && ac >= ab), ac < 0 ? "every tick" : $"first differs at tick {ac} (baseline {ab})");
                if (counter) Check($"{tag}: nothing in the feature allocates per frame or per hit", alloc == 0 && frames > 0, $"{alloc} bytes over {frames} frames{(NoFrames ? " (batchmode: Update and hits; OnGUI drawing is measured in a windowed run)" : " (Update, hits and OnGUI)")}");
            }
            PresentationSettings.TestOff = false; HitFeel.LegacyLook = false;
            yield return PhaseToMenu();
            d.SelectAllies(0);
            Note($"hit feel suite took {Time.realtimeSinceStartup - t0:0} s");
        }

        // the stop's own rules, on the clock: cap, no stacking, the gap, and the release
        void HitStopUnitChecks()
        {
            HitFeel.ClearTransient();
            int s0 = HitFeel.Stops;
            HitFeel.Stop(1f);
            Check("hit-stop: capped at 0.1 s", HitFeel.Stopped && Mathf.Approximately(HitFeel.LastStopLength, HitFeel.MaxStop), $"{HitFeel.LastStopLength:0.000} s");
            HitFeel.Stop(0.05f); HitFeel.Stop(0.05f);
            Check("hit-stop: a second stop during one (or in the gap after) is ignored, so stops never stack", HitFeel.Stops == s0 + 1, $"{HitFeel.Stops - s0} started");
            HitFeel.ClearTransient();
            Check("hit-stop: clearing releases the animation clock", !HitFeel.Stopped && CharacterPresenter.GlobalAnimScale == 1);
        }

        static int Diverge(ulong[] x, ulong[] y) { for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return i; return -1; }

        // One pass: a practice of boss e with a full party of Perfect bots (the player too, attacks on), stepped by hand two
        // ticks per frame from the first combat tick with a fixed random seed, hashing the world after every tick. Between the
        // ticks the frame runs normally, so every presentation Update, OnGUI and hit-stop runs as in play.
        IEnumerator SamePass(int e, string cls, ulong[] hashes, bool on, bool measure, string shots)
        {
            PresentationSettings.TestOff = !on; HitFeel.LegacyLook = !on;
            yield return PhaseToMenu();
            d.SelectAllies(RunParty.MaxAllies);
            if (!d.StartPractice(cls, e)) { Check("hit feel: practice accepted " + e, false); yield break; }
            yield return Until(() => RunDirector.Instance && RunDirector.Instance.State == RunState.Ready && RunDirector.Instance.Encounter == e, 30, "hit feel pass ready");
            if (RunDirector.Instance != d) { directorLog.AddRange(d.Log); d = RunDirector.Instance; d.InputOverride = bot; bot.Clear(); }
            var me = new AllyBot(RunParty.MaxAllies, RunParty.MaxAllies + 1, BotMode.Perfect);
            d.InputOverride = me; if (d.Player) d.Player.Input = me;
            bool attacks = AllyBot.Attacks; AllyBot.Attacks = true;
            d.RequestReady();
            yield return Until(() => d.State == RunState.Combat && d.Boss, 10, "hit feel pass combat");
            if (d.State != RunState.Combat) { AllyBot.Attacks = attacks; yield break; }
            d.SetPaused(true);
            HitFeel.ClearTransient(); HitFeel.NumbersShown = HitFeel.Stops = 0; HitFeel.AllocBytes = 0; HitFeel.MeasuredFrames = 0;
            UnityEngine.Random.InitState(9001);
            var w = d.World;
            for (int t = 0; t < hashes.Length; t++)
            {
                if (d.Player && !d.Player.Down && d.Player.Hp < 40) d.Player.DebugSetHp(d.Player.MaxHealth);   // test setup: the run keeps going
                w.Step();
                hashes[t] = WorldHash(w, d.Boss);
                if (measure && t == 120) { HitFeel.AllocBytes = 0; HitFeel.MeasuredFrames = 0; HitFeel.Measuring = true; }   // after warm-up
                if (shots != null && t == 600) { HitFeel.Measuring = false; yield return Shot("hitfeel_" + shots + "_fight"); HitFeel.Measuring = measure; }
                if ((t & 1) == 1) yield return null;
            }
            HitFeel.Measuring = false;
            if (shots != null) yield return PlayerHitShot(shots);
            AllyBot.Attacks = attacks; d.InputOverride = bot; if (d.Player) d.Player.Input = bot;
            yield return PhaseToMenu();
        }

        // a real test hazard on the player, for the hit vignette picture (taken a few frames after the hit)
        IEnumerator PlayerHitShot(string label)
        {
            if (NoFrames || !d.Player || d.Player.Down) yield break;
            d.Player.DebugSetHp(d.Player.MaxHealth);
            d.World.Hazards.Spawn(new Hazard { Source = "Test hit", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = d.Player.transform.position, Radius = 1.5f, ResolveAt = 2, Damage = 45 });
            for (int i = 0; i < 3; i++) d.World.Step();
            yield return null; yield return null;
            yield return Shot("hitfeel_" + label + "_player_hit");
        }

        static ulong Mix(ulong h, float f) { unchecked { return (h ^ (uint)BitConverter.SingleToInt32Bits(f)) * 1099511628211UL; } }
        static ulong Mix(ulong h, int v) { unchecked { return (h ^ (uint)v) * 1099511628211UL; } }
        static ulong WorldHash(CombatWorld w, BossEncounter boss)
        {
            ulong h = 1469598103934665603UL;
            h = Mix(h, w.Tick); h = Mix(h, w.Hazards.Count); h = Mix(h, w.Projectiles.Count); h = Mix(h, w.DamageDealt); h = Mix(h, w.DamageTaken);
            if (boss) { h = Mix(h, boss.Hp); var bp = boss.transform.position; h = Mix(h, bp.x); h = Mix(h, bp.z); h = Mix(h, boss.transform.eulerAngles.y); h = Mix(h, (boss.Current ?? "").GetHashCode()); }
            foreach (var p in w.Players)
            {
                if (!p) continue;
                var pp = p.transform.position; h = Mix(h, pp.x); h = Mix(h, pp.z); h = Mix(h, p.Hp); h = Mix(h, p.Down ? 1 : 0); h = Mix(h, p.Stamina);
            }
            return h;
        }
    }
}
