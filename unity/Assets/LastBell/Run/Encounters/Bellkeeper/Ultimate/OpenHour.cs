using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // The Open Hour (`bellkeeper.ult_open_hour`), Bellkeeper Ultimate v2 P3 — proposal: bullet hell, built 2026-10-25 from the
    // browser simulation Chzy18 asked to see in Unity (tools/bellkeeper-ultimate/sims/p3-example.html); not human-approved.
    // One 60 s pattern, repeated until P3 ends (times below are seconds into the pattern):
    //   MAINSPRING   three spiral arms of brass bullets from the boss: one shot per arm every 0.75 s at 3.5 m/s; the arms
    //                turn 30°/s and reverse every 12 s. A bullet hit is 50 (two kill).
    //   REWOUND SHOT at 15, 30, 45: every bullet turns round and flies back into the boss at 1.5x speed (the arms pause 2 s).
    //                "REWIND" shows 1.5 s before.
    //   ECHO OF YOU  every 1 s each player leaves an echo where they stand; 2 s later it bursts (1.1 m): 999 to its owner
    //                only, and it throws four small bullets (2.5 m/s, 1 s) that hit anyone for 50. Never stand still.
    //   BELL CURTAIN at 20, 38, 52: a wall of bullets sweeps the arena at 3 m/s with four 2.4 m gaps marked I–IV. Through
    //                your own numeral: safe; another's: 999; the wall: 50. A plate beat that falls inside a sweep gives that
    //                beat's soaker the gap nearest its plate.
    //   PLATE        every 6 s the next hour (I, II, III, IV, ...) must be on the lit plate (r 7). Empty = WIPE.
    // No unavoidable damage. Bots steer with a short route search (Steer), the simulation's: 3 legs of 0.2 s against
    // every predicted bullet, burst and wall. 5-8 players: hours repeat (two soakers, shared numerals).
    public sealed class OpenHour : UltimateMechanic
    {
        public override string Id => UltimateMechanics.OpenHour;
        public override string Name => "The Open Hour";

        public const string Source = "The Open Hour";
        const float Loop = 60f, ArmRate = 0.75f, ArmSpeed = 3.5f, ArmTurn = 30f, ArmFlip = 12f, Rev = 1.5f, RewindPause = 2f, Warn = 1.5f,
            EchoEvery = 1f, EchoDelay = 2f, EchoR = 1.1f, EchoShotSpeed = 2.5f, EchoShotLife = 1f,
            CurtSpeed = 3f, GapHalf = 1.2f, Beat = 6f, PlateR = 7f, OnPlate = 1.6f, BR = 0.22f, PR = 0.45f;
        const int Arms = 3, EchoShots = 4, BulletDamage = 50;
        static readonly float[] Rewinds = { 15, 30, 45 };
        static readonly float[] GapOff = { -6.6f, -2.2f, 2.2f, 6.6f };
        static readonly (float at, float a)[] Curtains = { (20, 0), (38, 90), (52, 225) };
        static readonly int[][] CurtOrder = { new[] { 2, 0, 3, 1 }, new[] { 1, 3, 0, 2 }, new[] { 3, 1, 2, 0 } };

        sealed class Bullet { public Vector3 P, V; public bool Rev, Echo, Gone; public float Born; }
        sealed class Mine { public Vector3 P; public float At; public PlayerController Owner; public int OwnerHour; public bool Done, Burst; public LastBell.VFX.BellkeeperFx.Telegraph Tele; }
        readonly List<Bullet> bullets = new List<Bullet>();
        readonly List<Mine> mines = new List<Mine>();
        readonly Dictionary<PlayerController, bool[]> crossed = new Dictionary<PlayerController, bool[]>();
        float t0Tick, nextArm, nextEcho; int beatsDone, loopIndex;
        int[][] orders;
        bool stop;
        public int Beats { get; private set; }
        public int BeatsClean { get; private set; }
        public readonly List<string> Fails = new List<string>();

        public override UltCue DefaultCue() => new UltCue { Variant = "practice" };
        public void StopAtLoopEnd() => stop = true;   // ends at once, or when the current curtain has swept past

        // ---------------- time and geometry ----------------
        float T => (Now - t0Tick) / Sim.Hz;                   // seconds since the cast began
        float TL(float t) => t - Mathf.Floor(t / Loop) * Loop; // seconds into the 60 s pattern
        static Vector3 Dir(float a) => EscapementRelay.PlanDir(a);
        float ArmAngle(float t)
        {
            float a = 0, s = 0;
            while (s + ArmFlip <= t) { a += (Mathf.FloorToInt(s / ArmFlip) % 2 == 1 ? -1 : 1) * ArmTurn * ArmFlip; s += ArmFlip; }
            return a + (Mathf.FloorToInt(s / ArmFlip) % 2 == 1 ? -1 : 1) * ArmTurn * (t - s);
        }
        bool RewindAt(float t) { float tl = TL(t); foreach (var r in Rewinds) if (tl >= r && tl < r + RewindPause) return true; return false; }
        float? RewindSoon(float t) { float tl = TL(t); foreach (var r in Rewinds) if (tl < r && tl >= r - (Warn + WarnLead)) return r - tl; return null; }
        // the rewind (absolute seconds) inside (t, t+dt], or -1
        float RewindIn(float t, float dt) { float b = t - TL(t); foreach (var r in Rewinds) { float a = b + r; if (a > t && a <= t + dt) return a; a += Loop; if (a > t && a <= t + dt) return a; } return -1; }
        float CurtStart(int ci, float t) => t - TL(t) + Curtains[ci].at;   // this loop's start of curtain ci
        float CurtLine(int ci, float t) => -12.5f + CurtSpeed * (t - CurtStart(ci, t));
        bool CurtOn(int ci, float t) { float s = t - CurtStart(ci, t); return s >= 0 && CurtLine(ci, t) <= 12.5f; }
        bool CurtSoon(int ci, float t) { float s = t - CurtStart(ci, t); return s >= -3 && s < 0; }
        static Vector3 Perp(Vector3 d) => new Vector3(d.z, 0, -d.x);
        Vector3 PlateOf(int k) => Dir(90f * k) * PlateR;            // beat k (1..) of this cast
        int HourOf(PlayerController p) { int i = W.Players.IndexOf(p); return i < 0 ? -1 : i % 4; }
        int Hours => Mathf.Clamp(Party, 1, 4);
        int SoakHour(int k) => (k - 1) % Hours;
        // 0 safe, 1 the wall, 2 another's gap
        int CurtainHit(int ci, Vector3 q, float t, int hour, float margin = 0)
        {
            if (!CurtOn(ci, t)) return 0;
            var d = Dir(Curtains[ci].a); float s = CurtLine(ci, t);
            if (Mathf.Abs(Vector3.Dot(q, d) - s) > PR + BR + margin) return 0;
            float o = Vector3.Dot(q, Perp(d)); int g = -1;
            for (int i = 0; i < 4; i++) if (Mathf.Abs(o - GapOff[i]) < GapHalf - PR * 0.6f - margin) g = i;
            if (g < 0) return 1;
            return orders[ci][g] == hour % 4 ? 0 : 2;
        }
        // gap index -> hour for each curtain of this loop: a soaker whose beat falls inside the sweep gets the gap nearest its plate
        void DealGaps(float loopStart)
        {
            orders = new int[3][];
            for (int ci = 0; ci < 3; ci++)
            {
                var o = (int[])CurtOrder[ci].Clone(); var d = Dir(Curtains[ci].a); float st = loopStart + Curtains[ci].at, end = st + 25f / CurtSpeed;
                for (int k = Mathf.CeilToInt(st / Beat); k * Beat <= end; k++)
                {
                    if (k < 1) continue;
                    int s = SoakHour(k); float po = Vector3.Dot(PlateOf(k), Perp(d)); int gi = 0;
                    for (int i = 1; i < 4; i++) if (Mathf.Abs(GapOff[i] - po) < Mathf.Abs(GapOff[gi] - po)) gi = i;
                    int cur = System.Array.IndexOf(o, s); if (cur < 0) continue;
                    (o[gi], o[cur]) = (o[cur], o[gi]);
                }
                orders[ci] = o;
            }
        }

        // ---------------- cast ----------------
        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name);
            stop = false; t0Tick = Now; nextArm = 3; nextEcho = 2; beatsDone = 0; loopIndex = 0;
            bullets.Clear(); ClearMines(); crossed.Clear(); plans.Clear();
            DealGaps(0);
            Build();
            while (true)
            {
                float t = T, nt = t + Sim.Dt;
                int li = Mathf.FloorToInt(nt / Loop);
                if (li != loopIndex) { loopIndex = li; DealGaps(li * Loop); crossed.Clear(); }
                if (stop && !CurtOn(0, nt) && !CurtOn(1, nt) && !CurtOn(2, nt)) break;   // P3 is over: end now (not inside a curtain sweep)
                StepFight(t, nt);
                if (Boss.Ult.WipeCause != null) break;
                Present(nt);
                yield return 1;
            }
            bullets.Clear(); ClearMines();
            Teardown();
        }

        void StepFight(float t, float nt)
        {
            var bossP = Sim.Flat(Boss.transform.position);
            // the Mainspring (paused while rewinding)
            while (nextArm <= nt)
            {
                if (!RewindAt(nextArm))
                    for (int k = 0; k < Arms; k++) { var d = Dir(ArmAngle(nextArm) + 120 * k); bullets.Add(new Bullet { P = bossP + d * Boss.BodyRadius, V = d * ArmSpeed, Born = nextArm }); }
                nextArm += ArmRate;
            }
            // rewinds: every bullet turns round
            if (RewindIn(t, nt - t) >= 0) { foreach (var b in bullets) { b.V = -b.V * Rev; b.Rev = true; } Boss.PlanLog.Add((Now, "open hour rewind")); W.BossFx.ChimeResolve(bossP, 3f); }
            // echoes
            while (nextEcho <= nt)
            {
                foreach (var p in W.Players) if (!p.Down) mines.Add(new Mine { P = Sim.Flat(p.transform.position), At = nextEcho + EchoDelay, Owner = p, OwnerHour = HourOf(p) });
                nextEcho += EchoEvery;
            }
            // bullets fly and hit
            foreach (var b in bullets) b.P += b.V * (nt - t);
            foreach (var p in W.Players)
            {
                if (p.Down) continue; var q = Sim.Flat(p.transform.position);
                foreach (var b in bullets) if (!b.Gone && (q - b.P).magnitude < PR + BR) { b.Gone = true; Hit(p, BulletDamage, b.Echo ? "an echo shot" : "a Mainspring bullet"); }
            }
            bullets.RemoveAll(b => b.Gone || (b.P - bossP).magnitude > 13.5f || (b.Rev && !b.Echo && (b.P - bossP).magnitude < Boss.BodyRadius * 0.8f) || (b.Echo && nt - b.Born > EchoShotLife));
            // echo bursts
            foreach (var m in mines)
            {
                if (m.Done || nt < m.At) continue; m.Done = true;
                if (m.Owner && !m.Owner.Down && (Sim.Flat(m.Owner.transform.position) - m.P).magnitude < EchoR + PR) Hit(m.Owner, UltimateState.Lethal, "its own echo");
                for (int k = 0; k < EchoShots; k++) { var d = Dir(360f / EchoShots * k + 45 * (m.OwnerHour % 2)); bullets.Add(new Bullet { P = m.P, V = d * EchoShotSpeed, Echo = true, Born = nt }); }
                W.BossFx.ChimeResolve(m.P, EchoR);
            }
            for (int i = mines.Count - 1; i >= 0; i--) if (mines[i].Done && nt - mines[i].At > 0.3f) { if (mines[i].Tele != null && !mines[i].Burst) mines[i].Tele.Dismiss(); mines.RemoveAt(i); }
            // curtains
            for (int ci = 0; ci < 3; ci++)
            {
                if (!CurtOn(ci, nt)) continue; var d = Dir(Curtains[ci].a); float line = CurtLine(ci, nt);
                foreach (var p in W.Players)
                {
                    if (p.Down) continue; var cr = Crossed(p); if (cr[ci]) continue;
                    var q = Sim.Flat(p.transform.position);
                    if (Vector3.Dot(q, d) < line - PR - BR) { cr[ci] = true; continue; }
                    int h = CurtainHit(ci, q, nt, HourOf(p));
                    if (h == 1) { cr[ci] = true; Hit(p, BulletDamage, "the Bell Curtain"); }
                    if (h == 2) { cr[ci] = true; Hit(p, UltimateState.Lethal, "another's curtain gap"); }
                }
            }
            // plate beats
            int kb = Mathf.FloorToInt(nt / Beat);
            if (kb >= 1 && kb > beatsDone)
            {
                beatsDone = kb; Beats++;
                int s = SoakHour(kb); var pl = PlateOf(kb); bool on = false;
                foreach (var p in W.Players) if (!p.Down && HourOf(p) == s && (Sim.Flat(p.transform.position) - pl).magnitude <= OnPlate) on = true;
                W.BossFx.ChimeResolve(pl, OnPlate);
                if (!on) { Fails.Add($"beat {kb}: plate empty"); Ult.Wipe("The open hour (empty plate)"); return; }
                BeatsClean++;
            }
        }
        bool[] Crossed(PlayerController p) { if (!crossed.TryGetValue(p, out var c)) crossed[p] = c = new bool[3]; return c; }
        void Hit(PlayerController p, int dmg, string what)
        {
            if (LogSteer) Debug.Log($"[open hour] {T:0.00}s {UltimateState.Roman[HourOf(p) + 1]} hit by {what} at ({p.transform.position.x:0.0},{p.transform.position.z:0.0}) job {JobOf(p)}");
            Fails.Add($"{T:0.0}s {UltimateState.Roman[HourOf(p) + 1]}: {what}");
            p.ReceiveHit(new Hazard { Source = Source + ": " + what, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = Sim.Flat(p.transform.position), Radius = 0.5f, Damage = dmg, Knockback = 0, Stun = 0 });
        }

        // ---------------- the bots: a short route search (UltimateBot asks Steer while this runs) ----------------
        sealed class Plan { public Vector3 V; public int Until; public string Job; public Vector3 Goal; public bool Urgent; public float BeatAt = -1; public bool Searched; }
        readonly Dictionary<PlayerController, Plan> plans = new Dictionary<PlayerController, Plan>();
        public static bool LogSteer;   // tests: every time a bot finds no safe route
        public string JobOf(PlayerController p) => plans.TryGetValue(p, out var pl) ? pl.Job : null;

        // A velocity for bot p (world, m/s). skill: Late re-plans 0.25 s behind; Miss walks in to attack during a rewind.
        public override bool Steer(PlayerController p, UltimateBot.Skill skill, out Vector3 vel)
        {
            vel = Vector3.zero;
            if (!Running || p.Down) return false;
            float t = T; var pos = Sim.Flat(p.transform.position); var bossP = Sim.Flat(Boss.transform.position);
            if (skill == UltimateBot.Skill.Miss && RewindAt(t))
            {
                var to = bossP + (pos - bossP).normalized * (Boss.BodyRadius + PR + 0.2f); var v = to - pos;
                vel = v.magnitude > 0.05f ? v.normalized * PlayerController.WalkSpeed : Vector3.zero;
                plans[p] = new Plan { V = vel, Until = Now + 1, Job = "attacks the boss" }; return true;
            }
            if (plans.TryGetValue(p, out var cur) && Now < cur.Until) { vel = cur.V; return true; }
            var goal = GoalOf(p, t, out bool urgent, out float beatAt, out string job);
            var v2 = Search(p, pos, t, goal, urgent, beatAt);
            plans[p] = new Plan { V = v2, Until = Now + (skill == UltimateBot.Skill.Late ? 15 : 3), Job = job, Goal = goal, Urgent = urgent, BeatAt = beatAt, Searched = true };
            vel = v2; return true;
        }

        Vector3 GoalOf(PlayerController p, float t, out bool urgent, out float beatAt, out string job)
        {
            int h = HourOf(p); urgent = false; beatAt = -1; job = "WEAVE";
            var pos = Sim.Flat(p.transform.position);
            int k = Mathf.FloorToInt(t / Beat) + 1; float tb = k * Beat - t;
            bool soaker = SoakHour(k) == h % Hours;
            bool crossFirst = false;
            if (soaker)
                for (int ci = 0; ci < 3; ci++)
                {
                    if (Crossed(p)[ci] || !(CurtOn(ci, t) || CurtSoon(ci, t))) continue;
                    var d = Dir(Curtains[ci].a); float sw = CurtOn(ci, t) ? CurtLine(ci, t) : -12.5f, atPlate = Vector3.Dot(PlateOf(k), d);
                    if (Vector3.Dot(pos, d) < sw - 1) continue;
                    if (atPlate < sw || CurtStart(ci, t) + (atPlate + 12.5f) / CurtSpeed <= k * Beat + 0.3f) crossFirst = true;
                }
            if (soaker && !crossFirst)
            {
                var pl = PlateOf(k); float need = (pl - pos).magnitude / PlayerController.WalkSpeed + 1.6f;   // not more: arriving early means standing still on the plate, and your own echo bursts under you
                if (tb <= need) { urgent = true; beatAt = k * Beat; job = $"SOAK in {tb:0.0}s"; return pl; }
            }
            for (int ci = 0; ci < 3; ci++)
            {
                if (Crossed(p)[ci] || !(CurtOn(ci, t) || CurtSoon(ci, t))) continue;
                var d = Dir(Curtains[ci].a); var pp = Perp(d); float s0 = CurtOn(ci, t) ? CurtLine(ci, t) : -12.5f, my = Vector3.Dot(pos, d);
                if (my < s0 - 1) continue;
                int g = System.Array.IndexOf(orders[ci], h % 4); if (g < 0) continue;
                float wallNow = CurtOn(ci, t) ? s0 : -12.5f - (CurtStart(ci, t) - t) * CurtSpeed, tContact = (my - wallNow) / CurtSpeed, side = Mathf.Abs(Vector3.Dot(pos, pp) - GapOff[g]);
                if (tContact > side / PlayerController.WalkSpeed * 1.6f + 1.5f) continue;
                float off = GapOff[g], lim = Mathf.Sqrt(Mathf.Max(0, 9.3f * 9.3f - off * off)), along = Mathf.Clamp(wallNow - 1.5f, -lim, lim);
                urgent = true; job = "CURTAIN: gap " + UltimateState.Roman[h % 4 + 1];
                return pp * off + d * along;
            }
            float[] rings = { 4.3f, 4.3f, 9.6f, 10.6f };   // I and II share a ring 120° apart (each passes the other's spot 4 s later: its echo is gone)
            int i = W.Players.IndexOf(p);
            float a = ArmAngle(t) + 60 + 120 * (h % 3) + (i >= 4 ? 30 : 0);
            // a solo melee player weaves against the boss, where its swings reach (its lunge into the solid boss can't carry it into a
            // bullet); a party keeps the rings, where the others' echoes stay clear
            if (Party == 1 && p.ClassName == "Vanguard") return Dir(a) * (Boss.BodyRadius + PR + 0.35f);
            return Dir(a) * Mathf.Min(rings[h % 4] + (i >= 4 ? 0.8f : 0), Bound - 0.6f);
        }
        float Bound => Mathf.Min(11.6f, CombatWorld.PlayerBound - 0.1f);

        public override bool SafeToRoot(PlayerController p, float seconds, float scale)
        {
            if (!Running || p.Down || !plans.TryGetValue(p, out var pl) || !pl.Searched) return true;
            return RootTest(seconds, scale, () => Search(p, Sim.Flat(p.transform.position), T, pl.Goal, pl.Urgent, pl.BeatAt));
        }

        // bots: the boss is the target, but a swing roots its caster, and an echo bursts where you stood 2 s ago: no attack while one
        // of my own echoes is due here within 1.2 s (a solo Vanguard swinging against the boss stood still into its own echo)
        public override bool AttackTarget(PlayerController p, out Vector3 aim, out float reach, out bool fullKit)
        {
            aim = Boss.transform.position; fullKit = false; reach = 99;
            if (!Running || p.Down) return false;
            var q = Sim.Flat(p.transform.position); float t = T;
            reach = (q - Sim.Flat(Boss.transform.position)).magnitude - Boss.BodyRadius;
            if (p.ClassName == "Vanguard" && reach > 0.6f) reach = 99;   // melee swings only pressed against the boss: a lunge elsewhere carries it into bullets
            foreach (var m in mines) if (m.Owner == p && !m.Done && m.At > t && m.At <= t + 1.2f && (q - m.P).magnitude < EchoR + PR + 0.6f) { reach = 99; break; }
            GoalOf(p, t, out bool urgent, out _, out _); if (urgent) reach = 99;   // the plate or a curtain is due: no rooting swing
            return true;
        }

        readonly List<Vector3>[] pred = new List<Vector3>[16];
        Vector3 Search(PlayerController p, Vector3 pos, float t, Vector3 goal, bool urgent, float beatAt)
        {
            // 4 legs (0.8 s): with 3 a bot could walk into a pocket between its own echo and a curtain before it saw the exit shut
            const float Leg = 0.2f; const int Legs = 4, Samples = 4, BeamW = 12; float dtS = Leg / Samples; int N = Legs * Samples;
            var bossP = Sim.Flat(Boss.transform.position); int hour = HourOf(p);
            // the boss body pushes a player out to exactly BodyRadius + PR: one already pressed there (a swing's lunge) may stay
            float r0 = (pos - bossP).magnitude;
            for (int k = 0; k < N; k++)
            {
                float s = (k + 1) * dtS; var arr = pred[k] ?? (pred[k] = new List<Vector3>()); arr.Clear();
                float r = RewindIn(t, s);
                foreach (var b in bullets) { if (r >= 0 && !b.Rev) { float a = r - t, c = t + s - r; arr.Add(b.P + b.V * (a - Rev * c)); } else arr.Add(b.P + b.V * s); }
                for (float te = nextArm; te <= t + s; te += ArmRate) { if (RewindAt(te)) continue; for (int ka = 0; ka < Arms; ka++) arr.Add(bossP + Dir(ArmAngle(te) + 120 * ka) * (Boss.BodyRadius + ArmSpeed * (t + s - te))); }
                foreach (var m in mines)
                {
                    if (m.At > t + s || t + s - m.At > EchoShotLife) continue; float age = t + s - m.At;
                    for (int k2 = 0; k2 < EchoShots; k2++) arr.Add(m.P + Dir(360f / EchoShots * k2 + 45 * (m.OwnerHour % 2)) * EchoShotSpeed * age);
                }
            }
            // 0 safe, 1 a 50 hit (a bullet, the wall), 2 lethal or a wipe (my own echo, another's gap, off the plate on the beat, the edge)
            int Severity(Vector3 q, int k, ref float extra, out float mc)
            {
                float s = (k + 1) * dtS; mc = 9;
                float rq = (q - bossP).magnitude, rc = q.magnitude; if (rc > Bound - 0.1f || rq < Mathf.Min(Boss.BodyRadius + PR + 0.05f, r0 - 0.02f)) return 2;
                foreach (var bp in pred[k]) { float c = (q - bp).magnitude - PR - BR; if (c < mc) mc = c; }
                foreach (var m in mines) if (m.Owner == p && m.At > t + s - dtS - 0.02f && m.At <= t + s + 0.02f && (q - m.P).magnitude < EchoR + PR + 0.5f) return 2;
                for (int ci = 0; ci < 3; ci++) { if (Crossed(p)[ci]) continue; int ch = CurtainHit(ci, q, t + s, hour, 0.2f); if (ch == 2) return 2; if (ch == 1) return 1; }
                if (beatAt >= 0 && Mathf.Abs(t + s - beatAt) <= dtS * 0.6f && (q - goal).magnitude > OnPlate - 0.3f) return 2;
                if (mc < 0.12f) return 1;
                // leave my own echo spots early: a burst due within the next second costs, so the route is clear before the pocket closes
                foreach (var m in mines) if (m.Owner == p && m.At > t + s && m.At <= t + s + 1f && (q - m.P).magnitude < EchoR + PR + 0.6f) extra += 1f;
                int kb = Mathf.FloorToInt((t + s) / Beat) + 1; float tbb = kb * Beat - (t + s);
                if (SoakHour(kb) != hour % Hours && tbb < 3 && (q - PlateOf(kb)).magnitude < OnPlate + EchoR + 0.3f) extra += 0.5f;   // keep my echoes off the next soaker's plate
                return 0;
            }
            var dirs = new List<Vector3>(); for (int k = 0; k < 24; k += 2) dirs.Add(Dir(15 * k));
            var tg = goal - pos; if (tg.magnitude > 1e-3f) dirs.Add(tg.normalized);
            for (int ci = 0; ci < 3; ci++) if (!Crossed(p)[ci] && (CurtOn(ci, t) || CurtSoon(ci, t))) { var d = Dir(Curtains[ci].a); dirs.Add(d); dirs.Add(-d); }
            var vels = new List<Vector3> { Vector3.zero };
            foreach (var d in dirs) { vels.Add(d * PlayerController.WalkSpeed); vels.Add(d * PlayerController.SprintSpeed); vels.Add(d * PlayerController.WalkSpeed * 0.4f); }
            // moves at the player's speed while its current action (or a tested press) slows it: a swing roots to its end
            var (slowFor, slowMs) = SlowOf(p); lastClean = true;
            var beam = new List<(Vector3 q, Vector3 first, float risk, float score)> { (pos, Vector3.zero, 0f, 0f) };
            (Vector3 v, float score)? fallback = null;
            for (int leg = 0; leg < Legs; leg++)
            {
                var next = new List<(Vector3 q, Vector3 first, float risk, float score)>();
                foreach (var r in beam)
                    foreach (var v in vels)
                    {
                        var q = r.q; float risk = r.risk; bool dead = false; int worst = 0;
                        for (int k2 = 0; k2 < Samples; k2++)
                        {
                            q += v * dtS * ((leg * Samples + k2) * dtS < slowFor ? slowMs : 1f); float extra = 0;
                            int sev = Severity(q, leg * Samples + k2, ref extra, out float mc);
                            if (sev > 0) { dead = true; worst = sev; break; }
                            risk += Mathf.Max(0, 0.5f - mc) * 2 + extra;
                        }
                        var first = leg == 0 ? v : r.first;
                        if (dead) { if (leg == 0) { float sc = risk + 1000 * worst + (q - goal).magnitude;   /* cornered: a 50 hit beats a lethal one */ if (fallback == null || sc < fallback.Value.score) fallback = (v, sc); } continue; }
                        next.Add((q, first, risk, risk + (q - goal).magnitude * (urgent ? 1.6f : 0.8f) + (v.magnitude > PlayerController.WalkSpeed ? 0.15f : 0)));
                    }
                if (next.Count == 0) { lastClean = false; if (leg == 0) { if (LogSteer) Debug.Log($"[open hour] {t:0.00}s {UltimateState.Roman[hour + 1]} cornered at ({pos.x:0.0},{pos.z:0.0}) goal ({goal.x:0.0},{goal.z:0.0}) urgent {urgent} · bullets {bullets.Count} · fallback {(fallback.HasValue ? fallback.Value.score.ToString("0") : "-")}"); return fallback?.v ?? Vector3.zero; } break; }
                next.Sort((x, y) => x.score.CompareTo(y.score));
                beam = next.GetRange(0, Mathf.Min(BeamW, next.Count));
            }
            return beam[0].first;
        }

        // ---------------- presentation (stand-ins: brass / lavender / ivory bullet spheres, echo discs, plates, labels) ----------------
        Transform root, hand; readonly Renderer[] plates = new Renderer[4];
        readonly List<Transform> pool = new List<Transform>();
        static Material brassB, revB, echoB, ivoryB, plateDim, plateHot, handM;
        float handYaw, handVel;
        void Build()
        {
            if (root) return;
            if (!brassB)
            {
                brassB = Prim.Material(new Color(1f, 0.78f, 0.35f), 2.2f); revB = Prim.Material(new Color(1f, 0.55f, 0.4f), 2.4f);
                echoB = Prim.Material(new Color(1f, 0.86f, 0.78f), 2.0f);   // echo shots: warm ivory, coral glow (no Arcanist violet, gpt/08 §4) ivoryB = Prim.Material(new Color(0.97f, 0.95f, 0.88f), 2.2f);
                plateDim = Prim.Material(new Color(0.55f, 0.43f, 0.2f), 0.35f); plateHot = Prim.Material(new Color(1f, 0.8f, 0.4f), 2.2f);
                handM = Prim.Material(new Color(0.9f, 0.7f, 0.32f), 1.4f);
            }
            root = new GameObject("The Open Hour (stand-in)").transform; root.SetParent(W.Root, false);
            for (int i = 0; i < 4; i++) { var t = Prim.Make(PrimitiveKind.Cylinder, root, Dir(90 * i) * PlateR + Vector3.up * 0.04f, new Vector3(3.2f, 0.02f, 3.2f), Color.white); plates[i] = t.GetComponent<Renderer>(); Mat(t, plateDim); UltArt.DressPlate(t); }
            hand = new GameObject("hand").transform; hand.SetParent(root, false);
            Mat(Prim.Make(PrimitiveKind.Cube, hand, new Vector3(0, 0.06f, 3.6f), new Vector3(0.45f, 0.03f, 4.6f), Color.white), plateHot);
            UltArt.DressHand(hand);
        }
        static void Mat(Transform t, Material m) { var r = t.GetComponent<Renderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
        void Present(float t)
        {
            if (!root) return;
            int k = Mathf.FloorToInt(t / Beat) + 1, lit = ((k % 4) + 4) % 4;
            for (int i = 0; i < 4; i++) if (plates[i]) plates[i].sharedMaterial = i == lit ? plateHot : plateDim;
            UltArt.TickHand(hand, ref handYaw, ref handVel, Quaternion.LookRotation(Dir(90f * k)).eulerAngles.y, Sim.Dt);   // a clockwork snap
            for (int i = 0; i < 4; i++) if (i == lit) UltArt.BreatheRim(plates[i], plateHot, t); else UltArt.StillRim(plates[i]);
            int n = 0;
            void Ball(Vector3 p, Material m, float size)
            {
                if (pool.Count <= n) { var tr = Prim.Make(PrimitiveKind.Sphere, root, Vector3.zero, Vector3.one, Color.white); Mat(tr, m); pool.Add(tr); }
                var b = pool[n++]; b.gameObject.SetActive(true); b.position = new Vector3(p.x, 0.9f, p.z); b.localScale = Vector3.one * size;
                var rr = b.GetComponent<Renderer>(); if (rr.sharedMaterial != m) rr.sharedMaterial = m;
            }
            foreach (var b in bullets) Ball(b.P, b.Echo ? echoB : b.Rev ? revB : brassB, BR * (b.Echo ? 1.7f : 2.2f));   // echo shots smaller than the Mainspring's
            for (int ci = 0; ci < 3; ci++)
            {
                if (!CurtOn(ci, t)) continue; var d = Dir(Curtains[ci].a); var pp = Perp(d); float line = CurtLine(ci, t);
                for (float o = -12.4f; o <= 12.4f; o += 0.62f)
                {
                    bool gap = false; foreach (var x in GapOff) if (Mathf.Abs(o - x) < GapHalf) gap = true;
                    if (gap) continue; var q = pp * o + d * line; if (q.magnitude > 12.2f) continue; Ball(q, ivoryB, BR * 2.4f);
                }
            }
            for (int i = n; i < pool.Count; i++) pool[i].gameObject.SetActive(false);
            // Echoes (accepted 28 Oct, VIOLET-PROPOSAL-2026-10-28.md): a fixed outline at the true kill distance (EchoR + PR = 1.55 m) from
            // the moment it is left; your own: ivory rim, a coral fill growing outward over its last 0.8 s and a flash on the burst; the
            // others' (they can't kill you, their shots can): dashed and half see-through. No Arcanist violet, no growing filled disc.
            var me = W.Players.Count > 0 ? W.Players[0] : null;
            foreach (var m in mines)
            {
                if (m.Tele == null)
                {
                    m.Tele = W.BossFx.Circle(EchoR + PR); m.Tele.Place(m.P, 0); m.Tele.SpentFast = true;
                    if (m.Owner == me) m.Tele.Tint(new Color(1f, 0.42f, 0.32f), new Color(0.97f, 0.94f, 0.86f)); else m.Tele.Memory();
                }
                if (m.Done) { if (!m.Burst) { m.Burst = true; if (m.Owner == me) m.Tele.Resolve(); else m.Tele.Dismiss(); } continue; }
                m.Tele.Progress(m.Owner == me ? Mathf.Clamp01(1 - (m.At - t) / (0.8f + WarnLead)) : 0);
            }
        }
        void ClearMines() { foreach (var m in mines) if (m.Tele != null && !m.Burst) m.Tele.Dismiss(); mines.Clear(); }
        void Teardown()
        {
            if (root) Object.Destroy(root.gameObject);
            root = hand = null; pool.Clear(); for (int i = 0; i < 4; i++) plates[i] = null; plans.Clear();
        }

        static GUIStyle nameStyle, bigStyle, numStyle;
        public void DrawGui(Camera cam)
        {
            if (!Running || cam == null || !root) return;
            if (nameStyle == null)
            {
                nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                numStyle = new GUIStyle(nameStyle) { fontSize = 20 }; bigStyle = new GUIStyle(nameStyle) { fontSize = 40 };
            }
            float t = T; int k = Mathf.FloorToInt(t / Beat) + 1, s = SoakHour(k);
            Label(cam, PlateOf(k), 0.3f, UltimateState.Roman[s + 1] + " SOAK in " + (k * Beat - t).ToString("0.0") + "s", EscapementRelay.DutyColors[0], nameStyle);
            for (int ci = 0; ci < 3; ci++)
            {
                bool on = CurtOn(ci, t), soon = CurtSoon(ci, t); if (!on && !soon) continue;
                var d = Dir(Curtains[ci].a); var pp = Perp(d); float line = on ? CurtLine(ci, t) : -10.5f;
                for (int g = 0; g < 4; g++) { var q = pp * GapOff[g] + d * line; if (q.magnitude < 12.3f) Label(cam, q, 0.9f, UltimateState.Roman[orders[ci][g] + 1], new Color(1f, 0.82f, 0.48f), numStyle); }
                if (soon) Label(cam, d * -10.8f, 0.3f, "CURTAIN in " + (CurtStart(ci, t) - t).ToString("0.0") + "s", Color.white, nameStyle);
            }
            // your own echoes carry your hour numeral: they only kill you
            if (W.Players.Count > 0) foreach (var m in mines) if (!m.Done && m.Owner == W.Players[0]) Label(cam, m.P, 0.15f, UltimateState.Roman[m.OwnerHour + 1], new Color(0.97f, 0.94f, 0.86f), numStyle);
            for (int i = 0; i < W.Players.Count; i++)
            {
                var p = W.Players[i]; if (p.Down) continue;
                var job = JobOf(p) ?? (SoakHour(k) == HourOf(p) % Hours ? "SOAK next" : "KEEP MOVING");
                Label(cam, p.transform.position, 2.9f, (i == 0 ? "YOU " : "") + UltimateState.Roman[HourOf(p) + 1] + "  " + job, job.StartsWith("SOAK") ? EscapementRelay.DutyColors[0] : job.StartsWith("CURTAIN") ? new Color(1f, 0.82f, 0.48f) : Color.white, nameStyle);
            }
            var rs = RewindSoon(t);
            if (rs.HasValue || RewindAt(t))
            {
                bigStyle.normal.textColor = RewindAt(t) ? new Color(1f, 0.6f, 0.45f) : Color.Lerp(new Color(1f, 0.4f, 0.3f), Color.white, Mathf.PingPong(Time.unscaledTime * 3, 1));
                GUI.Label(new Rect(0, Screen.height * 0.2f, Screen.width, 60), RewindAt(t) ? "<< REWIND" : $"REWIND in {rs.Value:0.0}s", bigStyle);
            }
        }
        static void Label(Camera cam, Vector3 world, float height, string text, Color c, GUIStyle style)
        {
            var sp = cam.WorldToScreenPoint(new Vector3(world.x, height, world.z)); if (sp.z <= 0) return;
            var r = new Rect(sp.x - 130, Screen.height - sp.y - 13, 260, 26);
            style.normal.textColor = new Color(0, 0, 0, 0.85f); GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, style);
            style.normal.textColor = c; GUI.Label(r, text, style);
        }
        public string HudText(PlayerController p)
        {
            if (!Running || p == null) return null;
            float t = T; int k = Mathf.FloorToInt(t / Beat) + 1;
            return $"plate {UltimateState.Roman[SoakHour(k) + 1]} in {k * Beat - t:0.0}s · you ({UltimateState.Roman[HourOf(p) + 1]}): {JobOf(p) ?? "keep moving (your echo bursts where you stood 2 s ago)"}";
        }

        public override void Cancel()
        {
            base.Cancel(); bullets.Clear(); ClearMines(); Teardown(); stop = false;
        }
        public void ResetCounts() { Beats = 0; BeatsClean = 0; Fails.Clear(); }
    }
}
