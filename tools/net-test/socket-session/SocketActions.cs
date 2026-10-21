using System;
using System.Collections.Generic;
using System.Linq;
using LastBell.Network.ActionPrototype;

namespace LastBell.Network.SocketPrototype
{
    public sealed class SocketActionAudit
    {
        public bool Enabled;
        public long Requests, WithheldReceipts, WithheldConsumedGuardReceipts, Consumed, DuplicateConsumptions, InvalidReceipts, Sent, Confirmed, Unknown;
        public string[] ConsumptionKeys = new string[0];
        public string FirstOutcome, SecondOutcome, ThirdOutcome;
        public Dictionary<string, int> Attempts = new Dictionary<string, int>();
    }
    // Fixture-only AP/AR binding. Host socket identity is routing metadata, not cryptographic authentication.
    internal sealed class SocketActionHost
    {
        private readonly ActionHost host;
        private readonly Dictionary<string, int> attempts = new Dictionary<string, int>();
        private readonly HashSet<string> consumed = new HashSet<string>();
        public readonly SocketActionAudit Audit = new SocketActionAudit { Enabled = true };
        public SocketActionHost(Guid session, int generation) { host = new ActionHost(session, new Policy(), generation); }
        public void Register(ulong connection, long player) { host.Register(connection, player); }
        public void StartGeneration(int generation) { host.ResetGeneration(generation); }
        public void Disconnect(ulong connection) { host.Disconnect(connection); }
        public void Receive(ulong connection, byte[] bytes, long now, Channel channel)
        {
            Request request;
            if (!RequestCodec.TryDecode(bytes, out request)) throw new System.IO.InvalidDataException("malformed_action");
            string key = connection + "/" + request.Id; int count;
            attempts.TryGetValue(key, out count); attempts[key] = ++count; Audit.Requests++;
            Audit.Attempts[key] = count;
            Receipt receipt = host.Receive(connection, request, now);
            // Suppress application replies, not TCP packets. ID 1 eventually confirms; ID 2 stays uncertain.
            if (request.Id == 2 || (request.Id == 1 && (count < 4 || receipt.Status == ActionStatus.Pending)))
            {
                Audit.WithheldReceipts++;
                if (request.Id == 1 && receipt.Status == ActionStatus.Consumed) Audit.WithheldConsumedGuardReceipts++;
                return;
            }
            byte[] packet;
            if (!ReceiptCodec.TryEncode(receipt, connection, out packet)) throw new InvalidOperationException("encode action receipt");
            if (request.Id == 3 && count == 1)
            {
                byte[] wrongRecipient;
                if (!ReceiptCodec.TryEncode(receipt, connection + 1000, out wrongRecipient)) throw new InvalidOperationException("probe receipt");
                channel.Send(Op.ActionReceipt, wrongRecipient);
                var malformed = (byte[])packet.Clone(); malformed[2] = 255; channel.Send(Op.ActionReceipt, malformed);
            }
            channel.Send(Op.ActionReceipt, packet, duplicate: request.Id == 3);
        }
        public void Step(long tick)
        {
            foreach (ConsumedAction action in host.Step(tick))
            {
                string key = action.ConnectionHandle + "/" + action.PlayerId + "/" + action.Request.Id;
                if (!consumed.Add(key)) Audit.DuplicateConsumptions++;
                Audit.Consumed++;
            }
            Audit.ConsumptionKeys = consumed.OrderBy(x => x).ToArray();
        }
    }
    internal sealed class SocketActionClient
    {
        private readonly ActionClient client;
        private readonly ulong connection;
        private int queued;
        public readonly SocketActionAudit Audit = new SocketActionAudit { Enabled = true };
        public SocketActionClient(Guid session, int generation, ulong handle)
        { client = new ActionClient(session, new Policy(), generation); connection = handle; }
        public void Tick(long tick, long now)
        {
            while (queued < 3 && tick >= 6 + queued * 12)
            {
                var ability = queued == 0 ? Ability.Guard : queued == 1 ? Ability.Technique : Ability.Signature;
                client.Queue(ability, tick, tick + 3, now); queued++;
            }
        }
        public void Depart(long tick, long now, Channel channel)
        {
            // Queued in the future immediately before Quit; removal must prevent scheduler emission.
            var request = client.Queue(Ability.Guard, tick, tick + 10, now); queued++;
            channel.Send(Op.ActionRequest, RequestCodec.Encode(request)); Audit.Sent++;
        }
        public void Poll(long now, Channel channel)
        {
            foreach (Request request in client.Poll(now))
            { channel.Send(Op.ActionRequest, RequestCodec.Encode(request)); Audit.Sent++; }
        }
        public void Receive(byte[] packet)
        {
            string reason;
            if (!ReceiptIngress.TryApply(packet, 0, 0, connection, client, out reason))
            {
                if (reason != "wrong_recipient" && reason != "malformed_receipt")
                    throw new System.IO.InvalidDataException("action receipt: " + reason);
                Audit.InvalidReceipts++;
            }
        }
        public SocketActionAudit Finish()
        {
            Audit.FirstOutcome = client.Outcome(1) == null ? "Pending" : client.Outcome(1).Status.ToString();
            Audit.SecondOutcome = client.Outcome(2) == null ? "Pending" : client.Outcome(2).Status.ToString();
            Audit.ThirdOutcome = client.Outcome(3) == null ? "Pending" : client.Outcome(3).Status.ToString();
            Audit.Confirmed = 0; Audit.Unknown = 0;
            for (long id = 1; id <= queued; id++)
            {
                Receipt outcome = client.Outcome(id);
                if (outcome == null) continue;
                if (outcome.Status == ActionStatus.Consumed) Audit.Confirmed++;
                else if (outcome.Status == ActionStatus.UnconfirmedTimeout) Audit.Unknown++;
            }
            return Audit;
        }
    }
}
