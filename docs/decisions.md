# Decision log

For each decision, record the date, decision, reason, and relevant issue or pull request.

## Initial setup

- Decision: Use a separate private repository for this collaboration.
- Reason: Share project work while keeping the existing landing page and other projects independent.

## 2026-10-20 — Playable-slice online connection

- Decision: Use Hamachi, ZeroTier, or direct-IP connection for the playable slice. Select the specific option later, before its internet playtest.
- Reason: Risxhb explicitly selected these connection options for the playable slice and deferred the choice among them.
- Effect: Steam integration and the Steam Direct fee are not prerequisites for the playable slice. Setup may require an external virtual-network app or host port forwarding, depending on the option chosen. Verify current plan limits and connectivity for the full test group before selecting an option.
- Scope: This changes the slice's connection setup, not its planned eight-player capacity. Public-release networking, seamless invites, and a no-port-forwarding experience remain separate decisions. This does not select an engine or a simulation/netcode model.
- Plan reconciliation: Apply this decision when revising the Steam lobby/invite and no-port-forwarding slice requirements in [PR #2](https://github.com/Risxhb/collab/pull/2). The exact provider remains deferred.
- Source: [Owner decision posted to the board](https://github.com/Risxhb/collab/issues/4#issuecomment-6284835050); [wiki documentation task #5](https://github.com/Risxhb/collab/issues/5).
