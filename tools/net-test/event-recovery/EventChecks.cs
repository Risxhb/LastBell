using System;
using System.Collections.Generic;
using LastBell.Network.Rules;

namespace LastBell.Network.EventRecovery
{
    public sealed class EventCheckReport
    {
        public int Assertions;
        public string[] Checks, Failures;
    }
    public static class EventChecks
    {
        private static readonly Guid Session = new Guid("01020304-0506-0708-090a-0b0c0d0e0f10");
        private static readonly List<string> checks = new List<string>(), failures = new List<string>();
        private static int assertions;
        private static void Check(bool value, string label) { assertions++; if (!value) failures.Add(label); }
        private static bool Throws(Action action)
        { try { action(); return false; } catch (ArgumentException) { return true; } catch (InvalidOperationException) { return true; } }
        private static void Scenario(string name, Action action)
        {
            int before = failures.Count;
            try { action(); } catch (Exception ex) { failures.Add(name + ": " + ex); }
            if (failures.Count == before) checks.Add(name);
        }
        private static EventRecord Record(long id, int generation = 1, byte value = 1)
        { return new EventRecord(Session, generation, id, id, 0, "p", new[] { value }); }
        private static byte[] Batch(EventRecord[] events, long watermark, ulong recipient = 501, int generation = 1)
        { return EventWire.Encode(new EventFrame(FrameKind.Batch, recipient, Session, generation, watermark, events)); }
        private static byte[] Ack(long watermark, ulong connection = 501, int generation = 1, bool checkpoint = false, long tick = 0)
        { return EventWire.Encode(new EventFrame(checkpoint ? FrameKind.CheckpointAck : FrameKind.Ack, connection, Session, generation, watermark, snapshotTick: tick)); }
        private static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
        private static RuleResult Receive(EventReceiver receiver, byte[] bytes, Action<EventRecord> apply = null,
            Func<byte[], long, long, bool> install = null, ulong host = 0)
        { return receiver.Receive(bytes, host, apply ?? (_ => { }), install ?? ((_, __, ___) => true)); }
        public static EventCheckReport Run()
        {
            assertions = 0; checks.Clear(); failures.Clear();
            Scenario("fixed-v1-vectors-and-maximum-roundtrip", Codec);
            Scenario("truncation-length-utf8-and-semantic-validation", Malformed);
            Scenario("authenticated-source-recipient-session-generation", Scope);
            Scenario("gap-replay-ordering-dedup-and-conflict", Gaps);
            Scenario("application-failure-keeps-cursor-and-idempotent-retry", ApplicationFailure);
            Scenario("chunked-send-rejects-unsent-acknowledgment", UnsentAck);
            Scenario("lost-bootstrap-checkpoint-and-ack-retry", Bootstrap);
            Scenario("checkpoint-validation-rollback-duplicates-and-skipped-feedback", Checkpoints);
            Scenario("reconnect-and-reset-reject-old-traffic", Lifecycle);
            Scenario("history-overflow-recovers-current-warning-phase-state", Overflow);
            Scenario("bounded-random-parser-inputs", RandomInput);
            return new EventCheckReport { Assertions = assertions, Checks = checks.ToArray(), Failures = failures.ToArray() };
        }
        private static void Codec()
        {
            string common = "f5010000000000000403020106050807090a0b0c0d0e0f1001000000";
            byte[] ack = Ack(7);
            Check(Hex(ack) == "45560101" + common + "0700000000000000", "fixed 40-byte ack");
            byte[] batch = Batch(new[] { new EventRecord(Session, 1, 1, 2, 0, "p", new byte[] { 1, 2 }) }, 1);
            Check(Hex(batch) == "45560100" + common + "0100000000000000" + "01" +
                "01000000000000000200000000000000000000000000000001007002000102", "fixed one-record batch");
            byte[] checkpoint = EventWire.Encode(new EventFrame(FrameKind.Checkpoint, 501, Session, 1, 7, snapshotTick: 8, checkpoint: new byte[] { 42 }));
            Check(Hex(checkpoint) == "45560102" + common + "0700000000000000080000000000000001002a", "fixed checkpoint");
            Check(Hex(Ack(7, checkpoint: true, tick: 8)) == "45560103" + common + "07000000000000000800000000000000", "fixed checkpoint ack");
            var records = new EventRecord[8];
            for (int i = 0; i < records.Length; i++) records[i] = new EventRecord(Session, 1, i + 1, i, 0, new string('\u754c', 64), new byte[4096]);
            byte[] maximum = Batch(records, 8); EventFrame decoded;
            Check(maximum.Length <= EventWire.MaxPacketBytes && EventWire.TryDecode(maximum, out decoded), "maximum bounded batch");
            Check(EventWire.TryDecode(maximum, out decoded) && decoded.Events.Length == 8 && decoded.Events[7].SameContent(records[7]), "maximum batch content");
            Check(Throws(() => Batch(new EventRecord[9], 9)), "encode count bound");
            Check(Throws(() => Batch(new[] { new EventRecord(Session, 1, 1, 0, 0, "\ud800", new byte[0]) }, 1)), "encode rejects unpaired surrogate");
            Check(Throws(() => Batch(new[] { Record(1), Record(1, value: 2) }, 1)), "encode conflicting duplicate");
            byte[] largestState = EventWire.Encode(new EventFrame(FrameKind.Checkpoint, 501, Session, 1, 1, checkpoint: new byte[4096]));
            Check(largestState.Length == 4146 && EventWire.TryDecode(largestState, out decoded) && decoded.State.Length == 4096, "bounded maximum checkpoint");
            Check(Throws(() => EventWire.Encode(new EventFrame(FrameKind.Checkpoint, 501, Session, 1, 1, checkpoint: new byte[4097]))), "oversized checkpoint rejected");
        }
        private static void Malformed()
        {
            byte[] valid = Batch(new[] { Record(1) }, 1); EventFrame frame;
            for (int length = 0; length < valid.Length; length++)
            { var truncated = new byte[length]; Array.Copy(valid, truncated, length); Check(!EventWire.TryDecode(truncated, out frame) && frame == null, "truncation rejected"); }
            var suffix = new byte[valid.Length + 1]; Array.Copy(valid, suffix, valid.Length);
            Check(!EventWire.TryDecode(suffix, out frame), "trailing bytes rejected");
            Check(!EventWire.TryDecode(new byte[EventWire.MaxPacketBytes + 1], out frame), "oversized allocation guard");
            foreach (int offset in new[] { 0, 1, 2, 3, 40 })
            { byte[] bytes = (byte[])valid.Clone(); bytes[offset] = 255; Check(!EventWire.TryDecode(bytes, out frame), "invalid header/count"); }
            byte[] negative = (byte[])valid.Clone(); negative[31] = 255;
            Check(!EventWire.TryDecode(negative, out frame), "negative generation");
            negative = (byte[])valid.Clone(); negative[39] = 255;
            Check(!EventWire.TryDecode(negative, out frame), "negative watermark");
            byte[] empty = (byte[])valid.Clone(); Array.Clear(empty, 12, 16);
            Check(!EventWire.TryDecode(empty, out frame), "empty session");
            byte[] bad = (byte[])valid.Clone(); bad[67] = 255;
            Check(!EventWire.TryDecode(bad, out frame), "invalid UTF-8");
            bad = (byte[])valid.Clone(); bad[65] = 255; bad[66] = 255;
            Check(!EventWire.TryDecode(bad, out frame), "oversized kind length");
            bad = (byte[])valid.Clone(); bad[68] = 255; bad[69] = 255;
            Check(!EventWire.TryDecode(bad, out frame), "oversized payload length");
            bad = (byte[])valid.Clone(); bad[41] = 2;
            Check(!EventWire.TryDecode(bad, out frame), "event beyond advertised watermark");
            var receiver = new EventReceiver(Session, 501); int applied = 0;
            Check(!Receive(receiver, bad, _ => applied++).Ok && receiver.Inbox.LastApplied == 0 && receiver.Inbox.BufferedCount == 0 && applied == 0, "malformed bytes cannot mutate inbox");
            Receive(receiver, EventWire.Encode(new EventFrame(FrameKind.Checkpoint, 501, Session, 1, 0)));
            byte[] partial = Batch(new[] { Record(1), Record(2) }, 2); Array.Resize(ref partial, partial.Length - 1);
            Check(!Receive(receiver, partial, _ => applied++).Ok && applied == 0 && receiver.Inbox.LastApplied == 0,
                "invalid second record cannot partially apply valid first record");
        }
        private static void Scope()
        {
            byte[] packet = Batch(new[] { Record(1) }, 1); var receiver = new EventReceiver(Session, 501);
            Check(Receive(receiver, packet, host: 9).Reason == "unauthorized_host", "source metadata checked");
            Check(Receive(new EventReceiver(Session, 502), packet).Reason == "wrong_recipient", "recipient incarnation checked");
            Check(Receive(new EventReceiver(Guid.NewGuid(), 501), packet).Reason == "wrong_session", "session checked");
            Check(Receive(new EventReceiver(Session, 501, 2), packet).Reason == "wrong_generation", "generation checked");
            Check(receiver.Inbox.LastApplied == 0 && receiver.Inbox.BufferedCount == 0, "scope failures preserve cursor");
            Check(Receive(receiver, packet).Reason == "bootstrap_required", "fresh client cannot apply tail without checkpoint");
        }
        private static void Gaps()
        {
            var receiver = new EventReceiver(Session, 501); var applied = new List<long>();
            Receive(receiver, EventWire.Encode(new EventFrame(FrameKind.Checkpoint, 501, Session, 1, 0)));
            Check(Receive(receiver, Batch(new[] { Record(3), Record(2) }, 3), r => applied.Add(r.EventId)).Ok, "out of order batch accepted");
            Check(receiver.Inbox.LastApplied == 0 && receiver.Inbox.NeedsReplay && receiver.Inbox.BufferedCount == 2, "gap withholds application/ack");
            Check(Receive(receiver, Batch(new[] { Record(1), Record(2, value: 2) }, 3)).Reason == "conflicting_event", "pending conflict rejects whole batch");
            Check(receiver.Inbox.BufferedCount == 2 && receiver.Inbox.LastApplied == 0, "conflict is atomic");
            Check(Receive(receiver, Batch(new[] { Record(1) }, 3), r => applied.Add(r.EventId)).Ok, "missing record repairs gap");
            Check(string.Join(",", applied) == "1,2,3" && receiver.Inbox.LastApplied == 3, "ordered contiguous application");
            Check(Receive(receiver, Batch(new[] { Record(1), Record(2), Record(3) }, 3), r => applied.Add(r.EventId)).Ok && applied.Count == 3, "duplicates never replay effects");
        }
        private static void ApplicationFailure()
        {
            var receiver = new EventReceiver(Session, 501); byte[] packet = Batch(new[] { Record(1) }, 1);
            Receive(receiver, EventWire.Encode(new EventFrame(FrameKind.Checkpoint, 501, Session, 1, 0)));
            var sink = new HashSet<long>(); int attempts = 0;
            Check(Throws(() => Receive(receiver, packet, r => { sink.Add(r.EventId); attempts++; throw new InvalidOperationException("injected failure after idempotent effect"); })), "application exception visible");
            EventFrame ack; EventWire.TryDecode(receiver.BuildAck(), out ack);
            Check(receiver.Inbox.LastApplied == 0 && ack.Watermark == 0 && receiver.Inbox.BufferedCount == 1, "failure not acknowledged");
            Check(Receive(receiver, packet, r => { sink.Add(r.EventId); attempts++; }).Ok && attempts == 2 && sink.Count == 1,
                "idempotent sink retries without duplicate effect");
        }
        private static void UnsentAck()
        {
            var sender = new EventSender(Session); sender.Register(501);
            sender.BuildCheckpoint(501, 0, 0, new byte[0]); Check(sender.ReceiveAck(Ack(0, checkpoint: true), 501).Ok, "bootstrap ack");
            for (int i = 0; i < 20; i++) sender.Append(i, "p", new byte[0]);
            byte[] packet = sender.BuildBatch(501); EventFrame frame; EventWire.TryDecode(packet, out frame);
            Check(frame.Events.Length == 8 && frame.Watermark == 20, "chunk differs from advertised stream head");
            Check(sender.ReceiveAck(Ack(20), 501).Reason == "unsent_event_ack", "cannot ack unsent tail");
            Check(sender.ReceiveAck(Ack(8), 501).Ok, "actual sent prefix may ack");
            EventWire.TryDecode(sender.BuildBatch(501), out frame); Check(frame.Events[0].EventId == 9, "remaining tail retained");
            Check(sender.ReceiveAck(Ack(8, 502), 501).Reason == "wrong_incarnation", "ack connection spoof rejected");
            Check(sender.ReceiveAck(Ack(8), 502).Reason == "unknown_sender", "unregistered source rejected");
        }
        private static void Bootstrap()
        {
            var sender = new EventSender(Session); sender.Register(501);
            Check(sender.BuildBatch(501) == null, "new subscriber needs checkpoint");
            byte[] first = sender.BuildCheckpoint(501, 0, 10, new byte[] { 1 });
            Check(sender.BuildBatch(501) == null, "sending is not proof of checkpoint installation");
            Check(sender.ReceiveAck(Ack(0), 501).Reason == "checkpoint_ack_required", "ordinary cursor ack cannot confirm bootstrap");
            sender.Append(11, "p", new byte[0]);
            Check(Hex(sender.BuildCheckpoint(501, 1, 11, new byte[] { 2 })) == Hex(first), "lost checkpoint retries stable frozen snapshot");
            Check(sender.ReceiveAck(Ack(0, checkpoint: true, tick: 9), 501).Reason == "checkpoint_ack_required", "wrong snapshot identity rejected");
            Check(sender.ReceiveAck(Ack(0, checkpoint: true, tick: 10), 501).Ok, "matching checkpoint ack enables tail");
            EventFrame frame; EventWire.TryDecode(sender.BuildBatch(501), out frame);
            Check(frame.Events.Length == 1 && frame.Events[0].EventId == 1, "post-checkpoint tail preserved");
        }
        private static void Checkpoints()
        {
            var receiver = new EventReceiver(Session, 501); var state = new Projection(); int installs = 0;
            var captured = new Projection { Feedback = 5, Phase = 2, WarningUntil = 100 };
            byte[] packet = EventWire.Encode(new EventFrame(FrameKind.Checkpoint, 501, Session, 1, 10, snapshotTick: 20, checkpoint: captured.Capture()));
            Func<byte[], long, long, bool> install = (bytes, wm, tick) => { installs++; return state.Install(bytes, wm, tick); };
            Check(Receive(receiver, packet, install: install).Ok && state.Same(captured) && receiver.Inbox.LastApplied == 10, "full diagnostic state installed with cursor");
            Check(Receive(receiver, packet, install: install).Ok && installs == 1, "duplicate checkpoint does not replay install/effects");
            byte[] conflict = (byte[])packet.Clone(); conflict[50]++;
            Check(Receive(receiver, conflict, install: install).Reason == "conflicting_checkpoint" && installs == 1, "same checkpoint identity cannot change content");
            byte[] invalid = EventWire.Encode(new EventFrame(FrameKind.Checkpoint, 501, Session, 1, 20, snapshotTick: 30, checkpoint: new byte[3]));
            Check(Receive(receiver, invalid, install: install).Reason == "invalid_checkpoint_state" && receiver.Inbox.LastApplied == 10 && state.Same(captured), "invalid domain state leaves cursor/projection intact");
            byte[] stale = EventWire.Encode(new EventFrame(FrameKind.Checkpoint, 501, Session, 1, 9, snapshotTick: 40, checkpoint: captured.Capture()));
            Check(Receive(receiver, stale, install: install).Reason == "stale_checkpoint", "older watermark cannot roll back");
            int effects = 0; Check(Receive(receiver, Batch(new[] { Record(1), Record(5) }, 10), _ => effects++).Ok && effects == 0, "checkpoint suppresses historical effects");
            Check(state.WarningVisible(99) && !state.WarningVisible(100), "recovered warning expires at authoritative deadline");
        }
        private static void Lifecycle()
        {
            var sender = new EventSender(Session); sender.Register(501); sender.Remove(501); sender.Register(502);
            Check(sender.ReceiveAck(Ack(0), 501).Reason == "unknown_sender", "retired ack source rejected");
            Check(Throws(() => sender.Register(501)), "connection incarnation cannot be reused");
            var current = new EventReceiver(Session, 502);
            Check(Receive(current, Batch(new[] { Record(1) }, 1)).Reason == "wrong_recipient", "old endpoint frame rejected");
            sender.Reset(2); current.Reset(2);
            Check(Receive(current, Batch(new[] { Record(1) }, 1, 502)).Reason == "wrong_generation", "old event generation rejected");
            Check(sender.ReceiveAck(Ack(0, 502), 502).Reason == "wrong_generation", "old ack generation rejected");
            Check(current.Inbox.LastApplied == 0 && current.Inbox.BufferedCount == 0, "reset cursor remains clean");
        }
        private static void Overflow()
        {
            var sender = new EventSender(Session); sender.Register(501); var receiver = new EventReceiver(Session, 501);
            var host = new Projection(); var local = new Projection();
            byte[] initial = sender.BuildCheckpoint(501, 0, 0, host.Capture()); Receive(receiver, initial, install: local.Install);
            sender.ReceiveAck(receiver.BuildAck(), 501);
            for (int i = 1; i <= 150; i++)
            { EventRecord record = sender.Append(i, "sample_feedback", BitConverter.GetBytes((long)i)); host.Apply(record); }
            host.Phase = 3; host.WarningUntil = 200;
            Check(sender.BuildBatch(501) == null, "journal overflow requires checkpoint instead of incomplete replay");
            byte[] checkpoint = sender.BuildCheckpoint(501, 150, 150, host.Capture());
            Check(Receive(receiver, checkpoint, install: local.Install).Ok && local.Same(host) && receiver.Inbox.LastApplied == 150, "current phase/warning restored together");
            Check(sender.ReceiveAck(receiver.BuildAck(), 501).Ok, "installed checkpoint acknowledged");
            EventRecord next = sender.Append(151, "sample_feedback", BitConverter.GetBytes(151L)); host.Apply(next); int feedback = 0;
            Check(Receive(receiver, sender.BuildBatch(501), r => { local.Apply(r); feedback++; }, local.Install).Ok && local.Same(host) && feedback == 1,
                "tail feedback resumes once without replaying 150 historical effects");
        }
        private static void RandomInput()
        {
            uint random = 77291; EventFrame frame;
            for (int i = 0; i < 2048; i++)
            {
                random ^= random << 13; random ^= random >> 17; random ^= random << 5;
                var bytes = new byte[random % 1024];
                for (int j = 0; j < bytes.Length; j++) { random ^= random << 13; random ^= random >> 17; random ^= random << 5; bytes[j] = (byte)random; }
                Check(!EventWire.TryDecode(bytes, out frame) && frame == null, "arbitrary bounded input rejected without throw");
            }
            // Keep the valid header so mutations exercise inner record parsing too.
            byte[] seed = Batch(new[] { Record(1), Record(2) }, 2);
            for (int i = 0; i < 1024; i++)
            {
                random ^= random << 13; random ^= random >> 17; random ^= random << 5;
                byte[] bytes = (byte[])seed.Clone(); int offset = 40 + (int)(random % (bytes.Length - 40));
                random ^= random << 13; random ^= random >> 17; random ^= random << 5; bytes[offset] = (byte)random;
                bool decoded = EventWire.TryDecode(bytes, out frame);
                Check(decoded ? Hex(EventWire.Encode(frame)) == Hex(bytes) : frame == null, "structured mutation is rejected or canonically roundtrips");
            }
        }
    }
}
