# Crucivar ability set v2: overnight build plan

This is Chzy18's Part C and "Rules for every session", as given to the boss sessions on 2026-10-22, plus the Crucivar specifics. Every session in the Crucivar tree reads this file.

## Crucivar specifics

- **Design:** `ABILITIES.md` plus one brief per ability (`01`–`05`), in this folder.
- **Escape check:** `tools/encounter-escape/crucivar-escape-check.ps1 -Level Easy|Medium|Hard`. Results are in `escape-*.md`; 13/13 pass on Medium.
- **Branches:**
  - Design: `design/crucivar-abilities-v2`.
  - Integration: `feature/crucivar-abilities-v2` (owned by the Abilities session, cut from the design branch).
  - Ability branches: `feature/crucivar-ability-<id>`, cut from the integration branch. `<id>` is `mould_press`, `gear_chain`, `bellows_draw`, `great_casting` or `second_press`.
- **Current Crucivar code** (on `feature/run-loop` since `62acf88`, and on `feature/boss-crucivar`):
  - `unity/Assets/LastBell/Run/Encounters/CrucivarEncounter.cs` (v1 attacks)
  - `CrucivarRig.cs` (vents, flywheel, insulation, fins, pour)
  - `VFX/Boss/Crucivar/CrucivarFx.cs`
  - `Run/CrucivarAutoTest.cs`
  - The practice entry is `RunDirector.Bosses` "Crucivar (area 2)" (`inRun: false`, `map: "brass-foundry"`).
- **Where new code goes:** `unity/Assets/LastBell/Run/Encounters/Crucivar/Abilities/<AbilityId>.cs`, one class per ability. The v2 schedule wiring and the `-lastbellAbilitySet v1` switch belong to the Abilities session.
- **Tests:**
  - **Crucivar autotest:**
    ```
    Unity.exe -projectPath unity -executeMethod LastBell.Run.EditorTools.CrucivarTestTools.PlayModeTest -crucivarTest -lastbellOut <dir>
    ```
  - **Run-loop autotest:**
    ```
    Unity.exe -projectPath unity -executeMethod LastBell.Run.EditorTools.RunBuild.PlayModeAutotest -lastbellAutotest -lastbellOut <dir>
    ```
  - Both run in editor Play Mode (not batch). They exit 0 when everything passes.
- **Fresh worktree setup:** run the asset packs with `powershell -File tools/asset-packs/Get-AssetPacks.ps1`. To skip a 20-minute first import, copying `boss-crucivar/unity/Library` into the new worktree's `unity/Library` is fine (read-only copy). Known editor-only noise in copied Libraries: a `UnityEditor.Search` indexing exception and `SkywardSpireArena.OnValidate` touching `Renderer.material`. Neither is gameplay.

## Part C — build (overnight)

### Ability sessions (one per ability)

1. **Implement the ability** as data plus code through the shared hazard system in Crucivar's encounter. Use one class or section per ability (for example `Run/Encounters/Crucivar/Abilities/<AbilityId>.cs`) so ability sessions don't edit the same lines.
2. **Add the telegraph presentation:** coral/amber, crisp edges, the real hit shape, and the Brass Foundry look (pour channels, gear-tooth rims, furnace bellows, crucible splashes, chains and anvils; heat stays coral/amber only where it's a real hazard). Use a simple stand-in mesh or particles if no art exists.
3. **Add autotest checks:**
   - the hazard spawns and resolves at the right ticks
   - the escape route works at 4.2 m/s with finite stamina
   - a player standing still gets hit
   - melee and ranged positions: a scripted player next to the boss and one standing still at long range (8 m+ and at the arena edge) both get hit if they don't move
   - one hit per instance
   - the solo rule and the N = 2 / 4 / 8 rules (scripted players)
   - boss death mid-windup cancels the hazard
   - the Easy / Hard differences
4. **Add a practice hook** so the ability can be fired on its own (for example a dev key or `-lastbellAbility <id>`).
5. **Commit and report:** commit on `feature/crucivar-ability-<id>`, push, and tell the Abilities session (SendMessage) when it's ready, with test results.

### The Abilities session

1. Merge each ready ability branch into `feature/crucivar-abilities-v2`.
2. Wire the phase plan and the second-form transition.
3. Make the new set the default in Crucivar's practice slot. Keep the old set reachable with `-lastbellAbilitySet v1` for comparison.
4. Build and run the full autotest after each merge. Everything must pass, including all existing run-loop checks.
5. Fix integration bugs. Send ability-specific bugs back to that ability's session.
6. At the end, push and open a draft PR into Crucivar's current branch (`feature/boss-crucivar`). Write `abilities-v2/MORNING-REPORT.md` covering:
   - what's playable and how to launch it (exact command and practice slot)
   - pass/fail counts
   - escape-check results
   - which abilities are complete, partial or not started, and why
   - conflicts and open questions
   - a short clip or screenshots per ability, if the build/test script makes them

## Rules for every session in this tree

- **Settled rules in `gpt/08-design-rules.md` win.** Highlights:
  - no iframes, grace windows or post-hit immunity
  - no block, parry or counter (Brace is −60 % on Strike only)
  - no teleporting players
  - at most two damaging patterns at once
  - one hit per hazard instance
  - no enrage timers, and pools always expire
  - every co-op attack has a written solo rule
  - empty soaks wipe the party, and their telegraph must be impossible to miss
  - no encounter requires a specific class
  - every attack has a real escape route at 4.2 and 6 m/s
  - melee and ranged must both be in danger: most attacks threaten both, at least one punishes staying at range, and there's no permanently safe spot (`gpt/08-design-rules.md` §5)
- **Stay in your lane.**
  - Don't edit other bosses' branches, `feature/run-loop` directly, the class kits, class rigs/animation, the main menu, or networking.
  - Shared-system changes (a new hazard shape, for example) go in a small separate commit that the Abilities session reviews, with a note in its report.
- **One Unity build or test at a time, machine-wide.** Unity builds are heavy and many sessions share one PC.
  - Before any Unity command, take the lock by creating the folder `C:\Users\BS_Ch\Documents\Codex\last bell\.unity-build-lock`. `mkdir` fails if it exists, so wait 60 s and retry.
  - Write your session name into `owner.txt` inside it.
  - Remove the folder when your build/test finishes, including on failure.
  - If a lock is more than 45 minutes old and its owner is idle, you may break it. Note that in your report.
- **Large binaries** (GLB, big PNG, video) go through asset packs (`docs/asset-packs.md`), not git.
- **Build and test in your own worktree,** never in a checkout another session is using.
- **Git hygiene:**
  - Commit only your own changes and push often.
  - Keep "designed", "built", "tested" and "human-approved" as separate states. Nothing is human-approved until Chzy18 plays it.
  - Commit with `git -c user.name=Chzy18 -c user.email=345140526+Chzy18@users.noreply.github.com`. Pushing is pre-authorized.
- **Usage limits:** if you run out of usage, the session pauses until the limit resets. Make sure your work is committed and pushed before each long step.
- **End report:** end each session's work with a short report: Completed / Next / Blocked, with evidence.
