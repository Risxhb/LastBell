# Partial hostile import and friendly comparison

October 23, 2026. User requested a local commit of the regenerated sources, exports and updated QA sheets.

Five supplied ChatGPT icons are present: line stack, stretch tether, movement stop, movement move and ring. Their sources and corresponding exports are preserved here. `CHATGPT-REGEN-ROUND2.md` records their origin, Chzy18's approval of these five designs, and the prior removal of crop-line rows 61–62 from the stop/move sources. The original versions remain in Downloads according to the supplied handoff; this task did not alter them.

Agent checked cobalt/ivory contact sheets, native 64 px hostile grayscale, and the regenerated friendly-versus-hostile grayscale sheet. No horizontal artifact line is visible over stop or move. Stop's palm and move's boot/arrow remain distinct. All five supplied hostile glyphs are distinguishable from the twelve friendly markers in the combined sheet: line stack has inward arrows and a lane; stretch tether has outward arrows; ring has a broad body and pointed studs, unlike the thin friendly rim. The friendly partner glyph has curved loops rather than the hostile tether's straight chain and arrowheads.

All five exports pass 256 x 256, real-alpha extrema 0/255 and fully transparent border checks. Hashes and measurements: `hostile-import-validation.json`. Existing `hostile-regenerated-report.json` reports no clipping on all five. The friendly script rerun also passed all twelve friendly image checks. This pass did not regenerate or repaint any hostile artwork.

The combined sheet deliberately uses only files in `export/hostile/`: eleven slots are still marked MISSING. The top-level hostile contact sheets additionally show explicitly labeled provisional art; those are not final exports. Full-set comparison remains pending the eleven round-2 files listed in `CHATGPT-REGEN-ROUND2.md`. Rerun the hostile cleanup and then the friendly script after those arrive.

States: supplied/generated externally — five selected hostile sources; agent-checked — these five and the partial combined sheet; human approval — five designs as recorded in the supplied round-2 document, no blanket approval of the friendly set or remaining hostile set inferred; integrated/built/gameplay-tested — none. Image validation passed. No Unity or GitHub commands were run.
