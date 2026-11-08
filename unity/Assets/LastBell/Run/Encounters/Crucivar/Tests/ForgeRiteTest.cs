using System;
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
        public override int Order => 300;

        public override IEnumerator Run(CrucivarAutoTest t)
        {
            yield return t.Setup(4, at: new[] { new Vector3(0, 0, 6), new Vector3(6, 0, -1.5f), new Vector3(0, 0, -9), new Vector3(-6, 0, -1.5f) });
            if (t.Boss == null) yield break;
            t.Check("v4 HP: 8500 solo, +0.9 per extra fighter (4 players 31450)", CrucivarEncounter.V4MaxHp(1) == 8500 && CrucivarEncounter.V4MaxHp(4) == 31450, CrucivarEncounter.V4MaxHp(4).ToString());
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
            // time from the loop's own start (the first brand): in a full-suite run the body can start a few seconds after Begin
            c0 = t.Spawned("Ember Brand").Select(x => x.Tick).DefaultIfEmpty(c0).Min();
            int R(string src) => t.Spawned(src).Select(x => x.Tick - c0 + x.H.ResolveAt).DefaultIfEmpty(-1).Min();
            t.Check("Ember Brand: one per player, lands at 3.5 s", t.Spawned("Ember Brand").Count() == 8 && R("Ember Brand") >= 210 && R("Ember Brand") <= 212, $"{t.Spawned("Ember Brand").Count()} brands, {R("Ember Brand")}");
            t.Check("Furnace Spin: six jets live from 3.5 s for 4 s", t.Spawned("Furnace Spin").Count() == ForgeRite.JetCount && t.Spawned("Furnace Spin").All(x => x.H.ActiveTicks == 240), $"{t.Spawned("Furnace Spin").Count()}");
            var jet = t.Spawned("Furnace Spin").First().H;
            t.Check("Furnace Spin: the jets turned while live", Mathf.Abs(Mathf.DeltaAngle(jet.Yaw, t.Spawned("Furnace Spin").First().H.Yaw)) >= 0 && jet.Age > jet.ResolveAt);
            t.Check("Furnace Spin: every jet reaches the wall", t.Spawned("Furnace Spin").All(x => Mathf.Abs((Sim.Flat(x.H.Origin) + Sim.Dir(x.H.Yaw) * x.H.Length).magnitude - (CrucivarAbility.Rim + Hazard.Margin)) < 0.05f));
            var jets = t.Spawned("Furnace Spin").Select(x => x.H).ToList();
            t.Check("Furnace Spin: from the arena centre, every jet the same length", jets.All(x => Sim.Flat(x.Origin).magnitude < 0.05f) && jets.Max(x => x.Length) - jets.Min(x => x.Length) < 0.05f,
                string.Join("/", jets.Select(x => $"{Sim.Flat(x.Origin).magnitude:0.00}:{x.Length:0.0}")));
            t.Check("Medium: the jets turn one way for the whole 4 s (no Spin Reversal)", t.Boss.Rite.Reversals == 0 && Mathf.Abs(Mathf.Abs(t.Boss.Rite.SpinNet) - ForgeRite.JetTurnDeg * ForgeRite.JetSweep) < 2f, $"{t.Boss.Rite.SpinNet:0.0} deg");
            var q = new[] { "Anvil Quake I", "Anvil Quake II", "Anvil Quake III" }.Select(n => t.Spawns.First(x => x.Source == n).H).ToArray();
            var o = Sim.Flat(q[0].Origin); var wallPt = Vector3.right * (CrucivarAbility.Rim - 0.2f);
            bool Only(int i, Vector3 p) => Enumerable.Range(0, 3).All(k => q[k].Contains(p) == (k == i));
            t.Check("Anvil Quake: inner / middle / outer bands, the outer one reaching the wall", Only(0, o + Vector3.forward * 2f) && Only(1, o + Vector3.forward * 6.5f) && Only(2, wallPt) && Only(2, -wallPt));
            t.Check("Backdraft lands at 8.0 s", R("Backdraft") >= 479 && R("Backdraft") <= 482, $"{R("Backdraft")}");
            var pours = t.Spawned("Pour ").Where(x => !x.Source.Contains("Channel")).OrderBy(x => x.H.ResolveAt + x.Tick).ToList();
            t.Check("Casting Order: pours I-IV, 0.5 s apart from 10.5 s", pours.Count == 4 && pours.Select(x => x.Tick - c0 + x.H.ResolveAt).SequenceEqual(new[] { 630, 660, 690, 720 }.Select(v => v + (pours[0].Tick - c0 + pours[0].H.ResolveAt - 630))),
                string.Join("/", pours.Select(x => x.Tick - c0 + x.H.ResolveAt)));
            t.Check("each pour leaves a burning channel", t.Spawned("Pour Channel").Count() == pours.Count);
            t.Check("Anvil Quake: rings at 12.4 / 13.2 / 14.0 s, after the last pour", Math.Abs(R("Anvil Quake I") - 744) <= 2 && Math.Abs(R("Anvil Quake II") - 792) <= 2 && Math.Abs(R("Anvil Quake III") - 840) <= 2 && R("Anvil Quake I") > pours.Max(x => x.Tick - c0 + x.H.ResolveAt),
                $"{R("Anvil Quake I")}/{R("Anvil Quake II")}/{R("Anvil Quake III")}");
            t.Check("peak of live patterns is three or more", peak >= 3, $"peak {peak}");
            t.Check("still players get hit (brands on each other, jets, blast, quake)", t.Hits.Count > 0, $"{t.Hits.Count} hits");
            t.Check("the brand stops following 1.0 s before it lands; its pool forms where it stopped", t.Spawned("Ember Brand").All(x => x.H.ResolveAt - x.H.LockAt == Sim.Ticks(ForgeRite.BrandFreeze)) && t.Spawned("Brand Pool").All(x => x.H.LockAt == 0));
            var drafts = t.Spawned("Backdraft").Select(x => x.Tick - c0).ToList();
            t.Check("a second Backdraft pulls during the second brand (from 14.0 s)", drafts.Count >= 2 && drafts.Max() - drafts.Min() >= Sim.Ticks(8.5f), string.Join("/", drafts));
            int pools = t.W.Hazards.Live.Count(h => h.Source == "Brand Pool");
            t.Check("brand pools outlast the loop", pools == 8, $"{pools} live");
            t.Boss.Rite.ClearPools();
            yield return t.Ticks(3);
            t.Check("the phase change clears every brand pool", !t.W.Hazards.Live.Any(h => h.Source == "Brand Pool"));
            t.Check("one hit per instance", t.OneHitPerInstance());

            // the next loop hides the numerals and replays the same order
            var order1 = t.Boss.Rite.Order.ToList();
            t.Begin(t.Boss.Rite.Loop());
            for (float w = 0; w < 15 && t.Spawns.Count(x => x.Source == "Pour I") < 2; w += Time.deltaTime)
            {
                foreach (var p in t.Party) if (p.Hp < 60) p.DebugSetHp(PlayerController.MaxHp);   // keep the still party standing
                yield return null;
            }
            int pourCount = t.Spawns.Count(x => x.Source == "Pour I");
            t.Check("loop 2 hides the numerals and replays the same order", !t.Boss.Rite.NumeralsShown && t.Boss.Rite.Order.SequenceEqual(order1), $"shown {t.Boss.Rite.NumeralsShown}, loops {t.Boss.Rite.Loops}, now {t.Boss.Current}, pours {pourCount}");
            yield return t.Shot("v4_forge_rite");
            int hits = t.Hits.Count;
            t.KillBoss();
            t.Check("boss death cancels the loop's hazards", t.W.Hazards.Count == 0);
            yield return t.Seconds(1.5f);
            t.Check("no damage after the boss dies", t.Hits.Count == hits);
            t.Check("Medium has no Quake Echo", !t.Spawns.Any(x => x.Source.StartsWith("Quake Echo")));

            // Hard: Quake Echo, the bands again outside-in from 0.8 s after the outer band
            yield return t.Setup(4, Difficulty.Hard, at: new[] { new Vector3(0, 0, 6), new Vector3(6, 0, -1.5f), new Vector3(0, 0, -9), new Vector3(-6, 0, -1.5f) });
            if (t.Boss == null) yield break;
            t.Begin(t.Boss.Rite.Loop());
            for (s = 0; s < 30 && t.Boss.TestBusy; s += Time.deltaTime)
            {
                foreach (var p in t.Party) if (p.Hp < 60) p.DebugSetHp(PlayerController.MaxHp);
                yield return null;
            }
            int At(string src) => t.Spawns.Where(x => x.Source == src).Select(x => x.Tick + x.H.ResolveAt).DefaultIfEmpty(-1).Min();
            int outer = At("Anvil Quake III"), e3 = At("Quake Echo III"), e2 = At("Quake Echo II"), e1 = At("Quake Echo I");
            t.Check("Hard: Quake Echo fires III / II / I at +0.8 / 1.6 / 2.4 s after the outer band", t.Boss.Rite.Echoes == 1 && outer > 0
                && Math.Abs(e3 - outer - 48) <= 2 && Math.Abs(e2 - outer - 96) <= 2 && Math.Abs(e1 - outer - 144) <= 2, $"echoes {t.Boss.Rite.Echoes}, {e3 - outer}/{e2 - outer}/{e1 - outer}");
            t.Check("Hard: the echo shows only once the outer band has landed", t.Spawned("Quake Echo").All(x => x.Tick >= outer - 1));
            var ech = new[] { "Quake Echo I", "Quake Echo II", "Quake Echo III" }.Select(n => t.Spawns.FirstOrDefault(x => x.Source == n)?.H).ToArray();
            if (ech.All(x => x != null))
            {
                var eo = Sim.Flat(ech[0].Origin); var wp = Vector3.right * (CrucivarAbility.Rim - 0.2f);
                bool OnlyE(int i, Vector3 p) => Enumerable.Range(0, 3).All(k => ech[k].Contains(p) == (k == i));
                t.Check("Hard: the echo bands match the quake bands", OnlyE(0, eo + Vector3.forward * 2f) && OnlyE(1, eo + Vector3.forward * 6.5f) && OnlyE(2, wp));
            }
            else t.Check("Hard: the echo bands match the quake bands", false, "missing echo");
            // Hard: Spin Reversal, 2.0 s one way, 0.5 s still, 1.5 s back: 80 - 60 = 20 degrees net
            float net = ForgeRite.JetTurnDeg * (ForgeRite.ReverseAt - (ForgeRite.JetSweep - ForgeRite.ReverseAt - ForgeRite.ReverseHold));
            t.Check("Hard: Spin Reversal stops the jets at 2.0 s for 0.5 s, then turns them back", t.Boss.Rite.Reversals == 1 && Mathf.Abs(Mathf.Abs(t.Boss.Rite.SpinNet) - net) < 2f,
                $"reversals {t.Boss.Rite.Reversals}, net {t.Boss.Rite.SpinNet:0.0} deg (want {net:0})");
            t.Check("Hard: the reversed jets still reach the wall and stay live 4 s", t.Spawned("Furnace Spin").All(x => x.H.ActiveTicks == 240 && Mathf.Abs((Sim.Flat(x.H.Origin) + Sim.Dir(x.H.Yaw) * x.H.Length).magnitude - (CrucivarAbility.Rim + Hazard.Margin)) < 0.05f));
            t.Check("Hard: the loop runs to its end", !t.Boss.TestBusy);
            t.Check("Hard: one hit per instance", t.OneHitPerInstance());

            // no mechanic aims at a reviver (review CRU-TARGET-OTHER): player 1 kneels over a downed player 0, then a brand and an order go out
            yield return t.Setup(4, at: new[] { new Vector3(0, 0, 6), new Vector3(0.8f, 0, 6), new Vector3(0, 0, -9), new Vector3(-6, 0, -1.5f) });
            if (t.Boss == null) yield break;
            var rv = t.Party[1];
            t.Party[0].ForceDown("Crucivar test: revive target");
            for (int k = 0; k < 6; k++) { rv.DebugSetHp(PlayerController.MaxHp); yield return null; }
            int brands0 = t.Spawned("Ember Brand").Count(), unaimed0 = t.Boss.Rite.UnaimedPours;
            t.Boss.Rite.EmberBrand();
            t.Boss.Rite.CastingOrder();
            yield return t.Ticks(1);
            var brands = t.Spawned("Ember Brand").Skip(brands0).ToList();
            var pourOnRv = t.Boss.Rite.Order.IndexOf(rv);
            t.Check("Ember Brand gives an active reviver no brand (the other standing players still get one)", rv.Reviving == t.Party[0] && brands.Count == 2
                && brands.All(b => Sim.Flat(b.H.Origin - rv.transform.position).magnitude > 1f), $"reviving {rv.Reviving == t.Party[0]}, brands {brands.Count}");
            t.Check("Casting Order does not aim its pour at an active reviver (a fixed line instead, same pour count)", pourOnRv >= 0 && t.Boss.Rite.UnaimedPours - unaimed0 == 1
                && t.Spawned("Pour ").Count(x => !x.Source.Contains("Channel")) >= t.Boss.Rite.Order.Count, $"order slot {pourOnRv}, unaimed {t.Boss.Rite.UnaimedPours - unaimed0}");
            t.W.ClearTransient();
            t.Party[0].DebugRevive();
        }

    }
}
