# The Bellkeeper — Ultimate: morning report (2026-10-24)

> **Out of date (kept as the 24 Oct record).** It describes the Ultimate's first version (v1: the Pendulum Clocks to the Thirteenth Hour). Since then:
> - **Ultimate v2** replaced the phases: P1 Escapement Relay and Chime Debts, P2 Broken Hour, P3 Open Hour, The Floor Breaks, P4 Gear Floor, The Train Runs Wild, P5 Final Toll. All are proposals, built in Unity, and none is human-approved.
> - The Ultimate is the only Bellkeeper fight. The normal v2 attack set is retired; the normal entry is v1.
> - Easy, Medium and Hard all apply to the Ultimate (gpt/08, settled 25 Oct). It no longer forces Medium.
> - The repository moved to Azure DevOps. The Ultimate is on `integrate/bellkeeper-ultimate`, draft PR 5.
> - Current docs: `BUILD.md`, `P1-v2-LOCKED.md`, `P2-v2-LOCKED.md`, `P5-v2-PROPOSAL.md` and `ART-PLAN-v2.md`.

**Status:** designed, built and tested by autotest and strat bots. **Not human-approved:** nothing here is approved until Chzy18 plays it. Every mechanic is a proposal (`ULTIMATE.md`, as changed below).

Branch: `feature/bellkeeper-ultimate` (worktree `last bell\bellkeeper-ultimate-design`), **local only**. GitHub is suspended, so nothing was pushed. Eight mechanic branches `feature/bellkeeper-ult-<id>` are merged into it.

## How to play it

Build `unity/Builds/Windows/LastBell.exe` with `tools/bellkeeper-ultimate/build-and-test.sh <name>` or Unity's normal Windows build, then:

| What | Launch |
|---|---|
| The Ultimate | menu → Practice → **"Bellkeeper — Ultimate"** (loads the Shattered Hour Throne) |
| Ultimate as the run's final boss | `LastBell.exe -lastbellUltimate` (no menu change; Easy/Medium/Hard keep v2) |
| Start at a phase (practice only) | `-lastbellUltimatePhase 3` (`1`–`5`, `T1`, `T2`) |
| One move alone | `-lastbellAbility ult_pendulum_clocks` (`ult_stopped_hour`, `ult_ledger`, `ult_backwards_bell`, `ult_two_futures`, `ult_conductors_hand`, `ult_tolling_hour`, `ult_thirteenth_hour`, `ult_area_quotes`, `ult_fracture`) |
| v2 / v1 | unchanged (`Bellkeeper` entry = v2; `-lastbellAbilitySet v1`) |
| Seeded variation (tests) | `-lastbellSeed N` |
| Dev keys | F8 next phase threshold, F9 defeat |

The HUD shows the segment, the push timer (P5: "XIII in N s"), and each player's ivory glyph (clock numeral and station, hourglass, CRACKED, LASH).

## What was built

- **Scaffold** (`Ultimate/BellkeeperUltimate.cs`, `UltimateState.cs`, `UltimateMechanic.cs`, `BUILD.md`):
  - Ability set `ultimate` and the separate practice entry.
  - The plan: P1 → T1 → P2 → second form at 55 % → P3 → T2 → P4 → P5, running the timelines of `ULTIMATE.md` §5 (as-built times are in `BUILD.md`).
  - HP floors, so no phase can be skipped. Push timers, the hard enrage, wipes and the Ultimate HUD.
  - Personal status with ivory glyphs, Stilled, Cracked Hour (per-player damage attribution), first-body lanes, the ledger recorder and the Ultimate shapes.
- **Mechanics**, one session each, all merged:
  - Pendulum Clocks, The Stopped Hour, The Ledger, Backwards Bell, Two Futures, The Conductor's Hand.
  - Tolling Hour, The Thirteenth Hour and the T2 area quotes (Sundial, Bellows, Root Hours).
  - Fracture of Time.
- **Strat bot** (`Ultimate/Bots/UltimateBot.cs`): plays `GUIDE.md` through ordinary input. It solves every live hazard plus each mechanic's `Demands()`. It has three skills: Perfect, Late (+0.25 s) and Miss (skips one memory assignment per phase).
- **Tools:**
  - `tools/bellkeeper-ultimate/build-and-test.sh` (lock-aware; timeout 6000 s).
  - `tools/bellkeeper-ultimate/parallel-runs.sh`: one build, then many game instances at once, with a 1.5 GB free-RAM floor and one job worker each (Chzy18: "run more").

## Test results (latest)

| Run | Result | Evidence (`last bell\bellkeeper-ultimate-evidence\`) |
|---|---|---|
| Full run-loop autotest (every check: run loop, v1, v2, Ultimate) | **865 / 865, 0 errors** | `clears-par-7\runloop` |
| Ultimate section (scaffold, plan, all mechanics) | **392 / 392, 0 errors** | `clears-par-7\fullult` |
| Scaffold only (first) | 529 / 529 full | `scaffold-full-1` |

Each mechanic's own checks passed on its branch:

| Mechanic | Own checks |
|---|---|
| Pendulum Clocks | 52/52 |
| The Stopped Hour | 49/49 |
| The Ledger | 71 checks |
| Backwards Bell | 26/26 |
| Two Futures | 37/37 |
| The Conductor's Hand | 46/47 before its N = 8 Stopped Hour fix; passing in the full section |
| Tolling Hour group | 33/33 |
| Fracture of Time | 30 checks |

## Bot results (Part 5) at the final tuning

Final tuning (`b58568e`):
- Base HP 12 000, × (1 + 0.7 (N − 1)); solo bar × 0.65.
- Push timers: P1 54 s, P2 100 s, P3 95 s, P4 125 s.
- P5 hard enrage 78 s (the Thirteenth Hour's XIII).

Fights are stepped (exact ticks), with several seeds each, run in parallel (`clears-par-6` + `clears-par-7`):

| Bots | Result | Target |
|---|---|---|
| Perfect party of 4 (Vanguard, Chimesinger, Arcanist, Vanguard) | **7 / 8 kills**, 5:29–6:43 | must win ✓ |
| Perfect party of 8 | **2 / 2 kills** | must win ✓ |
| Perfect solo Chimesinger | 1 / 2 | must win: partly |
| Perfect solo Vanguard | 0 / 2 (dies in T2 to the Sundial or the tolls) | must win ✗ |
| Perfect party of 2 | 0 / 3 (P5 at 3–11 %) | must win ✗ |
| Miss (one assignment per phase), party of 4 | 0 / 2 | must wipe ✓ |
| Late (+0.25 s), solo | 0 / 2 | must wipe ✓ |
| Late (+0.25 s), party of 4 | **4 / 8 kills** | must wipe ✗ (half) |

**Recorded attempts** (30 fps, ~29 MB MP4s; videos stay out of git):

| Folder | Attempt | Result |
|---|---|---|
| `clears-par-7\video` | Perfect party of 4 | **KILL in 5:38** |
| `clears-par-7\video` | Late party of 4 | wipe at 3.1 % |
| `clears-par-7\video` | Perfect solo | wipe in T2 |
| `live-attempts-1`, `part5-attempts-2/3` | Earlier, provisional tunings | see each folder |

## Escape proof

- **Paper:** `tools/encounter-escape/bellkeeper-ultimate-escape-check.ps1` + results (Part 3). Every real route passes at 4.2 and 6 m/s with finite stamina, and the memory-free routes fail by design.
- **In game:** every mechanic session added bot-route checks at 4.2 m/s with finite stamina for N = 1/2/4/8.
- **Design changes since Part 3**, each with its route proved by its session:
  - Two Futures' coral warning is 0.5 s, with ≤ 2.42 m steps.
  - The Ledger's coral warning is 1.2 s.
  - The Conductor's Hand's real telegraph is 1.5 s.
  - Backwards Bell's rewinds are complements.

## Decision density (per player per 10 s; reads + forced repositions, counted by the strat bot)

| | P1 | T1 | P2 | P3 | T2 | P4 | P5 |
|---|---|---|---|---|---|---|---|
| Ultimate, perfect party of 4 | 2.0 | 1.2 | 2.8 | 2.2 | 2.1 | 2.4 | 3.6 |
| Ultimate, perfect party of 2 | 1.9 | 1.2 | 3.0 | 3.0 | 2.2 | 3.1 | 5.3 |
| Ultimate, solo | 1.8 | 1.6 | 3.6 | 4.4 | – | – | – |

**Comparison caveat:**
- **v2:** the same counter on v2 gave 17.2 per 10 s. It counts every 2 m re-target, and the v2 bot re-targets constantly while dodging with nothing to remember, so the numbers aren't comparable. A fair metric, counting only reads plus forced moves of ≥ 2 m per rule, is an open item.
- **Aurelion v4:** not measured. I didn't run another boss's branch, and its design doc's estimate is about 2.3.

## Designed / built / tested / human-approved

| Mechanic | Designed | Built | Tested | Human-approved |
|---|---|---|---|---|
| Pendulum Clocks | ✓ | ✓ | ✓ (52/52 + integration) | no |
| The Stopped Hour | ✓ | ✓ | ✓ (49/49) | no |
| The Ledger | ✓ | ✓ | ✓ (71) | no |
| Backwards Bell | ✓ | ✓ | ✓ (26/26) | no |
| Two Futures | ✓ | ✓ | ✓ (37/37) | no |
| The Conductor's Hand | ✓ | ✓ | ✓ (integration 392/392) | no |
| Tolling Hour · Thirteenth Hour · T2 quotes | ✓ | ✓ | ✓ (33/33) | no |
| Fracture of Time | ✓ | ✓ | ✓ (30) | no |
| Plan, scaffold, bot | ✓ | ✓ | ✓ | no |

## Changes to the design (hardening; Chzy18: "Definitely add and replace abilities as you see fit")

- **Stations:** the Throne's real diagonal NE/SE/SW/NW (the design said N/E/S/W). The boss holds the centre.
- **Pendulum Clocks:**
  - A down striker still cracks.
  - With 5+ players, a pair tick uses two adjacent hands, held twice (side-by-side strikers).
- **Stopped Hour:**
  - The boss's body stops a chain only on its centre line.
  - Reflection resumes use adjacent stations.
  - P5 hiding spots sit 1.65–1.9 m off the fissure.
  - The lash station locks at the hourglass.
  - Frozen clocks must still hit, so solo hugs the boss.
- **Ledger:**
  - Coral 1.2 s.
  - A messy P1 costs: images spread ×1.5, and opposite halves replay back to back.
  - No reflections replay in stopped time.
  - One page = one strike.
  - At most one forced, survivable hit (MinHp 61) in P4 block 2.
- **Backwards Bell:** each rewind strikes everywhere its original didn't (complements).
- **Two Futures:**
  - Coral 0.5 s.
  - Each step covers everything but one pocket.
  - Authored, seeded pairs.
  - P5 steps are bands.
- **Conductor's Hand:**
  - Telegraph 1.5 s.
  - The Leaning half is fixed at the phrase.
  - The right swing uses Backwards Bell's complement rewinds.
- **Tolling / T2:**
  - Sundial on the farthest player.
  - The Bellows gets a Forge Core, and the push is a pure shove.
  - Root Hours cover the whole floor (rooted = one-shot chain).
  - Thirteenth Hour tolls 6 → 4 (Part 5: perfect parties died of attrition).
- **Fracture:** flares (2.4 m, 60) on every great-bell toll.
- **Plan timing:** P4 block 1's clock pairs moved to 7.75 / 10.75 s, and the tuned v2 moves strike exactly on their cue tick.
- **Shared code (review):**
  - Damage attribution and `DealtScale`, `HpFloor`, `HudLine` / `HudMarks`.
  - `CastTuning` on the v2 moves.
  - `Hazard.Hidden`; a 0-damage hit is a push only.

## Conflicts with settled rules

1. Overlap, enrage, wipes, one-shots and length are lifted for this fight (Chzy18, 2026-10-23), as recorded in `gpt/08-design-rules.md`.
2. No main-menu change: the Ultimate "run option" is only `-lastbellUltimate`.
3. Easy/Hard scaling doesn't apply: the Ultimate entry runs at Medium.
4. **Length:** the design asked for 10–14 min. At the measured bot DPS the tuned fight is ~5:30–6:45, because the 78 s enrage and five potions cap a longer fight. A longer bar killed perfect and Late parties alike (`clears-par-3`, HP 16 000), so I kept the shorter fight. Chzy18 to decide.

## Open questions

1. **Late (+0.25 s) parties of 4 still win half the time.** To make reaction matter more:
   - shorter coral on the Leaning and Heart Tolls (2.0 s → 1.5 s), or
   - a reaction-gated P5 mechanic.

   Or accept it: a 0.25 s slower party that plays every memory perfectly is still a strong party.
2. **Solo and duo don't clear reliably** (solo 1/4, duo 0/3). Options:
   - a per-N push timer, or
   - a larger solo HP cut (× 0.5), or
   - accept that solo is harder.
3. **Length:** keep ~6 min (current), or raise the enrage and potions for 10+ min?
4. **Should the Ultimate replace the run's final boss** (beyond `-lastbellUltimate`)? Provisionally practice-only until a human clear.
5. **A fair decision-density metric**, and an Aurelion v4 measurement once its branch can be run here.
6. **The rig clip list for the Bellkeeper 2.0 rig** (stand-ins in the build today):
   - Clocks: HourTick.
   - Stopped Hour: CatchClapper, ReleaseClapper.
   - Ledger: WriteLedger, TurnLedger.
   - Backwards Bell: ReverseToll.
   - Two Futures: TraceFuture, HaloVerdict.
   - Conductor's Hand: ConductPhrase.
   - Tolling: CallTheBell, SundialTurn, ForgeBreath, RootBloom.
7. **Audio:** each mechanic has a distinct rhythm in the design, but there is no audio system yet.

## Next

- Chzy18 plays it.
- Then decide on the open questions above: Late-4 separation, solo tuning, length.
- Push the branches once GitHub is back.
