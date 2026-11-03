# The Rivet Cantor

**Choirmaster of the Brass Foundry.** A Brass Foundry miniboss (Area 2): a 3.2 m brass automaton with a porcelain-ivory
mask, a resonant-gold chest core and a great foundry drum slung at his hip. He leads the foundry's work-song: he
*sings* each line of a verse, and a few beats later the floor sings it back as hammered rivets. Every mechanic is
call-and-response: he calls, the party answers.

- **Status:** proposal (November 3, 2026), replacing the earlier "Tuner of the Great Cast" draft (still in git
  history). Practice-only encounter in the current player, **placeholder visuals only** (primitive stand-in body, no
  Meshy model, no final FX or audio). Not in the run.
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
- **Placeholder:** brass cylinder body, ivory sphere head, cobalt drum disc, mallet cylinder. Labelled
  "STAND-IN" in the hierarchy.
- **The call queue:** while he sings a verse, one glyph per call floats above his head, oldest lowest:
  a gold disc for *Chorus*, four ivory beads for *Solo*. A glyph disappears when the floor answers it. This is the
  "score" players read; the floor telegraph for each answer appears at the same moment as its glyph.
- **Tempo:** 120 BPM above 50 %, 140 BPM below. Every resolve lands on a beat.
- **Raid indicators:** every warning is a real hit shape drawn with the shared Telegraph-Style v2 look
  (`HazardSystem.RaidStyle`, Brass Foundry motif). Soaks show their numeral. Marks also tag the player
  (`PlayerController.Marks`) so the HUD names them.

## The four mechanics

### 1. Work-Song Verse (call and response: stack / spread) — the signature

- **Tell:** he lifts the mallet and beats the drum's rim; each beat is one *call*.
- **The calls:** 3 calls above 50 %, 4 below, **3 beats apart**. Each call is one of:
  - **Chorus** (stack): a 3.5 m soak circle round his feet. It is a shared soak: **70 total**, split between everyone
    inside (settled §5 rule). **Empty = party wipe**, so the telegraph shows the numeral and the gold glyph.
  - **Solo** (spread): a 2.6 m circle on every player. It never hurts its owner; anyone *else* inside it takes **40**.
- **The answer:** each call's telegraph appears the moment he sings it and resolves **6 beats later** (3 s at 120,
  2.6 s at 140). So the floor always lags the song by two calls: you are standing for call *n* while reading call
  *n + 2*.
- **Verses are fixed, not random** (learnable): above 50 % they alternate *Chorus · Solo · Chorus* and
  *Solo · Chorus · Solo*; below 50 % *Chorus · Solo · Solo · Chorus* and *Solo · Chorus · Chorus · Solo*.
- **Melee threat:** Solo marks force melee to fan out round him; a Chorus landing right after a Solo means stepping
  back in on time. **Ranged threat:** every Chorus is at his feet, so ranged players must come in (from the edge that
  is ~9 m in 3 s, inside the 4.2 m/s escape check).
- **Tag:** Ground (both).
- **2 players:** both stack for Chorus (35 each); for Solo, stand on opposite sides of him.
- **3 players:** Chorus 24 each. Solo: a triangle round him (melee front, ranged at ~5 m on the flanks).
- **4 players:** Chorus 18 each. Solo: the four quarters round him; this is the tightest version, which is why Solo
  circles are 2.6 m and not larger.
- **Solo (1 player, or bots):** Chorus takes the whole 70 (survivable from full). Solo marks have no one else to hit,
  so they are harmless: a solo verse is "be at his feet on every Chorus beat".
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
- **The pairs:** players are chained in pairs in party order (1–2, 3–4). An odd player is chained **to the drum** (to
  him).
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
| Verse Solo | harmless | opposite sides | triangle | quarters |
| Rivet Run | dodge | dodge | dodge | one per gap |
| Chains | drum-chain: 7 m from him | one pair, opposite sides | pair + drum-chain (no crossing) | two pairs on perpendicular axes |
| Anvil Hymn | step in through pulses | same | same | same |

Bot allies (0–3, Perfect mode) solve every mechanic: Chorus is a normal shared soak (the generic soak job), Solo and
the chains are hazards that hit only *other* players, so bots keep out of them like any telegraph.

## Open questions for review

1. Chorus wipes the party when empty (the settled soak rule). Is that the right weight for a *first* Area 2
   miniboss, or should Chorus be a heavy hit (e.g. 80 to everyone) instead of a wipe?
2. Solo marks are harmless to their owner, so solo players never feel them. Should a mark also hit its owner if they
   stand within 3 m of him (a melee check), so Solo matters solo too?
3. Rivet Chains pair players in party order. Should the pairing instead follow the ivory order numerals
   (I–II, III–IV) once those markers are shown in-game?
4. The model: the earlier Meshy rig (`RivetCantor.glb`, `rivet_cantor_rig.py`) has fork and dial clips for the old
   design. Re-rig for drum and mallet, or a new model?
5. Run slot: which of the three Brass Foundry miniboss slots should he take once he joins the run?
