# Aurelion ability set v3: Milestone A (proposal)

*Local branch `feature/aurelion-v3`. Launch with `-lastbellAbilitySet v3`; `v2` keeps v2.1.*

This is the response to Chzy18's "still not hard enough solo" (Hard, solo ranged, 20 % HP), following `HARDER-PROMPT.md`.

Milestone A delivers:
- two memory mechanics
- the interim loop
- Step 0 telemetry
- the bot proof
- the escape check

Milestone B adds the third mechanic, the full phase plan, the Hard named mechanic and the co-op assignment tests.

## 1. Step 0: telemetry and the control run

**Telemetry.** `Encounters/Aurelion/AurelionTelemetry.cs` sits on the boss for every v2 or v3 fight. It logs:
- hits per ability
- every resolution's escape margin: the signed distance to the shape's edge at the resolve tick ÷ 6 m/s, negative = hit
- phase start times
- potions drunk

Outside the autotest it appends to `persistentDataPath/aurelion-telemetry.csv`. In the autotest the summary goes into the report.

**The control.** The reflex-perfect, memory-blind bot (`AllyBot` `BotMode.Reflex`):
- reacts on the tick any shape appears, using dash and sprint
- drinks under 70 HP
- holds a ranged 8.5 m gap

It ran solo on **v2.1 Hard**, 3 loops (each loop = one pass of every phase's plan):

| | Result |
|---|---|
| Hits per loop | **0 / 0 / 0** |
| Survived 3 loops | yes |
| Loop length | 80 s |
| Potions | 2 |

**Verdict:** the diagnosis stands. Perfect reflexes beat v2.1 completely.

Run 2's first control died to 2 Orrery Horizon hits, but that was a bot bug: it dashed 3 m past a near safe spot into the lit rim and didn't drink at 48 HP. With both fixed it takes no hits.

**Escape margins at resolve** (telemetry, v2.1 Hard, control bot): the bot's typical clearance was a few centimetres to half a second. Every v2.1 shape is answered by stepping just out of it.

| Shape | Median clearance |
|---|---|
| Meridian Lances | 1.35 s |
| Starfall | 0.44 s |
| Noon Shadow | 0.28 s |
| Zenith Collapse | 0.02 s |

## 2. Why v2.1 is easy (confirmed)

1. Every pattern shows its whole answer, and the escape is a short sidestep. A bot with no memory takes no damage.
2. Paired patterns share a route.
3. Damage was uneven: Lances 35, Orrery 40, Zenith Collapse 45. With 300 effective HP that is about 7 mistakes per fight.

## 3. What changed, and why

| v2.1 slot | v3 (Milestone A) | Why |
|---|---|---|
| Noon Shadow (every phase) | **Ecliptic Order** | Noon is a pure "see the half, step out". Ecliptic needs memory. |
| plain Orrery Closing (Phases 1–2) | **Zenith Marks + Meridian Lances** | Orrery's band is a reflex read. Marks need you to remember a word and fight the Lances' aim to get there. |
| Grand Orrery (Phase 3) | unchanged | It keeps the Hard named mechanic (Hour Hands) until Milestone B. |

**Damage tiers** (v3 only, `AurelionAbility.V3Tier`): every hit is punish (≤ 30) or kill (≥ 50) at Medium, with nothing in between.

| Ability | v2.1 | v3 |
|---|---|---|
| Meridian Lances | 35 | 30 |
| Orrery Closing | 40 | 50 |
| Zenith Collapse | 45 | 50 |

Ecliptic Order and Zenith Marks are 50. Hard is ×1.3: 39 for punish hits, 65 for kill hits.

**Hard windows:** the two new mechanics keep Medium timings on Hard; their difficulty is memory, not speed. `-lastbellWindowsMedium` (test switch) gives Hard damage and mechanics with Medium window lengths for every v2.1 ability.

## 4. The mechanics

### 4.1 Ecliptic Order (`aurelion.ecliptic_order`): kill tier

- **Source:** FFXIV Limit Cut (ordering + memory), turned into a solo-first floor mechanic.
- **Fantasy and look:**
  - He returns to the compass star and calls the ecliptic.
  - Brass-rimmed discs light on the floor, each with an ivory numeral I–IV (friendly identity colours).
  - After 2.5 s they go dark.
  - Then the sky strikes the whole dais except one disc at a time, in numeral order.
- **Shape:**
  - 3 anchors in Perfect Order, 4 after that, on a 5 m circle round the centre.
  - Each strike is a Ground ring from the safe disc's edge out past the rim.
  - Safe disc: 3.0 m at N 1–2, 3.5 m at N ≤ 4, 4.5 m above.
  - No knockback, 0.25 s stun.
- **Order:** consecutive anchors are always ≥ 120° apart (≥ 8.66 m). There are 3 authored orders per anchor count, cycled per cast (variation without randomness).

**Timings (from the reveal):**

| Level | Numerals | Strike I | Then every | Coral shows |
|---|---|---|---|---|
| Easy | stay lit | 4.0 s | 2.75 s | 1.0 s before |
| Medium | 2.5 s | 3.2 s | 2.2 s | 0.35 s before |
| Hard | 2.5 s | 3.2 s | 2.2 s | 0.35 s before |

- **Why memory is required:** the coral strike shows 0.35 s before it lands. The reflex escape from the worst spot in the previous disc is 0.83 s (dash + sprint), which exceeds 0.35 + 0.25 s.
  - From the one best spot (near edge, closest pair) a reflex can make it (0.23 s). That's information, not a failure.
  - In the autotest the memory-blind bot is hit by ≥ 3 of 4 strikes.
- **Melee and ranged:** both must move every strike; standing still anywhere is hit by every strike.
- **Worst case:** at the reveal you are on the rim opposite anchor I (16.2 m). That's 0.62 s of margin at 4.2 m/s with dash + sprint, 0.48 s at 6 m/s with one dash.

### 4.2 Zenith Marks (`aurelion.zenith_marks`): kill tier

- **Source:** FFXIV debuff resolution (DSR Wroth Flames, TOP Hello World): your position must suit a mark that resolves with a later pattern.
- **Fantasy and look:**
  - He calls each player to his shade or out into the sun.
  - A word over your head: **SHADE** in cobalt or **SUN** in ivory.
  - The verdict is coral, for that player only (new `Hazard.Affects`).
- **Shapes:**
  - SHADE: a Ground ring from 5.5 m out past the rim (be within 5.5 m of him).
  - SUN: a Ground disc of 8.5 m round him (be beyond 8.5 m).
  - No knockback, 0.25 s stun.
- **Pairing:** it resolves on the same tick as the paired Meridian Lances (3 beams), so it is that pattern's partner, never a third pattern. It never pairs with Zenith Collapse. Its block walks him to the centre first.
- **Misalignment (Phase 2), two-step:** both words show from the start ("SHADE > SUN"). Step 1 resolves with Lances volley 1, step 2 (the flip) with volley 2.
- **Solo:** alternates SUN / SHADE per cast, SUN first (the Zenith Collapse block before it has just pulled you in).
- **N ≥ 2:** half SHADE, half SUN, rotating per cast.
- **Timings:**

| Level | Mark shows | Coral verdict |
|---|---|---|
| Easy | 4.0 s ahead | 1.0 s ahead |
| Medium | 3.0 s ahead | 0.35 s ahead |
| Hard | 3.0 s ahead | 0.35 s ahead |

- **Why memory is required:** the reflex escape from the worst spot is 0.83 s (rim to shade) or 0.65 s (beside him to sun). Both exceed 0.35 + 0.25 s.
- **Melee and ranged:**
  - SHADE pulls a ranged player in.
  - SUN pushes a melee player out.
  - Standing still between 5.5 and 8.5 m is hit by both.
- **Worst case:** two-step SUN > SHADE from the rim, 8.7 m in 2.04 s on Hard: 0.34 s at 6 m/s walking, 0.57 s at 4.2 m/s with dash + sprint.
- **Fixed during testing:**
  - Shade 4.5 m with 4 Lances beams left almost no gap (eight half-lanes 45° apart).
  - Now the shade is 5.5 m and the beams are 3.

## 5. The interim loop (Milestone A)

| Phase | Blocks (gaps 0.3 s) |
|---|---|
| 1 Perfect Order | Lances (3) + Starfall › **Ecliptic Order (3)** › Zenith Collapse + Lances (3) › **Zenith Marks + Lances (3)** › punish 3.5 s |
| 2 Misalignment | Lances (4, 2 volleys) + Starfall › **Ecliptic Order (4)** › Zenith Collapse + Lances (3) › **Zenith Marks two-step + Lances (3, 2 volleys)** › punish 3.5 s |
| 3 Exposed Meridian | Cascading Lances + Starfall › **Ecliptic Order (4)** › Zenith Collapse + Lances (4) › Grand Orrery › Horizon (Hard: Hour Hands) › punish 3.5 s |

Rules kept:
- at most two damaging patterns at once
- one hit per instance
- harmless transitions
- 3.5 s punish
- no class requirement
- no invincibility anywhere

## 6. Results against the targets (Hard, solo)

From full autotest run 5 (`aurelion-logs/v3-5/autotest-report.txt`): **620 passed, 2 failed**. The two failures are the known intermittent Bellkeeper check in shared run-loop code and the density note below.

| # | Target | Result | Status |
|---|---|---|---|
| Step 0 | Memory-blind reflex bot on v2.1 Hard: < 2 hits per loop, survives 3 loops | **0 / 0 / 0 hits**, survives | met: the diagnosis stands |
| 1 | Decision density ≥ 1 per 3 s outside the punish, and ≥ 1.5 × v2.1 | v3 **0.68**, v2.1 0.47 (1.45 ×) | **not met on the interim loop.** Milestone A only replaces 2 of the 4 blocks per phase; the full plan (Milestone B) must carry it. |
| 3 | ≥ 2 conflict pairings per loop | Zenith Marks + Lances, where the beams track you as you run in or out (Phases 1 and 2); Grand Orrery (Phase 3) | met by design; a formal check is Milestone B |
| 5 | ≤ 5 mistakes per fight; ≥ 60 % of the blind bot's hits kill-tier; no damage between 30 and 50 | mean Hard hit **65.0** = **4.6 mistakes**; **18 / 18** hits kill-tier | met |
| 6 | Hit recovery, no chain deaths | hit by Ecliptic strike I at the worst moment, the perfect route still makes II and III (Hard) | met |
| 7 | Control/test pair with `-lastbellWindowsMedium` | the blind bot **goes down within 1.3 loops**, every kill hit from a memory mechanic, none from Hour Hands or a soak. Kept alive for 2 loops, it takes 9 kill hits per loop: Ecliptic 16, Zenith Marks 2 | met. The downing run's hits are all Ecliptic (it dies before the Marks), so the "≥ 2 memory mechanics" proof uses the kept-alive run. |
| 7 | Fairness: the perfect route survives every v3 block | one full loop on Hard, no Ecliptic or Marks hit, not downed | met |
| 8 | Margins Hard ≥ 0.3, Medium ≥ 0.5, Easy ≥ 0.8; no warning under 2 s; loop stamina | escape check: all pass, tightest 0.30 s; cues show 2.5 s and 3.0 s ahead | met |
| Memory | The resolve-time cue is shown later than the reflex escape + 0.25 s | coral leads 0.35 s vs reflex escapes 0.65–0.94 s; the blind bot fails ≥ 3 of 4 Ecliptic strikes and the SHADE mark | met |

Targets 2, 4 and 9 wait for Milestone B, as the prompt sets out.

### Damage mix

| Set | Effective HP ÷ mean Hard hit | Mistakes per fight |
|---|---|---|
| v2.1 | about 7 | 7 |
| v3 interim | 300 ÷ 65 | 4.6 |

**When to drink:** with 1 s between drinks, the windows are:
- the 3.5 s punish
- the 2.5 s numeral reveal before strike I

You can't safely drink between Ecliptic strikes (2.2 s, mostly travel).

## 7. Escape check

`tools/encounter-escape/aurelion-v3-escape-check.ps1` (output: `abilities-v3/escape-check.md`). It covers:
- every new shape at Easy, Medium and Hard, and N = 1, 4, 8
- the SHADE + Lances side-step and both two-step orders
- the memory test
- the Ecliptic block as a route from every rim start and order, with real stamina (lowest 12 stamina on Medium at 4.2 m/s)

**All cases pass.** The tightest margin is 0.30 s: Hard, Ecliptic chain at 4.2 m/s.

## 8. Conflicts

1. **Medium gets harder too.** The new core mechanics apply to Medium and Hard, which raises Medium (open question 1).
2. **Hard windows.** v3 mechanics don't use v2.1's Hard ×0.7 windows (rule: don't uniformly speed attacks up). Hard is harder through damage (×1.3) and Hour Hands.
3. **The "own sound" requirement.** Kill-tier memory mechanics need their cue shown ≥ 2 s ahead (met: 2.5 s and 3.0 s) with its own sound. **The project has no audio system yet**, so the sounds are a gap until one exists.
4. **Hazard.Affects (per-player verdicts):** every player sees every coral verdict (a SHADE ring for one player covers the floor another stands on). It's fine solo; in co-op it may read as "everyone is hit". Milestone B should tint other players' verdicts or draw only your own.

## 9. Open questions for Chzy18

1. Should Medium get the new mechanics, or only Hard (with Medium keeping v2.1)?
2. Ecliptic numerals for 2.5 s: too short, too long?
3. SHADE / SUN as words over the head: readable mid-fight? Icons instead?
4. New statuses (Zenith Marks, numerals) are new to the project: OK to keep?
5. Hard named mechanic for Milestone B: replace Hour Hands with Afterimage or Sundial Brand (recommended in the prompt)?
