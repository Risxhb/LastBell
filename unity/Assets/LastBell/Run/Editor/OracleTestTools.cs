using UnityEditor;
using UnityEditor.SceneManagement;

namespace LastBell.Run.EditorTools
{
    // Runs the Chimebound Oracle acceptance test in editor Play Mode, in the run-loop arena:
    //   Unity.exe -projectPath unity -executeMethod LastBell.Run.EditorTools.OracleTestTools.PlayModeTest -oracleTest -lastbellOut <dir>
    // OracleAutoTest exits the editor with 0 (all checks passed) or 1.
    public static class OracleTestTools
    {
        const string Scene = "Assets/LastBell/Run/RunArena.unity";

        public static void PlayModeTest()
        {
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }
    }
}
