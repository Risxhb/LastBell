using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // The Dial Warden's sound cues, generated in code (no audio assets). The fight shows no text, so each mechanic has
    // its own voice:
    //   Tick         a dry clock tick on every beat of the hand's lap (and softly through the whole fight)
    //   Star         a rising glass scale as each Constellation line is drawn; a bright zap as a segment fires
    //   Whirl/Thud   a low swelling hum as the weight whirls, a heavy thud on impact
    //   Balanced     a clean two-note chime when the weigh balances; Unbalanced a dull, detuned clang
    //   Clang        the hand caught: a bright ringing anvil strike; Crack the hand breaking through
    //   Slam/Rumble  the Mainspring impact and the wave's travelling rumble; Implode the centre burst
    //   Key/Bad      QTE feedback: a soft click per correct key, a buzz on a mistake
    // Clips are built once and shared. Presentation only.
    public sealed class DialWardenAudio : MonoBehaviour
    {
        const int Rate = 44100;
        static readonly float[] StarHz = { 783.99f, 880.00f, 987.77f, 1174.66f, 1318.51f, 1567.98f, 1760.00f, 1975.53f, 2349.32f, 2637.02f };
        static AudioClip[] stars;
        static AudioClip tick, zap, whirl, thud, balanced, unbalanced, clang, crack, slam, rumble, implode, key, bad, toll;
        AudioSource src;
        public int Played { get; private set; }
        public readonly List<string> Log = new List<string>();
        public static float Volume = 0.55f;

        void Awake()
        {
            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false; src.spatialBlend = 0; src.volume = Volume;
            if (stars == null) Build();
        }

        static void Build()
        {
            stars = new AudioClip[StarHz.Length];
            for (int i = 0; i < StarHz.Length; i++) { float hz = StarHz[i]; stars[i] = Make("WardenStar" + i, 0.7f, t => 0.3f * (Partial(t, hz, 5f) + 0.4f * Partial(t, hz * 2.01f, 8f)) * Mathf.Clamp01(t / 0.003f)); }
            tick = Make("WardenTick", 0.05f, t => 0.45f * Noise(t) * Mathf.Exp(-t * 110f) + 0.25f * Partial(t, 3200f, 90f));
            zap = Make("WardenZap", 0.35f, t => 0.35f * (Partial(t, 1400f - 1800f * t, 10f) + 0.5f * Noise(t) * Mathf.Exp(-t * 20f)));
            whirl = Make("WardenWhirl", 1.2f, t => 0.3f * Mathf.Clamp01(t / 0.9f) * Mathf.Sin(2 * Mathf.PI * (55f + 30f * t) * t) * (0.7f + 0.3f * Mathf.Sin(2 * Mathf.PI * 6f * t)) * (1 - Mathf.Clamp01((t - 1.12f) / 0.08f)));
            thud = Make("WardenThud", 0.8f, t => 0.6f * (Partial(t, 62f, 5f) + 0.5f * Partial(t, 93f, 7f) + 0.4f * Noise(t) * Mathf.Exp(-t * 25f)));
            balanced = Make("WardenBalanced", 0.9f, t => 0.32f * (Partial(t, 1046.5f, 4f) + (t > 0.12f ? Partial(t - 0.12f, 1568f, 4.5f) : 0)));
            unbalanced = Make("WardenUnbalanced", 1.0f, t => 0.45f * (Partial(t, 146.8f, 3f) + 0.8f * Partial(t, 155.6f, 3f) + 0.3f * Noise(t) * Mathf.Exp(-t * 14f)));
            clang = Make("WardenClang", 1.2f, t => 0.42f * (Partial(t, 1244f, 3.2f) + 0.6f * Partial(t, 1873f, 4f) + 0.4f * Partial(t, 2690f, 6f) + 0.5f * Noise(t) * Mathf.Exp(-t * 60f)));
            crack = Make("WardenCrack", 0.9f, t => 0.55f * (Noise(t) * Mathf.Exp(-t * 7f) + 0.6f * Partial(t, 80f, 4f)));
            slam = Make("WardenSlam", 1.0f, t => 0.65f * (Partial(t, 48f, 3.5f) + 0.6f * Partial(t, 72f, 4.5f) + 0.5f * Noise(t) * Mathf.Exp(-t * 12f)));
            rumble = Make("WardenRumble", 1.6f, t => 0.3f * Noise(t) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 1.6f)) * (0.5f + 0.5f * Mathf.Sin(2 * Mathf.PI * 9f * t)));
            implode = Make("WardenImplode", 0.9f, t => 0.55f * (Partial(t, 90f - 40f * t, 4f) + 0.5f * Noise(t) * Mathf.Exp(-t * 9f)));
            key = Make("WardenKey", 0.06f, t => 0.35f * Partial(t, 2200f, 70f));
            bad = Make("WardenBad", 0.3f, t => 0.3f * Mathf.Sign(Mathf.Sin(2 * Mathf.PI * 110f * t)) * Mathf.Exp(-t * 9f));
            toll = Make("WardenToll", 2.4f, t => 0.5f * (0.5f * Partial(t, 98f, 1.8f) + Partial(t, 196f, 2.6f) + 0.55f * Partial(t, 233f, 3.5f) + 0.4f * Partial(t, 294f, 4.5f)) / 2.4f);
        }

        static float Partial(float t, float hz, float decay) => t < 0 ? 0 : Mathf.Sin(2 * Mathf.PI * hz * t) * Mathf.Exp(-t * decay);
        static uint noiseState = 90210;
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

        public void Tick(bool loud) => Play(tick, loud ? "tick" : "tick soft", loud ? 0.9f : 0.25f);
        public void Star(int i) => Play(stars[Mathf.Clamp(i, 0, stars.Length - 1)], "star " + i);
        public void Zap() => Play(zap, "zap", 0.7f);
        public void Whirl() => Play(whirl, "whirl");
        public void Thud() => Play(thud, "thud");
        public void Balanced() => Play(balanced, "balanced");
        public void Unbalanced() => Play(unbalanced, "unbalanced");
        public void Clang() => Play(clang, "clang");
        public void Crack() => Play(crack, "crack");
        public void Slam() => Play(slam, "slam");
        public void Rumble() => Play(rumble, "rumble", 0.8f);
        public void Implode() => Play(implode, "implode");
        public void Key() => Play(key, "key", 0.8f);
        public void Bad() => Play(bad, "bad");
        public void Toll() => Play(toll, "toll");
        public void StopAll() { if (src) src.Stop(); }
    }
}
