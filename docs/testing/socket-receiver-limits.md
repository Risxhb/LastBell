# Diagnostic socket receiver limits

This networking-only follow-up to PR #26 bounds reads and incomplete-frame retention. It does not select a Unity transport or change session/combat interfaces.

The previous idle-only timeout could be refreshed indefinitely by an introduced peer trickling bytes. The receiver now enforces both a 1,000 ms idle timeout and a 2,000 ms absolute frame assembly deadline, starting at the first header byte. It checks deadlines before consuming available data, so a late byte cannot revive an expired partial frame. Each Pump reads at most 8,192 bytes; parsing retains incomplete frames across calls. Existing 65,536-byte frame and 32-frame queue caps remain.

Run both real-socket checks from the repository root:

```powershell
pwsh -NoProfile -File tools/net-test/socket-session/framing-checks.ps1
pwsh -NoProfile -File tools/net-test/socket-session/run.ps1
```

The five focused checks use actual ephemeral IPv4 loopback sockets in one process: read budget with a 32 KiB frame spanning calls, successful deadline reset after completion, deterministic coalesced queue overflow, idle partial-header rejection despite a newly available byte, and absolute rejection despite continuous 250 ms body progress. They compile C# 7.3 with warnings as errors. The runner throws/returns nonzero if any check fails; only success writes its report, so check the process exit, not a previous report file.

The process suite additionally introduces a slow-drip non-player peer into each full eight-player lobby. It sends body bytes every 300 ms, below the idle timeout, and must be rejected as `frame_deadline` before the hello deadline. The controller verifies that rejection and all existing legitimate input/event delivery, departure and closure assertions. This makes six non-player probes and 36 child processes across the default six cases. Earlier PR #26 evidence remains historical; follow-up evidence is stored separately.

These are diagnostic limits. A slow network may legitimately require another reviewed frame budget. Read bounds alone do not guarantee scheduling latency: writes remain synchronous with a two-second timeout; multiple peers and process scheduling can delay ticks. No throughput/latency benchmark, long real-time soak, authentication, Unity, Internet or full combat-state acceptance is claimed. Existing lane boundaries and U0/U3 gates remain unchanged.
