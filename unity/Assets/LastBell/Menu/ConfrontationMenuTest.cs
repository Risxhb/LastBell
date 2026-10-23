using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LastBell.Run;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LastBell.Menu
{
    // Explicit opt-in rendered checks. Never runs in an ordinary preview.
    public sealed class ConfrontationMenuTest : MonoBehaviour
    {
        public static bool Requested=>Environment.GetCommandLineArgs().Contains("-lastbellConfrontationTest");
        // Opt-in: framing captures and checks only, skipping scene transitions.
        static bool CaptureOnly=>Environment.GetCommandLineArgs().Contains("-lastbellConfrontationCaptureOnly");
        static bool launched;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Prepare(){if(Requested)MenuAppearance.Selected=MenuStyle.Alternative;}
        readonly List<string> report=new List<string>(),errors=new List<string>();
        int failures;
        string output,originalHero;
        Keyboard keyboard;Gamepad pad;
        InputSettings oldSettings,testSettings;
        IEnumerator Start()
        {
            if(launched){Destroy(this);yield break;}launched=true;
            var lifetime=new GameObject("Confrontation validation lifetime");DontDestroyOnLoad(lifetime);
            var runner=lifetime.AddComponent<ConfrontationMenuTestRunner>();runner.Owner=this;
            yield return null;runner.StartCoroutine(Run());
        }
        IEnumerator Run()
        {
            Application.logMessageReceived+=Log;Application.runInBackground=true;
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-lastbellOut");output=at>=0&&at+1<args.Length?args[at+1]:"E:/Collab/build-evidence/confrontation-menu-20261023/editor";Directory.CreateDirectory(output);
            originalHero=SavedHeroSelection.Current;
            oldSettings=InputSystem.settings;oldSettings.hideFlags=HideFlags.DontUnloadUnusedAsset;
            testSettings=Instantiate(oldSettings);testSettings.hideFlags=HideFlags.DontUnloadUnusedAsset;testSettings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            testSettings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            InputSystem.settings=testSettings;keyboard=InputSystem.AddDevice<Keyboard>();pad=InputSystem.AddDevice<Gamepad>();
            yield return new WaitForSecondsRealtime(3);
            var menu=FindAnyObjectByType<ConfrontationMenu>();
            EventSystem.current.GetComponent<InputSystemUIInputModule>().actionsAsset.devices=new InputDevice[]{keyboard,pad};
            Check("perspective fixed camera",!menu.View.orthographic&&Vector3.Distance(menu.View.transform.position,menu.CameraPosition)<.001f);
            Check("exact uniform double boss scale",menu.BossWrapper.localScale==Vector3.one*2&&menu.Boss.transform.localScale==Vector3.one);
            Check("authored boss and idle",menu.Boss.HasModel&&menu.Boss.Has("Idle"));
            Check("alternate logo sprite with alpha",Resources.Load<Sprite>("Menu/ConfrontationLogo")!=null);
            Check("no gameplay authority",!RunDirector.Instance&&!FindAnyObjectByType<BossEncounter>()&&!FindAnyObjectByType<PlayerController>());
            Check("single music owner",FindObjectsByType<MenuMusic>(FindObjectsSortMode.None).Length==1&&menu.Music.MenuSource.isPlaying);
            yield return Press(Key.DownArrow);Check("keyboard moves to Boss Rush",EventSystem.current.currentSelectedGameObject==menu.RushButton.gameObject);
            InputSystem.QueueStateEvent(pad,new GamepadState().WithButton(GamepadButton.DpadDown));yield return null;yield return null;InputSystem.QueueStateEvent(pad,new GamepadState());yield return null;yield return null;
            Check("controller moves to Settings",EventSystem.current.currentSelectedGameObject==menu.SettingsButton.gameObject);
            yield return Press(Key.Enter);Check("keyboard opens settings",menu.SettingsOpen&&!menu.StartButton.IsInteractable());
            menu.CloseSettings();Check("settings focus returns",!menu.SettingsOpen&&EventSystem.current.currentSelectedGameObject==menu.SettingsButton.gameObject);
            var source=menu.Music.MenuSource;float musicTime=source.time;
            foreach(var size in new[]{new Vector2Int(1600,900),new Vector2Int(1280,720),new Vector2Int(2560,1080),new Vector2Int(1024,768)})
            {
                MenuFlowTest.SetResolution(size.x,size.y);yield return new WaitForSecondsRealtime(1);
                Check("actual requested resolution "+size,Screen.width==size.x&&Screen.height==size.y);
                for(int i=0;i<RunDirector.Classes.Length;i++)
                {
                    menu.SelectClass(i);yield return new WaitForSecondsRealtime(.45f);
                    Check("selected class "+size+" "+i,menu.Hero.HasModel&&menu.Hero.Has("Idle")&&SavedHeroSelection.Current==RunDirector.Classes[i]);
                    Check("one active hero",menu.GetComponentsInChildren<CharacterPresenter>().Length==2);
                    var bones=menu.Hero.GetComponentsInChildren<SkinnedMeshRenderer>().SelectMany(r=>r.bones).Where(b=>b).Distinct().ToArray();
                    var rotations=bones.Select(b=>b.localRotation).ToArray();yield return new WaitForSecondsRealtime(.35f);
                    Check("idle bones animate "+i,bones.Where((b,j)=>Quaternion.Angle(rotations[j],b.localRotation)>.01f).Any());
                    Vector3 hero=menu.View.WorldToViewportPoint(menu.Hero.transform.parent.position+Vector3.up*1.4f),boss=menu.View.WorldToViewportPoint(menu.BossWrapper.position+Vector3.up*4);
                    report.Add("FRAME "+Screen.width+"x"+Screen.height+" hero="+hero+" boss="+boss);
                    Check("hero left / boss middle",hero.z>0&&hero.x>.03f&&hero.x<.43f&&boss.x>.32f&&boss.x<.68f);
                    var rect=(RectTransform)menu.StartButton.transform;var corners=new Vector3[4];rect.GetWorldCorners(corners);
                    Check("navigation fits screen",corners.All(p=>p.x>=0&&p.x<=Screen.width&&p.y>=0&&p.y<=Screen.height));
                    yield return Capture(RunDirector.Classes[i]+"-"+Screen.width+"x"+Screen.height+".png");
                }
            }
            Check("music continuous across class changes",menu.Music.MenuSource==source&&source.isPlaying&&source.time>musicTime);
            if(!CaptureOnly){
            menu.SelectClass(2);menu.BossRush();yield return SceneReady(()=>{var s=FindAnyObjectByType<HeroSelection>();return s&&s.Setup;});
            var selection=FindAnyObjectByType<HeroSelection>();Check("existing Boss Rush setup retains class",selection&&selection.Setup&&selection.Setup.Hero==RunDirector.Classes[2]);
            SceneManager.LoadScene(ConfrontationMenu.ScenePath);yield return SceneReady(()=>FindAnyObjectByType<ConfrontationMenu>()?.Hero!=null);
            menu=FindAnyObjectByType<ConfrontationMenu>();Check("class restored on scene return",menu.Hero&&SavedHeroSelection.Current==RunDirector.Classes[2]);
            menu.StartRun();yield return SceneReady(()=>RunDirector.Instance&&RunDirector.Instance.State==RunState.Ready);
            Check("Start Run retains selected class",RunDirector.Instance&&RunDirector.Instance.ClassName==RunDirector.Classes[2]&&!MenuLaunch.Pending);
            Check("preview actors unloaded",!FindAnyObjectByType<ConfrontationMenu>()&&!GameObject.Find("Bellwarden presentation — exact 2x"));
            if(RunDirector.Instance){RunDirector.Instance.SetPaused(true);RunDirector.Instance.ReturnToMenu();}yield return SceneReady(()=>FindAnyObjectByType<ConfrontationMenu>()?.Hero!=null);
            Check("gameplay returns only to selected alternative",MenuAppearance.Selected==MenuStyle.Alternative&&!FindAnyObjectByType<MainMenu>()&&SceneManager.sceneCount==1);
            for(int i=0;i<2;i++)
            {
                SceneManager.LoadScene(ConfrontationMenu.ScenePath);yield return SceneReady(()=>FindAnyObjectByType<ConfrontationMenu>()?.Hero!=null);
                menu=FindAnyObjectByType<ConfrontationMenu>();menu.SelectClass(i);menu.StartRun();yield return SceneReady(()=>RunDirector.Instance&&RunDirector.Instance.State==RunState.Ready);
                Check("run uses selected class "+RunDirector.Classes[i],RunDirector.Instance&&RunDirector.Instance.ClassName==RunDirector.Classes[i]);
                if(RunDirector.Instance){RunDirector.Instance.SetPaused(true);RunDirector.Instance.ReturnToMenu();}
                yield return SceneReady(()=>FindAnyObjectByType<ConfrontationMenu>()?.Hero!=null);
            }
            MenuFlowTest.SetResolution(1600,900);yield return new WaitForSecondsRealtime(1);
            menu=FindAnyObjectByType<ConfrontationMenu>();menu.OpenSettings();yield return Capture("alternative-settings.png");menu.UISettingsButton.onClick.Invoke();
            Check("alternative Settings has UI category",menu.UISettings.IsOpen&&menu.SettingsOpen&&!menu.StartButton.IsInteractable());
            yield return Capture("alternative-ui-settings.png");
            yield return Press(Key.Escape);Check("UI Escape returns to Settings",menu.SettingsOpen&&!menu.UISettings.IsOpen);
            menu.UISettingsButton.onClick.Invoke();menu.UISettings.ShowcaseButton.onClick.Invoke();
            yield return SceneReady(()=>FindAnyObjectByType<MainMenu>()?.StartButton!=null);
            var original=FindAnyObjectByType<MainMenu>();Check("UI switches and saves showcase",original&&MenuAppearance.Selected==MenuStyle.Showcase&&!FindAnyObjectByType<ConfrontationMenu>()&&SceneManager.sceneCount==1);
            original.OpenSettings();original.UISettingsButton.onClick.Invoke();Check("showcase Settings has UI category",original.UISettings.IsOpen&&original.SettingsOpen);
            yield return Capture("showcase-ui-settings.png");
            original.UISettings.BackButton.onClick.Invoke();original.CloseSettings();Check("original Settings remain usable",!original.SettingsOpen&&original.StartButton.IsInteractable());
            SceneManager.LoadScene(ConfrontationMenu.ScenePath);yield return SceneReady(()=>FindAnyObjectByType<MainMenu>()?.StartButton!=null);
            original=FindAnyObjectByType<MainMenu>();Check("saved showcase overrides alternate startup scene",original&&!FindAnyObjectByType<ConfrontationMenu>());
            original.OpenSettings();original.UISettingsButton.onClick.Invoke();original.UISettings.AlternativeButton.onClick.Invoke();
            yield return SceneReady(()=>FindAnyObjectByType<ConfrontationMenu>()?.Hero!=null);
            Check("UI switches and saves alternative",MenuAppearance.Selected==MenuStyle.Alternative&&!FindAnyObjectByType<MainMenu>()&&SceneManager.sceneCount==1);
            SceneManager.LoadScene(MainMenu.ScenePath);yield return SceneReady(()=>FindAnyObjectByType<ConfrontationMenu>()?.Hero!=null);
            Check("saved alternative overrides showcase startup scene",!FindAnyObjectByType<MainMenu>()&&SceneManager.sceneCount==1);
            Check("one persistent audio owner after navigation",FindObjectsByType<MenuMusic>(FindObjectsSortMode.None).Length==1);
            }
            SavedHeroSelection.Current=originalHero;InputSystem.settings=oldSettings;InputSystem.RemoveDevice(keyboard);InputSystem.RemoveDevice(pad);Destroy(testSettings);
            Check("zero runtime errors",errors.Count==0);report.AddRange(errors);report.Add("RESULT "+(report.Count(s=>s.StartsWith("PASS ")))+" passed / "+failures+" failed");
            File.WriteAllLines(Path.Combine(output,"report.txt"),report);Application.logMessageReceived-=Log;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(failures==0?0:1);
#else
            Application.Quit(failures==0?0:1);
#endif
        }
        IEnumerator SceneReady(Func<bool> ready){float end=Time.realtimeSinceStartup+45;while(!ready()&&Time.realtimeSinceStartup<end)yield return null;yield return new WaitForSecondsRealtime(1);Check("scene transition completed",ready());}
        IEnumerator Press(Key key){InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));yield return null;yield return null;InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;yield return null;}
        IEnumerator Capture(string name){yield return new WaitForEndOfFrame();var t=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0);t.Apply();File.WriteAllBytes(Path.Combine(output,name),t.EncodeToPNG());Destroy(t);}
        void Check(string name,bool pass){report.Add((pass?"PASS ":"FAIL ")+name);if(!pass)failures++;Debug.Log(report[report.Count-1]);}
        void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+stack);}
    }
    public sealed class ConfrontationMenuTestRunner:MonoBehaviour {public ConfrontationMenuTest Owner;}
}
