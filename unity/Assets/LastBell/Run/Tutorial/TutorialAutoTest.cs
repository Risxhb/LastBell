using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Plays the whole tutorial end to end in the real game:  LastBell.exe -lastbellTutorialTest -lastbellOut <folder>
    // A bot drives the player through ordinary PlayerInput (dodging like an AllyBot, walking to the shown gold ring,
    // pressing what each step asks), presses Enter on the text pages, captures a screenshot of every step, checks the
    // boss can't die early, and writes tutorial-report.txt. Exits 0 when every check passed, else 1.
    public sealed class TutorialAutoTest : MonoBehaviour
    {
        public static bool Requested => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellTutorialTest", StringComparison.OrdinalIgnoreCase));
        static string OutDir
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.FindIndex(args, a => a.Equals("-lastbellOut", StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Application.persistentDataPath, "tutorial-test");
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Requested || FindAnyObjectByType<TutorialAutoTest>()) return;
            var go = new GameObject("Tutorial autotest"); DontDestroyOnLoad(go); go.AddComponent<TutorialAutoTest>();
        }

        // ---- the player bot ----
        sealed class Bot : IInputSource
        {
            readonly AllyBot dodge = new AllyBot(3, 4, BotMode.Perfect) { HoldRange = 8.5f };
            public int tick;
            public PlayerInput Read(PlayerController me)
            {
                tick++;
                var inp = dodge.Read(me);
                var t = TutorialDirector.Current; if (t == null || t.Boss == null) return inp;
                var bp = Sim.Flat(t.Boss.transform.position);
                inp.Aim = bp; inp.Potion = false;
                inp.P0 = inp.P1 = inp.P2 = inp.P3 = inp.P4 = false; inp.H0 = inp.H1 = inp.H2 = inp.H3 = inp.H4 = false;
                var pos = Sim.Flat(me.transform.position);
                Vector3? goal = t.PlayerGoal;
                var k = me.Kit as ArcanistKit;
                switch (t.StepId)
                {
                    case "dash": inp.Move = TutorialBot.ToScreen(Vector3.right); if (tick % 20 == 0) inp.Dash = true; break;
                    case "sprint": inp.Move = TutorialBot.ToScreen(Vector3.left); inp.Sprint = true; break;
                    case "potion": if (tick % 30 == 0) inp.Potion = true; break;
                    case "missile":
                        if (k != null && !k.Acting && tick % 40 == 0) { inp.P0 = inp.H0 = true; holding = true; }
                        else if (holding && k != null && k.Missiles < 3) inp.H0 = true;
                        else holding = false;
                        break;
                    case "disintegrate": Disintegrate(k, ref inp); break;
                    case "sigil": if (tick % 20 == 0) inp.P2 = true; break;
                    case "comet": if (tick % 20 == 0) inp.P3 = true; break;
                    case "surge":
                        if (k != null && !k.Surging && k.Glyphs >= 2 && tick % 15 == 0) inp.P4 = true;
                        else if (k != null && !k.Surging) Disintegrate(k, ref inp);
                        else if (tick % 12 == 0) inp.P0 = true;
                        break;
                    case "finish": Rotation(k, ref inp); break;
                    case "marks":
                        if (t.Labels.TryGetValue(me, out var mark))
                        {
                            var away = pos - bp; if (away.sqrMagnitude < 0.5f) away = Vector3.forward;
                            if (mark.StartsWith("SHADE")) goal = bp + away.normalized * 3f;
                            else
                            {
                                var g = Inside(bp + away.normalized * 11f);
                                if (Sim.Flat(g - bp).magnitude < 9.6f) g = Inside(bp - bp.normalized * 22f);
                                goal = g;
                            }
                        }
                        break;
                    case "tether":
                        if (t.TetherPost.HasValue) goal = t.TetherPost.Value - t.TetherPost.Value.normalized * 8f;
                        break;
                }
                if (goal.HasValue)
                {
                    var to = Sim.Flat(goal.Value - pos);
                    inp.Dash = false; inp.Sprint = to.magnitude > 3f;
                    inp.Move = to.magnitude > 0.3f ? TutorialBot.ToScreen(to.normalized) : Vector2.zero;
                }
                return inp;
            }
            bool holding;
            void Disintegrate(ArcanistKit k, ref PlayerInput inp)
            {
                if (k == null) return;
                if (k.BeamActive || k.Acting) { inp.H1 = true; return; }
                if (tick % 10 == 0) { inp.P1 = inp.H1 = true; }
            }
            void Rotation(ArcanistKit k, ref PlayerInput inp)
            {
                if (k == null) { if (tick % 10 == 0) inp.P0 = inp.H0 = true; return; }
                if (k.BeamActive) { inp.H1 = true; return; }
                if (k.Acting) return;
                if (tick % 6 != 0) return;
                if (k.Glyphs >= 2 && k.Cooldown01(4) <= 0.001f) inp.P4 = true;
                else if ((k.Glyphs > 0 || k.Surging) && k.Cooldown01(3) <= 0.001f) inp.P3 = true;
                else if ((k.Glyphs > 0 || k.Surging) && k.Cooldown01(2) <= 0.001f) inp.P2 = true;
                else if (k.Cooldown01(1) <= 0.001f) inp.P1 = inp.H1 = true;
                else inp.P0 = true;
            }
            static Vector3 Inside(Vector3 p) { p = Sim.Flat(p); float lim = CombatWorld.PlayerBound - 1.2f; return p.magnitude > lim ? p.normalized * lim : p; }
        }

        RunDirector d; readonly Bot bot = new Bot();
        readonly List<string> lines = new List<string>(); int pass, fail, errors, shots;
        string dir;

        void Start()
        {
            dir = OutDir; Directory.CreateDirectory(dir);
            Application.logMessageReceived += OnLog;
            Application.runInBackground = true;
            StartCoroutine(Main());
        }
        void OnDestroy() { Application.logMessageReceived -= OnLog; }
        void OnLog(string msg, string stack, LogType t) { if ((t == LogType.Exception || t == LogType.Error) && !(stack ?? "").Contains("UnityEditor.")) { errors++; lines.Add("      LOG " + t + ": " + msg); } }
        void Check(string name, bool ok, string detail = "")
        {
            if (ok) pass++; else fail++;
            var line = $"{(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : "  — " + detail)}";
            lines.Add(line); Debug.Log("[tutorial-test] " + line);
        }
        void Note(string s) { lines.Add("      " + s); Debug.Log("[tutorial-test] " + s); }
        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, $"{++shots:00}_{name}.png"), tex.EncodeToPNG()); Destroy(tex);
        }

        IEnumerator Main()
        {
            yield return new WaitForSeconds(1f);
            int enc = RunDirector.TutorialPractice, map = RunMaps.IndexOf(RunDirector.TempoTyrantMap);
            Check("tutorial entry registered: practice only, on Metronome Court", enc >= 0 && !RunDirector.Bosses[enc].InRun && RunDirector.Bosses[enc].Map == RunDirector.TempoTyrantMap);
            Check("Tempo Tyrant practice still resolves to the Tyrant", RunDirector.TempoTyrantPractice >= 0 && RunDirector.Bosses[RunDirector.TempoTyrantPractice].Name.StartsWith("Tempo Tyrant"));
            Check("run order unchanged (tutorial excluded)", !RunDirector.RunOrder.Contains(enc));
            float boot = 0; while (RunDirector.Instance == null && boot < 4) { boot += Time.deltaTime; yield return null; }
            if (RunDirector.Instance == null) UnityEngine.SceneManagement.SceneManager.LoadScene(RunMaps.All[map].RunScene);
            while (RunDirector.Instance == null || RunDirector.Instance.State != RunState.Menu) yield return null;
            d = RunDirector.Instance;
            Check("tutorial practice accepted (Arcanist)", d.StartPractice("Arcanist", enc));
            float t0 = 0; while ((RunDirector.Instance == null || RunDirector.Instance.State != RunState.Ready) && t0 < 30) { t0 += Time.deltaTime; yield return null; }
            d = RunDirector.Instance;
            Check("Ready on Metronome Court", d && d.State == RunState.Ready && RunMaps.Current == map && d.Encounter == enc);
            d.Player.Input = bot;
            Check("Ready accepted", d.RequestReady());
            t0 = 0; while ((d.State != RunState.Combat || TutorialDirector.Current == null) && t0 < 10) { t0 += Time.deltaTime; yield return null; }
            var t = TutorialDirector.Current;
            Check("combat starts with the tutorial attached to the Tempo Tyrant", d.State == RunState.Combat && t != null && d.Boss is TempoTyrantEncounter);
            if (t == null) { Finish(); yield break; }
            Check("three bot allies join, driven by the tutorial", d.Allies.Count == 3 && d.Allies.All(a => a.Input is TutorialBot) && d.World.Players.Count == 4, $"{d.Allies.Count} allies");
            Check("boss HP floor set above Broken Cadence", d.Boss.HpFloor >= d.Boss.MaxHp / 5, $"{d.Boss.HpFloor}/{d.Boss.MaxHp}");

            int seenShot = 0; string lastStep = ""; float stepTime = 0, pageTime = 0, total = 0; bool floorChecked = false;
            float minBeforeHours = 1f; bool diedEarly = false;
            while (!t.Finished && total < 25 * 60)
            {
                float dt = Time.deltaTime; total += dt; stepTime += dt;
                if (t.StepId != lastStep) { Note($"step {t.StepIndex + 1}: {t.StepId} at {total:0}s"); lastStep = t.StepId; stepTime = 0; }
                if (t.ShotSerial != seenShot) { seenShot = t.ShotSerial; yield return Shot(t.ShotName); }
                if (t.WaitingForContinue) { pageTime += dt; if (pageTime > 0.8f) { t.Continue(); pageTime = 0; } } else pageTime = 0;
                if (t.StepId != "hours" && t.StepId != "finish") minBeforeHours = Mathf.Min(minBeforeHours, d.Boss.Hp / (float)d.Boss.MaxHp);
                if (d.Boss.Dead && t.StepId != "finish") diedEarly = true;
                // the clamp itself: a lethal hit while the floor is up leaves him standing at the floor
                if (!floorChecked && t.StepId == "tempo")
                {
                    floorChecked = true;
                    d.World.DamageBoss(999999, d.Boss.transform.position + Vector3.up * 2, "Tutorial floor check (test only)");
                    Check("a lethal hit before the finale leaves him standing at the floor", !d.Boss.Dead && d.Boss.Hp == d.Boss.HpFloor, $"hp {d.Boss.Hp}, floor {d.Boss.HpFloor}");
                }
                if (stepTime > 240) { Check("step finishes within 4 minutes: " + t.StepId, false); break; }
                yield return null;
            }
            Check("the boss never fell below 25% before the finale", minBeforeHours >= TutorialDirector.EarlyFloor - 0.001f, $"min {minBeforeHours:0.000}");
            Check("the boss couldn't be defeated before the final step", !diedEarly);
            Check("finish step reached, boss defeated naturally", t.Finished && d.Boss != null && d.Boss.Dead && t.FinaleOpen);
            t0 = 0; while (d.State != RunState.Results && t0 < 10) { t0 += Time.deltaTime; yield return null; }
            Check("completion screen: practice victory", d.State == RunState.Results && d.Victory);
            yield return new WaitForSeconds(0.5f);
            Check("'Tutorial complete' panel shown with the results", TutorialCompletePanel.Current != null);
            yield return Shot("complete");
            Note("--- steps ---");
            foreach (var r in t.Records)
            {
                Note($"{r.Id,-14} tries {r.Tries}  {(r.Passed ? "handled" : "NOT handled")}  {r.Note}");
                Check("step handled within " + TutorialDirector.MaxTries + " tries: " + r.Id, r.Passed, r.Note);
            }
            string[] expected = { "welcome", "move", "dash", "sprint", "potion", "arcanist", "missile", "disintegrate", "sigil", "comet", "surge", "rotation", "tempo", "severance", "wake", "crown", "slices", "rush", "allies", "soak", "spread", "burn", "marks", "tether", "follow", "hours", "finish" };
            var ran = t.Records.Select(r => r.Id).ToArray();
            Check("every Arcanist step ran, in order (Tollbreaker/Chimesinger note skipped)", ran.SequenceEqual(expected), string.Join(",", ran));
            Note($"mercy heals {t.MercyHeals}, revives {t.MercyRevives}; total {total:0}s");
            Check("no errors or exceptions logged", errors == 0, errors + " logged");
            Finish();
        }

        void Finish()
        {
            lines.Insert(0, $"Tutorial autotest — {pass} passed, {fail} failed  ({DateTime.Now:yyyy-MM-dd HH:mm})");
            File.WriteAllLines(Path.Combine(dir, "tutorial-report.txt"), lines);
            Debug.Log($"[tutorial-test] RESULT {pass} passed, {fail} failed");
            Application.Quit(fail == 0 ? 0 : 1);
        }
    }
}
