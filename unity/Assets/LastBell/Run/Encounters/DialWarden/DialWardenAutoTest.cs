using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Acceptance run for the Dial Warden inside the real game:  -dialWardenTest -lastbellOut <folder>
    // Pure rules first (QTE outcomes, lit hours, the dial's geometry, route checks over hundreds of generated patterns),
    // then the fight on Skyward Spire through the director's public requests. Bots send ordinary PlayerInput; a scripted
    // QTE source presses keys through the same path as the keyboard. Labelled dev hooks: ForcedNext, ForceLitCount,
    // ForceEnrage, boss HP fraction / defeat, player DebugSetHp, direct player placement, extra roster players (not
    // co-op). Writes dial-warden-report.txt plus screenshots and exits 0 / 1.
    public sealed class DialWardenAutoTest : MonoBehaviour
    {
        public static bool Requested => Environment.GetCommandLineArgs().Any(a => a.Equals("-dialWardenTest", StringComparison.OrdinalIgnoreCase));
        static string OutDir
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.FindIndex(args, a => a.Equals("-lastbellOut", StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Application.persistentDataPath, "dial-warden-test");
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Requested || FindAnyObjectByType<DialWardenAutoTest>()) return;
            var go = new GameObject("Dial Warden autotest"); DontDestroyOnLoad(go); go.AddComponent<DialWardenAutoTest>();
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

        // follows a planned route: the cell for the next 9-tick step (DialWardenRoutes.Plan), timeline tick 0 = origin
        sealed class PlanBot : IInputSource
        {
            public List<Vector3> Path; public int Origin; public Func<int> Tick;
            public PlayerInput Read(PlayerController who)
            {
                var r = new PlayerInput { Aim = who.transform.position + who.transform.forward };
                if (Path == null || Path.Count == 0) return r;
                int t = Tick() - Origin, s = t < 0 ? -1 : t / DialWardenRoutes.StepTicks;
                var goal = Path[Mathf.Clamp(s + 1, 0, Path.Count - 1)];
                var to = Sim.Flat(goal - who.transform.position);
                if (to.magnitude > 0.06f) r.Move = ToMove(to.normalized * Mathf.Min(1, to.magnitude / 0.1f));
                return r;
            }
        }

        // scripted QTE presses through the same IQteSource path as the keyboard
        public enum QteMode { Correct, Wrong, Early, None }
        sealed class ScriptQte : IQteSource
        {
            public QteMode Mode; public Func<DialWardenEncounter> Boss; public PlayerController Me;
            int lastPress = -999; bool wrongDone;
            public void Begin() { lastPress = -999; wrongDone = false; }
            public void Drain(List<QteKey> into)
            {
                var b = Boss(); var q = b ? b.PromptFor(Me, out _) : null;
                if (q == null || !q.Open || Mode == QteMode.None) return;
                int now = b.Tick;
                if (Mode == QteMode.Wrong && !wrongDone && now - q.OpenTick >= 10) { wrongDone = true; into.Add(q.Sequence[q.Index] == QteKey.Up ? QteKey.Down : QteKey.Up); return; }
                if (!q.OnParry) { if (now - lastPress >= 6 && now - q.OpenTick >= 8) { into.Add(q.Sequence[q.Index]); lastPress = now; } return; }
                if (Mode == QteMode.Early || (Mode == QteMode.Correct && now >= q.ArriveTick)) into.Add(QteKey.Parry);
            }
        }

        RunDirector d; readonly Bot bot = new Bot();
        readonly List<string> lines = new List<string>(); int pass, fail, errors, shots;
        readonly List<(int tick, string source, PlayerController who, int dmg)> hits = new List<(int, string, PlayerController, int)>();
        readonly List<PlayerController> extras = new List<PlayerController>();
        string dir; bool keepAlive = true;
        DialWardenEncounter boss;
        ScriptQte qte;

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
            lines.Add(line); Debug.Log("[dial-warden-test] " + line);
        }
        void Note(string s) { lines.Add("      " + s); Debug.Log("[dial-warden-test] " + s); }
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
        void Overhead(string name) => RenderShot(name, new Vector3(0, 30, 13), new Vector3(0, 0, -0.5f), 50);

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
            foreach (var p in d.World.Players) if (keepAlive && !p.Down && p.Hp < 61) p.DebugSetHp(100);
            var me = d.Player;
            var g = goalFn != null ? goalFn() : goal;
            if (me && g.HasValue && me.Input == bot) { var to = Sim.Flat(g.Value - me.transform.position); bot.MoveWorld = to.magnitude > 0.15f ? to.normalized : Vector3.zero; }
            else bot.MoveWorld = Vector3.zero;
        }
        void OnHit(PlayerController p, Hazard h, int dmg) => hits.Add((d.World.Tick, h.Source, p, dmg));
        void Place(PlayerController p, Vector3 at) { p.transform.position = d.World.Constrain(new Vector3(at.x, 0, at.z), PlayerController.Radius); }
        void PlaceMe(Vector3 at) { Place(d.Player, at); goal = d.Player.transform.position; goalFn = null; d.Player.Input = bot; }
        int HitsSince(int tick, string prefix, PlayerController who = null) => hits.Count(h => h.tick >= tick && h.source.StartsWith(prefix) && (who == null || h.who == who));
        IEnumerable<int> DamageSince(int tick, string source, PlayerController who) => hits.Where(h => h.tick >= tick && h.source == source && h.who == who).Select(h => h.dmg);

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
        IEnumerator EndOfBlock(string what, float timeout = 60f) { yield return Until(() => boss.Current == "between blocks" || boss.Dead, timeout, what + " ends"); }

        // ======================================================================================================
        IEnumerator Main()
        {
            Section("pure rules");
            yield return new WaitForSeconds(1f);
            PureQte(); PureDial();
            DialWardenRoutes.Bound = RunMaps.All[RunMaps.IndexOf(RunDirector.AurelionMap)].Bound;
            PureRoutes();

            // ---------------- practice start ----------------
            Section("registration and practice start");
            int map = RunMaps.IndexOf(RunDirector.AurelionMap), enc = RunDirector.DialWardenPractice;
            Check("practice entry registered on Skyward Spire, practice only", enc >= 0 && RunDirector.Bosses[enc].Map == "skyward-spire" && !RunDirector.Bosses[enc].InRun);
            Check("run order unchanged (4 run bosses, Dial Warden excluded)", RunDirector.RunOrder.Length == 4 && !RunDirector.RunOrder.Contains(enc));
            Check("Aurelion's practice still resolves to Aurelion", RunDirector.AurelionPractice >= 0 && RunDirector.Bosses[RunDirector.AurelionPractice].Name.StartsWith("Aurelion"));
            Check("listed in the Minibosses row", LastBell.Menu.TestRushSetup.MinibossRow.Contains(enc) && !LastBell.Menu.TestRushSetup.BossRow.Contains(enc));
            float boot = 0; while (RunDirector.Instance == null && boot < 4) { boot += Time.deltaTime; yield return null; }
            // the player boots into the main menu, which has no director: load the arena's run scene like the menu would
            if (RunDirector.Instance == null) UnityEngine.SceneManagement.SceneManager.LoadScene(RunMaps.All[map].RunScene);
            float waitMenu = 0; while ((RunDirector.Instance == null || RunDirector.Instance.State != RunState.Menu) && waitMenu < 30) { waitMenu += Time.deltaTime; yield return null; }
            if (RunDirector.Instance == null) { Check("run scene loads", false, "no RunDirector after 30 s"); Finish(); yield break; }
            d = RunDirector.Instance;
            Check("practice request accepted", d.StartPractice("Vanguard", enc));
            float t0 = 0; while ((RunDirector.Instance == null || RunDirector.Instance.State != RunState.Ready) && t0 < 30) { t0 += Time.deltaTime; yield return null; }
            d = RunDirector.Instance;
            Check("practice Ready on Skyward Spire", d && d.State == RunState.Ready && RunMaps.Current == map && d.Mode == RunMode.Practice && d.Encounter == enc);
            yield return BeginFight(20261026);
            Check("fight opens in phase 1 at 120 BPM", !boss.Phase2 && Mathf.Approximately(boss.Bpm, 120) && boss.BeatTicks == 30);
            Check("the Meshy model is loaded and rigged (not the stand-in)", !boss.Placeholder && boss.Presenter && boss.Presenter.HasModel);
            var clips = new[] { "Idle", "Walk", "SkyRaise", "SkyLoop", "Hoist", "HoistLoop", "WeightSlam", "BalanceLoop", "HandPlant", "HandLoop", "Caught", "BreakThrough", "GroundSlam", "Collapse", "PhaseShift", "Defeat" };
            var missing = clips.Where(c => !boss.Presenter.Has(c)).ToList();
            Check("all 16 animation clips imported", missing.Count == 0, missing.Count > 0 ? "missing: " + string.Join(", ", missing) : "");
            Check("no ability text: empty phase line and intro lesson, diagnostics off by default", boss.PhaseName == "" && boss.Lesson == "" && !DialWardenQteHud.ShowDiagnostics);
            yield return Until(() => Sim.Flat(boss.transform.position).magnitude < 0.35f, 8, "walks to the centre");
            Check("he takes the centre of the dial", Sim.Flat(boss.transform.position).magnitude < 0.35f);
            RenderShot("model_front", new Vector3(0, 3.2f, 7.5f), new Vector3(0, 1.4f, 0), 40);

            yield return ArrestChecks();
            yield return ConstellationChecks();
            yield return ScalesChecks();
            yield return MainspringChecks();
            DifficultyChecks();
            yield return PhaseTwoChecks();
            yield return MidnightAndDefeat();
            yield return TwoMissesKill();
            Finish();
        }

        IEnumerator BeginFight(int seed)
        {
            d.Player.Input = bot; goal = null; goalFn = null; hits.Clear();   // a retry builds a new world whose tick restarts at 0
            d.World.PlayerHit -= OnHit; d.World.PlayerHit += OnHit;
            Check("Ready accepted", d.RequestReady());
            yield return Until(() => d.Boss is DialWardenEncounter, 3, "boss spawned");
            boss = d.Boss as DialWardenEncounter;
            boss.Seed = seed;
            d.World.Difficulty = Difficulty.Medium;   // a saved menu choice must not change the measured numbers
            DialWardenQteHud.ShowDiagnostics = false;
            qte = new ScriptQte { Boss = () => boss, Me = d.Player, Mode = QteMode.Correct };
            boss.QteSources[d.Player] = qte;
            yield return Until(() => d.State == RunState.Combat, 6, "combat");
        }

        // ---------------- pure rules ----------------
        void PureQte()
        {
            var seq = new[] { QteKey.Up, QteKey.Left, QteKey.Right, QteKey.Parry };
            ParryQte Q() => new ParryQte(seq, 100, 200, 8);
            var a = Q(); a.Press(QteKey.Up, 110); a.Press(QteKey.Left, 120); a.Press(QteKey.Right, 130); a.Press(QteKey.Parry, 195);
            Check("QTE: the shown directions then E inside the window succeeds", a.Result == ParryQte.Outcome.Success && a.ResultTick == 195);
            var b = Q(); b.Press(QteKey.Up, 110); b.Press(QteKey.Down, 120);
            Check("QTE: a wrong direction fails at once", b.Result == ParryQte.Outcome.Fail && b.FailReason == "wrong key" && b.WrongKey == QteKey.Down);
            var c = Q(); c.Press(QteKey.Up, 110); c.Press(QteKey.Parry, 120);
            Check("QTE: E before the directions are done fails", c.Result == ParryQte.Outcome.Fail && c.FailReason == "wrong key");
            var e = Q(); e.Press(QteKey.Up, 110); e.Press(QteKey.Left, 120); e.Press(QteKey.Right, 130); e.Press(QteKey.Parry, 191);
            Check("QTE: E one tick before the window fails as early", e.Result == ParryQte.Outcome.Fail && e.FailReason == "early");
            var f = Q(); f.Press(QteKey.Up, 110); f.Press(QteKey.Left, 120); f.Press(QteKey.Right, 130); f.Press(QteKey.Parry, 208);
            Check("QTE: E on the window's last tick still succeeds", f.Result == ParryQte.Outcome.Success);
            var g = Q(); g.Press(QteKey.Up, 110); for (int t = 111; t <= 209; t++) g.Step(t);
            Check("QTE: nothing by the end of the window fails as late", g.Result == ParryQte.Outcome.Fail && g.FailReason == "late" && g.ResultTick == 209);
            var h = Q(); h.Delay(24); h.Press(QteKey.Up, 110); h.Press(QteKey.Left, 120); h.Press(QteKey.Right, 130); h.Press(QteKey.Parry, 224);
            Check("QTE: a held hand moves the arrival (and the window) later", h.ArriveTick == 224 && h.Result == ParryQte.Outcome.Success);
            var r = new System.Random(7); bool rollOk = true;
            for (int i = 0; i < 2000; i++)
            {
                int n = 2 + i % 4; var s = ParryQte.Roll(r, n);
                if (s.Length != n + 1 || s[n] != QteKey.Parry) rollOk = false;
                for (int k = 0; k < n; k++) { if (s[k] == QteKey.Parry || (k > 0 && s[k] == s[k - 1])) rollOk = false; }
            }
            Check("QTE rolls: N directions then E, never the same direction twice in a row (2000 rolls)", rollOk);
        }

        void PureDial()
        {
            Check("hour 12 is north (top of the screen), 3 east, 6 south, 9 west",
                (DialWardenRoutes.HourPos(0) - new Vector3(0, 0, -10)).magnitude < 0.01f && (DialWardenRoutes.HourPos(3) - new Vector3(10, 0, 0)).magnitude < 0.01f &&
                (DialWardenRoutes.HourPos(6) - new Vector3(0, 0, 10)).magnitude < 0.01f && (DialWardenRoutes.HourPos(9) - new Vector3(-10, 0, 0)).magnitude < 0.01f);
            bool steps = true;
            for (int s = 0; s <= 12; s++)
                foreach (var cw in new[] { true, false })
                    if (Vector3.Angle(Sim.Dir(DialWardenRoutes.HandYaw(s, cw)), DialWardenRoutes.HourPos(DialWardenRoutes.HourAtStep(s, cw))) > 0.1f) steps = false;
            Check("the hand points at the lit hour on the step it arrives (both directions)", steps);
            var r = new System.Random(11); bool lit = true;
            for (int i = 0; i < 2000; i++)
            {
                int n = 1 + i % 4; var l = DialWardenRoutes.LitSteps(r, n);
                if (l.Count != n || l.Any(s => s < 3 || s > 11)) lit = false;
                for (int k = 1; k < l.Count; k++) if (l[k] - l[k - 1] < 2) lit = false;
            }
            Check("lit hours: 1-4 hours, steps 3-11, at least 2 apart (2000 rolls)", lit);
            var p = DialWardenRoutes.HourPos(3) * 0.8f; float y25 = DialWardenRoutes.HandYaw(2.5f, true), y35 = DialWardenRoutes.HandYaw(3.5f, true);
            Check("the hand passes a player in its path", DialWardenRoutes.HandCrosses(p, y25, y35, DialWardenRoutes.SafeRadius));
            Check("the hand misses a player behind it", !DialWardenRoutes.HandCrosses(p, DialWardenRoutes.HandYaw(4, true), DialWardenRoutes.HandYaw(5, true), DialWardenRoutes.SafeRadius));
            Check("the safe circle: the hand never reaches inside 4 m", !DialWardenRoutes.HandCrosses(DialWardenRoutes.HourPos(3) * 0.3f, y25, y35, DialWardenRoutes.SafeRadius));
            Check("the broken-through lap reaches everywhere", DialWardenRoutes.HandCrosses(DialWardenRoutes.HourPos(3) * 0.2f, y25, y35, 0));
            var rr = new System.Random(5); bool mirror = true, pair = true;
            for (int i = 0; i < 500; i++)
            {
                var a = DialWardenRoutes.WeightSpot(rr, null); var m = DialWardenRoutes.Mirror(a);
                if ((m + a).magnitude > 0.01f || m.magnitude < 4.9f) mirror = false;
                var b = DialWardenRoutes.PairedSpot(rr, a); float apart = Vector3.Angle(a, b); var m2 = DialWardenRoutes.Mirror(a, b);
                if (apart < 45 || apart > 120.01f || m2.magnitude < DialWardenRoutes.MirrorMin - 0.01f) pair = false;
            }
            Check("Tip the Scales: one weight's target is its mirror across the centre (500 rolls)", mirror);
            Check("Tip the Scales: paired weights 45-120 degrees apart, target at least 4 m out (500 rolls)", pair);
            var cen = DialWardenRoutes.Centroid(new PlayerController[0], out int none);
            Check("centroid of nobody is defined (no division by zero)", none == 0 && cen == Vector3.zero);
        }

        void PureRoutes()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var lvl in new[] { (Difficulty.Easy, 6, 4, 18f, 60f), (Difficulty.Medium, 4, 3, 15f, 60f), (Difficulty.Hard, 3, 3, 13f, 90f) })
            {
                var (level, gapBeats, gapCount, half, turn) = lvl;
                foreach (var (doubled, bpm) in new[] { (false, 120f), (true, 150f) })
                {
                    int beat = Mathf.RoundToInt(3600f / bpm), ok = 0, n = 100; long tries = 0; float worst = 0;
                    var r = new System.Random(1000 + (int)level * 10 + (doubled ? 1 : 0));
                    for (int i = 0; i < n; i++)
                    {
                        int k = 0; DialWardenRoutes.RouteResult res;
                        do
                        {
                            k++;
                            var gold = DialWardenRoutes.Stars(r, doubled ? 5 : 6, null); var silver = doubled ? DialWardenRoutes.Stars(r, 5, gold, 3.5f) : null;
                            var tl = new DialWardenRoutes.Timeline(); tl.Fires.AddRange(DialWardenRoutes.Fires(gold, silver, beat));
                            if (doubled) tl.Waves.Add(PureWave(r, 0, gapCount, half, turn, bpm / 120f, beat, null));
                            res = DialWardenRoutes.Check(tl, (doubled ? Mathf.Max(gapBeats, DialWardenEncounter.ComboGapBeats) : gapBeats) * beat);
                        } while (!res.Ok && k < 40);
                        if (res.Ok) ok++; tries += k; worst = Mathf.Max(worst, k);
                    }
                    Check($"Constellation{(doubled ? " + Mainspring (below 50%)" : "")} on {level}: every cast finds a walking route within 40 rolls ({n} casts)", ok == n,
                        $"{ok}/{n}, {tries / (float)n:0.0} rolls on average, worst {worst}");
                }
                foreach (var (count, bpm) in new[] { (1, 120f), (2, 150f) })
                {
                    int beat = Mathf.RoundToInt(3600f / bpm), ok = 0, n = 100; long tries = 0;
                    var r = new System.Random(2000 + (int)level * 10 + count);
                    for (int i = 0; i < n; i++)
                    {
                        int k = 0; DialWardenRoutes.RouteResult res;
                        do
                        {
                            k++; var tl = new DialWardenRoutes.Timeline(); DialWardenRoutes.Wave first = null;
                            for (int w = 0; w < count; w++) { var wv = PureWave(r, w * 2 * beat, gapCount, half, turn, bpm / 120f, beat, first); first ??= wv; tl.Waves.Add(wv); }
                            res = DialWardenRoutes.Check(tl, (count > 1 ? DialWardenEncounter.TellDouble : DialWardenEncounter.TellSingle) * beat);
                        } while (!res.Ok && k < 40);
                        if (res.Ok) ok++; tries += k;
                    }
                    Check($"Mainspring{(count > 1 ? " double (below 50%)" : "")} on {level}: every cast finds a walking route within 40 rolls ({n} casts)", ok == n, $"{ok}/{n}, {tries / (float)n:0.0} rolls on average");
                }
            }
            var mid = new DialWardenRoutes.Timeline();
            mid.Waves.Add(new DialWardenRoutes.Wave { Start = 0, GapHalf = 0, VOut = 7, VIn = 9, ImplosionDelay = 30 });
            Check("Midnight's gapless wave has no walking route (the checker sees danger)", !DialWardenRoutes.Check(mid, 120).Ok);
            Note($"route checks took {sw.ElapsedMilliseconds} ms");
        }
        static DialWardenRoutes.Wave PureWave(System.Random r, int start, int gaps, float half, float turn, float speed, int beat, DialWardenRoutes.Wave first)
        {
            var outG = first == null ? DialWardenRoutes.EvenGaps(r, gaps) : DialWardenRoutes.Turned(first.OutGaps, (r.Next(2) == 0 ? -1 : 1) * (20f + 20f * (float)r.NextDouble()));
            return new DialWardenRoutes.Wave { Start = start, OutGaps = outG, InGaps = DialWardenRoutes.Turned(outG, (r.Next(2) == 0 ? -1 : 1) * turn), GapHalf = half,
                VOut = DialWardenEncounter.VOut * speed, VIn = DialWardenEncounter.VIn * speed, ImplosionDelay = Mathf.RoundToInt(beat * (speed > 1 ? 1.5f : 1f)) };
        }

        // ---------------- Arrest the Hand ----------------
        IEnumerator ArrestCast(QteMode mode, string label, bool stand = true, List<(PlayerController p, Vector3 at)> others = null)
        {
            qte.Mode = mode;
            boss.ForceLitCount = 1;
            yield return StartOf("Arrest");
            yield return Until(() => boss.LapActive && boss.LitList.Count > 0, 10, label + ": lap set up");
            if (stand) PlaceMe(boss.LitList[0].Pos); else PlaceMe(Sim.Dir(90) * 2.6f);
            if (others != null) foreach (var (p, at) in others) Place(p, at);
        }

        IEnumerator ArrestChecks()
        {
            Section("Arrest the Hand (the QTE)");
            // success, with two extra roster players: one in the safe circle, one in the hand's path off the pads
            var inner = AddExtra(Vector3.zero); var outer = AddExtra(Vector3.zero);
            int start = d.World.Tick;
            yield return ArrestCast(QteMode.Correct, "success", true, null);
            var l = boss.LitList[0];
            Check("solo: one lit hour, steps 3-11", boss.LitList.Count == 1 && l.Step >= 3 && l.Step <= 11);
            Place(inner, Sim.Dir(DialWardenRoutes.HourYaw(l.Hour) + 150) * 3.0f);
            var outerAt = Sim.Dir(DialWardenRoutes.HourYaw(l.Hour) + 150) * 7.5f; Place(outer, outerAt);
            yield return Until(() => boss.PromptFor(d.Player, out _) != null, 8, "prompt opens on the lit hour");
            var q = boss.PromptFor(d.Player, out _);
            int lead = q != null ? q.ArriveTick - q.OpenTick : -1;
            Check("the prompt opens 4 beats before the hand arrives", Mathf.Abs(lead - 4 * boss.BeatTicks) <= 1, $"{lead} ticks");
            Check("Medium: 3 directions then E, 8-tick window", q != null && q.Sequence.Length == 4 && q.Sequence[3] == QteKey.Parry && q.Window == 8);
            Check("braced: rooted on the hour while the prompt is open", !d.Player.MovementEnabled);
            var at0 = d.Player.transform.position; goal = at0 + new Vector3(5, 0, 0);
            yield return Wait(0.3f);
            Check("braced: pushing to walk away does nothing", Sim.Flat(d.Player.transform.position - at0).magnitude < 0.05f);
            yield return Until(() => q.Index >= 1, 3, "first key");
            yield return Shot("qte_box_first_key");
            yield return Until(() => q.OnParry && q.InWindow(boss.Tick), 4, "parry window");
            yield return Shot("qte_box_ring_in_window");
            yield return Until(() => !q.Open, 3, "parry resolves");
            yield return Shot("qte_box_success");
            int catchTick = boss.Tick; float stepsAt = boss.HandSteps;
            Check("E on the beat catches the hand", q.Result == ParryQte.Outcome.Success && boss.Catches == 1 && boss.ParryLog.Last().result == "Success");
            yield return Wait(0.25f);
            Check("a caught hand stops dead for a beat", Mathf.Abs(boss.HandSteps - stepsAt) < 0.05f && Mathf.Abs(stepsAt - l.Step) < 0.05f, $"{stepsAt:0.00} -> {boss.HandSteps:0.00} (hour step {l.Step})");
            Check("braced players are released when the parry resolves", d.Player.MovementEnabled);
            yield return EndOfBlock("Arrest (success)");
            Check("the parrier is never hit by the hand", HitsSince(start, "Arrest the Hand", d.Player) == 0);
            Check("the safe circle: a player within 4 m of him is never hit", HitsSince(start, "Arrest the Hand", inner) == 0);
            var outerHits = DamageSince(start, "Arrest the Hand", outer).ToList();
            Check("the hand hits a player in its path off the pads once, for 35", outerHits.Count == 1 && outerHits[0] == DialWardenEncounter.HandDmg, string.Join(",", outerHits));
            Check("a caught lap has no broken-through lap", HitsSince(start, "Arrest the Hand (broken through)") == 0);
            RemoveExtras();

            // failures
            foreach (var (mode, reason, label) in new[] { (QteMode.Wrong, "wrong key", "a wrong key"), (QteMode.Early, "early", "E too early"), (QteMode.None, "late", "no E") })
            {
                start = d.World.Tick;
                yield return ArrestCast(mode, label);
                if (mode == QteMode.Wrong) { yield return Until(() => boss.ParryLog.Count > 0 && boss.ParryLog.Last().tick >= start, 10, "wrong key resolves"); yield return Shot("qte_box_fail"); }
                yield return EndOfBlock("Arrest (" + label + ")");
                var last = boss.ParryLog.Last();
                Check($"QTE fails on {label}", last.tick >= start && last.result == "Fail" && last.reason == reason, $"{last.result} {last.reason}");
                var mine = DamageSince(start, "Arrest the Hand", d.Player).ToList(); var brk = DamageSince(start, "Arrest the Hand (broken through)", d.Player).ToList();
                Check($"{label}: the hand hits the braced player (35), then breaks through for 55 x 1 miss", mine.SequenceEqual(new[] { DialWardenEncounter.HandDmg }) && brk.SequenceEqual(new[] { DialWardenEncounter.BreakDmg }),
                    $"hand [{string.Join(",", mine)}] break [{string.Join(",", brk)}]");
            }
            // nobody on the hour
            start = d.World.Tick;
            yield return ArrestCast(QteMode.Correct, "unmanned", stand: false);
            yield return Until(() => boss.Breaking, 15, "break-through lap");
            Overhead("arrest_broken_through");
            yield return EndOfBlock("Arrest (unmanned)");
            var um = boss.ParryLog.Last();
            Check("a lit hour nobody stands on is missed", um.result == "Missed" && um.reason == "unmanned");
            Check("unmanned: the safe circle spares the player from the hand, the broken-through lap does not",
                HitsSince(start, "Arrest the Hand", d.Player) - HitsSince(start, "Arrest the Hand (broken through)", d.Player) == 0 && DamageSince(start, "Arrest the Hand (broken through)", d.Player).SequenceEqual(new[] { DialWardenEncounter.BreakDmg }));
            qte.Mode = QteMode.Correct;
        }

        // ---------------- Constellation ----------------
        IEnumerator FollowPlan(DialWardenRoutes.Timeline tl, int origin, string what)
        {
            var g = DialWardenRoutes.Solve(tl);
            var path = DialWardenRoutes.Plan(g, d.Player.transform.position);
            Check(what + ": a walking route exists", path != null && path.Count > 0);
            d.Player.Input = new PlanBot { Path = path, Origin = origin, Tick = () => d.World.Tick };
            yield break;
        }

        IEnumerator ConstellationChecks()
        {
            Section("Constellation");
            PlaceMe(new Vector3(0, 0, 4));
            int start = d.World.Tick, fires0 = boss.FireLog.Count, beat = boss.BeatTicks;
            yield return StartOf("Constellation");
            yield return Until(() => boss.Gold != null && boss.ConstellationCasts > 0 && boss.FireStart > start, 12, "lines drawn");
            Check("6 stars, at least 4 m apart, 3-10 m from the centre", boss.Gold.Count == 6 && Pairs(boss.Gold).All(x => x >= 4f) && boss.Gold.All(p => p.magnitude >= 3f && p.magnitude <= 10.01f));
            Check("the cast found a walking route", boss.LastRoute.Ok, $"{boss.ConstellationTries} rolls, {boss.LastRoute.StartCells} safe start cells, farthest walk {boss.LastRoute.FarthestWalk:0.0} of {boss.LastRoute.Lead:0.0} m");
            Overhead("constellation_stars_after_draw");
            var tl = new DialWardenRoutes.Timeline(); tl.Fires.AddRange(DialWardenRoutes.Fires(boss.Gold, null, beat));
            yield return FollowPlan(tl, boss.FireStart, "Constellation");
            yield return EndOfBlock("Constellation");
            var fl = boss.FireLog.Skip(fires0).ToList();
            Check("5 segments fire in drawn order, one per beat, starting 4 beats after the last line", fl.Count == 5 && fl[0].tick == boss.FireStart &&
                Enumerable.Range(1, fl.Count - 1).All(i => fl[i].tick - fl[i - 1].tick == beat) && Enumerable.Range(0, 5).All(i => fl[i].fire.A == boss.Gold[i]),
                string.Join(",", fl.Select(x => x.tick - boss.FireStart)));
            Check("a bot walking the planned route takes no beam or star hit", HitsSince(start, "Constellation", d.Player) == 0, $"{HitsSince(start, "Constellation", d.Player)} hits");
            // standing on the first star: hit once by its eruption (45)
            PlaceMe(new Vector3(0, 0, 4)); start = d.World.Tick;
            yield return StartOf("Constellation");
            yield return Until(() => boss.FireStart > start && boss.Gold != null, 12, "second cast drawn");
            var first = boss.Gold[0]; PlaceMe(first); goalFn = () => boss.Gold[0];
            yield return EndOfBlock("Constellation (standing on a star)");
            var dmg = DamageSince(start, "Constellation star", d.Player).ToList();
            Check("standing on the first star: its eruption hits for 45", dmg.Count >= 1 && dmg.All(x => x == DialWardenEncounter.BeamDmg), string.Join(",", dmg));
            goalFn = null; d.Player.Input = bot;
        }
        static IEnumerable<float> Pairs(List<Vector3> pts) { for (int i = 0; i < pts.Count; i++) for (int j = i + 1; j < pts.Count; j++) yield return (pts[i] - pts[j]).magnitude; }

        // ---------------- Tip the Scales ----------------
        IEnumerator ScalesChecks()
        {
            Section("Tip the Scales");
            PlaceMe(new Vector3(0, 0, 3)); int start = d.World.Tick, bal0 = boss.Balanced, w0 = boss.WeighLog.Count, t0 = boss.ThrowLog.Count;
            goalFn = () => boss.ScalesActive && boss.ThrowLog.Count > t0 ? boss.ThrowLog.Last().mirror : (Vector3?)null;
            yield return StartOf("Scales");
            yield return Until(() => boss.ThrowLog.Count > t0, 10, "first throw");
            Overhead("scales_target_and_weight");
            yield return EndOfBlock("Tip the Scales");
            goalFn = null;
            var tl = boss.ThrowLog.Skip(t0).ToList(); var wl = boss.WeighLog.Skip(w0).ToList();
            Check("3 throws, each 5-9 m out and at least 90 degrees from the last", tl.Count == 3 && tl.All(x => x.a.magnitude >= 4.99f && x.a.magnitude <= 9.01f) &&
                Enumerable.Range(1, tl.Count - 1).All(i => Vector3.Angle(tl[i].a, tl[i - 1].a) >= 89.9f));
            Check("each target is the weight's mirror; Medium zone 2.5 m", tl.All(x => (x.mirror + x.a).magnitude < 0.01f && Mathf.Approximately(x.zone, 2.5f)));
            Check("a player who walks to each target balances all 3 weighs", wl.Count == 3 && wl.All(x => x.balanced) && boss.Balanced - bal0 == 3, string.Join(",", wl.Select(x => $"{x.dist:0.0}m")));
            int beat = boss.BeatTicks;
            Check("each weight lands 3 beats after its marker, the weigh 4 beats after that", wl.Count == 3 && Enumerable.Range(0, 3).All(i => wl[i].tick - tl[i].tick == 3 * beat + 1 + 4 * beat),
                string.Join(",", Enumerable.Range(0, Mathf.Min(wl.Count, tl.Count)).Select(i => wl[i].tick - tl[i].tick)));
            Check("walking to the target never stands under a weight", HitsSince(start, "Counterweight", d.Player) == 0);
            // staying put: three off-balance hits, rising 35 / 50 / 65
            PlaceMe(new Vector3(0, 0, 2.5f)); start = d.World.Tick;
            yield return Block("Scales");
            var off = DamageSince(start, "Tip the Scales (off balance)", d.Player).ToList();
            Check("off balance: every player is hit for 35, then 50, then 65", off.SequenceEqual(new[] { 35, 50, 65 }), string.Join(",", off));
            // co-op: four players arranged round the target balance it together; all on the far side does not
            var ex = new[] { AddExtra(Vector3.zero), AddExtra(Vector3.zero), AddExtra(Vector3.zero) };
            int t1 = boss.ThrowLog.Count, w1 = boss.WeighLog.Count;
            yield return StartOf("Scales");
            float until = Time.time + 40;
            while (boss.Current != "between blocks" && Time.time < until)
            {
                if (boss.ScalesActive && boss.ThrowLog.Count > t1)
                {
                    var thr = boss.ThrowLog.Last(); var m = thr.mirror; var side = Vector3.Cross(Vector3.up, m.normalized);
                    bool lastThrow = boss.ThrowLog.Count - t1 == 3;
                    // throws 1-2: spread round the target (centroid on it); throw 3: everyone 3 m beyond it on one side
                    var offs = lastThrow ? new[] { m.normalized * 3 + side * 3, m.normalized * 3 + side * 3.5f, m.normalized * 3.5f + side * 3, m.normalized * 3.5f + side * 3.5f }
                                         : new[] { side * 1.5f, -side * 1.5f, m.normalized * 1.5f, -m.normalized * 1.5f };
                    Place(d.Player, m + offs[0]); goal = d.Player.transform.position;
                    for (int i = 0; i < 3; i++) Place(ex[i], m + offs[i + 1]);
                }
                Keep(); yield return null;
            }
            var cw = boss.WeighLog.Skip(w1).ToList();
            Check("co-op: 4 players spread round the target balance it; all off to one side do not", cw.Count == 3 && cw[0].balanced && cw[1].balanced && !cw[2].balanced && cw.All(x => x.standing == 4),
                string.Join(",", cw.Select(x => $"{x.standing}p {x.dist:0.0}m {(x.balanced ? "ok" : "off")}")));
            RemoveExtras();
        }

        // ---------------- Mainspring ----------------
        IEnumerator MainspringChecks()
        {
            Section("Mainspring");
            PlaceMe(new Vector3(0, 0, 5)); int start = d.World.Tick, w0 = boss.WaveLog.Count;
            yield return StartOf("Mainspring");
            yield return Until(() => boss.WaveLog.Count > w0, 8, "wave set up");
            var wl = boss.WaveLog.Skip(w0).ToList();
            var w = wl[0].wave;
            Check("Medium: 3 gaps of 30 degrees, return gaps turned 60", w.OutGaps.Length == 3 && Mathf.Approximately(w.GapHalf * 2, 30) &&
                Enumerable.Range(0, 3).All(i => Mathf.Abs(Mathf.Abs(Mathf.DeltaAngle(w.OutGaps[i], w.InGaps[i])) - 60) < 0.01f));
            Check("the cast found a walking route", boss.LastRoute.Ok, $"{boss.MainspringTries} rolls, farthest walk {boss.LastRoute.FarthestWalk:0.0} of {boss.LastRoute.Lead:0.0} m");
            var tl = new DialWardenRoutes.Timeline(); foreach (var x in wl) tl.Waves.Add(x.wave);
            yield return FollowPlan(tl, wl[0].origin, "Mainspring");
            yield return Until(() => boss.ActiveWaveCount > 0 && d.World.Tick > wl[0].origin + w.Start + 30, 8, "wave travelling");
            Overhead("mainspring_wave_out");
            yield return Until(() => d.World.Tick > wl[0].origin + w.Start + w.OutTicks + 20, 8, "wave rebounding");
            Overhead("mainspring_wave_back");
            yield return EndOfBlock("Mainspring");
            Check("a bot walking the planned route is never hit by the wave or the implosion", HitsSince(start, "Mainspring", d.Player) == 0, $"{HitsSince(start, "Mainspring", d.Player)} hits");
            Check("the wave wall was drawn", boss.Fx.WaveFrames > 0);
            // standing still 7 m out, half way between two outward gaps: hit by the wave on its way out (45)
            d.Player.Input = bot; start = d.World.Tick; int w1 = boss.WaveLog.Count;
            yield return StartOf("Mainspring");
            yield return Until(() => boss.WaveLog.Count > w1, 8, "second wave set up");
            var g = boss.WaveLog.Last().wave; float between = g.OutGaps[0] + 60f;
            PlaceMe(Sim.Dir(between) * 7f);
            yield return EndOfBlock("Mainspring (standing still)");
            var wd = DamageSince(start, "Mainspring", d.Player).ToList();
            Check("standing still off the gaps: the wave hits once each way at most, 45 each", wd.Count >= 1 && wd.Count <= 2 && wd.All(x => x == DialWardenEncounter.WaveDmg), string.Join(",", wd));
        }

        // ---------------- difficulty ----------------
        void DifficultyChecks()
        {
            Section("difficulty");
            var keep = d.World.Difficulty;
            d.World.Difficulty = Difficulty.Easy;
            Check("Easy: 2 directions + E, 12-tick window, 5-beat prompt, 3.5 m balance zone", boss.Keys == 2 && boss.Window == 12 && boss.LeadBeats == 5 && Mathf.Approximately(boss.Zone, 3.5f));
            d.World.Difficulty = Difficulty.Hard;
            Check("Hard: 4 directions + E, 5-tick window, 3-beat prompt, 2.0 m balance zone", boss.Keys == 4 && boss.Window == 5 && boss.LeadBeats == 3 && Mathf.Approximately(boss.Zone, 2.0f));
            d.World.Difficulty = keep;
            Check("co-op lit hours: one per two players, at most 4", boss.LitCountFor(1) == 1 && boss.LitCountFor(2) == 1 && boss.LitCountFor(3) == 2 && boss.LitCountFor(8) == 4);
        }

        // ---------------- below 50% ----------------
        IEnumerator PhaseTwoChecks()
        {
            Section("the 50% shift and phase 2");
            qte.Mode = QteMode.Correct;
            PlaceMe(new Vector3(0, 0, 3));
            boss.DebugSetHpFraction(0.505f);
            boss.TakeHit(200, boss.transform.position + Vector3.up);
            Check("HP stops at half when the 50% mark is reached", boss.Hp == (boss.MaxHp + 1) / 2);
            yield return Until(() => boss.HoldHp, 40, "shift begins");
            boss.TakeHit(400, boss.transform.position + Vector3.up);
            Check("HP holds at half through the shift", boss.Hp == (boss.MaxHp + 1) / 2);
            yield return Until(() => boss.Phase2, 10, "tempo commit");
            Check("phase 2 runs at 150 BPM", Mathf.Approximately(boss.Bpm, 150) && boss.BeatTicks == 24);
            yield return Until(() => boss.LapActive && boss.LitList.Count > 0, 10, "reversed lap set up");
            Check("the shift casts Arrest the Hand counter-clockwise", !boss.Clockwise);
            PlaceMe(boss.LitList[0].Pos);
            yield return Until(() => boss.PromptFor(d.Player, out _) != null, 8, "phase 2 prompt");
            var q = boss.PromptFor(d.Player, out _);
            Check("solo below 50%: one more key (4 directions + E)", q != null && q.Sequence.Length == 5);
            yield return Until(() => !boss.HoldHp, 20, "HP released after the reversed lap");
            Check("the reversed lap was caught", boss.ParryLog.Last().result == "Success");
            int hp = boss.Hp; boss.TakeHit(100, boss.transform.position + Vector3.up);
            Check("after the shift he can be damaged below half", boss.Hp == hp - 100);
            yield return Until(() => boss.Current == "between blocks", 10, "shift ends");

            // gold + silver with a Mainspring
            PlaceMe(new Vector3(0, 0, 5)); int start = d.World.Tick, f0 = boss.FireLog.Count, w0 = boss.WaveLog.Count, beat = boss.BeatTicks;
            yield return StartOf("ConstellationMainspring");
            yield return Until(() => boss.Silver != null && boss.FireStart > start && boss.WaveLog.Count > w0, 15, "gold and silver drawn");
            Check("below 50%: gold and silver paths of 5 stars", boss.Gold.Count == 5 && boss.Silver.Count == 5);
            var tl = new DialWardenRoutes.Timeline(); tl.Fires.AddRange(DialWardenRoutes.Fires(boss.Gold, boss.Silver, beat));
            foreach (var x in boss.WaveLog.Skip(w0)) tl.Waves.Add(x.wave);
            yield return FollowPlan(tl, boss.FireStart, "Constellation + Mainspring");
            Overhead("p2_gold_silver");
            yield return EndOfBlock("Constellation + Mainspring");
            var fl = boss.FireLog.Skip(f0).ToList();
            Check("8 segments on alternating beats: gold in drawn order, silver in reverse", fl.Count == 8 && Enumerable.Range(0, 8).All(i => fl[i].fire.Silver == (i % 2 == 1)) &&
                fl[1].fire.A == boss.Silver[4] && fl[0].fire.A == boss.Gold[0]);
            Check("a bot walking the planned route takes no hit from beams, stars, wave or implosion",
                HitsSince(start, "Constellation", d.Player) + HitsSince(start, "Mainspring", d.Player) == 0, $"{HitsSince(start, "Constellation", d.Player)} beam/star, {HitsSince(start, "Mainspring", d.Player)} wave");

            // double Mainspring
            d.Player.Input = bot; PlaceMe(new Vector3(0, 0, 5)); start = d.World.Tick; w0 = boss.WaveLog.Count;
            yield return StartOf("DoubleMainspring");
            yield return Until(() => boss.WaveLog.Count >= w0 + 2, 8, "two waves");
            var ws = boss.WaveLog.Skip(w0).ToList(); var t2 = new DialWardenRoutes.Timeline(); foreach (var x in ws) t2.Waves.Add(x.wave);
            Check("below 50%: two slams 2 beats apart", ws.Count == 2 && ws[1].wave.Start - ws[0].wave.Start == 2 * beat);
            yield return FollowPlan(t2, ws[0].origin, "double Mainspring");
            yield return EndOfBlock("double Mainspring");
            Check("a bot walking the planned route survives both waves untouched", HitsSince(start, "Mainspring", d.Player) == 0, $"{HitsSince(start, "Mainspring", d.Player)} hits");

            // two weights while a constellation fires
            d.Player.Input = bot; PlaceMe(new Vector3(0, 0, 3)); start = d.World.Tick; int t0 = boss.ThrowLog.Count; f0 = boss.FireLog.Count;
            goalFn = () => boss.ScalesActive && boss.ThrowLog.Count > t0 ? boss.ThrowLog.Last().mirror : (Vector3?)null;
            yield return Block("ScalesConstellation");
            goalFn = null;
            var tw = boss.ThrowLog.Skip(t0).ToList();
            Check("below 50%: two weights per throw, target the mirror of their midpoint, at least 4 m out", tw.Count == 3 && tw.All(x => x.b.HasValue && x.mirror.magnitude >= 3.99f));
            Check("a constellation fires while the weights land (5 segments)", boss.FireLog.Count - f0 == 5);
        }

        // ---------------- Midnight and defeat ----------------
        IEnumerator MidnightAndDefeat()
        {
            Section("Midnight (soft enrage) and defeat");
            Check("Midnight is set for 5:00 of combat", DialWardenEncounter.EnrageTicks == 300 * Sim.Hz);
            d.Player.Input = bot; PlaceMe(new Vector3(0, 0, 6)); int start = d.World.Tick;
            boss.ForceEnrage = true;
            yield return Until(() => boss.Midnight, 40, "Midnight");
            yield return Until(() => HitsSince(start, "Midnight", d.Player) >= 2, 15, "Midnight waves");
            Check("Midnight: gapless waves keep coming and hit wherever you stand", HitsSince(start, "Midnight", d.Player) >= 2);
            Overhead("midnight");
            yield return Until(() => boss.ActiveWaveCount > 0, 5, "a wave in flight");
            boss.DebugDefeat();
            yield return null;
            Check("defeat: waves, fires and the wave wall are cleared at once", boss.ActiveWaveCount == 0 && d.World.Hazards.Count == 0);
            Check("defeat: the player can move", d.Player.MovementEnabled);
            yield return Until(() => d.State == RunState.Results, 10, "results");
        }

        // ---------------- two misses kill ----------------
        IEnumerator TwoMissesKill()
        {
            Section("two misses kill (co-op roster, nobody on the hours)");
            Check("retry accepted", d.RequestRetry());
            yield return Until(() => d.State == RunState.Ready, 5, "ready again");
            yield return BeginFight(77);
            boss.ForcedNext = "Arrest";   // before his first block starts
            yield return Until(() => Sim.Flat(boss.transform.position).magnitude < 0.35f, 8, "centre");
            for (int i = 0; i < 3; i++) AddExtra(Vector3.zero);
            keepAlive = false;
            var spots = new[] { 0f, 90f, 180f, 270f };
            int start = d.World.Tick;
            float until = Time.time + 40;
            while (Time.time < until && d.World.Players.Any(p => !p.Down))
            {
                int k = 0; foreach (var p in d.World.Players) if (!p.Down && Sim.Flat(p.transform.position).magnitude > 3.2f) Place(p, Sim.Dir(spots[k++ % 4]) * 2.8f);
                goal = null;
                yield return null;
            }
            Check("4 players: two lit hours", boss.LitList.Count == 2, $"{boss.LitList.Count}");
            var brk = hits.Where(h => h.tick >= start && h.source == "Arrest the Hand (broken through)").Select(h => h.dmg).ToList();
            Check("two misses: the broken-through lap hits everyone for 110", brk.Count == 4 && brk.All(x => x == 2 * DialWardenEncounter.BreakDmg), string.Join(",", brk));
            Check("two misses kill everyone at full health", d.World.Players.All(p => p.Down));
            yield return Until(() => d.State == RunState.Downed || d.State == RunState.Results, 5, "defeat");
        }

        // ---------------- helpers ----------------
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
            lines.Insert(0, $"Dial Warden autotest: {pass} passed, {fail} failed  ({(Application.isEditor ? "Editor Play Mode" : "Windows player")}, Unity {Application.unityVersion})");
            File.WriteAllLines(Path.Combine(dir, "dial-warden-report.txt"), lines);
            Debug.Log($"[dial-warden-test] done: {pass} passed, {fail} failed");
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
