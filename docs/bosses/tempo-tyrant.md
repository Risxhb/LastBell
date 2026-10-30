# The Tempo Tyrant — Metronome Court miniboss (practice entry)

**Status, October 23, 2026:** playable local practice encounter in the current combined player (`feature/confrontation-menu`). The boss uses **Risxhb's Meshy model and greatsword** (28 Oct; see below); the code-built stand-in remains as a fallback. The arena is code-built and has been through **three critic → refinement polish passes**: the prompts are in the workspace `docs/metronome-arena/refinement/round-1..3-prompt.md`. The fight's music is **By My Measure**. **Normal** plays it as written at 165 BPM, with the former Haste pace. **Haste** is a savage tier: the song runs at 1.33× (220 BPM), and indicators, windups and combos have much shorter windows. This applies to this fight only. All numbers below are tunable first-pass proposals, not approved balance.

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

## Boss: Meshy model (October 28, 2026)

Risxhb supplied the Meshy model (`Characters/TempoTyrant/Resources/Characters/TempoTyrant.glb`: ivory plate knight, Mixamo-style rig, 71 bones) and his greatsword, the Azure Oathblade (`TempoTyrantSword.glb`). The model is 1.7 m as authored and scaled to 3.5 m in game; the sword is scaled to 3.3 m. The fight now uses them; the code-built stand-in below is only a fallback if the files are missing.
- **Clips:** only `Walk`, `Run` and `SlowWalkLoop` (renamed from Meshy's Walking / Running / walking_2_inplace; all in place). Walk plays while he pursues and Run during a Closing Rush. Every other pose is his rest stance.
- **Attacks have no clips yet.** The stand-in stays as an invisible pose skeleton: its arms pivot and greatsword still take every attack pose (slashes, overhead, thrust, backswing, ground strike). The Meshy sword is drawn exactly on that greatsword, and both of the model's arms are bent onto the grip with two-bone IK each frame (`TempoTyrantRig.LateUpdate`). So every swing reads, but the legs and torso don't act. Authored attack clips (Meshy's animation library or Mixamo, same rig) would replace this.
- **Not yet:** Haste/Normal seam tint on the model (the stand-in's seams changed colour; the model keeps its own teal), hand wrap on the grip, and a defeat clip (he tips over with the presenter's procedural fall).

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
| Sundering Wake, charge | 3 m lane to the farthest player, travel ≤15 m clamped to 8.5 m, +2.6 m thrust reach. **Haste: 2 charges** (Oct 28): after the backswing he re-targets the farthest player from where he landed and charges again at once, no centre-up or recovery; Normal: 1 charge | 4/5 warning (second charge 3), 0.45 s travel; hit when the blade arrives | 24 |
| Sundering Wake, **backswing** (Oct 28) | half-disc, 6 m radius, behind his landing point, facing back up the charge lane (Strike). Dashed preview at the landing marker from the start of each charge warning | live on arrival, lands **1** beat later in Haste, **2** in Normal | 26, knockback 1.5 m |
| Sundering Wake, eruption | 2.2 m lane on the travelled path (yellow, with a jagged crack); separate hazard and ID. Every charge leaves its own, so Haste lanes can cross | 3/4 warning from arrival, resolves once | 16 |
| Hollow Crown, ring | everything outside a 4 m pocket around him | 6/7 | 22 |
| Hollow Crown, inner | 5.5 m circle under him | +3/+4, then 4/6 recovery | 22 |
| Closing Rush | announced (text, plus a solid ivory stop ring and pip), **no damage** (open question: should the stop ring hit?) | **1-beat** tell after **0.5 s** with the pursuit target beyond **7.5 m**; **24 m/s**; **3 s** cooldown (Oct 28). Fires between blocks, between Haste charges and during the Spiral Slices walk-in | — |
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
- **Hollow Crown and Wake** start from the centre gate (a Haste Wake's second charge starts where the first landed). In ordinary blocks the Wake block ends only after its last eruption, so it never overlaps another attack; in Broken Cadence it does, by design.

## Tunables (constants in `TempoTyrantEncounter` / `TempoClock`)

| Value | Now | Notes |
| --- | --- | --- |
| `HasteBpm` / `NormalBpm` | 220 / 165 | the prepared music grids; music rate 1.33× in Haste |
| `HasteTicks` / `NormalTicks` | 720 / 960 (12 s / 16 s) | nominal; actual passages are longer, see below |
| `TransitionBeats` | 3 | accented ticks; commit on the last |
| `SevOpen` / `SevGap` / `SevRecover` (Haste, Normal) | (9,9) / (4,4) / (3,5) beats | route checks set the floor |
| `SevSide` | 2.5 m half-width | ≈63% of the floor per cleave from the centre. `SevBand` (3.5 m) is no longer used: strike 3 covers the whole front half |
| `CentreGate` | 1.5 m | Severance, Crown and Wake start this close to the centre |
| `WakeWidth` / `FissureWidth` / `WakeReach` | 3 / 2.2 / 2.6 m | `WakeWarn` (4,5), `WakeWarn2` (3,4), `FissureWarn` (3,4), `WakeRecover` (2,3), travel 27 ticks |
| `HasteCharges` / `NormalCharges` | 2 / 1 | charges per Sundering Wake |
| `BackswingRadius` / `BackswingHalf` / `Backswing` | 6 m / 90° / (1,2) beats | `BackswingDmg` 26 |
| `CrownPocket` / `CrownInner` | 4 / 5.5 m | `CrownRing` (6,7), `CrownInnerWarn` (3,4), `CrownRecover` (4,6) |
| `RushTell` | (1,1) beats | non-damaging (was 2,2) |
| `FightDist` / `RushDist` / hysteresis | 4.5 / 7.5 / 1.5 m | `RushSustainTicks` 30 (0.5 s far), `RushCooldownTicks` 3 s, `RushSpeed` 24 m/s (was 9 m, 90 ticks, 8 s, 16 m/s) |
| `HasteWalk` / `NormalWalk` | 4.2 / 3.4 m/s | |
| `FrenzyAt` / `FissureLeadBeats` | strictly < 20% / 8 | Broken Cadence windows: `TempoTyrantEncounter.Cadence` (table below) |
| `MirrorMinAngle` | 20° | skip the mirror when it would nearly coincide with the real fissure |
| `SliceCount` / `SliceHalf` / `SpiralSteps` | 12 / 15° / 10 | slices are on the floor's hour spokes (spoke i at i·30°) |
| `SliceIndicate` / `SliceFirst` / `SliceStep` | (4,5) / (4,5) / (2,2) beats | whole-floor indication, first pulse, then one pulse per step |
| `AltWarn` / `AltGap` / `SliceRecover` | (4,5) / (3,4) / (2,3) beats | every other slice |
| `SpiralDmg` / `AltDmg` | 12 / 18 | spiral pulses: no knockback or stun |
| `MaxHp` | 2600 | shortened-test-HP convention of the other minibosses |
| Damage | 26/26/30, 24, 16, 22/22 | identical in both tempos; Easy/Hard scale via `RunDifficulty` |

## Below 20%: Broken Cadence

Triggers once, the first block boundary after HP is **strictly below** 20%. It plays a 1.5 s non-damaging tell (announcement, stance, metronome cracks and stronger light), and the tempo keeps alternating.

**Rework, October 28, 2026 (Risxhb: "chains mechanics on top of one another, never two slice mechanics at once, much faster").** The old chain (each Haste passage opening with Wake then Severance) is replaced by an **overlap scheduler** (`CadenceBlock`), in **both tempos**:
- **Every block is a chain of 3 attacks.** The first comes from the ordinary rotation. Each next one is picked in turn from the pairings allowed after the previous attack (`Cadence.Pairs`) and **starts while the previous attack's last hit is still on the floor** (2–4 beats before it lands), not after its recovery. Only the chain's last attack recovers.
- **Pairings** (first → overlapping second, and what is still pending when the second starts):

| Pairing | Pending from the first | Second attack's first warning (Haste/Normal beats) |
| --- | --- | --- |
| Hollow Crown → Sundering Wake | inner circle | charge 1: 3/3 |
| Sundering Wake → Threefold Severance | last fissure (+ mirror) | opening 6/6, and never less than 5 beats after the eruption |
| Hollow Crown → Spiral Slices | inner circle | indicate 4/3 |
| Spiral Slices → Hollow Crown | second every-other set | ring 4/5 |
| Threefold Severance → Spiral Slices | strike 3 | indicate 3/3; the first safe slice is the one **behind him** (safe from strike 3) |
| Spiral Slices → Threefold Severance | second every-other set | opening 6/6 |

- **Never two slice attacks at once:** Spiral Slices and its every-other part count as one slice attack. There is no Slices → Slices pairing, and a Slices attack waits until no other slice is live.
- **He never moves mid-Severance:** Severance is paired only with hazards already on the floor (a pending fissure, or slices). While a strike is pending, the centre-up and any rush wait.
- **Faster:** every warning starts at about 0.65× its ordinary value and is raised back to the route-check floor (`routes_check.py floors`). In Broken Cadence the first safe slice is revealed while the floor is indicated, so an overlap is readable from the start.

| Window (beats, Haste/Normal) | Ordinary | Broken Cadence | Note |
| --- | --- | --- | --- |
| Severance opening / gap | 9/9 / 4/4 | 9/9 / 4/4 | a lone Severance can't go faster: 6-beat opening and 3-beat gap fail far starts. Overlapping openings are 6 (table above) |
| Wake warning / second charge | 4/5 / 3/4 | 3/3 / 2/3 | |
| Backswing | 1/2 | 1/1.5 | |
| Fissure warning | 3/4 | 2/3 | |
| Crown ring / inner | 6/7 / 3/4 | 5/6 / 3/3 | 4 beats can't reach the pocket from the rim; a 2-beat inner can't leave it |
| Spiral opening (indicate + first) | 8/10 | 8/9 | a co-op safe slice can be across the floor |
| Spiral step | 2/2 | 1.5/1.5 | 1 beat passes only hugging the boss; 1.5 keeps the 5 m chase |
| Every other 1 / 2 | 4/5 / 3/4 | 3/3 / 2/3 | |

- **Routes:** every pairing, and every attack alone (it can open a chain), passes at 220 BPM sprinting with finite stamina and at 165 BPM walking, in both the in-game check and the full offline sweep. See Validation.

## Below 10%: Falling Hours

Added October 28, 2026 at Risxhb's request. This is a **deliberate soft enrage**, allowed for this boss only (`content-rules.md`, Tempo Tyrant exception). Code: `TempoTyrantEncounter.Orbs.cs`.
- **Trigger:** once, at the first block boundary strictly below 10% HP. A 1 s non-damaging tell ("THE HOURS FALL") plays, then the orbs run alongside every attack, in both tempos, until he dies.
- **Drops:** glowing orbs fall from the sky into a 1.5 m landing circle (Ground). The warning is 5 beats in Haste and 4 in Normal (1.36 / 1.45 s), and a drop deals 18. Every 3rd drop lands on a standing player (in turn); the rest land anywhere on the floor (seeded, same every attempt).
- **Density:** 0.4 drops/s at the start, +0.02/s every second (one more drop per second every 50 s), capped at 3/s (reached after 130 s).
- **Bouncing orbs:** each landing orb spews **3** orbs, 120° apart at a random angle. Each is 0.6 m (Strike), rolls at 6 m/s (walking speed) and deals 10. It can't be jumped (Risxhb): touching one hits. It bounces off the arena wall **3** times and dissipates on its fourth wall contact. Each orb hits a player at most once.
- **Escape:** a walker leaves a landing circle in 0.6 s of a 1.36 s warning; a bouncing orb is no faster than walking, so players step out of its path. Survival gets harder without limit as density rises; that is the enrage.
- **Defeat** clears every orb in the killing tick. The orbs share one cached material and mesh, so nothing accumulates across attempts.
- **Open:** damage, density ramp and cap are first-pass numbers; Risxhb to tune by feel. Bouncing orbs have no direction preview beyond the orb itself.

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

### October 28, 2026: ultimate rework (plan sections 0–3)

Separate player `builds/tempo-ultimate-20261028/r1/LastBell.exe` (`tools/tempo-tyrant/build-and-test.sh`), **0 errors / 50 warnings**. Acceptance run **170 / 170, zero runtime errors**, 177 fps at 2560×1440 with the 240 FPS cap. Evidence: `build-evidence/tempo-ultimate-20261028/r1/test/`.
- **Backswing:** dashed preview from the start of the warning; live on arrival; lands 2 beats later in Normal (44 ticks); hits a player 3 m behind his landing point once and misses one in front.
- **Haste second charge:** re-targets the farthest player (yaw 225° = bearing to that player), starts exactly 108 ticks after charge 1 (warning + travel + backswing); each charge leaves its own fissure.
- **Closing Rush:** 1-beat tell (16 ticks), 8.7 m in 22 ticks = 23.8 m/s, stops at 4.5 m; 14 rushes, every gap ≥ 3 s; fired from pursuit and the Slices walk-in.
- **Broken Cadence:** all 6 pairings ran in 61 s, in both tempos, each with the previous attack's hazards still pending (1–6). Never more than one slice attack live; 0 ticks off his planted spot while a Severance strike was pending; pending fissures erupted 149–183 ticks before strike 1.
- **Routes (in game, sampled):** Wake rework, the 4 attacks alone and the 6 pairings, each in Haste (sprint + finite stamina) and Normal (walking): all pass, worst slack 0.00 s (Crown → Slices, Haste) to 1.16 s, in 22 s. Ordinary Severance/Crown/Mirrored Wake routes unchanged. Offline full sweep `python tools/tempo-tyrant/routes_check.py ultimate`: **22 / 22**.
- **Route model notes:** the Haste stamina model starts each sequence with full stamina; a player arriving drained has less. Pairings are proven pairwise (whole first attack + overlapping second), not as whole 3-attack chains. In co-op the spiral's safe slice belongs to the player nearest the centre, so a far player must cross the floor (pre-existing, sampled as every slice).
- **Frame pacing (`docs/frame-pacing.md`):** the new code is tick-only (no frame-time code). The model is drawn up to one tick (≤0.55 m at charge speed) behind the logic position at arrival; the backswing lands 16+ ticks later, so the swing lines up. A rush is 0.4 m per tick, below the 2 m snap. The suite also passes **170/170 at a 60 FPS cap** (58.7 fps) **and with VSync** (145.8 fps), using the test-only `-tempoTyrantFps <n>` / `-tempoTyrantVsync` override (never saved). Menu flow (stage 8 quick) 155/155. `-lastbellPractice tempo-tyrant` reaches combat with no exceptions.
- **Not checked:** human feel and difficulty, real co-op, the placeholder backswing pose's readability.

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
