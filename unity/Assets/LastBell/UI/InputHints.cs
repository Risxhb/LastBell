using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace LastBell
{
    // What a screen can ask a key hint for. Menu actions follow MenuInput's bindings plus the run's own hotkeys
    // (RunDirector: Enter = Ready, Esc = pause; RunHud: R / M on Results; RunShopFlow: R = reroll); gameplay actions follow
    // KeyboardMouseInput (the only gameplay input source; it reads no gamepad).
    public enum HintAction { Confirm, Ready, Back, Resume, Pause, Retry, MainMenu, Reroll, Primary, Slot1, Slot2, Slot3, Slot4, Dash, Potion, Sprint, Move }

    // Key hints in one place, for the device the player used last (keyboard/mouse or gamepad).
    //   InputHints.Key(HintAction.Retry)                 "R" on keyboard, "" on a gamepad (no direct button; focus the row, press A)
    //   InputHints.Label("Retry Run", HintAction.Retry)  "Retry Run  (R)", or "Retry Run" when the device has no key for it
    //   InputHints.Slot(2)                               ability slot 0..4: "LMB", "2" .. "5"
    // Strings are cached, so per-frame OnGUI calls do not allocate. InputHints.Changed fires when the device switches.
    public static class InputHints
    {
        public enum Device { KeyboardMouse, Gamepad }
        public static Device Last { get; private set; } = Device.KeyboardMouse;
        public static event Action Changed;
        // The gameplay input reads only keyboard and mouse, so gameplay hints stay keyboard/mouse even on a gamepad.
        public const bool GamepadPlaysGameplay = false;
        static Device? forced;
        // Tests and captures: pin the device (null = follow the last device used again). -lastbellHints gamepad does this at start.
        public static void Force(Device? device) { forced = device; Set(device ?? Last, true); }

        public static string Key(HintAction action) => Key(action, Last);
        public static string Key(HintAction action, Device device)
        {
            if (device == Device.Gamepad && !IsGameplay(action))
                switch (action)
                {
                    case HintAction.Confirm: case HintAction.Ready: return "A";
                    case HintAction.Back: case HintAction.Resume: return "B";
                    default: return "";   // Pause, Retry, Main Menu, Reroll: no gamepad button of their own
                }
            switch (action)
            {
                case HintAction.Confirm: case HintAction.Ready: return "Enter";
                case HintAction.Back: case HintAction.Resume: case HintAction.Pause: return "Esc";
                case HintAction.Retry: return "R";
                case HintAction.MainMenu: return "M";
                case HintAction.Reroll: return "R";
                case HintAction.Primary: return "LMB";
                case HintAction.Slot1: return KeyName(Run.KeyboardMouseInput.SlotKeys[0]);
                case HintAction.Slot2: return KeyName(Run.KeyboardMouseInput.SlotKeys[1]);
                case HintAction.Slot3: return KeyName(Run.KeyboardMouseInput.SlotKeys[2]);
                case HintAction.Slot4: return KeyName(Run.KeyboardMouseInput.SlotKeys[3]);
                case HintAction.Dash: return "RMB";
                case HintAction.Potion: return KeyName(Run.KeyboardMouseInput.PotionKey);
                case HintAction.Sprint: return "Shift";
                case HintAction.Move: return "WASD";
            }
            return "";
        }
        public static bool IsGameplay(HintAction a) => a >= HintAction.Primary;
        // Ability slot 0..4 (0 = primary).
        public static string Slot(int slot) => slot <= 0 ? Key(HintAction.Primary) : slot <= 4 ? Key(HintAction.Slot1 + (slot - 1)) : "";

        static readonly Dictionary<(string, HintAction, Device), string> labels = new Dictionary<(string, HintAction, Device), string>();
        // "text  (key)" in the house format, or the text alone when the device has no key for the action.
        public static string Label(string text, HintAction action)
        {
            var k = (text, action, Last);
            if (labels.TryGetValue(k, out var s)) return s;
            var key = Key(action, Last);
            if (labels.Count > 512) labels.Clear();
            return labels[k] = key.Length == 0 ? text : text + "  (" + key + ")";
        }

        static string KeyName(Key key)
        {
            switch (key)
            {
                case UnityEngine.InputSystem.Key.Digit0: case UnityEngine.InputSystem.Key.Digit1: case UnityEngine.InputSystem.Key.Digit2:
                case UnityEngine.InputSystem.Key.Digit3: case UnityEngine.InputSystem.Key.Digit4: case UnityEngine.InputSystem.Key.Digit5:
                case UnityEngine.InputSystem.Key.Digit6: case UnityEngine.InputSystem.Key.Digit7: case UnityEngine.InputSystem.Key.Digit8:
                case UnityEngine.InputSystem.Key.Digit9:
                    return key == UnityEngine.InputSystem.Key.Digit0 ? "0" : (1 + (key - UnityEngine.InputSystem.Key.Digit1)).ToString();
                case UnityEngine.InputSystem.Key.Escape: return "Esc";
                case UnityEngine.InputSystem.Key.Enter: return "Enter";
                case UnityEngine.InputSystem.Key.Space: return "Space";
                case UnityEngine.InputSystem.Key.LeftShift: case UnityEngine.InputSystem.Key.RightShift: return "Shift";
                default: return key.ToString().ToUpperInvariant();
            }
        }

        static void Set(Device d, bool notify)
        {
            if (forced.HasValue) d = forced.Value;
            if (d == Last) return;
            Last = d; if (notify) Changed?.Invoke();
        }

        // ---------------------------------------------------------------- last device used
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "-lastbellHints");
            if (at >= 0 && at + 1 < args.Length) forced = args[at + 1].Equals("gamepad", StringComparison.OrdinalIgnoreCase) ? Device.Gamepad : Device.KeyboardMouse;
            if (forced.HasValue) Last = forced.Value;
            var go = new GameObject("Input hints (last device)") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go); go.AddComponent<Tracker>();
        }
        sealed class Tracker : MonoBehaviour
        {
            void Update()
            {
                foreach (var device in InputSystem.devices)
                {
                    if (!device.enabled) continue;
                    if (device is Keyboard kb) { if (kb.anyKey.wasPressedThisFrame) { Set(Device.KeyboardMouse, true); return; } }
                    else if (device is Mouse m)
                    {
                        if (m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame || m.middleButton.wasPressedThisFrame || m.delta.ReadValue().sqrMagnitude > 9)
                        { Set(Device.KeyboardMouse, true); return; }
                    }
#if !UNITY_WEBGL || UNITY_EDITOR
                    // standard gamepads only: other joysticks (rudder pedals) never count, as in MenuInput
                    else if (device is Gamepad pad && PadUsed(pad)) { Set(Device.Gamepad, true); return; }
#endif
                }
            }
            static bool PadUsed(Gamepad p)
            {
                foreach (var c in p.allControls) if (c is ButtonControl b && !c.synthetic && b.wasPressedThisFrame) return true;
                return p.leftStick.ReadValue().sqrMagnitude > .25f || p.rightStick.ReadValue().sqrMagnitude > .25f;
            }
        }
    }
}
