# Ultimate: no Arcanist violets in boss cues (proposal)

Status: **proposal**, 28 Oct 2026. The look doesn't change until Chzy18 accepts it through the review inbox.
Source: Codex's audit `work/violet-clash-20261028/REPORT.md`, P1 "Bellkeeper Ultimate lane", which was read at 793d4c2. The line numbers below are from the current branch.
Rule: gpt/08 §4 says the Arcanist owns the violets, and no boss warning may use them. Hostile cues are coral or amber with crisp edges, and friendly cues never use coral or amber. Co-op cues are ivory numerals with brass and cobalt rims.

## What is violet today
| Cue | Where | Colour now | What it means |
|---|---|---|---|
| P3 own-echo disc | `OpenHour.cs` 359 (`echoRing`), drawn at 395 | violet `(0.70, 0.45, 1.00)` ×1.6, a **filled disc that grows** to 1.1 m | 2 s after you stood here it bursts: 999 to its owner only (hit at 158 uses EchoR + PR = 1.55 m) |
| P3 echo shots | `OpenHour.cs` 357 (`echoB`), drawn at 382 | lilac `(0.85, 0.72, 1.00)` ×2.2 | 4 small bullets from each burst, 50 to anyone |
| P2 INVERTED status | `BrokenHour.cs` 388 (`purple`), drawn at 434 | violet `(0.72, 0.45, 1.00)` ×2.2, a **filled 0.95 m disc under the player** | this player must stand inside the slam (a required-player cue, not damage) |
| P2 HUD job | `BrokenHour.cs` 457 | pale violet `(0.78, 0.60, 1.00)` | "INVERTED ✓ → stand in the SLAM" |
| P5 INVERTED label | `FinalToll.cs` 723 | pale violet `(0.80, 0.65, 1.00)` | "II INVERTED: inside" over the slam half |

## Proposal
Nothing changes in the rules: radii, timings and who it hits all stay the same. Only colour, fill and shape change.

1. **P3 own echo becomes a fixed outline with a countdown.**
   - It is drawn at its true size from the moment it's left, instead of a disc that grows.
   - The outline is at the exact kill distance, 1.55 m, which is the same convention as the P4 teeth disc Chzy18 accepted on 26 Oct.
   - **Ownership:** a thin ivory dashed rim with the owner's hour numeral (I–IV) in the middle. It only kills its owner, so the numeral says whose it is. Other players' echoes draw at half opacity.
   - **Countdown:** a coral fill grows outward from the centre over its last 0.8 s. A short coral flash plays when it bursts. This is the settled telegraph style: an outward fill over a fixed outline, then a flash.
   - There is no filled glowing disc until the countdown, so it can't read as an Arcanist rune.
2. **P3 echo shots become hostile projectiles.** They get a warm ivory core with a coral rim (instead of lilac) and stay smaller than Mainspring's brass bullets. They remain distinct from the rewound bullets, which are orange.
3. **INVERTED becomes a co-op mark, not a floor disc.** In P2 the filled purple disc goes and is replaced by:
   - a thin **cobalt ring**, unfilled, at the same 0.95 m;
   - four short **ivory ticks** pointing inward, like an upside-down bell rim;
   - the approved "Inverted" / "Get Inverted" glyph over the head once the glyph set is integrated, which is still pending Chzy18's go-ahead on the rebuilt set.

   The HUD text becomes ivory on cobalt: "INVERTED ✓ → stand in the SLAM". "GET INVERTED" uses bright ivory, not amber, because it's a friendly cue. The slam half itself is a hazard and stays coral.
4. **P5 INVERTED label:** ivory text with a cobalt underline, matching P2.

## Kept as they are
- Every true-hit outline and real geometry.
- Who each cue is for: the owner numeral, the INVERTED player, the label text.
- Delayed resolve times.
- The Echo Bellkeeper's teal ghost in P5. Teal is not an Arcanist violet.

## Checks once accepted
- Autotest presentation checks: no hazard material within the audit's Arcanist hue band.
- A capture of P2 and P3 at the gameplay camera with an Arcanist casting Comet and Sigil beside the cues.

## Open questions for Chzy18
1. Own-echo outline at 1.55 m (the true kill distance) or at the 1.1 m burst size?
2. Should other players' echoes show at all? They don't hurt you directly, but their shots do.
