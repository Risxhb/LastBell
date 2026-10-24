# Last Bell — current game design

Decision date: October 20, 2026 (America/Chicago). Source: Risxhb's completed list, published in [board #29](https://github.com/Risxhb/collab/issues/29). This supersedes conflicting gameplay assumptions in older plans, references and prototypes. It records direction, not implementation or collaborator acknowledgment.

## October 21 reconciliation — current direction

Current human decisions and the class-specific handoffs override conflicting October 20 blanket restrictions. Sources: [Vanguard #31](https://github.com/Risxhb/collab/issues/31#issuecomment-6306130658), [Chimesinger #36](https://github.com/Risxhb/collab/issues/36), [Arcanist #41](https://github.com/Risxhb/collab/issues/41), and [health/dodge decision](https://github.com/Risxhb/collab/wiki/Updates#2026-10-21--decisions-no-dodge-immunity-health-refills-per-boss-potions-carry-until-the-final-boss).

- **No dodge or post-hit immunity.** Avoid damage by leaving the resolved footprint; deduplicate each attack instance per player.
- **Health and potions:** start a fresh run full; refill health before each subsequent boss; carry spent potions between minibosses; refill health and potions before the area's final boss. The #42 prototype uses five potions, +40 HP and a 1 s lockout; these quantities remain tuning.
- **Roster:** Vanguard, Chimesinger and Arcanist. The generic Healer placeholder is retired. Duplicate classes remain supported; no encounter requires a healer.
- **Vanguard:** Steel Rhythm, Sundering Blow, Last Word and Brace; dash separate. Brace permits partial frontal mitigation and knockback prevention on mitigated hits; rear attacks and ground hazards bypass it. Opening is one non-stacking empowerment. No parry, counter loop or iframe exception.
- **Chimesinger:** DPS kit with Harmony, two-charge Bellstrike, bells/echoes, Crescendo, immediate self-inclusive nearby-ally Mending Chorus and separate dash. This specifically permits an attack resource and burst healing; it does not authorize arbitrary resources/healing in other kits.
- **Arcanist:** Clockbolt Ticks, two independent Time Seals with ripe windows, empowered-Lance inscriptions, Convergence, Temporal Anchor return/shield and Clockstep. Timings, costs, damage and shield values are prototype tuning.
- **Campaign reference:** [#44](https://github.com/Risxhb/collab/issues/44) describes Skyward Spire, Brass Foundry, Verdant Chimes and Shattered Hour: nine minibosses, three area bosses and one final boss (13 encounters). [#47](https://github.com/Risxhb/collab/issues/47) delivers Aurelion, Crucivar, Sylvara and Bellkeeper concepts/phases for review. These reference packets do not establish accepted production content or expand the current **one-arena three-plus-one slice**.
- **Evidence:** draft #42 at `18e89ed` reports build 15 at **107/107**, after old test-code removal; the earlier task-list snapshot reported 108/108. Neither count establishes human feel, multiplayer or final balance acceptance. PF-02–PF-06 have reported implementations awaiting human review; PF-07 is a first pass with substantial remaining scope.

The October 20 restrictions below are historical where explicitly superseded here. [Playtest and decision queue](https://github.com/Risxhb/collab/wiki/Playtest-and-Decisions) separates settled direction, provisional values and decisions still needed.

## Confirmed decisions

| Area | Current decision |
| --- | --- |
| Identity/name | **Last Bell**, a real-time boss rush. Defeat the bosses. |
| Core loop | **Three minibosses → shop intermission → larger boss.** Shop functionality comes later. Exact campaign length/repetition beyond this sequence is open. |
| Enemies/levels | Bosses only, in handcrafted arenas. No normal mob encounters. Claude primarily creates arena content; Codex integrates the playable Unity scene. |
| Combat | Melee, ranged and magic; mouse-cursor aiming for all abilities; rotation-like combos. No auto-target, lock-on or target cycling. Boss abilities must be avoided: no parry; Vanguard Brace is the approved partial frontal mitigation exception. |
| Hit consequences | Brief hit-stun; support knockback and other authored hit mechanics. Mistakes have consequences. Tune stun/knockback/repeated hits to avoid unavoidable chains. |
| Movement | Walking, jumping, limited sprint, dodge/dash and enemy collision. Jump/sprint/dodge share movement stamina. **No invincibility frames**, including dash or blanket post-hit immunity. |
| Characters | Fixed characters; **three starting classes**. Use the approved class-specific slots listed above, with dash separate; the original uniform four-slot capacity is superseded. |
| Weapons/abilities | Class-specific starting weapons; buyable class weapons later. Ability cooldowns; Vanguard has no attack-resource costs. Chimesinger Harmony and Arcanist Ticks/seals are approved class state. Movement stamina is separate. Upgrades later; equipment switching remains a future decision. |
| Death/practice | Death restarts the whole run; separate practice mode. Co-op downed players can be revived, with duration increasing for that downed player's own repeated deaths. See interpretation below. |
| Difficulty | **Four clock/lore-themed levels**. Scale mechanics beyond HP; highest difficulty adds a boss mechanic/ability. Names and parameters below are proposals. |
| Controls/UI | WASD and mouse-cursor aim; readable HUD/menus/tutorials and input remapping. The original Space-dodge/uniform four-slot mapping is superseded by class-specific kits. #42 currently uses Space jump, 5/RMB dash, 1–4 class actions, 6 Chimesinger heal/Arcanist Anchor, Shift sprint and F potion; these are prototype bindings, not remapping acceptance. |
| Presentation/technology | Retain adopted clockwork-fantasy art, modeled 2.5D/fixed isometric camera, Unity/C#, Windows-first, solo plus 2–8 total online including host. Existing docs govern unaffected structure/platform details. |
| Deferred | Progression/talents, additional healing systems beyond approved kits/potions, story/lore detail, shop economy/inventory, upgrades and equipment switching. Existing world ideas are brainstorming material. |

**Superseded October 20 roster note:** the earlier Vanguard/Arcanist/Healer placeholder and undecided-healing assumptions are replaced by Vanguard/Chimesinger/Arcanist and the explicit healing approvals above. Duplicate classes remain supported.

## Implementation interpretation and defaults

These make the list actionable and are distinct from user-specified requirements. Adjust proposals through playtests or human feedback.

- **Run failure:** interpret death as solo defeat or a co-op party wipe/terminal failure. Downed co-op players can be rescued first. Restart the current run, not account data or the application. Practice resets are separate.
- **Revival proposal:** a per-player, per-run down count survives encounters/revivals and clears on a fresh run. Start at 3 seconds, then +2 seconds per additional down for that player. Interruption rules, terminal-death timeout, revive range and restored health remain open. Downed/disconnected actors stop acting and cannot leave impossible mechanic assignments.
- **Movement speeds (delegated choice):** begin at **6 m/s walk**, **9 m/s sprint**, **3 m over 0.20 s dodge/dash**. These are initial tuning defaults, not measured balance. U3 must publish units/tick rounding; old arena units are not automatically metres.
- **Stamina proposal:** 100 maximum; sprint drains 25/s, jump costs 20, dodge costs 30; recover 20/s after 0.75 s without spending. Validate repeated actions and reachable safety before adoption. Attacks never consume this budget.
- **Jumping:** retain planar navigation/footprints with authoritative jump state/height. Jump grants no general immunity. Only explicitly authored jump-clearable hazards can be avoided by satisfying a height rule; other attacks still resolve against the actor. Define landing/collision/knockback eligibility in U3.
- **Superseded control proposal (October 20):** 1 Basic, 2 secondary, 3 dodge (alias of Space), 4 optional fourth; **5 reserved/unassigned** pending clarification. Left Shift sprint, J jump and E revive are provisional remappable bindings. Hold-to-repeat Basic is a convenience proposal; other presses are consumed once. Dodge direction/zero-input fallback still needs a rule.
- **No iframes:** deduplicate the same resolved attack/event so it cannot damage twice. This is not immunity to another distinct attack. Disable old guard/counter/invulnerability gameplay presets in production acceptance scenes; isolate and label diagnostic fixtures.
- **Class state:** old Vanguard Resonance/Reversal and guard-to-counter loops remain superseded. Chimesinger Harmony and Arcanist Ticks/seals follow their approved handoffs; generic no-resource wording must not erase them.

## Four difficulty proposals

Names are suggestions for lore review. All tiers must be solvable with the baseline kit, finite stamina, collisions and no iframes; no mandatory upgrade/healing is assumed.

| Tier / proposed name | Mechanical scaling |
| --- | --- |
| 1 — First Chime | Isolated teaching patterns, longer warnings/locks, generous recovery and safe-space margins. |
| 2 — Steady Ticking | Full intended base patterns, standard timing and positioning demands. |
| 3 — Striking Hour | Authored combinations of known attacks, staggered/subgroup assignments, less recovery; preserve travel and stamina feasibility. |
| 4 — Midnight Toll | An additional original boss-specific mechanic/ability, introduced visibly before lethal combinations. Echo is a candidate, not a mandatory feature. |

Other scaling options: sequencing, safe-region routing, assignment complexity and recovery-window placement. HP/damage can tune duration/punishment but cannot be the only change. Verify difficulty independently from party counts 1–8. Do not uniformly speed every attack into an impossible stamina check.

## Boss references and unresolved tuning

Use the supplied [Bellkeeper v1.0 reference](https://github.com/Risxhb/collab/blob/docs/bellkeeper-design-reference/reference/LAST-BELL-BELLKEEPER-BOSS-DESIGN-v1.0.md) and [Claude's reconciliation](https://github.com/Risxhb/collab/issues/8#issuecomment-6301563765) as input. Preserve the supplied reference; revise the working specification to match this direction. Its older party limits, optional recovery and proposed numbers do not override current decisions.

The reported Bell Sweep escape deficit remains unresolved. Dodge is now required but old dash iframes are rejected. Recalculate using actual displacement, finite stamina, collision, reaction margin, jumping rules and shared units. Lengthen warning, shorten tracking or narrow the cone if needed. Guard, parry or immunity cannot be the solution.

Exact HP/damage, phase structure, optional Echo, hit-stun duration, revive details and highest-tier ability need spec/balance work. No blanket adoption of Vanguard 2.0 or Bellkeeper v1.0 numerical values is implied.

## Scope and acceptance changes

The subsequent requested playable-slice plan puts the complete three-miniboss/final-boss sequence in **one arena for now**. See [single-arena slice](plans/single-arena-slice.md) for proposed encounters, transitions, practice, staged implementation and acceptance. This changes the immediate complete-run milestone; first-class/first-fight work remains an engineering increment.

- First engineering increment: one Unity arena, one class/boss teaching sequence, cursor aim, movement/stamina/no-iframe dodge, truthful damage/stun, full practice reset and Windows build. Training targets are testing fixtures, not normal mob content.
- Starting roster target is three classes; implementation can still start with Vanguard.
- Run content target is three miniboss encounters, intermission representing the future shop, then a larger boss, all in one arena for the requested slice. Stand-in encounters prove the full flow first; authored bosses and complete acceptance follow the staged plan.
- Shop purchases/weapons, upgrades/relics, talents/progression and additional unapproved healing systems are deferred. Retire mandatory six-upgrade, 12-relic/six-modifier and two-class/two-boss slice gates.
- Practice, run reset, escalating per-player revival, four difficulties and solo/eight-player acceptance remain distinct delivery gates.

## Owners

Foundation [#6](https://github.com/Risxhb/collab/issues/6): shared rules/contracts/project/packages/integration. Chzy18/Codex: Unity arena [#30](https://github.com/Risxhb/collab/issues/30) and networking [#7](https://github.com/Risxhb/collab/issues/7). Chzy18/Claude: model viewer [#31](https://github.com/Risxhb/collab/issues/31), primary arena/content presentation and encounters [#8](https://github.com/Risxhb/collab/issues/8). Humans review feel, visuals, lore names and remaining choices. Publishing a task does not establish acknowledgment or execution.
