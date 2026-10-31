# Hostile mechanic icons — cleanup and QA report

October 23, 2026, chzy18/claude. **Proposal.** No icon is production-approved, and none has been put into Unity.

## Summary

- **Transparency fixed.** The 16 ChatGPT crops were RGB with a checkerboard and white padding baked in, with no alpha. `tools/clean-hostile-icons.ps1` removes them:
  - flood-fills from the border
  - clears enclosed checker pockets, such as the ring's centre
  - de-mattes the edges
  - drops stray fragments from neighbouring icons
  
  It exports 256 × 256 RGBA files. On cobalt and ivory backgrounds there is no visible checkerboard, halo or speck. The report is in `qa/hostile-cleanup-report.json`.
- **Every icon is clipped.** Each crop window cut through its glyph: the main shape touches the crop edge on at least one side in all 16. Removing the background can't restore missing art, so **0 of 16 pass QA**.
- **Semantics:**
  - 7 provisional icons depict the right idea (spread, soak tower, line stack, pull, cone, ring, lane).
  - 4 depict it wrongly: pass the mark has no bell, one-side cleave is a sweep, low bar is an arrow, and delayed circle has an hourglass and a ring-like rim.
  - 1 is incomplete: stretch tether has one arrow, not two.
  - 4 are missing: stop, move, player mirror, shrinking edge.
  - The 4 unrequested substitutes are excluded: cross, dive, knockback, rotating.
- **Next step:** regenerate one icon per image in ChatGPT using `CHATGPT-REGEN-PROMPTS.md`. Then drop the files in `source/hostile-regenerated/` and rerun the script; it keys the flat green background, checks for clipping and exports to `export/hostile/`.

## Per-icon status

| Mechanic | Provisional | Verdict |
|---|---|---|
| Spread | yes | Design **approved**; clipped right and bottom. Regenerate the same design, attaching the crop as a reference. |
| Soak tower | yes | Pedestal clipped off. Blank numeral slot is correct. Regenerate. |
| Line stack | yes | Reads correctly; clipped left and bottom. Regenerate. |
| Stretch tether | yes | Left bead clipped; only one outward arrow. Regenerate. |
| Pass the mark | yes | No bell, so it reads as swap/rotate; arrows clipped. Regenerate. |
| Movement stop | — | Missing. |
| Movement move | — | Missing. |
| One-side cleave | substitute | Curved sweep, not a semicircle. Generate. |
| Player mirror | — | Missing. |
| Pull | yes | Correct; left and bottom arrows clipped short. Closest to final. Regenerate. |
| Shrinking edge | — | Missing. |
| Cone | yes | Correct; clipped left and bottom. Regenerate. |
| Ring | yes | Correct; gem tips slightly clipped. **Usable as a placeholder**; regenerate for final. |
| Lane | yes | Top clipped; tapered and chevroned rather than a straight rectangle. Regenerate. |
| Delayed circle | yes | Hourglass instead of a clock hand; gem rim makes it look like Ring at 64 px in grayscale; clipped. Regenerate. |
| Low bar | substitute | Wrong: no bar and no boot. Generate. |

## Distinction checks (qa/hostile-64-gray.png, qa/friendly-vs-hostile-64-gray.png)

| Pair | Result |
|---|---|
| Spread vs Pull | Pass: eight outward arrows vs four inward. |
| Ring vs Delayed circle | **Fail:** both are round, gem-studded rims at 64 px in grayscale. The prompt now asks for a filled disk with a clock hand and no gems. |
| Cone vs One-side cleave | Can't judge: the cleave art is a substitute. The new prompts ask for a sector vs a half-disk with a straight edge. |
| Stop vs Move | Missing art. |
| Line stack vs Lane | Pass: beads and inward arrows vs chevroned strip. |
| Stretch tether vs Pass the mark | Pass on silhouette, but pass the mark is semantically wrong. |
| Friendly vs hostile | Pass in grayscale. Friendly numerals and the bell are upright inscriptions with no arrows or fill; hostile glyphs are filled shapes with brass gems. The friendly sources themselves are still unreviewed (Codex task). |

## Notes and conflicts

- **Coral hex:** the brief uses `#F07870`, but the settled palette (`gpt/08-design-rules.md`, art bible) is Ember Coral `#EB685D`. The regeneration prompts use `#EB685D`.
- **Unity import settings:** the brief asks for Sprite (2D and UI). The project's existing HUD icons (`Run/Resources/Run/Icons/*.png.meta`) are **Default** textures, uncompressed, with no mipmaps, alpha-is-transparency on and max size 256. `RunHud` loads them with `Resources.Load<Texture2D>` and draws them with IMGUI. Follow that convention unless the HUD moves to uGUI.
- **No mechanic-indicator system exists** in the run-loop code. The HUD only maps ability icons by filename. Showing mechanic icons needs a small HUD hook owned by the run-loop and encounter sessions. That hook is out of this art task's scope, and `Run/**` is off-limits here, so nothing was integrated. When icons are approved:
  1. copy `export/hostile/*.png` to `Run/Resources/Run/Icons/Mechanics/`, with metas matching the existing icon settings
  2. map each ID in `hostile-manifest.json` explicitly in code, not by filename guessing
- The soak tower numeral is drawn at runtime; the art keeps a blank ivory medallion.

## Evidence

- `qa/hostile-contact-cobalt.png`, `qa/hostile-contact-ivory.png`: the 16 slots at 128 px. Missing slots are dashed.
- `qa/hostile-64-color.png`, `qa/hostile-64-gray.png`: native 64 px.
- `qa/friendly-vs-hostile-64-gray.png`
- `qa/hostile-cleanup-report.json`: per-file window, clipped sides, fragments removed, residual checker pixels, scale.
- The chroma-key path was self-tested on a synthetic green-screen image: corner alpha 0, interior colour preserved, no clipping.

**States:**
- **Generated:** provisional crops by ChatGPT, not by me.
- **Cleaned:** all 16.
- **Agent-checked:** all 16 plus the sheets.
- **Human-approved:** spread design only.
- **Integrated or built:** none.
