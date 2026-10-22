using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Integration: the v2 phase plan and the second-form transition (ABILITIES.md "Phase plan"). Lets Crucivar's schedule
    // run with one bot that stands in moulds, keeps to the 4.8-9.2 m safe band otherwise, and is healed between hits,
    // and checks the order of abilities per phase, the 65 % second form and the 30 % change.
    public sealed class PhasePlanTest : CrucivarAbilityTest
    {
        public override string Id => "crucivar.phase_plan";
        public override int Order => 100;   // after the ability tests

        readonly List<(int phase, string name)> order = new List<(int, string)>();

        static readonly HashSet<string> Abilities = new HashSet<string> { "Mould Press", "Gear Chain", "Bellows Draw", "The Great Casting", "Second Press" };

        IEnumerator Watch(CrucivarV2AutoTest t, float seconds, System.Func<bool> until)
        {
            float s = 0; string last = null;
            while (s < seconds && !until() && t.D.State == RunState.Combat)
            {
                var b = t.Boss; var p = t.Main;
                if (b.Current != last) { last = b.Current; if (Abilities.Contains(last) && (order.Count == 0 || order[order.Count - 1].name != last || order[order.Count - 1].phase != b.PhaseIndex)) order.Add((b.PhaseIndex, last)); }
                if (p.Hp < 50) p.DebugSetHp(PlayerController.MaxHp);
                var mould = t.W.Hazards.Live.Where(h => h.ShareTotal > 0 && h.WipeIfEmpty).OrderBy(h => h.ResolveAt - h.Age).FirstOrDefault();
                if (mould != null) t.In(p).Go(mould.Origin, 6f);
                else
                {
                    var from = Sim.Flat(p.transform.position - b.transform.position); if (from.sqrMagnitude < 0.01f) from = Vector3.forward;
                    var spot = b.transform.position + from.normalized * 6.5f;
                    if (new Vector2(spot.x, spot.z).magnitude > 8.8f) spot = Sim.Flat(spot).normalized * 8.8f;
                    t.In(p).Go(spot, 6f);
                }
                s += Time.deltaTime; yield return null;
            }
            t.In(t.Main).Stop();
        }

        public override IEnumerator Run(CrucivarV2AutoTest t)
        {
            yield return t.Setup(1, Difficulty.Medium, hpFraction: 0.95f);
            var boss = t.Boss; if (boss == null) yield break;
            t.Check("v2 is the default set for the practice entry", !CrucivarArgs.V1);
            boss.TestHold = false;

            // phase 1: Mould Press, Bellows Draw, Great Casting (pour only), in that order
            yield return Watch(t, 45, () => order.Count(o => o.phase == 1) >= 4);
            var p1 = order.Where(o => o.phase == 1).Select(o => o.name).ToList();
            t.Note("phase 1 order: " + string.Join(" > ", p1));
            t.Check("phase 1 runs Mould Press > Bellows Draw > Great Casting", p1.Take(3).SequenceEqual(new[] { "Mould Press", "Bellows Draw", "The Great Casting" }), string.Join(", ", p1));
            t.Check("phase 1 has no Gear Chain", !p1.Contains("Gear Chain"));

            // 65 %: the second form, queued to the end of the current attack, no heal, harmless
            int hpBefore = Mathf.RoundToInt(boss.MaxHp * 0.64f);
            boss.DebugSetHpFraction(0.64f);
            int hitsBefore = t.Hits.Count; int tr0 = boss.Transitions;
            yield return Watch(t, 20, () => boss.PhaseIndex == 2);
            t.Check("second form at 65 %: Casting Heat", boss.PhaseIndex == 2 && boss.SecondForm && boss.PhaseName == "Casting Heat", boss.PhaseName);
            t.Check("one transition, and it doesn't heal", boss.Transitions == tr0 + 1 && boss.Hp == hpBefore, $"transitions {boss.Transitions - tr0}, hp {hpBefore} -> {boss.Hp}");
            yield return t.Seconds(0.8f);
            yield return t.Shot("v2_second_form");

            // phase 2: Gear Chain alone, Rim + Mould, Rim + Chain, Bellows, Casting (pour + 3)
            yield return Watch(t, 60, () => order.Count(o => o.phase == 2) >= 5);
            var p2 = order.Where(o => o.phase == 2).Select(o => o.name).ToList();
            t.Note("phase 2 order: " + string.Join(" > ", p2));
            t.Check("phase 2 runs Chain > Mould > Chain > Bellows > Casting",
                p2.Take(5).SequenceEqual(new[] { "Gear Chain", "Mould Press", "Gear Chain", "Bellows Draw", "The Great Casting" }), string.Join(", ", p2));

            // 30 %: Open Pressure
            boss.DebugSetHpFraction(0.29f);
            yield return Watch(t, 25, () => boss.PhaseIndex == 3);
            t.Check("Open Pressure at 30 %", boss.PhaseIndex == 3 && boss.PhaseName == "Open Pressure", boss.PhaseName);
            yield return Watch(t, 60, () => order.Count(o => o.phase == 3) >= 4);
            var p3 = order.Where(o => o.phase == 3).Select(o => o.name).ToList();
            t.Note("phase 3 order: " + string.Join(" > ", p3));
            t.Check("phase 3 opens with the Great Casting, then Chain > Mould > Bellows",
                p3.Take(4).SequenceEqual(new[] { "The Great Casting", "Gear Chain", "Mould Press", "Bellows Draw" }), string.Join(", ", p3));
            t.Check("never more than two damaging patterns at once (phase plan)", MaxPatterns(t) <= 2, $"max {MaxPatterns(t)}");

            // two thresholds in one burst queue a single change
            t.KillBoss();
            yield return t.Setup(1, Difficulty.Medium, hpFraction: 0.95f);
            boss = t.Boss; if (boss == null) yield break;
            boss.TestHold = false;
            yield return t.Seconds(0.5f);
            int trA = boss.Transitions;
            boss.DebugSetHpFraction(0.20f);
            yield return Watch(t, 20, () => boss.PhaseIndex == 3);
            yield return t.Seconds(2f);
            t.Check("crossing 65 % and 30 % in one burst queues one transition", boss.PhaseIndex == 3 && boss.Transitions == trA + 1, $"transitions {boss.Transitions - trA}");
            boss.TestHold = true;
        }

        // damaging families live at the same tick (standing slag counts as one pattern), from the spawn log
        static int MaxPatterns(CrucivarV2AutoTest t)
        {
            int max = 0;
            foreach (var s in t.Spawns)
            {
                int at = s.Tick;
                var live = t.Spawns.Where(o => o.Tick <= at && at < o.Tick + o.H.ResolveAt + Mathf.Max(1, o.H.ActiveTicks))
                    .Select(o => o.H.BurnPerSecond > 0 ? "slag" : Family(o.Source)).Distinct().Count();
                max = Mathf.Max(max, live);
            }
            return max;
        }
        // one ability = one pattern, except Bellows Draw whose rim and blast are two
        static string Family(string src) => src.StartsWith("Mould") ? "Mould" : src.StartsWith("Gear") ? "Chain" : src.StartsWith("Pour") ? "Pour" :
            src.StartsWith("Stamp") || src.StartsWith("March") ? "March" : src;
    }
}
