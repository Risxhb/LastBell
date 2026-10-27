using UnityEngine;

namespace LastBell.Menu
{
    // Menu-only motion for the baked Metronome Court showcase stage. The arena's own builder (which reads the Tempo
    // Tyrant's clock and drives scene lighting) is removed from the menu copy, so this just swings the rod and steps the
    // escapement at a calm tempo. No clocks, audio, lighting or gameplay state.
    public sealed class MenuMetronomeSwing : MonoBehaviour
    {
        public float BeatsPerMinute = 110;   // half the arena's opening Haste tempo: reads as a metronome without flicking
        public float SwingDegrees = 26;      // MetronomeCourtArena.SwingDegrees
        Transform rod, escapement, pawl;
        float beats, escapementAngle, escapementShown, pawlKick;
        int lastBeat;
        public float RodAngle { get; private set; }

        void Awake()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (!rod && t.name == "Rod") rod = t;
                else if (!escapement && t.name == "EscapementWheel") escapement = t;
                else if (!pawl && t.name == "Pawl") pawl = t;
            }
        }

        void Update()
        {
            beats += Time.deltaTime * BeatsPerMinute / 60f;
            RodAngle = Mathf.Cos(beats % 2f * Mathf.PI) * SwingDegrees;
            if (rod) rod.localRotation = Quaternion.Euler(0, 0, RodAngle);
            int beat = Mathf.FloorToInt(beats);
            if (beat != lastBeat) { lastBeat = beat; escapementAngle -= 24; pawlKick = 1; }
            escapementShown = Mathf.MoveTowards(escapementShown, escapementAngle, Time.deltaTime * 600);
            if (escapement) escapement.localRotation = Quaternion.Euler(0, 0, escapementShown);
            pawlKick = Mathf.MoveTowards(pawlKick, 0, Time.deltaTime * 8);
            if (pawl) pawl.localRotation = Quaternion.Euler(0, 0, 14 * pawlKick);
        }
    }
}
