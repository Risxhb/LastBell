using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LastBell.Areas.Area1
{
    // Moving parts and cameras of Area 1 — The Skyward Spire (imported from SkywardSpire.glb):
    // banners sway, armillary rings turn, clouds drift, and the floor's gold inlay dims while a warning is up.
    // Mirrors update() in prototype/arena-3d/areas/area-1/skyward-spire-env.js. Press T (or call SetView) to switch
    // between the gameplay camera and the top-down view framed like the reference. Groups named "Occluder…"
    // (camera-side towers) are hidden in the gameplay view so they never block the floor.
    // Built by Last Bell > Area 1: Skyward Spire.
    [ExecuteAlways]
    public sealed class SkywardSpireArena : MonoBehaviour
    {
        public enum View { Gameplay, TopDown, AbyssCloseup }

        [SerializeField] Transform arenaRoot;
        [SerializeField] Camera viewCamera;
        [SerializeField] View view = View.Gameplay;
        [Tooltip("Hazards are up: floor gold inlay dims.")]
        public bool quiet;
        [Tooltip("Play mode: draw all ~840 cloud puffs as one merged mesh (one draw call), sorted back to front per camera.")]
        public bool batchClouds = true;

        // Gameplay camera matches index.html (fov 44). Top-down matches skyward-spire.html?view=top: the reference is
        // 850 x 742 px, plaza centre at (437, 325), 16.3 px per metre, fov 8. x is mirrored from three.js (glTF import).
        public static readonly Vector3 GamePos = new Vector3(0, 29, 22.5f), GameLook = new Vector3(0, 0, 1);
        public const float TopFov = 8, TopAspect = 850f / 742f;
        public static readonly Vector3 TopLook = new Vector3(0.736f, 0, 2.82f);
        public const float TopDistance = 354.8f, TopTilt = 27; // tipped from the south like the painting (its floor reads 0.89 high to wide)
        public static Vector3 TopPos => TopLook + new Vector3(0, Mathf.Cos(TopTilt * Mathf.Deg2Rad), Mathf.Sin(TopTilt * Mathf.Deg2Rad)) * TopDistance;
        const float FogNear = 70, FogFar = 420;
        // Review shot: 25 m out from the west belfry's base, looking down at 45° across the tower and its clouds.
        public static readonly Vector3 CloseLook = new Vector3(23, -18, 13.5f), CloseDir = new Vector3(0.61f, 0.71f, 0.36f);

        readonly List<(Transform t, Quaternion rest, float phase)> banners = new List<(Transform, Quaternion, float)>();
        readonly List<(Transform t, float speed)> rings = new List<(Transform, float)>();
        readonly List<GameObject> occluders = new List<GameObject>();
        readonly List<(Transform t, float angle, float radius, float speed)> clouds = new List<(Transform, float, float, float)>();
        readonly List<Material> floorMats = new List<Material>();
        readonly List<(Transform t, Vector3 scale, float phase)> breathers = new List<(Transform, Vector3, float)>();
        readonly List<(Material m, float opacity, float phase)> cloudMats = new List<(Material, float, float)>();
        float floorLevel = 1;
        bool collected;
        SkyCloudBatch cloudBatch;
        public bool checkMode;   // SkyCloudBatchCheck: set the clouds up in edit mode, to render them both ways
        public SkyCloudBatch CloudBatch => cloudBatch;
        public void CollectNow() => Collect();

        void OnEnable() { collected = false; ApplyView(); UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += SortClouds; }

        public View CurrentView => view;
        public void SetView(View v) { view = v; ApplyView(); }

        void ApplyView()
        {
            if (!collected) Collect();
            foreach (var o in occluders) if (o) o.SetActive(view != View.Gameplay);
            if (!viewCamera) return;
            if (view == View.Gameplay)
            {
                viewCamera.fieldOfView = 44;
                viewCamera.transform.position = GamePos; viewCamera.transform.LookAt(GameLook);
                RenderSettings.fogStartDistance = FogNear; RenderSettings.fogEndDistance = FogFar;
            }
            else if (view == View.AbyssCloseup)
            {
                viewCamera.fieldOfView = 50;
                viewCamera.transform.position = CloseLook + CloseDir.normalized * 25; viewCamera.transform.LookAt(CloseLook);
                RenderSettings.fogStartDistance = FogNear; RenderSettings.fogEndDistance = FogFar;
            }
            else
            {
                viewCamera.fieldOfView = TopFov;
                viewCamera.transform.SetPositionAndRotation(TopPos, Quaternion.LookRotation(TopLook - TopPos, Vector3.back));
                RenderSettings.fogStartDistance = TopDistance + 25; RenderSettings.fogEndDistance = TopDistance + 700; // gentle depth fade; the deep sky stays blue between the clouds
            }
        }

        void Collect()
        {
            collected = true; banners.Clear(); rings.Clear(); occluders.Clear(); clouds.Clear(); floorMats.Clear(); breathers.Clear(); cloudMats.Clear();
            cloudBatch?.Dispose(); cloudBatch = null;
            if (!arenaRoot || !gameObject.scene.IsValid()) return;   // prefab assets (OnValidate on load): nothing to animate, and .material would log errors
            var puffs = new List<(Renderer r, float opacityPhase)>();
            if (Application.isPlaying || checkMode)             // per-session copies of the cloud materials, so opacity can vary
            {                                                   // without touching the saved assets
                var copies = new Dictionary<Material, (Material copy, float phase)>();
                foreach (var r in arenaRoot.GetComponentsInChildren<Renderer>(true))
                {
                    var m = r.sharedMaterial; if (!m || !m.HasProperty("_SoftDistance")) continue;
                    if (!copies.TryGetValue(m, out var c))
                    {
                        copies[m] = c = (batchClouds ? m : new Material(m), (copies.Count + 1) * 1.9f);
                        if (!batchClouds) cloudMats.Add((c.copy, m.GetFloat("_Opacity"), c.phase));
                    }
                    if (batchClouds) puffs.Add((r, c.phase)); else r.sharedMaterial = c.copy;
                }
            }
            var rnd = new System.Random(5);
            foreach (var t in arenaRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("Occluder")) { occluders.Add(t.gameObject); continue; }
                if (t.name.StartsWith("BannerCloth")) { banners.Add((t, t.localRotation, (float)rnd.NextDouble() * 6)); continue; }   // Blender names copies "BannerCloth.001"
                if (t.name.StartsWith("ArmillaryRing")) { rings.Add((t, (rings.Count % 2 == 0 ? 0.25f : -0.35f))); continue; }
                switch (t.name)
                {
                    case "BannerCloth": banners.Add((t, t.localRotation, (float)rnd.NextDouble() * 6)); break;
                    case "ArmillaryRing": rings.Add((t, (rings.Count % 2 == 0 ? 0.25f : -0.35f))); break;
                    case "Cloud":
                        var p = t.position; clouds.Add((t, Mathf.Atan2(p.z, p.x), new Vector2(p.x, p.z).magnitude, 0.002f + (float)rnd.NextDouble() * 0.004f));
                        breathers.Add((t, t.localScale, (float)rnd.NextDouble() * 6)); break;
                    case "CloudWaist": breathers.Add((t, t.localScale, (float)rnd.NextDouble() * 6)); break;   // stays with its tower
                    case "FloorSurface":
                        if (Application.isPlaying && t.TryGetComponent(out Renderer r)) floorMats.Add(r.material); break;
                }
            }
            if (puffs.Count == 0) return;
            // Hand every puff's motion to the merged mesh: same drift and breathing numbers as above, applied in the shader.
            var drift = new Dictionary<Transform, (float angle, float radius, float speed)>();
            foreach (var (c, a, rad, sp) in clouds) drift[c] = (a, rad, sp);
            var breathe = new Dictionary<Transform, float>();
            foreach (var (c, _, ph) in breathers) breathe[c] = ph;
            cloudBatch = SkyCloudBatch.Build(puffs, drift, breathe, gameObject.scene, Time.time);
            if (cloudBatch == null) { batchClouds = false; Collect(); return; }   // puffs it can't merge: animate them one by one as before
            clouds.RemoveAll(c => cloudBatch.Covers(c.t));
            breathers.RemoveAll(b => cloudBatch.Covers(b.t));
        }

        void SortClouds(UnityEngine.Rendering.ScriptableRenderContext _, Camera cam)
        {
            if (cloudBatch != null && cam && Application.isPlaying) cloudBatch.Sort(cam.transform.position, Time.time);
        }

        void Update()
        {
            if (!Application.isPlaying) return; // edit mode only places the camera (ApplyView)
            if (!collected || (floorMats.Count == 0 && arenaRoot)) { collected = false; Collect(); }
            float t = Time.time, dt = Time.deltaTime;
            foreach (var (b, rest, ph) in banners) b.localRotation = rest * Quaternion.Euler(0, Mathf.Sin(t * 1.2f + ph) * 3.4f, 0);
            foreach (var (r, speed) in rings) r.localRotation = Quaternion.Euler(0, t * Mathf.Rad2Deg * speed, 0);
            AnimateClouds(t, dt);
            floorLevel = Mathf.MoveTowards(floorLevel, quiet ? 0f : 1f, dt * 4);
            foreach (var m in floorMats)
                if (m.HasProperty("emissiveFactor")) m.SetColor("emissiveFactor", new Color(1f, 0.85f, 0.63f) * 0.12f * floorLevel);
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
                SetView(view == View.Gameplay ? View.TopDown : View.Gameplay);
#endif
        }

        public void AnimateClouds(float t, float dt)
        {
            for (int i = 0; i < clouds.Count; i++)
            {
                var (c, a, rad, sp) = clouds[i]; a += sp * dt; clouds[i] = (c, a, rad, sp);
                c.position = new Vector3(Mathf.Cos(a) * rad, c.position.y, Mathf.Sin(a) * rad);
            }
            foreach (var (c, s, ph) in breathers) c.localScale = s * (1 + Mathf.Sin(t * 0.25f + ph) * 0.03f);   // ±3% breathing
            foreach (var (m, op, ph) in cloudMats) m.SetFloat("_Opacity", op * (0.92f + 0.08f * Mathf.Sin(t * 0.18f + ph)));
            cloudBatch?.Animate(t);
        }

        // The shared URP asset keeps shadows to 50 m; the top-down camera sits ~325 m up, so it borrows a longer range
        // while it is active (play mode or a capture) and always hands the original back.
        static float? savedShadowDistance;
        public static void UseLongShadows(bool on)
        {
            if (!(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)) return;
            if (on && savedShadowDistance == null) { savedShadowDistance = urp.shadowDistance; urp.shadowDistance = 420; }
            else if (!on && savedShadowDistance != null) { urp.shadowDistance = savedShadowDistance.Value; savedShadowDistance = null; }
        }

        void LateUpdate() { if (Application.isPlaying) UseLongShadows(view == View.TopDown); }
        void OnDisable()
        {
            UseLongShadows(false);
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= SortClouds;
            cloudBatch?.Dispose(); cloudBatch = null; collected = false;
        }
        void OnApplicationQuit() { UseLongShadows(false); }

        void OnValidate() { ApplyView(); }
    }
}
