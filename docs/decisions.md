# Decision log

For each decision, record the date, decision, reason, and relevant issue or pull request.

## 2026-10-20 — Boss-rush gameplay and revised delivery scope

- Decision: Keep Last Bell; three minibosses, shop intermission, larger boss; handcrafted arenas and bosses only. Three fixed starting classes, Basic/secondary/dodge/optional fourth, cursor aim and cooldowns without attack-resource costs. No block/parry/iframes. Jump/sprint/dodge share movement stamina; enemy collision, brief hit-stun and authored knockbacks are required.
- Failure: separate practice mode; run restart on solo defeat/party wipe, with co-op down/revive and increasing revive duration based on the downed player's own deaths. Interpretation/tuning defaults are explicit in the design document.
- Difficulty: four clock/lore tiers, mechanics beyond HP and an extra boss ability/mechanic on the highest. Names remain proposals. Story, healing, progression/talents, shop functionality, upgrades and equipment switching are deferred.
- Reason/source: Risxhb completed the design checklist and requested documentation/task updates and notification to Chzy18's agents. [Board #29](https://github.com/Risxhb/collab/issues/29); full [current design](game-design.md), [production plan](../raid-simulator-plan.md).
- Supersedes: auto-target/Guard/parry/counter/resource/iframe rules; spectate-only knockouts; mandatory six upgrades and two-class/two-boss/12-relic/six-modifier slice. Older source references remain historical, not acceptance requirements.
- Ownership: foundation #6; Chzy18/Codex arena #30 and network #7; Chzy18/Claude viewer #31, primary arena/content presentation and encounters #8. Existing Unity/style/solo + 2–8 decisions continue.
- Status: direction recorded; docs/implementation review and separate runtime/human evidence still required. Codex acknowledged original #30 during this update; its answered assignment is preserved and the design delta is a follow-up. Unanswered #31/#29 were rewritten; receipt of the new decisions remains unverified.

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
