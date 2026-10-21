# Ability requests and confirmations across processes

Networking lane #7, stacked on PR #28. This extends the diagnostic C# TCP harness with an explicit AP/AR action mode. It uses the existing `ActionHost`, `ActionClient`, `RequestCodec`, `ReceiptCodec` and `ReceiptIngress`; those candidates and shared session rules are unchanged.

```powershell
pwsh -NoProfile -File tools/net-test/socket-session/run.ps1 -ActionStream
```

PowerShell 7 launches one host and either one or seven separate client processes. The default runs Two, Eight and HostLoss twice. IPv4 loopback only, ephemeral ports, hidden test processes and owned-process cleanup remain as in the [socket harness](csharp-loopback-session.md). The default command without `-ActionStream` retains whole-input delivery. The offline verifier remains socket-free.

In action mode, movement and held Basic still use the existing input codec. Fixture-generated Technique/Signature/Guard bits are cleared in that input path, including local host input, to avoid dispatching the same press twice. Remote clients send three immutable action requests: Guard at received tick 6, Technique at tick 18, Signature at tick 30, each targeting three ticks ahead. The host advances the candidate scheduler once per actual session completed tick, after session input advancement. It binds the scheduler to the session's incremented generation when Start succeeds; lobby generation zero must not leak into the running attempt.

Two diagnostic frame opcodes are added: 13 carries the existing 56-byte AP v1 request; 14 carries the existing 77-byte AR v1 receipt. They use the existing bounded TCP framing. Connection handles and stable actor IDs come from the host's socket/session mapping. The client accepts receipts only through its host socket and validates recipient/full request through ReceiptIngress. This fixture routing does not provide cryptographic authentication.

The fault is deliberately **withheld application receipts**, not packet loss in TCP:

| Request | Host behavior | Required observation |
| --- | --- | --- |
| Guard, ID 1 | Suppress the first three replies and any Pending reply | At least four identical-ID attempts; at least one Consumed receipt withheld; exactly one scheduler emission and eventual Consumed confirmation |
| Technique, ID 2 | Suppress every reply even after consumption | One scheduler emission; surviving clients time out after 2 seconds with UnconfirmedTimeout, meaning outcome unknown, and do not generate another action ID for that intent |
| Signature, ID 3 | Send replies twice; inject one wrong-recipient and one unsupported-version reply first | Both invalid replies rejected; duplicate valid replies leave one confirmed result and one scheduler emission |
| Departure Guard, ID 4 | Departing client submits a request targeting ten ticks later, then Quit | Host receives the request once, removes pending work, and never emits ID 4 |

The Eight case keeps all existing capacity, malformed-frame, slow-drip, input/event and departure checks. Each remote actor must emit exactly IDs 1–3; all actors together emit 21 actions. Six survivors finish with two known Consumed outcomes and one unknown. The departing client's unconfirmed request remains pending locally when it exits; that is not a confirmed cancellation result. HostLoss kills only the test-owned host after live exchange and a confirmed retried Guard. It accepts actual EOF or the specific ConnectionReset/ConnectionAborted socket errors, recording which occurred; other socket failures still fail. No final host report is fabricated.

An initial run exposed incorrect generation binding; another exposed a reset while writing a retry during forced host termination. The final implementation addresses both. Evidence in `tools/net-test/socket-session/evidence/ability-confirmations.json` records only final successful source runs; failed development reports remain local. Counts/timing can vary with OS scheduling, so repeats check invariants rather than byte-identical reports.

`Consumed` means diagnostic scheduler emission, **not Guard activation, cooldown eligibility or gameplay success**. There is no real combat callback, Unity binding, Dash/aim mapping, reconnect, host migration, encryption, Internet/RTT/loss test or performance result. No exactly-once promise across a host crash or loss of deduplication history is made. The host's local actor sends only movement/Basic in this fixture; action traffic belongs to remote clients. Production stage ordering, input schema, gameplay completion, clock origin and recovery still need U3 agreement.
