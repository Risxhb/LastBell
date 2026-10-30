using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace LastBell.Menu.EditorTools
{
    // Main menu refinement (October 26, 2026), brief: docs/main-menu/menu-refinement-20261026.md.
    // One re-runnable pass per asset; each deletes what it generated before, rebuilds with absolute values and saves.
    // Everything is menu-only (materials, textures and meshes under ConfrontationGenerated/Refine).
    // Batch: Unity.exe -batchmode -projectPath unity -executeMethod LastBell.Menu.EditorTools.ConfrontationMenuRefine.<Pass>
    public static class ConfrontationMenuRefine
    {
        const string Gen = "Assets/LastBell/Menu/ConfrontationGenerated", Dir = Gen + "/Refine";

        // ---------------- shared helpers ----------------
        static UnityEngine.SceneManagement.Scene Open() => EditorSceneManager.OpenScene(ConfrontationMenu.ScenePath);
        static void Save(UnityEngine.SceneManagement.Scene s) { EditorSceneManager.MarkSceneDirty(s); EditorSceneManager.SaveScene(s); AssetDatabase.SaveAssets(); }
        static Renderer[] Renderers(UnityEngine.SceneManagement.Scene s) => s.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).ToArray();
        static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;
        static void Rebuild(string rootName, out Transform root)
        {
            foreach (var g in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(t => t && t.name == rootName && !t.parent).ToArray())
                UnityEngine.Object.DestroyImmediate(g.gameObject);
            root = new GameObject(rootName).transform;
        }
        static void EnsureDir() { if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder(Gen, "Refine"); }
        static Material Mat(string name, Shader shader, Action<Material> set)
        {
            EnsureDir(); var path = Dir + "/" + name + ".mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(shader) { name = name }; AssetDatabase.CreateAsset(m, path); } else if (m.shader != shader) m.shader = shader;
            set(m); EditorUtility.SetDirty(m); return m;
        }
        static Texture2D Tex(string name, int w, int h, Func<int, int, Color> px, bool linear = false, TextureWrapMode wrap = TextureWrapMode.Clamp, bool normal = false)
        {
            EnsureDir(); var path = Dir + "/" + name + ".png";
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false, linear);
            var c = new Color[w * h]; for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) c[y * w + x] = px(x, y);
            t.SetPixels(c); t.Apply(); File.WriteAllBytes(path, t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default; imp.sRGBTexture = !linear && !normal;
            imp.alphaIsTransparency = !normal && !linear; imp.wrapMode = wrap; imp.mipmapEnabled = true; imp.anisoLevel = 8; imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport(); return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static Mesh MeshAsset(string name, Mesh m) { EnsureDir(); var path = Dir + "/" + name + ".asset"; AssetDatabase.DeleteAsset(path); m.name = name; AssetDatabase.CreateAsset(m, path); return m; }

        // value noise / fbm (tileable when period > 0)
        static float Hash(int x, int y, int s) { uint h = (uint)(x * 374761393 + y * 668265263 + s * 1442695041); h = (h ^ (h >> 13)) * 1274126177u; return (h ^ (h >> 16)) / 4294967295f; }
        static float Noise(float x, float y, int s)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y); float fx = x - xi, fy = y - yi; fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            return Mathf.Lerp(Mathf.Lerp(Hash(xi, yi, s), Hash(xi + 1, yi, s), fx), Mathf.Lerp(Hash(xi, yi + 1, s), Hash(xi + 1, yi + 1, s), fx), fy);
        }
        // GLSL-style smoothstep(edge0, edge1, x). Not Mathf.SmoothStep, which interpolates between two values by t.
        static float SS(float e0, float e1, float x) { float t = Mathf.Clamp01((x - e0) / (e1 - e0)); return t * t * (3 - 2 * t); }
        static float Fbm(float x, float y, int s, int oct = 5) { float n = 0, a = .5f, t = 0; for (int i = 0; i < oct; i++) { n += Noise(x, y, s + i * 17) * a; t += a; x *= 2.03f; y *= 2.03f; a *= .5f; } return n / t; }

        static Shader Lit => Shader.Find("Universal Render Pipeline/Lit");
        static void Transparent(Material m)
        {
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetFloat("_AlphaClip", 0); m.SetFloat("_ZWrite", 0);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetOverrideTag("RenderType", "Transparent"); m.renderQueue = (int)RenderQueue.Transparent;
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.DisableKeyword("_ALPHATEST_ON");
            // URP's "preserve specular" keeps reflections at full strength even where alpha is 0, so a soft puddle mask
            // still reflected as a full rectangle; turn it off so alpha masks reflections too
            m.SetFloat("_BlendModePreserveSpecular", 0); m.DisableKeyword("_ALPHAPREMULTIPLY_ON"); m.DisableKeyword("_ALPHAMODULATE_ON");
            m.SetFloat("_ReceiveShadows", 1);
        }

        // floor height under a point: temporary mesh colliders on the given renderers, one ray down
        static bool FloorAt(Renderer[] floor, Vector2 xz, out Vector3 hit, out Renderer on)
        {
            hit = default; on = null; var added = new System.Collections.Generic.List<MeshCollider>();
            foreach (var r in floor) { var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh || r.GetComponent<Collider>()) continue; var c = r.gameObject.AddComponent<MeshCollider>(); c.sharedMesh = mf.sharedMesh; added.Add(c); }
            Physics.SyncTransforms();
            bool ok = Physics.Raycast(new Vector3(xz.x, 20, xz.y), Vector3.down, out var h, 40);
            if (ok) { hit = h.point; on = h.collider.GetComponent<Renderer>(); }
            foreach (var c in added) UnityEngine.Object.DestroyImmediate(c);
            return ok && on && floor.Contains(on);
        }
        static Mesh Disc(float radius, int seg)
        {
            var v = new Vector3[seg + 1]; var uv = new Vector2[seg + 1]; var t = new int[seg * 3];
            v[0] = Vector3.zero; uv[0] = new Vector2(.5f, .5f);
            for (int i = 0; i < seg; i++) { float a = i * Mathf.PI * 2 / seg; v[i + 1] = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius; uv[i + 1] = new Vector2(.5f + Mathf.Cos(a) * .5f, .5f + Mathf.Sin(a) * .5f); t[i * 3] = 0; t[i * 3 + 1] = (i + 1) % seg + 1; t[i * 3 + 2] = i + 1; }
            var m = new Mesh { vertices = v, uv = uv, triangles = t }; m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }
        static Mesh Quad(float w, float d)
        {
            var m = new Mesh { vertices = new[] { new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, 0, -d / 2), new Vector3(w / 2, 0, d / 2), new Vector3(-w / 2, 0, d / 2) },
                uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) }, triangles = new[] { 0, 2, 1, 0, 3, 2 } };
            m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }
        static GameObject Flat(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos, float yaw = 0, float scale = 1)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0)); go.transform.localScale = Vector3.one * scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = true; r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            return go;
        }

        // ---------------- 1. clock floor: wear and rain puddles ----------------
        // Settings live here so the critic's refinement pass only edits numbers.
        // Refinement 1 (critic, October 26, 2026): reflections capped by an occlusion mask (grazing Fresnel made the pools
        // mirror the pale horizon), puddles moved out from under the menu column and from behind the hero, warm lantern
        // glints, a soaked lower-gloss edge band, and stronger (still subtle) wear.
        public static float WearStrength = 1.0f, PuddleAlpha = .9f, PuddleSmooth = .96f, PuddleReflect = .3f;
        public static readonly (float x, float z, float size, float yaw)[] Puddles =
        {
            (0.4f, 5.2f, 1.3f, 20), (2.8f, 4.6f, 1.15f, 110), (-1.1f, 3.0f, 2.0f, 65), (2.2f, 1.2f, 1.0f, 200),
            (1.0f, -1.5f, 1.8f, 300), (1.3f, -3.6f, 2.6f, 150), (-1.9f, -7.6f, 2.0f, 40), (-0.6f, -5.0f, 1.4f, 260),
        };
        static readonly int[] GlintPuddles = { 1, 2 };   // on the mirror points of the far lanterns
        public static void FloorPass()
        {
            var s = Open(); var rs = Renderers(s);
            var dialR = rs.Where(r => r.gameObject.activeInHierarchy && r.sharedMaterials.Any(m => m && m.name == "WetDial")).ToArray();
            var floorR = rs.Where(r => r.gameObject.activeInHierarchy && r.sharedMaterials.Any(m => m && (m.name == "WetDial" || m.name == "WetPaving" || m.name == "MenuWetStone"))).ToArray();
            if (dialR.Length == 0) throw new InvalidOperationException("WetDial renderers not found");
            var db = dialR[0].bounds; foreach (var r in dialR) db.Encapsulate(r.bounds);
            Vector2 c = new Vector2(db.center.x, db.center.z); float radius = Mathf.Max(db.extents.x, db.extents.z);
            Rebuild("Menu floor wear and puddles", out var root);

            // wear texture: grime toward the rim and the dial rings, scuffs along the walking line (z), hairline cracks
            var wear = Tex("MenuDialWear", 1024, 1024, (x, y) =>
            {
                float u = x / 1023f * 2 - 1, v = y / 1023f * 2 - 1, r = Mathf.Sqrt(u * u + v * v);
                if (r > 1) return new Color(0, 0, 0, 0);
                float grime = SS(.55f, 1f, r) * .9f;                                      // collects toward the rim
                grime += Mathf.Max(0, Fbm(u * 3, v * 3, 11) - .45f) * 1.4f;                             // blotches
                float ring = Mathf.Abs(Mathf.Repeat(r * 6.5f, 1) - .5f); grime += SS(.08f, 0, ring) * .5f * Fbm(u * 8, v * 8, 5);    // dirt in the recessed rings
                float path = Mathf.Exp(-u * u / .05f) * (1 - Mathf.Abs(v));                           // the walking line toward the boss: worn cleaner, with long scuffs
                float scuff = Mathf.Max(0, Fbm(u * 40, v * 2.5f, 23) - .52f) * 2.2f;
                grime = grime * (1 - path * .6f) + scuff * path * .5f;
                // hairline cracks: thin ridges of a warped noise field
                float cr = Fbm(u * 2.2f + Fbm(u * 5, v * 5, 41) * .6f, v * 2.2f, 37); float crack = SS(.02f, 0, Mathf.Abs(cr - .5f)) * SS(.35f, .7f, Fbm(u * 1.5f, v * 1.5f, 51));
                float a = Mathf.Clamp01(grime * WearStrength + crack * 1.0f) * SS(1f, .97f, r);
                float tone = .75f + .25f * Fbm(u * 6, v * 6, 61);
                return new Color(.055f * tone, .048f * tone, .042f * tone, a);
            });
            var wearM = Mat("MenuDialWear", Lit, m =>
            {
                Transparent(m); m.SetTexture("_BaseMap", wear); m.SetColor("_BaseColor", Color.white); m.SetFloat("_Smoothness", .18f); m.SetFloat("_Metallic", 0);
                m.renderQueue = 3000;
            });
            var disc = MeshAsset("MenuDialWearDisc", Disc(radius * .985f, 128));
            Flat(root, "Dial wear", disc, wearM, new Vector3(c.x, db.max.y + .005f, c.y));

            // puddle masks: two irregular blobs so pools never repeat in shape. Each has three maps from one shape:
            //   base alpha: water body ~1, a soaked outer band ~0.5, a wide 0.2 feather (no decal outline)
            //   gloss (metallic-gloss A): mirror centre, ~0.4 on the soaked band
            //   occlusion (G): caps the probe reflection to PuddleReflect in the centre so pools read darker than the stone
            float Shape(float u, float v, int seed, out float band)
            {
                float ang = Mathf.Atan2(v, u), r = Mathf.Sqrt(u * u + v * v);
                float edge = .62f + .22f * (Fbm(Mathf.Cos(ang) * 1.6f + 3, Mathf.Sin(ang) * 1.6f + 3, seed) - .5f) * 2 + .1f * (Fbm(u * 6, v * 6, seed + 9) - .5f);
                float outer = SS(edge + .05f, edge - .15f, r);                    // soaked stone reaches a little further
                float water = SS(edge - .15f, edge - .30f, r);                    // the water body inside it
                band = Mathf.Clamp01(outer - water);
                return water;
            }
            Texture2D[] PuddleMaps(string name, int seed) => new[]
            {
                Tex(name, 512, 512, (x, y) => { float w = Shape(x / 511f * 2 - 1, y / 511f * 2 - 1, seed, out var band); return new Color(1, 1, 1, Mathf.Clamp01(w * (.88f + .12f * Fbm(x / 51f, y / 51f, seed + 3)) + band * .5f)); }),
                Tex(name + "Gloss", 512, 512, (x, y) => { float w = Shape(x / 511f * 2 - 1, y / 511f * 2 - 1, seed, out var band); float g = Mathf.Clamp01(w + band * .42f); return new Color(0, 0, 0, g); }, true),
                Tex(name + "Occlusion", 512, 512, (x, y) => { float w = Shape(x / 511f * 2 - 1, y / 511f * 2 - 1, seed, out var band); float o = Mathf.Lerp(.75f, PuddleReflect, w); return new Color(o, o, o, 1); }, true),
            };
            var maps = new[] { PuddleMaps("MenuPuddleA", 101), PuddleMaps("MenuPuddleB", 202) };
            var puddleM = maps.Select((t, i) => Mat("MenuPuddle" + (char)('A' + i), Lit, m =>
            {
                Transparent(m); m.SetTexture("_BaseMap", t[0]); m.SetColor("_BaseColor", new Color(.03f, .028f, .045f, PuddleAlpha));   // dark violet water
                m.SetTexture("_MetallicGlossMap", t[1]); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_Smoothness", PuddleSmooth); m.SetFloat("_GlossMapScale", PuddleSmooth); m.SetFloat("_Metallic", 0);
                m.SetTexture("_OcclusionMap", t[2]); m.EnableKeyword("_OCCLUSIONMAP"); m.SetFloat("_OcclusionStrength", 1);
                m.SetFloat("_SpecularHighlights", 1); m.SetFloat("_EnvironmentReflections", 1);
                m.renderQueue = 3001;
            })).ToArray();
            // warm lantern glints: soft additive amber streaks lying in two pools, stretched along the view direction
            var streakTex = Tex("MenuPuddleGlint", 64, 256, (x, y) =>
            {
                float u = x / 63f * 2 - 1, v = y / 255f * 2 - 1;
                float a = Mathf.Exp(-u * u * 9) * Mathf.Exp(-v * v * 3) * (.75f + .25f * Noise(v * 18, 2, 9));
                return new Color(1, 1, 1, a);
            });
            var glintM = Mat("MenuPuddleGlint", Shader.Find("Universal Render Pipeline/Particles/Unlit"), m =>
            {
                m.SetTexture("_BaseMap", streakTex); m.SetColor("_BaseColor", new Color(1f, .48f, .16f, .55f));
                m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 2); m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.One); m.SetFloat("_ZWrite", 0);
                m.SetOverrideTag("RenderType", "Transparent"); m.renderQueue = 3002; m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            });
            var camPos = new Vector3(.6f, .75f, 10.9f);   // the menu's runtime camera (ConfrontationMenu.CameraPosition)
            var quad = MeshAsset("MenuPuddleQuad", Quad(1, 1));
            int placed = 0;
            for (int i = 0; i < Puddles.Length; i++)
            {
                var p = Puddles[i];
                if (!FloorAt(floorR, new Vector2(p.x, p.z), out var hit, out var on)) { Debug.LogWarning($"Floor pass: no floor under puddle {i} at ({p.x},{p.z})"); continue; }
                var go = Flat(root, "Rain puddle " + i, quad, puddleM[i % 2], hit + Vector3.up * .009f, p.yaw, p.size);
                go.transform.localScale = new Vector3(p.size, 1, p.size * (.55f + .3f * Hash(i, 7, 3)));   // pools are longer than wide
                if (Array.IndexOf(GlintPuddles, i) >= 0)
                    for (int k = 0; k < 2; k++)
                    {
                        var toCam = Vector3.ProjectOnPlane(camPos - hit, Vector3.up).normalized;
                        var side = Vector3.Cross(Vector3.up, toCam) * ((k - .5f) * p.size * .25f);
                        var g = Flat(root, $"Puddle glint {i}.{k}", quad, glintM, hit + side + Vector3.up * .011f);
                        g.transform.rotation = Quaternion.LookRotation(toCam, Vector3.up);
                        g.transform.localScale = new Vector3(.08f, 1, .8f * (1 - .3f * k));
                    }
                placed++;
            }
            Save(s);
            Debug.Log($"Floor pass: dial centre {c} radius {radius:0.00} top {db.max.y:0.000}; wear disc + {placed} puddles");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // ---------------- 2. gold rim: aged, rain-wet brass with a polished worn top ----------------
        // Refinement 1 (critic, October 26, 2026): the first version read as chrome (silver highlight, black body); gold-tinted
        // reflections in the shader, a lighter less-metallic body, a narrower softer worn lip, visible streaks and verdigris.
        public static Color RimPolish = new Color(.78f, .58f, .28f), RimBody = new Color(.34f, .24f, .11f), RimVerdigris = new Color(.20f, .30f, .24f);
        public static float RimPolishSmooth = .68f, RimBodySmooth = .45f, RimPolishStart = .72f, RimVerdigrisAmount = .6f;
        public static void RimPass()
        {
            var s = Open(); var rs = Renderers(s);
            var damp = AssetDatabase.LoadAssetAtPath<Material>(Gen + "/MenuDampGold.mat");
            var brass = Mat("MenuRimAgedBrass", Shader.Find("LastBell/MenuAgedBrass"), m =>
            {
                m.SetColor("_Polish", RimPolish); m.SetColor("_Body", RimBody); m.SetColor("_Verdigris", RimVerdigris);
                m.SetFloat("_PolishSmooth", RimPolishSmooth); m.SetFloat("_BodySmooth", RimBodySmooth); m.SetFloat("_PolishStart", RimPolishStart); m.SetFloat("_VerdigrisAmount", RimVerdigrisAmount);
            });
            int n = 0;
            foreach (var r in rs)
            {
                var mats = r.sharedMaterials; bool ch = false;
                for (int i = 0; i < mats.Length; i++) if (mats[i] && (mats[i] == damp || mats[i].name == "MenuDampGold")) { mats[i] = brass; ch = true; }
                if (ch) { r.sharedMaterials = mats; n++; Debug.Log($"Rim pass: {PathOf(r.transform)} bounds {r.bounds.size}"); }
            }
            if (n == 0 && rs.All(r => !r.sharedMaterials.Contains(brass))) throw new InvalidOperationException("Rim pass: no MenuDampGold renderers found");
            Save(s); Debug.Log($"Rim pass: aged brass on {n} renderers");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // ---------------- 3. lantern heads: brass cage, amber glass, flame core, soft halo ----------------
        // Refinement 1 (critic, October 26, 2026): frosted deeper-amber glass that barely emits, a visible slim flame core,
        // a taller narrower head that replaces the old cap, a stronger smaller halo, a 30% warmer light pool, lighter iron.
        public static float LanternGlass = .55f, LanternCore = 8f, LanternHalo = .7f, LanternHaloSize = 1.3f, LanternGlassEmit = .3f, LanternLight = 1.3f;
        static Mesh Box(Vector3 size, Vector3 centre)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube); var src = c.GetComponent<MeshFilter>().sharedMesh; UnityEngine.Object.DestroyImmediate(c);
            var v = src.vertices.Select(p => Vector3.Scale(p, size) + centre).ToArray();
            var m = new Mesh { vertices = v, normals = src.normals, uv = src.uv, triangles = src.triangles }; m.RecalculateBounds(); return m;
        }
        static Mesh Frustum(float rBottom, float rTop, float h, int sides, float y0)
        {
            var v = new System.Collections.Generic.List<Vector3>(); var t = new System.Collections.Generic.List<int>();
            for (int i = 0; i < sides; i++)
            {
                float a0 = (i + .5f) * Mathf.PI * 2 / sides, a1 = (i + 1.5f) * Mathf.PI * 2 / sides;
                Vector3 b0 = new Vector3(Mathf.Cos(a0) * rBottom, y0, Mathf.Sin(a0) * rBottom), b1 = new Vector3(Mathf.Cos(a1) * rBottom, y0, Mathf.Sin(a1) * rBottom);
                Vector3 t0 = new Vector3(Mathf.Cos(a0) * rTop, y0 + h, Mathf.Sin(a0) * rTop), t1 = new Vector3(Mathf.Cos(a1) * rTop, y0 + h, Mathf.Sin(a1) * rTop);
                int k = v.Count; v.AddRange(new[] { b0, b1, t1, t0 }); t.AddRange(new[] { k, k + 2, k + 1, k, k + 3, k + 2 });       // side
                k = v.Count; v.AddRange(new[] { new Vector3(0, y0 + h, 0), t0, t1 }); t.AddRange(new[] { k, k + 1, k + 2 });          // top cap
                k = v.Count; v.AddRange(new[] { new Vector3(0, y0, 0), b1, b0 }); t.AddRange(new[] { k, k + 1, k + 2 });              // bottom cap
            }
            var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }
        static Mesh Combine(params Mesh[] parts)
        {
            var ci = parts.Select(p => new CombineInstance { mesh = p, transform = Matrix4x4.identity }).ToArray();
            var m = new Mesh(); m.CombineMeshes(ci, true, true); m.RecalculateBounds(); foreach (var p in parts) UnityEngine.Object.DestroyImmediate(p); return m;
        }
        public static void LanternPass()
        {
            var s = Open(); var rs = Renderers(s);
            var glow = rs.Where(r => r.sharedMaterials.Any(m => m && m.name == "GlowLantern")).ToArray();
            if (glow.Length == 0) throw new InvalidOperationException("Lantern pass: no GlowLantern renderers");
            Rebuild("Menu lantern heads", out var root);
            var cam = Camera.main;
            // a unit head 1 m tall, 0.6 m wide, origin at the base plate; scaled per lantern to the tube it replaces
            var cage = MeshAsset("MenuLanternCage", Combine(
                Box(new Vector3(.62f, .07f, .62f), new Vector3(0, .035f, 0)),                                   // base plate
                Box(new Vector3(.5f, .05f, .5f), new Vector3(0, .1f, 0)),                                       // base step
                Box(new Vector3(.05f, .72f, .05f), new Vector3(.25f, .48f, .25f)), Box(new Vector3(.05f, .72f, .05f), new Vector3(-.25f, .48f, .25f)),
                Box(new Vector3(.05f, .72f, .05f), new Vector3(.25f, .48f, -.25f)), Box(new Vector3(.05f, .72f, .05f), new Vector3(-.25f, .48f, -.25f)),    // corner posts
                Box(new Vector3(.56f, .04f, .56f), new Vector3(0, .85f, 0)),                                   // top band
                Frustum(.40f, .08f, .2f, 4, .87f),                                                              // pyramid cap
                Box(new Vector3(.04f, .14f, .04f), new Vector3(0, 1.13f, 0)),                                  // finial stem
                Frustum(.06f, .0f, .08f, 6, 1.2f)));                                                            // finial point
            var glass = MeshAsset("MenuLanternGlass", Combine(
                Box(new Vector3(.46f, .66f, .012f), new Vector3(0, .48f, .225f)), Box(new Vector3(.46f, .66f, .012f), new Vector3(0, .48f, -.225f)),
                Box(new Vector3(.012f, .66f, .46f), new Vector3(.225f, .48f, 0)), Box(new Vector3(.012f, .66f, .46f), new Vector3(-.225f, .48f, 0))));
            var core = MeshAsset("MenuLanternCore", Combine(Frustum(.045f, .0f, .24f, 8, .42f), Frustum(.03f, .045f, .08f, 8, .34f)));   // slim flame
            var halo = MeshAsset("MenuLanternHalo", Quad(1, 1));
            var haloTex = Tex("MenuLanternHalo", 256, 256, (x, y) =>
            {
                float u = x / 255f * 2 - 1, v = y / 255f * 2 - 1, r = Mathf.Sqrt(u * u + v * v);
                float a = Mathf.Pow(Mathf.Clamp01(1 - r), 2.4f);                                                  // soft, no edge
                return new Color(1, .62f, .3f, a);
            });
            var brassM = Mat("MenuLanternBrass", Lit, m => { m.SetColor("_BaseColor", new Color(.30f, .21f, .10f)); m.SetFloat("_Metallic", .85f); m.SetFloat("_Smoothness", .55f); });
            var glassM = Mat("MenuLanternGlass", Lit, m =>
            {
                Transparent(m); m.SetColor("_BaseColor", new Color(1f, .58f, .26f, LanternGlass)); m.SetFloat("_Smoothness", .6f); m.SetFloat("_Metallic", 0);   // frosted
                m.EnableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; m.SetColor("_EmissionColor", new Color(1f, .42f, .12f) * LanternGlassEmit);   // the flame is the light; the glass only glows a little
                m.SetFloat("_Cull", 0);                                                                        // both sides: the far panes show through
            });
            var coreM = Mat("MenuLanternCore", Shader.Find("Universal Render Pipeline/Unlit"), m => m.SetColor("_BaseColor", new Color(1f, .62f, .28f) * LanternCore));
            var haloM = Mat("MenuLanternHalo", Shader.Find("Universal Render Pipeline/Particles/Unlit"), m =>
            {
                m.SetTexture("_BaseMap", haloTex); m.SetColor("_BaseColor", new Color(1f, .55f, .22f, LanternHalo));
                m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 2);                                             // additive
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.One); m.SetFloat("_ZWrite", 0);
                m.SetOverrideTag("RenderType", "Transparent"); m.renderQueue = 3100; m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.EnableKeyword("_BLENDMODE_ADD");
            });
            // Each dais lantern has two GlowLantern parts: a tall glowing post (~2.2 m) and a small glowing head box
            // (~0.35 x 0.5 m). The post becomes dark wrought iron (same mesh); the head box is replaced by a real head.
            var ironM = Mat("MenuLanternIron", Lit, m => { m.SetColor("_BaseColor", new Color(.09f, .08f, .10f)); m.SetFloat("_Metallic", .7f); m.SetFloat("_Smoothness", .6f); });
            foreach (var g in glow.Where(g => g.bounds.size.y > 1f))
            { var mats = g.sharedMaterials; for (int i = 0; i < mats.Length; i++) if (mats[i] && mats[i].name == "GlowLantern") mats[i] = ironM; g.sharedMaterials = mats; g.enabled = true; }
            int n = 0;
            foreach (var g in glow.Where(g => g.bounds.size.y <= 1f))
            {
                var b = g.bounds; if (b.size.y < .01f) continue;
                float h = b.size.y * 1.35f, w = Mathf.Max(b.size.x, b.size.z) * 1.05f;          // taller than wide: a bell-style lantern head
                // hide the old head's own frame and cap (small parts of the same lantern around the glowing box) so they don't
                // show through or double the new cap; the post and anything large stay
                var zone = new Bounds(b.center + Vector3.up * b.size.y * .25f, new Vector3(b.size.x + .3f, b.size.y * 1.8f, b.size.z + .3f));
                foreach (var part in g.transform.parent.GetComponentsInChildren<Renderer>(true))
                    if (part != g && part.bounds.size.y < .8f && zone.Contains(part.bounds.center) && !part.sharedMaterials.Any(m => m && m.name == "MenuLanternIron")) part.enabled = false;
                foreach (var l in g.transform.parent.GetComponentsInChildren<Light>(true)) if (l.type == LightType.Point) l.intensity = (l.name == "Lantern Light" ? 2.4f : 2.7f) * LanternLight;
                var head = new GameObject("Lantern head " + n).transform; head.SetParent(root, false);
                head.position = new Vector3(b.center.x, b.min.y - b.size.y * .12f, b.center.z);
                head.localScale = new Vector3(w / .62f, h, w / .62f);
                head.rotation = Quaternion.Euler(0, Mathf.Atan2(cam.transform.position.x - b.center.x, cam.transform.position.z - b.center.z) * Mathf.Rad2Deg + 45, 0);   // a corner toward the camera
                void Part(string name, Mesh mesh, Material mat, bool shadow)
                {
                    var go = new GameObject(name); go.transform.SetParent(head, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
                    r.shadowCastingMode = shadow ? ShadowCastingMode.On : ShadowCastingMode.Off; r.lightProbeUsage = LightProbeUsage.Off;
                }
                Part("Cage", cage, brassM, true); Part("Glass", glass, glassM, false); Part("Flame", core, coreM, false);
                var hq = Flat(root, "Lantern halo " + n, halo, haloM, head.position + Vector3.up * h * .48f);
                hq.transform.rotation = Quaternion.LookRotation(hq.transform.position - cam.transform.position) * Quaternion.Euler(-90, 0, 0);   // face the (fixed) menu camera
                hq.transform.localScale = Vector3.one * h * LanternHaloSize;
                g.enabled = false;                                                                          // the old tube stays, hidden
                n++;
            }
            Save(s); Debug.Log($"Lantern pass: {n} lantern heads (glass {LanternGlass}, core {LanternCore}, halo {LanternHalo})");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // ---------------- 4. defined storm clouds in the sky ----------------
        // Refinement 1 (critic): sharper edges, more value contrast (body ~0.06, gap ~0.22, rim ~0.40 luminance), ~40% gaps.
        public static Color SkyCloudLit = new Color(.52f, .38f, .70f), SkyCloudDark = new Color(.045f, .032f, .09f);
        public static float SkyCoverage = .56f, SkySharp = .04f, SkyZenithCalm = .65f;
        public static void SkyPass()
        {
            var s = Open();
            var sky = AssetDatabase.LoadAssetAtPath<Material>(Gen + "/StormSky.mat"); if (!sky) throw new FileNotFoundException("StormSky.mat");
            sky.SetFloat("_Defined", 1); sky.SetColor("_CloudLit", SkyCloudLit); sky.SetColor("_CloudDark", SkyCloudDark);
            sky.SetFloat("_Coverage", SkyCoverage); sky.SetFloat("_Sharp", SkySharp); sky.SetFloat("_ZenithCalm", SkyZenithCalm);
            EditorUtility.SetDirty(sky); RenderSettings.skybox = sky; Save(s);
            Debug.Log($"Sky pass: defined clouds on (coverage {SkyCoverage}, sharp {SkySharp}, zenith calm {SkyZenithCalm})");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // colour of a glTF or URP material (glTF imports use baseColorFactor)
        static Color BaseColor(Material m) => !m ? Color.clear : m.HasProperty("baseColorFactor") ? m.GetColor("baseColorFactor") : m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.clear;
        static bool Near(Color a, Color b, float tol = .04f) => Mathf.Abs(a.r - b.r) < tol && Mathf.Abs(a.g - b.g) < tol && Mathf.Abs(a.b - b.b) < tol;

        // ---------------- 6. mauve merlons -> carved, damp masonry parapet ----------------
        // Refinement 1 (critic, October 26, 2026): caps were pink (sky reflection) and drew a bright line at the boss's
        // ankles, detail sat on tops the low camera can't see, the brass band was invisible. Now: dark non-reflective
        // caps with a real overhang, a shadow line under each cap (brass on posts), a plinth, a damp darker band, slightly
        // lighter chamfered post edges and block seams, and an untinted stone body.
        // Set while another pass calls MerlonPass, so batch mode does not exit before the caller finishes.
        public static bool Chained;
        public static Color MerlonStone = new Color(.17f, .16f, .22f), MerlonCap = new Color(.13f, .12f, .17f);
        public static void MerlonPass()
        {
            var s = Open(); var rs = Renderers(s).Where(r => PathOf(r.transform).StartsWith("Bellkeeper's Dais/Platform/")).ToArray();
            var low = new Color(.54f, .49f, .65f); var post = new Color(.42f, .37f, .55f);
            var stoneSrc = AssetDatabase.LoadAssetAtPath<Material>(Gen + "/MenuMasonry.mat");
            void Matte(Material m, Color c, float smooth) { if (stoneSrc) m.CopyPropertiesFromMaterial(stoneSrc); m.SetTexture("_BaseMap", null); m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0); m.SetFloat("_EnvironmentReflections", 0); m.SetFloat("_SpecularHighlights", 0); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); }
            var sh = stoneSrc ? stoneSrc.shader : Lit;
            var stone = Mat("MenuParapetStone", sh, m => Matte(m, MerlonStone, .2f));
            var capM = Mat("MenuParapetCap", sh, m => Matte(m, MerlonCap, .15f));
            var shadowM = Mat("MenuParapetShadow", Lit, m => { m.SetColor("_BaseColor", new Color(.03f, .028f, .04f)); m.SetFloat("_Smoothness", .1f); });
            var dampM = Mat("MenuParapetDamp", Lit, m => { m.SetColor("_BaseColor", MerlonStone * .6f); m.SetFloat("_Smoothness", .5f); });
            var edgeM = Mat("MenuParapetEdge", Lit, m => { m.SetColor("_BaseColor", MerlonStone * 1.25f); m.SetFloat("_Smoothness", .3f); });
            var bandM = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/MenuLanternBrass.mat") ?? Mat("MenuLanternBrass", Lit, m => { m.SetColor("_BaseColor", new Color(.30f, .21f, .10f)); m.SetFloat("_Metallic", .85f); m.SetFloat("_Smoothness", .55f); });
            Rebuild("Menu parapet caps", out var root);
            var unit = MeshAsset("MenuParapetBox", Box(Vector3.one, new Vector3(0, .5f, 0)));            // 1 m cube, origin at its base
            var capMesh = MeshAsset("MenuParapetCapMesh", Combine(Box(new Vector3(1, .8f, 1), new Vector3(0, .4f, 0)), Frustum(.707f, .6f, .2f, 4, .8f)));   // slab + low chamfer, 1 m tall unit
            void Piece(string name, Mesh mesh, Material mat, Vector3 basePos, Vector3 size, float yaw = 0)
            {
                var go = new GameObject(name); go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(basePos, Quaternion.Euler(0, yaw, 0)); go.transform.localScale = size;
                go.AddComponent<MeshFilter>().sharedMesh = mesh; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off;
            }
            int swapped = 0, caps = 0;
            foreach (var r in rs)
            {
                var mats = r.sharedMaterials; bool isLow = false, isPost = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var c = BaseColor(mats[i]);
                    if (Near(c, low) || (mats[i] && mats[i].name == "MenuParapetStone" && r.bounds.size.y < 1)) { mats[i] = stone; isLow = true; }
                    else if (Near(c, post) || (mats[i] && mats[i].name == "MenuParapetStone" && r.bounds.size.y >= 1)) { mats[i] = stone; isPost = true; }
                }
                if (!isLow && !isPost) continue;
                r.sharedMaterials = mats; swapped++;
                var b = r.bounds; float sx = b.size.x, sz = b.size.z, top = b.max.y, bot = b.min.y; var c0 = new Vector3(b.center.x, 0, b.center.z);
                string id = (isPost ? "Post " : "Merlon ") + caps;
                float capH = isPost ? .18f : .14f;
                Piece(id + " cap", capMesh, capM, c0 + Vector3.up * (top - .01f), new Vector3(sx + .16f, capH, sz + .16f));                 // 8 cm overhang
                Piece(id + " shadow line", unit, isPost ? bandM : shadowM, c0 + Vector3.up * (top - .05f), new Vector3(sx + .03f, .04f, sz + .03f));
                Piece(id + " plinth", unit, stone, c0 + Vector3.up * bot, new Vector3(sx * 1.08f, .12f, sz * 1.08f));
                Piece(id + " damp band", unit, dampM, c0 + Vector3.up * (bot + .12f), new Vector3(sx + .02f, .25f, sz + .02f));
                if (isPost)
                {
                    float h = top - bot - .3f;
                    for (int k = 0; k < 4; k++)                                                              // chamfered vertical edges
                    {
                        float ex = (k % 2 == 0 ? -1 : 1) * sx / 2, ez = (k < 2 ? -1 : 1) * sz / 2;
                        Piece(id + " edge " + k, unit, edgeM, new Vector3(b.center.x + ex, bot + .12f, b.center.z + ez), new Vector3(.05f, h, .05f), 45);
                    }
                    foreach (var f in new[] { .42f, .72f })                                                  // block seams
                        Piece(id + " seam", unit, shadowM, c0 + Vector3.up * (bot + (top - bot) * f), new Vector3(sx + .012f, .012f, sz + .012f));
                }
                caps++;
            }
            if (swapped == 0) throw new InvalidOperationException("Merlon pass: no mauve blocks found");
            Save(s); Debug.Log($"Merlon pass: {swapped} blocks re-stoned, {caps} parapet details");
            if (Application.isBatchMode && !Chained) EditorApplication.Exit(0);
        }
        // ---------------- 7. green bushes -> wet rubble and a broken bell fragment ----------------
        // Refinement 1 (critic, October 26, 2026): shards were floating ~0.4 m up (tilt about a far pivot) and edge-on;
        // rubble was tiny and black. Now: bigger shards with a rolled lip and a jagged break, grounded by their bounds,
        // mouth-down with the convex face to the camera, 5 of them; rubble sized 0.35-0.7 m, piled 3-4 per spot, wetter.
        static Mesh BellShard(float radius, float height, float arcDeg, float thick, int seg, int seed)
        {
            var v = new System.Collections.Generic.List<Vector3>(); var t = new System.Collections.Generic.List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.Deg2Rad * (-arcDeg / 2 + arcDeg * i / seg);
                float jag = (i == 0 || i == seg) ? -.12f : (Hash(i, seed, 5) - .5f) * .16f;   // broken top edge
                float hTop = height + jag, rTop = radius * .62f, rBot = radius;
                Vector3 dirA = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                v.Add(dirA * rBot); v.Add(dirA * Mathf.Lerp(rBot, rTop, hTop / height) + Vector3.up * hTop);
                v.Add(dirA * (rBot - thick)); v.Add(dirA * (Mathf.Lerp(rBot, rTop, hTop / height) - thick) + Vector3.up * hTop);
            }
            for (int i = 0; i < seg; i++)
            {
                int k = i * 4, n = k + 4;
                t.AddRange(new[] { k, k + 1, n + 1, k, n + 1, n });
                t.AddRange(new[] { k + 2, n + 3, k + 3, k + 2, n + 2, n + 3 });
                t.AddRange(new[] { k + 1, k + 3, n + 3, k + 1, n + 3, n + 1 });
                t.AddRange(new[] { k, n, n + 2, k, n + 2, k + 2 });
            }
            var wall = new Mesh(); wall.SetVertices(v); wall.SetTriangles(t, 0); wall.RecalculateNormals();
            // rolled lip: a thicker band around the mouth
            var lv = new System.Collections.Generic.List<Vector3>(); var lt = new System.Collections.Generic.List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.Deg2Rad * (-arcDeg / 2 + arcDeg * i / seg); Vector3 dirA = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                lv.Add(dirA * (radius + .025f)); lv.Add(dirA * (radius + .02f) + Vector3.up * .06f); lv.Add(dirA * (radius - thick - .01f) + Vector3.up * .06f); lv.Add(dirA * (radius - thick - .01f));
            }
            for (int i = 0; i < seg; i++) { int k = i * 4, n = k + 4; lt.AddRange(new[] { k, k + 1, n + 1, k, n + 1, n, k + 1, k + 2, n + 2, k + 1, n + 2, n + 1, k + 2, k + 3, n + 3, k + 2, n + 3, n + 2 }); }
            var lip = new Mesh(); lip.SetVertices(lv); lip.SetTriangles(lt, 0); lip.RecalculateNormals();
            return Combine(wall, lip);
        }
        static readonly int[] ShardSpots = { 2, 5, 8, 11, 14 };   // 5 of the 18 bush spots: gaps between feet and posts
        public static void RubblePass()
        {
            var s = Open(); var all = s.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var bushes = all.Where(t => t.name.StartsWith("Bush") && PathOf(t).StartsWith("Bellkeeper's Dais/Platform/")).OrderBy(t => t.position.x).ToArray();
            if (bushes.Length == 0) throw new InvalidOperationException("Rubble pass: no bushes");
            var debris = all.FirstOrDefault(t => t.name == "Debris" && t.GetComponent<Renderer>());
            Debug.Log("Rubble pass: debris source " + (debris ? PathOf(debris) + " bounds " + debris.GetComponent<Renderer>().bounds.size + " scale " + debris.lossyScale : "NOT FOUND (chamfered blocks)"));
            Rebuild("Menu rubble", out var root);
            var wetStone = Mat("MenuRubbleStone", Lit, m => { m.SetColor("_BaseColor", new Color(.16f, .15f, .20f)); m.SetFloat("_Smoothness", .65f); m.SetFloat("_Metallic", 0); });
            var bronze = Mat("MenuBellBronze", Shader.Find("LastBell/MenuAgedBrass"), m => { m.SetColor("_Polish", new Color(.62f, .45f, .22f)); m.SetColor("_Body", new Color(.26f, .18f, .08f)); m.SetFloat("_PolishStart", .85f); m.SetFloat("_VerdigrisAmount", .2f); m.SetFloat("_BodySmooth", .5f); });
            var camPos = new Vector3(.6f, .75f, 10.9f);
            Mesh unitRock = debris ? debris.GetComponent<MeshFilter>()?.sharedMesh : null;
            float rockSize = unitRock ? Mathf.Max(unitRock.bounds.size.x, unitRock.bounds.size.z) : 1;
            var block = MeshAsset("MenuRubbleBlock", Combine(Box(new Vector3(1, .55f, .8f), new Vector3(0, .275f, 0)), Frustum(.55f, .4f, .12f, 5, .55f)));
            int i = 0, shards = 0;
            foreach (var bush in bushes)
            {
                var rend = bush.GetComponentsInChildren<Renderer>(true); if (rend.Length == 0) { i++; continue; }
                var b = rend[0].bounds; foreach (var r in rend) b.Encapsulate(r.bounds);
                foreach (var r in rend) r.enabled = false;
                float floorY = b.min.y;
                int chunks = 3 + (i % 2);
                for (int k = 0; k < chunks; k++)
                {
                    float size = .35f + Hash(i, k, 3) * .35f;                                     // 0.35-0.7 m across
                    float sx = (Hash(i, k, 1) - .5f) * b.size.x * .8f, sz = (Hash(i, k, 2) - .5f) * b.size.z * .6f - .1f;
                    var rock = new GameObject($"Rubble {i}.{k}"); rock.transform.SetParent(root, false);
                    rock.AddComponent<MeshFilter>().sharedMesh = unitRock ? unitRock : block;
                    var mr = rock.AddComponent<MeshRenderer>(); mr.sharedMaterial = wetStone; mr.shadowCastingMode = ShadowCastingMode.Off;
                    rock.transform.rotation = Quaternion.Euler(Hash(i, k, 4) * 30 - 15, Hash(i, k, 5) * 360, Hash(i, k, 6) * 30 - 15);   // leaning
                    float sc = size / rockSize; rock.transform.localScale = new Vector3(sc, sc * (.6f + Hash(i, k, 7) * .35f), sc);   // flatter than wide
                    rock.transform.position = new Vector3(b.center.x + sx, floorY, b.center.z + sz);
                    var rb = mr.bounds; rock.transform.position += Vector3.up * (floorY - rb.min.y - .03f);   // sit on the stone, slightly sunk
                }
                if (Array.IndexOf(ShardSpots, i) >= 0)
                {
                    var go = new GameObject("Bell fragment " + shards); go.transform.SetParent(root, false);
                    go.AddComponent<MeshFilter>().sharedMesh = MeshAsset("MenuBellShard" + shards, BellShard(.85f, .55f, 100, .07f, 14, shards));
                    var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = bronze; mr.shadowCastingMode = ShadowCastingMode.Off;
                    var toCam = Vector3.ProjectOnPlane(camPos - b.center, Vector3.up).normalized;
                    // mouth-down on its side: the shell's axis tips over toward the camera so the convex face shows
                    go.transform.rotation = Quaternion.LookRotation(-toCam, Vector3.up) * Quaternion.Euler(-100, 0, 0);
                    go.transform.position = new Vector3(b.center.x + (shards % 2 == 0 ? .25f : -.25f), floorY, b.center.z);
                    var sb = mr.bounds; go.transform.position += Vector3.up * (floorY - sb.min.y + .01f);          // grounded
                    shards++;
                }
                i++;
            }
            Save(s); Debug.Log($"Rubble pass: {bushes.Length} bushes hidden, rubble piles + {shards} bell fragments");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        // ---------------- 8. cloud cards -> soft, continuous cloud sea ----------------
        public static Color CloudSeaColor = new Color(.30f, .23f, .42f, .2f), WispColor = new Color(.34f, .28f, .48f, .4f); public static float WispLift = .1f;
        public static int WispCount = 0;   // refinement 1 tried 10 parapet wisps: they did not read against the same-hue sky at menu distance, so none are placed
        public static float CloudSeaScale = 1.6f;
        public static void CloudSeaPass()
        {
            var s = Open(); var all = s.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var sea = all.FirstOrDefault(t => t.name == "Sea of Clouds" && !t.parent); if (!sea) throw new InvalidOperationException("Cloud sea: not found");
            var puff = Tex("MenuCloudPuff", 512, 512, (x, y) =>
            {
                float u = x / 511f * 2 - 1, v = y / 511f * 2 - 1, r = Mathf.Sqrt(u * u + v * v);
                float n = Fbm(u * 2.2f + 5, v * 2.2f + 5, 71, 6);
                float falloff = Mathf.Clamp01(1 - r * r * 1.15f);                            // feathered to zero well before the card edge
                float a = Mathf.Clamp01((n - .32f) * 2.2f) * falloff * falloff;
                float lit = .78f + .22f * Mathf.Clamp01(v * .6f + n * .6f);                 // brighter on top
                return new Color(lit, lit, lit, a);
            }, false, TextureWrapMode.Clamp);
            var mat = Mat("MenuCloudSea", Shader.Find("Universal Render Pipeline/Particles/Unlit"), m =>
            {
                m.SetTexture("_BaseMap", puff); m.SetColor("_BaseColor", CloudSeaColor);
                m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetFloat("_ZWrite", 0); m.SetFloat("_Cull", 0);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetOverrideTag("RenderType", "Transparent"); m.renderQueue = 2990; m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetFloat("_SoftParticlesEnabled", 1); m.SetVector("_SoftParticleFadeParams", new Vector4(0, 4f, 0, 0)); m.EnableKeyword("_SOFTPARTICLES_ON");
                m.SetFloat("_CameraFadingEnabled", 1); m.SetVector("_CameraFadeParams", new Vector4(8f, 1f / 30f, 0, 0)); m.EnableKeyword("_FADING_ON");
            });
            int n2 = 0;
            foreach (Transform card in sea)
            {
                var r = card.GetComponent<Renderer>(); if (!r) continue;
                r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                if (!card.name.EndsWith(" (refined)")) { card.localScale *= CloudSeaScale; card.name += " (refined)"; }
                n2++;
            }
            // Refinement 1 (critic): the far sea is out of view and its tops only lifted the parapet base into a flat film.
            // Now: a fainter far sea, plus low wide wisps just behind the visible parapet so their crests roll over the coping.
            var wispM = Mat("MenuCloudWisp", mat.shader, m =>
            {
                m.CopyPropertiesFromMaterial(mat); m.SetColor("_BaseColor", WispColor); m.renderQueue = 2991;
                m.SetVector("_SoftParticleFadeParams", new Vector4(0, 1.5f, 0, 0)); m.SetVector("_CameraFadeParams", new Vector4(4f, 1f / 8f, 0, 0));
            });
            var caps = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(t => t.name.StartsWith("Merlon ") && t.name.EndsWith(" cap") && t.parent && t.parent.name == "Menu parapet caps").ToArray();
            Rebuild("Menu parapet mist", out var mistRoot);
            int wisps = 0;
            if (caps.Length > 0 && WispCount > 0)
            {
                var camPos = new Vector3(.6f, .75f, 10.9f); var camGo = new GameObject("tmp wisp camera"); var vc = camGo.AddComponent<Camera>();
                vc.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(new Vector3(-.6f, 2.2f, -7) - camPos)); vc.fieldOfView = 42; vc.aspect = 2560f / 1080f;
                var center = caps.Aggregate(Vector3.zero, (a, t) => a + t.position) / caps.Length; center.y = 0;
                var inView = caps.Select(t => (t, vp: vc.WorldToViewportPoint(t.position))).Where(q => q.vp.z > 0 && q.vp.x > .02f && q.vp.x < .6f && q.vp.y > 0 && q.vp.y < 1)
                                 .OrderBy(q => q.vp.x).Select(q => q.t).ToArray();
                UnityEngine.Object.DestroyImmediate(camGo);
                var wispMesh = MeshAsset("MenuCloudWispQuad", Quad(1, 1));
                int step = Mathf.Max(1, inView.Length / WispCount);
                for (int i = 0; i < inView.Length && wisps < WispCount; i += step)
                {
                    var t = inView[i]; var outDir = t.position - center; outDir.y = 0; outDir.Normalize();
                    float jit = Hash(i, 3, 91);
                    var pos = t.position + outDir * .7f + Vector3.up * (WispLift + (jit - .5f) * .2f);
                    var toCam = camPos - pos; toCam.y = 0;
                    var go = new GameObject("Wisp " + wisps); go.transform.SetParent(mistRoot, false);
                    go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(Vector3.up, toCam.normalized));
                    go.transform.localScale = new Vector3(2.4f * (.85f + .35f * jit), 1, 1.2f);          // quad lies in x/z: z becomes height
                    go.AddComponent<MeshFilter>().sharedMesh = wispMesh; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = wispM;
                    mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; wisps++;
                }
            }
            Save(s); Debug.Log($"Cloud sea pass: {n2} cards, soft puff texture, scale x{CloudSeaScale}; {wisps} parapet wisps from {caps.Length} merlon caps");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // ---------------- 9. banners -> cloth with folds, heavy hem, frayed edge ----------------
        public static float BannerFoldDepth = .035f, BannerHem = .03f, BannerHemRoll = .015f, BannerSag = .02f, BannerPin = .12f, BannerLean = 1.5f, BannerSmooth = .12f;
        public static int BannerFolds = 3, BannerSubdiv = 2;
        public static void BannerPass()
        {
            var s = Open(); var all = s.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var cloths = all.Where(t => t.name == "BannerCloth" && t.GetComponent<MeshFilter>()).ToArray();
            if (cloths.Length == 0) throw new InvalidOperationException("Banner pass: no BannerCloth");
            int done = 0;
            foreach (var c in cloths)
            {
                var mf = c.GetComponent<MeshFilter>(); var src = mf.sharedMesh;
                // always rebuild from the authored flat cloth: saved once as an asset, reloaded on later runs
                var origPath = Dir + "/MenuBannerOriginal" + done + ".asset";
                if (src && !src.name.StartsWith("MenuBannerCloth")) { EnsureDir(); AssetDatabase.DeleteAsset(origPath); var copy = UnityEngine.Object.Instantiate(src); copy.name = "MenuBannerOriginal" + done; AssetDatabase.CreateAsset(copy, origPath); }
                src = AssetDatabase.LoadAssetAtPath<Mesh>(origPath); if (!src) throw new FileNotFoundException(origPath);
                var b = src.bounds; var sz = b.size;
                int thin = sz.x < sz.y && sz.x < sz.z ? 0 : sz.y < sz.z ? 1 : 2;                  // the flat axis is the cloth normal
                int down = thin == 1 ? 2 : 1;                                                     // hang direction (usually local y)
                int across = 3 - thin - down;
                float Get(Vector3 p, int ax) => ax == 0 ? p.x : ax == 1 ? p.y : p.z;
                Vector3 Set(Vector3 p, int ax, float val) { if (ax == 0) p.x = val; else if (ax == 1) p.y = val; else p.z = val; return p; }
                // keep the authored outline (pointed hem) and UVs (emblem): midpoint-subdivide the original triangles, then only displace
                var v = new System.Collections.Generic.List<Vector3>(src.vertices); var uv = new System.Collections.Generic.List<Vector2>(src.uv);
                var tri = new System.Collections.Generic.List<int>(src.triangles);
                for (int level = 0; level < BannerSubdiv; level++)
                {
                    var mid = new System.Collections.Generic.Dictionary<long, int>(); var nt = new System.Collections.Generic.List<int>(tri.Count * 4);
                    int Mid(int a, int e) { long key = a < e ? (long)a << 32 | (uint)e : (long)e << 32 | (uint)a; if (mid.TryGetValue(key, out var i)) return i; i = v.Count; v.Add((v[a] + v[e]) * .5f); uv.Add((uv[a] + uv[e]) * .5f); mid[key] = i; return i; }
                    for (int t = 0; t < tri.Count; t += 3) { int a = tri[t], e = tri[t + 1], d = tri[t + 2], ae = Mid(a, e), ed = Mid(e, d), da = Mid(d, a); nt.AddRange(new[] { a, ae, da, ae, e, ed, da, ed, d, ae, ed, da }); }
                    tri = nt;
                }
                // the hem line under each column, from the authored outline (lowest point per across-bin)
                const int Bins = 32; var hemAt = new float[Bins + 1]; for (int i = 0; i <= Bins; i++) hemAt[i] = float.MaxValue;
                float Fx(Vector3 p) => Mathf.Clamp01((Get(p, across) - Get(b.min, across)) / Mathf.Max(1e-4f, Get(sz, across)));
                foreach (var p in v) { int i = Mathf.RoundToInt(Fx(p) * Bins); hemAt[i] = Mathf.Min(hemAt[i], Get(p, down)); }
                for (int i = 0; i <= Bins; i++) if (hemAt[i] == float.MaxValue) hemAt[i] = i > 0 ? hemAt[i - 1] : Get(b.min, down);
                float H = Get(sz, down), top = Get(b.max, down);
                for (int i = 0; i < v.Count; i++)
                {
                    var p = v[i]; float fx = Fx(p), fy = Mathf.Clamp01((top - Get(p, down)) / H);   // fy 0 = hanging point, 1 = lowest
                    float hemLine = hemAt[Mathf.RoundToInt(fx * Bins)], toHem = (Get(p, down) - hemLine) / H;   // 0 on the hem edge
                    // folds: pinned flat at the bar, deepening to the hem, each leaning a little so they are not machine-regular
                    float lean = Mathf.Tan(BannerLean * Mathf.Deg2Rad) * (done % 2 == 0 ? 1 : -1) * fy * H / Mathf.Max(1e-4f, Get(sz, across));
                    float phase = (fx + lean) * Mathf.PI * BannerFolds * 2 + .6f + done * 1.3f;
                    float fold = Mathf.Sin(phase) * BannerFoldDepth * SS(0, BannerPin, fy) * (.4f + .6f * fy);
                    float hem = toHem < BannerHem ? Mathf.Sin((1 - toHem / BannerHem) * Mathf.PI * .5f) * BannerHemRoll : 0;   // rolled bead along the authored outline
                    float sag = BannerSag * SS(.7f, 1, fy) * SS(.25f, 0, Mathf.Abs(fx - .5f));                                 // the wet point hangs heavier
                    p = Set(p, thin, Get(p, thin) + fold + hem);
                    p = Set(p, down, Get(p, down) - sag);
                    v[i] = p;
                }
                var m = new Mesh(); m.indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
                mf.sharedMesh = MeshAsset("MenuBannerCloth" + done, m);
                // menu-only material copy: double-sided, matte (critic: wet-glossy crests read as metal ribs)
                var rr = c.GetComponent<Renderer>(); var mats = rr.sharedMaterials;
                for (int k = 0; k < mats.Length; k++)
                {
                    var mat = mats[k]; if (!mat) continue; var path = AssetDatabase.GetAssetPath(mat);
                    if (!path.StartsWith(Dir)) { var baseName = "MenuBanner_" + mat.name.Replace(" (refined)", ""); Debug.Log($"Banner pass: {c.parent?.name}/{c.name} material '{mat.name}' at '{path}' -> {baseName}"); var src2 = mat; mat = Mat(baseName, src2.shader, q => q.CopyPropertiesFromMaterial(src2)); }
                    if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0);
                    if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", BannerSmooth);
                    EditorUtility.SetDirty(mat); mats[k] = mat;
                }
                rr.sharedMaterials = mats;
                done++;
            }
            Save(s); Debug.Log($"Banner pass: {done} banners rebuilt with {BannerFolds} folds, depth {BannerFoldDepth}");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // ---------------- inspect: where everything is ----------------
        public static void Inspect()
        {
            var s = Open(); var rs = Renderers(s);
            foreach (var key in new[] { "WetDial", "WetPaving", "MenuWetStone", "MenuDampGold", "GlowLantern" })
                foreach (var r in rs.Where(r => r.sharedMaterials.Any(m => m && m.name == key)))
                    Debug.Log($"INSPECT {key}: {PathOf(r.transform)} active={r.gameObject.activeInHierarchy} bounds {r.bounds.center} size {r.bounds.size}");
            var cam = Camera.main; Debug.Log($"INSPECT camera {cam.transform.position} fwd {cam.transform.forward} fov {cam.fieldOfView} near {cam.nearClipPlane}");
            foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Debug.Log($"INSPECT light {PathOf(l.transform)} {l.type} {l.transform.position} int {l.intensity} range {l.range} col {l.color}");
            foreach (var p in UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None)) Debug.Log($"INSPECT probe {p.name} {p.transform.position} size {p.size} int {p.intensity}");
            // survey: every visible renderer in front of the camera within 45 m, with its material and base colour,
            // to find the mauve blocks, green rocks, cloud cards and banner
            var cam2 = Camera.main; var planes = GeometryUtility.CalculateFrustumPlanes(cam2.projectionMatrix * cam2.worldToCameraMatrix);
            foreach (var r in rs.Where(r => r.gameObject.activeInHierarchy && r.enabled && GeometryUtility.TestPlanesAABB(planes, r.bounds) && Vector3.Distance(cam2.transform.position, r.bounds.center) < 45))
            {
                var m = r.sharedMaterial; Color col = m ? (m.HasProperty("baseColorFactor") ? m.GetColor("baseColorFactor") : m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.clear) : Color.clear;
                var mf = r.GetComponent<MeshFilter>();
                Debug.Log($"SURVEY {PathOf(r.transform)} | mat {(m ? m.name : "-")} col ({col.r:0.00},{col.g:0.00},{col.b:0.00}) | mesh {(mf && mf.sharedMesh ? mf.sharedMesh.name : "-")} | c {r.bounds.center} s {r.bounds.size}");
            }
            var dial = AssetDatabase.LoadAssetAtPath<Material>(Gen + "/WetDial.mat"); if (dial) Debug.Log($"INSPECT WetDial shader {dial.shader.name}");
            var gold = AssetDatabase.LoadAssetAtPath<Material>(Gen + "/MenuDampGold.mat"); if (gold) Debug.Log($"INSPECT MenuDampGold shader {gold.shader.name}");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
