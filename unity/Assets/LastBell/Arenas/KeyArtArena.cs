using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LastBell.Arenas
{
    // Moving parts of the key-art test arena (the Bellkeeper's Dais, imported from BellkeepersDais.glb):
    // banners sway, the floor gears turn, loose rubble bobs, and the clock dial's gold dims while a warning is up.
    // Mirrors update() in prototype/arena-3d/arena-env.js. Press C (or call SetView) to switch between the
    // gameplay camera and the key-art angle. Built by Last Bell > Test Arena (Key Art).
    [ExecuteAlways]
    public sealed class KeyArtArena : MonoBehaviour
    {
        public enum View { Gameplay, KeyArt }

        [SerializeField] Transform arenaRoot;
        [SerializeField] Camera viewCamera;
        [SerializeField] View view = View.Gameplay;
        [Tooltip("Hazards are up: floor decoration dims (boss design §8.1).")]
        public bool quiet;

        // Gameplay camera matches index.html (CAM_BASE / camLook, fov 44); key-art angle matches arena-look.html.
        // x is mirrored from three.js because glTF import flips handedness on x.
        static readonly Vector3 GamePos = new Vector3(0, 29, 22.5f), GameLook = new Vector3(0, 0, 1);
        static readonly Vector3 ArtPos = new Vector3(23, 16.5f, 25), ArtLook = new Vector3(-2.5f, 0.5f, -4);

        readonly List<(Transform t, float speed)> gears = new List<(Transform, float)>();
        readonly List<(Transform t, float y0, float phase)> debris = new List<(Transform, float, float)>();
        readonly List<(Mesh mesh, Vector3[] rest, float id)> banners = new List<(Mesh, Vector3[], float)>();
        readonly List<Material> dialGlow = new List<Material>();
        float dialLevel = 1;
        bool collected;

        void OnEnable() { collected = false; ApplyView(); }

        public void SetView(View v) { view = v; ApplyView(); }

        void ApplyView()
        {
            if (!viewCamera) return;
            bool game = view == View.Gameplay;
            viewCamera.fieldOfView = game ? 44 : 42;
            viewCamera.transform.position = game ? GamePos : ArtPos;
            viewCamera.transform.LookAt(game ? GameLook : ArtLook);
        }

        void Collect()
        {
            collected = true; gears.Clear(); debris.Clear(); banners.Clear(); dialGlow.Clear();
            if (!arenaRoot) return;
            var rnd = new System.Random(5);
            foreach (var t in arenaRoot.GetComponentsInChildren<Transform>(true))
            {
                switch (t.name)
                {
                    case "GearCW": gears.Add((t, 0.25f / 0.8f)); break;
                    case "GearCCW": gears.Add((t, -0.25f / 0.48f)); break;
                    case "Debris": debris.Add((t, t.localPosition.y, (float)rnd.NextDouble() * 6)); break;
                    case "BannerCloth":
                        if (t.TryGetComponent(out MeshFilter mf) && mf.sharedMesh && mf.sharedMesh.isReadable)
                        {
                            var m = Instantiate(mf.sharedMesh); mf.sharedMesh = m;
                            banners.Add((m, m.vertices, banners.Count * 1.7f));
                        }
                        break;
                    case "DialFace":
                        if (t.TryGetComponent(out Renderer r)) dialGlow.Add(r.material);
                        break;
                }
            }
        }

        void Update()
        {
            if (!Application.isPlaying) return; // edit mode only places the camera (ApplyView)
            if (!collected) Collect();
            float t = Time.time;
            foreach (var (g, speed) in gears) g.localRotation = Quaternion.Euler(0, -t * Mathf.Rad2Deg * speed, 0);
            foreach (var (d, y0, ph) in debris)
            {
                var p = d.localPosition; p.y = y0 + Mathf.Sin(t * 0.5f + ph) * 0.25f; d.localPosition = p;
            }
            foreach (var (mesh, rest, id) in banners)
            {
                var v = new Vector3[rest.Length];
                for (int i = 0; i < rest.Length; i++)
                {
                    float y = rest[i].y, drop = (2 - y) / 4;
                    v[i] = new Vector3(rest[i].x, y, Mathf.Sin(t * 1.3f + y * 1.4f + id) * 0.07f * drop + Mathf.Sin(rest[i].x * 2.5f) * 0.04f);
                }
                mesh.vertices = v; mesh.RecalculateNormals();
            }
            if (dialGlow.Count > 0)
            {
                dialLevel = Mathf.MoveTowards(dialLevel, quiet ? 0.3f : 1f, Time.deltaTime * 3);
                foreach (var m in dialGlow)
                    if (m.HasProperty("emissiveFactor")) m.SetColor("emissiveFactor", new Color(1f, 0.72f, 0.38f) * 0.28f * dialLevel);
            }
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame)
                SetView(view == View.Gameplay ? View.KeyArt : View.Gameplay);
#endif
        }

        void OnValidate() { ApplyView(); }
    }
}
