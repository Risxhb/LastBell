using System;
using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Rivet Cantor props (miniboss art pass, 9 Nov 2026; prompts and critic rounds in
    // build-evidence/miniboss-art-20261109/rivet-cantor/). Three Blender-built models
    // (prototype/arena-3d/models/blender/rivet_cantor_props.py) under Characters/RivetCantor/Resources/RivetCantorProps:
    //   RivetHead    the rivet the floor punches up when a Rivet Run warning resolves (nodes "Head" and "Plate")
    //   ChainLink    one stud link; a Rivet Chain is many of them along a sagging curve (slack = too short, taut = safe)
    //   ChainShackle the cuff at each chain end (a player's waist, or the boss's drum)
    // Presentation only: nothing here reads or changes a hit shape, timing or damage. Missing models fall back to primitives.
    public static class RivetCantorProps
    {
        public const string Rivet = "RivetHead", Link = "ChainLink", Shackle = "ChainShackle";
        public const float Punch = 0.22f, LinkPitch = 0.32f, WaistHeight = 1.15f, SideOffset = 0.55f, ShackleScale = 1.2f;
        static readonly Dictionary<string, GameObject> cache = new Dictionary<string, GameObject>();
        static readonly Color Brass = new Color(0.78f, 0.6f, 0.3f), Iron = new Color(0.16f, 0.15f, 0.14f), Slate = new Color(0.18f, 0.19f, 0.2f), Cobalt = new Color(0.16f, 0.22f, 0.5f);

        static GameObject Prefab(string name)
        {
            if (!cache.TryGetValue(name, out var g)) { g = Resources.Load<GameObject>("RivetCantorProps/" + name); cache[name] = g; }
            return g;
        }
        public static bool Has(string name) => Prefab(name) != null;
        public static bool HasAll => Has(Rivet) && Has(Link) && Has(Shackle);

        // an instance of the model under `parent` (no colliders), or null when the GLB is not in this build
        public static Transform Instance(string name, Transform parent)
        {
            var prefab = Prefab(name); if (!prefab) return null;
            var go = UnityEngine.Object.Instantiate(prefab, parent, false); go.name = name;
            foreach (var c in go.GetComponentsInChildren<Collider>()) UnityEngine.Object.Destroy(c);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go.transform;
        }

        // the chain's sag (m) for a horizontal span: slack below 7 m, straight at 7 m and beyond (the chain rule's length)
        public static float Sag(float span) => Mathf.Min(1f, 0.06f + 0.28f * Mathf.Max(0f, RivetCantorEncounter.ChainLength - span));

        internal static Transform PrimLink(Transform parent)
        {
            var t = new GameObject("ChainLink (stand-in)").transform; t.SetParent(parent, false);
            Prim.Make(PrimitiveKind.Cube, t, Vector3.zero, new Vector3(0.28f, 0.09f, 0.48f), Brass);
            return t;
        }
        internal static Transform PrimShackle(Transform parent)
        {
            var t = new GameObject("ChainShackle (stand-in)").transform; t.SetParent(parent, false);
            Prim.Make(PrimitiveKind.Cylinder, t, new Vector3(0, -0.2f, 0), new Vector3(0.3f, 0.2f, 0.08f), Brass, 0, Quaternion.Euler(90, 0, 0));
            Prim.Make(PrimitiveKind.Cube, t, new Vector3(0, -0.06f, 0), new Vector3(0.1f, 0.06f, 0.1f), Cobalt);
            return t;
        }
        internal static (Transform head, Transform plate) PrimRivet(Transform parent)
        {
            var head = Prim.Make(PrimitiveKind.Sphere, parent, new Vector3(0, 0.08f, 0), new Vector3(1.1f, 0.6f, 1.1f), Brass);
            Prim.Make(PrimitiveKind.Cylinder, head, new Vector3(0, -0.4f, 0), new Vector3(0.4f, 0.6f, 0.4f), Iron);
            var plate = Prim.Make(PrimitiveKind.Cylinder, parent, new Vector3(0, 0.015f, 0), new Vector3(1.5f, 0.015f, 1.5f), Slate);
            head.name = "Head"; plate.name = "Plate";
            return (head, plate);
        }
    }

    // One rivet pop on the render clock: the head punches up out of the floor (0.08 s), settles, holds, then the head and
    // the torn plate sink away together (gone by 0.9 s, so a plate never sits on a later warning).
    public sealed class RivetPop : MonoBehaviour
    {
        public const float Life = 0.9f;
        Transform head, plate; Vector3 headRest, plateRest, plateScale; float t;
        public bool HasModel { get; private set; }
        public static readonly List<RivetPop> Live = new List<RivetPop>();   // tests: pops on screen now
        public static int Spawned, ModelSpawned;                              // tests: pops created since start (and how many from the model)

        public static RivetPop Create(Transform parent, Vector3 at)
        {
            var root = new GameObject("Rivet pop").transform; root.SetParent(parent, false);
            root.position = new Vector3(at.x, 0, at.z);
            root.rotation = Quaternion.Euler(0, Mathf.Repeat(at.x * 37f + at.z * 91f, 360f), 0);   // stable per spot (host and LAN client agree)
            var pop = root.gameObject.AddComponent<RivetPop>();
            var model = RivetCantorProps.Instance(RivetCantorProps.Rivet, root);
            if (model) { pop.head = model.Find("Head"); pop.plate = model.Find("Plate"); pop.HasModel = pop.head && pop.plate; }
            if (!pop.HasModel)
            {
                if (model) Destroy(model.gameObject);
                (pop.head, pop.plate) = RivetCantorProps.PrimRivet(root);
            }
            pop.headRest = pop.head.localPosition; pop.plateRest = pop.plate.localPosition; pop.plateScale = pop.plate.localScale;
            Spawned++; if (pop.HasModel) ModelSpawned++;
            pop.Apply();
            return pop;
        }

        void OnEnable() { if (!Live.Contains(this)) Live.Add(this); }
        void OnDisable() => Live.Remove(this);
        void Update() { t += Time.deltaTime; Apply(); if (t >= Life) Destroy(gameObject); }

        static float Ease(float x) { x = Mathf.Clamp01(x); return 1 - (1 - x) * (1 - x); }
        void Apply()
        {
            float y, ps;
            if (t < 0.08f) { y = Mathf.Lerp(-0.55f, RivetCantorProps.Punch, t / 0.08f); ps = Mathf.Lerp(0.55f, 1f, t / 0.08f); }
            else if (t < 0.2f) { y = Mathf.Lerp(RivetCantorProps.Punch, 0, Ease((t - 0.08f) / 0.12f)); ps = 1; }
            else if (t < 0.45f) { y = 0; ps = 1; }
            else { float k = Ease((t - 0.45f) / 0.4f); y = -0.75f * k; ps = 1 - k; }
            head.localPosition = headRest + Vector3.up * y;
            plate.localScale = plateScale * Mathf.Max(0.001f, ps);
            plate.localPosition = plateRest + Vector3.down * 0.06f * (1 - ps);
        }
    }

    // One Rivet Chain between two moving ends (world points at waist height, read every frame). Links hang along a parabola
    // whose sag says how short the chain still is; at the lock it either snaps (links fall and scatter) or lashes taut
    // (a quick ripple), then it is gone.
    public sealed class RivetChainView : MonoBehaviour
    {
        public enum State { Live, Snap, Lash }
        public State Now { get; private set; } = State.Live;
        public Func<Vector3> EndA, EndB;
        public bool HasModel { get; private set; }
        public int LinkCount => links.Count;
        public float CurrentSag { get; private set; }
        public static readonly List<RivetChainView> Live = new List<RivetChainView>();   // tests
        public static int Spawned, Snapped, Lashed, MaxLinksSeen;                           // tests
        public static float MinSagSeen = 99, MaxSagSeen;

        readonly List<Transform> links = new List<Transform>();
        readonly List<Vector3> vel = new List<Vector3>(), spin = new List<Vector3>();
        Transform shA, shB; float t; Vector3 lastA, lastB;

        public static RivetChainView Create(Transform parent, Func<Vector3> a, Func<Vector3> b)
        {
            var go = new GameObject("Rivet chain"); go.transform.SetParent(parent, false);
            var v = go.AddComponent<RivetChainView>(); v.EndA = a; v.EndB = b;
            v.HasModel = RivetCantorProps.HasAll;
            v.shA = v.MakeShackle(); v.shB = v.MakeShackle();
            v.lastA = a(); v.lastB = b();
            Spawned++;
            v.Layout();
            return v;
        }

        Transform MakeShackle()
        {
            var s = RivetCantorProps.Instance(RivetCantorProps.Shackle, transform) ?? RivetCantorProps.PrimShackle(transform);
            s.localScale = Vector3.one * RivetCantorProps.ShackleScale; return s;
        }
        Transform MakeLink() => RivetCantorProps.Instance(RivetCantorProps.Link, transform) ?? RivetCantorProps.PrimLink(transform);

        public void Lock(bool snapped)
        {
            if (Now != State.Live) return;
            Now = snapped ? State.Snap : State.Lash; t = 0;
            if (snapped) Snapped++; else Lashed++;
            if (!snapped) return;
            // the snap: every link flies off the break point (the middle) and falls, deterministic per index
            var mid = (lastA + lastB) * 0.5f;
            vel.Clear(); spin.Clear();
            for (int i = 0; i < links.Count; i++)
            {
                var away = links[i].position - mid; away.y = 0;
                float h = Mathf.Sin(i * 12.9898f) * 43758.5453f; h -= Mathf.Floor(h);
                vel.Add(away.normalized * (1.5f + 2.5f * h) + Vector3.up * (1.5f + 2f * h));
                spin.Add(new Vector3(300 * h - 150, 500 * (1 - h) - 250, 200));
            }
        }

        void OnEnable() { if (!Live.Contains(this)) Live.Add(this); }
        void OnDisable() => Live.Remove(this);

        void LateUpdate()
        {
            t += Time.deltaTime;
            if (Now == State.Snap)
            {
                float dt = Time.deltaTime;
                for (int i = 0; i < links.Count; i++)
                {
                    vel[i] += Vector3.down * 14f * dt;
                    var p = links[i].position + vel[i] * dt; if (p.y < 0.05f) { p.y = 0.05f; vel[i] = Vector3.Scale(vel[i], new Vector3(0.5f, -0.3f, 0.5f)); }
                    links[i].position = p; links[i].Rotate(spin[i] * dt, Space.Self);
                    links[i].localScale = Vector3.one * Mathf.Clamp01(1.4f - t * 1.6f);
                }
                foreach (var s in new[] { shA, shB }) s.localScale = Vector3.one * RivetCantorProps.ShackleScale * Mathf.Clamp01(1 - t * 2f);
                if (t > 0.9f) Destroy(gameObject);
                return;
            }
            if (EndA != null) lastA = EndA(); if (EndB != null) lastB = EndB();
            Layout();
            if (Now == State.Lash && t > 0.45f) Destroy(gameObject);
        }

        void Layout()
        {
            Vector3 a = lastA, b = lastB, flat = b - a; flat.y = 0;
            float span = flat.magnitude;
            float sag = Now == State.Lash ? 0 : RivetCantorProps.Sag(span);
            CurrentSag = sag;
            if (Now == State.Live) { MinSagSeen = Mathf.Min(MinSagSeen, sag); MaxSagSeen = Mathf.Max(MaxSagSeen, sag); }
            // the lash: a travelling ripple that dies in 0.4 s
            float lashAmp = Now == State.Lash ? 0.28f * Mathf.Clamp01(1 - t / 0.4f) : 0;
            float len = Mathf.Max(0.05f, (b - a).magnitude + 8f / 3f * sag * sag / Mathf.Max(0.5f, (b - a).magnitude));   // parabola arc length
            int n = Mathf.Max(1, Mathf.RoundToInt(len / RivetCantorProps.LinkPitch));
            while (links.Count < n) links.Add(MakeLink());
            MaxLinksSeen = Mathf.Max(MaxLinksSeen, n);
            for (int i = 0; i < links.Count; i++) links[i].gameObject.SetActive(i < n);
            Vector3 side = Vector3.Cross(flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward, Vector3.up);
            for (int i = 0; i < n; i++)
            {
                float u = (i + 0.5f) / n;
                var p = Vector3.Lerp(a, b, u) + Vector3.down * sag * 4 * u * (1 - u);
                var tan = (b - a) + Vector3.down * sag * 4 * (1 - 2 * u);
                if (lashAmp > 0) p += Vector3.up * lashAmp * Mathf.Sin(Mathf.PI * (u * 3 - t * 12)) * Mathf.Sin(Mathf.PI * u);
                if (tan.sqrMagnitude < 1e-6f) tan = Vector3.forward;
                var up = (i % 2 == 0) ? Vector3.up : side;   // alternate links turn 90 degrees
                if (Mathf.Abs(Vector3.Dot(tan.normalized, up)) > 0.98f) up = side.sqrMagnitude > 0 ? side : Vector3.right;
                links[i].SetPositionAndRotation(p, Quaternion.LookRotation(tan, up));
            }
            // shackles hang under each end, the eye toward the other end
            var dir = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
            shA.SetPositionAndRotation(a, Quaternion.LookRotation(dir, Vector3.up));
            shB.SetPositionAndRotation(b, Quaternion.LookRotation(-dir, Vector3.up));
        }
    }
}
