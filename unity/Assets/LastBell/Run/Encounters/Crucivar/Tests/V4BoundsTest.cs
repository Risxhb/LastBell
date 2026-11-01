using System.Collections;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // v4 escape check, the floor's edge: a player can't leave the floor (walking, sprinting, dashing, shoved or pulled), and
    // the attacks meant to reach the wall reach the farthest standable spot, on the Brass Foundry circle and on the square
    // key-art dais Test Rush can also put him on (its corners lie 1.41 x the half-width out). No safe corner, no out-ranging.
    public sealed class V4BoundsTest : CrucivarAbilityTest
    {
        public override string Id => "crucivar.v4_bounds";
        public override string Set => "v4";
        public override int Order => 330;

        public override IEnumerator Run(CrucivarV2AutoTest t)
        {
            float keepBound = CombatWorld.PlayerBound; bool keepSquare = CombatWorld.SquareBound;
            foreach (var square in new[] { false, true })
            {
                CombatWorld.SetPlayerBound(square ? 12.5f : 13.7f, square);
                float b = CombatWorld.PlayerBound;
                string tag = square ? "square 12.5 m" : "circle 13.7 m";
                // the farthest standable spot: a corner of the square, the rim of the circle (on a jet diagonal)
                var far = square ? new Vector3(b - 0.05f, 0, b - 0.05f) : Sim.Dir(45f) * (b - 0.05f);
                yield return t.Setup(1, at: new[] { far });
                if (t.Boss == null) break;
                var p = t.Party[0];
                t.Check($"{tag}: the player starts on the far spot", CombatWorld.InPlayerBound(p.transform.position, 0.01f) && Sim.Flat(p.transform.position).magnitude > b - 0.2f,
                    $"{Sim.Flat(p.transform.position)}");

                // try to leave: an outward pull, then sprinting and dashing straight out
                var outward = Sim.Flat(p.transform.position).normalized;
                p.Pull(outward, 8f, 30);
                yield return t.Ticks(40);
                bool inPull = CombatWorld.InPlayerBound(p.transform.position, 0.01f);
                t.In(p).Heading = outward; t.In(p).Sprint = true;
                yield return t.Ticks(20); t.In(p).Dash(); yield return t.Ticks(40);
                t.In(p).Stop();
                t.Check($"{tag}: pulls, sprints and dashes can't take the player off the floor", inPull && CombatWorld.InPlayerBound(p.transform.position, 0.01f),
                    $"{Sim.Flat(p.transform.position)}");
                t.Place(p, far);

                // the attacks meant to reach the wall reach the far spot (boss at the centre, as in Forge Rite)
                t.Boss.transform.position = Vector3.zero;
                t.Boss.Rite.AnvilQuake();
                t.Boss.Rite.FurnaceSpin();
                yield return t.Ticks(1);
                var outer = t.Spawns.First(x => x.Source == "Anvil Quake III").H;
                t.Check($"{tag}: the outer Anvil Quake band covers the far spot", outer.Contains(far), $"radius {outer.Radius:0.0}");
                var jets = t.Spawned("Furnace Spin").Select(x => x.H).ToList();
                bool jetsReach = jets.All(j => { return j.Length >= Sim.Flat(far).magnitude && j.Contains(Sim.Dir(j.Yaw) * (Sim.Flat(far).magnitude - 0.1f)); });
                t.Check($"{tag}: every Furnace Spin jet reaches the farthest spot along it", jets.Count == 4 && jetsReach, string.Join("/", jets.Select(j => $"{j.Yaw:0}:{j.Length:0.0}")));
                t.Check($"{tag}: the floor edge along any heading is inside the wall distance", Enumerable.Range(0, 72).All(k =>
                {
                    var d = Sim.Dir(k * 5f); float w = CrucivarAbility.WallDist(Vector3.zero, d) * b / (b + Hazard.Margin);   // from the centre the margin scales with the ray, so this is the floor edge
                    return CombatWorld.InPlayerBound(d * (w - 0.02f), 0.001f) && !CombatWorld.InPlayerBound(d * (w + 0.05f));
                }));
                W(t).ClearTransient();
                yield return t.Ticks(2);

                // a pour aimed at the far spot runs to the wall behind it
                t.Boss.Rite.CastingOrder();
                yield return t.Ticks(1);
                var pour = t.Spawns.First(x => x.Source == "Pour I").H;
                t.Check($"{tag}: a pour aimed at the far spot reaches it", pour.Contains(far), $"length {pour.Length:0.0}");
                t.Check($"{tag}: the crown and eruption size covers the far spot", CrucivarAbility.Far + 1f > Sim.Flat(far).magnitude + Hazard.Margin, $"{CrucivarAbility.Far:0.0}");
                W(t).ClearTransient();
                yield return t.Ticks(2);
            }
            CombatWorld.SetPlayerBound(keepBound, keepSquare);
            t.Check("one hit per instance", t.OneHitPerInstance());
        }

        static CombatWorld W(CrucivarV2AutoTest t) => t.W;
    }
}
