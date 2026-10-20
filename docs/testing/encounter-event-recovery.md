# Encounter event delivery and recovery candidate

Networking lane, issue #7. Tools-only follow-up to PR #22, built around the existing `EventJournal` and `EventInbox` from PR #15. This adds event/acknowledgment/checkpoint byte framing, a bounded sender/receiver candidate, and reproducible fault evidence. It does not implement combat, boss scheduling, damage eligibility or encounter visuals. No existing Unity rules or project files are changed.

## Run and evidence

Run in a fresh PowerShell 7 process from the repository root:

```powershell
pwsh -NoProfile -File tools/net-test/event-recovery/run.ps1
```

`-Report` can select another JSON path. The default `results/` folder is ignored. No Unity editor or SDK installation is required. The runner compiles C# 7.3 with warnings treated as errors, runs the boundary suite and fault scenarios twice, and exits nonzero on any failure or repeat mismatch. The committed [report](../../tools/net-test/event-recovery/evidence/event-recovery.json) records seeds, conditions, counts, runtime and explicit scope flags.

Executed on 2026-10-20 with PowerShell 7.6.5 / .NET 10.0.11:

- 11 boundary groups / 3,217 assertions passed, matching exactly on repeat.
- 63 matrix cases: every subscriber count 2–8 × RTT 50/100/150 ms × loss 0/3/10%, with independent ±20 ms one-way jitter and 2% duplication. The last viewer has a 1.5-second blackout at 4–5.5 seconds. These are diagnostic projection subscribers around one journal, not launched players or proof of the eventual host-plus-seven-client topology.
- Four eight-viewer probes at 150 ms RTT / 10% loss: 15-second history overflow, two-second acknowledgment-only outage, reconnect under a fresh incarnation, and generation reset with old traffic still in flight.
- Every case produces diagnostic events at 20 Hz for 30 virtual seconds, then drains for ten seconds. Event batches/checkpoints resend every 50 virtual milliseconds, including while waiting for a lost acknowledgment. The same symmetric-delay fault link from #19 is used; all viewers traverse it.
- All 67 scenarios caught up in state and event cursor, with no remaining buffered records or unacknowledged host tail. Both full passes matched exactly.

One-pass totals:

| Measurement | Result |
| --- | ---: |
| Host events issued | 40,200 |
| Diagnostic feedback records issued | 32,697 |
| Event applications across all viewer incarnations | 207,528 |
| Feedback effects across all viewer incarnations | 169,085 |
| Historical feedback effects suppressed at checkpoint | 444 |
| Checkpoints installed, including bootstrap/reset/reconnect | 358 |
| Duplicate effects / projection conflicts | 0 / 0 |
| Sent / dropped / duplicated / delivered virtual packets | 544,758 / 28,156 / 10,307 / 526,909 |
| Additional acknowledgment-only outage drops | 39 |
| Old recipient/generation/checkpoint frames rejected | 8 |
| Peak buffered inbox / observed frame size | 8 records / 448 bytes |

Host event counts and viewer effect counts have different populations. In the reconnect/reset probes, totals include earlier incarnations/generations, while final catch-up checks target the active roster and current generation. For each current viewer, replayed feedback effects plus feedback represented by checkpoints must equal the authoritative generation's feedback count.

The overflow probe installed ten checkpoints across eight viewers, restored current diagnostic state, and suppressed 248 historical feedback effects. It resumes fresh feedback once, rather than showing a burst of old damage-style effects. These are sample counters, not damage calculations or proof of combat checkpoint restoration.

## Byte format: EV version 1

All integers are little endian. GUIDs use `.NET Guid.ToByteArray()` layout. UTF-8 decoding/encoding is strict; invalid byte sequences or unpaired UTF-16 surrogates are rejected. The common header is 40 bytes:

| Offset | Bytes | Field |
| --- | ---: | --- |
| 0 | 2 | ASCII `EV` |
| 2 | 1 | Version 1 |
| 3 | 1 | Kind: batch 0, cursor ack 1, checkpoint 2, checkpoint ack 3 |
| 4 | 8 | Bound connection incarnation: destination for host frames, authenticated origin for acknowledgments |
| 12 | 16 | Session GUID, nonempty |
| 28 | 4 | Generation, nonnegative |
| 32 | 8 | Event watermark/cumulative applied cursor, nonnegative |

Frame bodies:

| Kind | Body after header | Limit |
| --- | --- | --- |
| Batch | One-byte count; then records | At most 8 records; at most 35,081 total bytes |
| Cursor ack | No body | Exactly 40 bytes |
| Checkpoint | Signed 64-bit completed snapshot tick, unsigned 16-bit state length, opaque state bytes | Tick nonnegative; at most 4,096 state bytes / 4,146 total bytes |
| Checkpoint ack | Signed 64-bit installed checkpoint tick | Tick nonnegative; exactly 48 bytes |

Each batch record contains signed 64-bit event ID, tick and player ID; unsigned 16-bit UTF-8 kind length and bytes; unsigned 16-bit payload length and bytes. Event IDs are positive and no greater than the advertised watermark. Tick/player ID are nonnegative. Kind is nonempty, at most 64 UTF-16 code units and 256 encoded bytes. Payload is at most 4,096 bytes. Session/generation come from the enclosing frame. Conflicting copies of an ID within one batch are rejected. No trailing bytes are allowed.

Decoding completes before any inbox/application mutation. Boundary checks include fixed byte vectors for all four frame types, maximum-size records/checkpoints, truncated frames, oversized lengths/counts, invalid UTF-8, invalid scope/bounds, and a malformed second record after a valid first. Random tests include 2,048 arbitrary inputs and 1,024 mutations that retain a valid header; successful structured decodes must canonically round-trip.

The maximum frame sizes are parser budgets, not selected transport packet sizes. These frames can exceed a datagram's usable size. Fragmentation/reassembly, integrity, rate limits, send queues and a bounded large-snapshot protocol must be designed and tested with the chosen Unity transport. A real full combat snapshot may not fit the candidate's 4 KiB opaque-state limit.

## Delivery and checkpoint invariants

`EventSender` wraps the existing journal, retaining its 128-event replay history and scope checks. It admits at most eight current subscribers and 128 distinct connection incarnations per generation. The trusted coordinator owns registration, removal and generation changes. A returning actor receives a fresh incarnation and a checkpoint; it does not inherit another endpoint's acknowledgment or replay a previous endpoint's historical visual feedback.

Two adapter constraints are covered by focused regressions:

1. **The advertised stream head is not an acknowledgment allowance.** The journal's `ReplayFor` exposes and marks its whole retained window sent, while this candidate sends only eight records per frame. The wrapper independently tracks the highest event actually included in a frame or checkpoint. A client cannot acknowledge an unsent tail merely by echoing the advertised watermark. The regression advertises event 20, sends only 1–8, rejects ack 20, accepts ack 8, and verifies 9 onward remain available.
2. **Sending a checkpoint is not installation confirmation.** A lost bootstrap snapshot must not enable normal replay from an uninitialized projection. The sender freezes/retries the same checkpoint bytes until an acknowledgment identifies its completed tick and covers its watermark. A plain cursor acknowledgment cannot satisfy bootstrap, including at watermark zero. The receiver rejects batches before installing a checkpoint. Lost checkpoint acknowledgments lead to idempotent snapshot retries, not repeated effects.

The receiver checks adapter-supplied authenticated host identity, destination incarnation, session and current generation before applying a frame. Generation changes only through the trusted local reset coordinator; incoming frames cannot advance it. Buffered events apply in contiguous ID order; gaps withhold the applied cursor, duplicates do not repeat effects, and conflicting buffered records reject the whole offer. Already-applied records are ignored by cursor, not retained in an unlimited content cache. Receive/apply/reset must be serialized; callbacks must not reenter or mutate networking state.

A checkpoint carries the event watermark and completed snapshot tick alongside opaque state. The application callback must validate and stage a complete snapshot, then commit atomically. Only after that callback succeeds does the inbox install the cursor. Stale snapshot ticks/watermarks cannot roll state back; duplicate checkpoint identity/content does not repeat installation; changed content under the same checkpoint identity is rejected. A callback that throws or returns false must not partially mutate application state.

The diagnostic checkpoint is exactly three signed 64-bit counters: feedback total, sample phase and warning end tick. A staging validator rejects malformed values before changing the projection. This is intentionally not a production combat schema. Its purpose is to prove that state restoration and event-cursor restoration happen together, while skipped historical feedback is counted explicitly.

Warnings are represented as current state with an authoritative deadline. An expired recovered warning must not become visible again merely because an old open event arrived late. The diagnostic visibility check treats the end tick as exclusive. Real presentation must use the agreed authoritative clock estimate and foundation-owned geometry/lifecycle; this suite supplies neither actual warning meshes nor combat timing judgments. Recovery cannot make a warning arrive before its deadline during an outage; timely readability/fairness remains a separate Unity and human acceptance gate.

The event application callback must be atomic or idempotent per session/generation/event identity. If it throws, the inbox retains the event and does not advance acknowledgment. The regression injects an exception after an idempotent effect sink, then retries without duplicating that effect. This does not promise exactly-once effects across a non-idempotent callback, process crash or lost persistent state.

## Foundation and encounter handoff

Before production binding, U3 must define:

- Actual damage/failure feedback, warning lifecycle/assignments, phase and other event payload schemas. Preserve mechanic failure versus prevented damage and stable event/player/source identities; this tool does not decide those rules.
- A complete snapshot captured atomically with its completed tick, generation and event watermark. Include all current warning/phase/roster and other required state, not only a counter. Which state is restored for observation versus authoritative simulation is a foundation decision.
- Staged/atomic snapshot installation and idempotent feedback application; an explicit policy for suppressing expired historical effects and drawing still-active warning state.
- Authenticated host/peer metadata and message integrity. Codecs alone do not authenticate anything; acknowledgments also cannot cryptographically prove a client really applied state.
- Transport frame/chunk/large-checkpoint budgets, rate limiting, retries, backpressure, lifecycle/cancellation and host-loss policy. The 50 ms fixture resend interval is experimental.

No Unity APIs, sockets, actual encounter checkpoints, boss mechanics, art, frame-time/heap profiling, host migration or human readability checks ran. The existing fault lab and reliable-action/receipt suites passed independently in fresh PowerShell processes. See the [Unity integration checklist](unity-network-integration-checklist.md); issue #7 remains open for those gates.
