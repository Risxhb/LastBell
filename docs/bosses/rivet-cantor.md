# The Rivet Cantor

**Choirmaster of the Brass Foundry.** A Brass Foundry miniboss (Area 2): a 3.2 m brass automaton with a porcelain-ivory
mask, a resonant-gold chest core and a great foundry drum slung at his hip. He leads the foundry's work-song: he
*sings* each line of a verse, and a few beats later the floor sings it back as hammered rivets. Every mechanic is
call-and-response: he calls, the party answers.

- **Status:** proposal (November 3, 2026), review answers applied the same day (see "Review decisions"), replacing the earlier "Tuner of the Great Cast" draft (still in git
  history). Practice-only encounter in the current player. Visuals (9 Nov 2026): the rigged Meshy body (14 clips; drum in his right hand and mallet in his left, mirrored from the text below), a
  rivet-headed mallet in place of the old tuning fork, and model props for the rivets and chains (see "Art" below); no
  final FX or audio. Not in the run.
- **Sources read:** `docs/design/content-rules.md` (this brief follows §5 and §10), `docs/bosses/pendulum-sentinel.md`,
  and Chzy18's merged bosses for ideas: Aurelion v4 (Meridian Tower soaks, Zenith Marks, Ecliptic Order), Crucivar v4
  (Forge Rite loop, Casting Order, Ember Brand, punish windows), Sylvara v3 (Two-Colour Choir, Pruning Arc).
- **Code:** `unity/Assets/LastBell/Run/Encounters/RivetCantor/`. Practice: Test Rush > Minibosses > Rivet Cantor, or
  `Play Rivet Cantor Practice.cmd` (`LastBell.exe -lastbellPractice rivet`).
- **Numbers:** Medium, out of 100 player HP. Easy and Hard scale boss damage by ×0.6 and ×1.3 (the run's setting);
  the table at the end lists the per-level timing changes and Hard's named extra mechanic.

## Identity and look

- 3.2 m tall against a 1.8 m human: a broad brass bell-shaped torso, ivory mask, gold chest core, the drum at his left
  hip (cobalt skin, brass hoops) and a rivet-headed mallet in his right hand.
- **Art (9 Nov 2026, `art/miniboss-rivet-cantor`):** the rigged body holds the drum in his right hand and the
  **mallet** in his left (the model's hands; the fork was cut out and the mallet bone-parented, clips unchanged). Props:
  a **rivet head** punches up out of a torn floor plate where each Rivet Run warning has just resolved (gone in 0.9 s);
  **brass chains** hang between chained players (or a player and the drum), sagging while shorter than 7 m and straight
  when safe, then snap or lash at the lock; a cobalt-banded **shackle** at each end. Presentation only; LAN clients see
  them too. Prompts, critic scores and report: `build-evidence/miniboss-art-20261109/rivet-cantor/` (outside git).
- **Fallback:** without the GLBs, a labelled primitive stand-in body ("STAND-IN" in the hierarchy) and primitive props.
- **The call queue:** while he sings a verse, one glyph per call floats above his head, oldest lowest:
  a gold disc for *Chorus*, four ivory beads for *Solo*. A glyph disappears when the floor answers it. This is the
  "score" players read; the floor telegraph for each answer appears at the same moment as its glyph.
- **Tempo:** 120 BPM above 50 %, 140 BPM below. Every resolve lands on a beat.
- **Raid indicators:** every warning is a real hit shape drawn with the shared Telegraph-Style v2 look
  (`HazardSystem.RaidStyle`, Brass Foundry motif). The Chorus soak shows its numeral (see Work-Song Verse). Marks also tag the player
  (`PlayerController.Marks`) so the HUD names them.

## The four mechanics

### 1. Work-Song Verse (call and response: stack / spread) — the signature

- **Tell:** he lifts the mallet and beats the drum's rim; each beat is one *call*.
- **The calls:** 3 calls above 50 %, 4 below, **3 beats apart**. Each call is one of:
  - **Chorus** (stack): a 4 m soak circle round his feet. It is a shared soak: **70 total**, split between everyone
    inside (settled §5 rule). **Empty = a heavy hit, not a wipe:** if nobody is inside, everyone standing takes **80**.
    The soonest Chorus shows a big ivory **numeral** (players needed: everyone standing, live) with one slot tick per
    player, filled for each player inside, just outside the ring on the camera's side (the Mould Press soak look).
  - **Solo** (spread): a 2.6 m circle on every player. It never hurts its owner; anyone *else* inside it takes **40**.
    Each Solo also strikes **his feet: everyone within 2.5 m of him takes 40**, so melee steps off him for every Solo (the 2.5–4 m band is in the Chorus and clear of his feet).
- **The answer:** each call's telegraph appears the moment he sings it and resolves **6 beats later** (3 s at 120,
  2.6 s at 140). So the floor always lags the song by two calls: you are standing for call *n* while reading call
  *n + 2*.
- **Verses are fixed, not random** (learnable): above 50 % they alternate *Chorus · Solo · Chorus* and
  *Solo · Chorus · Solo*; below 50 % *Chorus · Solo · Solo · Chorus* and *Solo · Chorus · Chorus · Solo*.
- **Melee threat:** every Solo forces melee off his feet (2.5 m) and to fan out round him; a Chorus landing right
  after a Solo means stepping back in on time (the 2.5–4 m band). **Ranged threat:** every Chorus is at his feet, so ranged players must come in (from the edge that
  is ~9 m in 3 s, inside the 4.2 m/s escape check).
- **Tag:** Ground (both).
- **2 players:** both stack for Chorus (35 each); for Solo, stand on opposite sides of him.
- **3 players:** Chorus 24 each. Solo: a triangle round him (melee front, ranged at ~5 m on the flanks).
- **4 players:** Chorus 18 each. Solo: the four quarters round him; this is the tightest version, which is why Solo
  circles are 2.6 m and not larger.
- **Solo (1 player, or bots):** Chorus takes the whole 70 (survivable from full); missing it costs 80. The Solo mark
  has no one else to hit, but his feet still do: a solo verse is "in for every Chorus beat, out past 2.5 m for every
  Solo beat".
- **Hard — Encore:** the verse's last call is sung a second time, 3 beats later.

### 2. Rivet Run (sequential lines: the drum's echo in the floor)

- **Tell:** he slams the mallet into the drum's face; four lines of rivet heads appear.
- **The lines:** 4 lines in an X from his feet to the floor's edge, rivets 0.9 m radius, 2 m apart.
- **The hammering:** rivets pop in order, **outward from his feet**, one every half-beat, all four lines together;
  first pop after 3 beats. **35** each.
- **The counter-line:** when the first X starts popping, a second X turned 45° appears and pops **inward from the
  edge**, starting when the first set has finished (above 50 %) or halfway through it (below 50 %).
- **What you do:** stand between the lines. Near him the gaps are narrow, so melee steps out along a gap; ranged
  players at the edge have to leave the second set's outer rivets, which fire first.
- **Melee threat:** the first rivets are at his feet. **Ranged threat:** the counter-line starts at the edge.
- **Tag:** Ground.
- **2 / 3 / 4 players:** no job split: everyone dodges their own gap. In co-op the four quadrants are natural homes
  (one player per gap).
- **Solo:** the same.

### 3. Rivet Chains (stretch tethers)

- **Tell:** he swings the mallet in a wide arc and sings one long note; brass chains snap between players.
- **The pairs:** players are chained by their **order numerals**, I–II and III–IV (party slots), and each chained
  player shows their numeral while the chain is up. A player whose numeral partner is missing or down (III in a party
  of 3) is chained **to the drum** (to him).
- **The rule:** after **5 beats** each chain locks. A chain shorter than **7 m** snaps: both ends take **45** (the
  player chained to the drum takes 45 if within 7 m of him). The locked chain also whips along its length: anyone
  *else* standing on it takes **40**.
- **Telegraph:** a 7 m circle round each partner that hurts only the other end (so it reads as "keep out of your
  partner's circle"), plus the chain as a thin lane between them.
- **Melee threat:** the drum-chain (odd player, or solo) drags you out of melee; melee partners have to split
  sides. **Ranged threat:** two ranged partners on the same side have to cross the arena, and the whip lanes cut
  through the space other players use.
- **Tag:** Strike (a chain lash).
- **2 players:** one chain: walk to opposite sides of him.
- **3 players:** players 1–2 chained to each other, player 3 to the drum: 3 stands well clear of him, and must not
  stand on the 1–2 chain.
- **4 players:** two chains; the pairs pick perpendicular axes (1–2 north/south, 3–4 east/west is the simple call)
  so neither chain crosses the other's players.
- **Solo:** chained to the drum: get 7 m away from him.

### 4. Anvil Hymn (expanding drum rings) → Retuning (the punish window)

- **Tell:** he lifts the drum over his head for 3 beats; the core glows.
- **The pulses:** the drum is struck once, and four pulses run outward: a 5 m disc at his feet on beat 3, then rings
  5–8.5 m (beat 4.5), 8.5–12 m (beat 6) and 12 m–edge (beat 7.5). **50** each.
- **What you do:** the pulses travel faster than you walk, so you don't outrun them: you step **inward through** a
  pulse after it lands. Melee steps out to ~5.5 m for the disc and back in; ranged players walk inward across the
  ring that has just landed.
- **Melee threat:** the disc at his feet. **Ranged threat:** the last pulse is the floor's outer band.
- **Tag:** Ground.
- **Retuning:** the drum skin splits; he kneels to re-tension it for **3.5 s**. Nothing is live: the fight's major
  punish window (long enough for every heavy attack and stationary cast).
- **2 / 3 / 4 players:** no split; every player reads the same rings. **Solo:** the same.

## Phases and rotation

- **Above 50 % (120 BPM):** Verse → Rivet Run → Rivet Chains → Anvil Hymn + Retuning, then repeat.
- **50 %:** he finishes the current mechanic, then a 2 s harmless **Key Change** (he stays damageable, no heal, same
  bar): the mask rings, tempo 120 → 140.
- **Below 50 % (140 BPM):** Verse (4 calls) → Rivet Run (counter-line overlapping) → **Chains under Rivet Run** (the
  chains lock on the beat after the first X has finished: the only two-pattern overlap, routes checked by the bots in
  the acceptance test) → Anvil Hymn + Retuning.
- **No enrage** (content rules §5). Pools: none.
- **HP:** 3600 (co-op ×(1 + 0.7 per extra attacking player), as Aurelion v4). Target length 60–120 s.

## Difficulty

| | Easy | Medium | Hard |
|---|---|---|---|
| Verse answer delay | 8 beats | 6 beats | 6 beats |
| Verse | as Medium | as above | **Encore** (named extra): last call sung twice |
| Rivet first pop | 4 beats | 3 beats | 3 beats |
| Chain lock | 6 beats | 5 beats | 5 beats |
| Anvil Hymn tell | 4 beats | 3 beats | 3 beats |
| Damage taken | ×0.6 | ×1 | ×1.3 |

## How it plays at each party size (summary)

| | 1 (solo) | 2 | 3 | 4 |
|---|---|---|---|---|
| Verse Chorus | take 70 alone | 35 each | 24 each | 18 each |
| Verse Solo | off his feet (2.5 m) | opposite sides, off his feet | triangle | quarters |
| Rivet Run | dodge | dodge | dodge | one per gap |
| Chains | drum-chain: 7 m from him | I–II, opposite sides | I–II + III to the drum (no crossing) | I–II and III–IV on perpendicular axes |
| Anvil Hymn | step in through pulses | same | same | same |

Bot allies (0–3, Perfect mode) solve every mechanic: Chorus is a normal shared soak (the generic soak job), Solo and
the chains are hazards that hit only *other* players, so bots keep out of them like any telegraph.

## Review decisions (November 3, 2026)

1. **Empty Chorus:** a heavy hit (80 to everyone standing), not a wipe. Done.
2. **Solo matters solo:** yes. Each Solo also hits everyone within 2.5 m of him (40); the Chorus grew to 4 m so the band between them (2.5–4 m) holds a whole party. Done.
3. **Chain pairing:** by the order numerals I–II, III–IV, shown over the chained players. Done.
4. **Model:** keep the earlier Meshy model and re-rig/animate it for the drum and mallet. Done 9 Nov (rig and clips
   on `feature/miniboss-models`; the mallet and props on `art/miniboss-rivet-cantor`).
5. **Run slot:** none yet. Bosses will share arenas and the gameplay loop is not decided; he stays practice-only on the
   Crucivar map.
6. **LAN lobby:** add him to `LanLobby.Encounters` after all the miniboss work is done.
7. **Minibosses card row** (fits 5): the menu gets adjusted, with graphics, once the bosses are done.
8. **Chorus numeral:** added (above), after a critique pass: live count instead of the count at the call, slot ticks
   for who is in, placed just outside the ring on a smoothed camera side, the soonest Chorus only, hidden on defeat.
   Drawn by the host only; LAN clients see the soak ring without the numeral until the lobby work above.
9. **Chains and standing still:** fine as is (chains threaten by geometry).
