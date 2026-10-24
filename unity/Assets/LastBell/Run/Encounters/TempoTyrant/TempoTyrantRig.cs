using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    public enum TyrantPose { Idle, Walk, Rush, Windup, SlashLeft, SlashRight, Overhead, ThrustWindup, Thrust, RingCast, GroundStrike, Recover, Frenzy, Defeat }

    // LABELLED STAND-IN for The Tempo Tyrant (docs/metronome-arena/tempo-tyrant.md): a code-built clockwork knight with
    // the same readable silhouette as the greatsword sheet (3.5 m, ivory plate, brass trim, dark joints, narrow closed
    // helmet, short split tabard, clean backplate) and a broad straight two-handed greatsword. No metronome, pendulum,
    // clock face or tempo machinery is attached to him. Poses are procedural transforms, not authored animation.
    //
    // Presentation only: the encounter calls Pose() on its ticks with the gameplay durations and Step() every combat
    // tick (so poses freeze with pause). Strikes snap to their impact pose on the resolve tick. Seams are dim teal in
    // Normal and amber in Haste on identical geometry. Materials are cached per process and shared, so repeated
    // attempts never accumulate materials; emission changes go through MaterialPropertyBlocks.
    public sealed class TempoTyrantRig : MonoBehaviour
    {
        public const float Height = 3.5f;
        static readonly Color Ivory = new Color(0.86f, 0.82f, 0.72f), Brass = new Color(0.72f, 0.53f, 0.26f),
            Joint = new Color(0.11f, 0.11f, 0.13f), Tabard = new Color(0.09f, 0.10f, 0.13f), Steel = new Color(0.80f, 0.80f, 0.78f),
            Grip = new Color(0.20f, 0.13f, 0.08f);
        public static readonly Color TealSeam = new Color(0.25f, 1.0f, 0.85f), AmberSeam = new Color(1.0f, 0.62f, 0.18f);

        static readonly Dictionary<int, Material> cache = new Dictionary<int, Material>();
        static readonly int PEmission = Shader.PropertyToID("_EmissionColor"), PBase = Shader.PropertyToID("_BaseColor");

        public bool Haste = true, Frenzy;
        public float Glow = 1, RodPhase;
        public TyrantPose Current { get; private set; } = TyrantPose.Idle;
        public Transform Body { get; private set; }
        public Transform Sword { get; private set; }
        public Transform BladeTip { get; private set; }

        Transform arms, fissure; Renderer fissureR;
        readonly List<Renderer> seams = new List<Renderer>();
        MaterialPropertyBlock mpb;
        int poseTicks, poseLength; float pulse, frenzyAmount, walkPhase;
        Quaternion armsRot = Quaternion.identity, swordRot = Quaternion.Euler(130, 0, 0);
        Vector3 bodyOffset; float bodyLean;

        public static Material Mat(Color c, bool glow)
        {
            int key = (glow ? 1 : 0) ^ (Mathf.RoundToInt(c.r * 255) << 1) ^ (Mathf.RoundToInt(c.g * 255) << 9) ^ (Mathf.RoundToInt(c.b * 255) << 17);
            if (cache.TryGetValue(key, out var m) && m) return m;
            m = Prim.Material(c, glow ? 2f : 0);
            m.name = "TempoTyrant_" + key;
            cache[key] = m; return m;
        }

        Transform Part(PrimitiveKind k, Transform parent, Vector3 pos, Vector3 scale, Color c, bool glow = false, Vector3? euler = null, string name = null)
        {
            var go = new GameObject(name ?? k.ToString());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = scale; go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            go.AddComponent<MeshFilter>().sharedMesh = Prim.MeshOf(k);
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = Mat(c, glow);
            if (glow) seams.Add(r);
            return go.transform;
        }

        public static TempoTyrantRig Create(Transform boss)
        {
            var host = new GameObject("Visual (STAND-IN: Tempo Tyrant)");
            host.transform.SetParent(boss, false);
            var rig = host.AddComponent<TempoTyrantRig>();
            rig.BuildBody();
            return rig;
        }

        void BuildBody()
        {
            mpb = new MaterialPropertyBlock();
            Body = new GameObject("Body").transform; Body.SetParent(transform, false);
            var b = Body;
            // legs: greaves over dark joints, sabatons
            for (int s = -1; s <= 1; s += 2)
            {
                Part(PrimitiveKind.Cube, b, new Vector3(0.34f * s, 0.42f, 0.02f), new Vector3(0.40f, 0.80f, 0.48f), Ivory, name: "Greave");
                Part(PrimitiveKind.Sphere, b, new Vector3(0.34f * s, 0.86f, 0.06f), new Vector3(0.34f, 0.30f, 0.34f), Joint, name: "Knee");
                Part(PrimitiveKind.Cube, b, new Vector3(0.34f * s, 1.22f, 0.0f), new Vector3(0.44f, 0.62f, 0.50f), Ivory, name: "Cuisse");
                Part(PrimitiveKind.Cube, b, new Vector3(0.34f * s, 0.08f, 0.12f), new Vector3(0.42f, 0.16f, 0.70f), Brass, name: "Sabaton");
                Part(PrimitiveKind.Cube, b, new Vector3(0.34f * s, 0.86f, 0.25f), new Vector3(0.05f, 0.05f, 0.05f), TealSeam, true, name: "KneeSeam");
            }
            // hips, short split dark tabard (front and back halves, split down the middle)
            Part(PrimitiveKind.Cube, b, new Vector3(0, 1.55f, 0), new Vector3(0.95f, 0.30f, 0.60f), Joint, name: "Hips");
            for (int s = -1; s <= 1; s += 2)
            {
                Part(PrimitiveKind.Cube, b, new Vector3(0.21f * s, 1.20f, 0.32f), new Vector3(0.38f, 0.75f, 0.04f), Tabard, name: "TabardFront");
                Part(PrimitiveKind.Cube, b, new Vector3(0.21f * s, 1.20f, -0.32f), new Vector3(0.38f, 0.70f, 0.04f), Tabard, name: "TabardBack");
            }
            Part(PrimitiveKind.Cube, b, new Vector3(0, 1.72f, 0.31f), new Vector3(0.98f, 0.07f, 0.04f), Brass, name: "BeltTrim");
            // torso: tapered ivory cuirass, brass trim, seams; the backplate stays clean
            Part(PrimitiveKind.Cube, b, new Vector3(0, 2.05f, 0), new Vector3(0.95f, 0.60f, 0.66f), Ivory, name: "Fauld");
            Part(PrimitiveKind.Cube, b, new Vector3(0, 2.50f, 0), new Vector3(1.22f, 0.62f, 0.78f), Ivory, name: "Cuirass");
            Part(PrimitiveKind.Cube, b, new Vector3(0, 2.50f, 0.40f), new Vector3(0.10f, 0.58f, 0.03f), Brass, name: "ChestRidge");
            Part(PrimitiveKind.Cube, b, new Vector3(0, 2.18f, 0.335f), new Vector3(0.90f, 0.04f, 0.02f), TealSeam, true, name: "WaistSeam");
            for (int s = -1; s <= 1; s += 2)
                Part(PrimitiveKind.Cube, b, new Vector3(0.38f * s, 2.50f, 0.395f), new Vector3(0.04f, 0.46f, 0.02f), TealSeam, true, name: "ChestSeam");
            Part(PrimitiveKind.Cylinder, b, new Vector3(0, 2.86f, 0), new Vector3(0.36f, 0.08f, 0.36f), Joint, name: "Gorget");
            // narrow closed helmet with a glowing visor slit and brass crest ridge
            Part(PrimitiveKind.Capsule, b, new Vector3(0, 3.17f, 0.02f), new Vector3(0.40f, 0.62f, 0.46f), Ivory, name: "Helmet");
            Part(PrimitiveKind.Cube, b, new Vector3(0, 3.24f, 0.225f), new Vector3(0.24f, 0.035f, 0.03f), TealSeam, true, name: "Visor");
            Part(PrimitiveKind.Cube, b, new Vector3(0, 3.40f, -0.01f), new Vector3(0.06f, 0.16f, 0.48f), Brass, name: "Crest");
            // pauldrons
            for (int s = -1; s <= 1; s += 2)
            {
                Part(PrimitiveKind.Sphere, b, new Vector3(0.80f * s, 2.72f, 0), new Vector3(0.72f, 0.48f, 0.74f), Ivory, name: "Pauldron");
                Part(PrimitiveKind.Cube, b, new Vector3(0.84f * s, 2.62f, 0), new Vector3(0.50f, 0.05f, 0.70f), Brass, name: "PauldronTrim");
            }
            // arms + greatsword: one pivot between the shoulders so both hands stay on the grip in every pose
            arms = new GameObject("ArmsPivot").transform; arms.SetParent(b, false); arms.localPosition = new Vector3(0, 2.55f, 0.05f);
            var gripAt = new Vector3(0, -0.70f, 0.62f);
            for (int s = -1; s <= 1; s += 2)
            {
                var sh = new Vector3(0.78f * s, 0.05f, 0); var hand = gripAt + new Vector3(0.06f * s, 0.10f * (s > 0 ? 1 : -1), 0);
                var mid = (sh + hand) / 2; var dir = hand - sh;
                var arm = Part(PrimitiveKind.Cube, arms, mid, new Vector3(0.24f, dir.magnitude, 0.26f), Joint, name: "Arm");
                arm.localRotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
                Part(PrimitiveKind.Cube, arms, hand, new Vector3(0.22f, 0.22f, 0.24f), Brass, name: "Gauntlet");
            }
            Sword = new GameObject("Greatsword").transform; Sword.SetParent(arms, false); Sword.localPosition = gripAt;
            // wrapped two-hand grip (centred between the hands), brass crossguard and pommel, broad straight blade,
            // tapered point, subtle teal fuller. Blade runs along local +Y.
            Part(PrimitiveKind.Cylinder, Sword, new Vector3(0, 0, 0), new Vector3(0.09f, 0.32f, 0.09f), Grip, name: "Grip");
            Part(PrimitiveKind.Sphere, Sword, new Vector3(0, -0.38f, 0), new Vector3(0.17f, 0.17f, 0.17f), Brass, name: "Pommel");
            Part(PrimitiveKind.Cube, Sword, new Vector3(0, 0.36f, 0), new Vector3(0.78f, 0.10f, 0.14f), Brass, name: "Crossguard");
            Part(PrimitiveKind.Cube, Sword, new Vector3(0, 1.62f, 0), new Vector3(0.30f, 2.40f, 0.06f), Steel, name: "Blade");
            Part(PrimitiveKind.Cube, Sword, new Vector3(0, 1.45f, 0.032f), new Vector3(0.06f, 1.9f, 0.01f), TealSeam, true, name: "Fuller");
            Part(PrimitiveKind.Cube, Sword, new Vector3(0, 1.45f, -0.032f), new Vector3(0.06f, 1.9f, 0.01f), TealSeam, true, name: "FullerBack");
            var tip = Part(PrimitiveKind.Cube, Sword, new Vector3(0, 2.93f, 0), new Vector3(0.212f, 0.212f, 0.058f), Steel, false, new Vector3(0, 0, 45), "Tip");
            BladeTip = tip;
            // world-space fissure glow for Sundering Wake (hidden until a charge ends)
            fissure = new GameObject("Wake fissure (cosmetic)").transform;
            fissure.SetParent(transform.parent ? transform.parent.parent : null, false);
            var crack = Part(PrimitiveKind.Cube, fissure, new Vector3(0, 0.03f, 0.5f), new Vector3(0.22f, 0.05f, 1f), AmberSeam, false, name: "Crack");
            fissureR = crack.GetComponent<Renderer>(); fissureR.sharedMaterial = Mat(AmberSeam, true);
            fissure.gameObject.SetActive(false);
            ApplyPose(true);
        }

        // ---------------- encounter hooks ----------------
        public void Pose(TyrantPose p, int ticks = 0)
        {
            if (Current == TyrantPose.Defeat) return;
            if (p == Current && ticks == 0) return;
            Current = p; poseTicks = 0; poseLength = Mathf.Max(0, ticks);
        }
        public void Step() { poseTicks++; if (Current == TyrantPose.Walk || Current == TyrantPose.Rush) walkPhase += Sim.Dt * (Haste ? 9f : 7f); }
        public void Tick(bool accent) { pulse = accent ? 1.6f : 0.7f; }
        public void Fissure(bool on, Vector3 from = default, Vector3 to = default)
        {
            if (!fissure) return;
            fissure.gameObject.SetActive(on);
            if (!on) return;
            var d = Sim.Flat(to - from); float len = Mathf.Max(0.1f, d.magnitude);
            fissure.SetPositionAndRotation(Sim.Flat(from), Quaternion.LookRotation(d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.forward));
            fissure.localScale = new Vector3(1, 1, len);
        }

        void OnDestroy() { if (fissure) Destroy(fissure.gameObject); }

        // pose targets: (arms pivot euler, sword local euler, body offset y, lean deg)
        static (Vector3 arms, Vector3 sword, float y, float lean) Target(TyrantPose p, float k)
        {
            switch (p)
            {
                case TyrantPose.Walk: return (new Vector3(0, 0, 0), new Vector3(125, 0, 0), 0, 6);
                case TyrantPose.Rush: return (new Vector3(10, 0, 0), new Vector3(100, 0, -20), -0.15f, 22);
                case TyrantPose.Windup: return (new Vector3(-10, 0, 0), new Vector3(60, 0, 0), -0.1f, 0);
                case TyrantPose.SlashLeft: return k < 1 ? (new Vector3(0, 70, 0), new Vector3(90, 0, -30), -0.2f, 4) : (new Vector3(0, -85, 0), new Vector3(90, 0, 10), -0.3f, 10);
                case TyrantPose.SlashRight: return k < 1 ? (new Vector3(0, -70, 0), new Vector3(90, 0, 30), -0.2f, 4) : (new Vector3(0, 85, 0), new Vector3(90, 0, -10), -0.3f, 10);
                case TyrantPose.Overhead: return k < 1 ? (new Vector3(-40, 0, 0), new Vector3(-20, 0, 0), 0, -6) : (new Vector3(20, 0, 0), new Vector3(118, 0, 0), -0.45f, 18);
                case TyrantPose.ThrustWindup: return (new Vector3(0, 25, 0), new Vector3(90, 0, 0), -0.25f, 8);
                case TyrantPose.Thrust: return (new Vector3(5, 0, 0), new Vector3(92, 0, 0), -0.2f, 24);
                case TyrantPose.RingCast: return (new Vector3(-55, 0, 0), new Vector3(-5, 0, 0), 0.05f, -4);
                case TyrantPose.GroundStrike: return k < 1 ? (new Vector3(-50, 0, 0), new Vector3(-10, 0, 0), 0, -4) : (new Vector3(25, 0, 0), new Vector3(165, 0, 0), -0.55f, 20);
                case TyrantPose.Recover: return (new Vector3(15, 0, 0), new Vector3(150, 0, 0), -0.25f, 12);
                case TyrantPose.Frenzy: return (new Vector3(-20, 30, 0), new Vector3(40, 0, -25), -0.3f, -8);
                case TyrantPose.Defeat: return (new Vector3(30, 0, 0), new Vector3(175, 0, 0), -0.95f, 28);
                default: return (Vector3.zero, new Vector3(130, 0, 0), 0, 0);
            }
        }

        void ApplyPose(bool snap)
        {
            // timed strikes hold the windup until the last 5 ticks before impact, then whip through
            float k = poseLength > 0 ? (poseTicks >= poseLength - 5 ? 1 : 0) : 1;
            var t = Target(Current, k);
            float rate = snap ? 1 : Mathf.Clamp01(Time.deltaTime * (k >= 1 && poseLength > 0 ? 22f : 7f));
            armsRot = Quaternion.Slerp(armsRot, Quaternion.Euler(t.arms), rate);
            swordRot = Quaternion.Slerp(swordRot, Quaternion.Euler(t.sword), rate);
            bodyOffset.y = Mathf.Lerp(bodyOffset.y, t.y, rate); bodyLean = Mathf.Lerp(bodyLean, t.lean, rate);
            float bob = Current == TyrantPose.Walk || Current == TyrantPose.Rush ? Mathf.Abs(Mathf.Sin(walkPhase)) * 0.08f : 0;
            Body.localPosition = new Vector3(0, bodyOffset.y + bob, 0);
            Body.localRotation = Quaternion.Euler(bodyLean, 0, 0);
            arms.localRotation = armsRot; Sword.localRotation = swordRot;
        }

        void Update()
        {
            if (Body == null || mpb == null) return;   // a script reload drops the code-built state
            ApplyPose(false);
            pulse = Mathf.MoveTowards(pulse, 0, Time.deltaTime * 4f);
            frenzyAmount = Mathf.MoveTowards(frenzyAmount, Frenzy ? 1 : 0, Time.deltaTime);
            var c = (Haste ? AmberSeam : TealSeam) * ((Haste ? 2.2f : 0.9f) + 0.6f * Glow + pulse + 1.2f * frenzyAmount);
            mpb.SetColor(PEmission, c); mpb.SetColor(PBase, Haste ? AmberSeam : TealSeam);
            foreach (var r in seams) if (r) r.SetPropertyBlock(mpb);
            if (fissureR && fissure.gameObject.activeSelf)
            {
                mpb.SetColor(PEmission, AmberSeam * (2.5f + Mathf.Sin(Time.time * 18) * 0.8f)); mpb.SetColor(PBase, AmberSeam);
                fissureR.SetPropertyBlock(mpb);
            }
        }
    }
}
