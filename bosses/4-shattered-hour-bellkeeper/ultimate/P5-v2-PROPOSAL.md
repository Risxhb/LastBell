# Ultimate v2 · the end of the fight (proposal)

Status: **proposal**. Nothing here is locked. Browser mockup: `tools/bellkeeper-ultimate/sims/p5-example.html`
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

The bell tolls, and it **speeds up**: every 4 s for tolls I–VI, every 3.5 s for VII–IX, and every 3 s for X–XII.
**Toll XIII (46.5 s) is the hard enrage**: if the boss is still alive, the hour completes (WIPE). In the mockup, a perfect
party of 4 kills it at about 42.5 s, between tolls XI and XII.

Every toll brings the same three things.

1. **Clapper.** The boss strikes a 3 m lane straight across the bell, at a random angle. It is shown 1.6 s before and
   kills only on the toll (999). Walking across it while it is telegraphed is fine.
2. **Sound ring.** A ring rushes inward from the rim at 5 m/s, all the way to the boss, so hugging the boss doesn't skip
   it.
   - It has four gaps marked I–IV. Pass through your own gap only: another hour's gap is 999, the ring itself is 50.
   - Each hour's gap moves 60° per toll, so you keep relocating around the boss.
3. **Callbacks, which stack.**
   - **Tolls I–VI:** one callback per toll, in turn: plate → debts → INVERTED → hammer.
   - **Tolls VII–IX:** two per toll (INVERTED + plate, hammer + debts, plate + INVERTED).
   - **Tolls X–XII:** all four on every toll.

   The four callbacks:
   - **Plate** (P1/P2): the next hour soaks the lit plate (5.5 m from the boss), or WIPE. The plate is always square to
     the clapper's lane, on the side away from the debts.
     - **Standing on the lit plate pays any debt** (as in P1). This keeps the all-four tolls fair.
   - **Debts** (P1): a 2 m ring under every player that detonates on the next toll (999).
   - **INVERTED** (P2): the far half slams. The INVERTED hour must be in it and everyone else out (999). The half always
     opens on the side where that hour's ring gap was, so you come out of your gap straight into your half.
   - **Hammer** (P4): a ring of 12 shards bursts from a random point near the rim (50). The burst point is marked 1.6 s
     before.

**Mainspring (P3 returns).** From toll IV, the boss fires P3's three spiral arms between sound rings, stopping 0.8 s
before each toll (50 per bullet). As the tolls speed up, the gaps between rings shrink, so the bullet windows get shorter
but fall closer to the tolls.

The kill: the boss dies, and the great bell cracks open ("THE LAST BELL").

**Mockup result:** perfect bots kill the boss cleanly in 19 of 20 random starts. In the other one they still kill it but
take one 50-damage hit. Every scripted mistake (wrong gap, forgotten debt, INVERTED player stays out) wipes the run.

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
2. Is XIII at 46.5 s (a perfect kill at about 42.5 s, one toll of slack) the right enrage window?
3. Is the X–XII "all four" stretch too much to read? A lighter option is three callbacks per toll.
4. The transitions use 50-damage bullets and shards. Do potions refill before P5? (Rule: potions carry between
   minibosses; P5 is inside one fight.)
5. Unity: none of this is built yet. P4 (`GearFloor.cs`) is the latest built phase.
