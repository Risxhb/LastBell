# Sylvara v3: attack models and animation (proposal, 1 Nov)

Status: **proposal**. Chzy18 asked on 1 Nov to "build out the animation and models of its attacks". Gameplay doesn't change: hit shapes, timings and damage stay as built. This is presentation only (`gpt/08` §1: rules live in C#, not in art).

**Sources:**
- Look: `Reference/Boss-Model-Review/03-sylvara-v2.png` (bell-flowers, tuning-fork arm, three-hook branch hand, chest bells, back wheel; sage stone, wet wood, aged brass, verdigris, turquoise resonance).
- Warnings: `Reference/Telegraph-Style/03-verdant-chimes-proposal-v2.png` (coral fill with branch and blossom motif, outward-fill countdown, flash on resolve).
- Effects: `docs/design/spell-vfx-pipeline.md`.

## Today
- **Bullets:** every bullet is a glowing sphere in one of four colours: coral petal, brass, cobalt, or green for the big seed.
- **Her model:** it has 10 clips, but v3 only plays 6 of them: Idle, PruningArc, Rootscript, Seed, PhaseShift and Defeat. Spirals, tolls, jets, lanes, buds and the Seed Storm all play Idle.
- **Choir Buds:** primitive stand-ins with a coral timer ring.

## Readability rules for this work
1. Every bullet model fits inside its hit circle and keeps a bright core at the hit centre. The silhouette may spin, but the circle you dodge doesn't change.
2. **Hostile colours only.**
   - Petals stay coral.
   - The two-colour petals stay brass and cobalt (the player markers' colours).
   - Turquoise is her resonance colour and is never used on a bullet that hurts.
3. Props sit inside their existing warnings, never on top of them. A prop that hides a warning gets lowered or faded.
4. The bullet count is up to about 920 on screen, so bullet models are 1 small mesh each, drawn in one batch per kind. There are no per-bullet lights.

## Bullets and props (models)

| Thing | Today | Proposed model | Animation |
|---|---|---|---|
| Petal bullet (all phases) | coral sphere | a single cream-to-coral petal, about 0.5 m long, with a coral glowing core | turns to face its flight path and spins slowly |
| Two-colour petal (P2) | brass or cobalt sphere | the same petal tinted brass or cobalt, with a matching rim | same |
| Big seed (P4 Seed Ring) | green sphere | a brass-capped seed pod about 0.6 m across, with a verdigris husk and seams | the seams glow brighter as the fuse runs down, so you can see when it will burst; on burst the husk splits and the petals fly out |
| Choir Bud (P3) | primitive | a bell-flower on a root stalk, matching the reference sheet's Phase 2 panel: a brass bell inside ivory petals | grows out of the floor in 0.6 s; the petals open wider as the bloom timer runs; when cut it droops and sinks; when it blooms it flares open, then crumbles |
| Seed Pod drop (P1, P4) | warning circle only | a heavy seed pod that falls into the circle | drops in the last 0.4 s of the warning, lands with the flash, splits and leaves a short ring of shards |
| Waterfall Jets (P1) | warning only | a column of falling water from above, with a splash ring at the base | the column pours for the length of the hit, then thins out |
| Root Ring (P4) | warning only | a ring of roots bursting up around her feet | rise on resolve, hold 0.3 s, sink back |
| Pruning Arc (P1, P3) | warning plus sweep flash | a turquoise ribbon trail behind the tuning-fork arm | follows the existing PruningArc swing |

## Her animation (new and reused clips)

| Attack | Today | Proposed clip |
|---|---|---|
| Petal Spiral, P2 spirals, P4 Slow Spiral | Idle | **Conduct** (new loop): the tuning-fork arm draws slow circles the same way the spiral turns, and the chest bells sway. |
| Last Storm | Idle | **Storm** (new loop): a faster, wider Conduct with both arms up and the mask open. |
| Toll and Two-Colour Toll burst | Idle plus the Crescendo effect | **Toll** (new, 0.7 s): she lurches forward so the chest bells ring, the arms snap down, and a turquoise ring rides out with the burst. |
| Waterfall Jets | Idle | **Call Water** (new, 1.5 s, the warning length): the branch hand lifts up over the jets, then pulls down as they pour. |
| P2 Lanes | Idle | Conduct with a **downbeat** every lane (a small accent on top of the loop, not a new clip). |
| Choir Buds planted (P3) | Idle | reuse **Bloom** (built for v2, unused): one downbeat per bud. |
| Seed Pods | Seed | keep it. |
| Seed Ring (P4, every 2 s) | Idle | **Sow** (new, 0.5 s): a chest pulse and an arm flick that throws the ring. It's short, so it doesn't fight Conduct. |
| Root Ring | Rootscript | keep it. |
| Pruning Arc | PruningArc | keep it, and add the ribbon trail. |
| Phase change, Defeat | PhaseShift, Defeat | keep them. |

## How it gets built
1. **Clips:** add Conduct, Storm, Toll, Call Water and Sow to `prototype/arena-3d/models/blender/sylvara_rig.py`. Rebuild `Sylvara.glb` with `build-sylvara.ps1`, which uses the same Meshy source as today. Then show a contact sheet of the key poses for review.
2. **Props:** model them in Blender by script, like the Area 4 props: petal, seed, bell-flower bud, seed pod, water column, root ring. Then show turntable renders for review.
3. **Unity:**
   - The bullet view draws the petal and seed meshes instead of spheres.
   - The bud, pod, jet and root props are placed by `SylvaraEncounter`.
   - The clips are played from the attack methods.
   - The autotest checks that each attack plays its clip and that bullet hit radii don't change.

## Open questions
1. **Petal look:** one petal shape for every phase, or a different shape per phase (round petal in P1, pointed in P4)?
2. **Bell-flower buds:** should they be modelled from the reference sheet (Blender, like the arena props), or should Chzy18 run them through Meshy like the boss?
3. **Second form (P3–P4):** the sheet shows the mask split open with turquoise light. That's a model change (a split mask mesh), not a clip. Include it in this pass, or keep it for later?
