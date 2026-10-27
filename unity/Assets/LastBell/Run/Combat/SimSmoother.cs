using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Render interpolation (October 26, 2026). The world steps at a fixed 60 Hz while frames render at 120 fps or more,
    // so a sim-driven transform used to move on one frame and hold on the next; the follow camera stuttered with it and
    // the Metronome Court floor's fine lines read as heavy shimmer. Each frame RunDirector:
    //   RestoreAll()  puts every smoothed transform back on its latest sim pose, so the ticks see exact sim state;
    //   CaptureAll()  after each tick records the previous and the new sim pose;
    //   PresentAll(a) draws the transform a fraction a (0..1) of the way from the previous to the latest pose.
    // Gameplay is unchanged: hit tests run inside ticks on sim poses. A pose moved from outside the ticks (a test
    // placing the player, a respawn) is taken as the new sim pose, and a jump over TeleportDistance snaps.
    [DisallowMultipleComponent]
    public sealed class SimSmoother : MonoBehaviour
    {
        const float TeleportDistance = 2f;   // metres in one tick: faster than any dash, so it must be a teleport
        static readonly List<SimSmoother> all = new List<SimSmoother>();
        Vector3 prevP, curP, shownP; Quaternion prevR, curR, shownR; bool init;

        public static void Attach(GameObject go) { if (!go.GetComponent<SimSmoother>()) go.AddComponent<SimSmoother>(); }
        // The latest sim pose (not the drawn one). Per-frame checks of gameplay state outside the ticks read these.
        public static Vector3 SimPosition(Transform t) { var s = t ? t.GetComponent<SimSmoother>() : null; return s && s.init && s.enabled ? s.curP : t.position; }
        public static Quaternion SimRotation(Transform t) { var s = t ? t.GetComponent<SimSmoother>() : null; return s && s.init && s.enabled ? s.curR : t.rotation; }
        void OnEnable() { all.Add(this); init = false; }
        void OnDisable() { all.Remove(this); }

        void Sync() { transform.GetPositionAndRotation(out curP, out curR); prevP = shownP = curP; prevR = shownR = curR; init = true; }

        public static void RestoreAll()
        {
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var s = all[i]; if (!s) { all.RemoveAt(i); continue; }
                s.transform.GetPositionAndRotation(out var p, out var r);
                bool external = !s.init || (p - s.shownP).sqrMagnitude > 1e-8f || Quaternion.Angle(r, s.shownR) > 0.01f;
                if (external) s.Sync(); else s.transform.SetPositionAndRotation(s.curP, s.curR);
            }
        }
        public static void CaptureAll()
        {
            foreach (var s in all)
            {
                if (!s) continue;
                s.prevP = s.curP; s.prevR = s.curR;
                s.transform.GetPositionAndRotation(out s.curP, out s.curR);
                if ((s.curP - s.prevP).sqrMagnitude > TeleportDistance * TeleportDistance) { s.prevP = s.curP; s.prevR = s.curR; }
            }
        }
        // -lastbellNoSmoothing turns it off (draws the latest sim pose), for before/after measurements
        public static readonly bool Enabled = !System.Array.Exists(System.Environment.GetCommandLineArgs(), a => a.Equals("-lastbellNoSmoothing", System.StringComparison.OrdinalIgnoreCase));
        public static void PresentAll(float alpha)
        {
            alpha = Enabled ? Mathf.Clamp01(alpha) : 1f;
            foreach (var s in all)
            {
                if (!s || !s.init) continue;
                s.shownP = Vector3.LerpUnclamped(s.prevP, s.curP, alpha);
                s.shownR = Quaternion.Slerp(s.prevR, s.curR, alpha);
                s.transform.SetPositionAndRotation(s.shownP, s.shownR);
            }
        }
    }
}
