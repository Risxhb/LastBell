# Standalone network fault lab

Owner: chzy18/codex, [#7](https://github.com/Risxhb/collab/issues/7). Depends on the package-free C# rules in [PR #15](https://github.com/Risxhb/collab/pull/15), commit `880d3cfea5835a6c48ad1d44f6dfc464bae68040`.

This tool can run before a Unity project exists. It schedules **virtual messages in memory** around the actual C# session model, input codec and event journal/inbox. It does not select a networking package, create a Unity project, change gameplay rules, open sockets or validate an internet connection.

## Run

From a fresh PowerShell 7 process at the repository root:

```powershell
pwsh -NoProfile -File .\tools\net-test\fault-lab\run.ps1
```

Optional `-Report C:\absolute\fault-lab.json` changes the output path. Default reports go under ignored `tools/net-test/fault-lab/results/`. No SDK, package restore or Unity install is needed. PowerShell's Roslyn compiler enforces C# 7.3 and treats warnings as errors.

The runner repeats the complete suite and requires identical seeded case results before returning success. The timestamp is metadata outside that comparison. Each report includes explicit `unityEditorExecuted: false`, `networkSocketsExecuted: false` and `productionTransportValidated: false`.

## Scenarios and observed results

The [recorded report](../../tools/net-test/fault-lab/evidence/fault-lab.json) contains all seeds, conditions and counters. Executed with PowerShell 7.6.5 / .NET 10.0.11; no Unity APIs.

| Group | Cases | Conditions | Observation |
|---|---:|---|---|
| Fault matrix | 42 | Every count 2–8 × RTT 50/100/150 ms × loss 0/3%; independent ±20 ms one-way jitter, 2% message duplication | Session invariants held and event streams caught up; 347 Guard samples were not observed as commands |
| Ordered controls | 21 | Every count 2–8 × RTT 50/100/150 ms; fixed delay, no loss/reordering, 2% duplication | All 1,050 generated Guard samples appeared in commands; events caught up |
| History-overflow probe | 1 | Eight actors, 100 ms RTT, 3% loss; one client's virtual link blacked out for four seconds | Required a checkpoint; one installed counter-state snapshot skipped 246 historical effects and resumed contiguous events |
| Disconnect-policy probe | 1 | Eight actors, 150 ms RTT, 3% loss; remove one halfway through | Late input from removed sender rejected; no removed-actor commands; remaining streams caught up |

All 65 cases met their **session/event invariants** and the seeded repeat matched. This is not a pass of reliable action delivery, gameplay responsiveness or Unity/network acceptance.

The fault-matrix Guard finding also appears at **zero configured loss**: jitter/reordering alone left 160 of 1,050 Guard samples unobserved. At 3% loss, 187 of 1,050 were unobserved. These are deterministic fixture observations, not estimates of a real game's failure rate.

### Concrete input-delivery regression

The runner also executes this minimal case against the actual `SessionRules`:

1. Client sequence 1 carries Guard but is dropped or delayed.
2. Sequence 2 carries no Guard and reaches the host.
3. The host consumes sequence 2 and reports processed acknowledgment 2.
4. Retrying sequence 1 is rejected as stale; its Guard never appears in a command.

This reproduces why **a processed movement sequence is not proof that every earlier action was delivered**. The current rules correctly reject stale input; an adapter that sends independent lossy/unordered samples cannot infer reliable press delivery from that acknowledgment.

The original spike intentionally proposed reliable input. This lab tests a deliberately lossy/unordered policy to make the integration constraint explicit, rather than silently changing that policy or declaring the C# model production-ready.

Before enabling unreliable movement in Unity, agree one of these through U3:

- Keep complete input on a reliable ordered channel for the initial adapter, then measure head-of-line delay, stale-tick rejection and guard deadlines under loss.
- Separate edge actions into an acknowledged, deduplicated action stream with independent action IDs, expiry rules and host results; movement can then use its own delivery policy.

A transport's reliability flag alone does not establish acceptable action timing. The ordered controls here have no loss/retransmission delay and are not proof that a reliable transport passes the full fault matrix. Real Unity adapter tests must cover that distinction.

The regression is an expected diagnostic finding, not hidden inside a green gameplay result. If the action protocol changes, update this fixture and its expected result through review.

## What the fixture actually does

- Uses XorShift32 with an explicit nonzero seed and stable insertion order for equal delivery times. Host and clients share a virtual millisecond timeline; delay per message is half the requested RTT plus bounded uniform jitter.
- Applies loss and duplication independently to messages. Jitter can reorder arrival; even duplicate copies can arrive separately. The blackout drops messages sent during its interval for the selected peer.
- Bootstraps join/ready/start before injecting faults. Faulty lobby establishment, packet fragmentation, MTU, encryption, socket buffering, transport retransmission and NAT are not modeled.
- Simulates 600 active ticks (10 seconds) followed by 300 draining ticks (5 seconds), at 60 Hz. Each active actor samples input each tick; remote input passes through the actual 54-byte codec. Host-local calls bypass the link.
- Uses a deliberately idealized clock estimate with **known symmetric RTT** to choose target ticks. It is not production clock synchronization and does not measure clock drift, asymmetry or prediction/correction. Reordered/stale-target inputs may be rejected, and reasons are reported.
- Publishes immutable session snapshots every three ticks and event replays every six ticks. Snapshots/event objects are delivered in memory; event/control wire codecs do not exist in this tool. Input byte serialization is the real candidate codec.
- Produces one diagnostic event every ten ticks in ordinary cases. The overflow probe produces one every tick to exceed the 128-event journal while a peer is blacked out.
- Tracks monotonic processed sequences, finite bounded movement, commands only for active actors, contiguous event effects, no duplicate feedback and final event catch-up.
- Acknowledges event application over the faulty return link. An older acknowledgment arriving late is safely rejected. Retained events resend until acknowledged; history gaps explicitly require checkpoint.
- The **checkpoint's entire test state is one counter**. Installing it sets that counter and the event watermark, suppressing skipped historical effects. It neither builds nor restores combat/roster/RNG/provider/assignment state. Foundation U3 must supply a complete authoritative snapshot seam.
- Removing a client is a coordinator policy injection, not a real socket timeout or reconnect test. No host-loss, lobby/invite, Unity scene or human readability claim follows.

Counts such as `PressSamplesNotObserved` compare generated Guard samples with output commands, not successful guards or damage outcomes. The session model contains no fight/cooldown simulation. Packet counters are virtual-message counts; they are not measured bandwidth.

## Next use after Unity bootstrap

Use these seeds and expected invariants as adapter regression cases. After the editor/packages and U3 clock/action/event contracts are agreed, run actual two/eight Unity processes, capacity rejection, disconnect/host loss and the chosen reliable/action delivery policy at 50/100/150 ms RTT plus modest loss. Add real bandwidth, frame time, corrections and event-delay measurements. Keep full separate-network runs and human fairness/readability gates distinct.
