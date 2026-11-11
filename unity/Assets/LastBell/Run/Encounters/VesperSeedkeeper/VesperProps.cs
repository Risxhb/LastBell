using System;
using UnityEngine;

namespace LastBell.Run
{
    // Vesper Seedkeeper props (miniboss art pass, 9 Nov 2026): the cord stake, the cord (tiled 1 m segments) and the seed of
    // light, built in Blender from code (prototype/arena-3d/models/blender/vesper_props.py; prompts in
    // build-evidence/miniboss-art-20261109/vesper-seedkeeper/prompts.md). The GLBs live in
    // Characters/VesperSeedkeeper/Resources/VesperProps; without them every view falls back to the old labelled primitive.
    // Presentation only: no hit shape, timing or damage here. Host and LAN client build the same views.
    public static class VesperProps
    {
        public const string Stake = "VesperStake", Cord = "VesperCord", Seed = "VesperSeed";
        // glTFast drops KHR_materials_emissive_strength: restore the glow (cord core, seed body)
        // emissive multipliers (glTFast drops KHR_materials_emissive_strength); tuned on the in-game frames so nothing clips to white
        const float StakeGlow = 2f, CordGlow = 2f, SeedGlow = 2.2f;
        // the seed floats over its carrier above the 'Carried mark' icon (RunHud draws it at head + 2.6 m and ~1 m up)
        public const float SeedHeight = 4.0f;
        public static int ModelsMade, FallbacksMade;   // tests: how many views used the models / the primitives

        public static bool Has(string name) => Resources.Load<GameObject>("VesperProps/" + name);
        public static bool HasAll => Has(Stake) && Has(Cord) && Has(Seed);

        static Transform Spawn(string name, Transform parent, float glow)
        {
            var prefab = Resources.Load<GameObject>("VesperProps/" + name);
            if (!prefab) return null;
            var go = UnityEngine.Object.Instantiate(prefab, parent, false); go.name = name;
            foreach (var c in go.GetComponentsInChildren<Collider>()) UnityEngine.Object.Destroy(c);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (glow != 1)
                    foreach (var m in r.materials)
                        if (m.HasProperty("emissiveFactor")) m.SetColor("emissiveFactor", m.GetColor("emissiveFactor") * glow);
            }
            return go.transform;
        }
        static void NoShadow(Transform t) { var r = t ? t.GetComponent<MeshRenderer>() : null; if (r) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }

        // A stake: root at the floor point, the model (or the old cylinder) under it, turned to face the vessel.
        public static Transform MakeStake(Transform parent, Transform vessel, Vector3 at, string name)
        {
            var root = new GameObject(name).transform; root.SetParent(parent, false); root.position = at;
            var model = Spawn(Stake, root, StakeGlow);
            if (model) ModelsMade++;
            else
            {
                FallbacksMade++;
                var t = Prim.Make(PrimitiveKind.Cylinder, root, Vector3.up * 0.5f, new Vector3(0.35f, 0.5f, 0.35f), VesperSeedkeeperEncounter.Cobalt, 0);
                t.name = "Cord stake (stand-in)"; NoShadow(t);
            }
            root.gameObject.AddComponent<VesperStakeView>().Bind(vessel);
            return root;
        }

        // A cord: root at the stake's floor point; the view runs from the vessel to the stake's eyelet every frame.
        public static Transform MakeCord(Transform parent, Transform vessel, Vector3 at, string name)
        {
            var root = new GameObject(name).transform; root.SetParent(parent, false); root.position = at;
            root.gameObject.AddComponent<VesperCordView>().Bind(vessel);
            return root;
        }

        // A seed: root at the point over the carrier's head; the model (or the old orb) spins and bobs under it.
        public static Transform MakeSeed(Transform parent, Vector3 at, string name)
        {
            var root = new GameObject(name).transform; root.SetParent(parent, false); root.position = at;
            var model = Spawn(Seed, root, SeedGlow);
            if (model) { ModelsMade++; root.gameObject.AddComponent<VesperSeedView>().Bind(model); }
            else
            {
                FallbacksMade++;
                var t = Prim.Make(PrimitiveKind.Sphere, root, Vector3.zero, Vector3.one * 0.45f, VesperSeedkeeperEncounter.Gold, 4f);
                t.name = "Seed of light (stand-in)"; NoShadow(t);
            }
            return root;
        }

        internal static Transform CordSegment(Transform parent) => Spawn(Cord, parent, CordGlow);
    }

    // turns the stake's eyelet (+Z) toward the vessel (host and LAN replica alike)
    public sealed class VesperStakeView : MonoBehaviour
    {
        Transform vessel;
        public void Bind(Transform v) { vessel = v; LateUpdate(); }
        void LateUpdate()
        {
            if (!vessel) return;
            var d = Sim.Flat(vessel.position - transform.position);
            if (d.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(d);
        }
    }

    // the seed turns slowly and bobs a few centimetres (the root follows the carrier)
    public sealed class VesperSeedView : MonoBehaviour
    {
        Transform model; float t;
        public void Bind(Transform m) { model = m; t = UnityEngine.Random.value * 25f; }
        void Update()
        {
            if (!model) return;
            t += Time.deltaTime;
            model.localPosition = Vector3.up * (0.05f * Mathf.Sin(t * 2.4f));
            model.localRotation = Quaternion.Euler(0, t * 40f, 0);
        }
    }

    // The cord from the vessel to the stake: tiled 1 m segments along a shallow sag, or one stretched glowing bar when the
    // model is missing. Host and LAN client: the root sits at the stake's floor point.
    public sealed class VesperCordView : MonoBehaviour
    {
        public const float StartHeight = 1.9f, StartOut = 0.9f, EyeletHeight = 0.80f, EyeletOut = 0.0875f, Sag = 0.45f;   // eyelet: vesper_props.py
        Transform vessel, bar; readonly System.Collections.Generic.List<Transform> segs = new System.Collections.Generic.List<Transform>();
        bool useModel; Vector3 lastFrom = Vector3.positiveInfinity, lastTo;

        public void Bind(Transform v)
        {
            vessel = v;
            useModel = VesperProps.Has(VesperProps.Cord);
            if (useModel) VesperProps.ModelsMade++;
            else
            {
                VesperProps.FallbacksMade++;
                bar = Prim.Make(PrimitiveKind.Cube, transform, Vector3.zero, new Vector3(0.08f, 0.08f, 1f), VesperSeedkeeperEncounter.CordGlow, 3f);
                bar.name = "Vesper cord (stand-in)";
                var r = bar.GetComponent<MeshRenderer>(); if (r) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            LateUpdate();
        }

        void LateUpdate()
        {
            if (!vessel) return;
            var boss = Sim.Flat(vessel.position); var stake = Sim.Flat(transform.position);
            var flat = stake - boss; if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            var dir = flat.normalized;
            if (!useModel)
            {
                // the old placeholder: a straight bar 1 m up from the vessel to the stake
                if (!bar) return;
                bar.position = (boss + stake) / 2 + Vector3.up * 1.0f;
                bar.rotation = Quaternion.LookRotation(dir);
                bar.localScale = new Vector3(0.08f, 0.08f, flat.magnitude);
                return;
            }
            var from = boss + dir * StartOut + Vector3.up * StartHeight;
            var to = stake - dir * EyeletOut + Vector3.up * EyeletHeight;
            if ((from - lastFrom).sqrMagnitude < 1e-6f && (to - lastTo).sqrMagnitude < 1e-6f) return;
            lastFrom = from; lastTo = to;
            int n = Mathf.Max(1, Mathf.RoundToInt((to - from).magnitude));
            while (segs.Count < n) { var s = VesperProps.CordSegment(transform); if (!s) { useModel = false; return; } segs.Add(s); }
            for (int i = 0; i < segs.Count; i++) segs[i].gameObject.SetActive(i < n);
            var prev = from;
            for (int k = 1; k <= n; k++)
            {
                float t = k / (float)n;
                var p = Vector3.Lerp(from, to, t) + Vector3.down * (Sag * 4f * t * (1 - t));
                var d = p - prev; var s = segs[k - 1];
                s.SetPositionAndRotation(prev, Quaternion.LookRotation(d.sqrMagnitude > 1e-6f ? d : dir));
                s.localScale = new Vector3(1, 1, d.magnitude);
                prev = p;
            }
        }
    }
}
