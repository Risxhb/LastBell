using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // -lastbellAurelionV4Live (with -lastbellAurelionOnly): a real v4 fight from the menu, the way a player starts it:
    // Hard, practice, no test hold, the player driven by the Perfect bot (with AllyBot.Attacks, so the boss takes
    // damage and every phase and transition plays). Logs each phase, the wipe or the kill, and the time. Run it with
    // -lastbellBossHp to keep it short.
    public sealed partial class RunAutoTest
    {
        static bool AurelionV4Live => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellAurelionV4Live", StringComparison.OrdinalIgnoreCase));

        IEnumerator AurelionV4LiveFight()
        {
            Note("---------- Aurelion v4 live fight (menu > Hard > practice) ----------");
            AurelionArgs.SetOverride = null; AurelionArgs.SoloOverride = null;
            if (d.State != RunState.Menu) { d.SetPaused(true); d.ReturnToMenu(); yield return null; }
            d.SelectDifficulty(Difficulty.Hard);
            var ally = new AllyBot(0, 1, BotMode.Perfect) { HoldRange = V3Ranged };
            d.InputOverride = ally;
            d.StartPractice("Arcanist", RunDirector.AurelionPractice);
            int spire = RunMaps.IndexOf(RunDirector.AurelionMap);
            yield return Until(() => RunMaps.Current == spire && RunDirector.Instance && RunDirector.Instance.State == RunState.Ready, 30, "Skyward Spire practice ready");
            if (RunDirector.Instance != d) { directorLog.AddRange(d.Log); d = RunDirector.Instance; }
            d.InputOverride = ally; if (d.Player) d.Player.Input = ally;
            d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 15, "combat");
            var b = d.Boss as AurelionEncounter;
            Check("v4 live: Aurelion uses the v4 set by default", b != null && AurelionArgs.V4, b == null ? "no boss" : "");
            if (b == null) yield break;
            Note($"v4 live: boss HP {b.MaxHp}, difficulty {d.World.Difficulty}");
            bool was = AllyBot.Attacks; AllyBot.Attacks = true;
            Time.timeScale = 4;
            string last = ""; float t = 0;
            while (t < 480 && d.State == RunState.Combat)
            {
                if (b.PhaseName != last) { last = b.PhaseName; Note($"v4 live @{b.FightTicks / (float)Sim.Hz:0.0}s: {last} (HP {b.Hp})"); }
                t += Time.deltaTime; yield return null;
            }
            Time.timeScale = 1; AllyBot.Attacks = was;
            Note($"v4 live end @{b.FightTicks / (float)Sim.Hz:0.0}s: state {d.State}, boss {(b.Dead ? "dead" : "alive " + b.Hp)}, wipe {b.WipeCause ?? "-"}, player potions {d.Player?.PotionsDrunk}, last hit {d.World.LastDamageSource}");
            Check("v4 live: the fight ran to an end without an error", d.State != RunState.Combat, d.State.ToString());
            d.InputOverride = bot;
        }
    }
}
