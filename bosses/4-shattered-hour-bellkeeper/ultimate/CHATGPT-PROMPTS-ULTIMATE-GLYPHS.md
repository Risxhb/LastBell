# ChatGPT prompt, 26 Oct 2026: Ultimate job glyphs (18 icons)

For the Bellkeeper Ultimate v2 (proposal). These replace the text-only job labels in the HUD and over players' heads
("SOAK", "TICK", "GET INVERTED"…) with glyphs. The words stay beside them at first. Each glyph is one
transparent-background icon in the house ability-icon style (template T6).

## Before you start

1. Open the **Last Bell — Art References** project. Make sure `02-visual-style-guide.md`, `06-icons-ui-vfx.md` and
   `07-prompt-templates.md` are up to date there.
2. Start a new chat in the project and drag in `gpt/images/11-ability-icons-overview.png` (the style to match).
3. Paste the prompt below and send it. Say "next" after each image, or "redo #N: …" if one misses.

## Where to save the results

Save each as `Reference/Ultimate-Glyphs/<name>.png`, using the names in the table. I'll check them and put them into
the game.

| # | Name | Meaning in the fight | Accent |
|---|---|---|---|
| 1 | `soak` | stand on the lit plate (P1, P2, P5) | gold |
| 2 | `chain` | hold the pendulum chain out on the far side (P1) | ivory |
| 3 | `tick` | stand on the ✕ just ahead of the hand (P1) | gold |
| 4 | `tock` | stand on the ✕ just behind the hand (P1) | teal |
| 5 | `debt` | you owe a chime debt: pay it on your next soak (P1) | ivory + coral rim |
| 6 | `inverted` | you are INVERTED: stand inside the slam (P2, P5) | violet |
| 7 | `get-inverted` | INVERTED is coming to you: meet its holder (P2) | violet, open |
| 8 | `pass` | hand INVERTED on: touch the next holder (P2) | violet + gold |
| 9 | `safe-half` | stay out of the slam (P2, P5) | teal |
| 10 | `ride-gear` | stay on your gear (P4) | gold |
| 11 | `to-centre` | get to the centre gear (P4, transitions) | gold |
| 12 | `gap` | go through your gap in the sound ring (P5) | gold |
| 13 | `echo-gap` | go through your gap in the echo ring (P5) | sky blue |
| 14 | `platform` | stay on your platform (Last Stand) | gold |
| 15 | `live` | this Bellkeeper takes damage now (P5 echo phase) | gold halo |
| 16 | `danger-teeth` | the gear teeth are about to bite | coral |
| 17 | `danger-hammer` | the hammer is coming down | coral |
| 18 | `danger-clapper` | the clapper's lane | coral |

## The prompt

```
We're making HUD job glyphs for Last Bell's Bellkeeper Ultimate. Use template T6 from 07-prompt-templates.md and the attached icon overview (image 11) as the exact style to match. Everything here is proposal art.

Rules for every icon:
- ONE icon per reply. Wait for me to say "next".
- Square PNG with a genuinely transparent background. No frame, tile, backdrop, floor or drop shadow.
- One bold central glyph about 76% of the canvas with about 12% padding. Readable at 48 px and in grayscale.
- Painterly sculpted enamel and clockwork brass, 2–3 broad tone bands, soft upper-left light, very few interior strokes.
- Base palette brass #C38D46, ivory #F4E9D1, dark cobalt #17263F silhouette edges, plus the ONE accent I give.
- No text, letters, numerals, arrows made of text, tiny ornaments, fine particles or heavy bloom.
- Friendly jobs use calm accents (gold #F6C968, teal #55C9CE, violet #8E6BD8, sky blue #9FE0FF). Only the three DANGER icons use ember coral #EB685D.
- The set must read as one family: same lighting, same brass, same thickness of edges.

Icons (in order):
1. soak: a round brass floor plate seen at a slight angle, with a thick downward chevron pressing onto it. Accent gold.
2. chain: one heavy pendulum chain stretched taut on a diagonal, ending in a small brass bob. Accent ivory.
3. tick: a brass counterweight (a squat bell-shaped weight) with a thick curved arc swinging CLOCKWISE over its top. Accent gold.
4. tock: the same counterweight with the thick arc swinging ANTICLOCKWISE, so tick and tock read as a pair but never the same. Accent teal.
5. debt: a porcelain ring like a cracked enamel bangle with one small brass bell hanging inside it; the ring's rim has a thin coral line. Accent ivory, coral rim only.
6. inverted: a brass bell turned upside down (mouth up), glowing softly inside. Accent violet.
7. get-inverted: the same upside-down bell drawn as an outline-heavy, hollow shape with a thick inward chevron below it, meaning "it is coming to you". Accent violet.
8. pass: two upside-down bells side by side, a thick curved hand-off arc from the left one to the right one. Accent violet, the arc gold.
9. safe-half: a circle split in two halves; one half solid calm teal enamel, the other half dark and empty. Accent teal.
10. ride-gear: a single brass gear with a small round footprint pad on its face. Accent gold.
11. to-centre: a brass gear with three thick chevrons pointing in to its hub from outside. Accent gold.
12. gap: a thick ring of sound (concentric brass linework) with one clean gap cut out of it, a small gold pip passing through the gap. Accent gold.
13. echo-gap: the same gap ring drawn twice, the second ring a little offset and paler, the pip passing through both gaps. Accent sky blue.
14. platform: a small floating round slate platform with a brass rim, a few broken shards falling away beneath it. Accent gold.
15. live: a brass bell with a gold halo crown above it, radiant but compact. Accent gold.
16. danger-teeth: two brass gear teeth meshing together with a sharp coral flash at the bite point. Accent coral.
17. danger-hammer: a heavy brass bell-hammer head striking down, one thick coral impact ring under it. Accent coral.
18. danger-clapper: a long bell clapper swinging, its path a thick coral band. Accent coral.
```
