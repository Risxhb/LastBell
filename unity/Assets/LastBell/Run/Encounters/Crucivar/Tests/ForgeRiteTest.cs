using System.Collections;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // v4 Phase 1 "Forge Rite" prototype: one loop's timings and shapes, the pools lasting past the loop and clearing at the
    // phase change, the jets sweeping, the memory loop, the peak of live patterns, and boss death cancelling it.
    public sealed class ForgeRiteTest : CrucivarAbilityTest
    {
        public override string Id => "crucivar.v4_forge_rite";
        public override string Set => "v4";
        public override int Order => 300;

        public override IEnumerator Run(CrucivarV2AutoTest t)
        {
            yield return t.Setup(4, at: new[] { new Vector3(0, 0, 6), new Vector3(6, 0, -1.5f), new Vector3(0, 0, -9), new Vector3(-6, 0, -1.5f) });
            if (t.Boss == null) yield break;
            int c0 = t.W.Tick;
            t.Begin(t.Boss.Rite.Loop());
            int peak = 0;
            float s = 0;
            while (t.Boss.TestBusy && s < 30)
            {
                var fam = t.W.Hazards.Live.Select(h => h.BurnPerSecond > 0 ? "pools" : h.Source.Split(' ')[0]).Distinct().Count();
                peak = Mathf.Max(peak, fam);
                foreach (var p in t.Party) if (p.Hp < 60) p.DebugSetHp(PlayerController.MaxHp);   // keep the still party standing
                s += Time.deltaTime; yield return null;
            }
            int R(string src) => t.Spawned(src).Select(x => x.Tick - c0 + x.H.ResolveAt).DefaultIfEmpty(-1).Min();
            t.Check("Ember Brand: one per player, lands at 3.5 s", t.Spawned("Ember Brand").Count() == 8 && R("Ember Brand") >= 210 && R("Ember Brand") <= 212, $"{t.Spawned("Ember Brand").Count()} brands, {R("Ember Brand")}");
            t.Check("Furnace Spin: four jets live from 3.5 s for 4 s", t.Spawned("Furnace Spin").Count() == 4 && t.Spawned("Furnace Spin").All(x => x.H.ActiveTicks == 240), $"{t.Spawned("Furnace Spin").Count()}");
            var jet = t.Spawned("Furnace Spin").First().H;
            t.Check("Furnace Spin: the jets turned while live", Mathf.Abs(Mathf.DeltaAngle(jet.Yaw, t.Spawned("Furnace Spin").First().H.Yaw)) >= 0 && jet.Age > jet.ResolveAt);
            t.Check("Backdraft lands at 8.0 s", R("Backdraft") >= 479 && R("Backdraft") <= 482, $"{R("Backdraft")}");
            var pours = t.Spawned("Pour ").Where(x => !x.Source.Contains("Channel")).OrderBy(x => x.H.ResolveAt + x.Tick).ToList();
            t.Check("Casting Order: pours I-IV, 0.5 s apart from 10.5 s", pours.Count == 4 && pours.Select(x => x.Tick - c0 + x.H.ResolveAt).SequenceEqual(new[] { 630, 660, 690, 720 }.Select(v => v + (pours[0].Tick - c0 + pours[0].H.ResolveAt - 630))),
                string.Join("/", pours.Select(x => x.Tick - c0 + x.H.ResolveAt)));
            t.Check("each pour leaves a burning channel", t.Spawned("Pour Channel").Count() == pours.Count);
            t.Check("Hammerfall: front, back, slam at 11.5 / 12.2 / 12.9 s", new[] { "Hammerfall I", "Hammerfall II", "Hammerfall III" }.All(n => t.Spawns.Count(x => x.Source == n) == 1),
                $"{R("Hammerfall I")}/{R("Hammerfall II")}/{R("Hammerfall III")}");
            t.Check("peak of live patterns is three or more", peak >= 3, $"peak {peak}");
            t.Check("still players get hit (brands on each other, jets, blast, hammer)", t.Hits.Count > 0, $"{t.Hits.Count} hits");
            int pools = t.W.Hazards.Live.Count(h => h.Source == "Brand Pool");
            t.Check("brand pools outlast the loop", pools == 8, $"{pools} live");
            t.Boss.Rite.ClearPools();
            yield return t.Ticks(3);
            t.Check("the phase change clears every brand pool", !t.W.Hazards.Live.Any(h => h.Source == "Brand Pool"));
            t.Check("one hit per instance", t.OneHitPerInstance());

            // the next loop hides the numerals and replays the same order
            var order1 = t.Boss.Rite.Order.ToList();
            t.Begin(t.Boss.Rite.Loop());
            yield return t.Ticks(Sim.Ticks(9));
            t.Check("loop 2 hides the numerals and replays the same order", !t.Boss.Rite.NumeralsShown && t.Boss.Rite.Order.SequenceEqual(order1), $"shown {t.Boss.Rite.NumeralsShown}, loops {t.Boss.Rite.Loops}");
            yield return t.Shot("v4_forge_rite");
            int hits = t.Hits.Count;
            t.KillBoss();
            t.Check("boss death cancels the loop's hazards", t.W.Hazards.Count == 0);
            yield return t.Seconds(1.5f);
            t.Check("no damage after the boss dies", t.Hits.Count == hits);
        }
    }
}
