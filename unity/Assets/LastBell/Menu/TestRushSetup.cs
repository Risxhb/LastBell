using LastBell.Run;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace LastBell.Menu
{
    public sealed class TestRushSetup:MonoBehaviour
    {
        public static int SavedMap,SavedEncounter;
        public static string SavedHero="Vanguard";
        public int MapIndex {get;private set;}
        public int EncounterIndex {get;private set;}
        public string Hero {get;private set;}
        public Button LaunchButton {get;private set;}
        public Button[] MapButtons {get;private set;}
        public Button[] EncounterButtons {get;private set;}
        public Text Summary {get;private set;}
        public Text Restriction {get;private set;}
        public bool Valid {get;private set;}
        public bool Loading {get;private set;}
        public bool OwnsFocus=>ownedPanel&&EventSystem.current.currentSelectedGameObject&&EventSystem.current.currentSelectedGameObject.transform.IsChildOf(ownedPanel.transform);
        Text[] mapLabels,encounterLabels;Text difficultyLabel;
        HeroSelection selection;
        public void Build(HeroSelection owner,Transform parent,string hero)
        {
            selection=owner;Hero=hero;SavedHero=hero;MapIndex=Mathf.Clamp(SavedMap,0,RunMaps.All.Length-1);EncounterIndex=Mathf.Clamp(SavedEncounter,0,RunDirector.Bosses.Length-1);
            var panel=MenuUI.Panel("Test Rush setup",parent,Vector2.zero,Vector2.one,new Color(.025f,.035f,.065f,1));panel.raycastTarget=true;transform.SetParent(panel.transform,false);
            // This component owns the full panel; changing heroes returns to the same 3D selection presentation.
            ownedPanel=panel.gameObject;
            MenuUI.Text("BOSS-RUSH TEST RUSH",panel.transform,new Vector2(.1f,.91f),new Vector2(.9f,.99f),36,MenuUI.Gold);
            MenuUI.Button("Hero: "+hero+"  /  Change",panel.transform,new Vector2(.08f,.815f),new Vector2(.48f,.90f),Close);
            var difficulty=MenuUI.Button("Difficulty: "+RunDifficulty.Name(RunDifficulty.Selected),panel.transform,new Vector2(.52f,.815f),new Vector2(.92f,.90f),CycleDifficulty);difficultyLabel=difficulty.GetComponentInChildren<Text>();
            MenuUI.Text("CHOOSE AN ARENA",panel.transform,new Vector2(.03f,.765f),new Vector2(.97f,.815f),21,MenuUI.Gold,TextAnchor.MiddleLeft);
            MapButtons=new Button[RunMaps.All.Length];mapLabels=new Text[MapButtons.Length];
            for(int i=0;i<MapButtons.Length;i++)
            {
                int at=i;float x=.025f+i*.95f/MapButtons.Length;
                var button=MenuUI.Button("",panel.transform,new Vector2(x,.435f),new Vector2(x+.95f/MapButtons.Length,.76f),()=>SelectMap(at));MapButtons[i]=button;
                var previewBox=MenuUI.Rect("Preview frame",button.transform,new Vector2(.025f,.23f),new Vector2(.975f,.97f));
                var pic=MenuUI.Rect("Current playable arena preview",previewBox,Vector2.zero,Vector2.one).gameObject.AddComponent<RawImage>();pic.texture=Preview(i);pic.raycastTarget=false;
                var fit=pic.gameObject.AddComponent<AspectRatioFitter>();fit.aspectMode=AspectRatioFitter.AspectMode.FitInParent;fit.aspectRatio=16f/9;
                mapLabels[i]=MenuUI.Text(RunMaps.All[i].Name,button.transform,new Vector2(.02f,.01f),new Vector2(.98f,.22f),22);
            }
            MenuUI.Text("CHOOSE AN ENCOUNTER",panel.transform,new Vector2(.03f,.385f),new Vector2(.97f,.435f),21,MenuUI.Gold,TextAnchor.MiddleLeft);
            EncounterButtons=new Button[RunDirector.Bosses.Length];encounterLabels=new Text[EncounterButtons.Length];
            for(int i=0;i<EncounterButtons.Length;i++)
            {int at=i;float x=.025f+i*.95f/EncounterButtons.Length;EncounterButtons[i]=MenuUI.Button(RunDirector.Bosses[i].Name,panel.transform,new Vector2(x,.305f),new Vector2(x+.95f/EncounterButtons.Length,.385f),()=>SelectEncounter(at));encounterLabels[i]=EncounterButtons[i].GetComponentInChildren<Text>();}
            Summary=MenuUI.Text("",panel.transform,new Vector2(.03f,.20f),new Vector2(.97f,.29f),27);
            Restriction=MenuUI.Text("",panel.transform,new Vector2(.04f,.105f),new Vector2(.96f,.20f),22,MenuUI.Gold);
            MenuUI.Button("Back to Heroes",panel.transform,new Vector2(.03f,.025f),new Vector2(.28f,.095f),Close);
            LaunchButton=MenuUI.Button("Launch Test Rush",panel.transform,new Vector2(.37f,.025f),new Vector2(.68f,.095f),Launch);
            MenuUI.Button("Main Menu",panel.transform,new Vector2(.75f,.025f),new Vector2(.97f,.095f),MainMenu);
            Refresh();EventSystem.current.SetSelectedGameObject(MapButtons[MapIndex].gameObject);
        }
        GameObject ownedPanel;
        public static Texture2D Preview(int i)=>Resources.Load<Texture2D>("Menu/Maps/"+RunMaps.All[i].Id);
        public void SelectMap(int i){if(Loading||i<0||i>=MapButtons.Length)return;MapIndex=SavedMap=i;Refresh();}
        public void SelectEncounter(int i){if(Loading||i<0||i>=EncounterButtons.Length)return;EncounterIndex=SavedEncounter=i;Refresh();}
        void CycleDifficulty(){if(Loading)return;selection.CycleDifficulty();difficultyLabel.text="Difficulty: "+RunDifficulty.Name(RunDifficulty.Selected);Refresh();}
        public static string Incompatibility(int map,int encounter)
        {
            if(map<0||map>=RunMaps.All.Length||encounter<0||encounter>=RunDirector.Bosses.Length)return "Choose a valid map and encounter.";
            if(!RunMaps.InBuild(map))return "Arena unavailable in this build.";
            if(!Preview(map))return "Current arena preview unavailable. Launch is disabled.";
            // Current bosses create their own shapes/props and use shared arena bounds. BossEntry.Map is an authored
            // default, not a geometry dependency. New encounters requiring scene objects must add a capability check here.
            return null;
        }
        void Refresh()
        {
            for(int i=0;i<mapLabels.Length;i++)mapLabels[i].text=(i==MapIndex?"◆ ":"")+RunMaps.All[i].Name+(Preview(i)?"":" — unavailable");
            for(int i=0;i<encounterLabels.Length;i++)encounterLabels[i].text=(i==EncounterIndex?"◆ ":"")+RunDirector.Bosses[i].Name;
            Summary.text=Hero+"  /  "+RunMaps.All[MapIndex].Name+"  /  "+RunDirector.Bosses[EncounterIndex].Name+"  /  "+RunDifficulty.Name(RunDifficulty.Selected);
            var reason=Incompatibility(MapIndex,EncounterIndex);Valid=reason==null;LaunchButton.interactable=Valid&&!Loading;
            Restriction.text=reason??"Shared flat arena supported • Retry keeps this setup • Normal run progress is unchanged";
        }
        public void Launch()
        {
            if(Loading||!Valid||!MenuLaunch.Test(Hero,MapIndex,EncounterIndex))return;
            Loading=true;LaunchButton.interactable=false;
            foreach(var b in ownedPanel.GetComponentsInChildren<Button>())b.interactable=false;
        }
        public void Close(){if(Loading)return;selection.CloseSetup();Destroy(ownedPanel);}
        void MainMenu(){if(Loading)return;UnityEngine.SceneManagement.SceneManager.LoadScene(LastBell.Menu.MainMenu.ScenePath);}
    }
}
