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
        GameObject[] chosenMarks;
        static Texture2D fadeTexture;
        GameObject selectedBackdrop;
        readonly Dictionary<SkinnedMeshRenderer,Mesh> baked=new Dictionary<SkinnedMeshRenderer,Mesh>();
        readonly Dictionary<int,Bounds> boundsCache=new Dictionary<int,Bounds>();
        readonly Dictionary<int,List<Bounds>> partBounds=new Dictionary<int,List<Bounds>>();
        readonly Dictionary<int,float> boundsTime=new Dictionary<int,float>();
        readonly List<RaycastResult> uiHits=new List<RaycastResult>();
        readonly List<GameObject> hiddenUi=new List<GameObject>();
        readonly List<GameObject> hiddenStage=new List<GameObject>();
        // Boss Rush select shows the live arena showcase; park this scene's camera, models and lights meanwhile.
        public bool StageHidden {get;private set;}
        public void SetStageHidden(bool hidden)
        {
            if(StageHidden==hidden)return;StageHidden=hidden;
            if(View)View.enabled=!hidden;
            foreach(var p in Presenters)if(p){var root=p.transform.parent&&p.transform.parent!=transform?p.transform.parent:p.transform;root.gameObject.SetActive(!hidden);}
            // The selection scene is built on the Key Art arena (dais, clouds, sun, post volume). Park every root except this
            // controller and the camera object (its AudioListener stays live) so nothing overlaps or tints the showcase.
            if(hidden){hiddenStage.Clear();foreach(var node in gameObject.scene.GetRootGameObjects())if(node!=gameObject&&(!View||node!=View.gameObject)&&node.activeSelf){hiddenStage.Add(node);node.SetActive(false);}}
            else{foreach(var node in hiddenStage)if(node)node.SetActive(true);hiddenStage.Clear();}
        }
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
            // Opaque bands frame the stage; 64 px gradient fades soften where each band meets the scene.
            var upper=MenuUI.Panel("Upper frame",canvas.transform,new Vector2(0,.84f),Vector2.one,opaqueInk);Fade("Upper fade",upper.transform,true,opaqueInk);
            var footer=MenuUI.Panel("Footer frame",canvas.transform,Vector2.zero,new Vector2(1,.17f),opaqueInk);Fade("Footer fade",footer.transform,false,opaqueInk);
            var selectedFrame=MenuUI.Panel("Selected frame",canvas.transform,Vector2.zero,new Vector2(1,.405f),opaqueInk);Fade("Selected fade",selectedFrame.transform,false,opaqueInk);
            selectedBackdrop=selectedFrame.gameObject;selectedBackdrop.SetActive(false);
            MenuUI.Panel("Header",canvas.transform,new Vector2(0,.87f),Vector2.one);
            heading=MenuUI.Text(TestRush?"BOSS-RUSH TEST RUSH  /  CHOOSE YOUR HERO":"LAST BELL  /  CHOOSE YOUR HERO",canvas.transform,new Vector2(.1f,.92f),new Vector2(.9f,.99f),30,MenuUI.Gold);
            // Hero tabs: text tabs on a thin gold rail. Focus = drawn diamond + underline (MenuFocus); the chosen hero
            // additionally keeps a bright full-width underline with a centre diamond (shape cue, not colour alone).
            var rail=MenuUI.Panel("Tab rail",canvas.transform,new Vector2(.17f,.84f),new Vector2(.83f,.84f),UiTheme.GoldDim);
            rail.rectTransform.pivot=new Vector2(.5f,0);rail.rectTransform.sizeDelta=new Vector2(0,1);rail.rectTransform.anchoredPosition=new Vector2(0,7);
            HeroButtons=new Button[RunDirector.Classes.Length];chosenMarks=new GameObject[HeroButtons.Length];
            for(int i=0;i<HeroButtons.Length;i++){int at=i;float x=.17f+i*.22f;HeroButtons[i]=MenuUI.Button(RunDirector.Classes[i],canvas.transform,new Vector2(x,.84f),new Vector2(x+.22f,.92f),()=>SelectHero(at));chosenMarks[i]=TextTab(HeroButtons[i]);}
            RefreshTabs();
            BackButton=MenuUI.Button("Back",canvas.transform,new Vector2(.03f,.025f),new Vector2(.18f,.095f),Back);
            instructions=MenuUI.Text("CLICK A HERO OR USE ARROW KEYS + ENTER",canvas.transform,new Vector2(.25f,.10f),new Vector2(.85f,.15f),18,MenuUI.Gold);
            // Same baseline and height as Back / Difficulty; the only Gold-fill primary on this screen.
            SelectButton=MenuUI.Button("Select Class",canvas.transform,new Vector2(.39f,.025f),new Vector2(.61f,.095f),ConfirmClass);SelectButton.GetComponent<MenuFocus>().Primary=true;SelectButton.gameObject.SetActive(false);
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
            SavedHeroSelection.Current=RunDirector.Classes[i];
            DisplayKit=KitFor(RunDirector.Classes[i]);selectedBackdrop.SetActive(true);BuildSkills();SelectButton.gameObject.SetActive(true);instructions.gameObject.SetActive(false);
            RefreshTabs();LinkNavigation();
            EventSystem.current.SetSelectedGameObject(HeroButtons[i].gameObject);
        }
        public static PlayerKit KitFor(string hero)=>hero=="Chimesinger"?new ChimesingerKit():hero=="Arcanist"?new ArcanistKit():new VanguardKit2();
        void BuildSkills()
        {
            if(skills){skills.SetActive(false);Destroy(skills);}
            skills=MenuUI.Panel("Class skills",canvas.transform,new Vector2(0,.105f),new Vector2(1,.365f)).gameObject;
            int n=DisplayKit.SlotNames.Length;SkillDescriptions=new Text[n];
            // Card grid (1080p reference units): 24 padding, 56 px icon top-left, Heading title beside it, key chip
            // under the title, Body 17 below with room for four-plus lines. Every element is left-aligned.
            const float pad=UiTheme.G3,iconSize=56,textX=pad+iconSize+UiTheme.G2,bodyTop=pad+iconSize+UiTheme.G2;
            for(int j=0;j<n;j++)
            {
                float x=.025f+j*.95f/n;
                var card=MenuUI.FramedPanel(DisplayKit.SlotNames[j],skills.transform,new Vector2(x,.04f),new Vector2(x+.95f/n,.94f),new Color(.055f,.068f,.10f,.98f),false);
                card.rectTransform.offsetMin=new Vector2(UiTheme.G1,0);card.rectTransform.offsetMax=new Vector2(-UiTheme.G1,0);
                var icon=MenuUI.Rect("Skill icon",card.transform,new Vector2(0,1),new Vector2(0,1)).gameObject.AddComponent<RawImage>();
                Pin(icon.rectTransform,new Vector2(pad,-pad),new Vector2(iconSize,iconSize));
                icon.texture=RunHud.SlotIcon(RunDirector.Classes[SelectedIndex],j);icon.raycastTarget=false;
                var title=MenuUI.Text(DisplayKit.SlotNames[j],card.transform,new Vector2(0,1),Vector2.one,UiTheme.SizeHeading,UiTheme.Ivory,TextAnchor.MiddleLeft);
                title.rectTransform.offsetMin=new Vector2(textX,-pad-30);title.rectTransform.offsetMax=new Vector2(-pad,-pad+4);
                title.resizeTextForBestFit=true;title.resizeTextMinSize=15;title.resizeTextMaxSize=UiTheme.SizeHeading;
                var chip=MenuUI.Panel("Key chip",card.transform,new Vector2(0,1),new Vector2(0,1),UiTheme.InkDeep);
                chip.rectTransform.pivot=new Vector2(0,1);chip.rectTransform.anchoredPosition=new Vector2(textX,-pad-34);
                var chipEdge=chip.gameObject.AddComponent<Outline>();chipEdge.effectColor=UiTheme.GoldDim;chipEdge.effectDistance=Vector2.one;
                var chipRow=chip.gameObject.AddComponent<HorizontalLayoutGroup>();chipRow.padding=new RectOffset(8,8,1,1);
                chipRow.childControlWidth=true;chipRow.childControlHeight=true;chipRow.childForceExpandWidth=false;chipRow.childForceExpandHeight=false;
                var chipFit=chip.gameObject.AddComponent<ContentSizeFitter>();chipFit.horizontalFit=ContentSizeFitter.FitMode.PreferredSize;chipFit.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
                var key=MenuUI.Text(DisplayKit.SlotKeys[j],chip.transform,Vector2.zero,Vector2.one,UiTheme.SizeNumeric,UiTheme.GoldBright);
                key.font=UiTheme.SemiBold;key.horizontalOverflow=HorizontalWrapMode.Overflow;
                var body=MenuUI.Text(Description(DisplayKit,j),card.transform,Vector2.zero,Vector2.one,UiTheme.SizeBody,UiTheme.IvoryBody,TextAnchor.UpperLeft);
                body.rectTransform.offsetMin=new Vector2(pad,pad);body.rectTransform.offsetMax=new Vector2(-pad,-bodyTop);
                SkillDescriptions[j]=body;
            }
            if(movementCaption)Destroy(movementCaption);
            // Icon sits inline, immediately left of the centred caption: a centred row sized from the text's preferredWidth.
            var movement=MenuUI.Rect("Movement",canvas.transform,new Vector2(.04f,.365f),new Vector2(.96f,.402f));movementCaption=movement.gameObject;
            var movementRow=movement.gameObject.AddComponent<HorizontalLayoutGroup>();movementRow.childAlignment=TextAnchor.MiddleCenter;movementRow.spacing=10;
            movementRow.childControlWidth=true;movementRow.childControlHeight=true;movementRow.childForceExpandWidth=false;movementRow.childForceExpandHeight=false;
            DashIcon=MenuUI.Rect("Dash icon",movement,Vector2.zero,Vector2.zero).gameObject.AddComponent<RawImage>();DashIcon.texture=RunHud.SlotIcon(RunDirector.Classes[SelectedIndex],n);DashIcon.raycastTarget=false;
            var dashSize=DashIcon.gameObject.AddComponent<LayoutElement>();dashSize.minWidth=dashSize.preferredWidth=28;dashSize.minHeight=dashSize.preferredHeight=28;
            var dashName=RunDirector.Classes[SelectedIndex]=="Chimesinger"?"Harmonic Step":RunDirector.Classes[SelectedIndex]=="Arcanist"?"Clockstep":"Dash";
            var caption=MenuUI.Text($"MOVEMENT / {dashName} [5 / RMB]   •   {PlayerController.DashDistance} m in {PlayerController.DashTicks/(float)Sim.Hz:0.0} s   •   {PlayerController.DashCost} stamina   •   No invincibility",movement,Vector2.zero,Vector2.one,17,MenuUI.Gold);
            caption.horizontalOverflow=HorizontalWrapMode.Overflow;
        }
        static void Pin(RectTransform r,Vector2 topLeft,Vector2 size){r.pivot=new Vector2(0,1);r.sizeDelta=size;r.anchoredPosition=topLeft;}
        // Turns a house button into a text tab: no box fill or edge; the label, focus diamond and focus underline stay.
        // Returns the chosen-state mark (bright underline + centre diamond), toggled by RefreshTabs.
        static GameObject TextTab(Button tab)
        {
            var focus=tab.GetComponent<MenuFocus>();focus.Fill=null;focus.Edge=null;
            tab.GetComponent<Image>().color=new Color(0,0,0,0);var edge=tab.GetComponent<Outline>();if(edge)edge.enabled=false;if(focus.Stroke){focus.Stroke.enabled=false;focus.Stroke=null;}
            var mark=MenuUI.Rect("Chosen mark",tab.transform,new Vector2(.12f,0),new Vector2(.88f,0));mark.pivot=new Vector2(.5f,.5f);mark.sizeDelta=new Vector2(0,2);mark.anchoredPosition=Vector2.zero;
            var line=mark.gameObject.AddComponent<Image>();line.color=UiTheme.GoldBright;line.raycastTarget=false;
            var diamond=MenuUI.Rect("Chosen diamond",mark,new Vector2(.5f,.5f),new Vector2(.5f,.5f)).gameObject.AddComponent<FocusDiamond>();
            diamond.rectTransform.sizeDelta=new Vector2(9,9);diamond.color=UiTheme.GoldBright;diamond.raycastTarget=false;
            mark.gameObject.SetActive(false);return mark.gameObject;
        }
        void RefreshTabs(){if(chosenMarks!=null)for(int i=0;i<chosenMarks.Length;i++)if(chosenMarks[i])chosenMarks[i].SetActive(i==SelectedIndex);}
        // Opaque-to-clear 64 px fade hung off a band edge: below the band (top edge of the fade opaque) or above it.
        static void Fade(string name,Transform band,bool below,Color ink)
        {
            if(!fadeTexture)
            {
                fadeTexture=new Texture2D(1,64,TextureFormat.RGBA32,false){hideFlags=HideFlags.HideAndDontSave,wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
                for(int y=0;y<64;y++){float t=y/63f;fadeTexture.SetPixel(0,y,new Color(1,1,1,t*t*(3-2*t)));}
                fadeTexture.Apply();
            }
            float edge=below?0:1;
            var r=MenuUI.Rect(name,band,new Vector2(0,edge),new Vector2(1,edge));r.pivot=new Vector2(.5f,below?1:0);r.sizeDelta=new Vector2(0,64);r.anchoredPosition=Vector2.zero;
            var img=r.gameObject.AddComponent<RawImage>();img.texture=fadeTexture;img.color=ink;img.raycastTarget=false;
            // The texture is opaque at its top row; flip it for fades that rise above a band.
            img.uvRect=below?new Rect(0,0,1,1):new Rect(0,1,1,-1);
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
                TestRushSetup.SavedHero=RunDirector.Classes[SelectedIndex];ConfrontationMenu.ResumeBossRush=true;SceneManager.LoadScene(ConfrontationMenu.ScenePath);return;   // retired scene: Boss Rush lives on the main menu
            }
            if(!MenuLaunch.Normal(RunDirector.Classes[SelectedIndex]))return;
            Loading=true;SelectButton.interactable=false;foreach(var b in HeroButtons)b.interactable=false;BackButton.interactable=false;DifficultyButton.interactable=false;
        }
        public void CloseSetup(){Setup=null;foreach(var node in hiddenUi)if(node)node.SetActive(true);hiddenUi.Clear();if(skills)skills.SetActive(true);if(movementCaption)movementCaption.SetActive(true);SelectButton.gameObject.SetActive(true);EventSystem.current.SetSelectedGameObject(SelectButton.gameObject);}
        public void Back()
        {if(Loading)return;if(SelectedIndex>=0){SelectedIndex=-1;if(skills){skills.SetActive(false);Destroy(skills);}if(movementCaption)Destroy(movementCaption);selectedBackdrop.SetActive(false);SelectButton.gameObject.SetActive(false);instructions.gameObject.SetActive(true);heading.text=TestRush?"BOSS-RUSH TEST RUSH  /  CHOOSE YOUR HERO":"LAST BELL  /  CHOOSE YOUR HERO";RefreshTabs();LinkNavigation();EventSystem.current.SetSelectedGameObject(HeroButtons[0].gameObject);return;}SceneManager.LoadScene(MenuAppearance.ScenePath);}
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
            // Selected view: a neighbouring hero that would be sliced by the screen edge (e.g. the Arcanist beside the
            // Vanguard at 16:9) is pulled fully into frame when that costs at most 30% extra distance; heroes further out
            // stay entirely off-frame. The selected hero's own fit above is never reduced, so PresentationRect still holds it.
            if(SelectedIndex>=0)
            {
                float own=distance;
                for(int i=0;i<Presenters.Count;i++)
                {
                    if(i==SelectedIndex)continue;float need=0;
                    foreach(var corner in ModelCorners(i)){var v=inv*(corner-b.center);need=Mathf.Max(need,Mathf.Abs(v.x)/(tan*presentationAspect*.90f)-v.z,Mathf.Abs(v.y)/(tan*.90f)-v.z);}
                    if(need>distance&&need<=own*1.3f)distance=need;
                }
            }
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

