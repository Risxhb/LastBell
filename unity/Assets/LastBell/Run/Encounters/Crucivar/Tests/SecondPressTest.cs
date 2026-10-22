using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // 05 Second Press (brief 05-crucivar.second_press.md). Hands Second Press a Mould Press result built from where the
    // scripted players actually stand (so it doesn't depend on Mould Press's own build), and checks the pick rules, the
    // ticks, damage per difficulty, the inward knockback, the escape at 4.2 and 6 m/s, melee and ranged soakers, solo and
    // N = 2 / 4 / 8, one hit per instance, boss death cancelling it, and that only Hard (or the practice hook) runs it.
    public sealed class SecondPressTest : CrucivarAbilityTest
    {
        public override string Id => "crucivar.second_press";
        public override int Order => 50;   // after Mould Press's own test

        const string Src = "Second Press";
        static readonly string[] Numeral = { "?", "I", "II", "III", "IV" };

        static MouldResult Result(int[] bays, int[] counts, bool sequential = false, int[] numerals = null) => new MouldResult
        {
            Centres = bays.Select(CrucivarAbility.Bay).ToArray(), Bays = bays, Counts = counts, Sequential = sequential,
            Numerals = numerals ?? Enumerable.Range(1, bays.Length).ToArray(),
        };
        // the counts Mould Press would report: players inside each tower (the press's own reach) when it resolves
        static int[] CountAt(CrucivarV2AutoTest t, int[] bays, IEnumerable<PlayerController> who) =>
            bays.Select(b => who.Count(p => Sim.Flat(p.transform.position - CrucivarAbility.Bay(b)).magnitude <= SecondPress.Radius + Hazard.Margin)).ToArray();
        static Vector3 Along(int bay, float fromCentre) => CrucivarAbility.Bay(bay).normalized * fromCentre;
        static float R(PlayerController p) => Sim.Flat(p.transform.position).magnitude;
        static int Expected(Difficulty d) => Mathf.RoundToInt(SecondPress.Damage * RunDifficulty.DamageTakenScale(d));

        public override IEnumerator Run(CrucivarV2AutoTest t)
        {
            // ---- pick rules (no world needed) ----
            t.Check("pick: the mould with the most players", SecondPress.Pick(Result(new[] { 0, 1, 2, 3 }, new[] { 1, 3, 2, 2 })) == 1);
            t.Check("pick: a tie goes to the lowest numeral", SecondPress.Pick(Result(new[] { 0, 2 }, new[] { 1, 1 }, numerals: new[] { 2, 1 })) == 1);
            t.Check("pick: same numeral, then NE > SE > SW > NW", SecondPress.Pick(Result(new[] { 3, 1, 2 }, new[] { 1, 1, 1 }, numerals: new[] { 1, 1, 1 })) == 1);
            t.Check("pick: solo presses the last tower (II)", SecondPress.Pick(Result(new[] { 0, 2 }, new[] { 1, 1 }, sequential: true)) == 1);
            t.Check("pick: nothing resolved, nothing pressed", SecondPress.Pick(Result(new int[0], new int[0])) == -1 && SecondPress.Pick(null) == -1);

            // ---- solo on Hard: the still player in tower II is pressed, on the right ticks, and pushed inward ----
            yield return t.Setup(1, Difficulty.Hard, new[] { CrucivarAbility.Bay(2) });
            if (t.Boss == null) yield break;
            var press = t.Boss.Press; var main = t.Main;
            float r0 = R(main);
            t.Begin(press.Run(Result(new[] { 0, 2 }, new[] { 0, 1 }, sequential: true)));
            yield return t.Until(() => t.Spawned(Src).Any(), 6, "Second Press telegraph");
            yield return t.Seconds(0.8f);
            yield return t.Shot("second_press_telegraph");
            yield return t.WaitIdle();
            var sp = t.Spawned(Src).FirstOrDefault(); var hit = t.HitsOn(main, Src).FirstOrDefault();
            t.Check("one press, on tower II's mould (SW)", t.Spawned(Src).Count() == 1 && sp != null && Sim.Flat(sp.H.Origin - CrucivarAbility.Bay(2)).magnitude < 0.01f);
            t.Check("shape: circle r 2.2, Strike, locked at 0", sp != null && sp.H.Shape == HazardShape.Circle && Mathf.Approximately(sp.H.Radius, 2.2f) && sp.H.Kind == HazardKind.Strike && sp.H.LockAt == 0);
            int delay = sp != null ? sp.Tick - press.StartTick : -1;
            t.Check("telegraph appears 2.0 s after the towers resolve", Mathf.Abs(delay - Sim.Ticks(2.0f)) <= 1, $"{delay} ticks");
            int warn = sp != null && hit != null ? hit.Tick - sp.Tick : -1;
            t.Check("it lands 2.0 s after the telegraph", Mathf.Abs(warn - Sim.Ticks(2.0f)) <= 1, $"{warn} ticks");
            t.Check("standing still in the mould is hit, Hard x1.3", hit != null && hit.Damage == Expected(Difficulty.Hard), hit != null ? $"{hit.Damage} (expected {Expected(Difficulty.Hard)})" : "not hit");
            yield return t.Seconds(0.4f);
            t.Check("knockback 1.5 m toward the arena centre", Mathf.Abs((r0 - R(main)) - SecondPress.Knock) < 0.35f, $"{r0:0.00} -> {R(main):0.00} m");
            t.Check("one hit per instance", t.OneHitPerInstance());

            // ---- the escape: leave the re-stamped mould from its centre once it appears (2.5 m), no dash. The walk aims
            // 3.0 m out: the scripted walker stops within one step (0.1 m at 6 m/s) of its target, so aiming at the edge
            // itself could leave it on the border ----
            foreach (float speed in new[] { 4.2f, 6f })
            {
                yield return t.Setup(1, Difficulty.Hard, new[] { CrucivarAbility.Bay(2) });
                main = t.Main;
                t.Begin(press.Run(Result(new[] { 2 }, new[] { 1 })));
                yield return t.Until(() => t.Spawned(Src).Any(), 6, "Second Press telegraph");
                int from = t.W.Tick;
                t.In(main).Go(Along(2, BayInward(3.0f)), speed);
                yield return t.Until(() => Sim.Flat(main.transform.position - CrucivarAbility.Bay(2)).magnitude > SecondPress.Radius + Hazard.Margin, 3, "out of the press");
                float took = (t.W.Tick - from) / (float)Sim.Hz;
                yield return t.WaitIdle();
                t.In(main).Stop();
                t.Check($"escape at {speed} m/s from the mould centre", !t.WasHit(main, Src), $"out after {took:0.00} s of 2.0 s, ended {Sim.Flat(main.transform.position - CrucivarAbility.Bay(2)).magnitude:0.00} m from the mould centre, stamina {main.Stamina:0}/{PlayerController.MaxStamina}");
            }

            // ---- melee and ranged: one soaker at the mould's inner edge (next to the boss), one at the outer edge (8 m+) ----
            // (a third player idles at the arena edge, r 13, outside every mould: the press is a small circle on one bay, so
            // it threatens whoever soaked, melee or ranged, and never the rest of the arena)
            yield return t.Setup(3, Difficulty.Hard, new[] { Along(1, 6f - 2.3f), Along(1, 6f + 2.3f), Along(3, 13f) });
            var melee = t.Party[0]; var ranged = t.Party[1]; var edge = t.Party[2];
            float rr0 = R(ranged);
            yield return t.Fire(press.Run(Result(new[] { 1 }, CountAt(t, new[] { 1 }, t.Party))));
            t.Note($"melee soaker {Sim.Flat(melee.transform.position - t.Boss.transform.position).magnitude:0.0} m from the boss, ranged soaker {rr0:0.0} m from the centre");
            t.Check("melee soaker standing still is hit", t.HitsOn(melee, Src).Count() == 1);
            t.Check("ranged soaker standing still is hit", t.HitsOn(ranged, Src).Count() == 1);
            t.Check("a player at the arena edge outside the moulds is not pressed", !t.WasHit(edge, Src), $"r {R(edge):0.0} m");
            yield return t.Seconds(0.4f);
            t.Check("the outer soaker is pushed inward, not toward the rim", R(ranged) < rr0 - 1f, $"{rr0:0.00} -> {R(ranged):0.00} m");

            // ---- N = 2 / 4 / 8: the fullest mould is pressed; only its soakers are hit ----
            var layouts = new (int n, int[] bays, int[][] who, int expectBay)[]
            {
                (2, new[] { 0, 2 }, new[] { new[] { 0 }, new[] { 1 } }, 0),                                           // 1-1 tie: tower I (NE)
                (4, new[] { 0, 1, 2, 3 }, new[] { new[] { 0 }, new[] { 1, 2 }, new int[0], new[] { 3 } }, 1),             // SE holds 2
                (8, new[] { 0, 1, 2, 3 }, new[] { new[] { 0, 1 }, new[] { 2, 3 }, new[] { 4, 5, 6 }, new[] { 7 } }, 2),   // SW holds 3
            };
            foreach (var (n, bays, who, expectBay) in layouts)
            {
                var at = new Vector3[n];
                for (int tower = 0; tower < who.Length; tower++)
                    for (int k = 0; k < who[tower].Length; k++)
                        at[who[tower][k]] = CrucivarAbility.Bay(bays[tower]) + Quaternion.Euler(0, 360f * k / Mathf.Max(1, who[tower].Length), 0) * Vector3.right * (who[tower].Length > 1 ? 0.7f : 0);
                yield return t.Setup(n, Difficulty.Hard, at);
                var counts = CountAt(t, bays, t.Party);
                yield return t.Fire(press.Run(Result(bays, counts)));
                var h = t.Spawned(Src).FirstOrDefault()?.H;
                int pressedTower = System.Array.IndexOf(bays, expectBay);
                var inside = who[pressedTower].Select(i => t.Party[i]).ToList();
                t.Check($"N={n}: presses the fullest mould ({CrucivarAbility.BayNames[expectBay]}, counts {string.Join("/", counts)})",
                    h != null && Sim.Flat(h.Origin - CrucivarAbility.Bay(expectBay)).magnitude < 0.01f);
                t.Check($"N={n}: its {inside.Count} soakers are hit once, nobody else", inside.All(p => t.HitsOn(p, Src).Count() == 1) && t.Party.Except(inside).All(p => !t.WasHit(p, Src)));
                t.Check($"N={n}: one hit per instance", t.OneHitPerInstance());
            }

            // ---- phase 2: the slag edge (from 9.5 m) still burns when the press lands; the push never reaches it ----
            yield return t.Setup(1, Difficulty.Hard, new[] { Along(3, 8.4f) });
            main = t.Main;
            t.Boss.Bellows.SlagRim();
            if (!t.W.Hazards.Live.Any(h => h.BurnPerSecond > 0 && h.Shape == HazardShape.Ring))
                t.W.Hazards.Spawn(new Hazard { Source = "Slag edge (test stand-in)", Shape = HazardShape.Ring, Kind = HazardKind.Ground, Inner = 9.5f, Radius = CrucivarAbility.Rim,
                    LockAt = 0, ResolveAt = Sim.Ticks(7f), BurnPerSecond = 6f });
            float re0 = R(main);
            yield return t.Fire(press.Run(Result(new[] { 3 }, new[] { 1 })));
            yield return t.Seconds(0.5f);
            t.Check("outer-edge soaker is hit by the press", t.WasHit(main, Src));
            t.Check("pushed toward the centre, never into the burning slag edge", R(main) < re0 && R(main) < 9.5f - Hazard.Margin && !t.HitsOn(main).Any(h => h.H.BurnPerSecond > 0),
                $"{re0:0.00} -> {R(main):0.00} m (edge from 9.5 m)");

            // ---- difficulty: damage and warning per level (via the hook path), and the gate in the phase plan ----
            foreach (var d in new[] { Difficulty.Easy, Difficulty.Medium })
            {
                yield return t.Setup(1, d, new[] { CrucivarAbility.Bay(0) });
                yield return t.Fire(press.Run(Result(new[] { 0 }, new[] { 1 })));
                var s = t.Spawned(Src).FirstOrDefault(); var hh = t.HitsOn(t.Main, Src).FirstOrDefault();
                int w = s != null && hh != null ? hh.Tick - s.Tick : -1;
                t.Check($"{d} (practice hook): {Expected(d)} damage, warning {(d == Difficulty.Easy ? 2.5f : 2f)} s",
                    hh != null && hh.Damage == Expected(d) && Mathf.Abs(w - Sim.Ticks(d == Difficulty.Easy ? 2.5f : 2f)) <= 1, hh != null ? $"{hh.Damage} dmg, {w} ticks" : "not hit");
            }
            foreach (var (d, force) in new[] { (Difficulty.Easy, false), (Difficulty.Medium, false), (Difficulty.Hard, false), (Difficulty.Medium, true) })
            {
                yield return t.Setup(1, d);
                int runs = press.Runs;
                t.Begin(t.Boss.MouldWithPress(force));
                while (t.Boss != null && t.Boss.TestBusy && t.D.State == RunState.Combat)
                {
                    // soak a live mould if Mould Press raised one (an empty one wipes), and stay healthy
                    var mould = t.W.Hazards.Live.Where(h => h.ShareTotal > 0 && h.WipeIfEmpty).OrderBy(h => h.ResolveAt - h.Age).FirstOrDefault();
                    if (mould != null) t.In(t.Main).Go(mould.Origin, 6f); else t.In(t.Main).Stop();
                    if (t.Main.Hp < 60) t.Main.DebugSetHp(PlayerController.MaxHp);
                    yield return null;
                }
                t.In(t.Main).Stop();
                bool ran = press.Runs > runs;
                bool want = force || d == Difficulty.Hard;
                t.Check(force ? "practice hook (-lastbellAbility crucivar.second_press) fires it on Medium" : $"{d}: Mould Press {(want ? "is" : "is not")} followed by Second Press",
                    ran == want, $"ran {ran}, towers {t.Boss.Mould.Last?.Centres?.Length ?? 0}");
            }

            // ---- boss death cancels it: during the 2.0 s lift, and mid-telegraph ----
            foreach (bool midTelegraph in new[] { false, true })
            {
                yield return t.Setup(1, Difficulty.Hard, new[] { CrucivarAbility.Bay(1) });
                t.Begin(t.Boss.Press.Run(Result(new[] { 1 }, new[] { 1 })));
                if (midTelegraph) { yield return t.Until(() => t.Spawned(Src).Any(), 6, "Second Press telegraph"); yield return t.Seconds(0.8f); }
                else yield return t.Seconds(1.0f);
                t.KillBoss();
                yield return t.Seconds(3.0f);
                t.Check($"boss death {(midTelegraph ? "mid-telegraph" : "during the lift")} cancels the press",
                    !t.WasHit(t.Main, Src) && !t.W.Hazards.Live.Any(h => h.Source == Src) && (midTelegraph || !t.Spawned(Src).Any()));
            }
        }

        static float BayInward(float step) => CrucivarAbility.BayRadius - step;
    }
}
