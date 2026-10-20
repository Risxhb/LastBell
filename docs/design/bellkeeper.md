# The Bellkeeper — encounter specification

Draft v0.1 · October 20, 2026 · Owner: `chzy18/claude` ([#8](https://github.com/Risxhb/collab/issues/8)) · Reviewer: `risxhb/codex`; `chzy18/codex` for session-facing rules.

Status: **specification only.** Nothing here is implemented. It is engine-independent and written for the Unity/C# production path ([decisions](../decisions.md), [Unity migration plan in #13](https://github.com/Risxhb/collab/pull/13)). Shared geometry, requirement evaluation, RNG, reset and events belong to the foundation lane ([#6](https://github.com/Risxhb/collab/issues/6)). This document says what the encounter needs from them; it does not define private hit testing. Every number is an initial tunable value, not a balance claim.

Companion documents: [prototype upgrades](prototype-upgrades.md) and [encounter test matrix](../testing/encounters.md).

## 1. Baseline values this spec is built on

The spec reuses the legacy Godot reference behaviour (`feature/hazard-lock-timing`, `game/core/combat_simulation.gd`) so the C# port and this spec can be compared directly. If U1 changes any of these, re-run the escape checks in §4.

| Value | Legacy reference | Used here as |
|---|---|---|
| Tick rate | 60 Hz | All timings below are ticks; seconds are rounded for reading |
| Baseline move speed | 250 units/s | `v = 4.1667` units/tick |
| Arena | rect origin (60, 140), size 820 × 600; actor centres clamped 14 units inside | Playable centre region x ∈ [74, 866], y ∈ [154, 726] |
| Boss position | fixed at (470, 360) | Origin of every boss-anchored hazard |
| Collision | actor centre point; boundaries are dangerous | Unchanged |
| Guard / immunity | 24-tick guard window, 180-tick cooldown; 45-tick immunity after a hit | Unchanged; upgrades in the companion doc reference these |
| Health | 5 pips, ordinary hazard removes 1 | Unchanged |

Boss health, damage scaling by party size and phase pacing by HP are foundation decisions. In v0.1 the timeline is **time-based**, not HP-gated, so every seed and party count produces the same schedule. HP gates can be added once kill times are measured.

## 2. Attack vocabulary

Original names. Each attack has one shape, one icon and one meaning at every party size. The solution may change with party size, but the visual meaning stays the same (plan §6).

| ID | Attack | Shape | Anchor and lock | Requirement | Default window |
|---|---|---|---|---|---|
| `bell_sweep` | **Bell Sweep** | Cone from the boss, radius 380, half-angle 36° (π/5) | `fixed`; direction aimed at a seeded living target at warning, ±0.15 rad seeded jitter | avoid | warn → resolve 90 |
| `bell_sweep_tracking` | **Tracking Sweep** | Same cone | `follow_until_lock`, component `direction`; lock = warn + 45 | avoid | resolve = warn + 120 |
| `hollow_toll` | **Hollow Toll** | Donut centred on the boss, inner 155, outer 380 | `fixed` | avoid | 90 |
| `tolling_mark` | **Tolling Mark** (spread) | Circle radius 110 (tier A) / 100 (tier B) on each marked player | `follow_until_lock`; lock = resolve − 1 (see §8, request R3) | spread | 150 |
| `lingering_mark` | **Lingering Mark** (solo spread) | Circle radius 110 on the solo player | `snapshot_at_lock`; lock = warn + 90 | avoid | resolve = warn + 160 |
| `gathering_peal` | **Gathering Peal** (stack) | Circle radius 90 (tier A) / 100 (tier B) on each stack target | `follow_until_lock`; lock = resolve − 1 | stack | 210 |
| `sanctuary_peal` | **Sanctuary Peal** (solo stack) | Circle radius 90 at a seeded shelter point, (330, 560) or (610, 560) | `fixed` | shelter (must be inside) | 210 |
| `remembered_sweep` | **Remembered Sweep** (Echo) | Exact locked geometry of the retained Bell Sweep | `fixed` | avoid | 120 (Echo intro), 180 (combination) |
| `final_hour` | **Final Hour** (enrage) | Circle radius 1000 centred on the boss (covers the arena) | `fixed` | guard | 90, repeating every 90 |

**Requirement kinds** are evaluated by the shared core from the locked geometry and actor centres at the resolve tick:

- **avoid:** every living player whose centre is inside fails.
- **spread:** every living player inside a circle that is *not their own* fails ("stood in another player's mark"). The owner of the circle does not fail for being in it. One failure per player per resolve, even when several circles overlap them.
- **stack:** for each stack, count living players inside. A player inside at least one stack whose count ≥ `min_players` succeeds. A player inside only under-filled stacks, or inside no stack, fails.
- **shelter:** the solo player fails when *outside* the circle. It's a stack with `min_players: 1` at a fixed point; listed separately so the failure card can say "outside the sanctuary".
- **guard:** every living player fails unless guard is active on the resolve tick. Position doesn't matter.

All attacks are guardable in v0.1. As the contract already establishes, a guarded failure prevents damage but still records `mechanic_failed: true`. **Final Hour is the only mechanic where guarding *is* the solution**, and the failure card must say so explicitly. Guard cooldown (180) is longer than the Final Hour interval (90), so the enrage still wipes the party within a few repetitions.

## 3. Timeline (Standard difficulty)

Times are ticks from the encounter's first scheduling tick. `W` = warning, `L` = lock, `R` = resolve. A row with two attacks warns them in the same provider call. Teaching order follows plan §6: show alone → repeat with variation → combine → twist → execution test.

### Phase 1 — Opening: cones and donuts (0:01–0:22)

| # | W | Attack | L | R | Teaches |
|---|---|---|---|---|---|
| 1 | 60 | Bell Sweep | 60 | 150 | Read a wedge, step sideways |
| 2 | 270 | Hollow Toll | 270 | 360 | The hole is safe; melee range (155) fits inside it |
| 3 | 480 | Bell Sweep | 480 | 570 | Repeat |
| 4 | 690 | Hollow Toll | 690 | 780 | Repeat |
| 5 | 900 | Tracking Sweep | 945 | 1020 | Variation: it follows you until the lock cue, then commits |
| 6 | 1110 / 1170 | Hollow Toll, then Bell Sweep | 1110 / 1170 | 1200 / 1260 | Combine: shelter in the hole, then leave the wedge |

### Phase 2 — Assignment: spreads, then a stack (0:23–0:44)

| # | W | Attack | L | R | Teaches |
|---|---|---|---|---|---|
| 7 | 1380 | Tolling Mark (solo: Lingering Mark) | per §2 | 1530 (solo 1540) | Personal responsibility |
| 8 | 1620 | Hollow Toll | 1620 | 1710 | Regroup through a known shape |
| 9 | 1800 | Gathering Peal (solo: Sanctuary Peal) | per §2 | 2010 | Regroup |
| 10 | 2100 / 2190 | Tolling Mark, then Bell Sweep | per §2 / 2190 | 2250 / 2280 | Spread without spreading into the wedge |
| 11 | 2400 | Gathering Peal (solo: Sanctuary Peal) | per §2 | 2610 | Regroup under pressure |

### Phase 3 — Echo introduction (0:45–0:59)

| # | W | Attack | L | R | Teaches |
|---|---|---|---|---|---|
| 12 | 2700 | Bell Sweep, **recorded** | 2700 | 2790 | The bell remembers this one |
| 13 | 2910 | Hollow Toll | 2910 | 3000 | Gap |
| 14 | 3120 | **Remembered Sweep** (Echo of #12) | 3120 | 3240 | It comes back exactly where it was |
| 15 | 3360 | Tracking Sweep, **recorded** | 3405 | 3480 | Where it locked is what's remembered |

### Phase 4 — Combination (1:00–1:24)

| # | W | Attack | L | R | Teaches |
|---|---|---|---|---|---|
| 16 | 3600 | Remembered Sweep (Echo of #15) + Tolling Mark | 3600 / per §2 | 3780 | Plan routes around a remembered shape |
| 17 | 3900 | Hollow Toll | 3900 | 3990 | Gap |
| 18 | 4110 | Bell Sweep, **recorded** | 4110 | 4200 | |
| 19 | 4380 / 4440 | Gathering Peal + Remembered Sweep (Echo of #18) | per §2 / 4440 | 4590 / 4590 | Stack outside the Echo |
| 20 | 4800 / 4890 | Tolling Mark, then Hollow Toll | per §2 / 4890 | 4950 / 5010 | Spread, then commit to hole or outside |

### Phase 5 — Final sequence (1:30 until defeat or enrage)

A repeating cycle with **150-tick spacing between warnings** instead of ~210. Each attack keeps its own warning window from §2. Only the gaps shrink, never the reaction windows. The cycle starts at tick 5400:

`Bell Sweep → Hollow Toll → Tolling Mark + Remembered Sweep → Gathering Peal → Tracking Sweep (recorded) → Hollow Toll →` repeat. The first Remembered Sweep in each cycle echoes the last recorded sweep from the previous cycle (Phase 4 #18 for the first cycle).

### Enrage — Final Hour (4:00)

At tick 14400, all pending warnings are cancelled (no Echo carries over), and Final Hour warns every 90 ticks until the party is defeated. Target kill time is about 3:00, leaving roughly a minute of Phase 5 slack. Boss HP is tuned by the foundation lane against this target.

## 4. Baseline-speed escape checks

Each attack must leave a reachable safe position for a player moving at baseline speed from **any** playable position:

```
reaction + ceil((d_max + margin) / v)  ≤  window
```

- `reaction = 30` ticks (0.5 s), provisional.
- `margin = 2` units. Boundaries are dangerous, so the player must end strictly outside.
- `v = 4.1667` units/tick.
- `window` = resolve − lock for tracking and snapshot anchors (the final footprint is only known at lock), and resolve − warning for fixed anchors.

The cone and donut `d_max` values come from sampling every playable centre (2–6 unit grid, 72 cone directions) against the legacy arena. They account for arena clipping, which is why the cone's worst case (into the top-left corner) is larger than the open-floor value.

| Attack | `d_max` (units) | Travel (ticks) | Needed | Window | Margin |
|---|---|---|---|---|---|
| Bell Sweep (fixed) | 153.1, cone aimed at −130° | 38 | 68 | 90 | 22 |
| Tracking Sweep | 153.1 | 38 | 68 | 75 (L → R) | 7 |
| Hollow Toll | 131.2, below the boss at (470, 646) | 32 | 62 | 90 | 28 |
| Lingering Mark (solo) | 110 (circle radius) | 27 | 57 | 70 (L → R) | 13 |
| Tolling Mark, 8 players starting stacked in a corner | 317 (3 × 3 grid, 112 spacing) | 77 | 107 | 150 | 43 |
| Gathering Peal, two players at opposite corners meeting | 398.5 (half the 977 diagonal, minus radius 90) | 97 | 127 | 210 | 83 |
| Sanctuary Peal | 582.4 (672.4 to the farthest corner, minus radius 90) | 141 | 171 | 210 | 39 |
| Remembered Sweep | 153.1 | 38 | 68 | 120 / 180 | 52 / 112 |

**Sequential checks.** When two resolves are close, the second attack's escape starts from the first attack's safe region, not from anywhere in the arena. Rows 6, 10, 19 and 20 were checked this way:

- **Row 6:** inside the donut hole, the cone's lateral escape is at most 93 units, which takes 23 ticks. That fits the 60-tick gap between resolves.
- **Row 10:** the Bell Sweep resolves only 30 ticks after the spread, which is less than the 38 ticks a worst-case cone escape needs. So this is a *combined* check: players must choose spread positions outside the cone, which is warned 60 ticks before the spread resolves. Plenty of space qualifies, but the validator must confirm a valid spread placement outside the cone exists for every seed and count.
- **Row 19:** the stack target can lead the group anywhere outside the Echo, which is fixed and previewed since tick 4200.
- **Row 20:** from any spread position, reaching Hollow Toll safety takes at most 32 ticks. That fits the 60-tick gap, and the toll was warned 120 ticks earlier.

The validator (§7) must repeat these checks for every scheduled pair, at every count.

Tier B's double Bell Sweep (two cones at once) isn't in this table. The union of two cones depends on the two seeded directions, so it's left to the validator's combined check. If that check fails for some seeds, widen the jitter separation or lengthen the window.

**Limits.** These are worst-case straight-line checks at baseline speed on the legacy arena. They assume no other hazard blocks the path, which is exactly what the sequential and combined checks in the validator must cover. They don't prove a solution exists for every combination of eight players' simultaneous constraints. §7 says what the validator does instead.

## 5. Echo rules

- **One retained attack.** Only Bell Sweep and Tracking Sweep instances flagged **recorded** are retained. A new recording replaces the previous one. The retained value is the *locked* world geometry (origin, direction, radius, half-angle) at its resolve tick, plus its attack ID.
- **One active Echo.** Never schedule a Remembered Sweep while another is warning. The validator rejects this.
- **Preview.** From the recorded sweep's resolve until its Echo resolves, the floor shows a **memory mark**: the same outline as a dotted, unfilled trace with the bell-memory glyph, at reduced contrast. It deals no damage. Per the art guide (§8.3, §11.2), it must never be mistakable for a live warning: no fill, no pulse, a different line style. Section 8 lists the contract request for presentation-only markers.
- **Return.** The Echo warns with the full danger treatment on the exact retained geometry. Its resolved events carry `source_attack_id` = the recorded attack's ID, so the failure card can say "Echo of Bell Sweep (0:46)".
- **Reset.** The retained attack and memory mark are encounter state. Reset and phase restore must clear or restore them together with RNG.
- **Knockouts don't cancel an Echo.** It's world geometry with no target.

## 6. Party-size policy (counts 1–8)

**Tier** is fixed at attempt start from the roster size (tier A: 1–4, tier B: 5–8), so the visual vocabulary never changes mid-fight. **Per-mechanic counts** are derived from *living, active* players at each warning tick, clamped to the table below.

| Count | Tier | Bell Sweep cones | Tolling Mark | Mark radius | Gathering Peal | Stack radius | Stack `min_players` | Subgroups |
|---|---|---|---|---|---|---|---|---|
| 1 | A | 1 | Lingering Mark on self | 110 | Sanctuary Peal shelter | 90 | 1 (shelter) | — |
| 2 | A | 1 | 2 marked (all) | 110 | 1 stack | 90 | 2 | — |
| 3 | A | 1 | 3 marked (all) | 110 | 1 stack | 90 | 3 | — |
| 4 | A | 1 | 4 marked (all) | 110 | 1 stack | 90 | 4 | — |
| 5 | B | 2 | 3 marked | 100 | 2 stacks | 100 | 2 | 3 / 2 |
| 6 | B | 2 | 3 marked | 100 | 2 stacks | 100 | 3 | 3 / 3 |
| 7 | B | 2 | 4 marked | 100 | 2 stacks | 100 | 3 | 4 / 3 |
| 8 | B | 2 | 4 marked | 100 | 2 stacks | 100 | 4 | 4 / 4 |

Rules behind the table:

- **Tier B spreads** mark `ceil(n / 2)` players. Unmarked players must stay out of every mark, so the spread still involves the whole party.
- **Tier B Bell Sweep** fires two cones in the same tick, aimed at two different seeded targets, one from each subgroup when possible. Hollow Toll and Echo are the same at every count.
- **Odd counts.** Spreads round up. Stacks use `min_players = floor(n / 2)`, so the larger subgroup has one spare player who can join either stack.
- **Subgroups (tier B).** Living roster sorted by stable player ID; index 0, 2, 4… → group 1, index 1, 3, 5… → group 2. Groups only choose stack and sweep targets so the two stacks start apart. Stack success is occupancy-based: any player counts toward any stack. The HUD should show group membership (shape tag, not colour alone).
- **Duplicate classes.** No assignment depends on class in v0.1, and none may in future. Any party of duplicate Vanguards (or Arcanists) must be able to clear. A class-specific mechanic would need an alternative solution for parties without that class.
- **Targeting.** Every seeded choice uses the independent encounter RNG stream (§8, R6), never combat RNG, and only ever picks living, active players.

### Knockout and disconnect during a mechanic

The session lane decides when a player becomes **inactive** (disconnected, or idle pending removal at the next encounter boundary, per plan §9). For targeting, the encounter treats inactive the same as knocked out.

| Situation | Policy | Event |
|---|---|---|
| Tolling Mark target unavailable **before lock** | Reassign to a seeded living, unmarked player if `R − tick ≥ 107` (the spread escape need), else cancel that mark | `assignment_changed` with old/new player, or `cancelled: true`, reason |
| Gathering Peal target unavailable before lock | Reassign within the same subgroup, else the other subgroup, if `R − tick ≥ 127`, else cancel that stack. A cancelled stack removes its requirement, so nobody fails for missing it | as above |
| Tracking Sweep target unavailable before lock | Retarget the direction to a seeded living player. If none remain, lock the current direction (the cone stays valid world geometry) | as above |
| Any target unavailable **after lock** | The hazard is world geometry and resolves normally | — |
| Stack under-filled because of a knockout after lock | At resolve, `min_players = min(locked_min, floor(living / 2))` in tier B, `min(locked_min, living)` in tier A. The event records both values | `requirement` block in the resolved event |
| Sanctuary Peal, Lingering Mark | Solo only; a solo knockout ends the attempt | — |

The legacy core currently **cancels** unlocked hazards whose target is unavailable. Reassignment needs provider-selected reassignment (§8, R4). Until that exists, these rows fall back to cancellation, and the test matrix records which behaviour ran.

## 7. Validator and timeline preview requirements

Production tools belong to the Unity implementation step of #8, after the C# contract (U3/A4) is reviewed. The validator checks authored Bellkeeper data, **for every party count 1–8 and a fixed seed set**, against these rules:

1. **Timing.** `W ≤ L < R` per the contract, warnings on scheduling ticks, unique attack IDs, and no two Remembered Sweeps active at once. Every Echo needs a recorded sweep before it, with no intervening recording.
2. **Escape.** Every attack meets §4 at baseline speed, using the cone/donut sampling method above.
3. **Combined and sequential reachability.** For hazards that overlap in warning time, or that resolve within 120 ticks of each other, sample positions on a grid. Then confirm that for the worst start position there is a sequence of safe positions reachable within each window. For spreads and stacks, confirm a valid placement exists with the required pairwise separation, outside every simultaneous avoid shape.
4. **Assignments.** Counts per §6, only living or active targets, subgroups non-empty, and every `min_players` satisfiable by the living count.
5. **Reset.** Running the same seed twice produces identical schedules and assignments, including after a mid-fight reset.

**Stated limits.** The validator samples a grid and uses straight-line travel. It is evidence for these authored patterns, not a general proof of solvability. It doesn't model player collisions, human reaction variance or network latency.

**Timeline preview.** A scrubbable view of the schedule per party count and seed, with mechanic toggles, overlaying exact geometry, lock moments and memory marks. Tool format and serialization are agreed with the foundation owner (#6).

## 8. Requests to the shared C# contract (#6)

These were accepted in principle in #6 and are not yet implemented. The spec depends on them. Each item says what the encounter needs; the foundation owner decides how.

| # | Need | Why |
|---|---|---|
| R1 | A **circle** shape kind (centre, radius) in shared geometry | Tolling Mark, Gathering Peal, Sanctuary Peal and Final Hour |
| R2 | A per-hazard **requirement** (`avoid` · `spread` · `stack` · `shelter` · `guard`, with `min_players`), evaluated by the shared core | Group mechanics without private hit testing in encounter code |
| R3 | Allow `lock_tick == resolve_tick` for `follow_until_lock` (or an equivalent `follow_until_resolve`) | Spreads and stacks should centre on the player at resolve. The `R − 1` workaround leaves up to 4.2 units of lag |
| R4 | **Provider-selected reassignment.** On an unavailable target, the core asks the provider for a replacement or cancel before cancelling by default | §6 knockout and disconnect policy |
| R5 | **Presentation-only markers** (memory marks) issued by the provider, never resolved, and cleared on reset or by ID | Echo preview without a fake hazard |
| R6 | An independent, seeded **encounter RNG** stream, included in reset and checkpoint state | Targeting can't drift when combat actions change |
| R7 | A session-provided **active** flag on roster entries | Disconnect handling without removing actors mid-fight |
| R8 | Resolved-event additions, listed in §9 | Truthful failure card |
| R9 | An `immunity_source` on `immune` outcomes (`hit_recovery` or an upgrade ID) | Overflow Bulwark ([upgrades](prototype-upgrades.md) §4) makes "immune" ambiguous otherwise |

## 9. Failure-card event requirements

The failure card must be built from **resolved historical state**, not current positions. Each `mechanic_resolved` event (or the group-resolution record it references) needs:

- **Identity:** `schema_version`, `generation`, `event_id`, `player_id`. The shared core already has these.
- **Attack:** `attack_id`, `attack_name`, plus the encounter's `pattern_id` (for example `tolling_mark`) and phase number.
- **Time:** the authoritative `tick` and the elapsed fight time in seconds, so the card can say "0:23".
- **Geometry as resolved:** shape kind, locked origin, direction, radii, angle, anchor mode, `lock_tick`, and `target_player_id` (the existing provenance fields, extended to circles).
- **Requirement evaluation:**
  - `kind`
  - `min_players`, plus `locked_min` when the knockout cap applied
  - `count_inside`
  - the player IDs inside
  - for spreads, the ID of the mark the player stood in
  - a short reason code: `inside_avoid`, `in_other_mark`, `stack_underfilled`, `outside_stack`, `outside_shelter`, `not_guarding`
- **Relevant positions:** the affected player's centre at resolve. For group requirements, the centres of every living player at resolve, so the card can draw the small arena diagram (plan §8). The legacy log keeps the latest 128 events; with eight players, positions should sit in one group-resolution record per hazard rather than repeating in every per-player event.
- **Outcome:** `outcome` (avoided · guarded · immune · hit) and `mechanic_failed`, kept separate. Guarded and immune must never read as avoided.
- **Echo provenance:** `source_attack_id` and the source's resolve tick.
- **Assignment history:** any `assignment_changed` events for this hazard, linked by `attack_id`.

Example failure card text the data must support: *"Gathering Peal (1:16): stack needed 3 players, yours had 2. You were guarding, so no damage was taken, but the mechanic failed."*

## 10. Visual notes for implementers

Follow the [art-direction guide](https://github.com/Risxhb/collab/blob/docs/2-5d-isometric-direction/reference/LAST-BELL-CLOCKWORK-FANTASY-ART-DIRECTION.md). It's adopted in #13, not yet on main.

- **Bell Sweep and Tracking Sweep (§10.5, §12.1):** radial harmonic lines with a strong outer boundary. While tracking, show TRACKING with a moving edge; at lock, a hard "click" cue and a solid edge. The boss turns its resonator to the locked direction.
- **Hollow Toll (§10.5):** a filled annulus with two solid boundaries and a visibly empty hole.
- **Tolling Mark:** a bell-rim circle carrying the owner's shape tag. It must differ from friendly circles by line style and fill, not colour alone (§11.5).
- **Gathering Peal and Sanctuary Peal:** a filled circle with an inward-pointing chevron ring and a count readout (`2/3`). Sanctuary Peal uses the same treatment, so solo and team read the same.
- **Memory mark:** a dotted, unfilled outline with the bell-memory glyph at low contrast. It must stay distinguishable from live warnings in grayscale.
- **Final Hour:** an arena-wide ring with a large guard icon. It's the only warning that says GUARD.
- **Eight players (§9.5, §12.4):** reduce per-player mark decoration and keep every mark's boundary crisp. This is covered in the test matrix's density checks.
