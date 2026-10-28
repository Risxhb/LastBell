using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LastBell.Arenas.MetronomeCourt;
using UnityEngine;

namespace LastBell.Run
{
    // Acceptance run for the Chimebound Oracle inside the real game:  -chimeboundOracleTest -lastbellOut <folder>
    // (Windows player or editor Play Mode). It uses the director's public requests, bots that send ordinary PlayerInput,
    // extra simulated roster players for the mark count (not co-op), the labelled dev hooks (ForcedNext, ForceEnrage,
    // boss HP fraction / defeat, player DebugSetHp, direct player placement for orb and well hit checks). It measures
    // what the hazards actually do, runs the lane fairness checks, captures screenshots, writes
    // chimebound-oracle-report.txt and exits 0 / 1. It fights on his default arena, the Skyward Spire; add
    // -chimeboundOracleCourt to fight on the Metronome Court instead and check that the court's metronome follows him.
    public sealed class ChimeboundOracleAutoTest : MonoBehaviour
    {
        public static bool Requested => Environment.GetCommandLineArgs().Any(a => a.Equals("-chimeboundOracleTest", StringComparison.OrdinalIgnoreCase));
        static bool Flicker => Environment.GetCommandLineArgs().Any(a => a.Equals("-chimeboundOracleFlicker", StringComparison.OrdinalIgnoreCase));
        static bool Court => Environment.GetCommandLineArgs().Any(a => a.Equals("-chimeboundOracleCourt", StringComparison.OrdinalIgnoreCase));
        static string OutDir
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.FindIndex(args, a => a.Equals("-lastbellOut", StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Application.persistentDataPath, "chimebound-oracle-test");
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Requested || FindAnyObjectByType<ChimeboundOracleAutoTest>()) return;
            var go = new GameObject("Chimebound Oracle autotest"); DontDestroyOnLoad(go); go.AddComponent<ChimeboundOracleAutoTest>();
        }

        static Vector2 ToMove(Vector3 world)
        {
            var cam = Camera.main; var fwd = cam ? Sim.Flat(cam.transform.forward).normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, fwd);
            return Vector2.ClampMagnitude(new Vector2(Vector3.Dot(world, right), Vector3.Dot(world, fwd)), 1);
        }

        sealed class Bot : IInputSource
        {
            public Vector3 MoveWorld; public bool Sprint;
            public PlayerInput Read(PlayerController who) => new PlayerInput { Aim = who.transform.position + who.transform.forward, Sprint = Sprint, Move = ToMove(MoveWorld) };
        }

        // Walks (never sprints or dashes) to the lane whose next strike is furthest away, only along paths where it is
        // never inside a lane while that lane strikes. It reads the same floor warnings a player sees.
        sealed class LaneDodger : IInputSource
        {
            public Func<CombatWorld> World; public float HoldZ = 2.5f;
            public int Target = -1;
            public PlayerInput Read(PlayerController who)
            {
                var r = new PlayerInput { Aim = who.transform.position + who.transform.forward };
                var w = World(); if (w == null) return r;
                var windows = new List<(int from, int to)>[OracleLanes.Count];
                for (int i = 0; i < windows.Length; i++) windows[i] = new List<(int, int)>();
                foreach (var h in w.Hazards.Live)
                {
                    if (h.Shape != HazardShape.Lane || !h.Source.StartsWith("Toll Lane")) continue;
                    int lane = OracleLanes.LaneOf(h.Origin.x), rem = h.ResolveAt - h.Age;
                    windows[lane].Add((rem - 1, rem + OracleLanes.CascadeTicks + 1));
                }
                float x = who.transform.position.x, v = PlayerController.WalkSpeed * Sim.Dt * 0.95f;
                int best = -1; float bestScore = float.MinValue;
                for (int L = 0; L < OracleLanes.Count; L++)
                {
                    float tx = OracleLanes.CentreX(L);
                    if (!PathClear(x, tx, v, windows)) continue;
                    int arrive = Mathf.CeilToInt(Mathf.Abs(tx - x) / v);
                    int nextAfter = int.MaxValue;
                    foreach (var wnd in windows[L]) if (wnd.to >= arrive) nextAfter = Mathf.Min(nextAfter, wnd.from);
                    float score = (nextAfter == int.MaxValue ? 100000 : nextAfter) - arrive * 0.6f + (L == Target ? 6 : 0);
                    if (score > bestScore) { bestScore = score; best = L; }
                }
                if (best >= 0) Target = best;
                if (Target < 0) return r;
                var goal = new Vector3(OracleLanes.CentreX(Target), 0, HoldZ);
                var to = Sim.Flat(goal - who.transform.position);
                if (to.magnitude > 0.12f) r.Move = ToMove(to.normalized);
                return r;
            }
            static bool PathClear(float x, float tx, float v, List<(int from, int to)>[] windows)
            {
                float dir = Mathf.Sign(tx - x); int steps = Mathf.CeilToInt(Mathf.Abs(tx - x) / v);
                for (int t = 0; t <= steps + 1; t++)
                {
                    float px = Mathf.Abs(tx - x) < 1e-3f ? x : x + dir * Mathf.Min(Mathf.Abs(tx - x), t * v);
                    for (int L = 0; L < OracleLanes.Count; L++)
                    {
                        float lo = OracleLanes.CentreX(L) - OracleLanes.Width / 2 - Hazard.Margin - 0.05f, hi = OracleLanes.CentreX(L) + OracleLanes.Width / 2 + Hazard.Margin + 0.05f;
                        if (px < lo || px > hi) continue;
                        foreach (var wnd in windows[L]) if (t >= wnd.from && t <= wnd.to) return false;
                    }
                }
                // and the destination stays clear for a while after arriving
                return true;
            }
        }

        // walks to the lit soak circle
        sealed class SoakBot : IInputSource
        {
            public Func<ChimeboundOracleEncounter> Boss;
            public PlayerInput Read(PlayerController who)
            {
                var r = new PlayerInput { Aim = who.transform.position + who.transform.forward };
                var b = Boss(); if (!b || !b.ChainActive || b.ChainIndex < 0 || b.ChainIndex >= b.ChainPositions.Count) return r;
                var to = Sim.Flat(b.ChainPositions[b.ChainIndex] - who.transform.position);
                if (to.magnitude > 0.25f) r.Move = ToMove(to.normalized);
                return r;
            }
        }

        RunDirector d; readonly Bot bot = new Bot();
        readonly List<string> lines = new List<string>(); int pass, fail, errors, shots;
        readonly List<(int tick, string source, PlayerController who, int dmg)> hits = new List<(int, string, PlayerController, int)>();
        readonly Dictionary<int, (string src, int spawn, int resolve)> seen = new Dictionary<int, (string, int, int)>();
        readonly List<PlayerController> extras = new List<PlayerController>();
        string dir; bool keepAlive = true;
        ChimeboundOracleEncounter boss;
        int maxLaneHazards, maxWarningLanes;

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
            lines.Add(line); Debug.Log("[chimebound-oracle-test] " + line);
        }
        void Note(string s) { lines.Add("      " + s); Debug.Log("[chimebound-oracle-test] " + s); }
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
        void Overhead(string name) => RenderShot(name, new Vector3(0, 30, 14), new Vector3(0, 0, -1), 46);

        IEnumerator Wait(float s) { float t = 0; while (t < s) { t += Time.deltaTime; Observe(); Keep(); yield return null; } }
        IEnumerator Until(Func<bool> cond, float timeout, string what)
        {
            float t = 0; while (!cond() && t < timeout) { t += Time.deltaTime; Observe(); Keep(); yield return null; }
            if (!cond()) Check("reached: " + what, false, $"timed out after {timeout}s");
        }

        Vector3? goal;
        void Keep()
        {
            if (d == null || d.World == null) return;
            foreach (var p in d.World.Players) if (keepAlive && !p.Down && p.Hp < 61) p.DebugSetHp(100);
            var me = d.Player;
            if (me && goal.HasValue && me.Input == bot)
            {
                var to = Sim.Flat(goal.Value - me.transform.position);
                bot.MoveWorld = to.magnitude > 0.2f ? to.normalized : Vector3.zero;
            }
            else bot.MoveWorld = Vector3.zero;
        }
        void Observe()
        {
            var w = d?.World; if (w == null) return;
            int lanes = 0;
            foreach (var h in w.Hazards.Live)
            {
                if (!seen.ContainsKey(h.Id)) seen[h.Id] = (h.Source, w.Tick - h.Age, w.Tick - h.Age + h.ResolveAt - 1);
                if (h.Source.StartsWith("Toll Lane")) lanes++;
            }
            maxLaneHazards = Mathf.Max(maxLaneHazards, lanes);
            // lanes still warning (not yet cascading): a lane whose strike is rolling down is not "lit"
            var warning = new HashSet<int>();
            foreach (var h in w.Hazards.Live) if (h.Source.StartsWith("Toll Lane") && h.ResolveAt - h.Age > OracleLanes.CascadeTicks) warning.Add(OracleLanes.LaneOf(h.Origin.x));
            maxWarningLanes = Mathf.Max(maxWarningLanes, warning.Count);
        }
        void OnHit(PlayerController p, Hazard h, int dmg) => hits.Add((d.World.Tick, h.Source, p, dmg));
        void Place(Vector3 at) { d.Player.transform.position = d.World.Constrain(new Vector3(at.x, 0, at.z), PlayerController.Radius); goal = d.Player.transform.position; }
        int HitsSince(int tick, string prefix, PlayerController who = null) => hits.Count(h => h.tick >= tick && h.source.StartsWith(prefix) && (who == null || h.who == who));

        // force one block and wait for it to start and end
        IEnumerator Block(string atk, float timeout = 40f)
        {
            int before = boss.BlockLog.Count;
            boss.ForcedNext = atk;
            yield return Until(() => boss.BlockLog.Skip(before).Any(s => s.EndsWith(" " + atk)), timeout, atk + " starts");
            yield return Until(() => boss.Current == "between blocks" || boss.Dead, timeout, atk + " ends");
        }

        IEnumerator Main()
        {
            Section("registration and fairness (pure rules)");
            yield return new WaitForSeconds(1f);
            int map = RunMaps.IndexOf(Court ? RunDirector.TempoTyrantMap : RunDirector.AurelionMap), enc = RunDirector.ChimeboundOraclePractice;
            Note(Court ? "arena: Metronome Court (-chimeboundOracleCourt)" : "arena: Skyward Spire (his default)");
            Check("practice entry registered on the Skyward Spire, practice only", enc >= 0 && RunDirector.Bosses[enc].Map == "skyward-spire" && !RunDirector.Bosses[enc].InRun);
            Check("run order unchanged (4 run bosses, Oracle excluded)", RunDirector.RunOrder.Length == 4 && !RunDirector.RunOrder.Contains(enc));
            Check("Tempo Tyrant practice entry still resolves", RunDirector.TempoTyrantPractice >= 0 && RunDirector.Bosses[RunDirector.TempoTyrantPractice].Name.StartsWith("Tempo Tyrant"));
            FairnessChecks();

            // ---------------- practice start ----------------
            float boot = 0; while (RunDirector.Instance == null && boot < 4) { boot += Time.deltaTime; yield return null; }
            if (RunDirector.Instance == null) UnityEngine.SceneManagement.SceneManager.LoadScene(RunMaps.All[map].RunScene);
            while (RunDirector.Instance == null || RunDirector.Instance.State != RunState.Menu) yield return null;
            d = RunDirector.Instance;
            if (Court && RunMaps.Current != map)
            {
                // Boss Rush with the Metronome Court picked: load the court, then start on the selected map
                d.SelectMap(map);
                float tm = 0; while ((RunDirector.Instance == null || RunMaps.Current != map || RunDirector.Instance.State != RunState.Menu) && tm < 30) { tm += Time.deltaTime; yield return null; }
                d = RunDirector.Instance;
            }
            Check("practice request accepted", Court ? d.StartPracticeOnSelectedMap("Vanguard", enc) : d.StartPractice("Vanguard", enc));
            float t0 = 0; while ((RunDirector.Instance == null || RunDirector.Instance.State != RunState.Ready) && t0 < 30) { t0 += Time.deltaTime; yield return null; }
            d = RunDirector.Instance;
            Check($"practice Ready on {RunMaps.All[map].Name}", d && d.State == RunState.Ready && RunMaps.Current == map && d.Mode == RunMode.Practice && d.Encounter == enc);
            var arena = FindAnyObjectByType<MetronomeCourtArena>();
            float minA = 999, maxA = -999;
            for (float t = 0; t < 1.2f; t += Time.deltaTime) { if (arena) { minA = Mathf.Min(minA, arena.RodAngle); maxA = Mathf.Max(maxA, arena.RodAngle); } yield return null; }
            if (Court)
                Check("readiness previews the metronome at the Oracle's Normal tempo", arena && arena.Preview && maxA - minA > 5f && arena.WeightHeight > MetronomeCourtArena.WeightHaste + 1f,
                    arena ? $"swing {maxA - minA:0.0} deg, weight {arena.WeightHeight:0.00}" : "no arena");
            else Check("the Skyward Spire has no court metronome: the fight keeps his own clock", arena == null);
            d.Player.Input = bot;
            d.World.PlayerHit += OnHit;
            OracleLanes.Bound = CombatWorld.PlayerBound;

            Check("Ready accepted", d.RequestReady());
            yield return Until(() => d.Boss is ChimeboundOracleEncounter, 3, "boss spawned");
            boss = d.Boss as ChimeboundOracleEncounter;
            boss.Seed = 20261025;
            d.World.Difficulty = Difficulty.Medium;   // a saved menu choice must not change the measured numbers
            boss.ForcedNext = "Rounds";
            ChimeboundOracleHud.ShowDiagnostics = false;
            yield return Until(() => d.State == RunState.Combat, 6, "combat");
            Section("combat start");
            Check("fight opens in Normal at 140 BPM, beat 0", boss.Clock.State == Tempo.Normal && Mathf.Approximately(boss.Clock.Bpm, 140) && boss.Clock.Beat < 1.5 && boss.Clock.Commits == 0);
            Check("Oracle uses its rigged model, not the stand-in", boss.Rig && boss.Rig.HasModel && !boss.Placeholder && !boss.Rig.name.Contains("STAND-IN"),
                boss.Rig && boss.Rig.Presenter ? $"clip {boss.Rig.Presenter.CurrentClip}" : "no presenter");
            Check("no ability text: empty phase line and intro lesson, diagnostics off by default", boss.PhaseName == "" && boss.Lesson == "" && !ChimeboundOracleHud.ShowDiagnostics);
            yield return Wait(0.5f);
            Check($"the lanes span this arena: 8 of {OracleLanes.Width:0.000} m across {2 * OracleLanes.Bound:0.#} m", Mathf.Approximately(OracleLanes.Bound, RunMaps.All[map].Bound),
                $"bound {OracleLanes.Bound:0.00}, map {RunMaps.All[map].Bound:0.00}");
            arena = FindAnyObjectByType<MetronomeCourtArena>();
            if (Court)
                Check("the court's metronome follows the Oracle's clock", arena && !arena.Preview && Mathf.Abs(arena.RodAngle - boss.Clock.RodPhase * MetronomeCourtArena.SwingDegrees) < 10f,
                    arena ? $"rod {arena.RodAngle:0.0}, clock {boss.Clock.RodPhase * MetronomeCourtArena.SwingDegrees:0.0}" : "");
            if (Court)
            {
                // only the Tempo Tyrant pulses the court's lights (Risxhb, October 28, 2026): steady through a few beats here
                float pulseMax = 0; bool pulsing = false;
                for (float t = 0; t < 1.2f; t += Time.deltaTime) { if (arena) { pulseMax = Mathf.Max(pulseMax, arena.BeatPulse); pulsing |= arena.LightsPulse; } yield return null; }
                Check("the court's lights stay steady (no beat pulse) for the Oracle", arena && !pulsing && pulseMax == 0, $"pulse max {pulseMax:0.00}");
            }
            yield return Shot("start_normal");
            if (Flicker) { yield return FlickerFrames(); Finish(); yield break; }

            yield return Rounds();
            yield return OddsEvens();
            yield return WellsHigh();
            yield return CarillonHigh();
            yield return MarkCheck();
            yield return ClapperCheck();
            yield return GrandToll();
            yield return CalledChanges();
            yield return WellsLow();
            yield return CarillonLow();
            yield return DifficultyChecks();
            yield return LastBellCheck();

            Section("defeat");
            boss.DebugDefeat();
            yield return Wait(0.3f);
            Check("defeat clears hazards, orbs and marks in the killing tick", boss.Dead && d.World.Hazards.Count == 0 && boss.OrbCount == 0 && boss.ActiveMarks == 0);
            yield return Until(() => d.State == RunState.Cleared || d.State == RunState.Results, 8, "practice cleared");
            Check("zero runtime errors logged", errors == 0, errors + " errors");
            Finish();
        }

        // ---------------- floor flicker capture (diagnostic) ----------------
        // The player walks slowly so the follow camera pans, and consecutive frames are saved as they are shown on
        // screen (with the game's own post and anti-aliasing). tools/chimebound-oracle/flicker_map.py turns each run
        // into a map of pixels that change direction frame to frame (flicker), against smooth motion.
        // Time is frozen (timeScale 0: no beat pulse, motes or follow smoothing), then only the camera moves, a few
        // centimetres per frame. Anything that changes direction frame to frame is aliasing or z-fighting, not motion.
        IEnumerator FrameRun(string name, Vector3 camFrom, Vector3 camTo, Vector3 look, int frames, bool live = false)
        {
            var sub = Path.Combine(dir, "flicker-" + name); Directory.CreateDirectory(sub);
            var cam = Camera.main; if (!cam) yield break;
            cam.transform.GetPositionAndRotation(out var keepPos, out var keepRot);
            float keepScale = Time.timeScale; Time.timeScale = live ? 1 : 0;   // live: time runs (lights swing), camera still
            for (int f = 0; f < frames; f++)
            {
                var p = Vector3.Lerp(camFrom, camTo, f / (float)(frames - 1));
                cam.transform.SetPositionAndRotation(p, Quaternion.LookRotation(look + (p - camFrom) - p));   // pan: the view direction stays fixed
                yield return new WaitForEndOfFrame();
                var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(sub, $"{f:000}.png"), tex.EncodeToPNG()); Destroy(tex);
                yield return null;
            }
            Time.timeScale = keepScale;
            cam.transform.SetPositionAndRotation(keepPos, keepRot);
        }
        IEnumerator FlickerFrames()
        {
            Section("floor flicker capture");
            Note($"frames at {Screen.width}x{Screen.height}, quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}, msaa {QualitySettings.antiAliasing}");
            Place(new Vector3(0, 0, 4)); goal = null;
            yield return Wait(1f);
            var game = Camera.main.transform.position;
            yield return FrameRun("floor", game, game + new Vector3(1.2f, 0, 0), new Vector3(0, 1, 4), 40);
            yield return FrameRun("far", new Vector3(0, 40.6f, 21.6f), new Vector3(1.5f, 40.6f, 21.6f), new Vector3(0, 1, -8), 40);
            // Diagnosis (October 26, 2026): the same floor pan with 4x MSAA, then with the sun's shadows off, to see which
            // one removes the remaining shimmer. Settings are restored afterwards.
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            int msaa = urp ? urp.msaaSampleCount : 1;
            if (urp) { urp.msaaSampleCount = 4; yield return Wait(0.3f); yield return FrameRun("floor-msaa4", game, game + new Vector3(1.2f, 0, 0), new Vector3(0, 1, 4), 40); urp.msaaSampleCount = msaa; }
            var sun = FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.type == LightType.Directional && l.shadows != LightShadows.None).ToArray();
            var kinds = sun.Select(l => l.shadows).ToArray();
            foreach (var l in sun) l.shadows = LightShadows.None;
            yield return Wait(0.3f);
            yield return FrameRun("floor-noshadow", game, game + new Vector3(1.2f, 0, 0), new Vector3(0, 1, 4), 40);
            for (int i = 0; i < sun.Length; i++) sun[i].shadows = kinds[i];
            Note($"diagnosis: msaa was {msaa}, {sun.Length} shadowed directional light(s)");
            // Walk smoothness (October 26, 2026): the player walks straight across while the game's own follow camera
            // tracks it; per-frame camera travel divided by frame time should be steady. Stutter shows as frames that
            // move about double and frames that barely move. Compare with -lastbellNoSmoothing.
            Time.timeScale = 1;
            Place(new Vector3(-9, 0, 6)); goal = new Vector3(9, 0, 6);
            yield return Wait(0.8f);
            var cam2 = Camera.main; var camSpeeds = new List<float>(); var heroSpeeds = new List<float>(); var dts = new List<float>();
            var lastCam = cam2.transform.position; var lastHero = d.Player.transform.position;
            for (int f = 0; f < 150; f++)
            {
                yield return null;
                float dt = Time.unscaledDeltaTime; if (dt <= 0) continue;
                camSpeeds.Add((cam2.transform.position - lastCam).magnitude / dt); heroSpeeds.Add((d.Player.transform.position - lastHero).magnitude / dt); dts.Add(dt);
                lastCam = cam2.transform.position; lastHero = d.Player.transform.position;
            }
            goal = null;
            string Stat(List<float> v)
            {
                var s = v.Skip(20).ToList(); float mean = s.Average(), sd = Mathf.Sqrt(s.Average(x => (x - mean) * (x - mean)));
                return $"{mean:0.00} m/s, variation {100 * sd / Mathf.Max(0.001f, mean):0}%, stalled frames {s.Count(x => x < mean * 0.35f)}/{s.Count}";
            }
            Note($"walk smoothness: smoothing {(SimSmoother.Enabled ? "on" : "off")}, {1f / dts.Skip(20).Average():0} fps; hero {Stat(heroSpeeds)}; camera {Stat(camSpeeds)}");
            Time.timeScale = 1;
            boss.ForcedNext = "Called";
            yield return Until(() => d.World.Hazards.Live.Count(h => h.Source == "Toll Lane") >= 4, 30, "lanes up for capture");
            yield return Wait(0.3f);
            game = Camera.main.transform.position;
            yield return FrameRun("lanes", game, game + new Vector3(0.8f, 0, 0), new Vector3(0, 1, 4), 30);
            Check("flicker frames written", Directory.GetFiles(Path.Combine(dir, "flicker-floor")).Length == 40);
        }

        // ---------------- pure rules ----------------
        void FairnessChecks()
        {
            // every check at both arenas he can be fought on: the Spire (default) and the Metronome Court
            float spire = RunMaps.All[RunMaps.IndexOf(RunDirector.AurelionMap)].Bound, court = RunMaps.All[RunMaps.IndexOf(RunDirector.TempoTyrantMap)].Bound;
            foreach (var (at, bound, width) in new[] { ("Skyward Spire", spire, 3.475f), ("Metronome Court", court, 3.125f) })
            {
                OracleLanes.Bound = bound;
                Check($"{at}: 8 lanes of {width:0.000} m across the {2 * bound:0.#} m floor", Mathf.Approximately(OracleLanes.Width, width), $"{OracleLanes.Width:0.000} m");
                Check($"{at}: Rounds and Odds & Evens survivable on foot (Normal, Medium and Easy warnings)",
                    OracleLanes.Survivable(OracleLanes.Rounds(), 140, 4, 0) && OracleLanes.Survivable(OracleLanes.OddsEvens(), 140, 4, 0) &&
                    OracleLanes.Survivable(OracleLanes.Rounds(), 140, 6, 0) && OracleLanes.Survivable(OracleLanes.OddsEvens(), 140, 6, 0));
                Check($"{at}: fallback Called Changes order passes every rule", OracleLanes.OrderRulesHold(OracleLanes.SafeFallback()) && OracleLanes.Survivable(OracleLanes.SafeFallback(), 185, 4, 2));
                foreach (var (name, warn, echo) in new[] { ("Medium", 4f, 0f), ("Easy", 6f, 0f), ("Hard (echo)", 4f, 2f) })
                {
                    var r = new System.Random(4242); int ok = 0, fallback = 0, totalTries = 0, maxTries = 0; const int N = 1000;
                    for (int i = 0; i < N; i++)
                    {
                        var s = OracleLanes.CalledChanges(r, 185, warn, echo, out int tries);
                        if (tries < 0) fallback++; else { totalTries += tries; maxTries = Mathf.Max(maxTries, tries); }
                        if (OracleLanes.OrderRulesHold(s) && OracleLanes.Survivable(s, 185, warn, echo)) ok++;
                    }
                    Check($"{at}: Called Changes {name}: {N} shuffled casts all follow the order rules and are survivable walking at 80% speed", ok == N && fallback == 0,
                        $"{ok}/{N} ok, fallback {fallback}, avg tries {(totalTries / (float)Mathf.Max(1, N - fallback)):0.0}, max {maxTries}");
                }
            }
            OracleLanes.Bound = spire;
            // the strict walking check rejects a pattern that is not survivable (the check can fail)
            var bad = new List<OracleLanes.Slot> { new OracleLanes.Slot(0, 0, 2), new OracleLanes.Slot(0, 4, 6), new OracleLanes.Slot(0, 1, 3), new OracleLanes.Slot(0, 5, 7) };
            Check("fairness check rejects an unsurvivable pattern (all 8 lanes on one beat)", !OracleLanes.Survivable(bad, 185, 4, 0));
        }

        // ---------------- phase 1 ----------------
        IEnumerator Rounds()
        {
            Section("Toll Lanes: Rounds");
            Place(new Vector3(OracleLanes.CentreX(3), 0, 4));
            int startLog = boss.LaneLog.Count, h0 = d.World.Tick;
            yield return Until(() => boss.LaneLog.Skip(startLog).Count(x => x.pattern == "Rounds") >= 3, 20, "Rounds strikes");
            yield return Shot("rounds_cascade");
            Overhead("rounds_overhead");
            yield return Until(() => boss.LaneLog.Skip(startLog).Count(x => x.pattern == "Rounds") >= 8, 20, "Rounds done");
            var log = boss.LaneLog.Skip(startLog).Where(x => x.pattern == "Rounds").Take(8).ToList();
            Check("Rounds strikes lanes 1..8 in order", log.Select(x => x.lane).SequenceEqual(Enumerable.Range(0, 8)), string.Join(",", log.Select(x => x.lane + 1)));
            int gap = TempoClock.TicksFor(2, 140);
            bool spacing = log.Count == 8 && Enumerable.Range(1, 7).All(i => Mathf.Abs(log[i].tick - log[i - 1].tick - gap) <= 2);
            Check("one lane every 2 beats at 140 BPM", spacing, string.Join(",", Enumerable.Range(1, Mathf.Max(0, log.Count - 1)).Select(i => log[i].tick - log[i - 1].tick)) + $" (want {gap})");
            Check("every strike lands on a metronome beat", boss.OffBeatStrikes == 0, boss.OffBeatStrikes + " off-beat");
            yield return Wait(0.4f);
            int laneHits = HitsSince(h0, "Toll Lane", d.Player);
            Check("standing in lane 4: hit exactly once, 45 damage, by the cascade", laneHits == 1 && hits.Last(h => h.source.StartsWith("Toll Lane")).dmg == 45, $"{laneHits} hits");
            var chimes = boss.ChimeLog; int beat = TempoClock.TicksFor(1, 140);
            Check("each lane rings its own pitch one beat before it strikes", log.All(s => chimes.Any(c => c.lane == s.lane && Mathf.Abs(s.tick - c.tick - beat) <= 3)));
            Check("Rounds lights at most 2 lanes at once", maxWarningLanes <= 2, $"max {maxWarningLanes} warning lanes");
            yield return Until(() => boss.Current == "between blocks", 10, "Rounds block ends");
            // dodge run: the walking dodger takes no lane hit in a full Rounds cast
            var dodger = new LaneDodger { World = () => d.World };
            d.Player.Input = dodger; int h1 = d.World.Tick;
            yield return Block("Rounds");
            Check("Rounds: a walking dodger reading only the floor takes no lane hit", HitsSince(h1, "Toll Lane", d.Player) == 0, HitsSince(h1, "Toll Lane", d.Player) + " hits");
            d.Player.Input = bot;
        }

        IEnumerator OddsEvens()
        {
            Section("Toll Lanes: Odds & Evens");
            maxLaneHazards = 0;
            var dodger = new LaneDodger { World = () => d.World };
            d.Player.Input = dodger; int h0 = d.World.Tick, startLog = boss.LaneLog.Count;
            boss.ForcedNext = "OddsEvens";
            yield return Until(() => boss.LaneLog.Skip(startLog).Any(x => x.pattern == "Odds & Evens"), 20, "Odds & Evens first strike");
            yield return Shot("odds_evens");
            yield return Until(() => boss.Current == "between blocks", 15, "Odds & Evens ends");
            var log = boss.LaneLog.Skip(startLog).Where(x => x.pattern == "Odds & Evens").ToList();
            var first = log.Where(x => x.tick - log[0].tick <= 2).Select(x => x.lane).OrderBy(x => x).ToList();
            var second = log.Where(x => x.tick - log[0].tick > 2).Select(x => x.lane).OrderBy(x => x).ToList();
            Check("odd lanes (2,4,6,8) together, then the others 4 beats later", first.SequenceEqual(new[] { 1, 3, 5, 7 }) && second.SequenceEqual(new[] { 0, 2, 4, 6 }) &&
                Mathf.Abs(log.First(x => x.lane == 0).tick - log[0].tick - TempoClock.TicksFor(4, 140)) <= 2);
            Check("Odds & Evens: the walking dodger takes no lane hit", HitsSince(h0, "Toll Lane", d.Player) == 0, HitsSince(h0, "Toll Lane", d.Player) + " hits");
            d.Player.Input = bot;
        }

        IEnumerator WellsHigh()
        {
            Section("Chime Wells (above 50%)");
            int rings0 = boss.RingLog.Count, erupt0 = boss.WellEruptions, h0 = d.World.Tick, conc0 = boss.ConcurrentWithWellsP1, g0 = boss.GroupLog.Count, arrows0 = boss.ArrowLog.Count;
            Place(new Vector3(0, 0, 7)); goal = null;
            boss.ForcedNext = "Wells";
            // group 1: stand in the middle of one well as the group erupts -> its eruption hits, the orbs leave from the rim
            yield return Until(() => d.World.Hazards.Live.Count(h => h.Source == "Chime Well") >= 3, 20, "first group drops");
            Check("wells drop as a group of 3, all at once", d.World.Hazards.Live.Count(h => h.Source == "Chime Well") == 3 && boss.GroupLog.Count == g0 + 1 && boss.GroupLog.Last().wells == 3);
            yield return Wait(0.2f);
            yield return Shot("well_group_arrows");
            Overhead("well_group_arrows_overhead");
            var arrows = boss.ArrowLog.Skip(arrows0).ToList();
            Check("each well shows one outward arrow per orb, matching where its orbs fly", arrows.Count == 3 && arrows.All(a => a.count == 6));
            var w1 = d.World.Hazards.Live.First(h => h.Source == "Chime Well");
            Place(w1.Origin);
            yield return Until(() => boss.WellEruptions >= erupt0 + 3, 5, "first group erupts");
            Check("the group detonates together", boss.RingLog.Skip(rings0).Take(3).Select(r => r.tick).Distinct().Count() == 1);
            var firstRings = boss.RingLog.Skip(rings0).Take(3).ToList();
            Check("orbs fly the way the arrows pointed", firstRings.All(r => arrows.Any(a => (a.at - r.at).magnitude < 0.01f && Mathf.Abs(Mathf.DeltaAngle(a.angle0, r.angle0)) < 0.01f)));
            yield return Shot("well_eruption_orbs");
            yield return Wait(0.6f);
            Check("standing in a well: its eruption hits once for 25, its own orbs leave from the rim and miss", HitsSince(h0, "Chime Well", d.Player) - HitsSince(h0, "Chime Well orb", d.Player) == 1 &&
                HitsSince(h0, "Chime Well orb", d.Player) == 0 && hits.Last(h => h.source == "Chime Well").dmg == 25);
            // group 2: stand on one orb's path -> exactly one orb hit for 15
            Place(new Vector3(0, 0, 9));
            yield return Until(() => boss.GroupLog.Count >= g0 + 2, 10, "second group drops");
            int h2 = d.World.Tick;
            yield return Until(() => boss.WellEruptions >= erupt0 + 6, 6, "second group erupts");
            var ring = boss.RingLog.Last();
            var onPath = ring.at + Sim.Dir(ring.angle0) * 4.2f;
            if (onPath.magnitude < CombatWorld.PlayerBound - 0.5f) Place(onPath); else Place(ring.at + Sim.Dir(ring.angle0) * 3.0f);
            yield return Wait(1.0f);
            Check("an orb on its path hits once for 15 and pops", HitsSince(h2, "Chime Well orb", d.Player) >= 1 && hits.Last(h => h.source == "Chime Well orb").dmg == 15, HitsSince(h2, "Chime Well orb", d.Player) + " orb hits");
            Place(new Vector3(0, 0, 7));
            yield return Until(() => boss.Current == "between blocks", 20, "Wells block ends");
            var rings = boss.RingLog.Skip(rings0).ToList();
            Check("two groups of 3 wells, 6 orbs each", rings.Count == 6 && rings.All(r => r.count == 6 && r.wave == 0) && boss.WellEruptions - erupt0 == 6 && boss.GroupLog.Count - g0 == 2);
            Check("no lane strikes while a well warns above 50%", boss.ConcurrentWithWellsP1 == conc0);
            Check("orbs leave the floor before the block ends", boss.OrbCount == 0);
        }

        IEnumerator CarillonHigh()
        {
            Section("Carillon Chain (above 50%)");
            var soaker = new SoakBot { Boss = () => boss };
            d.Player.Input = soaker;
            int s0 = boss.Soaks, m0 = boss.Misses, h0 = d.World.Tick;
            boss.ForcedNext = "Carillon";
            yield return Until(() => boss.ChainActive && boss.ChainIndex >= 0, 20, "chain lights");
            yield return Shot("carillon_chain");
            Overhead("carillon_overhead");
            Check("3 circles, 3 s each, every step walkable inside the live time",
                boss.ChainLength == 3 && boss.ChainLiveTicks == Sim.Ticks(3f) && StepsWalkable(boss.ChainPositions, 3f), Steps(boss.ChainPositions));
            yield return Until(() => !boss.ChainActive, 20, "chain ends");
            Check("walking soaker clears all 3 in order: no damage", boss.Soaks - s0 == 3 && boss.Misses == m0 && HitsSince(h0, "Carillon") == 0);
            // out of order: stand in circle 3 the whole time -> circles 1 and 2 go off; circle 3 soaks when it lights
            d.Player.Input = bot; goal = null;
            yield return Until(() => boss.Current == "between blocks", 10, "boundary");
            s0 = boss.Soaks; m0 = boss.Misses; h0 = d.World.Tick; int ooo = boss.OutOfOrderStands;
            boss.ForcedNext = "Carillon";
            yield return Until(() => boss.ChainActive && boss.ChainPositions.Count == 3, 20, "second chain");
            Place(boss.ChainPositions[2]);
            yield return Until(() => !boss.ChainActive, 20, "second chain ends");
            Check("standing in a future circle never counts: two go off (30 each to everyone), the third soaks when lit",
                boss.Misses - m0 == 2 && boss.Soaks - s0 == 1 && boss.OutOfOrderStands > ooo && HitsSince(h0, "Carillon (unsoaked)") == 2 && hits.Last(h => h.source.StartsWith("Carillon")).dmg == 30);
            Place(new Vector3(0, 0, 7));
        }

        IEnumerator MarkCheck()
        {
            Section("Resonant Mark");
            yield return Until(() => boss.Current == "between blocks", 15, "boundary");
            // three-player roster (simulated, not co-op): up to 3 marks
            AddExtra(new Vector3(-6, 0, 3)); AddExtra(new Vector3(6, 0, 3));
            Place(new Vector3(0, 0, 8));
            int c0 = boss.MarksCast, dr0 = boss.MarksDropped, r0 = boss.RingLog.Count;
            boss.ForcedNext = "Mark";
            yield return Until(() => boss.MarksCast > c0, 15, "mark cast");
            Check("one mark per player, up to 3", boss.MarksCast - c0 == 3 && boss.ActiveMarks == 3);
            // the player walks while marked: the ring follows
            goal = new Vector3(-3, 0, 6);
            yield return Wait(1.5f);
            yield return Shot("mark_on_players");
            goal = new Vector3(-8, 0, 2);
            yield return Until(() => boss.MarksDropped >= dr0 + 3, 12, "marks drop");
            var dropPos = Sim.Flat(d.Player.transform.position);
            yield return Until(() => boss.RingLog.Count >= r0 + 3, 4, "mark wells erupt");
            var rings = boss.RingLog.Skip(r0).ToList();
            Check("each mark drops a small well where its player stands, which fires 3 orbs", rings.Count == 3 && rings.All(r => r.count == 3) && rings.Any(r => (r.at - dropPos).magnitude < 0.8f),
                string.Join(" | ", rings.Select(r => $"{r.at.x:0.0},{r.at.z:0.0} x{r.count}")) + $"  player {dropPos.x:0.0},{dropPos.z:0.0}");
            RemoveExtras();
            yield return Until(() => boss.Current == "between blocks", 30, "boundary after mark");
        }

        IEnumerator ClapperCheck()
        {
            Section("Clapper Strike");
            goal = null; int h0 = d.World.Tick;
            Place(boss.transform.position + new Vector3(0, 0, 3f));
            yield return Block("Clapper");
            Check("cone hits the nearest player once for 60", HitsSince(h0, "Clapper Strike", d.Player) == 1 && hits.Last(h => h.source == "Clapper Strike").dmg == 60, HitsSince(h0, "Clapper Strike", d.Player) + " hits");
        }

        // ---------------- 50% ----------------
        IEnumerator GrandToll()
        {
            Section("Grand Toll (50%)");
            Place(new Vector3(0, 0, 8));
            boss.DebugSetHpFraction(0.49f);
            yield return Until(() => boss.HoldHp, 30, "Grand Toll begins");
            d.World.DamageBoss(400, boss.transform.position + Vector3.up * 2, "test");
            Check("HP holds at half through the Grand Toll", boss.Hp == (boss.MaxHp + 1) / 2, $"hp {boss.Hp}");
            yield return Until(() => boss.Clock.Commits >= 1, 12, "metronome commits");
            yield return Shot("grand_toll");
            Check("the metronome commits to Haste at 185 BPM, once", boss.Clock.State == Tempo.Haste && Mathf.Approximately(boss.Clock.Bpm, 185) && boss.Clock.Commits == 1 && boss.Phase2);
            var arena = FindAnyObjectByType<MetronomeCourtArena>();
            yield return Wait(1.2f);
            if (Court) Check("the weight slides low (fast) and the court lights warm", arena && arena.WeightHeight < MetronomeCourtArena.WeightHaste + 0.6f && arena.TempoBlend > 0.8f, arena ? $"weight {arena.WeightHeight:0.00} blend {arena.TempoBlend:0.00}" : "");
            int startLog = boss.LaneLog.Count;
            yield return Until(() => !boss.HoldHp, 25, "recital ends");
            Check("the recital is one Called Changes cast at Haste", boss.LaneLog.Skip(startLog).Count(x => x.pattern == "Called Changes") == 8);
            int hp = boss.Hp;
            d.World.DamageBoss(100, boss.transform.position + Vector3.up * 2, "test");
            Check("after the recital he takes damage again", boss.Hp == hp - 100);
        }

        // ---------------- phase 2 ----------------
        IEnumerator CalledChanges()
        {
            Section("Toll Lanes: Called Changes (below 50%)");
            yield return Until(() => boss.Current == "between blocks", 20, "boundary");
            var dodger = new LaneDodger { World = () => d.World };
            d.Player.Input = dodger;
            int totalHits = 0; var orders = new List<string>();
            for (int cast = 0; cast < 3; cast++)
            {
                maxLaneHazards = 0; int h0 = d.World.Tick, startLog = boss.LaneLog.Count, off0 = boss.OffBeatStrikes;
                boss.ForcedNext = "Called";
                yield return Until(() => boss.Current.StartsWith("Toll Lanes: Called"), 20, "Called Changes starts");
                yield return Until(() => d.World.Hazards.Live.Any(h => h.Source == "Toll Lane"), 3, "Called Changes runes");   // after the beat alignment
                yield return Wait(0.1f);
                if (cast == 0) { yield return Shot("called_changes_all_runes"); Overhead("called_changes_overhead"); }
                Check($"cast {cast + 1}: all 8 lanes' runes appear at once, one warning per lane", maxLaneHazards == 8, $"{maxLaneHazards} lanes lit");
                yield return Until(() => boss.Current == "between blocks", 15, "Called Changes ends");
                var log = boss.LaneLog.Skip(startLog).Where(x => x.pattern == "Called Changes").ToList();
                var pattern = boss.LastPattern;
                orders.Add(string.Join(" ", pattern.Select(s => s.B >= 0 ? $"{s.A + 1}+{s.B + 1}" : $"{s.A + 1}")));
                Check($"cast {cast + 1}: 8 strikes, order rules hold, on the beat", log.Count == 8 && OracleLanes.OrderRulesHold(pattern) && boss.OffBeatStrikes == off0);
                // fastest-filling fires first: every lane's warning started together, so strike order = shortest warning first
                totalHits += HitsSince(h0, "Toll Lane", d.Player);
            }
            Note("orders: " + string.Join("  |  ", orders));
            Check("three casts use different orders", orders.Distinct().Count() == 3);
            Check("Called Changes: the walking dodger takes no lane hit over 3 casts", totalHits == 0, totalHits + " hits");
            d.Player.Input = bot;
        }

        IEnumerator WellsLow()
        {
            Section("Chime Wells (below 50%)");
            Place(new Vector3(0, 0, 9)); goal = null;
            int r0 = boss.RingLog.Count, g0 = boss.GroupLog.Count;
            boss.ForcedNext = "WellsBurst";
            yield return Until(() => boss.GroupLog.Count >= g0 + 1, 20, "burst group drops");
            yield return Wait(0.2f);
            yield return Shot("wells_burst_group_arrows");
            Overhead("wells_burst_overhead");
            yield return Until(() => boss.Current == "between blocks", 25, "burst ends");
            var rings = boss.RingLog.Skip(r0).ToList(); var groups = boss.GroupLog.Skip(g0).ToList();
            Check("below 50%: more wells per group (4, against 3 above), two groups, 9 orbs each, each group detonating together",
                groups.Count == 2 && groups.All(g => g.wells == ChimeboundOracleEncounter.WellsPerGroupLow) && rings.Count == 8 && rings.All(r => r.count == 9) &&
                rings.GroupBy(r => r.tick).Count() == 2, $"groups {string.Join(",", groups.Select(g => g.wells))}, rings {rings.Count}");
            // Called Changes + Wells: two wells erupt mid-pattern
            int r1 = boss.RingLog.Count, l1 = boss.LaneLog.Count;
            yield return Block("CalledWells");
            var mid = boss.RingLog.Skip(r1).Where(r => r.wave == 0).ToList(); var lanes = boss.LaneLog.Skip(l1).ToList();
            Check("Called Changes + Wells: two 9-orb wells erupt while lanes are still striking",
                mid.Count == 2 && mid.All(r => r.count == 9) && lanes.Count >= 8 && mid.All(r => r.tick > lanes.First().tick - 60 && r.tick < lanes.Last().tick));
            var second = boss.RingLog.Skip(r1).Where(r => r.wave == 1).ToList();
            Check("...and each fires a second ring 2 beats later, turned 20 degrees", second.Count == 2 &&
                mid.All(a => second.Any(b => (b.at - a.at).magnitude < 0.01f && Mathf.Abs(Mathf.DeltaAngle(b.angle0 - a.angle0, ChimeboundOracleEncounter.WaveTwist)) < 0.01f && Mathf.Abs(b.tick - a.tick - TempoClock.TicksFor(2, 185)) <= 2)));
        }

        IEnumerator CarillonLow()
        {
            Section("Carillon Chain (below 50%)");
            yield return Until(() => boss.Current == "between blocks", 20, "boundary");
            d.Player.Input = new SoakBot { Boss = () => boss };
            int s0 = boss.Soaks, m0 = boss.Misses, p0 = boss.PressureLog.Count, h0 = d.World.Tick;
            boss.ForcedNext = "Carillon";
            yield return Until(() => boss.ChainActive && boss.ChainIndex >= 0, 20, "chain lights");
            Check("5 circles, 2 s each, every step walkable inside the live time", boss.ChainLength == 5 && boss.ChainLiveTicks == Sim.Ticks(2f) && StepsWalkable(boss.ChainPositions, 2f), Steps(boss.ChainPositions));
            yield return Until(() => boss.ChainIndex >= 2, 10, "chain mid");
            yield return Shot("carillon_pressure_lanes");
            yield return Until(() => !boss.ChainActive, 20, "chain ends");
            Check("walking soaker clears all 5 in order", boss.Soaks - s0 == 5 && boss.Misses == m0, $"soaked {boss.Soaks - s0}, missed {boss.Misses - m0}");
            Check("single lanes strike during the chain, never on the lit circle or the next two", boss.PressureLog.Count > p0 && boss.PressureOverlaps == 0, $"{boss.PressureLog.Count - p0} pressure lanes");
            Note($"pressure-lane hits on the soaker (it ignores lanes): {HitsSince(h0, "Toll Lane", d.Player)}");
            yield return Until(() => boss.Current == "between blocks", 10, "chain block ends");
            d.Player.Input = bot;
        }

        IEnumerator DifficultyChecks()
        {
            Section("difficulty variants");
            var level = d.World.Difficulty;
            // Hard: Echo
            d.World.Difficulty = Difficulty.Hard;
            int l0 = boss.LaneLog.Count; var dodger = new LaneDodger { World = () => d.World }; d.Player.Input = dodger; int h0 = d.World.Tick;
            yield return Block("Called");
            var log = boss.LaneLog.Skip(l0).ToList();
            var firsts = log.Where(x => !x.echo).ToList(); var echoes = log.Where(x => x.echo).ToList();
            int gap = TempoClock.TicksFor(2, 185) + 1;
            Check("Hard (Echo): each lane strikes again 2 beats after its first hit", firsts.Count == 8 && echoes.Count == 8 &&
                firsts.All(f => echoes.Any(e => e.lane == f.lane && Mathf.Abs(e.tick - f.tick - gap) <= 2)));
            Note($"Hard: walking dodger lane hits {HitsSince(h0, "Toll Lane", d.Player)} (route check above proves a walking route exists)");
            // Easy: 6-beat warnings
            d.World.Difficulty = Difficulty.Easy;
            l0 = boss.LaneLog.Count; int spawnTick = -1;
            boss.ForcedNext = "Called";
            yield return Until(() => boss.Current.StartsWith("Toll Lanes: Called"), 20, "Easy cast starts");
            yield return Until(() => d.World.Hazards.Live.Any(h => h.Source == "Toll Lane"), 3, "Easy runes");
            var firstRune = d.World.Hazards.Live.FirstOrDefault(h => h.Source == "Toll Lane");
            spawnTick = firstRune != null ? d.World.Tick - firstRune.Age : d.World.Tick;
            yield return Until(() => boss.Current == "between blocks", 15, "Easy cast ends");
            var first = boss.LaneLog.Skip(l0).First();
            int want = TempoClock.TicksFor(6, 185);
            Check("Easy: the first lane strikes 6 beats after the runes appear (4 on Medium)", Mathf.Abs(first.tick - spawnTick - want) <= 3, $"{first.tick - spawnTick} ticks, want {want}");
            d.World.Difficulty = level; d.Player.Input = bot;
        }

        IEnumerator LastBellCheck()
        {
            Section("Last Bell (soft enrage)");
            boss.ForceEnrage = true; int l0 = boss.LaneLog.Count;
            yield return Until(() => boss.LaneLog.Skip(l0).Count(x => x.pattern == "Last Bell") >= 8, 20, "Last Bell strikes");
            yield return Shot("last_bell");
            var lb = boss.LaneLog.Skip(l0).Where(x => x.pattern == "Last Bell").Take(8).ToList();
            Check("Last Bell: every lane strikes together, no safe gap", lb.Count == 8 && lb.Select(x => x.lane).Distinct().Count() == 8 && lb.Max(x => x.tick) - lb.Min(x => x.tick) <= 1 && boss.Enraged);
            Check("enrage starts at 4:00 of combat by default", ChimeboundOracleEncounter.EnrageTicks == 240 * Sim.Hz);
            int g0 = boss.GroupLog.Count;
            yield return Until(() => boss.GroupLog.Count > g0, 8, "berserk wells");
            Check("berserk: groups of 5 wells fall between the lane volleys", boss.GroupLog.Count > g0 && boss.GroupLog.Last().wells == ChimeboundOracleEncounter.WellsPerGroupLastBell);
        }

        // ---------------- helpers ----------------
        static bool StepsWalkable(List<Vector3> pts, float live)
        {
            for (int i = 1; i < pts.Count; i++)
            {
                float walk = Mathf.Max(0, (pts[i] - pts[i - 1]).magnitude - ChimeboundOracleEncounter.SoakRadius) / PlayerController.WalkSpeed;
                if (walk + ChimeboundOracleEncounter.SoakDwellTicks / 60f > live - 0.25f) return false;
            }
            return true;
        }
        static string Steps(List<Vector3> pts) => string.Join(", ", Enumerable.Range(1, Mathf.Max(0, pts.Count - 1)).Select(i => $"{(pts[i] - pts[i - 1]).magnitude:0.0} m"));

        PlayerController AddExtra(Vector3 at)
        {
            var p = PlayerController.Spawn(d.World, RunDirector.Classes[extras.Count % 3], d.World.Constrain(at, PlayerController.Radius), 180);
            p.Input = new Bot(); p.AbilitiesEnabled = false; extras.Add(p); return p;
        }
        void RemoveExtras()
        {
            foreach (var p in extras) if (p) { d.World.Players.Remove(p); Destroy(p.gameObject); }
            extras.Clear();
        }

        void Finish()
        {
            lines.Insert(0, $"Chimebound Oracle autotest: {pass} passed, {fail} failed  ({(Application.isEditor ? "Editor Play Mode" : "Windows player")}, Unity {Application.unityVersion})");
            File.WriteAllLines(Path.Combine(dir, "chimebound-oracle-report.txt"), lines);
            Debug.Log($"[chimebound-oracle-test] done: {pass} passed, {fail} failed");
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
