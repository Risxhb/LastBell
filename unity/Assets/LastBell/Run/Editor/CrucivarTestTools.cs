using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LastBell.Run.EditorTools
{
    // Last Bell > Bosses > Crucivar in the Brass Foundry: opens the run loop's Brass Foundry map (building the run scenes
    // if they're missing), where Crucivar is the "Crucivar" practice button.
    //   Unity.exe -projectPath unity -executeMethod LastBell.Run.EditorTools.CrucivarTestTools.PlayModeTest -crucivarTest -lastbellOut <dir>
    // CrucivarAutoTest (ability set v1) exits the editor with 0 (all checks passed) or 1. Ability set v2:
    //   Unity.exe -projectPath unity -executeMethod LastBell.Run.EditorTools.CrucivarTestTools.PlayModeTest -crucivarV2Test [-crucivarAbility <id>] -lastbellOut <dir>
    public static class CrucivarTestTools
    {
        static string FoundryScene => RunMaps.All[RunMaps.IndexOf("brass-foundry")].RunScene;

        [MenuItem("Last Bell/Bosses/Crucivar in the Brass Foundry", priority = 40)]
        public static void OpenScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(FoundryScene) == null) RunBuild.BuildScene();
            EditorSceneManager.OpenScene(FoundryScene, OpenSceneMode.Single);
            Debug.Log("Last Bell: " + FoundryScene + " open; press Play, then Practice > Crucivar");
        }

        public static void PlayModeTest()
        {
            OpenScene();
            EditorApplication.isPlaying = true;
        }
    }
}
