using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // 3 — Chimebound Oracle, Auditor of Five Notes. Schedule follows the encounter spec v0.1 (collab PR #57,
    // docs/design/chimebound-oracle.md) at Steady Ticking: the packet's two phases and new attacks, the stand-in's numbers
    // for Shattered and Dividing Chime. Stays within 3 m of the dial centre (the leash the escape check relies on).
    // Placeholder geometry: a cobalt sphere, a gold slit core and exactly five bells (I–V).
    public sealed class ChimeboundOracle : BossEncounter
    {
        public override string Id => "miniboss.chimebound_oracle";
        public override string DisplayName => "The Chimebound Oracle";
        public override string Lesson => "Read which bell stays silent, and never return to an old mark";

        public const string Phase1 = "Ordered Choir", Phase2 = "Broken Order";
        public const string Shattered = "Shattered Chime", Dividing = "Dividing Chime", Silent = "Silent Fifth",
            After = "Afterchime", Passing = "Passing Silence", Needle = "Unison Needle";

        public const float Leash = 3f;
        const float ChimeRadius = 1.4f, ConeRadius = 6.5f, ConeHalf = 35f, SectorRadius = 6.5f, SectorHalf = 144f, NeedleWidth = 1.5f;
        const int MarkFollow = 30, MarkResolve = 78, MarkGap = 24, MarkDmg = 12;
        const int ConeLock = 18, ConeResolve = 69, ConeDmg = 20;
        const int ComboConeStart = 110;   // spec §4.1: resolves 53 ticks after the last marker (15 stun + 38 escape)
        const int FifthWarn = 144, PassWarn = 120, RepeatWarn = 90, RepeatDelay = 12, NeedleTrack = 48, NeedleResolve = 108;
        const int HeavyDmg = 26;
        const int Recovery = 60, FifthRecovery = 204, AfterRecovery = 192, NeedleRecovery = 180, PassRecovery = 228, Shift = 90;

        int phase = 1, loop;
        // stand-in for the seeded encounter RNG (R6): deterministic per encounter
        readonly System.Random rng = new System.Random(0x5F1F7);

        // tests: the most recent instance of each attack, and the safe sector of the live sector attack
        public Hazard LastFifth { get; private set; }
        public Hazard LastPassingBeat2 { get; private set; }
        public Hazard LastNeedle { get; private set; }
        public readonly List<Hazard> LastOriginals = new List<Hazard>(), LastRepeats = new List<Hazard>();
        public int Outlines => outlines.Count;
        public float SafeYaw { get; private set; }
        public int SafeNumeral { get; private set; }   // 1..5 (V = 5 in Silent Fifth)

        Transform cluster, core, shellL, shellR; readonly List<Transform> bells = new List<Transform>();
        readonly List<Transform> glyphs = new List<Transform>(); Transform glyphRoot, hollow;
        readonly List<LastBell.VFX.BellkeeperFx.Telegraph> outlines = new List<LastBell.VFX.BellkeeperFx.Telegraph>();
        float spin = 1, iris;                     // presentation: bell orbit speed, phase 2 shell opening 0..1
        bool sectorPose; float poseYaw; int silentBell = 4;

        void Awake() { MaxHp = 1700; BodyRadius = 1.2f; HitHeight = 2.0f; MarkHeight = 4.2f; }

        protected override void Build()
        {
            var cobalt = new Color(0.16f, 0.22f, 0.55f); var ivory = new Color(0.95f, 0.92f, 0.84f); var brass = new Color(0.78f, 0.6f, 0.28f);
            cluster = new GameObject("Cluster").transform; cluster.SetParent(transform, false); cluster.localPosition = new Vector3(0, 2.0f, 0);
            core = Prim.Make(PrimitiveKind.Sphere, cluster, Vector3.zero, Vector3.one * 0.55f, new Color(1f, 0.82f, 0.4f), 2.5f);
            // the shell is two hemispheres that part like an iris in phase 2
            shellL = Prim.Make(PrimitiveKind.Sphere, cluster, new Vector3(-0.27f, 0, 0), new Vector3(0.55f, 1.1f, 1.1f), cobalt);
            shellR = Prim.Make(PrimitiveKind.Sphere, cluster, new Vector3(0.27f, 0, 0), new Vector3(0.55f, 1.1f, 1.1f), cobalt);
            Prim.Make(PrimitiveKind.Sphere, shellL, new Vector3(0.45f, 0.05f, 0.38f), new Vector3(0.5f, 0.5f, 0.25f), ivory);   // eye-mask halves
            Prim.Make(PrimitiveKind.Sphere, shellR, new Vector3(-0.45f, 0.05f, 0.38f), new Vector3(0.5f, 0.5f, 0.25f), ivory);
            Prim.Make(PrimitiveKind.Cube, cluster, new Vector3(0, 0.05f, 0.56f), new Vector3(0.06f, 0.36f, 0.04f), new Color(1f, 0.85f, 0.45f), 4f);   // gold slit
            for (int i = 0; i < 5; i++)
            {
                var b = Prim.Make(PrimitiveKind.Cone, cluster, Vector3.zero, new Vector3(0.5f, 0.55f, 0.5f), brass, 0.5f, Quaternion.Euler(180, 0, 0));
                // raised numeral on the bell: I..V as 1..5 small studs (stand-in for the glyph)
                for (int k = 0; k < i + 1; k++) Prim.Make(PrimitiveKind.Cube, b, new Vector3((k - i * 0.5f) * 0.16f, 0.05f, 0.52f), new Vector3(0.07f, 0.35f, 0.04f), ivory, 0.8f);
                bells.Add(b);
            }
            BuildGlyphs();
            PhaseName = Phase1;
        }

        // Floor numerals I–V for the sector attacks (spec §3.3): about 1.2 m tall, ivory/gold, never the danger palette.
        void BuildGlyphs()
        {
            glyphRoot = new GameObject("Sector numerals").transform; glyphRoot.SetParent(transform, false);
            var ink = new Color(0.98f, 0.9f, 0.7f);
            for (int n = 1; n <= 5; n++)
            {
                var g = new GameObject("Numeral " + n).transform; g.SetParent(glyphRoot, false);
                foreach (var (x, tilt) in Strokes(n))
                    Prim.Make(PrimitiveKind.Cube, g, new Vector3(x, 0.07f, 0), new Vector3(0.14f, 0.03f, 1.2f), ink, 1.2f, Quaternion.Euler(0, tilt, 0));
                glyphs.Add(g);
            }
            hollow = new GameObject("Silent glyph").transform; hollow.SetParent(glyphRoot, false);
            for (int k = 0; k < 16; k++)
            {
                float a = k * 22.5f;
                Prim.Make(PrimitiveKind.Cube, hollow, Quaternion.Euler(0, a, 0) * new Vector3(0, 0.07f, 0.75f), new Vector3(0.3f, 0.03f, 0.1f), ink, 1.6f, Quaternion.Euler(0, a, 0));
            }
            glyphRoot.gameObject.SetActive(false);
        }
        static IEnumerable<(float x, float tilt)> Strokes(int n)
        {
            switch (n)
            {
                case 1: yield return (0, 0); break;
                case 2: yield return (-0.2f, 0); yield return (0.2f, 0); break;
                case 3: yield return (-0.35f, 0); yield return (0, 0); yield return (0.35f, 0); break;
                case 4: yield return (-0.45f, 0); yield return (0.05f, -18); yield return (0.35f, 18); break;
                default: yield return (-0.18f, -18); yield return (0.18f, 18); break;
            }
        }

        // ---------------------------------------------------------------- schedule

        bool TransitionDue => phase == 1 && Hp <= MaxHp * 0.5f;

        protected override IEnumerable<int> Script()
        {
            while (true)
            {
                if (TransitionDue)
                {
                    phase = 2; loop = 0;
                    foreach (var w in PhaseShift()) yield return w;
                }
                if (phase == 1)
                {
                    // spec §4.1: teaching loop first (markers, then the cone alone), then markers + cone combined
                    if (loop == 0)
                    {
                        PhaseName = Phase1;
                        foreach (var w in Volley(false)) yield return w;
                        yield return 60;
                        if (TransitionDue) continue;
                        foreach (var w in DividingCone()) yield return w;
                    }
                    else foreach (var w in Volley(true)) yield return w;
                    foreach (var w in Rest(Recovery)) yield return w;
                    loop++;
                    if (TransitionDue) continue;
                    foreach (var w in Recentre()) yield return w;
                    foreach (var w in SilentFifth()) yield return w;
                }
                else
                {
                    // spec §4.2: one damaging pattern at a time
                    foreach (var w in Afterchime()) yield return w;
                    foreach (var w in DividingCone()) yield return w;
                    foreach (var w in Rest(Recovery)) yield return w;
                    foreach (var w in UnisonNeedle()) yield return w;
                    foreach (var w in Recentre()) yield return w;
                    foreach (var w in PassingSilence()) yield return w;
                    loop++;
                }
            }
        }

        IEnumerable<int> PhaseShift()
        {
            // queued: the active attack has finished and its hazards have resolved; the iris opens over 90 harmless ticks
            Current = "phase shift"; SetPhase(Phase2); glowTarget = 3f;
            for (int i = 0; i < Shift; i++)
            {
                iris = (i + 1) / (float)Shift;
                if (i == Shift / 2) { W.Fx.Crescendo(transform.position, true, 0, false); Release(1.5f); }   // gold, not danger
                yield return 1;
            }
            glowTarget = 1;
        }

        IEnumerable<int> Rest(int ticks)
        {
            Current = "recovery"; glowTarget = 0.35f; spin = 0.2f;
            yield return ticks;
            glowTarget = 1; spin = 1;
        }

        IEnumerable<int> Recentre()
        {
            foreach (var w in WalkTo(Vector3.zero, 2f, 90)) yield return w;
        }

        // ---------------------------------------------------------------- attacks

        Hazard SpawnMarker()
        {
            var h = Marker(Shattered, ChimeRadius, MarkFollow, MarkResolve, MarkDmg);
            h.OnResolve = hz => { W.BossFx.ChimeResolve(hz.Origin, hz.Radius); Release(0.6f); };
            return h;
        }

        // Shattered Chime volley: markers at 0 / 24 / 48, resolving at 78 / 102 / 126. With `combo` the Dividing Chime
        // warns at tick 110 and resolves at 179 (spec §4.1), instead of the stand-in's 78.
        IEnumerable<int> Volley(bool combo, List<Hazard> keep = null)
        {
            Current = Shattered; bells[0].localScale = Vector3.one * 0.6f;   // bell I sounds
            int end = combo ? ComboConeStart + ConeResolve : 2 * MarkGap + MarkResolve;
            for (int t = 0; t < end; t++)
            {
                if (t % MarkGap == 0 && t <= 2 * MarkGap) { var h = SpawnMarker(); keep?.Add(h); }
                if (combo && t == ComboConeStart) { Current = Dividing; SpawnCone(); }
                Windup(Mathf.Clamp01(t / (float)end)); yield return 1;
            }
            bells[0].localScale = new Vector3(0.5f, 0.55f, 0.5f);
            Release(0.6f);
        }

        Hazard SpawnCone()
        {
            var h = Cone(Dividing, ConeRadius, ConeHalf, ConeLock, ConeResolve, ConeDmg, true, 300);
            h.OnResolve = hz => { W.BossFx.SweepResolve(hz.Origin, hz.Yaw, hz.Radius, hz.HalfAngle); Release(); };
            return h;
        }

        IEnumerable<int> DividingCone()
        {
            Current = Dividing; spin = 3;
            SpawnCone();
            for (int i = 0; i < ConeResolve; i++) { Windup(i / (float)ConeResolve); yield return 1; }
            spin = 1;
        }

        // one 288° cone (four 72° sectors in a single hazard: a border is hit once); the gap is the silent sector
        Hazard Sectors(string src, float safeYaw, int warn)
        {
            var h = W.Hazards.Spawn(new Hazard
            {
                Source = src, Shape = HazardShape.Cone, Kind = HazardKind.Ground, Origin = Sim.Flat(transform.position),
                Yaw = safeYaw + 180, Radius = SectorRadius, HalfAngle = SectorHalf, LockAt = 0, ResolveAt = warn, Damage = HeavyDmg,
            });
            h.OnResolve = hz => { W.BossFx.SweepResolve(hz.Origin, hz.Yaw, hz.Radius, hz.HalfAngle); Release(); };
            return h;
        }

        // floor numerals: sector n (1..5) centred at safeYaw + (n - silent) * 72°
        void ShowNumerals(float safeYaw, int silent)
        {
            glyphRoot.gameObject.SetActive(true);
            var cam = Camera.main; float up = cam ? Sim.YawTo(Vector3.zero, Sim.Flat(cam.transform.forward), 0) : 0;
            for (int n = 1; n <= 5; n++)
            {
                var at = Sim.Flat(transform.position) + Sim.Dir(safeYaw + (n - silent) * 72f) * 4.2f;
                glyphs[n - 1].SetPositionAndRotation(at, Quaternion.Euler(0, up, 0));
            }
            hollow.position = Sim.Flat(transform.position) + Sim.Dir(safeYaw) * 4.2f;
        }
        void HideNumerals() { glyphRoot.gameObject.SetActive(false); sectorPose = false; }

        IEnumerable<int> SilentFifth()
        {
            Current = Silent;
            SafeYaw = rng.Next(0, 360); SafeNumeral = 5; silentBell = 4;
            transform.rotation = Quaternion.Euler(0, SafeYaw, 0);   // faces the silent bell
            sectorPose = true; poseYaw = SafeYaw;
            ShowNumerals(SafeYaw, 5);
            LastFifth = Sectors(Silent, SafeYaw, FifthWarn);
            for (int i = 0; i < FifthWarn; i++) { Windup(i / (float)FifthWarn); yield return 1; }
            HideNumerals();
            foreach (var w in Rest(FifthRecovery)) yield return w;
        }

        IEnumerable<int> PassingSilence()
        {
            Current = Passing;
            SafeYaw = rng.Next(0, 360); SafeNumeral = 5; silentBell = 4;
            transform.rotation = Quaternion.Euler(0, SafeYaw, 0);
            sectorPose = true; poseYaw = SafeYaw;
            ShowNumerals(SafeYaw, 5);
            LastFifth = Sectors(Passing, SafeYaw, FifthWarn);
            for (int i = 0; i < FifthWarn; i++) { Windup(i / (float)FifthWarn); yield return 1; }
            // beat 2, on beat 1's resolve tick: one neighbour becomes silent (seeded direction), fresh 120-tick warning
            int step = rng.Next(2) == 0 ? 1 : -1;
            float from = SafeYaw; SafeYaw += step * 72f; SafeNumeral = step > 0 ? 1 : 4; silentBell = SafeNumeral - 1;
            arrowFrom = from; arrowTo = SafeYaw; arrowT = 0;
            hollow.position = Sim.Flat(transform.position) + Sim.Dir(SafeYaw) * 4.2f;
            LastPassingBeat2 = Sectors(Passing, SafeYaw, PassWarn);
            for (int i = 0; i < PassWarn; i++) { Windup(i / (float)PassWarn); yield return 1; }
            HideNumerals(); arrowT = -1;
            foreach (var w in Rest(PassRecovery)) yield return w;
        }

        // Afterchime: a normal volley, hollow outlines as each circle resolves, then all of them repeat together as one
        // attack event (shared hit set: a player in overlapping repeats is hit once).
        IEnumerable<int> Afterchime()
        {
            LastOriginals.Clear(); LastRepeats.Clear();
            var originals = LastOriginals;
            foreach (var w in Volley(false, originals)) yield return w;
            foreach (var h in originals)
            {
                var o = W.BossFx.Circle(h.Radius); o.Memory(); o.Place(h.Origin, 0); outlines.Add(o);
            }
            Current = After;
            yield return RepeatDelay;
            foreach (var o in outlines) o.Dismiss();
            outlines.Clear();
            Hazard leader = null;
            foreach (var src in originals)
            {
                var h = W.Hazards.Spawn(new Hazard
                {
                    Source = After, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = src.Origin,
                    Radius = src.Radius, LockAt = 0, ResolveAt = RepeatWarn, Damage = MarkDmg,
                });
                h.Group = leader; leader ??= h;
                h.OnResolve = hz => W.BossFx.ChimeResolve(hz.Origin, hz.Radius);
                LastRepeats.Add(h);
            }
            for (int i = 0; i < RepeatWarn; i++) { Windup(i / (float)RepeatWarn); yield return 1; }
            Release(0.8f);
            foreach (var w in Rest(AfterRecovery)) yield return w;
        }

        IEnumerable<int> UnisonNeedle()
        {
            Current = Needle; spin = 0;
            var h = new Hazard
            {
                Source = Needle, Shape = HazardShape.Lane, Kind = HazardKind.Strike, Width = NeedleWidth,
                LockAt = NeedleTrack, ResolveAt = NeedleResolve, Damage = HeavyDmg,
                Origin = Sim.Flat(transform.position), Length = 2 * CombatWorld.PlayerBound + 1,   // telegraph quad sized for any aim
            };
            h.Track = AimNeedle;
            h.OnResolve = hz => { W.BossFx.ChargeLand(hz.Origin + Sim.Dir(hz.Yaw) * hz.Length); Release(1.2f); };
            LastNeedle = W.Hazards.Spawn(h);
            AimNeedle(h);
            for (int i = 0; i < NeedleResolve; i++) { Windup(i / (float)NeedleResolve); yield return 1; }
            spin = 1;
            foreach (var w in Rest(NeedleRecovery)) yield return w;
        }
        // boss centre -> target, all the way to the floor edge (spec: no out-of-reach parking spot)
        void AimNeedle(Hazard h)
        {
            if (Dead) return;
            TurnTowards(YawToTarget(), 9999);
            h.Origin = Sim.Flat(transform.position); h.Yaw = transform.eulerAngles.y;
            h.Length = ToEdge(h.Origin, Sim.Dir(h.Yaw), CombatWorld.PlayerBound + 0.3f);
            h.Tele?.Size(h.Length);
        }

        // ---------------------------------------------------------------- per tick and presentation

        protected override void StepAlways()
        {
            // drift away from a close target during the volley, never beyond the 3 m leash
            if (Current == Shattered && DistToTarget() < 3.5f)
            {
                var away = Sim.Flat(transform.position - Target()).normalized;
                var p = transform.position + away * 1.2f * Sim.Dt;
                if (new Vector2(p.x, p.z).magnitude <= Leash) transform.position = p;
            }
        }

        float arrowFrom, arrowTo, arrowT = -1;

        protected override void Update()
        {
            base.Update();
            if (!cluster) return;
            float t = Time.time;
            cluster.localPosition = new Vector3(0, Dead ? Mathf.MoveTowards(cluster.localPosition.y, 0.4f, Time.deltaTime) : 2.0f + 0.2f * Mathf.Sin(t * 1.4f), 0);
            float open = Mathf.SmoothStep(0, 1, iris);
            shellL.localPosition = new Vector3(-0.27f - 0.33f * open, 0, 0); shellR.localPosition = new Vector3(0.27f + 0.33f * open, 0, 0);
            Prim.SetGlow(core.GetComponent<Renderer>(), new Color(1f, 0.8f, 0.4f) * (1.5f + 1.5f * open) * glowLevel);
            for (int i = 0; i < bells.Count; i++)
            {
                Vector3 target;
                if (sectorPose)
                {
                    // each bell hangs over its sector; the silent bell drops low, the others rise and glow coral
                    var d = Quaternion.Euler(0, poseYaw - transform.eulerAngles.y + (i - 4) * 72f, 0) * Vector3.forward;   // bell i over sector i + 1
                    target = d * 1.5f + Vector3.up * (i == silentBell ? -1.1f : 0.6f);
                    Prim.SetGlow(bells[i].GetComponent<Renderer>(), i == silentBell ? Color.black : new Color(1f, 0.45f, 0.3f) * 1.2f);
                }
                else
                {
                    float a = t * spin * (Dead ? 0 : 1) + i * Mathf.PI * 2 / bells.Count;
                    float r = 1.25f + 0.45f * open;                    // phase 2: spread to stepped heights, one per numeral
                    float y = open > 0 ? Mathf.Lerp(0.3f * Mathf.Sin(a * 2 + i), -0.7f + 0.35f * i, open) : 0.3f * Mathf.Sin(a * 2 + i);
                    target = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                    if (Current == Needle) target = Vector3.forward * (0.9f + 0.18f * i) + Vector3.up * 0.15f * (i - 2);   // all bells point at the target
                    Prim.SetGlow(bells[i].GetComponent<Renderer>(), new Color(1f, 0.75f, 0.35f) * 0.5f * glowLevel);
                }
                bells[i].localPosition = Vector3.Lerp(bells[i].localPosition, target, Mathf.Clamp01(Time.deltaTime * 8));
            }
            if (arrowT >= 0 && glyphRoot.gameObject.activeSelf)
            {
                // Passing Silence: a brass mote sweeps from the old silent numeral to the new one, then the hollow glyph follows
                arrowT = Mathf.Min(1, arrowT + Time.deltaTime / 0.6f);
                hollow.position = Sim.Flat(transform.position) + Sim.Dir(Mathf.LerpAngle(arrowFrom, arrowTo, Mathf.SmoothStep(0, 1, arrowT))) * 4.2f;
            }
        }

        protected override void OnDefeat()
        {
            glowTarget = 0.05f; spin = 0; HideNumerals(); arrowT = -1;
            foreach (var o in outlines) o.Dismiss();
            outlines.Clear();
        }
        void OnDestroy() { foreach (var o in outlines) o.Dismiss(); outlines.Clear(); }
    }
}
