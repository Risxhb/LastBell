using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LastBell.Run.Networking
{
    // Main-thread session/lobby state. It does not start or synchronize combat.
    public sealed class LanLobby : IDisposable
    {
        // Total players in a LAN group, host included. Every LAN limit derives from this.
        public const int Capacity = 4;
        public const int MaxClients = Capacity - 1;
        const byte Protocol = 3, Hello = 1, Snapshot = 2, ChooseClass = 3, StartFight = 4, InputFrame = 5, WorldFrame = 6, SceneReady = 7;
        const int MaxMessagesPerPump = 64;
        static readonly string[] Classes = { "Vanguard", "Chimesinger", "Arcanist" };
        static readonly string[] Arenas = { "test", "skyward-spire", "brass-foundry", "verdant-chimes", "shattered-hour", "metronome-court" };
        static readonly string[] Encounters = { "Bellkeeper", "Aurelion", "Tempo Tyrant", "Chimebound Oracle", "Pendulum Sentinel" };
        public sealed class Member { public int Id; public string ClassName; }
        sealed class Peer { public IConnection Connection; public int Id; public bool Joined, Ready; public DateTime Deadline; public int LastInput = -1; }
        readonly List<Peer> peers = new List<Peer>();
        readonly List<Member> members = new List<Member>();
        readonly string build;
        readonly bool host;
        LanListener listener;
        IConnection server;
        bool helloSent, clientReadySent, disposed;
        string localClass;
        public bool IsHost => host;
        public string Error { get; private set; }
        public bool Connected { get; private set; }
        public bool FightStarted { get; private set; }
        public bool AllClientsReady => host && FightStarted && peers.Count > 0 && peers.TrueForAll(p => p.Joined && p.Ready);
        public event Action<int, int, byte[]> InputReceived;
        public event Action<int, byte[]> WorldReceived;
        public event Action FightStarting;
        int lastWorld = -1;
        public int LocalId { get; private set; } = -1;
        public string Arena { get; private set; } = Arenas[0];
        public string Encounter { get; private set; } = Encounters[0];
        public IReadOnlyList<Member> Members => members.AsReadOnly();
        public int Revision { get; private set; }

        LanLobby(string buildId, string className, bool isHost)
        {
            if (string.IsNullOrEmpty(buildId) || buildId.Length > 80) throw new ArgumentException("Build ID must contain 1–80 characters.");
            if (!ValidClass(className)) throw new ArgumentException("Unknown class.");
            build = buildId; localClass = className; host = isHost;
        }
        public static bool ValidClass(string value) => Array.IndexOf(Classes, value) >= 0;
        public static bool ValidArena(string value) => Array.IndexOf(Arenas, value) >= 0;
        public static bool ValidEncounter(string value) => Array.IndexOf(Encounters, value) >= 0;
        public static LanLobby Host(int port, string buildId, string className)
        {
            var lobby = new LanLobby(buildId, className, true);
            lobby.listener = new LanListener(port); lobby.LocalId = 0; lobby.Connected = true;
            lobby.members.Add(new Member { Id = 0, ClassName = className });
            return lobby;
        }
        public static LanLobby Connect(string address, int port, string buildId, string className)
        {
            var lobby = new LanLobby(buildId, className, false);
            lobby.server = LanConnection.Connect(address, port);
            return lobby;
        }
        static byte[] Pack(Action<BinaryWriter> write)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8)) { write(writer); writer.Flush(); return stream.ToArray(); }
        }
        static string ReadText(BinaryReader reader, int max)
        {
            // BinaryReader.ReadString allows large allocations, so bound the whole frame before parsing too.
            var value = reader.ReadString();
            if (value.Length > max) throw new InvalidDataException("Text too long.");
            return value;
        }
        static void Complete(BinaryReader reader) { if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Trailing data."); }
        bool Send(IConnection connection, byte[] message)
        {
            if (connection.Send(message)) return true;
            return false;
        }
        public bool SetClass(string className)
        {
            if (disposed || FightStarted || !ValidClass(className)) return false;
            if (host) { localClass = className; members[0].ClassName = className; Broadcast(); return true; }
            if (!Connected || !Send(server, Pack(w => { w.Write(ChooseClass); w.Write(className); }))) return false;
            localClass = className; return true;
        }
        public bool SetSelection(string arena, string encounter)
        {
            if (disposed || FightStarted || !host || !ValidArena(arena) || !ValidEncounter(encounter)) return false;
            Arena = arena; Encounter = encounter; Broadcast(); return true;
        }
        // Combat integration owns the payload schemas. TCP preserves order, but sequence checks reject stale frames.
        public bool Start()
        {
            if (disposed || !host || FightStarted || Error != null || members.Count < 2 || peers.Exists(p => !p.Joined)) return false;
            var frame = Pack(w => w.Write(StartFight));
            foreach (var peer in peers) if (!Send(peer.Connection, frame)) { Error = "Could not start: connection is not ready."; foreach (var other in peers) other.Connection.Dispose(); return false; }
            FightStarted = true; foreach (var peer in peers) peer.Deadline = DateTime.UtcNow.AddSeconds(90); FightStarting?.Invoke(); return true;
        }
        public bool SendInput(int sequence, byte[] payload)
        {
            if (disposed || host || !Connected || !FightStarted || !clientReadySent || sequence < 0 || payload == null || payload.Length > 256) return false;
            return Send(server, Pack(w => { w.Write(InputFrame); w.Write(sequence); w.Write(payload.Length); w.Write(payload); }));
        }
        public bool ClientSceneReady()
        {
            if (disposed || host || !Connected || !FightStarted || clientReadySent || !Send(server, Pack(w => w.Write(SceneReady)))) return false;
            clientReadySent = true; return true;
        }
        // tick is a session-wide sequence that must increase across retries (not the CombatWorld tick).
        public bool SendWorld(int tick, byte[] payload)
        {
            if (disposed || !host || !AllClientsReady || Error != null || tick < 0 || payload == null || payload.Length > LanConnection.MaxMessageBytes - 16) return false;
            var frame = Pack(w => { w.Write(WorldFrame); w.Write(tick); w.Write(payload.Length); w.Write(payload); });
            foreach (var peer in peers) if (!Send(peer.Connection, frame)) { Error = "A player could not receive the fight state."; foreach (var other in peers) other.Connection.Dispose(); return false; }
            return true;
        }
        static byte[] ReadPayload(BinaryReader reader, int limit)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > limit || length != reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException("Invalid frame length.");
            return reader.ReadBytes(length);
        }
        int NextId()
        {
            for (int id = 1; id < Capacity; id++) if (!peers.Exists(p => p.Id == id)) return id;
            return -1;
        }
        public void Pump()
        {
            if (disposed || Error != null) return;
            if (host) PumpHost(); else PumpClient();
        }
        void PumpHost()
        {
            if (listener.Error != null) { Error = "Listener failed: " + listener.Error; return; }
            while (listener.TryAccept(out var c))
            {
                int id = NextId();
                if (id < 0 || FightStarted) { c.Dispose(); continue; }
                peers.Add(new Peer { Connection = c, Id = id, Deadline = DateTime.UtcNow.AddSeconds(10) });
            }
            bool changed = false;
            for (int i = peers.Count - 1; i >= 0; i--)
            {
                var p = peers[i];
                try
                {
                    if (p.Connection.State == ConnectionState.Failed || p.Connection.State == ConnectionState.Closed || ((!p.Joined || (FightStarted && !p.Ready)) && DateTime.UtcNow > p.Deadline)) throw new IOException("Peer disconnected or timed out.");
                    int handled = 0;
                    while (handled++ < MaxMessagesPerPump && p.Connection.TryReceive(out var bytes))
                    {
                        if (bytes.Length > 512) throw new InvalidDataException("Client message too long.");
                        using (var reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8))
                        {
                            byte type = reader.ReadByte();
                            if (!p.Joined && type == Hello)
                            {
                                byte protocol = reader.ReadByte(); string version = ReadText(reader, 80); string cls = ReadText(reader, 32); Complete(reader);
                                if (protocol != Protocol || version != build || !ValidClass(cls)) throw new InvalidDataException("Incompatible client or class.");
                                p.Joined = true; members.Add(new Member { Id = p.Id, ClassName = cls }); changed = true;
                            }
                            else if (p.Joined && !FightStarted && type == ChooseClass)
                            {
                                string cls = ReadText(reader, 32); Complete(reader);
                                if (!ValidClass(cls)) throw new InvalidDataException("Unknown class.");
                                members.Find(m => m.Id == p.Id).ClassName = cls; changed = true;
                            }
                            else if (p.Joined && FightStarted && p.Ready && type == InputFrame)
                            {
                                int sequence = reader.ReadInt32(); var payload = ReadPayload(reader, 256);
                                if (sequence <= p.LastInput) throw new InvalidDataException("Out-of-order input.");
                                p.LastInput = sequence; InputReceived?.Invoke(p.Id, sequence, payload);
                            }
                            else if (p.Joined && FightStarted && type == SceneReady && !p.Ready) { Complete(reader); p.Ready = true; }
                            else throw new InvalidDataException("Unexpected lobby message.");
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is FormatException || ex is ArgumentException || ex is InvalidOperationException)
                {
                    p.Connection.Dispose(); peers.RemoveAt(i);
                    if (p.Joined) { members.RemoveAll(m => m.Id == p.Id); changed = true; }
                    if (FightStarted) Error = "A player disconnected during the fight.";
                }
            }
            if (FightStarted && Error != null) { foreach (var p in peers) p.Connection.Dispose(); return; }
            if (changed) Broadcast();
        }
        void Broadcast()
        {
            Revision++;
            foreach (var peer in peers)
            {
                if (!peer.Joined) continue;
                var data = Pack(w => { w.Write(Snapshot); w.Write(peer.Id); w.Write(Revision); w.Write(Arena); w.Write(Encounter); w.Write(members.Count); foreach (var m in members) { w.Write(m.Id); w.Write(m.ClassName); } });
                if (!Send(peer.Connection, data)) peer.Connection.Dispose();
            }
        }
        void PumpClient()
        {
            if (server.State == ConnectionState.Failed || server.State == ConnectionState.Closed) { Error = Connected || !(server is LanConnection tcp && tcp.WasConnected) ? "Connection lost: " + server.Error : "The host closed the connection (the group may be full at " + Capacity + " players, the fight may have started, or the build differs)."; Connected = false; return; }
            if (server.State != ConnectionState.Connected) return;
            if (!helloSent)
            {
                if (!Send(server, Pack(w => { w.Write(Hello); w.Write(Protocol); w.Write(build); w.Write(localClass); }))) { Error = "Could not send join request."; return; }
                helloSent = true;
            }
            try
            {
                int handled = 0;
                while (handled++ < MaxMessagesPerPump && server.TryReceive(out var bytes))
                {
                    using (var reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8))
                    {
                        byte type = reader.ReadByte();
                        if (type == StartFight && Connected && !FightStarted) { Complete(reader); FightStarted = true; FightStarting?.Invoke(); continue; }
                        if (type == WorldFrame && FightStarted)
                        {
                            int tick = reader.ReadInt32(); var payload = ReadPayload(reader, LanConnection.MaxMessageBytes - 16);
                            if (tick <= lastWorld) throw new InvalidDataException("Out-of-order world state.");
                            lastWorld = tick; WorldReceived?.Invoke(tick, payload); continue;
                        }
                        if (type != Snapshot || FightStarted || bytes.Length > 1024) throw new InvalidDataException("Unexpected server message.");
                        int id = reader.ReadInt32(), revision = reader.ReadInt32(); string arena = ReadText(reader, 40), encounter = ReadText(reader, 40); int count = reader.ReadInt32();
                        if (id < 1 || id >= Capacity || revision < Revision || !ValidArena(arena) || !ValidEncounter(encounter) || count < 1 || count > Capacity) throw new InvalidDataException("Invalid lobby snapshot.");
                        var updated = new List<Member>();
                        for (int i = 0; i < count; i++) { int memberId = reader.ReadInt32(); string cls = ReadText(reader, 32); if (memberId < 0 || memberId >= Capacity || !ValidClass(cls) || updated.Exists(m => m.Id == memberId)) throw new InvalidDataException("Invalid roster."); updated.Add(new Member { Id = memberId, ClassName = cls }); }
                        Complete(reader);
                        if (!updated.Exists(m => m.Id == 0) || !updated.Exists(m => m.Id == id)) throw new InvalidDataException("Incomplete roster.");
                        members.Clear(); members.AddRange(updated); LocalId = id; Revision = revision; Arena = arena; Encounter = encounter; Connected = true;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is FormatException || ex is ArgumentException || ex is InvalidOperationException) { Error = "Invalid lobby data: " + ex.Message; Connected = false; server.Dispose(); }
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true; Connected = false;
            listener?.Dispose(); server?.Dispose(); foreach (var p in peers) p.Connection.Dispose(); peers.Clear();
        }
    }
}
