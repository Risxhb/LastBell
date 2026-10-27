# Arcanist redesign: animation and spell-effect prompts

Written October 26, 2026 for the kit in [arcanist-redesign.md](arcanist-redesign.md). Each prompt is the build spec for one clip or one effect, and the brief the independent critic grades against. A piece is finished at **8/10 or better**, with at most **3 critique-and-refine rounds**.

## Shared rules (apply to every prompt)

- **Camera:** judge at the gameplay camera (high three-quarter follow view, the player about 1/12 of the screen height) and in one close view. Both must read.
- **Rules stay authoritative:** timings come from `ArcanistKit.cs` ticks (60 Hz). Effects and clips present events; they never decide hits.
- **Construction:** small procedural meshes (rings, ribbons, tubes, shards, glyph strokes) plus camera-facing billboards in bounded counts. The arcane shader (`LastBell/FX/ArcaneFlow`) draws two scrolling noise layers drifting through each other, an erosion threshold with a glowing edge, a rim/fresnel term for shells, and it is driven by a supplied time so a paused game freezes it.
- **Light discipline:** a small bright core, a visible coloured middle layer, sparse detail around it. Any brightest flash ends within 0.3 s. Nothing washes out to plain white: tint lives in the colour, intensity near 1.
- **Floor rule:** player floor effects are thin lines or rings, never filled glowing discs, and draw under boss telegraphs (queue 3040 < 3050).
- **Every effect has a cancel ending** that never shows the success release.
- **Palette** (HDR, additive unless noted):

| Name | Use | Value |
|---|---|---|
| Lilac | Magic Missile body | (1.35, 0.95, 2.2) |
| Hot white | missile tips, armed flashes | (2.4, 2.2, 2.6) |
| Ultraviolet | Disintegrate core sheath glow | (0.75, 0.25, 1.9) |
| Void | Disintegrate dark sheath (alpha-blended, absorbs light) | (0.05, 0.01, 0.10) |
| Ash magenta | Disintegrate ash erosion edge | (2.0, 0.45, 1.5) |
| Rune violet | Rune Sigil strokes | (0.95, 0.55, 2.0) |
| Armed gold | Rune Sigil / Comet armed | (2.4, 1.95, 1.25) |
| Star silver | Astral Orb shots | (2.0, 2.0, 2.3), edge (1.2, 0.7, 2.2) |
| Comet violet | Arcane Comet body and trail | (1.0, 0.55, 2.1) |

## Animation prompts

The rig: 1.8 m robed wizard, Vanguard-like body bones, no finger bones, 16×3 robe chain bones driven by a baked cloth sim, and the `orbit` bone carrying the astrolabe that floats at his right shoulder. Clips are 30 fps, in place (gameplay moves him). Casts play on an upper-body layer while he moves and full-body while he stands still, so every cast's upper body must read on its own and its legs must stand planted.

### A1. MissileTap / MissileTap2 (Magic Missile tap, alternating hands)
8 frames at 30 fps, stretched to 0.13 s. A quick one-hand flick: the casting hand draws back to the cheek in 2 frames (anticipation), snaps forward palm-out to full extension at 35% (release, the missile leaves the palm), the off hand stays low. Chest twists 10° into the throw, head steady on the target. Recover by 55% so taps chain. MissileTap2 is the mirror (left hand). Silhouette from above: an arm-line straight at the cursor.

### A2. MissileCharge (hold, raise then hold the last frame)
12 frames, played over 0.4 s and held. The casting (right) hand rises and draws back beside the shoulder, palm turned up and open toward the astrolabe as if gathering; the off hand comes across the chest, fingers spread. Slight lean back, chin up 5°. The last frame is a stable held pose (the darts hover in the fan behind this shoulder). Breathing-scale motion is fine but no drift.

### A3. MissileVolley (release)
9 frames over 0.15 s. From the charge pose, the right arm sweeps forward and outward in a wide fan (releasing the darts outward), ending palm-forward pointing at the target; off hand pulls back to the hip for counterbalance. Short follow-through; ready to chain by 70%.

### A4. DisintegrateStart (windup, 0.35 s, hold the last frame)
14 frames. Back foot plants (full-body version: the left foot steps back 0.25 m, knees bend 10°). The casting arm thrusts forward to full extension, palm open and vertical; the off hand grips the casting wrist from below. The astrolabe swings from the shoulder to hang 0.4 m in front of the palm (it becomes the lens). Head turns 8° away from the hand, eyes narrowed (chin down slightly).

### A5. DisintegrateHold (loop, 1.0 s cycle)
30 frames, seamless loop. Holds the A4 end pose under strain: a 2–3° tremor in the casting arm at roughly 6 Hz, the robe and sleeves pushed back by force (robe flow toward the rear, sleeves flutter), torso braced. The astrolabe stays in front of the palm and spins. No step, no drift.

### A6. DisintegrateEnd (release, 0.2 s)
8 frames. The casting arm snaps back to the chest, the off hand releases, a small shake-out of the casting hand, the astrolabe drifts back to the shoulder. The broken version (dash or stun) uses no clip: the cast layer blends out in 0.08 s.

### A7. RuneSigil (draw gesture, 0.3 s)
18 frames. The casting hand traces a quick rune in the air in front of the chest: down-stroke, hook, cross-stroke (three readable strokes), then flicks the palm down toward the target spot at 50% (placement). Off hand stays near the belt. Small dip of the shoulders on the flick.

### A8. AstralOrb (release, 0.75 s)
22 frames. The free (left) hand reaches up and across to the astrolabe at his right shoulder, opens beside it (25–55%: charging, rings spin up), then lifts and opens upward at 70–85% (the orb lifts free). Body rises onto the toes slightly at the release. Ends neutral with the left arm lowering.

### A9. ArcaneComet (call, 0.4 s)
24 frames. Both arms sweep up overhead, palms up (0–40%), head tilted up to the sky; then both arms pull down hard and forward to point at the target (45–55%, the mark appears at 50%), knees bend 10° with the pull. Robe lifts on the upswing and drops on the pull.

### A10. BlinkIn (arrival, 0.25 s)
15 frames. Starts folded (half-crouch 0.25 m lower, arms crossed tight over the chest, head down, robe gathered around the legs), unfolds: arms flare out and down to the sides at 40%, chest opens, robe and sleeves flare outward then settle by 100%. Lands on both feet, no step. There is no departure clip: the shared dash has no windup, so the body is hidden on the press and the departure is drawn by the effect (a baked snapshot of his current pose).

## Spell-effect prompts

### E1. Magic Missile
- **Charge (per summoned dart, appears at 0.25 / 0.5 / 0.75 s):** a lilac dart forms in its slot of a 3-slot fan behind the right shoulder (slot positions from `ArcanistKit.MissileSlot`): 10 converging motes pull in over 0.12 s, then the dart (an elongated diamond 0.35 m long, hot-white tip, lilac body) hovers, pointing at the target, bobbing ±3 cm. A thin glyph ring (0.35 m) at the casting hand fills by a third per dart (three arc segments).
- **Tap release:** a tiny glyph flash at the palm (0.12 s), the dart leaves the palm straight.
- **Volley release:** the glyph ring flashes and throws out one thin ring; each dart launches outward along its fan direction then curves onto the boss (homing in flight).
- **Missile in flight:** the dart mesh oriented along velocity, a short two-ribbon trail (0.6 m, fading), 1–2 sparkles shed per frame at most, a gentle sideways weave (±5 cm at 9 Hz). Small and sharp, no ball.
- **Contact:** a small starburst (6 thin rays, 0.5 m, 0.18 s), 3–4 rune shards thrown outward, one thin ring (0.9 m). Charged hits land about 0.05 s apart (tak-tak-tak). Light pulse ≤ 0.12 s.
- **Expiry (no hit):** the dart fizzles: shrinks and erodes in 0.15 s.
- **Cancel:** the hovering darts collapse inward toward the hand and the glyph ring cracks into 3 pieces that fall and fade (0.25 s). No release flash.
- **Acceptance:** at gameplay distance a tap reads as a dart, not a dot; the three charged hits are separately visible; nothing hides boss telegraphs.

### E2. Disintegrate
- **Windup (0.35 s):** the astrolabe-lens: two thin rings (0.32 m, 0.22 m) line up concentrically in front of the palm, facing the aim, spinning in opposite directions; 12 motes pulled into the palm; a small glyph forms in the inner ring.
- **Release:** the ray snaps out over 2 frames; a sharp thin ring kicks out from the lens (0.6 m, 0.15 s).
- **Beam:** a very thin bright core (ultraviolet-white) inside a darker sheath: an absorptive void sheath (alpha-blended) wrapped by a thin ultraviolet glow, plus two thin strands twisting along it. Width grows per damage step (core 0.05/0.07/0.10 m, sheath 0.16/0.22/0.30 m) so others see how far into the channel he is. Noise flows outward along the beam. The lens spins faster each step.
- **Contact:** the signature: ash flakes break off the boss's surface where the beam meets it (10–14 flakes per second, small dark quads with glowing magenta erosion edges, drifting outward and falling), a few hard sparks, and a dark scorch smudge (not a bright flash) at the contact.
- **End:** the beam thins to a thread over 0.15 s and goes out; the last ash keeps drifting.
- **Cancel / broken:** the beam stutters (2 flickers), breaks into 4–5 separate segments that fade in place over 0.2 s, and the lens rings spin apart outward.
- **Acceptance:** reads as an "unmaking" ray (dark sheath, not a laser pointer); step changes are visible at gameplay distance; the beam never washes the boss to white.

### E3. Rune Sigil
- **Drawing (0.6 s):** a 2.5 m-radius rune drawn on the floor at the target spot, stroke by stroke: an outer thin ring traced clockwise (the timer), then 3 inner strokes (a triangle of chords) and 6 small glyph ticks. Strokes are thin rune-violet lines with a bright advancing head, motes rising sparsely from the head.
- **Armed (last 0.1 s):** all strokes light from the centre outward and shift to armed gold.
- **Explosion:** a short column of violet light (radius 1.2 m, 3 m tall, 0.3 s, eroding), 8 rune fragments (stroke pieces) thrown outward and up, one thin floor ring expanding to the rune's edge. Light pulse ≤ 0.2 s.
- **Ending:** burnt rune lines stay as dim violet-grey on the floor and erode away over 0.8 s.
- **Fade (target lost):** the strokes dim and erode in 0.3 s without the explosion.
- **Acceptance:** lines only, never a filled disc; the drawing visibly counts down; the armed shift reads without colour (brightness and the centre-out wave).

### E4. Astral Orb
- **Charge (0.75 s):** the astrolabe rings at the right shoulder spin up and spread apart; 16 motes drawn into the orb; a thin ring tightens around the orb from 0.5 m to 0.18 m (progress).
- **Release:** the rings flash, the orb lifts free to its fighting spot (`ArcanistKit.OrbOffset`) with a small burst (one ring, 8 sparks).
- **Active (8 s):** the orb: a small bright star-silver core (0.12 m), a soft violet shell (0.22 m, rim-lit), two thin rings crossing around it at different tilts, slowly turning. Before each shot (0.12 s) it contracts 15%, then pulses and fires.
- **Shot:** small round silver-white bolt with a violet edge and a tiny trail (0.3 m). Homes.
- **Impact:** a tiny 4-point star burst (0.3 m, 0.12 s), no ring.
- **Last 1.5 s:** the orb flickers (shell intensity dips at an increasing rate) and its rings slow.
- **Ending:** the orb drifts back toward the shoulder and shrinks into the astrolabe (0.3 s).
- **Cancel (dash during charge):** the tightening ring snaps open and fades; motes scatter.
- **Acceptance:** the orb is readable as "his" and "active" at gameplay distance without competing with the missiles; shots and impacts stay small.

### E5. Arcane Comet
- **Mark (2 s):** at the target, a thin violet crosshair ring (radius 4 m) with four short tick marks and a small bullseye ring (radius 1.33 m). Lines only. The outer ring closes in toward the bullseye radius as the comet falls (timer), and the ticks rotate slowly.
- **Descent:** the comet is visible high in the sky from the cast: it starts high and beyond the mark on the far side from the camera, placed so the gameplay camera sees it in the upper part of the screen (about 16 m up and 10 m past the mark, tuned from captures), and falls on a steep straight path to the mark over the full 2 s with ease-in (slow then fast). A violet body (0.6 m, flowing noise shell), a white-hot core, a long tapering trail (6–8 m) and a few embers peeling off.
- **Ground shadow (Risxhb, October 26):** a soft dark shadow on the floor under the falling comet, centred on the mark. It starts wide and faint and tightens and darkens as the comet nears, reaching about the bullseye's size and full darkness on landing, so the landing moment reads from the floor too. It is dark (alpha-blended, absorbs light), not a glowing disc, and it pulses faintly violet at its rim in the last 0.3 s.
- **Impact:** a white-violet core flash (≤ 0.15 s), a shockwave ring racing along the floor to 4.5 m, 10–14 rock/crystal shards thrown outward and up with gravity, a short light pulse (gone by 0.3 s), a small camera shake (bullseye stronger).
- **Aftermath:** a cracked crater decal of 6 radial violet seams that glow then erode away over 1.5 s; a few embers rise.
- **Fade (target lost):** the mark erodes away; the comet burns out in the sky.
- **Acceptance:** the falling comet is the timer and is visible on screen in the gameplay camera before it lands; the mark is never a filled disc; the impact is the biggest effect in the kit but still brief.

### E6. Blink
- **Departure (on the press):** a small glyph ring flashes at his feet (0.6 m, 0.15 s); a snapshot of his current pose (baked skinned mesh) turns into a hollow violet silhouette (rim-lit shell, empty face) that erodes away in 0.15 s; 14 violet motes and 4 angular shards break off and drift.
- **Travel (0.2 s):** the body is hidden; a faint thin violet thread runs from the start toward his current position with 3–4 motes moving along it.
- **Arrival:** motes rush inward to his position (0.1 s) and he is rebuilt from the feet up (a horizontal bright band rising 0 → 1.9 m over 0.12 s), a thin ring at his feet, one soft pulse on the astrolabe, a brief small light.
- **Blocked (stunned mid-blink):** his body flickers twice and snaps back together where he stands; no arrival ring.
- **Acceptance:** the player never loses their character: departure and arrival are unmistakable at gameplay distance, travel is a thin hint, the whole thing is under 0.5 s.
