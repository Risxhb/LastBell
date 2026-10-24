# Last Bell — Unity migration and current gameplay

Updated October 20, 2026. Unity/C# is the only production runtime. Read [current game design](../game-design.md) before porting any legacy behavior. The earlier migration plan's guard/resource/immunity parity requirements are superseded.

| Gate | Owner | Acceptance |
| --- | --- | --- |
| U0 | Foundation #6 | Pin editor, render/input/test packages, Windows modules, metadata/lock files; clean checkout opens and minimal Windows build runs |
| U1 | Foundation | Shared scene-independent 60 Hz C# rules: movement, aim, geometry, cooldowns, stamina, jump/dodge, collision, hit-stun/knockback, down/revive and run reset. Port useful legacy test scenarios with intentional rule differences |
| U2 | Arena #30 with content/viewer #31 | Isometric presentation, input/HUD, truthful floor warnings, bounded arena, readable actors and Windows playtest guide/build |
| U3 | Foundation with both consumers | Versioned typed commands/state/events/reset, units, IDs/ticks/generation, authority eligibility, atomic checkpoints, assignment and lifecycle policy agreed before production binding |
| U4 | Networking #7 / encounters #8 | Unity transport compatibility, authenticated input ownership, delivery recovery and capacity; authored content uses shared rules |

Reuse geometry, tick ordering, lock/resolve, identity, detached state and reset scenarios. Do not preserve rejected guard/parry, resource spending, counter or iframe gameplay. Do not claim old Godot/JS tests prove the new C# behavior.

Production paths remain proposed under unity/Assets/LastBell: Core, Combat, Actors, Presentation, UI, Scenes, Network, Encounters, Data and lane-specific Tests. Foundation coordinates project/packages/assemblies/shared wiring; arena/viewer owners integrate through agreed seams. Version assets and .meta files; exclude generated caches.

Use one documented combat-plane/world mapping with authoritative jump state. Record metres, tolerances, RNG and tick rounding; do not silently reinterpret legacy units. Validate imported model scale, feet pivot, orientation, rig/materials and provenance before expanding assets.

Standalone C# can establish rule evidence, not Unity compatibility. Each PR states editor/packages/runtime, executed checks and remaining build/Internet/human gates. Diagnostic TCP does not choose the production transport. Existing eight-player and connection requirements continue.
