# Last Bell architecture policy

Approved by Risxhb on October 20, 2026. Read [current game design](game-design.md) for the newer boss-rush gameplay decisions, which supersede conflicting prototype/port requirements. This policy records architecture; Unity bootstrap and integration still need validation.

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

Cursor aim, dodge/dash, jumping, limited sprint and movement stamina are required. No parry or iframes. The approved Vanguard Brace permits partial frontal mitigation/knockback prevention, Chimesinger uses Harmony/bells/echoes and instant healing, and Arcanist uses Ticks/seals/inscriptions/Anchor shield (#31/#36/#41). These scoped exceptions supersede the October 20 blanket no-block/no-resource/healing deferral; old Vanguard counter/Resonance/Reversal loops remain superseded. Preserve historical branches/evidence. Combo buffering, boss numbers/phases, finite-stamina Bell Sweep fairness and other explicit tuning remain contract/design work. Three starting classes, practice, per-player escalating revival and four difficulties replace old build/relic gates.

## Foundation handoff gates

U0, owned by foundation: select and record the exact Unity editor, compatible rendering/input/test packages, Windows build modules and project metadata. Validate a clean-checkout minimal scene and Windows build. Networking package selection requires a compatibility spike; diagnostic TCP does not select the stack.

U3, owned by foundation with networking and encounter review: publish a versioned typed C# interface and representative fixtures covering:

- Stable player/session IDs separate from connection IDs, attempt generations, 60 Hz tick and step ordering.
- Movement/aim bounds and zero semantics, held Basic and consumed presses; stamina, jump/landing, sprint, no-iframe dodge, collision and buffering policy.
- Authority-side action eligibility and results, retries, acknowledgments, expiry and duplicate rejection. A transport receipt or scheduled action does not mean combat activation succeeded.
- Full actor/boss state and atomic checkpoints: RNG, cooldowns, stamina, jump/dodge, stun/knockback, down/revive timers and per-player run down counts, event IDs/watermarks and gap recovery. Include approved class setup, charges, expiry, healing and mitigation state; obsolete Vanguard Resonance/guard-counter state is not required.
- Shared hazard geometry, encounter context, lock/resolve timing, damage outcomes and truthful failure feedback.
- Down/revive/disconnect assignments, host loss, practice versus fresh-run reset, three-miniboss/intermission/main-boss progression and roster lifecycle. Shop transactions/rewards/upgrades are deferred.

The initial implementation should connect movement, the agreed action kit, damage and reset in one fight. Future fields may be staged; document unsupported behavior rather than claim the entire interface exists. Unity runtime and multiplayer acceptance remain separate gates.

## Next lane priorities

| Lane | Next priority | Completion evidence |
| --- | --- | --- |
| Foundation #6 | Deliver U0 and a small U3 contract proposal, review the networking handoff in #24 and prototype adoption inputs in #25/#27 | Pinned project/build evidence and reviewed typed contract fixtures |
| Chzy18/Codex #30/#7 | Finish or hand off AP/AR; deliver the revised solo Unity arena and viewer seam; map networking gaps to U3 | Actual Unity/Windows evidence for arena; diagnostic delivery results remain separately labeled |
| Chzy18/Claude #31/#8 | Deliver model viewer and handcrafted arena content, reconcile Bellkeeper to no-iframe/cooldown/stamina design and propose four difficulty variants | Accurate model/import labels, Unity evidence, revised spec and finite-stamina escape checks |

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

The policy is approved direction. The run-loop project already pins Unity 6000.6.6f1 / URP 17.6.0; independent clean-checkout Play Mode/Windows validation, reviewed U3 types and production integration remain separate gates. This document does not establish their completion. The migration work remains on separate branches; main may still contain historical engine language. Follow this policy and current human decisions when those sources conflict.
