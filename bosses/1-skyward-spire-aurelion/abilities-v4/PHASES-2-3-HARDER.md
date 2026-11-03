# Phases 2 and 3 harder, and Last Light at once (accepted 2 Nov, Chzy18)

Chzy18 said "P2 and 3 are too easy and p4 needs to start immediately when it hits the health threshold", and added "Don't make it hard by upping damage make it harder by giving the players something to do/dodge". **Damage is unchanged in every phase.**

## Accepted
- **Errant Planets (Phase 2).**
  - Two planets (one on Easy), 1.6 m across, circle the arena on off-centre orbits at 4.5 m/s, in opposite directions. Their paths are drawn as faint dark-red rings (gold ones vanished among the floor's own gold rings). Shots: `shots/01_aurelion_errant_planets_gamecam.png`, `shots/02_aurelion_falling_meridians_gamecam.png`.
  - Touching one deals 30 and knocks you back 1.5 m, at most once a second per player.
  - They run over every Phase 2 block except Total Eclipse.
  - Code: `ErrantPlanets.cs`.
- **Falling Meridians (Phase 3).**
  - Every 5 s, each standing player gets a 2.5 m circle at their feet, locked where they stood. It lands after 1.2 s (Easy 1.5 s, Hard 1.0 s) and deals 30.
  - It runs over every Phase 3 block except Severance.
  - Code: `FallingMeridians.cs`.
- **Shorter warning windows.** Phase 2 is ×0.9 and Phase 3 is ×0.8, on top of the difficulty, with the same minimum times. This applies to Lances, Starfall, Orrery, Hour Hands and Zenith Collapse; on Medium the Collapse window is 2.55 / 2.30 / 2.04 s.
- **Last Light at once.** At 20% the running block is cut off: its shapes fade with no hit, the bots' jobs are dropped, and the phase change starts that tick. The changes at 70% and 40% still wait for their block to finish.

## Rejected
- Damage ×1.15 / ×1.3 in Phases 2 and 3.
- Misaligned Ring and Gnomon Sweep. Both were answered by jumping, and Chzy18 said "we have decided against jumping in the game".

## Rules check
- **No one-shot:** every new hit is 30 before the difficulty scale.
- **No class needed:** every class can walk or dash out.
- **Melee and ranged:** both are in danger, since the planets cross every radius and the circles follow each player.
- **1–4 players:** there is one circle per player, and the planets are the same for any party size.
