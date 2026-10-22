using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Sylvara, The Rootbound Choir: Verdant Chimes' area boss, graybox. Practice-only for now (she isn't part of the
    // one-arena run), fought in the key-art arena, which matches the spec's 11.5 m placeholder circle.
    // Schedule follows docs/design/sylvara.md v0.1 (collab PR #50) at Steady Ticking, with its recommendations applied:
    // D1 3.6 m Procession lane, D2 72-tick Bloom stagger, D3 5 m Bloom ring, D4 60-degree Rootscript spread,
    // D5 Arc at the nearest / Procession at the farthest player, D6 no knockback and 0.15 s hit-stun.
    // HP thresholds 65 % / 30 %, queued to block boundaries. No two damaging patterns ever overlap at this tier.
    public sealed class SylvaraEncounter : BossEncounter
    {
        public override string Id => "boss.sylvara";
        public override string DisplayName => "Sylvara, the Rootbound Choir";
        public override string Lesson => "Step into the space the last flower spent";
        public override bool Placeholder => !presenter || !presenter.HasModel;

        // geometry (m)
        const float ArcRadius = 5.5f, ArcHalf = 50f;
        const float RootWidth = 1.8f, RootLength = 7f, RootSpread = 60f;
        const float SeedRadius = 1.5f;
        const float ProcessionWidth = 3.6f, ProcessionTravel = 7f;
        const float BloomRadius = 2f, BloomRing = 5f, BloomSpacing = 4.2f;
        // her centre keeps a player width between her body and the map's wall (spec §2): 11.4 m in Verdant Chimes
        float Bound => CombatWorld.PlayerBound + Hazard.Margin - BodyRadius - 2 * Hazard.Margin;
        // timing (ticks, 60 Hz)
        const int ArcTrack = 18, ArcResolve = 96, ArcRecovery = 102, ArcCd = 300;
        const int RootResolve = 114, RootRetract = 12, RootRecovery = 120, RootCd = 600;
        const int SeedFollow = 36, SeedResolve = 132, SeedRecovery = 108, SeedCd = 660;
        const int ProcResolve = 108, ProcTravel = 39, ProcRecovery = 138, ProcCd = 720;
        const int BloomWarn = 102, BloomActive = 18, BloomStagger = 72, BloomPunish = 180, BloomCd = 1260;
        const int ShiftTicks = 90;
        // damage (100 HP)
        const int ArcDmg = 13, RootDmg = 14, SeedDmg = 12, ProcDmg = 15, BloomDmg = 13;
        const int Stun = 9;

        CharacterPresenter presenter;
        int phase = 1, rotation;
        readonly Dictionary<string, int> lastUse = new Dictionary<string, int>();
        readonly List<Transform> props = new List<Transform>();
        Transform maskL, maskR, bellRoot, shoulderFlowers;
        // Verdant Chimes' sun sits behind her from the gameplay camera, so her camera-facing side is in shadow: a soft
        // front light lifts it and a turquoise rim edges her silhouette. Both are presentation only, short-range and
        // fixed in world offsets (the camera never turns), so they never paint a pool that reads as a floor warning.
        Light frontLight, rimLight;
        static readonly Vector3 FrontOffset = new Vector3(0, 3.0f, 2.6f), RimOffset = new Vector3(0, 5.2f, -2.4f);
        const float FrontIntensity = 12f, RimIntensity = 18f;
        public int BloomsCompleted { get; private set; }
        public readonly List<string> Order = new List<string>();   // attack starts, for tests
        public readonly List<(int index, int resolveTick, int spentTick)> BloomLog = new List<(int, int, int)>();

        void Awake() { MaxHp = 6600; BodyRadius = 1.8f; HitHeight = 2.4f; MarkHeight = 4.8f; }

        static readonly Color Stone = new Color(0.68f, 0.70f, 0.64f), Wood = new Color(0.27f, 0.20f, 0.15f),
            Brass = new Color(0.55f, 0.47f, 0.25f), Verdigris = new Color(0.25f, 0.50f, 0.44f), Ivory = new Color(0.90f, 0.87f, 0.78f),
            Turquoise = new Color(0.25f, 0.95f, 0.85f), Leaf = new Color(0.30f, 0.42f, 0.24f), SpentPetal = new Color(0.55f, 0.62f, 0.50f);

        protected override void Build()
        {
            // Resources/Characters/Sylvara.glb (Meshy, rigged by prototype/arena-3d/models/build-sylvara.ps1); the primitive
            // body below is the fallback stand-in when the model is missing
            presenter = CharacterPresenter.Create(transform, "Sylvara", 4f, 0.6f, 0, host =>
            {
                // four planted root legs, splayed to the 1.8 m footprint
                for (int i = 0; i < 4; i++)
                {
                    float a = 45 + 90 * i; var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                    Prim.Make(PrimitiveKind.Cylinder, host, dir * 0.95f + Vector3.up * 0.95f, new Vector3(0.34f, 0.85f, 0.34f), Wood, 0, Quaternion.LookRotation(dir) * Quaternion.Euler(-28, 0, 0));
                    Prim.Make(PrimitiveKind.Sphere, host, dir * 1.35f + Vector3.up * 0.2f, new Vector3(0.6f, 0.45f, 0.6f), Stone);
                }
                Prim.Make(PrimitiveKind.Capsule, host, new Vector3(0, 2.6f, 0), new Vector3(0.9f, 0.9f, 0.7f), Wood);
                // ribcage with three chest bells
                bellRoot = new GameObject("Bells").transform; bellRoot.SetParent(host, false); bellRoot.localPosition = new Vector3(0, 2.7f, 0.42f);
                for (int i = 0; i < 3; i++)
                    Prim.Make(PrimitiveKind.Cone, bellRoot, new Vector3((i - 1) * 0.22f, i == 1 ? -0.1f : 0, 0), new Vector3(0.24f, 0.26f, 0.24f), Brass, 0.4f);
                Prim.Make(PrimitiveKind.Sphere, host, new Vector3(0, 2.8f, 0.2f), Vector3.one * 0.32f, Turquoise, 1.2f);
                // split ivory mask
                maskL = Prim.Make(PrimitiveKind.Sphere, host, new Vector3(-0.13f, 3.65f, 0.2f), new Vector3(0.28f, 0.6f, 0.36f), Ivory);
                maskR = Prim.Make(PrimitiveKind.Sphere, host, new Vector3(0.13f, 3.65f, 0.2f), new Vector3(0.28f, 0.6f, 0.36f), Ivory);
                Prim.Make(PrimitiveKind.Sphere, host, new Vector3(-0.1f, 3.72f, 0.38f), Vector3.one * 0.07f, Turquoise, 2f);
                Prim.Make(PrimitiveKind.Sphere, host, new Vector3(0.1f, 3.72f, 0.38f), Vector3.one * 0.07f, Turquoise, 2f);
                // branching shoulders, tuning-fork arm (right) and three-hook branch hand (left)
                Prim.Make(PrimitiveKind.Cube, host, new Vector3(0, 3.25f, 0), new Vector3(1.9f, 0.25f, 0.35f), Wood);
                Prim.Make(PrimitiveKind.Cube, host, new Vector3(1.05f, 2.2f, 0.25f), new Vector3(0.14f, 2.0f, 0.14f), Brass, 0, Quaternion.Euler(-15, 0, 8));
                Prim.Make(PrimitiveKind.Cube, host, new Vector3(1.25f, 1.1f, 0.6f), new Vector3(0.08f, 1.0f, 0.08f), Brass, 0.3f, Quaternion.Euler(-15, 0, 8));
                Prim.Make(PrimitiveKind.Cube, host, new Vector3(1.05f, 1.1f, 0.6f), new Vector3(0.08f, 1.0f, 0.08f), Brass, 0.3f, Quaternion.Euler(-15, 0, 8));
                Prim.Make(PrimitiveKind.Cube, host, new Vector3(-1.0f, 2.3f, 0.2f), new Vector3(0.16f, 1.7f, 0.16f), Wood, 0, Quaternion.Euler(-10, 0, -10));
                for (int i = 0; i < 3; i++)
                    Prim.Make(PrimitiveKind.Cube, host, new Vector3(-1.1f + (i - 1) * 0.12f, 1.35f, 0.4f), new Vector3(0.05f, 0.4f, 0.05f), Wood, 0, Quaternion.Euler(-30, 0, (i - 1) * 15));
                // broken bronze wheel on the back, ferns on the shoulders
                Prim.Make(PrimitiveKind.Cylinder, host, new Vector3(0, 3.3f, -0.5f), new Vector3(1.5f, 0.05f, 1.5f), Brass, 0, Quaternion.Euler(90, 0, 0));
                Prim.Make(PrimitiveKind.Sphere, host, new Vector3(-0.8f, 3.5f, -0.1f), new Vector3(0.6f, 0.35f, 0.5f), Leaf);
                Prim.Make(PrimitiveKind.Sphere, host, new Vector3(0.8f, 3.5f, -0.1f), new Vector3(0.6f, 0.35f, 0.5f), Leaf);
                shoulderFlowers = new GameObject("ShoulderFlowers").transform; shoulderFlowers.SetParent(host, false);
                Prim.Make(PrimitiveKind.Sphere, shoulderFlowers, new Vector3(-0.7f, 3.75f, 0.1f), Vector3.one * 0.22f, Ivory, 0.3f);
                Prim.Make(PrimitiveKind.Sphere, shoulderFlowers, new Vector3(0.7f, 3.75f, 0.1f), Vector3.one * 0.22f, Ivory, 0.3f);
                shoulderFlowers.gameObject.SetActive(false);
                return host;
            });
            PhaseName = "Dormant Choir";
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

        int PhaseFor() => Hp > MaxHp * 0.65f ? 1 : Hp > MaxHp * 0.30f ? 2 : 3;

        protected override IEnumerable<int> Script()
        {
            while (true)
            {
                int want = PhaseFor();
                if (want != phase)
                {
                    phase = want;
                    foreach (var w in PhaseShift(phase == 2 ? "Awakening Bloom" : "Hollow Refrain")) yield return w;
                }
                if (phase == 1)
                {
                    // §4.1: every attack on its own, full recovery between
                    foreach (var w in Arc()) yield return w; if (PhaseFor() != phase) continue;
                    foreach (var w in Rootscript(true)) yield return w; if (PhaseFor() != phase) continue;
                    foreach (var w in Seeds()) yield return w; if (PhaseFor() != phase) continue;
                    foreach (var w in Procession()) yield return w; if (PhaseFor() != phase) continue;
                    foreach (var w in Arc()) yield return w; if (PhaseFor() != phase) continue;
                    foreach (var w in Bloom()) yield return w;
                }
                else if (phase == 2)
                {
                    // §4.2: A Seeded Bloom, B Pursuit, C Inscription
                    foreach (var w in Seeds(12)) yield return w;
                    foreach (var w in Bloom()) yield return w; if (PhaseFor() != phase) continue;
                    foreach (var w in Arc()) yield return w;
                    foreach (var w in Procession()) yield return w; if (PhaseFor() != phase) continue;
                    foreach (var w in Rootscript(true)) yield return w;
                    foreach (var w in Arc()) yield return w;
                }
                else
                {
                    // §4.3: X Refrain, F1, Y Root Prelude, F2
                    foreach (var w in Seeds(0)) yield return w;
                    foreach (var w in Arc()) yield return w;
                    foreach (var w in Bloom()) yield return w;
                    foreach (var w in Procession()) yield return w;
                    foreach (var w in Arc()) yield return w;
                    foreach (var w in Seeds()) yield return w;
                    foreach (var w in Rootscript(false)) yield return w;   // roots retract, then flower I warns
                    foreach (var w in Bloom()) yield return w;
                    foreach (var w in Procession()) yield return w;
                    foreach (var w in Arc()) yield return w;
                }
            }
        }

        // ---------------- scheduling helpers ----------------
        // cooldown hold: if an attack isn't ready, hold the recovery pose until it is; windows never shrink (spec §4)
        IEnumerable<int> Ready(string id, int cd)
        {
            while (lastUse.TryGetValue(id, out int t) && Age - t < cd) { Current = "listening"; yield return 1; }
            lastUse[id] = Age; Order.Add(id); Current = id;
        }
        IEnumerable<int> Recover(int ticks)
        {
            Current = "recovery"; glowTarget = 0.4f; yield return ticks; glowTarget = 1;
        }
        Hazard Tune(Hazard h) { h.Stun = Stun; h.Knockback = 0; return h; }

        Vector3 Farthest()
        {
            PlayerController best = null; float bd = -1;
            foreach (var p in W.Players)
            {
                if (p.Down) continue;
                float d = Sim.Flat(p.transform.position - transform.position).sqrMagnitude;
                if (d > bd) { bd = d; best = p; }
            }
            return best ? best.transform.position : Vector3.zero;
        }
        // rotating target, round-robin over standing players by index (solo: the player)
        Vector3 Rotating()
        {
            var standing = W.Players.FindAll(p => !p.Down);
            if (standing.Count == 0) return Vector3.zero;
            return standing[rotation++ % standing.Count].transform.position;
        }
        static Vector3 Clamp(Vector3 p, float r) { p.y = 0; var f = new Vector2(p.x, p.z); if (f.magnitude > r) { f = f.normalized * r; p.x = f.x; p.z = f.y; } return p; }

        // ---------------- attacks ----------------
        IEnumerable<int> PhaseShift(string name)
        {
            Current = "phase shift"; SetPhase(name);
            presenter.PlayOnce("PhaseShift", ShiftTicks / (float)Sim.Hz);
            glowTarget = 3f;
            for (int i = 0; i < ShiftTicks; i++)
            {
                if (i == ShiftTicks / 2)
                {
                    W.Fx.Crescendo(transform.position, true, 0, false); Release(1.5f);   // gold, not danger: deals no damage
                    if (phase >= 2 && shoulderFlowers) shoulderFlowers.gameObject.SetActive(true);
                }
                if (phase >= 2 && bellRoot) for (int b = 0; b < bellRoot.childCount; b++) { var c = bellRoot.GetChild(b); c.localPosition = Vector3.Lerp(c.localPosition, new Vector3((b - 1) * 0.42f, c.localPosition.y, 0), 0.05f); }
                if (phase == 3 && maskL) { maskL.localPosition = Vector3.Lerp(maskL.localPosition, new Vector3(-0.32f, 3.65f, 0.2f), 0.05f); maskR.localPosition = Vector3.Lerp(maskR.localPosition, new Vector3(0.32f, 3.65f, 0.2f), 0.05f); }
                yield return 1;
            }
            glowTarget = 1;
        }

        // Pruning Arc: steps toward the nearest player (planted roots, slow), tracks 0.3 s, then one sweep
        IEnumerable<int> Arc()
        {
            foreach (var w in Ready("Pruning Arc", ArcCd)) yield return w;
            foreach (var w in Approach(4.2f, 1.6f, 90)) yield return w;
            Current = "Pruning Arc"; presenter.PlayOnce("PruningArc", (ArcResolve + 12) / (float)Sim.Hz);
            var h = Tune(Cone("Pruning Arc", ArcRadius, ArcHalf, ArcTrack, ArcResolve, ArcDmg));
            h.OnResolve = hz => { W.BossFx.SweepResolve(hz.Origin, hz.Yaw, hz.Radius, hz.HalfAngle); Release(); };
            for (int i = 0; i < ArcResolve; i++) { Windup(i / (float)ArcResolve); yield return 1; }
            foreach (var w in Recover(ArcRecovery)) yield return w;
        }

        // Rootscript: three lanes 60 degrees apart from her centre, all locked at warning; roots retract after the eruption
        IEnumerable<int> Rootscript(bool recover)
        {
            foreach (var w in Ready("Rootscript", RootCd)) yield return w;
            Current = "Rootscript"; presenter.PlayOnce("Rootscript", (RootResolve + RootRetract) / (float)Sim.Hz);
            float c = Sim.YawTo(transform.position, Rotating(), transform.eulerAngles.y);
            transform.rotation = Quaternion.Euler(0, c, 0);
            for (int k = -1; k <= 1; k++)
            {
                float yaw = c + k * RootSpread;
                float len = Mathf.Min(RootLength, ToEdge(transform.position, Sim.Dir(yaw), CombatWorld.PlayerBound + Hazard.Margin));
                if (len < 0.5f) continue;
                var h = Tune(Lane("Rootscript", transform.position, yaw, len, RootWidth, RootResolve, RootDmg));
                h.Kind = HazardKind.Ground;
                h.OnResolve = hz => { W.BossFx.ChargeLand(hz.Origin + Sim.Dir(hz.Yaw) * hz.Length * 0.6f); Release(0.6f); };
            }
            for (int i = 0; i < RootResolve; i++) { Windup(i / (float)RootResolve); yield return 1; }
            Current = "roots retracting"; yield return RootRetract;
            if (recover) foreach (var w in Recover(RootRecovery - RootRetract)) yield return w;
        }

        // Seed of the Refrain: a circle follows each selected player for 0.6 s, locks, then one eruption.
        // `joinAfter` >= 0: skip the recovery and hand over that many ticks after the seeds resolve (block A, block X).
        IEnumerable<int> Seeds(int joinAfter = -1)
        {
            foreach (var w in Ready("Seed of the Refrain", SeedCd)) yield return w;
            Current = "Seed of the Refrain"; presenter.PlayOnce("Seed", SeedResolve / (float)Sim.Hz);
            int n = W.Players.Count <= 1 ? 1 : W.Players.Count <= 4 ? 2 : 3;
            var owners = W.Players.FindAll(p => !p.Down);
            for (int i = 0; i < n && i < owners.Count; i++)
            {
                var owner = owners[(rotation + i) % owners.Count];
                var h = Tune(new Hazard
                {
                    Source = "Seed of the Refrain", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Radius = SeedRadius,
                    LockAt = SeedFollow, ResolveAt = SeedResolve, Damage = SeedDmg, Origin = Sim.Flat(owner.transform.position),
                });
                h.Track = hz => { if (owner) hz.Origin = Vector3.MoveTowards(hz.Origin, Sim.Flat(owner.transform.position), 6f * Sim.Dt); };
                h.OnResolve = hz => { W.BossFx.ChimeResolve(hz.Origin, hz.Radius); Release(0.6f); };
                W.Hazards.Spawn(h);
            }
            rotation += n;
            for (int i = 0; i < SeedResolve; i++) { Windup(i / (float)SeedResolve); yield return 1; }
            if (joinAfter >= 0) { yield return Mathf.Max(1, joinAfter); yield break; }
            foreach (var w in Recover(SeedRecovery)) yield return w;
        }

        // Briar Procession: lane toward the farthest player, as wide as her body; resolves, then she scuttles its length
        IEnumerable<int> Procession()
        {
            foreach (var w in Ready("Briar Procession", ProcCd)) yield return w;
            Current = "Briar Procession"; presenter.PlayOnce("Procession", (ProcResolve + ProcTravel) / (float)Sim.Hz);
            var start = Sim.Flat(transform.position); var target = Farthest();
            var dir = Sim.Flat(target - start); if (dir.sqrMagnitude < 0.25f) dir = transform.forward; dir.Normalize();
            float travel = Mathf.Min(ProcessionTravel, Sim.Flat(target - start).magnitude + 1f);
            var end = Clamp(start + dir * travel, Bound);
            float yaw = Sim.YawTo(start, end, transform.eulerAngles.y);
            float len = Vector3.Distance(start, end) + BodyRadius;   // the danger covers where the body lands
            var h = Tune(Lane("Briar Procession", start, yaw, len, ProcessionWidth, ProcResolve, ProcDmg));
            h.OnResolve = hz => { W.BossFx.ChargeTrail(transform, true); Release(); };
            for (int i = 0; i < ProcResolve; i++) { TurnTowards(yaw, 480); Windup(i / (float)ProcResolve); yield return 1; }
            for (int i = 1; i <= ProcTravel; i++) { transform.position = Vector3.Lerp(start, end, i / (float)ProcTravel); yield return 1; }
            W.BossFx.ChargeTrail(transform, false); W.BossFx.ChargeLand(transform.position);
            foreach (var w in Recover(ProcRecovery)) yield return w;
        }

        // Threefold Bloom: flowers I, II, III on a 5 m ring, 4.2 m apart, opened in order 72 ticks apart; each has a
        // 102-tick warning and an 18-tick active period, then folds flat as spent (safe) space. 3 s punish after III.
        IEnumerable<int> Bloom()
        {
            foreach (var w in Ready("Threefold Bloom", BloomCd)) yield return w;
            Current = "Threefold Bloom"; presenter.PlayOnce("Bloom", 2f);
            var centres = BloomLayout(Sim.Flat(transform.position), Sim.YawTo(transform.position, Rotating(), transform.eulerAngles.y));
            var resolvedAt = new int[3];
            var spent = new bool[3];
            for (int f = 0; f < 3; f++)
            {
                int idx = f;
                var h = Tune(new Hazard
                {
                    Source = "Threefold Bloom " + Roman(f), Shape = HazardShape.Circle, Kind = HazardKind.Ground, Radius = BloomRadius,
                    LockAt = 0, ResolveAt = BloomWarn, ActiveTicks = BloomActive, Damage = BloomDmg, Origin = centres[f],
                });
                var bud = Prim.Make(PrimitiveKind.Cone, transform.parent, centres[f] + Vector3.up * 0.35f, new Vector3(0.45f, 0.7f, 0.45f), Ivory, 0.6f);
                props.Add(bud);
                RunHud.Float(centres[f] + Vector3.up * 1.4f, Roman(f), new Color(1f, 0.95f, 0.8f));
                h.OnResolve = hz => { resolvedAt[idx] = W.Tick; W.BossFx.ChimeResolve(hz.Origin, hz.Radius); Release(0.8f); };
                h.OnSpent = hz =>
                {
                    spent[idx] = true;
                    BloomLog.Add((idx, resolvedAt[idx], W.Tick));
                    if (bud) { bud.localScale = new Vector3(1.2f, 0.05f, 1.2f); bud.position = hz.Origin + Vector3.up * 0.03f; Prim.SetGlow(bud.GetComponent<Renderer>(), SpentPetal); }
                    var petals = Prim.Make(PrimitiveKind.Cylinder, transform.parent, hz.Origin + Vector3.up * 0.02f, new Vector3(BloomRadius * 2, 0.01f, BloomRadius * 2), SpentPetal);
                    props.Add(petals);
                };
                W.Hazards.Spawn(h);
                if (f < 2) yield return BloomStagger;
            }
            // flower III resolves 102 after its warning and is spent 18 later
            while (!spent[2]) { yield return 1; if (Dead) yield break; }
            BloomsCompleted++;
            Current = "punish"; W.Fx.Crescendo(centres[2], true, 0, false);   // harmless motes
            foreach (var w in Recover(BloomPunish)) yield return w;
            ClearProps();
        }

        static string Roman(int i) => i == 0 ? "I" : i == 1 ? "II" : "III";

        // the chain is centred on `yaw`; rotate it toward the arena centre until every flower sits fully inside
        Vector3[] BloomLayout(Vector3 boss, float yaw)
        {
            float step = 2 * Mathf.Asin(BloomSpacing / (2 * BloomRing)) * Mathf.Rad2Deg;
            float inward = Sim.YawTo(boss, Vector3.zero, yaw);
            var c = new Vector3[3];
            for (int attempt = 0; attempt <= 36; attempt++)
            {
                float y = Mathf.MoveTowardsAngle(yaw, inward, attempt * 10f);
                bool ok = true;
                for (int f = 0; f < 3; f++)
                {
                    c[f] = boss + Sim.Dir(y + (f - 1) * step) * BloomRing;
                    if (new Vector2(c[f].x, c[f].z).magnitude > CombatWorld.PlayerBound + Hazard.Margin - BloomRadius) ok = false;
                }
                if (ok) break;
            }
            return c;
        }

        public int PropCount => props.Count;
        void ClearProps() { foreach (var p in props) if (p) Destroy(p.gameObject); props.Clear(); }

        protected override void StepAlways()
        {
            if (!presenter) return;
            if (Current == "approach" || Current == "reposition") presenter.TickBoss("Walk");
            else if (Current == "recovery" || Current == "punish") presenter.TickBoss("Vulnerable");
            else presenter.TickBoss("Idle");
        }

        protected override void Update()
        {
            base.Update();
            PlaceLights();
            if (Dead && frontLight)   // defeat: the lights settle down with the residual core
            {
                frontLight.intensity = Mathf.MoveTowards(frontLight.intensity, FrontIntensity * 0.5f, Time.deltaTime * 2f);
                rimLight.intensity = Mathf.MoveTowards(rimLight.intensity, RimIntensity * 0.3f, Time.deltaTime * 3f);
            }
            if (presenter) presenter.GlowLevel = glowLevel;
            // idle: bells sway independently, the mask stays still
            if (bellRoot && !Dead)
                for (int b = 0; b < bellRoot.childCount; b++)
                    bellRoot.GetChild(b).localRotation = Quaternion.Euler(Mathf.Sin(Time.time * (1.3f + b * 0.37f)) * 8f, 0, Mathf.Cos(Time.time * (1.1f + b * 0.29f)) * 6f);
            // defeat: the model's Defeat clip seats the shrine; the stand-in just settles lower
            if (Dead && presenter && presenter.Visual && !presenter.HasModel)
                presenter.Sink = Mathf.MoveTowards(presenter.Sink, 0.9f, Time.deltaTime * 0.6f);
        }
        protected override void OnDefeat()
        {
            ClearProps();
            glowTarget = 0.3f; glowLevel = 2f;   // a small steady core remains
            presenter.PlayOnce("Defeat", 2.5f);
            W.Fx.Crescendo(transform.position, true, 2, true);
        }
        void OnDestroy() => ClearProps();
        public CharacterPresenter Presenter => presenter;
    }
}
