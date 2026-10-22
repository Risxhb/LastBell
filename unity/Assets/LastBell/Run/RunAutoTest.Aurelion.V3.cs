using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Aurelion ability set v3, Milestone A (abilities-v3/HARDER-PROMPT.md, ABILITIES-v3.md; proposal):
    //   Ecliptic Order and Zenith Marks checks, the Step 0 control run (reflex bot on v2.1 Hard), the bot proof (reflex bot
    //   on v3 Hard with Medium windows), the perfect route through every v3 block, decision density and telemetry.
    //   -lastbellAurelionV3Only    with -lastbellAurelionOnly: only this section
    // Called from AurelionV2Checks (after the v2 sections) or alone. The long bot runs play at 4x game speed.
    public sealed partial class RunAutoTest
    {
        static bool AurelionV3Only => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellAurelionV3Only", StringComparison.OrdinalIgnoreCase));
        const float V3Ranged = 8.5f;   // the bots hold a ranged player's gap (Chzy18 plays solo ranged)

        IEnumerator AurelionV3Checks()
        {
            Note("---------- Aurelion ability set v3, Milestone A (proposal) ----------");
            AurelionArgs.SoloOverride = null; AurelionArgs.WindowsMediumOverride = false;
            AurelionArgs.SetOverride = "v3";
            if (V2Wants("aurelion.ecliptic_order")) yield return V2Section("v3 Ecliptic Order", V3Ecliptic());
            if (V2Wants("aurelion.zenith_marks")) yield return V2Section("v3 Zenith Marks", V3Marks());
            if (V2Wants("aurelion.v3_proof")) yield return V2Section("v3 bot proof and density", V3Proof());
            Time.timeScale = 1;
            AurelionArgs.SetOverride = null; AurelionArgs.WindowsMediumOverride = false;
        }

        // ---- helpers ----
        void V3Bot(PlayerController p, BotMode mode) => p.Input = new AllyBot(0, 1, mode) { HoldRange = V3Ranged };
        void V3Unbot() { foreach (var p in V2Party) if (p && v2Inputs.TryGetValue(p, out var i)) p.Input = i; }
        // runs a body with every standing party member topped up each frame (an ability check, not a survival check)
        IEnumerator V3FireToppedUp(IEnumerable<int> body, float timeout = 30)
        {
            V2Begin(body); float t = 0;
            while (V2Boss != null && V2Boss.TestBusy && t < timeout)
            {
                foreach (var p in V2Party) if (p && !p.Down) p.DebugSetHp(PlayerController.MaxHp);
                t += Time.deltaTime; yield return null;
            }
        }

        // ================================================================ Ecliptic Order
        IEnumerator V3Ecliptic()
        {
            // 1: Medium, solo, the perfect (memory) route: anchors, numerals hidden after 2.5 s, late coral strikes, no hits
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, 9f) }, 1, Vector3.zero);
            if (V2Boss == null) yield break;
            var ec = V2Boss.Ecliptic;
            V3Bot(V2Main, BotMode.Perfect);
            int dur = ec.Duration(3);
            yield return V2Fire(ec.Run(3), 20);
            var strikes = V2Spawned("Ecliptic Order").ToList();
            V2Check("3 strikes: ring from the safe disc (3.0 m solo) past the rim, Ground, 50, no knockback, locked at once",
                strikes.Count == 3 && strikes.All(s => s.H.Shape == HazardShape.Ring && Mathf.Approximately(s.H.Inner, 3.0f) && s.H.Radius >= 28 && s.H.Kind == HazardKind.Ground
                    && s.H.Damage == EclipticOrder.Damage && s.H.Knockback == 0 && s.H.LockAt == 0),
                string.Join("; ", strikes.Select(s => $"{s.H.Shape} {s.H.Inner}-{s.H.Radius} {s.H.Damage}")));
            V2Check("each strike shows only 0.35 s (21 ticks) before it lands, on its anchor, at 3.2 / 5.4 / 7.6 s",
                strikes.Count == 3 && Enumerable.Range(0, 3).All(k => strikes[k].H.ResolveAt == Sim.Ticks(EclipticOrder.LeadS) && strikes[k].Tick + strikes[k].H.ResolveAt - 1 == ec.StrikeAt(k)
                    && Sim.Flat(strikes[k].H.Origin - ec.Anchors[k]).magnitude < 0.01f && ec.StrikeAt(k) - ec.RevealTick == Sim.Ticks(3.2f + 2.2f * k) - 1),
                string.Join(", ", strikes.Select(s => $"spawn +{s.Tick - ec.RevealTick} resolve {s.H.ResolveAt}")));
            V2Check("the numerals and rims hide 2.5 s after the reveal, before strike I appears (memory, not reflex)",
                ec.HideTick - ec.RevealTick == Sim.Ticks(EclipticOrder.RevealS) && strikes.Count > 0 && ec.HideTick < strikes[0].Tick, $"hidden at +{ec.HideTick - ec.RevealTick}");
            float minGap = Enumerable.Range(1, ec.Anchors.Count - 1).Min(k => Sim.Flat(ec.Anchors[k] - ec.Anchors[k - 1]).magnitude);
            V2Check("consecutive anchors are at least 8.6 m apart (120 degrees) (no reflex hop from one disc to the next)", minGap >= 8.6f, $"closest pair {minGap:0.0} m");
            V2Check("Duration() equals the last strike's resolve offset", ec.StrikeAt(2) - ec.RevealTick == dur, $"{ec.StrikeAt(2) - ec.RevealTick} vs {dur}");
            V2Check("the perfect route (walk to I, II, III in order) is never hit", !V2WasHit(V2Main), string.Join(", ", V2HitsOn(V2Main).Select(h => h.Source)));
            var order1 = ec.Anchors.ToList();
            V3Unbot();

            // 2: still at the centre (in no disc): every strike hits, once each
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, 2.5f) }, 1, Vector3.zero);
            if (V2Boss == null) yield break;
            yield return V3FireToppedUp(V2Boss.Ecliptic.Run(3), 20);
            V2Check("standing still off the anchors is hit by every strike, for 50 each", V2HitsOn(V2Main, "Ecliptic").Count() == 3 && V2HitsOn(V2Main, "Ecliptic").All(h => h.Damage == 50),
                string.Join(", ", V2HitsOn(V2Main).Select(h => h.Source + " " + h.Damage)));
            V2Check("the next cast uses another authored order", !V2Boss.Ecliptic.Anchors.SequenceEqual(order1));
            V2Check("one hit per instance", V2OneHitPerInstance());

            // 3: hit recovery: hit by strike I at the worst moment (standing off its disc), the route to II and III still works
            yield return V2Setup(1, Difficulty.Hard, new[] { new Vector3(0, 0, 2.5f) }, 1, Vector3.zero);
            if (V2Boss == null) yield break;
            ec = V2Boss.Ecliptic; int c3 = ec.Casts;
            V2Begin(ec.Run(3));
            yield return Until(() => ec.Casts > c3 && V2W.Tick > ec.StrikeAt(0), 15, "Ecliptic strike I");
            V3Bot(V2Main, BotMode.Perfect);
            yield return V2WaitIdle(15);
            V2Check("Hard hit recovery: hit by strike I (65), the perfect route still makes II and III", V2HitsOn(V2Main, "Ecliptic").Count() == 1 && !V2Main.Down,
                string.Join(", ", V2HitsOn(V2Main).Select(h => h.Source + " " + h.Damage)));
            V2Check("Hard keeps Medium timings (the difficulty is the order, not the window)", ec.StrikeAt(1) - ec.RevealTick == Sim.Ticks(5.4f) - 1);
            V3Unbot();

            // 4: Easy: the numerals stay lit, 1.0 s coral lead, 1.25x timings
            yield return V2Setup(1, Difficulty.Easy, new[] { new Vector3(0, 0, 9f) }, 1, Vector3.zero);
            if (V2Boss == null) yield break;
            ec = V2Boss.Ecliptic; int c4 = ec.Casts;
            V3Bot(V2Main, BotMode.Perfect);
            V2Begin(ec.Run(3));
            yield return Until(() => ec.Casts > c4 && V2W.Tick > ec.StrikeAt(1), 20, "Easy strike II");
            bool lit = ec.CueVisible;
            yield return V2WaitIdle(15);
            var easy = V2Spawned("Ecliptic Order").ToList();
            V2Check("Easy: numerals stay lit through the strikes, each strike shows 1.0 s ahead, the first lands at 4.0 s",
                lit && easy.All(s => s.H.ResolveAt == Sim.Ticks(1.0f)) && ec.StrikeAt(0) - ec.RevealTick == Sim.Ticks(4.0f) - 1, $"lit {lit}, first +{ec.StrikeAt(0) - ec.RevealTick}");
            V2Check("Easy: the perfect route is never hit", !V2WasHit(V2Main));
            V3Unbot();

            // 5: memory matters: the memory-blind reflex bot, which dashes the tick a shape appears, is still hit (Hard)
            yield return V2Setup(1, Difficulty.Hard, new[] { new Vector3(0, 0, 9f) }, 1, Vector3.zero);
            if (V2Boss == null) yield break;
            V3Bot(V2Main, BotMode.Reflex);
            yield return V3FireToppedUp(V2Boss.Ecliptic.Run(4), 20);
            int blind = V2HitsOn(V2Main, "Ecliptic").Count();
            V2Check("memory matters: the memory-blind reflex bot is hit by at least 3 of 4 strikes on Hard", blind >= 3, blind + " of 4");
            V3Unbot();
        }

        // ================================================================ Zenith Marks
        IEnumerator V3Marks()
        {
            // 1: solo alternation, the perfect route through Marks + Lances (Phase 1), twice: SHADE then SUN
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, 9f) }, 1, Vector3.zero);
            if (V2Boss == null) yield break;
            var mk = V2Boss.Marks;
            V3Bot(V2Main, BotMode.Perfect);
            var marks = new List<ZenithMarks.Mark>(); var verdictOk = new List<bool>(); var sameTick = new List<string>();
            for (int c = 0; c < 2; c++)
            {
                int casts = mk.Casts;
                V2Begin(V2Boss.MarksWithLances(1));
                yield return Until(() => mk.Casts > casts && mk.Marks.Count > 0, 10, "Zenith Marks cast");
                marks.Add(mk.Marks[0].Mark);
                yield return Until(() => !V2Boss.TestBusy || V2Boss.Current == "open chest", 15, "Marks resolved");
                var v = mk.Marks[0].Verdict;
                verdictOk.Add(v != null && v.ResolveAt == Sim.Ticks(ZenithMarks.LeadS) && v.Affects != null && v.Damage == 50 && v.Kind == HazardKind.Ground && v.Knockback == 0
                    && (mk.Marks[0].Mark == ZenithMarks.Mark.Shade ? v.Shape == HazardShape.Ring && Mathf.Approximately(v.Inner, 5.5f) : v.Shape == HazardShape.Circle && Mathf.Approximately(v.Radius, 8.5f)));
                var lanes = V2Spawned("Meridian Lances").Where(s => s.Tick >= mk.MarkTick - 300).ToList();
                int lastLane = lanes.Count > 0 ? lanes.Max(s => s.Tick + s.H.ResolveAt - 1) : -1;
                sameTick.Add($"marks {mk.ResolveTick} lances {lastLane}");
                yield return V2WaitIdle(10);
            }
            V2Check("solo marks alternate SHADE / SUN", marks.Count == 2 && marks[0] != marks[1], string.Join(", ", marks));
            V2Check("each verdict: 0.35 s coral lead, only its own player, Ground 50, no knockback; SHADE = ring 5.5 m out, SUN = disc 8.5 m",
                verdictOk.Count == 2 && verdictOk.All(x => x));
            V2Check("the marks resolve on the same tick as the Lances' last volley (one pattern pair, never a third)", sameTick.All(s => { var p = s.Split(' '); return p[1] == p[3]; }), string.Join("; ", sameTick));
            V2Check("the perfect route keeps its mark's side and dodges the Lances: never hit", !V2WasHit(V2Main), string.Join(", ", V2HitsOn(V2Main).Select(h => h.Source)));
            V2Check("Duration() matches the mark's 3.0 s", mk.ResolveTick - mk.MarkTick == mk.Duration() && mk.Duration() == Sim.Ticks(3f) - 1);
            V3Unbot();

            // 1b: Misalignment's two-step read (Hard): SHADE > SUN (or the reverse), one step per Lances volley
            yield return V2Setup(1, Difficulty.Hard, new[] { new Vector3(0, 0, 9f) }, 2, Vector3.zero);
            if (V2Boss == null) yield break;
            mk = V2Boss.Marks;
            V3Bot(V2Main, BotMode.Perfect);
            yield return V2Fire(V2Boss.MarksWithLances(2), 25);
            var steps = mk.Marks.Where(m => m.Who == V2Main).OrderBy(m => m.Step).ToList();
            var volleys = V2Spawned("Meridian Lances").Where(s => s.Tick >= mk.MarkTick).Select(s => s.Tick + s.H.ResolveAt - 1).Distinct().OrderBy(t => t).ToList();
            V2Check("Misalignment: two steps, the second the flip of the first, resolving on volley 1 and volley 2",
                steps.Count == 2 && steps[0].Mark != steps[1].Mark && volleys.Count == 2 && steps[0].ResolveTick == volleys[0] && steps[1].ResolveTick == volleys[1]
                && steps.All(s => s.Verdict != null && V2Spawns.Any(x => x.H == s.Verdict && x.Tick + x.H.ResolveAt - 1 == s.ResolveTick)),
                $"steps {string.Join(" > ", steps.Select(s => s.Mark + "@" + s.ResolveTick))}, volleys {string.Join("/", volleys)}");
            V2Check("Misalignment: the perfect route makes both steps and dodges both volleys", !V2WasHit(V2Main), string.Join(", ", V2HitsOn(V2Main).Select(h => h.Source)));
            V3Unbot();

            // 2: standing still 6 m out (between the shade and the sun): both marks hit
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, 6f) }, 1, Vector3.zero);
            if (V2Boss == null) yield break;
            yield return V3FireToppedUp(V2Boss.Marks.Run(), 10);
            yield return V3FireToppedUp(V2Boss.Marks.Run(), 10);
            V2Check("standing still 6 m out is hit by both SHADE and SUN", V2HitsOn(V2Main, "Zenith Marks").Count() == 2, V2HitsOn(V2Main, "Zenith Marks").Count() + " hits");

            // 3: N = 2: one of each, and a verdict hits only its own player
            yield return V2Setup(2, Difficulty.Medium, new[] { new Vector3(0, 0, 6f), new Vector3(0, 0, -6f) }, 1, Vector3.zero);
            if (V2Boss == null) yield break;
            yield return V3FireToppedUp(V2Boss.Marks.Run(), 10);
            mk = V2Boss.Marks;
            V2Check("N = 2: one SHADE and one SUN", mk.Marks.Count == 2 && mk.Marks[0].Mark != mk.Marks[1].Mark);
            V2Check("N = 2: both standing between the zones are hit once each, each by their own verdict",
                V2Party.All(p => V2HitsOn(p, "Zenith Marks").Count() == 1 && V2HitsOn(p, "Zenith Marks").All(h => mk.Marks.First(m => m.Who == p).Verdict == h.H)));

            // 4: Easy: 4.0 s marks, 1.0 s coral
            yield return V2Setup(1, Difficulty.Easy, new[] { new Vector3(0, 0, 9f) }, 1, Vector3.zero);
            if (V2Boss == null) yield break;
            yield return V3FireToppedUp(V2Boss.Marks.Run(), 10);
            var ev = V2Boss.Marks.Marks[0].Verdict;
            V2Check("Easy: the mark shows 4.0 s ahead and its verdict 1.0 s ahead", V2Boss.Marks.Duration() == Sim.Ticks(4f) - 1 && ev != null && ev.ResolveAt == Sim.Ticks(1f));

            // 5: memory matters: the memory-blind reflex bot is hit by the SHADE mark from its ranged spot (Hard)
            yield return V2Setup(1, Difficulty.Hard, new[] { new Vector3(0, 0, 9.8f) }, 1, Vector3.zero);
            if (V2Boss == null) yield break;
            if (ZenithMarks.SoloMark(V2Boss.Marks.Casts) != ZenithMarks.Mark.Shade) yield return V3FireToppedUp(V2Boss.Marks.Run(), 10);
            V2Hits.Clear();
            V3Bot(V2Main, BotMode.Reflex);
            yield return V3FireToppedUp(V2Boss.Marks.Run(), 10);
            V2Check("memory matters: the memory-blind reflex bot at range is hit by SHADE on Hard (0.35 s is too late to run in)", V2WasHit(V2Main, "Zenith Marks"));
            V3Unbot();
        }

        // ================================================================ bot proof, perfect route, decision density
        // Decision (HARDER-PROMPT target 1): a resolution where the route's position at the previous resolution is inside
        // the resolving shape, and the nearest point outside it is >= 3 m away or the straight line there crosses another
        // live hazard. Counted once per resolve tick, on the tracked player.
        int ddDecisions, ddResolutions, ddLastTick = -1; Vector3 ddPrev, ddHere; bool ddCounted; PlayerController ddWho; CombatWorld ddHooked;
        void DdReset(PlayerController who)
        {
            ddDecisions = 0; ddResolutions = 0; ddLastTick = -1; ddWho = who; ddPrev = ddHere = who.transform.position;
            if (ddHooked != V2W) { ddHooked = V2W; V2W.Hazards.Resolving += DdResolving; }
        }
        void DdResolving(Hazard h)
        {
            if (ddWho == null || !ddWho || ddWho.Down || !h.HitOnResolve || h.BurnPerSecond > 0 || (h.Affects != null && !h.Affects(ddWho))) return;
            int t = V2W.Tick;
            if (t != ddLastTick) { ddPrev = ddLastTick < 0 ? ddWho.transform.position : ddHere; ddHere = ddWho.transform.position; ddLastTick = t; ddCounted = false; ddResolutions++; }
            if (ddCounted || !h.Contains(ddPrev)) return;
            float best = 99; Vector3 to = ddPrev;
            for (float r = 0.2f; r <= 28 && best > 90; r += 0.2f)
                for (int k = 0; k < 48; k++)
                {
                    var q = ddPrev + Sim.Dir(k * 7.5f) * r;
                    if (!CombatWorld.InPlayerBound(q, -0.2f) || h.Contains(q)) continue;
                    best = r; to = q; break;
                }
            bool crosses = false;
            for (float s = 0.05f; s <= 1 && !crosses; s += 0.05f)
            {
                var q = Vector3.Lerp(ddPrev, to, s);
                foreach (var o in V2W.Hazards.Live) if (o != h && o.Age < o.ResolveAt && (o.Affects == null || o.Affects(ddWho)) && o.Contains(q)) { crosses = true; break; }
            }
            if (best >= 3 || crosses) { ddDecisions++; ddCounted = true; }
        }

        // One "loop" = one pass of each phase's plan (Perfect Order, Misalignment, Exposed Meridian), no transitions. Stops
        // early when the tracked player goes down. Returns per-loop hits through `hitsPerLoop`.
        IEnumerator V3Loops(int loops, List<int> hitsPerLoop, List<float> secondsPerLoop, List<int> punishTicks)
        {
            for (int l = 0; l < loops; l++)
            {
                int before = V2HitsOn(V2Main).Count(), t0 = V2W.Tick, p0 = V2Boss.Punishes;
                for (int ph = 1; ph <= 3; ph++)
                {
                    if (V2Main.Down || d.State != RunState.Combat) break;
                    V2Boss.DebugSetPhase(ph);
                    V2Begin(V2Boss.TestPhasePlan(ph));
                    yield return Until(() => V2Boss == null || !V2Boss.TestBusy || V2Main.Down || d.State != RunState.Combat, 200, $"phase {ph} plan");
                }
                hitsPerLoop.Add(V2HitsOn(V2Main).Count() - before);
                secondsPerLoop.Add((V2W.Tick - t0) / (float)Sim.Hz);
                punishTicks.Add((V2Boss.Punishes - p0) * Sim.Ticks(AurelionEncounter.PunishS));
                if (V2Main.Down || d.State != RunState.Combat) yield break;
            }
        }

        string V3HitList() => string.Join(", ", V2HitsOn(V2Main).Select(h => $"{h.Source} {h.Damage}"));
        void V3Telemetry(string label) { if (V2Boss && V2Boss.Telemetry) foreach (var s in V2Boss.Telemetry.Summary()) Note($"telemetry [{label}] {s}"); }

        IEnumerator V3Proof()
        {
            Time.timeScale = 4;
            var hits = new List<int>(); var secs = new List<float>(); var pun = new List<int>();

            // ---- Step 0 control: memory-blind reflex bot, v2.1 Hard (real Hard windows), 3 loops ----
            AurelionArgs.SetOverride = "v2"; AurelionArgs.WindowsMediumOverride = false;
            yield return V2Setup(1, Difficulty.Hard, new[] { new Vector3(0, 0, 9f) }, 1, Vector3.zero);
            if (V2Boss == null) yield break;
            V2Boss.Telemetry?.Clear();
            V3Bot(V2Main, BotMode.Reflex);
            yield return V3Loops(3, hits, secs, pun);
            Note($"control (v2.1 Hard, reflex bot): hits per loop {string.Join("/", hits)}, loop {secs.FirstOrDefault():0} s, potions {V2Main.PotionsDrunk}, down {V2Main.Down}; hits: {V3HitList()}");
            V3Telemetry("control v2.1 Hard");
            V2Check("Step 0 control: the reflex-perfect, memory-blind bot takes < 2 hits per loop on v2.1 Hard and survives 3 loops",
                hits.Count == 3 && hits.All(h => h < 2) && !V2Main.Down, $"hits {string.Join("/", hits)}, down {V2Main.Down}");
            V3Unbot();

            // ---- v2.1 Hard perfect route: decision density baseline ----
            hits.Clear(); secs.Clear(); pun.Clear();
            yield return V2Setup(1, Difficulty.Hard, new[] { new Vector3(0, 0, 9f) }, 1, Vector3.zero);
            if (V2Boss == null) yield break;
            V3Bot(V2Main, BotMode.Perfect); DdReset(V2Main);
            yield return V3Loops(1, hits, secs, pun);
            float active21 = secs.Sum() - pun.Sum() / (float)Sim.Hz;
            float dens21 = ddDecisions / Mathf.Max(1, active21) * 3f;
            Note($"decision density v2.1 Hard (perfect route): {ddDecisions} decisions in {active21:0.0} s outside the punish = {dens21:0.00} per 3 s ({ddResolutions} resolutions)");
            V3Unbot();

            // ---- v3 Hard perfect route: survives every block; decision density ----
            AurelionArgs.SetOverride = "v3";
            hits.Clear(); secs.Clear(); pun.Clear();
            yield return V2Setup(1, Difficulty.Hard, new[] { new Vector3(0, 0, 9f) }, 1, Vector3.zero);
            if (V2Boss == null) yield break;
            V2Boss.Telemetry?.Clear();
            V3Bot(V2Main, BotMode.Perfect); DdReset(V2Main);
            yield return V3Loops(1, hits, secs, pun);
            float active3 = secs.Sum() - pun.Sum() / (float)Sim.Hz;
            float dens3 = ddDecisions / Mathf.Max(1, active3) * 3f;
            Note($"decision density v3 Hard (perfect route): {ddDecisions} decisions in {active3:0.0} s outside the punish = {dens3:0.00} per 3 s ({ddResolutions} resolutions); v2.1 {dens21:0.00}");
            V3Telemetry("v3 Hard perfect route");
            V2Check("fairness: the perfect route survives every v3 Hard block (one loop, all three phases) and takes no Ecliptic / Marks hit",
                !V2Main.Down && hits.Count == 1 && !V2WasHit(V2Main, "Ecliptic") && !V2WasHit(V2Main, "Zenith Marks"), $"down {V2Main.Down}, hits: {V3HitList()}");
            // target 1's absolute bar (>= 1 per 3 s) is the full plan's job (Milestone B); the interim loop only replaces two slots
            Note($"decision density target >= 1 per 3 s: {(dens3 >= 1f ? "met" : "NOT MET on the interim loop (Milestone B, full plan)")} ({dens3:0.00})");
            Note($"decision density target >= 1.5x v2.1: {(dens3 >= 1.5f * dens21 ? "met" : "NOT MET on the interim loop (Milestone B, full plan)")} ({dens3:0.00} vs {1.5f * dens21:0.00})");
            V2Check("decision density: v3 is denser than v2.1", dens3 > dens21, $"{dens3:0.00} vs v2.1 {dens21:0.00}");
            V3Unbot();

            // ---- the bot proof: memory-blind reflex bot on v3 Hard with Medium windows goes down within 2 loops ----
            AurelionArgs.WindowsMediumOverride = true;
            var memory = new[] { "Ecliptic Order", "Zenith Marks" };
            hits.Clear(); secs.Clear(); pun.Clear();
            yield return V2Setup(1, Difficulty.Hard, new[] { new Vector3(0, 0, 9f) }, 1, Vector3.zero);
            if (V2Boss == null) yield break;
            V2Boss.Telemetry?.Clear();
            V3Bot(V2Main, BotMode.Reflex);
            yield return V3Loops(2, hits, secs, pun);
            var all = V2HitsOn(V2Main).ToList();
            Note($"bot proof (v3 Hard, Medium windows, reflex bot): down {V2Main.Down}, hits per loop {string.Join("/", hits)}, potions {V2Main.PotionsDrunk}; hits: {V3HitList()}");
            V3Telemetry("v3 Hard proof");
            V2Check("bot proof: the memory-blind reflex bot goes down within 2 loops of v3 Hard (Medium windows)", V2Main.Down, $"hits {string.Join("/", hits)}");
            V2Check("bot proof: every kill-tier hit on it is a memory mechanic, none from the Hard-only Hour Hands or a soak",
                all.Count > 0 && all.Where(h => h.H.Damage >= 50).All(h => memory.Contains(h.Source)) && !all.Any(h => h.Source.StartsWith("Hour Hands")), V3HitList());
            V3Unbot();

            // ---- the same blind bot kept alive (topped up) for 2 full loops: which mechanics land, and the damage mix ----
            hits.Clear(); secs.Clear(); pun.Clear();
            yield return V2Setup(1, Difficulty.Hard, new[] { new Vector3(0, 0, 9f) }, 1, Vector3.zero);
            if (V2Boss == null) yield break;
            V2Boss.Telemetry?.Clear();
            V3Bot(V2Main, BotMode.Reflex);
            v3TopUp = true; StartCoroutine(V3TopUpLoop());
            yield return V3Loops(2, hits, secs, pun);
            v3TopUp = false;
            all = V2HitsOn(V2Main).ToList();
            int kill = all.Count(h => h.H.Damage >= 50);
            float meanHard = all.Count > 0 ? (float)all.Average(h => h.Damage) : 0;
            var landed = all.Select(h => h.Source).Where(s => memory.Contains(s)).Distinct().ToList();
            Note($"blind bot kept alive, 2 loops: hits per loop {string.Join("/", hits)}; " + string.Join(", ", all.GroupBy(h => h.Source).Select(g => $"{g.Key} {g.Count()}")));
            Note($"damage mix: mean Hard hit {meanHard:0.0} => {(meanHard > 0 ? 300f / meanHard : 0):0.0} mistakes per fight (300 effective HP); kill-tier share {(all.Count > 0 ? 100f * kill / all.Count : 0):0} % of {all.Count} hits");
            V3Telemetry("v3 Hard blind, kept alive");
            V2Check("bot proof: both memory mechanics (Ecliptic Order, Zenith Marks) land on the blind bot over 2 loops, none from Hour Hands",
                landed.Count >= 2 && !all.Any(h => h.Source.StartsWith("Hour Hands")), string.Join(", ", landed));
            V2Check("lethality: <= 5 mistakes per fight (mean Hard hit >= 60) and >= 60 % of the blind bot's hits kill-tier",
                meanHard >= 60 && all.Count > 0 && kill >= 0.6f * all.Count, $"mean {meanHard:0.0}, kill {kill}/{all.Count}");
            V3Unbot();
            AurelionArgs.WindowsMediumOverride = false; AurelionArgs.SetOverride = "v3";
            Time.timeScale = 1;
        }

        bool v3TopUp;
        IEnumerator V3TopUpLoop() { while (v3TopUp) { foreach (var p in V2Party) if (p && !p.Down) p.DebugSetHp(PlayerController.MaxHp); yield return null; } }
    }
}
