using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LastBell.Areas.Area3.EditorTools
{
    // Last Bell > Area 3 > Open / Rebuild Verdant Chimes: builds (once) and opens the Area 3 arena scene.
    // The solid arena comes from Source/VerdantChimes.glb (exported from prototype/arena-3d/areas/area-3 by export-glb.mjs);
    // this adds what glTF can't carry: LB CustomLit materials (Windblown teardown §1) driven by a LookSettings asset, the
    // 4K floor (albedo, normal from height, mask), water, mist, glow billboards, particles, outlines, sky, lights, post,
    // colliders and the camera. Spec: prototype/arena-3d/areas/area-3/VERDANT-CHIMES-UNITY-PROMPT.md.
    public static class VerdantChimesBuilder
    {
        const string Dir = "Assets/LastBell/Areas/Area3", Gen = Dir + "/Generated", Src = Dir + "/Source";
        const string GlbPath = Src + "/VerdantChimes.glb", ScenePath = Dir + "/VerdantChimes_Arena.unity", LookPath = Dir + "/Area3_VerdantChimes_Look.asset";
        const string PrefabPath = Dir + "/LB_ENV_VerdantChimes_Arena.prefab";
        const float R = 14.6f, PlayR = 14f;

        [MenuItem("Last Bell/Area 3/Open Verdant Chimes", priority = 30)]
        static void Open()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) Build();
            EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Last Bell/Area 3/Rebuild Verdant Chimes", priority = 31)]
        static void Rebuild()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(); EditorSceneManager.OpenScene(ScenePath);
        }

        static Color Hex(uint rgb, float k = 1) => new Color(((rgb >> 16) & 255) / 255f * k, ((rgb >> 8) & 255) / 255f * k, (rgb & 255) / 255f * k);

        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(Gen)) AssetDatabase.CreateFolder(Dir, "Generated");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Look asset: created once, then left alone so hand tuning survives rebuilds.
            var look = AssetDatabase.LoadAssetAtPath<LookSettings>(LookPath);
            if (!look) { look = ScriptableObject.CreateInstance<LookSettings>(); AssetDatabase.CreateAsset(look, LookPath); }

            // Sky, ambient (the CustomLit shader takes ambient from LookSettings; this keeps other shaders consistent).
            RenderSettings.skybox = SaveAsset(new Material(Shader.Find("LastBell/Area3/Sky")), "Sky.mat");
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex(0x9cc8c4, 0.8f); RenderSettings.ambientEquatorColor = Hex(0x4a7470, 0.7f); RenderSettings.ambientGroundColor = Hex(0x1b2624);

            var sunGo = new GameObject("Sun"); var sun = sunGo.AddComponent<Light>(); sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.9f; RenderSettings.sun = sun;
            var applier = new GameObject("Look Settings").AddComponent<LookSettingsApplier>(); applier.settings = look; applier.sun = sun; applier.Apply();

            // The arena: the glb (three.js space) under a root turned 180 degrees, so north is +Z and the entry bridge -Z.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath);
            if (!prefab) throw new FileNotFoundException("Run prototype/arena-3d/areas/area-3/export-glb.mjs first", GlbPath);
            var root = new GameObject("LB_ENV_VerdantChimes_Arena");
            var arena = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform); arena.name = "VerdantChimes (glb)";
            root.transform.rotation = Quaternion.Euler(0, 180, 0);
            DressMaterials(arena);
            AddGlows(root.transform);
            AddLights(root.transform);
            var fx = AddParticles(root.transform);
            AddColliders(root.transform);
            MarkStatic(root);

            // Post: ACES, gentle bloom, light teal/warm split tone, soft vignette, fine grain (teardown §7: post only finishes).
            var volume = new GameObject("Post").AddComponent<Volume>(); volume.isGlobal = true; volume.sharedProfile = BuildProfile();

            // Camera with the outline overlay (teardown §3) as a child.
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>(); cam.nearClipPlane = 0.3f; cam.farClipPlane = 900; cam.clearFlags = CameraClearFlags.Skybox; cam.allowHDR = true;
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true; camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; camData.requiresDepthOption = CameraOverrideOption.On;
            camGo.AddComponent<AudioListener>();
            var outline = new GameObject("Outline Overlay"); outline.transform.SetParent(camGo.transform, false);
            outline.AddComponent<MeshFilter>().sharedMesh = OutlineQuad();
            var omr = outline.AddComponent<MeshRenderer>(); omr.sharedMaterial = SaveAsset(new Material(Shader.Find("LastBell/Area3/OutlineOverlay")), "OutlineOverlay.mat");
            omr.shadowCastingMode = ShadowCastingMode.Off; omr.receiveShadows = false;

            var driver = new GameObject("Verdant Chimes Arena").AddComponent<VerdantChimesArena>();
            fx.SetParent(driver.transform, true);
            var so = new SerializedObject(driver);
            so.FindProperty("arenaRoot").objectReferenceValue = root.transform;
            so.FindProperty("viewCamera").objectReferenceValue = cam;
            so.ApplyModifiedPropertiesWithoutUndo();
            driver.SetView(VerdantChimesArena.View.Gameplay);

            AssetDatabase.DeleteAsset(PrefabPath);
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, InteractionMode.AutomatedAction);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        // ---- materials: glb stand-ins are remapped by name to LB CustomLit / water / mist / glow ----
        // glTFast's own material graphs may not compile in this project, so base colours are read from the glb's JSON
        // (pbrMetallicRoughness.baseColorFactor, linear) and the one embedded texture (the drum bricks) from the import.
        static Dictionary<string, Color> GlbColors()
        {
            var bytes = File.ReadAllBytes(GlbPath); int len = System.BitConverter.ToInt32(bytes, 12);
            string json = System.Text.Encoding.UTF8.GetString(bytes, 20, len), key = "\"materials\":[";
            var result = new Dictionary<string, Color>(); int i = json.IndexOf(key); if (i < 0) return result; i += key.Length;
            int depth = 0, start = -1;
            for (; i < json.Length; i++)
            {
                char ch = json[i];
                if (ch == '{') { if (depth++ == 0) start = i; }
                else if (ch == '}') { if (--depth == 0) { var obj = json.Substring(start, i - start + 1);
                    var n = System.Text.RegularExpressions.Regex.Match(obj, "\"name\":\"([^\"]+)\""); var f = System.Text.RegularExpressions.Regex.Match(obj, @"""baseColorFactor"":\[([^\]]+)\]");
                    if (n.Success && !result.ContainsKey(n.Groups[1].Value)) { var c = Color.white;
                        if (f.Success) { var v = f.Groups[1].Value.Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray(); c = new Color(v[0], v[1], v[2], 1); }
                        result[n.Groups[1].Value] = c; } } }
                else if (ch == ']' && depth == 0) break;
            }
            return result;
        }

        static void DressMaterials(GameObject arena)
        {
            var lit = Shader.Find("LastBell/Area3/CustomLit");
            var colors = GlbColors(); var brick = AssetDatabase.LoadAllAssetsAtPath(GlbPath).OfType<Texture2D>().FirstOrDefault();
            var cache = new Dictionary<string, Material>();
            Material Lit(string name, Color c, bool vc, float metal = 0, Texture tex = null)
            {
                string key = name + (vc ? "_vc" : "");
                if (cache.TryGetValue(key, out var m)) return m;
                m = new Material(lit) { name = key }; m.SetColor("_BaseColor", c); m.SetFloat("_Metal", metal);
                if (tex) m.SetTexture("_BaseMap", tex);
                if (vc) { m.SetFloat("_UseVertexColor", 1); m.EnableKeyword("_LB_VERTEXCOLOR"); }
                return cache[key] = SaveAsset(m, key + ".mat");
            }
            Material Floor()
            {
                if (cache.TryGetValue("Floor", out var m)) return m;
                m = new Material(lit) { name = "Floor" }; m.SetColor("_BaseColor", Color.white);
                m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Src + "/FloorAlbedo.png"));
                m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Src + "/FloorHeight.png")); m.SetFloat("_BumpScale", 1f);
                m.SetFloat("_UseNormalMap", 1); m.EnableKeyword("_LB_NORMALMAP");
                m.SetTexture("_MaskMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Src + "/FloorMask.png"));
                m.SetFloat("_UseMask", 1); m.EnableKeyword("_LB_MASKMAP");
                m.SetColor("_GlowColor", new Color(0.25f, 0.95f, 0.85f) * 1.2f); m.SetFloat("_GlowStrength", 0.35f);
                return cache["Floor"] = SaveAsset(m, "Floor.mat");
            }
            Material Simple(string shader, string file) { if (!cache.TryGetValue(file, out var m)) cache[file] = m = SaveAsset(new Material(Shader.Find(shader)), file + ".mat"); return m; }
            Material Glow(string name, Color c)
            {
                if (cache.TryGetValue(name, out var m)) return m;
                m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = name }; m.SetColor("_BaseColor", c);
                return cache[name] = SaveAsset(m, name + ".mat");
            }
            foreach (var r in arena.GetComponentsInChildren<Renderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>(); bool hasVc = mf && mf.sharedMesh && mf.sharedMesh.colors32 != null && mf.sharedMesh.colors32.Length > 0;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i]; if (!src) continue;
                    string name = src.name.Replace(" (Instance)", "");
                    Color lin = colors.TryGetValue(name, out var cc) ? cc : Color.white, c = new Color(Mathf.LinearToGammaSpace(lin.r), Mathf.LinearToGammaSpace(lin.g), Mathf.LinearToGammaSpace(lin.b), 1);
                    bool transparentFx = false;
                    switch (name)
                    {
                        case "Floor": mats[i] = Floor(); break;
                        case "Brass": case "Bell": mats[i] = Lit(name, c, false, 1); break;
                        case "BrassDark": mats[i] = Lit(name, c, false, 0.7f); break;
                        case "LanternMetal": mats[i] = Lit(name, c, false, 0.6f); break;
                        case "Rock": case "Flower": mats[i] = Lit(name, Color.white, true); break;
                        case "Brick": mats[i] = Lit(name, Color.white, false, 0, brick); mats[i].SetTextureScale("_BaseMap", new Vector2(10, 1)); break;
                        case "GlowAmber": mats[i] = Glow(name, Hex(0xffc477) * 3.2f); break;
                        case "GlowTeal": mats[i] = Glow(name, Hex(0x6ff5df) * 2.6f); break;
                        case "Waterfall": mats[i] = Simple("LastBell/Area3/Waterfall", "Waterfall"); transparentFx = true; break;
                        case "Mist": mats[i] = Simple("LastBell/Area3/Mist", "Mist"); transparentFx = true; break;
                        case "Pool": mats[i] = Simple("LastBell/Area3/Pool", "Pool"); transparentFx = true; break;
                        case "Stone": case "StoneDark": case "Deck": mats[i] = Lit(name, c, false); mats[i].SetFloat("_StoneDetail", 1); mats[i].EnableKeyword("_LB_STONEDETAIL"); break;
                        default: mats[i] = Lit(name, c, hasVc && name != "Grass"); break;
                    }
                    if (transparentFx || name.StartsWith("Glow") || name == "Grass") { r.shadowCastingMode = ShadowCastingMode.Off; }
                    if (transparentFx) { r.receiveShadows = false; r.gameObject.layer = 1; } // TransparentFX
                }
                r.sharedMaterials = mats;
            }
        }

        // ---- fake lights: a glow billboard on every lantern (teardown §9) ----
        static void AddGlows(Transform root)
        {
            var m = new Material(Shader.Find("LastBell/Area3/GlowBillboard")); m.SetColor("_Color", Hex(0xffb868) * 1.6f); m.SetFloat("_Size", 1.9f);
            m = SaveAsset(m, "LanternGlow.mat");
            var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Lantern").ToArray())
            {
                var g = new GameObject("Glow"); g.transform.SetParent(t, false);
                g.AddComponent<MeshFilter>().sharedMesh = quad; var mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = m;
                mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            }
        }

        // ---- real lights: at most six (prompt §8): the four lanterns nearest the floor, plus a faint teal over the mosaic ----
        static void AddLights(Transform root)
        {
            var parent = new GameObject("Lights").transform; parent.SetParent(root.parent, false);
            var lanterns = root.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Lantern").OrderBy(t => t.position.magnitude).Take(4);
            foreach (var t in lanterns)
            {
                var l = new GameObject("Lantern Light").AddComponent<Light>(); l.type = LightType.Point; l.color = Hex(0xffb466); l.intensity = 2.2f; l.range = 7;
                l.transform.SetParent(parent, false); l.transform.position = t.position;
            }
            var c = new GameObject("Mosaic Light").AddComponent<Light>(); c.type = LightType.Point; c.color = Hex(0x58f0d8); c.intensity = 1.0f; c.range = 9;
            c.transform.SetParent(parent, false); c.transform.position = new Vector3(0, 1.2f, 0);
        }

        // ---- particles (shader-driven quads; the Particle System module is disabled in this project): spray at every
        // waterfall lip, turquoise motes over the rim, drifting white petals ----
        static Transform AddParticles(Transform root)
        {
            var parent = new GameObject("FX").transform;
            var rnd = new System.Random(9); float Rn() => (float)rnd.NextDouble();
            Material Mat(string file, float mode, bool additive)
            {
                var m = new Material(Shader.Find("LastBell/Area3/ParticleField")); m.SetFloat("_Mode", mode);
                m.SetFloat("_SrcBlend", additive ? (float)BlendMode.One : (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
                return SaveAsset(m, file + ".mat");
            }
            void Field(string name, Material mat, List<(Vector3 c, Vector3 dir, float size, Color col)> quads)
            {
                var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var uv2 = new List<Vector2>(); var col = new List<Color>(); var t = new List<int>();
                foreach (var q in quads)
                {
                    int k = v.Count; float seed = Rn();
                    foreach (var c in new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one }) { v.Add(q.c); n.Add(q.dir); uv.Add(c); uv2.Add(new Vector2(seed, q.size)); col.Add(q.col); }
                    t.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 });
                }
                var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetUVs(1, uv2); m.SetColors(col); m.SetTriangles(t, 0);
                m.bounds = new Bounds(Vector3.zero, Vector3.one * 400);
                AssetDatabase.DeleteAsset(Gen + "/" + name + ".asset"); AssetDatabase.CreateAsset(m, Gen + "/" + name + ".asset");
                var go = new GameObject(name); go.transform.SetParent(parent, false); go.AddComponent<MeshFilter>().sharedMesh = m;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            }
            var spray = new List<(Vector3, Vector3, float, Color)>();
            foreach (var lip in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == "FallLip"))
            {
                var outward = new Vector3(lip.position.x, 0, lip.position.z).normalized; var side = Vector3.Cross(Vector3.up, outward);
                for (int i = 0; i < 30; i++) spray.Add((lip.position + side * (Rn() - 0.5f) * 5f + Vector3.down * Rn() * 0.6f, (outward * (0.6f + Rn()) + side * (Rn() - 0.5f) * 0.6f), 1.6f + Rn() * 1.8f, new Color(0.9f, 0.98f, 0.96f, 0.45f)));
            }
            Field("Spray", Mat("SprayField", 2, false), spray);
            var motes = new List<(Vector3, Vector3, float, Color)>();
            for (int i = 0; i < 260; i++) { float a = Rn() * Mathf.PI * 2, r = 13.2f + Rn() * 12; motes.Add((new Vector3(Mathf.Cos(a) * r, 0.5f + Rn() * 6, Mathf.Sin(a) * r), Vector3.up, 0.12f + Rn() * 0.12f, new Color(0.55f, 1f, 0.9f, 0.9f))); }
            Field("Motes", Mat("MotesField", 0, true), motes);
            var petals = new List<(Vector3, Vector3, float, Color)>();
            for (int i = 0; i < 160; i++) petals.Add((new Vector3((Rn() - 0.5f) * 60, 12 + Rn() * 4, (Rn() - 0.5f) * 50), Vector3.up, 0.09f + Rn() * 0.06f, new Color(0.97f, 0.95f, 0.9f, 0.85f)));
            Field("Petals", Mat("PetalsField", 1, false), petals);
            return parent;
        }

        // ---- gameplay: flat floor collider, boundary ring at 14 m, a disabled entry gate; no colliders on decoration ----
        static void AddColliders(Transform root)
        {
            var parent = new GameObject("Gameplay Colliders").transform; parent.SetParent(root.parent, false);
            var floor = new GameObject("Floor Collider"); floor.transform.SetParent(parent, false);
            floor.AddComponent<MeshCollider>().sharedMesh = DiscMesh();
            const int n = 48; float seg = 2 * Mathf.PI * PlayR / n + 0.1f;
            var ring = new GameObject("Boundary").transform; ring.SetParent(parent, false);
            for (int i = 0; i < n; i++)
            {
                float a = (i + 0.5f) / n * Mathf.PI * 2; var b = new GameObject("Wall").AddComponent<BoxCollider>(); b.transform.SetParent(ring, false);
                b.transform.position = new Vector3(Mathf.Cos(a) * (PlayR + 0.25f), 1, Mathf.Sin(a) * (PlayR + 0.25f)); b.transform.rotation = Quaternion.Euler(0, -a * Mathf.Rad2Deg + 90, 0);
                b.size = new Vector3(seg, 2, 0.5f);
            }
            // The entry bridge opens to the south (-Z). Whether it closes during a fight is a design decision: off for now.
            var gate = new GameObject("EntryGate").AddComponent<BoxCollider>(); gate.transform.SetParent(parent, false);
            gate.transform.position = new Vector3(0, 1, -(R + 0.6f)); gate.size = new Vector3(4.6f, 2, 0.5f); gate.gameObject.SetActive(false);
        }

        static void MarkStatic(GameObject root)
        {
            var moving = new HashSet<Transform>(root.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Bell" || t.name == "MedallionGear").SelectMany(t => t.GetComponentsInChildren<Transform>(true)));
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (!moving.Contains(t) && t.GetComponent<Renderer>()) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
        }

        static Mesh DiscMesh()
        {
            string path = Gen + "/FloorCollider.asset"; var m = AssetDatabase.LoadAssetAtPath<Mesh>(path); if (m) return m;
            const int n = 96; var v = new Vector3[n + 1]; var t = new int[n * 3];
            for (int i = 0; i < n; i++) { float a = -i / (float)n * Mathf.PI * 2; v[i + 1] = new Vector3(Mathf.Cos(a) * R, 0, Mathf.Sin(a) * R); t[i * 3] = 0; t[i * 3 + 1] = i + 1; t[i * 3 + 2] = (i + 1) % n + 1; }
            m = new Mesh { name = "FloorCollider", vertices = v, triangles = t }; m.RecalculateNormals(); m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, path); return m;
        }

        static Mesh OutlineQuad()
        {
            string path = Gen + "/OutlineQuad.asset"; var m = AssetDatabase.LoadAssetAtPath<Mesh>(path); if (m) return m;
            m = new Mesh { name = "OutlineQuad", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, new Vector3(1, 1, 0) }, uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one }, triangles = new[] { 0, 2, 1, 1, 2, 3 } };
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 100000); // never culled: the shader places it in clip space
            AssetDatabase.CreateAsset(m, path); return m;
        }

        static VolumeProfile BuildProfile()
        {
            string path = Gen + "/VerdantPost.asset"; AssetDatabase.DeleteAsset(path);
            var p = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(p, path);
            var tone = p.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.ACES);
            var bloom = p.Add<Bloom>(true); bloom.threshold.Override(0.9f); bloom.intensity.Override(0.45f); bloom.scatter.Override(0.6f);
            var color = p.Add<ColorAdjustments>(true); color.postExposure.Override(0.05f); color.contrast.Override(8f); color.saturation.Override(-4f);
            var split = p.Add<SplitToning>(true); split.shadows.Override(new Color(0.40f, 0.58f, 0.56f)); split.highlights.Override(new Color(0.62f, 0.58f, 0.50f)); split.balance.Override(-10f);
            var vig = p.Add<Vignette>(true); vig.intensity.Override(0.24f); vig.smoothness.Override(0.45f); vig.color.Override(Hex(0x061a1c));
            var grain = p.Add<FilmGrain>(true); grain.type.Override(FilmGrainLookup.Thin1); grain.intensity.Override(0.12f); grain.response.Override(0.8f);
            foreach (var c in p.components) { c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy; AssetDatabase.AddObjectToAsset(c, p); }
            AssetDatabase.SaveAssets(); return p;
        }

        static T SaveAsset<T>(T asset, string file) where T : Object
        {
            string path = Gen + "/" + file; AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(asset, path); return asset;
        }

        // ---- Batch mode: build the scene and save review screenshots (gameplay, angle, top, and a readability test) ----
        // Unity.exe -batchmode -projectPath unity -executeMethod LastBell.Areas.Area3.EditorTools.VerdantChimesBuilder.BatchBuildAndCapture
        public static void BatchBuildAndCapture()
        {
            int code = 0;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                Build(); EditorSceneManager.OpenScene(ScenePath);
                string shots = Path.GetFullPath(Path.Combine(Application.dataPath, "LastBell/Areas/Area3/Screenshots")); Directory.CreateDirectory(shots);
                var driver = Object.FindFirstObjectByType<VerdantChimesArena>();
                Object.FindFirstObjectByType<LookSettingsApplier>().Apply();
                driver.Tick(1);
                driver.SetView(VerdantChimesArena.View.Gameplay); Capture(Path.Combine(shots, "unity-game.png")); Capture(Path.Combine(shots, "unity-game.png"));
                driver.SetView(VerdantChimesArena.View.Angle); Capture(Path.Combine(shots, "unity-angle.png"));
                driver.SetView(VerdantChimesArena.View.Top); Capture(Path.Combine(shots, "unity-top.png"));
                // readability: quiet on, a coral hostile telegraph, a teal friendly ring, eight player capsules
                var test = ReviewProps(); driver.quiet = true; driver.Tick(1);
                driver.SetView(VerdantChimesArena.View.Gameplay); Capture(Path.Combine(shots, "unity-readability.png"));
                Object.DestroyImmediate(test);
            }
            catch (System.Exception e) { Debug.LogException(e); code = 1; }
            EditorApplication.Exit(code);
        }

        static GameObject ReviewProps()
        {
            var root = new GameObject("Review Props");
            Material Flat(Color c)
            {
                var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); m.SetColor("_BaseColor", c);
                m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); m.SetFloat("_ZWrite", 0);
                m.SetOverrideTag("RenderType", "Transparent"); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = 3100; return m;
            }
            void Ring(Vector3 c, float r0, float r1, Color col)
            {
                const int n = 72; var v = new List<Vector3>(); var t = new List<int>();
                for (int i = 0; i <= n; i++) { float a = i / (float)n * Mathf.PI * 2; v.Add(new Vector3(Mathf.Cos(a) * r0, 0, Mathf.Sin(a) * r0)); v.Add(new Vector3(Mathf.Cos(a) * r1, 0, Mathf.Sin(a) * r1)); }
                for (int i = 0; i < n; i++) { int k = i * 2; t.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 }); } // clockwise seen from above
                var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals();
                var go = new GameObject("Ring"); go.transform.SetParent(root.transform); go.transform.position = c + Vector3.up * 0.04f;
                go.AddComponent<MeshFilter>().sharedMesh = m; go.AddComponent<MeshRenderer>().sharedMaterial = Flat(col);
            }
            var coral = new Color(1f, 0.35f, 0.24f, 0.55f); var rim = new Color(1f, 0.35f, 0.24f, 1f);              // hostile: #FF5A3C, 0.45 fill, solid 0.15 m edge
            var dark = new Color(0.1f, 0.05f, 0.04f, 0.8f);                                                         // thin dark outer edge on every telegraph
            Ring(new Vector3(7, 0, 2), 3.2f, 3.28f, dark); Ring(new Vector3(-2.5f, 0, -1), 2.4f, 2.48f, dark); Ring(new Vector3(2.5f, 0, -3), 1.85f, 1.92f, dark); Ring(new Vector3(-6, 0, -4), 2.45f, 2.52f, dark);
            Ring(new Vector3(7, 0, 2), 0, 3.2f, coral); Ring(new Vector3(7, 0, 2), 3.05f, 3.2f, rim);             // on the grey stone
            Ring(new Vector3(-2.5f, 0, -1), 0, 2.4f, coral); Ring(new Vector3(-2.5f, 0, -1), 2.25f, 2.4f, rim);   // overlapping the mosaic
            Ring(new Vector3(2.5f, 0, -3), 0, 1.7f, new Color(0.62f, 1f, 0.94f, 0.12f)); Ring(new Vector3(2.5f, 0, -3), 1.7f, 1.85f, new Color(0.62f, 1f, 0.94f, 0.95f));                      // friendly ring on the mosaic
            Ring(new Vector3(-6, 0, -4), 2.3f, 2.45f, new Color(0.3f, 0.95f, 0.85f, 0.95f));
            var cap = new Material(Shader.Find("Universal Render Pipeline/Lit")); cap.SetColor("_BaseColor", new Color(0.25f, 0.35f, 0.85f));
            for (int i = 0; i < 8; i++)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Capsule); c.transform.SetParent(root.transform);
                float a = (200 + i * 20) * Mathf.Deg2Rad; c.transform.position = new Vector3(Mathf.Cos(a) * (i % 2 == 0 ? 12.8f : 9f), 0.9f, Mathf.Sin(a) * (i % 2 == 0 ? 12.8f : 9f)); c.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
                c.GetComponent<Renderer>().sharedMaterial = cap;
            }
            return root;
        }

        public static void Capture(string file, int w = 1920, int h = 1080)
        {
            var cam = Camera.main; cam.aspect = (float)w / h;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
            Debug.Log("Saved " + file);
        }
    }

    // Floor textures: albedo sRGB 4K, height -> normal map, mask linear 4K.
    sealed class VerdantChimesTextureImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/LastBell/Areas/Area3/Source/")) return;
            var t = (TextureImporter)assetImporter; t.maxTextureSize = 4096; t.mipmapEnabled = true; t.anisoLevel = 8;
            if (assetPath.EndsWith("FloorHeight.png")) { t.textureType = TextureImporterType.NormalMap; t.convertToNormalmap = true; t.heightmapScale = 0.03f; t.normalmapFilter = TextureImporterNormalFilter.Sobel; }
            else if (assetPath.EndsWith("FloorMask.png")) { t.sRGBTexture = false; }
        }
    }
}
