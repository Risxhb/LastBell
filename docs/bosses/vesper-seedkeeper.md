# The Vesper Seedkeeper

**Custodian of the Last Bloom.** A Verdant Chimes miniboss: a floating lotus vessel of ivory petals with blue inlay and a calm sleeping face, under a gold halo ring. Three seed-lanterns hang from it on vine cords. It keeps the garden's last seeds and plants them at dusk. Its fight is lanterns tied to players, a petal storm where only lantern light is safe, seeds that root wherever players stand, and the halo turning like a lighthouse.

- **Status:** design and rig only. No encounter code yet. The rigged model with every clip below is `prototype/arena-3d/models/rigged/minibosses/VesperSeedkeeper.glb` (kept out of Unity's Resources until the encounter exists).
- **Body:** Chzy18's Meshy model (`Meshy_AI_Lotus_Oracle_Chandeli`), rigged in Blender with `prototype/arena-3d/models/blender/vesper_seedkeeper_rig.py`.
- **Numbers:** first-pass targets for Medium, out of 100 player HP. Easy and Hard scale boss damage by ×0.6 and ×1.3.

## Rules this fight follows

- **No ability text.** Names only in the F7 overlay.
- **Everything lands on its beat:** 84 BPM above 50%, 104 BPM below. Slow and serene, with long countdowns but crowded floors.
- **No jumping and no dash required.** Every mechanic is survivable walking at 80% speed.
- **No knockback.** The lantern tethers never pull.

## The four mechanics

### 1. Lantern Tethers (stretch)

- **Tell:** its eyes open and one lantern per target swings out on its cord.
- **The tethers:** each lantern drifts to a spot 4 m from the vessel and a glowing vine tethers it to a player. Solo: 1 lantern; 2–4 players: 2; 5+: 3.
- **Stretch:** the tether is red while short and turns gold once it is at least **11 m** long.
- **The bloom:** after 6 beats each lantern blooms along its tether:
  - **Gold (stretched):** 25 to the tethered player.
  - **Red:** **70** to the tethered player and 40 to anyone else the vine crosses.
- **What you do:** walk away from your lantern without crossing other players.
- **Below 50%:** the lanterns drift slowly toward their players (0.6 m per beat), so players keep walking.

### 2. Vesper Bloom (whole floor; safe only in lantern light)

- **Tell:** the lotus petals open wide and the halo blazes; a descending chord, and petals start to drift.
- **The light:** all three lanterns drop to the floor around the arena (8–10 m from the centre, about 120° apart). Each one lights a circle 4 m across.
- **Shrinking:** the light circles shrink to 2.4 m over 6 beats.
- **The storm:** then a petal storm hits the whole floor: **75** to anyone outside a lantern's light.
- **Crowding:** each light holds up to 3 players. A 4th dims it, and everyone in it takes 30.
- **Below 50%:** one lantern goes out 2 beats before the storm (it flickers first). Players in it move to another light.

### 3. Seedfall (bait and persistent roots)

- **Tell:** the vessel tips forward and seeds rattle inside it.
- **The seeds:** a seed falls under every player, 3 times, 2 beats apart. Each one roots after 3 beats.
- **Rooting:** a rooting seed hits a 2 m circle for **40** and leaves a thorn knot (2 m), which deals 20 per beat to anyone inside. The knots last until the next Vesper Bloom, then burst into flowers and vanish.
- **What you do:** drop all your seeds close together at the edge, so the middle stays open for later mechanics.
- **Fairness:** a seed never lands inside a lantern light.

### 4. Halo Sweep (rotating beams)

- **Tell:** the halo ring tilts and flares, and the face turns to look along one beam.
- **The beams:** two beams, 2.5 m wide, from the vessel to the edge, on opposite sides. They are shown for 2 beats, then sweep round at 20° per beat for 18 beats (one full half-turn each).
- **Damage:** **50** to anyone a beam crosses.
- **What you do:** walk ahead of a beam, staying between the two, round the arena. Thorn knots from Seedfall make this hard if they were dropped badly.
- **Below 50%:** at beat 9 both beams stop for 1 beat and reverse.

## Phases, rotation and enrage

- **Above 50%:** Seedfall → Halo Sweep → Lantern Tethers → Vesper Bloom, then repeat.
- **At 50%:** HP holds. The lotus opens into six broad petals, the lantern cords lengthen, the beat goes from 84 to 104 BPM, and it casts a Vesper Bloom where one lantern goes out.
- **Below 50% (combinations):**
  - Seedfall during Halo Sweep.
  - Lantern Tethers that drift.
  - Seedfall, then Vesper Bloom (the knots burst into flowers only after the storm).
  - Halo Sweep with a reverse.
- **Nightfall (soft enrage) at 5:00:** a Vesper Bloom every 12 beats, and each one puts out one more lantern, down to one.
- **Max HP:** 3200.

## Difficulty

| | Easy | Medium | Hard |
|---|---|---|---|
| Tether length needed | 9 m | 11 m | 13 m |
| Light circle after shrinking | 3 m | 2.4 m | 2 m |
| Seeds per player | 2 | 3 | 4 |
| Halo sweep speed | 15° per beat | 20° per beat | 25° per beat |

## Rig clips

The vessel floats. The halo ring, the face petals and each lantern on its cord are rigid parts pinned to their own bones.

| Clip | Use |
|---|---|
| Idle, Walk | hovering drift, the lanterns swaying on their cords |
| LanternCast, TetherLoop | Lantern Tethers: lanterns swung out and held |
| BloomOpen, BloomLoop | Vesper Bloom: rises, halo up, lanterns dropped low |
| SeedTip | Seedfall: tips forward, a shake |
| HaloTilt, SweepLoop | Halo Sweep: the halo tilted, turning with the beams |
| PhaseShift, Defeat | |
