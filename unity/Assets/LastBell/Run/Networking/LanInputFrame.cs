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
        // parry-QTE presses since the last packet (bits: Up, Left, Down, Right, Parry) and the host tick the client was
        // showing when it pressed them (the Dial Warden judges a parry at that tick, within a lag allowance)
        public byte Qte; public int QteTick;
        public const ushort ValidFlags = 0x3fff;
        public const int EncodedBytes = 27;
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public bool Valid => Finite(MoveX) && Finite(MoveY) && Finite(AimX) && Finite(AimY) && Finite(AimZ)
            && MoveX * MoveX + MoveY * MoveY <= 1.01f
            && Math.Abs(AimX) <= 1000 && Math.Abs(AimY) <= 1000 && Math.Abs(AimZ) <= 1000
            && (Flags & ~ValidFlags) == 0 && (Qte & ~0x1f) == 0 && QteTick >= 0;
        public byte[] Encode()
        {
            if (!Valid) throw new InvalidDataException("Invalid player input.");
            using (var stream = new MemoryStream(EncodedBytes))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(MoveX); writer.Write(MoveY); writer.Write(AimX); writer.Write(AimY); writer.Write(AimZ); writer.Write(Flags); writer.Write(Qte); writer.Write(QteTick);
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
                frame.Qte = reader.ReadByte(); frame.QteTick = reader.ReadInt32();
            }
            return frame.Valid;
        }
    }

    // A client's QTE presses waiting for its next input packet (filled by the replica boss, sent by LanClientWorld).
    public static class LanQteOut
    {
        static byte bits; static int tick;
        public static void Press(int key, int drawnTick) { if (key < 0 || key > 4) return; bits |= (byte)(1 << key); tick = System.Math.Max(0, drawnTick); }
        public static byte Take(out int at) { at = tick; var b = bits; bits = 0; return b; }
        public static void Clear() { bits = 0; tick = 0; }
    }

    // One instance per remote player, called from the host's simulation thread. Edge presses survive multiple
    // received packets until consumed, then fire once. A stalled sender eventually stops moving and holding actions.
    public sealed class LanInputBuffer
    {
        const ushort EdgeMask = 0x01fe;
        public const int StaleAfterTicks = 30;
        LanInputFrame latest;
        ushort edges;
        byte qte; int qteTick;
        // QTE presses are kept until the boss takes them (a braced player reads no movement input)
        public byte TakeQte(out int tick) { tick = qteTick; var b = qte; qte = 0; return b; }
        int age = StaleAfterTicks + 1;
        public bool Accept(LanInputFrame frame)
        {
            if (!frame.Valid) return false;
            latest = frame; edges |= (ushort)(frame.Flags & EdgeMask); age = 0;
            if (frame.Qte != 0) { qte |= frame.Qte; qteTick = frame.QteTick; }
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
