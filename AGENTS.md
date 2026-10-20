# Last Bell: instructions for every coding agent

These instructions apply to Codex, Claude and every other model contributing to this repository. Read [the architecture policy](docs/architecture.md), [decisions](docs/decisions.md), [workflow](docs/workflow.md) and your current lane issue before changing files. Follow direct human instructions and report conflicts with older plans.

## Required architecture

- Unity with C# is the only production game implementation. Preserve modeled 2.5D visuals, fixed isometric framing, planar authoritative combat and solo plus 2–8 total players.
- Put reusable gameplay rules in scene-independent C#. Unity and standalone C# tests must consume the same adopted rules; avoid separate production implementations of damage, geometry, cooldowns or action eligibility.
- Unity owns production scenes, presentation, controls, UI and network integration. Rendering and animation consume authoritative state; they cannot independently decide combat results.
- Keep browser prototypes optional and experimental. Keep Godot as a historical behavior/test reference. Neither is a second production runtime or proof of Unity acceptance.
- Read the adopted [art guide](https://github.com/Risxhb/collab/blob/docs/2-5d-isometric-direction/reference/LAST-BELL-CLOCKWORK-FANTASY-ART-DIRECTION.md) for visual work; it currently lives on the migration branch. Retain the original clockwork-fantasy direction.

## Ownership and integration

- Foundation, risxhb/codex (#6): Unity editor/packages/project wiring, shared C# contracts, combat rules, integration, UI and builds.
- Networking, chzy18/codex (#7): session adapters, transport experiments and diagnostics against the agreed contract.
- Encounters, chzy18/claude (#8): encounter content, authoring/validation and experimental character/environment work; coordinate adopted gameplay with foundation.
- Pin Unity editor, compatible packages, render pipeline and Windows build prerequisites through U0. Agree the command/state/event/reset contract through U3 before production binding. Current diagnostic TCP is not a selected production transport.
- PR #27 stays an experimental prototype pending review. This architecture decision does not adopt Vanguard 2.0, Dash, Bellkeeper v1.0 values or proposed balance changes automatically.

## Delivery and evidence

Use branches and reviewable PRs. Read the relevant board thread before posting an authorized project update; link this policy and record Completed / Next / Blocked. Other agents act under their own operators' instructions; a post does not start them or prove acknowledgment.

State whether checks exercised standalone C#, browser, Godot, Unity, loopback or Internet play. Only evidence from the claimed runtime establishes that runtime's acceptance. Follow [the architecture review checklist](docs/architecture.md#review-checklist).

Public collaboration in Risxhb/collab was approved on October 20, 2026, superseding historical private-only onboarding. Keep posts limited to authorized project information; never disclose credentials or private conversations. Repository instructions do not independently authorize messaging, deployment, access changes or unrelated repository work.
