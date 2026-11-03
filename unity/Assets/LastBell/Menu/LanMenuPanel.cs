#if LASTBELL_LAN_TEST
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using LastBell.Run;
using LastBell.Run.Networking;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LastBell.Menu
{
    // LAN test-edition session setup. All start and selection decisions are checked by LanLobby as well as the UI.
    public sealed class LanMenuPanel : MonoBehaviour
    {
        internal static string BuildId()
        {
            // The installed managed assembly identifies the exact executable code; asset identity still belongs in
            // the packaged build manifest before distribution.
            using (var sha=SHA256.Create()) using (var file=File.OpenRead(typeof(LanLobby).Assembly.Location))
                return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
        }
        ConfrontationMenu owner; GameObject panel; InputField address, port; Text status, roster, selection, classLabel;
        Button hostButton, joinButton, startButton, classPrev, classNext; Button[] maps, encounters;
        LanSessionCoordinator session; string hero, error;
        // Name card (LanProfile): shown before Host/Connect until the player has a saved name; Edit name reopens it.
        GameObject nameCard; InputField nameField; Text nameError, playingAs; Button nameConfirm, editName, readyButton;
        int seenSelection = -1; float selectionNoticeUntil;
        public bool NameCardOpen => nameCard && nameCard.activeSelf;
        public void Build(ConfrontationMenu parent, Transform container, string selectedHero)
        {
            owner = parent; hero = selectedHero;
            var baseImage = MenuUI.Panel("LAN Multiplayer", container, Vector2.zero, Vector2.one, new Color(.012f,.014f,.026f,.98f)); baseImage.raycastTarget = true;
            panel = baseImage.gameObject; transform.SetParent(panel.transform, false);
            var t = panel.transform;
            MenuUI.Text("LAN MULTIPLAYER", t, new Vector2(.04f,.90f), new Vector2(.65f,.98f), 42, MenuUI.Ivory, TextAnchor.MiddleLeft);
            playingAs = MenuUI.Text("", t, new Vector2(.50f,.90f), new Vector2(.78f,.98f), 20, MenuUI.Gold, TextAnchor.MiddleRight);
            editName = MenuUI.Button("Edit name", t, new Vector2(.79f,.91f), new Vector2(.96f,.97f), () => OpenNameCard());
            MenuUI.Text("Class", t, new Vector2(.04f,.81f), new Vector2(.16f,.86f), 20, MenuUI.Gold, TextAnchor.MiddleLeft);
            classPrev = MenuUI.Button("<", t, new Vector2(.16f,.81f), new Vector2(.205f,.87f), () => ChangeClass(-1));
            classLabel = MenuUI.Text("", t, new Vector2(.205f,.81f), new Vector2(.355f,.87f), 22);
            classNext = MenuUI.Button(">", t, new Vector2(.355f,.81f), new Vector2(.4f,.87f), () => ChangeClass(1));
            MenuUI.Text("Host address", t, new Vector2(.04f,.74f), new Vector2(.16f,.80f), 18, MenuUI.Gold, TextAnchor.MiddleLeft);
            address = Field(t, new Vector2(.16f,.74f), new Vector2(.36f,.80f), "127.0.0.1");
            MenuUI.Text("Port", t, new Vector2(.37f,.74f), new Vector2(.43f,.80f), 18, MenuUI.Gold);
            port = Field(t, new Vector2(.43f,.74f), new Vector2(.52f,.80f), "7777"); port.contentType = InputField.ContentType.IntegerNumber;
            hostButton = MenuUI.Button("Host LAN", t, new Vector2(.54f,.74f), new Vector2(.72f,.80f), Host);
            joinButton = MenuUI.Button("Connect", t, new Vector2(.73f,.74f), new Vector2(.91f,.80f), Join);
            status = MenuUI.Text("Host or connect to a trusted local network.", t, new Vector2(.04f,.68f), new Vector2(.96f,.73f), 18, MenuUI.Ivory, TextAnchor.MiddleLeft);
            selection = MenuUI.Text("", t, new Vector2(.04f,.62f), new Vector2(.96f,.68f), 20, MenuUI.Gold, TextAnchor.MiddleLeft);
            MenuUI.Text("ARENA", t, new Vector2(.04f,.57f), new Vector2(.25f,.62f), 18, MenuUI.Gold, TextAnchor.MiddleLeft);
            maps = new Button[RunMaps.All.Length];
            for (int i = 0; i < maps.Length; i++) { int n=i; float x=.04f+i*.15f; maps[i]=MenuUI.Button(RunMaps.All[i].Name,t,new Vector2(x,.50f),new Vector2(x+.15f,.57f),()=>ChooseMap(n)); }
            MenuUI.Text("ENCOUNTER", t, new Vector2(.04f,.45f), new Vector2(.25f,.50f), 18, MenuUI.Gold, TextAnchor.MiddleLeft);
            encounters = new Button[RunDirector.Bosses.Length];
            for (int i = 0; i < encounters.Length; i++) { int n=i; int col=i%5,row=i/5; float x=.04f+col*.18f,y=.37f-row*.07f; encounters[i]=MenuUI.Button(TestRushSetup.CardName(i),t,new Vector2(x,y),new Vector2(x+.18f,y+.07f),()=>ChooseEncounter(n)); }
            roster = MenuUI.Text("", t, new Vector2(.04f,.13f), new Vector2(.69f,.29f), 18, MenuUI.Ivory, TextAnchor.UpperLeft);
            MenuUI.Button("Back", t, new Vector2(.04f,.035f), new Vector2(.22f,.105f), Close);
            readyButton = MenuUI.Button("Ready", t, new Vector2(.45f,.035f), new Vector2(.68f,.105f), ToggleReady);
            startButton = MenuUI.Button("Start fight", t, new Vector2(.72f,.035f), new Vector2(.95f,.105f), StartFight);
            BuildNameCard(t);
            if (LanProfile.Current == null) OpenNameCard(); else EventSystem.current.SetSelectedGameObject(hostButton.gameObject);
            Refresh();
        }
        void BuildNameCard(Transform t)
        {
            var shade = MenuUI.Panel("Name card shade", t, Vector2.zero, Vector2.one, new Color(0,0,0,.72f)); shade.raycastTarget = true;
            nameCard = shade.gameObject; var c = nameCard.transform;
            MenuUI.Panel("Name card", c, new Vector2(.30f,.33f), new Vector2(.70f,.70f), new Color(.03f,.035f,.06f,1));
            MenuUI.Text("CHOOSE A NAME", c, new Vector2(.33f,.62f), new Vector2(.67f,.68f), 30, MenuUI.Ivory);
            MenuUI.Text("Other players see this name in the lobby and over your character. It's saved on this computer.", c, new Vector2(.33f,.555f), new Vector2(.67f,.615f), 16, MenuUI.Ivory);
            nameField = Field(c, new Vector2(.36f,.48f), new Vector2(.64f,.54f), LanProfile.Saved ?? "");
            nameField.characterLimit = LanPlayerName.MaxLength + 4;   // room to type spaces that get trimmed
            nameError = MenuUI.Text("", c, new Vector2(.33f,.43f), new Vector2(.67f,.475f), 16, MenuUI.Gold);
            MenuUI.Button("Back", c, new Vector2(.36f,.355f), new Vector2(.49f,.415f), Back);
            nameConfirm = MenuUI.Button("Confirm", c, new Vector2(.51f,.355f), new Vector2(.64f,.415f), ConfirmName);
            nameCard.SetActive(false);
        }
        void OpenNameCard()
        {
            if (session) return;   // the lobby already has your name; leave to change it
            nameField.text = LanProfile.Current ?? LanProfile.Saved ?? ""; nameCard.SetActive(true); nameCard.transform.SetAsLastSibling();
            EventSystem.current.SetSelectedGameObject(nameField.gameObject); nameField.ActivateInputField();
        }
        void ConfirmName()
        {
            if (LanProfile.Overridden) { error = "Name set by -lastbellProfile for this run; not saved."; nameCard.SetActive(false); return; }
            var problem = LanProfile.Save(nameField.text);
            if (problem != null) { nameError.text = problem; return; }
            nameCard.SetActive(false); error = null; EventSystem.current.SetSelectedGameObject(hostButton.gameObject);
        }
        // Esc / B: closes the name card when a name exists, otherwise leaves Multiplayer.
        public void Back()
        {
            if (NameCardOpen && LanProfile.Current != null) { nameCard.SetActive(false); EventSystem.current.SetSelectedGameObject(editName.gameObject); return; }
            Close();
        }
        void ToggleReady()
        {
            if (!session || session.Lobby == null) return;
            if (!session.Lobby.SetReady(!session.Lobby.LocalReady)) error = "Ready could not be changed.";
            else error = null;
        }
        static InputField Field(Transform parent, Vector2 min, Vector2 max, string value)
        {
            var background=MenuUI.Panel("Input", parent, min, max, new Color(.07f,.07f,.10f,1)); background.raycastTarget=true;
            var input=background.gameObject.AddComponent<InputField>(); input.targetGraphic=background;
            var text=MenuUI.Text("",background.transform,new Vector2(.03f,0),new Vector2(.97f,1),20,MenuUI.Ivory,TextAnchor.MiddleLeft);
            text.supportRichText=false; input.textComponent=text; input.text=value; return input;
        }
        bool Port(out int value) { if (int.TryParse(port.text, out value) && value > 0 && value < 65536) return true; error="Enter a valid port (1–65535)."; return false; }
        void Host()
        {
            if (!Port(out int p)) return;
            if (LanProfile.Current == null) { OpenNameCard(); return; }
            try { session=LanSessionCoordinator.Open(LanLobby.Host(p,BuildId(),hero,LanProfile.Current)); error=null; }
            catch (Exception e) { error="Could not host: "+e.Message; }
        }
        void Join()
        {
            if (!Port(out int p)) return;
            if (LanProfile.Current == null) { OpenNameCard(); return; }
            try { session=LanSessionCoordinator.Open(LanLobby.Connect(address.text.Trim(),p,BuildId(),hero,LanProfile.Current)); error=null; }
            catch (Exception e) { error="Could not connect: "+e.Message; }
        }
        void ChangeClass(int direction)
        {
            int i=Array.IndexOf(RunDirector.Classes,hero); string next=RunDirector.Classes[(i+direction+RunDirector.Classes.Length)%RunDirector.Classes.Length];
            if (session && session.Lobby.LocalReady) { error="Press Not ready to change class."; return; }
            if (session && !session.Lobby.SetClass(next)) { error="Class could not be changed."; return; }
            hero=next; error=null;
        }
        void ChooseMap(int i)
        {
            if (!session || !session.Lobby.IsHost) return;
            if (!session.Lobby.SetSelection(RunMaps.All[i].Id,session.Lobby.Encounter)) error="Arena is unavailable.";
        }
        void ChooseEncounter(int i)
        {
            if (!session || !session.Lobby.IsHost) return;
            if (!session.Lobby.SetSelection(session.Lobby.Arena,TestRushSetup.CardName(i))) error="Encounter is unavailable.";
        }
        void StartFight() { if (!session || !session.HostStart()) error=session && session.Lobby != null ? session.Lobby.StartBlocker ?? "Could not start." : "Host a lobby first."; }
        public void Close() { if (session) session.Leave(); owner.CloseLan(); Destroy(panel); }
        void Update()
        {
            if (NameCardOpen && (UnityEngine.InputSystem.Keyboard.current?.enterKey.wasPressedThisFrame == true || UnityEngine.InputSystem.Keyboard.current?.numpadEnterKey.wasPressedThisFrame == true)) ConfirmName();
            Refresh();
        }
        void Refresh()
        {
            if (!panel) return;
            var lobby=session ? session.Lobby : null; bool connected=lobby!=null && lobby.Connected; bool hosting=connected && lobby.IsHost;
            hostButton.interactable=joinButton.interactable=lobby==null; address.interactable=port.interactable=lobby==null;
            classPrev.interactable=classNext.interactable=lobby==null||(connected&&!lobby.LocalReady);
            string me=LanProfile.Current; playingAs.text=me==null?"No name yet":"Playing as "+(connected&&lobby.Local!=null?lobby.Local.Name:me);
            editName.interactable=lobby==null;
            if (NameCardOpen) { var problem=LanPlayerName.Problem(nameField.text); nameConfirm.interactable=problem==null; if (problem==null) nameError.text=""; else if (nameField.text.Length>0) nameError.text=problem; }
            readyButton.interactable=connected && !session.Loading; readyButton.GetComponentInChildren<Text>().text=connected && lobby.LocalReady ? "Not ready" : "Ready";
            if (connected && lobby.SelectionRevision!=seenSelection) { if (!hosting && seenSelection>=0) selectionNoticeUntil=Time.unscaledTime+4; seenSelection=lobby.SelectionRevision; }
            classLabel.text=hero.ToUpperInvariant();
            for(int i=0;i<maps.Length;i++) maps[i].interactable=hosting && RunMaps.InBuild(i);
            for(int i=0;i<encounters.Length;i++) encounters[i].interactable=hosting && LanLobby.ValidEncounter(TestRushSetup.CardName(i));
            startButton.interactable=hosting && lobby.CanStart && !session.Loading;
            selection.text=connected ? "Arena: "+RunMaps.All[RunMaps.IndexOf(lobby.Arena)].Name+"   ·   Encounter: "+lobby.Encounter : "";
            roster.text=connected ? "PLAYERS ("+lobby.Members.Count+" / "+LanLobby.Capacity+")\n"+string.Join("\n",lobby.Members.Select(m=>(m.Id==0?"[Host] ":"")+m.Name+" · "+m.ClassName+(m.Ready?"  · READY":"  · not ready"))) : "";
            status.text=error ?? (lobby==null ? (LanSessionCoordinator.LastError ?? "Host or connect to a trusted local network.") : lobby.Error ?? (session.Loading ? "Loading arena…" : !connected ? "Connecting…" : Time.unscaledTime<selectionNoticeUntil ? "The host changed the boss or arena. Press Ready again." : hosting ? (lobby.StartBlocker ?? "Everyone is ready. Start when you like.") : "Connected. The host selects the arena and encounter; press Ready when set."));
        }
    }
}
#endif
