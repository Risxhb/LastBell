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
    // Melee parks against its core, where the floor barely moves. 5-8 players: two per gear. Below 4: only gears with a
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
        public static Vector3 GearCentre(int g) => g == 4 ? Vector3.zero : Dir(90f * g) * O_D;
        static float GearR(int g) => g == 4 ? C_R : O_R;
        public static Vector3 Core(int g) => GearCentre(g);
        static Vector3 MeshPoint(int g) => Dir(90f * g) * C_R;
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
        Vector3 FloorVel(Vector3 q, float t)
        {
            int g = GearAt(q); if (g < 0) return Vector3.zero;
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
        bool Revealed(int k, float t) => t >= k * Slam - Warn;
        int HourOf(PlayerController p) { int i = W.Players.IndexOf(p); return i < 0 ? -1 : i % 4; }
        int TargetOf(PlayerController p, int k, float t)
        {
            int g = ((HourOf(p) + 2 * k) % 4 + 4) % 4;
            return Revealed(k, t) && SlamGears(k).Contains(g) ? 4 : g;
        }
        static (Vector3 a, Vector3 b) Pawl(int g) { var u = Dir(90f * g); return (GearCentre(g) + u * PawlIn, GearCentre(g) + u * O_R); }
        static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a; float t = Mathf.Clamp01(Vector3.Dot(Sim.Flat(p) - a, ab) / ab.sqrMagnitude);
            return (Sim.Flat(p) - (a + ab * t)).magnitude;
        }
        bool BiteWithin(int g, float t, float dur) { for (float s = 0; s <= dur + 1e-4f; s += 0.05f) if (Biting(g, t + s)) return true; return Biting(g, t + dur); }
        bool Biting(int g, float t) { float ph = ((t + g * BiteEvery / 4) % BiteEvery + BiteEvery) % BiteEvery; return ph < BiteLen; }
        // what kills a grounded player at q at time t ("" = nothing)
        string Deadly(Vector3 q, float t)
        {
            if (GearAt(q) < 0) return FloorGone(q, t) ? "the pit" : "";   // the floor is still whole at first (Settle, or the break's waves)
            for (int i = 0; i < 4; i++)
            {
                if (launched[i]) continue;
                var (a, b) = Pawl(i); if (SegDist(q, a, b) < PR + 0.15f) return "a pawl";
                if (t >= settle && !wild && Biting(i, t) && (Sim.Flat(q) - MeshPoint(i)).magnitude < BiteR + PR) return "the teeth";
            }
            return "";
        }
        static bool Grounded(PlayerController p) => p.Height < 0.05f && !p.Dashing;

        // ---------------- cast ----------------
        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name);
            stop = false; goWild = false; wild = false; locked = false; wildSlams = 0; for (int g = 0; g < 4; g++) launched[g] = false;
            breakMode = Cue.Variant == "break"; settle = breakMode ? 0 : Settle; nextArm = -BreakLen + 0.5f;
            t0 = Now + (breakMode ? Sim.Ticks(BreakLen) : 0); slams = 0; seed = Cue.Seed % 997; shards.Clear(); plans.Clear();
            for (int i = 0; i < 5; i++) spin[i] = 0;
            Build();
            var telegraphs = new List<Hazard>();
            while (true)
            {
                float t = T, nt = t + Sim.Dt;
                for (int g = 0; g < 5; g++) spin[g] += Omega(g, t) * Sim.Dt * Mathf.Rad2Deg;
                // the floor carries everyone standing on it
                foreach (var p in W.Players)
                {
                    if (p.Down || !Grounded(p)) continue;
                    var v = FloorVel(p.transform.position, t);
                    if (v.sqrMagnitude > 0) p.transform.position = W.Constrain(p.transform.position + v * Sim.Dt, PlayerController.Radius);
                }
                foreach (var p in W.Players) { if (p.Down || !Grounded(p)) continue; var why = Deadly(p.transform.position, nt); if (why != "") Hit(p, UltimateState.Lethal, why); }
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
                    if (t < -5 && nt >= -5) { Boss.PlanLog.Add((Now, "the floor breaks: wave 2")); CameraShake.Shake(0.2f, 0.4f); }
                    if (!locked && nt >= -3)
                    {
                        locked = true; Boss.PlanLog.Add((Now, "the gears lock"));
                        for (int g = 0; g < 4; g++)
                        {
                            bool assigned = false, held = false;
                            foreach (var p in W.Players) { if (p.Down || (HourOf(p) + 2) % 4 != g) continue; assigned = true; if (GearAt(p.transform.position) == g) held = true; }
                            if (assigned && !held) { Jams++; Fails.Add($"the lock: gear {GN[g]} empty"); Ult.Wipe($"The gear train jams (gear {GN[g]} empty)"); break; }
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
                            foreach (var p in W.Players) if (!p.Down && Grounded(p) && GearAt(p.transform.position) == sg) Hit(p, UltimateState.Lethal, "the hammer");
                            for (int j = 0; j < 12; j++) shards.Add(new Shard { P = GearCentre(sg), V = Dir(30 * j + 7 * ws) * ShardSpeed });
                            W.BossFx.TollResolve(GearCentre(sg), 0.5f, O_R); CameraShake.Shake(0.15f, 0.25f);
                        }
                    }
                    if (wildTele == null && ws < 3 && tw >= 2.5f * (ws + 1) - 1.5f)
                    {
                        int sg = WildSlamGear(ws + 1); float left2 = 2.5f * (ws + 1) - tw;
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
                    if (tw >= WildLen) { Boss.PlanLog.Add((Now, "the great bell falls")); W.BossFx.TollResolve(Vector3.zero, 1f, 11.5f); CameraShake.Shake(0.4f, 0.7f); break; }
                }
                // the hammer: telegraph, then the slam
                int k = Mathf.FloorToInt(nt / Slam) + 1;
                if (!wild && k >= 1 && telegraphs.Count == 0 && Revealed(k, nt))
                    foreach (var g in SlamGears(k))
                    {
                        int left = Mathf.Max(1, Sim.Ticks(k * Slam - t));
                        var h = Spawn(new Hazard { Source = Source + ": the hammer", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = GearCentre(g), Radius = O_R, LockAt = 0, ResolveAt = UltShapes.ResolveIn(left), Damage = 0, Knockback = 0, Stun = 0, HitOnResolve = false });
                        h.OnResolve = hz => { W.BossFx.TollResolve(hz.Origin, 0.5f, O_R); CameraShake.Shake(0.15f, 0.25f); };
                        telegraphs.Add(h);
                    }
                int ks = Mathf.FloorToInt(nt / Slam);
                if (!wild && ks >= 1 && ks > slams)
                {
                    slams = ks; telegraphs.Clear();
                    var ss = SlamGears(ks);
                    foreach (var p in W.Players) if (!p.Down && Grounded(p) && ss.Contains(GearAt(p.transform.position))) Hit(p, UltimateState.Lethal, "the hammer");
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
                        if (assigned && !held) { Jams++; Fails.Add($"slam {ks}: gear {GN[g]} without {who}"); Ult.Wipe($"The gear train jams (gear {GN[g]} needs {who})"); break; }
                    }
                    Boss.PlanLog.Add((Now, $"gear slam {ks} {string.Join("+", ss.ConvertAll(g => GN[g]))}"));
                    if (stop) break;
                    if (goWild) { wild = true; goWild = false; wildAt = nt; Boss.PlanLog.Add((Now, "the train runs wild")); }
                }
                if (Log && (Now - t0) % 30 == 0) foreach (var p in W.Players) if (!p.Down) Debug.Log($"[gear floor] {t:0.0}s {UltimateState.Roman[HourOf(p) + 1]} at ({p.transform.position.x:0.0},{p.transform.position.z:0.0}) h{p.Height:0.00}{(p.Dashing ? " dash" : "")} gear {GearAt(p.transform.position)} target {TargetOf(p, k, t)} job {JobOf(p)}");
                if (Boss.Ult.WipeCause != null) break;
                Present(nt);
                yield return 1;
            }
            shards.Clear(); wildTele = null;
            Teardown();
        }
        Hazard wildTele;
        int WildSlamGear(int j) { for (int n = 0; n < 4; n++) { int g = SlamGear(100 + j + 7 * n); if (!launched[g]) return g; } return SlamGear(100 + j); }

        void Hit(PlayerController p, int dmg, string what)
        {
            Fails.Add($"{T:0.0}s {UltimateState.Roman[HourOf(p) + 1]}: {what}");
            p.ReceiveHit(new Hazard { Source = Source + ": " + what, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = Sim.Flat(p.transform.position), Radius = 0.5f, Damage = dmg, Knockback = 0, Stun = 0 });
        }

        // ---------------- bots ----------------
        sealed class Plan { public Vector3 V; public int Until; public string Job; public Vector3 Goal; }
        readonly Dictionary<PlayerController, Plan> plans = new Dictionary<PlayerController, Plan>();
        public static bool Log;   // tests: bot positions every 0.5 s, and every time a bot finds no safe route
        public string JobOf(PlayerController p) => plans.TryGetValue(p, out var pl) ? pl.Job : null;
        static bool Melee(PlayerController p) => p.ClassName == "Vanguard";

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
                if (cur >= 0 && cur < 4 && !hammered && tw < 6.5f && tw < LaunchAt(cur) - wildAt - 3) tg = cur; else tg = 4;
                job = tg == 4 ? "→ CENTRE" : "ride " + GN[tg];
            }
            Vector3 Park(int g) => g == 4
                ? Dir(45 + 90 * (i % 4) + spin[4] + (i >= 4 ? 30 : 0)) * 2.3f
                : GearCentre(g) + Dir(90f * g + 180 + (SpinSign(t) > 0 ? 35 : -35) + (i >= 4 ? 70 : 0)) * (Melee(p) ? HubR + PR + 0.5f : 2.6f);
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

        public override bool Steer(PlayerController p, UltimateBot.Skill skill, out Vector3 vel)
        {
            vel = Vector3.zero;
            if (!Running || p.Down) return false;
            if (plans.TryGetValue(p, out var cur) && Now < cur.Until) { vel = cur.V; return true; }
            float t = T; var goal = GoalOf(p, t, out string job, out int k);
            if (skill == UltimateBot.Skill.Miss && k % 4 == 3)   // Miss forgets one hop: stays where it is for that slam
            {
                int g = GearAt(p.transform.position); if (g >= 0 && g < 4) goal = GearCentre(g) + Dir(90f * g + 180) * 2.6f;
            }
            var v = Search(p, Sim.Flat(p.transform.position), t, goal, k);
            plans[p] = new Plan { V = v, Until = Now + (skill == UltimateBot.Skill.Late ? 15 : 3), Job = job, Goal = goal };
            vel = v; return true;
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
            if (clearSteps < 10 || (!wild && GearAt(q) != TargetOf(p, k, t)) || (plans.TryGetValue(p, out var pl) && (q - Sim.Flat(pl.Goal)).magnitude > 1.0f)) { reach = 99; fullKit = false; }
            return true;
        }

        readonly List<Vector3>[] pred = new List<Vector3>[12];
        Vector3 Search(PlayerController p, Vector3 pos, float t, Vector3 goal, int k)
        {
            const float Leg = 0.2f; const int Legs = 3, Samples = 4, BeamW = 10; float dtS = Leg / Samples; int N = Legs * Samples;
            k = Mathf.Max(1, k); float slamT = wild ? float.MaxValue : k * Slam; var ss = SlamGears(k); bool revealed = !wild && Revealed(k, t + Leg * Legs);
            float tw0 = t - wildAt; int wsN = Mathf.FloorToInt(tw0 / 2.5f) + 1; float wSlamT = wild && wsN <= 3 ? wildAt + 2.5f * wsN : float.MaxValue; int wSlamG = wild && wsN <= 3 ? WildSlamGear(wsN) : -1;
            for (int s = 0; s < N; s++)
            {
                float tq = t + (s + 1) * dtS; var arr = pred[s] ?? (pred[s] = new List<Vector3>()); arr.Clear();
                foreach (var sh in shards) arr.Add(sh.P + sh.V * (tq - t));
                if (tq > slamT) foreach (var g in ss) for (int j = 0; j < Shards; j++) arr.Add(GearCentre(g) + Dir(360f / Shards * j + 11 * k) * ShardSpeed * (tq - slamT));
                if (tq > wSlamT && wSlamG >= 0) for (int j = 0; j < 12; j++) arr.Add(GearCentre(wSlamG) + Dir(30 * j + 7 * wsN) * ShardSpeed * (tq - wSlamT));
                if (wild) for (int g = 0; g < 4; g++) { float la = LaunchAt(g); if (!launched[g] && tq > la) for (int j = -2; j <= 2; j++) arr.Add(GearCentre(g) + Dir(90f * g + 180 + 24 * j) * 5f * (tq - la)); }
            }
            var bossP = Sim.Flat(Boss.transform.position); string why = ""; float ms = p.Kit != null ? p.Kit.MoveScale : 1f;
            bool Dead(Vector3 q, int s, ref float risk)
            {
                float tq = t + (s + 1) * dtS;
                if (Deadly(q, tq) != "") { why = "deadly " + Deadly(q, tq); return true; }
                { float c = (q - bossP).magnitude - Boss.BodyRadius - PR; if (c < -0.05f) { why = "boss"; return true; } if (c < 0.4f) risk += (0.4f - c) * 2; }   // the boss is solid, not deadly
                bool inside = false; for (int g = 0; g < 5; g++) if ((g == 4 || (!launched[g] && !(wild && tq >= LaunchAt(g) - 0.25f))) && (q - GearCentre(g)).magnitude <= GearR(g) - 0.25f) inside = true;
                if (!inside && FloorGone(q, tq + 0.1f)) { why = "off gear"; return true; }
                if (wSlamG >= 0 && tq >= wSlamT - dtS * 0.5f && tq < wSlamT + dtS * 0.5f && GearAt(q) == wSlamG) { why = "slam"; return true; }
                for (int g = 0; g < 4; g++) { if (launched[g]) continue; float c = (q - GearCentre(g)).magnitude - HubR - PR; if (c < -0.1f) { why = "core"; return true; } if (c < 0.3f) risk += (0.3f - c) * 1.5f; }   // the core is solid, not deadly: only a route through it is out
                if (revealed && tq >= slamT - dtS * 0.5f && tq < slamT + dtS * 0.5f && ss.Contains(GearAt(q))) { why = "slam"; return true; }
                float mc = 9; foreach (var b in pred[s]) { float c = (q - b).magnitude - PR - BR; if (c < mc) mc = c; }
                if (mc < 0.12f) { why = "shard"; return true; } if (mc < 0.5f) risk += (0.5f - mc) * 2;
                for (int g = 0; g < 4; g++) { float depth = BiteR + PR + 0.1f - (q - MeshPoint(g)).magnitude; if (tq >= settle && !wild && depth > 0 && BiteWithin(g, tq - 0.08f, depth / 3f + 0.15f)) { why = "teeth"; return true; } }   // inside a mesh only if it can get out before the bite
                int gq = GearAt(q);
                if (gq >= 0 && gq < 4) { float edge = O_R - (q - GearCentre(gq)).magnitude; if (edge < 0.6f && (q - MeshPoint(gq)).magnitude > 1.4f) risk += (0.6f - edge) * 3; }
                for (int g = 0; g < 4; g++) { if (launched[g]) continue; var (a, b) = Pawl(g); float c = SegDist(q, a, b) - PR; if (c < 0.8f) risk += (0.8f - c) * 2; }
                return false;
            }
            var dirs = new List<Vector3>(); for (int j = 0; j < 16; j++) dirs.Add(Dir(22.5f * j));
            var tg = goal - pos; if (tg.magnitude > 1e-3f) dirs.Add(tg.normalized);
            var vels = new List<Vector3> { Vector3.zero };
            foreach (var d in dirs) { vels.Add(d * PlayerController.WalkSpeed); vels.Add(d * PlayerController.SprintSpeed); vels.Add(d * PlayerController.WalkSpeed * 0.4f); }
            var beam = new List<(Vector3 q, Vector3 first, float risk, float score)> { (pos, Vector3.zero, 0f, 0f) };
            for (int leg = 0; leg < Legs; leg++)
            {
                var next = new List<(Vector3 q, Vector3 first, float risk, float score)>();
                foreach (var r in beam)
                    foreach (var v in vels)
                    {
                        var q = r.q; float risk = r.risk; bool dead = false;
                        for (int s2 = 0; s2 < Samples; s2++)
                        {
                            float tt = t + (leg * Samples + s2) * dtS;
                            q += ((leg == 0 ? v * ms : v) + FloorVel(q, tt)) * dtS;   // a cast still running slows the first leg
                            if (Dead(q, leg * Samples + s2, ref risk)) { dead = true; break; }
                        }
                        if (dead) continue;
                        next.Add((q, leg == 0 ? v : r.first, risk, risk + (q - goal).magnitude * 1.2f + (v.magnitude > PlayerController.WalkSpeed ? 0.1f : 0)));
                    }
                if (next.Count == 0) { if (leg == 0 && Log) Debug.Log($"[gear floor] {t:0.00}s {UltimateState.Roman[HourOf(p) + 1]} cornered at ({pos.x:0.0},{pos.z:0.0}) goal ({goal.x:0.0},{goal.z:0.0}) last {why}"); break; }
                next.Sort((x, y) => x.score.CompareTo(y.score));
                beam = next.GetRange(0, Mathf.Min(BeamW, next.Count));
            }
            return beam[0].first;
        }

        // ---------------- presentation (stand-ins) ----------------
        Transform root, mySpot, pit, floorIn; readonly List<Transform>[] gearParts = { new List<Transform>(), new List<Transform>(), new List<Transform>(), new List<Transform>() }; readonly Transform[] pivots = new Transform[5]; readonly Renderer[] gearR = new Renderer[5], meshR = new Renderer[4];
        readonly List<Transform> pool = new List<Transform>();
        static Material pitM, gearM, centreM, toothM, coreM, pawlM, meshDim, meshWarn, meshBite, hotM, shardM, spotM;
        void Build()
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
            root = new GameObject("The Gear Floor (stand-in)").transform; root.SetParent(W.Root, false);
            pit = Prim.Make(PrimitiveKind.Cylinder, root, new Vector3(0, 0.02f, 0), new Vector3(23.6f, 0.01f, 23.6f), Color.white); Mat(pit, pitM);
            floorIn = Prim.Make(PrimitiveKind.Cylinder, root, new Vector3(0, 0.03f, 0), new Vector3(18f, 0.01f, 18f), Color.white); Mat(floorIn, centreM); floorIn.gameObject.SetActive(false);
            for (int g = 0; g < 5; g++)
            {
                var c = GearCentre(g); float r = GearR(g);
                var disc = Prim.Make(PrimitiveKind.Cylinder, root, c + Vector3.up * 0.05f, new Vector3(r * 2, 0.02f, r * 2), Color.white); Mat(disc, g == 4 ? centreM : gearM); gearR[g] = disc.GetComponent<Renderer>();
                var pv = new GameObject("gear " + GN[g]).transform; pv.SetParent(root, false); pv.position = c; pivots[g] = pv;
                if (g < 4) { gearParts[g].Add(disc); gearParts[g].Add(pv); }
                int teeth = g == 4 ? 12 : 18;
                for (int j = 0; j < teeth; j++) { float a = 360f / teeth * j; Mat(Prim.Make(PrimitiveKind.Cube, pv, Dir(a) * (r - 0.15f) + Vector3.up * 0.09f, new Vector3(0.45f, 0.08f, 0.3f), Color.white, 0, Quaternion.Euler(0, a, 0)), toothM); }
                for (int j = 0; j < 4; j++) { float a = 90f * j; Mat(Prim.Make(PrimitiveKind.Cube, pv, Dir(a) * (r * 0.5f) + Vector3.up * 0.08f, new Vector3(0.18f, 0.04f, r * 0.9f), Color.white, 0, Quaternion.Euler(0, a, 0)), toothM); }
                if (g < 4)
                {
                    var core = Prim.Make(PrimitiveKind.Cylinder, root, c + Vector3.up * 0.6f, new Vector3(HubR * 2, 0.6f, HubR * 2), Color.white); Mat(core, coreM); gearParts[g].Add(core);
                    var (a2, b2) = Pawl(g); var mid = (a2 + b2) * 0.5f;
                    var pawl = Prim.Make(PrimitiveKind.Cube, root, mid + Vector3.up * 0.25f, new Vector3(0.35f, 0.5f, (b2 - a2).magnitude), Color.white, 0, Quaternion.LookRotation(b2 - a2)); Mat(pawl, pawlM); gearParts[g].Add(pawl);
                    var m = Prim.Make(PrimitiveKind.Cylinder, root, MeshPoint(g) + Vector3.up * 0.1f, new Vector3(BiteR * 2, 0.02f, BiteR * 2), Color.white); Mat(m, meshDim); meshR[g] = m.GetComponent<Renderer>();
                }
            }
            mySpot = new GameObject("my spot").transform; mySpot.SetParent(root, false);
            Mat(Prim.Make(PrimitiveKind.Cylinder, mySpot, new Vector3(0, 1.6f, 0), new Vector3(0.12f, 1.6f, 0.12f), Color.white), spotM);
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
                for (int g = 0; g < 4; g++) if (gearR[g]) gearR[g].sharedMaterial = flash ? hotM : gearM;
            }
            else { if (pit) pit.gameObject.SetActive(t >= settle || Mathf.FloorToInt(t * 6) % 2 == 0 && t > settle - 1.5f); if (floorIn) floorIn.gameObject.SetActive(false); }   // the floor flickers out, then the pit
            for (int g = 0; g < 4; g++) { bool flashCore = wild && !launched[g] && t > LaunchAt(g) - 2 && Mathf.FloorToInt(t * 6) % 2 == 0; foreach (var x in gearParts[g]) if (x) x.gameObject.SetActive(!launched[g] && !(flashCore && x == gearParts[g][2])); }
            int k = Mathf.FloorToInt(t / Slam) + 1; var ss = !wild && k >= 1 && Revealed(k, t) ? SlamGears(k) : new List<int>();
            if (wild) { float tw = t - wildAt; int ws = Mathf.FloorToInt(tw / 2.5f) + 1; if (ws <= 3 && 2.5f * ws - tw < 1.5f) ss.Add(WildSlamGear(ws)); }
            if (!(breakMode && t < 0)) for (int g = 0; g < 4; g++) if (gearR[g]) gearR[g].sharedMaterial = ss.Contains(g) ? hotM : gearM;
            for (int g = 0; g < 4; g++)
            {
                float ph = ((t + g * BiteEvery / 4) % BiteEvery + BiteEvery) % BiteEvery, soon = BiteEvery - ph;
                if (meshR[g]) meshR[g].sharedMaterial = ph < BiteLen ? meshBite : soon < 0.5f ? meshWarn : meshDim;
            }
            int n = 0;
            foreach (var s in shards)
            {
                if (pool.Count <= n) { var tr = Prim.Make(PrimitiveKind.Sphere, root, Vector3.zero, Vector3.one * BR * 2.2f, Color.white); Mat(tr, shardM); pool.Add(tr); }
                var b = pool[n++]; b.gameObject.SetActive(true); b.position = new Vector3(s.P.x, 0.9f, s.P.z);
            }
            for (int i = n; i < pool.Count; i++) pool[i].gameObject.SetActive(false);
            var me = W.Players.Count > 0 ? W.Players[0] : null;
            if (mySpot) { bool show = EscapementRelay.ShowMySpot && me && !me.Down; mySpot.gameObject.SetActive(show); if (show) { var gl = GoalOf(me, t, out _, out _); mySpot.position = new Vector3(gl.x, 0, gl.z); } }
        }
        void Teardown()
        {
            if (root) Object.Destroy(root.gameObject);
            root = mySpot = pit = floorIn = null; pool.Clear(); plans.Clear(); foreach (var l in gearParts) l.Clear();
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
            for (int g = 0; g < 4; g++) Label(cam, GearCentre(g) + Dir(90f * g + 90) * 3.0f, 0.3f, GN[g], new Color(0.97f, 0.95f, 0.88f), midStyle);
            for (int g = 0; g < 4; g++) Label(cam, GearCentre(g), 1.5f, "CORE", new Color(1f, 0.85f, 0.45f), nameStyle);
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
            float t = T; int k = Mathf.FloorToInt(t / Slam) + 1;
            if (breakMode && t < 0) return $"the floor breaks · get onto gear {GN[(HourOf(p) + 2) % 4]} and dodge the spiral";
            if (wild) return "the train runs wild · ride your gear, then get to the CENTRE before it tears loose";
            if (t < settle) return $"the floor falls in {settle - t:0.0}s · get onto gear {GN[TargetOf(p, k, t)]}";
            return $"slam {k} in {k * Slam - t:0.0}s · you ({UltimateState.Roman[HourOf(p) + 1]}): gear {GN[TargetOf(p, k, t)]} · hit the cores to hurt the boss";
        }

        public override void Cancel() { base.Cancel(); shards.Clear(); Teardown(); stop = false; }
        public void ResetCounts() { Jams = 0; Fails.Clear(); }
    }
}
