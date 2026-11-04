using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Aurelion, the Meridian Regent: the Skyward Spire area boss. Meshy model rigged in Blender (4.2 m). The fight is
    // ability set v4 (Aurelion/AurelionEncounter.V4.cs on the shared ability plumbing in Aurelion/AurelionEncounter.V2.cs);
    // the packet's v1 attacks and the v2-v3 phase plans were retired on 1 Nov (Chzy18) and their code removed on 2 Nov.
    // This file holds the boss's identity, health, the per-tick hooks, the model and the code-built stand-in.
    public sealed partial class AurelionEncounter : BossEncounter
    {
        public override string Id => "boss.aurelion";
        public override string DisplayName => "Aurelion, the Meridian Regent";
        public override string Lesson => "Watch the lock, take the flank, punish the lowered blade";

        public int Phase => phase;
        int phase = 1;

        // HP: Chzy18 2026-10-27 "beef up the health" (5-7 min target); was 10 000 (solo melee class 4:35, Arcanist 2:34 on Hard)
        void Awake() { MaxHp = 12000; BodyRadius = 1.1f; HitHeight = 2.6f; MarkHeight = 5.2f; }

        protected override bool PartyScaled => true;   // v4 is tuned with the co-op HP formula
        // 2026-10-27: 1.25 per extra fighter (was 0.7: 4 perfect bots cleared 31 000 HP in about 2:10, 8 in 2:15; 1.7 ran 4 bots
        // into the 6:30 enrage): 4 players 57 000, 8 players 117 000
        protected override float PartyStep => 1.25f;
        public override float NextPhaseFraction() => V4NextPhaseFraction();

        protected override IEnumerable<int> Script() => V4Script();

        protected override void StepAlways()
        {
            V4Clock();
            if (Light != null && W.CombatActive) Light.StepFalls();
            if (presenter) presenter.TickBoss(Current == "approach" || Current == "reposition" ? "Walk" : "Idle");
        }

        // ---------------- model: the Meshy Aurelion rigged in Blender (prototype/arena-3d/models/build-aurelion.ps1) ----------------
        // Clips are authored so their strike frame sits at the attack's resolve time; PlayOnce stretches a clip to the
        // given ticks, which keep the same proportions. Without the model the code-built stand-in below is used.
        CharacterPresenter presenter;
        public CharacterPresenter Presenter => presenter;
        public override bool Placeholder => !presenter || !presenter.HasModel;
        void Clip(string name, int ticks) { if (presenter) presenter.Action(name, ticks); }

        // ---------------- stand-in presentation (used only when Resources/Characters/Aurelion is missing) ----------------
        enum Pose { Neutral, Planted, Exposed }
        Pose pose;
        Transform body, glaive, head, pendulum, shutterL, shutterR, shoulderL, banners;
        readonly Transform[] arcs = new Transform[3];
        Renderer pendulumR; float swing, kneel;
        static readonly Color Ivory = new Color(0.93f, 0.9f, 0.82f), Gold = new Color(0.82f, 0.64f, 0.3f),
            Cobalt = new Color(0.2f, 0.32f, 0.85f), Navy = new Color(0.12f, 0.15f, 0.32f), Dark = new Color(0.08f, 0.08f, 0.12f);

        protected override void Build()
        {
            PhaseName = "Perfect Order";
            presenter = CharacterPresenter.Create(transform, "Aurelion", 3f, 0.8f, 0, host => { BuildStandIn(host); return host; });
            BindAbilities();
        }

        void BuildStandIn(Transform host)
        {
            body = new GameObject("Body").transform; body.SetParent(host, false);
            // legs: long, brass knee and hip joints
            foreach (float s in new[] { -1f, 1f })
            {
                Prim.Make(PrimitiveKind.Capsule, body, new Vector3(0.32f * s, 0.55f, 0), new Vector3(0.28f, 0.55f, 0.32f), Ivory);
                Prim.Make(PrimitiveKind.Sphere, body, new Vector3(0.32f * s, 1.12f, 0.02f), Vector3.one * 0.26f, Gold);
                Prim.Make(PrimitiveKind.Capsule, body, new Vector3(0.3f * s, 1.65f, 0), new Vector3(0.32f, 0.5f, 0.36f), Ivory);
                Prim.Make(PrimitiveKind.Sphere, body, new Vector3(0.3f * s, 2.15f, 0), Vector3.one * 0.3f, Gold);
                Prim.Make(PrimitiveKind.Cube, body, new Vector3(0.3f * s, 0.06f, 0.12f), new Vector3(0.22f, 0.12f, 0.5f), Ivory);
            }
            // narrow waist, belfry chest
            Prim.Make(PrimitiveKind.Cylinder, body, new Vector3(0, 2.3f, 0), new Vector3(0.45f, 0.18f, 0.4f), Navy);
            Prim.Make(PrimitiveKind.Cube, body, new Vector3(0, 2.95f, 0.02f), new Vector3(1.05f, 1.0f, 0.75f), Ivory);
            Prim.Make(PrimitiveKind.Cube, body, new Vector3(0, 2.95f, 0.39f), new Vector3(0.42f, 0.7f, 0.04f), Dark);   // lancet opening
            pendulum = new GameObject("Pendulum").transform; pendulum.SetParent(body, false); pendulum.localPosition = new Vector3(0, 3.25f, 0.33f);
            Prim.Make(PrimitiveKind.Cube, pendulum, new Vector3(0, -0.25f, 0), new Vector3(0.04f, 0.5f, 0.04f), Gold);
            pendulumR = Prim.Make(PrimitiveKind.Sphere, pendulum, new Vector3(0, -0.52f, 0), Vector3.one * 0.2f, Gold, 1.5f).GetComponent<Renderer>();
            shutterL = Prim.Make(PrimitiveKind.Cube, body, new Vector3(-0.11f, 2.95f, 0.43f), new Vector3(0.22f, 0.72f, 0.05f), Ivory);
            shutterR = Prim.Make(PrimitiveKind.Cube, body, new Vector3(0.11f, 2.95f, 0.43f), new Vector3(0.22f, 0.72f, 0.05f), Ivory);
            // broad architectural shoulders
            shoulderL = Prim.Make(PrimitiveKind.Cube, body, new Vector3(-0.78f, 3.35f, 0), new Vector3(0.6f, 0.36f, 0.7f), Ivory);
            Prim.Make(PrimitiveKind.Cube, body, new Vector3(0.78f, 3.35f, 0), new Vector3(0.6f, 0.36f, 0.7f), Ivory);
            foreach (float s in new[] { -1f, 1f })
                Prim.Make(PrimitiveKind.Capsule, body, new Vector3(0.85f * s, 2.75f, 0), new Vector3(0.22f, 0.5f, 0.22f), Ivory);
            // elongated mouthless mask, cobalt eye slits, brass brow
            head = new GameObject("Head").transform; head.SetParent(body, false); head.localPosition = new Vector3(0, 3.75f, 0.05f);
            Prim.Make(PrimitiveKind.Capsule, head, Vector3.zero, new Vector3(0.34f, 0.32f, 0.3f), Ivory);
            Prim.Make(PrimitiveKind.Cube, head, new Vector3(0, 0.12f, 0.12f), new Vector3(0.3f, 0.05f, 0.08f), Gold);
            foreach (float s in new[] { -1f, 1f })
                Prim.Make(PrimitiveKind.Cube, head, new Vector3(0.07f * s, 0.05f, 0.15f), new Vector3(0.08f, 0.025f, 0.02f), Cobalt, 4f);
            Prim.Make(PrimitiveKind.Cone, head, new Vector3(0, 0.42f, 0), new Vector3(0.12f, 0.25f, 0.12f), Gold);
            // three orrery arcs on independent pivots behind the shoulders
            for (int a = 0; a < 3; a++)
            {
                arcs[a] = new GameObject("Arc" + a).transform; arcs[a].SetParent(body, false);
                arcs[a].localPosition = new Vector3(0, 3.5f, -0.5f);
                float r = 1.0f + a * 0.28f;
                for (int k = 0; k <= 8; k++)
                {
                    float ang = Mathf.Lerp(-110, 110, k / 8f) * Mathf.Deg2Rad;
                    Prim.Make(PrimitiveKind.Cube, arcs[a], new Vector3(Mathf.Sin(ang) * r, Mathf.Cos(ang) * r, 0), new Vector3(0.09f, 0.26f, 0.05f), Gold, 0.4f, Quaternion.Euler(0, 0, -ang * Mathf.Rad2Deg));
                }
                Prim.Make(PrimitiveKind.Sphere, arcs[a], new Vector3(Mathf.Sin(1.92f) * r, Mathf.Cos(1.92f) * r, 0), Vector3.one * 0.14f, Gold);
            }
            // two navy waist banners, front and back, ending above the ankles
            banners = new GameObject("Banners").transform; banners.SetParent(body, false);
            Prim.Make(PrimitiveKind.Cube, banners, new Vector3(0, 1.3f, 0.3f), new Vector3(0.36f, 1.9f, 0.03f), Navy);
            Prim.Make(PrimitiveKind.Cube, banners, new Vector3(0, 1.3f, -0.3f), new Vector3(0.36f, 1.9f, 0.03f), Navy);
            // the clock-hand glaive: ivory blade, brass counterweight, held diagonally in the right hand
            glaive = new GameObject("Glaive").transform; glaive.SetParent(body, false); glaive.localPosition = new Vector3(0.95f, 2.4f, 0.25f);
            Prim.Make(PrimitiveKind.Cylinder, glaive, Vector3.zero, new Vector3(0.08f, 2.1f, 0.08f), Navy);
            Prim.Make(PrimitiveKind.Cube, glaive, new Vector3(0, 2.6f, 0), new Vector3(0.06f, 1.1f, 0.3f), Ivory, 0.5f);
            Prim.Make(PrimitiveKind.Sphere, glaive, new Vector3(0, 2.05f, 0), Vector3.one * 0.26f, Gold);
            Prim.Make(PrimitiveKind.Sphere, glaive, new Vector3(0, -2.1f, 0), Vector3.one * 0.3f, Gold);
        }

        protected override void Update()
        {
            base.Update();
            V2DevKeys();
            PresentLook(phase, pose, Dead, glowLevel, Time.deltaTime);
        }

        // the body glow and the stand-in's pendulum, glaive, head, arcs, shutters and kneel: the host from its own state
        // (Update), a LAN client's replica from the host's (ReplicaPresent)
        void PresentLook(int ph, Pose ps, bool dead, float glow, float dt)
        {
            if (presenter) presenter.GlowLevel = glow;
            if (!body) return;
            // one idle pendulum beat; it stops on defeat
            if (!dead) swing += dt * 2.4f;
            pendulum.localRotation = Quaternion.Euler(0, 0, dead ? 0 : Mathf.Sin(swing) * 18f);
            Prim.SetGlow(pendulumR, Gold * (0.6f + glow * (ph == 3 ? 1.4f : 0.6f)));

            // glaive pose follows the attack (presentation only: the hazards decide hits)
            Vector3 gEuler = ps == Pose.Neutral && !dead ? new Vector3(0, 0, -18) : Vector3.zero;
            glaive.localRotation = Quaternion.Slerp(glaive.localRotation, Quaternion.Euler(gEuler), dt * 8f);
            head.localRotation = Quaternion.Slerp(head.localRotation, Quaternion.identity, dt * 6f);

            // phases: cracked, tilted shoulder and independent arcs (2); chest shutters open (3)
            float t = Time.time;
            for (int a = 0; a < 3; a++)
            {
                Quaternion target;
                if (dead) target = Quaternion.Euler(0, 0, a * 25f - 25f) * Quaternion.Euler(70, 0, 0);
                else if (ph == 1) target = Quaternion.identity;
                else if (ph == 2) target = Quaternion.Euler(Mathf.Sin(t * 0.7f + a * 2.1f) * 25f, Mathf.Sin(t * 0.5f + a) * 20f, Mathf.Sin(t * 0.9f + a) * 35f);
                else target = Quaternion.Euler(0, (a - 1) * 35f, Mathf.Sin(t * 0.6f + a) * 15f);
                arcs[a].localRotation = Quaternion.Slerp(arcs[a].localRotation, target, dt * 3f);
                arcs[a].localScale = Vector3.one * (ph == 3 && !dead ? 1.25f : 1f);
            }
            shoulderL.localRotation = Quaternion.Slerp(shoulderL.localRotation, Quaternion.Euler(0, 0, ph >= 2 ? 16 : 0), dt * 4f);
            float open = ph == 3 || ps == Pose.Exposed ? 1 : 0;
            shutterL.localPosition = Vector3.Lerp(shutterL.localPosition, new Vector3(-0.11f - 0.26f * open, 2.95f, 0.43f), dt * 4f);
            shutterR.localPosition = Vector3.Lerp(shutterR.localPosition, new Vector3(0.11f + 0.26f * open, 2.95f, 0.43f), dt * 4f);

            // defeat: plants the glaive and kneels
            kneel = Mathf.MoveTowards(kneel, dead ? 1 : 0, dt * 1.2f);
            body.localPosition = new Vector3(0, -0.9f * kneel, 0);
            body.localRotation = Quaternion.Euler(12f * kneel, 0, 0);
        }

        // LAN looks: Glow = body glow; Look[0] = phase (1-4), Look[1] = stand-in pose (0 neutral, 1 planted, 2 exposed).
        // The swing, arc drift and easing run on the client's own clock. (Total Eclipse's umbra is a telegraph already.)
        public int ReplicaPhase { get; private set; } = 1;   // LAN smoke tests: the phase the replica draws
        public override void CaptureNetLook(Networking.LanWorldFrame.BossState into)
        {
            into.Glow = glowLevel;
            into.Look.Add(phase); into.Look.Add((int)pose);
        }
        public override void ReplicaPresent(Networking.LanWorldFrame.BossState state, float dt)
        {
            var look = state.Look;
            if (look.Count > 0) ReplicaPhase = Mathf.Clamp(Mathf.RoundToInt(look[0]), 1, 4);
            var ps = Pose.Neutral;
            if (look.Count > 1) { int k = Mathf.RoundToInt(look[1]); if (k == (int)Pose.Planted) ps = Pose.Planted; else if (k == (int)Pose.Exposed) ps = Pose.Exposed; }
            PresentLook(ReplicaPhase, ps, state.Dead, state.Glow, dt);
        }

        protected override void OnDefeat()
        {
            glowTarget = 0.1f; glowLevel = 2.5f; pose = Pose.Planted; Clip("Defeat", 150);
            W.Fx.Crescendo(transform.position, true, 2, true);
        }
    }
}
