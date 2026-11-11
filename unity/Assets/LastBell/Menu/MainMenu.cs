using System.Collections;
using LastBell.Run;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LastBell.Menu
{
    // Build index 0. The Confrontation menu is the only main menu (Risxhb, October 24, 2026); this scene keeps two roles:
    // the startup router that hands over to it, and Boss Rush's additive live arena backdrop (arenas, orbit camera, battle).
    // Its own menu UI (logo, button row, music panel, Settings modal) was retired on November 10, 2026.
    public sealed class MainMenu : MonoBehaviour
    {
        public const string ScenePath = "Assets/LastBell/Menu/ShowcaseMainMenu.unity";
        public MenuMusic Music { get; private set; }
        public MenuBattle Battle { get; private set; }

        // Handle of the one additive copy Boss Rush select loads as its backdrop: arenas, orbit camera and battle only.
        // Bound to that scene instance, so a real menu load can never be mistaken for the backdrop.
        public static Scene BackdropScene;
        void Awake()
        {
            if(BackdropScene.IsValid()&&gameObject.scene==BackdropScene)
            {
                BackdropScene=default;
                if(!GetComponent<MenuBattle>())gameObject.AddComponent<MenuBattle>();
                var live=GetComponent<MenuShowcase>();if(!live)live=gameObject.AddComponent<MenuShowcase>();live.Manual=true;
                enabled=false;return;
            }
            if(!MenuAppearance.Route(gameObject))return;
            MenuLaunch.Clear();
            MenuReturnBridge.TestSession=false;
            if(MapPreviewCapture.Requested){MapPreviewCapture.Launch();return;}
            // Existing run diagnostics retain their own entry point.
            // the shop suite (-lastbellShopTest) too: it starts from the run scene's own Menu state
            if (RunAutoTest.Requested || Run.Shop.RunShopAutoTest.Requested) { SceneManager.LoadScene(RunMaps.All[0].RunScene); return; }
            // Only the -lastbellMenuTest diagnostic stays here: the live showcase (battle, arena rotation, menu music), no UI.
            if (MenuSmokeTest.Requested) MenuSmokeTest.PrepareInput();
            MenuSettings.Restore();
            Music = MenuMusic.Ensure();
            Music.PlayMenu();
            Battle = GetComponent<MenuBattle>();
            if (!Battle) Battle = gameObject.AddComponent<MenuBattle>();
            if (!GetComponent<MenuShowcase>()) gameObject.AddComponent<MenuShowcase>();
            if (MenuSmokeTest.Requested) gameObject.AddComponent<MenuSmokeTest>();
        }
    }

    // Only installed after entering the run from this menu. Direct run-scene diagnostics stay untouched.
    public sealed class MenuReturnBridge : MonoBehaviour
    {
        public static bool TestSession;
        static bool returnToSetup;
        static MenuReturnBridge active;
        public static bool Active => active;
        RunDirector observed;
        public static void Ensure()
        {
            if (active) return;
            var go = new GameObject("Main menu return routing"); DontDestroyOnLoad(go); active = go.AddComponent<MenuReturnBridge>();
        }
        public static bool Return(bool setup)
        {
            if(!RunDirector.Instance||RunDirector.Instance.State==RunState.Menu)return false;returnToSetup=setup&&TestSession;
            if(RunDirector.Instance.ReturnToMenu())return true;returnToSetup=false;return false;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSession(){TestSession=false;returnToSetup=false;active=null;}
        void Update()
        {
            if (observed == RunDirector.Instance) return;
            if (observed) observed.StateChanged -= OnState;
            observed = RunDirector.Instance;
            if (observed) observed.StateChanged += OnState;
        }
        void OnState(RunState state)
        { if (state == RunState.Menu) StartCoroutine(Return()); }
        IEnumerator Return()
        {
            yield return null;bool setup=returnToSetup;returnToSetup=false;MenuLaunch.Clear();
            // "Test Setup" reopens Boss Rush on the main menu (the old hero-select scene is retired).
            if(setup)ConfrontationMenu.ResumeBossRush=true;
            SceneManager.LoadSceneAsync(MenuAppearance.ScenePath);
        }
        void OnDestroy() { if (observed) observed.StateChanged -= OnState; if (active == this) active = null; }
    }
}
