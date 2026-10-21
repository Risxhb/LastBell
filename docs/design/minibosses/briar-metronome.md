# Briar Metronome, Pruner of Lost Seasons — encounter specification

Draft v0.1 · October 21, 2026 · Verdant Chimes mini-boss 1 of 3 · Source: Codex's nine mini-boss handoff, branch `docs/nine-mini-bosses-20261021` (commit `a2ab311`, `docs/mini-bosses/design.md` §7 and `03-verdant-chimes.png`, row 1).

Status: **specification only. Nothing here is implemented.** It follows the method of the [Bellkeeper spec](https://github.com/Risxhb/collab/blob/docs/bellkeeper-spec/docs/design/bellkeeper.md) ([PR #16](https://github.com/Risxhb/collab/pull/16)) and the [Sylvara spec](https://github.com/Risxhb/collab/pull/50), so all the bosses share one validator method and one set of contract requests. The packet supplies the identity, abilities, geometry and timings. This spec adds what the packet leaves open: tick timings, schedules, targeting, party scaling, escape evidence and fixes for the places where the packet conflicts with decisions already made.

Companion tool: [`tools/encounter-escape/briar-metronome-escape-check.ps1`](../../../tools/encounter-escape/briar-metronome-escape-check.ps1).

## 1. Where this spec departs from the packet

Already-settled project decisions win over the packet. Where nothing was decided, this spec picks a value and records it in §13 so it can be reviewed.

| # | Packet says | This spec | Why |
|---|---|---|---|
| C1 | Validate against the 4.2 m/s prototype walk and a proposed 6 m/s walk | **6 m/s is the gate** (the run loop's `PlayerController.WalkSpeed`); 4.2 m/s is still reported | Keep the current numbers (Bellkeeper v0.3 decision) |
| C2 | Player radius 0.45 m plus 0.25 m clearance | **0.3 m hit margin**, as in `Hazard.Margin` | Current runtime value, used by the Bellkeeper and Sylvara checks |
| C3 | Designs and validates solo first | **Tuned for co-op.** Geometry and timings stay the same for any party size, and HP scales | The user's co-op-first direction |
| C4 | Vanguard Brace, Arcanist seals and Chimesinger heals are listed as optional | Unchanged. The approved class kits (#31/#36/#41) stand, and **nothing here needs any of them** | Approved kits stand |
| C5 | No post-hit immunity; dash has no assumed iframes | Same, stated as settled: **dodging never makes you immune** | Settled decision |
| C6 | Health and potions are not discussed | Health refills before this fight. Potions carry over from earlier mini-bosses; the boss never gives healing | Run-loop rules |
| C7 | Combat disk 24 m across | **Run-loop arena: player centres ≤ 11.5 m, boss centre ≤ 9.5 m** (`CombatWorld`) | That is the only arena that exists. Rerun the check when Verdant Chimes has collision (D7) |
| C8 | Ranged players face only Seedline Thrust in Phase 1 (reach about 10 m) and a Winter Rest limited to 8 m | **Seedline Thrust aims at the farthest player. Winter Rest has no range limit** | Otherwise a distant caster takes almost no pressure, which breaks the packet's own 15–20 % melee/ranged target (§6) |
| C9 | "Steps closer if a player retreats" doesn't say which player | **A 0.9 s replant step before every prune**, toward the nearest standing player | Defines the stalk for 2–8 players and spaces the cooldowns (§4) |
| C10 | The Overgrown Reach outer arc has no width | **150° (half-angle 75°)**, 3–5 m from the body | It must cover the sidesteps that escaped the 90° cone, so standing on the flank doesn't win every time |
| C11 | Returning Prune doesn't say which way the second beat faces | **Same facing as beat 1** (locked at the start of beat 1) | Fixed facing is the packet's stated weakness |

## 2. Baseline values

**Units are metres and seconds; the simulation runs at 60 Hz.** The packet measures radii from the boss's collision edge. Runtime shapes start at the boss centre, so every radius below is converted: **centre radius = packet radius + 1.0 m body**.

| Value | Used here | Source |
|---|---|---|
| Walk | 6 m/s (gate); 4.2 m/s reported | `PlayerController` / #27 |
| Sprint | 9 m/s, never required | `PlayerController` |
| Dash | 3 m, no immunity | `PlayerController`, settled decision |
| Stamina | 100, shared by sprint, jump and dash | `PlayerController` |
| Hit margin | 0.3 m; touching a border counts as inside | `Hazard.Margin` |
| Player health | 100 HP | `PlayerController.MaxHp` |
| Hit-stun | 9 ticks (0.15 s); no knockback | Packet |
| Boss body | **1.0 m radius** collision circle around the two planted root feet (about a 2 m stance on the sheet, 3.4 m tall) | This spec |
| Arena | Circle; player centres ≤ 11.5 m, boss centre ≤ 9.5 m | `CombatWorld` (C7) |

## 3. Attack vocabulary

W/A/R and CD are the packet's. Ticks are exact conversions. Damage uses the packet's normalized values at Steady Ticking (M 18, H 26).

| ID | Attack | Phase | Shape (from the boss centre) | Aim and lock | Warning → damage | Active | Recovery | CD | Damage |
|---|---|---|---|---|---|---|---|---|---|
| `inward_prune` | **Inward Prune** | 1 | Annular sector, 100° (half 50°), radius 3.0–5.5 m | Nearest standing player; facing locks at warning | 96 | 18 | 156 (2.6 s) | 360 | 18 |
| `outward_prune` | **Outward Prune** | 1 | Cone, 90° (half 45°), to 4.0 m | Nearest standing player; locks at warning | 84 | 15 | 150 (2.5 s) | 300 | 18 |
| `seedline_thrust` | **Seedline Thrust** | 1 + 2 | Lane 1.4 m wide, 10 m long, clipped at the wall | **Farthest** standing player (C8); tracks for 48 ticks, then locks for the final 60 | 108 (60 after lock) | 18 | 180 (3 s) | 480 | 26 |
| `returning_prune` | **Returning Prune** | 2 | Beat 1 = the Inward Prune sector. Beat 2 = cone, 75° (half 37.5°), to 3.5 m, same facing | Nearest; both beats lock at beat 1's warning | Beat 1: 96. Beat 2 warns at tick 114, resolves at 198 (84) | 18 each | 204 (3.4 s) after beat 2 | 480 | 18 per beat |
| `overgrown_reach` | **Overgrown Reach** | 2 | Beat 1 = the Outward Prune cone. Beat 2 = annular sector, 150° (half 75°), radius 4.0–6.0 m, same facing | Nearest; both lock at beat 1's warning | Beat 1: 84. Beat 2 warns at tick 99, resolves at 189 (90) | 15 each | 192 (3.2 s) after beat 2 | 480 | 18 per beat |
| `winter_rest` | **Winter Rest** (new) | 2 | Circle, radius 2.0 m, on each marked player | 1 / 2 / 3 players (§6); follows for 48 ticks, then locks for the final 60 | 108 (60 after lock) | 18 | 210 (3.5 s) | 660 | 26 |

**Per-attack notes:**

- **Inward Prune.** The hook curls toward the chest with a dry wooden tick, and the ground arc shows **inward arrows**. Melee steps inside the 3.0 m edge (a 2 m refuge outside the 1.0 m body) or round the shoulder. Ranged stays beyond 5.5 m or crosses a side edge.
- **Outward Prune.** The blade folds across the chest with a sharp chime, and the cone shows **outward arrows**. Melee leaves sideways; ranged changes angle or stays beyond 4 m. Its pose, sound and arrows must differ from Inward Prune at a glance from the high oblique camera (§10).
- **Seedline Thrust.** The blade points and a root cord tightens between the feet and the target. The lane follows the target for 0.8 s, then latches with a visible click and a solid edge. Sidestep to either side. The counterweight side is the punish position, because the blade arm is left extended there during recovery.
- **Returning Prune.** Beat 1 is Inward Prune unchanged, so the inner refuge is still the answer. After beat 1's active ticks the blade retracts and the arc is removed. A fresh 75° inner cone then warns for 84 ticks with inward arrows, aimed through the refuge. Answer: refuge first, then step sideways out of the inner cone (the boss's flank inside 3.5 m stays safe), or stay out at range. Recovery 3.4 s.
- **Overgrown Reach.** Beat 1 is Outward Prune unchanged, so sidestepping is still the answer. The cone is removed, and an outer 150° arc from 4.0–6.0 m warns for 90 ticks with expanding chevrons. It covers the flank positions people sidestepped to. Answer: step back in front of the boss (the cleared cone is now safe) or move past the outer edge. Recovery 3.2 s.
- **Winter Rest.** The fronds close with a brittle crackle; a fallen-petal marker follows each chosen player and locks 1 s before the single pulse. While the counterweight rewinds the boss holds still for 3.5 s, the major punish window. Bait the marker beside the body and then circle away; ranged just leaves the circle. There's no range limit (C8). The packet's "within 8 m" is dropped.

**Multi-beat rule.** A beat's footprint is removed before the next beat warns, and each beat is its own hit (up to 18 + 18). Beat 2 never warns while beat 1 is still active. The anti-chain rule is covered in §5.4.

## 4. Phases and timeline

| Phase | Name | Boss HP | Visual | What it teaches |
|---|---|---|---|---|
| 1 | Patient Pruning | 100–50 % | Closed mantle, pendulum inside the chest, curved blade | Inward vs outward: does the blade protect or threaten the inner space? |
| 2 | Unseasonal Growth | 50–0 % | Chest shutters open like fern fronds, counterweight swings out to the side, blade becomes a hook | Each read now has a second beat that punishes staying in the first safe spot |

**Shared rules** (the packet's, made exact):

- **Transition.** Crossing 50 % queues the transition until the current attack's last beat and recovery finish. Then comes a **120-tick (2 s)** harmless unfolding. The boss stays targetable, gains no health bar and doesn't heal. A burst through the threshold queues only one transition.
- **Defeat** cancels every warning and marker at once.
- **Cooldown hold.** If an attack isn't off cooldown when its turn comes, the boss holds its recovery pose until it is. Windows are never shortened.
- **Replant step (C9).** Before every prune (Inward, Outward, Returning, Overgrown), the boss takes a 54-tick (0.9 s) harmless step. If the nearest standing player is more than 4 m from its edge, it moves up to 1.5 m toward them. Then it plants. Thrust and Winter Rest don't replant.
- **Seeded variation.** Thrust and Winter Rest targets come from the encounter's seeded random number generator, so a run can be replayed exactly. Nothing reads player inputs.

### 4.1 Phase 1, Patient Pruning (loop 31.1 s)

`[X → Y → Thrust → Y → X → Thrust]`, where (X, Y) is chosen once per loop by the seed: **(Inward, Outward)** or **(Outward, Inward)**.

- The cut direction changes every prune. The order flips between loop halves and from loop to loop, so circling in one direction can't answer every cut (the packet's exploit note).
- **Durations:** Inward 5.4 s, Outward 5.05 s (each including the 0.9 s replant), Thrust 5.1 s.
- **Cooldowns check out with the replant step.** The tightest gap is a prune repeating across one Thrust: recovery end → Thrust (5.1 s) → replant (0.9 s) = 6.0 s, which exactly meets Inward's 6 s CD. Thrust repeats after two prunes (at least 10.45 s) against an 8 s CD. No hold is needed.

### 4.2 Phase 2, Unseasonal Growth (loop 26.1 s)

`[Returning → Thrust → Overgrown → Winter Rest]`, repeating. The loop begins with Returning or Overgrown, chosen by the seed.

- **Durations:** Returning 7.9 s, Overgrown 7.5 s (each including the replant), Thrust 5.1 s, Winter Rest 5.6 s.
- Returning and Overgrown always alternate, with one other attack between them.
- Gaps from recovery end to the next warning of the same attack are all at least 19 s, against CDs of 8 s (prunes, Thrust) and 11 s (Winter Rest). No holds.
- The packet's example "Returning Prune → punish → Overgrown Reach" is kept with Thrust between them, so a caster is forced to move between the two cuts.

**Only one damaging pattern at a time, in every phase.** Every join is sequential.

## 5. Fairness: escape, stamina, collision and hit-stun

### 5.1 Method

The method is the same as the Bellkeeper's and Sylvara's. `briar-metronome-escape-check.ps1`:
- samples every boss position in the bound (0–9.5 m, by symmetry) and every facing in 10° steps;
- finds the reachable danger point farthest from safety, with hit tests copied from the run loop's `Hazard.Contains`;
- converts that distance into walk-only and walk-plus-one-dash escape times, then into the minimum window for each difficulty.

Reaction margins: First Chime 0.60 s (walk only), Steady Ticking 0.45, Striking Hour 0.35, Midnight Toll 0.30. Each beat of a two-beat attack is checked on its own, since the earlier footprint is gone by then (§3).

**Limits:** straight lines (no routing around the body), no other players, no acceleration and grid accuracy of about ±0.05 m. It checks one Winter Rest marker. Merged markers are bigger but have more edge to leave by, and are covered by T3.

### 5.2 Results

Run on October 21, 2026, in the run-loop arena (about 7 s).

| Attack | d_max | Worst configuration |
|---|---|---|
| Inward Prune (also Returning beat 1) | 2.62 m | Boss 6 m from centre, facing the wall |
| Outward Prune (also Overgrown beat 1) | 2.79 m | Boss 8 m from centre, facing the wall |
| Seedline Thrust | 1.40 m | Sideways out of the lane |
| Returning Prune beat 2 (inner cone) | 1.91 m | Boss 8 m from centre, facing the wall |
| Overgrown Reach beat 2 (outer arc) | 2.01 m | Boss 6 m from centre, facing the wall |
| Winter Rest (one marker) | 2.31 m | From the marker's centre |

Minimum window in ticks, counted from warning (prunes, beats) or from lock (Thrust, Winter Rest). First Chime is walk-only; the other tiers allow one dash.

| Attack | First Chime, walk 6 / 4.2 | Steady Ticking | Striking Hour | Midnight Toll | Spec window |
|---|---|---|---|---|---|
| Inward Prune | 63 / 74 | 38 | 32 | 29 | 96 |
| Outward Prune | 64 / 76 | 39 | 33 | 30 | 84 |
| Seedline Thrust | 50 / 56 | 33 | 27 | 24 | 60 after lock |
| Returning beat 2 | 56 / 64 | 35 | 29 | 26 | 84 |
| Overgrown beat 2 | 57 / 65 | 36 | 30 | 27 | 90 |
| Winter Rest | 60 / **69** | 37 | 31 | 28 | 60 after lock |

**Findings:**
- **Every packet timing can be escaped at every tier, with no dash, at 6 m/s.** Even walk-only with the First Chime reaction margin, every window is met at 6 m/s. Winter Rest is the tightest, meeting its 60-tick window exactly.
- **At the 4.2 m/s prototype walk,** only Winter Rest misses a walk-only First Chime window (69 needed, 60 given). First Chime drops Winter Rest anyway (§9).
- **The cone escapes are the longest,** about 2.8 m when the boss faces the wall from near the edge. The anti-chain margin (§5.4) and the 2 m corridor rule are still easily met.
- The Seedline Thrust is the easiest dodge (1.4 m). Its real job is making ranged players move, not damage (C8).

### 5.3 Stamina budget

The Bellkeeper's rule applies: a worst-case player needs **at most one dash per 2.25 s**, and sprint and jump are never required. **The Briar Metronome needs no dash at all at Steady Ticking**, at either walk speed. Walk time plus the 0.45 s reaction margin fits every window; the closest is Winter Rest at 4.2 m/s, 60 needed against 60. Stamina is purely for gaining uptime: a melee player who dashes back into the refuge after an Inward Prune gets more hits in, but never has to.

### 5.4 Collision, body and hit-stun

- **Body: a 1.0 m circle** around the two planted feet. The feet never slide. The moss mantle, petals, counterweight arm and blade are decoration and never block movement. During the replant step the body pushes players aside (contract R20) without damage.
- **No contact damage and no knockback.** Hit-stun is 9 ticks (0.15 s).
- **Anti-chain rule.** For two hazards that can hit the same player, the later one must resolve at least `stun + escape` after the earlier one. The tightest pairs are the second beats: Returning beat 2 resolves 102 ticks after beat 1, and Overgrown beat 2 resolves 105 ticks after beat 1. Both are well above 9 ticks plus the beat-2 escape times in §5.2.
- **One hit per instance** (R16). Both beats of one attack can hit (36 HP). With health full on arrival, the worst sequence (miss both beats, then miss the Thrust) costs 62 HP, so one bad read is never lethal.

## 6. Party size (1–8)

Same geometry and timings for every size. Counts use standing, active players at each warning.

| Attack | 1 | 2–4 | 5–8 |
|---|---|---|---|
| Prunes (all four) | At the nearest player | Same | Same |
| Seedline Thrust | At the player | At the farthest player, never the same one twice in a row if another standing player is more than 6 m away | Same |
| Winter Rest | 1 marker | 2 markers | 3 markers |

- **Nearest / farthest** are measured from the boss centre at warning. Ties go to the lowest stable player ID.
- **Winter Rest recipients** rotate round-robin by stable player ID from a seeded offset. Nobody gets two volleys in a row while enough other players are standing. Markers that overlap merge into one damage event. If a placement would block every exit, the latest-locking marker shifts away up to 2 m along the target's facing before it locks.
- **Marked before lock.** Each Thrust target and marker owner gets a shape over their head and on the HUD, never colour alone.
- **Why the farthest player (C8).** Melee players stand inside 3 m for prune punishes, so the nearest-player prunes test them. Thrust and Winter Rest test whoever is far away. Ranged players get the packet's "keep changing firing lanes" pressure in both phases, without the boss leaving the melee players.
- **No class dependency**, and no assignment that needs two players to do something at the same moment.

## 7. Down, revive and disconnect

- A marked player who goes down before lock loses the marker; it is not reassigned.
- A Thrust or prune target who goes down before lock: the boss re-aims at the next valid player.
- After lock, every hazard is fixed in the world and resolves normally.
- Downed and inactive players are never chosen as targets and aren't hit.
- A disconnect counts as being downed.

## 8. Damage and boss HP

- **Damage at Steady Ticking:** prunes 18 per beat, Thrust 26, Winter Rest 26. First Chime ×0.8, Striking Hour ×1.0, Midnight Toll ×1.2.
- **Boss HP:** the packet targets **90–150 s** per kill. HP = measured reference sustained DPS × 120 s for one player, measured during real movement and recoveries, not on a dummy. Co-op multiplier: `1 + 0.7 × (n − 1)`, applied to HP only. Until measured, the run-loop stand-in's 1500 HP is the placeholder.
- No enrage and no healing requirement. Winter Rest's 3.5 s recovery is the 3–4 s major opening the packet requires for Vanguard's heavy attack and casters' stationary spells.

## 9. Difficulty tiers

The packet sets Hard at the third tier, **Striking Hour**. The tiers change mechanics, not warning speed.

| Tier | Briar Metronome changes |
|---|---|
| **1, First Chime** | Phase 2 drops Winter Rest from the loop. All recoveries +0.5 s. Every remaining window then works walk-only at 4.2 m/s (§5.2) |
| **2, Steady Ticking** | The baseline above |
| **3, Striking Hour** | Phase 2 adds **Pruning Sequence**: Returning Prune runs straight into Overgrown Reach, with Returning's recovery cut to 60 ticks before Overgrown's beat 1 warns. Every footprint is still removed before the next warns, and the three safe spots chain inner → flank → front. Recoveries −20 %, never below 0.6 s; Winter Rest keeps 3.5 s |
| **4, Midnight Toll** | Striking Hour plus Winter Rest markers **follow for 24 extra ticks** (the lock moment stays 60 ticks before the pulse). Outward Prune in Phase 1 gains its Phase 2 outer arc on every other cast |

## 10. Model, animation, camera and asset notes

- **Silhouette:** a slender 3.4 m leaning brass metronome on exactly two root-wrapped legs. Ivory wedge mask, moss mantle with violet petals, chest gold pendulum, curved clock-hand pruning blade on the right arm, counterweight sphere on the left. Phase 2: fern-frond chest shutters open, the counterweight swings out sideways, and the blade turns into a hook. Both legs are the same in both phases (sheet `3-1-verdant-chimes-briar-metronome.png`).
- **Camera readability (the packet's first tuning item).** The body leans over the inner refuge, the most important space in the fight. So:
  - ground telegraphs render on top of the boss (an x-ray outline where the body covers them);
  - the mantle fades to 40 % when a player is under it;
  - the warning's arrows (inward or outward) are drawn **outside** the body footprint, so the direction is readable even when the body covers the middle.
- **Inward vs outward tells** must be readable with the sound off: hook curled to the chest vs blade folded across the chest, plus the arrow direction on the ground.
- **Rig:** two root legs with separate planting, a pendulum that swings with the tempo, blade arm, counterweight arm, mask, and a frond shutter set for Phase 2.
- **Animations:** idle (pendulum ticking), replant step, inward cut, outward cut, thrust, frond-close rest (Winter Rest), counterweight rewind, transformation, defeat (pendulum stops, blade lowers into the moss).
- **VFX:** coral/amber hostile edges and countdown hatching. The arena's turquoise inlay is never used for danger. Petal markers for Winter Rest; root cord for Thrust.
- **Audio:** dry wooden tick (inward), sharp chime (outward), cord creak (Thrust), brittle crackle (Winter Rest), metronome tempo slowing to a stop at defeat.
- **Reward (proposal only):** Lost-Season pruning charm and a violet-petal cosmetic accent. It never gives healing or power.

## 11. Requests to the shared C# contract (#6)

Reuses the Bellkeeper's and Sylvara's requests (R1 circle, R4 reassign/cancel before lock, R5 markers, R6 seeded RNG, R10 lane, R11 boss position, R12 HP phases, R16 one hit per instance, R18 standing state, R20 body collision, R22 active windows, R23 ordered hazard groups, R25 sector). New:

| # | Need | Why |
|---|---|---|
| BM-1 | **Annular sector** shape: inner radius, outer radius, facing, half-angle. The run loop's `Ring` plus a `HalfAngle` limit | Inward Prune, Returning beat 1 and Overgrown beat 2 |
| BM-2 | **Direction glyphs on a telegraph** (inward or outward arrows), drawn outside the body footprint | The fight's main read (§10) |
| BM-3 | **Telegraph drawn over the boss** (x-ray edge) and a mantle fade near players | The leaning body hides the inner refuge |
| BM-4 | **"Farthest standing player" targeting** with a no-repeat rule | Seedline Thrust (C8); Sylvara's Procession needs the same thing |

## 12. Validation tests

| # | Check |
|---|---|
| T1 | Rerun the escape check on the real Verdant Chimes arena and body collider, for both movement sets |
| T2 | Every sequence in §4, at every tier, with real collision, finite stamina and hit-stun from every hit position |
| T3 | Winter Rest with 2 and 3 markers, merged and next to the wall, in parties of 5–8: an exit always stays at least 2 m wide |
| T4 | Parties of 1–8, including all-Vanguard, all-Arcanist and all-Chimesinger, with downs and disconnects during a two-beat attack and during the phase threshold |
| T5 | Melee vs ranged clear time within about 15–20 % (packet). Ranged gets pressure in Phase 1 from the farthest-target Thrust |
| T6 | Camera read: from the run loop's high oblique camera, a new player can tell inward from outward within the reaction margin, including when standing under the mantle |
| T7 | Audio-off and reduced-effects read; the turquoise inlay is never mistaken for a hazard |

## 13. Decisions for review

These are this spec's choices where no earlier decision existed. Each is applied above and can be changed.

| # | Decision | Chosen |
|---|---|---|
| D1 | Body radius | 1.0 m (two planted feet) |
| D2 | Stalk rule | Replant step before every prune, toward the nearest player (C9) |
| D3 | Ranged pressure | Thrust aims at the farthest player; Winter Rest has no range limit (C8) |
| D4 | Overgrown Reach outer arc width | 150° (C10) |
| D5 | Second-beat facing | Same as beat 1 (C11) |
| D6 | Striking Hour and Midnight Toll changes | §9 |
| D7 | Verdant Chimes arena dimensions | Run-loop arena until the area has collision (C7) |
