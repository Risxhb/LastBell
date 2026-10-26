using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    public enum OraclePose { Idle, Walk, StaffRaise, FloorStrike, ArmsSpread, Point, ClapperWindup, ClapperSwing, RingMetronome, Recover, Defeat }

    // Labelled code-built stand-in for the Chimebound Oracle until a real model and rig exist: a hooded robe, a brass
    // bell staff, a hanging clapper on the right arm and a ring of small bells round the waist. Each pose is a target
    // the parts ease toward, so the tells read from silhouette (staff up = lanes, staff down = wells, arms wide = soak
    // chain, arm out = mark, clapper back = cone). Presentation only: nothing here decides an outcome.
    public sealed class ChimeboundOracleRig : MonoBehaviour
    {
        public const float Height = 3.6f;
        static readonly Color Robe = new Color(0.16f, 0.15f, 0.22f), RobeTrim = new Color(0.62f, 0.48f, 0.24f), Brass = new Color(0.78f, 0.60f, 0.30f),
            Hood = new Color(0.10f, 0.10f, 0.15f), Iron = new Color(0.22f, 0.22f, 0.25f);
        public static readonly Color RuneGlow = new Color(0.55f, 0.85f, 1.0f), HasteGlow = new Color(1.0f, 0.72f, 0.35f);

        public OraclePose Current { get; private set; } = OraclePose.Idle;
        public bool Haste;
        public float Glow = 1;
        public Transform Body { get; private set; }
        public Transform Staff { get; private set; }

        Transform leftArm, rightArm, clapper, bellRing, face;
        readonly List<Renderer> glows = new List<Renderer>();
        readonly List<Transform> bells = new List<Transform>();
        MaterialPropertyBlock mpb;
        static readonly int PEmission = Shader.PropertyToID("_EmissionColor"), PBase = Shader.PropertyToID("_BaseColor");
        Quaternion lTarget = Quaternion.identity, rTarget = Quaternion.identity, staffTarget = Quaternion.identity;
        float bodyDip, bodyDipTarget, shake, walkPhase;
        int poseTicks, poseLength = 1;

        public static ChimeboundOracleRig Create(Transform parent)
        {
            var go = new GameObject("Chimebound Oracle (STAND-IN, labelled placeholder)");
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<ChimeboundOracleRig>(); r.Build(); return r;
        }

        Transform Part(PrimitiveKind k, Transform parent, Vector3 pos, Vector3 scale, Color c, float glow = 0, Vector3? euler = null, string name = null)
        {
            var t = Prim.Make(k, parent, pos, scale, c, glow, euler.HasValue ? Quaternion.Euler(euler.Value) : (Quaternion?)null);
            if (name != null) t.name = name;
            var mr = t.GetComponent<MeshRenderer>(); mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (glow > 0) glows.Add(mr);
            return t;
        }

        void Build()
        {
            mpb = new MaterialPropertyBlock();
            Body = new GameObject("Body").transform; Body.SetParent(transform, false);
            // robe: a wide cone skirt and a narrower torso, hem trim
            Part(PrimitiveKind.Cone, Body, new Vector3(0, 1.05f, 0), new Vector3(2.2f, 2.1f, 2.2f), Robe, name: "RobeSkirt");
            Part(PrimitiveKind.Cylinder, Body, new Vector3(0, 0.08f, 0), new Vector3(2.25f, 0.08f, 2.25f), RobeTrim, name: "Hem");
            Part(PrimitiveKind.Capsule, Body, new Vector3(0, 2.25f, 0), new Vector3(1.05f, 0.95f, 0.85f), Robe, name: "Torso");
            Part(PrimitiveKind.Cube, Body, new Vector3(0, 2.3f, 0.43f), new Vector3(0.18f, 1.2f, 0.04f), RobeTrim, name: "Stole");
            // hood and a glowing slit where a face would be
            Part(PrimitiveKind.Sphere, Body, new Vector3(0, 3.15f, 0), new Vector3(0.78f, 0.86f, 0.78f), Hood, name: "Hood");
            Part(PrimitiveKind.Cone, Body, new Vector3(0, 3.62f, -0.08f), new Vector3(0.5f, 0.5f, 0.5f), Hood, 0, new Vector3(-12, 0, 0), "HoodPeak");
            face = Part(PrimitiveKind.Cube, Body, new Vector3(0, 3.12f, 0.34f), new Vector3(0.36f, 0.07f, 0.06f), RuneGlow, 3f, name: "FaceSlit");
            // ring of small bells at the waist (they shake on the soak-chain tell)
            bellRing = new GameObject("WaistBells").transform; bellRing.SetParent(Body, false); bellRing.localPosition = new Vector3(0, 1.75f, 0);
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad; var p = new Vector3(Mathf.Sin(a) * 0.62f, 0, Mathf.Cos(a) * 0.62f);
                var b = new GameObject("Bell").transform; b.SetParent(bellRing, false); b.localPosition = p;
                Part(PrimitiveKind.Cone, b, new Vector3(0, -0.12f, 0), new Vector3(0.2f, 0.22f, 0.2f), Brass, name: "BellCup");
                bells.Add(b);
            }
            // arms
            leftArm = new GameObject("LeftArm").transform; leftArm.SetParent(Body, false); leftArm.localPosition = new Vector3(-0.62f, 2.65f, 0);
            Part(PrimitiveKind.Capsule, leftArm, new Vector3(0, -0.55f, 0), new Vector3(0.32f, 0.6f, 0.32f), Robe, name: "LeftSleeve");
            rightArm = new GameObject("RightArm").transform; rightArm.SetParent(Body, false); rightArm.localPosition = new Vector3(0.62f, 2.65f, 0);
            Part(PrimitiveKind.Capsule, rightArm, new Vector3(0, -0.55f, 0), new Vector3(0.32f, 0.6f, 0.32f), Robe, name: "RightSleeve");
            // the clapper: a heavy brass bob on a short chain, hanging from the right hand
            clapper = new GameObject("Clapper").transform; clapper.SetParent(rightArm, false); clapper.localPosition = new Vector3(0, -1.15f, 0);
            Part(PrimitiveKind.Cylinder, clapper, new Vector3(0, -0.3f, 0), new Vector3(0.06f, 0.3f, 0.06f), Iron, name: "ClapperChain");
            Part(PrimitiveKind.Sphere, clapper, new Vector3(0, -0.72f, 0), new Vector3(0.42f, 0.52f, 0.42f), Brass, name: "ClapperBob");
            // the staff in the left hand, topped by a bell and a rune ring
            Staff = new GameObject("Staff").transform; Staff.SetParent(leftArm, false); Staff.localPosition = new Vector3(0, -1.1f, 0.15f);
            Part(PrimitiveKind.Cylinder, Staff, new Vector3(0, 1.1f, 0), new Vector3(0.09f, 1.9f, 0.09f), Iron, name: "StaffShaft");
            Part(PrimitiveKind.Cone, Staff, new Vector3(0, 3.25f, 0), new Vector3(0.55f, 0.5f, 0.55f), Brass, 0, new Vector3(180, 0, 0), "StaffBell");
            Part(PrimitiveKind.Cylinder, Staff, new Vector3(0, 3.55f, 0), new Vector3(0.75f, 0.03f, 0.75f), RuneGlow, 2.5f, name: "StaffRuneRing");
            Part(PrimitiveKind.Sphere, Staff, new Vector3(0, 3.05f, 0), new Vector3(0.18f, 0.18f, 0.18f), RuneGlow, 3f, name: "StaffClapperGlow");
            // a labelled plate on the back so captures can never mistake the stand-in for final art
            Part(PrimitiveKind.Cube, Body, new Vector3(0, 2.2f, -0.45f), new Vector3(0.7f, 0.25f, 0.03f), new Color(0.9f, 0.85f, 0.7f), name: "STAND-IN plate");
        }

        // ticks: the pose's build-up length (windups ease in over it)
        public void Pose(OraclePose p, int ticks = 12)
        {
            if (Current == p && p != OraclePose.ClapperSwing && p != OraclePose.FloorStrike) return;
            Current = p; poseTicks = 0; poseLength = Mathf.Max(1, ticks);
            switch (p)
            {
                case OraclePose.StaffRaise: lTarget = Quaternion.Euler(0, 0, -165); rTarget = Quaternion.Euler(-20, 0, 25); staffTarget = Quaternion.identity; bodyDipTarget = 0; break;
                case OraclePose.FloorStrike: lTarget = Quaternion.Euler(-55, 0, -10); rTarget = Quaternion.Euler(-40, 0, 15); staffTarget = Quaternion.Euler(30, 0, 0); bodyDipTarget = 0.25f; break;
                case OraclePose.ArmsSpread: lTarget = Quaternion.Euler(0, 0, -95); rTarget = Quaternion.Euler(0, 0, 95); staffTarget = Quaternion.Euler(0, 0, 80); bodyDipTarget = 0; break;
                case OraclePose.Point: lTarget = Quaternion.Euler(-10, 0, -10); rTarget = Quaternion.Euler(-95, 0, 0); staffTarget = Quaternion.identity; bodyDipTarget = 0; break;
                case OraclePose.ClapperWindup: lTarget = Quaternion.Euler(-10, 0, -15); rTarget = Quaternion.Euler(60, 0, 40); staffTarget = Quaternion.identity; bodyDipTarget = 0.12f; break;
                case OraclePose.ClapperSwing: lTarget = Quaternion.Euler(-10, 0, -15); rTarget = Quaternion.Euler(-120, 0, -10); staffTarget = Quaternion.identity; bodyDipTarget = 0.05f; break;
                case OraclePose.RingMetronome: lTarget = Quaternion.Euler(-150, 0, -20); rTarget = Quaternion.Euler(-150, 0, 20); staffTarget = Quaternion.identity; bodyDipTarget = 0; break;
                case OraclePose.Defeat: lTarget = Quaternion.Euler(20, 0, -10); rTarget = Quaternion.Euler(20, 0, 10); staffTarget = Quaternion.Euler(70, 0, 0); bodyDipTarget = 0.8f; break;
                default: lTarget = Quaternion.Euler(-12, 0, -8); rTarget = Quaternion.Euler(-8, 0, 8); staffTarget = Quaternion.identity; bodyDipTarget = 0; break;
            }
            if (p == OraclePose.ArmsSpread) shake = 1;
        }

        // one combat tick (the encounter steps it; pose timing follows gameplay ticks, not frames)
        public void Step()
        {
            poseTicks++;
            if (Current == OraclePose.Walk) walkPhase += 0.22f;
        }

        void Update()
        {
            float k = 1 - Mathf.Exp(-Time.deltaTime * (Current == OraclePose.ClapperSwing || Current == OraclePose.FloorStrike ? 26f : 8f));
            if (leftArm) leftArm.localRotation = Quaternion.Slerp(leftArm.localRotation, lTarget, k);
            if (rightArm) rightArm.localRotation = Quaternion.Slerp(rightArm.localRotation, rTarget, k);
            if (Staff) Staff.localRotation = Quaternion.Slerp(Staff.localRotation, staffTarget, k);
            bodyDip = Mathf.Lerp(bodyDip, bodyDipTarget, k);
            float bob = Current == OraclePose.Walk ? Mathf.Abs(Mathf.Sin(walkPhase)) * 0.08f : Mathf.Sin(Time.time * 1.6f) * 0.04f;
            if (Body) Body.localPosition = new Vector3(0, bob - bodyDip, 0);
            // waist bells: shake on the soak-chain tell, otherwise sway gently
            shake = Mathf.MoveTowards(shake, Current == OraclePose.ArmsSpread ? 1 : 0, Time.deltaTime * 2);
            for (int i = 0; i < bells.Count; i++)
                if (bells[i]) bells[i].localRotation = Quaternion.Euler(Mathf.Sin(Time.time * (6 + 20 * shake) + i) * (4 + 26 * shake), 0, Mathf.Cos(Time.time * (5 + 18 * shake) + i * 1.3f) * (3 + 22 * shake));
            if (clapper) clapper.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 2.1f) * 6f, 0, 0);
            var c = (Haste ? HasteGlow : RuneGlow) * (1.2f + 1.2f * Mathf.Max(0, Glow - 1));
            foreach (var r in glows)
            {
                if (!r) continue;
                r.GetPropertyBlock(mpb); mpb.SetColor(PBase, c * 0.6f); mpb.SetColor(PEmission, c * 2.2f); r.SetPropertyBlock(mpb);
            }
        }
    }
}
