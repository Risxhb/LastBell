# Escapement Twins, The Two Who Agree — encounter specification

v0.2 (decisions accepted) · October 21, 2026 · Lane: mini-bosses / Brass Foundry · Source packet: Codex's nine mini-boss handoff, branch `docs/nine-mini-bosses-20261021` (commit `a2ab311`), `docs/mini-bosses/design.md` §6 and sheet `02-brass-foundry.png` (row 3).

Status: **specification only, nothing here is implemented.** It is engine-independent and targets the Unity/C# production path. It follows the structure of the [Sylvara spec](https://github.com/Risxhb/collab/pull/50) and the [Bellkeeper spec](https://github.com/Risxhb/collab/pull/16), so all three can share one validator and one set of contract requests.

The Escapement Twins are one of Brass Foundry's three mini-bosses ([#44](https://github.com/Risxhb/collab/issues/44)), so they are not part of the current one-arena three-plus-one slice. The packet's abilities, timings, damage and phases stand unless this spec says otherwise. Every change is listed in §1.1 with its reason. This spec fills in what the packet leaves open: exact geometry, the self-strike rule, targeting, schedules, party scaling, escape evidence and the decisions still needed.

Companion tool: [`tools/encounter-escape/escapement-twins-escape-check.ps1`](../../tools/encounter-escape/escapement-twins-escape-check.ps1) (about 7 s run).

## 1. Rules this encounter follows

These come from the [current game design](https://github.com/Risxhb/collab/blob/docs/boss-rush-decisions/docs/game-design.md) and the packet's shared mini-boss contract. Where they disagree, the game design wins.

- **Cursor aim, no lock-on.** Any part of the boss (either tower or the base) is a valid aim point. There is **no weak point to aim at**.
- **Cooldown-only boss attacks.** Players' movement stamina is separate and attacks never consume it.
- **No invincibility of any kind**: no block, parry, dash immunity or post-hit immunity. Damage is avoided only by leaving the footprint. Each attack instance hits a player at most once. Vanguard Brace may reduce frontal hits as approved; nothing here requires it.
- **No mandatory healer, block, upgrade, dash, sprint or jump.** Any mix of Vanguard, Arcanist and Chimesinger can clear, including duplicates.
- **Health and potions (mini-boss):** health refills before the fight; potions spent earlier in the area are not refilled.
- **Exactly two phases** (packet contract): Phase 1 at 100–50 %, Phase 2 at 50–0 %.
- **One damaging pattern at a time.** Multi-beat attacks retire the previous footprint before the next one resolves, and each beat has its own warning.
- **Damage geometry is the telegraph.** Hits are checked against shared authoritative geometry, never animation or VFX. **No contact damage.**
- **Locks are final.** Nothing retargets after lock.
- **Solo and 2–8 players use the same geometry and timings.** Only boss HP scales.

### 1.1 Changes from the packet

| # | Packet | This spec | Why |
|---|---|---|---|
| C1 | "Left Verdict", "Right Rebuttal" | **Sun Verdict**, **Moon Rebuttal**; the towers are the **Sun tower** and **Moon tower** | The boss rotates, and in co-op every player's "left" is different. The concept art already gives the faces a sun and a crescent moon |
| C2 | Phase 2 sender shown by "illuminated I" / "numbered face" | The sender's **face symbol lights** (sun or moon) and its pawl raises | The art has no numerals, and symbols read at a distance. No numerals are added |
| C3 | Combined collision width capped at 3.5 m | **Stadium body 5.2 m long × 2.2 m deep** (§2.1) | The art's plinth is about 5.7 m long at the stated 3.8 m tower height. Deadbeat's lanes plus a 2 m corridor need at least 3.5 m on their own. In an 11.5 m arena the larger body still leaves full flank circuits (8.9 m clear past each end, 10.4 m in front and behind) |
| C4 | Pawls "mounted near the base" in Phase 1; the art shows pawls only in Phase 2 | **Small Phase 1 pawls folded at either side of the ratchet**, extending in Phase 2 (§2.2) | Phase 1's stagger depends on them. Model correction M1 (§12) |
| C5 | Deadbeat "clear 2 m central corridor" | **2.6 m between the lanes**, which leaves 2.0 m for player centres | The contract requires 2 m *after* collision margins |
| C6 | "Rotates slowly between attacks" (rate and target unspecified) | Turns only in a **1.0 s gap** between attacks, at up to **40°/s**, toward the centroid of standing players. It never leaves the centre | Recovery forbids locomotion, so turning has to happen between attacks. A fixed centre keeps melee from chasing and means walls never clip an attack |
| C7 | Self-strike rule only partly specified | §3.2: the locked lane's **centreline** must pass within the opposite pawl's hit radius. The self-strike deals **no HP damage** | Exact and testable. No damage means there is no shared-HP double count |

## 2. Baseline values and body

**Units are metres and seconds; the simulation runs at 60 Hz.**

| Value | Used here | Source |
|---|---|---|
| Walk | 6 m/s (gate) and 4.2 m/s (also checked) | Game design; Unity prototype #42 |
| Sprint / dash | 9 m/s; 3 m over 0.20 s, 30 stamina, no immunity. Never required | Game design |
| Stamina | 100; dash 30, jump 20, sprint 25/s; regenerates 20/s after 0.75 s | Game design |
| Player hit margin | 0.3 m; touching a border counts as inside | Bellkeeper / Sylvara specs |
| Reaction margins | First Chime 0.60 s (walk only), Steady Ticking 0.45, Striking Hour 0.35, Midnight Toll 0.30 | Bellkeeper / Sylvara specs |
| Player health | 100 HP | Packet |
| Damage | Medium 18, heavy 26 (Steady Ticking) | Packet |
| Hit-stun | 9 ticks (0.15 s). **No knockback** | Packet |
| Arena | **Placeholder:** circle, player centres ≤ 11.5 m from the centre. The packet proposes a 24 m Brass Foundry disk, which is close | No Brass Foundry arena exists yet (D6) |

### 2.1 Body

Boss frame: the centre sits at the arena centre, **facing** is the direction the ivory faces look, and the long axis runs from the Sun tower to the Moon tower.

- **Collider:** a stadium (a rectangle with rounded ends). Its core segment runs 1.5 m along the long axis each way, with a 1.1 m radius around it, for **5.2 m long × 2.2 m deep** in total. It is the only body collision. Tracks, the bridge and the pawls never block movement.
- **Towers:** centres 1.6 m from the boss centre along the long axis, 3.8 m tall. They are **sender points** for lanes and cones. Their faces are the tells.
- **Hurtboxes:** both towers and the base are **one target with one health pool**. A player hit that touches several hurtboxes counts **once** (ET-R2).
- **Stationary.** The boss never translates. It only turns during the gaps between attacks (C6), so no attack can be clipped by the wall and no player can be pinned.

### 2.2 Pawls

Each pawl is a boss part, not an add. It is untargetable and takes no damage. It is a **hit circle** the Verdict lane checks against.

| Phase | Position (boss frame: x forward, y toward the Sun side) | Hit radius | Look |
|---|---|---|---|
| 1 | Sun pawl (1.3, +0.7), Moon pawl (1.3, −0.7): just in front of the ratchet, 0.2 m past the front edge | 0.5 m | Short brass catches folded down against the ratchet, readable from the isometric camera |
| 2 | (1.5, ±0.9), dropped from the extended bridge as on the sheet | 0.6 m | Full opposing hooked pawls, as on the sheet |

## 3. Attacks

The packet's W/A/R/CD are kept: warning to first damage / active duration / minimum recovery / cooldown (measured from the end of recovery to the next selection). Ticks are their exact conversion.

| ID | Attack | Phase | Shape | Targeting | Lock | Resolve (ticks from warning) | Active | Recovery | Cooldown | Damage |
|---|---|---|---|---|---|---|---|---|---|---|
| `sun_verdict` | **Sun Verdict** | 1 | Lane 1.5 m wide from the Sun tower centre, reaching 8 m past where it leaves the body | Rotating target (§6) | Tracks the target for 54 ticks, then **locks 60 ticks before resolve** | 114 | 15 | 156 (2.6 s); **240 (4.0 s) on a self-strike** | 420 (7 s) | 18 |
| `moon_rebuttal` | **Moon Rebuttal** | 1, 2 | Cone 100° wide, radius 5.1 m from the Moon tower centre (about 4 m past the body) | Nearest standing player | Facing fixed at warning start | 96 | 18 | 162 (2.7 s) | 360 (6 s) | 18 |
| `common_measure` | **Common Measure** | 1, 2 | Ring 2.5–5.5 m out from the body edge, following the stadium shape | None | Fixed | 120 | 18 | 180 (3.0 s) | 600 (10 s) | 26 |
| `alternating_verdict` | **Alternating Verdict** | 2 | As Sun Verdict, from **either** tower | Rotating target | As Sun Verdict | 114 | 15 | 168 (2.8 s); **240 (4.0 s) on a self-strike** | 420 (7 s) | 18 |
| `rebuttal_pair` | **Rebuttal Pair** | 2 | Cone 1: 100°, radius 5.1 m from one tower. Cone 2: 80°, radius 5.1 m from the other | Both at the nearest standing player at warning start | Both fixed at warning start | Cone 1 at 96; cone 2 at 204 | 18 each | 204 (3.4 s) after cone 2 | 540 (9 s) | 18 per cone |
| `deadbeat` | **Deadbeat** | 2 | Two parallel lanes 1.5 m wide, running forward 8 m from the front edge, 2.6 m apart (inner edges 1.3 m off the centre line) | Boss facing | Fixed at warning start | 120 | 18 | 216 (3.6 s) | 720 (12 s) | 26 |

Common Measure stays unchanged into Phase 2, as the contract requires.

### 3.1 Per-attack notes

- **Sun Verdict.** A single knock. The Sun face flashes and a dashed lane follows the target. At lock the lane latches solid with a countdown, 1 s before impact. The Sun tower swings and throws a striking line across the floor. The line passes over the body (the body is never floor, so this costs players nothing) and continues 8 m past it.
- **Moon Rebuttal.** The Moon face rises with a double knock, and the cone latches at once. One sweep, no backswing.
- **Common Measure.** Both pawls close visibly and a chord rings. The safe zones are inside 2.5 m (melee stays on the boss) or beyond 5.5 m.
- **Alternating Verdict.** At warning start the sender's face lights (sun or moon) and its pawl raises, so the direction can be read a full 1.9 s before impact. The sender comes from the encounter RNG (§4.3). Never two senders at once.
- **Rebuttal Pair.** Cone 1 comes from the tower that did *not* send the last Verdict, with a double knock. Cone 2's footprint is shown from the same moment as an **outline only**: no hatching and no countdown. When cone 1's active period ends, cone 2 switches to a full warning (hatched, double knock, countdown) and resolves **90 ticks later**. The lesson is to step into the space cone 1 has just left. Both cones aim at the same point, so cone 1's spent area is always partly outside cone 2.
- **Deadbeat.** The ratchet stops, both masks dim, and a triple clock click sounds. Two lanes are drawn straight ahead from the towers. The safe zones are the central corridor in front of the ratchet, anywhere outside the lanes, and beside or behind the boss.

### 3.2 Self-strike

The boss's one trick: a Verdict lane that passes over the **opposite** tower's pawl jams its own escapement.

- **Rule.** At resolve, if the locked lane's **centreline** passes within the opposite pawl's hit radius (§2.2), and the pawl lies ahead of the sender, the cast is a **self-strike**.
- **Effect.** That cast's recovery becomes **240 ticks (4.0 s)**. The ratchet sparks and stutters, both faces tilt down, and a sour double chord plays. The boss takes **no HP damage** from it. Players still take damage normally from the lane.
- **Limits.** At most one extended recovery per cast. The cooldown still runs from the end of the (extended) recovery and is never reset. Nothing else can trigger it, and Rebuttal, Common Measure and Deadbeat never interact with pawls.
- **Where to stand** (escape check §5.2). From the sender, the bait directions form a **wedge about 22° wide** (Phase 1) or **24° wide** (Phase 2), centred about 60° off the boss's facing toward the opposite tower. That is the **front corner on the receiving side** (Moon side in Phase 1). The bait area is about 30 m² and starts right against the body, so:
  - **Melee** stands at the front corner on the receiving side, lets the lane lock on them, and steps sideways (1.3 m at most) out of the lane.
  - **Ranged** stands far out along the same bearing. The lane reaches 8 m past the body, so ranged needs to sidestep only inside that reach. Beyond it they get the stagger for free, in exchange for having to hold that angle.
- **Why melee pays for it.** That corner is in reach of the next Moon Rebuttal, which aims at the nearest player. Melee must bait, sidestep, punish, then leave the receiving corner before the double knock. This is the packet's "without circling into the opposite cone".

## 4. Phases and timeline

**Shared rules:**

- **Gap.** After every recovery the boss has a 60-tick (1.0 s) gap, with no warning showing. It turns up to 40° toward the centroid of standing players. Facing is fixed for the whole attack after that.
- **Cooldown hold.** If an attack's cooldown isn't ready when its turn comes, the boss holds its gap pose until it is. Windows are never shortened.
- **Transition.** Crossing 50 % queues the transition until the current attack's last hazard expires. Then a **120-tick (2 s) harmless transformation** plays: the towers tilt apart, the bridge extends and the pawls unfold. The boss stays targetable. No healing, no new health bar. A lethal burst ends the fight immediately. A burst that crosses the threshold queues one transition only.
- **Defeat** cancels every warning at once; nothing lands after victory. Defeat pose: the towers lean back together, the ratchet settles on one last agreeing click, and the gold core stays lit.
- **Player setup persists.** Arcanist seals and Chimesinger bells keep their timers through the transition.

### 4.1 Phase 1 — Agreement (fixed loop, 17.65 s)

Sun Verdict → gap → Moon Rebuttal → gap → Common Measure → gap → repeat.

| Attack | Starts | Ends recovery |
|---|---|---|
| Sun Verdict | 0.00 | 4.75 (6.15 on a self-strike) |
| Moon Rebuttal | 5.75 | 10.35 |
| Common Measure | 11.35 | 16.65 |
| next Sun Verdict | 17.65 | — |

Cooldowns check out: the Verdict waits 12.9 s (needs 7), the Rebuttal 13.05 s (needs 6) and Common Measure 12.35 s (needs 10). A self-strike pushes the whole loop back 1.4 s, which only makes cooldowns easier.

Phase 1 always sends Verdict from the Sun tower and Rebuttal from the Moon tower. That teaches the self-strike with one fixed bait corner (Moon side, front).

### 4.2 Phase 2 — Disagreement (fixed loop, 33.2 s)

Alternating Verdict → Deadbeat → Common Measure → Alternating Verdict → Rebuttal Pair → repeat, with a 1.0 s gap after each.

| Attack | Starts | Ends recovery |
|---|---|---|
| Alternating Verdict (1) | 0.00 | 4.95 |
| Deadbeat | 5.95 | 11.85 |
| Common Measure | 12.85 | 18.15 |
| Alternating Verdict (2) | 19.15 | 24.10 |
| Rebuttal Pair | 25.10 | 32.20 |
| next Alternating Verdict | 33.20 | — |

Cooldowns: the Verdict waits 14.2 s and 9.1 s (needs 7), Deadbeat 27.3 s (12), Common Measure 27.9 s (10), Rebuttal Pair 26.1 s (9).

The packet's example sequence (Verdict → punish → Rebuttal Pair → wait for both → punish) is Verdict (2) → Rebuttal Pair.

### 4.3 Seeded variation

- **Alternating Verdict's sender** is drawn from the encounter RNG at warning start, with one limit: never the same tower three times in a row.
- **The Rebuttal Pair's first cone** comes from the tower that did not send the last Verdict, so the bait corner and the first cone are always on opposite sides.
- Targets rotate (§6). Nothing reads player input, and warnings are never secretly shortened.

### 4.4 Encounter length

The target is about 120 s solo at Steady Ticking: Phase 1 is about 3.5 loops (≈ 60 s), the transition 2 s, Phase 2 about 1.8 loops (≈ 60 s). That is inside the packet's 90–150 s.

## 5. Fairness: escape, stamina and collision

### 5.1 Method

Same as the Bellkeeper's and Sylvara's, with two changes. The boss is always at the arena centre, and the body is the stadium from §2.1. The script:
- samples every facing in 10° steps
- finds the reachable danger point farthest from safety (0.1 m danger grid, 0.05 m boundary grid; shapes expanded by 0.3 m; player centres inside the arena and outside the body)
- converts that into walk-only and walk-plus-one-dash escape times, then into the minimum window per difficulty

Rebuttal Pair's cones are checked one at a time: when cone 2 resolves, cone 1 is spent and safe. Counter-Measure (§9) is checked with the spent ring as safe. Limits: straight-line travel, no other players, no acceleration. This is evidence for single attacks, not proof that every combination is solvable.

### 5.2 Results (run on October 21, 2026, placeholder arena)

| Attack | d_max | Worst configuration |
|---|---|---|
| Sun / Alternating Verdict | 1.27 m | Lane thrown across the body toward the rear |
| Moon Rebuttal (100°) | 2.75 m | Cone 150° off the facing, wrapping the tower end |
| Rebuttal Pair, cone 2 (80°) | 2.55 m | |
| Common Measure | 1.81 m | Middle of the ring |
| Deadbeat | 1.05 m | Corridor 2.00 m for player centres |
| Counter-Measure (Midnight Toll) | 2.55 m | From the body edge out into the spent ring |

Minimum window in ticks. Verdict is counted after lock; the others from warning start.

| Attack | First Chime, walk 6 / 4.2 | Steady Ticking | Striking Hour | Midnight Toll | Spec window |
|---|---|---|---|---|---|
| Verdict | 49 / 55 | 33 | 27 | 24 | 60 |
| Moon Rebuttal | 64 / 76 | 38 | 32 | 29 | 96 |
| Rebuttal Pair, cone 2 | 62 / 73 | 38 | 32 | 29 | 90 |
| Common Measure | 55 / 62 | 35 | 29 | 26 | 120 |
| Deadbeat | 47 / 51 | 32 | 26 | 23 | 120 |
| Counter-Measure | 62 / 73 | 38 | 32 | 29 | 90 |

**Findings:**
- **Every packet timing is escapable on foot at every tier**, even at the 4.2 m/s prototype walk with First Chime's long reaction margin. No attack needs a dash.
- **Verdict is the tightest**: 60 ticks after lock, against 55 needed at 4.2 m/s on First Chime. First Chime still adds 12 ticks to the tracking phase (§9) for margin.
- **Self-strike bait:** in Phase 1 the wedge is 21.7° from the sender (bearings −71° to −50°) with 29.6 m² of reachable bait area. In Phase 2 it is 23.7° (−71° to −47°) and 32.1 m². In both, the nearest bait spot touches the body edge, so melee can bait without leaving melee range.

### 5.3 Stamina, collision and hit-stun

- **No dash, sprint or jump is ever required**, at either walk speed. The Bellkeeper's rule (at most one required dash per 2.25 s) is trivially met.
- **The body never moves**, so there's no body push and nothing can be pinned against the wall.
- **Anti-chain.** Rebuttal Pair is the only multi-beat attack. Cone 2 resolves 108 ticks after cone 1, against a need of 9 (stun) + 73 (cone 2 escape plus reaction at 4.2 m/s, First Chime). It passes. A player hit by cone 1 is never forced into cone 2.
- **One hit per instance** (R16). Each Rebuttal Pair cone is its own instance, so standing in both costs 36 HP. Deadbeat's two lanes are **one** instance: a player touching both is hit once.

## 6. Party size (1–8)

Same geometry and timings for every party size. There are no targeted markers on players, so the packet's 1/2/3 marker counts don't apply.

| Attack | Solo | 2–8 |
|---|---|---|
| Verdict (both phases) | The player | One lane at a **rotating** target |
| Moon Rebuttal / Rebuttal Pair | The player | Nearest standing player at warning start |
| Common Measure, Deadbeat | No target | Same |
| Gap turn | Toward the player | Toward the centroid of standing players |

**Targeting rules:**
- **Rotation** is round-robin by stable player ID from a seeded offset, skipping downed and disconnected players. Every player gets turns at baiting the stagger.
- **The Verdict target is marked** at warning start: a sun or moon glyph over the player and on their HUD, matching the sender. Not colour alone.
- **Nearest** is measured to the body edge; ties go to the lowest stable player ID.
- Only the Verdict's target can earn a self-strike, since the lane aims at them. Anyone else standing in the lane is hit normally.
- No assignment depends on class.

## 7. Down, revive and disconnect

The same as the Bellkeeper's and Sylvara's §7:
- A Verdict target downed before lock: the lane picks the next player in the rotation and the warning restarts with a fresh 1.9 s.
- After lock, every hazard is world geometry and resolves normally.
- Downed and inactive players hold no assignments and aren't hit.
- Disconnect is treated as downed.

Every change emits `assignment_changed`.

**Practice** starts the Twins at full health in Phase 1, from the practice seed. Its potion count is chosen in the practice menu. **Fresh run** resets everything.

## 8. Damage and boss HP

Damage is the packet's at Steady Ticking: Verdict and Rebuttal 18 (a full Rebuttal Pair failure is 36), Common Measure and Deadbeat 26. The worst single mistake is 26 HP. First Chime ×0.8, Striking Hour ×1.0, Midnight Toll ×1.2.

**Boss HP** = measured reference sustained DPS × 120 s (solo), measured while moving and through recoveries, not on a stationary dummy, with the player baiting self-strikes as a typical player would. At an illustrative 40 DPS that's 4800 HP. Co-op multiplier: `1 + 0.7 × (n − 1)`, applied to HP only (the same proposal as Sylvara's).

## 9. Difficulty tiers

Mechanics change, not just numbers. Warnings are never uniformly sped up.

| Tier | Twins changes |
|---|---|
| **1 — First Chime** | Verdict tracks 12 ticks longer (resolve 126, lock still 60 before). All recoveries +0.5 s. Phase 2 drops Deadbeat from the loop. Pawl hit radii +0.1 m |
| **2 — Steady Ticking** | The baseline above |
| **3 — Striking Hour** | Authored sequence: Rebuttal Pair's cone 1 always comes from the **bait-corner side** (the tower that just *received* the Verdict). Melee who stayed to punish must leave the corner they baited from. Gaps shrink to 0.5 s. Recoveries −20 %, never below 2.0 s, except the 4.0 s self-strike recovery, which stays |
| **4 — Midnight Toll** | Striking Hour plus **Counter-Measure** |

**Counter-Measure** (the highest tier's added mechanic, in Phase 2 only):
- Common Measure gains a second beat. When the ring's active period ends, both pawls snap **open**, a reversed chord plays, and the inner band (body edge out to 2.5 m) gets a full, fresh 90-tick warning.
- Then it resolves for 26 HP.
- The lesson is to step from the inner safety out into the ring that has just been spent.
- It is a separate instance that can hit again, and the spent ring is safe while it resolves.
- Escape is 2.55 m, walk-solvable at every tier (§5.2).
- Common Measure's recovery (3.0 s) follows the second beat.

## 10. Requests to the shared C# contract (#6)

The Twins reuse the Bellkeeper's and Sylvara's requests: R1 circle, R4 reassign before lock, R5 presentation markers, R6 seeded RNG, R10 lane, R12 HP phases, R13 HP damage, R14 recovery event, R16 one hit per instance, R18 standing state, R21 movement constants and R25 sector. They add (numbered ET- to avoid clashing with other boss lanes):

| # | Need | Why |
|---|---|---|
| ET-R1 | **Stadium shape** (segment plus radius) for the body collider and as a distance source for rings and bands | The body, Common Measure and Counter-Measure |
| ET-R2 | **One boss target with several hurtboxes**, deduplicated per player hit instance | Towers and base share HP; no double damage |
| ET-R3 | **Boss-part hit circles** (pawls) that a resolving lane can test against, emitting `self_strike` with the cast ID | §3.2 |
| ET-R4 | **Per-cast recovery override** (one extension per cast, cooldown unaffected) | The 4.0 s self-strike recovery |
| ET-R5 | **Preview state** for a hazard: outline only, not dangerous, which later promotes to a full warning | Rebuttal Pair cone 2 |
| ET-R6 | **Rotation-only boss locomotion** with a turn-rate cap, allowed only in gaps | §4 |

## 11. Resolved records

Same fields as the Bellkeeper's §12, with `encounter_id` = `escapement_twins`. Additions:
- `sender` (`sun` / `moon`)
- `self_strike: true/false` on each Verdict
- `beat` (1/2) on Rebuttal Pair and Counter-Measure

Example cards:

> *"Alternating Verdict from the Moon tower (1:41, Phase 2). You were 0.4 m inside the lane at impact. Its centreline passed 0.3 m from the Sun pawl: stagger earned, but you sidestepped late."*

> *"Rebuttal Pair, cone 2 (2:05). You moved out of cone 1 into cone 2's outline. Cone 1's spent side was 1.1 m away."*

## 12. Model, animation and asset notes

From the sheet, with gameplay constraints.

- **Silhouette:** one squat tracked plinth carrying two 3.8 m counterweight towers. The towers have ivory face plates (sun on one, crescent moon on the other), brass halo finials and navy panels. A brass rocker bridge over a central gold ratchet wheel joins them. The gold core is the brightest thing at rest. In Phase 2 the towers tilt apart, the bridge extends and the hooked pawls drop. The base stays one continuous piece.
- **Model corrections:**
  - **M1:** add Phase 1 pawls, short brass catches folded against the ratchet at (1.3, ±0.7). They must read from the isometric camera.
  - **M2:** fit the plinth to the 5.2 × 2.2 m collider (the sheet's plinth is slightly longer). Tracks may overhang by no more than 0.1 m.
  - **M3:** the sheet's human figure isn't at the stated scale. Model the towers at 3.8 m (about two human heights).
  - **M4:** keep the sun and moon symbols on the faces; add no numerals.
- **Rig:** each tower tilts and swings separately, plus face raise, flash and dim; the bridge extends; each pawl folds and drops; the ratchet spins, stops and stutters; the tracks turn in place.
- **Animations:** idle (ratchet ticks in agreement); turn in place; Verdict swing per tower; Rebuttal face raise per tower; Common Measure (pawls close, chord); Deadbeat (ratchet stop, masks dim); self-strike jam (sparks, stutter, faces tilt down); transition (towers tilt apart, bridge extends, pawls unfold); defeat.
- **VFX:** Verdict lane (dashed while tracking, solid at lock); cone and ring warnings with coral/amber outlines and hatching; cone 2's outline-only preview, which must look clearly different from a live warning; sender-face glow; the Verdict target glyph. Turquoise is never used on a hazard.
- **Audio:** single knock (Verdict); double knock (Rebuttal and both Pair cones); ringing chord (Common Measure); triple clock click (Deadbeat); sour chord (self-strike); reversed chord (Counter-Measure); one agreeing click (defeat). Every cue also has a visual.

## 13. Validation tests

| # | Check |
|---|---|
| T1 | Escape check rerun on the real Brass Foundry arena and final collider, both movement sets |
| T2 | Self-strike: a lane through the pawl wedge always triggers, a lane outside it never does, at most once per cast; the cooldown is unchanged |
| T3 | Every block in §4 at every tier, with real collision, finite stamina and hit-stun from every hit position |
| T4 | Parties of 1–8, including duplicate classes, downs and disconnects during a Verdict's tracking, lock and active ticks |
| T5 | A burst that crosses 50 % during Rebuttal Pair queues one transition after cone 2 expires |
| T6 | Multi-hurtbox hits from every class ability deal damage once (ET-R2) |
| T7 | Audio-off and reduced-effects readability: the sender face, the target glyph, and cone 2's preview versus a live warning |
| T8 | Melee and ranged clear times within roughly 15–20 % (packet target); tune recovery before HP |

## 14. Decisions

**October 21, 2026:** the user accepted every recommendation below ("go with your recommendations"). D1–D5 and D7 are decided as written. D6 stays open until the Brass Foundry arena's collision is measured; the 11.5 m placeholder stands until then. A Unity block-out of this spec is on branch `feature/escapement-twins`.

| # | Decision | Decided |
|---|---|---|
| D1 | Body size: the packet's 3.5 m cap, or the art's proportions | **5.2 × 2.2 m stadium** (C3) |
| D2 | Phase 1 pawls: add them to the model (M1), or drop the Phase 1 self-strike | **Add them**; the self-strike is the boss's identity and Phase 1 teaches it |
| D3 | Attack names | **Sun Verdict / Moon Rebuttal** (C1); keep Alternating Verdict, Rebuttal Pair, Common Measure and Deadbeat |
| D4 | Self-strike tolerance: centreline within the pawl radius (about a 22° wedge) | As written; widen on First Chime |
| D5 | Midnight Toll mechanic | **Counter-Measure** (§9) |
| D6 | Brass Foundry arena dimensions and collision | Needed before T1. This spec uses an 11.5 m placeholder |
| D7 | Reward | Packet's **Paired Pawl** cosmetic emblem; no gameplay effect |
