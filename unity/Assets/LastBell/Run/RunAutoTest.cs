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
            public bool[] press = new bool[5]; public bool[] hold = new bool[5]; public bool dash, potion;
            public PlayerInput Read(PlayerController who)
            {
                var r = new PlayerInput { Aim = Aim, Sprint = Sprint, Dash = dash, Potion = potion,
                    P0 = press[0], P1 = press[1], P2 = press[2], P3 = press[3], P4 = press[4],
                    H0 = hold[0] || press[0], H1 = hold[1], H2 = hold[2], H3 = hold[3] || press[3], H4 = hold[4] };
                var cam = Camera.main; var fwd = cam ? Sim.Flat(cam.transform.forward).normalized : Vector3.forward;
                var right = Vector3.Cross(Vector3.up, fwd);
                r.Move = Vector2.ClampMagnitude(new Vector2(Vector3.Dot(MoveWorld, right), Vector3.Dot(MoveWorld, fwd)), 1);
                for (int i = 0; i < 5; i++) press[i] = false; dash = potion = false;
                return r;
            }
            public void Clear() { MoveWorld = Vector3.zero; Sprint = false; for (int i = 0; i < 5; i++) { press[i] = hold[i] = false; } dash = false; }
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

        // -batchmode renders nothing, so WaitForEndOfFrame never fires there: picture steps are skipped and the checks go on
        static bool NoFrames => Application.isBatchMode;
        IEnumerator Shot(string name)
        {
            if (NoFrames) { Note("screenshot " + name + " skipped (batchmode)"); yield return null; yield break; }
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
            if (NoFrames) { Note($"video frames {name}: skipped (batchmode)"); yield break; }
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
            int pending = d.World.Hazards.Count;
            string names = string.Join(", ", d.World.Hazards.Live.Select(h => h.Source).Distinct());
            yield return Shot(label + "_windup");
            if (capture != null) StartCoroutine(Capture(capture, 200));
            // read just before the kill: hits during the screenshot frames (a bullet hell keeps firing) are not after death
            int hp0 = d.Player.Hp, resolved0 = d.World.Hazards.Resolved;
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
            if (SmokeOnly) { yield return SmokeChecks(); Check("no errors or exceptions logged", errors == 0, errors + " logged"); Finish(); yield break; }
            if (PartyOnly) { yield return PartyChecks(); yield return ReviveChecks(); Check("no errors or exceptions logged", errors == 0, errors + " logged"); Finish(); yield break; }
            if (AurelionOnly) { yield return AurelionChecks(); Check("no errors or exceptions logged", errors == 0, errors + " logged"); Finish(); yield break; }
            // -lastbellArcanistOnly: just the Arcanist run and its kit checks (skips the menu, map, other-class and boss suites)
            if (ArcanistOnly) { yield return ArcanistRun(); Check("no errors or exceptions logged", errors == 0, errors + " logged"); Finish(); yield break; }
            if (SoloSweepOn) { yield return SoloSweep(); Check("no errors or exceptions logged", errors == 0, errors + " logged"); Finish(); yield break; }
            if (TollbreakerOnly) { yield return TollbreakerRun(); Check("no errors or exceptions logged", errors == 0, errors + " logged"); Finish(); yield break; }
            Check($"boots into the menu on a run map ({RunMaps.CurrentName})", d.State == RunState.Menu && RunMaps.Current >= 0);
            for (int i = 0; i < RunMaps.All.Length; i++)
                Check($"map selectable: {RunMaps.All[i].Name}", RunMaps.InBuild(i), RunMaps.All[i].RunScene);
            yield return Shot("menu");
            if (PhaseSelectOnly) { yield return PracticePhaseChecks(); Check("no errors or exceptions logged", errors == 0, errors + " logged"); Finish(); yield break; }
            if (SylvaraClearOnly)
            {
                yield return SylvaraClearTimes();
                Check("no errors or exceptions logged", errors == 0, errors + " logged");
                Finish(); yield break;
            }
            if (OnlySylvara)
            {
                BossAttackIconChecks();   // the catalog carries her icons
                yield return SylvaraChecks();
                Check("no errors or exceptions logged", errors == 0, errors + " logged");
                Finish(); yield break;
            }
            foreach (var cls in RunDirector.Classes)
            {
                int slots = 6, found = 0;
                for (int i = 0; i < slots; i++) if (RunHud.SlotIcon(cls, i) != null) found++;
                Check($"{cls} skill-bar icons load ({slots} incl. dash)", found == slots, $"{found}/{slots}");
            }
            Check("potion icon loads", RunHud.Icon("potion") != null);
            BossAttackIconChecks();
            MechanicIconChecks();
            yield return DifficultyChecks();

            // ---------- run 1: Tollbreaker, full victory, repeated presses ----------
            Check("StartRun accepted once", d.StartRun("Tollbreaker") && !d.StartRun("Tollbreaker"));
            yield return Wait(0.6f);
            yield return Shot("ready_tollbreaker");
            int starts0 = d.EncounterStarts;
            for (int i = 0; i < 4; i++) { d.RequestReady(); d.RequestReady(); yield return null; }
            yield return Until(() => d.State == RunState.Combat, 4, "combat 1");
            yield return Wait(0.3f);
            Check("repeated Ready starts exactly one encounter", d.EncounterStarts == starts0 + 1 && d.Encounter == d.EncounterOrder[0], $"starts {d.EncounterStarts - starts0}");
            Check("run is a campaign (area minibosses then boss) with Bellkeeper last", Campaign.Valid(d.EncounterOrder) && d.EncounterOrder.Distinct().Count() == d.EncounterOrder.Length && RunDirector.Bosses[d.EncounterOrder.Last()].Final);
            Check("run starts at full health and potions", d.Player.Hp == PlayerController.MaxHp && d.Player.Potions == PlayerController.MaxPotions, $"hp {d.Player.Hp}, potions {d.Player.Potions}");
            yield return AdvanceToFinal();
            var bk = (BellkeeperEncounter)d.Boss;
            int bossHp0 = d.Boss.Hp;
            bool sawSplit = false; var tk = d.Player.Kit as TollbreakerKit;
            float t0 = Time.time;
            var fight = StartCoroutine(Fight(7f, TollRotation, 3f));
            bool shot = false;
            while (Time.time - t0 < 7f && d.State == RunState.Combat) { KeepAlive(); sawSplit |= tk.SplitActive; if (Time.time - t0 > 3.5f && !shot) { shot = true; yield return Shot("combat_tollbreaker_split"); } yield return null; }
            StopCoroutine(fight); bot.Clear();
            Check("Tollbreaker axes damage the boss through the kit", d.Boss.Hp < bossHp0, $"{bossHp0} -> {d.Boss.Hp}");
            Check("Splitting Throw split the boss in the run", sawSplit);
            yield return HazardOnceCheck();
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
            fight = StartCoroutine(Fight(4.5f, TollRotation, 3f));
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
            Check("victory after every campaign fight, Bellkeeper last", d.Victory && d.EncountersCleared == d.EncounterOrder.Length && d.EncounterStarts == d.EncounterOrder.Length && d.EncounterEnds == d.EncounterOrder.Length,
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
            // his model has no Down clip yet: the procedural fall stands in (noted as a placeholder)
            Check("Tollbreaker goes down (Down clip, or the procedural fall until his model has one)", d.Player.Presenter.CurrentClip == "Down" || d.Player.Presenter.Placeholders.Contains("Down"), d.Player.Presenter.CurrentClip);
            Check("player goes down from a real hazard", d.State == RunState.Downed || d.State == RunState.Results, $"state {d.State}, cause {d.DefeatCause}");
            yield return Wait(0.3f); yield return Shot("downed");
            yield return Until(() => d.State == RunState.Results, 4, "results 2");
            Check("defeat result shows the cause", !d.Victory && !string.IsNullOrEmpty(d.DefeatCause), d.DefeatCause);
            Check("no hazards or attacks after defeat", d.World.Hazards.Count == 0 && !d.World.CombatActive);
            yield return Shot("results_defeat");
            yield return DeathRecapIconChecks("defeat");
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
            Check("Chimesinger run reaches victory", d.Victory && d.EncountersCleared == d.EncounterOrder.Length);
            d.ReturnToMenu(); yield return null;

            // ---------- run 5: Arcanist rotation and full victory ----------
            yield return ArcanistRun();

            // ---------- run 6: Tollbreaker kit checks, solo Bellkeeper pass and full victory ----------
            yield return TollbreakerRun();

            // ---------- practice ----------
            d.StartPractice("Tollbreaker", 0);
            d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 4, "practice combat");
            Check("practice starts the chosen encounter", d.Boss is BellkeeperEncounter && d.Mode == RunMode.Practice);
            yield return Wait(1f);
            yield return MechanicIconInGameChecks();
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
                d.StartPractice("Tollbreaker", 0); d.RequestReady();
                yield return Until(() => d.State == RunState.Combat, 4, map.Name + " practice combat");
                int hp0 = d.Boss ? d.Boss.Hp : 0;
                var fm = StartCoroutine(Fight(4f, TollRotation, 3f));
                yield return WaitAlive(4f); StopCoroutine(fm); bot.Clear();
                yield return Shot("map_" + map.Id + "_combat");
                Check($"map {map.Name}: Bellkeeper fight runs (boss takes damage, player stays on the floor)",
                    d.Boss && d.Boss.Hp < hp0 && CombatWorld.InPlayerBound(d.Player.transform.position, 0.01f), $"boss hp {hp0} -> {(d.Boss ? d.Boss.Hp : -1)}");
                yield return TeamMarkShot(map, "team_marks");
                bot.MoveWorld = Vector3.right; bot.Sprint = true; yield return Wait(3f); bot.Clear();
                float reach = d.Player.transform.position.x;
                Check($"map {map.Name}: player walks up to the wall (bound {map.Bound} m)", reach >= map.Bound - 0.3f && CombatWorld.InPlayerBound(d.Player.transform.position, 0.01f), $"reached x = {reach:0.00}");
                yield return Shot("map_" + map.Id + "_wall");
                yield return TeamMarkShot(map, "team_marks_edge");
                d.Boss.DebugDefeat();
                yield return Until(() => d.State == RunState.Results, 6, map.Name + " practice results");
                d.ReturnToMenu();
            }
            Check("map tour returns to the starting map", RunMaps.Current == startMap);
            // ---------- area bosses (practice only for now) ----------
            yield return PartyChecks();
            yield return ReviveChecks();
            yield return AurelionChecks();

            yield return SylvaraChecks();

            // ---------- practice phase row: every boss, every phase ----------
            yield return PracticePhaseChecks();

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
                d.StartPractice("Tollbreaker", 0);
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

        // a quiet moment with the boss stopped: one test eruption hits the player exactly once
        IEnumerator HazardOnceCheck()
        {
            var p = d.Player; var w = d.World;
            p.DebugSetHp(PlayerController.MaxHp);
            yield return Until(() => w.Hazards.Count == 0, 6, "a quiet moment");
            d.Boss.Halt();   // test setup: stop the boss so the check only sees the test hazard
            yield return Wait(0.6f);
            int hp0 = p.Hp;
            w.Hazards.Spawn(new Hazard { Source = "Test eruption", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = p.transform.position, Radius = 1.5f, ResolveAt = 6, Damage = 10 });
            yield return Wait(0.3f);
            Check("one hazard instance hits at most once", hp0 - p.Hp == 10, $"took {hp0 - p.Hp}");
            p.DebugSetHp(PlayerController.MaxHp);
            d.Boss.BeginCombat();
        }

        // taps every 10 ticks, with a Rune Sigil, an Arcane Comet and (once glyphs are held) an Arcane Surge mixed in
        static int ArcRotation(int f) => f % 360 == 60 ? 2 : f % 1100 == 200 ? 3 : f % 1000 == 500 ? 4 : f % 10 == 0 ? 0 : -1;

        int MeterHits(string ability) => d.World.EncounterMeter.TryGetValue(ability, out var r) ? r.Hits : 0;
        int MeterDamage(string ability) => d.World.EncounterMeter.TryGetValue(ability, out var r) ? r.Damage : 0;

        // run 5: an Arcanist run to the final boss, the Arcanist kit checks there, then a full victory
        IEnumerator ArcanistRun()
        {
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
            Check("Arcanist run reaches victory", d.Victory && d.EncountersCleared == d.EncounterOrder.Length);
            d.ReturnToMenu(); yield return null;
        }

        // Arcane kit (docs/design/arcanist-redesign.md): Magic Missile tap and an aimed 3-missile charge, Disintegrate's
        // channel (steps, glyphs, turn cap, early release, dash refund), glyph-powered and weak Rune Sigil (0.8 s) and
        // Arcane Comet (3 s, bullseye), Arcane Surge (cast refund, 2 s per glyph, 5-missile charge) and Blink.
        IEnumerator ArcanistChecks()
        {
            var p = d.Player; var k = (ArcanistKit)p.Kit; var w = d.World;
            d.Boss.Halt(); yield return Wait(0.3f); w.ClearTransient();
            var boss = d.Boss.transform.position;
            var stand = boss + new Vector3(0, 0, 6f + d.Boss.BodyRadius);
            p.transform.position = stand;
            bot.Aim = boss;
            // ---- Magic Missile: tap
            int tap0 = MeterHits("Magic Missile");
            bot.press[0] = true; yield return Wait(0.5f);
            Check("Magic Missile tap fires one straight missile that hits", MeterHits("Magic Missile") == tap0 + 1, $"hits {MeterHits("Magic Missile") - tap0}");
            Check("a tap does not start a charge", !k.Charging);
            // ---- charge 3, release at the boss: all three hit
            int ch0 = MeterHits("Magic Missile (charged)");
            bot.press[0] = true; bot.hold[0] = true; yield return Wait(0.95f);
            Check("holding LMB summons 3 missiles", k.Charging && k.Missiles == 3, k.ClassState());
            yield return Shot("arcanist_missile_charge");
            bot.hold[0] = false; yield return Wait(1.4f);
            Check("releasing fires the charged volley at the cursor; all 3 hit", k.LastVolley == 3 && MeterHits("Magic Missile (charged)") == ch0 + 3, $"volley {k.LastVolley}, hits {MeterHits("Magic Missile (charged)") - ch0}");
            // no tracking: released while aiming 90 degrees away from the boss, they fly where the mouse is and miss
            bot.Aim = p.transform.position + Vector3.right * 6; bot.press[0] = true; bot.hold[0] = true; yield return Wait(0.95f);
            bot.hold[0] = false; yield return Wait(1.6f);
            Check("charged missiles fly at the cursor and do not track the boss", MeterHits("Magic Missile (charged)") == ch0 + 3, $"hits {MeterHits("Magic Missile (charged)") - ch0 - 3} of 3 (want 0)");
            bot.Aim = boss;
            // dash during a charge drops it
            bot.press[0] = true; bot.hold[0] = true; yield return Wait(0.6f);
            bot.dash = true; bot.MoveWorld = Vector3.right; yield return Wait(0.05f);
            Check("Blink drops a missile charge", !k.Charging && k.Missiles == 0, k.ClassState());
            Check("Blink hides the body during the dash", p.Presenter.Hidden && k.Blinking);
            yield return Wait(0.4f); bot.Clear();
            Check("Blink shows the body again where the dash ends", !p.Presenter.Hidden && !k.Blinking);
            p.transform.position = stand; bot.Aim = boss; yield return Wait(0.3f);
            // ---- Disintegrate: a dash during the windup refunds it
            bot.press[1] = true; bot.hold[1] = true; yield return Wait(0.12f);
            bot.dash = true; yield return Wait(0.3f); bot.hold[1] = false;
            Check("dashing during the Disintegrate windup refunds it", !k.BeamActive && k.Cooldown01(1) <= 0, k.SlotState(1));
            p.transform.position = stand; bot.Aim = boss; yield return Wait(0.3f);
            // full channel: 13 damage ticks, 3 steps, one glyph per step that hit
            Check("the Arcanist starts with no glyphs", k.Glyphs == 0, k.ClassState());
            int dis0 = MeterDamage("Disintegrate"); bool sawStep3 = false; int glyphsAtStep3 = -1;
            StartCoroutine(Capture("arcanist_disintegrate", 180));
            PlayerFx.SurfaceMsTotal = PlayerFx.SurfaceMsWorst = PlayerFx.BakeMsWorst = PlayerFx.PoseMsWorst = PlayerFx.ChunkMsWorst = 0; PlayerFx.SurfaceCalls = PlayerFx.SurfaceTraces = 0; PlayerFx.SurfaceTrisTested = PlayerFx.SurfaceTrisTotal = 0;
            bot.press[1] = true; bot.hold[1] = true;
            float tt = 0; bool shotBeam = false;
            while (tt < 2.8f)
            {
                sawStep3 |= k.BeamActive && k.BeamStep == 3;
                if (k.BeamActive && k.BeamStep == 3 && glyphsAtStep3 < 0) glyphsAtStep3 = k.Glyphs;
                if (sawStep3 && !shotBeam) { shotBeam = true; yield return Shot("arcanist_disintegrate_step3"); }
                tt += Time.deltaTime; yield return null;
            }
            bot.hold[1] = false; yield return Wait(0.3f);
            Check("Disintegrate channels for 2 s through 3 damage steps", sawStep3 && !k.BeamActive);
            Check("a full Disintegrate deals 4x8 + 4x12 + 5x16 = 160", MeterDamage("Disintegrate") - dis0 == 160 && k.BeamDamage == 160, $"dealt {MeterDamage("Disintegrate") - dis0}");
            Check("each Disintegrate step that hits carves a glyph: 2 by step 3, 3 after a full channel", glyphsAtStep3 == 2 && k.Glyphs == 3, $"step 3 start {glyphsAtStep3}, end {k.Glyphs}");
            Check("Disintegrate cooldown starts when the beam ends", k.Cooldown01(1) > 0.85f, k.SlotState(1));
            // the beam's contact on his model: chunked, it tests a few hundred triangles a trace, not all of them (the lag fix)
            double avg = PlayerFx.SurfaceCalls > 0 ? PlayerFx.SurfaceMsTotal / PlayerFx.SurfaceCalls : -1;
            Check("Disintegrate's contact trace stays cheap (under 0.3 ms a tick on average, no tick over 15 ms)", PlayerFx.SurfaceCalls > 0 && avg < 0.3 && PlayerFx.SurfaceMsWorst < 15,
                $"{PlayerFx.SurfaceCalls} calls, {PlayerFx.SurfaceTraces} traces, avg {avg:0.000} ms, worst {PlayerFx.SurfaceMsWorst:0.00} ms (snapshot worst {PlayerFx.BakeMsWorst:0.00}: pose {PlayerFx.PoseMsWorst:0.00}, worker {PlayerFx.ChunkMsWorst:0.00}, off the main thread), tested {PlayerFx.SurfaceTrisTested} of {PlayerFx.SurfaceTrisTotal} triangles");
            yield return Shot("arcanist_glyphs_full");
            // ---- Arcane Surge: a dash before the release refunds it and keeps the glyphs
            bot.press[4] = true; yield return Wait(0.07f);
            bot.dash = true; bot.MoveWorld = Vector3.left; yield return Wait(0.35f); bot.Clear();
            Check("dashing during the Arcane Surge cast cancels it for free and keeps the glyphs", !k.Surging && k.Glyphs == 3 && k.Cooldown01(4) <= 0, k.ClassState());
            p.transform.position = stand; bot.Aim = boss; yield return Wait(0.3f);
            // ---- Rune Sigil with a glyph: drawn, armed, explodes on the boss after 0.8 s
            int sig0 = MeterDamage("Rune Sigil");
            bot.Aim = boss; bot.press[2] = true; yield return Wait(0.45f);
            Check("Rune Sigil is placed at the cursor and spends a glyph", k.Sigils.Count == 1 && Sim.Flat(k.Sigils[0].Pos - boss).magnitude < 0.3f && k.Sigils[0].Powered && k.Glyphs == 2, k.ClassState());
            yield return Shot("arcanist_rune_sigil");
            yield return Wait(0.2f);
            Check("Rune Sigil is still arming at 0.65 s", k.Sigils.Count == 1);
            yield return Wait(0.4f);
            Check("a powered Rune Sigil explodes after 0.8 s for 55", k.Sigils.Count == 0 && MeterDamage("Rune Sigil") - sig0 == 55 && k.LastSigilPowered, $"dealt {MeterDamage("Rune Sigil") - sig0}");
            // ---- Arcane Comet with a glyph: a 3 s fall, bullseye on a still boss
            int com0 = MeterDamage("Arcane Comet (bullseye)");
            bot.Aim = boss; bot.press[3] = true; yield return Wait(0.3f);
            Check("Arcane Comet marks the cursor, spends a glyph and starts falling", k.Comets.Count == 1 && Sim.Flat(k.Comets[0].Pos - boss).magnitude < 0.3f && k.Comets[0].Powered && k.Glyphs == 1, k.ClassState());
            yield return Wait(2.3f); yield return Shot("arcanist_comet_falling");
            Check("the comet is still falling at 2.6 s", k.Comets.Count == 1);
            yield return Until(() => k.Comets.Count == 0, 1.5f, "comet lands");
            yield return Wait(0.05f); yield return Shot("arcanist_comet_impact");
            Check("Arcane Comet lands 3 s after the mark; a powered bullseye deals 180", k.LastComet == 180 && k.LastCometBullseye && k.LastCometPowered && MeterDamage("Arcane Comet (bullseye)") - com0 == 180, $"comet {k.LastComet}, bullseye {k.LastCometBullseye}");
            // ---- Arcane Surge with 3 glyphs (topped up from the 1 left): 6 s, a 5-missile charge
            k.DebugSetGlyphs(3);
            bot.Aim = boss; bot.press[4] = true; yield return Until(() => k.Surging, 1.5f, "Surge starts"); yield return Wait(0.1f);
            Check("Arcane Surge spends every glyph for 2 s each", k.Surging && k.Glyphs == 0 && k.LastSurgeGlyphs == 3 && k.SurgeSeconds > 5.4f && k.SurgeSeconds <= 6f, k.ClassState());
            yield return Shot("arcanist_surge");
            int sv0 = MeterHits("Magic Missile (charged)");
            yield return Wait(0.15f);   // the Surge cast (0.3 s) has to finish before LMB starts a charge
            bot.press[0] = true; bot.hold[0] = true; yield return Wait(0.6f); yield return Until(() => k.Missiles == 5, 1.5f, "5 missiles");
            Check("in Surge the charge summons 5 missiles", k.Charging && k.Missiles == 5, k.ClassState());
            bot.hold[0] = false; yield return Wait(1.2f);
            Check("the Surge volley fires 5 missiles that hit", k.LastVolley == 5 && MeterHits("Magic Missile (charged)") - sv0 == 5, $"volley {k.LastVolley}, hits {MeterHits("Magic Missile (charged)") - sv0}");
            yield return Until(() => !k.Surging, 6.5f, "Surge ends");
            Check("Arcane Surge ends on time", !k.Surging);
            // ---- Rune Sigil without a glyph: 35 %
            yield return Until(() => k.Cooldown01(2) <= 0, 7, "Rune Sigil ready");
            int weak0 = MeterDamage("Rune Sigil (no glyph)");
            bot.Aim = boss; bot.press[2] = true; yield return Wait(0.2f);
            Check("a Rune Sigil cast with no glyph is weak", k.Sigils.Count == 1 && !k.Sigils[0].Powered, k.ClassState());
            yield return Shot("arcanist_rune_sigil_weak");
            yield return Wait(0.9f);
            Check("a weak Rune Sigil deals 35 % (19)", MeterDamage("Rune Sigil (no glyph)") - weak0 == 19 && !k.LastSigilPowered, $"dealt {MeterDamage("Rune Sigil (no glyph)") - weak0}");
            bot.press[4] = true; yield return Wait(0.3f);
            Check("Arcane Surge needs glyphs", !k.Surging, k.SlotState(4));
            // ---- Disintegrate turn cap and early release
            yield return Until(() => k.Cooldown01(1) <= 0, 9, "Disintegrate ready");
            bot.Aim = boss; bot.press[1] = true; bot.hold[1] = true; yield return Wait(0.6f);
            var dir0 = k.BeamDir; int tick0 = w.Tick;
            bot.Aim = p.transform.position + Vector3.Cross(Vector3.up, dir0) * 8; yield return Wait(0.2f);
            float turned = Vector3.Angle(dir0, k.BeamDir); int ticks = w.Tick - tick0;
            Check("Disintegrate turns toward the cursor at 1.5° per tick at most", k.BeamActive && turned <= ticks * 1.5f + 0.5f && turned >= (ticks - 2) * 1.5f, $"{turned:0.0}° in {ticks} ticks");
            bot.hold[1] = false; yield return Wait(0.1f);
            Check("releasing 2 ends the beam early", !k.BeamActive && k.Cooldown01(1) > 0.9f);
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

        // ------------------------------------------------------------ Sylvara v3 (bullet hell, abilities-v3/P1..P4)
        static bool ArcanistOnly => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellArcanistOnly", StringComparison.OrdinalIgnoreCase));
        static bool OnlySylvara => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellSylvara", StringComparison.OrdinalIgnoreCase));
        static bool SylvaraClearOnly => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellSylvaraClear", StringComparison.OrdinalIgnoreCase));

        // HP tuning (opt-in, -lastbellSylvaraClear): attacking bots clear the whole fight on Medium, with real downs and no
        // healing by the test. The player is a bot too. Reports clear time, time per phase, downs and damage per second.
        IEnumerator SylvaraClearTimes()
        {
            bool was = AllyBot.Attacks; AllyBot.Attacks = true; int keep = RunParty.Selected;
            var keepLevel = RunDifficulty.Selected;
            // Hard: Tollbreaker solo plus one party of 4 (test less, 1 Nov: the levels only scale damage)
            var runs = new (string cls, int allies, Difficulty level)[] { ("Tollbreaker", 0, Difficulty.Hard), ("Tollbreaker", 3, Difficulty.Hard) };
            // -lastbellSylvaraClearPhase n starts each run in phase n (practice Start Phase row); -lastbellSylvaraClearRepeat k runs the set k times
            int startPhase = int.TryParse(AurelionArgs.Arg("-lastbellSylvaraClearPhase"), out var sp) ? Mathf.Clamp(sp, 1, 4) : 1;
            int repeats = int.TryParse(AurelionArgs.Arg("-lastbellSylvaraClearRepeat"), out var nr) ? Mathf.Clamp(nr, 1, 10) : 1;
            runs = Enumerable.Repeat(runs, repeats).SelectMany(r => r).ToArray();
            foreach (var (cls, allies, level) in runs)
            {
                if (d.State != RunState.Menu) { d.SetPaused(true); d.ReturnToMenu(); yield return null; }
                d.SelectAllies(allies); d.SelectDifficulty(level);
                if (!d.StartPractice(cls, RunDirector.SylvaraPractice, startPhase)) { Check($"Sylvara clear run {cls} +{allies} starts", false); continue; }
                yield return Until(() => RunDirector.Instance && RunDirector.Instance.State == RunState.Ready, 30, "Verdant Chimes loaded for a clear run");
                if (RunDirector.Instance != d) { directorLog.AddRange(d.Log); d = RunDirector.Instance; d.InputOverride = bot; bot.Clear(); }
                d.RequestReady();
                yield return Until(() => d.State == RunState.Combat, 4, "clear run combat");
                var sy = d.Boss as SylvaraEncounter; var w = d.World;
                if (sy == null) continue;
                d.Player.Input = new AllyBot(allies, allies + 1, BotMode.Perfect);
                int n = allies + 1, maxHp = sy.MaxHp, downs = 0, tick0 = w.Tick, last = tick0;
                Check($"Sylvara HP for {n} fighting players is the accepted table's {SylvaraEncounter.HpFor(n)}", maxHp == SylvaraEncounter.HpFor(n), maxHp.ToString());
                var down = new HashSet<PlayerController>(); var phaseAt = new List<(string name, int tick)> { (sy.PhaseName, tick0) };
                // hits per Hard addition (6 Nov): bullets and hazards made by one carry its name; the rest are "base". A down counts
                // for the last hit before it. PlayerHit fires first for a bullet hit, then Bullets.Hit names its bullet.
                var hitLog = new Dictionary<string, int[]>(); var lastKey = new Dictionary<PlayerController, string>();
                PlayerController pendP = null; string pendKey = null; int pendDmg = 0;
                void Count(string key, int n, int dmg) { if (!hitLog.TryGetValue(key, out var c)) hitLog[key] = c = new int[3]; c[0] += n; c[1] += dmg; }
                Action<PlayerController, Hazard, int> onHaz = (p, h, dmg) =>
                {
                    string key = $"{(sy.HazardTag.TryGetValue(h, out var tg) ? tg : "base")} | {sy.PhaseName} | {h.Source}";
                    Count(key, 1, dmg); lastKey[p] = key; pendP = p; pendKey = key; pendDmg = dmg;
                };
                Action<PlayerController, BulletSystem.Bullet, int> onBul = (p, b, dmg) =>
                {
                    if (pendP != p || pendKey == null || b.Tag == null) { pendP = null; return; }
                    string key = $"{b.Tag} | {sy.PhaseName} | {b.Source}";
                    Count(pendKey, -1, -pendDmg); Count(key, 1, pendDmg); lastKey[p] = key; pendP = null;
                };
                w.PlayerHit += onHaz; w.Bullets.Hit += onBul;
                // reach (6 Nov): how often each class stands where its bot attacks her (the bot's own range for the phase, so the
                // Tollbreaker's 9 m Seed Storm throw counts, + body + 2 m), and its mean distance
                var reachN = new Dictionary<string, int[]>(); var reachD = new Dictionary<string, float>();
                float SwingAt(string c) => AllyBot.SylvaraAttackRange(c, sy.PhaseName) + sy.BodyRadius + 2f;
                while (d.State == RunState.Combat && d.World == w && (w.Tick - tick0) < Sim.Ticks(480))
                {
                    last = w.Tick;
                    if (sy.PhaseName != phaseAt[phaseAt.Count - 1].name) phaseAt.Add((sy.PhaseName, w.Tick));
                    foreach (var p in w.Players) if (!p.Down)
                    {
                        float dist = Sim.Flat(p.transform.position - sy.transform.position).magnitude; var c = p.ClassName;
                        if (!reachN.TryGetValue(c, out var rn)) reachN[c] = rn = new int[2];
                        rn[0]++; if (dist <= SwingAt(c)) rn[1]++; reachD[c] = (reachD.TryGetValue(c, out var ds) ? ds : 0) + dist;
                    }
                    foreach (var p in w.Players) { if (p.Down && down.Add(p)) { downs++; if (lastKey.TryGetValue(p, out var lk)) Count(lk, 0, 0); if (lk != null) hitLog[lk][2]++; } else if (!p.Down) down.Remove(p); }
                    yield return null;
                }
                // the last down ends combat on the same tick, before the loop sees it: count it for its last hit too
                foreach (var p in w.Players) if (p.Down && down.Add(p)) { downs++; if (lastKey.TryGetValue(p, out var lk)) hitLog[lk][2]++; }
                float secs = (last - tick0) / (float)Sim.Hz; bool won = d.State == RunState.Cleared || d.Victory || (sy && sy.Dead), wiped = d.State == RunState.Downed;
                var parts = new List<string>();
                for (int i = 0; i < phaseAt.Count; i++)
                {
                    int end = i + 1 < phaseAt.Count ? phaseAt[i + 1].tick : last;
                    parts.Add($"{phaseAt[i].name} {(end - phaseAt[i].tick) / (float)Sim.Hz:0}s");
                }
                float hpLeft = sy ? sy.Hp / (float)maxHp : 0;
                Note($"Sylvara clear, {RunDifficulty.Name(level)}: {n} player{(n > 1 ? "s" : "")} ({cls} + {allies} bots), HP {maxHp}: {(won ? "cleared" : wiped ? $"wiped ({hpLeft:P0} left)" : $"not cleared ({hpLeft:P0} left)")} in {secs:0} s"
                    + $" ({string.Join(", ", parts)}); {downs} downs; party DPS {(maxHp * (1 - (won ? 0 : hpLeft))) / Mathf.Max(1, secs):0}; buds {sy?.BudsCut ?? 0} cut, {sy?.BudsBloomed ?? 0} bloomed");
                Check($"Sylvara clear run {cls} +{allies} {RunDifficulty.Name(level)}: the bots deal damage", won || hpLeft < 1f);
                w.PlayerHit -= onHaz; w.Bullets.Hit -= onBul;
                Note("  reach: " + string.Join(", ", reachN.Select(kv => $"{kv.Key} {kv.Value[1] * 100 / Mathf.Max(1, kv.Value[0])} % in swing range, mean {reachD[kv.Key] / Mathf.Max(1, kv.Value[0]):0.0} m")));
                Note("  damage: " + string.Join(", ", w.EncounterMeter.Values.OrderByDescending(r => r.Damage).Select(r => $"{r.Ability} {r.Damage} ({r.Hits} hits)")));
                var adds = new[] { SylvaraEncounter.MirrorTag, "Choir Pods", "Counter Spiral", "Storm Arc", "base" };
                foreach (var a in adds)
                {
                    var rows = hitLog.Where(kv => kv.Key.StartsWith(a + " |") && kv.Value[0] > 0).ToList();
                    Note($"  hits, {a}: {rows.Sum(r => r.Value[0])} hits, {rows.Sum(r => r.Value[1])} damage, {rows.Sum(r => r.Value[2])} downs");
                }
                foreach (var kv in hitLog.Where(kv => kv.Value[0] > 0).OrderByDescending(kv => kv.Value[1]).Take(12))
                    Note($"    {kv.Key}: {kv.Value[0]} hits, {kv.Value[1]} damage, {kv.Value[2]} downs");
                if (d.State == RunState.Combat && d.Boss) d.Boss.DebugDefeat();   // timed out: end it so the next run can start
                yield return Until(() => d.State != RunState.Combat, 10, "clear run ends");
            }
            AllyBot.Attacks = was; d.SelectDifficulty(keepLevel);
            if (d.State != RunState.Menu) { d.SetPaused(true); d.ReturnToMenu(); yield return null; }
            d.SelectAllies(keep);
        }

        IEnumerator SylvaraChecks()
        {
            // the practice section above leaves the director in Ready, where ReturnToMenu only works from the pause menu
            if (d.State != RunState.Menu) { d.SetPaused(true); d.ReturnToMenu(); yield return null; }
            int verdant = RunMaps.IndexOf(RunDirector.SylvaraMap);
            Check("Sylvara practice accepted from the menu", d.StartPractice("Tollbreaker", RunDirector.SylvaraPractice));
            yield return Until(() => RunMaps.Current == verdant && RunDirector.Instance && RunDirector.Instance.State == RunState.Ready, 30, "Verdant Chimes loaded for Sylvara");
            if (RunDirector.Instance != d) { directorLog.AddRange(d.Log); d = RunDirector.Instance; d.InputOverride = bot; bot.Clear(); }
            if (d.Player) d.Player.Input = bot;   // the new scene built the player before the bot was attached
            Check("Sylvara practice loads the Verdant Chimes arena", RunMaps.Current == verdant && HazardSystem.ArenaCount > 0, RunMaps.CurrentName);
            Check("Sylvara practice keeps the chosen class across the map load", d.ClassName == "Tollbreaker" && d.Mode == RunMode.Practice && d.Encounter == RunDirector.SylvaraPractice);
            d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 4, "Sylvara practice combat");
            var sy = d.Boss as SylvaraEncounter;
            Check("Sylvara practice starts her encounter", sy != null && d.Mode == RunMode.Practice);
            if (sy == null) yield break;
            Check("Sylvara: full health and potions (area final boss)", d.Player.Hp == PlayerController.MaxHp && d.Player.Potions == PlayerController.MaxPotions);
            var syPres = sy.Presenter; var syClips = new[] { "Idle", "PruningArc", "Rootscript", "Seed", "PhaseShift", "Defeat" };
            Check("Sylvara uses her rigged model with the clips v3 plays", syPres && syPres.HasModel && !sy.Placeholder && syClips.All(c => syPres.Has(c)), syPres ? string.Join(",", syClips.Where(c => !syPres.Has(c))) : "no presenter");
            Check("Sylvara v3 is rooted at the arena centre", Sim.Flat(sy.transform.position).magnitude < 0.01f);
            Check("Sylvara v3 opens in Spiral Bloom", sy.PhaseName == "Spiral Bloom", sy.PhaseName);
            Check("Sylvara solo HP is 4,600 (passive bots don't count)", sy.MaxHp == Mathf.RoundToInt(4600 * BossEncounter.TestHpScale), sy.MaxHp.ToString());
            d.World.Difficulty = Difficulty.Medium;   // a saved menu choice must not change the checked numbers; levels are checked in Phase 4

            // watch the whole fight: every bullet hits once, bullets never stun, hazards hit once per instance
            var bulletsHit = new HashSet<BulletSystem.Bullet>(); bool bulletOnce = true, bulletNoStun = true, colourOk = true;
            int bulletHits = 0, taken = 0;
            var hitsPerInstance = new Dictionary<int, int>();
            Action<PlayerController, BulletSystem.Bullet, int> onBullet = (p, b, dmg) =>
            {
                bulletOnce &= bulletsHit.Add(b); bulletHits++;
                if (b.Colour != 0) colourOk &= sy.ColourOf(p) == b.Colour;
            };
            Action<PlayerController, Hazard, int> onHit = (p, h, dmg) =>
            {
                taken += dmg;
                if (!h.Flinch) bulletNoStun &= h.Stun == 0 && h.Knockback == 0;
                else hitsPerInstance[h.Id] = hitsPerInstance.TryGetValue(h.Id, out int n) ? n + 1 : 1;
            };
            d.World.Bullets.Hit += onBullet; d.World.PlayerHit += onHit;
            bool watching = true; int maxBullets = 0;
            IEnumerator Watch()
            {
                while (watching && d.World != null)
                {
                    maxBullets = Mathf.Max(maxBullets, d.World.Bullets.Count);
                    if (d.Player && d.Player.Hp < 60) d.Player.DebugSetHp(PlayerController.MaxHp);   // keep the schedule running; damage is noted (60: three Hard petals at once are 48)
                    yield return null;
                }
            }
            StartCoroutine(Watch());
            IEnumerator Play(float seconds, Func<bool> until)
            {
                float t0 = Time.time;
                while (Time.time - t0 < seconds && d.State == RunState.Combat && !Safe(until))
                { var sv = StartCoroutine(Survive(0.25f)); yield return Wait(0.25f); StopCoroutine(sv); }
                bot.Clear();
            }

            // Attack props (proposal art, 1 Nov): every look fits its hit circle; the Meshy models are gitignored, so their
            // checks run only where the art is installed. A watcher takes one screenshot of each prop in action.
            {
                bool Fits(Mesh m, float r) { var b = m.bounds; return Mathf.Max(Mathf.Abs(b.min.x), Mathf.Abs(b.max.x), Mathf.Abs(b.min.z), Mathf.Abs(b.max.z)) <= r + 1e-3f; }
                Check($"petal bullets (coral, brass, cobalt) fit their {BulletArt.PetalHitRadius:0.00} m hit circle", Fits(BulletArt.Petal(false), BulletArt.PetalHitRadius) && Fits(BulletArt.Petal(true), BulletArt.PetalHitRadius));
                // the circle shrank to fit the slimmer petal (1 Nov): its tips reach the edge, no larger
                Check("petal hit circle is the petal's reach (0.30 m, was 0.35)", Mathf.Approximately(BulletArt.PetalHitRadius, 0.30f) && !Fits(BulletArt.Petal(false), BulletArt.PetalHitRadius - 0.02f));
                Check($"a fired petal uses the {BulletArt.PetalHitRadius:0.00} m circle", new BulletSystem.Bullet().Radius == BulletArt.PetalHitRadius);
                Check($"water drops fit their {BulletArt.DropHitRadius:0.00} m hit circle", Fits(BulletArt.Drop, BulletArt.DropHitRadius));
                bool art = SylvaraProps.Has(SylvaraProps.Bud) && SylvaraProps.Has(SylvaraProps.Pod) && SylvaraProps.Has(SylvaraProps.Root) && BulletArt.HasSeedModel;
                Note("Sylvara attack props: " + (art ? "Meshy models installed" : "art not installed, stand-ins used"));
                if (BulletArt.HasSeedModel)
                {
                    var seedT = BulletArt.MakeSeed(null, null); var rs = seedT.GetComponentsInChildren<Renderer>(); var sb = rs[0].bounds; foreach (var r in rs) sb.Encapsulate(r.bounds);
                    float half = Mathf.Max(sb.extents.x, sb.extents.z); Destroy(seedT.gameObject);
                    Check("the Storm Seed model fits its 0.6 m hit radius", half <= 0.6f, $"{half:0.00} m");
                }
                StartCoroutine(PropShots());
            }
            // review screenshots: the test player is moved next to each prop first so the camera frames it (the bot is healed)
            IEnumerator PropShots()
            {
                bool pod = false, bud = false, seedShot = false;
                if (SylvaraProps.Has(SylvaraProps.Root))
                {
                    // the Root Ring's look on demand, with the player just outside the 3.5 m disc so the camera frames it
                    float tw = Time.time;
                    while (Time.time - tw < 0.6f) { d.Player.transform.position = new Vector3(0, 0, 5f); yield return null; }
                    int r0 = sy.RootModels; sy.DebugRoots();
                    Check("the Root Ring look raises its root models", sy.RootModels - r0 == SylvaraEncounter.RootPieces);
                    yield return new WaitForSeconds(0.2f); yield return Shot("sylvara_props_root_ring");
                }
                while (d.World != null && !(pod && bud && seedShot))
                {
                    foreach (var m in FindObjectsByType<PropMotion>(FindObjectsSortMode.None))
                    {
                        if (!pod && m.name == SylvaraProps.Pod && m.Now == PropMotion.Mode.Fall && m.LandsIn < 0.12f) { pod = true; yield return Shot("sylvara_props_pod_falls"); break; }
                        if (!bud && sy.BudsCut > 0 && m.name == SylvaraProps.Bud && m.Now == PropMotion.Mode.Sprout && m.Open > 0.4f)   // after the cut test
                        {
                            bud = true; var bp = m.transform.position; var at = bp - bp.normalized * 3.5f; float tb = Time.time;
                            while (Time.time - tb < 0.8f) { d.Player.transform.position = at; yield return null; }
                            yield return Shot("sylvara_props_choir_bud"); break;
                        }
                    }
                    if (!seedShot && sy.PhaseName == "Seed Storm" && d.World.Bullets.Live.Any(b => b.Source == "Seed" && b.Life - b.Age < 0.3f && Sim.Flat(b.Pos - d.Player.transform.position).magnitude < 7f))
                    { seedShot = true; yield return Shot("sylvara_props_storm_seeds"); }   // near the burst, so the ray preview shows too
                    yield return null;
                }
            }

            // Phase 1: one full loop
            bool p1Shot = false;
            float s1 = Time.time;
            while (Time.time - s1 < 34f && d.State == RunState.Combat && !sy.Order.Contains("Triangle Pods"))
            {
                yield return Play(0.5f, () => false);
                if (!p1Shot && d.World.Bullets.Count > 80) { p1Shot = true; yield return Shot("sylvara_v3_p1"); StartCoroutine(Capture("sylvara_v3_p1", 240)); }
            }
            yield return Play(5f, () => sy.Order.Count(o => o == "Pruning Arc") >= 2);
            if (!p1Shot) yield return Shot("sylvara_v3_p1");
            Note("Sylvara P1 order: " + string.Join(" → ", sy.Order));
            foreach (var id in new[] { "Petal Spiral", "Seed Pods", "Waterfall Jets", "Toll", "Feet Pod", "Pruning Arc", "Doubled Pods", "Second Spiral", "Triangle Pods" })
                Check($"Sylvara P1 uses {id}", sy.Order.Contains(id));
            Check("Sylvara P1 fills the arena with bullets", d.World.Bullets.Fired > 300, $"{d.World.Bullets.Fired} fired, peak {d.World.Bullets.Peak}");
            // her attack clips (proposal 2 Nov): a Conduct loop under the spirals, one-shots on the attacks
            Check("Sylvara conducts while a spiral runs", sy.LoopClip == "ConductLoop", sy.LoopClip);
            foreach (var c in new[] { "Toll", "CallWater", "PruningArc", "Seed" }) Check($"Sylvara P1 plays the {c} clip", sy.Clips.Contains(c));
            if (sy.Presenter && sy.Presenter.Has("Intro")) Check("Sylvara wakes with the Intro clip on the intro card", sy.Clips.Contains("Intro"));
            var newClips = new[] { "ConductLoop", "StormLoop", "Toll", "CallWater", "Sow", "LanesLoop", "Intro", "MaskSplit", "HollowLoop", "StormCall", "Stagger" };
            if (sy.Presenter && sy.Presenter.HasModel && newClips.All(sy.Presenter.Has)) Check("her model has every v3 attack clip", true);
            else Note("Sylvara: this model has no v3 attack clips (old sylvara-assets pack): every attack falls back to Idle");
            if (SylvaraProps.Has(SylvaraProps.Pod)) Check("Seed Pods drop the pod model onto their warnings", sy.PodModels > 0, sy.PodModels.ToString());

            // Phase 2: colours
            sy.Order.Clear(); int rootsBefore = sy.RootModels;
            d.Boss.DebugSetHpFraction(0.74f);
            yield return Until(() => sy.PhaseName == "Two-Colour Choir", 4, "Sylvara phase 2");
            Check("Sylvara enters Two-Colour Choir below 75 %", sy.PhaseName == "Two-Colour Choir");
            Check("a phase change clears every bullet", d.World.Bullets.Count == 0, d.World.Bullets.Count + " left");
            yield return Until(() => sy.Current != "phase shift", 3, "phase 2 transition over");
            int mine = sy.ColourOf(d.Player), other = mine == SylvaraEncounter.Brass ? SylvaraEncounter.Cobalt : SylvaraEncounter.Brass;
            Check("the player has a colour in Phase 2", mine == SylvaraEncounter.Brass || mine == SylvaraEncounter.Cobalt, mine.ToString());
            {
                // a still bullet on the player: the other colour passes through, their own colour hits
                d.Player.DebugSetHp(PlayerController.MaxHp);
                var ghost = d.World.Bullets.Fire(d.Player.transform.position, 0, 0, 12, 0.25f, 0.35f, other, "Test Bullet");
                yield return Wait(0.35f);
                var real = d.World.Bullets.Fire(d.Player.transform.position, 0, 0, 12, 0.25f, 0.35f, sy.ColourOf(d.Player), "Test Bullet");
                yield return Wait(0.35f);
                Check("Two-Colour Choir: the other colour's bullet passes through you", !bulletsHit.Contains(ghost));
                Check("Two-Colour Choir: your own colour's bullet hits you", bulletsHit.Contains(real));
            }
            int c0 = sy.ColourOf(d.Player); bool swapped = false, p2Shot = false, laneBeat = false;
            float s2 = Time.time;
            while (Time.time - s2 < 12f && d.State == RunState.Combat)
            {
                yield return Play(0.5f, () => false);
                swapped |= sy.ColourOf(d.Player) != c0 && sy.ColourOf(d.Player) != 0;
                laneBeat |= sy.LoopClip == "LanesLoop";   // the lanes fire for most of the loop
                if (!p2Shot && d.World.Bullets.Count > 150) { p2Shot = true; yield return Shot("sylvara_v3_p2"); }
            }
            Check("Two-Colour Choir: a toll swaps the player's colour", swapped);
            Check("Sylvara beats the Lanes loop while the lanes fire", laneBeat);
            Check("the Phase 2 spiral shows the Two-Colour Choir icon", sy.AttackOf("Petal Spiral") == "sylvara.two_colour_choir");
            foreach (var id in new[] { "Colour Spiral", "Brass Lanes", "Cobalt Lanes", "Second Colour Spiral", "Root Ring", "Toll" })
                Check($"Sylvara P2 uses {id}", sy.Order.Contains(id));
            if (SylvaraProps.Has(SylvaraProps.Root))
            {
                yield return Play(4f, () => sy.RootModels > rootsBefore);   // the ring starts at 12 s and resolves 2 s later
                Check("the Root Ring raises root models inside its disc", sy.RootModels - rootsBefore >= SylvaraEncounter.RootPieces, (sy.RootModels - rootsBefore).ToString());
            }

            // Phase 3: buds
            sy.Order.Clear();
            d.Boss.DebugSetHpFraction(0.49f);
            yield return Until(() => sy.PhaseName == "Hollow Bloom", 4, "Sylvara phase 3");
            Check("Sylvara enters Hollow Bloom below 50 %", sy.PhaseName == "Hollow Bloom");
            Check("leaving Phase 2 clears the colours", sy.ColourOf(d.Player) == 0);
            yield return Until(() => sy.BudsAlive > 0, 5, "Choir Buds sprouted");
            yield return Shot("sylvara_v3_p3");
            if (SylvaraProps.Has(SylvaraProps.Bud)) Check("Choir Buds use the bell-flower model", sy.BudModels > 0, sy.BudModels.ToString());
            var buds = sy.AliveBudPositions();
            if (buds.Count > 0)
            {
                // buds are boss parts: standing next to one does nothing, the class's attacks cut it, and the damage also hits her
                int cut0 = sy.BudsCut; var at = buds[0] - buds[0].normalized * 1.5f;
                float t3 = Time.time;
                while (Time.time - t3 < 1f) { bot.Clear(); d.Player.transform.position = at; yield return null; }
                Check("standing next to a Choir Bud without attacking does not cut it", sy.BudsCut == cut0 && sy.BudHpAt(buds[0]) >= SylvaraEncounter.BudHp);
                d.Player.Kit.ClearTargetState();   // the held attacks above left Tollbreaker's axes lodged (back after 6-8 s): start with both in hand
                int hpBoss = d.Boss.Hp; t3 = Time.time;
                // Tollbreaker taps (a held Twin Hurl lodges); a tap stays pending until a sim tick reads it, or a fast frame rate drops it
                while (sy.BudsCut == cut0 && Time.time - t3 < 8f) { bool tap = bot.press[0]; bot.Clear(); bot.Aim = buds[0]; bot.hold[0] = d.ClassName != "Tollbreaker"; bot.press[0] = tap || Time.frameCount % 6 == 0; d.Player.transform.position = at; yield return null; }
                bot.Clear();
                float cutIn = Time.time - t3;
                Check("attacking a Choir Bud cuts it in a few seconds (class damage)", sy.BudsCut > cut0 && cutIn < 6f, $"{d.ClassName} {cutIn:0.0}s");
                Check("damage to a Choir Bud also hits Sylvara", hpBoss - d.Boss.Hp >= SylvaraEncounter.BudHp * 0.9f, $"{hpBoss - d.Boss.Hp}");
                Note($"Sylvara: {d.ClassName} cut a {SylvaraEncounter.BudHp} HP Choir Bud in {cutIn:0.0}s");
            }
            yield return Play(14f, () => sy.Order.Contains("Pruning Arc"));
            foreach (var id in new[] { "Hollow Spiral", "Choir Buds", "Feet Pod", "Pruning Arc" })
                Check($"Sylvara P3 uses {id}", sy.Order.Contains(id));
            Check("Sylvara plays the Bloom clip when she plants Choir Buds", sy.Clips.Contains("Bloom"));
            Check("the Hollow Spiral plays the open-mask loop (HollowLoop)", sy.LoopClip == "HollowLoop", sy.LoopClip);
            if (sy.Presenter && sy.Presenter.Has("MaskSplit")) Check("the second form plays MaskSplit as the mask opens", sy.Clips.Contains("MaskSplit"));
            Note($"Sylvara P3: Stagger played {sy.Clips.Count(c => c == "Stagger")}x (only on a bud cut while no other clip runs)");
            if (sy.HasMaskBones) Check("the second form splits her mask open", sy.MaskOpen > 0.99f, sy.MaskOpen.ToString("0.00"));
            else Note("Sylvara: this model has no mask bones (sylvara-assets 2.0.0 or older): the mask stays shut in the second form");
            Note($"Sylvara P3 buds: {sy.BudsCut} cut, {sy.BudsBloomed} bloomed");

            // Phase 4: seed storm and the enrage steps (timing accepted 29 Oct)
            sy.Order.Clear();
            d.Boss.DebugSetHpFraction(0.24f);
            yield return Until(() => sy.PhaseName == "Seed Storm", 4, "Sylvara phase 4");
            Check("Sylvara enters Seed Storm below 25 %", sy.PhaseName == "Seed Storm");
            bool seed = false, burst = false;
            float s4 = Time.time;
            while (Time.time - s4 < 7f && d.State == RunState.Combat)
            {
                yield return Play(0.25f, () => false);
                seed |= d.World.Bullets.Live.Any(b => b.Source == "Seed");
                burst |= d.World.Bullets.Live.Any(b => b.Source == "Seed Burst");
            }
            yield return Shot("sylvara_v3_p4");
            if (NoFrames) Note("Seed Storm washout check skipped (batchmode)");
            else
            {
                // hundreds of bullet meshes on screen must not wash the frame out (a NaN normal once bloomed it to white)
                yield return new WaitForEndOfFrame();
                var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); tex.Apply();
                var px = tex.GetPixels32(); double sum = 0; int white = 0;
                for (int i = 0; i < px.Length; i += 7) { float l = (px[i].r + px[i].g + px[i].b) / 765f; sum += l; if (l > 0.97f) white++; }
                Destroy(tex);
                float mean = (float)(sum / (px.Length / 7)), whiteShare = white / (float)(px.Length / 7);
                Check("Seed Storm frame is not washed out (bullet looks)", whiteShare < 0.2f && mean < 0.8f, $"mean {mean:0.00}, white {whiteShare:P0}");
            }
            Check("Seed Storm throws seeds", seed);
            Check("Sylvara plays the Sow clip as she throws a seed ring", sy.Clips.Contains("Sow"));
            Check("seeds burst into petals when their fuse ends", burst);
            Check("Seed Storm loop 1: 8 seeds every 2.0 s", sy.StormSeeds == 8 && Mathf.Approximately(sy.StormEvery, 2.0f), $"{sy.StormSeeds} / {sy.StormEvery}");
            sy.DebugStartStorm(2);
            Check("enrage step: loop 3 throws 10 seeds every 1.8 s", sy.StormSeeds == 10 && Mathf.Abs(sy.StormEvery - 1.8f) < 0.001f, $"{sy.StormSeeds} / {sy.StormEvery}");
            if (sy.Presenter && sy.Presenter.Has("StormCall")) { sy.DebugLastStormWarning(); Check("the LAST STORM warning plays StormCall", sy.Clips.Contains("StormCall")); }
            sy.DebugStartStorm(7);
            Check("enrage cap: Last Storm from 2:00 throws 14 seeds every 0.6 s", sy.LastStorm && sy.StormSeeds == 14 && Mathf.Abs(sy.StormEvery - 0.6f) < 0.001f);
            Check("Last Storm plays the Storm loop", sy.LoopClip == "StormLoop", sy.LoopClip);
            Check("Last Storm's spiral and seeds show the Last Storm icon", new[] { "Petal Spiral", "Seed", "Seed Burst" }.All(s => sy.AttackOf(s) == "sylvara.last_storm"));
            yield return Play(3f, () => false);
            yield return Shot("sylvara_v3_last_storm");

            // Easy and Hard extras (accepted 31 Oct): one Hard mechanic per phase, longer warnings and fewer bullets on Easy;
            // the enrage clock stays the same on every level, and Last Storm is softer on Easy
            int budBase = sy.BudCount;
            foreach (var lv in new[] { Difficulty.Easy, Difficulty.Hard })
            {
                d.World.Difficulty = lv; bool e = lv == Difficulty.Easy, h = !e;
                Check($"Sylvara {lv} P1: pods land at {(e ? 3.0f : 2.5f)} s, {(h ? "every one doubled" : "single")}, spiral {(e ? 12 : 15)}°/s",
                    Mathf.Approximately(sy.PodLandIn(1), e ? 3.0f : 2.5f) && sy.PodDoubledIn(1) == h && Mathf.Approximately(sy.P1Spin, e ? 12 : 15)
                    && !sy.PodDoubledIn(3) && Mathf.Approximately(sy.PodLandIn(3), 2.5f));
                Check($"Sylvara {lv} P1: jets warn {(e ? 2.0f : 1.5f)} s, Pruning Arc {(e ? "2.5 / 3.3" : "2.0 / 2.8")} s",
                    Mathf.Approximately(sy.JetWarn, e ? 2.0f : 1.5f) && Mathf.Approximately(sy.ArcExtra, e ? 0.5f : 0));
                Check($"Sylvara {lv} P2: lanes every {(e ? 0.5f : 0.35f)} s, second spiral {(e ? "off" : "on")}, {(h ? "double" : "single")} toll",
                    Mathf.Approximately(sy.LanePeriod, e ? 0.5f : 0.35f) && sy.SecondColourSpiral == h && sy.DoubleToll == h && sy.ChoirPods == h && sy.MirrorJets == h);
                Check($"Sylvara {lv} P3: {(h ? "one more bud" : "same buds")}, bloom {(e ? 15 : 12)} s, {(e ? 3 : 4)}-arm spiral",
                    sy.BudCount == budBase + (h ? 1 : 0) && Mathf.Approximately(sy.BloomAfter, e ? 15 : 12) && sy.HollowArms == (e ? 3 : 4) && sy.CounterSpiral == h, $"{sy.BudCount} buds");
                sy.DebugStartStorm(0);
                Check($"Sylvara {lv} P4: loop 1 {(e ? "6 seeds every 2.5" : "8 seeds every 2.0")} s, bursts of {(h ? 10 : 8)}",
                    sy.StormSeeds == (e ? 6 : 8) && Mathf.Abs(sy.StormEvery - (e ? 2.5f : 2.0f)) < 0.001f && sy.BurstPetals == (h ? 10 : 8) && sy.StormArc == h, $"{sy.StormSeeds} / {sy.StormEvery} / {sy.BurstPetals}");
                sy.DebugStartStorm(2);
                Check($"Sylvara {lv} P4: the same enrage steps (+2 seeds, 0.2 s sooner at loop 3)",
                    sy.StormSeeds == (e ? 8 : 10) && Mathf.Abs(sy.StormEvery - (e ? 2.3f : 1.8f)) < 0.001f, $"{sy.StormSeeds} / {sy.StormEvery}");
            if (sy.Presenter && sy.Presenter.Has("StormCall")) { sy.DebugLastStormWarning(); Check("the LAST STORM warning plays StormCall", sy.Clips.Contains("StormCall")); }
                sy.DebugStartStorm(7);
                Check($"Sylvara {lv} P4: Last Storm {(e ? "12 seeds every 0.8 s (softer on Easy)" : "14 seeds every 0.6 s, as on Medium")}",
                    sy.LastStorm && sy.StormSeeds == (e ? 12 : 14) && Mathf.Abs(sy.StormEvery - (e ? 0.8f : 0.6f)) < 0.001f, $"{sy.StormSeeds} / {sy.StormEvery}");
            }
            {
                // Hard extras (5 Nov, done as recommended): more to dodge, not more damage
                d.World.Difficulty = Difficulty.Hard; d.World.Hazards.ClearAll();
                sy.DebugJets();
                int wedges = d.World.Hazards.Live.Count(hz => hz.Source == "Waterfall Jet");
                Check("Sylvara Hard P1: each Waterfall Jet has a mirrored twin with its own warning", sy.MirrorJets && wedges >= 2 && wedges % 2 == 0, wedges + " wedges");
                Check("Sylvara Hard P2: Seed Pods join the Choir at 16.5 s", sy.ChoirPods);
                Check("Sylvara Hard P3: a counter spiral turns under the Hollow Spiral", sy.CounterSpiral);
                Check("Sylvara Hard P4: a Pruning Arc cuts through each Seed Storm loop", sy.StormArc);
                d.World.Hazards.ClearAll();
            }
            {
                // Hard live: a seed bursts into 10 petals. A fresh bullet field, so only Hard seeds take part (seeds still flying
                // from the Medium storm above would mix 8- and 10-ray bursts; review SYL-PREVIEW-TEST, 7 Nov)
                d.World.Bullets.ClearAll(); d.World.Difficulty = Difficulty.Hard; sy.DebugStartStorm(0);
                // burst preview (proposal 7 Nov): each seed carries its burst angle from launch, its last 0.6 s draws the rays,
                // and the petals fly exactly along the previewed rays from the previewed point
                var seeds = new Dictionary<BulletSystem.Bullet, (float off, Vector3 at, int rays)>(); int fresh = 0; bool rayShown = false, seedsCarry = true;
                // LAN (review SYL-PREVIEW-NET): a previewing seed sent through a world frame reaches a client with its rays and fuse
                int netChecked = 0; string netBad = null;
                void NetRoundTrip(BulletSystem.Bullet b)
                {
                    var f = new Networking.LanWorldFrame(); f.Players.Add(new Networking.LanWorldFrame.Player { Id = 0, Hp = 1, MaxHp = 1 });
                    f.Bullets.Add(Networking.LanWorldCapture.BulletOf(b));
                    if (!Networking.LanWorldFrame.TryDecode(f.Encode(), out var got) || got.Bullets.Count != 1) { netBad = "frame did not decode"; return; }
                    var c = new BulletSystem.Bullet(); Networking.LanClientWorld.ApplyBullet(got.Bullets[0], c, 0);
                    bool ok = c.BurstRays == b.BurstRays && Mathf.Abs(Mathf.DeltaAngle(c.BurstOff * Mathf.Rad2Deg, b.BurstOff * Mathf.Rad2Deg)) < 0.01f
                        && Mathf.Approximately(c.BurstSpeed, b.BurstSpeed) && Mathf.Abs((c.Life - c.Age) - (b.Life - b.Age)) < 0.001f && c.Radius >= 0.5f;
                    netChecked++; if (!ok && netBad == null) netBad = $"client {c.BurstRays} rays / {c.Life - c.Age:0.000} s left vs host {b.BurstRays} / {b.Life - b.Age:0.000}";
                }
                bool sampling = true;
                // petals live 6 s: ones from earlier steps are still flying, and their seeds were never sampled
                var older = new HashSet<BulletSystem.Bullet>(d.World.Bullets.Live.Where(b => b.Source == "Seed Burst"));
                IEnumerator SampleSeeds()
                {
                    while (sampling)
                    {
                        foreach (var b in d.World.Bullets.Live)
                            if (b.Source == "Seed" && !b.Dead && !seeds.ContainsKey(b))
                            {
                                // seeds launched on Hard carry 10 rays (older Medium seeds, still in flight, carry 8)
                                if (b.Age < 0.1f) { fresh++; seedsCarry &= b.BurstRays == 10 && Mathf.Approximately(b.BurstSpeed, 4.5f); }
                                seeds[b] = (b.BurstOff, b.Pos + b.Vel * (b.Life - b.Age), Mathf.Max(1, b.BurstRays));
                            }
                        rayShown |= GameObject.Find("Burst Ray") != null;
                        if (netChecked < 3) foreach (var b in d.World.Bullets.Live) if (b.Source == "Seed" && !b.Dead && b.Life - b.Age <= BulletSystem.PreviewFor) { NetRoundTrip(b); break; }
                        yield return null;
                    }
                }
                StartCoroutine(SampleSeeds());
                yield return Play(4.5f, () => false);   // the first ring comes at 2.0 s and bursts 1.6 s later
                sampling = false;
                Check("Sylvara Hard P4: a seed bursts into 10 petals", sy.LastBurstFired == 10, sy.LastBurstFired.ToString());
                Check("Sylvara P4 burst preview: every seed carries its 10 burst rays from launch", fresh > 0 && seedsCarry, $"{fresh} seeds");
                Check("Sylvara P4 burst preview: the rays are drawn before the burst", rayShown);
                Check("Sylvara P4 burst preview: a LAN client gets each seed's rays and fuse (world frame round trip)", netChecked > 0 && netBad == null, netBad ?? $"{netChecked} seeds");
                // the drawn track is the petal's real hit corridor: full petal width, from the burst point past the 0.4 s reach
                Check("Sylvara P4 burst preview: each ray track is a petal's full hit width (0.6 m), end caps included",
                        Mathf.Approximately(BulletSystem.PreviewRayWidth, 2 * BulletArt.PetalHitRadius), $"{BulletSystem.PreviewRayWidth:0.00} m");
                {
                    // SYL-PREVIEW-COLD: Sylvara's Ready warm-up builds the bars and materials, so the first preview builds none
                    var probeRoot = new GameObject("Prewarm Probe").transform; var probe = new BulletSystem(d.World);
                    var sylEntry = RunDirector.Bosses.First(e => e.Map == RunDirector.SylvaraMap && e.InRun);
                    probe.Prewarm(probeRoot); int built = probe.PreviewBars;
                    Check("Sylvara P4 burst preview: the ray bars are built on the Ready screen (Sylvara's warm-up), before the fight",
                        sylEntry.WarmUp != null && built >= BulletSystem.PrewarmBars, $"{built} bars");
                    UnityEngine.Object.Destroy(probeRoot.gameObject);
                }
                int petals = 0, onRay = 0; var miss = new List<string>();
                foreach (var p in d.World.Bullets.Live)
                {
                    if (p.Source != "Seed Burst" || p.Dead || older.Contains(p)) continue;
                    petals++;
                    var start = p.Pos - p.Vel * p.Age; float pa = Mathf.Atan2(p.Vel.z, p.Vel.x);
                    foreach (var s in seeds.Values)
                    {
                        float k = (pa - s.off) / (Mathf.PI * 2 / s.rays), dk = Mathf.Abs(k - Mathf.Round(k));
                        var from = s.at + new Vector3(Mathf.Cos(pa), 0, Mathf.Sin(pa)) * 0.3f;
                        if (dk < 0.01f && Sim.Flat(start - from).magnitude < 0.15f) { onRay++; goto matched; }
                    }
                    {
                        // unmatched: the nearest predicted burst point and the angle error there (diagnostics)
                        float bd = float.MaxValue, berr = 0; int br = 0; float age = p.Age;
                        foreach (var s in seeds.Values)
                        {
                            float dd = Sim.Flat(start - (s.at + new Vector3(Mathf.Cos(pa), 0, Mathf.Sin(pa)) * 0.3f)).magnitude;
                            if (dd < bd) { bd = dd; float k = (pa - s.off) / (Mathf.PI * 2 / s.rays); berr = Mathf.Abs(k - Mathf.Round(k)) * 360f / s.rays; br = s.rays; }
                        }
                        if (miss.Count < 5) miss.Add($"{bd:0.00} m / {berr:0.0} deg / {br} rays / age {age:0.00} / r {Sim.Flat(start).magnitude:0.0}");
                    }
                    matched:;
                }
                Check("Sylvara P4 burst preview: the petals fly along the previewed rays from the previewed point", petals > 0 && onRay == petals, $"{onRay} of {petals}" + (miss.Count > 0 ? "; misses: " + string.Join("; ", miss) : ""));
            }
            d.World.Difficulty = Difficulty.Medium; sy.DebugStartStorm(7);

            d.World.Bullets.Hit -= onBullet; d.World.PlayerHit -= onHit;
            Check("Sylvara: each bullet hits at most once", bulletOnce);
            Check("Sylvara: bullets deal damage with no stun or knockback", bulletNoStun);
            Check("Sylvara: coloured bullets only hit their own colour", colourOk);
            Check("Sylvara: each ground or arc hazard hits at most once (R16)", hitsPerInstance.Values.All(n => n == 1), $"{hitsPerInstance.Count} instances hit");
            Note($"Sylvara v3: bot took {taken} damage ({bulletHits} bullet hits, {hitsPerInstance.Count} hazard hits), healed by the test; peak {maxBullets} bullets on screen, {d.World.Bullets.Fired} fired");
            // attack icons (approved 2 Nov): every hit she dealt names its attack, and that attack has an icon
            {
                var w = d.World; var hits = Enumerable.Range(0, w.TakenLog.Count).Where(i => w.TakenLog[i].source != "Test Bullet").ToList();
                var bare = hits.Where(i => w.TakenAttackIds[i] == null).Select(i => w.TakenLog[i].source).Distinct().ToList();
                var ids = hits.Select(i => w.TakenAttackIds[i]).Where(id => id != null).Distinct().ToList();
                Check("Sylvara: every hit carries her attack ID", hits.Count > 0 && bare.Count == 0, bare.Count == 0 ? $"{hits.Count} hits" : "no ID: " + string.Join(", ", bare));
                Check("Sylvara: each attack hit has its icon and name", ids.All(id => BossAttackIcons.Icon(id) != null && BossAttackIcons.Get(id).bossName == "Sylvara"), string.Join(", ", ids));
            }

            yield return KillDuringWindup("sylvara");   // Last Storm is still running: the watcher keeps the bot alive until she dies
            watching = false;
            Check("sylvara: boss death clears every bullet", d.World == null || d.World.Bullets.Count == 0);
            yield return Until(() => d.State == RunState.Results, 8, "Sylvara practice results");
            Check("Sylvara practice clear ends in a practice result", d.Victory && d.Mode == RunMode.Practice);
            Check("Sylvara: no bud props left after defeat", sy == null || sy.PropCount == 0, sy ? sy.PropCount + " left" : "");
            d.ReturnToMenu(); yield return null;
            yield return SylvaraBotChecks();
        }

        // Sylvara with three bot allies (the menu's Allies row): they spawn, dodge the bullet hell and cut buds by themselves
        IEnumerator SylvaraBotChecks()
        {
            int keep = RunParty.Selected;
            Check("the menu sets the ally count", d.SelectAllies(3) && RunParty.Selected == 3);
            Check("Sylvara practice with 3 allies starts", d.StartPractice("Tollbreaker", RunDirector.SylvaraPractice));
            yield return Until(() => RunDirector.Instance && RunDirector.Instance.State == RunState.Ready, 30, "Verdant Chimes loaded for the bot fight");
            if (RunDirector.Instance != d) { directorLog.AddRange(d.Log); d = RunDirector.Instance; d.InputOverride = bot; bot.Clear(); }
            if (d.Player) d.Player.Input = bot;
            d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 4, "bot fight combat");
            var sy = d.Boss as SylvaraEncounter;
            Check("three bot allies fight beside the player", d.Allies.Count == 3 && d.Allies.All(a => a.Input is AllyBot), d.Allies.Count.ToString());
            if (sy == null || d.Allies.Count == 0) { RunParty.Selected = keep; yield break; }
            var hits = new Dictionary<PlayerController, int>(); int downs = 0;
            var budGap = new Dictionary<PlayerController, float>();
            Action<PlayerController, BulletSystem.Bullet, int> onBullet = (p, b, dmg) => { if (p != d.Player) hits[p] = (hits.TryGetValue(p, out int n) ? n : 0) + 1; };
            d.World.Bullets.Hit += onBullet;
            var report = new List<string>();
            float[] at = { 1f, 0.74f, 0.49f, 0.24f };
            for (int ph = 0; ph < 4; ph++)
            {
                if (ph > 0) { d.Boss.DebugSetHpFraction(at[ph]); yield return Wait(2f); }
                hits.Clear();
                float t0 = Time.time, seconds = 20f;
                while (Time.time - t0 < seconds && d.State == RunState.Combat)
                {
                    bot.Clear(); d.Player.transform.position = new Vector3(0, 0, -CombatWorld.PlayerBound + 0.5f);
                    d.Player.DebugSetHp(PlayerController.MaxHp);   // the test player stands aside; only the bots play
                    foreach (var a in d.Allies) if (a.Down) { downs++; a.DebugRevive(); }
                    if (ph == 2) foreach (var a in d.Allies) foreach (var bp in sy.AliveBudPositions()) { float g = Sim.Flat(a.transform.position - bp).magnitude; if (!budGap.TryGetValue(a, out float o) || g < o) budGap[a] = g; }
                    yield return null;
                }
                int n = hits.Values.Sum();
                report.Add($"{sy.PhaseName}: {n} bullet hits on 3 bots in {seconds:0} s ({n * 60f / seconds / 3:0} per bot per minute)");
                if (ph == 2) Note("Sylvara bots, closest to a bud: " + string.Join(", ", d.Allies.Select(a => $"{a.ClassName} {(budGap.TryGetValue(a, out float g) ? g.ToString("0.0") + " m" : "none")} [{(a.Input as AllyBot)?.DebugLine}]")));
                if (ph == 2) Check("bots cut Choir Buds by themselves", sy.BudsCut > 0, $"{sy.BudsCut} cut, {sy.BudsBloomed} bloomed");
            }
            d.World.Bullets.Hit -= onBullet;
            Note("Sylvara bots: " + string.Join(" · ", report) + $" · downs {downs}");
            Check("Sylvara bots survive every phase (no downs in 80 s)", downs == 0, downs + " downs");
            d.Boss.DebugDefeat();
            yield return Until(() => d.State == RunState.Results, 8, "bot fight results");
            d.ReturnToMenu(); yield return null;
            RunParty.Selected = keep;
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
