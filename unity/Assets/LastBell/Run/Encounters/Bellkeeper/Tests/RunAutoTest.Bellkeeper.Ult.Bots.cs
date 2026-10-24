using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Bellkeeper Ultimate, Part 5 of ultimate/PROMPT.md (the Ultimate session): the strat bots (UltimateBot, GUIDE.md) on
    // the Ultimate practice entry. Long runs, never part of the default suite:
    //   -lastbellTestAbility bots_dps    measure DPS per class and phase, solo and 4 bots, and print the HP / push numbers
    //   -lastbellTestAbility bots        full clears: perfect solo and 4 bots must win; Late (+0.25 s) and Miss (one memory
    //                                    assignment per phase) must wipe; decision density per phase vs v2
    //   -lastbellTestAbility bots_video  real-time demo clips of a perfect 4-bot party, one per segment
    // Fights are stepped in exact combat ticks with the director paused (like the rest of the harness), so a 12-minute
    // fight takes a minute or two of real time.
    public sealed partial class RunAutoTest
    {
        static readonly string[] PartyClasses = { "Vanguard", "Chimesinger", "Arcanist", "Vanguard" };
        static readonly BellkeeperEncounter.UltSegment[] HpSegments =
            { BellkeeperEncounter.UltSegment.P1, BellkeeperEncounter.UltSegment.P2, BellkeeperEncounter.UltSegment.P3, BellkeeperEncounter.UltSegment.P4, BellkeeperEncounter.UltSegment.P5 };
        // ULTIMATE.md §9: target phase lengths at perfect DPS, and each phase's share of the bar
        static readonly float[] TargetSeconds = { 105, 130, 110, 115, 65 }, BarShare = { 0.15f, 0.30f, 0.20f, 0.20f, 0.15f };

        sealed class UltFight
        {
            public string Label; public bool Killed, Wiped; public string Cause; public int Ticks, Deaths, Potions;
            public readonly List<(BellkeeperEncounter.UltSegment seg, int tick)> Segments = new List<(BellkeeperEncounter.UltSegment, int)>();
            public readonly Dictionary<BellkeeperEncounter.UltSegment, int> Decisions = new Dictionary<BellkeeperEncounter.UltSegment, int>();
            public readonly List<string> DeathLines = new List<string>();
            public int Dealt; public float Hp01;
            public override string ToString() =>
                $"{Label}: {(Killed ? "KILL" : Wiped ? "WIPE (" + Cause + ")" : "time cap")} at {Ticks / 60f / 60f:0}:{Ticks / 60f % 60:00} · boss {Hp01:P1} left · " +
                $"deaths {Deaths} · potions {Potions} · segments {string.Join(" ", Segments.Select(s => $"{s.seg}@{s.tick / 60f:0}s"))}";
        }
        UltFight ultLast;

        // One stepped fight: n strat bots of `skill` (the run's player is bot 0), from `segment` (null = P1), until the boss
        // dies, the party is down, or capSeconds pass.
        IEnumerator UltRun(string label, int n, UltimateBot.Skill skill, string segment = null, float capSeconds = 1200, string cls0 = "Vanguard",
                           Action<UltFight> perSecond = null, bool pushOn = true)
        {
            ultLast = null;
            yield return UltStart(null, segment, cls0);
            if (bk == null) yield break;
            BellkeeperEncounter.PushTimersOn = pushOn;
            var classes = PartyClasses.Skip(1).ToArray();
            var bots = UltimateParty.Fill(d.World, n, skill, n > 1 ? new[] { cls0 }.Concat(classes).ToArray() : new[] { cls0 });
            // the seed also turns the party's starting spots round the boss
            var turn = Quaternion.Euler(0, (BellkeeperEncounter.SeedOffset * 47) % 360, 0);
            foreach (var p in d.World.Players) BkPlace(p, turn * p.transform.position);
            foreach (var b in bots) b.Speed = PlayerController.WalkSpeed;
            var f = new UltFight { Label = label };
            var down = new HashSet<PlayerController>();
            int potions0 = d.World.Players.Sum(p => p.PotionsDrunk);
            bk.BeginCombat();
            int t0 = d.World.Tick, cap = Sim.Ticks(capSeconds), lastSeg = -1, decisions0 = 0;
            while (!bk.Dead && d.World.AnyStanding && d.World.Tick - t0 < cap)
            {
                for (int i = 0; i < 240 && !bk.Dead && d.World.AnyStanding && d.World.Tick - t0 < cap; i++)
                {
                    d.World.Step();
                    if ((int)bk.Segment != lastSeg && bk.SegmentStartTick >= 0)
                    {
                        int dec = bots.Sum(b => b.Decisions);
                        if (lastSeg >= 0) f.Decisions[(BellkeeperEncounter.UltSegment)lastSeg] = dec - decisions0;
                        decisions0 = dec; lastSeg = (int)bk.Segment; f.Segments.Add((bk.Segment, d.World.Tick - t0));
                    }
                    foreach (var p in d.World.Players)
                        if (p.Down && down.Add(p))
                        {
                            var last = bkHits.LastOrDefault(h => h.Player == p);
                            f.DeathLines.Add($"{p.name} down at {(d.World.Tick - t0) / 60f:0.0}s in {bk.Segment} by {last.Source} ({last.Damage}) · {(p.Input as UltimateBot)?.DebugLine}");
                        }
                    if (perSecond != null && (d.World.Tick - t0) % 60 == 0) perSecond(f);
                }
                yield return null;
            }
            if (lastSeg >= 0) f.Decisions[(BellkeeperEncounter.UltSegment)lastSeg] = bots.Sum(b => b.Decisions) - decisions0;
            f.Killed = bk.Dead; f.Wiped = !d.World.AnyStanding; f.Cause = bk.Ult.WipeCause ?? d.World.LastDamageSource;
            f.Ticks = d.World.Tick - t0; f.Deaths = down.Count; f.Potions = d.World.Players.Sum(p => p.PotionsDrunk) - potions0;
            f.Dealt = d.World.DealtBy.Values.Sum(); f.Hp01 = bk.Hp / (float)bk.MaxHp;
            ultLast = f;
            Note(f.ToString());
            foreach (var l in f.DeathLines.Take(6)) Note("   " + l);
            Note("   plan: " + string.Join(" · ", bk.PlanLog.Where(e => e.what.StartsWith("segment") || e.what.StartsWith("wipe") || e.what.StartsWith("second") || e.what.StartsWith("cracked"))
                .Select(e => $"{e.what}@{(e.tick - t0) / 60f:0.0}s")) + $" · max HP {bk.MaxHp}");
            BellkeeperEncounter.PushTimersOn = true;
        }

        // ---------------- DPS (ULTIMATE.md §9: set HP from measured DPS, never guessed) ----------------
        IEnumerator UltDpsChecks()
        {
            Note("---------- Ultimate Part 5: measured DPS ----------");
            int savedHp = BellkeeperEncounter.UltimateBaseHp;
            BellkeeperEncounter.UltimateBaseHp = 5000000;   // test setup: a bar no phase can end, so each phase runs its loop
            var solo = new Dictionary<string, float[]>(); var party = new float[HpSegments.Length];
            foreach (var cls in RunDirector.Classes)
            {
                var row = new float[HpSegments.Length];
                for (int s = 0; s < HpSegments.Length; s++)
                {
                    yield return UltRun($"DPS {cls} solo {HpSegments[s]}", 1, UltimateBot.Skill.Perfect, HpSegments[s].ToString(), 60, cls, pushOn: false);
                    row[s] = ultLast == null ? 0 : ultLast.Dealt / (ultLast.Ticks / 60f);
                }
                solo[cls] = row;
                Note($"DPS {cls} solo: " + string.Join("  ", HpSegments.Select((s, i) => $"{s} {row[i]:0}")));
            }
            for (int s = 0; s < HpSegments.Length; s++)
            {
                yield return UltRun($"DPS party of 4 {HpSegments[s]}", 4, UltimateBot.Skill.Perfect, HpSegments[s].ToString(), 60, pushOn: false);
                party[s] = ultLast == null ? 0 : ultLast.Dealt / (ultLast.Ticks / 60f);
            }
            Note("DPS party of 4 (V C A V): " + string.Join("  ", HpSegments.Select((s, i) => $"{s} {party[i]:0}")));
            BellkeeperEncounter.UltimateBaseHp = savedHp;
            // HP that makes each phase last its target at the median solo class's DPS (one bar: fixed shares per phase)
            var medianSolo = new float[HpSegments.Length];
            for (int s = 0; s < HpSegments.Length; s++) medianSolo[s] = solo.Values.Select(r => r[s]).OrderBy(x => x).ElementAt(1);
            var implied = HpSegments.Select((s, i) => medianSolo[i] * TargetSeconds[i] / BarShare[i]).ToArray();
            float hp = implied.Average();
            Note("implied solo bar per phase: " + string.Join("  ", HpSegments.Select((s, i) => $"{s} {implied[i]:0}")) + $"  -> UltimateBaseHp ≈ {hp:0}");
            float scale = 1 + 0.7f * 3;
            Note("phase time at that HP, solo median / party of 4 (HP x " + scale + "): " + string.Join("  ", HpSegments.Select((s, i) =>
                $"{s} {hp * BarShare[i] / Mathf.Max(1, medianSolo[i]):0}s / {hp * scale * BarShare[i] / Mathf.Max(1, party[i]):0}s")));
            Check("Part 5: DPS measured for every class and phase, solo and party", solo.Values.All(r => r.All(x => x > 0)) && party.All(x => x > 0));
        }

        // ---------------- clears and wipes ----------------
        // Which fights run (parallel batches split them over several game instances): -lastbellBotRuns p1,p2,p4,p8,c1,late1,
        // late4,miss1,miss4,v2 (default: all). Tuning sweeps: -lastbellUltHp N and -lastbellUltPush P1,P2,P3,P4,P5 (seconds).
        static string ArgOf(string name)
        {
            var a = Environment.GetCommandLineArgs(); int i = Array.FindIndex(a, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }
        static bool BotRun(string id) { var s = ArgOf("-lastbellBotRuns"); return s == null || s.Split(',').Any(x => x.Trim().Equals(id, StringComparison.OrdinalIgnoreCase)); }
        static void ApplyTuning(int hp, float[] push)
        {
            BellkeeperEncounter.UltimateBaseHp = int.TryParse(ArgOf("-lastbellUltHp"), out int h) ? h : hp;
            var p = ArgOf("-lastbellUltPush")?.Split(',').Select(x => float.TryParse(x, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : -1).ToArray();
            BellkeeperEncounter.PushSeconds = p != null && p.Length == 5 && p.All(v => v > 0) ? new[] { p[0], 0, p[1], p[2], 0, p[3], p[4] } : push;
        }

        IEnumerator UltBotChecks()
        {
            Note("---------- Ultimate Part 5: strat bots on the Ultimate entry ----------");
            int savedHp = BellkeeperEncounter.UltimateBaseHp;
            ApplyTuning(BellkeeperEncounter.DefaultBaseHp, (float[])BellkeeperEncounter.DefaultPush.Clone());   // the defaults (Part 5); sweeps override
            var ps = BellkeeperEncounter.PushSeconds;
            Note($"tuning: UltimateBaseHp {BellkeeperEncounter.UltimateBaseHp} · push P1 {ps[0]} P2 {ps[2]} P3 {ps[3]} P4 {ps[5]} · P5 enrage {ps[6]}");
            var fights = new List<UltFight>();
            var runs = new (string id, string label, int n, UltimateBot.Skill skill, string cls, bool mustWin)[]
            {
                ("p1", "PERFECT solo (Vanguard)", 1, UltimateBot.Skill.Perfect, "Vanguard", true),
                ("p2", "PERFECT party of 2", 2, UltimateBot.Skill.Perfect, "Vanguard", true),
                ("p4", "PERFECT party of 4 (V C A V)", 4, UltimateBot.Skill.Perfect, "Vanguard", true),
                ("p8", "PERFECT party of 8", 8, UltimateBot.Skill.Perfect, "Vanguard", true),
                ("c1", "PERFECT solo (Chimesinger)", 1, UltimateBot.Skill.Perfect, "Chimesinger", true),
                ("late1", "LATE +0.25 s solo", 1, UltimateBot.Skill.Late, "Vanguard", false),
                ("late4", "LATE +0.25 s party of 4", 4, UltimateBot.Skill.Late, "Vanguard", false),
                ("miss1", "MISS one assignment per phase, solo", 1, UltimateBot.Skill.Miss, "Vanguard", false),
                ("miss4", "MISS one assignment per phase, party of 4", 4, UltimateBot.Skill.Miss, "Vanguard", false),
            };
            foreach (var r in runs)
            {
                if (!BotRun(r.id)) continue;
                yield return UltRun(r.label, r.n, r.skill, cls0: r.cls); fights.Add(ultLast);
                if (r.mustWin) Check($"Part 5: {r.label} clears the Ultimate", ultLast != null && ultLast.Killed, ultLast?.ToString());
                else Check($"Part 5: imperfect bots wipe ({r.label})", ultLast != null && !ultLast.Killed, ultLast?.ToString());
            }
            // decision density per phase (personal reads + forced repositions, per player per 10 s) vs v2
            foreach (var f in fights.Where(x => x != null && x.Label.StartsWith("PERFECT")))
            {
                var parts = new List<string>();
                for (int i = 0; i < f.Segments.Count; i++)
                {
                    var seg = f.Segments[i].seg; int len = (i + 1 < f.Segments.Count ? f.Segments[i + 1].tick : f.Ticks) - f.Segments[i].tick;
                    int n = f.Label.Contains("of 8") ? 8 : f.Label.Contains("of 4") ? 4 : f.Label.Contains("of 2") ? 2 : 1;
                    f.Decisions.TryGetValue(seg, out int dec);
                    parts.Add($"{seg} {dec / (float)n / Mathf.Max(1, len / 600f):0.0}");
                }
                Note($"decision density ({f.Label}), per player per 10 s: " + string.Join("  ", parts));
            }
            // v2 reference: the same bot on the v2 fight (Medium), 120 s
            if (BotRun("v2")) yield return BkStart(null); else bk = null;
            if (bk != null)
            {
                var vb = UltimateParty.Drive(d.Player, 0, 1); vb.Speed = PlayerController.WalkSpeed;
                bk.BeginCombat(); int t0 = d.World.Tick;
                while (d.World.Tick - t0 < Sim.Ticks(120) && !bk.Dead && d.World.AnyStanding) { for (int i = 0; i < 240; i++) { d.World.Step(); BkTopUp(); } yield return null; }
                Note($"decision density (v2 Medium, solo strat bot, 120 s): {vb.Decisions / Mathf.Max(1, (d.World.Tick - t0) / 600f):0.0} per 10 s");
                d.Player.Input = bot;
            }
            BellkeeperEncounter.UltimateBaseHp = savedHp;
            BellkeeperEncounter.PushSeconds = (float[])BellkeeperEncounter.DefaultPush.Clone();
        }

        // ---------------- recorded full attempts (Chzy18: "I would like to see the boss attempts") ----------------
        // One run: a quick stepped DPS measure sets a provisional HP and push timers, then three real-time attempts from P1 are
        // recorded at 30 fps (frames in attempt_<name>/, 5-digit names; encoded afterwards with ffmpeg): a perfect party of
        // 4, a perfect solo bot, and the Late (+0.25 s) party of 4.
        IEnumerator UltAttempts()
        {
            Note("---------- Ultimate Part 5: recorded attempts ----------");
            int savedHp = BellkeeperEncounter.UltimateBaseHp;
            // Provisional numbers from part5-attempts-1's stepped DPS measure (party of 4: P1 187, P2 224, P3 158, P4 122,
            // P5 102): the bar is capped so the party's P5 fits the 78 s enrage (13000 x 3.1 x 15 % / 102 = 59 s); push = the
            // party's perfect phase time x ~1.3. Solo HP is the same base (no solo tuning yet).
            // part5-attempts-2: both parties wiped to the P2 push at ~58 % (real P2 party DPS ~154, 69 % of the stepped measure),
            // so run 3 scales HP to that and gives each push ~1.4x the party's expected phase time.
            BellkeeperEncounter.UltimateBaseHp = 10000;
            BellkeeperEncounter.PushSeconds = new float[] { 45, 0, 85, 80, 0, 105, 78 };
            Note($"provisional UltimateBaseHp {BellkeeperEncounter.UltimateBaseHp} · push P1 45 P2 85 P3 80 P4 105 · P5 enrage 78");
            yield return UltRecordAttempt("party4_perfect", 4, UltimateBot.Skill.Perfect);
            yield return UltRecordAttempt("party4_late", 4, UltimateBot.Skill.Late);
            yield return UltRecordAttempt("solo_perfect", 1, UltimateBot.Skill.Perfect);
            BellkeeperEncounter.UltimateBaseHp = savedHp;
            BellkeeperEncounter.PushSeconds = (float[])BellkeeperEncounter.DefaultPush.Clone();
        }

        // -lastbellTestAbility bots_watch: the same three attempts, live and smooth (no frame capture, real game time), for watching
        IEnumerator UltWatchAttempts()
        {
            watchOnly = true;
            yield return UltAttempts();
            watchOnly = false;
        }
        bool watchOnly;

        IEnumerator UltRecordAttempt(string name, int n, UltimateBot.Skill skill)
        {
            yield return UltStart();
            if (bk == null) yield break;
            var bots = UltimateParty.Fill(d.World, n, skill, n > 1 ? PartyClasses : new[] { "Vanguard" });
            foreach (var b in bots) b.Speed = PlayerController.WalkSpeed;
            var cdir = System.IO.Path.Combine(dir, "attempt_" + name); if (!watchOnly) System.IO.Directory.CreateDirectory(cdir);
            var boss = bk;
            bk.BeginCombat(); d.SetPaused(false);
            if (!watchOnly) Time.captureFramerate = 30;
            int frame = 0, tail = -1, max = watchOnly ? int.MaxValue : 30 * 60 * 16; float t0 = Time.unscaledTime;
            bool killed = false; string seg = "P1", cause = null; float hpLeft = 1;
            while (frame < max && (tail < 0 || tail-- > 0))
            {
                yield return new WaitForEndOfFrame();
                if (watchOnly) frame++;
                else
                {
                    var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); tex.Apply();
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(cdir, $"{frame++:00000}.jpg"), tex.EncodeToJPG(80)); Destroy(tex);
                }
                if (boss) { killed |= boss.Dead; seg = boss.Segment.ToString(); hpLeft = boss.Hp / (float)boss.MaxHp; if (boss.Ult.WipeCause != null) cause = boss.Ult.WipeCause; }
                if (tail < 0 && d.State != RunState.Combat) { tail = watchOnly ? 480 : 30 * 4; if (!killed) cause = cause ?? d.DefeatCause; }   // keep 4 s of the outcome screen
            }
            Time.captureFramerate = 0;
            Note($"attempt {name}: {(killed ? "KILL" : "WIPE (" + cause + ")")} after {(watchOnly ? (int)(Time.unscaledTime - t0) : frame / 30) / 60}:{(watchOnly ? (int)(Time.unscaledTime - t0) : frame / 30) % 60:00} {(watchOnly ? "watched" : "recorded")} · reached {seg} · boss {hpLeft:P1} left · frames {frame}");
            d.SetPaused(true);
        }

        // ---------------- demo video ----------------
        IEnumerator UltDemoVideo()
        {
            foreach (var seg in new[] { "P1", "T1", "P2", "P3", "T2", "P4", "P5" })
            {
                yield return UltStart(null, seg);
                if (bk == null) yield break;
                UltimateParty.Fill(d.World, 4, UltimateBot.Skill.Perfect, PartyClasses);
                bk.BeginCombat(); d.SetPaused(false);
                yield return Capture("ult_demo_" + seg, 20 * 60);
                d.SetPaused(true); bk.Halt();
            }
        }
    }
}
