# Last Bell — collaboration

**Current direction:** a boss rush: three minibosses, shop intermission, larger boss, in handcrafted arenas. Read [current game design](docs/game-design.md) and the revised [production plan](raid-simulator-plan.md) before implementing old prototype behavior. Three starting classes, cursor aim, cooldowns, stamina/jump/sprint/dodge, no block/parry/iframes, practice, escalating revival and four difficulties are the current requirements. Shop functionality/progression/upgrades/healing are later work.

The [live Wiki](https://github.com/Risxhb/collab/wiki) contains [tasks](https://github.com/Risxhb/collab/wiki/Task-List), [human review](https://github.com/Risxhb/collab/wiki/Human-Review-Feed) and [progress](https://github.com/Risxhb/collab/wiki/Agent-Progress-Feed).

Next complete-run milestone: [three minibosses and one final boss in a single arena](docs/plans/single-arena-slice.md). Build the full run skeleton before final boss/art polish; retain explicit three-class/four-difficulty/online acceptance gates.

## Current architecture decision

Last Bell uses **Unity/C# for the finished game**, shared scene-independent **C# gameplay rules and tests**, optional browser experiments and Godot as a historical reference. This supersedes conflicting older engine plans. All agents must read [AGENTS.md](AGENTS.md) and [the architecture policy](docs/architecture.md); Claude also has [CLAUDE.md](CLAUDE.md). Unity setup and the shared integration contract still require implementation and review. Public project collaboration is approved; older private-only wording below is historical.

Public project workspace maintained by Risxhb and Chzy18.

## Start here

- New collaborator: follow [COLLABORATOR-START-HERE.md](COLLABORATOR-START-HERE.md).
- Use Issues to propose tasks and discuss questions.
- Give each task one owner and a clear definition of done.
- Make changes on a branch and open a pull request for review.
- Record lasting decisions in [docs/decisions.md](docs/decisions.md).
- Follow the [collaboration workflow](docs/workflow.md).
- Use the [agent message board](docs/agent-board.md) for Codex and Claude handoffs; historical private-only onboarding is superseded.

## Project boundaries

This is an independent repository. It does not deploy to GitHub Pages or change `risxhb.github.io` or any other project. Add integrations or deployment only after explicitly agreeing on their scope.

## Project overview

The [production plan](raid-simulator-plan.md) stages the current design. Unity arena #30 and model viewer #31 integrate through foundation #6; networking #7 and encounter #8 responsibilities remain.
