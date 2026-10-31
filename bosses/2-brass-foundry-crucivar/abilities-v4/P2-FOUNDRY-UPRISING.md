# Crucivar v4, Phase 2: Foundry Uprising (proposal)

**Status: accepted for now (Chzy18, 28 Oct 2026), after watching the Unity prototype (`-lastbellAbilitySet v4`, FoundryUprisingTest 11/11, 33c3584). Numbers are placeholders.** Built on Chzy18's add and bullet-hell idea (27 Oct). Chzy18 chose to make it its own phase with the boss still fighting, not an intermission.

## Phase
- **HP band:** 65–30 % (Casting Heat).
- **Opening:** the second-form change at 65 % plays first: insulation falls, harmless, same bar, no heal. Phase 1's brand pools clear at the change.
- **Theme:**
  - The foundry rises against you. Lava turrets climb out of the perimeter lava, and Crucivar pours a Molten Casting to drive each one.
  - The party has to split up, cross bullet fire, kill the castings and dodge the boss at the same time.
- **Compared with Aurelion v4 Phase 2:** both run three or more patterns at once. Aurelion's come from memory and towers. Here they come from bullet fields, assignments and a wipe timer.

## Mechanics (Medium)

### Uprising: turrets and castings (R&S block 20 painshare, block 15 bullets, blocks 7/9 numerals and links)
- **Turrets:**
  - 2–4 turrets rise from the lava at fixed rim bays, 2.5 s rise, harmless.
  - Each gets an ivory numeral (I–IV).
  - They can't be damaged.
- **Castings:**
  - For each turret, Crucivar pours a **Molten Casting**: a 1.2 m body, stationary, with the same numeral and a glowing chain to its turret.
  - Each one stands 6–9 m from the centre, **on the far side from its own turret**, so reaching it means crossing that turret's fire.
- **Killing a casting:**
  - **400 HP each** at N = 1, scaled like the boss.
  - Melee and ranged can both hit it.
  - **Painshare:** 50 % of the damage it takes also hits Crucivar.
  - When it dies, its turret sinks and its patterns stop.
- **Failure:**
  - **30 s** after the turrets rise, every turret still up floods the floor (**Pour Flood**) and the party wipes.
  - **Warning:**
    - a big countdown over each turret from 20 s;
    - the lava rim rising from 25 s;
    - the floor glowing coral from 28 s.
  - The first Uprising introduces it and the boss attacks lightly (see the timeline). Later Uprisings layer on top of it.

### Turret patterns (every stream draws its real path at least 2.0 s first)
- **Lane Stream:** three parallel lanes of bullets marching 6 m/s across the arena from the turret, 2.5 m gaps between the lanes.
- **Ring Wave:** a ring of bullets expanding 5 m/s from the turret, with two 2.5 m gaps at the drawn angles.
- **Spiral Arm:** three bullet arms turning 30°/s around the turret, bullets marching out 6 m/s.

Common to all three:
- **Bullets:** 0.5 m dots, Strike, **20 damage each**, one hit per bullet.
- **Rhythm:** each turret repeats its pattern every 4 s: a 2.0 s path, then fire, then a 1.0 s rest. The turrets are staggered so they don't all fire on the same beat.

### Boss during the phase
He stays at the centre and keeps using Phase 1 attacks, so the bullet fire is layered over them:
- **Ember Brand:** its pools last until the phase ends, so the floor fills again as the soft enrage.
- **Furnace Spin:** 4 jets.
- **Backdraft:** its pull drags players away from their castings.
- **Casting Order** and **Anvil Quake:** only in the gap between Uprisings.

## Timeline: one cycle of about 48 s, repeating until 30 %

| t (s) | Event | Live patterns |
|---|---|---|
| 0.0 | Uprising: turrets rise, castings poured, numerals and chains shown | — |
| 2.5 | turrets start firing (paths drawn first) | bullets |
| 4.0 | Ember Brand (lands 7.5) | bullets + brand |
| 11.0 | Furnace Spin (live 12.5–16.5) | bullets + jets + pools |
| 19.0 | Backdraft (pull 20.6–21.0, blast 22.0) | bullets + blast + pools |
| 30.0 | **Pour Flood** if any turret is still up: wipe | — |
| last casting dies | turrets gone; the boss staggers: **3.5 s punish** | pools |
| +3.5 | Casting Order | pours + pools |
| +6.0 | Anvil Quake (after the last pour) | quake + channels + pools |
| +10.0 | next Uprising: turrets at new bays, each one stepping up a pattern | |

- **First Uprising:** Furnace Spin and Backdraft are left out, so the phase is introduced before it's layered.
- **Scaling every cycle:**
  - Uprising 2 gives each turret a second pattern, alternating between them.
  - Uprising 3 cuts the rest to 0.5 s.

## Party size

| Party | Turrets | Notes |
|---|---|---|
| 1 | 2, **in sequence** | the second rises when the first sinks; the timer gives each one 18 s |
| 2 | 2 | one casting each, or both together |
| 4 | 3 | one casting needs two people, or one fast pair clears two |
| 8 | 4 | the group must split into four teams |

Casting HP scales like the boss, `1 + 0.7 × (fighters − 1)`, so splitting up is always the fastest way.

## Melee and ranged danger
- **Ranged:** the turrets sit on the rim, so the edge is where the bullets are thickest. The castings also pull ranged players into the middle.
- **Melee:** the boss's attacks keep the area near him dangerous, and Backdraft drags people back in.
- No spot is safe for the whole phase.

## Easy and Hard
- **Easy:** paths drawn 2.5 s, timer 40 s, damage ×0.6.
- **Hard (named extra: Cast in Order):** castings must die in numeral order. Killing one out of order makes every remaining turret fire a Ring Wave at once.

## Escape check (to add to `crucivar-escape-check.ps1` once accepted)
Worst cases to check:
- crossing a Lane Stream gap at 4.2 m/s while carrying a brand;
- a Ring Wave gap lining up with a Furnace Spin jet;
- the solo player's route to the second casting under a Spiral Arm.

## Conflicts with the rules
None found.
- The castings are boss parts with painshare, not trash mobs.
- Bullet paths keep the 2.0 s warning floor.
- The wipe has an impossible-to-miss warning and is introduced first.

## Open questions
1. Bullet warnings: keep 2.0 s, or allow shorter for the 0.5 m dots (R&S goes below 2 s only for dot bullets)?
2. Painshare at 50 %, and casting HP at 400 per fighter-scale: are these about right? Final numbers get set from measured DPS.
3. Turret count for 3, 5, 6 and 7 players. The proposal is 2, 3, 3, 4.
4. Should Phase 2's brand pools also keep filling the floor all phase (soft enrage again), or should they expire here because the bullets already fill the space?
