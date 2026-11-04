using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // -lastbellSoloSweep: one class (-lastbellDemoClass, default the Tollbreaker) played by a Perfect bot against the four
    // main bosses (no minibosses), at 4x. Hard only by default (Chzy18, 1 Nov: the levels only scale damage); -lastbellSweepLevels
    // Easy,Medium,Hard picks others. -lastbellSweepAllies N adds N attacking bot allies (3 = a party of 4); -lastbellSweepOnly
    // Crucivar sweeps only the bosses whose name holds that text; -lastbellSweepRepeat N fights each N times, the bots starting
    // 0.2 s later each time (the fight is deterministic: the same code always plays the same fight). A measurement,
    // not a pass/fail: each fight's result (win or loss, clear time, how far the boss got, what killed him, what hit him
    // most) goes in the report and in solo-sweep.txt.
    public sealed partial class RunAutoTest
    {
        // -lastbellSweepTrace also writes sweep-trace.txt: every hit on every player, downs, phases, where brands and pools
        // appear, and each bot's last moves before a bullet hits it (bot work, 11-03)
        static bool SweepTraceOn => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellSweepTrace", StringComparison.OrdinalIgnoreCase));
        static bool SoloSweepOn => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellSoloSweep", StringComparison.OrdinalIgnoreCase));
        const float SweepFightCap = 480f;   // fight seconds before a fight is called a timeout

        IEnumerator SoloSweep()
        {
            string cls = AurelionArgs.Arg("-lastbellDemoClass") ?? "Tollbreaker";
            int allies = int.TryParse(AurelionArgs.Arg("-lastbellSweepAllies"), out var na) ? Mathf.Clamp(na, 0, 3) : 0;
            int repeats = int.TryParse(AurelionArgs.Arg("-lastbellSweepRepeat"), out var nr) ? Mathf.Clamp(nr, 1, 20) : 1;
            var levels = (AurelionArgs.Arg("-lastbellSweepLevels") ?? "Hard").Split(',').Select(s => (Difficulty)Enum.Parse(typeof(Difficulty), s.Trim(), true)).ToArray();
            var rows = new List<string> { $"solo sweep: {cls}, Perfect bot, {(allies == 0 ? "no allies" : allies + " attacking bot allies")}, 4x · {DateTime.Now:yyyy-MM-dd HH:mm}", "boss | level | result | time | boss hp left | cause | hits taken (most first)" };
            var taken = new Dictionary<string, int>();
            var trace = new List<string>(); int tt0 = 0; BossEncounter tb = null; bool traceOn = SweepTraceOn; var recent = new Dictionary<PlayerController, Queue<string>>();
            void Took(PlayerController p, Hazard h, int dmg) { if (p == d.Player) { taken.TryGetValue(h.Source, out int n); taken[h.Source] = n + 1; } if (!traceOn) return;
                if (tb && h.Track != null && recent.TryGetValue(p, out var rq)) { while (rq.Count > 0) trace.Add("      | " + rq.Dequeue()); }
                if (tb) trace.Add($"{(d.World.Tick - tt0) / (float)Sim.Hz,6:0.0} P{tb.PhaseNumber} p{d.World.Players.IndexOf(p)} hit {h.Source} -{dmg} hp {p.Hp} r {Sim.Flat(p.transform.position - tb.transform.position).magnitude:0.0} age {h.Age} lock {h.LockAt} res {h.ResolveAt}"); }
            for (int e = 0; e < RunDirector.Bosses.Length; e++)
                foreach (var (level, rep) in levels.SelectMany(l => Enumerable.Range(0, repeats).Select(r => (l, r))))
                {
                    string name = RunDirector.Bosses[e].Name;
                    if (name.Contains("(practice)")) continue;   // minibosses are not swept (Chzy18, 2 Nov)
                    var only = AurelionArgs.Arg("-lastbellSweepOnly"); if (only != null && !name.Contains(only)) continue;   // one boss by name
                    if (d.State != RunState.Menu) { d.SetPaused(true); d.ReturnToMenu(); yield return null; }
                    d.SelectDifficulty(level); d.SelectAllies(allies); AllyBot.StartDelay = rep * Sim.Ticks(0.2f);
                    AllyBot.AllyMode = BotMode.Perfect; AllyBot.Attacks = true;
                    var me = new AllyBot(0, 1, BotMode.Perfect);
                    d.InputOverride = me;
                    d.StartPractice(cls, e);
                    yield return Until(() => RunDirector.Instance && RunDirector.Instance.State == RunState.Ready, 30, $"{name} practice ready");
                    if (RunDirector.Instance != d) { directorLog.AddRange(d.Log); d = RunDirector.Instance; }
                    d.InputOverride = me; if (d.Player) d.Player.Input = me;
                    d.RequestReady();
                    yield return Until(() => d.State == RunState.Combat, 15, $"{name} combat");
                    if (d.State != RunState.Combat || !d.Boss) { rows.Add($"{name} | {level}{(repeats > 1 ? " #" + rep : "")} | no fight | - | - | {d.State}"); continue; }
                    var boss = d.Boss; int t0 = d.World.Tick; float real0 = Time.realtimeSinceStartup;
                    taken.Clear(); var world = d.World; world.PlayerHit += Took; tt0 = t0; tb = boss; trace.Add("== " + name + " " + level + " #" + rep + " allies " + allies); var downs = new HashSet<PlayerController>(); int lastPh = 0; var seenHz = new HashSet<Hazard>(); int lastRec = 0, lastSolo = 0, lastPart = -99;
                    Time.timeScale = 4;
                    while (d.State == RunState.Combat && (d.World.Tick - t0) < SweepFightCap * Sim.Hz && Time.realtimeSinceStartup - real0 < 300)
                    {
                        if (boss && boss.PhaseNumber != lastPh) { lastPh = boss.PhaseNumber; trace.Add($"{(d.World.Tick - t0) / (float)Sim.Hz,6:0.0} phase {lastPh} boss {boss.Hp}"); }
                        if (traceOn) foreach (var hz in d.World.Hazards.Live)
                            if ((hz.Source == "Brand Pool" || hz.Source == "Ember Brand") && seenHz.Add(hz))
                                trace.Add($"{(d.World.Tick - t0) / (float)Sim.Hz,6:0.0} P{lastPh} {hz.Source} at r {Sim.Flat(hz.Origin).magnitude:0.0} yaw {Sim.YawTo(Vector3.zero, hz.Origin, 0):0} | " + string.Join(" ", d.World.Players.Select(pp => $"p{d.World.Players.IndexOf(pp)} r{Sim.Flat(pp.transform.position).magnitude:0.0} y{Sim.YawTo(Vector3.zero, pp.transform.position, 0):0} {(pp.Input as AllyBot)?.DebugLine}")));
                        if (traceOn && lastPh == 1 && (d.World.Tick - t0) < 30 * Sim.Hz && d.World.Tick % 12 == 0 && d.World.Hazards.Live.Any(hz => hz.Source == "Ember Brand"))
                            foreach (var p in d.World.Players) trace.Add($"{(d.World.Tick - t0) / (float)Sim.Hz:0.00}   brand p{d.World.Players.IndexOf(p)} r{Sim.Flat(p.transform.position).magnitude:0.0} y{Sim.YawTo(Vector3.zero, p.transform.position, 0):0} {(p.Input as AllyBot)?.DebugLine}");
                        if (traceOn && d.World.Parts.Count > 0 && d.World.Tick - lastPart >= Sim.Hz && (lastPart = d.World.Tick) > 0)   // boss parts each second: kill order, HP, who is nearest
                            trace.Add($"{(d.World.Tick - t0) / (float)Sim.Hz,6:0.0} P{lastPh} parts " + string.Join(" ", d.World.Parts.Select(pt => $"[{pt.Name} o{BotSight.PartOrder(pt)} {BotSight.PartHp(pt):0}/{BotSight.PartMaxHp(pt):0} {(pt.Alive ? "up" : "sunk")} near {string.Join("", d.World.Players.Where(pp => pp && !pp.Down && Sim.Flat(pp.transform.position - pt.Pos).magnitude < pt.BodyRadius + 4.5f).Select(pp => "p" + d.World.Players.IndexOf(pp)))}]")) + " | " + string.Join(" ", d.World.Players.Select(pp => $"p{d.World.Players.IndexOf(pp)} {(pp.Down ? "down" : (pp.Input as AllyBot)?.Brief)}")));
                        if (traceOn && lastPh >= 2 && d.World.Tick - lastRec >= 4)
                            foreach (var p in d.World.Players)
                            {
                                lastRec = d.World.Tick;
                                if (!p || p.Down) continue;
                                if (!recent.TryGetValue(p, out var q)) recent[p] = q = new Queue<string>();
                                var dl = p.Input is AllyBot ab ? ab.DebugLine : null;
                                q.Enqueue($"{(d.World.Tick - t0) / (float)Sim.Hz:0.00} p{d.World.Players.IndexOf(p)} ({p.transform.position.x:0.0},{p.transform.position.z:0.0}) hp {p.Hp} st {p.Stamina:0} stun {p.Stunned} {dl}");
                                while (q.Count > 16) q.Dequeue();
                                if (d.World.Players.Count == 1 && d.World.Tick - lastSolo >= 30) { lastSolo = d.World.Tick; var tgt = d.World.Parts.Where(bp => bp.Alive).OrderBy(bp => bp.Name, StringComparer.Ordinal).FirstOrDefault(); trace.Add("  solo " + q.Last() + " [" + p.Kit.ClassState() + "]" + (tgt != null ? $" | {tgt.Name} at ({tgt.Pos.x:0.0},{tgt.Pos.z:0.0}) gap {Sim.Flat(tgt.Pos - p.transform.position).magnitude - tgt.BodyRadius:0.0}" : "")); }
                            }
                        foreach (var p in d.World.Players) if (p && p.Down && downs.Add(p)) trace.Add($"{(d.World.Tick - t0) / (float)Sim.Hz,6:0.0} P{lastPh} p{d.World.Players.IndexOf(p)} DOWN by {d.World.LastDamageSource}");
                        yield return null;
                    }
                    Time.timeScale = 1;
                    world.PlayerHit -= Took;
                    string hitList = string.Join(", ", taken.OrderByDescending(kv => kv.Value).Take(4).Select(kv => $"{kv.Key} x{kv.Value}"));
                    int secs = (d.World.Tick - t0) / Sim.Hz;
                    string result = boss && boss.Dead ? "WIN" : d.State == RunState.Combat ? "TIMEOUT" : "LOSS";
                    string left = boss ? $"{100f * Mathf.Max(0, boss.Hp) / Mathf.Max(1, boss.MaxHp):0}%" : "-";
                    string cause = result == "LOSS" ? d.DefeatCause ?? d.World.LastDamageSource ?? "?" : "";
                    string row = $"{name} | {level}{(repeats > 1 ? " #" + rep : "")} | {result} | {secs / 60}:{secs % 60:00} | {left} | {cause} | {hitList}";
                    rows.Add(row); Note("solo sweep: " + row);
                    yield return null;
                }
            if (d.State != RunState.Menu) { d.SetPaused(true); d.ReturnToMenu(); yield return null; }
            AllyBot.AllyMode = BotMode.Perfect; AllyBot.Attacks = false; AllyBot.StartDelay = 0; d.SelectAllies(0); d.InputOverride = bot;
            if (traceOn) try { File.WriteAllLines(Path.Combine(OutDir, "sweep-trace.txt"), trace); } catch { }
            try { File.WriteAllLines(Path.Combine(OutDir, "solo-sweep.txt"), rows); } catch (Exception ex) { Note("solo sweep: could not write the table: " + ex.Message); }
        }
    }
}
