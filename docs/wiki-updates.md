# Live project Wiki and feed maintenance

The [Last Bell Wiki](https://github.com/Risxhb/collab/wiki) is the human-facing project hub. It is live, separate from the game repository, and supersedes the earlier proposed `docs/wiki` status hub. It contains:

- [Human review feed](https://github.com/Risxhb/collab/wiki/Human-Review-Feed): meaningful game, asset and doc changes, with explicit requests for decisions, visual reviews and playtests; routine changes are FYI.
- [Agent progress feed](https://github.com/Risxhb/collab/wiki/Agent-Progress-Feed): Completed / Next / Blocked, validation evidence, ownership and handoffs.
- [Tasks/delegation](https://github.com/Risxhb/collab/wiki/Task-List), [asset development](https://github.com/Risxhb/collab/wiki/Asset-Development), decisions and acceptance gates.

**Risxhb and Chzy18 jointly own direction and acceptance**, including style, movement/combat/readability, design decisions, connection choice and tester coordination. Either can provide feedback; changes to shared direction require agreement. Code review and human feel/visual acceptance remain separate.

## Agent responsibilities

Read [the Wiki protocol](https://github.com/Risxhb/collab/wiki/Wiki-Update-Protocol) and current feeds at task start/resume. During authorized work:

1. Update progress/task status at meaningful milestones, blockers and handoffs. Include Completed / Next / Blocked, owner, links and checks actually run.
2. Post meaningful game/asset/documentation proposals and results to the human feed when reviewable. State why, evidence and whether a human action is needed; routine changes are FYI. Include gameplay previews for visual work.
3. For human decisions, give the exact question/test, options/recommendation/tradeoffs, reviewers and dependency. Do not re-request settled decisions.
4. Keep asset stages and approval provenance current. Do not call placeholders final assets or code merge human approval.
5. Update entries after review/merge/decision with outcome links. Never label branch code shipped or legacy checks Unity evidence.
6. Before handoff, link the feed update in the PR/issue. If publishing failed, report that blocker honestly and retain prepared text.

Use America/Chicago dates/times, stable feed IDs, newest-first entries and last-checked dates. Preserve historical entries; mark superseded ones. Read other lanes' updates before editing and do not invent their commitments.

Wiki repository: `https://github.com/Risxhb/collab.wiki.git`. Use an isolated clone, pull before editing/pushing, reconcile concurrent changes without force-push, and verify rendered links/navigation. Small edits can use GitHub's browser editor. Repository code/docs still go through reviewable PRs. These instructions do not start agents or grant new implementation scope.

## Visibility and freshness

Risxhb made the repository public and enabled the Wiki on October 20, 2026. Content, Wiki, Issues and PRs are publicly readable; project writing access remains with Risxhb and Chzy18. Post project facts/intended artifacts only; exclude secrets, private transcripts, personal information and excluded local research.

Feeds are maintained **during active agent work**. This setup does not install recurring polling, automatic PR imports, notifications or agent wakeups. Keep live source links so readers can inspect newer evidence. The old Markdown-wiki PR #9 and historical private-board descriptions need reconciliation with this live hub.
