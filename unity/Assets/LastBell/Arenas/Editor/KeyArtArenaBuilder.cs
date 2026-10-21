using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LastBell.Arenas.EditorTools
{
    // Last Bell > Test Arena (Key Art): builds (once) and opens the key-art test arena scene.
    // The solid arena comes from BellkeepersDais.glb (exported from prototype/arena-3d by tools/export-arena-glb.mjs);
    // this adds what glTF can't carry: the sunset sky, fog and ambient light, the sun, lantern lights, HDR glow
    // materials, the floor normal map, the sea of clouds, post-processing and the camera.
    public static class KeyArtArenaBuilder
    {
        const string Dir = "Assets/LastBell/Arenas/KeyArt", Gen = Dir + "/Generated";
        const string GlbPath = Dir + "/BellkeepersDais.glb", HeightPath = Dir + "/FloorHeight.png", ScenePath = Dir + "/KeyArtTestArena.unity";

        [MenuItem("Last Bell/Test Arena (Key Art)", priority = 10)]
        static void Open()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) Build();
            EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Last Bell/Rebuild Test Arena (Key Art)", priority = 11)]
        static void Rebuild()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(); EditorSceneManager.OpenScene(ScenePath);
        }

        static Color Hex(uint rgb, float k = 1) => new Color(((rgb >> 16) & 255) / 255f * k, ((rgb >> 8) & 255) / 255f * k, (rgb & 255) / 255f * k);
        static Vector3 FromThree(float x, float y, float z) => new Vector3(-x, y, z); // glTF import mirrors x

        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(Gen)) AssetDatabase.CreateFolder(Dir, "Generated");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Sky, fog and ambient (arena-env.js: sky dome, Fog(0x6a4f98, 90, 360), HemisphereLight(0xc2a8ff, 0x4a3460, 0.95)).
            var sky = SaveAsset(new Material(Shader.Find("LastBell/SunsetSky")), "SunsetSky.mat");
            RenderSettings.skybox = sky;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = Hex(0x6a4f98);
            RenderSettings.fogStartDistance = 90; RenderSettings.fogEndDistance = 360;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex(0xc2a8ff, 0.95f); RenderSettings.ambientEquatorColor = Hex(0x8a70b4, 0.75f); RenderSettings.ambientGroundColor = Hex(0x4a3460, 0.9f);

            // Warm low sun from the back right, cool bounce from the front left.
            var sun = NewLight("Sun", LightType.Directional, Hex(0xffc49a), 2.1f, FromThree(30, 26, -25.6f));
            sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.85f; RenderSettings.sun = sun;
            NewLight("Bounce", LightType.Directional, Hex(0x8f88ff), 0.45f, FromThree(-14, 8, 16));

            // The arena itself.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath);
            if (!prefab) throw new FileNotFoundException("Run tools/export-arena-glb.mjs first", GlbPath);
            var arena = (GameObject)PrefabUtility.InstantiatePrefab(prefab); arena.name = "Bellkeeper's Dais";
            DressMaterials(arena);
            foreach (var t in arena.GetComponentsInChildren<Transform>().Where(t => t.name == "Lantern").ToArray())
            {
                var l = NewLight("Lantern Light", LightType.Point, Hex(0xffa850), 2.5f, Vector3.zero);
                l.range = 9; l.transform.SetParent(t, false); l.transform.localPosition = new Vector3(0, 3.4f, 0);
            }
            BuildClouds();

            // Post: ACES, bloom for the glows, a soft violet vignette.
            var volume = new GameObject("Post").AddComponent<Volume>(); volume.isGlobal = true;
            volume.sharedProfile = BuildProfile();

            // Camera (KeyArtArena places it: gameplay view by default, C toggles the key-art angle in Play mode).
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>(); cam.nearClipPlane = 0.1f; cam.farClipPlane = 900; cam.clearFlags = CameraClearFlags.Skybox;
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true; camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.AddComponent<AudioListener>();

            var driver = new GameObject("Key Art Arena").AddComponent<KeyArtArena>();
            var so = new SerializedObject(driver);
            so.FindProperty("arenaRoot").objectReferenceValue = arena.transform;
            so.FindProperty("viewCamera").objectReferenceValue = cam;
            so.ApplyModifiedPropertiesWithoutUndo();
            driver.SetView(KeyArtArena.View.Gameplay);

            EditorSceneManager.SaveScene(scene, ScenePath);
            if (!EditorBuildSettings.scenes.Any(s => s.path == ScenePath))
                EditorBuildSettings.scenes = EditorBuildSettings.scenes.Concat(new[] { new EditorBuildSettingsScene(ScenePath, true) }).ToArray();
        }

        static Light NewLight(string name, LightType type, Color color, float intensity, Vector3 from)
        {
            var go = new GameObject(name); var l = go.AddComponent<Light>();
            l.type = type; l.color = color; l.intensity = intensity;
            if (type == LightType.Directional) go.transform.rotation = Quaternion.LookRotation(-from.normalized);
            return l;
        }

        // glTF has no HDR colours or bump maps: windows, lantern glass and the rose window become bright unlit
        // materials so bloom picks them up, and the floor gets a URP Lit material with a normal map from its height map.
        static void DressMaterials(GameObject arena)
        {
            var unlit = Shader.Find("Universal Render Pipeline/Unlit"); var lit = Shader.Find("Universal Render Pipeline/Lit");
            Material Glow(string name, Color c, float k)
            {
                var m = new Material(unlit) { name = name }; m.SetColor("_BaseColor", c * k); return SaveAsset(m, name + ".mat");
            }
            var glows = new System.Collections.Generic.Dictionary<string, Material>
            {
                ["GlowLantern"] = Glow("GlowLantern", Hex(0xffc56a), 4f),
                ["GlowWindow"] = Glow("GlowWindow", Hex(0xffb35e), 2.2f),
                ["GlowRose"] = Glow("GlowRose", Hex(0xffc879), 3.2f),
            };
            Material paving = null;
            foreach (var r in arena.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials; bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (!mats[i]) continue;
                    if (glows.TryGetValue(mats[i].name, out var g)) { mats[i] = g; changed = true; }
                    else if (mats[i].name == "Paving")
                    {
                        if (!paving)
                        {
                            var src = mats[i];
                            var baseTex = src.HasProperty("baseColorTexture") ? src.GetTexture("baseColorTexture") : src.mainTexture;
                            paving = new Material(lit) { name = "Paving" };
                            paving.SetTexture("_BaseMap", baseTex); paving.SetColor("_BaseColor", Color.white);
                            paving.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(HeightPath)); paving.SetFloat("_BumpScale", 1f);
                            paving.EnableKeyword("_NORMALMAP"); paving.SetFloat("_Smoothness", 0.12f); paving.SetFloat("_Metallic", 0);
                            paving = SaveAsset(paving, "Paving.mat");
                        }
                        mats[i] = paving; changed = true;
                    }
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        // Same layout rules as the cloud sprites in arena-env.js: a wide low sea of clouds plus a closer ring.
        static void BuildClouds()
        {
            var puff = CloudTexture();
            var shader = Shader.Find("LastBell/CloudBillboard");
            var tints = new[] { 0x5a4590u, 0x7a5aa8u, 0xc0849eu, 0xe89a88u, 0x463a7cu }.Select((c, i) =>
            {
                var m = new Material(shader); m.SetTexture("_MainTex", puff); m.SetColor("_Tint", Hex(c)); m.SetFloat("_Opacity", 0.85f);
                return SaveAsset(m, $"Cloud{i}.mat");
            }).ToArray();
            var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var parent = new GameObject("Sea of Clouds").transform;
            var rnd = new System.Random(7);
            float R() => (float)rnd.NextDouble();
            void Puff(int i, Vector3 pos, float size)
            {
                var go = new GameObject("Cloud"); go.transform.SetParent(parent, false);
                go.transform.position = pos; go.transform.localScale = new Vector3(size, size * 0.55f, 1);
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = tints[i % tints.Length];
                mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            }
            for (int i = 0; i < 110; i++)
            {
                float a = R() * Mathf.PI * 2, r = 40 + R() * 150, y = -16 - R() * 14 + (r > 70 ? R() * 18 : 0);
                Puff(i, new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r), 18 + R() * 30);
            }
            for (int i = 0; i < 24; i++)
            {
                float a = R() * Mathf.PI * 2, r = 34 + R() * 25;
                Puff(i + 2, new Vector3(Mathf.Cos(a) * r, -12 - R() * 8, Mathf.Sin(a) * r), 14 + R() * 16);
            }
        }

        static Texture2D CloudTexture()
        {
            string path = Gen + "/CloudPuff.png";
            if (!File.Exists(path))
            {
                const int N = 256; var px = new Color[N * N]; var rnd = new System.Random(3);
                var blobs = Enumerable.Range(0, 14).Select(_ => new Vector3(60 + (float)rnd.NextDouble() * 136, 80 + (float)rnd.NextDouble() * 96, 30 + (float)rnd.NextDouble() * 50)).ToArray();
                for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                {
                    float a = 0;
                    foreach (var b in blobs) { float d = Vector2.Distance(new Vector2(x, y), new Vector2(b.x, b.y)) / b.z; if (d < 1) a = a + (1 - a) * 0.55f * (1 - d); }
                    px[y * N + x] = new Color(1, 1, 1, a);
                }
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false); tex.SetPixels(px); tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.alphaIsTransparency = true; imp.wrapMode = TextureWrapMode.Clamp; imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static VolumeProfile BuildProfile()
        {
            string path = Gen + "/KeyArtPost.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, path);
            var tone = profile.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true); bloom.threshold.Override(1f); bloom.intensity.Override(0.7f); bloom.scatter.Override(0.6f);
            var color = profile.Add<ColorAdjustments>(true); color.postExposure.Override(0.35f); color.saturation.Override(8f);
            var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.22f); vig.color.Override(Hex(0x140f2e));
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

        // ---- Batch mode: build the scene and save gameplay + key-art screenshots for review ----
        // Unity.exe -batchmode -projectPath unity -executeMethod LastBell.Arenas.EditorTools.KeyArtArenaBuilder.BatchBuildAndCapture
        public static void BatchBuildAndCapture()
        {
            int code = 0;
            try
            {
                ShaderUtil.allowAsyncCompilation = false; // otherwise the first frames render with placeholder shaders
                Build(); EditorSceneManager.OpenScene(ScenePath);
                string shots = Path.GetFullPath(Path.Combine(Application.dataPath, "../../prototype/arena-3d/shots"));
                var driver = Object.FindFirstObjectByType<KeyArtArena>();
                driver.SetView(KeyArtArena.View.Gameplay); Capture(Path.Combine(shots, "unity-game.png")); // warm-up frame
                Capture(Path.Combine(shots, "unity-game.png"));
                driver.SetView(KeyArtArena.View.KeyArt); Capture(Path.Combine(shots, "unity-keyart.png"));
            }
            catch (System.Exception e) { Debug.LogException(e); code = 1; }
            EditorApplication.Exit(code);
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

    // FloorHeight.png is a height map: import it as a normal map so the flagstone bevels catch the light.
    sealed class KeyArtTextureImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.EndsWith("/KeyArt/FloorHeight.png")) return;
            var t = (TextureImporter)assetImporter;
            t.textureType = TextureImporterType.NormalMap; t.convertToNormalmap = true;
            t.heightmapScale = 0.035f; t.normalmapFilter = TextureImporterNormalFilter.Sobel;
            t.maxTextureSize = 4096;
        }
    }
}
