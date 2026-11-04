using System.Collections;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Party run with the menu's bots: AllyBot (Perfect, no attacks) plays one Molten Crown cycle, solo and with 4. The
    // generic soak job (AllyBot, 11-01) must walk them into the Mould Towers: every set lands full (no Molten Overflow) and
    // nobody is an extra. The Run loop and shop smoke run found bots stepping out of Crucivar's towers (no Soak flag).
    public sealed class BotSoakTest : CrucivarAbilityTest
    {
        public override string Id => "crucivar.v4_bot_soak";
        public override int Order => 340;

        public override IEnumerator Run(CrucivarAutoTest t)
        {
            foreach (int n in new[] { 1, 4 })
            {
                yield return t.Setup(n, at: Enumerable.Range(0, n).Select(i => Sim.Dir(90f * i) * 9f).ToArray());
                if (t.Boss == null) yield break;
                var crown = t.Boss.Crown; int wipes0 = crown.Wipes;
                for (int i = 0; i < n; i++) t.Party[i].Input = new AllyBot(i, n, BotMode.Perfect);
                t.Begin(crown.Run());
                float s = 0;
                // heal the other attacks (Backdraft, brands, the turret): this checks the soaks only
                while (s < 26f && t.Boss.TestBusy) { foreach (var p in t.Party) if (p.Hp < 60) p.DebugSetHp(PlayerController.MaxHp); s += Time.deltaTime; yield return null; }
                foreach (var p in t.Party) p.Input = t.In(p);
                var towers = t.Spawned("Mould Tower").ToList();
                t.Check($"{n} bot{(n > 1 ? "s" : "")}: every Mould Tower lands full (no Molten Overflow)", towers.Count == (n == 1 ? 3 : 4) && crown.Wipes == wipes0 && t.Party.All(p => !p.Down),
                    $"{towers.Count} towers, wipes {crown.Wipes - wipes0}, down {t.Party.Count(p => p.Down)}");
                t.Check($"{n} bot{(n > 1 ? "s" : "")}: no extra in a tower", !t.Hits.Any(h => h.Source.StartsWith("Mould Tower")),
                    string.Join(",", t.Hits.Where(h => h.Source.StartsWith("Mould Tower")).Select(h => h.Source)));
            }
        }
    }
}
