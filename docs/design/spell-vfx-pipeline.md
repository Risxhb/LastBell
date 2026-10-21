# Spell effects pipeline: guidelines

**Status:** Proposed guidelines, October 20, 2026 (America/Chicago). Not an implementation record.
**Applies to:** Every player ability, boss attack, status effect and hit reaction in the Unity URP project.
**Owners:** Risxhb and Chzy18 share acceptance of visual direction. Code review is separate from visual approval.
**Related:** [Art direction bible](https://github.com/Risxhb/collab/blob/docs/2-5d-isometric-direction/reference/LAST-BELL-CLOCKWORK-FANTASY-ART-DIRECTION.md) §11–12 (readability, VFX language), [game design](https://github.com/Risxhb/collab/blob/docs/boss-rush-decisions/docs/game-design.md) (current rules).

Where this document and the art bible differ, the **current game design wins**: there is no block or parry against boss abilities, no invincibility frames, 2–8 players online, mouse-cursor aiming, and cooldowns with no attack-resource costs. Art bible entries about Guard or immunity outcomes are historical.

---

## 1. Goals

1. **Readable first, pretty second.** A player in a full 8-player fight must always be able to tell what will hit them, when, and what just happened.
2. **The simulation owns the facts.** Effects show events the simulation has already decided. They never decide hits, timing or shapes.
3. **One toolkit, many spells.** Build a small set of reusable shaders, meshes, textures and VFX templates, then make each spell by recombining and recolouring them. Don't hand-build every spell from scratch.
4. **Cheap enough for eight players.** Effects have budgets, pooling, distance culling and a "not my effect" reduced mode from day one.

## 2. Reference study: what Windblown does

Windblown (Motion Twin, 2024) is our style and tech reference. It is a stylized 3D co-op action game on Unity 6 URP, so it is the closest match to our stack. Using [REA](https://github.com/morluto/rea), we read the game's shipped IL2CPP metadata (type, method and string names) and the names inside its asset bundles. No code was decompiled and no assets were extracted or reused. The findings below are names observed in the shipped files; what each one does is our interpretation.

| Finding | Observed evidence | What it means for us |
|---|---|---|
| **Simulation and effects are separate** | Deterministic Photon Quantum simulation (`quantum.code.dll`) raises events such as `EventAlertBeforeAttack`, `EventStartAction`, `EventCastHurtFrame`, `EventActionHit`, `EventCharacterReceiveDamage`, `EventCharacterReceiveHeal`, `EventProjectileHitStopUpdated`. View classes (`CharacterView`, `ProjectileView`, `CharacterAffectView`, `ImpactManager`) listen and play effects. | Matches our tick-based simulation. Effects subscribe to simulation events and never run their own timers for gameplay moments. |
| **VFX Graph everywhere** | 1,353 VFX Graph assets, 9,085 particle outputs. About 97% of outputs are **unlit**. The most common output is a **Shader Graph mesh** (3,695), then Shader Graph quads (2,985); trails use particle strips; ground marks use forward decals. | Stylized spells are unlit and colour-driven. Big shapes (slashes, shockwaves, domes) are **meshes**, not stacks of billboards. |
| **One standard parameter vocabulary** | `VFXProperties`: `CasterPosition`, `CastPosition`, `AimPosition`, `TargetDistance`, `HurtShapeCenter/Size/Scale`, `Angle`, `AngleMask`, `AlertDuration`, `CastDuration`, `ChargeDuration`, `ChargeRate`, `MainGradient`, `SecondaryGradient`, `ColorLine`, `ColorGlow`, `Intensity`, `Fade`, `Dissolve`, `NoFlashes`, `StunTime`. `VFXEvents`: `OnCharged`, `OnReleased`, `OnPerfectReleased`, `OnSlash`, `OnShieldHit`, `OnCharacterReceiveStandardHeal`, `OnDone`… | Every effect exposes the same named inputs, so code can drive any effect without per-asset scripting. We adopt the same idea (§5). |
| **Generic, data-driven telegraphs** | Shared preview effects: `SphericalAoePreview`, `60DegreesConeAoePreview`, `RectangularAoePreview`, `ShockwavePreview`, `RushPreview`, `TrianglePreview`, `PerfectTimingPreview`, each a single mesh system set from hurt-shape parameters. `SkillPreview` shows the player's own aim with `DirectionalRange`, `CircleRadius` and a "validation" alpha boost on confirm. | A few telegraph templates take their exact shape from the simulation. Boss-specific dressing goes on top. |
| **Modular layer recipes** | `BaseModularSlash` = `Slash_Back` + `SlashCore` + `SlashHighlight` + `Refraction` + `Sparks`. `Impact_Basic` = `GlowADD` + `Glow` + `Radial` + `Spikes` + `DirectionalSparks` + `Impact Texture`. `BurnAuraAOEAtk` and `BleedAuraAOEAtk` share an identical 10-layer structure with different colours. | Templates first, variants second. Most "new" spells are a template with new gradients and textures. |
| **One uber shader for effects** | `FX_SH_Graph_ColorDissolve` plus about 10 variants (`_SoftDissolve`, `_Distort`, `_Fresnel`, `_AddEmissive`, `_RotateUV`, `_NormalmapSpecular`). Properties: `_MainTexture`, `_ColorLow`, `_ColorHigh`, `_ColorIn`, `_Prelerp`, `_Dissolve`, `_DissolveRemap`, `_DissolveColorRemap`, `_FlipbookTile`, `_Frame`, `_AddVertexAlpha`, `_AddVertexColor`, `_AddVertexAlphaToDissolve`, `_ClippingAlpha`, flowmap and fresnel-alpha inputs. | Grayscale textures are coloured by a gradient remap and faded out with an erosion dissolve driven by particle data. This is the core look (§6). |
| **Strict asset naming** | `FX_TX_` textures (631), `FX_MS_` effect meshes (393), `FX_SH_` shaders (26), `FX_PF_` prefabs (98), plus shared libraries `FX_MS_BasicMeshes_01`, `FX_MS_SlashesCombo_01`, `FX_MS_ExtraShapes_02`. Common textures: `FX_TX_SoftGlow_01`, `FX_TX_RadialImpact_01`, `FX_TX_RadialBurst_01`, `FX_TX_DirectionalSpikes_Blur`, `FX_TX_SlashDirectional_02_Highlight`, `FX_TX_SmokeMovement_01_Dissolve`. | We adopt a matching convention (§9). |
| **Impacts come from a lookup table** | `ImpactManager.PlayHitVfxs` picks an `ImpactEffect` by `hitTypes`, `damageKinds`, `impactTypes` (`CQC`, `Body`, `AOE_GroundOrStomp`, `AOE_Air`), `impactTargets` and `impactSources` (`PlayerFocused`, `PlayerNotFocused`, `NeutralEntity`), with `matterBasedOverrides` (different effects per surface) and `weaponSpecifics`. | Hit effects are chosen by data, not wired into each attack (§7.3). |
| **Co-op "focused player" rule** | `FocusedPlayer`, `OnlyFocused`, `NonFocusedPlayersOnly`, `PlayerExclusiveVFX`, `ManagedVisualEffectInstance.SetFocused`, `_otherPlayerMaterial`, `isOtherPlayer`. | Your own effects play at full strength. Other players' effects are reduced. Essential for 8 players (§8). |
| **Feel and accessibility hooks** | Simulation-side `hitStopFrames` / `IsHitStopper`; Cinemachine Impulse for camera shake; `SquashAndStretch`; hit flash and fresnel fade on characters; options `NoFlashes`, `NoScreenshake`, `EnvironmentEffectsQuality`; `HideFlashesCondition`. | Hit-stop is a simulation decision. Flash and shake have player-facing off switches (§10). |
| **Runtime management** | `VisualEffectPool` (`AddInstanceIfNecessary`, `keepInstanceTime`), `VFXPrewarmer` (plays every effect once during loading), `VFXTrackingManager` (counts active instances), `FrustumCulledVFX`, "Distance culled VFXs" logging, `VFXInstanceGroup` (GPU-instanced groups). | Pool, prewarm, cull and count from the start (§11). |

The raw evidence (REA evidence records, name lists) is kept in local research and is not committed. Only the conclusions belong here.

## 3. Architecture

```
Simulation (60 Hz, authoritative)            Presentation (Unity, every frame)
---------------------------------            ---------------------------------
Ability state machine ──► SpellEvent ──────► SpellCueRouter
Boss timeline         ──► TelegraphEvent ──► TelegraphPresenter  (exact geometry)
Hit resolution        ──► HitEvent ───────► ImpactTable lookup ─► pooled VFX + SFX + hit-stop/shake
Status system         ──► StatusEvent ────► StatusPresenter     (attached loops)
```

Rules:

1. **Simulation events are the only trigger** for gameplay effects: cast start, windup, release, active hurt frame, projectile spawn/stop, hit, heal, status add/remove, down, revive. Each event carries tick, actor, ability ID, positions, aim, shape and outcome.
2. **No gameplay in effects.** An effect never decides damage or collision, and never runs a timer for a gameplay moment. Its length can differ from the simulation as long as gameplay beats line up with simulation ticks.
3. **Late or replayed events are safe.** Online, an event can arrive several ticks late. The presenter fast-forwards the effect by the elapsed ticks (`VisualEffect.Simulate` or a `StartTime` property) instead of playing from zero, and ignores duplicates by event ID.
4. **Telegraph geometry is copied, not re-authored.** Telegraph meshes are built from the same shape data the simulation tests against: cone origin, direction, half-angle and radius; donut inner and outer radii; rectangle and circle. Art bible §11.6 still applies.
5. **Cosmetic-only events are allowed** (footstep dust, idle glow, ambient sparks) but must be marked cosmetic and run under the cosmetic budget.

## 4. Tools and packages

| Need | Choice | Notes |
|---|---|---|
| Particles | **VFX Graph** (`com.unity.visualeffectgraph`, matching URP 17.6) | Not installed yet; add it to the project. Use Shader Graph outputs. Keep the legacy Particle System only for UI or tiny one-offs. |
| Shaders | **Shader Graph**, unlit, transparent or alpha-clip | Uber shader in §6. No lit particle shaders without visual sign-off. |
| Big shapes | **Meshes** from Blender (slash arcs, rings, domes, cylinders, cones, beams) | Unwrap each mesh so U runs along the motion and V across the width, so scrolling and dissolve work. |
| Ground marks | URP Decal Projector or VFX Graph forward decal | Telegraphs use generated meshes (exact geometry), not decals. |
| Trails | VFX Graph particle strips | For dashes, projectiles and weapon swings. |
| Camera shake | Cinemachine Impulse | Only after the camera rig uses Cinemachine; until then, one small `CameraShake` service with the same API. |
| Tuning | `SpellCue` ScriptableObjects (§5) | Designers tune in the inspector, not in code. |

## 5. The cue contract

### 5.1 Standard property names

Every VFX Graph that a spell can spawn exposes the subset it needs from this list, with **exactly these names**. The router sets whatever exists and silently skips the rest, so one router drives every effect.

| Property | Type | Meaning |
|---|---|---|
| `CasterPosition` | Vector3 | Caster's feet at the event tick |
| `CastPosition` | Vector3 | Origin of the spell (hand, weapon tip, target point) |
| `AimDirection` | Vector3 | Normalized aim on the ground plane |
| `AimPosition` | Vector3 | Cursor point used by the simulation |
| `TargetPosition` | Vector3 | Locked target or impact point |
| `ShapeCenter` / `ShapeSize` / `ShapeAngle` / `ShapeInnerRadius` | Vector3 / Vector3 / float / float | Exact hurt-shape data |
| `Range` | float | Ability range in world units |
| `WarnDuration` | float | Seconds from telegraph start to resolve |
| `CastDuration` / `ChargeDuration` / `ChargeRatio` | float | Windup and charge state |
| `StartTime` | float | Seconds already elapsed, for late events |
| `MainGradient` / `SecondaryGradient` | Gradient | Spell colours (see §6.2) |
| `ColorLine` / `ColorGlow` | Color (HDR) | Outline and glow colours |
| `Intensity` | float | 0–1 overall strength (charge level, crit) |
| `Focus` | float | 1 = local player's effect, 0 = someone else's (§8) |
| `NoFlashes` | bool | Accessibility toggle (§10) |
| `Seed` | int | Deterministic variation per event |

Standard events (VFX Graph event names): `OnCast`, `OnRelease`, `OnHit`, `OnCharged`, `OnStop`, `OnDone`.

### 5.2 `SpellCue` asset

```csharp
[CreateAssetMenu(menuName = "Last Bell/VFX/Spell Cue")]
public sealed class SpellCue : ScriptableObject
{
    public string abilityId;                 // matches the simulation ability ID
    public CueStage[] stages;                // Windup, Release, Active, Projectile, Impact, End
}

[System.Serializable]
public struct CueStage
{
    public SpellEventType trigger;           // simulation event that plays this stage
    public VisualEffectAsset vfx;            // pooled
    public AttachMode attach;                // World, Caster, CasterWeapon, Target, Projectile
    public bool alignToAim;
    public GradientPreset colors;            // class or variant palette
    public float hitStopTicks;               // display-only; the simulation sets real hit-stop
    public ImpulsePreset shake;              // null = none
    public EventReference sfx;               // audio fires on the same event
    public CueScope scope;                   // Everyone, FocusedOnly, OthersOnly
}
```

The `SpellCueRouter` maps `(abilityId, eventType)` to a stage, takes an instance from the pool, sets §5.1 properties from the event, applies the focus rule and plays it. Audio and effects fire from the **same** event so they never drift apart.

## 6. The look: the `SG_FX_ColorDissolve` uber shader

### 6.1 How it works

All spell surfaces (quads, meshes, strips) share one Unlit Shader Graph family with keyword variants. Textures are **grayscale shapes and masks**; colour comes from the shader.

1. Sample `_MainTex` (R = shape, G = secondary detail, B = erosion noise). Use flipbook UVs when `_FlipbookTile` > 1, and optional UV scroll and flowmap distortion.
2. **Gradient remap:** `color = lerp(_ColorLow, _ColorHigh, smoothstep(_RemapMin, _RemapMax, shape))`, or sample a gradient ramp texture for 3+ stops. Bright cores and dark edges come from one texture.
3. **Erosion dissolve:** `alpha = saturate((noise - dissolve) / _DissolveSoftness)`, where `dissolve` comes from particle **age** (or custom data / vertex alpha). Effects eat away from the edges instead of fading uniformly, which reads as painted.
4. **Dissolve edge colour:** a thin band at the dissolve front tinted `_ColorEdge` (`_DissolveColorRemap`) gives the hot rim on slashes and bursts.
5. **Vertex alpha and colour** from VFX Graph multiply in (`_AddVertexAlpha`, `_AddVertexColor`), so the graph controls fades.
6. Optional variants: `_FRESNEL` (domes, shields), `_DISTORT` (writes to a refraction pass), `_SOFT_DEPTH` (fades where it meets the floor), `_ROTATE_UV`.

Blending: **additive** for glows, **alpha** for smoke and shapes, **premultiplied** for anything that needs both. Never use lit blending for spells.

### 6.2 Colour rules

- Each class owns a **gradient preset** (low → high → edge). Spells use their class preset unless a variant overrides it.
- **Dark accents on purpose:** include a few dark sparks or a dark back-layer in bright effects so they read on bright floors (Windblown's `Sparks Dark`, `SquareDark` layers).
- HDR: keep the glow core between 1.5 and 4 so bloom catches it without washing out telegraphs. The bloom threshold is set per arena by the look settings.
- Hostile colours (Ember Coral and amber) are **reserved for boss danger**. Player spells never use them as a dominant colour.

| Owner | Low | High | Edge / glow | Shape language |
|---|---|---|---|---|
| Vanguard | Midnight Cobalt `#17263F` | Porcelain Ivory `#F4E9D1` | Resonant Gold `#F6C968` | Wedges, arcs, clock hands, heavy and short |
| Arcanist | Twilight Violet `#655491` | Sky Teal `#55C9CE` | Ivory-teal | Straight lines, lenses, segmented rings, glyphs |
| Healer | Clockwork Brass `#C38D46` | Porcelain Ivory | Resonant Gold, a little teal | Soft concentric pulses, chimes, rising motes |
| Boss danger | Deep Ink `#101825` | Ember Coral `#EB685D` | Amber/white at resolve | Filled zones with hard borders, hatching, radial rays |

Values come from art bible §5. Check every preset against colour-vision simulations and a grayscale screenshot before approval.

## 7. Building a spell

### 7.1 Stages

Every ability is authored as up to six stages, each mapped to a simulation event:

| Stage | Trigger | Purpose | Typical length |
|---|---|---|---|
| **Windup / anticipation** | Cast start | Tells everyone what's coming | Matches simulation startup ticks |
| **Release** | Release tick | The decisive moment: flash, snap, sound | 2–6 frames |
| **Active** | Active hurt frames | The swing, beam or area while it can hit | Matches simulation active ticks exactly |
| **Projectile** | Projectile spawn / stop | Body + trail, then landing | Follows the projectile |
| **Impact** | Hit event (via the impact table) | Confirms contact and hit type | 0.15–0.4 s, then a little linger |
| **End / linger** | Stop / done | Smoke, embers, ground scorch | Fades quickly; never covers the next telegraph |

Every effect is front-loaded: big and bright on the first frames, then a fast falloff and a short tail. Long glowing tails cost readability.

### 7.2 Layer recipes (templates)

Build these templates once, then make each spell as a variant.

| Template | Layers (back to front) |
|---|---|
| `VFX_T_Slash` | back slash (dark, wide) → core slash (gradient) → highlight (thin, bright) → optional refraction → sparks along the arc |
| `VFX_T_Impact` | soft glow (additive) → radial burst → spikes / star → directional sparks (along the hit direction) → optional dark sparks → small smoke puff |
| `VFX_T_Burst` (AoE release) | ground ring / wave cylinder → burst sphere → spikes → lingering sparks → ground scorch decal |
| `VFX_T_Projectile` | core mesh → glow quad → ribbon strip trail → small sparks; plus a `_Land` impact variant |
| `VFX_T_Beam` | beam mesh (scrolling UVs) → start flare → end impact → sparks along the beam |
| `VFX_T_Aura` (loop) | ground disc → rising motes → rim fresnel on the character |
| `VFX_T_Heal` | rising soft pulse rings → glow burst → motes; never a filled ground disc |
| `VFX_T_Shield` | fresnel dome mesh → hit ripple (`OnHit`) → break burst |
| `VFX_T_Dash` | ground dust → body afterimage strip → end puff |
| `VFX_T_Telegraph_*` | Circle, Donut, Cone, Rectangle, Line, Rush, Triangle (§7.4) |

### 7.3 Impacts come from the impact table

Attacks don't own their hit effect. On each hit event, `ImpactTable.Resolve(hit)` picks an entry by:

- hit kind: melee, projectile, area-ground, area-air, beam, damage-over-time tick
- damage kind / element
- target: boss body, boss weak point, player, environment
- source: local player, other player, boss
- surface: brass, stone, flesh, energy (switches spark colour and smoke)
- optional per-ability override

This gives consistent hits for free, and one tuning pass improves every attack. Heals use the same path (`HealTable`) with heal kinds: direct, regen tick, area, overheal.

### 7.4 Telegraphs (boss danger)

- **Exact geometry, generated at runtime** from the simulation's shape data. One mesh template per shape. Inner and outer borders are drawn on the true boundary. The boundary line counts as dangerous, as art bible §11 requires.
- **Three layers**, as in art bible §11.2: geometry (fill + hard border, must read alone), identity (hatching, radial rays, class of attack), timing (a fill or closing ring driven by `(resolveTick - tick) / 60`, never by an animation clip).
- **Lock behaviour is visible.** A telegraph that stops tracking snaps its border bright at the lock tick.
- Resolve: one short flash on the resolve tick, then clear within 0.2 s so the next warning is visible.
- **Telegraphs render above player spells.** Use a dedicated render layer and sorting priority. Player effects near the floor are dimmed while a telegraph is active underneath them.
- Friendly areas (Sanctuary, healing zones) use a dotted or broken soft rim, almost no fill, slow gentle pulse and an ally icon, never the hostile fill-and-hard-border style (art bible §11.5).
- The player's own aim preview (`SkillPreview` equivalent) is local-only, thin, low-alpha, and gets a short alpha boost when the cast is confirmed.

## 8. Co-op rules (2–8 players)

| Rule | Detail |
|---|---|
| **Focus** | Effects from the local player play at full strength (`Focus = 1`). Other players' effects run at 35–60% alpha, roughly half the particles, no screen flash, no shake and no distortion (`Focus = 0`). Scope `FocusedOnly` effects (aim previews, cooldown-ready pings) never play for others. |
| **Identity** | Each player gets a persistent outline colour or ground tag. Class gradients stay fixed so a class is recognizable whoever plays it. |
| **Overlap** | When more than three player areas overlap, dim the older ones. Never stack eight copies of a bright ground effect at full alpha. |
| **Boss priority** | Boss telegraphs and the boss body are never covered by player effects. If they must compete, player effects lose. |
| **Online timing** | Others' effects can arrive late. Fast-forward (§3), and skip windup stages that are already over when the event arrives. |

## 9. Naming and folders

```
Assets/LastBell/VFX/
  Shaders/      SG_FX_ColorDissolve.shadergraph, SG_FX_Telegraph.shadergraph, SubGraphs/
  Textures/     FX_TX_<Shape>_<Variant>_<NN>   e.g. FX_TX_SoftGlow_01, FX_TX_RadialBurst_01, FX_TX_SlashDirectional_01
  Meshes/       FX_MS_<Library>_<NN>           e.g. FX_MS_Slashes_01 (arcs), FX_MS_Shapes_01 (ring, dome, cylinder, cone)
  Templates/    VFX_T_<Type>                   reusable VFX Graph templates (§7.2)
  Spells/<Class>/VFX_<Class>_<Ability>_<Stage> e.g. VFX_Vanguard_FinalHour_Release
  Boss/<Boss>/VFX_<Boss>_<Attack>_<Stage>      e.g. VFX_Bellkeeper_BellSweep_Telegraph
  Impacts/      VFX_Impact_<Kind>_<Surface>
  Status/       VFX_Status_<Effect>_<Loop|Start|End>
  Cues/         CUE_<Class>_<Ability>.asset    SpellCue assets
  Gradients/    GRAD_<Owner>_<Variant>.asset
```

Textures: grayscale, packed into channels where useful (R shape, G detail, B erosion noise), power of two, 256–512 for most, 1024 only for big ground shapes. Flipbooks use 4×4 or 8×8 grids. Keep a shared **library** of about 30 core textures before making spell-specific ones.

## 10. Feel and accessibility

- **Hit-stop:** set by the simulation (ability data `hitStopTicks`), applied to both actors' animation and their effects. Presentation never adds its own freeze. Start at 2–4 ticks for basics and 6–8 for signatures, then tune.
- **Shake:** small, directional, short. Only for the local player's big hits, boss resolves and the player getting hit. Off when `NoScreenshake` is enabled.
- **Flashes:** full-screen or large white flashes are rare, short (≤3 frames) and disabled by `NoFlashes`. Effects read the `NoFlashes` property and swap flashes for a colour pulse.
- **Effect intensity setting:** Full / Reduced / Minimal. Reduced halves particle counts and linger. Minimal keeps telegraph geometry, impact confirmation and heal confirmation only. **No mechanic may depend on an effect that Minimal removes.**
- **Never colour alone:** every outcome (hit, heal, status, down, revive) also has shape, icon or motion.
- **Squash and stretch** on the hit actor and a short hit-flash or fresnel pulse (MaterialPropertyBlock on the CustomLit shader) confirm hits without adding particles.

## 11. Performance budgets (starting targets, tune with profiling)

| Item | Budget |
|---|---|
| Active VFX instances, whole screen | ≤ 120 typical, hard cap 200 (oldest cosmetic effects culled first) |
| Particles per player spell | ≤ 300 at Full; ≤ 150 at Reduced |
| Particles per boss attack | ≤ 1,500 including telegraph |
| Overdraw | Spot-check with the URP debug overdraw view; no spell should cover more than about 30% of the screen with transparent layers |
| Texture memory, all VFX | ≤ 128 MB |
| Spawn cost | Zero instantiation during a fight: **pool** every cue (warm count from the arena's ability list) |
| First play | **Prewarm** every VFX asset used in the arena during loading so there are no first-cast hitches |
| Culling | Off-screen and distant cosmetic effects pause; gameplay effects keep simulating but stop rendering |
| Tracking | A debug overlay shows active instance count per category (player, other players, boss, status, cosmetic) |

## 12. Class starter briefs

Current prototype kits from the class-test harness ([`ClassKits.cs`](https://github.com/Risxhb/collab/blob/feature/class-test-arena/unity/Assets/LastBell/Core/ClassTest/ClassKits.cs)). Names and numbers are provisional.

**Vanguard:** cobalt, ivory, gold; wedges and clock hands; heavy and short.
- *Clockblade Combo:* `VFX_T_Slash` variants, one mesh arc per hit; hit 3 is wider with a gold highlight. Impact table: melee, boss body.
- *Pendulum Rush:* `VFX_T_Dash` with a pendulum-arc afterimage; strike uses the slash template.
- *Reversal Rush:* rush plus a radial shockwave (`VFX_T_Burst` with a clock-face ring) and a persistent mark glyph on the boss.
- *Final Hour:* a large golden clock-hand sweep (mesh), brief afterimage, then a fast clear.

**Arcanist:** violet and teal; straight lines and lenses.
- *Spark:* `VFX_T_Projectile`, small, fast, thin ribbon.
- *Arc Lance:* `VFX_T_Beam`, a straight segmented lance with lens flares at the ends.
- *Resonant Cast:* a lens array that charges (`ChargeRatio`), then releases a line burst.
- *Ward Step:* `VFX_T_Dash` as a blink: start and end glyph rings, no trail between them.

**Healer:** brass, ivory, gold, a little teal; pulses and chimes.
- *Gleam:* a small bright projectile with a chime ring on hit (it is a damage spell, so it uses the damage impact table).
- *Mend:* a thread or tether from the healer to the target during the cast, then `VFX_T_Heal` on the target, plus small tick pulses for regen.
- *Sanctuary:* a caster-centred friendly area: dotted rim, rising rings, an ally icon, no filled floor.
- *Purifying Ward:* `VFX_T_Shield` bubble on the target; the absorb hit uses `OnHit`; the cleanse uses a short upward burst.

**Boss (Bellkeeper):** Bell Sweep → `VFX_T_Telegraph_Cone` (filled wedge, harmonic ray marks); Hollow Toll → `VFX_T_Telegraph_Donut` (annulus, both borders solid). Resolve effects use coral and amber and the impact table's `AOE_GroundOrStomp` entry.

## 13. Review checklist (every new spell)

- [ ] Triggered only by simulation events; no gameplay timers in the effect
- [ ] Uses the uber shader and §5.1 property names; plays from a pool
- [ ] Built from a template; class gradient preset applied
- [ ] Gameplay beats line up with simulation ticks (check frame-by-frame at 60 fps)
- [ ] Readable at gameplay camera distance and in a 50%-scale screenshot
- [ ] Does not cover boss telegraphs; dimmed over active telegraphs
- [ ] `Focus = 0` version checked (another player's view)
- [ ] Reduced and Minimal intensity checked; `NoFlashes` and `NoScreenshake` respected
- [ ] Grayscale and colour-blind (deuteranopia, protanopia) screenshots still read
- [ ] Within particle and instance budgets; no first-cast hitch (prewarmed)
- [ ] Late-event playback checked (start the effect 6 ticks late)
- [ ] An eight-player capture of the ability with all players casting, reviewed

## 14. Build order

1. Add VFX Graph; create `SG_FX_ColorDissolve` and `SG_FX_Telegraph`; build the 30-texture core library and the basic shape meshes.
2. `SpellCue`, `SpellCueRouter`, pooling, prewarm, the focus rule and the active-instance overlay, hooked to the class-test simulation events.
3. Telegraph templates (Circle, Donut, Cone) driven by Bellkeeper hazard data, verified against simulation membership tests.
4. `VFX_T_Impact` + impact table; hit-stop and shake hooks; accessibility toggles.
5. Vanguard kit (slash, dash, burst templates), then Healer (heal, shield, friendly area), then Arcanist (projectile, beam).
6. Eight-player stress capture, budget pass, then human visual review.

---

*Reference findings were gathered with REA from Windblown's shipped metadata and asset names, for technique study only. No Windblown code or assets are used or redistributed.*
