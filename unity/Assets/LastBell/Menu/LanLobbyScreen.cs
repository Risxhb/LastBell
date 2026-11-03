#if LASTBELL_LAN_TEST
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using LastBell.Run;
using LastBell.Run.Networking;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LastBell.Menu
{
    // The LAN lobby scene (docs/lan-lobby-plan-20261102.md). Three layers, all built in code:
    //   name card (LanProfile) -> Host / Connect page -> lobby page.
    // Lobby page: the host picks the boss and arena (clients see them read-only), four player slots show each player's
    // class model with their name underneath, everyone picks a class in their own slot and presses Ready, and the host's
    // Start unlocks when LanLobby.CanStart. Every rule is enforced by LanLobby on the host; this screen only mirrors it.
    public sealed class LanLobbyScreen : MonoBehaviour
    {
        public static string ScenePath => LanSessionCoordinator.LobbyScenePath;
        public static LanLobbyScreen Instance { get; private set; }
        const string ClassKey = "LastBell.Lan.Class", AddressKey = "LastBell.Lan.Address", PortKey = "LastBell.Lan.Port";
        public const int DefaultPort = 7777;

        public static void Open() => SceneManager.LoadScene(ScenePath, LoadSceneMode.Single);
        static bool hooked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            // The scene file holds only a camera and a light, so the editor (without LASTBELL_LAN_TEST) can create it;
            // the screen adds itself whenever that scene loads.
            if (hooked) return; hooked = true;
            SceneManager.sceneLoaded += (scene, mode) => { if (scene.path == ScenePath) new GameObject("LAN lobby").AddComponent<LanLobbyScreen>(); };
        }

        internal static string BuildId()
        {
            // The installed managed assembly identifies the exact executable code; asset identity still belongs in
            // the packaged build manifest before distribution.
            using (var sha = SHA256.Create()) using (var file = File.OpenRead(typeof(LanLobby).Assembly.Location))
                return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
        }

        // ------------------------------------------------------------------ state
        public enum Page { Connect, Lobby }
        public Page Current { get; private set; } = Page.Connect;
        public LanSessionCoordinator Session => session;
        public string Hero => hero;
        public string Status => status ? status.text : "";
        public string Notice => notice ? notice.text : "";
        public bool NameCardOpen => nameCard && nameCard.activeSelf;
        public bool LeaveConfirmOpen => leaveConfirm && leaveConfirm.activeSelf;
        LanSessionCoordinator session; string hero, err; float errorUntil;
        // A message from the last action (refused class change, connect failure); it fades after a few seconds.
        string error { get => Time.unscaledTime < errorUntil ? err : null; set { err = value; errorUntil = Time.unscaledTime + 6; } }
        int seenSelection = -1; float noticeUntil;

        // ------------------------------------------------------------------ UI
        Canvas canvas; RectTransform root; GameObject connectPage, lobbyPage, nameCard, leaveConfirm;
        InputField address, port, nameField; Text status, playingAs, nameError, notice, bossName, arenaName, hint, hostAddress, title;
        public Button HostButton, JoinButton, EditNameButton, ConfirmNameButton, ReadyButton, StartButton, BackButton, BossPrev, BossNext, ArenaPrev, ArenaNext, LeaveButton, StayButton;
        RawImage arenaArt;
        public sealed class Slot
        {
            public RectTransform Card; public Image Fill; public StrokeGraphic Edge; public RawImage Art; public Text ClassName, PlayerName, Tag, HostTag, Empty;
            public Button Prev, Next; public Camera Cam; public RenderTexture Texture; public Transform Stage;
            public readonly Dictionary<string, CharacterPresenter> Models = new Dictionary<string, CharacterPresenter>();
            public string ShownClass; public int MemberId = -1;
        }
        public readonly Slot[] Slots = new Slot[LanLobby.Capacity];
        Light key;

        void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }
        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            foreach (var s in Slots) if (s != null && s.Texture) { if (s.Cam) s.Cam.targetTexture = null; s.Texture.Release(); Destroy(s.Texture); }
        }

        void Start()
        {
            hero = PlayerPrefs.GetString(ClassKey, LanLobby.DefaultClass);
            if (!LanLobby.ValidClass(hero)) hero = LanLobby.DefaultClass;   // first-ever default: Tollbreaker
            try { MenuMusic.Ensure().PlayMenu(); } catch (Exception ex) { Debug.LogWarning("[LAN] lobby music: " + ex.Message); }
            var view = Camera.main;
            if (view) { view.clearFlags = CameraClearFlags.SolidColor; view.backgroundColor = new Color(.012f, .014f, .026f, 1); view.cullingMask = 0; }
            key = new GameObject("Lobby key light").AddComponent<Light>(); key.type = LightType.Directional; key.intensity = 1.3f;
            key.color = new Color(1f, .95f, .88f); key.transform.rotation = Quaternion.Euler(28, 160, 0);
            canvas = MenuUI.Canvas(transform); root = (RectTransform)canvas.transform;
            MenuUI.Panel("Backdrop", root, Vector2.zero, Vector2.one, new Color(.012f, .014f, .026f, 1));
            title = MenuUI.Text("LAN MULTIPLAYER", root, new Vector2(.04f, .90f), new Vector2(.5f, .98f), 42, MenuUI.Ivory, TextAnchor.MiddleLeft);
            playingAs = MenuUI.Text("", root, new Vector2(.48f, .91f), new Vector2(.78f, .97f), 20, MenuUI.Gold, TextAnchor.MiddleRight);
            EditNameButton = MenuUI.Button("Edit name", root, new Vector2(.79f, .91f), new Vector2(.96f, .97f), () => OpenNameCard());
            status = MenuUI.Text("", root, new Vector2(.04f, .135f), new Vector2(.96f, .18f), 19, MenuUI.Ivory);
            BackButton = MenuUI.Button("Back", root, new Vector2(.04f, .04f), new Vector2(.19f, .11f), Back);
            BuildConnectPage(); BuildLobbyPage(); BuildNameCard(); BuildLeaveConfirm();
            ShowPage(Page.Connect);
            if (LanProfile.Current == null) OpenNameCard();
        }

        // ------------------------------------------------------------------ connect page
        void BuildConnectPage()
        {
            connectPage = MenuUI.Rect("Connect page", root, Vector2.zero, Vector2.one).gameObject; var t = connectPage.transform;
            MenuUI.FramedPanel("Connect card", t, new Vector2(.28f, .30f), new Vector2(.72f, .80f));
            MenuUI.Text("HOST A GAME", t, new Vector2(.31f, .70f), new Vector2(.69f, .76f), 26, MenuUI.Gold);
            HostButton = MenuUI.Button("Host LAN", t, new Vector2(.36f, .615f), new Vector2(.64f, .685f), Host);
            MenuUI.Text("OR JOIN ONE", t, new Vector2(.31f, .53f), new Vector2(.69f, .59f), 26, MenuUI.Gold);
            MenuUI.Text("Host address", t, new Vector2(.31f, .455f), new Vector2(.45f, .515f), 18, MenuUI.Gold, TextAnchor.MiddleLeft);
            address = Field(t, new Vector2(.45f, .455f), new Vector2(.69f, .515f), PlayerPrefs.GetString(AddressKey, "127.0.0.1"));
            MenuUI.Text("Port", t, new Vector2(.31f, .385f), new Vector2(.45f, .445f), 18, MenuUI.Gold, TextAnchor.MiddleLeft);
            port = Field(t, new Vector2(.45f, .385f), new Vector2(.56f, .445f), PlayerPrefs.GetInt(PortKey, DefaultPort).ToString()); port.contentType = InputField.ContentType.IntegerNumber;
            JoinButton = MenuUI.Button("Connect", t, new Vector2(.57f, .38f), new Vector2(.69f, .45f), Join);
            MenuUI.Text("Trusted local networks only. The host's screen shows the address to type.", t, new Vector2(.31f, .315f), new Vector2(.69f, .37f), 15, MenuUI.Ivory);
            Vertical(HostButton, address, port, JoinButton);
        }
        static InputField Field(Transform parent, Vector2 min, Vector2 max, string value)
        {
            var background = MenuUI.Panel("Input", parent, min, max, new Color(.07f, .07f, .10f, 1)); background.raycastTarget = true;
            var input = background.gameObject.AddComponent<InputField>(); input.targetGraphic = background;
            var text = MenuUI.Text("", background.transform, new Vector2(.03f, 0), new Vector2(.97f, 1), 20, MenuUI.Ivory, TextAnchor.MiddleLeft);
            text.supportRichText = false; input.textComponent = text; input.text = value; return input;
        }
        bool ReadPort(out int value)
        {
            if (int.TryParse(port.text, out value) && value > 0 && value < 65536) return true;
            error = "Enter a valid port (1–65535)."; return false;
        }
        public void Host()
        {
            if (LanProfile.Current == null) { OpenNameCard(); return; }
            if (!ReadPort(out int p)) return;
            try { session = LanSessionCoordinator.Open(LanLobby.Host(p, BuildId(), hero, LanProfile.Current)); error = null; PlayerPrefs.SetInt(PortKey, p); }
            catch (Exception e) { error = "Could not host: " + e.Message; return; }
            ShowPage(Page.Lobby);
        }
        public void Join()
        {
            if (LanProfile.Current == null) { OpenNameCard(); return; }
            if (!ReadPort(out int p)) return;
            string host = address.text.Trim();
            try { session = LanSessionCoordinator.Open(LanLobby.Connect(host, p, BuildId(), hero, LanProfile.Current)); error = null; PlayerPrefs.SetString(AddressKey, host); PlayerPrefs.SetInt(PortKey, p); }
            catch (Exception e) { error = "Could not connect: " + e.Message; return; }
            ShowPage(Page.Lobby);
        }
        // Tests: point the connect page at a port / address without typing.
        public void SetConnectTarget(string host, int portNumber) { address.text = host; port.text = portNumber.ToString(); }

        // ------------------------------------------------------------------ lobby page
        void BuildLobbyPage()
        {
            lobbyPage = MenuUI.Rect("Lobby page", root, Vector2.zero, Vector2.one).gameObject; var t = lobbyPage.transform;
            hostAddress = MenuUI.Text("", t, new Vector2(.04f, .855f), new Vector2(.70f, .90f), 18, MenuUI.Ivory, TextAnchor.MiddleLeft);
            // Boss (left) and arena (right): the host's pickers; clients see the same labels without arrows.
            MenuUI.FramedPanel("Boss bar", t, new Vector2(.04f, .755f), new Vector2(.49f, .85f), null, false);
            MenuUI.Text("BOSS", t, new Vector2(.05f, .76f), new Vector2(.13f, .845f), 18, MenuUI.Gold, TextAnchor.MiddleLeft);
            BossPrev = MenuUI.Button("<", t, new Vector2(.13f, .77f), new Vector2(.17f, .835f), () => CycleBoss(-1));
            bossName = MenuUI.Text("", t, new Vector2(.17f, .76f), new Vector2(.44f, .845f), 26, MenuUI.Ivory);
            BossNext = MenuUI.Button(">", t, new Vector2(.44f, .77f), new Vector2(.48f, .835f), () => CycleBoss(1));
            MenuUI.FramedPanel("Arena bar", t, new Vector2(.51f, .755f), new Vector2(.96f, .85f), null, false);
            arenaArt = MenuUI.Rect("Arena preview", t, new Vector2(.515f, .762f), new Vector2(.585f, .843f)).gameObject.AddComponent<RawImage>(); arenaArt.raycastTarget = false;
            MenuUI.Text("ARENA", t, new Vector2(.59f, .76f), new Vector2(.66f, .845f), 18, MenuUI.Gold, TextAnchor.MiddleLeft);
            ArenaPrev = MenuUI.Button("<", t, new Vector2(.66f, .77f), new Vector2(.70f, .835f), () => CycleArena(-1));
            arenaName = MenuUI.Text("", t, new Vector2(.70f, .76f), new Vector2(.91f, .845f), 26, MenuUI.Ivory);
            ArenaNext = MenuUI.Button(">", t, new Vector2(.91f, .77f), new Vector2(.95f, .835f), () => CycleArena(1));
            notice = MenuUI.Text("", t, new Vector2(.04f, .715f), new Vector2(.96f, .75f), 18, UiTheme.GoldBright);
            for (int i = 0; i < Slots.Length; i++) Slots[i] = BuildSlot(i, t);
            ReadyButton = MenuUI.Button("Ready", t, new Vector2(.41f, .04f), new Vector2(.59f, .11f), ToggleReady);
            StartButton = MenuUI.Button("Start fight", t, new Vector2(.78f, .04f), new Vector2(.96f, .11f), StartFight);
            hint = MenuUI.Text("", t, new Vector2(.20f, .105f), new Vector2(.80f, .135f), 15, new Color(MenuUI.Ivory.r, MenuUI.Ivory.g, MenuUI.Ivory.b, .7f));
            foreach (var b in new[] { BossPrev, BossNext, ArenaPrev, ArenaNext }) b.navigation = new Navigation { mode = Navigation.Mode.None };
        }
        Slot BuildSlot(int i, Transform t)
        {
            const float left = .04f, width = .2125f, gap = .0167f;
            float x = left + i * (width + gap);
            var s = new Slot();
            s.Fill = MenuUI.FramedPanel("Player slot " + (i + 1), t, new Vector2(x, .22f), new Vector2(x + width, .70f));
            s.Card = s.Fill.rectTransform; s.Edge = s.Card.GetComponentInChildren<StrokeGraphic>();
            s.Art = MenuUI.Rect("Class model", s.Card, new Vector2(.04f, .27f), new Vector2(.96f, .98f)).gameObject.AddComponent<RawImage>(); s.Art.raycastTarget = false;
            s.HostTag = MenuUI.Text("HOST", s.Card, new Vector2(.04f, .90f), new Vector2(.40f, .98f), 16, UiTheme.GoldBright, TextAnchor.MiddleLeft);
            s.Empty = MenuUI.Text("Waiting for player…", s.Card, new Vector2(0, .40f), new Vector2(1, .60f), 20, new Color(MenuUI.Ivory.r, MenuUI.Ivory.g, MenuUI.Ivory.b, .55f));
            s.Prev = MenuUI.Button("<", s.Card, new Vector2(.03f, .175f), new Vector2(.20f, .265f), () => ChangeClass(-1));
            s.ClassName = MenuUI.Text("", s.Card, new Vector2(.18f, .175f), new Vector2(.82f, .265f), 22, MenuUI.Gold);
            s.Next = MenuUI.Button(">", s.Card, new Vector2(.80f, .175f), new Vector2(.97f, .265f), () => ChangeClass(1));
            s.PlayerName = MenuUI.Text("", s.Card, new Vector2(.02f, .085f), new Vector2(.98f, .175f), 24, MenuUI.Ivory);
            s.Tag = MenuUI.Text("", s.Card, new Vector2(.02f, .015f), new Vector2(.98f, .085f), 17, MenuUI.Gold);
            s.Prev.navigation = s.Next.navigation = new Navigation { mode = Navigation.Mode.None };
            // The model stands far away from the other slots' models; this slot's own camera renders it into the card.
            s.Stage = new GameObject("Slot " + (i + 1) + " stage").transform; s.Stage.SetParent(transform, false); s.Stage.position = new Vector3(500 + i * 60, 0, 0);
            s.Texture = new RenderTexture(512, 560, 24, RenderTextureFormat.ARGB32) { name = "Lobby slot " + (i + 1), antiAliasing = 2 };
            s.Cam = new GameObject("Slot " + (i + 1) + " camera").AddComponent<Camera>(); s.Cam.transform.SetParent(transform, false);
            s.Cam.clearFlags = CameraClearFlags.SolidColor; s.Cam.backgroundColor = new Color(0, 0, 0, 0); s.Cam.fieldOfView = 26; s.Cam.nearClipPlane = .1f; s.Cam.farClipPlane = 30;
            s.Cam.targetTexture = s.Texture; s.Cam.enabled = false; s.Art.texture = s.Texture;
            return s;
        }
        void ShowModel(Slot s, string className)
        {
            if (s.ShownClass == className) return;
            foreach (var m in s.Models.Values) m.gameObject.SetActive(false);
            if (className != null)
            {
                if (!s.Models.TryGetValue(className, out var p) || !p)
                {
                    var holder = new GameObject(className + " lobby model").transform; holder.SetParent(s.Stage, false);
                    holder.localRotation = Quaternion.identity;
                    p = CharacterPresenter.ForPlayer(holder, className); Sanitize(p); s.Models[className] = p;
                }
                p.gameObject.SetActive(true); p.transform.parent.gameObject.SetActive(true);
                Frame(s, p);
            }
            s.ShownClass = className; s.Cam.enabled = className != null;
        }
        static void Sanitize(CharacterPresenter p)
        {
            foreach (var c in p.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (var r in p.GetComponentsInChildren<Rigidbody>(true)) { r.isKinematic = true; r.detectCollisions = false; }
            foreach (var b in p.GetComponentsInChildren<MonoBehaviour>(true)) if (b != p) b.enabled = false;
            foreach (var a in p.GetComponentsInChildren<AudioSource>(true)) { a.Stop(); a.enabled = false; }
        }
        // Frames the whole model: height from its renderers (clamped, so a missing model still frames sensibly).
        static void Frame(Slot s, CharacterPresenter p)
        {
            float h = 2.1f;
            var rs = p.GetComponentsInChildren<Renderer>(); if (rs.Length > 0) { var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); h = Mathf.Clamp(b.max.y - s.Stage.position.y, 1.4f, 4.5f); }
            float dist = h * .62f / Mathf.Tan(s.Cam.fieldOfView * .5f * Mathf.Deg2Rad);
            var target = s.Stage.position + Vector3.up * h * .5f;
            s.Cam.transform.position = s.Stage.position + new Vector3(0, h * .55f, dist);
            s.Cam.transform.rotation = Quaternion.LookRotation(target - s.Cam.transform.position);
        }

        // ------------------------------------------------------------------ actions (also used by the diagnostics)
        LanLobby Lobby => session ? session.Lobby : null;
        public bool IsHost => Lobby != null && Lobby.IsHost;
        // Boss and arena lists: the LAN-enabled encounters in catalog order, and the arenas in this build.
        public static string[] BossChoices => Enumerable.Range(0, RunDirector.Bosses.Length).Select(TestRushSetup.CardName).Where(LanLobby.ValidEncounter).Distinct().ToArray();
        public static string[] ArenaChoices => Enumerable.Range(0, RunMaps.All.Length).Where(RunMaps.InBuild).Select(i => RunMaps.All[i].Id).Where(LanLobby.ValidArena).ToArray();
        public void ChangeClass(int direction)
        {
            var list = LanLobby.ClassList; int i = Math.Max(0, list.ToList().IndexOf(hero));
            string next = list[(i + direction + list.Count) % list.Count];
            if (Lobby != null && Lobby.LocalReady) { error = "Press Not ready to change class."; return; }
            if (Lobby != null && !Lobby.SetClass(next)) { error = "Class could not be changed."; return; }
            hero = next; error = null; PlayerPrefs.SetString(ClassKey, hero);
        }
        public void CycleBoss(int direction)
        {
            if (!IsHost) return;
            var list = BossChoices; int i = Array.IndexOf(list, Lobby.Encounter); if (i < 0) i = 0; else i = (i + direction + list.Length) % list.Length;
            if (!Lobby.SetSelection(Lobby.Arena, list[i])) error = "That boss is unavailable.";
        }
        public void CycleArena(int direction)
        {
            if (!IsHost) return;
            var list = ArenaChoices; int i = Array.IndexOf(list, Lobby.Arena); if (i < 0) i = 0; else i = (i + direction + list.Length) % list.Length;
            if (!Lobby.SetSelection(list[i], Lobby.Encounter)) error = "That arena is unavailable.";
        }
        public void ToggleReady()
        {
            if (Lobby == null || !Lobby.Connected) return;
            error = Lobby.SetReady(!Lobby.LocalReady) ? null : "Ready could not be changed.";
        }
        public void StartFight()
        {
            if (!session || !session.HostStart()) error = Lobby != null ? Lobby.StartBlocker ?? "Could not start." : "Host a lobby first.";
        }
        public void Back()
        {
            if (NameCardOpen) { if (LanProfile.Current != null) { nameCard.SetActive(false); Focus(Current == Page.Lobby ? ReadyButton : HostButton); } else ToMainMenu(); return; }
            if (LeaveConfirmOpen) { leaveConfirm.SetActive(false); Focus(ReadyButton); return; }
            if (Current == Page.Lobby && session) { leaveConfirm.SetActive(true); leaveConfirm.transform.SetAsLastSibling(); Focus(StayButton); return; }
            ToMainMenu();
        }
        public void Leave()
        {
            ToMainMenu();
        }
        void ToMainMenu() { if (session) session.Leave(); SceneManager.LoadScene(ConfrontationMenu.ScenePath); }

        // ------------------------------------------------------------------ name card and leave confirm
        void BuildNameCard()
        {
            var shade = MenuUI.Panel("Name card shade", root, Vector2.zero, Vector2.one, new Color(0, 0, 0, .78f)); shade.raycastTarget = true;
            nameCard = shade.gameObject; var c = nameCard.transform;
            MenuUI.FramedPanel("Name card", c, new Vector2(.30f, .33f), new Vector2(.70f, .70f));
            MenuUI.Text("CHOOSE A NAME", c, new Vector2(.33f, .62f), new Vector2(.67f, .68f), 30, MenuUI.Ivory);
            MenuUI.Text("Other players see this name in the lobby and over your character. It's saved on this computer.", c, new Vector2(.33f, .555f), new Vector2(.67f, .615f), 16, MenuUI.Ivory);
            nameField = Field(c, new Vector2(.36f, .48f), new Vector2(.64f, .54f), LanProfile.Saved ?? "");
            nameField.characterLimit = LanPlayerName.MaxLength + 4;   // room to type spaces that get trimmed
            nameError = MenuUI.Text("", c, new Vector2(.33f, .43f), new Vector2(.67f, .475f), 16, MenuUI.Gold);
            var back = MenuUI.Button("Back", c, new Vector2(.36f, .355f), new Vector2(.49f, .415f), Back);
            ConfirmNameButton = MenuUI.Button("Confirm", c, new Vector2(.51f, .355f), new Vector2(.64f, .415f), ConfirmName);
            Vertical(nameField, ConfirmNameButton, back);
            nameCard.SetActive(false);
        }
        public InputField NameField => nameField;
        public void OpenNameCard()
        {
            if (session) return;   // the lobby already has your name; leave to change it
            nameField.text = LanProfile.Current ?? LanProfile.Saved ?? ""; nameCard.SetActive(true); nameCard.transform.SetAsLastSibling();
            Focus(nameField); nameField.ActivateInputField();
        }
        public void ConfirmName()
        {
            if (LanProfile.Overridden) { error = "Name set by -lastbellProfile for this run; not saved."; nameCard.SetActive(false); Focus(HostButton); return; }
            var problem = LanProfile.Save(nameField.text);
            if (problem != null) { nameError.text = problem; return; }
            nameCard.SetActive(false); error = null; Focus(HostButton);
        }
        void BuildLeaveConfirm()
        {
            var shade = MenuUI.Panel("Leave shade", root, Vector2.zero, Vector2.one, new Color(0, 0, 0, .7f)); shade.raycastTarget = true;
            leaveConfirm = shade.gameObject; var c = leaveConfirm.transform;
            MenuUI.FramedPanel("Leave card", c, new Vector2(.35f, .40f), new Vector2(.65f, .60f));
            MenuUI.Text("Leave the lobby?", c, new Vector2(.37f, .52f), new Vector2(.63f, .58f), 26, MenuUI.Ivory);
            StayButton = MenuUI.Button("Stay", c, new Vector2(.38f, .425f), new Vector2(.49f, .49f), Back);
            LeaveButton = MenuUI.Button("Leave", c, new Vector2(.51f, .425f), new Vector2(.62f, .49f), Leave);
            Horizontal(StayButton, LeaveButton);
            leaveConfirm.SetActive(false);
        }

        // ------------------------------------------------------------------ navigation helpers
        static void Focus(Selectable s) { if (s && EventSystem.current) EventSystem.current.SetSelectedGameObject(s.gameObject); }
        static void Vertical(params Selectable[] items)
        {
            for (int i = 0; i < items.Length; i++)
                items[i].navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnDown = items[(i + 1) % items.Length], selectOnUp = items[(i + items.Length - 1) % items.Length] };
        }
        static void Horizontal(params Selectable[] items)
        {
            for (int i = 0; i < items.Length; i++)
                items[i].navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = items[(i + 1) % items.Length], selectOnLeft = items[(i + items.Length - 1) % items.Length] };
        }
        void ShowPage(Page page)
        {
            Current = page; connectPage.SetActive(page == Page.Connect); lobbyPage.SetActive(page == Page.Lobby);
            title.text = page == Page.Lobby ? "LAN LOBBY" : "LAN MULTIPLAYER";
            if (page == Page.Lobby)
            {
                // Lobby: Left/Right change class, so focus moves only up/down between Ready, Start and Back.
                if (IsHost) Vertical(ReadyButton, StartButton, BackButton); else Vertical(ReadyButton, BackButton);
                seenSelection = -1; Focus(ReadyButton);
            }
            else
            {
                foreach (var s in Slots) ShowModel(s, null);
                BackButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = JoinButton, selectOnDown = HostButton };
                JoinButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = port, selectOnDown = BackButton };
                HostButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = BackButton, selectOnDown = address };
                Focus(HostButton);
            }
        }

        // ------------------------------------------------------------------ per frame
        bool stickHeld; int artMap = -2; Texture2D artTexture;
        void Update()
        {
            if (!canvas) return;
            HandleInput();
            Refresh();
        }
        static bool Pressed(Func<Keyboard, bool> k, Func<Gamepad, bool> g) => (Keyboard.current != null && k(Keyboard.current)) || (Gamepad.current != null && g(Gamepad.current));
        void HandleInput()
        {
            bool typing = EventSystem.current && EventSystem.current.currentSelectedGameObject && EventSystem.current.currentSelectedGameObject.GetComponent<InputField>();
            if (Pressed(k => k.escapeKey.wasPressedThisFrame, g => g.buttonEast.wasPressedThisFrame)) { Back(); return; }
            if (NameCardOpen) { if (Pressed(k => k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame, g => false)) ConfirmName(); return; }
            if (LeaveConfirmOpen || typing || Current != Page.Lobby || Lobby == null || session.Loading) return;
            float sx = Gamepad.current != null ? Gamepad.current.leftStick.x.ReadValue() : 0; int stick = 0;
            if (Mathf.Abs(sx) > .6f) { if (!stickHeld) stick = sx > 0 ? 1 : -1; stickHeld = true; } else if (Mathf.Abs(sx) < .3f) stickHeld = false;
            if (stick < 0 || Pressed(k => k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame, g => g.dpad.left.wasPressedThisFrame)) ChangeClass(-1);
            if (stick > 0 || Pressed(k => k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame, g => g.dpad.right.wasPressedThisFrame)) ChangeClass(1);
            if (IsHost)
            {
                if (Pressed(k => k.qKey.wasPressedThisFrame, g => g.leftShoulder.wasPressedThisFrame)) CycleBoss(-1);
                if (Pressed(k => k.eKey.wasPressedThisFrame, g => g.rightShoulder.wasPressedThisFrame)) CycleBoss(1);
                if (Pressed(k => k.zKey.wasPressedThisFrame, g => g.leftTrigger.wasPressedThisFrame)) CycleArena(-1);
                if (Pressed(k => k.cKey.wasPressedThisFrame, g => g.rightTrigger.wasPressedThisFrame)) CycleArena(1);
            }
        }
        void Refresh()
        {
            var lobby = Lobby;
            // The session closed while in the lobby (host left, connection lost): back to the connect page with the reason.
            if (Current == Page.Lobby && !session) { error = LanSessionCoordinator.LastError ?? "The session closed."; ShowPage(Page.Connect); }
            string me = LanProfile.Current;
            playingAs.text = me == null ? "No name yet" : "Playing as " + (lobby != null && lobby.Local != null ? lobby.Local.Name : me);
            EditNameButton.interactable = !session; EditNameButton.gameObject.SetActive(Current == Page.Connect);
            if (NameCardOpen)
            {
                var problem = LanPlayerName.Problem(nameField.text); ConfirmNameButton.interactable = problem == null;
                nameError.text = problem == null ? "" : nameField.text.Length > 0 ? problem : "";
            }
            if (Current == Page.Connect)
            {
                HostButton.interactable = JoinButton.interactable = !session;
                status.text = error ?? LanSessionCoordinator.LastError ?? "Host a game, or type the host's address and connect.";
                return;
            }
            bool connected = lobby != null && lobby.Connected, hosting = connected && lobby.IsHost, loading = session && session.Loading;
            // pickers
            BossPrev.gameObject.SetActive(hosting); BossNext.gameObject.SetActive(hosting); ArenaPrev.gameObject.SetActive(hosting); ArenaNext.gameObject.SetActive(hosting);
            BossPrev.interactable = BossNext.interactable = ArenaPrev.interactable = ArenaNext.interactable = hosting && !loading;
            int map = connected ? RunMaps.IndexOf(lobby.Arena) : -1;
            bossName.text = connected ? lobby.Encounter : "—";
            arenaName.text = map >= 0 ? RunMaps.All[map].Name : "—";
            if (map != artMap) { artMap = map; artTexture = map >= 0 ? TestRushSetup.Preview(map) : null; }
            arenaArt.texture = artTexture; arenaArt.enabled = artTexture;
            if (connected && lobby.SelectionRevision != seenSelection)
            {
                if (!hosting && seenSelection >= 0) noticeUntil = Time.unscaledTime + 5;
                seenSelection = lobby.SelectionRevision;
            }
            notice.text = Time.unscaledTime < noticeUntil ? "The host changed the boss or arena. Press Ready again." : "";
            hostAddress.text = hosting ? "Others connect to " + LocalAddresses() + "   port " + port.text : connected ? "Connected to " + address.text : "Connecting…";
            // slots
            var members = connected ? lobby.Members.OrderBy(m => m.Id).ToList() : new List<LanLobby.Member>();
            if (!connected && lobby != null) members.Add(new LanLobby.Member { Id = -1, Name = me ?? "", ClassName = hero });
            for (int i = 0; i < Slots.Length; i++)
            {
                var s = Slots[i]; var m = i < members.Count ? members[i] : null; bool mine = m != null && connected && m.Id == lobby.LocalId;
                s.MemberId = m != null ? m.Id : -1;
                ShowModel(s, m != null ? (mine ? hero : m.ClassName) : null);
                s.Art.enabled = m != null; s.Empty.enabled = m == null;
                s.ClassName.text = m != null ? (mine ? hero : m.ClassName).ToUpperInvariant() : "";
                s.PlayerName.text = m != null ? m.Name : "";
                s.HostTag.enabled = m != null && m.Id == 0;
                s.Tag.text = m == null ? "" : m.Ready ? "READY" : "not ready";
                s.Tag.color = m != null && m.Ready ? UiTheme.GoldBright : new Color(MenuUI.Ivory.r, MenuUI.Ivory.g, MenuUI.Ivory.b, .55f);
                s.Prev.gameObject.SetActive(mine); s.Next.gameObject.SetActive(mine);
                s.Prev.interactable = s.Next.interactable = mine && !lobby.LocalReady && !loading;
                float edge = mine ? 3 : 1;
                if (s.Edge && s.Edge.Thickness != edge) { s.Edge.color = mine ? UiTheme.GoldBright : UiTheme.GoldDim; s.Edge.Thickness = edge; s.Edge.SetVerticesDirty(); }
            }
            // buttons and status
            ReadyButton.interactable = connected && !loading;
            ReadyButton.GetComponentInChildren<Text>().text = connected && lobby.LocalReady ? "Not ready" : "Ready";
            StartButton.gameObject.SetActive(hosting); StartButton.interactable = hosting && lobby.CanStart && !loading;
            hint.text = hosting ? "Left / Right: class    Enter / A: ready    Q / E or LB / RB: boss    Z / C or LT / RT: arena"
                                : "Left / Right: class    Enter / A: ready";
            status.text = error ?? (lobby != null && lobby.Error != null ? lobby.Error
                : loading ? "Loading arena…"
                : !connected ? "Connecting…"
                : hosting ? (lobby.StartBlocker ?? "Everyone is ready. Start the fight.")
                : lobby.LocalReady ? "Ready. Waiting for the host to start." : "Pick your class and press Ready.");
        }
        static string cachedAddresses;
        // The host's IPv4 addresses on this computer, for others to type (cached: one DNS lookup per lobby).
        static string LocalAddresses()
        {
            if (cachedAddresses != null) return cachedAddresses;
            try
            {
                var found = Dns.GetHostEntry(Dns.GetHostName()).AddressList.Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)).Select(a => a.ToString()).Distinct().Take(3).ToArray();
                cachedAddresses = found.Length > 0 ? string.Join("  or  ", found) : "this computer's address";
            }
            catch { cachedAddresses = "this computer's address"; }
            return cachedAddresses;
        }
    }
}
#endif
