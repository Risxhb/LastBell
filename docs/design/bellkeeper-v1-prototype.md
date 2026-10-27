# Bellkeeper v1.0 — prototype implementation report

Implements `reference/LAST-BELL-BELLKEEPER-BOSS-DESIGN-v1.0.md` (branch `docs/bellkeeper-design-reference`) in the playable browser prototype `prototype/arena-3d/index.html`. It's now the default encounter. The earlier v0.1 timeline (`docs/design/bellkeeper.md`, 4:00 enrage) is still selectable under Esc → Encounter. Status: **prototype only**. There is no Unity scene, and the C# class-test harness is unchanged.

## What's in it

| Doc section | Implemented |
|---|---|
| §3 four core attacks | Bell Sweep (80° cone, 8 m, 0.95 s, tracks 0.30 s then locks), Hollow Toll (2.6–6.8 m annulus centred on the boss's **current** position, 1.10 s), Pendulum Charge (2.2 m lane locked at warning, 0.80 s; the boss then travels the lane in 0.30 s and **stays where it lands**), Shattered Chime (solo: 3 sequential 1.4 m markers, 0.4 s apart, each follows the player then locks at 0.5 s, resolves at 1.20 s) |
| §3.5 animation contract | Windup with telegraph → lock → authoritative resolve tick → recovery → director decision |
| §4.1 phases | HP-based: I 100–70 %, II 70–35 %, III 35–0 %. A transition is queued until the active pattern, its pending markers and its hazards have fully resolved, then a banner and bell mark the new phase |
| §4.2 Phase 1 | Fixed order Sweep → Toll → Charge → Chime → 0.9 s recovery, looping |
| §4.3 Phase 2 | Blocks A Pursuit (Charge → Toll), B Discord (Chime → Sweep), C Reversal (Toll → mirrored Charge), cycling A → B → C. Left/right approach is chosen by the seed at the block boundary; C mirrors A |
| §4.4 Phase 3 | **The Final Toll**: Charge → Toll at the new position → Chime → Sweep (decisive, ~80 % max HP) → ~2 s extended vulnerability ("THE BELL FALTERS"), alternating with a Pursuit block |
| §5.3 randomisation | Seeded per attempt, chosen only at block boundaries; geometry never changes after lock |
| §6.1 punishment | Health is 10 pips in v1.0. Sweep 5, Charge 5, Toll 4, Chime 2 per marker, Final Toll strike 8. Post-hit immunity is unchanged (0.75 s). Healer heals and wards scale ×2, keeping the same share of the bar |
| §6.4 outcomes | Avoided, guarded, immune, dodged (dash i-frames), warded, hit, and countered are all kept separate |
| §6.5 / §3.5 recovery | Recovery windows after lone attacks (~0.7–0.8 s) and at block ends; the HUD shows VULNERABLE, and the boss core dims and wobbles |
| §9.3 failure report | The result screen shows phase reached, pattern block, fatal mechanic, what happened, a **How to improve** tip, and mechanics avoided as X / Y |

Pendulum Strike (the counterable strike from the Vanguard redesign) is appended to Phase 1 and to block B while the "Pendulum Strikes" toggle is on, so Resonant Guard counters can be practised.

**Unit conversion:** 1 m = 40 arena units (the prototype's drawing scale); 60 ticks per second. Baseline movement is 250 units/s (6.25 m/s). The dash covers 150 units (3.75 m) in 0.2 s.

## Fairness gate result (§11): needs a design decision

`tools/class-test/js-parity.mjs` samples every attack the director spawns in all three phases. For each one it measures the worst straight-line escape from inside its locked geometry, adds an 18-tick (0.3 s) reaction allowance, and compares that with the warning time left after lock.

| Attack | Margin with one Clockstep Dash | Walking only |
|---|---|---|
| Bell Sweep | +9 ticks | **−9 ticks: fails** (needs 48, has 39) |
| Hollow Toll | +36 | passes |
| Pendulum Charge | +18 | passes |
| Shattered Chime | +12 | passes |

**At the doc's numbers, the Bell Sweep can't be walked out of from mid-range on the cone's centreline after it locks.** It's escapable with the universal dash. Options for the encounter owner:
- Accept the dash as part of the baseline kit (§4.5 says "movement/dodge/defense kit").
- Lengthen the warning to about 1.1 s.
- Shorten the tracking sub-window.
- Narrow the cone to about 70°.

The prototype keeps the doc's values and reports this as a warning.

**What the gate does not cover:** walls blocking an escape path, chained combinations (for example the Final Toll's Toll right after the Charge), co-op spacing, or human reaction variance. §11 still requires playtests.

## Decisions taken in the prototype

1. The charge's hit check resolves once, at the end of the warning, over the whole locked lane. The boss then travels the lane as presentation, and players in its path are pushed out of its body.
2. Charge target = the player's position at warning, plus a seeded ±70-unit side offset in Phase 2 and a 110-unit overshoot. The lane stops 70 units short of the arena wall.
3. Resonance Reclaim (§6.2) is **not** implemented, by the user's decision on 2026-10-20.
4. Co-op target distribution (§7) isn't implemented: the prototype has one player, and the Healer's training ally isn't targeted. The user's direction is co-op first, so this is the next encounter task once the session layer exists.
5. Rehearsal (§9.2) is partial: Esc → Start at Phase I/II/III sets boss HP to the phase threshold. There's no block picker or seed field yet.
6. There's no enrage in v1.0, since the doc defines none. The fight timer is informational.

## Verification

```bash
node tools/class-test/js-parity.mjs
```

38 assertions, 0 failures. Covered:
- Phase 1 order and looping; Sweep tracking and lock timing; Chime marker lock.
- Charge lane geometry, boss travel and landing, and Pursuit's Toll centring on the landing point; both seeded charge sides appear.
- Damage tiers; transitions that wait for the pattern; the Final Toll's order, decisive damage and extended vulnerability; seeded reproducibility.
- The fairness gate, plus all earlier Vanguard/dash parity checks, pinned to v0.1.

Visual checks: headless Edge captures at fixed ticks with `index.html?autoplay=vanguard&inv&phase=2&ff=96` (the charge lane, with chevrons) and later ticks (the Toll at the boss's new position). All three classes load with no page errors.

**Not verified:** human play at full frame rate, the Healer and Arcanist through a full v1.0 clear, or balance.
