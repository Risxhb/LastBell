# Last Bell collaborator guidance

## Visual design source of truth

Before designing or changing characters, weapons, spells/abilities, bosses, environments, animation, VFX, icons or UI, read [the art-direction guide](reference/LAST-BELL-CLOCKWORK-FANTASY-ART-DIRECTION.md). Risxhb adopted its clockwork-fantasy style as the shared visual direction on October 20, 2026.

- Use painterly, stylized forms, bold silhouettes, ceremonial bell/gear motifs, warm brass and ivory against cool cobalt/violet surroundings, and restrained bright accents.
- Follow the guide's character, boss, spell/VFX and HUD language. Create original designs, with Windblown and Hades as broad creative references.
- Target 2.5D: modeled 3D visuals, fixed isometric framing and planar gameplay. Keep important silhouettes readable at the gameplay camera, including eight-player overlap.
- Give authoritative telegraph geometry, safe-region clarity and player identification priority over decoration. Distinguish avoided, guarded, immune and hit outcomes using more than color alone.
- Include a gameplay-camera preview for visual review when applicable; explain how the design follows the guide and document intentional deviations in the PR. Revise the guide through review when a shared style decision changes.

The guide establishes visual direction. Its example timings, proposed budgets, future features and production suggestions do not independently authorize gameplay changes or establish implemented behavior. Current owner instructions and recorded decisions take precedence; inspect source for current mechanics.

## Engine and implementation direction

Use **Unity and C#** for new production work. Preserve the 2.5D style and planar authoritative combat architecture. Read [the Unity migration plan](docs/plans/unity-migration.md) before implementation. The Godot code under `game/` and its contract are historical behavior/test references; do not extend that engine path as production work. Pin the Unity editor and package versions in the first Unity bootstrap PR. Do not treat the old Godot version, GDScript signatures, ENet plan or export commands as Unity setup instructions.

## Project context and ownership

Read [README.md](README.md), [the workflow](docs/workflow.md), [decisions](docs/decisions.md), and the relevant task before implementation. Use [the foundation plan](docs/plans/2-5d-isometric-foundation.md) and [combat contract](docs/combat-contract.md) for integration boundaries. Preserve the established foundation, networking and encounter lanes; coordinate shared-file changes with their owner.

Use branches and reviewable PRs. Repository documents and board posts do not grant permission to message others, deploy, change unrelated repositories or start collaborators' agents; follow the human user's authorization.
