using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Bellkeeper Ultimate: Fracture of Time checks (ULTIMATE.md §3.8, §5.7; Fracture.cs). Owned by the Fracture session.
    // Harness: RunAutoTest.Bellkeeper.Ultimate.cs (UltStart, BkStep, UltStepAlive, bkHits, UltimateBot).
    public sealed partial class RunAutoTest
    {
        IEnumerator UltFractureChecks()
        {
            Note("---------- Ultimate: Fracture of Time ----------");
            var n = UltimateState.FissureNormal; var f = UltimateState.FissureDir;
            const string burnSrc = "Fracture of Time", flareSrc = FractureOfTime.FlareSource;
            int Burned(PlayerController p, int from, int to) => bkHits.Where(h => h.Player == p && h.Source == burnSrc && h.Tick >= from && h.Tick <= to).Sum(h => h.Damage);
            int Flares(PlayerController p) => BkHitsOn(p, flareSrc);

            // ---- the lane, the burn ticks, melee and ranged standing still ----
            yield return UltStart();
            if (bk == null) yield break;
            UltManual();   // the scheduler runs; launches below are by hand
            var fr = bk.Mechanic<FractureOfTime>();
            var melee = d.Player; BkPlace(melee, -f * 2.3f);                  // hugging the boss, SE, on the fissure
            var ranged = BkAdd(f * 10f, "Arcanist");                           // 10 m NW, on the fissure
            var edgeIn = BkAdd(f * 6f + n * 0.95f, "Chimesinger");             // just inside the burn's edge (1.0 m)
            var beside = BkAdd(-f * 6f + n * 1.2f);                            // beside the burn, inside the flare (1.5 m)
            var clear = BkAdd(f * 4f - n * 1.7f);                              // clear of both
            var far = BkAdd(-f * 12.2f);                                       // the SE end, at the wall
            bk.UltLaunch(fr, new UltCue { Phase = 5 });
            int T = fr.LastCastTick;
            var burn = fr.Burn;
            Check("Fracture: built (no longer the scaffold stub) and running from its launch", fr.Built && fr.Running && burn != null && fr.Spawned.Contains(burn));
            Check("Fracture: the lane is the fissure: 1.4 m wide, NW <-> SE through the centre, across the whole floor, Ground, 30/s",
                  burn != null && burn.Shape == HazardShape.Lane && Mathf.Approximately(burn.Width, 1.4f) && burn.Kind == HazardKind.Ground && Mathf.Approximately(burn.BurnPerSecond, 30)
                  && new[] { 0f, 3.2f, 6f, 12.3f, 12.9f }.All(r => burn.Contains(f * r) && burn.Contains(-f * r)) && !burn.Contains(n * 1.05f) && !burn.Contains(-n * 1.05f)
                  && Mathf.Abs(Vector3.Dot(Sim.Dir(burn.Yaw), f)) > 0.999f,
                  burn == null ? "none" : $"w {burn.Width} len {burn.Length:0.0} yaw {burn.Yaw:0}");
            BkStep(OpenTicksPlus(0) - 1);
            Check("Fracture: it cracks open for 1.0 s before it burns (no damage, the glow not yet lit)", bkHits.Count == 0 && fr.Glow == null, string.Join(", ", bkHits.Select(h => h.Source)));
            BkStep(121);
            int t1 = T + FractureOfTime.OpenTicks;
            Check("Fracture: burns exactly 30 per second (melee on it, hugging the boss)", Burned(melee, t1, t1 + 59) == 30 && Burned(melee, t1, t1 + 119) == 60, $"{Burned(melee, t1, t1 + 59)} in 1 s, {Burned(melee, t1, t1 + 119)} in 2 s");
            Check("Fracture: burns exactly 30 per second (ranged on it, 10 m out; at the wall)", Burned(ranged, t1, t1 + 59) == 30 && Burned(ranged, t1, t1 + 119) == 60 && Burned(far, t1, t1 + 119) == 60,
                  $"{Burned(ranged, t1, t1 + 119)}, wall {Burned(far, t1, t1 + 119)}");
            Check("Fracture: the burn's edge is the 1.4 m lane (+ body margin): 0.95 m off burns, 1.2 m off doesn't", Burned(edgeIn, t1, t1 + 119) == 60 && Burned(beside, 0, int.MaxValue) == 0 && Burned(clear, 0, int.MaxValue) == 0);
            Check("Fracture: the glow is lit once it burns (amber, a live hazard)", fr.Glow != null);
            foreach (var p in d.World.Players) p.DebugSetHp(PlayerController.MaxHp);
            // flares: one per toll of the great bell (6 s .. 72 s), 60 each, the whole length; never at XIII or after
            // standing still: each flare's knockback is undone so every player stays on its spot
            var still = d.World.Players.Select(p => (p, at: Sim.Flat(p.transform.position))).ToList();
            while (d.World.Tick - T < Sim.Ticks(FractureOfTime.FlareEnrage + 6f)) { UltStepAlive(1); foreach (var (p, at) in still) BkPlace(p, at); }
            var flareTicks = bkHits.Where(h => h.Player == melee && h.Source == flareSrc).Select(h => h.Tick - T).ToList();
            var wantFlares = Enumerable.Range(1, 12).Select(k => Sim.Ticks(6f * k)).ToList();
            Check("Fracture flare: on every toll, 6 s apart from P5 start (6 ... 72 s), none at XIII (78 s) or after", flareTicks.SequenceEqual(wantFlares),
                  string.Join(" ", flareTicks.Select(t => (t / 60f).ToString("0.0"))));
            Check("Fracture flare: 60 to still melee, still ranged, at the wall and beside the burn (2.4 m lane); nothing to a player 1.7 m off",
                  new[] { melee, ranged, edgeIn, beside, far }.All(p => Flares(p) == 12 && bkHits.Where(h => h.Player == p && h.Source == flareSrc).All(h => h.Damage == 60)) && bkHits.All(h => h.Player != clear),
                  $"melee {Flares(melee)} ranged {Flares(ranged)} beside {Flares(beside)} clear {bkHits.Count(h => h.Player == clear)}");
            var flare = fr.Spawned.FirstOrDefault(h => h.Source == flareSrc);
            Check("Fracture flare: Ground, coral for 1.2 s before it strikes", flare != null && flare.Kind == HazardKind.Ground && flare.ResolveAt == UltShapes.ResolveIn(FractureOfTime.FlareWarnTicks) && Mathf.Approximately(flare.Width, 2.4f));
            Check("Fracture: no wipe and no one-shot of its own (78 s, six players)", bk.Ult.Wipes == 0 && bkHits.All(h => h.Damage <= 60) && d.World.Players.All(p => !p.Down));

            // ---- the perfect bot never waits in it ----
            yield return UltStart();
            if (bk == null) yield break;
            UltManual();   // the scheduler runs; launches below are by hand
            fr = bk.Mechanic<FractureOfTime>();
            BkPlace(d.Player, f * 6f);
            var sb = UltimateParty.Drive(d.Player, 0, 1); sb.Speed = 4.2f; sb.Attacks = false;
            bk.UltLaunch(fr, new UltCue { Phase = 5 });
            BkStep(Sim.Ticks(20));
            Check("Fracture: the strat bot steps off before it burns and is never caught by a flare (4.2 m/s, 20 s)", Burned(d.Player, 0, int.MaxValue) == 0 && Flares(d.Player) == 0 && !d.Player.Down,
                  $"burn {Burned(d.Player, 0, int.MaxValue)}, flares {Flares(d.Player)} · {sb.DebugLine}");
            d.Player.Input = bot;

            // ---- the intended route: cross on a P5 Leaning Bell (both P5 loops' timings), N = 1/2/4/8, 4.2 m/s, finite stamina ----
            foreach (var (count, leanAt, dash, speed) in new[] { (1, 28f, true, 4.2f), (1, 63f, true, 4.2f), (1, 28f, false, 4.2f), (2, 28f, true, 4.2f), (4, 63f, true, 4.2f), (4, 63f, true, 6f), (8, 28f, true, 4.2f), (8, 63f, true, 4.2f) })
                yield return UltFractureCross(count, leanAt, dash, speed);
            // walking only, loop-2 timing, from a resume spot (7 m out): still works
            yield return UltFractureCross(1, 63f, false, 4.2f, 7f);

            // ---- Stilled with a NW or SE station (the fissure's own ends): hide beside it, not on it ----
            yield return UltStart();
            if (bk == null) yield break;
            UltManual();   // the scheduler runs; launches below are by hand
            fr = bk.Mechanic<FractureOfTime>();
            var owner = bk.Mechanic<StoppedHour>();
            var hiders = new List<(PlayerController p, int st, bool beside)>();
            foreach (int st in new[] { 1, 3 })
            {
                hiders.Add((BkAdd(n * 6), st, true)); hiders.Add((BkAdd(n * 7), st, true)); hiders.Add((BkAdd(n * 8), st, false));
            }
            BkPlace(d.Player, -n * 6);
            bk.UltLaunch(fr, new UltCue { Phase = 5 });
            T = fr.LastCastTick;
            UltStepAliveUntil(() => d.World.Tick >= T + Sim.Ticks(23f) - 1, Sim.Ticks(24));
            int k2 = 0;
            foreach (var (p, st, bes) in hiders)
            {
                var behind = -UltimateState.StationDir(st) * 7f;
                BkPlace(p, bes ? behind + n * ((k2++ % 2 == 0) ? 1.75f : -1.75f) : behind);
                p.DebugSetHp(PlayerController.MaxHp);
                bk.Ult.SetClock(p, 0, st, -1);
                bk.Ult.Still(p, d.World.Tick + 1, 240, 300);
            }
            var dem = new List<BotDemand>();
            bool demOk = hiders.All(h => { dem.Clear(); fr.Demands(h.p, dem); var r = dem.FirstOrDefault(x => x.Why.StartsWith("Stilled")); return r.Ok != null && r.Ok(h.p.transform.position) == h.beside && r.Ok(r.Hint); });
            Check("Fracture Demands: a Stilled player's spot must be off the fissure's flare; the hint is a hiding spot beside it", demOk);
            bkHits.Clear();
            foreach (var (p, st, _) in hiders)
                bk.Ult.FirstBodyLane(owner, "Chain probe", UltimateState.Station(st), p.transform.position, 1.6f, UltShapes.ResolveIn(240), _ => UltimateState.Lethal);
            BkStep(245);
            bool flareInStop = bkHits.Any(h => h.Source == flareSrc) || fr.FlareTicks.Any(t => t > T + Sim.Ticks(23f) && t < T + Sim.Ticks(27f));
            Check("Stilled NW/SE: hiding 1.75 m beside the fissure, 7 m behind the boss: the boss's body stops the chain, no burn, no flare (a real route)",
                  flareInStop && hiders.Where(h => h.beside).All(h => !h.p.Down && BkHitsOn(h.p) == 0),
                  string.Join(", ", hiders.Where(h => h.beside).Select(h => $"{UltimateState.StationNames[h.st]}:{BkHitsOn(h.p)} {string.Join("/", bkHits.Where(x => x.Player == h.p).Select(x => x.Source).Distinct())}")));
            Check("Stilled NW/SE: on the fissure's axis (the plain hiding strip) the burn and the toll's flare cost 90+ of 100 HP (the conflict the fix avoids)",
                  hiders.Where(h => !h.beside).All(h => Burned(h.p, 0, int.MaxValue) >= 30 && Flares(h.p) == 1 && bkHits.Where(x => x.Player == h.p && x.Source != "Chain probe").Sum(x => x.Damage) >= 90), string.Join(", ", hiders.Where(h => !h.beside).Select(h => $"{Burned(h.p, 0, int.MaxValue)} burn, {Flares(h.p)} flare")));

            // ---- boss death cancels everything: the lane, the flares and the glow ----
            yield return UltStart();
            if (bk == null) yield break;
            UltManual();   // the scheduler runs; launches below are by hand
            fr = bk.Mechanic<FractureOfTime>();
            BkPlace(d.Player, f * 5f);
            bk.UltLaunch(fr, new UltCue { Phase = 5 });
            UltStepAlive(Sim.Ticks(5.5f));                                    // the first flare is drawn (resolves at 6 s)
            var glowT = fr.Glow; int live = d.World.Hazards.Live.Count(h => h.Source == flareSrc || h.Source == burnSrc);
            bk.DebugDefeat();
            bkHits.Clear();
            BkStep(Sim.Ticks(8));
            Check("Fracture: boss death cancels the lane, the pending flare and the glow, and nothing comes back",
                  live == 2 && glowT != null && glowT.flash >= 0 && fr.Glow == null && fr.Burn == null && !fr.Running && d.World.Hazards.Count == 0 && bkHits.Count == 0,
                  $"live {live}, glow fading {glowT != null && glowT.flash >= 0}, hits {bkHits.Count}");

            // ---- practice: -lastbellAbility ult_fracture, and the plan from P5 ----
            yield return UltStart(UltimateMechanics.Fracture);
            if (bk == null) yield break;
            fr = bk.Mechanic<FractureOfTime>();
            BkPlace(d.Player, -n * 6);
            bk.ManualPlan = false; bk.BeginCombat(); UltStepAlive(2);
            int p0 = fr.LastCastTick;
            UltStepAlive(Sim.Ticks(10.2f));
            var pl = bk.UltLean;
            Check("practice -lastbellAbility ult_fracture: the fissure burns, and a Leaning Bell across it at 10 s with the toll 2 s into its warning",
                  fr.Running && fr.Burn != null && pl.Casts == 1 && pl.LastCastTick - p0 == Sim.Ticks(10) && Mathf.Abs(Vector3.Dot(pl.LeanDir, n)) > 0.99f && fr.FlareTicks.Contains(p0 + Sim.Ticks(12)),
                  $"lean {pl.Casts} at {(pl.LastCastTick - p0) / 60f:0.00}s");

            yield return UltStart(null, "P5");
            if (bk == null) yield break;
            BellkeeperEncounter.PushTimersOn = false;
            fr = bk.Mechanic<FractureOfTime>();
            sb = UltimateParty.Drive(d.Player, 0, 1); sb.Speed = 4.2f; sb.Attacks = false;
            bk.ManualPlan = false; bk.BeginCombat(); BkStep(3);
            int s0 = UltTick0();
            Check("practice from P5: the fissure is lit at P5 start", bk.Segment == BellkeeperEncounter.UltSegment.P5 && fr.Running && fr.Burn != null && fr.LastCastTick == s0, $"{bk.Segment}");
            bkHits.Clear();
            BkStep(Sim.Ticks(32.2f) - (d.World.Tick - s0));
            int lt = UltLaunchTick("leaning_bell", s0);
            Check("P5 plan: the Leaning Bell at 28 s leans across the fissure, with a toll's flare inside its warning",
                  lt - s0 == Sim.Ticks(28) && Mathf.Abs(Vector3.Dot(bk.UltLean.LeanDir, n)) > 0.99f && fr.FlareTicks.Any(t => t > lt && t < lt + UltShapes.LeanTicks),
                  $"lean at {(lt - s0) / 60f:0.00}s");
            Check("P5 plan: the solo strat bot crosses (4.2 m/s): no Leaning Bell, no flare, little burn",
                  !d.Player.Down && BkHitsOn(d.Player, "Leaning Bell") == 0 && Flares(d.Player) == 0 && Burned(d.Player, 0, int.MaxValue) <= 14,
                  $"burn {Burned(d.Player, 0, int.MaxValue)} · {sb.DebugLine}");
            d.Player.Input = bot;
            BellkeeperEncounter.PushTimersOn = true;
        }
        static int OpenTicksPlus(int extra) => FractureOfTime.OpenTicks + extra;

        // One crossing: the fissure lit at P5 start, players placed on (mostly) the hit side at the Leaning Bell's launch
        // (leanAt s after P5 start, as the plan's loops: 28 and 63), driven by the strat bot. The bell falls on the majority.
        IEnumerator UltFractureCross(int count, float leanAt, bool dash, float speed, float startOut = -1)
        {
            yield return UltStart();
            if (bk == null) yield break;
            UltManual();   // the scheduler runs; launches below are by hand
            var n = UltimateState.FissureNormal; var f = UltimateState.FissureDir;
            var fr = bk.Mechanic<FractureOfTime>();
            // (along the normal, along the fissure): the far wall first (the worst start), two on the safe side at N = 8
            var spots = new List<Vector2> { new Vector2(12f, 0), new Vector2(6, 6), new Vector2(2.2f, -3), new Vector2(4, -9), new Vector2(9, -5), new Vector2(1.6f, 4), new Vector2(-5, 3), new Vector2(-9, -4) };
            if (startOut > 0) spots[0] = new Vector2(startOut, 0);
            var ps = new List<PlayerController> { d.Player };
            for (int i = 1; i < count; i++) ps.Add(BkAdd(n * 6 + f * i, RunDirector.Classes[i % RunDirector.Classes.Length]));
            for (int i = 0; i < count; i++) BkPlace(ps[i], n * 6 - f * 3 + f * i * 0.8f);   // parked off the fissure until the bell
            bk.UltLaunch(fr, new UltCue { Phase = 5 });
            int T = fr.LastCastTick;
            UltStepAliveUntil(() => d.World.Tick >= T + Sim.Ticks(leanAt), Sim.Ticks(leanAt + 1));
            var bots = new List<UltimateBot>();
            for (int i = 0; i < count; i++)
            {
                BkPlace(ps[i], n * spots[i].x + f * spots[i].y); ps[i].DebugSetHp(PlayerController.MaxHp);
                var b = UltimateParty.Drive(ps[i], i, count); b.Speed = speed; b.Attacks = false; b.UseDash = dash; bots.Add(b);
            }
            bkHits.Clear();
            int from = d.World.Tick;
            bk.UltMove(bk.UltLean, BellkeeperEncounter.LeanTuning(FractureOfTime.MajorityNormal(d.World)));
            var lean = bk.UltLean;
            BkStep(UltShapes.LeanTicks + 6);
            int toll = fr.FlareTicks.FirstOrDefault(t => t > from && t < from + UltShapes.LeanTicks);
            var burns = ps.Select(p => bkHits.Where(h => h.Player == p && h.Source == "Fracture of Time").Sum(h => h.Damage)).ToList();
            bool ok = toll > 0 && ps.All(p => !p.Down && BkHitsOn(p, "Leaning Bell") == 0 && BkHitsOn(p, FractureOfTime.FlareSource) == 0)
                      && burns.All(b => b <= 20) && (count > 1 || !dash || leanAt < 60 || burns[0] <= 6) && ps.All(p => Vector3.Dot(Sim.Flat(p.transform.position), lean.LeanDir) < 0);
            string label = $"N = {count}, bell at {leanAt:0} s (toll {(toll - from) / 60f:0.0} s in), {speed:0.0} m/s{(dash ? "" : ", walking only")}{(startOut > 0 ? $", from {startOut:0} m" : ", far wall start")}";
            Check("Fracture crossing: " + label + ": everyone reaches the safe half with no Leaning Bell, no flare, burn <= 20 (solo dash, loop 2: <= 6)", ok,
                  $"burn {string.Join("/", burns)} · hits {string.Join(", ", bkHits.Where(h => h.Source != "Fracture of Time").Select(h => h.Source).Distinct())} · {bots[0].DebugLine}");
            d.Player.Input = bot;
        }
    }
}
