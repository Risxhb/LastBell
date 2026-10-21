using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

namespace LastBell.Characters
{
    // Presentation test for the Vanguard model (Resources/Characters/Vanguard.glb, built by
    // prototype/arena-3d/models/build-vanguard.ps1). Not gameplay: no damage, cooldowns, Resonance or hit tests; those
    // live in Core/ClassTest. It only sequences the kit's animations the way the rules would (combo chain, Dash Attack
    // window, Reversal priming) so they can be judged in the arena. Spawns itself when a scene with the key-art arena
    // enters Play mode.
    // Controls: WASD move (camera-relative), mouse aims, LMB Clockblade Combo (LMB out of a dodge = Dash Attack),
    // RMB dodge, Shift sprint, Space jump, 2 Pendulum Rush (Reversal Rush when primed), 3 Final Hour, 4 Guard,
    // X previews a Counter, V toggles the follow camera.
    // Clips are glTFast "Mecanim" clips driven through a PlayableGraph, so no Animator Controller asset is needed.
    public sealed class VanguardTestController : MonoBehaviour
    {
        const float RunSpeed = 4.2f, SprintSpeed = 6.5f, TurnSpeed = 720f, ArenaRadius = 11.5f;
        const float DodgeDistance = 4.5f, JumpMoveFactor = 0.8f, Fade = 0.1f;
        const float ChainWindow = 0.4f, DashAttackWindow = 0.1f, ReversalWindow = 2.5f;   // 24, 6 and 150 ticks
        static readonly Vector3 FollowOffset = new Vector3(0, 13f, 9.7f);   // the arena's gameplay angle, 45% of the distance

        // Forward travel baked into each action: metres moved between two normalized times of the clip.
        readonly struct Travel
        {
            public readonly float Distance, From, To;
            public Travel(float d, float from, float to) { Distance = d; From = from; To = to; }
        }
        static readonly Dictionary<string, Travel> Moves = new Dictionary<string, Travel>
        {
            ["Combo1"] = new Travel(0.35f, 0.25f, 0.55f), ["Combo2"] = new Travel(0.35f, 0.25f, 0.55f),
            ["Combo3"] = new Travel(0.6f, 0.3f, 0.6f), ["DashAttack"] = new Travel(1.5f, 0f, 0.5f),
            ["PendulumRush"] = new Travel(4f, 0.1f, 0.5f), ["ReversalRush"] = new Travel(4f, 0.1f, 0.45f),
            ["FinalHour"] = new Travel(0.3f, 0.3f, 0.45f), ["Counter"] = new Travel(0.3f, 0f, 0.3f),
        };

        // Hurt frames for test fights (e.g. Characters/Bellkeeper/EncounterTest.cs): fired once per attack, on the frame
        // the blade connects in the animation. Reach/arc/damage follow docs/design/vanguard-v2-implementation.md
        // converted at 1 legacy unit = 0.025 m. Final Hour reports its unmarked damage (no Reversal mark tracking here).
        // Counter and Guard deal no damage, as in the rules.
        public delegate void HurtFrame(string action, Vector3 origin, Vector3 forward, float reach, float arcDeg, int damage);
        public event HurtFrame OnHurtFrame;
        readonly struct Hit
        {
            public readonly float At, Reach, Arc; public readonly int Damage;   // At = normalized clip time of contact
            public Hit(float at, float reach, float arc, int damage) { At = at; Reach = reach; Arc = arc; Damage = damage; }
        }
        static readonly Dictionary<string, Hit> Hits = new Dictionary<string, Hit>
        {
            ["Combo1"] = new Hit(0.45f, 4.0f, 110, 2), ["Combo2"] = new Hit(0.45f, 4.0f, 110, 2),
            ["Combo3"] = new Hit(0.5f, 4.375f, 170, 4), ["DashAttack"] = new Hit(0.5f, 4.0f, 110, 3),
            ["PendulumRush"] = new Hit(0.6f, 2.75f, 160, 9), ["ReversalRush"] = new Hit(0.63f, 3.5f, 360, 15),
            ["FinalHour"] = new Hit(0.5f, 6.75f, 220, 18),
        };
        bool hitFired;

        enum Act { Locomotion, Action, Dodge, Jump }

        [SerializeField] AnimationClip[] clips;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        readonly Dictionary<string, int> index = new Dictionary<string, int>();
        float[] times, weights;
        int current = -1;
        Act act = Act.Locomotion;
        string actName = "";
        float actTime, actLength;
        Vector3 dodgeDir;
        int comboStep;                 // last combo hit played (0 = none)
        bool comboQueued;
        float sinceAction = 99, sinceDodge = 99, reversalReady;
        Camera cam;
        bool follow = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn()
        {
            if (FindAnyObjectByType<LastBell.Arenas.KeyArtArena>() == null || FindAnyObjectByType<VanguardTestController>() != null) return;
            var prefab = Resources.Load<GameObject>("Characters/Vanguard");
            if (prefab == null) { Debug.LogWarning("Vanguard test: Resources/Characters/Vanguard.glb not found"); return; }
            var go = Instantiate(prefab, new Vector3(0, 0, 6), Quaternion.Euler(0, 180, 0));
            go.name = "Vanguard (test)";
            var c = go.AddComponent<VanguardTestController>();
            c.clips = Resources.LoadAll<AnimationClip>("Characters/Vanguard");
            c.Init();
        }

        void Init()
        {
            cam = Camera.main;
            var animator = GetComponentInChildren<Animator>();
            if (animator == null) animator = gameObject.AddComponent<Animator>();
            animator.applyRootMotion = false;
            graph = PlayableGraph.Create("Vanguard");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, clips.Length);
            for (int i = 0; i < clips.Length; i++)
            {
                index[clips[i].name] = i;
                var p = AnimationClipPlayable.Create(graph, clips[i]);
                p.SetApplyFootIK(false);
                graph.Connect(p, 0, mixer, i);
            }
            AnimationPlayableOutput.Create(graph, "Vanguard", animator).SetSourcePlayable(mixer);
            times = new float[clips.Length]; weights = new float[clips.Length];
            graph.Play();
            Play("Idle");
            // glTFast doesn't read KHR_materials_emissive_strength, so restore the eye/rune glow strength here
            foreach (var r in GetComponentsInChildren<Renderer>())
                foreach (var m in r.materials)
                    if (m.HasProperty("emissiveFactor")) m.SetColor("emissiveFactor", m.GetColor("emissiveFactor") * 2.5f);
            Debug.Log($"Vanguard test: {clips.Length} clips ({string.Join(", ", index.Keys)})");
        }

        void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }

        // PlayerVitals disables this component while the player is down; come back in a clean state
        void OnEnable() { act = Act.Locomotion; comboStep = 0; comboQueued = false; }

        void Play(string name)
        {
            if (!index.TryGetValue(name, out int i) || i == current) return;
            current = i; times[i] = 0;
        }

        static bool Loops(string name) => name == "Idle" || name == "Run" || name == "Sprint";

        void Begin(Act a, string clip)
        {
            act = a; actName = clip; actTime = 0; hitFired = false;
            actLength = index.TryGetValue(clip, out int i) ? clips[i].length : 0.6f;
            current = -1; Play(clip);
        }

        void StartAction(string clip, Mouse mouse)
        {
            FaceCursor(mouse); comboQueued = false;
            Begin(Act.Action, clip);
        }

        void Update()
        {
            if (!graph.IsValid()) return;
            var kb = Keyboard.current; var mouse = Mouse.current;
            float dt = Time.deltaTime;
            sinceAction += dt; sinceDodge += dt; reversalReady = Mathf.Max(0, reversalReady - dt);

            // camera-relative WASD
            Vector3 fwd = cam ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector2 a = Vector2.zero;
            if (kb != null)
            {
                if (kb.wKey.isPressed) a.y += 1; if (kb.sKey.isPressed) a.y -= 1;
                if (kb.dKey.isPressed) a.x += 1; if (kb.aKey.isPressed) a.x -= 1;
                if (kb.vKey.wasPressedThisFrame) follow = !follow;
            }
            Vector3 move = (fwd * a.y + right * a.x); if (move.sqrMagnitude > 1) move.Normalize();
            bool sprint = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
            bool lmb = mouse != null && mouse.leftButton.wasPressedThisFrame;

            // Dash Attack: basic pressed during a dodge or just after it
            if (lmb && (act == Act.Dodge || (act == Act.Locomotion && sinceDodge < DashAttackWindow)))
            { StartAction("DashAttack", mouse); comboStep = 0; lmb = false; }

            // combo chain: a press during a combo hit queues the next one
            if (lmb && act == Act.Action && actName.StartsWith("Combo")) { comboQueued = true; lmb = false; }
            if (act == Act.Action && actName.StartsWith("Combo") && comboQueued && actTime >= actLength * 0.7f && comboStep < 3)
            { comboQueued = false; comboStep++; StartAction("Combo" + comboStep, mouse); }

            if (act == Act.Locomotion)
            {
                if (lmb)
                {
                    comboStep = sinceAction < ChainWindow && comboStep < 3 ? comboStep + 1 : 1;
                    StartAction("Combo" + comboStep, mouse);
                }
                else if (mouse != null && mouse.rightButton.wasPressedThisFrame)
                {
                    dodgeDir = move.sqrMagnitude > 0.01f ? move.normalized : transform.forward;
                    transform.rotation = Quaternion.LookRotation(dodgeDir); Begin(Act.Dodge, "Dodge");
                }
                else if (kb != null && kb.spaceKey.wasPressedThisFrame) Begin(Act.Jump, "Jump");
                else if (kb != null && kb.digit2Key.wasPressedThisFrame)
                {
                    bool reversal = reversalReady > 0; reversalReady = 0;
                    StartAction(reversal ? "ReversalRush" : "PendulumRush", mouse);
                }
                else if (kb != null && kb.digit3Key.wasPressedThisFrame) StartAction("FinalHour", mouse);
                else if (kb != null && kb.digit4Key.wasPressedThisFrame) StartAction("Guard", mouse);
                else if (kb != null && kb.xKey.wasPressedThisFrame) { StartAction("Counter", mouse); reversalReady = ReversalWindow; }
            }

            Vector3 velocity = Vector3.zero;
            switch (act)
            {
                case Act.Locomotion:
                    if (move.sqrMagnitude > 0.01f)
                    {
                        velocity = move * (sprint ? SprintSpeed : RunSpeed);
                        Turn(move, dt); Play(sprint ? "Sprint" : "Run");
                    }
                    else Play("Idle");
                    break;
                case Act.Action:
                    if (Moves.TryGetValue(actName, out var tr))
                    {
                        float u = actTime / actLength, span = Mathf.Max(0.01f, tr.To - tr.From);
                        if (u >= tr.From && u <= tr.To) velocity = transform.forward * (tr.Distance / (span * actLength));
                    }
                    break;
                case Act.Dodge:
                    // sine speed profile integrates to DodgeDistance over the clip
                    velocity = dodgeDir * (DodgeDistance * Mathf.PI / (2 * actLength)) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(actTime / actLength));
                    break;
                case Act.Jump:
                    velocity = move * (sprint ? SprintSpeed : RunSpeed) * JumpMoveFactor;
                    if (move.sqrMagnitude > 0.01f) Turn(move, dt);
                    break;
            }
            if (act == Act.Action && !hitFired && Hits.TryGetValue(actName, out var hit) && actTime + dt >= hit.At * actLength)
            {
                hitFired = true;
                OnHurtFrame?.Invoke(actName, transform.position, transform.forward, hit.Reach, hit.Arc, hit.Damage);
            }
            if (act != Act.Locomotion && (actTime += dt) >= actLength)
            {
                if (act == Act.Dodge) sinceDodge = 0;
                if (act == Act.Action)
                {
                    sinceAction = 0;
                    if (actName == "Combo3") { reversalReady = ReversalWindow; comboStep = 0; }
                }
                act = Act.Locomotion;
            }

            var pos = transform.position + velocity * dt; pos.y = 0;
            var flat = new Vector2(pos.x, pos.z);
            if (flat.magnitude > ArenaRadius) { flat = flat.normalized * ArenaRadius; pos.x = flat.x; pos.z = flat.y; }
            transform.position = pos;

            Animate(dt);
        }

        void Turn(Vector3 dir, float dt) =>
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), TurnSpeed * dt);

        void FaceCursor(Mouse mouse)
        {
            if (!cam || mouse == null) return;
            var ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float d))
            {
                var to = ray.GetPoint(d) - transform.position; to.y = 0;
                if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(to);
            }
        }

        void Animate(float dt)
        {
            for (int i = 0; i < clips.Length; i++)
            {
                weights[i] = Mathf.MoveTowards(weights[i], i == current ? 1 : 0, dt / Fade);
                if (weights[i] <= 0) continue;
                float len = clips[i].length;
                times[i] += dt;
                float t = Loops(clips[i].name) ? Mathf.Repeat(times[i], len) : Mathf.Min(times[i], len);
                mixer.GetInput(i).SetTime(t);
            }
            float sum = 0; foreach (var w in weights) sum += w;
            for (int i = 0; i < clips.Length; i++) mixer.SetInputWeight(i, sum > 0 ? weights[i] / sum : 0);
            graph.Evaluate(0);
        }

        void LateUpdate()
        {
            if (!follow || !cam) return;
            var target = transform.position + FollowOffset;
            cam.transform.position = Vector3.Lerp(cam.transform.position, target, 1 - Mathf.Exp(-8 * Time.deltaTime));
            cam.transform.LookAt(cam.transform.position - FollowOffset + Vector3.up * 1f);
        }

        void OnGUI()
        {
            GUI.Label(new Rect(12, 12, 900, 22), "Vanguard test · WASD move · LMB combo (after dodge: Dash Attack) · RMB dodge · Shift sprint · Space jump");
            GUI.Label(new Rect(12, 32, 900, 22), $"2 Pendulum Rush{(reversalReady > 0 ? " → REVERSAL READY" : "")} · 3 Final Hour · 4 Guard · X Counter preview · V camera");
        }
    }
}
