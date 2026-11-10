using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // The Gear Floor (`bellkeeper.ult_gear_floor`), Bellkeeper Ultimate v2 P4 "The Unfinished Hour" — proposal, built
    // 2026-10-25 from the browser simulation Chzy18 approved for building (tools/bellkeeper-ultimate/sims/p4-example.html:
    // gear floor, random hammer, harder, cores for melee); not human-approved.
    // The floor is the clock's gear train: a centre gear under the boss (r 3.6) and four outer gears N/E/S/W (r 4.0, 7.2 m
    // out) meshing with it and turning the other way. Everything else is the pit (999). The gears carry you, so you are
    // always walking: away from each outer gear's rim pawl (999). The gears only touch at the four mesh points beside the
    // centre gear, and those bite in turn (999 for 0.35 s in every 2 s).
    // Every 7.5 s the HAMMER slams a random outer gear (shown 2.5 s before; every third slam two gears): anyone on it 999,
    // and it throws a ring of 16 brass shards (50, two kill). Each player belongs to a gear that moves two places round
    // after every slam (so most slams everyone crosses through the centre); the player whose gear is hammered waits on the
    // centre. A gear without ITS OWN player at a slam jams the train: WIPE (anyone else on it doesn't count). Every third slam the gears reverse.
    // Each outer gear's hub is a CORE (BellkeeperEncounter.ExtraBodies): solid, and a hit on it damages the boss (one bar).
    // Melee parks against its core, where the floor barely moves. Below 4: only gears with a
    // player assigned must be held.
    // Bots steer (Steer) with the simulation's route search, the floor's drift included.
    // The two transitions live here too (proposal, sims/p5-example.html, Chzy18 25 Oct "the entire fight put together"):
    //   THE FLOOR BREAKS (cue "break", P3 -> P4): 15 s before the gears turn (P4 time runs from -15). P3's spiral arms fire
    //     twice as fast; the floor cracks along the gear outlines and falls in two waves (-9 s: beyond 9 m off the gears,
    //     -5 s: everything off the gears); at -3 s the gears lock: each player's first gear must be held or the train jams.
    //   THE TRAIN RUNS WILD (GoWild(), P4 -> P5): after the last slam, 15 s: the gears speed up x1 -> x2.2, a single hammer
    //     slams every 2.5 s (shown 1.5 s, 12 shards), then the outer gears tear loose N, E, S, W at 9, 10.5, 12, 13.5 s (anyone
    //     on one goes with it; each sprays 5 shards); everyone ends on the centre gear and the great bell falls (P5).
    public sealed class GearFloor : UltimateMechanic
    {
        public override string Id => UltimateMechanics.GearFloor;
        public override string Name => "The Gear Floor";

        public const string Source = "The Gear Floor";
        public const float C_R = 3.6f, O_R = 4.0f, O_D = 7.2f, HubR = 0.9f, PawlIn = 2.0f, W0 = 0.95f, Slam = 7.5f, Warn = 2.5f,
            BiteEvery = 2f, BiteLen = 0.35f, Settle = 4f, BiteR = 1f, ShardSpeed = 4f, BR = 0.22f, PR = 0.45f;
        const int Flip = 3, Shards = 16, ShardDamage = 50;
        static readonly string[] GN = { "N", "E", "S", "W", "CENTRE" };

        sealed class Shard { public Vector3 P, V; public bool Gone; public string What = "a hammer shard"; }
        readonly List<Shard> shards = new List<Shard>();
        int t0, slams, seed; bool stop, goWild, wild, breakMode, locked; float settle, wildAt, nextArm; int wildSlams;
        readonly bool[] launched = new bool[4];
        public const float BreakLen = 15f, WildLen = 15f;
        // the pawls are drawn from the first tick and lethal 3 s later: P3's outer ring (9.6-10.6 m) crosses the pawl lines (9.2-11.2 m on
        // the N/E/S/W axes), so a player could end P3 standing on one with no warning (every mechanic needs a real way to survive)
        public const float PawlGrace = 3f;
        float pawlLive;
        public bool CoreGone(int g) => launched[g];
        readonly float[] spin = new float[5];
        public int Slams => slams;
        public int Jams { get; private set; }
        public readonly List<string> Fails = new List<string>();

        public override UltCue DefaultCue() => new UltCue { Variant = "practice" };
        public void StopAfterSlam() => stop = true;
        public void GoWild() => goWild = true;   // after the next slam: The Train Runs Wild, then the cast ends

        // ---------------- geometry and the rule (world coordinates; angles via the plan dial) ----------------
        float T => (Now - t0) / (float)Sim.Hz;
        static Vector3 Dir(float a) => EscapementRelay.PlanDir(a);
        // fixed geometry, worked out once (the route search looked these up with a sine and cosine each, several times per check:
        // 11 ms a search in P4, performance agent 2 Nov). Same values as before.
        static readonly Vector3[] centres = Each(5, g => g == 4 ? Vector3.zero : Dir(90f * g) * O_D), meshPts = Each(4, g => Dir(90f * g) * C_R),
            pawlA = Each(4, g => GearCentre(g) + Dir(90f * g) * PawlIn), pawlB = Each(4, g => GearCentre(g) + Dir(90f * g) * O_R);
        static Vector3[] Each(int n, System.Func<int, Vector3> f) { var a = new Vector3[n]; for (int i = 0; i < n; i++) a[i] = f(i); return a; }
        public static Vector3 GearCentre(int g) => centres[g];
        static float GearR(int g) => g == 4 ? C_R : O_R;
        public static Vector3 Core(int g) => GearCentre(g);
        static Vector3 MeshPoint(int g) => meshPts[g];
        int SpinSign(float t) => (Mathf.FloorToInt(t / Slam) / Flip) % 2 == 1 ? -1 : 1;
        float SpeedK(float t) => t < 0 ? 0 : wild ? 1 + 1.2f * Mathf.Min(1, (t - wildAt) / 12f) : 1;   // locked in the break, faster and faster in the wild
        float Omega(int g, float t) => SpeedK(t) * SpinSign(t) * (g == 4 ? W0 : -W0 * C_R / O_R);
        float LaunchAt(int g) => wildAt + 9 + 1.5f * g;
        bool FloorGone(Vector3 q, float t) => t >= settle || (breakMode && t < 0 && (t >= -5 || (t >= -9 && Sim.Flat(q).magnitude > 9)));
        int GearAt(Vector3 q)
        {
            int best = -1; float bd = 9;
            for (int i = 0; i < 5; i++) { if (i < 4 && launched[i]) continue; float d = (Sim.Flat(q) - GearCentre(i)).magnitude / GearR(i); if (d <= 1 && d < bd) { bd = d; best = i; } }
            return best;
        }
        Vector3 FloorVel(Vector3 q, float t) => FloorVel(q, t, GearAt(q));
        Vector3 FloorVel(Vector3 q, float t, int g)   // g = GearAt(q), when the caller has it
        {
            if (g < 0) return Vector3.zero;
            var r = Sim.Flat(q) - GearCentre(g); float w = Omega(g, t);
            return new Vector3(-w * r.z, 0, w * r.x);
        }
        int SlamGear(int k)
        {
            uint x = (uint)(k * 2654435761u) + (uint)(seed * 40503);
            x ^= x >> 15; x *= 2246822519u; x ^= x >> 13;
            return (int)(x % 4);
        }
        List<int> SlamGears(int k)
        {
            var l = new List<int> { SlamGear(k) };
            if (k % 3 == 0) l.Add((SlamGear(k) + 1 + ((k >> 2) % 3)) % 4);
            return l;
        }
        bool Revealed(int k, float t) => t >= k * Slam - (Warn + WarnLead);
        int HourOf(PlayerController p) { int i = W.Players.IndexOf(p); return i < 0 ? -1 : i % 4; }
        int TargetOf(PlayerController p, int k, float t)
        {
            int g = ((HourOf(p) + 2 * k) % 4 + 4) % 4;
            return Revealed(k, t) && SlamGears(k).Contains(g) ? 4 : g;
        }
        static (Vector3 a, Vector3 b) Pawl(int g) => (pawlA[g], pawlB[g]);
        static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a; float t = Mathf.Clamp01(Vector3.Dot(Sim.Flat(p) - a, ab) / ab.sqrMagnitude);
            return (Sim.Flat(p) - (a + ab * t)).magnitude;
        }
        bool BiteWithin(int g, float t, float dur) { for (float s = 0; s <= dur + 1e-4f; s += 0.05f) if (Biting(g, t + s)) return true; return Biting(g, t + dur); }
        bool Biting(int g, float t) { float ph = ((t + g * BiteEvery / 4) % BiteEvery + BiteEvery) % BiteEvery; return ph < BiteLen; }
        // what kills a grounded player at q at time t ("" = nothing)
        string Deadly(Vector3 q, float t) => Deadly(q, t, GearAt(q));
        string Deadly(Vector3 q, float t, int gear, bool pawls = true)   // the route search passes pawls: false (its own pawl rule is stricter)
        {
            if (gear < 0) return FloorGone(q, t) ? "the pit" : "";   // the floor is still whole at first (Settle, or the break's waves)
            for (int i = 0; i < 4; i++)
            {
                if (launched[i]) continue;
                if (pawls && t >= pawlLive && SegDist(q, pawlA[i], pawlB[i]) < PR + 0.15f) return "a pawl";
                if (t >= settle && !wild && Biting(i, t) && (Sim.Flat(q) - MeshPoint(i)).magnitude < BiteR + PR) return "the teeth";
            }
            return "";
        }

        // ---------------- cast ----------------
        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name);
            stop = false; goWild = false; wild = false; locked = false; wildSlams = 0; for (int g = 0; g < 4; g++) launched[g] = false;
            breakMode = Cue.Variant == "break"; settle = breakMode ? 0 : Settle; pawlLive = (breakMode ? -BreakLen : 0) + PawlGrace; nextArm = -BreakLen + 0.5f;
            t0 = Now + (breakMode ? Sim.Ticks(BreakLen) : 0); slams = 0; seed = Cue.Seed % 997; shards.Clear(); plans.Clear();
            for (int i = 0; i < 5; i++) spin[i] = 0;
            using (BuildMarker.Auto()) Build();
            if (breakMode) CastClip("ShatteredChime", 3.1f);   // the floor cracks
            var telegraphs = new List<Hazard>();
            while (true)
            {
                float t = T, nt = t + Sim.Dt;
                for (int g = 0; g < 5; g++) spin[g] += Omega(g, t) * Sim.Dt * Mathf.Rad2Deg;
                // the floor carries everyone on it, a downed body too (it rides its gear, so a reviver beside it can stand still)
                foreach (var p in W.Players)
                {
                    if (p.Dead) continue;
                    var v = FloorVel(p.transform.position, t);
                    if (v.sqrMagnitude > 0) p.transform.position = W.Constrain(p.transform.position + v * Sim.Dt, PlayerController.Radius);
                }
                foreach (var p in W.Players) { if (p.Down) continue; var why = Deadly(p.transform.position, nt); if (why != "") Hit(p, UltimateState.Lethal, why); }
                // shards
                foreach (var s in shards) s.P += s.V * Sim.Dt;
                foreach (var p in W.Players)
                {
                    if (p.Down) continue; var q = Sim.Flat(p.transform.position);
                    foreach (var s in shards) if (!s.Gone && (q - s.P).magnitude < PR + BR) { s.Gone = true; Hit(p, ShardDamage, s.What); }
                }
                shards.RemoveAll(s => s.Gone || s.P.magnitude > 12.5f);
                // THE FLOOR BREAKS: P3's spiral arms (twice as fast) until the gears lock; the lock
                if (breakMode && t < 0)
                {
                    while (nextArm <= nt && nextArm < -3) { for (int j = 0; j < 3; j++) { var d = Dir(40 * (nextArm + BreakLen) + 120 * j); shards.Add(new Shard { P = d * Boss.BodyRadius, V = d * 4f, What = "a spiral bullet" }); } nextArm += 0.38f; }
                    if (t < -9 && nt >= -9) Boss.PlanLog.Add((Now, "the floor breaks: wave 1"));
                    if (t < -5 && nt >= -5) { Boss.PlanLog.Add((Now, "the floor breaks: wave 2")); CameraShake.Shake(0.2f, 0.4f); CastClip("ShatteredChime", 2f); }
                    if (!locked && nt >= -3)
                    {
                        locked = true; Boss.PlanLog.Add((Now, "the gears lock"));
                        for (int g = 0; g < 4; g++)
                        {
                            bool assigned = false, held = false;
                            foreach (var p in W.Players) { if (p.Down || (HourOf(p) + 2) % 4 != g) continue; assigned = true; if (GearAt(p.transform.position) == g) held = true; }
                            if (assigned && !held) { Jams++; Fails.Add($"the lock: gear {GN[g]} empty"); Ult.Wipe($"The gear train jams (gear {GN[g]} empty)", Id); break; }
                        }
                        if (Boss.Ult.WipeCause != null) break;
                    }
                }
                // THE TRAIN RUNS WILD
                if (wild)
                {
                    float tw = nt - wildAt;
                    int ws = Mathf.FloorToInt(tw / 2.5f);
                    if (ws >= 1 && ws <= 3 && ws > wildSlams)
                    {
                        wildSlams = ws; int sg = WildSlamGear(ws);
                        if (!launched[sg])
                        {
                            foreach (var p in W.Players) if (!p.Down && GearAt(p.transform.position) == sg) Hit(p, UltimateState.Lethal, "the hammer");   // it falls from above: a dash is no shield
                            for (int j = 0; j < 12; j++) shards.Add(new Shard { P = GearCentre(sg), V = Dir(30 * j + 7 * ws) * ShardSpeed });
                            W.BossFx.TollResolve(GearCentre(sg), 0.5f, O_R); CameraShake.Shake(0.15f, 0.25f);
                        }
                    }
                    if (wildTele == null && ws < 3 && tw >= 2.5f * (ws + 1) - 1.5f)
                    {
                        int sg = WildSlamGear(ws + 1); float left2 = 2.5f * (ws + 1) - tw;
                        CastClip("BellSweep", left2);
                        wildTele = Spawn(new Hazard { Source = Source + ": the hammer", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = GearCentre(sg), Radius = O_R, LockAt = 0, ResolveAt = UltShapes.ResolveIn(Mathf.Max(1, Sim.Ticks(left2))), Damage = 0, Knockback = 0, Stun = 0, HitOnResolve = false });
                    }
                    if (wildTele != null && wildTele.Age >= wildTele.ResolveAt) wildTele = null;
                    for (int g = 0; g < 4; g++)
                        if (!launched[g] && nt >= LaunchAt(g))
                        {
                            foreach (var p in W.Players) if (!p.Down && GearAt(p.transform.position) == g) Hit(p, UltimateState.Lethal, $"gear {GN[g]} (it tore loose)");
                            launched[g] = true; Boss.PlanLog.Add((Now, $"gear {GN[g]} tears loose"));
                            for (int j = -2; j <= 2; j++) shards.Add(new Shard { P = GearCentre(g), V = Dir(90f * g + 180 + 24 * j) * 5f });
                            CameraShake.Shake(0.25f, 0.35f);
                        }
                    if (tw >= WildLen) { CastClip("HollowToll", 2f); Boss.PlanLog.Add((Now, "the great bell falls")); W.BossFx.TollResolve(Vector3.zero, 1f, 11.5f); CameraShake.Shake(0.4f, 0.7f); break; }
                }
                // the hammer: telegraph, then the slam
                int k = Mathf.FloorToInt(nt / Slam) + 1;
                if (!wild && k >= 1 && telegraphs.Count == 0 && Revealed(k, nt))
                    using (TeleMarker.Auto())
                    foreach (var g in SlamGears(k))
                    {
                        if (telegraphs.Count == 0) CastClip("BellSweep", k * Slam - t);   // the hammer swings onto the slam
                        int left = Mathf.Max(1, Sim.Ticks(k * Slam - t));
                        var h = Spawn(new Hazard { Source = Source + ": the hammer", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = GearCentre(g), Radius = O_R, LockAt = 0, ResolveAt = UltShapes.ResolveIn(left), Damage = 0, Knockback = 0, Stun = 0, HitOnResolve = false });
                        h.OnResolve = hz => { W.BossFx.TollResolve(hz.Origin, 0.5f, O_R); CameraShake.Shake(0.15f, 0.25f); };
                        telegraphs.Add(h);
                    }
                int ks = Mathf.FloorToInt(nt / Slam);
                if (!wild && ks >= 1 && ks > slams)
                {
                    using var slamMark = SlamMarker.Auto();
                    slams = ks; telegraphs.Clear();
                    var ss = SlamGears(ks);
                    foreach (var p in W.Players) if (!p.Down && ss.Contains(GearAt(p.transform.position))) Hit(p, UltimateState.Lethal, "the hammer");   // from above: no dash exemption
                    foreach (var g in ss) for (int j = 0; j < Shards; j++) shards.Add(new Shard { P = GearCentre(g), V = Dir(360f / Shards * j + 11 * ks) * ShardSpeed });
                    // the train jams if a gear is not held by its own player (Chzy18 25 Oct "force it": anyone else on it doesn't
                    // count, so the two-place rotation is the rule, not just the bots' strat)
                    for (int g = 0; g < 4; g++)
                    {
                        if (ss.Contains(g)) continue;
                        bool assigned = false, held = false; string who = "";
                        foreach (var p in W.Players)
                        {
                            if (p.Down || ((HourOf(p) + 2 * ks) % 4 + 4) % 4 != g) continue;
                            assigned = true; who = UltimateState.Roman[HourOf(p) + 1];
                            if (GearAt(p.transform.position) == g) held = true;
                        }
                        if (assigned && !held) { Jams++; Fails.Add($"slam {ks}: gear {GN[g]} without {who}"); Ult.Wipe($"The gear train jams (gear {GN[g]} needs {who})", Id); break; }
                    }
                    Boss.PlanLog.Add((Now, $"gear slam {ks} {string.Join("+", ss.ConvertAll(g => GN[g]))}"));
                    if (stop) break;
                    if (goWild) { wild = true; goWild = false; wildAt = nt; Boss.PlanLog.Add((Now, "the train runs wild")); }
                }
                if (Log && (Now - t0) % 30 == 0) foreach (var p in W.Players) if (!p.Down) Debug.Log($"[gear floor] {t:0.0}s {UltimateState.Roman[HourOf(p) + 1]} at ({p.transform.position.x:0.0},{p.transform.position.z:0.0}){(p.Dashing ? " dash" : "")} gear {GearAt(p.transform.position)} target {TargetOf(p, k, t)} job {JobOf(p)}");
                if (Boss.Ult.WipeCause != null) break;
                using (PresentMarker.Auto()) Present(nt);
                yield return 1;
            }
            shards.Clear(); wildTele = null;
            Teardown();
        }
        Hazard wildTele;
        int WildSlamGear(int j) { for (int n = 0; n < 4; n++) { int g = SlamGear(100 + j + 7 * n); if (!launched[g]) return g; } return SlamGear(100 + j); }

        void Hit(PlayerController p, int dmg, string what)
        {
            if (Log) Debug.Log($"[gear floor] {T:0.00}s {UltimateState.Roman[HourOf(p) + 1]} hit by {what} at ({p.transform.position.x:0.0},{p.transform.position.z:0.0}) job {JobOf(p)} · {p.ClassName} hp {p.Hp} slow {(p.Kit != null ? p.Kit.SlowLeft : 0):0.00}");
            Fails.Add($"{T:0.0}s {UltimateState.Roman[HourOf(p) + 1]}: {what}");
            p.ReceiveHit(new Hazard { Source = Source + ": " + what, AttackId = Id, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = Sim.Flat(p.transform.position), Radius = 0.5f, Damage = dmg, Knockback = 0, Stun = 0 });
        }

        // ---------------- bots ----------------
        sealed class Plan { public Vector3 V; public int Until; public string Job; public Vector3 Goal; }
        readonly Dictionary<PlayerController, Plan> plans = new Dictionary<PlayerController, Plan>();
        public static bool Log;   // tests: bot positions every 0.5 s, and every time a bot finds no safe route
        public string JobOf(PlayerController p) => plans.TryGetValue(p, out var pl) ? pl.Job : null;
        public override string Job(PlayerController p) => JobOf(p);
        // the Tollbreaker parks against its core too (Chzy18 2 Nov, review inbox): its axes fly back sooner from close in
        static bool Melee(PlayerController p) => p.ClassName == "Tollbreaker";

        Vector3 GoalOf(PlayerController p, float t, out string job, out int k)
        {
            k = Mathf.Max(1, Mathf.FloorToInt(t / Slam) + 1); int tg = TargetOf(p, k, t), cur = GearAt(p.transform.position);
            job = "→ " + GN[tg];
            int i = W.Players.IndexOf(p);
            if (wild)
            {
                // ride my gear until it is hammered or about to tear loose, then cross to the centre gear and stay there
                float tw = t - wildAt; int ws = Mathf.FloorToInt(tw / 2.5f) + 1;
                bool hammered = cur >= 0 && cur < 4 && ws <= 3 && 2.5f * ws - tw < 1.5f && WildSlamGear(ws) == cur;
                // leave 5 s before the gear tears loose (was 3): the spin-up made the last crossing impossible (a party Vanguard went with gear E)
                if (cur >= 0 && cur < 4 && !hammered && tw < 4.5f && tw < LaunchAt(cur) - wildAt - 5) tg = cur; else tg = 4;
                job = tg == 4 ? "→ CENTRE" : "ride " + GN[tg];
            }
            Vector3 Park(int g) => g == 4
                ? Dir(45 + 90 * (i % 4) + spin[4]) * 2.3f
                : GearCentre(g) + Dir(90f * g + 180 + (SpinSign(t) > 0 ? 35 : -35)) * (Melee(p) ? HubR + PR + 0.5f : 2.6f);
            // revives: get a downed ally up (a spot beside it, off its core), on any gear (5 Nov: each bot rides its own gear, so a
            // body on a gear nobody else rides was left to bleed out). Free on the gear I need next; otherwise only while the trip
            // there, the 3 s channel and the trip on to my next gear all fit before that gear needs me (the lock, or the next slam)
            if (!wild && cur >= 0 && ReviveSpot(p, q => { int g = GearAt(q); return g >= 0 && (g == 4 || (q - GearCentre(g)).magnitude > HubR + PR + 0.2f); }, out var rs))
            {
                int rg = GearAt(rs); var pos0 = Sim.Flat(p.transform.position);
                float due = breakMode && !locked ? -3 : k * Slam, walk = PlayerController.WalkSpeed * (t < 0 ? 0.8f : 0.6f);
                float trip = Route(pos0, cur, rs, rg) + (rg == tg ? 0 : Route(rs, rg, Park(tg), tg)), left = PlayerController.ReviveSeconds;
                foreach (var q in W.Players) if (q.Down && !q.Dead && Sim.Flat(q.transform.position - rs).magnitude < 1.2f) left *= 1 - q.ReviveProgress;   // a revive under way: only the rest of it
                if (rg == tg || t + trip / walk + left + 1.5f < due)
                {
                    if (rg == cur) { job = "revive"; return rs; }
                    job = "→ revive " + GN[rg]; tg = rg;   // cross to its gear the usual way (through the centre gear's mesh points)
                }
            }
            Vector3 goal = cur == tg ? Park(tg) : cur == 4 ? (tg == 4 ? Park(4) : MeshPoint(tg)) : cur < 0 ? Park(tg) : Dir(90f * cur) * 2.6f;   // else across the mesh point onto the centre gear's ring
            // the core (and the pawl behind it) block the straight line: go round the core first (the short route search can't see past it)
            if (cur >= 0 && cur < 4)
            {
                var hub = GearCentre(cur); var pos = Sim.Flat(p.transform.position);
                if (SegDist(hub, pos, goal) < HubR + PR + 0.35f && (pos - hub).magnitude > 0.1f)
                {
                    var along = (goal - hub).normalized; var side = Vector3.Cross(Vector3.up, along);
                    if (Vector3.Dot(pos - hub, side) < 0) side = -side;
                    return hub + side * (HubR + PR + 0.8f) + along * 0.6f;
                }
            }
            return goal;
        }

        // walking distance from a on gear ga to b on gear gb: gears only meet at the centre gear's mesh points
        static float Route(Vector3 a, int ga, Vector3 b, int gb)
        {
            if (ga == gb || ga < 0 || gb < 0) return (a - b).magnitude;
            if (ga == 4) return (a - MeshPoint(gb)).magnitude + (MeshPoint(gb) - b).magnitude;
            if (gb == 4) return (a - MeshPoint(ga)).magnitude + (MeshPoint(ga) - b).magnitude;
            return (a - MeshPoint(ga)).magnitude + (MeshPoint(ga) - MeshPoint(gb)).magnitude + (MeshPoint(gb) - b).magnitude;
        }

        // The Train Runs Wild spins the floor at up to x2.2 and sprays shards from every hammer and torn gear: replan every 3 ticks
        // there, as P5 does (2 Nov: at 6 a party Tollbreaker on the centre gear drifted into a shard)
        protected override int ReplanTicks => wild ? 3 : 6;

        public override bool Steer(PlayerController p, UltimateBot.Skill skill, out Vector3 vel)
        {
            vel = Vector3.zero;
            if (!Running || p.Down) return false;
            if (plans.TryGetValue(p, out var cur) && (Now < cur.Until || !SearchTurn(p))) { vel = cur.V; return true; }
            float t = T; var goal = GoalOf(p, t, out string job, out int k);
            if (skill == UltimateBot.Skill.Miss && k % 4 == 3)   // Miss forgets one hop: stays where it is for that slam
            {
                int g = GearAt(p.transform.position); if (g >= 0 && g < 4) goal = GearCentre(g) + Dir(90f * g + 180) * 2.6f;
            }
            var v = Search(p, Sim.Flat(p.transform.position), t, goal, k);
            plans[p] = new Plan { V = v, Until = Now + (skill == UltimateBot.Skill.Late ? 15 : 1), Job = job, Goal = goal };
            vel = v; return true;
        }

        public override bool SafeToRoot(PlayerController p, float seconds, float scale)
        {
            if (!Running || p.Down || !plans.TryGetValue(p, out var pl)) return true;
            float t = T; int k = Mathf.Max(1, Mathf.FloorToInt(t / Slam) + 1);
            return RootTest(seconds, scale, () => Search(p, Sim.Flat(p.transform.position), t, pl.Goal, k));
        }

        public override bool AttackTarget(PlayerController p, out Vector3 aim, out float reach, out bool fullKit)
        {
            aim = Boss.transform.position; reach = 99; fullKit = false;
            if (!Running) return false;
            var q = Sim.Flat(p.transform.position);
            for (int i = 0; i < Boss.BodyCount; i++)
            {
                Boss.Body(i, out var c, out float r); if (r <= 0) continue;
                float d = (q - Sim.Flat(c)).magnitude - r - PR;
                if (d < reach) { reach = d; aim = c; }
            }
            float t = T; int k = Mathf.Max(1, Mathf.FloorToInt(t / Slam) + 1);
            fullKit = !wild && t >= 0 && GearAt(q) == TargetOf(p, k, t) && k * Slam - t > Warn + 1.5f;   // settled on its gear, the hammer far off
            // still travelling, or about to hop: no swings (the Vanguard's basic roots it and lunges it into the core)
            // casts slow you below the floor's drift: a basic attack needs 0.5 s of drift clear of every mesh and pawl, the whole kit 1.5 s
            int clearSteps = 30; var dq = q;
            for (int s = 0; s <= 30; s++) { bool hit = false; for (int g = 0; g < 4; g++) { if ((dq - MeshPoint(g)).magnitude < BiteR + PR + 0.3f) hit = true; var (pa, pb) = Pawl(g); if (SegDist(dq, pa, pb) < PR + 0.5f) hit = true; } if (hit) { clearSteps = s; break; } dq += FloorVel(dq, t + s * 0.05f) * 0.05f; }
            if (clearSteps < 30) fullKit = false;
            // The Train Runs Wild: no swing on a gear that tears loose within 2 s (a rooted solo Vanguard went with gear S)
            { int gq = GearAt(q); if (wild && gq >= 0 && gq < 4 && !launched[gq] && LaunchAt(gq) - t < 2f) { reach = 99; fullKit = false; return true; } }
            // a swing roots its caster: none while a shard or spiral bullet crosses this spot in the next 0.6 s (in The Floor Breaks a parked
            // Vanguard swung at its core and took four bullets in a row)
            foreach (var sh in shards) if (!sh.Gone) for (float dt = 0; dt <= 0.6f; dt += 0.05f) if ((sh.P + sh.V * dt - q).magnitude < PR + BR + 0.3f) { reach = 99; fullKit = false; return true; }
            if (clearSteps < 10 || (!wild && GearAt(q) != TargetOf(p, k, t)) || (plans.TryGetValue(p, out var pl) && (q - Sim.Flat(pl.Goal)).magnitude > 1.0f)) { reach = 99; fullKit = false; }
            return true;
        }

        // the route search (P4 bots): 3 legs of 0.2 s, a beam of 5 over 40 velocities (10 over 52 before 2 Nov). 2 Nov (performance agent: P4 ran at about 7 fps
        // with 4 bots, all in the bots' step): the same routes and picks with less work. A shard is only predicted where a 0.6 s route
        // could reach it (sprint plus the floor's fastest drift), each sample looks its gear up once, and the lists are reused.
        const float MaxFloorSpeed = 8.4f;   // 2.2 x 0.95 rad/s at a 4 m rim, rounded up
        readonly List<Vector3>[] pred = new List<Vector3>[12];
        readonly List<Vector3> dirs = new List<Vector3>(), vels = new List<Vector3>();
        struct Node { public Vector3 Q, First; public float Risk, Score; public int Gear; }
        List<Node> beam = new List<Node>(), grown = new List<Node>();
        static readonly System.Comparison<Node> ByScore = (x, y) => x.Score.CompareTo(y.Score);
        static readonly System.Comparison<Vector3> ByX = (a, b) => a.x.CompareTo(b.x);
        // performance captures: the cast's own steps by name (P4 had a 23 ms frame about 42 s into the gear floor: slam 6's two hammers show then)
        static readonly Unity.Profiling.ProfilerMarker BuildMarker = new Unity.Profiling.ProfilerMarker("Ultimate.GearFloor.Build"), PresentMarker = new Unity.Profiling.ProfilerMarker("Ultimate.GearFloor.Present"),
            SlamMarker = new Unity.Profiling.ProfilerMarker("Ultimate.GearFloor.Slam"), TeleMarker = new Unity.Profiling.ProfilerMarker("Ultimate.GearFloor.HammerTelegraph"), TeardownMarker = new Unity.Profiling.ProfilerMarker("Ultimate.GearFloor.Teardown");
        static readonly Unity.Profiling.ProfilerMarker SearchMarker = new Unity.Profiling.ProfilerMarker("Ultimate.GearFloor.Search");
        Vector3 Search(PlayerController p, Vector3 pos, float t, Vector3 goal, int k)
        {
            using (SearchMarker.Auto()) return SearchRoutes(p, pos, t, goal, k);
        }
        Vector3 SearchRoutes(PlayerController p, Vector3 pos, float t, Vector3 goal, int k)
        {
            const float Leg = 0.2f; const int Legs = 3, Samples = 4, BeamW = 5; float dtS = Leg / Samples; int N = Legs * Samples;
            k = Mathf.Max(1, k); float slamT = wild ? float.MaxValue : k * Slam; var ss = SlamGears(k); bool revealed = !wild && Revealed(k, t + Leg * Legs);
            float tw0 = t - wildAt; int wsN = Mathf.FloorToInt(tw0 / 2.5f) + 1; float wSlamT = wild && wsN <= 3 ? wildAt + 2.5f * wsN : float.MaxValue; int wSlamG = wild && wsN <= 3 ? WildSlamGear(wsN) : -1;
            for (int s = 0; s < N; s++)
            {
                float tq = t + (s + 1) * dtS; var arr = pred[s] ?? (pred[s] = new List<Vector3>()); arr.Clear();
                // a shard farther than any route can get by this sample, plus the 0.5 m risk margin, can't change the result
                float lim = (s + 1) * dtS * (PlayerController.SprintSpeed + MaxFloorSpeed) + PR + BR + 0.5f, lim2 = lim * lim;
                void Add(Vector3 b) { float dx = b.x - pos.x, dz = b.z - pos.z; if (dx * dx + dz * dz < lim2) arr.Add(b); }
                foreach (var sh in shards) Add(sh.P + sh.V * (tq - t));
                if (tq > slamT) foreach (var g in ss) for (int j = 0; j < Shards; j++) Add(GearCentre(g) + Dir(360f / Shards * j + 11 * k) * ShardSpeed * (tq - slamT));
                if (tq > wSlamT && wSlamG >= 0) for (int j = 0; j < 12; j++) Add(GearCentre(wSlamG) + Dir(30 * j + 7 * wsN) * ShardSpeed * (tq - wSlamT));
                if (wild) for (int g = 0; g < 4; g++) { float la = LaunchAt(g); if (!launched[g] && tq > la) for (int j = -2; j <= 2; j++) Add(GearCentre(g) + Dir(90f * g + 180 + 24 * j) * 5f * (tq - la)); }
                arr.Sort(ByX);   // looked up by x below
            }
            var bossP = Sim.Flat(Boss.transform.position); string why = ""; var (slowFor, ms) = SlowOf(p); lastClean = true;
            // The Train Runs Wild: once on the centre gear, stay on it (everyone ends there). A solo Vanguard dodged shards onto gear S
            // as the spin-up began and could never cross back: every route led off the gear
            bool holdCentre = wild && GearAt(pos) == 4;
            float hitR = PR + BR; const float Near = 0.5f + PR + BR;   // a shard farther than this adds no risk
            bool Dead(Vector3 q, int s, ref float risk, out int gq)
            {
                float tq = t + (s + 1) * dtS;
                gq = GearAt(q);
                string d = Deadly(q, tq, gq, false); if (d != "") { why = d; return true; }
                { float c = (q - bossP).magnitude - Boss.BodyRadius - PR; if (c < -0.05f) { why = "boss"; return true; } if (c < 0.4f) risk += (0.4f - c) * 2; }   // the boss is solid, not deadly
                bool inside = false; for (int g = 0; g < 5; g++) if ((g == 4 || (!launched[g] && !(wild && tq >= LaunchAt(g) - 0.25f))) && (q - GearCentre(g)).magnitude <= GearR(g) - 0.25f) inside = true;
                if (!inside && FloorGone(q, tq + 0.1f)) { why = "off gear"; return true; }
                if (holdCentre && gq >= 0 && gq < 4) { why = "left the centre"; return true; }
                if (wSlamG >= 0 && tq >= wSlamT - dtS * 0.5f && tq < wSlamT + dtS * 0.5f && gq == wSlamG) { why = "slam"; return true; }
                for (int g = 0; g < 4; g++) { if (launched[g]) continue; float c = (q - GearCentre(g)).magnitude - HubR - PR; if (c < -0.1f) { why = "core"; return true; } if (c < 0.3f) risk += (0.3f - c) * 1.5f; }   // the core is solid, not deadly: only a route through it is out
                if (revealed && tq >= slamT - dtS * 0.5f && tq < slamT + dtS * 0.5f && ss.Contains(gq)) { why = "slam"; return true; }
                var ps = pred[s]; float m2 = float.MaxValue;
                int lo = 0, hi = ps.Count; while (lo < hi) { int mid = (lo + hi) >> 1; if (ps[mid].x < q.x - Near) lo = mid + 1; else hi = mid; }
                for (int i = lo; i < ps.Count && ps[i].x <= q.x + Near; i++) { var b = ps[i]; float dx = q.x - b.x, dz = q.z - b.z, d2 = dx * dx + dz * dz; if (d2 < m2) m2 = d2; }
                float mc = m2 == float.MaxValue ? 9 : Mathf.Sqrt(m2) - hitR;
                if (mc < 0.12f) { why = "shard"; return true; } if (mc < 0.5f) risk += (0.5f - mc) * 2;
                for (int g = 0; g < 4; g++) { float depth = BiteR + PR + 0.1f - (q - MeshPoint(g)).magnitude; if (tq >= settle && !wild && depth > 0 && BiteWithin(g, tq - 0.08f, depth / 3f + 0.15f)) { why = "teeth"; return true; } }   // inside a mesh only if it can get out before the bite
                if (gq >= 0 && gq < 4) { float edge = O_R - (q - GearCentre(gq)).magnitude; if (edge < 0.6f && (q - MeshPoint(gq)).magnitude > 1.4f) risk += (0.6f - edge) * 3; }
                // a live pawl kills at PR + 0.15: the route keeps a hard 0.4 (circling round the core to the inner park spot, a Vanguard
                // shaved the pawl and the floor's drift finished it), then a soft cost out to 0.8
                for (int g = 0; g < 4; g++) { if (launched[g]) continue; var (a, b) = Pawl(g); float c = SegDist(q, a, b) - PR; if (tq >= pawlLive && c < 0.4f) { why = "pawl"; return true; } if (c < 0.8f) risk += (0.8f - c) * 2; }
                return false;
            }
            dirs.Clear(); for (int j = 0; j < 12; j++) dirs.Add(Dir(30f * j));
            var tg = goal - pos; if (tg.magnitude > 1e-3f) dirs.Add(tg.normalized);
            vels.Clear(); vels.Add(Vector3.zero);
            foreach (var d in dirs) { vels.Add(d * PlayerController.WalkSpeed); vels.Add(d * PlayerController.SprintSpeed); vels.Add(d * PlayerController.WalkSpeed * 0.4f); }
            beam.Clear(); beam.Add(new Node { Q = pos, First = Vector3.zero, Risk = 0, Score = 0, Gear = GearAt(pos) });
            for (int leg = 0; leg < Legs; leg++)
            {
                grown.Clear();
                foreach (var r in beam)
                    foreach (var v in vels)
                    {
                        var q = r.Q; float risk = r.Risk; bool dead = false; int gq = r.Gear;
                        for (int s2 = 0; s2 < Samples; s2++)
                        {
                            float tt = t + (leg * Samples + s2) * dtS;
                            q += (((leg * Samples + s2) * dtS < slowFor ? v * ms : v) + FloorVel(q, tt, gq)) * dtS;   // a swing or cast still running slows the route to its end
                            if (Dead(q, leg * Samples + s2, ref risk, out gq)) { dead = true; break; }
                        }
                        if (dead) continue;
                        grown.Add(new Node { Q = q, First = leg == 0 ? v : r.First, Risk = risk, Score = risk + (q - goal).magnitude * 1.2f + (v.magnitude > PlayerController.WalkSpeed ? 0.1f : 0), Gear = gq });
                    }
                if (grown.Count == 0) { lastClean = false; if (leg == 0 && Log) Debug.Log($"[gear floor] {t:0.00}s {UltimateState.Roman[HourOf(p) + 1]} cornered at ({pos.x:0.0},{pos.z:0.0}) goal ({goal.x:0.0},{goal.z:0.0}) last {why}"); break; }
                grown.Sort(ByScore);
                if (grown.Count > BeamW) grown.RemoveRange(BeamW, grown.Count - BeamW);
                var sw = beam; beam = grown; grown = sw;
            }
            return beam[0].First;
        }

        // ---------------- presentation (stand-ins) ----------------
        const int CountdownPieces = 20; readonly List<Transform>[] countdown = { new List<Transform>(), new List<Transform>(), new List<Transform>(), new List<Transform>() };
        Transform root, mySpot, pit, floorIn; readonly List<Transform>[] gearParts = { new List<Transform>(), new List<Transform>(), new List<Transform>(), new List<Transform>() }; readonly Transform[] pivots = new Transform[5]; readonly Renderer[] gearR = new Renderer[5], meshR = new Renderer[4];
        readonly List<Transform> pool = new List<Transform>();
        static Material pitM, gearM, centreM, toothM, coreM, pawlM, meshDim, meshWarn, meshBite, hotM, shardM, spotM;
        // Prebuild: the gears are made hidden when combat starts (performance agent, 2 Nov: the P3 -> P4 change took 77-91 ms);
        // the break floor stays a cast-time step (it hides the arena's own floor)
        public override void Prebuild() { MakeObjects(); root.gameObject.SetActive(false); }
        void Build() { MakeObjects(); root.gameObject.SetActive(true); BuildBreakFloor(); }
        void MakeObjects()
        {
            if (root) return;
            if (!pitM)
            {
                pitM = Prim.Material(new Color(0.03f, 0.03f, 0.06f), 0f); gearM = Prim.Material(new Color(0.25f, 0.24f, 0.32f), 0.2f);
                centreM = Prim.Material(new Color(0.32f, 0.26f, 0.16f), 0.2f); toothM = Prim.Material(new Color(0.85f, 0.66f, 0.3f), 0.9f);
                coreM = Prim.Material(new Color(1f, 0.82f, 0.4f), 2.2f); pawlM = Prim.Material(new Color(1f, 0.3f, 0.22f), 1.8f);
                meshDim = Prim.Material(new Color(0.5f, 0.4f, 0.2f), 0.3f); meshWarn = Prim.Material(new Color(1f, 0.55f, 0.35f), 1.6f);
                meshBite = Prim.Material(new Color(1f, 0.2f, 0.15f), 2.5f); hotM = Prim.Material(new Color(1f, 0.35f, 0.25f), 1.4f);
                shardM = Prim.Material(new Color(1f, 0.8f, 0.4f), 2.2f); spotM = Prim.Material(new Color(0.3f, 1f, 0.85f), 2.5f);
            }
            root = new GameObject("The Gear Floor").transform; root.SetParent(W.Root, false);
            pit = Prim.Make(PrimitiveKind.Cylinder, root, new Vector3(0, 0.02f, 0), new Vector3(23.6f, 0.01f, 23.6f), Color.white); Mat(pit, pitM);
            floorIn = Prim.Make(PrimitiveKind.Cylinder, root, new Vector3(0, 0.03f, 0), new Vector3(18f, 0.01f, 18f), Color.white); Mat(floorIn, centreM); floorIn.gameObject.SetActive(false);
            for (int g = 0; g < 5; g++)
            {
                var c = GearCentre(g); float r = GearR(g);
                var disc = Prim.Make(PrimitiveKind.Cylinder, root, c + Vector3.up * 0.05f, new Vector3(r * 2, 0.02f, r * 2), Color.white); Mat(disc, g == 4 ? centreM : gearM); gearR[g] = disc.GetComponent<Renderer>();
                var pv = new GameObject("gear " + GN[g]).transform; pv.SetParent(root, false); pv.position = c; pivots[g] = pv;
                if (g < 4) { gearParts[g].Add(disc); gearParts[g].Add(pv); }
                if (Model(g == 4 ? "GF_CentreGear" : "GF_OuterGear", pv, c + Vector3.up * ModelLift, Quaternion.identity) != null)
                {
                    models = true; disc.position = c + Vector3.up * (ModelLift + 0.05f);   // the hammered-gear coral sits on the model's face
                    if (g < 4)
                    {
                        gearParts[g].Add(Model("GF_Core", root, c + Vector3.up * ModelLift, Quaternion.identity));
                        var (pa, pb) = Pawl(g); gearParts[g].Add(Model("GF_Pawl", root, pa + Vector3.up * ModelLift, Quaternion.FromToRotation(Vector3.left, (pb - pa).normalized)));
                        var mm = Prim.Make(PrimitiveKind.Cylinder, root, MeshPoint(g) + Vector3.up * 0.13f, new Vector3((BiteR + PR) * 2, 0.02f, (BiteR + PR) * 2), Color.white); Mat(mm, meshDim); meshR[g] = mm.GetComponent<Renderer>();
                        for (int j = 0; j < CountdownPieces; j++) { var cd = Prim.Make(PrimitiveKind.Cube, root, Vector3.zero, new Vector3(0.28f, 0.06f, 0.16f), Color.white); Mat(cd, meshWarn); countdown[g].Add(cd); }
                    }
                    continue;
                }
                int teeth = g == 4 ? 12 : 18;
                for (int j = 0; j < teeth; j++) { float a = 360f / teeth * j; Mat(Prim.Make(PrimitiveKind.Cube, pv, Dir(a) * (r - 0.15f) + Vector3.up * 0.09f, new Vector3(0.45f, 0.08f, 0.3f), Color.white, 0, Quaternion.Euler(0, a, 0)), toothM); }
                for (int j = 0; j < 4; j++) { float a = 90f * j; Mat(Prim.Make(PrimitiveKind.Cube, pv, Dir(a) * (r * 0.5f) + Vector3.up * 0.08f, new Vector3(0.18f, 0.04f, r * 0.9f), Color.white, 0, Quaternion.Euler(0, a, 0)), toothM); }
                if (g < 4)
                {
                    var core = Prim.Make(PrimitiveKind.Cylinder, root, c + Vector3.up * 0.6f, new Vector3(HubR * 2, 0.6f, HubR * 2), Color.white); Mat(core, coreM); gearParts[g].Add(core);
                    var (a2, b2) = Pawl(g); var mid = (a2 + b2) * 0.5f;
                    var pawl = Prim.Make(PrimitiveKind.Cube, root, mid + Vector3.up * 0.25f, new Vector3(0.35f, 0.5f, (b2 - a2).magnitude), Color.white, 0, Quaternion.LookRotation(b2 - a2)); Mat(pawl, pawlM); gearParts[g].Add(pawl);
                    var m = Prim.Make(PrimitiveKind.Cylinder, root, MeshPoint(g) + Vector3.up * 0.1f, new Vector3((BiteR + PR) * 2, 0.02f, (BiteR + PR) * 2), Color.white); Mat(m, meshDim); meshR[g] = m.GetComponent<Renderer>();   // the true kill zone (a body overlapping the bite)
                    for (int j = 0; j < CountdownPieces; j++) { var cd = Prim.Make(PrimitiveKind.Cube, root, Vector3.zero, new Vector3(0.28f, 0.06f, 0.16f), Color.white); Mat(cd, meshWarn); countdown[g].Add(cd); }
                }
            }
            mySpot = new GameObject("my spot").transform; mySpot.SetParent(root, false);
            Mat(UltMarkMesh.Mark(PrimitiveKind.Cylinder, UltMarkMesh.Beacon, mySpot, new Vector3(0, 1.6f, 0), new Vector3(0.12f, 1.6f, 0.12f)), spotM);
            for (int i = 0; i < 2; i++) { hammers[i] = Model("GF_Hammer", root, Vector3.zero, Quaternion.identity); if (hammers[i]) hammers[i].gameObject.SetActive(false); }
            for (int g = 0; g < 4; g++) { partBase[g].Clear(); foreach (var x in gearParts[g]) partBase[g].Add(x ? (x.position, x.rotation) : (Vector3.zero, Quaternion.identity)); }
            bell = Model("GB_GreatBell", root, Vector3.up * 60, Quaternion.identity); if (bell) bell.gameObject.SetActive(false);
            // the Floor Breaks' stones, made hidden here (performance agent, 2 Nov: loading and remapping them at the P3 -> P4
            // change took 14 ms): they replace the arena's own floor (Area 4's FloorDisc) and wear its material with its planar
            // mapping, so nothing changes until they crack; when they fall you see the arena's void beneath
            // (Chzy18 26 Oct: "fall away and match the background of the arena")
            stones = models ? Model("FB_Floor", root, Vector3.zero, Quaternion.identity) : null;
            if (stones)
            {
                stones.gameObject.SetActive(false);
                var floor = ArenaVoid.Prepare(W.Root);   // also builds the open-topped spire now
                foreach (var mf in stones.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!floor) break;
                    var mr = mf.GetComponent<Renderer>(); mr.sharedMaterial = floor.sharedMaterial; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    var mesh = mf.mesh; var vs = mesh.vertices; var uv = new Vector2[vs.Length];
                    for (int i = 0; i < vs.Length; i++) { var l = floor.transform.InverseTransformPoint(mf.transform.TransformPoint(vs[i])); uv[i] = new Vector2(-l.x / (2 * ArenaFloorH) + 0.5f, -l.z / (2 * ArenaFloorH) + 0.5f); }
                    mesh.uv = uv;
                }
            }
        }
        Transform stones;
        void BuildBreakFloor()
        {
            tiles.Clear();
            if (breakMode && models)
            {
                var fl = stones;
                if (fl)
                {
                    fl.gameObject.SetActive(true);
                    ArenaVoid.Open(W.Root); arenaFloor = ArenaVoid.Floor;   // the arena floor hidden, its void opened beneath
                    foreach (var r in fl.GetComponentsInChildren<Renderer>())
                    {
                        var c = Sim.Flat(r.transform.position); bool onGear = false; for (int g = 0; g < 5; g++) if ((c - GearCentre(g)).magnitude < GearR(g) - 0.4f) onGear = true;
                        float wave = !onGear && c.magnitude > 9 ? -9f : -5f;   // the two waves: the outer floor first, then everything left
                        tiles.Add(new Tile { T = r.transform, R = r, Mat = r.sharedMaterial, Base = r.transform.position, Rot = r.transform.rotation, At = wave + Random.Range(0f, 0.45f), Spin = Random.onUnitSphere * Random.Range(60f, 160f) });
                    }
                }
            }
        }
        bool models; readonly Transform[] hammers = new Transform[2];
        sealed class Tile { public Transform T; public Renderer R; public Material Mat; public Vector3 Base, Spin; public Quaternion Rot; public float At; public bool Loose; }
        readonly List<Tile> tiles = new List<Tile>();
        readonly List<(Vector3 p, Quaternion r)>[] partBase = { new List<(Vector3, Quaternion)>(), new List<(Vector3, Quaternion)>(), new List<(Vector3, Quaternion)>(), new List<(Vector3, Quaternion)>() };
        Transform bell; Renderer arenaFloor; MaterialPropertyBlock tileGlow;
        const float ArenaFloorH = 13.6f;   // Area 4 builder's FloorH: the floor texture's planar half-size
        public const float BellFallAt = 12.2f;   // s into the wild: the great bell drops, landing as the wild ends (WildLen)
        const float ModelLift = 0.07f;   // gear tops at 0.1 m, clear of the pit disc (0.02) and the arena floor
        internal static Transform Model(string name, Transform parent, Vector3 pos, Quaternion rot)   // also FinalToll
        {
            var prefab = Resources.Load<GameObject>("Ultimate/" + name); if (prefab == null) return null;
            var t = Object.Instantiate(prefab, parent, false).transform; t.name = name; t.position = pos; t.rotation = rot;
            foreach (var r in t.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return t;
        }
        // the bell-hammer over a gear: hovers high when the slam is shown, sinks, drops in the last 0.25 s, rests, lifts away
        void PlaceHammer(int i, int gear, float left)
        {
            var hm = hammers[i]; if (!hm) return;
            if (gear < 0 || left > Warn + WarnLead + 0.01f || left < -0.6f) { hm.gameObject.SetActive(false); return; }
            hm.gameObject.SetActive(true);
            float h = left > 0.25f ? Mathf.Lerp(4.0f, 9.0f, (left - 0.25f) / (Warn + WarnLead)) : left > 0 ? 4.0f * (left / 0.25f) : Mathf.Lerp(0, 6f, -left / 0.6f);
            hm.position = GearCentre(gear) + Vector3.up * h;
        }
        static void Mat(Transform t, Material m) { var r = t.GetComponent<Renderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
        void Present(float t)
        {
            if (!root) return;
            for (int g = 0; g < 5; g++) if (pivots[g]) pivots[g].rotation = Quaternion.Euler(0, -spin[g], 0);
            if (breakMode && t < 0)
            {
                // the floor breaks in two waves: the cracks flash first
                bool flash = ((t > -11 && t < -9) || (t > -7 && t < -5)) && Mathf.FloorToInt(t * 6) % 2 == 0;
                if (pit) pit.gameObject.SetActive(t >= -9); if (floorIn) floorIn.gameObject.SetActive(t >= -9 && t < -5);
                for (int g = 0; g < 4; g++) if (gearR[g]) gearR[g].sharedMaterial = flash && !models ? hotM : gearM;
            }
            else { if (pit) pit.gameObject.SetActive(t >= settle || Mathf.FloorToInt(t * 6) % 2 == 0 && t > settle - 1.5f); if (floorIn) floorIn.gameObject.SetActive(false); }   // the floor flickers out, then the pit
            if (tiles.Count > 0)
            {
                if (pit) pit.gameObject.SetActive(!arenaFloor); if (floorIn) floorIn.gameObject.SetActive(false);
                // the cracks glow: in its last 2 s each stone's own glowing lines (the floor's fissure and engraving, its emission
                // map) burn brighter in an uneven pulse (the floor material is see-through, so nothing can glow from under it)
                if (tileGlow == null) tileGlow = new MaterialPropertyBlock();
                bool flick = Mathf.FloorToInt(t * 8) % 2 == 0;
                foreach (var ti in tiles)
                {
                    if (!ti.T) continue; float tf = t - ti.At;
                    if (tf < 0)
                    {
                        ti.T.gameObject.SetActive(true); ti.R.sharedMaterial = ti.Mat; ti.Loose = false;
                        float sh = tf > -2f ? (2f + tf) / 2f : 0;   // shudders harder as its fall nears
                        ti.T.position = ti.Base + new Vector3(Mathf.PerlinNoise(t * 23f, ti.At) - 0.5f, 0, Mathf.PerlinNoise(ti.At, t * 23f) - 0.5f) * 0.09f * sh + Vector3.down * 0.05f * sh;
                        ti.T.rotation = Quaternion.Euler((Mathf.PerlinNoise(t * 17f, ti.At * 3) - 0.5f) * 5f * sh, 0, (Mathf.PerlinNoise(ti.At * 3, t * 17f) - 0.5f) * 5f * sh) * ti.Rot;
                        if (sh > 0 && ti.Mat && ti.Mat.HasProperty("_EmissionColor")) { tileGlow.SetColor("_EmissionColor", ti.Mat.GetColor("_EmissionColor") * (1f + 3.5f * sh * (0.6f + 0.4f * Mathf.PerlinNoise(t * 9f, ti.At * 5)))); ti.R.SetPropertyBlock(tileGlow); }
                        else ti.R.SetPropertyBlock(null);
                        continue;
                    }
                    if (!ti.Loose) { ti.Loose = true; W.BossFx.UltStoneDrop(ti.Base); }   // it breaks loose: dust and sparks from the crack
                    if (tf > 1.1f) { ti.T.gameObject.SetActive(false); continue; }
                    ti.R.sharedMaterial = ti.Mat;
                    ti.T.position = ti.Base + Vector3.down * (0.4f * tf + 7f * tf * tf); ti.T.rotation = Quaternion.Euler(ti.Spin * tf) * ti.Rot;
                    ti.T.localScale = Vector3.one * Mathf.Lerp(1f, 0.55f, tf / 1.1f);
                }
            }
            for (int g = 0; g < 4; g++)
            {
                bool flashCore = wild && !launched[g] && t > LaunchAt(g) - 2 && Mathf.FloorToInt(t * 6) % 2 == 0;
                float fly = wild && launched[g] ? t - LaunchAt(g) : -1;   // tearing loose: thrown outward, tumbling, for 1.4 s
                for (int j = 0; j < gearParts[g].Count; j++)
                {
                    var x = gearParts[g][j]; if (!x) continue;
                    bool show = (!launched[g] || fly < 1.4f) && !(flashCore && j == 2); x.gameObject.SetActive(show);
                    if (fly >= 0 && fly < 1.4f && j < partBase[g].Count)
                    {
                        var (bp, br) = partBase[g][j]; var outw = Dir(90f * g);
                        x.position = bp + outw * (5f * fly + 6f * fly * fly) + Vector3.up * (3f * fly - 2.5f * fly * fly);
                        x.rotation = Quaternion.AngleAxis(140f * fly, Vector3.Cross(Vector3.up, outw)) * br;
                    }
                }
            }
            if (wild && bell)
            {
                float tw = t - wildAt, fb = (tw - BellFallAt) / (WildLen - BellFallAt);
                bell.gameObject.SetActive(fb > 0);
                if (fb > 0) bell.position = Vector3.up * (60f * (1 - Mathf.Clamp01(fb) * Mathf.Clamp01(fb)));   // it falls, faster and faster
                RunDirector.CameraPull = Mathf.Clamp01((tw - (BellFallAt - 0.6f)) / 0.8f);
            }
            int k = Mathf.FloorToInt(t / Slam) + 1; var ss = !wild && k >= 1 && Revealed(k, t) ? SlamGears(k) : new List<int>();
            if (wild) { float tw = t - wildAt; int ws = Mathf.FloorToInt(tw / 2.5f) + 1; if (ws <= 3 && 2.5f * ws - tw < 1.5f) ss.Add(WildSlamGear(ws)); }
            if (!(breakMode && t < 0)) for (int g = 0; g < 4; g++) if (gearR[g]) gearR[g].sharedMaterial = ss.Contains(g) ? hotM : gearM;
            for (int g = 0; g < 4; g++)
            {
                float ph = ((t + g * BiteEvery / 4) % BiteEvery + BiteEvery) % BiteEvery, soon = BiteEvery - ph;
                bool biteOn = t >= settle && !wild;
                if (meshR[g]) { meshR[g].sharedMaterial = !biteOn ? meshDim : ph < BiteLen ? meshBite : soon < 1.0f + WarnLead ? meshWarn : meshDim; if (models) meshR[g].enabled = biteOn && (ph < BiteLen || soon < 1.0f + WarnLead); }
                float kr = BiteR + PR, cr = kr + 2.2f * Mathf.Clamp01((soon - 0.0f) / (BiteEvery - BiteLen));   // closes on the zone as the bite comes
                for (int j = 0; j < countdown[g].Count; j++)
                {
                    var x = countdown[g][j]; bool show = biteOn && !launched[g] && ph >= BiteLen; x.gameObject.SetActive(show); if (!show) continue;
                    float a = 360f / CountdownPieces * j; var d = Dir(a); x.position = MeshPoint(g) + d * cr + Vector3.up * 0.14f; x.rotation = Quaternion.LookRotation(d);
                    x.GetComponent<Renderer>().sharedMaterial = soon < 1.0f + WarnLead ? meshWarn : meshDim;
                }
            }
            if (models) for (int g = 0; g < 5; g++) if (gearR[g]) gearR[g].enabled = gearR[g].sharedMaterial == hotM;
            {
                int hk = Mathf.FloorToInt(t / Slam) + 1; List<int> hs = !wild && hk >= 1 ? SlamGears(hk) : new List<int>(); float hl = hk * Slam - t;
                int last = Mathf.FloorToInt(t / Slam); if (!wild && last >= 1 && t - last * Slam < 0.6f) { hs = SlamGears(last); hl = last * Slam - t; }
                if (wild) { float tw = t - wildAt; int ws = Mathf.FloorToInt(tw / 2.5f) + 1; hs = new List<int>(); if (ws <= 3) { hs.Add(WildSlamGear(ws)); hl = Mathf.Min(1.5f, 2.5f * ws - tw); if (2.5f * ws - tw > 1.5f) hs.Clear(); } }
                for (int i = 0; i < 2; i++) PlaceHammer(i, i < hs.Count ? hs[i] : -1, hl);
            }
            int n = 0;
            foreach (var s in shards)
            {
                if (pool.Count <= n) { var tr = Model("GF_Shard", root, Vector3.zero, Quaternion.identity); if (!tr) { tr = UltBulletMesh.Make(root, shardM); tr.localScale = Vector3.one * BR * 2.2f; } pool.Add(tr); }   // without the shard model: a small bell
                var b = pool[n++]; b.gameObject.SetActive(true); b.position = new Vector3(s.P.x, 0.9f, s.P.z);
                if (s.V.sqrMagnitude > 1e-4f) b.rotation = Quaternion.FromToRotation(Vector3.left, s.V.normalized);
            }
            for (int i = n; i < pool.Count; i++) pool[i].gameObject.SetActive(false);
            var me = W.Players.Count > 0 ? W.Players[0] : null;
            if (mySpot) { bool show = EscapementRelay.ShowMySpot && me && !me.Down; mySpot.gameObject.SetActive(show); if (show) { var gl = GoalOf(me, t, out _, out _); mySpot.position = new Vector3(gl.x, 0, gl.z); } }
        }
        void Teardown()
        {
            using var mark = TeardownMarker.Auto();
            if (root) Object.Destroy(root.gameObject);
            ArenaVoid.Close(); arenaFloor = null;   // the arena gets its floor back (P5 opens the void again under the bell)
            root = mySpot = pit = floorIn = null; hammers[0] = hammers[1] = null; models = false; bell = null; tiles.Clear(); pool.Clear(); plans.Clear(); foreach (var l in gearParts) l.Clear(); foreach (var l in countdown) l.Clear();
            for (int i = 0; i < 5; i++) { pivots[i] = null; gearR[i] = null; } for (int i = 0; i < 4; i++) meshR[i] = null;
        }

        static GUIStyle nameStyle, bigStyle, midStyle;
        public void DrawGui(Camera cam)
        {
            if (!Running || cam == null || !root) return;
            if (nameStyle == null)
            {
                nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                midStyle = new GUIStyle(nameStyle) { fontSize = 24 }; bigStyle = new GUIStyle(nameStyle) { fontSize = 34 };
            }
            float t = T; int k = Mathf.Max(1, Mathf.FloorToInt(t / Slam) + 1); float left = k * Slam - t;
            for (int g = 0; g < 4; g++) if (!launched[g]) Label(cam, GearCentre(g) + Dir(90f * g + 90) * 3.0f, 0.3f, GN[g], new Color(0.97f, 0.95f, 0.88f), midStyle);
            for (int g = 0; g < 4; g++) if (!launched[g]) Label(cam, GearCentre(g), 1.5f, "CORE", new Color(1f, 0.85f, 0.45f), nameStyle);
            for (int i = 0; i < W.Players.Count; i++)
            {
                var p = W.Players[i]; if (p.Down) continue;
                int tg = TargetOf(p, k, t);
                Label(cam, p.transform.position, 2.9f, (i == 0 ? "YOU " : "") + UltimateState.Roman[HourOf(p) + 1] + "  → " + GN[tg], tg == 4 ? new Color(1f, 0.82f, 0.48f) : Color.white, nameStyle);
            }
            if (breakMode && t < 0)
            {
                string bt = t < -9 ? $"the floor cracks · the outer floor falls in {-9 - t:0.0}s" : t < -5 ? $"everything but the gears falls in {-5 - t:0.0}s" : t < -3 ? $"the gears lock in {-3 - t:0.0}s: be on YOUR gear" : $"the gears turn in {-t:0.0}s";
                bigStyle.normal.textColor = t < -5 ? new Color(1f, 0.85f, 0.55f) : new Color(1f, 0.42f, 0.3f);
                GUI.Label(new Rect(0, Screen.height * 0.17f, Screen.width, 50), "THE FLOOR BREAKS · " + bt, bigStyle); return;
            }
            if (wild)
            {
                float tw = t - wildAt; string wt = tw < 9 ? $"the gears run wild x{SpeedK(t):0.0} · the hammer every 2.5 s" : tw < WildLen ? "the gears tear loose: get to the CENTRE" : "";
                bigStyle.normal.textColor = new Color(1f, 0.42f, 0.3f);
                GUI.Label(new Rect(0, Screen.height * 0.17f, Screen.width, 50), "THE TRAIN RUNS WILD · " + wt, bigStyle); return;
            }
            var ss = SlamGears(k);
            string text = Revealed(k, t) ? $"HAMMER → {string.Join(" + ", ss.ConvertAll(g => GN[g]))} in {left:0.0}s" : $"hammer in {left:0.0}s · gear unknown" + (k % 3 == 0 ? " · TWO gears" : "");
            bigStyle.normal.textColor = Revealed(k, t) ? new Color(1f, 0.42f, 0.3f) : new Color(1f, 0.85f, 0.7f);
            GUI.Label(new Rect(0, Screen.height * 0.17f, Screen.width, 50), text, bigStyle);
            if (k % Flip == 0 && Revealed(k, t)) { midStyle.normal.textColor = new Color(1f, 0.82f, 0.48f); GUI.Label(new Rect(0, Screen.height * 0.17f + 44, Screen.width, 34), "the gears reverse after this slam", midStyle); }
        }
        public string HudText(PlayerController p)
        {
            if (!Running || p == null) return null;
            float t = T; int k = Mathf.FloorToInt(t / Slam) + 1;
            if (breakMode && t < 0) return $"the floor breaks · get onto gear {GN[(HourOf(p) + 2) % 4]} and dodge the spiral";
            if (wild) return "the train runs wild · ride your gear, then get to the CENTRE before it tears loose";
            if (t < settle) return $"the floor falls in {settle - t:0.0}s · get onto gear {GN[TargetOf(p, k, t)]}";
            return $"slam {k} in {k * Slam - t:0.0}s · you ({UltimateState.Roman[HourOf(p) + 1]}): gear {GN[TargetOf(p, k, t)]} · hit the cores to hurt the boss";
        }

        public override void Cancel() { base.Cancel(); shards.Clear(); Teardown(); stop = false; RunDirector.CameraPull = 0; }
        public void ResetCounts() { Jams = 0; Fails.Clear(); }
    }
}
