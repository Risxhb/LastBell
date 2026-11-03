using System;
using System.IO;

namespace LastBell.Run.Networking
{
    // Network intent is not an action result: the host still evaluates movement, cooldowns and damage.
    public struct LanInputFrame
    {
        public float MoveX, MoveY, AimX, AimY, AimZ;
        // Bits: sprint, jump, dash, potion, five pressed, five held.
        public ushort Flags;
        public const ushort ValidFlags = 0x3fff;
        public const int EncodedBytes = 22;
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public bool Valid => Finite(MoveX) && Finite(MoveY) && Finite(AimX) && Finite(AimY) && Finite(AimZ)
            && MoveX * MoveX + MoveY * MoveY <= 1.01f
            && Math.Abs(AimX) <= 1000 && Math.Abs(AimY) <= 1000 && Math.Abs(AimZ) <= 1000
            && (Flags & ~ValidFlags) == 0;
        public byte[] Encode()
        {
            if (!Valid) throw new InvalidDataException("Invalid player input.");
            using (var stream = new MemoryStream(EncodedBytes))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(MoveX); writer.Write(MoveY); writer.Write(AimX); writer.Write(AimY); writer.Write(AimZ); writer.Write(Flags);
                return stream.ToArray();
            }
        }
        public static bool TryDecode(byte[] bytes, out LanInputFrame frame)
        {
            frame = default;
            if (bytes == null || bytes.Length != EncodedBytes) return false;
            using (var reader = new BinaryReader(new MemoryStream(bytes)))
            {
                frame.MoveX = reader.ReadSingle(); frame.MoveY = reader.ReadSingle(); frame.AimX = reader.ReadSingle();
                frame.AimY = reader.ReadSingle(); frame.AimZ = reader.ReadSingle(); frame.Flags = reader.ReadUInt16();
            }
            return frame.Valid;
        }
    }

    // One instance per remote player, called from the host's simulation thread. Edge presses survive multiple
    // received packets until consumed, then fire once. A stalled sender eventually stops moving and holding actions.
    public sealed class LanInputBuffer
    {
        const ushort EdgeMask = 0x01fe;
        public const int StaleAfterTicks = 30;
        LanInputFrame latest;
        ushort edges;
        int age = StaleAfterTicks + 1;
        public bool Accept(LanInputFrame frame)
        {
            if (!frame.Valid) return false;
            latest = frame; edges |= (ushort)(frame.Flags & EdgeMask); age = 0;
            return true;
        }
        public LanInputFrame Consume()
        {
            if (age < int.MaxValue) age++;
            var value = latest;
            if (age > StaleAfterTicks) { value.MoveX = value.MoveY = 0; value.Flags = 0; edges = 0; return value; }
            value.Flags = (ushort)((value.Flags & ~EdgeMask) | edges);
            edges = 0;
            return value;
        }
    }
}
