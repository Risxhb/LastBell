# Ultimate v2 · the end of the fight (proposal)

Status: **proposal**, built in Unity (2026-10-26: both transitions, P4, P5 with the Echo and the Last Stand, all on Easy, Medium and Hard). Nothing here is locked or human-approved. In Unity, hits on the boss inside the bell count x1.25 (`V2P5Resonance`, first asked as x2) and x4 in the Last Stand. A dash gives no protection anywhere (no iframes).

Earlier status line: Browser mockup: `tools/bellkeeper-ultimate/sims/p5-example.html`
(headless test: `node tools/bellkeeper-ultimate/sims/test-p5.js`). It has three scenes and a "play all three in a row"
button. P4 itself is in `p4-example.html` and in Unity (`GearFloor.cs`).

The arc: P3's bullet hell breaks the floor into P4's gear train. The train runs wild and tears itself apart, the great
bell falls over the arena, and P5 is fought inside it. Every earlier phase comes back for one last toll.

## T · P3 → P4 · The Floor Breaks (15 s)

- P3's spiral arms keep firing, twice as fast (50 per bullet).
- The floor cracks along the gear outlines (the cracks glow first) and falls into the pit in two waves:
  - at 6 s, the outer floor between the gears (beyond 9 m);
  - at 10 s, everything that isn't a gear (999).
- At 12 s the gears lock. Each player must be on their first P4 gear (hour h → gear h+2, the same rule as P4's first
  slam), or the train jams: WIPE.
- At 15 s the gears turn and P4 starts.
- Why: it teaches P4's "your gear" rule before anything moves, under bullet-hell pressure.

## T · P4 → P5 · The Train Runs Wild (15 s)

- The gears speed up from ×1.0 to ×2.2 over 12 s.
- The hammer slams a random gear every 2.5 s (shown 1.5 s before) and throws its 12 shards.
- The outer gears tear loose in hour order: N, E, S, W at 9, 10.5, 12 and 13.5 s. Their cores flash for 2 s first.
  - Anyone still on a gear when it launches goes with it (999).
  - Each launch sprays a fan of 5 shards at 24° spacing, which leaves gaps you can step through.
- Everyone ends packed on the centre gear. At 15 s the great bell crashes down over the arena, and P5 begins inside it.

## P5 · Final Toll (inside the great bell)

The bell tolls every 4 s for tolls I–VI, then every 3.5 s for VII–XII. From toll X the **echo** doubles every toll (see
below). **Toll XIII (48.5 s) shatters the bell** into the Last Stand (below), a 10 s burn to finish it. In the mockup,
a perfect party of 4 kills it about 6 s into the Last Stand.

Every toll brings the same things.

1. **Clapper.** The boss strikes a 3 m lane straight across the bell, at a random angle. It is shown 1.6 s before and
   kills only on the toll (999). Walking across it while it is telegraphed is fine. The lane always misses the plate.
2. **Sound ring.** A ring rushes inward from the rim at 5 m/s, all the way to the boss, so hugging the boss doesn't skip
   it.
   - It has four gaps marked I–IV. Pass through your own gap only: another hour's gap is 999, the ring itself is 50.
   - Each hour's gap moves 60° per toll, so you keep relocating around the boss.
3. **The echo** (tolls X–XII). A second ring (blue) follows 0.8 s behind the first, with every gap 45° further round.
   You thread your gap, then step to your next one.
4. **Callbacks, which stack.**
   - **Tolls I–VI:** one callback per toll, in turn: plate → debts → INVERTED → hammer.
   - **Tolls VII–IX:** two per toll (INVERTED + plate, hammer + debts, plate + INVERTED).
   - **Tolls X–XII:** all four on every toll.

   **The rings lead you to your job.** The plate lies by the soaker's last gap, and the INVERTED half is centred on the
   INVERTED hour's last gap. The two neighbouring hours sit on the half's edge and only need to step out of it.

   The four callbacks:
   - **Plate** (P1/P2): the next hour soaks the lit plate (5.5 m from the boss, 20° clear of the INVERTED half), or WIPE.
     - **Standing on the lit plate pays any debt** (as in P1). This keeps the all-four tolls fair.
   - **Debts** (P1): a 2 m ring under every player that detonates on the next toll (999).
   - **INVERTED** (P2): the half slams. The INVERTED hour must be in it and everyone else out (999).
   - **Hammer** (P4): a ring of 12 shards bursts from a random point near the rim (50). The burst point is marked 1.6 s
     before.

**Mainspring (P3 returns).** From toll IV, the boss fires P3's three spiral arms between sound rings, stopping 0.8 s
before each toll (50 per bullet).

**The Echo Bellkeeper** (Chzy18, 25 Oct: "a mix of 2 and 3"). From toll IX, a ghost Bellkeeper (blue) stands 6.5 m
out, across the boss from that toll's plate, and moves after every toll. It strikes its own clapper lane through itself
(999 on the toll, shown 1.6 s before, always misses the plate). The two share one bar, and **only the one that rang last
takes damage** (gold LIVE): the echo after IX and XI, the boss after X and XII. So the party swaps targets every toll,
and melee have to cross the bell to do it.

**Last Stand.** Toll XIII doesn't wipe. It **shatters the bell**:
- Only four platforms stay, one per hour. Each is 1.9 m in radius, 3.4 m from the boss, and touches it, so a melee lunge ends on the platform. They are shown from toll XII.
- Off your own platform at XIII, or at any point after, is the fall (999). Another hour's platform doesn't count.
- Bell shards drop on the platforms, never at their centre, so there is always room. Each is shown 1 s before and kills
  (999).
- **Every hit counts ×4 for 10 s.** If the boss is still standing at the end, the bell falls on you (WIPE).

In the mockup, a perfect party reaches XIII with the boss at about 40 % and breaks the bell about 6 s into the last
stand, with 4 s to spare. One death in the last stand makes it very tight.

The kill: the boss dies, and the great bell cracks open ("THE LAST BELL").

**Mockup result (with the Echo and the Last Stand):** in 10 random starts, all 10 kill the boss in the last stand (8
with 0 damage, 2 with one 50-damage hit). In the 5 test starts, 4 are clean kills, and 1 loses a melee player to the
echo's clapper on toll XI. Every scripted mistake wipes, including the new one, "XIII: II runs to the wrong platform".

**Unity:** built (`FinalToll.cs`). Hits count ×1.25 inside the bell before XIII, then ×4 in the Last Stand. Perfect bots kill in the
Last Stand (party of 4, trio and P1–P5 with 0 deaths; party of 8 with 1); the slow-reacting bots wipe when the bell falls.

## Design rules check (gpt/08)

- No unavoidable damage: every hit is from a shown or known rule. The mockup's perfect bots take 0 in all three scenes
  (5 jitters each).
- No invincibility: nothing relies on dash immunity.
- No class requirement: plates, gaps and halves are by hour, not class.
- Solo: unchanged phases, only HP and push timers scale (Chzy18, 25 Oct). Open: how a solo player covers the
  plate/INVERTED hours (see below).

## Open questions

1. **Solo and duo.** Plate and INVERTED name an hour. With fewer than 4 players, does one player take every hour, or
   do the hours fold onto who's there (as in P1–P4)? Is this still "no phase changes"?
2. The enrage is now tight (a perfect kill at about 47 s, XIII at 48.5 s). Is that right once real players have mechanics in hand?
3. The transitions use 50-damage bullets and shards. Do potions refill before P5? (Rule: potions carry between
   minibosses; P5 is inside one fight.)
4. Unity: none of this is built yet. P4 (`GearFloor.cs`) is the latest built phase.
