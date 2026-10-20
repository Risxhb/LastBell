using System;
using System.Collections.Generic;
using LastBell.Network.ActionPrototype;
using LastBell.Network.FaultLab;

namespace LastBell.Network.ActionChecks
{
    public sealed class FaultResult
    {
        public string Name;
        public int Participants, RttMs, Seed;
        public double Loss;
        public long Generated, Consumed, Expired, Unknown, Cancelled, Transmissions, Retries, Duplicates, Dropped;
        public long TechniqueConsumed, SignatureConsumed, GuardConsumed;
        public long InvalidResponses, LateConsumptions, DuplicateConsumptions;
        public long ConfirmationsEncoded, ConfirmationsAccepted;
        public bool Passed;
        public string[] Failures;
    }
    public sealed class Report
    {
        public int Assertions;
        public string[] PassedChecks, Failures;
        public FaultResult[] Cases;
    }
    internal sealed class SimClient
    {
        public ulong Peer;
        public ActionClient Client;
        public readonly List<Request> Created = new List<Request>();
    }

    public static class Runner
    {
        private static readonly Guid Identity = new Guid("3b8a9715-898c-4c70-a7d8-5d747bba624a");
        private static int assertions;
        private static readonly List<string> failures = new List<string>();
        private static readonly List<string> passed = new List<string>();
        private static void Check(bool value, string label) { assertions++; if (!value) failures.Add(label); }
        private static void Case(string name, Action action)
        {
            int before = failures.Count;
            try { action(); } catch (Exception ex) { failures.Add(name + ": " + ex); }
            if (before == failures.Count) passed.Add(name);
        }
        private static Request Req(long id, Ability ability = Ability.Guard, long created = 0,
            long target = 1, long expiry = 12, int generation = 1)
        { return new Request(Identity, generation, id, created, target, expiry, ability); }

        public static Report Run()
        {
            assertions = 0; failures.Clear(); passed.Clear();
            Case("lost-request-retry-and-duplicate-consumption", LostRequest);
            Case("lost-receipt-is-not-new-action", LostReceipt);
            Case("out-of-order-independent-ability-ids", OutOfOrder);
            Case("host-exclusive-expiry-and-pending-reset", Expiry);
            Case("scope-ownership-disconnect-generation", Scope);
            Case("bounded-history-no-reexecution", History);
            Case("rate-backpressure-and-client-bounds", Bounds);
            Case("binary-request-codec", Codec);
            Case("unknown-timeout-and-delayed-confirmation", Timeout);
            var cases = new List<FaultResult>();
            foreach (int count in new[] {2,3,4,5,6,7,8})
                foreach (int rtt in new[] {50,100,150})
                    foreach (double loss in new[] {0.0,0.03,0.10})
                        cases.Add(Simulate(count, rtt, loss, 7000 + count * 100 + rtt + (int)(loss * 100), false, false));
            cases.Add(Simulate(8, 150, 0.10, 9201, true, false));
            cases.Add(Simulate(8, 150, 0.10, 9202, false, true));
            foreach (FaultResult result in cases)
                if (!result.Passed) failures.Add(result.Name + "/" + result.Participants + "/" + result.RttMs + ": " + string.Join("; ", result.Failures));
            return new Report { Assertions = assertions, PassedChecks = passed.ToArray(), Cases = cases.ToArray(), Failures = failures.ToArray() };
        }

        private static void LostRequest()
        {
            var policy = new Policy();
            var host = new ActionHost(Identity, policy); host.Register(501, 2);
            var client = new ActionClient(Identity, policy);
            Request action = client.Queue(Ability.Guard, 0, 1, 0);
            Check(client.Poll(0).Length == 1, "first transmission");
            // Drop the first transmission, send next unchanged ID at retry interval.
            Check(client.Poll(49).Length == 0, "retry pacing");
            Request retry = client.Poll(50)[0];
            Check(retry.Same(action), "retry keeps identity and deadline");
            for (long tick = 1; tick <= 3; tick++) host.Step(tick);
            Receipt pending = host.Receive(501, retry, 50);
            Check(pending.Status == ActionStatus.Pending, "retry admitted");
            Check(host.Receive(501, retry, 51).Status == ActionStatus.Pending, "duplicate pending");
            ConsumedAction[] consumed = host.Step(4);
            Check(consumed.Length == 1 && consumed[0].Request.Id == action.Id && consumed[0].PlayerId == 2, "one authenticated consumption");
            Receipt final = host.Receive(501, retry, 67);
            Check(final.Status == ActionStatus.Consumed && final.HostTick == 4, "consumption receipt");
            client.OnReceipt(final);
            Check(client.PendingCount == 0 && client.Outcome(action.Id).Status == ActionStatus.Consumed, "confirmation stops retry");
            Check(host.Receive(501, retry, 68).Status == ActionStatus.Consumed && host.Step(5).Length == 0, "late duplicate never executes again");
            Check(client.OnReceipt(pending) && client.Outcome(action.Id).Status == ActionStatus.Consumed, "old Pending cannot roll back terminal");
        }
        private static void LostReceipt()
        {
            var p = new Policy(); var host = new ActionHost(Identity, p); host.Register(501, 2);
            var client = new ActionClient(Identity, p);
            Request action = client.Queue(Ability.Technique, 0, 1, 0);
            host.Receive(501, client.Poll(0)[0], 0);
            Check(host.Step(1).Length == 1, "initial technique");
            // Drop result; client repeats same request, never invents another action ID.
            Request retry = client.Poll(50)[0];
            Receipt receipt = host.Receive(501, retry, 50);
            Check(receipt.Status == ActionStatus.Consumed && receipt.Request.Id == action.Id, "cached result after lost ack");
            Check(host.Step(2).Length == 0, "lost receipt does not duplicate action");
            Check(client.OnReceipt(receipt) && client.Poll(100).Length == 0, "retry stops on result");
        }
        private static void OutOfOrder()
        {
            var p = new Policy(); var host = new ActionHost(Identity, p); host.Register(501, 2);
            var client = new ActionClient(Identity, p);
            Request first = client.Queue(Ability.Guard, 0, 1, 0);
            Request second = client.Queue(Ability.Signature, 0, 1, 0);
            Check(host.Receive(501, second, 1).Status == ActionStatus.Pending, "newer action first");
            Check(host.Step(1).Length == 1, "signature consumed");
            Check(host.Receive(501, first, 17).Status == ActionStatus.Pending, "older unreceived Guard still admitted");
            ConsumedAction[] actions = host.Step(2);
            Check(actions.Length == 1 && actions[0].Request.Ability == Ability.Guard, "Guard survives newer action receipt");
            var conflict = new Request(Identity, 1, first.Id, 0, 1, 12, Ability.Technique);
            Check(host.Receive(501, conflict, 18).Reason == "conflicting_id", "same ID cannot change ability");
            Check(host.Step(3).Length == 0, "conflict no extra consumption");
        }
        private static void Expiry()
        {
            var p = new Policy(); var host = new ActionHost(Identity, p); host.Register(501, 2);
            for (int t = 1; t <= 11; t++) host.Step(t);
            Check(host.Receive(501, Req(1), 190).Status == ActionStatus.Expired, "no execution at exclusive expiry tick");
            Check(host.Step(12).Length == 0, "expired Guard not emitted");
            Check(host.Receive(501, Req(1), 210).Status == ActionStatus.Expired, "expired receipt remains expired");
            var lastTimely = new ActionHost(Identity, p); lastTimely.Register(501, 2);
            for (int t = 1; t <= 10; t++) lastTimely.Step(t);
            Check(lastTimely.Receive(501, Req(1), 170).Status == ActionStatus.Pending && lastTimely.Step(11).Length == 1, "last eligible tick accepted");
            lastTimely.Step(12);
            Check(lastTimely.Receive(501, Req(1), 210).Status == ActionStatus.Consumed, "receipt after deadline retains actual consumed result");
            var queued = new ActionHost(Identity, p); queued.Register(501, 2);
            Check(queued.Receive(501, Req(1, target: 5, expiry: 6), 0).Status == ActionStatus.Pending, "future request queued");
            for (int t = 1; t < 5; t++) Check(queued.Step(t).Length == 0, "wait for target");
            Check(queued.Step(5).Length == 1, "consume before deadline");
            Check(queued.Receive(501, Req(2, target: 6, expiry: 6), 90).Status == ActionStatus.Rejected, "target at expiry invalid");
            Check(queued.Receive(501, Req(2, created: 5, target: 6, expiry: 100), 90).Status == ActionStatus.Rejected, "client cannot extend host lifetime");
            var pending = new ActionHost(Identity, p); pending.Register(501, 2);
            Check(pending.Receive(501, Req(1, created: 0, target: 1, expiry: 2), 0).Status == ActionStatus.Pending, "short valid lifetime");
            // Future generation reset discards queued work instead of firing it late.
            pending.ResetGeneration(2);
            Check(pending.Step(1).Length == 0, "reset cancels pending");
        }
        private static void Scope()
        {
            var p = new Policy(); var host = new ActionHost(Identity, p); host.Register(501, 2); host.Register(502, 3);
            Request action = Req(1);
            Check(host.Receive(999, action, 0).Status == ActionStatus.Cancelled, "unknown peer");
            var wrong = new Request(Guid.NewGuid(), 1, 1, 0, 1, 12, Ability.Guard);
            Check(host.Receive(501, wrong, 0).Reason == "wrong_session", "session scope");
            Check(host.Receive(501, Req(1, generation: 0), 0).Reason == "wrong_generation", "generation scope");
            Check(host.Receive(501, action, 0).Status == ActionStatus.Pending, "peer one action 1");
            Check(host.Receive(502, action, 0).Status == ActionStatus.Pending, "peer two own action 1");
            host.Disconnect(501);
            ConsumedAction[] consumed = host.Step(1);
            Check(consumed.Length == 1 && consumed[0].PlayerId == 3, "disconnect clears own pending only");
            Check(host.Receive(501, action, 17).Status == ActionStatus.Cancelled, "late disconnected request");
            host.Register(503, 2);
            Check(host.Receive(503, action, 17).Status == ActionStatus.Pending, "fresh connection may restart local action IDs");
            ConsumedAction[] fresh = host.Step(2);
            Check(fresh.Length == 1 && fresh[0].PlayerId == 2 && fresh[0].ConnectionHandle == 503, "consumption retains connection incarnation");
            Check(host.Receive(501, action, 34).Status == ActionStatus.Cancelled, "old incarnation cannot control returned actor");
            bool reused = false; try { host.Register(501, 4); } catch (InvalidOperationException) { reused = true; }
            Check(reused, "retired connection handle cannot be reused within generation");
            host.ResetGeneration(2);
            host.Register(501, 4);
            Check(host.Receive(501, action, 18).Reason == "wrong_generation", "reset permits handle reuse but rejects stale request");
            Check(host.Receive(502, action, 18).Reason == "wrong_generation", "old request cannot cross reset");
            var client = new ActionClient(Identity, p); client.Queue(Ability.Guard, 0, 1, 0); client.ResetGeneration(2);
            Check(client.PendingCount == 0 && client.Queue(Ability.Guard, 0, 1, 0).Id == 1, "new generation restarts action IDs");
            Check(!client.OnReceipt(new Receipt(action, ActionStatus.Consumed, 1)), "old receipt rejected");
        }
        private static void History()
        {
            var p = new Policy(); var host = new ActionHost(Identity, p); host.Register(501, 2);
            for (int id = 1; id <= 100; id++)
            {
                long created = host.Tick;
                var request = Req(id, created: created, target: created + 1, expiry: created + 12);
                Check(host.Receive(501, request, id * 20L).Status == ActionStatus.Pending, "history request");
                Check(host.Step(created + 1).Length == 1, "history one consumption");
            }
            Check(host.TicketCount(501) == 64, "bounded terminal results");
            Check(host.Receive(501, Req(1), 2100).Status == ActionStatus.HistoryUnavailable, "evicted result ambiguous, never replayed");
            Check(host.Step(101).Length == 0, "ancient action not executed");
            Check(host.Receive(501, Req(0), 2100).Reason == "invalid_id", "nonpositive id");
            Check(host.Receive(501, Req(1000, created: 101, target: 102, expiry: 113), 2100).Reason == "invalid_id", "bounded ID jump");
        }
        private static void Bounds()
        {
            var p = new Policy(); var host = new ActionHost(Identity, p); host.Register(501, 2);
            Request r = Req(1);
            for (int i = 0; i < 120; i++) Check(host.Receive(501, r, 0).Status == ActionStatus.Pending, "request rate budget");
            Check(host.Receive(501, r, 999).Status == ActionStatus.RetryLater, "rate limit is nonterminal backpressure");
            Check(host.Receive(501, r, 1000).Status == ActionStatus.Pending, "rate recovery");
            Check(host.Receive(501, Req(2, created: 99, target: 100, expiry: 111), 1000).Status == ActionStatus.Rejected, "future clock bound");
            var client = new ActionClient(Identity, p);
            for (int i = 0; i < 32; i++) client.Queue(Ability.Guard, 0, 1, 0);
            bool threw = false; try { client.Queue(Ability.Guard, 0, 1, 0); } catch (InvalidOperationException) { threw = true; }
            Check(threw && client.PendingCount == 32, "bounded client outstanding");
            var pendingHost = new ActionHost(Identity, p); pendingHost.Register(501, 2);
            foreach (Request request in client.Poll(0)) pendingHost.Receive(501, request, 0);
            Check(pendingHost.Receive(501, Req(33), 0).Reason == "pending_full", "bounded host pending");
        }
        private static void Codec()
        {
            Request original = Req(7, Ability.Technique, expiry: 30), decoded;
            byte[] bytes = RequestCodec.Encode(original);
            Check(bytes.Length == 56 && RequestCodec.TryDecode(bytes, out decoded) && decoded.Same(original), "request codec roundtrip");
            Check(!RequestCodec.TryDecode(null, out decoded) && !RequestCodec.TryDecode(new byte[57], out decoded), "size allowlist");
            bytes[0] = 0; Check(!RequestCodec.TryDecode(bytes, out decoded), "type header isolation");
            bytes = RequestCodec.Encode(original); bytes[2] = 2; Check(!RequestCodec.TryDecode(bytes, out decoded), "version isolation");
            bytes = RequestCodec.Encode(original); bytes[55] = 255; Check(!RequestCodec.TryDecode(bytes, out decoded), "ability allowlist");
        }
        private static void Timeout()
        {
            var p = new Policy(); var client = new ActionClient(Identity, p);
            Request action = client.Queue(Ability.Guard, 0, 1, 0);
            client.Poll(0); Check(client.Poll(2000).Length == 0, "bounded retry horizon");
            Check(client.Outcome(action.Id).Status == ActionStatus.UnconfirmedTimeout, "timeout does not assert failure/expiry");
            var late = new Receipt(action, ActionStatus.Consumed, 2);
            Check(client.OnReceipt(late) && client.Outcome(action.Id).Status == ActionStatus.Consumed, "late authoritative receipt resolves uncertainty");
            for (int id = 2; id <= 100; id++)
            {
                Request next = client.Queue(Ability.Guard, 0, 1, 2000);
                client.OnReceipt(new Receipt(next, ActionStatus.Consumed, 1));
            }
            Check(client.CompletedCount == 64 && client.Outcome(action.Id) == null, "bounded client result history");
            Check(!client.OnReceipt(new Receipt(action, (ActionStatus)999, 2)), "invalid status");
        }

        private static FaultResult Simulate(int count, int rtt, double loss, int seed, bool blackout, bool dropReceipts)
        {
            var policy = new Policy(); var host = new ActionHost(Identity, policy);
            var link = new FaultLink(rtt, 20, loss, (uint)seed);
            var clients = new List<SimClient>();
            var errors = new List<string>();
            var consumed = new HashSet<string>();
            var sentIds = new HashSet<string>();
            var result = new FaultResult { Name = blackout ? "blackout-expiry" : dropReceipts ? "receipt-blackout-recovery" : "matrix",
                Participants = count, RttMs = rtt, Loss = loss, Seed = seed };
            for (int i = 0; i < count; i++)
            {
                ulong peer = i == 0 ? 0UL : (ulong)(500 + i);
                host.Register(peer, i + 1);
                clients.Add(new SimClient { Peer = peer, Client = new ActionClient(Identity, policy) });
            }
            if (blackout) { link.BlackoutPeer = clients[count - 1].Peer; link.BlackoutStart = 1000; link.BlackoutEnd = 1800; }
            for (int step = 0; step < 840; step++)
            {
                link.RunUntil(step * 1000L / 60);
                foreach (SimClient client in clients)
                {
                    if (step < 600 && step % 30 == 10)
                    {
                        Ability ability = (Ability)(1 + (step / 30) % 3);
                        // Exact shared fixture clock; real clock estimation is a U3 integration requirement.
                        Request action = client.Client.Queue(ability, host.Tick, host.Tick + 1, link.NowMs);
                        client.Created.Add(action); result.Generated++;
                    }
                    foreach (Request request in client.Client.Poll(link.NowMs))
                    {
                        string key = client.Peer + "/" + request.Id;
                        result.Transmissions++; if (!sentIds.Add(key)) result.Retries++;
                        byte[] wire = RequestCodec.Encode(request);
                        Action receive = () =>
                        {
                            Request decoded;
                            if (!RequestCodec.TryDecode(wire, out decoded)) { errors.Add("codec"); return; }
                            Receipt receipt = host.Receive(client.Peer, decoded, link.NowMs);
                            byte[] receiptWire;
                            if (!ReceiptCodec.TryEncode(receipt, client.Peer, out receiptWire))
                            { errors.Add("host confirmation encoding failed"); return; }
                            result.ConfirmationsEncoded++;
                            Action confirm = () =>
                            {
                                string reason;
                                if (!ReceiptIngress.TryApply(receiptWire, 0, 0, client.Peer, client.Client, out reason))
                                { result.InvalidResponses++; errors.Add("confirmation ingress: " + reason); }
                                else result.ConfirmationsAccepted++;
                            };
                            if (dropReceipts && client.Peer == clients[count - 1].Peer && link.NowMs >= 1000 && link.NowMs < 1800) return;
                            if (client.Peer == 0) confirm(); else link.Send(client.Peer, confirm);
                        };
                        if (client.Peer == 0) receive(); else link.Send(client.Peer, receive);
                    }
                }
                foreach (ConsumedAction action in host.Step(step + 1))
                {
                    string key = action.PlayerId + "/" + action.Request.Id;
                    if (!consumed.Add(key)) result.DuplicateConsumptions++;
                    if (action.Tick >= action.Request.ExpiryTick) result.LateConsumptions++;
                    result.Consumed++;
                    switch (action.Request.Ability)
                    {
                        case Ability.Technique: result.TechniqueConsumed++; break;
                        case Ability.Signature: result.SignatureConsumed++; break;
                        case Ability.Guard: result.GuardConsumed++; break;
                    }
                }
            }
            link.RunUntil(14300);
            foreach (SimClient client in clients)
            {
                if (client.Client.PendingCount != 0) errors.Add("unconfirmed pending after drain");
                foreach (Request request in client.Created)
                {
                    Receipt receipt = client.Client.Outcome(request.Id);
                    if (receipt == null) { errors.Add("missing outcome"); continue; }
                    switch (receipt.Status)
                    {
                        case ActionStatus.Consumed:
                            if (!consumed.Contains((client.Peer == 0 ? 1 : (long)client.Peer - 499) + "/" + request.Id))
                                errors.Add("consumed receipt without emission");
                            break;
                        case ActionStatus.Expired:
                            result.Expired++;
                            if (consumed.Contains((client.Peer == 0 ? 1 : (long)client.Peer - 499) + "/" + request.Id)) errors.Add("expired result for consumed action");
                            break;
                        case ActionStatus.UnconfirmedTimeout: result.Unknown++; break;
                        case ActionStatus.Cancelled: result.Cancelled++; break;
                        default: errors.Add("unexpected terminal: " + receipt.Status + "/" + receipt.Reason); break;
                    }
                }
            }
            if (result.InvalidResponses != 0) errors.Add("invalid authoritative receipt");
            if (result.DuplicateConsumptions != 0 || result.LateConsumptions != 0) errors.Add("unsafe consumption");
            if (result.Consumed + result.Expired + result.Unknown + result.Cancelled != result.Generated) errors.Add("outcome accounting");
            if (result.Unknown != 0) errors.Add("seeded cases should confirm after drain");
            if (blackout && result.Expired == 0) errors.Add("blackout did not exercise expiry");
            if (dropReceipts && result.Retries == 0) errors.Add("receipt probe no retry");
            result.Duplicates = link.Duplicated; result.Dropped = link.Dropped;
            result.Failures = errors.ToArray(); result.Passed = errors.Count == 0;
            return result;
        }
    }
}
