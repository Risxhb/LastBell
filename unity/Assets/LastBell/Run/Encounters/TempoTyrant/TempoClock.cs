using UnityEngine;

namespace LastBell.Run
{
    public enum Tempo { Haste, Normal }

    // The Tempo Tyrant's single authoritative tempo state and musical phase. Advances only on the 60 Hz combat tick
    // (never Time.timeScale), so players, inputs and every other system keep normal speed. The metronome rod, tick
    // audio, HUD label, boss emission and music rate are all derived from this object; nothing else decides tempo.
    //
    // Musical phase is counted in beats and is continuous across tempo changes. One beat = one metronome swing from
    // one extreme to the other = one tick. A duration expiring only *requests* a change (Pending); the encounter
    // starts the three accented transition ticks at its next complete attack-block boundary and the change commits
    // on the third tick (see TempoTyrantEncounter.TempoTransition). Nothing retimes an attack already in progress.
    public sealed class TempoClock
    {
        // Tunable first-pass values (docs/metronome-arena/tempo-tyrant.md). Damage never depends on tempo.
        public float HasteBpm = 160f, NormalBpm = 120f;
        public int HasteTicks = 12 * Sim.Hz, NormalTicks = 16 * Sim.Hz;
        public const int TransitionBeats = 3;

        public Tempo State { get; private set; } = Tempo.Haste;
        public double Beat { get; private set; }            // musical phase (beats since combat start)
        public int StateAge { get; private set; }            // combat ticks in the current tempo
        public int Commits { get; private set; }             // number of committed tempo changes
        public bool Transitioning { get; private set; }      // inside the three accented ticks
        public int TransitionTick { get; private set; }      // 0..3 accented ticks heard so far
        public int LastBeatIndex { get; private set; } = -1; // integer beat crossed on the last Step (or -1)
        public bool LastBeatAccented { get; private set; }
        public readonly System.Collections.Generic.List<(int tick, Tempo to, int stateTicks)> History = new System.Collections.Generic.List<(int, Tempo, int)>();

        public float Bpm => State == Tempo.Haste ? HasteBpm : NormalBpm;
        public float BeatSeconds => 60f / Bpm;
        public int Duration => State == Tempo.Haste ? HasteTicks : NormalTicks;
        public bool Pending => StateAge >= Duration;          // duration expired: change at the next safe boundary
        public float MusicRate => Bpm / HasteBpm;             // 1.0 in Haste, 0.75 outside it
        public Tempo Next => State == Tempo.Haste ? Tempo.Normal : Tempo.Haste;

        public void Reset()
        {
            State = Tempo.Haste; Beat = 0; StateAge = 0; Commits = 0; Transitioning = false; TransitionTick = 0;
            LastBeatIndex = -1; LastBeatAccented = false; History.Clear();
        }

        // One combat tick. Returns true when an integer beat (a metronome tick) was crossed on this step.
        public bool Step(int combatTick)
        {
            StateAge++;
            double before = Beat;
            Beat += Bpm / 60.0 / Sim.Hz;
            LastBeatIndex = -1; LastBeatAccented = false;
            if (System.Math.Floor(Beat) > System.Math.Floor(before))
            {
                LastBeatIndex = (int)System.Math.Floor(Beat);
                if (Transitioning)
                {
                    TransitionTick++; LastBeatAccented = true;
                    if (TransitionTick >= TransitionBeats) Commit(combatTick);
                }
                return true;
            }
            return false;
        }

        // Called by the encounter at a complete attack-block boundary when Pending.
        public void BeginTransition() { if (!Transitioning) { Transitioning = true; TransitionTick = 0; } }

        void Commit(int combatTick)
        {
            History.Add((combatTick, Next, StateAge));
            State = Next; StateAge = 0; Commits++; Transitioning = false;
            // the third accented tick is beat-aligned, so the new tempo starts exactly on a swing extreme
            Beat = System.Math.Round(Beat);
        }

        // Combat ticks for a span of beats at the current tempo (160 BPM: 4 beats = 90 ticks; 120 BPM: 4 beats = 120).
        public int TicksFor(float beats) => Mathf.Max(1, Mathf.RoundToInt(beats * 60f / Bpm * Sim.Hz));
        public static int TicksFor(float beats, float bpm) => Mathf.Max(1, Mathf.RoundToInt(beats * 60f / bpm * Sim.Hz));

        // Ticks until the next integer beat (0 when this tick landed on one).
        public int TicksToNextBeat()
        {
            double frac = Beat - System.Math.Floor(Beat);
            if (frac < 1e-6) return 0;
            return Mathf.CeilToInt((float)((1 - frac) * 60.0 / Bpm * Sim.Hz) - 1e-4f);
        }

        // Rod angle in [-1, 1]: an extreme on every integer beat, swinging through centre halfway between.
        public float RodPhase => Mathf.Cos((float)(Beat % 2.0) * Mathf.PI);
    }
}
