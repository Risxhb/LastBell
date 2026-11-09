using System;
using System.Collections.Generic;
using System.Linq;
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
        // Risxhb, October 26, 2026: Bittersweet Dawn opens the menu, The Hero Rises is the second song; the two alternate
        // (each plays once, then the next; Next skips). The earlier playlist was { "MainMenu2", "MainMenuOpening",
        // "MainMenuMusic" }. With a single track it loops and the Confrontation menu hides its skip button.
        static readonly string[] MenuTracks = { "BittersweetDawn", "TheHeroRises" };
        static readonly string[] MenuTitles = { "BITTERSWEET DAWN", "THE HERO RISES" };
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

        // ---------------- wing music (Risx, 9 Nov 2026) ----------------
        // Each area's minibosses share 1-2 wing songs from Resources/Menu/WingMusic/<area>/ (played in name order, A -> B -> A,
        // crossfading at each track end). The wing song keeps playing from the area's first miniboss intro to its area boss
        // intro: fights, rewards and the shop never restart it (it ducks outside combat). Area bosses keep their own track.
        public const string WingFolder = "Menu/WingMusic/";
        public const float WingDuck = .6f, WingCrossfade = 2;
        static readonly Dictionary<string, AudioClip[]> wingCache = new Dictionary<string, AudioClip[]>();
        // The area whose wing music plays for this fight, or null (area bosses, final, bonus, tutorial, no fight).
        // Matched by name so a LAN client's replica (which has no encounter index) gets the same answer.
        public static string WingFor(BossEncounter encounter)
        {
            if (!encounter || TrackFor(encounter) != null) return null;
            var name = encounter.DisplayName ?? "";
            foreach (var area in Campaign.Areas)
                if (area.Minibosses.Any(m => name.EndsWith(m, StringComparison.Ordinal))) return area.Name;
            return null;
        }
        // The area's wing songs in play order; empty (one warning per area) when its folder has none.
        public static AudioClip[] WingTracks(string area)
        {
            if (string.IsNullOrEmpty(area)) return new AudioClip[0];
            if (wingCache.TryGetValue(area, out var clips)) return clips;
            clips = Resources.LoadAll<AudioClip>(WingFolder + area).OrderBy(c => c.name, StringComparer.Ordinal).Take(2).ToArray();
            if (clips.Length == 0) Debug.LogWarning("No wing music for " + area + " (Resources/" + WingFolder + area + "/)");
            return wingCache[area] = clips;
        }
        string wing; AudioClip[] wingClips = new AudioClip[0]; int wingIndex; bool wingObserved;
        float duck = 1, duckTarget = 1;
        bool WingPlaying => wing != null && boss && boss.clip && Array.IndexOf(wingClips, boss.clip) >= 0;
        public string Wing => WingPlaying ? wing : null;   // the area whose wing song is on the boss source, or null

        public static void OnRunState(RunDirector director, RunState state)
        {
            var music = Ensure();
            // between fights (rewards, shop, results) the wing song ducks; the next intro brings it back up
            music.duckTarget = state == RunState.Intro || state == RunState.Combat ? 1 : WingDuck;
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
            var area = WingFor(director.Boss);
            if (area != null) { BeginWing(area, director); return; }
            var track = TrackFor(director.Boss);
            var clip = track == null ? null : Resources.Load<AudioClip>("Menu/BossMusic/" + track);
            if (!clip) { Debug.LogWarning("No boss music for " + director.Boss); return; }
            StartBossClip(clip, true, director);
            wing = null;
        }
        void BeginWing(string area, RunDirector director)
        {
            // the next miniboss of the same wing: keep the song and its position, just bring it back up
            if (area == wing && WingPlaying && boss.isPlaying) { duckTarget = 1; BeginFade(true, director); return; }
            var clips = WingTracks(area);
            if (clips.Length == 0) return;
            Debug.Log("[music] wing " + area + ": " + clips[0].name);
            StartBossClip(clips[0], clips.Length == 1, director);
            wing = area; wingClips = clips; wingIndex = 0;
        }
        void StartBossClip(AudioClip clip, bool loop, RunDirector director, float seconds = .45f)
        {
            // a ducked wing song fades out from where it is
            bossMix *= WingPlaying ? duck : 1; duck = duckTarget = 1;
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
            boss.volume = 0; boss.clip = clip; boss.loop = loop; boss.Play(); wingObserved = false;
            BeginFade(true, director, seconds);
        }
        // Two wing songs: crossfade into the other one near the end (or straight away if a song stopped on its own).
        void AdvanceWingPlaylist()
        {
            if (!inCombat || transition < 1 || !WingPlaying || wingClips.Length < 2) return;
            if (boss.isPlaying) wingObserved = true;
            bool ending = boss.isPlaying && boss.clip.length - boss.time < WingCrossfade;
            if (!ending && (boss.isPlaying || !wingObserved)) return;
            wingIndex = (wingIndex + 1) % wingClips.Length;
            float level = duck, target = duckTarget;   // keep a duck that is under way
            StartBossClip(wingClips[wingIndex], false, null, WingCrossfade);
            duck = level; duckTarget = target;
        }
        float fadeSeconds = .45f;
        void BeginFade(bool toBoss, RunDirector intro = null, float seconds = .45f)
        {
            inCombat = toBoss; countdown = intro; transition = 0; fadeSeconds = seconds;
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
                    ? countdown.IntroProgress : Mathf.MoveTowards(transition, 1, Time.unscaledDeltaTime / fadeSeconds));
            duck = Mathf.MoveTowards(duck, duckTarget, Time.unscaledDeltaTime / 1.5f);
            AdvanceWingPlaylist();
            ApplyVolume();
        }
        // A fight's own crossfade (TempoTyrantAudio swapping song versions) scales the boss track by this; 1 = untouched.
        // BossLevel is the boss track's level without it, for the fight's fading-out tail source.
        public float BossFade = 1;
        public float BossLevel => Volume * envelope * bossMix;
        void ApplyVolume()
        {
            if (menu) menu.volume = Volume * envelope * menuMix;
            if (boss) boss.volume = Volume * envelope * bossMix * BossFade * (WingPlaying ? duck : 1);
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
