# Co-op markers and mechanic icons — approval record

Chzy18 approved both sets on October 24, 2026, stating: "i approve both sets" in the task-coordination chat.

- Friendly set: all 12 delivered 256 px exports are human-approved. Their image checks and small-size/grayscale review were completed before this approval. The approved export hashes are recorded in `friendly-manifest.json`.
- Hostile set: approval for the set is recorded. Five final delivered icons are present: line stack, stretch tether, movement stop, movement move and ring. These five already had approval recorded on October 23.
- Eleven hostile replacements are still absent from the final export folder. Approval does not establish that these files have been delivered, cleaned or checked. Preserve the existing round-2 correction requirements; do not promote the rejected provisional crops to final exports.
- The complete 28-icon technical comparison remains pending receipt of all 16 final hostile exports. The existing partial comparison covers 12 friendly markers and five hostile icons.
- No game integration, Unity build or gameplay test is implied by this approval.

This record remains authoritative if a packaging-script rerun resets generated manifest approval flags.

## October 24, 2026: round-2 hostile replacements

**Chzy18 approved all 11 round-2 hostile replacements as they are**, stating: "I approve all 11 round-2 hostile icons as they are." The 11 are spread, soak tower, pass the mark, one side cleave, player mirror, pull, shrinking edge, cone, lane, delayed circle and low bar. With the five approved on October 23, **all 16 hostile icons are human-approved**. Their source and export hashes are recorded in `hostile-manifest.json`.

What Chzy18 saw before approving:
- The cobalt contact sheet, the 64 px grayscale sheets and the full 28-icon friendly-vs-hostile comparison (`qa/friendly/friendly-vs-hostile-64-gray.png`).
- The project cleanup run (`tools/clean-hostile-icons.ps1`): all 16 exports are `"clipped": false` with zero border pixels (`qa/hostile-regenerated-report.json`).
- The caveat that the 11 were cut from one generated 4×4 sheet rather than generated one per image, with margins added after cutting. The clip check can't rule out trimming inside that original sheet. Nothing looks cut off at review size.
- The closest friendly/hostile pairs at 64 px grayscale: brass/round vs ring, tether partner vs stretch tether, and carried mark vs pass the mark.

The bullets above saying the eleven replacements are absent and the 28-icon comparison is pending are superseded by this section.

The friendly cleanup script now keeps approval fields when it regenerates `friendly-manifest.json`, as long as the source file is unchanged. Changed art resets to unapproved.

No game integration, Unity build or gameplay test is implied by this approval.

Next: integrate both sets in game. Map each ID in `hostile-manifest.json` and `friendly-manifest.json` explicitly in code, and show the mechanic name next to each icon. Don't request approval again unless the artwork changes materially.
