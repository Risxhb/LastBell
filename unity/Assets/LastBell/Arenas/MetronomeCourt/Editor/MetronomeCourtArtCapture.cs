using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LastBell.Arenas.MetronomeCourt.EditorTools
{
    // Art-review renders of the production Metronome Court (editor only, nothing saved). Opens the source scene, builds the
    // arena exactly as Awake does, evaluates one presentation frame (Haste preview, no fight) and renders fixed cameras.
    //   Unity.exe -batchmode -projectPath unity -executeMethod LastBell.Arenas.MetronomeCourt.EditorTools.MetronomeCourtArtCapture.Capture
    //   with LASTBELL_ARENA_EVIDENCE=<folder>. Not a gameplay, warning or performance capture.
    public static class MetronomeCourtArtCapture
    {
        public static void Capture()
        {
            string output = Environment.GetEnvironmentVariable("LASTBELL_ARENA_EVIDENCE");
            if (string.IsNullOrEmpty(output)) throw new InvalidOperationException("Set LASTBELL_ARENA_EVIDENCE.");
            Directory.CreateDirectory(output);
            // r4: LASTBELL_ARENA_VIEWS = comma-separated view-name prefixes (empty = every view), so one item's views can
            // be rendered without the whole set
            var wanted = (Environment.GetEnvironmentVariable("LASTBELL_ARENA_VIEWS") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(v => v.Trim()).ToArray();
            bool Want(string name) => wanted.Length == 0 || wanted.Any(w => name.StartsWith(w, StringComparison.OrdinalIgnoreCase));
            EditorSceneManager.OpenScene("Assets/LastBell/Arenas/MetronomeCourt/MetronomeCourt_Arena.unity", OpenSceneMode.Single);
            var arena = UnityEngine.Object.FindAnyObjectByType<MetronomeCourtArena>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            typeof(MetronomeCourtArena).GetMethod("Build", flags).Invoke(arena, null);
            var update = typeof(MetronomeCourtArena).GetMethod("Update", flags);
            for (int i = 0; i < 3; i++) update.Invoke(arena, null);   // tempo colours, lantern chase, probe render, sky
            Inventory(arena.transform);

            var cam = Camera.main; int shots = 0; float baseFov = cam.fieldOfView;
            if (arena.SkyActive) cam.clearFlags = CameraClearFlags.Skybox;
            void Shot(string name, Vector3 pos, Vector3 target, int width = 1920, int height = 1080, float fov = 0)
            {
                if (!Want(name)) return;
                cam.fieldOfView = fov > 0 ? fov : baseFov;
                cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(target - pos));
                var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32); cam.targetTexture = rt;
                bool async = ShaderUtil.allowAsyncCompilation;   // avoid the grey async-compilation fallback in the first render
                try { ShaderUtil.allowAsyncCompilation = false; cam.Render(); cam.Render(); }
                finally { ShaderUtil.allowAsyncCompilation = async; }
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, width, height), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(output, name + ".png"), tex.EncodeToPNG());
                RenderTexture.active = prev; cam.targetTexture = null; UnityEngine.Object.DestroyImmediate(tex); UnityEngine.Object.DestroyImmediate(rt);
                cam.fieldOfView = baseFov; shots++;
            }
            var follow = new Vector3(0, 13, 9.7f);   // RunDirector.LateUpdate offset; looks at the follow point + 1 m
            void Fight(string name, Vector3 player) => Shot(name, player + follow, player + Vector3.up, 2560, 1440);   // r4: the fight camera at the player build's resolution
            // r4 view groups, shared by the Haste and Normal passes
            void FightViews(string suffix)
            {
                Fight("20_fight_south_bound" + suffix, new Vector3(0, 0, 12f));        // like player frame 43: the bridge mouth
                Fight("21_fight_southwest" + suffix, new Vector3(-8.6f, 0, 8.6f));     // like 22: the rim at lower left
                Fight("22_fight_southeast" + suffix, new Vector3(8.6f, 0, 8.6f));      // like 38: the rim down the right side
                Fight("23_fight_west_rim" + suffix, new Vector3(-12f, 0, -5f));        // like 26: the west flank
                Fight("24_fight_centre" + suffix, Vector3.zero);                       // like 04 / 11
                Fight("25_fight_north" + suffix, new Vector3(0, 0, -9f));              // the far rim and the banner posts
            }
            Shot("01_overview", new Vector3(32, 32, 43), new Vector3(0, -1, -3));
            Shot("02_gameplay_projection_centre", follow, Vector3.up);
            Shot("03_metronome", new Vector3(0, 9, 4), new Vector3(0, 5, -19.5f));
            Shot("04_south_approach", new Vector3(13, 13, 36), new Vector3(0, 0, 7));
            Shot("05_underside", new Vector3(34, 7, 29), new Vector3(0, -6, 0));
            Shot("06_ultrawide", new Vector3(32, 32, 43), new Vector3(0, -1, -3), 2560, 1080);
            Shot("07_west_flank", new Vector3(-36, 10, 14), new Vector3(0, -3, -6));
            foreach (var edge in new[] { new Vector3(0, 0, 12.5f), new Vector3(12.5f, 0, 0), new Vector3(0, 0, -12.5f), new Vector3(-12.5f, 0, 0) })
                Shot($"edge_{edge.x:0}_{edge.z:0}", edge + follow, edge + Vector3.up);
            // pass 5/6: close-ups of the pieces Risxhb flagged (a west pier head, a near bollard, a bridge rail post cap)
            Shot("09_closeup_pier_head", new Vector3(-13f, 3.5f, 3f), new Vector3(-19.5f, -4.5f, -5.5f));
            Shot("10_closeup_bollard", new Vector3(3.6f, 1.9f, 10.9f), new Vector3(5.5f, 0.55f, 13.3f));
            Shot("11_closeup_bridge_post", new Vector3(0.9f, 1.7f, 15.6f), new Vector3(2.45f, 0.6f, 17.65f));
            // October 28 critic pass: the view Risxhb screenshotted (gameplay camera, player near the west rim, a pier head
            // and the rim posts below), plus a medium shot of the same pier head from above
            var west = new Vector3(-12f, 0, -5f);
            Shot("12_gameplay_west_pier", west + follow, west + Vector3.up);
            Shot("13_mid_pier_head_above", new Vector3(-13.5f, 9f, 1f), new Vector3(-19.5f, -1.5f, -5.5f));
            Shot("14_closeup_banner_post", new Vector3(-9.6f, 4.4f, -2.6f), new Vector3(-13.3f, 2.7f, -5.5f));   // the rim post at 247.5 degrees
            // r4 (November 12 critic): the fight camera at the bounds the critic judged from, and one close view per item
            FightViews("");
            Shot("30_tt_metronome_elevation", new Vector3(0, 6, 4), new Vector3(0, 5, -19.5f), 1280, 720, 50);       // the autotest frames 02 and 40
            Shot("31_closeup_east_post_lantern", new Vector3(9.6f, 4.9f, -2.6f), new Vector3(13.3f, 4.0f, -5.5f));   // B2: the post under the rod at the chase peak
            Shot("32_rim_southwest_low", new Vector3(-5.5f, 5.5f, 21f), new Vector3(-9.5f, -0.6f, 11.5f));           // B1, T6: ledge, trim and drum edge
            Shot("33_bridge_mouth", new Vector3(0, 4.6f, 9.6f), new Vector3(0, 0.3f, 14.6f));                        // B6: bollards and newels
            Shot("34_low_towers_northeast", new Vector3(-4f, 2.2f, 8f), new Vector3(40f, -6f, -45f));                // B5: tower feet over the cloud sea
            Shot("35_low_towers_west", new Vector3(6f, 2.2f, 4f), new Vector3(-55f, -6f, -20f));                     // B5, B4
            for (int k = 0; k < 4; k++)   // B5: the menu orbit (target (0,1,0), 32 up and 26.5 out, FOV 44)
                Shot($"36_menu_orbit_{k * 90}", new Vector3(0, 1, 0) + Quaternion.Euler(0, k * 90, 0) * new Vector3(0, 32f, 26.5f), new Vector3(0, 1, 0));
            Shot("37_floor_from_above", new Vector3(0, 30f, 9f), new Vector3(0, 0, -0.5f));                          // T1: the whole disc
            Shot("38_metronome_case", new Vector3(4.5f, 6.5f, -7.5f), new Vector3(0, 5.2f, -19.5f));                 // T2: case, stiles and depth
            Shot("39_banner_front", new Vector3(-10.4f, 2.6f, -4.3f), new Vector3(-13.0f, 2.1f, -5.4f));             // T5: border, tails and folds
            Shot("40_swag_close", new Vector3(10.4f, 1.7f, 15.2f), new Vector3(7.85f, 0.4f, 11.75f));                // T3: the chain between two bollards
            Shot("41_bollard_from_above", new Vector3(5.5f, 5.2f, 10.2f), new Vector3(5.51f, 0.9f, 13.3f));          // T7: the cap from the fight camera side
            Shot("42_bridge_deck", new Vector3(0, 8.5f, 31f), new Vector3(0, 0, 20f));                               // T8: rails and paving
            Shot("43_pier_roof_east", new Vector3(13f, 3.5f, 3f), new Vector3(19.5f, -4.5f, -5.5f));                 // T4: the lit side
            Shot("44_pool_close", new Vector3(-4.0f, 3.2f, -8.5f), new Vector3(-5.3f, 0, -12.9f));                          // B3: a floor pool under its rim post
            // B2: every light flashing together on a transition tick (the autotest frame 40 catches this in frenzy)
            if (wanted.Length == 0 || wanted.Any(w => w.StartsWith("30_", StringComparison.OrdinalIgnoreCase) || w.StartsWith("31_", StringComparison.OrdinalIgnoreCase)))
            {
                var flashField = typeof(MetronomeCourtArena).GetField("flash", flags);
                flashField.SetValue(arena, 1f); update.Invoke(arena, null);
                Shot("30_tt_metronome_elevation_flash", new Vector3(0, 6, 4), new Vector3(0, 5, -19.5f), 1280, 720, 50);
                Shot("31_closeup_east_post_lantern_flash", new Vector3(9.6f, 4.9f, -2.6f), new Vector3(13.3f, 4.0f, -5.5f));
                flashField.SetValue(arena, 0f); update.Invoke(arena, null);
            }
            if (wanted.Length == 0 || wanted.Contains("probe", StringComparer.OrdinalIgnoreCase)) PoolProbe(arena, update, flags);
            // pass 4: the same gameplay projections in the Normal (cool) tempo, to check that the stone keeps its colour
            MetronomeCourtArena.ReviewHaste = false;
            typeof(MetronomeCourtArena).GetField("tempoBlend", flags).SetValue(arena, 0f);
            for (int i = 0; i < 3; i++) update.Invoke(arena, null);
            Shot("08_gameplay_normal", follow, Vector3.up);
            Shot("edge_-13_0_normal", new Vector3(-12.5f, 0, 0) + follow, new Vector3(-12.5f, 1, 0));
            FightViews("_normal");
            Shot("41_bollard_from_above_normal", new Vector3(5.5f, 5.2f, 10.2f), new Vector3(5.51f, 0.9f, 13.3f));
            MetronomeCourtArena.ReviewHaste = null;
            Debug.Log($"METRONOME ARENA CAPTURE: {shots} editor views (Haste preview plus Normal, no fight; not gameplay/performance) -> " + output);
        }

        // r4 (B3): the floor lantern pools must hold a steady level. Steps the preview through two seconds of Haste beats
        // (with the transition flash forced once) and reports how far the pools' colour moves.
        static void PoolProbe(MetronomeCourtArena arena, MethodInfo update, BindingFlags flags)
        {
            var beatField = typeof(MetronomeCourtArena).GetField("previewBeat", flags); var flashField = typeof(MetronomeCourtArena).GetField("flash", flags);
            var pools = ((System.Collections.IEnumerable)typeof(MetronomeCourtArena).GetField("pools", flags).GetValue(arena)).Cast<object>()
                .Select(p => (Renderer)p.GetType().GetField("Item1").GetValue(p)).Where(r => r).ToArray();
            double start = (double)beatField.GetValue(arena); float min = float.MaxValue, max = 0; var block = new MaterialPropertyBlock(); int high = Shader.PropertyToID("_ColorHigh");
            for (int i = 0; i < 120; i++)
            {
                beatField.SetValue(arena, start + i / 60.0 * 220.0 / 60.0);
                if (i == 40) flashField.SetValue(arena, 1f);
                update.Invoke(arena, null);
                foreach (var r in pools)
                {
                    r.GetPropertyBlock(block); var c = block.GetColor(high); float l = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
                    min = Mathf.Min(min, l); max = Mathf.Max(max, l);
                }
            }
            flashField.SetValue(arena, 0f); beatField.SetValue(arena, start); update.Invoke(arena, null);
            Debug.Log($"METRONOME ARENA PROBE: floor pools={pools.Length}, _ColorHigh luminance min {min:0.000} max {max:0.000}, max/min {(min > 0 ? max / min : 0):0.000} over 2 s of Haste beats with one transition flash (steady = under 1.03)");
        }

        // Rewrites the Test Rush map tile (Resources/Menu/Maps/metronome-court.png) from the current arena code, with the
        // same view as MapPreviewCapture and the Tempo Tyrant autotest (camera (0,26,20) on the origin, FOV 40, 960x540).
        // No player is spawned and nothing is saved except the PNG.
        //   -executeMethod LastBell.Arenas.MetronomeCourt.EditorTools.MetronomeCourtArtCapture.CapturePreview
        public static void CapturePreview()
        {
            EditorSceneManager.OpenScene("Assets/LastBell/Arenas/MetronomeCourt/MetronomeCourt_Arena.unity", OpenSceneMode.Single);
            var arena = UnityEngine.Object.FindAnyObjectByType<MetronomeCourtArena>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            typeof(MetronomeCourtArena).GetMethod("Build", flags).Invoke(arena, null);
            var update = typeof(MetronomeCourtArena).GetMethod("Update", flags);
            for (int i = 0; i < 3; i++) update.Invoke(arena, null);
            var cam = Camera.main; if (arena.SkyActive) cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 40; var pos = new Vector3(0, 26, 20);
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-pos));
            var rt = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32); cam.targetTexture = rt;
            bool async = ShaderUtil.allowAsyncCompilation;
            try { ShaderUtil.allowAsyncCompilation = false; cam.Render(); cam.Render(); } finally { ShaderUtil.allowAsyncCompilation = async; }
            RenderTexture.active = rt; var tex = new Texture2D(960, 540, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); tex.Apply();
            const string png = "Assets/LastBell/Menu/Resources/Menu/Maps/metronome-court.png";
            File.WriteAllBytes(png, tex.EncodeToPNG()); RenderTexture.active = null; cam.targetTexture = null;
            AssetDatabase.ImportAsset(png, ImportAssetOptions.ForceUpdate);
            Debug.Log("METRONOME ARENA PREVIEW: wrote " + png);
        }

        // Checks the menu's baked Metronome Court stage after a save: renderer count, and that every generated texture
        // it references was saved with its pixels (a texture saved without them would load blank).
        //   -executeMethod LastBell.Arenas.MetronomeCourt.EditorTools.MetronomeCourtArtCapture.VerifyShowcaseStage
        public static void VerifyShowcaseStage()
        {
            EditorSceneManager.OpenScene("Assets/LastBell/Menu/ShowcaseMainMenu.unity", OpenSceneMode.Single);
            var stage = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.name == "Showcase environment - Metronome Court" && t.gameObject.scene.IsValid());
            if (!stage) { Debug.LogError("METRONOME SHOWCASE CHECK: stage not found"); return; }
            var renderers = stage.GetComponentsInChildren<Renderer>(true);
            var textures = renderers.SelectMany(r => r.sharedMaterials).Where(m => m).Distinct()
                .SelectMany(m => m.GetTexturePropertyNameIDs().Select(m.GetTexture)).OfType<Texture2D>().Distinct().ToArray();
            int missingMat = renderers.Count(r => r.sharedMaterials.Any(m => !m));
            int missingMesh = stage.GetComponentsInChildren<MeshFilter>(true).Count(f => !f.sharedMesh);
            foreach (var t in textures.Where(t => t.name.StartsWith("MetronomeCourt")))
            {
                string data = t.isReadable ? $"pixels {(t.GetPixelData<byte>(0).Length > 0 ? "present" : "EMPTY")}" : "not readable";
                Debug.Log($"METRONOME SHOWCASE TEXTURE: {t.name} {t.width}x{t.height} {t.format} mips {t.mipmapCount} {data} asset={(AssetDatabase.Contains(t) ? AssetDatabase.GetAssetPath(t) : "in scene")}");
            }
            Debug.Log($"METRONOME SHOWCASE CHECK: renderers={renderers.Length}, textures={textures.Length}, renderers missing a material={missingMat}, mesh filters missing a mesh={missingMesh}");
        }

        static void Inventory(Transform root)
        {
            var renderers = root.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled).ToArray();
            long tris = 0;
            foreach (var r in renderers)
            {
                var mesh = r.GetComponent<MeshFilter>()?.sharedMesh; if (!mesh) continue;
                int start = r.isPartOfStaticBatch ? r.subMeshStartIndex : 0;
                int count = r.isPartOfStaticBatch ? r.sharedMaterials.Length : mesh.subMeshCount;
                for (int k = start; k < start + count && k < mesh.subMeshCount; k++) tris += mesh.GetIndexCount(k) / 3;
            }
            var mats = renderers.SelectMany(r => r.sharedMaterials).Where(m => m).Distinct().ToArray();
            long texBytes = mats.SelectMany(m => m.GetTexturePropertyNameIDs().Select(m.GetTexture)).Where(t => t).Distinct()
                .Sum(t => UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t));
            int transparent = renderers.Count(r => r.sharedMaterials.Any(m => m && m.renderQueue >= 2500));
            Debug.Log($"METRONOME ARENA INVENTORY: renderers={renderers.Length}, triangles={tris}, materials={mats.Length}, transparent_renderers={transparent}, texture_bytes={texBytes} (editor estimate, after the arena's own static batching)");
            foreach (Transform g in root.GetComponentsInChildren<Transform>().Where(t => t.parent && t.parent.name == "Generated"))
            {
                var rs = g.GetComponentsInChildren<MeshRenderer>(); if (rs.Length == 0) continue;
                Debug.Log($"METRONOME ARENA GROUP: {g.name} | renderers={rs.Length}");
            }
        }
    }
}
