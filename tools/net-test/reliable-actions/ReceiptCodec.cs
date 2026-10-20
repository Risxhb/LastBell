using System;
using System.IO;

namespace LastBell.Network.ActionPrototype
{
    public sealed class ReceiptMessage
    {
        public readonly ulong RecipientConnection;
        public readonly Receipt Receipt;
        public ReceiptMessage(ulong recipient, Receipt receipt) { RecipientConnection = recipient; Receipt = receipt; }
    }

    // Tools-only wire candidate: AR/version 1 + recipient + AP request + status/reason/tick.
    // Parsing does not establish authenticity. Sender identity comes from the transport boundary.
    public static class ReceiptCodec
    {
        public const int ByteCount = 77;
        private static readonly string[] Reasons = {
            "", "deadline", "unknown_sender", "wrong_session", "wrong_generation", "invalid_clock",
            "rate_limited", "conflicting_id", "invalid_id", "history_unavailable",
            "invalid_timing_or_ability", "pending_full"
        };

        public static bool TryEncode(Receipt receipt, ulong recipientConnection, out byte[] bytes)
        {
            bytes = null;
            int reason = ReasonCode(receipt == null ? null : receipt.Reason);
            if (!Valid(receipt, reason)) return false;
            using (var stream = new MemoryStream(ByteCount))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((byte)'A'); writer.Write((byte)'R'); writer.Write((byte)1);
                writer.Write(recipientConnection);
                writer.Write(RequestCodec.Encode(receipt.Request));
                writer.Write((byte)StatusCode(receipt.Status)); writer.Write((byte)reason); writer.Write(receipt.HostTick);
                bytes = stream.ToArray();
                return true;
            }
        }

        public static bool TryDecode(byte[] bytes, out ReceiptMessage message)
        {
            message = null;
            if (bytes == null || bytes.Length != ByteCount) return false;
            using (var reader = new BinaryReader(new MemoryStream(bytes, false)))
            {
                if (reader.ReadByte() != (byte)'A' || reader.ReadByte() != (byte)'R' || reader.ReadByte() != 1) return false;
                ulong recipient = reader.ReadUInt64();
                Request request;
                if (!RequestCodec.TryDecode(reader.ReadBytes(RequestCodec.ByteCount), out request)) return false;
                ActionStatus status;
                if (!TryStatus(reader.ReadByte(), out status)) return false;
                int reason = reader.ReadByte();
                long tick = reader.ReadInt64();
                if (reason >= Reasons.Length) return false;
                var receipt = new Receipt(request, status, tick, Reasons[reason]);
                if (!Valid(receipt, reason)) return false;
                message = new ReceiptMessage(recipient, receipt);
                return true;
            }
        }

        // Version-1 codes are independent of the local enum's underlying numeric values.
        private static int StatusCode(ActionStatus status)
        {
            switch (status)
            {
                case ActionStatus.Pending: return 0;
                case ActionStatus.Consumed: return 1;
                case ActionStatus.Expired: return 2;
                case ActionStatus.Rejected: return 3;
                case ActionStatus.RetryLater: return 4;
                case ActionStatus.Cancelled: return 5;
                case ActionStatus.HistoryUnavailable: return 6;
                default: return -1;
            }
        }
        private static bool TryStatus(int code, out ActionStatus status)
        {
            status = ActionStatus.Pending;
            switch (code)
            {
                case 0: status = ActionStatus.Pending; return true;
                case 1: status = ActionStatus.Consumed; return true;
                case 2: status = ActionStatus.Expired; return true;
                case 3: status = ActionStatus.Rejected; return true;
                case 4: status = ActionStatus.RetryLater; return true;
                case 5: status = ActionStatus.Cancelled; return true;
                case 6: status = ActionStatus.HistoryUnavailable; return true;
                default: return false;
            }
        }

        private static int ReasonCode(string reason)
        {
            for (int i = 0; i < Reasons.Length; i++) if (string.Equals(reason, Reasons[i], StringComparison.Ordinal)) return i;
            return -1;
        }

        private static bool Valid(Receipt receipt, int reason)
        {
            if (receipt == null || receipt.Request == null || reason < 0 || reason >= Reasons.Length || receipt.HostTick < 0) return false;
            Request r = receipt.Request;
            // Confirmations match canonical requests a legitimate ActionClient can create.
            // Invalid raw request identities use a separate protocol-error/drop path, not a confirmation.
            if (r.Session == Guid.Empty || r.Generation < 0 || r.Id <= 0 || !Policy.Known(r.Ability) ||
                r.CreatedTick < 0 || r.CreatedTick > long.MaxValue - 60 || r.TargetTick < r.CreatedTick ||
                r.TargetTick - r.CreatedTick > 12 || r.ExpiryTick <= r.TargetTick || r.ExpiryTick - r.CreatedTick > 60)
                return false;
            switch (receipt.Status)
            {
                case ActionStatus.Pending:
                    return reason == 0 && receipt.HostTick < r.ExpiryTick - 1 &&
                        (r.CreatedTick <= receipt.HostTick || r.CreatedTick - receipt.HostTick <= 6);
                case ActionStatus.Consumed:
                    return reason == 0 && receipt.HostTick > 0 &&
                        receipt.HostTick >= r.TargetTick && receipt.HostTick < r.ExpiryTick;
                case ActionStatus.Expired:
                    return reason == 1 && receipt.HostTick >= r.ExpiryTick - 1;
                case ActionStatus.Rejected:
                    return reason == 3 || reason == 4 || reason == 7 || reason == 8 || reason == 10;
                case ActionStatus.RetryLater:
                    return reason == 5 || reason == 6 || reason == 11;
                case ActionStatus.Cancelled:
                    return reason == 2;
                case ActionStatus.HistoryUnavailable:
                    return reason == 9;
                default:
                    // UnconfirmedTimeout is client-local and may never arrive from the wire.
                    return false;
            }
        }
    }

    public static class ReceiptIngress
    {
        // All handles here are authenticated adapter metadata, never a claimed sender in the payload.
        public static bool TryApply(byte[] bytes, ulong authenticatedSender, ulong trustedHost,
            ulong localConnection, ActionClient client, out string reason)
        {
            reason = "";
            if (client == null) { reason = "no_client"; return false; }
            if (authenticatedSender != trustedHost) { reason = "unauthorized_host"; return false; }
            ReceiptMessage message;
            if (!ReceiptCodec.TryDecode(bytes, out message)) { reason = "malformed_receipt"; return false; }
            if (message.RecipientConnection != localConnection) { reason = "wrong_recipient"; return false; }
            Receipt known = client.Outcome(message.Receipt.Request.Id);
            if (known != null && known.Request.Same(message.Receipt.Request) &&
                (known.Status == ActionStatus.Consumed || known.Status == ActionStatus.Expired) &&
                (message.Receipt.Status == ActionStatus.Consumed || message.Receipt.Status == ActionStatus.Expired) &&
                (known.Status != message.Receipt.Status || known.HostTick != message.Receipt.HostTick))
            { reason = "conflicting_terminal_receipt"; return false; }
            if (!client.OnReceipt(message.Receipt)) { reason = "unknown_or_mismatched_request"; return false; }
            return true;
        }
    }
}
