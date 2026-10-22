# Rabbit and Steel: boss mechanics study

**Team-channel summary (paste-ready):** We read the Rabbit and Steel install statically, without launching it. It's a GameMaker YYC build, so its boss scripts are native code. We decompiled all 404 boss scripts with Ghidra and recovered 3,179 attack placements, each with its pattern, timing and size.

- **Telegraphs** (warning to hit) have a median of **3.5 s**. A quarter are under 3.0 s, and only 10 % are under 2 s; those are mostly bullet and laser lines.
- **Typical windows per attack type:**
  - spreads: 4.0 s
  - stacks: 4.4 s
  - cleaves: 4.0 s
  - stretch-tethers: 4.5 s
  - stop/keep-moving checks: 3.0 s
- **Lunar difficulty** sometimes shortens a telegraph by about 25 % (4.0 → 3.0 s, 6.0 → 4.5 s).
- **Area-boss forms:** every area boss's "variant 1" script is its **second form in the same fight**, not a harder mode. At the switch the boss heals, with a value of 0.45–0.70, probably the share of max HP it returns to.
- **Not in the boss scripts:** damage values and hit counts.

- **Co-op toolkit:** the game builds its co-op from a small set of parts:
  - spread circles, cones and lines
  - stack circles and stack lines
  - tethers to stretch, fixed-anchor tethers and boss-to-player tethers
  - colour-match zones
  - "stop moving / keep moving" checks
  - mirrored hazards
  - numbered order marks (First to Eighth) and two- or four-group splits
  - mass teleports into assigned spots
- **Solo:** almost every phase script has a hand-written solo twin (`_s`). None of those twins calls a player-count switch.
- **Clashes with our rules:** R&S leans on things Last Bell has ruled out: iframes on Defensive moves, enrage timers, a boss heal at the second-form transform, revives and shops.
- **For Last Bell:** the report gives 13 **proposals** that adapt the co-op parts without iframes. Spread, soak, tether and pass-the-mark come first. Chzy18 has answered some of the open questions (see §7).
- **From the official modding docs:** R&S supports at most 4 players. Warning timers are usually 3–6 s. Harder difficulties switch whole attacks on, not only bigger numbers.

---

## 1. Status

**Research + proposals.** Nothing here is approved Last Bell design. Every Last Bell idea is a **proposal**.

**What I read:**
- `research/rabbit-and-steel-rea-prompt.md` (the brief)
- `gpt/08-design-rules.md`
- `gpt/04-bosses.md`
- `research/windblown-evidence/`, as the layout example
- From the game: `Data\Titles_Enemy.csv`, `Names_Move.csv`, `Descriptions_Move.csv`, `Names_HBS.csv`, `Descriptions_HBS.csv`, `Strings_Effect.csv`, `Strings_Menu.csv`, `Strings*.csv`, `SheetList.csv` and `Mods\mod_readme.txt`
- The `data.win` chunk table and STRG string table
- The `RabbitSteel.exe` PE sections, `.pdata` and `.text` call sites

**REA commands** (run as `node rea/scripts/rea.mjs … --json`; raw output is in `rabbit-and-steel-evidence/`):

| Command | Result |
|---|---|
| `doctor` | First run: unhealthy (no engine; Windows native controls missing). After setup: `doctor --provider ghidra` is **healthy** (`rea-doctor-ghidra.json`). |
| `function RabbitSteel.exe 0x140cb2a70 --provider ghidra` | **Worked** on the second try. The first try hit REA's default 330 s Ghidra start-up limit. With `REA_GHIDRA_STARTUP_TIMEOUT_MS=14400000` and five slow analysers disabled, it finished in 11.6 min (`rea-function-steeltooth0_pt2.json`). |
| `inspect-desktop-application <install>` | **Worked.** Family `gamemaker`. |
| `inspect-artifact RabbitSteel.exe` | **Worked.** PE, 63.3 MB, sha256 `a251a3db…`. |
| `inspect-pe-resources RabbitSteel.exe` | **Worked.** Icons and version info only. |
| `inspect-artifact <install folder>` | Failed: needs a file, not a directory. |
| `search`, `analyze`, `decompile` | Failed: `provider_unavailable` (needs one of the absent engines). |
| `inspect-binary-layout` | Failed: Linux x64 only. |
| `inspect-with-objdump` | Failed: objdump isn't configured. |

**Setup (2026-10-21, approved by Chzy18):**
- Ghidra 12.1.4 in `C:\Users\BS_Ch\Tools\ghidra`; its SHA-256 matches the GitHub release.
- Temurin JDK 21.0.12 through winget.
- REA 6.7.0's Windows native files, restored into `rea/native/windows/build/` from the matching npm package, with matching hashes.

**Why most scripts went through Ghidra directly:** every REA CLI call re-imports and re-analyses the 63 MB exe, about 12 min per function. So I ran the other 404 boss scripts through **Ghidra headless directly**:
- one analysis, with the same five analysers disabled
- then `DecompileList.java`
- 135 min in total, 404 of 404 decompiled

**My read-only Node scripts** (`rabbit-and-steel-evidence/tools/`):
- parse `data.win`
- name the YYC script functions
- resolve the decompiled constants
- turn each script into a list of attack placements

## 2. Method and evidence

### Build type (confirmed)

- `data.win` is a `FORM` file with 31 chunks:
  `GEN8 OPTN LANG EXTN SOND AGRP SPRT BGND PATH SCPT GLOB SHDR FONT TMLN OBJT FEDS ACRV SEQN TAGS ROOM UILR DAFL EMBI PSEM PSYS TPAG TGIN FEAT STRG TXTR AUDO`
- It has **no CODE, VARI or FUNC chunk**, so this is a **YYC build**. Game logic is native x64 in `RabbitSteel.exe` (`datawin-chunks.json`).
- The exe's `.text` is 51 MB. `.pdata` lists 123,807 functions.

### Where each kind of fact came from

| Fact type | Source | Confidence |
|---|---|---|
| Boss, phase and pattern script **names** | `data.win` STRG, and `gml_Script_*` strings in the exe | **Confirmed**: the names exist |
| Which helpers a boss script **calls** | Direct `call rel32` sites inside each named function (`exe-yyc-script-callgraph.json`) | **Confirmed** for direct calls. Calls made through variables are missed. |
| What a helper **does** | Helper name, plus the warning or status text it pairs with | **Inferred** |
| Warning text players see | `Strings_Effect.csv` | **Confirmed** text. Which pattern shows it is **inferred**. |
| Status effects | `Names_HBS.csv` / `Descriptions_HBS.csv` | **Confirmed** text |
| Difficulty modes | `Strings_Menu.csv` | **Confirmed** text |
| Per-attack pattern, timings and sizes | Ghidra decompile of all 404 boss scripts → `boss-pattern-adds.json` (3,179 placements); summaries in `boss-timing-summary.json` | **Confirmed** for literal values. Values computed at runtime (positions, player groups, random angles) show as `<expr>`. 93 of 2,597 delay values are computed, and 33 more are per-difficulty switches. |
| Damage per hit, hit counts | Not in the boss scripts | **Unknown.** No pattern takes a damage value. Damage looks fixed per pattern type in engine code that wasn't decompiled. The mod docs list no damage parameter either (inferred). |
| Distances in metres | Not recoverable directly | R&S measures in screen units. The play room is 1920 × 1080 (`datawin-rooms.tsv`), and the battle field inside it is set at runtime. Sizes stay in R&S units; see §3.0 for a rough scale. |
| Attack **names** shown to players | Not present | **Unknown**. `Names_Move.csv` holds only player moves (`mv_<class>_N`, with gem variants). It has no enemy move keys. |

### Corrections to the brief's assumptions (confirmed from data)

- **The `level` column in `Titles_Enemy.csv` isn't difficulty.**
  - Level 0 is the full title ("Merran, the Steeltooth Wolf").
  - Level 1 is the short name ("Merran").
- **Difficulty modes are** CUTE, NORMAL, HARD and LUNAR.
  - Hard is described as "enemies must be defeated within the timer".
  - Lunar is described as "you'll need to know the fights to survive".
- **Mods.** `Mods\mod_readme.txt` points to an external Google Drive and a Discord; no pattern data ships in the install. With Chzy18's OK, I read the developer's **Encounter API** sheet in that Drive, read-only. My paraphrased notes are in `rabbit-and-steel-evidence/mod-encounter-api-notes.md`. It confirms (**confirmed: developer docs**):
  - R&S supports **at most 4 players** (player IDs 0–3).
  - Encounters are timelines of `time` and `timeRepeating` blocks. Each attack has a `warningDelay` (telegraph appears) and a `spawnDelay` (hit). Typical presets show the warning at 0 ms and hit at **3000–6000 ms**.
  - `condDifficultyCheck` turns whole time blocks on or off for Cute/Normal, Hard or Lunar. So **harder modes can add or remove attacks**, not only change numbers.
  - `condPlayerCount` does the same per player count (1–4). This is how solo versions differ.
  - Spread circles and cones resize automatically by player count (`circleRadiusDef` / `coneRadiusDef`).
  - Boss commands include `heal`, `transform`, `erasePatterns` (used at phase transitions) and these flags: "Holmgang" (can't drop below 1 HP), "Queen" (killing her KOs the rest) and "Painshare".

### Naming scheme for boss pattern scripts

- `bp_<area>_<boss><variant>` is the opener and phase-1 controller. Example: `bp_wolf_steeltooth0`.
- `…_pt2 … _pt9` are later phase scripts.
- `…_s` is the **solo version** of a phase script. This is a strong inference: none of the 191 `_s` phase scripts calls `bpsw_2p`, `bpsw_plnum` or `bpsw_multiplayer`, while 58 of their 189 multiplayer twins do. Only 24 of 191 `_s` scripts call a player-order helper, against 131 of 189 twins (`exe-solo-vs-multi-phase-scripts.json`).
- `…_h`/`…_sh` and `…_l`/`…_sl` are extra variants with their own solo versions. They might be Hard and Lunar versions (**inferred, unverified**).
- `…_enrage` is that boss's enrage pattern. `…_faceoff` is an intro set piece.
- **Variant digits:**
  - Area bosses come as `0` and `1`.
  - **`…1` is the second form of the same fight (confirmed from decompiled code).** All 10 area and final bosses add their own `bp_<boss>1` script as a pattern from inside `bp_<boss>0`, for example `bp_frog_idol0` and `bp_bird_valedictorian0`.
  - Form 1 calls `scrbp_health_threshold`, which gates its HP phases.
  - Form 2 starts with `transform`, `phase_pattern_remove` and a **boss heal**: Merran 0.55, Ranalie 0.45, Matti 0.65, Avy 0.70, Twili 0.70, Asha 0.60 (`boss-heal-and-thresholds.json`). The value is probably the fraction of max HP the boss returns to (inferred).
  - My first report guessed variant 1 was a harder mode. That was wrong.
- `enc_training_*` holds 61 developer test encounters, one per mechanic: cleave, colormatch, tether, thorns, linestack, movecheck, playermirror and so on. These came straight from the data, so they're the most reliable list of the game's building blocks.
  - Minibosses come as `0`/`1` and sometimes `2`. The first enemy listed for each area has a variant `0` with no hard-timer call (`bpsw_enrage_time`) and one phase script. Examples: `wolf_blackear0`, `dragon_granite0`, `mouse_cadet0`, `mouse_medic0`, `frog_tinkerer0`, `bird_sophomore0`. It might be an intro or story version (**inferred**).
### How the decompile was read (method)

1. **Ghidra pseudo-C** names nothing, so three lookups fill it in:
   - `FUN_…` addresses are renamed from the `.pdata` and name-string scan (`exe-yyc-function-names.json`).
   - `DAT_…` constants are read straight from the exe. They're GameMaker values: real numbers, booleans or strings.
   - Setting-name globals ("spawnDelay", "radius" and so on) are filled at start-up from string literals; 29,435 of them are resolved (`exe-yyc-string-globals.json`).
2. **Pattern IDs** are stored as 100000 + an index into `data.win`'s script table (`datawin-scpt-index.tsv`). For example, 101387 is `bp_prscircle`. This matches the mod docs' parameter lists for every pattern I spot-checked.
3. **Arguments:** `parse.mjs` follows each call's real argument array (YYC passes an array of value pointers on the stack), so every `pattVars` key keeps its own value.
   - `bpsw_d(a, b, c)` returns a per-difficulty value: Cute/Normal, Hard, Lunar.
   - Pattern variables reset at each new time block, as the developer documents.
4. Each placement's **telegraph** is `spawnDelay − warningDelay`. A missing warningDelay counts as 0.

## 3. Boss catalogue

Field key:
- **Group** says whether the boss fights alone or with others.
- **Phases** is the highest `_ptN` script found.
- **Co-op helpers** are confirmed direct calls.
- **Shape and attack** notes are inferred from helper names unless marked.
- **Telegraph windows and sizes are confirmed** from decompiled code (§3.0). Hit counts and damage aren't in the boss scripts (§2).

Every boss in this game is built from shared patterns plus per-boss scripts. Phase scripts add those patterns with `bpatt_add`. The game has no player-facing attack names, so **each boss's attack list is a list of pattern types with their recovered timings** (§3.0). The per-area tables below keep the earlier call-graph notes. Where they say "v1", read it as **form 2 of the same fight** (§2).

### 3.0 Recovered timings (confirmed: decompiled code)

**Pattern types**, all bosses together. Telegraph is warning-to-hit in ms (min / median / max). Sizes are R&S units; the play screen is 1920 × 1080.

| Pattern | Uses | Bosses | Telegraph ms | Size (median, range) |
|---|---|---|---|---|
| Circle spread (`bp_circle_spreads`) | 224 | 67 | 2000 / **4000** / 6000 | radius 300 (140–450). Often auto-sized by player count. |
| Stack circle (`bp_prscircle`) | 200 | 41 | 1000 / **4350** / 7000 | radius 600 (180–1000). Then fires 60 bullets outward. |
| Colour match, stand-together (`bp_colormatch`) | 140 | 35 | 2500 / **4000** / 13500 | radius 250 (170–500) |
| Cardinal cross lasers (`bp_cardinal_r`) | 126 | 13 | 2000 / **4000** / 8000 | width 380 (280–400) |
| Spinning ray (`bp_ray_spinfast`) | 88 | 21 | 2000 / **4000** / 7000 | width 150 |
| Line spreads, horizontal / vertical | 81 / 78 | 40 / 35 | 500–2000 / **3500** / 9000 | width 210–220 |
| Colour match, own symbol (`bp_colormatch2`) | 71 | 12 | 2600 / **4000** / 9500 | radius 250 |
| Half-room cleave (`bp_cleave`) | 63 | 14 | 2200 / **4000** / 5000 | half the room |
| Fixed cleave (`bp_cleave_fixed`) | 29 | 6 | 3000 / **4000** / 7200 | half the room |
| Stretch tether to a point (`bp_thorns_fixed`) | 58 | 7 | 3500 / **4000** / 13000 | required stretch 800 (600–1400) |
| Stretch tether between players (`bp_thorns`) | 40 | 14 | 3000 / **4500** / 7000 | required stretch 600 (400–800) |
| Leash tether (`bp_tether`) | 42 | 15 | 200 / **2000** / 5000 | leash radius 500 (150–750) |
| Stack line (`bp_prsline_h` / `_v`) | 52 / 51 | 21 / 23 | 2500 / **4000** / 8000 | width 450–460 (200–1100) |
| Stack line on a player (`bp_prsline_*_follow`) | 45 | 14 | 1500 / **3000–3500** / 6000 | width 300 |
| Stop / keep moving (`bp_movementcheck`) | 40 | 15 | 500 / **3000** / 6000 | — |
| Knockback circle / line | 38 / 41 | 18 / 16 | 1000–2000 / **2500–3000** / 6000 | radius 250; push 400–600 over 300–500 ms |
| Clock spots (`bp_clockspot`) | 32 | 12 | 3000 / **5500** / 7000 | radius 200, fan 40° (40–70°) |
| Rotating crosses (`bp_cardinal_spinning`) | 45 | 8 | 3500 / **6000** / 6250 | width 240 |
| Fire lines (`bp_fire2_line_repeating`) | 56 | 10 | 500 / **800** / 4000 | bullet lines; the shortest warnings in the game |
| Slicing rays (`bp_ray_multi_slice`) | 20 | 3 | 50 / **2000** / 3000 | width 150 |
| Temporary field limit | 19 | 10 | 200 / **1000** / 2000 | box 650 wide |

**All 1,264 literal telegraphs together:**

| Statistic | Value |
|---|---|
| 10th percentile | 1.7 s |
| 25th percentile | 3.0 s |
| median | **3.5 s** |
| 75th percentile | 4.0 s |
| 90th percentile | 5.0 s |
| share under 2 s | 10 % |
| share under 3 s | 27 % |

The short ones are almost all bullet lines and rays in second forms (Merran's form 2 has a median of **0.85 s**).

**Area and final bosses.** Telegraph is min / median / max in ms. Heal is the value passed at the form switch.

| Boss | Form | Telegraph ms | Most-used patterns | Heal at switch |
|---|---|---|---|---|
| Merran (`wolf_steeltooth`) | 1 / 2 | 500 / 3500 / 7000 · 50 / **850** / 7400 | F1: fixed cleaves, cleaves, knockback circles. F2: fire lines ×22, slicing rays ×18, stack circles | 0.55 |
| Ranalie (`dragon_mythril`) | 1 / 2 | 1000 / 3500 / 7000 · 2500 / 4000 / 6000 | cardinal lasers ×36 / ×32, spreads, colour match, knockback lines, spinning rays | 0.45 |
| Matti (`mouse_paladin`) | 1 / 2 | 2000 / 3500 / 6000 · 1000 / 3500 / 7000 | F1: stretch-to-point ×14, marching bullets ×14, stacks, stack lines. F2: army phases | 0.65; −0.05 per army phase |
| Avy (`frog_idol`) | 1 / 2 | 1500 / 4000 / 5000 · 1500 / 3000 / 5000 | colour match ×17, moving water balls, stacks, move checks; F2 ordered colour match ×11 | 0.70 |
| Twili (`bird_valedictorian`) | 1 / 2 | 1500 / 4000 / 13500 · 2700 / **5000** / 7000 | moons ×23, stacks ×15/×47, timestop ×15 with delayed stacks and spreads | 0.70 |
| Shira (`rabbit_queen`) | 1 / 2 | 2000 / 3500 / 5000 · 2000 / 4000 / 4000 | moons, spreads, own-symbol colour match, cardinal lasers ×11, "steel" | computed |
| Farrah (`depths_hound`) | 1 / 2 | 1000 / 4000 / 6500 · 500 / 3500 / 6000 | following angel circles ×15, pounce ×9/×14, cardinal lasers, pull-in lines | — |
| Mell (`sanct_owl`) | 1 / 2 | 1000 / 3494 / 4000 · 1000 / **2000** / 6000 | colour match, spreads, feather shield; F2 moving bloom waves, pursuit | −0.03 per shield phase |
| Asha (`aurum_blackcat`) | 1 / 2 | 1000 / 3000 / 4000 · 199 / 4000 / 5500 | marching bullets, cleaves ×11/×17, stacks, clock spots ×8 | 0.60 |
| Witch (`heart_witch`) | 1 / 2 | 2000 / 3500 / 6000 · 3500 / 4000 / 6000 | following angel circles, bullet lines, spinning rays; F2 spreads ×10, Magnus | computed |

**Other notable fights** (telegraph median):
- **Tassha:** 3.0 s. Cleaves ×8 and clone wolves that teleport.
- **Meg:** 4.0 s. Stretch-to-point ×21, targeted light, between-player stretch ×10.
- **Saya:** every phase has `_h`/`_sh` versions; teleports plus stacks.
- **Karsi:** 5.0 s. Spinning rays ×10.
- **Lette:** 2.9 s. Spreads and move checks.
- **Mav:** 3.0 s. Moving water balls and tethers.
- **Most minibosses:** 3.0–4.0 s.

The full table is in `boss-timing-summary.json`.

**Phase timelines** (confirmed for Merran; the pattern is inferred for others):
- Phases loop on repeating time blocks. Merran's phase 2 runs `timeRepeatTimes(1000, 8000, 2)` then `(5000, 8000, 2)`, an 8 s loop with two offset blocks. Phase 3 is a 7 s loop.
- Each block adds 2–6 placements with telegraphs of 3.0–4.0 s.
- Downtime isn't a separate command. It's whatever gap the timeline leaves, which I haven't measured per boss.

**Difficulty (confirmed):**
- 33 delays use `bpsw_d(normal, hard, lunar)`. Most common:
  - 4000 / 4000 / 3000 (×8)
  - 6000 / 4500 / 4500 (×7)
  - 5000 / 5000 / 4000 (×4)
- Lunar mostly cuts the window by 25–33 %, but in 5 cases it's longer: 5000 → 6000, presumably because more happens at once.
- Hard's kill timer scales per miniboss through `bpsw_enrage_time(×)`: usually 1.2× or 1×, and 0.25× for Pi. The base length isn't in the boss scripts.
- Once an area boss's `_enrage` script triggers, it repeats unavoidable blasts every **4–8 s** (`boss-enrage-times.json`).

**Rough scale (inferred, use with care):** treat the 1080-unit screen height as Last Bell's 23 m play diameter, so 1 unit ≈ 2.1 cm. Then a median spread radius of 300 ≈ 6.4 m, a stack radius of 600 ≈ 12.8 m and a stretch of 600–800 ≈ 13–17 m. These are clearly too big for Last Bell, because the R&S field is narrower than the screen and the player is a dot. Use the **times**, not the sizes, for Last Bell maths.

Area names come from `Strings_Menu` map strings (**confirmed**) where one exists. Otherwise they're inferred from music file names.

### 3.1 Wolves: Outskirts (inferred from `music_outskirts_*`)

| Key | Name | Role | Phases | Notes (confirmed calls → inferred meaning) |
|---|---|---|---|---|
| `en_wolf_blackear` | Rem, the Sooteared Wolf | miniboss (3 variants) | 1 | v1: `order_twogroups`, `order_chaos`, `2p`, `plnum`, so it splits players into two groups and scales by player count. v0 has no hard timer. |
| `en_wolf_greyeye` | Mink, the Greyeyed Wolf | miniboss | 1 | `conespr_default`: cone spreads. Targets a random player (`target_random_index`). |
| `en_wolf_bluepaw` | Ran, the Bluepawed Wolf | miniboss | 1 | `transform_effect_minor` and `order_save`: a minor form change, and remembers player order between casts. |
| `en_wolf_redclaw` | Xin, the Redclawed Wolf | miniboss | 1 | Same helper set as Bluepaw. A pair design is likely (**inferred**). |
| `en_wolf_snowfur` | Tassha, the Snowfur Wolf | multi-phase (pre-boss?) | 7 | Has `_enrage`. Calls `make_warning_enrage(_burst)`, `trigger_enrage` and `spread_warning_sound`. Spawns adds (`spawn_animation`, `bp_wolf_spawn_*`). |
| `en_wolf_steeltooth` | Merran, the Steeltooth Wolf | **area boss** | v0: 8, v1: 9 | v0 has HP thresholds and `multiplayer`. v1 adds a transform, a **boss heal**, `phase_pattern_remove` and an enrage message. Has a `_faceoff` intro, plus `bp_wolf_disappear` (player `add_hbs`, likely Vanish). Enrage text: "Time seems to slow…" / "You're already gone…". |

Group fights: `bp_wolf_spawn_appear/teleport/disappear` shows summoned wolves that move and teleport (**confirmed** names). Whether minibosses fight as a pack isn't recovered.

### 3.2 Dragons: The King's Arsenal (confirmed)

| Key | Name | Role | Phases | Notes |
|---|---|---|---|---|
| `en_dragon_granite` | Sohko, the Granite Dragon | miniboss (3 variants) | 1 | Cone spreads, random-binary targeting, `bullet_enlarge` (bullets that grow). |
| `en_dragon_silver` | Kuu, the Silver Dragon | miniboss | 1 | `random_set_seed` (a seeded, repeatable random draw) and `bullet_enlarge`. |
| `en_dragon_gold` | Pi, the Golden Dragon | miniboss | 1 | Seeded random, `ray_enlarge_hv` (horizontal/vertical beams that widen). |
| `en_dragon_emerald` | Illie, the Emerald Dragon | miniboss | 1 | Directions from arena centre (`direction_center_x/y`), random-binary targets. |
| `en_dragon_ruby` | Karsi, the Ruby Dragon | multi-phase | 7 | Minor transform. `bp_dragon_ruby0_perm` (a permanent pattern). |
| `en_dragon_mythril` | Ranalie, the Mythril Dragon | **area boss** | v0: 8, v1: 9 | v1 transforms, heals and triggers enrage. Uses `bullet_enlarge_params`. |

### 3.3 Mice: The Churchmouse Streets (confirmed)

| Key | Name | Role | Phases | Notes |
|---|---|---|---|---|
| `en_mouse_cadet` | Pin, the Blackshield Mouse | miniboss (3 variants) | 1 | v2 calls `fire_ray`, `activate_ray`/`deactivate_ray` and `set_spawndelay`: beams that appear first and switch on later. |
| `en_mouse_medic` | Elta, the Woodcane Mouse | miniboss (3) | 1 | v2: `order_twogroups` and `order_bifurcate` (party split). |
| `en_mouse_oakspear` | Orn, the Oakspear Mouse | miniboss | 1 | Centre-relative lanes (`direction_center_y`), widening rays. |
| `en_mouse_axewielder` | Aus, the Axewielder Mouse | miniboss | 1 | Same family as Oakspear. |
| `en_mouse_archer` | Nimi, the Archer Mouse | miniboss | 1 | Same family. Scales by player number (`plnum`). |
| `en_mouse_rosemage` | Meg, the Rosemage Mouse | multi-phase | 7 | `bp_mouse_rosemage0_painsplit` and `scrbp_boss_heal_even`: damage is shared and the boss heals the parts evenly (**inferred**). Uses `order_tethergroups`. |
| `en_mouse_commander` | Varo, the Commander Mouse | multi-phase | 7 | Has `_painsplit`. |
| `en_mouse_paladin` | Matti, the Paladin Mouse | **area boss** | v0: 8, v1: 9 | v1: `holyshield` and army spawns at pt2, 4, 6 and 8 (`scrbp_mouse_paladin1_spawn_pt*`, units `en_mouse_army0–6`). Enrage text: she "is charging an unavoidable attack!! Defeat her army to break her shield". Also `bpatt_positions_target_x/y` (patterns placed at players' positions). |

**Group fight (confirmed by keys):** `en_mouse_army0–6` reuse the seven Streets minibosses as Matti's summoned army.

### 3.4 Frogs: The Emerald Lakeside (confirmed)

| Key | Name | Role | Phases | Notes |
|---|---|---|---|---|
| `en_frog_tinkerer` | Maxi, the Crafter Frog | miniboss (3) | 1 | Random targets, two groups (v1). |
| `en_frog_seamstress` | Mav, the Seamstress Frog | miniboss | 1 | `order_save` (v1), and a `_h` variant (v1). |
| `en_frog_songstress` | Lette, the Songstress Frog | miniboss | 1 | Seeded random. |
| `en_frog_musician` | Jay, the Musician Frog | miniboss | 1 | Seeded random. |
| `en_frog_painter` | Blush, the Painter Frog | multi-phase | 7 | `_l`/`_sl` variant at pt3. Uses `order_chaos`. |
| `en_frog_idol` | Avy, the Starry Frog | **area boss** | v0: 7, v1: 9 | v1: `target_different_index` (a target different from the last one), `make_warning_enrage`, transform, heal. |

### 3.5 Crows: The Scholar's Nest (confirmed)

| Key | Name | Role | Phases | Notes |
|---|---|---|---|---|
| `en_bird_sophomore` | Bella, the Seeker Crow | miniboss (3) | 1 | `teleport_all_set_ext` (teleports every player to set spots). |
| `en_bird_student` | Menna, the Studious Crow | miniboss | 1 | `fire_bullet_circle`, `delete_bullet_by_marker` (bullet rings that can be cleared on cue). v1 has two groups and a `_l` variant. |
| `en_bird_whispering` | Azel, the Whispering Crow | miniboss | 1 | `fire_bullet_line2`, and a `_line` sub-pattern. |
| `en_bird_archon` | Saya, the Archon Crow | multi-phase | 7, each with `_h`/`_sh` | The most teleport-heavy boss: `teleport_all_order_x/y/xy`, `alternating`, `random`. It moves players into **assigned spots by order**, plus `order_fourgroups`. |
| `en_bird_valedictorian` | Twili, the Valedictorian Crow | **area boss** | v0: 7, v1: 9 | v1 adds delayed ordered teleports (`teleport_all_order_delayed_x/y`), `make_number_warning`, transform and heal. |

### 3.6 The Queen (final area of the main game; area name not recovered)

| Key | Name | Role | Notes |
|---|---|---|---|
| `en_queens_staff`, `_knife_l`, `_knife_r`, `_axe`, `_spear`, `_shield`, `_harp` | The Queen's weapons | **group**: seven separate enemies, each with its own pattern (`bp_queens_*0`) | Each has a single script. Spear uses `order_chaos`, Axe uses widening rays, the two Knives use centre-relative lanes. Encounters `enc_queens_*0` exist, so they may be fought one by one or as a set (**unknown**). |
| `en_rabbit_queen` | Rabbit of the Moon, Shira | **final boss** | v0: 7 phases with HP thresholds. v1: 9 phases with `bp_rabbit_queen1_steel(_activate)`, a donut (`fire_donut`), `target_different_index`, transform, heal and enrage. Enrage text: she "is charging an unavoidable attack!! Be prepared to defend yourself!", which points at the iframe Defensive (see §6). |
| `en_rabbit_hero` | Shira, the Hero Rabbit | not a boss pattern | No `bp_rabbit_hero*` script. Probably a story or ally actor (**inferred**). |

### 3.7 Extra/DLC stages (map strings confirmed: Darkhouse Depths, Subterra Sanctum, Atelier Aurum)

| Key | Name | Area | Role | Phases | Notes |
|---|---|---|---|---|---|
| `en_depths_basilisk` | Laiza, the Sly Basilisk | Darkhouse Depths | miniboss | 1 | Synced movement. |
| `en_depths_beast` | Flamebeast Taurus | Depths | miniboss | 1 | `_sl` variant, widening rays. |
| `en_depths_angel` | Arinae, the Dying Ember | Depths | multi-phase | 7 (`_h` at pt2) | `player_set_radius` (changes the player hitbox size), two- and four-group splits, ordered teleports. Has a `_quickstart`. |
| `en_depths_hound` | Farrah, the Witch's Loyal Hound | Depths | **area boss** | v0: 7, v1: 9 | `_pounce`, `_faceoff`, `order_save`/`order_load`, `overwrite_handling`. Enrage text: the edges first deal extra damage, then **instantly KO**, then she pulls players "into the depths". |
| `en_sanct_saph` | Rih, the Sapphire Dragon | Subterra Sanctum | miniboss | 1 | `set_painshare_ratio`. |
| `en_sanct_capricorn` | Seabeast Capricorn | Sanctum | miniboss | 1 | Painshare, two- and four-group splits, `instance_create` (summons). |
| `en_sanct_flower` | Eternal Bloom | Sanctum | multi-phase | 7 | `flowershield` (Thorn Shield status), painshare, every group type. Enrage text: "provokes the fury of the thorns". |
| `en_sanct_owl` | Mell, the Witch's Loyal Messenger | Sanctum | **area boss** | v0: 5, v1: 9 | The most group logic of any boss: `order_twogroups`, `fourgroups`, `tethergroups`, `chaos`, `save`/`load`. Has a `_faceoff`. |
| `en_aurum_whitecat` | Stella, the Stray Cat | Atelier Aurum | miniboss | 1 | Scales with `2p`. |
| `en_aurum_beast` | Darkbeast Scorpio | Aurum | miniboss | 1 | Painshare, `order_chaos`. |
| `en_aurum_ghost` | Spell Manifest | Aurum | multi-phase | 8 | Has `_appear`/`_disappear`/`_dialog`. Calls `boss_heal`. |
| `en_aurum_blackcat` | Asha, the Witch's Loyal Familiar | Aurum | **area boss** | v0: 7, v1: 5 (+ colour sub-patterns `_b/_g/_p/_r/_y`) | v1: `blackcat_marching`, `make_number_warning`, `order_fourgroups`/`tethergroups`/`save`/`load`, ordered delayed teleports. The five colour scripts match the five field-limit colours P/B/R/Y/G (**inferred**). |
| `en_geode_butterfly`, `_moth`, `_firefly` | Plee / Whit / Tap, the Fey | Geode (inferred from `music_geode_*`) | minibosses (3 variants each) | 1 | All use painshare or group splits. Moth uses `order_tethergroups`. `bp_summon_butterfly_0–6` adds Spirit Butterflies (`en_spawn_butterfly0–2`). |
| `en_darkhall_spelllock0–4` | Spell Lock Tetrahedron … Icosahedron | Darkhall | five-lock gauntlet | 1 each | Four-group splits, ordered teleports (`spelllock1`), painshare (`spelllock4`). Shows the "SPELLLOCK" effect text. |
| `en_heart_witch` | The Witch of Sorrowed Heart | final DLC boss | **final boss** | v0: 7, v1: 9 | v1: `bp_heart_witch1_magnus(_activate)` (effect text "MAGNUS ATTACK!!"), donut, enrage burst, transform, heal. v0 uses `tethergroups` and `fourgroups`. |
| `en_toybox_sphere(_sm)` | Treasurespheres | Toybox mode | special | 1 | Not a real boss fight. Encounters are `_attack`, `_move`, `_summon`, `_two`, `_four`. |

### 3.8 Fields shared by every boss

- **Targeting** (confirmed helper names):
  - `ttrg_players_opponent` (everyone)
  - `ttrg_players_opponent_binary` (a set of players held as a bitmask)
  - `scrbp_target_random_index` / `_random_binary` / `_different_index`
  - `ttrg_players_prune_closest` (nearest)
  - `ttrg_players_opponent_focus` (the boss's focus target)
  - `ttrg_players_ally_lowest_hp`

  No helper is named "aggro"; `focus` is the closest match (**inferred**).
- **Pattern structure** (inferred from call shape):
  - Each phase script uses timing helpers: `scrbp_time`, `scrbp_time_repeating`, `scrbp_time_repeat_times`, `scrbp_time_repeat_until`, `scrbp_loop_time`.
  - It adds patterns with `bpatt_add`.
  - It sometimes draws randomly with `math_random_switch` or a seeded random (`scrbp_random_set_seed`).

  So each phase is a **timeline script with a few random switches**, not a free random draw. Phases end with `scrbp_end`. In variant 0, HP gates use `scrbp_health_threshold`. Effect text "PHASE START!" / "PHASE BREAK!" / "ONCE MORE!" is confirmed.
- **Punish windows:** no explicit "recovery" helper exists. Boss downtime would sit in the `scrbp_time` gaps, which aren't recovered.
- **Difficulty scaling:**
  - `bpsw_d(normal, hard, lunar)` returns a per-difficulty value (**confirmed**: decompiled). Lunar mostly shortens telegraphs by 25–33 %; see §3.0.
  - `bpsw_enrage_time(×)` scales the Hard-mode kill timer per miniboss (**confirmed**). Area bosses use dedicated `_enrage` scripts, which loop blasts every 4–8 s.
  - `_h`/`_l` scripts may add whole new patterns on harder modes (inferred).
  - `bpsw_story` exists for Story mode.
- **Telegraph language:**
  - Sprites: `spr_pattern_warning`, `_warning_outline`, `_warning_direction`, `spr_pattern_prswarning`, `spr_proj_ray_warning(2)`, `spr_pattern_clockspot`, `spr_effect_clockface`, `spr_pattern_colormatch_circle`/`_symbols`, `spr_pattern_rope_tether`, `spr_pattern_thorn_tether`, `spr_pattern_heavy`, `spr_pattern_fieldlimit_bound`, `spr_enrage_circle`, `spr_enrage_skull`, `spr_bubble_number`.
  - Warning text: "spread out!!", "get in the circle!!", "stand together!!", "get in the line!!", "match colors!!", "stop moving!!" / "keep moving!!", "stretch the tether!!", "cleaving one side!!", "match number!!".
  - Sound helpers: `scrbp_warning_sound`, `scrbp_spread_warning_sound`.
  - `ENEMY ATTACK COLORS` is an accessibility option with five palettes, one of them "MAGENTA STANDARD" (confirmed), so warning colour is player-selectable.
  - The tutorial says the player hitbox is "JUST THAT DOT!" while the enemy hitbox is a large circle.

## 4. Mechanic building blocks (cross-boss)

Each block is backed by a shared `bp_` pattern, its helper and, usually, a training encounter (all names confirmed). The meaning is inferred from names and warning text.

| # | Block | R&S evidence | What players do |
|---|---|---|---|
| 1 | **Spread** (circle, cone, line) | `bp_circle_spreads`, `bp_cone_spreads`, `bp_line_spreads_h/v`, `bp_delayed_*_spreads`. Calls `make_warning_*_spread`, `bpsw_multiplayer`, `ttrg_players_opponent_binary`. Text: "spread out!!" | Each targeted player carries a shape. Stand apart. |
| 2 | **Soak circle / stack** | `bp_prscircle(_follow)`, `bp_delayed_prscircle`, `scrbp_make_warning_prs*`, `make_number_warning`, `spr_pattern_prswarning`. Text: "get in the circle!!", "stand together!!" | Players stack in one circle, which then fires a bullet ring outward (`doubled` adds a faster second ring). The optional number shows **resolve order**, not a headcount (**confirmed: developer docs**; this corrects my first guess). |
| 3 | **Line stack** | `bp_prsline_h/v(_follow)`, `enc_training_linestack(_follow)`. Text: "get in the line!!" | Stand in one lane together. |
| 4 | **Tether you must stretch** | `bp_thorns`, `bp_thorns_fixed`, `spr_pattern_thorn_tether`. Text: "stretch the tether!!" | Two linked players, or one player linked to a fixed point, move apart. |
| 5 | **Rope tether** | `bp_tether`, `bp_tether_fixed`, `bp_tether_enemy`, `spr_pattern_rope_tether`, status "Tethered". Calls `average_pl_pos` and `scr_player_shift_pos` | Linked players are pulled toward each other or a point (**inferred**). |
| 6 | **Boss-to-player tether / mark** | `bp_target_tether`, `scrbp_make_warning_target_tether`, `add_hbs`, statuses "Targeted" and "In Pursuit!" (`bp_pursuit`) | One player is marked. The mark carries a follow-up. |
| 7 | **Order numbers** | Statuses `hbs_order_0–7` ("First"…"Eighth"), `scrbp_order_*`, `bp_showorder`, `bp_displaynumbers`, `make_number_warning`. Text: "match number!!" | Players resolve their part in numbered turns. |
| 8 | **Groups** | `hbs_group_0–3` ("Group One"…"Four"), `scrbp_order_twogroups`/`fourgroups`/`fillgroups`/`tethergroups`/`bifurcate`, `bp_showgroups` | The party is split into 2 or 4 teams with different jobs. |
| 9 | **Colour match** | `bp_colormatch(2/3)`, `_activate_donut`, `spr_pattern_colormatch_*`. Text: "match colors!!", "follow this color!!" | Stand in, or follow, the zone of your colour. |
| 10 | **Movement check** | `bp_movementcheck`, `make_warning_movecheck`. Text: "stop moving!!" / "keep moving!!" | Obey a stop or move state when it resolves. |
| 11 | **Player mirror** | `bp_playermirror`, plus mirrored spread, colour-match and soak variants | A hazard spawns at each player's mirrored position. |
| 12 | **One-side cleave** | `bp_cleave(_fixed/_enemy)`, `make_warning_cleave_t`, `average_pl_pos`. Text: "cleaving one side!!" | Half the arena is hit, chosen from player positions (**inferred**). Cross the line. |
| 13 | **Clock spots** | `bp_clockspot`, `spr_pattern_clockspot`, `spr_effect_clockface`. Calls `make_warning_cone_spread` | Cones placed on a clock face. Take a distinct "hour". |
| 14 | **Rays and lasers** | `bp_ray_single`/`_spinning`/`_spinfast`/`_multi_*`/`_cascading`, `bp_cardinal_spinning`, `bp_light_cross` | Lines, crosses and rotating beams, often placed first and switched on later. |
| 15 | **Bullets and enlarging projectiles** | `bp_bullet_enlarge`, `bp_marching_bullet`, `bp_bloom_circle`/`_moving_wave`, `bp_dark_targeted`, `bp_water_*`, `bp_fire_*` | Bullet-hell layers that grow or march. |
| 16 | **Knockback and pull** | `bp_knockback_circle`/`_line`, `bp_pullin_circle`/`_line`, `bp_gravity_pull`/`_fall`. Text: "pushed!!", "pulled!!" | Forced movement before a second hazard. |
| 17 | **Arena edges** | `bp_fieldlimit_rectangle(_temporary)`, statuses Fieldlimit-P/B/R/Y/G, `bp_deathwall_rectangle(_temporary)` | The safe floor shrinks for a while, or a coloured box limits each player. |
| 18 | **Player statuses** | Bind, Heavy / Very Heavy, Gravity, Teleport(-L/-S), Timestop, Turbulent Winds (`bp_tailwind`) | Movement is changed or restricted for a time. |
| 19 | **Mass teleport** | `scrbp_teleport_all_*` (ordered, delayed, alternating, random) | Players are moved to assigned spots before a mechanic. |
| 20 | **Summons with painshare** | `scrbp_set_painshare_ratio`. Tutorial: damage to summons "WILL ALSO DAMAGE MAIN ENEMY" | Hitting adds still damages the boss. |

**How R&S stacks two blocks and keeps them readable** (inferred from the script structure):

- **It splits warning from activation.** `_activate` and `bp_delayed_*` versions show a warning first, and the hit resolves later as a separate step (`scrbp_activate_ray`/`deactivate_ray`, `set_spawndelay`). So mechanic A can be read and positioned for before mechanic B appears.
- **Ready-made combined pairs** exist as their own scripts:
  - `bp_playermirror_spread`, `bp_playermirror_prscircle` and `bp_playermirror_colormatch_c/d/h/v`: where to stand (mirror) plus whether to stand apart or together.
  - `bp_colormatch_activate_donut`: colour plus a donut.
  - `bp_clockspot`, which calls the cone-spread warning.

  These combine one rule about **where you stand** with one rule about **who stands together or apart**. They don't stack two independent damage layers.
- **Assigned positions:** `teleport_all_order_*` and `order_*` give every player a pre-assigned number or group. The second mechanic then reads off that assignment, so there's no new information to parse.

## 5. Ideas for Last Bell (proposal)

All of these are **proposals**. Every one must pass `gpt/08-design-rules.md`:
- telegraphs built from the real hit shape, in coral/amber
- an escape route without iframes, checked at 4.2 m/s and 6 m/s walk with finite stamina
- at most two damaging patterns at once
- one hit per hazard instance per player

The numbers are Steady Ticking placeholders.

**Timing rule of thumb** (from §3.0; proposal):
- **Positional co-op mechanics** (spread, stack, cleave, tether) get **3–4.5 s**, because R&S's medians are 3.5–4.5 s.
- Nothing damaging gets **under 2 s** at Steady Ticking. R&S only goes that low for bullet lines, with a dot hitbox.
- **Midnight Toll** may shorten telegraphs by up to 25 %, as Lunar does, but it **adds a named mechanic** rather than only speeding up (§5 rule).

**Solo rule (applies to all):** R&S hand-authors a solo version of nearly every phase. Proposal: every co-op mechanic below has an explicit `players == 1` rule, written next to the attack.

| # | Idea (proposal) | R&S source | Last Bell look | Slot | Tag | Escape without iframes | 1 → 8 players, and solo | Rule touched |
|---|---|---|---|---|---|---|---|---|
| 1 | **Peal Spread**: each targeted player gets a 3 m chime circle that resolves after **3 s** (was 2.5 s; R&S spread median 4.0 s, floor 2.0 s, confirmed). Overlapping circles hit both players. | Spread (block 1) | A small bronze bell outline hovers over each target and fills amber, then rings. | Chimebound Oracle hardest tier, or an Area 1 boss phase 2 | Ground | Walk 3–4 m apart: 0.95 s at 4.2 m/s, leaving about 2 s to read. | Targets: min(N, 4) for N ≤ 4, then everyone. The radius shrinks from 3 m to 2.2 m at 8 players so all fit inside 11.5 m. Solo: one circle, you leave your spot. | None. Check overlap if cast during another pattern. |
| 2 | **Harmony Tower (soak)**: an engraved ring with a numeral, which is how many players must stand in it. It resolves after **4.5 s** (R&S stack median 4.35 s, confirmed). The damage is cut into as many shares as the numeral. Each player inside takes one share, and **missing players' shares fall on whoever is inside** (Chzy18, 2026-10-21). For example, a "2" tower with one player inside means that player takes both shares. There's no follow-up ring. | Stack circle (block 2). R&S's number is resolve order; the headcount numeral is our own twist. R&S tops out at 4 players, so the 5–8 scaling is new. | A brass floor dial with a gold numeral that ticks down. | Area 2 boss, or a Brass Foundry miniboss | Ground | You *enter* it. The worst case is crossing the 23 m arena to reach it: about 11.5 m from the edge to a centre tower takes 2.7 s at 4.2 m/s, so 4.5 s works and 3 s wouldn't. Coming up short is survivable but costly: the players inside take the missing shares. | Numeral = ceil(N/2), so 1, 1, 2, 2, 3, 3, 4, 4 for 1–8 players. Solo: numeral 1, and the one share is a normal hit. | Tune the total so that **one player taking every share** at numeral 4 still survives from full HP. An **empty tower wipes the group** (Chzy18). |
| 3 | **Chord Line (line stack)**: a lane from the boss to one marked player. Damage is split by the number of players standing in the lane. | Line stack (block 3) | A taut bell rope traced in amber across the floor. | Pendulum Sentinel hardest tier, or the Area 1 boss | Strike (Brace reduces it) | Solo: step out of the lane, because the solo version is dodgeable. Co-op: stand in the lane. | Damage split across 1–8. Solo: a normal lane dodge (R&S-style solo branch). | No class required: Brace helps but isn't needed. |
| 4 | **Gear Chain (stretch tether)**: two players linked by a clockwork chain must end ≥ 9 m apart after **4.5 s** (was 4 s; R&S between-player stretch median 4.5 s, to-a-point 4.0 s, confirmed), or both take damage. A dash can break the chain (Chzy18, provisional). | Thorn tether (block 4) and `bp_thorns_fixed` | A segmented brass chain between the two players that tightens and glows coral when too short. | Area 2 boss (Foundry theme) | Ground | Walk apart. From touching, each walks 4.5 m in 1.1 s at 4.2 m/s, so there's lots of slack. The real risk is the room: two chained pairs must not cross. | Pairs: floor(N/2), and an odd player is chained to a fixed **anchor post** (R&S has `tether_fixed`). Solo: chained to the anchor post. | None. Check the room against the 11.5 m bound: a 9 m stretch fits. |
| 5 | **Passing Toll (pass-the-mark)**: one player carries a toll mark with a 6 s fuse. Touching an ally hands it over and resets the fuse to 4 s. When the fuse ends, a 3 m ring goes off on the carrier. | Target tether / Targeted / In Pursuit (block 6). R&S passing isn't confirmed; this is an adaptation. | A small ringing bell icon over the head, with a fuse dial around it. | Area 3 boss (Verdant Chimes) | Ground | The carrier walks away from the group before the fuse ends. | Passing is optional, so solo is simply "move away". At 8 players, cap at 3 hand-offs. | Don't add a new resource. It's a status on a timer. |
| 6 | **Hour Hands (clock spots)**: the boss raises a dial with 12 hour marks, and one **30°** cone fires from the boss at each player's hour after **4.5 s**. Players on the same hour stack cones. R&S clock spots use a 40° fan (40–70°) and a 5.5 s median (confirmed). A 40° fan fits only 9 players around a circle; 30° keeps 12 hours for 8 players. | Clock spots (block 13). R&S literally uses a clock face. | The Dial Warden's own face projects 12 notches on the floor. | **Dial Warden** hardest tier (replaces or joins Retoll) | Strike | Step to an empty hour sideways (≤ 3 m). | One cone per player, with 12 hours available. Solo: a single aimed cone, which is the existing Hand Sweep. | Two-pattern overlap: don't run alongside Hollow Dial. |
| 7 | **Striking Order (numbered resolution)**: players get numerals I–VIII, and a small ring falls on each in order, **1.0 s apart** (was 0.8 s; R&S numbered sequences step 1000 ms, and its ordered colour match steps 1000–3000 ms, confirmed). | Order numbers (block 7) | Roman numerals over each head, in friendly ivory (not coral). | Bellkeeper "Combination" phase | Ground | Move off your spot after your turn. Don't walk into the next numeral's ring. | One numeral per player. Solo: the one player gets I–IV in a row, which becomes a chasing-puddle pattern. | Friendly marker colour must not be coral or amber. |
| 8 | **Two Bells (group split)**: the party splits into two groups shown by friendly ivory or cobalt rims, each with its own safe half. | Groups and colour match (blocks 8–9) | Brass vs cobalt bell icons over players. The safe zones are floor gaps between coral fills. | Area 3 boss | Ground | Walk to your half (≤ 6 m). | Group sizes from fill rules, so odd counts work. Solo: one group, one safe half. | Telegraph colour rule: show group identity on players, not as hazard colour. |
| 9 | **Lopsided Toll (one-side cleave)**: a half-arena sweep toward the side where most players stand. | One-side cleave (block 12) | The bell housing tilts to that side and its rim lights coral. | Bellkeeper, as a Bell Sweep variant | Strike | Cross the midline. That's ≤ 5 m: 1.2 s at 4.2 m/s, inside a **3 s** warning (was 2.5 s; R&S cleave median 4.0 s, minimum 2.2 s, confirmed). | The side is picked by headcount. Solo: it's your side. | None. |
| 10 | **Stillness Toll (movement check)**: after a **3 s** chime (was 2 s; R&S stop/keep-moving median 3.0 s, confirmed), anyone moving at resolve takes damage. A mirrored "keep ticking" version exists for the highest tier. | Movement check (block 10) | A loud pendulum freeze. The UI shows a pendulum stopping or swinging. | Pendulum Sentinel highest tier (one named mechanic) | Ground | Stop, or move. Movement is the answer, not a dodge. | Same for everyone, so solo works. | Readability: needs a strong audio cue too. |
| 11 | **Reflection of the Hour (player mirror)**: a ring spawns at each player's position mirrored through the arena centre. | Player mirror (block 11) | Faint porcelain "after-images" across the floor that fill amber. | Bellkeeper "Memory of Midnight" tier (fits the echo theme) | Ground | Don't stand on the mirror of an ally. Stepping 3 m sideways clears it. | One ring per player. Solo: your own mirror, so avoid the far side. | Counts as one damaging pattern, so it obeys the overlap rule. |
| 12 | **Escapement Pull (pull then ring)**: the boss pulls players 1.5 m toward it after a **2.5 s** warning (R&S knockback median 2.5 s, with a 300–500 ms push, confirmed). A donut then resolves **at least 2 s after the pull ends**, so players get a fresh read before the second hit. | Pull-in plus donut (blocks 16 + `colormatch_activate_donut` pairing) | The arena's floor gears turn inward, then a Hollow Toll donut. | Bellkeeper "Final Toll" phase | Ground | After the pull, step into the donut's inner circle or go out past the ring. | Same for all. | Knockback rule: use the settled 1.5 m pull, and check the wall-pinning note. |
| 13 | **Choir Bells (shared-damage summons)**: the boss rings out 2–3 small bells. Hitting them deals a share of that damage to the boss, and they cast spread rings until destroyed. | Painshare (block 20) | Small hanging bells orbiting the boss. | Chimebound Oracle (it already has five orbiting bells) | Ground (their rings) | Their rings are ordinary spread circles. | Bell count fixed at 3. HP scales with N. Solo works. | "Every enemy is a boss, no trash mobs": these are boss parts, not enemies. **Open question.** |

## 6. Conflicts with Last Bell's settled rules

| R&S mechanic (evidence) | Rule it breaks | Last Bell alternative (proposal) |
|---|---|---|
| **Iframes on Defensive moves.** All 14 class Defensive descriptions mention invulnerability (the 15th row is a `???` placeholder) (`csv-move-defensive-slot3-english.tsv`). Also `scrbp_invincibility`, `scr_player_invuln`, the tutorial line "DEFENSIVE (AVOID DAMAGE)" and the Super status. R&S tunes **around** iframes with the status "No Invulnerability" (`hbs_nodefense`) and `bp_invulncancel`, which switch them off for set attacks. | No iframes anywhere | No iframes to cancel. Every hit needs a real route: walk out, jump a low bar, or cross the midline. Brace gives −60 % on Strike only. |
| **"Unavoidable attack, be prepared to defend yourself"** (Shira's enrage) | Every attack needs an escape route | Not adopted. A big finisher must leave a reachable safe zone. |
| **Enrage timers.** The Hard mode timer (`bpsw_enrage_time`), `_enrage` scripts on every multi-phase boss, `scrbp_trigger_enrage`, `spr_enrage_skull` and "defeat your foe!!" | No enrage timers | Not adopted. The highest tier adds one named mechanic per boss (for example idea 10 or 11), not a kill timer. |
| **Soft enrage on the arena.** Depths enrage edges "deal extra damage" and then "instantly KO". `bp_deathwall_rectangle` and `fieldlimit` | No soft-enrage floor fill. Scenery is never a surprise lethal edge. | Only temporary, telegraphed bounds, and they always expire (R&S's `_temporary` variants are the closest match). |
| **Boss heals.** At the switch to form 2, every area boss heals: Merran 0.55, Ranalie 0.45, Matti 0.65, Avy 0.70, Twili 0.70, Asha 0.60 (confirmed values, probably fractions of max HP). Rosemage uses `boss_heal_even`. The Spell Manifest heals fully (1.0) between its phases. | The boss never heals or gets a new health bar | Keep the transform as a short harmless animation, with no heal. |
| **Revive and KO tracker** (`scr_kotracker_*`, "revive!!", "knocked out…") | Don't invent revive rules | Not adopted. Open question if a co-op idea needs one. |
| **Shop, gold and upgrades** (shopkeeper cat, "purchased!", "not enough gold") | No shops or upgrades for now | Not adopted. The intermission shop is already future work. |
| **Player hitbox is a tiny dot** (tutorial) | Not a rule conflict, but R&S's density comes from a dot hitbox | Last Bell uses body-sized hits, so cut spread counts and bullet density. Don't copy densities. |
| **Teleporting players and changing their hitbox** (`teleport_all_*`, `player_set_radius`) | No settled rule, but it's a new mechanic | Not proposed. Open question. |
| **Movement statuses**: Bind, Heavy, Gravity, Timestop | No settled rule, but they shrink escape routes | Only if the route stays valid at 4.2 m/s. Open question. |

**Requested but not adopted:** the brief asked to note how R&S tunes around iframes. That's recorded above. None of it is suggested for Last Bell.

## 7. Open questions for Chzy18

### Answers so far (2026-10-21)

All answered items below were added to `gpt/08-design-rules.md` on 2026-10-21 at Chzy18's request. Plain answers went in as Settled, and "for now" answers as Provisional.

| # | Question | Chzy18's answer | Status |
|---|---|---|---|
| 1 | Should area bosses get a mid-fight form change (a harmless animation, no heal, same health bar)? This was reworded after the decompile showed R&S's "variant 1" is a second form, not a harder tier. | **Yes, main bosses should** | Provisional yes for area bosses and the final boss. The form change follows rule §5: no heal, no new health bar, a harmless transformation. Minibosses don't get one. |
| 2 | A written solo rule for every co-op attack? | **Yes** | Provisional: every co-op attack spec states its `players == 1` behaviour next to the attack. |
| 3 | Harmony Tower failure branch? | **"That one player would take the damage that a 2nd player would"** | Provisional: missing players' shares fall on those inside. There's no follow-up ring. The empty-tower case is still open (question 14). |
| 4 | Friendly marker colours (ivory, brass, cobalt)? | "Let's see what it looks like", then "I don't see the mockup colors" | Then **"Yea those are fine for now"** | Provisional yes: coral = danger; ivory numerals and brass / cobalt team rims show identity only (`research/marker-colours-mockup.svg`). |
| 5 | Boss-owned summons (Choir Bells)? | **Yes** (after the explanation) | Provisional: small bells that belong to the boss count as boss parts, not trash mobs. Idea 13 can go ahead. |
| 6 | Mass teleport of players? | **No** | Not adopted. Players walk to their spots. |
| 7 | Temporary movement statuses (slow, root), if routes still check at 4.2 m/s? | **Yes, for now** | Provisional yes. |
| 8 | Can a dash break a tether? | **Yes, for now** | Provisional: Clockstep and Harmonic Step can break a Gear Chain. |
| 9 | Can a downed player carry a passed mark? | **No, for now** | Provisional: the mark skips downed players. If the carrier goes down, the mark resolves or moves on. |
| 10 | Install Ghidra so REA can decompile? | **Yes** | Done. All 404 boss scripts decompiled; timings in §3.0. |
| 11 | Read the official mod docs? | **Yes** | Done. See §2 and `mod-encounter-api-notes.md`. |

### What questions 2, 3, 5 and 6 mean

- **Q2, solo rule.** Some co-op attacks only work with friends. For example, a stack splits damage between everyone standing in it. Alone, you'd take all of it. R&S writes a separate solo version of almost every phase (the `_s` scripts, plus the `condPlayerCount` command). The question: should each Last Bell co-op attack say in its spec what happens with one player? For example, "solo: the line becomes a normal lane you dodge." Or should solo players just take the full hit?
- **Q3, Harmony Tower failure.** The tower needs N players standing in it. If too few show up, something must happen. A big unavoidable blast would break "every attack has an escape route". My proposal: the tower recasts 3 s later as a bigger ring you *can* walk out of. The alternative is that a failed tower does nothing at all. Which do you prefer, or a third option?
- **Q5, summons.** Some R&S bosses spawn extra small enemies mid-fight; Matti's mouse army and Twili's butterflies are examples. With "Painshare", hitting those extras also damages the boss. Idea 13 would have the Chimebound Oracle drop 2–3 small bells that cast rings, where hitting them also hurts the Oracle. Our rules say "every enemy is a boss, no trash mobs". Do bells that belong to the boss count as boss parts (allowed), or as extra enemies (not allowed)?
- **Q6, why teleports.** R&S moves all players to set spots right before some mechanics, for example one player per corner before a four-way attack. That guarantees the puzzle starts from a known layout, which makes hard group mechanics fair and learnable. Last Bell doesn't need this: our proposals let players walk to their spots. I listed it only because it's a big R&S tool we're *not* copying. Answering "no" is fine.

### Still open

12. Should the Bellkeeper or area bosses ever get a "can't drop below 1 HP until X" lock (R&S "Holmgang")? It isn't in our rules.
13. R&S tops out at 4 players and Last Bell goes to 8. Should group mechanics cap at 4 groups, as R&S does, with larger parties sharing groups?
14. ~~Harmony Tower: if nobody stands in it, what happens?~~ **Answered: the group wipes** (Chzy18, 2026-10-21). The escape route is "get in the tower", so the rule holds. But it's an instant-fail mechanic, so:
    - Its telegraph must be impossible to miss: a loud chime and a numeral in the middle of the screen.
    - Introduce it gently before a boss relies on it.
    - Solo, it means "you must step in".

---

*Limits honoured:* no assets, text or code copied. Game strings are quoted only as short fragments that identify a mechanic. The game was never launched and its folder wasn't written to. Evidence and scripts are in [`rabbit-and-steel-evidence/`](rabbit-and-steel-evidence/README.md).
