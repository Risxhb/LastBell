# Hostile icons, round 2: 11 to regenerate

The round-1 set (`Downloads/last_bell_hostile_regen_provisional/`) was cut from one grid sheet. Five icons are fine as they are and stay: **stretch tether, ring, movement stop, movement move, line stack**. These 11 need regenerating, either for a design fix or because pieces of neighbouring cells crept into the frame.

**Approved by Chzy18 on 23 Oct 2026:** those 5 round-1 icons. They're copied to `source/hostile-regenerated/` and exported to `export/hostile/` (clean, `"clipped": false`). Found in the comparison sheet afterwards: **movement stop** and **movement move** still carry a faint horizontal line above the icon, left over from the grid crop. **Fixed the same day:** rows 61–62 (the full-width line, with empty rows between it and the icon) were made transparent in both sources, then re-exported. The icons themselves are untouched.

## How

1. Start **one ChatGPT image per icon**, square 1024 × 1024. Never generate a grid or sheet.
2. Attach the round-1 icon of the same name. Say "match this style, but apply the fix below".
3. Paste the **style block** from `CHATGPT-REGEN-PROMPTS.md`, then the prompt below.
4. Save the result as `hostile_<id>.png` in `source/hostile-regenerated/`, then run `powershell -NoProfile -File tools/clean-hostile-icons.ps1` from this folder.
5. Check `qa/hostile-regenerated-report.json` for `"clipped": false`.

**Margin line for every prompt:** add this at the end: *"Leave at least 15% empty green margin on all four sides. Nothing else in the image: no partial shapes, lines or fragments near any edge."*

## Design fixes (3)

| File | What was wrong | Prompt (after the style block) |
|---|---|---|
| `hostile_lane.png` | A tilted bar reads like the low bar's beam at 64 px | Top-down view of a long straight coral ground lane, **vertical**, flat square ends, ivory bevel along both long edges, lying flat on the ground (no posts, no depth). Inside the lane, three ivory chevrons in a column all pointing UP along its length to show the direction it travels. Centred. |
| `hostile_delayed_circle.png` | Reads as a gauge or speedometer, not a circle on the ground | Top-down view of one FILLED coral disk lying flat on the ground (no hole, no needle, no clock hand). Around its rim, a thin ivory countdown track, three-quarters filled clockwise from the top like a fuse burning down, with one small brass gem marking where the fill ends. Centred. |
| `hostile_player_mirror.png` | The players are coral; every other icon draws players in ivory | A tall upright mirror pane in the centre with a **coral enamel frame** and pale silver glass. One **ivory** player bust silhouette on each side facing the mirror, exact mirror images of each other. Symmetric and centred. The coral must be on the mirror frame, not on the players. |

## Edge and crop fixes (8)

The designs are fine. Regenerate each one alone with the margin line so no fragment or top-edge line appears.

| File | Also fix | Prompt (after the style block) |
|---|---|---|
| `hostile_spread.png` | Arrows too long; tips near the edge | Eight short, stout coral arrows radiating outward in a full circle from a round brass compass medallion with a small gold orb at its centre. Compact: the arrow tips form a circle about 70% of the image width. |
| `hostile_soak_tower.png` | Too many brass spikes; busy at 64 px | A stout coral cylindrical tower on a broad oval coral pedestal, fully visible. A large blank ivory circular medallion fills the upper tower for a number added later; the medallion is EMPTY. Only four small brass gems on the rim. No spikes on the base. |
| `hostile_pass_the_mark.png` | Faint line along the top edge | A small coral bell at the top centre. One curved coral arrow sweeps from an ivory player bead on the left to an ivory player bead on the right, carrying the bell across. One arrow, two beads, one bell. |
| `hostile_one_side_cleave.png` | Sits off-centre to the right | Top-down: one filled coral HALF-DISK with a perfectly straight vertical cutting edge on its left and a small ivory pivot dot on that edge. The half-disk, edge included, is **centred** in the image. Not a fan or a sweep. |
| `hostile_pull.png` | Fragment at the bottom right | Four stout coral arrows from the four sides pointing INWARD at one large ivory player bead in the centre. All four the same length and fully visible. |
| `hostile_shrinking_edge.png` | Red arc fragment at the bottom | Four thick coral corner brackets of a square arena moving inward around a small empty square centre, with four small ivory chevrons pointing inward. Square, not round. Centred. |
| `hostile_cone.png` | Gem at the point made a fragment below it; top-heavy | One clean solid coral fan sector (about 60 degrees), point at the bottom, curved edge at the top, ivory bevel along the curved edge. **No gem or ornament at the point.** The whole fan is centred vertically. |
| `hostile_low_bar.png` | Edge risk | A low horizontal coral barrier bar on two short brass posts near the bottom, and one coral armoured boot clearly in the air ABOVE the bar, as if jumping over it, with a small ivory arc showing the jump path. |

## After all 11 are in

- Put the 5 keepers from round 1 into `source/hostile-regenerated/` alongside the new 11, so all 16 come from one folder.
- Ask Codex to run the cleanup, then rerun `tools/clean-friendly-icons.ps1` to make the **friendly vs hostile** grayscale sheet.
- Chzy18 then approves both sets. Until then everything stays "generated / agent-checked", not human-approved.
