using System;
using System.Linq;
using LastBell.Menu;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LastBell.Run.Networking
{
    // Owns a lobby while in the menu and hands it to the host/client fight adapters after the map loads.
    public sealed class LanSessionCoordinator : MonoBehaviour
    {
        public static LanSessionCoordinator Instance { get; private set; }
        public static string LastError { get; private set; }
        public LanLobby Lobby { get; private set; }
        public bool Loading { get; private set; }
        public bool InFight { get; private set; }
        public LanClientWorld ClientView => client;
        LanHostFightPump host; LanClientWorld client; bool hostSceneLoaded, hostStarted, previousShop;
        int map, encounter;
        public static LanSessionCoordinator Open(LanLobby lobby)
        {
            if (lobby == null || Instance) throw new InvalidOperationException("A session is already open.");
            var go = new GameObject("LAN session coordinator"); DontDestroyOnLoad(go);
            var session = go.AddComponent<LanSessionCoordinator>();
            Instance = session; LastError = null; session.Lobby = lobby; lobby.FightStarting += session.StartLoading;
            return session;
        }
        public bool HostStart() => Lobby != null && Lobby.IsHost && !Loading && Lobby.Start();
        void StartLoading()
        {
            map = RunMaps.IndexOf(Lobby.Arena);
            encounter = Array.FindIndex(RunDirector.Bosses, b => b.Name.StartsWith(Lobby.Encounter, StringComparison.Ordinal));
            if (!RunMaps.InBuild(map) || encounter < 0 || !LanLobby.ValidEncounter(Lobby.Encounter)) { Fail("The selected arena or encounter is unavailable in this build."); return; }
            Loading = true;
            try
            {
                if (Lobby.IsHost) host = LanHostFightPump.Create(Lobby);
                SceneManager.sceneLoaded += OnSceneLoaded;
                SceneManager.LoadSceneAsync(RunMaps.All[map].RunScene, LoadSceneMode.Single);
            }
            catch (Exception ex) { Fail("Could not load the arena: " + ex.Message); }
        }
        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!Loading || scene.path != RunMaps.All[map].RunScene) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            try
            {
                if (Lobby.IsHost) { hostSceneLoaded = true; previousShop = Shop.RunShopFlow.Enabled; Shop.RunShopFlow.Enabled = false; }
                else
                {
                    client = LanClientWorld.Create(Lobby);
                    if (!Lobby.ClientSceneReady()) throw new InvalidOperationException("Could not report that the arena loaded.");
                    Loading = false; InFight = true;
                }
            }
            catch (Exception ex) { Fail("Could not enter the arena: " + ex.Message); }
        }
        void Update()
        {
            if (Lobby == null) return;
            if (!Loading && !InFight) Lobby.Pump();
            if (Lobby == null) return; // a start callback can fail and close the session while Pump is running
            string failure = host ? host.Error : client ? client.Error : Lobby.Error;
            if (failure != null) { Fail(failure); return; }
            if (hostSceneLoaded && !hostStarted && Lobby.AllClientsReady)
            {
                var director = RunDirector.Instance;
                var local = Lobby.Members.FirstOrDefault(m => m.Id == 0);
                if (!director || local == null || !director.StartPracticeOnSelectedMap(local.ClassName, encounter)) { Fail("The host could not start the encounter."); return; }
                hostStarted = true; Loading = false; InFight = true;
            }
            if (hostStarted && RunDirector.Instance && RunDirector.Instance.State == RunState.Menu) Leave();
        }
        public void Leave() => Close(null, true);
        void Fail(string reason) => Close(reason, true);
        void Close(string reason, bool returnToMenu)
        {
            LastError = reason;
            if (reason != null) Debug.LogWarning("[LAN] session closed: " + reason);
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Lobby != null) Lobby.FightStarting -= StartLoading;
            if (hostSceneLoaded) Shop.RunShopFlow.Enabled = previousShop;
            if (client) Destroy(client.gameObject);
            if (host) { Destroy(host.gameObject); host = null; }
            else Lobby?.Dispose();
            Lobby = null; Instance = null;
            if (returnToMenu && SceneManager.GetActiveScene().path != ConfrontationMenu.ScenePath) SceneManager.LoadScene(ConfrontationMenu.ScenePath);
            Destroy(gameObject);
        }
        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Lobby != null) { Lobby.FightStarting -= StartLoading; if (!host) Lobby.Dispose(); }
            if (hostSceneLoaded) Shop.RunShopFlow.Enabled = previousShop;
            if (Instance == this) Instance = null;
        }
    }
}
