# Unity networking migration: C# rules and event delivery

Production engine is Unity/C#. The [migration plan in PR #13](https://github.com/Risxhb/collab/blob/docs/2-5d-isometric-direction/docs/plans/unity-migration.md) governs this work. Owner: chzy18/codex, [#7](https://github.com/Risxhb/collab/issues/7). The [foundation lane](https://github.com/Risxhb/collab/issues/6) owns exact editor/packages, shared assembly/scene wiring and U3 combat contracts.

## Migration disposition

| Prior work in PR #14 | Current treatment |
|---|---|
| Godot session model | Behavior port into package-free C# |
| Godot ENet adapter and test scene | Historical reference; replacement waits for U0 networking choice |
| Godot process/check evidence | Preserved in [historical draft PR #14](https://github.com/Risxhb/collab/pull/14); never Unity evidence |
| Session IDs, ownership, readiness and sequence rules | Preserved with typed C# inputs and explicit validation |
| Event delivery follow-up | Implemented as a transport-free journal/inbox with acknowledgment, duplicate suppression, replay and explicit checkpoint fallback |

No Unity project/editor/package version is implied by placing candidate source under `unity/Assets/LastBell/Network/Rules/`. These four files have no UnityEngine, NGO, Mirror or Godot references. They are source for review and eventual import, not a runnable Unity host/join scene. C# 7.3 syntax is enforced by the independent check harness. This does not establish the selected Unity runtime/API profile, import, IL2CPP or build compatibility.

## Executed checks and reproduction

From a fresh **PowerShell 7** process in the repository root:

```powershell
pwsh -NoProfile -File .\tools\net-test\unity\check.ps1
```

The script compiles the four rules files and `SessionChecks.cs` using PowerShell's Roslyn compiler with `/langversion:7.3 /warnaserror`. It requires no .NET SDK, package restore, downloaded library, account or network. Run in a fresh process to avoid Add-Type type/assembly reuse. Output defaults to ignored `tools/net-test/unity/results/`; pass `-Report C:\absolute\report.json` to change it.

Fourteen scenarios cover:

- Sender ownership, eight total slots/ninth rejection, duplicate Vanguard choice, host-only start and ready gates.
- Finite movement, overflow-safe normalization, target/sequence bounds, rate-window boundaries, input coalescing, once-only press consumption, processed acknowledgments, neutral missing input and immediate disconnect cleanup.
- Explicit 54-byte wire round-trip plus a fixed golden vector; truncated/extended packets, version/flag mismatch and NaN are rejected.
- Event loss/reordering, duplicate suppression, contiguous application acknowledgments, unsent/forged/stale acknowledgments, bounded history overflow, checkpoint resume, generation/session isolation, invalid atomic batches and application failure before acknowledgment.
- Separate **in-memory** runs for every count 2–8, each over 300 ticks, passing input through the actual codec and checking per-actor ownership/processed sequence. These runs do not open network sockets.

[The recorded report](../../tools/net-test/unity/evidence/csharp-rules.json) identifies the exact executed environment: PowerShell 7.6.5, Roslyn assembly 5.0.0.0, C# 7.3, .NET 10.0.11, Windows 10.0.26200 x64. All 14 scenarios passed. Assertions include repeated per-tick invariants; their count is not a count of independent test cases.

**Unity Editor executed: false. Network sockets executed: false.** The legacy local ENet runs remain separately historical. No Unity host/join, render, controller, internet, latency, bandwidth, IL2CPP or soak evidence is claimed.

## Candidate typed contract

These APIs are session-only proposals for U3 review. They do not replace foundation-owned combat types.

- `SessionRules(Guid, ulong hostPeerId)` allocates the host as stable player ID 1. Transport peer IDs remain opaque handles; the host need not have peer ID 1. Connection approval calls `Join(trustedPeerId)`; transport callbacks call `Disconnect(trustedPeerId)`. Stable IDs increase monotonically within a session; display slots 1–8 may be reused in the lobby.
- `Control(trustedPeerId, ControlRequest, monotonicMs)` handles class/ready/start/sync. Only Vanguard is enabled for this increment; duplicate classes work. Class changes clear ready state. Only the host starts, after all current members ready. Start increments generation and resets tick, input sequences, acknowledgments and queues.
- `ISessionInputSink.SubmitInput(trustedPeerId, InputRequest, monotonicMs)` validates the authenticated mapping, session/generation, finite planar input, sequence and target bounds. Trusted peer ID is a separate adapter argument, never a field decoded from a client.
- `Advance()` is one explicit diagnostic tick and returns commands keyed by stable actor ID. There is no wall clock, MonoBehaviour, physics authority or animation dependency. U3 must replace this diagnostic clock with **one** agreed authoritative combat loop; do not advance this plus another 60 Hz combat clock independently.
- `Snapshot()` returns detached immutable player records and copied roster/acknowledgment collections. Snapshot tick means last completed diagnostic step. Session snapshot schema is version 1; it is not a combat save/checkpoint.
- All calls are single-threaded on the owning coordinator thread. A future adapter marshals callbacks there and authenticates the authoritative host before accepting state/events.
- New peers cannot join a running encounter. Reconnection identity/build recovery, retry/reset boundaries, ready/reward synchronization and live mechanic reassignment are integration work, not implemented by this port.

Input policy matches the legacy spike: 120 shared requests/peer/one-second window; sequence increases by 1–1024; target in [host tick−6, host tick+12], applying no earlier than next tick. Effective target ticks cannot regress. Movement is clamped to one. Same-tick inputs preserve any press while taking the newest movement/held Basic. Missing input is neutral, including Basic; the adapter must sample held Basic each desired tick or agree a later explicit policy. Processed acknowledgment advances only when a command is consumed.

C# movement uses finite IEEE-754 doubles and scaled normalization to avoid overflow when squaring huge finite components. This is an intentional numerical implementation difference from Godot Vector2; it does not claim bitwise parity or cross-platform determinism. Foundation U3 must agree the eventual common numeric type/tolerances and XZ conversion.

### Candidate input wire format v1

Little endian, exactly 54 bytes:

| Offset | Field |
|---|---|
| 0 | Version byte 1 |
| 1–16 | Session Guid in documented .NET `Guid.ToByteArray()` ordering |
| 17–20 | Generation, signed int32 |
| 21–28 | Input sequence, signed int64 |
| 29–36 | Target tick, signed int64 |
| 37–44 / 45–52 | Planar X/Y, IEEE-754 binary64 |
| 53 | Bits 0 Basic, 1 Technique, 2 Signature, 3 Guard; other bits rejected |

The fixed payload cannot contain actor ID, health, damage, loot or phase. `TryDecode` rejects incorrect size/version/flags/nonfinite movement; the model validates scope and numeric scheduling bounds. This is an explicit candidate serializer, not Unity automatic serialization or a promise that NGO/Mirror accepts it without an adapter. Future control/event codecs must likewise bound lengths/types before constructing objects; they are not supplied here.

## Event acknowledgment and recovery

`EventJournal` and `EventInbox` are reusable delivery rules with default 128-record retention/buffer, configurable 1–1024. Each immutable envelope has session identity, generation, event ID, tick, player ID, a bounded kind string and copied opaque payload (maximum 4096 bytes). Event IDs increase within a generation.

Combat payload schemas remain foundation-owned. The stream does not reinterpret a guarded outcome as mechanic success. A fixture confirms that a payload carrying guarded damage plus mechanic failure remains unchanged. Real combat producers, typed outcome decoding and wire serialization must be agreed in U3.

Host flow:

1. Register authenticated peer handles; remove them on disconnect.
2. Append authoritative records, then call `ReplayFor(peer)`. The journal remembers the highest event watermark sent to that peer.
3. Accept acknowledgments only for the correct registered peer/session/generation, monotonically and no higher than what was sent. Acknowledgment is delivery state only; it cannot decide combat results.
4. Resend retained events after the applied acknowledgment. If that point has fallen out of history, return `NeedsCheckpoint` rather than silently skip events.
5. Build/send a complete authoritative snapshot through the foundation seam with an event watermark; only then call `MarkCheckpointSent`. The journal does not create combat checkpoints.

Client flow:

1. Authenticate the host and verify generation at the session boundary. Offer bounded event batches; validate the whole batch before mutation. Conflicting duplicate IDs or wrong scope reject atomically.
2. `ObserveWatermark` from authoritative snapshots detects a missing final batch even if no later event arrives. Request replay from `LastApplied` on a gap.
3. `ApplyNext` only presents the next contiguous record. It advances the acknowledgment after the application callback returns; a failed callback retains the record without acknowledging it. Consumers must apply atomically/idempotently, because a callback that partially changes external state then throws cannot be rolled back by this stream.
4. Suppress IDs already applied within the generation. Bound out-of-order buffering; overflow explicitly requests checkpoint.
5. The trusted coordinator installs a full authoritative snapshot and its matching event watermark atomically, then calls `InstallCheckpoint`. Clear buffered feedback and start at that watermark. Do not replay skipped old cosmetic effects. An event packet alone cannot change generation.

Generation changes require an explicit trusted snapshot/reset boundary. Peer/session/generation validation, immutable payloads and bounded history are not public-account authentication, a transport, a complete combat checkpoint, or crash-durable exactly-once delivery.

## Networking stack proposal and compatibility gate

Proposed in [#6](https://github.com/Risxhb/collab/issues/6#issuecomment-6286171529); selection remains with the shared bootstrap owner.

| Candidate | Fit to evaluate | Required proof |
|---|---|---|
| NGO + Unity Transport | First evaluation candidate for the GameObject project; official framework layers over transports including UTP | Exact editor/package compatibility, custom-message/approval binding, explicit loop ownership, direct-IP/headless Windows tests |
| Mirror + KCP | Alternative high-level GameObject stack with KCP transport | Same capacity/ownership/delivery criteria and measured Windows latency/loss behavior |

Unity describes NGO's GameObject/MonoBehaviour focus and UTP interoperability in its [official repository](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects). Its [connection approval documentation](https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.13/manual/basics/connection-approval.html) is a starting point for the eight-slot gate; the exact chosen package's API must be checked after U0. Mirror documents its [KCP transport](https://mirror-networking.gitbook.io/docs/manual/transports/kcp-transport). Sources reviewed October 20, 2026. These options do not select a package version, install it or authorize a paid service.

After U0 pins editor/packages, implement the selected adapter/test scene through foundation-owned assembly/scene wiring and run one host + seven clients/ninth rejection, ready/start gates, actual disconnect/host loss and event resend/checkpoint cases. Pin package lock files in the bootstrap PR. Test Windows client/headless builds before production binding; no cloud/Steam dependency for the slice. Hamachi/ZeroTier/direct-IP setup is selected separately before internet tests.

Then bind the C# Vanguard fight through U3: prediction/correction, authoritative outcomes, shared assignment policy and retry/reward/reconnect boundaries. Test every count 2–8, 50/100/150 ms RTT and modest loss, bandwidth/frame time/event delivery and soak. Full separate-network eight-player mini-runs and human readability/fairness remain required.
