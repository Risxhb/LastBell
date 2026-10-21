using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LastBell.Areas.Area1.EditorTools
{
    // Last Bell > Area 1: Skyward Spire builds (once) and opens the Area 1 arena scene; Rebuild Area 1 regenerates it.
    // The solid arena comes from SkywardSpire.glb (exported from prototype/arena-3d/areas/area-1 by export-glb.mjs).
    // This adds what glTF can't carry: the day sky, fog and ambient light, the sun and a reflection probe, the cloud
    // sea, colliders and spawn points, post-processing, the camera and a Vanguard for scale.
    // Same pattern as Arenas/Editor/KeyArtArenaBuilder.cs.
    public static class SkywardSpireBuilder
    {
        const string Dir = "Assets/LastBell/Areas/Area1", Gen = Dir + "/Generated", Prefabs = Dir + "/Prefabs", Captures = Dir + "/Captures";
        const string GlbPath = Dir + "/SkywardSpire.glb", ScenePath = Dir + "/SkywardSpire_Arena.unity", PrefabPath = Prefabs + "/LB_ENV_SkywardSpire_Arena.prefab";
        const string VanguardPath = "Assets/LastBell/Characters/Vanguard/Resources/Characters/Vanguard.glb";

        // Layout (metres, Unity axes: x is mirrored from three.js; north is -z, the gameplay camera sits south at +z).
        const float R = 14f, Rail = 14.65f, Ledge = 15.6f, BridgeW = 4.2f;

        [MenuItem("Last Bell/Area 1: Skyward Spire", priority = 20)]
        static void Open()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) Build();
            EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Last Bell/Rebuild Area 1", priority = 21)]
        static void Rebuild()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(); EditorSceneManager.OpenScene(ScenePath);
        }

        static Color Hex(uint rgb, float k = 1) => new Color(((rgb >> 16) & 255) / 255f * k, ((rgb >> 8) & 255) / 255f * k, (rgb & 255) / 255f * k);
        static Vector3 FromThree(float x, float y, float z) => new Vector3(-x, y, z); // glTF import mirrors x

        public static void Build()
        {
            foreach (var (parent, name) in new[] { (Dir, "Generated"), (Dir, "Prefabs"), (Dir, "Captures") })
                if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Sky, fog and ambient (skyward-spire-env.js: sky dome, Fog(0xbcd3f3, 70, 420), HemisphereLight(0xd6e6ff, 0xb9ac92)).
            var sky = SaveAsset(new Material(Shader.Find("LastBell/SkywardSky")), "SkywardSky.mat");
            RenderSettings.skybox = sky;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = Hex(0xcfe0f5);
            RenderSettings.fogStartDistance = 70; RenderSettings.fogEndDistance = 420;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex(0xf4ead8, 0.55f); RenderSettings.ambientEquatorColor = Hex(0xc8d4e8, 0.45f); RenderSettings.ambientGroundColor = Hex(0xb9ac92, 0.4f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox; RenderSettings.reflectionIntensity = 0.35f; // a full-strength sky sheen lifts the lapis toward lavender

            // The arena: unpack the glTF instance so Lighting/Gameplay can live under the same root.
            var glb = AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath);
            if (!glb) throw new FileNotFoundException("Run prototype/arena-3d/areas/area-1/export-glb.mjs first", GlbPath);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(glb);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            // glTFast folds a single root node into the asset's own root (named after the file), so accept either layout.
            var rootT = inst.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "LB_ENV_SkywardSpire_Arena") ?? inst.transform;
            rootT.SetParent(null, true);
            if (rootT.gameObject != inst) Object.DestroyImmediate(inst);
            var root = rootT.gameObject; root.name = "LB_ENV_SkywardSpire_Arena";
            DressMaterials(root);
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true)) { r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true; }
            foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t.parent && (t.parent.name == "Rim" || t.parent.name == "Bridges" || t.parent.name == "Floor" || t.parent.name == "Spires" || t.parent.name == "Abyss")))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);

            // Lighting: high sun in the north-west (shadows fall to the lower right, as in the reference), cool fill, probe.
            var lighting = Child(root.transform, "Lighting");
            var sun = NewLight("Sun", LightType.Directional, Hex(0xfff6ec), 1.7f, FromThree(-0.5f, 0.82f, -0.28f), lighting);
            sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.8f; RenderSettings.sun = sun;
            NewLight("Fill", LightType.Directional, Hex(0xa9c6ff), 0.4f, FromThree(20, 10, 25), lighting);
            var probe = new GameObject("Reflection Probe").AddComponent<ReflectionProbe>(); probe.transform.SetParent(lighting, false);
            probe.transform.localPosition = new Vector3(0, 6, 0); probe.mode = ReflectionProbeMode.Realtime; probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.size = new Vector3(120, 80, 120); probe.resolution = 256; probe.boxProjection = false; probe.intensity = 0.5f;

            BuildClouds(Child(root.transform, "Abyss"));

            // Gameplay: floor collider, invisible walls on the balustrade line and at the bridge ends, spawn points.
            var gameplay = Child(root.transform, "Gameplay");
            var floor = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "FloorSurface");
            if (floor && floor.TryGetComponent(out MeshFilter fmf)) floor.gameObject.AddComponent<MeshCollider>().sharedMesh = fmf.sharedMesh;
            BuildWalls(Child(gameplay, "Walls"));
            var spawns = Child(gameplay, "SpawnPoints");
            for (int i = 0; i < 8; i++) // eight players on an arc on the entry (south) side, facing the centre
            {
                float a = Mathf.PI / 2 + (i - 3.5f) * 0.22f;
                var s = new GameObject("Spawn_" + i).transform; s.SetParent(spawns, false);
                s.position = new Vector3(Mathf.Cos(a) * 10.5f, 0, Mathf.Sin(a) * 10.5f); s.LookAt(new Vector3(0, 0, 0));
            }
            var vanguard = AssetDatabase.LoadAssetAtPath<GameObject>(VanguardPath);
            if (vanguard)
            {
                var v = (GameObject)PrefabUtility.InstantiatePrefab(vanguard); v.name = "ScaleReference_Vanguard";
                v.transform.SetParent(gameplay, false); v.transform.position = FromThree(-3.5f, 0, 6.5f); v.transform.rotation = Quaternion.Euler(0, 180 + 36, 0);
            }
            BuildTelegraphCheck(gameplay);

            // Post: ACES, gentle bloom for the gold, slightly warm white balance, soft navy vignette.
            var volume = new GameObject("Post").AddComponent<Volume>(); volume.isGlobal = true; volume.transform.SetParent(lighting, false);
            volume.sharedProfile = BuildProfile();

            // Camera (SkywardSpireArena places it: gameplay view by default, T toggles the top-down reference view).
            var rig = Child(gameplay, "CameraRig");
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" }; camGo.transform.SetParent(rig, false);
            var cam = camGo.AddComponent<Camera>(); cam.nearClipPlane = 0.3f; cam.farClipPlane = 1600; cam.clearFlags = CameraClearFlags.Skybox;
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true; camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; camData.antialiasingQuality = AntialiasingQuality.High;
            camGo.AddComponent<AudioListener>();

            var driver = root.AddComponent<SkywardSpireArena>();
            var so = new SerializedObject(driver);
            so.FindProperty("arenaRoot").objectReferenceValue = root.transform;
            so.FindProperty("viewCamera").objectReferenceValue = cam;
            so.ApplyModifiedPropertiesWithoutUndo();
            driver.SetView(SkywardSpireArena.View.Gameplay);

            PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, InteractionMode.AutomatedAction);
            EditorSceneManager.SaveScene(scene, ScenePath);
            if (!EditorBuildSettings.scenes.Any(s => s.path == ScenePath))
                EditorBuildSettings.scenes = EditorBuildSettings.scenes.Concat(new[] { new EditorBuildSettingsScene(ScenePath, true) }).ToArray();
        }

        static Transform Child(Transform parent, string name)
        {
            var t = parent.Find(name); if (t) return t;
            t = new GameObject(name).transform; t.SetParent(parent, false); return t;
        }

        static Light NewLight(string name, LightType type, Color color, float intensity, Vector3 from, Transform parent)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); var l = go.AddComponent<Light>();
            l.type = type; l.color = color; l.intensity = intensity;
            if (type == LightType.Directional) go.transform.rotation = Quaternion.LookRotation(-from.normalized);
            return l;
        }

        // Star windows glow a little so bloom lifts them; everything else keeps its glTF material.
        static void DressMaterials(GameObject arena)
        {
            foreach (var r in arena.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m && m.name == "StarWindow" && m.HasProperty("emissiveFactor")) m.SetColor("emissiveFactor", Hex(0x2a50c0) * 0.6f);
        }

        // Invisible walls: short box segments on the balustrade line with gaps at the four bridge mouths,
        // bridge side walls and an end wall 3 m out on each bridge.
        static void BuildWalls(Transform parent)
        {
            const int n = 96; float gapHalf = BridgeW / 2 + 0.35f;
            for (int i = 0; i < n; i++)
            {
                float a = (i + 0.5f) / n * Mathf.PI * 2, x = Mathf.Cos(a) * Rail, z = Mathf.Sin(a) * Rail;
                bool gap = false; for (int k = 0; k < 4; k++) { float d = Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, k * 90)) * Mathf.Deg2Rad; if (d * Rail < gapHalf) gap = true; }
                if (gap) continue;
                var w = new GameObject("Wall").AddComponent<BoxCollider>(); w.transform.SetParent(parent, false);
                w.transform.position = new Vector3(x, 1.5f, z); w.transform.rotation = Quaternion.Euler(0, 90 - a * Mathf.Rad2Deg, 0);
                w.size = new Vector3(Rail * Mathf.PI * 2 / n * 1.05f, 3, 0.5f);
            }
            for (int k = 0; k < 4; k++)
            {
                var b = new GameObject("BridgeMouth_" + "ESWN"[k]).transform; b.SetParent(parent, false); b.rotation = Quaternion.Euler(0, k * 90, 0);
                float len = Ledge + 3 - Rail;
                foreach (var s in new[] { -1f, 1f })
                {
                    var side = new GameObject("Side").AddComponent<BoxCollider>(); side.transform.SetParent(b, false);
                    side.transform.localPosition = new Vector3(Rail + len / 2, 1.5f, s * (BridgeW / 2 + 0.25f)); side.size = new Vector3(len, 3, 0.5f);
                }
                var end = new GameObject("End").AddComponent<BoxCollider>(); end.transform.SetParent(b, false);
                end.transform.localPosition = new Vector3(Ledge + 3, 1.5f, 0); end.size = new Vector3(0.5f, 3, BridgeW + 1);
            }
        }

        // Billboard puffs (Arenas/Shaders/CloudBillboard): a cloud sea with sky between banks, puffs round the tower bases,
        // a few high far clouds. Same counts as skyward-spire-env.js.
        static void BuildClouds(Transform abyss)
        {
            var parent = Child(abyss, "Clouds");
            var puff = CloudTexture();
            var shader = Shader.Find("LastBell/CloudBillboard");
            var tints = new[] { 0xffffffu, 0xffffffu, 0xf4f7fdu, 0xb9cbe6u }.Select((c, i) =>
            {
                var m = new Material(shader); m.SetTexture("_MainTex", puff); m.SetColor("_Tint", Hex(c)); m.SetFloat("_Opacity", 0.9f);
                return SaveAsset(m, $"SkyCloud{i}.mat");
            }).ToArray();
            var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var rnd = new System.Random(21);
            float Rn() => (float)rnd.NextDouble();
            void Puff(int i, float a, float d, float y, float size)
            {
                var go = new GameObject("Cloud"); go.transform.SetParent(parent, false);
                go.transform.position = new Vector3(Mathf.Cos(a) * d, y, Mathf.Sin(a) * d); go.transform.localScale = new Vector3(size * 1.25f, size * 0.75f, 1);
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = tints[i % tints.Length];
                mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            }
            for (int i = 0; i < 90; i++) Puff(i, Rn() * Mathf.PI * 2, 24 + Rn() * 200, -28 - Rn() * 30, 16 + Rn() * 30);
            for (int i = 0; i < 70; i++) Puff(i + 1, Rn() * Mathf.PI * 2, 20 + Rn() * 34, -14 - Rn() * 16, 9 + Rn() * 12);
            for (int i = 0; i < 30; i++) Puff(i + 2, Rn() * Mathf.PI * 2, 200 + Rn() * 200, 20 + Rn() * 80, 60 + Rn() * 80);
            for (int i = 0; i < 90; i++) Puff(i + 3, Mathf.PI * (0.1f + Rn() * 0.8f), 20 + Rn() * 45, -12 - Rn() * 18, 14 + Rn() * 18); // dense banks gathered on the south side, as in the reference
            for (int i = 0; i < 60; i++) Puff(i, Rn() * Mathf.PI * 2, 19 + Rn() * 22, -10 - Rn() * 10, 10 + Rn() * 10);              // cumulus hiding the tower bases
            // the far blue below the clouds
            var sea = GameObject.CreatePrimitive(PrimitiveType.Quad); sea.name = "SkyBelow"; Object.DestroyImmediate(sea.GetComponent<Collider>());
            sea.transform.SetParent(abyss, false); sea.transform.position = new Vector3(0, -75, 0); sea.transform.rotation = Quaternion.Euler(90, 0, 0); sea.transform.localScale = Vector3.one * 1800;
            var seaMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); seaMat.SetColor("_BaseColor", Hex(0x4f8fd6));
            sea.GetComponent<MeshRenderer>().sharedMaterial = SaveAsset(seaMat, "SkyBelow.mat");
            sea.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        static Texture2D CloudTexture()
        {
            string path = Gen + "/SkyCloudPuff.png";
            if (!File.Exists(path))
            {
                const int N = 256; var px = new Color[N * N]; var rnd = new System.Random(3);
                var blobs = Enumerable.Range(0, 18).Select(_ => new Vector3(50 + (float)rnd.NextDouble() * 156, 76 + (float)rnd.NextDouble() * 96, 26 + (float)rnd.NextDouble() * 44)).ToArray();
                for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                {
                    float a = 0;
                    foreach (var b in blobs) { float d = Vector2.Distance(new Vector2(x, y), new Vector2(b.x, b.y)) / b.z; if (d < 1) a = a + (1 - a) * 0.6f * (1 - d * d); }
                    float shade = Mathf.Lerp(0.86f, 1f, y / (float)N); // undersides a touch bluer-grey
                    px[y * N + x] = new Color(shade, shade + 0.03f * (1 - shade), 1, a);
                }
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false); tex.SetPixels(px); tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.alphaIsTransparency = true; imp.wrapMode = TextureWrapMode.Clamp; imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Generic warning shapes (deep red-orange fill, dark outer rim) for the readability check. Off by default.
        static void BuildTelegraphCheck(Transform gameplay)
        {
            var check = Child(gameplay, "TelegraphCheck"); check.gameObject.SetActive(false);
            var shader = Shader.Find("LastBell/TelegraphCheck");
            Material Mat(string name, bool cone)
            {
                string path = Gen + $"/{name}.png";
                const int N = 512; var px = new Color[N * N]; // 512: smooth edges at gameplay distance
                for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2 - 1, v = (y + 0.5f) / N * 2 - 1, r = Mathf.Sqrt(u * u + v * v), a = Mathf.Atan2(v, u), hw = cone ? 0.5236f : 4;
                    if (r > 1 || Mathf.Abs(a) > hw) { px[y * N + x] = Color.clear; continue; }
                    float edge = Mathf.Min(1 - r, cone ? (hw - Mathf.Abs(a)) * r : 1);
                    float rim = 1 - Mathf.SmoothStep(0.035f, 0.07f, edge), dark = 1 - Mathf.SmoothStep(0, 0.025f, edge);
                    var col = Color.Lerp(new Color(0.75f, 0.06f, 0.03f), new Color(1f, 0.4f, 0.22f), rim); col = Color.Lerp(col, new Color(0.09f, 0.004f, 0.002f), dark);
                    col.a = Mathf.Max(Mathf.Max(rim, dark) * 0.97f, 0.62f); px[y * N + x] = col;
                }
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false, true); tex.SetPixels(px); tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex); AssetDatabase.ImportAsset(path);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path); imp.sRGBTexture = false; imp.alphaIsTransparency = true; imp.wrapMode = TextureWrapMode.Clamp; imp.SaveAndReimport();
                var m = new Material(shader); m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
                return SaveAsset(m, name + ".mat");
            }
            void Decal(string name, Material m, Vector3 pos, float size, float yaw)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad); q.name = name; Object.DestroyImmediate(q.GetComponent<Collider>());
                q.transform.SetParent(check, false); q.transform.position = pos; q.transform.rotation = Quaternion.Euler(90, yaw, 0); q.transform.localScale = Vector3.one * size;
                var mr = q.GetComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = ShadowCastingMode.Off;
            }
            Decal("Circle", Mat("TeleCircle", false), FromThree(-5.5f, 0.03f, -4), 9, 0);
            Decal("Cone", Mat("TeleCone", true), FromThree(2, 0.03f, 0), 16, 180 - 36);
        }

        static VolumeProfile BuildProfile()
        {
            string path = Gen + "/SkywardPost.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, path);
            var tone = profile.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true); bloom.threshold.Override(1.2f); bloom.intensity.Override(0.3f); bloom.scatter.Override(0.55f);
            var color = profile.Add<ColorAdjustments>(true); color.postExposure.Override(-0.4f); color.saturation.Override(4f); color.contrast.Override(8f);
            var wb = profile.Add<WhiteBalance>(true); wb.temperature.Override(0f);
            var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.16f); vig.color.Override(Hex(0x1a2a5a));
            foreach (var c in profile.components) { c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy; AssetDatabase.AddObjectToAsset(c, profile); }
            AssetDatabase.SaveAssets();
            return profile;
        }

        static T SaveAsset<T>(T asset, string file) where T : Object
        {
            string path = Gen + "/" + file;
            AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        // ---- Batch mode: build the scene and save top-down + gameplay screenshots for review ----
        // Unity.exe -batchmode -projectPath unity -executeMethod LastBell.Areas.Area1.EditorTools.SkywardSpireBuilder.BatchBuildAndCapture
        public static void BatchBuildAndCapture()
        {
            int code = 0;
            try
            {
                ShaderUtil.allowAsyncCompilation = false; // otherwise the first frames render with placeholder shaders
                Build(); EditorSceneManager.OpenScene(ScenePath);
                CaptureAll();
            }
            catch (System.Exception e) { Debug.LogException(e); code = 1; }
            EditorApplication.Exit(code);
        }

        [MenuItem("Last Bell/Capture Area 1", priority = 22)]
        public static void CaptureAll()
        {
            string dir = Path.GetFullPath(Captures);
            Directory.CreateDirectory(dir);
            var driver = Object.FindFirstObjectByType<SkywardSpireArena>();
            var tele = driver.transform.Find("Gameplay/TelegraphCheck");
            foreach (var probe in Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None)) probe.RenderProbe();
            try
            {
                driver.SetView(SkywardSpireArena.View.TopDown); SkywardSpireArena.UseLongShadows(true);
                Capture(Path.Combine(dir, "topdown.png"), 1700, 1484); // warm-up frame
                Capture(Path.Combine(dir, "topdown.png"), 1700, 1484);
                SkywardSpireArena.UseLongShadows(false);
                driver.SetView(SkywardSpireArena.View.Gameplay);
                Capture(Path.Combine(dir, "gameplay.png"));
                if (tele) { tele.gameObject.SetActive(true); Capture(Path.Combine(dir, "gameplay-telegraphs.png")); tele.gameObject.SetActive(false); }
            }
            finally { SkywardSpireArena.UseLongShadows(false); driver.SetView(SkywardSpireArena.View.Gameplay); }
        }

        public static void Capture(string file, int w = 1920, int h = 1080)
        {
            var cam = Camera.main; cam.aspect = (float)w / h;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev; cam.ResetAspect();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
            Debug.Log("Saved " + file);
        }
    }
}
