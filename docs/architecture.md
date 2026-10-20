# Last Bell architecture policy

Approved by Risxhb on October 20, 2026. This policy supersedes conflicting historical engine plans. It records direction and adoption boundaries; Unity bootstrap and runtime integration still need implementation and validation.

## One production game

| Component | Required home and role |
| --- | --- |
| Finished game | Unity/C#: scenes, modeled visuals, fixed isometric camera, controls, UI, audio and network integration |
| Authoritative gameplay | Shared scene-independent C#: planar movement, geometry, combat, actor state, action eligibility, ticks and reset |
| Automated diagnostics | Standalone C# runners that exercise adopted shared rules, plus Unity scene/build checks |
| Browser prototype | Optional look/feel and design experiments; proposed mechanics require review before adoption |
| Godot prototype | Preserved historical behavior and test scenarios; production development moves to Unity |

Keep solo and 2–8 total online players, including the host. Use one authoritative 60 Hz combat clock. Unity adapters translate controls into commands and state/events into presentation. Transport adapters deliver commands and replicate authoritative results. Boss authoring uses shared geometry and damage rules.

Shared C# rules must avoid Unity scene, rendering, input and transport dependencies. Source assemblies or compatible libraries may be used; validate the selected C# language/runtime profile in both the standalone runner and pinned Unity editor. A .NET console test passing alone does not prove Unity compatibility.

## Prototype adoption

Keep draft PR #27 as an experimental review source. Review its class harness, Vanguard 2.0 and browser arena separately for reusable rules, test cases, presentation ideas and asset references. Extract accepted behavior into the shared C# rules, with explicit differences and acceptance checks. Avoid maintaining a second authoritative JavaScript combat implementation as production code.

Dash, aim, combo buffering, counters, Resonance/Reversal, boss damage/phase changes and Bell Sweep fairness remain explicit design/contract decisions. Publishing this policy approves the architecture, not those individual proposals. Preserve useful historical branches and evidence.

## Foundation handoff gates

U0, owned by foundation: select and record the exact Unity editor, compatible rendering/input/test packages, Windows build modules and project metadata. Validate a clean-checkout minimal scene and Windows build. Networking package selection requires a compatibility spike; diagnostic TCP does not select the stack.

U3, owned by foundation with networking and encounter review: publish a versioned typed C# interface and representative fixtures covering:

- Stable player/session IDs separate from connection IDs, attempt generations, 60 Hz tick and step ordering.
- Movement bounds, aim/zero semantics, held Basic and consumed action press IDs; agree Dash and buffering only when their gameplay is adopted.
- Authority-side action eligibility and results, retries, acknowledgments, expiry and duplicate rejection. A transport receipt or scheduled action does not mean combat activation succeeded.
- Full actor/boss state and atomic snapshots/checkpoints, RNG, cooldowns and adopted kit state, event IDs/watermarks, deduplication and gap recovery.
- Shared hazard geometry, encounter context, lock/resolve timing, damage outcomes and truthful failure feedback.
- Knockout/disconnect assignments, host loss, coordinated retry/reset, ready/reward boundaries and roster lifecycle.

The initial implementation should connect movement, the agreed action kit, damage and reset in one fight. Future fields may be staged; document unsupported behavior rather than claim the entire interface exists. Unity runtime and multiplayer acceptance remain separate gates.

## Next lane priorities

| Lane | Next priority | Completion evidence |
| --- | --- | --- |
| Foundation #6 | Deliver U0 and a small U3 contract proposal, review the networking handoff in #24 and prototype adoption inputs in #25/#27 | Pinned project/build evidence and reviewed typed contract fixtures |
| Networking #7 | Finish the currently authorized AP/AR socket experiment; map its APIs and gaps to the U3 proposal before binding | One-emission/receipt/unknown-outcome evidence, documented gaps; then a Unity adapter spike after U0/U3 |
| Encounters #8 | Reconcile Bellkeeper proposals and list geometry/state/event requirements against U3; keep experimental visuals isolated | Reviewed spec and explicit outstanding design decisions, then shared-rule integration |

Existing ownership remains. Collaborators' operators control execution and priorities; request acknowledgment in lane threads. No automatic launch or acknowledgment is implied.

## Review checklist

Before a PR is ready, its author and reviewer check:

- Production changes target Unity/C# and use adopted shared rules rather than duplicate authority.
- Prototype files are clearly experimental and production imports are deliberate and reviewed.
- Changes to packages, shared contracts or ownership have foundation coordination and consumer impact documented.
- New gameplay adoption is explicit, including changed values and any unresolved human decisions.
- Validation names its actual runtime and limitations. Standalone C#, loopback, browser and legacy tests do not claim Unity, Internet, performance or human readability acceptance.
- Relevant decisions/docs and authorized lane handoffs point to this policy; acknowledgment is tracked separately from delivery.

## Current status

The policy is approved direction. U0 editor/package selection, Unity builds, final U3 types and production integration are not completed by this document. The migration work remains on separate branches; main may still contain historical engine language. Follow this policy and current human decisions when those sources conflict.
