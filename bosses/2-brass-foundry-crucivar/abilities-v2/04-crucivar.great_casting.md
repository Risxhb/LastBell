# 04 · The Great Casting (`crucivar.great_casting`) — proposal, signature

Part of Crucivar's ability set v2 (`ABILITIES.md`, same folder). Build only this ability. **Phase 3 and its visuals revolve around it.**

## Fantasy

Crucivar drives its hammer into the central anvil and raises the LEFT crucible high:
1. **The pour.** A river of molten brass runs down one of the six pour channels in the floor toward the player farthest away.
2. **The march.** The poured channel cools and the gear-tooth rim answers: die-stamp pistons slam down in rows along the six channels, marching from the rim wall in to the anvil.

**Look:**
- **In phase 3**, the six channels glow amber as flush floor decals. They're decorative only; the stamps are the hazards.
- **The pour lane** is coral with a molten core line and a chevron flow toward the target.
- **Each stamp** is a coral circle with a gear-tooth rim and a countdown fill. The rows fill rim-first so the march reads as a wave.

## Shape and numbers (Medium)

**Part 1: Pour (line stack).**
- **Shape:** lane, width **2.6 m**, from the boss (at the anvil) to the arena wall, aimed at the **farthest standing player**. Ground.
- **Timing:** it tracks the target until **lock at 1.5 s**, and **resolves at 3.5 s** (R&S stack-line median 4.0 s, tightened; lock-to-hit 2.0 s).
- **Damage:** **80 total**, split evenly among the players inside (line stack). The target alone takes 80, which is survivable from full HP only. With 2 inside it's 40 each, with 8 inside 10 each.
- **Solo:** the pour is a normal lane you dodge, **40 damage** if you stay in it.

**Part 2: March (marching projectiles).**
- **Spokes:** **6 spokes** from the anvil, rotated **30° off the pour direction**, so the cooled pour channel is always a safe gap.
- **Stamps:** circle r **1.2 m** at r 12.5 / 10.5 / 8.5 / 6.5 / 4.5 m on each spoke.
- **Timing:** every stamp telegraph appears at **t = 3.0**. They resolve rim-first at **t = 5.0 / 5.5 / 6.0 / 6.5 / 7.0**, so the first warning is 2.0 s and the last 4.0 s.
- **Damage:** **18** each, Ground, one hit per stamp instance.
- **Phase variants:**
  - Phase 1 uses the pour only.
  - Phase 2 uses the pour plus 3 spokes (every other one).
  - Phase 3 uses the full six.

**Overlap:** the pour and stamp telegraphs share 3.0–3.5 s, which is two patterns.

**Punish after:** **3.5 s major punish** (the hammer is lodged in the anvil and the crucible is cooling). This is phase 3's main window.

**Easy:** all times ×1.25.

## Escape route (from `escape-medium.md`)

| Case | Distance | 4.2 walk | 4.2 + dash | 6 m/s | Window | Margin |
|---|---|---|---|---|---|---|
| join the pour from anywhere (the boss end never moves) | 12.1 m | 2.88 s | 2.37 s | 2.02 s | 3.5 s | 1.13 s |
| solo: dodge the locked pour | 1.6 m | 0.38 s | 0.11 s | 0.27 s | 2.0 s | 1.89 s |
| leave a stamp without entering the neighbouring spoke (ring r 4.5 m, the tightest gap) | 1.5 m | 0.36 s | 0.10 s | 0.25 s | 2.0 s | 1.90 s |

- **Ranged:** the pour always aims at the farthest player, and the march hits the rim ring first. Standing still at the edge on a spoke is hit.
- **Melee:** the last stamps land 4.5 m from the anvil (2.8 m from the body edge), so a melee player standing on a spoke is hit at 7.0 s. They must stand in the gaps, including the cooled pour channel.

## Co-op and solo

- **Co-op rule:** everyone joins the pour lane to split it, then steps into a spoke gap. The pour channel itself is a gap.
- **Solo rule:** dodge the pour (a normal lane), then the march.
- **N = 1 / 2 / 4 / 8:** the share is 80 / k for k players inside. At 8 players, everyone in the lane takes 10 each and then must spread into six gaps.

## Phase use

| Phase | Version |
|---|---|
| 1 | Pour only |
| 2 | Pour + 3 spokes |
| 3 | The full Great Casting, opening every loop, followed by the major punish |

## Easy / Hard

- **Easy:** ×1.25 on all times.
- **Hard:** no change. Global ×1.3 damage taken.

## R&S source

- §4 block 3 (`bp_prsline_*_follow`, a stack line on a player).
- §4 block 15 (marching bullets: Asha's `blackcat_marching`, Matti's marching bullets).
- §5 proposal 3 (Chord Line).

## Escape-check plan

- **Worst cases:**
  - joining the pour from the far side of the anvil;
  - leaving an inner-ring stamp (r 4.5 m) where the gap to the next spoke is narrowest;
  - a player standing still on a spoke at the edge (first ring) and at 5 m (last ring).

## Build notes

- **Code:** `Run/Encounters/Crucivar/Abilities/GreatCasting.cs`.
- **Shared change:** the line split reuses Mould Press's shared-soak math with the lane shape and no wipe. If Mould Press's shared change isn't merged yet, put the split in a small shared commit compatible with `Hazard.ShareTotal` / `ShareCount`.
- **Floor channels:** the phase 3 channel decals are presentation only.
- **Practice hook:** `-lastbellAbility crucivar.great_casting`.
