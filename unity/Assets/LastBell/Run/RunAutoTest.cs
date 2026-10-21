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
    public sealed class RunAutoTest : MonoBehaviour
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
        string dir;

        void Start()
        {
            d = GetComponent<RunDirector>(); dir = OutDir; Directory.CreateDirectory(dir);
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
                if (h.Shape == HazardShape.Stadium)
                {
                    var l = Quaternion.Euler(0, -h.Yaw, 0) * Sim.Flat(pos - h.Origin); var core = new Vector3(Mathf.Clamp(l.x, -h.Segment, h.Segment), 0, 0);
                    var outward = Quaternion.Euler(0, h.Yaw, 0) * (l - core).normalized; float s = (l - core).magnitude;
                    return s - h.Inner < h.Radius - s ? -outward : outward;
                }
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

        // ------------------------------------------------------------ the scenario
        IEnumerator Main()
        {
            Note($"Last Bell autotest · {Application.version} · Unity {Application.unityVersion} · {SystemInfo.graphicsDeviceName} · {DateTime.Now:yyyy-MM-dd HH:mm}");
            d.InputOverride = bot;
            yield return Wait(1.0f);
            Check("boots into the menu in the arena scene", d.State == RunState.Menu && FindAnyObjectByType<LastBell.Arenas.KeyArtArena>() != null);
            yield return Shot("menu");
            foreach (var cls in RunDirector.Classes)
            {
                int slots = cls == "Vanguard" ? 5 : 6, found = 0;
                for (int i = 0; i < slots; i++) if (RunHud.SlotIcon(cls, i) != null) found++;
                Check($"{cls} skill-bar icons load ({slots} incl. dash)", found == slots, $"{found}/{slots}");
            }
            Check("potion icon loads", RunHud.Icon("potion") != null);

            // -lastbellOnly twins: just the Escapement Twins block-out section (quick iteration)
            if (OnlyTwins)
            {
                yield return TwinsChecks();
                Check("no errors or exceptions logged", errors == 0, errors + " logged");
                Finish(); yield break;
            }

            // ---------- run 1: Vanguard, full victory, repeated presses ----------
            Check("StartRun accepted once", d.StartRun("Vanguard") && !d.StartRun("Vanguard"));
            yield return Wait(0.6f);
            yield return Shot("ready_vanguard");
            int starts0 = d.EncounterStarts;
            for (int i = 0; i < 4; i++) { d.RequestReady(); d.RequestReady(); yield return null; }
            yield return Until(() => d.State == RunState.Combat, 4, "combat 1");
            yield return Wait(0.3f);
            Check("repeated Ready starts exactly one encounter", d.EncounterStarts == starts0 + 1 && d.Encounter == 0, $"starts {d.EncounterStarts - starts0}");
            Check("encounter 1 is the Dial Warden", d.Boss is DialWarden);
            int bossHp0 = d.Boss.Hp;
            bool sawOpening = false; var vk = d.Player.Kit as VanguardKit2;
            float t0 = Time.time;
            var fight = StartCoroutine(Fight(7f, VanguardRotation, 1.6f));
            bool shot = false;
            while (Time.time - t0 < 7f && d.State == RunState.Combat) { sawOpening |= vk.OpeningActive; if (Time.time - t0 > 3.5f && !shot) { shot = true; yield return Shot("combat_dial_warden"); } yield return null; }
            StopCoroutine(fight); bot.Clear();
            Check("Vanguard strikes damage the boss through the kit", d.Boss.Hp < bossHp0, $"{bossHp0} -> {d.Boss.Hp}");
            Check("Steel Rhythm hit 3 granted Opening", sawOpening);
            yield return VanguardShowcase();
            yield return VanguardKitChecks();
            // potions: drink one in encounter 1; it must still be spent in encounter 2 while health is full again
            yield return Until(() => d.World.Hazards.Count == 0, 6, "quiet for the potion check"); d.Boss.Halt();
            yield return Until(() => d.Player.Stunned == 0, 2, "hit-stun over");   // drinking is blocked while stunned
            d.Player.DebugSetHp(50); bot.potion = true; yield return Wait(0.1f);
            Check("F potion heals +40 and spends one", d.Player.Hp == 90 && d.Player.Potions == PlayerController.MaxPotions - 1, $"hp {d.Player.Hp}, potions {d.Player.Potions}");
            bot.potion = true; yield return Wait(0.1f);
            Check("potion lockout: no second drink within 1 s", d.Player.Potions == PlayerController.MaxPotions - 1);
            d.Player.DebugSetHp(37); d.Boss.BeginCombat();
            yield return KillDuringWindup("dial_warden");
            yield return Until(() => d.State == RunState.Intro && d.Encounter == 1, 6, "intro 2");
            Check("encounter 1 ended exactly once", d.EncounterEnds == 1 && d.EncountersCleared == 1);
            yield return null;   // Destroy lands at the end of the frame
            Check("previous boss removed", FindObjectsByType<BossEncounter>().Length == 1);
            yield return Until(() => d.State == RunState.Combat, 4, "combat 2");
            Check("encounter 2 is the Pendulum Sentinel", d.Boss is PendulumSentinel);
            Check("health refilled before miniboss 2; potions carried over", d.Player.Hp == PlayerController.MaxHp && d.Player.Potions == PlayerController.MaxPotions - 1, $"hp {d.Player.Hp}, potions {d.Player.Potions}");
            yield return LowPendulumCheck();
            yield return KillDuringWindup("pendulum_sentinel");
            yield return Until(() => d.State == RunState.Combat && d.Encounter == 2, 10, "combat 3");
            Check("encounter 3 is the Chimebound Oracle", d.Boss is ChimeboundOracle);
            yield return Survive(2.2f);
            yield return Shot("combat_oracle_markers");
            yield return KillDuringWindup("chimebound_oracle");
            yield return Until(() => d.State == RunState.Intermission, 6, "intermission");
            yield return Wait(1.5f);
            Check("intermission is free of hazards and bosses", d.World.Hazards.Count == 0 && d.Boss == null && !d.World.CombatActive);
            int potInter = d.Player.Potions;
            Check("potions still carried at the intermission", potInter == PlayerController.MaxPotions - 1, "potions " + potInter);
            yield return Shot("intermission");
            int starts1 = d.EncounterStarts;
            for (int i = 0; i < 4; i++) { d.RequestContinue(); d.RequestContinue(); d.RequestReady(); yield return null; }
            yield return Until(() => d.State == RunState.Combat, 4, "final combat");
            Check("repeated Continue starts exactly one final encounter", d.EncounterStarts == starts1 + 1 && d.Boss is BellkeeperEncounter);
            Check("health and potions refilled for the final boss", d.Player.Hp == PlayerController.MaxHp && d.Player.Potions == PlayerController.MaxPotions, $"hp {d.Player.Hp}, potions {d.Player.Potions}");
            d.Player.DebugSetHp(PlayerController.MaxHp);   // test setup: the rest of this run checks phases, not survival
            var bk = (BellkeeperEncounter)d.Boss;
            Check("Bellkeeper uses its model", bk.Presenter != null && bk.Presenter.HasModel);
            StartCoroutine(Capture("bellkeeper_fight", 300));
            fight = StartCoroutine(Fight(4.5f, VanguardRotation, 1.8f));
            yield return Wait(2.5f); yield return Shot("combat_bellkeeper"); yield return Wait(2.2f);
            StopCoroutine(fight); bot.Clear();
            // The Unfinished Hour (spec v0.3 §3.1, §4.1): phase 1 records one footprint, then replays it alone
            yield return Survive(8, () => bk.HasMemory);
            Check("phase 1 records a footprint and shows its inactive outline", bk.HasMemory);
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
            d.Boss.DebugSetHpFraction(0.34f);
            d.Player.DebugSetHp(PlayerController.MaxHp);
            yield return Survive(22, () => d.Boss.PhaseName == BellkeeperEncounter.Phase3);
            Check("phase 3 queued and entered", d.Boss.PhaseName == BellkeeperEncounter.Phase3);
            yield return Survive(2.5f); yield return Shot("bellkeeper_final_toll");
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
            yield return Until(() => d.State == RunState.Results, 6, "results 1");
            Check("victory after the fourth encounter", d.Victory && d.EncountersCleared == 4 && d.EncounterStarts == 4 && d.EncounterEnds == 4,
                $"victory {d.Victory}, cleared {d.EncountersCleared}, starts {d.EncounterStarts}, ends {d.EncounterEnds}");
            yield return Shot("results_victory");

            // ---------- retry after victory (spam) ----------
            int runs0 = d.RunsStarted;
            for (int i = 0; i < 4; i++) { d.RequestRetry(); d.RequestRetry(); yield return null; }
            yield return Wait(0.5f);
            Check("repeated Retry starts exactly one fresh run", d.RunsStarted == runs0 + 1 && d.State == RunState.Ready && d.Encounter == 0, $"runs +{d.RunsStarted - runs0}, state {d.State}");
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
            yield return ChimesingerChecks();
            for (int e = 0; e < 4; e++)
            {
                if (e == 3) { yield return Until(() => d.State == RunState.Intermission, 8, "intermission (chimesinger)"); d.RequestContinue(); }
                yield return Until(() => d.State == RunState.Combat, 8, "combat " + (e + 1));
                if (d.State != RunState.Combat) break;
                chimeStep = 0; d.Player.DebugSetHp(PlayerController.MaxHp);
                var f2 = StartCoroutine(Fight(e == 3 ? 4f : 2.5f, ChimeRotation, 6f));
                yield return Wait(e == 3 ? 4f : 2.5f); StopCoroutine(f2); bot.Clear();
                if (e == 3) yield return Shot("chimesinger_bellkeeper");
                d.Boss.DebugDefeat();
                yield return Until(() => d.State != RunState.Combat, 2, "boss defeated " + (e + 1));
            }
            yield return Until(() => d.State == RunState.Results, 8, "results 4");
            Check("Chimesinger run reaches victory", d.Victory && d.EncountersCleared == 4);
            d.ReturnToMenu(); yield return null;

            // ---------- run 5: Arcanist rotation and full victory ----------
            d.StartRun("Arcanist");
            yield return Wait(0.4f); yield return Shot("ready_arcanist");
            Check("Arcanist uses its model", d.Player.Presenter.HasModel);
            d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 4, "arcanist combat");
            yield return ArcanistChecks();
            for (int e = 0; e < 4; e++)
            {
                if (e == 3) { yield return Until(() => d.State == RunState.Intermission, 8, "intermission (arcanist)"); d.RequestContinue(); }
                yield return Until(() => d.State == RunState.Combat, 8, "arcanist combat " + (e + 1));
                if (d.State != RunState.Combat) break;
                d.Player.DebugSetHp(PlayerController.MaxHp);
                var f3 = StartCoroutine(Fight(e == 3 ? 4f : 2f, ArcRotation, 6f));
                yield return Wait(e == 3 ? 4f : 2f); StopCoroutine(f3); bot.Clear();
                if (e == 3) yield return Shot("arcanist_bellkeeper");
                d.Boss.DebugDefeat();
                yield return Until(() => d.State != RunState.Combat, 2, "arcanist boss defeated " + (e + 1));
            }
            yield return Until(() => d.State == RunState.Results, 8, "results 5");
            Check("Arcanist run reaches victory", d.Victory && d.EncountersCleared == 4);
            d.ReturnToMenu(); yield return null;

            // ---------- practice ----------
            d.StartPractice("Vanguard", 3);
            d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 4, "practice combat");
            Check("practice starts the chosen encounter", d.Boss is BellkeeperEncounter && d.Mode == RunMode.Practice);
            yield return Wait(1f);
            d.Boss.DebugDefeat();
            yield return Until(() => d.State == RunState.Results, 6, "practice results");
            Check("practice clear ends in a practice result, not a run victory", d.Mode == RunMode.Practice && d.EncountersCleared == 1);
            d.RequestRetry();
            Check("practice retry repeats the same encounter", d.State == RunState.Ready && d.Encounter == 3 && d.Mode == RunMode.Practice);
            d.ReturnToMenu();

            yield return TwinsChecks();

            // ---------- report ----------
            Check("no errors or exceptions logged", errors == 0, errors + " logged");
            Finish();
        }

        static bool OnlyTwins
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.FindIndex(args, a => a.Equals("-lastbellOnly", StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i + 1 < args.Length && args[i + 1].Equals("twins", StringComparison.OrdinalIgnoreCase);
            }
        }

        // Escapement Twins block-out (docs/design/escapement-twins.md): stadium body, the self-strike, the queued 50 %
        // transition, Deadbeat's corridor and Rebuttal Pair's one-cone-at-a-time rule, all from real hazards.
        IEnumerator TwinsChecks()
        {
            // the menu is only reachable from results (or pause): finish whatever practice the earlier section left ready
            if (d.State == RunState.Ready)
            {
                d.RequestReady();
                yield return Until(() => d.State == RunState.Combat, 6, "leftover practice combat");
                d.Boss?.DebugDefeat();
                yield return Until(() => d.State == RunState.Results, 8, "leftover practice results");
            }
            if (d.State != RunState.Menu) d.ReturnToMenu();
            yield return Until(() => d.State == RunState.Menu, 4, "menu before Twins practice");
            yield return null;
            Check("Twins practice accepted from the menu", d.StartPractice("Vanguard", RunDirector.PracticeEscapementTwins));
            yield return Wait(0.3f);
            d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 6, "Twins practice combat");
            var t = d.Boss as EscapementTwins;
            Check("practice starts the Escapement Twins at the arena centre", t != null && Sim.Flat(t.transform.position).magnitude < 0.01f);
            if (t == null) yield break;

            // stadium body: a point inside the tower end is pushed out; a short swing at the tower end connects
            var pushed = d.World.Constrain(t.transform.TransformPoint(new Vector3(2.0f, 0, 0.1f)), 0.3f);
            Check("stadium body pushes players out at the tower ends", t.BodyDistance(pushed) >= 0.29f, $"body distance {t.BodyDistance(pushed):0.00}");
            var endPos = t.transform.TransformPoint(new Vector3(3.1f, 0, 0));
            int hp0 = t.Hp;
            d.World.StrikeBoss(d.Player, endPos, -t.transform.right, 1.0f, 90, 1, "test");
            Check("a 1 m swing at the tower end hits the body (not the centre circle)", t.Hp == hp0 - 1, $"boss hp {hp0} -> {t.Hp}");

            // self-strike: stand on the bearing from the Sun tower through the Moon pawl, let the lane lock, sidestep
            var sender = t.transform.TransformPoint(new Vector3(-EscapementTwins.TowerX, 0, 0));
            var pawl = t.Pawl(false);
            var u = Sim.Flat(pawl - sender).normalized;
            var bait = pawl + u * 2.0f;
            d.Player.PrepareForEncounter(bait, 0);
            Check("bait spot is outside the body", t.BodyDistance(bait) > 0.4f, $"{t.BodyDistance(bait):0.00} m");
            yield return Until(() => d.World.Hazards.Live.Any(h => h.Source == "Sun Verdict"), 3, "Sun Verdict warning");
            var lane = d.World.Hazards.Live.FirstOrDefault(h => h.Source == "Sun Verdict");
            if (lane == null) yield break;
            yield return Until(() => lane.Locked, 2, "Sun Verdict lock");
            yield return Shot("twins_verdict_bait");
            var side = -Vector3.Cross(Vector3.up, Sim.Dir(lane.Yaw));
            if (t.BodyDistance(d.Player.transform.position + side * 1.6f) < 0.3f) side = -side;
            int php = d.Player.Hp; StartCoroutine(Capture("twins_self_strike", 330));
            float w = 0; while (w < 0.45f) { bot.MoveWorld = side; w += Time.deltaTime; yield return null; }
            bot.Clear();
            yield return Until(() => t.Current == "jammed" || t.Current == "recovery", 3, "Verdict resolve");
            Check("Verdict across the far pawl jams the boss", t.SelfStrikes == 1 && t.Current == "jammed", $"self-strikes {t.SelfStrikes}, now {t.Current}");
            Check("a jammed cast recovers for 4.0 s", t.RecoveryTicks == EscapementTwins.JamRecovery, $"{t.RecoveryTicks} ticks");
            Check("sidestepping after lock avoids the lane", d.Player.Hp == php, $"hp {php} -> {d.Player.Hp}");
            Check("a lane aimed straight ahead from the Sun tower misses the Moon pawl",
                !t.CrossesPawl(sender, t.transform.eulerAngles.y, pawl, t.PawlRadius) && t.CrossesPawl(sender, Sim.YawTo(sender, pawl, 0), pawl, t.PawlRadius));
            yield return Wait(1.0f);
            yield return Shot("twins_jammed");
            yield return Survive(3.5f, () => t.Current == "Moon Rebuttal");
            if (t.Current == "Moon Rebuttal") yield return Shot("twins_moon_rebuttal");
            yield return Survive(9f, () => t.Current == "Common Measure");
            if (t.Current == "Common Measure") { yield return Wait(0.8f); yield return Shot("twins_common_measure"); }

            // queued 50 % transition: only after the current attack, with nothing warning
            d.Player.DebugSetHp(PlayerController.MaxHp);
            t.DebugSetHpFraction(0.49f);
            yield return Survive(16f, () => t.Phase == 2);
            Check("50 % queues one transition at an attack boundary, nothing warning", t.Phase == 2 && t.Current == "transition" && d.World.Hazards.Count == 0 && t.PhaseName == EscapementTwins.Phase2,
                $"phase {t.Phase}, {t.Current}, {d.World.Hazards.Count} hazards");
            yield return Wait(1.6f);
            yield return Shot("twins_phase2");

            // Deadbeat: stand in the central corridor in front of the ratchet and take nothing
            yield return Survive(16f, () => t.Current == "Deadbeat");
            if (t.Current == "Deadbeat")
            {
                d.Player.PrepareForEncounter(t.transform.position + t.transform.forward * 3.2f, t.transform.eulerAngles.y + 180);
                int dh = d.Player.Hp; yield return Wait(0.5f); yield return Shot("twins_deadbeat_corridor");
                yield return Until(() => t.Current != "Deadbeat", 4, "Deadbeat resolve");
                Check("Deadbeat's central corridor is safe", d.Player.Hp == dh, $"hp {dh} -> {d.Player.Hp}");
            }
            else Check("Deadbeat came up in Phase 2", false, "now " + t.Current);

            // Rebuttal Pair: cone 2 is an outline until cone 1 is spent; never two live cones
            yield return Survive(30f, () => t.Current == "Rebuttal Pair");
            bool sawSecond = false, overlap = false, shot = false;
            while (t.Current == "Rebuttal Pair" && d.State == RunState.Combat)
            {
                var live = d.World.Hazards.Live;
                bool one = live.Any(h => h.Source == "Rebuttal Pair (1)"), two = live.Any(h => h.Source == "Rebuttal Pair (2)");
                if (two) sawSecond = true; if (one && two) overlap = true;
                if (one && !shot && live[0].Age > 40) { shot = true; StartCoroutine(Shot("twins_rebuttal_pair_preview")); }
                var esc = Escape(d.Player); bot.MoveWorld = esc;
                yield return null;
            }
            bot.Clear();
            Check("Rebuttal Pair: cone 2 goes live only after cone 1 resolves", sawSecond && !overlap, $"second {sawSecond}, overlap {overlap}");

            // boss death during a windup cancels everything
            d.Player.DebugSetHp(PlayerController.MaxHp);
            yield return KillDuringWindup("Escapement Twins");
            yield return Until(() => d.State == RunState.Results, 8, "Twins practice results");
            Check("Twins practice clear ends in a practice result", d.Mode == RunMode.Practice && d.Victory);
            d.ReturnToMenu(); yield return null;
        }

        void FreshRunChecks(string when)
        {
            var p = d.Player;
            Check($"fresh run {when}: full health/stamina/potions, encounter 1", p.Hp == PlayerController.MaxHp && p.Potions == PlayerController.MaxPotions && Mathf.Approximately(p.Stamina, PlayerController.MaxStamina) && d.Encounter == 0 && d.EncountersCleared == 0);
            bool cdsClear = true; for (int i = 0; i < p.Kit.SlotNames.Length; i++) cdsClear &= p.Kit.Cooldown01(i) <= 0;
            Check($"fresh run {when}: cooldowns and class state reset", cdsClear && string.IsNullOrEmpty(p.Kit.ClassState()) || (p.Kit is ChimesingerKit ck && ck.Harmony == 0 && !ck.Bell));
            Check($"fresh run {when}: no stale bosses, hazards or projectiles", FindObjectsByType<BossEncounter>().Length == 0 && d.World.Hazards.Count == 0 && d.World.Projectiles.Count == 0 && d.Boss == null);
            Check($"fresh run {when}: one player only", FindObjectsByType<PlayerController>().Length == 1);
        }

        // Low Pendulum height rule: standing in the lane and jumping through the resolve avoids it
        IEnumerator LowPendulumCheck()
        {
            yield return Until(() => d.World.Hazards.Live.Any(h => h.JumpClearable), 14, "Low Pendulum warning");
            var bar = d.World.Hazards.Live.FirstOrDefault(h => h.JumpClearable);
            if (bar == null) yield break;
            // put the player in the middle of the bar (test setup) and jump so the resolve tick lands mid-air
            d.Player.transform.position = bar.Origin + Sim.Dir(bar.Yaw) * Mathf.Min(4f, bar.Length * 0.5f);
            int hp0 = d.Player.Hp; bool jumped = false, capStarted = false;
            yield return Shot("low_pendulum_jump_warning");
            while (d.World.Hazards.Live.Contains(bar))
            {
                if (!capStarted && bar.ResolveAt - bar.Age <= 50) { capStarted = true; StartCoroutine(Capture("low_pendulum_jump", 110)); }
                if (!jumped && bar.ResolveAt - bar.Age <= 16) { bot.jump = true; jumped = true; }
                yield return null;
            }
            yield return Wait(0.3f);
            Check("jumping through the Low Pendulum avoids it", d.Player.Hp == hp0, $"hp {hp0} -> {d.Player.Hp}");
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

        void Finish()
        {
            lines.Insert(0, $"RESULT: {(fail == 0 ? "ALL PASSED" : "FAILURES")} · {pass} passed, {fail} failed · errors logged: {errors}");
            lines.Add(""); lines.Add("Director transitions:"); lines.AddRange(d.Log.Select(l => "  " + l));
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
