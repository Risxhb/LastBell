# Collaboration workflow

1. Open an issue describing the desired result and acceptance criteria.
2. Agree on one owner before starting work.
3. Create a branch from the latest main, such as `feature/short-description` or `docs/short-description`.
4. Commit focused changes. Keep credentials, local settings, and generated files out of Git.
5. Open a pull request that explains what changed and how it was checked. Link its issue.
6. Ask the other collaborator to review before merging. Resolve feedback together.
7. Merge, close the issue, and update relevant documentation.

## Handoffs

Leave a short update on the issue: Completed / Next / Blocked. Link the relevant branch, pull request, or document.

## Decisions

Discuss freely in chat, then record the decision and reason in `docs/decisions.md` so both collaborators have the same context.

## Isolation

Do not add deployment workflows, dependencies on other repositories, or changes to the existing landing page without explicit agreement. This repository starts with no automation or external integrations.

## Live Wiki updates and shared acceptance

Risxhb and Chzy18 jointly own direction and acceptance: style, movement/combat/readability playtests, design choices, connection choice and tester coordination. See [Wiki maintenance](wiki-updates.md) and [the live hub](https://github.com/Risxhb/collab/wiki).

At task start/resume, read both feeds. At milestones/blockers/handoffs, update the progress feed and task/asset status. When game, asset or documentation changes become reviewable, add a human-feed summary with evidence and exact human action, or FYI if none. Record review/merge/decision outcomes without claiming unmerged work shipped. Include the Wiki update link in each relevant PR/handoff. Updates happen during active authorized work; no background updater or wakeup is implied.
