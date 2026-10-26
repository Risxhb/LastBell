using System;
using System.Collections.Generic;
using System.Linq;
using LastBell.Run;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LastBell.Menu
{
    // The game's only main menu (October 24, 2026): split-screen hero/boss scene, right-hand text navigation with a
    // highlight strip, a mini music player, settings in the same column, and Boss Rush hosted in this scene.
    public sealed class ConfrontationMenu : MonoBehaviour
    {
        public const string ScenePath = "Assets/LastBell/Menu/ConfrontationMainMenu.unity";
        [Serializable] public class Presentation
        {
            public string Class;
            public Vector3 Offset;
            public float Yaw, Scale = 1;
        }
        // Defaults match the saved scene; ConfrontationMenuBuild.PolishPass writes the same values.
        public Presentation[] Classes = {
            new Presentation { Class="Vanguard", Offset=new Vector3(.15f,0,0), Yaw=6 }, new Presentation { Class="Chimesinger", Offset=new Vector3(.1f,0,.15f), Yaw=4, Scale=.97f },
            new Presentation { Class="Arcanist", Offset=new Vector3(0,0,.15f), Yaw=4 } };
        public Vector3 HeroPosition = new Vector3(1.45f,0,8.3f), BossPosition = new Vector3(0,0,-15);
        public Vector3 CameraPosition = new Vector3(.6f,.75f,10.9f), CameraTarget = new Vector3(-.6f,2.2f,-7);
        // Set before loading this scene to reopen Boss Rush (e.g. "Test Setup" from a practice run).
        public static bool ResumeBossRush;
        public CharacterPresenter Hero { get; private set; }
        public CharacterPresenter Boss { get; private set; }
        public Transform BossWrapper { get; private set; }
        public Camera View { get; private set; }
        public Button StartButton, RushButton, SettingsButton, ExitButton, PreviousButton, NextButton;
        public Button DifficultyButton { get; private set; }
        public Button MusicPlayButton { get; private set; }
        public Button MusicPauseButton { get; private set; }
        public Button MusicNextButton { get; private set; }
        public Slider MusicVolumeControl { get; private set; }
        public Button ApplyButton { get; private set; }
        public Button SettingsBackButton { get; private set; }
        public TestRushSetup Rush { get; private set; }
        public bool Loading { get; private set; }
        public bool SettingsOpen => settings && settings.activeSelf;
        public bool StageHidden { get; private set; }
        public MenuMusic Music { get; private set; }
        public Canvas Canvas { get; private set; }
        public int SelectedIndex => Array.IndexOf(RunDirector.Classes, SavedHeroSelection.Current);
        readonly List<CharacterPresenter> heroes = new List<CharacterPresenter>();
        readonly List<GameObject> hiddenStage = new List<GameObject>();
        Transform heroRoot;
        RectTransform safe, navigationRect, musicRect, settingsRect;
        GameObject settings;
        CanvasGroup navigation, musicGroup;
        Text className, difficulty, message, selectClass, trackTitle;
        RectTransform difficultyPrev, difficultyNext;
        int sizedHeight = -1;
        Texture2D gradient, stripTex;
        Image shade;
        Sprite gradientSprite, stripSprite;

        void Awake(){MenuAppearance.Enter(MenuStyle.Alternative,gameObject);}
        void Start()
        {
            MenuLaunch.Clear(); MenuReturnBridge.TestSession=false;
            if(MenuFlowTest.Requested)MenuSmokeTest.PrepareInput();
            MenuSettings.Restore(); Music=MenuMusic.Ensure(); Music.PlayMenu();
            View=Camera.main; View.orthographic=false; View.nearClipPlane=.08f;
            View.transform.SetPositionAndRotation(CameraPosition,Quaternion.LookRotation(CameraTarget-CameraPosition));
            heroRoot=new GameObject("Selected hero presentation").transform; heroRoot.SetParent(transform,false);
            foreach(var name in RunDirector.Classes)
            {
                var root=new GameObject(name+" preview only").transform;root.SetParent(heroRoot,false);
                var p=CharacterPresenter.ForPlayer(root,name); Sanitize(p); heroes.Add(p); root.gameObject.SetActive(false);
            }
            BossWrapper=new GameObject("Bellwarden presentation — exact 2x").transform;BossWrapper.SetParent(transform,false);
            BossWrapper.position=BossPosition;BossWrapper.localScale=Vector3.one*2;
            BossWrapper.rotation=Quaternion.LookRotation(HeroPosition-BossPosition);
            // Bellkeeper2 is the project's authored bell-headed Bellwarden presentation.
            Boss=CharacterPresenter.Create(BossWrapper,"Bellkeeper2",2,.8f,0,null);Sanitize(Boss);
            SelectClass(SelectedIndex); BuildUI();
            EventSystem.current.SetSelectedGameObject(StartButton.gameObject);
            if(ResumeBossRush){ResumeBossRush=false;BossRush();if(Rush)Rush.OpenBossStep();}
            MenuFlowTest.Install();
            if(ConfrontationMenuTest.Requested) gameObject.AddComponent<ConfrontationMenuTest>();
        }
        static void Sanitize(CharacterPresenter p)
        {
            foreach(var c in p.GetComponentsInChildren<Collider>(true))c.enabled=false;
            foreach(var r in p.GetComponentsInChildren<Rigidbody>(true)){r.isKinematic=true;r.detectCollisions=false;}
            foreach(var b in p.GetComponentsInChildren<MonoBehaviour>(true))if(b!=p)b.enabled=false;
            foreach(var a in p.GetComponentsInChildren<AudioSource>(true)){a.Stop();a.enabled=false;}
        }
        public void SelectClass(int index)
        {
            if(Loading||SettingsOpen)return; index=(index+RunDirector.Classes.Length)%RunDirector.Classes.Length;
            SavedHeroSelection.Current=RunDirector.Classes[index];
            for(int i=0;i<heroes.Count;i++)heroes[i].transform.parent.gameObject.SetActive(i==index);
            Hero=heroes[index];var profile=Classes.FirstOrDefault(p=>p.Class==SavedHeroSelection.Current)??new Presentation();
            var root=Hero.transform.parent;root.position=HeroPosition+profile.Offset;root.localScale=Vector3.one*profile.Scale;
            root.rotation=Quaternion.LookRotation(BossPosition-root.position)*Quaternion.Euler(0,profile.Yaw,0);
            Hero.ResetPose();Hero.Tick(null);if(className)className.text=SavedHeroSelection.Current.ToUpperInvariant();
        }

        // ---------------------------------------------------------------- UI
        void BuildUI()
        {
            Canvas=MenuUI.Canvas(transform);Canvas.name="Confrontation menu UI";
            Canvas.GetComponent<CanvasScaler>().matchWidthOrHeight=.5f;
            safe=MenuUI.Rect("Safe area",Canvas.transform,Vector2.zero,Vector2.one);
            gradient=new Texture2D(128,1,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Clamp};
            // Steeper falloff keeps the right third dark enough for labels, including ultrawide.
            for(int i=0;i<128;i++)gradient.SetPixel(i,0,new Color(.008f,.008f,.022f,Mathf.SmoothStep(0,.985f,Mathf.Clamp01(i/127f*2.3f))));
            gradient.Apply();gradientSprite=Sprite.Create(gradient,new Rect(0,0,128,1),Vector2.one*.5f);
            // Highlight strip: soft gold band, transparent at both ends.
            stripTex=new Texture2D(128,1,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Clamp};
            for(int i=0;i<128;i++){float u=i/127f;float a=Mathf.SmoothStep(0,1,Mathf.Clamp01(u/.22f))*Mathf.SmoothStep(0,1,Mathf.Clamp01((1-u)/.32f));stripTex.SetPixel(i,0,new Color(1,1,1,a));}
            stripTex.Apply();stripSprite=Sprite.Create(stripTex,new Rect(0,0,128,1),Vector2.one*.5f);
            shade=MenuUI.Panel("Right contrast gradient",safe,new Vector2(.42f,0),Vector2.one,Color.white);shade.sprite=gradientSprite;

            var panel=navigationRect=MenuUI.Rect("Right navigation",safe,new Vector2(.73f,.08f),new Vector2(.97f,.86f));navigation=panel.gameObject.AddComponent<CanvasGroup>();
            var logo=MenuUI.Panel("Last Bell logo",panel,new Vector2(-.1f,.73f),new Vector2(1.1f,1),Color.white);
            logo.sprite=Resources.Load<Sprite>("Menu/ConfrontationLogo");logo.preserveAspect=true;
            StartButton=Nav("Start Run",panel,.51f,StartRun,28);RushButton=Nav("Boss Rush",panel,.40f,BossRush);
            SettingsButton=Nav("Settings",panel,.29f,OpenSettings);ExitButton=Nav("Exit",panel,.18f,Exit);
#if UNITY_WEBGL && !UNITY_EDITOR
            ExitButton.GetComponentInChildren<Text>().text="Return to Menu";
#endif
            // Class picker keeps its approved place above Start Run, as a secondary control.
            selectClass=MenuUI.Text("S E L E C T   C L A S S",panel,new Vector2(0,.705f),new Vector2(1,.745f),13,new Color(MenuUI.Gold.r,MenuUI.Gold.g,MenuUI.Gold.b,.72f));
            className=MenuUI.Text(SavedHeroSelection.Current.ToUpperInvariant(),panel,new Vector2(.13f,.645f),new Vector2(.87f,.705f),UiTheme.SizeHeading,UiTheme.Ivory);
            PreviousButton=ClassArrow(panel,false);FitBox(PreviousButton,new Vector2(.22f,.675f),44);
            NextButton=ClassArrow(panel,true);FitBox(NextButton,new Vector2(.78f,.675f),44);
            var diff=DifficultyButton=Nav("Difficulty",panel,.06f,()=>{RunDifficulty.Selected=(Difficulty)(((int)RunDifficulty.Selected+1)%RunDifficulty.Names.Length);RefreshDifficulty();},16);
            difficulty=diff.GetComponentInChildren<Text>();diff.GetComponent<ConfrontationNavFocus>().Rest=new Color(MenuUI.Gold.r,MenuUI.Gold.g,MenuUI.Gold.b,.75f);RefreshDifficulty();
            difficultyPrev=CycleArrow(diff.transform,false,16,new Color(MenuUI.Gold.r,MenuUI.Gold.g,MenuUI.Gold.b,.5f));
            difficultyNext=CycleArrow(diff.transform,true,16,new Color(MenuUI.Gold.r,MenuUI.Gold.g,MenuUI.Gold.b,.5f));

            BuildMusicPlayer();
            var stack=new Selectable[]{PreviousButton,NextButton,StartButton,RushButton,SettingsButton,ExitButton,diff,MusicPlayButton};
            for(int i=0;i<stack.Length;i++)stack[i].navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnDown=stack[(i+1)%stack.Length],selectOnUp=stack[(i+stack.Length-1)%stack.Length],selectOnLeft=i==0||i==1?PreviousButton:null,selectOnRight=i==0||i==1?NextButton:i==stack.Length-1?MusicPauseButton:null};
            MusicPauseButton.navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnLeft=MusicPlayButton,selectOnRight=MusicNextButton,selectOnUp=diff,selectOnDown=PreviousButton};
            MusicNextButton.navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnLeft=MusicPauseButton,selectOnRight=MusicPlayButton,selectOnUp=diff,selectOnDown=PreviousButton};
            BuildSettings();
            message=MenuUI.Text("",safe,new Vector2(.70f,.074f),new Vector2(.99f,.106f),14,MenuUI.Gold);
        }
        public void RefreshDifficulty(){if(difficulty)difficulty.text="Difficulty: "+RunDifficulty.Name(RunDifficulty.Selected);}

        // Mini music player, bottom right: track title and three drawn controls (play, pause, skip).
        void BuildMusicPlayer()
        {
            musicRect=MenuUI.Rect("Music player",safe,new Vector2(.73f,.012f),new Vector2(.97f,.062f));musicGroup=musicRect.gameObject.AddComponent<CanvasGroup>();
            trackTitle=MenuUI.Text("",musicRect,new Vector2(0,0),new Vector2(.62f,1),UiTheme.SizeMicro,UiTheme.IvoryMuted,TextAnchor.MiddleRight);
            trackTitle.resizeTextForBestFit=true;trackTitle.resizeTextMinSize=10;trackTitle.resizeTextMaxSize=UiTheme.SizeMicro;
            MusicPlayButton=MusicButton("Play",MusicGlyph.Kind.Play,.66f,()=>Music.ResumeTrack());
            MusicPauseButton=MusicButton("Pause",MusicGlyph.Kind.Pause,.78f,()=>Music.PauseTrack());
            MusicNextButton=MusicButton("Next",MusicGlyph.Kind.Next,.90f,()=>Music.NextTrack());
        }
        Button MusicButton(string name,MusicGlyph.Kind kind,float x,UnityEngine.Events.UnityAction action)
        {
            var r=MenuUI.Rect(name,musicRect,new Vector2(x,.5f),new Vector2(x,.5f));r.sizeDelta=new Vector2(34,34);
            var im=r.gameObject.AddComponent<Image>();im.color=new Color(0,0,0,0);im.raycastTarget=true;
            var b=r.gameObject.AddComponent<Button>();b.targetGraphic=im;b.transition=Selectable.Transition.None;b.onClick.AddListener(action);
            var strip=Strip(r);
            var g=MenuUI.Rect("Glyph",r,Vector2.zero,Vector2.one,new Vector2(10,10)).gameObject.AddComponent<MusicGlyph>();g.Shape=kind;g.raycastTarget=false;
            var focus=r.gameObject.AddComponent<ConfrontationNavFocus>();focus.Icon=g;focus.Strip=strip;focus.IconRest=new Color(UiTheme.Ivory.r,UiTheme.Ivory.g,UiTheme.Ivory.b,.72f);
            return b;
        }
        // Highlight strip behind a control (first child, so the label/glyph draws over it), shown on focus.
        public Image Strip(Transform parent)
        {
            var s=MenuUI.Panel("Highlight strip",parent,Vector2.zero,Vector2.one,new Color(UiTheme.Gold.r,UiTheme.Gold.g,UiTheme.Gold.b,.26f));
            s.sprite=stripSprite;s.rectTransform.offsetMin=new Vector2(-28,2);s.rectTransform.offsetMax=new Vector2(28,-2);s.transform.SetAsFirstSibling();
            var top=MenuUI.Panel("Strip edge",s.transform,new Vector2(0,1),new Vector2(1,1),new Color(UiTheme.Gold.r,UiTheme.Gold.g,UiTheme.Gold.b,.55f));top.sprite=stripSprite;top.rectTransform.pivot=new Vector2(.5f,1);top.rectTransform.sizeDelta=new Vector2(0,1);
            var bottom=MenuUI.Panel("Strip edge",s.transform,new Vector2(0,0),new Vector2(1,0),new Color(UiTheme.Gold.r,UiTheme.Gold.g,UiTheme.Gold.b,.55f));bottom.sprite=stripSprite;bottom.rectTransform.pivot=new Vector2(.5f,0);bottom.rectTransform.sizeDelta=new Vector2(0,1);
            s.enabled=false;top.enabled=bottom.enabled=false;return s;
        }
        // Small drawn chevron (non-interactive) centred on the parent's middle; callers position it.
        static RectTransform CycleArrow(Transform parent,bool next,float size,Color color)
        {
            var a=MenuUI.Rect(next?"Cycle next":"Cycle previous",parent,new Vector2(.5f,.5f),new Vector2(.5f,.5f)).gameObject.AddComponent<ClassSelectionArrow>();
            a.Next=next;a.color=color;a.raycastTarget=false;a.rectTransform.sizeDelta=new Vector2(size,size);return a.rectTransform;
        }
        // Class arrows hug the rendered name (half width + 32) and the difficulty chevrons hug their caption.
        void PlaceArrows()
        {
            if(className&&PreviousButton&&NextButton)
            {
                float limit=Mathf.Max(22,navigationRect.rect.width*.5f-22);
                float x=Mathf.Clamp(className.preferredWidth*.5f+32,22,limit);
                foreach(var b in new[]{PreviousButton,NextButton})
                {
                    var r=(RectTransform)b.transform;r.anchorMin=r.anchorMax=new Vector2(.5f,.675f);
                    r.anchoredPosition=new Vector2(b==NextButton?x:-x,0);
                }
            }
            if(difficulty&&difficultyPrev&&difficultyNext)
            {
                float x=ConfrontationNavFocus.HalfWidth(difficulty)+16;
                difficultyPrev.anchoredPosition=new Vector2(-x,0);difficultyNext.anchoredPosition=new Vector2(x,0);
            }
        }
        // 4:3 and other short screens: the canvas scales down, so the small captions get a 16 px floor.
        void ApplyTextSizes(bool small)
        {
            if(selectClass)selectClass.fontSize=small?16:13;
            if(difficulty){difficulty.fontSize=small?18:16;difficulty.resizeTextMaxSize=difficulty.fontSize;difficulty.resizeTextMinSize=small?16:13;}
            if(trackTitle){trackTitle.fontSize=small?14:UiTheme.SizeMicro;trackTitle.resizeTextMaxSize=trackTitle.fontSize;}
        }
        Button ClassArrow(Transform parent,bool next)
        {
            var b=Nav(next?"Next class":"Previous class",parent,.63f,()=>SelectClass(SelectedIndex+(next?1:-1)));
            b.GetComponentInChildren<Text>().text="";
            var icon=MenuUI.Rect("Class arrow",b.transform,Vector2.zero,Vector2.one,new Vector2(8,8)).gameObject.AddComponent<ClassSelectionArrow>();
            icon.Next=next;icon.color=MenuUI.Ivory;icon.raycastTarget=false;
            var focus=b.GetComponent<ConfrontationNavFocus>();focus.Label=null;if(focus.Strip){Destroy(focus.Strip.gameObject);focus.Strip=null;}focus.Icon=icon;
            return b;
        }
        static void Fit(Component b,float x,float y,float x2,float y2){var r=(RectTransform)b.transform;r.anchorMin=new Vector2(x,y);r.anchorMax=new Vector2(x2,y2);r.offsetMin=r.offsetMax=Vector2.zero;}
        // Fixed square hit box (canvas units) centred on an anchor point.
        static void FitBox(Button b,Vector2 at,float size){var r=(RectTransform)b.transform;r.anchorMin=r.anchorMax=at;r.pivot=new Vector2(.5f,.5f);r.sizeDelta=new Vector2(size,size);r.anchoredPosition=Vector2.zero;}
        // Text navigation entry: transparent hit area, label (first Text child) and a highlight strip on focus.
        Button Nav(string label,Transform parent,float y,UnityEngine.Events.UnityAction action,int size=24)
        {
            var b=MenuUI.Button(label,parent,new Vector2(0,y),new Vector2(1,y+.09f),action);
            var shared=b.GetComponent<MenuFocus>();
            if(shared){if(shared.Stroke)Destroy(shared.Stroke.gameObject);if(shared.Marker)Destroy(shared.Marker);if(shared.Underline)Destroy(shared.Underline);Destroy(shared);}
            b.GetComponent<Image>().color=new Color(0,0,0,0);
            var t=b.GetComponentInChildren<Text>();t.fontSize=size;t.resizeTextForBestFit=true;t.resizeTextMinSize=13;t.resizeTextMaxSize=size;
            var focus=b.gameObject.AddComponent<ConfrontationNavFocus>();focus.Label=t;focus.Strip=Strip(b.transform);
            return b;
        }

        // ---------------------------------------------------------------- actions
        public void StartRun(){if(Loading||SettingsOpen||Rush)return;Loading=MenuLaunch.Normal(SavedHeroSelection.Current);if(!Loading)message.text="Run scenes unavailable in this build.";}
        // Boss Rush opens in this scene (no separate hero-select scene): arena step, then the boss step.
        public void BossRush()
        {
            if(Loading||SettingsOpen||Rush)return;
            navigation.interactable=navigation.blocksRaycasts=false;navigation.alpha=0;musicGroup.interactable=musicGroup.blocksRaycasts=false;musicGroup.alpha=0;
            shade.enabled=false;Rush=new GameObject("Boss Rush controls").AddComponent<TestRushSetup>();Rush.Build(this,safe,SavedHeroSelection.Current);
        }
        // Called by TestRushSetup when it closes.
        public void CloseRush()
        {
            Rush=null;shade.enabled=true;navigation.interactable=navigation.blocksRaycasts=true;navigation.alpha=1;musicGroup.interactable=musicGroup.blocksRaycasts=true;musicGroup.alpha=1;
            if(className)className.text=SavedHeroSelection.Current.ToUpperInvariant();
            SelectClass(SelectedIndex);RefreshDifficulty();
            EventSystem.current.SetSelectedGameObject(RushButton.gameObject);
        }
        // Boss Rush shows the live arena showcase scene behind its UI; park this scene's camera, models and environment.
        public void SetStageHidden(bool hidden)
        {
            if(StageHidden==hidden)return;StageHidden=hidden;
            if(View)View.enabled=!hidden;
            if(heroRoot)heroRoot.gameObject.SetActive(!hidden);if(BossWrapper)BossWrapper.gameObject.SetActive(!hidden);
            if(hidden){hiddenStage.Clear();foreach(var node in gameObject.scene.GetRootGameObjects())if(node!=gameObject&&(!View||node!=View.gameObject)&&node.activeSelf){hiddenStage.Add(node);node.SetActive(false);}}
            else{foreach(var node in hiddenStage)if(node)node.SetActive(true);hiddenStage.Clear();}
        }
        public void Exit()
        {
            if(Loading||SettingsOpen||Rush)return;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
#elif UNITY_WEBGL
            SceneManager.LoadScene(MenuAppearance.ScenePath);
#else
            Application.Quit();
#endif
        }

        // ---------------------------------------------------------------- settings
        // Settings replaces the navigation column in place, in the same language as the menu and Boss Rush:
        // eyebrow, title and gold rule, text rows with a highlight strip, faint dividers, Gold primary + text Back.
        void BuildSettings()
        {
            var block=MenuUI.Panel("Settings modal",safe,Vector2.zero,Vector2.one,new Color(0,0,0,.35f));block.raycastTarget=true;settings=block.gameObject;
            var col=settingsRect=MenuUI.Rect("Settings column",block.transform,new Vector2(.60f,.08f),new Vector2(.96f,.90f));
            MenuUI.Text(UiTheme.Spaced("Options"),col,new Vector2(0,.93f),new Vector2(1,.97f),14,MenuUI.Gold,TextAnchor.MiddleLeft);
            var title=MenuUI.Text("SETTINGS",col,new Vector2(0,.83f),new Vector2(1,.93f),52,UiTheme.Ivory,TextAnchor.MiddleLeft);title.resizeTextForBestFit=true;title.resizeTextMinSize=28;title.resizeTextMaxSize=52;
            var rule=MenuUI.Panel("Title rule",col,new Vector2(0,.825f),new Vector2(0,.825f),MenuUI.Gold);rule.rectTransform.pivot=new Vector2(0,.5f);rule.rectTransform.sizeDelta=new Vector2(96,2);

            const float rowH=.09f; float RowY(int i)=>.70f-i*.105f;
            void Divider(int i){var d=MenuUI.Panel("Row divider",col,new Vector2(0,RowY(i)-.008f),new Vector2(1,RowY(i)-.008f),new Color(UiTheme.Ivory.r,UiTheme.Ivory.g,UiTheme.Ivory.b,.10f));d.rectTransform.sizeDelta=new Vector2(0,1);}
            Text Caption(Transform row,string text)=>MenuUI.Text(text,row,new Vector2(0,0),new Vector2(.42f,1),UiTheme.SizeBody,UiTheme.IvoryBody,TextAnchor.MiddleLeft);
            // value row: the value is the button's first Text child; chevrons sit at both ends of the value area
            Button ValueRow(string value,int i,UnityEngine.Events.UnityAction act,string caption)
            {
                var b=Nav(value,col,RowY(i),act,20);Fit(b,0,RowY(i),1,RowY(i)+rowH);
                var t=b.GetComponentInChildren<Text>();var tr=t.rectTransform;tr.anchorMin=new Vector2(.46f,0);tr.anchorMax=new Vector2(1,1);tr.offsetMin=new Vector2(28,3);tr.offsetMax=new Vector2(-28,-3);
                Caption(b.transform,caption);
                foreach(var next in new[]{false,true})
                {
                    var a=MenuUI.Rect(next?"Value next":"Value previous",b.transform,new Vector2(next?1:.46f,.5f),new Vector2(next?1:.46f,.5f)).gameObject.AddComponent<ClassSelectionArrow>();
                    a.Next=next;a.color=new Color(UiTheme.Gold.r,UiTheme.Gold.g,UiTheme.Gold.b,.6f);a.raycastTarget=false;a.rectTransform.sizeDelta=new Vector2(16,16);a.rectTransform.anchoredPosition=new Vector2(next?-10:10,0);
                }
                Divider(i);return b;
            }
            // volume row: the whole row is the slider's hit area; the trough sits in the value area
            var vrow=MenuUI.Rect("Music volume",col,new Vector2(0,RowY(0)),new Vector2(1,RowY(0)+rowH));
            var vhit=vrow.gameObject.AddComponent<Image>();vhit.color=new Color(0,0,0,0);vhit.raycastTarget=true;
            var vstrip=Strip(vrow);
            Caption(vrow,"Music volume");
            var track=MenuUI.Panel("Volume track",vrow,new Vector2(.46f,.46f),new Vector2(.84f,.54f),UiTheme.BarBack);track.rectTransform.offsetMin=new Vector2(28,0);
            var fill=MenuUI.Panel("Fill",track.transform,Vector2.zero,Vector2.one,MenuUI.Gold);
            var handle=MenuUI.Panel("Handle",track.transform,new Vector2(0,.5f),new Vector2(0,.5f),UiTheme.GoldBright);handle.rectTransform.sizeDelta=new Vector2(4,22);
            var percent=MenuUI.Text("",vrow,new Vector2(.84f,0),new Vector2(1,1),UiTheme.SizeNumeric,UiTheme.IvoryBody,TextAnchor.MiddleRight);percent.font=UiTheme.SemiBold;percent.rectTransform.offsetMax=new Vector2(-12,-3);
            var slider=MusicVolumeControl=vrow.gameObject.AddComponent<Slider>();slider.fillRect=fill.rectTransform;slider.handleRect=handle.rectTransform;slider.targetGraphic=vhit;slider.transition=Selectable.Transition.None;
            slider.value=Music.Volume;slider.onValueChanged.AddListener(Music.SetVolume);
            void ShowPercent(float v){percent.text=Mathf.RoundToInt(v*100)+"%";}
            ShowPercent(slider.value);slider.onValueChanged.AddListener(ShowPercent);
            var vfocus=vrow.gameObject.AddComponent<ConfrontationNavFocus>();vfocus.Strip=vstrip;
            Divider(0);

            var modes=MenuSettings.Resolutions;int resolution=Mathf.Max(0,Array.FindIndex(modes,r=>r.x==Screen.width&&r.y==Screen.height));int mode=MenuSettings.DisplayMode,quality=QualitySettings.GetQualityLevel();
            Button display=null,res=null,q=null;
            display=ValueRow(mode==0?"Windowed":"Borderless",1,()=>{mode=1-mode;display.GetComponentInChildren<Text>().text=mode==0?"Windowed":"Borderless";},"Display mode");
            res=ValueRow(modes[resolution].x+" × "+modes[resolution].y,2,()=>{resolution=(resolution+1)%modes.Length;res.GetComponentInChildren<Text>().text=modes[resolution].x+" × "+modes[resolution].y;},"Resolution");
            q=ValueRow(QualitySettings.names[quality],3,()=>{quality=(quality+1)%QualitySettings.names.Length;q.GetComponentInChildren<Text>().text=QualitySettings.names[quality];},"Quality");
#if UNITY_WEBGL && !UNITY_EDITOR
            display.interactable=res.interactable=false;res.GetComponentInChildren<Text>().text="Use browser fullscreen";
#endif
            // actions: text Back on the left, the one Gold primary (Apply) on the right
            SettingsBackButton=Nav("Back",col,.10f,CloseSettings,22);Fit(SettingsBackButton,0,.10f,.36f,.18f);
            SettingsBackButton.GetComponentInChildren<Text>().alignment=TextAnchor.MiddleLeft;
            ApplyButton=MenuUI.Button("Apply",col,new Vector2(.52f,.10f),new Vector2(1,.18f),()=>MenuSettings.Apply(modes[resolution].x,modes[resolution].y,mode,quality));
            var af=ApplyButton.GetComponent<MenuFocus>();af.Primary=true;
            var ink=new Color(.07f,.05f,.02f,1f);
            if(af.Marker){var d=af.Marker.GetComponent<FocusDiamond>();if(d)d.color=ink;}
            if(af.Underline){var u=af.Underline.GetComponent<Image>();if(u)u.color=ink;}
            Selectable[] order={slider,display,res,q,SettingsBackButton,ApplyButton};
            for(int i=0;i<order.Length;i++)order[i].navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnUp=order[(i+order.Length-1)%order.Length],selectOnDown=order[(i+1)%order.Length],
                selectOnLeft=order[i]==ApplyButton?SettingsBackButton:order[i]==slider?null:null,selectOnRight=order[i]==SettingsBackButton?ApplyButton:null};
            settings.SetActive(false);
        }
        public void OpenSettings()
        {
            if(Loading||Rush)return;settings.SetActive(true);
            navigation.interactable=navigation.blocksRaycasts=false;navigation.alpha=0;musicGroup.interactable=musicGroup.blocksRaycasts=false;
            EventSystem.current.SetSelectedGameObject(MusicVolumeControl.gameObject);
        }
        public void CloseSettings()
        {
            settings.SetActive(false);navigation.interactable=navigation.blocksRaycasts=true;navigation.alpha=1;musicGroup.interactable=musicGroup.blocksRaycasts=true;
            PlayerPrefs.Save();EventSystem.current.SetSelectedGameObject(SettingsButton.gameObject);
        }

        void Update()
        {
            if(Hero)Hero.Tick(null);if(Boss)Boss.TickBoss("Idle",.65f);
            if(!safe)return;
            var area=Screen.safeArea;safe.anchorMin=area.position/new Vector2(Screen.width,Screen.height);safe.anchorMax=(area.position+area.size)/new Vector2(Screen.width,Screen.height);
            // Ultrawide: pull the columns inward so they stay in the darkest negative space.
            bool wide=View.aspect>2;
            navigationRect.anchorMin=new Vector2(wide?.76f:.73f,.08f);navigationRect.anchorMax=new Vector2(wide?.95f:.97f,.86f);
            musicRect.anchorMin=new Vector2(wide?.76f:.73f,.012f);musicRect.anchorMax=new Vector2(wide?.95f:.97f,.062f);
            settingsRect.anchorMin=new Vector2(wide?.66f:.60f,.08f);settingsRect.anchorMax=new Vector2(wide?.94f:.96f,.90f);
            if(Screen.height!=sizedHeight){sizedHeight=Screen.height;ApplyTextSizes(Screen.height<900);}
            PlaceArrows();
            if(Music&&trackTitle)trackTitle.text=Music.TrackTitle+(Music.UserPaused?"  ·  paused":"");
            // Keep vertical field of view, widen for 4:3; no moving cinematic camera behind UI.
            if(View.enabled)View.fieldOfView=Mathf.Clamp(42*Mathf.Max(1,1.77778f/View.aspect),42,62);
            bool back=Keyboard.current?.escapeKey.wasPressedThisFrame==true||Gamepad.current?.buttonEast.wasPressedThisFrame==true;
            if(MenuFlowTest.Requested&&MenuSmokeTest.TestKeyboard!=null)back|=MenuSmokeTest.TestKeyboard.escapeKey.wasPressedThisFrame||MenuSmokeTest.TestGamepad.buttonEast.wasPressedThisFrame;
            if(back){if(Rush)Rush.Back();else if(SettingsOpen)CloseSettings();}
            if(Loading){navigation.interactable=false;musicGroup.interactable=false;}
        }
        void OnDestroy()
        {
            foreach(var p in heroes.Concat(new[]{Boss}))if(p)foreach(var r in p.GetComponentsInChildren<Renderer>(true))foreach(var m in r.sharedMaterials)if(m&&m.name.EndsWith("(Instance)"))Destroy(m);
            if(gradientSprite)Destroy(gradientSprite);if(gradient)Destroy(gradient);if(stripSprite)Destroy(stripSprite);if(stripTex)Destroy(stripTex);
        }
    }

    // Focus cue for the main menu: a highlight strip behind the entry (shape cue) plus a brighter label / icon.
    public sealed class ConfrontationNavFocus : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public Text Label; public Graphic Icon; public Image Strip;
        public Color Rest=new Color(.93f,.88f,.76f,.74f);
        public Color IconRest=MenuUI.Ivory;
        Selectable owner;
        bool selected, hovered;
        void Awake(){owner=GetComponent<Selectable>();}
        void OnEnable(){Refresh();}
        void Start(){Refresh();}
        void OnDisable(){hovered=false;}
        bool live;
        void LateUpdate(){bool l=!owner||owner.IsInteractable();if(l!=live)Refresh();}
        public void OnSelect(BaseEventData data){selected=true;Refresh();}
        public void OnDeselect(BaseEventData data){selected=false;Refresh();}
        public void OnPointerEnter(PointerEventData data){hovered=true;Refresh();}
        public void OnPointerExit(PointerEventData data){hovered=false;Refresh();}
        // Distance from the label's centre to its rendered left edge (canvas units).
        public static float HalfWidth(Text label)
        {
            // fontSizeUsedForBestFit is in screen pixels, so remove the canvas scale before comparing.
            float scale=label.canvas?label.canvas.scaleFactor:1;
            float fit=label.resizeTextForBestFit?label.cachedTextGenerator.fontSizeUsedForBestFit/Mathf.Max(1f,label.fontSize*scale):1;
            return Mathf.Min(label.preferredWidth*Mathf.Clamp01(fit>0?fit:1),label.rectTransform.rect.width)*.5f;
        }
        void Refresh()
        {
            live=!owner||owner.IsInteractable();bool on=(selected||hovered)&&live;
            if(Label)Label.color=!live?new Color(Rest.r,Rest.g,Rest.b,.35f):on?UiTheme.GoldBright:Rest;
            if(Strip){Strip.enabled=on;foreach(var e in Strip.GetComponentsInChildren<Image>(true))if(e!=Strip)e.enabled=on;}
            if(Icon)Icon.color=on?UiTheme.GoldBright:IconRest;
        }
    }
    public sealed class FocusDiamond : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();var r=GetPixelAdjustedRect();var c=r.center;
            vertices.AddVert(new Vector2(c.x,r.yMax),color,Vector2.zero);vertices.AddVert(new Vector2(r.xMax,c.y),color,Vector2.zero);
            vertices.AddVert(new Vector2(c.x,r.yMin),color,Vector2.zero);vertices.AddVert(new Vector2(r.xMin,c.y),color,Vector2.zero);
            vertices.AddTriangle(0,1,2);vertices.AddTriangle(0,2,3);
        }
    }
    // Canvas geometry keeps these controls visible in WebGL without relying on font glyphs.
    public sealed class ClassSelectionArrow : MaskableGraphic
    {
        public bool Next;
        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();var rect=GetPixelAdjustedRect();float size=Mathf.Min(rect.width,rect.height)*.75f;
            float direction=Next?1:-1;var center=rect.center;
            var top=center+new Vector2(-.25f*direction,.5f)*size;
            var tip=center+new Vector2(.3f*direction,0)*size;
            var bottom=center+new Vector2(-.25f*direction,-.5f)*size;
            Stroke(vertices,top,tip,Mathf.Max(2,size*.14f));Stroke(vertices,tip,bottom,Mathf.Max(2,size*.14f));
        }
        void Stroke(VertexHelper vertices,Vector2 start,Vector2 end,float width)
        {
            Vector2 delta=(end-start).normalized;var normal=new Vector2(-delta.y,delta.x)*width*.5f;
            int i=vertices.currentVertCount;
            vertices.AddVert(start+normal,color,Vector2.zero);vertices.AddVert(start-normal,color,Vector2.zero);
            vertices.AddVert(end-normal,color,Vector2.zero);vertices.AddVert(end+normal,color,Vector2.zero);
            vertices.AddTriangle(i,i+1,i+2);vertices.AddTriangle(i,i+2,i+3);
        }
    }
    // Music player glyphs drawn as geometry: play triangle, pause bars, skip (triangle + bar).
    public sealed class MusicGlyph : MaskableGraphic
    {
        public enum Kind { Play, Pause, Next }
        public Kind Shape;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var r=GetPixelAdjustedRect();float s=Mathf.Min(r.width,r.height);var c=r.center;
            void Tri(Vector2 a,Vector2 b,Vector2 d){int i=vh.currentVertCount;vh.AddVert(a,color,Vector2.zero);vh.AddVert(b,color,Vector2.zero);vh.AddVert(d,color,Vector2.zero);vh.AddTriangle(i,i+1,i+2);}
            void Box(float x0,float y0,float x1,float y1){int i=vh.currentVertCount;vh.AddVert(new Vector2(x0,y0),color,Vector2.zero);vh.AddVert(new Vector2(x0,y1),color,Vector2.zero);vh.AddVert(new Vector2(x1,y1),color,Vector2.zero);vh.AddVert(new Vector2(x1,y0),color,Vector2.zero);vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);}
            float h=s*.5f;
            switch(Shape)
            {
                case Kind.Play: Tri(c+new Vector2(-h*.75f,h),c+new Vector2(h*.9f,0),c+new Vector2(-h*.75f,-h)); break;
                case Kind.Pause: Box(c.x-h*.75f,c.y-h,c.x-h*.2f,c.y+h); Box(c.x+h*.2f,c.y-h,c.x+h*.75f,c.y+h); break;
                default: Tri(c+new Vector2(-h*.85f,h),c+new Vector2(h*.45f,0),c+new Vector2(-h*.85f,-h)); Box(c.x+h*.5f,c.y-h,c.x+h*.85f,c.y+h); break;
            }
        }
    }
}
