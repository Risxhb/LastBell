using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // Broken Hour (`bellkeeper.ult_broken_hour`), Bellkeeper Ultimate v2 P2 — proposal: the plates from P1 with the Gear
    // Train (card 03) and the Inverted Bell (card 04) on one beat. Built 2026-10-24 from the browser simulation Chzy18 asked
    // to see in Unity (tools/bellkeeper-ultimate/sims/p2*); not human-approved.
    // Every 3 s the hand ticks 90° to the next plate (P1's dial, with P1's rewinds) and three rules resolve on that tick:
    //   PLATE      the next hour in turn (I, II, III, IV, ...) soaks the lit plate. Empty = WIPE; anyone else on it = 60.
    //   SLAM       the bell slams the half away from the plate. Everyone must be on the plate's half, except the INVERTED
    //              players, who must be inside the slam. Wrong half = 999.
    //   GEAR TRAIN I–III and II–IV are chained: each chain 6–10 m on the beat (over 10 snaps = WIPE, under 6 grinds = 60 to
    //              both), and no two chains may cross (60 to the four players).
    // INVERTED belongs to the soaker's partner (hour + 2), so every beat it changes hands: the holder must touch the next
    // holder between beats. It jumps on touch (within 1.5 m) to a player who doesn't hold it, then can't move again for 1 s.
    // The wrong holders on a beat = WIPE. Party sizes: players 5-8 take hours I..IV again (two per hour: two soakers, two
    // INVERTED, their own chains); below 4, the hours that exist rotate and missing partners mean no chain / no INVERTED.
    // Whoever receives INVERTED keeps it until the beat (it can't be passed on twice). Below 4 players it is dealt each beat;
    // with 5-8 each group of four passes its own, and the two soakers share the plate 1.6 m apart.
    // The perfect bots' strat is a party plan per beat (the simulation's solver): a spot for everyone, and the meeting
    // point and time for the touch. The run's own player gets the same plan as a practice beam (EscapementRelay.ShowMySpot).
    public sealed class BrokenHour : UltimateMechanic
    {
        public override string Id => UltimateMechanics.BrokenHour;
        public override string Name => "Broken Hour";

        public const string Source = "Broken Hour";
        public const float BeatSeconds = 3f, PlateRadius = 7f, OnPlate = 1.8f, ChainMin = 6f, ChainMax = 10f, Touch = 1.5f, PassLock = 1f,
            SlamWarn = 1.2f, HalfMargin = 0.6f;
        public const int SmallDamage = 60;
        const float SolveMargin = 0.9f, SpotTolerance = 0.25f, ChainSlack = 0.5f;   // planned spots sit 0.9 m inside their half and chains 0.5 m inside 6-10; a bot stands within 0.25 m of its spot
        static readonly int[] Runs = { 6, 4, 5, 3 };

        public int Beat { get; private set; } = 1;          // dial position of the next beat
        int dir = 1, runIndex, runLeft = Runs[0], count;    // count = beats resolved this cast (the soaker turn)
        public int NextBeatTick { get; private set; } = -1;
        public int Beats { get; private set; }
        public int BeatsClean { get; private set; }
        public int Passes { get; private set; }
        public readonly List<string> Fails = new List<string>();
        readonly HashSet<PlayerController> inverted = new HashSet<PlayerController>();
        readonly Dictionary<PlayerController, int> lockUntil = new Dictionary<PlayerController, int>();
        readonly HashSet<PlayerController> settled = new HashSet<PlayerController>();   // received INVERTED this beat: keeps it until the beat
        bool stop;

        public override UltCue DefaultCue() => new UltCue { Variant = "practice", At = new[] { 4f } };
        public void StopAfterBeat() => stop = true;
        public bool IsInverted(PlayerController p) => inverted.Contains(p);

        // ---------------- the rule ----------------
        static Vector3 PlanDir(float a) => EscapementRelay.PlanDir(a);
        public static Vector3 HandDir(int v) => PlanDir(90f * v);
        public static Vector3 Plate(int v) => HandDir(v) * PlateRadius;
        int Hours => Mathf.Clamp(Party, 1, 4);
        public int HourOf(PlayerController p) { int i = W.Players.IndexOf(p); return i < 0 ? -1 : i % 4; }   // 0..3 = I..IV
        int SoakHour(int n) => n % Hours;                    // the n-th resolved beat's soaker hour
        int InvHour(int n) { int h = (SoakHour(n) + 2) % 4; return h < Hours ? h : -1; }
        PlayerController Partner(PlayerController p)
        {
            int i = W.Players.IndexOf(p); if (i < 0) return null;
            int j = i / 4 * 4 + (i % 4 + 2) % 4;
            return j < W.Players.Count && j != i ? W.Players[j] : null;
        }
        static bool Near(Vector3 q, int v) => Vector3.Dot(Sim.Flat(q), HandDir(v)) > HalfMargin;
        static bool Far(Vector3 q, int v) => Vector3.Dot(Sim.Flat(q), HandDir(v)) < -HalfMargin;
        int BeatAhead(int k)
        {
            int b = Beat, d = dir, left = runLeft, idx = runIndex;
            for (int i = 0; i < k; i++) { if (--left == 0) { d = -d; idx = (idx + 1) % Runs.Length; left = Runs[idx]; } b += d; }
            return b;
        }
        public bool RewindAfterNext => runLeft == 1;
        void Advance()
        {
            if (--runLeft == 0) { dir = -dir; runIndex = (runIndex + 1) % Runs.Length; runLeft = Runs[runIndex]; Boss.PlanLog.Add((Now, "broken hour rewind")); }
            Beat += dir; count++;
        }
        static bool Cross(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            float O(Vector3 p, Vector3 q, Vector3 r) => Mathf.Sign((q.x - p.x) * (r.z - p.z) - (q.z - p.z) * (r.x - p.x));
            return O(a, b, c) != O(a, b, d) && O(c, d, a) != O(c, d, b);
        }
        // the smallest distance between two segments (0 when they cross)
        static float SegGap(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            if (Cross(a, b, c, d)) return 0;
            float P(Vector3 p, Vector3 s0, Vector3 s1) { var s = s1 - s0; float t = s.sqrMagnitude < 1e-6f ? 0 : Mathf.Clamp01(Vector3.Dot(p - s0, s) / s.sqrMagnitude); return (p - (s0 + s * t)).magnitude; }
            return Mathf.Min(Mathf.Min(P(a, c, d), P(b, c, d)), Mathf.Min(P(c, a, b), P(d, a, b)));
        }
        IEnumerable<(PlayerController a, PlayerController b)> Chains()
        {
            for (int i = 0; i < W.Players.Count; i++) { var p = W.Players[i]; var q = Partner(p); if (q != null && W.Players.IndexOf(q) > i) yield return (p, q); }
        }

        // ---------------- cast ----------------
        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name);
            stop = false; count = 0; inverted.Clear(); lockUntil.Clear(); settled.Clear();
            foreach (var p in W.Players) if (HourOf(p) == InvHour(0)) inverted.Add(p);
            Build();
            NextBeatTick = TickAt(Cue.At0(BeatSeconds));
            SolvePlan(Now);
            Hazard slam = null;
            while (true)
            {
                while (Now < NextBeatTick)
                {
                    if (count > 0 && Hours >= 4) StepPasses();   // the first beat's holder is dealt, not passed (players may start bunched)
                    if (slam == null && NextBeatTick - Now <= Sim.Ticks(SlamWarn)) slam = SpawnSlam();
                    Present(); yield return 1;
                }
                Resolve(); slam = null;
                Advance(); Beats++;
                settled.Clear();
                // no partner hour this beat: INVERTED vanishes; below 4 players (no party plan to pass it) it is dealt every beat
                if (InvHour(count) < 0 || Hours < 4) inverted.Clear();
                if (inverted.Count == 0 && InvHour(count) >= 0) foreach (var p in W.Players) if (!p.Down && HourOf(p) == InvHour(count)) inverted.Add(p);
                NextBeatTick += Sim.Ticks(BeatSeconds);
                if (stop || Boss.Ult.WipeCause != null) break;
                SolvePlan(Now);
                Present(); yield return 1;
            }
            NextBeatTick = -1; inverted.Clear();
            Teardown();
        }

        // the slam's telegraph: the far half in coral (no damage of its own: Resolve deals the hits per player)
        Hazard SpawnSlam()
        {
            var far = -HandDir(Beat);
            var h = Spawn(new Hazard
            {
                Source = Source + ": the bell slams", Shape = HazardShape.Cone, Kind = HazardKind.Strike, Origin = Vector3.zero, Yaw = Quaternion.LookRotation(far).eulerAngles.y,
                Radius = 13f, HalfAngle = 90, LockAt = 0, ResolveAt = UltShapes.ResolveIn(NextBeatTick - Now), Damage = 0, Knockback = 0, Stun = 0, HitOnResolve = false,
            });
            h.OnResolve = hz => W.BossFx.SweepResolve(Vector3.zero, hz.Yaw, 13f, 90f);
            return h;
        }

        void StepPasses()
        {
            // INVERTED jumps from a holder to a non-holder it touches; both are locked for 1 s
            var players = W.Players;
            foreach (var from in players)
            {
                if (from.Down || !inverted.Contains(from) || Locked(from) || settled.Contains(from)) continue;
                foreach (var to in players)
                {
                    if (to == from || to.Down || inverted.Contains(to) || Locked(to)) continue;
                    if (players.IndexOf(to) / 4 != players.IndexOf(from) / 4) continue;   // 5-8 players: each group of four passes its own INVERTED
                    if ((Sim.Flat(to.transform.position) - Sim.Flat(from.transform.position)).magnitude > Touch) continue;
                    inverted.Remove(from); inverted.Add(to); settled.Add(to); Passes++;
                    if (LogSolver) Debug.Log($"[broken hour] pass {players.IndexOf(from)} -> {players.IndexOf(to)} at beat {Beats + 1} ({(NextBeatTick - Now) / (float)Sim.Hz:0.00} s before it)");
                    lockUntil[from] = lockUntil[to] = Now + Sim.Ticks(PassLock);
                    W.BossFx.ChimeResolve((from.transform.position + to.transform.position) * 0.5f, 0.8f);
                    Boss.PlanLog.Add((Now, $"inverted {UltimateState.Roman[HourOf(from) + 1]} -> {UltimateState.Roman[HourOf(to) + 1]}"));
                    break;
                }
            }
        }
        bool Locked(PlayerController p) => lockUntil.TryGetValue(p, out int t) && Now < t;

        void Resolve()
        {
            int v = Beat, sh = SoakHour(count), ih = InvHour(count); bool clean = true;
            var plate = Plate(v); int onPlate = 0, soakers = 0;
            foreach (var p in W.Players)
            {
                if (p.Down) continue;
                bool on = (Sim.Flat(p.transform.position) - plate).magnitude <= OnPlate;
                if (HourOf(p) == sh) { soakers++; if (on) onPlate++; }
                else if (on) { clean = false; Fail($"{Roman(p)} on the plate"); Hit(p, SmallDamage, "crowded plate"); }
            }
            Boss.Face(Quaternion.LookRotation(HandDir(v)).eulerAngles.y);
            W.BossFx.ChimeResolve(plate, 1.6f);
            if (onPlate == 0) { Fail("plate empty"); Ult.Wipe("The hour breaks (empty plate)"); return; }
            // the right INVERTED holders
            bool right = true;
            foreach (var p in W.Players) if (!p.Down && (HourOf(p) == ih) != inverted.Contains(p)) right = false;
            if (!right) { Debug.Log($"[broken hour] wrong holder on beat {Beats + 1}: holds " + string.Join(",", Holders()) + " · should " + string.Join(",", W.Players.FindAll(q => !q.Down && HourOf(q) == ih).ConvertAll(q => W.Players.IndexOf(q)))); Fail("wrong INVERTED holder"); Ult.Wipe("The bell rings inverted (wrong holder)"); return; }
            // the slam
            foreach (var p in W.Players)
            {
                if (p.Down) continue;
                bool inv = inverted.Contains(p), ok = inv ? Far(p.transform.position, v) : Near(p.transform.position, v);
                if (!ok) { clean = false; Fail($"{Roman(p)} {(inv ? "INVERTED outside the slam" : "in the slam")}"); Hit(p, UltimateState.Lethal, inv ? "the bell (inverted)" : "the bell slams"); }
            }
            // the gear train
            var chains = new List<(PlayerController a, PlayerController b)>(Chains());
            foreach (var (a, b) in chains)
            {
                if (a.Down || b.Down) continue;
                float L = (Sim.Flat(a.transform.position) - Sim.Flat(b.transform.position)).magnitude;
                if (L > ChainMax) { Fail($"chain {Roman(a)}–{Roman(b)} snaps"); Ult.Wipe("The gear train snaps"); return; }
                if (L < ChainMin) { clean = false; Fail($"chain {Roman(a)}–{Roman(b)} grinds"); Hit(a, SmallDamage, "the gears grind"); Hit(b, SmallDamage, "the gears grind"); }
            }
            for (int i = 0; i < chains.Count; i++)
                for (int j = i + 1; j < chains.Count; j++)
                {
                    var (a, b) = chains[i]; var (c, d) = chains[j];
                    if (a.Down || b.Down || c.Down || d.Down) continue;
                    if (!Cross(a.transform.position, b.transform.position, c.transform.position, d.transform.position)) continue;
                    clean = false; Fail("chains cross");
                    foreach (var p in new[] { a, b, c, d }) Hit(p, SmallDamage, "crossed chains");
                }
            if (clean) BeatsClean++;
        }
        List<int> Holders() { var l = new List<int>(); foreach (var q in inverted) l.Add(W.Players.IndexOf(q)); l.Sort(); return l; }
        string Roman(PlayerController p) => UltimateState.Roman[HourOf(p) + 1];
        void Fail(string why) { Fails.Add($"beat {Beats + 1}: {why}"); Boss.PlanLog.Add((Now, "broken hour fail " + why)); }
        void Hit(PlayerController p, int dmg, string what)
        {
            var h = new Hazard { Source = Source + ": " + what, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = Sim.Flat(p.transform.position), Radius = 0.5f, Damage = dmg, Knockback = 0, Stun = 0 };
            p.ReceiveHit(h);
        }

        // ---------------- the party plan (the perfect strat; tools/bellkeeper-ultimate/sims/p2solve.js) ----------------
        sealed class Step { public Vector3 Spot; public Vector3? Meet; public int MeetTick; }
        readonly Dictionary<PlayerController, Step> plan = new Dictionary<PlayerController, Step>();
        static List<Vector3> grid, fine;   // 1 m grid for the SAFE pair, 0.5 m for the INVERTED spot (a thin band between the half line and 10 m from the plate)
        public bool Planned(PlayerController p, out Vector3 spot) { spot = default; if (!plan.TryGetValue(p, out var s)) return false; spot = s.Spot; return true; }

        void SolvePlan(int now)
        {
            plan.Clear();
            if (grid == null)
            {
                grid = new List<Vector3>();
                float bound = Mathf.Min(11.2f, CombatWorld.PlayerBound - 0.3f);
                for (int x = -11; x <= 11; x++) for (int z = -11; z <= 11; z++) { var q = new Vector3(x, 0, z); if (q.magnitude >= 2.4f && q.magnitude <= bound) grid.Add(q); }
                fine = new List<Vector3>();
                for (int x = -22; x <= 22; x++) for (int z = -22; z <= 22; z++) { var q = new Vector3(x * 0.5f, 0, z * 0.5f); if (q.magnitude >= 2.4f && q.magnitude <= bound) fine.Add(q); }
            }
            int v = Beat, sh = SoakHour(count), ih = InvHour(count), left = NextBeatTick - now;
            float secs = left / (float)Sim.Hz, walk = Mathf.Max(0.5f, secs - 0.4f) * PlayerController.WalkSpeed;
            // groups of four are planned one after another (each avoids the chains and spots already planned). With two
            // groups, if one finds no plan: plan them the other way round, then with tighter margins (chains 0.6 m apart)
            int groups = (W.Players.Count + 3) / 4;
            int[][] orders = groups == 2 ? new[] { new[] { 0, 1 }, new[] { 1, 0 } } : new[] { new[] { 0 } };
            bool solved = false;
            for (int attempt = 0; attempt < orders.Length * 2 && !solved; attempt++)
            {
                gapMin = attempt < orders.Length ? 1f : 0.6f; spaceMin = attempt < orders.Length ? 2.5f : 2.1f;
                plan.Clear(); solved = true;
                var done = new List<(Vector3 a, Vector3 b)>(); var taken = new List<Vector3>();
                foreach (int g in orders[attempt % orders.Length])
                {
                    var grp = new PlayerController[4];
                    for (int k = 0; k < 4; k++) { int i = g * 4 + k; grp[k] = i < W.Players.Count && !W.Players[i].Down ? W.Players[i] : null; }
                    if (System.Array.IndexOf(grp, null) >= 0 || Hours < 4) { SimplePlan(grp, v, sh, ih); continue; }
                    if (!SolveGroup(grp, g, v, sh, ih, now, secs, walk, done, taken)) { solved = false; break; }
                }
            }
            gapMin = 1f; spaceMin = 2.5f;
            if (!solved)
            {
                Debug.LogWarning($"Broken Hour: no party plan for beat {Beats + 1}; simple spots");
                plan.Clear();
                for (int g = 0; g < groups; g++)
                {
                    var grp = new PlayerController[4];
                    for (int k = 0; k < 4; k++) { int i = g * 4 + k; grp[k] = i < W.Players.Count && !W.Players[i].Down ? W.Players[i] : null; }
                    SimplePlan(grp, v, sh, ih);
                }
            }
        }
        float gapMin = 1f, spaceMin = 2.5f;
        public static bool LogSolver;   // tests: why a beat found no plan, and every pass

        bool SolveGroup(PlayerController[] grp, int g, int v, int sh, int ih, int now, float secs, float walk, List<(Vector3 a, Vector3 b)> done, List<Vector3> taken)
        {
            // 5-8 players: the two soakers stand 0.8 m either side of the plate's centre, so their chains don't start at one point
            Vector3 plateC = Plate(v), pl = plateC + (W.Players.Count > 4 ? Vector3.Cross(Vector3.up, HandDir(v)) * (g == 0 ? -0.8f : 0.8f) : Vector3.zero); var cur = new Vector3[4];
            for (int k = 0; k < 4; k++) cur[k] = Sim.Flat(grp[k].transform.position);
            int s = sh, h = ih, x = -1;
            for (int k = 0; k < 4; k++) if (inverted.Contains(grp[k]) && k != h) x = k;      // this group's holder who must pass to h
            int ya = (s + 1) % 4, yb = (s + 3) % 4;
            // 5-8 players: each group keeps to its own side of the line through the plate and the boss, so the groups' chains never meet
            var sideDir = Vector3.Cross(Vector3.up, HandDir(v)) * (g == 0 ? -1f : 1f); bool split = W.Players.Count > 4;
            bool OwnSide(Vector3 q) => !split || Vector3.Dot(q, sideDir) >= 0.8f;
            int nH = 0, nPair = 0, nGap = 0, nSpace = 0, nMeet = 0;
            var near = new List<Vector3>(); foreach (var q in grid) if (Vector3.Dot(q, HandDir(v)) > SolveMargin && (q - plateC).magnitude > OnPlate + 0.6f && FarFrom(q, taken) && OwnSide(q)) near.Add(q);
            float bestScore = float.MaxValue; Vector3 bh = default, ba = default, bb = default, bm = default; float btm = 0; bool found = false;
            foreach (var ph in fine)
            {
                if (Vector3.Dot(ph, HandDir(v)) > -SolveMargin || !FarFrom(ph, taken) || !OwnSide(ph)) continue; float dph = (ph - pl).magnitude;
                if (dph < ChainMin + ChainSlack || dph > ChainMax - ChainSlack) continue;
                nH++;
                if (x < 0 && (cur[h] - ph).magnitude > walk) continue;
                foreach (var pa in near)
                {
                    if (!Near(pa, v) || (pa - plateC).magnitude <= OnPlate + 0.6f || (cur[ya] - pa).magnitude > walk) continue;
                    foreach (var pb in near)
                    {
                        if (!Near(pb, v) || (pb - plateC).magnitude <= OnPlate + 0.6f || (cur[yb] - pb).magnitude > walk) continue;
                        float dab = (pa - pb).magnitude; if (dab < ChainMin + ChainSlack || dab > ChainMax - ChainSlack) continue;
                        nPair++;
                        if (SegGap(pl, ph, pa, pb) < gapMin) continue;   // chains at least 1 m apart: a bot's 0.25 m slack can't make them cross
                        bool bad = false;
                        foreach (var (ca, cb) in done) if (SegGap(pl, ph, ca, cb) < gapMin || SegGap(pa, pb, ca, cb) < gapMin) { bad = true; break; }
                        if (!bad) nGap++;
                        if (bad) continue;
                        var P = new Vector3[4]; P[s] = pl; P[h] = ph; P[ya] = pa; P[yb] = pb;
                        // spots at least 2.5 m apart (no stray touches), except across the plate
                        for (int i = 0; i < 4 && !bad; i++) for (int j = i + 1; j < 4; j++) if ((P[i] - P[j]).magnitude < spaceMin) { bad = true; break; }
                        if (!bad) nSpace++;
                        if (bad) continue;
                        float mcost = 0, tm = 0; Vector3 m = default;
                        if (x >= 0)
                        {
                            bool ok = false; float mb = float.MaxValue;
                            for (float f = 0.2f; f <= 0.81f; f += 0.15f)
                            {
                                var mm = Vector3.Lerp(cur[x], cur[h], f); if (mm.magnitude < 2.4f) continue;
                                float tx = (cur[x] - mm).magnitude, th = (cur[h] - mm).magnitude, t = Mathf.Max(tx, th) / PlayerController.SprintSpeed + 0.35f;
                                float rest = Mathf.Max((mm - P[x]).magnitude, (mm - P[h]).magnitude) / PlayerController.SprintSpeed;
                                if (t + rest > secs - 0.5f) continue;
                                float c = tx + th + (mm - P[x]).magnitude + (mm - P[h]).magnitude;
                                if (c < mb) { mb = c; m = mm; tm = t; ok = true; }
                            }
                            if (ok) nMeet++;
                            if (!ok) continue; mcost = mb;
                        }
                        float travel = 0; for (int i = 0; i < 4; i++) if (i != x && i != h) travel += (cur[i] - P[i]).magnitude;
                        float score = travel + mcost + 0.15f * (P[0].magnitude + P[1].magnitude + P[2].magnitude + P[3].magnitude);
                        if (score < bestScore) { bestScore = score; bh = ph; ba = pa; bb = pb; bm = m; btm = tm; found = true; }
                    }
                }
            }
            if (!found) { if (LogSolver) Debug.Log($"[broken hour] group {g} beat {Beats + 1}: no plan · holder spots {nH} · near {near.Count} · pairs {nPair} · clear chains {nGap} · spaced {nSpace} · meets {nMeet} · gap {gapMin} · passer {x}"); return false; }
            plan[grp[s]] = new Step { Spot = pl }; plan[grp[h]] = new Step { Spot = bh }; plan[grp[ya]] = new Step { Spot = ba }; plan[grp[yb]] = new Step { Spot = bb };
            if (x >= 0) { int mt = now + Sim.Ticks(btm); plan[grp[x]].Meet = bm; plan[grp[x]].MeetTick = mt; plan[grp[h]].Meet = bm; plan[grp[h]].MeetTick = mt; }
            done.Add((pl, bh)); done.Add((ba, bb)); taken.Add(bh); taken.Add(ba); taken.Add(bb);
            return true;
        }

        static bool FarFrom(Vector3 q, List<Vector3> taken) { foreach (var t in taken) if ((q - t).magnitude < 2.5f) return false; return true; }   // (between groups always 2.5 m: a stray touch can't pass across groups anyway)

        // no full group (or a small party): the plate, the slam halves and the chains as plain spots
        void SimplePlan(PlayerController[] grp, int v, int sh, int ih)
        {
            var d = HandDir(v); var side = Vector3.Cross(Vector3.up, d);
            for (int k = 0; k < 4; k++)
            {
                var p = grp[k]; if (p == null) continue;
                Vector3 spot = k == sh ? Plate(v) : k == ih ? -d * 1.5f + side * 2.5f : k == (sh + 1) % 4 ? d * 3.5f - side * 1.8f : d * 3.0f - side * 8.6f;   // chains 8.9 m and 6.8 m, uncrossed
                plan[p] = new Step { Spot = spot };
            }
        }

        public override void Demands(PlayerController p, List<BotDemand> into)
        {
            if (!Running || NextBeatTick < 0 || p.Down || !plan.TryGetValue(p, out var s)) return;
            int since = NextBeatTick - Sim.Ticks(BeatSeconds);
            var spot = s.Spot;
            bool inv = HourOf(p) == InvHour(count); int v = Beat;
            into.Add(new BotDemand { Tick = NextBeatTick, Since = since, Hint = spot, From = this, Why = "broken hour spot", Ok = q => (Sim.Flat(q) - spot).magnitude <= SpotTolerance && (HourOf(p) == SoakHour(count) || (inv ? Far(q, v) : Near(q, v))) });
            // the touch: remembering to pass is the memory part (a Miss bot forgets one)
            if (s.Meet.HasValue && s.MeetTick > Now && (inverted.Contains(p) || plan.ContainsKey(p)))
            {
                var m = s.Meet.Value;
                into.Add(new BotDemand { Tick = s.MeetTick, Since = since, Hint = m, From = this, Memory = true, Why = "pass INVERTED", Ok = q => (Sim.Flat(q) - m).magnitude <= 0.55f });
            }
        }

        // ---------------- presentation ----------------
        Transform root, pivot, mySpot, myMeet; readonly Renderer[] plates = new Renderer[4];
        readonly List<Transform> chainBars = new List<Transform>(), auras = new List<Transform>();
        static Material brassDim, brassHot, brassM, goldChain, blueChain, coralM, purple, spotM, passM;
        float shownYaw;

        void Build()
        {
            if (root) return;
            if (!brassDim)
            {
                brassDim = Prim.Material(new Color(0.55f, 0.43f, 0.2f), 0.35f); brassHot = Prim.Material(new Color(1f, 0.8f, 0.4f), 2.2f);
                brassM = Prim.Material(new Color(0.9f, 0.7f, 0.32f), 1.4f); goldChain = Prim.Material(new Color(1f, 0.78f, 0.35f), 1.6f);
                blueChain = Prim.Material(new Color(0.45f, 0.65f, 1f), 1.6f); coralM = Prim.Material(new Color(1f, 0.2f, 0.2f), 2f);
                purple = Prim.Material(new Color(0.72f, 0.45f, 1f), 2.2f); spotM = Prim.Material(new Color(0.3f, 1f, 0.85f), 2.5f);
                passM = Prim.Material(new Color(1f, 0.6f, 0.4f), 2.5f);
            }
            root = new GameObject("Broken Hour (stand-in)").transform; root.SetParent(W.Root, false);
            for (int i = 0; i < 4; i++)
            {
                var t = Prim.Make(PrimitiveKind.Cylinder, root, PlanDir(90 * i) * PlateRadius + Vector3.up * 0.04f, new Vector3(3.2f, 0.02f, 3.2f), Color.white);
                plates[i] = t.GetComponent<Renderer>(); plates[i].sharedMaterial = brassDim;
            }
            pivot = new GameObject("hand").transform; pivot.SetParent(root, false);
            Mat(Prim.Make(PrimitiveKind.Cube, pivot, new Vector3(0, 0.06f, 3.6f), new Vector3(0.45f, 0.03f, 4.6f), Color.white), brassHot);
            Mat(Prim.Make(PrimitiveKind.Cube, pivot, new Vector3(0, 4.2f, 3.0f), new Vector3(0.4f, 0.2f, 6.0f), Color.white), brassM);
            Mat(Prim.Make(PrimitiveKind.Cone, pivot, new Vector3(0, 4.2f, 6.4f), new Vector3(1.1f, 0.9f, 1.1f), Color.white, 0, Quaternion.Euler(90, 0, 0)), brassM);
            Mat(Prim.Make(PrimitiveKind.Cube, pivot, new Vector3(0, 0.05f, 0), new Vector3(23f, 0.03f, 0.12f), Color.white), brassM);   // the half line
            mySpot = new GameObject("my spot").transform; mySpot.SetParent(root, false);
            Mat(Prim.Make(PrimitiveKind.Cylinder, mySpot, new Vector3(0, 0.07f, 0), new Vector3(1.1f, 0.02f, 1.1f), Color.white), spotM);
            Mat(Prim.Make(PrimitiveKind.Cylinder, mySpot, new Vector3(0, 1.6f, 0), new Vector3(0.12f, 1.6f, 0.12f), Color.white), spotM);
            myMeet = new GameObject("my touch point").transform; myMeet.SetParent(root, false);
            Mat(Prim.Make(PrimitiveKind.Cylinder, myMeet, new Vector3(0, 0.08f, 0), new Vector3(1.4f, 0.02f, 1.4f), Color.white), passM);
        }
        static void Mat(Transform t, Material m) { var r = t.GetComponent<Renderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }

        void Present()
        {
            if (!pivot) return;
            int lit = ((Beat % 4) + 4) % 4;
            for (int i = 0; i < 4; i++) if (plates[i]) plates[i].sharedMaterial = i == lit ? brassHot : brassDim;
            float want = Quaternion.LookRotation(HandDir(Beat)).eulerAngles.y;
            shownYaw = Mathf.MoveTowardsAngle(shownYaw, want, 720 * Sim.Dt); pivot.rotation = Quaternion.Euler(0, shownYaw, 0);
            int n = 0;
            foreach (var (a, b) in Chains())
            {
                if (a.Down || b.Down) continue;
                if (chainBars.Count <= n) chainBars.Add(Prim.Make(PrimitiveKind.Cube, root, Vector3.zero, Vector3.one, Color.white));
                var c = chainBars[n++]; c.gameObject.SetActive(true);
                Vector3 pa = Sim.Flat(a.transform.position), pb = Sim.Flat(b.transform.position), dv = pb - pa; float L = dv.magnitude;
                c.position = (pa + pb) * 0.5f + Vector3.up * 0.9f; c.rotation = Quaternion.LookRotation(L > 0.01f ? dv : Vector3.forward); c.localScale = new Vector3(0.1f, 0.1f, L);
                Mat(c, L > ChainMax || L < ChainMin ? coralM : HourOf(a) % 2 == 0 ? goldChain : blueChain);
            }
            for (int i = n; i < chainBars.Count; i++) chainBars[i].gameObject.SetActive(false);
            int k = 0;
            foreach (var p in inverted)
            {
                if (p.Down) continue;
                if (auras.Count <= k) { var t = Prim.Make(PrimitiveKind.Cylinder, root, Vector3.zero, new Vector3(1.9f, 0.02f, 1.9f), Color.white); Mat(t, purple); auras.Add(t); }
                var au = auras[k++]; au.gameObject.SetActive(true); au.position = Sim.Flat(p.transform.position) + Vector3.up * 0.09f;
            }
            for (int i = k; i < auras.Count; i++) auras[i].gameObject.SetActive(false);
            var me = W.Players.Count > 0 ? W.Players[0] : null; Step st = null;
            bool show = EscapementRelay.ShowMySpot && me && !me.Down && plan.TryGetValue(me, out st);
            if (mySpot) { mySpot.gameObject.SetActive(show); if (show) mySpot.position = st.Spot; }
            if (myMeet) { bool meet = show && st.Meet.HasValue && Now < st.MeetTick + Sim.Ticks(0.4f); myMeet.gameObject.SetActive(meet); if (meet) myMeet.position = st.Meet.Value; }
        }
        void Teardown()
        {
            if (root) Object.Destroy(root.gameObject);
            root = pivot = mySpot = myMeet = null; chainBars.Clear(); auras.Clear(); plan.Clear();
            for (int i = 0; i < 4; i++) plates[i] = null;
        }

        public string JobOf(PlayerController p, out Color c)
        {
            int sh = SoakHour(count), ih = InvHour(count), h = HourOf(p);
            if (h == sh) { c = EscapementRelay.DutyColors[0]; return "SOAK"; }
            if (h == ih) { c = new Color(0.78f, 0.6f, 1f); return "INVERTED"; }
            if (inverted.Contains(p)) { c = new Color(1f, 0.62f, 0.42f); return "PASS → " + UltimateState.Roman[ih + 1]; }
            c = EscapementRelay.DutyColors[2]; return "SAFE";
        }
        public string HudText(PlayerController p)
        {
            if (!Running || NextBeatTick < 0 || p == null) return null;
            var partner = Partner(p);
            string chain = partner != null && !partner.Down ? $" · chain to {UltimateState.Roman[HourOf(partner) + 1]} {(Sim.Flat(p.transform.position) - Sim.Flat(partner.transform.position)).magnitude:0.0} m" : "";
            return $"beat in {Mathf.Max(0, (NextBeatTick - Now) / (float)Sim.Hz):0.0}s · you ({UltimateState.Roman[HourOf(p) + 1]}): {JobOf(p, out _)}{chain}" + (RewindAfterNext ? "   ·   REWIND after this beat" : "");
        }
        static GUIStyle nameStyle, bigStyle, spotStyle;
        public void DrawGui(Camera cam)
        {
            if (!Running || NextBeatTick < 0 || cam == null) return;
            if (nameStyle == null)
            {
                nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                spotStyle = new GUIStyle(nameStyle) { fontSize = 16 }; bigStyle = new GUIStyle(nameStyle) { fontSize = 40 };
            }
            Label(cam, Plate(Beat), 0.3f, "PLATE", EscapementRelay.DutyColors[0], spotStyle);
            Label(cam, -HandDir(Beat) * 9.5f, 0.3f, "SLAM (INVERTED here)", new Color(1f, 0.55f, 0.45f), spotStyle);
            for (int i = 0; i < W.Players.Count; i++)
            {
                var p = W.Players[i]; if (p.Down) continue;
                var job = JobOf(p, out var c);
                Label(cam, p.transform.position, 2.9f, (i == 0 ? "YOU " : "") + UltimateState.Roman[HourOf(p) + 1] + "  " + job, c, nameStyle);
            }
            if (RewindAfterNext)
            {
                bigStyle.normal.textColor = Color.Lerp(new Color(1f, 0.45f, 0.35f), Color.white, Mathf.PingPong(Time.unscaledTime * 3, 1));
                GUI.Label(new Rect(0, Screen.height * 0.2f, Screen.width, 60), "REWIND NEXT BEAT", bigStyle);
            }
        }
        static void Label(Camera cam, Vector3 world, float height, string text, Color c, GUIStyle style)
        {
            var sp = cam.WorldToScreenPoint(new Vector3(world.x, height, world.z)); if (sp.z <= 0) return;
            var r = new Rect(sp.x - 130, Screen.height - sp.y - 13, 260, 26);
            style.normal.textColor = new Color(0, 0, 0, 0.85f); GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, style);
            style.normal.textColor = c; GUI.Label(r, text, style);
        }

        public override void Cancel()
        {
            base.Cancel();
            Teardown(); NextBeatTick = -1; stop = false; inverted.Clear(); lockUntil.Clear();
        }
        public void ResetBeats() { Beat = 1; dir = 1; runIndex = 0; runLeft = Runs[0]; count = 0; Beats = 0; BeatsClean = 0; Passes = 0; Fails.Clear(); }
    }
}
