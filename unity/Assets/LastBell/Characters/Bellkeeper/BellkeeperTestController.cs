using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace LastBell.Characters
{
    // Look test for the Bellkeeper boss (Resources/Characters/Bellkeeper.glb, built by
    // prototype/arena-3d/models/build-bellkeeper.ps1). Not gameplay: no attacks, phases or hit tests. It stands on the
    // clock dial at the arena's centre facing +Z (toward the gameplay camera) and loops its idle.
    // Spawns itself when a scene with the key-art arena enters Play mode.
    // The Idle clip is a glTFast "Mecanim" clip played through a PlayableGraph, so no Animator Controller asset is needed.
    public sealed class BellkeeperTestController : MonoBehaviour
    {
        const float GlowBoost = 5f;   // glTFast drops KHR_materials_emissive_strength (3 in the GLB); a bit more reads better
        const float MetalScale = 0.55f;

        PlayableGraph graph;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn()
        {
            if (FindAnyObjectByType<LastBell.Arenas.KeyArtArena>() == null || FindAnyObjectByType<BellkeeperTestController>() != null) return;
            var prefab = Resources.Load<GameObject>("Characters/Bellkeeper");
            if (prefab == null) { Debug.LogWarning("Bellkeeper test: Resources/Characters/Bellkeeper.glb not found"); return; }
            var go = Instantiate(prefab, Vector3.zero, Quaternion.identity);
            go.name = "Bellkeeper (test)";
            var idle = System.Array.Find(Resources.LoadAll<AnimationClip>("Characters/Bellkeeper"), c => c.name == "Idle");
            go.AddComponent<BellkeeperTestController>().Init(idle);
        }

        void Init(AnimationClip idle)
        {
            foreach (var r in GetComponentsInChildren<Renderer>())
                foreach (var m in r.materials)
                {
                    if (m.HasProperty("emissiveFactor")) m.SetColor("emissiveFactor", m.GetColor("emissiveFactor") * GlowBoost);
                    // Meshy bakes the brass almost fully metallic, so it mirrors the arena's violet sky; keep it warm
                    if (m.HasProperty("metallicFactor")) m.SetFloat("metallicFactor", m.GetFloat("metallicFactor") * MetalScale);
                }
            if (idle == null) { Debug.LogWarning("Bellkeeper test: no Idle clip in Bellkeeper.glb"); return; }
            var animator = GetComponentInChildren<Animator>();
            if (animator == null) animator = gameObject.AddComponent<Animator>();
            animator.applyRootMotion = false;
            graph = PlayableGraph.Create("Bellkeeper");
            var clip = AnimationClipPlayable.Create(graph, idle);
            clip.SetApplyFootIK(false);
            AnimationPlayableOutput.Create(graph, "Bellkeeper", animator).SetSourcePlayable(clip);
            graph.Play();
        }

        void Update()
        {
            // loop by hand: a clip playable runs past its end otherwise
            if (!graph.IsValid()) return;
            var p = (AnimationClipPlayable)graph.GetOutput(0).GetSourcePlayable();
            float len = p.GetAnimationClip().length;
            if (p.GetTime() >= len) p.SetTime(p.GetTime() % len);
        }

        void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }
    }
}
