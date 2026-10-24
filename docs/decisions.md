# Decision log

For each decision, record the date, decision, reason, and relevant issue or pull request.

## 2026-10-21 — Reconciled class, dodge and recovery decisions

- **Decided:** no dodge/post-hit immunity; per-instance hit deduplication. Fresh runs start full, health refills before each subsequent boss, potions carry between minibosses, and both refill before the area final boss. Source: Chzy18's decision and #42 `359f5b3`, recorded in [wiki Updates](https://github.com/Risxhb/collab/wiki/Updates).
- **Approved class exceptions:** [Vanguard #31](https://github.com/Risxhb/collab/issues/31#issuecomment-6306130658) Brace is partial frontal mitigation/knockback prevention; Opening is non-stacking. [Chimesinger #36](https://github.com/Risxhb/collab/issues/36) is DPS with Harmony/bells/echoes, instant self-inclusive ally healing and separate dash. [Arcanist #41](https://github.com/Risxhb/collab/issues/41) uses Ticks, independent seals/inscriptions and Anchor return/shield. Three-class roster is Vanguard/Chimesinger/Arcanist; Healer placeholder retired. All numerical balance remains prototype tuning.
- **Supersedes:** October 20 blanket slot/resource/block/healing deferrals only where those approved exceptions apply, and carry-health/no-potion assumptions. No parry, iframes or old Vanguard counter/resource loops are restored.
- **Scope/reference:** #44 delivers four-area/13-encounter reference; #47 delivers Aurelion, Crucivar, Sylvara and Bellkeeper concepts. Both remain review input; no production adoption/expansion or replacement of the current one-arena slice is inferred.
- **Status:** #42 is draft; reported PF-02–PF-06 implementations and PF-07 first pass await human review. Latest handoff at `18e89ed` reports 107/107 (build 15), versus historical 108/108 before old-code removal. These author results do not establish independent runtime or human acceptance.
- **Next:** use [playtest and decision queue](https://github.com/Risxhb/collab/wiki/Playtest-and-Decisions) for D1/D2/D4/D5/D6, meter depth and boss concepts. D3's no-immunity/potion policy is settled; numeric tuning remains open.

## 2026-10-20 — Boss-rush gameplay and revised delivery scope

Historical record: uniform slot/no-block/no-resource/healing restrictions are superseded only by the October 21 approved exceptions above.

- Follow-up scope: Risxhb requested a plan for a playable three-miniboss/final-boss sequence entirely in one arena. [Single-arena slice plan](plans/single-arena-slice.md) proposes the encounter roster, reusable scene, run-state flow, future-shop intermission, failure/practice behavior and staged gates. Names/numbers/new balancing policies are planning proposals, not implemented or human-approved balance.

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
