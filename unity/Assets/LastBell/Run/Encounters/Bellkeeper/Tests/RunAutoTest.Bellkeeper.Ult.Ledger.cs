using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Bellkeeper Ultimate: The Ledger checks (proposal, ULTIMATE.md §3.3, ultimate/LEDGER.md).
    // Harness: RunAutoTest.Bellkeeper.Ultimate.cs (UltStart, BkStep, UltStepAlive, bkHits, UltimateBot).
    // Direct launches run under the practice hook with LedgerMechanic.PracticeHold, so the encounter's scheduler steps
    // the casts while the test decides what P1 records.
    public sealed partial class RunAutoTest
    {
        LedgerMechanic led;
        static Vector3 LedDir1 => UltimateState.StationDir(LedgerMechanic.AuthoredStation);
        static float LedYaw1 => Quaternion.LookRotation(LedDir1).eulerAngles.y;
        static readonly string[] LedSources = { "Leaning Bell", "Toll Tide", "Heart Toll", "Reflection of the Hour", "Leaning Bell", "Heart Toll" };
        static readonly float[] LedNominal = { 8.5f, 11.5f, 13.5f, 19f, 22f, 27f };   // plan P1 resolve times (s from the loop's start)

        IEnumerator UltLedgerChecks()
        {
            Note("---- Ultimate: The Ledger ----");
            BellkeeperEncounter.PushTimersOn = false;
            yield return LedgerPlanRecordChecks();
            List<string> reference = null;
            foreach (int n in new[] { 4, 1, 2, 8 })
            {
                yield return LedgerCleanChecks(n, reference);
                if (n == 4 && led != null && led.Recorded != null) reference = LedgerShapes(led.Recorded);
            }
            yield return LedgerStratRecordChecks();
            yield return LedgerMessyChecks();
            yield return LedgerP4PracticeChecks(1);
            yield return LedgerP4PracticeChecks(8);
            yield return LedgerPageOnceChecks();
            yield return LedgerCancelChecks();
            yield return LedgerPracticeHookChecks();
            BellkeeperEncounter.PushTimersOn = true;
            d.Player.Input = bot;
        }

        // ---------------- helpers ----------------
        IEnumerator LedgerStartHeld(int n)
        {
            yield return UltStart("ult_ledger");
            if (bk == null) yield break;
            led = bk.Mechanic<LedgerMechanic>();
            led.PracticeHold = true;
            for (int i = 1; i < n; i++) BkAdd(Sim.Dir(360f * i / n) * 6f, RunDirector.Classes[i % RunDirector.Classes.Length]);
            bk.BeginCombat(); UltStepAlive(1);
        }
        // P1's six strikes at the plan's times (relative ticks 300 Lean I, 540 Tide + Heart, 900 Reflection, 1110 Lean V,
        // 1500 Heart), the party held where hold(i, t) says (or free when it returns null). lean1 null = the bell picks its
        // half from where the party stands (the real rule). Returns the record's launch tick.
        int LedgerRecord(Vector3? lean1, Vector3 leanV, Func<int, int, Vector3?> hold, Action<int> extra = null)
        {
            int t0 = d.World.Tick;
            bk.UltLaunch(led, new UltCue { Variant = "record" });
            var ps = d.World.Players.ToList();
            for (int t = 0; t < Sim.Ticks(40) && led.Running; t++)
            {
                if (t == 300) bk.UltMove(bk.UltLean, BellkeeperEncounter.LeanTuning(lean1));
                if (t == 540) bk.UltMove(bk.UltTide, BellkeeperEncounter.TideTuning());
                if (t == 900) bk.UltMove(bk.UltMirror, BellkeeperEncounter.MirrorTuning());
                if (t == 1110) bk.UltMove(bk.UltLean, BellkeeperEncounter.LeanTuning(leanV));
                if (t == 1500) bk.UltMove(bk.UltTide, BellkeeperEncounter.HeartTuning());
                extra?.Invoke(t);
                for (int i = 0; i < ps.Count; i++) { var at = hold(i, t); if (at.HasValue && !ps[i].Down) BkPlace(ps[i], at.Value); }
                UltStepAlive(1);
            }
            return t0;
        }
        // the clean P1: the party on the ledger half for Lean I, clumped on the safe side at Reflection's lock (images clump
        // on the hit half), both halves toward the NE station
        static Vector3? LedgerCleanHold(int i, int t)
        {
            if (t < 300) return LedDir1 * 5f + Vector3.Cross(Vector3.up, LedDir1) * (0.4f * i);
            if (t >= 900 && t <= 1140) return Sim.Dir(LedYaw1 + 180 + 50 + 8 * i) * 5.5f;
            return Sim.Dir(LedYaw1 + 180 - 50 - 8 * i) * 5.5f;
        }
        static List<string> LedgerShapes(IReadOnlyList<LedgerMechanic.Page> pages)
        {
            var o = new List<string>();
            foreach (var p in pages) if (p.Source != "Reflection of the Hour") foreach (var f in p.Prints)
                o.Add($"{p.Roman} {f.Shape} ({f.Origin.x:0.0},{f.Origin.z:0.0}) yaw {Mathf.Round(f.Yaw) % 360} r {f.Radius:0.0} in {f.Inner:0.0}");
            return o;
        }
        List<UltimateBot> LedgerBots(Func<int, Vector3> start)
        {
            var bots = new List<UltimateBot>(); var ps = d.World.Players;
            for (int i = 0; i < ps.Count; i++)
            {
                ps[i].DebugSetHp(PlayerController.MaxHp); BkPlace(ps[i], start(i));
                var b = UltimateParty.Drive(ps[i], i, ps.Count); b.Speed = 4.2f; b.Attacks = false; bots.Add(b);
            }
            return bots;
        }
        void LedgerUnbot() { foreach (var p in d.World.Players) p.Input = p == d.Player ? bot : (IInputSource)new ScriptedPlayer(); }
        // replay now (first strike in 4 s) and step until it ends; real damage. Returns the launch tick.
        int LedgerReplay(float at0 = 4f)
        {
            int t0 = d.World.Tick;
            bk.UltLaunch(led, new UltCue { Variant = "replay", At = new[] { at0 } });
            ledStillArea = LedgerStillArea();
            ledSteps = led.Upcoming.ToList();
            BkStepUntil(() => !led.Running, Sim.Ticks(at0 + 10));
            return t0;
        }
        // the gate, measured: floor area (m²) safe from V, IV and III at once (where a player frozen from V to III, as the
        // Stopped Hour does in P4 block 2, can stand), outside the boss's body
        float ledStillArea;
        List<LedgerMechanic.Step> ledSteps = new List<LedgerMechanic.Step>();
        LedgerMechanic.Step LedStep(int numeral) => ledSteps.FirstOrDefault(s => s.Page.Numeral == numeral);
        float LedgerStillArea()
        {
            var s = led.Upcoming.OrderByDescending(x => x.Page.Numeral).ToList(); if (s.Count < 4) return 0;
            int n = 0; const float g = 0.5f;
            for (float x = -13; x <= 13; x += g) for (float z = -13; z <= 13; z += g)
            {
                var q = new Vector3(x, 0, z);
                if (!CombatWorld.InPlayerBound(q, -0.3f) || q.magnitude < bk.BodyRadius + 0.5f) continue;
                if (!s[1].Hits(q) && !s[2].Hits(q) && !s[3].Hits(q)) n++;
            }
            return n * g * g;
        }
        int LedgerHits(PlayerController p = null) => bkHits.Count(h => h.Source == LedgerMechanic.ReplaySource && (p == null || h.Player == p));

        // ---------------- the record inside the real plan (P1 loop 1, solo) ----------------
        IEnumerator LedgerPlanRecordChecks()
        {
            yield return UltStart();
            if (bk == null) yield break;
            led = bk.Mechanic<LedgerMechanic>();
            bk.BeginCombat(); UltStepAlive(1);
            int t0 = UltTick0();
            var marks = new Dictionary<int, int>();   // world tick -> marks shown after it
            for (int i = 0; i < Sim.Ticks(33) && (led.Casts == 0 || led.Running); i++) { UltStepAlive(1); marks[d.World.Tick] = led.MarksShown; }
            var pages = led.Recorded;
            Check("Ledger: the plan launches \"record\" with P1 (loop 1, 0 s)", UltLaunchTick("ult_ledger", t0) == t0);
            Check("Ledger record (plan): six pages I..VI, Leaning Bell, Toll Tide, Heart Toll, images, Leaning Bell, Heart Toll",
                  pages != null && pages.Count == 6 && pages.Select(p => p.Source).SequenceEqual(LedSources) && pages.Select(p => p.Numeral).SequenceEqual(new[] { 1, 2, 3, 4, 5, 6 }),
                  pages == null ? "nothing" : string.Join(", ", pages.Select(p => p.Roman + " " + p.Source)));
            if (pages == null || pages.Count < 6) yield break;
            var rel = pages.Select(p => (p.Tick - t0) / 60f).ToArray();
            bool near = true; for (int k = 0; k < 6; k++) near &= Mathf.Abs(rel[k] - LedNominal[k]) <= 2.5f / 60f;
            Check("Ledger record (plan): each page is the move's resolve tick (8.5, 11.5, 13.5, 19, 22, 27 s, ±2 ticks)", near, string.Join(", ", rel.Select(r => r.ToString("0.000"))));
            Check("Ledger record (plan): solo images = 2 footprints on one page (mirror + own)", pages[3].Prints.Count == 2, $"{pages[3].Prints.Count}");
            Check("Ledger record (plan): the recorder is off once VI is kept", !bk.Ult.Recording && !led.IsRecording);
            int T = pages[0].Tick;
            int M(int tick) => marks.TryGetValue(tick, out int m) ? m : -1;
            Check("Ledger record: each kept footprint shows (dashed ivory outline + numeral) for 2 s, then fades",
                  M(T) >= 1 && M(T + 118) >= 1 && M(T + 122) == 0 && led.MarksShown == 0, $"{M(T)} → {M(T + 118)} → {M(T + 122)}");
            Check("Ledger record: no damage of its own (it only records)", !led.Spawned.Any());
        }

        // ---------------- a clean P1 recorded, replayed (exact ticks, exact shapes, damage rules, bots at 4.2 m/s) ----------------
        IEnumerator LedgerCleanChecks(int n, List<string> reference)
        {
            yield return LedgerStartHeld(n);
            if (bk == null) yield break;
            var clocks = bk.Mechanic<PendulumClocks>();
            LedgerRecord(LedDir1, LedDir1, LedgerCleanHold, t =>
            {
                if (t == 600) clocks.SpawnShared(new Hazard { Source = "Pendulum Clock", Shape = HazardShape.Circle, Kind = HazardKind.Strike, Origin = new Vector3(11, 0, 0), Radius = 0.5f, ResolveAt = 20, Damage = 0 });
            });
            var pages = led.Recorded;
            Check($"Ledger N={n}: records six pages in order", pages != null && pages.Count == 6 && pages.Select(p => p.Source).SequenceEqual(LedSources),
                  pages == null ? "nothing" : string.Join(", ", pages.Select(p => p.Source)));
            if (pages == null || pages.Count < 6) yield break;
            if (n == 4) Check("Ledger: the recorder hears clocks too, the Ledger keeps only its four moves",
                              bk.Ult.Recorded.Any(f => f.Source == "Pendulum Clock") && pages.All(p => p.Source != "Pendulum Clock"));
            Check($"Ledger N={n}: the images page has one footprint per player (solo: 2)", pages[3].Prints.Count == (n == 1 ? 2 : n), $"{pages[3].Prints.Count}");
            if (reference != null) Check($"Ledger N={n}: every page but the images is identical to N=4", LedgerShapes(pages).SequenceEqual(reference),
                                         string.Join(" | ", LedgerShapes(pages).Except(reference)));

            if (n == 4)
            {
                // probes: melee by the boss, ranged at 10 m, deep in the hit half, and the guide's spot
                var ps = d.World.Players;
                var spots = new[] { -LedDir1 * 2.0f, -LedDir1 * 10f, LedDir1 * 9f, Sim.Dir(LedYaw1 + 180 - 50) * 5.5f };
                for (int i = 0; i < 4; i++) { ps[i].DebugSetHp(PlayerController.MaxHp); BkPlace(ps[i], spots[i]); }
                bkHits.Clear();
                int launch = d.World.Tick;
                bk.UltLaunch(led, new UltCue { Variant = "replay", At = new[] { 4f } });
                var steps = led.Upcoming.ToList();
                bool timed = steps.Count == 6;
                for (int k = 0; k < steps.Count; k++)
                    timed &= steps[k].Page.Numeral == 6 - k && steps[k].ResolveTick == launch + Sim.Ticks(4 + 1.5f * k) && steps[k].SpawnTick == steps[k].ResolveTick - LedgerMechanic.WarnTicks;
                Check("Ledger replay: VI → I, 1.5 s apart from At[0], each coral 1.2 s before", timed,
                      string.Join(", ", steps.Select(s => $"{s.Page.Roman}@{(s.ResolveTick - launch) / 60f:0.00}")));
                Check("Ledger replay: plays this attempt's recording", !led.UsedAuthored && led.Book.SequenceEqual(pages));
                bool exact = steps.All(s => s.Probes.Count == s.Page.Prints.Count && s.Probes.Zip(s.Page.Prints, (h, f) =>
                    h.Shape == f.Shape && (h.Origin - f.Origin).sqrMagnitude < 1e-6f && Mathf.Abs(h.Yaw - f.Yaw) < 1e-3f && h.Radius == f.Radius && h.Inner == f.Inner && h.HalfAngle == f.HalfAngle && h.Damage == 60).All(x => x));
                Check("Ledger replay: each strike is the recorded footprint exactly (shape, place, size), 60 damage", exact);
                var dem = new List<BotDemand>(); led.Demands(ps[3], dem);
                Check("Ledger strat: six memory demands, known since their P1 tick, hinted along a route",
                      dem.Count == 6 && dem.All(x => x.Memory && x.From == led) && dem.Select(x => x.Since).SequenceEqual(steps.Select(s => s.Page.Tick)) && dem.All(x => x.Ok(x.Hint)),
                      string.Join(", ", dem.Select(x => $"{x.Why} since {x.Since}")));
                Check("Ledger proof: a clean P1 leaves a walking route, every step ≤ 2.5 m", led.RouteExists && led.RouteLongestHop <= LedgerMechanic.ShortHop + 0.01f, $"longest hop {led.RouteLongestHop:0.00} m");
                BkStepUntil(() => !led.Running, Sim.Ticks(14));
                int spawned = led.Spawned.Count(h => h.Source == LedgerMechanic.ReplaySource);
                Check("Ledger replay: one live hazard per recorded footprint", spawned == pages.Sum(p => p.Prints.Count), $"{spawned}");
                IEnumerable<int> TicksOn(PlayerController p) => bkHits.Where(h => h.Player == p && h.Source == LedgerMechanic.ReplaySource).Select(h => h.Tick);
                Check("Ledger: a still melee player is struck by the Heart Tolls (VI, III) on their ticks", TicksOn(ps[0]).SequenceEqual(new[] { steps[0].ResolveTick, steps[3].ResolveTick }),
                      string.Join(",", TicksOn(ps[0]).Select(t => (t - launch) / 60f)));
                Check("Ledger: a still ranged player (10 m) is struck by the tide (II) on its tick", TicksOn(ps[1]).SequenceEqual(new[] { steps[4].ResolveTick }),
                      string.Join(",", TicksOn(ps[1]).Select(t => (t - launch) / 60f)));
                Check("Ledger: deep in the hit half = struck by V then II on their ticks", TicksOn(ps[2]).SequenceEqual(new[] { steps[1].ResolveTick, steps[4].ResolveTick }),
                      string.Join(",", TicksOn(ps[2]).Select(t => (t - launch) / 60f)));
                Check("Ledger: 60 per strike; one strike never kills, the second does (no one-shot)",
                      bkHits.Where(h => h.Source == LedgerMechanic.ReplaySource).All(h => h.Damage == 60) && ps[0].Down && ps[2].Down && !ps[1].Down && ps[1].Hp == 40,
                      $"melee {(ps[0].Down ? "down" : ps[0].Hp.ToString())}, ranged {ps[1].Hp}, deep {(ps[2].Down ? "down" : ps[2].Hp.ToString())}");
                Check("Ledger: the guide's spot (safe half, band, clear of the images) takes nothing", LedgerHits(ps[3]) == 0 && !ps[3].Down);
                Check("Ledger: never a wipe or a Cracked Hour", bk.Ult.Wipes == 0 && bk.Ult.CrackedCount == 0);
                Check("Ledger replay: numerals gone once it ends", led.MarksShown == 0);
                // the probes went down: a fresh fight records the same clean P1 for the bots
                yield return LedgerStartHeld(n);
                if (bk == null) yield break;
                LedgerRecord(LedDir1, LedDir1, LedgerCleanHold);
            }

            // the intended route: perfect strat bots at 4.2 m/s with finite stamina, starting spread at 8 m (solo: the far wall of the hit half)
            bkHits.Clear();
            LedgerBots(i => n == 1 ? LedDir1 * 12f : Sim.Dir(360f * i / n + 20) * 8f);
            LedgerReplay();
            Note($"Ledger N={n} clean: {ledStillArea:0} m² safe from V, IV and III at once (a Stilled spot in P4 block 2)");
            Check($"Ledger N={n}: a clean P1 replays exactly (no running images, halves 1.5 s apart)", !led.SpreadImages && !led.SplitHalves && LedStep(1) != null
                  && LedStep(1).ResolveTick - LedStep(5).ResolveTick == Sim.Ticks(4 * LedgerMechanic.Spacing) && LedStep(4).Scale == 1f);
            Check($"Ledger N={n}: the strat bots (4.2 m/s, memory) clear a clean replay untouched", LedgerHits() == 0 && d.World.Players.All(p => !p.Down),
                  string.Join(", ", bkHits.Where(h => h.Source == LedgerMechanic.ReplaySource).Select(h => $"{h.Player.name}@{h.Tick}")));
            LedgerUnbot();
        }

        // ---------------- the GUIDE strat records a clean P1 by itself (the bell picks Lean I's half from where the party stands) ----------------
        IEnumerator LedgerStratRecordChecks()
        {
            yield return LedgerStartHeld(4);
            if (bk == null) yield break;
            LedgerBots(i => Sim.Dir(360f * i / 4 + 200) * 8f);
            LedgerRecord(null, led.LedgerHalf(), (i, t) => null);
            var pages = led.Recorded;
            if (pages == null || pages.Count < 6) { Check("Ledger strat record: six pages", false, pages == null ? "nothing" : pages.Count.ToString()); LedgerUnbot(); yield break; }
            float apart = Mathf.Abs(Mathf.DeltaAngle(pages[0].Prints[0].Yaw, pages[4].Prints[0].Yaw));
            var imgs = pages[3].Prints.Select(f => f.Origin).ToList();
            float spread = 0; foreach (var a in imgs) foreach (var b in imgs) spread = Mathf.Max(spread, Sim.Flat(a - b).magnitude);
            Check("Ledger strat: standing together on the ledger half makes both Leaning Bells strike it", apart < 30, $"{apart:0}° apart");
            Check("Ledger strat: the images clump (all within 6 m of each other)", spread <= 6f, $"{spread:0.0} m");
            bkHits.Clear();
            LedgerBots(i => Sim.Dir(360f * i / 4 + 110) * 8f);
            LedgerReplay();
            Check("Ledger strat: the bots' own P1 records a P4 they clear (some P1 play always records a solvable P4)",
                  led.RouteExists && LedgerHits() == 0 && d.World.Players.All(p => !p.Down) && !led.SpreadImages && !led.SplitHalves,
                  $"route {led.RouteExists}, hop {led.RouteLongestHop:0.0} m, {LedgerHits()} hits, spread {led.SpreadImages}, split {led.SplitHalves}");
            LedgerUnbot();
        }

        // ---------------- a messy P1 costs: opposite halves replay 0.75 s apart, scattered images run x 1.5; still survivable from memory ----------------
        IEnumerator LedgerMessyChecks()
        {
            foreach (int n in new[] { 4, 1, 8 })
            {
                yield return LedgerStartHeld(n);
                if (bk == null) yield break;
                LedgerRecord(LedDir1, -LedDir1, (i, t) => t >= 900 && t <= 1140 ? Sim.Dir(LedYaw1 + 45 + 360f / n * i) * 6f : (Vector3?)(Sim.Dir(LedYaw1 + 90 + 20 * i) * 5f));
                var pages = led.Recorded;
                bkHits.Clear();
                LedgerBots(i => Sim.Dir(360f * i / n + 20) * 8f);
                LedgerReplay();
                var v = LedStep(5); var one = LedStep(1); var img = LedStep(4);
                Note($"Ledger messy P1 N={n} (opposite halves, images scattered): route {(led.RouteExists ? $"exists, longest hop {led.RouteLongestHop:0.0} m" : "none")}; {ledStillArea:0} m² safe from V, IV and III at once");
                Check($"Ledger messy N={n}: opposite halves replay back to back (V 0.75 s before I, nothing else moves)",
                      led.SplitHalves && v != null && one != null && one.ResolveTick - v.ResolveTick == Sim.Ticks(LedgerMechanic.SplitGap)
                      && LedStep(6).ResolveTick + Sim.Ticks(5 * LedgerMechanic.Spacing) == one.ResolveTick && LedStep(3).ResolveTick - LedStep(6).ResolveTick == Sim.Ticks(3 * LedgerMechanic.Spacing),
                      v == null ? "-" : $"V {(v.ResolveTick - LedStep(6).ResolveTick) / 60f:0.00} s, I {(one.ResolveTick - LedStep(6).ResolveTick) / 60f:0.00} s after VI");
                bool spreadOk = n == 1 ? !led.SpreadImages && img.Scale == 1f
                                       : led.SpreadImages && img.Probes.Zip(pages[3].Prints, (h, f) => Mathf.Abs(h.Radius - f.Radius * LedgerMechanic.SpreadScale) < 1e-3f && (h.Origin - f.Origin).sqrMagnitude < 1e-6f).All(x => x);
                Check($"Ledger messy N={n}: {(n == 1 ? "solo images never count as scattered" : "scattered images replay where they were, radius x 1.5")}", spreadOk, $"spread {led.SpreadImages}");
                Check($"Ledger messy N={n}: still survivable from memory (a walking route exists; strat bots at 4.2 m/s take no hits)",
                      led.RouteExists && LedgerHits() == 0 && d.World.Players.All(p => !p.Down),
                      $"route {led.RouteExists}, longest hop {led.RouteLongestHop:0.0} m, hits {string.Join(", ", bkHits.Where(h => h.Source == LedgerMechanic.ReplaySource).Select(h => $"{h.Player.name}@{(h.Tick - LedStep(6).ResolveTick) / 60f + 4:0.00}"))}");
                LedgerUnbot();
            }
            float coralOnly = LedgerMechanic.WarnTicks / 60f * LedgerMechanic.WalkSpeed + PlayerController.DashDistance;
            Check("Ledger: without memory, coral alone (1.2 s at 4.2 m/s + one dash) can't cross a replayed half from its far wall",
                  CombatWorld.PlayerBound + Hazard.Margin > coralOnly + 0.5f, $"{CombatWorld.PlayerBound + Hazard.Margin:0.0} m needed, {coralOnly:0.0} m possible");
            LedgerUnbot();
        }

        // ---------------- practice from P4: the authored clean ledger inside the real P4 plan ----------------
        IEnumerator LedgerP4PracticeChecks(int n)
        {
            yield return UltStart(null, "P4");
            if (bk == null) yield break;
            led = bk.Mechanic<LedgerMechanic>();
            UltimateParty.Fill(d.World, n);
            LedgerBots(i => Sim.Dir(360f * i / n + 20) * 7f);
            bkHits.Clear();
            var where = new List<(int tick, string what)>();
            Action<PlayerController, Hazard, int> spy = (p, h, dmg) => { if (h.Source == LedgerMechanic.ReplaySource) where.Add((d.World.Tick, $"{p.name} {(led.PageOf.TryGetValue(h.Id, out int pg) ? UltimateState.Roman[pg] : "?")} at ({p.transform.position.x:0.0},{p.transform.position.z:0.0}) hp {p.Hp} · {(p.Input as UltimateBot)?.DebugLine}")); };
            d.World.PlayerHit += spy;
            bk.BeginCombat(); BkStep(1);
            int tp4 = UltTick0();
            BkStepUntil(() => led.Casts > 0, 5);
            var book = led.Book;
            var clean = led.Authored();
            Check($"Ledger P4 practice N={n}: nothing recorded = the authored clean ledger", bk.Segment == BellkeeperEncounter.UltSegment.P4 && led.UsedAuthored && book != null
                  && book.Select(p => p.Source).SequenceEqual(LedSources) && LedgerShapes(book).SequenceEqual(LedgerShapes(clean)) && book[3].Prints.Count == (n == 1 ? 2 : n));
            // block 2: the replay launches 0.5 s after the Stopped Hour; read what it held right after its launch
            BkStepUntil(() => UltLaunchTick("ult_ledger", tp4, 1) > 0, Sim.Ticks(25));
            var held = led.Held.Select(p => p.Roman + " " + p.Source).ToList();
            var stilled = bk.Mechanic<StoppedHour>().StilledPlayers.ToList();
            var clocks1 = bk.Mechanic<PendulumClocks>().Strikes.ToList();
            var clockHours = clocks1.Select(s => $"{s.Owner.name} {UltimateState.Roman[s.Hour]}→{UltimateState.StationNames[s.Station]} @{(s.Tick - tp4) / 60f:0.00}").ToList();
            BkStep(Sim.Ticks(51) - (d.World.Tick - tp4));
            d.World.PlayerHit -= spy;
            Note($"Ledger P4 N={n} block 1 clocks: {string.Join("; ", clockHours)} · ledger half NE · stilled in block 2: {string.Join(", ", stilled.Select(p => p.name + " " + UltimateState.StationNames[Math.Max(0, bk.Ult.StationOf(p))]))}");
            if (where.Count > 0) Note($"Ledger P4 N={n} hits: {string.Join("; ", where.Select(w => $"@{(w.tick - tp4) / 60f:0.00} {w.what}"))}");
            int r1 = UltLaunchTick("ult_ledger", tp4, 0), r2 = UltLaunchTick("ult_ledger", tp4, 1), r3 = UltLaunchTick("ult_ledger", tp4, 2), sh = UltLaunchTick("ult_stopped_hour", tp4, 0);
            Check($"Ledger P4 N={n}: three replays per loop at 0, 0.5 after the Stopped Hour, and with the Hand", r1 == tp4 && r2 > 0 && r3 > r2 && r2 - sh == Sim.Ticks(0.5f), $"{(r1 - tp4) / 60f:0.0}, {(r2 - tp4) / 60f:0.0}, {(r3 - tp4) / 60f:0.0} s");
            Check($"Ledger P4 N={n}: block 2's III lands on the Stopped Hour's resume (9.0 s)", r2 + Sim.Ticks(4 + 1.5f * 3) == sh + Sim.Ticks(9));
            Check($"Ledger P4 N={n}: no reflections in stopped time (block 2 holds IV, the images, and nothing else)", held.Count == 1 && held[0] == "IV Reflection of the Hour", string.Join(", ", held));
            // the one documented forced hit (replayed V, 60): block 2, a Stilled player whose station is opposite the ledger
            // half hides inside V's half. Block 1's clock pairs lock between replayed strikes (7.75 / 10.75): nothing forced.
            int opposite = (LedgerMechanic.AuthoredStation + 2) % 4;
            string Page(BkHit h) => led.PageOf.TryGetValue(h.HazardId, out int pg) ? UltimateState.Roman[pg] : "?";
            bool Forced(BkHit h) => Page(h) == "V" && h.Tick >= r2 && h.Tick <= r2 + Sim.Ticks(9) && stilled.Contains(h.Player) && bk.Ult.StationOf(h.Player) == opposite;
            var hits = bkHits.Where(h => h.Source == LedgerMechanic.ReplaySource).ToList();
            var unforced = hits.Where(h => !Forced(h)).ToList();
            Check($"Ledger P4 N={n}: strat bots at 4.2 m/s survive all three replays; the only hits are the documented forced V (block 2, Stilled, opposite station; at most one each)",
                  unforced.Count == 0 && hits.GroupBy(h => h.Player).All(g => g.Count() <= 1) && d.World.Players.All(p => !p.Down),
                  hits.Count == 0 ? "untouched" : string.Join(", ", hits.Select(h => $"{h.Player.name} {Page(h)}@{(h.Tick - tp4) / 60f:0.00}{(Forced(h) ? " forced" : "")}")));
            LedgerUnbot();
        }

        // ---------------- one page is one strike: a player inside several overlapping images is hit once ----------------
        IEnumerator LedgerPageOnceChecks()
        {
            yield return LedgerStartHeld(8);
            if (bk == null) yield break;
            var ps = d.World.Players;
            for (int i = 0; i < ps.Count; i++) { ps[i].DebugSetHp(PlayerController.MaxHp); BkPlace(ps[i], i == 0 ? LedDir1 * 5.5f : Sim.Dir(LedYaw1 + 180 - 40 + 10 * i) * 5.5f); }
            bkHits.Clear();
            bk.UltLaunch(led, led.DefaultCue());   // authored, N = 8: eight images 10° apart, overlapping
            var img = led.Upcoming.First(s => s.Page.Numeral == 4);
            int inside = img.Probes.Count(h => h.Contains(ps[0].transform.position));
            UltStepAliveUntil(() => d.World.Tick > img.ResolveTick, Sim.Ticks(10));
            int got = bkHits.Count(h => h.Player == ps[0] && led.PageOf.TryGetValue(h.HazardId, out int pg) && pg == 4);
            Check("Ledger: one page is one strike (inside several overlapping images = one 60 hit)", inside >= 2 && got == 1, $"inside {inside} images, hit {got}×");
            UltStepAliveUntil(() => !led.Running, Sim.Ticks(10));
        }

        // ---------------- boss death and halt cancel everything (outlines and numerals included) ----------------
        IEnumerator LedgerCancelChecks()
        {
            yield return LedgerStartHeld(1);
            if (bk == null) yield break;
            bk.UltLaunch(led, new UltCue { Variant = "record" });
            bk.UltMove(bk.UltLean, BellkeeperEncounter.LeanTuning(LedDir1));
            UltStepAliveUntil(() => led.MarksShown > 0, Sim.Ticks(5));
            bool shown = led.MarksShown > 0 && bk.Ult.Recording;
            bk.DebugDefeat();
            Check("Ledger: boss death mid-record drops the outlines and turns the recorder off", shown && led.MarksShown == 0 && !bk.Ult.Recording && !led.Running && d.World.Hazards.Count == 0);

            yield return LedgerStartHeld(1);
            if (bk == null) yield break;
            bk.UltLaunch(led, led.DefaultCue());
            UltStepAlive(Sim.Ticks(4.6f));
            bool live = led.MarksShown > 0 && d.World.Hazards.Live.Any(h => h.Source == LedgerMechanic.ReplaySource);
            bk.DebugDefeat();
            Check("Ledger: boss death mid-replay cancels every strike and numeral", live && led.MarksShown == 0 && d.World.Hazards.Count == 0 && led.Upcoming.Count == 0 && !led.Running);

            yield return LedgerStartHeld(1);
            if (bk == null) yield break;
            bk.UltLaunch(led, led.DefaultCue());
            UltStepAlive(Sim.Ticks(4.6f));
            bk.Halt(); d.World.ClearTransient();
            Check("Ledger: a halt (defeat, retry) drops the replay too", led.MarksShown == 0 && led.Upcoming.Count == 0 && !led.Running);
        }

        // ---------------- -lastbellAbility ult_ledger: a short P1, then its replay ----------------
        IEnumerator LedgerPracticeHookChecks()
        {
            yield return UltStart("ult_ledger");
            if (bk == null) yield break;
            led = bk.Mechanic<LedgerMechanic>();
            bk.BeginCombat();
            int t = UltStepAliveUntil(() => led.Casts >= 2, Sim.Ticks(40));
            Check("Ledger practice: records a short P1, then replays that recording", t >= 0 && led.Recorded != null && led.Recorded.Count == 6 && !led.UsedAuthored && led.Book.SequenceEqual(led.Recorded),
                  $"after {t / 60f:0.0} s, {led.Recorded?.Count ?? 0} pages");
            UltStepAliveUntil(() => !led.Running, Sim.Ticks(15));
            Check("Ledger practice: the replay strikes", led.Spawned.Any(h => h.Source == LedgerMechanic.ReplaySource));
        }
    }
}
