# The Briar Metronome

**Pruner of Lost Seasons.** A Verdant Chimes miniboss: a 3.4 m figure of brass roots and wood under a moss mantle with violet flowers, an ivory mask, and a metronome pendulum in its open chest. In its right hand it carries a long curved pruning blade, and in its left an orb on a chain like a metronome weight. It cuts in and out on its own tempo, grows hedges, hurls the orb, and plants seeds of seasons that bloom in shapes.

- **Status:** design and rig only. No encounter code yet. The rigged model with every clip below is `prototype/arena-3d/models/rigged/minibosses/BriarMetronome.glb` (kept out of Unity's Resources until the encounter exists).
- **Body:** Chzy18's Meshy model (`Meshy_AI_Verdant_Clockwork_Sen`), rigged in Blender with `prototype/arena-3d/models/blender/briar_metronome_rig.py`.
- **Numbers:** first-pass targets for Medium, out of 100 player HP. Easy and Hard scale boss damage by ×0.6 and ×1.3.

## Rules this fight follows

- **No ability text.** Names only in the F7 overlay.
- **Everything lands on its beat:** 108 BPM above 50%, 132 BPM below. The pendulum in its chest swings in time.
- **No jumping and no dash required.** Every mechanic is survivable walking at 80% speed.
- **No knockback.**

## The four mechanics

### 1. Pruning Cuts (in and out, in rhythm)

- **Tell:**
  - **In:** the blade is pulled in to its chest and curled inward.
  - **Out:** the blade is swept back wide and low.
  - The floor shows the next shape faintly too, and a dry wooden tick sounds.
- **The cuts:**
  - **In** is a ring from 4 m to the edge: safe close to it.
  - **Out** is a circle within 5 m: safe away from it.
  - **50 damage** each.
- **The chain:** 4 cuts, 3 beats apart, in a shown order (for example In, Out, Out, In). The next cut is always shown while the current one is warning.
- **Below 50%:** 6 cuts, 2 beats apart. A third shape joins: **Across**, a 120° frontal cone to the edge.

### 2. Overgrowth (hedges that box you in)

- **Tell:** it drives the blade into the floor; roots crawl from it to the edge.
- **The hedges:** 4 lines of bramble grow along the shown paths (straight or one bend), 1.5 m wide, growing 3 m per beat.
- **Damage:** being caught as a hedge grows over you does **40**. A grown hedge does 20 per beat to anyone inside it.
- **Lifetime:** the hedges stay for 16 beats, then wither. They block nothing physically, but walking through costs health.
- **Why it matters:** the next mechanic always lands while the hedges stand, so they decide which way you can walk.
- **Below 50%:** 6 hedges, and each has one gap (shown when it starts growing).

### 3. Weight of Seasons (proximity drop)

- **Tell:** the orb is whirled on its chain overhead, faster and faster; a whistling rise.
- **The target:** a marker on the floor under the player **farthest** from it. It locks 2 beats before impact.
- **The impact:** 5 beats after the tell, the orb lands. Damage falls off with distance from the impact: **90** at the centre, 0 beyond 14 m.
- **What you do:** everyone gets far away from the marked spot. The marked player stands at the edge, and the others go to the far side. Solo, walk to the far edge and step away as it locks.
- **Below 50%:** two orbs in a row (it spins and throws again), the second at the farthest player from the first impact.

### 4. Lost Seasons (seed puzzle, small safe spots)

- **Tell:** it shakes its mantle and petals fall; seeds drop onto the floor and glow.
- **The seeds:** 5 seeds, each showing its season:
  - **Summer** (sunburst): bursts in an 8 m circle.
  - **Winter** (snowflake): bursts everywhere from 2 m to 10 m around it. Only its centre is safe.
- **The burst:** 6 beats after the drop, all seeds burst together: **65** per hit, and every seed can hit.
- **Placement:** always 2 summers and 3 winters. The cast is checked so that at least one spot is safe from all five (a winter's centre outside every summer). It is at least 2.5 m across and reachable walking.
- **Below 50%:** 7 seeds, in two waves 3 beats apart. The second wave's spot is reachable from the first wave's.

## Phases, rotation and enrage

- **Above 50%:** Pruning Cuts → Overgrowth → Weight of Seasons → Lost Seasons, then repeat.
- **At 50%:** HP holds. Fern shutters unfold on its chest, the blade turns into a fuller hook, the beat goes from 108 to 132 BPM, and it casts a 6-cut Pruning chain.
- **Below 50% (combinations):**
  - Overgrowth with Pruning Cuts.
  - Weight of Seasons (double).
  - Overgrowth with Lost Seasons: the hedges show where you can't go.
  - Pruning Cuts with Across.
- **No enrage** (content rules §5). Its other parts still need checking against those rules (phase change, Hard mechanic, soaks) before it is built.
- **Max HP:** 3400.

## Difficulty

| | Easy | Medium | Hard |
|---|---|---|---|
| Cut spacing | 4 beats | 3 beats | 2 beats (and the next cut is not shown) |
| Hedge growth | 2 m per beat | 3 m per beat | 4 m per beat |
| Seed burst countdown | 8 beats | 6 beats | 5 beats |
| Seeds (above / below 50%) | 4 / 5 | 5 / 7 | 6 / 8 |

## Rig clips

| Clip | Use |
|---|---|
| Idle, Walk | long slow stride, the chest pendulum always swinging |
| CutIn, CutOut, CutAcross | Pruning Cuts |
| BladePlant, PlantLoop | Overgrowth: the blade driven into the floor |
| OrbWhirl, WhirlLoop, OrbThrow | Weight of Seasons |
| MantleShake | Lost Seasons |
| PhaseShift, Defeat | |
