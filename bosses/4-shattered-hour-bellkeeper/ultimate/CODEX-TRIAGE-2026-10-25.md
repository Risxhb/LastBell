# Triage of Codex's 24 Oct Ultimate review (proposal)

**Source:** `CODEX-HANDOFF-2026-10-24.md`, its guide check (`work/ultimate-guide-check-20261024/CHECK.md`, D01–D37), the telegraph audit and the readability review. Triaged 2026-10-25 by the Ultimate session. These are proposals; Chzy18 decides.

## Context
The review covers the **v1** Ultimate (the build Codex inspected on 24 Oct). Since then:
- Chzy18 rejected v1.
- v2 has rebuilt P1–P3, each locked or built after Chzy18 saw it:
  - **P1:** Escapement Relay and Chime Debts.
  - **P2:** Broken Hour.
  - **P3:** The Open Hour, a bullet hell.
- A v2 attempt ends after P3. v1's P4, P5 and transitions are not reachable in v2 yet.

## Disposition

| Finding | v2 status | Proposal |
|---|---|---|
| D01–D17+ (guide/spec drift: stations, clocks, Stopped Hour, Ledger, Backwards Bell, Two Futures …) | **Superseded.** None of these mechanics is in v2 P1–P3. | Close for v2. If v2 P4/P5 reuse a v1 mechanic, re-check the matching D-items then. |
| Solo / 8-player formations "must not be settled from bot claims" | **Still applies**, and now harder: solo is a requirement (08-design-rules §5, 2026-10-25). | v2 records per-size bot results honestly (P1–P3 LOCKED docs; solo, duo, trio and 8 are open in places). A human solo strategy must be written and played, not inferred from bots. |
| Telegraph F: Ultimate Toll Tide (inward front vs outward fill) | Not used in v2 P1–P3. | Fix only if P4/P5 bring Toll Tide back. |
| Telegraph F: Stopped Hour lash lane runs past the first-body stop | Not used in v2 P1–P3. | As above. |
| Telegraph F: Tolling Hour raidwide hidden telegraph | **Moot.** v2 has no unavoidable damage (ULTIMATE-PROMPT-v2 item 1), so the raidwide is gone. | Close. |
| Readability review (5 Ultimate sequences) | v1 footage. | v2 got its own readability pass (P1: duty-coloured labels, spot words, a REWIND banner, the practice beam), which Chzy18 approved for P1. Re-review v2 clips, not v1. |
| Bot summary timing formatting error | Harness detail. | Keep raw timestamps as the source in v2 reports. |

## New requirements from 2026-10-25 (08-design-rules §1, §5) that change v2 work
1. **Only fight.** The Ultimate is the only Bellkeeper fight; keep it practice-only until Chzy18 decides the run ending.
2. **Soloable to the normal standard.** In v2 so far:
   - P1 solo soaks every beat but hits the 150 s push for lack of damage.
   - P2 solo was not run (the trio clears).
   - P3 solo was not run, and the trio dies to its own echoes.
   - Needs solo/duo tuning per phase: HP, push and, where needed, simpler solo assignments.
3. **Easy/Medium/Hard.** Medium is the v2 baseline. Easy and Hard need defining. The proposal is to change timings and margins (beat length, gap widths, bullet density, echo delay), not one-shot rules.
