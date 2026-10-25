using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // Final Toll (`bellkeeper.ult_final_toll`), Bellkeeper Ultimate v2 P5 — proposal, built 2026-10-25 from the browser
    // simulation Chzy18 asked to build (tools/bellkeeper-ultimate/sims/p5-example.html, scene 3, "do 1 2 and 3", then
    // "2": faster tolls, stacked callbacks, the Mainspring, the echo ring, a tight enrage); not human-approved.
    // Inside the great bell. The bell tolls at TollAt[k]: every 4 s for I-VI, then every 3.5 s; XIII (48.5 s) is the hard
    // enrage (WIPE unless the boss is dead). Every toll:
    //   CLAPPER   a 3 m lane straight across the bell at a seeded angle, shown 1.6 s before, 999 on the toll. It always
    //             misses the plate.
    //   SOUND     a ring rushes in from the rim at 5 m/s to the boss with four gaps (I-IV; each hour's gap moves 60° a toll).
    //             Your own gap only: another hour's gap 999, the ring itself 50.
    //   ECHO      tolls X-XII: a second ring 0.8 s behind, every gap 45° further round (same hours).
    //   CALLBACKS PLATE (P1/P2: one player of the next hour soaks the plate by its last gap or WIPE; standing on it pays
    //             debts), DEBTS (P1: a 2 m ring under everyone that detonates next toll), INVERTED (P2: the half centred
    //             on the INVERTED hour's last gap slams: that hour inside, everyone else out), HAMMER (P4: 12 shards from a
    //             point near the rim, marked 1.6 s before). One a toll for I-VI, two for VII-IX, all four for X-XII.
    //   RESONANCE inside the bell every hit on the boss counts double (BellkeeperEncounter.V2P5Resonance)
    //   MAINSPRING from toll IV, P3's three spiral arms fire between sound rings, stopping 0.8 s before each toll (50).
    // Hours are W.Players index % 4 (5-8 players: two per hour). An hour with nobody in it asks nothing (no plate that toll;
    // nobody may stand in the INVERTED half). Bots steer (Steer) with the simulation's route search.
    public sealed class FinalToll : UltimateMechanic
    {
        public override string Id => UltimateMechanics.FinalToll;
        public override string Name => "Final Toll";

        public const string Source = "Final Toll";
        public static readonly float[] TollAt = { 0, 4, 8, 12, 16, 20, 24, 27.5f, 31, 34.5f, 38, 41.5f, 45, 48.5f };
        public const float RingStart = 11.6f, RingSpeed = 5f, GapHalf = 16f, RingHalf = 0.3f, LaneHalf = 1.5f, LaneShow = 1.6f,
            PlateR = 5.5f, PlateSize = 1.6f, DebtR = 2f, EchoDelay = 0.8f, ArmEvery = 0.45f, ArmSpeed = 3.5f, ArmTurn = 35f,
            ShardSpeed = 4f, BR = 0.22f, PR = 0.45f, Bound = 11.1f;
        const int BulletDamage = 50, RingDamage = 50;
        static readonly string[] KN = { "PLATE", "DEBTS", "INVERTED", "HAMMER" };

        sealed class Bullet { public Vector3 P, V; public bool Gone; public string What; }
        sealed class Debt { public Vector3 P; public int At, Owner; public Hazard H; }
        readonly List<Bullet> bullets = new List<Bullet>();
        readonly List<Debt> debts = new List<Debt>();
        int t0, tolls, seed; float nextArm;
        public int Tolls => tolls;
        public readonly List<string> Fails = new List<string>();

        public override UltCue DefaultCue() => new UltCue { Variant = "practice" };

        // ---------------- the rule (plan angles: Dir(a) = EscapementRelay.PlanDir) ----------------
        float T => (Now - t0) / (float)Sim.Hz;
        static Vector3 Dir(float a) => EscapementRelay.PlanDir(a);
        static float AngOf(Vector3 q) => (Mathf.Atan2(-q.x, -q.z) * Mathf.Rad2Deg + 360) % 360;
        static float AngDiff(float a, float b) => Mathf.Abs(((a - b) % 360 + 540) % 360 - 180);
        static float Cross(Vector3 d, Vector3 q) => d.x * q.z - d.z * q.x;
        static int Cur(float t) { int k = 0; while (k < 13 && TollAt[k + 1] <= t) k++; return k; }   // tolls already rung
        static float NextAt(int k) => TollAt[Mathf.Min(k, 13)];
        static int[] Kinds(int k) { int b = (k - 1) % 4; return k <= 6 ? new[] { b } : k <= 9 ? new[] { b, (b + 2) % 4 } : new[] { 0, 1, 2, 3 }; }
        static bool Has(int k, int kind) => k >= 1 && k < 13 && System.Array.IndexOf(Kinds(k), kind) >= 0;
        static int Soaker(int k) => (k - 1) % 4;
        static int InvHour(int k) => k % 4;
        uint Hash(int k, int salt) { uint x = (uint)(k * 2654435761u) + (uint)((seed + salt) * 40503); x ^= x >> 15; x *= 2246822519u; x ^= x >> 13; return x; }
        static float GapCentre(int k, int j) => (45 + 90 * j + 30 * k) % 360;
        static int GapHour(int k, int j) => (j + k) % 4;
        static float GapOf(int k, int h, bool echo) { for (int j = 0; j < 4; j++) if (GapHour(k, j) == h) return (GapCentre(k, j) + (echo ? 45 : 0)) % 360; return 0; }
        static bool Echoes(int k) => k >= 10 && k <= 12;
        // where hour h last passed a ring before toll k (the echo's gap when that toll had one)
        static float LastGap(int k, int h) { int kk = k - 1; return kk < 1 ? 45 + 90 * h : GapOf(kk, h, Echoes(kk)); }
        float InvAng(int k) => LastGap(k, InvHour(k));
        float PlateAng(int k) { float b = LastGap(k, Soaker(k)), a = LastGap(k, InvHour(k)), d = ((a - b) % 360 + 540) % 360 - 180; return b - (d >= 0 ? 20 : -20); }
        Vector3 PlatePos(int k) => Dir(PlateAng(k)) * PlateR;
        float LaneAng(int k)
        {
            float a = Hash(k, 3) % 180;
            if (Has(k, 0)) { var pl = PlatePos(k); for (int n = 0; n < 26; n++) { if (Mathf.Abs(Cross(Dir(a), pl)) > LaneHalf + PR + 1.8f) break; a = (a + 7) % 180; } }
            return a;
        }
        bool InLane(int k, Vector3 q, float margin = 0) => Mathf.Abs(Cross(Dir(LaneAng(k)), Sim.Flat(q))) < LaneHalf + PR + margin;
        Vector3 HammerAt(int k) => Dir(Hash(k, 9) % 360) * 9f;
        float RingR(float t) { int k = Cur(t); if (k < 1 || k >= 13) return -1; float r = RingStart - RingSpeed * (t - TollAt[k]); return r > Boss.BodyRadius + 0.3f ? r : -1; }
        float EchoV(float t) { int k = Cur(t); return Echoes(k) ? RingStart - RingSpeed * (t - TollAt[k] - EchoDelay) : -1; }
        float EchoR(float t) { float r = EchoV(t); return r > Boss.BodyRadius + 0.3f && r <= RingStart ? r : -1; }
        // 0 nothing, 1 the ring itself (50), 2 another hour's gap (999)
        int RingHit(int hour, Vector3 q, float t, float margin, bool echo)
        {
            float R = echo ? EchoR(t) : RingR(t); if (R < 0) return 0;
            q = Sim.Flat(q); if (Mathf.Abs(q.magnitude - R) > RingHalf + PR) return 0;
            int k = Cur(t); float a = AngOf(q);
            for (int j = 0; j < 4; j++) if (AngDiff(a, GapCentre(k, j) + (echo ? 45 : 0)) < GapHalf - margin * 20) return GapHour(k, j) == hour ? 0 : 2;
            return 1;
        }
        bool Firing(float t) { int k = Cur(t); return t >= TollAt[4] && k < 13 && RingR(t) < 0 && EchoR(t) < 0 && TollAt[k + 1] - t > 0.8f; }
        static float ArmAng(float t) => ArmTurn * t;
        int HourOf(PlayerController p) { int i = W.Players.IndexOf(p); return i < 0 ? -1 : i % 4; }
        bool HourPresent(int h) { foreach (var p in W.Players) if (!p.Down && HourOf(p) == h) return true; return false; }
        bool OnPlate(int k, Vector3 q, float slack = 0) => Has(k, 0) && HourPresent(Soaker(k)) && (Sim.Flat(q) - PlatePos(k)).magnitude < PlateSize + slack;
        static bool Melee(PlayerController p) => p.ClassName == "Vanguard";

        // ---------------- cast ----------------
        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name);
            t0 = Now; tolls = 0; seed = Cue.Seed % 997; nextArm = TollAt[4]; bullets.Clear(); debts.Clear(); plans.Clear(); ringHits.Clear();
            Build();
            Hazard lane = null, half = null, mark = null;
            while (true)
            {
                float t = T, nt = t + Sim.Dt; int c = Cur(t), k = c + 1;
                // the Mainspring
                while (nextArm <= nt)
                {
                    if (Firing(nextArm)) for (int j = 0; j < 3; j++) { var d = Dir(ArmAng(nextArm) + 120 * j); bullets.Add(new Bullet { P = d * Boss.BodyRadius, V = d * ArmSpeed, What = "a spiral bullet" }); }
                    nextArm += ArmEvery;
                }
                foreach (var b in bullets) b.P += b.V * Sim.Dt;
                foreach (var p in W.Players)
                {
                    if (p.Down) continue; var q = Sim.Flat(p.transform.position);
                    foreach (var b in bullets) if (!b.Gone && (q - b.P).magnitude < PR + BR) { b.Gone = true; Hit(p, BulletDamage, b.What); }
                }
                bullets.RemoveAll(b => b.Gone || b.P.magnitude > 12.5f);
                // telegraphs for the next toll
                if (k < 13)
                {
                    float left = TollAt[k] - t;
                    if (lane == null && left <= LaneShow)
                    {
                        float yaw = LaneAng(k) + 180;   // Sim.Dir(yaw) = Dir(plan angle)
                        lane = Spawn(new Hazard { Source = Source + ": the clapper", Shape = HazardShape.Lane, Kind = HazardKind.Strike, Origin = -Sim.Dir(yaw) * 12f, Yaw = yaw, Length = 24, Width = LaneHalf * 2, LockAt = 0, ResolveAt = UltShapes.ResolveIn(Mathf.Max(1, Sim.Ticks(left))), Damage = 0, Knockback = 0, Stun = 0, HitOnResolve = false });
                        lane.OnResolve = hz => { W.BossFx.TollResolve(Vector3.zero, 0.6f, 11f); CameraShake.Shake(0.2f, 0.3f); };
                    }
                    if (half == null && Has(k, 2))
                        half = Spawn(new Hazard { Source = Source + ": INVERTED", Shape = HazardShape.Cone, Kind = HazardKind.Ground, Origin = Vector3.zero, Yaw = InvAng(k) + 180, HalfAngle = 90, Radius = 11.6f, LockAt = 0, ResolveAt = UltShapes.ResolveIn(Mathf.Max(1, Sim.Ticks(left))), Damage = 0, Knockback = 0, Stun = 0, HitOnResolve = false });
                    if (mark == null && Has(k, 3) && left <= LaneShow)
                        mark = Spawn(new Hazard { Source = Source + ": the hammer", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = HammerAt(k), Radius = 0.9f, LockAt = 0, ResolveAt = UltShapes.ResolveIn(Mathf.Max(1, Sim.Ticks(left))), Damage = 0, Knockback = 0, Stun = 0, HitOnResolve = false });
                }
                // the toll
                int kn = Cur(nt);
                if (kn >= 1 && kn > tolls)
                {
                    tolls = kn; lane = half = mark = null;
                    if (kn >= 13) { Fails.Add("XIII"); Boss.PlanLog.Add((Now, "final toll XIII")); Ult.Wipe("The Thirteenth Hour"); break; }
                    Toll(kn);
                    if (Boss.Ult.WipeCause != null) break;
                }
                // the rings
                foreach (var p in W.Players)
                {
                    if (p.Down) continue; int h = HourOf(p);
                    for (int e = 0; e < 2; e++)
                    {
                        int hit = RingHit(h, p.transform.position, nt, 0, e == 1); if (hit == 0) continue;
                        var key = (p, e); if (ringHits.TryGetValue(key, out int at) && at == kn) continue;
                        ringHits[key] = kn; Hit(p, hit == 2 ? UltimateState.Lethal : RingDamage, hit == 2 ? (e == 1 ? "another hour's echo gap" : "another hour's gap") : (e == 1 ? "the echo" : "the sound ring"));
                    }
                }
                if (Boss.Ult.WipeCause != null) break;
                Present(nt);
                yield return 1;
            }
            bullets.Clear(); debts.Clear();
            Teardown();
        }
        readonly Dictionary<(PlayerController, int), int> ringHits = new Dictionary<(PlayerController, int), int>();

        void Toll(int k)
        {
            var ks = Kinds(k);
            Boss.PlanLog.Add((Now, $"final toll {UltimateState.Roman[Mathf.Min(k, 12)]} {string.Join("+", System.Array.ConvertAll(ks, x => KN[x]))}"));
            foreach (var p in W.Players) if (!p.Down && InLane(k, p.transform.position)) Hit(p, UltimateState.Lethal, "the clapper");
            foreach (var d in debts)
                if (d.At == k)
                    foreach (var p in W.Players)
                        if (!p.Down && !OnPlate(k, p.transform.position) && (Sim.Flat(p.transform.position) - d.P).magnitude < DebtR + PR) Hit(p, UltimateState.Lethal, $"{UltimateState.Roman[d.Owner + 1]}'s debt");
            debts.RemoveAll(d => d.At <= k);
            if (Has(k, 0) && HourPresent(Soaker(k)))
            {
                bool held = false; foreach (var p in W.Players) if (!p.Down && HourOf(p) == Soaker(k) && OnPlate(k, p.transform.position)) held = true;
                if (!held) { Fails.Add($"toll {k}: plate empty"); Ult.Wipe("The plate was empty"); return; }
            }
            if (Has(k, 2))
            {
                var d = Dir(InvAng(k)); int ih = InvHour(k);
                foreach (var p in W.Players)
                {
                    if (p.Down) continue; float o = Vector3.Dot(Sim.Flat(p.transform.position), d);
                    if (HourOf(p) == ih ? o <= 0.6f : o >= -0.6f) Hit(p, UltimateState.Lethal, HourOf(p) == ih ? "the bell (inverted)" : "the slam");
                }
            }
            if (Has(k, 3)) { var c = HammerAt(k); for (int j = 0; j < 12; j++) bullets.Add(new Bullet { P = c, V = Dir(30 * j + 7 * k) * ShardSpeed, What = "a hammer shard" }); }
            if (Has(k, 1) && k < 12)
                foreach (var p in W.Players)
                {
                    if (p.Down) continue; var pos = Sim.Flat(p.transform.position);
                    var h = Spawn(new Hazard { Source = Source + ": a debt", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = pos, Radius = DebtR, LockAt = 0, ResolveAt = UltShapes.ResolveIn(Mathf.Max(1, Sim.Ticks(TollAt[k + 1] - TollAt[k]))), Damage = 0, Knockback = 0, Stun = 0, HitOnResolve = false });
                    debts.Add(new Debt { P = pos, At = k + 1, Owner = HourOf(p), H = h });
                }
        }

        void Hit(PlayerController p, int dmg, string what)
        {
            Fails.Add($"{T:0.0}s {UltimateState.Roman[HourOf(p) + 1]}: {what}");
            p.ReceiveHit(new Hazard { Source = Source + ": " + what, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = Sim.Flat(p.transform.position), Radius = 0.5f, Damage = dmg, Knockback = 0, Stun = 0 });
        }

        // ---------------- bots ----------------
        sealed class Plan { public Vector3 V; public int Until; public string Job; public Vector3 Goal; }
        readonly Dictionary<PlayerController, Plan> plans = new Dictionary<PlayerController, Plan>();
        public string JobOf(PlayerController p) => plans.TryGetValue(p, out var pl) ? pl.Job : null;
        public static bool Log;   // tests: every time a bot finds no safe route

        // the nearest spot to 'to' clear of toll k's lane and debts, inside the bell, that passes need
        Vector3 Clear(int k, Vector3 to, System.Func<Vector3, bool> need)
        {
            for (int n = 0; n <= 7; n++)
                for (int a = 0; a < (n == 0 ? 1 : 12); a++)
                {
                    var c = to + Dir(30 * a + 15 * n) * (n * 0.9f); float r = c.magnitude;
                    if (r > 10.5f || r < 2.6f || (need != null && !need(c))) continue;
                    if (k < 13 && InLane(k, c, 0.6f)) continue;
                    bool ok = true; foreach (var d in debts) if (d.At == k && (c - d.P).magnitude <= DebtR + PR + 0.6f) ok = false;
                    if (ok) return c;
                }
            return to;
        }

        Vector3 GoalOf(PlayerController p, float t, UltimateBot.Skill skill, out string job)
        {
            int c = Cur(t), k = c + 1, h = HourOf(p), i = W.Players.IndexOf(p); float left = NextAt(k) - t;
            var pos = Sim.Flat(p.transform.position); float myR = pos.magnitude;
            if (skill == UltimateBot.Skill.Miss && k == 7 && h == 2) { job = "forgets its debt"; return pos; }
            // the rings: line up with my gap at my radius before the next one reaches me (the echo once the first has passed)
            float R1 = RingR(t), R2 = EchoV(t);
            if (R1 > 0 && R1 > myR - 1.2f && (R1 - myR) / RingSpeed < 2.4f) { job = "gap " + UltimateState.Roman[h + 1]; return Dir(GapOf(c, h, false)) * Mathf.Clamp(myR, 3.4f, 10.5f); }
            if (R2 > Boss.BodyRadius + 0.3f && R2 > myR - 1.2f && (R2 - myR) / RingSpeed < 2.4f) { job = "echo gap " + UltimateState.Roman[h + 1]; return Dir(GapOf(c, h, true)) * Mathf.Clamp(myR, 3.4f, 10.5f); }
            if (k < 13 && Has(k, 0) && Soaker(k) == h && left < 2.6f) { job = "SOAK"; return PlatePos(k); }
            if (k < 13 && Has(k, 2))
            {
                var d = Dir(InvAng(k));
                if (h == InvHour(k)) { job = "INVERTED"; return Clear(k, d * 5f, q => Vector3.Dot(q, d) > 1.4f); }
                float o = Vector3.Dot(pos, d); var st = o > -1.6f ? pos - d * (o + 1.8f) : pos; float r = st.magnitude;
                if (r < 2.8f) st = st * (2.8f / Mathf.Max(r, 1e-3f)); else if (r > 10f) st = st * (10f / r);
                job = "out of the slam"; return Clear(k, st, q => Vector3.Dot(q, d) < -1.4f);
            }
            float a = (k < 13 ? LaneAng(k) : 0) + 90 + (i % 2 == 1 ? 0 : 180) + ((i % 4) < 2 ? -25 : 25) + (i >= 4 ? 40 : 0);
            job = Melee(p) ? "melee" : "ranged";
            return Clear(k, Dir(a) * (Melee(p) ? 3.1f : 5.2f), null);
        }

        public override bool Steer(PlayerController p, UltimateBot.Skill skill, out Vector3 vel)
        {
            vel = Vector3.zero;
            if (!Running || p.Down) return false;
            if (plans.TryGetValue(p, out var cur) && Now < cur.Until) { vel = cur.V; return true; }
            float t = T; var goal = GoalOf(p, t, skill, out string job);
            var v = job == "forgets its debt" ? Vector3.zero : Search(p, Sim.Flat(p.transform.position), t, goal);
            plans[p] = new Plan { V = v, Until = Now + (skill == UltimateBot.Skill.Late ? 15 : 3), Job = job, Goal = goal };
            vel = v; return true;
        }

        public override bool AttackTarget(PlayerController p, out Vector3 aim, out float reach, out bool fullKit)
        {
            aim = Boss.transform.position; reach = 99; fullKit = false;
            if (!Running) return false;
            var q = Sim.Flat(p.transform.position); float t = T; int k = Cur(t) + 1;
            reach = q.magnitude - Boss.BodyRadius - PR;
            float left = NextAt(k) - t, R1 = RingR(t), R2 = EchoV(t), myR = q.magnitude;
            bool ringSoon = (R1 > 0 && R1 > myR - 1.2f && (R1 - myR) / RingSpeed < 0.7f) || (R2 > 0 && R2 > myR - 1.2f && (R2 - myR) / RingSpeed < 0.7f);
            bool settled = plans.TryGetValue(p, out var pl) && (q - Sim.Flat(pl.Goal)).magnitude < (Melee(p) ? 1.5f : 3f);
            // casts slow you (the Vanguard's basic roots it): ranged swing on the move, melee once near its spot; never with a ring
            // or the toll close (the route search counts the slowdown)
            float nearB = 9; foreach (var b in bullets) nearB = Mathf.Min(nearB, (b.P - q).magnitude);
            if (!settled || ringSoon || left < (Melee(p) ? 1.3f : 0.6f) || (Melee(p) && nearB < 2.2f)) reach = 99;   // nor a lunge into a bullet   // the Vanguard's combo lunges: not into the lane
            fullKit = settled && !ringSoon && left > 1.6f;
            return true;
        }

        readonly List<Vector3>[] pred = new List<Vector3>[12];
        Vector3 Search(PlayerController p, Vector3 pos, float t, Vector3 goal)
        {
            const float Leg = 0.2f; const int Legs = 3, Samples = 4, BeamW = 10; float dtS = Leg / Samples; int N = Legs * Samples;
            int hour = HourOf(p);
            for (int s = 0; s < N; s++)
            {
                float tq = t + (s + 1) * dtS; var arr = pred[s] ?? (pred[s] = new List<Vector3>()); arr.Clear();
                foreach (var b in bullets) arr.Add(b.P + b.V * (tq - t));
                for (float te = nextArm; te <= tq; te += ArmEvery) if (Firing(te)) for (int j = 0; j < 3; j++) arr.Add(Dir(ArmAng(te) + 120 * j) * (Boss.BodyRadius + ArmSpeed * (tq - te)));
                int kq = Cur(tq); if (kq > tolls && kq < 13 && Has(kq, 3)) { var c = HammerAt(kq); for (int j = 0; j < 12; j++) arr.Add(c + Dir(30 * j + 7 * kq) * ShardSpeed * (tq - TollAt[kq])); }
            }
            float ms = p.Kit != null ? p.Kit.MoveScale : 1f;
            int Dead(Vector3 q, int s, ref float risk)
            {
                float tq = t + (s + 1) * dtS, r = q.magnitude;
                if (r > Bound) return 2;
                float cb = r - Boss.BodyRadius - PR; if (cb < -0.05f) return 2; if (cb < 0.4f) risk += (0.4f - cb) * 2;
                int k = Cur(tq + dtS * 0.7f);
                if (k >= 1 && k < 13 && Mathf.Abs(tq - TollAt[k]) < dtS * 0.7f)
                {
                    if (InLane(k, q)) return 2;
                    bool onPl = OnPlate(k, q, -0.3f);
                    if (!onPl) foreach (var d in debts) if (d.At == k && (q - d.P).magnitude < DebtR + PR + 0.25f) return 2;
                    if (Has(k, 2)) { float o = Vector3.Dot(q, Dir(InvAng(k))); if (hour == InvHour(k) ? o <= 0.9f : o >= -0.9f) return 2; }
                    if (Has(k, 0) && Soaker(k) == hour && (q - PlatePos(k)).magnitude > PlateSize - 0.3f) return 2;
                }
                int rh = RingHit(hour, q, tq, 0.15f, false); if (rh != 0) return rh;
                rh = RingHit(hour, q, tq, 0.15f, true); if (rh != 0) return rh;
                float mc = 9; foreach (var b in pred[s]) { float cc = (q - b).magnitude - PR - BR; if (cc < mc) mc = cc; }
                if (mc < 0.12f) return 1; if (mc < 0.5f) risk += (0.5f - mc) * 2;
                return 0;
            }
            var dirs = new List<Vector3>(); for (int j = 0; j < 16; j++) dirs.Add(Dir(22.5f * j));
            var tg = goal - pos; if (tg.magnitude > 1e-3f) dirs.Add(tg.normalized);
            var vels = new List<Vector3> { Vector3.zero };
            foreach (var d in dirs) { vels.Add(d * PlayerController.WalkSpeed); vels.Add(d * PlayerController.SprintSpeed); vels.Add(d * PlayerController.WalkSpeed * 0.4f); }
            var beam = new List<(Vector3 q, Vector3 first, float risk, float score)> { (pos, Vector3.zero, 0f, 0f) };
            Vector3 fallback = Vector3.zero; float fallbackScore = float.MaxValue;
            for (int leg = 0; leg < Legs; leg++)
            {
                var next = new List<(Vector3 q, Vector3 first, float risk, float score)>();
                foreach (var r in beam)
                    foreach (var v in vels)
                    {
                        var q = r.q; float risk = r.risk; int worst = 0;
                        for (int s2 = 0; s2 < Samples; s2++)
                        {
                            q += (leg == 0 ? v * ms : v) * dtS;
                            int dd = Dead(q, leg * Samples + s2, ref risk); if (dd != 0) { worst = dd; break; }
                        }
                        if (worst != 0)
                        {
                            if (leg == 0) { float sc = 1000 * worst + (q - goal).magnitude; if (sc < fallbackScore) { fallbackScore = sc; fallback = v; } }
                            continue;
                        }
                        next.Add((q, leg == 0 ? v : r.first, risk, risk + (q - goal).magnitude * 1.4f + (v.magnitude > PlayerController.WalkSpeed ? 0.1f : 0)));
                    }
                if (next.Count == 0)
                {
                    if (leg == 0) { if (Log) Debug.Log($"[final toll] {t:0.00}s {UltimateState.Roman[hour + 1]} cornered at ({pos.x:0.0},{pos.z:0.0}) goal ({goal.x:0.0},{goal.z:0.0})"); return fallback; }
                    break;
                }
                next.Sort((x, y) => x.score.CompareTo(y.score));
                beam = next.GetRange(0, Mathf.Min(BeamW, next.Count));
            }
            return beam[0].first;
        }

        // ---------------- presentation (stand-ins) ----------------
        Transform root, plate, mySpot; readonly List<Transform> pool = new List<Transform>(), ring1 = new List<Transform>(), ring2 = new List<Transform>();
        static Material bellM, ringM, echoM, plateM, bulletM, spotM;
        const int RingPieces = 90;
        void Build()
        {
            if (root) return;
            if (!bellM)
            {
                bellM = Prim.Material(new Color(0.24f, 0.17f, 0.09f), 0.15f); ringM = Prim.Material(new Color(1f, 0.88f, 0.62f), 2.4f);
                echoM = Prim.Material(new Color(0.62f, 0.88f, 1f), 2.4f); plateM = Prim.Material(new Color(0.89f, 0.7f, 0.37f), 1.2f);
                bulletM = Prim.Material(new Color(1f, 0.8f, 0.4f), 2.2f); spotM = Prim.Material(new Color(0.3f, 1f, 0.85f), 2.5f);
            }
            root = new GameObject("Final Toll (stand-in)").transform; root.SetParent(W.Root, false);
            Mat(Prim.Make(PrimitiveKind.Cylinder, root, new Vector3(0, 0.02f, 0), new Vector3(23.4f, 0.01f, 23.4f), Color.white), bellM);
            for (int j = 0; j < RingPieces; j++)
            {
                var a = Prim.Make(PrimitiveKind.Cube, root, Vector3.zero, new Vector3(0.6f, 0.18f, 0.45f), Color.white); Mat(a, ringM); a.gameObject.SetActive(false); ring1.Add(a);
                var b = Prim.Make(PrimitiveKind.Cube, root, Vector3.zero, new Vector3(0.6f, 0.18f, 0.45f), Color.white); Mat(b, echoM); b.gameObject.SetActive(false); ring2.Add(b);
            }
            plate = Prim.Make(PrimitiveKind.Cylinder, root, Vector3.zero, new Vector3(PlateSize * 2, 0.03f, PlateSize * 2), Color.white); Mat(plate, plateM); plate.gameObject.SetActive(false);
            mySpot = new GameObject("my spot").transform; mySpot.SetParent(root, false);
            Mat(Prim.Make(PrimitiveKind.Cylinder, mySpot, new Vector3(0, 1.6f, 0), new Vector3(0.12f, 1.6f, 0.12f), Color.white), spotM);
        }
        static void Mat(Transform t, Material m) { var r = t.GetComponent<Renderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
        void DrawRing(List<Transform> pieces, float R, int k, bool echo)
        {
            if (R < 0) { foreach (var x in pieces) x.gameObject.SetActive(false); return; }
            float step = 360f / RingPieces;
            for (int j = 0; j < RingPieces; j++)
            {
                float a = step * (j + 0.5f); bool gap = false;
                for (int g = 0; g < 4; g++) if (AngDiff(a, GapCentre(k, g) + (echo ? 45 : 0)) < GapHalf) gap = true;
                var x = pieces[j]; x.gameObject.SetActive(!gap); if (gap) continue;
                var d = Dir(a); x.position = d * R + Vector3.up * 0.12f; x.rotation = Quaternion.LookRotation(d);
                x.localScale = new Vector3(Mathf.Max(0.2f, 2 * Mathf.PI * R / RingPieces + 0.05f), 0.18f, 0.45f);
            }
        }
        void Present(float t)
        {
            if (!root) return;
            int c = Cur(t), k = c + 1;
            DrawRing(ring1, RingR(t), c, false); DrawRing(ring2, EchoR(t), c, true);
            bool showPlate = k < 13 && Has(k, 0) && HourPresent(Soaker(k));
            plate.gameObject.SetActive(showPlate); if (showPlate) plate.position = PlatePos(k) + Vector3.up * 0.04f;
            int n = 0;
            foreach (var b in bullets)
            {
                if (pool.Count <= n) { var tr = Prim.Make(PrimitiveKind.Sphere, root, Vector3.zero, Vector3.one * BR * 2.2f, Color.white); Mat(tr, bulletM); pool.Add(tr); }
                var x = pool[n++]; x.gameObject.SetActive(true); x.position = new Vector3(b.P.x, 0.9f, b.P.z);
            }
            for (int i = n; i < pool.Count; i++) pool[i].gameObject.SetActive(false);
            var me = W.Players.Count > 0 ? W.Players[0] : null;
            if (mySpot) { bool show = EscapementRelay.ShowMySpot && me && !me.Down && plans.ContainsKey(me); mySpot.gameObject.SetActive(show); if (show) { var gl = plans[me].Goal; mySpot.position = new Vector3(gl.x, 0, gl.z); } }
        }
        void Teardown()
        {
            if (root) Object.Destroy(root.gameObject);
            root = plate = mySpot = null; pool.Clear(); ring1.Clear(); ring2.Clear(); plans.Clear();
        }

        static GUIStyle nameStyle, bigStyle, midStyle;
        public void DrawGui(Camera cam)
        {
            if (!Running || cam == null || !root) return;
            if (nameStyle == null)
            {
                nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                midStyle = new GUIStyle(nameStyle) { fontSize = 22 }; bigStyle = new GUIStyle(nameStyle) { fontSize = 32 };
            }
            float t = T; int c = Cur(t), k = c + 1; float left = NextAt(k) - t;
            // gap numerals on each ring (or at the rim, for the next toll)
            float R1 = RingR(t), R2 = EchoR(t);
            for (int j = 0; j < 4; j++)
            {
                if (R1 > 0) Label(cam, Dir(GapCentre(c, j)) * (R1 + 0.9f), 0.4f, UltimateState.Roman[GapHour(c, j) + 1], new Color(1f, 0.85f, 0.5f), midStyle);
                else if (k < 13) Label(cam, Dir(GapCentre(k, j)) * 11.0f, 0.4f, UltimateState.Roman[GapHour(k, j) + 1], new Color(1f, 0.85f, 0.5f, 0.6f), nameStyle);
                if (R2 > 0) Label(cam, Dir(GapCentre(c, j) + 45) * (R2 + 0.9f), 0.4f, UltimateState.Roman[GapHour(c, j) + 1], new Color(0.65f, 0.9f, 1f), midStyle);
            }
            if (k < 13 && Has(k, 0) && HourPresent(Soaker(k))) Label(cam, PlatePos(k), 0.6f, UltimateState.Roman[Soaker(k) + 1] + " SOAK", new Color(1f, 0.85f, 0.5f), nameStyle);
            if (k < 13 && Has(k, 2)) Label(cam, Dir(InvAng(k)) * 8f, 0.6f, UltimateState.Roman[InvHour(k) + 1] + " INVERTED: inside", new Color(0.8f, 0.65f, 1f), nameStyle);
            for (int i = 0; i < W.Players.Count; i++)
            {
                var p = W.Players[i]; if (p.Down) continue;
                Label(cam, p.transform.position, 2.9f, (i == 0 ? "YOU " : "") + UltimateState.Roman[HourOf(p) + 1] + (JobOf(p) != null ? "  · " + JobOf(p) : ""), Color.white, nameStyle);
            }
            string text = k >= 13 ? $"XIII in {left:0.0}s · THE HOUR COMPLETES" : $"toll {UltimateState.Roman[k]} in {left:0.0}s · {string.Join(" + ", System.Array.ConvertAll(Kinds(k), x => KN[x]))}" + (Echoes(k) ? " · ECHO" : "") + (t >= TollAt[4] ? " · MAINSPRING" : "");
            bigStyle.normal.textColor = k >= 10 ? new Color(1f, 0.42f, 0.3f) : new Color(1f, 0.85f, 0.55f);
            GUI.Label(new Rect(0, Screen.height * 0.17f, Screen.width, 50), text, bigStyle);
        }
        static void Label(Camera cam, Vector3 world, float height, string text, Color c, GUIStyle style)
        {
            var sp = cam.WorldToScreenPoint(new Vector3(world.x, height, world.z)); if (sp.z <= 0) return;
            var r = new Rect(sp.x - 130, Screen.height - sp.y - 14, 260, 28);
            style.normal.textColor = new Color(0, 0, 0, 0.85f); GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, style);
            style.normal.textColor = c; GUI.Label(r, text, style);
        }
        public string HudText(PlayerController p)
        {
            if (!Running || p == null) return null;
            float t = T; int k = Cur(t) + 1, h = HourOf(p);
            if (k >= 13) return $"XIII in {NextAt(k) - t:0.0}s: kill the boss or the hour completes";
            string job = Has(k, 0) && Soaker(k) == h ? "you SOAK the plate" : Has(k, 2) && InvHour(k) == h ? "you are INVERTED: stand inside the slam" : Has(k, 2) ? "stay out of the slam" : "dodge";
            return $"toll {UltimateState.Roman[k]} in {NextAt(k) - t:0.0}s · you ({UltimateState.Roman[h + 1]}): through gap {UltimateState.Roman[h + 1]} · {job} · hits ring the bell: x2";
        }

        public override void Cancel() { base.Cancel(); bullets.Clear(); debts.Clear(); Teardown(); }
        public void ResetCounts() { Fails.Clear(); }
    }
}
