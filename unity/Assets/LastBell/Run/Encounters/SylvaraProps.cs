using System;
using UnityEngine;

namespace LastBell.Run
{
    // Sylvara v3 attack props (proposal, 1 Nov): Chzy18's Meshy models cleaned up in Blender
    // (prototype/arena-3d/models/blender/sylvara_props_prep.py) to the written sizes: Choir Bud 1.7 m, Seed Pod 1.1 m,
    // one 1.1 m root piece for the Root Ring. The glb files are gitignored art (Resources/SylvaraProps); without them
    // Spawn returns null and the encounter keeps its stand-ins. Presentation only: no hit shape, timing or damage here.
    public static class SylvaraProps
    {
        public const string Bud = "ChoirBud", Pod = "SeedPod", Root = "RootModule";
        public static bool Has(string name) => Resources.Load<GameObject>("SylvaraProps/" + name);
        public static PropMotion Spawn(string name, Transform parent, Vector3 pos, float yawDeg, float scale = 1)
        {
            var prefab = Resources.Load<GameObject>("SylvaraProps/" + name);
            if (!prefab) return null;
            var go = UnityEngine.Object.Instantiate(prefab, parent, false);
            go.name = name;
            foreach (var c in go.GetComponentsInChildren<Collider>()) UnityEngine.Object.Destroy(c);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yawDeg, 0));
            var m = go.AddComponent<PropMotion>(); m.Size = scale; go.transform.localScale = Vector3.one * scale;
            return m;
        }
    }

    // Timed motion for one prop, on the render clock (presentation; the sim never reads it).
    public sealed class PropMotion : MonoBehaviour
    {
        public enum Mode { Still, Sprout, Fall, Land, Rise, Wither, Bloom }
        public Mode Now { get; private set; } = Mode.Still;
        public float Size = 1;
        public float Open;                     // Choir Bud: 0 just sprouted .. 1 about to bloom (the bud swells)
        public Func<Vector3> Follow;           // Seed Pod: tracks its warning's centre until it lands
        float t, landIn, hold;
        Vector3 basePos; Quaternion baseRot; Vector3 tiltAxis;

        void Begin(Mode m) { Now = m; t = 0; basePos = transform.position; baseRot = transform.rotation; }
        public void Sprout() { Begin(Mode.Sprout); Apply(); }
        public void FallIn(float seconds) { landIn = seconds; Begin(Mode.Fall); Apply(); }
        public float LandsIn => Now == Mode.Fall ? landIn - t : 0;   // tests: seconds until the pod lands
        public void Land() { if (Follow != null) transform.position = Flat(Follow()); Begin(Mode.Land); }
        public void Rise(float holdSeconds) { hold = holdSeconds; Begin(Mode.Rise); Apply(); }
        public void Wither() { tiltAxis = Quaternion.Euler(0, (transform.position.x * 37 + transform.position.z * 91) % 360, 0) * Vector3.right; Begin(Mode.Wither); }   // no gameplay RNG
        public void Bloom() { Begin(Mode.Bloom); }

        static Vector3 Flat(Vector3 p) => new Vector3(p.x, 0, p.z);
        static float Ease(float x) { x = Mathf.Clamp01(x); return 1 - (1 - x) * (1 - x); }

        void Update() { t += Time.deltaTime; Apply(); }
        void Apply()
        {
            var tr = transform; float s = Size;
            switch (Now)
            {
                case Mode.Sprout:   // grows out of the floor in 0.6 s, then swells with Open
                {
                    float g = Ease(t / 0.6f);
                    float sw = 1 + 0.12f * Open + (Open > 0.8f ? 0.03f * Mathf.Sin(t * 18f) : 0);
                    tr.localScale = new Vector3(s * sw, s * Mathf.Max(0.05f, g), s * sw);
                    tr.position = basePos + Vector3.down * (0.3f * (1 - g));
                    break;
                }
                case Mode.Fall:     // hidden until 0.4 s before landing, then drops from 7 m onto the warning's centre
                {
                    if (Follow != null) basePos = Flat(Follow());
                    float left = landIn - t, drop = Mathf.Clamp01(left / 0.4f);
                    bool show = left <= 0.4f;
                    foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = show;
                    tr.position = basePos + Vector3.up * (7f * drop * drop);
                    tr.localScale = Vector3.one * s;
                    break;
                }
                case Mode.Land:     // squash on impact, then sink into the floor and go
                {
                    foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = true;
                    float sq = t < 0.12f ? 1 - 0.25f * Mathf.Sin(Mathf.PI * t / 0.12f) : 1;
                    tr.localScale = new Vector3(s * (2 - sq), s * sq, s * (2 - sq));
                    tr.position = basePos + Vector3.down * (t < 0.35f ? 0 : 1.3f * Ease((t - 0.35f) / 0.6f));
                    if (t > 1.0f) Destroy(gameObject);
                    break;
                }
                case Mode.Rise:     // roots burst up in 0.15 s, hold, then sink back over 0.6 s
                {
                    float up = t < 0.15f ? Ease(t / 0.15f) : t < 0.15f + hold ? 1 : 1 - Ease((t - 0.15f - hold) / 0.6f);
                    tr.position = basePos + Vector3.down * (1.25f * s * (1 - up));
                    tr.localScale = Vector3.one * s;
                    if (t > 0.75f + hold) Destroy(gameObject);
                    break;
                }
                case Mode.Wither:   // cut: droops to one side and sinks
                {
                    float d = Ease(t / 0.9f);
                    tr.rotation = Quaternion.AngleAxis(40f * d, tiltAxis) * baseRot;
                    tr.position = basePos + Vector3.down * (0.9f * d * d);
                    tr.localScale = new Vector3(s, s * (1 - 0.3f * d), s);
                    if (t > 1.2f) Destroy(gameObject);
                    break;
                }
                case Mode.Bloom:    // flares open with its petal ring, then shrinks away
                {
                    float f = t < 0.15f ? 1 + 0.35f * Ease(t / 0.15f) : 1.35f * (1 - Ease((t - 0.15f) / 0.5f));
                    tr.localScale = new Vector3(s * f * 1.1f, s * Mathf.Max(0.02f, f), s * f * 1.1f);
                    if (t > 0.7f) Destroy(gameObject);
                    break;
                }
            }
        }
    }
}
