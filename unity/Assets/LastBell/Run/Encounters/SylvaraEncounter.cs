using System;
using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Sylvara, The Rootbound Choir: Verdant Chimes' area boss, v3 bullet-hell fight (Chzy18 accepted all four phases on
    // 29 Oct 2026; design in sylvara-design/bosses/3-verdant-chimes-sylvara/abilities-v3/P1..P4-*.md, ported from the
    // browser demo's numbers). She stays rooted at the arena centre. Four phases at 75 / 50 / 25 % HP, no intermission,
    // no punish windows (Sylvara exception, design rules §5). Each phase is a timed loop that repeats until its HP
    // threshold; crossing one clears every bullet and warning for a harmless 1.5 s transition.
    //   P1 Spiral Bloom (30 s)      petal spiral, seed pods, sweeping waterfall jets, front-and-back Pruning Arc
    //   P2 Two-Colour Choir (21 s)  brass/cobalt arms and lanes; only your colour hurts you; each toll swaps it
    //   P3 Hollow Bloom (24 s)      second form: 4-arm spiral and two waves of Choir Buds to cut
    //   P4 Seed Storm (24 s)        rings of seeds that burst into petals; enrage (timing accepted 29 Oct)
    // Angles in this file are radians on the floor: a = 0 is +x, a = 90° is +z. Distances are metres from the centre.
    public sealed class SylvaraEncounter : BossEncounter
    {
        public override string Id => "boss.sylvara";
        public override string DisplayName => "Sylvara, the Rootbound Choir";
        public override string Lesson => "Weave through the petals; only your own colour hurts";
        public override bool Placeholder => !presenter || !presenter.HasModel;

        public static readonly string[] PhaseNames = { "Spiral Bloom", "Two-Colour Choir", "Hollow Bloom", "Seed Storm" };
        static readonly float[] LoopSeconds = { 30, 21, 24, 24 };
        const float Thr2 = 0.75f, Thr3 = 0.50f, Thr4 = 0.25f;
        const int TransTicks = 90;
        const float Deg = Mathf.Deg2Rad, Tau = Mathf.PI * 2;
        const int Stun = 9;                       // ground and arc hits: 0.15 s, no knockback (v1 D6); bullets never stun
        public const int Brass = 1, Cobalt = 2;
        // Enrage (accepted, Chzy18 29 Oct; same clock on every difficulty): +1 seed per ring and rings 0.1 s sooner every 24 s loop, capped at loop 5;
        // Last Storm at 2:00 into Phase 4.
        public const float LastStormAt = 120;

        CharacterPresenter presenter;
        int phase;
        int loopStart, p4Start = -1;
        public int Phase => phase;
        public int P4Loop { get; private set; }
        public bool LastStorm { get; private set; }
        public int StormSeeds { get; private set; } = 8;
        public float StormEvery { get; private set; } = 2.0f;
        public int BudsCut { get; private set; }
        public int BudsBloomed { get; private set; }
        public readonly List<string> Order = new List<string>();   // attack starts, for tests
        float LoopT => (Age - loopStart) * Sim.Dt;
        public float P4Time => p4Start < 0 ? 0 : (Age - p4Start) * Sim.Dt;   // seconds since Phase 4 began (enrage clock)

        sealed class Emitter { public bool On = true, Looping; public Action<float> Update; }
        readonly List<Emitter> emit = new List<Emitter>();
        readonly List<(int at, Action fn)> later = new List<(int, Action)>();
        Emitter sp1, sp2;

        sealed class Bud { public Vector3 Pos; public float Hp = 100, Life, Base, Acc; public int Born; public bool Alive = true; public Transform Prop; public Hazard Timer; }
        readonly List<Bud> buds = new List<Bud>();
        public int BudsAlive { get { int n = 0; foreach (var b in buds) if (b.Alive) n++; return n; } }
        public List<Vector3> AliveBudPositions() { var l = new List<Vector3>(); foreach (var b in buds) if (b.Alive) l.Add(b.Pos); return l; }
        // test hook: restart the Seed Storm as Phase 4 loop k would start it (k >= 5 = Last Storm), without waiting
        public void DebugStartStorm(int k)
        {
            if (phase != 4) return;
            foreach (var e in emit) if (e.Looping) e.On = false;
            P4Loop = k; StartStorm();
        }

        readonly Dictionary<PlayerController, int> colour = new Dictionary<PlayerController, int>();
        readonly HashSet<PlayerController> swapWarned = new HashSet<PlayerController>();
        readonly Dictionary<PlayerController, Transform> colourRings = new Dictionary<PlayerController, Transform>();
        public int ColourOf(PlayerController p) => colour.TryGetValue(p, out int c) ? c : 0;

        Transform maskL, maskR, bellRoot, shoulderFlowers;
        readonly List<Transform> props = new List<Transform>();
        // Verdant Chimes' sun sits behind her from the gameplay camera, so her camera-facing side is in shadow: a soft
        // front light lifts it and a turquoise rim edges her silhouette. Presentation only, fixed world offsets.
        Light frontLight, rimLight;
        static readonly Vector3 FrontOffset = new Vector3(0, 3.0f, 2.6f), RimOffset = new Vector3(0, 5.2f, -2.4f);
        const float FrontIntensity = 12f, RimIntensity = 18f;

        void Awake() { MaxHp = 13500; BodyRadius = 1.6f; HitHeight = 2.4f; MarkHeight = 4.8f; }

        static readonly Color Stone = new Color(0.68f, 0.70f, 0.64f), Wood = new Color(0.27f, 0.20f, 0.15f),
            BrassC = new Color(0.55f, 0.47f, 0.25f), Ivory = new Color(0.90f, 0.87f, 0.78f),
            Turquoise = new Color(0.25f, 0.95f, 0.85f), Leaf = new Color(0.30f, 0.42f, 0.24f), Amber = new Color(1f, 0.71f, 0.28f),
            RingBrass = new Color(0.85f, 0.64f, 0.25f), RingCobalt = new Color(0.36f, 0.52f, 1f);

        protected override void Build()
        {
            // Resources/Characters/Sylvara.glb (Meshy, rigged by prototype/arena-3d/models/build-sylvara.ps1); the primitive
            // body below is the fallback stand-in when the model is missing
            presenter = CharacterPresenter.Create(transform, "Sylvara", 4f, 0.6f, 0, host =>
            {
                for (int i = 0; i < 4; i++)
                {
                    float a = 45 + 90 * i; var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                    Prim.Make(PrimitiveKind.Cylinder, host, dir * 0.95f + Vector3.up * 0.95f, new Vector3(0.34f, 0.85f, 0.34f), Wood, 0, Quaternion.LookRotation(dir) * Quaternion.Euler(-28, 0, 0));
                    Prim.Make(PrimitiveKind.Sphere, host, dir * 1.35f + Vector3.up * 0.2f, new Vector3(0.6f, 0.45f, 0.6f), Stone);
                }
                Prim.Make(PrimitiveKind.Capsule, host, new Vector3(0, 2.6f, 0), new Vector3(0.9f, 0.9f, 0.7f), Wood);
                bellRoot = new GameObject("Bells").transform; bellRoot.SetParent(host, false); bellRoot.localPosition = new Vector3(0, 2.7f, 0.42f);
                for (int i = 0; i < 3; i++)
                    Prim.Make(PrimitiveKind.Cone, bellRoot, new Vector3((i - 1) * 0.22f, i == 1 ? -0.1f : 0, 0), new Vector3(0.24f, 0.26f, 0.24f), BrassC, 0.4f);
                Prim.Make(PrimitiveKind.Sphere, host, new Vector3(0, 2.8f, 0.2f), Vector3.one * 0.32f, Turquoise, 1.2f);
                maskL = Prim.Make(PrimitiveKind.Sphere, host, new Vector3(-0.13f, 3.65f, 0.2f), new Vector3(0.28f, 0.6f, 0.36f), Ivory);
                maskR = Prim.Make(PrimitiveKind.Sphere, host, new Vector3(0.13f, 3.65f, 0.2f), new Vector3(0.28f, 0.6f, 0.36f), Ivory);
                Prim.Make(PrimitiveKind.Sphere, host, new Vector3(-0.1f, 3.72f, 0.38f), Vector3.one * 0.07f, Turquoise, 2f);
                Prim.Make(PrimitiveKind.Sphere, host, new Vector3(0.1f, 3.72f, 0.38f), Vector3.one * 0.07f, Turquoise, 2f);
                Prim.Make(PrimitiveKind.Cube, host, new Vector3(0, 3.25f, 0), new Vector3(1.9f, 0.25f, 0.35f), Wood);
                Prim.Make(PrimitiveKind.Cube, host, new Vector3(1.05f, 2.2f, 0.25f), new Vector3(0.14f, 2.0f, 0.14f), BrassC, 0, Quaternion.Euler(-15, 0, 8));
                Prim.Make(PrimitiveKind.Cube, host, new Vector3(-1.0f, 2.3f, 0.2f), new Vector3(0.16f, 1.7f, 0.16f), Wood, 0, Quaternion.Euler(-10, 0, -10));
                Prim.Make(PrimitiveKind.Cylinder, host, new Vector3(0, 3.3f, -0.5f), new Vector3(1.5f, 0.05f, 1.5f), BrassC, 0, Quaternion.Euler(90, 0, 0));
                Prim.Make(PrimitiveKind.Sphere, host, new Vector3(-0.8f, 3.5f, -0.1f), new Vector3(0.6f, 0.35f, 0.5f), Leaf);
                Prim.Make(PrimitiveKind.Sphere, host, new Vector3(0.8f, 3.5f, -0.1f), new Vector3(0.6f, 0.35f, 0.5f), Leaf);
                shoulderFlowers = new GameObject("ShoulderFlowers").transform; shoulderFlowers.SetParent(host, false);
                Prim.Make(PrimitiveKind.Sphere, shoulderFlowers, new Vector3(-0.7f, 3.75f, 0.1f), Vector3.one * 0.22f, Ivory, 0.3f);
                Prim.Make(PrimitiveKind.Sphere, shoulderFlowers, new Vector3(0.7f, 3.75f, 0.1f), Vector3.one * 0.22f, Ivory, 0.3f);
                shoulderFlowers.gameObject.SetActive(false);
                return host;
            });
            PhaseName = PhaseNames[0];
            frontLight = MakeLight("Front Light", new Color(1f, 0.95f, 0.86f), FrontIntensity, 6.5f);
            rimLight = MakeLight("Rim Light", new Color(0.55f, 1f, 0.92f), RimIntensity, 6.5f);
            PlaceLights();
        }

        Light MakeLight(string name, Color c, float intensity, float range)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.transform.SetParent(transform, false);
            l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
            return l;
        }
        void PlaceLights()
        {
            if (frontLight) frontLight.transform.position = transform.position + FrontOffset;
            if (rimLight) rimLight.transform.position = transform.position + RimOffset;
        }

        int PhaseFor() => Hp > MaxHp * Thr2 ? 1 : Hp > MaxHp * Thr3 ? 2 : Hp > MaxHp * Thr4 ? 3 : 4;
        public override float NextPhaseFraction() => Hp > MaxHp * Thr2 ? Thr2 - 0.01f : Hp > MaxHp * Thr3 ? Thr3 - 0.01f : Thr4 - 0.01f;

        // ---------------- schedule ----------------
        protected override IEnumerable<int> Script()
        {
            W.Bullets.Bound = CombatWorld.PlayerBound + 0.6f;
            W.Bullets.ColourOf = ColourOf;
            while (true)
            {
                int want = PhaseFor();
                if (want != phase) foreach (var w in Transition(want)) yield return w;
                var ev = Loop(phase);
                ev.Sort((a, b) => a.at.CompareTo(b.at));
                int len = Sim.Ticks(LoopSeconds[phase - 1]), i = 0;
                loopStart = Age;
                for (int t = 0; t < len; t++)
                {
                    while (i < ev.Count && Sim.Ticks(ev[i].at) <= t) ev[i++].fn();
                    if (PhaseFor() != phase) break;
                    yield return 1;
                }
                foreach (var e in emit) if (e.Looping) e.On = false;   // the next loop starts its own spirals and lanes
                if (phase == 4 && PhaseFor() == 4) P4Loop++;
            }
        }

        // Crossing a threshold: everything clears, the boss shifts (harmless), and the new phase starts clean.
        IEnumerable<int> Transition(int to)
        {
            bool first = phase == 0;
            phase = to; SetPhase(PhaseNames[to - 1]);
            ClearFight();
            if (to == 2) AssignColours();
            if (to == 4) { p4Start = Age + (first ? 0 : TransTicks); P4Loop = 0; LastStorm = false; }
            if (first) yield break;
            Current = "phase shift";
            presenter.PlayOnce("PhaseShift", TransTicks / (float)Sim.Hz);
            glowTarget = 3f;
            RunHud.Float(transform.position + Vector3.up * 5.4f, to == 3 ? "The mask splits" : PhaseNames[to - 1], Amber);
            for (int i = 0; i < TransTicks; i++)
            {
                if (i == TransTicks / 2) { W.Fx.Crescendo(transform.position, true, 0, false); Release(1.5f); if (shoulderFlowers) shoulderFlowers.gameObject.SetActive(to >= 2); }
                if (to >= 3 && maskL)
                {
                    maskL.localPosition = Vector3.Lerp(maskL.localPosition, new Vector3(-0.32f, 3.65f, 0.2f), 0.05f);
                    maskR.localPosition = Vector3.Lerp(maskR.localPosition, new Vector3(0.32f, 3.65f, 0.2f), 0.05f);
                }
                yield return 1;
            }
            glowTarget = 1;
        }

        void ClearFight()
        {
            foreach (var e in emit) e.On = false;
            emit.Clear(); later.Clear(); sp1 = sp2 = null;
            foreach (var b in buds) b.Alive = false;
            buds.Clear();
            W.Bullets.ClearAll();
            W.Hazards.ClearAll();
            colour.Clear(); swapWarned.Clear();
            ClearProps();
        }

        List<(float at, Action fn)> Loop(int ph)
        {
            var ev = new List<(float, Action)>();
            void At(float t, string id, Action fn) => ev.Add((t, (Action)(() => { if (id != null) { Order.Add(id); Current = id; } fn(); })));
            switch (ph)
            {
                case 1:   // Spiral Bloom
                    At(0, "Petal Spiral", () => sp1 = Spiral(1, 4, 15 * Deg, 5, 0.15f, 9, 7));
                    At(3, "Seed Pods", () => PodsOnFar());
                    At(6, "Waterfall Jets", Jets);
                    At(8, "Toll", () => Toll("TOLL: the spin reverses"));
                    At(9, null, () => Reverse(sp1));
                    At(10, "Feet Pod", () => Pod(null, Vector3.zero, 3.0f));
                    At(13, "Pruning Arc", PruningArc);
                    At(14.5f, "Doubled Pods", () => PodsOnFar(doubled: true));
                    At(18, "Waterfall Jets", Jets);
                    At(21, "Toll", () => Toll("TOLL: the spin reverses"));
                    At(22, "Second Spiral", () => { Reverse(sp1); sp2 = Spiral(-1, 3, 20 * Deg, 4, 0.18f, 8, 5); });
                    At(23.5f, "Triangle Pods", TrianglePods);
                    At(27, "Pruning Arc", PruningArc);
                    break;
                case 2:   // Two-Colour Choir, harder version
                    At(0, "Colour Spiral", () => sp1 = Spiral(1, 6, 14 * Deg, 5, 0.13f, 9, 7, new[] { Brass, Cobalt }));
                    At(0.01f, "Brass Lanes", () => ChoirLanes(Brass, -Mathf.PI / 2, 1, 10));
                    At(6, "Cobalt Lanes", () => ChoirLanes(Cobalt, Mathf.PI, 1, 20));
                    At(10, "Second Colour Spiral", () => sp2 = Spiral(-1, 4, 18 * Deg, 4, 0.18f, 8, 5, new[] { Cobalt, Brass }));
                    At(12, "Root Ring", () => RootRing(3.5f));
                    At(14.5f, "Brass Lanes", () => ChoirLanes(Brass, Mathf.PI / 2, -1, 20));
                    foreach (float t in W.Players.Count == 1 ? new[] { 4f, 8, 12, 16, 20 } : new[] { 5f, 10, 15, 20 })
                    {
                        At(t - 1, "Toll", () => { Toll("TOLL: every colour swaps"); WarnSwap(); });
                        At(t, null, SwapColours);
                        At(t + 0.02f, null, TollBurst);
                    }
                    break;
                case 3:   // Hollow Bloom (second form)
                    At(0.01f, "Hollow Spiral", () => sp1 = Spiral(1, 4, 10 * Deg, 4.5f, 0.18f, 9, 7));
                    At(1, "Choir Buds", () => PlantBuds(12));
                    At(8, "Feet Pod", () => Pod(null, Vector3.zero, 3.0f));
                    At(12, "Choir Buds", () => PlantBuds(12));
                    At(15, "Pruning Arc", PruningArc);
                    At(20, "Seed Pods", () => PodsOnFar());
                    break;
                default:  // Seed Storm, with the enrage steps
                    At(0, "Seed Storm", StartStorm);
                    At(6, "Seed Pods", () => PodsOnFar());
                    At(12, "Root Ring", () => RootRing(3.5f));
                    At(18, "Seed Pods", () => PodsOnFar());
                    if (P4Loop == 4) At(LastStormAt - 4 * 24 - 3, "Toll", () => Toll("LAST STORM in 3 s"));
                    break;
            }
            return ev;
        }

        // ---------------- helpers ----------------
        List<PlayerController> Standing => W.Players.FindAll(p => !p.Down);
        static Vector3 On(float a, float r) => new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
        static float AngOf(Vector3 v) => Mathf.Atan2(v.z, v.x);
        static float YawOf(float a) => 90f - a * Mathf.Rad2Deg;   // floor angle -> Sim yaw (degrees, 0 = +z)
        float Rim => BodyRadius;
        float R => CombatWorld.PlayerBound;
        void Later(float seconds, Action fn) => later.Add((Age + Mathf.Max(1, Sim.Ticks(seconds)), fn));
        Emitter Add(Emitter e) { emit.Add(e); return e; }
        void Shot(Vector3 at, float a, float v, int dmg = 12, float life = 6, int col = 0, string src = "Petal", float r = 0.35f, Action<BulletSystem.Bullet> split = null) =>
            W.Bullets.Fire(at, a, v, dmg, life, r, col, src, split);
        void PetalRing(Vector3 c, int n, float v, float off, float r0, int dmg = 12, string src = "Petal")
        {
            for (int i = 0; i < n; i++) { float a = off + i * Tau / n; Shot(c + On(a, r0), a, v, dmg, 6, 0, src); }
        }
        static float Rnd(float a, float b) => UnityEngine.Random.Range(a, b);
        Hazard Tune(Hazard h) { h.Stun = Stun; h.Knockback = 0; return h; }
        int FarCount { get { int n = W.Players.Count; return n <= 2 ? 1 : n <= 4 ? 2 : 3; } }
        int BudCount { get { int n = W.Players.Count; return n <= 2 ? 2 : n <= 4 ? 3 : 4; } }
        List<PlayerController> Farthest(int k)
        {
            var s = Standing; s.Sort((a, b) => Sim.Flat(b.transform.position).sqrMagnitude.CompareTo(Sim.Flat(a.transform.position).sqrMagnitude));
            return s.GetRange(0, Mathf.Min(k, s.Count));
        }
        void Toll(string text)
        {
            RunHud.Float(transform.position + Vector3.up * 5.4f, text, Amber);
            W.Fx.Crescendo(transform.position, true, 0, false);
            Release(0.8f);
        }

        // Petal Spiral: arms from her rim, a petal per arm every `period`, gaps of (skipEvery - keep) steps; turns `w` rad/s
        Emitter Spiral(int dir, int arms, float w, float v, float period, int skipEvery, int keep, int[] cols = null)
        {
            float bse = Rnd(0, Tau), acc = 0; int c = 0;
            var e = new Emitter { Looping = true };
            float d = dir;
            e.Update = dt =>
            {
                bse += d * w * dt; acc += dt;
                while (acc >= period)
                {
                    acc -= period; c++;
                    if (c % skipEvery < keep)
                        for (int i = 0; i < arms; i++) { float a = bse + i * Tau / arms; Shot(On(a, Rim), a, v, 10, 4, cols != null ? cols[i % cols.Length] : 0, "Petal Spiral"); }
                }
            };
            spinDir[e] = () => d = -d;
            return Add(e);
        }
        readonly Dictionary<Emitter, Action> spinDir = new Dictionary<Emitter, Action>();
        void Reverse(Emitter e) { if (e != null && spinDir.TryGetValue(e, out var f)) f(); }

        // Seed Pod: a 2.5 m circle on the target, tracking 0.6 s, lands at 2.5 s, then 12 petals; doubled = a second ring
        // 0.6 s later rotated half a step into the first ring's gaps
        void Pod(PlayerController target, Vector3 at, float r = 2.5f, bool doubled = false)
        {
            const int n = 12; float off = Rnd(0, Tau);
            var h = Tune(new Hazard
            {
                Source = target ? "Seed Pod" : "Feet Pod", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Radius = r,
                LockAt = target ? Sim.Ticks(0.6f) : 0, ResolveAt = Sim.Ticks(2.5f), Damage = 16,
                Origin = target ? Sim.Flat(target.transform.position) : at,
            });
            if (target) h.Track = hz => { if (target && !target.Down) hz.Origin = Sim.Flat(target.transform.position); };
            h.OnResolve = hz =>
            {
                W.BossFx.ChimeResolve(hz.Origin, hz.Radius); Release(0.5f);
                float r0 = Mathf.Max(r - 0.3f, hz.Origin.sqrMagnitude < 0.01f ? Rim : 0);
                PetalRing(hz.Origin, n, 5.5f, off, r0, 12, "Pod Petal");
                if (doubled) { var o = hz.Origin; Later(0.6f, () => PetalRing(o, n, 5.5f, off + Mathf.PI / n, r0, 12, "Pod Petal")); }
            };
            W.Hazards.Spawn(h);
        }
        void PodsOnFar(bool doubled = false)
        {
            presenter.PlayOnce("Seed", 1.2f);
            if (W.Players.Count == 1) { var p = W.Players[0]; if (!p.Down) Pod(p, Vector3.zero, 2.5f, doubled); return; }
            foreach (var p in Farthest(FarCount)) Pod(p, Vector3.zero, 2.5f, doubled);
        }
        void TrianglePods()
        {
            var f = Farthest(1); float a0 = f.Count > 0 ? AngOf(f[0].transform.position) : Rnd(0, Tau);
            for (int i = 0; i < 3; i++)
            {
                var at = On(a0 + i * Tau / 3, 7);
                if (W.Players.Count == 1) Later(i * 0.8f, () => Pod(null, at)); else Pod(null, at);
            }
        }
        void RootRing(float r)
        {
            presenter.PlayOnce("Rootscript", 2f);
            var h = Tune(new Hazard { Source = "Root Ring", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Radius = r, LockAt = 0, ResolveAt = Sim.Ticks(2), Damage = 18, Origin = Vector3.zero });
            h.OnResolve = hz => { W.BossFx.ChimeResolve(hz.Origin, hz.Radius); Release(); };
            W.Hazards.Spawn(h);
        }

        // Waterfall Jets: one per player past 8 m (else the farthest). The wedge warns 1.5 s; then a solid stream (a drop
        // every 0.07 s at 9 m/s) pours from the wall to her rim while its source slides 40° along the wall over 2 s,
        // starting 20° before the target.
        void Jets()
        {
            var ts = Standing.FindAll(p => Sim.Flat(p.transform.position).magnitude > 8);
            if (ts.Count == 0) ts = Farthest(1);
            for (int i = 0; i < ts.Count; i++)
            {
                float a0 = AngOf(ts[i].transform.position), dir = i % 2 == 1 ? -1 : 1, span = 40 * Deg, start = a0 - dir * span / 2;
                W.Hazards.Spawn(new Hazard
                {
                    Source = "Waterfall Jet", Shape = HazardShape.Cone, Kind = HazardKind.Strike, Origin = Vector3.zero, Yaw = YawOf(a0),
                    Radius = R, HalfAngle = 20, LockAt = 0, ResolveAt = Sim.Ticks(1.5f), Damage = 0, HitOnResolve = false,
                });
                Later(1.5f, () =>
                {
                    float t = 0, acc = 0;
                    var e = new Emitter();
                    e.Update = dt =>
                    {
                        t += dt; if (t > 2) { e.On = false; return; }
                        float a = start + dir * span * (t / 2); acc += dt;
                        while (acc >= 0.07f) { acc -= 0.07f; Shot(On(a, R - 0.1f), a + Mathf.PI, 9, 14, (R - Rim) / 9, 0, "Waterfall Jet"); }
                    };
                    Add(e);
                });
            }
        }

        // Pruning Arc: a 120° cut, 8 m, toward the nearest player (tracks 0.3 s), lands at 2.0 s; the back cut lands 0.8 s
        // later. Each cut throws 9 petals (15° apart) from 8 m out to the wall. The two 60° side slivers are safe.
        void PruningArc()
        {
            presenter.PlayOnce("PruningArc", 2.9f);
            var near = W.NearestStanding(Vector3.zero);
            float a = near ? AngOf(near.transform.position) : Rnd(0, Tau);
            var front = Tune(new Hazard
            {
                Source = "Pruning Arc", Shape = HazardShape.Cone, Kind = HazardKind.Strike, Origin = Vector3.zero, Yaw = YawOf(a),
                Radius = 8, HalfAngle = 60, LockAt = Sim.Ticks(0.3f), ResolveAt = Sim.Ticks(2.0f), Damage = 20,
            });
            front.Track = hz => { if (near && !near.Down) hz.Yaw = YawOf(AngOf(near.transform.position)); };
            var back = Tune(new Hazard
            {
                Source = "Pruning Arc (back)", Shape = HazardShape.Cone, Kind = HazardKind.Strike, Origin = Vector3.zero, Yaw = front.Yaw + 180,
                Radius = 8, HalfAngle = 60, LockAt = Sim.Ticks(0.3f), ResolveAt = Sim.Ticks(2.8f), Damage = 20,
            });
            back.Track = hz => hz.Yaw = front.Yaw + 180;
            Action<Hazard> fan = hz =>
            {
                W.BossFx.SweepResolve(hz.Origin, hz.Yaw, hz.Radius, hz.HalfAngle); Release();
                float c = (90f - hz.Yaw) * Deg;
                for (int i = 0; i < 9; i++) { float b = c - 60 * Deg + i * 15 * Deg; Shot(On(b, 8), b, 6, 12, 6, 0, "Arc Petal"); }
            };
            front.OnResolve = fan; back.OnResolve = fan;
            W.Hazards.Spawn(front); W.Hazards.Spawn(back);
        }

        // ---------------- Phase 2: colours ----------------
        void AssignColours()
        {
            colour.Clear();
            for (int i = 0; i < W.Players.Count; i++) colour[W.Players[i]] = i % 2 == 0 ? Brass : Cobalt;
        }
        void WarnSwap() { swapWarned.Clear(); foreach (var p in W.Players) swapWarned.Add(p); }
        void SwapColours()
        {
            swapWarned.Clear();
            foreach (var p in W.Players) if (colour.TryGetValue(p, out int c)) colour[p] = c == Brass ? Cobalt : Brass;
        }
        void TollBurst()
        {
            float off = Rnd(0, Tau);
            for (int i = 0; i < 20; i++) { float a = off + i * Tau / 20; Shot(On(a, Rim), a, 5, 12, 6, i % 2 == 1 ? Cobalt : Brass, "Toll Burst"); }
        }
        // Choir lanes: bullets cross the whole arena in parallel lanes 2.4 m apart, fired every 0.25 s from the far wall,
        // drifting 0.4 m/s sideways. They stop at `until` seconds into the loop.
        void ChoirLanes(int col, float travel, int drift, float until, float period = 0.25f)
        {
            var d = new Vector3(Mathf.Cos(travel), 0, Mathf.Sin(travel)); var pp = new Vector3(-d.z, 0, d.x);
            float acc = 0, off = 0;
            var e = new Emitter { Looping = true };
            e.Update = dt =>
            {
                if (LoopT >= until) { e.On = false; return; }
                off = (off + drift * 0.4f * dt + 2.4f) % 2.4f; acc += dt;
                while (acc >= period)
                {
                    acc -= period;
                    for (float o = -R + 0.3f + off; o < R; o += 2.4f)
                    {
                        float back = Mathf.Sqrt(Mathf.Max(0, R * R - o * o));
                        Shot(pp * o - d * back, travel, 6, 12, 5, col, col == Brass ? "Brass Lane" : "Cobalt Lane");
                    }
                }
            };
            Add(e);
        }

        // ---------------- Phase 3: Choir Buds ----------------
        // A bud is a boss part 10 m out with its own 3-arm spiral. Standing within 3 m cuts it (graybox rule from the demo:
        // 100 HP at 30/s per player in reach, halved per extra player); left alive 12 s it blooms into 20 petals.
        void PlantBuds(float life)
        {
            int n = BudCount; float a0 = Rnd(0, Tau);
            for (int i = 0; i < n; i++)
            {
                float a = a0 + i * Tau / n;
                if (W.Players.Count == 1) Later(i * 4f, () => SproutBud(a, life)); else SproutBud(a, life);
            }
        }
        void SproutBud(float a, float life)
        {
            var b = new Bud { Pos = On(a, 10), Born = Age, Life = life, Base = Rnd(0, Tau) };
            b.Prop = Prim.Make(PrimitiveKind.Cone, transform.parent, b.Pos + Vector3.up * 0.6f, new Vector3(0.8f, 1.2f, 0.8f), Ivory, 0.8f);
            props.Add(b.Prop);
            props.Add(Prim.Make(PrimitiveKind.Sphere, b.Prop, new Vector3(0, -0.3f, 0), new Vector3(1.6f, 0.5f, 1.6f), new Color(0.30f, 0.42f, 0.24f)));
            b.Timer = W.Hazards.Spawn(new Hazard
            {
                Source = "Choir Bud", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = b.Pos, Radius = 1.0f,
                LockAt = 0, ResolveAt = Sim.Ticks(life), Damage = 0, HitOnResolve = false,
            });
            buds.Add(b);
            var e = new Emitter();
            e.Update = dt =>
            {
                if (!b.Alive) { e.On = false; return; }
                b.Base += 30 * Deg * dt; b.Acc += dt;
                while (b.Acc >= 0.3f) { b.Acc -= 0.3f; for (int i = 0; i < 3; i++) { float aa = b.Base + i * Tau / 3; Shot(b.Pos + On(aa, 0.8f), aa, 4, 10, 4, 0, "Bud Petal"); } }
                int near = 0; foreach (var p in W.Players) if (!p.Down && Sim.Flat(p.transform.position - b.Pos).magnitude < 3) near++;
                if (near > 0) b.Hp -= near * 30 * dt / Mathf.Max(1, near * 0.5f);
                if (b.Hp <= 0)
                {
                    b.Alive = false; BudsCut++; W.Hazards.Cancel(b.Timer);
                    RunHud.Float(b.Pos + Vector3.up * 1.8f, "CUT", Turquoise);
                    W.Fx.Crescendo(b.Pos, true, 0, false);
                    if (b.Prop) b.Prop.gameObject.SetActive(false);
                }
                else if ((Age - b.Born) * Sim.Dt >= b.Life)
                {
                    b.Alive = false; BudsBloomed++;
                    RunHud.Float(b.Pos + Vector3.up * 1.8f, "BLOOM", Amber);
                    PetalRing(b.Pos, 20, 5, Rnd(0, Tau), 0.8f, 12, "Bud Bloom");
                    if (b.Prop) b.Prop.gameObject.SetActive(false);
                }
            };
            Add(e);
        }

        // ---------------- Phase 4: Seed Storm ----------------
        // Rings of big seeds (0.6 m) from her rim at 3 m/s; alternate rings burst after 1.6 s or 2.4 s into 8 petals at
        // 4.5 m/s. Enrage steps apply from the second loop; Last Storm replaces everything from 2:00.
        void StartStorm()
        {
            LastStorm = P4Loop >= 5;
            if (LastStorm) { StormSeeds = 14; StormEvery = 0.6f; sp1 = Spiral(-1, 4, 12 * Deg, 5, 0.15f, 9, 7); }
            else
            {
                int k = Mathf.Min(P4Loop, 4); StormSeeds = 8 + k; StormEvery = 2.0f - 0.1f * k;
                if (k > 0) RunHud.Float(transform.position + Vector3.up * 5.4f, $"The storm thickens: {StormSeeds} seeds", Amber);
                sp1 = Spiral(-1, 2, 8 * Deg, 4.5f, 0.22f, 9, 7);
            }
            if (LastStorm) Order.Add("Last Storm");
            float acc = 2.5f; int ring = 0;
            var e = new Emitter { Looping = true };
            e.Update = dt =>
            {
                acc += dt; if (acc < StormEvery) return;
                acc -= StormEvery; ring++;
                SeedRing(StormSeeds, Rnd(0, Tau), ring % 2 == 1 ? 1.6f : 2.4f);
            };
            Add(e);
        }
        void SeedRing(int n, float off, float fuse)
        {
            for (int i = 0; i < n; i++)
            {
                float a = off + i * Tau / n;
                Shot(On(a, Rim), a, 3, 16, fuse, 0, "Seed", 0.6f, b => PetalRing(b.Pos, 8, 4.5f, Rnd(0, Tau), 0.3f, 12, "Seed Burst"));
            }
        }

        // ---------------- per tick ----------------
        protected override void StepAlways()
        {
            if (presenter) presenter.TickBoss("Idle");
            if (Dead || Current == "idle" || phase == 0) return;   // halted (party down) or not started
            for (int i = later.Count - 1; i >= 0; i--) if (Age >= later[i].at) { var fn = later[i].fn; later.RemoveAt(i); fn(); }
            for (int i = 0; i < emit.Count; i++) if (emit[i].On) emit[i].Update?.Invoke(Sim.Dt);
            emit.RemoveAll(e => !e.On);
        }

        public int PropCount => props.Count;
        void ClearProps() { foreach (var p in props) if (p) Destroy(p.gameObject); props.Clear(); }

        protected override void Update()
        {
            base.Update();
            PlaceLights();
            if (Dead && frontLight)
            {
                frontLight.intensity = Mathf.MoveTowards(frontLight.intensity, FrontIntensity * 0.5f, Time.deltaTime * 2f);
                rimLight.intensity = Mathf.MoveTowards(rimLight.intensity, RimIntensity * 0.3f, Time.deltaTime * 3f);
            }
            if (presenter) presenter.GlowLevel = glowLevel;
            if (bellRoot && !Dead)
                for (int b = 0; b < bellRoot.childCount; b++)
                    bellRoot.GetChild(b).localRotation = Quaternion.Euler(Mathf.Sin(Time.time * (1.3f + b * 0.37f)) * 8f, 0, Mathf.Cos(Time.time * (1.1f + b * 0.29f)) * 6f);
            if (Dead && presenter && presenter.Visual && !presenter.HasModel)
                presenter.Sink = Mathf.MoveTowards(presenter.Sink, 0.9f, Time.deltaTime * 0.6f);
            foreach (var b in buds) if (b.Alive && b.Prop) b.Prop.localScale = new Vector3(0.8f, 1.2f, 0.8f) * (1 + 0.06f * Mathf.Sin(Time.time * 6));
            DrawColourRings();
        }

        // Phase 2: a brass or cobalt ring under each player shows their colour; it flickers to the next colour during the
        // 1 s toll warning.
        void DrawColourRings()
        {
            if (W == null) return;
            foreach (var p in W.Players)
            {
                if (!p) continue;
                colourRings.TryGetValue(p, out var ring);
                int c = Dead ? 0 : ColourOf(p);
                if (c == 0) { if (ring) ring.gameObject.SetActive(false); continue; }
                if (!ring)
                {
                    ring = Prim.Make(PrimitiveKind.Cylinder, transform.parent, Vector3.zero, new Vector3(1.3f, 0.01f, 1.3f), RingBrass, 2f);
                    colourRings[p] = ring;
                }
                if (!ring.gameObject.activeSelf) ring.gameObject.SetActive(true);
                bool flick = swapWarned.Contains(p) && Mathf.Repeat(Time.time * 6, 1) < 0.5f;
                int show = flick ? (c == Brass ? Cobalt : Brass) : c;
                var mr = ring.GetComponent<MeshRenderer>();
                var want = show == Brass ? RingBrass : RingCobalt;
                if (mr.material.GetColor("_BaseColor") != want) { mr.material.SetColor("_BaseColor", want); mr.material.SetColor("_EmissionColor", want * 2f); }
                ring.position = Sim.Flat(p.transform.position) + Vector3.up * 0.04f;
            }
        }

        protected override void OnDefeat()
        {
            foreach (var e in emit) e.On = false;
            emit.Clear(); later.Clear(); buds.Clear(); colour.Clear(); swapWarned.Clear();
            ClearProps();
            glowTarget = 0.3f; glowLevel = 2f;
            presenter.PlayOnce("Defeat", 2.5f);
            W.Fx.Crescendo(transform.position, true, 2, true);
        }
        void OnDestroy()
        {
            ClearProps();
            foreach (var r in colourRings.Values) if (r) Destroy(r.gameObject);
        }
        public CharacterPresenter Presenter => presenter;
    }
}
