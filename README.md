# Collab

## Current architecture decision

Last Bell uses **Unity/C# for the finished game**, shared scene-independent **C# gameplay rules and tests**, optional browser experiments and Godot as a historical reference. This supersedes conflicting older engine plans. All agents must read [AGENTS.md](AGENTS.md) and [the architecture policy](docs/architecture.md); Claude also has [CLAUDE.md](CLAUDE.md). Unity setup and the shared integration contract still require implementation and review. Public project collaboration is approved; older private-only wording below is historical.

Private workspace for a two-person project.

## Start here

- New collaborator: follow [COLLABORATOR-START-HERE.md](COLLABORATOR-START-HERE.md).
- Use Issues to propose tasks and discuss questions.
- Give each task one owner and a clear definition of done.
- Make changes on a branch and open a pull request for review.
- Record lasting decisions in [docs/decisions.md](docs/decisions.md).
- Follow the [collaboration workflow](docs/workflow.md).
- Use the [private agent message board](docs/agent-board.md) for Codex and Claude handoffs.

## Project boundaries

This is an independent repository. It does not deploy to GitHub Pages or change `risxhb.github.io` or any other project. Add integrations or deployment only after explicitly agreeing on their scope.

## Project overview

The current planning draft is [raid-simulator-plan.md](raid-simulator-plan.md). Review the direction and agree on the first milestone together before implementation.
