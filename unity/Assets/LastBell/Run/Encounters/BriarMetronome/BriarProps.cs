using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Briar Metronome prop models (miniboss art pass, 9 Nov 2026): the bramble hedge module (1 m, variants A-C) and the
    // season graft (I-IV), built in Blender (prototype/arena-3d/models/blender/briar_props.py) and loaded from
    // Characters/BriarMetronome/Resources/BriarProps. Without a GLB, Spawn returns null and the encounter keeps its
    // primitive stand-ins. Presentation only: no collider, hit shape, timing or damage comes from here.
    public static class BriarProps
    {
        public const float HedgeModule = 1f;   // the hedge model's length along +Z
        static readonly string[] hedges = { "BriarHedge_A", "BriarHedge_B", "BriarHedge_C" };
        static readonly string[] grafts = { "BriarGraft_I", "BriarGraft_II", "BriarGraft_III", "BriarGraft_IV" };
        static readonly Dictionary<string, GameObject> cache = new Dictionary<string, GameObject>();

        public static string Hedge(int i) => hedges[((i % 3) + 3) % 3];
        public static string Graft(int season) => grafts[Mathf.Clamp(season, 0, 3)];

        static GameObject Prefab(string name)
        {
            if (!cache.TryGetValue(name, out var p)) cache[name] = p = Resources.Load<GameObject>("BriarProps/" + name);
            return p;
        }
        public static bool Has(string name) => Prefab(name);

        public static Transform Spawn(string name, Transform parent, Vector3 localPos, float yaw, Vector3 scale)
        {
            var prefab = Prefab(name);
            if (!prefab) return null;
            var go = Object.Instantiate(prefab, parent, false); go.name = name;
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            go.transform.localPosition = localPos; go.transform.localRotation = Quaternion.Euler(0, yaw, 0); go.transform.localScale = scale;
            return go.transform;
        }

        // Ends a view: a model sinks into the floor and goes (~0.6 s, detached so nothing else tracks it); a stand-in goes now.
        public static void Retire(Transform view)
        {
            if (!view) return;
            var g = view.GetComponent<Grow>();
            if (g && view.gameObject.activeInHierarchy) { view.SetParent(null, true); g.Sink(); }
            else Object.Destroy(view.gameObject);
        }

        // The model pieces rise out of the floor when shown (0.35 s) and sink when retired (render clock; the sim never reads it).
        // Only the children move, never the root, so the LAN client's placement of the root is left alone; the numeral stays put.
        public sealed class Grow : MonoBehaviour
        {
            const float Depth = 1.1f, RiseTime = 0.35f, SinkTime = 0.6f;
            float t; bool sinking;
            readonly List<(Transform t, Vector3 at)> parts = new List<(Transform, Vector3)>();
            void Awake()
            {
                foreach (Transform c in transform) if (!c.GetComponent<TextMesh>()) parts.Add((c, c.localPosition));
                Apply();
            }
            public void Sink()
            {
                if (!sinking) { sinking = true; t = 0; }
                foreach (Transform c in transform) if (c.GetComponent<TextMesh>()) c.gameObject.SetActive(false);   // the graft is done
            }
            void Update() { t += Time.deltaTime; Apply(); }
            void Apply()
            {
                float k = sinking ? Mathf.Clamp01(t / SinkTime) : 1 - Mathf.Clamp01(t / RiseTime);
                k *= k;
                foreach (var (c, at) in parts) if (c) c.localPosition = at + Vector3.down * (Depth * k);
                if (sinking && t >= SinkTime) Destroy(gameObject);
            }
        }
    }
}
