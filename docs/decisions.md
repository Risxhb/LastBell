# Decision log

For each decision, record the date, decision, reason, and relevant issue or pull request.

## 2026-10-20 — Shared clockwork-fantasy visual style

- Decision: Adopt [the art-direction guide](../reference/LAST-BELL-CLOCKWORK-FANTASY-ART-DIRECTION.md) for characters, weapons, spells/abilities, bosses, environments, animation, VFX, icons and UI across all collaborator lanes.
- Reason: Risxhb explicitly requested that the supplied reference guide the team's visual designs consistently.
- Application: Painterly stylized forms, ceremonial clockwork motifs, warm brass/ivory against cobalt/violet, readable silhouettes and authoritative telegraphs. Follow [agent guidance](../AGENTS.md) and [visual review workflow](workflow.md#visual-design-review).
- Limits: Adopting the style does not implement assets or approve every future feature, example timing, technical proposal or budget in the reference. The confirmed 2.5D direction remains in effect.
- Tracking: [documentation PR #13](https://github.com/Risxhb/collab/pull/13).

## 2026-10-20 — 2.5D with a fixed isometric camera

- Decision: Last Bell will use modeled 3D environments/characters with a fixed isometric camera. Windblown and Hades are creative references; the game retains its original clockwork-fantasy setting, four-action kit, and raid/build focus.
- Reason: Risxhb explicitly confirmed this direction after supplying the art-direction Markdown. The owner subsequently refined the direction to 2.5D. Here that means modeled 3D visuals, fixed isometric framing and strictly planar gameplay. This supersedes the imported reference's sprite/pre-rendered-art-first recommendation and the original flat Node2D presentation target.
- Architecture: retain the planar authoritative 60 Hz simulation; map it to world XZ through one presentation adapter. Camera, models, animation and VFX do not determine damage or mechanic success.
- Unchanged requirements: Godot 4.7.2-stable, solo plus 2–8 total online, existing lane owners, and slice Hamachi/ZeroTier/direct-IP allowance. Exact renderer, camera framing and art budgets require evidence.
- Work: [foundation implementation plan](plans/2-5d-isometric-foundation.md), [art reference](../reference/LAST-BELL-CLOCKWORK-FANTASY-ART-DIRECTION.md), combat lane [#6](https://github.com/Risxhb/collab/issues/6).
- Source: direct owner instruction on October 20, 2026; recorded here without publishing private chat.

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
