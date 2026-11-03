# The Vesper Seedkeeper (v2, proposal)

**Status:** proposal, practice only (`inRun: false`), placeholder visuals. Replaces the first design (still in git history,
commit `ca0f3a7`). Sources read: `docs/design/content-rules.md` (this file follows it), Aurelion v4 (`Aurelion/Abilities/*`),
Crucivar v4 (`Crucivar/Abilities/*`), Sylvara v3 (`SylvaraEncounter.cs`), and the Pendulum Sentinel / Chimebound Oracle
encounters it is built like. Code: `unity/Assets/LastBell/Run/Encounters/VesperSeedkeeper/`.

## Identity

The Verdant Chimes' evening keeper. A floating dusk lantern-vessel (about 3.2 m tall, hovering 1 m off the floor,
roughly twice the height of a 1.8 m hero): an ivory bowl with a gold core of light, a ring of six petals and a halo above it.
It keeps the garden's seeds of light and, at the vesper hour, pulls the dark down over the floor. Its fight is about light
and dark: cords that tie the vessel to the ground until players snap them, seeds it hands to players to carry and plant
together, and the nightfall that only those planted seeds keep out.

**How it relates to Sylvara** (same arena): she is the day choir, a rooted bullet-hell with petals and colours and no
pause. The Seedkeeper is her dusk: no bullets at all, slow raid telegraphs, long co-op jobs, and a real punish window
every loop. Seeds and blooms are the shared motif; where her seed pods burst into petals, its seeds become the only light.

**Placeholder look (this build):** primitives only, labelled `(STAND-IN, labelled placeholder)`. The Meshy rig
(`prototype/arena-3d/models/rigged/minibosses/VesperSeedkeeper.glb`) is not used yet. No final FX or audio: every warning
is a shared raid telegraph (`HazardSystem.RaidStyle`, Telegraph-Style v2), friendly lights use the thin friendly rim.

## The four abilities

Numbers are **Medium**, out of 100 player HP. Easy and Hard also scale damage taken (×0.6 / ×1.3, shared rule).
"N" is the number of standing players (bots included), 1 to 4.

| # | Ability | One line |
|---|---|---|
| 1 | **Vesper Cords** | N cords tie the vessel to stakes near the edge; stand on a stake when it snaps, or the dusk surges through the party. |
| 2 | **Seedgift** | the vessel hands glowing seeds to the farthest players; a partner (or the whole party) stacks under the carrier to plant it. |
| 3 | **Nightfall** | the floor goes dark; only the planted seeds' blooms are lit, each holding as many players as planted it. |
| 4 | **Dusk Tide** | its light ebbs then floods: get close, then get out; then it closes its petals (the punish window). |

### 1. Vesper Cords (Ground job + Strike lanes)

- **Tell:** the vessel sinks and N glowing cords run out to stakes on the floor, 10.5 m from the centre, evenly spaced
  (random starting angle). Each stake is a 1.6 m circle with a numeral **1**.
- **Snap (7.0 s):** every cord whips along its length: a **1.4 m lane from the vessel to just short of the stake, 40**
  (Strike). At the same tick each stake checks for a player standing on it: **severed**. Each **unsevered** cord surges:
  **30 to every standing player** (unavoidable, so a missed stake always costs the whole party).
- **Melee threat:** the lanes start at the vessel, so hugging it on a cord line gets hit, and every stake is at the edge.
  **Ranged threat:** the lanes reach the edge; ranged players have to leave their spot for a stake.
- **Escape:** walk to a stake (worst case about 24 m in 7 s = 3.4 m/s), keep off the cord lines.
- **Co-op:** one stake per player, so the party splits round the edge. **2 players:** two stakes opposite each other.
  **3:** three at 120°. **4:** four at 90°. **Solo:** one stake (it is a normal "go stand there" job).
- **Below 50%: two waves.** A second set of N stakes appears halfway between the first ones and snaps after the first, with
  a gap sized to the walk between neighbouring stakes at 4.2 m/s plus 0.7 s (N = 1: 5.7 s, 2: 4.2 s, 3: 3.2 s, 4: 2.6 s).
  Each player severs two.

### 2. Seedgift (Ground stack, carried)

- **Tell:** the vessel tips and a bright seed lands over the carrier's head (carried-mark icon). Carriers: the player
  **farthest from the vessel**, then (4 players) the player farthest from the first carrier.
- **Seeds:** 1 seed for 1–3 players, 2 seeds for 4. Each seed is a **2.4 m circle that follows its carrier for 5 s, then
  locks**, and lands **1 s later**: **80 split between everyone inside** (a shared soak, ShareCount shown as a numeral).
  Solo: 40, all on the carrier.
- **Shares:** 1 player: 1 (40). 2 players: both under the one seed (40 each). 3 players: all three (27 each). 4 players: two
  pairs, one under each seed (40 each). Over-soaking lowers each share; a player in two seeds takes both.
- **Planting:** where it lands, the seed is planted and becomes a **Dusk Bloom** (2.4 m light) for the Nightfall that
  follows. The vessel's shadow pushes it out: a bloom is never closer than 7.2 m to the vessel (it rolls out).
- **Withering:** a carrier who walks out of their own locked seed leaves it empty: it withers, **60 to the carrier**, no
  bloom. A downed carrier's seed is withdrawn (no bloom either).
- **Melee / ranged:** the farthest player gets the seed, so ranged players carry; the stack pulls melee out to them.

### 3. Nightfall (whole floor, safe only in light)

- Always follows Seedgift. **Tell:** the vesper toll; the whole floor gets a dark hostile footprint with the blooms cut out
  of it (holes = the exact lit test). Each bloom shows its capacity numeral.
- **Dark (6.0 s):** **70 to every player not standing in a lit bloom.** A bloom holds as many players as planted it
  (the seed's share count: 1 / 2 / 3 / 2); one more and it **gutters**: everyone in it takes the 70 too.
- **Umbra:** the vessel's own shadow, a **4 m circle under it, 70**, lands with the dark (Ground). A player hit by the
  umbra is not hit again by the dark. Blooms never overlap it (7.2 m rule).
- **Melee threat:** the umbra. **Ranged threat:** you must be in a bloom, wherever the carriers planted it.
- **Co-op:** plant pairs apart (4 players), plant near where the party will want to stand.
- **Below 50%:** Nightfall starts the moment the seeds land and lasts **5.0 s**.
- **Hard (the named extra mechanic): Late Bloom.** As the dark lifts, each bloom bursts a moment later (2.8 m circle, 40,
  1.2 s warning): step out of the light right after Nightfall.

### 4. Dusk Tide (Ground in/out, then the punish window)

- **Tell:** the vessel drifts to a spot up to 3 m from the centre (toward the party) and its core flares. Both shapes
  show at once.
- **Ebb (2.8 s):** a ring from **6 m to the edge, 55**: come in close.
- **Flow (1.6 s later, 4.4 s):** the **6 m circle under it, 55**: get out.
- **Below 50%:** every other Tide is reversed (Flow first, then Ebb); the vessel's halo drops low to show it.
- **Escape:** from the far edge to inside 5.7 m (vessel drifted 3 m away: about 10.5 m) in 2.8 s, under 4.2 m/s with a 0.3 s reaction; then 0.6 m out after the Ebb. Reversed: from the body
  (1.75 m) out past 6.3 m in 2.8 s, then back in within 1.6 s (0.6 m).
- **Punish window:** afterwards the vessel closes its petals and dims for **3.5 s**: nothing is live (Tollbreaker heavy
  attacks and casters' stationary casts fit).
- **Melee / ranged:** both, one after the other; Ebb is the attack that punishes staying at range.

## Phases and rotation

- **Phase 1 (100–50%):** Vesper Cords → Dusk Tide → Seedgift + Nightfall → Dusk Tide, repeat. Gap 1 s between blocks.
- **50%:** the threshold is noticed at once but the shift waits for the current block to finish; then a **2 s harmless
  transformation** (the petals open wider; it stays damageable, no heal, same bar). Two thresholds crossed by one burst
  queue one shift.
- **Phase 2 (below 50%):** Seedgift + Nightfall (chained) → Vesper Cords (two waves) → Dusk Tide (reversed every other
  time) → Dusk Tide, repeat.
- **No enrage** (content rules §5: only Sylvara, Bellkeeper Ultimate and Tempo Tyrant have exceptions). Pools and lights
  always expire within their block.
- **Overlap:** never more than two damaging patterns at once (cord lanes + surge; umbra + dark; Late Bloom comes after).
- **Max HP:** 3000 (no co-op HP scaling, like the other practice minibosses; bots don't attack by default).
- **No ability text:** the phase line and intro lesson are empty; names live in the F7 overlay only.

## Difficulty

| | Easy | Medium | Hard |
|---|---|---|---|
| Warnings (every cast) | ×1.25 | as above | as above |
| Late Bloom after Nightfall | – | – | yes |
| Damage taken (shared rule) | ×0.6 | ×1 | ×1.3 |

## Party sizes at a glance

| | Solo | 2 players | 3 players | 4 players |
|---|---|---|---|---|
| Vesper Cords | 1 stake (2 below 50%) | 2 stakes, one each | 3 stakes | 4 stakes, one per side |
| Seedgift | 1 seed, 40 on you | 1 seed, both stack (40 each) | 1 seed, all stack (27 each) | 2 seeds, two pairs (40 each) |
| Nightfall | your bloom holds 1 | the bloom holds 2 | the bloom holds 3 | two blooms hold 2 each |
| Dusk Tide | same for everyone | | | |

**Bots (allies 0–3):** every job is a generic soak (stakes, seed stacks, blooms) or a plain shape to leave, so the standard
ally bots do the whole fight: they take the nearest open stake, stack on the nearest seed, fill the blooms by their
numerals and dodge the cords, umbra and tide.

## Conflicts with settled rules

- The request's "seeds handed to players that must be carried and planted together or delivered before nightfall" is kept
  as a carried stack; nothing teleports or pulls players (§2).
- The first design's "no jumping, no knockback" stays: every hit here has no knockback.
- Nothing else conflicts. No enrage was added.

## Open questions

1. Should cords also be severable by **attacking** the stake (a boss part), as well as by standing on it? Bots don't attack
   by default, so the standing rule is the one they can do.
2. Is 30 per unsevered cord (120 if four are missed) the right sting, or should a missed cord wipe like an empty soak?
3. Three players share one seed (27 each). Would two seeds (a pair and a solo carrier) be more interesting?
4. Should the Meshy rig replace the stand-in now, and which clips map to which ability (the old clip list fits:
   LanternCast for Cords, SeedTip for Seedgift, BloomOpen for Nightfall, HaloTilt for Dusk Tide)?
5. LAN: hazards, telegraphs and marks replicate; the placeholder cord lines, stake posts and seed orbs are host-only meshes.
