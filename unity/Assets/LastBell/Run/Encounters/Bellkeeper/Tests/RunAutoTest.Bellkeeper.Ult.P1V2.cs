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
            foreach (var r in runs)
            {
                int start = -1;
                System.Action<UltFight> trace = null;
                if (r.label == "v2 P1 party4 perfect")   // where each bot is around beats 9-11 (24-33 s)
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
                Note($"   relay beats {relay.BeatsClean}/{relay.Beats} clean · debts detonated {debts.Detonated} · damage taken {taken} ({string.Join(", ", bkHits.GroupBy(h => h.Source).Select(g => g.Key + " " + g.Sum(h => h.Damage)))})" +
                     (relay.Fails.Count > 0 ? " · fails: " + string.Join("; ", relay.Fails.Take(5)) : ""));
                Note("   dealt: " + string.Join(", ", d.World.Players.Select(p => $"{p.ClassName} {(d.World.DealtBy.TryGetValue(p, out int v) ? v : 0)}")));
                if (r.must)
                {
                    Check($"{r.label}: clears P1", ultLast.Killed, ultLast.ToString());
                    Check($"{r.label}: 0 damage taken, 0 potions", taken == 0 && ultLast.Potions == 0, $"damage {taken}, potions {ultLast.Potions}");
                    Check($"{r.label}: every relay beat clean", relay.Beats > 20 && relay.BeatsClean == relay.Beats, $"{relay.BeatsClean}/{relay.Beats}");
                    Check($"{r.label}: two debt rounds detonated", debts.Detonated >= 8, debts.Detonated.ToString());
                }
            }
        }

        IEnumerator UltP1V2Video()
        {
            yield return UltStart(null, "P1");
            if (bk == null) yield break;
            var bots = UltimateParty.Fill(d.World, 4, UltimateBot.Skill.Perfect, PartyClasses);
            foreach (var b in bots) b.Speed = PlayerController.WalkSpeed;
            var cdir = System.IO.Path.Combine(dir, "clip_p1v2"); System.IO.Directory.CreateDirectory(cdir);
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
            bk.BeginCombat(); d.SetPaused(false);
            Time.captureFramerate = 30;
            int frame = 0, max = 30 * 58;
            while (frame < max && d.State == RunState.Combat)
            {
                yield return new WaitForEndOfFrame();
                var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); tex.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(cdir, $"{frame++:00000}.jpg"), tex.EncodeToJPG(82)); Destroy(tex);
            }
            Time.captureFramerate = 0;
            if (over) Destroy(over.gameObject);
            if (game) game.enabled = true;
            var relay = bk.UltRelay;
            Note($"clip p1v2: {frame} frames · relay {relay.BeatsClean}/{relay.Beats} clean · debts detonated {bk.Mechanic<ChimeDebts>().Detonated} · damage taken {bkHits.Sum(h => h.Damage)}");
            d.SetPaused(true); bk.Halt();
        }
    }
}
