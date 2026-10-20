# Standalone session spike

Task: [#7](https://github.com/Risxhb/collab/issues/7). Parent foundation: [PR #10](https://github.com/Risxhb/collab/pull/10), commit `6fa2e6c3c0f3caf8499bbd481bbec15c8223dec3`.
Toolchain: **Godot 4.7.2-stable standard editor**, typed GDScript. Matching export templates are needed only for exports, not these editor/headless tests.

## What this increment demonstrates

One host plus seven clients, stable session-local actor IDs distinct from transport peer IDs, reusable display slots, duplicate Vanguard selection, ready/start gate, authenticated and bounded input requests, processed-input acknowledgments, departure cleanup and host-loss notification. A ninth participant receives `session_full` and is disconnected without entering the roster.

This is an isolated session experiment. It does not move networked combat actors, change the foundation simulation, synchronize boss outcomes, reconnect builds, implement rewards, or demonstrate internet fairness. The scene explicitly identifies this scope.

## Run the interactive scene

From the repository root in PowerShell:

```powershell
$godotExe = 'C:\path\to\Godot_v4.7.2-stable_win64_console.exe'
& $godotExe --path .\game res://scenes/network_test/session_test.tscn
```

Open the same scene in another process. Host on UDP port 27841, then join `127.0.0.1`. Select Ready in each window and Start on the host. All slots currently use Vanguard; duplicates are intentional. The roster shows stable ID, display slot, ready state, completed session tick and processed-input sequences. While running, WASD/arrows and J/K/L/Space submit input; this scene contains no combat.

Close ends that participant's connection; closing the host ends the session. Reopen the scene for another session. The experiment does not implement session reuse, host migration or new joins during a running encounter.

## Repeatable checks

```powershell
$godotExe = 'C:\path\to\Godot_v4.7.2-stable_win64_console.exe'
& $godotExe --headless --path .\game --script (Resolve-Path .\tools\net-test\state_checks.gd).Path
& $godotExe --headless --path .\game --script (Resolve-Path .\tools\net-test\view_checks.gd).Path
.\tools\net-test\smoke.ps1 -Godot $godotExe -OutputDirectory .\tools\net-test\results
```

Use absolute script paths if launching from a different working directory. The PowerShell harness derives its game/script paths from its own location and quotes paths containing spaces. All automated peers bind/connect to loopback; they do not open an internet session. Ports 27841–27847 must be free, or pass another `-BasePort`.

The harness starts hidden native Godot processes and stops only processes it created. It overwrites its expected per-peer report/marker files, retains stdout/stderr, and fails on a nonzero process exit, missing/failed report, engine script error or timeout. Forced-termination cases intentionally kill one marked process after it reaches tick 90; the survivor must observe the loss. Expected killed processes are listed separately in the report. Per-peer reports include counts, ticks, processed acknowledgments and departure/termination reasons. Revision logs include authoritative session events. Godot user data stays inside the output directory. Raw logs/cache are local; only the checked summary is committed.

| Check | Expected result |
|---|---|
| Two participants | Host and client reach running; both receive processed acknowledgments |
| Eight participants | Host plus seven clients, roster never exceeds eight; input from all eight |
| Ninth connection | Existing eight remain; ninth receives exactly `session_full` before encounter starts |
| Unready start | Host start rejected before any player readies |
| Non-host departure | Host removes departed actor; its pending inputs are discarded |
| Host shutdown | Client receives `host_disconnected`, ends input |
| Abrupt client termination | Host detects timeout and removes actor |
| Abrupt host termination | Client detects timeout and reports host loss |
| Pure state checks | Sender/ownership, class/ready gates, malformed/nonfinite data, bounded ticks/sequence/rate, press consumption, snapshot detachment |
| Actual scene checks | Host/Ready/Start/Close buttons, roster and rejection status work |

The adapter uses a short ENet timeout (limit 5, minimum 1000 ms, maximum 2000 ms) for this diagnostic spike. These are not validated production settings.

## Commands and snapshots

`game/network/session_state.gd` is a transport-free host model. Only `enet_session.gd` binds peers to stable player IDs; the model never accepts a client actor ID. Joining allocates a monotonically increasing actor ID and lowest available slot 1–8. Slots may be reused; IDs are not reused within the session. Reconnecting identity/build restoration is deferred.

Control envelope:

```text
{session_id, generation, kind: class | ready | start | sync, ...fields}
```

- `class`: `class_id: "vanguard"`; lobby only and clears that player's ready flag.
- `ready`: strictly boolean `ready`; lobby only.
- `start`: host peer 1 only; all current players ready. Increments generation, clears transient inputs/events and starts the diagnostic clock.
- `sync`: returns current authoritative state.
- Unknown senders, wrong session/generation and unsupported commands are rejected.
- The shared request budget is 120 requests per peer per one-second window, covering control and input. Production flood resistance is not claimed.

Input envelope:

```text
{session_id, generation, kind: input, seq, target_tick,
 move: [x,y], basic: bool, technique: bool, signature: bool, guard: bool}
```

All fields have strict types; unexpected fields (including player_id, health, damage, loot and phase) are rejected. Move components must be finite and the vector is capped to unit length. Sequence must increase by 1–1024 over the previous accepted value. Target ticks must be within [host tick − 6, host tick + 12], and their effective scheduled tick must not regress. Late permitted samples apply no earlier than the next host tick. At most one coalesced command is queued per actor per scheduled tick. Later same-tick samples replace movement/held Basic and preserve any press requests.

The host advances at 60 Hz. Missing samples are neutral, including Basic; the test client samples at 30 Hz. This intentionally tests the existing missing-command contract rather than defining production hold/interpolation policy. `advance()` consumes each scheduled command once and updates `processed_sequences` only after consumption. Acknowledgments cannot move backward. `commands_ready(tick, commands)` exposes the result for a future agreed integration seam. It currently has no combat consumer.

Session snapshots use their own `schema_version: 1`, random session identity, generation, revision, last completed session tick, phase, detached roster, processed sequences and latest 128 session events. Event identity is (session_id, generation, event_id). These are session join/ready/start/disconnect records, not combat resolution records.

For this correctness-first spike, control/state use reliable ENet channel 0 and input uses reliable channel 1. State is published on lobby changes and every three running ticks. This choice preserves press samples without implementing unreliable resend/prediction yet; stale samples still fail the tick bound. It is not a production latency result. Reliable snapshot delivery and bounded session-event history do not implement combat-event gap recovery. Consumers must deduplicate event feedback and explicitly handle history gaps before combat binding.

Object decoding remains Godot's default disabled remote-object mode. ENet here authenticates transport peer ownership, not human accounts; this is a trusted-group experiment, not a public-server security claim.

## Shared foundation boundary

The [v0 combat contract](../combat-contract.md) still needs the accepted roster/configuration and input-sequence revisions before binding. See the [proposal in #6](https://github.com/Risxhb/collab/issues/6#issuecomment-6285942114).

Exactly one authoritative loop must drive integrated combat. Replace the diagnostic session clock with the agreed step seam; do not run two independent clocks. Preserve roster/class/build across reset, map session and combat generations explicitly, attach processed acknowledgments to authoritative combat ticks, and settle loss/recovery for combat events. Prediction/correction, knockout/disconnect mechanic reassignment, encounter-boundary reconnect and shared reward/rehearsal checks are subsequent PRs.

No shared project/root/core/encounter/build files are changed by this spike. The separate test scene launches explicitly.

## Evidence recorded October 20, 2026

Environment: Microsoft Windows 10.0.26200, x64, PowerShell 7.6.5; official Godot `4.7.2.stable.official.ed1daf0bf`. The portable download's SHA-512 matched the official release checksum. No export templates were required.

- **193 state checks passed.**
- **6 actual scene checks passed.**
- Foundation **40 simulation checks passed** unchanged.
- Seven real-process ENet cases passed: two, eight, ninth, departure, host shutdown, abrupt client termination, abrupt host termination. Total: 27 process roles, including two deliberately terminated roles. Exact per-case reports are in [local-summary.json](../../tools/net-test/evidence/local-summary.json).
- One editor import reported an environment safe-save error for editor settings; editor startup completed. Runtime suites above exited cleanly with no script errors after the cleanup fix. No editor-save success or physical-controller/feel result is claimed.

Local source was imported through the authenticated GitHub connector at the exact parent commit because Git's bundled credential-helper shell could not start in this sandbox. The local baseline commit is a snapshot marker, not the original remote history; publish/review uses GitHub Git-data APIs with the real remote parent. Do not force-push the local snapshot history.

## Remaining staged gates

1. Review this standalone PR; agree the single-loop combat seam and roster/input revisions with #6.
2. Bind the Vanguard fight and test two clients, event dedup/gap recovery, prediction/correction, knockouts, reconnect boundaries and shared rewards/rehearsal.
3. Test each count 2–8 at 50/100/150 ms RTT and modest loss; measure bandwidth, host frame time, corrections and event delivery; repeat mini-run soak.
4. Choose Hamachi, ZeroTier or direct IP before the separate-network slice test, rechecking current official limits and the full group's connectivity. This ENet experiment does not choose that option. Steam is not a slice prerequisite.
5. Complete real separate-network runs, including eight players, and human readability/fairness/fun tests. Local scripted capacity does not satisfy these gates.
