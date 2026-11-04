using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using LastBell.Run.Networking;

static class Program
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
    static bool Wait(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < until) { if (condition()) return true; Thread.Sleep(10); }
        return false;
    }
    static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop(); return port;
    }
    static void Main()
    {
        var intent = new LanInputFrame { MoveX = .6f, MoveY = .8f, AimX = 12, AimY = 0, AimZ = -5, Flags = 0x2011 };
        Check(LanInputFrame.TryDecode(intent.Encode(), out var roundtrip) && roundtrip.MoveX == intent.MoveX && roundtrip.Flags == intent.Flags, "input intent round trips");
        Check(!LanInputFrame.TryDecode(new byte[21], out _), "truncated player input rejected");
        var invalid = intent; invalid.MoveX = float.NaN;
        Check(!invalid.Valid, "nonfinite input rejected");
        invalid = intent; invalid.MoveX = 1; invalid.MoveY = 1;
        Check(!invalid.Valid, "overspeed input rejected");
        invalid = intent; invalid.Flags = 0x8000;
        Check(!invalid.Valid, "unknown input flags rejected");
        var inputs = new LanInputBuffer();
        var first = intent; first.Flags = 0x12; var second = intent; second.Flags = 0x2000;
        Check(inputs.Accept(first) && inputs.Accept(second), "valid input packets accepted");
        Check(inputs.Consume().Flags == 0x2012 && inputs.Consume().Flags == 0x2000, "presses latch across packets and fire once");
        for (int i = 0; i < LanInputBuffer.StaleAfterTicks; i++) inputs.Consume();
        var stale = inputs.Consume();
        Check(stale.Flags == 0 && stale.MoveX == 0 && stale.MoveY == 0, "stalled input stops movement and held actions");
        var unityInput = new LastBell.Run.PlayerInput { Move = new UnityEngine.Vector2(.6f, .8f), Aim = new UnityEngine.Vector3(1, 2, 3), Sprint = true, Jump = true, Dash = true, Potion = true, P0 = true, P1 = true, P2 = true, P3 = true, P4 = true, H0 = true, H1 = true, H2 = true, H3 = true, H4 = true };
        var encodedUnity = LanUnityInput.Encode(unityInput);
        var decodedUnity = LanUnityInput.Decode(encodedUnity);
        Check(encodedUnity.Flags == LanInputFrame.ValidFlags && decodedUnity.Sprint && decodedUnity.Jump && decodedUnity.Dash && decodedUnity.Potion && decodedUnity.P0 && decodedUnity.P1 && decodedUnity.P2 && decodedUnity.P3 && decodedUnity.P4 && decodedUnity.H0 && decodedUnity.H1 && decodedUnity.H2 && decodedUnity.H3 && decodedUnity.H4 && decodedUnity.Aim.z == 3, "all Unity input controls map to network flags and back");
        var remote = new LanRemoteInput();
        Check(remote.Accept(encodedUnity.Encode()) && remote.Read(null).P4 && !remote.Read(null).P4, "remote player source consumes a press once");
        var world = new LanWorldFrame { Tick = 75, RunState = 3, StateTicks = 12, Boss = new LanWorldFrame.BossState { Hp = 800, MaxHp = 1000, Phase = "Phase 1", Attack = "sweep", Clip = "Attack", PoseKind = 1, Pose = 3 } };
        var worldPlayer = new LanWorldFrame.Player { Id = 0, Hp = 55, MaxHp = 100, Potions = 3, MaxStamina = 100, Stamina = 70, ClassState = "ready", Clip = "Run", Overlay = "Cast", ClipSpeed = 1.2f };
        worldPlayer.Marks.Add("sun"); worldPlayer.Cooldowns[1] = .5f; world.Players.Add(worldPlayer);
        world.Hazards.Add(new LanWorldFrame.HazardState { Id = 7, Shape = 1, Radius = 6, Inner = 2, Age = 10, LockAt = 20, ResolveAt = 50, Source = "ring", Mechanic = "spread" });
        var tele = new LanWorldFrame.TelegraphState { Id = 42, Shape = 3, Radius = 12, Inner = 3, X = 2, Z = -1, Progress = .4f, Tinted = true, FillR = 1.2f, BorderB = .7f, MaxFade = .55f, HoleCount = 1 };
        tele.Holes[0] = 3; tele.Holes[1] = -2; tele.Holes[2] = 1.5f; world.Telegraphs.Add(tele);
        world.Projectiles.Add(new LanWorldFrame.ProjectileState { Id = 8, X = 1, Y = 2, Z = 3, R = .5f, A = 1 });
        var worldBytes = world.Encode();
        Check(LanWorldFrame.TryDecode(worldBytes, out var decodedWorld) && decodedWorld.Tick == 75 && decodedWorld.Boss.Hp == 800 && decodedWorld.Boss.Pose == 3 && decodedWorld.Players[0].Clip == "Run" && decodedWorld.Players[0].Overlay == "Cast" && decodedWorld.Players[0].Marks[0] == "sun" && decodedWorld.Hazards[0].Inner == 2 && decodedWorld.Telegraphs[0].Holes[2] == 1.5f && decodedWorld.Telegraphs[0].FillR == 1.2f && decodedWorld.Projectiles[0].Z == 3, "world snapshot round trips actors, poses, hazards, telegraphs and projectiles");
        Check(!LanWorldFrame.TryDecode(worldBytes[..^1], out _) && !LanWorldFrame.TryDecode(new byte[0], out _), "truncated world snapshots rejected");
        // boss clock, HUD text, strike numbers and boss props (orbs) for the client's replica
        var clockFrame = new LanWorldFrame { Tick = 76, RunState = 3, Boss = new LanWorldFrame.BossState { Hp = 5, MaxHp = 10, PoseKind = 2, Beat = 41.25, BeatHaste = true, Transitioning = true, TransitionTick = 2, StateAge = 600, Announcement = "TEMPO SLOWS" } };
        clockFrame.Players.Add(worldPlayer);
        clockFrame.Boss.Numbers.Add(new LanWorldFrame.Number { X = 1, Z = -2, N = 3, Imminent = true });
        clockFrame.Boss.Props.Add(new LanWorldFrame.Prop { Id = -7, Kind = 2, X = 4, Y = 0.9f, Z = -3 });
        Check(LanWorldFrame.TryDecode(clockFrame.Encode(), out var clockBack) && clockBack.Boss.Beat == 41.25 && clockBack.Boss.BeatHaste && clockBack.Boss.Transitioning && clockBack.Boss.TransitionTick == 2
            && clockBack.Boss.StateAge == 600 && clockBack.Boss.Announcement == "TEMPO SLOWS" && clockBack.Boss.Numbers.Count == 1 && clockBack.Boss.Numbers[0].N == 3 && clockBack.Boss.Numbers[0].Imminent
            && clockBack.Boss.Props.Count == 1 && clockBack.Boss.Props[0].Id == -7 && clockBack.Boss.Props[0].Kind == 2 && clockBack.Boss.Props[0].Z == -3, "world snapshot round trips the boss clock, HUD text, numbers and props");
        // boss looks, telegraph flash/stretch and hostile bullets (4 Nov 2026)
        var lookFrame = new LanWorldFrame { Tick = 77, RunState = 3, Boss = new LanWorldFrame.BossState { Hp = 5, MaxHp = 10, Glow = 2.5f } };
        lookFrame.Players.Add(worldPlayer); lookFrame.Boss.Look.Add(3); lookFrame.Boss.Look.Add(-1.5f);
        var stretched = new LanWorldFrame.TelegraphState { Id = 5, Shape = 3, Radius = 4, Inner = 1, FlashScale = 0.4f, ScaleZ = 2.5f }; lookFrame.Telegraphs.Add(stretched);
        lookFrame.Bullets.Add(new LanWorldFrame.BulletState { X = 1, Z = 2, VX = -3, VZ = 0.5f, Radius = 0.6f, Colour = 2, Water = true });
        Check(LanWorldFrame.TryDecode(lookFrame.Encode(), out var lookBack) && lookBack.Boss.Glow == 2.5f && lookBack.Boss.Look.Count == 2 && lookBack.Boss.Look[1] == -1.5f
            && lookBack.Telegraphs[0].FlashScale == 0.4f && lookBack.Telegraphs[0].ScaleZ == 2.5f && lookBack.Bullets.Count == 1 && lookBack.Bullets[0].VX == -3 && lookBack.Bullets[0].Colour == 2 && lookBack.Bullets[0].Water,
            "world snapshot round trips boss looks, telegraph flash and stretch, and hostile bullets");
        var tooManyLooks = new LanWorldFrame { Tick = 1, RunState = 3, Boss = new LanWorldFrame.BossState { Hp = 1, MaxHp = 1 } }; tooManyLooks.Players.Add(worldPlayer);
        for (int i = 0; i <= LanWorldFrame.MaxLook; i++) tooManyLooks.Boss.Look.Add(i);
        bool lookRejected; try { tooManyLooks.Encode(); lookRejected = false; } catch (System.IO.InvalidDataException) { lookRejected = true; }
        Check(lookRejected, "world snapshot refuses more boss look numbers than the limit");
        // parry presses travel with the input, with the tick the client was showing, and wait until the boss takes them
        var qteIn = new LanInputFrame { Qte = 0x11, QteTick = 1234 };
        Check(LanInputFrame.TryDecode(qteIn.Encode(), out var qteBack) && qteBack.Qte == 0x11 && qteBack.QteTick == 1234, "input round trips parry presses and their tick");
        Check(!new LanInputFrame { Qte = 0x20 }.Valid, "input refuses unknown parry bits");
        var qteBuffer = new LanInputBuffer(); qteBuffer.Accept(new LanInputFrame { Qte = 0x01, QteTick = 10 }); qteBuffer.Accept(new LanInputFrame { Qte = 0x10, QteTick = 12 }); qteBuffer.Accept(new LanInputFrame());
        Check(qteBuffer.TakeQte(out int qteTick) == 0x11 && qteTick == 12 && qteBuffer.TakeQte(out _) == 0, "parry presses from several packets add up until taken, then clear");
        var tooMany = new LanWorldFrame { Tick = 1, RunState = 3, Boss = new LanWorldFrame.BossState { Hp = 1, MaxHp = 1 } }; tooMany.Players.Add(worldPlayer);
        for (int i = 0; i <= LanWorldFrame.MaxProps; i++) tooMany.Boss.Props.Add(new LanWorldFrame.Prop { Id = i });
        bool rejected; try { tooMany.Encode(); rejected = false; } catch (System.IO.InvalidDataException) { rejected = true; }
        Check(rejected, "world snapshot refuses more boss props than the limit");
        // recorded effect calls travel with the snapshot: references, numbers, colours, text and projectiles
        var fxFrame = new LanWorldFrame { Tick = 9, RunState = 3 }; fxFrame.Players.Add(worldPlayer);
        fxFrame.Effects.Add(new LanWorldFrame.Effect { Tick = 8, Target = 0, Method = "Slash", Args = new[] {
            new LanWorldFrame.EffectArg { Kind = LanWorldFrame.EffectArg.PlayerRef, I = 2 }, new LanWorldFrame.EffectArg { Kind = LanWorldFrame.EffectArg.Number, X = 2.5f },
            new LanWorldFrame.EffectArg { Kind = LanWorldFrame.EffectArg.Flag, I = 1 }, new LanWorldFrame.EffectArg { Kind = LanWorldFrame.EffectArg.Vector, X = 1, Y = 2, Z = 3 },
            new LanWorldFrame.EffectArg { Kind = LanWorldFrame.EffectArg.Tint, X = 1.5f, W = 1 }, new LanWorldFrame.EffectArg { Kind = LanWorldFrame.EffectArg.Words, S = "-12" },
            new LanWorldFrame.EffectArg { Kind = LanWorldFrame.EffectArg.BossTransform }, new LanWorldFrame.EffectArg { Kind = LanWorldFrame.EffectArg.Integer, I = -4 } } });
        fxFrame.Effects.Add(new LanWorldFrame.Effect { Tick = 9, Target = 4, Method = "Move", Args = new[] { new LanWorldFrame.EffectArg { Kind = LanWorldFrame.EffectArg.ProjectileRef, I = 77, X = 1, VZ = 30 } } });
        Check(LanWorldFrame.TryDecode(fxFrame.Encode(), out var fxBack) && fxBack.Effects.Count == 2 && fxBack.Effects[0].Method == "Slash" && fxBack.Effects[0].Tick == 8
            && fxBack.Effects[0].Args[0].I == 2 && fxBack.Effects[0].Args[1].X == 2.5f && fxBack.Effects[0].Args[2].I == 1 && fxBack.Effects[0].Args[3].Z == 3 && fxBack.Effects[0].Args[4].X == 1.5f
            && fxBack.Effects[0].Args[5].S == "-12" && fxBack.Effects[0].Args[6].Kind == LanWorldFrame.EffectArg.BossTransform && fxBack.Effects[0].Args[7].I == -4
            && fxBack.Effects[1].Target == 4 && fxBack.Effects[1].Args[0].I == 77 && fxBack.Effects[1].Args[0].VZ == 30, "world snapshot round trips recorded effect calls");
        bool EncodesFx(LanWorldFrame.Effect e) { var f = new LanWorldFrame { Tick = 9, RunState = 3 }; f.Players.Add(worldPlayer); f.Effects.Add(e); try { f.Encode(); return true; } catch (System.IO.InvalidDataException) { return false; } }
        Check(!EncodesFx(new LanWorldFrame.Effect { Tick = 1, Target = 9, Method = "Slash" }) && !EncodesFx(new LanWorldFrame.Effect { Tick = 1, Method = "" })
            && !EncodesFx(new LanWorldFrame.Effect { Tick = 1, Method = "Slash", Args = new[] { new LanWorldFrame.EffectArg { Kind = LanWorldFrame.EffectArg.PlayerRef, I = 4 } } })
            && !EncodesFx(new LanWorldFrame.Effect { Tick = 1, Method = "Slash", Args = new[] { new LanWorldFrame.EffectArg { Kind = 99 } } })
            && !EncodesFx(new LanWorldFrame.Effect { Tick = 1, Method = "Slash", Args = new[] { new LanWorldFrame.EffectArg { Kind = LanWorldFrame.EffectArg.Number, X = float.NaN } } }), "invalid effect calls rejected (target, name, player outside the group, unknown argument, non-finite number)");
        var invalidWorld = new LanWorldFrame { Tick = 1, RunState = 3 }; invalidWorld.Players.Add(worldPlayer); invalidWorld.Players.Add(worldPlayer);
        bool duplicateRejected = false; try { invalidWorld.Encode(); } catch (System.IO.InvalidDataException) { duplicateRejected = true; }
        Check(duplicateRejected, "duplicate player identities rejected in snapshots");
        LanWorldFrame Party(int count, int firstId)
        {
            var frame = new LanWorldFrame { Tick = 2, RunState = 3 };
            for (int i = 0; i < count; i++) frame.Players.Add(new LanWorldFrame.Player { Id = firstId + i, Hp = 50, MaxHp = 100 });
            return frame;
        }
        bool Encodes(LanWorldFrame frame) { try { return LanWorldFrame.TryDecode(frame.Encode(), out var back) && back.Players.Count == frame.Players.Count; } catch (System.IO.InvalidDataException) { return false; } }
        Check(LanLobby.Capacity == 4 && LanLobby.MaxClients == 3, "LAN group limit is four players (host plus three clients)");
        Check(Encodes(Party(LanLobby.Capacity, 0)), "world snapshot carries a full four-player party");
        Check(!Encodes(Party(LanLobby.Capacity + 1, 0)), "world snapshot with five players rejected");
        Check(!Encodes(Party(1, LanLobby.Capacity)), "world snapshot player ID outside the four-player range rejected");
        int port = FreePort();
        using (var host = LanConnection.Host(port))
        using (var join = LanConnection.Connect("127.0.0.1", port))
        {
            bool connected = Wait(() => host.State == ConnectionState.Connected && join.State == ConnectionState.Connected);
            Check(connected, $"host and client connect over loopback TCP (host={host.State}: {host.Error}; client={join.State}: {join.Error})");
            Check(!host.Send(new byte[0]) && !host.Send(new byte[LanConnection.MaxMessageBytes + 1]), "invalid message sizes rejected");
            for (int i = 0; i < 100; i++) Check(join.Send(Encoding.UTF8.GetBytes("input-" + i)), "send " + i);
            int received = 0;
            Check(Wait(() => { while (host.TryReceive(out var bytes)) { if (Encoding.UTF8.GetString(bytes) != "input-" + received) throw new Exception("Out of order"); received++; } return received == 100; }), "100 framed messages arrive in order");
            // A host that stalls (scene load) must not drop a client sending 60 Hz input: ten seconds' worth queues up.
            for (int i = 0; i < 600; i++) if (!join.Send(new byte[] { (byte)(i & 255), (byte)(i >> 8) })) Wait(() => join.Send(new byte[] { (byte)(i & 255), (byte)(i >> 8) }));
            Thread.Sleep(500);
            Check(host.State == ConnectionState.Connected, "receiver survives ten seconds of undrained 60 Hz input");
            int drained = 0;
            Check(Wait(() => { while (host.TryReceive(out var bytes)) { if ((bytes[0] | (bytes[1] << 8)) != drained) throw new Exception("Out of order after stall"); drained++; } return drained == 600; }), "stalled messages drain in order");
            Check(host.Send(new byte[] { 7, 8, 9 }), "host sends reply");
            byte[] reply = null;
            Check(Wait(() => join.TryReceive(out reply)) && reply.Length == 3 && reply[2] == 9, "client receives reply");
            join.Dispose();
            Check(Wait(() => host.State == ConnectionState.Failed), "peer disconnect is detected");
        }
        using (var waiting = LanConnection.Host(port)) Check(waiting.State == ConnectionState.Connecting, "port can be reused after close");
        using (var missing = LanConnection.Connect("127.0.0.1", port)) Check(Wait(() => missing.State == ConnectionState.Failed), "connection failure is reported");
        using (var listener = new LanListener(port))
        {
            var clients = new LanConnection[LanLobby.MaxClients];
            var peers = new LanConnection[LanLobby.MaxClients];
            LanConnection overflow = null;
            try
            {
                for (int i = 0; i < clients.Length; i++) clients[i] = LanConnection.Connect("127.0.0.1", port);
                Check(Wait(() => Array.TrueForAll(clients, c => c.State == ConnectionState.Connected)), "three client sockets connect");
                // Nothing has been drained yet, so the pending queue is full and a fourth socket must be closed.
                Thread.Sleep(200);
                overflow = LanConnection.Connect("127.0.0.1", port);
                Check(Wait(() => overflow.State == ConnectionState.Failed), "listener refuses a fourth pending client");
                int count = 0;
                Check(Wait(() => { while (count < peers.Length && listener.TryAccept(out var peer)) peers[count++] = peer; return count == peers.Length; }), "listener accepts three remote peers");
                Check(!listener.TryAccept(out _), "listener queued no more than three peers");
                Check(Wait(() => Array.TrueForAll(peers, p => p.State == ConnectionState.Connected) && Array.TrueForAll(clients, p => p.State == ConnectionState.Connected)), "four-player transport connections established");
                for (int i = 0; i < clients.Length; i++) Check(clients[i].Send(new byte[] { (byte)(i + 1) }), "peer sends " + i);
                var from = new bool[clients.Length];
                LanConnection firstPeer = null;
                for (int i = 0; i < peers.Length; i++)
                {
                    int n = i; byte[] received = null;
                    Check(Wait(() => peers[n].TryReceive(out received)) && received.Length == 1 && received[0] >= 1 && received[0] <= clients.Length && !from[received[0] - 1], "host receives unique peer " + i);
                    from[received[0] - 1] = true;
                    if (received[0] == 1) firstPeer = peers[i];
                }
                var buffer = new byte[] { 42 };
                Check(clients[0].Send(buffer), "queued send accepts caller buffer"); buffer[0] = 99;
                byte[] snapshot = null;
                Check(Wait(() => firstPeer.TryReceive(out snapshot)) && snapshot[0] == 42, "queued send copies caller buffer");
            }
            finally
            {
                foreach (var client in clients) client?.Dispose();
                foreach (var peer in peers) peer?.Dispose();
                overflow?.Dispose();
            }
        }
        Check(LanPlayerName.Normalize("  Bell   Ringer ") == "Bell Ringer", "player name: trimmed, repeated spaces collapsed");
        Check(LanPlayerName.Valid("Ana") && LanPlayerName.Valid("Toll_Breaker-2") && LanPlayerName.Valid("ab"), "player name: letters, digits, space, _ and - accepted");
        Check(!LanPlayerName.Valid("A") && !LanPlayerName.Valid("   ") && !LanPlayerName.Valid(null), "player name: too short or empty rejected");
        Check(!LanPlayerName.Valid("Seventeen chars!!") && !LanPlayerName.Valid("abcdefghijklmnopq"), "player name: over 16 characters rejected");
        foreach (var bad in new[] { "<b>Ana</b>", "Ana!", "Ãna", "Ana." }) Check(!LanPlayerName.Valid(bad), "player name: markup, symbols and non-ASCII rejected: " + bad + " -> " + LanPlayerName.Problem(bad));
        Check(LanPlayerName.MakeUnique("Ana", new[] { "ana", "Ana (2)" }) == "Ana (3)" && LanPlayerName.MakeUnique("Bo", new[] { "Ana" }) == "Bo", "player name: host suffixes duplicates (ignoring case)");
        Check(LanPlayerName.ValidRoster("Ana (2)") && LanPlayerName.ValidRoster("abcdefghijklmnop (4)") && !LanPlayerName.ValidRoster("Ana (1)") && !LanPlayerName.ValidRoster("Ana (x)") && !LanPlayerName.ValidRoster("<i>x</i>"), "player name: roster names allow only the host's suffix");
        bool badNameRefused = false; try { LanLobby.Host(port, "build-a", "Tollbreaker", "<b>Ana</b>").Dispose(); } catch (ArgumentException) { badNameRefused = true; }
        Check(badNameRefused, "an invalid local name can't open a lobby");
        using (var lobby = LanLobby.Host(port, "build-a", "Tollbreaker", "Host"))
        {
            var clients = new LanLobby[LanLobby.MaxClients];
            try
            {
                string[] joinNames = { "Ana", "ana", "Bo" };
                for (int i = 0; i < clients.Length; i++) clients[i] = LanLobby.Connect("127.0.0.1", port, "build-a", "Arcanist", joinNames[i]);
                bool TickUntil(Func<bool> condition) => Wait(() => { lobby.Pump(); foreach (var c in clients) c.Pump(); return condition(); });
                Check(TickUntil(() => lobby.Members.Count == 4 && Array.TrueForAll(clients, c => c.Connected && c.Members.Count == 4)), "lobby admits host and three clients with complete roster");
                Check(Array.TrueForAll(clients, c => string.Join(",", System.Linq.Enumerable.OrderBy(System.Linq.Enumerable.Select(c.Members, m => m.Name.ToLowerInvariant()), n => n, StringComparer.Ordinal)) == "ana,ana (2),bo,host"), "names reach every client; the host suffixes a duplicate name (join order varies)");
                Check(Array.TrueForAll(clients, c => System.Linq.Enumerable.All(c.Members, m => !m.Ready)) && !lobby.AllMembersReady, "everyone joins not ready");
                using (var fourth = LanLobby.Connect("127.0.0.1", port, "build-a", "Tollbreaker", "Fourth"))
                {
                    Check(TickUntil(() => { fourth.Pump(); return fourth.Error != null; }) && !fourth.Connected, "fourth client is rejected when the group is full");
                    Check(fourth.Error.Contains("full"), "rejected client is told the group may be full: " + fourth.Error);
                    Check(lobby.Members.Count == 4 && Array.TrueForAll(clients, c => c.Members.Count == 4), "full group roster is unchanged by the rejected client");
                }
                Check(lobby.SetSelection("metronome-court", "Tempo Tyrant"), "host chooses allowed arena and encounter");
                Check(!lobby.SetSelection("test", "Crucivar") && !clients[0].SetSelection("test", "Bellkeeper"), "unavailable encounter and client selection rejected");
                Check(clients[0].SetClass("Chimesinger"), "client requests class");
                int memberId = clients[0].LocalId;
                Check(TickUntil(() => Array.TrueForAll(clients, c => c.Arena == "metronome-court" && c.Encounter == "Tempo Tyrant" && System.Linq.Enumerable.Any(c.Members, m => m.Id == memberId && m.ClassName == "Chimesinger"))), "host selection and class propagate to all clients");
                Check(clients[0].SetReady(true) && clients[0].LocalReady, "client readies");
                Check(TickUntil(() => Array.TrueForAll(clients, c => System.Linq.Enumerable.Any(c.Members, m => m.Id == memberId && m.Ready))), "Ready reaches every client");
                Check(!clients[0].SetClass("Arcanist"), "Ready locks the class");
                Check(!lobby.CanStart && !lobby.Start() && lobby.StartBlocker.StartsWith("Waiting for"), "host can't start while players aren't ready: " + lobby.StartBlocker);
                Check(lobby.SetReady(true) && clients[1].SetReady(true) && clients[2].SetReady(true), "everyone else readies");
                Check(TickUntil(() => lobby.CanStart && Array.TrueForAll(clients, c => c.AllMembersReady)), "host can start once all four are ready");
                int selection0 = clients[1].SelectionRevision;
                Check(lobby.SetSelection("metronome-court", "Tempo Tyrant") && lobby.AllMembersReady, "re-choosing the same boss and arena keeps Ready");
                Check(lobby.SetSelection("metronome-court", "Pendulum Sentinel") && !lobby.CanStart, "changing the boss clears Ready on the host");
                Check(TickUntil(() => Array.TrueForAll(clients, c => c.SelectionRevision > selection0 && System.Linq.Enumerable.All(c.Members, m => !m.Ready) && !c.LocalReady)), "clients see the boss change and everyone un-readied");
                Check(clients[0].SetClass("Arcanist") && TickUntil(() => System.Linq.Enumerable.Any(lobby.Members, m => m.Id == memberId && m.ClassName == "Arcanist")), "un-readied players can change class again");
                Check(lobby.SetReady(true) && clients[0].SetReady(true) && clients[1].SetReady(true) && clients[2].SetReady(true) && TickUntil(() => lobby.CanStart), "all ready again");
                clients[2].Dispose();
                Check(TickUntil(() => lobby.Members.Count == 3 && clients[0].Members.Count == 3), "disconnect removes peer and updates roster");
                using (var wrong = LanLobby.Connect("127.0.0.1", port, "different-build", "Tollbreaker", "Wrong"))
                {
                    Check(Wait(() => { lobby.Pump(); wrong.Pump(); return wrong.Error != null; }), "different build is rejected");
                    Check(lobby.Members.Count == 3, "rejected build never joins roster");
                }
                Check(lobby.CanStart, "a player leaving doesn't un-ready the others");
                var old = LanConnection.Connect("127.0.0.1", port);
                try
                {
                    Check(Wait(() => old.State == ConnectionState.Connected), "protocol-3 client connects");
                    using (var ms = new System.IO.MemoryStream()) using (var w = new System.IO.BinaryWriter(ms, Encoding.UTF8))
                    { w.Write((byte)1); w.Write((byte)3); w.Write("build-a"); w.Write("Arcanist"); w.Flush(); old.Send(ms.ToArray()); }
                    Check(TickUntil(() => old.State == ConnectionState.Closed || old.State == ConnectionState.Failed) && lobby.Members.Count == 3, "a protocol-3 client is refused and never joins");
                }
                finally { old.Dispose(); }
                clients[2] = LanLobby.Connect("127.0.0.1", port, "build-a", "Tollbreaker", "Ana");
                Check(TickUntil(() => lobby.Members.Count == 4 && clients[2].Connected && Array.TrueForAll(clients, c => c.Members.Count == 4)), "a freed slot can be filled again up to four players");
                Check(System.Linq.Enumerable.Any(lobby.Members, m => m.Name == "Ana (3)" && !m.Ready) && !lobby.CanStart && lobby.StartBlocker == "Waiting for 1 to ready.", "a new joiner starts not ready, so Start locks again");
                Check(System.Linq.Enumerable.Count(lobby.Members, m => m.Ready) == 3, "players already ready stay ready when someone joins");
                Check(clients[2].SetReady(true) && TickUntil(() => lobby.CanStart), "Start unlocks when the joiner readies");
                int inputFrom = -1, inputSeq = -1, worldTick = -1; byte[] inputData = null, worldData = null;
                lobby.InputReceived += (id, seq, data) => { inputFrom = id; inputSeq = seq; inputData = data; };
                clients[0].WorldReceived += (tick, data) => { worldTick = tick; worldData = data; };
                Check(lobby.Start(), "host starts a frozen session");
                Check(TickUntil(() => Array.TrueForAll(clients, c => c.FightStarted)), "start reaches clients");
                Check(!lobby.AllClientsReady, "host waits for clients to load");
                for (int i = 0; i < clients.Length; i++) Check(clients[i].ClientSceneReady(), "client reports scene ready " + i);
                Check(TickUntil(() => lobby.AllClientsReady), "host sees all clients ready");
                Check(!lobby.SetSelection("test", "Bellkeeper") && !clients[0].SetClass("Tollbreaker"), "fight freezes lobby selection and class");
                Check(clients[0].SendInput(1, new byte[] { 9, 8 }), "client queues input frame");
                Check(TickUntil(() => inputFrom == memberId) && inputSeq == 1 && inputData.Length == 2 && inputData[0] == 9, "host receives player input with identity and sequence");
                Check(lobby.SendWorld(75, worldBytes), "host queues world snapshot");
                Check(TickUntil(() => worldTick == 75) && LanWorldFrame.TryDecode(worldData, out var receivedWorld) && receivedWorld.Boss.Hp == 800 && receivedWorld.Hazards[0].Id == 7, "client receives and decodes host combat snapshot");
                using (var roster = new LanHostRoster(lobby))
                {
                    Check(roster.Remote.Count == LanLobby.MaxClients && roster.InputFor(memberId) != null, "host roster creates one input source per remote player");
                    Check(clients[0].SendInput(2, encodedUnity.Encode()), "client queues validated player intent");
                    Check(TickUntil(() => inputSeq == 2) && roster.InputFor(memberId).Read(null).P4 && roster.Error == null, "host actor source consumes remote player intent");
                }
                Check(LanHostRoster.Active == null, "host roster clears its global context on disposal");
                clients[2].Dispose();
                Check(TickUntil(() => lobby.Error != null), "session reports a player disconnect after start");
            }
            finally { foreach (var c in clients) c?.Dispose(); }
        }
    }
}
