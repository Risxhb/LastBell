# 03 — Reflection of the Hour (`bellkeeper.reflection`) — proposal

Part of the Bellkeeper ability set v2 (`ABILITIES.md`; read it first). Status: proposal, designed 2026-10-22, not built. **Co-op mechanic.**

## Fantasy
The Bellkeeper raises its long conducting fingers; the clapper in its chest swings to a stop; the broken halo flares. **A porcelain after-image of every player appears on the far side of the Bellkeeper** — each one exactly mirrored through the boss, as if the hour hand had reflected them across the dial. The after-images crack and fill coral, then shatter with a ringing note. Area look: echo after-images, fractured time, porcelain-ivory silhouettes inside coral circles.

## Shape (shared hazard)
- One **Circle** per player, **Ground**, radius **3.0 m** (N ≤ 4) / **2.5 m** (N ≥ 5), centred at the player's position **point-mirrored through the boss centre** (`2·boss − player`), clamped to inside the map bound.
- Images **track** their player (and the boss) until they **lock 2.0 s before resolving**.
- **Solo adds a second image at your own position** (see solo rule).

## Telegraph
- **Medium: 4.0 s** warning, lock at 2.0 s.
- R&S source timing: player mirror / colour match median 4.0 s (§3.0, block 11).
- Coral fill with a crisp edge and a porcelain silhouette of the mirrored player inside (identity only); a thin line from the player through the boss to their image may help readability (non-damaging, not coral).

## Damage
35 at Medium; stun 15 ticks; knockback 1.5 m away from the image centre.

## Escape route
Leave an image (an ally's, or your own when solo): 3.3 m (2.8 m at N ≥ 5) after lock.
| | 4.2 m/s walk | +dash | 6 m/s walk |
|---|---|---|---|
| Leave an image after lock (3.3 m) | 1.39 s ✓ (2.0 window) | 0.87 s | 1.15 s ✓ |
Times include the 0.6 s reaction margin. The better answer is not to be there at lock: **the whole party gathers on one side of the boss**, so every image falls on the other side.

## Melee and ranged threat
- **Melee:** melee players spread round the boss stand right on each other's images (a player 2 m east of the boss puts an image 2 m west). Melee must gather on one side.
- **Ranged:** ranged players spread round the edge stand on each other's images at the opposite edge. Ranged must rejoin the group's side, even if it means walking in.
- Solo, the own-position image hits a player standing still anywhere, melee or ranged.

## Co-op rule and solo rule
- **Co-op:** never stand where an ally's image will land; gather on one side of the boss before the images lock.
- **Solo (N = 1): two images** — your mirror through the boss and one at your own position; both lock 2.0 s before. Move ≥ 3.3 m and don't run to the far side of the boss.
- N = 2: mirror partners; N = 4: four images; N = 8: eight images at 2.5 m.

## Phase use
- Phase 1: alone (the lesson).
- Phase 2: alone.
- Phase 3: **The Final Toll** with Toll Tide: Reflection warns 1.0 s into the tide, locks when the tide breaks, resolves with the Heart Toll. Everyone must be in the band (inside 6.7 m of the centre, outside 4.3 m of the boss) *and* on one side of the boss.

## Punish window
None of its own; 1.0 s recovery. In phase 3 it's followed by the Toll Tide's 3.5 s window.

## Easy / Hard
- Easy: warning 5.0 s with lock at 2.5 s, radius −0.4 m.
- Hard: unchanged.

## R&S source
Block 11 player mirror (`bp_playermirror`, `bp_playermirror_*`); §5 proposal 11 (Reflection of the Hour). The Bellkeeper's echo heritage (Unfinished Hour).

## Escape-check plan
Worst case: a player standing on an ally's image at lock (3.3 m out); solo with both images near the wall; phase 3 at N = 8 inside the Toll Tide band with the boss at 9.5 m from the centre (images clamped to the bound).

## Build notes (for the ability session)
- Code: `Run/Encounters/Bellkeeper/Abilities/Reflection.cs`. Circles via the shared hazard system with a `Track` that recomputes `2·boss − player` (clamped) until lock.
- Autotest: images spawn at the mirrored points and resolve at 4.0 s; with N = 2 scripted players standing as mirror partners, both are hit; with both on one side, neither is; solo still player is hit by their own image; melee (hugging the boss) and ranged (8 m+, and at the wall) still players are hit by the images of scripted partners opposite; leaving 3.3 m at 4.2 m/s after lock escapes; one hit per instance; boss death mid-windup cancels; Easy radius/warning.
- Practice hook: `-lastbellAbility reflection`.
