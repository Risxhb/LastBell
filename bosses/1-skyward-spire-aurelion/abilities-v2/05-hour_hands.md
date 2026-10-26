# 05 — Hour Hands (`aurelion.hour_hands`) — Hard-only named mechanic — proposal

**Status:** proposal. It is designed and escape-checked, but not built. The full set is in `ABILITIES.md` (same folder), and the escape check is `tools/encounter-escape/aurelion-v2-escape-check.ps1`.

**Boss:** Aurelion, the Meridian Regent. **Arena:** Skyward Spire, play bound 13.9 m.

## Fantasy and look

On Hard, the Regent counts the hours. A 12-hour dial projects onto the dais round him, and his glaive (the minute hand) and halo arcs (the hour hands) swing to each player's hour. A narrow clock-hand spoke then strikes from his feet through that hour, out to the rim.

- **Telegraph:**
  - Faint ivory hour ticks round the boss (identity only, never coral).
  - Each live spoke is a coral/amber lane with a crisp edge and a tapered "hand" tip at the rim.
  - While a spoke tracks, it's a thin gold line; at the lock it snaps to full width with a clock tick.

## Shape (Strike)

- **Spoke:** a lane from the boss centre outward, **1.8 m** wide, 28 m long (`HazardShape.Lane`), aimed at the player's **hour**: their angle round the boss, snapped to the nearest 30°.
- **Sharing an hour:** players on the same hour share one spoke instance, which hits each of them once.
- **Tag:** Strike.

## Timing (Medium/Hard)

- Each spoke tracks its player's hour for **1.5 s (90 ticks)**, **locks**, and resolves **1.0 s (60 ticks)** later. That's 2.5 s total.
- **Solo second hand:** spoke II starts on spoke I's lock tick (90), tracks your new hour for 1.5 s, and resolves at 240.
- **R&S basis:** clock spots, minimum 3.0 s and median 5.5 s with a 40–70° fan. Ours is a narrow 1.8 m lane, so we tighten to 2.5 s total.
- **Hard only:** it doesn't exist on Easy or Medium.

## Damage

**30** (Hard's global ×1.3 applies). The standard 1.5 m sideways knockback; stun 0.25 s.

## Escape route

| Case | Worst escape | Window | 4.2 m/s walk / walk+dash | 6 m/s walk / walk+dash | Margin at 4.2 m/s |
|---|---|---|---|---|---|
| Own spoke | 1.20 m | 1.0 s | 0.54 / 0.33 s | 0.45 / 0.33 s | 0.67 s |

- **Worst case:** on your spoke's centre line. Step 1.2 m sideways without stepping into a partner's spoke. Near the boss the hours overlap: two adjacent hours leave a gap only from about 4.6 m out.
- **Melee threat:** every spoke starts at his feet, and near him adjacent hours overlap, so melee must step out.
- **Ranged threat:** spokes reach the rim. During the Horizon nobody can retreat outward, so every player has to sidestep inside 8.2 m.

## Co-op and solo

- **Co-op rule:** each player gets a spoke on their hour. Spread across different hours so a sidestep doesn't land in a partner's spoke.
- **Solo rule:** **the hands strike twice** (a sequence, above).

| N | Behaviour |
|---|---|
| 1 | Spoke I, then spoke II on your new hour |
| 2 | 2 spokes |
| 4 | Up to 4 spokes |
| 8 | Up to 8 of the 12 hours used: assign hours |

## Phase use

- **Hard, Phases 2 and 3**, inside the Horizon after Orrery Closing.
- It warns on the tick that Horizon's Starfall resolves, and the Horizon is extended to 6 s on Hard to cover it.
- At most 2 damaging patterns: Horizon + Hour Hands.
- **Punish window:** the loop's 3.5 s open-chest punish follows the Horizon.

## Escape-check plan (autotest)

- A player standing still on their spoke is hit.
- A player stepping 1.2 m sideways after 0.25 s from the lock is not hit.
- Two players on the same hour share one spoke (each hit once).
- Two players on adjacent hours at 3 m from him: the sidestep is blocked by the neighbour's spoke unless they step outward first.
- Solo: spoke II spawns on spoke I's lock tick.
- It never appears on Easy or Medium.

**R&S source:** §4 block 13 (clock spots). §5 proposal 6 (Hour Hands).

---

## How to build it (ability session)

You are the session for this one ability. Work only on this ability. Your parent is the **Abilities** session (`abilities-aurelion`), which owns `feature/aurelion-abilities-v2` and integrates the five abilities.

**Setup**
- Work in your own worktree, branched from `feature/aurelion-abilities-v2`. Commit on **`feature/aurelion-ability-<id>`**, where `<id>` is the ID after `aurelion.`.
- The Abilities session puts a skeleton on `feature/aurelion-abilities-v2` before it starts you:
  - `AurelionAbility` (base class)
  - the v2 scheduler in `AurelionEncounter`
  - the `-lastbellAbility <id>` practice hook
  - a scripted-player test helper
- Implement your ability in **`unity/Assets/LastBell/Run/Encounters/Aurelion/Abilities/<AbilityName>.cs`**, one class, so ability sessions never edit the same lines.
- If you truly need a shared-system change, such as a new hazard shape, put it in a small separate commit and tell the Abilities session. The only shared files you may touch are your own section of `RunAutoTest.Aurelion.V2.<AbilityName>.cs` and the registration line the skeleton marks for you.

**Mechanics**
- Use the shared hazard system only. That means `Hazard`, `HazardShape` (Circle, Ring, Cone, Lane) and `HazardSystem` on `CombatWorld`, with one hit per instance, `Hazard.Margin` 0.3 m, `HitOnResolve`, `ActiveTicks` and `HazardSystem.Cancel`. Don't build a parallel combat system.
- The game runs at 60 Hz. Convert seconds to ticks with `Sim.Ticks(s)`. Read the difficulty from `CombatWorld.Difficulty`: Easy ×1.25 on every warning, track and window.
- Hostile telegraphs are coral or amber, with crisp edges, drawn from the real hit shape, in the area's look (§ Look). Use simple stand-in meshes or particles if no art exists.

**Practice hook:** `LastBell.exe -lastbellAbility aurelion.<id>` (and the dev key the skeleton provides) fires your ability on its own, repeatedly, in Aurelion's practice slot on Skyward Spire.

**Autotest checks.** Add them in your own partial file, `RunAutoTest.Aurelion.V2.<AbilityName>.cs`, called from the skeleton:
1. the hazard spawns, locks and resolves on the right ticks
2. the escape route works at 4.2 m/s with finite stamina (a scripted player that walks, plus one dash where this brief says so)
3. a player standing still gets hit
4. a scripted player next to the boss and one standing still at long range (8 m+, and at the arena edge) both get hit if they don't move
5. one hit per instance
6. the solo rule and the N = 2 / 4 / 8 rules, using scripted players from the helper
7. boss death mid-windup cancels the hazard on the same tick
8. the Easy and Hard differences

**Builds and handoff**
- **Unity lock:** only one Unity build or test runs at a time, machine-wide. Before any Unity command, `mkdir "C:\Users\BS_Ch\Documents\Codex\last bell\.unity-build-lock"`. If that fails, wait 60 s and retry. Write your session name into `owner.txt` inside it, and remove the folder when you finish, including on failure. A lock older than 45 minutes whose owner is idle may be broken; say so in your report.
- **Build and test:** `aurelion-logs/build-and-test.sh <n> [-lastbellAurelionOnly]` shows the pattern. Copy it and point it at your worktree. In a fresh worktree, run `tools/asset-packs/Get-AssetPacks.ps1` once. Copying `run-loop/unity/Library` into your worktree's `unity/` folder makes the first import fast.
- When your ability is ready, push your branch and **SendMessage the Abilities session** with your test results.

## Rules for every session in this tree

- **Settled rules in `gpt/08-design-rules.md` win:**
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
  - melee and ranged must both be in danger: most attacks threaten both, at least one punishes staying at range, and no spot is permanently safe (§5)
- **Stay in your lane:**
  - Don't edit other bosses' branches, `feature/run-loop` directly, the class kits, class rigs or animation, the main menu, or networking.
  - Shared-system changes go in a small separate commit that the Abilities session reviews.
- **Unity builds are heavy and many sessions share one PC.** Take the lock above before any Unity command.
- **Large binaries** (GLB, big PNG, video) go through asset packs (`docs/asset-packs.md`), not git.
- **Build and test in your own worktree,** never in a checkout another session is using.
- **Commits:**
  - Commit only your own changes, with `git -c user.name=Chzy18 -c user.email=345140526+Chzy18@users.noreply.github.com`.
  - Push often. Pushing is pre-authorized.
  - Keep "designed", "built", "tested" and "human-approved" as separate states. Nothing is human-approved until Chzy18 plays it.
- **Usage:** if you run out of usage, the session pauses until the limit resets. Commit and push before every long step.
- **Chzy18 is asleep.** Don't stop to ask questions: make the call, write it under "Open questions", and keep going.
- **End with a short report:** Completed / Next / Blocked, with evidence such as test counts and screenshots.
