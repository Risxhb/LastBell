using System;
using System.Collections.Generic;

namespace LastBell.Network.Rules
{
    // Outer delivery envelope only; foundation U3 defines combat payload schemas.
    public sealed class EventRecord
    {
        public readonly Guid SessionId;
        public readonly int Generation;
        public readonly long EventId, Tick, PlayerId;
        public readonly string Kind;
        private readonly byte[] payload;
        public byte[] Payload { get { return (byte[])payload.Clone(); } }
        public EventRecord(Guid session, int generation, long id, long tick, long playerId, string kind, byte[] bytes)
        {
            if (session == Guid.Empty || generation < 0 || id <= 0 || tick < 0 || playerId < 0 ||
                string.IsNullOrEmpty(kind) || kind.Length > 64 || bytes == null || bytes.Length > 4096)
                throw new ArgumentException("Invalid event envelope");
            SessionId = session; Generation = generation; EventId = id; Tick = tick;
            PlayerId = playerId; Kind = kind; payload = (byte[])bytes.Clone();
        }
        public bool SameContent(EventRecord other)
        {
            if (other == null || other.SessionId != SessionId || other.Generation != Generation ||
                other.EventId != EventId || other.Tick != Tick || other.PlayerId != PlayerId ||
                other.Kind != Kind || other.payload.Length != payload.Length) return false;
            for (int i = 0; i < payload.Length; i++) if (payload[i] != other.payload[i]) return false;
            return true;
        }
    }

    public sealed class EventReplay
    {
        public readonly bool NeedsCheckpoint;
        public readonly long Watermark;
        private readonly EventRecord[] records;
        public EventRecord[] Records { get { return (EventRecord[])records.Clone(); } }
        public EventReplay(bool checkpoint, long watermark, EventRecord[] events)
        { NeedsCheckpoint = checkpoint; Watermark = watermark; records = (EventRecord[])events.Clone(); }
    }

    public sealed class EventJournal
    {
        private sealed class Subscriber { public long Ack, Sent; }
        private readonly Guid session;
        private readonly int capacity;
        private readonly List<EventRecord> history = new List<EventRecord>();
        private readonly Dictionary<ulong, Subscriber> subscribers = new Dictionary<ulong, Subscriber>();
        public int Generation { get; private set; }
        public long LastIssued { get; private set; }
        public EventJournal(Guid identity, int generation = 0, int retained = 128)
        {
            if (identity == Guid.Empty || generation < 0 || retained < 1 || retained > 1024)
                throw new ArgumentException("Invalid stream configuration");
            session = identity; Generation = generation; capacity = retained;
        }
        // Only trusted session coordinator may allocate/remove subscribers.
        public void RegisterPeer(ulong peer) { subscribers.Add(peer, new Subscriber()); }
        public void RemovePeer(ulong peer) { subscribers.Remove(peer); }
        public void BeginGeneration(int generation)
        {
            if (generation <= Generation) throw new ArgumentException("Generation must increase");
            Generation = generation; LastIssued = 0; history.Clear();
            foreach (Subscriber subscriber in subscribers.Values) { subscriber.Ack = 0; subscriber.Sent = 0; }
        }
        public EventRecord Append(long tick, long playerId, string kind, byte[] payload)
        {
            long next = checked(LastIssued + 1);
            var record = new EventRecord(session, Generation, next, tick, playerId, kind, payload);
            LastIssued = next; history.Add(record);
            if (history.Count > capacity) history.RemoveAt(0);
            return record;
        }
        public EventReplay ReplayFor(ulong trustedPeer)
        {
            Subscriber peer;
            if (!subscribers.TryGetValue(trustedPeer, out peer)) throw new ArgumentException("Unknown subscriber");
            bool checkpoint = history.Count > 0 && peer.Ack < history[0].EventId - 1;
            if (checkpoint) return new EventReplay(true, LastIssued, new EventRecord[0]);
            var result = new List<EventRecord>();
            foreach (EventRecord record in history) if (record.EventId > peer.Ack) result.Add(record);
            peer.Sent = Math.Max(peer.Sent, LastIssued);
            return new EventReplay(false, LastIssued, result.ToArray());
        }
        // Call only after sending a trusted full snapshot with this event watermark.
        // This function does not construct/restore a combat checkpoint.
        public void MarkCheckpointSent(ulong trustedPeer, long watermark)
        {
            Subscriber peer;
            if (!subscribers.TryGetValue(trustedPeer, out peer) || watermark < 0 || watermark > LastIssued)
                throw new ArgumentException("Invalid checkpoint watermark");
            peer.Sent = Math.Max(peer.Sent, watermark);
        }
        public RuleResult Acknowledge(ulong trustedPeer, Guid identity, int generation, long applied)
        {
            Subscriber peer;
            if (!subscribers.TryGetValue(trustedPeer, out peer)) return RuleResult.Reject("unknown_sender");
            if (identity != session) return RuleResult.Reject("wrong_session");
            if (generation != Generation) return RuleResult.Reject("wrong_generation");
            if (applied < peer.Ack || applied > peer.Sent) return RuleResult.Reject("invalid_event_ack");
            peer.Ack = applied; return RuleResult.Accept();
        }
    }

    public sealed class EventInbox
    {
        private readonly Guid session;
        private readonly int capacity;
        private readonly SortedDictionary<long, EventRecord> pending = new SortedDictionary<long, EventRecord>();
        private long observedWatermark;
        public int Generation { get; private set; }
        public long LastApplied { get; private set; }
        public bool NeedsCheckpoint { get; private set; }
        public bool NeedsReplay
        { get { return !NeedsCheckpoint && observedWatermark > LastApplied && !pending.ContainsKey(LastApplied + 1); } }
        public int BufferedCount { get { return pending.Count; } }
        public EventInbox(Guid identity, int generation = 0, int buffered = 128)
        {
            if (identity == Guid.Empty || generation < 0 || buffered < 1 || buffered > 1024)
                throw new ArgumentException("Invalid inbox configuration");
            session = identity; Generation = generation; capacity = buffered;
        }

        public RuleResult ObserveWatermark(Guid identity, int generation, long watermark)
        {
            if (identity != session) return RuleResult.Reject("wrong_session");
            if (generation != Generation) return RuleResult.Reject("wrong_generation");
            if (watermark < 0) return RuleResult.Reject("invalid_watermark");
            observedWatermark = Math.Max(observedWatermark, watermark);
            return RuleResult.Accept();
        }

        // Validate whole bounded batch before mutating. Caller authenticates host and
        // decodes lengths/types before constructing these records.
        public RuleResult Offer(EventRecord[] batch)
        {
            if (NeedsCheckpoint) return RuleResult.Reject("checkpoint_required");
            if (batch == null || batch.Length > capacity) return RuleResult.Reject("invalid_batch_size");
            var additions = new SortedDictionary<long, EventRecord>();
            long high = observedWatermark;
            foreach (EventRecord record in batch)
            {
                if (record == null) return RuleResult.Reject("null_event");
                if (record.SessionId != session) return RuleResult.Reject("wrong_session");
                if (record.Generation != Generation) return RuleResult.Reject("wrong_generation");
                if (record.EventId <= LastApplied) continue; // already applied feedback
                EventRecord existing;
                if ((pending.TryGetValue(record.EventId, out existing) || additions.TryGetValue(record.EventId, out existing))
                    && !existing.SameContent(record)) return RuleResult.Reject("conflicting_event");
                additions[record.EventId] = record;
                high = Math.Max(high, record.EventId);
            }
            if (high - LastApplied > capacity)
            { NeedsCheckpoint = true; pending.Clear(); return RuleResult.Reject("checkpoint_required"); }
            foreach (var item in additions) pending[item.Key] = item.Value;
            observedWatermark = high;
            return RuleResult.Accept();
        }

        public bool ApplyNext(Action<EventRecord> apply)
        {
            if (apply == null) throw new ArgumentNullException("apply");
            if (NeedsCheckpoint) return false;
            EventRecord record;
            if (!pending.TryGetValue(LastApplied + 1, out record)) return false;
            // Application must be atomic/idempotent. On failure retain event and do not ack.
            apply(record);
            pending.Remove(record.EventId); LastApplied = record.EventId;
            return true;
        }

        // Trusted coordinator calls after installing a full authoritative snapshot
        // atomically, including roster/tick/generation and the supplied event watermark.
        // Skipped historical VFX are not replayed. No arbitrary-tick combat restore here.
        public RuleResult InstallCheckpoint(Guid identity, int generation, long watermark)
        {
            if (identity != session) return RuleResult.Reject("wrong_session");
            if (generation < Generation) return RuleResult.Reject("wrong_generation");
            if (watermark < 0 || (generation == Generation && watermark < LastApplied))
                return RuleResult.Reject("invalid_watermark");
            Generation = generation; LastApplied = watermark; observedWatermark = watermark;
            pending.Clear(); NeedsCheckpoint = false;
            return RuleResult.Accept();
        }
    }
}
