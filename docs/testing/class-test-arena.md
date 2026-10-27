# Class test arena

A bare playable space for trying the class kits: one player, a training dummy at the boss position, range rings, a damage meter and optional demo hazards. It's a test harness, not the U2 presentation scene and not the U1 combat core.

## Run it

1. Until U0 commits the real project settings, create a new Unity project (the URP 3D template works) and copy `unity/Assets/LastBell` into its `Assets/` folder. No extra packages are needed.
2. Use the menu **Last Bell → Play Class Test Arena**. The first time, it creates `Assets/LastBell/Scenes/ClassTest/ClassTest.unity` and adds it to Build Settings.

You can also put `ClassTestArena` on an empty GameObject in any empty scene. It builds its own camera, light, floor, dummy, player and HUD.

## Controls

| Input | Keyboard / mouse | Gamepad |
|---|---|---|
| Move | WASD or arrows | Left stick |
| Basic (hold to repeat) | J or left mouse | A / South |
| Technique | K or right mouse | X / West |
| Signature | L or Q | Y / North |
| Guard | Space | RB |
| Switch class | 1, 2 or Tab | — |
| Hazards on/off | H | — |
| Invulnerable | I | — |
| Dummy infinite / 180 HP (time-to-kill) | U | — |
| Range rings | G | — |
| Reset meter / reset all / help | M / R / F1 | — |

The input code works with the legacy Input Manager or the Input System package.

## What's in it

- **Vanguard** uses the legacy reference values (range 155/185/270, damage 2/9/18, cooldowns 24/48/360, guard 24-tick window on 180).
- **Arcanist** is a first guess at the plan's "spend energy or hold a casting stance" identity: ranged basics, a 3-energy Arc Lance, a 1-second rooted **Resonant Cast** that moving or guarding cancels (60-tick cooldown on cancel), and **Ward Step**, a guard that also blinks 110 units. All Arcanist numbers are placeholders to tune here.
- Demo hazards are the legacy Bell Sweep / Hollow Toll alternation every 210 ticks, so guard timing and escape distances can be felt. They aren't the Bellkeeper timeline.
- Range rings are centred on the dummy and light up when you're in range for that action.

Edit `unity/Assets/LastBell/Core/ClassTest/ClassKits.cs` to tune or add a class. New kits appear in `ClassKits.All` and get the next number key.

## Checks

The rules are plain C# with no Unity dependency:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/class-test/check.ps1
```

They cover range, charge/energy, shared cadence and priority, cast completion and cancellation, the blink and its arena clamp, guard/immunity/knockout/respawn, the finite dummy, and seeded reset determinism. They don't run the Unity scene. Camera framing, input feel and visuals need someone to play it in the editor.

## Healer

Select 3 (or Tab). See [Healer controls, rules, verification and Claude handoff](healer.md). The Healer adds a support training ally and uses the same four action slots.

## Without Unity

Double-click `Play Class Test.cmd` at the repository root. It compiles the same `Core/ClassTest` rules plus a top-down Windows window (`tools/class-test-desktop/`) with the C# compiler built into Windows, then starts it. Nothing to install. Controls match the table above; Healer adds **T** to cycle the support target (Auto/Self/Ally) and **Shift / LB** to force Self. An Xbox-style controller works through XInput.

`LastBellClassTest.exe --snapshots <folder>` plays a short scripted fight for each class and saves a PNG per class, for checking the drawing without a person at the keyboard.

## Vanguard 2.0 and dash

The Vanguard is now the 2.0 redesign (Clockblade Combo, Resonance, Pendulum/Reversal Rush, Final Hour, Resonant Guard counters), and every class has the Clockstep Dash (E or 5; desktop gamepad B). The old kit stays as `ClassKits.VanguardLegacy` for parity checks. Turn on `CounterableStrikes` to add the counterable Pendulum Strike to the demo hazards. Rules, decisions and verification: `docs/design/vanguard-v2-implementation.md`.
