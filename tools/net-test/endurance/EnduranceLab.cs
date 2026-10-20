using System;
using System.Collections.Generic;
using LastBell.Network.ActionPrototype;

namespace LastBell.Network.Endurance
{
    public sealed class EnduranceCase
    {
        public string Name;
        public int Participants, Seed, VirtualSeconds, UpMs, DownMs, JitterMs, MaxDriftPpm;
        public double Loss;
        public long Generated, Consumed, ConfirmedConsumed, Expired, Unknown, Rejected, Cancelled, Abandoned;
        public long Sent, Delivered, Dropped, Duplicated, RequestsSent, ReceiptsAccepted, ReceiptsIgnored;
        public long Reconnects, Resets, UnsynchronizedAttempts, SamplesAccepted, SamplesIgnored, ClockComparisons;
        public long RequestOutageDrops, ReceiptOutageDrops, BidirectionalOutageDrops, HeldPackets;
        public long DuplicateConsumptions, LateConsumptions, InactiveConsumptions, ConflictingOutcomes;
        public long ConsumedWithoutConfirmation, UnknownActuallyConsumed, AbandonedActuallyConsumed;
        public long RecoveryWindows, RecoveredWindows, MaxRecoveryMs;
        public int PeakClientPending, PeakClientResults, PeakHostTickets, PeakPacketQueue, PeakClockSamples, PeakClockProbes;
        public int MaxPendingAfterDrain, PacketsAfterDrain;
        public double MaxClockErrorMs, MaxClockUncertaintyMs;
        public bool Passed;
        public string[] Failures;
        public Dictionary<string, long> IgnoredReasons;
        public Dictionary<string, long> HostRequestReasons;
    }
    internal sealed class VirtualLink
    {
        private readonly SortedDictionary<long, Queue<Action>> queue = new SortedDictionary<long, Queue<Action>>();
        private uint state;
        private int pending;
        private readonly EnduranceCase report;
        public long Now;
        public int Pending { get { return pending; } }
        public VirtualLink(EnduranceCase result) { report = result; state = (uint)result.Seed; }
        private double Random()
        { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return state / 4294967296.0; }
        public void Send(bool up, bool disturbed, Action receive)
        {
            report.Sent++;
            long phase = Now % 120000;
            if (disturbed && phase >= 20000 && phase < 23000 && up)
            { report.Dropped++; report.RequestOutageDrops++; return; }
            if (disturbed && phase >= 45000 && phase < 48000 && !up)
            { report.Dropped++; report.ReceiptOutageDrops++; return; }
            if (disturbed && phase >= 70000 && phase < 76000)
            { report.Dropped++; report.BidirectionalOutageDrops++; return; }
            if (Random() < report.Loss) { report.Dropped++; return; }
            int delay = Math.Max(1, (up ? report.UpMs : report.DownMs) +
                (int)(Random() * (2 * report.JitterMs + 1)) - report.JitterMs);
            // Sparse very late delivery, including packets crossing reconnect/reset boundaries.
            if (disturbed && ((phase >= 29000 && phase < 30000) || (phase >= 59000 && phase < 60000) || phase >= 119000))
            { delay += 8000; report.HeldPackets++; }
            Add(Now + delay, receive);
            if (Random() < 0.03) { report.Duplicated++; Add(Now + delay + (int)(Random() * 25), receive); }
        }
        private void Add(long due, Action receive)
        {
            Queue<Action> at;
            if (!queue.TryGetValue(due, out at)) { at = new Queue<Action>(); queue.Add(due, at); }
            at.Enqueue(receive); pending++;
            report.PeakPacketQueue = Math.Max(report.PeakPacketQueue, pending);
        }
        public void RunUntil(long until)
        {
            if (until < Now) throw new ArgumentException("Virtual time regression");
            while (queue.Count > 0)
            {
                long due = 0; foreach (long key in queue.Keys) { due = key; break; }
                if (due > until) break;
                Queue<Action> callbacks = queue[due]; queue.Remove(due); Now = due;
                while (callbacks.Count > 0) { pending--; report.Delivered++; callbacks.Dequeue()(); }
            }
            Now = until;
        }
    }
    internal sealed class AuditAction
    {
        public Request Request;
        public ulong Handle;
        public long Player;
        public int Emissions;
        public long CreatedAt;
        public string Outcome = "pending";
        public string Key { get { return Request.Generation + "/" + Handle + "/" + Request.Id; } }
    }
    internal sealed class RecoveryWindow
    {
        public int Slot;
        public long Start;
        public string Kind;
    }
    internal sealed class DriftingPeer
    {
        public ulong Handle;
        public int Slot, Ppm;
        public double Offset;
        public bool Active = true;
        public long NextProbe;
        public ActionClient Client;
        public ClockEstimator Clock;
        public readonly Dictionary<long, double> Probes = new Dictionary<long, double>();
        public readonly Dictionary<long, AuditAction> Watching = new Dictionary<long, AuditAction>();
        public double Local(long realMs) { return Offset + realMs * (1 + Ppm / 1000000.0); }
        // The client's retry clock has a different epoch and rate too, but must be nonnegative.
        public long RetryTime(long realMs) { return (long)(600000 + Local(realMs)); }
    }

    public static class EnduranceRunner
    {
        public static EnduranceCase[] Run(int durationSeconds, bool includeTwoHour)
        {
            var results = new List<EnduranceCase>();
            for (int count = 2; count <= 8; count++)
                for (int profile = 0; profile < 3; profile++)
                    results.Add(new Simulation(count, durationSeconds, profile, 17001 + count * 101 + profile).Run());
            if (includeTwoHour)
            {
                results.Add(new Simulation(2, 7200, 1, 77021).Run());
                results.Add(new Simulation(8, 7200, 2, 77081).Run());
            }
            return results.ToArray();
        }
        private sealed class Simulation
        {
            private static readonly Guid Session = new Guid("b73da075-5f59-4528-a199-6ad9e42d6779");
            private readonly Policy policy = new Policy();
            private readonly ActionHost host;
            private readonly VirtualLink link;
            private readonly EnduranceCase report;
            private readonly List<DriftingPeer> peers = new List<DriftingPeer>();
            // Test oracle only, intentionally retains full transcripts. Not protocol resident state.
            private readonly Dictionary<string, AuditAction> audit = new Dictionary<string, AuditAction>();
            private readonly List<string> failures = new List<string>();
            private readonly List<RecoveryWindow> recovery = new List<RecoveryWindow>();
            private ulong nextHandle = 100;
            private long epochStart, disconnectedAt = -1;
            private long probeId;
            public Simulation(int count, int seconds, int profile, int seed)
            {
                if (seconds < 120 || seconds > 86400) throw new ArgumentException("Duration must be 120..86400 virtual seconds");
                report = new EnduranceCase { Name = seconds == 7200 ? "two-hour" : "matrix", Participants = count,
                    VirtualSeconds = seconds, Seed = seed, UpMs = profile == 0 ? 25 : profile == 1 ? 25 : 125,
                    DownMs = profile == 0 ? 25 : profile == 1 ? 125 : 25, JitterMs = profile == 0 ? 10 : 25,
                    Loss = profile == 0 ? 0 : profile == 1 ? 0.03 : 0.10, MaxDriftPpm = 500,
                    IgnoredReasons = new Dictionary<string, long>(), HostRequestReasons = new Dictionary<string, long>() };
                host = new ActionHost(Session, policy); link = new VirtualLink(report);
                for (int i = 0; i < count; i++)
                {
                    var peer = new DriftingPeer { Slot = i, Handle = nextHandle++, Ppm = i % 2 == 0 ? 500 : -500,
                        Offset = (i % 2 == 0 ? 1 : -1) * (120000 + i * 17000),
                        Client = new ActionClient(Session, policy), Clock = new ClockEstimator(1) };
                    peers.Add(peer); host.Register(peer.Handle, i + 1);
                }
            }
            private void Fail(string message)
            { if (failures.Count < 30 && !failures.Contains(message)) failures.Add(message); }
            private string Key(int generation, ulong handle, long id) { return generation + "/" + handle + "/" + id; }
            private void Ignore(string reason)
            {
                report.ReceiptsIgnored++; long count; report.IgnoredReasons.TryGetValue(reason, out count);
                report.IgnoredReasons[reason] = count + 1;
            }
            private void SendProbe(DriftingPeer peer)
            {
                double local = peer.Local(link.Now);
                var expired = new List<long>();
                foreach (var entry in peer.Probes) if (local - entry.Value > ClockEstimator.MaxAgeMs) expired.Add(entry.Key);
                foreach (long id in expired) peer.Probes.Remove(id);
                if (peer.Probes.Count >= 8) return;
                long nonce = ++probeId; peer.Probes.Add(nonce, local);
                report.PeakClockProbes = Math.Max(report.PeakClockProbes, peer.Probes.Count);
                ulong handle = peer.Handle; int generation = host.Generation;
                link.Send(true, peer.Slot == peers.Count - 1, () =>
                {
                    // Probe/session authentication is fixture metadata, not a claimed wire field.
                    if (!peer.Active || handle != peer.Handle || generation != host.Generation) return;
                    double hostAt = link.Now;
                    link.Send(false, peer.Slot == peers.Count - 1, () =>
                    {
                        double sent;
                        if (!peer.Active || handle != peer.Handle || generation != host.Generation ||
                            !peer.Probes.TryGetValue(nonce, out sent)) { report.SamplesIgnored++; return; }
                        peer.Probes.Remove(nonce);
                        if (peer.Clock.Add(generation, sent, hostAt, hostAt, peer.Local(link.Now))) report.SamplesAccepted++;
                        else report.SamplesIgnored++;
                    });
                });
            }
            private void Transmit(DriftingPeer peer, Request request)
            {
                report.RequestsSent++;
                ulong handle = peer.Handle;
                byte[] packet = RequestCodec.Encode(request);
                link.Send(true, peer.Slot == peers.Count - 1, () =>
                {
                    Request decoded;
                    if (!RequestCodec.TryDecode(packet, out decoded)) { Fail("generated request decode"); return; }
                    Receipt receipt = host.Receive(handle, decoded, link.Now);
                    if (receipt.Reason != "")
                    {
                        long count; report.HostRequestReasons.TryGetValue(receipt.Reason, out count);
                        report.HostRequestReasons[receipt.Reason] = count + 1;
                    }
                    byte[] response;
                    if (!ReceiptCodec.TryEncode(receipt, handle, out response)) { Fail("host receipt encode"); return; }
                    link.Send(false, peer.Slot == peers.Count - 1, () =>
                    {
                        string reason;
                        if (!peer.Active) { Ignore("disconnected_endpoint"); return; }
                        if (!ReceiptIngress.TryApply(response, 0, 0, peer.Handle, peer.Client, out reason))
                        {
                            bool obsolete = handle != peer.Handle || decoded.Generation != peer.Client.Generation;
                            bool evicted = !peer.Watching.ContainsKey(decoded.Id) && peer.Client.Outcome(decoded.Id) == null;
                            if (!obsolete && !(reason == "unknown_or_mismatched_request" && evicted))
                                Fail("unexpected genuine confirmation rejection: " + reason);
                            Ignore(reason); return;
                        }
                        report.ReceiptsAccepted++;
                        AuditAction record;
                        if (!audit.TryGetValue(Key(decoded.Generation, handle, decoded.Id), out record)) { Fail("receipt without generated action"); return; }
                        Observe(peer, record);
                    });
                });
            }
            private void Observe(DriftingPeer peer, AuditAction record)
            {
                Receipt outcome = peer.Client.Outcome(record.Request.Id);
                if (outcome == null) return;
                record.Outcome = outcome.Status.ToString();
                peer.Watching.Remove(record.Request.Id);
                if (outcome.Status == ActionStatus.Consumed && record.Emissions != 1)
                { report.ConflictingOutcomes++; Fail("consumed receipt without exactly one emission"); }
                if (outcome.Status == ActionStatus.Expired && record.Emissions != 0)
                { report.ConflictingOutcomes++; Fail("expired receipt contradicts emission"); }
                if (outcome.Status == ActionStatus.Consumed)
                    for (int i = recovery.Count - 1; i >= 0; i--)
                    {
                        RecoveryWindow window = recovery[i];
                        if (window.Slot != peer.Slot || record.CreatedAt < window.Start) continue;
                        if (link.Now - window.Start > 3000) Fail("late recovery: " + window.Kind);
                        report.RecoveredWindows++;
                        report.MaxRecoveryMs = Math.Max(report.MaxRecoveryMs, link.Now - window.Start);
                        recovery.RemoveAt(i);
                    }
            }
            private void RequireRecovery(DriftingPeer peer, string kind)
            {
                recovery.Add(new RecoveryWindow { Slot = peer.Slot, Start = link.Now, Kind = kind });
                report.RecoveryWindows++;
            }
            private void Retire(DriftingPeer peer, string why)
            {
                foreach (AuditAction action in peer.Watching.Values) action.Outcome = why;
                peer.Watching.Clear(); peer.Probes.Clear();
            }
            private void Lifecycle(long step, long activeSteps)
            {
                if (step >= activeSteps) return;
                // Reset every five minutes; the retained roster gets the new generation atomically.
                if (step > 0 && step % 18000 == 0)
                {
                    foreach (DriftingPeer peer in peers) Retire(peer, "abandoned_generation");
                    host.ResetGeneration(host.Generation + 1); epochStart = link.Now; report.Resets++;
                    foreach (DriftingPeer peer in peers)
                    {
                        peer.Client.ResetGeneration(host.Generation); peer.Clock.Reset(host.Generation); peer.NextProbe = link.Now;
                        RequireRecovery(peer, "generation_reset");
                    }
                }
                DriftingPeer last = peers[peers.Count - 1];
                if (step > 0 && step % 1800 == 0 && last.Active)
                {
                    host.Disconnect(last.Handle); Retire(last, "abandoned_connection");
                    last.Active = false; disconnectedAt = link.Now;
                }
                if (!last.Active && link.Now - disconnectedAt >= 1000)
                {
                    last.Handle = nextHandle++; host.Register(last.Handle, last.Slot + 1);
                    last.Client = new ActionClient(Session, policy, host.Generation);
                    last.Clock = new ClockEstimator(host.Generation); last.Active = true;
                    last.NextProbe = link.Now; report.Reconnects++;
                    RequireRecovery(last, "reconnect");
                }
                long phase = link.Now % 120000;
                if (phase == 23000 || phase == 48000 || phase == 76000) RequireRecovery(last, "outage_end");
            }
            private void SampleBounds(DriftingPeer peer)
            {
                int pending = peer.Client.PendingCount, completed = peer.Client.CompletedCount, tickets = host.TicketCount(peer.Handle);
                report.PeakClientPending = Math.Max(report.PeakClientPending, pending);
                report.PeakClientResults = Math.Max(report.PeakClientResults, completed);
                report.PeakHostTickets = Math.Max(report.PeakHostTickets, tickets);
                report.PeakClockSamples = Math.Max(report.PeakClockSamples, peer.Clock.Count);
                if (pending > policy.MaxPending || completed > policy.RetainedResults ||
                    tickets > policy.MaxPending + policy.RetainedResults || peer.Clock.Count > ClockEstimator.Capacity || peer.Probes.Count > 8)
                    Fail("protocol resident collection bound");
            }
            public EnduranceCase Run()
            {
                long activeSteps = report.VirtualSeconds * 60L;
                for (long step = 0; step < activeSteps + 720; step++)
                {
                    link.RunUntil(step * 1000 / 60);
                    Lifecycle(step, activeSteps);
                    for (int i = recovery.Count - 1; i >= 0; i--)
                        if (link.Now - recovery[i].Start > 3000)
                        { Fail("recovery deadline: " + recovery[i].Kind); recovery.RemoveAt(i); }
                    foreach (DriftingPeer peer in peers)
                    {
                        if (!peer.Active) continue;
                        if (step < activeSteps && link.Now >= peer.NextProbe)
                        { SendProbe(peer); peer.NextProbe = link.Now + 250; }
                        if (step < activeSteps && step % 30 == (peer.Slot * 3 + 10) % 30)
                        {
                            double estimated, uncertainty;
                            if (!peer.Clock.TryEstimate(peer.Local(link.Now), out estimated, out uncertainty)) report.UnsynchronizedAttempts++;
                            else
                            {
                                double error = Math.Abs(estimated - link.Now);
                                report.ClockComparisons++;
                                report.MaxClockErrorMs = Math.Max(report.MaxClockErrorMs, error);
                                report.MaxClockUncertaintyMs = Math.Max(report.MaxClockUncertaintyMs, uncertainty);
                                if (error > uncertainty + 1) Fail("clock error outside diagnostic uncertainty");
                                long created = Math.Max(0, (long)Math.Floor((estimated - epochStart) * 60 / 1000));
                                Ability ability = (Ability)(1 + (step / 30 + peer.Slot) % 3);
                                Request request = peer.Client.Queue(ability, created, created + 1, peer.RetryTime(link.Now));
                                var record = new AuditAction { Request = request, Handle = peer.Handle, Player = peer.Slot + 1, CreatedAt = link.Now };
                                audit.Add(record.Key, record); peer.Watching.Add(request.Id, record); report.Generated++;
                            }
                        }
                        foreach (Request request in peer.Client.Poll(peer.RetryTime(link.Now))) Transmit(peer, request);
                        foreach (AuditAction record in new List<AuditAction>(peer.Watching.Values)) Observe(peer, record);
                        SampleBounds(peer);
                    }
                    foreach (ConsumedAction consumed in host.Step(host.Tick + 1))
                    {
                        AuditAction record;
                        if (!audit.TryGetValue(Key(consumed.Request.Generation, consumed.ConnectionHandle, consumed.Request.Id), out record))
                        { Fail("emission without generated action"); continue; }
                        record.Emissions++; report.Consumed++;
                        if (record.Emissions != 1) { report.DuplicateConsumptions++; Fail("duplicate consumption"); }
                        if (consumed.Tick < consumed.Request.TargetTick || consumed.Tick >= consumed.Request.ExpiryTick)
                        { report.LateConsumptions++; Fail("outside action window"); }
                        bool active = false;
                        foreach (DriftingPeer peer in peers)
                            if (peer.Active && peer.Handle == consumed.ConnectionHandle && peer.Slot + 1 == consumed.PlayerId) active = true;
                        if (!active || record.Player != consumed.PlayerId || consumed.Request.Generation != host.Generation ||
                            !record.Request.Same(consumed.Request))
                        { report.InactiveConsumptions++; Fail("inactive incarnation consumption"); }
                    }
                }
                // Stop at an exact frame boundary after >8s maximum hold plus the retry horizon.
                foreach (DriftingPeer peer in peers)
                {
                    report.MaxPendingAfterDrain = Math.Max(report.MaxPendingAfterDrain, peer.Client.PendingCount);
                    if (peer.Client.PendingCount != 0 || peer.Watching.Count != 0) Fail("pending action after drain");
                }
                foreach (AuditAction record in audit.Values)
                {
                    if (record.Emissions == 1 && record.Outcome != "Consumed") report.ConsumedWithoutConfirmation++;
                    if (record.Emissions == 1 && record.Outcome == "UnconfirmedTimeout") report.UnknownActuallyConsumed++;
                    if (record.Emissions == 1 && record.Outcome.StartsWith("abandoned_", StringComparison.Ordinal)) report.AbandonedActuallyConsumed++;
                    switch (record.Outcome)
                    {
                        case "Consumed": report.ConfirmedConsumed++; break;
                        case "Expired": report.Expired++; break;
                        case "UnconfirmedTimeout": report.Unknown++; break;
                        case "Rejected": report.Rejected++; break;
                        case "Cancelled": report.Cancelled++; break;
                        case "HistoryUnavailable": report.Unknown++; break;
                        case "abandoned_generation": case "abandoned_connection": report.Abandoned++; break;
                        default: Fail("unaccounted outcome: " + record.Outcome); break;
                    }
                }
                if (report.Generated != report.ConfirmedConsumed + report.Expired + report.Unknown + report.Rejected + report.Cancelled + report.Abandoned)
                    Fail("final action accounting");
                if (report.ConfirmedConsumed > report.Consumed) Fail("confirmation overcount");
                report.PacketsAfterDrain = link.Pending;
                if (link.Pending != 0) Fail("fixture packets after drain");
                if (report.Sent - report.Dropped + report.Duplicated != report.Delivered + link.Pending)
                    Fail("packet accounting");
                if (report.Consumed != report.ConfirmedConsumed + report.ConsumedWithoutConfirmation) Fail("consumption accounting");
                if (report.RecoveryWindows != report.RecoveredWindows || recovery.Count != 0) Fail("not all recovery windows confirmed");
                if (report.Rejected != 0 || report.Cancelled != 0) Fail("fresh synchronized action rejected/cancelled");
                if (report.Reconnects == 0 || report.RequestOutageDrops == 0 || report.ReceiptOutageDrops == 0 ||
                    report.BidirectionalOutageDrops == 0 || report.HeldPackets == 0 || report.UnsynchronizedAttempts == 0)
                    Fail("required disturbance not exercised");
                if (report.VirtualSeconds > 300 && report.Resets == 0) Fail("reset not exercised");
                if (report.PeakPacketQueue > report.Participants * 256) Fail("fixture packet queue high-water budget");
                if (report.ClockComparisons == 0 || report.SamplesAccepted == 0) Fail("clock samples not exercised");
                report.Failures = failures.ToArray(); report.Passed = failures.Count == 0;
                return report;
            }
        }
    }
}
