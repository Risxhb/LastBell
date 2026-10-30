using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Acceptance run for the Pendulum Sentinel inside the real game:  -pendulumSentinelTest -lastbellOut <folder>
    // Pure rules first (swing clip chaining, walking routes for every swing combo from every start position at 4.2 and
    // 6 m/s, weight spots, shares and walk-outs, orb bounces, plumb marks, stroke escapes, the rotation), then the fight on Skyward Spire through the
    // director's public requests. Bots send ordinary PlayerInput. Labelled dev hooks: ForcedNext, boss HP fraction /
    // defeat, player DebugSetHp, direct player placement, extra roster players (not co-op). Writes
    // pendulum-sentinel-report.txt plus screenshots and exits 0 / 1.
    public sealed class PendulumSentinelAutoTest : MonoBehaviour
    {
        public static bool Requested => Environment.GetCommandLineArgs().Any(a => a.Equals("-pendulumSentinelTest", StringComparison.OrdinalIgnoreCase));
        static string OutDir
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.FindIndex(args, a => a.Equals("-lastbellOut", StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Application.persistentDataPath, "pendulum-sentinel-test");
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Requested || FindAnyObjectByType<PendulumSentinelAutoTest>()) return;
            var go = new GameObject("Pendulum Sentinel autotest"); DontDestroyOnLoad(go); go.AddComponent<PendulumSentinelAutoTest>();
        }

        static Vector2 ToMove(Vector3 world)
        {
            var cam = Camera.main; var fwd = cam ? Sim.Flat(cam.transform.forward).normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, fwd);
            return Vector2.ClampMagnitude(new Vector2(Vector3.Dot(world, right), Vector3.Dot(world, fwd)), 1);
        }
        sealed class Bot : IInputSource
        {
            public Vector3 MoveWorld;
            public PlayerInput Read(PlayerController who) => new PlayerInput { Aim = who.transform.position + who.transform.forward, Move = ToMove(MoveWorld) };
        }

        RunDirector d; readonly Bot bot = new Bot();
        readonly List<string> lines = new List<string>(); int pass, fail, errors, shots;
        readonly List<(int tick, string source, PlayerController who, int dmg)> hits = new List<(int, string, PlayerController, int)>();
        readonly List<(int tick, string source)> spawned = new List<(int, string)>();
        readonly List<PlayerController> extras = new List<PlayerController>();
        string dir; bool keepAlive = true;
        PendulumSentinelEncounter boss;

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
            lines.Add(line); Debug.Log("[pendulum-sentinel-test] " + line);
        }
        void Note(string s) { lines.Add("      " + s); Debug.Log("[pendulum-sentinel-test] " + s); }
        void Section(string s) { lines.Add(""); lines.Add("== " + s); }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, $"{++shots:00}_{name}.png"), tex.EncodeToPNG()); Destroy(tex);
        }
        void RenderShot(string name, Vector3 pos, Vector3 look, float fov, int w = 1280, int h = 720)
        {
            var go = new GameObject("capture camera"); var cam = go.AddComponent<Camera>();
            if (Camera.main) cam.CopyFrom(Camera.main);
            cam.fieldOfView = fov; cam.aspect = w / (float)h; cam.orthographic = false;
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32); cam.targetTexture = rt; cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev; cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(dir, $"{++shots:00}_{name}.png"), tex.EncodeToPNG());
            Destroy(tex); rt.Release(); Destroy(rt); Destroy(go);
        }
        void Overhead(string name) => RenderShot(name, new Vector3(0, 32, 14), new Vector3(0, 0, -0.5f), 50);

        IEnumerator Wait(float s) { float t = 0; while (t < s) { t += Time.deltaTime; Keep(); yield return null; } }
        IEnumerator Until(Func<bool> cond, float timeout, string what)
        {
            float t = 0; while (!cond() && t < timeout) { t += Time.deltaTime; Keep(); yield return null; }
            if (!cond()) Check("reached: " + what, false, $"timed out after {timeout}s");
        }

        Vector3? goal; Func<Vector3?> goalFn;
        void Keep()
        {
            if (d == null || d.World == null) return;
            foreach (var p in d.World.Players) if (keepAlive && !p.Down && p.Hp < 100) p.DebugSetHp(100);   // hits now reach 90
            var me = d.Player;
            var g = goalFn != null ? goalFn() : goal;
            if (me && g.HasValue && me.Input == bot) { var to = Sim.Flat(g.Value - me.transform.position); bot.MoveWorld = to.magnitude > 0.15f ? to.normalized * Mathf.Min(1, to.magnitude / 0.4f) : Vector3.zero; }
            else bot.MoveWorld = Vector3.zero;
        }
        void OnHit(PlayerController p, Hazard h, int dmg) => hits.Add((d.World.Tick, h.Source, p, dmg));
        void OnSpawn(Hazard h) => spawned.Add((d.World.Tick, h.Source));
        void Place(PlayerController p, Vector3 at) { p.transform.position = d.World.Constrain(new Vector3(at.x, 0, at.z), PlayerController.Radius); }
        void PlaceMe(Vector3 at) { Place(d.Player, at); goal = d.Player.transform.position; goalFn = null; d.Player.Input = bot; }
        int HitsSince(int tick, string prefix, PlayerController who = null) => hits.Count(h => h.tick >= tick && h.source.StartsWith(prefix) && (who == null || h.who == who));
        int HitsExact(int tick, string source, PlayerController who = null) => hits.Count(h => h.tick >= tick && h.source == source && (who == null || h.who == who));

        IEnumerator Block(string atk, float timeout = 60f)
        {
            int before = boss.BlockLog.Count;
            boss.ForcedNext = atk;
            yield return Until(() => boss.BlockLog.Skip(before).Any(s => s.EndsWith(" " + atk)), timeout, atk + " starts");
            yield return Until(() => boss.Current == "between blocks" || boss.Dead, timeout, atk + " ends");
        }
        IEnumerator StartOf(string atk, float timeout = 30f)
        {
            int before = boss.BlockLog.Count; boss.ForcedNext = atk;
            yield return Until(() => boss.BlockLog.Skip(before).Any(s => s.EndsWith(" " + atk)), timeout, atk + " starts");
        }
        IEnumerator BetweenBlocks(float timeout = 60f) { yield return Until(() => boss.Current == "between blocks" || boss.Dead, timeout, "between blocks"); }

        // ======================================================================================================
        IEnumerator Main()
        {
            Section("pure rules");
            yield return new WaitForSeconds(1f);
            PendulumSentinelRules.Bound = RunMaps.All[RunMaps.IndexOf(RunDirector.AurelionMap)].Bound;
            Note($"Skyward Spire player bound {PendulumSentinelRules.Bound} m, boss bound {CombatWorld.BossBound} m");
            PureSwing(); PureWeights(); PurePlumb(); PureStroke();
            bool every = true;
            foreach (bool p2 in new[] { false, true })
            {
                var rot = PendulumSentinelEncounter.Rotation(p2);
                for (int i = 0; i < rot.Length; i++) if ((rot[i] == "Stroke") != (i % 2 == 1)) every = false;
            }
            Check("rotation: Return Stroke every other attack in both phases", every,
                string.Join(",", PendulumSentinelEncounter.Rotation(false)) + " / " + string.Join(",", PendulumSentinelEncounter.Rotation(true)));

            Section("registration and practice start");
            int map = RunMaps.IndexOf(RunDirector.AurelionMap), enc = RunDirector.PendulumSentinelPractice;
            Check("practice entry registered on Skyward Spire, practice only", enc >= 0 && RunDirector.Bosses[enc].Map == "skyward-spire" && !RunDirector.Bosses[enc].InRun);
            Check("run order unchanged (4 run bosses, Pendulum Sentinel excluded)", RunDirector.RunOrder.Length == 4 && !RunDirector.RunOrder.Contains(enc));
            Check("listed in the Minibosses row", LastBell.Menu.TestRushSetup.MinibossRow.Contains(enc) && !LastBell.Menu.TestRushSetup.BossRow.Contains(enc));
            float boot = 0; while (RunDirector.Instance == null && boot < 4) { boot += Time.deltaTime; yield return null; }
            if (RunDirector.Instance == null) UnityEngine.SceneManagement.SceneManager.LoadScene(RunMaps.All[map].RunScene);
            float waitMenu = 0; while ((RunDirector.Instance == null || RunDirector.Instance.State != RunState.Menu) && waitMenu < 30) { waitMenu += Time.deltaTime; yield return null; }
            if (RunDirector.Instance == null) { Check("run scene loads", false, "no RunDirector after 30 s"); Finish(); yield break; }
            d = RunDirector.Instance;
            Check("practice request accepted", d.StartPractice("Vanguard", enc));
            float t0 = 0; while ((RunDirector.Instance == null || RunDirector.Instance.State != RunState.Ready) && t0 < 30) { t0 += Time.deltaTime; yield return null; }
            d = RunDirector.Instance;
            Check("practice Ready on Skyward Spire", d && d.State == RunState.Ready && RunMaps.Current == map && d.Mode == RunMode.Practice && d.Encounter == enc);
            yield return BeginFight(20261026);
            Check("fight opens in phase 1 at 90 BPM", !boss.Phase2 && Mathf.Approximately(boss.Bpm, 90) && boss.BeatTicks == 40);
            Check("the Meshy model is loaded and rigged (not the stand-in)", !boss.Placeholder && boss.Presenter && boss.Presenter.HasModel);
            var clipNames = new[] { "Idle", "Walk", "SwingWind", "SwingLeft", "SwingRight", "WeightSpin", "WeightThrow", "PlumbDrop", "PlumbLoop", "ChargeLean", "Charge", "ChargeReturn", "WallSlam", "StuckLoop", "PhaseShift", "Defeat" };
            var missing = clipNames.Where(c => !boss.Presenter || !boss.Presenter.Has(c)).ToList();
            Check("all 16 animation clips imported", missing.Count == 0, missing.Count > 0 ? "missing: " + string.Join(", ", missing) : "");
            Check("no ability text: empty phase line and intro lesson, diagnostics off by default", boss.PhaseName == "" && boss.Lesson == "" && !PendulumSentinelHud.ShowDiagnostics);
            yield return Until(() => Sim.Flat(boss.transform.position).magnitude < 0.35f, 8, "walks to the centre");
            RenderShot("model_front", new Vector3(0, 3.6f, 9.5f), new Vector3(0, 2.2f, 0), 40);

            yield return SwingChecks();
            yield return WeightChecks();
            yield return PlumbChecks();
            yield return StrokeChecks();
            yield return DifficultyChecks();
            yield return PhaseTwoChecks();
            yield return DefeatChecks();
            Finish();
        }

        IEnumerator BeginFight(int seed)
        {
            d.Player.Input = bot; goal = null; goalFn = null; hits.Clear(); spawned.Clear();
            d.World.PlayerHit -= OnHit; d.World.PlayerHit += OnHit;
            d.World.Hazards.Spawned -= OnSpawn; d.World.Hazards.Spawned += OnSpawn;
            Check("Ready accepted", d.RequestReady());
            yield return Until(() => d.Boss is PendulumSentinelEncounter, 3, "boss spawned");
            boss = d.Boss as PendulumSentinelEncounter;
            boss.Seed = seed;
            d.World.Difficulty = Difficulty.Medium;   // a saved menu choice must not change the measured numbers
            PendulumSentinelHud.ShowDiagnostics = false;
            yield return Until(() => d.State == RunState.Combat, 6, "combat");
        }

        // ---------------- pure rules ----------------
        void PureSwing()
        {
            bool chainOk = true; string chainWhy = "";
            foreach (var lvl in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard })
                foreach (var (p2, bpm) in new[] { (false, 90f), (true, 112f) })
                for (float first = PendulumSentinelRules.FirstFloorBeats(lvl); first <= 6f; first += 0.5f)
                {
                    int beat = Mathf.RoundToInt(3600f / bpm);
                    var w = PendulumSentinelRules.ToTicks(PendulumSentinelRules.SwingWarnBeats(lvl, p2, first), beat);
                    var c = PendulumSentinelRules.SwingClipTicks(w);
                    int at = 0, prevEnd = -1;
                    for (int k = 0; k < w.Length; k++)
                    {
                        int start = at + w[k] - c[k] / 2; at += w[k];
                        if (c[k] < 6 || start < 0) { chainOk = false; chainWhy = $"{lvl} {bpm}: clip {k} {c[k]} ticks from {start}"; }
                        if (k == 0 && start < 0) chainOk = false;
                        if (prevEnd >= 0 && Mathf.Abs(start - prevEnd) > 1) { chainOk = false; chainWhy = $"{lvl} {bpm}: gap {start - prevEnd} before clip {k}"; }
                        prevEnd = start + c[k];
                    }
                }
            Check("Shortening Swing: the four swing clips chain without a jump and each passes the middle on its hit (all levels and tempos)", chainOk, chainWhy);
            Check("Shortening Swing: arcs 180, 150, 120, 90 degrees", PendulumSentinelRules.Arcs.SequenceEqual(new[] { 180f, 150f, 120f, 90f }));
            Check("Shortening Swing: Medium swings 2-4 take 1.75, 1.5, 1.5 beats above 50% and 2, 2, 1.75 below; Easy adds a beat",
                PendulumSentinelRules.SwingRestBeats(Difficulty.Medium, false).SequenceEqual(new[] { 1.75f, 1.5f, 1.5f }) &&
                PendulumSentinelRules.SwingRestBeats(Difficulty.Medium, true).SequenceEqual(new[] { 2f, 2f, 1.75f }) &&
                PendulumSentinelRules.SwingRestBeats(Difficulty.Easy, false).SequenceEqual(new[] { 2.75f, 2.5f, 2.5f }));

            // every start position on the floor and several boss positions: he lunges at the walker, swing 1 is sized to
            // the walk out, and the walker reads each arc: 4.2 m/s (prototype) and 6 m/s (design)
            var bosses = new[] { Vector3.zero, new Vector3(0, 0, 4), new Vector3(-5, 0, 3), new Vector3(6, 0, -6), new Vector3(0, 0, -9.5f) };
            foreach (float speed in new[] { 4.2f, 6f })
                foreach (var lvl in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard })
                    foreach (var (p2, bpm) in new[] { (false, 90f), (true, 112f) })
                    {
                        int n = 0, ok = 0; string why = ""; float longest = 0;
                        foreach (var b0 in bosses)
                            for (float x = -14; x <= 14; x += 1f)
                                for (float z = -14; z <= 14; z += 1f)
                                {
                                    var p = new Vector3(x, 0, z); float r = new Vector2(x, z).magnitude;
                                    if (r > PendulumSentinelRules.Bound - 0.2f || Sim.Flat(p - b0).magnitude < PendulumSentinelRules.BodyRadius + 0.6f) continue;
                                    n++;
                                    var b = PendulumSentinelRules.LungeEnd(b0, p);
                                    float first = PendulumSentinelRules.FirstSwingBeats(b, Sim.YawTo(b, p, 0), new[] { p }, bpm, lvl);
                                    longest = Mathf.Max(longest, first * 60f / bpm);
                                    var warn = PendulumSentinelRules.SwingWarnBeats(lvl, p2, first);
                                    if (PendulumSentinelRules.SwingComboSurvivable(b, p, warn, bpm, speed, lvl == Difficulty.Hard, out string w)) ok++;
                                    else if (why == "") why = $"boss ({b0.x},{b0.z}), from ({x},{z}): {w}";
                                }
                        Check($"Shortening Swing at {speed} m/s, {lvl}, {bpm} BPM{(lvl == Difficulty.Hard ? " with Backswing" : "")}: a walker survives the combo from every start ({n} boss and player positions; swing 1 at most {longest:0.00} s)", ok == n, ok == n ? "" : $"{ok}/{n}; {why}");
                    }
        }

        void PureWeights()
        {
            bool spots = true;
            foreach (bool solo in new[] { false, true })
                for (float yaw = 0; yaw < 360; yaw += 15)
                    foreach (var from in new[] { Vector3.zero, new Vector3(3, 0, 0), new Vector3(0, 0, -3.5f) })
                    {
                        var s = PendulumSentinelRules.WeightSpots(from, yaw, solo);
                        if (s.Count != 3 || s.Any(p => p.magnitude > PendulumSentinelRules.Bound - PendulumSentinelRules.WeightRadius + 0.01f)) spots = false;
                    }
            Check("Counterweight: three spots along his arms, each circle wholly on the floor", spots);
            var c = PendulumSentinelRules.WeightSpots(Vector3.zero, 0, false); var cs = PendulumSentinelRules.WeightSpots(Vector3.zero, 0, true);
            Check("Counterweight: 4 m circles, 8 m out in co-op, 5.5 m solo, 120 degrees apart, the solo three never overlap",
                Mathf.Approximately(PendulumSentinelRules.WeightRadius, 4f) &&
                c.All(p => Mathf.Abs(p.magnitude - 8) < 0.01f) && cs.All(p => Mathf.Abs(p.magnitude - 5.5f) < 0.01f) && Mathf.Abs(Vector3.Angle(c[0], c[1]) - 120) < 0.5f && Mathf.Abs(Vector3.Angle(c[0], c[2]) - 120) < 0.5f &&
                Sim.Flat(cs[0] - cs[1]).magnitude > 2 * (PendulumSentinelRules.WeightRadius + Hazard.Margin));
            bool walkOut = true; string woWhy = "";
            foreach (var lvl in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard })
                foreach (var (p2, bpm) in new[] { (false, 90f), (true, 112f) })
                {
                    float have = 4.2f * (PendulumSentinelRules.WeightWarnBeats(lvl, p2) * 60f / bpm - PendulumSentinelRules.Reaction);
                    if (have < PendulumSentinelRules.WeightEscape) { walkOut = false; woWhy = $"{lvl} {bpm}: {have:0.00} m of {PendulumSentinelRules.WeightEscape:0.00}"; }
                }
            Check($"Counterweight: from under a weight's centre you can walk out before it lands at 4.2 m/s ({PendulumSentinelRules.WeightEscape:0.00} m; 2.5 beats, 3 below 50%)", walkOut, woWhy);
            Check("Counterweight: an unabsorbed weight lies 3 beats (4 on Easy), then its orbs bounce 3-4 times",
                PendulumSentinelRules.SoakBeats(Difficulty.Medium) == 3f && PendulumSentinelRules.SoakBeats(Difficulty.Easy) == 4f && PendulumSentinelRules.BouncesMin == 3 && PendulumSentinelRules.BouncesMax == 4);
            bool refl = true;
            for (float a = 0; a < 360; a += 10)
                for (float off = -80; off <= 80; off += 20)
                {
                    var at = Sim.Dir(a) * PendulumSentinelRules.Bound; var v = Sim.Dir(a + off) * 7f; var r = PendulumSentinelRules.Reflect(v, at);
                    if (Mathf.Abs(r.magnitude - 7f) > 0.01f || Vector3.Dot(r, at) >= 0 || Mathf.Abs(Vector3.Angle(-r, at) - Mathf.Abs(off)) > 0.5f) refl = false;
                }
            Check("Counterweight orbs: a bounce off the edge keeps the speed, turns inward, angle in = angle out", refl);
            int wd = PendulumSentinelEncounter.WeightDmg;
            Check($"Counterweight: one catcher takes {wd} and survives from full; two take {PendulumSentinelRules.Share(wd, 2)} each; three {PendulumSentinelRules.Share(wd, 3)}",
                PendulumSentinelRules.Share(wd, 1) == wd && PendulumSentinelRules.Share(wd, 2) == 38 && PendulumSentinelRules.Share(wd, 3) == 25 && wd < 100);
            var d1 = PendulumSentinelRules.WeightDelays(1, 100, 40); var d2 = PendulumSentinelRules.WeightDelays(2, 100, 40); var d3 = PendulumSentinelRules.WeightDelays(5, 100, 40);
            Check("Counterweight landings: solo one by one 2 beats apart, two players front pair then back, 3+ all together",
                d1.SequenceEqual(new[] { 100, 180, 260 }) && d2.SequenceEqual(new[] { 100, 100, 180 }) && d3.SequenceEqual(new[] { 100, 100, 100 }));
        }

        void PurePlumb()
        {
            var r = new System.Random(3); bool ok = true; string why = "";
            for (int i = 0; i < 2000; i++)
            {
                int players = 1 + i % 8, count = PendulumSentinelRules.MarkCount(players);
                var m = PendulumSentinelRules.PlumbMarks(r, count, Vector3.zero);
                if (m.Count != count || count < 4 || count * 2 < players) { ok = false; why = $"{players} players, {m.Count} marks"; }
                foreach (var p in m) if (p.magnitude < PendulumSentinelRules.MarkMin - 0.01f || p.magnitude > PendulumSentinelRules.MarkMax + 0.01f) { ok = false; why = $"mark at {p.magnitude:0.0} m"; }
                for (int a = 0; a < m.Count; a++) for (int b = a + 1; b < m.Count; b++) if (Vector3.Angle(m[a], m[b]) < 89.5f) { ok = false; why = $"marks {Vector3.Angle(m[a], m[b]):0} degrees apart"; }
            }
            Check("Plumb Line: 4+ marks (room for 2 players each), 6-10 m out, at least 90 degrees apart (2000 rolls)", ok, why);
            // reach: from anywhere on the floor to a mark's final spot, walking at 4.2 m/s after the 0.3 s reaction
            var rr = new System.Random(9); float worstNeed = 0, worstHave = float.MaxValue; bool reach = true;
            foreach (var (drift, bpm) in new[] { (false, 90f), (true, 112f) })
                foreach (float radius in new[] { PendulumSentinelEncounter.MarkRadiusMedium, PendulumSentinelEncounter.MarkRadiusEasy })
                    for (int i = 0; i < 60; i++)
                    {
                        var m = PendulumSentinelRules.PlumbMarks(rr, 4, Vector3.zero);
                        if (drift) m = m.Select(p => PendulumSentinelRules.Drift(p, Vector3.zero, (i % 2 == 0 ? 1 : -1) * PendulumSentinelEncounter.DriftPerBeat * PendulumSentinelEncounter.DriftBeats)).ToList();
                        int beats = drift ? PendulumSentinelEncounter.PlumbBeatsLow : PendulumSentinelEncounter.PlumbBeatsHigh;
                        float need = PendulumSentinelRules.WorstReach(m, radius), have = 4.2f * (beats * 60f / bpm - PendulumSentinelRules.Reaction);
                        worstNeed = Mathf.Max(worstNeed, need); worstHave = Mathf.Min(worstHave, have);
                        if (need > have) reach = false;
                    }
            Check("Plumb Line: every floor spot reaches a mark in time at 4.2 m/s (4 beats still, 5 drifting; 240 casts)", reach, $"worst walk {worstNeed:0.0} m, time for {worstHave:0.0} m");
            var marks = new List<Vector3> { new Vector3(8, 0, 0), new Vector3(-8, 0, 0) };
            Check("Plumb Line: a player on a mark's border counts as inside; just outside does not",
                PendulumSentinelRules.MarkIndex(marks, 2.2f, new Vector3(10.4f, 0, 0)) == 0 && PendulumSentinelRules.MarkIndex(marks, 2.2f, new Vector3(10.6f, 0, 0)) == -1);
        }

        void PureStroke()
        {
            var (dir, yaw, len, end) = PendulumSentinelRules.StrokeLane(Vector3.zero, new Vector3(0, 0, 5), Vector3.forward);
            Check("Return Stroke: the lane runs to the floor's edge; his body stops at the boss bound (9.5 m)",
                Mathf.Abs(len - (PendulumSentinelRules.Bound + 0.5f)) < 0.01f && Mathf.Abs(end.magnitude - CombatWorld.BossBound) < 0.01f);
            var (show, dash, cleave) = PendulumSentinelRules.StrokeBeats(Difficulty.Medium, false);
            var (show2, _, _) = PendulumSentinelRules.StrokeBeats(Difficulty.Medium, true);
            Check("Return Stroke: 5.5 m lane and 9 m cleave; lane shown 2 beats (2.5 below 50%), the charge 0.75 beat, the cleave 0.75 beat after he lands",
                PendulumSentinelRules.StrokeWidth == 5.5f && PendulumSentinelRules.RearRadius == 9f && show == 2f && show2 == 2.5f && dash == 0.75f && cleave == 0.75f);
            // the target walks out of each lane and each cleave, every start position, several boss positions
            var bossAt = new[] { Vector3.zero, new Vector3(0, 0, 3.5f), new Vector3(3, 0, -2) };
            foreach (var (count, bpm) in new[] { (1, 90f), (PendulumSentinelEncounter.StrokesLow, 112f) })
                foreach (var lvl in new[] { Difficulty.Medium, Difficulty.Easy })
                    foreach (float speed in new[] { 4.2f, 6f })
                    {
                        int n = 0, ok = 0; string why = "";
                        foreach (var b0 in bossAt)
                            for (float x = -13; x <= 13; x += 2f)
                                for (float z = -13; z <= 13; z += 2f)
                                {
                                    var p = new Vector3(x, 0, z);
                                    if (p.magnitude > PendulumSentinelRules.Bound - 0.2f || Sim.Flat(p - b0).magnitude < PendulumSentinelRules.BodyRadius + 0.8f) continue;
                                    n++;
                                    if (PendulumSentinelRules.StrokeChainSurvivable(b0, p, count, bpm, speed, lvl, out string w)) ok++;
                                    else if (why == "") why = $"boss ({b0.x},{b0.z}): {w}";
                                }
                        Check($"Return Stroke x{count} at {speed} m/s, {lvl}, {bpm} BPM: the target walks clear of every lane and cleave ({n} positions)", ok == n, ok == n ? "" : $"{ok}/{n}; {why}");
                    }
        }

        // ---------------- Shortening Swing ----------------
        IEnumerator SwingChecks()
        {
            Section("Shortening Swing");
            // standing still next to him: the first arc is aimed at you
            PlaceMe(new Vector3(0, 0, 2.6f)); int start = d.World.Tick, s0 = boss.SwingLog.Count;
            yield return Block("Swing");
            var sl = boss.SwingLog.Skip(s0).ToList();
            Check("four swings: 180, 150, 120 then 90 degrees", sl.Count == 4 && sl.Select(x => x.arc).SequenceEqual(new[] { 180f, 150f, 120f, 90f }));
            Check("swing 1 sized to your walk out, then 1.75, 1.5, 1.5 beats (70, 60, 60 ticks at 90 BPM)",
                sl.Count == 4 && sl[0].warn == Mathf.RoundToInt(boss.LastFirstBeats * 40) && boss.LastFirstBeats <= 3f && sl.Skip(1).Select(x => x.warn).SequenceEqual(new[] { 70, 60, 60 }),
                string.Join(",", sl.Select(x => x.warn)) + $" (swing 1 {boss.LastFirstBeats} beats)");
            Check("standing still next to him: hit (melee is in danger)", HitsSince(start, "Shortening Swing", d.Player) >= 1);
            Overhead("swing_from_close");

            // standing still at long range: he lunges at you first
            yield return Until(() => Sim.Flat(boss.transform.position).magnitude < 5f, 10, "near the centre (swing)");
            PlaceMe(new Vector3(0, 0, 12.5f)); start = d.World.Tick; int l0 = boss.LungeCount;
            yield return Block("Swing");
            Check("standing still at 12.5 m: he lunges to 3 m from you first (no damage from the lunge)", boss.LungeCount == l0 + 1 && Mathf.Abs(boss.LastLungeGap - PendulumSentinelRules.LungeStop) < 0.35f, $"gap {boss.LastLungeGap:0.00} m");
            Check("standing still at 12.5 m: hit (ranged is in danger)", HitsSince(start, "Shortening Swing 1", d.Player) == 1);
            Check("swing 1 after the lunge is short (at most 3 beats, 2 s; it was 6 beats)", boss.LastFirstBeats <= 3f, $"{boss.LastFirstBeats} beats");

            // a walker that reads each arc and steps to its nearest safe spot, from long range
            PlaceMe(new Vector3(4, 0, 12)); start = d.World.Tick; s0 = boss.SwingLog.Count;
            int seen = s0; Vector3? g = null;
            goalFn = () =>
            {
                if (boss.SwingLog.Count > seen)
                {
                    seen = boss.SwingLog.Count; var s = boss.SwingLog[seen - 1];
                    g = PendulumSentinelRules.EscapePoint(boss.transform.position, s.yaw, s.arc / 2, d.Player.transform.position, 0.7f);
                }
                return g;
            };
            yield return StartOf("Swing");
            yield return Until(() => boss.SwingLog.Count >= s0 + 4, 20, "four swings");
            yield return Wait(1.2f);
            goalFn = null;
            Check("a walker reading the arcs from 12 m out takes no swing", HitsSince(start, "Shortening Swing", d.Player) == 0, $"{HitsSince(start, "Shortening Swing", d.Player)} hits");
            var aims = boss.SwingLog.Skip(s0).ToList();
            Check("every swing turns to face the target where they stand at its warning", aims.Count == 4 && aims.All(a => Vector3.Angle(Sim.Dir(a.yaw), Sim.Flat(a.aimedAt - boss.transform.position)) < 2f));
            yield return BetweenBlocks();
        }

        // ---------------- Counterweight Balance ----------------
        IEnumerator WeightChecks()
        {
            Section("Counterweight Balance (solo)");
            PlaceMe(new Vector3(0, 0, 3)); int start = d.World.Tick, w0 = boss.WeightLog.Count, sp0 = boss.WeightSpawnLog.Count, orbs0 = boss.OrbsFired, oe0 = boss.OrbEnds.Count, sk0 = boss.SoakLog.Count;
            // first weight: stand under it as it lands; next: step into one lying on the floor; the rest: stay clear so they burst
            goalFn = () =>
            {
                var live = d.World.Hazards.Live.Where(h => h.Source == "Counterweight" && !h.Cancelled).OrderBy(h => h.ResolveAt - h.Age).ToList();
                var mine = boss.WeightLog.Skip(w0).ToList();
                bool landed = mine.Any(x => x.catchers > 0 && !x.late), late = mine.Any(x => x.catchers > 0 && x.late);
                var lying = boss.LyingSpots;
                if (!landed && live.Count > 0) return live[0].Origin;
                if (landed && !late && lying.Count > 0) return lying[0];
                return ClearSpot(live.Select(h => h.Origin).Concat(lying).ToList());
            };
            yield return StartOf("Weights");
            yield return Until(() => boss.WeightLog.Count >= w0 + 1, 15, "first landing");
            yield return Until(() => boss.LyingCount > 0, 10, "a weight lying on the floor");
            Overhead("weights_lying");
            yield return Until(() => boss.OrbCount > 0, 10, "a burst");
            yield return Wait(1.0f);
            Overhead("weights_orbs");
            yield return BetweenBlocks(60);
            goalFn = null;
            var wl = boss.WeightLog.Skip(w0).ToList(); var sp = boss.WeightSpawnLog.Skip(sp0).ToList(); var sk = boss.SoakLog.Skip(sk0).ToList();
            Check("three throws of three weights, each absorbed or burst once", wl.Count == 9, $"{wl.Count} resolved");
            Check("solo: the three weights of a throw land one after another, 2 beats apart", sp.Count >= 3 && sp[1].delay - sp[0].delay == 80 && sp[2].delay - sp[1].delay == 80);
            Check($"under a weight as it lands: absorbed, the lone soaker takes the full {PendulumSentinelEncounter.WeightDmg}", wl.Any(x => x.catchers == 1 && !x.late && x.share == PendulumSentinelEncounter.WeightDmg));
            Check("stepping into a weight lying on the floor absorbs it (no orbs from it)", wl.Any(x => x.catchers == 1 && x.late) && sk.Any(x => x.absorbed));
            var burst = sk.Where(x => !x.absorbed).ToList();
            Check("an unabsorbed weight lies 3 beats (120 ticks), then bursts", burst.Count >= 1 && burst.All(x => Mathf.Abs(x.end - x.land - 120) <= 1), string.Join(",", burst.Select(x => x.end - x.land)));
            int misses = wl.Count(x => x.catchers == 0);
            Check("each burst weight fires 9 orbs", misses >= 1 && boss.OrbsFired - orbs0 == 9 * misses, $"{misses} burst, {boss.OrbsFired - orbs0} orbs");
            var ends = boss.OrbEnds.Skip(oe0).ToList(); var free = ends.Where(x => !x.popped).ToList();
            Check("every orb that hits no one bounces off the edge 3-4 times, then vanishes",
                free.Count > 0 && free.All(x => x.bounces == x.max && x.max >= 3 && x.max <= 4) && free.Any(x => x.max == 3) && free.Any(x => x.max == 4),
                $"{free.Count} free orbs; bounces {string.Join(",", free.Select(x => x.bounces).Distinct())}");
            Check("orbs never leave the floor", boss.OrbFarthest <= CombatWorld.PlayerBound + 0.01f, $"{boss.OrbFarthest:0.00} m");
            Check("a weight hurts only those who absorb it (only its orbs can hit otherwise)", HitsSince(start, "Counterweight (caught)", d.Player) == wl.Count(x => x.catchers == 1));
            Overhead("weights_after");
        }


        // ---------------- Plumb Line ----------------
        IEnumerator PlumbChecks()
        {
            Section("Plumb Line");
            PlaceMe(new Vector3(0, 0, 2.6f)); int start = d.World.Tick;
            yield return Block("Plumb");
            Check("standing still next to him: hit (no mark is within 3.8 m of him)", HitsSince(start, "Plumb Line", d.Player) == 1);
            PlaceMe(new Vector3(0, 0, 13.4f)); start = d.World.Tick;
            yield return Block("Plumb");
            Check("standing still at the edge: hit (marks are at most 10 m out)", HitsSince(start, "Plumb Line", d.Player) == 1);
            // a walker heading for the nearest mark
            PlaceMe(new Vector3(-6, 0, 9)); start = d.World.Tick;
            goalFn = () => boss.LiveMarks != null ? boss.LiveMarks.OrderBy(m => Sim.Flat(m - d.Player.transform.position).magnitude).First() : (Vector3?)null;
            yield return StartOf("Plumb");
            yield return Until(() => boss.PlumbActive, 15, "marks drawn");
            yield return Wait(0.5f);
            Overhead("plumb_marks");
            yield return BetweenBlocks(30);
            goalFn = null;
            var pl = boss.PlumbLog.Last();
            Check("four small level marks for a solo player", pl.marks.Count == 4 && Mathf.Approximately(pl.radius, PendulumSentinelEncounter.MarkRadiusMedium));
            Check("a walker who reaches a mark is safe", HitsSince(start, "Plumb Line", d.Player) == 0);

            // crowding: three players in one mark crack it
            Section("Plumb Line crowding (co-op roster)");
            var a = AddExtra(new Vector3(0, 0, 5)); var b = AddExtra(new Vector3(0, 0, 5));
            start = d.World.Tick;
            yield return StartOf("Plumb");
            yield return Until(() => boss.PlumbActive && boss.LiveMarks != null, 15, "marks drawn (3 players)");
            var mark = boss.LiveMarks[0];
            PlaceMe(mark); Place(a, mark + new Vector3(0.6f, 0, 0)); Place(b, mark + new Vector3(-0.6f, 0, 0));
            goalFn = () => boss.LiveMarks != null ? boss.LiveMarks[0] : (Vector3?)null;
            float until = Time.time + 8;
            while (boss.PlumbActive && Time.time < until) { if (boss.LiveMarks != null) { Place(a, boss.LiveMarks[0] + new Vector3(0.6f, 0, 0)); Place(b, boss.LiveMarks[0] + new Vector3(-0.6f, 0, 0)); } Keep(); yield return null; }
            goalFn = null;
            yield return BetweenBlocks(10);
            Check("three in one mark: it cracks and all three are hit", boss.PlumbCracks >= 1 && HitsSince(start, "Plumb Line", d.Player) == 1 && HitsSince(start, "Plumb Line", a) == 1 && HitsSince(start, "Plumb Line", b) == 1);
            RemoveExtras();
        }

        // ---------------- Return Stroke ----------------
        IEnumerator StrokeChecks()
        {
            Section("Return Stroke");
            yield return Until(() => Sim.Flat(boss.transform.position).magnitude < 5f, 10, "near the centre");
            PlaceMe(boss.transform.position + new Vector3(0, 0, 2.8f)); int start = d.World.Tick, st0 = boss.StrokeLog.Count, stuck0 = boss.StuckLog.Count;
            yield return StartOf("Stroke");
            yield return Until(() => boss.Stuck, 15, "stuck at the edge");
            Check("standing still in front of him: the charge hits (melee)", HitsExact(start, "Return Stroke", d.Player) == 1);
            Check("the stuck window opens with nothing live", d.World.Hazards.Count == 0, $"{d.World.Hazards.Count} live");
            var s = boss.StrokeLog.Last();
            Check("the lane reaches the floor's edge; his body stays inside the boss bound", s.length >= PendulumSentinelRules.EdgeDistance(s.from, Sim.Dir(s.yaw), CombatWorld.PlayerBound) - 0.01f && s.end.magnitude <= CombatWorld.BossBound + 0.05f);
            var c = boss.CleaveLog.LastOrDefault();
            Check("the cleave lands 0.75 beat after he hits the edge, round where he stopped", boss.CleaveLog.Count > 0 && Mathf.Abs(c.tick - s.tick - Mathf.RoundToInt(0.75f * boss.BeatTicks)) <= 2 && Sim.Flat(c.at - s.end).magnitude < 0.05f, $"{c.tick - s.tick} ticks after landing");
            int hp = boss.Hp; boss.TakeHit(50, boss.transform.position + Vector3.up * 2);
            Check("stuck: he can be damaged (the punish window)", boss.Hp == hp - 50);
            yield return BetweenBlocks(20);
            var sw = boss.StuckLog.Skip(stuck0).ToList();
            Check("stuck for 3.5 s after slamming the edge", sw.Count == 1 && sw[0].end - sw[0].start == PendulumSentinelEncounter.StuckTicks);
            RenderShot("stroke_stuck", boss.transform.position + new Vector3(0, 6, 9), boss.transform.position + Vector3.up * 2, 45);

            // long range, in the lane: the blow carries to the edge
            yield return Until(() => Sim.Flat(boss.transform.position).magnitude < 5f, 10, "back toward the centre");
            var away = Sim.Flat(boss.transform.position).sqrMagnitude > 0.5f ? -Sim.Flat(boss.transform.position).normalized : Vector3.forward;
            PlaceMe(boss.transform.position + away * 12f); start = d.World.Tick;
            yield return Block("Stroke");
            Check("standing still at long range in the lane: hit (ranged)", HitsExact(start, "Return Stroke", d.Player) == 1);

            // beside the lane near where he stops: the cleave behind him (an extra stands in front as the target)
            yield return Until(() => Sim.Flat(boss.transform.position).magnitude < 5f, 10, "back toward the centre (2)");
            var fwd = Sim.Flat(-boss.transform.position).sqrMagnitude > 0.5f ? Sim.Flat(-boss.transform.position).normalized : Vector3.forward;
            AddExtra(boss.transform.position + fwd * 2.0f);   // the nearest player: the stroke goes their way
            var lane = PendulumSentinelRules.StrokeLane(boss.transform.position, boss.transform.position + fwd * 2.0f, fwd);
            var side = Vector3.Cross(Vector3.up, lane.dir);
            PlaceMe(lane.end - lane.dir * 3.5f + side * 3.5f); start = d.World.Tick;
            float until = Time.time + 8;
            yield return StartOf("Stroke");
            while (!boss.Stuck && Time.time < until) { Keep(); yield return null; }
            Check("standing still beside the lane near where he stops: the charge misses, the cleave hits", HitsExact(start, "Return Stroke", d.Player) == 0 && HitsExact(start, "Return Stroke (cleave)", d.Player) == 1,
                $"charge {HitsExact(start, "Return Stroke", d.Player)}, cleave {HitsExact(start, "Return Stroke (cleave)", d.Player)}");
            yield return BetweenBlocks(20);
            RemoveExtras();
        }

        // ---------------- difficulty ----------------
        IEnumerator DifficultyChecks()
        {
            Section("difficulty");
            d.World.Difficulty = Difficulty.Hard;
            PlaceMe(new Vector3(0, 0, 3)); int sp = spawned.Count;
            yield return Block("Swing");
            int back = spawned.Skip(sp).Count(x => x.source.StartsWith("Backswing"));
            Check("Hard adds Backswing: swings 3 and 4 also hit the arc behind him", back == 2, $"{back} backswing arcs");
            d.World.Difficulty = Difficulty.Easy;
            yield return Block("Swing");
            Check("Easy: a beat more on every swing (swings 2-4 2.75, 2.5, 2.5 beats; swing 1 at least 2.5)", boss.LastSwingWarns.Skip(1).SequenceEqual(new[] { 110, 100, 100 }) && boss.LastSwingWarns[0] >= 100, string.Join(",", boss.LastSwingWarns));
            d.World.Difficulty = Difficulty.Medium;
        }

        // ---------------- the 50% transformation and phase 2 ----------------
        IEnumerator PhaseTwoChecks()
        {
            Section("the 50% transformation and phase 2");
            PlaceMe(new Vector3(0, 0, 3));
            yield return StartOf("Swing");
            boss.DebugSetHpFraction(0.505f);
            boss.TakeHit(100, boss.transform.position + Vector3.up * 2);
            Check("crossing 50% mid-attack: the attack carries on", !boss.Phase2 && boss.ShiftStart < 0 && boss.Current.StartsWith("Shortening Swing"));
            yield return Until(() => boss.ShiftStart >= 0, 30, "transformation starts");
            Check("the transformation waits for the attack to finish", boss.BlockLog.Last().EndsWith("Shift"));
            int hp = boss.Hp; boss.TakeHit(60, boss.transform.position + Vector3.up * 2);
            Check("he stays damageable during the transformation (no HP hold, no heal)", boss.Hp == hp - 60 && boss.Hp < boss.HpAtShiftStart);
            int haz = d.World.Hazards.Count;
            Check("the transformation is harmless (no hazards)", haz == 0, $"{haz} live");
            yield return Until(() => boss.Phase2, 5, "phase 2");
            Check("2 s transformation, then 112 BPM", boss.ShiftEnd - boss.ShiftStart == PendulumSentinelEncounter.ShiftTicks && Mathf.Approximately(boss.Bpm, 112) && boss.BeatTicks == 32);

            // the drifting plumb line opens phase 2
            yield return Until(() => boss.PlumbActive && boss.LiveMarks != null, 20, "drifting Plumb Line");
            var before = boss.LiveMarks.Select(m => m).ToList();
            goalFn = () => boss.LiveMarks != null ? boss.LiveMarks.OrderBy(m => Sim.Flat(m - d.Player.transform.position).magnitude).First() : (Vector3?)null;
            int start = d.World.Tick;
            yield return BetweenBlocks(20);
            goalFn = null;
            var after = boss.PlumbLog.Last().marks;
            float moved = Vector3.Angle(before[0], after[0]);
            float driftTotal = PendulumSentinelEncounter.DriftPerBeat * PendulumSentinelEncounter.DriftBeats;
            Check($"below 50% the marks drift {driftTotal:0} degrees round him before locking", Mathf.Abs(moved - driftTotal) < 3f, $"{moved:0.0} degrees");
            Check("a walker following its mark is safe", HitsSince(start, "Plumb Line", d.Player) == 0);

            // swing + counterweight, with a second player: swings 2 and 4 turn to the farthest
            var far = AddExtra(new Vector3(-9, 0, 6)); int s0 = boss.SwingLog.Count, w0 = boss.WeightSpawnLog.Count;
            PlaceMe(new Vector3(0, 0, 3));
            yield return Block("SwingWeights", 40);
            var sl = boss.SwingLog.Skip(s0).ToList();
            Check("below 50%: swings 2 and 4 turn to the farthest player", sl.Count == 4 && Vector3.Angle(Sim.Flat(sl[1].aimedAt), Sim.Flat(far.transform.position)) < 25f && Vector3.Angle(Sim.Flat(sl[3].aimedAt), Sim.Flat(far.transform.position)) < 25f);
            Check("a set of weights lands during the combo", boss.WeightSpawnLog.Count - w0 == 3);
            RemoveExtras();

            // triple stroke: a walker who reads each lane and cleave and walks clear
            int st0 = boss.StrokeLog.Count, stk0 = boss.StuckLog.Count, cl0 = boss.CleaveLog.Count;
            PlaceMe(new Vector3(5, 0, 5)); int start2 = d.World.Tick;
            goalFn = () => StrokeGoal();
            yield return Block("Stroke", 40);
            goalFn = null;
            var strokes = boss.StrokeLog.Skip(st0).ToList();
            Check("below 50%: three strokes, then one stuck window", strokes.Count == 3 && boss.StuckLog.Count - stk0 == 1 && boss.CleaveLog.Count - cl0 == 3);
            // each charge lands 3.25 beats after the last one (2.5-beat lane, 0.75-beat charge): 104 ticks at 112 BPM
            var (sh2, da2, _) = PendulumSentinelRules.StrokeBeats(Difficulty.Medium, true); int gapT = Mathf.RoundToInt(sh2 * boss.BeatTicks) + Mathf.RoundToInt(da2 * boss.BeatTicks);
            Check($"the three charges come back to back (one every {gapT / 60f:0.00} s)", strokes.Count == 3 && strokes.Zip(strokes.Skip(1), (a, b) => b.tick - a.tick).All(g => Mathf.Abs(g - gapT) <= 2),
                string.Join(",", strokes.Zip(strokes.Skip(1), (a, b) => b.tick - a.tick)));
            Check("a walker reading each lane and cleave takes no stroke hit", HitsSince(start2, "Return Stroke", d.Player) == 0, $"{HitsSince(start2, "Return Stroke", d.Player)} hits");
        }

        // ---------------- defeat ----------------
        IEnumerator DefeatChecks()
        {
            Section("defeat");
            PlaceMe(new Vector3(0, 0, 3));
            yield return StartOf("Weights");
            yield return Until(() => boss.LyingCount > 0, 15, "a weight lying on the floor");
            boss.DebugDefeat();
            yield return null;
            Check("defeat: weights, lying soaks, orbs and telegraphs are cleared at once", boss.OrbCount == 0 && boss.LyingCount == 0 && d.World.Hazards.Count == 0);
            yield return Until(() => d.State == RunState.Results, 10, "results");
        }

        // ---------------- helpers ----------------
        // a stroke walker: while any lane or cleave is live, keep to the nearest spot clear of all of them (with a buffer)
        Vector3? strokeGoal;
        Vector3? StrokeGoal()
        {
            var live = d.World.Hazards.Live.Where(h => h.Source.StartsWith("Return Stroke") && !h.Cancelled).ToList();
            if (live.Count == 0) { strokeGoal = null; return null; }
            bool Clear(Vector3 q)
            {
                if (Sim.Flat(q - boss.transform.position).magnitude < PendulumSentinelRules.BodyRadius + 1f) return false;
                foreach (var h in live)
                    for (int k = 0; k < 9; k++)
                    {
                        var o = k == 0 ? Vector3.zero : Sim.Dir(k * 45f) * 0.6f;
                        if (h.Contains(q + o)) return false;
                    }
                return true;
            }
            if (strokeGoal.HasValue && Clear(strokeGoal.Value)) return strokeGoal;
            var p = Sim.Flat(d.Player.transform.position); Vector3? best = null; float bd = float.MaxValue;
            for (float a = 0; a < 360; a += 6)
                for (float r = 0; r < 16f && r < bd; r += 0.25f)
                {
                    var q = PendulumSentinelRules.Clamp(p + Sim.Dir(a) * r, 0.6f);
                    if (Clear(q)) { float dd = Sim.Flat(q - p).magnitude; if (dd < bd) { bd = dd; best = q; } break; }
                }
            strokeGoal = best ?? p;
            return strokeGoal;
        }
        // a spot 3 m from him, as far as possible from every listed weight
        Vector3 ClearSpot(List<Vector3> avoid)
        {
            Vector3 best = boss.transform.position + Vector3.forward * 3; float bd = -1;
            for (float a = 0; a < 360; a += 30)
            {
                var c = Sim.Flat(boss.transform.position) + Sim.Dir(a) * 3f; float m = avoid.Count == 0 ? 99 : avoid.Min(o => Sim.Flat(o - c).magnitude);
                if (m > bd) { bd = m; best = c; }
            }
            return best;
        }
        PlayerController AddExtra(Vector3 at)
        {
            var p = PlayerController.Spawn(d.World, RunDirector.Classes[(extras.Count + 1) % 3], d.World.Constrain(at, PlayerController.Radius), 180);
            p.Input = new Bot(); p.AbilitiesEnabled = false; extras.Add(p); return p;
        }
        void RemoveExtras()
        {
            foreach (var p in extras) if (p) { d.World.Players.Remove(p); Destroy(p.gameObject); }
            extras.Clear();
        }

        void Finish()
        {
            Check("no runtime errors", errors == 0, errors + " logged");
            lines.Insert(0, $"Pendulum Sentinel autotest: {pass} passed, {fail} failed  ({(Application.isEditor ? "Editor Play Mode" : "Windows player")}, Unity {Application.unityVersion})");
            File.WriteAllLines(Path.Combine(dir, "pendulum-sentinel-report.txt"), lines);
            Debug.Log($"[pendulum-sentinel-test] done: {pass} passed, {fail} failed");
            StartCoroutine(Quit());
        }
        IEnumerator Quit()
        {
            yield return new WaitForSeconds(0.5f);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            UnityEditor.EditorApplication.Exit(fail == 0 ? 0 : 1);
#else
            Application.Quit(fail == 0 ? 0 : 1);
#endif
        }
    }
}
