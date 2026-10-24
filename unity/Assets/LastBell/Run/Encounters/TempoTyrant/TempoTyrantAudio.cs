using UnityEngine;

namespace LastBell.Run
{
    // Metronome ticks and the music-tempo adapter for The Tempo Tyrant. Everything is derived from the encounter's
    // TempoClock; this component never decides tempo.
    //
    // Ticks: one per beat (one rod swing), scheduled on the audio DSP clock a short lookahead before the beat, from the
    // clock's current musical phase. Each schedule is recomputed from the authoritative phase, so there is no
    // accumulated drift; pause stops both voices (cancelling anything queued) and nothing is replayed on resume.
    // Haste ticks are higher and sharper, Normal ticks deeper; the three transition ticks are accented.
    //
    // Music: Unity's AudioSource.pitch changes pitch with speed, and the pitch-shifter mixer effect is unavailable on
    // WebGL, so a raw pitch change is NOT used. The supported path is two externally prepared, sample-aligned versions of
    // the same track: Resources/Menu/BossMusic/"The Tempo Tyrant" (160 BPM, Haste) and "The Tempo Tyrant (120)"
    // (same music at 75% speed, pitch preserved). On each tempo commit the playing MenuMusic boss source swaps clips at
    // the musically equivalent position. With no supplied track the status is MissingMusic and only ticks play.
    public sealed class TempoTyrantAudio : MonoBehaviour
    {
        public enum MusicStatus { MissingMusic, MissingSlowVersion, Paired }
        public const string HasteTrack = "The Tempo Tyrant", SlowTrack = "The Tempo Tyrant (120)";
        // measured per supplied track (beat grid / loop / encoder delay); placeholders until a track exists
        public static float TrackBpm = 160f, DownbeatOffsetSeconds = 0f;

        static AudioClip hasteTick, normalTick, accentTick;
        TempoTyrantEncounter boss;
        AudioSource a, b; bool flip;
        int scheduledBeat = -1;
        int seenCommits;
        AudioClip musicFast, musicSlow;
        public MusicStatus Music { get; private set; }
        public int TicksScheduled { get; private set; }
        public int PauseStops { get; private set; }
        public int MusicSwaps { get; private set; }
        public string StatusText => Music == MusicStatus.Paired ? "music: paired 160/120 versions" :
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

        void Update()
        {
            var d = RunDirector.Instance;
            if (!boss || boss.Dead || d == null || d.State != RunState.Combat || d.Paused || d.World == null || !d.World.CombatActive)
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
                var src = flip ? a : b; flip = !flip;
                src.clip = accent ? accentTick : c.State == Tempo.Haste ? hasteTick : normalTick;
                src.volume = Volume * (accent ? 1f : 0.7f);
                src.PlayScheduled(AudioSettings.dspTime + Mathf.Max(0, until));
                TicksScheduled++;
            }
            if (c.Commits != seenCommits) { seenCommits = c.Commits; ApplyMusicTempo(c.State); }
        }

        // Swap to the musically equivalent position in the other prepared version (pitch preserved by preparation).
        void ApplyMusicTempo(Tempo t)
        {
            if (Music != MusicStatus.Paired) return;
            var music = LastBell.Menu.MenuMusic.Ensure(); var src = music.Source;
            if (!src || (src.clip != musicFast && src.clip != musicSlow)) return;
            var want = t == Tempo.Haste ? musicFast : musicSlow;
            if (src.clip == want) return;
            double frac = src.clip.length > 0 ? src.time / src.clip.length : 0;
            src.clip = want; src.time = (float)(frac * want.length); src.Play();
            MusicSwaps++;
        }

        void OnDisable() { if (a) a.Stop(); if (b) b.Stop(); }
    }
}
