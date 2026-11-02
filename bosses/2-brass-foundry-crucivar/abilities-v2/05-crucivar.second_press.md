# 05 · Second Press (`crucivar.second_press`) — proposal, Hard only

Part of Crucivar's ability set v2 (`ABILITIES.md`, same folder). Build only this ability. It's Hard's one named extra mechanic.

## Fantasy

The moulds have just been poured. Crucivar wrenches its hammer free and slams it down again on the fullest mould to press the cast. It's the packet's "Second Press", reworked for the new moulds.

**Look:** a coral circle with a hammer-face cross on the chosen mould, and the hammer's three-notch ratchet sound.

## Shape and numbers (Hard)

- **Trigger:** 2.0 s after the Mould Press towers resolve. For solo, that's after tower II.
- **Shape:** circle, r **2.2 m**, centred on the mould that held the **most players** at resolve. Ties go to the lowest numeral, then NE → SE → SW → NW. **Strike** (Brace helps, −60 %).
- **Telegraph:** **2.0 s**, locked at 0 (the 2 s floor).
- **Damage:** **25**, with 1.5 m knockback **toward the arena centre** and 0.25 s stun. Hard's ×1.3 damage taken applies on top.
  - The push goes inward because in phase 2 the press (resolving at 6.2 s) lands while the slag edge still burns (until 7.0 s). An outward push from the mould edge (8.5 m) would end at 10.0 m, inside the burning edge from 9.5 m.
- **Overlap:**
  - Phase 2: the slag edge may still be burning, so slag edge + press makes two patterns.
  - Phase 3: it's alone.
- It's never the only escape route: a small circle on one bay.

## Escape route

| Case | Distance | 4.2 walk | 4.2 + dash | 6 m/s | Window | Margin |
|---|---|---|---|---|---|---|
| leave the re-stamped mould from its centre | 2.5 m | 0.60 s | 0.17 s | 0.42 s | 2.0 s | 1.83 s |

(See `escape-hard.md`.)

- **Melee and ranged:** whoever soaked the mould is standing in it. They must step out right after the soak, whatever their role.
- **Standing still in the mould is hit.**

## Co-op and solo

- No co-op rule.
- **Solo:** it presses tower II (the last one you soaked).
- **N = 2 / 4 / 8:** it presses the fullest mould.

## Phase use

- Hard only, after every Mould Press: phase 1, phase 2 (with the slag rim) and phase 3.

## Easy / Medium

Not present on Easy or Medium.

## R&S source

- R&S harder modes add whole attacks (`condDifficultyCheck`, §2).
- The packet's "highest-tier Second Press".
- §4 block 2 follow-up.

## Escape-check plan

- **Worst case:** a player standing at the mould centre when it resolves.
- **Also test:** in phase 2, a player hit at the mould's outer edge is pushed toward the centre, never into the burning slag edge.

## Build notes

- **Code:** `Run/Encounters/Crucivar/Abilities/SecondPress.cs`.
- **Inputs:** reads Mould Press's resolved-tower result (player counts per tower). Coordinate the small interface with the Mould Press session through the Abilities session, e.g. an event `MouldPress.Resolved(towerCentres, counts)`.
- **Hard check:** read `CombatWorld.Difficulty == Difficulty.Hard`.
- **Practice hook:** `-lastbellAbility crucivar.second_press`. It fires a Mould Press plus a Second Press regardless of difficulty, and the test also checks that it doesn't fire on Medium or Easy.
