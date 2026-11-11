using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LastBell.Run.Networking;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LastBell.Menu
{
    // Opt-in capture of every front-end screen (UI uniformity plan, sub-thread A):
    //   -lastbellFrontendCapture [-lastbellOut <dir>]       main menu, Settings, Boss Rush (arena, boss), LAN name card,
    //                                                        Host/Join page, lobby (host + one guest) and the leave dialog.
    //   -lastbellFrontendCapture -lastbellFrontendPad      the same screens reached with a virtual gamepad only (dpad, A, B,
    //                                                        LB); every step's focus is logged to frontend-capture.txt.
    // Uses its own LAN profile file and restores the saved LAN class. Run once per window size.
    public sealed class FrontendCapture : MonoBehaviour
    {
        static bool Has(string a) => Environment.GetCommandLineArgs().Any(x => x.Equals(a, StringComparison.OrdinalIgnoreCase));
        public static bool Requested => Has("-lastbellFrontendCapture");
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!Requested || FindAnyObjectByType<FrontendCapture>()) return;
            var go = new GameObject("Front-end capture"); DontDestroyOnLoad(go); go.AddComponent<FrontendCapture>();
        }

        readonly List<string> report = new List<string>(); int failures; string output, prefix;
        bool padOnly; Gamepad pad; Keyboard keyboard; readonly List<LanLobby> raw = new List<LanLobby>();
        void Check(string name, bool ok) { report.Add((ok ? "PASS " : "FAIL ") + name); if (!ok) failures++; }
        void Note(string s) => report.Add("NOTE " + s);
        void Update() { foreach (var l in raw) l.Pump(); }
        static GameObject Selected => EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        static string SelectedName => Selected ? Selected.name : "(none)";

        IEnumerator Start()
        {
            Application.runInBackground = true; padOnly = Has("-lastbellFrontendPad");
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "-lastbellOut");
            output = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.Combine(Application.persistentDataPath, "frontend-capture"); Directory.CreateDirectory(output);
            var old = InputSystem.settings; old.hideFlags = HideFlags.DontUnloadUnusedAsset; var test = Instantiate(old); test.hideFlags = HideFlags.DontUnloadUnusedAsset;
            test.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; InputSystem.settings = test;
            pad = InputSystem.AddDevice<Gamepad>("FrontendCapturePad"); keyboard = InputSystem.AddDevice<Keyboard>("FrontendCaptureKeyboard");
            LanProfile.PathOverride = Path.Combine(output, "lan-profile.json"); if (File.Exists(LanProfile.PathOverride)) File.Delete(LanProfile.PathOverride);
            string savedClass = PlayerPrefs.HasKey("LastBell.Lan.Class") ? PlayerPrefs.GetString("LastBell.Lan.Class") : null;
            if (padOnly) InputHints.Force(InputHints.Device.Gamepad);

            ConfrontationMenu menu = null; float until = Time.realtimeSinceStartup + 40;
            while ((!(menu = FindAnyObjectByType<ConfrontationMenu>()) || !menu.Canvas) && Time.realtimeSinceStartup < until) yield return null;
            yield return new WaitForSecondsRealtime(3);
            Check("main menu loaded", menu);
            yield return ForceWindowSize();
            if (menu) yield return padOnly ? PadMenus(menu) : Menus(menu);
            menu = FindAnyObjectByType<ConfrontationMenu>();
            if (menu) yield return padOnly ? PadLobby(menu) : Lobby(menu);

            foreach (var l in raw) l.Dispose(); raw.Clear();
            if (savedClass != null) PlayerPrefs.SetString("LastBell.Lan.Class", savedClass); else PlayerPrefs.DeleteKey("LastBell.Lan.Class");
            PlayerPrefs.Save();
            report.Add("RESULT " + (report.Count(r => r.StartsWith("PASS"))) + "/" + failures);
            File.WriteAllLines(Path.Combine(output, prefix + "frontend-capture.txt"), report);
            Application.Quit(failures == 0 ? 0 : 1);
        }

        // ---------------------------------------------------------------- direct (keyboard/mouse-free) capture
        IEnumerator Menus(ConfrontationMenu menu)
        {
            yield return Shot("main-menu.png");
            menu.OpenSettings(); yield return Frames(4); yield return Shot("settings.png");
            EventSystem.current.SetSelectedGameObject(menu.ApplyButton.gameObject); yield return Frames(4); yield return Shot("settings-apply.png");
            menu.CloseSettings(); yield return Frames(4);
            menu.BossRush(); yield return new WaitForSecondsRealtime(4);
            var rush = menu.Rush; Check("Boss Rush opens", rush);
            if (!rush) yield break;
            yield return Shot("rush-arena.png");
            rush.Continue(); yield return new WaitForSecondsRealtime(1);
            yield return Shot("rush-boss.png");
            // focus a long miniboss name (the card-name fix)
            int briar = Array.FindIndex(Enumerable.Range(0, rush.EncounterButtons.Length).ToArray(), i => TestRushSetup.CardName(i).StartsWith("Briar"));
            if (briar >= 0) { EventSystem.current.SetSelectedGameObject(rush.EncounterButtons[briar].gameObject); yield return new WaitForSecondsRealtime(.6f); yield return Shot("rush-boss-minifocus.png"); }
            rush.Back(); yield return Frames(4); rush.Back(); yield return new WaitForSecondsRealtime(1.5f);
        }
        IEnumerator Lobby(ConfrontationMenu menu)
        {
            menu.MultiplayerButton.onClick.Invoke();
            yield return Until(() => LanLobbyScreen.Instance && SceneManager.GetActiveScene().path == LanLobbyScreen.ScenePath, 20); yield return Frames(6);
            var screen = LanLobbyScreen.Instance; Check("LAN lobby opens", screen);
            if (!screen) yield break;
            Check("name card opens with no saved name", screen.NameCardOpen);
            yield return Shot("lan-name-card.png");
            screen.NameField.text = "Bellringer"; yield return Frames(3); yield return Shot("lan-name-card-typed.png");
            screen.ConfirmName(); yield return Frames(4);
            Check("name card closes on Confirm", !screen.NameCardOpen);
            yield return Shot("lan-connect.png");
            var addressField = screen.GetComponentsInChildren<InputField>(true).FirstOrDefault(f => f != screen.NameField && f.gameObject.activeInHierarchy);
            if (addressField) { EventSystem.current.SetSelectedGameObject(addressField.gameObject); yield return Frames(4); yield return Shot("lan-connect-field.png"); }
            int port = FreePort(); screen.SetConnectTarget("127.0.0.1", port);
            screen.HostButton.onClick.Invoke(); yield return Frames(6);
            Check("Host opens the lobby", screen.Current == LanLobbyScreen.Page.Lobby);
            // three guests fill the four player cards (one ready)
            raw.Add(LanLobby.Connect("127.0.0.1", port, LanLobbyScreen.BuildId(), "Arcanist", "Guest Two"));
            raw.Add(LanLobby.Connect("127.0.0.1", port, LanLobbyScreen.BuildId(), "Chimesinger", "Guest Three"));
            raw.Add(LanLobby.Connect("127.0.0.1", port, LanLobbyScreen.BuildId(), "Tollbreaker", "Guest Four"));
            yield return Until(() => screen.Session && screen.Session.Lobby != null && screen.Session.Lobby.Members.Count >= 4, 10);
            yield return new WaitForSecondsRealtime(.5f); raw[0].SetReady(true);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot("lan-lobby-host.png");
            StyleChecks(screen);
            screen.Back(); yield return Frames(4);
            Check("Back opens the leave dialog", screen.LeaveConfirmOpen);
            yield return Shot("lan-leave.png");
            screen.LeaveButton.onClick.Invoke();
            yield return Until(() => FindAnyObjectByType<ConfrontationMenu>(), 20); yield return new WaitForSecondsRealtime(1);
            foreach (var l in raw) l.Dispose(); raw.Clear();
            yield return ForceWindowSize();   // the main menu restored the saved resolution again

            // client view: an in-process host, this screen joins it
            int port2 = FreePort(); var remote = LanLobby.Host(port2, LanLobbyScreen.BuildId(), "Arcanist", "Remote Host"); raw.Add(remote);
            LanLobbyScreen.Open();
            yield return Until(() => LanLobbyScreen.Instance && SceneManager.GetActiveScene().path == LanLobbyScreen.ScenePath, 20); yield return Frames(6);
            screen = LanLobbyScreen.Instance; if (!screen) yield break;
            screen.SetConnectTarget("127.0.0.1", port2); screen.JoinButton.onClick.Invoke();
            yield return Until(() => screen.Session && screen.Session.Lobby != null && screen.Session.Lobby.Connected, 10);
            yield return new WaitForSecondsRealtime(1.5f);
            Check("Connect joins as a client", screen.Current == LanLobbyScreen.Page.Lobby && !screen.IsHost);
            yield return Shot("lan-lobby-client.png");
            screen.Back(); yield return Frames(4); yield return Shot("lan-leave-client.png");
            screen.LeaveButton.onClick.Invoke();
            yield return Until(() => FindAnyObjectByType<ConfrontationMenu>(), 20); yield return new WaitForSecondsRealtime(1);
        }

        // ---------------------------------------------------------------- controller-only walkthrough
        IEnumerator PadMenus(ConfrontationMenu menu)
        {
            var module = EventSystem.current.GetComponent<InputSystemUIInputModule>();
            if (module && module.actionsAsset) module.actionsAsset.devices = new InputDevice[] { pad };
            yield return Shot("main-menu.png");
            yield return MoveTo(GamepadButton.DpadDown, menu.SettingsButton.gameObject, 12, "dpad to Settings");
            yield return Press(GamepadButton.South); Check("A opens Settings", menu.SettingsOpen);
            yield return Shot("settings.png");
            yield return MoveTo(GamepadButton.DpadUp, menu.ApplyButton.gameObject, 12, "dpad (wrap) to Apply");
            yield return Shot("settings-apply.png");
            yield return Press(GamepadButton.East); Check("B closes Settings", !menu.SettingsOpen);
            yield return MoveTo(GamepadButton.DpadUp, menu.RushButton.gameObject, 12, "dpad to Boss Rush");
            yield return Press(GamepadButton.South); yield return new WaitForSecondsRealtime(4);
            var rush = menu.Rush; Check("A opens Boss Rush", rush);
            if (!rush) yield break;
            yield return Shot("rush-arena.png");
            yield return Press(GamepadButton.DpadRight); Note("arena step dpad right -> " + SelectedName);
            yield return Press(GamepadButton.South); Note("A picks arena " + rush.MapIndex);
            yield return MoveTo(GamepadButton.DpadDown, rush.ContinueButton.gameObject, 4, "dpad down to the footer", GamepadButton.DpadRight);
            yield return Press(GamepadButton.South); yield return new WaitForSecondsRealtime(1);
            Check("A on Continue opens the boss step", rush.BossStep);
            yield return Shot("rush-boss.png");
            yield return Press(GamepadButton.DpadDown); yield return Press(GamepadButton.DpadRight); Note("boss step dpad down, right -> " + SelectedName);
            yield return new WaitForSecondsRealtime(.6f); yield return Shot("rush-boss-minifocus.png");
            yield return Press(GamepadButton.East); Check("B returns to the arena step", !rush.BossStep);
            yield return Press(GamepadButton.East); yield return new WaitForSecondsRealtime(1.5f);
            Check("B returns to the main menu", !menu.Rush);
        }
        IEnumerator PadLobby(ConfrontationMenu menu)
        {
            var module = EventSystem.current.GetComponent<InputSystemUIInputModule>();
            if (module && module.actionsAsset) module.actionsAsset.devices = new InputDevice[] { pad };
            yield return MoveTo(GamepadButton.DpadDown, menu.MultiplayerButton.gameObject, 12, "dpad to Multiplayer");
            yield return Press(GamepadButton.South);
            yield return Until(() => LanLobbyScreen.Instance && SceneManager.GetActiveScene().path == LanLobbyScreen.ScenePath, 20); yield return Frames(6);
            var screen = LanLobbyScreen.Instance; Check("A opens the LAN lobby", screen);
            if (!screen) yield break;
            module = EventSystem.current.GetComponent<InputSystemUIInputModule>();
            if (module && module.actionsAsset) module.actionsAsset.devices = new InputDevice[] { pad };
            Check("name card opens, name field focused", screen.NameCardOpen && Selected == screen.NameField.gameObject);
            yield return Shot("lan-name-card.png");
            // A gamepad cannot type: the name comes from the keyboard (here set directly), then the pad confirms.
            screen.NameField.text = "Bellringer"; screen.NameField.DeactivateInputField(); yield return Frames(3);
            yield return MoveTo(GamepadButton.DpadDown, screen.ConfirmNameButton.gameObject, 4, "dpad to Confirm");
            yield return Press(GamepadButton.South); Check("A confirms the name", !screen.NameCardOpen);
            yield return Shot("lan-connect.png");
            yield return MoveTo(GamepadButton.DpadDown, screen.HostButton.gameObject, 8, "dpad to Host");
            yield return Press(GamepadButton.DpadDown); Note("dpad down from Host -> " + SelectedName);
            yield return Frames(4); yield return Shot("lan-connect-field.png");
            screen.SetConnectTarget("127.0.0.1", FreePort());
            yield return MoveTo(GamepadButton.DpadUp, screen.HostButton.gameObject, 8, "dpad back to Host");
            yield return Press(GamepadButton.South); yield return Frames(6);
            Check("A on Host opens the lobby", screen.Current == LanLobbyScreen.Page.Lobby);
            string before = screen.Hero; yield return Press(GamepadButton.DpadRight); Check("dpad right changes class", screen.Hero != before);
            var lobby = screen.Session ? screen.Session.Lobby : null; string boss = lobby != null ? lobby.Encounter : null;
            yield return Press(GamepadButton.RightShoulder); Check("RB changes the boss", lobby != null && lobby.Encounter != boss);
            yield return Press(GamepadButton.South); Check("A readies", lobby != null && lobby.LocalReady);
            yield return new WaitForSecondsRealtime(1); yield return Shot("lan-lobby-host.png");
            yield return Press(GamepadButton.East); Check("B opens the leave dialog", screen.LeaveConfirmOpen);
            yield return Shot("lan-leave.png");
            yield return MoveTo(GamepadButton.DpadRight, screen.LeaveButton.gameObject, 3, "dpad to Leave");
            yield return Press(GamepadButton.South);
            yield return Until(() => FindAnyObjectByType<ConfrontationMenu>(), 20);
            Check("A on Leave returns to the main menu", FindAnyObjectByType<ConfrontationMenu>());
        }

        // The lobby speaks the main menu's language (UI uniformity plan, section 3).
        void StyleChecks(LanLobbyScreen screen)
        {
            var buttons = screen.GetComponentsInChildren<Button>(true);
            Check("lobby: every button is a text row or chevron (NavFocus, no boxed fill)", buttons.Length > 10 && buttons.All(b => b.GetComponent<NavFocus>() && (!b.targetGraphic || b.targetGraphic.color.a == 0)));
            Check("lobby: boss, arena and class choosers use drawn chevrons", new[] { screen.BossNext, screen.ArenaPrev, screen.Slots[0].Next }.All(b => b.GetComponentInChildren<ClassSelectionArrow>(true)));
            var texts = screen.GetComponentsInChildren<Text>(true);
            Check("lobby: header eyebrow, Ivory title and 96x2 gold rule", texts.Any(t => t.text == UiTheme.Spaced("LAN Lobby")) && texts.Any(t => t.text == "Lobby" && t.color == UiTheme.Ivory)
                && screen.GetComponentsInChildren<Image>(true).Any(i => i.name == "Title rule" && i.rectTransform.sizeDelta == new Vector2(UiTheme.RuleWidth, UiTheme.RuleHeight)));
            Check("lobby: right side gradient", screen.GetComponentsInChildren<Image>(true).Any(i => i.name == "Right contrast gradient" && i.sprite && i.sprite.texture == UiTheme.SideGradient));
            Check("lobby: no corner ornaments or boxed fields", !screen.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Corner") && screen.GetComponentsInChildren<InputField>(true).All(f => f.targetGraphic && f.targetGraphic.color.a == 0));
        }

        // The main menu restores a saved Settings resolution over -screen-width/-height; captures need the asked-for size.
        // The window is resized for this run only (the saved preference is not touched).
        IEnumerator ForceWindowSize()
        {
            var args = Environment.GetCommandLineArgs();
            int Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int v) ? v : 0; }
            int w = Arg("-screen-width"), h = Arg("-screen-height");
            if (w > 0 && h > 0 && (Screen.width != w || Screen.height != h))
            {
                Note("window was " + Screen.width + "x" + Screen.height + " (saved Settings resolution); resized to " + w + "x" + h);
                Screen.SetResolution(w, h, FullScreenMode.Windowed); float until = Time.realtimeSinceStartup + 5;
                while ((Screen.width != w || Screen.height != h) && Time.realtimeSinceStartup < until) yield return null;
                yield return new WaitForSecondsRealtime(1);
            }
            prefix = (padOnly ? "pad-" : "") + Screen.width + "x" + Screen.height + "-";
            Check("window is " + w + "x" + h, w == 0 || (Screen.width == w && Screen.height == h));
        }

        // ---------------------------------------------------------------- helpers
        IEnumerator MoveTo(GamepadButton step, GameObject target, int max, string what, GamepadButton? then = null)
        {
            for (int i = 0; i < max && Selected != target; i++) { yield return Press(step); if (then.HasValue && Selected != target) { yield return Press(then.Value); if (Selected == target) break; } }
            Check(what + " (focus " + SelectedName + ")", Selected == target);
        }
        IEnumerator Press(GamepadButton b)
        {
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(b)); yield return null; yield return null;
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null; yield return null;
        }
        static int FreePort() { var p = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); p.Start(); int port = ((System.Net.IPEndPoint)p.LocalEndpoint).Port; p.Stop(); return port; }
        IEnumerator Until(Func<bool> done, float seconds) { float t = Time.realtimeSinceStartup + seconds; while (!done() && Time.realtimeSinceStartup < t) yield return null; }
        IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        IEnumerator Shot(string name)
        {
            yield return Frames(6); yield return new WaitForEndOfFrame();
            var path = Path.Combine(output, prefix + name);
            var grab = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            grab.ReadPixels(new UnityEngine.Rect(0, 0, Screen.width, Screen.height), 0, 0); grab.Apply(false);
            File.WriteAllBytes(path, grab.EncodeToPNG()); Destroy(grab);
            Check("saved " + prefix + name + " (focus " + SelectedName + ")", File.Exists(path));
        }
    }
}
