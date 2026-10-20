using System;
using System.Collections.Generic;
using System.IO;

namespace LastBell.Network.ActionPrototype
{
    // TOOLS-ONLY protocol candidate. Not bound to combat or a Unity transport.
    public enum Ability { Technique = 1, Signature = 2, Guard = 3 }
    public enum ActionStatus { Pending, Consumed, Expired, Rejected, RetryLater, Cancelled, HistoryUnavailable, UnconfirmedTimeout }
    public sealed class Policy
    {
        public readonly int GuardLifetime, OffensiveLifetime, FutureTolerance, MaxLead;
        public readonly int MaxPending, RetainedResults, IdWindow, RequestsPerSecond, RetryMs, ConfirmationTimeoutMs;
        public Policy(int guardLifetime = 12, int offensiveLifetime = 30, int retryMs = 50, int confirmationTimeoutMs = 2000)
        {
            if (guardLifetime < 2 || guardLifetime > 60 || offensiveLifetime < 2 || offensiveLifetime > 60 ||
                retryMs < 10 || confirmationTimeoutMs < 1000 || confirmationTimeoutMs > 10000)
                throw new ArgumentException("Invalid prototype policy");
            GuardLifetime = guardLifetime; OffensiveLifetime = offensiveLifetime;
            RetryMs = retryMs; ConfirmationTimeoutMs = confirmationTimeoutMs;
            FutureTolerance = 6; MaxLead = 12; MaxPending = 32; RetainedResults = 64; IdWindow = 64; RequestsPerSecond = 120;
        }
        public int Lifetime(Ability ability) { return ability == Ability.Guard ? GuardLifetime : OffensiveLifetime; }
        public static bool Known(Ability ability)
        { return ability == Ability.Guard || ability == Ability.Technique || ability == Ability.Signature; }
    }
    public sealed class Request
    {
        public readonly Guid Session;
        public readonly int Generation;
        public readonly long Id, CreatedTick, TargetTick, ExpiryTick;
        public readonly Ability Ability;
        public Request(Guid session, int generation, long id, long created, long target, long expiry, Ability ability)
        { Session = session; Generation = generation; Id = id; CreatedTick = created; TargetTick = target; ExpiryTick = expiry; Ability = ability; }
        public bool Same(Request other)
        { return other != null && Session == other.Session && Generation == other.Generation && Id == other.Id &&
            CreatedTick == other.CreatedTick && TargetTick == other.TargetTick && ExpiryTick == other.ExpiryTick && Ability == other.Ability; }
    }
    public sealed class Receipt
    {
        public readonly Request Request;
        public readonly ActionStatus Status;
        public readonly string Reason;
        public readonly long HostTick;
        public Receipt(Request request, ActionStatus status, long tick, string reason = "")
        { Request = request; Status = status; HostTick = tick; Reason = reason; }
        public bool Terminal { get { return Status != ActionStatus.Pending && Status != ActionStatus.RetryLater; } }
    }
    public sealed class ConsumedAction
    {
        public readonly Request Request;
        public readonly long PlayerId, Tick;
        public readonly ulong ConnectionHandle;
        public ConsumedAction(Request request, long player, long tick, ulong handle)
        { Request = request; PlayerId = player; Tick = tick; ConnectionHandle = handle; }
    }
    public static class RequestCodec
    {
        public const int ByteCount = 56;
        public static byte[] Encode(Request request)
        {
            if (request == null || !Policy.Known(request.Ability)) throw new ArgumentException("Invalid action");
            using (var stream = new MemoryStream(ByteCount))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((byte)'A'); writer.Write((byte)'P'); writer.Write((byte)1);
                writer.Write(request.Session.ToByteArray()); writer.Write(request.Generation);
                writer.Write(request.Id); writer.Write(request.CreatedTick); writer.Write(request.TargetTick);
                writer.Write(request.ExpiryTick); writer.Write((byte)request.Ability);
                return stream.ToArray();
            }
        }
        public static bool TryDecode(byte[] bytes, out Request request)
        {
            request = null;
            if (bytes == null || bytes.Length != ByteCount) return false;
            using (var reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                if (reader.ReadByte() != (byte)'A' || reader.ReadByte() != (byte)'P' || reader.ReadByte() != 1) return false;
                Guid session = new Guid(reader.ReadBytes(16)); int generation = reader.ReadInt32();
                long id = reader.ReadInt64(), created = reader.ReadInt64(), target = reader.ReadInt64(), expiry = reader.ReadInt64();
                Ability ability = (Ability)reader.ReadByte();
                if (!Policy.Known(ability)) return false;
                request = new Request(session, generation, id, created, target, expiry, ability);
                return true;
            }
        }
    }

    public sealed class ActionHost
    {
        private sealed class Peer
        {
            public long Player, Highest, RejectThrough, RateStart;
            public ulong Handle;
            public int RateCount;
            public readonly SortedDictionary<long, Receipt> Tickets = new SortedDictionary<long, Receipt>();
        }
        private readonly Guid session;
        private readonly Policy policy;
        private readonly Dictionary<ulong, Peer> peers = new Dictionary<ulong, Peer>();
        private readonly HashSet<ulong> seenPeers = new HashSet<ulong>();
        public int Generation { get; private set; }
        public long Tick { get; private set; }
        public ActionHost(Guid identity, Policy settings, int generation = 1)
        {
            if (identity == Guid.Empty || settings == null || generation < 0) throw new ArgumentException("Invalid host");
            session = identity; policy = settings; Generation = generation;
        }
        public void Register(ulong authenticatedPeer, long playerId)
        {
            if (playerId <= 0) throw new ArgumentException("Invalid stable player ID");
            if (peers.Count >= 8 || seenPeers.Count >= 128 || seenPeers.Contains(authenticatedPeer))
                throw new InvalidOperationException("Capacity or reused connection handle; use a fresh generation/incarnation");
            foreach (Peer peer in peers.Values) if (peer.Player == playerId) throw new ArgumentException("Duplicate actor");
            peers.Add(authenticatedPeer, new Peer { Player = playerId, Handle = authenticatedPeer }); seenPeers.Add(authenticatedPeer);
        }
        public void Disconnect(ulong authenticatedPeer) { peers.Remove(authenticatedPeer); }
        public int TicketCount(ulong peer) { return peers.ContainsKey(peer) ? peers[peer].Tickets.Count : 0; }
        public void ResetGeneration(int generation)
        {
            if (generation <= Generation) throw new ArgumentException("Generation must increase");
            Generation = generation; Tick = 0;
            seenPeers.Clear(); foreach (ulong handle in peers.Keys) seenPeers.Add(handle);
            foreach (Peer peer in peers.Values)
            { peer.Tickets.Clear(); peer.Highest = 0; peer.RejectThrough = 0; peer.RateStart = 0; peer.RateCount = 0; }
        }

        public Receipt Receive(ulong authenticatedPeer, Request request, long nowMs)
        {
            if (request == null) throw new ArgumentNullException("request");
            Peer peer;
            if (!peers.TryGetValue(authenticatedPeer, out peer)) return Result(request, ActionStatus.Cancelled, "unknown_sender");
            if (request.Session != session) return Result(request, ActionStatus.Rejected, "wrong_session");
            if (request.Generation != Generation) return Result(request, ActionStatus.Rejected, "wrong_generation");
            if (nowMs < peer.RateStart || nowMs < 0) return Result(request, ActionStatus.RetryLater, "invalid_clock");
            if (nowMs - peer.RateStart >= 1000) { peer.RateStart = nowMs; peer.RateCount = 0; }
            if (peer.RateCount >= policy.RequestsPerSecond) return Result(request, ActionStatus.RetryLater, "rate_limited");
            peer.RateCount++;
            Receipt prior;
            if (peer.Tickets.TryGetValue(request.Id, out prior))
                return prior.Request.Same(request) ? prior : Result(request, ActionStatus.Rejected, "conflicting_id");
            if (request.Id <= 0) return Result(request, ActionStatus.Rejected, "invalid_id");
            if (request.Id <= peer.RejectThrough)
                return Result(request, ActionStatus.HistoryUnavailable, "history_unavailable");
            if (request.Id <= 0 || (request.Id > peer.Highest && request.Id - peer.Highest > policy.IdWindow))
                return Result(request, ActionStatus.Rejected, "invalid_id");
            if (!Policy.Known(request.Ability) || request.CreatedTick < 0 ||
                request.CreatedTick > Tick + policy.FutureTolerance || request.CreatedTick > long.MaxValue - 60 ||
                request.TargetTick < request.CreatedTick || request.TargetTick - request.CreatedTick > policy.MaxLead ||
                request.ExpiryTick <= request.CreatedTick || request.ExpiryTick - request.CreatedTick > policy.Lifetime(request.Ability) ||
                request.TargetTick >= request.ExpiryTick)
                return Result(request, ActionStatus.Rejected, "invalid_timing_or_ability");
            if (CountPending(peer) >= policy.MaxPending) return Result(request, ActionStatus.RetryLater, "pending_full");
            peer.Highest = Math.Max(peer.Highest, request.Id);
            Receipt receipt = request.ExpiryTick <= Tick + 1 ? Result(request, ActionStatus.Expired, "deadline") :
                Result(request, ActionStatus.Pending);
            peer.Tickets.Add(request.Id, receipt); Trim(peer);
            return receipt;
        }

        // Caller supplies the one authoritative completed tick; no independent wall clock.
        // Consumed means emitted by this diagnostic scheduler, NOT cooldown eligibility/activation.
        public ConsumedAction[] Step(long completedTick)
        {
            if (completedTick != Tick + 1) throw new ArgumentException("Supply each authoritative tick exactly once");
            Tick = completedTick;
            var emitted = new List<ConsumedAction>();
            var ordered = new List<Peer>(peers.Values);
            ordered.Sort((a, b) => a.Player.CompareTo(b.Player));
            foreach (Peer peer in ordered)
            {
                var ids = new List<long>(peer.Tickets.Keys);
                foreach (long id in ids)
                {
                    Receipt current = peer.Tickets[id];
                    if (current.Status != ActionStatus.Pending) continue;
                    Request request = current.Request;
                    if (Tick >= request.ExpiryTick) peer.Tickets[id] = Result(request, ActionStatus.Expired, "deadline");
                    else if (Tick >= request.TargetTick)
                    {
                        peer.Tickets[id] = Result(request, ActionStatus.Consumed);
                        emitted.Add(new ConsumedAction(request, peer.Player, Tick, peer.Handle));
                    }
                }
                Trim(peer);
            }
            return emitted.ToArray();
        }
        private Receipt Result(Request request, ActionStatus status, string reason = "")
        { return new Receipt(request, status, Tick, reason); }
        private static int CountPending(Peer peer)
        { int count = 0; foreach (Receipt ticket in peer.Tickets.Values) if (ticket.Status == ActionStatus.Pending) count++; return count; }
        private void Trim(Peer peer)
        {
            int terminal = peer.Tickets.Count - CountPending(peer);
            if (terminal <= policy.RetainedResults) return;
            var ids = new List<long>(peer.Tickets.Keys);
            foreach (long id in ids)
            {
                if (terminal <= policy.RetainedResults) break;
                if (!peer.Tickets[id].Terminal) continue;
                peer.Tickets.Remove(id); peer.RejectThrough = Math.Max(peer.RejectThrough, id); terminal--;
            }
        }
    }

    public sealed class ActionClient
    {
        private sealed class Outgoing
        {
            public Request Request;
            public long QueuedMs, LastSentMs = -1;
        }
        private readonly Guid session;
        private readonly Policy policy;
        private readonly SortedDictionary<long, Outgoing> pending = new SortedDictionary<long, Outgoing>();
        private readonly SortedDictionary<long, Receipt> completed = new SortedDictionary<long, Receipt>();
        private long nextId = 1, lastPollMs = -1;
        public int Generation { get; private set; }
        public int PendingCount { get { return pending.Count; } }
        public int CompletedCount { get { return completed.Count; } }
        public ActionClient(Guid identity, Policy settings, int generation = 1)
        {
            if (identity == Guid.Empty || settings == null || generation < 0) throw new ArgumentException("Invalid client");
            session = identity; policy = settings; Generation = generation;
        }
        public Request Queue(Ability ability, long estimatedCreatedTick, long targetTick, long nowMs)
        {
            if (!Policy.Known(ability) || nowMs < 0 || nowMs < lastPollMs || estimatedCreatedTick < 0 ||
                estimatedCreatedTick > long.MaxValue - 60 || targetTick < estimatedCreatedTick ||
                targetTick - estimatedCreatedTick > policy.MaxLead ||
                targetTick >= estimatedCreatedTick + policy.Lifetime(ability))
                throw new ArgumentException("Invalid local action");
            if (pending.Count >= policy.MaxPending) throw new InvalidOperationException("Pending capacity");
            var request = new Request(session, Generation, nextId++, estimatedCreatedTick,
                targetTick, estimatedCreatedTick + policy.Lifetime(ability), ability);
            pending.Add(request.Id, new Outgoing { Request = request, QueuedMs = nowMs });
            return request;
        }
        public Request[] Poll(long nowMs)
        {
            if (nowMs < 0 || nowMs < lastPollMs) throw new ArgumentException("Monotonic client clock required");
            lastPollMs = nowMs;
            var due = new List<Request>();
            var ids = new List<long>(pending.Keys);
            foreach (long id in ids)
            {
                Outgoing item = pending[id];
                if (nowMs - item.QueuedMs >= policy.ConfirmationTimeoutMs)
                {
                    pending.Remove(id);
                    Remember(new Receipt(item.Request, ActionStatus.UnconfirmedTimeout, -1, "outcome_unknown"));
                }
                else if (item.LastSentMs < 0 || nowMs - item.LastSentMs >= policy.RetryMs)
                { item.LastSentMs = nowMs; due.Add(item.Request); }
            }
            return due.ToArray();
        }
        // Transport must authenticate host first. Receipts are not client assertions of combat success.
        public bool OnReceipt(Receipt receipt)
        {
            if (receipt == null || receipt.Request == null || receipt.Request.Session != session ||
                receipt.Request.Generation != Generation || !Enum.IsDefined(typeof(ActionStatus), receipt.Status) || receipt.Status == ActionStatus.UnconfirmedTimeout) return false;
            Outgoing item; Receipt known;
            if (pending.TryGetValue(receipt.Request.Id, out item))
            {
                if (!item.Request.Same(receipt.Request)) return false;
                if (receipt.Terminal) { pending.Remove(receipt.Request.Id); Remember(receipt); }
                return true;
            }
            if (!completed.TryGetValue(receipt.Request.Id, out known) || !known.Request.Same(receipt.Request)) return false;
            if (known.Status == ActionStatus.UnconfirmedTimeout && receipt.Terminal) Remember(receipt);
            // A delayed Pending receipt never rolls a terminal result backward.
            return true;
        }
        public Receipt Outcome(long id) { Receipt result; return completed.TryGetValue(id, out result) ? result : null; }
        public void ResetGeneration(int generation)
        {
            if (generation <= Generation) throw new ArgumentException("Generation must increase");
            Generation = generation; pending.Clear(); completed.Clear(); nextId = 1; lastPollMs = -1;
        }
        private void Remember(Receipt receipt)
        {
            completed[receipt.Request.Id] = receipt;
            while (completed.Count > policy.RetainedResults)
            { foreach (long id in new List<long>(completed.Keys)) { completed.Remove(id); break; } }
        }
    }
}
