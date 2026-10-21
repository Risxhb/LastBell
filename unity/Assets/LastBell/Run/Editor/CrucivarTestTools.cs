using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LastBell.Run.EditorTools
{
    // Last Bell > Bosses > Crucivar: the Brass Foundry arena (Area 2) with a RunDirector in it, so Crucivar can be
    // practised and auto-tested where it fights. The scene is generated into the arena's git-ignored Generated/ folder.
    //   Unity.exe -projectPath unity -executeMethod LastBell.Run.EditorTools.CrucivarTestTools.PlayModeTest -crucivarTest -lastbellOut <dir>
    // CrucivarAutoTest exits the editor with 0 (all checks passed) or 1.
    public static class CrucivarTestTools
    {
        const string ArenaScene = "Assets/LastBell/Areas/Area2/BrassFoundry_Arena.unity";
        public const string TestScene = "Assets/LastBell/Areas/Area2/Generated/Crucivar_FoundryTest.unity";

        [MenuItem("Last Bell/Bosses/Crucivar in the Brass Foundry", priority = 40)]
        public static void BuildScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ArenaScene) == null) LastBell.Areas.Area2.EditorTools.BrassFoundryArenaBuilder.Build();
            var scene = EditorSceneManager.OpenScene(ArenaScene, OpenSceneMode.Single);
            var go = new GameObject("RunDirector"); go.AddComponent<RunDirector>();
            EditorSceneManager.SaveScene(scene, TestScene, true);
            EditorSceneManager.OpenScene(TestScene);
            Debug.Log("Last Bell: Crucivar test scene ready at " + TestScene + " (practice: Crucivar)");
        }

        public static void PlayModeTest()
        {
            BuildScene();
            EditorApplication.isPlaying = true;
        }
    }
}
