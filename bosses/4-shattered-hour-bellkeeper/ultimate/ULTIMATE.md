# The Bellkeeper — Ultimate (proposal)

**Status: proposal**, 2026-10-23, by the Shattered Hour (Bellkeeper) session. Designed, not built, not tested, not human-approved.

**Read:** `ULTIMATE-PROMPT` (this folder's `PROMPT.md`), `gpt/08-design-rules.md` (with the new "Exception, Bellkeeper Ultimate"), `RESEARCH.md` (this folder), `bosses/4-shattered-hour-bellkeeper/BOSS.md` + `04-bellkeeper-v1.png`, `gpt/02-visual-style-guide.md`, `gpt/04-bosses.md`, `gpt/05-arenas.md`, `Reference/LAST-BELL-ARENA-ART-HANDOFF/LAST-BELL-SHATTERED-HOUR-ARENA.md`, the v2 set (`../abilities-v2/ABILITIES.md`), Aurelion v4 (`aurelion-design/.../abilities-v4/ABILITIES-v4.md`).

## 0. Decided (Chzy18, 2026-10-23: "forget those rules")

Any number of overlapping patterns; a hard enrage plus per-phase DPS checks; party wipes on failure, even solo; **one-shots allowed** for mechanic failures. Still holding: every mechanic has a real way to survive, no player teleports, no class required, no boss heal or new bar, mouse aiming, no iframes.

My calls (provisional): a separate **Ultimate** entry (practice list "Bellkeeper — Ultimate" and a run-menu option); Easy/Medium/Hard keep v2. 10–14 min, 5 phases + 2 transitions, second form at 55 %. Practice may start at any phase. Healing: potions only (5 × +40); Mending Chorus helps but is never required.

## 1. The idea: the Bellkeeper refuses the last second

Ultimates are hard because they make you **remember**, **read your own assignment**, and **resolve several rules at once**, for a long time, with no forgiveness. The Bellkeeper's lane is **time**: he writes the fight into a ledger, rings it backwards, stops it, shows you two futures and plays them out of order, and gives every player their own hour. Nothing here is a tower-to-soak, a growing devour, a light-well shield, a Day/Night split, a floor that fills, or a Limit-Cut beam (all Aurelion's), nor soak towers, tethers, edges or passed marks (Crucivar, Sylvara).

**v2 Striking Order is retired** (too close to Aurelion's Ecliptic Order). Its replacement, **Pendulum Clocks**, gives each player an hour *and a direction*, and the job is to strike the boss with your own clock without striking anyone else.

## 2. Damage scale (Medium, 100 HP players)

| Tier | Damage | Used by |
|---|---|---|
| **Lethal** | 999 (one-shot) | Stilled player whose chain reaches them; the Thirteenth toll (enrage); "The hour runs out" (push timer fail) |
| **Heavy** | 60 | Being struck by an ally's clock; Ledger replays; Futures steps; Leaning Bell (Ultimate) |
| **Standard** | 40 | Toll Tide, Heart Toll, Reflection images, Backwards copies |
| **Raidwide** | 12 per toll | Tolling Hour (unavoidable; 3–6 tolls in a row) |
| **Cracked Hour** | 30 to everyone + the failing player deals 0 damage for 20 s | A clock that doesn't strike the boss |

Two heavy mistakes kill. A raidwide chain before a heavy mechanic means you must enter it topped up — potions are the only guaranteed healing, so the fight is also a potion-planning check. Hard ×1.3 and Easy ×0.6 don't apply: the Ultimate entry has one tuning.

## 3. Signature mechanics (8)

Each entry: ID, inspiration (technique, not copy), look, rule, shape/tag, telegraph, damage, N = 1/2/4/8, melee/ranged threat, intended solution, escape.

### 3.1 Pendulum Clocks — `ultimate.pendulum_clocks`
- **Inspiration:** personal timers resolved in turn (TOP Program Loop, Argus sentences); facing rules (Azshara's decree).
- **Look:** an **ivory clock face** over each player's head: a Roman numeral (their hour) and one **brass hand** pointing at one of the four rim ritual stations (N/E/S/W — the arena's own stations). Friendly colours only. At the hour, the clock's hand swings and the player's own **clock strike** fires: a coral cone from the player toward that station. The pendulum in the Bellkeeper's chest ticks once per hour; the boss's halo ring turns one notch.
- **Rule:** at your hour your clock strikes a **cone from you, radius 6 m, half-angle 25°, toward your hand's station** (Strike). The strike must **include the Bellkeeper's body**, or **Cracked Hour**: 30 to everyone, and you deal no damage for 20 s (a soft failure that becomes a push-timer failure, as Dancing Mad's −90 %). **Anyone else** inside your cone takes 60. So: stand on the **far side of the boss from your station**, within 7.4 m of the boss centre, with no ally beyond the boss in that line.
- **Telegraph:** numerals and hands appear ≥ 6 s before hour I. Your cone draws **3.0 s** before your hour, tracks you, and **locks 1.5 s** before it strikes. Hours are **5.0 s** apart in P1, then pairs and quads later.
- **Pairing rule (proof fix):** when a Leaning Bell resolves with an hour, the bell **leans toward that hour's station** (not toward the majority), so the striker's spot lies on the safe half. Strikers stand **3.8–7.4 m** from the boss (closer than 3.8 m can still be inside the hit half).
- **N:** solo **I–IV all on you**, hands N/E/S/W in a seeded order: you circle the boss. N = 2: I, III to one and II, IV to the other. N = 4: one each. N = 8: two per hour, **never the same hand**; at a shared hour the two hands are at right angles, so the two strikers stand on two different sides.
- **Melee / ranged:** the cone only reaches 6 m, so a ranged player must come within ≈ 7.4 m of the boss at their hour (the ranged punisher of every phase); melee must leave the line of every other clock.
- **Escape:** ally's locked cone: leave 3.1 m in 1.5 s (1.34 s at 4.2 m/s) ✓. Reaching your spot: worst 17 m in ≥ 6 s ✓.
- **Look line:** body part = the chest pendulum and halo notch; clip `HourTick` (new, a single sharp pendulum beat).

### 3.2 The Stopped Hour — `ultimate.stopped_hour`
- **Inspiration:** time-stop formations and "only some can act" (TEA Inception; FRU time puzzles).
- **Look:** the Bellkeeper **catches his own clapper** with the conducting hand; the pendulum hangs mid-swing; motes freeze in the air; the four rim stations' chains go taut. Stilled players get a **cracked hourglass** glyph (ivory) over their heads 5 s before. At resume the chains **lash** from the stations.
- **Rule:** half the party (seeded; solo: you) is **Stilled** for **4.0 s** (can't move or act). At resume, each Stilled player is lashed by a **chain lane** (width 1.6 m, Strike) from **the station their clock hand points to** through their position. The lane **stops at the first body**: an ally (takes 60; Brace helps) or **the Bellkeeper's body** (harmless). If it reaches the Stilled player: **999**. So: before you freeze, put the **boss between you and your station** (within ±1.1 m of the line through the boss), the same place your clock wants you.
- **Boss position (proof fix):** before every Stopped Hour the Bellkeeper walks back to within 3 m of the centre, so every hiding spot behind him is inside the arena.
- **Free players:** they act during the stop (free damage) and are the safety net: stand in a lane to save a Stilled ally who got it wrong (take 60).
- **Telegraph:** the hourglass glyph **5.0 s** before the stop; the chain lanes are drawn (dashed ivory, then coral) for the whole 4 s stop.
- **N:** solo: you're Stilled every time (no safety net). N = 2: one Stilled per stop, alternating. N = 4: two. N = 8: four, and the four hiding spots behind the boss crowd together — call-outs decide who stands closer.
- **Melee / ranged:** a Stilled ranged player at the edge must walk to the boss's far side; melee must find the narrow strip behind the boss.
- **Escape:** worst 17 m in 5.0 s: 4.65 s walking at 4.2 m/s ✓ (dash 4.13 s).
- **Look line:** clips `CatchClapper` (new: left hand closes on the clapper, freeze pose held 4 s) and `ReleaseClapper`.

### 3.3 The Ledger — `ultimate.ledger`
- **Inspiration:** "your past comes back" (FRU Ultimate Relativity); gate puzzles (Dancing Mad P1).
- **Look:** in P1 every recorded strike leaves an **ivory dashed footprint with a numeral (I–VI)** and a clock glyph, for 2 s, then it fades. The left hand moves as if writing on the air. In P4 the halo spins backwards, the ledger pages turn, and the footprints return **coral, in reverse order, twice as fast**.
- **Rule:** six P1 strikes are recorded exactly as they resolved (Leaning Bell halves, Toll Tide ring, Heart Toll circle, Reflection images). In P4 they replay **VI → I, 1.5 s apart**, each coral for **1.5 s** before it strikes, with no dashed preview. 60 each.
- **Gate:** what P1 records is what P4 replays. A party that makes both P1 Leaning Bells strike the *same* half, and keeps the Reflection images clumped, gets an easier P4. A messy P1 records a nasty P4.
- **N:** identical; images scale with N.
- **Intended solution:** pre-position from memory. For two Leaning halves 1.5 s apart, stand 1 m on the safe side of the line for the first, then step 2 m across for the second.
- **Escape:** each step: 1.5 s window, ≤ 2.5 m between consecutive safe spots when pre-positioned (walk) ✓; without memory, the long moves fail by design (that is the Ultimate check).
- **Look line:** clips `WriteLedger` (new: conducting hand traces a numeral), `TurnLedger` (halo reverse spin).

### 3.4 Backwards Bell — `ultimate.backwards_bell`
- **Inspiration:** rewound resolutions; escalating trios (UCoB).
- **Look:** after a block of strikes, the halo rings **run backwards**, the shell's crack glows in reverse, and each footprint returns with its effects **playing in reverse** (shock rings contracting instead of expanding).
- **Rule:** in P2, strikes come in blocks of three (A, B, C, 1.5 s apart). Then the rewound copies resolve **C, B, A**, 1.0 s apart, starting 2.0 s after C. Each copy is drawn coral 1.5 s before, with an ivory "rewind" arrow. 40 each.
- **N:** identical.
- **Intended solution:** after C, go to where A was safe-but-C-is-not … the rewind order means the spot you just left is the next one struck.
- **Escape:** 1.0 s spacing with 1.5 s warnings: ≤ 2.5 m per step ✓ (authored blocks only).
- **Look line:** clip `ReverseToll` (new: shell swings backwards, halo reverses).

### 3.5 Two Futures — `ultimate.two_futures`
- **Inspiration:** a future shown first and resolved later (TEA Fate Calibration, FRU Crystallize Time).
- **Look:** the conducting fingers **trace two futures on the dial**: Future I in porcelain ivory, Future II in brass, three dashed footprints each (0.8 s per step), then they fade. The halo stops on **I or II**: that future plays first.
- **Rule:** the six steps resolve **interleaved**: first future step 1, second future step 1, first step 2 … 1.2 s apart. Each step goes coral **1.2 s** before it strikes; no dashed preview. 60 each. The **last step resolves on the same tick as a Heart Toll** (simultaneous).
- **Shapes:** from a fixed authored set: 60° cones from the boss, 3 m circles at the hour marks, station lanes. The step sets are authored so consecutive safe spots are ≤ 2.5 m apart.
- **N:** identical.
- **Intended solution:** memorise both futures as three "safe hour marks" each; on the halo's verdict, walk the interleaved path.
- **Look line:** clips `TraceFuture` (new), `HaloVerdict` (new: halo snaps to I or II).

### 3.6 The Conductor's Hand — `ultimate.conductors_hand`
- **Inspiration:** announced combos you decode (UCoB quotes, Azshara's Decree, "Kefka Says").
- **Look:** the left hand rises and **shows 1–4 fingers**; the right pendulum hand swings **left or right**. The two together are a "phrase".
- **Rule:** fingers name the first move (1 Leaning Bell, 2 Toll Tide, 3 Reflection, 4 Stopped Hour); the pendulum swing names the second (left = Heart Toll, right = Backwards copy of the first). The phrase is shown **5 s** before; the real telegraphs appear only **2.0 s** before (shorter than the moves need on their own), so reading the phrase is how you get there in time. A real route always exists for the reader.
- **N:** identical.
- **Look line:** clip `ConductPhrase` (new: finger count readable at the game camera, oversized fingers lit gold).

### 3.7 Tolling Hour and The Thirteenth Hour — `ultimate.tolling_hour`, `ultimate.thirteenth_hour`
- **Inspiration:** heavy raidwides and hidden last phases (Dancing Mad, Golden Bahamut, L'ura's secret phase).
- **Look:** the **monumental cracked bell behind the north rim** tolls; each toll is a full-arena shock (decorative) and a fixed raidwide. In P5 its count appears as numerals on the void sky.
- **Rule:** Tolling Hour = 3–6 tolls, 1.0 s apart, 12 each, unavoidable. **The Thirteenth Hour (P5):** the great bell counts I…XIII, one toll every 6 s; each toll is a 6-damage raidwide; **XIII is the hard enrage** (999 to everyone). 78 s from the start of P5.
- **Look line:** arena event `GreatBellToll` (VFX/audio on `LB_ENV_FinalToll_BrokenBell`); boss clip `CallTheBell` (new: faces north, pendulum hand raised).

### 3.8 Fracture of Time — `ultimate.fracture`
- **Inspiration:** the arena handoff's gold fissure "unless explicitly activated" (our own).
- **Look:** in P5 the **gold fissure** cracks open (1.0 s, outline only), then glows amber along its length (a live hazard); on each toll of the great bell it **flares** coral. Stand-in: an amber lane telegraph over the decorative fissure and a coral flare lane; no new boss clip.
- **Rule:** the fissure (a 1.4 m-wide lane across the whole floor through the centre at its art angle, NW ↔ SE) **burns 30/s** for all of P5 (Ground, persistent). Every P5 Leaning Bell uses the fissure as its line, so reaching the safe half means **crossing the burning fissure**: dash across (≈ 0.13 s inside, 4 damage) or walk (≈ 0.48 s at 4.2 m/s, 14 damage). Not a party split: everyone crosses to the same side.
- **Hardening (Fracture session, 2026-10-23, proposal): the fissure flares on every toll of the great bell**, the Thirteenth Hour's beat: 6, 12 … 72 s after P5 start (none at XIII). Each flare is a **2.4 m lane along the whole fissure, 60 (heavy), Ground, coral for 1.2 s**. On the P5 timeline as built (35 s loop):
  - Each Leaning Bell across the fissure has a toll inside its warning (loop 1: 2.0 s into its 3.5 s; loop 2: 3.0 s in). So you **cross before the toll**. In loop 1 you can also cross in the 1.5 s right after it; in loop 2 the 0.5 s after it needs a dash.
  - Each Stopped Hour stop holds a toll (24 s, 60 s). A **Stilled player whose station is NW or SE** (the fissure's own ends, so their hiding strip *is* the fissure) must hide **beside** it: 6.6–7.5 m behind the boss and 1.65–1.9 m off the fissure's axis (the Stopped Hour session's hardened chain, which the boss's 1.4 m body still stops there). On the axis the burn (about 1 s before the toll) and the flare cost 93 of 100 HP; the flare's knockback then throws you off the crack.
- **N:** identical for 1/2/4/8. **Melee / ranged:** the lane runs from the boss's body to both walls, so still melee and still ranged on it both burn and both take the flare.
- **Escape:** a dash costs 30 stamina per crossing. Walking also works from ≤ 7 m out, or from the far wall in loop 1; from the far wall in loop 2 you need the dash. Tested with the strat bot at 4.2 m/s (and 6 m/s), N = 1/2/4/8 (`RunAutoTest.Bellkeeper.Ult.Fracture.cs`).

## 4. Reworked v2 vocabulary (Ultimate tuning)

| Move | Ultimate tuning |
|---|---|
| Leaning Bell | 50 damage; warning 3.5 s (map-adaptive) alone, 2.0 s when announced by the Conductor's Hand |
| Toll Tide + Heart Toll | Tide 2.5 s, Heart Toll 2.0 s; Heart Toll is the "same tick" partner of Futures and resumes |
| Reflection of the Hour | Images now resolve on the tick time resumes after a Stopped Hour |
| Unfinished Hour | Becomes the Ledger (P1 → P4) |
| Striking Order | **Retired** (Pendulum Clocks) |

## 5. Phases (Medium; times from phase start; HP as % of the single health bar)

| Phase | HP | Push (wipe if not reached) | Signature | Look |
|---|---|---|---|---|
| **P1 Last Guardian** | 100 → 85 % | 2:10 | Pendulum Clocks (one hour at a time); the Ledger records | closed shell, compact rings |
| **T1 The Hour Stops** | at 85 % | — (25 s) | Stopped Hour introduced alone, twice | the pendulum freezes |
| **P2 Broken Hour** | 85 → 55 % | 2:30 | Backwards Bell; clocks in pairs; the Conductor's Hand | parted shell, offset rings |
| **Second form** at 55 % | | | harmless 2 s transformation | chest open, halo spread |
| **P3 The Open Hour** | 55 → 35 % | 2:10 | Two Futures; Stopped Hour + Reflection resolve together | open chest, bright core |
| **T2 Thirteen Chimes** | at 35 % | — (30 s) | Tolling Hour ×6 (72) — enter full; one "final exam" quote of each area, reworked (§5.6) | the great bell wakes |
| **P4 The Unfinished Hour** | 35 → 15 % | 2:10 | **Replay**: the Ledger in reverse at double speed, with clocks and Stopped Hour on top | rings run backwards |
| **P5 Final Toll** | 15 → 0 % | hard enrage 1:18 | Everything; the Fracture; the Thirteenth Hour | the great bell counts |

Target length at measured perfect-party DPS: ≈ 11–12 min. Every phase ends a block with a **3.5 s punish window** (chest open, the clapper exposed).

### 5.1 P1 Last Guardian (one loop ≈ 35 s; repeats with seeded variation until 85 %)
| t (s) | Event | Live patterns | Who acts |
|---|---|---|---|
| 0 | Tolling Hour ×2 (24) | 0 | all (potion decision) |
| 3 | Clock numerals + hands assigned (I at 12, II 17, III 22, IV 27) | — | each reads own |
| 5 | Leaning Bell warns → 8.5 (**Ledger I**) | 1 | all cross |
| 9 | Toll Tide warns → 11.5; Heart Toll → 13.5 (**Ledger II, III**) | 2 | all into band |
| 9 | Clock I cone draws → locks 10.5 → strikes 12 | 3 | I strikes from the band |
| 14 | Clock II draws → strikes 17 | 1–2 | II |
| 15 | Reflection warns → 19 (**Ledger IV**) | 2 | all on one side |
| 19 | Clock III draws → strikes 22 **with Leaning Bell (Ledger V)** warning 18.5 → 22 | 3 | III + all cross: **simultaneous** |
| 24 | Clock IV draws → strikes 27; Heart Toll alone warns 25 → 27 (**Ledger VI**): **simultaneous** | 2 | IV |
| 27 | **Punish 3.5 s** | 0 | damage |
| 31 | next loop (clocks re-assigned; Ledger records only the first loop) | | |

### 5.2 T1 The Hour Stops (25 s)
Stopped Hour alone ×2 (12 s each): glyph at 0, stop 5–9, lash at 9; then a 3.5 s punish.

### 5.3 P2 Broken Hour (loop ≈ 30 s)
| t | Event | Live |
|---|---|---|
| 0 | Conductor's Hand phrase | — |
| 5 | Phrase move 1 (2.0 s telegraph) and move 2 | 2 |
| 6 | Clocks: **two hours at once** (pairs): I+II at 12, III+IV at 17 (solo: I, II at 12 and 14; III, IV at 17, 19) | +1 |
| 9 | **Backwards Bell** block A,B,C (Leaning Bell, Heart Toll, Toll Tide) 9, 10.5, 12; rewinds C,B,A 14, 15, 16 | 3–4 |
| 17 | Clocks III+IV strike **with** rewind A (simultaneous) | 3 |
| 20 | Stopped Hour (glyph 20, stop 25–29) | 1–2 |
| 29 | Lash **with** Heart Toll | 2 |
| 30 | **Punish 3.5 s** | 0 |

### 5.4 P3 The Open Hour (loop ≈ 32 s)
| t | Event | Live |
|---|---|---|
| 0 | Tolling Hour ×3 (36) | 0 |
| 3 | **Two Futures** traced (3–7.8), halo verdict 8.5 | — |
| 10 | Steps 10, 11.2, 12.4, 13.6, 14.8, 16.0 (last **with Heart Toll**) | 2 |
| 12 | Clocks I–IV (singles) at 18, 21, 24, 27 | +1 |
| 18 | Stopped Hour glyph; stop 23–27; resume at 27 **with Reflection images and clock IV** (three rules on one tick) | 3 |
| 28 | **Punish 3.5 s** | 0 |

### 5.5 T2 Thirteen Chimes (30 s)
Tolling Hour ×6 (72) at 3–8: anyone below 73 HP dies — potion before. Then **one quote of each area, reworked** (the final exam), each alone: **Spire** — a sundial shadow cone that follows the boss's facing for 3 s, then locks (Aurelion's light, inverted to shadow); **Foundry** — a bellows knockback from the boss 3 m outward toward the tide line; **Verdant** — root circles at three hour marks that bloom 1.0 s apart. Then a 3.5 s punish.

### 5.6 P4 The Unfinished Hour (≈ 2:10)
The P1 **Ledger replays VI → I** (1.5 s apart) three times through the phase, each time with something new on top: (1) with clocks in pairs; (2) with a Stopped Hour whose resume lands on Ledger III; (3) with the Conductor's Hand phrase. Punish after each replay.

### 5.7 P5 Final Toll (hard enrage at 78 s)
Fracture active. The Thirteenth Hour counts every 6 s. Loop: Conductor's Hand phrase → Two Futures (steps interleaved with clock pairs) → Stopped Hour resuming with Reflection, Heart Toll and a clock (four rules, one tick) → Leaning Bell across the fissure → punish 3.5 s. Up to **4 live patterns**.

## 6. Player counts (summary)

| | N = 1 | N = 2 | N = 4 | N = 8 |
|---|---|---|---|---|
| Clocks | I–IV on you every round | 2 hours each | 1 each | 2 per hour, right-angle hands |
| Stopped Hour | always Stilled, no safety net | 1 Stilled, alternating | 2 | 4, crowded hiding strip |
| Ledger / Futures / Backwards / Hand | same shapes; images per player | same | same | same |
| Boss HP | measured DPS × target | × (1 + 0.7 (N−1)) | | |

## 7. Random branches (seeded, readable)

Clock hours and hands per round; the Stilled half; which future plays first and which authored future pair is used; the Conductor's phrase order (from four phrases); Leaning Bell's side (player-driven). Everything else is a fixed timeline, as in an Ultimate.

## 8. Decision density (decisions per 10 s; estimated, to be measured by bots)

| | P1 | P2 | P3 | P4 | P5 |
|---|---|---|---|---|---|
| Bellkeeper Ultimate | 3.0 | 3.8 | 4.2 | 4.5 | 5.0 |
| v2 (Medium) | ≈ 1.3 | | | | |
| Aurelion v4 | ≈ 2.3 (v3 measured 0.68 per 3 s); v4 higher | | | | |

A decision = one personal read (assignment) or one forced reposition.

## 9. DPS checks

Push times per phase (§5) and the P5 enrage. HP per phase = measured perfect-bot party DPS × target phase length (P1 1:45, P2 2:10, P3 1:50, P4 1:55, P5 1:05). Bots measure DPS first (Part 5); the numbers are set from that, never guessed.

## 10. New systems needed (each in its own small commit)

- **Personal status** with an ivory HUD/world glyph (clock: hour + hand; Stilled; Cracked) and expiry.
- **Stilled:** a timed root + ability lock on a player (the rules' provisional root, now with Part 0's freedom).
- **First-body lanes:** a lane that stops at the first player or the boss body.
- **Cracked Hour:** a damage-dealt multiplier of 0 for one player for 20 s.
- **Party wipe + push timer + enrage counter** (Bellkeeper-owned; Aurelion has its own on its branch — don't depend on it).
- **Ledger recorder:** store resolved hazard footprints with tick; replay reversed and sped up.
- **Practice:** `-lastbellUltimate`, `-lastbellUltimatePhase N`, `-lastbellAbility <id>`, and a menu option.

## 11. Conflicts with settled rules

1. Overlap, enrage, wipes, one-shots, length: **lifted for this fight** by Chzy18 (2026-10-23), recorded in `gpt/08-design-rules.md` and the repo copy (`docs/content-rules`, local commit `cce392e`).
2. "Boss stays targetable through phase changes" — kept: no untargetable transitions.
3. Easy ×0.6 / Hard ×1.3 damage scaling doesn't apply to the Ultimate entry (one tuning).
4. Potions: the Ultimate uses the normal 5 × +40; whether a run's leftover potions carry in is an open question.

## 12. Open questions

1. Does the run menu's Ultimate option replace the final boss of a run, or is it practice-only at first? (Provisional: practice-only until a human clear.)
2. Should a failed Pendulum Clock (Cracked Hour) also wipe on P5? (Provisional: no — the enrage already punishes it.)
3. Is 11–12 min at perfect DPS right, or should P4 be shorter?
4. Audio: every mechanic has a distinct rhythm (tick, catch, rewind, toll); there's no audio system yet.
