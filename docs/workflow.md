# Collaboration workflow

Production work targets **Unity with C#**. Follow the [Unity migration plan](plans/unity-migration.md); historical Godot task descriptions and prototype commands are reference material. Pin editor/packages and publish the C# integration contract before engine-dependent lanes bind.

1. Open an issue describing the desired result and acceptance criteria.
2. Agree on one owner before starting work.
3. Create a branch from the latest main, such as `feature/short-description` or `docs/short-description`.
4. Commit focused changes. Keep credentials, local settings, and generated files out of Git.
5. Open a pull request that explains what changed and how it was checked. Link its issue.
6. Ask the other collaborator to review before merging. Resolve feedback together.
7. Merge, close the issue, and update relevant documentation.

## Visual design review

Use the [art-direction guide](../reference/LAST-BELL-CLOCKWORK-FANTASY-ART-DIRECTION.md) for all character, weapon, spell/ability, boss, environment, animation, VFX, icon and UI work. It is the adopted shared style reference; [AGENTS.md](../AGENTS.md) makes this discoverable to coding agents.

Visual PRs should link the relevant guide sections and include a gameplay-camera preview when applicable. Check silhouette, palette/material language, telegraph accuracy, safe-region clarity and effect density. Record intentional deviations in the PR and update the guide through review if the shared style changes. Proposed budgets and example mechanics in the guide remain subject to current decisions and implementation evidence.

## Handoffs

Leave a short update on the issue: Completed / Next / Blocked. Link the relevant branch, pull request, or document.

## Decisions

Discuss freely in chat, then record the decision and reason in `docs/decisions.md` so both collaborators have the same context.

## Isolation

Do not add deployment workflows, dependencies on other repositories, or changes to the existing landing page without explicit agreement. This repository starts with no automation or external integrations.
