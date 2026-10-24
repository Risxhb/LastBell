using UnityEngine;

namespace LastBell.Run
{
    // Metronome ticks and the music-tempo adapter for The Tempo Tyrant. Everything is derived from the encounter's
    // TempoClock; this component never decides tempo, and it only ever touches this fight's own track.
    //
    // Music: "By My Measure" (Risxhb, October 23, 2026). The supplied song drifts from about 162 to 168 BPM, so it was
    // prepared offline (tools/tempo-tyrant: beat_track.py + warp_wsola.py) into two pitch-preserved, beat-aligned
    // versions on exact grids: "By My Measure" at 165 BPM (Haste, 1.0x) and "By My Measure (75)" at 123.75 BPM
    // (Normal, 0.75x), both with the same beat count. MenuMusic starts the Haste version for the countdown as for any
    // boss. This adapter then:
    //   - at combat start, aligns the music's beat phase to the clock (a sub-half-beat seek), fixing a beat offset K;
    //   - on every tempo commit, swaps to the other version at the same beat index (K + clock beat);
    //   - resyncs when the playing position drifts more than 50 ms from the clock (frame jitter, loop wrap);
    //   - pauses the track with the game pause and resyncs on resume.
    // A raw AudioSource.pitch change is never used (it lowers the pitch); Unity's pitch-shifter mixer effect is
    // unavailable on WebGL, so prepared versions are the portable path.
    //
    // Ticks: one per beat (one rod swing), scheduled on the DSP clock a short lookahead before the beat from the clock's
    // live phase (no accumulated drift); pause stops both voices, nothing is replayed on resume. Haste ticks are higher and
    // sharper, Normal deeper; the three transition ticks are accented. While the track plays the plain ticks drop to a
    // low level (the song carries the pulse) and only the accented transition ticks stay at full level.
    public sealed class TempoTyrantAudio : MonoBehaviour
    {
        public enum MusicStatus { MissingMusic, MissingSlowVersion, Paired }
        // Haste = the 1.33x version (220 BPM grid); Normal = the song as written (warped onto an exact 165 BPM grid)
        public const string HasteTrack = "By My Measure (Haste)", SlowTrack = "By My Measure";
        // measured grids of the prepared files (warp_wsola.py output): first beat offset and BPM
        public const float HasteGridBpm = 220f, HasteFirstBeat = 0.1052f, SlowGridBpm = 165f, SlowFirstBeat = 0.1403f;
        public const float ResyncTolerance = 0.05f;

        static AudioClip hasteTick, normalTick, accentTick;
        TempoTyrantEncounter boss;
        AudioSource a, b; bool flip;
        int scheduledBeat = -1;
        int seenCommits;
        AudioClip musicFast, musicSlow;
        bool aligned, pausedByUs; double beatOffset;
        public MusicStatus Music { get; private set; }
        public int TicksScheduled { get; private set; }
        public int PauseStops { get; private set; }
        public int MusicSwaps { get; private set; }
        public int MusicResyncs { get; private set; }
        public float LastMusicError { get; private set; }   // seconds, playing position minus clock-derived position
        public bool MusicLocked => aligned;
        public string StatusText => Music == MusicStatus.Paired ? $"music: By My Measure {(boss && boss.Clock.State == Tempo.Haste ? "1.33x" : "1.0x")} {(aligned ? "locked" : "waiting")} · err {LastMusicError * 1000:0} ms · swaps {MusicSwaps}" :
            Music == MusicStatus.MissingSlowVersion ? "music: slow version missing (no pitch-preserving stretch; stays 1.0x)" : "music: MISSING (ticks only)";

        public void Bind(TempoTyrantEncounter owner)
        {
            boss = owner;
            a = MakeSource(); b = MakeSource();
            EnsureClips();
            musicFast = Resources.Load<AudioClip>("Menu/BossMusic/" + HasteTrack);
            musicSlow = Resources.Load<AudioClip>("Menu/BossMusic/" + SlowTrack);
            Music = !musicFast ? MusicStatus.MissingMusic : !musicSlow ? MusicStatus.MissingSlowVersion : MusicStatus.Paired;
        }

        AudioSource MakeSource()
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false; s.loop = false; s.spatialBlend = 0; s.dopplerLevel = 0;
            return s;
        }

        static void EnsureClips()
        {
            if (hasteTick) return;
            hasteTick = Click("TyrantTickHaste", 2350f, 0.045f, 0.55f, 0f);
            normalTick = Click("TyrantTickNormal", 1150f, 0.075f, 0.6f, 0f);
            accentTick = Click("TyrantTickAccent", 1650f, 0.11f, 0.95f, 420f);
        }

        // short wooden/brass click: a decaying partial plus a noise transient (and an optional low partial for accents)
        static AudioClip Click(string name, float freq, float length, float gain, float low)
        {
            int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            int n = Mathf.CeilToInt(length * rate);
            var data = new float[n]; var rng = new System.Random(7);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate, env = Mathf.Exp(-t * 60f), noise = (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 400f);
                float v = Mathf.Sin(2 * Mathf.PI * freq * t) * env + 0.5f * noise;
                if (low > 0) v += 0.8f * Mathf.Sin(2 * Mathf.PI * low * t) * Mathf.Exp(-t * 25f);
                data[i] = Mathf.Clamp(v * gain, -1, 1);
            }
            var clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Volume => PlayerPrefs.GetFloat("LastBell.Menu.MusicVolume", 0.6f);

        public void StopAll() { if (a) a.Stop(); if (b) b.Stop(); scheduledBeat = int.MaxValue; }

        // ---------------- music ----------------
        AudioSource OurSource()
        {
            if (Music != MusicStatus.Paired) return null;
            var src = LastBell.Menu.MenuMusic.Ensure().Source;
            return src && (src.clip == musicFast || src.clip == musicSlow) ? src : null;
        }
        static float Period(AudioClip c, AudioClip fast) => 60f / (c == fast ? HasteGridBpm : SlowGridBpm);
        static float First(AudioClip c, AudioClip fast) => c == fast ? HasteFirstBeat : SlowFirstBeat;
        int GridBeats(AudioClip c) => Mathf.Max(1, Mathf.FloorToInt((c.length - First(c, musicFast)) / Period(c, musicFast)));
        // where the music should be for the clock's current beat (wrapping at the clip's last whole beat)
        float ExpectedTime(AudioClip c)
        {
            int n = GridBeats(c); double idx = beatOffset + boss.Clock.Beat;
            idx = ((idx % n) + n) % n;
            return First(c, musicFast) + (float)(idx * Period(c, musicFast));
        }

        void SyncMusic(bool paused)
        {
            var src = OurSource(); if (!src) return;
            if (paused) { if (src.isPlaying) { src.Pause(); pausedByUs = true; } return; }
            if (pausedByUs) { src.UnPause(); pausedByUs = false; Resync(src, true); }
            if (!aligned)
            {
                // fix the beat offset K so the nearest music beat sits on the clock's current phase
                double musicBeat = (src.time - First(src.clip, musicFast)) / Period(src.clip, musicFast);
                beatOffset = System.Math.Round(musicBeat - boss.Clock.Beat);
                aligned = true; Resync(src, true);
            }
            var want = boss.Clock.State == Tempo.Haste ? musicFast : musicSlow;
            if (src.clip != want)
            {
                src.clip = want; src.time = ExpectedTime(want); src.Play(); MusicSwaps++;
            }
            Resync(src, false);
        }

        void Resync(AudioSource src, bool force)
        {
            float expect = ExpectedTime(src.clip);
            float err = src.time - expect; float len = src.clip.length;
            if (err > len / 2) err -= len; else if (err < -len / 2) err += len;
            LastMusicError = err;
            if (force || Mathf.Abs(err) > ResyncTolerance) { src.time = Mathf.Clamp(expect, 0, len - 0.01f); MusicResyncs++; LastMusicError = 0; }
        }

        void OnDestroy()
        {
            var music = Music == MusicStatus.Paired ? FindAnyObjectByType<LastBell.Menu.MenuMusic>() : null;   // never create one while tearing down
            var src = music ? music.Source : null;
            if (pausedByUs && src) src.UnPause();
        }

        // ---------------- ticks ----------------
        void Update()
        {
            var d = RunDirector.Instance;
            bool fighting = boss && !boss.Dead && d != null && d.State == RunState.Combat && d.World != null && d.World.CombatActive;
            if (fighting) SyncMusic(d.Paused);
            if (!fighting || d.Paused)
            {
                if (d != null && d.Paused && (a.isPlaying || b.isPlaying || scheduledBeat >= 0)) PauseStops++;
                if (a.isPlaying || b.isPlaying) { a.Stop(); b.Stop(); }
                if (scheduledBeat != int.MaxValue) scheduledBeat = -1;   // reschedule from the live phase after resume
                return;
            }
            if (scheduledBeat == int.MaxValue) return;   // defeated
            var c = boss.Clock;
            double frac = c.Beat - System.Math.Floor(c.Beat);
            int next = (int)System.Math.Floor(c.Beat) + 1;
            float until = (float)((1 - frac) * c.BeatSeconds);
            if (next > scheduledBeat && until < 0.12f)
            {
                scheduledBeat = next;
                bool accent = c.Transitioning;
                bool songPlaying = OurSource() != null;
                var src = flip ? a : b; flip = !flip;
                src.clip = accent ? accentTick : c.State == Tempo.Haste ? hasteTick : normalTick;
                src.volume = Volume * (accent ? 1f : songPlaying ? 0.22f : 0.7f);
                src.PlayScheduled(AudioSettings.dspTime + Mathf.Max(0, until));
                TicksScheduled++;
            }
            if (c.Commits != seenCommits) seenCommits = c.Commits;
        }

        void OnDisable() { if (a) a.Stop(); if (b) b.Stop(); }
    }
}
