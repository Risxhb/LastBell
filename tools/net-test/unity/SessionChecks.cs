using System;
using System.Collections.Generic;
using System.Text;
using LastBell.Network.Rules;

namespace LastBell.Network.Checks
{
    public sealed class CheckReport
    {
        public int Assertions;
        public string[] PassedScenarios;
        public string[] Failures;
    }
    public static class SessionChecks
    {
        private static int assertions;
        private static readonly List<string> failures = new List<string>();
        private static readonly List<string> scenarios = new List<string>();
        private static readonly Guid Identity = new Guid("01020304-0506-0708-090a-0b0c0d0e0f10");
        private static void Expect(bool condition, string label)
        { assertions++; if (!condition) failures.Add(label); }
        private static void Scenario(string name, Action test)
        {
            int before = failures.Count;
            try { test(); } catch (Exception ex) { failures.Add(name + ": " + ex); }
            if (failures.Count == before) scenarios.Add(name);
        }
        private static ControlRequest Control(SessionRules s, ControlKind kind, string classId = "", bool ready = false)
        { return new ControlRequest(s.SessionId, s.Generation, kind, classId, ready); }
        private static InputRequest Input(SessionRules s, long seq, long tick = -1,
            double x = 1, double y = 1, bool technique = true)
        { return new InputRequest(s.SessionId, s.Generation, seq, tick == -1 ? s.Tick + 1 : tick,
            new PlanarMove(x, y), true, technique, false, true); }
        private static SessionRules Running()
        {
            var s = new SessionRules(Identity, 0);
            s.Join(501);
            s.Control(0, Control(s, ControlKind.Ready, ready: true), 0);
            s.Control(501, Control(s, ControlKind.Ready, ready: true), 0);
            Expect(s.Control(0, Control(s, ControlKind.Start), 0).Ok, "setup start");
            return s;
        }
        public static CheckReport Run()
        {
            assertions = 0; failures.Clear(); scenarios.Clear();
            Scenario("session-validation", SessionValidation);
            Scenario("sequence-press-and-disconnect", InputConsumption);
            Scenario("wire-codec-and-golden-vector", Codec);
            Scenario("rate-boundary", RateBoundary);
            Scenario("event-duplicate-gap-ack", EventRecovery);
            Scenario("event-checkpoint-generation-and-scope", EventCheckpoint);
            Scenario("event-batch-and-consumer-failure", EventFailure);
            for (int n = 2; n <= 8; n++)
            { int count = n; Scenario("in-memory-" + count + "-participants", () => Capacity(count)); }
            return new CheckReport { Assertions = assertions, PassedScenarios = scenarios.ToArray(), Failures = failures.ToArray() };
        }
        private static void SessionValidation()
        {
            var s = new SessionRules(Identity, 0);
            Expect(!s.Join(0).Ok, "duplicate host");
            for (ulong peer = 101; peer <= 107; peer++) Expect(s.Join(peer).Ok, "join " + peer);
            Expect(s.Snapshot().Players.Length == 8, "includes host in capacity");
            Expect(s.Join(108).Reason == "session_full", "ninth rejection");
            Expect(s.Control(999, Control(s, ControlKind.Ready, ready: true), 0).Reason == "unknown_sender", "sender authentication");
            Expect(s.Control(101, Control(s, ControlKind.Start), 0).Reason == "host_only", "host start ownership");
            Expect(s.Control(0, Control(s, ControlKind.Start), 0).Reason == "players_not_ready", "ready gate");
            Expect(s.Control(0, Control(s, (ControlKind)999), 0).Reason == "unsupported_command", "unknown control");
            Expect(s.Control(101, Control(s, ControlKind.SelectClass, "arcanist"), 0).Reason == "invalid_class", "class allowlist");
            Expect(s.Control(101, null, 0).Reason == "null_request", "null control");
            foreach (ulong peer in new ulong[] {0,101,102,103,104,105,106,107})
            {
                Expect(s.Control(peer, Control(s, ControlKind.SelectClass, "vanguard"), 0).Ok, "duplicate class");
                Expect(s.Control(peer, Control(s, ControlKind.Ready, ready: true), 0).Ok, "ready");
            }
            var old = Input(s, 1);
            Expect(s.Control(0, Control(s, ControlKind.Start), 0).Ok, "start");
            Expect(s.Generation == 1 && s.Tick == 0, "generation/tick start");
            Expect(s.SubmitInput(101, old, 0).Reason == "wrong_generation", "old input rejected");
            Expect(s.Join(108).Reason == "encounter_in_progress", "late join");
            Expect(s.Control(101, Control(s, ControlKind.Ready), 0).Reason == "encounter_in_progress", "ready frozen");
            Expect(s.SubmitInput(101, new InputRequest(Guid.NewGuid(), 1, 1, 1, new PlanarMove(0,0), false,false,false,false), 0).Reason == "wrong_session", "session scope");
            Expect(s.SubmitInput(101, null, 0).Reason == "null_request", "null input");
            Expect(s.SubmitInput(101, Input(s, 1, x: double.NaN), 0).Reason == "invalid_movement", "NaN");
            Expect(s.SubmitInput(101, Input(s, 1, x: double.PositiveInfinity), 0).Reason == "invalid_movement", "infinite");
            Expect(s.SubmitInput(101, Input(s, 1, tick: 13), 0).Reason == "tick_out_of_bounds", "future bound");
            Expect(s.SubmitInput(101, Input(s, 1, tick: -7), 0).Reason == "tick_out_of_bounds", "past bound");
            Expect(s.SubmitInput(101, Input(s, 1025), 0).Reason == "sequence_jump", "sequence jump");
            Expect(s.SubmitInput(101, Input(s, 0), 0).Reason == "stale_sequence", "nonpositive sequence");
            var detached = s.Snapshot();
            detached.Players[0] = null;
            detached.ProcessedSequences[1] = 100;
            Expect(s.Snapshot().Players[0] != null && s.Snapshot().ProcessedSequences[1] == 0, "detached snapshot");
            s.Disconnect(101);
            // During a fight slots do not reopen; reconnect is an encounter-boundary follow-up.
            Expect(s.Snapshot().Players.Length == 7 && !s.Join(109).Ok, "disconnect does not enable midfight join");
            s.Disconnect(0);
            Expect(s.Phase == SessionPhase.Closed && s.Advance().Count == 0, "host closes clock");
            Expect(s.SubmitInput(102, Input(s, 1), 1000).Reason == "session_closed", "closed session stops input");
        }
        private static void InputConsumption()
        {
            var s = Running();
            Expect(s.SubmitInput(501, Input(s, 1), 0).Ok, "accept sample");
            Expect(s.SubmitInput(501, Input(s, 1), 0).Reason == "stale_sequence", "repeat packet cannot replay press");
            Expect(s.Snapshot().ProcessedSequences[2] == 0, "ack only after consumption");
            var commands = s.Advance();
            Expect(commands.ContainsKey(2) && !commands.ContainsKey(1), "authenticated ID not peer ID");
            Expect(commands[2].Guard && commands[2].Technique, "press received");
            Expect(Math.Abs(commands[2].Move.X * commands[2].Move.X + commands[2].Move.Y * commands[2].Move.Y - 1) < 1e-12, "clamp diagonal");
            Expect(s.Snapshot().ProcessedSequences[2] == 1 && s.Advance().Count == 0, "once then neutral");
            Expect(s.SubmitInput(501, Input(s, 2, technique: true), 1000).Ok, "first same tick");
            Expect(s.SubmitInput(501, Input(s, 3, x: 0, y: 0, technique: false), 1000).Ok, "coalesced same tick");
            commands = s.Advance();
            Expect(commands[2].Technique && commands[2].Move.X == 0, "keep press use newest movement");
            Expect(s.Snapshot().ProcessedSequences[2] == 3, "newest processed ack");
            Expect(s.SubmitInput(501, Input(s, 4, tick: s.Tick + 5), 1000).Ok, "queue future");
            Expect(s.SubmitInput(501, Input(s, 5), 1000).Reason == "target_tick_regression", "no ack rewind");
            for (int i = 0; i < 5; i++) s.Advance();
            Expect(s.Snapshot().ProcessedSequences[2] == 4, "future sample consumed");
            Expect(s.SubmitInput(501, Input(s, 5, x: double.MaxValue, y: double.MaxValue), 2000).Ok, "huge finite normalized");
            commands = s.Advance();
            Expect(commands[2].Move.IsFinite && Math.Abs(commands[2].Move.X - Math.Sqrt(0.5)) < 1e-12, "normalization does not overflow");
            s.SubmitInput(501, Input(s, 6), 2000); s.Disconnect(501);
            Expect(s.Advance().Count == 0, "disconnect clears queued commands");
            var slots = new SessionRules(Identity, 0);
            Expect(slots.Join(55).PlayerId == 2, "peer separate from stable id");
            slots.Disconnect(55);
            Expect(slots.Join(56).PlayerId == 3 && slots.Snapshot().Players[1].Slot == 2, "slot reused identity not reused");
        }
        private static void Codec()
        {
            var input = new InputRequest(Identity, 1, 1, 2, new PlanarMove(0.5, -0.25), true, true, false, false);
            byte[] bytes = InputCodec.Encode(input);
            string hex = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            Expect(hex == "010403020106050807090a0b0c0d0e0f100100000001000000000000000200000000000000000000000000e03f000000000000d0bf03", "fixed v1 golden vector");
            InputRequest decoded; string reason;
            Expect(InputCodec.TryDecode(bytes, out decoded, out reason) && decoded.Move.X == 0.5 && decoded.Move.Y == -0.25 && decoded.Technique, "wire round trip");
            Expect(!InputCodec.TryDecode(null, out decoded, out reason), "null payload");
            Expect(!InputCodec.TryDecode(new byte[53], out decoded, out reason), "short payload");
            Expect(!InputCodec.TryDecode(new byte[55], out decoded, out reason), "extra authoritative fields cannot fit");
            var wrong = (byte[])bytes.Clone(); wrong[0] = 255;
            Expect(!InputCodec.TryDecode(wrong, out decoded, out reason) && reason == "invalid_version", "version mismatch");
            wrong = (byte[])bytes.Clone(); wrong[53] = 128;
            Expect(!InputCodec.TryDecode(wrong, out decoded, out reason) && reason == "invalid_flags", "flags are typed/bounded");
            wrong = (byte[])bytes.Clone(); Array.Copy(BitConverter.GetBytes(double.NaN), 0, wrong, 37, 8);
            Expect(!InputCodec.TryDecode(wrong, out decoded, out reason) && reason == "invalid_movement", "wire nonfinite");
        }
        private static void RateBoundary()
        {
            var s = new SessionRules(Identity, 0);
            for (int i = 0; i < 120; i++) Expect(s.Control(0, Control(s, ControlKind.Sync), 0).Ok, "rate budget");
            Expect(s.Control(0, Control(s, ControlKind.Sync), 999).Reason == "rate_limited", "120 ceiling");
            Expect(s.Control(0, Control(s, ControlKind.Sync), 1000).Ok, "new window");
            Expect(s.Control(0, Control(s, ControlKind.Sync), 999).Reason == "invalid_clock", "monotonic clock");
        }
        private static byte[] Payload() { return Encoding.UTF8.GetBytes("{\"outcome\":\"guarded\",\"mechanic_failed\":true}"); }
        private static void EventRecovery()
        {
            var journal = new EventJournal(Identity, retained: 3);
            journal.RegisterPeer(55);
            var a = journal.Append(1, 2, "mechanic_resolved", Payload());
            var b = journal.Append(2, 2, "mechanic_resolved", Payload());
            var c = journal.Append(3, 2, "mechanic_resolved", Payload());
            Expect(journal.Acknowledge(55, Identity, 0, 3).Reason == "invalid_event_ack", "cannot ack unsent events");
            var replay = journal.ReplayFor(55);
            Expect(replay.Records.Length == 3 && !replay.NeedsCheckpoint, "initial replay");
            var inbox = new EventInbox(Identity, buffered: 3);
            inbox.ObserveWatermark(Identity, 0, replay.Watermark);
            Expect(inbox.Offer(new[] {c, b}).Ok && inbox.NeedsReplay, "reorder gap detected");
            Expect(!inbox.ApplyNext(_ => { }) && inbox.LastApplied == 0, "never acknowledge a gap");
            Expect(inbox.Offer(new[] {a, a}).Ok, "duplicate buffered event");
            var ids = new List<long>();
            while (inbox.ApplyNext(e => ids.Add(e.EventId))) { }
            Expect(ids.Count == 3 && ids[0] == 1 && ids[2] == 3, "contiguous ordered delivery");
            Expect(inbox.LastApplied == 3 && !inbox.NeedsReplay, "applied watermark");
            Expect(inbox.Offer(new[] {b, c}).Ok && !inbox.ApplyNext(_ => ids.Add(99)), "feedback deduplicated");
            Expect(journal.Acknowledge(55, Identity, 0, inbox.LastApplied).Ok, "ack applied");
            Expect(journal.ReplayFor(55).Records.Length == 0, "acked events not resent");
            Expect(journal.Acknowledge(55, Identity, 0, 2).Reason == "invalid_event_ack", "ack regression rejected");
            byte[] payload = a.Payload; payload[0] = 0;
            Expect(Encoding.UTF8.GetString(a.Payload).Contains("\"mechanic_failed\":true"), "immutable payload preserves failure distinct from prevented damage");
        }
        private static void EventCheckpoint()
        {
            var journal = new EventJournal(Identity, retained: 2);
            journal.RegisterPeer(55);
            for (int i = 0; i < 3; i++) journal.Append(i, 2, "resolved", Payload());
            var replay = journal.ReplayFor(55);
            Expect(replay.NeedsCheckpoint && replay.Records.Length == 0, "history overflow requires explicit full state");
            var inbox = new EventInbox(Identity, buffered: 2);
            Expect(!inbox.Offer(new[] {new EventRecord(Identity, 0, 3, 3, 2, "resolved", Payload())}).Ok && inbox.NeedsCheckpoint, "bounded receiver overflow");
            journal.MarkCheckpointSent(55, replay.Watermark);
            Expect(inbox.InstallCheckpoint(Identity, 0, replay.Watermark).Ok && inbox.LastApplied == 3, "checkpoint watermark clears pending");
            Expect(journal.Acknowledge(55, Identity, 0, 3).Ok, "checkpoint ack");
            journal.Append(4, 2, "resolved", Payload());
            Expect(inbox.Offer(journal.ReplayFor(55).Records).Ok && inbox.ApplyNext(_ => { }) && inbox.LastApplied == 4, "resume after checkpoint");
            journal.BeginGeneration(1);
            Expect(journal.Acknowledge(55, Identity, 0, 4).Reason == "wrong_generation", "old generation ack rejected");
            Expect(!inbox.Offer(new[] {journal.Append(1, 2, "resolved", Payload())}).Ok, "generation cannot switch by event packet");
            Expect(inbox.InstallCheckpoint(Identity, 1, 0).Ok && inbox.LastApplied == 0, "trusted reset changes generation");
            Expect(inbox.Offer(journal.ReplayFor(55).Records).Ok && inbox.ApplyNext(_ => { }), "event id reuse in new generation");
            Expect(inbox.InstallCheckpoint(Identity, 0, 5).Reason == "wrong_generation", "stale checkpoint");
            Expect(inbox.InstallCheckpoint(Guid.NewGuid(), 1, 0).Reason == "wrong_session", "checkpoint session scope");
            Expect(journal.Acknowledge(66, Identity, 1, 0).Reason == "unknown_sender", "ack sender authentication");
            journal.RemovePeer(55);
            Expect(journal.Acknowledge(55, Identity, 1, 0).Reason == "unknown_sender", "disconnected ack rejected");
        }
        private static void EventFailure()
        {
            var inbox = new EventInbox(Identity, buffered: 3);
            Expect(!inbox.Offer(null).Ok && inbox.BufferedCount == 0, "null batch");
            Expect(!inbox.Offer(new EventRecord[4]).Ok && inbox.BufferedCount == 0, "oversize batch");
            Expect(!inbox.Offer(new EventRecord[] {null}).Ok && inbox.BufferedCount == 0, "null event");
            var first = new EventRecord(Identity, 0, 1, 1, 2, "resolved", Payload());
            var conflicting = new EventRecord(Identity, 0, 1, 1, 2, "different", Payload());
            Expect(inbox.Offer(new[] { first, conflicting }).Reason == "conflicting_event" && inbox.BufferedCount == 0, "atomic bad batch");
            var wrongScope = new EventRecord(Guid.NewGuid(), 0, 2, 2, 2, "resolved", Payload());
            Expect(!inbox.Offer(new[] {first, wrongScope}).Ok && inbox.BufferedCount == 0, "scope failure no prefix mutation");
            Expect(inbox.Offer(new[] {first}).Ok, "valid event buffered");
            Expect(inbox.LastApplied == 0, "receipt is not application ack");
            try { inbox.ApplyNext(_ => { throw new InvalidOperationException("consumer failed"); }); } catch (InvalidOperationException) { }
            Expect(inbox.LastApplied == 0 && inbox.BufferedCount == 1, "failed application retained without ack");
            Expect(inbox.ApplyNext(_ => { }) && inbox.LastApplied == 1, "retry after application failure");
            inbox.ObserveWatermark(Identity, 0, 2);
            Expect(inbox.NeedsReplay, "snapshot watermark exposes dropped tail batch");
        }
        private static void Capacity(int count)
        {
            var s = new SessionRules(Identity, 0);
            var peers = new List<ulong> { 0 };
            for (int i = 1; i < count; i++) { peers.Add((ulong)(500 + i)); Expect(s.Join(peers[i]).Ok, "capacity join"); }
            Expect(!s.Control(0, Control(s, ControlKind.Start), 0).Ok, "unready capacity start");
            foreach (ulong peer in peers)
            {
                Expect(s.Control(peer, Control(s, ControlKind.SelectClass, "vanguard"), 0).Ok, "capacity duplicate");
                s.Control(peer, Control(s, ControlKind.Ready, ready: true), 0);
            }
            Expect(s.Control(0, Control(s, ControlKind.Start), 0).Ok, "capacity start");
            for (int step = 1; step <= 300; step++)
            {
                foreach (ulong peer in peers)
                {
                    byte[] wire = InputCodec.Encode(Input(s, step, technique: step % 17 == 0));
                    InputRequest decoded; string reason;
                    Expect(InputCodec.TryDecode(wire, out decoded, out reason), "capacity codec");
                    Expect(s.SubmitInput(peer, decoded, step * 17L).Ok, "capacity owned input");
                    if (step % 17 == 0) Expect(s.SubmitInput(peer, decoded, step * 17L).Reason == "stale_sequence", "capacity retransmission");
                }
                var commands = s.Advance();
                Expect(commands.Count == count && s.Tick == step, "capacity single step all actors");
                foreach (PlayerInfo player in s.Snapshot().Players)
                    Expect(s.Snapshot().ProcessedSequences[player.PlayerId] == step, "capacity processed ack");
            }
            Expect(s.Snapshot().Players.Length == count, "capacity stable roster");
        }
    }
}
