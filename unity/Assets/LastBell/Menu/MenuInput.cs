using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace LastBell.Menu
{
    // Match the project's keyboard/mouse + standard-gamepad policy. Unity's generic defaults also
    // navigate from Joystick axes (including rudder pedals), which can steal menu focus continuously.
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
            var move = map.AddAction("Navigate", InputActionType.Value); move.expectedControlType = "Vector2";
            move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            move.AddBinding("<Gamepad>/dpad"); move.AddBinding("<Gamepad>/leftStick").WithProcessor("stickDeadzone(min=0.3,max=1)");
            var submit = map.AddAction("Submit", InputActionType.Button, "<Keyboard>/enter");
            submit.AddBinding("<Keyboard>/numpadEnter"); submit.AddBinding("<Gamepad>/buttonSouth");
            var cancel = map.AddAction("Cancel", InputActionType.Button, "<Keyboard>/escape"); cancel.AddBinding("<Gamepad>/buttonEast");
            module.actionsAsset = asset;
            if (MenuSmokeTest.Requested || MenuFlowTest.Requested) asset.devices = MenuSmokeTest.TestDevices;
            module.point = Reference(point); module.leftClick = Reference(click); module.scrollWheel = Reference(scroll);
            module.move = Reference(move); module.submit = Reference(submit); module.cancel = Reference(cancel);
            module.enabled = true; asset.Enable();
        }
        InputActionReference Reference(InputAction action)
        { var reference = InputActionReference.Create(action); references.Add(reference); return reference; }
        void OnDestroy()
        {
            if (asset) { asset.Disable(); Destroy(asset); }
            foreach (var reference in references) if (reference) Destroy(reference);
        }
    }
}
