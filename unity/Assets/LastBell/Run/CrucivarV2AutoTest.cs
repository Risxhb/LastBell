using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Acceptance run for Crucivar's ability set v2 inside the real game (Brass Foundry practice):
    //   Unity.exe -projectPath unity -executeMethod LastBell.Run.EditorTools.CrucivarTestTools.PlayModeTest
    //             -crucivarV2Test [-crucivarAbility <id>] -lastbellOut <dir>
    // It holds Crucivar's schedule (TestHold) and runs every CrucivarAbilityTest (one per ability in
    // Encounters/Crucivar/Tests, plus the phase-plan test), optionally only the one whose Id matches -crucivarAbility.
    // Extra party members are real PlayerControllers driven by scripted input, so hits, stamina, dash and knockback all
    // go through the normal code. Writes crucivar-v2-report.txt and screenshots, exits 0 (all passed) or 1.
    public sealed class CrucivarV2AutoTest : MonoBehaviour
    {
        public static bool Requested => Environment.GetCommandLineArgs().Any(a => a.Equals("-crucivarV2Test", StringComparison.OrdinalIgnoreCase));
        static string OutDir => CrucivarArgs.Arg("-lastbellOut") ?? Path.Combine(Application.persistentDataPath, "crucivar-v2-test");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Requested) return;
            var d = FindAnyObjectByType<RunDirector>();
            if (d && !d.GetComponent<CrucivarV2AutoTest>()) d.gameObject.AddComponent<CrucivarV2AutoTest>();
        }

        // ---------------- scripted input ----------------
        // Steers toward a target at a set speed (4.2 m/s = the prototype walk, 6 = design walk), or holds still. Dash()
        // presses dash once on the next tick, in the steering direction.
        public sealed class Scripted : IInputSource
        {
            public Vector3? Target; public float Speed = 4.2f; public Vector3 Aim; public bool Sprint;
            public Vector3 Heading;   // used when Target is null: constant direction (zero = stand still)
            bool dash;
            public void Dash() => dash = true;
            public void Stop() { Target = null; Heading = Vector3.zero; Sprint = false; }
            public void Go(Vector3 to, float speed = 4.2f) { Target = Sim.Flat(to); Speed = speed; }
            public PlayerInput Read(PlayerController who)
            {
                var r = new PlayerInput { Aim = Aim, Sprint = Sprint };
                Vector3 move = Heading;
                if (Target.HasValue)
                {
                    var to = Target.Value - Sim.Flat(who.transform.position);
                    float step = Speed * Sim.Dt;
                    move = to.magnitude <= Mathf.Max(0.05f, step) ? Vector3.zero : to.normalized * Mathf.Clamp01(Speed / PlayerController.WalkSpeed);
                }
                var cam = Camera.main; var fwd = cam ? Sim.Flat(cam.transform.forward).normalized : Vector3.forward;
                var right = Vector3.Cross(Vector3.up, fwd);
                r.Move = Vector2.ClampMagnitude(new Vector2(Vector3.Dot(move, right), Vector3.Dot(move, fwd)), 1);
                if (dash) { r.Dash = true; dash = false; }
                return r;
            }
        }

        public sealed class HitRec { public PlayerController P; public Hazard H; public string Source; public int Damage, Tick; }
        public sealed class SpawnRec { public Hazard H; public string Source; public int Tick; }

        public RunDirector D { get; private set; }
        public CombatWorld W => D.World;
        public CrucivarEncounter Boss { get; private set; }
        public PlayerController Main => D.Player;
        public readonly List<PlayerController> Party = new List<PlayerController>();
        public readonly List<HitRec> Hits = new List<HitRec>();
        public readonly List<SpawnRec> Spawns = new List<SpawnRec>();
        readonly Dictionary<PlayerController, Scripted> inputs = new Dictionary<PlayerController, Scripted>();
        readonly Scripted mainInput = new Scripted();
        readonly List<string> lines = new List<string>();
        int pass, fail, errors, shots; string dir, section = "";
        CombatWorld hooked;

        void Start()
        {
            D = GetComponent<RunDirector>(); dir = OutDir; Directory.CreateDirectory(dir);
            Application.logMessageReceived += OnLog;
            Application.runInBackground = true;
            CrucivarArgs.SetOverride = "v2";
            StartCoroutine(MainLoop());
        }
        void OnDestroy() { Application.logMessageReceived -= OnLog; }
        void OnLog(string msg, string stack, LogType t) { if ((t == LogType.Exception || t == LogType.Error) && !(stack ?? "").Contains("UnityEditor.")) { errors++; lines.Add("      LOG " + t + ": " + msg); } }

        // ---------------- reporting ----------------
        public void Check(string name, bool ok, string detail = "")
        {
            if (ok) pass++; else fail++;
            var line = $"{(ok ? "PASS" : "FAIL")}  [{section}] {name}{(string.IsNullOrEmpty(detail) ? "" : "  — " + detail)}";
            lines.Add(line); Debug.Log("[crucivar-v2-test] " + line);
        }
        public void Note(string s) { lines.Add("      " + s); Debug.Log("[crucivar-v2-test] " + s); }
        public IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, $"{++shots:00}_{name}.png"), tex.EncodeToPNG()); Destroy(tex);
        }
        public int Failures => fail;

        // ---------------- waiting ----------------
        public IEnumerator Seconds(float s) { float t = 0; while (t < s) { t += Time.deltaTime; yield return null; } }
        public IEnumerator Ticks(int n) { int end = W.Tick + n; float guard = 0; while (W != null && W.Tick < end && guard < n / 20f + 10) { guard += Time.deltaTime; yield return null; } }
        public IEnumerator Until(Func<bool> cond, float timeout, string what)
        {
            float t = 0; while (!cond() && t < timeout) { t += Time.deltaTime; yield return null; }
            if (!cond()) Check("reached: " + what, false, $"timed out after {timeout}s");
        }

        // ---------------- setup ----------------
        // Fresh combat with a held Crucivar, `players` party members (the director's player first), everyone standing with
        // full HP and stamina at the given spots (default: a ring 5 m south of the centre), scripted input idle.
        public IEnumerator Setup(int players, Difficulty level = Difficulty.Medium, Vector3[] at = null, float hpFraction = 0.9f)
        {
            yield return EnsureCombat();
            if (Boss == null) yield break;
            Boss.TestHold = true;
            yield return Until(() => !Boss.TestBusy, 20, "previous test body finished");
            W.ClearTransient();
            W.Difficulty = level;
            Boss.DebugSetHpFraction(hpFraction);
            Boss.transform.SetPositionAndRotation(new Vector3(0, 0, -1.5f), Quaternion.identity);
            while (Party.Count > players) { var p = Party[Party.Count - 1]; Party.RemoveAt(Party.Count - 1); W.Players.Remove(p); inputs.Remove(p); Destroy(p.gameObject); }
            if (Party.Count == 0) { Party.Add(Main); inputs[Main] = mainInput; }
            while (Party.Count < players)
            {
                var p = PlayerController.Spawn(W, "Vanguard", new Vector3(0, 0, 5), 180);
                var s = new Scripted(); p.Input = s; inputs[p] = s; p.AbilitiesEnabled = false; Party.Add(p);
            }
            for (int i = 0; i < Party.Count; i++)
            {
                var p = Party[i];
                p.DebugRevive();
                var pos = at != null && i < at.Length ? at[i] : new Vector3(0, 0, 5) + Quaternion.Euler(0, 360f * i / Mathf.Max(1, players), 0) * Vector3.right * (players > 1 ? 1.5f : 0);
                p.PrepareForEncounter(pos, 180);
                p.DebugSetHp(PlayerController.MaxHp);
                p.AbilitiesEnabled = false;
                In(p).Stop();
            }
            Hits.Clear(); Spawns.Clear();
            yield return Ticks(2);
        }

        public Scripted In(PlayerController p) => inputs[p];
        public void Place(PlayerController p, Vector3 pos) { pos.y = 0; p.transform.position = pos; }

        // Runs a body on Crucivar's script (one tick per step, exactly like a scheduled attack) and waits for it to end.
        public IEnumerator Fire(IEnumerable<int> body, float timeout = 30)
        {
            Begin(body);
            yield return WaitIdle(timeout);
        }
        // Starts a body and returns at once, so the test can steer players while it runs.
        public void Begin(IEnumerable<int> body) { Boss.TestRun(body); }
        public IEnumerator WaitIdle(float timeout = 30) { yield return Until(() => Boss == null || !Boss.TestBusy, timeout, "ability finished"); }

        // ---------------- queries ----------------
        public IEnumerable<HitRec> HitsOn(PlayerController p, string sourcePrefix = null) => Hits.Where(h => h.P == p && (sourcePrefix == null || h.Source.StartsWith(sourcePrefix)));
        public bool WasHit(PlayerController p, string sourcePrefix = null) => HitsOn(p, sourcePrefix).Any();
        public IEnumerable<SpawnRec> Spawned(string sourcePrefix) => Spawns.Where(s => s.Source.StartsWith(sourcePrefix));
        // no instance hit the same player twice
        public bool OneHitPerInstance() => Hits.Where(h => h.H.BurnPerSecond <= 0).GroupBy(h => (h.P, h.H.Id)).All(g => g.Count() == 1);

        // Kills Crucivar now (the same path as a killing blow); the practice then ends, and the next Setup restarts it.
        public void KillBoss() { Boss.DebugDefeat(); }

        IEnumerator EnsureCombat()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (D.State == RunState.Combat && D.Boss is CrucivarEncounter b && !b.Dead && !Main.Down) { Hook(); Boss = b; yield break; }
                if (D.State == RunState.Combat || D.State == RunState.Downed || D.State == RunState.Cleared) yield return Until(() => D.State == RunState.Results, 12, "results after a wipe/defeat");
                if (D.State == RunState.Results) { Party.Clear(); inputs.Clear(); D.RequestRetry(); }
                if (D.State == RunState.Menu) { D.InputOverride = mainInput; D.StartPractice("Vanguard", RunDirector.CrucivarPractice); }
                yield return Until(() => D.State == RunState.Ready, 8, "ready");
                D.RequestReady();
                if (D.Boss is CrucivarEncounter nb) nb.TestHold = true;   // before the script's first step
                yield return Until(() => D.State == RunState.Combat, 10, "combat");
            }
            Boss = D.Boss as CrucivarEncounter;
            Check("practice combat with Crucivar", Boss != null && D.State == RunState.Combat);
        }

        void Hook()
        {
            if (hooked == W) return;
            hooked = W;
            W.PlayerHit += (p, h, dmg) => Hits.Add(new HitRec { P = p, H = h, Source = h.Source, Damage = dmg, Tick = W.Tick });
            W.Hazards.Spawned += h => Spawns.Add(new SpawnRec { H = h, Source = h.Source, Tick = W.Tick });
        }

        // ---------------- the run ----------------
        IEnumerator MainLoop()
        {
            Note($"Crucivar v2 autotest · Unity {Application.unityVersion} · {SystemInfo.graphicsDeviceName} · {DateTime.Now:yyyy-MM-dd HH:mm}");
            D.InputOverride = mainInput;
            yield return Seconds(1.0f);
            Check("boots into the menu", D.State == RunState.Menu);
            string only = CrucivarArgs.Arg("-crucivarAbility");
            var tests = typeof(CrucivarAbilityTest).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(CrucivarAbilityTest).IsAssignableFrom(t))
                .Select(t => (CrucivarAbilityTest)Activator.CreateInstance(t))
                .Where(t => only == null || t.Id == only || t.Id.StartsWith(only + "_"))
                .OrderBy(t => t.Order).ThenBy(t => t.Id).ToList();
            Note("tests: " + string.Join(", ", tests.Select(t => t.Id)));
            Check("found the requested tests", tests.Count > 0, only ?? "all");
            foreach (var t in tests)
            {
                section = t.Id; int before = fail;
                CrucivarArgs.SetOverride = t.Set;
                Note($"---- {t.Id} ----");
                var e = t.Run(this);
                while (true)
                {
                    object cur;
                    try { if (!e.MoveNext()) break; cur = e.Current; }
                    catch (Exception ex) { Check("test body ran without an exception", false, ex.GetType().Name + ": " + ex.Message); break; }
                    yield return cur;
                }
                Note($"{t.Id}: {(fail == before ? "all passed" : (fail - before) + " failed")}");
            }
            section = "suite";
            Check("no errors or exceptions logged", errors == 0, $"{errors}");
            lines.Insert(0, $"Crucivar v2 autotest: {pass} passed, {fail} failed");
            File.WriteAllLines(Path.Combine(dir, "crucivar-v2-report.txt"), lines);
            Debug.Log($"[crucivar-v2-test] done: {pass} passed, {fail} failed");
            yield return Seconds(0.5f);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            UnityEditor.EditorApplication.Exit(fail == 0 ? 0 : 1);
#else
            Application.Quit(fail == 0 ? 0 : 1);
#endif
        }
    }

    // One test per ability (Encounters/Crucivar/Tests/<Ability>Test.cs). Id is the ability id (or crucivar.phase_plan).
    public abstract class CrucivarAbilityTest
    {
        public abstract string Id { get; }
        public virtual int Order => 0;
        public virtual string Set => "v2";   // the ability set this test runs under (CrucivarArgs.SetOverride)
        public abstract IEnumerator Run(CrucivarV2AutoTest t);
    }
}
