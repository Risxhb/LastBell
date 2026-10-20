using System;
using System.Collections.Generic;

namespace LastBell.Network.Rules
{
    // Provisional session-only API. Foundation U3 owns combat schemas and clock binding.
    public enum SessionPhase { Lobby, Running, Closed }
    public enum ControlKind { SelectClass, Ready, Start, Sync }

    public sealed class RuleResult
    {
        public bool Ok { get; private set; }
        public string Reason { get; private set; }
        public long PlayerId { get; private set; }
        private RuleResult(bool ok, string reason, long playerId)
        { Ok = ok; Reason = reason; PlayerId = playerId; }
        public static RuleResult Accept(long id = 0) { return new RuleResult(true, "", id); }
        public static RuleResult Reject(string reason) { return new RuleResult(false, reason, 0); }
    }

    public sealed class ControlRequest
    {
        public readonly Guid SessionId;
        public readonly int Generation;
        public readonly ControlKind Kind;
        public readonly string ClassId;
        public readonly bool Ready;
        public ControlRequest(Guid sessionId, int generation, ControlKind kind, string classId = "", bool ready = false)
        { SessionId = sessionId; Generation = generation; Kind = kind; ClassId = classId; Ready = ready; }
    }

    public readonly struct PlanarMove
    {
        public readonly double X;
        public readonly double Y;
        public PlanarMove(double x, double y) { X = x; Y = y; }
        public bool IsFinite
        { get { return !double.IsNaN(X) && !double.IsNaN(Y) && !double.IsInfinity(X) && !double.IsInfinity(Y); } }
        public PlanarMove Clamped()
        {
            // Scale before squaring to avoid overflow on finite but huge values.
            double largest = Math.Max(Math.Abs(X), Math.Abs(Y));
            if (largest == 0) return this;
            double sx = X / largest, sy = Y / largest;
            double norm = Math.Sqrt(sx * sx + sy * sy);
            if (largest <= 1 && largest * norm <= 1) return this;
            return new PlanarMove(sx / norm, sy / norm);
        }
    }

    public sealed class InputRequest
    {
        public readonly Guid SessionId;
        public readonly int Generation;
        public readonly long Sequence;
        public readonly long TargetTick;
        public readonly PlanarMove Move;
        public readonly bool Basic, Technique, Signature, Guard;
        public InputRequest(Guid sessionId, int generation, long sequence, long targetTick,
            PlanarMove move, bool basic, bool technique, bool signature, bool guard)
        {
            SessionId = sessionId; Generation = generation; Sequence = sequence; TargetTick = targetTick;
            Move = move; Basic = basic; Technique = technique; Signature = signature; Guard = guard;
        }
    }

    public sealed class TickCommand
    {
        public readonly PlanarMove Move;
        public readonly bool Basic, Technique, Signature, Guard;
        public TickCommand(PlanarMove move, bool basic, bool technique, bool signature, bool guard)
        { Move = move; Basic = basic; Technique = technique; Signature = signature; Guard = guard; }
    }

    public sealed class PlayerInfo
    {
        public long PlayerId { get; private set; }
        public int Slot { get; private set; }
        public string ClassId { get; private set; }
        public bool Ready { get; private set; }
        public PlayerInfo(long id, int slot, string classId, bool ready)
        { PlayerId = id; Slot = slot; ClassId = classId; Ready = ready; }
    }

    public sealed class SessionSnapshot
    {
        public const int SchemaVersion = 1;
        public readonly Guid SessionId;
        public readonly int Generation;
        public readonly long Tick, Revision;
        public readonly SessionPhase Phase;
        private readonly PlayerInfo[] players;
        private readonly Dictionary<long, long> processed;
        public PlayerInfo[] Players { get { return (PlayerInfo[])players.Clone(); } }
        public Dictionary<long, long> ProcessedSequences { get { return new Dictionary<long, long>(processed); } }
        public SessionSnapshot(Guid sessionId, int generation, long tick, long revision,
            SessionPhase phase, PlayerInfo[] roster, Dictionary<long, long> acknowledgments)
        {
            SessionId = sessionId; Generation = generation; Tick = tick; Revision = revision; Phase = phase;
            players = (PlayerInfo[])roster.Clone(); processed = new Dictionary<long, long>(acknowledgments);
        }
    }

    // An adapter supplies its authenticated connection handle separately from decoded input.
    // Never deserialize peerId from a client payload. No Unity/transport type leaks here.
    public interface ISessionInputSink
    {
        RuleResult SubmitInput(ulong authenticatedPeerId, InputRequest request, long monotonicMs);
    }
}
