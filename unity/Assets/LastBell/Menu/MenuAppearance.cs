using System;
using System.Linq;
using LastBell.Run;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LastBell.Menu
{
    public enum MenuStyle { Showcase, Alternative }

    // A saved application preference, independent of which scene a launcher opens first.
    public static class MenuAppearance
    {
        const string Key="LastBell.Menu.Style";
        public static MenuStyle Selected
        {
            get=>PlayerPrefs.GetInt(Key,0)==1?MenuStyle.Alternative:MenuStyle.Showcase;
            set {PlayerPrefs.SetInt(Key,value==MenuStyle.Alternative?1:0);PlayerPrefs.Save();}
        }
        public static string PathFor(MenuStyle style)=>style==MenuStyle.Alternative?ConfrontationMenu.ScenePath:MainMenu.ScenePath;
        public static string ScenePath=>PathFor(Selected);
        public static string[] BuildScenes=>new[]{MainMenu.ScenePath,ConfrontationMenu.ScenePath,HeroSelection.ScenePath}.Concat(RunMaps.All.Select(m=>m.RunScene)).ToArray();
        public static bool Available(MenuStyle style)=>Application.CanStreamedLevelBeLoaded(PathFor(style));
        public static bool Enter(MenuStyle presented,GameObject owner)
        {
            // Old opt-in diagnostics explicitly exercise the showcase, without replacing the user's saved choice.
            var args=Environment.GetCommandLineArgs();
            if(RunAutoTest.Requested||MapPreviewCapture.Requested)return true;
            if(args.Contains("-lastbellMenuTest")||args.Contains("-lastbellFlowStage")||args.Contains("-lastbellShowcaseTest"))return true;
            if(!PlayerPrefs.HasKey(Key))Selected=presented;
            if(Selected==presented)return true;
            if(!Available(Selected)){Debug.LogWarning("Saved menu style is unavailable in this build.");return true;}
            owner.SetActive(false);
            SceneManager.LoadScene(ScenePath,LoadSceneMode.Single);
            return false;
        }
        public static void Switch(MenuStyle style)
        {
            if(!Available(style))return;
            Selected=style;
            if(SceneManager.GetActiveScene().path!=ScenePath)SceneManager.LoadScene(ScenePath,LoadSceneMode.Single);
        }
    }
}
