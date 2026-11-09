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

        void LateUpdate() => PresentLook(phase, Dead, Current, Time.deltaTime);

        // the shell and ring offsets for the eased phase look: the host from its own phase (LateUpdate), a LAN client's
        // replica from the host's (ReplicaPresent -> ReplicaLate, also after the presenter has evaluated the clip)
        void PresentLook(int targetPhase, bool dead, string current, float dt)
        {
            if (!lookBound) BindLook();
            if (!PhaseRigFound) return;
            lookPhase = Mathf.MoveTowards(lookPhase, targetPhase - 1, dt / 0.8f);    // the change plays across the 1.5 s shift
            float open = dead ? DefeatOpen : current == "recovery" ? Mathf.Max(PunishOpen, Lerp3(ShellOpen, lookPhase)) : Lerp3(ShellOpen, lookPhase);
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
            IntroPrebuild();
            PresentBody(glowLevel, Dead, Time.deltaTime);
        }
        // phase glow and the defeat sink (presenter values: read by the presenter's next Update)
        void PresentBody(float glow, bool dead, float dt)
        {
            if (presenter) presenter.GlowLevel = glow * Lerp3(PhaseGlow, lookPhase);
            if (dead && presenter && presenter.Visual)
            {
                // defeat: placeholder sink below the dial edge after the Defeat/Vulnerable pose
                defeatT += dt;
                if (defeatT > 1.2f) presenter.Sink = Mathf.MoveTowards(presenter.Sink, 1.5f, dt * 0.8f);
            }
        }

        // ---------------- LAN looks ----------------
        // Glow = core glow (before the phase factor); Look[0] = phase (1-3), Look[1] = the host's eased look phase (0-2).
        // The replica eases toward the phase itself and snaps to the host's look when it is far off (a practice start).
        // The shell layering must follow the presenter's clip evaluation, so a small enabled helper calls it in LateUpdate.
        int netPhase = 1; float netLook = -1, netGlow = 1; bool netDead; string netCurrent = "";
        ReplicaLate replicaLate;
        public int ReplicaLookFrames { get; private set; }   // LAN smoke tests: frames the replica drew the phase look
        public override void CaptureNetLook(Networking.LanWorldFrame.BossState into)
        {
            into.Glow = glowLevel;
            into.Look.Add(phase); into.Look.Add(lookPhase);
        }
        public override void ReplicaPresent(Networking.LanWorldFrame.BossState state, float dt)
        {
            var look = state.Look;
            if (look.Count > 0) netPhase = Mathf.Clamp(Mathf.RoundToInt(look[0]), 1, 3);
            if (look.Count > 1) netLook = Mathf.Clamp(look[1], 0, 2);
            netGlow = state.Glow; netDead = state.Dead; netCurrent = state.Attack ?? "";
            if (!replicaLate) { replicaLate = gameObject.AddComponent<ReplicaLate>(); replicaLate.Owner = this; }
        }
        void PresentReplica(float dt)
        {
            if (netLook >= 0 && Mathf.Abs(lookPhase - netLook) > 0.35f) lookPhase = netLook;
            PresentLook(netPhase, netDead, netCurrent, dt);
            PresentBody(netGlow, netDead, dt);
            ReplicaLookFrames++;
        }
        sealed class ReplicaLate : MonoBehaviour
        {
            public BellkeeperEncounter Owner;
            void LateUpdate() { if (Owner && Owner.IsNetReplica) Owner.PresentReplica(Time.deltaTime); }
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
