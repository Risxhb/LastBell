using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Bellkeeper Ultimate: Two Futures checks (ULTIMATE.md §3.5; this session's file). Harness: RunAutoTest.Bellkeeper.Ultimate.cs.
    //   -lastbellBellkeeperUltimate -lastbellTestAbility ult_two_futures
    public sealed partial class RunAutoTest
    {
        TwoFutures tf;
        readonly List<(int tick, Hazard h)> tfSpawns = new List<(int, Hazard)>();

        IEnumerator TfFresh(string segment = null)
        {
            yield return UltStart(null, segment);
            tf = bk ? bk.Mechanic<TwoFutures>() : null;
            tfSpawns.Clear();
            if (bk && segment == null) UltManual();   // hand launches: the scheduler runs, no plan
            if (bk) bk.HazardSpawned += (a, h) => tfSpawns.Add((d.World.Tick, h));
        }
        static UltCue TfCue(int phase, int seed) => new UltCue { Variant = "futures", At = new[] { 7f }, Phase = phase, Seed = seed };
        // the seeded choice, as TwoFutures.Choose() draws it
        static (int pair, int rot, bool mirror, bool iFirst) TfPick(int phase, int seed)
        {
            var rng = new System.Random(seed * 16807 + 11);
            int pair = rng.Next(phase == 5 ? TwoFutures.P5Pairs.Length : TwoFutures.P3Pairs.Length), rot = rng.Next(12); bool mirror = rng.Next(2) == 1, first = rng.Next(2) == 0;
            return (pair, rot, mirror, first);
        }
        static int TfSeed(int phase, int pair, bool iFirst, int skip = 0)
        {
            for (int s = 0; s < 5000; s++) { var r = TfPick(phase, s); if (r.pair == pair && r.iFirst == iFirst && skip-- <= 0) return s; }
            return -1;
        }
        bool TfHit(BkHit h) => h.Source == TwoFutures.Source || h.Source == UltShapes.HeartSource;

        // one cast with n players driven by strat bots (no attacks, `speed`), stepped to the end without top-ups
        IEnumerator TfBotRun(int phase, int seed, int n, UltimateBot.Skill mode, float speed = 4.2f)
        {
            yield return TfFresh();
            if (bk == null) yield break;
            for (int i = 1; i < n; i++) BkAdd(Sim.Dir(360f * i / n) * 6f);
            var bots = new List<UltimateBot>();
            for (int i = 0; i < n; i++) { var b = UltimateParty.Drive(d.World.Players[i], i, n, mode); b.Speed = speed; b.Attacks = false; bots.Add(b); }
            bkHits.Clear(); tfTrace.Clear();
            bk.UltLaunch(tf, TfCue(phase, seed));
            var me = d.Player;
            tfTrace.Add($"P{phase} seed {seed} pair {tf.PairIndex} rot {tf.Rotation} {(tf.FirstIsI ? "I" : "II")} first: {string.Join(" / ", tf.Order.Select(o => tf.Futures[o].ToString()))}");
            int end = tf.StepTicks[5] + 3;
            while (d.World.Tick < end)
            {
                BkStep(1);
                int t = d.World.Tick;
                int k = System.Array.IndexOf(tf.StepTicks, t);
                bool pre = t == tf.VerdictTick, before = System.Array.IndexOf(tf.StepTicks, t + 30) >= 0;
                if (k >= 0 || pre || before)
                {
                    var pos = Sim.Flat(me.transform.position); var hint = pre ? tf.PrepositionFor(me) : tf.HintFor(me, k >= 0 ? k : System.Array.IndexOf(tf.StepTicks, t + 30));
                    tfTrace.Add($"  {(pre ? "verdict" : k >= 0 ? "step " + (k + 1) : "coral " + (System.Array.IndexOf(tf.StepTicks, t + 30) + 1))} t {(t - tf.StepTicks[0]) / 60f:0.00}: at ({pos.x:0.0},{pos.z:0.0}) r {(pos - tf.Anchor).magnitude:0.0} hint ({hint.x:0.0},{hint.z:0.0}) r {Sim.Flat(hint - tf.Anchor).magnitude:0.0}{(k >= 0 ? (tf.StepHits(k, pos) ? " HIT" : " ok") : "")} st {me.Stamina:0} · {bots[0].DebugLine}");
                }
            }
            d.Player.Input = bot;
        }
        readonly List<string> tfTrace = new List<string>();
        int tfTraced;
        void TfNoteTrace() { if (tfTraced++ >= 3) return; foreach (var l in tfTrace) Note(l); }

        IEnumerator UltTwoFuturesChecks()
        {
            Note("Ultimate: Two Futures (§3.5): trace 4.8 s, verdict 1.5 s before step 1, six interleaved steps 1.2 s apart, coral 0.5 s, 60 once per step; last step with a Heart Toll");

            // ---------- authored sets: routes exist on paper for every pair, rotation, mirror and verdict ----------
            yield return TfFresh();
            if (bk == null) yield break;
            {
                float worst = 0; string where = ""; bool lastOut = true, clockOk = true, fissureOk = true, gaps = true; float minGap = 99;
                for (int phase = 3; phase <= 5; phase += 2)
                    for (int seed = 0; seed < 160; seed++)
                    {
                        bk.Halt(); d.World.ClearTransient();
                        bk.UltLaunch(tf, TfCue(phase, seed));
                        var me = d.Player;
                        for (int st = -1; st < 4; st++)
                        {
                            if (st >= 0) bk.Ult.SetClock(me, 1, st, -1); else bk.Ult.ClearClock(me);
                            if (st < 0) bk.Ult.Of(me).Station = -1;
                            for (int o = 0; o < 2; o++)
                            {
                                bool iFirst = o == 0;
                                float m = tf.RouteMaxStep(me, iFirst);
                                if (m > worst) { worst = m; where = $"P{phase} seed {seed} pair {tf.PairIndex} rot {tf.Rotation} {(iFirst ? "I" : "II")} first"; }
                                var order = iFirst ? TwoFutures.OrderIFirst : TwoFutures.OrderIIFirst;
                                for (int k = 0; k < 6; k++)
                                {
                                    var q = tf.HintFor(me, k, iFirst);
                                    if (tf.PartsOf(order[k]).Any(h => h.Contains(q))) { worst = 99; where = $"hint {k} inside its step, P{phase} seed {seed}"; }
                                    if (k == 5 && Sim.Flat(q - tf.Anchor).magnitude < TollTideAbility.TollRadius + 0.3f) lastOut = false;
                                    if (phase == 5 && (FractureOfTime.InFlare(q) || Mathf.Abs(Vector3.Dot(q, UltimateState.FissureNormal)) < 1.65f)) fissureOk = false;
                                    if (phase == 5 && k == 1) { float r = Sim.Flat(q - tf.Anchor).magnitude; if (r < TwoFutures.ClockMin || r > TwoFutures.ClockMax) clockOk = false; }
                                    // the pockets of consecutive steps don't overlap: standing still for two steps fails
                                    if (k > 0)
                                    {
                                        var a = tf.Futures[order[k - 1]]; var b = tf.Futures[order[k]];
                                        if (a.Band && b.Band) { float g = Mathf.Max(a.Lo - b.Hi, b.Lo - a.Hi); minGap = Mathf.Min(minGap, g); if (g < 0.9f) gaps = false; }
                                        if (!tf.PartsOf(order[k]).Any(h => h.Contains(tf.HintFor(me, k - 1, iFirst)))) gaps = false;
                                    }
                                }
                            }
                        }
                        bk.Ult.ClearClock(me);
                    }
                bk.Halt(); d.World.ClearTransient();
                Check("Two Futures: every authored pair, rotation, mirror and verdict has a guide route ≤ 2.45 m per 1.2 s step (4.2 m/s: 5.0 m)", worst <= 2.45f, $"worst {worst:0.00} m ({where})");
                Check("Two Futures: no guide spot can be held through the next step (each step's spot is inside the next one's shapes)", gaps, $"band gaps ≥ {minGap:0.0} m");
                Check("Two Futures: the route ends beyond the Heart Toll (r 4 m + margin)", lastOut);
                Check("Two Futures P5: step 2 (with the clock pair) has its spot 3.8-7.4 m from the boss on every clock line", clockOk);
                Check("Two Futures P5: no guide spot stands in the fissure or its toll flare (≥ 1.65 m off the axis; crossing is allowed)", fissureOk);
            }

            // ---------- timing: trace, verdict, interleaved steps, coral lead, own Heart Toll ----------
            yield return TfFresh();
            if (bk == null) yield break;
            {
                int seed = TfSeed(3, 0, false);
                BkPlace(d.Player, new Vector3(0, 0, 6));
                bk.UltLaunch(tf, TfCue(3, seed));
                int L = tf.LastCastTick;
                var outl = new List<int>(); bool haloEarly = false, haloLate = false;
                int coralEarly = 0;
                for (int t = L; t <= tf.StepTicks[5] + 2; t++)
                {
                    if (t > L) UltStepAlive(1);
                    int rel = d.World.Tick - L;
                    if (rel % 48 == 1 && rel < 300) outl.Add(tf.OutlinesShown);
                    if (d.World.Tick < tf.VerdictTick && tf.HaloShown) haloEarly = true;
                    if (d.World.Tick == tf.VerdictTick + 1 && !tf.HaloShown) haloLate = true;
                    if (d.World.Tick < tf.StepTicks[0] - 30) coralEarly += d.World.Hazards.Live.Count(h => h.Source == TwoFutures.Source);
                    if (rel == 60) yield return Shot("ult-futures-trace");
                    if (d.World.Tick == tf.StepTicks[2] - 12) yield return Shot("ult-futures-step3-coral");
                }
                int[] want = Enumerable.Range(0, 6).Select(k => L + Sim.Ticks(7f) + Sim.Ticks(1.2f * k)).ToArray();
                Check("Two Futures: the trace draws one footprint per 0.8 s (I1 I2 I3 II1 II2 II3), then fades",
                      outl.Count >= 7 && Enumerable.Range(0, 6).All(j => outl[j] == tf.PartsOf(j).Count) && outl[6] == 0, string.Join(",", outl));
                Check("Two Futures: halo verdict exactly 1.5 s before step 1, not before", !haloEarly && !haloLate && tf.VerdictTick == want[0] - 90);
                Check("Two Futures: six steps resolve at At[0] + 1.2 k on their exact ticks", tf.StepResolves.SequenceEqual(want) && tf.StepTicks.SequenceEqual(want), string.Join(",", tf.StepResolves.Select(t => (t - L) / 60f)));
                var stepSpawn = tfSpawns.Where(s => s.h.Source == TwoFutures.Source).GroupBy(s => s.tick).Select(g => g.Key).ToList();
                Check("Two Futures: each step goes coral only 0.5 s before it strikes (no earlier coral)", coralEarly == 0 && stepSpawn.SequenceEqual(want.Select(w => w - 30)), string.Join(",", stepSpawn.Select(t => (t - L) / 60f)));
                Check("Two Futures: interleaved order (II first here): II1, I1, II2, I2, II3, I3", !tf.FirstIsI && tf.Order.SequenceEqual(TwoFutures.OrderIIFirst));
                var ht = tf.OwnHeartToll; int htSpawn = tfSpawns.FirstOrDefault(s => s.h == ht).tick;
                Check("Two Futures: with no Heart Toll on its way (practice / P5), it rings its own on the last step's tick (2.0 s warning)",
                      ht != null && htSpawn + ht.ResolveAt - 1 == want[5] && want[5] - htSpawn == UltShapes.HeartTicks, ht == null ? "none" : $"resolves {(htSpawn + ht.ResolveAt - 1 - L) / 60f:0.00}s");
                Check("Two Futures: one tuning, 60 per step (Heavy), Strike cones and Ground bands", tf.PartsOf(0).All(h => h.Damage == 60) && tf.PartsOf(0).All(h => (h.Shape == HazardShape.Cone) == (h.Kind == HazardKind.Strike)));
            }

            // ---------- still melee / still ranged / a still player on a cone seam ----------
            yield return TfFresh();
            if (bk == null) yield break;
            {
                int seed = TfSeed(3, 0, true);
                var melee = d.Player; BkPlace(melee, new Vector3(0, 0, 2.3f));
                var ranged = BkAdd(new Vector3(0, 0, 11f));
                var seam = BkAdd(new Vector3(-3, 0, 3));
                bkHits.Clear();
                bk.UltLaunch(tf, TfCue(3, seed));
                var f0 = tf.Futures[tf.Order[0]];
                BkPlace(seam, tf.Anchor + TwoFutures.HourDir(f0.Hour + 3) * 6.5f);   // on the seam of two cones, inside the ring
                UltStepAlive(tf.StepTicks[5] - d.World.Tick + 2);
                int Hm = bkHits.Count(h => h.Player == melee && h.Source == TwoFutures.Source), Hr = bkHits.Count(h => h.Player == ranged && h.Source == TwoFutures.Source);
                var seamHits = bkHits.Where(h => h.Player == seam && h.Source == TwoFutures.Source).ToList();
                bool once = d.World.Players.All(p => bkHits.Where(h => h.Player == p && h.Source == TwoFutures.Source).GroupBy(h => h.Tick).All(g => g.Count() == 1));
                Check("Two Futures: a still melee player (2.3 m) is hit", Hm >= 3, $"{Hm} of 6 steps");
                Check("Two Futures: a still ranged player (11 m) is hit", Hr >= 3, $"{Hr} of 6 steps");
                Check("Two Futures: one hit per player per step, even where two cones overlap (60, not 120)", once && seamHits.Count(h => h.Tick == tf.StepTicks[0]) == 1 && seamHits.All(h => h.Damage == 60),
                      $"seam: {seamHits.Count} hits, step 1: {seamHits.Count(h => h.Tick == tf.StepTicks[0])}");
                Check("Two Futures: never a one-shot, a wipe or a Cracked Hour", bkHits.All(h => h.Damage < UltimateState.Lethal) && bk.Ult.Wipes == 0 && bk.Ult.CrackedCount == 0);
            }

            // ---------- the intended route at 4.2 m/s, finite stamina: every pair, both verdicts, solo ----------
            var fails = new List<string>(); int runs = 0;
            for (int phase = 3; phase <= 5; phase += 2)
                for (int pair = 0; pair < (phase == 5 ? TwoFutures.P5Pairs.Length : TwoFutures.P3Pairs.Length); pair++)
                    for (int o = 0; o < 2; o++)
                    {
                        int seed = TfSeed(phase, pair, o == 0);
                        yield return TfBotRun(phase, seed, 1, UltimateBot.Skill.Perfect);
                        if (bk == null) yield break;
                        runs++;
                        var hits = bkHits.Where(TfHit).ToList();
                        if (hits.Count > 0 || d.Player.Down) { TfNoteTrace(); fails.Add($"P{phase} pair {pair} {(o == 0 ? "I" : "II")} first: {string.Join(", ", hits.Select(h => $"{h.Source}@{(h.Tick - tf.StepTicks[0]) / 60f:0.0}"))}"); }
                    }
            Check("Two Futures: the guide route clears every authored pair and verdict solo at 4.2 m/s (strat bot, no attacks)", fails.Count == 0, fails.Count == 0 ? $"{runs} runs clean" : string.Join(" | ", fails));

            // ---------- N = 2 / 4 / 8 ----------
            foreach (int n in new[] { 2, 4, 8 })
                foreach (int phase in new[] { 3, 5 })
                {
                    int seed = TfSeed(phase, n % (phase == 5 ? 2 : 3), n != 4);
                    yield return TfBotRun(phase, seed, n, UltimateBot.Skill.Perfect);
                    if (bk == null) yield break;
                    var hits = bkHits.Where(TfHit).ToList();
                    Check($"Two Futures N = {n}, P{phase}: identical rules, every player clears at 4.2 m/s", hits.Count == 0 && d.World.Players.All(p => !p.Down),
                          hits.Count == 0 ? $"pair {tf.PairIndex}" : string.Join(", ", hits.Select(h => $"{h.Player.name} {h.Source}@{(h.Tick - tf.StepTicks[0]) / 60f:0.0}")));
                }
            // identical shapes for any N (same seed)
            yield return TfFresh();
            if (bk == null) yield break;
            bk.UltLaunch(tf, TfCue(3, 41)); string solo = string.Join(";", tf.Futures.Select(f => f.ToString()));
            bk.Halt(); d.World.ClearTransient();
            for (int i = 0; i < 7; i++) BkAdd(Sim.Dir(45 * i) * 5);
            bk.UltLaunch(tf, TfCue(3, 41)); string eight = string.Join(";", tf.Futures.Select(f => f.ToString()));
            Check("Two Futures: the same shapes for N = 1 and N = 8", solo == eight, solo);

            // ---------- memory matters: a reactive-only bot fails, a late but memorising bot clears ----------
            {
                int seed = TfSeed(3, 1, true);
                yield return TfBotRun(3, seed, 1, UltimateBot.Skill.Miss);
                if (bk == null) yield break;
                int missHits = bkHits.Count(h => h.Source == TwoFutures.Source);
                Check("Two Futures: without memory (reacting to the 0.5 s coral only) the bot is hit at 4.2 m/s", missHits >= 1, $"{missHits} step hits");
                yield return TfBotRun(3, seed, 1, UltimateBot.Skill.Late);
                if (bk == null) yield break;
                Check("Two Futures: with memory but reacting 0.25 s late, the route still clears", !bkHits.Any(TfHit), string.Join(", ", bkHits.Where(TfHit).Select(h => h.Source)));
            }

            // ---------- boss death cancels everything, mid-trace and mid-steps ----------
            yield return TfFresh();
            if (bk == null) yield break;
            {
                bk.UltLaunch(tf, TfCue(3, 7)); UltStepAlive(100);
                bool tracing = tf.OutlinesShown > 0;
                bk.DebugDefeat();
                Check("Two Futures: boss death mid-trace clears the trace and the cast", tracing && tf.OutlinesShown == 0 && !tf.Running && !tf.HaloShown && d.World.Hazards.Count == 0);
            }
            yield return TfFresh();
            if (bk == null) yield break;
            {
                BkPlace(d.Player, new Vector3(0, 0, 2.3f));
                bk.UltLaunch(tf, TfCue(5, 9)); UltStepAlive(tf.StepTicks[1] - 10 - d.World.Tick);
                bool live = d.World.Hazards.Live.Any(h => h.Source == TwoFutures.Source) && tf.HaloShown;
                bkHits.Clear(); int at = d.World.Tick;
                bk.DebugDefeat(); BkStep(Sim.Ticks(8));
                Check("Two Futures: boss death mid-steps cancels the coral steps, the halo and the Heart Toll in that tick", live && d.World.Hazards.Count == 0 && !tf.HaloShown && !bkHits.Any(h => h.Tick > at), $"{bkHits.Count} hits after");
            }

            // ---------- in the plan: P3 and P5 practice starts ----------
            foreach (var seg in new[] { "P3", "P5" })
            {
                yield return TfFresh(seg);
                if (bk == null) yield break;
                BellkeeperEncounter.PushTimersOn = false;
                var sb = UltimateParty.Drive(d.Player, 0, 1); sb.Speed = 4.2f; sb.Attacks = false;
                bkHits.Clear();
                bk.BeginCombat(); BkStep(2);
                int t0 = UltTick0();
                int launched = -1; for (int i = 0; i < Sim.Ticks(12) && launched < 0; i++) { BkStep(1); launched = UltLaunchTick("ult_two_futures", t0); }
                float la = seg == "P3" ? 3 : 9;
                if (launched < 0) { Check($"Two Futures in {seg}: launched by the plan", false); d.Player.Input = bot; continue; }
                BkStep(tf.StepTicks[5] - d.World.Tick + 2);
                var hearts = tfSpawns.Where(s => s.h.Source == UltShapes.HeartSource).Select(s => s.tick + s.h.ResolveAt - 1).ToList();
                Check($"Two Futures in {seg}: launched at {la} s, steps {la + 7:0.0} … {la + 13:0.0} s on their ticks",
                      launched - t0 == Sim.Ticks(la) && tf.StepTicks[0] - t0 == Sim.Ticks(la + 7) && tf.StepTicks[5] - t0 == Sim.Ticks(la + 13) && tf.StepResolves.SequenceEqual(tf.StepTicks),
                      $"launch {(launched - t0) / 60f:0.00}s, steps {string.Join(",", tf.StepResolves.Select(t => ((t - t0) / 60f).ToString("0.0")))}");
                Check($"Two Futures in {seg}: exactly one Heart Toll on the last step's tick ({(seg == "P3" ? "the plan's" : "its own")})",
                      hearts.Count(h => h == tf.StepTicks[5]) == 1 && (seg == "P3" ? tf.OwnHeartToll == null : tf.OwnHeartToll != null), $"heart tolls at {string.Join(",", hearts.Select(h => (h - t0) / 60f))}");
                if (seg == "P5") Check("Two Futures in P5: step 2 lands on the clock pairs' tick (17.2 s)", tf.StepTicks[1] - t0 == Sim.Ticks(17.2f) && tf.BandFutures);
                Check($"Two Futures in {seg}: the strat bot walks the route at 4.2 m/s with no Futures or Heart Toll hit", !bkHits.Any(TfHit) && !d.Player.Down, string.Join(", ", bkHits.Where(TfHit).Select(h => $"{h.Source}@{(h.Tick - t0) / 60f:0.0}")));
                d.Player.Input = bot;
            }

            // ---------- practice hook ----------
            yield return UltStart("ult_two_futures");
            if (bk == null) yield break;
            tf = bk.Mechanic<TwoFutures>();
            bk.BeginCombat();
            int tp = UltStepAliveUntil(() => tf.Casts > 0, 300);
            UltStepAlive(Sim.Ticks(16));
            int tp2 = UltStepAliveUntil(() => tf.Casts > 1, Sim.Ticks(6));
            Check("practice -lastbellAbility ult_two_futures: loops alone, alternating the P3 and P5 futures", bk.PracticeAbility == tf && tp >= 0 && tp2 >= 0 && tf.BandFutures, $"casts {tf.Casts}");
        }
    }
}
