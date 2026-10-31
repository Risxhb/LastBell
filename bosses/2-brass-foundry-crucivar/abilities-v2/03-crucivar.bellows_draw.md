# 03 · Bellows Draw (`crucivar.bellows_draw`) — proposal

Part of Crucivar's ability set v2 (`ABILITIES.md`, same folder). Build only this ability. **This is the ranged punisher.**

## Fantasy

Crucivar plants itself on the anvil and works the furnace like a bellows:
1. The gear-tooth rim tips its slag troughs inward, flooding the outer ring of the floor.
2. Every belly shutter, flank grille and the back exhaust swing open, and the furnace **inhales**, dragging everyone toward it.
3. It **exhales** in a point-blank blast through all four vents.

**Look:**
- The slag rim is a coral/amber hatched annulus from the rim wall in. After it lands, it becomes a dark-molten standing pool that cools over 5 s.
- The blast is a coral circle around the body.
- The inhale shows inward amber chevrons on the floor plus the vent shutters opening (`CrucivarRig.Vents`), with a rising bellows sound.

## Shape and numbers (Medium)

Crucivar first walks to within 3 m of the arena centre (at most 1.5 s at 1.4 m/s). Then, from the cast at t = 0:

| t (s) | Part | Shape | Damage | Notes |
|---|---|---|---|---|
| 0 → **2.0** | **Slag Rim** | annulus centred on the arena, inner r **9.5 m** out to the wall | **30**, Ground | warning 2.0 s (R&S temporary field limit median 1.0 s, raised to the 2 s floor) |
| 2.0 → **7.0** | **Slag Edge** | same annulus as a standing pool (`BurnPerSecond` **10**) | 10/s, never stacks | a temporary edge; always expires |
| 0.5 → **3.5** | **Blast** | circle r **4.5 m** around the boss | **30**, Ground, 1.5 m knockback outward | warning 3.0 s |
| 2.2 → **2.6** | **Inhale** | everyone pulled **1.5 m** toward the boss | none | forced movement only: no damage, stun or interrupt. Telegraphed from t = 1.2 |

- **Safe band:** between 4.8 m from the boss and 9.2 m from the arena centre, at least 4.4 m wide.
- **Overlap:** rim (or edge) + blast = two damaging patterns. Nothing else is cast during it.
- **Easy:** every time above ×1.25.
- **Punish after:** **3.5 s major punish** in phases 1–2 (vents cooling, hammer braced); 2.0 s in phase 3.
- **Slag Rim part:** used alone in phase 2–3 pairings, as the rim plus the edge, without the inhale or blast.

## Escape route (from `escape-medium.md`)

| Case | Distance | 4.2 walk | 4.2 + dash | 6 m/s | Window | Margin |
|---|---|---|---|---|---|---|
| slag rim: from the edge inward | 4.55 m | 1.08 s | 0.57 s | 0.76 s | 2.0 s | 1.43 s |
| blast: from the body outward (edge still burning) | 2.65 m | 0.63 s | 0.18 s | 0.44 s | 3.0 s | 2.82 s |
| after the inhale: walk back out | 1.5 m | 0.36 s | 0.10 s | 0.25 s | 0.9 s | 0.80 s |

- **Melee:** a player hugging the body must walk out of the 4.5 m blast. The inhale then drags them 1.5 m back in 0.9 s before it fires.
- **Ranged:** anyone beyond 9.5 m from the centre is in the slag rim at 2.0 s, and the edge keeps burning to 7.0 s. Ranged must come into the band.
- Standing still at either place is hit.

## Co-op and solo

- No co-op rule: the same for every player.
- **Solo:** identical.
- **N = 8:** the band has about 220 m² of floor, plenty for eight bodies.

## Phase use

- **Phases 1, 2, 3** at full strength.
- **Slag Rim** alone pairs with Mould Press (phase 2) and Gear Chain (phases 2–3).

## Easy / Hard

- **Easy:** ×1.25 on all times.
- **Hard:** no change. Global ×1.3 damage taken.

## R&S source

- §4 block 17 (temporary arena edges, `bp_fieldlimit_rectangle_temporary`).
- §4 block 16 (`bp_pullin_circle`).
- §5 proposal 12 (Escapement Pull: pull, then a fresh read before the second hit).

## Escape-check plan

- **Worst cases:**
  - the boss 3 m off centre with a melee player on the far side of the body;
  - a ranged player at the wall;
  - the pull landing a player at the blast edge with 0.9 s left.
- **Autotest** a still player at the wall (hit by the rim) and a still player hugging the body (hit by the blast).

## Build notes

- **Code:** `Run/Encounters/Crucivar/Abilities/BellowsDraw.cs`.
- **Shared change:** the pull needs a no-damage forced-movement path. Use the existing knockback (`knockDir` / `knockDist`) with no damage, stun or interrupt, in its own small shared commit. **No teleporting.**
- **Slag edge:** use `Hazard.BurnPerSecond` with the Ring shape (the burn code already uses `Contains`).
- **Practice hook:** `-lastbellAbility crucivar.bellows_draw`.
