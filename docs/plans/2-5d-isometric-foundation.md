# Last Bell — our 2.5D foundation implementation plan

Updated October 20, 2026. Owner: `risxhb/codex`, combat foundation and integration lane [#6](https://github.com/Risxhb/collab/issues/6). Status: planned for Unity/C#; this document does not implement the conversion. Read [the Unity migration plan](unity-migration.md) first. Its bootstrap and behavior-port gates precede A1.

## Direction and scope

**2.5D means modeled 3D characters and environments, a fixed isometric camera, and gameplay confined to one flat plane.** Port the existing combat semantics to a scene-independent C# planar simulation; map its coordinates to world XZ. Visual height, stairs in scenery, weapon arcs and particles are cosmetic. Jumping, vertical combat, camera rotation and platforming are outside this slice.

Windblown and Hades are creative references for our desired movement feel, clear silhouettes, decisive attack feedback, painterly color and isometric composition. Build an original clockwork-fantasy identity using the [art direction](../../reference/LAST-BELL-CLOCKWORK-FANTASY-ART-DIRECTION.md). Keep the four-action combat kit; a dash, new targeting scheme or changed combat rules requires a separate design decision and playtest.

The owner refined the initial 3D direction to 2.5D. This plan uses 3D visuals on the existing planar gameplay foundation; it does not assume sprite-only characters or a pre-rendered sprite pipeline.

## Starting point and ownership

The current foundation branch contains solo Vanguard/Bellkeeper play through a Node2D view, a 60 Hz simulation, shared cone/donut geometry, a 1–8 actor roster, anchor/lock timing and outcome events. The view is still solo. This is legacy Godot code, not a Unity implementation. Networking, group requirements, builds and the complete slice are not implemented. Existing engine-specific lane paths and proposals must be translated to the Unity project layout through the migration plan. Foundation PRs [#10](https://github.com/Risxhb/collab/pull/10), [#11](https://github.com/Risxhb/collab/pull/11) and [#12](https://github.com/Risxhb/collab/pull/12) remain under review; branch code is not a claim about merged main.

| Lane | Owner and scope | Dependency on our lane |
|---|---|---|
| Our foundation/integration | risxhb/codex: project/root wiring, core, combat, actors, presentation, UI, builds, rewards, rehearsal and exports | Own shared API changes, coordinate integration and regression checks |
| Sessions/networking #7 | chzy18/codex: network code, session test scenes and network diagnostics | Stable planar commands, roster, snapshots/events and scene binding |
| Encounters/validation #8 | chzy18/claude: encounter data/director, authoring preview and validator | Shared geometry, group requirements, assignments, reset and event interfaces |

These are existing assignments, not new authorization for the collaborators to start. Read current issue/PR feedback before each dependent change. This planning task does not send board messages or start other agents.

## Delivery sequence

Each gate is a focused PR with screenshots or reproducible checks as appropriate. Preserve existing behavior until a separate gameplay change is justified. Proposed filenames below are implementation targets, not files already present.

| Gate | Our deliverable | Acceptance and handoff |
|---|---|---|
| A1: playable 2.5D blockout | Unity scene with a 3D arena, fixed isometric Camera, placeholder hero/boss, camera-relative controls and screen-space Canvas HUD | All four actions, guard, restart, victory/defeat and bounds work with the same combat rules; input directions match the screen; every playable corner remains visible |
| A2: truthful mechanics | World mapping adapter, cone/donut floor meshes, tracking/lock cues, actor feet markers and outcome feedback | Warnings use authoritative geometry/ticks; donut hole is empty; boundary probes and historical failure positions align; restart clears stale visuals |
| A3: representative visual pass | One modeled Vanguard, Bellkeeper and arena treatment; stylized materials, minimal animation/VFX and HUD styling | Windup/resolve follows combat ticks; hazards survive effects/occlusion; screenshots at gameplay size and measured performance inform asset budgets |
| A4: shared combat APIs | Group requirements, independent encounter RNG, assignment policy seam, consumed press requests and versioned events | Reviewed contract supports #7/#8 without private collision or damage implementations; automated checks cover count/assignment/reset edge cases |
| A5: first complete fight/loop | Bind authored Bellkeeper, six prototype upgrades, rewards, truthful failure card and rehearsal checkpoint | Three taught mechanics and one retained Echo work; upgrades change decisions; retry under five seconds restores full authoritative and visual state |
| A6: integrated slice | Network presentation binding, Arcanist, build-system expansion, second boss integration and Windows export | Two classes, two bosses, 12 relics and six modifiers; solo and all counts 2–8; full eight-player internet mini-run and human readability gate |

After the Unity bootstrap and C# behavior port, A1–A3 use the ported solo demo and can proceed without collaborator implementations. A4 can progress alongside the presentation work, but shared changes must be agreed before #7/#8 bind. A5 depends on encounter deliverables. A6 depends on session and encounter integration plus human testers. No calendar promise is made while collaborator availability and art production capacity are unconfirmed.

## A1–A2 implementation detail

1. Add one foundation-owned mapping helper, provisionally `unity/Assets/LastBell/Presentation/ArenaMapping.cs`. Use a uniform positive scale and documented arena origin: `world = new Vector3((sim.X - origin.X) * scale, height, (sim.Y - origin.Y) * scale)`. Provide the inverse for debugging and input conversion. All radii use that same scale; all hazard directions use the same XZ basis. Simulation/network units stay unchanged.
2. Introduce `unity/Assets/LastBell/Scenes/Combat2_5D.unity` and a view/controller under `unity/Assets/LastBell/Presentation/`. Keep `game/` as a separate historical behavior reference. Keep exactly one authoritative 60 Hz clock in Unity, independent of render and animation updates; do not tie rules to frame rate.
3. Start with orthographic projection, 45-degree yaw and approximately 35-degree downward camera elevation. These are proposed blockout values. Adjust orthographic size and safe viewport margins against actual arena corners and HUD at 1280×800, then test resize/aspect ratios. Keep the camera fixed during active mechanics.
4. Project camera right/forward onto XZ, normalize that basis, convert desired screen-relative motion to the simulation plane, and clamp final magnitude. Check all cardinal and diagonal inputs on keyboard and controller. Keep automatic boss targeting and current cooldown/range rules.
5. Generate ground meshes from the same hazard definition and geometry helpers used by hit testing. Cones use the authoritative origin, direction, radius and angle. Donuts require an annulus with an actual hole, not a filled polygon across the inner safe pocket. Use enough segments for the selected camera and document the visual approximation tolerance.
6. Establish explicit floor/telegraph/feet/model/effects visibility rules. Prevent z-fighting with a small cosmetic offset; keep tall scenery outside playable bounds or fade obstructing objects. Test both inner and outer boundaries and camera-near edges. Never let a texture, shadow, animation frame or physics mesh decide mechanic membership.
7. Drive warnings from current authoritative anchor/lock state and remaining ticks. Interpolate decorative actor motion only with a clear feet/position policy; never interpolate the lethal footprint away from authoritative state. Deduplicate feedback by generation/event ID, dispatch by event type, and clear queued effects on reset/outcome.
8. Render one-, four- and eight-actor fixtures early to test visual density and identity tags. These fixtures prove presentation behavior only; they do not prove multiplayer connectivity or eight-player fairness.

## A3 art and performance work

Use simple geometry and flat colors first. Establish scale, feet pivot, facing, rig convention and a validated Unity model/animation import format with one representative character before commissioning a full asset set. Add the brass/cobalt/ivory/violet treatment, quiet arena floor, readable boss core and original bell motifs. Minimal states: idle, move, Basic, Technique, Signature, Guard, hit and knockout; boss idle, windup, resolve, recovery and defeat. Animation and VFX consume authoritative state/events and cannot delay damage or move hitboxes.

Use the Unity editor/packages pinned during bootstrap and the render pipeline validated there. Godot GL Compatibility shaders/settings are legacy references and require replacement. Profile representative eight-actor visuals, overlapping warnings and effects on agreed Windows hardware before setting geometry/texture/particle budgets or proposing a renderer change. Record hardware, resolution, frame times, draw calls, memory and overdraw. A provisional 60 fps target is 16.7 ms/frame; it is a target, not a measured result. Choose minimum hardware before treating performance as accepted.

Human review covers feel, attack weight, player identification, safe-region recognition, grayscale/reduced-size readability, muted-audio clarity and physical-controller use. Capture idle, cone/donut warnings, tracked/locked hazards, all four resolution outcomes and overlap fixtures. Reduced effects must preserve mechanic information.

## A4–A6 integration detail

- Finish shared circle/line geometry and avoid/stack/spread/guard requirement evaluation as needed by reviewed encounter specifications. Evaluate occupancy over authoritative actors; record relevant positions, counts, requirements and Echo provenance for explanations.
- Provide an independent seeded encounter-targeting RNG and define complete reset/checkpoint state. Add a shared knockout/disconnect assignment policy seam for provider-selected cancellation/reassignment. Coordinate schema changes through [the combat contract](../combat-contract.md); update versioning and all consumers together.
- Change view and simulation together for held Basic plus consumed Technique/Signature/Guard press requests. Session code owns sequencing, authentication, acknowledgments, delivery recovery and prediction/correction. Our view consumes its agreed state/events without becoming another host.
- Implement six initial upgrades with trigger eligibility, limits and event records; cap recursive procs. Integrate simultaneous personal reward picks and a visible timeout through the host-owned boundary. Expand to 12 relics and six modifiers only after the first fight/build loop works.
- Build a failure card from resolved historical state, not current positions. Distinguish avoided, guarded, immune and hit; blocked damage can still be mechanic failure. Retain the compact four-action HUD in a screen-space layer.
- Implement rehearsal with full RNG/provider/actor/boss/assignment restoration. Legacy snapshots are export-only and omit RNG state; do not present them as arbitrary-tick checkpoints. Clear presentation interpolation and event deduplication state safely on a new generation.
- Bind the networking lane's session to all roster actors and coordinated retries/rewards. Support every count 1–8 with tiers 1–4 and 5–8 and explicit odd-count policies. Scripted capacity tests are separate from human readability and internet fairness tests.
- Integrate the encounter lane's second boss and add Arcanist after the first fight and network gates. Build with the pinned Unity editor and documented Windows build configuration; verify a clean-checkout Windows build and controls. Slice connection options remain Hamachi, ZeroTier or direct IP; choose before internet testing. Host loss ends the session explicitly.

## Verification and completion evidence

For A1–A2, run the C# behavior tests ported from the legacy simulation checks plus focused mapping round-trip, cardinal input, scale/boundary and actual-scene smoke checks. Add tests that exercise these failure modes; do not duplicate the implementation in test code. Capture the new scene and inspect telegraph holes, anchors, feet alignment and all arena edges. Port the old screenshot/input scenarios into Unity scene tests; the GDScript fixtures do not validate Unity.

For A4–A6, extend checks for group counts, assignment cancellation/reassignment, input press consumption, proc limits, event identity and complete seeded reset/checkpoint behavior. Network owner supplies 2–8 client diagnostics, capacity rejection, disconnect/host-loss handling and 50/100/150 ms RTT plus packet-loss evidence; we integrate and inspect the visible outcomes. Recruit human testers for the full eight-player internet gate. No scripted run substitutes for feel/readability review.

Each PR should state completed work, checks actually run, remaining human checks, dependencies and next owner. The original slice estimate predates the Unity port, 2.5D art pipeline and eight-player scope; re-estimate after A3 and the network experiment using measured art, integration and authoring cost.

## First actionable task

Start with **U0–U1 in the Unity migration plan**, then **A1: playable 2.5D blockout** after checking foundation review feedback: create the mapping and alternate scene, preserve current combat behavior, verify controls/bounds/restart, capture the result and submit a focused PR. A2 follows before final art investment. This document update does not claim either gate has passed.
