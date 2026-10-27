using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastBell.Run
{
    // The parry quick-time event (first used by the Dial Warden's Arrest the Hand, docs/bosses/dial-warden.md).
    // A short run of directions, then the parry key timed to a moment:
    //   - directions are W A S D (or the arrow keys), in the order shown, at any pace while the prompt is open;
    //   - the last key is always the parry (E), and it only counts inside a short window around ArriveTick.
    // Any wrong key, a parry before the window, or nothing by the end of the window fails it. One try only.
    // Pure rules on the 60 Hz combat tick: no frame timing, no presentation.
    public enum QteKey { Up, Left, Down, Right, Parry }

    public sealed class ParryQte
    {
        public enum Outcome { Open, Success, Fail }

        public readonly QteKey[] Sequence;   // directions, then QteKey.Parry last
        public readonly int OpenTick, Window;
        public int ArriveTick { get; private set; }       // moves later if the hand is held up at an earlier hour
        public int Index { get; private set; }            // next key expected
        public Outcome Result { get; private set; } = Outcome.Open;
        public string FailReason { get; private set; } = "";   // "wrong key", "early", "late" (diagnostics and tests)
        public int ResultTick { get; private set; } = -1;
        public QteKey? WrongKey { get; private set; }

        public ParryQte(QteKey[] sequence, int openTick, int arriveTick, int window)
        {
            Sequence = sequence; OpenTick = openTick; ArriveTick = arriveTick; Window = Mathf.Max(0, window);
        }

        public bool Open => Result == Outcome.Open;
        public int Directions => Sequence.Length - 1;
        public bool OnParry => Open && Index == Sequence.Length - 1;
        public bool InWindow(int tick) => tick >= ArriveTick - Window && tick <= ArriveTick + Window;

        // A random run of `directions` directions (no key twice in a row), then the parry.
        public static QteKey[] Roll(System.Random r, int directions)
        {
            var s = new QteKey[directions + 1];
            for (int i = 0; i < directions; i++)
            {
                QteKey k;
                do k = (QteKey)r.Next(4); while (i > 0 && k == s[i - 1]);
                s[i] = k;
            }
            s[directions] = QteKey.Parry;
            return s;
        }

        public void Press(QteKey k, int tick)
        {
            if (!Open) return;
            var want = Sequence[Index];
            if (k != want) { WrongKey = k; End(Outcome.Fail, "wrong key", tick); return; }
            if (want != QteKey.Parry) { Index++; return; }
            if (tick < ArriveTick - Window) { End(Outcome.Fail, "early", tick); return; }
            if (tick > ArriveTick + Window) { End(Outcome.Fail, "late", tick); return; }
            Index++; End(Outcome.Success, "", tick);
        }

        // after the window closes with no parry
        public void Step(int tick)
        {
            if (Open && tick > ArriveTick + Window) End(Outcome.Fail, "late", tick);
        }

        void End(Outcome o, string why, int tick) { Result = o; FailReason = why; ResultTick = tick; }

        public void Delay(int ticks) { if (Open && ticks > 0) ArriveTick += ticks; }

        // test / dev: settle it now (used when the event is withdrawn)
        public void Cancel(int tick) { if (Open) End(Outcome.Fail, "cancelled", tick); }
    }

    // Where a player's QTE presses come from. The encounter drains it once per combat tick.
    public interface IQteSource
    {
        void Begin();                        // a prompt opened for this player
        void Drain(List<QteKey> into);       // presses since the last tick, in order
    }

    // Local keyboard: W A S D or the arrow keys for directions, E to parry. Presses latch per rendered frame and are
    // drained on the next tick, so a tap is never lost or counted twice. A key already held when the prompt opens (the
    // player was still walking onto the mark) is ignored until every QTE key has been released once.
    public sealed class KeyboardQte : IQteSource
    {
        readonly List<QteKey> latched = new List<QteKey>();
        bool armed;

        public void Begin() { latched.Clear(); armed = false; }

        public void Poll()
        {
            var kb = Keyboard.current; if (kb == null) return;
            if (KeyboardMouseInput.Blocked || (RunDirector.Instance && RunDirector.Instance.Paused)) { latched.Clear(); return; }
            if (!armed)
            {
                if (!AnyHeld(kb)) armed = true;
                return;
            }
            if (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame) latched.Add(QteKey.Up);
            if (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame) latched.Add(QteKey.Left);
            if (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame) latched.Add(QteKey.Down);
            if (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame) latched.Add(QteKey.Right);
            if (kb.eKey.wasPressedThisFrame) latched.Add(QteKey.Parry);
            if (latched.Count > 16) latched.RemoveRange(0, latched.Count - 16);   // between prompts nothing drains it
        }

        static bool AnyHeld(Keyboard kb) =>
            kb.wKey.isPressed || kb.aKey.isPressed || kb.sKey.isPressed || kb.dKey.isPressed || kb.eKey.isPressed ||
            kb.upArrowKey.isPressed || kb.leftArrowKey.isPressed || kb.downArrowKey.isPressed || kb.rightArrowKey.isPressed;

        public void Drain(List<QteKey> into) { into.AddRange(latched); latched.Clear(); }
    }
}
