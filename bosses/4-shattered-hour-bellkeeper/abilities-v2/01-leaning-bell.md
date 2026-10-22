# 01 — Leaning Bell (`bellkeeper.leaning_bell`) — proposal

Part of the Bellkeeper ability set v2 (`ABILITIES.md` in this folder; read it first). Status: proposal, designed 2026-10-22, not built.

## Fantasy
The Bellkeeper strides back to its throne at the arena centre, plants its feet and **heaves its whole bell shell over to one side**, like a cathedral bell swung to the top of its arc. The hood tilts, the rim glows coral along the whole half of the floor it leans over, harmonic ray lines sweep across that half, and the shell crashes down: a compressed bell-wave rolls across half the Throne to the wall. Area look: cracked bell shock-fronts and fractured clock-ring arcs; coral/amber only on the hit half.

## Shape (shared hazard)
- **Half of the arena** beyond a line through the **arena centre** (not the boss), clipped to the map bound. Build it as one `Cone` hazard with origin at the arena centre, `HalfAngle = 90°`, `Radius` ≥ the map's farthest point (19 m covers the square test map's corners). **Strike** (Brace from the front helps; incoming from the centre).
- Before casting, the boss walks to within **3 m of the centre** (1.75 m/s, cap 2 s; skip if already there).
- **Side:** the half holding more standing players at warning time; tie → the side of the boss's current target; solo → your side. The line and side **lock at warning** and never move.

## Telegraph
- **Medium: 3.5 s**, but **map-adaptive**: `warning = max(3.5 s, 0.6 s + walk-and-dash time for the map's worst distance at 4.2 m/s)`, rounded up to 0.1 s. Throne (circle 13.0): 3.5 s. Test map (square 12.5): 4.4 s. Compute from `RunMaps` bound at cast time (or a table).
- R&S source timing: half-room cleave median 4.0 s, min 2.2 s (§3.0).
- Coral translucent half-disc, crisp edge on the dividing line, harmonic rays animating across; the bell shell visibly tilts to that side during the windup (use `BellSweep`-style clip or the presenter's yaw toward the side).

## Damage
40 at Medium. Knockback 1.5 m away from the centre (stops at the wall, no wall damage), stun 15 ticks.

## Escape route
Cross the dividing line. Worst case: the far wall on the hit side, 13.43 m on the Throne (18.04 m at the test map's corners).
| | 4.2 m/s walk | 4.2 + one dash (3 m / 0.2 s, 30 stamina) | 6 m/s walk |
|---|---|---|---|
| Throne, far edge (13.43 m) | 3.80 s (fails at 3.5) | **3.28 s ✓** | 2.84 s ✓ |
| Test map corner (18.04 m) | 4.90 s | **4.38 s ✓** (4.4 window) | 3.61 s ✓ |
| Melee, boss within 3 m of centre (≤ 4.7 m) | 1.72 s ✓ | | |
Times include the 0.6 s reaction margin. Sprint (25 stamina/s) also works.

## Melee and ranged threat
- **Melee:** the boss stands within 3 m of the centre, so a player hugging it is in the hit half unless they're already on the far side; they cross past the boss (≤ 4.7 m).
- **Ranged (punisher):** the hit half reaches the wall. A ranged player at the edge on the hit side has the longest run in the fight and needs a dash at 4.2 m/s. Standing far away is the most expensive place to be.

## Co-op / player count
Not a co-op mechanic. N = 1: your side. N = 2/4/8: the majority side; at 8 the whole party crosses together (watch the boss body: route round it). Damage never scales with N.

## Phase use
- Phase 1: alone (loop start).
- Phase 2 and 3: **Lean and Order** — paired with Striking Order (numerals at 1.5 s into this warning, ring I 1.0 s after this resolves). Rule pair: where to stand (cross the line) + apart (rings follow you across).
- Hard: each Leaning Bell's half is remembered for The Unfinished Hour (05). Expose the locked half (normal vector + map) for 05 to read.

## Punish window
None of its own: 1.0 s recovery (the boss's arm hangs low after the heave).

## Easy / Hard
- Easy: warning ×1.25 (Throne 4.4 s; test map 5.5 s).
- Hard: unchanged here; 05 replays it.

## R&S source
Block 12 one-side cleave (`bp_cleave`, "cleaving one side!!"); §5 proposal 9 (Lopsided Toll).

## Escape-check plan
Worst case: the far wall on the hit side, every line orientation (15° steps), on the Throne circle and on the test-map square. Done: `tools/encounter-escape/bellkeeper-v2-escape-check.ps1` (all pass with the adaptive warning).

## Build notes (for the ability session)
- Code: `unity/Assets/LastBell/Run/Encounters/Bellkeeper/Abilities/LeaningBell.cs`, one class with `IEnumerable<int> Cast(BellkeeperEncounter boss)` (or the shape the Abilities session sets up), using `BossEncounter.Cone(...)`/`W.Hazards.Spawn`. Don't touch other abilities' files.
- Autotest checks: spawns and resolves at the right ticks; side = majority (scripted players on both halves); a still player at the far edge of the hit half is hit; a still player hugging the boss on the hit side is hit; a player who walks + dashes from the far edge at 4.2 m/s escapes; one hit per instance; boss death mid-windup cancels it; Easy warning is longer; the warning on the test map is 4.4 s.
- Practice hook: `-lastbellAbility leaning_bell` (and/or a dev key) fires it alone.
