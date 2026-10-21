# Collaboration workflow

Every model starts with [AGENTS.md](../AGENTS.md) and [the architecture policy](architecture.md). Use its [review checklist](architecture.md#review-checklist) before requesting review. State the runtime actually tested and distinguish experimental work from adopted production behavior. Update decisions and the relevant authorized lane handoff when architecture or shared interfaces change.

0. Before starting or resuming, complete [AGENTS.md mandatory coordination](../AGENTS.md#mandatory-coordination-and-wiki-reporting): read open board topics and complete relevant threads, check wiki Updates/TODO/progress, compare both operators' current scope and resolve overlaps before editing. Record assigned scope/branch/files in the relevant thread when posting is authorized. Recheck on scope changes and before handoff. An unreadable board blocks potentially overlapping edits.
1. Open an issue describing the desired result and acceptance criteria.
2. Agree on one owner before starting work.
3. Create a branch from the latest main, such as `feature/short-description` or `docs/short-description`.
4. Commit focused changes. Keep credentials, local settings, and generated files out of Git.
5. Open a pull request that explains what changed and how it was checked. Link its issue.
6. Ask the other collaborator to review before merging. Resolve feedback together.
7. Merge, close the issue, and update relevant documentation.

## Wiki Updates, TODOs and current progress

Every major update/new system must appear in [Updates](https://github.com/Risxhb/collab/wiki/Updates). Maintain [TODOs/owners](https://github.com/Risxhb/collab/wiki/Task-List) and [current progress](https://github.com/Risxhb/collab/wiki/Agent-Progress-Feed) at milestones, blockers and handoffs. Include date, owner, purpose, status, source evidence, actual validation, next and blockers. Link relevant entries in PRs/handoffs; distinguish accepted content from independently validated runtime behavior. Pull/reconcile wiki changes before publishing; never overwrite another lane. [Protocol](https://github.com/Risxhb/collab/wiki/Wiki-Update-Protocol), [request #38](https://github.com/Risxhb/collab/issues/38).

## Handoffs

Leave a short update on the issue: Completed / Next / Blocked. Link the relevant branch, pull request, or document.

## Decisions

Discuss freely in chat, then record the decision and reason in `docs/decisions.md` so both collaborators have the same context.

## Isolation

Do not add deployment workflows, dependencies on other repositories, or changes to the existing landing page without explicit agreement. This repository starts with no automation or external integrations.
