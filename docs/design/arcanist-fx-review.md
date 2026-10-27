# Arcanist redesign: animation and spell-effect review

October 26, 2026. The kit is in [arcanist-redesign.md](arcanist-redesign.md); the build specs the critics graded against are in [arcanist-fx-prompts.md](arcanist-fx-prompts.md). Each round, every clip or effect was scored 0–10 by a fresh, independent critic agent from real captures. The target was 8/10, with at most 3 critique-and-refine rounds.

**Result:** all 11 animation clips reached 8/10 by round 3. No spell effect reached 8/10 within 3 rounds; the final scores are 6.4–7.3.

Evidence (all under `F:\Last Bell\Collab\build-evidence\arcanist-20261026\`):
- Animation sheets and GIFs: `anim\round-1\` to `round-3\`.
- Effect captures in game (Skyward Spire against Aurelion, 30 fps, gameplay camera plus a closer review camera): `r1b\` (round 1), `r2\` (round 2), `r3b\` (round 3). Each `showcase\` folder has `<sequence>_game.png`, `_close.png` and GIFs.
- Capture mode: `LastBell.exe -lastbellPractice aurelion -lastbellClass Arcanist -arcanistShowcase <dir>` (`Run/ArcanistShowcase.cs`). Sheets: `tools/arcanist/contact_sheets.py`.

## Scores

| Effect | Round 1 | Round 2 | Round 3 |
|---|---|---|---|
| E1 Magic Missile | 6.0 | 6.0 | 6.9 |
| E2 Disintegrate | 5.5 | 6.4 | 6.4 |
| E3 Rune Sigil | 6.0 | 6.5 | 6.8 |
| E4 Astral Orb | 6.0 | 7.2 | **7.3** |
| E5 Arcane Comet | 5.0 | 5.5 | 6.8 |
| E6 Blink | 6.5 | 6.4 | 6.8 |

| Clip | Round 1 | Round 2 | Round 3 |
|---|---|---|---|
| MissileTap / MissileTap2 | 7 / 7 | 8 / 8 | 8 / 8 |
| MissileCharge | 7 | 8 | 8 |
| MissileVolley | 6 | 7 | 8 |
| DisintegrateStart | 6 | 8 | 8 |
| DisintegrateHold | 7 | 7 | 8 |
| DisintegrateEnd | 6 | 7 | 8 |
| RuneSigil | 6 | 8 | 8 |
| AstralOrb | 6 | 6 | 8 |
| ArcaneComet | 6 | 7 | 8 |
| BlinkIn | 8 | 8 | 8 |

## Spell effects: pros and cons (round 3)

### E1 Magic Missile: 6.9
**Pros**
- A flying missile reads as a sharp, pointed dart at gameplay distance: white tip, lilac body, short trail.
- The three hovering darts can be counted from the gameplay camera, so charge progress is clear.
- The volley fans out, then curves in on the boss. Critics called it the best part.
- Nothing washes out to white or touches the floor.

**Cons**
- Two charged hits still land on the same frame. The launches are 0.05 s apart, but different flight paths cancel the gap.
- The impact reads like a targeting reticle: about 2 m across against the 0.9 m spec, with no bright core and shards too small to see.
- The glyph ring at the hand is too big (about 0.65 m) and doesn't visibly fill by thirds.
- A cancel caused by Blink plays under the Blink effect, so it can't be read.

### E2 Disintegrate: 6.4
**Pros**
- Every stage exists. The broken ending reads best: it stutters twice, breaks into 5 pieces and the lens rings spin away.
- The lens rings in front of the palm sell "the astrolabe becomes a lens", even at gameplay distance.
- The normal ending thins to a thread, and ash keeps drifting afterwards.
- The beam visibly widens with each damage step.

**Cons**
- **Bug:** the bright core draws as flat grey. The dark sheath is alpha-blended in the same transparent queue and sometimes draws on top of it. Fix: put the sheath material in an earlier render queue.
- Because of that bug, at gameplay distance it reads as a violet rail rather than a dark "unmaking" ray.
- Ash flakes clump in front of the boss instead of peeling off and falling.
- The strands don't visibly twist, and the release ring is weak.

### E3 Rune Sigil: 6.8
**Pros**
- Lines only, never a filled disc. The ring, triangle and ticks read at a glance in the gameplay view.
- The violet stands out from the arena's gold lines.
- The armed state lights up from the centre outward, reading by brightness and timing, not just colour.
- Tight timing that cleans up fast.

**Cons**
- The faint full "ghost" ring meant to show time left makes the ring look finished early, so the countdown reads poorly.
- Armed reads as near-white, not gold.
- The column still looks flat. Fragments and the expanding floor ring are hard to see.
- Burnt lines last about 0.45 s instead of 0.8 s.

### E4 Astral Orb: 7.3 (best)
**Pros**
- At gameplay distance it's an unmistakable violet-ringed point above his shoulder, clearly his, never on the floor.
- The charge's progress ring tightens visibly, tied to the real astrolabe rings spreading.
- Shots and impacts are suitably restrained.
- The ending drifts back into the astrolabe and reads clearly.
- The last-1.5 s warning blinks are now visible.

**Cons**
- Up close the core still blows out to white, so the orb looks like a pearl or bubble.
- During the charge, several violet rings of similar weight clutter the astrolabe.
- There's no readable "about to fire" squeeze before each shot.
- The impact looks like a hit-marker X.
- A cancel by Blink is buried under the Blink effect.

### E5 Arcane Comet: 6.8
**Pros**
- The mark works as a timer: the closing outer ring and turning ticks count down, in thin lines.
- The comet is now on screen for the whole 2 s, with a bright core, a halo and a tapering tail.
- Shadow, bullseye, comet and flash all meet on the same frame.
- The aftermath (seams, dark crater, rising embers) is clean.

**Cons**
- From the gameplay camera the fall reads as a slow sideways drift, not a fall. The start point lies almost along the camera's line of sight.
- The impact isn't yet the biggest effect in the kit: the flash is partly hidden behind the boss and the shards are small.
- The shockwave is a heavy, pale pink.
- The seams fade faster than the 1.5 s spec.

### E6 Blink: 6.8
**Pros**
- The departure ghost is clearly his silhouette, hollow and rim-lit, and reads at gameplay distance.
- Floor rings are thin lines and don't cover boss warnings.
- You can always tell where he left from and where he went.
- The whole effect stays under 0.5 s, and the stunned case reads as a distinct beat.

**Cons**
- On arrival his real body shows for one frame, hides behind the rebuild shell, then shows again. The rebuild starts a frame late.
- The travel line reads as a bright moving streak rather than a faint thread.
- The astrolabe pulse floats in mid-air while he's hidden.
- In the stunned case some departure debris still plays.
- Shards lean pink rather than violet.

## Animations: pros and cons (all 8/10)

All clips were built on the existing rig and merged into `Arcanist.glb`, leaving the 12 original clips and the mesh byte-identical. Casts play on an upper-body layer while he moves and full-body while he stands. All clips start at t = 0, unlike the original 12, which start at 1/30 s.

| Clip | Pros | Cons |
|---|---|---|
| MissileTap / 2 | Quick flick reaching full extension at the release mark; alternating hands chain. | At the recover mark the hand is still about 20 cm forward of rest. |
| MissileCharge | Gathering pose that holds steadily under the hovering darts. | The off hand stops at the chest instead of crossing; the lean back is subtle. |
| MissileVolley | Wide fan sweep that matches the darts leaving. | The sweep ends about 15° left of the aim. |
| DisintegrateStart | Planted stance, wrist grip, astrolabe swings to the palm as the lens. | The astrolabe jumps about 0.35 m in one frame mid-clip; the grip sits a bit behind the wrist. |
| DisintegrateHold | Seamless loop, strain tremor, robe pushed back. | The lens bobs with the trembling palm and tumbles as a whole; robe flutter is small. |
| DisintegrateEnd | Clean recoil back to rest. | The shake-out reads as a fist near the hip. |
| RuneSigil | Three readable strokes, then a flick down to place the rune. | The strokes are small (about 25 cm) from the gameplay camera. |
| AstralOrb | The reach to the astrolabe and the upward release line up with the effect. | His arms can't reach the right shoulder, so the astrolabe comes forward; the charge is fairly static and covers his face briefly from the front. |
| ArcaneComet | Big up-then-down pull; reads at distance. | After the pull the astrolabe sits low near the right hand. |
| BlinkIn | Folded-to-unfold arrival with the robe flaring out. | The robe fans out slightly in the first frames. |

Rig limits: there are no finger or sleeve bones, so "fingers spread" and "sleeve flutter" can only be suggested by wrist angle and the robe sim.

## What changed outside the effects

- **Shared input (all classes):** basic attack on left mouse only, dash on right mouse only, the fifth slot on 5.
- **Shared projectiles:** optional homing, plus custom-visual hooks (`OnMove`, `OnEnd`). Defaults are unchanged.
- **Boss-hit flash:** `BossEncounter.TakeHit`, `CombatWorld.DamageBoss` and `LineBoss` take an optional `hitFx` flag (default on). Abilities that draw their own impact skip the shared flash.
- **Character presenter:** opt-in upper-body cast layers (the Arcanist only), `SetHidden`, and fallbacks from the new clip names to the old clips.
- **New shader** `LastBell/FX/ArcaneFlow`: two flowing noise layers, erosion with a glowing edge, a rim term, a world-height clip, and 9 shape modes.
- **Bug fixed during testing:** Rune Sigil and Arcane Comet marks were pushed out of the boss's body by the player-placement clamp. Marks now clamp only to the arena floor.

## Fixes after the review (October 26, no further critic round)

At Risxhb's request, three clear bugs were fixed after round 3 and checked in fresh captures (`r4\showcase\`), without another critique:
- **Disintegrate grey core:** the alpha-blended dark layers now use render queue 3058, before the additive light (3060), so the bright core always draws on top.
- **Missile impact size:** the ring is now 0.9 m across (was 1.8 m), with six 0.3 m rays starting from a small centre gap.
- **Blink arrival pop:** the body is hidden on the arrival tick itself, so it is never drawn before the rebuild; the astrolabe pulse waits until the band passes his shoulder.
