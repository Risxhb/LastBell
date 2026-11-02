# Crucivar, the Furnace Heart: ability set v2 (proposal)

**Status: proposal.** Designed, not built. Nothing here is human-approved until Chzy18 plays it.

**Sources read:**
- `AGENTS.md`
- `gpt/08-design-rules.md` (updated 2026-10-22, including the §5 melee-and-ranged rule)
- `research/rabbit-and-steel-boss-mechanics.md` (§3.0, §4, §5, §7)
- `bosses/2-brass-foundry-crucivar/BOSS.md` and its sheet `02-crucivar-v1.png`
- `gpt/02-visual-style-guide.md`, `gpt/04-bosses.md`, `gpt/05-arenas.md`
- The current Crucivar code on `feature/boss-crucivar` (`Run/Encounters/CrucivarEncounter.cs`, `CrucivarRig.cs`, `CrucivarAutoTest.cs`)
- The Bellkeeper's escape checker (`collab` `tools/encounter-escape/escape-check.ps1`)

**What stays from the packet:**
- Identity: the 3.8 m furnace automaton, with the hammer on its RIGHT and the crucible on its LEFT.
- The Meshy model and its rig and clips.
- Phase names: Sealed Furnace, Casting Heat, Open Pressure.
- The vents on all four sides.

**What's replaced:** all five v1 attacks (Diebreaker, Slag Cast, Rail Ram, Pressure Release, Casting Cycle). v1 stays reachable with `-lastbellAbilitySet v1` for comparison.

**Arena:** the Brass Foundry.
- Player bound: 13.7 m radius. Boss positions stay inside 9.5 m.
- Four **die bays** sit at 6 m from the centre on the diagonals (NE, SE, SW, NW). They're flush brass inlays that only light up when a mould rises.
- Six **pour channels** run as spokes from the central anvil to the gear-tooth rim. In phase 3 they glow as floor decals, but they deal no damage by themselves; only telegraphed stamps do.

## The set at a glance

| # | Ability (ID) | R&S blocks | Tag | Co-op? | Melee threat | Ranged threat | Phases |
|---|---|---|---|---|---|---|---|
| 1 | **Mould Press** (`crucivar.mould_press`) | 2 soak towers | Ground | yes (solo: two towers in a row) | towers are 6 m out from the boss's anvil, so melee must leave | towers are 5+ m in from the edge, so ranged must come in | 1, 2, 3 |
| 2 | **Gear Chain** (`crucivar.gear_chain`) | 4 stretch tether (pair + fixed post) | Ground | yes (solo: chained to a rim post) | nearest player is chained to the farthest; partners must end 6–10 m apart | same chain: the far partner must close in; rim posts push edge players inward | 2, 3 |
| 3 | **Bellows Draw** (`crucivar.bellows_draw`) | 17 temporary edge + 16 pull | Ground | no (same rule for everyone) | inhale pull, then a point-blank blast around the boss | **slag rim floods the outer ring, then burns for 5 s: the ranged punisher** | 1, 2, 3 |
| 4 | **The Great Casting** (`crucivar.great_casting`), signature | 3 line stack + 15 marching projectiles | Ground | yes (solo: dodge the pour, then the march) | the march's last stamps land 4.5 m from the anvil | **the pour aims at the farthest player**; stamps start at the rim | 1 (pour only), 2 (pour + 3 spokes), 3 (full) |
| 5 | **Second Press** (`crucivar.second_press`), Hard only | follow-up circle on a soaked tower | Strike | no | soakers must step out of the tower right after it resolves | same | Hard, every Mould Press |

All four core abilities threaten both a player hugging the boss and a player standing still at 8 m or more / at the edge (§5 requires at least 3 of 4). **No single-role ability.**

**Ranged punishers:**
- Bellows Draw's slag rim (edge-anchored, and a temporary edge).
- The Great Casting (aims at the farthest player, and its stamps come in from the rim).
- Gear Chain's rim posts.

**No permanently safe spot:**
- The outer ring (r > 9.5 m) is flooded by every Bellows Draw and stamped first by every march.
- Behind the boss is covered by the march's six spokes and the bays.
- Each loop moves ranged players at least four times: towers, slag rim, pour target, march.

## How "hard at every player count" is met (Medium)

- **Tight, fair windows.** Every worst case leaves a 0.58–1.9 s reaction margin at 4.2 m/s with one dash (`escape-medium.md`). Nothing damaging is under 2.0 s, and the positional co-op mechanics run 3.0–4.2 s.
- **Mistakes cost a lot:**
  - Snapped chain: 28.
  - Slag rim: 30.
  - Blast: 30.
  - Pour taken alone: 80.
  - Stamp: 18.
  - A missed tower share: +20 to each soaker.

  Three ordinary mistakes (28 + 30 + 30) down a full-HP player. A soak taken alone (an 80 pour, or a "2" tower taken alone for 40) is survivable from full HP only.
- **Layering in phases 2–3** always pairs one *where-you-stand* rule (slag rim, march) with one *who-stands-together-or-apart* rule (towers, chains, pour). Never two independent damage layers, and never more than two damaging patterns live.
- **Pressure never stops** except the 3.5 s major punish after Bellows Draw (phases 1–2) and after the Great Casting (phase 3). Short 1.5–2.0 s recoveries elsewhere.
- **Difficulty comes from learned sequences, not stats:**
  - fixed alternating bays,
  - the pour always aims at the farthest player,
  - the march spokes always sit 30° off the pour (so the cooled pour channel is the safe gap),
  - the chain always pairs nearest with farthest.

  No HP bloat, no randomness, no global speed-up.
- **Player count:**
  - Solo gets sequences: two towers in a row, a rim post instead of a partner, dodge-the-pour then the march.
  - 8 players get spatial jobs: four towers with two each, four chain pairs, an eight-way pour split.
  - Damage never scales with N. Boss HP scales with N for fight length (formula pending, Bellkeeper D6). The test build keeps 2500 HP.

## Phase plan (Medium)

Times are seconds from each step's start. Crucivar walks to its central anvil (within 3 m of the centre) before Mould Press, Bellows Draw and the Great Casting, at 1.4 m/s for at most 1.5 s.

**Phase 1: Sealed Furnace (100–65 %).** Each ability is introduced alone. The loop is about 22 s.

| Step | Ability | Resolves at | Then |
|---|---|---|---|
| 1 | Mould Press (towers; solo: I then II) | 4.2 (solo II 6.8) | 2.0 recover |
| 2 | Bellows Draw (rim 2.0, pull 2.2–2.6, blast 3.5; slag edge burns to 7.0) | 3.5 | **3.5 major punish** (vents cooling, hammer braced) |
| 3 | Great Casting, pour only (line stack) | 3.5 | 2.0 recover |

**Transition at 65 %: second form, "Casting Heat".**
- It's queued to the end of the current attack, takes 1.5 s, and does no damage.
- The insulation plates fall off and the furnace glow deepens. Same HP bar, no heal. Crossing two thresholds in one burst queues only one transition.

**Phase 2: Casting Heat (65–30 %).** Pairings start. The loop is about 30 s.

| Step | Ability | Resolves at | Then |
|---|---|---|---|
| 1 | Gear Chain alone (introduced) | 3.0 | 1.5 recover |
| 2 | Mould Press + Slag Rim. The rim resolves at 2.0 and its edge burns to 7.0; the towers sit inside the 9.2 m disc and resolve at 4.2. *(where + together)* | 4.2 | 2.0 recover |
| 3 | Gear Chain + Slag Rim. The chain band must fit inside the disc. *(where + apart)* | 3.0 | 1.5 recover |
| 4 | Bellows Draw (full) | 3.5 | **3.5 major punish** |
| 5 | Great Casting, pour + 3-spoke march (every other spoke) | 7.0 | 2.0 recover |

**Transition at 30 %: "Open Pressure".** The flywheel guard and shoulder fins open, the six pour channels light on the floor. 1.5 s, harmless.

**Phase 3: Open Pressure (30–0 %).** The signature fight. The loop is about 30 s.

| Step | Ability | Resolves at | Then |
|---|---|---|---|
| 1 | **The Great Casting** (pour line stack + full 6-spoke march) | 7.0 | **3.5 major punish** (hammer lodged in the anvil, crucible cooling) |
| 2 | Gear Chain + Slag Rim | 3.0 | 1.5 recover |
| 3 | Mould Press (Hard: + Second Press) | 4.2 (Hard press 6.2) | 2.0 recover |
| 4 | Bellows Draw (full) | 3.5 | 2.0 recover |

**Overlap audit:**
- In every row, at most two damaging patterns are live at once.
- Slag Rim is "Bellows Draw without the blast", so the rim + towers and rim + chain pairings stay at two.
- The Great Casting's pour and stamps overlap only from 3.0 to 3.5 s (two patterns). The pour resolves at 3.5 and the first stamp at 5.0.
- Second Press always comes after its towers have resolved.

## The Hard mechanic

**Second Press** (`crucivar.second_press`), one named extra mechanic:
- 2.0 s after each Mould Press tower resolves, the hammer re-stamps the tower that held the most players (ties: the lowest numeral) with a 2.2 m Strike circle for 25 damage. Its 1.5 m push goes toward the arena centre, so it never shoves anyone into a burning slag edge.
- Soakers must leave the mould right after soaking.
- Hard also keeps the global ×1.3 damage taken. Warnings are **not** shortened.

**Easy:**
- All warnings ×1.25.
- The Gear Chain band widens to 5–11 m.
- No Second Press.

All three levels pass the escape check (`escape-easy.md`, `escape-medium.md`, `escape-hard.md`).

## Escape check

`tools/encounter-escape/crucivar-escape-check.ps1 [-Level Easy|Medium|Hard]`. It uses the same grid method as the Bellkeeper's checker, at the Brass Foundry bound 13.7 m with body 1.7 m and margin 0.3 m. PASS needs a margin of at least 0.5 s at 4.2 m/s with one dash and at 6 m/s walking.

**Medium: 13 of 13 worst cases pass.** Tightest margins (4.2 m/s plus one dash):

| Worst case | Margin |
|---|---|
| Mould Press, a bad split sending a player to the far bay | 0.58 s (walk-only needs 4.14 of 4.2 s, so the dash is the answer) |
| Bellows Draw, walking back out after the pull | 0.80 s |
| Mould Press, solo tower I → II | 1.09 s |
| Great Casting, joining the pour from the far side | 1.13 s |

Tuning history: the first pass used R&S medians (4.5 / 4.0 / 2.5 / 3.5 / 4.0 s) and every margin was 1.4–3.8 s, too loose for "hard". The windows were tightened to 4.2 / 3.0 / 2.0 / 3.0 / 3.5 s and re-checked.

## Shared-system changes the build needs

Each goes in its own small commit for the Abilities session to review. No parallel combat system.

1. **Shared soak** (Mould Press):
   - A hazard flag for "damage = total split into numeral shares among players inside".
   - Missing shares fall on those inside.
   - An empty soak downs every standing player (settled rule).
   - A large ivory numeral on the telegraph.
2. **Line split** (Great Casting): the same split math for a lane, with no wipe (the target is always inside its own lane at lock unless they leave).
3. **Tether check** (Gear Chain): a hazard shape that is "inside" when partner distance is outside a band, resolved once per player through `HazardSystem`. A dash ends the tether (provisional rule).
4. **Forced pull with no damage** (Bellows Draw): use the existing knockback path with zero damage, no stun and no interrupt. No teleport.
5. **Standing pool** (Bellows slag edge): already exists (`Hazard.BurnPerSecond`) as an annulus variant. The ring shape needs `BurnPerSecond` support in `Contains` (it already works per shape).

## Conflicts with settled rules (kept the rule)

- **Empty soak wipes the party** (§5, settled). The prompt's "two or three mistakes down a full-HP player" can't apply to a fully empty tower. That's an instant group fail by rule. Its telegraph is a loud chime plus a large ivory numeral in the mould, and it's introduced alone in phase 1 before any pairing.
- **A dash can break a player-to-player tether** (§5, provisional). This gives every Gear Chain an alternative answer (one dash, 30 stamina), which softens "hard". I kept the rule; see Open questions.
- **Prompt: "tighten toward R&S minimums".** I tightened only where the escape check still leaves at least 0.5 s at 4.2 m/s. R&S's sub-2 s bullet-line timings don't transfer (dot hitbox), so nothing here is under 2.0 s (§5).
- **Knockback near walls.** The blast pushes 1.5 m outward, which could push a player into the burning slag edge. The safe band (4.8–9.2 m) is 4.4 m wide, so a pushed player still has 2.9 m. No pinning, but see Open questions.
- **Arena scenery stays harmless.** The die bays and pour channels are flush inlays and are never a surprise hazard (§7); only telegraphed shapes hurt.

## Open questions for Chzy18

1. **Dash breaks a Gear Chain** (provisional rule): keep it, or make the chain the one tether a dash *can't* break, so the stretch has to be walked?
2. **Extra players in a mould or pour:** I split the total among everyone inside (never more than one share each). Should over-soaking instead cost nothing extra but not reduce shares below the numeral's?
3. **Second form at 65 %.** The packet had insulation shedding at 65 %; I made that the second-form change. Should the form change sit at 50 % instead, with three HP phases inside two forms?
4. **Boss HP vs player count:** waiting on the Bellkeeper D6 formula. The test build stays at 2500.
5. **Pull strength:** the settled knockback is about 1.5 m. A stronger inhale (2.5 m) would make Bellows Draw's melee threat much sharper; it still passes the escape check at 2.5 m (1.4 s to walk back, 0.9 s warning → would need the blast moved to 4.1 s). Want it stronger?
6. **Who gets chained to whom:** "nearest with farthest" is learnable but invisible until cast. Should partners be shown 1 s before the chains drop (ivory numerals), R&S `order_*` style?
7. **8 players and four moulds:** an empty fourth mould wipes everyone. Should the 5–8 player version use three moulds (numerals 3/3/2) to lower the wipe risk, or is the current four-mould version the intended difficulty?
