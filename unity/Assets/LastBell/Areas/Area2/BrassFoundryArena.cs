using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LastBell.Areas.Area2
{
    // Moving parts of Area 2, The Brass Foundry:
    //  - drives the global _LB_Area2Time that the MoltenFlow shader reads (Time.time in Play mode, the editor clock in
    //    edit mode, so the lava also flows in the Scene and Game views),
    //  - lerps _Quiet on the molten materials and dims the window glow to 60% while attack telegraphs are up (quiet),
    //  - flickers the molten and lantern lights, lets the glow curtain breathe, and sways the hanging cage lanterns.
    // Press C (or call SetView) to cycle the gameplay camera, a top-down view framed like the reference image, and the
    // reference board's hero angle. Built by Last Bell > Area 2 – Brass Foundry (Editor/BrassFoundryArenaBuilder.cs).
    [ExecuteAlways]
    public sealed class BrassFoundryArena : MonoBehaviour
    {
        public enum View { Gameplay, TopDown, Hero }

        [SerializeField] Camera viewCamera;
        [SerializeField] View view = View.Gameplay;
        [Tooltip("Attack telegraphs are up: molten and window glow dim to 60%.")]
        public bool quiet;
        [SerializeField] Material[] moltenMaterials = new Material[0];
        [SerializeField] Material[] glowMaterials = new Material[0];
        [SerializeField] Material curtainMaterial;
        [SerializeField] Light[] flickerLights = new Light[0];
        [SerializeField] Transform[] swingers = new Transform[0];

        public static readonly int TimeId = Shader.PropertyToID("_LB_Area2Time");
        static readonly int QuietId = Shader.PropertyToID("_Quiet"), BaseColorId = Shader.PropertyToID("_BaseColor");

        // Gameplay camera matches the Bellkeeper arena (prototype index.html, fov 44).
        static readonly Vector3 GamePos = new Vector3(0, 29, 22.5f), GameLook = new Vector3(0, 0, 1);
        static readonly Vector3 HeroPos = new Vector3(-17, 34, 34), HeroLook = new Vector3(1, -4, -1);

        Color[] glowBase; Color curtainBase; float[] lightBase;
        Quaternion[] swingRest;
        float quietLevel;
        bool collected;

        public static void SetTime(float t) => Shader.SetGlobalFloat(TimeId, t);

        void OnEnable()
        {
            collected = false; ApplyView();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update += EditorTick;
#endif
        }

        public void SetView(View v) { view = v; ApplyView(); }

        void ApplyView()
        {
            if (!viewCamera) return;
            var t = viewCamera.transform;
            viewCamera.orthographic = view == View.TopDown;
            switch (view)
            {
                case View.Gameplay: viewCamera.fieldOfView = 44; t.position = GamePos; t.LookAt(GameLook); break;
                case View.Hero: viewCamera.fieldOfView = 40; t.position = HeroPos; t.LookAt(HeroLook); break;
                default: viewCamera.orthographicSize = 24; t.position = new Vector3(0, 42, 0); t.rotation = Quaternion.Euler(90, 180, 0); break; // -Z at the top of the screen
            }
        }

#if UNITY_EDITOR
        double lastTick;
        // Edit mode: advance the lava clock so the Scene view (with Always Refresh) and the Game view show it flowing.
        void EditorTick()
        {
            if (Application.isPlaying || !this) return;
            double now = UnityEditor.EditorApplication.timeSinceStartup;
            if (now - lastTick < 1 / 30.0) return;
            lastTick = now; SetTime((float)(now % 3600));
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        }
#endif

        void Collect()
        {
            collected = true;
            glowBase = new Color[glowMaterials.Length];
            for (int i = 0; i < glowMaterials.Length; i++) if (glowMaterials[i]) glowBase[i] = glowMaterials[i].GetColor(BaseColorId);
            if (curtainMaterial) curtainBase = curtainMaterial.GetColor(BaseColorId);
            lightBase = new float[flickerLights.Length];
            for (int i = 0; i < flickerLights.Length; i++) if (flickerLights[i]) lightBase[i] = flickerLights[i].intensity;
            swingRest = new Quaternion[swingers.Length];
            for (int i = 0; i < swingers.Length; i++) if (swingers[i]) swingRest[i] = swingers[i].localRotation;
        }

        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            if (Application.isPlaying && Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame) SetView((View)(((int)view + 1) % 3));
#endif
            if (!Application.isPlaying) return; // edit mode: the editor tick drives the lava clock; nothing else changes
            if (!collected) Collect();
            float t = Time.time;
            SetTime(t);
            quietLevel = Mathf.MoveTowards(quietLevel, quiet ? 1 : 0, Time.deltaTime / 0.25f);
            foreach (var m in moltenMaterials) if (m) m.SetFloat(QuietId, quietLevel);
            float dim = Mathf.Lerp(1, 0.6f, quietLevel);
            for (int i = 0; i < glowMaterials.Length; i++) // furnace light: dimmed while quiet, with a slow ±6% flicker
                if (glowMaterials[i]) glowMaterials[i].SetColor(BaseColorId, glowBase[i] * dim * (1 + 0.06f * (Mathf.PerlinNoise(t * 3.3f, i * 4.1f) * 2 - 1)));
            float pulse = 1 + 0.08f * Mathf.Sin(t * 1.5708f);
            if (curtainMaterial) { var c = curtainBase; c.a *= pulse * dim; curtainMaterial.SetColor(BaseColorId, c); }
            // ±10% flicker, 3–6 Hz noise, de-synced per light
            for (int i = 0; i < flickerLights.Length; i++)
                if (flickerLights[i]) flickerLights[i].intensity = lightBase[i] * dim * (1 + 0.1f * (Mathf.PerlinNoise(t * (3 + i % 4), i * 7.31f) * 2 - 1));
            for (int i = 0; i < swingers.Length; i++)
                if (swingers[i]) swingers[i].localRotation = swingRest[i] * Quaternion.Euler(Mathf.Sin(t * 0.7f + i * 1.3f) * 2f, 0, Mathf.Sin(t * 0.55f + i) * 2f);
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorTick;
#endif
            if (!collected) return; // restore shared materials so Play mode leaves no trace in the assets
            foreach (var m in moltenMaterials) if (m) m.SetFloat(QuietId, 0);
            for (int i = 0; i < glowMaterials.Length; i++) if (glowMaterials[i]) glowMaterials[i].SetColor(BaseColorId, glowBase[i]);
            if (curtainMaterial) curtainMaterial.SetColor(BaseColorId, curtainBase);
        }
    }
}
