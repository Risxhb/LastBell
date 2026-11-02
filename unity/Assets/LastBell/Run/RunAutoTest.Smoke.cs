using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Quick smoke run (Chzy18, 2026-11-01: test less): about 5 minutes, for routine checks while building. The full
    // autotest stays the one check before a push. It covers: the menus answer, every boss on the practice list starts and
    // fights about 20 s (each class takes a turn; the first fight is solo, the rest have a full party of 4 whose bots do the
    // mechanics, since a lone bot can't soak), the Start Phase picker, a
    // potion, and no errors logged.
    //   LastBell.exe -lastbellAutotest -lastbellSmokeOnly -lastbellOut <dir>
    public sealed partial class RunAutoTest
    {
        static bool SmokeOnly => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellSmokeOnly", StringComparison.OrdinalIgnoreCase));
        const float SmokeFightSeconds = 20f;

        IEnumerator SmokeChecks()
        {
            Note("---------- smoke run ----------");
            float t0 = Time.realtimeSinceStartup;

            // menus: the run menu's map, difficulty and allies rows, the Boss Rush cards, the icons
            Check($"smoke: boots into the menu on a run map ({RunMaps.CurrentName})", d.State == RunState.Menu && RunMaps.Current >= 0);
            Check("smoke: every map is in the build", Enumerable.Range(0, RunMaps.All.Length).All(RunMaps.InBuild));
            Check("smoke: difficulty can be chosen", d.SelectDifficulty(Difficulty.Hard) && RunDifficulty.Selected == Difficulty.Hard && d.SelectDifficulty(Difficulty.Medium));
            Check("smoke: allies go up to 3 (1-4 players) and clamp there", d.SelectAllies(3) && RunParty.Selected == 3 && d.SelectAllies(7) && RunParty.Selected == 3 && RunParty.MaxAllies == 3);
            Check("smoke: allies back to solo", d.SelectAllies(0) && RunParty.Selected == 0);
            Check("smoke: every boss has a Boss Rush card name", Enumerable.Range(0, RunDirector.Bosses.Length).All(e => !string.IsNullOrEmpty(LastBell.Menu.TestRushSetup.CardName(e))));
            Check("smoke: skill-bar and potion icons load", RunDirector.Classes.All(c => RunHud.SlotIcon(c, 0) != null) && RunHud.Icon("potion") != null);
            yield return Shot("smoke_menu");

            // every boss: start, fight ~20 s with the bot, check it took damage (the potion check rides on the solo first fight)
            for (int e = 0; e < RunDirector.Bosses.Length; e++)
                yield return SmokeFight(e, RunDirector.Classes[e % RunDirector.Classes.Length], allies: e == 0 ? 0 : RunParty.MaxAllies, potion: e == 0);

            // Start Phase picker: the final boss's last phase (with a retry) and one area boss's last phase
            int bk = Array.FindIndex(RunDirector.Bosses, b => b.Final);
            yield return PracticeAtPhase(bk, RunDirector.Bosses[bk].Phases, true);
            int area = Array.FindIndex(RunDirector.Bosses, b => !b.Final && b.InRun);
            if (area >= 0) yield return PracticeAtPhase(area, RunDirector.Bosses[area].Phases, false);
            yield return PhaseToMenu();

            Note($"smoke run took {Time.realtimeSinceStartup - t0:0} s");
        }

        IEnumerator SmokeFight(int e, string cls, int allies, bool potion)
        {
            yield return PhaseToMenu();
            string tag = "smoke: " + LastBell.Menu.TestRushSetup.CardName(e);
            d.SelectAllies(allies);
            Check($"{tag}: practice accepted ({cls}{(allies > 0 ? $", {allies} allies" : "")})", d.StartPractice(cls, e));
            yield return Until(() => RunDirector.Instance && RunDirector.Instance.State == RunState.Ready && RunDirector.Instance.Encounter == e, 30, tag + " ready");
            if (RunDirector.Instance != d) { directorLog.AddRange(d.Log); d = RunDirector.Instance; d.InputOverride = bot; bot.Clear(); }
            if (d.Player) d.Player.Input = bot;   // a new scene built the player before the bot was attached
            if (allies > 0) Check($"{tag}: a party of {allies + 1} spawns", d.World.Players.Count == allies + 1 && d.Allies.Count == allies, $"{d.World.Players.Count} players");
            d.RequestReady();
            yield return Until(() => d.State == RunState.Combat && d.Boss, 8, tag + " combat");
            if (d.State != RunState.Combat || !d.Boss) { Check($"{tag}: fight starts", false, $"state {d.State}"); yield break; }

            // test setup: the player is driven by the Perfect ally bot with attacks on (it soaks towers and does the
            // mechanics, which a lone test bot can't), and is topped up after every hit: this checks that the fight runs,
            // not survival. A wipe still ends the fight: then it is retried until it has had its 20 s (at most 5 tries; Crucivar's
            // Mould Press towers aren't marked Soak yet, so the bots leave them and the press wipes the party at 4 s),
            // which also exercises the retry.
            var me = new AllyBot(allies, allies + 1, BotMode.Perfect); bool attacks = AllyBot.Attacks; AllyBot.Attacks = true;
            float t = 0; int dealt = 0, tries = 0;
            while (t < SmokeFightSeconds && tries < 5 && d.State == RunState.Combat && d.Boss)
            {
                tries++; var b = d.Boss; int hp0 = b.Hp;
                d.InputOverride = me; if (d.Player) d.Player.Input = me;
                while (t < SmokeFightSeconds && d.State == RunState.Combat)
                {
                    if (d.Player && !d.Player.Down && d.Player.Hp < d.Player.MaxHealth) d.Player.DebugSetHp(d.Player.MaxHealth);
                    t += Time.deltaTime; yield return null;
                }
                d.InputOverride = bot; if (d.Player) d.Player.Input = bot; bot.Clear();
                dealt += hp0 - (b ? b.Hp : hp0);
                if (d.State == RunState.Combat) break;
                Note($"{tag}: try {tries} ended at {t:0.0} s ({d.State}{(d.Player && d.Player.Down ? ", player down: " + d.World.LastDamageSource : "")}); retrying");
                yield return Until(() => d.State == RunState.Results, 8, tag + " results before the retry");
                if (d.State != RunState.Results || !d.RequestRetry()) break;
                d.RequestReady();
                yield return Until(() => d.State == RunState.Combat && d.Boss, 8, tag + " retry combat");
            }
            AllyBot.Attacks = attacks; d.InputOverride = bot; if (d.Player) d.Player.Input = bot;
            Check($"{tag}: fights about {SmokeFightSeconds:0} s and takes damage", t >= SmokeFightSeconds && dealt > 0, $"{t:0.0} s in {tries} {(tries == 1 ? "try" : "tries")}, {dealt} damage, state {d.State}");
            if (d.State == RunState.Combat && potion) yield return SmokePotion(tag);

            // test teardown: a miniboss holds at 50 % until its shift, so DebugDefeat in phase 1 needs the last phase first
            if (d.State == RunState.Combat && d.Boss && !d.Boss.Dead) { if (d.Boss.PhaseNumber < d.Boss.PhaseCount) d.Boss.StartAtPhase(d.Boss.PhaseCount); d.Boss.DebugDefeat(); }
            { float w = 0; while (d.State != RunState.Results && w < 8) { w += Time.deltaTime; yield return null; } }
            yield return PhaseToMenu();   // a boss mid-transition may not die at once: then pause and leave instead
            d.SelectAllies(0);
        }

        IEnumerator SmokePotion(string tag)
        {
            d.Boss.Halt();
            yield return Until(() => d.World.Hazards.Count == 0, 6, tag + " quiet for the potion");
            yield return Until(() => d.Player.Stunned == 0, 2, tag + " hit-stun over");   // drinking is blocked while stunned
            int pots = d.Player.Potions;
            d.Player.DebugSetHp(50); bot.potion = true; yield return Wait(0.1f);
            Check("smoke: a potion heals and spends one", d.Player.Hp > 50 && d.Player.Potions == pots - 1, $"hp {d.Player.Hp}, potions {pots} -> {d.Player.Potions}");
        }
    }
}
