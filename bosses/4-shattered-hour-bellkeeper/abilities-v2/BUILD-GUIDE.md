# Bellkeeper ability set v2 — build guide for the ability sessions

Status: scaffolding for the overnight build (2026-10-22). Integration branch: `feature/bellkeeper-abilities-v2` (from `feature/run-loop` + the design branch). Each ability is built on its own branch `feature/bellkeeper-ability-<id>` cut from it, then merged back by the Abilities session.

## Where your code goes (one owner per file)

| Ability | Your code | Your autotest checks |
|---|---|---|
| Leaning Bell | `unity/Assets/LastBell/Run/Encounters/Bellkeeper/Abilities/LeaningBell.cs` | `.../Bellkeeper/Tests/RunAutoTest.Bellkeeper.LeaningBell.cs` |
| Striking Order | `.../Abilities/StrikingOrder.cs` | `.../Tests/RunAutoTest.Bellkeeper.StrikingOrder.cs` |
| Reflection of the Hour | `.../Abilities/Reflection.cs` | `.../Tests/RunAutoTest.Bellkeeper.Reflection.cs` |
| The Last Toll Tide | `.../Abilities/TollTide.cs` | `.../Tests/RunAutoTest.Bellkeeper.TollTide.cs` |
| The Unfinished Hour | `.../Abilities/UnfinishedHour.cs` | `.../Tests/RunAutoTest.Bellkeeper.UnfinishedHour.cs` |

Each file already exists as a stub (with a committed `.meta`, so GUIDs don't conflict). Replace the stub; keep the class name, `Id` and the `…Checks()` method name. Extra files of your own (presentation helpers) go next to it, named after your ability (`Abilities/LeaningBellFx.cs`), with a `.meta` you commit. Don't edit other abilities' files, `BellkeeperEncounter.cs`, `BellkeeperV2.cs`, `BellkeeperAbility.cs`, `ScriptedParty.cs`, `RunAutoTest.BellkeeperV2.cs` or `RunAutoTest.Bellkeeper.PhasePlan.cs`. If you need a hook there, or a shared-system change (`Run/Combat/Hazards.cs`, `VFX/Boss/Bellkeeper/BellkeeperFx.cs`), make it a small separate commit with a clear message and say so in your report: the Abilities session reviews it.

## The ability contract (`Abilities/BellkeeperAbility.cs`)

- `Cast()` is a tick coroutine (`yield return n` = wait n combat ticks at 60 Hz). Tick 0 is the warning's first tick. It ends after the ability's own short recovery (ABILITIES.md §5: e.g. 1.0 s after Leaning Bell resolves). The 3.5 s punish window after the Toll Tide belongs to the encounter, not to `Cast()`.
- `Setup()` is boss movement before the warning (Leaning Bell's walk to within 3 m of the centre: `foreach (var w in Boss.Walk(dest, 1.75f, 120)) yield return w;`).
- Spawn hazards with `Spawn(new Hazard { ... })` (the base keeps them in `Spawned` for tests). Shapes: `Circle`, `Ring`, `Cone`, `Lane` (`Run/Combat/Hazards.cs`). A cone with `HalfAngle = 90` and a large `Radius` is a half-plane; a ring with a large outer radius reaches the wall. `Track` moves a hazard until `LockAt`; `OnResolve` is presentation after the hit test. One hit per instance per player is already enforced.
- **Inward knockback needs no shared change:** `Knockback = -1.5f` pushes toward the hazard's origin (the push is `PushDir × Knockback`).
- Per level: read `Level` (`Difficulty.Easy/Medium/Hard`). Damage scaling ×0.6/×1.3 is already applied when a hit lands; author Medium damage.
- Players: `W.Players` (scripted extras included), `StandingCount`. Boss: `Boss.transform`, `Boss.TargetPosition`, `Boss.Phase`, `Boss.Presenter` (clips: `BellSweep`, `HollowToll`, `PendulumCharge`, `ShatteredChime`, `EchoTell`, `Vulnerable`, `PhaseShift`), `Boss.ShowAttack(name)` (HUD), `Boss.Glow(windup01)` / `Boss.Flare()`, `Boss.Face(yaw)`.
- Map: `CombatWorld.PlayerBound`, `CombatWorld.SquareBound` (Throne: 13.0 m circle; test map: 12.5 m square), `RunMaps.Current`.
- `Cancel()` runs on encounter restart, halt, phase change and defeat: drop references and destroy any presentation you made yourself (numerals, after-images). Live hazards are cancelled by the encounter (boss death cancels them on the killing tick).
- `Practice()` is the `-lastbellAbility <key>` loop (default: Setup, Cast, 2 s gap). Override it only if your move needs a lead-in (The Unfinished Hour needs two Leaning Bells first; use `Boss.Ability(BellkeeperAbilities.LeaningBell)` and `Boss.Launch(...)`).
- **Leaning Bell → The Unfinished Hour:** Leaning Bell calls `Boss.RememberLean(itsLockedHalfHazard)` from its `OnResolve`. The encounter keeps `Boss.LastLean` / `Boss.PreviousLean` (cleared on phase change, defeat, halt, restart) and raises `LeanRemembered` / `LeansForgotten` (use these for the dashed memory outline). In the phase plan on Hard from phase 2 the encounter launches The Unfinished Hour on the tick after a Leaning Bell resolves when there is a previous one; Unfinished Hour's `Cast()` replays `Boss.PreviousLean` in world space.

## Presentation

Hostile telegraphs: coral/amber, crisp edges, built from the real hit shape (the shared telegraph meshes already do that: `W.BossFx.Circle/Ring/Cone/Lane`, `Tint`, `Memory()` for an inactive dashed outline). Area look: broken cathedral, cosmic void, fractured time — toll rings, cracked bell shock-fronts, fractured clock rings, echo after-images. Use simple stand-in meshes (`Prim.Make`) or the existing burst effects if no art exists. Identity markers (Striking Order numerals) are ivory, never coral. Friendly effects never use coral/amber.

## Phase plan (wired by the Abilities session in `BellkeeperV2.cs`)

Phase 1: Leaning Bell → Striking Order → Reflection → Toll Tide → 3.5 s punish. Phase 2: Lean and Order (Order launched 90 ticks after Lean's tick 0) → Reflection → Toll Tide → punish. Second form at 35 % (one queued 1.5 s harmless transition). Phase 3: Lean and Order → Final Toll (Toll Tide, Reflection launched 60 ticks later) → punish. Hard: The Unfinished Hour after each Leaning Bell from phase 2. Your timings inside `Cast()` must match ABILITIES.md §5 and your brief so these pairings line up (e.g. Striking Order's ring I at its tick 180, Reflection resolving at its tick 240, Heart Toll resolving at Toll Tide's tick 300).

## Practice and launch

- Default practice slot 1 ("Bellkeeper") = set v2, phase plan, on the Shattered Hour map (Throne of the Final Toll).
- `LastBell.exe -lastbellAbility leaning_bell` (keys: `leaning_bell`, `striking_order`, `reflection`, `toll_tide`, `unfinished_hour`; dashes, full ids and `01`–`05` also work) loops one ability alone, in practice and in the run.
- `-lastbellAbilitySet v1` = the old set and the old practice behaviour (current map).
- `-lastbellBots 4` = practice with 3 scripted stand-ins (they step out of hazards that are about to resolve), to see the co-op shapes.
- `-lastbellDifficulty easy|medium|hard`.

## Tests

Write your checks in your `RunAutoTest.Bellkeeper.<Ability>.cs` with the harness in `RunAutoTest.BellkeeperV2.cs`:

```csharp
yield return BkStart("leaning_bell");            // practice, Medium, Throne; paused at combat start, boss halted
// or BkStart("leaning_bell", Difficulty.Easy) / BkStart(key, testMap: true) for the square test map
var ally = BkAdd(new Vector3(5, 0, 0));          // scripted extra player (N = 2/4/8)
BkPlace(d.Player, BkEdge(Vector3.right));        // the run's own player (bot); BkEdge = the wall along a direction
var a = bk.Ability(BellkeeperAbilities.LeaningBell);
bk.BeginCombat();                                // restart the boss: the practice loop launches your ability
int t = BkStepUntil(() => a.Spawned.Count > 0, 300);
var s = BkScript(ally); s.Goal = somewhere; s.Speed = 4.2f; s.DashNext = true;   // escape at 4.2 m/s + one dash
BkStep(60);
Check("Leaning Bell: ...", BkHitsOn(ally) == 1, "detail");
```

`bkHits` records every hit (tick, player, source, hazard id, damage). `BkPatternsLive()` counts distinct live hazard sources. `bk.DebugDefeat()` kills the boss mid-windup. `yield return Shot("bk_leaning_bell_warning")` saves a screenshot; `yield return BkClip("bk_leaning_bell", 6f)` records a short real-time clip (frames → MP4 by the build script).

Required checks (Part C): hazard spawns and resolves at the right ticks; the escape route works at 4.2 m/s with finite stamina (and no iframes); a still player gets hit; a still player next to the boss and one still at long range (8 m+, and one at the wall) both get hit; one hit per instance; solo rule and N = 2/4/8 rules with scripted players; boss death mid-windup cancels; Easy/Hard differences. Label checks with your ability's name.

## Build and test (one Unity at a time on this PC)

Use `tools/bellkeeper-v2/build-and-test.sh` from **your own worktree**. It takes the machine-wide lock `C:\Users\BS_Ch\Documents\Codex\last bell\.unity-build-lock` (waits 60 s between tries, writes your name in `owner.txt`, breaks a lock older than 45 min only when no Unity/LastBell process runs, always releases it).

```bash
BK_SESSION=bellkeeper-leaning-bell tools/bellkeeper-v2/build-and-test.sh leaning-bell-1 -lastbellBellkeeperV2 -lastbellTestAbility leaning_bell
```

Run it with `run_in_background` (a build is several minutes; waiting for the lock can be longer). Evidence: `C:\Users\BS_Ch\Documents\Codex\last bell\bellkeeper-v2-evidence\<name>\` (`autotest-report.txt`, `player.log`, `build.log`, screenshots, MP4s). Before your first build, copy the Unity `Library` from the integration worktree to skip a full import (PowerShell: `robocopy "C:\Users\BS_Ch\Documents\Codex\last bell\bellkeeper-abilities-v2\unity\Library" "<your worktree>\unity\Library" /E /MT:8 /NFL /NDL /NP`), and run `setup-assets.cmd` from your worktree root (`powershell -NoProfile -ExecutionPolicy Bypass -File tools/asset-packs/Get-AssetPacks.ps1`, run with the worktree as the current folder).
