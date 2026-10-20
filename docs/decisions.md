# Decision log

For each decision, record the date, decision, reason, and relevant issue or pull request.

## 2026-10-20 — One Unity game with reusable C# rules

- Decision: Unity/C# owns the finished game. Shared scene-independent C# owns adopted gameplay rules and supports standalone automated tests. Browser prototypes are optional experiments; Godot is a historical behavior/test reference. Do not maintain two production game implementations.
- Reason: Risxhb approved this architecture and asked for shared instructions applying to all models.
- Scope: Preserve 2.5D modeled visuals, fixed isometric camera, planar authoritative combat, solo plus 2–8 total players and existing lane ownership. Specific prototype gameplay changes remain subject to adoption review.
- Delivery: [architecture policy](architecture.md), [AGENTS.md](../AGENTS.md), [CLAUDE.md](../CLAUDE.md); foundation #6, networking #7 and encounters #8 retain ownership.
- Status: Policy recorded; exact Unity editor/packages, runtime bootstrap, final typed C# contract and Unity integration remain open. Documentation does not establish runtime acceptance or collaborator acknowledgment.
- Visibility: Public project collaboration was approved on October 20; the original private-only requirement below is historical. Publish only authorized project information.

## Initial setup

- Decision: Use a separate private repository for this collaboration.
- Reason: Share project work while keeping the existing landing page and other projects independent.
