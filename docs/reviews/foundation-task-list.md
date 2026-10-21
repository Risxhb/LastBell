# Foundation task-list delivery — October 21, 2026

Owner/operator: risxhb/codex / Risxhb. Source: [Claude's suggested task list](https://github.com/Risxhb/collab/issues/6#issuecomment-6311643559), explicitly authorized for completion by Risxhb. Current runtime review target is draft #42 at `18e89ed264eb3ba2cd0f18b50dfb7eaacc6f3149`. This delivery changes documents and diagnostic fixtures, not the run-loop implementation or class assets. Networking remains held; accepted arena and existing content ownership remain.

## Deliverables

| Task | Delivery / remaining gate |
| --- | --- |
| Start-of-day coordination | Open board listed; full relevant #6/#8/#29/#31/#36/#38/#41/#44/#47/#7/#30 comments read; live wiki freshly cloned and reviewed; scope [posted in #6](https://github.com/Risxhb/collab/issues/6#issuecomment-6313283625). Isolated documentation branch and fresh runtime validation clone. |
| 1. Review #42 | [Component dispositions and findings](run-loop-42.md): keep presentation/content adapters, adapt rules behind U3, replace incomplete event authority. Requests changes before shared production acceptance. |
| 2. U0 | [Independent bootstrap/runtime evidence](u0-validation.md), exact source/packages, Windows build and Editor/Player results. A green smoke test does not overrule targeted defects or establish gameplay/human acceptance. |
| 3. R1–R21 / U3 | [Every request disposition and typed interface](../contracts/u3-review.md), C# types, staged capabilities and fixtures. Proposal delivered for consumer review, not adopted/bound production authority. |
| 4. Reconcile docs | Existing #33 updated at `ca19096`; wiki Game-Design, Single-Arena-Slice, Task-List and feedback queue reconciled. Settled no-immunity/recovery and approved class exceptions supersede blanket restrictions. Four-area/boss packets remain review scope. |
| 5. Combat accounting | [Event/metric schema and #42 gap assessment](../contracts/combat-accounting.md): source/target/action/attempt/segment IDs, damage/heal/absorb, dedup, effective damage, frozen combat-time DPS, bounded history and recap. |
| 6. Class exceptions | U1 section in [U3](../contracts/u3-review.md): Vanguard Brace/Opening; Chimesinger Harmony/bells/echoes/instant heal; Arcanist Ticks/seals/inscriptions/Anchor shield. No old counter/iframe system. |
| 7. PR housekeeping | All ten open Risx-authored PRs received explicit status comments; #40 also received a holding rationale. Table below. No blind merges or source-branch deletion. |
| 8. Human playtest | [Short actionable checklist](playtest-and-decisions.md), all classes, Brace/input, HUD/tooltips, recap/meter, Arcanist timing, health/potions and no-immunity geometry. Prepared, not executed by a human. |
| 9. Human decisions | Same queue consolidates D1–D6 (D3 settled; D2 reframed under current recovery), meter scope and four supplied boss concepts with recommended defaults. No silent design acceptance. |
| End-of-day handoff | Wiki Updates, Task-List and progress synchronized; review/contract/U0 links posted to #6/#8 and #42. Consumer acknowledgment and human answers remain pending. |

## Open PR dispositions

Each linked conversation has the October 21 risxhb/codex status. “Retain” is a concrete review disposition, not completion of its original acceptance gate.

| PR | Disposition |
| --- | --- |
| [#2](https://github.com/Risxhb/collab/pull/2) | Retain; reconcile early online plan with current boss-rush/U3. Preserve solo + 2–8 and networking hold. |
| [#9](https://github.com/Risxhb/collab/pull/9) | Retain historical wiki snapshot; #18/#39 and live wiki supersede stale setup/status. |
| [#10](https://github.com/Risxhb/collab/pull/10) | Retain Godot behavior/test reference; old Guard/resources/iframe/victory ordering is not production acceptance. |
| [#11](https://github.com/Risxhb/collab/pull/11) | Retain historical roster evidence; Unity/C# lifecycle is U3. |
| [#12](https://github.com/Risxhb/collab/pull/12) | Retain historical lock/anchor evidence; use reviewed semantics in C#, not Godot bindings. |
| [#13](https://github.com/Risxhb/collab/pull/13) | Retain for stack/reference reconciliation; setup-selection prose is superseded, art-guide overlap with #40 needs explicit resolution. |
| [#17](https://github.com/Risxhb/collab/pull/17) | Retain unchanged supplied Bellkeeper v1.0 reference pending collaborator review; no automatic number/mechanic adoption. |
| [#18](https://github.com/Risxhb/collab/pull/18) | Retain live-wiki workflow for review; resolve overlapping onboarding/README/workflow with #33/#39. |
| [#33](https://github.com/Risxhb/collab/pull/33) | Updated design reconciliation; Chzy18 review requested before merge. |
| [#39](https://github.com/Risxhb/collab/pull/39) | Retain coordination delta stacked on #33; reconcile base and preserve mandatory checks. |
| [#40](https://github.com/Risxhb/collab/pull/40) | Held for art-guide overlap and separation of reference concepts from approved gameplay. Former local merge is archived/unpushed, not a GitHub merge. Assets unchanged. |

## Remaining acceptance

Chzy18/Claude must review U3/order/capabilities before binding; foundation review does not transfer content ownership. Human feel/readability, D1/D2/D4/D5/D6 and prototype tuning remain open. #42's reported defects require the content/integration owner's coordinated fixes and new targeted regressions. Full difficulty/fairness, down/revive and 2–8/network acceptance are separate future gates; no network work was restarted here.
