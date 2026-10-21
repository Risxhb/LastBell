# Last Bell: instructions for every coding agent

These instructions apply to Codex, Claude and every other model contributing to this repository. Read [current game design](docs/game-design.md), [architecture](docs/architecture.md), [decisions](docs/decisions.md), [workflow](docs/workflow.md) and your current task before changing files. Current human decisions override old plans/prototypes; report conflicts.

## Current gameplay and tasks

- Last Bell is a boss rush: three minibosses, shop intermission, larger boss; handcrafted arenas, bosses only. Shop implementation, upgrades, talents/progression and healing are deferred.
- Three fixed starting classes; Basic, secondary, dodge and an optional fourth slot. Cursor-aim every ability; cooldowns without attack-resource costs. No auto-target, block/parry or iframes. Movement stamina covers jump, limited sprint and dodge/dash. Support enemy collision, brief hit-stun and knockback.
- Practice is separate from the run. Solo defeat/party wipe restarts the run; co-op down/revive uses that player's own escalating down count. Four clock-themed difficulties; top tier adds a boss mechanic. Read explicit defaults/proposals and unresolved details in the design document.
- Chzy18/Codex owns playable arena #30 and networking #7. Chzy18/Claude owns viewer #31, primarily authors handcrafted arena/character/environment content, and retains encounters #8. Coordinate scene/menu and shared-file seams with foundation #6.
- Old Guard/Resonance/counter/iframe and six-upgrade/12-relic slice requirements are historical. Preserve useful evidence; do not port rejected mechanics as parity requirements.

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
- PR #27 stays experimental. Adopt only behavior matching current design; dash is required but its old iframes, counters and resource costs are rejected. Boss values, buffering and other balance details still require explicit review.

## Delivery and evidence

Use branches and reviewable PRs. Read the relevant board thread before posting an authorized project update; link this policy and record Completed / Next / Blocked. Other agents act under their own operators' instructions; a post does not start them or prove acknowledgment.

State whether checks exercised standalone C#, browser, Godot, Unity, loopback or Internet play. Only evidence from the claimed runtime establishes that runtime's acceptance. Follow [the architecture review checklist](docs/architecture.md#review-checklist).

Public collaboration in Risxhb/collab was approved on October 20, 2026, superseding historical private-only onboarding. Keep posts limited to authorized project information; never disclose credentials or private conversations. Repository instructions do not independently authorize messaging, deployment, access changes or unrelated repository work.
