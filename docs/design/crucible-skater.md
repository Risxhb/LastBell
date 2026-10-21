# Crucible Skater, Keeper of the Cooling Ring: encounter specification

Draft v0.1 · October 21, 2026 · Lane: Mini-bosses / Brass Foundry (Crucible Skater) · Source packet: Codex's nine mini-boss handoff, branch `docs/nine-mini-bosses-20261021` (commit `a2ab311`, `docs/mini-bosses/`), split locally into `bosses/mini/2-2-brass-foundry-crucible-skater/`.

Status: **specification only. Nothing here is implemented.** It doesn't depend on an engine and targets the Unity/C# production path. It follows the method of the [Bellkeeper spec](https://github.com/Risxhb/collab/blob/docs/bellkeeper-spec/docs/design/bellkeeper.md) ([PR #16](https://github.com/Risxhb/collab/pull/16)) and the [Sylvara spec](https://github.com/Risxhb/collab/blob/docs/sylvara-spec/docs/design/sylvara.md) ([PR #50](https://github.com/Risxhb/collab/pull/50)), so all three can share one validator and one set of contract requests.

The Crucible Skater is the **second of Brass Foundry's three mini-bosses**. Rivet Cantor and the Escapement Twins fight on the same floor before or after it, and Crucivar is the area's final boss. Like the other campaign packets ([#44](https://github.com/Risxhb/collab/issues/44)), it isn't part of the current one-arena three-plus-one slice.

The packet's identity, geometry, timings and damage are taken as given. This spec adds what the packet leaves open:
- ticks and lock points
- how the boss moves between attacks
- the body collider
- schedules
- targeting and party scaling
- escape evidence
- the calls this lane made where nothing had been decided (§13)

Concept art: `2-2-brass-foundry-crucible-skater.png` (both phases) and `area-sheet-02-brass-foundry.png`, in the packet folder.

Companion tool: [`tools/encounter-escape/crucible-skater-escape-check.ps1`](../../tools/encounter-escape/crucible-skater-escape-check.ps1). It runs in about 5 seconds.

## 1. Rules this encounter follows

These rules come from the [current game design](https://github.com/Risxhb/collab/blob/docs/boss-rush-decisions/docs/game-design.md), the human decisions recorded on the wiki and the packet's shared mini-boss contract. Where older documents or prototypes disagree, these win.

- **Health, dodge and immunity:**
  - Dash gives no immunity, and nothing gives post-hit immunity. Players avoid damage only by leaving the footprint.
  - Each attack event hits a player at most once.
  - The boss deals no contact damage.
- **Nothing is mandatory.** Healing, Brace, parry, upgrades and dash are never required. Any mix of Vanguard, Arcanist and Chimesinger can clear, including duplicates.
- **Vanguard Brace** is the approved partial frontal mitigation. Here it can only matter against Rim Kick, a frontal sector. Every lane, pool and ring is a ground hazard and bypasses Brace. No pattern assumes Brace.
- **Finite stamina governs routes** (§5.3). Attacks never consume it.
- **Jumping never avoids an attack.** Jump state doesn't change how a hit resolves.
- **Health and potions:**
  - Health refills before each boss after the first.
  - Spent potions carry over between mini-bosses.
  - The Skater is a mini-boss, so players may arrive with fewer potions. Its damage stays at the packet's values, and no single mistake costs more than 26 HP.
- **The boss stays targetable** through its phase change. Arcanist seals, Chimesinger bells and other player setup persist with their timers running.
- **Damage geometry is the telegraph.** Hits are checked against authoritative shared geometry, never animation or VFX. Idle drips, the gold reservoir glow and furnace light never deal damage.
- **Locks are final.** Nothing retargets after it locks.
- **One damaging pattern at a time.** Every hazard expires before the next unrelated attack warns.

## 2. Baseline values

**Units are metres and seconds; the simulation runs at 60 Hz.**

| Value | Used here | Source |
|---|---|---|
| Walk | 6 m/s (the gate); 4.2 m/s (also checked) | Game design #33; Unity prototype #27 / #42 |
| Sprint | 9 m/s, never required | Game design |
| Dash | 3 m over 0.20 s, 30 stamina, no immunity | Game design |
| Stamina | 100. Dash 30, jump 20, sprint 25/s; regenerates 20/s after 0.75 s | Game design |
| Player hit margin | 0.3 m; touching a border counts as inside | Bellkeeper spec |
| Player health | 100 HP; light / medium / heavy hits = 10 / 18 / 26 | Packet |
| Hit-stun | 9 ticks (0.15 s) | Packet |
| Boss body | **1.5 m radius circle around the bowl** (3 m across). The three skates never block (§5.4) | This spec (D2) |
| Arena | **Placeholder:** the packet's proposed 24 m circle. Player centres stay ≤ 11.7 m from the centre, and the boss centre ≤ 9.5 m | No Brass Foundry collision mesh exists yet (D7) |

The boss-centre bound leaves a full player width between the bowl and the wall, so nobody can be pinned there. Rerun the escape check once the real floor exists.

## 3. Attack vocabulary

Six attacks: three in Phase 1 and three in Phase 2. Rim Kick appears in both.

- **Geometry and damage** are the packet's.
- **W / A / R / CD** are the packet's warning, active, minimum recovery and minimum cooldown, converted exactly into ticks.
- **Reach** is measured from the bowl's edge, as the packet specifies.
- **Lock** is when the footprint stops moving. The window is the number of ticks from lock until the first damage.

### 3.1 Phase 1: Measured Pour

| ID | Attack | Shape | Targeting | Lock | Window after lock | Active | Recovery | Cooldown | Damage |
|---|---|---|---|---|---|---|---|---|---|
| `casting_run` | **Casting Run** | Swept lane 3 m wide (**D1**; packet 2 m), up to 7 m of travel; end clamped to the boss bound | Rotating target (§6) | Aims at the target for 48 ticks, then locks | 60 | 72 (7 m in 1.2 s) | 210 (3.5 s) once the trail cools | 540 | 18 |
| `measured_spill` | **Measured Spill** | Circle, radius 1.8 m, on each marked player | 1 / 2 / 3 recipients (§6) | Follows its owner for 60 ticks, then locks | 60 | 90 (live pool) | 180 (3.0 s) once the pools expire | 480 | 18 per volley |
| `rim_kick` | **Rim Kick** | 100° sector from the boss centre, reach 3.5 m past the bowl edge (5.0 m from the centre) | Nearest standing player at warning | Facing locks at warning start | 84 | 15 | 150 (2.5 s) | 300 | 18 |

**Casting Run:**
- **Tell:** the skates align and scrape, and the whole lane is drawn before the boss moves, with an arrow at the end showing where it stops.
- **Damage:** the trail behind the moving bowl is live during the 72 active ticks and deals damage to anyone inside it, once. When travel ends, the trail cools and the floor reads as plain brass.
- **Punish:** 3.5 s at the stopping point. Melee walks alongside the lane toward that endpoint instead of chasing the bowl. Ranged side-steps the lane.
- **Clamping:** a run that would cross the boss bound is shortened, never bent. Its speed stays the same, so its active time is shorter.

**Measured Spill:**
- **Tell:** a spout tips toward each recipient with a pouring chime, and a dashed circle follows each of them.
- **Lock:** at the halfway point of the warning, the circle latches solid and shows a countdown.
- **Damage:** the pool is live for 1.5 s. It damages anyone inside, at most once per volley, even if two pools overlap them. Then it expires and cools visibly.
- **Response:** the recipient carries the circle to a flank, then walks out of it. Melee who weren't marked can close in immediately.
- **The boss is planted for the whole attack**, with all three skates down.

**Rim Kick:**
- **Tell:** the front skate lifts and clicks. The sector is drawn at warning start and doesn't track.
- **Response:** step behind the adjacent skate, or leave the reach.
- **Recovery:** 2.5 s, with the skate visibly re-planting.
- **Brace:** Vanguard Brace may mitigate it from the front. That is optional.

### 3.2 Phase 2: Open Casting Ring

| ID | Attack | Shape | Targeting | Lock | Window after lock | Active | Recovery | Cooldown | Damage |
|---|---|---|---|---|---|---|---|---|---|
| `elbow_cast` | **Elbow Cast** (evolves Casting Run) | Leg 1 = Casting Run. Leg 2: a 3 m swept lane from leg 1's end, turned 90°, up to 5 m | Leg 1 at a rotating target. Leg 2's side is fixed when leg 1 locks (D4) | Leg 1 as Casting Run. Leg 2's footprint latches at its warning start | 60 (leg 1), 96 (leg 2) | 72 per leg | 240 (4.0 s) once leg 2 cools | 660 | 18 per leg |
| `paired_pour` | **Paired Pour** (evolves Measured Spill) | Volley 1 = Measured Spill. Volley 2: the same pools on the next recipients in rotation | §6 | Volley 1 as Spill. Volley 2 follows its owner for 48 ticks, then locks (D5) | 60, then 60 | 90 per volley | 210 (3.5 s) once volley 2 expires | 600 | 18 per volley |
| `rim_kick` | **Rim Kick** | Unchanged | | | | | | | |
| `empty_ladle` | **Empty the Ladle** (new) | Ring from 2.5 to 5.0 m past the bowl edge (4.0 to 6.5 m from the centre) | None; centred on the planted boss | Latches at warning start | 108 | 18 | 204 (3.4 s) | 720 | 26 (heavy) |

**Elbow Cast:**
- **Leg 1 tell:** as Casting Run, plus a **bent arrow** at the stopping point that shows which way leg 2 will turn. The packet's "explicit pause" is this visible side, fixed at lock. It never homes.
- **Leg 2:** its lane is drawn the moment leg 1 cools (tick 180) and resolves 96 ticks later (1.6 s).
- **Timeline** (ticks from warning): leg 1 warns at 0, locks at 48 and moves 108–179. Leg 2 warns at 180 and moves 276–347. Recovery runs 348–587.
- **Response:** melee waits in the inner elbow (the cooled corner) and meets the bowl at the final stop. Ranged side-steps the second lane.
- **Turn side rule:**
  - The director picks a side whose full 5 m fits inside the boss bound, alternating when both fit.
  - If neither fits, it takes the longer side and shortens leg 2. It never bends it.
  - A leg shorter than 2 m is dropped, and the boss goes straight to recovery.

**Paired Pour:**
- **Volley 1** is Measured Spill.
- **Volley 2:**
  - New marks appear the moment volley 1's pools expire (tick 210), on the next recipients in rotation. Solo, that's the same player.
  - They follow their owners for 48 ticks, lock, resolve 60 ticks later and stay live for 90.
  - There's no hidden prediction: each mark sits under its owner until it visibly latches.
- **Response:** put the second pool on the opposite flank, or step back into the space the first pool just cooled.
- **Recovery:** 3.5 s.

**Empty the Ladle:**
- **Tell:** the raised pour ring hums, and the ring is drawn with radial tick marks counting down.
- **Safe spaces:** inside the ring (a 2.2 m band against the bowl for player centres) or outside it.
- **Response:** pick the nearest boundary. Melee stays close, and ranged steps out.
- **Recovery:** 3.4 s, shown by the drained reservoir.
- **Shape:** it's a fixed ring, not an expanding pulse, so nobody has to jump anything.

### 3.3 Movement between attacks (D3)

The packet gives the behaviour ("slides to medium distance, then plants all three skates; cannot attack while circling") but no numbers. This spec's are:

- **Slide** happens only between attacks, never during recovery.
  - Speed 4.5 m/s, at most 90 ticks (6.75 m).
  - Then a **24-tick plant** (0.4 s): all three skates click down before the next warning can start.
  - The boss can't warn while sliding.
- **Before Measured Spill / Paired Pour:** slide to a point 6 m (body edge to player centre) from the first recipient, on the side away from the arena wall.
- **Before Rim Kick:** slide toward the nearest standing player until the gap is 1.5 m or the 90 ticks run out. Then kick, even if the kick whiffs.
- **Before Casting Run, Elbow Cast and Empty the Ladle:** no slide. They start from where the boss already is.
- **The body pushes players aside** while sliding or running (R20). The push deals no damage and never pushes into the wall.

Together these cap kiting: no slide is longer than 1.5 s, no run is longer than 7 + 5 m, and no hazard outlives its active time.

## 4. Phases and timeline

| Phase | Name | Boss HP | Visual | What it teaches |
|---|---|---|---|---|
| 1 | Measured Pour | 100–50 % | Closed bowl, narrow reservoir slit, skates flat | Bait a pool then leave it; read where the run stops |
| 2 | Open Casting Ring | 50–0 % | Rim lifts to show the segmented rotating pour ring; skates spread into broad fins | Two-step versions of both lessons, plus choosing a radius |

**Shared phase rules:**

- **Queued transition.** When the boss crosses 50 %:
  - the current attack finishes, and its hazards expire;
  - the 120-tick (2 s) transformation begins when that attack's recovery begins and runs alongside it;
  - Phase 2 starts once both are done, so players never lose a punish window;
  - if the threshold is crossed during a slide or plant, no warning starts and the transformation begins at once.
- **Harmless and targetable.** The transformation deals no damage, and the boss doesn't heal or get a new health bar. A burst that crosses the threshold queues exactly one transition.
- **Defeat** cancels every warning, mark and trail at once, so nothing can land after victory. Defeat pose: the skates splay, the ring stops, and the reservoir cools to brass.
- **Cooldown hold.** If an attack's cooldown isn't ready when its turn comes, the boss holds its plant pose until it is. Windows are never shortened to fit a cooldown.
- **Seeded variation.** Targets and the Elbow side come from the encounter RNG (R6), with a pre-validated fallback picked before the tell.

### 4.1 Phase 1 loop (21.3 s plus slides)

Measured Spill → Rim Kick → Casting Run → Rim Kick → repeat.

| Attack | Ticks from warning to recovery end |
|---|---|
| Measured Spill | 390 |
| Rim Kick | 249 |
| Casting Run | 390 |

- **Cooldown check** (ticks between one recovery ending and the next time that attack is chosen): Spill 888 (needs 480), Run 888 (needs 540), Kick 390 (needs 300). All pass.
- **Slides** add up to 114 ticks before each Spill and each Kick, so the loop runs 21.3 to 28.9 s.

### 4.2 Phase 2 loop (33.9 s plus slides)

Elbow Cast → Rim Kick → Paired Pour → Empty the Ladle → Rim Kick → repeat.

| Attack | Ticks from warning to recovery end |
|---|---|
| Elbow Cast | 588 |
| Rim Kick | 249 |
| Paired Pour | 618 |
| Empty the Ladle | 330 |

- **Cooldown check:** Elbow 1446 (needs 660), Pour 1416 (needs 600), Ladle 1704 (needs 720), Kick 588 or more (needs 300). All pass.
- The Ladle follows Paired Pour, which ended with the boss about 6 m from a recipient, inside the ring band. So the Ladle threatens where the party is without needing to target anyone.
- Phase 2 opens with Elbow Cast.

Every join is strictly one after another, so patterns never overlap. Expected clear at the target damage output is about 60 s per phase, inside the packet's 90–150 s target.

## 5. Fairness: escape, stamina, collision and hit-stun

### 5.1 Method

The method is the same as the Bellkeeper and Sylvara specs. `crucible-skater-escape-check.ps1`:
- samples every boss position in the bound (0–9.5 m, using the arena's symmetry) and every direction in 10° steps;
- finds the reachable danger point farthest from safety. It uses a 0.1 m danger grid and a 0.05 m boundary grid, expands shapes by 0.3 m, and keeps player centres inside the arena and outside the bowl;
- converts that distance into walk-only and walk-plus-one-dash escape times, then into the minimum window for each difficulty.

Reaction margins: First Chime 0.60 s (walk only), Steady Ticking 0.45, Striking Hour 0.35, Midnight Toll 0.30.

**Simplifications** (these all make the check stricter than the real fight):
- **Casting Run and leg 1** count as if the whole swept lane went live at once when motion starts. In the fight, the far end only goes live 1.2 s later.
- **Elbow leg 2** is checked with leg 1's trail already cooled, which matches the rules. Both turn sides are checked, from every leg-1 start.

**Limits:**
- Players travel in straight lines.
- There are no other players and no acceleration.
- The dash moves at constant speed.
- Grid accuracy is about ±0.05 m.

This is evidence for single attacks and each two-beat attack, not proof that every combination is solvable.

### 5.2 Results (run on October 21, 2026, placeholder 24 m floor)

| Attack | d_max | Worst configuration |
|---|---|---|
| Casting Run / Elbow leg 1 | 1.81 m | Sideways out of a full 7 m lane |
| Elbow Cast leg 2 | 1.85 m | Sideways out of a full 5 m second lane |
| Spill / Paired Pour pool | 2.11 m | From the pool's centre |
| Rim Kick | **3.23 m** | Boss 8 m from the centre, facing 20° off the wall |
| Empty the Ladle | 2.34 m | From the middle of the ring |

The next table compares the minimum window, in ticks after lock, with this spec's window. A pair like 55 / 62 means the 6 m/s walk / the 4.2 m/s walk. The Steady Ticking column is walk-only, with no dash.

| Attack | First Chime, 6 / 4.2 | Steady Ticking, 6 / 4.2 | Striking Hour (dash) | Midnight Toll (dash) | Spec window |
|---|---|---|---|---|---|
| Casting Run / Elbow leg 1 | 55 / **62** | 46 / 53 | 29 | 26 | 60 |
| Elbow Cast leg 2 | 55 / 63 | 46 / 54 | 29 | 26 | 96 |
| Spill / Paired Pour pool | 58 / **67** | 49 / 58 | 30 | 27 | 60 |
| Rim Kick | 69 / 83 | 60 / 74 | 37 | 34 | 84 |
| Empty the Ladle | 60 / 70 | 51 / 61 | 31 | 28 | 108 |

**Findings:**
- **Every packet timing can be walked out of at Steady Ticking at both walk speeds.** No pattern needs a dash.
- **The tightest is Measured Spill at 4.2 m/s:** 58 ticks needed against a 60-tick window. The packet's own spot check (2.70 m out of 2.94 m) agrees.
- **Rim Kick is the largest escape** (3.23 m), but it doesn't track, so the full 1.4 s counts. It passes at both speeds.
- **First Chime** needs the Casting Run and Spill locks lengthened to 72 ticks for a 4.2 m/s walker (§9).
- **Widening the run lane** from 2 m to 3 m (D1) costs 0.5 m of escape (1.31 → 1.81 m) and stays well within budget.

### 5.3 Stamina budget

The rule is the Bellkeeper's: a worst-case player may need **at most one dash per 2.25 s**, and at most three in a row after 5 s without one.

This baseline needs **none**: every pattern can be walked out of at both speeds. Dash, jump and sprint only buy uptime. That matters because melee shouldn't spend stamina chasing a bowl that's meant to be read.

### 5.4 Collision, body and hit-stun

**Body collider (D2): a 1.5 m circle around the bowl.**
- The skates and the Phase 2 fins are visual only and never block. A player standing on a skate is outside the body.
- Measuring from the bowl edge keeps Rim Kick reach and the Ladle's inner band the same in both phases, even though the fins spread.
- The rig should fade or tuck skates that overlap a player in the high isometric camera.

**Why the run lane must widen (D1).** The packet gives a 2 m lane, but the bowl is 3 m across. Either:
- a player just outside the lane gets shoved by a bowl they "dodged", which is a surprise collision; or
- the bowl passes through them without effect.

Making the lane exactly the width the bowl sweeps means danger is what the body passes through, and the push (R20) only ever touches players who are already being hit. Sylvara made the same choice (her D1).

**Hit-stun:** 9 ticks.

**No knockback.** Every hazard is a one-hit footprint, and pushes only come from the moving body.

**Anti-chain rule.** For any two hazards that can hit the same player, the later one must resolve at least `stun + escape` ticks after the earlier one. These are the tightest pairs at 4.2 m/s; all pass:
- **Elbow:** the last leg-1 hit is at tick 179, and leg 2 resolves at 276. Gap 97 against 9 + 27 = 36 needed.
- **Paired Pour:** volley 2 locks at 258 and resolves at 318, after the last volley-1 hit at 209. Gap 109 against 9 + 30 = 39 needed.

**One hit per instance** (R16). Each leg and each volley is its own instance. Standing in both Elbow legs costs 36 HP, and so does failing both Pour volleys. A player who walks into a live trail or pool during its active time is hit once.

## 6. Party size (1–8)

Same geometry and timings for every party size. Counts use standing, active players at each warning.

| Attack | 1 | 2–4 | 5–8 |
|---|---|---|---|
| Casting Run / Elbow leg 1 | At the player | At a rotating target | Same |
| Measured Spill / each Pour volley | 1 pool | 2 pools | 3 pools |
| Rim Kick | Nearest player | Same | Same |
| Empty the Ladle | Ring on the boss | Same | Same |

**Targeting rules:**
- **Rotation** is round-robin by stable player ID from a seeded offset.
- **Runs:** no player is run at twice in a row if another standing player exists.
- **Pools:** no player gets a pool in two consecutive volleys if enough others exist. Paired Pour's two volleys continue the same rotation.
- **Nearest** is measured from the boss centre at warning start; ties go to the lowest stable player ID.
- **Merging.** Overlapping pools resolve as one damage event per player. At most three pools exist at once, never one per player. A pool locked at the wall always leaves the inward side open: the 24 m floor holds three expanded pools (≈ 42 m²) with room to spare.
- **Marked before lock.** Every recipient and run target is tagged with a shape on the HUD and over the player, not colour alone.
- **Duplicate classes.** No assignment depends on class.

## 7. Down, revive and disconnect

The same as the Bellkeeper's §7.

| Situation | Result |
|---|---|
| A pool recipient is downed before lock | That pool is cancelled |
| A run target is downed before lock | A new target is chosen |
| After lock | Every hazard is world geometry and resolves normally |
| Downed or inactive players | Hold no assignments and aren't hit |
| Disconnect | Treated as downed |

Every change emits `assignment_changed`.

**Practice** starts the Skater at full health in Phase 1 from the practice seed. **Fresh run** resets everything.

## 8. Damage and boss HP

Damage at Steady Ticking is the packet's: Casting Run, Spill, Kick and each Elbow leg or Pour volley 18; Empty the Ladle 26.
- The worst single mistake is 26 HP.
- The worst full two-beat failure is 36 HP.

Other tiers: First Chime ×0.8, Striking Hour ×1.0, Midnight Toll ×1.2.

**Boss HP** = measured reference sustained DPS × 120 s (solo, the middle of the packet's 90–150 s range). Measure DPS during movement and recovery, not on a stationary dummy.
- At an illustrative 40 DPS, that's 4800 HP.
- Co-op multiplier: `1 + 0.7 × (n − 1)`, applied to HP only, the same as Sylvara. Hazard density never scales with party size.

## 9. Difficulty tiers

Mechanics change, not just numbers. Warnings are never sped up across the board.

| Tier | Crucible Skater changes |
|---|---|
| **1 — First Chime** | Casting Run, Elbow leg 1 and Spill / Pour locks 72 ticks after lock (+12), so a 4.2 m/s walker escapes. Elbow leg 2 warns for 120. All recoveries +30 ticks. Phase 2 drops the second Rim Kick |
| **2 — Steady Ticking** | The baseline above |
| **3 — Striking Hour** (the packet's "Hard") | Authored sequences, with the same windows after lock: (a) Casting Run and Elbow leg 1 target the last pool recipient, who has just relocated; (b) Pour volley 2 follows its owner for only 30 ticks (window unchanged); (c) the Elbow bent arrow appears only when leg 1 cools, so the 96-tick leg-2 window is the only read; (d) recoveries −20 %, but the run, Elbow, Pour and Ladle never go below 3.0 s, and Kick never below 2.0 s |
| **4 — Midnight Toll** | Striking Hour plus damage ×1.2. No extra mechanic is designed for mini-bosses (D8) |

These rules hold at every tier:
- Patterns never overlap.
- The scheduler never skips a recovery to force a combo.
- The escape windows in §5.2 still pass.

## 10. Requests to the shared C# contract (#6)

The Skater reuses earlier requests:

| # | Need | From |
|---|---|---|
| R1 | Circle shape | Bellkeeper |
| R4 | Reassign or cancel before lock | Bellkeeper |
| R5 | Presentation markers | Bellkeeper |
| R6 | Seeded RNG | Bellkeeper |
| R10 | Lane shape | Bellkeeper |
| R11 | Mutable boss position | Bellkeeper |
| R12 | HP phases | Bellkeeper |
| R13 | HP damage | Bellkeeper |
| R14 | Recovery event | Bellkeeper |
| R16 | One hit per instance | Bellkeeper |
| R18 | Standing state | Bellkeeper |
| R20 | Body collision | Bellkeeper |
| R21 | Published movement constants | Bellkeeper |
| R22 | Active-duration hazards | Sylvara |
| R23 | Ordered hazard groups | Sylvara |
| R25 | Sector (cone) shape | Sylvara |

It adds:

| # | Need | Why |
|---|---|---|
| R26 | **Swept trail hazard:** a capsule from the start point to the body's current position, live from motion start to active end, then a `cooled` event | Casting Run and both Elbow legs. A static lane would damage the far end before the bowl arrives |
| R27 | **Annulus shape** with inner and outer radius around a fixed centre | Empty the Ladle |
| R28 | **Pre-warning locomotion state** (slide then plant), with an event at plant so presentation can show "planted, attack coming" | §3.3. The boss must never warn while sliding |

## 11. Resolved records

The fields are the same as the Bellkeeper's §12, with `encounter_id` = `crucible_skater`. Additions:
- `group_id` and `member_index` (Elbow legs 1/2, Pour volleys 1/2)
- `trail_progress` for run hits (how far along the lane the bowl was)
- `merged_pools` when several pools resolved as one event

Example card:

> *"Elbow Cast, second leg (1:41, Phase 2). You were 0.6 m inside the second lane. The bent arrow showed the turn 2.6 s earlier: wait in the cooled inner corner."*

## 12. Model, animation and asset notes

These come from the packet and sheet, with gameplay constraints.

- **Silhouette:**
  - A low, wide circular brass bowl on **exactly three** articulated skate legs: two forward, one rear.
  - A frontal ivory mask with a narrow gold reservoir slit (the core), and two short pouring spouts built into the rim. No arms.
  - The rear skate is small on the three-quarter sheet and must read clearly from the high isometric camera.
- **Scale:** the packet says 2.4 m tall. The sheet shows the bowl about 3 m across and the skates spanning about 5–6 m. The collider uses the bowl only (D2).
- **Phase 2:** the rim lifts on struts to reveal a segmented ring that rotates. The skates spread into curved fins. Keep the same three legs, mask and spouts.
- **Idle drips (D9):** the sheet shows molten drips from the spouts at rest. Keep them **gold, small and never pooling on the floor**. Coral and amber hatching is reserved for real warnings, and decorative drips must never look like a Spill.
- **Rig:**
  - three independent skate legs with planted feet (no foot sliding when planted)
  - a spout tip control per spout
  - rim lift and ring rotation for Phase 2
  - a fade or tuck for skates overlapping players
- **Animations:**
  - idle
  - slide (skates glide), then plant (three distinct clicks)
  - scrape-align (Run), skate run, stop and settle
  - spout tip and pour (Spill), toward each recipient
  - front skate lift and kick
  - bent-arrow turn (Elbow)
  - ring hum and reservoir drain (Ladle)
  - transformation (rim lifts, fins spread)
  - defeat (skates splay, the ring stops, the reservoir cools)
- **VFX:**
  - the full lane with a stop arrow, then a live trail that cools to dull brass
  - dashed, then latched, pool circles with a countdown; pools cooling into harmless brass discs
  - the bent arrow
  - the Ladle ring's radial ticks
- **Audio:** each cue also has a visual, so the fight reads with audio off.
  - scrape: Run
  - pouring chime: Spill
  - skate click: Kick
  - distinct turn chime: Elbow
  - rising hum and tick: Ladle
  - an audible cooling hiss when each trail or pool turns safe
- **Reward (packet proposal):** a Cooled-Brass rosette trophy and a harmless molten-to-brass material cosmetic. Neither grants a floor or damage effect.

## 13. Calls this lane made

The packet left these open, and nothing had been decided yet. Each one is applied above; any of them can be overridden.

| # | Decision | Call made here |
|---|---|---|
| D1 | Casting Run lane width: 2 m (packet) with a 3 m bowl | **Widened to 3 m** (the bowl's width), so the danger is what the body passes through (§5.4) |
| D2 | Body collider | **Bowl only, 1.5 m radius.** Skates and fins never block; reaches are measured from the bowl edge |
| D3 | Movement between attacks | **4.5 m/s, at most 1.5 s, then a 0.4 s plant.** Targets per §3.3 |
| D4 | Elbow turn side | **Fixed when leg 1 locks and shown by the bent arrow.** Must fit the bound; leg 2 is shortened (never bent) if it doesn't; under 2 m it's dropped |
| D5 | Paired Pour volley 2 | **Next recipients in rotation; follows its owner for 48 ticks, then a 60-tick lock**, the same lock window as volley 1 |
| D6 | Empty the Ladle centre and origin | **Cast in place** after Paired Pour; radii measured from the bowl edge |
| D7 | Brass Foundry floor dimensions and collision | Needed before T1. This spec uses the packet's 24 m placeholder |
| D8 | Midnight Toll for mini-bosses | **Striking Hour plus damage ×1.2**, with no extra mechanic for now |
| D9 | Idle molten drips on the sheet | **Keep them, but gold, small and never pooling** |
| D10 | Phase transition timing | **Runs alongside the finishing attack's recovery**, so no punish window is lost |

## 14. Validation tests

| # | Check |
|---|---|
| T1 | Rerun the escape check on the real Brass Foundry floor and bowl collider, for both movement sets |
| T2 | Run both loops in §4 at every tier, with real collision, finite stamina and hit-stun from every hit position |
| T3 | Elbow Cast: every start position and turn side the director can choose keeps a 2 m corridor and never traps a player between the lane and the wall |
| T4 | Parties of 1–8, including all-Vanguard, all-Arcanist and all-Chimesinger, with downs and disconnects during a pool lock and between Elbow legs |
| T5 | The slide and run body push never forces a player into a live hazard or the wall |
| T6 | No trail, pool or ring survives defeat, a practice reset or the end of its active time |
| T7 | Readability with audio off and reduced effects; idle drips are never mistaken for a Spill |
| T8 | Melee time-to-first-hit after each recovery starts, compared with ranged, aiming for median clear times within 15–20 % |
