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
        // Easy and Hard (accepted, Chzy18 31 Oct; docs DIFFICULTY-EXTRAS.md): Hard adds one mechanic per phase (P1 every pod
        // doubled, P2 double toll burst, P3 one more bud per wave, P4 10-petal seed bursts); Easy warns longer and fires
        // less. The enrage clock is the same on every level; Last Storm is softer on Easy (accepted 31 Oct).
        public Difficulty Level => W != null ? W.Difficulty : Difficulty.Medium;
        bool Easy => Level == Difficulty.Easy;
        bool Hard => Level == Difficulty.Hard;
        public int BurstPetals => Hard ? 10 : 8;
        public int LastBurstFired { get; private set; }        // petals the latest seed burst actually fired (tests)
        public float P1Spin => Easy ? 12 : 15;                 // P1 spiral, °/s
        public float LanePeriod => Easy ? 0.5f : 0.35f;        // P2 choir lanes
        public bool SecondColourSpiral => !Easy;
        public bool DoubleToll => Hard;
        public float BloomAfter => Easy ? 15 : 12;             // P3 buds
        public int HollowArms => Easy ? 3 : 4;
        public float PodLandIn(int ph) => ph == 1 && Easy ? 3.0f : 2.5f;
        public bool PodDoubledIn(int ph) => ph == 1 && Hard;
        public bool MirrorJets => Hard;   // Hard (5 Nov, done as recommended): each jet pours a twin from the opposite wall
        public bool ChoirPods => Hard;    // Hard: Seed Pods on the farthest players at 16.5 s in Phase 2 (coral petals hit every colour)
        public bool CounterSpiral => Hard;   // Hard: a 2-arm spiral turning the other way under the Hollow Spiral, same gaps
        public bool StormArc => Hard;   // Hard: a Pruning Arc at 15 s in each Seed Storm loop (not in Last Storm)

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
        // Her clips (presentation; proposal 2 Nov, ATTACK-VISUALS.md): one-shots go through Act, and the loop under them is
        // Conduct while a spiral runs (Lanes, a downbeat per volley, while P2 lanes fire), Storm in Last Storm, else Idle.
        // Clips the model lacks fall back to Idle (Lanes falls back to Conduct).
        public readonly List<string> Clips = new List<string>();   // one-shots played, for tests
        int actUntil; bool introPlayed;
        bool Has(string clip) => presenter && presenter.Has(clip);   // newer clips need the newest sylvara-assets pack
        void Act(string clip, float seconds)
        {
            Clips.Add(clip); actUntil = Age + Sim.Ticks(seconds);
            if (presenter) presenter.PlayOnce(clip, seconds);
        }
        public string LoopClip => LastStorm ? "StormLoop" : emit.Exists(e => e.On && e.Lane) ? "LanesLoop"
            : (sp1 != null && sp1.On) || (sp2 != null && sp2.On) ? (phase == 3 ? "HollowLoop" : "ConductLoop") : "Idle";   // P3 pours from the open mask
        const float LanesClip = 0.7f;   // LanesLoop is one 0.7 s beat, sped up so each beat lands on a lane volley
        float LoopT => (Age - loopStart) * Sim.Dt;
        public float P4Time => p4Start < 0 ? 0 : (Age - p4Start) * Sim.Dt;   // seconds since Phase 4 began (enrage clock)

        sealed class Emitter { public bool On = true, Looping, Lane; public Action<float> Update; public string Tag; }
        readonly List<Emitter> emit = new List<Emitter>();
        readonly List<(int at, Action fn)> later = new List<(int, Action)>();
        // a doubled pod's second ring between its first ring and its own spawn: the hazard is gone by then, so bots read it here
        readonly List<(Vector3 at, float r0, int fire)> podRings = new List<(Vector3, float, int)>();
        public void PendingPodRings(List<(Vector3 at, float r0, float land, bool twice)> into) { foreach (var p in podRings) if (p.fire >= Age) into.Add((p.at, p.r0, (p.fire - Age) * Sim.Dt, false)); }
        // where a pod's petal ring starts (its circle less PodInset; a pod on her centre starts at her rim)
        public float PodR0(Vector3 origin, float r) => Mathf.Max(r - PodInset, Sim.Flat(origin).sqrMagnitude < 0.01f ? Rim : 0);
        Emitter sp1, sp2;

        sealed class Bud { public Vector3 Pos; public float Hp = BudHp, Life, Base, Acc; public int Born; public bool Alive = true; public Transform Prop; public PropMotion View; public Hazard Timer; public BossPart Part; }
        // Choir Buds are boss parts (CombatWorld.Parts): every class's attacks hit them, and the damage also hits Sylvara.
        // 120 HP: one player cuts one in about 3-4 s (P3 doc).
        public const float BudHp = 120;
        readonly List<Bud> buds = new List<Bud>();
        public int BudsAlive { get { int n = 0; foreach (var b in buds) if (b.Alive) n++; return n; } }
        public float BudHpAt(Vector3 pos) { foreach (var b in buds) if (b.Alive && (b.Pos - pos).sqrMagnitude < 0.01f) return b.Hp; return 0; }   // tests
        // a Choir Bud fires 3 Bud Petals every BudEvery s from BudFrom m out, its aim turning BudTurn rad/s
        public const float BudEvery = 0.3f, BudFrom = 0.8f, BudSpeed = 4f, BudTurn = 30 * Mathf.Deg2Rad;
        // the volleys still to come, for the bots' steer (a petal hits on the tick it appears, too fast to see and dodge):
        // each live bud's position, aim now and seconds to its next volley
        public void BudVolleys(List<(Vector3 pos, float aim, float next)> into) { into.Clear(); foreach (var b in buds) if (b.Alive) into.Add((b.Pos, b.Base, BudEvery - b.Acc)); }
        public List<Vector3> AliveBudPositions() { var l = new List<Vector3>(); foreach (var b in buds) if (b.Alive) l.Add(b.Pos); return l; }
        public void DebugLastStormWarning() => Toll("LAST STORM in 3 s");   // test hook: loop 4's warning, without waiting
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
        // on a LAN client's replica the host's colours (ReplicaPresent) stand in for the fight's own
        public int ColourOf(PlayerController p)
        {
            if (p == null) return 0;
            if (colour.TryGetValue(p, out int c)) return c;
            return netColour.TryGetValue(p, out int n) ? n : 0;
        }
        public bool SwapWarned(PlayerController p) => p != null && (swapWarned.Contains(p) || netWarned.Contains(p));   // the ring is flickering: a toll swaps colours soon

        Transform maskL, maskR, bellRoot, shoulderFlowers;
        readonly List<Transform> props = new List<Transform>();
        // Verdant Chimes' sun sits behind her from the gameplay camera, so her camera-facing side is in shadow: a soft
        // front light lifts it and a turquoise rim edges her silhouette. Presentation only, fixed world offsets.
        Light frontLight, rimLight;
        static readonly Vector3 FrontOffset = new Vector3(0, 3.0f, 2.6f), RimOffset = new Vector3(0, 5.2f, -2.4f);
        const float FrontIntensity = 12f, RimIntensity = 18f;

        void Awake() { MaxHp = PartyHp[1];  BodyRadius = 1.6f; HitHeight = 2.4f; MarkHeight = 4.8f; }

        // HP by fighting party size, about a 3:20 clear at the measured bot damage (HP-SCALING.md, accepted 1 Nov). A table, not
        // PartyStep, so each size can be tuned on its own. Same on every level. The game is 1-4 players (Chzy18, 1 Nov).
        public static readonly int[] PartyHp = { 0, 4600, 9200, 13800, 18400 };
        public static int HpFor(int fighters) => PartyHp[Mathf.Clamp(fighters, 1, PartyHp.Length - 1)];

        static readonly Color Stone = new Color(0.68f, 0.70f, 0.64f), Wood = new Color(0.27f, 0.20f, 0.15f),
            BrassC = new Color(0.55f, 0.47f, 0.25f), Ivory = new Color(0.90f, 0.87f, 0.78f),
            Turquoise = new Color(0.25f, 0.95f, 0.85f), Leaf = new Color(0.30f, 0.42f, 0.24f), Amber = new Color(1f, 0.71f, 0.28f),
            RingBrass = new Color(0.85f, 0.64f, 0.25f), RingCobalt = new Color(0.36f, 0.52f, 1f);

        protected override void Build()
        {
            int fighters = 0; foreach (var p in W.Players) if (!(p.Input is AllyBot) || AllyBot.Attacks) fighters++;   // as BossEncounter.Spawn counts them
            MaxHp = Mathf.Max(1, Mathf.RoundToInt(HpFor(fighters) * TestHpScale)); Hp = MaxHp;
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
            if (presenter && presenter.HasModel)
            {
                maskBoneL = FindDeep(presenter.transform, "mask.L"); maskBoneR = FindDeep(presenter.transform, "mask.R"); maskHead = FindDeep(presenter.transform, "head");
                if (HasMaskBones)
                {
                    maskCore = Prim.Make(PrimitiveKind.Sphere, transform, Vector3.zero, Vector3.one * 0.1f, Turquoise, 1.6f);
                    maskGlow = MakeLight("Mask Core", Turquoise, 0, 2.5f);
                    maskCore.gameObject.SetActive(false); maskGlow.enabled = false;
                }
            }
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

        // practice phase row: start in phase n with its looks at once. phase = 0 makes the script's next tick open that phase
        // the way a fight start does (clean, no shift animation): colours in Phase 2, the enrage clock in Phase 4.
        public override float[] PhaseStarts => new[] { 1f, Thr2, Thr3, Thr4 };
        public override int PhaseNumber => phase == 0 ? PhaseFor() : phase;
        protected override void EnterPhase(int n)
        {
            phase = 0; PhaseName = PhaseNames[n - 1];
            if (shoulderFlowers) shoulderFlowers.gameObject.SetActive(n >= 2);
            if (n >= 3 && maskL) { maskL.localPosition = new Vector3(-0.32f, 3.65f, 0.2f); maskR.localPosition = new Vector3(0.32f, 3.65f, 0.2f); }
            maskTarget = MaskOpen = n >= 3 ? 1 : 0;
        }

        // Second form (P3-P4; done as recommended 2 Nov, review later): the model's mask halves (bones mask.L / mask.R from
        // sylvara_rig.py) swing 16 degrees outward on their outer hinges and slide apart, and a turquoise core glows in the gap.
        // Applied after the presenter has posed her, so every clip keeps the mask open. A model without the bones keeps it shut.
        Transform maskBoneL, maskBoneR, maskHead, maskCore; Light maskGlow; float maskTarget, maskSign;
        public float MaskOpen { get; private set; }
        public bool HasMaskBones => maskBoneL && maskBoneR && maskHead;
        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var f = FindDeep(c, name); if (f) return f; }
            return null;
        }
        void LateUpdate()
        {
            if (!HasMaskBones) return;
            MaskOpen = Mathf.MoveTowards(MaskOpen, maskTarget, Time.deltaTime / 1.5f);
            Vector3 l = maskBoneL.position, r = maskBoneR.position, mid = (l + r) / 2, up = transform.up;
            Vector3 side = Vector3.ProjectOnPlane(l - r, up), fwd = Vector3.ProjectOnPlane(mid - maskHead.position, up).normalized;
            if (maskSign == 0)   // which way round the hinge opens the halves forward and out, whatever the import axes
            {
                var v = Vector3.ProjectOnPlane(mid - l, up);
                maskSign = Vector3.Dot(Quaternion.AngleAxis(10, up) * v, fwd) > Vector3.Dot(v, fwd) ? 1 : -1;
            }
            float deg = 16 * MaskOpen; var slide = side * (0.15f * MaskOpen);   // 9 cm per half on her 0.6 m mask
            maskBoneL.rotation = Quaternion.AngleAxis(maskSign * deg, up) * maskBoneL.rotation; maskBoneL.position = l + slide;
            maskBoneR.rotation = Quaternion.AngleAxis(-maskSign * deg, up) * maskBoneR.rotation; maskBoneR.position = r - slide;
            bool on = MaskOpen > 0.01f;
            maskCore.gameObject.SetActive(on); maskGlow.enabled = on;
            maskCore.position = mid + up * (0.42f * side.magnitude) - fwd * 0.03f; maskCore.localScale = Vector3.one * (0.3f * side.magnitude * MaskOpen);
            maskGlow.transform.position = maskCore.position + fwd * 0.2f; maskGlow.intensity = 2.5f * MaskOpen;
        }

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
            phase = to;
            if (!first || PhaseName != PhaseNames[to - 1]) SetPhase(PhaseNames[to - 1]);   // a practice start already shows its name
            ClearFight();
            if (to == 2) AssignColours();
            if (to == 4) { p4Start = Age + (first ? 0 : TransTicks); P4Loop = 0; LastStorm = false; }
            if (first) yield break;
            Current = "phase shift";
            Act(to == 3 && Has("MaskSplit") ? "MaskSplit" : "PhaseShift", TransTicks / (float)Sim.Hz);   // hands to the face, flung apart as the mask opens
            glowTarget = 3f;
            RunHud.Float(transform.position + Vector3.up * 5.4f, to == 3 ? "The mask splits" : PhaseNames[to - 1], Amber);
            for (int i = 0; i < TransTicks; i++)
            {
                if (i == TransTicks / 2) { W.Fx.Crescendo(transform.position, true, 0, false); Release(1.5f); if (shoulderFlowers) shoulderFlowers.gameObject.SetActive(to >= 2); }
                if (to >= 3) maskTarget = 1;   // the model's mask opens over 1.5 s (LateUpdate)
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
            emit.Clear(); spinDir.Clear(); later.Clear(); podRings.Clear(); sp1 = sp2 = null; HazardTag.Clear(); tagNow = null;
            foreach (var b in buds) KillBud(b);
            buds.Clear();
            W.Bullets.ClearAll();
            W.Hazards.ClearAll();
            colour.Clear(); swapWarned.Clear();
            ClearProps();
            if (NetRecording) NetFx("SylvaraEncounter.Clear");
        }

        List<(float at, Action fn)> Loop(int ph)
        {
            var ev = new List<(float, Action)>();
            void At(float t, string id, Action fn) => ev.Add((t, (Action)(() => { if (id != null) { Order.Add(id); Current = id; } WithTag(id != null && HardAdds.Contains(id) ? id : tagNow, fn); })));
            switch (ph)
            {
                case 1:   // Spiral Bloom
                    At(0, "Petal Spiral", () => sp1 = Spiral(1, 4, P1Spin * Deg, 5, 0.15f, 9, 7));
                    At(3, "Seed Pods", () => PodsOnFar());
                    At(6, "Waterfall Jets", Jets);
                    At(8, "Toll", () => Toll("TOLL: the spin reverses"));
                    At(9, null, () => Reverse(sp1));
                    At(10, "Feet Pod", FeetPod);
                    At(13, "Pruning Arc", PruningArc);
                    At(14.5f, "Doubled Pods", () => PodsOnFar(doubled: true));
                    At(18, "Waterfall Jets", Jets);
                    At(21, "Toll", () => Toll("TOLL: the spin reverses"));
                    At(22, "Second Spiral", () => { Reverse(sp1); sp2 = Spiral(-1, 3, 20 * Deg, 4, 0.18f, 8, 5); });
                    At(23.5f, "Triangle Pods", TrianglePods);
                    At(27, "Pruning Arc", PruningArc);
                    break;
                case 2:   // Two-Colour Choir, harder version; 29 Oct playtest (Chzy18): bullets a little slower and spaced out so you can run between them
                    At(0, "Colour Spiral", () => sp1 = Spiral(1, 6, 14 * Deg, 4.2f, 0.17f, 9, 7, new[] { Brass, Cobalt }));
                    float lane = LanePeriod;   // Easy: lanes every 0.5 s and no second spiral set
                    At(0.01f, "Brass Lanes", () => ChoirLanes(Brass, -Mathf.PI / 2, 1, 10, lane));
                    At(6, "Cobalt Lanes", () => ChoirLanes(Cobalt, Mathf.PI, 1, 20, lane));
                    if (SecondColourSpiral) At(10, "Second Colour Spiral", () => sp2 = Spiral(-1, 4, 18 * Deg, 3.5f, 0.22f, 8, 5, new[] { Cobalt, Brass }));
                    At(12, "Root Ring", () => RootRing(3.5f));
                    At(14.5f, "Brass Lanes", () => ChoirLanes(Brass, Mathf.PI / 2, -1, 20, lane));
                    if (ChoirPods) At(16.5f, "Choir Pods", () => PodsOnFar());   // Hard (5 Nov): lands at 19 s, before the 20 s toll
                    foreach (float t in W.Players.Count == 1 ? new[] { 4f, 8, 12, 16, 20 } : new[] { 5f, 10, 15, 20 })
                    {
                        At(t - SwapWarnFor, "Toll", () => { Toll("TOLL: every colour swaps"); WarnSwap(); });
                        At(t, null, SwapColours);
                        At(t + 0.02f, null, () => { TollBurst(); if (DoubleToll) Later(0.5f, TollBurst); });   // Hard: double toll
                    }
                    break;
                case 3:   // Hollow Bloom (second form)
                    float bloom = BloomAfter;   // Easy: buds bloom after 15 s and the spiral has 3 arms
                    At(0.01f, "Hollow Spiral", () => sp1 = Spiral(1, HollowArms, 10 * Deg, 4.5f, 0.18f, 9, 7));
                    At(1, "Choir Buds", () => PlantBuds(bloom));
                    if (CounterSpiral) At(4, "Counter Spiral", () => sp2 = Spiral(-1, 2, 12 * Deg, 4f, 0.2f, 9, 7));   // Hard (5 Nov)
                    At(8, "Feet Pod", FeetPod);
                    At(12, "Choir Buds", () => PlantBuds(bloom));
                    At(15, "Pruning Arc", PruningArc);
                    At(20, "Seed Pods", () => PodsOnFar());
                    break;
                default:  // Seed Storm, with the enrage steps
                    At(0, "Seed Storm", StartStorm);
                    At(6, "Seed Pods", () => PodsOnFar());
                    At(12, "Root Ring", () => RootRing(3.5f));
                    if (StormArc && P4Loop < 5) At(15, "Storm Arc", PruningArc);   // Hard (5 Nov): both cuts land by 17.8 s, before the 18 s pods
                    At(18, "Seed Pods", () => PodsOnFar());
                    if (P4Loop == 4) At(LastStormAt - 4 * 24 - 3, "Toll", () => Toll("LAST STORM in 3 s"));
                    break;
            }
            return ev;
        }

        // ---------------- helpers ----------------
        // who her aimed attacks may pick: standing and not reviving (revive rule, accepted 4 Nov: no mechanic aims at a reviver;
        // a reviver standing in a hazard is still hit). Every target picker below starts here, with an unaimed fallback.
        List<PlayerController> Standing => W.Players.FindAll(p => !p.Down && p.Reviving == null);
        static Vector3 On(float a, float r) => new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
        static float AngOf(Vector3 v) => Mathf.Atan2(v.z, v.x);
        static float YawOf(float a) => 90f - a * Mathf.Rad2Deg;   // floor angle -> Sim yaw (degrees, 0 = +z)
        float Rim => BodyRadius;
        float R => CombatWorld.PlayerBound;
        void Later(float seconds, Action fn) { var tg = tagNow; later.Add((Age + Mathf.Max(1, Sim.Ticks(seconds)), tg == null ? fn : () => WithTag(tg, fn))); }
        Emitter Add(Emitter e) { e.Tag ??= tagNow; emit.Add(e); return e; }
        // hit bookkeeping for the sweep (6 Nov): bullets and hazards made by a Hard addition carry its name (Bullet.Tag,
        // HazardTag), so the clear sweep can count hits per addition. Bookkeeping only: nothing in the fight reads it.
        static readonly HashSet<string> HardAdds = new HashSet<string> { "Choir Pods", "Counter Spiral", "Storm Arc" };
        public const string MirrorTag = "Mirrored Jets";
        string tagNow;
        public readonly Dictionary<Hazard, string> HazardTag = new Dictionary<Hazard, string>();
        void WithTag(string tg, Action fn)
        {
            if (tg == null) { fn(); return; }
            var k = tagNow; tagNow = tg; int n0 = W.Hazards.Live.Count;
            try { fn(); }
            finally
            {
                var live = W.Hazards.Live;
                for (int i = n0; i < live.Count; i++)
                {
                    var h = live[i]; if (HazardTag.ContainsKey(h)) continue; HazardTag[h] = tg;
                    var res = h.OnResolve; if (res != null) h.OnResolve = hz => WithTag(tg, () => res(hz));
                }
                tagNow = k;
            }
        }
        BulletSystem.Bullet Shot(Vector3 at, float a, float v, int dmg = 12, float life = 6, int col = 0, string src = "Petal", float r = BulletArt.PetalHitRadius, Action<BulletSystem.Bullet> split = null)
            { var b = W.Bullets.Fire(at, a, v, dmg, life, r, col, src, split); b.AttackId = AttackOf(src); b.Tag = tagNow; return b; }
        // the boss attack icon (BossAttackIcons, approved 2 Nov) a bullet or hazard belongs to. Sources repeat across phases, so
        // the spiral is Two-Colour Choir in Phase 2 and the spiral and seeds are Last Storm once it starts. Choir Bud (the
        // harmless bloom timer) has none.
        public string AttackOf(string src)
        {
            if (LastStorm && (src == "Petal Spiral" || src == "Seed" || src == "Seed Burst")) return "sylvara.last_storm";
            switch (src)
            {
                case "Petal Spiral": return phase == 2 ? "sylvara.two_colour_choir" : "sylvara.spiral_bloom";
                case "Brass Lane": case "Cobalt Lane": return "sylvara.two_colour_choir";
                case "Seed Pod": case "Feet Pod": case "Pod Petal": return "sylvara.seed_pods";
                case "Waterfall Jet": return "sylvara.waterfall_jets";
                case "Pruning Arc": case "Pruning Arc (back)": case "Arc Petal": return "sylvara.pruning_arc";
                case "Toll Burst": return "sylvara.toll_burst";
                case "Root Ring": return "sylvara.root_ring";
                case "Bud Petal": return "sylvara.hollow_bloom";
                case "Bud Bloom": return "sylvara.bud_bloom";
                case "Seed": case "Seed Burst": return "sylvara.seed_storm";
                default: return null;
            }
        }
        void PetalRing(Vector3 c, int n, float v, float off, float r0, int dmg = 12, string src = "Petal")
        {
            for (int i = 0; i < n; i++) { float a = off + i * Tau / n; Shot(c + On(a, r0), a, v, dmg, 6, 0, src); }
        }
        static float Rnd(float a, float b) => UnityEngine.Random.Range(a, b);
        static readonly System.Random look = new System.Random(31);   // presentation only, so props never shift the fight's rolls
        static float LookRnd(float a, float b) => a + (float)look.NextDouble() * (b - a);
        Hazard Tune(Hazard h) { h.Stun = Stun; h.Knockback = 0; h.AttackId ??= AttackOf(h.Source); return h; }
        int FarCount => W.Players.Count <= 2 ? 1 : 2;   // 1-4 players
        public int BudCount => (W.Players.Count <= 2 ? 2 : 3) + (Hard ? 1 : 0);   // Hard: one more bud
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
            if (text.StartsWith("LAST STORM") && Has("StormCall")) Act("StormCall", 3f);   // she gathers and rises into StormLoop
            else Act("Toll", 1.4f);   // the lurch lands 1.0 s in, on the reversal / colour swap
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
        // 0.6 s later rotated half a step into the first ring's gaps. Phase 1 pods: every one doubled on Hard, 3.0 s on Easy.
        // its petals start PodInset inside the circle at PodPetalSpeed m/s; a doubled pod fires its second ring PodSecondAfter s later
        public const float PodInset = 0.3f, PodPetalSpeed = 5.5f, PodSecondAfter = 0.6f;
        void Pod(PlayerController target, Vector3 at, float r = 2.5f, bool doubled = false)
        {
            const int n = 12; float off = Rnd(0, Tau);
            doubled |= PodDoubledIn(phase);
            var h = Tune(new Hazard
            {
                Source = target ? "Seed Pod" : "Feet Pod", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Radius = r,
                LockAt = target ? Sim.Ticks(0.6f) : 0, ResolveAt = Sim.Ticks(PodLandIn(phase)), Damage = 16,
                Origin = target ? Sim.Flat(target.transform.position) : at,
            });
            if (target) h.Track = hz => { if (target && !target.Down) hz.Origin = Sim.Flat(target.transform.position); };
            h.OnResolve = hz =>
            {
                W.BossFx.ChimeResolve(hz.Origin, hz.Radius); Release(0.5f);
                float r0 = PodR0(hz.Origin, r);
                PetalRing(hz.Origin, n, PodPetalSpeed, off, r0, 12, "Pod Petal");
                if (doubled)
                {
                    var ring = (hz.Origin, r0, Age + Mathf.Max(1, Sim.Ticks(PodSecondAfter)));   // the tick Later fires on
                    podRings.Add(ring);
                    Later(PodSecondAfter, () => { podRings.Remove(ring); PetalRing(ring.Item1, n, PodPetalSpeed, off + Mathf.PI / n, ring.Item2, 12, "Pod Petal"); });
                }
            };
            // the pod model drops onto the warning in its last 0.4 s and lands on the resolve (proposal art)
            var pv = SylvaraProps.Spawn(SylvaraProps.Pod, transform.parent, h.Origin, LookRnd(0, 360));
            if (pv) { PodModels++; props.Add(pv.transform); pv.Follow = () => h.Origin; pv.FallIn(PodLandIn(phase)); }
            if (NetRecording) NetFx("SylvaraEncounter.Pod", h.Origin, PodLandIn(phase));   // LAN: the replica drops its own pod model
            var land = h.OnResolve;
            h.OnResolve = hz => { land(hz); if (pv) pv.Land(); if (NetRecording) NetFx("SylvaraEncounter.PodLand", hz.Origin); };
            W.Hazards.Spawn(h);
        }
        void PodsOnFar(bool doubled = false)
        {
            Act("Seed", 1.2f);
            if (W.Players.Count == 1) { var p = W.Players[0]; if (!p.Down) Pod(p, Vector3.zero, 2.5f, doubled); return; }
            var far = Farthest(FarCount);
            if (far.Count == 0) Pod(null, On(Rnd(0, Tau), 7), 2.5f, doubled);   // everyone standing is reviving: an unaimed pod
            foreach (var p in far) Pod(p, Vector3.zero, 2.5f, doubled);
        }
        // the pods without a cast of their own reuse her Seed clip (graphics audit 8 Nov; presentation only)
        void FeetPod() { Act("Seed", 1.2f); Pod(null, Vector3.zero, 3.0f); }
        void TrianglePods()
        {
            Act("Seed", 1.2f);
            var f = Farthest(1); float a0 = f.Count > 0 ? AngOf(f[0].transform.position) : Rnd(0, Tau);
            for (int i = 0; i < 3; i++)
            {
                var at = On(a0 + i * Tau / 3, 7);
                if (W.Players.Count == 1) Later(i * 0.8f, () => Pod(null, at)); else Pod(null, at);
            }
        }
        void RootRing(float r)
        {
            Act("Rootscript", 2f);
            var h = Tune(new Hazard { Source = "Root Ring", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Radius = r, LockAt = 0, ResolveAt = Sim.Ticks(2), Damage = 18, Origin = Vector3.zero });
            h.OnResolve = hz => { W.BossFx.ChimeResolve(hz.Origin, hz.Radius); Release(); RootsErupt(hz.Origin, hz.Radius); };
            W.Hazards.Spawn(h);
        }
        // Root Ring look (proposal art): root pieces burst up inside the disc around her feet after it resolves, hold 0.3 s
        // and sink, so the warning is never covered.
        public const int RootPieces = 10;
        public int PodModels, RootModels, BudModels;   // tests: prop models placed (0 when the art isn't installed)
        public void DebugJets() => Jets();   // tests: one round of Waterfall Jets
        public void DebugRoots() => RootsErupt(Vector3.zero, 3.5f);   // tests: the Root Ring's look alone, for a screenshot
        void RootsErupt(Vector3 c, float r)
        {
            if (NetRecording) NetFx("SylvaraEncounter.Roots", c, r);   // LAN: the replica raises its own roots
            float a0 = LookRnd(0, Tau);
            for (int i = 0; i < RootPieces; i++)
            {
                float a = a0 + i * Tau / RootPieces, rr = r * (i % 2 == 0 ? 0.78f : 0.62f);
                var p = SylvaraProps.Spawn(SylvaraProps.Root, transform.parent, c + On(a, rr), 90 - a / Deg + LookRnd(-20, 20), LookRnd(0.9f, 1.2f));
                if (!p) return;
                RootModels++; props.Add(p.transform); p.Rise(0.3f);
            }
        }

        // Waterfall Jets: one per player past 8 m (else the farthest). The wedge warns 1.5 s; then a solid stream (a drop
        // every 0.07 s at 9 m/s) pours from the wall to her rim while its source slides 40° along the wall over 2 s,
        // starting 20° before the target.
        // Hard: each jet also pours from the opposite wall (its own wedge warning), sliding the other way.
        public float JetWarn => Easy ? 2.0f : 1.5f;   // Easy: 2.0 s
        void Jets()
        {
            float warn = JetWarn;
            Act("CallWater", warn * 1.9f / 1.5f);   // the hand pulls down (1.5 of the clip's 1.9 s) as the water pours
            var ts = Standing.FindAll(p => Sim.Flat(p.transform.position).magnitude > 8);
            if (ts.Count == 0) ts = Farthest(1);
            var aims = new List<float>(); foreach (var p in ts) aims.Add(AngOf(p.transform.position));
            if (aims.Count == 0) aims.Add(Rnd(0, Tau));   // everyone standing is reviving: one unaimed jet
            int baseN = aims.Count;
            if (MirrorJets) for (int i = 0, n = aims.Count; i < n; i++) aims.Add(aims[i] + Mathf.PI);   // Hard twins
            for (int i = 0; i < aims.Count; i++)
            {
                int k = i < baseN ? i : i - baseN + 1;   // a twin slides opposite to its jet
                var tg0 = tagNow; if (i >= baseN) tagNow = MirrorTag;   // hit bookkeeping only
                float a0 = aims[i], dir = k % 2 == 1 ? -1 : 1, span = 40 * Deg, start = a0 - dir * span / 2;
                W.Hazards.Spawn(new Hazard
                {
                    Source = "Waterfall Jet", AttackId = AttackOf("Waterfall Jet"), Shape = HazardShape.Cone, Kind = HazardKind.Strike, Origin = Vector3.zero, Yaw = YawOf(a0),
                    Radius = R, HalfAngle = 20, LockAt = 0, ResolveAt = Sim.Ticks(warn), Damage = 0, HitOnResolve = false,
                });
                Later(warn, () =>
                {
                    float t = 0, acc = 0;
                    var e = new Emitter();
                    e.Update = dt =>
                    {
                        t += dt; if (t > 2) { e.On = false; return; }
                        float a = start + dir * span * (t / 2); acc += dt;
                        while (acc >= 0.07f) { acc -= 0.07f; Shot(On(a, R - 0.1f), a + Mathf.PI, 9, 14, (R - Rim) / 9, 0, "Waterfall Jet", BulletArt.DropHitRadius); }
                    };
                    Add(e);
                });
                tagNow = tg0;
            }
        }

        // Pruning Arc: a 120° cut, 8 m, toward the nearest player (tracks 0.3 s), lands at 2.0 s; the back cut lands 0.8 s
        // later. Each cut throws 9 petals (15° apart) from 8 m out to the wall. The two 60° side slivers are safe.
        public float ArcExtra => Easy ? 0.5f : 0;   // Easy: both cuts land 0.5 s later (2.5 / 3.3 s)
        void PruningArc()
        {
            float w = ArcExtra;
            Act("PruningArc", 2.9f);
            PlayerController near = null; foreach (var p in Standing) if (!near || Sim.Flat(p.transform.position).sqrMagnitude < Sim.Flat(near.transform.position).sqrMagnitude) near = p;
            float a = near ? AngOf(near.transform.position) : Rnd(0, Tau);
            var front = Tune(new Hazard
            {
                Source = "Pruning Arc", Shape = HazardShape.Cone, Kind = HazardKind.Strike, Origin = Vector3.zero, Yaw = YawOf(a),
                Radius = 8, HalfAngle = 60, LockAt = Sim.Ticks(0.3f), ResolveAt = Sim.Ticks(2.0f + w), Damage = 20,
            });
            front.Track = hz => { if (near && !near.Down) hz.Yaw = YawOf(AngOf(near.transform.position)); };
            var back = Tune(new Hazard
            {
                Source = "Pruning Arc (back)", Shape = HazardShape.Cone, Kind = HazardKind.Strike, Origin = Vector3.zero, Yaw = front.Yaw + 180,
                Radius = 8, HalfAngle = 60, LockAt = Sim.Ticks(0.3f), ResolveAt = Sim.Ticks(2.8f + w), Damage = 20,
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
        void WarnSwap() { swapWarned.Clear(); foreach (var p in W.Players) swapWarned.Add(p); swapDue = W.Tick + Sim.Ticks(SwapWarnFor); }
        public const float SwapWarnFor = 1f;   // the toll warns this long before the swap (At(t - 1) / At(t) above)
        int swapDue;
        // seconds until a warned swap lands (bots: their own colour hurts until then, the other one after)
        public float SwapIn => Mathf.Max(0, (swapDue - W.Tick) * Sim.Dt);
        void SwapColours()
        {
            swapWarned.Clear();
            foreach (var p in W.Players) if (colour.TryGetValue(p, out int c)) colour[p] = c == Brass ? Cobalt : Brass;
        }
        void TollBurst()
        {
            float off = Rnd(0, Tau);
            for (int i = 0; i < 20; i++) { float a = off + i * Tau / 20; Shot(On(a, Rim), a, 4.5f, 12, 6, i % 2 == 1 ? Cobalt : Brass, "Toll Burst"); }
        }
        // Choir lanes: bullets cross the whole arena at 5 m/s in parallel lanes 3.4 m apart, fired every 0.35 s from the far wall,
        // drifting 0.4 m/s sideways (29 Oct playtest: was 6 m/s, 2.4 m and 0.25 s). They stop at `until` seconds into the loop.
        const float LaneGap = 3.4f;
        void ChoirLanes(int col, float travel, int drift, float until, float period = 0.35f)
        {
            var d = new Vector3(Mathf.Cos(travel), 0, Mathf.Sin(travel)); var pp = new Vector3(-d.z, 0, d.x);
            float acc = 0, off = 0;
            var e = new Emitter { Looping = true, Lane = true };
            e.Update = dt =>
            {
                if (LoopT >= until) { e.On = false; return; }
                off = (off + drift * 0.4f * dt + LaneGap) % LaneGap; acc += dt;
                while (acc >= period)
                {
                    acc -= period;
                    for (float o = -R + 0.3f + off; o < R; o += LaneGap)
                    {
                        float back = Mathf.Sqrt(Mathf.Max(0, R * R - o * o));
                        Shot(pp * o - d * back, travel, 5, 12, 6, col, col == Brass ? "Brass Lane" : "Cobalt Lane");
                    }
                }
            };
            Add(e);
        }

        // ---------------- Phase 3: Choir Buds ----------------
        // A bud is a boss part 10 m out with its own 3-arm spiral. Class damage cuts it (BudHp); each point dealt to a bud
        // also hits Sylvara, so cutting buds keeps the damage going. Left alive 12 s (Easy 15) it blooms into 20 petals.
        void PlantBuds(float life)
        {
            int n = BudCount; float a0 = Rnd(0, Tau);
            Act("Bloom", 2f);   // one downbeat per bud
            for (int i = 0; i < n; i++)
            {
                float a = a0 + i * Tau / n;
                if (W.Players.Count == 1) Later(i * 4f, () => SproutBud(a, life)); else SproutBud(a, life);
            }
        }
        // the bell-flower model (proposal art), facing her; the cone and moss stand-in when the art isn't installed
        Transform MakeBudView(Vector3 pos, out PropMotion view)
        {
            view = SylvaraProps.Spawn(SylvaraProps.Bud, transform.parent, pos, Mathf.Atan2(-pos.x, -pos.z) / Deg);
            if (view) { BudModels++; props.Add(view.transform); view.Sprout(); return view.transform; }
            var prop = Prim.Make(PrimitiveKind.Cone, transform.parent, pos + Vector3.up * 0.6f, new Vector3(0.8f, 1.2f, 0.8f), Ivory, 0.8f);
            props.Add(prop);
            props.Add(Prim.Make(PrimitiveKind.Sphere, prop, new Vector3(0, -0.3f, 0), new Vector3(1.6f, 0.5f, 1.6f), new Color(0.30f, 0.42f, 0.24f)));
            return prop;
        }
        static void EndBudView(PropMotion view, Transform prop, bool bloom)
        {
            if (view) { if (bloom) view.Bloom(); else view.Wither(); }
            else if (prop) prop.gameObject.SetActive(false);
        }
        static void PulseBud(Transform prop) { if (prop) prop.localScale = new Vector3(0.8f, 1.2f, 0.8f) * (1 + 0.06f * Mathf.Sin(Time.time * 6)); }
        void SproutBud(float a, float life)
        {
            var b = new Bud { Pos = On(a, 10), Born = Age, Life = life, Base = Rnd(0, Tau) };
            b.Prop = MakeBudView(b.Pos, out b.View);
            if (NetRecording) NetFx("SylvaraEncounter.Bud", b.Pos, life);   // LAN: the replica grows its own bud view
            b.Timer = W.Hazards.Spawn(new Hazard
            {
                Source = "Choir Bud", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = b.Pos, Radius = 1.0f,
                LockAt = 0, ResolveAt = Sim.Ticks(life), Damage = 0, HitOnResolve = false,
            });
            b.Part = new BossPart { Name = "Choir Bud", Pos = b.Pos, BodyRadius = 0.9f, HitHeight = 0.8f };
            b.Part.TakeHit = (dmg, contact) =>
            {
                if (!b.Alive) return 0;
                int dealt = Mathf.Min(dmg, Mathf.CeilToInt(b.Hp)); b.Hp -= dealt;
                RunHud.Float(contact + Vector3.up * 0.5f, dealt.ToString(), Turquoise);
                if (Solid && dealt > 0) TakeHit(dealt, transform.position + Vector3.up * HitHeight, false);
                return dealt;
            };
            W.Parts.Add(b.Part);
            buds.Add(b);
            var e = new Emitter();
            e.Update = dt =>
            {
                if (!b.Alive) { e.On = false; return; }
                if (b.View) b.View.Open = Mathf.Clamp01((Age - b.Born) * Sim.Dt / b.Life);
                b.Base += BudTurn * dt; b.Acc += dt;
                while (b.Acc >= BudEvery) { b.Acc -= BudEvery; for (int i = 0; i < 3; i++) { float aa = b.Base + i * Tau / 3; Shot(b.Pos + On(aa, BudFrom), aa, BudSpeed, 10, 4, 0, "Bud Petal"); } }
                if (b.Hp <= 0)
                {
                    KillBud(b); BudsCut++; W.Hazards.Cancel(b.Timer);
                    if (Age >= actUntil && Has("Stagger")) Act("Stagger", 0.8f);   // she flinches when a bud is pruned; never mid-cast
                    RunHud.Float(b.Pos + Vector3.up * 1.8f, "CUT", Turquoise);
                    W.Fx.Crescendo(b.Pos, true, 0, false);
                    EndBudView(b.View, b.Prop, false);
                    if (NetRecording) NetFx("SylvaraEncounter.BudEnd", b.Pos, false);
                }
                else if ((Age - b.Born) * Sim.Dt >= b.Life)
                {
                    KillBud(b); BudsBloomed++;
                    RunHud.Float(b.Pos + Vector3.up * 1.8f, "BLOOM", Amber);
                    PetalRing(b.Pos, 20, 5, Rnd(0, Tau), 0.8f, 12, "Bud Bloom");
                    if (Age >= actUntil) Act("Bloom", 2f);   // she conducts the bloom (reuses Bloom; graphics audit 8 Nov); never mid-cast
                    EndBudView(b.View, b.Prop, true);
                    if (NetRecording) NetFx("SylvaraEncounter.BudEnd", b.Pos, true);
                }
            };
            Add(e);
        }

        // ---------------- Phase 4: Seed Storm ----------------
        // Rings of big seeds (0.6 m) from her rim at 3 m/s; alternate rings burst after 1.6 s or 2.4 s into 8 petals (Hard 10) at
        // 4.5 m/s. Enrage steps apply from the second loop; Last Storm replaces everything from 2:00.
        void StartStorm()
        {
            LastStorm = P4Loop >= 5;
            if (LastStorm)
            {
                // Easy (accepted 31 Oct): 12 seeds every 0.8 s and a 3-arm spiral, so it downs a player in about 18 s, not 5
                if (Easy) { StormSeeds = 12; StormEvery = 0.8f; sp1 = Spiral(-1, 3, 10 * Deg, 4.5f, 0.15f, 9, 7); }
                else { StormSeeds = 14; StormEvery = 0.6f; sp1 = Spiral(-1, 4, 12 * Deg, 5, 0.15f, 9, 7); }
            }
            else
            {
                int k = Mathf.Min(P4Loop, 4); StormSeeds = (Easy ? 6 : 8) + k; StormEvery = (Easy ? 2.5f : 2.0f) - 0.1f * k;   // Easy: 6 every 2.5 s, same steps
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
            if (!LastStorm && Age >= actUntil) Act("Sow", 0.6f);   // a short flick between other moves; Last Storm keeps Storm

            for (int i = 0; i < n; i++)
            {
                float a = off + i * Tau / n;
                // its burst angle is rolled at launch, not at the burst, so its last 0.6 s can show the exact rays (BulletSystem
                // preview; proposal 7 Nov, Seed Burst fairness review): same petals, speed, damage, fuse and schedule
                float burstOff = Rnd(0, Tau); int rays = BurstPetals;   // both fixed at launch: the burst always matches its preview
                var seed = Shot(On(a, Rim), a, 3, 16, fuse, 0, "Seed", 0.6f, b =>
                {
                    int f0 = W.Bullets.Fired;
                    PetalRing(b.Pos, rays, 4.5f, burstOff, BulletSystem.BurstFrom, 12, "Seed Burst");
                    LastBurstFired = W.Bullets.Fired - f0;
                });
                seed.BurstRays = rays; seed.BurstOff = burstOff; seed.BurstSpeed = 4.5f;
            }
        }

        // ---------------- per tick ----------------
        protected override void StepAlways()
        {
            string loop = Dead || phase == 0 ? "Idle" : LoopClip;
            if ((loop == "LanesLoop" || loop == "HollowLoop") && !Has(loop)) loop = "ConductLoop";   // older packs
            if (presenter) presenter.TickBoss(loop, loop == "LanesLoop" ? LanesClip / LanePeriod : 1);
            if (Dead || Current == "idle" || phase == 0) return;   // halted (party down) or not started
            for (int i = later.Count - 1; i >= 0; i--) if (Age >= later[i].at) { var fn = later[i].fn; later.RemoveAt(i); fn(); }
            for (int i = 0; i < emit.Count; i++) if (emit[i].On) { var e = emit[i]; var k = tagNow; tagNow = e.Tag; e.Update?.Invoke(Sim.Dt); tagNow = k; }
            emit.RemoveAll(e => { if (e.On) return false; spinDir.Remove(e); return true; });   // retired spirals drop their reversal hook
        }

        void KillBud(Bud b) { b.Alive = false; if (b.Part != null) { b.Part.Alive = false; W.Parts.Remove(b.Part); } }

        public int PropCount => props.Count;
        void ClearProps() { foreach (var p in props) if (p) Destroy(p.gameObject); props.Clear(); }

        protected override void Update()
        {
            base.Update();
            // the run's 2 s intro card, before the sim runs (so not in StepAlways): she wakes and lifts the fork
            if (phase == 0 && !introPlayed && RunDirector.Instance != null && RunDirector.Instance.State == RunState.Intro && Has("Intro"))
            { introPlayed = true; Clips.Add("Intro"); presenter.PlayOnce("Intro", 2f); }
            PresentLook(glowLevel, Dead, Time.deltaTime);
            foreach (var b in buds) if (b.Alive) PulseBud(b.Prop);
            DrawColourRings();
        }
        // lights, glow, bell sway and the stand-in's defeat sink: the host from its own state (Update), a LAN client's
        // replica from the host's (ReplicaPresent)
        void PresentLook(float glow, bool dead, float dt)
        {
            PlaceLights();
            if (dead && frontLight)
            {
                frontLight.intensity = Mathf.MoveTowards(frontLight.intensity, FrontIntensity * 0.5f, dt * 2f);
                rimLight.intensity = Mathf.MoveTowards(rimLight.intensity, RimIntensity * 0.3f, dt * 3f);
            }
            if (presenter) presenter.GlowLevel = glow;
            if (bellRoot && !dead)
                for (int b = 0; b < bellRoot.childCount; b++)
                    bellRoot.GetChild(b).localRotation = Quaternion.Euler(Mathf.Sin(Time.time * (1.3f + b * 0.37f)) * 8f, 0, Mathf.Cos(Time.time * (1.1f + b * 0.29f)) * 6f);
            if (dead && presenter && presenter.Visual && !presenter.HasModel)
                presenter.Sink = Mathf.MoveTowards(presenter.Sink, 0.9f, dt * 0.6f);
        }

        // Phase 2: an open dashed brass or cobalt ring under each player shows their colour (friendly cues are thin and
        // unfilled, so it never reads as a boss floor warning); it flickers to the next colour during the 1 s toll warning.
        // One built mesh per player (graphics audit 8 Nov: it was 14 cylinder dots): 14 flat arc dashes on a 0.7 m ring and a
        // faint hairline inside them, so the colour reads as one ring; it turns slowly. Presentation only.
        public const int RingDots = 14;
        const float RingSpin = 25f;   // degrees per second
        Material ringBrassMat, ringCobaltMat;   // shared by every ring
        static Mesh colourRingMesh;
        public static Mesh ColourRingMesh
        {
            get
            {
                if (colourRingMesh) return colourRingMesh;
                float span = 360f / RingDots;
                var parts = new CombineInstance[RingDots + 1];
                for (int k = 0; k < RingDots; k++)
                    parts[k] = new CombineInstance { mesh = VFX.FxMeshes.Sector(0.64f, 0.76f, (span - 9f) / 2, 6), transform = Matrix4x4.Rotate(Quaternion.Euler(0, k * span, 0)) };
                parts[RingDots] = new CombineInstance { mesh = VFX.FxMeshes.Annulus(0.6f, 0.615f, 72), transform = Matrix4x4.identity };
                var m = new Mesh { name = "Sylvara_ColourRing" }; m.CombineMeshes(parts, true, true);
                foreach (var p in parts) Destroy(p.mesh);
                var n = new Vector3[m.vertexCount]; for (int i = 0; i < n.Length; i++) n[i] = Vector3.up; m.normals = n;
                m.RecalculateBounds();
                return colourRingMesh = m;
            }
        }
        void DrawColourRings()
        {
            if (W == null) return;
            int shown = 0;
            foreach (var p in W.Players)
            {
                if (!p) continue;
                colourRings.TryGetValue(p, out var ring);
                int c = Dead ? 0 : ColourOf(p);
                if (c == 0) { if (ring) ring.gameObject.SetActive(false); continue; }
                if (!ring)
                {
                    ring = new GameObject("ColourRing").transform; ring.SetParent(transform.parent, false);
                    ring.gameObject.AddComponent<MeshFilter>().sharedMesh = ColourRingMesh;
                    var rr = ring.gameObject.AddComponent<MeshRenderer>();
                    rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rr.receiveShadows = false;
                    colourRings[p] = ring;
                }
                if (!ring.gameObject.activeSelf) ring.gameObject.SetActive(true);
                bool flick = SwapWarned(p) && Mathf.Repeat(Time.time * 6, 1) < 0.5f;
                int show = flick ? (c == Brass ? Cobalt : Brass) : c;
                if (!ringBrassMat) { ringBrassMat = Prim.Material(RingBrass, 1.5f); ringCobaltMat = Prim.Material(RingCobalt, 1.5f); }
                var want = show == Brass ? ringBrassMat : ringCobaltMat;
                foreach (var mr in ring.GetComponentsInChildren<MeshRenderer>()) if (mr.sharedMaterial != want) mr.sharedMaterial = want;
                ring.position = Sim.Flat(p.transform.position) + Vector3.up * 0.04f;
                ring.rotation = Quaternion.Euler(0, Time.time * RingSpin, 0);
                shown++;
            }
            ColourRingsShown = shown;
        }

        // ---------------- LAN looks (the replica on a client is never stepped) ----------------
        // Glow = body glow. Look: [0] phase (0-4), [1] the stand-in mask's half-gap (m), [2] shoulder flowers (1/0),
        // [3] n, then n triples (lobby id, colour 0 none / 1 brass / 2 cobalt, toll warning 1/0). Pods, roots and buds are
        // recorded calls (SylvaraEncounter.Pod / PodLand / Roots / Bud / BudEnd / Clear) replayed in ReplayNetEffect; the
        // replica keeps its own views of them and drives them every frame here.
        public int ColourRingsShown { get; private set; }   // colour rings drawn this frame (host and replica)
        public int ReplicaPhase { get; private set; }       // LAN client: the phase the replica draws
        public int NetColourRingsMax { get; private set; }  // LAN client: most colour rings shown at once
        public int NetPods { get; private set; }            // LAN client: replayed prop calls, by kind
        public int NetPodLands { get; private set; }
        public int NetRoots { get; private set; }
        public int NetBuds { get; private set; }
        public int NetBudEnds { get; private set; }
        public int NetBudsShown => netBuds.Count;
        readonly Dictionary<PlayerController, int> netColour = new Dictionary<PlayerController, int>();
        readonly HashSet<PlayerController> netWarned = new HashSet<PlayerController>();
        sealed class NetBud { public Vector3 Pos; public float Age, Life; public PropMotion View; public Transform Prop; }
        readonly List<NetBud> netBuds = new List<NetBud>();
        readonly List<PropMotion> netPods = new List<PropMotion>();
        const float MaskClosed = 0.13f;

        public override void CaptureNetLook(Networking.LanWorldFrame.BossState into)
        {
            into.Glow = glowLevel;
            into.Look.Add(phase);
            into.Look.Add(maskL ? Mathf.Abs(maskL.localPosition.x) : MaskClosed);
            into.Look.Add(shoulderFlowers && shoulderFlowers.gameObject.activeSelf ? 1 : 0);
            var id = NetIdOf; int at = into.Look.Count; into.Look.Add(0);
            if (id == null || W == null) return;
            int n = 0;
            foreach (var p in W.Players)
            {
                int c = ColourOf(p); if (c == 0) continue;
                int pid = id(p); if (pid < 0) continue;
                if (into.Look.Count + 3 > Networking.LanWorldFrame.MaxLook) break;
                into.Look.Add(pid); into.Look.Add(c); into.Look.Add(swapWarned.Contains(p) ? 1 : 0); n++;
            }
            into.Look[at] = n;
        }

        public override void ReplicaPresent(Networking.LanWorldFrame.BossState state, float dt)
        {
            var L = state.Look;
            ReplicaPhase = L.Count > 0 ? Mathf.Clamp(Mathf.RoundToInt(L[0]), 0, 4) : 0;
            PresentLook(state.Glow, state.Dead, dt);
            if (L.Count > 1 && maskL && maskR)
            {
                float half = Mathf.Clamp(L[1], 0, 1), x = Mathf.MoveTowards(Mathf.Abs(maskL.localPosition.x), half, dt * 0.8f);
                maskL.localPosition = new Vector3(-x, 3.65f, 0.2f); maskR.localPosition = new Vector3(x, 3.65f, 0.2f);
            }
            if (L.Count > 2 && shoulderFlowers) { bool on = L[2] > 0.5f; if (shoulderFlowers.gameObject.activeSelf != on) shoulderFlowers.gameObject.SetActive(on); }
            // the host's colours, by lobby id, on this computer's replica players
            netColour.Clear(); netWarned.Clear();
            var of = NetPlayerOf;
            if (L.Count > 3 && of != null && !state.Dead)
            {
                int n = Mathf.Max(0, Mathf.RoundToInt(L[3]));
                for (int i = 0; i < n && 4 + i * 3 + 2 < L.Count; i++)
                {
                    int c = Mathf.RoundToInt(L[4 + i * 3 + 1]); if (c != Brass && c != Cobalt) continue;
                    var p = of(Mathf.RoundToInt(L[4 + i * 3])); if (!p) continue;
                    netColour[p] = c; if (L[4 + i * 3 + 2] > 0.5f) netWarned.Add(p);
                }
            }
            DrawColourRings();
            NetColourRingsMax = Mathf.Max(NetColourRingsMax, ColourRingsShown);
            // prop views: buds swell toward their bloom and pulse; a pod whose landing never arrived lands on its own
            if (state.Dead) { ReplicaClear(); return; }
            for (int i = netBuds.Count - 1; i >= 0; i--)
            {
                var b = netBuds[i]; b.Age += dt;
                if (!b.Prop) { netBuds.RemoveAt(i); continue; }
                if (b.View) b.View.Open = Mathf.Clamp01(b.Age / Mathf.Max(0.1f, b.Life));
                PulseBud(b.Prop);
                if (b.Age > b.Life + 2f) { EndBudView(b.View, b.Prop, true); netBuds.RemoveAt(i); }
            }
            for (int i = netPods.Count - 1; i >= 0; i--)
            {
                var pod = netPods[i];
                if (!pod || pod.Now != PropMotion.Mode.Fall) { netPods.RemoveAt(i); continue; }
                if (pod.LandsIn < -1f) { pod.Land(); netPods.RemoveAt(i); }
            }
        }

        public override bool ReplayNetEffect(string method, Networking.LanWorldFrame.EffectArg[] a, System.Func<int, PlayerController> player)
        {
            switch (method)
            {
                case "SylvaraEncounter.Pod":
                {
                    if (a.Length != 2) return false;
                    NetPods++;
                    var pv = SylvaraProps.Spawn(SylvaraProps.Pod, transform.parent, NetV(a[0]), LookRnd(0, 360));
                    if (pv)
                    {
                        PodModels++; props.Add(pv.transform); netPods.Add(pv);
                        Vector3 last = Sim.Flat(NetV(a[0]));
                        pv.Follow = () => last = ReplicaPodCentre(last);
                        pv.FallIn(NetF(a[1]));
                    }
                    return true;
                }
                case "SylvaraEncounter.PodLand":
                {
                    if (a.Length != 1) return false;
                    NetPodLands++;
                    var at = Sim.Flat(NetV(a[0])); PropMotion best = null; float bd = 9f;
                    foreach (var pod in netPods)
                    {
                        if (!pod || pod.Now != PropMotion.Mode.Fall) continue;
                        float d = (Sim.Flat(pod.transform.position) - at).sqrMagnitude;
                        if (d < bd) { bd = d; best = pod; }
                    }
                    if (best) { best.Follow = () => at; best.Land(); netPods.Remove(best); }
                    return true;
                }
                case "SylvaraEncounter.Roots":
                    if (a.Length != 2) return false;
                    NetRoots++; RootsErupt(NetV(a[0]), NetF(a[1]));
                    return true;
                case "SylvaraEncounter.Bud":
                {
                    if (a.Length != 2) return false;
                    NetBuds++;
                    var b = new NetBud { Pos = NetV(a[0]), Life = NetF(a[1]) };
                    b.Prop = MakeBudView(b.Pos, out b.View);
                    netBuds.Add(b);
                    return true;
                }
                case "SylvaraEncounter.BudEnd":
                {
                    if (a.Length != 2) return false;
                    NetBudEnds++;
                    var at = NetV(a[0]); NetBud best = null; float bd = 1f;
                    foreach (var b in netBuds) { float d = Sim.Flat(b.Pos - at).sqrMagnitude; if (d < bd) { bd = d; best = b; } }
                    if (best != null) { EndBudView(best.View, best.Prop, NetB(a[1])); netBuds.Remove(best); }
                    return true;
                }
                case "SylvaraEncounter.Clear":
                    ReplicaClear();
                    return true;
            }
            return false;
        }
        void ReplicaClear() { netBuds.Clear(); netPods.Clear(); if (props.Count > 0) ClearProps(); }
        // the pod's warning on this computer: the nearest seed or feet pod stand-in hazard (it tracks its target for 0.6 s)
        Vector3 ReplicaPodCentre(Vector3 last)
        {
            if (W == null) return last;
            Vector3 best = last; float bd = 16f;
            foreach (var h in W.Hazards.Live)
            {
                if (h == null || !h.Replica || h.Cancelled || (h.Source != "Seed Pod" && h.Source != "Feet Pod")) continue;
                float d = (Sim.Flat(h.Origin) - last).sqrMagnitude;
                if (d < bd) { bd = d; best = Sim.Flat(h.Origin); }
            }
            return best;
        }

        // host test hooks (LAN smoke tests): seed pods on the farthest players and a wave of Choir Buds, now
        public void DebugPods() { if (!Dead && W != null) PodsOnFar(); }
        public void DebugBuds() { if (!Dead && W != null) PlantBuds(BloomAfter); }

        protected override void OnDefeat()
        {
            foreach (var e in emit) e.On = false;
            emit.Clear(); spinDir.Clear(); later.Clear(); podRings.Clear(); foreach (var b in buds) KillBud(b); buds.Clear(); colour.Clear(); swapWarned.Clear();
            ClearProps();
            if (NetRecording) NetFx("SylvaraEncounter.Clear");
            glowTarget = 0.3f; glowLevel = 2f;
            presenter.PlayOnce("Defeat", 2.5f);
            maskTarget = 0;   // the mask closes as she bows
            W.Fx.Crescendo(transform.position, true, 2, true);
        }
        void OnDestroy()
        {
            ClearProps();
            foreach (var r in colourRings.Values) if (r) Destroy(r.gameObject);
            if (ringBrassMat) { Destroy(ringBrassMat); Destroy(ringCobaltMat); }
        }
        public CharacterPresenter Presenter => presenter;
    }
}
