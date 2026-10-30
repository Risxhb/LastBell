using System.Collections.Generic;
using LastBell.Run;
using UnityEngine;

namespace LastBell.Arenas.MetronomeCourt
{
    // Painted surface kit (October 27, 2026, Risxhb: "add textures so these elements don't look so plain").
    // Five small generated albedo textures, painted at one consistent world scale: coursed masonry, dressed ivory stone,
    // brass, plated iron and folded cloth. Broad marks only (blocks, joints, mottling, edge wear, folds): no fine grain
    // and no normal maps (the floor's October 25 rule). The floor's own materials are untouched.
    // Every textured part gets world-space box-mapped UVs at build time (before static batching), so a 30 m ledge
    // and a 0.7 m bollard carry the same size of stone; moving parts keep their baked UVs as they turn.
    public sealed partial class MetronomeCourtArena
    {
        public static bool SurfaceKit = true;   // review switch; the October 27 pass-1 build had no kit
        enum Surface { None, Masonry, Ivory, Brass, Iron, Cloth, Slate, Paving, Weathered }   // r3: Weathered (pier masonry), assigned explicitly   // pass 4: slate roofs and soft bridge paving (assigned explicitly)
        static readonly Dictionary<Surface, Texture2D> surfaceTex = new Dictionary<Surface, Texture2D>();
        static readonly Dictionary<Surface, float> surfaceMean = new Dictionary<Surface, float>();
        // metres covered by one texture repeat
        static float TileOf(Surface s) => s == Surface.Masonry || s == Surface.Weathered ? 3.6f : s == Surface.Ivory ? 2.0f : s == Surface.Brass ? 1.2f : s == Surface.Iron ? 2.0f : s == Surface.Slate ? 1.8f : s == Surface.Paving ? 2.4f : 0.8f;
        public int SurfaceUvBakes { get; private set; }

        static Surface Classify(Color c, float metal, bool glow)
        {
            if (glow) return Surface.None;
            if (c.g > c.r * 1.8f && c.b > c.r * 1.8f) return Surface.Cloth;   // teal banners
            if (metal > 0.5f) return c.r > c.b * 1.5f ? Surface.Brass : Surface.Iron;
            float lum = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
            return lum > 0.45f ? Surface.Ivory : Surface.Masonry;
        }

        // called from M(): attach the matching painted texture and compensate its mean so the swatch colour holds
        static void ApplySurface(Material m, Color c, float metal, bool glow)
        {
            if (!SurfaceKit) return;
            var s = Classify(c, metal, glow); if (s == Surface.None) return;
            var tex = SurfaceTexture(s);
            m.SetTexture("_BaseMap", tex); m.SetTexture("_MainTex", tex);
            m.SetColor("_BaseColor", c / surfaceMean[s]);
        }

        // pass 4: a kit material with an explicit surface (roof slate, bridge paving), cached like M()
        static readonly Dictionary<int, Material> surfMats = new Dictionary<int, Material>();
        static Material SurfM(Color c, Surface s, float smooth = 0.18f)
        {
            int key = Mathf.RoundToInt(c.r * 255) | Mathf.RoundToInt(c.g * 255) << 8 | Mathf.RoundToInt(c.b * 255) << 16 | (int)s << 24;
            if (surfMats.TryGetValue(key, out var m) && m) return m;
            m = Prim.Material(c); m.name = "MetronomeCourt_" + s + "_" + key;
            m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0f);
            if (SurfaceKit) { var tex = SurfaceTexture(s); m.SetTexture("_BaseMap", tex); m.SetTexture("_MainTex", tex); m.SetColor("_BaseColor", c / surfaceMean[s]); }
            return surfMats[key] = m;
        }
        static Material SlateM(Color c) => SurfM(c, Surface.Slate, 0.25f);
        static Material PavingM(Color c) => SurfM(c, Surface.Paving);

        static float SHash(int x, int y, int seed) { uint n = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041); n = (n ^ (n >> 13)) * 1274126177u; return ((n ^ (n >> 16)) & 0xffff) / 65535f; }
        // tileable value noise with period p
        static float SNoise(float x, float y, int p, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y); float fx = x - xi, fy = y - yi; fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            int x0 = ((xi % p) + p) % p, y0 = ((yi % p) + p) % p, x1 = (x0 + 1) % p, y1 = (y0 + 1) % p;
            return Mathf.Lerp(Mathf.Lerp(SHash(x0, y0, seed), SHash(x1, y0, seed), fx), Mathf.Lerp(SHash(x0, y1, seed), SHash(x1, y1, seed), fx), fy);
        }
        static float Broad(float u, float v, int seed) => (SNoise(u * 3, v * 3, 3, seed) * 0.6f + SNoise(u * 6, v * 6, 6, seed + 1) * 0.4f) - 0.5f;   // -0.5..0.5, no fine octave

        static Texture2D SurfaceTexture(Surface s)
        {
            if (surfaceTex.TryGetValue(s, out var t) && t) return t;
            const int S = 256;
            var px = new Color32[S * S]; double sum = 0;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S, v = (y + 0.5f) / S, val = 1f;
                    switch (s)
                    {
                        case Surface.Masonry:
                        {   // pass 4 (final critic: "the drum, piers and towers are smooth plain planes"; the pass-3 joints,
                            // 1 cm soft at 0.7, vanished on dark stone): five 0.72 m courses of 1.8 m blocks per 3.6 m repeat,
                            // joints about 5 cm wide at half value, per-block value steps, pigment patches, a top-lit face
                            // gradient, a lit upper arris and chipped corners. Broad enough to hold at tower distance.
                            int row = Mathf.FloorToInt(v * 5); float rv = v * 5 - row;
                            float off = SHash(row, 0, 11) * 0.6f + (row % 2) * 0.5f, bu = (u * 2 + off) % 2f; int block = Mathf.FloorToInt(bu); float fu = bu - block;
                            float joint = Mathf.Min(Mathf.Min(rv, 1 - rv), Mathf.Min(fu, 1 - fu) * 2.5f);    // in course heights (block 1.8 m / course 0.72 m)
                            val = 0.84f + 0.22f * SHash(block, row, 12);
                            val *= 1 + 0.14f * (SNoise(u * 2, v * 2, 2, 15) - 0.5f) + 0.08f * (SNoise(u * 5, v * 5, 5, 16) - 0.5f);   // pigment patches
                            val *= Mathf.Lerp(0.86f, 1.04f, Mathf.SmoothStep(0f, 1f, rv));                  // top-lit face
                            val *= Mathf.Lerp(1f, 1.12f, Mathf.SmoothStep(0.80f, 0.90f, rv) * Mathf.SmoothStep(0.985f, 0.95f, rv));   // lit upper arris
                            if (SHash(block, row, 14) > 0.6f && fu + (1 - rv) * 0.6f < 0.3f) val *= 0.84f;   // a chipped corner
                            val *= Mathf.Lerp(0.5f, 1f, Mathf.SmoothStep(0.03f, 0.09f, joint));             // painted joint, one value step down
                            break;
                        }
                        case Surface.Weathered:
                        {   // r3 (October 28 critic, F5: "perfectly clean"): the pier masonry, older than the court's. The same
                            // courses and blocks as Masonry, with stronger per-block steps, rain streaks that run down from each
                            // course joint (narrow vertical noise, darkest just under the joint, gone by the lower fifth), a dark
                            // grime band under each joint and a lighter worn arris above it. Two broad values that hold at range.
                            int row = Mathf.FloorToInt(v * 5); float rv = v * 5 - row;
                            float off = SHash(row, 0, 11) * 0.6f + (row % 2) * 0.5f, bu = (u * 2 + off) % 2f; int block = Mathf.FloorToInt(bu); float fu = bu - block;
                            float joint = Mathf.Min(Mathf.Min(rv, 1 - rv), Mathf.Min(fu, 1 - fu) * 2.5f);
                            val = 0.80f + 0.30f * SHash(block, row, 12);
                            val *= 1 + 0.16f * (SNoise(u * 2, v * 2, 2, 15) - 0.5f) + 0.08f * (SNoise(u * 5, v * 5, 5, 16) - 0.5f);
                            float streak = Mathf.Clamp01((SNoise(u * 16, v * 1.6f, 16, 71) - 0.45f) * 2.6f);
                            val *= 1 - 0.32f * streak * Mathf.SmoothStep(0.15f, 0.85f, rv);                       // rain streaks under each joint
                            val *= Mathf.Lerp(1f, 0.82f, Mathf.SmoothStep(0.72f, 0.94f, rv) * Mathf.SmoothStep(0.99f, 0.95f, rv));   // grime band under the joint
                            val *= Mathf.Lerp(1f, 1.16f, Mathf.SmoothStep(0.10f, 0.04f, rv) * Mathf.SmoothStep(0.0f, 0.03f, rv));    // worn arris above the joint
                            if (SHash(block, row, 14) > 0.55f && fu + (1 - rv) * 0.6f < 0.34f) val *= 0.8f;    // chipped corners
                            val *= Mathf.Lerp(0.48f, 1f, Mathf.SmoothStep(0.03f, 0.09f, joint));
                            break;
                        }
                        case Surface.Ivory:
                        {   // pass 3: dressed limestone with visible broad courses (1 m) and staggered joints (1.5 m), pigment
                            // patches, weathering streaks running down from each course, and a worn, lit top arris
                            int row = Mathf.FloorToInt(v * 2); float rv = v * 2 - row;
                            float bu = (u * 1.333f + (row % 2) * 0.5f) % 1.333f; float fu = bu / 1.333f;
                            float joint = Mathf.Min(Mathf.Min(rv, 1 - rv), Mathf.Min(fu, 1 - fu) * 1.33f);
                            float streak = SNoise(u * 7, v * 1.2f, 7, 21) - 0.45f;
                            val = 0.93f + 0.12f * (SNoise(u * 2, v * 2, 2, 22) - 0.5f) + 0.05f * (SHash(Mathf.FloorToInt(bu * 3), row, 23) - 0.5f);
                            val -= 0.16f * Mathf.Max(0, streak) * Mathf.SmoothStep(1f, 0.3f, rv);
                            val *= 1 + 0.12f * (SHash(Mathf.FloorToInt(bu / 1.333f * 2), row, 24) - 0.5f);   // pass 4: per-block value steps
                            val *= Mathf.Lerp(1f, 1.07f, Mathf.SmoothStep(0.8f, 0.93f, rv));
                            if (SHash(Mathf.FloorToInt(fu * 6), row, 25) > 0.72f && rv > 0.86f) val *= 0.84f;   // pass 4: rounded chips on the lit arris
                            val *= Mathf.Lerp(0.6f, 1f, Mathf.SmoothStep(0.02f, 0.075f, joint));              // pass 4: joints that read (critic: "flat cream")
                            break;
                        }
                        case Surface.Brass:
                        {   // aged brass: broad brushed diagonal planes, dark patina pooling in low areas
                            float plane = Mathf.SmoothStep(0.35f, 0.65f, Mathf.Repeat(u * 2 + v, 1));
                            float patina = Mathf.SmoothStep(0.15f, 0.35f, Broad(u, v, 31));
                            val = 0.86f + 0.12f * plane - 0.26f * patina;
                            break;
                        }
                        case Surface.Iron:
                        {   // riveted plates two to a course: dark seams, a worn lighter top edge
                            int row = Mathf.FloorToInt(v * 2); float rv = v * 2 - row; float bu = (u * 2 + row * 0.5f) % 2f; float fu = bu - Mathf.Floor(bu);
                            float seam = Mathf.Min(Mathf.Min(rv, 1 - rv) * 2, Mathf.Min(fu, 1 - fu) * 2);
                            val = (0.92f + 0.07f * SHash(Mathf.FloorToInt(bu), row, 41)) * (1 + 0.04f * Broad(u, v, 42));
                            val *= Mathf.Lerp(0.66f, 1f, Mathf.SmoothStep(0.01f, 0.04f, seam));
                            if (rv > 0.9f && rv < 0.97f) val *= 1.12f;
                            break;
                        }
                        case Surface.Slate:
                        {   // pass 4 (critic: "pyramid caps"): overlapping slate courses, 0.3 m rows of 0.45 m slates, the foot
                            // of each row darker (the overlap shadow), small per-slate value steps
                            int row = Mathf.FloorToInt(v * 6); float rv = v * 6 - row;
                            float bu = (u * 4 + (row % 2) * 0.5f) % 4f; int sl = Mathf.FloorToInt(bu); float fu = bu - sl;
                            val = 0.84f + 0.22f * SHash(sl, row, 51);
                            val *= Mathf.Lerp(0.6f, 1f, Mathf.SmoothStep(0f, 0.25f, rv));
                            val *= Mathf.Lerp(0.74f, 1f, Mathf.SmoothStep(0f, 0.06f, Mathf.Min(fu, 1 - fu)));
                            val *= 1 + 0.08f * (SNoise(u * 3, v * 3, 3, 52) - 0.5f);
                            break;
                        }
                        case Surface.Paving:
                        {   // pass 4 (critic: "blocky, hard-stepped patches" on the bridge): the slabs are separate stones, so the
                            // paving texture carries only soft wear: broad mottle and a few soft stains, no blocks and no steps
                            val = 1f + 0.1f * (SNoise(u * 3, v * 3, 3, 61) - 0.5f) + 0.05f * (SNoise(u * 6, v * 6, 6, 62) - 0.5f);
                            val -= 0.07f * Mathf.SmoothStep(0.62f, 0.8f, SNoise(u * 4, v * 4, 4, 63));
                            break;
                        }
                        case Surface.Cloth:
                        {   // broad vertical folds and a darker hem band
                            val = 0.84f + 0.2f * (0.5f + 0.5f * Mathf.Sin(u * Mathf.PI * 2 * 3 + Mathf.Sin(v * 6.28f) * 0.4f)) - 0.08f;
                            if (v < 0.08f) val *= 0.85f;
                            break;
                        }
                    }
                    val = Mathf.Clamp01(val); sum += val;
                    byte b = (byte)Mathf.RoundToInt(val * 255);
                    px[y * S + x] = new Color32(b, b, b, 255);
                }
            t = new Texture2D(S, S, TextureFormat.RGBA32, true, false) { name = "MetronomeCourt_Surface_" + s, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            t.SetPixels32(px); t.Apply(true, false); t.Compress(true); t.Apply(false, Application.isPlaying);   // block-compressed; GPU only in the game, readable in the editor so a baked copy (menu showcase) can be saved into a scene
            surfaceTex[s] = t; surfaceMean[s] = (float)(sum / (S * S));
            return t;
        }

        readonly HashSet<MeshFilter> baked = new HashSet<MeshFilter>();
        // world-space box mapping for every renderer that carries a kit texture (floor, clouds, glow and FX are skipped)
        void BakeSurfaceUVs(Transform under)
        {
            if (!SurfaceKit) return;
            var kit = new Dictionary<Texture, float>();
            foreach (var kv in surfaceTex) kit[kv.Value] = TileOf(kv.Key);
            foreach (var mf in under.GetComponentsInChildren<MeshFilter>(true))
            {
                if (baked.Contains(mf) || !mf.sharedMesh) continue;
                var r = mf.GetComponent<MeshRenderer>(); if (!r || !r.sharedMaterial || r.isPartOfStaticBatch) continue;
                if (!r.sharedMaterial.HasTexture("_BaseMap")) continue;   // clouds, pools and FX shaders have no base map (asking logs an error)
                var tex = r.sharedMaterial.GetTexture("_BaseMap"); if (!tex || !kit.TryGetValue(tex, out float tile)) continue;
                var src = mf.sharedMesh; if (!src.isReadable) continue;
                var mesh = Instantiate(src); mesh.name = src.name + " (box UV)";
                var v = mesh.vertices; var n = mesh.normals; var uv = new Vector2[v.Length];
                var l2w = mf.transform.localToWorldMatrix; var nrm = l2w.inverse.transpose;
                bool round = src.name.StartsWith("Prim_Cylinder") || src.name.StartsWith("Prim_Cone") || src.name.StartsWith("Sector");
                var centre = mf.transform.position;
                for (int i = 0; i < v.Length; i++)
                {
                    var p = l2w.MultiplyPoint3x4(v[i]); var w = n.Length > i ? nrm.MultiplyVector(n[i]).normalized : Vector3.up;
                    float ax = Mathf.Abs(w.x), ay = Mathf.Abs(w.y), az = Mathf.Abs(w.z);
                    if (round && ay < 0.6f)
                    {   // pass 3: wrap-around mapping on round sides (arc length about the part's axis), so a drum or ledge
                        // carries continuous courses instead of a per-vertex patchwork of planar projections
                        var d = p - centre; float rad = Mathf.Max(0.3f, new Vector2(d.x, d.z).magnitude);
                        uv[i] = new Vector2(Mathf.Atan2(d.x, d.z) * rad, p.y) / tile;
                    }
                    else if (round)
                    {   // pass 4: tops of round parts (the rim ledge ring) in polar form: courses run round the ring and joints
                        // point at the centre, instead of straight bands that read as steps across the ledge
                        var d = p - centre; float rad = new Vector2(d.x, d.z).magnitude;
                        uv[i] = new Vector2(Mathf.Atan2(d.x, d.z) * Mathf.Max(rad, 0.3f), rad) / tile;
                    }
                    else uv[i] = (ay >= ax && ay >= az ? new Vector2(p.x, p.z) : ax >= az ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y)) / tile;
                }
                mesh.uv = uv; mf.sharedMesh = mesh; baked.Add(mf); SurfaceUvBakes++;
            }
        }
    }
}
