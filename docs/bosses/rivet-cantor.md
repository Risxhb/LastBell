# The Rivet Cantor

**Tuner of the Great Cast.** A Brass Foundry miniboss: a 3.2 m armoured brass automaton with an ivory mask and a glowing chest dial. His right hand carries a round drum-mallet and his left a two-pronged tuning fork. He tests each new casting by striking it to pitch, so his mechanics are about sound: pitches to match, rivets hammered in sequence, bells to soak and standing waves to step between.

- **Status:** design and rig only. No encounter code yet. The rigged model with every clip below is `prototype/arena-3d/models/rigged/minibosses/RivetCantor.glb` (kept out of Unity's Resources until the encounter exists).
- **Body:** Chzy18's Meshy model (`Meshy_AI_Celestial_Sentinel`), rigged in Blender with `prototype/arena-3d/models/blender/rivet_cantor_rig.py`.
- **Numbers:** first-pass targets for Medium, out of 100 player HP. Easy and Hard scale boss damage by ×0.6 and ×1.3.

## Rules this fight follows

- **No ability text.** Names only in the F7 overlay.
- **Everything lands on his beat:** 120 BPM above 50%, 140 BPM below.
- **No jumping and no dash required.** Every mechanic is survivable walking at 80% speed.
- **No knockback.**
- **Colour, never sound alone:** every pitch is also shown as a colour (high = pale gold, low = deep blue) on his chest dial and on the floor.

## The four mechanics

### 1. Pitch Match (find the right plate)

- **Tell:** the fork is raised and struck on the mallet; the chest dial glows.
- **The plates:** 10 tuning plates (2.4 m circles) light up across the floor, half pale gold (high) and half deep blue (low).
- **The note:** his chest dial shows a colour and holds it for 3 beats, then he sings: anyone **not** on a plate of that colour takes **55**.
- **Three notes per cast,** 4 beats apart. The plates stay; only the note changes, so players move between plates.
- **Below 50%:** 2 notes are shown together (dial then mask, read in that order) and sung 2 beats apart. Players must be on the first colour and then reach the second colour in 2 beats. Matching plates are always within reach of each other.

### 2. Riveting (sequential dodge)

- **Tell:** the mallet is raised over his head; a quick hammer rattle.
- **The lines:** 4 lines of rivet marks (1.6 m circles, 2 m apart) run from him to the edge in an X.
- **The hammering:** the rivets explode in order from his feet outward, one rivet per half-beat, all four lines together. **40 damage** each.
- **What you do:** stand between the lines, or step over a line just behind the explosion.
- **Second set:** once the first set ends, a second X turned 45° fires the same way.
- **Below 50%:** the second set starts from the edge and runs inward, while the first set is still halfway out.

### 3. The Great Cast (soak towers)

- **Tell:** he slams the mallet onto the floor like a drum; a deep bell tone.
- **Bell moulds:** bell-shaped circles (3 m) appear: 1 solo, 2 for up to 4 players, 3 for 5–8.
- **The cast:** 5 beats later each mould must hold at least one player. Soakers take **20** and gain **Ringing** for 12 s.
- **Ringing:** a player who is Ringing can't soak. Their soak doesn't count, and they take 40 instead.
- **Fail:** an empty mould hits everyone for **50**.
- **Per cast:** 2 waves of moulds, 8 beats apart. In co-op, the second wave needs fresh soakers. Solo, the waves are 14 beats apart, so Ringing has worn off.

### 4. Standing Wave (whole floor, rings to step between)

- **Tell:** he plants the fork prongs-down in front of him; a rising hum, and rings of shimmering floor spread from him.
- **The wave:** the floor is divided into rings 2 m wide, from his body to the edge. Alternate rings are lit.
- **Pulses:** every 2 beats the lit rings pulse for **45**, then swap with the dark ones. Standing still means being hit every other pulse.
- **What you do:** step one ring in or out on each pulse.
- **Per cast:** 6 pulses.
- **Below 50%:** the rings drift outward 1 m per pulse. The safe ring moves with them, so players also walk outward.

## Phases, rotation and enrage

- **Above 50%:** Riveting → Pitch Match → The Great Cast → Standing Wave, then repeat.
- **At 50%:** HP holds. The mask cracks open to show gold light, the soundboard plates flare, the beat goes from 120 to 140 BPM, and he casts a full Standing Wave.
- **Below 50% (combinations):**
  - Pitch Match during Standing Wave: the plates are only safe on a dark ring.
  - Riveting.
  - The Great Cast with the moulds placed in rivet lines; the rivets fire after the cast.
  - Pitch Match (two notes).
- **No enrage** (content rules §5). Its other parts still need checking against those rules (phase change, Hard mechanic, soaks) before it is built.
- **Max HP:** 3600.

## Difficulty

| | Easy | Medium | Hard |
|---|---|---|---|
| Pitch Match note warning | 4 beats | 3 beats | 2 beats |
| Rivet pace | 1 per beat | 1 per half-beat | 1 per half-beat, 1.8 m rivets |
| Great Cast countdown | 6 beats | 5 beats | 4 beats |
| Standing Wave ring width | 2.4 m | 2 m | 1.7 m |

## Rig clips

| Clip | Use |
|---|---|
| Idle, Walk | heavy stomp |
| ForkStrike, DialLoop | Pitch Match: the fork struck on the mallet, then held up while the dial glows |
| HammerRaise, HammerLoop | Riveting: the mallet above his head, hammering in rhythm |
| DrumSlam | The Great Cast: the mallet driven onto the floor |
| ForkPlant, ForkLoop | Standing Wave: the fork driven prongs-down, his body humming |
| PhaseShift, Defeat | |
