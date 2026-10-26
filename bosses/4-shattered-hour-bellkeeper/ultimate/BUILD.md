# The Bellkeeper — Ultimate: build contract (scaffold)

**Status:** scaffold built by the Ultimate session on `feature/bellkeeper-ultimate` (local branch; GitHub is suspended, nothing is pushed). Designed in `ULTIMATE.md`; strats in `GUIDE.md`. Built ≠ tested ≠ human-approved: nothing here is approved until Chzy18 plays it.

## How to launch

| What | How |
|---|---|
| The Ultimate | practice entry **"Bellkeeper — Ultimate"** (loads the Shattered Hour map), or any Bellkeeper fight with `-lastbellUltimate` / `-lastbellAbilitySet ultimate` |
| Start at a phase (practice only) | `-lastbellUltimatePhase N` with N = `1`–`5`, `P3`, `T1`, `T2`; a real attempt (a run) always starts at P1 |
| One move alone | `-lastbellAbility ult_<id>` (keys below) or `leaning_bell`, `toll_tide`, `reflection` (Ultimate tuning) |
| v2 / v1 | unchanged: the normal "Bellkeeper" entry is v2; `-lastbellAbilitySet v1` |
| Autotest, Ultimate only | `LastBell.exe -lastbellAutotest -lastbellBellkeeperUltimate [-lastbellTestAbility ult_<id> \| scaffold \| plan \| bots]` |
| Build + test (holds the Unity lock) | `tools/bellkeeper-ultimate/build-and-test.sh <name> [player args]` → `last bell/bellkeeper-ultimate-evidence/<name>` |

## Files (one owner each)

| File | Owner |
|---|---|
| `unity/Assets/LastBell/Run/Encounters/Bellkeeper/Ultimate/BellkeeperUltimate.cs` (plan, segments, push timers, HUD) | Ultimate session |
| `Ultimate/UltimateMechanic.cs` (base class, `UltCue`, `BotDemand`, registry) | Ultimate session |
| `Ultimate/UltimateState.cs` (shared systems, `UltShapes`) | Ultimate session |
| `Ultimate/Bots/UltimateBot.cs` (strat bot) | Ultimate session |
| `Ultimate/<Mechanic>.cs` + `Tests/RunAutoTest.Bellkeeper.Ult.<Mechanic>.cs` | that mechanic's session |
| `Tests/RunAutoTest.Bellkeeper.Ultimate.cs` (harness, scaffold + plan checks) | Ultimate session |

| Mechanic | Key | Class / file | Test file |
|---|---|---|---|
| Pendulum Clocks | `ult_pendulum_clocks` | `PendulumClocks` / `PendulumClocks.cs` | `Ult.PendulumClocks.cs` |
| The Stopped Hour | `ult_stopped_hour` | `StoppedHour` / `StoppedHour.cs` | `Ult.StoppedHour.cs` |
| The Ledger | `ult_ledger` | `LedgerMechanic` / `Ledger.cs` | `Ult.Ledger.cs` |
| Backwards Bell | `ult_backwards_bell` | `BackwardsBell` / `BackwardsBell.cs` | `Ult.BackwardsBell.cs` |
| Two Futures | `ult_two_futures` | `TwoFutures` / `TwoFutures.cs` | `Ult.TwoFutures.cs` |
| The Conductor's Hand | `ult_conductors_hand` | `ConductorsHand` / `ConductorsHand.cs` | `Ult.ConductorsHand.cs` |
| Tolling Hour · The Thirteenth Hour · T2 area quotes | `ult_tolling_hour`, `ult_thirteenth_hour`, `ult_area_quotes` | `TollingHour`, `ThirteenthHour`, `AreaQuotes` (three files) | `Ult.TollingHour.cs` |
| Fracture of Time | `ult_fracture` | `FractureOfTime` / `Fracture.cs` | `Ult.Fracture.cs` |

Each mechanic file starts as a stub (`Built = false`) that only waits out its cue, so the whole plan runs before any mechanic is built. Replace the stub; keep the class name, `Id` and public members the plan uses.

## The mechanic contract (`UltimateMechanic : BellkeeperAbility`)

- `Cast()` is a tick coroutine (`yield return n` = wait n combat ticks). **Tick 0 is the launch tick.** Hazards go through `Spawn()` (tests see them in `Spawned`; the ledger recorder hears them).
- **Timing.** A hazard spawned on tick *s* with `ResolveAt = R` resolves on tick *s + R − 1* (it ages on its spawn tick). `UltShapes.ResolveIn(n)` gives the `ResolveAt` that resolves exactly *n* ticks from now. Simultaneous resolutions are built by resolving on the **cue's tick**, so resolve exactly on `TickAt(Cue.At[k])`.
- **Cue** (`UltCue`, set by the plan before launch; `DefaultCue()` for a bare launch): `Variant`, `At` (seconds after launch), `Phase` (1–5, 0 = transition), `Seed` (seeded variation), `Count`. Each stub's header says what its cue means (copied below).
- **Bot strat.** `Demands(PlayerController p, List<BotDemand> into)`: for each thing the guide tells player *p* to do, add `{ Tick = when it is checked, Since = when a player could first know it, Ok = positions that pass, Hint = the guide's spot, Memory = true if it needs a read or a memory (not a visible coral shape), MinHp, Why, From = this }`. `UltimateBot` solves these together with every live hazard. Imperfect bots: `Late` reacts 0.25 s later; `Miss` skips the first `Memory` demand of each segment.
- **Cancel()**: boss death, halt, restart, phase change. Drop presentation and references. The encounter already cancels hazards and clears statuses (`Ult.Reset()`).
- **Practice()**: `-lastbellAbility ult_<id>` loops it with `DefaultCue()`. Override if it needs partners.

### Cues the plan sends (`BellkeeperUltimate.cs`)

| Mechanic | Cue |
|---|---|
| Pendulum Clocks | launch = numerals and hands appear (≥ 6 s before hour I). `"singles"`: hour k strikes at `At[k]` (I..IV). `"pairs"`: I+II strike at `At[0]`, III+IV at `At[1]`; solo plays II and IV 2.0 s after I and III. Must also implement `Vector3? LeanDirAt(int worldTick)`: the Leaning Bell pairing rule (§3.1), the normalised sum of the striking hour's station directions (null if no strike on that tick). |
| The Stopped Hour | launch = cracked-hourglass glyph. `At[0]` = the stop starts, `At[1]` = time resumes and the chains lash (default 5, 9). `"alone"` (T1) or `"lash"` (partners resolve on the resume tick). The lane comes from `Ult.StationOf(p)`; assign one (and show it) for a player with no clock. |
| The Ledger | `"record"` (P1 loop 1): set `Ult.Recording`, keep the six strikes Ledger I..VI (Leaning Bell, Toll Tide, Heart Toll, Reflection images, Leaning Bell, Heart Toll), then stop. `"replay"` (P4): `At[0]` = the first replayed strike (VI), then 1.5 s apart to I. With nothing recorded (practice from P4) replay an authored clean ledger. |
| Backwards Bell | `"block"`: `At` = A, B, C resolve (Leaning Bell, Heart Toll, Toll Tide, built from `UltShapes`); rewinds resolve C, B, A at C + 2.0, + 3.0, + 4.0 s. |
| Two Futures | launch = the fingers start tracing. `At[0]` = the first interleaved step; six steps 1.2 s apart (the plan puts a Heart Toll on the last). |
| The Conductor's Hand | launch = the phrase. `At[0]` / `At[1]` = move 1 / move 2 resolve (real telegraphs only 2.0 s before each). It launches the moves itself: `Boss.UltMove(Boss.UltLean, BellkeeperEncounter.LeanTuning(dir, 120))`, `Boss.UltLaunch(mechanic, cue)`, or shapes from `UltShapes`. |
| Tolling Hour | `"tolls"`: one unavoidable 12-damage raidwide per `At` entry (`UltShapes.Raidwide`). |
| The Thirteenth Hour | P5 start; `At[0]` = XIII (78 s): 999 to everyone. One 6-damage toll every 6 s, I..XIII. The plan also wipes 0.5 s after `PushSeconds[P5]` as a fallback. |
| T2 area quotes | `"quotes"`: `At` = Spire (sundial shadow cone), Foundry (bellows knockback), Verdant (root circles) resolve times, each alone. |
| Fracture of Time | P5 start: the fissure lane (`UltimateState.FissureDir` through the centre, 1.4 m wide) burns 30/s (`BurnPerSecond`) until the boss dies. |

## Shared systems (`boss.Ult`, `UltimateState`)

- **Arena:** `StationDir(i)`, `Station(i)` for the four rim stations, i = 0..3 = **NE, SE, SW, NW** (plan 45° + 90° k, r 13.2 m; north is −Z, east is −X). `ULTIMATE.md` says N/E/S/W; the build uses the arena's real diagonal stations (a recorded conflict). `FissureDir` (NW ↔ SE) and `FissureNormal`.
- **Status and glyphs:** `Of(p)` (Hour, Station, StrikeTick, Stilled window, hourglass, Cracked, Note). `SetClock`, `ClearClock`, `StationOf`. Glyphs are drawn automatically: ivory disc, brass hand toward the station, and the numeral and state on the HUD over the head. Friendly colours only.
- **Stilled:** `Still(p, fromTick, ticks, warnTicks)`, `IsStilled(p)`. Roots the player and blocks abilities, dash and potions (no iframes: a Stilled player still takes hits).
- **Cracked Hour:** `CrackedHour(failingPlayer)`: 30 to everyone, and the failing player deals 0 damage for 20 s (`World.DealtScale`).
- **First-body lanes:** `FirstBodyLane(owner, source, from, toward, width, resolveAt, damageFor, firstHit)`: the first standing player along the lane takes `damageFor(p)`; the Bellkeeper's body stops it harmlessly.
- **Ledger recorder:** `Recording`, `Recorded` (`Footprint`s in resolve order), `FootprintRecorded` event, `Footprint.ToHazard(...)`, `Outline(footprint)` (dashed ivory memory look).
- **Wipes:** `Wipe(cause)` (lethal to every standing player; the director goes to Downed). `UltimateState.Lethal = 999`.
- **Shapes:** `UltShapes.LeaningHalf / Tide / HeartToll / Raidwide` (Ultimate numbers: Leaning Bell 50, standard 40, heavy 60, toll 12; warnings 3.5 / 2.5 / 2.0 s).
- **Reworked v2 moves** (launched by the plan with `CastTuning`): Leaning Bell (50, 3.5 s, `Dir` forced for the pairing rule and the fissure), Toll Tide (2.5 s tide, 2.0 s Heart Toll, 40 each; `SecondOnly` = Heart Toll alone), Reflection (40). Striking Order is retired; the Unfinished Hour became the Ledger.

## The plan (`BellkeeperUltimate.cs`, ULTIMATE.md §5; seconds from each loop's start)

- **P1 (31 s loop, until 85 %, push 2:10):** Tolling ×2 at 0; Ledger "record" at 0 (first loop only); clocks singles at 3 (strikes 12, 17, 22, 27); Leaning Bell 5 → 8.5; Toll Tide 9 → 11.5, Heart Toll 13.5; Reflection 15 → 19; Leaning Bell 18.5 → 22 leaning toward clock III's station; Heart Toll 25 → 27 with clock IV; punish 27–30.5.
- **T1 (25 s):** Stopped Hour at 0 and 12 (stop +5, lash +9); punish 21.5–25.
- **P2 (34 s loop, until 55 %, push 2:30):** Hand at 0 (moves 7, 8.5); clocks pairs at 6 (12, 18.5); Backwards Bell at 8 (A 11.5, B 13, C 14.5; rewinds 16.5, 17.5, 18.5 = with clocks III+IV); Stopped Hour at 20 (stop 25, lash 29); Heart Toll 27 → 29 with the lash; punish 30–33.5. **Second form** (2 s) at 55 %.
- **P3 (31.5 s loop, until 35 %, push 2:10):** Tolling ×3 at 0; Two Futures at 3 (steps 10.0 … 16.0); Heart Toll 14 → 16; clocks singles at 12 (18, 21, 24, 27); Stopped Hour at 18 (stop 23, resume 27); Reflection 23 → 27 (resume + images + clock IV on one tick); punish 28–31.5.
- **T2 (29 s):** Tolling ×6 at 3; quotes at 10 (14, 19, 24); punish 25.5–29.
- **P4 (50.5 s loop, until 15 %, push 2:10):** (1) Ledger replay at 0 (first strike 4) with clock pairs (7.75, 10.75: locks between replayed strikes), punish; (2) Stopped Hour at 0 (resume 9) with Ledger replay at 0.5 (III lands at 9), punish; (3) Hand at 0 (moves 7, 9.5) with Ledger replay at 0 (first 4), punish.
- **P5 (35 s loop: timeline 31.5 + punish 3.5; hard enrage 78 s):** Fracture and Thirteenth Hour at P5 start; Hand at 0 (7, 8.5); Two Futures at 9 (steps 16.0 … 22.0); clock pairs at 10 (17.2 with step 2, 27 with the resume); Stopped Hour at 18 (stop 23, resume 27); Reflection 23 → 27; Heart Toll 25 → 27 (four rules on one tick); Leaning Bell across the fissure 28 → 31.5; punish 31.5–35.
- HP can't fall below the running segment's threshold until its transition; transitions wait for the current moves. Push timers wipe the party if the threshold isn't reached in time. Boss HP is a placeholder (`UltimateBaseHp`) until the bots measure DPS (Part 5).

## Test harness (`RunAutoTest.Bellkeeper.Ultimate.cs`)

`UltStart(ability, segment)` → an Ultimate practice fight on the Throne, paused at combat start, boss halted at the centre, player at (0, 0, 6). Then `bk.BeginCombat()` (plan), or **`UltManual()` and then launch directly** (`bk.UltLaunch(m, cue)`, `bk.UltMove(...)`): a halted boss runs only a launched move's first tick, so always call `UltManual()` (scheduler on, no plan) before launching by hand, step exact ticks with `BkStep(n)` / `UltStepAlive(n)` / `UltStepAliveUntil(cond, max)`, add players with `BkAdd(pos)`, read hits in `bkHits` / `BkHitsOn(p, source)`, drive a player with `UltimateParty.Drive(p, slot, of)` (set `Speed = 4.2f`, `Attacks = false` for escape proofs). `UltLeave()` clears the overrides.
