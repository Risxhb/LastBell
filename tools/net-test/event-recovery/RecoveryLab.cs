using System;
using System.Collections.Generic;
using System.IO;
using LastBell.Network.Rules;
using LastBell.Network.FaultLab;

namespace LastBell.Network.EventRecovery
{
    // Diagnostic projection only: no damage/geometry, boss AI, combat eligibility, roster or RNG restore.
    public sealed class Projection
    {
        public long Feedback, Phase, WarningUntil;
        public bool WarningVisible(long authoritativeTick) { return WarningUntil > authoritativeTick; }
        public void Apply(EventRecord record)
        {
            byte[] payload = record.Payload; if (payload.Length != 8) throw new ArgumentException("Diagnostic payload");
            long value = BitConverter.ToInt64(payload, 0);
            if (value < 0) throw new ArgumentException("Diagnostic value");
            switch (record.Kind)
            {
                case "sample_feedback": if (value != Feedback + 1) throw new InvalidOperationException("Feedback state gap"); Feedback = value; break;
                case "sample_phase": Phase = value; break;
                case "sample_warning": WarningUntil = value; break;
                default: throw new ArgumentException("Unknown diagnostic kind");
            }
        }
        public byte[] Capture()
        {
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
            { writer.Write(Feedback); writer.Write(Phase); writer.Write(WarningUntil); return stream.ToArray(); }
        }
        public bool Install(byte[] state, long watermark, long tick)
        {
            if (state == null || state.Length != 24 || watermark < 0 || tick < 0) return false;
            long feedback = BitConverter.ToInt64(state, 0), phase = BitConverter.ToInt64(state, 8), warning = BitConverter.ToInt64(state, 16);
            if (feedback < 0 || feedback > watermark || phase < 0 || phase > watermark || warning < 0) return false;
            Feedback = feedback; Phase = phase; WarningUntil = warning; return true;
        }
        public bool Same(Projection other)
        { return other.Feedback == Feedback && other.Phase == Phase && other.WarningUntil == WarningUntil; }
    }
    public sealed class EventCase
    {
        public string Name;
        public int Participants, RttMs, Seed;
        public double Loss;
        public long EventsIssued, FeedbackIssued, AppliedRecords, FeedbackEffects, CheckpointsInstalled, FeedbackSuppressed;
        public long BatchFrames, CheckpointFrames, AckFrames, AckRejections, StaleFramesRejected, FramesDelivered;
        public long DuplicateEffects, ProjectionConflicts, ReplayGapsObserved, Reconnects, GenerationResets;
        public long Sent, Dropped, Duplicated, Delivered, AckBlackoutDrops;
        public int PeakInbox, PeakFrameBytes, PeersAfterDrain;
        public bool CaughtUp, Passed;
        public string[] Failures;
    }
    internal sealed class Viewer
    {
        public ulong Connection;
        public EventReceiver Receiver;
        public Projection State = new Projection();
        public readonly HashSet<string> Applied = new HashSet<string>();
        public readonly HashSet<string> Feedback = new HashSet<string>();
        public long GenerationFeedbackEffects, GenerationFeedbackSuppressed;
    }
    public static class EventLab
    {
        public static EventCase[] Run()
        {
            var cases = new List<EventCase>();
            for (int count = 2; count <= 8; count++)
                foreach (int rtt in new[] { 50, 100, 150 })
                    foreach (double loss in new[] { 0.0, 0.03, 0.10 })
                        cases.Add(new Simulation("matrix-short-gap", count, rtt, loss, 31000 + count * 101 + rtt + (int)(loss * 1000)).Run());
            foreach (string probe in new[] { "history-overflow", "ack-blackout", "reconnect", "generation-reset" })
                cases.Add(new Simulation(probe, 8, 150, 0.10, 91000 + cases.Count).Run());
            return cases.ToArray();
        }
        private sealed class Simulation
        {
            private static readonly Guid Session = new Guid("cd08e2c9-8b02-45fc-a79b-15849578bfc8");
            private readonly EventSender sender = new EventSender(Session);
            private Projection authoritative = new Projection();
            private readonly FaultLink link;
            private readonly EventCase report;
            private readonly List<Viewer> viewers = new List<Viewer>();
            private readonly List<string> failures = new List<string>();
            private long tick, generationFeedback;
            private ulong nextConnection = 501;
            public Simulation(string name, int count, int rtt, double loss, int seed)
            {
                report = new EventCase { Name = name, Participants = count, RttMs = rtt, Loss = loss, Seed = seed };
                link = new FaultLink(rtt, 20, loss, (uint)seed);
                for (int i = 0; i < count; i++)
                {
                    ulong handle = nextConnection++; sender.Register(handle);
                    viewers.Add(new Viewer { Connection = handle, Receiver = new EventReceiver(Session, handle) });
                }
                if (name == "matrix-short-gap" || name == "history-overflow")
                {
                    link.BlackoutPeer = viewers[count - 1].Connection;
                    link.BlackoutStart = 4000; link.BlackoutEnd = name == "history-overflow" ? 19000 : 5500;
                }
            }
            private void Fail(string message) { if (!failures.Contains(message) && failures.Count < 25) failures.Add(message); }
            private void Produce()
            {
                long ordinal = sender.LastIssued + 1, value; string kind;
                if (ordinal % 50 == 0) { kind = "sample_phase"; value = authoritative.Phase + 1; }
                else if (ordinal % 12 == 1) { kind = "sample_warning"; value = tick + 90; }
                else if (ordinal % 12 == 7) { kind = "sample_warning"; value = 0; }
                else { kind = "sample_feedback"; value = authoritative.Feedback + 1; report.FeedbackIssued++; generationFeedback++; }
                EventRecord record = sender.Append(tick, kind, BitConverter.GetBytes(value));
                authoritative.Apply(record); report.EventsIssued++;
            }
            private void Send(Viewer viewer)
            {
                ulong connection = viewer.Connection;
                byte[] packet = sender.BuildBatch(connection);
                if (packet == null)
                {
                    packet = sender.BuildCheckpoint(connection, sender.LastIssued, tick, authoritative.Capture());
                    report.CheckpointFrames++;
                }
                else report.BatchFrames++;
                report.PeakFrameBytes = Math.Max(report.PeakFrameBytes, packet.Length);
                link.Send(connection, () =>
                {
                    report.FramesDelivered++;
                    RuleResult received;
                    try
                    {
                        received = viewer.Receiver.Receive(packet, 0, record =>
                        {
                            string key = record.Generation + "/" + viewer.Connection + "/" + record.EventId;
                            if (!viewer.Applied.Add(key)) { report.DuplicateEffects++; Fail("duplicate applied event"); }
                            viewer.State.Apply(record); report.AppliedRecords++;
                            if (record.Kind == "sample_feedback")
                            {
                                if (!viewer.Feedback.Add(key)) { report.DuplicateEffects++; Fail("duplicate feedback effect"); }
                                report.FeedbackEffects++; viewer.GenerationFeedbackEffects++;
                            }
                        }, (state, watermark, atTick) =>
                        {
                            long before = viewer.State.Feedback;
                            if (!viewer.State.Install(state, watermark, atTick)) return false;
                            long skipped = viewer.State.Feedback - before;
                            if (skipped < 0) { Fail("checkpoint state rollback"); report.ProjectionConflicts++; }
                            report.FeedbackSuppressed += skipped; viewer.GenerationFeedbackSuppressed += skipped;
                            report.CheckpointsInstalled++; return true;
                        });
                    }
                    catch (Exception ex) { Fail("application failure: " + ex.Message); return; }
                    report.PeakInbox = Math.Max(report.PeakInbox, viewer.Receiver.PeakBuffered);
                    if (viewer.Receiver.Inbox.NeedsReplay) report.ReplayGapsObserved++;
                    if (!received.Ok)
                    {
                        if (received.Reason == "wrong_recipient" || received.Reason == "wrong_generation" || received.Reason == "stale_checkpoint")
                            report.StaleFramesRejected++;
                        else if (received.Reason != "checkpoint_required") Fail("genuine frame rejected: " + received.Reason);
                    }
                    // No ack on an application exception; a valid duplicate/stale frame may prompt the current cursor ack.
                    byte[] ack = viewer.Receiver.BuildAck(); ulong authenticated = viewer.Connection;
                    report.AckFrames++;
                    if (report.Name == "ack-blackout" && viewer == viewers[viewers.Count - 1] && link.NowMs >= 4000 && link.NowMs < 6000)
                    { report.AckBlackoutDrops++; return; }
                    link.Send(authenticated, () =>
                    {
                        RuleResult result = sender.ReceiveAck(ack, authenticated);
                        if (!result.Ok)
                        {
                            report.AckRejections++;
                            if (result.Reason != "invalid_event_ack" && result.Reason != "wrong_generation" &&
                                result.Reason != "unknown_sender" && result.Reason != "checkpoint_ack_required")
                                Fail("genuine ack rejected: " + result.Reason);
                        }
                    });
                });
            }
            private void Lifecycle(int step)
            {
                if (step != 720) return;
                if (report.Name == "reconnect")
                {
                    Viewer viewer = viewers[viewers.Count - 1]; sender.Remove(viewer.Connection);
                    viewer.Connection = nextConnection++; sender.Register(viewer.Connection);
                    viewer.Receiver = new EventReceiver(Session, viewer.Connection, sender.Generation);
                    viewer.State = new Projection(); viewer.GenerationFeedbackEffects = 0; viewer.GenerationFeedbackSuppressed = 0;
                    report.Reconnects++;
                }
                if (report.Name == "generation-reset")
                {
                    sender.Reset(sender.Generation + 1); authoritative = new Projection(); tick = 0; generationFeedback = 0;
                    foreach (Viewer viewer in viewers)
                    {
                        viewer.Receiver.Reset(sender.Generation); viewer.State = new Projection();
                        viewer.GenerationFeedbackEffects = 0; viewer.GenerationFeedbackSuppressed = 0;
                    }
                    report.GenerationResets++;
                }
            }
            public EventCase Run()
            {
                for (int step = 0; step < 2400; step++)
                {
                    link.RunUntil(step * 1000L / 60); Lifecycle(step); tick++;
                    if (step < 1800 && step % 3 == 0) Produce();
                    if (step % 3 == 0) foreach (Viewer viewer in viewers) Send(viewer);
                }
                // End in a quiet interval so the final wire ack is delivered before inspection.
                link.RunUntil(40300);
                report.CaughtUp = true;
                foreach (Viewer viewer in viewers)
                {
                    if (viewer.Receiver.Inbox.LastApplied != sender.LastIssued || !viewer.State.Same(authoritative))
                    { report.CaughtUp = false; Fail("event/state catch-up failed"); }
                    if (viewer.GenerationFeedbackEffects + viewer.GenerationFeedbackSuppressed != generationFeedback)
                        Fail("feedback replay/checkpoint accounting");
                    if (viewer.Receiver.Inbox.BufferedCount != 0 || viewer.Receiver.Inbox.NeedsCheckpoint) Fail("inbox not drained");
                    byte[] remaining = sender.BuildBatch(viewer.Connection); EventFrame decoded;
                    if (remaining == null || !EventWire.TryDecode(remaining, out decoded) || decoded.Events.Length != 0)
                        Fail("host has unacknowledged events after drain");
                }
                if (report.Name == "history-overflow" && report.CheckpointsInstalled <= report.Participants) Fail("overflow recovery not exercised");
                if (report.Name == "ack-blackout" && report.AckBlackoutDrops == 0) Fail("ack blackout not exercised");
                if (report.DuplicateEffects != 0 || report.ProjectionConflicts != 0) Fail("unsafe application");
                if (link.Sent - link.Dropped + link.Duplicated != link.Delivered) Fail("wire queue not drained/accounted");
                if (report.PeakInbox > 128 || report.PeakFrameBytes > EventWire.MaxPacketBytes) Fail("state/frame bound");
                report.PeersAfterDrain = sender.PeerCount;
                report.Sent = link.Sent; report.Dropped = link.Dropped; report.Delivered = link.Delivered; report.Duplicated = link.Duplicated;
                report.Failures = failures.ToArray(); report.Passed = failures.Count == 0;
                return report;
            }
        }
    }
}
