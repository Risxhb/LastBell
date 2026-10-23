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
    public sealed class ConfrontationMenu : MonoBehaviour
    {
        public const string ScenePath = "Assets/LastBell/Menu/ConfrontationMainMenu.unity";
        [Serializable] public class Presentation
        {
            public string Class;
            public Vector3 Offset;
            public float Yaw, Scale = 1;
        }
        public Presentation[] Classes = {
            new Presentation { Class="Vanguard" }, new Presentation { Class="Chimesinger", Offset=new Vector3(0,0,.15f) },
            new Presentation { Class="Arcanist", Offset=new Vector3(0,0,.15f) } };
        public Vector3 HeroPosition = new Vector3(1.45f,0,6.5f), BossPosition = new Vector3(0,0,-7);
        public Vector3 CameraPosition = new Vector3(0,1.7f,10.5f), CameraTarget = new Vector3(-1,2.8f,-7);
        public CharacterPresenter Hero { get; private set; }
        public CharacterPresenter Boss { get; private set; }
        public Transform BossWrapper { get; private set; }
        public Camera View { get; private set; }
        public Button StartButton, RushButton, SettingsButton, ExitButton, PreviousButton, NextButton;
        public bool Loading { get; private set; }
        public bool SettingsOpen => (settings && settings.activeSelf) || (UISettings && UISettings.IsOpen);
        public Button UISettingsButton {get;private set;}
        public MenuAppearancePanel UISettings {get;private set;}
        public MenuMusic Music { get; private set; }
        public Canvas Canvas { get; private set; }
        public int SelectedIndex => Array.IndexOf(RunDirector.Classes, SavedHeroSelection.Current);
        readonly List<CharacterPresenter> heroes = new List<CharacterPresenter>();
        Transform heroRoot;
        RectTransform safe;
        GameObject settings;
        CanvasGroup navigation;
        Text className, difficulty, message;
        Button settingsBack;
        Texture2D gradient;
        Sprite gradientSprite;
        void Awake(){MenuAppearance.Enter(MenuStyle.Alternative,gameObject);}
        void Start()
        {
            MenuLaunch.Clear(); MenuReturnBridge.TestSession=false;
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
        void BuildUI()
        {
            Canvas=MenuUI.Canvas(transform);Canvas.name="Confrontation menu UI";
            Canvas.GetComponent<CanvasScaler>().matchWidthOrHeight=.5f;
            safe=MenuUI.Rect("Safe area",Canvas.transform,Vector2.zero,Vector2.one);
            gradient=new Texture2D(128,1,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Clamp};
            for(int i=0;i<128;i++)gradient.SetPixel(i,0,new Color(.008f,.008f,.022f,Mathf.SmoothStep(0,.985f,Mathf.Clamp01(i/127f*2.5f))));
            gradient.Apply();gradientSprite=Sprite.Create(gradient,new Rect(0,0,128,1),Vector2.one*.5f);
            var shade=MenuUI.Panel("Right contrast gradient",safe,new Vector2(.55f,0),Vector2.one,Color.white);shade.sprite=gradientSprite;
            var panel=MenuUI.Rect("Right navigation",safe,new Vector2(.73f,.06f),new Vector2(.97f,.84f));navigation=panel.gameObject.AddComponent<CanvasGroup>();
            var logo=MenuUI.Panel("Last Bell logo",panel,new Vector2(-.1f,.73f),new Vector2(1.1f,1),Color.white);
            logo.sprite=Resources.Load<Sprite>("Menu/ConfrontationLogo");logo.preserveAspect=true;
            StartButton=Nav("Start Run",panel,.51f,StartRun);RushButton=Nav("Boss Rush",panel,.40f,BossRush);
            SettingsButton=Nav("Settings",panel,.29f,OpenSettings);ExitButton=Nav("Exit",panel,.18f,Exit);
#if UNITY_WEBGL && !UNITY_EDITOR
            ExitButton.GetComponentInChildren<Text>().text="Return to Menu";
#endif
            MenuUI.Text("SELECT CLASS",panel,new Vector2(0,.69f),new Vector2(1,.73f),12,MenuUI.Gold);
            className=MenuUI.Text(SavedHeroSelection.Current.ToUpperInvariant(),panel,new Vector2(.13f,.63f),new Vector2(.87f,.69f),19);
            PreviousButton=Nav("‹",panel,.63f,()=>SelectClass(SelectedIndex-1));Fit(PreviousButton,.02f,.63f,.13f,.69f);
            NextButton=Nav("›",panel,.63f,()=>SelectClass(SelectedIndex+1));Fit(NextButton,.87f,.63f,.98f,.69f);
            var diff=Nav("Difficulty",panel,0,()=>{RunDifficulty.Selected=(Difficulty)(((int)RunDifficulty.Selected+1)%RunDifficulty.Names.Length);RefreshDifficulty();});
            difficulty=diff.GetComponentInChildren<Text>();difficulty.fontSize=16;RefreshDifficulty();
            MenuUI.Text("ALTERNATIVE MENU  /  WORK IN PROGRESS",safe,new Vector2(.69f,.018f),new Vector2(.985f,.05f),11,MenuUI.Gold);
            var buttons=new[]{PreviousButton,NextButton,StartButton,RushButton,SettingsButton,ExitButton,diff};
            for(int i=0;i<buttons.Length;i++)buttons[i].navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnDown=buttons[(i+1)%buttons.Length],selectOnUp=buttons[(i+buttons.Length-1)%buttons.Length],selectOnLeft=i==0||i==1?PreviousButton:null,selectOnRight=i==0||i==1?NextButton:null};
            BuildSettings();
            message=MenuUI.Text("",safe,new Vector2(.70f,.01f),new Vector2(.99f,.08f),14,MenuUI.Gold);
        }
        void RefreshDifficulty(){difficulty.text="Difficulty: "+RunDifficulty.Name(RunDifficulty.Selected);}
        static void Fit(Button b,float x,float y,float x2,float y2){var r=(RectTransform)b.transform;r.anchorMin=new Vector2(x,y);r.anchorMax=new Vector2(x2,y2);r.offsetMin=r.offsetMax=Vector2.zero;}
        Button Nav(string label,Transform parent,float y,UnityEngine.Events.UnityAction action)
        {
            var b=MenuUI.Button(label,parent,new Vector2(0,y),new Vector2(1,y+.09f),action);
            Destroy(b.GetComponent<Outline>());b.GetComponent<Image>().color=new Color(.06f,.045f,.02f,.12f);
            var t=b.GetComponentInChildren<Text>();t.fontSize=30;t.resizeTextForBestFit=true;t.resizeTextMinSize=14;t.resizeTextMaxSize=30;
            return b;
        }
        public void StartRun(){if(Loading||SettingsOpen)return;Loading=MenuLaunch.Normal(SavedHeroSelection.Current);if(!Loading)message.text="Run scenes unavailable in this build.";}
        public void BossRush()
        {
            if(Loading||SettingsOpen)return;
            // Existing Boss-Rush Test Rush setup, not a new game mode.
            TestRushSetup.SavedHero=SavedHeroSelection.Current;HeroSelection.TestRush=true;HeroSelection.ResumeSetup=true;
            Loading=true;SceneManager.LoadSceneAsync(HeroSelection.ScenePath);
        }
        public void Exit()
        {
            if(Loading||SettingsOpen)return;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
#elif UNITY_WEBGL
            SceneManager.LoadScene(MenuAppearance.ScenePath);
#else
            Application.Quit();
#endif
        }
        void BuildSettings()
        {
            var block=MenuUI.Panel("Settings modal",safe,Vector2.zero,Vector2.one,new Color(.008f,.012f,.025f,.95f));block.raycastTarget=true;settings=block.gameObject;
            var panel=MenuUI.Rect("Settings controls",block.transform,new Vector2(.28f,.12f),new Vector2(.72f,.88f));
            MenuUI.Text("S E T T I N G S",panel,new Vector2(0,.86f),Vector2.one,32,MenuUI.Gold);
            MenuUI.Text("Music volume",panel,new Vector2(0,.68f),new Vector2(.4f,.78f),22);
            var track=MenuUI.Panel("Music volume slider",panel,new Vector2(.45f,.71f),new Vector2(.95f,.75f),new Color(.18f,.18f,.22f));track.raycastTarget=true;
            var fill=MenuUI.Panel("Fill",track.transform,Vector2.zero,Vector2.one,MenuUI.Gold);
            var handle=MenuUI.Panel("Handle",track.transform,new Vector2(0,-.7f),new Vector2(0,1.7f),MenuUI.Ivory);handle.rectTransform.sizeDelta=new Vector2(15,0);handle.raycastTarget=true;
            var slider=track.gameObject.AddComponent<Slider>();slider.fillRect=fill.rectTransform;slider.handleRect=handle.rectTransform;slider.targetGraphic=handle;slider.value=Music.Volume;slider.onValueChanged.AddListener(Music.SetVolume);
            var modes=MenuSettings.Resolutions;int resolution=Mathf.Max(0,Array.FindIndex(modes,r=>r.x==Screen.width&&r.y==Screen.height));int mode=MenuSettings.DisplayMode,quality=QualitySettings.GetQualityLevel();
            Button display=null,res=null,q=null;
            display=Nav(mode==0?"Windowed":"Borderless",panel,.53f,()=>{mode=1-mode;display.GetComponentInChildren<Text>().text=mode==0?"Windowed":"Borderless";});
            res=Nav(modes[resolution].x+" × "+modes[resolution].y,panel,.39f,()=>{resolution=(resolution+1)%modes.Length;res.GetComponentInChildren<Text>().text=modes[resolution].x+" × "+modes[resolution].y;});
            q=Nav("Quality: "+QualitySettings.names[quality],panel,.25f,()=>{quality=(quality+1)%QualitySettings.names.Length;q.GetComponentInChildren<Text>().text="Quality: "+QualitySettings.names[quality];});
            UISettingsButton=Nav("UI",panel,.13f,OpenUISettings);
            UISettings=MenuAppearancePanel.Create(safe,()=>{settings.SetActive(true);EventSystem.current.SetSelectedGameObject(UISettingsButton.gameObject);});
            var apply=Nav("Apply",panel,.025f,()=>MenuSettings.Apply(modes[resolution].x,modes[resolution].y,mode,quality));
            settingsBack=Nav("Back",panel,-.08f,CloseSettings);
#if UNITY_WEBGL && !UNITY_EDITOR
            display.interactable=res.interactable=false;res.GetComponentInChildren<Text>().text="Use browser fullscreen";
#endif
            Selectable[] order={slider,display,res,q,UISettingsButton,apply,settingsBack};
            for(int i=0;i<order.Length;i++)order[i].navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnUp=order[(i+order.Length-1)%order.Length],selectOnDown=order[(i+1)%order.Length]};
            settings.SetActive(false);
        }
        public void OpenSettings(){if(Loading)return;settings.SetActive(true);navigation.interactable=navigation.blocksRaycasts=false;EventSystem.current.SetSelectedGameObject(settingsBack.gameObject);}
        public void OpenUISettings(){if(Loading||!SettingsOpen)return;settings.SetActive(false);UISettings.Open();}
        public void CloseSettings(){if(UISettings)UISettings.Hide();settings.SetActive(false);navigation.interactable=navigation.blocksRaycasts=true;EventSystem.current.SetSelectedGameObject(SettingsButton.gameObject);}
        void Update()
        {
            if(Hero)Hero.Tick(null);if(Boss)Boss.TickBoss("Idle",.65f);
            if(!safe)return;
            var area=Screen.safeArea;safe.anchorMin=area.position/new Vector2(Screen.width,Screen.height);safe.anchorMax=(area.position+area.size)/new Vector2(Screen.width,Screen.height);
            // Keep vertical field of view, widen for 4:3; no moving cinematic camera behind UI.
            View.fieldOfView=Mathf.Clamp(42*Mathf.Max(1,1.77778f/View.aspect),42,62);
            if(Keyboard.current?.escapeKey.wasPressedThisFrame==true||Gamepad.current?.buttonEast.wasPressedThisFrame==true){if(UISettings&&UISettings.IsOpen)UISettings.Close();else if(SettingsOpen)CloseSettings();}
            if(Loading)navigation.interactable=false;
        }
        void OnDestroy()
        {
            foreach(var p in heroes.Concat(new[]{Boss}))if(p)foreach(var r in p.GetComponentsInChildren<Renderer>(true))foreach(var m in r.sharedMaterials)if(m&&m.name.EndsWith("(Instance)"))Destroy(m);
            if(gradientSprite)Destroy(gradientSprite);if(gradient)Destroy(gradient);
        }
    }
}
