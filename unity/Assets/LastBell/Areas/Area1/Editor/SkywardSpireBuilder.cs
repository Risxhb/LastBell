using System.Collections.Generic;
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
            RenderSettings.ambientSkyColor = Hex(0xf6ead4, 0.62f); RenderSettings.ambientEquatorColor = Hex(0xc8d4e8, 0.6f); RenderSettings.ambientGroundColor = Hex(0xb4c4e0, 0.55f); // cool blue fill keeps shaded ivory from going olive
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
            UseSkyArchitecture(root);
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true)) { r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true; }
            foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t.parent && (t.parent.name == "Rim" || t.parent.name == "Bridges" || t.parent.name == "Floor" || t.parent.name == "Spires" || t.parent.name == "Abyss")))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);

            // Lighting: high sun in the north-west (shadows fall to the lower right, as in the reference), cool fill, probe.
            var lighting = Child(root.transform, "Lighting");
            var sun = NewLight("Sun", LightType.Directional, Hex(0xfff0d8), 1.8f, FromThree(-0.5f, 0.82f, -0.28f), lighting);
            sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.8f; RenderSettings.sun = sun;
            NewLight("Fill", LightType.Directional, Hex(0xa9c6ff), 0.4f, FromThree(20, 10, 25), lighting);
            var probe = new GameObject("Reflection Probe").AddComponent<ReflectionProbe>(); probe.transform.SetParent(lighting, false);
            probe.transform.localPosition = new Vector3(0, 6, 0); probe.mode = ReflectionProbeMode.Realtime; probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.size = new Vector3(120, 80, 120); probe.resolution = 256; probe.boxProjection = false; probe.intensity = 0.5f;

            BuildClouds(root.transform, Child(root.transform, "Abyss"));

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
            camData.requiresDepthOption = CameraOverrideOption.On; // SkyCloud fades against scene depth (per camera; shared URP assets untouched)
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
                    if (m && m.name.StartsWith("StarWindow") && m.HasProperty("emissiveFactor")) m.SetColor("emissiveFactor", Hex(0x2a50c0) * 0.6f);
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

        // Every background material becomes a SkyArchitecture copy (same map, tiling, colour, metal, smoothness, emission),
        // so towers, piers and the dais underside fade and dissolve into the clouds. Floor and bridge decks keep glTF ones.
        static void UseSkyArchitecture(GameObject arena)
        {
            var shader = Shader.Find("LastBell/SkyArchitecture");
            var made = new Dictionary<Material, Material>();
            foreach (var r in arena.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials; bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i]; if (!src || src.name == "Floor" || src.name == "BridgeDeck") continue;
                    if (!made.TryGetValue(src, out var m))
                    {
                        m = new Material(shader) { name = "Sky_" + src.name };
                        var tex = src.HasProperty("baseColorTexture") ? src.GetTexture("baseColorTexture") : null;
                        if (tex)
                        {
                            m.SetTexture("_BaseMap", tex);
                            if (src.HasProperty("baseColorTexture_ST")) { var st = src.GetVector("baseColorTexture_ST"); m.SetTextureScale("_BaseMap", new Vector2(st.x, st.y)); m.SetTextureOffset("_BaseMap", new Vector2(st.z, st.w)); }
                        }
                        if (src.HasProperty("baseColorFactor")) m.SetColor("_BaseColor", src.GetColor("baseColorFactor"));
                        if (src.HasProperty("metallicFactor")) m.SetFloat("_Metallic", src.GetFloat("metallicFactor"));
                        if (src.HasProperty("roughnessFactor")) m.SetFloat("_Smoothness", 1 - src.GetFloat("roughnessFactor"));
                        if (src.HasProperty("emissiveFactor")) m.SetColor("_EmissionColor", src.GetColor("emissiveFactor"));
                        var em = src.HasProperty("emissiveTexture") ? src.GetTexture("emissiveTexture") : null;
                        if (em) m.SetTexture("_EmissionMap", em);
                        if (src.name.StartsWith("StarWindow")) m.SetFloat("_Cutoff", 0.5f);            // pointed-arch cut-outs
                        if (src.name == "Banner" || src.name.StartsWith("StarWindow")) m.SetFloat("_Cull", 0);
                        made[src] = m = SaveAsset(m, m.name + ".mat");
                    }
                    mats[i] = m; changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        // Layered soft cumulus (Shaders/SkyCloud: an 8-shape atlas, lit tops, lavender undersides, soft where they meet
        // geometry). Three layers: a deep cloud sea with sky-blue gaps between banks; a waist band clustered round every
        // tower, pier and the dais drum at the height where SkyArchitecture dissolves it (positions from the CloudAnchor
        // nodes exported with the GLB; node scale = footprint radius); and thin wisps between the towers. Waist puffs stay
        // with their tower (they only breathe); sea, wisp and high puffs drift.
        static void BuildClouds(Transform root, Transform abyss)
        {
            var parent = Child(abyss, "Clouds");
            var atlas = CloudAtlas();
            var shader = Shader.Find("LastBell/SkyCloud");
            var sunDir = FromThree(-0.5f, 0.82f, -0.28f).normalized;
            Material[] Layer(string name, uint lit, uint shade, float opacity, float soft) => Enumerable.Range(0, 16).Select(c =>   // 8 shapes x mirrored
            {
                var m = new Material(shader); m.SetTexture("_MainTex", atlas); m.SetFloat("_Cell", c % 8); m.SetFloat("_FlipX", c / 8);
                m.SetColor("_Lit", Hex(lit)); m.SetColor("_Shade", Hex(shade)); m.SetFloat("_Opacity", opacity); m.SetFloat("_SoftDistance", soft);
                m.SetVector("_SunDir", sunDir);
                return SaveAsset(m, $"Cloud{name}{c}.mat");
            }).ToArray();
            var sea = Layer("Sea", 0xfff8ec, 0x9fb4d8, 0.95f, 9); var waist = Layer("Waist", 0xfff8ec, 0xa9bcdd, 0.92f, 2.5f);   // short soft distance: collars stay visible right up to the stone
            var wisp = Layer("Wisp", 0xffffff, 0xdfe7f4, 0.38f, 3); var high = Layer("High", 0xffffff, 0xd8e2f2, 0.6f, 10);
            var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var rnd = new System.Random(21);
            float Rn() => (float)rnd.NextDouble();
            void Puff(string name, Material[] mats, Vector3 pos, float w, float h)
            {
                var go = new GameObject(name); go.transform.SetParent(parent, false);
                go.transform.position = pos; go.transform.localScale = new Vector3(w, h, 1);
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mats[rnd.Next(mats.Length)];
                mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            }
            for (int b = 0; b < 30; b++)                       // deep sea: big banks of 4–8 puffs, sky-blue gaps between banks
            {
                float a = Rn() * Mathf.PI * 2, d = 24 + Rn() * 230, by = -46 - Rn() * 22, spread = 8 + Rn() * 14;
                if (Mathf.PerlinNoise(Mathf.Cos(a) * d * 0.025f + 3.1f, Mathf.Sin(a) * d * 0.025f + 7.7f) < 0.68f) continue;   // noise-masked gaps: blue sky shows through
                int n = 4 + rnd.Next(5);
                for (int i = 0; i < n; i++)
                {
                    float s = (26 + Rn() * 34) * (0.7f + Rn() * 0.8f);
                    Puff("Cloud", sea, new Vector3(Mathf.Cos(a) * d + (Rn() - 0.5f) * spread * 2, by + (Rn() - 0.5f) * 6, Mathf.Sin(a) * d + (Rn() - 0.5f) * spread * 2), s * (1.6f + Rn() * 0.7f), s * 0.7f);   // wide flat banks, not discs
                }
            }
            var anchors = root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("CloudAnchor")).ToArray();
            foreach (var an in anchors)                        // cloud collars: each structure sinks into a ring of puffs
            {                                                  // set just OUTSIDE its surface, in three uneven bands, so the
                                                               // cloud sits in front of the fading stone (a puff centred inside the
                                                               // tower would be faded away by the soft-depth test and show nothing)
                float r = Mathf.Max(0.8f, Mathf.Abs(an.lossyScale.x)); int n = Mathf.Clamp(Mathf.RoundToInt(2 + r * 0.8f), 3, 7);
                foreach (float band in new[] { -12f, -20f })   // the lower band sits where SkyArchitecture cuts the stone off
                    for (int i = 0; i < n; i++)
                    {
                        if (Rn() < 0.25f) continue;   // ragged collars, sky between them
                        float a = (i + Rn() * 0.7f) / n * Mathf.PI * 2, d = r * (1.05f + Rn() * 0.55f), s = (4 + r * (1.6f + Rn() * 1.2f)) * (0.75f + Rn() * 0.5f);
                        var p = an.position + new Vector3(Mathf.Cos(a) * d, band + (Rn() - 0.5f) * 5, Mathf.Sin(a) * d);
                        Puff("CloudWaist", waist, p, s * (1.5f + Rn() * 0.6f), s * 0.72f);
                    }
            }
            for (int i = 0; i < 28; i++)                       // wisps: thin strands between the towers, outside the floor
            {
                float a = Rn() * Mathf.PI * 2, d = 45 + Rn() * 40;   // well clear of the bridges in the gameplay view
                Puff("Cloud", wisp, new Vector3(Mathf.Cos(a) * d, -4 - Rn() * 6, Mathf.Sin(a) * d), 26 + Rn() * 24, 5 + Rn() * 4);
            }
            for (int i = 0; i < 30; i++) { float a = Rn() * Mathf.PI * 2, d = 200 + Rn() * 200; Puff("Cloud", high, new Vector3(Mathf.Cos(a) * d, 20 + Rn() * 80, Mathf.Sin(a) * d), 80 + Rn() * 100, 40 + Rn() * 50); }
            // deep sky beneath it all
            var below = GameObject.CreatePrimitive(PrimitiveType.Quad); below.name = "SkyBelow"; Object.DestroyImmediate(below.GetComponent<Collider>());
            below.transform.SetParent(abyss, false); below.transform.position = new Vector3(0, -80, 0); below.transform.rotation = Quaternion.Euler(90, 0, 0); below.transform.localScale = Vector3.one * 1800;
            var seaMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); seaMat.SetColor("_BaseColor", Hex(0x4c9ae0));
            below.GetComponent<MeshRenderer>().sharedMaterial = SaveAsset(seaMat, "SkyBelow.mat");
            below.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            Debug.Log($"Area 1 clouds: {parent.childCount} puffs round {anchors.Length} anchors");
        }

        // 2 x 4 atlas (1024²) of cumulus shapes in 512 x 256 cells: billowy domed tops, flatter bottoms, fbm-broken edges.
        // Red = baked light (domes lit from above, darker toward the flat base); alpha = density. Row 0 is the bottom row.
        static Texture2D CloudAtlas()
        {
            string path = Gen + "/SkyCloudAtlas.png";
            if (!File.Exists(path))
            {
                const int N = 1024, CW = 512, CH = 256; var px = new Color[N * N]; var rnd = new System.Random(9);
                for (int cell = 0; cell < 8; cell++)
                {
                    int cx = (cell % 2) * CW, cy = (cell / 2) * CH, nb = 5 + rnd.Next(4);   // few, large lobes: cumulus heads, not scallops
                    var blobs = new Vector3[nb];
                    for (int b = 0; b < nb; b++)
                    {
                        float bx = 0.24f + (float)rnd.NextDouble() * 0.52f, r = 0.17f + (float)rnd.NextDouble() * 0.12f;
                        blobs[b] = new Vector3(bx, 0.3f + r * 0.6f + (float)rnd.NextDouble() * 0.28f * (1 - Mathf.Abs(bx - 0.5f) * 2), r);
                    }
                    float seed = cell * 17.3f, baseY = 0.22f + (float)rnd.NextDouble() * 0.08f;
                    for (int y = 0; y < CH; y++) for (int x = 0; x < CW; x++)
                    {
                        float u = (x + 0.5f) / CW, v = (y + 0.5f) / CH, field = 0, light = 0, wsum = 0;
                        foreach (var b in blobs)           // the cell is 2:1, so measure u at double scale
                        {
                            float dx = (u - b.x) * 2, dy = v - b.y, d = Mathf.Sqrt(dx * dx + dy * dy) / b.z;
                            if (d < 1.4f) { float f = 1 - d * d / 1.96f; field = Mathf.Max(field, f); float wgt = f * f; light += Mathf.Clamp01(0.55f + dy / b.z * 0.45f) * wgt; wsum += wgt; }
                        }
                        light = wsum > 0 ? light / wsum : 0;   // weighted blend of every lobe: no seam where one lobe takes over from another
                        float n = Mathf.PerlinNoise(u * 6 + seed, v * 3 + seed) * 0.45f + Mathf.PerlinNoise(u * 15 + seed, v * 7.5f + seed) * 0.33f + Mathf.PerlinNoise(u * 38 + seed, v * 19 + seed) * 0.22f;
                        float dens = Mathf.SmoothStep(0, 1, Mathf.Clamp01((field + (n - 0.5f) * 0.85f - 0.1f) / 0.75f));   // wide, noise-eroded edge: no round rims
                        dens *= Mathf.SmoothStep(0, 1, Mathf.Clamp01((v - baseY) / 0.12f));           // flatter bottom
                        dens *= Mathf.Clamp01(Mathf.Min(u, 1 - u) * 12) * Mathf.Clamp01((1 - v) * 10);  // never touch the cell edge
                        float lit = Mathf.Clamp01(light * 0.85f + v * 0.35f - 0.12f + (n - 0.5f) * 0.2f);   // stronger dome shading: bright heads, shadowed bellies
                        px[(cy + y) * N + cx + x] = new Color(lit, lit, lit, dens);
                    }
                }
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false); tex.SetPixels(px); tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.alphaIsTransparency = true; imp.wrapMode = TextureWrapMode.Clamp; imp.mipmapEnabled = true; imp.SaveAndReimport();
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
                driver.SetView(SkywardSpireArena.View.AbyssCloseup); Capture(Path.Combine(dir, "abyss-closeup.png"));
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
