using System.Collections;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // v4 Phase 3 "Molten Crown" proposal: tower sets and counts, filled towers land safe and leave slag, the pours, Backdraft
    // and turret run during the cycle, the crown rises to 11 m and burns, heat raises later hits, the second cycle hides
    // the counts, and an empty tower wipes the party.
    public sealed class MoltenCrownTest : CrucivarAbilityTest
    {
        public override string Id => "crucivar.v4_molten_crown";
        public override int Order => 320;

        public override IEnumerator Run(CrucivarV2AutoTest t)
        {
            yield return t.Setup(4, at: new[] { new Vector3(0, 0, 4f), new Vector3(4, 0, 0), new Vector3(0, 0, -4f), new Vector3(-4, 0, 0) });
            if (t.Boss == null) yield break;
            var crown = t.Boss.Crown;
            t.Check("4 players: set A 2 + 2, set B 2 + 2", MoltenCrown.Sets(4).Length == 2 && MoltenCrown.Sets(4).All(s => s.SequenceEqual(new[] { 2, 2 })));
            t.Check("3 players: set A 2 + 1, set B 2 + 1", MoltenCrown.Sets(3)[0].SequenceEqual(new[] { 2, 1 }) && MoltenCrown.Sets(3)[1].SequenceEqual(new[] { 2, 1 }));
            t.Check("solo: three towers of one", MoltenCrown.Sets(1).Length == 3 && MoltenCrown.Sets(1).All(s => s.SequenceEqual(new[] { 1 })));

            t.Begin(crown.Run());
            float s = 0; bool sawB = false;
            while (s < 21f && t.Boss.TestBusy)
            {
                foreach (var p in t.Party) if (p.Hp < 60) p.DebugSetHp(PlayerController.MaxHp);
                // stand the party in the soonest set's towers, two each
                var set = crown.Towers.Where(x => x.H.Age < x.H.ResolveAt).GroupBy(x => x.Set).OrderBy(g => g.Min(x => x.H.ResolveAt - x.H.Age)).FirstOrDefault();
                if (set != null)
                {
                    if (set.Key == 1) sawB = true;
                    var tw = set.OrderBy(x => x.H.Id).ToList();
                    for (int i = 0; i < t.Party.Count; i++) t.Place(t.Party[i], tw[(i / 2) % tw.Count].H.Origin + new Vector3((i % 2) * 0.8f - 0.4f, 0, 0));
                }
                else foreach (var p in t.Party) if (Sim.Flat(p.transform.position).magnitude > 8f) t.Place(p, Sim.Flat(p.transform.position).normalized * 6f);
                s += Time.deltaTime; yield return null;
            }
            var towers = t.Spawned("Mould Tower").ToList();
            t.Check("two sets of two towers rise, set B 1.6 s after set A", towers.Count == 4 && sawB && towers.Max(x => x.Tick) - towers.Min(x => x.Tick) >= Sim.Ticks(1.6f) - 1, $"{towers.Count} towers");
            var towA = towers.Where(x => x.Source == "Mould Tower A").ToList(); var towB = towers.Where(x => x.Source == "Mould Tower B").ToList();
            float gapAB = towB.Count == 0 || towA.Count == 0 ? 0 : towB.Min(b => towA.Min(a => Sim.Flat(b.H.Origin - a.H.Origin).magnitude));
            t.Check("set B lands between set A's towers, not on them (you soak A, then run to B)", gapAB >= MoltenCrown.TowerRadius * 2f, $"nearest A-B {gapAB:0.0} m");
            t.Check("filled towers land safe (no wipe) and leave slag", crown.Wipes == 0 && t.Spawned("Tower Slag").Count() == 4, $"wipes {crown.Wipes}, slag {t.Spawned("Tower Slag").Count()}");
            var lastTower = towers.Max(x => x.Tick + x.H.ResolveAt);
            t.Check("pours come after the towers have landed (no pour lands on a soaker who must stay)", t.Spawned("Pour I").Any() && t.Spawned("Pour ").Where(x => !x.Source.Contains("Channel")).All(x => x.Tick > lastTower),
                string.Join("/", t.Spawned("Pour ").Select(x => x.Tick - lastTower)));
            t.Check("tower slag heats 1.0 s before it burns", t.Spawned("Tower Slag").All(x => x.H.LockAt == Sim.Ticks(ForgeRite.PoolArm)));
            t.Check("Backdraft right after set B", t.Spawned("Backdraft").Any());
            t.Check("the lava turret fires Lane Streams", t.Spawns.Any(x => x.Source == "Lane Stream bullet"));
            t.Check("the crown rises to 11 m and burns for good", crown.Crowns == 1 && Mathf.Approximately(crown.Edge, 11f) && t.W.Hazards.Live.Any(h => h.Source == "Molten Crown" && h.BurnPerSecond > 0),
                $"crowns {crown.Crowns}, edge {crown.Edge}");
            var quake = t.Spawned("Anvil Quake I").LastOrDefault();
            t.Check("heat: after one crown, Anvil Quake hits for 40 x 1.15", quake != null && quake.H.Damage == Mathf.RoundToInt(40 * 1.15f), quake == null ? "no quake" : $"{quake.H.Damage}");
            t.Check("one hit per instance", t.OneHitPerInstance());
            yield return t.Shot("v4_molten_crown");

            // second cycle, on Hard: the counts are hidden, and Second Press re-stamps the fullest tower of each set 2.0 s after it lands
            yield return t.Until(() => !t.Boss.TestBusy, 5f, "cycle 1 over");
            yield return t.Setup(4, Difficulty.Hard, at: new[] { new Vector3(0, 0, 4f), new Vector3(4, 0, 0), new Vector3(0, 0, -4f), new Vector3(-4, 0, 0) });
            int presses0 = crown.Presses, cooled0 = crown.Cooled;
            t.Begin(crown.Run());
            yield return t.Ticks(2);
            // burning ground all round the tower ring: every piece that touches a rising tower cools at once
            for (int k = 0; k < 24; k++) t.W.Hazards.Spawn(new Hazard { Source = "Test Slag", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = Sim.Dir(k * 15f) * 5.5f, Radius = 2f, LockAt = 0, ResolveAt = Sim.Ticks(1f), BurnPerSecond = 20f });   // short-lived: it must not outlast this check
            yield return t.Ticks(3);
            bool Touch(Hazard b, Hazard tw) => b.Contains(tw.Origin) || Enumerable.Range(0, 8).Any(k => b.Contains(tw.Origin + Sim.Dir(k * 45f) * MoltenCrown.TowerRadius));
            var pend = crown.Towers.Where(x => x.H.Age < x.H.ResolveAt).Select(x => x.H).ToList();
            t.Check("a rising tower cools the burning ground on its spot (soaking never means standing in burn)", pend.Count > 0 && crown.Cooled > cooled0
                && !t.W.Hazards.Live.Any(b => b.BurnPerSecond > 0 && b.Age < b.ResolveAt - 1 && pend.Any(tw => Touch(b, tw))), $"cooled {crown.Cooled - cooled0}, pending {pend.Count}");
            s = 0; bool hidden = false;
            while (s < 12f && t.Boss.TestBusy)
            {
                foreach (var p in t.Party) if (p.Hp < 60) p.DebugSetHp(PlayerController.MaxHp);
                hidden |= crown.CountsHidden;
                var set = crown.Towers.Where(x => x.H.Age < x.H.ResolveAt).GroupBy(x => x.Set).OrderBy(g => g.Min(x => x.H.ResolveAt - x.H.Age)).FirstOrDefault();
                if (set != null)
                {
                    var tw = set.OrderBy(x => x.H.Id).ToList();
                    for (int i = 0; i < t.Party.Count; i++) t.Place(t.Party[i], tw[(i / 2) % tw.Count].H.Origin + new Vector3((i % 2) * 0.8f - 0.4f, 0, 0));
                }
                s += Time.deltaTime; yield return null;
            }
            t.Check("the second cycle hides the tower counts", hidden);
            var press = t.Spawned("Second Press").ToList();
            var setA = t.Spawned("Mould Tower A").ToList();
            var first = setA.FirstOrDefault();
            t.Check("Hard: Second Press re-stamps one tower of each set", crown.Presses - presses0 == 2 && press.Count == 2 && setA.Any(a => (press[0].H.Origin - a.H.Origin).sqrMagnitude < 0.01f) && crown.Wipes == 0,
                $"presses {crown.Presses - presses0}, spawned {press.Count}, on a set A tower {setA.Any(a => press.Count > 0 && (press[0].H.Origin - a.H.Origin).sqrMagnitude < 0.01f)}, wipes {crown.Wipes}");
            t.Check("Hard: the press starts once its set has landed and warns about 2 s", first != null && press.Count > 0 && press[0].Tick - first.Tick >= first.H.ResolveAt && press[0].H.ResolveAt >= Sim.Ticks(1.5f),
                press.Count > 0 && first != null ? $"{press[0].Tick - first.Tick} ticks after the rise, warn {press[0].H.ResolveAt}" : "none");

            // third cycle: nobody soaks, so the first tower to land wipes the party
            for (s = 0; s < 30f && t.Boss.TestBusy; s += Time.deltaTime) { foreach (var p in t.Party) p.DebugSetHp(PlayerController.MaxHp); yield return null; }   // keep the party up until the Hard cycle ends
            t.Check("the Hard cycle runs to its end", !t.Boss.TestBusy);
            yield return t.Setup(4, at: new[] { new Vector3(0, 0, 4f), new Vector3(4, 0, 0), new Vector3(0, 0, -4f), new Vector3(-4, 0, 0) });
            if (t.Boss == null) yield break;
            crown = t.Boss.Crown;   // the last pour channels can down the idle party after the Hard cycle, which restarts the encounter
            foreach (var p in t.Party) { p.DebugSetHp(PlayerController.MaxHp); t.Place(p, new Vector3(0, 0, 9.5f)); }
            t.Begin(crown.Run());
            yield return t.Until(() => t.Party.All(p => p.Down), 8f, "overflow");
            t.Check("an empty tower is Molten Overflow: the party wipes", t.Party.All(p => p.Down) && crown.Wipes == 1, $"down {t.Party.Count(p => p.Down)}/4, wipes {crown.Wipes}");
        }
    }
}
