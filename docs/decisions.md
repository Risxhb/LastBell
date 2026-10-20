# Decision log

For each decision, record the date, decision, reason, and relevant issue or pull request.

## 2026-10-20 — Prototype toolchain and work ownership

- Decision: Godot 4.7.2-stable standard editor, typed GDScript, matching export templates. Pin changes through review.
- Reason: Adopt the collaborator's 2D prototype recommendation and keep all three work lanes on one version. Risxhb authorized Codex to choose the division and begin delegation.
- Work: risxhb/codex owns combat foundation, builds, UI and integration ([#6](https://github.com/Risxhb/collab/issues/6)); chzy18/codex owns sessions/networking ([#7](https://github.com/Risxhb/collab/issues/7)); chzy18/claude owns encounter authoring and validation ([#8](https://github.com/Risxhb/collab/issues/8)). Assignment does not assert collaborator availability or agent launch.
- Source: [startup handoff](https://github.com/Risxhb/collab/issues/4#issuecomment-6284864349), [official release archive](https://godotengine.org/download/archive/4.7.2-stable/).

## 2026-10-20 — Slice connection setup

- Decision: The playable slice may use Hamachi, ZeroTier or direct IP. Choose the exact option before the internet playtest; VPN setup or port forwarding is acceptable for this milestone. Steam networking is deferred.
- Reason: Risxhb explicitly selected these options for the slice. Eight total players remains required; the eventual public-release connection experience is a separate decision.
- Source: [confirmed human decision](https://github.com/Risxhb/collab/issues/4#issuecomment-6284835050).

## 2026-10-20 — Eight-player capacity

- Decision: Solo plus online co-op for 2–8 total, including the host. Eight-player internet play remains a playable-slice gate.
- Reason: The owner confirmed this requirement; slice connection setup changes do not reduce capacity.
- Source: [scope handoff and acceptance gates](https://github.com/Risxhb/collab/issues/4#issuecomment-6284864349), [plan PR #2](https://github.com/Risxhb/collab/pull/2).

## Initial setup

- Decision: Use a separate private repository for this collaboration.
- Reason: Share project work while keeping the existing landing page and other projects independent.
