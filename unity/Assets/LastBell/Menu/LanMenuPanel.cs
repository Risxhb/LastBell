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
        public void Build(ConfrontationMenu parent, Transform container, string selectedHero)
        {
            owner = parent; hero = selectedHero;
            var baseImage = MenuUI.Panel("LAN Multiplayer", container, Vector2.zero, Vector2.one, new Color(.012f,.014f,.026f,.98f)); baseImage.raycastTarget = true;
            panel = baseImage.gameObject; transform.SetParent(panel.transform, false);
            var t = panel.transform;
            MenuUI.Text("LAN MULTIPLAYER", t, new Vector2(.04f,.90f), new Vector2(.65f,.98f), 42, MenuUI.Ivory, TextAnchor.MiddleLeft);
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
            startButton = MenuUI.Button("Start fight", t, new Vector2(.72f,.035f), new Vector2(.95f,.105f), StartFight);
            EventSystem.current.SetSelectedGameObject(hostButton.gameObject); Refresh();
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
            try { session=LanSessionCoordinator.Open(LanLobby.Host(p,BuildId(),hero)); error=null; }
            catch (Exception e) { error="Could not host: "+e.Message; }
        }
        void Join()
        {
            if (!Port(out int p)) return;
            try { session=LanSessionCoordinator.Open(LanLobby.Connect(address.text.Trim(),p,BuildId(),hero)); error=null; }
            catch (Exception e) { error="Could not connect: "+e.Message; }
        }
        void ChangeClass(int direction)
        {
            int i=Array.IndexOf(RunDirector.Classes,hero); string next=RunDirector.Classes[(i+direction+RunDirector.Classes.Length)%RunDirector.Classes.Length];
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
        void StartFight() { if (!session || !session.HostStart()) error="Wait for all players to connect before starting."; }
        public void Close() { if (session) session.Leave(); owner.CloseLan(); Destroy(panel); }
        void Update() => Refresh();
        void Refresh()
        {
            if (!panel) return;
            var lobby=session ? session.Lobby : null; bool connected=lobby!=null && lobby.Connected; bool hosting=connected && lobby.IsHost;
            hostButton.interactable=joinButton.interactable=lobby==null; address.interactable=port.interactable=lobby==null;
            classPrev.interactable=classNext.interactable=lobby==null||connected;
            classLabel.text=hero.ToUpperInvariant();
            for(int i=0;i<maps.Length;i++) maps[i].interactable=hosting && RunMaps.InBuild(i);
            for(int i=0;i<encounters.Length;i++) encounters[i].interactable=hosting && LanLobby.ValidEncounter(TestRushSetup.CardName(i));
            startButton.interactable=hosting && lobby.Members.Count>=2 && !session.Loading;
            selection.text=connected ? "Arena: "+RunMaps.All[RunMaps.IndexOf(lobby.Arena)].Name+"   ·   Encounter: "+lobby.Encounter : "";
            roster.text=connected ? "PLAYERS ("+lobby.Members.Count+" / "+LanLobby.Capacity+")\n"+string.Join("   ",lobby.Members.Select(m=>(m.Id==0?"Host":"Player "+m.Id)+": "+m.ClassName)) : "";
            status.text=error ?? (lobby==null ? (LanSessionCoordinator.LastError ?? "Host or connect to a trusted local network.") : lobby.Error ?? (session.Loading ? "Loading arena…" : connected ? "Connected. The host selects the arena and encounter." : "Connecting…"));
        }
    }
}
#endif
