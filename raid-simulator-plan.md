# Raid simulator game plan

Planning draft · October 20, 2026

## 1. Game direction

**Pitch:** A 1–4 player, top-down action raid game where a compact combat kit meets intricate boss mechanics, and every short expedition offers new ways to build your character.

Give **encounter mastery and roguelite build variety equal weight**, as requested. Learning the fight should improve your next attempt; choosing a different build should change the decisions you make during it.

Working concept: **Last Bell**. Players are wardens exploring a suspended clockwork city whose guardians repeat distorted fragments of the past. Distinct silhouettes, mechanical architecture, and bell-driven attacks give the game its own identity. The title and setting are provisional.

Planning assumptions: Windows PC first; small independent team; keyboard/mouse and controller; solo plus up to four friends online. Engine, team size, budget, art style, and commercial scope remain open. No implementation is included in this planning task.

## 2. What we learned from Rabbit & Steel

The developer describes immediate access to boss fights, cooldown-driven combat, run-based equipment, and cooperation. Its solo encounters replace team mechanics with different attack patterns. These are useful foundations for our design. [Official Steam description](https://store.steampowered.com/app/2132850/rabbit_and_steel?l=english)

The installed game also exposes readable localization tables. Their descriptions contain cooldown resets, charged defensive abilities, movement tradeoffs, ability-changing upgrades, positioning warnings, and visibility controls. These establish the presence of those descriptions, not their precise implementation or current balance.

| Reference lesson | Application to our game |
|---|---|
| A small action set can support meaningful combat decisions | Four actions per class; depth from timing, positioning, and interactions |
| Offensive and defensive systems can interact | Builds can reward a well-timed guard or turn resource spending into protection |
| Equipment can change behavior | Prefer upgrades that alter a rotation or positioning over many small percentage bonuses |
| Team size changes the encounter problem | Explicit solo, duo, and larger-party mechanic variants |
| Clarity needs dedicated settings | Separate hazard and cosmetic effects; configurable ally effects and screen shake |

Our differentiators: rehearsal tools integrated with the run loop, readable explanations of failed mechanics, and bosses that remix previously taught patterns through an original **Echo** mechanic. Use original characters, art, music, writing, and encounter sequences.

## 3. Player experience and core loop

**First session:** Choose a class → learn movement and four actions in a short interactive exercise → fight a teaching boss → choose an upgrade → see how the next boss changes the value of that choice.

**Expedition:** Choose class and starting modifier → fight → choose one of three upgrades → choose the next encounter route → fight → repeat → defeat the final guardian or lose the expedition → review failures and unlock new options.

Target completed expedition: 20–30 minutes. Target main boss: 3–4 minutes. These are starting design targets, to be adjusted after playtesting.

**Rehearsal:** Select a discovered boss and phase, optionally import a saved run build, practice, and retry immediately. Practice earns no expedition rewards. This lets players learn both the encounter and their current build without turning repeated early bosses into mandatory homework.

Initially, save a build recipe after a run for use in rehearsal. Resuming an entire interrupted expedition is a later production feature.

## 4. Combat model

Use a fixed arena and shared view. Keep movement independent of targeting, with automatic boss targeting and a manual target-cycle option when adds exist. Attack shapes and range still matter. Avoid precision mouse aiming as a second mandatory skill.

Every class has four actions:

| Action | Purpose |
|---|---|
| Basic | Reliable damage and resource generation; hold-to-repeat option |
| Technique | Resource spender or combo action that defines the class rhythm |
| Signature | Strong cooldown that rewards choosing a good damage window |
| Guard | Defensive tool with a clear availability indicator and class-specific utility |

Each class should have one main resource and one central decision. Begin with a shared attack cadence for offensive actions and independent guard timing; tune actual values through the prototype.

Prototype health: five pips, with ordinary hazards removing one. Use a short damage immunity window to prevent overlapping effects from causing an unreadable instant death. Mechanic failure, damage taken, and knockout are separate recorded events.

Successful guards can prevent damage while a missed team requirement still counts as a mechanic failure. Mechanics must clearly state when guarding is insufficient. Do not make a mandatory survival check depend on a random upgrade or a class the party might not bring.

No required tank/healer composition in the first release scope. Every class contributes damage and personal survival; support abilities improve team performance without making a class mandatory. Prototype knockouts as spectating until the next encounter, with full recovery between encounters. Revisit a limited rescue mechanic only if spectating proves too punishing.

## 5. Initial classes

| Class | Identity and main decision | Example upgrade directions |
|---|---|---|
| Vanguard | Close-range attacker; choose when to commit a stored strike while staying near the boss | Guard-to-counterattack, charge banking, wider reach |
| Arcanist | Ranged attacker; choose when to spend energy or hold a brief casting stance | Mobile casting, delayed bursts, protective fields |
| Conductor | Mobile support attacker; place a beacon to help allies without losing personal uptime | Moving beacon, pulse damage, shared protection |

Build Vanguard and Arcanist for the playable slice. Add Conductor only after two-class combat and balance are convincing. Duplicate classes must be allowed.

## 6. Boss design and mechanic vocabulary

Start with a small reusable set: circles, cones, lines, donuts, stack markers, spread markers, tethers, and knockback. Each mechanic needs a distinct shape/icon, readable timing, a defined resolution rule, and a party-size variant.

Every boss follows a teaching sequence: show a mechanic alone → repeat with a variation → combine two known mechanics → add a signature twist → finish with a controlled execution test.

**Echo:** A boss can repeat one earlier attack at a visibly marked arena location. Preview the remembered shape before it returns. In the first implementation, retain only one earlier attack and allow one Echo at a time. This creates memory and routing decisions without multiplying visual noise.

Example first boss: **The Bellkeeper**, approximately three minutes, with a final enrage target around four minutes during initial tuning.

| Segment | Encounter behavior | What it teaches |
|---|---|---|
| Opening | Alternating cone and donut attacks | Read the arena and move while attacking |
| Assignment | Player markers resolve into spreads; later, a shared stack | Personal responsibility and regrouping |
| Echo introduction | An earlier cone returns at a marked location | Remember and anticipate a known attack |
| Combination | Echo plus spreads, with generous travel time | Plan routes without blocking teammates |
| Final sequence | Familiar patterns at tighter intervals | Execute consistently while maintaining damage |

Second slice boss: **The Glass Cartographer**, using lanes, marked destinations, and paired tethers. Its purpose is to test whether the same engine can support a different movement puzzle.

Author mechanics for each party size. For example, a stack becomes a designated solo shelter, a paired tether becomes a tether to an arena anchor in solo, and spreads choose a count appropriate to available players. Keep the same visual meaning even when the solution changes.

Randomize tested pattern variants and player assignments, rather than arbitrary overlapping hazards. Build an encounter validator for incompatible timings and insufficient safe space. Each generated sequence must leave a reachable solution at baseline movement speed.

## 7. Roguelite builds and progression

Use three interacting layers: class kit, ability modifiers, and relics. Start the slice with **12 relics and six ability modifiers total**, shared or class-specific as appropriate. Ensure both classes have at least two viable build directions.

Example original effects:

- A precisely timed guard empowers the next technique, with an internal cooldown.
- A signature creates a delayed repeat at reduced damage, requiring you to plan its location.
- A stationary damage bonus persists briefly after moving, supporting repositioning between casts.
- Spending a full resource bar produces a small protective field, capped to prevent permanent protection.

After each boss, each player chooses a personal reward from three offers. Keep offer choices seeded for reproduction. Allow one reroll per expedition initially. Guarantee access to at least one broadly useful option; do not let progression depend on drawing a particular synergy.

Use an explicit effect pipeline: trigger → eligibility → cooldown/charge check → effect → event log. Limit recursive triggers and document which effects can trigger other effects. This prevents infinite proc loops and makes builds explainable.

Permanent progression unlocks classes, cosmetic options, alternative starting modifiers, and additional sidegrade choices. Balance standard difficulty around a fresh account. Avoid permanent numerical power that makes encounter mastery hard to compare.

At expedition end, show both dimensions of success: mechanic execution and build performance. Useful readouts include failed mechanic types, damage uptime, unused guard charges at knockout, and the contribution of major relic effects. Use neutral feedback rather than a public blame ranking.

## 8. Rehearsal, difficulty, and readability

Rehearsal should support phase starts, repeatable seeds, a chosen build, and optional explanations. A phase restart restores health, resources, cooldowns, boss state, mechanic assignments, and random state together. Partial resets would make practice misleading.

Start with a concise failure card: attack name, resolution time, required behavior, and a small arena diagram of the relevant positions. Record actual resolved state so the explanation matches the damage decision. Full replay and arbitrary rewind are later features.

Proposed difficulty structure:

- **Guided:** longer warnings, explanations, forgiving damage pressure.
- **Standard:** complete intended mechanics and normal encounter timing.
- **Challenge:** authored extra combinations and tighter execution requirements.

Ship Standard and rehearsal first; expand difficulty after the core patterns work. Difficulty should change decisions and execution demands, not only health totals.

Visual priority: hazards and safe boundaries → personal markers → players and boss → cosmetic attacks. Use color plus shape/icon, optional ally-effect reduction, scalable UI, remappable controls, hold-to-repeat attacks, and configurable screen shake. Show cooldown information close enough to the player that watching the arena remains practical.

For every attack, define whether it locks onto a position, follows a player until a lock time, or continuously checks an area. Show the lock transition visibly. Telegraph geometry and collision geometry must share the same underlying definition.

## 9. Technology and architecture

**Provisional choice: Godot with GDScript for a 2D PC prototype**, unless the team already has stronger production experience in another engine. Godot documents a dedicated 2D workflow and high-level networking with replaceable multiplayer peers. This is a project recommendation, not a claim about Rabbit & Steel's internal architecture. [Godot 2D documentation](https://docs.godotengine.org/en/stable/tutorials/2d/index.html) · [Networking documentation](https://docs.godotengine.org/en/stable/tutorials/networking/high_level_multiplayer.html)

Keep these systems distinct:

- Combat simulation: actor state, resources, cooldowns, damage, status effects.
- Encounter director: phases, timestamped events, target selection, seeded variants.
- Mechanic definitions: shared geometry, telegraph, lock time, resolve time, party-size policy.
- Build system: modifiers, relics, trigger rules, offer generation.
- Presentation: animation, audio, UI, cosmetic effects.
- Session layer: lobby, player slots, input ownership, disconnect handling.
- Practice/diagnostics: checkpoints and resolved event records.

Author encounters as reusable data plus small scripted behaviors. Build a simple timeline preview with time scrubbing, mechanic toggles, and party-size selection before producing many bosses. This internal tool is a production requirement.

For the prototype, use a player-hosted authoritative simulation, fixed simulation steps, immediate local movement prediction, correction, and timestamped boss events. The host decides targeting, attack resolution, health, and loot. Clients send bounded input requests rather than damage results. Seeded boss sequences aid reproducibility; they do not by themselves guarantee cross-machine determinism.

Test responsiveness early. Compare the authoritative prototype against actual latency tolerance before committing to production networking; evaluate rollback only if the simpler approach cannot meet the combat target. Rabbit & Steel advertises rollback, but that does not make it an automatic requirement for this project. [Developer press kit](https://www.minodevgames.com/rabbit-steel-press-kit)

LAN/direct connection is enough for the first network experiment. A public PC release needs a separately validated lobby and connection/relay solution; basic ENet alone does not solve consumer NAT traversal. Use a transport abstraction and schedule a Steam integration spike before promising online delivery. Host migration and drop-in participation during active fights are outside the initial scope. If the host disconnects, show a clear end-of-session result; other disconnects take effect at the next encounter boundary.

## 10. Build order and gates

The estimates below are rough **person-weeks for an experienced developer**, not a promised calendar schedule. They exclude substantial original art/music production and assume reuse of suitable development tools. Re-estimate after the first combat and networking experiments.

| Stage | Deliverable | Exit gate | Initial effort |
|---|---|---|---|
| Combat experiment | One arena, one class, one boss pattern sequence, guard and damage feedback | Movement and attacking feel responsive; players understand hits | 1–2 person-weeks |
| Network experiment | Two clients running the same encounter, latency simulation, event diagnostics | No divergent mechanic outcomes; acceptable movement and hit feedback | 1–2 person-weeks |
| Complete loop | One boss, upgrade selection, retry, run end, first rehearsal checkpoint | Upgrade choices visibly change play; retry takes under five seconds | 2–3 person-weeks |
| Playable slice | Two bosses, two classes, 12 relics, six modifiers, solo and 2–4 player variants | Both mastery and build choices sustain repeated sessions | 4–6 person-weeks |
| Production planning | External feedback, measured authoring cost, connection spike, revised budget | Evidence supports the next content investment | 1 person-week |

Total planning range: roughly **9–14 person-weeks to evaluate the slice**, with art, integration problems, or unfamiliar tools potentially extending it. Begin with placeholders and a deliberately simple interface.

The full expedition length cannot be demonstrated by two bosses alone. The slice should be a 10–15 minute mini-run with enough reward decisions to test the loop; reserve the 20–30 minute target for broader production content.

## 11. Validation and production scope

Test these risks before expanding content:

| Risk | Practical check |
|---|---|
| Players cannot explain a hit | After a failure, ask what resolved and where safety was; compare with the event log |
| Roguelite choices are cosmetic | Ask players to explain how an upgrade changed their next decision; observe at least two successful builds per class |
| Mechanics become unfair in a smaller party | Play each boss at 1, 2, 3, and 4 players, including duplicate classes |
| Latency changes fairness | Test at 50/100/150 ms round-trip latency and modest packet loss; inspect guard and position corrections |
| Random patterns create impossible states | Reproduce seeds, validate timing constraints, and inspect collision/safe-space overlays |
| Boss creation costs too much | Build the second boss using existing mechanics and measure authoring/debugging time |
| Practice undermines expeditions | Check whether players use rehearsal to return to runs, and whether run-specific builds still feel valuable |

Automated checks should focus on shared geometry, phase reset completeness, cooldown ordering, proc recursion limits, party-size rules, and reproducible seeded schedules. Human testing remains necessary for readability and enjoyment.

Recruit a small initial group with mixed raid experience, including solo players and established friend groups. Proposed slice goals: first meaningful fight within two minutes, retry within five seconds, and most observed failures explainable without a developer intervention. Treat these as iteration targets, not market validation.

If the slice passes, a provisional compact release could contain three classes, six bosses arranged across branching short expeditions, a final guardian, approximately 30 relics, and 12–18 ability modifiers. Confirm whether that actually supports enough distinct runs before fixing the release scope.

Defer an open world, mandatory role trinity, large AI raid parties, competitive rankings, player-made encounters, cross-platform support, elaborate cinematics, full replay editing, and dozens of classes. Add another boss only after the authoring workflow and both core loops are proven.

## 12. Immediate first milestone

Build **Vanguard versus the Bellkeeper** with placeholder visuals, three taught mechanics, one Echo, six prototype upgrades, a failure explanation, and instant restart. Follow immediately with a two-client network experiment, then add Arcanist and the second boss.

The decision at that point is concrete: does the same fight remain enjoyable as players learn it, and do different upgrades give them a reason to approach it differently? Both must work before scaling production.

## Evidence and limits

Local reference inspected read-only: `F:\SteamLibrary\steamapps\common\Rabbit and Steel`.

- `Data/Descriptions_Move.csv`: sampled ability descriptions and upgrade variants, including cooldown interactions, movement costs, defensive charges, and protection effects.
- `Data/Descriptions_Item.csv`: sampled item descriptions that connect attacks, debuffs, defensive actions, and cooldowns.
- `Data/Strings_Menu.csv`: difficulty descriptions, input buffering/hold input labels, screen shake, HUD, and effect visibility settings.
- `Data/Strings_Effect.csv`: positioning warning labels.
- Installation inventory includes `RabbitSteel.exe`, `data.win`, and the data tables above. The package layout suggests a GameMaker build; no engine/source-code conclusion is needed for this plan.
- REA static PE inventory: evidence `ev_d3a2bb3bd8bb2993fd43b7b4951d166e2d41a2d9cd8c55fd75a375bdd3e513c2`; executable SHA-256 `a251a3db60b696677a158839d37a5b0ff4072b35f8321445fad5a5af4297e0f0`. This establishes inspected artifact identity and resource structure only.

No gameplay session was launched, no boss implementation was decompiled, and no runtime timing, collision, networking, or balance behavior was measured. Public behavior claims above come from developer sources; all proposed systems, names, scopes, numbers, and estimates are design choices for the new game. A later hands-on reference session should examine attack readability, input feel, failure recovery, reward cadence, and solo/co-op differences before final tuning.
