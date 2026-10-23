using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Scripted acceptance run inside the real game (Windows player or Play Mode), started with
    //   LastBell.exe -lastbellAutotest -lastbellOut <folder>
    // It drives the RunDirector through its public requests and a bot that sends ordinary PlayerInput (no direct ability
    // calls), checks run/transition/kit invariants, captures gameplay-camera screenshots and writes a report, then quits
    // with exit code 0 (all checks passed) or 1. Dev hooks it uses are the same ones the HUD labels (F9/F8: defeat the
    // boss, set its HP fraction) plus DebugSetHp on the player to shorten the defeat path; all damage taken comes from
    // real hazards.
    public sealed partial class RunAutoTest : MonoBehaviour
    {
        public static bool Requested => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellAutotest", StringComparison.OrdinalIgnoreCase));
        static string OutDir
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.FindIndex(args, a => a.Equals("-lastbellOut", StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Application.persistentDataPath, "autotest");
            }
        }

        sealed class Bot : IInputSource
        {
            public Vector3 MoveWorld; public Vector3 Aim; public bool Sprint;
            public bool[] press = new bool[5]; public bool[] hold = new bool[5]; public bool dash, jump, potion;
            public PlayerInput Read(PlayerController who)
            {
                var r = new PlayerInput { Aim = Aim, Sprint = Sprint, Dash = dash, Jump = jump, Potion = potion,
                    P0 = press[0], P1 = press[1], P2 = press[2], P3 = press[3], P4 = press[4],
                    H0 = hold[0] || press[0], H1 = hold[1], H2 = hold[2], H3 = hold[3] || press[3], H4 = hold[4] };
                var cam = Camera.main; var fwd = cam ? Sim.Flat(cam.transform.forward).normalized : Vector3.forward;
                var right = Vector3.Cross(Vector3.up, fwd);
                r.Move = Vector2.ClampMagnitude(new Vector2(Vector3.Dot(MoveWorld, right), Vector3.Dot(MoveWorld, fwd)), 1);
                for (int i = 0; i < 5; i++) press[i] = false; dash = jump = potion = false;
                return r;
            }
            public void Clear() { MoveWorld = Vector3.zero; Sprint = false; for (int i = 0; i < 5; i++) { press[i] = hold[i] = false; } dash = jump = false; }
        }

        RunDirector d; readonly Bot bot = new Bot();
        readonly List<string> lines = new List<string>(); int pass, fail, errors, shots;
        readonly List<string> errorLines = new List<string>();
        readonly List<string> directorLog = new List<string>();   // transitions from every map's director
        string dir;

        // The test lives on its own object that survives scene loads, so the map tour can switch maps under it.
        static RunAutoTest active;
        public static void Launch()
        {
            if (active) return;
            var go = new GameObject("Run AutoTest");
            DontDestroyOnLoad(go);
            active = go.AddComponent<RunAutoTest>();
        }

        void Start()
        {
            d = RunDirector.Instance; dir = OutDir; Directory.CreateDirectory(dir);
            Application.logMessageReceived += OnLog;
            Application.runInBackground = true;   // the project pauses unfocused players; the test must not
            Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
            StartCoroutine(Main());
        }
        void OnDestroy() { Application.logMessageReceived -= OnLog; }
        void OnLog(string msg, string stack, LogType t)
        {
            if (t == LogType.Error || t == LogType.Exception || t == LogType.Assert) { errors++; if (errorLines.Count < 30) errorLines.Add($"{t}: {msg}\n{stack}"); }
        }

        void Check(string name, bool ok, string detail = "")
        {
            if (ok) pass++; else fail++;
            var line = $"{(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : "  — " + detail)}";
            lines.Add(line); Debug.Log("[autotest] " + line);
        }
        void Note(string s) { lines.Add("      " + s); Debug.Log("[autotest] " + s); }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var path = Path.Combine(dir, $"{++shots:00}_{name}.png");
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG()); Destroy(tex);
            Note("screenshot " + Path.GetFileName(path));
            yield return null;
        }
        // Fixed-step frame capture for short videos: Time.captureFramerate makes every frame exactly 1/60 s of game
        // time, so the sequence plays back at real speed whatever the capture cost. Frames become MP4 afterwards
        // (tools: run-loop-logs/frames-to-mp4.py via Blender's bundled FFmpeg).
        IEnumerator Capture(string name, int frames)
        {
            var cdir = Path.Combine(dir, "frames_" + name); Directory.CreateDirectory(cdir);
            Time.captureFramerate = 60;
            for (int i = 0; i < frames; i++)
            {
                yield return new WaitForEndOfFrame();
                var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(cdir, $"{i:0000}.jpg"), tex.EncodeToJPG(88)); Destroy(tex);
            }
            Time.captureFramerate = 0;
            Note($"video frames {name}: {frames}");
        }
        IEnumerator Wait(float s) { float t = 0; while (t < s) { t += Time.deltaTime; yield return null; } }
        // test setup for fixed-length kit/rotation fights against the Bellkeeper: these check a class, not survival, so the
        // player is topped up instead of being downed mid-check (the defeat path is tested on its own)
        void KeepAlive() { if (d.Player && d.Player.Hp < 60) d.Player.DebugSetHp(PlayerController.MaxHp); }
        IEnumerator WaitAlive(float s) { float t = 0; while (t < s) { KeepAlive(); t += Time.deltaTime; yield return null; } }
        bool Safe(Func<bool> c) { try { return c(); } catch (Exception) { return false; } }
        IEnumerator Until(Func<bool> cond, float timeout, string what)
        {
            float t = 0; while (!Safe(cond) && t < timeout) { t += Time.deltaTime; yield return null; }
            if (!Safe(cond)) Check("reached: " + what, false, $"timed out after {timeout}s (state {d.State})");
        }

        // ------------------------------------------------------------ bot behaviours
        IEnumerator Fight(float seconds, Func<int, int> rotation, float keepAt)
        {
            float t = 0; int tick = 0;
            while (t < seconds && d.State == RunState.Combat)
            {
                var p = d.Player; var b = d.Boss;
                if (p && b)
                {
                    var to = Sim.Flat(b.transform.position - p.transform.position);
                    float dist = to.magnitude - b.BodyRadius;
                    bot.Aim = b.transform.position;
                    bot.MoveWorld = dist > keepAt + 0.4f ? to.normalized : dist < keepAt - 0.6f ? -to.normalized : Vector3.zero;
                    var esc = Escape(p);
                    if (esc != Vector3.zero) { bot.MoveWorld = esc; bot.Sprint = true; } else bot.Sprint = false;
                    int slot = rotation(tick++);
                    if (slot >= 0) bot.press[slot] = true;
                }
                t += Time.deltaTime; yield return null;
            }
            bot.Clear();
        }
        // move out of any hazard that will resolve soon: straight away from its origin, sideways out of a lane,
        // and for a ring toward whichever safe side (inside / outside) is closer
        Vector3 Escape(PlayerController p)
        {
            var pos = p.transform.position;
            foreach (var h in d.World.Hazards.Live)
            {
                if (!h.Contains(pos) || h.ResolveAt - h.Age > 45) continue;
                var away = Sim.Flat(pos - h.Origin); if (away.sqrMagnitude < 0.01f) away = Vector3.right; away.Normalize();
                if (h.Shape == HazardShape.Lane) return h.PushDir(pos);
                if (h.Shape == HazardShape.Ring) return Sim.Flat(pos - h.Origin).magnitude - h.Inner < h.Radius - Sim.Flat(pos - h.Origin).magnitude ? -away : away;
                if (h.Shape == HazardShape.Cone) { var side = Vector3.Cross(Vector3.up, Sim.Dir(h.Yaw)); return (Vector3.Dot(away, side) >= 0 ? side : -side) * 0.8f + away * 0.6f; }
                return away;
            }
            return Vector3.zero;
        }
        // keep away from the boss and out of hazards, no attacks (used while waiting on boss behaviour)
        IEnumerator Survive(float seconds, Func<bool> until = null)
        {
            float t = 0;
            while (t < seconds && d.State == RunState.Combat && (until == null || !Safe(until)))
            {
                var p = d.Player; var b = d.Boss;
                if (p && b)
                {
                    var to = Sim.Flat(b.transform.position - p.transform.position);
                    bot.Aim = b.transform.position;
                    bot.MoveWorld = to.magnitude < 7.5f ? -to.normalized : Vector3.zero;
                    if (new Vector2(p.transform.position.x, p.transform.position.z).magnitude > 10f) bot.MoveWorld = Vector3.Cross(Vector3.up, to.normalized);
                    var esc = Escape(p); if (esc != Vector3.zero) bot.MoveWorld = esc;
                }
                t += Time.deltaTime; yield return null;
            }
            bot.Clear();
        }
        static int VanguardRotation(int f) => f % 240 == 60 ? 1 : f % 8 == 0 ? 0 : -1;
        int[] chimeSeq = { 1, 0, 0, 0, 2, 1, 0, 0, 0, 2, 3 }; int chimeStep; int chimeWait;
        int ChimeRotation(int f)
        {
            if (chimeWait-- > 0) return -1;
            int s = chimeSeq[chimeStep % chimeSeq.Length]; chimeStep++; chimeWait = s == 0 ? 26 : 34; return s;
        }

        IEnumerator KillDuringWindup(string label, string capture = null)
        {
            yield return Until(() => d.World != null && d.World.Hazards.Count > 0 && d.World.Hazards.Live.All(h => h.Age < h.ResolveAt - 6), 12, label + " hazard warning up");
            if (d.State != RunState.Combat) yield break;
            int hp0 = d.Player.Hp, resolved0 = d.World.Hazards.Resolved, pending = d.World.Hazards.Count;
            string names = string.Join(", ", d.World.Hazards.Live.Select(h => h.Source).Distinct());
            yield return Shot(label + "_windup");
            if (capture != null) StartCoroutine(Capture(capture, 200));
            d.Boss.DebugDefeat();
            Check($"{label}: boss death cancels pending hazards", d.World.Hazards.Count == 0, $"{pending} pending ({names}) -> {d.World.Hazards.Count}");
            Check($"{label}: defeated boss stops colliding", d.Boss == null || !d.Boss.Solid);
            yield return Wait(1.5f);
            Check($"{label}: no damage after boss death", d.Player == null || d.Player.Hp == hp0, $"hp {hp0} -> {d.Player?.Hp}");
            Check($"{label}: nothing resolved after boss death", d.World == null || d.World.Hazards.Resolved == resolved0);
        }

        IEnumerator AdvanceToFinal()
        {
            while (d.State == RunState.Combat && d.ProgressSlot < d.EncounterOrder.Length - 1)
            {
                int slot = d.ProgressSlot;d.Boss.DebugDefeat();
                yield return Until(() => d.State == RunState.Combat && d.ProgressSlot == slot + 1, 8, "random run advances slot " + slot);
                if (d.Player) d.Player.DebugSetHp(PlayerController.MaxHp);
            }
            Check("final encounter is Bellkeeper",d.Boss is BellkeeperEncounter && d.ProgressSlot == d.EncounterOrder.Length - 1);
        }

        // ------------------------------------------------------------ the scenario
        IEnumerator Main()
        {
            Note($"Last Bell autotest · {Application.version} · Unity {Application.unityVersion} · {SystemInfo.graphicsDeviceName} · {DateTime.Now:yyyy-MM-dd HH:mm}");
            d.InputOverride = bot;
            yield return Wait(1.0f);
            if (AurelionOnly) { yield return AurelionChecks(); Check("no errors or exceptions logged", errors == 0, errors + " logged"); Finish(); yield break; }
            Check($"boots into the menu on a run map ({RunMaps.CurrentName})", d.State == RunState.Menu && RunMaps.Current >= 0);
            for (int i = 0; i < RunMaps.All.Length; i++)
                Check($"map selectable: {RunMaps.All[i].Name}", RunMaps.InBuild(i), RunMaps.All[i].RunScene);
            yield return Shot("menu");
            if (OnlySylvara)
            {
                yield return SylvaraChecks();
                Check("no errors or exceptions logged", errors == 0, errors + " logged");
                Finish(); yield break;
            }
            foreach (var cls in RunDirector.Classes)
            {
                int slots = cls == "Vanguard" ? 5 : 6, found = 0;
                for (int i = 0; i < slots; i++) if (RunHud.SlotIcon(cls, i) != null) found++;
                Check($"{cls} skill-bar icons load ({slots} incl. dash)", found == slots, $"{found}/{slots}");
            }
            Check("potion icon loads", RunHud.Icon("potion") != null);
            yield return DifficultyChecks();

            // ---------- run 1: Vanguard, full victory, repeated presses ----------
            Check("StartRun accepted once", d.StartRun("Vanguard") && !d.StartRun("Vanguard"));
            yield return Wait(0.6f);
            yield return Shot("ready_vanguard");
            int starts0 = d.EncounterStarts;
            for (int i = 0; i < 4; i++) { d.RequestReady(); d.RequestReady(); yield return null; }
            yield return Until(() => d.State == RunState.Combat, 4, "combat 1");
            yield return Wait(0.3f);
            Check("repeated Ready starts exactly one encounter", d.EncounterStarts == starts0 + 1 && d.Encounter == d.EncounterOrder[0], $"starts {d.EncounterStarts - starts0}");
            Check("run contains all four real bosses with Bellkeeper last", d.EncounterOrder.Length == 4 && d.EncounterOrder.Distinct().Count() == 4 && RunDirector.Bosses[d.EncounterOrder.Last()].Final && !d.Boss.Placeholder);
            Check("run starts at full health and potions", d.Player.Hp == PlayerController.MaxHp && d.Player.Potions == PlayerController.MaxPotions, $"hp {d.Player.Hp}, potions {d.Player.Potions}");
            yield return AdvanceToFinal();
            var bk = (BellkeeperEncounter)d.Boss;
            int bossHp0 = d.Boss.Hp;
            bool sawOpening = false; var vk = d.Player.Kit as VanguardKit2;
            float t0 = Time.time;
            var fight = StartCoroutine(Fight(7f, VanguardRotation, 1.6f));
            bool shot = false;
            while (Time.time - t0 < 7f && d.State == RunState.Combat) { KeepAlive(); sawOpening |= vk.OpeningActive; if (Time.time - t0 > 3.5f && !shot) { shot = true; yield return Shot("combat_vanguard_opening"); } yield return null; }
            StopCoroutine(fight); bot.Clear();
            Check("Vanguard strikes damage the boss through the kit", d.Boss.Hp < bossHp0, $"{bossHp0} -> {d.Boss.Hp}");
            Check("Steel Rhythm hit 3 granted Opening", sawOpening);
            yield return VanguardShowcase();
            yield return VanguardKitChecks();
            // potions: drink one mid-fight (heal and lockout), then the Bellkeeper resumes
            yield return Until(() => d.World.Hazards.Count == 0, 6, "quiet for the potion check"); d.Boss.Halt();
            yield return Until(() => d.Player.Stunned == 0, 2, "hit-stun over");   // drinking is blocked while stunned
            d.Player.DebugSetHp(50); bot.potion = true; yield return Wait(0.1f);
            Check("F potion heals +40 and spends one", d.Player.Hp == 90 && d.Player.Potions == PlayerController.MaxPotions - 1, $"hp {d.Player.Hp}, potions {d.Player.Potions}");
            bot.potion = true; yield return Wait(0.1f);
            Check("potion lockout: no second drink within 1 s", d.Player.Potions == PlayerController.MaxPotions - 1);
            d.Boss.DebugSetHpFraction(0.95f); d.Boss.BeginCombat();   // test setup: phase 1 again after the kit checks
            d.Player.DebugSetHp(PlayerController.MaxHp);   // test setup: the rest of this run checks phases, not survival
            Check("Bellkeeper resumes in Last Guardian after the kit checks", d.Boss.PhaseName == BellkeeperEncounter.Phase1, d.Boss.PhaseName);
            Check("Bellkeeper uses its model", bk.Presenter != null && bk.Presenter.HasModel);
            Check("Bellkeeper 2.0 rig has the shell halves and three rings", bk.PhaseRigFound);
            StartCoroutine(Capture("bellkeeper_fight", 300));
            fight = StartCoroutine(Fight(4.5f, VanguardRotation, 1.8f));
            yield return WaitAlive(2.5f); yield return Shot("combat_bellkeeper"); yield return WaitAlive(2.2f);
            StopCoroutine(fight); bot.Clear();
            // The Unfinished Hour (spec v0.3 §3.1, §4.1): phase 1 records one footprint, then replays it alone
            yield return Survive(8, () => bk.HasMemory);
            Check("phase 1 records a footprint and shows its inactive outline", bk.HasMemory);
            Check("Last Guardian look: shell closed, rings compact", bk.ShellAngle < 2 && bk.RingSpread < 0.01f, $"shell {bk.ShellAngle:0}°, rings {bk.RingSpread:0.00} m");
            yield return Shot("bellkeeper_memory_outline");
            d.Player.DebugSetHp(PlayerController.MaxHp);
            yield return Survive(14, () => bk.LiveEcho != null);
            var echo1 = bk.LiveEcho;
            Check("phase 1 replays the memory as an isolated echo", echo1 != null && d.World.Hazards.Count == 1 && !bk.HasMemory,
                $"echo {echo1 != null}, live hazards {d.World.Hazards.Count}");
            Check("the echo warns for 90 ticks", echo1 != null && echo1.ResolveAt == 90 && echo1.LockAt == 0);
            yield return Survive(1.0f); yield return Shot("bellkeeper_unfinished_hour");
            yield return Survive(2, () => bk.EchoesResolved > 0);
            Check("the echo resolves once and the memory is erased", bk.EchoesResolved == 1 && !bk.HasMemory && bk.LiveEcho == null);
            yield return Survive(0.2f);
            Check("the chest opens after the echo", d.Boss.Current == "recovery", d.Boss.Current);
            d.Player.DebugSetHp(PlayerController.MaxHp);
            d.Boss.DebugSetHpFraction(0.69f);
            yield return Survive(16, () => d.Boss.PhaseName == BellkeeperEncounter.Phase2);
            Check("phase 2 queued and entered", d.Boss.PhaseName == BellkeeperEncounter.Phase2);
            yield return null; yield return null;
            Check("Bellkeeper plays PhaseShift on the transition", bk.Presenter.CurrentClip == "PhaseShift", bk.Presenter.CurrentClip);
            yield return Survive(3f); yield return Shot("bellkeeper_phase2");
            Check("Broken Hour look: shell parted, rings offset", bk.ShellAngle >= 10 && bk.RingSpread > 0.15f, $"shell {bk.ShellAngle:0}°, rings {bk.RingSpread:0.00} m");
            d.Boss.DebugSetHpFraction(0.34f);
            d.Player.DebugSetHp(PlayerController.MaxHp);
            yield return Survive(22, () => d.Boss.PhaseName == BellkeeperEncounter.Phase3);
            Check("phase 3 queued and entered", d.Boss.PhaseName == BellkeeperEncounter.Phase3);
            yield return Survive(2.5f); yield return Shot("bellkeeper_final_toll");
            Check("Final Toll look: chest open, rings spread", bk.ShellAngle >= 38 && bk.RingSpread > 0.35f, $"shell {bk.ShellAngle:0}°, rings {bk.RingSpread:0.00} m");
            // spec v0.3 §4.3: the Final Toll remembers its own March and replays it with the paired Bell Sweep
            d.Player.DebugSetHp(PlayerController.MaxHp);
            yield return Survive(6, () => bk.LiveEcho != null);
            var echo3 = bk.LiveEcho;
            Check("the Final Toll replays its March lane", echo3 != null && echo3.Shape == HazardShape.Lane && echo3.Damage == 30);
            Check("the echo stays where the March started, not on the boss",
                echo3 != null && Sim.Flat(echo3.Origin - d.Boss.transform.position).magnitude > 1f,
                echo3 == null ? "" : $"{Sim.Flat(echo3.Origin - d.Boss.transform.position).magnitude:0.0} m");
            yield return Survive(1, () => bk.LastFinalSweep != null);
            var sw = bk.LastFinalSweep;
            Check("the paired Bell Sweep resolves on the same tick as the echo",
                echo3 != null && sw != null && echo3.ResolveAt - echo3.Age == sw.ResolveAt - sw.Age,
                echo3 == null || sw == null ? "" : $"echo {echo3.ResolveAt - echo3.Age}, sweep {sw.ResolveAt - sw.Age}");
            Check("never more than two damaging patterns at once", d.World.Hazards.Count <= 2, d.World.Hazards.Count.ToString());
            yield return Shot("bellkeeper_final_toll_pairing");
            yield return Survive(2, () => bk.LiveEcho == null);
            d.Player.DebugSetHp(PlayerController.MaxHp);
            yield return KillDuringWindup("bellkeeper", "bellkeeper_defeat");
            Check("Bellkeeper plays Defeat", bk.Presenter.CurrentClip == "Defeat", bk.Presenter.CurrentClip);
            Check("defeat erases the memory and every hazard", !bk.HasMemory && bk.LiveEcho == null && d.World.Hazards.Count == 0);
            yield return Wait(1.0f); yield return Shot("bellkeeper_defeat");
            Check("defeat opens the chest fully", bk.ShellAngle > 60, $"shell {bk.ShellAngle:0}°");
            yield return Until(() => d.State == RunState.Results, 6, "results 1");
            Check("victory after all four bosses, Bellkeeper last", d.Victory && d.EncountersCleared == 4 && d.EncounterStarts == 4 && d.EncounterEnds == 4,
                $"victory {d.Victory}, cleared {d.EncountersCleared}, starts {d.EncounterStarts}, ends {d.EncounterEnds}");
            yield return Shot("results_victory");

            // ---------- retry after victory (spam) ----------
            int runs0 = d.RunsStarted;
            for (int i = 0; i < 4; i++) { d.RequestRetry(); d.RequestRetry(); yield return null; }
            yield return Wait(0.5f);
            Check("repeated Retry starts exactly one fresh run", d.RunsStarted == runs0 + 1 && d.State == RunState.Ready && d.Encounter == d.EncounterOrder[0], $"runs +{d.RunsStarted - runs0}, state {d.State}");
            FreshRunChecks("after victory");

            // ---------- run 2: defeat path ----------
            d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 4, "run 2 combat");
            d.Player.DebugSetHp(14);
            bot.Clear();
            // stand in the boss's reach and take real hazards until down
            float tt = 0;
            while (d.State == RunState.Combat && tt < 30) { if (d.Boss) { bot.Aim = d.Boss.transform.position; var to = Sim.Flat(d.Boss.transform.position - d.Player.transform.position); bot.MoveWorld = to.magnitude > 3f ? to.normalized : Vector3.zero; } tt += Time.deltaTime; yield return null; }
            bot.Clear();
            Check("Vanguard plays Down", d.Player.Presenter.CurrentClip == "Down", d.Player.Presenter.CurrentClip);
            Check("player goes down from a real hazard", d.State == RunState.Downed || d.State == RunState.Results, $"state {d.State}, cause {d.DefeatCause}");
            yield return Wait(0.3f); yield return Shot("downed");
            yield return Until(() => d.State == RunState.Results, 4, "results 2");
            Check("defeat result shows the cause", !d.Victory && !string.IsNullOrEmpty(d.DefeatCause), d.DefeatCause);
            Check("no hazards or attacks after defeat", d.World.Hazards.Count == 0 && !d.World.CombatActive);
            yield return Shot("results_defeat");
            runs0 = d.RunsStarted;
            for (int i = 0; i < 3; i++) { d.RequestRetry(); yield return null; }
            Check("retry after defeat starts one fresh run", d.RunsStarted == runs0 + 1 && d.State == RunState.Ready);
            FreshRunChecks("after defeat");

            // ---------- return to menu cleanly (from pause) ----------
            d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 4, "run 3 combat");
            yield return Wait(1.5f);
            d.SetPaused(true); yield return Shot("paused"); d.ReturnToMenu();
            yield return null; yield return null;
            Check("return to menu leaves no run objects", d.State == RunState.Menu && d.World == null && FindObjectsByType<PlayerController>().Length == 0 && FindObjectsByType<BossEncounter>().Length == 0);

            // ---------- run 4: Chimesinger rotation and full victory ----------
            d.StartRun("Chimesinger");
            yield return Wait(0.4f); yield return Shot("ready_chimesinger");
            d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 4, "chimesinger combat");
            yield return AdvanceToFinal();
            yield return ChimesingerChecks();
            chimeStep = 0; d.Player.DebugSetHp(PlayerController.MaxHp); d.Boss.BeginCombat();
            var f2 = StartCoroutine(Fight(4f, ChimeRotation, 6f));
            yield return WaitAlive(4f); StopCoroutine(f2); bot.Clear();
            yield return Shot("chimesinger_bellkeeper");
            yield return TickRegressions();   // ends the fight with a real Resonant Note killing blow
            yield return Until(() => d.State == RunState.Results, 8, "results 4");
            Check("Chimesinger run reaches victory", d.Victory && d.EncountersCleared == 4);
            d.ReturnToMenu(); yield return null;

            // ---------- run 5: Arcanist rotation and full victory ----------
            d.StartRun("Arcanist");
            yield return Wait(0.4f); yield return Shot("ready_arcanist");
            Check("Arcanist uses its model", d.Player.Presenter.HasModel);
            d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 4, "arcanist combat");
            yield return AdvanceToFinal();
            yield return ArcanistChecks();
            if (d.State == RunState.Combat)
            {
                d.Player.DebugSetHp(PlayerController.MaxHp); d.Boss.BeginCombat();
                var f3 = StartCoroutine(Fight(4f, ArcRotation, 6f));
                yield return WaitAlive(4f); StopCoroutine(f3); bot.Clear();
                yield return Shot("arcanist_bellkeeper");
                d.Boss.DebugDefeat();
                yield return Until(() => d.State != RunState.Combat, 2, "arcanist boss defeated");
            }
            yield return Until(() => d.State == RunState.Results, 8, "results 5");
            Check("Arcanist run reaches victory", d.Victory && d.EncountersCleared == 4);
            d.ReturnToMenu(); yield return null;

            // ---------- practice ----------
            d.StartPractice("Vanguard", 0);
            d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 4, "practice combat");
            Check("practice starts the chosen encounter", d.Boss is BellkeeperEncounter && d.Mode == RunMode.Practice);
            yield return Wait(1f);
            d.Boss.DebugDefeat();
            yield return Until(() => d.State == RunState.Results, 6, "practice results");
            Check("practice clear ends in a practice result, not a run victory", d.Mode == RunMode.Practice && d.EncountersCleared == 1);
            d.RequestRetry();
            Check("practice retry repeats the same encounter", d.State == RunState.Ready && d.Encounter == 0 && d.Mode == RunMode.Practice);
            d.ReturnToMenu();

            // ---------- map tour: switch maps after fights have run (FX layers already exist), fight briefly on each ----------
            if (d.State != RunState.Menu) { d.SetPaused(true); d.ReturnToMenu(); }   // the practice retry above leaves it in Ready
            yield return Until(() => d.State == RunState.Menu, 2, "menu before the map tour");
            int startMap = RunMaps.Current;
            for (int step = 1; step <= RunMaps.All.Length; step++)
            {
                int m = (startMap + step) % RunMaps.All.Length; var map = RunMaps.All[m];
                directorLog.AddRange(d.Log);
                d.SelectMap(m);
                yield return Until(() => RunMaps.Current == m && RunDirector.Instance && RunDirector.Instance.State == RunState.Menu, 30, "map loaded: " + map.Name);
                d = RunDirector.Instance; d.InputOverride = bot; bot.Clear();
                yield return Wait(1.5f);
                Check($"map {map.Name}: loads into the menu with its arena", RunMaps.Current == m && d.State == RunState.Menu && HazardSystem.ArenaCount > 0, $"arena scripts {HazardSystem.ArenaCount}");
                yield return Shot("map_" + map.Id + "_menu");
                d.StartPractice("Vanguard", 0); d.RequestReady();
                yield return Until(() => d.State == RunState.Combat, 4, map.Name + " practice combat");
                int hp0 = d.Boss ? d.Boss.Hp : 0;
                var fm = StartCoroutine(Fight(4f, VanguardRotation, 2.2f));
                yield return WaitAlive(4f); StopCoroutine(fm); bot.Clear();
                yield return Shot("map_" + map.Id + "_combat");
                Check($"map {map.Name}: Bellkeeper fight runs (boss takes damage, player stays on the floor)",
                    d.Boss && d.Boss.Hp < hp0 && CombatWorld.InPlayerBound(d.Player.transform.position, 0.01f), $"boss hp {hp0} -> {(d.Boss ? d.Boss.Hp : -1)}");
                bot.MoveWorld = Vector3.right; bot.Sprint = true; yield return Wait(3f); bot.Clear();
                float reach = d.Player.transform.position.x;
                Check($"map {map.Name}: player walks up to the wall (bound {map.Bound} m)", reach >= map.Bound - 0.3f && CombatWorld.InPlayerBound(d.Player.transform.position, 0.01f), $"reached x = {reach:0.00}");
                yield return Shot("map_" + map.Id + "_wall");
                d.Boss.DebugDefeat();
                yield return Until(() => d.State == RunState.Results, 6, map.Name + " practice results");
                d.ReturnToMenu();
            }
            Check("map tour returns to the starting map", RunMaps.Current == startMap);
            // ---------- area bosses (practice only for now) ----------
            yield return AurelionChecks();

            yield return SylvaraChecks();

            // ---------- report ----------
            Check("no errors or exceptions logged", errors == 0, errors + " logged");
            Finish();
        }

        void FreshRunChecks(string when)
        {
            var p = d.Player;
            Check($"fresh run {when}: full health/stamina/potions, encounter 1", p.Hp == PlayerController.MaxHp && p.Potions == PlayerController.MaxPotions && Mathf.Approximately(p.Stamina, PlayerController.MaxStamina) && d.Encounter == d.EncounterOrder[0] && d.EncountersCleared == 0);
            bool cdsClear = true; for (int i = 0; i < p.Kit.SlotNames.Length; i++) cdsClear &= p.Kit.Cooldown01(i) <= 0;
            Check($"fresh run {when}: cooldowns and class state reset", cdsClear && string.IsNullOrEmpty(p.Kit.ClassState()) || (p.Kit is ChimesingerKit ck && ck.Harmony == 0 && !ck.Bell));
            Check($"fresh run {when}: no stale bosses, hazards or projectiles", FindObjectsByType<BossEncounter>().Length == 0 && d.World.Hazards.Count == 0 && d.World.Projectiles.Count == 0 && d.Boss == null);
            Check($"fresh run {when}: one player only", FindObjectsByType<PlayerController>().Length == 1);
        }

        // Difficulty row (Easy / Medium / Hard): menu-only selection, copied into the run, kept on retry, and scaling the
        // damage a real hit deals once. Leaves the selection on Medium for the rest of the test.
        IEnumerator DifficultyChecks()
        {
            Check("difficulty starts at Medium in the autotest", RunDifficulty.Selected == Difficulty.Medium);
            Check("difficulty damage scales 0.6 / 1 / 1.3",
                  Mathf.Approximately(RunDifficulty.DamageTakenScale(Difficulty.Easy), 0.6f) && Mathf.Approximately(RunDifficulty.DamageTakenScale(Difficulty.Medium), 1f)
                  && Mathf.Approximately(RunDifficulty.DamageTakenScale(Difficulty.Hard), 1.3f));
            Check("difficulty can be chosen in the menu", d.SelectDifficulty(Difficulty.Easy) && RunDifficulty.Selected == Difficulty.Easy);
            yield return Shot("menu_difficulty_easy");
            foreach (var (level, expect) in new[] { (Difficulty.Easy, 30), (Difficulty.Medium, 50), (Difficulty.Hard, 65) })
            {
                d.SelectDifficulty(level);
                d.StartPractice("Vanguard", 0);
                yield return Wait(0.3f);
                string n = RunDifficulty.Name(level);
                Check($"{n}: practice copies the menu difficulty", d.Difficulty == level && d.World != null && d.World.Difficulty == level);
                Check($"{n}: difficulty can't change outside the menu", !d.SelectDifficulty(level == Difficulty.Hard ? Difficulty.Easy : Difficulty.Hard) && d.Difficulty == level);
                var p = d.Player; int hp0 = p.Hp;
                p.ReceiveHit(new Hazard { Source = "Difficulty probe", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Radius = 1, Origin = p.transform.position, Damage = 50, ResolveAt = 1 });
                Check($"{n}: a 50-damage boss hit deals {expect}", hp0 - p.Hp == expect, $"{hp0} -> {p.Hp}");
                d.SetPaused(true); d.RequestRetry(); yield return null;
                Check($"{n}: retry keeps the level", d.Difficulty == level && d.World.Difficulty == level);
                d.SetPaused(true); d.ReturnToMenu(); yield return null;
            }
            d.SelectDifficulty(Difficulty.Medium);
            Check("difficulty back on Medium for the rest of the test", d.State == RunState.Menu && RunDifficulty.Selected == Difficulty.Medium);
        }

        // One readable rotation on camera: Steel Rhythm x3 -> empowered Sundering Blow -> Brace -> Last Word.
        IEnumerator VanguardShowcase()
        {
            var p = d.Player; var seen = new HashSet<string>();
            yield return Until(() => d.World.Hazards.Count == 0, 6, "quiet before showcase");
            d.Boss.Halt(); p.DebugSetHp(PlayerController.MaxHp);
            yield return Survive(9f, () => p.Kit.Cooldown01(1) <= 0 && p.Kit.Cooldown01(2) <= 0);   // fresh cooldowns for the showcase
            yield return Until(() => p.Kit.Cooldown01(1) <= 0, 1, "Sundering Blow ready for the showcase");
            for (float w = 0; w < 3f; w += Time.deltaTime)   // walk back into reach
            {
                var to0 = Sim.Flat(d.Boss.transform.position - p.transform.position);
                if (to0.magnitude - d.Boss.BodyRadius < 1.8f) break;
                bot.Aim = d.Boss.transform.position; bot.MoveWorld = to0.normalized; yield return null;
            }
            StartCoroutine(Capture("vanguard_kit", 330));
            float t = 0;
            while (t < 5.4f)
            {
                var b = d.Boss; var to = Sim.Flat(b.transform.position - p.transform.position);
                bot.Aim = b.transform.position; bot.MoveWorld = to.magnitude - b.BodyRadius > 1.8f ? to.normalized : Vector3.zero;
                if (t < 1.25f) bot.press[0] = true;
                else if (t < 1.4f) bot.press[1] = true;                 // buffered into the end of the third hit
                else if (t > 2.2f && t < 3.0f) { bot.hold[3] = true; bot.press[3] = t < 2.25f; }
                else if (t > 3.3f && t < 3.5f) { bot.hold[3] = false; bot.press[2] = true; }
                seen.Add(p.Presenter.CurrentClip);
                t += Time.deltaTime; yield return null;
            }
            bot.Clear();
            Note("clips seen: " + string.Join(", ", seen));
            foreach (var c in new[] { "SteelRhythm1", "SteelRhythm2", "SteelRhythm3", "SunderingBlow", "LastWord" })
                Check("Vanguard plays " + c, seen.Contains(c));
            Check("Vanguard plays the Brace set", seen.Contains("BraceRaise") || seen.Contains("BraceHold") || seen.Contains("BraceWalk"));
            Check("Vanguard approved-kit clips need no placeholder", !p.Presenter.Placeholders.Any(x => x != "Land"), string.Join(",", p.Presenter.Placeholders));
            d.Boss.BeginCombat();
        }

        IEnumerator VanguardKitChecks()
        {
            d.Player.DebugSetHp(PlayerController.MaxHp);
            var p = d.Player; var k = (VanguardKit2)p.Kit; var w = d.World;
            yield return Until(() => w.Hazards.Count == 0, 6, "a quiet moment");
            d.Boss.Halt();   // test setup: stop the boss so the checks only see test hazards
            yield return Wait(1.6f);
            // dash cancels an unreleased Sundering Blow: no cooldown spent
            yield return Until(() => k.Cooldown01(1) <= 0, 9, "Sundering Blow ready");
            bot.press[1] = true; yield return Wait(0.12f); bot.dash = true; yield return Wait(0.4f);
            Check("dash before release cancels Sundering Blow without cooldown", k.Cooldown01(1) <= 0);
            // Opening -> empowered Sundering Blow takes 2 s off Last Word
            bot.Aim = d.Boss.transform.position;
            bot.press[2] = true; yield return Wait(1.6f);   // Last Word released, 45 s cooldown running
            float ultBefore = k.UltRemaining;
            int guard = 0;
            while (!k.OpeningActive && guard++ < 400) { bot.Aim = d.Boss.transform.position; var to = Sim.Flat(d.Boss.transform.position - p.transform.position); bot.MoveWorld = to.magnitude > 3f ? to.normalized : Vector3.zero; bot.press[0] = true; yield return null; }
            bot.Clear(); yield return Wait(0.6f);
            float before = k.UltRemaining;
            bot.Aim = d.Boss.transform.position; bot.press[1] = true; yield return Wait(0.6f);
            Check("Last Word cooldown is running", ultBefore > 5, ultBefore.ToString("0.0") + " s left");
            Check("empowered Sundering Blow consumes Opening and cuts Last Word by 2 s", !k.OpeningActive && before - k.UltRemaining > 2.3f && before - k.UltRemaining < 2.9f, $"ult {before:0.00} -> {k.UltRemaining:0.00}");
            // Brace: frontal strike reduced 60 %, no knockback; rear strike full
            yield return Until(() => k.Cooldown01(3) <= 0, 13, "Brace ready");
            var front = p.transform.position + p.transform.forward * 3;
            bot.Aim = front; bot.hold[3] = true; bot.press[3] = true; yield return Wait(0.25f);
            int hp0 = p.Hp; var pos0 = p.transform.position;
            w.Hazards.Spawn(new Hazard { Source = "Test strike (front)", Shape = HazardShape.Circle, Kind = HazardKind.Strike, Origin = front, Radius = 3.2f, ResolveAt = 6, Damage = 20 });
            yield return Wait(0.3f);
            Check("Brace: frontal strike reduced by 60 %", hp0 - p.Hp == 8, $"took {hp0 - p.Hp} of 20");
            Check("Brace: no knockback from a mitigated hit", Sim.Flat(p.transform.position - pos0).magnitude < 0.3f);
            Check("Brace absorption grants Opening", k.OpeningActive);
            hp0 = p.Hp;
            w.Hazards.Spawn(new Hazard { Source = "Test strike (rear)", Shape = HazardShape.Circle, Kind = HazardKind.Strike, Origin = p.transform.position - p.transform.forward * 3, Radius = 3.2f, ResolveAt = 6, Damage = 20 });
            yield return Wait(0.3f);
            Check("Brace: rear strike bypasses", hp0 - p.Hp == 20, $"took {hp0 - p.Hp}");
            bot.Clear(); yield return Wait(0.4f);
            hp0 = p.Hp;
            w.Hazards.Spawn(new Hazard { Source = "Test eruption", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = p.transform.position, Radius = 1.5f, ResolveAt = 6, Damage = 10 });
            var probe = w.Hazards.Live.Last();
            yield return Wait(0.3f);
            Check("one hazard instance hits at most once", hp0 - p.Hp == 10);
            p.DebugSetHp(PlayerController.MaxHp);
            d.Boss.BeginCombat();
        }

        static int ArcRotation(int f) => f % 150 == 10 ? 1 : f % 150 == 100 ? 2 : f % 300 == 140 ? 3 : f % 10 == 0 ? 0 : -1;

        // #41 sequence: Seal A -> three Clockbolts -> empowered Lance -> Seal B -> three Clockbolts -> empowered Lance ->
        // Convergence in the shared ripe window. Plus early detonation, Tick cap, dash-cancelled Lance and the Anchor.
        IEnumerator ArcanistChecks()
        {
            var p = d.Player; var k = (ArcanistKit)p.Kit; var w = d.World;
            d.Boss.Halt(); yield return Wait(0.3f); w.ClearTransient();
            p.transform.position = d.Boss.transform.position + new Vector3(0, 0, 6f);
            bot.Aim = d.Boss.transform.position;
            // Tick cap
            for (int i = 0; i < 5; i++) { bot.press[0] = true; yield return Wait(0.36f); }
            Check("Clockbolt builds Ticks and caps at 3", k.Ticks == 3, k.ClassState());
            // dash cancels an unfinished Lance: nothing spent, Ticks kept
            bot.press[2] = true; yield return Wait(0.08f); bot.dash = true; bot.MoveWorld = Vector3.right; yield return Wait(0.3f); bot.Clear();
            Check("Clockstep cancels an unfinished Arc Lance and keeps Ticks", k.Ticks == 3 && k.Cooldown01(2) <= 0, k.ClassState());
            p.transform.position = d.Boss.transform.position + new Vector3(0, 0, 6f); bot.Aim = d.Boss.transform.position;
            // early detonation: one fresh seal, no inscription
            bot.press[1] = true; yield return Wait(0.4f);
            Check("Time Seal places a seal and spends a charge", k.Seals.Count == 1 && k.Charges == 1, k.ClassState());
            bot.press[3] = true; yield return Wait(0.5f);
            int early = k.LastConvergence;
            Check("early Convergence detonates and clears seals", early > 0 && k.Seals.Count == 0, "damage " + early);
            yield return Until(() => k.Cooldown01(3) <= 0 && k.Charges == 2, 7, "Convergence and both seal charges ready");
            // full sequence
            StartCoroutine(Capture("arcanist_rotation", 480));
            bot.Aim = d.Boss.transform.position;
            float seq0 = Time.time;
            bot.press[1] = true; yield return Wait(0.25f);                                      // Seal A
            for (int i = 0; i < 3; i++) { bot.press[0] = true; yield return Wait(0.27f); }      // three Clockbolts
            yield return Until(() => k.Ticks == 3, 0.4f, "3 Ticks after three Clockbolts");
            Check("three Clockbolts reach 3 Ticks", k.Ticks == 3, k.ClassState());
            bot.press[2] = true; yield return Wait(0.38f);                                      // empowered Lance
            Check("empowered Arc Lance spends Ticks and inscribes the oldest seal", k.LastLanceEmpowered && k.Ticks == 0 && k.Seals.Count == 1 && k.Seals[0].Inscribed, k.ClassState());
            bot.press[1] = true; yield return Wait(0.25f);                                      // Seal B, < 2 s after A
            Note($"Seal A -> Seal B spacing: {(Time.time - seq0) - 0.25f:0.00} s (must be < 2 s for a shared ripe window)");
            Check("Seal B is a separate seal on the same target", k.Seals.Count == 2 && !k.Seals[1].Inscribed, k.ClassState());
            for (int i = 0; i < 3; i++) { bot.press[0] = true; yield return Wait(0.27f); }
            bot.press[2] = true; yield return Wait(0.38f);
            Check("second empowered Lance inscribes seal B", k.Seals.Count == 2 && k.Seals[0].Inscribed && k.Seals[1].Inscribed, k.ClassState());
            yield return Until(() => k.Seals.Count == 2 && k.Seals[0].Ripe && k.Seals[1].Ripe, 5, "both seals ripe together");
            Check("the two-seal schedule fits inside the shared ripe window", k.Seals.Count == 2 && k.Seals[1].Ripe, k.ClassState());
            yield return Shot("arcanist_two_ripe_seals");
            int bossHp = d.Boss.Hp;
            bot.press[3] = true; yield return Wait(0.5f);
            int full = k.LastConvergence;
            Check("perfect Convergence out-damages early detonation", full > early * 4, $"perfect {full} vs early {early}");
            Check("Convergence resolves exactly once", bossHp - d.Boss.Hp == full, $"boss lost {bossHp - d.Boss.Hp}, convergence {full}");
            // Temporal Anchor: place, move, recast -> back at the anchor with a shield that absorbs a hit
            yield return Wait(0.3f);
            var anchorAt = p.transform.position;
            bot.press[4] = true; yield return Wait(0.35f);
            Check("Temporal Anchor records the position", k.AnchorSet);
            bot.MoveWorld = Vector3.left; yield return Wait(0.6f); bot.Clear();
            bot.press[4] = true; yield return Wait(0.25f);
            Check("recast returns to the anchor", Sim.Flat(p.transform.position - anchorAt).magnitude < 0.5f, $"{Sim.Flat(p.transform.position - anchorAt).magnitude:0.00} m away");
            int hp0 = p.Hp;
            w.Hazards.Spawn(new Hazard { Source = "Test strike", Shape = HazardShape.Circle, Kind = HazardKind.Strike, Origin = p.transform.position, Radius = 1.5f, ResolveAt = 6, Damage = 20 });
            yield return Wait(0.3f);
            Check("Anchor shield absorbs the next hit", p.Hp == hp0 && k.ShieldHp == 10, $"hp {hp0} -> {p.Hp}, shield {k.ShieldHp}");
            p.DebugSetHp(PlayerController.MaxHp);
            d.Boss.BeginCombat();
        }

        IEnumerator ChimesingerChecks()
        {
            var p = d.Player; var k = (ChimesingerKit)p.Kit; var w = d.World;
            d.Boss.Halt(); yield return Wait(0.3f);
            w.ClearTransient();
            p.transform.position = d.Boss.transform.position + new Vector3(0, 0, 5f);
            bot.Aim = d.Boss.transform.position;
            // early detonation: Bellstrike then Crescendo
            bot.press[1] = true; yield return Wait(0.5f);
            Check("Bellstrike rings a bell on the boss", k.Bell, k.ClassState());
            Check("Bellstrike spends one of two charges", k.Charges == 1);
            bot.press[3] = true; yield return Wait(0.6f);
            int early = k.LastCrescendo;
            Check("early Crescendo detonates and clears the bell", early > 0 && !k.Bell, "damage " + early);
            yield return Wait(5.2f);
            StartCoroutine(Capture("chimesinger_rotation", 540));
            // full sequence: Bellstrike -> basics -> Refrain -> Bellstrike (refresh) -> basics -> Refrain -> Crescendo
            bot.Aim = d.Boss.transform.position;
            bot.press[1] = true; yield return Wait(0.45f);
            for (int i = 0; i < 3; i++) { bot.press[0] = true; yield return Wait(0.45f); }
            bot.press[2] = true; yield return Wait(0.5f);
            Check("Refrain on a ringing bell stores an echo", k.Echoes == 1, k.ClassState());
            // heal and dash mid-rotation must not clear setup
            p.DebugSetHp(50);
            bot.press[4] = true; yield return Wait(0.1f);
            Check("Mending Chorus heals immediately and includes the caster", p.Hp == 80, "hp " + p.Hp);
            Check("healing keeps Harmony, bell and echoes", k.Bell && k.Echoes == 1 && k.Harmony >= 2, k.ClassState());
            bot.dash = true; bot.MoveWorld = Vector3.right; yield return Wait(0.3f); bot.Clear();
            Check("Harmonic Step keeps Harmony, bell and echoes", k.Bell && k.Echoes == 1 && k.Harmony >= 2, k.ClassState());
            bot.Aim = d.Boss.transform.position;
            yield return Until(() => k.Charges > 0, 7, "Bellstrike charge");
            bot.press[1] = true; yield return Wait(0.45f);
            Check("Bellstrike on an existing bell refreshes and strengthens it", k.Bell && k.Strength == 1, k.ClassState());
            for (int i = 0; i < 3; i++) { bot.press[0] = true; yield return Wait(0.45f); }
            yield return Until(() => p.Kit.Cooldown01(2) <= 0, 5, "Refrain ready");
            bot.Aim = d.Boss.transform.position; bot.press[2] = true; yield return Wait(0.5f);
            Check("second Refrain fills the two-echo cap", k.Echoes == 2, k.ClassState());
            bot.press[2] = true; yield return Wait(0.2f);
            yield return Shot("chimesinger_full_setup");
            yield return Until(() => p.Kit.Cooldown01(3) <= 0, 6, "Crescendo ready");
            int bossHp = d.Boss.Hp;
            bot.Aim = d.Boss.transform.position; bot.press[3] = true; yield return Wait(0.6f);
            int full = k.LastCrescendo;
            Check("full-sequence Crescendo out-damages early detonation", full > early * 2, $"full {full} vs early {early}");
            Check("Crescendo resolves exactly once", bossHp - d.Boss.Hp == full, $"boss lost {bossHp - d.Boss.Hp}, crescendo {full}");
            Check("Crescendo spends Harmony and clears the bell", k.Harmony == 0 && !k.Bell);
            p.DebugSetHp(PlayerController.MaxHp);
            d.Boss.BeginCombat();
        }

        // ------------------------------------------------------------ Sylvara (graybox, docs/design/sylvara.md v0.1)
        static bool OnlySylvara => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellSylvara", StringComparison.OrdinalIgnoreCase));

        IEnumerator SylvaraChecks()
        {
            // the practice section above leaves the director in Ready, where ReturnToMenu only works from the pause menu
            if (d.State != RunState.Menu) { d.SetPaused(true); d.ReturnToMenu(); yield return null; }
            int verdant = RunMaps.IndexOf(RunDirector.SylvaraMap);
            Check("Sylvara practice accepted from the menu", d.StartPractice("Vanguard", RunDirector.SylvaraPractice));
            yield return Until(() => RunMaps.Current == verdant && RunDirector.Instance && RunDirector.Instance.State == RunState.Ready, 30, "Verdant Chimes loaded for Sylvara");
            if (RunDirector.Instance != d) { directorLog.AddRange(d.Log); d = RunDirector.Instance; d.InputOverride = bot; bot.Clear(); }
            if (d.Player) d.Player.Input = bot;   // the new scene built the player before the bot was attached
            Check("Sylvara practice loads the Verdant Chimes arena", RunMaps.Current == verdant && HazardSystem.ArenaCount > 0, RunMaps.CurrentName);
            Check("Sylvara practice keeps the chosen class across the map load", d.ClassName == "Vanguard" && d.Mode == RunMode.Practice && d.Encounter == RunDirector.SylvaraPractice);
            d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 4, "Sylvara practice combat");
            var sy = d.Boss as SylvaraEncounter;
            Check("Sylvara practice starts her encounter", sy != null && d.Mode == RunMode.Practice);
            if (sy == null) yield break;
            Check("Sylvara: full health and potions (area final boss)", d.Player.Hp == PlayerController.MaxHp && d.Player.Potions == PlayerController.MaxPotions);
            var syPres = sy.Presenter; var syClips = new[] { "Idle", "Walk", "PruningArc", "Rootscript", "Seed", "Procession", "Bloom", "Vulnerable", "PhaseShift", "Defeat" };
            Check("Sylvara uses her rigged model with all clips", syPres && syPres.HasModel && !sy.Placeholder && syClips.All(c => syPres.Has(c)), syPres ? string.Join(",", syClips.Where(c => !syPres.Has(c))) : "no presenter");
            d.Player.transform.position = new Vector3(0, 0, 3.2f); yield return Wait(0.4f);   // frame the body for the shot
            yield return Shot("sylvara_body");
            StartCoroutine(Capture("sylvara_arc", 210));   // the first Pruning Arc on the model

            // watch the whole fight: hits per instance, hit rules, patterns live at once
            var hitsPerInstance = new Dictionary<int, int>(); bool hitRules = true; int maxPatterns = 0, taken = 0;
            Action<PlayerController, Hazard, int> onHit = (p, h, dmg) =>
            {
                hitsPerInstance[h.Id] = hitsPerInstance.TryGetValue(h.Id, out int n) ? n + 1 : 1;
                hitRules &= h.Knockback == 0 && h.Stun == 9; taken += dmg;
            };
            d.World.PlayerHit += onHit;
            bool watching = true;
            IEnumerator Watch()
            {
                while (watching && d.World != null)
                {
                    int patterns = d.World.Hazards.Live.Select(h => h.Source.StartsWith("Threefold Bloom") ? "Threefold Bloom" : h.Source).Distinct().Count();
                    maxPatterns = Mathf.Max(maxPatterns, patterns);
                    if (d.Player && d.Player.Hp < 45) d.Player.DebugSetHp(PlayerController.MaxHp);   // keep the schedule running; damage is noted
                    yield return null;
                }
            }
            StartCoroutine(Watch());

            // Phase 1: every attack once, and one full Bloom
            bool bloomShot = false, rootShot = false;
            float t0 = Time.time;
            while (Time.time - t0 < 40f && d.State == RunState.Combat && !(sy.Order.Distinct().Count() >= 5 && sy.BloomsCompleted >= 1))
            {
                var sv = StartCoroutine(Survive(0.25f)); yield return Wait(0.25f); StopCoroutine(sv);
                if (!rootShot && d.World.Hazards.Live.Count(h => h.Source == "Rootscript") >= 2) { rootShot = true; yield return Shot("sylvara_rootscript"); }
                if (!bloomShot && d.World.Hazards.Live.Count(h => h.Source.StartsWith("Threefold Bloom")) >= 2) { bloomShot = true; yield return Shot("sylvara_bloom"); StartCoroutine(Capture("sylvara_bloom", 240)); }
            }
            bot.Clear();
            Note("Sylvara phase 1 order: " + string.Join(" → ", sy.Order));
            foreach (var id in new[] { "Pruning Arc", "Rootscript", "Seed of the Refrain", "Briar Procession", "Threefold Bloom" })
                Check($"Sylvara phase 1 uses {id}", sy.Order.Contains(id));
            var log = sy.BloomLog.Take(3).ToList();
            bool ordered = log.Count == 3 && log[0].index == 0 && log[1].index == 1 && log[2].index == 2;
            bool timing = ordered && log.All(e => e.spentTick - e.resolveTick == 17) && log[1].resolveTick - log[0].resolveTick == 72 && log[2].resolveTick - log[1].resolveTick == 72;
            Check("Threefold Bloom resolves I → II → III", ordered, string.Join(", ", log.Select(e => e.index + 1)));
            Check("Bloom flowers: 72-tick stagger, 18-tick active period (resolve tick + 17)", timing, string.Join(" | ", log.Select(e => $"{e.resolveTick}->{e.spentTick}")));

            // stand in flower II through its whole active period: exactly one 13 HP hit, no knockback (test setup moves
            // the player; the hit comes from the real hazard). Then walk into flower III during its active period: hit once.
            yield return Until(() => d.World.Hazards.Live.Any(h => h.Source == "Threefold Bloom II" && h.ResolveAt - h.Age <= 4), 40, "next Bloom flower II about to open");
            var f2 = d.World.Hazards.Live.FirstOrDefault(h => h.Source == "Threefold Bloom II");
            if (f2 != null)
            {
                d.Player.DebugSetHp(PlayerController.MaxHp); bot.Clear();
                d.Player.transform.position = f2.Origin; var at = f2.Origin;
                int hits0 = hitsPerInstance.Count;
                while (d.World.Hazards.Live.Contains(f2)) { bot.Clear(); yield return null; }
                yield return Wait(0.1f);
                Check("standing in a flower for its whole active period: hit exactly once for 13", hitsPerInstance.TryGetValue(f2.Id, out int n2) && n2 == 1 && d.Player.Hp == PlayerController.MaxHp - 13, $"hits {n2}, hp {d.Player.Hp}");
                Check("no knockback: the player is still where the flower hit them", Sim.Flat(d.Player.transform.position - at).magnitude < 0.05f);
                var f3 = d.World.Hazards.Live.FirstOrDefault(h => h.Source == "Threefold Bloom III");
                if (f3 != null)
                {
                    yield return Until(() => f3.Age >= f3.ResolveAt + 6, 4, "flower III active");
                    d.Player.DebugSetHp(PlayerController.MaxHp);
                    d.Player.transform.position = f3.Origin;   // entering mid-activation
                    while (d.World.Hazards.Live.Contains(f3)) yield return null;
                    yield return Wait(0.1f);
                    Check("entering a flower during its active period: hit once", hitsPerInstance.TryGetValue(f3.Id, out int n3) && n3 == 1, $"hits {n3}");
                    yield return Wait(0.5f);
                    d.Player.DebugSetHp(PlayerController.MaxHp);
                    Check("a spent flower is safe: no further hits", hitsPerInstance.Count == hits0 + 2);
                }
            }

            // phase transitions are queued to block boundaries
            d.Boss.DebugSetHpFraction(0.64f);
            yield return Until(() => sy.PhaseName == "Awakening Bloom", 25, "Sylvara phase 2");
            Check("Sylvara enters Awakening Bloom below 65 %", sy.PhaseName == "Awakening Bloom");
            yield return Wait(2f); yield return Shot("sylvara_phase2");
            d.Boss.DebugSetHpFraction(0.29f);
            float t1 = Time.time;
            while (sy.PhaseName != "Hollow Refrain" && Time.time - t1 < 40f && d.State == RunState.Combat)
            { var sv = StartCoroutine(Survive(0.25f)); yield return Wait(0.25f); StopCoroutine(sv); }
            Check("Sylvara enters Hollow Refrain below 30 %", sy.PhaseName == "Hollow Refrain");
            yield return Wait(2f); yield return Shot("sylvara_phase3");

            watching = false; d.World.PlayerHit -= onHit;
            Check("Sylvara: each hazard instance hits at most once (R16)", hitsPerInstance.Values.All(n => n == 1), $"{hitsPerInstance.Count} instances hit");
            Check("Sylvara: hits stun 0.15 s with no knockback (D6)", hitRules);
            Check("Sylvara: never more than one damaging pattern live (Steady Ticking)", maxPatterns <= 1, $"max {maxPatterns}");
            Note($"Sylvara: bot took {taken} damage over the fight ({hitsPerInstance.Count} hits), healed by the test to keep the schedule running");

            yield return KillDuringWindup("sylvara");
            yield return Until(() => d.State == RunState.Results, 8, "Sylvara practice results");
            Check("Sylvara practice clear ends in a practice result", d.Victory && d.Mode == RunMode.Practice);
            Check("Sylvara: no Bloom props left after defeat", sy.PropCount == 0, sy.PropCount + " left");
            d.ReturnToMenu(); yield return null;
        }

        // Exact-tick regressions from the foundation review of PR #42 (risxhb/codex, Oct 21). The director is paused
        // and the test steps the world itself, so every count below is in authoritative combat ticks.
        bool StepUntil(Func<bool> cond, int max) { for (int i = 0; i < max && !cond(); i++) d.World.Step(); return cond(); }
        void StepTicks(int n) { for (int i = 0; i < n; i++) d.World.Step(); }

        IEnumerator TickRegressions()
        {
            var p = d.Player; var k = (ChimesingerKit)p.Kit; var w = d.World;
            d.Boss.Halt(); yield return Until(() => p.Stunned == 0 && !p.Dashing, 2, "player free for the tick regressions");
            d.SetPaused(true); bot.Clear();
            w.ClearTransient(); p.DebugSetHp(PlayerController.MaxHp);
            p.transform.position = d.Boss.transform.position + new Vector3(0, 0, 5f);
            bot.Aim = d.Boss.transform.position;

            // dash: 3 m over exactly 12 ticks, the activation tick included
            StepTicks(120);   // stamina and dash lock recovered
            var pos0 = p.transform.position;
            bot.MoveWorld = Vector3.right; bot.dash = true; d.World.Step(); bot.MoveWorld = Vector3.zero;
            int dashTicks = 1; while (p.Dashing && dashTicks < 40) { d.World.Step(); dashTicks++; }
            float dashDist = Sim.Flat(p.transform.position - pos0).magnitude;
            Check("dash moves 3 m over 12 ticks", dashTicks == PlayerController.DashTicks && Mathf.Abs(dashDist - PlayerController.DashDistance) < 0.01f, $"{dashDist:0.000} m in {dashTicks} ticks");

            // capped bell: refreshes past strength 2 must not keep multiplying stored echoes
            p.transform.position = d.Boss.transform.position + new Vector3(0, 0, 5f);
            StepUntil(() => k.Charges == 2 && k.Cooldown01(2) <= 0, 900);
            bot.press[1] = true; StepTicks(40);                                  // Bellstrike: bell rung
            bot.press[2] = true; StepTicks(45);                                  // Refrain: one echo at strength 0
            int echo0 = k.EchoTotal;
            bot.press[1] = true; StepTicks(40);                                  // refresh: strength 1
            int echo1 = k.EchoTotal;
            StepUntil(() => k.Charges > 0, 400); bot.press[1] = true; StepTicks(40);   // refresh: strength 2
            int echo2 = k.EchoTotal, strength2 = k.Strength;
            StepUntil(() => k.Charges > 0, 400); bot.press[1] = true; StepTicks(40);   // refresh at the cap
            int echo3 = k.EchoTotal;
            Check("bell strengthens up to twice", k.Bell && strength2 == 2 && k.Strength == 2 && echo0 > 0 && echo1 > echo0 && echo2 > echo1, k.ClassState());
            Check("refreshing a capped bell leaves stored echoes unchanged", echo3 == echo2, $"echo {echo0} -> {echo1} -> {echo2} -> {echo3}");
            w.ClearTransient();

            // input buffer: a press buffered during an action expires on absolute ticks, so it can't outlive a stun
            StepUntil(() => k.Charges == 2 && k.Cooldown01(2) <= 0, 900);
            bot.press[2] = true; d.World.Step();                                 // Refrain cast running
            bot.press[1] = true; d.World.Step();                                 // Bellstrike buffered behind it
            w.Hazards.Spawn(new Hazard { Source = "Test stun", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = p.transform.position, Radius = 1.5f, ResolveAt = 2, Damage = 5 });
            StepUntil(() => p.Stunned > 0, 6);
            int stunTicks = 0; while (p.Stunned > 0 && stunTicks < 60) { d.World.Step(); stunTicks++; }
            int charges0 = k.Charges; StepTicks(40);
            Check("buffered press expires during stun instead of firing on recovery", stunTicks > 9 && k.Charges == charges0 && charges0 == 2, $"stunned {stunTicks} ticks, charges {charges0} -> {k.Charges}");
            p.DebugSetHp(PlayerController.MaxHp);

            // a real projectile killing blow: no exception, and pending hazards and notes are cancelled in that tick
            p.transform.position = d.Boss.transform.position + new Vector3(0, 0, 5f);
            w.Hazards.Spawn(new Hazard { Source = "Test pending", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = d.Boss.transform.position + new Vector3(0, 0, -6f), Radius = 1f, ResolveAt = 600, Damage = 10 });
            d.Boss.DebugSetHpFraction(0);   // 1 HP left
            int resolved0 = w.Hazards.Resolved, inFlight = 0; string thrown = null;
            bot.hold[0] = true;
            try
            {
                for (int i = 0; i < 180 && !d.Boss.Dead; i++) { bot.press[0] = true; d.World.Step(); inFlight = Mathf.Max(inFlight, w.Projectiles.Count); }
            }
            catch (Exception e) { thrown = e.GetType().Name + ": " + e.Message; }
            bot.Clear();
            Check("Resonant Note killing blow kills the boss without an exception", thrown == null && d.Boss.Dead, thrown ?? $"dead {d.Boss.Dead}");
            Check("projectile kill cancels pending hazards and notes in the same tick", w.Hazards.Count == 0 && w.Projectiles.Count == 0 && w.Hazards.Resolved == resolved0, $"hazards {w.Hazards.Count}, notes {w.Projectiles.Count} (max in flight {inFlight})");
            d.SetPaused(false);
            yield return Until(() => d.State != RunState.Combat, 2, "projectile kill ends encounter 1");
        }

        void Finish()
        {
            lines.Insert(0, $"RESULT: {(fail == 0 ? "ALL PASSED" : "FAILURES")} · {pass} passed, {fail} failed · errors logged: {errors}");
            directorLog.AddRange(d.Log);
            lines.Add(""); lines.Add("Director transitions:"); lines.AddRange(directorLog.Select(l => "  " + l));
            if (errorLines.Count > 0) { lines.Add(""); lines.Add("Errors:"); lines.AddRange(errorLines); }
            File.WriteAllLines(Path.Combine(dir, "autotest-report.txt"), lines);
            Debug.Log($"[autotest] done: {pass} passed, {fail} failed");
            StartCoroutine(Quit());
        }
        IEnumerator Quit()
        {
            yield return Wait(0.5f);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            UnityEditor.EditorApplication.Exit(fail == 0 ? 0 : 1);   // launched from the command line for the Play Mode check
#else
            Application.Quit(fail == 0 ? 0 : 1);
#endif
        }
    }
}
