using System.Collections;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Bot allies (RunParty, menu "Allies"): a full party of 4 spawns with the classes spread; the bots are passive (they move,
    // dodge and spread but never attack), so the boss keeps its solo HP and takes no damage from them.
    public sealed partial class RunAutoTest
    {
        static bool PartyOnly => System.Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellPartyOnly", System.StringComparison.OrdinalIgnoreCase));

        IEnumerator PartyChecks()
        {
            Note("---------- bot allies ----------");
            if (d.State != RunState.Menu) { d.SetPaused(true); d.ReturnToMenu(); yield return null; }
            // passive menu allies: the final entry as an idle target on this map (the live Ultimate drives its allies with its strat bots)
            bool idle = BellkeeperEncounter.TestIdle, stay = RunDirector.PracticeStaysOnMap; BellkeeperEncounter.TestIdle = true; RunDirector.PracticeStaysOnMap = true;
            // solo baseline for the HP check
            Check("allies: selectable from the menu", d.SelectAllies(0) && RunParty.Selected == 0);
            d.StartPractice("Arcanist", 0); yield return PartyLoaded("solo"); d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 4, "solo practice combat");
            int soloHp = d.Boss ? d.Boss.MaxHp : 0;
            Check("allies: solo practice has one player", d.World.Players.Count == 1 && d.Allies.Count == 0);
            d.SetPaused(true); d.ReturnToMenu(); yield return null;

            Check("allies: 3 selected (the most)", d.SelectAllies(RunParty.MaxAllies) && RunParty.Selected == 3);
            d.StartPractice("Arcanist", 0); yield return PartyLoaded("party of 4");
            Check("allies: a party of 4 spawns", d.World.Players.Count == 4 && d.Allies.Count == 3, $"{d.World.Players.Count} players");
            var classes = d.World.Players.GroupBy(p => p.ClassName).ToDictionary(g => g.Key, g => g.Count());
            Check("allies: classes spread across all three (no class more than 2 of 4)", classes.Count == 3 && classes.Values.Max() <= 2,
                string.Join(", ", classes.Select(kv => kv.Key + " " + kv.Value)));
            Check("allies: the first allies are the classes the player didn't pick", d.Allies[0].ClassName == "Tollbreaker" && d.Allies[1].ClassName == "Chimesinger");
            d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 4, "party practice combat");
            Check("allies: passive bots don't raise the boss's HP (only fighting players count)", d.Boss && d.Boss.MaxHp == soloHp, $"solo {soloHp}, party {d.Boss?.MaxHp}");
            int hp0 = d.Boss.Hp, dealt0 = d.World.DamageDealt;
            // the test player only walks away and dodges, and the bots only do the mechanics: nobody damages the boss
            yield return Survive(14f);
            Check("allies: the bots never attack (mechanics only)", d.World.DamageDealt == dealt0 && d.Boss.Hp == hp0, $"dealt {d.World.DamageDealt - dealt0}");
            int standing = d.Allies.Count(a => a && !a.Down);
            Note($"allies after 14 s with the boss: {standing} of 3 standing, HP " + string.Join(" ", d.Allies.Select(a => a.ClassName[0] + ":" + a.Hp)));
            Check("allies: most bots survive 14 s (they dodge)", standing >= 2, standing + " of 3");
            if (d.Boss && !d.Boss.Dead) { if (d.Boss.PhaseNumber < d.Boss.PhaseCount) d.Boss.StartAtPhase(d.Boss.PhaseCount); d.Boss.DebugDefeat(); }   // a miniboss holds at 50 % until its shift
            yield return Until(() => d.State == RunState.Results, 8, "party practice results");
            d.ReturnToMenu(); yield return null;
            BellkeeperEncounter.TestIdle = false; RunDirector.PracticeStaysOnMap = false;
            // the Ultimate: menu allies are its Perfect strat bots, which also attack
            Check("allies: 3 selected for the Ultimate", d.SelectAllies(3) && RunParty.Selected == 3);
            BellkeeperEncounter.PhaseOverride = null;   // a practice phase left by an earlier check
            d.StartPractice("Arcanist", RunDirector.BellkeeperUltimatePractice); yield return PartyLoaded("Ultimate party of 4"); d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 4, "Ultimate party combat");
            Check("allies: the Ultimate drives menu allies with its strat bot", d.Allies.Count == 3 && d.Allies.All(a => a.Input is UltimateBot),
                string.Join(",", d.Allies.Select(a => a.Input?.GetType().Name)));
            yield return Survive(6f);
            if (d.Boss && !d.Boss.Dead) d.Boss.DebugDefeat();
            yield return Until(() => d.State == RunState.Results, 8, "Ultimate party results");
            d.ReturnToMenu(); yield return null;
            Check("allies: back to solo for the rest of the test", d.SelectAllies(0) && RunParty.Selected == 0);
            BellkeeperEncounter.TestIdle = idle; RunDirector.PracticeStaysOnMap = stay;
        }

        // a practice may load its boss's map, which builds a new director: follow it and re-attach the test bot
        IEnumerator PartyLoaded(string label)
        {
            yield return Until(() => RunDirector.Instance && RunDirector.Instance.State == RunState.Ready, 30, label + " practice loaded");
            if (RunDirector.Instance != d) { directorLog.AddRange(d.Log); d = RunDirector.Instance; d.InputOverride = bot; bot.Clear(); }
            if (d.Player) d.Player.Input = bot;
        }
    }
}
