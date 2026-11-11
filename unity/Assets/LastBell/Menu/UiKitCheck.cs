using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace LastBell.Menu
{
    // Opt-in check of the UI kit (docs/ui/ui-kit.md): -lastbellUiKitTest [-lastbellOut <dir>]. Runs over the main menu:
    // shared textures and tokens, the ported main menu (NavFocus, Settings Apply as a text row), InputHints following the
    // last device, and ImguiFocus keyboard/controller navigation on a sample IMGUI screen (captured as imgui-kit-*.png).
    public sealed class UiKitCheck : MonoBehaviour
    {
        public static bool Requested => Environment.GetCommandLineArgs().Contains("-lastbellUiKitTest");
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!Requested) return;
            var go = new GameObject("UI kit check"); DontDestroyOnLoad(go); go.AddComponent<UiKitCheck>();
        }

        readonly List<string> report = new List<string>(), errors = new List<string>();
        int failures; string output;
        Keyboard keyboard; Gamepad pad;
        // sample screen state
        readonly ImguiFocus focus = new ImguiFocus();
        bool drawSample; int submitted = -1, submitCount; int cancels; int lastIndex, indexChanges;
        static readonly string[] Rows = { "Resume", "Retry", "Disabled row", "Main Menu" };

        IEnumerator Start()
        {
            Application.logMessageReceived += Log; Application.runInBackground = true;
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "-lastbellOut");
            output = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.Combine(Application.persistentDataPath, "ui-kit-check"); Directory.CreateDirectory(output);
            var oldSettings = InputSystem.settings; oldSettings.hideFlags = HideFlags.DontUnloadUnusedAsset; var testSettings = Instantiate(oldSettings); testSettings.hideFlags = HideFlags.DontUnloadUnusedAsset; testSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; InputSystem.settings = testSettings;
            keyboard = InputSystem.AddDevice<Keyboard>("UiKitKeyboard"); pad = InputSystem.AddDevice<Gamepad>("UiKitGamepad");
            float until = Time.realtimeSinceStartup + 30; ConfrontationMenu menu = null;
            while ((!(menu = FindAnyObjectByType<ConfrontationMenu>()) || !menu.Canvas) && Time.realtimeSinceStartup < until) yield return null;
            yield return new WaitForSecondsRealtime(2);
            Check("main menu loaded", menu && menu.Canvas);

            // ---- tokens and shared textures
            Check("one cached strip texture", UiTheme.StripTexture && UiTheme.StripTexture == UiTheme.StripTexture && UiTheme.StripTexture.width == 128);
            Check("strip fades at both ends", UiTheme.StripTexture.GetPixel(0, 0).a < .01f && UiTheme.StripTexture.GetPixel(127, 0).a < .01f && UiTheme.StripTexture.GetPixel(64, 0).a > .99f);
            Check("side gradient clear inside, dark outside", UiTheme.SideGradient.GetPixel(0, 0).a < .01f && UiTheme.SideGradient.GetPixel(127, 0).a > .98f);
            Check("centre band dark middle, clear edges", UiTheme.CentreBand.GetPixel(0, 0).a < .01f && UiTheme.CentreBand.GetPixel(0, 32).a > .7f);
            Check("one Spaced()", UiTheme.Spaced("Select Class") == "S E L E C T   C L A S S");

            // ---- the ported main menu
            if (menu)
            {
                var shade = menu.Canvas.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.name == "Right contrast gradient");
                Check("main menu gradient uses the shared texture", shade && shade.sprite && shade.sprite.texture == UiTheme.SideGradient);
                var strips = menu.Canvas.GetComponentsInChildren<Image>(true).Where(i => i.name == "Highlight strip").ToArray();
                Check("main menu strips use the shared texture", strips.Length > 10 && strips.All(s => s.sprite && s.sprite.texture == UiTheme.StripTexture));
                Check("main menu rows on NavFocus", menu.StartButton.GetComponent<NavFocus>() && menu.RushButton.GetComponent<NavFocus>() && menu.PreviousButton.GetComponent<NavFocus>());
                Check("Settings Apply is a text row (no gold box)", menu.ApplyButton.GetComponent<NavFocus>() && !menu.ApplyButton.GetComponent<MenuFocus>() && menu.ApplyButton.GetComponent<Image>().color.a == 0);
                var apply = menu.ApplyButton.GetComponentInChildren<Text>(); var back = menu.SettingsBackButton.GetComponentInChildren<Text>();
                Check("Apply is the larger main action", apply.text == "Apply" && apply.fontSize > back.fontSize);
                menu.OpenSettings(); yield return null; yield return null;
                EventSystem.current.SetSelectedGameObject(menu.ApplyButton.gameObject); yield return null; yield return null;
                var applyStrip = menu.ApplyButton.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.name == "Highlight strip");
                Check("focused Apply shows the strip and GoldBright label", applyStrip && applyStrip.enabled && apply.color == UiTheme.GoldBright);
                yield return Capture("settings-apply-focused.png");
                menu.CloseSettings(); yield return null;
            }

            // keep the main menu's EventSystem out of the test presses (A would start a run)
            var es = EventSystem.current; if (es) es.enabled = false;
            MenuInput.PollDevices = new InputDevice[] { keyboard, pad };

            // ---- InputHints follows the last device
            InputHints.Force(null);
            yield return Press(Key.Space);
            Check("keyboard press -> keyboard hints", InputHints.Last == InputHints.Device.KeyboardMouse && InputHints.Key(HintAction.Retry) == "R" && InputHints.Label("Retry Run", HintAction.Retry) == "Retry Run  (R)" && InputHints.Key(HintAction.Ready) == "Enter");
            Check("gameplay hints match KeyboardMouseInput", InputHints.Key(HintAction.Potion) == "F" && InputHints.Slot(0) == "LMB" && InputHints.Slot(1) == "2" && InputHints.Slot(4) == "5" && InputHints.Key(HintAction.Dash) == "RMB");
            yield return Pad(GamepadButton.North);
            Check("gamepad press -> gamepad hints", InputHints.Last == InputHints.Device.Gamepad && InputHints.Key(HintAction.Confirm) == "A" && InputHints.Key(HintAction.Back) == "B" && InputHints.Key(HintAction.Retry) == "" && InputHints.Label("Retry Run", HintAction.Retry) == "Retry Run");
            Check("gameplay hints stay keyboard on a gamepad (gameplay reads no pad)", InputHints.Key(HintAction.Potion) == "F" && InputHints.Slot(2) == "3");

            // ---- ImguiFocus on a sample IMGUI screen
            drawSample = true; focus.Reset(0); yield return null; yield return null;
            Check("rows registered", focus.Count == Rows.Length);
            yield return Press(Key.DownArrow); Check("arrow down -> row 1", focus.Index == 1);
            yield return Press(Key.DownArrow); Check("disabled row skipped -> row 3", focus.Index == 3);
            yield return Press(Key.DownArrow); Check("wraps to row 0", focus.Index == 0);
            yield return Press(Key.UpArrow); Check("wraps up to row 3", focus.Index == 3);
            yield return Pad(GamepadButton.DpadDown); Check("dpad down -> row 0", focus.Index == 0);
            InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(0, -1) }); yield return null; yield return null;
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null; yield return null;
            Check("left stick down -> row 1", focus.Index == 1);
            yield return Capture("imgui-kit-gamepad.png");
            yield return Press(Key.Enter); Check("Enter confirms the focused row", submitted == 1 && submitCount == 1);
            yield return Pad(GamepadButton.South); Check("A confirms the focused row", submitted == 1 && submitCount == 2);
            yield return Press(Key.Escape); Check("Esc -> Cancelled", cancels == 1);
            yield return Pad(GamepadButton.East); Check("B -> Cancelled", cancels == 2);
            // held arrow repeats after the delay
            focus.Reset(0); yield return null; lastIndex = 0; indexChanges = 0;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.DownArrow)); yield return new WaitForSecondsRealtime(MenuInput.RepeatDelay + 3.5f * MenuInput.RepeatRate);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
            Check("held arrow repeats after the delay (" + indexChanges + " steps)", indexChanges >= 3);
            InputHints.Force(InputHints.Device.KeyboardMouse); yield return null; yield return null;
            yield return Capture("imgui-kit-keyboard.png");
            drawSample = false; InputHints.Force(null);

            if (es) es.enabled = true; MenuInput.PollDevices = null;
            InputSystem.RemoveDevice(keyboard); InputSystem.RemoveDevice(pad); if (oldSettings) InputSystem.settings = oldSettings; Destroy(testSettings);
            Check("zero runtime errors", errors.Count == 0); report.AddRange(errors);
            report.Add("RESULT " + report.Count(s => s.StartsWith("PASS ")) + " passed / " + failures + " failed");
            File.WriteAllLines(Path.Combine(output, "report.txt"), report); Application.logMessageReceived -= Log;
            Application.Quit(failures == 0 ? 0 : 1);
        }

        // A sample run overlay in the kit: side gradient, centre band, header, info strip, a focus list and input hints.
        void OnGUI()
        {
            if (!drawSample) return;
            float vw = UiTheme.VirtualWidth, cx = vw / 2 - 260;
            UiTheme.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, .25f));
            UiImgui.SideGradient(new Rect(vw * .55f, 0, vw * .45f, UiTheme.VirtualHeight));
            UiImgui.Band(new Rect(0, 200, vw, 680));
            float y = UiImgui.Header(new Rect(cx, 260, 520, 0), "UI kit check", "PAUSED", true) + 24;
            UiImgui.InfoStrip(new Rect(cx, y, 520, 52));
            UiTheme.Label(UiTheme.R(cx + 18, y, 500, 52), "Info strip: ink with a gold left bar.", UiTheme.Style(UiTheme.SizeBody, false, TextAnchor.MiddleLeft, UiTheme.IvoryBody));
            y += 76;
            focus.Begin();
            for (int i = 0; i < Rows.Length; i++)
            {
                string label = i == 1 ? InputHints.Label(Rows[i], HintAction.Retry) : i == 0 ? InputHints.Label(Rows[i], HintAction.Resume) : Rows[i];
                if (UiImgui.Row(focus, new Rect(cx, y, 520, 48), label, i == 0 ? UiImgui.Styles.Primary : UiImgui.Styles.Row, enabled: i != 2)) { submitted = i; submitCount++; }
                y += 52;
            }
            if (focus.Cancelled) cancels++;
            if (focus.Index != lastIndex) { lastIndex = focus.Index; indexChanges++; }
            UiTheme.Label(UiTheme.R(cx, y + 12, 520, 20), InputHints.Last == InputHints.Device.Gamepad ? "A  Confirm      B  Back" : "Enter  Confirm      Esc  Back", UiImgui.Styles.Hint);
        }

        IEnumerator Press(Key key) { InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); yield return null; yield return null; InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null; }
        IEnumerator Pad(GamepadButton b) { InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(b)); yield return null; yield return null; InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null; yield return null; }
        IEnumerator Capture(string name) { yield return new WaitForEndOfFrame(); var t = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); t.Apply(); File.WriteAllBytes(Path.Combine(output, name), t.EncodeToPNG()); Destroy(t); }
        void Check(string name, bool pass) { report.Add((pass ? "PASS " : "FAIL ") + name); if (!pass) failures++; Debug.Log("[uikit] " + report[report.Count - 1]); }
        void Log(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + stack); }
    }
}
