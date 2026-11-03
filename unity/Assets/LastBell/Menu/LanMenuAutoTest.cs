#if LASTBELL_LAN_TEST
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
using UnityEngine.UI;

namespace LastBell.Menu
{
    // Opt-in player diagnostic. It verifies UI wiring and availability, not multiplayer gameplay or visuals.
    public sealed class LanMenuAutoTest : MonoBehaviour
    {
        readonly List<string> results = new List<string>(); int failures;
        public static void Install(ConfrontationMenu owner)
        {
            if (Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellLanMenuTest", StringComparison.OrdinalIgnoreCase))) owner.gameObject.AddComponent<LanMenuAutoTest>();
        }
        void Check(string name, bool passed) { results.Add((passed ? "PASS " : "FAIL ") + name); if (!passed) failures++; }
        static Button Find(IEnumerable<Button> buttons, string name) => buttons.FirstOrDefault(b => b && b.GetComponentInChildren<Text>() && b.GetComponentInChildren<Text>().text == name);
        IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(2);
            var menu=GetComponent<ConfrontationMenu>();
            var main=menu.Canvas.GetComponentsInChildren<Button>(true);
            Check("Confrontation starts with Multiplayer", Find(main,"Multiplayer") != null);
            Check("Start Run and Boss Rush are absent", Find(main,"Start Run") == null && Find(main,"Boss Rush") == null);
            Check("all three classes available", RunDirector.Classes.SequenceEqual(new[]{"Tollbreaker","Chimesinger","Arcanist"}));
            Check("LAN class list matches the game's, Tollbreaker first", LastBell.Run.Networking.LanLobby.ClassList.SequenceEqual(RunDirector.Classes) && LastBell.Run.Networking.LanLobby.DefaultClass=="Tollbreaker");
            var args=Environment.GetCommandLineArgs(); int at=Array.IndexOf(args,"-lastbellOut");
            string outDir=at>=0 && at+1<args.Length ? args[at+1] : Path.Combine(Application.temporaryCachePath,"lan-menu-test");
            Directory.CreateDirectory(outDir);
            // Never touch the player's real profile: this run saves into the test's own folder.
            LanProfile.PathOverride=Path.Combine(outDir,"lan-profile.json"); if (File.Exists(LanProfile.PathOverride)) File.Delete(LanProfile.PathOverride);
            bool overridden=LanProfile.Overridden;
            if (!overridden)
            {
                menu.RushButton.onClick.Invoke(); yield return null;
                Check("first run: Multiplayer asks for a name", menu.LanPanel && menu.LanPanel.NameCardOpen);
                var field=menu.LanPanel ? menu.LanPanel.transform.parent.GetComponentsInChildren<InputField>(true).Last() : null;
                var confirm=menu.LanPanel ? Find(menu.LanPanel.transform.parent.GetComponentsInChildren<Button>(true),"Confirm") : null;
                if (field && confirm)
                {
                    field.text=""; yield return null; Check("Confirm is disabled with no name", !confirm.interactable);
                    field.text="A"; yield return null; Check("a one-letter name is refused", !confirm.interactable);
                    field.text="<b>Bell</b>"; yield return null; Check("markup in a name is refused", !confirm.interactable);
                    field.text="  Test   Ringer "; yield return null; Check("a valid name enables Confirm", confirm.interactable);
                    confirm.onClick.Invoke(); yield return null;
                    Check("Confirm saves the name (trimmed) and closes the card", !menu.LanPanel.NameCardOpen && LanProfile.Current=="Test Ringer" && File.ReadAllText(LanProfile.PathOverride).Contains("Test Ringer"));
                }
                else Check("name card has a field and Confirm", false);
                menu.LanPanel.Close(); yield return null;
                menu.RushButton.onClick.Invoke(); yield return null;
                Check("a saved name skips the card next time", menu.LanPanel && !menu.LanPanel.NameCardOpen);
                menu.LanPanel.Close(); yield return null;
                File.WriteAllText(LanProfile.PathOverride,"{ not json");
                Check("a corrupt profile counts as no name", LanProfile.Current==null);
                menu.RushButton.onClick.Invoke(); yield return null;
                Check("a corrupt profile asks for the name again", menu.LanPanel && menu.LanPanel.NameCardOpen);
                menu.LanPanel.Back(); yield return null;
                Check("Back on the first-run card leaves Multiplayer", menu.LanPanel==null);
                LanProfile.Save("Test Ringer");
            }
            else
            {
                Check("-lastbellProfile names this run without a card", LanProfile.Current!=null && !File.Exists(LanProfile.PathOverride));
            }
            menu.RushButton.onClick.Invoke(); yield return null;
            Check("Multiplayer opens", menu.LanPanel != null && !menu.LanPanel.NameCardOpen);
            if (menu.LanPanel)
            {
                var buttons=menu.LanPanel.transform.parent.GetComponentsInChildren<Button>(true);
                var fields=menu.LanPanel.transform.parent.GetComponentsInChildren<InputField>(true);
                var probe=new TcpListener(IPAddress.Loopback,0); probe.Start(); int port=((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
                if (fields.Length >= 2) fields[1].text=port.ToString();
                var host=Find(buttons,"Host LAN"); if (host) host.onClick.Invoke();
                yield return new WaitForSecondsRealtime(.25f);
                var session=LanSessionCoordinator.Instance;
                Check("host opens a LAN lobby", session && session.Lobby != null && session.Lobby.IsHost && session.Lobby.Connected);
                Check("the lobby uses the profile name", session && session.Lobby != null && session.Lobby.Local != null && session.Lobby.Local.Name==LanProfile.Current);
                if (overridden) Check("-lastbellProfile never writes the profile file", !File.Exists(LanProfile.PathOverride));
                var ready=Find(buttons,"Ready"); Check("Ready button available in the lobby", ready && ready.interactable);
                if (ready) { ready.onClick.Invoke(); yield return null; }
                Check("Ready marks the host ready and locks the class", session && session.Lobby != null && session.Lobby.LocalReady && !session.Lobby.SetClass("Arcanist"));
                Check("Ready button now reads Not ready", Find(buttons,"Not ready") != null);
                foreach (var map in RunMaps.All) { var b=Find(buttons,map.Name); Check("arena available: "+map.Name, b && b.interactable); }
                for (int i=0;i<RunDirector.Bosses.Length;i++)
                {
                    string name=TestRushSetup.CardName(i); var b=Find(buttons,name);
                    Check("encounter availability: "+name,b && b.interactable==LanLobby.ValidEncounter(name));
                }
                var start=Find(buttons,"Start fight"); Check("start requires a second player even when the host is ready",start && !start.interactable && session.Lobby.StartBlocker=="Need at least 2 players.");
                menu.LanPanel.Close(); yield return null;
            }
            LanProfile.PathOverride=null;
            if (at>=0 && at+1<args.Length) { results.Add("RESULT "+(results.Count-failures)+" passed, "+failures+" failed"); File.WriteAllLines(Path.Combine(args[at+1],"lan-menu-test.txt"),results); }
            foreach (var line in results) Debug.Log("[LAN menu test] "+line);
            Application.Quit(failures==0?0:1);
        }
    }
}
#endif
