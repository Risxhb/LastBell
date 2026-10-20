# Last Bell — legacy Godot combat reference

**Production engine changed to Unity with C# on October 20, 2026.** This directory is retained unchanged as a behavior and test reference for migration. Commands and APIs below run the historical Godot experiment only; they do not launch or validate a Unity build. Do not continue production implementation here. See [the Unity migration plan](../docs/plans/unity-migration.md).

Use **Godot 4.7.2-stable, standard editor** and typed GDScript. Open `project.godot`, then press F6/F5 to run the scene/project. Exporting requires matching 4.7.2 standard export templates; editor play and the checks below do not require templates.

This first increment contains solo Vanguard, four actions, a bounded arena, alternating Bellkeeper cone/donut warnings, five health pips, guard, a resolution readout, victory/defeat and instant restart. It is not the full playable slice. Networking, spreads, Echo, upgrades, second class/boss, rewards and rehearsal checkpoints are subsequent tasks in issues [#6](https://github.com/Risxhb/collab/issues/6), [#7](https://github.com/Risxhb/collab/issues/7) and [#8](https://github.com/Risxhb/collab/issues/8).

## Confirmed production direction

The target is **2.5D with a fixed isometric camera**, inspired by Windblown and Hades. This branch still runs the temporary Node2D view; no 3D assets or camera are implemented by the documentation update. Port the combat behavior and shared geometry into C# during migration; existing GDScript cannot be reused as Unity scripts. See [the foundation plan](../docs/plans/2-5d-isometric-foundation.md) for delivery gates and [the art direction](../reference/LAST-BELL-CLOCKWORK-FANTASY-ART-DIRECTION.md) for visual constraints.

## Controls

| Action | Keyboard | Controller (Xbox labels) | Prototype rule |
|---|---|---|---|
| Move | WASD / arrows | Left stick | 250 units/second; diagonal magnitude capped |
| Basic | Hold J | Hold A | 2 damage, +1 charge up to 6; 0.4s cooldown; 155 range |
| Technique | K | X | 9 damage, costs 3 charge; 0.8s cooldown; 185 range |
| Signature | L | Y | 18 damage; 6s cooldown; 270 range |
| Guard | Space | B | Prevents eligible damage for 0.4s; 3s cooldown |
| Restart | R | Start | Restore the same seed immediately, even mid-fight |

Offensive actions share a 0.3s cadence. Simultaneous presses prefer Signature, Technique, Basic; an unavailable action falls through to the next eligible one. Holding any action repeats when eligible. Failed range/resource checks do not spend cooldown or charge. Move closer to the boss to attack; the thin circle marks Basic reach. Controller mappings are implemented but still need a physical-controller playtest.

The demo warns for 1.5s, alternating a locked cone and a donut every 3.5s. The donut's center is safe. Attack geometry checks the player's center, not the drawn silhouette. A hit costs one pip and grants 0.75s damage immunity. Guard is independent of offensive cadence and activates before hazard resolution on the same tick. Guard and immunity end at their stated exclusive end tick. Guarding a failed positioning requirement prevents damage but still records that failure. These are initial tuning values.

## Run and check

PowerShell, replacing the executable path with your portable editor location:

```powershell
$godotExe = 'C:\path\to\Godot_v4.7.2-stable_win64_console.exe'
& $godotExe --path .\game
& $godotExe --headless --path .\game --script res://tests/core/run.gd
& $godotExe --headless --fixed-fps 60 --path .\game --script res://tests/core/input_smoke.gd
```

Run these from the repository root. Both scripts return nonzero on failures. The simulation suite covers geometry edges, range/resource/cooldown ordering, clamped movement, guard/immunity timing, knockout freeze, complete reset, seeded reproduction, detached snapshots, the encounter-provider seam and victory. The input smoke drives the actual view through Godot input actions; it does not emulate a physical keyboard/controller or assess enjoyment.

For a rendered preview, launch without `--headless` and add `-- --capture C:\absolute\preview.png`. Optional `--capture-tick 220` advances the idle simulation before capture (donut); `--capture-tick 1000` shows defeat. The process captures its own viewport and exits. User data/logs normally go to Godot's standard user directory; restricted environments can set process-local `APPDATA` to a writable directory and pass `--log-file C:\writable\run.log`.

For the anchor/lock render fixture, run `& $godotExe --path .\game --script res://tests/core/anchor_preview.gd -- --capture-dir C:\existing\output` without `--headless`. It saves five screenshots covering tracking, pending lock, both locked states, and an assignment-only cancellation log. This is visual inspection input, not an automated readability verdict. The ordinary solo demo stays fixed at warning time.

## Architecture and handoff

See [the integration contract](../docs/combat-contract.md). The simulation has no scene nodes, input access, renderer or network peer. It advances in integer ticks at 60Hz. The view accumulates real time, capped at 0.25s per frame; excessive stalls intentionally slow the local experiment rather than simulating unbounded catch-up. This accumulator is not a network clock.

The solo view uses stable ID 1. The shared simulation also supports an explicit 1–8 Vanguard roster through `configure_roster`, with independent actor state, party knockout rules and class/build configuration preserved across reset. Builds are stored metadata without upgrade effects. Contract v2 adds `queue_hazard` and player anchors that follow until lock or sample at lock, including fixed-origin aimed cones. The view labels tracking/lock state, and resolution events record the locked geometry. Unavailable targets cancel unlocked warnings with assignment events; locked footprints persist. See the contract for validation, tick ordering and limits. Group mechanics and reassignment remain follow-ups. The view and demo balance remain solo; the roster checks establish simulation behavior, not network capacity. Snapshots are diagnostic detached dictionaries; there is no snapshot import, prediction/reconciliation or production wire protocol yet. Seeded schedules do not establish cross-machine determinism.

Human checks still required before acceptance: movement/attack feel, warning clarity, guard timing, controller hardware, resize readability and collaborator review. No eight-player or internet-play result is claimed by this PR.
