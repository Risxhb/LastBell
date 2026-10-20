# Last Bell — Unity migration plan

Decision date: October 20, 2026. Production engine: **Unity with C#**. Status: planning only; no Unity project, C# port or Unity build has been created yet.

## Scope and preserved decisions

Unity replaces the Godot/GDScript production path. Keep the modeled 2.5D visuals, fixed isometric camera, planar combat, original clockwork-fantasy style, compact four-action kit, solo plus 2–8 total online, and existing foundation/session/encounter owners. Hamachi, ZeroTier or direct IP remain allowed for the slice; these connection options do not select a Unity networking library.

The existing `game/` directory and [legacy combat contract](../combat-contract.md) describe the Godot reference implementation. Retain them for behavior comparisons and test scenarios. Port behavior into C# rather than renaming GDScript files or implying the current code runs in Unity. Old Godot editor/export commands and ENet-specific proposals do not apply to Unity production work.

## Migration gates

| Gate | Owner and work | Acceptance evidence |
|---|---|---|
| U0: Unity bootstrap | Foundation: create project, agree and pin exact editor, package set, render pipeline and Windows build configuration | Clean checkout opens with documented prerequisites; minimal scene builds/runs; committed editor/package metadata and lock file; generated caches excluded |
| U1: C# behavior port | Foundation: scene-independent 60 Hz combat, actors/roster, geometry, seeded state, commands, snapshots and events | Port legacy geometry/tick/cooldown/guard/reset/anchor tests; record intentional differences; no rendering or network ownership in combat rules |
| U2: playable presentation | Foundation: [foundation gates A1–A3](2-5d-isometric-foundation.md), camera/input/Canvas, accurate floor meshes, actor prefabs and representative art | Solo Vanguard/Bellkeeper scene reproduces documented rules; camera/input/bounds/outcomes/restart verified; rendered visual review and profiling |
| U3: Unity lane contracts | Foundation with session/encounter owners: publish C# command/state/event APIs and proposed folder/assembly boundaries | Consumers agree on stable IDs, tick/generation/event identity, press semantics, serialization, reset and assignment policy before binding |
| U4: session and content integration | Existing session and encounter owners, foundation integrates shared wiring/builds/UI | Revalidate selected Unity networking stack, input authentication, delivery recovery and capacity; port authored encounter content through shared geometry; continue foundation gates A4–A6 |

U0 precedes implementation against Unity APIs/packages. U1 can begin as an isolated C# rules port once its test toolchain is pinned. Networking can use a standalone Unity session spike after U0 without waiting for art, but production binding waits for U3. No change in collaborator authorization or availability is implied by this plan.

## Proposed project layout and ownership

Use a separate `unity/` project so the old behavior reference remains reproducible. These paths are proposed, not implemented:

```text
unity/
  Assets/LastBell/
    Core/             # foundation: combat state, ticks, seeded state
    Combat/           # foundation: geometry and requirements
    Actors/           # foundation: actor logic and prefabs
    Presentation/     # foundation: mapping, camera, input, VFX binding
    UI/               # foundation: HUD, rewards, failure card
    Scenes/           # foundation: shared wiring; coordinate lane test scenes
    Network/          # session owner: transport/session adapters
    Encounters/       # encounter owner: director and authoring adapters
    Data/Encounters/  # encounter owner: authored content
    Tests/            # separate foundation/session/encounter test assemblies
  Packages/
  ProjectSettings/
```

Foundation owns shared project settings, assembly boundaries and package changes; request shared edits through the combat lane. Session and encounter owners retain their assigned responsibilities within the new paths. Keep reusable art/source files discoverable through an asset manifest.

## Bootstrap decisions to record

- Select the exact Unity editor and required Windows build modules before creating the project. Record the version in project metadata and setup docs. This decision does not silently choose or install an editor version.
- Validate the render pipeline with the target stylized lighting, floor warnings and hardware; record it with the package set. Prototype material/shader features in that pipeline before committing to a production art pipeline.
- Pin input/test packages and the networking library after compatibility checks. Keep network APIs behind a session adapter; do not assume the Godot high-level networking or ENet implementation transfers.
- Version project assets, their `.meta` files, editor settings and package manifests/lock files. Exclude generated Unity caches, local IDE settings and build output; set appropriate ignore rules in U0. Confirm serialization settings and large-asset storage policy before importing production content.
- Validate one character/model/animation export into Unity and record scale, feet pivot, facing and rig conventions. Do not assume the old proposed glTF workflow works without a validated importer.

## Behavior-port requirements

Preserve authoritative tick ordering, finite/clamped movement, cooldown/resource eligibility, guard/immunity end-tick semantics, actor-center collision, roster IDs independent of peers, full retry reset, lock/tracking behavior, detached consumer state and mechanic-failure versus damage-outcome distinctions. The simulation must advance through explicit fixed ticks; presentation frames, animation events and physics callbacks cannot independently decide damage.

Use one uniform combat-plane-to-world-XZ mapping and its inverse. Preserve numeric arena units during the initial port to make comparisons understandable. Record the C# numeric types, tolerances, seeded RNG algorithm and serialization rules; Godot RNG/vector behavior does not guarantee identical C# sequences or cross-machine determinism. Use captured legacy scenarios and boundary cases to check meaningful gameplay parity, and document any intentional sequence changes.

Keep commands, snapshots and event records versioned. The old dictionaries/Godot Vector2 types are not a Unity wire format. Before U3 acceptance, define typed C# contracts for roster/configuration, input ownership/press consumption, tick/generation/event identity, event delivery/gap recovery, encounter provider context and assignment changes. Preserve accepted pending requirements such as shared group evaluation and independent encounter RNG; do not report them as already ported features.

## Validation and handoff

Port existing automated cases into the pinned C# test setup and add Unity scene tests for input, camera, hazard meshes and visual reset. Existing GDScript checks only validate the legacy reference. Record the actual Unity editor, packages, platform and checks run in each PR. Human controls/readability review, full eight-player internet tests and measured performance remain required for slice acceptance.

Old foundation PRs preserve useful historical work but do not establish Unity readiness. Publish the revised Unity contract and link this migration plan in future authorized handoffs; old issue descriptions retain historical engine details until separately updated. Do not begin by deleting the reference prototype. Re-estimate after the first behavior port, representative art scene and network spike.
