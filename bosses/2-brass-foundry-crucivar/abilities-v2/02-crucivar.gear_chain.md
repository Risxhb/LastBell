# 02 · Gear Chain (`crucivar.gear_chain`) — proposal

Part of Crucivar's ability set v2 (`ABILITIES.md`, same folder). Build only this ability.

## Fantasy

The foundry gantries drop segmented brass gear-chains. Crucivar's flywheel winds them, and they clamp onto players' wrists in pairs. A player left over is chained to a **gear-tooth post** that rises from the rim wall.

The chain has a fixed working length. Too slack and the gears jam; too taut and the links shear. Either way it snaps and whips both ends.

**Look:**
- A segmented chain drawn between partners (or between a player and its post).
- While the distance is inside the band, the chain is brass with a soft ivory pulse.
- While it's outside the band, it's coral with crisp links and a countdown on the middle link.
- A thin dotted ivory guide ring around each partner marks 6 m and 10 m (friendly guidance, not a hazard).

## Shape and numbers (Medium)

- **Rule at resolve:** partner distance must be **6–10 m**. Outside the band, the chain snaps: **28 damage** to both ends, Ground, 0.25 s stun. No knockback, because it would pin players against the rim wall.
- **Pairing**, taken at cast from players standing at that moment:
  - Sort by distance to the boss. Pair the nearest with the farthest, the second-nearest with the second-farthest, and so on.
  - With an odd count, the middle player is chained to a **rim post** at the rim point nearest them (r 13.7 m). They must end 6–10 m from the post.
- **Player count:**

  | N | Chains |
  |---|---|
  | 1 | the player to a rim post |
  | 2 | 1 pair |
  | 3 | 1 pair + 1 post |
  | 4 | 2 pairs |
  | 8 | 4 pairs |

  A downed player's chain ends with no snap; their partner is re-chained to a post with the remaining time.
- **Telegraph:** **3.0 s** from clamp to resolve (R&S between-player stretch median 4.5 s, to-a-point 4.0 s, tightened to the 3 s floor for positional co-op, §5). Easy: ×1.25 and band 5–11 m.
- **Dash:** a dash breaks the player's chain with no snap (provisional rule §5). It costs 30 stamina and the dash.
- **Punish after:** 1.5 s.

## Escape route (from `escape-medium.md`)

| Case | Distance (each) | 4.2 walk | 4.2 + dash | 6 m/s | Window | Margin |
|---|---|---|---|---|---|---|
| partners on the same spot | 3.0 m | 0.71 s | 0.20 s | 0.50 s | 3.0 s | 2.80 s |
| partners at opposite edges | 8.7 m | 2.07 s | 1.56 s | 1.45 s | 3.0 s | 1.44 s |
| solo on its rim post | 6.0 m | 1.43 s | 0.91 s | 1.00 s | 3.0 s | 2.09 s |
| paired with Slag Rim (inside the 9.2 m disc) | 4.2 m | 1.00 s | 0.49 s | 0.70 s | 3.0 s | 2.51 s |

- **Melee:** the nearest player is chained to the farthest. A melee player hugging the boss with a ranged partner at the edge is about 11+ m apart, so both must move: ranged in, or melee out.
- **Ranged:** same chain. Solo or odd players on a rim post must walk 6 m inward from the edge, and a player at the anvil (13.7 m from the post) must move out toward it.
- Standing still next to the boss or at the edge snaps the chain in the standard layouts.

## Co-op and solo

- **Co-op rule:** partners end 6–10 m apart.
- **Solo rule:** the chain anchors to a rim post at the rim point nearest you. End 6–10 m from it (walk in from the edge, or out from the centre).
- N = 1 / 2 / 4 / 8 follows the table above.

## Phase use

| Phase | How it's used |
|---|---|
| 2 | Introduced alone, then with **Slag Rim** (the rim floods r > 9.5 m and burns 5 s). The 6–10 m band fits the 18.4 m disc. |
| 3 | With Slag Rim. |

It's never paired with Mould Press or the pour: that would be two "who stands where" rules.

## Easy / Hard

- **Easy:** ×1.25 warning, band 5–11 m.
- **Hard:** no change (Hard's named mechanic is Second Press). Global ×1.3 damage taken.

## R&S source

- §4 block 4 (`bp_thorns` between players, `bp_thorns_fixed` to a point).
- §5 proposal 4 (Gear Chain). R&S's `tether_fixed` is the odd/solo anchor.

## Escape-check plan

- **Worst case:** partners at opposite edges (each walks 8.7 m in 3.0 s).
- **Also test with Slag Rim active:** partners on opposite sides of the disc, and a solo player at the edge.

## Build notes

- **Code:** `Run/Encounters/Crucivar/Abilities/GearChain.cs`.
- **Shared change:** resolve through `HazardSystem` with a new tether check, in its own small shared commit. Suggested: `HazardShape.Tether`, with `Contains` = "partner distance outside [min, max]"; one hit per player per instance. Don't add a separate damage path.
- **Drawing:** chain rendering lives in presentation only (a LineRenderer or a mesh strip).
- **Practice hook:** `-lastbellAbility crucivar.gear_chain`.
