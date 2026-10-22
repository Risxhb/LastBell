# 05 — The Unfinished Hour (`bellkeeper.unfinished_hour`) — proposal, **Hard only**

Part of the Bellkeeper ability set v2 (`ABILITIES.md`; read it first). Status: proposal, designed 2026-10-22, not built. The one named extra mechanic on Hard (settled rule: Hard adds a named mechanic, not just faster or harder hits).

## Fantasy
The Bellkeeper cannot let an hour finish. On Hard it **remembers the half of the Throne that each Leaning Bell struck**. When it leans again, a **porcelain after-image of its bell shell** appears where it leaned the previous time, tilts the same way, and swings down a second later than the real one. The after-image is the heritage of the old Unfinished Hour (stored footprints replayed in world space). Area look: echo after-images, a dashed ghost outline that fills coral when it becomes live.

## Shape (shared hazard)
The **previous** Leaning Bell's half: the same `Cone` (origin arena centre, half-angle 90°, radius ≥ map's farthest point), at its original world orientation. **Strike.**
- Phase 1 has no echo (the boss is learning nothing yet). From phase 2: when a Leaning Bell resolves and a previous one is remembered, the echo starts warning.
- Between Leaning Bells, the remembered half may show as a faint dashed, unfilled outline (presentation only, never damaging) — reuse the v1 Unfinished Hour's `_Memory` telegraph mode.
- One memory at a time; cleared on phase change, defeat, practice restart and retry.

## Telegraph
Same map-adaptive rule as Leaning Bell, **counted from the current Leaning Bell's resolve**: Throne 3.5 s, test map 4.4 s. Coral half-disc with the porcelain shell after-image tilting over it.

## Damage
40 at Medium (×1.3 on Hard = 52). Knockback 1.5 m away from the centre, stun 15 ticks.

## Escape route
As Leaning Bell: cross the line, worst 13.43 m on the Throne (walk + one dash at 4.2 m/s: 3.28 s ≤ 3.5 s ✓), 18.04 m on the test map (4.38 s ≤ 4.4 s ✓). The learned sequence is **"cross, then cross back"**: the party usually crosses away from the current cleave into the remembered half.
Stamina: two worst-case crossings within ≈ 7 s cost two dashes (60) + some sprint; regeneration (20/s after 0.75 s) keeps it under 100.

## Melee and ranged threat
As Leaning Bell: melee near the centre must cross; ranged at the edge face the longest run, twice.

## Player count
Not co-op. Same at N = 1/2/4/8.

## Phase use
Hard only, phases 2 and 3, in the **Lean and Order** block. Overlap: the echo starts warning only after the current Leaning Bell has resolved, so echo + Striking Order rings = 2 damaging patterns (never 3).

## Punish window
None.

## Easy / Medium
Not present on Easy or Medium.

## R&S source
Block 11 echoes / player-mirror family, plus the Bellkeeper's own Unfinished Hour heritage (spec v0.3 §3.1); R&S "harder modes switch whole attacks on" (mod docs, `condDifficultyCheck`).

## Escape-check plan
Same as Leaning Bell (done: passes with the adaptive warning) plus a stamina trace for two consecutive worst-case crossings with Striking Order rings following.

## Build notes (for the ability session)
- Code: `Run/Encounters/Bellkeeper/Abilities/UnfinishedHour.cs`. Read the locked half that Leaning Bell exposes; don't edit LeaningBell.cs (ask its session or the Abilities session for a hook if needed).
- Autotest: Hard only (absent on Easy/Medium); the echo replays the *previous* half in world space at the right tick; a still player in that half is hit; crossing back at 4.2 m/s with stamina left escapes; never more than 2 live damaging patterns; memory cleared on phase change, defeat and retry; boss death mid-windup cancels.
- Practice hook: `-lastbellAbility unfinished_hour` (forces Hard for that fire, or documents that it needs Hard).
