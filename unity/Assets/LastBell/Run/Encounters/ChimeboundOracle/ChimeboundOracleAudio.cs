using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // The Oracle's sound cues, generated in code (no audio assets): the fight teaches through sight and sound, never
    // text, so each mechanic has its own voice.
    //   Lane chime      one bell pitch per lane, low on the left and high on the right, one beat before the lane strikes.
    //   Chain chime     the soak circles ring a rising scale as each one lights, so the ear tracks progress.
    //   Soak ding       a bright double chime when a circle is soaked; a dull clang when one goes off unsoaked.
    //   Well hum        a low swell under the cracking circles, then a burst.
    //   Mark tick       a dry tick on each beat of the mark, doubling in its last beats.
    //   Clapper whoosh  a rising noise sweep on the cone windup.
    // Clips are built once and shared. Presentation only.
    public sealed class ChimeboundOracleAudio : MonoBehaviour
    {
        const int Rate = 44100;
        // a pentatonic run over two octaves: lane 0 (left) lowest, lane 7 (right) highest
        static readonly float[] LaneHz = { 392.00f, 440.00f, 523.25f, 587.33f, 659.25f, 783.99f, 880.00f, 1046.50f };
        static readonly float[] ChainHz = { 523.25f, 587.33f, 659.25f, 783.99f, 880.00f, 1046.50f, 1174.66f };
        static AudioClip[] laneClips, chainClips;
        static AudioClip soaked, missed, hum, burst, tick, whoosh, toll;
        AudioSource src;
        public int Played { get; private set; }
        public readonly List<string> Log = new List<string>();   // diagnostics: what played, in order
        public static float Volume = 0.55f;

        void Awake()
        {
            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false; src.spatialBlend = 0; src.volume = Volume;
            if (laneClips == null) BuildClips();
        }

        static void BuildClips()
        {
            laneClips = new AudioClip[LaneHz.Length];
            for (int i = 0; i < LaneHz.Length; i++) laneClips[i] = Bell("OracleLane" + i, LaneHz[i], 1.1f, 0.5f);
            chainClips = new AudioClip[ChainHz.Length];
            for (int i = 0; i < ChainHz.Length; i++) chainClips[i] = Bell("OracleChain" + i, ChainHz[i], 0.9f, 0.45f);
            soaked = Make("OracleSoaked", 0.5f, t => 0.35f * (Partial(t, 1568f, 6f) + (t > 0.09f ? Partial(t - 0.09f, 2093f, 7f) : 0)));
            missed = Make("OracleMissed", 0.9f, t => 0.5f * (Partial(t, 110f, 3.5f) + 0.6f * Partial(t, 173f, 4f) + 0.3f * Noise(t) * Mathf.Exp(-t * 18f)));
            hum = Make("OracleHum", 1.4f, t => 0.32f * Mathf.Clamp01(t / 1.3f) * Mathf.Sin(2 * Mathf.PI * (70f + 25f * t) * t) * (1 - Mathf.Clamp01((t - 1.32f) / 0.08f)));
            burst = Make("OracleBurst", 0.6f, t => 0.5f * (Noise(t) * Mathf.Exp(-t * 9f) + Partial(t, 98f, 5f)));
            tick = Make("OracleTick", 0.06f, t => 0.4f * Noise(t) * Mathf.Exp(-t * 90f));
            whoosh = Make("OracleWhoosh", 0.7f, t => 0.35f * Noise(t) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.7f)) * (0.4f + 0.6f * t / 0.7f));
            toll = Bell("OracleToll", 196f, 2.4f, 0.8f);
        }

        // struck-bell partials (hum, prime, tierce, quint, nominal) with exponential decay
        static AudioClip Bell(string name, float hz, float length, float gain) => Make(name, length, t =>
            gain * (0.5f * Partial(t, hz * 0.5f, 1.8f) + Partial(t, hz, 3f) + 0.55f * Partial(t, hz * 1.19f, 4f) + 0.4f * Partial(t, hz * 1.5f, 5f) + 0.3f * Partial(t, hz * 2f, 6f)) / 2.75f
            * Mathf.Clamp01(t / 0.004f));
        static float Partial(float t, float hz, float decay) => t < 0 ? 0 : Mathf.Sin(2 * Mathf.PI * hz * t) * Mathf.Exp(-t * decay);
        static uint noiseState = 22222;
        static float Noise(float t) { noiseState ^= noiseState << 13; noiseState ^= noiseState >> 17; noiseState ^= noiseState << 5; return (noiseState / (float)uint.MaxValue) * 2 - 1; }

        static AudioClip Make(string name, float seconds, System.Func<float, float> f)
        {
            int n = Mathf.CeilToInt(seconds * Rate); var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)Rate), -1, 1);
            var c = AudioClip.Create(name, n, 1, Rate, false); c.SetData(data, 0); return c;
        }

        void Play(AudioClip c, string what, float vol = 1)
        {
            if (!c || !src) return;
            src.PlayOneShot(c, vol); Played++;
            if (Log.Count < 4000) Log.Add(what);
        }

        public void Lane(int lane) => Play(laneClips[Mathf.Clamp(lane, 0, laneClips.Length - 1)], "lane " + lane, 0.8f);
        public void Chain(int index) => Play(chainClips[Mathf.Clamp(index, 0, chainClips.Length - 1)], "chain " + index);
        public void Soaked() => Play(soaked, "soaked");
        public void Missed() => Play(missed, "missed");
        public void Hum() => Play(hum, "hum", 0.9f);
        public void Burst() => Play(burst, "burst", 0.8f);
        public void Tick() => Play(tick, "tick", 0.7f);
        public void Whoosh() => Play(whoosh, "whoosh");
        public void Toll() => Play(toll, "toll");
        public void StopAll() { if (src) src.Stop(); }
    }
}
