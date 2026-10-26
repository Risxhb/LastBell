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
        MenuStyle originalMenuStyle;
        bool menuPreferenceCaptured;
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
            originalMenuStyle=MenuAppearance.Selected;menuPreferenceCaptured=true;
            Application.logMessageReceived+=Log;originalInput=InputSystem.settings;originalInputFlags=originalInput.hideFlags;originalInput.hideFlags=HideFlags.DontUnloadUnusedAsset;
            testInput=Instantiate(originalInput);testInput.hideFlags=HideFlags.DontUnloadUnusedAsset;testInput.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings=testInput;
#if UNITY_EDITOR
            testInput.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            if(stage>=8)SetResolution(1920,1080);
            Application.runInBackground=true; yield return new WaitForSeconds(2);
            // The Confrontation menu is the only main menu; the Showcase scene is the startup router and Boss Rush backdrop.
            yield return SceneReady(()=>{var m=FindAnyObjectByType<ConfrontationMenu>();return m&&m.Canvas;});
            var menu=FindAnyObjectByType<ConfrontationMenu>();
            Check("startup routes to the only main menu",menu&&!FindAnyObjectByType<MainMenu>()&&SceneUtility.GetScenePathByBuildIndex(0)==MainMenu.ScenePath);
            Check("old hero select is not in the build",!Application.CanStreamedLevelBeLoaded(HeroSelection.ScenePath));
            Check("four primary actions",menu.StartButton && menu.RushButton && menu.SettingsButton && menu.ExitButton);
            Check("keyboard focus starts at Start Run",EventSystem.current.currentSelectedGameObject==menu.StartButton.gameObject);
            if(stage>=8)
            {
                yield return Press(Key.DownArrow);Check("keyboard down selects Boss Rush",EventSystem.current.currentSelectedGameObject==menu.RushButton.gameObject);
                yield return Press(Key.DownArrow);Check("keyboard down selects Settings",EventSystem.current.currentSelectedGameObject==menu.SettingsButton.gameObject);
                yield return Press(Key.Enter);Check("keyboard submit opens Settings",menu.SettingsOpen);
                yield return Press(Key.Escape);Check("keyboard Escape closes Settings",!menu.SettingsOpen);
            }
            menu.OpenSettings(); Check("settings and music preserved",menu.SettingsOpen && menu.MusicVolumeControl && menu.Music.Source.isPlaying);
            menu.CloseSettings(); Check("settings Back restores focus",!menu.SettingsOpen && EventSystem.current.currentSelectedGameObject==menu.SettingsButton.gameObject);
            if(stage>=6)
            {
                var menuSource=menu.Music.Source;
                menu.SelectClass(0);menu.BossRush();yield return null;var setup=menu.Rush;
                Check("Boss Rush opens on the main menu",setup&&!setup.BossStep&&setup.MapButtons.Length==RunMaps.All.Length&&setup.EncounterButtons.Length==RunDirector.Bosses.Length&&!FindAnyObjectByType<HeroSelection>());
                MusicCheck("test setup","MainMenuMusic");
                Check("menu music survives into Boss Rush",FindAnyObjectByType<MenuMusic>().Source==menuSource&&menuSource.isPlaying);
                if(stage>=8)
                {
                    Check("Boss Rush suspends the menu controls",!menu.StartButton.IsInteractable()&&!menu.RushButton.IsInteractable());
                    EventSystem.current.SetSelectedGameObject(setup.MapButtons[0].gameObject);yield return Press(Key.RightArrow);yield return Press(Key.Enter);
                    Check("setup keyboard changes map",setup.MapIndex==1);
                    foreach(var key in new[]{Key.UpArrow,Key.RightArrow,Key.DownArrow,Key.LeftArrow}){yield return Press(key);Check("setup focus stays in active panel "+key,setup.OwnsFocus);}
                }
                for(int map=0;map<RunMaps.All.Length;map++){setup.SelectMap(map);Check("current preview "+map,TestRushSetup.Preview(map)&&TestRushSetup.Preview(map).width==960&&TestRushSetup.Preview(map).height==540);}
                setup.Continue();Check("Continue opens the boss step",setup.BossStep&&setup.EncounterButtons[0].gameObject.activeInHierarchy&&!setup.MapButtons[0].gameObject.activeInHierarchy);
                for(int map=0;map<RunMaps.All.Length;map++)
                {
                    setup.SelectMap(map);
                    for(int enc=0;enc<RunDirector.Bosses.Length;enc++)
                    {setup.SelectEncounter(enc);Check("setup selection "+map+"/"+enc,setup.EncounterIndex==enc&&setup.MapIndex==map&&setup.EncounterButtons[enc].gameObject.activeInHierarchy&&setup.Valid);}
                }
                Check("boss step rows: Bosses (Bellkeeper last) and Minibosses (Chimebound Oracle)",TestRushSetup.BossRow.Length+TestRushSetup.MinibossRow.Length==RunDirector.Bosses.Length&&RunDirector.Bosses[TestRushSetup.BossRow[TestRushSetup.BossRow.Length-1]].Final&&TestRushSetup.MinibossRow.Length>=1&&RunDirector.Bosses[TestRushSetup.MinibossRow[0]].Name.StartsWith("Chimebound Oracle"));
                Check("invalid selection rejected",TestRushSetup.Incompatibility(-1,0)!=null&&TestRushSetup.Incompatibility(0,999)!=null);
                setup.Back();Check("Back returns to the arena step",!setup.BossStep);
                setup.SelectMap(1);float liveUntil=Time.realtimeSinceStartup+12;while(!setup.LiveBackdrop&&Time.realtimeSinceStartup<liveUntil)yield return null;
                Check("setup shows live showcase arena",setup.LiveBackdrop);Check("setup hides the menu stage",menu.StageHidden&&!menu.View.enabled);yield return new WaitForSeconds(1);
                yield return Shot("test-rush-setup-stage"+stage+".png");
                yield return MetronomeCourtBackdrop(setup);
                setup.SelectMap(1);liveUntil=Time.realtimeSinceStartup+12;while(!setup.LiveBackdrop&&Time.realtimeSinceStartup<liveUntil)yield return null;
                setup.Continue();yield return new WaitForSeconds(.5f);yield return Shot("test-rush-boss-stage"+stage+".png");
                setup.Back();setup.Back();yield return new WaitForSeconds(1.5f);
                Check("setup Back restores the menu stage",!menu.Rush&&!menu.StageHidden&&menu.View.enabled&&menu.StartButton.IsInteractable());
                Check("setup Back unloads the backdrop",SceneManager.sceneCount==1);
                Integrity("main menu after Boss Rush");
            }
            if(stage>=5)
            {
                var permutations=Enumerable.Range(0,128).Select(seed=>RunDirector.CreateRunOrder(new System.Random(seed))).ToArray();
                Check("all sampled orders contain each boss once and Bellkeeper last",permutations.All(order=>order.Length==4&&order.Distinct().Count()==4&&RunDirector.Bosses[order.Last()].Final));
                Check("opening sequence is randomized",permutations.Select(order=>string.Join(",",order)).Distinct().Count()==6);
                var original=RunDifficulty.Selected;
                for(int hero=0;hero<RunDirector.Classes.Length;hero++)
                {
                    menu=FindAnyObjectByType<ConfrontationMenu>();
                    RunDifficulty.Selected=(Difficulty)hero;menu.SelectClass(hero);menu.StartRun();menu.StartRun();
                    yield return SceneReady(()=>RunDirector.Instance&&RunDirector.Instance.State==RunState.Ready);var d=RunDirector.Instance;
                    Check("normal launch class/difficulty "+hero,d&&d.ClassName==RunDirector.Classes[hero]&&d.Difficulty==(Difficulty)hero&&d.Player.Presenter.HasModel);
                    Check("fresh Ready and authoritative first boss "+hero,d&&d.State==RunState.Ready&&d.Encounter==d.EncounterOrder[0]&&d.Mode==RunMode.Run&&d.RunsStarted==1&&d.RunTicks==0);
                    Check("launch consumed once "+hero,!MenuLaunch.Pending&&d&&d.RunsStarted==1);
                    MusicCheck("normal Ready "+hero,"MainMenuMusic");
                    if(d)
                    {
                        var order=(int[])d.EncounterOrder.Clone();
                        Check("normal session has four bosses and final Bellkeeper "+hero,order.Length==4&&order.Distinct().Count()==4&&RunDirector.Bosses[order.Last()].Final);
                        Check("normal initial map matches first boss "+hero,RunMaps.All[RunMaps.Current].Id==(RunDirector.Bosses[order[0]].Map??"test"));
                        d.RequestReady();d.RequestReady();MusicCheck("normal Intro "+hero,ExpectedMusic(order[0]));
                        if(stage>=8&&hero==0){yield return new WaitForSeconds(1.0f);yield return Shot("intro-"+RunDirector.Bosses[order[0]].Name.Split(' ')[0].ToLowerInvariant()+".png");yield return new WaitForSeconds(1.5f);}
                        else yield return new WaitForSeconds(2.5f);Check("intro starts one encounter "+hero,d.State==RunState.Combat&&d.EncounterStarts==1);
                        for(int slot=0;slot<order.Length;slot++)
                        {
                            Check("normal sequence slot "+hero+"/"+slot,d.State==RunState.Combat&&d.Encounter==order[slot]&&d.ProgressSlot==slot&&d.EncounterStarts==slot+1);
                            MusicCheck("normal combat "+hero+"/"+slot,ExpectedMusic(order[slot]));
                            d.Boss.DebugDefeat();
                            if(stage>=8&&hero==0){yield return new WaitForSeconds(1.2f);yield return Shot("cleared-"+RunDirector.Bosses[order[slot]].Name.Split(' ')[0].ToLowerInvariant()+".png");
                                if(slot<order.Length-1){yield return new WaitForSeconds(2.2f);yield return Shot("intro-"+RunDirector.Bosses[order[slot+1]].Name.Split(' ')[0].ToLowerInvariant()+".png");yield return new WaitForSeconds(2.0f);}else yield return new WaitForSeconds(2.2f);}
                            else yield return new WaitForSeconds(slot==order.Length-1?3.5f:5.5f);
                        }
                        Check("normal victory only after final Bellkeeper "+hero,d.State==RunState.Results&&d.Victory&&d.EncountersCleared==4&&d.EncounterEnds==4&&d.Boss==null);
                        Check("normal retry preserves randomized order "+hero,d.RequestRetry()&&d.State==RunState.Ready&&d.Encounter==order[0]&&d.EncounterOrder.SequenceEqual(order)&&d.EncountersCleared==0&&d.RunsStarted==2);
                        MusicCheck("normal retry "+hero,ExpectedMusic(order.Last()));d.SetPaused(true);d.ReturnToMenu();
                    }
                    yield return SceneReady(()=>{var m=FindAnyObjectByType<ConfrontationMenu>();return m&&m.Canvas&&!m.Loading;});menu=FindAnyObjectByType<ConfrontationMenu>();Check("normal return cleans selection "+hero,menu&&!FindAnyObjectByType<HeroSelection>()&&!RunDirector.Instance);
                }
                RunDifficulty.Selected=original;
            }
            if(stage>=7&&!args.Contains("-lastbellFlowQuick"))
            {
                var original=RunDifficulty.Selected;
                for(int map=0;map<RunMaps.All.Length;map++)for(int enc=0;enc<RunDirector.Bosses.Length;enc++)
                {
                    menu=FindAnyObjectByType<ConfrontationMenu>();
                    int hero=(map+enc)%RunDirector.Classes.Length;RunDifficulty.Selected=(Difficulty)hero;
                    menu.SelectClass(hero);menu.BossRush();yield return null;var setup=menu.Rush;
                    setup.SelectMap(map);setup.Continue();setup.SelectEncounter(enc);setup.Launch();setup.Launch();
                    yield return SceneReady(()=>RunDirector.Instance&&RunDirector.Instance.State==RunState.Ready);var d=RunDirector.Instance;string tag=map+"/"+enc;
                    Check("test honors map and encounter "+tag,d&&RunMaps.Current==map&&d.Encounter==enc&&d.Mode==RunMode.Practice);
                    Check("test honors hero and difficulty "+tag,d&&d.ClassName==RunDirector.Classes[hero]&&d.Difficulty==(Difficulty)hero&&d.RunsStarted==1&&!MenuLaunch.Pending);
                    if(d)
                    {
                        MusicCheck("test Ready "+tag,"MainMenuMusic");
                        d.RequestReady();MusicCheck("test Intro "+tag,ExpectedMusic(enc));yield return new WaitForSeconds(4.5f);
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
                        MusicCheck("retry preparation "+tag,expected);
                        d.SetPaused(true);MenuReturnBridge.Return(true);MenuReturnBridge.Return(true);yield return SceneReady(()=>{var m=FindAnyObjectByType<ConfrontationMenu>();return m&&m.Rush;});
                        menu=FindAnyObjectByType<ConfrontationMenu>();var back=menu?menu.Rush:null;
                        Check("return restores test setup "+tag,back&&back.BossStep&&back.MapIndex==map&&back.EncounterIndex==enc&&back.Hero==RunDirector.Classes[hero]);
                        Check("test transition cleanup "+tag,!RunDirector.Instance&&!FindAnyObjectByType<PlayerController>()&&FindObjectsByType<AudioListener>().Count(a=>a.enabled)==1&&FindObjectsByType<EventSystem>().Length==1);
                        if(back){back.Close();yield return new WaitForSeconds(1);}
                    }
                    yield return SceneReady(()=>{var m=FindAnyObjectByType<ConfrontationMenu>();return m&&m.Canvas&&!m.Loading&&!m.Rush;});menu=FindAnyObjectByType<ConfrontationMenu>();Check("test returns to main menu "+tag,menu);
                }
                RunDifficulty.Selected=original;
            }
            if(originalInput){InputSystem.settings=originalInput;originalInput.hideFlags=originalInputFlags;}else Check("original input settings retained",false);
            Destroy(testInput);yield return null;
            Check("zero runtime errors",errors.Count==0);report.AddRange(errors);
            report.Add($"RESULT: {report.Count(s=>s.StartsWith("PASS"))} passed, {failed} failed");
            File.WriteAllLines(Path.Combine(output,"stage"+stage+".txt"),report);
            Application.logMessageReceived-=Log;
            RestoreMenuPreference();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(failed==0?0:1);
#else
            Application.Quit(failed==0?0:1);
#endif
        }
        void RestoreMenuPreference(){if(menuPreferenceCaptured){MenuAppearance.Selected=originalMenuStyle;menuPreferenceCaptured=false;}}
        void OnDestroy(){RestoreMenuPreference();}
        IEnumerator SceneReady(Func<bool> ready)
        {
            float deadline=Time.realtimeSinceStartup+30;
            while(!ready()&&Time.realtimeSinceStartup<deadline)yield return null;
            if(!ready())Check("scene ready before timeout",false);
            yield return null;
        }
        // Metronome Court (map 5) is a live showcase stage with its own orbit framing: the metronome and the court centre
        // stay on screen for the full orbit at 16:9 and 21:9, and the five original stages keep the shared framing.
        IEnumerator MetronomeCourtBackdrop(TestRushSetup setup)
        {
            int court=RunMaps.IndexOf("metronome-court");setup.SelectMap(court);
            // LiveBackdrop is still true from the previous arena until the wipe starts, so also wait for the stage swap.
            var show=FindAnyObjectByType<MenuShowcase>();float until=Time.realtimeSinceStartup+12;
            while(!(show&&show.CurrentStage==TestRushSetup.ShowcaseStage(court)&&setup.LiveBackdrop)&&Time.realtimeSinceStartup<until)yield return null;
            Check("Metronome Court shows live showcase arena",court==5&&TestRushSetup.ShowcaseStage(court)==5&&show&&show.CurrentStage==5&&setup.LiveBackdrop);
            var battle=show?show.GetComponent<MenuBattle>():null;
            if(!show||!battle||show.CurrentStage!=5){Check("Metronome Court stage active",false);yield break;}
            var stage=show.Stages[5];
            Check("Metronome Court stage active",stage.Name=="Metronome Court"&&stage.Environment.activeInHierarchy&&show.Stages.Count(s=>s.Environment.activeInHierarchy)==1);
            Check("original five stages keep the shared orbit",show.Stages.Take(5).All(s=>!s.CustomCamera)&&stage.CustomCamera);
            Check("Metronome Court geometry baked",stage.Environment.GetComponentsInChildren<MeshRenderer>(true).Length>100&&!stage.Environment.GetComponentInChildren<LastBell.Arenas.MetronomeCourt.MetronomeCourtArena>(true));
            var swing=stage.Environment.GetComponent<MenuMetronomeSwing>();float rod0=swing?swing.RodAngle:0;yield return new WaitForSeconds(.3f);
            Check("Metronome Court rod swings",swing&&swing.isActiveAndEnabled&&Mathf.Abs(swing.RodAngle-rod0)>.5f);
            var metronome=stage.Environment.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Metronome (arena object)");
            var cam=show.gameObject.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Camera>()).FirstOrDefault(c=>c.enabled);
            if(!metronome||!cam){Check("Metronome Court framing inputs",false);yield break;}
            var points=new List<Vector3>();
            for(int i=0;i<24;i++){float a=i*Mathf.PI*2/24;points.Add(new Vector3(Mathf.Sin(a)*6,0,Mathf.Cos(a)*6));}   // court centre; the near floor edge may pass under the cards, as on the other stages
            // centred orbit like the other stages (Risxhb, October 25, 2026): the court centre stays in frame; the metronome, 20 m out, may swing past the edge
            float minX=1,maxX=0,minY=1,maxY=0;bool behind=false;
            cam.transform.GetPositionAndRotation(out var keepPos,out var keepRot);
            foreach(float aspect in new[]{16f/9,2560f/1080})
            {
                cam.aspect=aspect;
                for(int angle=0;angle<360;angle+=5)
                {
                    battle.OrbitPose(angle,out var pos,out var rot);cam.transform.SetPositionAndRotation(pos,rot);
                    foreach(var p in points){var v=cam.WorldToViewportPoint(p);if(v.z<=0)behind=true;
                        minX=Mathf.Min(minX,v.x);maxX=Mathf.Max(maxX,v.x);minY=Mathf.Min(minY,v.y);maxY=Mathf.Max(maxY,v.y);}
                }
            }
            cam.ResetAspect();cam.transform.SetPositionAndRotation(keepPos,keepRot);
            Debug.Log($"Metronome Court orbit framing: x {minX:0.000}-{maxX:0.000}, y {minY:0.000}-{maxY:0.000} ({points.Count} points, 72 angles, 16:9 + 21:9)");
            Check("Metronome Court centre in frame for the full orbit",!behind&&minX>=0&&maxX<=1&&minY>=0&&maxY<=1);
            Check("Metronome Court orbit centred like the other stages",Mathf.Abs((minX+maxX)/2-.5f)<.06f&&stage.ScreenOffsetDegrees==Vector2.zero&&stage.CameraTarget.z==0);
            yield return Shot("metronome-court-live.png");
            battle.DriveCamera=false;
            foreach(int angle in new[]{0,90,180,270})
            {battle.OrbitPose(angle,out var pos,out var rot);cam.transform.SetPositionAndRotation(pos,rot);yield return Shot("metronome-court-orbit-"+angle+".png");}
            battle.DriveCamera=true;
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
        static string ExpectedMusic(int encounter) => new[]{"The Unfinished Hour","Crown of the Meridian","Where the Last Flower Falls","Pressure Beneath the Brass","By My Measure (Haste)"}[encounter];
        void MusicCheck(string label,string clip)
        {
            var owners=FindObjectsByType<MenuMusic>();var music=owners.Length==1?owners[0]:null;
            bool menuTrack=clip=="MainMenuMusic";
            // A boss whose final track has not been supplied yet is reported as a known gap, not as a pass or a failure.
            if(!menuTrack&&!Resources.Load<AudioClip>("Menu/BossMusic/"+clip)){report.Add("GAP  music "+label+" — boss track '"+clip+"' not supplied");Debug.Log(report.Last());return;}
            Check("music "+label,music&&music.Source&&music.Source.clip&&
                (menuTrack?music.Source==music.MenuSource:music.Source.clip.name==clip)&&
                music.Source.isPlaying&&(menuTrack?!music.Source.loop:music.Source.loop));
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
