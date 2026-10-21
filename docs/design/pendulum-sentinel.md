# Pendulum Sentinel, Counterweight of the Heights — encounter specification

Draft v0.1 · October 21, 2026 · Lane: Encounters / minibosses ([#8](https://github.com/Risxhb/collab/issues/8)) · Source packet: [nine-miniboss handoff, PR #49](https://github.com/Risxhb/collab/pull/49) (`docs/nine-mini-bosses-20261021`, commit `a2ab311`, §2).

Status: **proposal, specification only, nothing here is implemented.** It targets the Unity/C# run loop ([PR #42](https://github.com/Risxhb/collab/pull/42)), where the Pendulum Sentinel is already miniboss 2 as a labelled stand-in (`Run/Encounters/Minibosses.cs`, `miniboss.pendulum_sentinel`). It follows the method of the [Bellkeeper spec](https://github.com/Risxhb/collab/blob/docs/bellkeeper-spec/docs/design/bellkeeper.md) ([PR #16](https://github.com/Risxhb/collab/pull/16)) and the [Dial Warden spec](https://github.com/Risxhb/collab/blob/docs/dial-warden-spec/docs/design/dial-warden.md) ([PR #53](https://github.com/Risxhb/collab/pull/53)).

Sources read: the packet's §2 and shared miniboss contract, its Skyward Spire sheet (row 2), the [content rules](https://github.com/Risxhb/collab/blob/docs/content-rules/docs/design/content-rules.md) ([PR #59](https://github.com/Risxhb/collab/pull/59)), the Bellkeeper and Dial Warden specs, and the run loop's `Minibosses.cs`, `BossEncounter.cs`, `Hazards.cs` and `PlayerController.cs`.

**Order of authority.** Settled content rules, then the Bellkeeper spec (this boss teaches its Pendulum March), then the run loop's current values (hit-stun, knockback, body, arena, jump), then the packet. Where the packet's numbers don't conflict with any of those, they're used as given. Every conflict is listed in §13.

Concept: [media/pendulum-sentinel-concept.png](media/pendulum-sentinel-concept.png) (Phase 1 left, Phase 2 right, with the Pendulum Arches vignette).

Companion tool: [`tools/encounter-escape/pendulum-sentinel-escape-check.js`](../../tools/encounter-escape/pendulum-sentinel-escape-check.js) (Node, about 7 s).

## 1. Role and rules

The Pendulum Sentinel is **Skyward Spire's second miniboss**, between the Dial Warden and the Chimebound Oracle, on the area's shared floor. It is the **straight-line commitment** fight: it charges, has to stop, and is open where it stops. It teaches two things the Bellkeeper relies on:
- **Pendulum March:** a lane that locks the moment it appears. Leave it sideways, then go where the boss will stop, not where it started.
- **Jumping:** the only low attack in Skyward Spire that can be jumped. A ground route always exists too.

Rules it follows:
- **No invincibility.** Dash and jump give no immunity, and there's no post-hit immunity. Each attack instance hits a player at most once.
- **Jump is real but never required.** Only Low Pendulum and Counter-Swing can be cleared by jumping (§3). Everything else ignores jump state.
- **No mandatory class, healer, Brace or dash.** Brace (partial frontal mitigation) works on the Strike-tagged attacks, but no pattern assumes it.
- **No contact damage.** The body pushes players aside while it travels. It never damages them.
- **One damaging pattern at a time** in the baseline (packet rule, stricter than the settled limit of two). Multi-beat attacks retire each footprint before the next beat warns.
- **Damage geometry is the telegraph.** Hits are checked against shared geometry at the resolve tick, never against the swinging pendulum mesh.
- **Locks are final.** Nothing retargets after a lock.

## 2. Baseline values

**Units are metres and seconds; the simulation runs at 60 Hz.**

| Value | Used here | Source |
|---|---|---|
| Walk | 6 m/s (gate); 4.2 m/s also checked | Run loop; content rules |
| Sprint | 9 m/s, never required | Content rules |
| Dash | 3 m in 0.20 s, 30 stamina, no immunity | Content rules |
| Jump | About 0.8 m over 0.6 s, 20 stamina; clears a low hazard while at least 0.35 m high | Run loop `PlayerController.JumpHeight`, `Hazard.ClearHeight` |
| Stamina | 100; regenerates 20/s after 0.75 s | Content rules |
| Hit-stun and knockback | **15 ticks (0.25 s) and 1.5 m** **[packet: ≤ 0.15 s, none]** | Run loop `Hazard` defaults; content rules (provisional) |
| Player health / hit margin | 100 HP / 0.3 m (touching a border counts) | Run loop |
| Boss body | **1.1 m radius** collision circle | Run loop stand-in `BodyRadius` |
| Boss height | **4.6 m** against a 1.8 m hero (about 2.6 heroes) | Packet; stand-in `MarkHeight` 4.6 |
| Arena | Player centres ≤ 11.5 m from the centre; boss centre ≤ **7.4 m** | Run loop `PlayerBound`; packet's 3 m between the body and the wall (11.5 − 3 − 1.1) |
| Phase shift | 120 ticks (2 s), non-damaging, boss targetable | Packet |

**Reach convention (packet).** Listed reaches are measured from the body boundary, so shapes from the boss centre use radius 1.1 m + reach.

## 3. Attack vocabulary

**Resolve model** (as in the Dial Warden spec): each hazard resolves on one tick. The packet's active time becomes a non-damaging follow-through after the resolve, so total timings are unchanged.

| ID | Attack | Phase | Tag | Shape | Targeting and lock | Warning (ticks) | Follow-through | Recovery | Cooldown | Damage |
|---|---|---|---|---|---|---|---|---|---|---|
| `pendulum_charge` | **Pendulum Charge** | 1 | Strike | Lane 2.2 m wide from the boss to the target + 2.75 m, at most 9 m of travel, clamped to the 7.4 m bound | **Lean:** 48 ticks turning toward the rotating target, no hazard drawn. Then the lane appears **already locked** | 60 from lock | Body travels the lane in 18 ticks | **192 (3.2 s)**, the Phase 1 major opening | 480 (8 s) | 26 |
| `anchor_drop` | **Anchor Drop** | 1 and 2 | Ground | Disc, radius 3.1 m (2 m from the body), on the boss | Fixed at warning | 90 | 15 | 168 (2.8 s) | 420 (7 s) | 18 |
| `low_pendulum` | **Low Pendulum** | 1 | Strike, **jump-clearable** | 90° sector, radius 5.1 m (4 m bar), apex at the boss centre, centred on the target's bearing | Nearest standing player; locks at warning | 96 | 27 (the bar sweeps) | 144 (2.4 s) | 360 (6 s) | 18 |
| `return_stroke` | **Return Stroke** (replaces Charge) | 2 | Strike | Leg 1: the Charge lane. Leg 2: **the same footprint, reversed**, back to the start | Leg 1 as Charge. Leg 2 is fixed by leg 1, with no retargeting | Leg 1: 60 from lock. Leg 2: **96**, starting on landing | 18 travel per leg | **228 (3.8 s)** after leg 2, the Phase 2 major opening | 600 (10 s) | 26 per leg |
| `counter_swing` | **Counter-Swing** (replaces Low Pendulum) | 2 | Strike, **jump-clearable** | Beat I: Low Pendulum's sector. Beat II: the same sector rotated 180°, marked **II** | Beat I as Low Pendulum. Beat II is fixed by beat I | I: 96. II: **84**, warning after I's follow-through | 27 each | 180 (3.0 s) after II | 480 (8 s) | 18 per beat |
| `counterweight_cast` | **Counterweight Cast** (new) | 2 | Ground | Circle of radius 2.0 m, centre within 8.1 m of the boss centre (7 m from the body) and inside the play bound | Follows its owner at 6 m/s for 48 ticks, then latches | **60 after latch** (108 in total) | 18 (retrieval) | 198 (3.3 s) | 660 (11 s) | 26 |

**How each one reads and plays** (packet behaviour, made exact):
- **Pendulum Charge.** The tower leans and the chain tightens through the 48-tick lean, so the target sees it coming. The bell clicks, then the lane draws already latched, with a direction chevron and a landing marker. Leave it sideways. Melee follows *behind the body* to the landing marker; ranged releases a planted cast when the lane draws. The 3.2 s stop is the punish. The lean is presentation only: it stands in for the packet's early tracking without breaking the Bellkeeper rule that lanes lock when they appear.
- **Anchor Drop.** The tower rises on its feet with downward arrows and a heavy chain clunk. A hatched disc 2 m beyond the body. Step out, then come back in for the 2.8 s grounded recovery. It follows the Charge's recovery in both phases, so melee players who punished the landing learn to leave.
- **Low Pendulum.** The pendulum lowers with a deep scrape, and the bar points down one side of the tower. Step to the opposite side or beyond 5.1 m, or jump the bar at the resolve tick. The sector is centred on the nearest player because they're the one it teaches. The ground route is always at most 3.5 m (§5).
- **Return Stroke.** Leg 1 is the Charge. On landing, the lane gains reverse arrows and two descending bells, then resolves 96 ticks later on exactly the same footprint. Stay alongside the lane through both passes. Cross behind only after the body has passed *back*. The pause between legs is planted but isn't the full punish: the 3.8 s opening comes after leg 2.
- **Counter-Swing.** Beat I is Low Pendulum. After its follow-through, the opposite sector warns, marked **II** with a fresh scrape at a lower pitch. Players cross to the cleared beat I side. Beat II can also be jumped.
- **Counterweight Cast.** The weight swings forward on a ringing chain. A circle follows its owner, then latches for the final 60 ticks with a visible latch cue. Leave the circle. The chain drawn to it is decorative and never deals damage. The 3.3 s retrieval is a stationary opening for both melee and casters.

**Jump rule (R19).** Low Pendulum and both Counter-Swing beats are the only jump-clearable hazards. A player at least 0.35 m off the ground at the resolve tick is not hit (the run loop's existing rule). Jumping costs 20 stamina and is never needed: the ground route is checked in §5.

## 4. Phases and timeline

| Phase | Name | Boss HP | Look | Teaches |
|---|---|---|---|---|
| 1 | **Balanced Weight** | 100–50 % | Closed tower, pendulum high, small counterweight arms | Leave the lane, go to the stop; the jumpable bar |
| 2 | **Unbalanced Return** | 50–0 % | Side panels unfolded into balance fins, pendulum hanging lower | Hold beside a lane through two passes; cross to the cleared side |

**Shared phase rules:**
- **Queued transition.** Crossing 50 % queues the transition until the current attack's recovery ends and its hazards are spent. Then a 120-tick harmless transformation: panels unfold, the pendulum drops. The boss stays targetable; no heal, no new health bar.
- **One transition only.** A burst that crosses 50 % queues it once. A lethal burst ends the fight at once.
- **Defeat** cancels every warning, lane and circle on the same tick; nothing lands after victory. If the last player goes down on the same tick, defeat wins. Defeat pose: the pendulum swings to rest, the tower kneels on its stilts.
- **Walk back to the middle.** After each loop, the boss walks toward the arena centre at 2.5 m/s for up to 120 ticks, stopping within 2 m of it (stand-in values). It never chases one ranged player.
- **Cooldown hold.** If an attack's cooldown isn't ready on its turn, the boss holds in place until it is. Windows are never shortened. With the loops below, no hold occurs.
- **Seeded variation:** the Charge's rotation offset, from the encounter RNG (R6).

### 4.1 Phase 1 — Balanced Weight (fixed loop, ≈ 14.3–16.3 s)

Pendulum Charge → Anchor Drop → Low Pendulum → walk back → repeat.

| Step | Start | Lock | Resolve | Recovery ends |
|---|---|---|---|---|
| Pendulum Charge (lean 0–48) | 0 | 48 | 108; travel to 126 | 318 |
| Anchor Drop | 318 | 318 | 408 | 591 |
| Low Pendulum | 591 | 591 | 687 | 858 |
| Walk back | 858 | — | — | ≤ 978 |

This is the packet's example: Charge → move sideways and follow into the punish → Anchor Drop → leave and re-enter.

### 4.2 Phase 2 — Unbalanced Return (fixed loop, ≈ 24.7–26.7 s)

Return Stroke → Anchor Drop → Counter-Swing → Counterweight Cast → walk back → repeat.

| Step | Start | Lock | Resolve | Recovery ends |
|---|---|---|---|---|
| Return Stroke leg 1 (lean 0–48) | 0 | 48 | 108; travel to 126 | — |
| Return Stroke leg 2 | 126 | 126 | 222; travel to 240 | 468 |
| Anchor Drop | 468 | 468 | 558 | 741 |
| Counter-Swing beat I | 741 | 741 | 837 | — |
| Counter-Swing beat II | 864 | 864 | 948 | 1155 |
| Counterweight Cast | 1155 | 1203 | 1263 | 1479 |
| Walk back | 1479 | — | — | ≤ 1599 |

This is the packet's example: Return Stroke → hold beside the lane through both passes → 3.8 s punish → Counter-Swing → cross to the cleared side → punish.

## 5. Fairness: escape, stamina, collision and hit-stun

### 5.1 Method

`pendulum-sentinel-escape-check.js` uses the Bellkeeper and Dial Warden method. It samples every boss position in the 7.4 m bound (in 0.5 m steps), every facing (10°) and every danger point on a 0.1 m grid: inside the 11.5 m play bound, outside the body (1.1 + 0.3 m), and inside the shape expanded by 0.3 m. For each point it finds the straight-line distance to the nearest safe point (0.05 m boundary grid). Minimum window = tier reaction margin + distance ÷ walk speed. Reaction margins are the Bellkeeper's: 36 / 27 / 21 / 18 ticks for First Chime / Steady Ticking / Striking Hour / Midnight Toll.

**Limits:** straight-line travel with no routing around the body, no other players, no acceleration, accurate to about ±0.05 m. This is evidence for single attacks, not proof for every sequence.

### 5.2 Results (October 21, 2026, boss bound 7.4 m)

| Shape | d_max | Worst case |
|---|---|---|
| Charge / Return Stroke lane | 1.40 m | Any lane: half-width + margin, sideways |
| Anchor Drop disc | 2.00 m | Hugging the body |
| Low Pendulum / Counter-Swing sector | **3.50 m** | Boss 7 m out, facing the wall: a player at the wall in the bar's middle must run along it |
| Counterweight Cast circle | 2.31 m | Circle centred on a player at the wall |

Minimum window in ticks (walk only, no dash) against what this spec uses at Steady Ticking:

| Attack | Steady @ 6 m/s | Steady @ 4.2 m/s | First Chime @ 4.2 m/s | Used (Steady) |
|---|---|---|---|---|
| Charge / Return leg 1, after lock | 41 | 47 | 56 | 60 |
| Return Stroke leg 2 | 41 | 47 | 56 | 96 |
| Anchor Drop | 47 | 56 | 65 | 90 |
| Low Pendulum / Counter-Swing I | 62 | 77 | 86 | 96 |
| Counter-Swing II | 62 | 77 | 86 | 84 (First Chime 96) |
| Counterweight Cast, after latch | 51 | **60** | 69 | **60** (First Chime 72) |

**Everything is walk-solvable at Steady Ticking with both walk speeds.** Counterweight Cast at 4.2 m/s passes with no spare tick. That matches the packet's own spot check (2.94 m available against 2.70 m needed), and the design walk has 9 ticks to spare (D3).

**Body and wall.** Keeping the boss 3 m off the wall means no lane or disc ever pins a player against it. The sector is the only shape that reaches the wall, and its route runs along the wall, not through the body.

### 5.3 Stamina budget

Same rule as the other specs: at most one required dash per 2.25 s, and three in a row only after 5 s with none. **No dash, sprint or jump is ever required here**, at either walk speed. Jumping the bar is a choice that saves walking, for 20 stamina.

### 5.4 Collision, knockback and hit-stun

- **Body: a 1.1 m circle.** While travelling a lane it pushes players sideways out of its path without damage or stun (R20). Charge endpoints keep a 3 m corridor to the wall, so travel never plugs the ring.
- **Knockback** 1.5 m: sideways out of a lane, radially away from the boss for the disc and the sectors, away from the circle's centre for the Cast. It stops at the wall and the body and never deals wall damage.
- **Anti-chain rule:** the later resolve must come at least `stun + reaction + escape` after the earlier one, using the knocked-back position.
  - **Return leg 1 → leg 2:** 114 ticks apart (108 → 222). Leg 1's knockback carries a hit player out of the shared footprint, so 15 + 27 + 0 = 42 is needed. The worst case, knockback blocked, needs 15 + 27 + 14 = 56. Passes.
  - **Counter-Swing I → II:** 111 ticks apart. Beat I knocks players away from the boss inside the beat I side; beat II is the opposite sector. Passes.
  - **Striking Hour: Return leg 2 → Anchor Drop** (§9): 108 ticks apart; 15 + 21 + 20 = 56 needed at 6 m/s, 65 at 4.2 m/s. Passes.
- **One hit per instance** (R16). Each leg and beat is its own instance, so failing both legs of the Return Stroke costs 52 HP.

## 6. Party size (1–8)

Same geometry and timings at every size. Counts use standing, active players at each warning.

| Attack | 1 | 2–4 | 5–8 |
|---|---|---|---|
| Pendulum Charge / Return Stroke | 1 lane | 1 lane, rotating target | Same |
| Low Pendulum / Counter-Swing | Sector at the nearest player | Same | Same |
| Anchor Drop | On the boss | Same | Same |
| Counterweight Cast | **1 circle** (the pendulum) | **2 circles** (the two arm counterweights) | **3 circles** (all three weights) |

- **Counterweight Cast follows the packet's 1 / 2 / 3 marker rule.** The sheet already shows three weights, so each circle has a visible source. All circles warn and latch together as one pattern. Circles that overlap at the latch merge into one damage event. A placement that would cut every exit is rejected and that circle stays latched at its last valid position (D4).
- **Rotation:** the Charge target is round-robin by stable player ID from a seeded offset. Cast owners rotate the same way, and nobody gets a circle twice in a row while another standing player is eligible.
- **Nearest** is measured from the boss centre at warning; ties go to the lowest player ID.
- **Marked before lock:** the Charge target gets a shape tag during the lean, and Cast owners get theirs while their circle follows. Not colour alone.
- **Boss HP** scales `base × (1 + 0.6 × (n − 1))`, the Bellkeeper value (D5). Mechanics never scale with party size beyond the Cast count.

## 7. Down, revive and disconnect

- **Charge target downed or inactive during the lean:** the lane locks on the next player in the rotation and emits `assignment_changed`.
- **Cast owner downed before the latch:** that circle is cancelled (`assignment_changed`, `cancelled: true`), as with the Bellkeeper's chimes.
- **After lock:** every hazard is world geometry and resolves normally, including on revivers.
- **Downed and inactive players** hold no assignments and aren't hit. A disconnect counts as downed.
- **Run and practice:** as the second miniboss, players arrive with health refilled and the potions they had left. Practice starts at full health and potions, in Phase 1, from the practice seed.

## 8. Damage and boss HP

Steady Ticking damage, the packet's normalized values (same scale as the Dial Warden spec):

| Attack | Damage |
|---|---|
| Pendulum Charge, each Return Stroke leg, Counterweight Cast | 26 |
| Anchor Drop, Low Pendulum, each Counter-Swing beat | 18 |

First Chime ×0.8, Striking Hour ×1.0, Midnight Toll ×1.2.

**Boss HP:** keep the stand-in's **1700** until measured. Then HP = measured reference sustained DPS × 120 s solo, measured while moving and punishing, not on a dummy. Packet target 90–150 s, inside the settled 60–150 s miniboss range.

## 9. Difficulty tiers

Warnings stay the same from Steady Ticking up. Higher tiers change sequences and recoveries, never by speeding everything up.

| Tier | Pendulum Sentinel changes |
|---|---|
| **1 — First Chime** | Counter-Swing II warns 96; the Cast latches 72 before resolving; every recovery +30 ticks. Walk-solvable at 4.2 m/s throughout |
| **2 — Steady Ticking** | The baseline above |
| **3 — Striking Hour** | **Pursuit:** in Phase 2, Anchor Drop warns on Return Stroke leg 2's landing tick (240) and resolves at 330. This replaces the loop's separate Anchor Drop step, and the 228-tick major recovery follows the Anchor Drop instead. It previews the Bellkeeper's Block A. Other recoveries −20 %, but the major openings (Charge, Return Stroke, Cast) never go below 180 ticks (3.0 s) |
| **4 — Midnight Toll** | Striking Hour plus **Overbalance** (proposal): Counter-Swing gains beat **III**, back on beat I's side, with a fresh 84-tick warning after beat II's follow-through. Players who crossed to the cleared side must cross again. 18 damage; walk-solvable (needs 53 at 6 m/s, 68 at 4.2 m/s); the 180-tick recovery follows beat III |

## 10. Requests to the shared C# contract (#6)

The run loop already has lanes, per-hazard jump clearance, hit-stun, knockback and one hit per instance. This encounter reuses: **R1** circle, **R4** cancel or reassign before lock, **R6** seeded RNG, **R10** lane, **R11** mutable boss position, **R12** HP phases, **R14** recovery event, **R16** one hit per instance, **R17** hit-stun and knockback, **R18** standing state, **R19** height eligibility (sectors as well as lanes), **R20** body collision during lane travel, **R25** cone/sector (Sylvara), and **R27** a published turn/lean state (Dial Warden), for the Charge lean.

New:

| Need | Why |
|---|---|
| **Per-encounter boss bound** (today a single `CombatWorld.BossBound` = 9.5 m) | 7.4 m keeps the packet's 3 m wall corridor. The Dial Warden needs 7.0 m |
| **Hazard group with a fixed follow-on footprint** (leg 2 / beat II made from leg 1 / beat I's locked geometry, warning when the first one's follow-through ends) | Return Stroke and Counter-Swing. No retargeting is possible by construction |
| **Linked markers** (one pattern, several owners, latch together, merge on overlap) | Counterweight Cast in parties |

## 11. Resolved records

Same fields as the Bellkeeper's §12, with `encounter_id` = `miniboss.pendulum_sentinel`. Additions:
- `leg` (1 / 2) for Return Stroke and `beat` (I / II / III) for Counter-Swing
- `cleared_by_jump: true` when a jump avoided a bar
- `merged_markers` for Cast circles that merged

Example card:

> *"Return Stroke (1:12, Phase 2). The tower came back along the same lane. You crossed behind it 0.4 s before the second pass. Stay beside the lane until both passes are done."*

## 12. Model, animation and asset notes

- **Scale: 4.6 m** against a 1.8 m hero. The sheet's figure is about 2.5 hero heights, which agrees.
- **Collider vs sheet.** The collider is the tower base, 2.2 m across. The stilt legs splay about 3.3 m tip to tip on the sheet. Keep the legs thin, and fade them at the camera if they hide the floor.
- **Phase 2 fins are presentation only.** On the sheet they span about 8 m, wider than any hazard here. They have no collision, are never a damage shape, and must not read as a hitbox from the high camera. Recommend shrinking them to about 5 m and keeping them above head height (D6).
- **The sheet has three-quarter views only.** The content rules ask for front, side, back and three-quarter views on one baseline. A model sheet is still needed.
- **Facing:** the arch and pendulum face forward. The lean must read from 50–60° above, and the pendulum's tilt shows which side Low Pendulum will hit.
- **Weights:** one central pendulum bob and two arm counterweights. Those three are the Counterweight Cast sources (§6).
- **Rig:** tower body with lean and rise; two stilt legs with planted feet (no sliding during the 18-tick travel; skid or brace instead); pendulum chain with a lowering bob; two counterweight arms; side panels that unfold into fins.
- **Animations:** idle (slow swing), walk, Charge lean and travel, landing skid, Anchor Drop (rise, slam), Low Pendulum (lower, sweep), Return Stroke (reverse brace, return travel), Counter-Swing (two sweeps), Counterweight Cast (swing out, retrieval), phase transformation (2 s), defeat.
- **VFX:** coral/amber hatched warnings for every hazard; a direction chevron and landing marker for lanes; reverse arrows for leg 2; a numbered **II** (and **III**) sector; a latch cue on Cast circles. **Jumpable bars use the hostile palette plus a distinct low-bar pattern and a JUMP glyph, not the run loop's current teal tint** (§13 item 8).
- **Audio:** chain tightening and bell click (Charge), deep scrape (bar), lower-pitched scrape (beat II), heavy chain clunk (Anchor Drop), two descending bells (Return), ringing chain (Cast). Every cue also has a visual.

## 13. Conflicts and what this spec did

| # | Conflict | Decision here |
|---|---|---|
| 1 | Packet Charge tracks until 1 s before impact; the Bellkeeper's March, which this boss teaches, locks at warning | **The lane locks when it appears.** The packet's tracking time becomes a 48-tick lean with no hazard drawn, so the 1.8 s total and the 1 s locked lane are kept |
| 2 | Packet lane is 2 m wide with its stop "within 5 m of the bait target" | **2.2 m lane to the target + 2.75 m** (March and run loop), capped at 9 m and clamped 3 m off the wall |
| 3 | Packet hit-stun ≤ 0.15 s, no knockback | **0.25 s and 1.5 m** (current game). The 3 m wall corridor keeps knockback from pinning |
| 4 | Run-loop stand-in Low Pendulum is a 1.4 m lane to the wall (72 ticks, 18 damage) | **Packet's 90° sector**, still jump-clearable. The stand-in is labelled provisional |
| 5 | Stand-in Charge: 48-tick warning, 22 damage; first-pass recoveries 50–80 ticks | **Packet timings and damage**, because the settled rules need a 3–4 s major opening and the stand-in has none |
| 6 | Packet allows one pattern at a time; settled rules allow two | Kept the stricter packet rule. Only Striking Hour's Pursuit chains attacks, and it never overlaps them |
| 7 | Packet labels its design as a "hard" fit (Striking Hour) | Used as the **Steady Ticking** baseline, like every other boss spec |
| 8 | Run loop tints jump-clearable hazards **teal**; settled rules say hostile telegraphs are coral/amber and turquoise never means danger | Recommend coral/amber with a low-bar pattern and a JUMP glyph (D7) |
| 9 | Packet said Vanguard Brace and seals exist; the game rule is no block or parry | No conflict: Brace is settled as partial frontal mitigation, not a block. Strike/Ground tags are given in §3; nothing requires Brace |

## 14. Validation tests

| # | Check |
|---|---|
| T1 | Rerun the escape check on the real Skyward Spire collision and final body, at both walk speeds |
| T2 | Both phase loops at every tier, with real collision, finite stamina, hit-stun and knockback from every hit position |
| T3 | Charge and Return Stroke travel: R20 pushes players aside with no damage; no endpoint within 3 m of the wall; the ring is never plugged |
| T4 | Jump: clearing each bar at the resolve tick works; jumping any other attack never avoids it; the ground route works with 0 stamina |
| T5 | Melee approach time to the Charge's landing point and into the 3.8 s Return opening, per class (the packet's first tuning priority) |
| T6 | Parties of 1–8, including all-Vanguard, all-Arcanist and all-Chimesinger, with 1 / 2 / 3 Cast circles, overlaps, and downs or disconnects during the lean and before the latch |
| T7 | The 50 % threshold crossed between Return Stroke legs and between Counter-Swing beats: the attack finishes, then one 2 s transition |
| T8 | Audio-off, grayscale and reduced-effects readability of lanes, reverse arrows, II/III sectors and latch cues |

## 15. Human decisions needed

| # | Decision | Recommendation |
|---|---|---|
| D1 | Charge: a 48-tick lean, then a lane locked for 60 ticks (vs the Bellkeeper's 48 locked) | **As specified.** The Sentinel is the slower teacher; the Bellkeeper is the test |
| D2 | Boss bound 7.4 m for this encounter (shared constant is 9.5 m) | **7.4 m**, keeping the packet's 3 m corridor; needs a per-encounter bound |
| D3 | Counterweight Cast latch: 60 ticks passes 4.2 m/s with no spare tick | **Keep 60** (6 m/s is the design walk); First Chime uses 72 |
| D4 | Counterweight Cast in parties: the packet's 1 / 2 / 3 rule, using the sheet's three weights | **Yes** |
| D5 | Co-op HP multiplier: 0.6 (Bellkeeper) or 0.7 (Dial Warden, Sylvara) | **0.6** until the encounter lane picks one value for all |
| D6 | Phase 2 fins (about 8 m on the sheet) | **Shrink to about 5 m, keep them above head height, no collision** |
| D7 | Jump-clearable hazard colour | **Coral/amber + low-bar pattern + JUMP glyph**, replacing teal in the run loop |
| D8 | Midnight Toll **Overbalance** (beat III) | Approve as proposed, after Striking Hour is playtested |

## 16. Implementation notes for the run loop

The stand-in (`PendulumSentinel` in `Minibosses.cs`) has the Charge lane and the jumpable bar. To reach v0.1 it needs:
- HP phases at 50 % with the 120-tick transformation, replacing the Teaching/Alternating loops;
- the 48-tick lean before the Charge lane, 60 ticks locked, 192-tick recovery, and 26 damage;
- Low Pendulum as a 90° sector (R25) with `JumpClearable`, replacing the lane;
- Anchor Drop, Return Stroke, Counter-Swing and Counterweight Cast;
- a 7.4 m boss bound for this encounter;
- autotest rows: Charge sidestep, jump and ground-route clears of each bar, Return leg 2 hitting a player who crossed early, and Cast circles at 1, 3 and 8 players.
