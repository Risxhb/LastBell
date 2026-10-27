using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace LastBell.Run
{
    // Plays a character's glTF clips (glTFast "Mecanim" import) through a PlayableGraph, the same way the test
    // controllers do, so no Animator Controller asset is needed. Root motion is off: gameplay moves the transform and
    // the clips are stretched to the gameplay durations they are asked to fill. When a clip is missing (e.g. Hit / Down
    // / GetUp on a model that hasn't been authored yet), a labelled procedural placeholder moves the visual pivot instead.
    public sealed class CharacterPresenter : MonoBehaviour
    {
        // semantic name -> clip names to try, in order (approved-kit name first, then reused older clips)
        static readonly Dictionary<string, string[]> Fallbacks = new Dictionary<string, string[]>
        {
            ["SteelRhythm1"] = new[] { "SteelRhythm1", "Combo1" }, ["SteelRhythm2"] = new[] { "SteelRhythm2", "Combo2" },
            ["SteelRhythm3"] = new[] { "SteelRhythm3", "Combo3" },
            ["SunderingBlow"] = new[] { "SunderingBlow", "PendulumRush" }, ["LastWord"] = new[] { "LastWord", "ReversalRush", "FinalHour" },
            ["BraceHold"] = new[] { "BraceHold", "Guard" }, ["BraceWalk"] = new[] { "BraceWalk", "BraceHold", "Guard" },
            ["BraceRaise"] = new[] { "BraceRaise" }, ["BraceLower"] = new[] { "BraceLower" }, ["BraceHit"] = new[] { "BraceHit" },
            ["PhaseShift"] = new[] { "PhaseShift", "Vulnerable" }, ["Defeat"] = new[] { "Defeat", "Vulnerable" },
            // Arcanist arcane kit: the new clips first, then the closest clip of the earlier kit
            ["MissileTap"] = new[] { "MissileTap", "Clockbolt" }, ["MissileTap2"] = new[] { "MissileTap2", "Clockbolt2" },
            ["MissileCharge"] = new[] { "MissileCharge", "TimeSeal" }, ["MissileVolley"] = new[] { "MissileVolley", "Clockbolt" },
            ["DisintegrateStart"] = new[] { "DisintegrateStart", "ArcLance" }, ["DisintegrateHold"] = new[] { "DisintegrateHold", "ArcLance" },
            ["DisintegrateEnd"] = new[] { "DisintegrateEnd", "Clockbolt" }, ["RuneSigil"] = new[] { "RuneSigil", "TimeSeal" },
            ["AstralOrb"] = new[] { "AstralOrb", "TemporalAnchor" }, ["ArcaneSurge"] = new[] { "ArcaneSurge", "AstralOrb", "TemporalAnchor" }, ["ArcaneComet"] = new[] { "ArcaneComet", "Convergence" },
            ["BlinkIn"] = new[] { "BlinkIn", "AnchorReturn" },
        };
        static readonly HashSet<string> Looping = new HashSet<string> { "Idle", "Run", "Sprint", "Walk", "Vulnerable", "BraceHold", "BraceWalk", "Guard", "Float" };

        public Transform Visual { get; private set; }
        public bool HasModel { get; private set; }
        public readonly List<string> Placeholders = new List<string>();   // semantic clips shown procedurally (manifest/HUD)

        PlayableGraph graph; AnimationMixerPlayable mixer;
        AnimationClip[] clips; readonly Dictionary<string, int> index = new Dictionary<string, int>();
        float[] times, weights, speeds; int current = -1;
        string oneShot; float oneShotLeft;
        int brace;                       // 0 off, 1 raised, 2 lowering
        string idleClip = "Idle";

        // locomotion inputs set by the controller each tick
        public float Locomotion; public bool Sprinting; public float HeightOffset, Sink; public float RunClipSpeed = 4.2f;

        // procedural placeholder state (Hit / Down / GetUp / missing model)
        float flinch, downAmount, downTarget, yaw;
        readonly List<(Material m, Color c)> glow = new List<(Material, Color)>();
        public float GlowLevel = 1;

        public static CharacterPresenter ForPlayer(Transform parent, string className) =>
            Create(parent, className, className == "Chimesinger" ? 2f : 2.5f, 1f, 0, PlaceholderBody(className), className == "Arcanist");   // astrolabe / rune glow restored like the Vanguard's

        public static CharacterPresenter Create(Transform parent, string resource, float glowBoost, float metalScale, float yawOffset, System.Func<Transform, Transform> placeholder, bool castLayers = false)
        {
            var host = new GameObject("Visual"); host.transform.SetParent(parent, false);
            host.transform.localRotation = Quaternion.Euler(0, yawOffset, 0);
            var p = host.AddComponent<CharacterPresenter>();
            p.Visual = host.transform; p.yaw = yawOffset; p.layered = castLayers;
            var prefab = Resources.Load<GameObject>("Characters/" + resource);
            if (prefab != null)
            {
                var model = Instantiate(prefab, host.transform, false); model.name = resource;
                p.HasModel = true;
                p.InitGraph(model, Resources.LoadAll<AnimationClip>("Characters/" + resource), glowBoost, metalScale);
            }
            else
            {
                if (placeholder != null) placeholder(host.transform);
                p.clips = new AnimationClip[0]; p.layered = false;
                Debug.LogWarning($"Last Bell: Resources/Characters/{resource} missing, using a placeholder body");
            }
            return p;
        }

        void InitGraph(GameObject model, AnimationClip[] loaded, float glowBoost, float metalScale)
        {
            foreach (var r in model.GetComponentsInChildren<Renderer>())
                foreach (var m in r.materials)
                {
                    // glTFast drops KHR_materials_emissive_strength; restore a readable glow (same factors as the tests)
                    if (m.HasProperty("emissiveFactor")) { m.SetColor("emissiveFactor", m.GetColor("emissiveFactor") * glowBoost); glow.Add((m, m.GetColor("emissiveFactor"))); }
                    if (metalScale != 1 && m.HasProperty("metallicFactor")) m.SetFloat("metallicFactor", m.GetFloat("metallicFactor") * metalScale);
                }
            clips = loaded;
            var animator = model.GetComponentInChildren<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.applyRootMotion = false;
            graph = PlayableGraph.Create("Presenter " + model.name);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, clips.Length);
            for (int i = 0; i < clips.Length; i++)
            {
                index[clips[i].name] = i;
                var c = AnimationClipPlayable.Create(graph, clips[i]); c.SetApplyFootIK(false);
                graph.Connect(c, 0, mixer, i);
            }
            times = new float[clips.Length]; weights = new float[clips.Length]; speeds = new float[clips.Length];
            if (layered) BuildCastLayers(animator); else AnimationPlayableOutput.Create(graph, "Presenter", animator).SetSourcePlayable(mixer);
            graph.Play();
            SetLoop(idleClip);
        }

        void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }

        public bool Has(string semantic) => Resolve(semantic) >= 0;
        int Resolve(string semantic)
        {
            if (index.TryGetValue(semantic, out int i)) return i;
            if (Fallbacks.TryGetValue(semantic, out var names)) foreach (var n in names) if (index.TryGetValue(n, out i)) return i;
            return -1;
        }

        void SetLoop(string semantic, float speed = 1)
        {
            int i = Resolve(semantic); if (i < 0) i = Resolve("Idle");
            if (i < 0) return;
            if (i != current) { current = i; times[i] = 0; }
            speeds[i] = speed;
        }

        // One-shot clip stretched to fill exactly `ticks` of gameplay time. Returns false when no clip exists.
        public bool Action(string semantic, int ticks, float startAt = 0) => PlayOnce(semantic, ticks / (float)Sim.Hz, startAt);
        public bool PlayOnce(string semantic, float seconds, float startAt = 0)
        {
            int i = Resolve(semantic);
            if (i < 0) { NotePlaceholder(semantic); return false; }
            float len = clips[i].length * (1 - startAt);
            current = i; times[i] = clips[i].length * startAt; speeds[i] = len / Mathf.Max(0.05f, seconds);
            oneShot = clips[i].name; oneShotLeft = seconds;
            return true;
        }
        void NotePlaceholder(string s) { if (!Placeholders.Contains(s)) Placeholders.Add(s); }

        public void Dash() => PlayOnce("Dodge", 0.3f, 0.05f);
        public void Jump() => PlayOnce("Jump", 0.62f, 0.2f);
        public void Land() { if (!PlayOnce("Land", 0.2f)) { } }
        public void Brace(int state)
        {
            brace = state;
            if (state == 1) { if (!Action("BraceRaise", 6)) oneShotLeft = 0; }
            else if (state == 2) { if (!Action("BraceLower", 9)) { oneShotLeft = 0; } }
            else oneShotLeft = 0;
        }
        public void BraceHit() { if (!Action("BraceHit", 12)) flinch = 0.4f; }
        public void Hit() { if (!PlayOnce("Hit", 0.3f)) flinch = 1; }
        public void Down() { EndOverlay(); downTarget = 1; if (PlayOnce("Down", 0.8f)) { downTarget = 0; oneShotLeft = 9999; } }
        public void GetUp() { downTarget = 0; if (!PlayOnce("GetUp", 0.9f)) { downAmount = 1; } }
        public void ResetPose() { downTarget = downAmount = 0; flinch = 0; brace = 0; oneShotLeft = 0; GlowLevel = 1; SetLoop(idleClip); EndOverlay(); SetHidden(false); }
        public void SetIdleClip(string name) { idleClip = name; if (oneShotLeft <= 0) SetLoop(name); }

        // called on the combat tick by its owner; picks the locomotion loop when no one-shot is playing
        public void Tick(PlayerController pc)
        {
            if (oneShotLeft > 0) return;
            if (brace == 1) { SetLoop(Locomotion > 0.3f ? "BraceWalk" : "BraceHold"); return; }
            if (Locomotion > 0.3f) SetLoop(Sprinting ? "Sprint" : "Run", Sprinting ? Locomotion / 6.5f : Locomotion / RunClipSpeed);
            else SetLoop(idleClip);
        }

        public void TickBoss(string loop, float speed = 1) { if (oneShotLeft <= 0) SetLoop(loop, speed); }

        void Update()
        {
            float dt = Time.deltaTime;
            if (oneShotLeft > 0 && oneShotLeft < 9000) oneShotLeft -= dt;
            // placeholders: flinch tilts back, down lays the body over (labelled in the manifest)
            flinch = Mathf.MoveTowards(flinch, 0, dt / 0.3f);
            downAmount = Mathf.MoveTowards(downAmount, downTarget, dt / 0.45f);
            if (Visual != null)
            {
                Visual.localPosition = new Vector3(0, HeightOffset - 0.25f * downAmount - Sink, 0);
                Visual.localRotation = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(-12 * Mathf.Sin(flinch * Mathf.PI) - 82 * downAmount, 0, 0);
            }
            foreach (var (m, c) in glow) m.SetColor("emissiveFactor", c * GlowLevel);
            if (!graph.IsValid() || clips.Length == 0) return;
            for (int i = 0; i < clips.Length; i++)
            {
                weights[i] = Mathf.MoveTowards(weights[i], i == current ? 1 : 0, dt / 0.08f);
                if (weights[i] <= 0) continue;
                float len = clips[i].length;
                times[i] += dt * (speeds[i] <= 0 ? 1 : speeds[i]);
                // named loops, plus any clip whose name ends in "Loop" (the Dial Warden's held poses: SkyLoop, HandLoop ...)
                float t = Looping.Contains(clips[i].name) || clips[i].name.EndsWith("Loop") ? Mathf.Repeat(times[i], len) : Mathf.Min(times[i], len);
                mixer.GetInput(i).SetTime(t);
            }
            float sum = 0; foreach (var w in weights) sum += w;
            for (int i = 0; i < clips.Length; i++) mixer.SetInputWeight(i, sum > 0 ? weights[i] / sum : 0);
            if (layered) UpdateCastLayers(dt);
            graph.Evaluate(0);
        }

        // ---------------------------------------------------------------- cast layers (opt-in, the Arcanist)
        // A cast clip plays on two extra layers: an upper-body copy (spine, arms, head and the astrolabe's orbit bone)
        // that always shows, and a full-body copy that shows only while he stands still. So a cast keeps its planted
        // legs when he is still, and his legs keep walking when he moves. The base layer (locomotion, one-shots like the
        // dash) is unchanged. Overlay() starts a cast clip; EndOverlay() blends it out.
        bool layered;
        AnimationLayerMixerPlayable layers; AnimationMixerPlayable fullMix, upperMix;
        float[] ovTimes, ovWeights; int ov = -1; float ovLeft, ovSpeed, ovW, stillW = 1; bool ovLoop;
        Renderer[] renderers; bool hidden;
        public bool Overlaying => ov >= 0 && ovLeft > 0;
        public string OverlayClip => Overlaying ? clips[ov].name : "-";

        void BuildCastLayers(Animator animator)
        {
            fullMix = AnimationMixerPlayable.Create(graph, clips.Length); upperMix = AnimationMixerPlayable.Create(graph, clips.Length);
            for (int i = 0; i < clips.Length; i++)
            {
                var a = AnimationClipPlayable.Create(graph, clips[i]); a.SetApplyFootIK(false); graph.Connect(a, 0, fullMix, i);
                var b = AnimationClipPlayable.Create(graph, clips[i]); b.SetApplyFootIK(false); graph.Connect(b, 0, upperMix, i);
            }
            layers = AnimationLayerMixerPlayable.Create(graph, 3);
            graph.Connect(mixer, 0, layers, 0); graph.Connect(fullMix, 0, layers, 1); graph.Connect(upperMix, 0, layers, 2);
            layers.SetInputWeight(0, 1); layers.SetInputWeight(1, 0); layers.SetInputWeight(2, 0);
            var mask = new AvatarMask(); mask.AddTransformPath(animator.transform, true);
            int upper = 0;
            for (int i = 0; i < mask.transformCount; i++)
            {
                var path = mask.GetTransformPath(i); bool on = false;
                foreach (var seg in path.Split('/')) if (seg == "spine" || seg == "orbit") { on = true; break; }
                mask.SetTransformActive(i, on); if (on) upper++;
            }
            layers.SetLayerMaskFromAvatarMask(2, mask);
            if (upper == 0) Debug.LogWarning("Last Bell: cast layers found no spine/orbit bones; casts play full-body");
            ovTimes = new float[clips.Length]; ovWeights = new float[clips.Length];
            AnimationPlayableOutput.Create(graph, "Presenter", animator).SetSourcePlayable(layers);
            renderers = GetComponentsInChildren<Renderer>(true);
        }

        // Plays a cast clip stretched to `seconds` (loop: one cycle per `seconds`; hold: stays on the last frame until
        // EndOverlay or the next cast). Without cast layers it falls back to a full-body one-shot.
        public bool Overlay(string semantic, float seconds, bool loop = false, bool hold = false)
        {
            if (!layered) return PlayOnce(semantic, seconds);
            int i = Resolve(semantic);
            if (i < 0) { NotePlaceholder(semantic); return false; }
            ov = i; ovTimes[i] = 0; ovLoop = loop; ovSpeed = clips[i].length / Mathf.Max(0.05f, seconds);
            ovLeft = loop || hold ? float.MaxValue : seconds;
            return true;
        }
        public void EndOverlay() { ovLeft = 0; }

        public void SetHidden(bool on)
        {
            if (renderers == null) renderers = GetComponentsInChildren<Renderer>(true);
            hidden = on; foreach (var r in renderers) if (r) r.enabled = !on;
        }
        public bool Hidden => hidden;

        void UpdateCastLayers(float dt)
        {
            if (ovLeft > 0 && ovLeft < float.MaxValue) ovLeft -= dt;
            bool on = ov >= 0 && ovLeft > 0;
            ovW = Mathf.MoveTowards(ovW, on ? 1 : 0, dt / 0.08f);
            stillW = Mathf.MoveTowards(stillW, Locomotion > 0.3f ? 0 : 1, dt / 0.15f);
            float sum = 0;
            for (int i = 0; i < clips.Length; i++)
            {
                ovWeights[i] = Mathf.MoveTowards(ovWeights[i], i == ov ? 1 : 0, dt / 0.08f);
                if (ovWeights[i] <= 0) continue;
                sum += ovWeights[i];
                float len = clips[i].length;
                if (i == ov) ovTimes[i] += dt * ovSpeed;
                float t = i == ov && ovLoop ? Mathf.Repeat(ovTimes[i], len) : Mathf.Min(ovTimes[i], len);
                fullMix.GetInput(i).SetTime(t); upperMix.GetInput(i).SetTime(t);
            }
            for (int i = 0; i < clips.Length; i++)
            {
                float w = sum > 0 ? ovWeights[i] / sum : 0;
                fullMix.SetInputWeight(i, w); upperMix.SetInputWeight(i, w);
            }
            layers.SetInputWeight(1, ovW * stillW); layers.SetInputWeight(2, ovW);
        }

        public string CurrentClip => current >= 0 && clips != null && current < clips.Length ? clips[current].name : "-";

        // simple readable stand-in body if a character GLB is missing from Resources
        static System.Func<Transform, Transform> PlaceholderBody(string className) => host =>
        {
            var col = className == "Chimesinger" ? new Color(0.92f, 0.9f, 0.85f) : className == "Arcanist" ? new Color(0.45f, 0.3f, 0.7f) : new Color(0.2f, 0.32f, 0.62f);
            Prim.Make(PrimitiveKind.Capsule, host, new Vector3(0, 0.9f, 0), new Vector3(0.7f, 0.9f, 0.7f), col);
            Prim.Make(PrimitiveKind.Cube, host, new Vector3(0, 1.3f, 0.4f), new Vector3(0.2f, 0.2f, 0.5f), new Color(1f, 0.8f, 0.3f), 1.5f);
            return host;
        };
    }
}
