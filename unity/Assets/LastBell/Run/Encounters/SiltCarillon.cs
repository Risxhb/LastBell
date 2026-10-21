using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Verdant Chimes miniboss 2 — the Silt Carillon, Warden of the Water Steps. Labelled STAND-IN: code-built silhouette,
    // schedule and numbers from docs/design/silt-carillon.md v0.1 (PR #56), Steady Ticking only.
    //   Phase 1 Clear Channels:  Spillway Note -> Silt Steps (I, II, III) -> Bellback, 60-tick walk/plant between.
    //   Phase 2 Uneven Flow:     Split Spillway -> Broken Steps (I, III, II) -> Basin Toll -> Bellback.
    // Hazards resolve on the first tick of the spec's active window: the shared HazardSystem has no active-duration
    // hazards yet (contract request R22). No knockback (spec D7), 9-tick hit-stun.
    public sealed class SiltCarillon : BossEncounter
    {
        public override string Id => "miniboss.silt_carillon";
        public override string DisplayName => "The Silt Carillon";
        public override string Lesson => "Shelter in the lane that just fired; read the numbered order";
        public const string Phase1 = "Clear Channels", Phase2 = "Uneven Flow";

        // spec §3 (ticks at 60 Hz, geometry from the 1.8 m collision boundary)
        const float Body = 1.8f;
        const int NoteTrack = 48, NoteResolve = 108, NoteRecovery = 21 + 168;
        const int SplitTrack = 54, SplitResolve = 114, SplitRecovery = 21 + 180;
        const int StepFirst = 108, StepGap1 = 84, StepGap2 = 90, StepRecovery1 = 15 + 216, StepRecovery2 = 15 + 228;
        const int BackTurn = 30, BackResolve = 90, BackRecovery = 18 + 150;
        const int TollResolve = 114, TollRecovery = 18 + 204;
        const int Walk = 48, Plant = 12, TransformTicks = 120;
        const int Medium = 18, Heavy = 26, HitStun = 9;
        const float NoteWidth = 2f, NoteLength = 9f, SplitWidth = 1.5f, SplitGap = 2.6f;
        const float StepWidth = 1.5f, StepLength = 7f, StepPitch = 2f;
        const float BackRange = 3.5f, BackHalf = 55f, TollInner = 2.5f, TollOuter = 5f;

        static readonly Color Coral = new Color(1f, 0.42f, 0.32f);
        public static readonly int[] OrderP1 = { 1, 2, 3 }, OrderP2 = { 1, 3, 2 };

        Transform body, shell, core; readonly Transform[] bells = new Transform[3]; readonly List<Transform> ferns = new List<Transform>();
        readonly float[] bellTilt = new float[3], bellDrop = new float[3]; float shellLift, fernGrow;
        Transform numeralRoot; readonly Transform[] numerals = new Transform[3]; readonly Hazard[] laneHazards = new Hazard[3];
        int nextLane = -1, splitSide = 1, rotation;
        bool phase2, transformed;

        // test hooks
        public int[] LastOrder { get; private set; }
        public readonly List<int> ResolvedLanes = new List<int>();
        public bool NumeralsVisible { get { foreach (var n in numerals) if (n && n.gameObject.activeSelf) return true; return false; } }

        void Awake() { MaxHp = 2400; BodyRadius = Body; HitHeight = 1.3f; MarkHeight = 3.3f; PhaseName = Phase1; }

        protected override void Build()
        {
            var stone = new Color(0.72f, 0.74f, 0.66f); var moss = new Color(0.36f, 0.5f, 0.26f);
            var brass = new Color(0.66f, 0.5f, 0.24f); var ivory = new Color(0.95f, 0.92f, 0.82f); var teal = new Color(0.3f, 0.6f, 0.6f);
            body = new GameObject("Body").transform; body.SetParent(transform, false);
            // four short brass-root legs at the corners of the 1.8 m footprint
            for (int i = 0; i < 4; i++)
            {
                float x = i % 2 == 0 ? -1.2f : 1.2f, z = i < 2 ? 1.0f : -1.0f;
                Prim.Make(PrimitiveKind.Cylinder, body, new Vector3(x, 0.5f, z), new Vector3(0.45f, 0.5f, 0.45f), brass, 0, Quaternion.Euler(z * 12, 0, -x * 10));
                Prim.Make(PrimitiveKind.Sphere, body, new Vector3(x * 1.08f, 0.1f, z * 1.08f), new Vector3(0.6f, 0.22f, 0.6f), brass);
            }
            // stone chest with a frontal arch (the bells hang inside it)
            Prim.Make(PrimitiveKind.Cube, body, new Vector3(0, 1.75f, -0.15f), new Vector3(2.9f, 0.55f, 2.2f), stone);
            Prim.Make(PrimitiveKind.Cube, body, new Vector3(-1.3f, 1.25f, 0.6f), new Vector3(0.3f, 0.7f, 0.5f), stone);
            Prim.Make(PrimitiveKind.Cube, body, new Vector3(1.3f, 1.25f, 0.6f), new Vector3(0.3f, 0.7f, 0.5f), stone);
            Prim.Make(PrimitiveKind.Cube, body, new Vector3(0, 2.05f, -0.1f), new Vector3(3.0f, 0.12f, 2.3f), moss);
            // rounded aqueduct shell on the back, with the rose window facing the rear (Bellback reads from it)
            shell = new GameObject("Shell").transform; shell.SetParent(body, false); shell.localPosition = new Vector3(0, 2.05f, -0.4f);
            Prim.Make(PrimitiveKind.Sphere, shell, new Vector3(0, 0.25f, 0), new Vector3(2.7f, 1.2f, 2.0f), stone);
            Prim.Make(PrimitiveKind.Cylinder, shell, new Vector3(0, 0.35f, -0.95f), new Vector3(0.8f, 0.05f, 0.8f), teal, 0.3f, Quaternion.Euler(90, 0, 0));
            Prim.Make(PrimitiveKind.Cube, shell, new Vector3(0, 0.75f, 0), new Vector3(0.25f, 0.2f, 1.9f), moss);
            // gold regulator core, hidden under the shell until Phase 2
            core = Prim.Make(PrimitiveKind.Sphere, body, new Vector3(0, 2.35f, -0.4f), new Vector3(0.7f, 0.7f, 0.7f), new Color(1f, 0.78f, 0.3f), 2f);
            // small ivory face above the bells = facing
            Prim.Make(PrimitiveKind.Sphere, body, new Vector3(0, 2.0f, 1.0f), new Vector3(0.4f, 0.5f, 0.2f), ivory, 0.3f);
            // three bells under the arch: left = lane I, centre = II, right = III
            for (int i = 0; i < 3; i++)
            {
                var b = new GameObject("Bell" + (i + 1)).transform; b.SetParent(body, false); b.localPosition = new Vector3((i - 1) * 0.8f, 1.5f, 0.55f);
                Prim.Make(PrimitiveKind.Cylinder, b, new Vector3(0, -0.3f, 0), new Vector3(0.55f, 0.35f, 0.55f), brass);
                Prim.Make(PrimitiveKind.Cylinder, b, new Vector3(0, -0.62f, 0), new Vector3(0.72f, 0.05f, 0.72f), teal);
                Prim.Make(PrimitiveKind.Sphere, b, new Vector3(0, -0.72f, 0), new Vector3(0.16f, 0.16f, 0.16f), brass);
                bells[i] = b;
            }
            // Phase 2 ferns (grow in on the transformation)
            for (int i = 0; i < 6; i++)
            {
                float a = (i * 60 + 30) * Mathf.Deg2Rad;
                ferns.Add(Prim.Make(PrimitiveKind.Cone, body, new Vector3(Mathf.Sin(a) * 1.3f, 2.2f, Mathf.Cos(a) * 1.0f - 0.2f), Vector3.zero, moss, 0, Quaternion.Euler(Mathf.Cos(a) * 35, 0, -Mathf.Sin(a) * 35)));
            }
            // lane numerals I / II / III on the floor: one to three coral strokes, shown only while that lane is live
            numeralRoot = new GameObject("SiltCarillonNumerals").transform; numeralRoot.SetParent(transform.parent, false);
            for (int i = 0; i < 3; i++)
            {
                var n = new GameObject("Numeral" + (i + 1)).transform; n.SetParent(numeralRoot, false);
                for (int s = 0; s <= i; s++) Prim.Make(PrimitiveKind.Cube, n, new Vector3((s - i / 2f) * 0.32f, 0.03f, 0), new Vector3(0.14f, 0.04f, 0.9f), Coral, 0.9f);
                n.gameObject.SetActive(false); numerals[i] = n;
            }
        }

        void OnDestroy() { if (numeralRoot) Destroy(numeralRoot.gameObject); }

        bool PhaseDue => !phase2 && Hp * 2 <= MaxHp;

        protected override IEnumerable<int> Script()
        {

            if (PhaseDue && !transformed) { foreach (var w in Transformation()) yield return w; }
            if (!phase2)
            {
                foreach (var w in Step(SpillwayNote(), NoteRecovery)) yield return w;
                foreach (var w in Step(Steps(OrderP1, StepGap1), StepRecovery1)) yield return w;
                foreach (var w in Step(Bellback(), BackRecovery)) yield return w;
            }
            else
            {
                foreach (var w in Step(SplitSpillway(), SplitRecovery)) yield return w;
                foreach (var w in Step(Steps(OrderP2, StepGap2), StepRecovery2)) yield return w;
                foreach (var w in Step(BasinToll(), TollRecovery)) yield return w;
                foreach (var w in Step(Bellback(), BackRecovery)) yield return w;
            }
        }

        // walk/plant slot, the attack, then its recovery (the transformation replaces what's left of the recovery, §4)
        IEnumerable<int> Step(IEnumerable<int> attack, int recovery)
        {
            if (PhaseDue) yield break;
            foreach (var w in WalkAndPlant()) yield return w;
            if (PhaseDue) yield break;
            foreach (var w in attack) yield return w;
            Current = "recovery"; glowTarget = 0.5f;
            for (int i = 0; i < recovery && !PhaseDue; i++) yield return 1;
            glowTarget = 1;
        }

        // short arc toward the centre that keeps the nearest player 3–6 m from the boundary; never retreats (§3)
        IEnumerable<int> WalkAndPlant()
        {
            Current = "walk";
            for (int i = 0; i < Walk; i++)
            {
                var to = Sim.Flat(Target() - transform.position); float gap = to.magnitude - Body;
                var move = (gap > 6f ? to.normalized : Vector3.zero) + Sim.Flat(-transform.position) * 0.06f;
                if (move.sqrMagnitude > 0.01f)
                {
                    var p = transform.position + Vector3.ClampMagnitude(move, 1f) * 1.5f * Sim.Dt;
                    if (new Vector2(p.x, p.z).magnitude <= CombatWorld.BossBound) transform.position = p;
                }
                TurnTowards(YawToTarget(), 150);
                yield return 1;
            }
            Current = "plant";
            for (int i = 0; i < Plant; i++) { TurnTowards(YawToTarget(), 150); yield return 1; }
        }

        PlayerController RotatingTarget()
        {
            var standing = new List<PlayerController>();
            foreach (var p in W.Players) if (!p.Down) standing.Add(p);
            if (standing.Count == 0) return null;
            return standing[rotation++ % standing.Count];
        }
        static Vector3 Left(float yaw) => Quaternion.Euler(0, yaw, 0) * Vector3.left;

        Hazard SpawnLane(string src, Vector3 from, float yaw, float length, float width, int lockAt, int resolveAt, HazardKind kind, int dmg)
        {
            var h = W.Hazards.Spawn(new Hazard { Source = src, Shape = HazardShape.Lane, Kind = kind, Origin = from, Yaw = yaw, Length = length, Width = width, LockAt = lockAt, ResolveAt = resolveAt, Damage = dmg });
            h.Knockback = 0; h.Stun = HitStun; return h;
        }
        float Clip(Vector3 from, float yaw, float length) => Mathf.Clamp(ToEdge(from, Sim.Dir(yaw), CombatWorld.PlayerBound + 0.5f), 1f, length);

        IEnumerable<int> SpillwayNote()
        {
            Current = "Spillway Note"; bellTilt[0] = 0;
            var target = RotatingTarget();
            Vector3 Front() => transform.position + transform.forward * Body;
            var h = SpawnLane("Spillway Note", Front(), transform.eulerAngles.y, NoteLength, NoteWidth, NoteTrack, NoteResolve, HazardKind.Strike, Medium);
            h.Track = hz =>
            {
                if (Dead) return;
                if (target && !target.Down) TurnTowards(Sim.YawTo(transform.position, target.transform.position, transform.eulerAngles.y), 120);
                hz.Origin = Front(); hz.Yaw = transform.eulerAngles.y; hz.Length = Clip(hz.Origin, hz.Yaw, NoteLength);
            };
            h.OnResolve = hz => { Release(0.8f); W.BossFx.ChimeResolve(hz.Origin + Sim.Dir(hz.Yaw) * hz.Length * 0.5f, 0.8f); };
            for (int i = 0; i < NoteResolve; i++) { bellTilt[0] = 35f * i / NoteResolve; Windup(i / (float)NoteResolve); yield return 1; }
            bellTilt[0] = 0;
        }

        IEnumerable<int> SplitSpillway()
        {
            Current = "Split Spillway";
            var target = RotatingTarget(); splitSide = -splitSide;
            float off = SplitGap / 2 + SplitWidth / 2;
            // one lane (alternating left/right) is aimed through the target: rotate the boss so that lane's line hits it
            float Aim()
            {
                float yaw = transform.eulerAngles.y;
                if (!target || target.Down) return yaw;
                var to = Sim.Flat(target.transform.position - transform.position); float dist = Mathf.Max(to.magnitude, off + 0.1f);
                return Sim.YawTo(transform.position, target.transform.position, yaw) + splitSide * Mathf.Rad2Deg * Mathf.Asin(off / dist);
            }
            Vector3 From(float side) => transform.position + transform.forward * Body + Left(transform.eulerAngles.y) * side * off;
            var a = SpawnLane("Split Spillway", From(1), transform.eulerAngles.y, NoteLength, SplitWidth, SplitTrack, SplitResolve, HazardKind.Strike, Medium);
            var b = SpawnLane("Split Spillway", From(-1), transform.eulerAngles.y, NoteLength, SplitWidth, SplitTrack, SplitResolve, HazardKind.Strike, Medium);
            a.Track = hz => { if (Dead) return; TurnTowards(Aim(), 120); hz.Origin = From(1); hz.Yaw = transform.eulerAngles.y; hz.Length = Clip(hz.Origin, hz.Yaw, NoteLength); };
            b.Track = hz => { if (Dead) return; hz.Origin = From(-1); hz.Yaw = transform.eulerAngles.y; hz.Length = Clip(hz.Origin, hz.Yaw, NoteLength); };
            a.OnResolve = hz => Release(0.8f);
            for (int i = 0; i < SplitResolve; i++) { bellTilt[0] = bellTilt[2] = 30f * i / SplitResolve; Windup(i / (float)SplitResolve); yield return 1; }
            bellTilt[0] = bellTilt[2] = 0;
        }

        // Silt Steps / Broken Steps: three numbered lanes, centres 2 m apart, resolving one at a time in `order` (§3, D2)
        IEnumerable<int> Steps(int[] order, int gap)
        {
            Current = phase2 ? "Broken Steps (I · III · II)" : "Silt Steps (I · II · III)";
            LastOrder = order; ResolvedLanes.Clear();
            // lane II at the rotating target; rotate toward the centre in 30° steps while more than half the band is clipped
            var target = RotatingTarget();
            float yaw = target ? Sim.YawTo(transform.position, target.transform.position, transform.eulerAngles.y) : transform.eulerAngles.y;
            float toCentre = Sim.YawTo(transform.position, Vector3.zero, yaw);
            for (int k = 0; k < 6 && ToEdge(transform.position + Sim.Dir(yaw) * Body, Sim.Dir(yaw), CombatWorld.PlayerBound) < StepLength / 2; k++)
                yaw = Mathf.MoveTowardsAngle(yaw, toCentre, 30);
            for (int i = 0; i < 20; i++) { TurnTowards(yaw, 360); yield return 1; }
            yaw = transform.eulerAngles.y;
            for (int lane = 1; lane <= 3; lane++)
            {
                int at = StepFirst; for (int k = 0; k < 3; k++) if (order[k] == lane) at = StepFirst + k * gap;
                var from = transform.position + Sim.Dir(yaw) * Body + Left(yaw) * (2 - lane) * StepPitch;
                int idx = lane - 1;
                var h = SpawnLane((phase2 ? "Broken Steps " : "Silt Steps ") + new string('I', lane), from, yaw, Clip(from, yaw, StepLength), StepWidth, 0, at, HazardKind.Ground, Medium);
                h.OnResolve = hz => { ResolvedLanes.Add(idx + 1); bellTilt[idx] = 0; W.BossFx.ChimeResolve(hz.Origin + Sim.Dir(hz.Yaw) * hz.Length * 0.5f, 0.6f); Release(0.5f); };
                laneHazards[idx] = h;
                numerals[idx].position = from + Sim.Dir(yaw) * Mathf.Min(h.Length * 0.7f, StepLength * 0.7f);
                numerals[idx].rotation = Quaternion.Euler(0, yaw, 0);
                numerals[idx].gameObject.SetActive(true);
            }
            int total = StepFirst + 2 * gap;
            for (int t = 0; t < total; t++)
            {
                int k = Mathf.Min(2, t < StepFirst ? 0 : 1 + (t - StepFirst) / gap);
                nextLane = order[k] - 1;
                bellTilt[nextLane] = 25f * Mathf.Sin(t * 0.25f);
                Windup(0.6f);
                yield return 1;
            }
            nextLane = -1;
        }

        IEnumerable<int> Bellback()
        {
            // visible, harmless turn putting the rear toward the nearest standing player, then the facing locks (D5, R26)
            Current = "Bellback (turn)";
            for (int i = 0; i < BackTurn; i++) { TurnTowards(YawToTarget() + 180, 360); shellLift = 0.4f * i / BackTurn; yield return 1; }
            Current = "Bellback";
            var h = W.Hazards.Spawn(new Hazard
            {
                Source = "Bellback", Shape = HazardShape.Cone, Kind = HazardKind.Strike, Origin = transform.position, Yaw = transform.eulerAngles.y + 180,
                Radius = Body + BackRange, HalfAngle = BackHalf, LockAt = 0, ResolveAt = BackResolve, Damage = Medium,
            });
            h.Knockback = 0; h.Stun = HitStun;
            h.OnResolve = hz => { Release(1f); shellLift = phase2 ? 0.6f : 0; };
            for (int i = 0; i < BackResolve; i++) { shellLift = (phase2 ? 0.6f : 0.4f) + 0.5f * i / BackResolve; Windup(i / (float)BackResolve); yield return 1; }
        }

        IEnumerable<int> BasinToll()
        {
            Current = "Basin Toll";
            var h = Ring("Basin Toll", transform.position, Body + TollInner, Body + TollOuter, TollResolve, Heavy);
            h.Knockback = 0; h.Stun = HitStun;
            h.OnResolve = hz => { Release(1.2f); W.BossFx.ChimeResolve(transform.position, 1.2f); };
            for (int i = 0; i < TollResolve; i++) { for (int b = 0; b < 3; b++) bellDrop[b] = 0.5f * i / TollResolve; Windup(i / (float)TollResolve); yield return 1; }
            for (int b = 0; b < 3; b++) bellDrop[b] = 0;
        }

        IEnumerable<int> Transformation()
        {
            transformed = true; Current = "transformation"; glowTarget = 2f;
            for (int i = 0; i < TransformTicks; i++) { shellLift = 0.6f * i / TransformTicks; fernGrow = i / (float)TransformTicks; yield return 1; }
            phase2 = true; SetPhase(Phase2); glowTarget = 1;
        }

        protected override void StepAlways()
        {
            // a numeral shows only while its lane is still pending; the spent lane reads clear (and nothing survives a reset)
            for (int i = 0; i < 3; i++)
            {
                bool live = laneHazards[i] != null && Contains(W.Hazards.Live, laneHazards[i]);
                if (numerals[i] && numerals[i].gameObject.activeSelf != live) numerals[i].gameObject.SetActive(live);
                if (!live) laneHazards[i] = null;
            }
        }
        static bool Contains(IReadOnlyList<Hazard> l, Hazard h) { for (int i = 0; i < l.Count; i++) if (l[i] == h) return true; return false; }

        protected override void Update()
        {
            base.Update();
            float dt = Time.deltaTime;
            if (shell) { shell.localRotation = Quaternion.Slerp(shell.localRotation, Quaternion.Euler(-40 * shellLift, 0, 0), dt * 6); shell.localPosition = Vector3.Lerp(shell.localPosition, new Vector3(0, 2.05f + 0.5f * shellLift, -0.4f - 0.3f * shellLift), dt * 6); }
            for (int i = 0; i < 3; i++)
            {
                if (!bells[i]) continue;
                // Phase 2 hangs the bells at staggered lengths (centre lowest)
                float stagger = phase2 ? (i == 1 ? 0.35f : i == 0 ? 0.1f : 0.2f) : 0;
                bells[i].localPosition = Vector3.Lerp(bells[i].localPosition, new Vector3((i - 1) * 0.8f, 1.5f - stagger - bellDrop[i], 0.55f), dt * 6);
                bells[i].localRotation = Quaternion.Slerp(bells[i].localRotation, Quaternion.Euler(bellTilt[i] + 3 * Mathf.Sin(Time.time * 1.7f + i), 0, 0), dt * 8);
            }
            if (core) Prim.SetGlow(core.GetComponent<Renderer>(), new Color(1f, 0.75f, 0.35f) * (phase2 ? 1.2f : 0.25f) * glowLevel);
            foreach (var f in ferns) if (f) f.localScale = Vector3.Lerp(f.localScale, new Vector3(0.35f, 0.8f, 0.35f) * fernGrow, dt * 4);
            if (nextLane >= 0 && numerals[nextLane]) numerals[nextLane].localScale = Vector3.one * (1f + 0.15f * Mathf.Sin(Time.time * 10f));
            for (int i = 0; i < 3; i++) if (i != nextLane && numerals[i]) numerals[i].localScale = Vector3.one;
            if (Dead && body) body.localPosition = Vector3.MoveTowards(body.localPosition, new Vector3(0, -0.5f, 0), dt * 0.5f);
        }
        protected override void OnDefeat() { glowTarget = 0.1f; nextLane = -1; foreach (var n in numerals) if (n) n.gameObject.SetActive(false); }
    }
}
