# U0 independent clean-clone validation — October 21, 2026

Owner/operator: risxhb/codex / Risxhb. Review target: draft [#42](https://github.com/Risxhb/collab/pull/42), exact source **18e89ed264eb3ba2cd0f18b50dfb7eaacc6f3149**. Clean source was cloned from the freshly fetched local repository using `git clone --no-hardlinks --branch feature/run-loop`; starting status was clean and no Library, imported cache or archived build was copied. This tests fresh project import/build on an existing licensed machine, not installation on a new Windows machine or uncached package-registry availability.

## Pinned environment

- Windows 11 x64; NVIDIA GeForce RTX 3060. Unity **6000.6.6f1 (d20c9ac777da)**, ProductVersion `6000.6.6f1_d20c9ac777da`.
- Editor at `E:/Collab/.unity-tools/6000.6.6f1/Editor/Unity.exe`; WindowsStandaloneSupport installed. Licensing entitlement resolved successfully. No engine/package/module selection was changed.
- Manifest: glTFast 6.20.0, Input System 1.20.0, URP 17.6.0, UGUI 2.6.0, Rider 3.0.40, Visual Studio 2.0.28, vectorgraphics module 1.0.0. Committed lock includes Unity Test Framework 1.8.0 and Performance 6.6.0 transitively. Mono Windows player built using the supplied project settings.
- Scene: `Assets/LastBell/Run/RunArena.unity`; build helper `LastBell.Run.EditorTools.RunBuild.BuildWindows` rebuilds the scene/materials from the committed arena before building. Unity's editor/build helper regenerated scene/settings assets inside the disposable clone; those changes are not pushed. Runtime C# and package manifest/lock remain the tested source. Preserve the original source checkout untouched.

## Reproduction commands

Use absolute paths and allow each process to finish. `Start-Process -PassThru -Wait` captures a GUI application's exit status; `& Unity.exe` alone may return before the editor finishes. Do not start a second editor against an already-open validation project.

```powershell
$editor = 'E:/Collab/.unity-tools/6000.6.6f1/Editor/Unity.exe'
$project = 'E:/Collab/validation-run-loop/unity'
Start-Process $editor -ArgumentList "-batchmode -projectPath $project -executeMethod LastBell.Run.EditorTools.RunBuild.BuildWindows -logFile E:/Collab/foundation-evidence/windows-build.log" -WindowStyle Hidden -Wait
Start-Process "$project/Builds/Windows/LastBell.exe" -ArgumentList '-lastbellAutotest -lastbellOut E:/Collab/foundation-evidence/player-visible -logFile E:/Collab/foundation-evidence/player-visible.log -screen-width 1600 -screen-height 900 -screen-fullscreen 0' -Wait
Start-Process $editor -ArgumentList "-projectPath $project -executeMethod LastBell.Run.EditorTools.RunBuild.PlayModeAutotest -lastbellAutotest -lastbellOut E:/Collab/foundation-evidence/editor -logFile E:/Collab/foundation-evidence/editor-playmode.log" -WindowStyle Hidden -Wait
```

The visible player window is intentional: a hidden player window does not supply valid drawing frames for this capture-based suite. Its first hidden run passed 106 gameplay assertions but failed the final log check with 1,984 ReadPixels errors and black captures. It is retained as a failed run, not represented as green or evidence of broken game logic. A visible-window run follows. This limitation matters to any future unattended CI runner.

## Results

Windows build: **Succeeded, 0 errors, 0 warnings**, 500 MB, **00:03:11.8778070**. Fresh import/script compilation and player construction completed. Unity shutdown separately reports JobTempAlloc/remaining-allocation diagnostics; the BuildReport's zero warnings is not a performance/leak acceptance claim.

| Check | Actual result | Interpretation |
| --- | --- | --- |
| Clean Windows build | Succeeded, 0 errors / 0 warnings; 500 MB; 3:11.8778070 | Fresh import/compilation and Windows support validated on this licensed machine. |
| [Visible Windows player](evidence/player-autotest.txt) | **107 passed / 0 failed, 0 errors; exit 0** | Independent current-inventory scripted gameplay/build smoke check passed. [Rendered gameplay capture](evidence/player-combat.png) was visually inspected. |
| [Editor Play Mode](evidence/editor-autotest.txt) | **106 passed / 1 failed, 1 logged exception; exit 1** | All gameplay checks passed, but log cleanliness failed due `UnityEditor.Search.SearchDatabase.EnumerateAll` / `GetDefaultSearchDatabase` startup `ArgumentOutOfRangeException`. This matches the earlier author-described engine issue. It is not silently waived as green. |
| [Hidden Windows player](evidence/player-hidden-autotest.txt) | 106 passed / 1 failed, 1,984 capture errors; exit 1 | Invalid capture context/black frames. Superseded for player validation by the successful visible run; retained failed evidence. |
| [Targeted Unity diagnostic probe](evidence/review-probe.txt) | All four expected defects reproduced; diagnostic exit 0 | Projectile lethal dispatch throws; dash 3.2500 m/13 steps; capped echo amplifies 20→30→45; buffered press survives stun past TTL. Exit 0 means reproduction succeeded, not game acceptance. |
| U3 proposal helpers | 32 passed; C# 7.3 build 0 warnings/errors; pinned Unity API-profile compile exit 0 | See [contract validation](../contracts/validation.md). No full authority/restore/geometry/transport implementation or consumer adoption. |

**U0 status: partially validated.** Pinned packages/editor, clean import, Windows build and packaged player are independently validated. Actual Editor Play Mode executes all gameplay checks, but its zero-error acceptance gate is **blocked by the Unity SearchDatabase exception**. No package/editor change or undocumented waiver was made. The content/shared-rule review remains changes-requested independently of that engine issue.

The current test inventory is **107**, not the older task-list snapshot's 108; the current committed README/build15 report documents that old-code removal changed the inventory. A green smoke/kit suite does not exercise every normal killing-blow path, encounter schedule, difficulty, input device or networking feature. The four vulnerabilities were exercised through the actual Unity gameplay classes by [this diagnostic-only script](fixtures/FoundationReviewProbe.cs), added only after both baseline build and Play Mode execution. It uses manual ticks/reflection to isolate rule boundaries, not human keyboard play. The packaged player was already built and remains free of the probe.

To reproduce the probe, copy its single source file into a disposable project's `Assets/`, then launch the pinned editor with `-projectPath <disposable-unity> -executeMethod FoundationReviewProbeLauncher.Play -foundationReviewProbe -foundationProbeOut <report-file> -logFile <log-file>` and omit `-quit`. The script opens committed RunArena, pauses the automatic director, exercises real world/player/kit methods and writes the four outcomes. No Run source or asset modification is needed. Do not import the diagnostic script into production.

## Limits and evidence

Local raw logs/reports/captures: `E:/Collab/foundation-evidence/`. Lightweight final reports and reproduction evidence are committed alongside this report; raw imported Library/build output and thousands of video frames are not committed. Gameplay-camera captures must be inspected rather than assuming file existence proves rendering.

Human feel/readability/final-art acceptance, complete wall/stamina/fairness checks, performance/soak, gamepad/remapping, 2–8 players, Internet and adopted U3 authority remain separate gates. The [source review](run-loop-42.md) requests changes for real defects even if the baseline scripted suite passes. No code fix to another owner's run/content files was made.
