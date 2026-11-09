using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Aurelion v4 props and markers (8 Nov graphics audit, work/graphics-coverage-20261108/REPORT.md; done as recommended,
    // review later). Presentation only: code-built from Prim meshes (no new assets), sized to the hitboxes they dress, and
    // they never move a hazard or change a timing. Each follows the hazard it belongs to and is removed with it.
    public static class AurelionProps
    {
        static readonly Color Brass = new Color(0.72f, 0.52f, 0.26f), Coral = new Color(1.0f, 0.38f, 0.28f),
            Cobalt = new Color(0.35f, 0.6f, 1.0f), Umber = new Color(0.32f, 0.2f, 0.14f);

        // Errant Planets: a 1.6 m planet (the 0.8 m hitbox exactly) with a coral glow core and a tilted brass ring. Its
        // underside sits just above the floor, so what you see is what touches you.
        public static Transform Planet(Transform parent, float radius)
        {
            var root = new GameObject("Errant Planet").transform; root.SetParent(parent, false);
            var spin = new GameObject("spin").transform; spin.SetParent(root, false); spin.localPosition = new Vector3(0, radius + 0.05f, 0);
            float d = radius * 2;
            Prim.Make(PrimitiveKind.Sphere, spin, Vector3.zero, Vector3.one * d, Umber, 0);
            Prim.Make(PrimitiveKind.Sphere, spin, Vector3.zero, Vector3.one * d * 0.86f, Coral, 2.2f);   // the glowing seams show through
            Prim.Make(PrimitiveKind.Sphere, spin, new Vector3(0.12f, 0.1f, 0.05f) * d, Vector3.one * d * 0.92f, Umber, 0);
            Prim.Make(PrimitiveKind.Cylinder, spin, Vector3.zero, new Vector3(d * 1.55f, 0.012f, d * 1.55f), Brass, 0.6f, Quaternion.Euler(18, 0, 12));
            root.gameObject.AddComponent<Spinner>().Speed = 70f;
            return root;
        }

        // Meridian Towers: four short brass meridian posts on the tower's edge (outside the soak, so nothing stands in the
        // way) with cobalt caps, rising out of the floor as the tower is called and sinking after it lands.
        public static TowerPosts Tower(Transform parent, Vector3 at, float radius, int landTicks)
        {
            var root = new GameObject("Meridian Tower").transform; root.SetParent(parent, false); root.position = new Vector3(at.x, 0, at.z);
            for (int k = 0; k < 4; k++)
            {
                var dir = Quaternion.Euler(0, 45 + k * 90, 0) * Vector3.forward;
                var post = Prim.Make(PrimitiveKind.Cube, root, dir * (radius + 0.25f) + Vector3.up * 0.6f, new Vector3(0.22f, 1.2f, 0.22f), Brass, 0, Quaternion.Euler(0, 45 + k * 90, 0));
                Prim.Make(PrimitiveKind.Sphere, post, new Vector3(0, 0.62f, 0), new Vector3(1.3f, 0.2f, 1.3f), Cobalt, 3f);
            }
            var t = root.gameObject.AddComponent<TowerPosts>(); t.LandTicks = Mathf.Max(1, landTicks);
            return t;
        }

        public sealed class Spinner : MonoBehaviour
        {
            public float Speed;
            void Update() { if (transform.childCount > 0) transform.GetChild(0).Rotate(0, Speed * Time.deltaTime, 0, Space.Self); }
        }

        public sealed class TowerPosts : MonoBehaviour
        {
            public int LandTicks; float t, after = -1;
            public void Land() { if (after < 0) after = 0; }
            void Update()
            {
                t += Time.deltaTime;
                float rise = Mathf.Clamp01(t / 0.6f), y;
                if (after >= 0) { after += Time.deltaTime; y = Mathf.Lerp(0, -1.5f, after / 0.6f); if (after > 0.6f) { Destroy(gameObject); return; } }
                else y = Mathf.Lerp(-1.4f, 0, 1 - (1 - rise) * (1 - rise));
                transform.position = new Vector3(transform.position.x, y, transform.position.z);
                if (t > LandTicks / (float)Sim.Hz + 3f) Destroy(gameObject);   // safety: never outlives its tower
            }
        }

        // ---- Zenith Marks glyphs (replace the SUN / SHADE words): a gold sun with eight rays, a cobalt crescent ----
        static Texture2D sun, shade, arrow;
        public static Texture2D SunGlyph => sun ? sun : (sun = Glyph(true));
        public static Texture2D ShadeGlyph => shade ? shade : (shade = Glyph(false));
        public static Texture2D ArrowGlyph => arrow ? arrow : (arrow = Arrow());

        static Texture2D Glyph(bool isSun)
        {
            const int S = 128;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, name = isSun ? "zenith_sun" : "zenith_shade" };
            var px = new Color32[S * S];
            Color fill = isSun ? new Color(1f, 0.82f, 0.36f) : new Color(0.42f, 0.62f, 1f), ink = new Color(0.06f, 0.08f, 0.13f);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S * 2 - 1, v = (y + 0.5f) / S * 2 - 1, r = Mathf.Sqrt(u * u + v * v);
                    float inside;   // signed distance-ish: > 0 inside the shape (in uv units)
                    if (isSun)
                    {
                        float disc = 0.46f - r;
                        float a = Mathf.Atan2(v, u), ray = Mathf.Abs(Mathf.Repeat(a / (Mathf.PI * 2) * 8 + 0.5f, 1f) - 0.5f) * 2;   // 0 on a ray's axis
                        float rays = (r > 0.5f && r < 0.94f) ? (0.14f * (1 - (r - 0.5f) / 0.44f) - ray * 0.4f) : -1;
                        inside = Mathf.Max(disc, rays);
                    }
                    else
                    {
                        float outer = 0.82f - r, bite = Mathf.Sqrt((u - 0.36f) * (u - 0.36f) + (v - 0.22f) * (v - 0.22f)) - 0.66f;
                        inside = Mathf.Min(outer, bite);
                    }
                    float aFill = Mathf.Clamp01(inside * S * 0.5f + 0.5f), aEdge = Mathf.Clamp01((inside + 0.07f) * S * 0.5f + 0.5f);
                    var c = Color.Lerp(ink, fill, aFill); c.a = aEdge;
                    px[y * S + x] = c;
                }
            tex.SetPixels32(px); tex.Apply(false, true); return tex;
        }

        static Texture2D Arrow()
        {
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, name = "zenith_then" };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S, v = Mathf.Abs((y + 0.5f) / S - 0.5f);
                    float d = Mathf.Abs(u - 0.65f + v * 0.6f) - 0.07f;   // a chevron ">" (points right, to the next mark)
                    float a = (v < 0.3f) ? Mathf.Clamp01(-d * S + 0.5f) : 0;
                    px[y * S + x] = new Color(1f, 0.95f, 0.85f, a);
                }
            tex.SetPixels32(px); tex.Apply(false, true); return tex;
        }
    }
}
