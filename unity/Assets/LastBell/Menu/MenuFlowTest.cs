using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using LastBell.Run;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace LastBell.Menu
{
    public sealed class MenuFlowTest : MonoBehaviour
    {
        static bool installed;
        public static bool Requested=>Environment.GetCommandLineArgs().Contains("-lastbellFlowStage");
        readonly List<string> report=new List<string>(); int failed;
        readonly List<string> errors=new List<string>();
        string output;InputSettings originalInput,testInput;
        HideFlags originalInputFlags;
        public static void Install()
        {
            if(installed || !Environment.GetCommandLineArgs().Contains("-lastbellFlowStage")) return;
            installed=true; var go=new GameObject("Menu flow validation"); DontDestroyOnLoad(go); go.AddComponent<MenuFlowTest>();
        }
        void Check(string name,bool ok) { report.Add((ok?"PASS ":"FAIL ")+name); if(!ok) failed++; Debug.Log(report.Last()); }
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();int stage=int.Parse(args[Array.IndexOf(args,"-lastbellFlowStage")+1]);
            int outputAt=Array.IndexOf(args,"-lastbellOut");output=outputAt>=0&&outputAt+1<args.Length?args[outputAt+1]:"E:/Collab/build-evidence/menu-flow-20261022";Directory.CreateDirectory(output);
            Application.logMessageReceived+=Log;originalInput=InputSystem.settings;originalInputFlags=originalInput.hideFlags;originalInput.hideFlags=HideFlags.DontUnloadUnusedAsset;
            testInput=Instantiate(originalInput);testInput.hideFlags=HideFlags.DontUnloadUnusedAsset;testInput.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings=testInput;
#if UNITY_EDITOR
            testInput.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            if(stage>=8)SetResolution(1920,1080);
            Application.runInBackground=true; yield return new WaitForSeconds(2);
            var menu=FindAnyObjectByType<MainMenu>();
            Check("menu is first build scene",SceneUtility.GetScenePathByBuildIndex(0)==MainMenu.ScenePath);
            Check("four primary actions",menu.StartButton && menu.TestRushButton && menu.SettingsButton && menu.ExitButton);
            Check("keyboard focus starts at Start Run",EventSystem.current.currentSelectedGameObject==menu.StartButton.gameObject);
            Check("explicit navigation reaches test setup",menu.StartButton.navigation.selectOnRight==menu.TestRushButton && menu.TestRushButton.navigation.selectOnRight==menu.SettingsButton);
            if(stage>=8)
            {
                yield return Press(Key.RightArrow);Check("keyboard right selects Test Rush",EventSystem.current.currentSelectedGameObject==menu.TestRushButton.gameObject);
                yield return Press(Key.RightArrow);Check("keyboard right selects Settings",EventSystem.current.currentSelectedGameObject==menu.SettingsButton.gameObject);
                yield return Press(Key.Enter);Check("keyboard submit opens Settings",menu.SettingsOpen);
                yield return Press(Key.Escape);Check("keyboard Escape closes Settings",!menu.SettingsOpen);
            }
            menu.OpenSettings(); Check("settings and music preserved",menu.SettingsOpen && menu.MusicVolumeControl && menu.Music.Source.isPlaying);
            menu.CloseSettings(); Check("settings Back restores focus",!menu.SettingsOpen && EventSystem.current.currentSelectedGameObject==menu.SettingsButton.gameObject);
            for(int i=0;i<2;i++)
            {
                var menuSource=menu.Music.Source;int menuSample=menuSource.timeSamples;
                menu.OpenSelection(i==1); yield return new WaitForSeconds(2);
                var selection=FindAnyObjectByType<HeroSelection>();
                Check("selection path "+i,selection && HeroSelection.TestRush==(i==1));
                MusicCheck("hero selection "+i,"MainMenuMusic");
                Check("menu music survives scene without restart "+i,FindAnyObjectByType<MenuMusic>().Source==menuSource&&menuSource.timeSamples>menuSample);
                if(stage>=3 && selection)
                {
                    Check("all three real idle models",selection.Presenters.Count==RunDirector.Classes.Length && selection.Presenters.All(p=>p.HasModel&&p.Has("Idle")));
                    Check("decorative heroes have no combat controllers",!FindAnyObjectByType<PlayerController>()&&!RunDirector.Instance&&!FindAnyObjectByType<BossEncounter>());
                    Check("one camera listener and event system",FindObjectsByType<Camera>().Count(c=>c.enabled)==1&&FindObjectsByType<AudioListener>().Count(a=>a.enabled)==1&&FindObjectsByType<EventSystem>().Length==1);
                    yield return Shot(stage>=8?(i==0?"hero-group-normal.png":"hero-group-test.png"):"hero-group-stage"+stage+".png");
                    if(stage>=8)
                    {
                        foreach(var device in MenuSmokeTest.TestDevices)if(!device.enabled)InputSystem.EnableDevice(device);
                        yield return null;yield return null;
                        for(int pick=0;pick<selection.HeroButtons.Length;pick++)
                        {
                        yield return new WaitForSeconds(1);
                        var point=selection.View.WorldToScreenPoint(selection.ModelBounds(pick).center);
                        InputSystem.QueueStateEvent(MenuSmokeTest.TestMouse,new MouseState{position=point});yield return null;yield return null;
                        InputSystem.QueueStateEvent(MenuSmokeTest.TestMouse,new MouseState{position=point}.WithButton(MouseButton.Left));yield return null;
                        InputSystem.QueueStateEvent(MenuSmokeTest.TestMouse,new MouseState{position=point});yield return null;yield return null;
                        Check("clicking 3D hero "+pick+" selects it",selection.SelectedIndex==pick);report.Add("INPUT "+selection.LastPick+" mouse enabled="+MenuSmokeTest.TestMouse.enabled+" keyboard enabled="+MenuSmokeTest.TestKeyboard.enabled);if(selection.SelectedIndex>=0)selection.Back();
                        }
                        EventSystem.current.SetSelectedGameObject(selection.HeroButtons[0].gameObject);
                        yield return Press(Key.RightArrow);yield return Press(Key.Enter);Check("keyboard chooses Chimesinger",selection.SelectedIndex==1);report.Add("FOCUS "+EventSystem.current.currentSelectedGameObject?.name);if(selection.SelectedIndex>=0)selection.Back();
                        Integrity("hero scene");
                    }
                    for(int hero=0;hero<selection.HeroButtons.Length;hero++){selection.HeroButtons[hero].onClick.Invoke();Check("select hero "+hero,selection.SelectedIndex==hero);}
                    if(stage==4 || stage>=8)
                    {
                        foreach(var size in new[]{new Vector2Int(1280,720),new Vector2Int(1920,1080),new Vector2Int(2560,1080)})
                        {
                            SetResolution(size.x,size.y);yield return new WaitForSeconds(1);
                            Check("actual render resolution "+size,Screen.width==size.x&&Screen.height==size.y);
                            for(int hero=0;hero<selection.HeroButtons.Length;hero++)
                            {
                                selection.SelectHero(hero);yield return new WaitForSeconds(1.5f);
                                report.Add("FRAME "+hero+" "+size+" bounds="+selection.ModelBounds(hero)+" camera="+selection.View.transform.position+" aspect="+selection.View.aspect);
                                Check("full body "+hero+" "+size,selection.ModelCorners(hero).All(c=>{var p=selection.View.WorldToViewportPoint(c);var r=selection.PresentationRect;return p.z>0&&p.x>r.xMin+.005f&&p.x<r.xMax-.005f&&p.y>r.yMin+.005f&&p.y<r.yMax-.005f;}));
                                var k=HeroSelection.KitFor(RunDirector.Classes[hero]);
                                Check("movement icon sourced from HUD "+hero,selection.DashIcon&&selection.DashIcon.texture==RunHud.SlotIcon(RunDirector.Classes[hero],k.SlotNames.Length));
                                Check("implemented skills "+hero,selection.SkillDescriptions.Length==k.SlotNames.Length&&selection.SkillDescriptions.Select(t=>t.text).SequenceEqual(Enumerable.Range(0,k.SlotNames.Length).Select(j=>HeroSelection.Description(k,j))));
                                Canvas.ForceUpdateCanvases();Check("skill descriptions fit "+hero+" "+size,selection.SkillDescriptions.All(t=>t.preferredHeight<=t.rectTransform.rect.height+1));
                                if(i==0)yield return Shot("selected-"+RunDirector.Classes[hero]+"-"+size.x+"x"+size.y+".png");
                            }
                        }
                        selection.SelectHero(0);selection.SelectHero(2);selection.SelectHero(1);yield return new WaitForSeconds(1.5f);
                        Check("rapid switching finishes on latest hero",selection.SelectedIndex==1&&selection.DisplayKit is ChimesingerKit);
                    }
                    selection.Back();Check("Back restores group",selection.SelectedIndex==-1);
                    if(stage>=6 && i==1)
                    {
                        selection.SelectHero(0);selection.ConfirmClass();yield return null;var setup=selection.Setup;
                        Check("dedicated setup opens",setup&&setup.MapButtons.Length==RunMaps.All.Length&&setup.EncounterButtons.Length==RunDirector.Bosses.Length);
                        MusicCheck("test setup","MainMenuMusic");
                        if(stage>=8)
                        {
                            Check("setup excludes background hero controls",selection.HeroButtons.All(b=>!b.gameObject.activeInHierarchy));
                            EventSystem.current.SetSelectedGameObject(setup.MapButtons[0].gameObject);yield return Press(Key.RightArrow);yield return Press(Key.Enter);
                            Check("setup keyboard changes map",setup.MapIndex==1);
                            foreach(var key in new[]{Key.UpArrow,Key.RightArrow,Key.DownArrow,Key.LeftArrow}){yield return Press(key);Check("setup focus stays in active panel "+key,setup.OwnsFocus);}
                        }
                        for(int map=0;map<RunMaps.All.Length;map++)
                        {
                            setup.SelectMap(map);Check("current preview "+map,TestRushSetup.Preview(map)&&TestRushSetup.Preview(map).width==960&&TestRushSetup.Preview(map).height==540);
                            for(int enc=0;enc<RunDirector.Bosses.Length;enc++)
                            {setup.SelectEncounter(enc);Check("setup summary "+map+"/"+enc,setup.Summary.text.Contains(RunMaps.All[map].Name)&&setup.Summary.text.Contains(RunDirector.Bosses[enc].Name)&&setup.Summary.text.Contains(RunDifficulty.Name(RunDifficulty.Selected))&&setup.Valid);}
                        }
                        Check("invalid selection rejected",TestRushSetup.Incompatibility(-1,0)!=null&&TestRushSetup.Incompatibility(0,999)!=null);
                        yield return Shot("test-rush-setup-stage"+stage+".png");setup.Close();yield return null;
                        Check("setup Back restores selected hero",!selection.Setup&&selection.SelectedIndex==0);
                        if(stage>=8)Check("setup restores hero controls",selection.HeroButtons.All(b=>b.gameObject.activeInHierarchy));
                        selection.Back();
                    }
                }
                if(selection) selection.Back(); yield return new WaitForSeconds(2); menu=FindAnyObjectByType<MainMenu>();
                Check("Back returns to main menu "+i,menu && !FindAnyObjectByType<HeroSelection>());
            }
            if(stage>=5)
            {
                var permutations=Enumerable.Range(0,128).Select(seed=>RunDirector.CreateRunOrder(new System.Random(seed))).ToArray();
                Check("all sampled orders contain each boss once and Bellkeeper last",permutations.All(order=>order.Length==4&&order.Distinct().Count()==4&&RunDirector.Bosses[order.Last()].Final));
                Check("opening sequence is randomized",permutations.Select(order=>string.Join(",",order)).Distinct().Count()==6);
                var original=RunDifficulty.Selected;
                for(int hero=0;hero<RunDirector.Classes.Length;hero++)
                {
                    menu.OpenSelection(false);yield return new WaitForSeconds(2);var s=FindAnyObjectByType<HeroSelection>();
                    RunDifficulty.Selected=(Difficulty)hero;s.SelectHero(hero);s.ConfirmClass();s.ConfirmClass();
                    yield return new WaitForSeconds(2);var d=RunDirector.Instance;
                    Check("normal launch class/difficulty "+hero,d&&d.ClassName==RunDirector.Classes[hero]&&d.Difficulty==(Difficulty)hero&&d.Player.Presenter.HasModel);
                    Check("fresh Ready and authoritative first boss "+hero,d&&d.State==RunState.Ready&&d.Encounter==d.EncounterOrder[0]&&d.Mode==RunMode.Run&&d.RunsStarted==1&&d.RunTicks==0);
                    Check("launch consumed once "+hero,!MenuLaunch.Pending&&d&&d.RunsStarted==1);
                    MusicCheck("normal Ready "+hero,"MainMenuMusic");
                    if(d)
                    {
                        var order=(int[])d.EncounterOrder.Clone();
                        Check("normal session has four bosses and final Bellkeeper "+hero,order.Length==4&&order.Distinct().Count()==4&&RunDirector.Bosses[order.Last()].Final);
                        Check("normal initial map matches first boss "+hero,RunMaps.All[RunMaps.Current].Id==(RunDirector.Bosses[order[0]].Map??"test"));
                        d.RequestReady();d.RequestReady();MusicCheck("normal Intro "+hero,"MainMenuMusic");yield return new WaitForSeconds(2.5f);Check("intro starts one encounter "+hero,d.State==RunState.Combat&&d.EncounterStarts==1);
                        for(int slot=0;slot<order.Length;slot++)
                        {
                            Check("normal sequence slot "+hero+"/"+slot,d.State==RunState.Combat&&d.Encounter==order[slot]&&d.ProgressSlot==slot&&d.EncounterStarts==slot+1);
                            MusicCheck("normal combat "+hero+"/"+slot,ExpectedMusic(order[slot]));
                            d.Boss.DebugDefeat();yield return new WaitForSeconds(slot==order.Length-1?3.5f:5.5f);
                        }
                        Check("normal victory only after final Bellkeeper "+hero,d.State==RunState.Results&&d.Victory&&d.EncountersCleared==4&&d.EncounterEnds==4&&d.Boss==null);
                        Check("normal retry preserves randomized order "+hero,d.RequestRetry()&&d.State==RunState.Ready&&d.Encounter==order[0]&&d.EncounterOrder.SequenceEqual(order)&&d.EncountersCleared==0&&d.RunsStarted==2);
                        MusicCheck("normal retry "+hero,"MainMenuMusic");d.SetPaused(true);d.ReturnToMenu();
                    }
                    yield return new WaitForSeconds(2);menu=FindAnyObjectByType<MainMenu>();Check("normal return cleans selection "+hero,menu&&!FindAnyObjectByType<HeroSelection>());
                }
                RunDifficulty.Selected=original;
            }
            if(stage>=7&&!args.Contains("-lastbellFlowQuick"))
            {
                var original=RunDifficulty.Selected;
                for(int map=0;map<RunMaps.All.Length;map++)for(int enc=0;enc<RunDirector.Bosses.Length;enc++)
                {
                    menu.OpenSelection(true);yield return SceneReady(()=>{var heroView=FindAnyObjectByType<HeroSelection>();return heroView&&heroView.HeroButtons!=null;});var selection=FindAnyObjectByType<HeroSelection>();
                    int hero=(map+enc)%RunDirector.Classes.Length;RunDifficulty.Selected=(Difficulty)hero;
                    selection.SelectHero(hero);selection.ConfirmClass();yield return null;var setup=selection.Setup;
                    setup.SelectMap(map);setup.SelectEncounter(enc);setup.Launch();setup.Launch();
                    yield return SceneReady(()=>RunDirector.Instance&&RunDirector.Instance.State==RunState.Ready);var d=RunDirector.Instance;string tag=map+"/"+enc;
                    Check("test honors map and encounter "+tag,d&&RunMaps.Current==map&&d.Encounter==enc&&d.Mode==RunMode.Practice);
                    Check("test honors hero and difficulty "+tag,d&&d.ClassName==RunDirector.Classes[hero]&&d.Difficulty==(Difficulty)hero&&d.RunsStarted==1&&!MenuLaunch.Pending);
                    if(d)
                    {
                        MusicCheck("test Ready "+tag,"MainMenuMusic");
                        d.RequestReady();MusicCheck("test Intro "+tag,"MainMenuMusic");yield return new WaitForSeconds(4.5f);
                        Check("test encounter starts once "+tag,d.EncounterStarts==1&&d.Boss&&d.State==RunState.Combat);
                        var expected = ExpectedMusic(enc);
                        MusicCheck("test combat "+tag,expected);
                        bool hadVolume=PlayerPrefs.HasKey("LastBell.Menu.MusicVolume");var music=FindAnyObjectByType<MenuMusic>();float volume=music.Volume;
                        music.SetVolume(0);Check("boss music respects mute "+tag,music.Source.volume==0);
                        music.SetVolume(.25f);Check("boss music respects volume "+tag,Mathf.Abs(music.Source.volume-.25f)<.01f);
                        music.SetVolume(volume);if(!hadVolume)PlayerPrefs.DeleteKey("LastBell.Menu.MusicVolume");
                        if(stage>=8)Integrity("test "+tag);
                        if(enc==0)yield return Shot("test-map-"+RunMaps.All[map].Id+".png");
                        d.SetPaused(true);Check("retry accepted "+tag,d.RequestRetry());yield return null;
                        Check("retry keeps setup "+tag,d.State==RunState.Ready&&d.Encounter==enc&&RunMaps.Current==map&&d.Difficulty==(Difficulty)hero&&d.ClassName==RunDirector.Classes[hero]&&d.RunsStarted==2);
                        MusicCheck("retry preparation "+tag,"MainMenuMusic");
                        d.SetPaused(true);MenuReturnBridge.Return(true);MenuReturnBridge.Return(true);yield return SceneReady(()=>{var heroView=FindAnyObjectByType<HeroSelection>();return heroView&&heroView.Setup;});
                        selection=FindAnyObjectByType<HeroSelection>();
                        Check("return restores test setup "+tag,selection&&selection.Setup&&selection.Setup.MapIndex==map&&selection.Setup.EncounterIndex==enc&&selection.Setup.Hero==RunDirector.Classes[hero]);
                        Check("test transition cleanup "+tag,!RunDirector.Instance&&!FindAnyObjectByType<PlayerController>()&&FindObjectsByType<AudioListener>().Count(a=>a.enabled)==1&&FindObjectsByType<EventSystem>().Length==1);
                        if(selection&&selection.Setup){selection.Setup.Close();yield return null;selection.Back();selection.Back();}
                    }
                    yield return SceneReady(()=>{var main=FindAnyObjectByType<MainMenu>();return main&&main.StartButton&&!main.Loading;});menu=FindAnyObjectByType<MainMenu>();Check("test returns to main menu "+tag,menu);
                }
                RunDifficulty.Selected=original;
            }
            if(originalInput){InputSystem.settings=originalInput;originalInput.hideFlags=originalInputFlags;}else Check("original input settings retained",false);
            Destroy(testInput);yield return null;
            Check("zero runtime errors",errors.Count==0);report.AddRange(errors);
            report.Add($"RESULT: {report.Count(s=>s.StartsWith("PASS"))} passed, {failed} failed");
            File.WriteAllLines(Path.Combine(output,"stage"+stage+".txt"),report);
            Application.logMessageReceived-=Log;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(failed==0?0:1);
#else
            Application.Quit(failed==0?0:1);
#endif
        }
        IEnumerator SceneReady(Func<bool> ready)
        {
            float deadline=Time.realtimeSinceStartup+30;
            while(!ready()&&Time.realtimeSinceStartup<deadline)yield return null;
            if(!ready())Check("scene ready before timeout",false);
            yield return null;
        }
        IEnumerator Shot(string file)
        {
            foreach(var label in FindObjectsByType<UnityEngine.UI.Text>())label.SetAllDirty();Canvas.ForceUpdateCanvases();yield return null;
            yield return new WaitForEndOfFrame();
            var t=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0);t.Apply();
            if(file.StartsWith("selected-")||file.StartsWith("hero-group-"))
            {
                var pixels=t.GetPixels32();int header=0,skills=0;
                for(int y=(int)(t.height*.935f);y<t.height*.985f;y++)for(int x=(int)(t.width*.1f);x<t.width*.9f;x++)
                {var c=pixels[y*t.width+x];if(c.r>150&&c.g>110&&c.b>50&&c.r>c.b*1.4f)header++;}
                Check("rendered header text "+file,header>50);
                if(file.StartsWith("selected-"))
                {for(int y=(int)(t.height*.12f);y<t.height*.34f;y++)for(int x=(int)(t.width*.03f);x<t.width*.97f;x++){var c=pixels[y*t.width+x];if(c.r>160&&c.g>160&&c.b>140&&Mathf.Abs(c.r-c.g)<25)skills++;}Check("rendered skill text "+file,skills>100);}
            }
            File.WriteAllBytes(Path.Combine(output,file),t.EncodeToPNG());Destroy(t);
        }
        static string ExpectedMusic(int encounter) => new[]{"The Unfinished Hour","Crown of the Meridian","Where the Last Flower Falls","Pressure Beneath the Brass"}[encounter];
        void MusicCheck(string label,string clip)
        {
            var owners=FindObjectsByType<MenuMusic>();var music=owners.Length==1?owners[0]:null;
            bool opening=clip=="MainMenuMusic"&&music&&music.PlayingOpening;
            Check("music "+label,music&&music.Source&&music.Source.clip&&music.Source.clip.name==(opening?"MainMenuOpening":clip)&&music.Source.isPlaying&&(opening?!music.Source.loop:music.Source.loop));
        }
        IEnumerator Press(Key key)
        {InputSystem.QueueStateEvent(MenuSmokeTest.TestKeyboard,new KeyboardState(key));yield return null;yield return null;InputSystem.QueueStateEvent(MenuSmokeTest.TestKeyboard,new KeyboardState());yield return null;yield return null;}
        void Log(string message,string stack,LogType type)
        {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+stack);}
        void Integrity(string label)
        {
            Check("no missing scripts "+label,!Resources.FindObjectsOfTypeAll<GameObject>().Where(g=>g.scene.IsValid()&&g.scene.isLoaded).Any(g=>g.GetComponents<MonoBehaviour>().Any(b=>b==null)));
            Check("no error or unsupported materials "+label,FindObjectsByType<Renderer>().SelectMany(r=>r.sharedMaterials).Where(m=>m).All(m=>m.shader&&m.shader.isSupported&&m.shader.name!="Hidden/InternalErrorShader"));
        }
        public static void SetResolution(int w,int h)
        {
#if UNITY_EDITOR
            var asm=typeof(UnityEditor.Editor).Assembly;
            var sizesType=asm.GetType("UnityEditor.GameViewSizes");
            var singleton=typeof(UnityEditor.ScriptableSingleton<>).MakeGenericType(sizesType);
            var sizes=singleton.GetProperty("instance").GetValue(null);
            var group=sizesType.GetMethod("GetGroup").Invoke(sizes,new object[]{0});
            var sizeType=asm.GetType("UnityEditor.GameViewSize");
            var fixedType=System.Enum.ToObject(asm.GetType("UnityEditor.GameViewSizeType"),1);
            var size=System.Activator.CreateInstance(sizeType,new object[]{fixedType,w,h,"Last Bell validation "+w+"x"+h});
            group.GetType().GetMethod("AddCustomSize").Invoke(group,new[]{size});
            int count=(int)group.GetType().GetMethod("GetTotalCount").Invoke(group,null);
            var game=UnityEditor.EditorWindow.GetWindow(asm.GetType("UnityEditor.GameView"));
            game.GetType().GetProperty("selectedSizeIndex").SetValue(game,count-1);game.Focus();
#else
            Screen.SetResolution(w,h,FullScreenMode.Windowed);
#endif
        }
    }
}
