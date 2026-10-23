using UnityEngine;
using LastBell.Run;

namespace LastBell.Menu
{
    // One scene-independent music owner. Menu presentation objects are never retained with it.
    public sealed class MenuMusic : MonoBehaviour
    {
        const string VolumeKey = "LastBell.Menu.MusicVolume";
        static MenuMusic active;
        [Range(0, 1)] public float DefaultVolume = 0.6f;
        AudioSource menu, boss;
        float bossMix, envelope;
        bool inCombat, menuPaused;
        bool openingTrack;
        bool openingPlaybackObserved;
        public bool PlayingOpening => openingTrack;
        public AudioSource Source => inCombat ? boss : menu;
        public float Volume => PlayerPrefs.GetFloat(VolumeKey, DefaultVolume);
        public static MenuMusic Ensure()
        {
            if (active) return active;
            return new GameObject("Last Bell Music").AddComponent<MenuMusic>();
        }
        void Awake()
        {
            // Older menu scenes serialize this component on the menu presentation root.
            if (GetComponent<MainMenu>()) { Ensure(); Destroy(this); return; }
            if (active && active != this) { Destroy(gameObject); return; }
            active = this; DontDestroyOnLoad(gameObject);
            menu = MakeSource(); boss = MakeSource();
            menu.clip = Resources.Load<AudioClip>("Menu/MainMenuOpening");
            openingTrack = menu.clip;
            if (!openingTrack) menu.clip = Resources.Load<AudioClip>("Menu/MainMenuMusic");
            menu.loop = !openingTrack;
            if (menu.clip) menu.Play();
        }
        AudioSource MakeSource()
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.loop = true; source.playOnAwake = false; source.spatialBlend = 0; source.dopplerLevel = 0; source.volume = 0;
            return source;
        }
        public static string TrackFor(BossEncounter encounter)
        {
            if (encounter is AurelionEncounter) return "Crown of the Meridian";
            if (encounter is CrucivarEncounter) return "Pressure Beneath the Brass";
            if (encounter is BellkeeperEncounter) return "The Unfinished Hour";
            if (encounter is SylvaraEncounter) return "Where the Last Flower Falls";
            return null;
        }
        public static void OnRunState(RunDirector director, RunState state)
        {
            var music = Ensure();
            if (state == RunState.Menu || state == RunState.Ready || state == RunState.Intro) music.PlayMenu();
            else if (state == RunState.Combat)
            {
                var track = TrackFor(director.Boss);
                var clip = track == null ? null : Resources.Load<AudioClip>("Menu/BossMusic/" + track);
                if (!clip) { Debug.LogWarning("No boss music for " + director.Boss); return; }
                music.boss.Stop(); music.boss.clip = clip; music.boss.Play(); music.inCombat = true;
            }
        }
        public void PlayMenu()
        {
            if (!menu) return;
            inCombat = false;
            if (menuPaused) { menu.UnPause(); menuPaused = false; }
        }
        void Update()
        {
            if (active != this) return;
            // The opening song is prepended once per application session. Arena wipes and
            // selection/preparation never replace or restart this persistent source.
            if (openingTrack && menu.isPlaying) openingPlaybackObserved = true;
            if (openingTrack && openingPlaybackObserved && !menuPaused && !menu.isPlaying && menu.clip)
            {
                openingTrack = false; menu.clip = Resources.Load<AudioClip>("Menu/MainMenuMusic");
                menu.loop = true; if (menu.clip) menu.Play();
            }
            envelope = Mathf.MoveTowards(envelope, 1, Time.unscaledDeltaTime / .8f);
            bossMix = Mathf.MoveTowards(bossMix, inCombat ? 1 : 0, Time.unscaledDeltaTime / .45f);
            ApplyVolume();
            if (bossMix == 1 && !menuPaused) { menu.Pause(); menuPaused = true; }
            if (!inCombat && bossMix == 0 && boss.isPlaying) boss.Stop();
        }
        void ApplyVolume()
        {
            if (menu) menu.volume = Volume * envelope * (1 - bossMix);
            if (boss) boss.volume = Volume * envelope * bossMix;
        }
        public void SetVolume(float value)
        {
            PlayerPrefs.SetFloat(VolumeKey, Mathf.Clamp01(value)); ApplyVolume();
        }
        void OnDestroy()
        {
            if (active != this) return;
            active = null; PlayerPrefs.Save();
        }
    }
}
