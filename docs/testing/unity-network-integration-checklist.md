# Unity network integration checklist

Proposed U3 review checklist for the foundation/session owner and networking owner. This document does not select a Unity editor version, networking package or production protocol. The [endurance evidence](network-endurance.md) validates a standalone candidate; every unchecked item below remains open.

Begin with the [integration handoff](network-integration-handoff.md) for dependency order, callable API signatures and unresolved seams. Run `pwsh -NoProfile -File tools/net-test/verify.ps1` for the combined standalone baseline; this does not check off Unity/human acceptance.

## Agree before wiring the candidate

- [ ] Choose reliable ordered whole-input delivery or the separate ability request/confirmation candidate. Review PR #15 → #19 → #20 → #21 and the endurance follow-up as a dependency stack; record the accepted contract.
- [ ] Define authoritative ability completion: distinguish request accepted, scheduler emission, cooldown/eligibility rejection, combat activation and final result. The current `Consumed` means diagnostic scheduler emission only.
- [ ] Map the current gameplay proposal to input/result semantics. The local Vanguard v2 reference proposes Basic combos, Technique variants, Signature, Defense and universal Dash; this prototype only enumerates Technique, Signature and Guard. Combo timing, facing, dash/recovery cancels, Resonance and precision-counter eligibility remain untested and need an explicit authoritative contract.
- [ ] Agree Guard/Technique/Signature lifetimes, lead limits, retry cadence, confirmation horizon and backpressure. Existing 12/30 tick lifetimes, 50 ms retries and two-second confirmation horizon are experimental.
- [ ] Define player-visible handling of uncertain outcomes and actions abandoned at reconnect/reset. In endurance testing, some unconfirmed actions had already been consumed. Never issue a fresh ID automatically to retry the same uncertain player intent.
- [ ] Choose a host-restart policy: advance session/generation before accepting traffic, or design durable authoritative recovery. Reusing the same generation after process loss can replay previously consumed actions.

## Transport and lifecycle boundary

- [ ] Bind source identity to authenticated transport metadata, with message integrity. An actor ID, connection handle or sender field supplied in a packet is insufficient. AR/AP codecs do not authenticate traffic.
- [ ] Route AR confirmations to the exact connection incarnation. Verify recipient, session/generation and complete echoed request before changing local action state. A returning actor can reuse action ID 1 and all timing fields.
- [ ] Keep immutable bytes/identity for retries. Apply exact-size/version/status/reason/timing validation; bound receive buffers and handle invalid raw request identities through an agreed generic-error/drop policy.
- [ ] Review the [event recovery candidate](encounter-event-recovery.md): cap cumulative acknowledgments at actually transmitted chunks, confirm checkpoint installation explicitly, and retry a frozen snapshot until acknowledged. Agree transport frame/fragmentation and larger-checkpoint limits before adoption.
- [ ] Define full current warning/phase/roster snapshot state captured with completed tick/generation/event watermark. Stage and commit restoration atomically, then install the event cursor. Define idempotent feedback and expired-history suppression; sample counter recovery is not combat restoration.
- [ ] Drive `ActionHost.Step` exactly once for each authoritative tick. All receive/step/roster/reset operations must be serialized by the adapter; the candidate is not thread-safe.
- [ ] Keep a monotonic millisecond retry/rate clock distinct from the generation-relative simulation tick. Define behavior during pause, backgrounding, suspension, host stalls and tick catch-up.
- [ ] On disconnect, retire the old handle and cancel/mark outstanding local intents uncertain as appropriate. On reconnect, register a fresh handle; do not let delayed old packets control the returning actor.
- [ ] Atomically bind new generation, roster, tick origin, action IDs, pending results and clock estimates. Drop/reject old packets even when the transport retains them.
- [ ] Handle the 128 distinct-connection limit within one generation explicitly. Plan a generation/session transition or revise/review the incarnation ledger design; never silently reuse a retired handle.

## Clock service

- [ ] Define and authenticate the generation's tick/time origin. The fixture supplies this contract atomically; it is not implemented in Unity.
- [ ] Supply nonce-matched four-timestamp probes or an agreed alternative, with bounded outstanding probes, fixed wire limits, replay protection, generation checks and monotonic timestamps.
- [ ] Specify freshness, drift and uncertainty policies before queueing actions. The fixture suppresses queueing when no sample is fresh within two seconds. Confirm how the game tells the player about lost synchronization.
- [ ] Measure asymmetric uplink/downlink error and clock-rate changes on actual machines. The fixture reached 81.267 ms estimation error; neither midpoint estimates nor low RTT prove an exact host tick.

## Execute in the chosen Unity project

- [ ] Run the standalone commands in [network-endurance.md](network-endurance.md) and preserve seeds/reports. Keep the existing action and receipt regression commands passing.
- [ ] Add transport-adapter tests for wire routing, authenticated metadata, malformed packets, stale generation/recipient and exact-once host stepping.
- [ ] Run host + one remote client, then host + seven remote clients. Include real process boundaries; an in-memory fixture does not establish socket behavior.
- [ ] Apply asymmetric delay/jitter, 0/3/10% packet loss, duplicates, 3–6 second outages and very late packets. Check action accounting and newly confirmed actions after recovery.
- [ ] Disconnect/reconnect while actions are pending; reset during queued traffic; restart the host process; exhaust/recover incarnation capacity. Verify the agreed policy at every boundary.
- [ ] Change client frame rate, pause/background/suspend, and vary host frame pacing. Verify monotonic clocks, tick scheduling, bounded queues and no unintended action replay.
- [ ] Run a real wall-clock soak and record managed heap, GC allocations, CPU/frame time and transport queue high-water marks. The offline suite measures collection counts and virtual duration only.
- [ ] Run human timing checks for Guard and offensive abilities under network delay. Observe explicit expiry/uncertainty feedback and ensure retries do not create repeated activation.
- [ ] Validate actual combat eligibility, authoritative activation/results and reset/checkpoint state. Diagnostic action counts do not prove combat behavior or encounter state recovery.

Record editor/package versions, topology, test duration, seeds or impairment settings, per-action accounting, failures and reviewer acceptance. Networking issue #7 stays open until the agreed Unity and human gates pass.
