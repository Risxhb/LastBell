# The Bellkeeper — ability set v2: morning report

**Status:** designed, built and tested by autotest. **Not human-approved:** nothing is approved until Chzy18 plays it. Everything here is a proposal (`ABILITIES.md`).

Integration branch: `feature/bellkeeper-abilities-v2`, which is `feature/run-loop` plus the design branch, the scaffolding and the merged abilities. GitHub accepted pushes up to `128ecb7`. After that it answered 403 "Your account is suspended", so later commits are **local only** (every worktree shares one repository). The draft PR into `feature/run-loop` is waiting for push access.

## How to play it

Build: `tools/bellkeeper-v2/build-and-test.sh <name>` in `bellkeeper-abilities-v2`, or Unity's normal Windows build. Then:

```bat
unity\Builds\Windows\LastBell.exe
```

- **Practice slot 1, "Bellkeeper"**, now runs set v2 with the full phase plan, on the Shattered Hour map (Throne of the Final Toll). Pick a difficulty on the menu; Hard adds The Unfinished Hour.
- One ability alone: `LastBell.exe -lastbellAbility toll_tide`. Keys: `leaning_bell`, `striking_order`, `reflection`, `toll_tide`, `unfinished_hour`, or `01`–`05`. The Unfinished Hour also needs `-lastbellDifficulty hard`.
- Co-op shapes with stand-in players: `-lastbellBots 4` (3 scripted players who dodge).
- Old set for comparison: `-lastbellAbilitySet v1` (old moves, practice on the current map).
- Dev keys as before: F8 jumps to the next phase, F9 defeats the boss.

## Abilities

| # | Ability | Ability session | State | Own checks | Evidence |
|---|---|---|---|---|---|
| 01 | Leaning Bell | Leaning Bell | merged | 44/44 | `bellkeeper-v2-evidence/leaning-bell-2` |
| 02 | Striking Order | Striking Order | merged | 30/30 | `bellkeeper-v2-evidence/striking-order-1` |
| 03 | Reflection of the Hour | Reflection of the Hour | merged | 47/47 | `bellkeeper-v2-evidence/reflection-1` |
| 04 | The Last Toll Tide | The Last Toll Tide | merged | 77/77 | `bellkeeper-v2-evidence/toll-tide-2` |
| 05 | The Unfinished Hour (Hard) | The Unfinished Hour | merged | 27/27 | `bellkeeper-v2-evidence/unfinished-hour-2` |

Evidence folders are in `C:\Users\BS_Ch\Documents\Codex\last bell\` (outside git: screenshots and MP4 clips per ability).

## Full autotest (every run-loop check plus the v2 section)

| Run | Contents | Result |
|---|---|---|
| `abilities-1` | scaffold + 5 stubs, v2 section only | 29/30 (test bug, fixed) |
| `abilities-full-1` | + Reflection, Toll Tide, Striking Order, Unfinished Hour | **427/429**. Two test bugs fixed in `e76b52d`: a timing-fragile v1 resume check, and my practice-hook check not allowing for The Unfinished Hour's Hard-only lead-in |
| `abilities-full-2` | + those fixes | **429/429** |
| `abilities-full-3` | + Leaning Bell: **all five abilities** | **474/474, 0 errors** (includes Hard: The Unfinished Hour replays a real Leaning Bell; max two damaging patterns over the whole plan) |

## Escape check

`tools/encounter-escape/bellkeeper-v2-escape-check.ps1`: every Medium window passes at 4.2 and 6 m/s on the Throne and on the test map (`bellkeeper-v2-results.md`). Leaning Bell needs one dash at 4.2 m/s from the far wall; that is deliberate, because the far edge is where ranged players get punished.

## What the Abilities session added

- **Set switch and practice hook:** v2 is the default; `-lastbellAbilitySet v1`; `-lastbellAbility <key>`; `-lastbellBots N`.
- **Scheduler and phase plan** (`BellkeeperV2.cs`), following ABILITIES.md §5:
  - The scheduler runs two abilities side by side.
  - Each phase follows the order in §5, and every Toll Tide is followed by the 3.5 s punish window.
  - Phase transitions wait for the current move to finish and are harmless: no heal, and only one transition per burst of damage.
  - The second form starts at 35 %.
  - Hard adds The Unfinished Hour from phase 2.
- **Boss HP** scales with the party (3600 × (1 + 0.7 (N − 1))).
- **Scripted extra players** for the N = 2/4/8 checks.
- **Test harness:** `-lastbellBellkeeperV2` runs only the v2 checks; `-lastbellTestAbility <key>` runs one ability's checks.
- **Locked build script:** `tools/bellkeeper-v2/build-and-test.sh`.

**Shared-code changes to review:**
- `BossEncounter.OnHalt()` hook.
- `RunDirector.BossEntry.Map` can depend on a setting (the Bellkeeper's practice map).
- From The Unfinished Hour session (reviewed): `BellkeeperAbility.OnBind()`, and clearing the remembered halves on a phase change.
- One test-setup line in the v1 run-loop checks (`RunAutoTest.cs`).

## Conflicts and open questions

- Open questions 1–7: `ABILITIES.md` §10. Question 4 (the tide's inward push) is answered: Chzy18 said "yes for now" (provisional), and it is recorded in `ABILITIES.md` and the Toll Tide brief.
- Each ability session's own decisions are listed in its notes (`side-session-notes/.../*.md`). For example, Striking Order gives I–IV to 3 players with the first player taking I and IV.
- **The Unity lock is a bottleneck.** With four boss trees on one PC, builds waited up to 3 hours. The lock is a plain retry loop, not a queue.

## Next

- Nothing left to merge. All five abilities are complete (built and tested), and none is human-approved.
- Push and open the draft PR into `feature/run-loop` once GitHub accepts pushes.
- Chzy18 plays it.

## Notes on the evidence

- The latest full run's screenshots and clips are in `bellkeeper-v2-evidence/abilities-full-3/`: `38_…`–`48_bk_*.png` and `bk_*.mp4`. Each ability's own run folder has its clip too.
- The screenshots show the pause menu over the floor, because the harness pauses the game to step it tick by tick. The clips are clean.
- The `Error : EXCEPTION_ACCESS_VIOLATION` line in the build script's output comes from Blender crashing at the end of the frames-to-MP4 step, not from the game; the game exits normally. That step sometimes loses the last clip (`vanguard_kit` in run 2).

## Extra open questions from the ability sessions

- **Leaning Bell, direction of the dividing line:** the brief doesn't say. The session made it perpendicular to the line from the centre to the boss's target (the nearest standing player). The side with more standing players is hit; a tie goes to the target's side.
- **Toll Tide on the square test map:** "bound + 2 m" doesn't reach the corners, so the outer radius there is the corner distance + 2 m.
- **Striking Order:** with 3 players, I–IV go to them with the first player taking I and IV. Easy ring I comes at 3.75 s.
