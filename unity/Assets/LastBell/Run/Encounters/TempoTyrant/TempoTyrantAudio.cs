using UnityEngine;

namespace LastBell.Run
{
    // The music-tempo adapter for The Tempo Tyrant (no metronome ticks or tempo-change sound: removed October 24, 2026
    // at Risxhb's request; the song alone carries the pulse). Everything is derived from the encounter's
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
    public sealed class TempoTyrantAudio : MonoBehaviour
    {
        public enum MusicStatus { MissingMusic, MissingSlowVersion, Paired }
        // Haste = the 1.33x version (220 BPM grid); Normal = the song as written (warped onto an exact 165 BPM grid)
        public const string HasteTrack = "By My Measure (Haste)", SlowTrack = "By My Measure";
        // measured grids of the prepared files (warp_wsola.py output): first beat offset and BPM
        public const float HasteGridBpm = 220f, HasteFirstBeat = 0.1052f, SlowGridBpm = 165f, SlowFirstBeat = 0.1403f;
        public const float ResyncTolerance = 0.05f;

        TempoTyrantEncounter boss;
        bool stopped;
        AudioClip musicFast, musicSlow;
        bool aligned, pausedByUs; double beatOffset;
        public MusicStatus Music { get; private set; }
        public int PauseStops { get; private set; }
        public int MusicSwaps { get; private set; }
        public int MusicResyncs { get; private set; }
        public float LastMusicError { get; private set; }   // seconds, playing position minus clock-derived position
        public bool MusicLocked => aligned;
        public string StatusText => Music == MusicStatus.Paired ? $"music: By My Measure {(boss && boss.Clock.State == Tempo.Haste ? "1.33x" : "1.0x")} {(aligned ? "locked" : "waiting")} · err {LastMusicError * 1000:0} ms · swaps {MusicSwaps}" :
            Music == MusicStatus.MissingSlowVersion ? "music: slow version missing (no pitch-preserving stretch; stays 1.0x)" : "music: MISSING";

        public void Bind(TempoTyrantEncounter owner)
        {
            boss = owner;
            musicFast = Resources.Load<AudioClip>("Menu/BossMusic/" + HasteTrack);
            musicSlow = Resources.Load<AudioClip>("Menu/BossMusic/" + SlowTrack);
            Music = !musicFast ? MusicStatus.MissingMusic : !musicSlow ? MusicStatus.MissingSlowVersion : MusicStatus.Paired;
        }

        public void StopAll() { stopped = true; }

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

        void Update()
        {
            if (stopped) return;
            var d = RunDirector.Instance;
            bool fighting = boss && !boss.Dead && d != null && d.State == RunState.Combat && d.World != null && d.World.CombatActive;
            if (fighting) { if (d.Paused && !pausedByUs) PauseStops++; SyncMusic(d.Paused); }
        }
    }
}
