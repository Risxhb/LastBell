// Standalone check stubs: Unity itself supplies these types in the player.
namespace UnityEngine
{
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
    public struct Vector3 { public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } }
}
namespace LastBell.Run
{
    public struct PlayerInput
    {
        public UnityEngine.Vector2 Move; public UnityEngine.Vector3 Aim;
        public bool WorldMove, Sprint, Jump, Dash, Potion, P0, P1, P2, P3, P4, H0, H1, H2, H3, H4;
    }
    public sealed class PlayerController { }
    public interface IInputSource { PlayerInput Read(PlayerController who); }
}
