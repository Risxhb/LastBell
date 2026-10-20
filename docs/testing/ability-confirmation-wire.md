# Ability confirmation wire prototype v1

Tools-only follow-up to [PR #20](https://github.com/Risxhb/collab/pull/20), based on `c34f3071050535ad27ccee84cf1bd683e37c775b`. Session owner: chzy18/codex, [#7](https://github.com/Risxhb/collab/issues/7). Read [the reliable-action semantics](reliable-actions.md) first.

The request path already serialized bytes; the confirmation path previously passed Receipt objects directly. It now encodes a bounded versioned frame and validates it through `ReceiptIngress` before changing client state. Production session/core/Unity/arena files are unchanged; U3 review is still required.

## Run and actual evidence

```powershell
pwsh -NoProfile -File .\tools\net-test\reliable-actions\run.ps1
```

Use a fresh PowerShell 7 process from the repository root. Optional `-Report C:\absolute\wire-report.json` selects another output. No SDK, package restore, Unity project or sockets are needed.

The runner now executes and repeats both suites:

- Existing 9 action-boundary groups: 390 assertions.
- New 8 receipt-wire groups: 2,467 assertions, including all strict prefixes, malformed semantic frames and 2,048 seeded random malformed inputs.
- Existing 65 fault cases, with **both requests and confirmations encoded/decoded as bytes**, including host-local confirmations.

[Recorded evidence](../../tools/net-test/reliable-actions/evidence/confirmation-wire.json): C# 7.3 with warnings-as-errors, PowerShell 7.6.5 / .NET 10.0.11; both repeated suites matched exactly. The 65 cases produced 21,635 encoded replies and 21,102 accepted confirmation deliveries (including idempotent duplicates). All legitimate received frames passed ingress. Invalid-frame tests are separate and require rejection without pending/result mutation.

Outcome metrics match the PR #20 baseline: 6,620 generated requests, 6,617 single diagnostic consumptions, 3 confirmed expiries, no duplicate/late consumptions and no unknown final outcomes after drain. This is not a network throughput or real-game success-rate measurement.

Unity Editor executed: false. Network sockets executed: false. Combat ability activation validated: false. The existing fixture's exact shared clock and experimental TTL/retry choices remain unchanged.

## Fixed frame

Exactly **77 bytes**, little endian. Unknown header/version, extra bytes, truncation and unsupported codes are rejected. No length-controlled allocation or unbounded reason string is read from the packet.

| Offset | Size | Field |
|---|---:|---|
| 0–1 | 2 | ASCII `AR` (ability receipt), distinct from `AP` request |
| 2 | 1 | Version 1 |
| 3–10 | 8 | Unsigned recipient connection-incarnation handle |
| 11–66 | 56 | Complete AP v1 request, including its own header/version |
| 67 | 1 | Frozen v1 status code |
| 68 | 1 | Frozen v1 reason code |
| 69–76 | 8 | Signed completed host tick for this result |

The nested AP request retains .NET Guid byte ordering, int32 generation, int64 ID/creation/target/expiry fields and one ability byte. Full request identity is echoed so a valid action ID cannot be reused to confirm different timings or another ability. Handles are adapter-owned routing identifiers, not secrets or evidence of authentication.

Wire status codes are explicitly mapped independently of the local enum's numeric representation:

| Code | Meaning |
|---:|---|
| 0 | Pending |
| 1 | Consumed by diagnostic scheduler |
| 2 | Expired |
| 3 | Rejected |
| 4 | RetryLater |
| 5 | Cancelled |
| 6 | HistoryUnavailable |

`UnconfirmedTimeout` is local-only and cannot be encoded or accepted from the wire. Consumed still does **not** mean combat eligibility/activation; foundation U3 owns final combat completion semantics.

Reason codes: 0 none; 1 deadline; 2 unknown_sender; 3 wrong_session; 4 wrong_generation; 5 invalid_clock; 6 rate_limited; 7 conflicting_id; 8 invalid_id; 9 history_unavailable; 10 invalid_timing_or_ability; 11 pending_full. Status/reason pairs are allowlisted, not arbitrary combinations. For example Expired requires deadline, and Pending/Consumed require no reason. Arbitrary raw text is not serialized.

Changes to codes or nested schemas require explicit version/compatibility review. Tests freeze every current status/reason byte and an independent full golden packet.

## Decoder and consistency checks

`ReceiptCodec.TryDecode` returns a validated immutable ReceiptMessage or false with a null output. It does not mutate client state or authenticate a sender. `TryEncode` returns false for unrepresentable results instead of throwing for invalid caller data.

Canonical echoed identities require a nonempty session, nonnegative generation, positive action ID, known ability, safe nonnegative creation tick, target within 12 ticks of creation and before expiry, and an expiry no more than the prototype's maximum 60-tick configured lifetime. There is no receipt for an invalid zero/negative ID or malformed original timing: use a bounded generic protocol-error/drop path for such raw requests. Do not assume every rejected arbitrary input can be echoed into a matching-action confirmation.

Result consistency:
- Pending must still have time for the earliest next-step consumption and cannot claim admission beyond the prototype's six-tick future-creation tolerance.
- Consumed must have a positive host tick at/after the requested target and strictly before expiry.
- Expired must be at a host tick where the next possible step reaches/passes exclusive expiry. Cached consumed results remain Consumed even if retrieved after the deadline.
- RetryLater, Rejected, Cancelled and HistoryUnavailable require their corresponding allowlisted reasons. They do not invent combat activation outcomes.

The parser has no expected-session or party context. A structurally valid frame from a different session/generation is rejected at ingress by matching the client's actual request state.

## Authenticated routing and state changes

Call `ReceiptIngress.TryApply(bytes, authenticatedSender, trustedHost, localConnection, client, out reason)`:

1. Obtain authenticated sender and expected host/recipient handles from the adapter/session setup. They must never come from claimed payload fields.
2. Reject a wrong sender before decoding. Decode the fixed frame.
3. Reject another connection's recipient handle, even when two clients have identical session/action IDs and request fields.
4. Match the full echoed request against that client's pending or retained completed entry. Unknown IDs, sessions, generations, ability or timing changes cannot confirm it.
5. Reject contradictory definitive outcomes (Consumed versus Expired, or a changed definitive result tick) without changing state.
6. Apply accepted results through the existing client. A duplicate terminal receipt is idempotent. An old Pending cannot reopen retries, and a later HistoryUnavailable response cannot erase an already known result. A valid late receipt may resolve a retained local unknown timeout.

The transport must establish sender authenticity and integrity. A matching numeric handle in this test API is a contract supplied by a trusted adapter, not cryptographic authentication, account identity or protection supplied by the codec itself. Do not broadcast receipts or call OnReceipt directly on untrusted wire objects.

## Checks and remaining integration work

The eight wire groups cover all host status/reason combinations; fixed byte codes/golden frame; framing/numeric/semantic rejection; sender/recipient/session/generation/request checks; duplicate/stale/conflicting results; late timeout confirmation; host-produced replies and encode failures; bounded random parser inputs. Rejected packets leave the legitimate request pending and its result absent; conflicting packets leave the definitive result unchanged.

The fault simulator now encodes each host-produced reply before delay/loss/duplication and uses the new ingress boundary on delivery. It no longer hands Receipt objects directly to simulated remote or host-local clients. Message scheduling remains virtual; packet fragmentation, transport authentication, MTU, congestion, encryption and bandwidth are not modeled.

U3 still needs to approve action/receipt schemas, real clock estimation/deadlines, connection lifecycle, authoritative combat eligibility/results and decoder error policy. Unity transport/scene wiring and actual two/eight-process internet/latency/human gates remain separate.
