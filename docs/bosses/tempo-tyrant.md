# The Tempo Tyrant — Metronome Court miniboss (practice entry)

**Status, October 23, 2026:** playable local practice encounter in the current combined player (`feature/confrontation-menu`). The boss body is a **labelled code-built stand-in**: the final model and rig are still an art gate. The arena is code-built and has been through **three critic → refinement polish passes**: the prompts are in the workspace `docs/metronome-arena/refinement/round-1..3-prompt.md`. The fight's music is **By My Measure**. **Normal** plays it as written at 165 BPM, with the former Haste pace. **Haste** is a savage tier: the song runs at 1.33× (220 BPM), and indicators, windups and combos have much shorter windows. This applies to this fight only. All numbers below are tunable first-pass proposals, not approved balance.

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
| Music adapter (no ticks since October 24, 2026) | `.../TempoTyrant/TempoTyrantAudio.cs` |
| Strike effects (sword arc, rims, streaks) | `.../TempoTyrant/TempoTyrantFx.cs` |
| Music preparation tools | workspace `tools/tempo-tyrant/` (`analyse_tempo.py`, `beat_track.py`, `warp_wsola.py`, `stretch_wsola.py`; numpy only) |
| Music files | `Menu/Resources/Menu/BossMusic/By My Measure.wav` (Normal, 165 BPM grid) and `By My Measure (Haste).wav` (1.33×, 220 BPM grid) |
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
- `MenuMusic.TrackFor`: the fight opens in Haste with "By My Measure (Haste)". The adapter swaps to the song as written (1.0×) in Normal, for this fight only.
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

- The fight starts in **Haste, 220 BPM** (the savage tier). **Normal is 165 BPM**, the song as written, which is the former Haste pace. Nominal passages are 12 s Haste and 16 s Normal.
- When a duration expires, the clock only flags a pending change. At the next complete attack block, the boss holds still for **three accented ticks** and the new tempo commits on the third tick. Nothing is ever retimed mid-attack, and nothing launches across the commit.
- Damage is identical in both tempos.
- **Normal (165 BPM) is the former Haste pace:** fully survivable walking.
- **Haste (220 BPM) is a savage/ultimate-style tier.**
  - Every beat is 25% shorter, and indicators, windups and combos also get fewer beats from placement to activation.
  - The boss walks faster (4.2 vs 3.4 m/s).
  - It needs sprinting and pre-positioning, and the windows are set at the tightest values the route checks still prove survivable without dashes or immunity.

  Placement → activation:

| Indicator | Haste (220 BPM) | Normal (165 BPM) |
| --- | --- | --- |
| Severance opening | 9 beats = 2.45 s | 9 beats = 3.27 s |
| Severance gap | 4 beats = 1.08 s | 4 beats = 1.45 s |
| Wake charge warning | **4** beats = 1.08 s | 5 beats = 1.82 s |
| Fissure eruption warning | **3** beats = 0.82 s | 4 beats = 1.45 s |
| Hollow Crown ring | **6** beats = 1.63 s | 7 beats = 2.55 s |
| Hollow Crown inner | **3** beats = 0.82 s | 4 beats = 1.45 s |
| Closing rush tell (no damage) | 2 beats = 0.55 s | 2 beats = 0.73 s |

  Recoveries are 3/5 beats for Severance, 2/3 for Wake and 4/6 for Crown (Haste/Normal).

  **Where the limits are** (offline port `tools/tempo-tyrant/routes_check.py`, matching the in-game checker):
  - A Haste opening of 8 beats, a gap of 3, or a ring of 5 each leaves some starts with no survivable route, so the values above are the floor.
  - Worst-case Haste Severance needs about 3 s of sprinting, an estimated 75 of 100 stamina.
- The HUD tempo panel shows HASTE/NORMAL, BPM, the current Wake warning and Severance gap in seconds, the shift countdown and a mini metronome. A frenzy badge appears under it. **F7** toggles diagnostics: state, age, PENDING, TRANSITION, beat, commits, frenzy/chain, and music lock and error.

**Actual passage durations:** see Validation. Passages run longer than nominal because a change waits for the current block and the 3 transition ticks.

## Attacks (beats convert at the block's tempo)

| Attack | Geometry | Timing (beats) | Damage |
| --- | --- | --- | --- |
| Threefold Severance 1, left cleave | lane slab x < +2.5 m (boss frame), clipped to the floor; ≈63% of the floor from the centre | opening **9** | 26 |
| Threefold Severance 2, right cleave | x > −2.5 m | +4 | 26 |
| Threefold Severance 3, overhead | **his entire front half**: everything ahead of his centre line (changed October 24, 2026 at Risxhb's request; was a 7 m front band). Only the area behind him is safe | +4, then 3/5 recovery | 30 |
| Sundering Wake, charge | 3 m lane to the farthest player, travel ≤15 m clamped to 8.5 m, +2.6 m thrust reach | 4/5 warning, 0.45 s travel; hit when the blade arrives | 24 |
| Sundering Wake, eruption | 2.2 m lane on the travelled path (yellow, with a jagged crack); separate hazard and ID | 3/4 warning, resolves once | 16 |
| Hollow Crown, ring | everything outside a 4 m pocket around him | 6/7 | 22 |
| Hollow Crown, inner | 5.5 m circle under him | +3/+4, then 4/6 recovery | 22 |
| Closing Rush | announced (text, plus a solid ivory stop ring and pip), **no damage** | 2-beat tell after 1.5 s with the pursuit target beyond 9 m; 8 s cooldown | — |
| Sundering Wake, mirrored eruption (**Haste only**) | the eruption lane reflected across the arena's north-south line (left/right on screen) | dashed preview from the start of the charge warning; goes live and erupts on the same ticks as the real fissure | 16 |
| Spiral Slices, spiral | the floor's 12 wedge slices (30° each, between the brass hour spokes). 11 pulse each step; the one safe slice moves one slice right (clockwise) or left (anticlockwise) per step | whole floor dashed for 4/5 beats; first pulse after 4/5 more; then 9 more pulses 2/2 beats apart | 12 per pulse, no stun or knockback |
| Spiral Slices, every other | 6 alternate slices from the centre, then the other 6 | 4/5, then +3/+4; 2/3 recovery | 18 |

### Harder mechanics added October 24, 2026

Risxhb asked for harder boss mechanics, starting with the Tempo Tyrant. Both come from the Rabbit and Steel research page (workspace `docs/research/rabbit-and-steel-boss-mechanics.html`): *movement check* (`movementcheck`) and *player mirror* (`playermirror`).

**Stop-Time** was removed on October 24, 2026 at Risxhb's request. Its place in the rotation is taken by Spiral Slices (below). The rotation is now Severance, Wake, Crown, Spiral Slices, Severance, Crown, Wake, Spiral Slices.

**Spiral Slices** (added October 24, 2026; Risxhb's design, using the arena's floor slices):
- He walks to the centre. The floor's 12 wedge slices, between the brass hour spokes, are the attack.
- **The whole floor is indicated:** every slice is shown dashed. The announcement says **SPIRAL RIGHT >>>** or **<<< SPIRAL LEFT**.
- **The first slice goes:** the outlines clear, and every slice except one goes live. The safe slice is the one under the player nearest the centre, so the opening is always reachable.
- **The chase:** after each pulse the safe slice moves one slice in the announced direction. Right is clockwise on screen, left is anticlockwise. There are 10 pulses, 2 beats apart in both tempos. Players run round the floor to stay in the gap. The direction alternates on each use.
- Each pulse hits once per player, for 12, with no stun or knockback, so one hit never stalls the chase. Standing on a spoke counts as being in both slices, as with any hazard edge. Near the boss the slices are too thin to stand in, so the chase happens out on the floor.
- **Every other slice:** then, from the centre, 6 alternate slices go live: the set that holds the spiral's last safe slice. The other 6 are shown dashed and follow 3/4 beats later. Each hits for 18.
- **Routes:** chasing slice-centre to slice-centre at 5 m needs 4.75 m/s at 220 BPM (walking is 6 m/s). Reaching the first safe slice from the far rim needs 8.0 m/s at 220 BPM (sprinting is 9 m/s).
- **Validation:** separate player `builds/tempo-slices-20261024/run1/LastBell.exe`, 0 errors / 46 warnings. Acceptance run **133 / 133**, zero runtime errors, about 113 fps at 2560×1440 (evidence in `build-evidence/tempo-slices-20261024/run1/test/`). It ran twice, both times in **Haste**: once spiralling right and once left. A bot chasing the safe slice took no damage. A player standing still was hit once by every pulse that wasn't safe for it (9/9) and once by the every-other phase. The boss has no audio source left. The Normal tempo, human feel and difficulty haven't been checked.

**Mirrored Wake** (Haste only, including the Broken Cadence chain):
- During the charge warning, a dashed outline shows the fissure's mirror image (x → −x) and the HUD announces MIRRORED WAKE.
- When the real fissure goes live, the mirror goes live with it and both erupt on the same tick. Each is its own hazard, so standing where they cross means two hits.
- It is skipped when the reflected lane would point within 20° of the real one (a charge almost straight north or south), because the two would overlap.
- A player standing where the mirror lands has the whole charge warning plus travel to read the preview. The route check proves that the 3-beat fissure warning alone, at walking speed, is enough to clear both lanes.

**Validation (October 24, 2026):** Windows player `builds/tempo-stoptime-20261024/LastBell.exe` (separate folder, built with `MetronomeCourtBuilder.BuildWindowsPlayer`; 0 errors / 46 warnings). Acceptance run: **125 / 125, zero runtime errors**, about 112 fps at 2560×1440. Evidence: `build-evidence/tempo-stoptime-20261024/player2/`.
- **Stop-Time,** run twice: checks land on their own metronome beats (within one tick), after a 4-beat count-in. A bot that follows the score takes no damage; a bot that stands still fails exactly the MOVE checks; a bot that never stops fails exactly the STOP checks; misses cause no stun. Both runs happened in **Haste**, so the Normal score is checked only by its definition, not in play.
- **Mirrored Wake:** the dashed preview appears during the charge warning; the mirror is the exact reflection; a player in the mirror lane is hit once by it and never by the real fissure; both lanes erupt on the same tick. In Normal there is no mirror.
- **Routes:** chain with mirror 2264/2264 (worst slack 0.31 s); Mirrored Wake alone, walking, 10263/10263 (0.18 s; 2 of 24 directions skip the mirror). The earlier Severance and Crown routes are unchanged.
- **Severance strike 3 as a full front half** (later the same day): separate player `builds/tempo-severance-half-20261024/LastBell.exe`, 0 errors / 46 warnings; acceptance **125 / 125**, zero runtime errors (`build-evidence/tempo-severance-half-20261024/player/`). Every Severance route still passes: 220 BPM sprinting 9352/9352 (worst slack 0.25 s), 165 BPM walking 9352/9352 (0.11 s), and the frenzy chain 2264/2264 (0.31 s). The safe route was already to be behind his left flank by strike 3, so the margins didn't shrink.
- **Open:** the dashed mirror preview is faint in captures. Brightening it is a candidate fix. The run used the HUD before the UI polish session's restyle. Human feel and difficulty are not checked.

- **Severance lock:** before the first preview the boss re-centres (within 1.5 m of the arena centre) and faces the nearest player. Position and facing then stay locked; the test measured 0.000 drift. All three strikes preview from the start. **Imminent:** a saturated red-orange fill and a white numeral on an orange box. **Upcoming:** a dashed, unfilled outline and a bracketed numeral on a dark pill. **Spent:** drops at once to a faint ivory, sorts under live warnings and is gone in 0.15 s. On impact a short sword-arc ribbon shows the blade path, without flooding the floor. Each strike is its own hazard, so one player can take one hit per strike, and three separate strikes can each hit.
- **Pursuit:** the target is the farthest standing player (ties go to the lowest roster index). The current target is kept while it stays beyond 7.5 m (hysteresis). If the target goes down during a rush tell, the rush cancels and the boss reselects at the next boundary. Pursuit walks for at most 1.25 s per boundary, so one distant player cannot drag him indefinitely.
- **Hollow Crown and Wake** start from the centre gate. The Wake block ends only after its eruption, so it never overlaps another attack.

## Tunables (constants in `TempoTyrantEncounter` / `TempoClock`)

| Value | Now | Notes |
| --- | --- | --- |
| `HasteBpm` / `NormalBpm` | 220 / 165 | the prepared music grids; music rate 1.33× in Haste |
| `HasteTicks` / `NormalTicks` | 720 / 960 (12 s / 16 s) | nominal; actual passages are longer, see below |
| `TransitionBeats` | 3 | accented ticks; commit on the last |
| `SevOpen` / `SevGap` / `SevRecover` (Haste, Normal) | (9,9) / (4,4) / (3,5) beats | route checks set the floor |
| `SevSide` | 2.5 m half-width | ≈63% of the floor per cleave from the centre. `SevBand` (3.5 m) is no longer used: strike 3 covers the whole front half |
| `CentreGate` | 1.5 m | Severance, Crown and Wake start this close to the centre |
| `WakeWidth` / `FissureWidth` / `WakeReach` | 3 / 2.2 / 2.6 m | `WakeWarn` (4,5), `FissureWarn` (3,4), `WakeRecover` (2,3), travel 27 ticks |
| `CrownPocket` / `CrownInner` | 4 / 5.5 m | `CrownRing` (6,7), `CrownInnerWarn` (3,4), `CrownRecover` (4,6) |
| `RushTell` | (2,2) beats | non-damaging |
| `FightDist` / `RushDist` / hysteresis | 4.5 / 9 / 1.5 m | rush after 90 ticks far, 8 s cooldown, 16 m/s |
| `HasteWalk` / `NormalWalk` | 4.2 / 3.4 m/s | |
| `FrenzyAt` / `FissureLeadBeats` | strictly < 20% / 8 | |
| `MirrorMinAngle` | 20° | skip the mirror when it would nearly coincide with the real fissure |
| `SliceCount` / `SliceHalf` / `SpiralSteps` | 12 / 15° / 10 | slices are on the floor's hour spokes (spoke i at i·30°) |
| `SliceIndicate` / `SliceFirst` / `SliceStep` | (4,5) / (4,5) / (2,2) beats | whole-floor indication, first pulse, then one pulse per step |
| `AltWarn` / `AltGap` / `SliceRecover` | (4,5) / (3,4) / (2,3) beats | every other slice |
| `SpiralDmg` / `AltDmg` | 12 / 18 | spiral pulses: no knockback or stun |
| `MaxHp` | 2600 | shortened-test-HP convention of the other minibosses |
| Damage | 26/26/30, 24, 16, 22/22 | identical in both tempos; Easy/Hard scale via `RunDifficulty` |

## Below 20%: Broken Cadence

Triggers once, the first block boundary after HP is **strictly below** 20%. It plays a 1.5 s non-damaging tell (announcement, stance, metronome cracks and stronger light), and the tempo keeps alternating. **Each Haste passage opens with Sundering Wake, then Threefold Severance.** The Severance setup is recomputed after the charge has ended, and its opening is `max(9 beats, eruption + 8 beats)`, so the fissure is always spent at least 8 beats before strike 1 and never blocks a route between slash safe regions. If frenzy begins during Haste, the chain runs as the next block, queued once. If a pending tempo change comes first, the chain opens the next Haste passage instead. Normal passages use the ordinary rotation.

## Music and ticks

- **Track:** *By My Measure* (supplied October 23, 2026, `audio/New/Boss/`; the original is untouched). The song drifts from about 162 to 168 BPM, so it was prepared offline with numpy-only tools (workspace `tools/tempo-tyrant/`):
  - `beat_track.py` found 521 beats, with no outliers.
  - `warp_wsola.py` used a pitch-preserving, beat-map time warp to put every beat on an exact grid. It produced **two versions with the same beat count:**
    - `By My Measure` at **165 BPM** (Normal, the song as written; first beat 140.3 ms);
    - `By My Measure (Haste)` at **220 BPM** (1.33×, pitch preserved; first beat 105.2 ms).
  - Re-tracking the outputs puts beats within about 4–5 ms of the grid (median).
  - The spectral peaks are unchanged at 79/82/85 Hz, so the pitch is unchanged.
- **Haste speeds the song up only in this fight.** `MenuMusic` starts the Haste version for the countdown. `TempoTyrantAudio` then:
  - beat-locks it to the clock at combat start;
  - on every tempo commit, swaps versions **at the same beat index**;
  - resyncs if the error exceeds 50 ms;
  - pauses and resumes with the game pause.
  - It never uses `AudioSource.pitch` and no mixer effects.
- **No metronome ticks or tempo-change sound** (removed October 24, 2026 at Risxhb's request). The song carries the pulse. The tempo change is still shown: the HUD's SHIFT n / 3, the lantern flash and the metronome.
- **Still open:** human listening, to check whether the 1.33× Haste version sounds driving rather than smeared.

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
- **Severance geometry and hits:** three distinct strikes hit once each; strike 1 leaves the boss's right safe, 2 mirrors it, 3 leaves the flanks safe (before October 24; strike 3 now takes his whole front half); telegraphs are clipped to the floor.
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
