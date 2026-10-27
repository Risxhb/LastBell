using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LastBell.Areas.Area3.EditorTools
{
    // Style test (VERDANT-CHIMES-BLENDER-ART-PROMPT.md §6.1): drops the Blender hero pieces (Source/HeroTest/hero_test.glb)
    // into the built Verdant Chimes scene beside the procedural versions, with LB CustomLit materials, and captures views.
    // Unity.exe -batchmode -projectPath <proj> -executeMethod LastBell.Areas.Area3.EditorTools.HeroTestCapture.Run
    public static class HeroTestCapture
    {
        public static void Run()
        {
            int code = 0;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                EditorSceneManager.OpenScene("Assets/LastBell/Areas/Area3/VerdantChimes_Arena.unity");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LastBell/Areas/Area3/Source/HeroTest/hero_test.glb");
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.transform.position = new Vector3(-30, 0, 22); go.transform.rotation = Quaternion.Euler(0, 200, 0);
                var lit = Shader.Find("LastBell/Area3/CustomLit");
                var colors = new System.Collections.Generic.Dictionary<string, Color> {
                    ["Stone"] = new Color(0.68f, 0.65f, 0.59f), ["StoneDark"] = new Color(0.56f, 0.54f, 0.49f), ["Rock"] = new Color(0.62f, 0.61f, 0.56f),
                    ["Moss"] = new Color(0.33f, 0.47f, 0.24f), ["Ivy"] = new Color(0.25f, 0.42f, 0.22f), ["Brass"] = new Color(0.69f, 0.54f, 0.25f),
                    ["Bell"] = new Color(0.8f, 0.62f, 0.28f), ["Timber"] = new Color(0.36f, 0.27f, 0.19f), ["LanternMetal"] = new Color(0.17f, 0.16f, 0.13f),
                    ["Flower"] = new Color(0.96f, 0.95f, 0.9f), ["Pool"] = new Color(0.2f, 0.55f, 0.55f) };
                var cache = new System.Collections.Generic.Dictionary<string, Material>();
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        string n = mats[i] ? mats[i].name.Replace(" (Instance)", "") : "Stone";
                        if (!cache.TryGetValue(n, out var m))
                        {
                            m = new Material(lit) { name = "Hero" + n }; m.SetColor("_BaseColor", colors.TryGetValue(n, out var c) ? c : Color.white);
                            m.SetFloat("_UseVertexColor", 1); m.EnableKeyword("_LB_VERTEXCOLOR");
                            if (n == "Brass" || n == "Bell") m.SetFloat("_Metal", 1);
                            if (n == "Ivy" || n == "Flower") { m.SetFloat("_Sway", 1); m.SetFloat("_Cull", 0); }
                            cache[n] = m;
                        }
                        mats[i] = m;
                    }
                    r.sharedMaterials = mats;
                }
                Object.FindFirstObjectByType<LookSettingsApplier>().Apply();
                var driver = Object.FindFirstObjectByType<VerdantChimesArena>(); driver.timeOverride = 20; driver.Tick(1);
                string shots = Path.GetFullPath(Path.Combine(Application.dataPath, "LastBell/Areas/Area3/Screenshots")); Directory.CreateDirectory(shots);
                var cam = Camera.main;
                void Shot(string name, Vector3 pos, Vector3 look, float fov) { cam.fieldOfView = fov; cam.transform.position = pos; cam.transform.LookAt(look); VerdantChimesBuilder.Capture(Path.Combine(shots, name)); }
                Shot("hero-unity-close.png", new Vector3(-12, 14, 2), new Vector3(-32, 6, 24), 42);      // hero pieces, procedural arena behind
                Shot("hero-unity-close.png", new Vector3(-12, 14, 2), new Vector3(-32, 6, 24), 42);
                Shot("hero-unity-wide.png", new Vector3(-6, 30, -20), new Vector3(-18, 0, 12), 48);      // hero pieces beside the arena
            }
            catch (System.Exception e) { Debug.LogException(e); code = 1; }
            EditorApplication.Exit(code);
        }
    }
}
