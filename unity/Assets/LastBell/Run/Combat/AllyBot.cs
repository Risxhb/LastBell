using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // A bot ally (RunParty): plays through ordinary PlayerInput, like a human would. No cheats: it sees the bullets and
    // telegraphs a player sees, and it has no immunity.
    // This branch's version is the Sylvara v3 dodger (bullet hell). The Aurelion lane has its own AllyBot with Aurelion's
    // memory jobs (integrate/aurelion); when both land, keep that one and call SylvaraSteer from it for SylvaraEncounter.
    // - Steering: every 3 ticks it tries standing still and 16 directions at walk and sprint speed, plays every bullet that
    //   can hurt it 0.8 s ahead (its own colour only in Two-Colour Choir, both while a swap is warned; seed bursts included) plus the telegraphs about to
    //   land, and takes the cheapest path. When every path is hit, it dashes if a dash clears.
    // - Jobs: it holds its own angle round her (casters at range, Vanguard close) and in Hollow Bloom stands next to a
    //   Choir Bud to cut it (one bud per bot, by slot).
    // - Attacks: OFF by default (AllyBot.Attacks): Chzy18 wants bots that do the mechanics without shortening the fight.
    //   With -lastbellAllyAttack they keep the basic attack going on her (or on their bud) while they dodge.
    public enum BotMode { Passive, Reflex, Perfect }

    public sealed class AllyBot : IInputSource
    {
        readonly int slot, of;
        public readonly BotMode Mode;
        public string DebugLine;
        public float? HoldRange;          // tests: keep this gap to the boss instead of the class's range
        public static bool Attacks = System.Array.Exists(System.Environment.GetCommandLineArgs(), a => a.Equals("-lastbellAllyAttack", System.StringComparison.OrdinalIgnoreCase));
        public static BotMode AllyMode = BotMode.Perfect;
        int tick;
        Vector3 lastMove; bool lastSprint;

        public AllyBot(int index, int count, BotMode mode = BotMode.Passive) { slot = index; of = Mathf.Max(1, count); Mode = mode; }

        float Range(PlayerController me) => HoldRange ?? (me.ClassName == "Vanguard" ? 1.2f : me.ClassName == "Chimesinger" ? 6.5f : 8.5f);

        public PlayerInput Read(PlayerController me)
        {
            tick++;
            var inp = new PlayerInput();
            var w = me.World; var boss = w.Boss;
            if (boss == null || !boss.Solid) { inp.Move = ToScreen(Vector3.zero); return inp; }
            var pos = Sim.Flat(me.transform.position); var bp = Sim.Flat(boss.transform.position);
            var target = bp;

            // where it wants to be: its own angle round her, or next to its Choir Bud
            float ang = 360f * slot / of + 30f;
            var home = bp + Sim.Dir(ang) * (boss.BodyRadius + Range(me) + 0.5f);
            if (boss is SylvaraEncounter sy)
            {
                var buds = sy.AliveBudPositions();
                if (buds.Count > 0)
                {
                    var bud = buds[slot % buds.Count];
                    home = bud + Sim.Flat(pos - bud).normalized * 1.8f; target = bud;
                }
            }
            var flat = new Vector2(home.x, home.z); float lim = CombatWorld.PlayerBound - 1f;
            if (flat.magnitude > lim) { flat = flat.normalized * lim; home = new Vector3(flat.x, 0, flat.y); }

            if (tick % 3 == 1 || me.Dashing)
            {
                var plan = SylvaraSteer.Choose(me, pos, home, out bool sprint, out bool dash, out float cost);
                lastMove = plan; lastSprint = sprint;
                if (dash) inp.Dash = true;
                DebugLine = $"cost {cost:0.0} move ({plan.x:0.0},{plan.z:0.0}) sprint {sprint} dash {dash} at ({pos.x:0.0},{pos.z:0.0})";
            }
            inp.Sprint = lastSprint;
            inp.Aim = target;
            if (Attacks && Sim.Flat(target - pos).magnitude <= Range(me) + boss.BodyRadius + 2f) inp.P0 = inp.H0 = tick % 6 == 0 || me.ClassName != "Arcanist";
            if (me.Hp < 70 && me.Potions > 0 && tick % 30 == 0) inp.Potion = true;
            inp.Move = ToScreen(lastMove);
            return inp;
        }

        static Vector2 ToScreen(Vector3 world)
        {
            var cam = Camera.main; var fwd = cam ? Sim.Flat(cam.transform.forward).normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, fwd);
            return Vector2.ClampMagnitude(new Vector2(Vector3.Dot(world, right), Vector3.Dot(world, fwd)), 1);
        }
    }

    // Bullet-hell steering (Sylvara v3): scores candidate moves against every hostile bullet and telegraph that can hurt
    // this player in the next 0.8 s.
    public static class SylvaraSteer
    {
        const float Horizon = 0.8f, Step = 0.05f, Buffer = 0.25f;
        static readonly List<BulletSystem.Bullet> near = new List<BulletSystem.Bullet>();
        static readonly Vector3[] dirs = MakeDirs();
        static Vector3[] MakeDirs() { var d = new Vector3[16]; for (int i = 0; i < 16; i++) d[i] = Sim.Dir(i * 22.5f); return d; }

        public static Vector3 Choose(PlayerController me, Vector3 pos, Vector3 home, out bool sprint, out bool dash, out float bestCost)
        {
            var w = me.World; var boss = w.Boss;
            int mine = w.Bullets.ColourOf != null ? w.Bullets.ColourOf(me) : 0;
            // its ring flickers before a toll swaps colours (the same warning a player sees): both colours count until the swap
            bool both = w.Boss is SylvaraEncounter sy && sy.SwapWarned(me);
            // bullets that can reach anywhere it could be within the horizon
            near.Clear();
            float reach = PlayerController.SprintSpeed * Horizon + 1.2f;
            foreach (var b in w.Bullets.Live)
            {
                if (b.Dead || (b.Colour != 0 && b.Colour != mine && !both)) continue;
                var end = b.Pos + b.Vel * Horizon;
                if (SegDist(pos, b.Pos, end) < reach + (b.Split != null ? 3f : 0f)) near.Add(b);
            }
            sprint = false; dash = false;
            var best = Vector3.zero; bestCost = Cost(me, pos, Vector3.zero, 0, home, boss);
            bool canSprint = me.Stamina > PlayerController.SprintCost;
            for (int s = 0; s < (canSprint ? 2 : 1); s++)
            {
                float v = s == 0 ? PlayerController.WalkSpeed : PlayerController.SprintSpeed;
                foreach (var d in dirs)
                {
                    float c = Cost(me, pos, d, v, home, boss) + (s == 1 ? 0.6f : 0);
                    if (c < bestCost) { bestCost = c; best = d; sprint = s == 1; }
                }
            }
            // every path is hit: a 3 m dash (0.2 s) clears it when its line and landing are clean
            if (bestCost >= 50 && !me.Dashing && me.Stamina >= PlayerController.DashCost)
            {
                float dashSpeed = PlayerController.DashDistance / (PlayerController.DashTicks * Sim.Dt);
                foreach (var d in dirs)
                {
                    float c = Cost(me, pos, d, dashSpeed, home, boss, PlayerController.DashTicks * Sim.Dt) + 2f;
                    if (c < bestCost) { bestCost = c; best = d; dash = true; sprint = false; }
                }
            }
            return best;
        }

        // moves at v along d for `moveFor` seconds (all of the horizon by default), then stands
        static float Cost(PlayerController me, Vector3 pos, Vector3 d, float v, Vector3 home, BossEncounter boss, float moveFor = Horizon)
        {
            float cost = 0; var bp = boss != null ? Sim.Flat(boss.transform.position) : Vector3.zero;
            float body = boss != null ? boss.BodyRadius + PlayerController.Radius + 0.1f : 0;
            Vector3 q = pos;
            int steps = Mathf.RoundToInt(Horizon / Step);
            for (int k = 1; k <= steps; k++)
            {
                float t = k * Step;
                q = pos + d * v * Mathf.Min(t, moveFor);
                if (!CombatWorld.InPlayerBound(q, -0.3f)) return 500 + k;
                if (boss != null && Sim.Flat(q - bp).magnitude < body) return 400 + k;
                float early = 1.5f - t / Horizon;   // hits sooner are worse: there is less time to fix them later
                foreach (var b in near)
                {
                    float fuse = b.Split != null ? b.Life - b.Age : float.MaxValue;
                    if (t <= fuse)
                    {
                        float gap = Sim.Flat(q - (b.Pos + b.Vel * t)).magnitude - (b.Radius + Hazard.Margin + Buffer);
                        if (gap < 0) cost += 100 * early;
                        else if (gap < 0.6f) cost += (0.6f - gap) * 4;
                    }
                    else
                    {
                        // a seed bursts into a ring of petals: keep clear of the burst point and its ring for a moment
                        var at = b.Pos + b.Vel * fuse; float since = t - fuse;
                        float ring = 4.5f * since, gap = Mathf.Abs(Sim.Flat(q - at).magnitude - ring) - (0.35f + Hazard.Margin + Buffer);
                        if (since < 0.6f && gap < 0) cost += 40 * early;
                    }
                }
            }
            // telegraphs that land within the horizon (plus a little) where it ends up
            foreach (var h in me.World.Hazards.Live)
            {
                bool hurts = (h.HitOnResolve && h.Damage > 0) || h.BurnPerSecond > 0;
                if (!hurts || !h.Contains(q)) continue;
                float left = (h.ResolveAt - h.Age) * Sim.Dt;
                cost += h.BurnPerSecond > 0 ? 60 : left < Horizon + 0.4f ? 150 : 25;
            }
            cost += Sim.Flat(q - home).magnitude * 0.35f;
            return cost;
        }

        static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a; float l = ab.sqrMagnitude;
            float t = l < 1e-6f ? 0 : Mathf.Clamp01(Vector3.Dot(p - a, ab) / l);
            return Sim.Flat(p - (a + ab * t)).magnitude;
        }
    }
}
