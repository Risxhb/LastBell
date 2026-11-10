using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Clip timing for a rigged boss (miniboss models, 9 Nov 2026). Presentation only: it never changes gameplay.
    // Strike() starts a one-shot early, at its natural speed, so its strike frame lands on a gameplay tick (a resolve,
    // a pop, a lock); if that tick is too close, the clip starts partway in. LoopAt() changes the held loop at a tick.
    // Step() runs once per combat tick from the boss's StepAlways (host only; LAN clients get the clip from the host).
    public sealed class BossClipCues
    {
        readonly CharacterPresenter presenter;
        readonly List<(int at, string clip, int ticks, string loop, float speed)> queue = new List<(int, string, int, string, float)>();
        int now;
        public string Loop = "Idle";
        public float LoopSpeed = 1;
        public string LastStrike { get; private set; } = "";   // tests and the F7 overlay

        public BossClipCues(CharacterPresenter presenter) { this.presenter = presenter; }

        // play `clip` (lenSec long, strike frame at strikeSec) so the strike frame is `inTicks` from now
        public void Strike(string clip, int inTicks, float strikeSec, float lenSec)
        {
            int len = Mathf.Max(1, Mathf.RoundToInt(lenSec * Sim.Hz)), start = inTicks - Mathf.RoundToInt(strikeSec * Sim.Hz);
            if (start > 0) queue.Add((now + start, clip, len, null, 1));
            else Play(clip, len, -start);
        }
        // play `clip` now over `ticks` (one cycle for a loop clip), optionally skipping its first `skip` ticks
        public void Play(string clip, int ticks, int skip = 0)
        {
            if (!presenter || skip >= ticks) return;
            presenter.Action(clip, ticks - skip, skip / (float)ticks);
            LastStrike = clip;
        }
        // one-shot `clip` over `ticks`, `inTicks` from now
        public void At(int inTicks, string clip, int ticks) { if (inTicks <= 0) Play(clip, ticks); else queue.Add((now + inTicks, clip, ticks, null, 1)); }
        // the held loop from `inTicks` on (it shows whenever no one-shot is playing)
        public void LoopAt(int inTicks, string loop, float speed = 1)
        {
            if (inTicks <= 0) { Loop = loop; LoopSpeed = speed; }
            else queue.Add((now + inTicks, null, 0, loop, speed));
        }
        public void Clear() { queue.Clear(); }
        public int Pending => queue.Count;

        public void Step(bool walking)
        {
            now++;
            for (int i = 0; i < queue.Count; i++)
            {
                var c = queue[i];
                if (c.at > now) continue;
                if (c.clip != null) Play(c.clip, c.ticks);
                else { Loop = c.loop; LoopSpeed = c.speed; }
                queue.RemoveAt(i--);
            }
            if (presenter) presenter.TickBoss(walking ? "Walk" : Loop, walking ? 1 : LoopSpeed);
        }
    }
}
