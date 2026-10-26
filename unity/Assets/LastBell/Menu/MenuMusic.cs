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
        AudioSource menu, boss, previousBoss;
        float menuMix = 1, bossMix, previousMix, envelope;
        float fromMenu, fromBoss, fromPrevious, transition = 1;
        RunDirector countdown;
        bool inCombat, menuPaused;
        // Risxhb, October 26, 2026: Bittersweet Dawn is the only menu song and loops. The earlier three-song playlist was
        // { "MainMenu2", "MainMenuOpening", "MainMenuMusic" }; with more than one track, Next cycles and each song plays once.
        static readonly string[] MenuTracks = { "BittersweetDawn" };
        static readonly string[] MenuTitles = { "BITTERSWEET DAWN" };
        public static int TrackCount => MenuTracks.Length;
        bool playbackObserved, trackStarted;
        int trackIndex;
        public bool PlayingOpening => trackIndex == 0;
        public bool UserPaused { get; private set; }
        public int TrackIndex => trackIndex;
        public string TrackTitle => MenuTitles[trackIndex];
        public AudioSource MenuSource => menu;
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
            menu = MakeSource(); boss = MakeSource(); previousBoss = MakeSource();
            SelectTrack(0);
        }
        void SelectTrack(int index)
        {
            trackIndex = index;
            menu.Stop(); menu.clip = Resources.Load<AudioClip>("Menu/" + MenuTracks[trackIndex]);
            menu.loop = MenuTracks.Length == 1; playbackObserved = false; trackStarted = false;
            if (!menu.clip) { Debug.LogError("Missing menu track: " + MenuTracks[trackIndex]); return; }
            if (!UserPaused && !menuPaused) { menu.Play(); trackStarted = true; }
        }
        public void NextTrack() => SelectTrack((trackIndex + 1) % MenuTracks.Length);
        public void PauseTrack() { UserPaused = true; menu.Pause(); }
        public void ResumeTrack()
        {
            UserPaused = false;
            if (menuPaused || !menu.clip) return;
            if (trackStarted) menu.UnPause(); else { menu.Play(); trackStarted = true; }
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
            if (encounter is TempoTyrantEncounter) return TempoTyrantAudio.HasteTrack;   // the fight opens in Haste: By My Measure at 1.33x; the adapter swaps to 1.0x in Normal
            return null;
        }
        public static void OnRunState(RunDirector director, RunState state)
        {
            var music = Ensure();
            if (state == RunState.Menu) music.PlayMenu();
            else if (state == RunState.Ready && music.countdown) music.BeginFade(true);
            else if (state == RunState.Intro) music.BeginBoss(director);
            else if (state == RunState.Combat)
            {
                if (!music.inCombat) music.BeginBoss(director);
                if (music.inCombat) music.AdvanceFade(1);
            }
        }
        void BeginBoss(RunDirector director)
        {
            var track = TrackFor(director.Boss);
            var clip = track == null ? null : Resources.Load<AudioClip>("Menu/BossMusic/" + track);
            if (!clip) { Debug.LogWarning("No boss music for " + director.Boss); return; }
            // Keep the outgoing boss source running while the new fight fades in. For a rapid
            // interrupted countdown, retain the louder source and reuse the quieter channel.
            if (previousMix > bossMix)
            {
                var source = boss; boss = previousBoss; previousBoss = source;
                float mix = bossMix; bossMix = previousMix; previousMix = mix;
            }
            previousBoss.Stop();
            var incoming = previousBoss; previousBoss = boss; boss = incoming;
            previousMix += bossMix; bossMix = 0;
            boss.volume = 0; boss.clip = clip; boss.Play();
            BeginFade(true, director);
        }
        void BeginFade(bool toBoss, RunDirector intro = null)
        {
            inCombat = toBoss; countdown = intro; transition = 0;
            fromMenu = menuMix; fromBoss = bossMix; fromPrevious = previousMix;
            ApplyVolume();
        }
        void AdvanceFade(float progress)
        {
            transition = Mathf.Clamp01(progress);
            menuMix = Mathf.Lerp(fromMenu, inCombat ? 0 : 1, transition);
            bossMix = Mathf.Lerp(fromBoss, inCombat ? 1 : 0, transition);
            previousMix = Mathf.Lerp(fromPrevious, 0, transition);
            if (transition == 1)
            {
                countdown = null; previousBoss.Stop();
                if (inCombat) { menu.Pause(); menuPaused = true; }
                else { boss.Stop(); menuPaused = false; }
            }
            ApplyVolume();
        }
        public void PlayMenu()
        {
            if (!menu) return;
            if (inCombat)
            {
                menuPaused = false;
                NextTrack(); // A genuine menu return starts the next menu song (the only one, when there is one) from its beginning.
                BeginFade(false);
            }
            else if (menuPaused) { menuPaused = false; if (!UserPaused) ResumeTrack(); }
        }
        void LateUpdate()
        {
            if (active != this) return;
            // Manual pause and combat suspension are distinct from a naturally finished track.
            // Arena changes never touch the persistent source or this playlist cursor.
            if (menu.isPlaying) playbackObserved = true;
            if (playbackObserved && !menuPaused && !UserPaused && !menu.isPlaying && menu.clip) NextTrack();
            envelope = Mathf.MoveTowards(envelope, 1, Time.unscaledDeltaTime / .8f);
            // Follow the authoritative countdown after RunDirector.Update, including pause and
            // slow frames. Combat's state notification completes the fade on the zero tick.
            if (transition < 1)
                AdvanceFade(countdown && countdown.State == RunState.Intro
                    ? countdown.IntroProgress : Mathf.MoveTowards(transition, 1, Time.unscaledDeltaTime / .45f));
            ApplyVolume();
        }
        void ApplyVolume()
        {
            if (menu) menu.volume = Volume * envelope * menuMix;
            if (boss) boss.volume = Volume * envelope * bossMix;
            if (previousBoss) previousBoss.volume = Volume * envelope * previousMix;
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
