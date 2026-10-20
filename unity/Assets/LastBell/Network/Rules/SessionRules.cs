using System;
using System.Collections.Generic;

namespace LastBell.Network.Rules
{
    public sealed class SessionRules : ISessionInputSink
    {
        public const int MaxPlayers = 8;
        public const int RequestsPerSecond = 120;
        private sealed class Player
        {
            public long Id, Received, Processed, LastTarget;
            public int Slot;
            public string ClassId = "vanguard";
            public bool Ready;
            public readonly SortedDictionary<long, PendingInput> Pending = new SortedDictionary<long, PendingInput>();
        }
        private sealed class PendingInput { public long Sequence; public TickCommand Command; }
        private sealed class Rate { public long Start; public int Count; }
        private readonly Guid sessionId;
        private readonly ulong hostPeer;
        private readonly Dictionary<ulong, Player> peers = new Dictionary<ulong, Player>();
        private readonly Dictionary<ulong, Rate> rates = new Dictionary<ulong, Rate>();
        private long nextId = 1, revision;
        public int Generation { get; private set; }
        public long Tick { get; private set; }
        public SessionPhase Phase { get; private set; }
        public Guid SessionId { get { return sessionId; } }
        public SessionRules(Guid identity, ulong hostPeerId)
        {
            if (identity == Guid.Empty) throw new ArgumentException("Nonempty session identity required");
            sessionId = identity; hostPeer = hostPeerId; Phase = SessionPhase.Lobby;
            Join(hostPeerId);
        }

        public RuleResult Join(ulong trustedPeerId)
        {
            if (peers.ContainsKey(trustedPeerId)) return RuleResult.Reject("already_joined");
            if (Phase != SessionPhase.Lobby) return RuleResult.Reject("encounter_in_progress");
            if (peers.Count >= MaxPlayers) return RuleResult.Reject("session_full");
            int slot = 1;
            var occupied = new HashSet<int>();
            foreach (Player p in peers.Values) occupied.Add(p.Slot);
            while (occupied.Contains(slot)) slot++;
            var player = new Player { Id = nextId++, Slot = slot };
            peers.Add(trustedPeerId, player); revision++;
            return RuleResult.Accept(player.Id);
        }

        public void Disconnect(ulong trustedPeerId)
        {
            if (!peers.Remove(trustedPeerId)) return;
            rates.Remove(trustedPeerId); revision++;
            if (trustedPeerId == hostPeer)
            {
                Phase = SessionPhase.Closed;
                foreach (Player p in peers.Values) p.Pending.Clear();
            }
            // U3's shared encounter policy owns cancellation/reassignment, not this model.
        }

        public RuleResult Control(ulong trustedPeerId, ControlRequest request, long monotonicMs)
        {
            if (request == null) return RuleResult.Reject("null_request");
            RuleResult access = Validate(trustedPeerId, request.SessionId, request.Generation, monotonicMs);
            if (!access.Ok) return access;
            Player player = peers[trustedPeerId];
            switch (request.Kind)
            {
                case ControlKind.SelectClass:
                    if (Phase != SessionPhase.Lobby) return RuleResult.Reject("encounter_in_progress");
                    if (request.ClassId != "vanguard") return RuleResult.Reject("invalid_class");
                    player.ClassId = request.ClassId; player.Ready = false; break;
                case ControlKind.Ready:
                    if (Phase != SessionPhase.Lobby) return RuleResult.Reject("encounter_in_progress");
                    player.Ready = request.Ready; break;
                case ControlKind.Start:
                    if (trustedPeerId != hostPeer) return RuleResult.Reject("host_only");
                    if (Phase != SessionPhase.Lobby) return RuleResult.Reject("encounter_in_progress");
                    foreach (Player p in peers.Values)
                        if (!p.Ready) return RuleResult.Reject("players_not_ready");
                    Generation = checked(Generation + 1); Tick = 0; Phase = SessionPhase.Running;
                    foreach (Player p in peers.Values)
                    { p.Received = 0; p.Processed = 0; p.LastTarget = 0; p.Pending.Clear(); }
                    break;
                case ControlKind.Sync: return RuleResult.Accept(player.Id);
                default: return RuleResult.Reject("unsupported_command");
            }
            revision++; return RuleResult.Accept(player.Id);
        }

        public RuleResult SubmitInput(ulong trustedPeerId, InputRequest input, long monotonicMs)
        {
            if (input == null) return RuleResult.Reject("null_request");
            RuleResult access = Validate(trustedPeerId, input.SessionId, input.Generation, monotonicMs);
            if (!access.Ok) return access;
            if (Phase != SessionPhase.Running) return RuleResult.Reject("not_running");
            Player p = peers[trustedPeerId];
            if (input.Sequence <= p.Received) return RuleResult.Reject("stale_sequence");
            if (input.Sequence - p.Received > 1024) return RuleResult.Reject("sequence_jump");
            if (input.TargetTick < Tick - 6 || input.TargetTick > Tick + 12)
                return RuleResult.Reject("tick_out_of_bounds");
            if (!input.Move.IsFinite) return RuleResult.Reject("invalid_movement");
            long target = Math.Max(Tick + 1, input.TargetTick);
            if (target < p.LastTarget) return RuleResult.Reject("target_tick_regression");
            bool technique = input.Technique, signature = input.Signature, guard = input.Guard;
            PendingInput prior;
            if (p.Pending.TryGetValue(target, out prior))
            { technique |= prior.Command.Technique; signature |= prior.Command.Signature; guard |= prior.Command.Guard; }
            p.Pending[target] = new PendingInput { Sequence = input.Sequence,
                Command = new TickCommand(input.Move.Clamped(), input.Basic, technique, signature, guard) };
            p.Received = input.Sequence; p.LastTarget = target;
            return RuleResult.Accept(p.Id);
        }

        // Explicit diagnostic step; no wall clock or automatic Unity Update/FixedUpdate.
        // Replace with agreed U3 game-loop seam before production binding.
        public Dictionary<long, TickCommand> Advance()
        {
            var result = new Dictionary<long, TickCommand>();
            if (Phase != SessionPhase.Running) return result;
            Tick = checked(Tick + 1);
            foreach (Player p in peers.Values)
            {
                PendingInput pending;
                if (!p.Pending.TryGetValue(Tick, out pending)) continue;
                result.Add(p.Id, pending.Command); p.Processed = pending.Sequence; p.Pending.Remove(Tick);
            }
            return result;
        }

        public SessionSnapshot Snapshot()
        {
            var roster = new List<PlayerInfo>();
            var processed = new Dictionary<long, long>();
            foreach (Player p in peers.Values)
            { roster.Add(new PlayerInfo(p.Id, p.Slot, p.ClassId, p.Ready)); processed.Add(p.Id, p.Processed); }
            roster.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            return new SessionSnapshot(sessionId, Generation, Tick, revision, Phase, roster.ToArray(), processed);
        }

        private RuleResult Validate(ulong peer, Guid identity, int generation, long now)
        {
            if (!peers.ContainsKey(peer)) return RuleResult.Reject("unknown_sender");
            if (Phase == SessionPhase.Closed) return RuleResult.Reject("session_closed");
            if (now < 0) return RuleResult.Reject("invalid_clock");
            Rate window;
            if (!rates.TryGetValue(peer, out window)) { window = new Rate { Start = now }; rates.Add(peer, window); }
            if (now < window.Start) return RuleResult.Reject("invalid_clock");
            if (now - window.Start >= 1000) { window.Start = now; window.Count = 0; }
            if (window.Count >= RequestsPerSecond) return RuleResult.Reject("rate_limited");
            window.Count++;
            if (identity != sessionId) return RuleResult.Reject("wrong_session");
            if (generation != Generation) return RuleResult.Reject("wrong_generation");
            return RuleResult.Accept();
        }
    }
}
