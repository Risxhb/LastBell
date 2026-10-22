using System.Collections;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Bot allies (RunParty, menu "Allies"): a party of 8 spawns with the classes spread; the bots are passive (they move,
    // dodge and spread but never attack), so the boss keeps its solo HP and takes no damage from them.
    public sealed partial class RunAutoTest
    {
        static bool PartyOnly => System.Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellPartyOnly", System.StringComparison.OrdinalIgnoreCase));

        IEnumerator PartyChecks()
        {
            Note("---------- bot allies ----------");
            if (d.State != RunState.Menu) { d.SetPaused(true); d.ReturnToMenu(); yield return null; }
            // solo baseline for the HP check
            Check("allies: selectable from the menu", d.SelectAllies(0) && RunParty.Selected == 0);
            d.StartPractice("Arcanist", 0); d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 4, "solo practice combat");
            int soloHp = d.Boss ? d.Boss.MaxHp : 0;
            Check("allies: solo practice has one player", d.World.Players.Count == 1 && d.Allies.Count == 0);
            d.SetPaused(true); d.ReturnToMenu(); yield return null;

            Check("allies: 7 selected", d.SelectAllies(7) && RunParty.Selected == 7);
            d.StartPractice("Arcanist", 0);
            Check("allies: a party of 8 spawns", d.World.Players.Count == 8 && d.Allies.Count == 7, $"{d.World.Players.Count} players");
            var classes = d.World.Players.GroupBy(p => p.ClassName).ToDictionary(g => g.Key, g => g.Count());
            Check("allies: classes spread across all three (no class more than 3 of 8)", classes.Count == 3 && classes.Values.Max() <= 3,
                string.Join(", ", classes.Select(kv => kv.Key + " " + kv.Value)));
            Check("allies: the first allies are the classes the player didn't pick", d.Allies[0].ClassName == "Vanguard" && d.Allies[1].ClassName == "Chimesinger");
            d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 4, "party practice combat");
            Check("allies: passive bots don't raise the boss's HP (only fighting players count)", d.Boss && d.Boss.MaxHp == soloHp, $"solo {soloHp}, party {d.Boss?.MaxHp}");
            int hp0 = d.Boss.Hp, dealt0 = d.World.DamageDealt;
            // the test player only walks away and dodges, and the bots only do the mechanics: nobody damages the boss
            yield return Survive(14f);
            Check("allies: the bots never attack (mechanics only)", d.World.DamageDealt == dealt0 && d.Boss.Hp == hp0, $"dealt {d.World.DamageDealt - dealt0}");
            int standing = d.Allies.Count(a => a && !a.Down);
            Note($"allies after 14 s with the Bellkeeper: {standing} of 7 standing, HP " + string.Join(" ", d.Allies.Select(a => a.ClassName[0] + ":" + a.Hp)));
            Check("allies: most bots survive 14 s (they dodge)", standing >= 4, standing + " of 7");
            if (d.Boss && !d.Boss.Dead) d.Boss.DebugDefeat();
            yield return Until(() => d.State == RunState.Results, 8, "party practice results");
            d.ReturnToMenu(); yield return null;
            Check("allies: back to solo for the rest of the test", d.SelectAllies(0) && RunParty.Selected == 0);
        }
    }
}
