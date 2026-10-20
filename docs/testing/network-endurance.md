# Network endurance and recovery lab

Tools-only follow-up to [ability confirmations](ability-confirmation-wire.md), stacked on PR #21. This suite runs independent client clocks and repeatedly interrupts a long-running action protocol. It requires no Unity project, package, SDK or editor installation. The C# sources compile as language version 7.3 with warnings treated as errors through PowerShell `Add-Type`.

## Reproduce

From the repository root, in a fresh PowerShell process:

```powershell
pwsh -NoProfile -File tools/net-test/endurance/run.ps1
```

Default: 21 ten-minute matrix cases and two two-hour cases, each run twice with exactly the same seeds. Each pass simulates 7.5 virtual hours at 60 host ticks per second. A twelve-second drain follows each active run. This is accelerated virtual time, not a wall-clock endurance test. The committed [JSON evidence](../../tools/net-test/endurance/evidence/endurance.json) contains run metadata, per-case results, totals, maxima, failures and repeat comparisons.

For a short development check:

```powershell
pwsh -NoProfile -File tools/net-test/endurance/run.ps1 -MatrixSeconds 120 -SkipTwoHour
```

`-Report` chooses the output JSON path; the default `results/` folder is ignored. `-MatrixSeconds` accepts 120 through 86400; the two-hour cases stay fixed unless `-SkipTwoHour` is supplied. The runner exits nonzero on any recovery/invariant failure or repeat mismatch. It also runs ten targeted recovery groups twice. The earlier request/receipt regressions remain independently runnable with `tools/net-test/reliable-actions/run.ps1`.

## Matrix and disruptions

Every participant count from 2 through 8 runs each profile below. All participants use the network fixture, including the first actor; this suite does not simulate a zero-latency host-local shortcut.

| Profile | Uplink / downlink | Independent one-way jitter | Random loss | Duplicate probability |
| --- | --- | --- | --- | --- |
| Low latency | 25 / 25 ms | ±10 ms | 0% | 3% |
| Fast uplink | 25 / 125 ms | ±25 ms | 3% | 3% |
| Slow uplink | 125 / 25 ms | ±25 ms | 10% | 3% |

The two-hour cases use 2 participants / fast uplink / seed 77021 and 8 participants / slow uplink / seed 77081. Matrix seeds are `17001 + participants * 101 + profileIndex` (indices 0, 1, 2).

Each client has an alternating positive/negative clock offset of at least 120 seconds and a rate error of +500 or −500 parts per million relative to the host. Its retry clock also has an independent epoch and rate. Every client attempts an action twice per virtual second, rotating Technique, Signature and Guard. Queueing requires a fresh clock sample. Requests and confirmations use the AP/AR byte formats from #20/#21.

The last participant experiences the following repeated 120-second schedule; these windows also affect clock probes:

- 20–23 seconds: uplink outage.
- 45–48 seconds: downlink outage.
- 70–76 seconds: bidirectional outage.
- Packets sent during 29–30, 59–60 and 119–120 seconds receive an extra eight-second delay. They can arrive after the endpoint reconnects or the generation changes.

That participant disconnects every 30 seconds and returns after one second with a fresh connection handle and client instance. The stable actor ID remains the same, while action IDs restart. Every five minutes the host and retained clients atomically advance generation, reset the host tick, clear action/clock state and obtain fresh clock samples. Old queued packets are deliberately retained by the fixture so that rejection is exercised instead of being hidden by queue clearing.

Every outage end, reconnect and generation reset starts a recovery window. A newly created action for the affected participant must be consumed and receive a matching confirmation within three seconds. A confirmation for an older action cannot satisfy recovery. After the final drain there must be no pending client actions, watched actions or queued fixture packets.

## Clock candidate

`ClockEstimator.cs` is a diagnostic candidate, not an adopted Unity clock service. Four timestamps come from a nonce-matched fixture probe: client send, host receive, host send, client receive. The fixture supplies authenticated connection/generation context; it does not implement clock packet serialization or transport authentication. There are at most eight outstanding probes per client. Probe replies are single-use, obsolete incarnations/generations are rejected, and samples older than two seconds cannot be used to queue actions.

The estimator retains eight samples, selects the lowest corrected RTT among fresh samples, and computes the midpoint offset. Equal-RTT samples favor the newest observation. Its diagnostic uncertainty is half the corrected RTT plus 1000 ppm of sample age plus one millisecond. These assumptions require nonnegative path delays, comparable monotonic millisecond rates and bounded relative drift; this value is not a cryptographic guarantee or a calibrated confidence interval. Asymmetric delays produce persistent offset error that extra samples alone cannot remove.

The estimated host time is translated into a generation-relative tick. The host still validates action timing and owns consumption. The fixture compares estimates with its private true clock only for auditing; client queueing never reads the host's current tick. The generation start time is supplied by the fixture's atomic lifecycle contract. Unity must define and authenticate that contract before adopting this approach.

## Executed result

The evidence was produced with PowerShell 7.6.5 / .NET 10.0.11 on 2026-10-20. Both full passes and the targeted suite matched exactly. All 23 scenarios and all 10 recovery groups / 21,688 assertions passed. The existing 9 action groups / 390 assertions, 8 receipt groups / 2,467 assertions and 65 short fault cases also passed independently after this addition.

Totals below describe one full pass, not both repeats:

| Measurement | Result |
| --- | ---: |
| Virtual active time | 7.5 hours |
| Generated actions | 264,724 |
| Diagnostic consumptions | 261,118 |
| Confirmed consumed outcomes | 260,424 |
| Confirmed expired outcomes | 828 |
| Unconfirmed outcomes | 1,911 |
| Actions abandoned at connection/generation boundaries | 1,561 |
| Fresh actions rejected / cancelled | 0 / 0 |
| Reconnects / generation resets | 877 / 67 |
| Successful recovery windows | 1,887 / 1,887 |
| Worst recovery time | 2,288 ms |
| Duplicate / late / inactive consumptions | 0 / 0 / 0 |
| Conflicting authoritative outcomes | 0 |
| Packets sent / delivered | 3,437,565 / 3,190,757 |
| Packets dropped / extra duplicate deliveries scheduled | 339,461 / 92,653 |
| Worst measured clock error | 81.267 ms |

The final outcome partition sums to generated actions: confirmed consumed + expired + unconfirmed + abandoned. Host consumption is a separate metric: 694 consumed actions lack a retained client confirmation, comprising 534 unconfirmed and 160 abandoned actions. An unconfirmed or abandoned action must not be presented as definitely not executed. The three-second recovery budget applies to fresh post-disruption actions; it does not promise recovery of every old action's result.

| Resident state per participant unless noted | Observed maximum | Enforced/probed bound |
| --- | ---: | ---: |
| Client pending actions | 5 | 32 |
| Client completed results | 64 | 64 |
| Host action tickets | 65 | 32 pending + 64 terminal |
| Clock samples / outstanding probes | 8 / 8 | 8 / 8 |
| Fixture queued packets, whole case | 66 | participants × 256 diagnostic budget |
| Pending actions / packets after drain | 0 / 0 | 0 / 0 |

These are collection-size measurements. They do not establish managed heap stability, Unity allocation performance, CPU budgets or a bound on an arbitrary real transport queue. The test oracle intentionally retains a full audit ledger for the run and is not bounded protocol state.

## Targeted checks and integration findings

The ten groups cover clock offset/drift/staleness and malformed samples; empty-estimator generation rollback; host cache eviction and replay suppression; a low pending ID surviving eviction of higher terminal IDs; pending saturation, rate limiting and monotonic retry clocks; the 128-handle generation cap; reconnect identity reuse; old in-flight requests/receipts across reset; eight independent connections sharing action ID 1; and host process loss.

The host-loss check deliberately reproduces a limit of the existing candidate: a new host process using the same session and generation has no record of earlier consumptions and can consume a replayed action again. Recreating the process with a new generation rejects that replay. This suite does not add persistence or migration. A production restart must advance generation or session identity, or use a separately designed durable action/result recovery protocol.

Retired connection handles remain recorded within a generation and registration stops after 128 distinct handles. A new generation restores capacity. The long fixtures reset every five minutes, so their hundreds of reconnects do not prove unlimited same-generation churn. The targeted cap check verifies fail-closed exhaustion and recovery explicitly.

No existing request/receipt protocol code was changed: the suite found no violation of its current consumption and state-bound invariants. Production adoption, chosen transport, clock service, timeout/TTL policy, combat eligibility/results and Unity lifecycle binding remain pending foundation review. The [Unity integration checklist](unity-network-integration-checklist.md) lists the remaining concrete gates.
