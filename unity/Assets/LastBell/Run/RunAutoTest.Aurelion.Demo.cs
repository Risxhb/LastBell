using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // -lastbellAurelionDemo (with -lastbellAurelionOnly): a watchable v4 fight at normal speed. The player and three
    // allies are all AllyBot Perfect (they know the Ecliptic order and their marks, take their towers, wells and sides)
    // and attack, on Hard. Use -lastbellBossHp to keep it short. The window stays up 10 s after the fight ends.
    public sealed partial class RunAutoTest
    {
        static bool AurelionDemo => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellAurelionDemo", StringComparison.OrdinalIgnoreCase));

        IEnumerator AurelionDemoFight()
        {
            Note("---------- Aurelion v4 demo: 4 perfect bots, Hard ----------");
            AurelionArgs.SetOverride = null; AurelionArgs.SoloOverride = null;
            if (d.State != RunState.Menu) { d.SetPaused(true); d.ReturnToMenu(); yield return null; }
            d.SelectDifficulty(Difficulty.Hard);
            d.SelectAllies(3);
            AllyBot.AllyMode = BotMode.Perfect; AllyBot.Attacks = true;
            var me = new AllyBot(3, 4, BotMode.Perfect);
            d.InputOverride = me;
            d.StartPractice("Arcanist", RunDirector.AurelionPractice);
            int spire = RunMaps.IndexOf(RunDirector.AurelionMap);
            yield return Until(() => RunMaps.Current == spire && RunDirector.Instance && RunDirector.Instance.State == RunState.Ready, 30, "Skyward Spire practice ready");
            if (RunDirector.Instance != d) { directorLog.AddRange(d.Log); d = RunDirector.Instance; }
            d.InputOverride = me; if (d.Player) d.Player.Input = me;
            d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 15, "combat");
            var b = d.Boss as AurelionEncounter;
            if (b == null) yield break;
            Note($"demo: party of {d.World.Players.Count} ({string.Join(", ", d.World.Players.Select(p => p.ClassName))}), boss HP {b.MaxHp}");
            string last = ""; float t = 0;
            while (t < 480 && d.State == RunState.Combat)
            {
                if (b.PhaseName != last) { last = b.PhaseName; Note($"demo @{b.FightTicks / (float)Sim.Hz:0.0}s: {last}"); }
                t += Time.deltaTime; yield return null;
            }
            Note($"demo end @{b.FightTicks / (float)Sim.Hz:0.0}s: {(b.Dead ? "boss defeated" : "party down: " + (b.WipeCause ?? d.World.LastDamageSource))}; standing {d.World.Players.Count(p => !p.Down)} of {d.World.Players.Count}");
            Check("demo: the fight ran to an end", d.State != RunState.Combat, d.State.ToString());
            t = 0; while (t < 10) { t += Time.deltaTime; yield return null; }
            AllyBot.AllyMode = BotMode.Passive; AllyBot.Attacks = false;
            d.SelectAllies(0);
            d.InputOverride = bot;
        }
    }
}
