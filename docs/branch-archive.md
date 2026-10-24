# Archived branches

On October 24, 2026 the old open pull requests were closed and their branches deleted. Everything still useful moved into `docs/` on the main player. Each old branch is kept as a tag, so nothing is lost:

```bash
git fetch origin --tags
git switch -c <new-branch> archive/<tag-name>
```

| Tag | Old PR | What it was | Where it went |
|---|---|---|---|
| `archive/feature-combat-foundation` | #10 | First Godot combat prototype (Vanguard) | Retired: Unity/C# replaced Godot |
| `archive/feature-combat-roster` | #11 | Godot party roster and combat actors | Retired with #10 |
| `archive/feature-hazard-lock-timing` | #12 | Godot player-anchored hazards and lock timing | Retired with #10; the lock-timing idea is in the [architecture](architecture.md) |
| `archive/docs-2-5d-isometric-direction` | #13 | Unity decision and 2.5D visual direction | `docs/plans/` (newer copies from #48) |
| `archive/docs-boss-rush-decisions` | #33 | Boss-rush design and the one-arena slice plan | `docs/game-design.md`, `docs/decisions.md`, `docs/plans/single-arena-slice.md` |
| `archive/docs-wiki-coordination-rules` | #39 | Wiki/board check rules for agents | Replaced by the simplified wiki update rules |
| `archive/docs-foundation-u3-review-20261021` | #48 | Typed U3 contract proposal and run-loop review evidence | `docs/contracts/` (proposal); review evidence stays in the tag |
| `archive/docs-live-wiki-workflow` | #18 | Wiki review/progress feed workflow | Replaced by the simplified wiki |
| `archive/docs-bellkeeper-design-reference` | #17 | Supplied Bellkeeper boss design v1.0 | `reference/LAST-BELL-BELLKEEPER-BOSS-DESIGN-v1.0.md` |
| `archive/docs-project-wiki` | #9 | In-repo copy of the wiki | Replaced by the GitHub wiki |
| `archive/docs-eight-player-online-coop` | #2 | 2–8 player online co-op requirements | Merged into `raid-simulator-plan.md` |

Earlier on October 24, `feature/main-menu` (#66) and `feature/lastbell-web-demo` (#71) were deleted. They were already fully contained in `feature/confrontation-menu` (#72), so they have no tag.
