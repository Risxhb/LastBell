using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LastBell.Areas.Area3
{
    // Moving parts of the Verdant Chimes arena (Area 3): the belfry bells sway, the medallion's gear ring turns, and
    // decoration (mosaic glow, lantern glows, motes; all read _LB_DecoGlow) dims to 35% within a quarter second while `quiet` is on (a hazard
    // is up, boss design §8.1). Mirrors update() in prototype/arena-3d/areas/area-3/verdant-arena.js. Press C (or call
    // SetView) to switch between the gameplay camera and the angle view. Built by Last Bell > Area 3 > Open Verdant Chimes.
    [ExecuteAlways]
    public sealed class VerdantChimesArena : MonoBehaviour
    {
        public enum View { Gameplay, Angle, Top }

        [SerializeField] Transform arenaRoot;
        [SerializeField] Camera viewCamera;
        [SerializeField] View view = View.Gameplay;
        [Tooltip("Hazards are up: floor decoration dims (boss design §8.1).")]
        public bool quiet;

        // Gameplay camera: the prototype's (0, 29, 22.5) looking at (0, 0, 1), turned to Unity's north = +Z.
        static readonly Vector3 GamePos = new Vector3(0, 29, -22.5f), GameLook = new Vector3(0, 0, -1);
        static readonly Vector3 AnglePos = new Vector3(-34, 30, -40), AngleLook = new Vector3(1, -3, 2);
        static readonly int DecoGlow = Shader.PropertyToID("_LB_DecoGlow");

        readonly List<(Transform t, Quaternion rest, float phase)> bells = new List<(Transform, Quaternion, float)>();
        Transform gear;
        float glow = 1;
        bool collected;

        void OnEnable() { collected = false; ApplyView(); }

        public void SetView(View v) { view = v; ApplyView(); }

        void ApplyView()
        {
            if (!viewCamera) return;
            switch (view)
            {
                case View.Gameplay: viewCamera.fieldOfView = 44; viewCamera.transform.position = GamePos; viewCamera.transform.LookAt(GameLook); break;
                case View.Angle: viewCamera.fieldOfView = 40; viewCamera.transform.position = AnglePos; viewCamera.transform.LookAt(AngleLook); break;
                default: viewCamera.fieldOfView = 40; viewCamera.transform.position = new Vector3(0, 92, -0.01f); viewCamera.transform.LookAt(Vector3.zero); break;
            }
        }

        void Collect()
        {
            collected = true; bells.Clear(); gear = null;
            if (!arenaRoot) return;
            int i = 0;
            foreach (var t in arenaRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Bell") bells.Add((t, t.localRotation, i++ * 1.9f));
                else if (t.name == "MedallionGear") gear = t;
            }
        }

        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            if (Application.isPlaying && Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame) SetView(view == View.Gameplay ? View.Angle : View.Gameplay);
#endif
            Tick(Application.isPlaying ? Time.deltaTime : 0.016f);
        }

        // One step of the moving parts; the batch capture calls it directly because edit mode has no frame loop.
        public void Tick(float dt)
        {
            if (!collected) Collect();
            float t = Application.isPlaying ? Time.time : (float)(System.DateTime.Now.TimeOfDay.TotalSeconds % 3600);
            glow = Mathf.MoveTowards(glow, quiet ? 0.35f : 1f, dt / 0.25f * 0.65f);
            Shader.SetGlobalFloat(DecoGlow, glow);
            foreach (var (b, rest, ph) in bells) b.localRotation = rest * Quaternion.Euler(Mathf.Sin(t * 0.6f + ph * 2) * 1.5f, 0, Mathf.Sin(t * 0.9f + ph) * 3f);
            if (gear) gear.localRotation = Quaternion.Euler(0, t * 0.01f * Mathf.Rad2Deg, 0);
        }
    }
}
