using System.Collections;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Bellkeeper Ultimate v2, the P1 slice (proposal: Escapement Relay + Chime Debts, ULTIMATE-PROMPT-v2.md). Long, never
    // part of the default suite:
    //   -lastbellTestAbility bots_p1v2        stepped bot runs of P1: the perfect party of 4 must clear with 0 damage and
    //                                         0 potions and every beat clean; other sizes and skills are reported
    //   -lastbellTestAbility bots_p1v2_video  a real-time clip (30 fps frames, under a minute) of the perfect party of 4
    public sealed partial class RunAutoTest
    {
        IEnumerator UltP1V2Checks()
        {
            Note("---------- Ultimate v2 · P1 slice (proposal) ----------");
            var runs = new (string label, int n, UltimateBot.Skill skill, bool must)[]
            {
                ("v2 P1 party4 perfect", 4, UltimateBot.Skill.Perfect, true),
                ("v2 P1 party8 perfect", 8, UltimateBot.Skill.Perfect, false),
                ("v2 P1 trio perfect", 3, UltimateBot.Skill.Perfect, false),
                ("v2 P1 duo perfect", 2, UltimateBot.Skill.Perfect, false),
                ("v2 P1 solo perfect", 1, UltimateBot.Skill.Perfect, false),
                ("v2 P1 party4 late", 4, UltimateBot.Skill.Late, false),
                ("v2 P1 party4 miss", 4, UltimateBot.Skill.Miss, false),
            };
            ChimeDebts.LogDetonations = true;
            foreach (var r in runs)
            {
                int start = -1;
                System.Action<UltFight> trace = null;
                if (false)   // where each bot is around beats 9-11 (24-33 s)
                    trace = f =>
                    {
                        if (start < 0) start = d.World.Tick;
                        float s = (d.World.Tick - start) / 60f;
                        if (s < 24 || s > 33) return;
                        foreach (var p in d.World.Players)
                            Note($"   t {s:0.00} {p.ClassName} hp {p.Hp} st {p.Stamina:0} · {(p.Input as UltimateBot)?.DebugLine}");
                    };
                yield return UltRun(r.label, r.n, r.skill, "P1", 220, "Vanguard", trace);
                if (ultLast == null || bk == null) continue;
                var relay = bk.UltRelay;
                int taken = bkHits.Sum(h => h.Damage);
                var debts = bk.Mechanic<ChimeDebts>();
                Note($"   relay beats {relay.BeatsClean}/{relay.Beats} clean · debts detonated {debts.Detonated} erased {debts.Erased} · rewinds {relay.Reversals} · damage taken {taken} ({string.Join(", ", bkHits.GroupBy(h => h.Source).Select(g => g.Key + " " + g.Sum(h => h.Damage)))})" +
                     (relay.Fails.Count > 0 ? " · fails: " + string.Join("; ", relay.Fails.Take(5)) : ""));
                Note("   dealt: " + string.Join(", ", d.World.Players.Select(p => $"{p.ClassName} {(d.World.DealtBy.TryGetValue(p, out int v) ? v : 0)}")));
                if (r.must)
                {
                    Check($"{r.label}: clears P1", ultLast.Killed, ultLast.ToString());
                    Check($"{r.label}: 0 damage taken, 0 potions", taken == 0 && ultLast.Potions == 0, $"damage {taken}, potions {ultLast.Potions}");
                    Check($"{r.label}: every relay beat clean", relay.Beats > 20 && relay.BeatsClean == relay.Beats, $"{relay.BeatsClean}/{relay.Beats}");
                    Check($"{r.label}: two debt rounds resolved", debts.Detonated + debts.Erased >= 8, $"{debts.Detonated} detonated, {debts.Erased} erased");
                }
            }
        }

        // -lastbellTestAbility bots_p2v2: P2 Broken Hour from its practice start, and a full v2 attempt (P1 then P2)
        // -lastbellTestAbility bots_p2v2_8: the party of 8 alone, with the solver and every pass logged
        IEnumerator UltP2V2Eight()
        {
            BrokenHour.LogSolver = true;
            yield return UltRun("v2 P2 party8 perfect", 8, UltimateBot.Skill.Perfect, "P2", 330, "Vanguard");
            if (bk != null) { var bh = bk.UltBroken; Note($"   broken hour {bh.BeatsClean}/{bh.Beats} clean · passes {bh.Passes}" + (bh.Fails.Count > 0 ? " · fails: " + string.Join("; ", bh.Fails.Take(6)) : "")); }
            BrokenHour.LogSolver = false;
        }

        IEnumerator UltP2V2Checks()
        {
            Note("---------- Ultimate v2 · P2 Broken Hour (proposal) ----------");
            var runs = new (string label, int n, UltimateBot.Skill skill, string seg, bool must)[]
            {
                ("v2 P2 party4 perfect", 4, UltimateBot.Skill.Perfect, "P2", true),
                ("v2 P1+P2 party4 perfect", 4, UltimateBot.Skill.Perfect, "P1", true),
                ("v2 P2 party8 perfect", 8, UltimateBot.Skill.Perfect, "P2", false),
                ("v2 P2 trio perfect", 3, UltimateBot.Skill.Perfect, "P2", false),
                ("v2 P2 party4 late", 4, UltimateBot.Skill.Late, "P2", false),
                ("v2 P2 party4 miss", 4, UltimateBot.Skill.Miss, "P2", false),
            };
            foreach (var r in runs)
            {
                yield return UltRun(r.label, r.n, r.skill, r.seg, 330, "Vanguard");
                if (ultLast == null || bk == null) continue;
                var bh = bk.UltBroken; int taken = bkHits.Sum(h => h.Damage);
                Note($"   broken hour {bh.BeatsClean}/{bh.Beats} clean · passes {bh.Passes} · damage taken {taken} ({string.Join(", ", bkHits.GroupBy(h => h.Source).Select(g => g.Key + " " + g.Sum(h => h.Damage)))})" + (bh.Fails.Count > 0 ? " · fails: " + string.Join("; ", bh.Fails.Take(5)) : ""));
                Note("   dealt: " + string.Join(", ", d.World.Players.Select(p => $"{p.ClassName} {(d.World.DealtBy.TryGetValue(p, out int v) ? v : 0)}")));
                if (r.must)
                {
                    Check($"{r.label}: clears", ultLast.Killed, ultLast.ToString());
                    Check($"{r.label}: 0 damage taken, 0 potions", taken == 0 && ultLast.Potions == 0, $"damage {taken}, potions {ultLast.Potions}");
                    Check($"{r.label}: every Broken Hour beat clean", bh.Beats >= 18 && bh.BeatsClean == bh.Beats, $"{bh.BeatsClean}/{bh.Beats}");
                }
            }
        }

        IEnumerator UltP3V2Dbg()
        {
            OpenHour.LogSteer = true;
            yield return UltRun("v2 P3 party8 perfect", 8, UltimateBot.Skill.Perfect, "P3", 120, "Vanguard");
            OpenHour.LogSteer = false;
        }

        // -lastbellTestAbility bots_p3v2: P3 The Open Hour from its practice start (party sizes and skills), and the full v2 attempt
        IEnumerator UltP3V2Checks()
        {
            Note("---------- Ultimate v2 · P3 The Open Hour (proposal) ----------");
            var runs = new (string label, int n, UltimateBot.Skill skill, string seg, bool must)[]
            {
                ("v2 P3 party4 perfect", 4, UltimateBot.Skill.Perfect, "P3", true),
                ("v2 P1-P3 party4 perfect", 4, UltimateBot.Skill.Perfect, "P1", false),
                ("v2 P3 party8 perfect", 8, UltimateBot.Skill.Perfect, "P3", false),
                ("v2 P3 trio perfect", 3, UltimateBot.Skill.Perfect, "P3", false),
                ("v2 P3 party4 late", 4, UltimateBot.Skill.Late, "P3", false),
                ("v2 P3 party4 miss", 4, UltimateBot.Skill.Miss, "P3", false),
            };
            foreach (var r in runs)
            {
                yield return UltRun(r.label, r.n, r.skill, r.seg, 420, "Vanguard");
                if (ultLast == null || bk == null) continue;
                var oh = bk.UltOpen; int taken = bkHits.Sum(h => h.Damage);
                Note($"   open hour plates {oh.BeatsClean}/{oh.Beats} · damage taken {taken} ({string.Join(", ", bkHits.GroupBy(h => h.Source).Select(g => g.Key + " " + g.Sum(h => h.Damage)))})" + (oh.Fails.Count > 0 ? " · " + string.Join("; ", oh.Fails.Take(6)) : ""));
                Note("   dealt: " + string.Join(", ", d.World.Players.Select(p => $"{p.ClassName} {(d.World.DealtBy.TryGetValue(p, out int v) ? v : 0)}")));
                if (r.must)
                {
                    Check($"{r.label}: clears", ultLast.Killed, ultLast.ToString());
                    Check($"{r.label}: 0 damage taken, 0 potions", taken == 0 && ultLast.Potions == 0, $"damage {taken}, potions {ultLast.Potions}");
                }
            }
            BellkeeperEncounter.V2StopAfterP4 = false;
        }

        // -lastbellTestAbility bots_p5v2: P5 Final Toll from its practice start (sizes and skills), and the full v2 fight
        IEnumerator UltP5V2Checks()
        {
            Note("---------- Ultimate v2 · P5 Final Toll (proposal) ----------");
            var runs = new (string label, int n, UltimateBot.Skill skill, string seg, bool must)[]
            {
                ("v2 P5 party4 perfect", 4, UltimateBot.Skill.Perfect, "P5", true),
                ("v2 P1-P5 party4 perfect", 4, UltimateBot.Skill.Perfect, "P1", false),
                ("v2 P5 party8 perfect", 8, UltimateBot.Skill.Perfect, "P5", false),
                ("v2 P5 trio perfect", 3, UltimateBot.Skill.Perfect, "P5", false),
                ("v2 P5 party4 late", 4, UltimateBot.Skill.Late, "P5", false),
                ("v2 P5 party4 miss", 4, UltimateBot.Skill.Miss, "P5", false),
            };
            foreach (var r in runs)
            {
                FinalToll.Log = r.seg == "P5";
                yield return UltRun(r.label, r.n, r.skill, r.seg, 600, "Vanguard");
                FinalToll.Log = false;
                if (ultLast == null || bk == null) continue;
                var ft = bk.UltFinal; int taken = bkHits.Sum(h => h.Damage);
                Note($"   final toll tolls {ft.Tolls} · damage taken {taken} ({string.Join(", ", bkHits.GroupBy(h => h.Source).Select(g => g.Key + " " + g.Sum(h => h.Damage)))})" + (ft.Fails.Count > 0 ? " · " + string.Join("; ", ft.Fails.Take(8)) : ""));
                Note("   dealt: " + string.Join(", ", d.World.Players.Select(p => $"{p.ClassName} {(d.World.DealtBy.TryGetValue(p, out int v) ? v : 0)}")));
                if (r.must)
                {
                    Check($"{r.label}: kills before XIII", ultLast.Killed, ultLast.ToString());
                    Check($"{r.label}: 0 damage taken, 0 potions", taken == 0 && ultLast.Potions == 0, $"damage {taken}, potions {ultLast.Potions}");
                }
            }
        }

        // -lastbellTestAbility bots_p4v2: P4 The Gear Floor from its practice start (sizes and skills), and the full v2 attempt
        IEnumerator UltP4V2Checks()
        {
            Note("---------- Ultimate v2 · P4 The Gear Floor (proposal) ----------");
            var runs = new (string label, int n, UltimateBot.Skill skill, string seg, bool must)[]
            {
                ("v2 P4 party4 perfect", 4, UltimateBot.Skill.Perfect, "P4", true),
                ("v2 P1-P4 party4 perfect", 4, UltimateBot.Skill.Perfect, "P1", false),
                ("v2 P4 party8 perfect", 8, UltimateBot.Skill.Perfect, "P4", false),
                ("v2 P4 trio perfect", 3, UltimateBot.Skill.Perfect, "P4", false),
                ("v2 P4 party4 late", 4, UltimateBot.Skill.Late, "P4", false),
                ("v2 P4 party4 miss", 4, UltimateBot.Skill.Miss, "P4", false),
            };
            BellkeeperEncounter.V2StopAfterP4 = true;
            foreach (var r in runs)
            {
                GearFloor.Log = r.seg == "P4";
                yield return UltRun(r.label, r.n, r.skill, r.seg, 480, "Vanguard");
                GearFloor.Log = false;
                if (ultLast == null || bk == null) continue;
                var gf = bk.UltGear; int taken = bkHits.Sum(h => h.Damage);
                Note($"   gear floor slams {gf.Slams} · jams {gf.Jams} · damage taken {taken} ({string.Join(", ", bkHits.GroupBy(h => h.Source).Select(g => g.Key + " " + g.Sum(h => h.Damage)))})" + (gf.Fails.Count > 0 ? " · " + string.Join("; ", gf.Fails.Take(6)) : ""));
                Note("   dealt: " + string.Join(", ", d.World.Players.Select(p => $"{p.ClassName} {(d.World.DealtBy.TryGetValue(p, out int v) ? v : 0)}")));
                if (r.must)
                {
                    Check($"{r.label}: clears", ultLast.Killed, ultLast.ToString());
                    Check($"{r.label}: 0 damage taken, 0 potions", taken == 0 && ultLast.Potions == 0, $"damage {taken}, potions {ultLast.Potions}");
                }
            }
        }

        // watch = live in the window at real speed (no capture): the full P1 attempt plus 4 s of the outcome
        IEnumerator UltP1V2Video(bool watch = false, UltimateBot.Skill skill = UltimateBot.Skill.Perfect, string label = "perfect", string seg = "P1")
        {
            yield return UltStart(null, seg);
            if (bk == null) yield break;
            var bots = UltimateParty.Fill(d.World, 4, skill, PartyClasses);
            foreach (var b in bots) b.Speed = PlayerController.WalkSpeed;
            var cdir = System.IO.Path.Combine(dir, "clip_" + seg.ToLowerInvariant() + "v2"); if (!watch) System.IO.Directory.CreateDirectory(cdir);
            bkHits.Clear();
            // the whole dial from above, north up (the game's chase camera shows only a corner of it)
            var game = Camera.main; Camera over = null;
            if (game)
            {
                over = new GameObject("P1 v2 overview camera").AddComponent<Camera>();
                over.CopyFrom(game); over.tag = "MainCamera"; over.fieldOfView = 50;
                over.transform.SetPositionAndRotation(new Vector3(0, 27f, 9f), Quaternion.LookRotation(new Vector3(0, -27f, -9f)));
                game.enabled = false;
            }
            var boss = bk;
            bk.BeginCombat(); d.SetPaused(false);
            if (!watch) Time.captureFramerate = 30;
            int frame = 0, max = watch ? int.MaxValue : 30 * 58; float t0 = Time.unscaledTime, tail = -1;
            while (frame < max)
            {
                yield return new WaitForEndOfFrame();
                if (watch) frame++;
                else
                {
                    var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); tex.Apply();
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(cdir, $"{frame++:00000}.jpg"), tex.EncodeToJPG(82)); Destroy(tex);
                }
                if (d.State != RunState.Combat) { if (!watch) break; if (tail < 0) tail = Time.unscaledTime; if (Time.unscaledTime - tail > 4) break; }
                if (watch && Time.unscaledTime - t0 > 200) break;
            }
            Time.captureFramerate = 0;
            if (over) Destroy(over.gameObject);
            if (game) game.enabled = true;
            var relay = boss.UltRelay;
            var broken = boss.UltBroken;
            Note($"broken hour {broken.BeatsClean}/{broken.Beats} clean · passes {broken.Passes}" + (broken.Fails.Count > 0 ? " · fails: " + string.Join("; ", broken.Fails.Take(4)) : ""));
            Note($"{(watch ? "watched" : "clip")} {seg.ToLowerInvariant()}v2 {label}: {(boss.Dead ? "CLEAR" : "WIPE (" + (boss.Ult.WipeCause ?? d.World.LastDamageSource) + ")")} after {Time.unscaledTime - t0:0}s · relay {relay.BeatsClean}/{relay.Beats} clean · debts detonated {boss.Mechanic<ChimeDebts>().Detonated} · damage taken {bkHits.Sum(h => h.Damage)}");
            d.SetPaused(true); if (!boss.Dead) boss.Halt();
        }

        // -lastbellTestAbility bots_p1v2_watch: three live attempts in the window: perfect, Miss (forgets the first debts), Late
        IEnumerator UltP1V2Watch(string seg = "P1")
        {
            yield return UltP1V2Video(true, UltimateBot.Skill.Perfect, "perfect", seg);
            yield return UltP1V2Video(true, UltimateBot.Skill.Miss, "miss", seg);
            yield return UltP1V2Video(true, UltimateBot.Skill.Late, "late", seg);
        }
    }
}
