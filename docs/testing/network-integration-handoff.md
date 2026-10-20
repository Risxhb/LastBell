# Networking integration handoff

Owner: chzy18/codex, [networking #7](https://github.com/Risxhb/collab/issues/7). Integration owner: risxhb/codex, [foundation #6](https://github.com/Risxhb/collab/issues/6). Encounter/visual owner: chzy18/claude, [#8](https://github.com/Risxhb/collab/issues/8). This is an integration map of tested candidates, not an adopted production contract or runnable Unity package.

## Start here

```powershell
pwsh -NoProfile -File tools/net-test/verify.ps1
```

The [verification entry point](../../tools/net-test/README.md) describes profiles, prerequisites, reports and error behavior. Full runs all five existing suites, including the long virtual cases. The [recorded summary](../../tools/net-test/evidence/verification.json) records the source/report hashes and suite outcomes. Offline success does not close the Unity/Internet/human gates.

A separate [C# TCP loopback prototype](csharp-loopback-session.md) now executes host/client communication in real OS processes, including one host plus seven clients. Run `pwsh -NoProfile -File tools/net-test/socket-session/run.ps1` for that milestone. It retains explicit Unity/Internet/combat limitations and does not select the production transport.

## Review and integration order

The current changes form a stacked dependency chain. Review/land from the bottom upward; this guide does not merge or retarget branches automatically. If a dependency is squash-merged, reconcile the next PR's base/history before merging it. The foundation must select which candidate behavior to adopt, rather than copying every lab into `Assets`.

| Order | PR / branch | What it supplies | Location |
| --- | --- | --- | --- |
| 1 | [#15](https://github.com/Risxhb/collab/pull/15), `feature/unity-session-rules` → `main` | Session/input rules, immutable snapshots, journal/inbox, rule checks | `unity/Assets/LastBell/Network/Rules/`; `tools/net-test/unity/` |
| 2 | [#19](https://github.com/Risxhb/collab/pull/19), `tools/network-fault-lab` → previous | Fault fixture; unreliable action-delivery finding | `tools/net-test/fault-lab/` |
| 3 | [#20](https://github.com/Risxhb/collab/pull/20), `prototype/reliable-ability-input` → previous | Separate action IDs/retries/expiry/status candidate | `tools/net-test/reliable-actions/ActionProtocol.cs` |
| 4 | [#21](https://github.com/Risxhb/collab/pull/21), `prototype/ability-confirmation-wire` → previous | Confirmation wire format, ingress checks and fault evidence | `tools/net-test/reliable-actions/ReceiptCodec.cs` |
| 5 | [#22](https://github.com/Risxhb/collab/pull/22), `tools/network-endurance` → previous | Independent diagnostic clocks, lifecycle/endurance evidence | `tools/net-test/endurance/` |
| 6 | [#23](https://github.com/Risxhb/collab/pull/23), `tools/encounter-event-recovery` → previous | Event wire/chunks/checkpoint acknowledgment candidate | `tools/net-test/event-recovery/` |
| 7 | `docs/network-integration-handoff` → previous | Unified verification and this integration map | `tools/net-test/verify.ps1`; this guide |
| 8 | `prototype/csharp-loopback-session` → previous | Real-process loopback TCP session/input/event and lifecycle tests | `tools/net-test/socket-session/` |

Historical [Godot PR #14](https://github.com/Risxhb/collab/pull/14) is a reference, outside this Unity candidate chain. The [Unity migration plan](https://github.com/Risxhb/collab/blob/docs/2-5d-isometric-direction/docs/plans/unity-migration.md) assigns U0/project/packages and U1/combat to foundation, U3/contracts jointly, and U4/network/content binding to the existing lane owners.

Recommended binding sequence after review:

1. Foundation pins U0 editor, packages, render pipeline, build/test setup and assembly boundaries. Networking evaluates the chosen transport; no package is selected by these tools.
2. Agree identity, one authoritative tick/generation, numeric types, action-delivery choice, event/result schemas and full-snapshot installation in U3.
3. Import only the accepted package-free rules/candidate pieces into lane-owned assemblies through coordinated project changes. Keep lab runners and diagnostic projections outside production `Assets`.
4. Bind authenticated transport, lobby/roster, one game-loop coordinator, selected action delivery and authoritative combat results; then bind event replay/checkpoint state and presentation.
5. Run actual Unity host + one client, then host + seven clients, impairments, lifecycle, real soak/profiling and human timing/readability. See the [acceptance checklist](unity-network-integration-checklist.md).

## Current callable APIs

The signatures below exist in the current source snapshot. Types are qualified by the namespaces shown; no new foundation-owned interface is defined here. `RuleResult` lives in `LastBell.Network.Rules` and exposes `Ok`, `Reason`, `PlayerId`. Calls are serialized on one coordinator thread.

### Session and input: `LastBell.Network.Rules`

Sources: [SessionRules.cs](../../unity/Assets/LastBell/Network/Rules/SessionRules.cs), [SessionTypes.cs](../../unity/Assets/LastBell/Network/Rules/SessionTypes.cs), [InputCodec.cs](../../unity/Assets/LastBell/Network/Rules/InputCodec.cs).

```csharp
new SessionRules(Guid identity, ulong hostPeerId);
RuleResult Join(ulong trustedPeerId);
void Disconnect(ulong trustedPeerId);
RuleResult Control(ulong trustedPeerId, ControlRequest request, long monotonicMs);
RuleResult SubmitInput(ulong trustedPeerId, InputRequest input, long monotonicMs);
Dictionary<long, TickCommand> Advance();
SessionSnapshot Snapshot();

// The existing adapter interface:
ISessionInputSink.SubmitInput(ulong authenticatedPeerId, InputRequest request, long monotonicMs);
```

`Join` returns the stable actor ID in `RuleResult.PlayerId`; the adapter retains its authenticated connection-to-actor mapping. Host actor ID is 1, even when the host's connection handle is 0 or another value. A connection handle, stable actor ID and display slot are three different identities. `Snapshot().ProcessedSequences` is keyed by actor ID and records consumed movement/input sequence, not action-receipt confirmation.

`InputRequest` contains session/generation/sequence/target tick, planar movement and held Basic / Technique / Signature / Guard flags. `InputCodec.Encode` returns 54 bytes; `TryDecode(byte[], out InputRequest, out string)` checks framing and finite movement. Session rules own ownership/rate/tick bounds. Only Vanguard is currently allowlisted. Start requires all players ready, increments generation, and enters Running. Missing commands are neutral.

**Limits that require integration changes:** `Advance()` owns a diagnostic tick, not the accepted production clock seam. Joining during Running is rejected. The model has no coordinated Running→retry/lobby reset or returning-actor/build restoration API. The action/event labs' independent reconnect policies do not establish those session features. Stable IDs increase on new lobby joins; restoring a returning actor's identity requires the agreed coordinator policy.

### Optional separate action stream: `LastBell.Network.ActionPrototype`

Sources: [ActionProtocol.cs](../../tools/net-test/reliable-actions/ActionProtocol.cs), [ReceiptCodec.cs](../../tools/net-test/reliable-actions/ReceiptCodec.cs).

```csharp
new ActionHost(Guid identity, Policy settings, int generation = 1);
void Register(ulong authenticatedPeer, long playerId);
Receipt Receive(ulong authenticatedPeer, Request request, long nowMs);
ConsumedAction[] Step(long completedTick);
void Disconnect(ulong authenticatedPeer);
void ResetGeneration(int generation);

new ActionClient(Guid identity, Policy settings, int generation = 1);
Request Queue(Ability ability, long estimatedCreatedTick, long targetTick, long nowMs);
Request[] Poll(long nowMs);
Receipt Outcome(long id);
void ResetGeneration(int generation);

RequestCodec.Encode(Request request); // byte[56]
RequestCodec.TryDecode(byte[] bytes, out Request request);
ReceiptCodec.TryEncode(Receipt receipt, ulong recipientConnection, out byte[] bytes); // byte[77]
ReceiptIngress.TryApply(byte[] bytes, ulong authenticatedSender, ulong trustedHost,
    ulong localConnection, ActionClient client, out string reason);
```

Create/bind hosts and clients to the actual agreed active generation; do not rely on default generation 1 while the session is still in generation 0. `Register` receives the actor mapping allocated by the coordinator. `ConsumedAction` retains `PlayerId`, `ConnectionHandle`, `Request` and `Tick`. A request ID is scoped by session/generation/connection incarnation; it is not a global actor command ID.

Choose reliable ordered whole-input delivery **or** the separate action stream. If separate actions are accepted, clear the corresponding press bits from the movement-input path: one Technique/Signature/Guard intent must not arrive through both `TickCommand` and `ActionHost`. Held Basic remains an explicitly sampled input. The candidate has no Basic combo, Dash, aim/facing, Resonance, counter-eligibility or authoritative combat-result schema; foundation/encounter owners define those contracts.

`Consumed` currently means one diagnostic scheduler emission, not ability eligibility or activation. The `Step(completedTick)` API expects sequential ticks but is not yet bound to foundation execution order. Agree which stage schedules intents for tick T, which executes combat T, and when completed state/results T publish. Do not independently run `SessionRules.Advance`, `ActionHost.Step` and a foundation clock as three free-running authorities.

### Event/checkpoint stream: `LastBell.Network.EventRecovery`

Sources: [EventWire.cs](../../tools/net-test/event-recovery/EventWire.cs), underlying [EventStream.cs](../../unity/Assets/LastBell/Network/Rules/EventStream.cs).

```csharp
new EventSender(Guid identity, int generation = 1);
void Register(ulong connection);
void Remove(ulong connection);
void Reset(int generation);
EventRecord Append(long tick, string kind, byte[] payload);
byte[] BuildBatch(ulong connection); // null => capture/retry checkpoint
byte[] BuildCheckpoint(ulong connection, long capturedWatermark,
    long capturedTick, byte[] capturedState);
RuleResult ReceiveAck(byte[] bytes, ulong authenticatedConnection);

new EventReceiver(Guid identity, ulong localConnection, int generation = 1, ulong host = 0);
RuleResult Receive(byte[] bytes, ulong authenticatedHost,
    Action<EventRecord> apply, Func<byte[], long, long, bool> install);
byte[] BuildAck();
void Reset(int generation);
```

The checkpoint callback receives `(stateBytes, eventWatermark, completedSnapshotTick)` and must validate/stage/commit atomically. The event callback must be atomic or idempotent. Do not reenter networking state from either callback. Source/recipient/session/generation are validated before application. A checkpoint is retried unchanged until its installation acknowledgment; a cumulative event cursor cannot acknowledge an unsent chunk tail.

`EventSender.Append` currently wraps global events with `PlayerId = 0`. Underlying `EventJournal.Append(long tick, long playerId, string kind, byte[] payload)` supports a player ID, but changing the wrapper/typed combat producer requires review. Do not assume the sample projection is a damage/failure schema. EV batch maximum is 35,081 bytes and candidate checkpoint state maximum is 4,096 bytes; neither selects the eventual transport packet size or full-combat snapshot budget.

### Diagnostic clock: `LastBell.Network.Endurance`

Source: [ClockEstimator.cs](../../tools/net-test/endurance/ClockEstimator.cs).

```csharp
new ClockEstimator(int generation);
bool Add(int generation, double localSent, double hostReceived,
    double hostSent, double localReceived);
bool TryEstimate(double localNow, out double hostNow, out double uncertaintyMs);
void Reset(int generation);
```

The fixture supplies authenticated, nonce-matched four-timestamp probes and generation start time. There is no production clock packet/nonce service. Samples expire after two seconds, only eight are retained, and asymmetry caused up to 81.267 ms error in the endurance evidence. U3 must define clock origin, drift/uncertainty acceptance, pause/background behavior and the time→tick mapping. Frame time, retry milliseconds, simulation tick and event ID are different quantities.

## Binding responsibilities and unresolved decisions

| Seam | Existing candidate hook | Foundation/transport decision still required |
| --- | --- | --- |
| Identity/roster | `Join` → actor ID; `Register(connection, actor)`; event `Register(connection)` | Authenticated connection incarnation; inactive/returning actor, class/build and encounter-boundary policy |
| One authoritative clock | explicit diagnostic `Advance()` / `Step(T)` | Typed C# foundation step, ordering and exactly one authoritative tick owner |
| Input/actions | `SubmitInput`, `Queue/Poll/Receive`, scheduler emissions | Whole-input vs separate stream; no double dispatch; updated kit/aim/cancel semantics |
| Ability result | diagnostic `ConsumedAction` and AR receipt | Eligibility/rejection/activation/completion result, actual gameplay timing and UI for uncertainty |
| Event producer | `EventRecord` and `EventSender.Append` | Typed mechanic/feedback/phase/assignment fields, affected/source IDs and failure vs prevented damage |
| Full checkpoint | `BuildCheckpoint`, receiver `install` callback | Full current roster/state/warnings/phase and other required fields captured with tick/generation/watermark; atomic install and larger snapshots |
| Presentation | idempotent event callback and restored current state | Truthful geometry/assignments; expired history suppression; human warning/Guard readability |
| Transport | bounded codecs, out-of-band authenticated handles | Selected package, integrity, framing/reassembly, receive-rate/send-queue budgets, cancellation and shutdown |
| Lifecycle | action/event generation resets and retired handles | Complete encounter retry, session restart, host-loss explanation; new session/generation after process loss or durable recovery design |

Foundation keeps combat/actor/UI/shared wiring ownership; Claude keeps encounter content/environment/characters. Networking supplies adapter/delivery behavior only after the above seams are agreed. The [detailed acceptance checklist](unity-network-integration-checklist.md) remains unchecked until real evidence is available.

## Findings a green offline run preserves

- Acknowledging processed movement does not prove all prior unreliable action presses arrived. The fault suite intentionally reproduces that diagnostic.
- Some actions were consumed despite an unconfirmed/abandoned client outcome. Never automatically replay uncertain intent with a fresh action ID or label timeout as definite failure.
- Same-session/generation host process restart can replay actions because consumed-history state is not durable. Connection-incarnation history is capped at 128 within a generation.
- Checkpoint recovery restores sample state and skips historical effects; it does not make a warning arrive before its deadline or establish complete combat restoration.
- No Unity editor, sockets, Internet, real wall-clock soak, managed heap/frame budget, human controls/readability or authoritative gameplay acceptance follows from these reports.

Detailed evidence: [session rules](network.md), [fault lab](network-fault-lab.md), [actions](reliable-actions.md), [confirmations](ability-confirmation-wire.md), [endurance](network-endurance.md), [event recovery](encounter-event-recovery.md). Keep #7 open until the agreed staged Unity and human gates pass.
