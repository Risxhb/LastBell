# The Tempo Tyrant — Metronome Court miniboss (practice entry)

**Status, October 23, 2026:** playable local practice encounter in the current combined player (`feature/confrontation-menu`). The boss body is a **labelled code-built stand-in**: the final model and rig are still an art gate. The arena is code-built and has been through **three critic → refinement polish passes**: the prompts are in the workspace `docs/metronome-arena/refinement/round-1..3-prompt.md`. The fight's music is **By My Measure**, and Haste speeds it up for this fight only. All numbers below are tunable first-pass proposals, not approved balance.

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
| Strike effects (sword arc, rims, streaks) | `.../TempoTyrant/TempoTyrantFx.cs` |
| Music preparation tools | workspace `tools/tempo-tyrant/` (`analyse_tempo.py`, `beat_track.py`, `warp_wsola.py`, `stretch_wsola.py`; numpy only) |
| Music files | `Menu/Resources/Menu/BossMusic/By My Measure.wav` (165 BPM grid) and `By My Measure (75).wav` (123.75 BPM grid) |
| Tempo / combo HUD | `.../TempoTyrant/TempoTyrantHud.cs` |
| Route feasibility | `.../TempoTyrant/TempoTyrantRoutes.cs` |
| Acceptance test | `.../TempoTyrant/TempoTyrantAutoTest.cs` (`-tempoTyrantTest -lastbellOut <dir>`) |
| Arena (code-built) | `unity/Assets/LastBell/Arenas/MetronomeCourt/MetronomeCourtArena.cs` |
| Arena scenes | `Arenas/MetronomeCourt/MetronomeCourt_Arena.unity` (source), `Run/Maps/Run_MetronomeCourt.unity` (run) |
| Scene builder / test launcher | `Arenas/MetronomeCourt/Editor/MetronomeCourtBuilder.cs` (*Last Bell > Metronome Court*) |
| Menu preview tile | `Menu/Resources/Menu/Maps/metronome-court.png` |

### Shared files touched (small, additive)

- `Hazards.cs`: optional `Hazard.ClipRadius` (telegraph drawn only over the floor). Default 0 = unchanged.
- `BellkeeperFx.cs` + `FX_SH_Telegraph.shader`: `Telegraph.Clip()` and `_ClipRadius/_ClipCentre`, `Telegraph.FlashScale` and `Telegraph.SpentFast`. All are opt-in; the defaults leave every other boss unchanged.
- `RunMaps.cs`: the new map and `Map.InShowcase` / `RunMaps.Showcased`. The new arena is not a showcase stage, so `MenuShowcase`, `MenuShowcaseBuild` and `MenuShowcaseTest` now use `RunMaps.Showcased` (still the original five).
- `RunDirector.cs`: the practice entry. `RunBuild.cs`: source builder registration.
- `MenuMusic.TrackFor`: the Tyrant plays "By My Measure". The adapter swaps to the 0.75× version in Normal, for this fight only.
- `MenuFlowTest.cs`: expected track for the fifth encounter. A boss track that has not been supplied is reported as `GAP` instead of pass or fail; none are missing now.

## Arena: the Metronome Court

- Flat circular floor, 13.1 m radius; **player bound 12.5 m** (about 25 m playable). The boss stays inside 8.5 m for charge and rush endpoints, leaving a wall corridor of at least 2 m. There is no central obstacle, step, pit or pillar.
- **Floor:** paved in three bands of wedge slabs with constant 5 cm joints. One calm grey-blue stone family varies ±4%; the inner band is slightly darker.
- **Floor inlays:** 12 brass hour spokes laid in the joints, a brass inner ring, and a dark iron dial border with 12 brass hour plaques and minute ticks.
- **Tempo line:** an emissive line just inside the dial, amber in Haste and teal in Normal.
- **Camera side (south half):** low chamfered bollards with lanterns and a 0.4 m parapet, so architecture never hides a player at the wall from the fixed follow camera.
- **Far side:** layered posts (plinth, shaft, brass collars, cornice and spire) with iron-caged lanterns and teal banners facing the court.
- **Surroundings:** a south entry bridge with brass rail caps, tapered three-tier gothic towers with lit slit and arch windows in the fog, dark arch-bridge silhouettes, a lower tier under the court, and a deep abyss bowl.
- **Post-processing volume:** ACES tonemapping, bloom, a navy vignette and a slight desaturation.
- **Built for reuse:** shared cached materials, static batching, and procedural sector meshes.
- **Giant metronome** on a stepped plinth beyond the north edge:
  - A 10 m tapered frame with a dark iron back case and brass edge bevels, plus tempo-lit seams down both stiles.
  - **One** brass rod, 10.5 m long; its tip overshoots the apex.
  - A sliding brass weight with a tempo-lit slot, a 2.6 m escapement wheel that steps on each tick, and a pawl that clicks.
  - Lanterns at the base, and step nosings that glow faintly.
  - All of these are separate transforms. The rod angle is `TempoClock.RodPhase` × 26°, so it reaches an extreme on every beat.
  - In frenzy, thin red-orange cracks glow on the case, the seams flicker red and the light flickers. No floor debris is added.
- **The metronome reaches into the gameplay frame.** The shared follow camera (offset 0/13/9.7, FOV 44) can't frame anything north of the floor edge, so the metronome is present in play in two ways:
  - A tempo-tinted spot light behind its apex, plus a **projected rod shadow** that sweeps across the dial on every beat. The shadow is a soft dark quad projected from the light through the rod each frame; it is presentation only.
  - **Rim lanterns whose glow follows the rod's lean**, all flashing together on the accented transition ticks.
- **Tempo changes the world:** over 0.5 s the key light, ambient and colour grade shift slightly warm in Haste and cool in Normal, through a runtime copy of the volume profile.
- Without a running fight (Ready, menu return) the metronome previews Haste ticking. It deals no damage and freezes while paused.

## Boss: stand-in

About 3.5 m tall clockwork knight:
- **Armour:** matte ivory plate with brass trim and dark joints.
- **Helmet:** a narrow box with a forward brow, bevor, visor slit, brass crest and a dark crest plume, so his facing reads from above.
- **Pauldrons and arms:** two layered pauldron plates sloping down; arms made of a dark upper arm, an ivory vambrace and a brass gauntlet.
- **Tabard:** a long pointed front panel with a brass stripe. The back tabard has a small ivory plate, and the backplate stays clean.
- **Weapon:** a **two-handed greatsword**: a broad 0.42 m blade with dark edges, a tapered point, a brass crossguard and pommel, a wrapped grip between both hands, and a teal-lit fuller. Both hands sit on one pivot, so the grip never separates.
- **Nothing tempo-related is attached to him or his weapon.**
- **Seams:** amber in Haste and teal in Normal. In Haste they strobe on every tick and the visor brightens; in Normal they breathe slowly at 0.5 Hz. The geometry is identical in both tempos.

Procedural poses: idle, walk/pursuit, rush, windup, left slash, right slash, overhead, thrust windup, thrust, ring cast, ground strike, recovery, frenzy and defeat. Strikes hold the windup and whip to the impact pose in the last 5 ticks before the authoritative resolve. These are **not authored animation**.

## Tempo

One `TempoClock` per fight, advanced only on the 60 Hz combat tick. `Time.timeScale` is never touched, so player movement and input stay at normal speed.

- The fight starts in **Haste, 165 BPM**. Normal is **123.75 BPM** (0.75×). These are the prepared music grids. Nominal passages are 12 s Haste and 16 s Normal.
- When a duration expires, the clock only flags a pending change. At the next complete attack block, the boss holds still for **three accented ticks** and the new tempo commits on the third tick. Nothing is ever retimed mid-attack, and nothing launches across the commit.
- Damage is identical in both tempos. **Haste casts faster twice over:** every beat is shorter, and most indicators also get fewer beats from placement to activation. Haste also raises walk speed from 2.6 to 3.4 m/s. Placement → activation:

| Indicator | Haste | Normal |
| --- | --- | --- |
| Severance opening | 9 beats = 3.27 s | 9 beats = 4.37 s |
| Severance gap | 4 beats = 1.45 s | 4 beats = 1.93 s |
| Wake charge warning | **5** beats = 1.82 s | 6 beats = 2.92 s |
| Fissure eruption warning | **4** beats = 1.45 s | 5 beats = 2.42 s |
| Hollow Crown ring | **7** beats = 2.55 s | 8 beats = 3.88 s |
| Hollow Crown inner | 4 beats = 1.45 s | 4 beats = 1.93 s |
| Closing rush tell (no damage) | **2** beats = 0.73 s | 3 beats = 1.45 s |

  Recoveries are 5/6 beats for Severance, 3/4 for Wake and 6/8 for Crown (Haste/Normal). The Severance opening and gaps keep the same beat count, because the whole-route checks need them. They are still 25% shorter in seconds.
- Everything is derived from this one clock: the metronome rod and escapement, tick audio, HUD label and mini metronome, boss seam colour, and music rate (1.0 / 0.75).
- The HUD tempo panel shows HASTE/NORMAL, BPM, the current Wake warning and Severance gap in seconds, the shift countdown and a mini metronome. A frenzy badge appears under it. **F7** toggles diagnostics: state, age, PENDING, TRANSITION, beat, commits, frenzy/chain, and music lock and error.

**Actual passage durations:** see Validation. Passages run longer than nominal because a change waits for the current block and the 3 transition ticks.

## Attacks (beats convert at the block's tempo)

| Attack | Geometry | Timing (beats) | Damage |
| --- | --- | --- | --- |
| Threefold Severance 1, left cleave | lane slab x < +2.5 m (boss frame), clipped to the floor; ≈63% of the floor from the centre | opening **9** | 26 |
| Threefold Severance 2, right cleave | x > −2.5 m | +4 | 26 |
| Threefold Severance 3, overhead | front band \|x\| < 3.5 m from his back plate | +4, then 5/6 recovery | 30 |
| Sundering Wake, charge | 3 m lane to the farthest player, travel ≤15 m clamped to 8.5 m, +2.6 m thrust reach | 5/6 warning, 0.45 s travel; hit when the blade arrives | 24 |
| Sundering Wake, eruption | 2.2 m lane on the travelled path (yellow, with a jagged crack); separate hazard and ID | 4/5 warning, resolves once | 16 |
| Hollow Crown, ring | everything outside a 4 m pocket around him | 7/8 | 22 |
| Hollow Crown, inner | 5.5 m circle under him | +4, then 6/8 recovery | 22 |
| Closing Rush | announced (text, plus a solid ivory stop ring and pip), **no damage** | 2/3-beat tell after 1.5 s with the pursuit target beyond 9 m; 8 s cooldown | — |

- **Severance lock:** before the first preview the boss re-centres (within 1.5 m of the arena centre) and faces the nearest player. Position and facing then stay locked; the test measured 0.000 drift. All three strikes preview from the start. **Imminent:** a saturated red-orange fill and a white numeral on an orange box. **Upcoming:** a dashed, unfilled outline and a bracketed numeral on a dark pill. **Spent:** drops at once to a faint ivory, sorts under live warnings and is gone in 0.15 s. On impact a short sword-arc ribbon shows the blade path, without flooding the floor. Each strike is its own hazard, so one player can take one hit per strike, and three separate strikes can each hit.
- **Pursuit:** the target is the farthest standing player (ties go to the lowest roster index). The current target is kept while it stays beyond 7.5 m (hysteresis). If the target goes down during a rush tell, the rush cancels and the boss reselects at the next boundary. Pursuit walks for at most 1.25 s per boundary, so one distant player cannot drag him indefinitely.
- **Hollow Crown and Wake** start from the centre gate. The Wake block ends only after its eruption, so it never overlaps another attack.

## Tunables (constants in `TempoTyrantEncounter` / `TempoClock`)

| Value | Now | Notes |
| --- | --- | --- |
| `HasteBpm` / `NormalBpm` | 165 / 123.75 | the prepared music grids; music rate = ratio (0.75) |
| `HasteTicks` / `NormalTicks` | 720 / 960 (12 s / 16 s) | nominal; actual passages are longer, see below |
| `TransitionBeats` | 3 | accented ticks; commit on the last |
| `SevOpen` / `SevGap` / `SevRecover` (Haste, Normal) | (9,9) / (4,4) / (5,6) beats | route checks set the 9 |
| `SevSide` / `SevBand` | 2.5 m / 3.5 m half-widths | ≈63% of the floor per cleave from the centre |
| `CentreGate` | 1.5 m | Severance, Crown and Wake start this close to the centre |
| `WakeWidth` / `FissureWidth` / `WakeReach` | 3 / 2.2 / 2.6 m | `WakeWarn` (5,6), `FissureWarn` (4,5), `WakeRecover` (3,4), travel 27 ticks |
| `CrownPocket` / `CrownInner` | 4 / 5.5 m | `CrownRing` (7,8), `CrownInnerWarn` (4,4), `CrownRecover` (6,8) |
| `RushTell` | (2,3) beats | non-damaging |
| `FightDist` / `RushDist` / hysteresis | 4.5 / 9 / 1.5 m | rush after 90 ticks far, 8 s cooldown, 16 m/s |
| `HasteWalk` / `NormalWalk` | 3.4 / 2.6 m/s | |
| `FrenzyAt` / `FissureLeadBeats` | strictly < 20% / 8 | |
| `MaxHp` | 2600 | shortened-test-HP convention of the other minibosses |
| Damage | 26/26/30, 24, 16, 22/22 | identical in both tempos; Easy/Hard scale via `RunDifficulty` |

## Below 20%: Broken Cadence

Triggers once, the first block boundary after HP is **strictly below** 20%. It plays a 1.5 s non-damaging tell (announcement, stance, metronome cracks and stronger light), and the tempo keeps alternating. **Each Haste passage opens with Sundering Wake, then Threefold Severance.** The Severance setup is recomputed after the charge has ended, and its opening is `max(9 beats, eruption + 8 beats)`, so the fissure is always spent at least 8 beats before strike 1 and never blocks a route between slash safe regions. If frenzy begins during Haste, the chain runs as the next block, queued once. If a pending tempo change comes first, the chain opens the next Haste passage instead. Normal passages use the ordinary rotation.

## Music and ticks

- **Track:** *By My Measure* (supplied October 23, 2026, `audio/New/Boss/`; the original is untouched). The song drifts from about 162 to 168 BPM, so it was prepared offline with numpy-only tools (workspace `tools/tempo-tyrant/`):
  - `beat_track.py` found 521 beats, with no outliers.
  - `warp_wsola.py` used a pitch-preserving, beat-map time warp (≤1.8% local change) to put every beat on an exact grid. It produced **two sample-aligned versions with the same beat count:**
    - `By My Measure` at **165 BPM** (Haste, 1.0×; first beat 140.3 ms);
    - `By My Measure (75)` at **123.75 BPM** (Normal, 0.75×, pitch preserved; first beat 187.0 ms).
  - Re-tracking the outputs puts beats within about 5 ms of the grid (median; p95 12–18 ms).
  - The spectral peaks match at 79/82/123/164 Hz, so the pitch is unchanged.
- **Haste affects the song only in this fight.** `MenuMusic` starts the Haste version during the countdown, as for any boss. `TempoTyrantAudio` then:
  - beat-locks it to the clock at combat start (a sub-half-beat seek);
  - on every tempo commit, swaps to the other version **at the same beat index**;
  - resyncs if the error exceeds 50 ms;
  - pauses and resumes it with the game pause.
  - A fresh attempt starts on the Haste version.
  - It never uses `AudioSource.pitch` (that lowers pitch), and it uses no mixer effects, which helps WebGL.
  - Other bosses' music is untouched.
- **Ticks:** generated click clips. Each is scheduled on the DSP clock about 0.12 s before the beat from the clock's live phase, so drift does not accumulate.
  - Haste ticks are higher and sharper; Normal ticks are deeper.
  - The three transition ticks are accented.
  - While the song plays, plain ticks are quiet (22%) and accents stay full.
  - Pause stops them and nothing is replayed. Defeat, retry and menu return stop them.
- **Still open:** human listening, to check the warp and stretch artefacts and whether the slowdown sounds heavy rather than broken. The song also has its own baked-in pulse; quiet ticks are layered over it.

## Validation

Unity **6000.6.6f1**. Evidence: `E:/Collab/build-evidence/tempo-tyrant-20261023/`. The final polish build was made with `MetronomeCourtBuilder.BuildWindowsPlayer`, which uses the same scenes and options as Current Player, and outputs to `builds/tempo-tyrant-20261023/`. It finished with **0 errors / 46 warnings** (the same count as the recorded baseline).

| Check | Result |
| --- | --- |
| Tempo Tyrant acceptance, **Windows player, final** (`player-r3c`) | **99 / 99**, zero runtime errors |
| Earlier first-build acceptance (`player-run3`, before polish and music) | 88 / 88 |
| Whole-route feasibility (walking 6 m/s, no sprint/dash/immunity, 0.3 s reaction per stage) | Severance 9352/9352 at 165 BPM (worst slack 0.11 s) and 123.75 BPM (1.21 s); Crown 1169/1169 (0.46 / 1.79 s); frenzy chain 2264/2264 (0.86 s) |
| Existing menu flow, full stage 8 (`menu-flow2`, first build) | 716 / 0, then 18 GAP for missing music (now supplied) |
| Existing menu flow / run autotest after polish (`menu-flow-r3`, `run-autotest-r3`) | **Not valid; rerun needed.** Another game (`WardogsClient`) held about 85% of the GPU during these runs. They failed only timing-based checks (sequence slots, wall-walk distance, Aurelion rear-camping), which these suites drive in real time |
| Performance | **Not measured fairly** for the same reason: 6.6 fps at 2560×1440 under contention. Re-measure on a free GPU. The new costs are one soft-shadowed spot light (2048 additional-light shadow map), about 1,500 static-batched scene objects, two point lights and a 4-vertex dynamic shadow quad |
| Pre-existing (also on the untouched baseline) | run autotest *Bellkeeper resumes in Last Guardian*; menu smoke *return creates one clean choreography* |

What the final acceptance run measures (deterministic, from the live hazards):
- **Registration:** the practice entry exists; the run order is still 4 bosses with the Tyrant excluded; the showcase still has its original 5 stages.
- **Arena:** separate frame, pivot, **one** rod, weight and escapement. The metronome previews ticking at Ready (swing range 51°) with no hazards.
- **Metronome in gameplay:** a rod-shadow spot light with shadows on and 18 rim lanterns. The projected rod shadow sweeps across the dial on every swing, about half of each swing; at the swing extremes it runs past the floor edge like a sweeping beam.
- **World lighting:** warm in Haste and cool in Normal, following the tempo.
- **Music:** By My Measure is paired with its 0.75× version. Haste plays the 165 BPM version beat-locked to the clock (error 0 ms). Normal swaps to the 123.75 BPM version at the same beat and stays locked (error < 50 ms). Pause pauses it, and resume restores the lock.
- **Haste windows:** every indicator is shorter in seconds than in Normal, and four have fewer beats. See the table under Tempo.
- **Severance timing:** at 165 BPM impacts are exactly **87 ticks** apart with a **196-tick** opening; at 123.75 BPM, 116 apart with a 262-tick opening. Tempo stays fixed per block, and position and facing don't drift.
- **Severance geometry and hits:** three distinct strikes hit once each; strike 1 leaves the boss's right safe, 2 mirrors it, 3 leaves the flanks safe; telegraphs are clipped to the floor.
- **Severance number:** after strike 1 resolves, strike 2 is the boxed number.
- **Transitions:** no hazard is live during the transition ticks, and music rate is 0.75 outside Haste.
- **Wake:** the charge hits once, when the blade arrives. The eruption is a separate hazard that erupts once and hits a player in the wake once. The endpoint stays in bounds and nothing lingers.
- **Crown:** ring, then circle, 4 beats apart. A player outside the pocket is hit by the ring only.
- **Pursuit:** party rosters of 2, 4 and 8 (simulated, **not co-op**) always yield a valid pursuit target. The rush is announced, deals no damage and stops at fighting distance. A target downed during the tell cancels it.
- **Frenzy:** exactly 20% HP doesn't trigger it; 19% does, exactly once. Cracks appear. Chains run only in Haste, once per passage, and the fissure erupts at least 8 beats before strike 1.
- **Pause and resume:** pause freezes tempo, tick and hazards and silences ticks and music; resume causes no burst.
- **Defeat:** cancels pending hazards in the killing tick and stops the audio.
- **Retries:** three retries start fresh. **Encounter materials stay at 11, meshes at 24 and scene objects at 1506 across attempts.** Menu return leaves nothing behind, and the metronome returns to preview.

**Actual passage durations** (final run, nominal 12 s Haste / 16 s Normal): 20.7, 22.8, 16.4, 29.6, 14.6, 27.2, 14.6 and 31.5 s. A change waits for the current block plus the 3 transition ticks. To get closer to 12/16 s, shorten the nominal durations rather than cutting attacks.

**Design iterations from the route checks:**
- A 6-beat opening failed about 40% of far starts, and 8 beats still left far-corner misses, so it is now 9.
- A 3 m centre gate became 1.5 m.
- A 4-beat fissure lead became 8.
- The Wake lane now re-centres first and includes the 2.6 m thrust reach.

**Polish passes:** there were three critic → refinement rounds. Agreed issues were written as prompts first (workspace `docs/metronome-arena/refinement/round-1..3-prompt.md`); each prompt also records the items not taken and why. Captures for every round are in `player-r1`, `player-r2b` and `player-r3c`.

**Found outside this encounter:** each retry leaves 5 more `(Instance)` materials from the player character's sword (`sword_blade`, `sword_rune`, `sword_gold`, `sword_grip`, `Material_0`). This is the player presenter, not the boss, and happens with any boss. Reported for that owner.

**Not run:** a WebGL build (the tick and music code avoids mixer effects and uses only scheduled playback and prepared clips), human listening and feel, and real co-op.

## Manual playtest checklist

- [ ] From each side of the floor, can you read and walk the 1-2-3 without sprinting? Does the boxed imminent number make the order obvious?
- [ ] Does standing far away draw pursuit and then an announced rush quickly enough to feel pressured, without feeling cheap?
- [ ] With **audio muted**, can you tell Haste from Normal (words, mini metronome, seam colour, attack speed)?
- [ ] Are the transition ticks clear enough that the slow tempo reads as a damage window?
- [ ] Sundering Wake: is the sidestep readable, and is the delayed eruption distinguishable from the charge?
- [ ] Hollow Crown: in, then out. Is the pocket comfortably large with a party?
- [ ] Frenzy chain: is charge → 1-2-3 fair after a long fight?
- [ ] Music: do Haste → Normal swaps land on the beat, does the 0.75× version sound heavy rather than smeared, and are the quiet ticks welcome or redundant over the song's own pulse?

## Remaining gates

- Final model, rig and animation.
- A human listening pass on the warped/stretched music.
- A performance measurement on a free GPU, and a rerun of the menu-flow and run-autotest suites there.
- Human feel and balance.
- Real co-op (only a simulated roster was tested).
- A WebGL run.
- Run-slot integration (practice only so far).
