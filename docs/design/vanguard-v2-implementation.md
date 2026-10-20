# Vanguard 2.0 — implementation report

Implements the P0 and P1 rows of `reference/LAST-BELL-VANGUARD-CLASS-REDESIGN.md` §9 in the class-test harness: movement and the universal Clockstep Dash, the three-hit Clockblade Combo, Resonance, Pendulum Rush, Resonant Guard with precision counters, Reversal Rush, and Final Hour. Status: **implemented in the test harness and the 3D look prototype; not a Unity scene** (no Unity editor on this machine, so the Unity scene script was edited but never compiled or run).

## Repository check (redesign doc §12)

- The production Unity project does not exist yet; there is no `ProjectSettings/` and no U0 bootstrap. Following §12, nothing was migrated and no engine project was created.
- The redesign was built into the existing engine-independent C# class-test core (`unity/Assets/LastBell/Core/ClassTest`) and mirrored into the browser prototype (`prototype/arena-3d/index.html`).
- The legacy Godot prototype stays as reference. The old Vanguard kit is kept as `ClassKits.VanguardLegacy`, so `prototype-upgrades.md` comparisons and the legacy checks still run.

## What changed

| File | Change |
|---|---|
| `Core/ClassTest/VanguardKit.cs` (new) | `DashRules`, `VanguardRules`, the action state machine, facing and soft assist, counters, the Reversal state and the Reversal mark |
| `Core/ClassTest/ClassKits.cs` | New `Vanguard` kit (Resonance); legacy kit renamed `VanguardLegacy` |
| `Core/ClassTest/TrainingSim.cs` | `TrainingCommand.Aim` / `Dash` / `DashPressed`; circle and counterable hazards; `CounterableStrikes` option; dash i-frames give an `immune (dash)` outcome; `DealDamageAmount`; dash for the generic path |
| `Core/ClassTest/Healer.cs` | Healer uses the universal dash; dashing interrupts Mend; i-frames apply |
| `tools/class-test/ClassTestChecks.cs` | 9 new scenarios (126 assertions in total); legacy checks point at `VanguardLegacy` |
| `tools/class-test/js-parity.mjs` (new) | Runs the prototype's JS rules under Node against the same numbers |
| `prototype/arena-3d/index.html` | Same rules in JS, mouse/stick aim, dash for every class, counterable Pendulum Strikes in the Bellkeeper timeline (toggle), HUD dash slot and Reversal glow, mark glyph, facing arrow, combo/rush/counter/Final Hour effects and poses, `?autoplay=` test bot |
| `tools/class-test-desktop/ClassTestWindow.cs`, `Scenes/ClassTest/ClassTestArena.cs` | Dash bound to E or 5 (desktop also gamepad B) |

## Kit as implemented (all values provisional, 60 Hz ticks, legacy units)

| Action | Behaviour |
|---|---|
| **Clockblade Combo** (Basic) | Hits 1–2: 5-tick startup, 16 total, 2 damage, range 160, 110° arc. Hit 3: 8 / 26, 4 damage, range 175, 170° arc. The next press within 24 ticks of recovery continues the chain; hold to repeat. +1 Resonance per attack that lands (once per action). Small forward lunge, stopped at the boss body. |
| **Dash Attack** | Basic during a dash, or within 6 ticks after it: 6 / 20, 3 damage, 4.5-unit lunge per tick. Does not advance the chain. |
| **Pendulum Rush** (Technique) | Costs 3 Resonance; rejected below 3 with no spend. 4-tick startup with aim correction, then 14 ticks at 11 units/tick, stopping 70 units from the boss. Strike at range 110 in a 160° arc for 9. |
| **Reversal Rush** | After a full combo or a counter, ReversalReady lasts 150 ticks. The next rush consumes it: 15 damage, a radius-140 shockwave, and a ReversalMark for 240 ticks. |
| **Final Hour** (Signature) | 6 s cooldown, committed at start. 18-tick startup, 48 total, range 270, 220° arc. 18 damage, or 27 against a marked target (the mark is consumed). |
| **Resonant Guard** | 15-tick active window, 180-tick cooldown. A counterable strike resolving inside the window is a **counter**: no damage, positioning not failed, +1 Resonance, ReversalReady. |
| **Clockstep Dash** (all classes) | 2 charges, recharged one at a time every 72 ticks. 12 ticks over 150 units, 10 ticks of i-frames, 9-tick input buffer. Cancels Basic and Rush recovery only after the hit tick, and Final Hour only after its 30th tick. Cancels Arcanist casts and Healer Mend. |
| **Facing** | Aim input (mouse floor point or right stick) sets facing; otherwise the move direction does. Soft assist snaps to the dummy when it's within 30° and within range + 40. |

## Decisions that need a team call

1. **Name collision:** the doc names the Vanguard signature *Final Hour*, but the Bellkeeper's enrage is also *Final Hour*. Both are kept as written; failure cards say "Final Hour" for the enrage. Pick a rename (for example "Twelfth Stroke" for the signature).
2. **Counter semantics:** a counter on a designated counterable strike is treated as mechanic **success** (`countered`, not `guarded`). Guarding any non-counterable hazard is still a failed position (`guarded`). Dash i-frames prevent damage but are still a failed position (`immune (dash)` / `dodged`).
3. **Dash vs the enrage:** in the prototype, dash i-frames do **not** satisfy the Bellkeeper's Final Hour guard check, so the enrage still wipes. The C# harness has no enrage.
4. **Pendulum Strike** (doc §5, proposed): a circle of radius 80 centred on the player at warning, 60-tick warning, `Counterable = true`. In the C# harness it's in the demo cycle behind `CounterableStrikes`. In the prototype, five are added to the Bellkeeper timeline's quiet gaps (1290, 2630, 3270, 4000, 5300) behind the menu toggle "Pendulum Strikes". The encounter owner should decide whether it belongs in the real timeline.
5. **Controls:** the doc proposes A/X/Y/LB/RB with Dash on RB. The user asked for number keys, so the prototype uses 1 basic, 2 technique, 3 signature, 4 guard, 5 dash, and 6 for the Healer's target cycle, with mouse aim. The gamepad is A/X/Y/RB, with **B for dash** because RB stayed guard. Remapping is still to do.
6. **Boss body:** the prototype pushes players out of a 56-unit boss body; the C# harness has no body. Rush and lunges stop at 70 in both.
7. **Directional melee replaces auto-target** for the new Vanguard, as the doc asks. The Arcanist and Healer keep their range-based targeting.

## Verification

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File tools/class-test/check.ps1
```
```bash
node tools/class-test/js-parity.mjs
```

C#: 20 scenarios, 126 assertions, 0 failures. They cover:
- Resonance gain and the 0–6 cap, once-per-action gain, misses granting nothing, and modest soft assist.
- Rejecting Technique below 3 Resonance; rush distance and damage.
- Reversal priming by combo and by counter, its expiry and consumption; mark application and its Final Hour bonus.
- Dash charges, sequential recharge and i-frames; cancel windows (never during startup, Final Hour commitment kept); Dash Attack.
- Counters only on counterable strikes, only for the Vanguard, only inside the guard window.
- Universal dash for the Arcanist and Healer; seeded deterministic replay; and every earlier legacy, Arcanist and Healer check.

JS parity: 14 assertions, 0 failures.

The prototype was also played by its `?autoplay=vanguard&inv` bot in a throttled browser pane. Combos, Resonance, Reversal, Final Hour, a forced counter and Reversal Rush all fired with no page errors.

**Not verified:** feel at full frame rate with a human player, gamepad aim, the Unity scene script (never compiled), and balance.

## Not done (later rows of §9)

Bulwark, Executioner and Tempest relics; the optional role package; hit-stop; dash i-frame visuals; a perfect-counter sub-window narrower than the guard window; multiplayer; a real Animator. Every number above is a tuning target for playtests.
