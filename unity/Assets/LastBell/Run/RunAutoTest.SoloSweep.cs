using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // -lastbellSoloSweep: one class (-lastbellDemoClass, default the Tollbreaker) played solo by a Perfect bot, no allies,
    // against every practice boss on Easy, Medium and Hard, at 4x. A measurement, not a pass/fail: each fight's result
    // (win or loss, clear time, how far the boss got, what killed him) goes in the report and in solo-sweep.txt.
    public sealed partial class RunAutoTest
    {
        static bool SoloSweepOn => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellSoloSweep", StringComparison.OrdinalIgnoreCase));
        const float SweepFightCap = 480f;   // fight seconds before a fight is called a timeout

        IEnumerator SoloSweep()
        {
            string cls = AurelionArgs.Arg("-lastbellDemoClass") ?? "Tollbreaker";
            var rows = new List<string> { $"solo sweep: {cls}, Perfect bot, no allies, 4x · {DateTime.Now:yyyy-MM-dd HH:mm}", "boss | level | result | time | boss hp left | cause" };
            for (int e = 0; e < RunDirector.Bosses.Length; e++)
                foreach (var level in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard })
                {
                    string name = RunDirector.Bosses[e].Name;
                    if (d.State != RunState.Menu) { d.SetPaused(true); d.ReturnToMenu(); yield return null; }
                    d.SelectDifficulty(level); d.SelectAllies(0);
                    AllyBot.AllyMode = BotMode.Perfect; AllyBot.Attacks = true;
                    var me = new AllyBot(0, 1, BotMode.Perfect);
                    d.InputOverride = me;
                    d.StartPractice(cls, e);
                    yield return Until(() => RunDirector.Instance && RunDirector.Instance.State == RunState.Ready, 30, $"{name} practice ready");
                    if (RunDirector.Instance != d) { directorLog.AddRange(d.Log); d = RunDirector.Instance; }
                    d.InputOverride = me; if (d.Player) d.Player.Input = me;
                    d.RequestReady();
                    yield return Until(() => d.State == RunState.Combat, 15, $"{name} combat");
                    if (d.State != RunState.Combat || !d.Boss) { rows.Add($"{name} | {level} | no fight | - | - | {d.State}"); continue; }
                    var boss = d.Boss; int t0 = d.World.Tick; float real0 = Time.realtimeSinceStartup;
                    Time.timeScale = 4;
                    while (d.State == RunState.Combat && (d.World.Tick - t0) < SweepFightCap * Sim.Hz && Time.realtimeSinceStartup - real0 < 300) yield return null;
                    Time.timeScale = 1;
                    int secs = (d.World.Tick - t0) / Sim.Hz;
                    string result = boss && boss.Dead ? "WIN" : d.State == RunState.Combat ? "TIMEOUT" : "LOSS";
                    string left = boss ? $"{100f * Mathf.Max(0, boss.Hp) / Mathf.Max(1, boss.MaxHp):0}%" : "-";
                    string cause = result == "LOSS" ? d.DefeatCause ?? d.World.LastDamageSource ?? "?" : "";
                    string row = $"{name} | {level} | {result} | {secs / 60}:{secs % 60:00} | {left} | {cause}";
                    rows.Add(row); Note("solo sweep: " + row);
                    yield return null;
                }
            if (d.State != RunState.Menu) { d.SetPaused(true); d.ReturnToMenu(); yield return null; }
            AllyBot.AllyMode = BotMode.Perfect; AllyBot.Attacks = false; d.SelectAllies(0); d.InputOverride = bot;
            try { File.WriteAllLines(Path.Combine(OutDir, "solo-sweep.txt"), rows); } catch (Exception ex) { Note("solo sweep: could not write the table: " + ex.Message); }
        }
    }
}
