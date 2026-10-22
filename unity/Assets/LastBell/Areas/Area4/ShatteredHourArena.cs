using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LastBell.Areas.Area4
{
    // Moving parts and camera views of The Throne of the Final Toll (Area 4, The Shattered Hour, final-boss arena).
    // Clock rings turn, debris bobs, hanging bells sway, the bell shard drifts and the fissure shimmers ±5 %.
    // quiet = hazards are up: floor decoration eases down to 40 % (same idea as KeyArtArena.quiet).
    // C cycles Gameplay → Establishing → TopDown. Built by Last Bell > Areas > Area 4 – Shattered Hour.
    // Motion runs in Play mode, or in the editor when animateInEditor is on (so the scene isn't dirtied every frame).
    [ExecuteAlways]
    public sealed class ShatteredHourArena : MonoBehaviour
    {
        public enum View { Gameplay, Establishing, TopDown }

        public Camera viewCamera;
        public View view = View.Gameplay;
        [Tooltip("Hazards are up: floor decoration dims to 40 %.")]
        public bool quiet;
        public bool animateInEditor;

        [Header("Wired by the builder")]
        public Material floorMaterial;
        [ColorUsage(false, true)] public Color floorEmission;
        public Material bellGlowMaterial;
        [ColorUsage(false, true)] public Color bellGlow;
        public Light bellLight;
        public float bellLightIntensity = 6;
        public Transform[] spinners;
        public float[] spinSpeeds;      // degrees per second around the local Y axis
        public Transform[] bobbers;
        public Transform[] swingers;
        public Transform shard;
        public ShatteredHourOccluderFade[] fades;

        // Gameplay matches every other Last Bell arena (index.html CAM_BASE / camLook, fov 44).
        static readonly Vector3 GamePos = new Vector3(0, 29, 22.5f), GameLook = new Vector3(0, 0, 1);
        static readonly Vector3 EstPos = new Vector3(0, 25, 36), EstLook = new Vector3(0, 3, -8);

        Vector3[] bobBase; Quaternion[] swingBase, spinBase; Vector3 shardPos; Quaternion shardRot;
        float dim = 1, stageK = 1, lastEmission = -1;

        void OnEnable()
        {
            bobBase = new Vector3[bobbers?.Length ?? 0]; for (int i = 0; i < bobBase.Length; i++) if (bobbers[i]) bobBase[i] = bobbers[i].localPosition;
            swingBase = new Quaternion[swingers?.Length ?? 0]; for (int i = 0; i < swingBase.Length; i++) if (swingers[i]) swingBase[i] = swingers[i].localRotation;
            spinBase = new Quaternion[spinners?.Length ?? 0]; for (int i = 0; i < spinBase.Length; i++) if (spinners[i]) spinBase[i] = spinners[i].localRotation;
            if (shard) { shardPos = shard.localPosition; shardRot = shard.localRotation; }
            SetView(view);
        }

        public void SetView(View v)
        {
            view = v; if (!viewCamera) return;
            var t = viewCamera.transform;
            if (v == View.TopDown)
            {
                viewCamera.orthographic = true; viewCamera.orthographicSize = 22;
                t.position = new Vector3(0, 60, 0); t.rotation = Quaternion.Euler(90, 180, 0); // north (−Z) at the top of the frame
                return;
            }
            viewCamera.orthographic = false;
            bool game = v == View.Gameplay;
            viewCamera.fieldOfView = game ? 44 : 48;
            t.position = game ? GamePos : EstPos; t.LookAt(game ? GameLook : EstLook);
        }

        // Optional presentation stages (0 Arrival, 1 Opening, 2 Escalation, 3 Final): only motion speed and bell glow.
        public void SetPresentationStage(int stage)
        {
            stage = Mathf.Clamp(stage, 0, 3); stageK = new[] { 0.5f, 1f, 1.4f, 1.8f }[stage];
            if (bellGlowMaterial) bellGlowMaterial.SetColor("_EmissionColor", bellGlow * (0.7f + 0.2f * stage));
            if (bellLight) bellLight.intensity = bellLightIntensity * (0.7f + 0.2f * stage);
        }

        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            if (Application.isPlaying && Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame) SetView((View)(((int)view + 1) % 3));
#else
            if (Application.isPlaying && Input.GetKeyDown(KeyCode.C)) SetView((View)(((int)view + 1) % 3));
#endif
            bool live = Application.isPlaying || animateInEditor;
            float dt = Application.isPlaying ? Time.deltaTime : 0.016f, t = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
            dim = Mathf.MoveTowards(dim, quiet ? 0.4f : 1f, dt * 2f); // 0.3 s for the 0.6 swing
            float shimmer = live ? 1 + 0.05f * Mathf.Sin(t * Mathf.PI * 2 / 6f) : 1, k = dim * shimmer;
            if (floorMaterial && Mathf.Abs(k - lastEmission) > 0.002f) { floorMaterial.SetColor("_EmissionColor", floorEmission * k); lastEmission = k; }
            if (!live) return;

            float m = stageK;
            for (int i = 0; i < spinBase.Length; i++) if (spinners[i]) spinners[i].localRotation = spinBase[i] * Quaternion.Euler(0, t * spinSpeeds[i] * m, 0);
            for (int i = 0; i < bobBase.Length; i++)
            {
                if (!bobbers[i]) continue;
                float period = 15 + 10 * Frac(i * 0.618f), ph = i * 2.39f;
                bobbers[i].localPosition = bobBase[i] + Vector3.up * (0.4f * Mathf.Sin(t * Mathf.PI * 2 / period + ph));
                if (!bobbers[i].name.StartsWith("DebrisCluster")) bobbers[i].Rotate(0, (Frac(i * 0.37f) - 0.5f) * 6f * dt * m, 0, Space.Self); // merged clusters only bob
            }
            for (int i = 0; i < swingBase.Length; i++)
                if (swingers[i]) swingers[i].localRotation = swingBase[i] * Quaternion.Euler(0.7f * Mathf.Sin(t * 0.5f * m + i), 0, 0.5f * Mathf.Cos(t * 0.37f * m + i * 1.7f));
            if (shard)
            {
                shard.localPosition = shardPos + new Vector3(0, 0.3f * Mathf.Sin(t * 0.35f), 0.15f * Mathf.Cos(t * 0.27f));
                shard.localRotation = shardRot * Quaternion.Euler(4 * Mathf.Sin(t * 0.2f), t * 3f * m, 0);
            }
        }

        static float Frac(float x) => x - Mathf.Floor(x);
    }
}
