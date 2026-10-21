# Crucivar, The Furnace Heart: playable graybox fight and model

The Brass Foundry's area boss, from Codex's four-boss packet (draft PR #46, board #47, packet section 2). This branch, `feature/boss-crucivar`, stacks on `feature/run-loop` and adds the Brass Foundry arena commit from `feature/class-test-arena`. It adds:

- A playable fight with all five attacks and all three phases.
- A rigged 3D model with twelve body clips.
- An autotest that plays the fight in the Brass Foundry.

The numbers are the packet's Steady Ticking values. They are proposals, not balance that has been tuned.

## How to play it

- **In the run scene (key-art dais):** Practice, then **Crucivar** (the fifth practice button).
- **In the Brass Foundry:** **Last Bell > Bosses > Crucivar in the Brass Foundry**. It builds the Area 2 arena if it's missing, adds a RunDirector, and saves `Areas/Area2/Generated/Crucivar_FoundryTest.unity` (git-ignored). Press Play, then Practice and **Crucivar**.
- **Dev keys:** F8 jumps to the next phase (65 % / 30 %). F9 defeats the boss.
- **Autotest:**
  ```
  Unity.exe -projectPath unity -executeMethod LastBell.Run.EditorTools.CrucivarTestTools.PlayModeTest -crucivarTest -lastbellOut <dir>
  ```
  It writes `crucivar-report.txt`, a screenshot per attack, and the frames of a Casting Cycle.

## The fight (`Run/Encounters/CrucivarEncounter.cs`)

| Attack | Geometry | Warn / recover / cooldown (s) | Damage |
|---|---|---|---|
| Diebreaker | r 3 circle ahead of the hammer, locks as it appears | 1.8 / 2.4 (hammer lodged) / 6 | 18 |
| Slag Cast | r 1.4 circle per recipient: tracks 0.6 s, locked 1.4 s | 2.0 / 1.6 / 10 | 10, then a 3 s pool at 4/s |
| Rail Ram | 2.6 m lane, at most 8 m of travel, locks as it appears | 1.8 (+0.6 s travel) / 2.5 / 12 | 16 |
| Pressure Release | two opposite 70° sectors, reach 5 m from the body | 1.7 / 2.0 / 9 | 12 |
| Casting Cycle | three 2 m strips 3.6 m apart, numbered 1-2-3 | 1.8 each, 0.6 s apart / 3.5 / 22 | 14 per strip |

**Phases.** Transitions are queued until the current attack ends. Each plays a 1.5 s transformation that deals no damage.

| Phase | HP | Rotation | What changes |
|---|---|---|---|
| Sealed Furnace | 100–65 % | Diebreaker, Slag, Release, Ram | No Casting Cycle |
| Casting Heat | 65–30 % | Slag, Casting, Ram, Diebreaker, Release | The insulation plates fall off |
| Open Pressure | 30–0 % | Ram, Casting, Release, Diebreaker, Slag | The shoulder fins unfold and the flywheel guard opens. Casting always starts with a Rail Ram to a new spot, so the strips come from a new angle. |

**Rules followed:**
- Cooldowns only stop an attack being chosen again too soon. They never override safe scheduling.
- Slag pools have expired before the Casting strips that follow them resolve, so at most two patterns are ever live.
- Rail Ram endpoints stay within 8 m of the centre. That leaves a 1.8 m corridor behind the body, so nobody gets pinned against the wall.
- Defeat cancels every telegraph and pool in the killing tick.

**Party scaling.** Targeted Slag circles go to one player solo, two for 2–4 players, three for 5–8, rotating through whoever is standing.

**HP.** HP is 2500, the test-build scale-down the Bellkeeper also uses (packet example 6600 at 40 DPS × 0.375).

**Shared code.** The existing systems were extended rather than duplicated:
- `Hazard.BurnPerSecond` turns a hazard into a standing pool. While a player stands in it, the pool damages them through `PlayerController.ReceiveBurn` with no knockback. Overlapping pools never stack, and the pool expires without a resolve hit.
- `BossEncounter.NextPhaseFraction` makes F8 work for bosses whose thresholds aren't 70/35.
- Resolve effects are a partial of `BellkeeperFx` (`VFX/Boss/Crucivar/CrucivarFx.cs`): a brass slam ring, a slag splash, steam sectors and a strip stamp.

## The model (output `Characters/Crucivar/Resources/Characters/Crucivar.glb`)

The game model is the **Meshy sculpt** ("Forge Sentinel", generated from the sheet's front, side and back views in `tools/crucivar/meshy-input/`), on a rigid rig. Two steps build it:
```
blender -b -P tools/crucivar/crucivar_meshy_prep.py -- <Meshy_AI_Forge_Sentinel_..._texture.glb> <work dir>
blender -b <work dir>/crucivar_prep.blend -P tools/crucivar/crucivar_meshy_rig.py -- <work dir> unity/Assets/LastBell/Characters/Crucivar/Resources/Characters/Crucivar.glb [<render dir>]
```

**Prep (`crucivar_meshy_prep.py`):**
- Scales the sculpt to 3.8 m with the feet at 0, centred on the torso.
- Decimates it from 3.5M to 60k triangles.
- Saves the 2K Meshy textures.
- Builds an emissive mask from the molten-orange texels (belly windows, eyes, chimney mouths, crucible metal, joint glows).
- Renders front, side, back and top views on a 0.25 m grid, which is where the joint positions come from.

**Rig (`crucivar_meshy_rig.py`):**
- **Cutting.** Cuts the sculpt into rigid pieces by region, so each face belongs to one bone and the metal never stretches. Arm faces far from their own bone go back to the torso, so nothing flies off with a raised limb.
- **Flywheel.** The sculpted flywheel is cut out as its own `Flywheel` node, so the real wheel spins.
- **Code-driven parts.** The parts `CrucivarRig` drives are added on the sculpt's surface by raycast, in the sculpt's coppery palette:
  - brass frames over the three sculpted belly windows,
  - flank grilles with ember slots,
  - the back exhaust,
  - the pressure gauge,
  - dark insulation tiles,
  - an arched brass cage guard over the flywheel,
  - shoulder fins,
  - the crucible lip.
- **Size.** About 79k triangles, with JPEG textures.

**Shared code.** Both pipelines share `crucivar_common.py` (materials, armature builder, hard-surface helpers) and `crucivar_anim.py` (the clips and preview renders).

**Fallback.** `crucivar_build.py` still builds the earlier scripted hard-surface model. It's a fallback with the same rig, clips and node names.

**Meshy differences from the sheet:** two chimneys instead of one (kept, since it reads well), and a slightly different emblem on the hammer.

**Clips:** Idle, Walk (compression walk with IK-planted feet), Diebreaker (three ratchet notches, then the slam on the resolve tick), HammerLodged, SlagCast, RailRam, ReleaseFrontBack, ReleaseSides, CastingCycle, Vulnerable, PhaseShift and Defeat. Defeat kneels, pours harmlessly into its own crucible and locks solid.

**Parts driven from code.** `CrucivarRig.cs` moves these, so the clips never fight them:
- Shutters, flank vents and the back vent open in the chosen Pressure Release pair. The idle shutters breathe.
- The flywheel spins faster with heat.
- The pressure gauge needle shows the Release windup.
- At Casting Heat the insulation plates tumble off. At Open Pressure the shoulder fins unfold and the flywheel guard swings open.
- The pour column falls from the crucible lip during Slag Cast and Casting Cycle.

### Fixes against the v1 sheet

1. **Vents on all four sides.** The packet's Pressure Release opens front/back OR left/right shutter pairs, but the v1 model only had belly shutters. The model now has:
   - Front: the three belly shutters.
   - Left and right: a flank grille on each side.
   - Back: an exhaust under the flywheel.
2. **The Open Pressure vent crown is only the shoulder fins.** The packet text says shoulder vents. The sheet's all-round spikes would read as a new hazard.
3. **The back access plate is dark iron, not a glowing weak spot.** This follows the packet text.

### Still open

- The rigid cut leaves visible seams at the shoulders and elbows when the arms swing wide. That reads as mechanical joints, but a hand-modelled joint cap would be cleaner.
- No audio yet: pressure, ratchet, forge or cooling sounds.
- No stagger system. Defeat is the only state that cancels a warning.
- Difficulty tiers other than Steady Ticking, and the Midnight Toll "Second Press", aren't implemented.
- The Brass Foundry map in the run (the `RunMaps` work on the run-loop branch) isn't on this branch yet. Practise in the Foundry through the editor menu above.
