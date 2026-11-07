using System.Collections;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // v4 body clips (tools/crucivar/crucivar_v4_clips.py, 4 Nov): the model carries a clip for every wind-up the fight
    // plays, and a Forge Rite loop plays them without falling back to the procedural placeholder. Visual only.
    public sealed class BodyClipsTest : CrucivarAbilityTest
    {
        public override string Id => "crucivar.v4_body_clips";
        public override int Order => 360;

        static readonly string[] Clips = { "Idle", "Walk", "Intro", "EmberBrand", "FurnaceSpinCW", "FurnaceSpinCCW", "Backdraft", "CastingOrder",
            "Diebreaker", "TurretCall", "PourFlood", "MouldPress", "CrownRise", "Eruption", "Vulnerable", "PhaseShift", "Defeat" };

        public override IEnumerator Run(CrucivarAutoTest t)
        {
            yield return t.Setup(1, at: new[] { new Vector3(0, 0, 6f) });
            if (t.Boss == null) yield break;
            var pr = t.Boss.Presenter;
            var missing = Clips.Where(c => !pr.Has(c)).ToList();
            t.Check("the model carries every v4 body clip", pr.HasModel && missing.Count == 0, missing.Count == 0 ? $"{Clips.Length} clips" : "missing " + string.Join(", ", missing));
            var me = t.Party[0];
            t.Begin(t.Boss.Rite.Loop());
            float s = 0;
            while (s < 14f && t.Boss.TestBusy) { if (me.Hp < 60) me.DebugSetHp(PlayerController.MaxHp); s += Time.deltaTime; yield return null; }
            var fell = pr.Placeholders.Where(c => Clips.Contains(c)).ToList();
            t.Check("a Forge Rite loop plays its clips (no placeholder)", fell.Count == 0, fell.Count == 0 ? "none" : string.Join(", ", fell));
            yield return t.Shot("v4_body_clip");
        }
    }
}
