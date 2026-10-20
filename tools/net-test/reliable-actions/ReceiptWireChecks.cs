using System;
using System.Collections.Generic;
using LastBell.Network.ActionPrototype;

namespace LastBell.Network.ReceiptChecks
{
    public sealed class WireReport
    {
        public int Assertions;
        public string[] Checks, Failures;
    }
    public static class Runner
    {
        private static readonly Guid Identity = new Guid("01020304-0506-0708-090a-0b0c0d0e0f10");
        private static int assertions;
        private static readonly List<string> failures = new List<string>();
        private static readonly List<string> checks = new List<string>();
        private static void Expect(bool value, string label) { assertions++; if (!value) failures.Add(label); }
        private static void Scenario(string name, Action run)
        {
            int before = failures.Count;
            try { run(); } catch (Exception ex) { failures.Add(name + ": " + ex); }
            if (before == failures.Count) checks.Add(name);
        }
        private static Request Request(int generation = 1, long id = 1, Guid? session = null,
            Ability ability = Ability.Guard, long created = 0, long target = 1, long expiry = 12)
        { return new Request(session ?? Identity, generation, id, created, target, expiry, ability); }
        private static byte[] Packet(Receipt receipt, ulong recipient = 501)
        {
            byte[] bytes;
            if (!ReceiptCodec.TryEncode(receipt, recipient, out bytes)) throw new InvalidOperationException("Fixture encode");
            return bytes;
        }
        private static ActionClient Client(out Request request)
        {
            var client = new ActionClient(Identity, new Policy());
            request = client.Queue(Ability.Guard, 0, 1, 0);
            return client;
        }
        private static bool Apply(byte[] packet, ActionClient client, out string reason, ulong sender = 0, ulong recipient = 501)
        { return ReceiptIngress.TryApply(packet, sender, 0, recipient, client, out reason); }
        private static void WriteLong(byte[] bytes, int offset, long value)
        { Array.Copy(BitConverter.GetBytes(value), 0, bytes, offset, 8); }
        public static WireReport Run()
        {
            assertions = 0; checks.Clear(); failures.Clear();
            Scenario("all-host-status-reason-roundtrips", RoundTrips);
            Scenario("fixed-v1-golden-vector", Golden);
            Scenario("malformed-framing-and-semantic-rejection", Malformed);
            Scenario("sender-recipient-session-generation-request-validation", IdentityChecks);
            Scenario("replay-stale-pending-and-conflicting-outcomes", Replay);
            Scenario("unknown-timeout-late-wire-confirmation", UnknownTimeout);
            Scenario("host-produced-replies-and-encode-failures", HostReplies);
            Scenario("bounded-random-input-parser-no-throw", RandomInput);
            return new WireReport { Assertions = assertions, Checks = checks.ToArray(), Failures = failures.ToArray() };
        }

        private static void RoundTrips()
        {
            var receipts = new List<Receipt> {
                new Receipt(Request(), ActionStatus.Pending, 0),
                new Receipt(Request(), ActionStatus.Consumed, 1),
                new Receipt(Request(), ActionStatus.Expired, 11, "deadline"),
                new Receipt(Request(), ActionStatus.Cancelled, 0, "unknown_sender"),
                new Receipt(Request(), ActionStatus.HistoryUnavailable, 99, "history_unavailable")
            };
            foreach (string reason in new[] {"wrong_session","wrong_generation","conflicting_id","invalid_id","invalid_timing_or_ability"})
                receipts.Add(new Receipt(Request(), ActionStatus.Rejected, 0, reason));
            foreach (string reason in new[] {"invalid_clock","rate_limited","pending_full"})
                receipts.Add(new Receipt(Request(), ActionStatus.RetryLater, 0, reason));
            var statusCodes = new Dictionary<ActionStatus, byte> {
                {ActionStatus.Pending,0}, {ActionStatus.Consumed,1}, {ActionStatus.Expired,2}, {ActionStatus.Rejected,3},
                {ActionStatus.RetryLater,4}, {ActionStatus.Cancelled,5}, {ActionStatus.HistoryUnavailable,6}
            };
            var reasonCodes = new Dictionary<string, byte> {
                {"",0}, {"deadline",1}, {"unknown_sender",2}, {"wrong_session",3}, {"wrong_generation",4},
                {"invalid_clock",5}, {"rate_limited",6}, {"conflicting_id",7}, {"invalid_id",8},
                {"history_unavailable",9}, {"invalid_timing_or_ability",10}, {"pending_full",11}
            };
            foreach (Receipt receipt in receipts)
            {
                byte[] bytes = Packet(receipt);
                Expect(bytes[67] == statusCodes[receipt.Status], "frozen v1 status code");
                Expect(bytes[68] == reasonCodes[receipt.Reason], "frozen v1 reason code");
                ReceiptMessage message;
                Expect(bytes.Length == 77 && ReceiptCodec.TryDecode(bytes, out message), "roundtrip frame");
                ReceiptCodec.TryDecode(bytes, out message);
                Expect(message.RecipientConnection == 501 && message.Receipt.Request.Same(receipt.Request) &&
                    message.Receipt.Status == receipt.Status && message.Receipt.Reason == receipt.Reason &&
                    message.Receipt.HostTick == receipt.HostTick && message.Receipt.Terminal == receipt.Terminal, "roundtrip semantics");
            }
        }
        private static void Golden()
        {
            byte[] encoded = Packet(new Receipt(Request(), ActionStatus.Consumed, 2));
            // Explicit little-endian recipient, Guid ordering, AP v1 request, status/reason and host tick.
            string expected = "415201" + "f501000000000000" + "415001" +
                "0403020106050807090a0b0c0d0e0f10" + "01000000" +
                "0100000000000000" + "0000000000000000" + "0100000000000000" +
                "0c00000000000000" + "03" + "0100" + "0200000000000000";
            Expect(BitConverter.ToString(encoded).Replace("-", "").ToLowerInvariant() == expected, "fixed receipt golden bytes");
            var golden = new byte[expected.Length / 2];
            for (int i = 0; i < golden.Length; i++) golden[i] = Convert.ToByte(expected.Substring(i * 2, 2), 16);
            ReceiptMessage message;
            Expect(ReceiptCodec.TryDecode(golden, out message) && message.Receipt.HostTick == 2 &&
                message.Receipt.Status == ActionStatus.Consumed && message.RecipientConnection == 501, "decode independent golden");
        }
        private static void Malformed()
        {
            Request request; ActionClient client = Client(out request);
            byte[] good = Packet(new Receipt(request, ActionStatus.Consumed, 2));
            var bad = new List<byte[]> {null, new byte[78], new byte[10000]};
            for (int length = 0; length < ReceiptCodec.ByteCount; length++)
            { var prefix = new byte[length]; Array.Copy(good, prefix, length); bad.Add(prefix); }
            Action<int, byte> byteMutation = (offset, value) => { var bytes = (byte[])good.Clone(); bytes[offset] = value; bad.Add(bytes); };
            Action<int, long> longMutation = (offset, value) => { var bytes = (byte[])good.Clone(); WriteLong(bytes, offset, value); bad.Add(bytes); };
            byteMutation(0, 0); byteMutation(1, 0); byteMutation(2, 2); // AR header/version
            byteMutation(11, 0); byteMutation(13, 2); // nested AP header/version
            var emptySession = (byte[])good.Clone(); Array.Clear(emptySession, 14, 16); bad.Add(emptySession);
            var negativeGeneration = (byte[])good.Clone();
            for (int i = 30; i < 34; i++) negativeGeneration[i] = 255;
            bad.Add(negativeGeneration);
            longMutation(34, 0); longMutation(34, -1); // action ID
            longMutation(42, -1); longMutation(42, long.MaxValue); // creation
            longMutation(50, -1); longMutation(50, 5); // target backward or after consumed tick
            longMutation(58, 1); longMutation(58, 100); // deadline at target or excessive lifetime
            byteMutation(66, 0); byteMutation(66, 255); // ability
            byteMutation(67, 255); byteMutation(67, (byte)ActionStatus.UnconfirmedTimeout);
            byteMutation(68, 255); byteMutation(68, 1); // reason mismatch for Consumed
            longMutation(69, -1); longMutation(69, 0); longMutation(69, 12); // impossible consumption ticks
            var earlyExpired = (byte[])good.Clone(); earlyExpired[67] = (byte)ActionStatus.Expired; earlyExpired[68] = 1; bad.Add(earlyExpired);
            var futurePending = (byte[])good.Clone(); futurePending[67] = (byte)ActionStatus.Pending;
            WriteLong(futurePending, 42, 7); WriteLong(futurePending, 50, 8); WriteLong(futurePending, 58, 19); WriteLong(futurePending, 69, 0); bad.Add(futurePending);
            var latePending = (byte[])good.Clone(); latePending[67] = (byte)ActionStatus.Pending; WriteLong(latePending, 69, 11); bad.Add(latePending);
            foreach (byte[] packet in bad)
            {
                ReceiptMessage parsed;
                Expect(!ReceiptCodec.TryDecode(packet, out parsed) && parsed == null, "malformed packet rejected");
                string reason;
                Expect(!Apply(packet, client, out reason) && reason == "malformed_receipt", "malformed ingress rejected");
                Expect(client.PendingCount == 1 && client.Outcome(request.Id) == null, "malformed packet has no state effect");
            }
            byte[] output;
            Expect(!ReceiptCodec.TryEncode(null, 501, out output) && output == null, "null encode rejected");
            Expect(!ReceiptCodec.TryEncode(new Receipt(request, ActionStatus.UnconfirmedTimeout, -1, "outcome_unknown"), 501, out output), "local timeout not a wire result");
            Expect(!ReceiptCodec.TryEncode(new Receipt(request, ActionStatus.Consumed, 2, "deadline"), 501, out output), "reason/status encoder mismatch");
            Expect(!ReceiptCodec.TryEncode(new Receipt(request, ActionStatus.Rejected, 2, new string('x', 100000)), 501, out output), "unbounded reason not serialized");
        }
        private static void IdentityChecks()
        {
            Request request; ActionClient client = Client(out request);
            byte[] good = Packet(new Receipt(request, ActionStatus.Consumed, 2));
            string reason;
            Expect(!Apply(good, client, out reason, sender: 502) && reason == "unauthorized_host", "authenticated sender required");
            Expect(!Apply(null, client, out reason, sender: 502) && reason == "unauthorized_host", "untrusted sender rejected before parsing");
            Expect(!Apply(Packet(new Receipt(request, ActionStatus.Consumed, 2), 502), client, out reason) &&
                reason == "wrong_recipient", "recipient connection required");
            var mismatches = new[] {
                Request(session: Guid.NewGuid()), Request(generation: 0), Request(generation: 2),
                Request(id: 2), Request(ability: Ability.Signature), Request(created: 1),
                Request(target: 2), Request(expiry: 13)
            };
            foreach (Request mismatch in mismatches)
            {
                byte[] packet = Packet(new Receipt(mismatch, ActionStatus.Consumed, 2));
                Expect(!Apply(packet, client, out reason) && reason == "unknown_or_mismatched_request", "full request fingerprint");
                Expect(client.PendingCount == 1 && client.Outcome(request.Id) == null, "mismatched identity cannot confirm");
            }
            // Another client can have identical request fields. Connection routing still distinguishes it.
            Request other; ActionClient otherClient = Client(out other);
            Expect(other.Same(request) && !Apply(Packet(new Receipt(other, ActionStatus.Consumed, 2), 502), otherClient, out reason), "identical per-client IDs do not bypass recipient");
            Expect(client.PendingCount == 1, "sender/recipient rejection leaves pending unchanged");
            client.ResetGeneration(2);
            Request current = client.Queue(Ability.Guard, 0, 1, 0);
            Expect(!Apply(good, client, out reason) && client.PendingCount == 1 && client.Outcome(current.Id) == null, "old generation packet cannot confirm reused ID");
        }
        private static void Replay()
        {
            Request request; ActionClient client = Client(out request);
            byte[] consumed = Packet(new Receipt(request, ActionStatus.Consumed, 2));
            string reason;
            Expect(Apply(consumed, client, out reason) && client.PendingCount == 0, "valid wire confirmation applied");
            Expect(Apply(consumed, client, out reason) && client.CompletedCount == 1, "duplicate receipt idempotent");
            Expect(Apply(Packet(new Receipt(request, ActionStatus.Pending, 0)), client, out reason) &&
                client.Outcome(request.Id).Status == ActionStatus.Consumed, "stale Pending cannot resurrect retry");
            Expect(!Apply(Packet(new Receipt(request, ActionStatus.Expired, 11, "deadline")), client, out reason) &&
                reason == "conflicting_terminal_receipt" && client.Outcome(request.Id).Status == ActionStatus.Consumed, "conflicting expiry rejected");
            Expect(!Apply(Packet(new Receipt(request, ActionStatus.Consumed, 3)), client, out reason) &&
                reason == "conflicting_terminal_receipt" && client.Outcome(request.Id).HostTick == 2, "changed resolve tick rejected");
            Expect(Apply(Packet(new Receipt(request, ActionStatus.HistoryUnavailable, 100, "history_unavailable")), client, out reason) &&
                client.Outcome(request.Id).Status == ActionStatus.Consumed, "less definitive response cannot erase known result");
            Request second; ActionClient expiredClient = Client(out second);
            Expect(Apply(Packet(new Receipt(second, ActionStatus.Expired, 11, "deadline")), expiredClient, out reason), "valid expiry accepted");
            Expect(!Apply(consumed, expiredClient, out reason) && expiredClient.Outcome(second.Id).Status == ActionStatus.Expired, "expired action cannot become consumed");
        }
        private static void UnknownTimeout()
        {
            Request request; ActionClient client = Client(out request);
            client.Poll(0); client.Poll(2000);
            Expect(client.Outcome(request.Id).Status == ActionStatus.UnconfirmedTimeout, "local uncertainty");
            string reason;
            Expect(Apply(Packet(new Receipt(request, ActionStatus.Consumed, 2)), client, out reason) &&
                client.Outcome(request.Id).Status == ActionStatus.Consumed, "late valid wire receipt resolves unknown");
        }
        private static void HostReplies()
        {
            var host = new ActionHost(Identity, new Policy()); host.Register(501, 2);
            Request request = Request(); ReceiptMessage decoded;
            var replies = new List<Receipt> {
                host.Receive(501, request, 0),
                host.Receive(502, request, 0),
                host.Receive(501, Request(session: Guid.NewGuid()), 0),
                host.Receive(501, Request(generation: 2), 0),
                host.Receive(501, Request(ability: Ability.Technique), 0),
                host.Receive(501, Request(id: 1000), 0)
            };
            host.Step(1); replies.Add(host.Receive(501, request, 17));
            foreach (Receipt reply in replies)
                Expect(ReceiptCodec.TryDecode(Packet(reply), out decoded) && decoded.Receipt.Reason == reply.Reason, "host-produced reply serializes");
            byte[] output;
            Receipt invalidIdentity = host.Receive(501, Request(id: 0), 18);
            Expect(!ReceiptCodec.TryEncode(invalidIdentity, 501, out output), "malformed original identity needs protocol error/drop path");
        }
        private static void RandomInput()
        {
            uint seed = 0x193b61a5;
            for (int sample = 0; sample < 2048; sample++)
            {
                seed = unchecked(seed * 1664525 + 1013904223);
                int length = sample % 2 == 0 ? 77 : (int)(seed % 100);
                var bytes = new byte[length];
                for (int i = 0; i < length; i++) { seed = unchecked(seed * 1664525 + 1013904223); bytes[i] = (byte)(seed >> 24); }
                ReceiptMessage result;
                Expect(!ReceiptCodec.TryDecode(bytes, out result) && result == null, "seeded random malformed data rejected without throwing");
            }
        }
    }
}
