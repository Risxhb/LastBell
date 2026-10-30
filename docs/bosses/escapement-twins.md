# The Escapement Twins

**The Two Who Agree.** A Brass Foundry miniboss: two 3.8 m clock towers on one tracked plinth, with a sun face on the right tower (his right, -X) and a moon face on the left (+X). Between them sits a large gold escape wheel on a rocker bridge. One boss and one health bar. The two towers are an escapement: they lock and release in turn, tick and tock. Their fight is about polarity (sun or moon), the escape wheel's teeth, pairs that must agree, and a line where everyone shares the hit.

- **Status:** design and rig only. No encounter code yet. The rigged model with every clip below is `prototype/arena-3d/models/rigged/minibosses/EscapementTwins.glb` (kept out of Unity's Resources until the encounter exists).
- **Body:** Chzy18's Meshy model (`Meshy_AI_Celestial_Clockwork_C`), rigged in Blender with `prototype/arena-3d/models/blender/escapement_twins_rig.py`.
- **Numbers:** first-pass targets for Medium, out of 100 player HP. Easy and Hard scale boss damage by ×0.6 and ×1.3.

## Rules this fight follows

- **No ability text.** Names only in the F7 overlay.
- **Everything lands on the tick:** 100 BPM above 50%, 120 BPM below. The escape wheel steps one tooth each beat.
- **No jumping and no dash required.** Every mechanic is survivable walking at 80% speed.
- **No knockback.**
- **Sun is warm gold, moon is pale silver,** on the towers, the floor and the player markers. Shapes back up the colours: rays for the sun, a crescent for the moon.

## The four mechanics

### 1. Day and Night (polarity)

- **Tell:** both towers turn their faces outward; the sun face blazes and the moon face glows.
- **The floor:** the arena is split through the plinth into a gold sun half and a silver moon half.
- **The marks:** every player gets a sun or moon mark over their head. Co-op splits the marks evenly.
- **The ticks:** on each of 4 ticks, 3 beats apart, anyone standing in the half that does **not** match their mark takes **50**.
- **The swaps:** after each tick, every player's mark flips. Players cross to the other half every time.
- **Below 50%:** the dividing line also turns 45° after every tick, so players walk around the plinth as well.

### 2. Escape Wheel (rotating spokes, whole floor)

- **Tell:** the rocker bridge locks and the central wheel spins up with a ratchet.
- **The spokes:** 12 spokes (thin 15° wedges) from the plinth to the edge, alternately gold and silver.
- **Tick and tock:** on tick the gold spokes fire, on tock the silver ones: **45**. Between them, the whole wheel steps one tooth (15°) clockwise.
- **What you do:** stand in a gap (each is 15° wide) and step one tooth clockwise per beat.
- **Per cast:** 8 beats.
- **Below 50%:** the wheel reverses after beat 4, shown by the pawls flipping 1 beat before.

### 3. Vesica (paired soak)

- **Tell:** each tower lowers its face toward the floor; two circles appear, a sun circle and a moon circle.
- **Placement:**
  - Solo and pairs: the circles (4 m) overlap in a lens about 1.5 m wide.
  - With 3+ players they are 10 m apart, with no overlap.
- **The landing:** 5 beats later, each circle must hold at least one player, landing on the same tick.
  - Solo: stand in the lens, which counts for both.
  - Co-op: split. Damage is **60**, shared among everyone in each circle.
- **Fail:** an empty circle means a **Disagreement**: everyone takes **55**, and the next Day and Night is cast at once.
- **Below 50%:** two Vesicas, 4 beats apart, turned 90° from each other.

### 4. Agreement (line stack)

- **Tell:** both faces turn to look at the same player; a gold and silver line joins the plinth and that player.
- **The beam:** 4 beats later a beam 2.5 m wide fires along the line to the arena edge. **90 damage, shared** among everyone in it.
- **Solo:** the beam does 60 to a single player and can't be avoided. Instead, the player drops a **Sync** circle by standing still for 1 beat during the countdown, which halves it.
- **Co-op:** everyone stacks in the line with the target. Anyone who leaves makes it hurt more for those who stayed.
- **Below 50%:** two targets, two beams, at least 60° apart. The group splits into two stacks.

## Phases, rotation and enrage

- **Above 50%:** Escape Wheel → Day and Night → Agreement → Vesica, then repeat.
- **At 50%:** HP holds. The towers tilt apart, the rocker bridge stretches, the beat goes from 100 to 120 BPM, and they cast an Escape Wheel that reverses.
- **Below 50% (combinations):**
  - Day and Night with Escape Wheel: the spokes fire on the ticks.
  - Double Vesica.
  - Agreement into Day and Night: the beam fires and the marks are given out at once.
  - Escape Wheel (reverse).
- **No enrage** (content rules §5). Its other parts still need checking against those rules (phase change, Hard mechanic, soaks) before it is built.
- **Max HP:** 3800 (one health bar for both towers).

## Difficulty

| | Easy | Medium | Hard |
|---|---|---|---|
| Day and Night tick spacing | 4 beats | 3 beats | 2 beats |
| Escape Wheel spokes | 8 | 12 | 12, gaps 12° |
| Vesica lens (solo) | 2.2 m | 1.5 m | 1.1 m |
| Agreement countdown | 5 beats | 4 beats | 3 beats |

## Rig clips

The two towers lean and turn on their own bones; the escape wheel, the rocker arms and the track wheels spin.

| Clip | Use |
|---|---|
| Idle, Walk | Walk rolls the tracks |
| FacesOut, PolarityLoop | Day and Night: faces turned outward, alternate towers bowing on the tick |
| WheelSpin, WheelLoop, WheelReverse | Escape Wheel |
| FacesDown, VesicaLoop | Vesica: both towers bowed toward the floor |
| AgreeAim, AgreeFire | Agreement: both towers turn to one point, then recoil |
| PhaseShift, Defeat | |
