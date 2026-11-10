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
    //   RESONANCE inside the bell every hit on the boss counts x1.25 (BellkeeperEncounter.V2P5Resonance; first asked as x2,
    //             tuned down), and x4 in the Last Stand (StandScale)
    //   MAINSPRING from toll IV, P3's three spiral arms fire between sound rings, stopping 0.8 s before each toll (50).
    //   THE ECHO BELLKEEPER toll IX on: a ghost Bellkeeper (an extra body, r 1) 6.5 m out across the boss from each toll's
    //             plate (it moves after every toll) strikes its own clapper lane through itself. Only the one that RANG LAST
    //             takes damage (IX echo, X boss, XI echo, XII boss).
    //   LAST STAND toll XIII shatters the bell: four platforms (one per hour, 3.4 m out, r 1.9, touching the boss; shown from XII). Off your
    //             own platform is the fall (999); 13 bell shards drop on them (off-centre, shown 1 s, 999). Hits count x4 for
    //             10 s; then the bell falls (WIPE).
    // Hours are W.Players index (1-4 players). An hour with nobody in it asks nothing (no plate that toll;
    // nobody may stand in the INVERTED half). Bots steer (Steer) with the simulation's route search.
    public sealed class FinalToll : UltimateMechanic
    {
        public override string Id => UltimateMechanics.FinalToll;
        public override string Name => "Final Toll";

        public const string Source = "Final Toll";
        public static readonly float[] TollAt = { 0, 4, 8, 12, 16, 20, 24, 27.5f, 31, 34.5f, 38, 41.5f, 45, 48.5f };
        public const float RingStart = 11.6f, RingSpeed = 5f, GapHalf = 16f, RingHalf = 0.3f, LaneHalf = 1.5f, LaneShow = 1.6f,
            PlateR = 5.5f, PlateSize = 1.6f, DebtR = 2f, EchoDelay = 0.8f, ArmEvery = 0.45f, ArmSpeed = 3.5f, ArmTurn = 35f,
            ShardSpeed = 4f, BR = 0.22f, PR = 0.45f, Bound = 11.1f, StandScale = 4f, EchoOut = 6.5f, EchoRad = 1f,
            PlatOut = 3.4f, PlatR = 1.9f, ShardR = 0.9f;   // each platform reaches the boss, so a melee lunge ends on it
        float Stand => Boss.UltStandFor;
        float Show => LaneShow + WarnLead;   // the clapper and hammer lanes show 0.3 s earlier on Easy, 0.15 s on Medium   // the Last Stand before the bell falls: 10 s on Hard, longer on Medium and Easy
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
        // the Echo Bellkeeper
        static bool EchoOn(float t) => t >= TollAt[9] && t < TollAt[13];
        Vector3 EchoPos(int k) => Dir(PlateAng(Mathf.Clamp(k, 9, 12)) + 180) * EchoOut;   // where it rings toll k
        Vector3 EchoAt(float t) => EchoPos(Mathf.Min(12, Cur(t) + 1));                     // it moves after every toll
        static bool EchoLive(float t) { int c = Cur(t); return t < TollAt[13] && c >= 9 && c % 2 == 1; }   // the one that rang last
        float EchoLaneAng(int k)
        {
            float a = Hash(k, 21) % 180;
            if (Has(k, 0)) { var pl = PlatePos(k) - EchoPos(k); for (int n = 0; n < 26; n++) { if (Mathf.Abs(Cross(Dir(a), pl)) > LaneHalf + PR + 1.8f) break; a = (a + 7) % 180; } }
            return a;
        }
        bool InEchoLane(int k, Vector3 q, float margin = 0) => k >= 9 && k <= 12 && Mathf.Abs(Cross(Dir(EchoLaneAng(k)), Sim.Flat(q) - EchoPos(k))) < LaneHalf + PR + margin;
        public bool EchoBody(out Vector3 c, out float r) { c = Running ? EchoAt(T) : Vector3.zero; r = EchoRad; return Running && EchoOn(T); }
        // the last stand
        static bool Standing(float t) => t >= TollAt[13];
        static Vector3 PlatC(int h) => Dir(45 + 90 * h) * PlatOut;
        (Vector3 p, float at, int h) Shard(int n) { float a = Hash(n, 31) % 360, r = 0.7f + Hash(n, 37) % 100 / 100f * 0.5f; return (PlatC(n % 4) + Dir(a) * r, TollAt[13] + 0.9f + 0.75f * n, n % 4); }
        const int ShardCount = 13;
        // how much a hit at 'contact' counts (BellkeeperEncounter.DamageTakenScale)
        public float DamageScale(Vector3 contact)
        {
            float t = T; if (Standing(t)) return StandScale;
            if (!EchoOn(t)) return BellkeeperEncounter.V2P5Resonance;
            bool atEcho = (Sim.Flat(contact) - EchoAt(t)).magnitude < Sim.Flat(contact).magnitude;
            return atEcho == EchoLive(t) ? BellkeeperEncounter.V2P5Resonance : 0;
        }
        int HourOf(PlayerController p) { int i = W.Players.IndexOf(p); return i < 0 ? -1 : i % 4; }
        bool HourPresent(int h) { foreach (var p in W.Players) if (!p.Down && HourOf(p) == h) return true; return false; }
        bool OnPlate(int k, Vector3 q, float slack = 0) => Has(k, 0) && HourPresent(Soaker(k)) && (Sim.Flat(q) - PlatePos(k)).magnitude < PlateSize + slack;

        // ---------------- cast ----------------
        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name);
            t0 = Now; tolls = 0; seed = Cue.Seed % 997; nextArm = TollAt[4]; bullets.Clear(); debts.Clear(); plans.Clear(); ringHits.Clear();
            using (BuildMarker.Auto()) Build();
            Hazard lane = null, half = null, mark = null, eLane = null; var shardTele = new bool[ShardCount]; var shardDone = new bool[ShardCount];
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
                    if (lane == null && left <= Show)
                    {
                        float yaw = LaneAng(k) + 180;   // Sim.Dir(yaw) = Dir(plan angle)
                        CastClip(k == 9 ? "EchoTell" : "HollowToll", left);   // toll IX: the Echo appears
                        lane = Spawn(new Hazard { Source = Source + ": the clapper", Shape = HazardShape.Lane, Kind = HazardKind.Strike, Origin = -Sim.Dir(yaw) * 12f, Yaw = yaw, Length = 24, Width = LaneHalf * 2, LockAt = 0, ResolveAt = UltShapes.ResolveIn(Mathf.Max(1, Sim.Ticks(left))), Damage = 0, Knockback = 0, Stun = 0, HitOnResolve = false });
                        int kl = k; lane.OnResolve = hz => { W.BossFx.TollResolve(Vector3.zero, 0.6f, 11f); W.BossFx.UltClapperStrike(Vector3.zero, Dir(LaneAng(kl)), 22f); CameraShake.Shake(0.2f, 0.3f); };
                    }
                    if (eLane == null && k >= 9 && k <= 12 && left <= Show)
                    {
                        float yaw = EchoLaneAng(k) + 180;
                        eLane = Spawn(new Hazard { Source = Source + ": the echo's clapper", Shape = HazardShape.Lane, Kind = HazardKind.Strike, Origin = EchoPos(k) - Sim.Dir(yaw) * 14f, Yaw = yaw, Length = 28, Width = LaneHalf * 2, LockAt = 0, ResolveAt = UltShapes.ResolveIn(Mathf.Max(1, Sim.Ticks(left))), Damage = 0, Knockback = 0, Stun = 0, HitOnResolve = false });
                        int ke = k; eLane.OnResolve = hz => W.BossFx.UltClapperStrike(EchoPos(ke), Dir(EchoLaneAng(ke)), 22f);
                    }
                    if (half == null && Has(k, 2))
                        half = Spawn(new Hazard { Source = Source + ": INVERTED", Shape = HazardShape.Cone, Kind = HazardKind.Ground, Origin = Vector3.zero, Yaw = InvAng(k) + 180, HalfAngle = 90, Radius = 11.6f, LockAt = 0, ResolveAt = UltShapes.ResolveIn(Mathf.Max(1, Sim.Ticks(left))), Damage = 0, Knockback = 0, Stun = 0, HitOnResolve = false });
                    if (mark == null && Has(k, 3) && left <= Show)
                        mark = Spawn(new Hazard { Source = Source + ": the hammer", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = HammerAt(k), Radius = 0.9f, LockAt = 0, ResolveAt = UltShapes.ResolveIn(Mathf.Max(1, Sim.Ticks(left))), Damage = 0, Knockback = 0, Stun = 0, HitOnResolve = false });
                }
                // the toll
                int kn = Cur(nt);
                if (kn >= 1 && kn > tolls)
                {
                    tolls = kn; lane = half = mark = eLane = null;
                    if (kn >= 13) { Boss.PlanLog.Add((Now, "final toll XIII: the last stand")); debts.Clear(); bullets.Clear(); CameraShake.Shake(0.35f, 0.6f); W.BossFx.TollResolve(Vector3.zero, 1f, 11.5f); }
                    else Toll(kn);
                    if (Boss.Ult.WipeCause != null) break;
                }
                // the last stand: the fall, the shards, the end
                if (Standing(nt))
                {
                    foreach (var p in W.Players) if (!p.Down && (Sim.Flat(p.transform.position) - PlatC(HourOf(p))).magnitude > PlatR) Hit(p, UltimateState.Lethal, "the fall");   // off the platform falls: dashing too (no iframes)
                    for (int n = 0; n < ShardCount; n++)
                    {
                        var sh = Shard(n);
                        if (!shardTele[n] && nt >= sh.at - (1f + WarnLead)) { shardTele[n] = true; Spawn(new Hazard { Source = Source + ": a falling shard", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = sh.p, Radius = ShardR, LockAt = 0, ResolveAt = UltShapes.ResolveIn(Mathf.Max(1, Sim.Ticks(sh.at - t))), Damage = 0, Knockback = 0, Stun = 0, HitOnResolve = false }); }
                        if (!shardDone[n] && nt >= sh.at) { shardDone[n] = true; foreach (var p in W.Players) if (!p.Down && (Sim.Flat(p.transform.position) - sh.p).magnitude < ShardR + PR) Hit(p, UltimateState.Lethal, "a falling shard"); }
                    }
                    if (nt >= TollAt[13] + Stand) { Fails.Add("the bell falls"); Ult.Wipe("The bell falls", Id); break; }
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
                StepGhost(); Present(nt);
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
            foreach (var p in W.Players) if (!p.Down && InEchoLane(k, p.transform.position)) Hit(p, UltimateState.Lethal, "the echo's clapper");
            foreach (var d in debts)
                if (d.At == k)
                    foreach (var p in W.Players)
                        if (!p.Down && !OnPlate(k, p.transform.position) && (Sim.Flat(p.transform.position) - d.P).magnitude < DebtR + PR) Hit(p, UltimateState.Lethal, $"{UltimateState.Roman[d.Owner + 1]}'s debt");
            debts.RemoveAll(d => d.At <= k);
            if (Has(k, 0) && HourPresent(Soaker(k)))
            {
                bool held = false; foreach (var p in W.Players) if (!p.Down && HourOf(p) == Soaker(k) && OnPlate(k, p.transform.position)) held = true;
                if (!held) { Fails.Add($"toll {k}: plate empty"); Ult.Wipe("The plate was empty", Id); return; }
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
            if (Has(k, 3)) { var c = HammerAt(k); for (int j = 0; j < 12; j++) bullets.Add(new Bullet { P = c, V = Dir(30 * j + 7 * k) * ShardSpeed, What = "a hammer shard" }); W.BossFx.ChargeLand(c); }   // the hammer lands: a stomp ring, dust, sparks
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
            p.ReceiveHit(new Hazard { Source = Source + ": " + what, AttackId = Id, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = Sim.Flat(p.transform.position), Radius = 0.5f, Damage = dmg, Knockback = 0, Stun = 0 });
        }

        // ---------------- bots ----------------
        sealed class Plan { public Vector3 V; public int Until; public string Job; public Vector3 Goal; }
        readonly Dictionary<PlayerController, Plan> plans = new Dictionary<PlayerController, Plan>();
        public string JobOf(PlayerController p) => plans.TryGetValue(p, out var pl) ? pl.Job : null;
        public override string Job(PlayerController p) => JobOf(p);
        public static bool Log;   // tests: every time a bot finds no safe route

        // the nearest spot to 'to' clear of toll k's lane and debts, inside the bell, that passes need
        Vector3 Clear(int k, Vector3 to, System.Func<Vector3, bool> need)
        {
            for (int n = 0; n <= 7; n++)
                for (int a = 0; a < (n == 0 ? 1 : 12); a++)
                {
                    var c = to + Dir(30 * a + 15 * n) * (n * 0.9f); float r = c.magnitude;
                    if (r > 10.5f || r < 2.6f || (need != null && !need(c))) continue;
                    if (k < 13 && (InLane(k, c, 0.6f) || InEchoLane(k, c, 0.6f))) continue;
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
            var plat = PlatC(h);
            if (Standing(t))
            {
                // the last stand: on my platform, as near the boss as it allows (melee at its inner edge), clear of the next shards
                var want = plat - plat.normalized * 0.4f;
                var due = new List<Vector3>(); for (int n = 0; n < ShardCount; n++) { var sh = Shard(n); if (sh.h == h && sh.at > t - 0.05f && sh.at - t < 1.3f) due.Add(sh.p); }
                if (due.Count > 0)
                {
                    Vector3 best = want; float bd = float.MaxValue; bool found = false;
                    foreach (float r in new[] { 0f, 0.5f, 0.9f, 1.25f, 1.45f })
                        for (int j = 0; j < (r == 0 ? 1 : 16); j++)
                        {
                            var c4 = plat + Dir(22.5f * j) * r; bool hit = false; foreach (var q in due) if ((c4 - q).magnitude < ShardR + PR + 0.35f) hit = true;
                            if (hit) continue; float dd = (c4 - want).magnitude; if (dd < bd) { bd = dd; best = c4; found = true; }
                        }
                    if (found) want = best;
                }
                job = "platform " + UltimateState.Roman[h + 1]; return want;
            }
            // the rings: line up with my gap at my radius before the next one reaches me (the echo once the first has passed)
            float R1 = RingR(t), R2 = EchoV(t);
            if (R1 > 0 && R1 > myR - 1.2f && (R1 - myR) / RingSpeed < 2.4f) { job = "gap " + UltimateState.Roman[h + 1]; return Dir(GapOf(c, h, false)) * Mathf.Clamp(myR, 3.4f, 10.5f); }
            if (R2 > Boss.BodyRadius + 0.3f && R2 > myR - 1.2f && (R2 - myR) / RingSpeed < 2.4f) { job = "echo gap " + UltimateState.Roman[h + 1]; return Dir(GapOf(c, h, true)) * Mathf.Clamp(myR, 3.4f, 10.5f); }
            if (k >= 13) { job = "→ platform " + UltimateState.Roman[h + 1]; return plat - plat.normalized * 0.4f; }
            if (k < 13 && Has(k, 0) && Soaker(k) == h && left < 2.6f) { job = "SOAK"; return PlatePos(k); }
            if (k < 13 && Has(k, 2))
            {
                var d = Dir(InvAng(k));
                if (h == InvHour(k)) { job = "INVERTED"; return Clear(k, d * 5f, q => Vector3.Dot(q, d) > 1.4f); }
                float o = Vector3.Dot(pos, d); var st = o > -1.6f ? pos - d * (o + 1.8f) : pos; float r = st.magnitude;
                if (r < 2.8f) st = st * (2.8f / Mathf.Max(r, 1e-3f)); else if (r > 10f) st = st * (10f / r);
                job = "out of the slam"; return Clear(k, st, q => Vector3.Dot(q, d) < -1.4f);
            }
            if (k >= 9 && k <= 12 && k % 2 == 1)   // the echo rings next: be at it as it does
            {
                var E = EchoPos(k); float ea = AngOf(E) + (i % 2 == 1 ? 100 : -100) + ((i % 4) < 2 ? -20 : 20);
                job = "→ ECHO"; return Clear(k, E + Dir(ea) * 4.5f, null);
            }
            // nothing due: get a downed ally up, when the spot beside them is clear of the next lanes and debts
            if (ReviveSpot(p, null, out var rs) && (Clear(k, rs, null) - rs).magnitude < 0.35f) { job = "revive"; return Clear(k, rs, null); }
            float a = (k < 13 ? LaneAng(k) : 0) + 90 + (i % 2 == 1 ? 0 : 180) + ((i % 4) < 2 ? -25 : 25);
            job = k >= 9 ? "→ BOSS" : "";
            return Clear(k, Dir(a) * 5.2f, null);
        }

        public override bool Steer(PlayerController p, UltimateBot.Skill skill, out Vector3 vel)
        {
            vel = Vector3.zero;
            if (!Running || p.Down) return false;
            if (plans.TryGetValue(p, out var cur) && (Now < cur.Until || !SearchTurn(p))) { vel = cur.V; return true; }
            float t = T; var goal = GoalOf(p, t, skill, out string job);
            var v = job == "forgets its debt" ? Vector3.zero : Search(p, Sim.Flat(p.transform.position), t, goal);
            plans[p] = new Plan { V = v, Until = Now + (skill == UltimateBot.Skill.Late ? 15 : 1), Job = job, Goal = goal };
            vel = v; return true;
        }

        public override bool SafeToRoot(PlayerController p, float seconds, float scale)
        {
            if (!Running || p.Down || !plans.TryGetValue(p, out var pl) || pl.Job == "forgets its debt") return true;
            return RootTest(seconds, scale, () => Search(p, Sim.Flat(p.transform.position), T, pl.Goal));
        }

        public override bool AttackTarget(PlayerController p, out Vector3 aim, out float reach, out bool fullKit)
        {
            aim = Boss.transform.position; reach = 99; fullKit = false;
            if (!Running) return false;
            var q = Sim.Flat(p.transform.position); float t = T; int k = Cur(t) + 1;
            reach = q.magnitude - Boss.BodyRadius - PR;
            if (EchoOn(t) && EchoLive(t)) { var E = EchoAt(t); aim = E + Vector3.up * 1.2f; reach = (q - E).magnitude - EchoRad - PR; }
            float left = Standing(t) ? 99 : NextAt(k) - t, R1 = RingR(t), R2 = EchoV(t), myR = q.magnitude;
            bool ringSoon = (R1 > 0 && R1 > myR - 1.2f && (R1 - myR) / RingSpeed < 0.7f) || (R2 > 0 && R2 > myR - 1.2f && (R2 - myR) / RingSpeed < 0.7f);
            bool settled = plans.TryGetValue(p, out var pl) && (q - Sim.Flat(pl.Goal)).magnitude < 3f;
            // casts slow you: swing on the move, never with a ring
            // or the toll close (the route search counts the slowdown)
            if (!settled || ringSoon || left < 0.6f) reach = 99;
            fullKit = settled && !ringSoon && left > 1.6f;
            return true;
        }

        readonly List<Vector3>[] pred = new List<Vector3>[16];
        readonly List<Vector3> dirs = new List<Vector3>(), vels = new List<Vector3>();
        struct Node { public Vector3 Q, First; public float Risk, Score; }
        List<Node> beam = new List<Node>(), grown = new List<Node>();
        static readonly System.Comparison<Node> ByScore = (x, y) => x.Score.CompareTo(y.Score);
        static readonly System.Comparison<Vector3> ByX = (a, b) => a.x.CompareTo(b.x);
        struct SampleRule { public float Tq, RingR, EchoR; public int K, RingK; public bool Stand, EchoOn, Toll, EchoLane, OnPlateRule, SoakMine, Has2, InvMine; public Vector3 EchoAt, LaneD, ELaneD, EPos, PlatePos, InvD; }
        readonly SampleRule[] rule = new SampleRule[16];
        readonly Vector3[] shardP = new Vector3[ShardCount]; readonly float[] shardAt = new float[ShardCount];
        // RingHit with the ring's radius and toll already known
        int RingHitAt(int hour, Vector3 q, float R, int k, float margin, bool echo)
        {
            if (R < 0) return 0;
            q = Sim.Flat(q); if (Mathf.Abs(q.magnitude - R) > RingHalf + PR) return 0;
            float a = AngOf(q);
            for (int j = 0; j < 4; j++) if (AngDiff(a, GapCentre(k, j) + (echo ? 45 : 0)) < GapHalf - margin * 20) return GapHour(k, j) == hour ? 0 : 2;
            return 1;
        }
        static readonly Unity.Profiling.ProfilerMarker SearchMarker = new Unity.Profiling.ProfilerMarker("Ultimate.FinalToll.Search");
        protected override int ReplanTicks => 3;   // still staggered by player (at most 2 searches a tick, was all 4 at once); 6 cost the party its kill
        Vector3 Search(PlayerController p, Vector3 pos, float t, Vector3 goal) { using (SearchMarker.Auto()) return SearchRoutes(p, pos, t, goal); }
        Vector3 SearchRoutes(PlayerController p, Vector3 pos, float t, Vector3 goal)
        {
            // 4 legs (0.8 s), as P3: at 9 m/s a sprinting bot could commit to a route across the clapper lane before it saw the toll
            // 2 Nov (performance agent: up to 108 ms a frame at the end of P5): each bot on
            // its own tick (SearchTurn), only the bullets a route could reach, reused lists
            const float Leg = 0.2f; const int Legs = 4, Samples = 4, BeamW = 12; float dtS = Leg / Samples; int N = Legs * Samples;
            int hour = HourOf(p);
            for (int s = 0; s < N; s++)
            {
                float tq = t + (s + 1) * dtS; var arr = pred[s] ?? (pred[s] = new List<Vector3>()); arr.Clear();
                // a bullet farther than a sprint can get by this sample, plus the 0.5 m risk margin, can't change the result
                float lim = (s + 1) * dtS * PlayerController.SprintSpeed + PR + BR + 0.5f, lim2 = lim * lim;
                void Add(Vector3 b) { float dx = b.x - pos.x, dz = b.z - pos.z; if (dx * dx + dz * dz < lim2) arr.Add(b); }
                foreach (var b in bullets) Add(b.P + b.V * (tq - t));
                for (float te = nextArm; te <= tq; te += ArmEvery) if (Firing(te)) for (int j = 0; j < 3; j++) Add(Dir(ArmAng(te) + 120 * j) * (Boss.BodyRadius + ArmSpeed * (tq - te)));
                int kq = Cur(tq); if (kq > tolls && kq < 13 && Has(kq, 3)) { var c = HammerAt(kq); for (int j = 0; j < 12; j++) Add(c + Dir(30 * j + 7 * kq) * ShardSpeed * (tq - TollAt[kq])); }
                arr.Sort(ByX);   // looked up by x below
                // what the rules are at this sample, worked out once (they were recomputed in every check: 52 ms a search late in P5)
                ref var R = ref rule[s]; R = default; R.Tq = tq;
                R.Stand = tq >= TollAt[13] - 0.02f; if (R.Stand) continue;
                R.EchoOn = EchoOn(tq); if (R.EchoOn) R.EchoAt = EchoAt(tq);
                int k = Cur(tq + dtS * 0.7f); R.K = k;
                R.Toll = k >= 1 && k < 13 && Mathf.Abs(tq - TollAt[k]) < dtS * 0.7f;
                if (R.Toll)
                {
                    R.LaneD = Dir(LaneAng(k));
                    R.EchoLane = k >= 9 && k <= 12; if (R.EchoLane) { R.ELaneD = Dir(EchoLaneAng(k)); R.EPos = EchoPos(k); }
                    bool has0 = Has(k, 0); if (has0) R.PlatePos = PlatePos(k);
                    R.OnPlateRule = has0 && HourPresent(Soaker(k));
                    R.SoakMine = has0 && Soaker(k) == hour;
                    R.Has2 = Has(k, 2); if (R.Has2) { R.InvD = Dir(InvAng(k)); R.InvMine = hour == InvHour(k); }
                }
                R.RingK = Cur(tq); R.RingR = RingR(tq); R.EchoR = EchoR(tq);
            }
            var (slowFor, ms) = SlowOf(p); lastClean = true;   // a swing roots to its end, not only the first leg
            // the last stand's falling shards, once per search (each check worked out all 13: late P5 searches cost 20 ms, 2 Nov)
            if (rule[N - 1].Stand) for (int n = 0; n < ShardCount; n++) { var sh = Shard(n); shardP[n] = sh.p; shardAt[n] = sh.at; }
            const float Near = 0.5f + PR + BR;   // a bullet farther than this adds no risk
            int Dead(Vector3 q, int s, ref float risk)
            {
                ref var R = ref rule[s];
                float tq = R.Tq, r = q.magnitude;
                if (r > Bound) return 2;
                float cb = r - Boss.BodyRadius - PR; if (cb < -0.05f) return 2; if (cb < 0.4f) risk += (0.4f - cb) * 2;
                if (R.Stand)   // on my platform from XIII on, off the shards as they land
                {
                    if ((q - PlatC(hour)).magnitude > PlatR - 0.25f) return 2;
                    for (int n = 0; n < ShardCount; n++) if (Mathf.Abs(tq - shardAt[n]) < dtS * 0.7f && (q - shardP[n]).magnitude < ShardR + PR + 0.25f) return 2;
                    return 0;
                }
                if (R.EchoOn) { float ce = (q - R.EchoAt).magnitude - EchoRad - PR; if (ce < -0.05f) return 2; }
                if (R.Toll)
                {
                    if (Mathf.Abs(Cross(R.LaneD, q)) < LaneHalf + PR || (R.EchoLane && Mathf.Abs(Cross(R.ELaneD, q - R.EPos)) < LaneHalf + PR)) return 2;
                    bool onPl = R.OnPlateRule && (q - R.PlatePos).magnitude < PlateSize - 0.3f;
                    if (!onPl) foreach (var d in debts) if (d.At == R.K && (q - d.P).magnitude < DebtR + PR + 0.25f) return 2;
                    if (R.Has2) { float o = Vector3.Dot(q, R.InvD); if (R.InvMine ? o <= 0.9f : o >= -0.9f) return 2; }
                    if (R.SoakMine && (q - R.PlatePos).magnitude > PlateSize - 0.3f) return 2;
                }
                int rh = RingHitAt(hour, q, R.RingR, R.RingK, 0.15f, false); if (rh != 0) return rh;
                rh = RingHitAt(hour, q, R.EchoR, R.RingK, 0.15f, true); if (rh != 0) return rh;
                var ps = pred[s]; float m2 = float.MaxValue;
                int lo = 0, hi = ps.Count; while (lo < hi) { int mid = (lo + hi) >> 1; if (ps[mid].x < q.x - Near) lo = mid + 1; else hi = mid; }
                for (int i = lo; i < ps.Count && ps[i].x <= q.x + Near; i++) { var b = ps[i]; float dx = q.x - b.x, dz = q.z - b.z, d2 = dx * dx + dz * dz; if (d2 < m2) m2 = d2; }
                float mc = m2 == float.MaxValue ? 9 : Mathf.Sqrt(m2) - PR - BR;
                if (mc < 0.12f) return 1; if (mc < 0.5f) risk += (0.5f - mc) * 2;
                return 0;
            }
            dirs.Clear(); for (int j = 0; j < 16; j++) dirs.Add(Dir(22.5f * j));
            var tg = goal - pos; if (tg.magnitude > 1e-3f) dirs.Add(tg.normalized);
            vels.Clear(); vels.Add(Vector3.zero);
            foreach (var d in dirs) { vels.Add(d * PlayerController.WalkSpeed); vels.Add(d * PlayerController.SprintSpeed); vels.Add(d * PlayerController.WalkSpeed * 0.4f); }
            beam.Clear(); beam.Add(new Node { Q = pos });
            Vector3 fallback = Vector3.zero; float fallbackScore = float.MaxValue;
            for (int leg = 0; leg < Legs; leg++)
            {
                var next = grown; next.Clear();
                foreach (var r in beam)
                    foreach (var v in vels)
                    {
                        var q = r.Q; float risk = r.Risk; int worst = 0;
                        for (int s2 = 0; s2 < Samples; s2++)
                        {
                            q += ((leg * Samples + s2) * dtS < slowFor ? v * ms : v) * dtS;
                            int dd = Dead(q, leg * Samples + s2, ref risk); if (dd != 0) { worst = dd; break; }
                        }
                        if (worst != 0)
                        {
                            if (leg == 0) { float sc = 1000 * worst + (q - goal).magnitude; if (sc < fallbackScore) { fallbackScore = sc; fallback = v; } }
                            continue;
                        }
                        next.Add(new Node { Q = q, First = leg == 0 ? v : r.First, Risk = risk, Score = risk + (q - goal).magnitude * 1.4f + (v.magnitude > PlayerController.WalkSpeed ? 0.1f : 0) });
                    }
                if (next.Count == 0)
                {
                    lastClean = false;
                    if (leg == 0) { if (Log) Debug.Log($"[final toll] {t:0.00}s {UltimateState.Roman[hour + 1]} cornered at ({pos.x:0.0},{pos.z:0.0}) goal ({goal.x:0.0},{goal.z:0.0})"); return fallback; }
                    break;
                }
                next.Sort(ByScore);
                if (next.Count > BeamW) next.RemoveRange(BeamW, next.Count - BeamW);
                grown = beam; beam = next;
            }
            return beam[0].First;
        }

        // ---------------- presentation ----------------
        // Models (proposal, ART-PLAN-v2.md batch 3; tools/bellkeeper-ultimate/models/p5_kit.py): the bell's floor, the Last
        // Stand platforms and falling bell shards, the SOAK plate, the clappers, brass hammer shards and the Echo as a ghost of
        // the boss. The arena's void stays open beneath (ArenaVoid): at XIII the bell's floor drops into it. Stand-ins remain
        // where a model is missing.
        Transform root, plate, mySpot, echo, echoLive, floor, bellModel; readonly Transform[] plats = new Transform[4]; readonly List<Transform> pool = new List<Transform>();
        static Material bellM, ringM, echoM, plateM, bulletM, spotM, ghostM, liveM, pitM, platM, ghostFx;
        Transform floorModel; readonly Transform[] clappers = new Transform[2]; readonly List<Transform> shardPool = new List<Transform>(), fallers = new List<Transform>();
        readonly bool[] landed = new bool[ShardCount];
        const float ClapperPivot = 6.4f, ClapperScale = 0.6f;   // hung low and small (FT_Clapper x0.6: ball centre 5.4 m down, 1 m off the floor), so its rod stays below the camera
        // polish (proposal): the HAMMER callback's hammer, the bell's wall breaking at XIII, the Echo's re-forming, the platforms rising
        const float HammerScale = 0.45f, BellWallR = 12f, ShatterLen = 2.6f;
        const int BellPieces = 18;
        Transform hammer; readonly List<Transform> bellBits = new List<Transform>(); readonly Vector3[] platBase = new Vector3[4];
        readonly List<Renderer> ghostR = new List<Renderer>(); MaterialPropertyBlock ghostBlock; Vector3 echoWas; float echoMovedAt = -9; bool echoSeen, shattered;
        // Prebuild: the bell floor, clappers, platforms and shards are made hidden when combat starts (performance agent, 2 Nov:
        // the P4 -> P5 change took 60 ms and 4.4 MB); Arm() does the cast-time part
        public override void Prebuild() { MakeObjects(); root.gameObject.SetActive(false); ArenaVoid.Prepare(W.Root); EchoBuffers(); }   // the void scan, kept for the cast
        void Build() { MakeObjects(); root.gameObject.SetActive(true); Arm(); }
        void MakeObjects()
        {
            if (root) return;
            if (!bellM)
            {
                bellM = Prim.Material(new Color(0.24f, 0.17f, 0.09f), 0.15f); ringM = Prim.Material(new Color(1f, 0.88f, 0.62f), 2.4f);
                echoM = Prim.Material(new Color(0.62f, 0.88f, 1f), 2.4f); plateM = Prim.Material(new Color(0.89f, 0.7f, 0.37f), 1.2f);
                bulletM = Prim.Material(new Color(1f, 0.8f, 0.4f), 2.2f); spotM = Prim.Material(new Color(0.3f, 1f, 0.85f), 2.5f);
                ghostM = Prim.Material(new Color(0.45f, 0.72f, 1f), 1.6f); liveM = Prim.Material(new Color(1f, 0.82f, 0.4f), 2.6f);
                pitM = Prim.Material(new Color(0.02f, 0.02f, 0.05f), 0f); platM = Prim.Material(new Color(0.42f, 0.31f, 0.16f), 0.5f);
            }
            root = new GameObject("Final Toll (stand-in)").transform; root.SetParent(W.Root, false);
            floor = Prim.Make(PrimitiveKind.Cylinder, root, new Vector3(0, 0.02f, 0), new Vector3(23.4f, 0.01f, 23.4f), Color.white); Mat(floor, bellM);
            floorModel = GearFloor.Model("FT_BellFloor", root, Vector3.zero, Quaternion.identity); if (floorModel) floor.gameObject.SetActive(false);
            for (int i = 0; i < 2; i++) { clappers[i] = GearFloor.Model("FT_Clapper", root, Vector3.up * ClapperPivot, Quaternion.identity); if (clappers[i]) { clappers[i].localScale = Vector3.one * ClapperScale; clappers[i].gameObject.SetActive(false); } }
            for (int n = 0; n < ShardCount; n++) { var fx = GearFloor.Model("FT_BellShard", root, Vector3.up * 30, Quaternion.identity); if (!fx) break; fx.gameObject.SetActive(false); fallers.Add(fx); landed[n] = false; }
            var prefab = Resources.Load<GameObject>("Ultimate/GB_GreatBell");
            if (prefab) { bellModel = Object.Instantiate(prefab, root, false).transform; bellModel.name = "the great bell"; var dome = bellModel.Find("Dome"); if (dome) dome.gameObject.SetActive(false); }
            for (int h = 0; h < 4; h++) if (GearFloor.Model("FT_Platform", root, PlatC(h), Quaternion.FromToRotation(Vector3.left, PlatC(h).normalized)) is Transform pm) { plats[h] = pm; pm.gameObject.SetActive(false); } else { plats[h] = Prim.Make(PrimitiveKind.Cylinder, root, PlatC(h) + Vector3.up * 0.06f, new Vector3(PlatR * 2, 0.04f, PlatR * 2), Color.white); Mat(plats[h], platM); plats[h].gameObject.SetActive(false); }
            if (!ringGlowA) { ringGlowA = Fx(new Color(0.25f, 0.12f, 0.02f), new Color(1.9f, 1.35f, 0.55f), 0.85f); ringGlowB = Fx(new Color(0.02f, 0.07f, 0.16f), new Color(0.5f, 1.2f, 2.0f), 0.85f); }
            ringA = NewRing("the sound ring", ringM, ringGlowA); ringB = NewRing("the echo ring", echoM, ringGlowB);
            // the bell's own hammer for the HAMMER callback, and the great bell's wall in pieces for XIII
            hammer = GearFloor.Model("GF_Hammer", root, Vector3.up * 30, Quaternion.identity); if (hammer) { hammer.localScale = Vector3.one * HammerScale; hammer.gameObject.SetActive(false); }
            for (int n = 0; n < BellPieces; n++) { var bp = GearFloor.Model("FT_BellShard", root, Vector3.up * 30, Quaternion.identity); if (!bp) break; bp.localScale = Vector3.one * (1.4f + 0.5f * (n % 3)); bp.gameObject.SetActive(false); bellBits.Add(bp); }
            for (int h = 0; h < 4; h++) platBase[h] = plats[h] ? plats[h].position : Vector3.zero;
            plate = GearFloor.Model("FT_Plate", root, Vector3.zero, Quaternion.identity);
            if (!plate) { plate = UltMarkMesh.Mark(PrimitiveKind.Cylinder, UltMarkMesh.Dial, root, Vector3.zero, new Vector3(PlateSize * 2, 0.03f, PlateSize * 2)); Mat(plate, plateM); }
            plate.gameObject.SetActive(false);
            mySpot = new GameObject("my spot").transform; mySpot.SetParent(root, false);
            Mat(UltMarkMesh.Mark(PrimitiveKind.Cylinder, UltMarkMesh.Beacon, mySpot, new Vector3(0, 1.6f, 0), new Vector3(0.12f, 1.6f, 0.12f)), spotM);
        }
        // at the cast: the arena's floor goes, and the Echo is a copy of the boss as it looks now (it changes form at 55 %)
        void Arm()
        {
            using (VoidMarker.Auto()) ArenaVoid.Open(W.Root);   // the arena's own floor goes: below the bell's floor is the void
            if (!echo)
            {
                echo = new GameObject("the Echo Bellkeeper").transform; echo.SetParent(root, false); echo.gameObject.SetActive(false);
                ghostSteps = Ghost(echo).GetEnumerator(); StepGhost();
            }
            echoSeen = false; shattered = false;
        }
        // The Echo is copied over several ticks (performance agent, 2 Nov: copying it on the P5 cast tick was 13 ms and 4.4 MB): the
        // skinned meshes are all baked on the first tick (one pose), then one mesh is copied per tick. It first shows at toll IX.
        IEnumerator<bool> ghostSteps; bool ghostMade;
        // late P4 (the plan, once the train runs wild): start the copy then, a part per tick, so the P5 cast only shows it. The boss
        // has its second form by then (performance agent, 2 Nov: the cast-tick bake kept the P4 -> P5 frame at 22 ms)
        public void PrebakeEcho()
        {
            if (!root || echo) return;
            echo = new GameObject("the Echo Bellkeeper").transform; echo.SetParent(root, false); echo.gameObject.SetActive(false);
            ghostSteps = Ghost(echo).GetEnumerator();
        }
        public void StepEchoBake() => StepGhost();
        static readonly Unity.Profiling.ProfilerMarker BuildMarker = new Unity.Profiling.ProfilerMarker("Ultimate.FinalToll.Build"), VoidMarker = new Unity.Profiling.ProfilerMarker("Ultimate.FinalToll.ArenaVoid"),
            EchoMarker = new Unity.Profiling.ProfilerMarker("Ultimate.FinalToll.EchoBake"), TeardownMarker = new Unity.Profiling.ProfilerMarker("Ultimate.FinalToll.Teardown");
        void StepGhost()
        {
            if (ghostSteps == null) return;
            using var mark = EchoMarker.Auto();
            if (ghostSteps.MoveNext()) return;
            bool made = ghostMade; ghostSteps = null;
            if (!echo) return;
            if (!made)
            {
                Mat(Prim.Make(PrimitiveKind.Cylinder, echo, new Vector3(0, 1.4f, 0), new Vector3(EchoRad * 2, 1.4f, EchoRad * 2), Color.white), ghostM);
                Mat(Prim.Make(PrimitiveKind.Sphere, echo, new Vector3(0, 3.1f, 0), Vector3.one * 1.3f, Color.white), ghostM);
            }
            echoLive = Prim.Make(PrimitiveKind.Cylinder, echo, new Vector3(0, 0.05f, 0), new Vector3(EchoRad * 2 + 0.7f, 0.02f, EchoRad * 2 + 0.7f), Color.white); Mat(echoLive, liveM);
            echo.gameObject.SetActive(false);
            ghostR.Clear(); foreach (var r in echo.GetComponentsInChildren<Renderer>()) if (r.sharedMaterial == ghostFx) ghostR.Add(r);
        }
        void FinishGhost() { while (ghostSteps != null) StepGhost(); }
        // the Echo copy's buffers, sized in Prebuild (the intro) for the biggest mesh of either form, so late P4 allocates nothing
        static readonly List<Vector3> eV = new List<Vector3>(), eW = new List<Vector3>(); static readonly List<Vector2> eUv = new List<Vector2>();
        static readonly List<Color32> eCol = new List<Color32>(); static readonly List<int> eTri = new List<int>(), eSub = new List<int>();
        void EchoBuffers()
        {
            if (!Boss) return; int nv = 0, ni = 0;
            foreach (var r in Boss.GetComponentsInChildren<Renderer>(true))
            {
                var mesh = r is SkinnedMeshRenderer sk ? sk.sharedMesh : r.TryGetComponent<MeshFilter>(out var mf) ? mf.sharedMesh : null; if (!mesh) continue;
                nv = Mathf.Max(nv, mesh.vertexCount); long n = 0; for (int s = 0; s < mesh.subMeshCount; s++) n += mesh.GetIndexCount(s); ni = Mathf.Max(ni, (int)n);
            }
            if (eV.Capacity < nv) { eV.Capacity = nv; eW.Capacity = nv; eUv.Capacity = nv; eCol.Capacity = nv; }
            if (eTri.Capacity < ni) { eTri.Capacity = ni; eSub.Capacity = ni; }
        }
        // the Echo: a copy of the boss's own meshes (frozen in their pose) in the additive effect shader, its UVs remapped so the
        // "pillar" shape glows teal-blue at the feet and fades up the body, with slow vertical ripples. Yields true between
        // ticks; its last value is whether it made any mesh.
        IEnumerable<bool> Ghost(Transform into)
        {
            ghostMade = false; if (!Boss) yield break;
            if (!ghostFx)
            {
                var sh = Shader.Find("LastBell/FX/ColorDissolve"); if (!sh) { var m0 = Resources.Load<Material>("Run/FX_ColorDissolve"); if (m0) sh = m0.shader; }
                if (!sh) yield break;
                ghostFx = new Material(sh) { name = "Echo ghost" }; ghostFx.SetFloat("_Mode", 2); ghostFx.SetColor("_ColorLow", new Color(0.02f, 0.07f, 0.16f)); ghostFx.SetColor("_ColorHigh", new Color(0.25f, 0.62f, 1.0f));
                ghostFx.SetFloat("_Dissolve", 0); ghostFx.SetFloat("_Intensity", 0.16f);   // additive over every layer of the mesh: kept faint so it reads as a teal shape, not a white blob
                ghostFx.SetVector("_NoiseScale", new Vector4(3, 6, 0, 0)); ghostFx.SetVector("_Scroll", new Vector4(0, 0.4f, 0, 0));
            }
            float top = Mathf.Max(1f, Boss.MarkHeight); int made = 0;
            void Add(Mesh src, Matrix4x4 toBoss, string name)
            {
                if (!src || !src.isReadable) return;
                // through the reused lists (EchoBuffers): new arrays per part were 3.6 MB at once in late P4 (performance agent, 3 Nov)
                var m = new Mesh { name = name + " (echo)", indexFormat = src.indexFormat }; src.GetVertices(eV); eW.Clear(); eUv.Clear(); eCol.Clear();
                foreach (var v in eV) { var w = toBoss.MultiplyPoint3x4(v); eW.Add(w); eUv.Add(new Vector2(Mathf.Atan2(w.z, w.x) / (2 * Mathf.PI) + 0.5f, Mathf.Clamp01(w.y / top) * 0.92f)); eCol.Add(new Color32(255, 255, 255, 255)); }
                eTri.Clear(); for (int s = 0; s < src.subMeshCount; s++) { src.GetTriangles(eSub, s); eTri.AddRange(eSub); }
                m.SetVertices(eW); m.SetTriangles(eTri, 0); m.SetUVs(0, eUv); m.SetColors(eCol); m.RecalculateBounds();   // vertices in the boss's own space
                var go = new GameObject(name); go.transform.SetParent(into, false);
                go.AddComponent<MeshFilter>().sharedMesh = m; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = ghostFx; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; made++;
            }
            // every part in this tick's pose first, then one copy per tick
            var bossInv = Boss.transform.worldToLocalMatrix;
            var parts = new List<(Mesh mesh, Matrix4x4 toBoss, string name, bool baked)>();
            foreach (var r in Boss.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (r is SkinnedMeshRenderer sk)
                {
                    // BakeMesh gives the posed mesh in the renderer's space without its scale; put that scale back
                    var baked = new Mesh(); sk.BakeMesh(baked, false);
                    parts.Add((baked, bossInv * Matrix4x4.TRS(sk.transform.position, sk.transform.rotation, Vector3.one), sk.name, true));
                }
                else if (r is MeshRenderer && r.TryGetComponent<MeshFilter>(out var mf)) parts.Add((mf.sharedMesh, bossInv * r.transform.localToWorldMatrix, r.name, false));
            }
            foreach (var (mesh, toBoss, name, baked) in parts)
            {
                yield return true;
                if (into) Add(mesh, toBoss, name);
                if (baked) Object.Destroy(mesh);
            }
            ghostMade = made > 0;
        }
        // the clapper over a lane: winds up over one end as the lane shows, swings through on the toll, rides up the far side
        void PlaceClapper(Transform c, Vector3 pivot, float laneAng, float rel)
        {
            if (!c) return;
            bool on = rel > -Show && rel < 0.45f; c.gameObject.SetActive(on); if (!on) return;
            float a = rel < -0.3f ? Mathf.Lerp(-62f, -76f, Mathf.InverseLerp(-Show, -0.3f, rel)) + Mathf.Sin(rel * 40f) * 1.2f   // drawn back, trembling
                    : rel < 0.3f ? -76f + 152f * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-0.3f, 0.3f, rel)) : 76f;
            var axis = Vector3.Cross(Vector3.up, Dir(laneAng));
            c.position = pivot + Vector3.up * ClapperPivot; c.rotation = Quaternion.AngleAxis(a, axis);
        }
        static bool AddTo(List<Transform> l, Transform x) { x.gameObject.SetActive(false); l.Add(x); return true; }
        static void Mat(Transform t, Material m) { var r = t.GetComponent<Renderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
        // the sound ring (and the echo) as resonance linework: four arcs between the gaps, each a crisp filled band (the
        // hostile part: amber, or pale blue for the echo) with a wall of light rising off it and a fading wake behind it (the
        // ring rushes inward, so the wake trails outside). The gaps stay clean cuts. Rebuilt each frame from the rule's numbers.
        sealed class RingFx { public GameObject Go; public Mesh M; }
        RingFx ringA, ringB;
        static Material ringGlowA, ringGlowB;
        readonly List<Vector3> rv = new List<Vector3>(); readonly List<Vector2> ruv = new List<Vector2>(); readonly List<Color> rcol = new List<Color>();
        readonly List<int> rt0 = new List<int>(), rt1 = new List<int>();
        static Material Fx(Color low, Color high, float intensity)
        {
            var sh = Shader.Find("LastBell/FX/ColorDissolve"); if (!sh) { var m0 = Resources.Load<Material>("Run/FX_ColorDissolve"); if (m0) sh = m0.shader; }
            if (!sh) return null;
            var m = new Material(sh); m.SetFloat("_Mode", 2); m.SetColor("_ColorLow", low); m.SetColor("_ColorHigh", high); m.SetFloat("_Dissolve", 0); m.SetFloat("_Intensity", intensity);
            m.SetVector("_NoiseScale", new Vector4(9, 2, 0, 0)); m.SetVector("_Scroll", new Vector4(0.8f, 0, 0, 0)); return m;
        }
        RingFx NewRing(string name, Material core, Material glow)
        {
            var go = new GameObject(name); go.transform.SetParent(root, false);
            var m = new Mesh { name = name }; m.MarkDynamic(); go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = glow ? new[] { core, glow } : new[] { core }; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false); return new RingFx { Go = go, M = m };
        }
        void DrawRing(RingFx f, float R, int k, bool echo)
        {
            if (f == null) return;
            f.Go.SetActive(R >= 0); if (R < 0) return;
            rv.Clear(); ruv.Clear(); rcol.Clear(); rt0.Clear(); rt1.Clear();
            float off = echo ? 45 : 0;
            for (int g = 0; g < 4; g++)
            {
                float a0 = GapCentre(k, g) + off + GapHalf, a1 = GapCentre(k, (g + 1) % 4) + off - GapHalf;
                a1 = a0 + ((a1 - a0) % 360 + 360) % 360;   // gap centres are 90° apart, so each arc is 90 - 2 x GapHalf
                int segs = Mathf.Max(2, Mathf.CeilToInt((a1 - a0) / 2f));
                for (int s = 0; s <= segs; s++)
                {
                    float u = s / (float)segs; var d = Dir(Mathf.Lerp(a0, a1, u)); float ux = u * (a1 - a0) / 30f;
                    rv.Add(d * (R - RingHalf + 0.08f) + Vector3.up * 0.12f); rv.Add(d * (R + RingHalf - 0.08f) + Vector3.up * 0.12f);   // the band
                    rv.Add(d * R + Vector3.up * 0.05f); rv.Add(d * R + Vector3.up * 0.8f);                                            // the wall of light
                    rv.Add(d * (R + 0.2f) + Vector3.up * 0.07f); rv.Add(d * (R + 1.9f) + Vector3.up * 0.07f);                        // the wake
                    ruv.Add(new Vector2(ux, 0)); ruv.Add(new Vector2(ux, 1)); ruv.Add(new Vector2(ux, 0)); ruv.Add(new Vector2(ux, 1)); ruv.Add(new Vector2(ux, 0)); ruv.Add(new Vector2(ux, 1));
                    // the arc's ends fade a touch, so the gap reads as a soft-lit doorway, not a hole
                    float endA = Mathf.Clamp01(Mathf.Min(u, 1 - u) * (a1 - a0) / 6f) * 0.5f + 0.5f;
                    for (int v = 0; v < 6; v++) rcol.Add(new Color(1, 1, 1, v >= 4 ? 0.6f * endA : endA));
                    if (s == segs) continue;
                    int i = rv.Count - 6, j = i + 6;
                    rt0.Add(i); rt0.Add(j); rt0.Add(i + 1); rt0.Add(i + 1); rt0.Add(j); rt0.Add(j + 1);
                    for (int w = 2; w <= 4; w += 2)
                    {
                        rt1.Add(i + w); rt1.Add(j + w); rt1.Add(i + w + 1); rt1.Add(i + w + 1); rt1.Add(j + w); rt1.Add(j + w + 1);   // both faces
                        rt1.Add(i + w); rt1.Add(i + w + 1); rt1.Add(j + w); rt1.Add(i + w + 1); rt1.Add(j + w + 1); rt1.Add(j + w);
                    }
                }
            }
            var m = f.M; m.Clear(); m.SetVertices(rv); m.SetUVs(0, ruv); m.SetColors(rcol);
            m.subMeshCount = 2; m.SetTriangles(rt0, 0); m.SetTriangles(rt1, 1); m.RecalculateNormals(); m.RecalculateBounds();
        }
        void Present(float t)
        {
            if (!root) return;
            int c = Cur(t), k = c + 1;
            DrawRing(ringA, RingR(t), c, false); DrawRing(ringB, EchoR(t), c, true);
            bool st = Standing(t); float su = t - TollAt[13];   // su: seconds since XIII
            // the camera: back in from the bell's fall, then a short pull-back as the bell shatters (the 1-2 s transition
            // exception, ART-PLAN-v2 "Decided"); kept shallow so the first shard warnings stay readable
            float shatterPull = su < 0 ? 0 : su < 0.3f ? su / 0.3f : su < 0.7f ? 1 : Mathf.Clamp01(1 - (su - 0.7f) / 0.6f);
            RunDirector.CameraPull = Mathf.Max(Mathf.Clamp01(1 - t / 1.4f), 0.6f * shatterPull);
            if (bellModel)
            {
                bellModel.gameObject.SetActive(!st);   // XIII shatters it
                // every toll rocks the bell on its crown (16 m up), across the clapper's lane, and it rings down
                float since = c >= 1 && c < 13 ? t - TollAt[c] : 99, w = since < 3.5f ? 1.3f * Mathf.Exp(-1.4f * since) * Mathf.Sin(since * 6.5f) : 0;
                var rock = Quaternion.AngleAxis(w, Vector3.Cross(Vector3.up, Dir(c >= 1 ? LaneAng(c) : 0)));
                bellModel.localRotation = rock; bellModel.localPosition = Vector3.up * 16f - rock * (Vector3.up * 16f);
            }
            // the bell's wall breaks outward at XIII: pieces thrown off its whole round, tumbling down into the void
            if (st && !shattered) { shattered = true; W.BossFx.UltBellShatter(BellWallR); }
            if (!st) shattered = false;
            for (int i = 0; i < bellBits.Count; i++)
            {
                var bp = bellBits[i]; bool on = st && su < ShatterLen; bp.gameObject.SetActive(on); if (!on) continue;
                float a = 360f / bellBits.Count * i + (i * 37 % 11), y0 = 2f + (i % 4) * 2.2f, sp = 5f + (i % 3) * 2.5f;
                var d = Dir(a); bp.position = d * (BellWallR + sp * su) + Vector3.up * (y0 + 3f * su - 4.9f * su * su);
                bp.rotation = Quaternion.AngleAxis(200f * su + 40 * i, Vector3.Cross(Vector3.up, d) + Vector3.up * 0.3f * (i % 2 == 0 ? 1 : -1));
            }
            bool eo = EchoOn(t); if (eo) FinishGhost(); echo.gameObject.SetActive(eo);
            if (eo)
            {
                // the Echo moves after each toll: it comes apart where it was and gathers where it goes (re-forming over 0.4 s,
                // never fully gone: it is a body you can't walk through)
                var at = EchoAt(t);
                if (!echoSeen || (at - echoWas).sqrMagnitude > 0.25f) { W.BossFx.UltEchoShift(echoSeen ? echoWas : Vector3.zero, at); echoMovedAt = t; echoWas = at; echoSeen = true; }
                echo.position = at; echoLive.gameObject.SetActive(EchoLive(t));
                if (EchoLive(t)) { float p = 1f + 0.08f * Mathf.Sin(t * 8f); echoLive.localScale = new Vector3((EchoRad * 2 + 0.7f) * p, 0.02f, (EchoRad * 2 + 0.7f) * p); }
                if (ghostBlock == null) ghostBlock = new MaterialPropertyBlock();
                float form = Mathf.Clamp01((t - echoMovedAt) / 0.4f);
                ghostBlock.SetFloat("_Dissolve", 0.6f * (1 - form)); ghostBlock.SetFloat("_Intensity", 0.16f * (0.85f + 0.15f * Mathf.Sin(t * 3f)));
                foreach (var r in ghostR) if (r) r.SetPropertyBlock(ghostBlock);
            }
            else echoSeen = false;
            // the HAMMER callback: the bell's hammer hovers over its mark as it shows, drops in the last 0.25 s, lifts away
            if (hammer)
            {
                int kh = k < 13 && Has(k, 3) && TollAt[k] - t <= Show ? k : c >= 1 && c < 13 && Has(c, 3) && t - TollAt[c] < 0.6f ? c : -1;
                hammer.gameObject.SetActive(kh > 0);
                if (kh > 0)
                {
                    float left = TollAt[kh] - t, hh = left > 0.25f ? Mathf.Lerp(3f, 7f, (left - 0.25f) / Show) : left > 0 ? 3f * (left / 0.25f) : Mathf.Lerp(0, 5f, -left / 0.6f);
                    hammer.position = HammerAt(kh) + Vector3.up * hh;
                }
            }
            if (floorModel)
            {
                float fall = t - TollAt[13];   // XIII: the bell's floor drops away into the void (1.2 s)
                floorModel.gameObject.SetActive(fall < 1.2f); if (fall < 1.2f) floorModel.position = Vector3.down * (fall > 0 ? 9f * fall * fall : 0);
            }
            else floor.GetComponent<Renderer>().sharedMaterial = st ? pitM : bellM;
            // the clappers: the bell's (through the centre) and, IX-XII, the echo's
            int kc = c + 1 < 13 && t > TollAt[c + 1] - Show ? c + 1 : c; bool lanes = kc >= 1 && kc < 13; float relc = lanes ? t - TollAt[kc] : 99;
            PlaceClapper(clappers[0], Vector3.zero, lanes ? LaneAng(kc) : 0, relc);
            PlaceClapper(clappers[1], kc >= 9 && kc <= 12 ? EchoPos(kc) : Vector3.zero, kc >= 9 && kc <= 12 ? EchoLaneAng(kc) : 0, kc >= 9 && kc <= 12 ? relc : 99);
            // the Last Stand's falling shards: down from the sky over their 1 s warning, burst on landing
            for (int fi = 0; fi < fallers.Count; fi++)
            {
                var sh = Shard(fi); float u = t - (sh.at - 1f); var fx = fallers[fi];
                bool on = u > 0 && u < 1.12f; fx.gameObject.SetActive(on);
                if (on) { float uu = Mathf.Min(1, u); fx.position = sh.p + Vector3.up * (16f * (1 - uu * uu)); fx.rotation = Quaternion.Euler(70f * u + 13 * fi, 40f * fi, 25f * u); }
                if (u >= 1f && !landed[fi]) { landed[fi] = true; W.BossFx.TollResolve(sh.p, 0.2f, ShardR + 0.4f); }
                if (u < 0.5f) landed[fi] = false;
            }
            for (int h = 0; h < 4; h++)
            {
                // from XII the platforms rise up through the bell's floor (0.8 s, easing in) and wait there for XIII
                bool show = t >= TollAt[12]; plats[h].gameObject.SetActive(show); if (!show) continue;
                float up = 1 - Mathf.Clamp01((t - TollAt[12]) / 0.8f); plats[h].position = platBase[h] + Vector3.down * 3.5f * up * up;
            }
            bool showPlate = k < 13 && Has(k, 0) && HourPresent(Soaker(k));
            plate.gameObject.SetActive(showPlate); if (showPlate) plate.position = PlatePos(k) + Vector3.up * 0.04f;
            int n = 0, ns = 0;
            foreach (var b in bullets)
            {
                if (b.What == "a hammer shard" && (shardPool.Count > ns || GearFloor.Model("GF_Shard", root, Vector3.zero, Quaternion.identity) is Transform nsx && AddTo(shardPool, nsx)))
                {
                    var sx = shardPool[ns++]; sx.gameObject.SetActive(true); sx.position = new Vector3(b.P.x, 0.9f, b.P.z);
                    var vv = Sim.Flat(b.V); if (vv.sqrMagnitude > 0.01f) sx.rotation = Quaternion.FromToRotation(Vector3.left, vv.normalized) * Quaternion.Euler(Time.time * 360f, 0, 0);
                    continue;
                }
                if (pool.Count <= n) { var tr = UltBulletMesh.Make(root, bulletM); tr.localScale = Vector3.one * BR * 2.2f; pool.Add(tr); }   // a small bell (was a Prim sphere; same size)
                var x = pool[n++]; x.gameObject.SetActive(true); x.position = new Vector3(b.P.x, 0.9f, b.P.z);
            }
            for (int i = n; i < pool.Count; i++) pool[i].gameObject.SetActive(false);
            for (int i = ns; i < shardPool.Count; i++) shardPool[i].gameObject.SetActive(false);
            var me = W.Players.Count > 0 ? W.Players[0] : null;
            if (mySpot) { bool show = EscapementRelay.ShowMySpot && me && !me.Down && plans.ContainsKey(me); mySpot.gameObject.SetActive(show); if (show) { var gl = plans[me].Goal; mySpot.position = new Vector3(gl.x, 0, gl.z); } }
        }
        void Teardown()
        {
            using var mark = TeardownMarker.Auto();
            if (root) Object.Destroy(root.gameObject);
            ArenaVoid.Close(); floorModel = null; clappers[0] = clappers[1] = null; shardPool.Clear(); fallers.Clear();
            ringA = ringB = null; hammer = null; bellBits.Clear(); ghostR.Clear();
            RunDirector.CameraPull = 0;
            root = plate = mySpot = echo = echoLive = floor = bellModel = null; for (int h = 0; h < 4; h++) plats[h] = null; pool.Clear(); plans.Clear(); ghostSteps = null;
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
            if (EchoOn(t)) { Label(cam, EchoAt(t), 4.2f, EchoLive(t) ? "ECHO · LIVE" : "echo", EchoLive(t) ? new Color(1f, 0.85f, 0.45f) : new Color(0.65f, 0.85f, 1f), midStyle); Label(cam, Vector3.zero, 4.6f, EchoLive(t) ? "boss" : "BOSS · LIVE", EchoLive(t) ? new Color(0.9f, 0.88f, 0.8f, 0.7f) : new Color(1f, 0.85f, 0.45f), midStyle); }
            if (t >= TollAt[12]) for (int h = 0; h < 4; h++) Label(cam, PlatC(h) + PlatC(h).normalized * 2.3f, 0.4f, "platform " + UltimateState.Roman[h + 1], new Color(1f, 0.85f, 0.5f), nameStyle);
            if (k < 13 && Has(k, 0) && HourPresent(Soaker(k))) Label(cam, PlatePos(k), 0.6f, UltimateState.Roman[Soaker(k) + 1] + " SOAK", new Color(1f, 0.85f, 0.5f), nameStyle);
            if (k < 13 && Has(k, 2)) Label(cam, Dir(InvAng(k)) * 8f, 0.6f, UltimateState.Roman[InvHour(k) + 1] + " INVERTED: inside", new Color(0.97f, 0.94f, 0.86f), nameStyle);   // ivory, like P2 (no Arcanist violet, accepted 28 Oct)
            for (int i = 0; i < W.Players.Count; i++)
            {
                var p = W.Players[i]; if (p.Down) continue;
                Label(cam, p.transform.position, 2.9f, (i == 0 ? "YOU " : "") + UltimateState.Roman[HourOf(p) + 1] + (JobOf(p) != null ? "  · " + JobOf(p) : ""), Color.white, nameStyle);
            }
            string text = Standing(t) ? $"LAST STAND · {TollAt[13] + Stand - t:0.0}s · hits x{StandScale:0} · stay on your platform" : k >= 13 ? $"XIII in {left:0.0}s · THE BELL SHATTERS: get to your platform" : $"toll {UltimateState.Roman[k]} in {left:0.0}s · {string.Join(" + ", System.Array.ConvertAll(Kinds(k), x => KN[x]))}" + (Echoes(k) ? " · ECHO" : "") + (t >= TollAt[4] ? " · MAINSPRING" : "");
            bigStyle.normal.textColor = k >= 10 ? new Color(1f, 0.42f, 0.3f) : new Color(1f, 0.85f, 0.55f);
            GUI.Label(new Rect(0, Screen.height * 0.17f, Screen.width, 50), text, bigStyle);
            float su = t - TollAt[13]; if (su >= 0 && su < 1.8f) CrackedTitle("THE LAST BELL", su);
        }
        // XIII's title: the words crack along a slanted line, the two halves sliding apart, then fade
        static GUIStyle titleStyle;
        static void CrackedTitle(string text, float u)
        {
            if (titleStyle == null) titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 72, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            float a = Steps(u < 0.15f ? u / 0.15f : Mathf.Clamp01(1 - (u - 1.2f) / 0.6f), 10), split = Mathf.SmoothStep(0, 1, Mathf.Clamp01((u - 0.25f) / 0.5f)) * 9f;
            float y = Screen.height * 0.3f, h = 100, w = Screen.width;
            for (int half = 0; half < 2; half++)
            {
                // the top half drifts up-left, the bottom down-right; each is clipped to its side of the crack
                var shift = half == 0 ? new Vector2(-split, -split * 0.5f) : new Vector2(split, split * 0.5f);
                GUI.BeginGroup(new Rect(0, y + (half == 0 ? 0 : h * 0.52f), w, h * (half == 0 ? 0.52f : 0.48f)));
                var r = new Rect(shift.x, shift.y - (half == 0 ? 0 : h * 0.52f), w, h);
                titleStyle.normal.textColor = new Color(0.55f, 0.12f, 0.08f, 0.9f * a); GUI.Label(new Rect(r.x + 3, r.y + 3, r.width, r.height), text, titleStyle);
                titleStyle.normal.textColor = new Color(1f, 0.93f, 0.78f, a); GUI.Label(r, text, titleStyle);
                GUI.EndGroup();
            }
        }
        public string HudText(PlayerController p)
        {
            if (!Running || p == null) return null;
            float t = T; int k = Cur(t) + 1, h = HourOf(p);
            if (Standing(t)) return $"LAST STAND {TollAt[13] + Stand - t:0.0}s · stay on platform {UltimateState.Roman[h + 1]} · dodge the shards · every hit x{StandScale:0}";
            if (k >= 13) return $"XIII in {NextAt(k) - t:0.0}s: the bell shatters · get to platform {UltimateState.Roman[h + 1]}";
            string job = Has(k, 0) && Soaker(k) == h ? "you SOAK the plate" : Has(k, 2) && InvHour(k) == h ? "you are INVERTED: stand inside the slam" : Has(k, 2) ? "stay out of the slam" : "dodge";
            return $"toll {UltimateState.Roman[k]} in {NextAt(k) - t:0.0}s · you ({UltimateState.Roman[h + 1]}): through gap {UltimateState.Roman[h + 1]} · {job} · hits ring the bell: x{BellkeeperEncounter.V2P5Resonance:0.##}";
        }

        public override void Cancel() { base.Cancel(); bullets.Clear(); debts.Clear(); Teardown(); }
        public void ResetCounts() { Fails.Clear(); }
    }
}
