# Dial Warden, Keeper of the Missing Minute — encounter specification

Draft v0.1 · October 21, 2026 · Lane: Encounters / minibosses ([#8](https://github.com/Risxhb/collab/issues/8)) · Source packet: [nine-miniboss handoff, PR #49](https://github.com/Risxhb/collab/pull/49) (`docs/nine-mini-bosses-20261021`, commit `a2ab311`, `docs/mini-bosses/design.md` §1).

Status: **specification only, nothing here is implemented.** It targets the Unity/C# run loop ([PR #42](https://github.com/Risxhb/collab/pull/42)), where the Dial Warden is already miniboss 1 as a labelled stand-in (`Run/Encounters/Minibosses.cs`, `miniboss.dial_warden`). It follows the structure and method of the [Bellkeeper spec](https://github.com/Risxhb/collab/blob/docs/bellkeeper-spec/docs/design/bellkeeper.md) ([PR #16](https://github.com/Risxhb/collab/pull/16)) and the [Sylvara spec](https://github.com/Risxhb/collab/blob/docs/sylvara-spec/docs/design/sylvara.md) ([PR #50](https://github.com/Risxhb/collab/pull/50)).

The Dial Warden is the **first miniboss of Skyward Spire** and the first encounter of a fresh run. The packet's identity, abilities, geometry, timings and damage are taken as given. Where the packet disagrees with numbers the run loop already uses (hit-stun, knockback, body size, arena), **the current game wins**, as decided for the Bellkeeper. This spec adds what the packet leaves open: ticks, schedules, targeting, party scaling, escape evidence and the decisions that still need a human.

Reference sheet: `docs/mini-bosses/01-skyward-spire.png` on the packet branch, row 1 (Phase 1 left, Phase 2 right).

Companion tool: [`tools/encounter-escape/dial-warden-escape-check.ps1`](../../tools/encounter-escape/dial-warden-escape-check.ps1).

## 1. Rules this encounter follows

From the [current game design](https://github.com/Risxhb/collab/blob/docs/boss-rush-decisions/docs/game-design.md), the run loop and the packet's shared miniboss contract. Where they disagree, the order is: human decisions, then the run loop's current values, then the packet.

- **No invincibility of any kind.** Dash gives no immunity, and there is no post-hit immunity. Damage is avoided only by leaving the footprint. Each attack instance hits a player at most once.
- **No mandatory healer, block, parry, upgrade or dash.** Vanguard Brace is the approved partial frontal mitigation and works normally here, but no pattern assumes it. Any mix of Vanguard, Arcanist and Chimesinger, including duplicates, can clear.
- **The dial is not a shield.** The boss never blocks or reflects player attacks or projectiles from any side.
- **Finite stamina governs routes** (§5.3). Attacks never consume it.
- **No attack can be jumped.** Jump state never changes resolution.
- **Health:** a fresh run starts full. The Dial Warden is the run's first encounter, so players arrive with full health and potions. After it, health refills and spent potions stay spent until the area's final boss.
- **Boss stays targetable** through the phase change. Arcanist seals, Chimesinger bells and other player setup persist with their timers running.
- **Damage geometry is the telegraph.** Hits are checked against authoritative shared geometry, never animation or VFX. The gold escapement glow, dial ticks and decorative arcs never deal damage.
- **Locks are final.** Nothing retargets after lock.
- **One damaging pattern at a time** in the baseline (packet rule). Striking Hour adds one tested overlap (§9).
- **No contact damage.** The body collider blocks movement only.

## 2. Baseline values

**Units are metres and seconds; the simulation runs at 60 Hz.**

| Value | Used here | Source |
|---|---|---|
| Walk | 6 m/s (gate) and 4.2 m/s (also checked) | Run loop `PlayerController.WalkSpeed`; older prototype #27 |
| Sprint | 9 m/s, never required | Game design |
| Dash | 3 m over 0.20 s, 30 stamina, no immunity | Game design |
| Stamina | 100; dash 30, jump 20, sprint 25/s; regenerates 20/s after 0.75 s | Game design |
| Player hit margin | 0.3 m; touching a border counts as inside | Run loop `Hazard.Margin` |
| Hit-stun and knockback | **15 ticks (0.25 s) and 1.5 m away from the boss** **[packet: ≤ 0.15 s, no knockback]** | Run loop `Hazard` defaults (Bellkeeper v0.2 §5) |
| Player health | 100 HP | Packet and run loop |
| Boss body | **1.4 m radius** collision circle (2.8 m across) | Run loop stand-in `BodyRadius` |
| Boss height | 2.8 m against a 1.8 m hero | Packet |
| Arena | Player centres ≤ 11.5 m from the centre; boss centre ≤ **7.0 m** | Run loop `CombatWorld.PlayerBound` (key-art arena, the packet's 24 m floor); packet's 3 m clearance around the body |

**Reach convention (packet rule):** listed reaches are measured from the body boundary. Every cone, ring, disc and lane below starts at the boss centre with radius **1.4 m + listed reach**.

## 3. Attack vocabulary

Six attacks: three in Phase 1, two evolved plus one new in Phase 2, and Minute Pin in both.

**Resolve model.** The run loop resolves each hazard on a single tick. The packet's active durations (0.2–0.35 s) become a non-damaging follow-through (the blade completing its arc), placed before recovery so the packet's total timing is unchanged. A player who walks into a footprint during the follow-through is not hit (D3).

| ID | Attack | Shape (from boss centre) | Targeting | Tracking and lock | Resolve (ticks from warning) | Follow-through | Recovery | Cooldown | Damage |
|---|---|---|---|---|---|---|---|---|---|
| `hand_sweep` | **Hand Sweep** | Cone, 100° wide, radius 5.4 m | Nearest standing player | Facing locks at warning | 84 | 15 | 120 (2.0 s) | 300 (5 s) | 18 |
| `hollow_dial` | **Hollow Dial** | Ring, 3.9–6.4 m (2.5–5 m from the body); inside is safe | Centred on the boss | Fixed at warning | 108 | 18 | 156 (2.6 s) | 540 (9 s) | 18 |
| `minute_pin` | **Minute Pin** | Lane, 1.5 m wide, 11.4 m long, clipped by the wall | Rotating target (§6) | Body turns toward the target at 90°/s for 36 ticks, then locks | 90 (54 after lock) | 12 | 168 (2.8 s) | 420 (7 s) | 26 |
| `offset_sweep` | **Offset Sweep** (P2, replaces Hand Sweep) | Beat I: Hand Sweep's cone. Beat II: cone 80° wide, radius 5.4 m, centred 70° to one side of beat I | Nearest standing player; side of beat II seeded | Both cones lock at beat I's warning; beat II is shown as a numbered outline from tick 0 | I: 84. II warns at 99, resolves at **177 (78 after warning; packet 72, D4)** | 15 each | 192 (3.2 s) after beat II | 360 (6 s) | 18 per beat |
| `unwound_dial` | **Unwound Dial** (P2, replaces Hollow Dial) | Ring as Hollow Dial, then a disc of radius **3.9 m (2.5 m from the body; packet 2 m, D2)** | Centred on the boss | Fixed at warning | Ring: 108. Disc warns at 126, resolves at 210 (84 after warning) | 18 each | 180 (3.0 s) after the disc | 600 (10 s) | 18 per beat |
| `stolen_quarter` | **Stolen Quarter** (P2, new) | Cone, 60° wide, radius 8.4 m | Rotating target within reach (§6) | Turns toward the target at 90°/s for 36 ticks, then locks **72 ticks before resolve (packet 60, D1)** | 108 | 21 | 216 (3.6 s) | 720 (12 s) | 26 |

**Per-attack notes** (behaviour from the packet, made exact):

- **Hand Sweep.** The clock hand lifts horizontally; two audible ticks; a coral cone draws and latches. One stroke, no backswing. Melee circles to the unmarked flank; ranged steps beyond 5.4 m or out of the angle. The 2 s recovery is the flank or cast opening.
- **Hollow Dial.** The core contracts with a low toll. The ring has a hatched band and an unhatched centre, so the safe inner region reads without colour. Melee stays close; anyone in the band steps to its nearer edge, inner or outer. Never dash *through* the band while it is live.
- **Minute Pin.** The blade points at the target with a rising tick that sharpens at lock. A pin marker sits over the target until lock, then the lane latches and stops following. Sidestep toward the boss's shoulder or move perpendicular. The 2.8 s blade-extraction is a stationary opening.
- **Offset Sweep.** Beat II's outline is numbered II and visible from the start, so the **common safe flank** (outside both cones) is readable before anything moves. Beat II gets a fresh warning with the opposite tick pitch once beat I's follow-through ends. Beat II's side is seeded per use, never read from player input.
- **Unwound Dial.** The ring resolves exactly like Hollow Dial. After its follow-through, the disc warns with inward-pointing chevrons over the whole former refuge. Melee waits at the refuge's outer edge, then walks outward over the spent ring; ranged waits outside, then may walk in after the disc. The 3 s recovery follows the disc.
- **Stolen Quarter.** A quarter of the face opens with a loud ratchet; the sector tracks briefly, latches, and the hand jams. Melee steps behind the adjacent shoulder; ranged crosses the nearest sector edge. The 3.6 s jammed recovery exposes the escapement: this is the fight's major opening.

## 4. Phases and timeline

| Phase | Name | Boss HP | Visual | What it teaches |
|---|---|---|---|---|
| 1 | Measured Duty | 100–50 % | Closed ivory dial, folded blade | Read the facing; each attack alone |
| 2 | The Slipped Minute | 50–0 % | Dial split into two offset plates showing the gold escapement; blade unfolded at the elbow | Two-beat attacks: find the common safe space, then move again |

**Shared phase rules:**

- **Queued transition.** Crossing 50 % queues the transition until the current attack's recovery begins and its hazards are spent. Then a **120-tick (2 s) non-damaging transformation**: the dial plates slide apart, the blade unfolds. The boss stays targetable; no heal, no new health bar.
- **One transition only.** A burst that crosses 50 % queues it once. A lethal burst ends the encounter immediately.
- **Defeat** cancels every warning and outline at once; nothing can land after victory. Defeat pose: the hand drops to six o'clock, the plates close, the core dims to a small steady glow.
- **Cooldown hold.** If an attack's cooldown isn't ready when its turn comes, the boss holds (and may reposition, below) until it is. Windows are never shortened to fit a cooldown.
- **Reposition step** between attacks, 0–36 ticks, after recovery ends:
  - If the next target is farther than 5.4 m from the body, the boss walks toward it at 1.6 m/s (the stand-in's pace).
  - If the next attack's direction is more than 60° off the current facing, the boss plays a **planted reorientation**: two heavy steps and a ratchet, turning up to 180° over 36 ticks. Non-damaging and fully visible. This is the answer to rear camping: never an instant rear hit.
  - Outside attacks the boss turns at most 90°/s. It never turns during a committed swing.
- **Seeded variation** (Pin and Quarter target rotation, beat II side) is chosen at the attack's start from the encounter RNG (R6).

### 4.1 Phase 1 — Measured Duty (fixed loop, ≈ 13.7 s plus reposition steps)

Hand Sweep → Minute Pin → Hollow Dial → repeat.

| Attack | Total ticks | Cooldown gap in the loop |
|---|---|---|
| Hand Sweep | 219 | 552 (CD 300) |
| Minute Pin | 270 | 501 (CD 420) |
| Hollow Dial | 282 | 489 → **51-tick hold** (CD 540) |

The first loop is the teaching loop: each attack appears once, alone, and the Pin → Dial join is the packet's example sequence.

### 4.2 Phase 2 — The Slipped Minute (fixed loop, ≈ 23.5 s plus reposition steps)

Offset Sweep → Unwound Dial → Minute Pin → Stolen Quarter → repeat.

| Attack | Total ticks | Cooldown gap in the loop |
|---|---|---|
| Offset Sweep | 384 | 1023 (CD 360) |
| Unwound Dial | 408 | 999 (CD 600) |
| Minute Pin | 270 | 1137 (CD 420) |
| Stolen Quarter | 345 | 1062 (CD 720) |

This matches the packet's example: Offset Sweep → punish after beat II → Unwound Dial → move outward after the ring → 3 s window. Every join is strictly sequential, so nothing overlaps in the baseline.

## 5. Fairness: escape, stamina, collision and hit-stun

### 5.1 Method

Same as the Bellkeeper's §5.1 and Sylvara's §5.1. `dial-warden-escape-check.ps1`:
- samples every boss position in the bound (0–7 m, by symmetry) and every direction in 10° steps
- finds the reachable danger point farthest from safety (0.1 m danger grid, 0.05 m boundary grid; shapes expanded by 0.3 m; player centres inside the arena and outside the boss body)
- converts that into walk-only and walk-plus-one-dash escape times, then into the minimum window per difficulty

Reaction margins: First Chime 0.60 s (walk only), Steady Ticking 0.45, Striking Hour 0.35, Midnight Toll 0.30.

Two-beat attacks are checked beat by beat: beat II alone once beat I has cleared, and the disc alone once the ring has cleared.

**Limits:** straight-line travel, no other players, no acceleration, constant-speed dash, grid accuracy about ±0.05 m. This is evidence for single attacks, not proof that every combination is solvable.

### 5.2 Results (run on October 21, 2026, key-art arena bound)

| Attack | d_max | Worst configuration |
|---|---|---|
| Hand Sweep | 3.75 m | Boss at the bound, facing the wall |
| Offset Sweep, beat II | 3.19 m | Boss at the bound, facing the wall |
| Hollow Dial ring | 2.57 m | Boss 5 m from the centre |
| Unwound disc, 2 m (packet) | 2.01 m | Boss 5 m from the centre |
| Unwound disc, 2.5 m (spec) | 2.50 m | Boss at the centre |
| Minute Pin | 1.20 m | Sideways out of the lane |
| Stolen Quarter | **4.12 m** | Boss 4 m from the centre, sector 20° off the outward radial: the wall cuts off the outward exit, so the player must cross a side edge |

Minimum window in ticks, counted after lock (Pin, Quarter) or from warning (the others), against this spec's Steady Ticking window:

| Attack | First Chime, walk 6 / 4.2 | Steady Ticking, walk only 6 / 4.2 | Steady Ticking with one dash, 6 / 4.2 | Striking Hour | Midnight Toll | Spec window |
|---|---|---|---|---|---|---|
| Hand Sweep | 74 / 90 | 65 / 81 | 47 / 50 | 41 | 38 | 84 |
| Offset Sweep, beat II | 68 / 82 | 59 / 73 | 41 / 42 | 35 | 32 | 78 (packet 72) |
| Hollow Dial ring | 62 / 73 | 53 / 64 | 38 / 38 | 32 | 29 | 108 |
| Unwound disc (2.5 m) | 61 / 72 | 52 / 63 | 37 / 37 | 31 | 28 | 84 |
| Minute Pin | 48 / 54 | 39 / 45 | 32 / 32 | 26 | 23 | 54 |
| Stolen Quarter | 78 / 95 | 69 / 86 | 51 / 55 | 45 | 42 | 72 (packet 60) |

**Findings:**
- **Stolen Quarter as written fails the packet's own rule.** Its 60 ticks after lock aren't walk-solvable even at 6 m/s (69 needed), so the packet's "no pattern requires stamina" breaks. Locking 72 ticks before resolve (D1) makes it walk-solvable at 6 m/s; the aim still tracks for the first 36 ticks, so the "read the sector lock" lesson stays. At 4.2 m/s it needs one dash (55).
- **Offset Sweep beat II is one tick short** at 4.2 m/s walk-only (73 against 72). D4 adds 6 ticks. In practice the common safe flank is visible from tick 0, so only a player who walked into beat II's outline after beat I cleared is affected.
- **Everything else in the packet is walk-solvable at Steady Ticking at both speeds.**
- **Hand Sweep is tightest at First Chime:** 4.2 m/s walkers need 90, so First Chime warns 96 (§9).

### 5.3 Stamina budget

Same rule as the Bellkeeper and Sylvara: a worst-case player may need **at most one dash per 2.25 s**, and at most three in a row after 5 s without one. Sprint and jump are never required.

- **Design walk (6 m/s, the run loop's value):** the whole Steady Ticking baseline is walk-solvable. No dash is ever required.
- **Prototype walk (4.2 m/s):** only Stolen Quarter needs one dash, once per 23.5 s Phase 2 loop.

### 5.4 Collision, body and hit-stun

- **Body collider: a 1.4 m circle**, planted. It pushes players aside and never deals damage. The packet's 3 m traversal ring around the body is kept by the 7 m boss bound, so no one can be pinned against the wall.
- **Hit-stun and knockback (current game values):** 15 ticks and 1.5 m directly away from the boss centre. Radial knockback carries a player *out of* the Unwound ring area and away from the disc, never toward the boss.
- **Anti-chain rule.** For any two hazards that can hit the same player, the later resolve must come at least `stun + reaction + escape` after the earlier one (stricter than Sylvara's `stun + escape`, because knockback can leave the player inside the next footprint):
  - Offset Sweep I → II: 93 ticks apart; worst need 15 + 27 + 32 = 74 at 6 m/s. Passes.
  - Unwound ring → disc: 102 ticks apart; need 15 + 27 + 25 = 67. Passes.
  - Striking Hour Unwound disc → Minute Pin: 60 ticks apart; need 15 + 21 + 12 = 48. Passes (§9).
- **One hit per instance** (R16). Each beat is its own instance, so failing both beats of Offset Sweep costs 36 HP.

## 6. Party size (1–8)

Same geometry and timings for every party size. Counts use standing, active players at each warning.

| Attack | 1 | 2–4 | 5–8 |
|---|---|---|---|
| Hand Sweep / Offset Sweep | 1 cone at the nearest player | Same | Same |
| Hollow / Unwound Dial | Centred on the boss | Same | Same |
| Minute Pin | 1 pin | 1 pin, rotating | 1 pin, rotating |
| Stolen Quarter | 1 sector | 1 sector, rotating | 1 sector, rotating |

**Minute Pin is one pin at every party size (D5).** The packet's 1/2/3 rule covers *targeted markers* (circles on players). The pin is a directional attack from the boss's one blade, like Sylvara's Briar Procession, so it doesn't multiply. That keeps the sheet's single blade and the one-pattern-at-a-time rule.

**Targeting rules:**
- **Nearest** is measured to the boss centre at warning; ties go to the lowest stable player ID.
- **Rotation** is round-robin by stable player ID from a seeded offset. No player is pinned twice in a row if another standing player exists, so a lone caster isn't suppressed every time.
- **Stolen Quarter** rotates among standing players within its 8.4 m reach. If none is in reach, the reposition step advances toward the nearest player (up to 36 ticks), then aims at the nearest. Ranged players parked beyond 8.4 m must still read Minute Pin (11.4 m).
- **Marked before lock.** Pin and Quarter targets get a shape over the player and on the HUD, not colour alone.
- **Duplicate classes.** No assignment depends on class.
- **Rear camping in a group** is fine: someone is always behind a drum. The reorientation step only answers the *next target's* direction.

## 7. Down, revive and disconnect

The same as the Bellkeeper's §7:
- A downed Pin or Quarter target before lock: a new target is chosen.
- After lock, every hazard is world geometry and resolves normally.
- Downed and inactive players hold no assignments and aren't hit.
- Disconnect is treated as downed.

Every change emits `assignment_changed`.

**Practice** starts the Dial Warden at full health with full potions, in Phase 1, from the practice seed. **Fresh run** resets everything.

## 8. Damage and boss HP

Damage at Steady Ticking, the packet's normalized values, which already sit on the run loop's scale (stand-in Hand Sweep 20, Hollow Dial 18):

| Attack | Damage |
|---|---|
| Hand Sweep, Hollow Dial, each beat of Offset Sweep and Unwound Dial | 18 |
| Minute Pin, Stolen Quarter | 26 |

The worst single mistake is 26 HP; failing both beats of a two-beat attack is 36. First Chime ×0.8, Striking Hour ×1.0, Midnight Toll ×1.2.

**Boss HP:** keep the stand-in's **1500** until measured. Then HP = measured reference sustained DPS × 120 s (solo), measured during movement and recovery, not on a stationary dummy, against the packet's 90–150 s target. Co-op multiplier: `1 + 0.7 × (n − 1)`, applied to HP only (same as Sylvara).

## 9. Difficulty tiers

Mechanics change, not just numbers. Warnings are not uniformly sped up. The packet calls its design a "hard" fit for Striking Hour; this spec uses it as the **Steady Ticking baseline** like every other boss spec, because this is the run's first encounter (D6).

| Tier | Dial Warden changes |
|---|---|
| **1 — First Chime** | Hand Sweep warns 96. Offset beat II warns 84. Pin locks 60 before resolve (warning 96). Stolen Quarter locks at warning (108 after lock). All recoveries +0.5 s. Everything walk-solvable at 4.2 m/s |
| **2 — Steady Ticking** | The baseline above |
| **3 — Striking Hour** | One tested overlap: in Phase 2, **Minute Pin warns 54 ticks into Unwound Dial's disc warning** (tick 180), so it resolves 60 ticks after the disc. The pin tracks while players leave the refuge; the common safe route is "walk out over the spent ring, away from the pin". Recoveries −20 %, but Stolen Quarter, Offset Sweep and Unwound Dial keep at least 3.0 s (the packet's 3–4 s major opening) |
| **4 — Midnight Toll** | Striking Hour, plus a highest-tier mechanic still to be designed (D7) |

## 10. Requests to the shared C# contract (#6)

The run loop already has the cone, ring and lane hazards, per-tick tracking until lock, single-tick resolve, hit-stun, knockback and one hit per instance. The Dial Warden reuses the Bellkeeper's and Sylvara's numbered requests (R1 circle, R4 reassign/cancel before lock, R5 presentation markers, R6 seeded RNG, R10 lane, R12 HP phases, R14 recovery event, R16 one hit per instance, R18 standing state, R20 body collision, R25 cone). It adds:

| # | Need | Why |
|---|---|---|
| R26 | **Preview outlines:** a hazard can be shown (numbered, outlined, not hatched) before its warning starts, and can't damage in that state | Offset Sweep beat II is visible from tick 0 |
| R27 | **Boss turn-rate limit and a reorientation state** published to presentation and records | Slow pivots, the 36-tick planted turn, and truthful "it turned toward you" feedback |

## 11. Resolved records

Same fields as the Bellkeeper's §12, with `encounter_id` = `miniboss.dial_warden` (the run loop's ID). Additions:
- `beat` (I / II) for Offset Sweep and Unwound Dial
- `reoriented: true` when the attack followed a planted turn

Example card:

> *"Stolen Quarter (1:48, Phase 2). The sector locked 1.2 s before it struck, aimed at you. You were 1.1 m inside its left edge: step behind its shoulder."*

## 12. Model, animation and asset notes

From the packet and sheet, with gameplay constraints:

- **Scale: 2.8 m tall** against a 1.8 m hero (packet). The sheet draws the boss about three hero heights tall, which is too big; model to 2.8 m. The body collider is the drum footprint, 2.8 m across.
- **Left arm: a counterweight, not a shield.** The sheet's round disc on the left stabilizer reads as a buckler, which promises blocking that the game doesn't have. Make it a flat brass weight or a small pendulum bob, clearly not a guard.
- **Silhouette:** squat cobalt drum, ivory dial face, brass rim, small recessed ivory mask with a crown spike, two short planted legs, one long clock-hand blade on the right arm. Phase 2: the dial splits into two offset concentric plates exposing the gold escapement; the same blade unfolds at the elbow. No extra weapon, no floor damage in the transition.
- **The blade's length should match the sweep reach** (5.4 m from the centre) when extended, so the weapon tells the truth about the cone. The cone warning stays the authority.
- **Facing must read from the high isometric camera:** the dial face and the mask both point forward. The Phase 2 escapement glow is brightest during Stolen Quarter's jam (the opening), dim otherwise.
- **Rig:** blade arm (lift, sweep, point, unfold), counterweight arm, two legs with separate planting (no sliding), dial plates (P2 split and quarter-face opening), mask.
- **Animations:** idle (slow ticking), walk, planted reorientation (two heavy steps), Hand Sweep, Offset Sweep (two strokes), Hollow Dial (core contracts), Unwound Dial (ring, then chevron burst), Minute Pin (point, thrust, extraction), Stolen Quarter (quarter opens, sweep, jam), phase transformation (2 s), defeat (hand to six o'clock, plates close).
- **VFX:** coral/amber outlines with hatching and countdown marks for every hazard; numbered II outline for beat II; inward chevrons for the disc; turquoise never used for danger.
- **Audio:** two ticks (Hand Sweep), opposite tick pitch for beat II, low toll (Dial), rising tick sharpening at lock (Pin), loud ratchet then a jam clunk (Quarter), heavy two-step plus ratchet (reorientation). Every cue also has a visual, so the fight reads with audio off.

## 13. Validation tests

| # | Check |
|---|---|
| T1 | Escape check rerun on the real Skyward Spire collision and the final body collider, both movement sets |
| T2 | Both phase loops at every tier, with real collision, finite stamina, hit-stun and knockback from every hit position |
| T3 | Offset Sweep: every seeded beat II side leaves a common safe flank at least 2 m wide, including at the boss bound facing the wall |
| T4 | Parties of 1–8, including all-Vanguard, all-Arcanist and all-Chimesinger, with downs and disconnects mid-warning and mid-beat |
| T5 | Phase threshold crossed during each two-beat attack: both beats finish, then one 2 s transition |
| T6 | Rear camping solo: the reorientation always plays before an attack more than 60° off the facing, and never hits |
| T7 | Melee approach time after Stolen Quarter's recovery starts (packet's first tuning priority), per class |
| T8 | Audio-off and reduced-effects readability |

## 14. Human decisions needed

| # | Decision | Recommendation |
|---|---|---|
| D1 | Stolen Quarter lock: packet's 60 ticks before resolve fails walk-only even at 6 m/s | **Lock 72 ticks (1.2 s) before resolve**; still tracks for the first 36 |
| D2 | Unwound Dial disc: packet's 2 m leaves a 0.2 m sliver of "safe" refuge after the margin | **2.5 m, the whole former refuge**, so the lesson is clean: leave it |
| D3 | Active durations: run loop resolves on one tick | **Single-tick resolve; packet active time becomes follow-through**, total timing unchanged |
| D4 | Offset Sweep beat II warning | **78 ticks (1.3 s)**; packet 72 is one tick short at 4.2 m/s |
| D5 | Minute Pin in parties | **One pin, rotating target, at every size**; the pin isn't a targeted marker |
| D6 | Packet difficulty labelled "hard" | **Use as Steady Ticking**, like every other boss spec |
| D7 | Midnight Toll mechanic (packet gives none) | Leave open until Striking Hour is playtested |
| D8 | Hit-stun and knockback: packet's ≤ 0.15 s and none, or the run loop's 0.25 s and 1.5 m | **Run loop's values** (current game), anti-chain checked in §5.4 |
| D9 | Sheet scale and left-arm disc | **Model to 2.8 m; counterweight instead of a disc** |
