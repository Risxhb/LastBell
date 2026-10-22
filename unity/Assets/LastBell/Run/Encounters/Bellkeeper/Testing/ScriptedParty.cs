using System;
using UnityEngine;

namespace LastBell.Run
{
    // Scripted extra players for co-op checks (N = 2/4/8) until the run loop has real networked players. They are ordinary
    // PlayerControllers in CombatWorld.Players (so hazards, headcounts and "anyone standing" see them) driven by a
    // ScriptedPlayer input instead of the keyboard. They never press abilities. The run's own player stays RunDirector.Player.
    public sealed class ScriptedPlayer : IInputSource
    {
        public Vector3? Goal;                 // walk here (world), stopping on it
        public float Speed = 4.2f;            // walking speed in m/s (the prototype escape speed; 6 = full walk)
        public bool Sprint;                   // hold sprint (9 m/s while stamina lasts)
        public bool DashNext;                 // dash once on the next tick, toward Goal (or facing)
        public bool JumpNext;
        public Vector3 Aim;
        public Func<PlayerController, Vector3> Steer;   // optional: world-space move direction each tick (overrides Goal)

        public PlayerInput Read(PlayerController who)
        {
            var r = new PlayerInput { Aim = Aim, Sprint = Sprint, Dash = DashNext, Jump = JumpNext };
            DashNext = JumpNext = false;
            Vector3 move = Vector3.zero;
            if (Steer != null) move = Steer(who);
            else if (Goal.HasValue)
            {
                var to = Sim.Flat(Goal.Value - who.transform.position); float d = to.magnitude;
                float step = Mathf.Max(0.01f, (Sprint ? PlayerController.SprintSpeed : Speed) * Sim.Dt);
                if (d > 0.02f) move = to / d * Mathf.Min(1, d / step);
            }
            if (Steer == null && !Sprint) move *= Mathf.Clamp01(Speed / PlayerController.WalkSpeed);
            // the controller reads camera-relative input, like the keyboard
            var cam = Camera.main; var fwd = cam ? Sim.Flat(cam.transform.forward).normalized : Vector3.forward;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            var right = Vector3.Cross(Vector3.up, fwd);
            r.Move = Vector2.ClampMagnitude(new Vector2(Vector3.Dot(move, right), Vector3.Dot(move, fwd)), 1);
            return r;
        }
    }

    public static class ScriptedParty
    {
        public static PlayerController Add(CombatWorld w, Vector3 pos, string className = "Vanguard")
        {
            var p = PlayerController.Spawn(w, className, new Vector3(pos.x, 0, pos.z), 180);
            p.Input = new ScriptedPlayer();
            p.AbilitiesEnabled = true;    // dash and jump need it; a scripted player never presses an ability
            p.Presenter.GetUp();
            return p;
        }
        public static ScriptedPlayer Script(PlayerController p)
        {
            if (p.Input is ScriptedPlayer s) return s;
            s = new ScriptedPlayer(); p.Input = s; return s;
        }
        // Removes every player except `keep` (tests: back to a solo fight).
        public static void RemoveExtras(CombatWorld w, PlayerController keep)
        {
            for (int i = w.Players.Count - 1; i >= 0; i--)
            {
                var p = w.Players[i]; if (p == keep) continue;
                w.Players.RemoveAt(i); if (p) UnityEngine.Object.Destroy(p.gameObject);
            }
        }

        // -lastbellBots N: stand-ins spaced round the boss at 6 m that step out of any hazard about to resolve and then
        // walk back to their spot. Enough to see the co-op shapes (side headcounts, numerals, mirror images) in practice.
        public static void AddStandIns(CombatWorld w, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float a = (i + 1) * 360f / (count + 1);
                var home = Quaternion.Euler(0, a, 0) * new Vector3(0, 0, 6f);
                var p = Add(w, home, RunDirector.Classes[(i + 1) % RunDirector.Classes.Length]);
                var s = Script(p); var spot = home;
                s.Speed = PlayerController.WalkSpeed;
                s.Steer = me =>
                {
                    var pos = me.transform.position;
                    foreach (var h in w.Hazards.Live)
                    {
                        // react once the shape is final (locked) or close to resolving: a tide from the wall needs the whole warning
                        if (!h.Contains(pos) || (!h.Locked && h.ResolveAt - h.Age > 150)) continue;
                        var away = Sim.Flat(pos - h.Origin); if (away.sqrMagnitude < 0.01f) away = Vector3.right;
                        if (h.Shape == HazardShape.Ring)
                        {
                            float r = away.magnitude;
                            bool outsideIsWall = h.Radius >= CombatWorld.PlayerBound;   // a ring reaching the wall: the way out is in
                            return outsideIsWall || r - h.Inner < h.Radius - r ? -away.normalized : away.normalized;
                        }
                        if (h.Shape == HazardShape.Cone) return -Sim.Dir(h.Yaw);
                        if (h.Shape == HazardShape.Lane) return h.PushDir(pos);
                        return away.normalized;
                    }
                    var back = Sim.Flat(spot - pos);
                    return back.magnitude > 0.3f ? back.normalized : Vector3.zero;
                };
            }
        }
    }
}
