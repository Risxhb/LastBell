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
    public sealed class HeroSelection : MonoBehaviour
    {
        public const string ScenePath="Assets/LastBell/Menu/HeroSelection.unity";
        public static bool TestRush;
        public static bool ResumeSetup;
        public Button BackButton {get;private set;}
        public Button[] HeroButtons {get;private set;}
        public readonly List<CharacterPresenter> Presenters=new List<CharacterPresenter>();
        public int SelectedIndex {get;private set;}=-1;
        public Camera View {get;private set;}
        public Rect PresentationRect {get;private set;}
        public Button SelectButton {get;private set;}
        public PlayerKit DisplayKit {get;private set;}
        public RawImage DashIcon {get;private set;}
        public Text[] SkillDescriptions {get;private set;}
        public bool Loading {get;private set;}
        public Button DifficultyButton {get;private set;}
        public TestRushSetup Setup {get;private set;}
        Canvas canvas; Text heading; GameObject skills;Text instructions;GameObject movementCaption;
        Vector3 cameraVelocity;bool cameraInitialized;
        GameObject selectedBackdrop;
        readonly Dictionary<SkinnedMeshRenderer,Mesh> baked=new Dictionary<SkinnedMeshRenderer,Mesh>();
        readonly Dictionary<int,Bounds> boundsCache=new Dictionary<int,Bounds>();
        readonly Dictionary<int,List<Bounds>> partBounds=new Dictionary<int,List<Bounds>>();
        readonly Dictionary<int,float> boundsTime=new Dictionary<int,float>();
        readonly List<RaycastResult> uiHits=new List<RaycastResult>();
        readonly List<GameObject> hiddenUi=new List<GameObject>();
        void Start()
        {
            View=Camera.main; View.orthographic=false;View.fieldOfView=34;
            View.clearFlags=CameraClearFlags.SolidColor;View.backgroundColor=new Color(.018f,.028f,.055f);
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogStartDistance=7;RenderSettings.fogEndDistance=25;RenderSettings.fogColor=new Color(.025f,.035f,.065f);
            View.transform.SetPositionAndRotation(new Vector3(0,2.7f,10.5f),Quaternion.LookRotation(new Vector3(0,1,0)-new Vector3(0,2.7f,10.5f)));
            for(int i=0;i<RunDirector.Classes.Length;i++)
            {
                var root=new GameObject(RunDirector.Classes[i]+" presentation").transform;root.SetParent(transform,false);root.position=new Vector3((1-i)*2.6f,0,i==1?-.4f:0);
                var p=CharacterPresenter.ForPlayer(root,RunDirector.Classes[i]);Presenters.Add(p);
                var light=new GameObject("Hero rim light",typeof(Light)).GetComponent<Light>();light.transform.SetParent(root,false);light.transform.localPosition=new Vector3(0,2.5f,-1);
                light.type=LightType.Point;light.color=i==0?new Color(1,.7f,.35f):i==1?new Color(.3f,.85f,1):new Color(.6f,.4f,1);light.intensity=5;light.range=5;
            }
            canvas=MenuUI.Canvas(transform);
            var opaqueInk=new Color(.025f,.035f,.065f,1);
            MenuUI.Panel("Upper frame",canvas.transform,new Vector2(0,.84f),Vector2.one,opaqueInk);
            MenuUI.Panel("Footer frame",canvas.transform,Vector2.zero,new Vector2(1,.17f),opaqueInk);
            selectedBackdrop=MenuUI.Panel("Selected frame",canvas.transform,Vector2.zero,new Vector2(1,.405f),opaqueInk).gameObject;selectedBackdrop.SetActive(false);
            MenuUI.Panel("Header",canvas.transform,new Vector2(0,.87f),Vector2.one);
            heading=MenuUI.Text(TestRush?"BOSS-RUSH TEST RUSH  /  CHOOSE YOUR HERO":"LAST BELL  /  CHOOSE YOUR HERO",canvas.transform,new Vector2(.1f,.92f),new Vector2(.9f,.99f),30,MenuUI.Gold);
            HeroButtons=new Button[RunDirector.Classes.Length];
            for(int i=0;i<HeroButtons.Length;i++){int at=i;float x=.17f+i*.22f;HeroButtons[i]=MenuUI.Button(RunDirector.Classes[i],canvas.transform,new Vector2(x,.84f),new Vector2(x+.22f,.92f),()=>SelectHero(at));}
            BackButton=MenuUI.Button("Back",canvas.transform,new Vector2(.03f,.025f),new Vector2(.18f,.095f),Back);
            instructions=MenuUI.Text("CLICK A HERO OR USE ARROW KEYS + ENTER",canvas.transform,new Vector2(.25f,.10f),new Vector2(.85f,.15f),18,MenuUI.Gold);
            SelectButton=MenuUI.Button("Select Class",canvas.transform,new Vector2(.39f,.015f),new Vector2(.61f,.09f),ConfirmClass);SelectButton.gameObject.SetActive(false);
            DifficultyButton=MenuUI.Button("Difficulty: "+RunDifficulty.Name(RunDifficulty.Selected),canvas.transform,new Vector2(.72f,.025f),new Vector2(.97f,.095f),CycleDifficulty);
            LinkNavigation();
            EventSystem.current.SetSelectedGameObject(HeroButtons[0].gameObject);
            if(TestRush&&ResumeSetup){ResumeSetup=false;int hero=System.Array.IndexOf(RunDirector.Classes,TestRushSetup.SavedHero);SelectHero(Mathf.Max(0,hero));ConfirmClass();}
        }
        public Bounds ModelBounds(int i)
        {
            if(boundsTime.TryGetValue(i,out float at)&&Time.unscaledTime-at<.2f)return boundsCache[i];
            var renderers=Presenters[i].GetComponentsInChildren<Renderer>();var b=new Bounds();bool first=true;
            if(!partBounds.TryGetValue(i,out var parts)){parts=new List<Bounds>();partBounds[i]=parts;}parts.Clear();
            foreach(var r in renderers)
            {
                Bounds world=r.bounds;
                if(r is SkinnedMeshRenderer skin)
                {
                    if(!baked.TryGetValue(skin,out var mesh)){mesh=new Mesh();baked[skin]=mesh;}
                    skin.BakeMesh(mesh);world=new Bounds(skin.transform.TransformPoint(mesh.bounds.center),Vector3.zero);
                    foreach(var c in Corners(mesh.bounds))world.Encapsulate(skin.transform.TransformPoint(c));
                }
                world.Expand(.12f);parts.Add(world);
                if(first){b=world;first=false;}else b.Encapsulate(world);
            }
            if(first)b=new Bounds(Presenters[i].transform.position+Vector3.up,Vector3.one);
            boundsCache[i]=b;boundsTime[i]=Time.unscaledTime;return b;
        }
        public IEnumerable<Vector3> ModelCorners(int i)
        {ModelBounds(i);foreach(var part in partBounds[i])foreach(var corner in Corners(part))yield return corner;}
        public void SelectHero(int i)
        {
            if(Loading||i<0||i>=HeroButtons.Length)return;SelectedIndex=i;heading.text=RunDirector.Classes[i].ToUpperInvariant()+"  /  CHOOSE YOUR HERO";
            DisplayKit=KitFor(RunDirector.Classes[i]);selectedBackdrop.SetActive(true);BuildSkills();SelectButton.gameObject.SetActive(true);instructions.gameObject.SetActive(false);
            LinkNavigation();
            EventSystem.current.SetSelectedGameObject(HeroButtons[i].gameObject);
        }
        public static PlayerKit KitFor(string hero)=>hero=="Chimesinger"?new ChimesingerKit():hero=="Arcanist"?new ArcanistKit():new VanguardKit2();
        void BuildSkills()
        {
            if(skills){skills.SetActive(false);Destroy(skills);}
            skills=MenuUI.Panel("Class skills",canvas.transform,new Vector2(0,.105f),new Vector2(1,.365f)).gameObject;
            int n=DisplayKit.SlotNames.Length;SkillDescriptions=new Text[n];
            for(int j=0;j<n;j++)
            {
                float x=.025f+j*.95f/n;var card=MenuUI.Panel(DisplayKit.SlotNames[j],skills.transform,new Vector2(x,.04f),new Vector2(x+.95f/n-.006f,.94f),new Color(.055f,.068f,.10f,.98f));
                var icon=MenuUI.Rect("Skill icon",card.transform,new Vector2(.04f,.65f),new Vector2(.22f,.97f)).gameObject.AddComponent<RawImage>();icon.texture=RunHud.SlotIcon(RunDirector.Classes[SelectedIndex],j);icon.raycastTarget=false;
                MenuUI.Text(DisplayKit.SlotNames[j],card.transform,new Vector2(.23f,.72f),new Vector2(.99f,.98f),22,MenuUI.Gold);
                MenuUI.Text("[ "+DisplayKit.SlotKeys[j]+" ]",card.transform,new Vector2(.23f,.55f),new Vector2(.99f,.76f),19);
                SkillDescriptions[j]=MenuUI.Text(Description(DisplayKit,j),card.transform,new Vector2(.02f,.05f),new Vector2(.98f,.59f),20,null,TextAnchor.UpperLeft);
            }
            if(movementCaption)Destroy(movementCaption);
            var movement=MenuUI.Rect("Movement",canvas.transform,new Vector2(.15f,.365f),new Vector2(.85f,.402f));movementCaption=movement.gameObject;
            DashIcon=MenuUI.Rect("Dash icon",movement,Vector2.zero,new Vector2(.04f,1)).gameObject.AddComponent<RawImage>();DashIcon.texture=RunHud.SlotIcon(RunDirector.Classes[SelectedIndex],n);DashIcon.raycastTarget=false;
            var dashName=RunDirector.Classes[SelectedIndex]=="Chimesinger"?"Harmonic Step":RunDirector.Classes[SelectedIndex]=="Arcanist"?"Clockstep":"Dash";
            MenuUI.Text($"MOVEMENT / {dashName} [5 / RMB]   •   {PlayerController.DashDistance} m in {PlayerController.DashTicks/(float)Sim.Hz:0.0} s   •   {PlayerController.DashCost} stamina   •   No invincibility",movement,new Vector2(.045f,0),Vector2.one,17,MenuUI.Gold);
        }
        public static string Description(PlayerKit kit,int slot)
        {
            var body=string.Join(" ",kit.Tooltip(slot).Split('\n').Skip(1).Take(2));
            return string.Join(" ",System.Text.RegularExpressions.Regex.Split(body,@"(?<=[.!?])\s+").Take(2));
        }
        public string LastPick {get;private set;}
        void LinkNavigation()
        {
            for(int i=0;i<HeroButtons.Length;i++)HeroButtons[i].navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnLeft=HeroButtons[(i+HeroButtons.Length-1)%HeroButtons.Length],selectOnRight=HeroButtons[(i+1)%HeroButtons.Length],selectOnDown=SelectedIndex<0?DifficultyButton:SelectButton,selectOnUp=BackButton};
            SelectButton.navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnUp=HeroButtons[0],selectOnLeft=BackButton,selectOnRight=DifficultyButton};
            BackButton.navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnUp=HeroButtons[0],selectOnRight=SelectedIndex<0?DifficultyButton:SelectButton,selectOnLeft=HeroButtons[0]};
            DifficultyButton.navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnUp=HeroButtons[HeroButtons.Length-1],selectOnLeft=SelectedIndex<0?BackButton:SelectButton,selectOnRight=BackButton};
        }
        public void CycleDifficulty()
        {if(Loading)return;RunDifficulty.Selected=(Difficulty)(((int)RunDifficulty.Selected+1)%RunDifficulty.Names.Length);DifficultyButton.GetComponentInChildren<Text>(true).text="Difficulty: "+RunDifficulty.Name(RunDifficulty.Selected);}
        public void ConfirmClass()
        {
            if(Loading||SelectedIndex<0||Setup)return;
            if(TestRush)
            {
                if(skills)skills.SetActive(false);if(movementCaption)movementCaption.SetActive(false);SelectButton.gameObject.SetActive(false);
                // Opaque visuals alone do not exclude underlying buttons from automatic keyboard navigation.
                // Suspend the background UI while setup owns focus, then restore its previous active elements.
                hiddenUi.Clear();foreach(Transform child in canvas.transform)if(child.gameObject.activeSelf){hiddenUi.Add(child.gameObject);child.gameObject.SetActive(false);}
                Setup=new GameObject("Test Rush controls").AddComponent<TestRushSetup>();Setup.Build(this,canvas.transform,RunDirector.Classes[SelectedIndex]);return;
            }
            if(!MenuLaunch.Normal(RunDirector.Classes[SelectedIndex]))return;
            Loading=true;SelectButton.interactable=false;foreach(var b in HeroButtons)b.interactable=false;BackButton.interactable=false;DifficultyButton.interactable=false;
        }
        public void CloseSetup(){Setup=null;foreach(var node in hiddenUi)if(node)node.SetActive(true);hiddenUi.Clear();if(skills)skills.SetActive(true);if(movementCaption)movementCaption.SetActive(true);SelectButton.gameObject.SetActive(true);EventSystem.current.SetSelectedGameObject(SelectButton.gameObject);}
        public void Back()
        {if(Loading)return;if(SelectedIndex>=0){SelectedIndex=-1;if(skills){skills.SetActive(false);Destroy(skills);}if(movementCaption)Destroy(movementCaption);selectedBackdrop.SetActive(false);SelectButton.gameObject.SetActive(false);instructions.gameObject.SetActive(true);heading.text=TestRush?"BOSS-RUSH TEST RUSH  /  CHOOSE YOUR HERO":"LAST BELL  /  CHOOSE YOUR HERO";LinkNavigation();EventSystem.current.SetSelectedGameObject(HeroButtons[0].gameObject);return;}SceneManager.LoadScene(MainMenu.ScenePath);}
        void LateUpdate()
        {
            if(!View||Presenters.Count==0)return;
            var targetRect=SelectedIndex<0?new Rect(0,.17f,1,.67f):new Rect(0,.405f,1,.435f);
            PresentationRect=targetRect;
            // Keep scene rendering and overlay UI in one full-screen pixel coordinate system. Map the model's
            // virtual camera frustum into its presentation area while reserving the skills and navigation space.
            View.rect=new Rect(0,0,1,1);View.aspect=Screen.width/(float)Screen.height;
            float presentationAspect=Screen.width*targetRect.width/(Screen.height*targetRect.height);
            var viewport=Matrix4x4.identity;viewport.m00=targetRect.width;viewport.m11=targetRect.height;
            viewport.m03=2*targetRect.center.x-1;viewport.m13=2*targetRect.center.y-1;
            View.projectionMatrix=viewport*Matrix4x4.Perspective(View.fieldOfView,presentationAspect,View.nearClipPlane,View.farClipPlane);
            var b=ModelBounds(SelectedIndex<0?0:SelectedIndex);
            if(SelectedIndex<0)for(int i=1;i<Presenters.Count;i++)b.Encapsulate(ModelBounds(i));
            // Frame each rendered part's bounds. A single combined box creates empty corners between a forward weapon
            // and the head, unnecessarily shrinking the entire model. Every real attachment keeps its own margin.
            var rot=Quaternion.LookRotation(new Vector3(0,-.10f,-1));var inv=Quaternion.Inverse(rot);
            float tan=Mathf.Tan(View.fieldOfView*Mathf.Deg2Rad*.5f),distance=0;
            int firstHero=SelectedIndex<0?0:SelectedIndex,lastHero=SelectedIndex<0?Presenters.Count:SelectedIndex+1;
            for(int i=firstHero;i<lastHero;i++)foreach(var corner in ModelCorners(i))
            {var v=inv*(corner-b.center);distance=Mathf.Max(distance,Mathf.Abs(v.x)/(tan*presentationAspect*.90f)-v.z,Mathf.Abs(v.y)/(tan*.90f)-v.z);}
            var goal=b.center-rot*Vector3.forward*(distance+.08f);
            if(!cameraInitialized){View.transform.SetPositionAndRotation(goal,rot);cameraInitialized=true;}
            else{View.transform.position=Vector3.SmoothDamp(View.transform.position,goal,ref cameraVelocity,.22f,100,Time.unscaledDeltaTime);View.transform.rotation=Quaternion.Slerp(View.transform.rotation,rot,1-Mathf.Exp(-Time.unscaledDeltaTime*12));}
        }
        public static IEnumerable<Vector3> Corners(Bounds b)
        {for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)yield return b.center+Vector3.Scale(b.extents,new Vector3(x,y,z));}
        void Update()
        {
            var keyboard=MenuFlowTest.Requested?MenuSmokeTest.TestKeyboard:Keyboard.current;
            var gamepad=MenuFlowTest.Requested?MenuSmokeTest.TestGamepad:Gamepad.current;
            var mouse=MenuFlowTest.Requested?MenuSmokeTest.TestMouse:Mouse.current;
            if(Setup){if(keyboard?.escapeKey.wasPressedThisFrame==true||gamepad?.buttonEast.wasPressedThisFrame==true)Setup.Close();return;}
            if(keyboard?.escapeKey.wasPressedThisFrame==true || gamepad?.buttonEast.wasPressedThisFrame==true){Back();return;}
            if(mouse?.leftButton.wasPressedThisFrame==true && View)
            {
                // Query this click's position directly. Cached EventSystem pointer state can belong to another device.
                uiHits.Clear();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=mouse.position.ReadValue()},uiHits);
                LastPick="click="+mouse.position.ReadValue()+" UI="+string.Join(",",uiHits.Select(h=>h.gameObject.name));
                if(uiHits.Count>0)return;
                var ray=View.ScreenPointToRay(mouse.position.ReadValue());
                int picked=-1;float nearest=float.PositiveInfinity;
                for(int i=0;i<Presenters.Count;i++)if(ModelBounds(i).IntersectRay(ray,out float distance)&&distance<nearest){picked=i;nearest=distance;}
                LastPick+=" picked="+picked;
                if(picked>=0)SelectHero(picked);
            }
        }
        void OnDestroy()
        {
            foreach(var m in baked.Values)if(m)Destroy(m);
            foreach(var p in Presenters)if(p)foreach(var r in p.GetComponentsInChildren<Renderer>())foreach(var m in r.sharedMaterials)if(m&&m.name.EndsWith("(Instance)"))Destroy(m);
        }
    }
}
