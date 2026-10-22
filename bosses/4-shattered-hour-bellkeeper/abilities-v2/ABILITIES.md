# The Bellkeeper — ability set v2 (proposal)

**Status: proposal.** Designed overnight on 2026-10-22 by the Shattered Hour (Bellkeeper) session for Chzy18, who asked for a brand-new ability set and playable prototypes by morning. Nothing here is approved, and nothing is "human-approved" until Chzy18 plays it.

**Sources read:** `AGENTS.md`; `gpt/08-design-rules.md` (settled rules win over everything here); `research/rabbit-and-steel-boss-mechanics.md` §3.0, §4, §5, §7; this boss's packet `BOSS.md` (identity, model and phase names kept); `gpt/02-visual-style-guide.md`, `gpt/04-bosses.md`, `gpt/05-arenas.md`; the current encounter (`unity/Assets/LastBell/Run/Encounters/BellkeeperEncounter.cs` on `feature/run-loop`), shared hazards (`Run/Combat/Hazards.cs`), map bounds (`Run/RunMaps.cs`); spec v0.3.1 (`collab/docs/design/bellkeeper.md`, PR #16) and its escape checker.

**Escape check:** `tools/encounter-escape/bellkeeper-v2-escape-check.ps1`, results in `tools/encounter-escape/bellkeeper-v2-results.md`. Every Medium window passes at 4.2 m/s and 6 m/s on the Throne of the Final Toll (circle, 13.0 m) and on the key-art test map (square, 12.5 m) — see §6.

## 1. Identity (kept)

The Bellkeeper, Sovereign of the Shattered Hour: the final boss, a 5 m (sheet: 5.5 m) living cathedral bell with a hooded split bell shell, a pendulum right hand, conducting left fingers, a clapper in the chest crack and three broken clock rings. Model: Bellkeeper 2.0 (`Characters/Bellkeeper2`). Phases (kept): **Last Guardian** (100–70 %), **Broken Hour** (70–35 %), **Final Toll** (35–0 %). The **second form** begins at the Final Toll: the chest opens fully and the halo spreads (the phase-look code already does this) — a harmless 1.5 s transformation, same health bar, no heal, one queued transition per burst.

Arena: **The Throne of the Final Toll** (Shattered Hour map, play bound 13.0 m circle, boss bound 9.5 m). The monumental cracked bell looms behind the north rim; the gold floor fissure is decoration only.

## 2. Design intent: the final exam

The Bellkeeper is the synthesis of the three areas. Each core move is one area's lesson in its own voice, and the phases combine them:

| Lesson from | R&S block (§4) | Bellkeeper move |
|---|---|---|
| Skyward Spire (one-side cleave, rays) | 12 one-side cleave | **01 Leaning Bell** |
| Verdant Chimes (delayed circles, groups) | 7 order numbers | **02 Striking Order** |
| Its own echoes (Unfinished Hour heritage) | 11 player mirror | **03 Reflection of the Hour** |
| Brass Foundry (edges, push and pull) | 16 pull-then-ring, 17 arena edges | **04 The Last Toll Tide** (signature) |
| Hard only | 11 echo of a footprint | **05 The Unfinished Hour** |

The old Bell Sweep / Hollow Toll / Pendulum March / Shattered Chime set is v1. It stays reachable with `-lastbellAbilitySet v1`.

## 3. The ability set (Medium numbers)

| # | ID | Name | Tag | Shape | Warning (lock) | Damage | Melee threat | Ranged threat | Co-op |
|---|---|---|---|---|---|---|---|---|---|
| 01 | `bellkeeper.leaning_bell` | Leaning Bell | Strike | Half of the arena beyond a line through the centre (cone, 90° half-angle, from the centre) | 3.5 s, map-adaptive: max(3.5, need); 4.4 s on the square test map. Side locks at warning | 40 | The boss stands within 3 m of the centre, so the boss's own side is in it half the time | The hit half runs to the wall: the far-edge player has the longest run (13.4 m) | No (side picked by headcount) |
| 02 | `bellkeeper.striking_order` | Striking Order | Ground | Circle r 2.6 m (r 2.2 m at N ≥ 5) on each numbered player, in order | Numerals at 0; ring I at 3.0 s, then every 1.0 s; each ring follows its owner and locks 2.0 s before it rings | 25 per ring | Falls on you at the boss | Falls on you at the edge; casters must time stationary casts | **Yes** |
| 03 | `bellkeeper.reflection` | Reflection of the Hour | Ground | Circle r 3.0 m (r 2.5 m at N ≥ 5) at each player's position mirrored through the boss | 4.0 s; images track until 2.0 s before | 35 | Melee spread round the boss stand on each other's images | Ranged on opposite edges stand on each other's images | **Yes** |
| 04 | `bellkeeper.toll_tide` | The Last Toll Tide | Ground | Tide: ring centred on the arena centre from 7.0 m out past the wall. Then Heart Toll: circle r 4.0 m at the boss | Tide 3.0 s; Heart Toll warns when the tide breaks, 2.0 s. Then the 3.5 s major punish window | 35 + 35 | Heart Toll hits everyone within 4.3 m of the boss | The tide hits everyone 7 m or more from the centre, wall included | No |
| 05 | `bellkeeper.unfinished_hour` | The Unfinished Hour (**Hard only**) | Strike | The previous Leaning Bell's half, replayed in its original world position | Same rule as Leaning Bell, counted from the current Leaning Bell's resolve | 40 | as 01 | as 01 | No |

All four core abilities threaten both a player hugging the boss and a player standing still at 8 m or more and at the wall (rule §5: at least 3 of 4). The two that **punish staying at range** are 04 (the tide comes in from the wall) and 01 (the far edge is the longest run). **No permanently safe spot:** the edge is hit by 01 (its half) and 04 (all of it) every loop, the area behind the boss by 01 and 03, and the boss's feet by 04's Heart Toll and 02.

Damage: 100 HP players. Two big mistakes (35–40 each) plus a ring leave a player near death; three down them. Two overlapping Striking Order rings deal 50. Damage never scales with player count. Boss HP scales with N (spec v0.3.1 D6: `base × (1 + 0.7 × (N − 1))`).

## 4. Player-count rules (N = 1 / 2 / 4 / 8)

| Ability | N = 1 | N = 2 | N = 4 | N = 8 |
|---|---|---|---|---|
| 01 Leaning Bell | Your side | Side with more standing players; tie → the boss's target's side | same | same; 8 bodies cross the line together |
| 02 Striking Order | **Solo: I–IV all on you, 1.0 s apart** — a chasing sequence: keep moving on a curve | I–IV alternate A, B, A, B (two rings each) | I–IV, one each | I–VIII, one each, r 2.2 m. Neighbours in the order must be ≥ 5 m apart |
| 03 Reflection | **Solo: two images — your mirror through the boss and one at your own position** (locks 2.0 s before) | Mirror partners: don't stand on each other's mirror | four images | eight images, r 2.5 m: gather on one side of the boss so every image falls on the other side |
| 04 Toll Tide | Same shapes | same | same | same; the band left (≈ 83 m² round a central boss) holds 8 players at ≈ 10 m² each |
| 05 Unfinished Hour | Same as 01 | same | same | same |

## 5. Phase plan (Medium, N = 4)

Each phase is a loop; pressure only stops in the 3.5 s punish window after each Toll Tide. At most two damaging patterns are ever live.

**Phase 1 — Last Guardian (100–70 %), ≈ 25 s loop: each move alone, to learn it.**
| t (s) | Event |
|---|---|
| 0 | Boss walks to within 3 m of the centre (if needed), **Leaning Bell** warns; resolves 3.5 |
| 4.5 | **Striking Order** numerals; rings I–IV at 7.5 / 8.5 / 9.5 / 10.5 (N = 8: to 14.5) |
| 11.5 | **Reflection of the Hour** warns; locks 13.5; resolves 15.5 |
| 16.5 | **Toll Tide** warns; tide 19.5; **Heart Toll** 21.5 |
| 21.5 | **Punish window 3.5 s** (chest opens) → loop at 25.0 |

**Phase 2 — Broken Hour (70–35 %), ≈ 22 s loop: pairs.**
| t (s) | Event |
|---|---|
| 0 | **Lean and Order:** Leaning Bell warns (resolves 3.5); Striking Order numerals at 1.5, ring I at 4.5 (1.0 s after the cleave), then +1.0. Rule pair: *where* (cross the line) + *apart* (rings follow you across) |
| 8.5 | **Reflection** alone, resolves 12.5 |
| 13.5 | **Toll Tide** (tide 16.5, Heart Toll 18.5) → **punish 3.5 s** → loop at 22.0 |

**Second form at 35 %:** the queued 1.5 s transformation (chest fully open, halo spread, brighter core).

**Phase 3 — Final Toll (35–0 %), ≈ 17 s loop: the exam.**
| t (s) | Event |
|---|---|
| 0 | **Lean and Order** (as phase 2). Hard: **The Unfinished Hour** replays the previous Leaning Bell's half from 3.5 |
| 8.5 | **The Final Toll:** Toll Tide warns (tide 11.5); Reflection warns at 9.5, locks 11.5, resolves 13.5 with the Heart Toll. Rule pair: *where* (the band) + *together on one side* (images) |
| 13.5 | **Punish window 3.5 s** → loop at 17.0 |

Overlap count: Lean and Order = 2; Final Toll = tide + Reflection, then Heart Toll + Reflection = 2; Hard echo starts only after the current Leaning Bell has resolved, so echo + Striking Order = 2.

## 6. Escape check results (Medium, reaction margin 0.6 s)

From `bellkeeper-v2-results.md` (worst case d_max; time needed = reaction + travel):

| Ability | Map | d_max | 4.2 m/s walk / +dash | 6 m/s walk / +dash | Window | Result |
|---|---|---|---|---|---|---|
| Leaning Bell | Throne 13.0 | 13.43 m | 3.80 / **3.28** | 2.84 / 2.54 | 3.5 | pass with one dash at 4.2 (30 stamina) |
| Leaning Bell | Test map 12.5 sq | 18.04 m | 4.90 / **4.38** | 3.61 / 3.31 | 4.4 (adaptive) | pass with one dash at 4.2 |
| Toll Tide | Throne | 6.40 m | **2.12** / 1.61 | 1.67 / 1.37 | 3.0 | pass walking |
| Toll Tide | Test map | 11.03 m | 3.23 / **2.71** | 2.44 / 2.14 | 3.0 | pass with one dash at 4.2 |
| Heart Toll | both | 3.32 m | **1.39** / 0.88 | 1.15 / 0.85 | 2.0 | pass walking |
| Striking Order ring | any | 2.90 (2.50) m | **1.29** / 0.79 | 1.08 / 0.79 | 2.0 from lock | pass walking |
| Striking Order solo chain | any | — | needs ≥ 2.07 m/s steady walking | | 1.0 s step | pass |
| Reflection image | any | 3.30 (2.80) m | **1.39** / 0.87 | 1.15 / 0.85 | 2.0 from lock | pass walking |
| Unfinished Hour (Hard) | as Leaning Bell | | | | as Leaning Bell | pass |

The first run failed Leaning Bell on the square test map (its corners are 18 m from the line); it was fixed by the map-adaptive warning, never by immunity. Leaning Bell is the tightest move: at 4.2 m/s the far-edge player needs a dash, which is the intended ranged punishment.

## 7. Difficulty levels

- **Easy:** every warning ×1.25 (Leaning Bell 4.4 s on the Throne, Toll Tide 3.75 + 2.5, Reflection 5.0 with lock 2.5, Striking Order step 1.4 s with lock 2.5); Striking Order and Reflection radii −0.4 m. Damage ×0.6 (existing).
- **Medium:** this document.
- **Hard:** damage ×1.3 (existing) and one named extra mechanic, **The Unfinished Hour** (05). Warnings are not shortened.

## 8. Hard mechanic — The Unfinished Hour

The Bellkeeper remembers the half each Leaning Bell struck. On Hard, from phase 2 on, when a Leaning Bell resolves, the **previous** Leaning Bell's half replays once at its original world position, with the same warning rule, as a porcelain after-image of the bell. Players who just crossed away from the current cleave usually stand in the remembered half, so the learned answer is "cross, then cross back". Stamina: two worst-case crossings in ≈ 7 s cost two dashes (60) plus a little sprint, inside 100 with regeneration. See `05-unfinished-hour.md`.

## 9. Conflicts with settled rules (kept the rule)

1. **The prompt's 4.2 m/s timing on the test map vs a 3.5 s cleave.** The square test map's corners make Leaning Bell need 4.4 s. Kept the escape rule: the warning is map-adaptive, and Bellkeeper practice should load the Throne map (open question 1).
2. **Knockback into walls.** The tide's shock-front would push players outward into the wall (the shared rule pushes away from the hazard's origin). Kept the settled 1.5 m but pointed **inward** for the tide (a "pull", R&S block 16), so it never pins anyone; this may need a small shared change (`Knockback` < 0 = toward the origin) reviewed by the Abilities session.
3. **"Nothing damaging under 2 s" vs order rings.** Striking Order rings lock 2.0 s before they ring (not 1.8 s), so the final position is always known ≥ 2.0 s ahead.
4. **The run is not the campaign.** The prompt and rules describe the final boss alone in its own arena; the run loop currently fights the Bellkeeper on whichever map is selected. The design is checked on both the Throne and the test map.

## 10. Open questions (decided provisionally so the build could proceed)

1. Should Bellkeeper practice (and the run's final fight) always load the Shattered Hour map? **Provisional: practice loads it** (`BossEntry.map = "shattered-hour"`); the run keeps the selected map, with the adaptive warning.
2. Leaning Bell tie-break when the halves hold equal players: **provisional: the side of the boss's current target**.
3. Reflection in phase 1 can be solved by "everyone on one side of the boss". Kept on purpose as the lesson phase 3 then tests inside the Toll Tide band.
4. Tide knockback inward 1.5 m (conflict 2): is an inward push acceptable as the settled "about 1.5 m knockback"? **Answered by Chzy18 (2026-10-22): yes for now — provisional.**
5. The Throne's cracked bell is scenery; the tide's VFX starts at the rim. Should the great bell visibly ring (art task)?
6. HP for test builds stays 3600 × (1 + 0.7 (N − 1)); real HP waits for measured DPS.
7. Does Easy also shrink rings (−0.4 m), or only lengthen warnings? **Provisional: both.**
