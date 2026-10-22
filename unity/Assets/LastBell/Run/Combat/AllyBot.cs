using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // A bot ally (RunParty): fights the boss with its class kit through ordinary PlayerInput, like a human would.
    // - Positions: melee hugs the boss; casters keep their range. Each ally takes its own angle round the boss so the
    //   party spreads out (spread / mirror / clock mechanics need that).
    // - Dodging: when it stands in a hazard that locked at least its reaction time ago (0.25-0.4 s, varied per bot),
    //   it heads for the nearest spot outside every live hazard, dashing or sprinting only when walking is too slow.
    //   No cheats: it sees exactly the telegraphs a player sees, and has no immunity.
    // - Rotation: a simple per-class priority list; casters stop for their casts because the kits slow movement.
    public sealed class AllyBot : IInputSource
    {
        readonly int slot, of;            // this ally's index and the ally count (for its angle round the boss)
        readonly int reactTicks;
        int tick, lastPress = -999, castHold;
        int[] cd = new int[5];

        public AllyBot(int index, int count)
        {
            slot = index; of = Mathf.Max(1, count);
            reactTicks = 15 + (index * 7) % 10;   // 0.25-0.4 s
        }

        float Range(PlayerController me) => me.ClassName == "Vanguard" ? 0.9f : me.ClassName == "Chimesinger" ? 6.5f : 8.5f;

        public PlayerInput Read(PlayerController me)
        {
            tick++;
            var inp = new PlayerInput();
            var w = me.World; var boss = w.Boss;
            Vector3 pos = me.transform.position, want;
            if (boss == null || !boss.Solid) { inp.Move = ToScreen(Vector3.zero); return inp; }
            var bpos = Sim.Flat(boss.transform.position);
            inp.Aim = bpos;

            // 1) dodge: in a hazard that locked at least our reaction time ago -> nearest clear spot
            var live = w.Hazards.Live;
            Hazard threat = null; int left = int.MaxValue;
            foreach (var h in live)
                if (h.Age >= Mathf.Max(h.LockAt, 0) + reactTicks && h.Contains(pos) && h.ResolveAt - h.Age < left) { threat = h; left = h.ResolveAt - h.Age; }
            Vector3 move;
            if (threat != null)
            {
                move = NearestClear(pos, boss, live, out float need);
                float t = left / (float)Sim.Hz;
                if (move != Vector3.zero)
                {
                    if (need / PlayerController.WalkSpeed > t - 0.1f)
                    {
                        if (need > 2.2f && me.Stamina > 20) inp.Sprint = true;
                        if (!me.Dashing && me.Stamina >= PlayerController.DashCost && need / PlayerController.SprintSpeed > t - 0.15f) inp.Dash = true;
                    }
                }
                castHold = 0;
            }
            else
            {
                // 2) hold a position round the boss at this class's range, on this ally's own angle
                float ang = 360f * slot / of + 30f;
                float dist = boss.BodyRadius + Range(me);
                want = bpos + Sim.Dir(ang) * dist;
                var flat = new Vector2(want.x, want.z); float lim = CombatWorld.PlayerBound - 0.8f;
                if (flat.magnitude > lim) { flat = flat.normalized * lim; want = new Vector3(flat.x, 0, flat.y); }
                var v = Sim.Flat(want - pos);
                move = v.magnitude < 0.4f || castHold > 0 ? Vector3.zero : v.normalized;
                // never walk into a live telegraph
                if (move != Vector3.zero && Inside(live, pos + move * 0.4f) && !Inside(live, pos)) move = Vector3.zero;
                if (castHold > 0) castHold--;
                // 3) attack when in range
                float gap = Sim.Flat(bpos - pos).magnitude - boss.BodyRadius;
                if (gap <= Range(me) + 1.5f) Attack(me, ref inp);
            }
            if (me.Hp < 40 && me.Potions > 0 && tick % 30 == 0) inp.Potion = true;
            inp.Move = ToScreen(move);
            return inp;
        }

        void Attack(PlayerController me, ref PlayerInput inp)
        {
            for (int i = 0; i < cd.Length; i++) if (cd[i] > 0) cd[i]--;
            if (tick - lastPress < 6) return;
            int s = -1;
            switch (me.ClassName)
            {
                case "Vanguard":
                    if (cd[2] == 0) { s = 2; cd[2] = 600; }          // Last Word
                    else if (cd[1] == 0) { s = 1; cd[1] = 240; }     // Sundering Blow
                    else s = 0;                                      // Steel Rhythm
                    break;
                case "Chimesinger":
                    bool hurt = false;
                    foreach (var p in me.World.Players) if (!p.Down && p.Hp < 70 && Sim.Flat(p.transform.position - me.transform.position).magnitude < 6f) hurt = true;
                    if (hurt && cd[4] == 0) { s = 4; cd[4] = 900; }   // Mending Chorus
                    else if (cd[3] == 0) { s = 3; cd[3] = 480; }      // Crescendo
                    else if (cd[2] == 0) { s = 2; cd[2] = 300; }      // Harmonic Refrain
                    else if (cd[1] == 0) { s = 1; cd[1] = 150; }      // Bellstrike
                    else s = 0;                                       // Resonant Note
                    break;
                default:   // Arcanist
                    if (cd[1] == 0) { s = 1; cd[1] = 360; }           // Time Seal
                    else if (cd[2] == 0) { s = 2; cd[2] = 200; castHold = 50; }   // Arc Lance (stand still for the cast)
                    else if (cd[3] == 0) { s = 3; cd[3] = 420; }      // Convergence
                    else s = 0;                                       // Clockbolt
                    break;
            }
            if (s < 0) return;
            lastPress = tick;
            switch (s) { case 0: inp.P0 = inp.H0 = true; break; case 1: inp.P1 = true; break; case 2: inp.P2 = true; break; case 3: inp.P3 = true; break; default: inp.P4 = true; break; }
        }

        static bool Inside(IReadOnlyList<Hazard> live, Vector3 p) { foreach (var h in live) if (h.Contains(p)) return true; return false; }

        static Vector3 NearestClear(Vector3 pos, BossEncounter b, IReadOnlyList<Hazard> live, out float need)
        {
            var bp = b.transform.position;
            for (float r = 0.3f; r <= 14f; r += 0.3f)
                for (int k = 0; k < 24; k++)
                {
                    var dir = Sim.Dir(k * 15f); var q = pos + dir * r;
                    if (!CombatWorld.InPlayerBound(q, -0.2f)) continue;
                    if (Sim.Flat(q - bp).magnitude < b.BodyRadius + PlayerController.Radius + 0.1f) continue;
                    if (Inside(live, q)) continue;
                    need = r; return dir;
                }
            need = 99; return Vector3.zero;
        }

        static Vector2 ToScreen(Vector3 world)
        {
            var cam = Camera.main; var fwd = cam ? Sim.Flat(cam.transform.forward).normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, fwd);
            return Vector2.ClampMagnitude(new Vector2(Vector3.Dot(world, right), Vector3.Dot(world, fwd)), 1);
        }
    }
}
