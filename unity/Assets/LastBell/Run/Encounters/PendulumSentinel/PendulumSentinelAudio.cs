using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // The Pendulum Sentinel's sound cues, generated in code (no audio assets). The fight shows no text, so each mechanic
    // has its own voice:
    //   Tick         a soft wooden tock on every beat (the pendulum inside him)
    //   Ratchet      Shortening Swing's tell: a chain cranked tight; Swing a heavy whoosh on each arc's hit
    //   Whirl        the three weights spun up; Thud a weight landing; Catch a bright bell when someone takes it;
    //                Shatter the brass burst of a missed weight
    //   Stop/Level   Plumb Line: the pendulum stopping dead (a clank), then a pure held tone while the floor counts;
    //                Boom the floor's hit
    //   Creak/Rush   Return Stroke: the tower leaning in, the rush of the charge; Crash the edge impact
    //   Shift        the 50% transformation: a deep chord
    // Clips are built once and shared. Presentation only.
    public sealed class PendulumSentinelAudio : MonoBehaviour
    {
        const int Rate = 44100;
        static AudioClip tick, ratchet, swing, whirl, thud, catchBell, shatter, stop, level, boom, creak, rush, crash, shift;
        AudioSource src, held;
        public int Played { get; private set; }
        public readonly List<string> Log = new List<string>();
        public static float Volume = 0.55f;

        void Awake()
        {
            src = gameObject.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 0; src.volume = Volume;
            held = gameObject.AddComponent<AudioSource>(); held.playOnAwake = false; held.spatialBlend = 0; held.volume = Volume * 0.5f; held.loop = true;
            if (tick == null) Build();
        }

        static void Build()
        {
            tick = Make("SentinelTick", 0.08f, t => 0.4f * (Partial(t, 520f, 60f) + 0.6f * Noise(t) * Mathf.Exp(-t * 90f)));
            ratchet = Make("SentinelRatchet", 0.9f, t => 0.4f * Noise(t) * Mathf.Exp(-((t * 14f) % 1f) * 9f) * Mathf.Clamp01(t / 0.05f) * (1 - Mathf.Clamp01((t - 0.8f) / 0.1f)));
            swing = Make("SentinelSwing", 0.6f, t => 0.5f * Noise(t) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.6f)) * (0.4f + 0.6f * Mathf.Sin(2 * Mathf.PI * (8f + 30f * t) * t)) + 0.3f * Partial(t - 0.2f, 70f, 6f));
            whirl = Make("SentinelWhirl", 1.3f, t => 0.3f * Mathf.Clamp01(t / 1f) * Mathf.Sin(2 * Mathf.PI * (60f + 45f * t) * t) * (0.6f + 0.4f * Mathf.Sin(2 * Mathf.PI * 7f * t)) * (1 - Mathf.Clamp01((t - 1.2f) / 0.1f)));
            thud = Make("SentinelThud", 0.8f, t => 0.6f * (Partial(t, 58f, 5f) + 0.5f * Partial(t, 87f, 7f) + 0.4f * Noise(t) * Mathf.Exp(-t * 25f)));
            catchBell = Make("SentinelCatch", 1.0f, t => 0.32f * (Partial(t, 880f, 4f) + 0.5f * Partial(t, 1320f, 5f) + 0.3f * Partial(t, 1760f, 7f)));
            shatter = Make("SentinelShatter", 0.9f, t => 0.45f * (Noise(t) * Mathf.Exp(-t * 8f) + 0.5f * Partial(t, 1660f, 9f) + 0.4f * Partial(t, 2490f, 12f)));
            stop = Make("SentinelStop", 0.5f, t => 0.5f * (Partial(t, 330f, 14f) + 0.6f * Partial(t, 495f, 18f) + 0.5f * Noise(t) * Mathf.Exp(-t * 70f)));
            level = Make("SentinelLevel", 1.0f, t => 0.18f * (Mathf.Sin(2 * Mathf.PI * 1318.5f * t) + 0.3f * Mathf.Sin(2 * Mathf.PI * 2637f * t)));   // looped while the floor counts
            boom = Make("SentinelBoom", 1.4f, t => 0.65f * (Partial(t, 44f, 2.8f) + 0.6f * Partial(t, 66f, 3.6f) + 0.5f * Noise(t) * Mathf.Exp(-t * 10f)));
            creak = Make("SentinelCreak", 0.9f, t => 0.3f * Mathf.Sin(2 * Mathf.PI * (140f - 60f * t) * t + 3f * Mathf.Sin(2 * Mathf.PI * 23f * t)) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.9f)));
            rush = Make("SentinelRush", 0.7f, t => 0.45f * Noise(t) * Mathf.Clamp01(t / 0.15f) * Mathf.Exp(-t * 2.5f) * (0.5f + 0.5f * Mathf.Sin(2 * Mathf.PI * 13f * t)));
            crash = Make("SentinelCrash", 1.2f, t => 0.7f * (Partial(t, 52f, 3.5f) + 0.5f * Noise(t) * Mathf.Exp(-t * 6f) + 0.4f * Partial(t, 740f, 5f) + 0.3f * Partial(t, 1110f, 7f)));
            shift = Make("SentinelShift", 2.4f, t => 0.5f * (0.5f * Partial(t, 73.4f, 1.6f) + Partial(t, 110f, 2.4f) + 0.5f * Partial(t, 146.8f, 3.2f) + 0.35f * Partial(t, 220f, 4f)) / 2.2f);
        }

        static float Partial(float t, float hz, float decay) => t < 0 ? 0 : Mathf.Sin(2 * Mathf.PI * hz * t) * Mathf.Exp(-t * decay);
        static uint noiseState = 4242;
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

        public void Tick() => Play(tick, "tick", 0.3f);
        public void Ratchet() => Play(ratchet, "ratchet");
        public void Swing() => Play(swing, "swing");
        public void Whirl() => Play(whirl, "whirl");
        public void Thud() => Play(thud, "thud");
        public void Catch() => Play(catchBell, "catch");
        public void Shatter() => Play(shatter, "shatter");
        public void Stop() => Play(stop, "stop");
        public void Boom() => Play(boom, "boom");
        public void Creak() => Play(creak, "creak");
        public void Rush() => Play(rush, "rush");
        public void Crash() => Play(crash, "crash");
        public void Shift() => Play(shift, "shift");
        public void Level(bool on)
        {
            if (!held) return;
            if (on) { held.clip = level; held.Play(); Played++; if (Log.Count < 4000) Log.Add("level"); }
            else held.Stop();
        }
        public void StopAll() { if (src) src.Stop(); if (held) held.Stop(); }
    }
}
