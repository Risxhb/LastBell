using UnityEngine;

namespace LastBell.Run
{
    // A tutorial ally: an ordinary AllyBot (it dodges what a player can see, never attacks) that walks to the job the
    // tutorial gives it (a soak tower, a spread spot, its mark's side) and holds it, ignoring other shapes while it does.
    public sealed class TutorialBot : IInputSource
    {
        readonly TutorialDirector t; readonly AllyBot inner;
        public TutorialBot(TutorialDirector tutorial, AllyBot bot) { t = tutorial; inner = bot; }

        public PlayerInput Read(PlayerController me)
        {
            var inp = inner.Read(me);
            if (t && t.Jobs.TryGetValue(me, out var job))
            {
                var to = Sim.Flat(job - me.transform.position);
                inp.Dash = false;
                inp.Sprint = to.magnitude > 3f;
                inp.Move = to.magnitude > 0.3f ? ToScreen(to.normalized) : Vector2.zero;
            }
            return inp;
        }

        public static Vector2 ToScreen(Vector3 world)
        {
            var cam = Camera.main; var fwd = cam ? Sim.Flat(cam.transform.forward).normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, fwd);
            return Vector2.ClampMagnitude(new Vector2(Vector3.Dot(world, right), Vector3.Dot(world, fwd)), 1);
        }
    }
}
