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
            // The scene file holds only a camera and a light;
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
        // Main-menu language (docs/ui/ui-kit.md): no boxes, the right side gradient, eyebrow / Ivory title / gold rule,
        // text rows with the highlight strip, drawn chevrons, underline input fields and centre-band dialogs.
        Canvas canvas; RectTransform root, frame; GameObject connectPage, lobbyPage, nameCard, leaveConfirm;
        InputField address, port, nameField; Text status, playingAs, nameError, notice, bossName, arenaName, hint, hostAddress, leaveBody, editNameLabel;
        Header header; Chooser bossChooser, arenaChooser; CanvasGroup frameGroup;
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
            if (view) { view.clearFlags = CameraClearFlags.SolidColor; view.backgroundColor = UiTheme.Backdrop; view.cullingMask = 0; }
            key = new GameObject("Lobby key light").AddComponent<Light>(); key.type = LightType.Directional; key.intensity = 1.3f;
            key.color = new Color(1f, .95f, .88f); key.transform.rotation = Quaternion.Euler(28, 160, 0);
            canvas = MenuUI.Canvas(transform); root = (RectTransform)canvas.transform;
            MenuUI.Panel("Backdrop", root, Vector2.zero, Vector2.one, UiTheme.Backdrop);
            MenuUI.SideGradient(root, new Vector2(.42f, 0), Vector2.one);
            // One content frame for everything but the backdrop: ultrawide narrows it to a centred 16:9 frame (Update).
            frame = MenuUI.Rect("Content", root, Vector2.zero, Vector2.one); frameGroup = frame.gameObject.AddComponent<CanvasGroup>();
            header = MenuUI.Header(frame, "LAN Multiplayer", "Host or Join", new Vector2(.04f, .835f), new Vector2(.6f, .955f));
            playingAs = MenuUI.Text("", frame, new Vector2(.6f, .905f), new Vector2(.96f, .955f), UiTheme.SizeBody, UiTheme.IvoryMuted, TextAnchor.MiddleRight);
            status = MenuUI.Text("", frame, new Vector2(.04f, .16f), new Vector2(.96f, .2f), 19, UiTheme.IvoryBody);
            hint = MenuUI.Text("", frame, new Vector2(.15f, .126f), new Vector2(.85f, .158f), 15, UiTheme.IvoryMuted);
            MenuUI.Panel("Footer rule", frame, new Vector2(.04f, .122f), new Vector2(.96f, .1235f), UiTheme.RuleFaint);
            BackButton = MenuUI.Row("Back", frame, new Vector2(.035f, .035f), new Vector2(.19f, .105f), Back, 24, Vector2.zero);
            BackButton.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
            BuildConnectPage(); BuildLobbyPage(); BuildNameCard(); BuildLeaveConfirm();
            hint.transform.SetParent(root, false);   // over the dialogs too (kept last in Refresh)
            ShowPage(Page.Connect);
            if (LanProfile.Current == null) OpenNameCard();
        }

        // ------------------------------------------------------------------ connect page
        void BuildConnectPage()
        {
            // Right-hand column like Settings: your name, Host (the main action), then the join fields and Connect.
            connectPage = MenuUI.Rect("Connect page", frame, Vector2.zero, Vector2.one).gameObject;
            var c = MenuUI.Rect("Connect column", connectPage.transform, new Vector2(.60f, .2f), new Vector2(.96f, .82f));
            float Y(int i) => .87f - i * .105f; const float H = .085f;
            Color cueColor = new Color(UiTheme.Gold.r, UiTheme.Gold.g, UiTheme.Gold.b, .6f);
            void Caption(string text, int i) => MenuUI.Text(text, c, new Vector2(0, Y(i)), new Vector2(.4f, Y(i) + H), UiTheme.SizeBody, UiTheme.IvoryBody, TextAnchor.MiddleLeft);
            void Eyebrow(string text, float y) => MenuUI.Text(UiTheme.Spaced(text), c, new Vector2(0, y), new Vector2(1, y + .04f), 14, UiTheme.GoldLabel, TextAnchor.MiddleLeft);
            // name: a value row (caption left, the name as the row's label, a chevron cue); it opens the name card
            EditNameButton = MenuUI.Row("Choose a name", c, new Vector2(0, Y(0)), new Vector2(1, Y(0) + H), () => OpenNameCard(), 22, Vector2.zero);
            editNameLabel = EditNameButton.GetComponentInChildren<Text>(); var nr = editNameLabel.rectTransform;
            nr.anchorMin = new Vector2(.42f, 0); nr.offsetMin = new Vector2(12, 3); nr.offsetMax = new Vector2(-34, -3); editNameLabel.alignment = TextAnchor.MiddleLeft;
            MenuUI.Text("Your name", EditNameButton.transform, Vector2.zero, new Vector2(.42f, 1), UiTheme.SizeBody, UiTheme.IvoryBody, TextAnchor.MiddleLeft);
            var cue = MenuUI.Chevron(EditNameButton.transform, true, 16, cueColor); cue.anchorMin = cue.anchorMax = new Vector2(1, .5f); cue.anchoredPosition = new Vector2(-14, 0);
            var divider = MenuUI.Panel("Row divider", c, new Vector2(0, Y(0) - .012f), new Vector2(1, Y(0) - .012f), UiTheme.RuleFaint); divider.rectTransform.sizeDelta = new Vector2(0, 1);
            Eyebrow("Host a game", Y(1) + .02f);
            HostButton = MenuUI.Row("Host LAN game", c, new Vector2(0, Y(2) + .02f), new Vector2(1, Y(2) + .02f + H + .01f), Host, 28, Vector2.zero);
            HostButton.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
            Eyebrow("Or join one", Y(3) + .02f);
            Caption("Host address", 4);
            address = Field(c, new Vector2(.42f, Y(4)), new Vector2(1, Y(4) + H), PlayerPrefs.GetString(AddressKey, "127.0.0.1"), "192.168.0.10");
            Caption("Port", 5);
            port = Field(c, new Vector2(.42f, Y(5)), new Vector2(.7f, Y(5) + H), PlayerPrefs.GetInt(PortKey, DefaultPort).ToString(), DefaultPort.ToString()); port.contentType = InputField.ContentType.IntegerNumber;
            JoinButton = MenuUI.Row("Connect", c, new Vector2(.42f, Y(6)), new Vector2(1, Y(6) + H), Join, 24, Vector2.zero);
            JoinButton.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
            MenuUI.Text("Trusted local networks only. The host's screen shows the address to type.", c, new Vector2(0, Y(7) - .02f), new Vector2(1, Y(7) + H - .01f), 15, UiTheme.IvoryMuted, TextAnchor.UpperLeft);
        }
        // Input field in the row language, no box: a transparent hit area, an underline (Ivory .35, GoldBright on focus),
        // the highlight strip on focus or hover, a GoldBright caret and a muted placeholder.
        static InputField Field(Transform parent, Vector2 min, Vector2 max, string value, string placeholder)
        {
            var r = MenuUI.Rect("Input", parent, min, max);
            var hit = r.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, 0); hit.raycastTarget = true;
            var strip = MenuUI.Strip(r);
            var line = MenuUI.Panel("Field underline", r, Vector2.zero, new Vector2(1, 0), UiTheme.Ivory);
            line.rectTransform.pivot = new Vector2(.5f, 0); line.rectTransform.sizeDelta = new Vector2(0, 1); line.rectTransform.anchoredPosition = new Vector2(0, 4);
            var input = r.gameObject.AddComponent<InputField>(); input.targetGraphic = hit; input.transition = Selectable.Transition.None;
            var text = MenuUI.Text("", r, Vector2.zero, Vector2.one, 22, UiTheme.Ivory, TextAnchor.MiddleLeft); text.supportRichText = false;
            text.rectTransform.offsetMin = new Vector2(12, 6); text.rectTransform.offsetMax = new Vector2(-12, -3);
            var hint = MenuUI.Text(placeholder, r, Vector2.zero, Vector2.one, 20, UiTheme.IvoryMicro, TextAnchor.MiddleLeft);
            hint.rectTransform.offsetMin = new Vector2(12, 6); hint.rectTransform.offsetMax = new Vector2(-12, -3);
            input.textComponent = text; input.placeholder = hint;
            input.customCaretColor = true; input.caretColor = UiTheme.GoldBright; input.caretWidth = 2; input.selectionColor = UiTheme.StripFill;
            var focus = r.gameObject.AddComponent<NavFocus>(); focus.Strip = strip; focus.Icon = line; focus.IconRest = new Color(UiTheme.Ivory.r, UiTheme.Ivory.g, UiTheme.Ivory.b, .35f);
            input.text = value; return input;
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
            lobbyPage = MenuUI.Rect("Lobby page", frame, Vector2.zero, Vector2.one).gameObject; var t = lobbyPage.transform;
            hostAddress = MenuUI.Text("", t, new Vector2(.04f, .775f), new Vector2(.6f, .825f), 18, UiTheme.IvoryMuted, TextAnchor.MiddleLeft);
            // Boss (left) and arena (right): the host's choosers; clients see the same values without chevrons.
            MenuUI.Text(UiTheme.Spaced("Boss"), t, new Vector2(.04f, .735f), new Vector2(.47f, .765f), 14, UiTheme.GoldLabel, TextAnchor.MiddleLeft);
            // left-aligned choosers under their eyebrows: the value label starts after the previous chevron and the next
            // chevron follows the rendered text (PlaceChooser, every frame)
            var bossBox = MenuUI.Rect("Boss chooser", t, new Vector2(.04f, .675f), new Vector2(.47f, .735f));
            bossChooser = MenuUI.Chooser(bossBox, "", Vector2.zero, Vector2.one, 26, () => CycleBoss(-1), () => CycleBoss(1));
            bossName = bossChooser.Label; BossPrev = bossChooser.Previous; BossNext = bossChooser.Next;
            MenuUI.Text(UiTheme.Spaced("Arena"), t, new Vector2(.53f, .735f), new Vector2(.96f, .765f), 14, UiTheme.GoldLabel, TextAnchor.MiddleLeft);
            arenaArt = MenuUI.Rect("Arena preview", t, new Vector2(.535f, .678f), new Vector2(.59f, .732f)).gameObject.AddComponent<RawImage>(); arenaArt.raycastTarget = false;
            var arenaBox = MenuUI.Rect("Arena chooser", t, new Vector2(.595f, .675f), new Vector2(.96f, .735f));
            arenaChooser = MenuUI.Chooser(arenaBox, "", Vector2.zero, Vector2.one, 26, () => CycleArena(-1), () => CycleArena(1));
            arenaName = arenaChooser.Label; ArenaPrev = arenaChooser.Previous; ArenaNext = arenaChooser.Next;
            foreach (var label in new[] { bossName, arenaName }) { label.alignment = TextAnchor.MiddleLeft; label.rectTransform.offsetMin = new Vector2(44, 3); label.rectTransform.offsetMax = new Vector2(-44, -3); }
            notice = MenuUI.Text("", t, new Vector2(.04f, .638f), new Vector2(.96f, .672f), 18, UiTheme.GoldBright);
            for (int i = 0; i < Slots.Length; i++) Slots[i] = BuildSlot(i, t);
            ReadyButton = MenuUI.Row("Ready", t, new Vector2(.41f, .035f), new Vector2(.59f, .105f), ToggleReady, 24, Vector2.zero);
            StartButton = MenuUI.Row("Start fight", t, new Vector2(.71f, .03f), new Vector2(.96f, .108f), StartFight, 28, Vector2.zero);
            StartButton.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleRight;
            foreach (var b in new[] { BossPrev, BossNext, ArenaPrev, ArenaNext }) b.navigation = new Navigation { mode = Navigation.Mode.None };
        }
        // Player card in Boss Rush's card language: a faint 1 px edge over a light dim, no box or ornaments. Your own card
        // gets a GoldBright edge and the gold accent; its class has drawn chevrons.
        Slot BuildSlot(int i, Transform t)
        {
            const float left = .04f, width = .2125f, gap = .0167f;
            float x = left + i * (width + gap);
            var s = new Slot();
            s.Fill = MenuUI.Panel("Player slot " + (i + 1), t, new Vector2(x, .2f), new Vector2(x + width, .625f), new Color(0, 0, 0, .35f));
            s.Card = s.Fill.rectTransform; s.Edge = MenuUI.Stroke(s.Card, UiTheme.RuleFaint, 1.5f);   // 1.5: a 1 px edge drops out at odd scales
            var accent = MenuUI.Panel("Your accent", s.Card, Vector2.zero, new Vector2(1, 0), UiTheme.Gold); accent.rectTransform.pivot = new Vector2(.5f, 0); accent.rectTransform.sizeDelta = new Vector2(0, 4);
            var holder = MenuUI.Rect("Class model frame", s.Card, new Vector2(.04f, .30f), new Vector2(.96f, .88f));   // the fitter fills this, not the card
            s.Art = MenuUI.Rect("Class model", holder, Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>(); s.Art.raycastTarget = false;
            var fit = s.Art.gameObject.AddComponent<AspectRatioFitter>(); fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent; fit.aspectRatio = 512f / 560;   // never stretch the model
            MenuUI.Text((i + 1).ToString("00"), s.Card, new Vector2(.04f, .9f), new Vector2(.4f, .98f), 14, UiTheme.Gold, TextAnchor.MiddleLeft);
            s.HostTag = MenuUI.Text(UiTheme.Spaced("Host"), s.Card, new Vector2(.5f, .9f), new Vector2(.96f, .98f), 13, UiTheme.GoldBright, TextAnchor.MiddleRight);
            s.Empty = MenuUI.Text("Waiting for player…", s.Card, new Vector2(0, .40f), new Vector2(1, .60f), 20, UiTheme.IvoryMicro);
            s.ClassName = MenuUI.Text("", s.Card, new Vector2(.18f, .2f), new Vector2(.82f, .29f), 20, UiTheme.Gold);
            s.Prev = MenuUI.ChevronButton(s.Card, false, () => ChangeClass(-1)); MenuUI.FitBox(s.Prev, new Vector2(.1f, .245f), 40);
            s.Next = MenuUI.ChevronButton(s.Card, true, () => ChangeClass(1)); MenuUI.FitBox(s.Next, new Vector2(.9f, .245f), 40);
            s.PlayerName = MenuUI.Text("", s.Card, new Vector2(.02f, .1f), new Vector2(.98f, .2f), 24, UiTheme.Ivory);
            s.Tag = MenuUI.Text("", s.Card, new Vector2(.02f, .025f), new Vector2(.98f, .1f), 16, UiTheme.Gold);
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
            if (Current == Page.Lobby && session)
            {
                leaveBody.text = IsHost ? "You are hosting: the lobby closes for everyone." : "You return to the main menu; the others stay.";
                leaveConfirm.SetActive(true); leaveConfirm.transform.SetAsLastSibling(); Focus(StayButton); return;
            }
            ToMainMenu();
        }
        public void Leave()
        {
            ToMainMenu();
        }
        void ToMainMenu() { if (session) session.Leave(); SceneManager.LoadScene(ConfrontationMenu.ScenePath); }

        // ------------------------------------------------------------------ name card and leave confirm
        // Centre-band dialog (the Results / Pause overlay language): a dim, the centre band, a centred header, then rows.
        GameObject Dialog(string name, string eyebrow, string title, out Transform content)
        {
            var shade = MenuUI.Panel(name + " shade", root, Vector2.zero, Vector2.one, UiTheme.ModalDim); shade.raycastTarget = true;
            var band = MenuUI.Rect("Centre band", shade.transform, new Vector2(0, .24f), new Vector2(1, .76f)).gameObject.AddComponent<RawImage>(); band.texture = UiTheme.CentreBand; band.raycastTarget = false;
            var c = MenuUI.Rect(name, shade.transform, new Vector2(.3f, .3f), new Vector2(.7f, .7f)); content = c;
            MenuUI.Header(c, eyebrow, title, new Vector2(0, .68f), new Vector2(1, .92f), 44, TextAnchor.MiddleCenter);
            return shade.gameObject;
        }
        void BuildNameCard()
        {
            nameCard = Dialog("Name card", "LAN Profile", "Choose a name", out var c);
            MenuUI.Text("Other players see this name in the lobby and over your character. It's saved on this computer.", c, new Vector2(.05f, .53f), new Vector2(.95f, .65f), UiTheme.SizeBody, UiTheme.IvoryBody);
            nameField = Field(c, new Vector2(.2f, .39f), new Vector2(.8f, .49f), LanProfile.Saved ?? "", "Your name");
            nameField.characterLimit = LanPlayerName.MaxLength + 4;   // room to type spaces that get trimmed
            nameField.textComponent.alignment = TextAnchor.MiddleCenter; ((Text)nameField.placeholder).alignment = TextAnchor.MiddleCenter;
            nameError = MenuUI.Text("", c, new Vector2(.05f, .31f), new Vector2(.95f, .38f), 16, UiTheme.Gold);
            var back = MenuUI.Row("Back", c, new Vector2(.2f, .16f), new Vector2(.46f, .28f), Back, 24, Vector2.zero);
            ConfirmNameButton = MenuUI.Row("Confirm", c, new Vector2(.54f, .16f), new Vector2(.8f, .28f), ConfirmName, 28, Vector2.zero);
            // field above, Back / Confirm side by side under it (wrap-around both ways)
            nameField.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnDown = ConfirmNameButton, selectOnUp = ConfirmNameButton };
            back.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = ConfirmNameButton, selectOnLeft = ConfirmNameButton, selectOnUp = nameField, selectOnDown = nameField };
            ConfirmNameButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = back, selectOnRight = back, selectOnUp = nameField, selectOnDown = nameField };
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
            leaveConfirm = Dialog("Leave card", "LAN Lobby", "Leave the lobby?", out var c);
            leaveBody = MenuUI.Text("", c, new Vector2(.05f, .5f), new Vector2(.95f, .62f), UiTheme.SizeBody, UiTheme.IvoryBody);
            StayButton = MenuUI.Row("Stay", c, new Vector2(.2f, .3f), new Vector2(.46f, .42f), Back, 28, Vector2.zero);
            LeaveButton = MenuUI.Row("Leave", c, new Vector2(.54f, .3f), new Vector2(.8f, .42f), Leave, 24, Vector2.zero);
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
            header.Eyebrow.text = UiTheme.Spaced(page == Page.Lobby ? "LAN Lobby" : "LAN Multiplayer");
            header.Title.text = page == Page.Lobby ? "Lobby" : "Host or Join";
            if (page == Page.Lobby)
            {
                // Lobby: Left/Right change class, so focus moves only up/down between Ready, Start and Back.
                if (IsHost) Vertical(ReadyButton, StartButton, BackButton); else Vertical(ReadyButton, BackButton);
                seenSelection = -1; Focus(ReadyButton);
            }
            else
            {
                foreach (var s in Slots) ShowModel(s, null);
                // one wrap-around column: name, Host, address, port, Connect, Back
                MenuUI.LinkColumn(new Selectable[] { EditNameButton, HostButton, address, port, JoinButton, BackButton });
                Focus(HostButton);
            }
        }

        // ------------------------------------------------------------------ per frame
        bool stickHeld, stickYHeld; int artMap = -2; Texture2D artTexture;
        void Update()
        {
            if (!canvas) return;
            HandleInput();
            Refresh();
            // Ultrawide (wider than ~1.95:1): the content keeps a centred 16:9 frame; backdrop and gradient stay full width.
            var area = root.rect; float aspect = area.height > 0 ? area.width / area.height : 1.78f, w = aspect > 1.95f ? 1.7778f / aspect : 1;
            frame.anchorMin = new Vector2((1 - w) * .5f, 0); frame.anchorMax = new Vector2((1 + w) * .5f, 1);
            PlaceChooser(bossChooser); PlaceChooser(arenaChooser);
            // a dialog fades the page behind it, so its rows never read as part of the dialog
            float page = NameCardOpen || LeaveConfirmOpen ? .18f : 1; if (frameGroup.alpha != page) frameGroup.alpha = page;
            // status: under the column on the connect page, centred over the footer in the lobby
            var sr = status.rectTransform; var want = Current == Page.Lobby ? new Vector2(.04f, .16f) : new Vector2(.6f, .16f);
            if (sr.anchorMin != want) { sr.anchorMin = want; status.alignment = Current == Page.Lobby ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft; }
        }
        static void PlaceChooser(Chooser c)
        {
            if (c == null || !c.Label) return;
            var prev = (RectTransform)c.Previous.transform; var next = (RectTransform)c.Next.transform;
            prev.anchorMin = prev.anchorMax = new Vector2(0, .5f); prev.anchoredPosition = new Vector2(20, 0);
            float w = Mathf.Min(c.Label.preferredWidth, c.Label.rectTransform.rect.width);
            next.anchorMin = next.anchorMax = new Vector2(0, .5f); next.anchoredPosition = new Vector2(44 + w + 22, 0);
        }
        static bool Pressed(Func<Keyboard, bool> k, Func<Gamepad, bool> g) => (Keyboard.current != null && k(Keyboard.current)) || (Gamepad.current != null && g(Gamepad.current));
        void HandleInput()
        {
            bool typing = EventSystem.current && EventSystem.current.currentSelectedGameObject && EventSystem.current.currentSelectedGameObject.GetComponent<InputField>();
            if (Pressed(k => k.escapeKey.wasPressedThisFrame, g => g.buttonEast.wasPressedThisFrame)) { Back(); return; }
            // A focused input field swallows uGUI navigation, so a gamepad could never leave it. Up/Down (dpad, stick or
            // arrows; a single-line field has no use for them) step out along the field's own navigation links.
            if (typing)
            {
                var field = EventSystem.current.currentSelectedGameObject.GetComponent<InputField>();
                float sy = Gamepad.current != null ? Gamepad.current.leftStick.y.ReadValue() : 0; int step = 0;
                if (Mathf.Abs(sy) > .6f) { if (!stickYHeld) step = sy > 0 ? 1 : -1; stickYHeld = true; } else if (Mathf.Abs(sy) < .3f) stickYHeld = false;
                if (Pressed(k => k.upArrowKey.wasPressedThisFrame, g => g.dpad.up.wasPressedThisFrame)) step = 1;
                if (Pressed(k => k.downArrowKey.wasPressedThisFrame, g => g.dpad.down.wasPressedThisFrame)) step = -1;
                var to = step > 0 ? field.navigation.selectOnUp : step < 0 ? field.navigation.selectOnDown : null;
                if (field.isFocused && to && to.IsInteractable()) { field.DeactivateInputField(); Focus(to); return; }   // only while editing: otherwise uGUI moves itself
            }
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
            playingAs.text = me == null ? "" : "Playing as " + (lobby != null && lobby.Local != null ? lobby.Local.Name : me);
            playingAs.enabled = Current == Page.Lobby;   // the connect page shows the name in its own row
            EditNameButton.interactable = !session; EditNameButton.gameObject.SetActive(Current == Page.Connect);
            editNameLabel.text = me ?? "Choose a name";
            hint.text = Hints(); if (hint.transform.GetSiblingIndex() != root.childCount - 1) hint.transform.SetAsLastSibling();
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
                s.Tag.color = m != null && m.Ready ? UiTheme.GoldBright : UiTheme.IvoryMicro;
                s.Prev.gameObject.SetActive(mine); s.Next.gameObject.SetActive(mine);
                s.Prev.interactable = s.Next.interactable = mine && !lobby.LocalReady && !loading;
                float edge = mine ? 2 : 1.5f;
                if (s.Edge && s.Edge.Thickness != edge) { s.Edge.color = mine ? UiTheme.GoldBright : UiTheme.RuleFaint; s.Edge.Thickness = edge; s.Edge.SetVerticesDirty(); }
                var accent = s.Card.Find("Your accent"); if (accent && accent.gameObject.activeSelf != mine) accent.gameObject.SetActive(mine);
            }
            // buttons and status
            ReadyButton.interactable = connected && !loading;
            ReadyButton.GetComponentInChildren<Text>().text = connected && lobby.LocalReady ? "Not ready" : "Ready";
            StartButton.gameObject.SetActive(hosting); StartButton.interactable = hosting && lobby.CanStart && !loading;
            status.text = error ?? (lobby != null && lobby.Error != null ? lobby.Error
                : loading ? "Loading arena…"
                : !connected ? "Connecting…"
                : hosting ? (lobby.StartBlocker ?? "Everyone is ready. Start the fight.")
                : lobby.LocalReady ? "Ready. Waiting for the host to start." : "Pick your class and press Ready.");
        }
        // Key hints for the device used last (InputHints): keys in Gold, actions in muted Ivory.
        string Hints()
        {
            bool pad = InputHints.Last == InputHints.Device.Gamepad; string ok = InputHints.Key(HintAction.Confirm), back = InputHints.Key(HintAction.Back);
            string K(string key, string action) => "<color=#E0B86EFF>" + key + "</color>  " + action;
            const string gap = "      ";
            if (NameCardOpen) return K(ok, "confirm") + gap + K(back, "back");
            if (LeaveConfirmOpen) return K(pad ? "D-pad" : "Left / Right", "choose") + gap + K(ok, "select") + gap + K(back, "stay");
            if (Current != Page.Lobby) return K(pad ? "D-pad" : "Arrows", "move") + gap + K(ok, "select") + gap + K(back, "back");
            string text = K(pad ? "D-pad Left / Right" : "Left / Right", "class") + gap + K(ok, "ready");
            if (IsHost) text += gap + K(pad ? "LB / RB" : "Q / E", "boss") + gap + K(pad ? "LT / RT" : "Z / C", "arena");
            return text + gap + K(back, "leave");
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
