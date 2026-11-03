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
            Check("all three classes available", RunDirector.Classes.SequenceEqual(new[]{"Vanguard","Chimesinger","Arcanist"}));
            menu.RushButton.onClick.Invoke(); yield return null;
            Check("Multiplayer opens", menu.LanPanel != null);
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
                foreach (var map in RunMaps.All) { var b=Find(buttons,map.Name); Check("arena available: "+map.Name, b && b.interactable); }
                for (int i=0;i<RunDirector.Bosses.Length;i++)
                {
                    string name=TestRushSetup.CardName(i); var b=Find(buttons,name);
                    Check("encounter availability: "+name,b && b.interactable==LanLobby.ValidEncounter(name));
                }
                var start=Find(buttons,"Start fight"); Check("start requires a connected client",start && !start.interactable);
                menu.LanPanel.Close(); yield return null;
            }
            var args=Environment.GetCommandLineArgs(); int at=Array.IndexOf(args,"-lastbellOut");
            if (at>=0 && at+1<args.Length) { Directory.CreateDirectory(args[at+1]); results.Add("RESULT "+(results.Count-failures)+" passed, "+failures+" failed"); File.WriteAllLines(Path.Combine(args[at+1],"lan-menu-test.txt"),results); }
            foreach (var line in results) Debug.Log("[LAN menu test] "+line);
            Application.Quit(failures==0?0:1);
        }
    }
}
#endif
