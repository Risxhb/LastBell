# C# loopback socket session prototype

Networking lane #7, stacked on PR #24. This is an executed **TCP socket / separate-process** prototype using the existing C# session/input rules and event-wire candidate. It runs outside Unity, binds only to IPv4 `127.0.0.1` on an ephemeral port, and does not select the game's production networking package.

## Reproduce

From the repository root with PowerShell 7:

```powershell
pwsh -NoProfile -File tools/net-test/socket-session/run.ps1
```

Default: the two-player, eight-player and host-loss scenarios, each repeated twice. Each host/client/probe is a fresh hidden PowerShell process compiling the actual C# sources with language version 7.3 and warnings treated as errors. No .NET SDK, Unity project, editor, package install or account is required.

Options:

```powershell
pwsh -NoProfile -File tools/net-test/socket-session/run.ps1 -Scenario Eight -Repeat 1
```

`-Scenario` accepts `All`, `Two`, `Eight`, `HostLoss`. `-Repeat` accepts 1–5; it repeats real runs, not deterministic byte-for-byte report comparison. `-OutputDirectory` chooses the parent output folder. Each invocation writes a fresh timestamp/nonce directory with per-worker JSON/logs and aggregate `summary.json`. Default `results/` is ignored. A failed case returns exit code one after cleanup. The controller only terminates `Process` instances it started, never processes selected by name. Child windows are suppressed with `CreateNoWindow`.

The Codex sandbox allowed creating the listener but denied client connects with socket error 10013. The recorded successful runs used approved execution outside that sandbox. If your environment denies loopback, the connection test is blocked; do not treat a bind-only check as socket communication evidence. No firewall, router, VPN or machine networking settings were changed. The runner never elevates itself or opens a LAN/public listener.

This command is separate from the [five-suite offline verifier](../../tools/net-test/README.md). The offline command intentionally remains socket-free and does not count these process tests as in-memory cases.

## Executed cases

Recorded [aggregate evidence](../../tools/net-test/socket-session/evidence/loopback-session.json): PowerShell 7.6.5 / .NET 10.0.11, Windows, 2026-10-20. All six cases passed. The complete run launched 34 child processes, including protocol probes; these were actual OS processes, not tasks representing clients inside one host. Each case records distinct player PIDs, and compares client-reported PID/connection/actor fields with the controller's launched process IDs and host mapping. The PID in the hello is an audit claim, not a security identity.

| Scenario, repeated twice | Player processes | Additional probes | Observed result |
| --- | ---: | ---: | --- |
| Two | Host + one client | 0 | Two-member ready/start gate; 180 authoritative diagnostic ticks; 49 remote inputs consumed and 99 total commands including host input; 18 event effects applied once; graceful shutdown |
| Eight | Host + seven clients | 5 | Eight-member roster, ninth rejection while lobby is full, malformed peer isolation, one client disconnect; 313 remote inputs / 363 total commands; six surviving clients each apply all 18 effects once |
| HostLoss | Host + one client | 0 | After live tick/input/event exchange at tick 30 or later, controller kills only its host; client observes socket EOF after three events; no graceful-shutdown claim |

The eight-player case's extra probe processes never become player slots. They test capacity (`session_full`), an excessive frame length, a truncated frame, a stalled partial frame and a wrong room GUID. The server closes each offending connection while the seven legitimate clients remain connected. Then one legitimate client sends Quit after tick 60; the host removes its actor and pending input. The remaining six continue through the normal end. The departing client is not expected to receive future/in-flight events after it quits (five effects were observed before departure).

Both regular runs finish at tick 180 after stopping input generation at tick 150 and draining. Every surviving client's final processed input sequence equals its sent-input count. The host verifies its accepted sequence equals processed sequence for each active peer and that no commands are emitted for unknown/retired actors or twice for the same actor/tick. Event replay/acknowledgment must finish with no unacknowledged host tail. Each survivor has exactly 18 effects and zero duplicates.

Scope/validation probes on the first legitimate client send malformed input version, wrong session, old generation, and an identical duplicate input sequence. The first three are rejected; duplicate sequence is stale; valid subsequent input continues. All seven clients duplicate their first valid input. Event frames are deliberately duplicated/coalesced early in the run, and the inbox suppresses repeat feedback. These are application-level duplicates over TCP, not duplicated IP packets.

For normal shutdown the host sends the final tick/counter, closes the session and exits. For host loss there is deliberately no final host report; `hostKilled: true`, the actual host PID and surviving client's EOF/state evidence are recorded instead. Every case waits for its workers, cleans up only owned processes, and requires a post-exit loopback connection attempt to fail specifically with `ConnectionRefused`; timeout or access denial does not count as a closed-listener pass.

## What crosses the sockets

The host is one process containing `SessionRules` and a diagnostic local actor. Seven remote actors use seven other processes in the full-roster case. Network callbacks and `Advance()` run on a single host coordinator thread. A real monotonic stopwatch paces the diagnostic 60 Hz tick; this is not the earlier accelerated virtual clock, and not a measurement of production frame performance.

1. The host binds an OS-assigned loopback port and publishes port, room GUID and PID in a per-run endpoint file. The controller gives those fixture parameters to client processes. There is no discovery service or join UI.
2. Clients exchange Hello/Welcome/Ready over TCP. The host assigns connection incarnation and stable actor ID; clients cannot put an actor/peer ID in `InputCodec` payloads. The test coordinator holds a file-based start barrier to run the ninth-player probe while the eight-member lobby is full. The actual session ready/start rules still decide whether start is allowed.
3. Start carries generation/tick. Clients sample input using received diagnostic ticks and submit the existing 54-byte `InputCodec` payload over a reliable ordered TCP stream. Host ticks publish each peer's processed sequence. Held Basic and Guard flags are diagnostic commands only; no combat ability executes.
4. The existing EV codec carries initial checkpoints, event batches and checkpoint/event acknowledgments. Its opaque sample state is a single counter. Fresh events use `socket_counter` and an eight-byte value, with `EventSender`/`EventReceiver` providing replay and deduplication. The candidate's global `PlayerId=0` event wrapper remains unchanged.
5. StopInput ends generation before final draining; Shutdown reports final tick/counter. Client Quit retires that connection immediately. Host loss is detected from the actual socket closing, not a fabricated in-memory callback.

The input policy here is **whole input on reliable ordered TCP**, one of the previously documented alternatives. AP/AR separate ability request/receipt traffic is not exercised through sockets by this prototype. This does not decide which policy/Unity transport should ship.

## Stream framing and bounds

`SocketWire.cs` adds a four-byte unsigned **big-endian** body length, followed by one opcode and payload. Body length includes the opcode and must be 1–65,536. Message payload fields written by the fixture use little-endian `BinaryWriter` values and `.NET Guid.ToByteArray()` layout; IP/TCP framing is separate from AP/AR/EV formats.

| Opcode | Meaning | Payload |
| --- | --- | --- |
| 1 / 2 | Hello / Welcome | Hello version 1, room GUID, client PID / room GUID, assigned connection handle and actor ID |
| 3 / 4 | Ready / Start | Empty / generation int32 and tick int64 |
| 5 / 6 | Input / Tick | Existing 54-byte input / completed tick int64 and per-peer processed sequence int64 |
| 7 / 8 | Events / EventAck | Existing EV frame bytes |
| 9 | Reject | Bounded strict UTF-8 reason on receive |
| 10 | Shutdown | Final tick int64 and counter int64 |
| 11 / 12 | Quit / StopInput | Empty |

The parser retains partial headers/bodies, handles coalesced frames, and checks the declared size before allocation. Hello and selected input frames are deliberately written in separate chunks; duplicate events/inputs can be written in one coalesced write. TCP is free to split/combine reads differently, so parsing cannot assume write boundaries equal read boundaries.

The per-connection queued-frame cap is 32; partial frames time out after 1,000 ms without progress. Socket writes have a 2,000 ms timeout, and unintroduced peers have a 3,000 ms hello deadline. The host caps live connections at 16, total accepted connection records at 128, and accepts at most eight per coordinator iteration. These are prototype limits, not reviewed production load budgets. Disposing a channel clears its queued/body buffers.

An early development probe exposed an incorrect exception-category filter: invalid frame lengths could terminate the host. The fix explicitly handles `InvalidDataException` at the offending peer boundary. The final eight-player runs execute that regression, truncated/stalled frames and valid traffic in the same session; the host continues and closes normally.

## Limits and integration handoff

- **Real sockets/processes: yes. Unity editor, LAN/Internet, multiple machines, NAT/VPN or production package selection: no.** TCP loopback is a diagnostic transport choice only.
- This is not a login/authentication or encrypted transport. The room GUID and PID are fixture/audit metadata, not credentials. Socket-to-actor association prevents a payload-selected peer mapping in this prototype; production authenticated identity/integrity remains U3 work.
- No injected packet loss/RTT/retransmission delay, bandwidth/frame-time/heap benchmark, long wall-clock soak, GUI, controller or human responsiveness result is claimed. TCP head-of-line behavior needs separate testing.
- No Unity combat, damage eligibility, real warning geometry/phase state, full combat checkpoint or AP/AR socket integration was implemented. The event state is a counter, and command consumption is not ability activation.
- A disconnected actor does not rejoin a running encounter; retry/returning-build restoration and host migration remain unimplemented. The forced-loss case validates EOF in the executed environment; alternative reset/error behavior on other systems is not claimed to pass.

Use this evidence as an additional transport/process milestone alongside the [offline integration handoff](network-integration-handoff.md). Foundation still owns editor/packages/shared wiring and U3 gameplay/state interfaces. Actual Unity host + seven clients and human Internet/readability gates remain open in #7.
