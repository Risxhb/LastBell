# Collab

Private workspace for a two-person project.

## Start here

- New collaborator: follow [COLLABORATOR-START-HERE.md](COLLABORATOR-START-HERE.md).
- Artists and gameplay designers: use the [shared visual style guide](reference/LAST-BELL-CLOCKWORK-FANTASY-ART-DIRECTION.md) for characters, spells, bosses, environments and UI. Agent contributors should also read [AGENTS.md](AGENTS.md).
- Use Issues to propose tasks and discuss questions.
- Give each task one owner and a clear definition of done.
- Make changes on a branch and open a pull request for review.
- Record lasting decisions in [docs/decisions.md](docs/decisions.md).
- Follow the [collaboration workflow](docs/workflow.md).
- Use the [private agent message board](docs/agent-board.md) for Codex and Claude handoffs.

## Project boundaries

This is an independent repository. It does not deploy to GitHub Pages or change `risxhb.github.io` or any other project. Add integrations or deployment only after explicitly agreeing on their scope.

## Project overview

**Last Bell** is a **2.5D isometric action raid roguelite for 1–8 total players**, with Windblown and Hades as creative references and an original clockwork-fantasy identity. Encounter mastery and build variety have equal weight. Here 2.5D means modeled 3D visuals on a flat gameplay plane, with fixed isometric framing and no vertical combat.

Read the [design plan](raid-simulator-plan.md), [art direction](reference/LAST-BELL-CLOCKWORK-FANTASY-ART-DIRECTION.md), and [our 2.5D foundation implementation plan](docs/plans/2-5d-isometric-foundation.md). The current combat experiment uses a temporary Node2D presentation; the 2.5D presentation migration is planned and has not been implemented.

## Combat experiment

The first Godot increment is in [game/](game/README.md): solo Vanguard versus a placeholder Bellkeeper sequence, with guard, damage feedback and instant restart. Use Godot 4.7.2-stable. See the [integration contract](docs/combat-contract.md) and assigned lanes [#6](https://github.com/Risxhb/collab/issues/6), [#7](https://github.com/Risxhb/collab/issues/7), [#8](https://github.com/Risxhb/collab/issues/8). This increment does not yet include the full slice or networking.

For ChatGPT on the website, start with the [GPT context directory](gpt/README.md), which explains the local combat prototype, current mechanics, and planned features.
