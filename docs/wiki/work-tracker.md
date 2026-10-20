# Work tracker

[Wiki home](README.md) · [Decisions](../decisions.md) · [Live issues](https://github.com/Risxhb/collab/issues) · [Open PRs](https://github.com/Risxhb/collab/pulls)

Snapshot: October 20, 2026 (America/Chicago). Status is maintained manually. Unassigned means the humans have not agreed on an owner or reviewer.

## Completed setup and decisions

| Item | Evidence | Notes |
|---|---|---|
| Private collaboration repository and board | [Welcome #1](https://github.com/Risxhb/collab/issues/1) | Repository access is confirmed; local editor/build setup is not yet verified. |
| Chzy18 Codex and Claude connection checks | [Codex reply](https://github.com/Risxhb/collab/issues/1#issuecomment-6284472147), [Claude reply](https://github.com/Risxhb/collab/issues/1#issuecomment-6284609615) | Both can read project documents and post to the board. |
| Playable-slice connection approach | [Owner decision](https://github.com/Risxhb/collab/issues/4#issuecomment-6284835050) | Hamachi, ZeroTier, or direct IP; exact choice deferred. |
| First combat task recommendation received | [Chzy18 Codex proposal](https://github.com/Risxhb/collab/issues/4#issuecomment-6284810574) | Recommendation received; implementation is not assigned or started by this proposal. |

## Active coordination and review

| Work | Status | Owner / reviewer | Next action or blocker | Source |
|---|---|---|---|---|
| Eight-player online co-op plan | Under review | PR author: Risxhb; review feedback: chzy18/claude | Resolve review points and distinguish slice connectivity from later Steam/public-release requirements. | [PR #2](https://github.com/Risxhb/collab/pull/2), [review #3](https://github.com/Risxhb/collab/issues/3) |
| Toolchain and first-task agreement | Awaiting human decisions | Decision participants: Risxhb and Chzy18; implementation owner/reviewer unassigned | Agree on engine/version, readiness, task scope, owner, reviewer, and acceptance criteria. | [Startup #4](https://github.com/Risxhb/collab/issues/4) |
| Establish wiki and record slice decision | Under review once its documentation PR opens | Documentation owner: risxhb/codex; reviewer unassigned | Review the linked documentation PR before merging. | [Task #5](https://github.com/Risxhb/collab/issues/5), [open PRs](https://github.com/Risxhb/collab/pulls) |

## Proposed implementation queue

These are planning increments, not approved assignments or dates.

| Proposed increment | Owner / reviewer | Ready when | Expected evidence |
|---|---|---|---|
| First combat experiment: one arena, Vanguard, cone/donut sequence, four actions, guard/damage, restart | Unassigned / unassigned | Humans agree on toolchain and the detailed criteria in #4 | Fresh-checkout launch instructions, focused combat checks, and a manual readability/responsiveness playtest |
| Expand combat milestone: third mechanic, one Echo, six upgrades, richer failure explanation | Unassigned / unassigned | Initial combat experiment is reviewed and scope is agreed | Repeatable fight, complete resets, understandable failures, meaningful upgrade effects |
| Networking experiment: two clients, then eight across separate networks | Unassigned / unassigned | Choose Hamachi, ZeroTier, or direct IP; agree on client testing approach and acceptance criteria | Connection setup, consistent attack outcomes, latency/loss results, disconnect behavior, capacity measurements |
| Complete playable slice | Unassigned / unassigned | Combat/networking experiments support the agreed scope | Playtestable mini-run and evidence against the updated plan's gates |

## Open questions to carry forward

- **Toolchain:** Godot with typed GDScript and a pinned version was recommended in #4. Confirm the actual version when adopting it; the wiki does not approve an engine or guarantee the recommendation is current.
- **Human readiness and roles:** Chzy18's local clone, editor/export setup, experience, availability, and implementation ownership need direct confirmation.
- **Slice connection:** Exact provider is deferred. Check participant/device limits and host reachability before choosing; direct IP may require port forwarding and a reachable host.
- **Plan review:** Shared knockout/disconnect assignment rules, party-size authoring, eight-client testing, and reward-pick timing remain review topics in #3. Do not treat review suggestions as accepted decisions.
- **Public release:** Connection service, Steam integration, and seamless invites remain undecided by the slice connection decision.

## Adding a task

Create or link an issue with the desired result and acceptance criteria. Add one row with status, one agreed owner, reviewer, next action/blocker, and source links. Use Proposed, Ready, In progress, Blocked, Under review, or Done. Add a completion row with the merged PR and validation when finished. No implementation is considered assigned solely because it appears in this queue.
