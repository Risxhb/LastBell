# 02 — Striking Order (`bellkeeper.striking_order`) — proposal

Part of the Bellkeeper ability set v2 (`ABILITIES.md`; read it first). Status: proposal, designed 2026-10-22, not built. **Co-op mechanic.**

## Fantasy
The three broken clock rings behind the Bellkeeper's back turn like an astronomical clock striking the hours. **Ivory Roman numerals** (friendly identity markers, never coral) appear over the players' heads. One by one a shard of the shattered halo drops toward each numeral, and the ground under it rings in a coral circle with a clock-tick rim, in order: I, II, III… Area look: fractured clock rings, numeral glyphs, a single crack-ring on each strike.

## Shape (shared hazard)
- One **Circle** per strike, **Ground**: radius **2.6 m** at N ≤ 4, **2.2 m** at N ≥ 5.
- Each circle **follows its owner** at 6 m/s (like the current Shattered Chime `Marker`) until it **locks 2.0 s before it rings**, then stays put.
- **Every ring hits anyone inside it** (not just its owner). One hit per ring instance per player; two overlapping rings are two instances (50 damage).

## Telegraph
- Numerals appear at t = 0. **Ring I rings at 3.0 s, then one ring every 1.0 s.** Each ring's circle is drawn from the moment it starts tracking (the numeral's appearance for I, then staggered) and brightens when it locks.
- R&S source timing: numbered order steps 1000 ms; spread median 4.0 s (§3.0).
- Colour: coral circle with crisp edge; the numeral over the head is ivory and never coral.

## Damage
25 per ring at Medium; stun 15 ticks; knockback 1.5 m away from the ring centre.

## Escape route
After lock, leave the ring: 2.9 m (2.5 m at N ≥ 5).
| | 4.2 m/s walk | +dash | 6 m/s walk |
|---|---|---|---|
| Leave a locked ring (2.9 m) | 1.29 s ✓ (2.0 window) | 0.79 s | 1.08 s ✓ |
| Solo chain (4 rings on one player) | steady walking ≥ 2.07 m/s on a curve | | ✓ |
Times include the 0.6 s reaction margin.

## Melee and ranged threat
- **Melee:** the ring follows you to the boss's feet; you must step out and come back (about 1 s of lost uptime per numeral).
- **Ranged:** the ring follows you to the edge too; a caster standing still for Arc Lance or Harmonic Refrain must time the cast to land between their numeral's resolve and the next mechanic.

## Co-op rule and solo rule
- **Co-op:** numerals go to the standing players in a seeded order (skip downed players; a numeral whose owner goes down before lock is cancelled). Every ring hits anyone inside, so players whose numerals are next to each other must be ≥ 5 m apart.
- **Solo (N = 1): I–IV all on you, 1.0 s apart.** The four rings chase you: keep moving on a curve so the earlier locked rings stay behind you.
- N = 2: I–IV alternate A, B, A, B (two rings each).
- N = 4: I–IV, one each.
- N = 8: I–VIII, one each, radius 2.2 m.

## Phase use
- Phase 1: alone, after the Leaning Bell.
- Phases 2–3: **Lean and Order** with Leaning Bell: numerals appear 1.5 s into the cleave's warning; ring I rings 1.0 s after the cleave resolves. The rings follow players as they cross the line, so the party must cross *and* spread on the safe half.

## Punish window
None; 1.0 s recovery after the last ring.

## Easy / Hard
- Easy: ring step 1.4 s, lock 2.5 s before, radius −0.4 m.
- Hard: unchanged.

## R&S source
Block 7 order numbers (`hbs_order_0–7`, "match number!!"); §5 proposal 7 (Striking Order).

## Escape-check plan
Worst case: a locked ring on a player at the wall (must leave sideways, 2.9 m); the solo chain of four; N = 8 crossing a Leaning Bell line and then spreading eight 2.2 m rings on one half (≈ 265 m² half on the Throne: room for 8 rings ≥ 5 m apart).

## Build notes (for the ability session)
- Code: `Run/Encounters/Bellkeeper/Abilities/StrikingOrder.cs`. Use the shared `Marker`/`Circle` hazards; the numeral is a presentation-only HUD/world label in ivory (no damage).
- Autotest: rings spawn and ring at 3.0, 4.0, 5.0… s; a still player is hit; melee (at the boss) and ranged (8 m+, and at the wall) still players are both hit; a player walking out at 4.2 m/s after lock escapes; one hit per instance; solo gives I–IV on one player; scripted N = 2/4/8 players get the right numerals and ring sizes; a downed owner cancels their ring; boss death mid-windup cancels all rings; Easy spacing is 1.4 s.
- Practice hook: `-lastbellAbility striking_order`.
