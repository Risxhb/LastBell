using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LastBell.Menu
{
    // Separate-process restart check: deliberately does not seed or change the saved style.
    public sealed class MenuAppearanceProbe:MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if(!Environment.GetCommandLineArgs().Contains("-lastbellMenuPreferenceProbe"))return;
            var go=new GameObject("Menu preference restart probe");DontDestroyOnLoad(go);go.AddComponent<MenuAppearanceProbe>();
        }
        IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(4);
            bool pass=MenuAppearance.Selected==MenuStyle.Alternative&&SceneManager.GetActiveScene().path==ConfrontationMenu.ScenePath&&FindAnyObjectByType<ConfrontationMenu>()?.Hero!=null&&!FindAnyObjectByType<MainMenu>()&&SceneManager.sceneCount==1&&FindObjectsByType<MenuMusic>().Length==1;
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-lastbellOut");var output=at>=0?args[at+1]:Application.persistentDataPath;Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output,"restart-probe.txt"),(pass?"PASS":"FAIL")+" saved alternative restored on fresh process; scene="+SceneManager.GetActiveScene().path+"; style="+MenuAppearance.Selected);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(pass?0:1);
#else
            Application.Quit(pass?0:1);
#endif
        }
    }
}
