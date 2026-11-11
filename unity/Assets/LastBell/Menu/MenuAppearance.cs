using System.Linq;
using LastBell.Run;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LastBell.Menu
{
    // The Confrontation menu is the only main menu (Risxhb, October 24, 2026). The Showcase scene stays in the build as the
    // startup router and as Boss Rush's live arena backdrop. The old Showcase/Alternative choice and its Interface panel
    // were retired on November 10, 2026; a saved "LastBell.Menu.Style" key in old profiles is simply ignored.
    public static class MenuAppearance
    {
        public static string ScenePath=>ConfrontationMenu.ScenePath;
        // HeroSelection (old character select) is retired and no longer built.
        public static string[] BuildScenes=>new[]{MainMenu.ScenePath,ConfrontationMenu.ScenePath}.Concat(RunMaps.All.Select(m=>m.RunScene)).ToArray();
        // Startup router for the Showcase scene: hands over to the Confrontation menu unless a diagnostic owns this scene.
        // Returns true when the caller should keep running.
        public static bool Route(GameObject owner)
        {
            if(RunAutoTest.Requested||MapPreviewCapture.Requested||MenuSmokeTest.Requested)return true;
            if(!Application.CanStreamedLevelBeLoaded(ScenePath)){Debug.LogError("The Confrontation menu is missing from this build.");return true;}
            owner.SetActive(false);
            SceneManager.LoadScene(ScenePath,LoadSceneMode.Single);
            return false;
        }
    }
}
