using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // 01 Mould Press acceptance (brief 01-crucivar.mould_press.md, BUILD-PLAN Part C §3): spawn/resolve ticks, the solo
    // sequence, N = 2 / 4 / 8 splits with scripted players, the bad-split worst case at 4.2 m/s + dash, missing shares and
    // over-soaking, still players (melee and ranged edge) wiped by empty moulds, downed players not counted, one hit per
    // instance, Easy/Hard, and boss death mid-windup cancelling everything.
    public sealed class MouldPressTest : CrucivarAbilityTest
    {
        public override string Id => "crucivar.mould_press";

        const string Src = "Mould Press";
        MouldResult resolved; MouldPress hooked;

        void Hook(CrucivarV2AutoTest t)
        {
            if (hooked == t.Boss.Mould) return;
            hooked = t.Boss.Mould; hooked.Resolved += r => resolved = r;
        }

        // starts a cast and returns its towers (in spawn order) once spawned
        IEnumerator Cast(CrucivarV2AutoTest t, List<Hazard> towers, List<int> spawnTicks)
        {
            Hook(t); resolved = null;
            t.Begin(t.Boss.Mould.Run());
            yield return t.Ticks(1);
            if (towers.Count == 0) t.Check("display name while it runs", t.Boss.Current == "Mould Press", t.Boss.Current);
            yield return t.Until(() => t.Spawned(Src).Any(), 5, "moulds spawned");
            yield return t.Ticks(1);
            foreach (var s in t.Spawned(Src)) { towers.Add(s.H); spawnTicks.Add(s.Tick); }
        }

        static Vector3 Rim(Vector3 dir, float r = 13.4f) => Sim.Flat(dir).normalized * r;
        static bool Opposite(Hazard a, Hazard b) => Sim.Flat(a.Origin + b.Origin).magnitude < 0.1f;
        static float Secs(int ticks) => ticks / (float)Sim.Hz;

        public override IEnumerator Run(CrucivarV2AutoTest t)
        {
            // ---------------- table and shape ----------------
            var table = new Dictionary<int, int[]> { [1] = new[] { 1, 1 }, [2] = new[] { 1, 1 }, [3] = new[] { 2, 1 }, [4] = new[] { 2, 2 },
                [5] = new[] { 2, 1, 1, 1 }, [6] = new[] { 2, 2, 1, 1 }, [7] = new[] { 2, 2, 2, 1 }, [8] = new[] { 2, 2, 2, 2 } };
            t.Check("numerals per player count match the brief (N = 1..8)", table.All(kv => MouldPress.Numerals(kv.Key).SequenceEqual(kv.Value)));
            t.Check("shares cover every player (sum of numerals = N for N >= 2)", Enumerable.Range(2, 7).All(n => MouldPress.Numerals(n).Sum() == n));

            // ---------------- solo, Medium: I then II on adjacent bays, escape from the far side of I at 4.2 m/s ----------------
            yield return t.Setup(1, Difficulty.Medium, at: new[] { new Vector3(0, 0, 6) });
            var boss = t.Boss; if (boss == null) yield break;
            var p = t.Main;
            var towers = new List<Hazard>(); var ticks = new List<int>();
            yield return Cast(t, towers, ticks);
            t.Check("solo: two moulds, I and II", towers.Count == 2 && towers[0].Source == "Mould Press I" && towers[1].Source == "Mould Press II", string.Join(", ", towers.Select(h => h.Source)));
            if (towers.Count != 2) yield break;
            var I = towers[0]; var II = towers[1];
            t.Check("shape: circle r 2.0, Ground, on die bays at r 6.0", towers.All(h => h.Shape == HazardShape.Circle && Mathf.Approximately(h.Radius, 2f) && h.Kind == HazardKind.Ground
                && Mathf.Abs(Sim.Flat(h.Origin).magnitude - 6f) < 0.01f && Mathf.Abs(Mathf.Abs(h.Origin.x) - Mathf.Abs(h.Origin.z)) < 0.01f));
            t.Check("shared soak: 20 per share, empty wipes, no knockback, 0.25 s stun", towers.All(h => h.ShareTotal == 20 * h.ShareCount && h.WipeIfEmpty && h.Knockback == 0 && h.Stun == 15));
            t.Check("solo: adjacent bays (8.49 m apart)", Mathf.Abs(Vector3.Distance(I.Origin, II.Origin) - 8.485f) < 0.05f, $"{Vector3.Distance(I.Origin, II.Origin):0.00} m");
            t.Check("warning 4.2 s, locked from tick 0", I.ResolveAt == Sim.Ticks(4.2f) && I.LockAt == 0, $"{Secs(I.ResolveAt):0.00} s");
            t.Check("solo: II resolves 2.6 s after I", II.ResolveAt - I.ResolveAt == Sim.Ticks(2.6f), $"{Secs(II.ResolveAt - I.ResolveAt):0.00} s");
            // stand on the far side of I from II, step in, then cross to II after I resolves
            var farSideOfI = I.Origin + Sim.Flat(I.Origin - II.Origin).normalized * 2f;
            t.Place(p, farSideOfI); t.In(p).Stop();
            yield return t.Until(() => !t.W.Hazards.Live.Contains(I), 8, "tower I resolved");
            int iTick = t.W.Tick;
            t.In(p).Go(II.Origin, 4.2f);
            yield return t.WaitIdle(10);
            t.In(p).Stop();
            var hI = t.HitsOn(p).Where(h => h.Source == "Mould Press I").ToList(); var hII = t.HitsOn(p).Where(h => h.Source == "Mould Press II").ToList();
            t.Check("solo: I hits once for 20 at its resolve tick", hI.Count == 1 && hI[0].Damage == 20 && Mathf.Abs(hI[0].Tick - ticks[0] - Sim.Ticks(4.2f)) <= 1,
                hI.Count == 1 ? $"{hI[0].Damage} dmg, {Secs(hI[0].Tick - ticks[0]):0.00} s after spawn" : $"{hI.Count} hits");
            t.Check("solo escape I -> II at 4.2 m/s (no dash): II hits once for 20, still standing", hII.Count == 1 && hII[0].Damage == 20 && !p.Down,
                hII.Count == 1 ? $"{Secs(hII[0].Tick - iTick):0.00} s after I" : $"{hII.Count} hits, down {p.Down}");
            t.Check("solo: Resolved raised once with both towers, sequential, 1 inside each", resolved != null && resolved.Sequential && resolved.Centres.Length == 2
                && resolved.Counts.SequenceEqual(new[] { 1, 1 }) && resolved.Numerals.SequenceEqual(new[] { 1, 1 }) && resolved.Centres[1] == II.Origin);
            t.Check("the ability returned after the last tower", !boss.TestBusy);
            t.Check("no Mould Press FX left behind", Object.FindObjectsByType<MouldPressFx>(FindObjectsSortMode.None).Length == 0);

            // ---------------- N = 2: one each; pairs alternate between casts ----------------
            yield return t.Setup(2, Difficulty.Medium, at: new[] { Rim(new Vector3(-1, 0, 1)), Rim(new Vector3(1, 0, -1)) });
            var first = new List<Hazard>(); ticks.Clear();
            yield return Cast(t, first, ticks);
            t.Check("N=2: two moulds numbered 1, 1 on an opposite pair", first.Count == 2 && first.All(h => h.ShareCount == 1) && Opposite(first[0], first[1]));
            // each walks from the rim to the bay on the far side of the arena (the brief's 12.6 m "nearer bay" case is easier)
            for (int i = 0; i < 2; i++) { t.Party[i].AbilitiesEnabled = true; t.Party[i].transform.position = Rim(-first[i].Origin); t.In(t.Party[i]).Go(first[i].Origin, 4.2f); t.In(t.Party[i]).Dash(); }
            yield return t.WaitIdle(10);
            t.Check("N=2: each takes one 20 share, nobody down", t.Party.All(q => t.HitsOn(q, Src).Select(h => h.Damage).SequenceEqual(new[] { 20 }) && !q.Down),
                string.Join(" | ", t.Party.Select(q => string.Join(",", t.HitsOn(q, Src).Select(h => h.Damage)))));
            t.Check("N=2: Resolved counts 1, 1, not sequential", resolved != null && !resolved.Sequential && resolved.Counts.SequenceEqual(new[] { 1, 1 }));
            var second = new List<Hazard>(); ticks.Clear(); t.Hits.Clear(); t.Spawns.Clear();
            foreach (var q in t.Party) t.In(q).Stop();
            yield return Cast(t, second, ticks);
            for (int i = 0; i < 2 && second.Count == 2; i++) t.In(t.Party[i]).Go(second[i].Origin, 6f);
            yield return t.WaitIdle(10);
            t.Check("pairs alternate: the next cast uses the other diagonal", second.Count == 2 && first.Count == 2 && Opposite(second[0], second[1])
                && Mathf.Abs(Vector3.Dot(Sim.Flat(first[0].Origin).normalized, Sim.Flat(second[0].Origin).normalized)) < 0.01f);
            foreach (var q in t.Party) t.In(q).Stop();

            // ---------------- N = 4, bad split worst case: rim -> far bay of the pair, 17.4 m at 4.2 m/s + one dash ----------------
            yield return t.Setup(4, Difficulty.Medium);
            towers = new List<Hazard>(); ticks.Clear();
            yield return Cast(t, towers, ticks);
            t.Check("N=4: two moulds numbered 2, 2", towers.Count == 2 && towers.All(h => h.ShareCount == 2));
            if (towers.Count == 2)
            {
                // players 0, 1 -> tower 0 and 2, 3 -> tower 1, all starting on the rim behind the OTHER tower
                for (int i = 0; i < 4; i++)
                {
                    var q = t.Party[i]; var target = towers[i / 2];
                    var start = Rim(-target.Origin) + Vector3.Cross(Vector3.up, target.Origin.normalized) * (i % 2 == 0 ? 0.5f : -0.5f);
                    q.transform.position = start;
                    q.AbilitiesEnabled = true;   // dash needs it; the scripted input never presses an ability key
                    t.In(q).Go(target.Origin + (start - target.Origin).normalized * 1.2f, 4.2f); t.In(q).Dash();
                }
                yield return t.Ticks(6);
                t.Check("N=4 bad split: every player dashed (stamina spent)", t.Party.All(q => q.Stamina <= PlayerController.MaxStamina - PlayerController.DashCost + 1), string.Join(",", t.Party.Select(q => q.Stamina.ToString("0"))));
                yield return t.Seconds(2.9f);
                yield return t.Shot("mould_press_n4_bad_split");
                yield return t.WaitIdle(10);
                float staminaMin = t.Party.Min(q => q.Stamina);
                t.Check("N=4 bad split (17.4 m in 4.2 s at 4.2 m/s + dash): everyone made it, 20 each", t.Party.All(q => t.HitsOn(q, Src).Select(h => h.Damage).SequenceEqual(new[] { 20 }) && !q.Down),
                    string.Join(" | ", t.Party.Select(q => string.Join(",", t.HitsOn(q, Src).Select(h => h.Damage)))) + $"; stamina used, min left {staminaMin:0}");
                t.Check("N=4: Resolved counts 2, 2", resolved != null && resolved.Counts.SequenceEqual(new[] { 2, 2 }));
                foreach (var q in t.Party) t.In(q).Stop();
            }

            // ---------------- N = 4: missing share falls on the lone player, over-soak lowers each share, still player safe ----------------
            yield return t.Setup(4, Difficulty.Medium, at: new[] { new Vector3(0, 0, 9), new Vector3(1, 0, 9), new Vector3(-1, 0, 9), new Vector3(0, 0, 13) });
            towers = new List<Hazard>(); ticks.Clear();
            yield return Cast(t, towers, ticks);
            if (towers.Count == 2)
            {
                // players 0, 1, 2 into tower 0; player 3 alone into tower 1
                for (int i = 0; i < 3; i++) t.In(t.Party[i]).Go(towers[0].Origin + new Vector3(i - 1, 0, 0) * 0.6f, 6f);
                t.In(t.Party[3]).Go(towers[1].Origin, 6f);
                yield return t.WaitIdle(10);
                var over = Enumerable.Range(0, 3).Select(i => t.HitsOn(t.Party[i], Src).Sum(h => h.Damage)).ToList();
                int lone = t.HitsOn(t.Party[3], Src).Sum(h => h.Damage);
                t.Check("over-soak: three in a '2' mould take 14 each (never more than one share)", over.All(d => d == 14), string.Join(",", over));
                t.Check("missing share: one alone in a '2' mould takes 40", lone == 40, $"{lone}");
                t.Check("one hit per instance", t.OneHitPerInstance());
                foreach (var q in t.Party) t.In(q).Stop();
            }

            // ---------------- N = 4 with one downed before the cast: numerals for 3 ----------------
            yield return t.Setup(4, Difficulty.Medium);
            t.Party[3].ForceDown(new Hazard { Source = "test setup" });
            towers = new List<Hazard>(); ticks.Clear();
            yield return Cast(t, towers, ticks);
            t.Check("downed players don't count: 3 standing -> 2, 1", towers.Select(h => h.ShareCount).SequenceEqual(new[] { 2, 1 }), string.Join(",", towers.Select(h => h.ShareCount)));
            if (towers.Count == 2)
            {
                t.In(t.Party[0]).Go(towers[0].Origin, 6f); t.In(t.Party[1]).Go(towers[0].Origin + Vector3.right * 0.6f, 6f); t.In(t.Party[2]).Go(towers[1].Origin, 6f);
            }
            yield return t.WaitIdle(10);
            foreach (var q in t.Party) t.In(q).Stop();

            // ---------------- N = 8: four moulds on all four bays, two each ----------------
            yield return t.Setup(8, Difficulty.Medium);
            towers = new List<Hazard>(); ticks.Clear();
            yield return Cast(t, towers, ticks);
            t.Check("N=8: four moulds numbered 2, 2, 2, 2 on all four bays", towers.Count == 4 && towers.All(h => h.ShareCount == 2)
                && towers.Select(h => Mathf.RoundToInt(Mathf.Atan2(h.Origin.z, h.Origin.x) * Mathf.Rad2Deg)).Distinct().Count() == 4);
            if (towers.Count == 4)
            {
                for (int i = 0; i < 8; i++) t.In(t.Party[i]).Go(towers[i / 2].Origin + Vector3.right * (i % 2 == 0 ? 0.4f : -0.4f), 6f);
                yield return t.Seconds(3.2f);
                yield return t.Shot("mould_press_n8");
                yield return t.WaitIdle(10);
                t.Check("N=8: everyone takes one 20 share", t.Party.All(q => t.HitsOn(q, Src).Select(h => h.Damage).SequenceEqual(new[] { 20 }) && !q.Down),
                    string.Join(" | ", t.Party.Select(q => string.Join(",", t.HitsOn(q, Src).Select(h => h.Damage)))));
                t.Check("N=8: Resolved has four towers, 2 inside each", resolved != null && resolved.Counts.SequenceEqual(new[] { 2, 2, 2, 2 }) && resolved.Centres.Length == 4);
                t.Check("N=8: one hit per instance", t.OneHitPerInstance());
                foreach (var q in t.Party) t.In(q).Stop();
            }

            // ---------------- Hard: same timing, global x1.3 damage ----------------
            yield return t.Setup(2, Difficulty.Hard, at: new[] { new Vector3(4, 0, 4), new Vector3(-4, 0, -4) });
            towers = new List<Hazard>(); ticks.Clear();
            yield return Cast(t, towers, ticks);
            t.Check("Hard: warning stays 4.2 s", towers.Count == 2 && towers[0].ResolveAt == Sim.Ticks(4.2f));
            for (int i = 0; i < 2 && towers.Count == 2; i++) t.In(t.Party[i]).Go(towers[i].Origin, 6f);
            yield return t.WaitIdle(10);
            t.Check("Hard: a share hits for 26 (20 x 1.3)", t.Party.All(q => t.HitsOn(q, Src).Select(h => h.Damage).SequenceEqual(new[] { 26 })),
                string.Join(" | ", t.Party.Select(q => string.Join(",", t.HitsOn(q, Src).Select(h => h.Damage)))));
            foreach (var q in t.Party) t.In(q).Stop();

            // ---------------- Easy: warnings x1.25 (5.25 s, solo gap 3.25 s), damage x0.6 ----------------
            yield return t.Setup(1, Difficulty.Easy, at: new[] { new Vector3(0, 0, 6) });
            towers = new List<Hazard>(); ticks.Clear();
            yield return Cast(t, towers, ticks);
            t.Check("Easy: warning 5.25 s, solo gap 3.25 s", towers.Count == 2 && towers[0].ResolveAt == Sim.Ticks(5.25f) && towers[1].ResolveAt - towers[0].ResolveAt == Sim.Ticks(3.25f),
                towers.Count == 2 ? $"{Secs(towers[0].ResolveAt):0.00} s, gap {Secs(towers[1].ResolveAt - towers[0].ResolveAt):0.00} s" : "");
            if (towers.Count == 2)
            {
                t.In(p).Go(towers[0].Origin, 6f);
                yield return t.Until(() => !t.W.Hazards.Live.Contains(towers[0]), 8, "Easy tower I resolved");
                t.In(p).Go(towers[1].Origin, 4.2f);
            }
            yield return t.WaitIdle(12);
            t.In(p).Stop();
            t.Check("Easy: each solo tower hits for 12 (20 x 0.6)", t.HitsOn(p, Src).Select(h => h.Damage).SequenceEqual(new[] { 12, 12 }), string.Join(",", t.HitsOn(p, Src).Select(h => h.Damage)));

            // ---------------- still players: melee next to the boss and ranged at the edge, every mould empty -> everyone down ----------------
            var melee = new Vector3(0, 0, 0.7f); var ranged = new Vector3(0, 0, 13.2f); var mid = new Vector3(9, 0, 0);
            yield return t.Setup(4, Difficulty.Medium, at: new[] { melee, ranged, mid, new Vector3(-9.5f, 0, 9.5f) });
            towers = new List<Hazard>(); ticks.Clear();
            yield return Cast(t, towers, ticks);
            yield return t.Until(() => t.Party.All(q => q.Down), 8, "still players: the moulds resolved");
            t.Check("still players: melee (2.2 m from the boss) and ranged (edge, 13 m) are both downed by an empty mould",
                t.WasHit(t.Party[0], Src) && t.WasHit(t.Party[1], Src) && t.Party[0].Down && t.Party[1].Down);
            t.Check("still players: every standing player is downed (empty soak wipes)", t.Party.All(q => q.Down));
            // a full wipe ends the practice (Downed), which drops the boss script: Resolved is not raised for a wiped party

            // ---------------- solo still player: tower I empty wipes ----------------
            yield return t.Setup(1, Difficulty.Medium, at: new[] { new Vector3(0, 0, 9) });
            towers = new List<Hazard>(); ticks.Clear();
            yield return Cast(t, towers, ticks);
            yield return t.Until(() => towers.Count == 0 || !t.W.Hazards.Live.Contains(towers[0]), 8, "solo still: tower I resolved");
            yield return t.Ticks(2);
            t.Check("solo still player: downed by empty tower I", t.Main.Down && t.WasHit(t.Main, "Mould Press I"));

            // ---------------- boss death mid-windup cancels everything ----------------
            yield return t.Setup(2, Difficulty.Medium, at: new[] { new Vector3(0, 0, 9), new Vector3(1, 0, 9) });
            towers = new List<Hazard>(); ticks.Clear();
            yield return Cast(t, towers, ticks);
            yield return t.Seconds(1.5f);
            int killTick = t.W.Tick;
            t.KillBoss();
            yield return t.Seconds(5.5f);
            t.Check("boss death mid-windup: no Mould Press damage afterwards", !t.Hits.Any(h => h.Source.StartsWith(Src) && h.Tick >= killTick) && t.Party.All(q => !q.Down),
                $"{t.Hits.Count(h => h.Source.StartsWith(Src))} hits");
            t.Check("boss death mid-windup: moulds and their FX are gone", !t.W.Hazards.Live.Any(h => h.Source.StartsWith(Src)) && Object.FindObjectsByType<MouldPressFx>(FindObjectsSortMode.None).Length == 0);
        }
    }
}
