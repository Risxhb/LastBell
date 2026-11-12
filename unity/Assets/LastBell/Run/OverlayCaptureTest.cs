using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LastBell.Menu;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace LastBell.Run
{
    // Captures of the run overlays (UI sub-thread B, docs/plans/ui-uniformity-plan-20261110.md): a campaign run without
    // travel, through the real launch path, saving the Ready card, Pause, the final intermission shop, the downed card,
    // defeat Results with the death recap, the LAN client's Results and victory Results.
    //   LastBell.exe -lastbellOverlayTest -lastbellShop -lastbellOut <dir> [-lastbellOverlayPad]
    // -lastbellOverlayPad adds the controller-only walkthrough: a virtual gamepad readies up, works Pause (wrap, A, B),
    // buys and readies in the shop and retries from Results; nothing else drives those screens.
    // Fights are ended with the dev defeat; the defeat's hits are injected. Writes overlay-report.txt; exits 0 when every
    // check passed.
    public sealed class OverlayCaptureTest : MonoBehaviour
    {
        static bool Arg(string a) => Environment.GetCommandLineArgs().Any(x => x.Equals(a, StringComparison.OrdinalIgnoreCase));
        public static bool Requested => Arg("-lastbellOverlayTest");
        static bool PadWalk => Arg("-lastbellOverlayPad");
        static string OutDir
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.FindIndex(args, a => a.Equals("-lastbellOut", StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Application.persistentDataPath, "overlay-test");
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Requested || FindAnyObjectByType<OverlayCaptureTest>()) return;
            var go = new GameObject("Overlay capture test"); DontDestroyOnLoad(go); go.AddComponent<OverlayCaptureTest>();
        }

        readonly List<string> lines = new List<string>();
        int pass, fail, errors, shots; string dir, res;
        RunDirector d;
        Keyboard keyboard; Gamepad pad; InputSettings oldSettings, testSettings;

        void Start()
        {
            dir = OutDir; Directory.CreateDirectory(dir);
            Application.logMessageReceived += OnLog;
            Application.runInBackground = true;
            StartCoroutine(Main());
        }
        void OnDestroy() { Application.logMessageReceived -= OnLog; }
        void OnLog(string msg, string stack, LogType t) { if (t == LogType.Exception || t == LogType.Error) { errors++; if (errors < 30) lines.Add("      LOG " + t + ": " + msg); } }
        void Check(string name, bool ok, string detail = "")
        {
            if (ok) pass++; else fail++;
            var line = $"{(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : "  — " + detail)}";
            lines.Add(line); Debug.Log("[overlay-test] " + line);
        }
        void Note(string s) { lines.Add("      " + s); Debug.Log("[overlay-test] " + s); }
        // the size asked for on the command line (-screen-width / -screen-height): the menu restores the saved resolution
        // at launch, so the test sets it again (and before every picture, in case a scene load restored it)
        static int ArgInt(string a, int fallback)
        {
            var args = Environment.GetCommandLineArgs(); int i = Array.FindIndex(args, x => x.Equals(a, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int v) ? v : fallback;
        }
        static readonly int wantW = ArgInt("-screen-width", 1600), wantH = ArgInt("-screen-height", 900);
        IEnumerator Size()
        {
            if (Screen.width == wantW && Screen.height == wantH) yield break;
            MenuFlowTest.SetResolution(wantW, wantH);
            float t = 0; while ((Screen.width != wantW || Screen.height != wantH) && t < 3) { t += Time.unscaledDeltaTime; yield return null; }
            yield return null; yield return null;
            if (Screen.width != wantW || Screen.height != wantH) Note($"window is {Screen.width}x{Screen.height}, asked {wantW}x{wantH}");
        }
        IEnumerator Shot(string name)
        {
            if (Application.isBatchMode) yield break;
            yield return Size();
            yield return null; yield return new WaitForEndOfFrame();
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, $"{++shots:00}_{name}_{Screen.width}x{Screen.height}.png"), tex.EncodeToPNG()); Destroy(tex);
        }
        IEnumerator Until(Func<bool> cond, float seconds, string what)
        {
            float t = 0;
            while (t < seconds)
            {
                d = RunDirector.Instance; if (d && cond()) yield break;
                t += Time.unscaledDeltaTime; yield return null;
            }
            Check("in time: " + what, false, $"{seconds}s, state {(d ? d.State.ToString() : "no director")}");
        }
        IEnumerator Wait(float s) { float t = 0; while (t < s) { t += Time.unscaledDeltaTime; yield return null; } }
        static bool ShopShowing => Shop.RunShopFlow.Showing;
        static Shop.ShopRun ShopRun => Shop.RunShopFlow.Instance ? Shop.RunShopFlow.Instance.Run : null;

        // ---------------------------------------------------------------- virtual devices (pad walkthrough only)
        void AddDevices()
        {
            oldSettings = InputSystem.settings; oldSettings.hideFlags = HideFlags.DontUnloadUnusedAsset;
            testSettings = Instantiate(oldSettings); testSettings.hideFlags = HideFlags.DontUnloadUnusedAsset;
            testSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; InputSystem.settings = testSettings;
            keyboard = InputSystem.AddDevice<Keyboard>("OverlayKeyboard"); pad = InputSystem.AddDevice<Gamepad>("OverlayGamepad");
            MenuInput.PollDevices = new InputDevice[] { keyboard, pad };
        }
        void RemoveDevices()
        {
            if (pad == null) return;
            MenuInput.PollDevices = null;
            InputSystem.RemoveDevice(keyboard); InputSystem.RemoveDevice(pad); pad = null;
            if (oldSettings) InputSystem.settings = oldSettings; if (testSettings) Destroy(testSettings);
        }
        IEnumerator Pad(GamepadButton b)
        {
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(b)); yield return null; yield return null;
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null; yield return null;
        }
        IEnumerator Pads(GamepadButton b, int n) { for (int i = 0; i < n; i++) yield return Pad(b); }

        // ---------------------------------------------------------------- the run
        IEnumerator Main()
        {
            yield return Wait(1.5f);
            yield return Size();
            res = $"{Screen.width}x{Screen.height}";
            Note("resolution " + res + (PadWalk ? ", controller walkthrough" : ""));
            Campaign.Travel = false;
            Check("shop enabled (-lastbellShop)", Shop.RunShopFlow.Enabled);
            if (PadWalk) { AddDevices(); yield return Pad(GamepadButton.North); }   // last device: gamepad, so the hints show pad glyphs
            Check("Start Run's launch accepted", MenuLaunch.Normal("Arcanist"));
            yield return Until(() => d.State == RunState.Ready, 40, "first Ready");
            if (!d) { Finish(); yield break; }
            yield return Wait(1.2f);
            yield return Shot("ready");

            if (PadWalk)
            {
                yield return Pad(GamepadButton.South);
                Check("pad: A on the Ready card starts the fight", d.State != RunState.Ready, d.State.ToString());
            }
            else d.RequestReady();
            yield return Until(() => d.State == RunState.Combat && d.Boss, 30, "first combat");
            if (!d || !d.Boss) { Finish(); yield break; }

            // ---- pause
            if (PadWalk)
            {
                yield return Pad(GamepadButton.Start);
                Check("pad: Start opens Pause", d.Paused);
                if (!d.Paused) d.SetPaused(true);
            }
            else d.SetPaused(true);
            yield return Wait(0.4f);
            yield return Shot("pause");
            if (PadWalk)
            {
                yield return Pad(GamepadButton.East);
                Check("pad: B resumes from Pause", !d.Paused);
                d.SetPaused(true); yield return Wait(0.2f);
                // up from Resume wraps to the last row; down wraps back to Resume
                yield return Pad(GamepadButton.DpadUp); yield return Shot("pause-pad-wrapped");
                yield return Pad(GamepadButton.DpadDown);
                int runs = d.RunsStarted; var st = d.State;
                yield return Pad(GamepadButton.South);
                Check("pad: wrap up then down, A = Resume", !d.Paused && d.RunsStarted == runs && d.State == st, $"paused {d.Paused}, state {d.State}");
                d.SetPaused(false);
            }
            else d.SetPaused(false);

            // ---- every fight to the final intermission
            bool padShopDone = false;
            for (int guard = 0; guard < 40; guard++)
            {
                yield return Until(() => d.State == RunState.Combat || d.State == RunState.Ready, 40, "next fight");
                if (!d) { Finish(); yield break; }
                if (d.State == RunState.Ready)
                {
                    yield return Wait(0.3f);
                    if (ShopShowing && ShopRun != null && ShopRun.FinalShop) break;
                    if (ShopShowing && PadWalk && !padShopDone) { padShopDone = true; yield return PadShop(); continue; }
                    if (ShopShowing) Shop.RunShopFlow.Instance.Leave(); else d.RequestReady();
                    continue;
                }
                if (RunDirector.Bosses[d.Encounter].Final) { Check("reached the final shop before the Bellkeeper", false); break; }
                if (d.Boss && !d.Boss.Dead) d.Boss.DebugDefeat();
                yield return Until(() => d.State != RunState.Combat, 10, "cleared");
            }
            if (ShopShowing)
            {
                Check("final intermission shop", ShopRun.FinalShop);
                yield return Wait(1.0f);
                yield return Shot("shop-final");
                Shop.RunShopFlow.Instance.Leave();
            }

            // ---- defeat: injected hits, then the killing blow
            yield return Until(() => d.State == RunState.Combat && d.Boss, 30, "final combat");
            if (d && d.Boss && d.Player)
            {
                var ids = BossAttackIcons.All.Where(a => a != null).Select(a => a.id).Take(6).ToArray();
                string[] sources = { "Bell Sweep", "Leaning Bell", "Toll Ring", "Bell Sweep", "Hour Hands" };
                int hp = d.Player.MaxHealth;
                for (int i = 0; i < sources.Length; i++)
                {
                    int dmg = 14 + 7 * i; hp = Mathf.Max(1, hp - dmg); d.Player.DebugSetHp(hp);
                    d.World.ReportPlayerHit(d.Player, new Hazard { Source = sources[i], AttackId = i < ids.Length && i % 2 == 1 ? ids[i] : null }, dmg);
                    yield return Wait(0.35f);
                }
                d.Player.ForceDown(new Hazard { Source = ids.Length > 0 ? BossAttackIcons.Get(ids[0]).name : "Bell Sweep", AttackId = ids.Length > 0 ? ids[0] : null });
                yield return Until(() => d.State == RunState.Downed || d.State == RunState.Results, 5, "downed");
                if (d.State == RunState.Downed) { yield return Wait(0.35f); yield return Shot("downed"); }
                yield return Until(() => d.State == RunState.Results, 15, "defeat results");
                yield return Wait(0.8f);
                Check("defeat with a death recap", d.State == RunState.Results && !d.Victory && d.World != null && d.World.TakenLog.Count > 1);
                yield return Shot("results-defeat");
                RunHud.PinRecapTooltip = true; yield return Shot("results-defeat-tooltip"); RunHud.PinRecapTooltip = false;
                RunHud.LanClientView = true; yield return Shot("results-lan-client"); RunHud.LanClientView = false;
            }

            // ---- victory: retry, then every fight
            if (d && d.State == RunState.Results)
            {
                int runs = d.RunsStarted;
                if (PadWalk)
                {
                    // Retry is the focused main action when Results opens
                    yield return Pad(GamepadButton.South);
                    Check("pad: A on Results retries", d.RunsStarted == runs + 1 || d.State == RunState.Ready, d.State.ToString());
                }
                if (d.State == RunState.Results) Check("retry accepted", d.RequestRetry());
                for (int guard = 0; guard < 40; guard++)
                {
                    yield return Until(() => d.State == RunState.Combat || d.State == RunState.Ready || d.State == RunState.Results, 40, "next fight (victory run)");
                    if (!d || d.State == RunState.Results) break;
                    if (d.State == RunState.Ready) { yield return Wait(0.2f); if (ShopShowing) Shop.RunShopFlow.Instance.Leave(); else d.RequestReady(); continue; }
                    if (d.Boss && !d.Boss.Dead) d.Boss.DebugDefeat();
                    yield return Until(() => d.State != RunState.Combat, 10, "cleared (victory run)");
                }
                yield return Until(() => d.State == RunState.Results, 20, "victory results");
                yield return Wait(1.0f);
                Check("victory results", d.State == RunState.Results && d.Victory);
                yield return Shot("results-victory");
            }
            Finish();
        }

        // Controller-only shop: move to a card's Buy, buy, then to Ready.
        IEnumerator PadShop()
        {
            var run = ShopRun; var me = run.Slot(0);
            yield return Wait(0.5f);
            yield return Shot("shop-pad-open");
            int chimes = me.Chimes, items = me.Items.Count;
            // focus opens on Ready; Right walks the cards' Buy rows. Buy the first affordable card.
            for (int step = 0; step < 16 && me.Items.Count == items; step++)
            {
                yield return Pad(GamepadButton.DpadRight);
                yield return Pad(GamepadButton.South);
            }
            Check("pad: a card bought with dpad + A", me.Items.Count > items && me.Chimes < chimes, $"items {items} -> {me.Items.Count}, chimes {chimes} -> {me.Chimes}");
            yield return Shot("shop-pad-bought");
            // B returns focus to Ready, A readies
            yield return Pad(GamepadButton.East);
            yield return Pad(GamepadButton.South);
            yield return Until(() => !ShopShowing, 5, "shop closed by pad Ready");
            Check("pad: B then A readies from the shop", !ShopShowing);
        }

        void Finish()
        {
            RemoveDevices();
            Check("no errors or exceptions logged", errors == 0, errors + " logged");
            lines.Insert(0, $"RESULT {pass} passed, {fail} failed ({res})");
            File.WriteAllLines(Path.Combine(dir, "overlay-report.txt"), lines);
            Debug.Log($"[overlay-test] RESULT {pass} passed, {fail} failed");
            Application.Quit(fail == 0 ? 0 : 1);
        }
    }
}
