# Friendly markers — proposal

Update, October 23: five hostile exports have now arrived. The friendly script was rerun and the partial combined sheet was agent-checked. Those five are distinguishable from the friendly markers in grayscale; eleven hostile slots remain missing. See `qa/HOSTILE-IMPORT-CHECK.md` for current evidence. Statements below about zero hostile exports describe the earlier checkpoint.

October 23, 2026. Task 1 only. Twelve 256 px transparent exports are ready for Chzy18's review; no human approval or game integration is recorded.

## Completed

- Inspected the 12 original sources on a labeled source review sheet. I, II, III, IV, V, VI and VIII depict the exact requested numerals. VIII is V plus three I stems. The old VII has distracting orbit ornament; it was regenerated as a plain V plus two I stems. The original brass rim incorrectly includes a bell; its replacement has an empty center and four ivory studs. Both originals remain in `source/` alongside versioned replacements.
- Generated the two replacements with built-in imagegen and genuine alpha requested, one call per asset. Exact prompts and generation paths are in `friendly-regeneration-prompts.json`; original friendly prompts are in `friendly-original-prompts.json`.
- Exported all 12 selected sources through the existing read-only `IconTool.CleanChroma` helper using the task-specific `tools/clean-friendly-icons.ps1`. No hostile source, export, cleanup script or boss attack output was changed. The script produces only friendly-owned outputs.
- Validated 256 x 256 size, alpha extrema 0 and 255, transparent export borders, source clipping at opaque-border threshold, bounds and hashes. Every selected source has zero border pixels above alpha 128 and every export has zero nontransparent border pixels. Source alpha is genuinely present, not a baked checkerboard. Each selected source has both fully transparent and opaque pixels, with antialiased partial-alpha edges.
- Agent visually checked the cobalt and ivory contact sheets and all four native 64 px color/grayscale sheets. All eight numeral identities remain distinct, including VII versus VIII. Fine metal ornaments recede at 64 px but do not change the letters. The carried mark retains a bell, open dial arc and hand; the partner glyph reads as two linked beads. The brass circle and cobalt diamond remain distinct in grayscale by outline and stud/tab shape.
- Color review: ivory/cobalt faces and metallic brass trim, with no coral or orange/amber warning fill, glow or danger rays. Brass is explicitly allowed by the marker rule. The broad HSV warm-color screen in validation counts some brass/ivory shading too; it is diagnostic, not proof of a forbidden color. Material appearance and danger-color distinction were checked visually. No claim of exact palette quantization is made.

## Evidence and rerun

From the `ability-icons` checkout:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File docs/mechanic-icons/tools/clean-friendly-icons.ps1
```

Run using Windows PowerShell 5.1: the shared IconTool's System.Drawing references do not compile as-is under PowerShell 7. The helper was left unchanged.

- `export/friendly/`: twelve final-size proposal PNGs.
- `friendly-manifest.json`: selected source paths, export paths, complete prompts and SHA-256 hashes; humanApproved/integrated remain false.
- `qa/friendly/validation.json`: per-source, cleanup and export measurements.
- `qa/friendly/contact-cobalt.png`, `contact-ivory.png`: 128 px contact sheets.
- `qa/friendly/64-color-cobalt.png`, `64-color-ivory.png`, `64-gray-cobalt.png`, `64-gray-ivory.png`: icons drawn at native 64 x 64 on both backgrounds, individually inspected.
- `qa/friendly-source-review.png`: original source review including the superseded VII and brass rim.
- `qa/friendly/friendly-vs-hostile-64-gray.png` and `hostile-comparison-inputs.json`: final-export comparison availability, with all 16 missing hostile slots explicitly labeled.

## Next / blocked

The friendly-versus-hostile grayscale comparison cannot be assessed yet: `export/hostile/` contains no final PNGs at this checkpoint. Known-failed provisional crops were intentionally not substituted. Once the other owner supplies the hostile final exports, rerun this friendly script and inspect the combined sheet. This script reads hostile exports only and records their hashes. That final distinction check remains incomplete.

Open questions for Chzy18: approve the proposal art and circle-versus-diamond team identity? Is the retained brass trim sufficiently distinct from hostile amber in the gameplay context? Actual overhead gameplay visibility requires integration by its owner; the present check is at native 64 px against two flat backgrounds.

States: **generated:** twelve selected sources (ten original, two regenerated); **agent-checked:** twelve selected exports and friendly sheets; **human-approved:** zero; **integrated:** zero; **built/gameplay-tested:** not performed. Automated image validation passed, which is separate from a game build/test. No Unity or GitHub command was run. No third-party game material is included.
