# Notes on the official R&S Encounter API (paraphrased)

**Source:** the "RABBIT & STEEL Encounter API" Google Sheet, read on 2026-10-21 in a browser at
`https://docs.google.com/spreadsheets/d/1kGen2w1XKoe3nOSm8_Ijd57-yc5J_naLq4XigJZfv-k/htmlview`.
`Mods\mod_readme.txt` links to its folder: "RABBIT & STEEL Modding API and Examples". The sheet's tabs are Reserved Words, Random Notes, Timing, Commands, Patterns, Copy & Paste These and Pattern Examples. These are my notes in my own words; the sheet isn't copied. Every fact here is **confirmed by the developer's own documentation**, but it describes the modding layer, which may differ in detail from the base-game scripts.

## Player count and targeting

- R&S supports **at most 4 players**. Player IDs run 0–3, and group lists (`orderBin0`–`orderBin3`) are 4-player bitmasks.
- **`orderBin` group helpers** put players into groups:
  - random fill by size, with a variant that skips KO'd players
  - "chaos" (completely random)
  - two even groups (3 players split 2+1)
  - four groups, one player each
- **`trgBinary`** says which players a pattern targets; `all` targets everyone.
- **`targetId`** is a single player index. It's the older API, used by some patterns.
- **`circleRadiusDef(1)` / `coneRadiusDef(1)`** size spread circles and cones **automatically by player count**.

## Timing model

- An encounter is a script of **time blocks**:
  - `time` (once at T ms)
  - `timeRepeating` (start, period)
  - `timeRepeatTimes` (start, period, count)
  - `timeRepeatingMult` (a period with several offsets)
- Each pattern takes:
  - `warningDelay`: when the telegraph appears
  - `spawnDelay`: when it hits
  - `eraseDelay`: when lingering parts go away
- **Typical copy-and-paste values:** the warning appears at 0, and the hit lands **3000–6000 ms** later. Some set pieces use 10000 ms.
- The pattern examples overlap timings (a warning at 2.5 s, a hit at 6 s, repeating every 6 s), so a second pattern is announced before the first resolves.
- **`warnMsg`:** 0 = no timer, 1 = timer bar, 2 = text plus timer bar.

## Conditions (how difficulty and player count change a fight)

- `condPlayerCount`: run a time block only for 1, 2, 3 or 4 players. This is how solo and co-op versions differ.
- `condDifficultyCheck`: run a block only on Cute/Normal, Hard or Lunar. **Difficulty can add or remove whole attacks**, not just change numbers.
- `condHpThreshold`: run a block below an HP fraction. This is how HP phases work.
- `condVarCheck`: run a block on a saved-variable comparison.

## Boss-level commands

- **`heal`** heals the boss by a percentage.
- **`setFlags`** sets these flags:
  - **Holmgang**: the boss can't drop below 1 HP
  - no positional (backstab from any angle)
  - untargetable by auto-aim
  - **Queen**: killing this enemy KOs the rest
  - **Painshare**: this enemy takes damage when the other enemies do
  - hide statuses
- **`setExpMult`** sets the "rating bar" time and reward. The base game uses 1× for ordinary fights, 3× time and 1.2× reward for mid-bosses (Saya, Tassha), and 6× time and 1.5× reward for bosses (Twili, Avy).
- **`erasePatterns`** clears this enemy's other patterns. It's used at phase transitions.
- **`transform`** / **`transformSmall`** swap the animation, hitbox and scale. Base-game phase-2 forms are drawn larger.
- `move`, `facePoint`, `faceRandom`, `faceOrderBin`, `animation` (with a "snap" charge time) and `dialog`.
- `syncRandomSeed` and `setCoinflip` let two enemies make linked choices, such as taking turns.

## Pattern facts the scan couldn't give

- **`bp_cleave`:** half-room cleaves, one per player group, each with its own rotation. Used by Merran and Tassha.
- **`bp_colormatch`:** every player gets a circle, and **all must stack together**.
- **`bp_colormatch2`:** each player gets a symbol and must stand in the matching circle.
- **`bp_colormatch3`:** Avy's colour matches in a set order.
- **`bp_prscircle` / `bp_prsline`:** **stack** circles and lines. "prs" comes from an older game by the same developer.
  - The circle fires a ring of bullets outward.
  - `doubled` adds a faster second ring.
  - `displayNum` shows a big **order number**, not a headcount. **This corrects the report's guess that the number shows how many players a soak needs.**
- **`bp_thorns`:** "stretch the line" between linked players. `bp_thorns_bin` chains groups (P0–P1–P2), and `bp_thorns_fixed` links to a point.
- **`bp_tether` / `_enemy` / `_fixed`:** a tether between players, or to the enemy or a point, with a radius (a leash). Used by the frogs.
- **`bp_movementcheck`:** keep-moving and stop-moving checks (Lette, Jay).
- **`bp_fieldlimit_rectangle`:** traps chosen players in a coloured box for the rest of the fight, or temporarily. Used by Ran and Xin.
- **`bp_invulncancel`:** removes invulnerability from chosen players. Used by Merran phase 2.
- **`bp_timestop`:** Twili's timestop. The `bp_delayed_*` versions queue spreads, stacks and binds to fire during it.
- **`bp_knockback_*`:** push lasting about 300–500 ms.
- **Movement statuses:** `bp_heavy` (Orn), `bp_tailwind` (Mink), `bp_gravity_*` (Menna) and binds (Azel).
- **`bp_enrage`:** "the usual enrage, only needs to be called once".
- **Copy-and-paste presets** use the "No Invulnerability" status for 13 s at the start of some fights.
- **Treasure spheres** heal players 2 on Normal and Hard, 1 on Lunar.
