using System;
using System.IO;

namespace LastBell.Network.Rules
{
    // Candidate wire format v1, little endian, exactly 54 bytes.
    // Does not serialize actor IDs or authoritative health/damage/loot/phase.
    public static class InputCodec
    {
        public const byte Version = 1;
        public const int ByteCount = 54;
        public static byte[] Encode(InputRequest input)
        {
            if (input == null || !input.Move.IsFinite) throw new ArgumentException("Finite input required");
            using (var stream = new MemoryStream(ByteCount))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Version);
                // .NET Guid.ToByteArray ordering, explicitly part of candidate v1.
                writer.Write(input.SessionId.ToByteArray());
                writer.Write(input.Generation); writer.Write(input.Sequence); writer.Write(input.TargetTick);
                writer.Write(input.Move.X); writer.Write(input.Move.Y);
                writer.Write((byte)((input.Basic ? 1 : 0) | (input.Technique ? 2 : 0) |
                    (input.Signature ? 4 : 0) | (input.Guard ? 8 : 0)));
                return stream.ToArray();
            }
        }

        public static bool TryDecode(byte[] bytes, out InputRequest input, out string reason)
        {
            input = null; reason = "";
            if (bytes == null || bytes.Length != ByteCount) { reason = "invalid_length"; return false; }
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadByte() != Version) { reason = "invalid_version"; return false; }
                Guid session = new Guid(reader.ReadBytes(16));
                int generation = reader.ReadInt32();
                long seq = reader.ReadInt64(), tick = reader.ReadInt64();
                var move = new PlanarMove(reader.ReadDouble(), reader.ReadDouble());
                byte flags = reader.ReadByte();
                if ((flags & 0xf0) != 0) { reason = "invalid_flags"; return false; }
                if (!move.IsFinite) { reason = "invalid_movement"; return false; }
                input = new InputRequest(session, generation, seq, tick, move,
                    (flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0, (flags & 8) != 0);
                return true;
            }
        }
    }
}
