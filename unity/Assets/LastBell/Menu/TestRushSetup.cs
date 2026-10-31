using LastBell.Run;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace LastBell.Menu
{
    public sealed class TestRushSetup:MonoBehaviour
    {
        public static int SavedMap,SavedEncounter,SavedPhase=1;
        public static string SavedHero="Vanguard";
        public int MapIndex {get;private set;}
        public int EncounterIndex {get;private set;}
        public string Hero {get;private set;}
        public Button LaunchButton {get;private set;}
        public Button[] MapButtons {get;private set;}
        public Button[] EncounterButtons {get;private set;}
        // Phase row (boss step): the HP phase the fight starts in, 1-based. Only the selected boss's phases show
        // (RunDirector.BossEntry.Phases); picking another boss goes back to Phase 1.
        public int PhaseIndex {get;private set;}=1;
        public Button[] PhaseButtons {get;private set;}
        public static int MaxPhases{get{int n=1;foreach(var b in RunDirector.Bosses)n=Mathf.Max(n,b.Phases);return n;}}
        public static int PhasesOf(int encounter)=>encounter>=0&&encounter<RunDirector.Bosses.Length?RunDirector.Bosses[encounter].Phases:1;
        // Boss step rows (Risxhb, October 25, 2026): Bosses, then Minibosses, each a row of cards like the arena step.
        // Buttons stay indexed by RunDirector.Bosses; only their placement follows these rows. The final boss is last.
        public static readonly string[] MinibossPrefixes={"Chimebound Oracle","Dial Warden","Pendulum Sentinel"};
        public static bool IsMiniboss(int encounter)=>System.Array.Exists(MinibossPrefixes,p=>RunDirector.Bosses[encounter].Name.StartsWith(p));
        static readonly string[] BossOrder={"Aurelion","Crucivar","Sylvara","Tempo Tyrant"};
        public static int[] BossRow{get{
            var list=new System.Collections.Generic.List<int>();
            foreach(var name in BossOrder)for(int i=0;i<RunDirector.Bosses.Length;i++)if(RunDirector.Bosses[i].Name.StartsWith(name)&&!IsMiniboss(i)&&!list.Contains(i))list.Add(i);
            for(int i=0;i<RunDirector.Bosses.Length;i++)if(!IsMiniboss(i)&&!RunDirector.Bosses[i].Final&&!list.Contains(i))list.Add(i);
            for(int i=0;i<RunDirector.Bosses.Length;i++)if(!IsMiniboss(i)&&RunDirector.Bosses[i].Final&&!list.Contains(i))list.Add(i);
            return list.ToArray();}}
        public static int[] MinibossRow{get{var list=new System.Collections.Generic.List<int>();for(int i=0;i<RunDirector.Bosses.Length;i++)if(IsMiniboss(i))list.Add(i);return list.ToArray();}}
        // card name: the entry without its "(practice)" / "(area n)" note
        public static string CardName(int encounter){var n=RunDirector.Bosses[encounter].Name;int k=n.IndexOf(" (");return k>0?n.Substring(0,k):n;}
        public bool Valid {get;private set;}
        public bool Loading {get;private set;}
        public bool OwnsFocus=>ownedPanel&&EventSystem.current.currentSelectedGameObject&&EventSystem.current.currentSelectedGameObject.transform.IsChildOf(ownedPanel.transform);
        static readonly Color Muted=new Color(.93f,.88f,.76f,.62f), Faint=new Color(.93f,.88f,.76f,.16f);
        Text[] mapLabels,encounterLabels,phaseLabels;TestRushFocus[] phaseFocus;Text phaseNote;Text arenaTitle,titleEyebrow,backLabel;Image titleRule;RawImage[] encounterArt;
        GameObject arenaGroup,bossGroup;Button backButton;
        // Two steps on the main menu: arena first, then the boss (encounter) with hero, difficulty and launch.
        public bool BossStep {get;private set;}
        public Button ContinueButton {get;private set;}
        TestRushFocus[] mapFocus,encounterFocus;
        Image panelBase;MenuShowcase live;float stillAlpha=1;bool liveRequested;
        Texture2D shade,strip,cardFade;
        ConfrontationMenu selection;
        GameObject ownedPanel;
        public void Build(ConfrontationMenu owner,Transform parent,string hero)
        {
            selection=owner;Hero=hero;SavedHero=hero;MapIndex=Mathf.Clamp(SavedMap,0,RunMaps.All.Length-1);EncounterIndex=Mathf.Clamp(SavedEncounter,0,RunDirector.Bosses.Length-1);PhaseIndex=Mathf.Clamp(SavedPhase,1,PhasesOf(EncounterIndex));
            var panel=MenuUI.Panel("Test Rush setup",parent,Vector2.zero,Vector2.one,new Color(.012f,.014f,.026f,1));panel.raycastTarget=true;transform.SetParent(panel.transform,false);
            ownedPanel=panel.gameObject;panelBase=panel;
            BuildBackdrop(panel.transform);
            arenaGroup=MenuUI.Rect("Arena step",panel.transform,Vector2.zero,Vector2.one).gameObject;
            bossGroup=MenuUI.Rect("Boss step",panel.transform,Vector2.zero,Vector2.one).gameObject;
            var A=arenaGroup.transform;var B=bossGroup.transform;

            // Header: logo, mode caption and the current step.
            var logo=MenuUI.Panel("Last Bell logo",panel.transform,new Vector2(.03f,.865f),new Vector2(.19f,.975f),Color.white);
            logo.sprite=Resources.Load<Sprite>("Menu/ConfrontationLogo");logo.preserveAspect=true;logo.enabled=logo.sprite;
            // Header is the logo alone: the mode caption and hero/difficulty chips were redundant (the summary line shows
            // hero and difficulty; both are chosen on the main menu).

            // Featured selection: the arena on step one, the encounter on step two.
            titleEyebrow=MenuUI.Text("",panel.transform,new Vector2(.04f,.735f),new Vector2(.6f,.765f),14,MenuUI.Gold,TextAnchor.MiddleLeft);
            arenaTitle=MenuUI.Text("",panel.transform,new Vector2(.04f,.645f),new Vector2(.75f,.74f),64,MenuUI.Ivory,TextAnchor.MiddleLeft);
            arenaTitle.resizeTextForBestFit=true;arenaTitle.resizeTextMinSize=30;arenaTitle.resizeTextMaxSize=64;
            Shadow(arenaTitle,.8f);
            titleRule=MenuUI.Panel("Title rule",panel.transform,new Vector2(.04f,.638f),new Vector2(.04f,.638f),MenuUI.Gold);titleRule.rectTransform.sizeDelta=new Vector2(96,2);titleRule.rectTransform.pivot=new Vector2(0,.5f);

            // Arena step: arena cards.
            SectionLabel("Arena",A,.37f,RunMaps.All.Length);
            MapButtons=new Button[RunMaps.All.Length];mapLabels=new Text[MapButtons.Length];mapFocus=new TestRushFocus[MapButtons.Length];
            float cardWidth=.92f/MapButtons.Length;
            for(int i=0;i<MapButtons.Length;i++)
            {
                int at=i;float x=.04f+i*cardWidth;
                MapButtons[i]=ArenaCard(i,A,new Vector2(x,.14f),new Vector2(x+cardWidth,.365f),()=>SelectMap(at),out mapLabels[i],out mapFocus[i]);
            }

            // Boss step: two rows of cards in the arena step's style, Bosses above Minibosses. No title, summary or note.
            EncounterButtons=new Button[RunDirector.Bosses.Length];encounterLabels=new Text[EncounterButtons.Length];encounterFocus=new TestRushFocus[EncounterButtons.Length];encounterArt=new RawImage[EncounterButtons.Length];
            var bossRow=BossRow;var miniRow=MinibossRow;
            float bossCard=.92f/Mathf.Max(5,bossRow.Length);
            // rows sit 0.06 higher than before the phase row (Chzy18, October 29, 2026), so the phase tabs fit above the footer
            SectionLabel("Bosses",B,.655f,bossRow.Length);
            for(int k=0;k<bossRow.Length;k++){int at=bossRow[k];float x=.04f+k*bossCard;EncounterButtons[at]=BossCard(at,k,B,new Vector2(x,.47f),new Vector2(x+bossCard,.65f),()=>SelectEncounter(at),out encounterLabels[at],out encounterFocus[at],out encounterArt[at]);}
            SectionLabel("Minibosses",B,.425f,miniRow.Length);
            for(int k=0;k<miniRow.Length;k++){int at=miniRow[k];float x=.04f+k*bossCard;EncounterButtons[at]=BossCard(at,k,B,new Vector2(x,.24f),new Vector2(x+bossCard,.42f),()=>SelectEncounter(at),out encounterLabels[at],out encounterFocus[at],out encounterArt[at]);}
            // Phase row: one tab per phase of the selected boss, Phase 1 first
            MenuUI.Text(Spaced("Start Phase"),B,new Vector2(.035f,.195f),new Vector2(.5f,.227f),15,MenuUI.Gold,TextAnchor.MiddleLeft);
            phaseNote=MenuUI.Text("",B,new Vector2(.5f,.195f),new Vector2(.965f,.227f),UiTheme.SizeLabel,new Color(UiTheme.GoldLabel.r,UiTheme.GoldLabel.g,UiTheme.GoldLabel.b,.7f),TextAnchor.MiddleRight);
            PhaseButtons=new Button[MaxPhases];phaseLabels=new Text[PhaseButtons.Length];phaseFocus=new TestRushFocus[PhaseButtons.Length];
            for(int p=0;p<PhaseButtons.Length;p++){int n=p+1;float x=.04f+p*.15f;PhaseButtons[p]=Tab("Phase "+n,B,new Vector2(x,.135f),new Vector2(x+.15f,.19f),()=>SelectPhase(n),out phaseLabels[p],out phaseFocus[p]);}

            // Footer: one text action on the left (Main Menu / Back), the step's primary action on the right.
            MenuUI.Panel("Footer rule",panel.transform,new Vector2(.04f,.122f),new Vector2(.96f,.1235f),Faint);
            backButton=TextButton("Main Menu",panel.transform,new Vector2(.035f,.035f),new Vector2(.19f,.105f),Back);backLabel=backButton.GetComponentInChildren<Text>();
            ContinueButton=PrimaryButton("Continue",A,new Vector2(.71f,.03f),new Vector2(.96f,.108f),Continue);
            LaunchButton=PrimaryButton("Launch Test Rush",B,new Vector2(.71f,.03f),new Vector2(.96f,.108f),Launch);

            ShowStep(false);
            Refresh();
            BeginLive();
        }
        // Step changes keep the live arena backdrop; focus moves to the step's current choice.
        void ShowStep(bool boss)
        {
            BossStep=boss;arenaGroup.SetActive(!boss);bossGroup.SetActive(boss);
            if(boss)LinkBossStep();
            else Link(MapButtons,new[]{backButton,ContinueButton});
            backLabel.text=boss?"Back":"Main Menu";
            if(arenaTitle)Refresh();
            EventSystem.current.SetSelectedGameObject(boss?EncounterButtons[EncounterIndex].gameObject:MapButtons[MapIndex].gameObject);
        }
        public void Continue(){if(Loading)return;ShowStep(true);}
        // Back: boss step -> arena step -> main menu.
        public void Back(){if(Loading)return;if(BossStep)ShowStep(false);else Close();}
        public void OpenBossStep(){ShowStep(true);}
        public static Texture2D Preview(int i)=>Resources.Load<Texture2D>("Menu/Maps/"+RunMaps.All[i].Id);
        public void SelectMap(int i){if(Loading||i<0||i>=MapButtons.Length)return;MapIndex=SavedMap=i;Refresh();}
        public void SelectEncounter(int i){if(Loading||i<0||i>=EncounterButtons.Length)return;if(i!=EncounterIndex)PhaseIndex=SavedPhase=1;EncounterIndex=SavedEncounter=i;Refresh();if(BossStep)LinkBossStep();}
        public void SelectPhase(int n){if(Loading||n<1||n>PhasesOf(EncounterIndex))return;PhaseIndex=SavedPhase=n;Refresh();}
        // the phase row holds only the selected boss's phases, so its links follow the selection
        void LinkBossStep()
        {
            var phases=new Button[PhasesOf(EncounterIndex)];System.Array.Copy(PhaseButtons,phases,phases.Length);
            Link(System.Array.ConvertAll(BossRow,i=>EncounterButtons[i]),System.Array.ConvertAll(MinibossRow,i=>EncounterButtons[i]),phases,new[]{backButton,LaunchButton});
        }
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
            for(int i=0;i<mapLabels.Length;i++){mapLabels[i].text=RunMaps.All[i].Name+(Preview(i)?"":" (unavailable)");mapFocus[i].Chosen=i==MapIndex;}
            for(int i=0;i<encounterLabels.Length;i++){encounterLabels[i].text=CardName(i);encounterFocus[i].Chosen=i==EncounterIndex;if(encounterArt[i])encounterArt[i].texture=EncounterPreview(i,MapIndex);}
            int phaseCount=PhasesOf(EncounterIndex);PhaseIndex=Mathf.Clamp(PhaseIndex,1,phaseCount);
            for(int p=0;p<PhaseButtons.Length;p++){PhaseButtons[p].gameObject.SetActive(p<phaseCount);phaseFocus[p].Chosen=p+1==PhaseIndex;}
            phaseNote.text=Spaced(phaseCount==1?"Phase 1 only":phaseCount.ToString("00")+" phases");
            // the arena step names the selected arena; the boss step shows only its card rows
            arenaTitle.text=RunMaps.All[MapIndex].Name.ToUpperInvariant();
            titleEyebrow.text=Spaced("Selected Arena");
            arenaTitle.gameObject.SetActive(!BossStep);titleEyebrow.gameObject.SetActive(!BossStep);titleRule.gameObject.SetActive(!BossStep);
            var reason=Incompatibility(MapIndex,EncounterIndex);Valid=reason==null;LaunchButton.interactable=Valid&&!Loading;ContinueButton.interactable=!Loading&&RunMaps.InBuild(MapIndex)&&Preview(MapIndex);
        }
        void Update()
        {
            if(!panelBase)return;
            UpdateLive();
            // No still image: a dark panel until the live showcase starts, then only the orbiting arena shows.
            panelBase.color=new Color(.012f,.014f,.026f,stillAlpha);
        }
        public void Launch()
        {
            if(Loading||!Valid||!MenuLaunch.Test(Hero,MapIndex,EncounterIndex,PhaseIndex))return;
            Loading=true;LaunchButton.interactable=false;
            foreach(var b in ownedPanel.GetComponentsInChildren<Button>())b.interactable=false;
        }
        public void Close(){if(Loading)return;EndLive();selection.CloseRush();Destroy(ownedPanel);}
        // Live backdrop: the main menu's arena showcase, loaded additively and cut to the selected arena.
        // Arena cuts use the showcase's own fade; arenas that are not showcase stages keep the dark panel.
        public bool LiveBackdrop=>live&&live.Manual&&live.Started&&stillAlpha<=0;
        public static int ShowcaseStage(int map)=>map<0||map>=RunMaps.All.Length?-1:System.Array.IndexOf(RunMaps.Showcased,RunMaps.All[map]);
        void BeginLive()
        {
            if(!Application.CanStreamedLevelBeLoaded(MainMenu.ScenePath))return;
            liveRequested=true;selection.SetStageHidden(true);
            if(SceneManager.GetSceneByPath(MainMenu.ScenePath).IsValid())return; // still loaded or loading from a previous visit
            SceneManager.LoadSceneAsync(MainMenu.ScenePath,LoadSceneMode.Additive);MainMenu.BackdropScene=SceneManager.GetSceneByPath(MainMenu.ScenePath);
        }
        void UpdateLive()
        {
            if(liveRequested&&!live)
            {
                var scene=SceneManager.GetSceneByPath(MainMenu.ScenePath);
                if(scene.isLoaded)foreach(var node in scene.GetRootGameObjects())
                {
                    foreach(var ear in node.GetComponentsInChildren<AudioListener>(true))ear.enabled=false; // hero scene keeps the only listener
                    if(!live)live=node.GetComponentInChildren<MenuShowcase>();
                }
            }
            int stage=ShowcaseStage(MapIndex);
            if(live&&stage>=0)live.Show(stage);
            bool showLive=live&&live.Started&&stage>=0;
            stillAlpha=Mathf.MoveTowards(stillAlpha,showLive?0:1,Time.unscaledDeltaTime*3);
        }
        void EndLive()
        {
            if(!liveRequested)return;liveRequested=false;live=null;
            var own=selection.gameObject.scene;if(own.isLoaded)SceneManager.SetActiveScene(own);
            var scene=SceneManager.GetSceneByPath(MainMenu.ScenePath);
            if(scene.isLoaded)SceneManager.UnloadSceneAsync(scene); // a copy still loading unloads itself when it finds no open setup
            selection.SetStageHidden(false);
        }
        void ToMainMenu(){if(Loading)return;EndLive();SceneManager.LoadScene(MenuAppearance.ScenePath);}
        void OnDestroy(){if(shade)Destroy(shade);if(strip)Destroy(strip);if(cardFade)Destroy(cardFade);}

        // ---- Presentation helpers ----
        void BuildBackdrop(Transform panel)
        {
            // One vertical shade (dark floor for cards, dark ceiling for header) and one left shade for the title column.
            shade=new Texture2D(1,128,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Clamp};
            for(int y=0;y<128;y++){float t=y/127f;shade.SetPixel(0,y,new Color(.008f,.01f,.02f,Mathf.Max(1-Mathf.SmoothStep(0,.62f,t),.88f*Mathf.SmoothStep(.72f,1,t))*.97f));}
            shade.Apply();
            var vertical=MenuUI.Rect("Vertical shade",panel,Vector2.zero,Vector2.one).gameObject.AddComponent<RawImage>();vertical.texture=shade;vertical.raycastTarget=false;
            strip=new Texture2D(128,1,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Clamp};
            for(int x=0;x<128;x++)strip.SetPixel(x,0,new Color(.008f,.01f,.02f,(1-Mathf.SmoothStep(0,1,x/127f))));
            strip.Apply();
            cardFade=new Texture2D(1,64,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            for(int y=0;y<64;y++){float t=y/63f;cardFade.SetPixel(0,y,new Color(.008f,.01f,.02f,.96f*Mathf.Pow(1-t,1.15f)));}
            cardFade.Apply();
            var left=MenuUI.Rect("Left shade",panel,Vector2.zero,new Vector2(.75f,1)).gameObject.AddComponent<RawImage>();left.texture=strip;left.color=new Color(1,1,1,.8f);left.raycastTarget=false;
        }
        // CTA tracking: single space between letters, two between words (Spaced() uses three).
        static string Tracked(string s)=>string.Join("  ",System.Array.ConvertAll(s.ToUpperInvariant().Split(' '),w=>string.Join(" ",w.ToCharArray())));
        static readonly Color FocusGold=new Color(UiTheme.GoldDim.r,UiTheme.GoldDim.g,UiTheme.GoldDim.b,.6f); // focus edge: GoldDim 0.6, never Ivory
        static string Spaced(string s)=>string.Join("   ",System.Array.ConvertAll(s.ToUpperInvariant().Split(' '),w=>string.Join(" ",w.ToCharArray())));
        // MenuUI.Text already adds a shadow; reuse it (stronger alpha) instead of stacking a second one.
        static void Shadow(Text t,float a)=>MenuUI.Shadow(t,a);
        void SectionLabel(string label,Transform parent,float y,int count)
        {
            MenuUI.Text(Spaced(label),parent,new Vector2(.035f,y),new Vector2(.5f,y+.032f),15,MenuUI.Gold,TextAnchor.MiddleLeft);
            MenuUI.Text(Spaced(count.ToString("00")+" AVAILABLE"),parent,new Vector2(.5f,y),new Vector2(.965f,y+.032f),UiTheme.SizeLabel,new Color(UiTheme.GoldLabel.r,UiTheme.GoldLabel.g,UiTheme.GoldLabel.b,.7f),TextAnchor.MiddleRight);
        }
        static (Button button,Image frame,TestRushFocus focus) Base(string name,Transform parent,Vector2 min,Vector2 max,UnityEngine.Events.UnityAction action,Vector2 inset)
        {
            var r=MenuUI.Rect(name,parent,min,max,inset);var im=r.gameObject.AddComponent<Image>();im.color=new Color(0,0,0,.35f);
            var b=r.gameObject.AddComponent<Button>();b.targetGraphic=im;b.transition=Selectable.Transition.None;b.onClick.AddListener(action);
            var f=r.gameObject.AddComponent<TestRushFocus>();f.Button=b;return (b,im,f);
        }
        Button ArenaCard(int i,Transform parent,Vector2 min,Vector2 max,UnityEngine.Events.UnityAction action,out Text label,out TestRushFocus focus)
        {
            var (b,frame,f)=Base(RunMaps.All[i].Name,parent,min,max,action,new Vector2(8,0));focus=f;f.Frame=frame;f.Grow=1.02f;f.ChosenEdge=UiTheme.GoldBright;f.FocusEdge=FocusGold;
            var inner=MenuUI.Rect("Card art",b.transform,Vector2.zero,Vector2.one,new Vector2(2,2));inner.gameObject.AddComponent<RectMask2D>();
            MenuUI.Panel("Card backing",inner,Vector2.zero,Vector2.one,new Color(.02f,.025f,.04f,1));
            var art=MenuUI.Rect("Current playable arena preview",inner,Vector2.zero,Vector2.one).gameObject.AddComponent<RawImage>();art.texture=Preview(i);art.raycastTarget=false;
            var fit=art.gameObject.AddComponent<AspectRatioFitter>();fit.aspectMode=AspectRatioFitter.AspectMode.EnvelopeParent;fit.aspectRatio=16f/9;f.Art=art;
            var fade=MenuUI.Rect("Name shade",inner,Vector2.zero,new Vector2(1,.62f)).gameObject.AddComponent<RawImage>();fade.texture=cardFade;fade.raycastTarget=false;
            MenuUI.Text((i+1).ToString("00"),inner,new Vector2(.02f,.80f),new Vector2(.5f,.98f),14,MenuUI.Gold,TextAnchor.MiddleLeft);
            label=MenuUI.Text("",inner,new Vector2(.02f,.03f),new Vector2(.98f,.24f),21,MenuUI.Ivory,TextAnchor.MiddleLeft);label.resizeTextForBestFit=true;label.resizeTextMinSize=12;label.resizeTextMaxSize=21;f.Label=label;Shadow(label,.9f);
            var accent=MenuUI.Panel("Chosen accent",inner,Vector2.zero,new Vector2(1,0),MenuUI.Gold);accent.rectTransform.sizeDelta=new Vector2(0,4);accent.rectTransform.pivot=new Vector2(.5f,0);f.Accent=accent;
            return b;
        }
        // a boss's card art: its own arena when it has one, otherwise the selected arena
        public static Texture2D EncounterPreview(int encounter,int map)
        {
            var own=RunDirector.Bosses[encounter].Map;int m=own!=null?RunMaps.IndexOf(own):map;
            return m>=0&&m<RunMaps.All.Length?Preview(m):null;
        }
        // Same card frame as the arena step (number, gold accent, focus), for one encounter. Names only, no arena art
        // (Risxhb, October 26, 2026): the art comes back once each boss has its own portrait.
        Button BossCard(int encounter,int slot,Transform parent,Vector2 min,Vector2 max,UnityEngine.Events.UnityAction action,out Text label,out TestRushFocus focus,out RawImage art)
        {
            var (b,frame,f)=Base(CardName(encounter),parent,min,max,action,new Vector2(8,0));focus=f;f.Frame=frame;f.Grow=1.02f;f.ChosenEdge=UiTheme.GoldBright;f.FocusEdge=FocusGold;
            var inner=MenuUI.Rect("Card face",b.transform,Vector2.zero,Vector2.one,new Vector2(2,2));inner.gameObject.AddComponent<RectMask2D>();
            MenuUI.Panel("Card backing",inner,Vector2.zero,Vector2.one,new Color(.02f,.025f,.04f,1));
            art=null;
            MenuUI.Text((slot+1).ToString("00"),inner,new Vector2(.04f,.80f),new Vector2(.5f,.98f),14,MenuUI.Gold,TextAnchor.MiddleLeft);
            label=MenuUI.Text("",inner,new Vector2(.06f,.2f),new Vector2(.94f,.8f),26,MenuUI.Ivory,TextAnchor.MiddleCenter);label.resizeTextForBestFit=true;label.resizeTextMinSize=14;label.resizeTextMaxSize=26;f.Label=label;Shadow(label,.9f);
            var accent=MenuUI.Panel("Chosen accent",inner,Vector2.zero,new Vector2(1,0),MenuUI.Gold);accent.rectTransform.sizeDelta=new Vector2(0,4);accent.rectTransform.pivot=new Vector2(.5f,0);f.Accent=accent;
            return b;
        }
        Button Tab(string name,Transform parent,Vector2 min,Vector2 max,UnityEngine.Events.UnityAction action,out Text label,out TestRushFocus focus)
        {
            var (b,frame,f)=Base(name,parent,min,max,action,new Vector2(8,0));focus=f;frame.color=new Color(0,0,0,.42f);
            var line=MenuUI.Panel("Tab underline",b.transform,Vector2.zero,new Vector2(1,0),Faint);line.rectTransform.sizeDelta=new Vector2(0,2);line.rectTransform.pivot=new Vector2(.5f,0);f.Frame=line;f.FocusEdge=FocusGold;
            var mark=MenuUI.Rect("Chosen diamond",b.transform,new Vector2(0,.5f),new Vector2(0,.5f)).gameObject.AddComponent<FocusDiamond>();
            mark.rectTransform.sizeDelta=new Vector2(10,16);mark.rectTransform.anchoredPosition=new Vector2(18,0);mark.color=MenuUI.Gold;mark.raycastTarget=false;f.Accent=mark;
            label=MenuUI.Text(name,b.transform,Vector2.zero,Vector2.one,21);label.rectTransform.offsetMin=new Vector2(30,3);label.rectTransform.offsetMax=new Vector2(-12,-3);
            label.resizeTextForBestFit=true;label.resizeTextMinSize=12;label.resizeTextMaxSize=21;f.Label=label;
            return b;
        }
        Button Chip(string caption,string value,string hint,Transform parent,Vector2 min,Vector2 max,UnityEngine.Events.UnityAction action,out Text valueLabel)
        {
            var (b,frame,f)=Base(caption,parent,min,max,action,new Vector2(6,0));frame.color=new Color(0,0,0,.45f);
            var line=MenuUI.Panel("Chip edge",b.transform,Vector2.zero,new Vector2(0,1),Faint);line.rectTransform.sizeDelta=new Vector2(3,0);line.rectTransform.pivot=new Vector2(0,.5f);f.Frame=line;
            MenuUI.Text(Spaced(caption),b.transform,new Vector2(.04f,.55f),new Vector2(.96f,.95f),12,MenuUI.Gold,TextAnchor.MiddleLeft);
            valueLabel=MenuUI.Text(value,b.transform,new Vector2(.04f,.05f),new Vector2(.70f,.6f),22,MenuUI.Ivory,TextAnchor.MiddleLeft);f.Label=valueLabel;
            MenuUI.Text(hint+"  ›",b.transform,new Vector2(.6f,.05f),new Vector2(.96f,.6f),13,UiTheme.GoldLabel,TextAnchor.MiddleRight).horizontalOverflow=HorizontalWrapMode.Overflow;
            return b;
        }
        // Footer actions use the main menu's entries: transparent hit area, Ivory label (first Text child) and the gold
        // highlight strip on focus. The step's primary action (Continue / Launch) is larger, like Start Run.
        Button NavButton(string name,Transform parent,Vector2 min,Vector2 max,UnityEngine.Events.UnityAction action,int size)
        {
            var r=MenuUI.Rect(name,parent,min,max);var im=r.gameObject.AddComponent<Image>();im.color=new Color(0,0,0,0);im.raycastTarget=true;
            var b=r.gameObject.AddComponent<Button>();b.targetGraphic=im;b.transition=Selectable.Transition.None;b.onClick.AddListener(action);
            var label=MenuUI.Text(name,r,Vector2.zero,Vector2.one,size);label.resizeTextForBestFit=true;label.resizeTextMinSize=13;label.resizeTextMaxSize=size;
            var f=r.gameObject.AddComponent<ConfrontationNavFocus>();f.Label=label;f.Strip=selection.Strip(r);
            return b;
        }
        Button TextButton(string name,Transform parent,Vector2 min,Vector2 max,UnityEngine.Events.UnityAction action)=>NavButton(name,parent,min,max,action,24);
        Button PrimaryButton(string name,Transform parent,Vector2 min,Vector2 max,UnityEngine.Events.UnityAction action)=>NavButton(name,parent,min,max,action,28);
        // Explicit grid navigation keeps keyboard/controller focus inside this panel and predictable between rows.
        static void Link(params Button[][] rows)
        {
            for(int r=0;r<rows.Length;r++)for(int i=0;i<rows[r].Length;i++)
            {
                var row=rows[r];float frac=row.Length>1?i/(float)(row.Length-1):.5f;
                Button Near(int k)=>k<0||k>=rows.Length?null:rows[k][Mathf.RoundToInt(frac*(rows[k].Length-1))];
                row[i].navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnLeft=i>0?row[i-1]:null,selectOnRight=i<row.Length-1?row[i+1]:null,selectOnUp=Near(r-1),selectOnDown=Near(r+1)};
            }
        }
    }
    // Visual state for Test Rush controls: chosen (gold), focused (bright, slightly raised) and resting (muted).
    public sealed class TestRushFocus:MonoBehaviour,IPointerEnterHandler,IPointerExitHandler
    {
        public Button Button;public Graphic Frame,Label,Art,Accent;public bool Chosen,Primary,AccentOnFocusOnly;public float Grow=1;
        public Color ChosenEdge=MenuUI.Gold,FocusEdge=new Color(UiTheme.GoldDim.r,UiTheme.GoldDim.g,UiTheme.GoldDim.b,.6f); // cards: GoldBright chosen / GoldDim hover
        static readonly Color Rest=new Color(.93f,.88f,.76f,.62f), Edge=new Color(.93f,.88f,.76f,.16f);
        bool hovered;
        public bool Focused=>hovered||EventSystem.current&&EventSystem.current.currentSelectedGameObject==gameObject;
        public void OnPointerEnter(PointerEventData data){hovered=true;}
        public void OnPointerExit(PointerEventData data){hovered=false;}
        void OnDisable(){hovered=false;}
        void LateUpdate()
        {
            bool on=Focused&&(!Button||Button.interactable);bool live=!Button||Button.interactable;
            if(Primary)
            {
                if(Frame)Frame.color=!live?new Color(.22f,.2f,.17f,.9f):on?new Color(1f,.86f,.56f):MenuUI.Gold;
                if(Label)Label.color=live?new Color(.07f,.05f,.02f):new Color(.6f,.57f,.5f);
            }
            else
            {
                if(Frame)Frame.color=Chosen?ChosenEdge:on?FocusEdge:Edge;
                if(Art)Art.color=Chosen?Color.white:on?new Color(.85f,.85f,.88f):new Color(.42f,.42f,.48f);
                if(Label)Label.color=Chosen?MenuUI.Gold:on?MenuUI.Ivory:Rest;
                if(Accent)Accent.enabled=AccentOnFocusOnly?on:Chosen;
            }
            float target=on?Grow:1;var s=transform.localScale.x;s=Mathf.MoveTowards(s,target,Time.unscaledDeltaTime*.6f);transform.localScale=new Vector3(s,s,1);
        }
    }
}
