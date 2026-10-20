using System;
using System.Collections.Generic;
using LastBell.Network.ActionPrototype;

namespace LastBell.Network.Endurance
{
    public sealed class RecoveryReport
    {
        public int Assertions;
        public string[] Checks, Failures;
    }
    public static class RecoveryChecks
    {
        private static readonly Guid Session = new Guid("3ea1ac92-9462-4917-be56-5be199fb611a");
        private static readonly List<string> checks = new List<string>(), failures = new List<string>();
        private static int assertions;
        private static void Check(bool test, string label) { assertions++; if (!test) failures.Add(label); }
        private static void Scenario(string name, Action run)
        {
            int before = failures.Count;
            try { run(); } catch (Exception ex) { failures.Add(name + ": " + ex); }
            if (before == failures.Count) checks.Add(name);
        }
        private static bool Throws(Action run)
        { try { run(); return false; } catch (ArgumentException) { return true; } catch (InvalidOperationException) { return true; } }
        private static Request Request(long id, long tick = 0, int generation = 1)
        { return new Request(Session, generation, id, tick, tick + 1, tick + 12, Ability.Guard); }
        private static bool Apply(Receipt receipt, ulong recipient, ulong connection, ActionClient client)
        {
            byte[] wire; string reason;
            return ReceiptCodec.TryEncode(receipt, recipient, out wire) && ReceiptIngress.TryApply(wire, 0, 0, connection, client, out reason);
        }
        public static RecoveryReport Run()
        {
            assertions = 0; checks.Clear(); failures.Clear();
            Scenario("clock-offset-drift-expiry-and-bounds", ClockSamples);
            Scenario("clock-invalid-input-and-empty-reset-generation", InvalidClock);
            Scenario("host-history-eviction-never-replays-consumed-action", History);
            Scenario("pending-id-survives-higher-result-eviction", PendingHistory);
            Scenario("saturation-rate-limit-and-retry-clock-boundaries", Saturation);
            Scenario("retired-handle-capacity-and-generation-recovery", Handles);
            Scenario("reconnect-id-reuse-rejects-old-recipient", Reconnect);
            Scenario("reset-in-flight-request-and-confirmation", Reset);
            Scenario("eight-connections-independent-action-identities", IndependentIds);
            Scenario("host-process-loss-requires-new-generation", ProcessLoss);
            return new RecoveryReport { Assertions = assertions, Checks = checks.ToArray(), Failures = failures.ToArray() };
        }
        private static void ClockSamples()
        {
            var clock = new ClockEstimator(1); double host, uncertainty;
            Check(!clock.TryEstimate(0, out host, out uncertainty), "no action clock before synchronization");
            // Clock offset +120 seconds, 500 ppm drift; asymmetric 25/125ms path.
            for (int i = 0; i < 10000; i++)
            {
                double sent = i * 250.0, arrive = sent + 150;
                Check(clock.Add(1, 120000 + sent * 1.0005, sent + 25, sent + 25, 120000 + arrive * 1.0005), "clock accepts current probe");
                Check(clock.Count <= 8 && clock.TryEstimate(120000 + arrive * 1.0005, out host, out uncertainty) &&
                    Math.Abs(host - arrive) <= uncertainty + 1, "bounded estimate across drift");
            }
            double finalLocal = 120000 + (9999 * 250.0 + 150) * 1.0005;
            Check(!clock.TryEstimate(finalLocal + 2001, out host, out uncertainty), "stale samples suppress new action clock");
            clock.Reset(2);
            Check(clock.Count == 0 && !clock.Add(1, 0, 1, 1, 2), "generation reset invalidates old clock");
        }
        private static void InvalidClock()
        {
            var clock = new ClockEstimator(1); double host, uncertainty;
            Check(Throws(() => clock.Reset(1)), "same generation reset rejected even when empty");
            Check(Throws(() => clock.Reset(0)), "older generation rejected");
            Check(!clock.Add(1, double.NaN, 0, 0, 1), "NaN rejected");
            Check(!clock.Add(1, 0, 0, 0, double.PositiveInfinity), "infinity rejected");
            Check(!clock.Add(1, 2, 0, 0, 1), "local timestamp regression rejected");
            Check(!clock.Add(1, 0, 3, 2, 1), "host timestamp regression rejected");
            Check(!clock.Add(1, 0, 0, 200, 1), "negative corrected RTT rejected");
            Check(!clock.Add(1, 0, 0, 0, 1001), "excessively late probe rejected");
            Check(clock.Add(1, 10, 10, 10, 20), "valid control");
            Check(!clock.Add(1, 0, 1, 1, 2), "reordered local arrival rejected");
            Check(!clock.TryEstimate(19, out host, out uncertainty), "clock regression cannot use future sample");
        }
        private static void History()
        {
            var host = new ActionHost(Session, new Policy()); host.Register(1, 1);
            Request oldest = null;
            for (int id = 1; id <= 500; id++)
            {
                Request request = Request(id, host.Tick); if (oldest == null) oldest = request;
                Check(host.Receive(1, request, id * 1000).Status == ActionStatus.Pending, "history control accepted");
                Check(host.Step(host.Tick + 1).Length == 1, "history control consumed once");
                Check(host.TicketCount(1) <= 64, "terminal history stays bounded");
            }
            Check(host.Receive(1, oldest, 501000).Status == ActionStatus.HistoryUnavailable, "evicted result remains non-replayable");
            Check(host.Step(host.Tick + 1).Length == 0, "no replay after host cache eviction");
        }
        private static void PendingHistory()
        {
            var host = new ActionHost(Session, new Policy()); host.Register(1, 1);
            var pending = new Request(Session, 1, 1, 0, 11, 12, Ability.Guard);
            Check(host.Receive(1, pending, 0).Status == ActionStatus.Pending, "old pending admitted");
            for (int tick = 0; tick < 10; tick++)
            {
                for (int i = 0; i < 10; i++)
                {
                    Request request = Request(2 + tick * 10 + i, host.Tick);
                    Check(host.Receive(1, request, tick * 1000 + i).Status == ActionStatus.Pending, "higher pending accepted");
                }
                Check(host.Step(host.Tick + 1).Length == 10, "only higher ready actions emitted");
            }
            Check(host.TicketCount(1) == 65, "64 terminal plus one older pending");
            Check(host.Receive(1, pending, 11000).Status == ActionStatus.Pending, "pending survives reject-through tombstone");
            ConsumedAction[] result = host.Step(11);
            Check(result.Length == 1 && result[0].Request.Id == 1, "old pending eventually emitted once");
        }
        private static void Saturation()
        {
            var policy = new Policy(); var host = new ActionHost(Session, policy); host.Register(1, 1);
            var client = new ActionClient(Session, policy);
            for (int i = 0; i < 32; i++)
            {
                Request request = client.Queue(Ability.Guard, 0, 11, 0);
                Check(host.Receive(1, request, 0).Status == ActionStatus.Pending, "pending capacity accepted");
            }
            Check(Throws(() => client.Queue(Ability.Guard, 0, 11, 0)), "client applies backpressure");
            Check(host.Receive(1, new Request(Session, 1, 33, 0, 11, 12, Ability.Guard), 0).Reason == "pending_full", "host applies backpressure");
            for (int i = 33; i < 120; i++) host.Receive(1, Request(1), 0);
            Check(host.Receive(1, Request(1), 0).Reason == "rate_limited", "bounded per-second admission");
            Check(host.Receive(1, new Request(Session, 1, 1, 0, 11, 12, Ability.Guard), 1000).Status == ActionStatus.Pending, "rate window recovers");
            Check(host.Receive(1, Request(1), 999).Reason == "invalid_clock", "host monotonic rate clock");
            Check(client.Poll(0).Length == 32 && client.Poll(49).Length == 0 && client.Poll(50).Length == 32, "retry cadence");
            Check(Throws(() => client.Poll(49)), "client monotonic retry clock");
            Check(client.Poll(2000).Length == 0 && client.PendingCount == 0 && client.CompletedCount == 32, "outage drains to explicit uncertainty");
        }
        private static void Handles()
        {
            var host = new ActionHost(Session, new Policy());
            for (ulong handle = 1; handle <= 128; handle++) { host.Register(handle, 1); host.Disconnect(handle); }
            Check(Throws(() => host.Register(129, 1)), "generation handle ledger bounded at 128");
            Check(host.Receive(128, Request(1), 0).Status == ActionStatus.Cancelled, "retired handle cannot submit");
            host.ResetGeneration(2); host.Register(129, 1);
            Check(host.Receive(129, Request(1, 0, 2), 0).Status == ActionStatus.Pending, "fresh generation permits new incarnation");
            host.Disconnect(129);
            Check(Throws(() => host.Register(129, 1)), "same-generation handle reuse rejected");
        }
        private static void Reconnect()
        {
            var host = new ActionHost(Session, new Policy()); host.Register(1, 1);
            var old = new ActionClient(Session, new Policy()); Request first = old.Queue(Ability.Guard, 0, 1, 0);
            host.Receive(1, first, 0); host.Step(1); Receipt oldResult = host.Receive(1, first, 10);
            host.Disconnect(1); host.Register(2, 1);
            var current = new ActionClient(Session, new Policy()); Request reused = current.Queue(Ability.Guard, 0, 1, 20);
            Check(first.Same(reused), "probe genuinely reuses full request identity");
            Check(!Apply(oldResult, 1, 2, current) && current.PendingCount == 1, "recipient incarnation prevents old result resolving reused ID");
            Check(host.Receive(1, first, 20).Status == ActionStatus.Cancelled, "old request source retired");
            host.Receive(2, reused, 20); Check(host.Step(2).Length == 1, "new connection emits independent action");
            Check(Apply(host.Receive(2, reused, 30), 2, 2, current), "new confirmation accepted");
        }
        private static void Reset()
        {
            var host = new ActionHost(Session, new Policy()); host.Register(1, 1);
            var client = new ActionClient(Session, new Policy()); Request prior = client.Queue(Ability.Guard, 0, 1, 0);
            host.Receive(1, prior, 0); host.Step(1); Receipt old = host.Receive(1, prior, 10);
            host.ResetGeneration(2); client.ResetGeneration(2);
            Request current = client.Queue(Ability.Guard, 0, 1, 0);
            Check(!Apply(old, 1, 1, client) && client.PendingCount == 1, "in-flight old confirmation rejected");
            Check(host.Receive(1, prior, 20).Reason == "wrong_generation", "in-flight old request rejected");
            Check(host.Step(1).Length == 0, "old generation never emits");
            host.Receive(1, current, 20); Check(host.Step(2).Length == 1, "new generation emits once");
        }
        private static void IndependentIds()
        {
            var host = new ActionHost(Session, new Policy());
            for (ulong i = 1; i <= 8; i++) { host.Register(i, (long)i); host.Receive(i, Request(1), 0); host.Receive(i, Request(1), 1); }
            ConsumedAction[] actions = host.Step(1); var handles = new HashSet<ulong>();
            foreach (ConsumedAction action in actions) handles.Add(action.ConnectionHandle);
            Check(actions.Length == 8 && handles.Count == 8, "identical action IDs on eight connections remain independent");
            Check(host.Step(2).Length == 0, "duplicates produce no later emission");
        }
        private static void ProcessLoss()
        {
            var host = new ActionHost(Session, new Policy()); host.Register(1, 1);
            Request request = Request(1); host.Receive(1, request, 0); Check(host.Step(1).Length == 1, "original process emitted");
            var unsafeRestart = new ActionHost(Session, new Policy()); unsafeRestart.Register(1, 1);
            unsafeRestart.Receive(1, request, 10);
            Check(unsafeRestart.Step(1).Length == 1, "risk reproduced: same-generation process restart forgets consumption");
            var safeRestart = new ActionHost(Session, new Policy(), 2); safeRestart.Register(1, 1);
            Check(safeRestart.Receive(1, request, 10).Reason == "wrong_generation" && safeRestart.Step(1).Length == 0,
                "new generation rejects pre-crash replay");
        }
    }
}
