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
        public static string SavedHero="Tollbreaker";
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
        // Allies (bots) row (Chzy18, 24 Oct: every test build lets you pick 0-7 bots): one cycling text row for RunParty.Selected
        public Button AlliesButton {get;private set;}
        Text alliesLabel;RectTransform alliesPrev,alliesNext,content;
        readonly System.Collections.Generic.List<Text> miniLabels=new System.Collections.Generic.List<Text>();int miniSizedFor=-1;
        public static int MaxPhases{get{int n=1;foreach(var b in RunDirector.Bosses)n=Mathf.Max(n,b.Phases);return n;}}
        public static int PhasesOf(int encounter)=>encounter>=0&&encounter<RunDirector.Bosses.Length?RunDirector.Bosses[encounter].Phases:1;
        // Boss step rows (Risxhb, October 25, 2026): Bosses, then Minibosses, each a row of cards like the arena step.
        // Buttons stay indexed by RunDirector.Bosses; only their placement follows these rows. The final boss is last.
        public static readonly string[] MinibossPrefixes={"Chimebound Oracle","Dial Warden","Pendulum Sentinel","Escapement Twins","Rivet Cantor","Crucible Skater","Briar Metronome","Silt Carillon","Vesper Seedkeeper"};
        // Browser demo cut (Risx, 11 Nov 2026): Boss Rush only, on the arenas WebDemoBuild ships; minibosses and Start Run are
        // greyed out because their models are left out of the download.
#if UNITY_WEBGL && !UNITY_EDITOR
        public static readonly bool DemoCut=true;
#else
        public static readonly bool DemoCut=false;
#endif
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
        static readonly Color Faint=UiTheme.RuleFaint;
        Text[] mapLabels,encounterLabels,phaseLabels;NavFocus[] phaseFocus;Text phaseNote;Text backLabel;Header title;RawImage[] encounterArt;
        GameObject arenaGroup,bossGroup;Button backButton;
        // Two steps on the main menu: arena first, then the boss (encounter) with hero, difficulty and launch.
        public bool BossStep {get;private set;}
        int MiniPerRow=6;
        public Button ContinueButton {get;private set;}
        NavFocus[] mapFocus,encounterFocus;
        Image panelBase;MenuShowcase live;float stillAlpha=1;bool liveRequested;
        Texture2D shade,strip,cardFade;
        ConfrontationMenu selection;
        GameObject ownedPanel;
        public void Build(ConfrontationMenu owner,Transform parent,string hero)
        {
            selection=owner;Hero=hero;SavedHero=hero;MapIndex=Mathf.Clamp(SavedMap,0,RunMaps.All.Length-1);EncounterIndex=Mathf.Clamp(SavedEncounter,0,RunDirector.Bosses.Length-1);
            if(DemoCut){if(IsMiniboss(EncounterIndex))EncounterIndex=BossRow[0];if(!RunMaps.InBuild(MapIndex))for(int m=0;m<RunMaps.All.Length;m++)if(RunMaps.InBuild(m)){MapIndex=m;break;}}
            PhaseIndex=Mathf.Clamp(SavedPhase,1,PhasesOf(EncounterIndex));
            var panel=MenuUI.Panel("Test Rush setup",parent,Vector2.zero,Vector2.one,UiTheme.Backdrop);panel.raycastTarget=true;transform.SetParent(panel.transform,false);
            ownedPanel=panel.gameObject;panelBase=panel;
            BuildBackdrop(panel.transform);
            // Everything but the backdrop sits in one content rect; ultrawide narrows it to a centred 16:9 column (Update).
            content=MenuUI.Rect("Content",panel.transform,Vector2.zero,Vector2.one);var C=content.transform;
            arenaGroup=MenuUI.Rect("Arena step",C,Vector2.zero,Vector2.one).gameObject;
            bossGroup=MenuUI.Rect("Boss step",C,Vector2.zero,Vector2.one).gameObject;
            var A=arenaGroup.transform;var B=bossGroup.transform;

            // Header: logo, mode caption and the current step.
            var logo=MenuUI.Panel("Last Bell logo",C,new Vector2(.03f,.865f),new Vector2(.19f,.975f),Color.white);
            logo.sprite=Resources.Load<Sprite>("Menu/ConfrontationLogo");logo.preserveAspect=true;logo.enabled=logo.sprite;
            // Header is the logo alone: the mode caption and hero/difficulty chips were redundant (the summary line shows
            // hero and difficulty; both are chosen on the main menu).

            // Featured selection: the arena on step one, the encounter on step two.
            title=MenuUI.Header(C,"Selected Arena","",new Vector2(.04f,.735f),new Vector2(.6f,.765f),new Vector2(.04f,.645f),new Vector2(.75f,.74f),new Vector2(.04f,.638f),64,30);

            // Arena step: arena cards.
            SectionLabel("Arena",A,.37f,RunMaps.All.Length);
            MapButtons=new Button[RunMaps.All.Length];mapLabels=new Text[MapButtons.Length];mapFocus=new NavFocus[MapButtons.Length];
            float cardWidth=.92f/MapButtons.Length;
            for(int i=0;i<MapButtons.Length;i++)
            {
                int at=i;float x=.04f+i*cardWidth;
                MapButtons[i]=ArenaCard(i,A,new Vector2(x,.14f),new Vector2(x+cardWidth,.365f),()=>SelectMap(at),out mapLabels[i],out mapFocus[i]);
            }

            // Boss step: two rows of cards in the arena step's style, Bosses above Minibosses. No title, summary or note.
            EncounterButtons=new Button[RunDirector.Bosses.Length];encounterLabels=new Text[EncounterButtons.Length];encounterFocus=new NavFocus[EncounterButtons.Length];encounterArt=new RawImage[EncounterButtons.Length];
            var bossRow=BossRow;var miniRow=MinibossRow;
            float bossCard=.92f/Mathf.Max(5,bossRow.Length);
            // rows sit 0.06 higher than before the phase row (Chzy18, October 29, 2026), so the phase tabs fit above the footer
            SectionLabel("Bosses",B,.655f,bossRow.Length);
            for(int k=0;k<bossRow.Length;k++){int at=bossRow[k];float x=.04f+k*bossCard;EncounterButtons[at]=BossCard(at,k,false,B,new Vector2(x,.47f),new Vector2(x+bossCard,.65f),()=>SelectEncounter(at),out encounterLabels[at],out encounterFocus[at],out encounterArt[at]);}
            // Fighter cards (Risx, 4 Nov 2026): minibosses are half a boss card's height at the same width, wrapping into
            // rows of as many cards as the boss row holds.
            SectionLabel("Minibosses",B,.425f,miniRow.Length);
            MiniPerRow=Mathf.Max(5,bossRow.Length);
            for(int k=0;k<miniRow.Length;k++){int at=miniRow[k];int col=k%MiniPerRow,row=k/MiniPerRow;float x=.04f+col*bossCard,top=.42f-row*.096f;EncounterButtons[at]=BossCard(at,k,true,B,new Vector2(x,top-.084f),new Vector2(x+bossCard,top),()=>SelectEncounter(at),out encounterLabels[at],out encounterFocus[at],out encounterArt[at]);}
            if(DemoCut)
            {
                foreach(int at in miniRow){EncounterButtons[at].interactable=false;EncounterButtons[at].gameObject.AddComponent<CanvasGroup>().alpha=.35f;}
                for(int i=0;i<MapButtons.Length;i++)if(!RunMaps.InBuild(i)){MapButtons[i].interactable=false;MapButtons[i].gameObject.AddComponent<CanvasGroup>().alpha=.35f;}
            }
            // Phase row: one tab per phase of the selected boss, Phase 1 first
            MenuUI.Text(UiTheme.Spaced("Start Phase"),B,new Vector2(.035f,.195f),new Vector2(.5f,.227f),15,MenuUI.Gold,TextAnchor.MiddleLeft);
            phaseNote=MenuUI.Text("",B,new Vector2(.5f,.195f),new Vector2(.965f,.227f),UiTheme.SizeLabel,new Color(UiTheme.GoldLabel.r,UiTheme.GoldLabel.g,UiTheme.GoldLabel.b,.7f),TextAnchor.MiddleRight);
            PhaseButtons=new Button[MaxPhases];phaseLabels=new Text[PhaseButtons.Length];phaseFocus=new NavFocus[PhaseButtons.Length];
            float phaseW=.92f/Mathf.Max(5,PhaseButtons.Length);for(int p=0;p<PhaseButtons.Length;p++){int n=p+1;float x=.04f+p*phaseW;PhaseButtons[p]=Tab("Phase "+n,B,new Vector2(x,.135f),new Vector2(x+phaseW,.19f),()=>SelectPhase(n),out phaseLabels[p],out phaseFocus[p]);}

            // Footer: one text action on the left (Main Menu / Back), the step's primary action on the right.
            MenuUI.Panel("Footer rule",C,new Vector2(.04f,.122f),new Vector2(.96f,.1235f),Faint);
            backButton=TextButton("Main Menu",C,new Vector2(.035f,.035f),new Vector2(.19f,.105f),Back);backLabel=backButton.GetComponentInChildren<Text>();
            ContinueButton=PrimaryButton("Continue",A,new Vector2(.71f,.03f),new Vector2(.96f,.108f),Continue);
            LaunchButton=PrimaryButton("Launch Test Rush",B,new Vector2(.71f,.03f),new Vector2(.96f,.108f),Launch);
            AlliesButton=CycleRow(B,new Vector2(.25f,.035f),new Vector2(.55f,.105f),CycleAllies,out alliesLabel);

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
            if(title!=null)Refresh();
            EventSystem.current.SetSelectedGameObject(boss?EncounterButtons[EncounterIndex].gameObject:MapButtons[MapIndex].gameObject);
        }
        public void Continue(){if(Loading)return;ShowStep(true);}
        // Back: boss step -> arena step -> main menu.
        public void Back(){if(Loading)return;if(BossStep)ShowStep(false);else Close();}
        public void OpenBossStep(){ShowStep(true);}
        public static Texture2D Preview(int i)=>Resources.Load<Texture2D>("Menu/Maps/"+RunMaps.All[i].Id);
        public void SelectMap(int i){if(Loading||i<0||i>=MapButtons.Length||(DemoCut&&!RunMaps.InBuild(i)))return;MapIndex=SavedMap=i;Refresh();}
        public void SelectEncounter(int i){if(Loading||i<0||i>=EncounterButtons.Length||(DemoCut&&IsMiniboss(i)))return;if(i!=EncounterIndex)PhaseIndex=SavedPhase=1;EncounterIndex=SavedEncounter=i;Refresh();if(BossStep)LinkBossStep();}
        public void SelectPhase(int n){if(Loading||n<1||n>PhasesOf(EncounterIndex))return;PhaseIndex=SavedPhase=n;Refresh();}
        public void CycleAllies(){if(Loading)return;RunParty.Selected=(RunParty.Selected+1)%(RunParty.MaxAllies+1);Refresh();}
        // the phase row holds only the selected boss's phases, so its links follow the selection
        void LinkBossStep()
        {
            var phases=new Button[PhasesOf(EncounterIndex)];System.Array.Copy(PhaseButtons,phases,phases.Length);
            var rows=new System.Collections.Generic.List<Button[]>{System.Array.ConvertAll(BossRow,i=>EncounterButtons[i])};
            var minis=MinibossRow;
            for(int s=0;s<minis.Length;s+=MiniPerRow){var row=new Button[Mathf.Min(MiniPerRow,minis.Length-s)];for(int k=0;k<row.Length;k++)row[k]=EncounterButtons[minis[s+k]];rows.Add(row);}
            rows.Add(phases);rows.Add(new[]{backButton,AlliesButton,LaunchButton});
            Link(rows.ToArray());
        }
        public static string Incompatibility(int map,int encounter)
        {
            if(map<0||map>=RunMaps.All.Length||encounter<0||encounter>=RunDirector.Bosses.Length)return "Choose a valid map and encounter.";
            if(!RunMaps.InBuild(map))return "Arena unavailable in this build.";
            if(!Preview(map))return "Current arena preview unavailable. Launch is disabled.";
            if(DemoCut&&IsMiniboss(encounter))return "Minibosses are not in the browser demo.";
            if(encounter==Campaign.BonusIndex&&!Campaign.OverclockedUnlocked)return Campaign.LockedReason;   // campaign unlock
            // Current bosses create their own shapes/props and use shared arena bounds. BossEntry.Map is an authored
            // default, not a geometry dependency. New encounters requiring scene objects must add a capability check here.
            return null;
        }
        void Refresh()
        {
            for(int i=0;i<mapLabels.Length;i++){mapLabels[i].text=RunMaps.All[i].Name+(Preview(i)&&RunMaps.InBuild(i)?"":" (unavailable)");mapFocus[i].Chosen=i==MapIndex;}
            for(int i=0;i<encounterLabels.Length;i++){encounterLabels[i].text=CardName(i)+(i==Campaign.BonusIndex&&!Campaign.OverclockedUnlocked?" (locked)":"");encounterFocus[i].Chosen=i==EncounterIndex;}
            int phaseCount=PhasesOf(EncounterIndex);PhaseIndex=Mathf.Clamp(PhaseIndex,1,phaseCount);
            for(int p=0;p<PhaseButtons.Length;p++){PhaseButtons[p].gameObject.SetActive(p<phaseCount);phaseFocus[p].Chosen=p+1==PhaseIndex;}
            phaseNote.text=UiTheme.Spaced(phaseCount==1?"Phase 1 only":phaseCount.ToString("00")+" phases");
            alliesLabel.text=AlliesText(RunParty.Selected);
            // the arena step names the selected arena; the boss step shows only its card rows
            title.Title.text=RunMaps.All[MapIndex].Name.ToUpperInvariant();
            title.SetActive(!BossStep);
            var reason=Incompatibility(MapIndex,EncounterIndex);Valid=reason==null;LaunchButton.interactable=Valid&&!Loading;ContinueButton.interactable=!Loading&&RunMaps.InBuild(MapIndex)&&Preview(MapIndex);
        }
        // Miniboss names share one size (18) and stay on one line; the longest, "Chimebound Oracle", fits a 4:3 card.
        void UniformMiniNames()
        {
            int key=Screen.width*10000+Screen.height;if(key==miniSizedFor||miniLabels.Count==0)return;miniSizedFor=key;
            const int size=18;   // 16 read ~10 px at 1024x768
            foreach(var l in miniLabels){l.resizeTextForBestFit=false;l.fontSize=size;l.horizontalOverflow=HorizontalWrapMode.Overflow;}
        }
        public static string AlliesText(int bots)=>"Bot allies: "+(bots==0?"none":bots.ToString());
        void Update()
        {
            if(!panelBase)return;
            UpdateLive();
            // Ultrawide (wider than ~1.95:1): keep the cards and footer in a centred 16:9 column; the backdrop stays full width.
            var area=((RectTransform)panelBase.transform).rect;float aspect=area.height>0?area.width/area.height:1.78f,w=aspect>1.95f?1.7778f/aspect:1;
            content.anchorMin=new Vector2((1-w)*.5f,0);content.anchorMax=new Vector2((1+w)*.5f,1);
            UniformMiniNames();
            if(alliesLabel&&alliesPrev){float x=MenuUI.HalfWidth(alliesLabel)+18;alliesPrev.anchoredPosition=new Vector2(-x,0);alliesNext.anchoredPosition=new Vector2(x,0);}
            // No still image: a dark panel until the live showcase starts, then only the orbiting arena shows.
            panelBase.color=new Color(UiTheme.Backdrop.r,UiTheme.Backdrop.g,UiTheme.Backdrop.b,stillAlpha);
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
        void OnDestroy(){if(shade)Destroy(shade);if(strip)Destroy(strip);if(cardFade)Destroy(cardFade);}

        // ---- Presentation helpers ----
        static readonly Color Ink=UiTheme.GradientInk;
        void BuildBackdrop(Transform panel)
        {
            // One vertical shade (dark floor for cards, dark ceiling for header) and one left shade for the title column.
            shade=new Texture2D(1,128,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Clamp};
            for(int y=0;y<128;y++){float t=y/127f;shade.SetPixel(0,y,new Color(Ink.r,Ink.g,Ink.b,Mathf.Max(1-Mathf.SmoothStep(0,.62f,t),.88f*Mathf.SmoothStep(.72f,1,t))*.97f));}
            shade.Apply();
            var vertical=MenuUI.Rect("Vertical shade",panel,Vector2.zero,Vector2.one).gameObject.AddComponent<RawImage>();vertical.texture=shade;vertical.raycastTarget=false;
            strip=new Texture2D(128,1,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Clamp};
            for(int x=0;x<128;x++)strip.SetPixel(x,0,new Color(Ink.r,Ink.g,Ink.b,(1-Mathf.SmoothStep(0,1,x/127f))));
            strip.Apply();
            cardFade=new Texture2D(1,64,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            for(int y=0;y<64;y++){float t=y/63f;cardFade.SetPixel(0,y,new Color(Ink.r,Ink.g,Ink.b,.96f*Mathf.Pow(1-t,1.15f)));}
            cardFade.Apply();
            var left=MenuUI.Rect("Left shade",panel,Vector2.zero,new Vector2(.75f,1)).gameObject.AddComponent<RawImage>();left.texture=strip;left.color=new Color(1,1,1,.8f);left.raycastTarget=false;
        }
        // MenuUI.Text already adds a shadow; reuse it (stronger alpha) instead of stacking a second one.
        static void Shadow(Text t,float a)=>MenuUI.Shadow(t,a);
        void SectionLabel(string label,Transform parent,float y,int count)
        {
            MenuUI.Text(UiTheme.Spaced(label),parent,new Vector2(.035f,y),new Vector2(.5f,y+.032f),15,MenuUI.Gold,TextAnchor.MiddleLeft);
            MenuUI.Text(UiTheme.Spaced(count.ToString("00")+" AVAILABLE"),parent,new Vector2(.5f,y),new Vector2(.965f,y+.032f),UiTheme.SizeLabel,new Color(UiTheme.GoldLabel.r,UiTheme.GoldLabel.g,UiTheme.GoldLabel.b,.7f),TextAnchor.MiddleRight);
        }
        static (Button button,Image frame,NavFocus focus) Base(string name,Transform parent,Vector2 min,Vector2 max,UnityEngine.Events.UnityAction action,Vector2 inset)
        {
            var r=MenuUI.Rect(name,parent,min,max,inset);var im=r.gameObject.AddComponent<Image>();im.color=new Color(0,0,0,.35f);
            var b=r.gameObject.AddComponent<Button>();b.targetGraphic=im;b.transition=Selectable.Transition.None;b.onClick.AddListener(action);
            var f=r.gameObject.AddComponent<NavFocus>();f.Mode=NavFocus.Kind.Card;return (b,im,f);
        }
        Button ArenaCard(int i,Transform parent,Vector2 min,Vector2 max,UnityEngine.Events.UnityAction action,out Text label,out NavFocus focus)
        {
            var (b,frame,f)=Base(RunMaps.All[i].Name,parent,min,max,action,new Vector2(8,0));focus=f;f.Frame=frame;f.Grow=1.02f;f.ChosenEdge=UiTheme.GoldBright;
            var inner=MenuUI.Rect("Card art",b.transform,Vector2.zero,Vector2.one,new Vector2(2,2));inner.gameObject.AddComponent<RectMask2D>();
            MenuUI.Panel("Card backing",inner,Vector2.zero,Vector2.one,UiTheme.InkDeep);
            var art=MenuUI.Rect("Current playable arena preview",inner,Vector2.zero,Vector2.one).gameObject.AddComponent<RawImage>();art.texture=Preview(i);art.raycastTarget=false;
            var fit=art.gameObject.AddComponent<AspectRatioFitter>();fit.aspectMode=AspectRatioFitter.AspectMode.EnvelopeParent;fit.aspectRatio=16f/9;f.Art=art;
            var fade=MenuUI.Rect("Name shade",inner,Vector2.zero,new Vector2(1,.62f)).gameObject.AddComponent<RawImage>();fade.texture=cardFade;fade.raycastTarget=false;
            Shadow(MenuUI.Text((i+1).ToString("00"),inner,new Vector2(.04f,.80f),new Vector2(.5f,.98f),14,MenuUI.Gold,TextAnchor.MiddleLeft),1);
            label=MenuUI.Text("",inner,new Vector2(.02f,.03f),new Vector2(.98f,.24f),21,MenuUI.Ivory,TextAnchor.MiddleLeft);label.resizeTextForBestFit=true;label.resizeTextMinSize=12;label.resizeTextMaxSize=21;f.CardLabel=label;Shadow(label,.9f);
            var accent=MenuUI.Panel("Chosen accent",inner,Vector2.zero,new Vector2(1,0),MenuUI.Gold);accent.rectTransform.sizeDelta=new Vector2(0,4);accent.rectTransform.pivot=new Vector2(.5f,0);f.Accent=accent;
            return b;
        }
        // a boss's card art: its own arena when it has one, otherwise the selected arena
        public static Texture2D EncounterPreview(int encounter,int map)
        {
            var own=RunDirector.Bosses[encounter].Map;int m=own!=null?RunMaps.IndexOf(own):map;
            return m>=0&&m<RunMaps.All.Length?Preview(m):null;
        }
        // A boss's portrait (Menu/Resources/Menu/BossPortraits, made by BossPortraitCapture): the card name without
        // spaces; the Tutorial uses the Tempo Tyrant's.
        public static Texture2D Portrait(int encounter)
        {
            var name=CardName(encounter);if(name.StartsWith("Tutorial")||name.EndsWith("Tempo Tyrant"))name="Tempo Tyrant";
            return Resources.Load<Texture2D>("Menu/BossPortraits/"+new string(System.Array.FindAll(name.ToCharArray(),char.IsLetterOrDigit)));
        }
        // Fighter card: the boss's portrait under the arena card's frame (number, gold accent, focus). Boss cards show the
        // whole body with the name along the bottom; half-height miniboss cards show the upper body with the name on the
        // left. A boss without a portrait keeps the name-only card.
        Button BossCard(int encounter,int slot,bool mini,Transform parent,Vector2 min,Vector2 max,UnityEngine.Events.UnityAction action,out Text label,out NavFocus focus,out RawImage art)
        {
            var (b,frame,f)=Base(CardName(encounter),parent,min,max,action,new Vector2(8,0));focus=f;f.Frame=frame;f.Grow=mini?1.03f:1.02f;f.ChosenEdge=UiTheme.GoldBright;
            var inner=MenuUI.Rect("Card face",b.transform,Vector2.zero,Vector2.one,new Vector2(2,2));inner.gameObject.AddComponent<RectMask2D>();
            MenuUI.Panel("Card backing",inner,Vector2.zero,Vector2.one,UiTheme.InkDeep);
            var portrait=Portrait(encounter);art=null;
            if(portrait)
            {
                // half-height cards: the portrait sits in the right part of the card, zoomed to the head and shoulders
                var holder=mini?MenuUI.Rect("Portrait frame",inner,new Vector2(.36f,0),Vector2.one):inner;
                art=MenuUI.Rect("Fighter portrait",holder,Vector2.zero,Vector2.one).gameObject.AddComponent<RawImage>();art.texture=portrait;art.raycastTarget=false;
                var fit=art.gameObject.AddComponent<AspectRatioFitter>();fit.aspectMode=AspectRatioFitter.AspectMode.EnvelopeParent;fit.aspectRatio=(float)portrait.width/portrait.height;f.Art=art;
                if(mini){art.rectTransform.pivot=new Vector2(.5f,.66f);art.rectTransform.localScale=new Vector3(1.6f,1.6f,1);}
                if(mini){var side=MenuUI.Rect("Name shade",inner,Vector2.zero,new Vector2(.6f,1)).gameObject.AddComponent<RawImage>();side.texture=strip;side.color=new Color(1,1,1,.95f);side.raycastTarget=false;}
                else{var fade=MenuUI.Rect("Name shade",inner,Vector2.zero,new Vector2(1,.55f)).gameObject.AddComponent<RawImage>();fade.texture=cardFade;fade.raycastTarget=false;}
            }
            if(mini)
            {
                Shadow(MenuUI.Text((slot+1).ToString("00"),inner,new Vector2(.04f,.62f),new Vector2(.3f,.96f),12,MenuUI.Gold,TextAnchor.MiddleLeft),1);
                // one line, one size across the row: the name runs under the portrait over the left and bottom shades
                if(portrait){var low=MenuUI.Rect("Name floor",inner,Vector2.zero,new Vector2(1,.7f)).gameObject.AddComponent<RawImage>();low.texture=cardFade;low.raycastTarget=false;}
                label=MenuUI.Text("",inner,new Vector2(.04f,.05f),new Vector2(.97f,.42f),18,MenuUI.Ivory,TextAnchor.LowerLeft);label.resizeTextMaxSize=18;miniLabels.Add(label);
            }
            else
            {
                Shadow(MenuUI.Text((slot+1).ToString("00"),inner,new Vector2(.04f,.80f),new Vector2(.5f,.98f),14,MenuUI.Gold,TextAnchor.MiddleLeft),1);
                label=portrait?MenuUI.Text("",inner,new Vector2(.05f,.03f),new Vector2(.95f,.38f),22,MenuUI.Ivory,TextAnchor.LowerLeft)
                              :MenuUI.Text("",inner,new Vector2(.06f,.2f),new Vector2(.94f,.8f),26,MenuUI.Ivory,TextAnchor.MiddleCenter);
                label.resizeTextMaxSize=portrait?22:26;
            }
            // best fit wraps before it shrinks: long boss names take two lines at full size, mini names stay one line
            label.resizeTextForBestFit=true;label.resizeTextMinSize=mini?12:16;label.lineSpacing=.95f;f.CardLabel=label;Shadow(label,.9f);
            var accent=MenuUI.Panel("Chosen accent",inner,Vector2.zero,new Vector2(1,0),MenuUI.Gold);accent.rectTransform.sizeDelta=new Vector2(0,mini?3:4);accent.rectTransform.pivot=new Vector2(.5f,0);f.Accent=accent;
            return b;
        }
        Button Tab(string name,Transform parent,Vector2 min,Vector2 max,UnityEngine.Events.UnityAction action,out Text label,out NavFocus focus)
        {
            var (b,frame,f)=Base(name,parent,min,max,action,new Vector2(8,0));focus=f;f.Mode=NavFocus.Kind.Tab;frame.color=new Color(0,0,0,.42f);
            var line=MenuUI.Panel("Tab underline",b.transform,Vector2.zero,new Vector2(1,0),Faint);line.rectTransform.sizeDelta=new Vector2(0,2);line.rectTransform.pivot=new Vector2(.5f,0);f.Frame=line;
            var mark=MenuUI.Rect("Chosen diamond",b.transform,new Vector2(0,.5f),new Vector2(0,.5f)).gameObject.AddComponent<FocusDiamond>();
            mark.rectTransform.sizeDelta=new Vector2(10,16);mark.rectTransform.anchoredPosition=new Vector2(18,0);mark.color=MenuUI.Gold;mark.raycastTarget=false;f.Accent=mark;
            label=MenuUI.Text(name,b.transform,Vector2.zero,Vector2.one,21);label.rectTransform.offsetMin=new Vector2(30,3);label.rectTransform.offsetMax=new Vector2(-12,-3);
            label.resizeTextForBestFit=true;label.resizeTextMinSize=12;label.resizeTextMaxSize=21;f.CardLabel=label;
            return b;
        }
        // Cycling text row (the main menu's Difficulty row): a text row with two small drawn chevron cues hugging the label.
        Button CycleRow(Transform parent,Vector2 min,Vector2 max,UnityEngine.Events.UnityAction action,out Text label)
        {
            var b=MenuUI.Row(AlliesText(0),parent,min,max,action,22,Vector2.zero);label=b.GetComponentInChildren<Text>();
            var cue=new Color(MenuUI.Gold.r,MenuUI.Gold.g,MenuUI.Gold.b,.6f);
            alliesPrev=MenuUI.Chevron(b.transform,false,16,cue);alliesNext=MenuUI.Chevron(b.transform,true,16,cue);
            return b;
        }
        // Footer actions use the main menu's entries: transparent hit area, Ivory label (first Text child) and the gold
        // highlight strip on focus. The step's primary action (Continue / Launch) is larger, like Start Run.
        static Button NavButton(string name,Transform parent,Vector2 min,Vector2 max,UnityEngine.Events.UnityAction action,int size)=>MenuUI.Row(name,parent,min,max,action,size,Vector2.zero);
        // Back sits on the footer's left edge and the main action on its right edge, like Settings and the LAN lobby.
        Button TextButton(string name,Transform parent,Vector2 min,Vector2 max,UnityEngine.Events.UnityAction action){var b=NavButton(name,parent,min,max,action,24);b.GetComponentInChildren<Text>().alignment=TextAnchor.MiddleLeft;return b;}
        Button PrimaryButton(string name,Transform parent,Vector2 min,Vector2 max,UnityEngine.Events.UnityAction action){var b=NavButton(name,parent,min,max,action,28);b.GetComponentInChildren<Text>().alignment=TextAnchor.MiddleRight;return b;}
        // Explicit grid navigation keeps keyboard/controller focus inside this panel and predictable between rows.
        static void Link(params Button[][] rows)=>MenuUI.LinkGrid(System.Array.ConvertAll(rows,r=>(Selectable[])r));
    }
}
