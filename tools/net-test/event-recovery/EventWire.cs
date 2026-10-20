using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LastBell.Network.Rules;

namespace LastBell.Network.EventRecovery
{
    public enum FrameKind { Batch = 0, Ack = 1, Checkpoint = 2, CheckpointAck = 3 }
    public sealed class EventFrame
    {
        public readonly FrameKind Kind;
        public readonly ulong Connection;
        public readonly Guid Session;
        public readonly int Generation;
        public readonly long Watermark, SnapshotTick;
        private readonly EventRecord[] events;
        private readonly byte[] state;
        public EventRecord[] Events { get { return (EventRecord[])events.Clone(); } }
        public byte[] State { get { return (byte[])state.Clone(); } }
        public EventFrame(FrameKind kind, ulong connection, Guid session, int generation, long watermark,
            EventRecord[] records = null, long snapshotTick = 0, byte[] checkpoint = null)
        {
            Kind = kind; Connection = connection; Session = session; Generation = generation; Watermark = watermark;
            SnapshotTick = snapshotTick; events = records == null ? new EventRecord[0] : (EventRecord[])records.Clone();
            state = checkpoint == null ? new byte[0] : (byte[])checkpoint.Clone();
        }
    }
    // TOOLS-ONLY candidate. Authentication/integrity and framing/fragmentation belong to the adapter.
    public static class EventWire
    {
        public const int HeaderBytes = 40, MaxRecords = 8, MaxStateBytes = 4096, MaxPacketBytes = 35081;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        public static byte[] Encode(EventFrame frame)
        {
            if (frame == null || frame.Session == Guid.Empty || frame.Generation < 0 || frame.Watermark < 0)
                throw new ArgumentException("Invalid envelope");
            EventRecord[] records = frame.Events; byte[] state = frame.State;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((byte)'E'); writer.Write((byte)'V'); writer.Write((byte)1); writer.Write((byte)frame.Kind);
                writer.Write(frame.Connection); writer.Write(frame.Session.ToByteArray());
                writer.Write(frame.Generation); writer.Write(frame.Watermark);
                if (frame.Kind == FrameKind.Batch)
                {
                    if (records.Length > MaxRecords || state.Length != 0 || frame.SnapshotTick != 0) throw new ArgumentException("Invalid batch");
                    writer.Write((byte)records.Length);
                    var seen = new Dictionary<long, EventRecord>();
                    foreach (EventRecord record in records)
                    {
                        if (record == null || record.SessionId != frame.Session || record.Generation != frame.Generation ||
                            record.EventId > frame.Watermark) throw new ArgumentException("Invalid event scope");
                        EventRecord earlier;
                        if (seen.TryGetValue(record.EventId, out earlier) && !earlier.SameContent(record)) throw new ArgumentException("Conflicting batch");
                        seen[record.EventId] = record;
                        byte[] kind = Utf8.GetBytes(record.Kind), payload = record.Payload;
                        if (kind.Length == 0 || kind.Length > 256) throw new ArgumentException("Invalid kind encoding");
                        writer.Write(record.EventId); writer.Write(record.Tick); writer.Write(record.PlayerId);
                        writer.Write((ushort)kind.Length); writer.Write(kind);
                        writer.Write((ushort)payload.Length); writer.Write(payload);
                    }
                }
                else if (frame.Kind == FrameKind.Checkpoint)
                {
                    if (records.Length != 0 || state.Length > MaxStateBytes || frame.SnapshotTick < 0) throw new ArgumentException("Invalid checkpoint");
                    writer.Write(frame.SnapshotTick); writer.Write((ushort)state.Length); writer.Write(state);
                }
                else if (frame.Kind == FrameKind.CheckpointAck)
                {
                    if (records.Length != 0 || state.Length != 0 || frame.SnapshotTick < 0) throw new ArgumentException("Invalid checkpoint ack");
                    writer.Write(frame.SnapshotTick);
                }
                else if (frame.Kind != FrameKind.Ack || records.Length != 0 || state.Length != 0 || frame.SnapshotTick != 0)
                    throw new ArgumentException("Invalid frame kind");
                return stream.ToArray();
            }
        }
        public static bool TryDecode(byte[] bytes, out EventFrame frame)
        {
            frame = null;
            if (bytes == null || bytes.Length < HeaderBytes || bytes.Length > MaxPacketBytes) return false;
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream))
                {
                    if (reader.ReadByte() != (byte)'E' || reader.ReadByte() != (byte)'V' || reader.ReadByte() != 1) return false;
                    FrameKind kind = (FrameKind)reader.ReadByte(); ulong connection = reader.ReadUInt64();
                    Guid session = new Guid(reader.ReadBytes(16)); int generation = reader.ReadInt32(); long watermark = reader.ReadInt64();
                    if (session == Guid.Empty || generation < 0 || watermark < 0) return false;
                    EventFrame candidate;
                    if (kind == FrameKind.Batch)
                    {
                        int count = reader.ReadByte(); if (count > MaxRecords) return false;
                        var records = new EventRecord[count]; var seen = new Dictionary<long, EventRecord>();
                        for (int i = 0; i < count; i++)
                        {
                            long id = reader.ReadInt64(), tick = reader.ReadInt64(), player = reader.ReadInt64();
                            int kindLength = reader.ReadUInt16();
                            if (kindLength < 1 || kindLength > 256 || kindLength > stream.Length - stream.Position) return false;
                            string name = Utf8.GetString(reader.ReadBytes(kindLength)); int payloadLength = reader.ReadUInt16();
                            if (payloadLength > 4096 || payloadLength > stream.Length - stream.Position || id > watermark) return false;
                            var record = new EventRecord(session, generation, id, tick, player, name, reader.ReadBytes(payloadLength));
                            EventRecord earlier;
                            if (seen.TryGetValue(id, out earlier) && !earlier.SameContent(record)) return false;
                            seen[id] = record; records[i] = record;
                        }
                        candidate = new EventFrame(kind, connection, session, generation, watermark, records);
                    }
                    else if (kind == FrameKind.Checkpoint)
                    {
                        long tick = reader.ReadInt64(); int length = reader.ReadUInt16();
                        if (tick < 0 || length > MaxStateBytes || length > stream.Length - stream.Position) return false;
                        candidate = new EventFrame(kind, connection, session, generation, watermark, snapshotTick: tick, checkpoint: reader.ReadBytes(length));
                    }
                    else if (kind == FrameKind.Ack) candidate = new EventFrame(kind, connection, session, generation, watermark);
                    else if (kind == FrameKind.CheckpointAck)
                    {
                        long tick = reader.ReadInt64(); if (tick < 0) return false;
                        candidate = new EventFrame(kind, connection, session, generation, watermark, snapshotTick: tick);
                    }
                    else return false;
                    if (stream.Position != stream.Length) return false;
                    frame = candidate; return true;
                }
            }
            catch (EndOfStreamException) { return false; }
            catch (ArgumentException) { return false; } // Includes invalid UTF-8 and EventRecord bounds.
        }
    }

    public sealed class EventSender
    {
        private sealed class Peer
        {
            public long ActuallySent, CheckpointTick, CheckpointWatermark;
            public bool Bootstrapped;
            public byte[] Checkpoint;
        }
        private readonly Guid session;
        private readonly EventJournal journal;
        private readonly Dictionary<ulong, Peer> peers = new Dictionary<ulong, Peer>();
        private readonly HashSet<ulong> seen = new HashSet<ulong>();
        public int Generation { get { return journal.Generation; } }
        public long LastIssued { get { return journal.LastIssued; } }
        public int PeerCount { get { return peers.Count; } }
        public EventSender(Guid identity, int generation = 1) { session = identity; journal = new EventJournal(identity, generation); }
        public void Register(ulong connection)
        {
            if (peers.Count >= 8 || seen.Count >= 128 || seen.Contains(connection)) throw new InvalidOperationException("Invalid incarnation/capacity");
            journal.RegisterPeer(connection); peers.Add(connection, new Peer()); seen.Add(connection);
        }
        public void Remove(ulong connection) { journal.RemovePeer(connection); peers.Remove(connection); }
        public EventRecord Append(long tick, string kind, byte[] payload) { return journal.Append(tick, 0, kind, payload); }
        public void Reset(int generation)
        {
            journal.BeginGeneration(generation); seen.Clear();
            foreach (var entry in peers)
            { entry.Value.ActuallySent = 0; entry.Value.Bootstrapped = false; entry.Value.Checkpoint = null; seen.Add(entry.Key); }
        }
        // Null means the caller must capture a complete authoritative checkpoint and watermark together.
        public byte[] BuildBatch(ulong connection)
        {
            Peer peer = peers[connection]; if (!peer.Bootstrapped) return null;
            EventReplay replay = journal.ReplayFor(connection); if (replay.NeedsCheckpoint) return null;
            EventRecord[] available = replay.Records;
            int count = Math.Min(EventWire.MaxRecords, available.Length); var chunk = new EventRecord[count];
            Array.Copy(available, chunk, count);
            byte[] bytes = EventWire.Encode(new EventFrame(FrameKind.Batch, connection, session, Generation, replay.Watermark, chunk));
            if (count > 0) peer.ActuallySent = Math.Max(peer.ActuallySent, chunk[count - 1].EventId);
            return bytes;
        }
        public byte[] BuildCheckpoint(ulong connection, long capturedWatermark, long capturedTick, byte[] capturedState)
        {
            Peer peer = peers[connection];
            if (peer.Checkpoint != null) return (byte[])peer.Checkpoint.Clone();
            if (capturedWatermark != LastIssued) throw new ArgumentException("Capture state and event cursor atomically");
            byte[] bytes = EventWire.Encode(new EventFrame(FrameKind.Checkpoint, connection, session, Generation,
                capturedWatermark, snapshotTick: capturedTick, checkpoint: capturedState));
            journal.MarkCheckpointSent(connection, capturedWatermark);
            peer.ActuallySent = Math.Max(peer.ActuallySent, capturedWatermark); peer.Bootstrapped = false;
            peer.Checkpoint = (byte[])bytes.Clone(); peer.CheckpointTick = capturedTick; peer.CheckpointWatermark = capturedWatermark;
            return bytes;
        }
        public RuleResult ReceiveAck(byte[] bytes, ulong authenticatedConnection)
        {
            Peer peer; if (!peers.TryGetValue(authenticatedConnection, out peer)) return RuleResult.Reject("unknown_sender");
            EventFrame frame;
            if (!EventWire.TryDecode(bytes, out frame) || (frame.Kind != FrameKind.Ack && frame.Kind != FrameKind.CheckpointAck))
                return RuleResult.Reject("malformed_ack");
            if (frame.Connection != authenticatedConnection) return RuleResult.Reject("wrong_incarnation");
            if (frame.Session != session) return RuleResult.Reject("wrong_session");
            if (frame.Generation != Generation) return RuleResult.Reject("wrong_generation");
            // ReplayFor exposes the whole retained window, but only one chunk crossed this wire.
            // Observing the advertised high watermark does NOT authorize acknowledging unsent records.
            if (frame.Watermark > peer.ActuallySent) return RuleResult.Reject("unsent_event_ack");
            if (!peer.Bootstrapped && (peer.Checkpoint == null || frame.Kind != FrameKind.CheckpointAck ||
                frame.SnapshotTick != peer.CheckpointTick || frame.Watermark < peer.CheckpointWatermark))
                return RuleResult.Reject("checkpoint_ack_required");
            RuleResult result = journal.Acknowledge(authenticatedConnection, frame.Session, frame.Generation, frame.Watermark);
            if (result.Ok) { peer.Bootstrapped = true; peer.Checkpoint = null; }
            return result;
        }
    }

    public sealed class EventReceiver
    {
        private readonly Guid session;
        private readonly ulong connection, trustedHost;
        private long checkpointTick = -1, checkpointWatermark = -1;
        private byte[] checkpointState;
        public int PeakBuffered { get; private set; }
        public EventInbox Inbox { get; private set; }
        public EventReceiver(Guid identity, ulong localConnection, int generation = 1, ulong host = 0)
        { session = identity; connection = localConnection; trustedHost = host; Inbox = new EventInbox(identity, generation); }
        public void Reset(int generation)
        {
            if (generation <= Inbox.Generation) throw new ArgumentException("Generation must increase");
            Inbox = new EventInbox(session, generation); checkpointTick = -1; checkpointWatermark = -1; checkpointState = null; PeakBuffered = 0;
        }
        // Caller serializes receive/apply/reset. Checkpoint callback must validate/stage before atomic commit.
        public RuleResult Receive(byte[] bytes, ulong authenticatedHost, Action<EventRecord> apply,
            Func<byte[], long, long, bool> install)
        {
            if (authenticatedHost != trustedHost) return RuleResult.Reject("unauthorized_host");
            EventFrame frame; if (!EventWire.TryDecode(bytes, out frame)) return RuleResult.Reject("malformed_event_frame");
            if (frame.Connection != connection) return RuleResult.Reject("wrong_recipient");
            if (frame.Session != session) return RuleResult.Reject("wrong_session");
            if (frame.Generation != Inbox.Generation) return RuleResult.Reject("wrong_generation");
            if (frame.Kind == FrameKind.Checkpoint)
            {
                if (frame.Watermark < Inbox.LastApplied || frame.SnapshotTick < checkpointTick || frame.Watermark < checkpointWatermark)
                    return RuleResult.Reject("stale_checkpoint");
                if (frame.SnapshotTick == checkpointTick && frame.Watermark == checkpointWatermark)
                {
                    byte[] state = frame.State;
                    if (state.Length != checkpointState.Length) return RuleResult.Reject("conflicting_checkpoint");
                    for (int i = 0; i < state.Length; i++) if (state[i] != checkpointState[i]) return RuleResult.Reject("conflicting_checkpoint");
                    return RuleResult.Accept();
                }
                if (install == null || !install(frame.State, frame.Watermark, frame.SnapshotTick)) return RuleResult.Reject("invalid_checkpoint_state");
                RuleResult result = Inbox.InstallCheckpoint(session, frame.Generation, frame.Watermark);
                checkpointTick = frame.SnapshotTick; checkpointWatermark = frame.Watermark;
                checkpointState = frame.State;
                return result;
            }
            if (frame.Kind != FrameKind.Batch) return RuleResult.Reject("unexpected_frame_kind");
            if (checkpointTick < 0) return RuleResult.Reject("bootstrap_required");
            RuleResult offered = Inbox.Offer(frame.Events); if (!offered.Ok) return offered;
            PeakBuffered = Math.Max(PeakBuffered, Inbox.BufferedCount);
            Inbox.ObserveWatermark(session, frame.Generation, frame.Watermark);
            while (Inbox.ApplyNext(apply)) { }
            return RuleResult.Accept();
        }
        public byte[] BuildAck()
        {
            return EventWire.Encode(new EventFrame(checkpointTick < 0 ? FrameKind.Ack : FrameKind.CheckpointAck,
                connection, session, Inbox.Generation, Inbox.LastApplied, snapshotTick: Math.Max(0, checkpointTick)));
        }
    }
}
