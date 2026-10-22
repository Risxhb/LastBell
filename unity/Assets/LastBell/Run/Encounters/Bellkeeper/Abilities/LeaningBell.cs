using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // Leaning Bell (`bellkeeper.leaning_bell`), Bellkeeper ability set v2 — proposal, built 2026-10-22.
    // Brief: bosses/4-shattered-hour-bellkeeper/abilities-v2/01-leaning-bell.md. The boss walks back to within 3 m of the
    // arena centre, heaves its shell over to one side and crashes it down: half of the arena, beyond a line through the
    // centre (not the boss), to the wall. One Strike cone from the centre, HalfAngle 90, radius past the map's farthest
    // point. Line and side lock on the warning's first tick and never move.
    //
    // Side (build interpretation, open question in the report): the lean axis runs from the arena centre toward the boss's
    // current target (the nearest standing player); the hit half is the side of the line holding more standing players,
    // a tie goes to the target's side, so solo = your side. Players within 0.3 m of the line count for neither side.
    // Warning: map-adaptive, max(3.5 s, 0.6 s + walk-and-dash time across the map's worst distance at 4.2 m/s), rounded
    // up to 0.1 s: Throne 3.5 s, square test map 4.4 s. Easy ×1.25 (4.4 / 5.5 s). Hard: unchanged here; The Unfinished
    // Hour replays the remembered half (Boss.RememberLean on resolve).
    public sealed class LeaningBellAbility : BellkeeperAbility
    {
        public override string Id => BellkeeperAbilities.LeaningBell;
        public override string Name => "Leaning Bell";

        public const int Damage = 40, StunTicks = 15, RecoveryTicks = 60, WalkCapTicks = 120;
        public const float Knockback = 1.5f, CastRange = 3f, WalkSpeed = 1.75f, Reaction = 0.6f, EscapeSpeed = 4.2f;
        public const float DashDistance = 3f, DashTime = 0.2f, MediumWarning = 3.5f, EasyScale = 1.25f;
        const float LineDeadZone = Hazard.Margin;

        // Tests and The Unfinished Hour: the locked half of the latest cast (world space).
        public Hazard Half { get; private set; }
        public Vector3 LeanDir { get; private set; }        // unit normal of the dividing line, pointing into the hit half
        public int WarningTicks { get; private set; }
        public int CountHit { get; private set; }           // standing players on each side at warning (tests)
        public int CountSafe { get; private set; }
        LeaningBellFx fx;
        public LeaningBellFx Fx => fx;                      // tests: presentation, gone after a cancel

        static float Far => CombatWorld.SquareBound ? CombatWorld.PlayerBound * Mathf.Sqrt(2) : CombatWorld.PlayerBound;
        // Worst run on the current map: from the far wall of the hit half to just past the line (+ player margin).
        public static float WorstDistance => Far + Hazard.Margin;
        // The cone's radius: past the farthest floor point.
        public static float Reach => Far + 0.5f;

        public static float WarningSeconds(Difficulty level)
        {
            float d = WorstDistance;
            float escape = d <= DashDistance ? DashTime * d / DashDistance : DashTime + (d - DashDistance) / EscapeSpeed;
            float w = Mathf.Max(MediumWarning, Ceil10(Reaction + escape));
            return level == Difficulty.Easy ? Ceil10(w * EasyScale) : w;
        }
        static float Ceil10(float s) => Mathf.Ceil(s * 10f - 1e-3f) / 10f;
        public static int WarningTicksFor(Difficulty level) => Mathf.RoundToInt(WarningSeconds(level) * Sim.Hz);

        // walk back to within 3 m of the centre (1.75 m/s, 2 s cap); nothing if already there
        public override IEnumerable<int> Setup()
        {
            var at = Sim.Flat(Boss.transform.position);
            if (at.magnitude <= CastRange) yield break;
            foreach (var w in Boss.Walk(at.normalized * (CastRange - 0.2f), WalkSpeed, WalkCapTicks)) yield return w;
        }

        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name);
            WarningTicks = WarningTicksFor(Level);
            LeanDir = PickSide(out int hit, out int safe); CountHit = hit; CountSafe = safe;
            float yaw = Quaternion.LookRotation(LeanDir).eulerAngles.y;

            var half = Spawn(new Hazard
            {
                Source = Name, Shape = HazardShape.Cone, Kind = HazardKind.Strike, Origin = Vector3.zero, Yaw = yaw,
                Radius = Reach, HalfAngle = 90, LockAt = 0, ResolveAt = WarningTicks, Damage = Damage,
                Knockback = Knockback, Stun = StunTicks,
            });
            Half = half;
            half.OnResolve = h =>
            {
                W.BossFx.SweepResolve(h.Origin, h.Yaw, h.Radius, h.HalfAngle);
                if (fx) fx.Crash();
                Boss.Flare(1.3f);
                Boss.RememberLean(h);
            };

            DestroyFx();
            fx = LeaningBellFx.Create(W, Boss.Presenter ? Boss.Presenter.Visual : null, LeanDir);
            if (Boss.Presenter) Boss.Presenter.PlayOnce("BellSweep", WarningTicks / (float)Sim.Hz + 0.3f);

            for (int i = 0; i < WarningTicks; i++)
            {
                float k = i / (float)WarningTicks;
                Boss.Glow(k); Boss.Face(yaw, 120);
                if (fx) fx.Windup = k;
                yield return 1;
            }
            // 1.0 s recovery (the arm hangs low after the heave); no punish window of its own
            yield return RecoveryTicks;
            DestroyFx();
        }

        // Direction into the hit half (see the header for the rule).
        Vector3 PickSide(out int hit, out int safe)
        {
            var target = Sim.Flat(Boss.TargetPosition);
            var axis = target.magnitude > 0.5f ? target.normalized : Sim.Flat(Boss.transform.forward).normalized;
            if (axis.sqrMagnitude < 0.5f) axis = Vector3.forward;
            int plus = 0, minus = 0;
            foreach (var p in W.Players)
            {
                if (p.Down) continue;
                float s = Vector3.Dot(Sim.Flat(p.transform.position), axis);
                if (s > LineDeadZone) plus++; else if (s < -LineDeadZone) minus++;
            }
            if (minus > plus) { hit = minus; safe = plus; return -axis; }
            hit = plus; safe = minus; return axis;
        }

        public override void Cancel()
        {
            base.Cancel();
            DestroyFx(); Half = null;
        }
        void DestroyFx() { if (fx) Object.Destroy(fx.gameObject); fx = null; }
    }

    // Stand-in presentation for Leaning Bell (no art yet): the shell tilts toward the hit half, a hard amber edge on the
    // dividing line, harmonic rays sweeping across the half, fractured clock-ring arcs in it, and on the crash a cracked
    // bell shock-front rolling from the line to the wall. Coral/amber only on the hit half. Purely visual; the hazard's
    // own telegraph is the hit shape. Real time (Update), so it plays at any combat speed.
    public sealed class LeaningBellFx : MonoBehaviour
    {
        static readonly Color Coral = new Color(1.0f, 0.36f, 0.26f), Amber = new Color(1.0f, 0.66f, 0.28f);
        const float MaxTilt = 24f, Floor = 0.035f;

        public float Windup;              // 0..1 across the warning, set by the ability each tick
        public float Tilt { get; private set; }     // tests: the shell's current lean, degrees
        Transform visual; Vector3 dir; Quaternion lastSet, lastBase;
        float crashT = -1, reach;
        Transform line;
        readonly List<Transform> rays = new List<Transform>(), arcs = new List<Transform>(), front = new List<Transform>();
        readonly List<float> rayAngle = new List<float>();
        readonly List<Material> mats = new List<Material>();
        readonly List<Mesh> meshes = new List<Mesh>();

        public static LeaningBellFx Create(CombatWorld w, Transform visual, Vector3 dir)
        {
            var go = new GameObject("Leaning Bell FX");
            if (w.BossFx) go.transform.SetParent(w.BossFx.transform, false);
            var fx = go.AddComponent<LeaningBellFx>();
            fx.visual = visual; fx.dir = dir; fx.Build();
            return fx;
        }

        // distance from the centre to the floor edge along d
        static float Edge(Vector3 d)
        {
            float b = CombatWorld.PlayerBound;
            return CombatWorld.SquareBound ? b / Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.z), 1e-3f) : b;
        }

        void Build()
        {
            var root = transform;
            root.SetPositionAndRotation(Vector3.zero, Quaternion.LookRotation(dir));
            reach = Edge(dir);

            // the dividing line: a crisp amber edge across the floor (local x), clipped to the map
            float half = Edge(Quaternion.Euler(0, 90, 0) * dir);
            line = Own(Prim.Make(PrimitiveKind.Cube, root, new Vector3(0, Floor, 0), new Vector3(2 * half, 0.02f, 0.14f), Amber, 2.2f));

            // harmonic rays across the hit half
            for (int i = 0; i < 7; i++)
            {
                rays.Add(Own(Prim.Make(PrimitiveKind.Cube, root, Vector3.zero, Vector3.one, Coral, 1.2f)));
                rayAngle.Add(Mathf.Lerp(-75, 75, i / 6f));
            }

            // fractured clock-ring arcs: three radii, each broken into four plates with staggered cracks and offsets
            float[] radii = { 0.32f, 0.6f, 0.86f };
            for (int ri = 0; ri < radii.Length; ri++)
                for (int s = 0; s < 4; s++)
                {
                    float a = -67.5f + 45f * s + (ri - 1) * 6f;
                    float r = radii[ri] * Edge(transform.rotation * (Quaternion.Euler(0, a, 0) * Vector3.forward)) + ((s + ri) % 2 == 0 ? 0.12f : -0.1f);
                    arcs.Add(Plate("Clock arc", FxMeshes.Sector(r - 0.07f, r + 0.07f, 19f, 16), a, Floor + 0.005f, Coral, 1f));
                }

            // the cracked shock-front (hidden until the crash): five plates of a half ring, scaled outward
            for (int s = 0; s < 5; s++)
            {
                var f = Plate("Shock front", FxMeshes.Sector(0.88f, 1f, 16.5f, 12), -72f + 36f * s, Floor + 0.01f, Amber, 2.6f);
                f.gameObject.SetActive(false); front.Add(f);
            }
            Update();
        }
        Transform Own(Transform t)
        {
            var mr = t.GetComponent<MeshRenderer>(); mats.Add(mr.sharedMaterial);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; return t;
        }
        Transform Plate(string name, Mesh mesh, float yaw, float y, Color c, float glow)
        {
            meshes.Add(mesh);
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0, y, 0); go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = Prim.Material(c, glow); mats.Add(mr.sharedMaterial);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        public void Crash() { if (crashT < 0) crashT = 0; }

        void Update()
        {
            float k = Mathf.Clamp01(Windup), t = Time.time;
            bool crashed = crashT >= 0;
            if (crashed) crashT += Time.deltaTime;

            // the line stays crisp and brightens as the shell commits
            line.gameObject.SetActive(!crashed || crashT < 0.15f);
            Glow(line, Amber * Mathf.Lerp(1.4f, 3.2f, k));

            // rays: grow out from the centre, sway a little across the half, brighten with the windup
            for (int i = 0; i < rays.Count; i++)
            {
                var r = rays[i]; r.gameObject.SetActive(!crashed);
                if (crashed) continue;
                float a = rayAngle[i] + 9f * Mathf.Sin(t * 2.4f + i * 0.9f);
                var d = Quaternion.Euler(0, a, 0) * Vector3.forward;
                float len = Edge(transform.rotation * d) * Mathf.Lerp(0.35f, 1f, Mathf.SmoothStep(0, 1, Mathf.Clamp01(k * 1.6f)));
                r.localRotation = Quaternion.Euler(0, a, 0);
                r.localPosition = d * (len * 0.5f) + Vector3.up * (Floor + 0.01f);
                r.localScale = new Vector3(0.07f + 0.05f * k, 0.015f, len);
                Glow(r, Coral * (0.6f + 1.8f * k) * (0.75f + 0.25f * Mathf.Sin(t * 7f + i)));
            }

            // clock arcs: pulse out of step with each other (fractured time), brighter with the windup
            for (int i = 0; i < arcs.Count; i++)
            {
                var a = arcs[i]; a.gameObject.SetActive(!crashed);
                if (!crashed) Glow(a, Coral * (0.4f + 1.4f * k * (0.6f + 0.4f * Mathf.Sin(t * 5f + i * 1.7f))));
            }

            // shock-front: rolls from the line to the wall in 0.35 s, plates staggered, then dies out
            if (crashed)
                for (int i = 0; i < front.Count; i++)
                {
                    var f = front[i];
                    float u = Mathf.Clamp01((crashT - 0.03f * (i % 2)) / 0.35f);
                    f.gameObject.SetActive(crashT < 0.5f);
                    float wall = Edge(transform.rotation * (Quaternion.Euler(0, -72f + 36f * i, 0) * Vector3.forward));
                    float d = Mathf.Max(0.5f, wall * (1 - (1 - u) * (1 - u)));
                    f.localScale = new Vector3(d, 1, d);
                    Glow(f, Amber * 3f * (1 - Mathf.Clamp01((crashT - 0.3f) / 0.2f)));
                }
        }

        // the shell heaves over toward the hit half, then drops back on the crash; applied after the presenter set the pose
        void LateUpdate()
        {
            if (!visual) return;
            float k = Mathf.Clamp01(Windup);
            float angle = crashT < 0 ? MaxTilt * Mathf.SmoothStep(0, 1, Mathf.Clamp01(k * 1.25f)) : MaxTilt * (1 - Mathf.Clamp01(crashT / 0.12f));
            var cur = visual.localRotation;
            var baseRot = cur == lastSet ? lastBase : cur;     // the presenter did not reset it this frame
            lastBase = baseRot;
            var parent = visual.parent ? visual.parent.rotation : Quaternion.identity;
            visual.rotation = Quaternion.AngleAxis(angle, Vector3.Cross(Vector3.up, dir)) * parent * baseRot;
            lastSet = visual.localRotation;
            Tilt = angle;
        }

        void OnDestroy()
        {
            if (visual && visual.localRotation == lastSet) visual.localRotation = lastBase;
            foreach (var m in mats) if (m) Destroy(m);
            foreach (var m in meshes) if (m) Destroy(m);
        }

        static void Glow(Transform t, Color c) => t.GetComponent<MeshRenderer>().sharedMaterial.SetColor("_EmissionColor", c);
    }
}
