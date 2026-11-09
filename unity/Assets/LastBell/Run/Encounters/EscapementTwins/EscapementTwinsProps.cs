using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Escapement Twins props (9 Nov 2026 art pass, build-evidence/miniboss-art-20261109/escapement-twins/prompts.md):
    // the Pallet markers (brass sun for Tick, cobalt moon for Tock) and the Counterpoise weight with its Roman numeral.
    // Scripted in prototype/arena-3d/models/blender/escapement_twins_props.py; GLBs in Characters/EscapementTwinsProps/
    // Resources/EscapementTwinsProps. Spawn returns null when a model is missing, so callers keep their primitive stand-ins.
    // Presentation only: no hit shape, timing or rule reads these.
    public static class EscapementTwinsProps
    {
        public const string Sun = "PalletMarkBrass", Moon = "PalletMarkCobalt", Weight = "CounterpoiseWeight";
        public const float MarkGlow = 2.0f, WeightGlow = 2.2f;   // glTFast drops KHR_materials_emissive_strength: restore the glow
        static readonly Dictionary<string, GameObject> cache = new Dictionary<string, GameObject>();

        static GameObject Prefab(string name)
        {
            if (!cache.TryGetValue(name, out var p)) { p = Resources.Load<GameObject>("EscapementTwinsProps/" + name); cache[name] = p; }
            return p;
        }
        public static bool Has(string name) => Prefab(name) != null;

        public static Transform Spawn(string name, Transform parent, Vector3 pos, float glow)
        {
            var prefab = Prefab(name);
            if (!prefab) return null;
            var go = Object.Instantiate(prefab, parent, false);
            go.name = name;
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // a floating marker must not drop a shadow on a warning
                foreach (var m in r.materials)
                    if (m.HasProperty("emissiveFactor")) m.SetColor("emissiveFactor", m.GetColor("emissiveFactor") * glow);
            }
            go.transform.position = pos;
            go.AddComponent<EscapementPropFacing>();
            return go.transform;
        }

        // the Pallet marker for mark m (0 brass / Tick, 1 cobalt / Tock), or null
        public static Transform Mark(int m, Transform parent, Vector3 pos) => Spawn(m == 0 ? Sun : Moon, parent, pos, MarkGlow);

        // the Counterpoise weight showing numeral n (1-4); its bottom edge at pos. Null when the model or the numeral is missing.
        static readonly string[] Roman = { "", "I", "II", "III", "IV" };
        public static Transform WeightFor(int n, Transform parent, Vector3 pos)
        {
            if (n < 1 || n >= Roman.Length || !Has(Weight)) return null;
            var t = Spawn(Weight, parent, pos, WeightGlow);
            bool found = false;
            foreach (var c in t.GetComponentsInChildren<Transform>(true))
                if (c.name.StartsWith("Numeral_")) { bool on = c.name == "Numeral_" + Roman[n]; c.gameObject.SetActive(on); found |= on; }
            if (!found) { Object.Destroy(t.gameObject); return null; }
            return t;
        }
    }

    // Turns a prop's front (+Z) to this machine's camera every frame and pops it in when it appears (a marker flip reads
    // as a new marker). Render clock only.
    public sealed class EscapementPropFacing : MonoBehaviour
    {
        float age;
        void LateUpdate()
        {
            age += Time.deltaTime;
            var cam = Camera.main;
            if (cam) { var d = cam.transform.position - transform.position; if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(d); }
            float pop = age < 0.18f ? Mathf.Lerp(0.55f, 1.12f, age / 0.18f) : age < 0.3f ? Mathf.Lerp(1.12f, 1f, (age - 0.18f) / 0.12f) : 1f;
            transform.localScale = Vector3.one * pop;
        }
    }
}
