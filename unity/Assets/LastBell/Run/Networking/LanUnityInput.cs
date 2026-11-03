using UnityEngine;

namespace LastBell.Run.Networking
{
    // Conversion stays at the Unity boundary; neither packet receipt nor decoding authorizes an ability.
    public static class LanUnityInput
    {
        public static LanInputFrame Encode(PlayerInput p)
        {
            ushort flags = 0;
            void Bit(bool value, int bit) { if (value) flags |= (ushort)(1 << bit); }
            Bit(p.Sprint, 0); Bit(p.Jump, 1); Bit(p.Dash, 2); Bit(p.Potion, 3);
            Bit(p.P0, 4); Bit(p.P1, 5); Bit(p.P2, 6); Bit(p.P3, 7); Bit(p.P4, 8);
            Bit(p.H0, 9); Bit(p.H1, 10); Bit(p.H2, 11); Bit(p.H3, 12); Bit(p.H4, 13);
            return new LanInputFrame { MoveX = p.Move.x, MoveY = p.Move.y, AimX = p.Aim.x, AimY = p.Aim.y, AimZ = p.Aim.z, Flags = flags };
        }
        public static PlayerInput Decode(LanInputFrame f)
        {
            bool Bit(int bit) => (f.Flags & (1 << bit)) != 0;
            return new PlayerInput
            {
                Move = new Vector2(f.MoveX, f.MoveY), Aim = new Vector3(f.AimX, f.AimY, f.AimZ), WorldMove = true,   // clients send world x / z movement
                Sprint = Bit(0), Jump = Bit(1), Dash = Bit(2), Potion = Bit(3),
                P0 = Bit(4), P1 = Bit(5), P2 = Bit(6), P3 = Bit(7), P4 = Bit(8),
                H0 = Bit(9), H1 = Bit(10), H2 = Bit(11), H3 = Bit(12), H4 = Bit(13)
            };
        }
    }

    // Assigned to a remote player on the host. Call Accept on the Unity thread when a validated packet arrives.
    public sealed class LanRemoteInput : IInputSource
    {
        readonly LanInputBuffer buffer = new LanInputBuffer();
        public bool Accept(byte[] bytes) => LanInputFrame.TryDecode(bytes, out var input) && buffer.Accept(input);
        public PlayerInput Read(PlayerController who) => LanUnityInput.Decode(buffer.Consume());
    }
}
