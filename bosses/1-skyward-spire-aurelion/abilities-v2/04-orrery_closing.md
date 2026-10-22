# 04 — Orrery Closing (`aurelion.orrery_closing`) — signature — proposal

**Status:** proposal. It is designed and escape-checked, but not built. The full set is in `ABILITIES.md` (same folder), and the escape check is `tools/encounter-escape/aurelion-v2-escape-check.ps1`.

**Boss:** Aurelion, the Meridian Regent. **Arena:** Skyward Spire, play bound 13.9 m; the boss stays inside 9.5 m.

## Fantasy and look

Aurelion's signature. He spreads his arms and the great orrery closes:
- The **outermost ring** descends onto the dais rim, lighting everything beyond 8.5 m.
- At the same moment the **crown** (the halo's innermost ring) slams down round his feet.

From Phase 2 the rim ring **stays lit** for a few seconds as the **Horizon**: a temporary edge that keeps everyone inside. The Phase 3 visuals revolve around it. The halo arcs spread open, the Grand Orrery rings counter-rotate, and the open chest exposes the pendulum for the punish.

**Telegraph:**
- Reuse and extend the current **orrery ring visuals** (`AurelionEncounter.SpawnOrrery`): dashed gold circles with clock ticks laid exactly on each shape's edges, spinning up toward the hit.
- Fill each shape with coral/amber, with crisp edges.
- The Horizon, while it stays lit, is a calmer coral band with a pulsing edge. It must read as "don't cross", and it must expire visibly.

## Shape (Ground)

- **Rim:** `HazardShape.Ring` centred on the **arena centre**, inner radius **8.5 m**, outer 30 m (past the bound).
- **Crown:** `HazardShape.Circle` on the boss, radius **3.6 m**.
- Both are separate instances, so a player can only be in one.
- **Horizon** (Phase 2+): the rim's `ActiveTicks` keeps it hitting for **4.0 s (240 ticks)** after the resolve tick, or **6.0 s (360)** on Hard so it covers Hour Hands. Anyone entering is hit once (one hit per instance), and it always expires. `ActiveTicks` and `OnSpent` exist in the shared `Hazard` (from the Sylvara merge).
- **Tag:** Ground.

## Timing (Medium)

- Shapes **locked at the warning start**; both resolve **2.3 s (138 ticks)** later.
- **R&S basis:** temporary field limit, median 1.0 s (R&S gives it no damage), and a knockback donut, median 2.5–3.0 s. We use 2.3 s because the rim run is long. Our rule: no damaging warning under 2 s.
- **Easy:** ×1.25 (2.9 s; the Horizon stays 4 s).

## Damage

**40** per ring. No knockback (Ground; nobody pinned at the bound); stun 0.25 s.

## Escape route

| Case | Worst escape | Window | 4.2 m/s walk / walk+dash | 6 m/s walk / walk+dash | Margin at 4.2 m/s |
|---|---|---|---|---|---|
| Rim + crown | 7.65 m | 2.3 s | 2.07 / 1.62 s | 1.52 / 1.29 s | 0.68 s (needs 1 dash at 4.2; 0.23 s walk-only) |

- **Worst case:** an edge point behind a boss standing 6 m out, where the safe floor is inside 8.2 m and outside his crown.
- **Melee threat:** the crown. Step 2.4 m out from his body edge.
- **Ranged threat:** the rim, then the Horizon. Anyone outside 8.2 m must come in and stay in for 4 s while Starfall (or Hour Hands on Hard) lands inside. **This is the anti-range signature.**

## Co-op and player counts

Not a co-op mechanic: the same shapes for every N. The spatial job grows with N: at N = 8 the Horizon packs eight players inside 8.2 m while Starfall needs them spread. The check shows 8 players on a 6 m ring all have a 2.1 m route.

## Phase use

- **Perfect Order:** alone, no Horizon, then the **punish (3.5 s)**.
- **Misalignment:** Orrery Closing → Horizon 4 s with Starfall inside (on Hard, Hour Hands follows inside the Horizon) → **punish 3.5 s**.
- **Exposed Meridian:** **Grand Orrery**: Orrery Closing with Starfall cast 0.8 s in → Horizon → **punish 3.5 s**.
- **The punish:** the major 3–4 s punish window, the only long gap in the loop. No hazards, boss planted, chest open, pendulum glowing. This keeps v1's "open chest" pose and clip (`OpenChest`).

## Escape-check plan (autotest)

- A player standing still at 12 m is hit by the rim.
- A player standing still against his body is hit by the crown.
- A player at 13.5 m behind a boss at 6 m walks in with one dash after 0.25 s and is not hit.
- Horizon: a player stepping out past 8.5 m during the Horizon is hit **once**, and stepping out again isn't a second hit. After 4 s it's safe.
- On Hard the Horizon lasts 6 s.
- Boss death mid-Horizon cancels it.

**R&S source:** §4 block 17 (temporary arena edge) + a point-blank ring. The pairing style follows `bp_colormatch_activate_donut`. §5 proposal 12 (pull-then-ring) is a cousin.

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
