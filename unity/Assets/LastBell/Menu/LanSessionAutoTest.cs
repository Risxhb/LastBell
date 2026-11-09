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
            // Names come from -lastbellProfile (Run-TwoProcess.ps1 passes Host, Client 1..3); never from the saved profile.
            string myName=LanProfile.Overridden ? LanProfile.Current : host ? "Host" : "Client";
            try { session=LanSessionCoordinator.Open(host ? LanLobby.Host(Port(),LanLobbyScreen.BuildId(),"Tollbreaker",myName) : LanLobby.Connect("127.0.0.1",Port(),LanLobbyScreen.BuildId(),ClientClass,myName)); }
            catch(Exception ex) { Check("open LAN: "+ex.Message,false); Finish(); yield break; }
            Debug.Log(host ? "[LAN smoke] HOST READY" : "[LAN smoke] CLIENT CONNECTING");
            int peerCount=PeerCount(); float until=Time.realtimeSinceStartup+90;
            while (session && session.Lobby != null && Time.realtimeSinceStartup<until && (host ? session.Lobby.Members.Count<peerCount+1 : !session.Lobby.Connected)) yield return null;
            bool joined=session && session.Lobby != null && (host ? session.Lobby.Members.Count==peerCount+1 : session.Lobby.Connected);
            Check("host and client join lobby",joined);
            if (!joined) { Finish(); yield break; }
            Check("lobby shows this player's name",session.Lobby.Local!=null && session.Lobby.Local.Name.StartsWith(myName,StringComparison.Ordinal));
            if (host)
            {
                var lobby=session.Lobby; string arena=Argument("-lastbellLanArena","test"), boss=Argument("-lastbellLanBoss","Bellkeeper");
                Check("every player arrives with a name",lobby.Members.All(m=>LanPlayerName.ValidRoster(m.Name)) && lobby.Members.Select(m=>m.Name).Distinct().Count()==lobby.Members.Count);
                Check("host selects requested arena and encounter",lobby.SetSelection(arena,boss));
                Check("host can't start before everyone is ready",!session.HostStart() && !lobby.CanStart);
                until=Time.realtimeSinceStartup+60;
                while (lobby.Members.Any(m=>m.Id!=0&&!m.Ready) && Time.realtimeSinceStartup<until) yield return null;
                Check("clients press Ready",lobby.Members.All(m=>m.Id==0||m.Ready));
                Check("host still can't start until the host is ready",!lobby.CanStart && lobby.SetReady(true) && lobby.CanStart);
                // Change the boss and back: everyone is un-readied each time and must ready again.
                string other=boss=="Bellkeeper"?"Tempo Tyrant":"Bellkeeper";
                Check("changing the boss un-readies everyone",lobby.SetSelection(arena,other) && lobby.Members.All(m=>!m.Ready) && !lobby.CanStart);
                Check("host returns to the requested boss",lobby.SetSelection(arena,boss) && lobby.SetReady(true));
                until=Time.realtimeSinceStartup+60;
                while (!lobby.CanStart && Time.realtimeSinceStartup<until) yield return null;
                Check("everyone readies again after the boss change",lobby.CanStart);
                // the Bellkeeper is the Ultimate now: its jobs (Escapement Relay first) wipe an automated party before any plain
                // hazard appears, so the smoke fight practises Chime Debts alone (hazards, no wipe); the full fight is not covered here
                BellkeeperEncounter.AbilityOverride = boss=="Bellkeeper" ? UltimateMechanics.ChimeDebts : null;
                Check("host begins session",session.HostStart());
            }
            int clientSelections=0;
            until=Time.realtimeSinceStartup+90;
            while (session && !session.InFight && Time.realtimeSinceStartup<until)
            {
                // Automated client: press Ready whenever the host's lobby shows this player as not ready.
                var lobby=session.Lobby;
                if (!host && lobby!=null && lobby.Connected && !lobby.FightStarted && !lobby.LocalReady) lobby.SetReady(true);
                if (!host && lobby!=null) clientSelections=Mathf.Max(clientSelections,lobby.SelectionRevision);
                yield return null;
            }
            if (!host) Check("client saw the host change the boss (Ready cleared and pressed again)",clientSelections>=2);
            Check("both players load arena",session && session.InFight);
            if (!session || !session.InFight) { Finish(); yield break; }
            if (host) yield return HostFight(session); else yield return ClientFight(session);
            Finish();
        }
        IEnumerator HostFight(LanSessionCoordinator session)
        {
            var d=RunDirector.Instance;
            var lanRoster=LanHostRoster.Active; var lobbyMembers=session.Lobby.Members.OrderBy(m=>m.Id).ToList();
            Check("fight players match the lobby: host class, remote names and classes",d && lanRoster!=null && d.ClassName==lobbyMembers[0].ClassName
                && lanRoster.Remote.Select(m=>m.Name+"/"+m.ClassName).SequenceEqual(lobbyMembers.Skip(1).Select(m=>m.Name+"/"+m.ClassName))
                && d.Allies.Select(a=>a.ClassName).SequenceEqual(lobbyMembers.Skip(1).Select(m=>m.ClassName)));
            Check("host has all remote player actors",d && d.World != null && d.Allies.Count==PeerCount() && d.Allies.All(a=>a.ClassName==ClientClass));
            if (!d || d.World==null || d.Allies.Count!=PeerCount()) yield break;
            Vector3 initial=d.Allies[0].transform.position;
            if (d.State==RunState.Ready) d.RequestReady();
            // the Pendulum Sentinel's opening hits often wipe the automated party first: throw its weights straight away
            if (d.Boss is PendulumSentinelEncounter opener) opener.ForcedNext="Weights";
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
            // the boss's own presentation the client must mirror: force the mechanics a short fight never reaches
            if (session && d && d.State==RunState.Combat && d.Boss)
            {
                string forced=Showcase(d);
                Debug.Log("[LAN smoke] host showcase: "+forced);
                float showUntil=Time.realtimeSinceStartup+(d.Boss is AurelionEncounter ? 45 : 14);
                while (session && d && d.State==RunState.Combat && Time.realtimeSinceStartup<showUntil)
                {
                    if (d.Boss is AurelionEncounter a && !a.TestBusy && Time.realtimeSinceStartup>showUntil-41) break;
                    foreach (var p in d.World.Players) if (p && !p.Down) p.DebugSetHp(PlayerController.MaxHp);   // keep the party up while it plays
                    yield return null;
                }
                if (d && d.Boss is AurelionEncounter held) held.TestHold=false;
            }
            if (hazard) yield return new WaitForSecondsRealtime(1);
            bool diagnosticVictory = session && d && d.State==RunState.Combat && d.Boss;
            Debug.Log("[LAN smoke] trace after wait: state " + (d ? d.State.ToString() : "-") + " forcing kill " + diagnosticVictory);
            if (diagnosticVictory) d.Boss.DebugDefeat();
            // Some bosses (the Chimebound Oracle) shrug off the forced kill, so allow time for a real wipe or victory.
            until=Time.realtimeSinceStartup+45;
            float fallback=Time.realtimeSinceStartup+10; bool forcedWipe=false;
            while (session && d && d.State!=RunState.Results && Time.realtimeSinceStartup<until)
            {
                sawDowned |= d.State==RunState.Downed; sawCleared |= d.State==RunState.Cleared;
                // a boss that ignores the forced kill (the Chimebound Oracle holds its HP) and a party that won't fall: end it with a wipe
                if (!forcedWipe && d.State==RunState.Combat && Time.realtimeSinceStartup>fallback && d.World!=null)
                { forcedWipe=true; Debug.Log("[LAN smoke] forcing a party wipe to reach results"); foreach (var p in d.World.Players) if (p && !p.Down) p.ForceDown("LAN smoke test"); }
                yield return null;
            }
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
        // Forces the mechanics whose look only the boss draws: orbs and thrown weights, the Tempo Tyrant's banner and Falling
        // Hours, Aurelion's Zenith Collapse, Orrery Closing and Last Light.
        static string Showcase(RunDirector d)
        {
            switch (d.Boss)
            {
                case TempoTyrantEncounter t: t.DebugSetHpFraction(0.08f); return "Tempo Tyrant below 10% (Broken Cadence, Falling Hours)";
                case PendulumSentinelEncounter p: p.ForcedNext="Weights"; return "Pendulum Sentinel Weights";
                case ChimeboundOracleEncounter o: o.ForcedNext="Wells"; return "Chimebound Oracle Wells";
                case AurelionEncounter a: a.TestHold=true; a.TestRun(AurelionShowcase(a,d.World)); return "Aurelion Zenith Collapse, Orrery Closing, Last Light";
                case SiltCarillonEncounter s: s.ForcedNext="TideBells"; return "Silt Carillon Tide Bells (bell numerals)";
                case VesperSeedkeeperEncounter v: v.ForcedNext="Cords"; return "Vesper Seedkeeper Cords (stakes, cords, numerals)";
                case CrucibleSkaterEncounter c: c.ForcedNext="Toss"; return "Crucible Skater Toss (ladles, numerals)";
                case RivetCantorEncounter r: r.ForcedNext="Verse"; return "Rivet Cantor Verse (glyphs, Chorus numeral)";
                case DialWardenEncounter w: w.ForcedNext="Mainspring"; return "Dial Warden Mainspring (wave wall)";
                case SylvaraEncounter sy: sy.DebugSetHpFraction(0.74f); sy.DebugRoots(); return "Sylvara phase 2 colours and Root Ring";
                case EscapementTwinsEncounter et: et.ForcedNext="PalletLock"; return "Escapement Twins Pallet Lock (Tock, team marks)";
                case BriarMetronomeEncounter bm: bm.ForcedNext="Hedge"; return "Briar Metronome Hedge (hedges, grafts)";
                case CrucivarEncounter cv: cv.StartAtPhase(2); return "Crucivar Foundry Uprising (turrets, castings)";
                default: return "none";
            }
        }
        // what the client's replica boss drew that only the boss draws (numerals, prompts...): most at once, per boss
        static int ReplicaShown(BossEncounter b)
        {
            switch (b)
            {
                case SiltCarillonEncounter s: return s.ReplicaNumeralsShown;
                case VesperSeedkeeperEncounter _: return VesperSeedkeeperHud.ReplicaNumeralsShown;
                case CrucibleSkaterEncounter c: return c.ReplicaNumeralsShown;
                case RivetCantorEncounter r: return r.ReplicaNumeralsShown;
                case SylvaraEncounter sy: return sy.NetRoots + sy.NetColourRingsMax;
                case BellkeeperEncounter bk: return bk.ReplicaLookFrames;
                case EscapementTwinsEncounter et: return et.NetTockSynced>0 ? et.NetTockSynced : 0;
                case BriarMetronomeEncounter bm: return bm.NetNumeralViews + bm.NetHedgesMade;
                case CrucivarEncounter cv: return cv.NetMissed>0 ? 0 : cv.NetEffects;
                default: return -1;
            }
        }
        static IEnumerable<int> AurelionShowcase(AurelionEncounter a, CombatWorld w)
        {
            foreach (var t in a.HungryCollapse()) yield return t;
            yield return Sim.Ticks(1f);
            foreach (var t in a.Closing.Run(a.HorizonFor())) yield return t;
            yield return Sim.Ticks(1f);
            foreach (var t in LastLightFloorFx.During(w, a.Light, a.Light.Run().Take(3))) yield return t;
        }
        IEnumerator ClientFight(LanSessionCoordinator session)
        {
            float until=Time.realtimeSinceStartup+150; bool observedCombat=false, observedDamage=false, observedResult=false, observedHazard=false, observedTelegraph=false, observedProjectile=false, observedHud=false, observedOwnCooldown=false;
            float combatSince=-1, combatTime=0; int combatFrames=0; bool shotCombat=false;
            int musicExpected=0, musicPlaying=0; bool sawNormal=false; bool wantMusic=false, sawAurelion=false; bool sawProps=false, sawAnnouncement=false, shownAnnouncement=false, sawClock=false, clockMoved=false; double firstBeat=-1;
            int numbersSent=0, numbersShown=0, replicaShown=-1, bulletsSent=0; string bossName=""; TempoTyrantAudio tempoAudio=null; string lastTrack="";
            bool rosterMatched=false; float maxLead=0; LanWorldFrame lastFrame=null; int snapshots=0, movedFrames=0; Vector3 lastSnapshotPos=Vector3.zero; Vector3 lastDrawn=Vector3.zero; bool haveDrawn=false;
            while (session && session.ClientView && Time.realtimeSinceStartup<until)
            {
                var state=session.ClientView.Current;
                if (state!=null)
                {
                    var d=RunDirector.Instance; var me=session.Lobby!=null ? state.Players.Find(p=>p.Id==session.Lobby.LocalId) : null;
                    if (!rosterMatched) { var view=session.ClientView; rosterMatched=session.Lobby.Members.All(m=>view.NameOf(m.Id)==m.Name && view.ClassOf(m.Id)==m.ClassName); }
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
                    // boss music: the client plays the fight's own track (both By My Measure versions for the Tempo Tyrant)
                    if (d && d.Boss && state.RunState==(int)RunState.Combat && combatSince>=0 && Time.realtimeSinceStartup-combatSince>2 && MenuMusic.TrackFor(d.Boss)!=null)
                    {
                        var music=FindAnyObjectByType<MenuMusic>(); var clip=music && music.Source ? music.Source.clip : null;
                        bool tyrant=d.Boss is TempoTyrantEncounter; musicExpected++;
                        if (clip && music.Source.isPlaying && (clip.name==MenuMusic.TrackFor(d.Boss) || (tyrant && clip.name==TempoTyrantAudio.SlowTrack))) musicPlaying++;
                        if (clip) lastTrack=clip.name;
                        if (d.Boss is TempoTyrantEncounter t) { tempoAudio=t.Audio; sawNormal |= state.Boss!=null && !state.Boss.BeatHaste; }
                    }
                    // boss presentation that only the host computes: orbs, the Tempo Tyrant's HUD, the boss clock
                    if (state.Boss!=null && state.RunState==(int)RunState.Combat && d && d.Boss)
                    {
                        sawProps |= state.Boss.Props.Count>0;
                        replicaShown=Mathf.Max(replicaShown,ReplicaShown(d.Boss)); bulletsSent=Mathf.Max(bulletsSent,state.Bullets.Count); bossName=d.Boss.GetType().Name;
                        if (d.Boss is TempoTyrantEncounter ty)
                        {
                            if (!string.IsNullOrEmpty(state.Boss.Announcement)) { sawAnnouncement=true; shownAnnouncement |= ty.Announcement==state.Boss.Announcement; }
                            numbersSent=Mathf.Max(numbersSent,state.Boss.Numbers.Count); numbersShown=Mathf.Max(numbersShown,ty.Numbers.Count);
                        }
                        var clk = d.Boss is TempoTyrantEncounter t1 ? t1.Clock : d.Boss is ChimeboundOracleEncounter o1 ? o1.Clock : null;
                        if (clk!=null) { sawClock=true; if (firstBeat<0) firstBeat=clk.Beat; else clockMoved |= clk.Beat>firstBeat+2; }
                    }
                    wantMusic |= d && d.Boss && MenuMusic.TrackFor(d.Boss)!=null;
                    sawAurelion |= d && d.Boss is AurelionEncounter;
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
            Check("client draws every lobby player with their lobby name and class",rosterMatched);
            Debug.Log("[LAN smoke] client music: boss track playing on "+musicPlaying+"/"+musicExpected+" combat frames (last: "+lastTrack+")"+(tempoAudio ? " · "+tempoAudio.StatusText : ""));
            if (wantMusic) Check("client plays the boss music ("+lastTrack+")",musicExpected>0 && musicPlaying*20>=musicExpected*19);
            if (tempoAudio) Check("client Tempo Tyrant music follows the host's tempo (locked"+(sawNormal?", switched versions":"")+", at most 2 calm corrections)",
                tempoAudio.MusicLocked && (!sawNormal || tempoAudio.MusicSwaps>=1) && tempoAudio.CalmResyncs<=2);
            var view2=session.ClientView;
            Debug.Log("[LAN smoke] client boss presentation: props sent "+sawProps+", most drawn "+(view2?view2.PropsShown:0)+"; announcement sent "+sawAnnouncement+", shown "+shownAnnouncement
                +"; strike numbers sent "+numbersSent+", shown "+numbersShown+"; clock "+(sawClock?(clockMoved?"moving":"still"):"none")+"; replayed Aurelion effects "+(view2?view2.ReplicaEffects:0));
            if (sawProps) Check("client draws the boss's orbs and thrown objects",view2 && view2.PropsShown>0);
            Check("client boss looks run without errors",view2 && view2.LookFailures==0);
            if (bulletsSent>0) Check("client draws the host's bullets ("+bulletsSent+" in one snapshot)",view2 && view2.BulletsSeen>0);
            if (replicaShown>=0) Check("client draws the boss's own overlays ("+bossName+": "+replicaShown+")",replicaShown>0);
            if (sawAurelion) Check("client replays Aurelion's Zenith Collapse, Orrery Closing and Last Light",view2 && view2.ReplicaEffects>=3);
            if (sawAnnouncement) Check("client HUD shows the Tempo Tyrant's announcements",shownAnnouncement);
            if (numbersSent>0) Check("client HUD shows the Tempo Tyrant's strike numbers",numbersShown>0);
            if (sawClock) Check("client boss clock follows the host (Metronome Court, music, tempo HUD)",clockMoved);
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
