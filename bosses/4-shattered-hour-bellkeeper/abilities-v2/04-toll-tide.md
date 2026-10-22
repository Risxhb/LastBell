# 04 — The Last Toll Tide (`bellkeeper.toll_tide`) — proposal

Part of the Bellkeeper ability set v2 (`ABILITIES.md`; read it first). Status: proposal, designed 2026-10-22, not built. **The signature move** that the arena and phase-3 visuals revolve around; **punishes staying at range**.

## Fantasy
The Bellkeeper turns toward the **monumental cracked bell behind the north rim** and strikes the air with its pendulum hand. The great bell answers. Its sound arrives from the rim: a **cracked shock-front rolls in from the wall toward the centre**, coral at the rim, with fractured clock-ring arcs riding it. As the tide breaks at 7 m, the Bellkeeper's own chest bell tolls point-blank — **the Heart Toll** — and then the shell falls open: the major punish window. In phase 3 (second form) the halo spreads, the chest is already open, and the core blazes during this move. Area look: toll rings, cracked bell shock-fronts, the cosmic void beyond the rim.

## Shapes (shared hazards)
1. **Tide:** one **Ring**, **Ground**, centred on the **arena centre**, inner radius **7.0 m**, outer radius = map bound + 2 m (covers the wall). It hits everyone 6.7 m or more from the centre (with the 0.3 m player margin).
2. **Heart Toll:** one **Circle**, **Ground**, radius **4.0 m**, centred on the boss's position when it warns.

## Telegraph
- **Tide: 3.0 s** at Medium, fixed at warning. R&S source: knockback / field-limit pattern 2.5–3.0 s median (§3.0); our own shock-front.
- **Heart Toll: warns the moment the tide resolves, 2.0 s** (never under 2 s).
- Then the **major punish window: 3.5 s** (the chest opens; Vanguard's Last Word and casters' stationary casts fit).
- Colour: coral/amber ring with a crisp inner edge at 7 m and a rolling bright front moving inward during the warning (the hit is the whole ring at resolve; the moving front is presentation timed to reach 7 m at resolve). Heart Toll: coral disc with a strong clapper-strike flash.

## Damage
Tide 35, Heart Toll 35 at Medium; stun 15 ticks each.
Knockback: **Tide pushes 1.5 m inward** (toward the centre), never into the wall (settled 1.5 m, direction chosen to avoid wall pins; see conflict 2 in ABILITIES.md). Heart Toll pushes 1.5 m outward from the boss. **Chzy18, 2026-10-22: inward push accepted for now (provisional; answers ABILITIES.md open question 4).**

## Escape route
Be inside 6.7 m of the centre when the tide breaks, then outside 4.3 m of the boss when the Heart Toll rings: everyone ends in the band between.
| | 4.2 m/s walk | +dash | 6 m/s walk |
|---|---|---|---|
| Tide, Throne wall → 6.7 m (6.40 m) | **2.12 s ✓** (3.0) | 1.61 s | 1.67 s |
| Tide, test-map corner (11.03 m) | 3.23 s | **2.71 s ✓** | 2.44 s ✓ |
| Heart Toll, boss body edge → 4.3 m (3.32 m) | **1.39 s ✓** (2.0) | 0.88 s | 1.15 s |
Times include the 0.6 s reaction margin.

## Melee and ranged threat
- **Ranged (the punisher):** the tide covers the whole outer ring and the wall. Nobody can stay at range; ranged players must walk in (up to 6.4 m on the Throne) every loop.
- **Melee:** the Heart Toll hits everyone within 4.3 m of the boss two seconds later; melee must step out to the band and wait out the toll.
- No spot is safe through both: the band moves with the boss.

## Co-op / player count
Not a co-op mechanic; same for N = 1/2/4/8. At N = 8 the band (≈ 83 m² round a centred boss; less with the boss near a wall) holds 8 players at ≈ 10 m² each — tight on purpose in phase 3 with Reflection.

## Phase use
- Phase 1 and 2: alone at the end of each loop, followed by the punish window.
- Phase 3: **The Final Toll** with Reflection (images lock at the tide's break and resolve with the Heart Toll).
- The second form's look (chest open, halo spread) is the phase-3 visual frame for this move.

## Punish window
**3.5 s** after the Heart Toll, every time (the fight's major recovery; settled rule: 3–4 s).

## Easy / Hard
- Easy: tide 3.75 s, Heart Toll 2.5 s.
- Hard: unchanged (the Hard mechanic lives on Leaning Bell).

## R&S source
Block 16 pull-then-ring (`bp_pullin_*`, "pulled!!") and block 17 arena edges (`bp_fieldlimit_*_temporary`); §5 proposal 12 (Escapement Pull) adapted: the tide is the pull, the Heart Toll the ring.

## Escape-check plan
Worst cases: wall → 6.7 m (Throne and test-map corner); body edge → 4.3 m with the boss anywhere up to 9.5 m (the band shrinks against the wall); phase 3 with N = 8 and Reflection images inside the band.

## Build notes (for the ability session)
- Code: `Run/Encounters/Bellkeeper/Abilities/TollTide.cs`. Shared `Ring` + `Circle`. The inward knockback probably needs a small shared change (e.g. `Hazard.Knockback < 0` = toward the origin, or a `PushInward` flag) in its own commit for the Abilities session to review.
- Autotest: tide and Heart Toll spawn/resolve at the right ticks; a still player at 8 m+ and one at the wall are hit by the tide; a still player hugging the boss is hit by the Heart Toll; a player walking from the wall at 4.2 m/s reaches safety (and dashes on the test map); the tide knocks inward; one hit per instance; boss death mid-windup cancels both; the 3.5 s punish window follows; Easy timings.
- Practice hook: `-lastbellAbility toll_tide`.
