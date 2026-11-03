#if LASTBELL_LAN_TEST
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LastBell.Run;
using LastBell.Run.Networking;
using UnityEngine;

namespace LastBell.Menu
{
    // Opt-in two-process diagnostic. The existing DebugDefeat hook verifies result routing only, not natural victory.
    public sealed class LanSessionAutoTest : MonoBehaviour
    {
        readonly List<string> lines = new List<string>(); int failures; bool host;
        static bool installed;
        void Check(string label, bool ok) { lines.Add((ok ? "PASS " : "FAIL ") + label); if (!ok) failures++; }
        public static void Install()
        {
            var args=Environment.GetCommandLineArgs();
            bool h=args.Contains("-lastbellLanSmokeHost"), c=args.Contains("-lastbellLanSmokeClient");
            if (installed || (!h && !c)) return;
            installed=true;
            var go=new GameObject("LAN two-process diagnostic"); DontDestroyOnLoad(go); go.AddComponent<LanSessionAutoTest>().host=h;
        }
        static int Port()
        {
            var args=Environment.GetCommandLineArgs(); int n=Array.IndexOf(args,"-lastbellLanPort");
            return n>=0 && n+1<args.Length && int.TryParse(args[n+1],out int value) ? value : 7777;
        }
        static int PeerCount()
        {
            var args=Environment.GetCommandLineArgs(); int n=Array.IndexOf(args,"-lastbellLanPeerCount");
            return n>=0 && n+1<args.Length && int.TryParse(args[n+1],out int value) ? Mathf.Clamp(value,1,LanLobby.MaxClients) : 1;
        }
        static string Argument(string name, string fallback)
        {
            var args=Environment.GetCommandLineArgs(); int n=Array.IndexOf(args,name); return n>=0 && n+1<args.Length ? args[n+1] : fallback;
        }
        // The automated clients' class (default Arcanist); the host passes the same flag so it knows what to expect.
        static string ClientClass { get { var c=Argument("-lastbellLanClientClass","Arcanist"); return LanLobby.ValidClass(c) ? c : "Arcanist"; } }
        IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(1);
            LanSessionCoordinator session=null;
            try { session=LanSessionCoordinator.Open(host ? LanLobby.Host(Port(),LanMenuPanel.BuildId(),"Tollbreaker") : LanLobby.Connect("127.0.0.1",Port(),LanMenuPanel.BuildId(),ClientClass)); }
            catch(Exception ex) { Check("open LAN: "+ex.Message,false); Finish(); yield break; }
            Debug.Log(host ? "[LAN smoke] HOST READY" : "[LAN smoke] CLIENT CONNECTING");
            int peerCount=PeerCount(); float until=Time.realtimeSinceStartup+90;
            while (session && session.Lobby != null && Time.realtimeSinceStartup<until && (host ? session.Lobby.Members.Count<peerCount+1 : !session.Lobby.Connected)) yield return null;
            bool joined=session && session.Lobby != null && (host ? session.Lobby.Members.Count==peerCount+1 : session.Lobby.Connected);
            Check("host and client join lobby",joined);
            if (!joined) { Finish(); yield break; }
            if (host)
            {
                Check("host selects requested arena and encounter",session.Lobby.SetSelection(Argument("-lastbellLanArena","test"),Argument("-lastbellLanBoss","Bellkeeper")));
                Check("host begins session",session.HostStart());
            }
            until=Time.realtimeSinceStartup+90;
            while (session && !session.InFight && Time.realtimeSinceStartup<until) yield return null;
            Check("both players load arena",session && session.InFight);
            if (!session || !session.InFight) { Finish(); yield break; }
            if (host) yield return HostFight(session); else yield return ClientFight(session);
            Finish();
        }
        IEnumerator HostFight(LanSessionCoordinator session)
        {
            var d=RunDirector.Instance;
            Check("host has all remote player actors",d && d.World != null && d.Allies.Count==PeerCount() && d.Allies.All(a=>a.ClassName==ClientClass));
            if (!d || d.World==null || d.Allies.Count!=PeerCount()) yield break;
            Vector3 initial=d.Allies[0].transform.position;
            if (d.State==RunState.Ready) d.RequestReady();
            float fightStart=Time.realtimeSinceStartup, until=fightStart+50; int hostFrames=0; bool moved=false, damaged=false, hazard=false, classAction=false, sawDowned=false, sawCleared=false;
            while (session && d && Time.realtimeSinceStartup<until)
            {
                moved |= Vector3.Distance(initial,d.Allies[0].transform.position)>.4f;
                damaged |= d.Boss && d.Boss.Hp<d.Boss.MaxHp;
                hazard |= d.World.Hazards.Count>0;
                // a class action (slots 2-5, not the basic attack) put on cooldown by a remote player's input
                classAction |= d.Allies.Any(a=>a && Enumerable.Range(1,a.Kit.SlotNames.Length-1).Any(s=>a.Kit.Cooldown01(s)>0.001f));
                sawDowned |= d.State==RunState.Downed; sawCleared |= d.State==RunState.Cleared;
                hostFrames++;
                if (moved && damaged && hazard && classAction && d.State==RunState.Combat && Time.realtimeSinceStartup-fightStart>8) break;
                if (d.State==RunState.Downed || d.State==RunState.Results || d.State==RunState.Cleared) break;   // the fight ended by itself (a wipe)
                yield return null;
            }
            Debug.Log("[LAN smoke] host frame rate in combat: "+Mathf.RoundToInt(hostFrames/Mathf.Max(0.01f,Time.realtimeSinceStartup-fightStart))+" fps");
            Debug.Log("[LAN smoke] trace fight loop ended: session " + (bool)session + " director " + (bool)d + " state " + (d ? d.State.ToString() : "-") + " timedOut " + (Time.realtimeSinceStartup >= until));
            Check("remote input moves host's actual player actor",moved);
            Check("remote attack damages the host's actual boss",damaged);
            Check("host creates a live combat hazard",hazard);
            Check("remote player uses class abilities, not just the basic attack",classAction);
            if (hazard) yield return new WaitForSecondsRealtime(1);
            bool diagnosticVictory = session && d && d.State==RunState.Combat && d.Boss;
            Debug.Log("[LAN smoke] trace after wait: state " + (d ? d.State.ToString() : "-") + " forcing kill " + diagnosticVictory);
            if (diagnosticVictory) d.Boss.DebugDefeat();
            // Some bosses (the Chimebound Oracle) shrug off the forced kill, so allow time for a real wipe or victory.
            until=Time.realtimeSinceStartup+45;
            while (session && d && d.State!=RunState.Results && Time.realtimeSinceStartup<until) { sawDowned |= d.State==RunState.Downed; sawCleared |= d.State==RunState.Cleared; yield return null; }
            Debug.Log("[LAN smoke] trace results wait ended: session " + (bool)session + " director " + (bool)d + " state " + (d ? d.State.ToString() : "-") + " timedOut " + (Time.realtimeSinceStartup >= until) + " downed " + sawDowned + " cleared " + sawCleared);
            // The forced kill can lose a race with a real party wipe; either way the outcome must match what happened.
            Check("host reaches results with correct outcome",d && d.State==RunState.Results && (d.Victory ? sawCleared && !sawDowned : sawDowned));
            if (d && d.State==RunState.Results) Debug.Log("[LAN smoke] HOST OUTCOME " + (d.Victory ? "victory (diagnostic)" : "party defeat"));
            if (!session || !d || d.State!=RunState.Results) yield break;
            // Retry keeps the frozen LAN roster and must keep streaming to clients although the world tick restarts.
            yield return new WaitForSecondsRealtime(2);
            bool retried=d.RequestRetry();
            Check("host retries with the same remote players",retried && d.Allies.Count==PeerCount() && d.Allies.All(a=>a.ClassName==ClientClass));
            if (!retried) yield break;
            until=Time.realtimeSinceStartup+30;
            while (session && d && d.State!=RunState.Combat && Time.realtimeSinceStartup<until) { if (d.State==RunState.Ready) d.RequestReady(); yield return null; }
            Check("retry reaches a new fight",session && d && d.State==RunState.Combat);
            yield return new WaitForSecondsRealtime(3);
            Check("session survives the retry",session && session.Lobby != null && session.Lobby.Error == null);
        }
        IEnumerator ClientFight(LanSessionCoordinator session)
        {
            float until=Time.realtimeSinceStartup+100; bool observedCombat=false, observedDamage=false, observedResult=false, observedHazard=false, observedTelegraph=false, observedProjectile=false, observedHud=false, observedOwnCooldown=false;
            float combatSince=-1, combatTime=0; int combatFrames=0; bool shotCombat=false;
            float maxLead=0; LanWorldFrame lastFrame=null; int snapshots=0, movedFrames=0; Vector3 lastSnapshotPos=Vector3.zero; Vector3 lastDrawn=Vector3.zero; bool haveDrawn=false;
            while (session && session.ClientView && Time.realtimeSinceStartup<until)
            {
                var state=session.ClientView.Current;
                if (state!=null)
                {
                    var d=RunDirector.Instance; var me=state.Players.Find(p=>p.Id==session.Lobby.LocalId);
                    maxLead=Mathf.Max(maxLead,session.ClientView.PredictionLead);
                    // the normal RunHud draws the replica: the director holds this client's player, fed by the host's numbers
                    observedHud |= RunHud.LanClientView && d && d.Player && d.World!=null && me!=null && d.Player.Hp==me.Hp && d.Player.Kit.NetCooldowns!=null && (state.Boss==null || (d.Boss && d.Boss.Hp==state.Boss.Hp));
                    observedOwnCooldown |= me!=null && me.Cooldowns.Skip(1).Any(c=>c>0.001f);
                    if (!ReferenceEquals(state,lastFrame) && state.Boss!=null && state.RunState==(int)RunState.Combat) { lastFrame=state; var at=new Vector3(state.Boss.X,state.Boss.Y,state.Boss.Z); if ((at-lastSnapshotPos).sqrMagnitude>1e-6f) snapshots++; lastSnapshotPos=at; }
                    if (d && d.Boss && state.RunState==(int)RunState.Combat)
                    {
                        var drawn=d.Boss.transform.position;
                        if (haveDrawn && (drawn-lastDrawn).sqrMagnitude>1e-8f) movedFrames++;
                        lastDrawn=drawn; haveDrawn=true;
                    }
                    observedCombat |= state.RunState==(int)RunState.Combat;
                    observedDamage |= state.Boss!=null && state.Boss.Hp<state.Boss.MaxHp;
                    observedHazard |= state.Hazards.Count>0;
                    observedTelegraph |= state.Telegraphs.Count>0;
                    observedProjectile |= state.Projectiles.Count>0;
                    if (state.RunState==(int)RunState.Combat) { if (combatSince<0) combatSince=Time.realtimeSinceStartup; else { combatFrames++; combatTime+=Time.unscaledDeltaTime; } }
                    if (!shotCombat && combatSince>=0 && Time.realtimeSinceStartup-combatSince>4) { shotCombat=true; yield return Shot("client-combat.png"); }
                    observedResult |= state.RunState==(int)RunState.Results;
                    if (observedResult) { yield return new WaitForSecondsRealtime(1); yield return Shot("client-results.png"); break; }
                }
                yield return null;
            }
            Check("client receives host combat state",observedCombat);
            Check("client receives boss health change",observedDamage);
            Check("client receives common hazard state",observedHazard);
            Check("client receives actual telegraph state",observedTelegraph);
            Check("client receives player projectile state",observedProjectile);
            Check("client receives fight results",observedResult);
            Check("client HUD shows the host's fight (health, cooldowns, boss)",observedHud);
            Check("client sees its own class abilities go on cooldown",observedOwnCooldown);
            var fx=session.ClientView ? session.ClientView.Effects : null;
            if (fx!=null) Debug.Log("[LAN smoke] client effects: "+fx.Played+" played, "+fx.Failed+" failed: "+string.Join(", ",fx.PlayedMethods.OrderBy(m=>m)));
            string[] bossSide={"Float","Shake","SweepResolve","TollResolve","ChargeTrail","ChargeLand","ChimeResolve","BossHit","PlayerHit","PotionHeal","ResetAll","Crescendo"};
            string[] arcanist={"MissileTapCast","MissileChargeStart","Missile","BeamStart","Beam","SigilPlace","CometMark","GlyphGain"};
            Debug.Log("[LAN smoke] client prediction: drew its own character up to "+maxLead.ToString("0.00")+" m ahead of the host's last state");
            Check("client predicts its own movement (ahead of the host, within 3 m)",maxLead>0.05f && maxLead<3);
            if (ClientClass=="Arcanist") Check("client plays the Arcanists' ability effects",fx!=null && arcanist.Count(m=>fx.PlayedMethods.Contains(m))>=3);
            else Check("client plays player ability effects ("+ClientClass+" client)",fx!=null && fx.PlayedMethods.Any(m=>!bossSide.Contains(m)&&!m.Contains(".")));
            Check("client plays hit feedback and damage numbers",fx!=null && fx.PlayedMethods.Contains("Float"));
            Check("client effect replay has almost no failures",fx!=null && fx.Played>0 && fx.Failed*20<=fx.Played);
            if (combatTime>0) Debug.Log("[LAN smoke] client frame rate in combat: "+Mathf.RoundToInt(combatFrames/combatTime)+" fps over "+combatTime.ToString("0.0")+" s");
            Debug.Log("[LAN smoke] client drawing: the boss moved in "+snapshots+" snapshots and on "+movedFrames+" drawn frames");
            Check("client draws boss motion between snapshots (no freezing; skipped if the boss barely moved)",snapshots<5 || movedFrames*2>=snapshots);
            if (observedResult && session && session.ClientView && session.ClientView.Current != null) Debug.Log("[LAN smoke] CLIENT OUTCOME " + (session.ClientView.Current.Victory ? "victory" : "party defeat"));
            if (!observedResult) yield break;
            until=Time.realtimeSinceStartup+40; bool observedRetry=false;
            while (session && session.ClientView && session.ClientView.Error==null && Time.realtimeSinceStartup<until)
            {
                var state=session.ClientView.Current;
                if (state!=null && state.RunState==(int)RunState.Combat) { observedRetry=true; break; }
                yield return null;
            }
            Check("client follows the host's retry into a new fight",observedRetry && session && session.ClientView && session.ClientView.Error==null);
            yield return new WaitForSecondsRealtime(observedRetry ? 6 : 1); // stay connected while the host checks the session
        }
        // Review screenshot of what this client shows, into -lastbellOut.
        IEnumerator Shot(string name)
        {
            var args=Environment.GetCommandLineArgs(); int at=Array.IndexOf(args,"-lastbellOut");
            if (at<0 || at+1>=args.Length) yield break;
            yield return new WaitForEndOfFrame();   // after OnGUI, so the HUD is in the picture
            var tex=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);
            tex.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0); tex.Apply();
            Directory.CreateDirectory(args[at+1]); File.WriteAllBytes(Path.Combine(args[at+1],name),tex.EncodeToPNG()); Destroy(tex);
        }
        void Finish()
        {
            var args=Environment.GetCommandLineArgs(); int at=Array.IndexOf(args,"-lastbellOut");
            lines.Add("RESULT "+(lines.Count-failures)+" passed, "+failures+" failed");
            if (at>=0 && at+1<args.Length) { Directory.CreateDirectory(args[at+1]); File.WriteAllLines(Path.Combine(args[at+1],host?"lan-host-test.txt":"lan-client-test.txt"),lines); }
            foreach (string line in lines) Debug.Log("[LAN smoke] "+line);
            Application.Quit(failures==0?0:1);
        }
    }
}
#endif
