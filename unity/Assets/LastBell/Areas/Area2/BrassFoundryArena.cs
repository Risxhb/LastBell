using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LastBell.Areas.Area2
{
    // Moving parts of Area 2, The Brass Foundry: the molten channel and rivers scroll, cage lanterns sway, and the
    // molten and window glow dims to 60% while attack telegraphs are up (quiet). Press C (or call SetView) to cycle
    // the gameplay camera, a top-down view framed like the reference image, and the reference board's hero angle.
    // Built by Last Bell > Area 2 â€“ Brass Foundry (Editor/BrassFoundryArenaBuilder.cs).
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
        [SerializeField] Transform[] swingers = new Transform[0];

        // Gameplay camera matches the Bellkeeper arena (prototype index.html, fov 44).
        static readonly Vector3 GamePos = new Vector3(0, 29, 22.5f), GameLook = new Vector3(0, 0, 1);
        static readonly Vector3 HeroPos = new Vector3(-17, 34, 34), HeroLook = new Vector3(1, -4, -1);

        Color[] moltenBase, glowBase;
        Quaternion[] swingRest;
        float level = 1;

        void OnEnable() { moltenBase = null; ApplyView(); }

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

        void Collect()
        {
            moltenBase = new Color[moltenMaterials.Length]; glowBase = new Color[glowMaterials.Length];
            for (int i = 0; i < moltenMaterials.Length; i++) if (moltenMaterials[i]) moltenBase[i] = moltenMaterials[i].GetColor("_BaseColor");
            for (int i = 0; i < glowMaterials.Length; i++) if (glowMaterials[i]) glowBase[i] = glowMaterials[i].GetColor("_BaseColor");
            swingRest = new Quaternion[swingers.Length];
            for (int i = 0; i < swingers.Length; i++) if (swingers[i]) swingRest[i] = swingers[i].localRotation;
        }

        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            if (Application.isPlaying && Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame) SetView((View)(((int)view + 1) % 3));
#endif
            if (!Application.isPlaying) return; // edit mode only places the camera
            if (moltenBase == null) Collect();
            float t = Time.time;
            level = Mathf.MoveTowards(level, quiet ? 0.6f : 1f, Time.deltaTime / 0.25f * 0.4f);
            for (int i = 0; i < moltenMaterials.Length; i++)
            {
                var m = moltenMaterials[i]; if (!m) continue;
                m.SetColor("_BaseColor", moltenBase[i] * level * (0.94f + 0.06f * Mathf.Sin(t * 1.3f + i)));
                m.mainTextureOffset = new Vector2(t * 0.03f * (i % 2 == 0 ? 1 : -0.7f), t * 0.011f);
            }
            for (int i = 0; i < glowMaterials.Length; i++) if (glowMaterials[i]) glowMaterials[i].SetColor("_BaseColor", glowBase[i] * level);
            for (int i = 0; i < swingers.Length; i++)
                if (swingers[i]) swingers[i].localRotation = swingRest[i] * Quaternion.Euler(Mathf.Sin(t * 0.7f + i * 1.3f) * 2f, 0, Mathf.Sin(t * 0.55f + i) * 2f);
        }

        void OnDisable()
        {
            if (moltenBase == null) return; // restore shared material colours so Play mode leaves no trace
            for (int i = 0; i < moltenMaterials.Length; i++) if (moltenMaterials[i]) { moltenMaterials[i].SetColor("_BaseColor", moltenBase[i]); moltenMaterials[i].mainTextureOffset = Vector2.zero; }
            for (int i = 0; i < glowMaterials.Length; i++) if (glowMaterials[i]) glowMaterials[i].SetColor("_BaseColor", glowBase[i]);
        }
    }
}
