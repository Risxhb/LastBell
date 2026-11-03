using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Boss attack icons (Codex's v4 set, accepted by Chzy18 11-02): every hazard each phase spawns carries a Hazard.AttackId
    // that names a catalogued Crucivar attack with its 256 px icon, so the death recap, tooltips and Down card show the
    // icon with the attack's name. Solo on Hard (Quake Echo, the solo Mould Tower C, Second Press). In Phase 2 the castings
    // are broken as they rise, in numeral order; in Phase 3 a Perfect AllyBot fills the towers so the crown rises.
    public sealed class AttackIconTest : CrucivarAbilityTest
    {
        public override string Id => "crucivar.v4_attack_icons";
        public override int Order => 350;

        public override IEnumerator Run(CrucivarAutoTest t)
        {
            var seen = new HashSet<string>(); var bad = new List<string>();
            foreach (int phase in new[] { 1, 2, 3 })
            {
                yield return t.Setup(1, Difficulty.Hard, at: new[] { new Vector3(0, 0, 6f) });
                if (t.Boss == null) yield break;
                var me = t.Party[0];
                if (phase == 3) me.Input = new AllyBot(0, 1, BotMode.Perfect);
                t.Begin(phase == 1 ? t.Boss.Rite.Loop() : phase == 2 ? t.Boss.Uprising.Run() : t.Boss.Crown.Run());
                float s = 0, until = phase == 1 ? 22f : 40f;
                while (s < until && t.Boss.TestBusy)
                {
                    if (me.Hp < 60) me.DebugSetHp(PlayerController.MaxHp);
                    // Phase 2: once a pattern has fired, break each casting as it stands (solo, the second rises later)
                    if (phase == 2 && s > 9f)
                        foreach (var x in t.Boss.Uprising.Turrets.Where(x => x.Casting.Alive).OrderBy(x => x.K).ToList()) t.W.DamagePart(x.Casting, 99999, x.Casting.Pos, "test");
                    s += Time.deltaTime; yield return null;
                }
                me.Input = t.In(me);
                t.Check($"Phase {phase} ends on its own", !t.Boss.TestBusy, $"{s:0.0} s");
                foreach (var r in t.Spawns)
                {
                    var a = BossAttackIcons.Get(r.H.AttackId);
                    if (a == null || a.boss != "crucivar" || BossAttackIcons.Icon(a) == null) bad.Add($"P{phase} {r.Source} -> {r.H.AttackId ?? "none"}");
                    else seen.Add(a.id);
                }
                foreach (var h in t.Hits) if (BossAttackIcons.Resolve(h.H, t.Boss)?.boss != "crucivar") bad.Add($"P{phase} hit {h.Source}");
            }
            t.Check("every v4 hazard and hit names a Crucivar attack with an icon", bad.Count == 0, bad.Count == 0 ? $"{seen.Count} attacks: {string.Join(", ", seen.OrderBy(x => x))}" : string.Join("; ", bad.Distinct().Take(8)));
            var expect = new[] { "ember_brand", "furnace_spin", "backdraft", "casting_order", "anvil_quake", "quake_echo", "lane_stream", "mould_towers", "tower_slag", "molten_crown", "second_press" };
            var missing = expect.Where(k => !seen.Contains("crucivar." + k)).ToList();
            t.Check("the phases spawn every main attack's ID", missing.Count == 0, missing.Count == 0 ? $"{expect.Length} seen" : "missing " + string.Join(", ", missing));
        }
    }
}
