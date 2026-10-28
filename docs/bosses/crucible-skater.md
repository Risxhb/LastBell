# The Crucible Skater

**Keeper of the Cooling Ring.** A Brass Foundry miniboss: a 2.4 m brass and cobalt crucible bowl full of molten brass, carried on three articulated skate legs, with an ivory mask on its rim. It used to carry metal between the casting stations. It skates, pours and spills: hot brass is the danger, and cooled brass is the only safe ground when the bowl overflows.

- **Status:** design and rig only. No encounter code yet. The rigged model with every clip below is `prototype/arena-3d/models/rigged/minibosses/CrucibleSkater.glb` (kept out of Unity's Resources until the encounter exists).
- **Body:** Chzy18's Meshy model (`Meshy_AI_Celestial_Automaton_B`), rigged in Blender with `prototype/arena-3d/models/blender/crucible_skater_rig.py`.
- **Numbers:** first-pass targets for Medium, out of 100 player HP. Easy and Hard scale boss damage by ×0.6 and ×1.3.

## Rules this fight follows

- **No ability text.** Names only in the F7 overlay.
- **Everything lands on its beat:** 130 BPM above 50%, 155 BPM below.
- **No jumping and no dash required.** Every mechanic is survivable walking at 80% speed.
- **No knockback.**
- **Hot and cool always look different:** hot brass is bright orange with heat shimmer; cooled brass is dull gold with a frosted edge.

## The four mechanics

### 1. Figure of Eight (dodge, then a trail)

- **Tell:** the skates turn out and scrape; a dotted orange path is drawn on the floor ahead of it.
- **The path:** a figure-of-eight or a loop across the arena, shown in full 3 beats before it starts.
- **The skate:** it runs the path at 9 m/s. Anyone it touches takes **50**. Behind it, it leaves a **hot trail** 2.5 m wide.
- **The trail:** anyone standing in the hot trail takes **15** per beat. After 8 beats the trail **cools** into solid brass slabs that are safe to stand on, and they last 20 s.
- **Below 50%:** two loops in a row, the second turned 90°.

### 2. Spillway Chase (bait and kite)

- **Tell:** the bowl tips toward a player and the mask glows; a pouring hiss.
- **Marks:** a dripping-ladle mark goes on one player (two for 4+ players, three for 7+).
- **The chase:** for 6 beats, a pool of hot brass (3 m) lands under each marked player every beat. Each pool is hot for 8 beats, then cools to a slab.
- **What you do:** walk the pools along the edge, in a line, away from the others. Pools dropped in the middle cut the arena up.
- **Damage:** **30** to anyone a pool lands on, other than the marked player, who never stops moving; 15 per beat for standing in a hot pool.

### 3. Overflow (whole floor; the safe spots are what you cooled)

- **Tell:** it skates to the centre, sinks low, and the bowl's rim starts to glow white; a rising boil.
- **The overflow:** after 6 beats, molten brass floods the whole floor: **80 damage** to everyone not standing on a **cooled slab** (from Figure of Eight or Spillway Chase).
- **No slabs, no safety:** if the floor has no cooled slabs (e.g. the trails haven't cooled yet), the rim drops 3 cooling plates (2.5 m) at random spots instead.
- **The design:** this is the payoff for where players dropped the pools and how they dodged the path. The rotation always puts a trail-maker before it.

### 4. Crucible Spin (rotating cleaves)

- **Tell:** the three legs lift and lock; the bowl starts to turn and the skates whistle.
- **The fan:** three 40° cleaves come out from its legs to 9 m, like the blades of a fan, rotating 30° per beat.
- **The spin:** 8 beats. Anyone a cleave passes takes **40** (at most once per cleave per beat).
- **What you do:** walk round it in the gap between two blades, going the same way.
- **Below 50%:** the spin reverses halfway, with 1 beat's warning (the skates scrape the other way).

## Phases, rotation and enrage

- **Above 50%:** Figure of Eight → Crucible Spin → Spillway Chase → Overflow, then repeat.
- **At 50%:** HP holds. The bowl's rim rises and a ring of spouts opens, the beat goes from 130 to 155 BPM, and it skates a double Figure of Eight.
- **Below 50% (combinations):**
  - Spillway Chase during Crucible Spin.
  - Double Figure of Eight.
  - Overflow, then a second Overflow 8 beats later. Only slabs made after the first one count.
  - Crucible Spin with a reverse.
- **Boil-over (soft enrage) at 5:00:** an Overflow every 10 beats, with no free plates.
- **Max HP:** 3200.

## Difficulty

| | Easy | Medium | Hard |
|---|---|---|---|
| Path shown before skating | 4 beats | 3 beats | 2 beats |
| Trail cools after | 6 beats | 8 beats | 10 beats |
| Overflow countdown | 8 beats | 6 beats | 5 beats |
| Spin rate | 20° per beat | 30° per beat | 30° per beat, 4 blades |

## Rig clips

The bowl, the mask and each skate leg are rigid parts pinned to bones.

| Clip | Use |
|---|---|
| Idle, Walk | Walk is a three-legged skate stride |
| SkateCrouch, SkateLoop | Figure of Eight: low and leaning |
| TipPour, PourLoop | Spillway Chase: the bowl tipped toward the mark |
| SinkBoil, BoilLoop | Overflow: sunk low, the bowl trembling |
| SpinStart, SpinLoop, SpinReverse | Crucible Spin |
| PhaseShift, Defeat | |
