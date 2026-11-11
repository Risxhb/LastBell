using System.Collections;
using UnityEngine;

namespace LastBell.Run
{
    // Revives in burning ground (RI-152, accepted 9 Nov; review CRU-BURN-OVERLAP, 10 Nov): a Brand Pool burns a reviver but
    // its ticks do not reset the revive; any other burn the reviver also stands in (a Pour Channel) still resets it, even
    // when the pool is older and burns as hard (damage stays the one highest rate).
    public sealed class BurnReviveTest : CrucivarAbilityTest
    {
        public override string Id => "crucivar.v4_burn_revive";
        public override int Order => 370;

        static Hazard Pool(Vector3 at, bool keeps, string name) => new Hazard
        {
            Source = name, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = at, Radius = 2f, LockAt = 0,
            ResolveAt = Sim.Ticks(30f), BurnPerSecond = 20f, BurnKeepsRevive = keeps,
        };

        public override IEnumerator Run(CrucivarAutoTest t)
        {
            var at = new Vector3(-9f, 0, 0);
            yield return t.Setup(2, at: new[] { at, at + new Vector3(0.8f, 0, 0) });
            if (t.Boss == null) yield break;
            var down = t.Party[0]; var rv = t.Party[1];

            // a Brand Pool alone: the reviver burns, the revive still finishes
            down.ForceDown("Crucivar test: burn revive");
            t.W.Hazards.Spawn(Pool(at, true, "Brand Pool"));
            yield return t.Until(() => rv.Reviving == down, 3f, "reviver channelling");
            int hp0 = rv.Hp;
            yield return t.Seconds(PlayerController.ReviveSeconds + 0.6f);
            t.Check("a Brand Pool burns a reviver but does not reset the revive (RI-152)", !down.Down && rv.Hp < hp0, $"down {down.Down}, reviver hp {hp0} -> {rv.Hp}");

            // an older Brand Pool and a Pour Channel at the same 20/s: the channel still resets the revive
            t.W.ClearTransient();
            rv.DebugSetHp(PlayerController.MaxHp);
            down.ForceDown("Crucivar test: burn revive overlap");
            t.W.Hazards.Spawn(Pool(at, true, "Brand Pool"));
            t.W.Hazards.Spawn(Pool(at, false, "Pour Channel"));
            yield return t.Until(() => rv.Reviving == down, 3f, "reviver channelling");
            hp0 = rv.Hp;
            yield return t.Seconds(PlayerController.ReviveSeconds + 0.6f);
            t.Check("a Pour Channel under or over a Brand Pool still resets the revive (CRU-BURN-OVERLAP)", down.Down && down.ReviveProgress < 0.2f,
                $"down {down.Down}, progress {down.ReviveProgress:0.00}");
            t.Check("overlapping burns never add up: 20/s, not 40/s", hp0 - rv.Hp <= Mathf.CeilToInt(20f * (PlayerController.ReviveSeconds + 0.6f)) + 2, $"reviver hp {hp0} -> {rv.Hp}");
            t.W.ClearTransient();
            down.DebugRevive();
        }
    }
}
