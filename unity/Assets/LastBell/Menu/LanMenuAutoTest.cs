using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using LastBell.Run;
using LastBell.Run.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LastBell.Menu
{
    // Opt-in player diagnostic (-lastbellLanMenuTest): drives the Confrontation menu and the LAN lobby scene. It checks
    // UI wiring and lobby rules in one process (extra lobby members are in-process LanLobby clients or a host), and
    // saves host and client screenshots. It does not check a fight; LanSessionAutoTest does that.
    public sealed class LanMenuAutoTest : MonoBehaviour
    {
        readonly List<string> results = new List<string>(); int failures;
        readonly List<LanLobby> raw = new List<LanLobby>();   // in-process peers, pumped every frame
        string outDir;
        static bool installed;
        public static void Install(ConfrontationMenu owner)
        {
            if (installed || !Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellLanMenuTest", StringComparison.OrdinalIgnoreCase))) return;
            installed = true; var go = new GameObject("LAN menu diagnostic"); DontDestroyOnLoad(go); go.AddComponent<LanMenuAutoTest>();
        }
        void Check(string name, bool passed) { results.Add((passed ? "PASS " : "FAIL ") + name); if (!passed) failures++; }
        void Note(string text) => results.Add("NOTE " + text);
        static Button Find(IEnumerable<Button> buttons, string name) => buttons.FirstOrDefault(b => b && b.GetComponentInChildren<Text>() && b.GetComponentInChildren<Text>().text == name);
        static int FreePort() { var p = new TcpListener(IPAddress.Loopback, 0); p.Start(); int port = ((IPEndPoint)p.LocalEndpoint).Port; p.Stop(); return port; }
        void Update() { foreach (var l in raw) l.Pump(); }
        IEnumerator Until(Func<bool> done, float seconds) { float t = Time.realtimeSinceStartup + seconds; while (!done() && Time.realtimeSinceStartup < t) yield return null; }
        IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        IEnumerator OpenLobby()
        {
            LanLobbyScreen.Open();
            yield return Until(() => LanLobbyScreen.Instance && SceneManager.GetActiveScene().path == LanLobbyScreen.ScenePath, 15);
            yield return Frames(3);
        }
        IEnumerator Shot(string name)
        {
            yield return Frames(8);
            // ReadPixels at the end of the frame (the ScreenCapture module isn't in this project), like the boss autotests.
            yield return new WaitForEndOfFrame();
            var path = Path.Combine(outDir, name);
            var grab = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            grab.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); grab.Apply(false);
            File.WriteAllBytes(path, grab.EncodeToPNG()); Destroy(grab);
            Check("screenshot saved: " + name, File.Exists(path));
        }

        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "-lastbellOut");
            outDir = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.Combine(Application.temporaryCachePath, "lan-menu-test");
            Directory.CreateDirectory(outDir);
            // Never touch the player's real profile or saved LAN class: this run uses its own file and restores the class.
            LanProfile.PathOverride = Path.Combine(outDir, "lan-profile.json"); if (File.Exists(LanProfile.PathOverride)) File.Delete(LanProfile.PathOverride);
            string savedClass = PlayerPrefs.HasKey("LastBell.Lan.Class") ? PlayerPrefs.GetString("LastBell.Lan.Class") : null; PlayerPrefs.DeleteKey("LastBell.Lan.Class");
            bool overridden = LanProfile.Overridden;
            yield return new WaitForSecondsRealtime(2);

            var menu = FindFirstObjectByType<ConfrontationMenu>();
            var main = menu ? menu.Canvas.GetComponentsInChildren<Button>(true) : new Button[0];
            Check("Confrontation starts with Multiplayer", Find(main, "Multiplayer") != null);
            Check("Start Run and Boss Rush are on the same menu (one player for everything)", Find(main, "Start Run") != null && Find(main, "Boss Rush") != null);
            Check("all three classes available", RunDirector.Classes.SequenceEqual(new[] { "Tollbreaker", "Chimesinger", "Arcanist" }));
            Check("LAN class list matches the game's, Tollbreaker first", LanLobby.ClassList.SequenceEqual(RunDirector.Classes) && LanLobby.DefaultClass == "Tollbreaker");
            if (menu) menu.RushButton.onClick.Invoke();
            yield return Until(() => LanLobbyScreen.Instance, 15); yield return Frames(3);
            var screen = LanLobbyScreen.Instance;
            Check("Multiplayer opens the LAN lobby scene", screen && SceneManager.GetActiveScene().path == LanLobbyScreen.ScenePath);
            if (!screen) { Finish(savedClass); yield break; }

            if (!overridden)
            {
                Check("first run: the lobby scene asks for a name", screen.NameCardOpen);
                var field = screen.NameField; var confirm = screen.ConfirmNameButton;
                field.text = ""; yield return null; Check("Confirm is disabled with no name", !confirm.interactable);
                field.text = "A"; yield return null; Check("a one-letter name is refused", !confirm.interactable);
                field.text = "<b>Bell</b>"; yield return null; Check("markup in a name is refused", !confirm.interactable);
                field.text = "  Test   Ringer "; yield return null; Check("a valid name enables Confirm", confirm.interactable);
                confirm.onClick.Invoke(); yield return null;
                Check("Confirm saves the name (trimmed) and closes the card", !screen.NameCardOpen && LanProfile.Current == "Test Ringer" && File.ReadAllText(LanProfile.PathOverride).Contains("Test Ringer"));
                screen.EditNameButton.onClick.Invoke(); yield return null;
                Check("Edit name reopens the card with the saved name", screen.NameCardOpen && screen.NameField.text == "Test Ringer");
                screen.Back(); yield return null;
                Check("Back closes the card when a name exists", !screen.NameCardOpen && screen.Current == LanLobbyScreen.Page.Connect);
                yield return OpenLobby(); screen = LanLobbyScreen.Instance;
                Check("a saved name skips the card next time", screen && !screen.NameCardOpen);
                File.WriteAllText(LanProfile.PathOverride, "{ not json");
                yield return OpenLobby(); screen = LanLobbyScreen.Instance;
                Check("a corrupt profile asks for the name again", screen && screen.NameCardOpen && LanProfile.Current == null);
                screen.Back();
                yield return Until(() => SceneManager.GetActiveScene().path == ConfrontationMenu.ScenePath, 15);
                Check("Back on the first-run card returns to the main menu", SceneManager.GetActiveScene().path == ConfrontationMenu.ScenePath);
                LanProfile.Save("Test Ringer");
                yield return new WaitForSecondsRealtime(1.5f);
                yield return OpenLobby(); screen = LanLobbyScreen.Instance;
            }
            else Check("-lastbellProfile names this run without a card", LanProfile.Current != null && !screen.NameCardOpen && !File.Exists(LanProfile.PathOverride));
            if (!screen) { Finish(savedClass); yield break; }
            Check("first-ever LAN class is Tollbreaker", screen.Hero == "Tollbreaker");

            // ---------------------------------------------------------------- host view
            int port = FreePort(); screen.SetConnectTarget("127.0.0.1", port);
            screen.HostButton.onClick.Invoke(); yield return Frames(3);
            var lobby = screen.Session ? screen.Session.Lobby : null;
            Check("Host opens the lobby page", screen.Current == LanLobbyScreen.Page.Lobby && lobby != null && lobby.IsHost && lobby.Connected);
            if (lobby == null) { Finish(savedClass); yield break; }
            Check("the lobby uses the profile name", lobby.Local != null && lobby.Local.Name == LanProfile.Current);
            if (overridden) Check("-lastbellProfile never writes the profile file", !File.Exists(LanProfile.PathOverride));
            yield return Frames(5);
            var s0 = screen.Slots[0];
            Check("slot 1 shows the host: name underneath, Tollbreaker, HOST tag", s0.PlayerName.text == lobby.Local.Name && s0.ClassName.text == "TOLLBREAKER" && s0.HostTag.enabled);
            Check("slot 1 renders the Tollbreaker model", s0.Cam.enabled && s0.Models.ContainsKey("Tollbreaker") && s0.Models["Tollbreaker"].GetComponentsInChildren<Renderer>().Length > 0);
            Check("empty slots say Waiting for player", screen.Slots.Skip(1).All(s => s.Empty.enabled && !s.Cam.enabled));
            Check("only your own slot has class arrows", s0.Prev.gameObject.activeInHierarchy && screen.Slots.Skip(1).All(s => !s.Next.gameObject.activeInHierarchy));
            s0.Next.onClick.Invoke(); yield return Frames(2);
            Check("class arrows cycle the class and its model", screen.Hero == "Chimesinger" && lobby.Local.ClassName == "Chimesinger" && s0.ClassName.text == "CHIMESINGER" && s0.Models.ContainsKey("Chimesinger"));
            s0.Prev.onClick.Invoke(); yield return Frames(2);
            Check("host sees boss and arena pickers", screen.BossNext.gameObject.activeInHierarchy && screen.ArenaNext.gameObject.activeInHierarchy);
            string boss0 = lobby.Encounter, arena0 = lobby.Arena;
            screen.BossNext.onClick.Invoke(); screen.ArenaNext.onClick.Invoke(); yield return null;
            Check("boss picker moves to another LAN boss", lobby.Encounter != boss0 && LanLobbyScreen.BossChoices.Contains(lobby.Encounter));
            Check("arena picker moves to another arena", lobby.Arena != arena0 && LanLobbyScreen.ArenaChoices.Contains(lobby.Arena));
            Note("LAN bosses: " + string.Join(", ", LanLobbyScreen.BossChoices) + "; arenas: " + string.Join(", ", LanLobbyScreen.ArenaChoices));
            lobby.SetSelection("metronome-court", "Tempo Tyrant"); yield return null;
            Check("Start is disabled alone", !screen.StartButton.interactable && screen.Status == "Need at least 2 players.");
            string[] guests = { "Ana", "Bo", "Cy" }; string[] guestClass = { "Arcanist", "Chimesinger", "Tollbreaker" };
            for (int i = 0; i < guests.Length; i++) raw.Add(LanLobby.Connect("127.0.0.1", port, LanLobbyScreen.BuildId(), guestClass[i], guests[i]));
            yield return Until(() => lobby.Members.Count == 4 && raw.All(r => r.Connected), 10); yield return Frames(4);
            Check("three more players fill the slots, names underneath", Enumerable.Range(1, 3).All(i => screen.Slots[i].PlayerName.text == lobby.Members.OrderBy(m => m.Id).ElementAt(i).Name) && screen.Slots.All(s => !s.Empty.enabled));
            Check("each slot shows that player's class model", screen.Slots.All(s => s.Cam.enabled && s.ShownClass != null) && screen.Slots.Select(s => s.ShownClass).SequenceEqual(lobby.Members.OrderBy(m => m.Id).Select(m => m.ClassName)));
            screen.ReadyButton.onClick.Invoke(); yield return null;
            Check("Ready marks the host ready, locks the class arrows, reads Not ready", lobby.LocalReady && !s0.Next.interactable && screen.ReadyButton.GetComponentInChildren<Text>().text == "Not ready");
            screen.ChangeClass(1); Check("class can't change while ready", screen.Hero == "Tollbreaker");
            Check("Start stays locked until all are ready", !screen.StartButton.interactable && screen.Status == "Waiting for 3 to ready.");
            foreach (var r in raw) r.SetReady(true);
            yield return Until(() => lobby.CanStart, 5); yield return null;
            Check("Start unlocks when everyone is ready", screen.StartButton.interactable && screen.Slots.All(s => s.Tag.text == "READY"));
            screen.BossNext.onClick.Invoke(); yield return Frames(2);
            Check("changing the boss un-readies everyone and locks Start", !screen.StartButton.interactable && screen.Slots.All(s => s.Tag.text == "not ready"));
            lobby.SetSelection("metronome-court", "Tempo Tyrant"); lobby.SetReady(true); foreach (var r in raw) r.SetReady(true);
            yield return Until(() => lobby.CanStart, 5);
            // frame rate with four live class models
            float t0 = Time.realtimeSinceStartup; int f0 = Time.frameCount; yield return new WaitForSecondsRealtime(3);
            float fps = (Time.frameCount - f0) / (Time.realtimeSinceStartup - t0);
            Note("lobby frame rate with four class models: " + fps.ToString("0") + " fps at " + Screen.width + "x" + Screen.height);
            Check("lobby runs at a usable frame rate with four models (>= 20 fps on this shared test machine)", fps >= 20);
            yield return Shot("host-lobby.png");
            screen.Back(); yield return null;
            Check("Back in a lobby asks before leaving", screen.LeaveConfirmOpen);
            screen.StayButton.onClick.Invoke(); yield return null;
            Check("Stay keeps the lobby", !screen.LeaveConfirmOpen && screen.Session);
            foreach (var r in raw) r.Dispose(); raw.Clear();
            yield return Until(() => lobby.Members.Count == 1, 5);
            screen.Back(); screen.LeaveButton.onClick.Invoke();
            yield return Until(() => SceneManager.GetActiveScene().path == ConfrontationMenu.ScenePath, 15);
            Check("Leave closes the lobby and returns to the main menu", SceneManager.GetActiveScene().path == ConfrontationMenu.ScenePath && !LanSessionCoordinator.Instance);
            yield return new WaitForSecondsRealtime(1.5f);

            // ---------------------------------------------------------------- client view
            int port2 = FreePort(); var remote = LanLobby.Host(port2, LanLobbyScreen.BuildId(), "Arcanist", "Remote Host"); raw.Add(remote);
            remote.SetSelection("skyward-spire", "Aurelion");
            yield return OpenLobby(); screen = LanLobbyScreen.Instance;
            screen.SetConnectTarget("127.0.0.1", port2); screen.JoinButton.onClick.Invoke();
            yield return Until(() => screen.Session && screen.Session.Lobby != null && screen.Session.Lobby.Connected, 10); yield return Frames(4);
            var mine = screen.Session ? screen.Session.Lobby : null;
            Check("Connect joins as a client", mine != null && mine.Connected && !mine.IsHost && remote.Members.Count == 2);
            if (mine == null) { Finish(savedClass); yield break; }
            Check("client sees the host's boss and arena, without pickers", !screen.BossNext.gameObject.activeInHierarchy && !screen.ArenaPrev.gameObject.activeInHierarchy && mine.Encounter == "Aurelion" && mine.Arena == "skyward-spire");
            Check("client has no Start button", !screen.StartButton.gameObject.activeInHierarchy);
            Check("slot 1 is the host (HOST tag), the client's own slot is 2 with arrows", screen.Slots[0].PlayerName.text == "Remote Host" && screen.Slots[0].HostTag.enabled && screen.Slots[1].Next.gameObject.activeInHierarchy && !screen.Slots[0].Next.gameObject.activeInHierarchy);
            screen.ReadyButton.onClick.Invoke(); yield return Until(() => remote.Members.Any(m => m.Id == mine.LocalId && m.Ready), 5);
            Check("client Ready reaches the host", remote.Members.Any(m => m.Id == mine.LocalId && m.Ready));
            remote.SetSelection("skyward-spire", "Pendulum Sentinel");
            yield return Until(() => mine.Encounter == "Pendulum Sentinel", 5); yield return Frames(2);
            Check("client is told when the host changes the boss, and is un-readied", screen.Notice.Contains("changed the boss") && !mine.LocalReady && screen.ReadyButton.GetComponentInChildren<Text>().text == "Ready");
            yield return Shot("client-lobby.png");
            remote.Dispose(); raw.Clear();
            yield return Until(() => screen.Current == LanLobbyScreen.Page.Connect, 10);
            Check("if the host leaves, the client stays in the lobby scene and sees why", screen.Current == LanLobbyScreen.Page.Connect && SceneManager.GetActiveScene().path == LanLobbyScreen.ScenePath && screen.Status.Length > 0);
            Note("after host left: " + screen.Status);
            screen.Back();
            yield return Until(() => SceneManager.GetActiveScene().path == ConfrontationMenu.ScenePath, 15);
            Check("Back on the connect page returns to the main menu", SceneManager.GetActiveScene().path == ConfrontationMenu.ScenePath);
            Finish(savedClass);
        }
        void Finish(string savedClass)
        {
            foreach (var r in raw) r.Dispose(); raw.Clear();
            if (LanSessionCoordinator.Instance) LanSessionCoordinator.Instance.Leave();
            LanProfile.PathOverride = null;
            if (savedClass != null) PlayerPrefs.SetString("LastBell.Lan.Class", savedClass); else PlayerPrefs.DeleteKey("LastBell.Lan.Class");
            results.Add("RESULT " + results.Count(r => r.StartsWith("PASS")) + " passed, " + failures + " failed");
            File.WriteAllLines(Path.Combine(outDir, "lan-menu-test.txt"), results);
            foreach (var line in results) Debug.Log("[LAN menu test] " + line);
            Application.Quit(failures == 0 ? 0 : 1);
        }
    }
}
