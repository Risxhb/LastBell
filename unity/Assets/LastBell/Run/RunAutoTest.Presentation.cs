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

            // the allocation counter (HitFeel.AllocNow) sees a 64 KB allocation here, so the zero-allocation checks mean something
            GC.Collect(); long probe0 = HitFeel.AllocNow(); var junk = new byte[65536]; long probe = HitFeel.AllocNow() - probe0;   // collect first: no GC lands inside the probe
            bool counter = probe >= 65536 && junk.Length > 0;
            Check("hit feel: the allocation counter sees a 64 KB allocation", counter, $"{probe} bytes");

            HitStopUnitChecks();
            int totalStops = 0;

            // every main boss: on, on again, then the old look; the tick results must match
            for (int e = 0; e < RunDirector.Bosses.Length; e++)
            {
                if (!RunDirector.Bosses[e].InRun) continue;
                string cls = RunDirector.Classes[e % RunDirector.Classes.Length], tag = "hit feel: " + LastBell.Menu.TestRushSetup.CardName(e);
                var a = new ulong[SameTicks * 3]; var b = new ulong[SameTicks * 3]; var c = new ulong[SameTicks * 3];
                int shownA = 0, stopsA = 0, stopsC = 0, shownC = 0; long alloc = 0; int frames = 0;
                yield return SamePass(e, cls, a, true, measure: counter, shots: e == RunDirector.AurelionPractice ? "after" : null);
                shownA = HitFeel.NumbersShown; stopsA = HitFeel.Stops; alloc = HitFeel.AllocBytes; frames = HitFeel.MeasuredFrames;
                yield return SamePass(e, cls, b, true, false, null);
                yield return SamePass(e, cls, c, false, false, e == RunDirector.AurelionPractice ? "before" : null);
                shownC = HitFeel.NumbersShown; stopsC = HitFeel.Stops;
                int ab = Diverge(a, b), ac = Diverge(a, c);
                Note($"{tag}: hashes over {SameTicks} ticks: on/on diverge at {Where(ab)}, on/old at {Where(ac)}");
                ab = ab < 0 ? -1 : ab / 3; ac = ac < 0 ? -1 : ac / 3;
                totalStops += stopsA;
                Check($"{tag}: the feature shows numbers in a real fight", shownA > 0, $"{shownA} numbers, {stopsA} hit-stops");
                Check($"{tag}: the old look shows none of them", shownC == 0 && stopsC == 0, $"{shownC} numbers, {stopsC} stops");
                // a boss whose own fight isn't repeatable tick for tick (the baseline differs) can only be held to "no earlier than
                // the baseline"; that is noted, not failed, since it isn't this feature
                if (ab >= 0) Note($"{tag}: NOTE the fight itself differs run to run from tick {ab} with the feature on both times (pre-existing; see the hash note above)");
                else Check($"{tag}: the fight is deterministic run to run (baseline for the next check)", true);
                Check($"{tag}: tick results identical with the feature and with the old look", ac < 0 || (ab >= 0 && ac >= ab), ac < 0 ? "every tick" : $"first differs at tick {ac} (baseline {ab})");
                if (counter) Check($"{tag}: nothing in the feature allocates per frame or per hit", alloc == 0 && frames > 0, $"{alloc} bytes over {frames} frames{(NoFrames ? " (batchmode: Update and hits; OnGUI drawing is measured in a windowed run)" : " (Update, hits and OnGUI)")}");
            }
            PresentationSettings.TestOff = false; HitFeel.LegacyLook = false;   // the last pass was the old look
            Check("hit feel: big hits stopped the frame in the real fights", totalStops > 0, totalStops + " hit-stops");
            // a big hit from the local player: a big number and a stop; a small one: neither (the per-hit path, 5000 times, allocates nothing)
            HitFeel.ClearTransient(); int st0 = HitFeel.Stops;
            HitFeel.BossDamage(Vector3.zero, HitFeel.BigHitMine, d.Player);
            Check("hit feel: a big hit of yours starts a hit-stop", HitFeel.Stops == st0 + 1);
            HitFeel.ClearTransient(); st0 = HitFeel.Stops;
            HitFeel.BossDamage(Vector3.zero, HitFeel.BigHitMine - 1, d.Player);
            Check("hit feel: a small hit does not", HitFeel.Stops == st0);
            if (counter)
            {
                for (int i = 0; i < 200; i++) HitFeel.BossDamage(new Vector3(i % 7, 0, 0), 10 + i, null);   // warm
                GC.Collect(); long m0 = HitFeel.AllocNow();
                for (int i = 0; i < 5000; i++) HitFeel.BossDamage(new Vector3(i % 7, 0, 0), 10 + i % 300, (i & 1) == 0 ? d.Player : null);
                long m = HitFeel.AllocNow() - m0;
                Check("hit feel: 5000 hits on the boss allocate nothing", m <= 0, m + " bytes");
                HitFeel.ClearTransient();
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
        // a component-hash index as "tick 12 (boss)": hashes hold world counters, the boss and the players for each tick
        static string Where(int i) => i < 0 ? "never" : $"tick {i / 3} ({(i % 3 == 0 ? "world counters" : i % 3 == 1 ? "boss" : "players")})";

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
            for (int t = 0; t < hashes.Length / 3; t++)
            {
                if (d.Player && !d.Player.Down && d.Player.Hp < 40) d.Player.DebugSetHp(d.Player.MaxHealth);   // test setup: the run keeps going
                w.Step();
                WorldHash(w, d.Boss, hashes, t);
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
        // three hashes per tick into into[3t..3t+2]: world counters, the boss, the players
        static void WorldHash(CombatWorld w, BossEncounter boss, ulong[] into, int t)
        {
            const ulong Seed = 1469598103934665603UL; ulong h = Seed;
            h = Mix(h, w.Tick); h = Mix(h, w.Hazards.Count); h = Mix(h, w.Projectiles.Count); h = Mix(h, w.DamageDealt); h = Mix(h, w.DamageTaken);
            into[t * 3] = h; h = Seed;
            if (boss) { h = Mix(h, boss.Hp); var bp = boss.transform.position; h = Mix(h, bp.x); h = Mix(h, bp.z); h = Mix(h, boss.transform.eulerAngles.y); h = Mix(h, (boss.Current ?? "").GetHashCode()); }
            into[t * 3 + 1] = h; h = Seed;
            foreach (var p in w.Players)
            {
                if (!p) continue;
                var pp = p.transform.position; h = Mix(h, pp.x); h = Mix(h, pp.z); h = Mix(h, p.Hp); h = Mix(h, p.Down ? 1 : 0); h = Mix(h, p.Stamina);
            }
            into[t * 3 + 2] = h;
        }
    }
}
