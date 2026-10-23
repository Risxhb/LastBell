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
    // - Rotation: a simple per-class priority list, OFF by default (AllyBot.Attacks): Chzy18 wants bots that do the
    //   mechanics without fighting, so they never shorten the fight.
    // Test modes (Aurelion v3, HARDER-PROMPT §7; the run menu always uses Passive):
    // - Reflex: reflex-perfect and memory-blind. It reacts on the tick any hostile shape appears (amber included, not at
    //   lock), dashing and sprinting, enters any visible soak, drinks potions, and ignores numerals, marks and in/out cues.
    // - Perfect: the assignment-follower. Reflex dodging plus perfect knowledge of Aurelion's memory mechanics: it walks
    //   to the next Ecliptic Order anchor as soon as the previous strike lands and keeps its Zenith Mark's side.
    public enum BotMode { Passive, Reflex, Perfect }

    public sealed class AllyBot : IInputSource
    {
        readonly int slot, of;            // this ally's index and the ally count (for its angle round the boss)
        readonly int reactTicks;
        public readonly BotMode Mode;
        public string DebugLine;          // tests: what it decided last tick
        public float? HoldRange;          // tests: keep this gap to the boss instead of the class's range (8.5 = a ranged player)
        // bots are mechanics dummies by default: they move, dodge and spread, but never attack or heal. Launch switch
        // -lastbellAllyAttack turns their class rotation on (the boss then scales his HP for them, BossEncounter.Spawn).
        public static bool Attacks = System.Array.Exists(System.Environment.GetCommandLineArgs(), a => a.Equals("-lastbellAllyAttack", System.StringComparison.OrdinalIgnoreCase));
        public static BotMode AllyMode = BotMode.Perfect;   // menu allies do every mechanic, memory ones included (Chzy18: "do the mechanics"); still no attacks
        int tick, lastPress = -999, castHold;
        int[] cd = new int[5];

        public AllyBot(int index, int count, BotMode mode = BotMode.Passive)
        {
            slot = index; of = Mathf.Max(1, count); Mode = mode;
            reactTicks = mode == BotMode.Passive ? 15 + (index * 7) % 10 : 0;   // 0.25-0.4 s; test bots react on the first tick
        }

        float Range(PlayerController me) => HoldRange ?? (me.ClassName == "Vanguard" ? 0.9f : me.ClassName == "Chimesinger" ? 6.5f : 8.5f);

        public PlayerInput Read(PlayerController me)
        {
            tick++;
            var inp = new PlayerInput();
            var w = me.World; var boss = w.Boss;
            Vector3 pos = me.transform.position, want;
            if (boss == null || !boss.Solid) { inp.Move = ToScreen(Vector3.zero); return inp; }
            var bpos = Sim.Flat(boss.transform.position);
            inp.Aim = bpos;

            // 1) dodge: in a hazard that locked at least our reaction time ago (test bots: from its first tick) -> nearest
            // clear spot. A Perfect bot also heads for its memory job (next anchor, mark side) as soon as it is due.
            var live = w.Hazards.Live;
            bad = Bad(me, boss);
            Hazard threat = null; int left = int.MaxValue;
            foreach (var h in live)
                if (h.Age >= (Mode == BotMode.Passive ? Mathf.Max(h.LockAt, 0) + reactTicks : 0) && Hits(h, me, pos) && h.ResolveAt - h.Age < left) { threat = h; left = h.ResolveAt - h.Age; }
            int duty = Duty(me, boss);   // ticks until its job resolves, or -1
            if (threat != null && !bad(pos)) threat = null;   // a shape that lands after its job: not in the way yet
            // dodge whatever lands first: shapes landing more than 0.5 s after it are not in the way yet (Perfect / Reflex only)
            if (threat != null && Mode != BotMode.Passive) bad = Bad(me, boss, false, w.Tick + left);
            if (threat == null && duty >= 0 && bad(pos)) left = duty;
            Vector3 move;
            if (threat != null || (duty >= 0 && bad(pos)))
            {
                move = NearestClear(pos, boss, bad, out float need);
                if (move == Vector3.zero && need > 90) { bad = Bad(me, boss, true, threat != null ? w.Tick + left : int.MaxValue); move = NearestClear(pos, boss, bad, out need, false); }   // no clean spot: take the burn
                float t = left / (float)Sim.Hz;
                if (move != Vector3.zero)
                {
                    if (need / PlayerController.WalkSpeed > t - 0.1f)
                    {
                        if (need > 2.2f && me.Stamina > 20) inp.Sprint = true;
                        // dash only when its 3 m landing is clear too (it overshoots a near spot into the next shape)
                        if (!me.Dashing && me.Stamina >= PlayerController.DashCost && need / PlayerController.SprintSpeed > t - 0.15f && !bad(pos + move * 3f)) inp.Dash = true;
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
                if (move != Vector3.zero && bad(pos + move * 0.4f) && !bad(pos)) move = Vector3.zero;
                if (castHold > 0) castHold--;
                // 3) attack when in range (off: Chzy18 2026-10-22, "the bots are doing too much damage": they only do the
                // mechanics). Set Attacks = true to bring the rotation back.
                float gap = Sim.Flat(bpos - pos).magnitude - boss.BodyRadius;
                if (Attacks && gap <= Range(me) + 1.5f) Attack(me, ref inp);
            }
            if (me.Hp < (Mode == BotMode.Passive ? 40 : 70) && me.Potions > 0 && tick % 30 == 0) inp.Potion = true;   // test bots: before a Hard kill hit (65) could down them
            // walk round his body, not into it: a straight line across the centre would pin the bot against him
            if (move != Vector3.zero) move = AroundBoss(pos, move, boss);
            DebugLine = $"duty {duty} threat {threat?.Source ?? "-"} bad {bad(pos)} move ({move.x:0.0},{move.z:0.0}) at ({pos.x:0.0},{pos.z:0.0})";
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

        System.Func<Vector3, bool> bad;
        static bool Hits(Hazard h, PlayerController me, Vector3 q) => !h.Soak && h.Contains(q) && (h.Affects == null || h.Affects(me));

        // where this bot must not stand now: inside any shape that would hit it; a Perfect bot also avoids anywhere outside
        // the next Ecliptic anchor's disc and the wrong side of its Zenith Mark (with a 0.35-0.4 m buffer); v4 jobs: its
        // Meridian Tower, a light well during Total Eclipse, its Severance side (and clear of the line's next 2 s of sweep)
        System.Func<Vector3, bool> Bad(PlayerController me, BossEncounter boss, bool allowBurn = false, int soonest = int.MaxValue)
        {
            var live = me.World.Hazards.Live;
            // visible jobs (a tower, a light well, a Severance side) for every bot, the allies included; the memory jobs (the
            // Ecliptic order, a Zenith Mark) only for Perfect
            var a = boss as AurelionEncounter; bool memory = Mode == BotMode.Perfect;
            int tick = me.World.Tick;
            int k = memory && a != null && a.Ecliptic != null ? a.Ecliptic.NextStrike(tick) : -1;
            var mine = memory && a != null && a.Marks != null ? a.Marks.Current(me, tick) : null;
            var tower = a != null && a.Towers != null ? a.Towers.NextFor(me, tick) : null;
            bool wells = a != null && a.Eclipse != null && a.Eclipse.Active;
            int side = a != null && a.Sever != null && a.Sever.Active ? (a.Sever.Splitting ? a.Sever.Planned(me) : a.Sever.Assigned(me)) : 0;   // the call while splitting, then the locked side
            var spot = a != null && a.Spots != null ? a.Spots.NextFor(me, tick) : null;   // a Sunspot on me: take it to the rim
            // timed jobs one at a time: only the one that lands first counts now (a tower then a mark, a ring then a mark), or
            // the two zones can leave no ground that satisfies both and the bot freezes
            int due = int.MaxValue;
            if (k >= 0) due = Mathf.Min(due, a.Ecliptic.StrikeAt(k));
            if (mine != null) due = Mathf.Min(due, mine.ResolveTick);
            if (tower != null) due = Mathf.Min(due, tower.ResolveTick);
            if (spot != null) due = Mathf.Min(due, spot.DetonateTick);
            if (k >= 0 && a.Ecliptic.StrikeAt(k) > due) k = -1;
            if (mine != null && mine.ResolveTick > due) mine = null;
            if (tower != null && tower.ResolveTick > due) tower = null;
            if (spot != null && spot.DetonateTick > due) spot = null;
            if (k < 0 && mine == null && tower == null && spot == null && !wells && side == 0 && soonest == int.MaxValue) return q => { foreach (var h in live) if (Hits(h, me, q)) return true; return false; };
            var bp = Sim.Flat(boss.transform.position);
            Vector3 anchor = k >= 0 ? a.Ecliptic.Anchors[k] : Vector3.zero;
            float disc = k >= 0 ? a.Ecliptic.SafeRadius - Hazard.Margin - 0.2f : 0;
            // while a timed job is due, a shape that lands after it is not in the way yet: hold the job, dodge it afterwards
            int first = Mathf.Min(due, soonest);   // the job or the threat that lands first
            int ignoreAfter = first == int.MaxValue ? int.MaxValue : first + 30;
            return q =>
            {
                // allowBurn (no clean spot left): with a place to be (a tower, a ring, a well, a mark), a burning pool is a cost
                foreach (var h in live)
                {
                    if (h.BurnPerSecond <= 0 && tick + (h.ResolveAt - h.Age) > ignoreAfter) continue;
                    if (Hits(h, me, q) && !(allowBurn && h.BurnPerSecond > 0 && (tower != null || k >= 0 || wells || mine != null || spot != null))) return true;
                }
                if (k >= 0 && Sim.Flat(q - anchor).magnitude > disc) return true;
                if (mine != null)
                {
                    float d = Sim.Flat(q - bp).magnitude;
                    if (mine.Mark == ZenithMarks.Mark.Shade ? d > ZenithMarks.ShadeR - Hazard.Margin - 0.4f : d < ZenithMarks.SunR + Hazard.Margin + 0.4f) return true;
                }
                if (tower != null && Sim.Flat(q - tower.At).magnitude > tower.H.Radius - 0.4f) return true;
                if (wells && !a.Eclipse.InWell(q)) return true;
                if (spot != null && Sim.Flat(q - bp).magnitude < Sunspots.RimDrop) return true;
                if (spot != null && a.Spots.Spots.Exists(o => o.Who != me && o.Who && !o.Who.Down && !o.Done && Sim.Flat(o.Who.transform.position - q).magnitude < 4.5f)) return true;   // carriers keep apart
                if (side != 0 && (a.Sever.SideOfAt(q, a.Sever.LineYaw) != side || a.Sever.SideOfAt(q, a.Sever.LineYaw + 8f) != side || a.Sever.DistToLine(q) < 1.0f)) return true;
                return false;
            };
        }

        // ticks until its job (Perfect: the next Ecliptic strike, its Zenith Mark; everyone: its tower) resolves; standing jobs (a
        // light well, a Severance side) count as due now; -1 = none
        int Duty(PlayerController me, BossEncounter boss)
        {
            if (!(boss is AurelionEncounter a)) return -1;
            bool memory = Mode == BotMode.Perfect;
            int t = me.World.Tick, best = -1;
            if (memory && a.Ecliptic != null) { int k = a.Ecliptic.NextStrike(t); if (k >= 0) best = a.Ecliptic.StrikeAt(k) - t; }
            var mk = memory && a.Marks != null ? a.Marks.Current(me, t) : null;
            if (mk != null) { int m = mk.ResolveTick - t; if (best < 0 || m < best) best = m; }
            var sp = a.Spots != null ? a.Spots.NextFor(me, t) : null;
            if (sp != null) { int m = sp.DetonateTick - t; if (best < 0 || m < best) best = m; }
            var tw = a.Towers != null ? a.Towers.NextFor(me, t) : null;
            if (tw != null) { int m = tw.ResolveTick - t; if (best < 0 || m < best) best = m; }
            if ((a.Eclipse != null && a.Eclipse.Active) || (a.Sever != null && a.Sever.Active)) best = best < 0 ? 20 : Mathf.Min(best, 20);
            return best;
        }

        static bool ThroughBurn(Vector3 from, Vector3 to, BossEncounter b)
        {
            var w = (b as AurelionEncounter)?.World; if (w == null) return false;
            foreach (var h in w.Hazards.Live)
            {
                if (h.BurnPerSecond <= 0 || h.Contains(from)) continue;   // already standing in it: leaving is fine
                for (int i = 1; i < 6; i++) if (h.Contains(Vector3.Lerp(from, to, i / 6f))) return true;
            }
            return false;
        }

        static Vector3 AroundBoss(Vector3 pos, Vector3 move, BossEncounter b)
        {
            var bp = Sim.Flat(b.transform.position); var to = Sim.Flat(pos - bp);
            float clear = b.BodyRadius + PlayerController.Radius + 0.35f;
            if (to.magnitude > clear + 1.5f) return move;
            var ahead = Sim.Flat(pos + move.normalized * 0.8f) - bp;
            if (ahead.magnitude >= clear && Vector3.Dot(move, to) >= -0.2f) return move;
            // heading into him: go round on the side the move already leans to (tangent), drifting outward a little
            var tan = Vector3.Cross(Vector3.up, to.normalized); if (Vector3.Dot(tan, move) < 0) tan = -tan;
            return (tan + to.normalized * 0.25f).normalized * move.magnitude;
        }

        static Vector3 NearestClear(Vector3 pos, BossEncounter b, System.Func<Vector3, bool> bad, out float need, bool avoidBurn = true)
        {
            var bp = b.transform.position;
            for (float r = 0.2f; r <= 28f; r += r < 8f ? 0.2f : 0.5f)   // the whole arena: a tower can be 20 m away
                for (int k = 0; k < 48; k++)
                {
                    var dir = Sim.Dir(k * 7.5f); var q = pos + dir * r;
                    if (!CombatWorld.InPlayerBound(q, -0.2f)) continue;
                    if (Sim.Flat(q - bp).magnitude < b.BodyRadius + PlayerController.Radius + 0.1f) continue;
                    if (bad(q) || (avoidBurn && ThroughBurn(pos, q, b))) continue;   // go round burning pools, not through them
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
