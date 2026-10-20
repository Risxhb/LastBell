using System;
using System.Collections.Generic;
using LastBell.Network.Rules;

namespace LastBell.Network.FaultLab
{
    public sealed class CaseReport
    {
        public string Name;
        public int Participants, RttMs, JitterMs, Seed;
        public double Loss;
        public bool SafetyPassed, EventsCaughtUp;
        public long Sent, Dropped, Duplicated, Delivered;
        public long InputGenerated, InputAccepted, InputConsumed, PressGenerated, PressConsumed;
        public long EventCount, CheckpointsInstalled, HistoricalEffectsSkipped;
        public string[] Failures;
        public Dictionary<string, long> Rejections;
        public long PressSamplesNotObserved { get { return Math.Max(0, PressGenerated - PressConsumed); } }
    }
    public sealed class LabReport
    {
        public CaseReport[] Cases;
        public bool DroppedPressRegressionReproduced;
        public string[] Failures;
    }

    // Discrete event fixture only: no sockets, Unity APIs or selected transport semantics.
    internal sealed class FaultLink
    {
        private uint rng;
        private readonly SortedDictionary<long, Queue<Action>> queue = new SortedDictionary<long, Queue<Action>>();
        private readonly int halfRtt, jitter;
        private readonly double loss;
        public long NowMs, Sent, Dropped, Duplicated, Delivered;
        public ulong BlackoutPeer = ulong.MaxValue;
        public long BlackoutStart, BlackoutEnd;
        public FaultLink(int rtt, int jitterMs, double drop, uint seed)
        { halfRtt = rtt / 2; jitter = jitterMs; loss = drop; rng = seed == 0 ? 1U : seed; }
        private double Next()
        { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return rng / 4294967296.0; }
        public void Send(ulong remotePeer, Action receive)
        {
            Sent++;
            if ((remotePeer == BlackoutPeer && NowMs >= BlackoutStart && NowMs < BlackoutEnd) || Next() < loss)
            { Dropped++; return; }
            Schedule(receive);
            if (Next() < 0.02) { Duplicated++; Schedule(receive); }
        }
        private void Schedule(Action callback)
        {
            long due = NowMs + Math.Max(1, halfRtt + (int)Math.Floor(Next() * (jitter * 2 + 1)) - jitter);
            Queue<Action> entries;
            if (!queue.TryGetValue(due, out entries)) { entries = new Queue<Action>(); queue.Add(due, entries); }
            entries.Enqueue(callback);
        }
        public void RunUntil(long until)
        {
            if (until < NowMs) throw new InvalidOperationException("Virtual clock regression");
            while (queue.Count > 0)
            {
                long first = 0;
                foreach (long key in queue.Keys) { first = key; break; }
                if (first > until) break;
                Queue<Action> entries = queue[first]; queue.Remove(first); NowMs = first;
                while (entries.Count > 0) { Delivered++; entries.Dequeue()(); }
            }
            NowMs = until;
        }
    }

    internal sealed class Client
    {
        public ulong Peer;
        public long ActorId, Sequence, SnapshotTick, SnapshotAt, Counter;
        public bool Active = true, HasSnapshot;
        public EventInbox Inbox;
        public readonly HashSet<long> Effects = new HashSet<long>();
    }

    internal sealed class Fixture
    {
        private readonly Guid identity = new Guid("ade8cb55-019a-4a22-96cd-3953c301aa71");
        private readonly SessionRules session;
        private readonly EventJournal journal;
        private readonly FaultLink link;
        private readonly List<Client> clients = new List<Client>();
        private readonly List<string> failures = new List<string>();
        private readonly Dictionary<long, long> lastProcessed = new Dictionary<long, long>();
        private readonly CaseReport report;
        private readonly bool checkpointProbe, disconnectProbe;
        private const int ActiveSteps = 600, TotalSteps = 900;

        public Fixture(int count, int rtt, double loss, int seed, bool checkpoint, bool disconnect, int jitterMs = 20)
        {
            checkpointProbe = checkpoint; disconnectProbe = disconnect;
            report = new CaseReport { Name = checkpoint ? "blackout-checkpoint" : disconnect ? "disconnect-midrun" : jitterMs == 0 ? "ordered-baseline" : "matrix",
                Participants = count, RttMs = rtt, JitterMs = jitterMs, Loss = loss, Seed = seed,
                Rejections = new Dictionary<string, long>() };
            session = new SessionRules(identity, 0);
            for (int i = 0; i < count; i++)
            {
                ulong peer = i == 0 ? 0UL : (ulong)(500 + i);
                if (i > 0 && !session.Join(peer).Ok) throw new InvalidOperationException("Fixture join");
                session.Control(peer, new ControlRequest(identity, 0, ControlKind.Ready, ready: true), 0);
                clients.Add(new Client { Peer = peer, ActorId = i + 1 });
            }
            if (!session.Control(0, new ControlRequest(identity, 0, ControlKind.Start), 0).Ok)
                throw new InvalidOperationException("Fixture start");
            journal = new EventJournal(identity, session.Generation);
            foreach (Client c in clients)
            {
                c.Inbox = new EventInbox(identity, session.Generation);
                journal.RegisterPeer(c.Peer);
                lastProcessed[c.ActorId] = 0;
            }
            link = new FaultLink(rtt, jitterMs, loss, (uint)seed);
            if (checkpoint)
            { link.BlackoutPeer = clients[count - 1].Peer; link.BlackoutStart = 1000; link.BlackoutEnd = 5000; }
        }

        public CaseReport Run()
        {
            for (int step = 0; step < TotalSteps; step++)
            {
                link.RunUntil(step * 1000L / 60);
                if (disconnectProbe && step == 300) DisconnectLast();
                if (step < ActiveSteps)
                    foreach (Client client in clients) if (client.Active) GenerateInput(client, step);
                Dictionary<long, TickCommand> commands = session.Advance();
                report.InputConsumed += commands.Count;
                foreach (var entry in commands)
                {
                    TickCommand command = entry.Value;
                    if (!command.Move.IsFinite || command.Move.X * command.Move.X + command.Move.Y * command.Move.Y > 1.000000001)
                        failures.Add("Unsafe movement");
                    if (command.Guard) report.PressConsumed++;
                    bool known = false;
                    foreach (Client c in clients) if (c.Active && c.ActorId == entry.Key) known = true;
                    if (!known) failures.Add("Command for inactive/unknown actor");
                }
                SessionSnapshot snapshot = session.Snapshot();
                foreach (var ack in snapshot.ProcessedSequences)
                {
                    if (ack.Value < lastProcessed[ack.Key]) failures.Add("Acknowledgment regressed");
                    lastProcessed[ack.Key] = ack.Value;
                }
                if (step < ActiveSteps && (checkpointProbe || step % 10 == 0))
                    journal.Append(session.Tick, 1, "diagnostic_counter", new byte[0]);
                if (step % 3 == 0)
                    foreach (Client client in clients) if (client.Active) SendSnapshot(client, snapshot);
                if (step % 6 == 0)
                    foreach (Client client in clients) if (client.Active) SendEvents(client);
            }
            link.RunUntil(TotalSteps * 1000L / 60 + 300);
            report.EventsCaughtUp = true;
            foreach (Client c in clients)
                if (c.Active && (c.Inbox.LastApplied != journal.LastIssued || c.Counter != journal.LastIssued))
                    report.EventsCaughtUp = false;
            if (!report.EventsCaughtUp) failures.Add("Event stream failed to catch up");
            if (report.Name == "ordered-baseline" && report.PressGenerated != report.PressConsumed)
                failures.Add("Ordered baseline lost a Guard press");
            if (checkpointProbe && report.CheckpointsInstalled == 0) failures.Add("Checkpoint probe did not overflow");
            report.EventCount = journal.LastIssued;
            report.Sent = link.Sent; report.Dropped = link.Dropped; report.Duplicated = link.Duplicated; report.Delivered = link.Delivered;
            report.SafetyPassed = failures.Count == 0;
            report.Failures = failures.ToArray();
            return report;
        }

        private void GenerateInput(Client client, int step)
        {
            if (client.Peer != 0 && !client.HasSnapshot) return;
            bool guard = step % 60 == 30;
            client.Sequence++; report.InputGenerated++;
            if (guard) report.PressGenerated++;
            // Idealized, known symmetric-delay estimate for this fixture, NOT a production clock-sync algorithm.
            long target = client.Peer == 0 ? session.Tick + 1 :
                client.SnapshotTick + (link.NowMs - client.SnapshotAt + report.RttMs / 2) * 60 / 1000 + report.RttMs * 60 / 2000 + 2;
            var input = new InputRequest(identity, session.Generation, client.Sequence, target,
                new PlanarMove(0.5, 0.5), true, false, false, guard);
            byte[] bytes = InputCodec.Encode(input);
            Action receive = () =>
            {
                InputRequest decoded; string reason;
                if (!InputCodec.TryDecode(bytes, out decoded, out reason)) { failures.Add("Fixture codec rejected"); return; }
                RuleResult result = session.SubmitInput(client.Peer, decoded, link.NowMs);
                if (result.Ok) report.InputAccepted++; else Reject(result.Reason);
            };
            if (client.Peer == 0) receive(); else link.Send(client.Peer, receive);
        }

        private void SendSnapshot(Client client, SessionSnapshot snapshot)
        {
            Action receive = () =>
            {
                if (!client.Active || (client.HasSnapshot && snapshot.Tick < client.SnapshotTick)) return;
                client.HasSnapshot = true; client.SnapshotTick = snapshot.Tick; client.SnapshotAt = link.NowMs;
            };
            if (client.Peer == 0) receive(); else link.Send(client.Peer, receive);
        }

        private void SendEvents(Client client)
        {
            EventReplay replay = journal.ReplayFor(client.Peer);
            Action receive = () =>
            {
                if (!client.Active) return;
                if (replay.NeedsCheckpoint)
                {
                    // Fixture full state is ONLY an event counter. No combat state/checkpoint exists here.
                    long previous = client.Inbox.LastApplied;
                    RuleResult installed = client.Inbox.InstallCheckpoint(identity, session.Generation, replay.Watermark);
                    if (installed.Ok)
                    {
                        client.Counter = replay.Watermark;
                        report.CheckpointsInstalled++;
                        report.HistoricalEffectsSkipped += replay.Watermark - previous;
                    }
                    else if (installed.Reason != "invalid_watermark") failures.Add("Checkpoint scope failure");
                }
                else
                {
                    client.Inbox.ObserveWatermark(identity, session.Generation, replay.Watermark);
                    RuleResult offered = client.Inbox.Offer(replay.Records);
                    if (!offered.Ok && offered.Reason != "checkpoint_required") failures.Add("Event batch rejected: " + offered.Reason);
                    while (client.Inbox.ApplyNext(record =>
                    {
                        if (!client.Effects.Add(record.EventId)) failures.Add("Duplicate feedback");
                        if (record.EventId != client.Counter + 1) failures.Add("Noncontiguous event application");
                        client.Counter = record.EventId;
                    })) { }
                }
                long ack = client.Inbox.LastApplied;
                Action acknowledge = () =>
                {
                    if (!client.Active) return;
                    RuleResult result = journal.Acknowledge(client.Peer, identity, session.Generation, ack);
                    // Older ACK packets can arrive after newer ones. Rejection is the expected safe response.
                    if (!result.Ok && result.Reason != "invalid_event_ack") failures.Add("Ack scope failure");
                };
                if (client.Peer == 0) acknowledge(); else link.Send(client.Peer, acknowledge);
            };
            if (replay.NeedsCheckpoint) journal.MarkCheckpointSent(client.Peer, replay.Watermark);
            if (client.Peer == 0) receive(); else link.Send(client.Peer, receive);
        }

        private void DisconnectLast()
        {
            Client client = clients[clients.Count - 1];
            client.Active = false; session.Disconnect(client.Peer); journal.RemovePeer(client.Peer);
            var late = new InputRequest(identity, session.Generation, client.Sequence + 1, session.Tick + 1,
                new PlanarMove(0, 0), false, false, false, true);
            if (session.SubmitInput(client.Peer, late, link.NowMs).Reason != "unknown_sender")
                failures.Add("Disconnected sender accepted");
        }

        private void Reject(string reason)
        { long count; report.Rejections.TryGetValue(reason, out count); report.Rejections[reason] = count + 1; }
    }

    public static class Runner
    {
        public static LabReport Run()
        {
            var cases = new List<CaseReport>();
            var failures = new List<string>();
            foreach (int participants in new[] {2,3,4,5,6,7,8})
                foreach (int rtt in new[] {50,100,150})
                    foreach (double loss in new[] {0.0,0.03})
                    {
                        int seed = 1700 + participants * 100 + rtt + (loss > 0 ? 1 : 0);
                        cases.Add(new Fixture(participants, rtt, loss, seed, false, false).Run());
                    }
            foreach (int participants in new[] {2,3,4,5,6,7,8})
                foreach (int rtt in new[] {50,100,150})
                    cases.Add(new Fixture(participants, rtt, 0, 4000 + participants * 100 + rtt, false, false, 0).Run());
            cases.Add(new Fixture(8, 100, 0.03, 9001, true, false).Run());
            cases.Add(new Fixture(8, 150, 0.03, 9002, false, true).Run());
            foreach (CaseReport result in cases)
                if (!result.SafetyPassed) failures.Add(result.Name + "/" + result.Participants + "/" + result.RttMs + ": " + string.Join(", ", result.Failures));
            bool reproduced = DroppedPressRegression();
            if (!reproduced) failures.Add("Dropped press regression fixture no longer reproduces; investigate/update expectation");
            return new LabReport { Cases = cases.ToArray(), DroppedPressRegressionReproduced = reproduced, Failures = failures.ToArray() };
        }

        private static bool DroppedPressRegression()
        {
            Guid identity = new Guid("58e3a971-238c-4e04-a055-709bb3bc97cc");
            var s = new SessionRules(identity, 0);
            s.Join(501);
            foreach (ulong peer in new ulong[] {0,501})
                s.Control(peer, new ControlRequest(identity, 0, ControlKind.Ready, ready: true), 0);
            s.Control(0, new ControlRequest(identity, 0, ControlKind.Start), 0);
            // Sequence 1 with Guard is lost on the link. Sequence 2 without Guard reaches host.
            var later = new InputRequest(identity, 1, 2, 1, new PlanarMove(0,0), true, false, false, false);
            RuleResult accepted = s.SubmitInput(501, later, 1);
            Dictionary<long, TickCommand> commands = s.Advance();
            var retry = new InputRequest(identity, 1, 1, 1, new PlanarMove(0,0), true, false, false, true);
            return accepted.Ok && commands.ContainsKey(2) && !commands[2].Guard &&
                s.Snapshot().ProcessedSequences[2] == 2 && s.SubmitInput(501, retry, 2).Reason == "stale_sequence";
        }
    }
}
