# Encounter test matrix — Bellkeeper

Draft v0.1 · October 20, 2026 · Owner: `chzy18/claude` ([#8](https://github.com/Risxhb/collab/issues/8))

This matrix covers the [Bellkeeper specification](../design/bellkeeper.md) and [prototype upgrades](../design/prototype-upgrades.md). It's written before any Unity code exists. Each row says what to check, how, and what counts as a pass. Rows become C# tests or manual checklists once the shared contract (#6, U3/A4) and the encounter implementation exist. **Nothing in this matrix has been run.**

Every row is one of two kinds, kept separate as #8 requires:

- **Scripted (S):** an automated C# check with a fixed seed. Evidence is a test result in the PR.
- **Human (H):** a person at the gameplay camera, a physical controller where noted, and the shipping visuals. Evidence is the tester's notes plus screenshots or a short capture. Scripted results never substitute for human rows.

Old Godot fixtures validate only the legacy reference. They're not evidence for Unity.

**Standard seed set:** seeds 1, 2, 3, 17, 404, 9001 at every party count 1–8, unless a row says otherwise.

## 1. Geometry edges

| ID | Kind | Check | Pass |
|---|---|---|---|
| G1 | S | Actor centre exactly on each boundary: cone edge ray, cone arc, donut inner circle, donut outer circle, circle edge | Counted as inside for every shape (boundaries are dangerous) |
| G2 | S | Centre 0.5 units outside each boundary above | Counted as outside |
| G3 | S | Cone near-angle cases: ±1 microradian around the half-angle, and directions crossing ±π | Matches the shared tolerance; no wrap-around error |
| G4 | S | Donut hole: centre at the boss and 154.5 units from it | Safe |
| G5 | S | Hazard footprint reaching past the arena edge | Collision is unaffected; arena clamping only limits actor positions |
| G6 | S | Floor mesh vs collision: sample 1,000 points per shape; compare the generated warning mesh's containment with the shared geometry | Disagreement only within the documented tessellation tolerance, and only at curved boundaries |
| G7 | H | At the gameplay camera, stand on the visible edge of each warning and wait for resolve | Testers predict hit or safe correctly at least 9 times out of 10; record misreads |

## 2. Tracking and lock cues

| ID | Kind | Check | Pass |
|---|---|---|---|
| L1 | S | Tracking Sweep follows the target's post-movement position on each tick through `lock_tick`, inclusive; no change after | Direction is frozen from `lock_tick` onwards; collision uses the frozen value |
| L2 | S | Lingering Mark (snapshot) samples once at lock | Origin equals the player centre on `lock_tick` |
| L3 | S | Spreads and stacks with lock = resolve − 1 (until R3 lands) | Lag is at most 4.17 units; documented |
| L4 | S | Fixed attacks keep their locked geometry from warning to resolve | Unchanged |
| L5 | H | TRACKING → LOCKED transition is noticed without audio | Testers point to the moment it locked; muted and grayscale capture included |
| L6 | H | Boss resonator faces the locked cone direction | Visual direction matches the floor cone within a glance |

## 3. Timeline and escape

| ID | Kind | Check | Pass |
|---|---|---|---|
| T1 | S | Generate the full schedule for each count and seed | Matches spec §3 tick for tick; identical across two runs |
| T2 | S | Escape check per attack (spec §4), using the sampling method | Every attack meets `reaction + travel ≤ window` with recorded margins |
| T3 | S | Sequential and combined reachability for rows 6, 10, 16, 19, 20 and the Phase 5 cycle | A reachable safe sequence exists from the worst start, at every count |
| T4 | S | Tier B double Bell Sweep across the seed set | The union of both cones leaves a reachable region; log any failing seed |
| T5 | S | Echo rules: only one retained attack, one active Echo, every Echo preceded by a recording | Validator rejects a deliberately broken timeline (two Echoes; Echo with no recording) |
| T6 | S | Enrage at 14400 cancels pending warnings and repeats Final Hour every 90 ticks | No Echo or assignment carries into the enrage |
| T7 | H | A new player clears Phase 1 without explanation | Note which warnings were misunderstood |

## 4. Requirements and outcomes

| ID | Kind | Check | Pass |
|---|---|---|---|
| R1 | S | Avoid: inside / outside / guarded inside / immune inside | `hit` / `avoided` / `guarded` with `mechanic_failed: true` / `immune` with `mechanic_failed: true` |
| R2 | S | Spread: the owner alone in their own mark; another player inside; one player inside two marks | Owner succeeds; the intruder fails once, with `in_other_mark` and the mark owner's ID |
| R3 | S | Stack: exactly `min_players`, one fewer, a player outside every stack, two stacks with uneven occupancy | Correct per-player success or failure, with the reason codes from spec §9 |
| R4 | S | Shelter: inside and outside | `outside_shelter` on failure |
| R5 | S | Final Hour: guarding and not guarding | Guarding succeeds; not guarding fails with `not_guarding` |
| R6 | S | Outcome versus mechanic separation for every requirement kind | `outcome` and `mechanic_failed` are never conflated |
| R7 | H | The failure card after each failure kind | Testers explain what resolved, where safety was, and whether guard helped, without developer help (plan §11) |

## 5. Guard and immunity

| ID | Kind | Check | Pass |
|---|---|---|---|
| D1 | S | Guard window boundaries: guard ending on the resolve tick (exclusive end) vs one tick later | Matches the contract's exclusive end-tick rule |
| D2 | S | Two hazards resolving on the same tick, unguarded | One pip lost; the second is `immune` with `immunity_source: hit_recovery` |
| D3 | S | Overflow Bulwark immunity during a hazard | `immune` with `immunity_source: overflow_bulwark` |
| D4 | S | Bell-Timed Riposte fires when guard is active on any resolve tick, not just when the player is inside | Upgrade event logged; Technique empowered |
| D5 | H | Guarded vs avoided feedback at the gameplay camera, with reduced effects on | Testers never describe a guarded failure as a dodge |

## 6. Complete reset

| ID | Kind | Check | Pass |
|---|---|---|---|
| X1 | S | Reset mid-tracking, mid-Echo-preview, mid-stack and during enrage; then rerun the seed | Snapshot, schedule, assignments, retained attack and RNG stream all match a fresh start |
| X2 | S | Reset after a reassignment | No stale `assignment_changed` carries over; the generation increments |
| X3 | S | Reset clears upgrade state (Riposte charge, Steady Stance timer, Echoing Strike pending repeat) | No upgrade event fires after reset from pre-reset state |
| X4 | H | Restart takes under five seconds and leaves no stale warnings, memory marks or feedback | Stopwatch result recorded; screenshot after restart |

## 7. Every party count

Run T1–T3 and R1–R6 at each count. The count-specific checks below are in addition.

| ID | Kind | Count | Check | Pass |
|---|---|---|---|---|
| C1 | S | 1 | Lingering Mark and Sanctuary Peal replace spread and stack | Solo variants scheduled; no team requirement appears |
| C2 | S | 2–4 | Every living player is marked for Tolling Mark; one stack with `min_players = n` | Table in spec §6 |
| C3 | S | 5, 7 | Odd counts: `ceil(n/2)` marks; stacks at `floor(n/2)`; subgroup sizes 3/2 and 4/3 | Matches the table; the spare player can satisfy either stack |
| C4 | S | 6, 8 | Even tier B | Matches the table |
| C5 | S | 2–8 | Duplicate-class rosters (all Vanguard, and later all Arcanist) | No assignment references class; clear is possible |
| C6 | S | 5–8 | Tier is fixed at attempt start; knocking out players never switches tier mid-fight | Tier unchanged; per-mechanic counts follow living players |
| C7 | H | 1, 2, 4, 8 | Play the full fight | Notes on fairness and readability per count (plan §11: fairness in smaller parties) |

## 8. Knockout, disconnect and reassignment

| ID | Kind | Check | Pass |
|---|---|---|---|
| K1 | S | Mark target knocked out before lock, with ≥ 107 ticks left | Reassigned to a living unmarked player; `assignment_changed` logged (needs R4; until then, cancellation is logged and the row records the fallback) |
| K2 | S | Same, with < 107 ticks left | Mark cancelled; logged |
| K3 | S | Stack target knocked out before lock (≥ 127 / < 127 ticks) | Reassigned within the subgroup / cancelled; nobody fails a cancelled stack |
| K4 | S | Tracking Sweep target knocked out before lock; every other player is also down except one | Retargets; with no living target left, locks its current direction |
| K5 | S | Knockout after lock under-fills a stack | `min_players` capped per spec §6; the event records `locked_min` and the capped value |
| K6 | S | A player marked inactive by the session mid-mark | Treated as unavailable; never chosen for new targets |
| K7 | S | Knocked-out players receive no resolutions | No events with their `player_id` after the knockout tick |
| K8 | H | 8-player session: disconnect one player during a Tolling Mark | Remaining players understand the change from the visible cue and the failure card |

Rows K6 and K8 need the session lane (#7) and are reviewed there.

## 9. Eight-player effect density

| ID | Kind | Check | Pass |
|---|---|---|---|
| E1 | S | Eight-actor fixture during Tolling Mark + Remembered Sweep (row 16) and Gathering Peal + Remembered Sweep (row 19) | Rendered capture produced; all mark boundaries drawn; nothing occludes a hazard edge |
| E2 | H | The same fixtures at 100% and 50% screenshot size, plus grayscale | Testers find their own mark, the Echo region and a safe spot within about a second |
| E3 | H | Reduced-effects setting on | Mechanic information is unchanged (art guide §12.4) |
| E4 | H | Memory mark vs live warning | Nobody mistakes the memory mark for a live hazard, including in grayscale |

## 10. Recorded outcomes

| ID | Kind | Check | Pass |
|---|---|---|---|
| O1 | S | Every resolved event has the spec §9 fields for its requirement kind | Schema check passes; missing fields fail the test |
| O2 | S | The failure card is built from event data, not live positions: move every actor after resolve, then render the card | The card still shows the resolve-time positions |
| O3 | S | Echo provenance | The Echo event's `source_attack_id` and source tick match the recorded sweep |
| O4 | S | Event deduplication across repeated snapshots (128-event window) with eight players | Each failure is shown once per `generation` and `event_id` |
| O5 | H | Compare a tester's explanation of a failure with the event log | Matches in most observed failures; mismatches are logged as readability bugs |

## Evidence template for PRs

```
Seeds and counts run:
Scripted rows run (IDs) and results:
Human rows run (IDs), testers, hardware, capture links:
Rows not run and why:
Unity editor and package versions:
```
