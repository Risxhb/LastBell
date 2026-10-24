# The Tempo Tyrant — Metronome Court miniboss (practice entry)

**Status, October 23, 2026:** playable local practice encounter in the current combined player (`feature/confrontation-menu` working tree, uncommitted). The boss body is a **labelled code-built stand-in**: the final model and rig are still an art gate. The arena is code-built. No final music has been supplied; the metronome ticks are generated in code. All numbers below are tunable first-pass proposals, not approved balance.

Sources: [build brief](../../../docs/metronome-arena/claude-build-prompt.md) (workspace), the greatsword sheet `tempo-tyrant-meshy-greatsword-v2.png` and arena sheet `metronome-court-reference-v1.png` (workspace `docs/metronome-arena/`, plus `reference/metronome/Arena Concept.png`, which is identical to the arena sheet).

## How to play it

Main menu → **Boss Rush** (the existing Test Rush setup) → map **Metronome Court** → encounter **Tempo Tyrant (practice)** → Launch. Local build: `E:\Collab\Play Tempo Tyrant Practice.cmd` (`builds/tempo-tyrant-20261023/LastBell.exe`). The legacy run-scene menu also has a **Tempo Tyrant (practice)** button that loads the arena. The boss is **not in the shuffled run** (`BossEntry.InRun = false`).

- **Run integration point (for later review):** `RunDirector.Bosses`, the `Tempo Tyrant (practice)` entry. Setting `inRun: true` adds it to the shuffle; that also changes `RunOrder` length checks in `RunAutoTest`/`MenuFlowTest`, which assume 4 run bosses.
- Command line: `LastBell.exe -lastbellMap metronome-court`, then Practice.

## Files

| Area | Path |
| --- | --- |
| Encounter | `unity/Assets/LastBell/Run/Encounters/TempoTyrant/TempoTyrantEncounter.cs` |
| Tempo state | `.../TempoTyrant/TempoClock.cs` |
| Stand-in body and poses | `.../TempoTyrant/TempoTyrantRig.cs` |
| Ticks and music adapter | `.../TempoTyrant/TempoTyrantAudio.cs` |
| Tempo / combo HUD | `.../TempoTyrant/TempoTyrantHud.cs` |
| Route feasibility | `.../TempoTyrant/TempoTyrantRoutes.cs` |
| Acceptance test | `.../TempoTyrant/TempoTyrantAutoTest.cs` (`-tempoTyrantTest -lastbellOut <dir>`) |
| Arena (code-built) | `unity/Assets/LastBell/Arenas/MetronomeCourt/MetronomeCourtArena.cs` |
| Arena scenes | `Arenas/MetronomeCourt/MetronomeCourt_Arena.unity` (source), `Run/Maps/Run_MetronomeCourt.unity` (run) |
| Scene builder / test launcher | `Arenas/MetronomeCourt/Editor/MetronomeCourtBuilder.cs` (*Last Bell > Metronome Court*) |
| Menu preview tile | `Menu/Resources/Menu/Maps/metronome-court.png` |

### Shared files touched (small, additive)

- `Hazards.cs`: optional `Hazard.ClipRadius` (telegraph drawn only over the floor). Default 0 = unchanged.
- `BellkeeperFx.cs` + `FX_SH_Telegraph.shader`: `Telegraph.Clip()` and `_ClipRadius/_ClipCentre` (default off).
- `RunMaps.cs`: the new map and `Map.InShowcase` / `RunMaps.Showcased`. The new arena is not a showcase stage, so `MenuShowcase`, `MenuShowcaseBuild` and `MenuShowcaseTest` now use `RunMaps.Showcased` (still the original five).
- `RunDirector.cs`: the practice entry. `RunBuild.cs`: source builder registration.
- `MenuMusic.TrackFor`: the Tyrant's (not yet supplied) track name.
- `MenuFlowTest.cs`: expected track for the fifth encounter; boss music checks for a track that has not been supplied are reported as `GAP` instead of pass or fail.

## Arena: the Metronome Court

- Flat circular floor 13.1 m radius; **player bound 12.5 m** (≈25 m playable). The boss stays inside 8.5 m for charge and rush endpoints, leaving a ≥2 m wall corridor. There is no central obstacle, step, pit or pillar.
- Thin low-contrast brass radial inlays, an inner ring and a clock-dial border (60 ticks).
- **Camera-side perimeter (south half) is low bollards and a 0.4 m parapet**, so architecture never hides a player at the wall from the fixed follow camera. Tall posts with lamps and teal banners stand only on the far side.
- South entry bridge (decorative). Distant gothic towers. Shared cached materials, static batching.
- **Giant metronome** on its own plinth beyond the north edge (frame 10 m). It has a tapered open frame, lower pivot, **one** rotating rod, a sliding weight, an escapement wheel that steps on each tick, and a tempo scale. All are separate transforms. Rod angle = `TempoClock.RodPhase` × 26°, reaching an extreme on every beat. The light is amber in Haste and teal in Normal. In frenzy, cosmetic cracks glow on the frame; no floor debris is added.
- Without a running fight (Ready, menu return) the metronome previews Haste ticking. It deals no damage and freezes while paused.

**Known framing limit:** the shared follow camera (offset 0/13/9.7, FOV 44, about 53° down) sees roughly 12 m ahead of the player. A 10 m metronome beyond the north edge is therefore **not in frame during most of the fight**; only its base is visible when a player is near the north wall. Tempo stays readable through the HUD mini metronome, ticks, boss seam colour and the words HASTE/NORMAL. Making the metronome prominent in gameplay needs a camera decision: a per-map camera override in `RunDirector`, or a sunken/closer metronome. That is a human decision and was not made here.

## Boss: stand-in

About 3.5 m tall clockwork knight: ivory plate, brass trim, dark joints, a narrow closed helmet with a visor slit, a short split dark tabard and a clean backplate. He holds a **two-handed greatsword**: broad straight blade, tapered point, brass crossguard and pommel, a wrapped grip between both hands, and a teal fuller. Both hands sit on one pivot, so the grip never separates. Nothing tempo-related is attached to him or his weapon. Seams are amber in Haste and teal in Normal, on identical geometry.

Procedural poses: idle, walk/pursuit, rush, windup, left slash, right slash, overhead, thrust windup, thrust, ring cast, ground strike, recovery, frenzy and defeat. Strikes hold the windup and whip to the impact pose in the last 5 ticks before the authoritative resolve. These are **not authored animation**.

## Tempo

One `TempoClock` per fight, advanced only on the 60 Hz combat tick. `Time.timeScale` is never touched, so player movement and input stay at normal speed.

- The fight starts in **Haste, 160 BPM**. Normal is **120 BPM**. Nominal passages are 12 s Haste and 16 s Normal.
- When a duration expires, the clock only flags a pending change. At the next complete attack block, the boss holds still for **three accented ticks** and the new tempo commits on the third tick. Nothing is ever retimed mid-attack, and nothing launches across the commit.
- Damage is identical in both tempos. Haste shortens warnings, recoveries and gaps, because those are authored in beats, and raises walk speed from 2.6 to 3.4 m/s.
- Everything is derived from this one clock: the metronome rod and escapement, tick audio, HUD label and mini metronome, boss seam colour, and music rate (1.0 / 0.75).
- Diagnostics appear in the HUD tempo panel: state, age/duration, PENDING, TRANSITION, beat, commits, frenzy/chain, and music status.

**Actual passage durations** (Editor run, block log): Windows player run, one fight (Haste 12 s / Normal 16 s nominal): Haste→Normal after **21.8 s**, Normal→Haste **26.0 s**, then 17.6 / 32.0 / 17.3 / 27.5 / 15.8 / 32.5 s. Each passage runs over its nominal length because the change waits for the current block, the 3 transition ticks, and (in Normal) long blocks such as a 9-beat Severance at 120 BPM. The real cycle is therefore about 18–22 s of Haste and 26–32 s of Normal. If passages should be closer to 12/16 s, shorten the nominal durations rather than cutting attacks.

## Attacks (beats convert at the block's tempo)

| Attack | Geometry | Timing (beats) | Damage |
| --- | --- | --- | --- |
| Threefold Severance 1, left cleave | lane slab x < +2.5 m (boss frame), clipped to the floor; ≈63% of the floor from the centre | opening **9** | 26 |
| Threefold Severance 2, right cleave | x > −2.5 m | +4 | 26 |
| Threefold Severance 3, overhead | front band \|x\| < 3.5 m from his back plate | +4, then 6 recovery | 30 |
| Sundering Wake, charge | 3 m lane to the farthest player, travel ≤15 m clamped to 8.5 m, +2.6 m thrust reach | 6 warning, 0.45 s travel; hit when the blade arrives | 24 |
| Sundering Wake, eruption | 2.2 m lane on the travelled path; separate hazard and ID | 5 warning, resolves once | 16 |
| Hollow Crown, ring | everything outside a 4 m pocket around him | 8 | 22 |
| Hollow Crown, inner | 5.5 m circle under him | +4, then 8 recovery | 22 |
| Closing Rush | announced (text and endpoint outline), **no damage** | 3-beat tell after 1.5 s with the pursuit target beyond 9 m; 8 s cooldown | — |

- **Severance lock:** before the first preview the boss re-centres (within 1.5 m of the arena centre) and faces the nearest player. Position and facing then stay locked; the test measured 0.000 drift. All three strikes preview from the start. The imminent strike is filled with a large boxed number. Upcoming strikes are dashed, unfilled outlines with small bracketed numbers. A spent strike flashes and clears within 0.2 s. Each strike is its own hazard, so one player can take one hit per strike, and three separate strikes can each hit.
- **Pursuit:** the target is the farthest standing player (ties go to the lowest roster index). The current target is kept while it stays beyond 7.5 m (hysteresis). If the target goes down during a rush tell, the rush cancels and the boss reselects at the next boundary. Pursuit walks for at most 1.25 s per boundary, so one distant player cannot drag him indefinitely.
- **Hollow Crown and Wake** start from the centre gate. The Wake block ends only after its eruption, so it never overlaps another attack.

## Tunables (constants in `TempoTyrantEncounter` / `TempoClock`)

| Value | Now | Notes |
| --- | --- | --- |
| `HasteBpm` / `NormalBpm` | 160 / 120 | music rate = ratio (0.75) |
| `HasteTicks` / `NormalTicks` | 720 / 960 (12 s / 16 s) | nominal; actual passages are longer, see below |
| `TransitionBeats` | 3 | accented ticks; commit on the last |
| `SevOpenBeats` / `SevGapBeats` / `SevRecoverBeats` | 9 / 4 / 6 | route checks set the 9 |
| `SevSide` / `SevBand` | 2.5 m / 3.5 m half-widths | ≈63% of the floor per cleave from the centre |
| `CentreGate` | 1.5 m | Severance, Crown and Wake start this close to the centre |
| `WakeWidth` / `FissureWidth` / `WakeReach` | 3 / 2.2 / 2.6 m | `WakeWarnBeats` 6, `FissureWarnBeats` 5, travel 27 ticks |
| `CrownPocket` / `CrownInner` | 4 / 5.5 m | 8 + 4 beats |
| `FightDist` / `RushDist` / hysteresis | 4.5 / 9 / 1.5 m | rush after 90 ticks far, 8 s cooldown, 16 m/s |
| `HasteWalk` / `NormalWalk` | 3.4 / 2.6 m/s | |
| `FrenzyAt` / `FissureLeadBeats` | strictly < 20% / 8 | |
| `MaxHp` | 2600 | shortened-test-HP convention of the other minibosses |
| Damage | 26/26/30, 24, 16, 22/22 | identical in both tempos; Easy/Hard scale via `RunDifficulty` |

## Below 20%: Broken Cadence

Triggers once, the first block boundary after HP is **strictly below** 20%. It plays a 1.5 s non-damaging tell (announcement, stance, metronome cracks and stronger light), and the tempo keeps alternating. **Each Haste passage opens with Sundering Wake, then Threefold Severance.** The Severance setup is recomputed after the charge has ended, and its opening is `max(9 beats, eruption + 8 beats)`, so the fissure is always spent at least 8 beats before strike 1 and never blocks a route between slash safe regions. If frenzy begins during Haste, the chain runs as the next block, queued once. If a pending tempo change comes first, the chain opens the next Haste passage instead. Normal passages use the ordinary rotation.

## Music and ticks

- **Ticks:** generated click clips (higher and sharper in Haste, deeper in Normal, accented for the three transition ticks). Each tick is scheduled on the DSP clock about 0.12 s ahead of the beat, using the clock's live phase, so drift does not accumulate. Pause stops both voices, so nothing is replayed on resume. Defeat, retry and menu return stop them.
- **Music: missing.** No Suno track was supplied, so the status reads `music: MISSING (ticks only)`. During the fight the menu track keeps playing, because `MenuMusic` finds no boss clip.
- Pitch-preserving speed change is not available: raw `AudioSource.pitch` lowers pitch, and Unity's mixer pitch-shifter does not run on WebGL. The adapter therefore expects **two externally prepared, sample-aligned versions** at `Resources/Menu/BossMusic/The Tempo Tyrant.wav` (160 BPM) and `The Tempo Tyrant (120).wav` (the same music at 75% speed, pitch preserved). On each tempo commit it swaps clips at the proportional position. This path is **untested**, because no track exists.
- `TempoTyrantAudio.TrackBpm` and `DownbeatOffsetSeconds` must be measured from the supplied file, including beat grid, loop and encoder delay. Nothing aligns music beats to the clock's phase yet. If the track has baked-in ticks, use a tick-free stem.

## Validation

Unity **6000.6.6f1**. Source: `feature/confrontation-menu` at `67079e2` plus this uncommitted work. Evidence: `E:/Collab/build-evidence/tempo-tyrant-20261023/`.

| Check | Result |
| --- | --- |
| Compile (batch) | clean |
| Windows player build (`MetronomeCourtBuilder.BuildWindowsPlayer`, same scenes/options as Current Player) | **0 errors / 38 warnings** → `builds/tempo-tyrant-20261023/` |
| Tempo Tyrant acceptance, **Windows player** (`player-run3`) | **88 / 88**, zero runtime errors |
| Tempo Tyrant acceptance, Editor Play Mode (`editor-run4`) | 87 / 88 (only the chain route check, which used a wrong timing model, fixed afterwards); `editor-run5` was interrupted by an asset reimport from another session's concurrent edits |
| Existing menu flow, full stage 8 incl. all map × encounter pairs (`menu-flow2`) | **716 passed / 0 failed**, 18 `GAP` (Tyrant music not supplied) |
| Existing run autotest (`run-autotest`) | 229 / 230. The one failure, *Bellkeeper resumes in Last Guardian after the kit checks*, **also fails on the untouched baseline player** (`baseline/run-autotest`, 225 / 226) |
| Existing menu smoke | 44 / 45 on this build. *Return creates one clean choreography* **also fails on the baseline player** (`smoke-baseline2`). A rerun on this build failed two different early steps instead, so the suite looks timing-sensitive under load; the menu flow covers those paths and passed |

What the acceptance run measures (deterministic, from the live hazards):
- Practice entry registered; run order still 4 bosses with the Tyrant excluded; showcase still the original 5 stages; map in build.
- Arena has a separate frame, pivot, rod, weight and escapement and exactly one rod. The metronome previews ticking at Ready without hazards.
- Fight starts in Haste at beat 0. Severance impacts are exactly **90 ticks apart at 160 BPM and 120 at 120 BPM**. Openings are 202 / 270 ticks (9 beats). Tempo stays fixed per block. Position and facing drift during the combo: 0.000.
- Three distinct strikes hit a player standing in all three exactly once each. Geometry: 1 leaves the boss's right safe, 2 mirrors it, 3 leaves the flanks safe. Telegraphs are clipped to the floor.
- No hazard is live during the transition ticks (0 of ~117 frames). Music rate is 0.75 outside Haste.
- Wake: the charge hits once and only when the blade arrives (201 ticks after the start against a 180-tick warning). The eruption is a separate hazard ID and erupts exactly once; it hits a player standing in the wake once. The endpoint stays inside the bound and nothing lingers.
- Crown: ring then inner circle with separate warnings (4 beats apart). A player outside the pocket is hit by the ring only.
- Party rosters of 2, 4 and 8 (simulated players, **not co-op**) always yield a valid standing pursuit target. The rush is announced, deals no damage and stops at fighting distance. A target downed during the tell cancels the rush.
- Frenzy: exactly 20% HP does not trigger it; 19% does, exactly once. Cracks appear. Chains run only in Haste, once per passage (two passages observed). The fissure erupts ≥8 beats before strike 1.
- Pause freezes tempo, tick and hazard ages and silences scheduled ticks. Resume causes no burst (+0 ticks scheduled).
- Defeat cancels pending hazards in the killing tick: no damage afterwards, the body stops colliding and audio stops.
- Three retries each start fresh (Haste, beat 0, no frenzy, full HP, cracks off). **Encounter materials stay at 20, meshes at 22 and scene objects at 672 across attempts.** Menu return leaves no boss or fissure, and the metronome returns to preview.
- **Whole-route feasibility, walking at 6 m/s with no sprint, dash or immunity and a 0.3 s reaction per stage, around the boss body and inside the floor bound:**
  - Severance: 9352 / 9352 sampled starts at both tempos (worst slack 0.21 s in Haste, 1.35 s Normal).
  - Crown: 1169 / 1169 (worst slack 0.91 s / 1.91 s).
  - Frenzy chain: 2264 / 2264 (worst slack 1.28 s).
  - Sampled boss positions are within the 1.5 m centre gate at 8 facings. Starts include wall-adjacent points.
- **Iterations that changed the design:** a 6-beat opening failed ~40% of far starts, so it became 9 beats. A 3 m centre gate left far-corner misses, so it is now 1.5 m. A 4-beat fissure lead failed, so it is now 8. The Wake lane was too short when the boss stood near the wall, so he now re-centres first and the lane includes his 2.6 m thrust reach.

**Found outside this encounter:** each retry leaves 5 more `(Instance)` materials (`sword_blade`, `sword_rune`, `sword_gold`, `sword_grip`, `Material_0`). These come from the player character's presenter, not the boss; the same growth appears with any boss. Reported for the player/character owner.

**Not run:** WebGL build or browser test. The tick audio uses only `AudioSource.PlayScheduled` and `AudioClip.Create`, which WebGL supports; the music adapter avoids mixer effects. A human listening or feel playtest. Real co-op.

Screens (Windows player, `player-run3/`): arena overview, south-approach isometric view and metronome elevation; boss front, back and weapon in both tempos; Severance preview 1-2-3 and strikes 2/3 imminent in both tempos; Wake path/endpoint, travel and fissure warning; Crown ring/pocket and inner; party 2/4/8; rush tell; frenzy tell, cracks and chain; paused; defeat.

## Manual playtest checklist

- [ ] From each side of the floor, can you read and walk the 1-2-3 without sprinting? Does the boxed imminent number make the order obvious?
- [ ] Does standing far away draw pursuit and then an announced rush quickly enough to feel pressured, without feeling cheap?
- [ ] With **audio muted**, can you tell Haste from Normal (words, mini metronome, seam colour, attack speed)?
- [ ] Are the transition ticks clear enough that the slow tempo reads as a damage window?
- [ ] Sundering Wake: is the sidestep readable, and is the delayed eruption distinguishable from the charge?
- [ ] Hollow Crown: in, then out. Is the pocket comfortably large with a party?
- [ ] Frenzy chain: is charge → 1-2-3 fair after a long fight?
- [ ] Once music exists: do ticks sit on the music's beat at both speeds, and does the slowdown sound heavy rather than broken?

## Remaining gates

Final model/rig/animation; final music, its beat-grid measurement and the two-version export; the gameplay-camera framing decision for the metronome; human feel/balance; real co-op (simulated roster only); WebGL run of this encounter (not run); run-slot integration (not done; practice only).
