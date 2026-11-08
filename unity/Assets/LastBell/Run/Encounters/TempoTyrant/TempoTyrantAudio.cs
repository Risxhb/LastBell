using UnityEngine;

namespace LastBell.Run
{
    // The music-tempo adapter for The Tempo Tyrant (no metronome ticks or tempo-change sound: removed October 24, 2026
    // at Risxhb's request; the song alone carries the pulse). Everything is derived from the encounter's
    // TempoClock; this component never decides tempo, and it only ever touches this fight's own track.
    //
    // Music: "By My Measure" (Risxhb, October 23, 2026), prepared offline (tools/tempo-tyrant: beat_track.py +
    // warp_wsola.py) into two pitch-preserved, beat-aligned versions on exact grids with the same beat count:
    // "By My Measure (Haste)" at 220 BPM (1.33x) and "By My Measure" at 165 BPM (the song as written). MenuMusic starts
    // the Haste version for the countdown as for any boss. This adapter then:
    //   - at combat start, aligns the music's beat phase to the clock (a sub-half-beat move), fixing a beat offset K;
    //   - on every tempo commit, switches to the other version at the same beat index (K + clock beat);
    //   - corrects real drift (the game clock loses time on frames over 0.1 s) when the smoothed error stays over
    //     70 ms, never in the 0.3 s after a switch;
    //   - pauses the track with the game pause and lines it up again on resume.
    // Every switch or correction is an 80 ms equal-power crossfade: a tail source plays on from the old position and
    // fades out while the music source fades in at the new one (MenuMusic.BossFade). Both versions load into memory
    // (not streamed), so a jump is instant and sample-exact.
    //
    // November 3, 2026: replaced hard cuts and a raw 50 ms resync. The old version cut instantly between versions,
    // the streamed clip then stalled ~25 ms, and the 50 ms check (compared against a position Unity reports in ~21 ms
    // steps) jumped again: 11 of 17 tempo changes were a double stutter, and 40 of 73 jumps in a test fight happened
    // on normal frames. A raw AudioSource.pitch change is still never used (it changes the song's pitch); Unity's
    // pitch-shifter mixer effect is unavailable on WebGL, so prepared versions are the portable path.
    public sealed class TempoTyrantAudio : MonoBehaviour
    {
        public enum MusicStatus { MissingMusic, MissingSlowVersion, Paired }
        // Haste = the 1.33x version (220 BPM grid); Normal = the song as written (warped onto an exact 165 BPM grid)
        public const string HasteTrack = "By My Measure (Haste)", SlowTrack = "By My Measure";
        // measured grids of the prepared files (warp_wsola.py output): first beat offset and BPM
        public const float HasteGridBpm = 220f, HasteFirstBeat = 0.1052f, SlowGridBpm = 165f, SlowFirstBeat = 0.1403f;
        // "locked": the smoothed error is within this (tests). Corrections happen only past CorrectAfter.
        public const float ResyncTolerance = 0.05f;
        public const float CorrectAfter = 0.07f, Settle = 0.3f, FadeSeconds = 0.08f, Smoothing = 0.25f;

        TempoTyrantEncounter boss;
        bool stopped;
        AudioClip musicFast, musicSlow;
        bool aligned, pausedByUs; double beatOffset;
        bool fadeDirty; float lastExpected = -1; AudioSource tail; float fade = 1, quietUntil, smoothErr, lastLongFrame = -10;
        public MusicStatus Music { get; private set; }
        public int PauseStops { get; private set; }
        public int MusicSwaps { get; private set; }
        public int MusicResyncs { get; private set; }        // drift corrections (not tempo switches, alignment or resume)
        public int CalmResyncs { get; private set; }         // corrections with no frame over 0.1 s in the last second (should stay ~0)
        public int MusicLoops { get; private set; }          // crossfaded wraps at the end of the beat grid
        public int LongFrames { get; private set; }          // frames over 0.1 s while fighting (the game clock loses time on these)
        public float LastMusicError { get; private set; }   // seconds, smoothed playing position minus clock-derived position
        public bool MusicLocked => aligned;
        public string StatusText => Music == MusicStatus.Paired ? $"music: By My Measure {(boss && boss.Clock.State == Tempo.Haste ? "1.33x" : "1.0x")} {(aligned ? "locked" : "waiting")} · err {LastMusicError * 1000:0} ms · swaps {MusicSwaps} · corrections {MusicResyncs} ({CalmResyncs} calm, {LongFrames} long frames) · loops {MusicLoops}" :
            Music == MusicStatus.MissingSlowVersion ? "music: slow version missing (no pitch-preserving stretch; stays 1.0x)" : "music: MISSING";

        public void Bind(TempoTyrantEncounter owner)
        {
            boss = owner;
            musicFast = Resources.Load<AudioClip>("Menu/BossMusic/" + HasteTrack);
            musicSlow = Resources.Load<AudioClip>("Menu/BossMusic/" + SlowTrack);
            Music = !musicFast ? MusicStatus.MissingMusic : !musicSlow ? MusicStatus.MissingSlowVersion : MusicStatus.Paired;
            if (Music == MusicStatus.Paired) { musicFast.LoadAudioData(); musicSlow.LoadAudioData(); }
        }

        public void StopAll() { stopped = true; EndFade(); }

        // ---------------- music ----------------
        static LastBell.Menu.MenuMusic Player => LastBell.Menu.MenuMusic.Ensure();
        AudioSource OurSource()
        {
            if (Music != MusicStatus.Paired) return null;
            var src = Player.Source;
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
        static float Position(AudioSource src) => src.clip ? src.timeSamples / (float)src.clip.frequency : 0;
        float RawError(AudioSource src)
        {
            float err = Position(src) - ExpectedTime(src.clip), len = src.clip.length;
            if (err > len / 2) err -= len; else if (err < -len / 2) err += len;
            return err;
        }

        void SyncMusic(bool paused)
        {
            var src = OurSource(); if (!src) return;
            if (paused) { if (src.isPlaying) { src.Pause(); pausedByUs = true; } EndFade(); return; }
            if (pausedByUs) { src.UnPause(); pausedByUs = false; CrossTo(src, src.clip, ExpectedTime(src.clip)); }
            if (!aligned)
            {
                // fix the beat offset K so the nearest music beat sits on the clock's current phase
                double musicBeat = (Position(src) - First(src.clip, musicFast)) / Period(src.clip, musicFast);
                beatOffset = System.Math.Round(musicBeat - boss.Clock.Beat);
                aligned = true; CrossTo(src, src.clip, ExpectedTime(src.clip));
            }
            var want = boss.Clock.State == Tempo.Haste ? musicFast : musicSlow;
            if (src.clip != want) { CrossTo(src, want, ExpectedTime(want)); MusicSwaps++; }
            Track(src);
        }

        // Follows drift with a smoothed error (frame order and the ~21 ms position steps add jitter that isn't drift);
        // corrects only a sustained offset, and never right after a switch.
        void Track(AudioSource src)
        {
            // The beat grid ends on the clip's last whole beat, a little before the file ends: when the expected position
            // wraps to the start, follow it with a crossfade on that beat instead of letting the file loop on its own.
            float expected = ExpectedTime(src.clip);
            if (lastExpected >= 0 && expected < lastExpected - src.clip.length / 2) { MusicLoops++; CrossTo(src, src.clip, expected); return; }
            lastExpected = expected;
            float raw = RawError(src);
            if (Time.unscaledTime < quietUntil) { smoothErr = raw; LastMusicError = raw; return; }
            smoothErr += (raw - smoothErr) * (1 - Mathf.Exp(-Time.unscaledDeltaTime / Smoothing));
            LastMusicError = smoothErr;
            if (Mathf.Abs(smoothErr) > CorrectAfter)
            {
                MusicResyncs++; if (Time.unscaledTime - lastLongFrame > 1) CalmResyncs++;
                CrossTo(src, src.clip, ExpectedTime(src.clip));
            }
        }

        // Moves the music to `clip` at `seconds` with an equal-power crossfade from where it was.
        void CrossTo(AudioSource src, AudioClip clip, float seconds)
        {
            if (!tail)
            {
                tail = Player.gameObject.AddComponent<AudioSource>();   // on the music player, never on the boss
                tail.playOnAwake = false; tail.loop = false; tail.spatialBlend = 0; tail.dopplerLevel = 0; tail.priority = src.priority;
                tail.outputAudioMixerGroup = src.outputAudioMixerGroup; tail.ignoreListenerPause = src.ignoreListenerPause;
            }
            if (src.isPlaying && src.clip)
            {
                tail.Stop(); tail.clip = src.clip; tail.timeSamples = src.timeSamples; tail.volume = Player.BossLevel; tail.Play();
            }
            if (src.clip != clip) src.clip = clip;
            src.timeSamples = Mathf.Clamp(Mathf.RoundToInt(seconds * clip.frequency), 0, clip.samples - 1);
            if (!src.isPlaying) src.Play();
            lastExpected = seconds; fade = 0; fadeDirty = true; Player.BossFade = 0; quietUntil = Time.unscaledTime + Settle; smoothErr = 0; LastMusicError = 0;
        }
        void StepFade()
        {
            if (fade >= 1) return;
            fade = Mathf.Min(1, fade + Time.unscaledDeltaTime / FadeSeconds);
            Player.BossFade = Mathf.Sin(fade * Mathf.PI / 2);
            if (tail) tail.volume = Player.BossLevel * Mathf.Cos(fade * Mathf.PI / 2);
            if (fade >= 1) EndFade();
        }
        void EndFade()
        {
            if (fade >= 1 && (!tail || !tail.isPlaying) && !fadeDirty) return;
            fadeDirty = false; fade = 1; if (tail) tail.Stop();
            var music = FindAnyObjectByType<LastBell.Menu.MenuMusic>(); if (music) music.BossFade = 1;   // never create one here
        }

        void OnDestroy()
        {
            var music = Music == MusicStatus.Paired ? FindAnyObjectByType<LastBell.Menu.MenuMusic>() : null;   // never create one while tearing down
            if (music) music.BossFade = 1;
            if (tail) Destroy(tail);
            var src = music ? music.Source : null;
            if (pausedByUs && src) src.UnPause();
        }

        void Update()
        {
            if (stopped) return;
            var d = RunDirector.Instance;
            bool fighting = boss && !boss.Dead && d != null && d.State == RunState.Combat && d.World != null && (d.World.CombatActive || RunHud.LanClientView);   // a LAN client's replica world never sets CombatActive
            if (!fighting) { if (fade < 1) EndFade(); return; }
            if (!d.Paused && Time.unscaledDeltaTime > 0.1f) { LongFrames++; lastLongFrame = Time.unscaledTime; }
            if (d.Paused && !pausedByUs) PauseStops++;
            SyncMusic(d.Paused);
            if (!d.Paused) StepFade();
        }
    }
}
