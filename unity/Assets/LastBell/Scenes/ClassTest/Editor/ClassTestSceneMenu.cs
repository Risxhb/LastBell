using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LastBell.Scenes.ClassTest.EditorTools
{
    // Last Bell > Class Test Arena: creates (once) and opens the self-building class-test scene.
    public static class ClassTestSceneMenu
    {
        const string ScenePath = "Assets/LastBell/Scenes/ClassTest/ClassTest.unity";

        [MenuItem("Last Bell/Class Test Arena", priority = 0)]
        static void Open() { TryOpen(); }

        static bool TryOpen()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                new GameObject("Class Test Arena").AddComponent<ClassTestArena>();
                EditorSceneManager.SaveScene(scene, ScenePath);
                if (!EditorBuildSettings.scenes.Any(s => s.path == ScenePath))
                    EditorBuildSettings.scenes = EditorBuildSettings.scenes
                        .Concat(new[] { new EditorBuildSettingsScene(ScenePath, true) }).ToArray();
            }
            EditorSceneManager.OpenScene(ScenePath);
            return true;
        }

        [MenuItem("Last Bell/Play Class Test Arena", priority = 1)]
        static void Play()
        {
            if (TryOpen()) EditorApplication.isPlaying = true;
        }
    }
}
