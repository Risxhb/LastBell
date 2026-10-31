# Hostile icons: ChatGPT regeneration prompts

**One icon per image.** The first batch was a sheet cropped too tightly, which clipped every icon. Make each image separately, square (1024 × 1024), on a **flat pure green #00FF00 background**. The cleanup script removes the green exactly; don't ask for transparency, because ChatGPT sometimes paints a checkerboard instead.

Save each result as `hostile_<id>.png` in `ability-icons/docs/mechanic-icons/source/hostile-regenerated/`, then run:

```
powershell -NoProfile -File tools/clean-hostile-icons.ps1
```

from `ability-icons/docs/mechanic-icons`. Check `qa/hostile-regenerated-report.json` for `"clipped": false`, and look at the sheets in `qa/`.

**For style consistency,** attach `qa/hostile-contact-cobalt.png` (or the matching provisional icon) to each chat and say "match this style".

## Style block (paste in front of every prompt)

> Single original game HUD icon for "Last Bell", a clockwork-fantasy boss game. Painterly sculpted enamel with antique brass bevels, bold compact silhouette, 2–3 broad shading bands, soft upper-left light, very few interior lines, readable at 64 pixels and in grayscale. Main danger shape in coral #EB685D enamel; small brass #C38D46 bevels and gem accents; porcelain ivory #F4E9D1 highlights and symbols; thick midnight-cobalt #17263F outline. ONE icon only, centred, occupying about 70% of the image with wide empty margins on every side; nothing touches or crosses the image edge. Background: flat solid pure green #00FF00, no gradient, no shadow, no checkerboard, no floor. No text, letters, numbers, frame, tile, watermark, particles or glow halo.

## Prompts

| File | Prompt (after the style block) |
|---|---|
| `hostile_spread.png` | Recreate the attached approved design exactly: eight coral arrows radiating outward in a full circle from a round brass compass medallion with a small gold orb at its centre. All eight arrow tips fully inside the image. |
| `hostile_soak_tower.png` | A stout coral cylindrical tower standing on a broad solid oval coral pedestal that is fully visible. A large blank ivory circular medallion fills the upper tower, at least a third of the tower's width, for a number added later. The medallion is EMPTY: no numeral. |
| `hostile_line_stack.png` | A broad vertical coral lane with flat ends, fully visible top and bottom. Three ivory player beads in a row down its centre. Two thick coral arrows, one on each side at mid-height, pointing inward at the lane. |
| `hostile_stretch_tether.png` | Two ivory player beads far apart on the left and right, joined by a straight taut brass chain. Two stout coral arrows, one on each end, pointing OUTWARD away from each other. Wide horizontal silhouette, everything inside the margins. |
| `hostile_pass_the_mark.png` | A small coral bell in the centre top. One curved coral arrow sweeps from an ivory player bead on the left to an ivory player bead on the right, carrying the bell across. One arrow, two beads, one bell. Not a circular swap. |
| `hostile_movement_stop.png` | An upright coral open-palm STOP hand, five fingers together, ivory cuff at the wrist, inside a coral octagon outline. Still and planted, no motion lines. |
| `hostile_movement_move.png` | A coral armoured boot mid-stride facing right, three broad ivory motion streaks trailing behind it, one small coral forward arrow ahead of the toe. Clearly moving. |
| `hostile_one_side_cleave.png` | Top-down: a single filled coral HALF-DISK (semicircle) on the right, with a perfectly straight vertical cutting edge on its left side and a small ivory pivot dot on that edge. The left half is empty. Not a fan, not a curved sweep. |
| `hostile_player_mirror.png` | A tall upright ivory mirror pane in the centre. One coral player bust silhouette on each side facing the mirror, exact mirror images of each other. Symmetric. |
| `hostile_pull.png` | Four stout coral arrows from the four sides pointing INWARD at one large ivory player bead in the centre. All four arrows the same length and fully visible. |
| `hostile_shrinking_edge.png` | Four thick coral corner brackets of a square arena, each moving inward, around a small empty square centre. Four small ivory chevrons pointing inward. Square, not round. |
| `hostile_cone.png` | One clean solid coral fan-shaped sector (about 60 degrees), point at the bottom, curved edge at the top, ivory bevel along the curved edge. Fully visible. |
| `hostile_ring.png` | One bold coral donut ring with a large empty circular centre (pure green shows through the hole), ivory inner bevel, four small brass gems on the rim. Fully visible, gems inside the margins. |
| `hostile_lane.png` | One straight coral rectangle lane, seen from directly above (not in perspective), set on a diagonal, with parallel ivory long edges and flat square ends. No arrows, no chevrons, no beads. |
| `hostile_delayed_circle.png` | One FILLED solid coral disk (no hole, no gems) with a single ivory clock hand pointing up-right from its centre and three broad ivory countdown ticks along its top edge. |
| `hostile_low_bar.png` | A low horizontal coral barrier bar on two short stout posts near the bottom, and a single coral boot clearly in the air ABOVE the bar, as if jumping over it, with a small ivory arc showing the jump path. No shield or bubble. |
