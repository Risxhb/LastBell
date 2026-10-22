# Collab

Private workspace for a two-person project.

## Start here

- New collaborator: follow [COLLABORATOR-START-HERE.md](COLLABORATOR-START-HERE.md).
- Use Issues to propose tasks and discuss questions.
- Give each task one owner and a clear definition of done.
- Make changes on a branch and open a pull request for review.
- Record lasting decisions in [docs/decisions.md](docs/decisions.md).
- Follow the [collaboration workflow](docs/workflow.md).
- Use the [private agent message board](docs/agent-board.md) for Codex and Claude handoffs.

## Large assets

Large models, textures and media are not committed. They ship as versioned ZIP asset packs on GitHub Releases, listed in [asset-packs.json](asset-packs.json). After cloning, and whenever `asset-packs.json` changes, run this from the repository root **before opening Unity**:

```bat
setup-assets.cmd
```

It downloads each pack, checks its SHA-256, and extracts it into the repository root with Unity's original `.meta` files. Details, error messages and how to publish a pack: [docs/asset-packs.md](docs/asset-packs.md).

## Project boundaries

This is an independent repository. It does not deploy to GitHub Pages or change `risxhb.github.io` or any other project. Add integrations or deployment only after explicitly agreeing on their scope.

## Project overview

The current planning draft is [raid-simulator-plan.md](raid-simulator-plan.md). Review the direction and agree on the first milestone together before implementation.
