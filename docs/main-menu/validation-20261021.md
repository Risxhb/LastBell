# Main menu handoff — 2026-10-21

## Completed

- Human visually approved the revised presentation (“looks good”) after the shared movement was replaced with independent actor routes and planted casts.
- Vanguard, Arcanist, Chime and Bellkeeper use existing character presentation and spell effects in a presentation-only fight. Camera slowly orbits the arena.
- Supplied logo and main menu music are integrated; Settings includes a persistent live music volume control.
- The normal workspace `Play-LastBell.cmd` now prefers the menu build. `Open-LastBell.cmd` opens the menu scene.
- Windows build succeeded: `E:\Collab\build-evidence\menu-20261021\Build-isolated.log` (zero errors, 22 warnings).
- Latest player and Editor smoke runs each logged 28 passing checks, including input, Settings, music, layouts, spell rendering and initial orbit capture. No failing check or runtime exception was found in those logs.

## Next

- Complete the expanded five-loop soak and final start/return checks on this revision. Both latest test processes exited normally before producing a final report, so those checks remain unverified. This is a partial run, not a full-suite pass. Earlier reports belong to earlier revisions.
- Chzy18 review requested for `feature/main-menu`, targeting `feature/run-loop`. The build used base `4a749fd`; upstream subsequently added Bellkeeper phase appearances at `ac9791a`. The combined result needs integration validation.

## Blocked

- No blocker to local playback. Risxhb authorized Git, wiki publication and a Chzy18 review request. No merge or production acceptance is claimed.

## Evidence

Latest logs and screenshots are under `E:\Collab\build-evidence\menu-20261021\Player-final-isolated` and `Editor-final-isolated`, with their adjacent `.log` files. Preexisting checkout modifications are recorded in `pre-menu-local.patch` and `pre-menu-status.txt` and were preserved. Unity's QualitySettings and ProjectSettings files currently have no substantive Git diff from their tracked contents.

## Published review evidence

[Party spell preview](media/party-spell-release.png) and [Settings preview](media/settings.png) are captured from the latest player. [Smoke-check excerpts](smoke-check-excerpts.txt) contain the actual latest checks from both runtimes. Player startup also reports stripped/unsupported URP post-processing shaders; these are retained as a rendering limitation rather than a clean-log claim. Full raw logs remain in the local evidence directory described above.
