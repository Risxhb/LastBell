using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Areas.Area4
{
    // Rim pinnacles and walls on the camera side fade to 25 % while a player stands behind them (from the camera's view)
    // and within 3 m. Swaps each renderer to the transparent twin of its material and sets alpha per renderer.
    // Players register themselves in Players (the batch capture registers its test capsules).
    [ExecuteAlways]
    public sealed class ShatteredHourOccluderFade : MonoBehaviour
    {
        public static readonly List<Transform> Players = new List<Transform>();

        public Material[] opaque, faded;   // pairs: opaque[i] fades to faded[i]
        [Range(0, 1)] public float fadedAlpha = 0.25f;
        public float range = 3f;

        Renderer[] rends; Material[][] original; float alpha = 1;
        MaterialPropertyBlock block;

        void OnEnable()
        {
            rends = GetComponentsInChildren<Renderer>();
            original = new Material[rends.Length][];
            for (int i = 0; i < rends.Length; i++) original[i] = rends[i].sharedMaterials;
            block = new MaterialPropertyBlock();
        }

        void OnDisable() { alpha = 1; Apply(); }

        void Update() { if (Application.isPlaying) Tick(Camera.main, Time.deltaTime); }

        public void Tick(Camera cam, float dt)
        {
            if (rends == null || rends.Length == 0 || !cam) return;
            var b = rends[0].bounds; foreach (var r in rends) b.Encapsulate(r.bounds);
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = -min;
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) * 2 - 1, (i & 2) - 1, ((i & 4) >> 1) - 1));
                Vector2 s = cam.WorldToScreenPoint(c); min = Vector2.Min(min, s); max = Vector2.Max(max, s);
            }
            float depth = cam.WorldToScreenPoint(b.center).z; bool hide = false;
            foreach (var p in Players)
            {
                if (!p) continue;
                var sp = cam.WorldToScreenPoint(p.position + Vector3.up);
                var flat = p.position - b.center; flat.y = 0;
                if (sp.z > depth && sp.x > min.x && sp.x < max.x && sp.y > min.y && sp.y < max.y && flat.magnitude < range + Mathf.Max(b.extents.x, b.extents.z)) { hide = true; break; }
            }
            alpha = Mathf.MoveTowards(alpha, hide ? fadedAlpha : 1, dt * 4);
            Apply();
        }

        void Apply()
        {
            if (rends == null) return;
            bool fade = alpha < 0.999f;
            for (int i = 0; i < rends.Length; i++)
            {
                if (!rends[i]) continue;
                var mats = (Material[])original[i].Clone();
                if (fade) for (int k = 0; k < mats.Length; k++) { int j = System.Array.IndexOf(opaque, mats[k]); if (j >= 0) mats[k] = faded[j]; }
                rends[i].sharedMaterials = mats;
                if (fade)
                {
                    rends[i].GetPropertyBlock(block);
                    var col = mats[0].GetColor("_BaseColor"); col.a = alpha; block.SetColor("_BaseColor", col);
                    rends[i].SetPropertyBlock(block);
                }
                else rends[i].SetPropertyBlock(null);
            }
        }
    }
}
