# Project wiki

Human-readable hub for Risxhb and Chzy18. Last checked: October 20, 2026 (America/Chicago).

## Where things stand

The repository contains planning and collaboration documents. No playable build has been verified. Both of Chzy18's agents have confirmed repository and message-board access. Engine choice, local editor readiness, and the first implementation owner/reviewer still need human agreement.

The project direction is a top-down raid encounter game combining encounter mastery and roguelite builds. The eight-player plan is under review in [PR #2](https://github.com/Risxhb/collab/pull/2); its review discussion is [#3](https://github.com/Risxhb/collab/issues/3). Read proposals as proposals until agreed and recorded.

**Confirmed slice connection decision:** Hamachi, ZeroTier, or direct IP; choose the specific option later. Steam integration is not a slice prerequisite. See the [decision log](../decisions.md) and [source message](https://github.com/Risxhb/collab/issues/4#issuecomment-6284835050). Where the older plan assumes Steam invites or no port forwarding for the slice, this newer decision governs.

## Find what you need

| Question | Where to look |
|---|---|
| What is done, next, blocked, or awaiting review? | [Work tracker](work-tracker.md) |
| What have we actually decided, and why? | [Decision log](../decisions.md) |
| What are we building? | [Project planning draft](../../raid-simulator-plan.md) and [eight-player plan PR](https://github.com/Risxhb/collab/pull/2) |
| What is the proposed first implementation? | [Startup planning #4](https://github.com/Risxhb/collab/issues/4) |
| How do I join and contribute? | [Onboarding](../../COLLABORATOR-START-HERE.md) and [workflow](../workflow.md) |
| How do agents communicate? | [Board protocol](../agent-board.md) and [live Issues](https://github.com/Risxhb/collab/issues) |
| What changes are waiting for review? | [Open pull requests](https://github.com/Risxhb/collab/pulls) |

## Next human decisions

1. Agree on the engine, language, and exact editor/export version. Godot with typed GDScript is proposed; it has not been approved in this thread.
2. Confirm local setup and agree on the first combat task, its acceptance criteria, one owner, and a reviewer in #4.
3. Resolve the remaining plan review items in #3 and reconcile the plan with the slice connection decision.
4. Before internet testing, choose one of the three connection options and record setup instructions and the eight-client testing approach.

## Keep this wiki useful

- Update the tracker when work starts, changes owner, becomes blocked, enters review, or finishes. Use one owner per task; do not infer someone's availability or assignment from an agent proposal.
- Put durable decisions and their reasons in the decision log, then link them here. Keep detailed conversations in the relevant issue.
- Use branches and pull requests for wiki changes. Include a tracker update with the work PR when practical. Mark work done only after its acceptance criteria are met; use Under review while merge is pending.
- Link the task issue, PR, and validation evidence. For a handoff, record Completed / Next / Blocked in the issue.
- Update the last-checked date when refreshing the snapshot. The live issues and PRs may be newer; there is no automatic status synchronization or agent wakeup.

This wiki lives in the private repository under docs/wiki. It is available through ordinary GitHub file pages and local Markdown readers; it does not require GitHub's separate Wiki feature.
