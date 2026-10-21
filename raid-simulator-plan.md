# Last Bell — boss-rush production plan

Updated October 20, 2026. [Current game design](docs/game-design.md) records Risxhb's completed decisions in [#29](https://github.com/Risxhb/collab/issues/29) and supersedes the old raid/roguelite draft, retained in Git history. Read [architecture](docs/architecture.md) and [AGENTS.md](AGENTS.md).

## Game and combat

Last Bell is a real-time boss rush: **three minibosses → shop intermission → larger boss**, in handcrafted clockwork-fantasy arenas. Solo and 2–8 total online, fixed characters, three starting classes. Shop functionality is later work.

Mouse-cursor aim for melee/ranged/magic and rotation-like combos. Basic, secondary, dodge and an optional fourth ability. Cooldowns; no attack-resource costs, auto-target, block/parry or invincibility frames. Jump, limited sprint and dodge share movement stamina. Enemy collision, brief hit-stun and authored knockbacks make mistakes consequential.

Death ends the run; co-op downed players can be revived with increasing duration based on that player's own downs. Solo defeat/party wipe restarts the run. Practice is separate with full reproducible resets. Four clock-themed difficulties scale mechanics beyond HP; highest adds a boss ability/mechanic.

## Delivery stages

The near-term complete-run target is now the [single-arena slice](docs/plans/single-arena-slice.md): three distinct minibosses, a future-shop intermission and the final Bellkeeper, sequentially in one reusable combat arena. First prove four encounter slots/start/clear/failure/victory, then replace stand-in fights with authored bosses. One-class/one-tier builds are internal increments, not full slice acceptance.

| Stage | Deliverable | Evidence |
| --- | --- | --- |
| U0 | Pinned Unity editor/packages/pipeline and project/build | Clean checkout, minimal Play Mode and Windows build |
| U1/U3 | Shared rules and typed command/state/event/reset contract | Aim, cooldown, stamina, jump/dodge/collision, damage/stun/knockback and down/revive/run lifecycle; standalone checks plus Unity compatibility |
| Arena #30 / viewer #31 | Solo class-testing arena and model viewer | Unity compile/Play Mode, gameplay preview, practice reset, accurate model/import labels, Windows build and guide |
| First fight | Reconciled Bellkeeper working spec | Finite-stamina/no-iframe escape, shared geometry, truthful records, 1–8 assignments; human feel/readability |
| Session #7 | Unity host plus seven clients | Ownership/press deduplication, events/full checkpoints, down/revive/disconnect/host loss; latency and separate-network checks |
| Run content | Three minibosses, intermission and larger boss; three starting classes | Run/practice boundaries, party wipe, per-player revival, every count |
| Difficulty | Four clock-themed tiers | Mechanical scaling, top-tier new boss mechanic, feasibility and human tests |
| Later systems | Shop/weapons, upgrades, progression/talents, healing | Separate decisions and work; not arena prerequisites |

These are staged goals, not completed work or calendar estimates. Retire old two-class/two-boss/12-relic/six-modifier and mandatory six-upgrade gates. Old effort estimates no longer describe this scope; re-estimate after the arena, contract and boss classification are reviewed.

## Ownership and integration

Foundation risxhb/codex owns shared project/packages/rules/contracts and integration (#6). Chzy18/Codex owns arena delivery (#30) and networking (#7). Chzy18/Claude owns viewer (#31), primary arena/environment/character content and encounters (#8). Agree shared scene/menu seams; do not duplicate authority.

Unity/C# is production, with scene-independent shared C# rules. Keep modeled 2.5D/fixed isometric presentation and planar navigation with explicit jump state. [Adopted art guide](https://github.com/Risxhb/collab/blob/docs/2-5d-isometric-direction/reference/LAST-BELL-CLOCKWORK-FANTASY-ART-DIRECTION.md) still applies. Browser #27 and Godot are experimental/historical. Preserve old diagnostic evidence; Guard/resource/iframe behaviors are not port requirements. Complete or explicitly hand off the current AP/AR experiment, then map reusable delivery behavior to current abilities.

## Validation and open work

Test cursor/plane mapping, no auto-target fallback, collision/jump rules, cooldowns without attack resources, finite stamina, no dash/post-hit immunity, stun/knockback without unavoidable chains, and distinct hits versus duplicate event delivery. Verify complete practice/fresh-run resets and per-player revive escalation; down/disconnect cannot leave impossible assignments.

Check all counts 1–8, duplicate classes, four difficulty tiers and eight-actor visual density. Human tests establish feel/readability; Unity, Internet, wall-clock soak and performance each need their own evidence.

Healing, progression/talents, equipment switching, shop economy, final story and difficulty names remain future decisions. Four slots versus keys 1–5 needs mapping; key 5 is provisionally reserved. Movement defaults and other explicit proposals are in the design document. Windows-first, replaceable transport, host-loss termination and no host migration continue. Exact Unity toolchain, hardware and Hamachi/ZeroTier/direct-IP test setup remain technical/test decisions.
