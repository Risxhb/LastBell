# Escapement Twins — Unity block-out

Branch `feature/escapement-twins`, stacked on `feature/run-loop` (#42). Spec: [docs/design/escapement-twins.md](https://github.com/Risxhb/collab/blob/docs/escapement-twins-spec/docs/design/escapement-twins.md) (PR #55, v0.2, decisions accepted). Code: `unity/Assets/LastBell/Run/Encounters/EscapementTwins.cs`.

**Status:** playable block-out at Steady Ticking. The model is a code-built stand-in (plinth, two towers with sun and moon faces, bridge, ratchet, pawls), not production art.

## How to play it

Menu → **Practice one encounter → Escapement Twins**. It's practice-only (slot `RunDirector.PracticeEscapementTwins` = 23), not part of the run's sequence. Dev keys: **F8** drops it to 49 % (Phase 2 starts at the next attack boundary), **F9** defeats it.

## What's in

- **Body:** 5.2 × 2.2 m stadium at the arena centre. It only turns, at 40°/s, during a 1 s gap between attacks.
  - New `BossEncounter.BodySegment` and `Core()` make player collision, melee, beams, projectiles and the Last Word wave use the stadium.
  - Every other boss has `BodySegment` = 0, so it behaves exactly as before.
- **Phase 1, Agreement:** Sun Verdict → Moon Rebuttal → Common Measure, each followed by the gap.
- **Phase 2, Disagreement:** Alternating Verdict → Deadbeat → Common Measure → Alternating Verdict → Rebuttal Pair.
- **Phase transition:** crossing 50 % queues one transition at the next gap, lasting 2 s, harmless.
- **Self-strike:** if the locked Verdict lane's centreline crosses the opposite pawl, the floating text reads JAMMED, the ratchet stutters and that cast recovers for 240 ticks. No HP damage, at most once per cast.
- **Hit rules:** hit-stun 9 ticks and no knockback on every Twins hazard (spec §2).
- **New shared pieces:**
  - `HazardShape.Stadium` (a ring measured from a segment) with its telegraph (shader shape 4).
  - `Telegraph.SetLength`, so a tracking lane can change length.
  - `BossEncounter.NextPhaseFraction`, so F8 works for a 50 % threshold. This is the same change as the Crucivar branch.

## Not in yet

- Difficulty tiers: First Chime, Striking Hour, and Midnight Toll's Counter-Measure. The run loop has no difficulty setting.
- Active-duration hazards (R22). Each hazard still resolves on one tick; the active period is only scheduled time.
- Co-op: rotating targets and centroid turning are coded, but only solo has been run.
- Audio, the real model and animation, and the Brass Foundry arena (it's on `feature/class-test-arena` `87ab12b`; this uses the key-art arena).

## Tests

Twins only: `LastBell.exe -lastbellAutotest -lastbellOnly twins -lastbellOut <dir>`. The full autotest runs the same section at the end. The Twins checks cover:

- spawn at the centre
- stadium push-out, and a hit at the tower end
- baiting the self-strike, then sidestepping after lock without damage
- the 4 s jam recovery
- the pawl geometry with and without a cross
- the queued transition with nothing warning
- Deadbeat's corridor being safe
- Rebuttal Pair never having two live cones
- boss death mid-windup cancelling everything
- the practice result

**Results (October 21, 2026):**
- Twins-only run: 22/22.
- Full autotest in the packaged player: **134/134**, with 0 errors logged ([report](media/autotest-report-twins.txt)).
- Screenshots: [Verdict bait](media/twins-verdict-bait.png), [Common Measure](media/twins-common-measure.png).
- A self-strike video is kept locally in `run-loop-evidence/twins-1/twins_self_strike.mp4` (not committed).
