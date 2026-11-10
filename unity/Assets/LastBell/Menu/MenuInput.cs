using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace LastBell.Menu
{
    // Match the project's keyboard/mouse + standard-gamepad policy. Unity's generic defaults also
    // navigate from Joystick axes (including rudder pedals), which can steal menu focus continuously.
    // Bind() drives a uGUI EventSystem; Poll() reads the same bindings once per frame for IMGUI screens (UiImgui/ImguiFocus).
    public sealed class MenuInput : MonoBehaviour
    {
        InputActionAsset asset;
        readonly List<InputActionReference> references = new List<InputActionReference>();
        public void Bind(InputSystemUIInputModule module)
        {
            module.enabled = false; module.UnassignActions();
            asset = ScriptableObject.CreateInstance<InputActionAsset>(); asset.name = "Last Bell menu input";
            var map = asset.AddActionMap("Menu");
            var point = map.AddAction("Point", InputActionType.PassThrough, "<Pointer>/position"); point.expectedControlType = "Vector2";
            var click = map.AddAction("Click", InputActionType.PassThrough, "<Pointer>/press"); click.expectedControlType = "Button";
            var scroll = map.AddAction("Scroll", InputActionType.PassThrough, "<Mouse>/scroll"); scroll.expectedControlType = "Vector2";
            AddNavigation(map, out var move, out var submit, out var cancel);
            module.actionsAsset = asset;
            if (MenuSmokeTest.Requested || MenuFlowTest.Requested) asset.devices = MenuSmokeTest.TestDevices;
            module.point = Reference(point); module.leftClick = Reference(click); module.scrollWheel = Reference(scroll);
            module.move = Reference(move); module.submit = Reference(submit); module.cancel = Reference(cancel);
            module.enabled = true; asset.Enable();
        }
        // The one menu navigation binding set: arrows / dpad / left stick, Enter or A to confirm, Esc or B to go back.
        static void AddNavigation(InputActionMap map, out InputAction move, out InputAction submit, out InputAction cancel)
        {
            move = map.AddAction("Navigate", InputActionType.Value); move.expectedControlType = "Vector2";
            move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
#if !UNITY_WEBGL || UNITY_EDITOR
            move.AddBinding("<Gamepad>/dpad"); move.AddBinding("<Gamepad>/leftStick").WithProcessor("stickDeadzone(min=0.3,max=1)");
#endif
            submit = map.AddAction("Submit", InputActionType.Button, "<Keyboard>/enter");
            submit.AddBinding("<Keyboard>/numpadEnter");
            cancel = map.AddAction("Cancel", InputActionType.Button, "<Keyboard>/escape");
#if !UNITY_WEBGL || UNITY_EDITOR
            submit.AddBinding("<Gamepad>/buttonSouth"); cancel.AddBinding("<Gamepad>/buttonEast");
#endif
        }
        InputActionReference Reference(InputAction action)
        { var reference = InputActionReference.Create(action); references.Add(reference); return reference; }
        void OnDestroy()
        {
            if (asset) { asset.Disable(); Destroy(asset); }
            foreach (var reference in references) if (reference) Destroy(reference);
        }

        // ---------------------------------------------------------------- IMGUI polling
        // One navigation step per press, repeating while held (uGUI's defaults: 0.5 s delay, then every 0.1 s).
        public struct Frame
        {
            public Vector2Int Move;   // -1/0/1 per axis this frame (y up = +1)
            public bool Submit, Cancel;
            public bool Any => Move != Vector2Int.zero || Submit || Cancel;
        }
        public const float RepeatDelay = .5f, RepeatRate = .1f;
        static InputActionAsset pollAsset; static InputAction pollMove, pollSubmit, pollCancel;
        static int polledFrame = -1; static Frame polled;
        static Vector2Int held; static float nextRepeat;
        // Test devices only (the autotests' virtual keyboard/pad); null = every device.
        public static InputDevice[] PollDevices
        {
            set { Ensure(); if (value == null) pollAsset.devices = null; else pollAsset.devices = new UnityEngine.InputSystem.Utilities.ReadOnlyArray<InputDevice>(value); }
        }
        static void Ensure()
        {
            if (pollAsset) return;
            pollAsset = ScriptableObject.CreateInstance<InputActionAsset>(); pollAsset.name = "Last Bell IMGUI menu input"; pollAsset.hideFlags = HideFlags.HideAndDontSave;
            AddNavigation(pollAsset.AddActionMap("Menu"), out pollMove, out pollSubmit, out pollCancel);
            if ((MenuSmokeTest.Requested || MenuFlowTest.Requested) && MenuSmokeTest.TestKeyboard != null) pollAsset.devices = MenuSmokeTest.TestDevices;
            pollAsset.Enable();
        }
        // This frame's menu input; the same answer however many times it is called in a frame (OnGUI runs several passes).
        public static Frame Poll()
        {
            if (polledFrame == Time.frameCount) return polled;
            Ensure(); polledFrame = Time.frameCount; polled = default;
            var v = pollMove.ReadValue<Vector2>();
            var dir = Mathf.Abs(v.x) > Mathf.Abs(v.y) ? new Vector2Int(v.x > .5f ? 1 : v.x < -.5f ? -1 : 0, 0)
                                                      : new Vector2Int(0, v.y > .5f ? 1 : v.y < -.5f ? -1 : 0);
            float now = Time.unscaledTime;
            if (dir != held) { held = dir; polled.Move = dir; nextRepeat = now + RepeatDelay; }
            else if (dir != Vector2Int.zero && now >= nextRepeat) { polled.Move = dir; nextRepeat = now + RepeatRate; }
            polled.Submit = pollSubmit.WasPressedThisFrame();
            polled.Cancel = pollCancel.WasPressedThisFrame();
            return polled;
        }
    }
}
