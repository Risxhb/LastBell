using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // The final boss, shown with Bellkeeper 2.0 (Resources/Characters/Bellkeeper2.glb, from sheet 04-bellkeeper-v1.png,
    // built by prototype/arena-3d/models/build-bellkeeper2.ps1); the runtime ID stays "boss.bellkeeper". The fight is the
    // Bellkeeper Ultimate (Bellkeeper/Ultimate/BellkeeperUltimate.cs), run by the scheduler in Bellkeeper/BellkeeperScheduler.cs.
    // This file holds the model, the phase looks and defeat. (The v1 fight, spec v0.3, was deleted on 1 Nov, Chzy18.)
    public sealed partial class BellkeeperEncounter : BossEncounter
    {
        public override string Id => "boss.bellkeeper";
        public override string DisplayName => "The Bellkeeper";
        public override string Lesson => "Everything you learned, combined";
        public override bool Placeholder => !presenter || !presenter.HasModel;

        CharacterPresenter presenter;
        int phase = 1;

        void Awake() { MaxHp = DefaultBaseHp; BodyRadius = 1.4f; HitHeight = 2.2f; MarkHeight = 5.6f; }

        protected override void Build()
        {
            presenter = CharacterPresenter.Create(transform, "Bellkeeper2", 5f, 0.2f, 0, host =>   // Meshy metal map is near 1: too dark without reflections
            {
                Prim.Make(PrimitiveKind.Cone, host, new Vector3(0, 2.2f, 0), new Vector3(2.6f, 3.2f, 2.6f), new Color(0.6f, 0.45f, 0.2f));
                Prim.Make(PrimitiveKind.Sphere, host, new Vector3(0, 4.1f, 0.2f), Vector3.one * 0.7f, new Color(0.95f, 0.92f, 0.85f), 1f);
                return host;
            });
            BuildUltimate();
        }

        // practice phase row: the Ultimate's five HP phases P1-P5 (BellkeeperUltimate.cs)
        public override float[] PhaseStarts => UltPhaseStarts;
        public override int PhaseNumber => UltPhaseNumber;
        protected override void EnterPhase(int n) { UltEnterPhase(n); }

        protected override void StepAlways()
        {
            if (!presenter) return;
            if (Current == "approach" || Current == "reposition") presenter.TickBoss("Walk");
            else presenter.TickBoss("Idle");
        }

        // ---------------- phase looks (spec v0.3 §4, sheet 04-bellkeeper-v1.png) ----------------
        // Last Guardian: closed shell, compact rings. Broken Hour: shell halves parted, rings offset. Final Toll: chest
        // fully open, rings spread, brighter core. The chest also opens for every punish window and on defeat. The clips
        // keep the shell and ring offsets at rest, so this is layered on after the presenter evaluates each frame.
        static readonly float[] ShellOpen = { 0, 14, 42 }, ShellPart = { 0, 0.06f, 0.12f }, PhaseGlow = { 1, 1.5f, 2.2f };
        static readonly float[] RingOut = { 0, 0.18f, 0.4f }, RingUp = { 0, 0.04f, 0.2f }, RingTilt = { 0, 12, 25 }, BackRingUp = { 0, 0.1f, 0.25f };
        const float PunishOpen = 50, DefeatOpen = 70;
        Transform shellL, shellR, ringL, ringR, ringC; bool lookBound;
        public float ShellAngle { get; private set; }       // tests: current chest opening, degrees
        public float RingSpread { get; private set; }       // tests: current side-ring offset, metres
        public bool PhaseRigFound => shellL && shellR && ringL && ringR && ringC;
        float lookPhase;                                    // eased 0..2 between the phase looks

        void BindLook()
        {
            lookBound = true;
            if (!presenter || !presenter.Visual) return;
            foreach (var t in presenter.Visual.GetComponentsInChildren<Transform>(true))
            {
                switch (t.name)
                {
                    case "shell.L": shellL = t; break;
                    case "shell.R": shellR = t; break;
                    case "ring.L": ringL = t; break;
                    case "ring.R": ringR = t; break;
                    case "ring.C": ringC = t; break;
                }
            }
        }
        static float Lerp3(float[] v, float p) => p <= 1 ? Mathf.Lerp(v[0], v[1], p) : Mathf.Lerp(v[1], v[2], p - 1);

        void LateUpdate()
        {
            if (!lookBound) BindLook();
            if (!PhaseRigFound) return;
            float dt = Time.deltaTime;
            lookPhase = Mathf.MoveTowards(lookPhase, phase - 1, dt / 0.8f);    // the change plays across the 1.5 s shift
            float open = Dead ? DefeatOpen : Current == "recovery" ? Mathf.Max(PunishOpen, Lerp3(ShellOpen, lookPhase)) : Lerp3(ShellOpen, lookPhase);
            ShellAngle = Mathf.MoveTowards(ShellAngle, open, dt * (open > ShellAngle ? 120 : 60));
            RingSpread = Lerp3(RingOut, lookPhase);
            Vector3 up = transform.up, fwd = transform.forward, right = transform.right, c = transform.position;
            foreach (var shell in new[] { shellL, shellR })
            {
                // hinge at the bone head (the half's outer top corner): swing the inner edge forward and out
                var hinge = shell.position; var toAxis = Vector3.ProjectOnPlane(c - hinge, up);
                float s = Mathf.Sign(Vector3.Dot(Vector3.Cross(up, toAxis), fwd));
                shell.RotateAround(hinge, up, s * ShellAngle);
                shell.position += right * Mathf.Sign(Vector3.Dot(hinge - c, right)) * Lerp3(ShellPart, lookPhase);
            }
            foreach (var ring in new[] { ringL, ringR })
            {
                float side = Mathf.Sign(Vector3.Dot(ring.position - c, right));
                ring.RotateAround(ring.position, fwd, -side * Lerp3(RingTilt, lookPhase));   // tops lean outward
                ring.position += right * side * RingSpread + up * Lerp3(RingUp, lookPhase);
            }
            ringC.RotateAround(ringC.position, right, -0.4f * Lerp3(RingTilt, lookPhase));
            ringC.position += up * Lerp3(BackRingUp, lookPhase);
        }

        protected override void Update()
        {
            base.Update();
            if (presenter) presenter.GlowLevel = glowLevel * Lerp3(PhaseGlow, lookPhase);
            if (Dead && presenter && presenter.Visual)
            {
                // defeat: placeholder sink below the dial edge after the Defeat/Vulnerable pose
                defeatT += Time.deltaTime;
                if (defeatT > 1.2f) presenter.Sink = Mathf.MoveTowards(presenter.Sink, 1.5f, Time.deltaTime * 0.8f);
            }
        }
        float defeatT;
        protected override void OnDefeat()
        {
            StopAbilities();
            glowTarget = 0.1f; glowLevel = 3f;
            presenter.PlayOnce("Defeat", 2.5f);
            W.Fx.Crescendo(transform.position, true, 2, true);   // gold release burst; coral is reserved for danger
        }
        void OnDestroy() { StopAbilities(); }   // retry / practice restart: nothing of the encounter survives
        public CharacterPresenter Presenter => presenter;
    }
}
