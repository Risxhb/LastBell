# Reliable ability-request prototype

Owner: chzy18/codex, [#7](https://github.com/Risxhb/collab/issues/7). Tools-only follow-up to [fault lab PR #19](https://github.com/Risxhb/collab/pull/19), based on `b4327bda8c56a68020f210961429e98c70e59e70`.

The earlier lab demonstrated that a newer movement acknowledgment can skip a lost/reordered Guard press. This prototype gives **Technique, Signature and Guard independent action identities**, retries the same immutable request, suppresses repeat consumption and expires late work on the host. Basic remains a held movement/input sample. Production session/combat files, Unity packages and the arena are unchanged.

## Run and evidence

From a fresh PowerShell 7 process at the repository root:

```powershell
pwsh -NoProfile -File .\tools\net-test\reliable-actions\run.ps1
```

Optional `-Report C:\absolute\reliable-actions.json` changes the output. Default reports go under ignored `results/`. Uses the existing fault lab's virtual link, the prototype files and the candidate C# rules as compile dependencies. No Unity project, SDK/package restore or sockets are required.

Compilation enforces C# 7.3 with warnings-as-errors. PowerShell 7.6.5 / .NET 10.0.11 executed **9 boundary/regression groups (390 assertions) and 65 fault cases**. The runner repeats the suite and requires identical case results. See [recorded evidence](../../tools/net-test/reliable-actions/evidence/reliable-actions.json).

| Group | Cases | Generated | Consumed by diagnostic scheduler | Confirmed expired |
|---|---:|---:|---:|---:|
| Counts 2–8 × RTT 50/100/150 ms × loss 0/3/10% | 63 | 6,300 | 6,299 | 1 |
| Eight-player link blackout | 1 | 160 | 159 | 1 |
| Eight-player receipt blackout | 1 | 160 | 159 | 1 |
| Total | 65 | 6,620 | 6,617 | 3 |

All cases also include ±20 ms one-way jitter and 2% message duplication. There were **zero duplicate consumptions, zero consumptions at/after expiry, zero unknown final outcomes after the drain**, and 15,657 retries. The two 0/3% loss matrix groups each confirmed consumption of all 2,100 requests; at 10% loss one request expired instead of firing late.

This is a finite seeded fixture result, not a universal delivery guarantee or measured gameplay success rate. Its action frequency, TTL and loss conditions differ from the earlier movement-sample lab, so those aggregate loss counts are not a direct performance comparison.

Boundary groups explicitly exercise:
- Dropped first request; unchanged-ID retry and one consumption.
- Lost terminal receipt; retry retrieves the cached result without another action.
- Action 2 arriving before action 1; Guard 1 remains eligible despite the newer action result.
- Exclusive expiry boundary, last eligible tick, unchanged consumed result after its deadline, and reset of pending work.
- Sender/session/generation scope, per-peer identity, disconnect cleanup, and stale connection-handle reuse rejection.
- Bounded history, retired IDs, ID jumps, pending/rate backpressure and client result retention.
- Request codec length/type/version/ability validation.
- Bounded client retry horizon with an explicitly unknown outcome, later resolved by a valid host receipt.

## Candidate protocol

`ActionProtocol.cs` lives under `tools/net-test/reliable-actions/`, not the Unity production assets. It is an alternative for U3 review, not a selected network stack or replacement of #15's contract.

A request contains session GUID, generation, per-client action ID, ability, estimated creation tick, target tick and exclusive expiry tick. It has no client-submitted actor ID, health, damage, phase or loot. The host maps the authenticated connection handle to its stable player ID.

Full action identity includes the authenticated connection incarnation, session, generation and per-connection action ID. Emitted records retain the connection handle as well as stable player ID; do not deduplicate only by actor ID plus action ID across reconnections. Action IDs are independent of movement sequence and monotonic within the client generation. Multiple actions can be delivered out of order within the bounded window. Retrying preserves **all** fields: it cannot extend a deadline, change ability, acquire a new ID or repeat the action. Reusing an existing ID with altered fields is rejected.

Host flow:
1. The trusted coordinator registers a unique connection-incarnation handle and stable player ID. No more than eight active peers; at most 128 handles per generation. A retired handle cannot be reused until a generation reset, preventing delayed packets from becoming a new connection's actions.
2. `Receive` validates scope, ID bounds, host-approved timing, ability, pending capacity and per-peer request rate. A duplicate returns the ticket's current status without rescheduling.
3. The one authoritative caller supplies each completed tick to `Step`. Pending work is either emitted once or expired. No separate wall clock is advanced inside the host.
4. Terminal results are retained in a bounded cache. Later retries retrieve the actual earlier result even after its deadline. An evicted ID returns `HistoryUnavailable`, never a fresh execution or an invented expiry.
5. Disconnect removes that peer's queued work. A generation reset preserves the active roster but clears tickets/timing/rate state and requires a matching client reset.

Client flow:
1. Queue an immutable request. `Poll` retries at the configured interval while pending, including after the action deadline to retrieve a result that may already exist on the host.
2. The transport must authenticate the host **and route the receipt to the originating connection's client instance**. Do not broadcast receipts between client instances. `OnReceipt` verifies full request identity/scope; a delayed Pending result cannot roll a terminal result backward.
3. A terminal response stops retransmission. After the confirmation horizon, stop retries and report `UnconfirmedTimeout`: the ability may already have been consumed while receipts were lost. Never automatically issue a new action ID to replace it.
4. A retained late terminal receipt can resolve an unknown timeout. Client history is bounded; older unresolved history requires authoritative resynchronization. Reset discards the old generation's requests/results; old receipts cannot cross that boundary.

All calls are single-threaded on the coordinator thread. The prototype does not implement human account authentication, encrypted transport, crash-durable exactly-once delivery, reconnect/build restoration or arbitrary combat checkpoint restore.

## Outcomes and important limits

| Status | Meaning in this prototype |
|---|---|
| Pending | Host has queued this identity; not yet consumed |
| Consumed | Diagnostic scheduler emitted the request once |
| Expired | Host did not emit it before its exclusive deadline |
| Rejected | Request conflicts with scope/identity/timing/ability rules |
| RetryLater | Nonterminal rate/pending backpressure; retry the same identity |
| Cancelled | Connection is no longer registered; this is not proof that an earlier unconfirmed action never happened |
| HistoryUnavailable | Original result is outside retained host history; do not infer success/failure or execute again |
| UnconfirmedTimeout | Local retry horizon exhausted; actual host outcome is unknown |

**Consumed is not an activated ability.** The fixture has no combat/cooldown/resource/guard/damage rules. During production integration, complete action results only through the shared authoritative combat step and report eligibility/activation separately. Multiple distinct requests arriving in one tick are separate diagnostic emissions; U3 must define combat priority, buffering and rejection rules without collapsing their identities into one boolean.

Expiry values are **experimental test settings**, not approved game balance: Guard 12 ticks (200 ms), offensive abilities 30 ticks (500 ms), creation up to six host ticks in the future, target up to 12 ticks after creation. The host rejects an expiry beyond its configured kind-specific lifetime; it never accepts client extensions. At receipt, an action whose earliest possible next-step consumption reaches its deadline is immediately expired.

Other test settings: retry every 50 ms; stop waiting after 2,000 ms; 32 pending requests, 64 retained terminal results/client records, maximum forward ID jump 64 and 120 requests/peer/second. Backpressure is nonterminal. Repeated expired requests cannot restart execution. Timeout/retention values require actual RTT/loss and gameplay evidence before production selection.

The action request codec is a candidate fixed **56-byte**, little-endian format: ASCII `AP`, version 1, .NET Guid byte order (16 bytes), int32 generation, four int64 fields (ID/created/target/expiry) and one ability byte. Wrong length/header/version/ability is rejected before host rules. The confirmation-wire follow-up now serializes receipts through a fixed codec and checks authenticated routing metadata; see [the wire format and current evidence](ability-confirmation-wire.md). The original evidence above remains the PR #20 baseline, while the current runner additionally executes the wire-validation suite.

## Fixture scope and U3 follow-up

The 63 matrix cases use 600 active ticks plus 240 drain ticks. Each participant creates an ability every 30 ticks, cycling Technique/Signature/Guard. Both directions use the lossy, jittered, duplicate-capable virtual link; local host calls bypass it. Blackout cases suppress one peer's traffic or receipts between 1,000–1,800 virtual ms.

The fixture provides an **exact shared creation clock**, not a production clock estimate. It tests loss/retry/identity/deadline semantics without mixing in clock-sync errors. Actual clock drift/asymmetry, trust bounds and player-visible latency need separate Unity tests. A cached result is stored only in process memory; host restart requires a new session/generation or durable state, not reuse of old IDs with an empty cache.

Unity Editor executed: false. Network sockets executed: false. Combat ability activation validated: false. No bandwidth, performance, fairness, controller or internet acceptance is established.

Before adopting this alternative, foundation/session owners must agree U3 action/receipt schemas, clock estimation, authoritative deadlines, eligibility/results and generation/disconnect transitions. The simpler reliable ordered whole-input strategy remains an alternative. Then implement the selected Unity adapter, actual two/eight-client loss tests, guard/cooldown ordering, stale/reset handling and human timing feedback. This PR changes tools/docs only.
