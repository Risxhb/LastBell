# Encounter test matrix — Bellkeeper

Draft v0.3 · October 21, 2026 · Owner: `chzy18/claude` ([#8](https://github.com/Risxhb/collab/issues/8))

This matrix covers the [Bellkeeper specification](../design/bellkeeper.md) v0.3, which adds the Unfinished Hour (spec §3.1) at every tier. It replaces v0.1's Guard, immunity and upgrade rows, which the current design (#29, PR #33) made obsolete. Rows become C# tests or manual checklists once the shared contract (#6 U3) and the encounter implementation exist. **Nothing in this matrix has been run**, except the single-attack escape sampling in §3 (row T2).

Two kinds of row, kept separate:

- **Scripted (S):** an automated C# check with a fixed seed. Evidence is a test result in the PR.
- **Human (H):** people at the gameplay camera, on shipping-like visuals, with a physical controller where noted. Evidence is tester notes plus captures. A scripted result never substitutes for a human row.

Browser and Godot prototypes validate only themselves, not Unity.

**Standard seed set:** 1, 2, 3, 17, 404, 9001, at every party count 1–8 **and** every difficulty tier, unless a row says otherwise. Counts and tiers are independent axes: run the full grid.

## 1. Geometry and units

| ID | Kind | Check | Pass |
|---|---|---|---|
| G1 | S | Actor centre exactly on each expanded boundary: cone edges and arc, ring inner and outer, lane sides and ends, Chime circle | Counted as inside (touching counts) |
| G2 | S | 0.05 m outside each boundary | Outside |
| G3 | S | Cone angles near ±40° and directions crossing ±180° | No wrap-around error |
| G4 | S | Toll inner pocket: player centre 1.7–2.3 m from the boss | Safe; boss-body push-out never moves the player into the ring |
| G5 | S | Floor warning mesh vs shared geometry: 1,000 points per shape | Agreement within the documented tessellation tolerance |
| G6 | S | Units: shared movement and shape constants match spec §2, or the spec has been re-run with the new values (R21) | No hard-coded copies in encounter code |
| G7 | H | At the gameplay camera, stand on each visible edge and predict hit or safe | At least 9 of 10 predictions correct; misreads logged |

## 2. Tracking, lock and the moving boss

| ID | Kind | Check | Pass |
|---|---|---|---|
| L1 | S | Bell Sweep tracks for 18 ticks, then facing and cone never change | Frozen from the lock tick |
| L2 | S | Chime marker follows its owner at ≤ 6 m/s for 30 ticks, then stays | Frozen from lock; it never jumps |
| L3 | S | Charge lane locked at warning; boss travels it and ends at its far end | Lane fixed; the end is clamped to the boss bound |
| L4 | S | Hollow Toll after a Charge uses the landing position | The ring centre equals the boss position on its warning tick |
| L5 | S | Seeded variation fallback: force an invalid sample | A pre-validated fallback is chosen before the tell; geometry never changes mid-warning |
| L6 | H | Tracking-to-lock cue on the Sweep and Chime noticed with audio muted | Testers point to the lock moment |
| L7 | H | After a Charge, testers predict where the next Toll is centred | Correct from the landing marker |

## 3. Escape, stamina and timing

| ID | Kind | Check | Pass |
|---|---|---|---|
| T1 | S | Schedule generation per phase, block, tier and count | Matches spec §4 and §5.2; identical across two runs of a seed |
| T2 | S | Single-attack escape check (spec §5.1). **Done for the spec:** `tools/encounter-escape/escape-check.ps1`, October 21 | Every window ≥ its tier minimum. The 8 m Sweep fails at the current 39 ticks; the 6.5 m Sweep passes. Production must re-run with shared constants |
| T3 | S | Combined and sequential reachability: Block A/B/C, the echo steps, The Final Toll's March echo + Sweep pairing (union sampled in `results-2026-10-21-v0.3.md`), tier B double Sweep with an echo | A safe sequence exists from the worst start, at every count and tier |
| T4 | S | Stamina rule: at most one required dodge per 2.25 s, and three in a row only after 5 s with none; simulated through Final Toll → Pursuit → Final Toll ×3 | Worst-case stamina never needs to go below 0; sprint and jump never required |
| T5 | S | First Chime walk-only: every attack and sequence solvable with no dodge | Pass at the 0.60 s margin |
| T6 | S | At most two damaging patterns warning at once (a Chime volley counts as one) | Validator rejects a deliberately broken schedule |
| T7 | S | Phase threshold crossed mid-pattern | Transition waits for the pattern or block to finish; no warning is interrupted |
| T8 | H | New players clear Phase 1 at Steady Ticking without explanation | Misunderstood warnings logged |
| T9 | H | Stamina feel: testers never feel forced into a dodge they couldn't afford | Notes per tier |

## 4. Collision, knockback and hit-stun

| ID | Kind | Check | Pass |
|---|---|---|---|
| C1 | S | Boss body push-out during idle, turns and charge travel | Players never overlap the body; never pushed out of bounds |
| C2 | S | Charge landing near the wall with a player between the boss and the wall | The player fits (2.0 m gap); not pinned into the next Toll without an escape |
| C3 | S | Knockback 1.5 m away from the origin, stopping at the wall and boss body | Correct direction per shape; no wall damage |
| C4 | S | Anti-chain: hit by Chime marker 3, then the Final Toll Sweep (and every validator-flagged pair) | `stun + escape ≤ gap` from every knocked-back position |
| C5 | S | Per-instance dedup: one hazard can't hit a player twice; two distinct markers can | Exactly one damage event per instance per player |
| C6 | S | Jumping inside a Bellkeeper hazard | Still hit; jump state recorded in the event |
| C7 | H | Hit-stun and knockback read as fair, not stun-locking | Notes; any chain feeling unavoidable is logged |

## 5. Outcomes and records

| ID | Kind | Check | Pass |
|---|---|---|---|
| R1 | S | Inside at resolve vs outside | `hit` with damage, stun and knockback / `avoided`. No `guarded` or `immune` outcome exists |
| R2 | S | Dodging through a hazard at resolve | `hit`; dodge state recorded (no iframes) |
| R3 | S | Every record has the spec §12 fields | Schema check passes |
| R4 | S | The failure card is built from resolve-time data: move all actors after resolve, then render the card | The card shows resolve-time positions |
| R5 | S | Unfinished Hour provenance | `source_attack_id`, `source_instance_id` and recorded tick match the recorded Sweep, Toll or March; the echo resolves at the recorded world position, not the boss's current one |
| R6 | S | Event dedup across repeated snapshots with 8 players | Each outcome is shown once per generation and event ID |
| R7 | H | Testers explain each failure from the card (where safety was, why they were hit) | Matches the event log in most cases; mismatches are readability bugs |

## 6. Party counts and assignments

Run T1, T3 and R1–R3 at each count. These are in addition:

| ID | Kind | Count | Check | Pass |
|---|---|---|---|---|
| P1 | S | 1 | One cone, one lane at the player; 3 sequential Chime markers (2 on First Chime) | Spec §6 and §10 |
| P2 | S | 2–4 | One marked Sweep target; rotating Charge target; 2 markers per standing player | Every player is the Charge target within n Charges |
| P3 | S | 5, 7 | Odd tier B: subgroups 3/2 and 4/3; `ceil(n/2)` Chime owners; two Sweeps from different subgroups | Matches the table |
| P4 | S | 6, 8 | Even tier B | Matches the table |
| P5 | S | 5–8 | Double Sweep union across the seed set | Reachable; any failing seed logged and the stagger widened |
| P6 | S | 2–8 | Duplicate-class rosters | No class-dependent assignment; clear possible |
| P7 | S | 5–8 | Tier fixed at start; downs don't change it | Counts follow standing players |
| P8 | H | 1, 2, 4, 8 | Full fight per count at Steady Ticking | Fairness and readability notes per count |

## 7. Down, revive and disconnect

| ID | Kind | Check | Pass |
|---|---|---|---|
| K1 | S | Sweep target downed before lock | Retargeted to a seeded standing player; `assignment_changed` |
| K2 | S | Chime owner downed before lock | Marker cancelled, not moved; event logged |
| K3 | S | Target downed after lock | Hazard resolves at its locked footprint; can hit others |
| K4 | S | Downed players | Not targeted, not hit, hold no assignments |
| K5 | S | Player marked inactive by the session mid-volley | Treated as downed for targeting |
| K6 | S | Revive channel during a resolving hazard | A hit cancels the channel; channel time escalates per that player's downs |
| K7 | S | D4 extra recovery (if adopted) | +1.5 s at the end of a Phase 2 block only while someone is downed, only on tiers 1–2 |
| K8 | S | Last player downs on the same tick the boss dies | Defeat takes precedence, deterministically |
| K9 | H | 8 players: one disconnects during a Chime volley | Others understand the cancelled marker from the visible cue and the card |

K5 and K9 need the session lane (#7, currently on hold) and are reviewed there.

## 8. Reset: practice vs fresh run

| ID | Kind | Check | Pass |
|---|---|---|---|
| X1 | S | Practice start and retry mid-phase, mid-Final Toll, with an inactive memory outline and with an active echo | Health, stamina, cooldowns, boss HP and position, phase, block index, RNG, assignments and memory mark all match a fresh practice start |
| X2 | S | Fresh run after a Bellkeeper defeat | Restarts at encounter 1; down counts cleared; the Bellkeeper seed is derived from run seed + encounter index |
| X3 | S | Practice never unlocks run progress | Run state is unchanged after a practice clear |
| X4 | S | Boss defeat with hazards pending | All cancelled; no damage after victory; no stale markers |
| X5 | H | Retry under 5 s with no stale warnings, marks or feedback | Stopwatch result and screenshot |

## 9. Difficulty tiers

| ID | Kind | Check | Pass |
|---|---|---|---|
| D1 | S | Window tables per tier (spec §5.2) loaded from data | Each tier ≥ its minimum; First Chime walk-only |
| D2 | S | Striking Hour: Chime/Sweep overlap and no Pursuit between Final Tolls | T3 and T4 still pass |
| D3 | S | Unfinished Hour at every tier: records per the spec §4 rotation, shows the inactive outline, activates, resolves once, erases; Midnight Toll's Borrowed Second pauses 60 ticks without resolving | Validator rejects two memories, an echo without a recording, an echo that follows the boss, or a resolve during the pause |
| D4 | H | Players can tell the tiers apart by mechanics, not just damage | Notes per tier |
| D5 | H | Inactive memory outline vs active echo vs live warning, including grayscale and reduced effects | Never mistaken for a live hazard |

## 10. Eight-player density

| ID | Kind | Check | Pass |
|---|---|---|---|
| E1 | S | 8-actor capture during Block B and The Final Toll's Chime step | All boundaries drawn; nothing occludes a hazard edge |
| E2 | H | The same at 100 % and 50 % size, and in grayscale | Testers find their own markers, the cone and a safe spot within about a second |
| E3 | H | Reduced-effects setting on | Mechanic information unchanged |

## Evidence template for PRs

```
Seeds, counts and tiers run:
Scripted rows run (IDs) and results:
Human rows run (IDs), testers, hardware, capture links:
Rows not run and why:
Unity editor/package versions; movement constants used:
```
