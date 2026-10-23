# Aurelion ability set v4: mythic end-boss difficulty (proposal)

## Brief

Chzy18, 2026-10-22: "It's still not hard enough. I need it to be as difficult as the end boss in a mythic raid in wow. Think L'ura, Dimensius, Ansurek."

**Decisions taken** (`gpt/08-design-rules.md`, Aurelion exceptions):

| Topic | Decision |
|---|---|
| Who gets it | **Every level gets harder.** Easy differs only by longer warnings and lower damage, never by fewer mechanics. |
| Overlap | **Three or more damaging patterns may overlap.** Every route is still checked. |
| Enrage | **Enrage and scaling pressure are allowed:** a hard enrage, mechanics that grow every cast, a floor that fills. |
| Wipes | **Fight-ending wipes are allowed, even solo.** The telegraph must be impossible to miss, and the mechanic is introduced alone first. |
| Still settled | No one-shots. A mistake costs a heavy hit, so potions can save you. No iframes. No class requirement. The boss never heals or gets a new health bar, and stays targetable. |
| Fight shape | **Co-op first**, 5–7 min, 4 phases plus an intermission. |

## 1. What makes those three fights hard, and what Aurelion takes from each

| Reference | What makes it hard | Aurelion v4 takes |
|---|---|---|
| **L'ura (Midnight Falls), Mythic** | Memory game with the sequence split between groups. Wipe objects (Dawn Crystals). Dimension split where crossing kills and an empty side wipes. Constellations kill on the wrong spot. A long fight that "punishes tiny mistakes for a very long time" (Method: 200–300 pulls). | **Severance** (Day and Night halves), **Ecliptic Order** shared out as assignments, **Meridian Towers** (empty = wipe) |
| **Dimensius, Mythic** | Devour gets stronger every cast (Growing Hunger). Revive-unless-together adds. Stage 3 overlaps gravity wells, black holes, quakes and Devour. | **Solar Hunger:** Zenith Collapse grows every cast. Phase 4 overlaps everything. |
| **Queen Ansurek, Mythic** | Soaks staggered by 1 s or the raid wipes. Any avoidable hit lethal. Puddles fill the room. Phase 3 has the most overlaps. | **Sunspots:** staggered detonations whose scorch pools fill the floor; a chain reaction wipes. **Last Light** floor fill (enrage). |

Sources:
- [Method, Midnight Falls Mythic](https://www.method.gg/guides/march-on-queldanas/midnight-falls)
- [Icy Veins, Midnight Falls](https://www.icy-veins.com/wow/midnight-falls-raid-guide)
- [warcraft.wiki.gg, Dimensius tactics](https://warcraft.wiki.gg/wiki/Dimensius_(tactics))
- [Method, Queen Ansurek](https://method.gg/guides/nerubar-palace/queen-ansurek)
- [Mythic Trap, Ansurek](https://www.mythictrap.com/en/nerubar-palace/queen-ansurek/heroic)

The designs below are original adaptations, not copies.

## 2. Fight outline

| Phase | HP | Target length (solo, Medium) | Max patterns at once | Theme |
|---|---|---|---|---|
| 1 Perfect Order | 100–70 % | 90 s | 2 | Each new mechanic introduced alone, then paired |
| Intermission: Total Eclipse | at 70 % | 25 s | 3 | Light wells, a damage shield (wipe if not broken) |
| 2 Misalignment | 70–40 % | 90 s | 3 | Sunspots + towers + marks, conflicts |
| 3 Exposed Meridian (second form) | 40–15 % | 90 s | 3 | Severance: Day and Night |
| 4 Last Light | 15–0 % | ≤ 150 s | 4 | Everything; the floor fills; hard enrage |

**Hard enrage:** at **7:00** from the pull, or when Last Light's floor is full, whichever comes first, **Final Noon** wipes the party. It's announced at 6:30 with a gold countdown.

**Boss HP:** set so a party at **expected damage** reaches 0 % at about **6:00**. That gives 1 minute of slack for deaths, potions and repositioning.

**Co-op HP:** stays `1 + 0.7 × (N − 1)`. Co-op difficulty comes from jobs that scale with N, not from HP.

**Transitions:** as settled. They wait for the current block, are harmless, keep the same health bar and never heal.

## 3. New mechanics

### 3.1 Meridian Towers: fight-ending wipe, introduced alone in Phase 1

- **Look:** numbered brass columns of light (I, II…) with a loud chime and a big ivory numeral, cobalt rim. It must be impossible to miss.
- **Rule:**
  - Each tower needs ≥ 1 player inside at its resolve (≥ 2 players at N ≥ 6).
  - **Empty tower = Total Eclipse, the party wipes.**
  - Damage: 60 (Medium), split among the players inside.
  - One player taking it all survives from full (60 < 100; 78 on Hard).
- **Timing:** towers resolve in numeral order, 2.0 s apart, first at 4.0 s after they appear (Easy 5.0, then 2.5 apart).
- **Solo:** 2 towers, ≥ 9 m apart: stand in I, then run to II.
- **N = 2:** 2 towers. **N = 4:** 3. **N = 8:** 4 towers of 2.
- **Why it's hard:**
  - The route must also dodge whatever is paired with it.
  - In co-op, the assignment is who takes which tower.
  - A tower soaker can't also be the far-side SUN player.

### 3.2 Sunspots: staggered detonations, floor fill, chain reaction = wipe

- **Look:** a coral sun sigil grows on the marked player, with a numeral for its order.
- **Rule:**
  - Marked players (solo: you, twice) detonate in numeral order, **1.5 s apart**, at their own position.
  - Each detonation is a Ground circle of 3.5 m: a heavy hit (50) to anyone inside.
  - It leaves a **scorch pool** of 3.0 m that burns 20/s and lasts **until the end of the phase** (floor fill, enrage exception).
- **Chain reaction (wipe):** if a detonation catches a player whose own Sunspot hasn't gone off yet, it sets it off early and the party wipes ("Solar Flare").
- **Solo:** you carry two in a row: drop I, move ≥ 4 m, drop II.
- **Co-op:** N ≥ 2 → ⌈N/2⌉ marked per cast, numerals I.. in order.
- **Why it's hard:**
  - **placement:** the pools stay, so drop them at the rim, not on the paths you'll need for towers and Ecliptic discs
  - **staggering:** keep apart from other carriers until yours is gone
  - the floor shrinks every cast

### 3.3 Solar Hunger: scaling pressure on Zenith Collapse

- Every Zenith Collapse in the fight is hungrier than the last:
  - its safe disc shrinks 0.5 m per cast (solo 4.5 → 3.0 floor; co-op from the N table, never below 3.0 + 0.5 × ⌈N/2⌉)
  - its damage rises +5 per cast (50 → 70 cap)
- **Phase 4 only:** "Devour". After the Collapse resolves, he pulls everyone 2 m toward him over 1 s. A forced move (the rules allow it with a checked route), never a teleport.

### 3.4 Total Eclipse intermission: light wells and a damage check

At 70 %:
- He rises above the compass star, still targetable (settled rule).
- The dais goes dark except **4 light wells** (circles of 3.0 m), which orbit the centre at 6 m and 15°/s.
- **Umbra:** standing outside every well for more than 1.0 s straight deals 15 each second. A chip, so it can't kill quickly.
- **Corona Shield:** a shield worth **5 % of max HP** (800 solo at full HP: about 32 damage per second while moving between wells; a ranged class does about 60 standing still). The first draft had 18 %, which no party could break in time.
  - If it isn't broken in **25 s**, **Total Eclipse** wipes the party (damage check, enrage exception).
  - If it is broken, the intermission ends at once and Misalignment starts.
- **Also during it:** Meridian Lances (3 beams) every 6 s and Starfall Mirror. That's 3 patterns.
- **Solo:** the same, with the shield sized for solo expected damage.
- **N ≥ 5:** 6 wells of 2.5 m. Wells are never more than 2 per player, so spreading matters.

### 3.5 Severance: Day and Night, Phase 3

- **Look:** at the start of the second form, a gold meridian line splits the dais. One half is lit Day, the other is violet Night.
- **Rule:**
  - Your side when it lands is your side for 25 s.
  - **Crossing the line is a heavy hit** (50, and once more each time you cross back).
  - **An empty side at any moment ends the fight** (the half "falls", wipe).
  - Mechanics fire per side: Day gets Starfall Mirror, Night gets Ecliptic Order.
  - **The line rotates** 90° over the 25 s, so both halves sweep round the arena. You must keep moving round in front of it without crossing.
- **Solo:** you're assigned the side you stand in. The empty-side rule never applies solo; instead, at 12 s the sides **swap mechanics**.
- **Co-op:** the party must split. Severance ends with a **Reunion** tower at the centre that every player must stand in.

### 3.6 Last Light: Phase 4, floor fill and the hard enrage

- **The floor fills:** every 15 s a **shadow sector** (a 45° wedge from the centre to the rim, Ground) darkens permanently. It burns 25/s.
  - Sectors go round the clock in an authored order.
  - After 8 sectors the whole floor is gone = Final Noon wipe. That's 120 s after Phase 4 starts.
- **Rotation:** Lances (Cascading) + Starfall › Sunspots + Zenith Collapse (with Devour) › Towers + Zenith Marks › Ecliptic Order + Hour Hands (Hard) … on loop, up to 4 patterns live.
- **Why it's hard:** every mechanic now has less floor to solve on, and Sunspot pools from earlier phases are gone (they clear at each phase end). The sectors aren't.

## 4. Existing mechanics in v4

| Mechanic | v4 role |
|---|---|
| Meridian Lances | Paired filler; Cascading in P3–P4 |
| Starfall Mirror | Paired; the Day mechanic in Severance |
| Zenith Collapse | With Solar Hunger |
| Ecliptic Order | 4 anchors from P2. **Co-op:** numerals go **over players' heads** (I..N, Limit Cut). Each player's strike is a beam through them at their turn, so you must stand where your beam misses the others. The solo anchor version stays. |
| Zenith Marks | P2 and P4; two-step from P2 |
| Orrery Closing + Horizon | P2 only (Horizon + Sunspots = the floor shrinks from both sides) |
| Hour Hands | Hard named mechanic, P3–P4 |
| Noon Shadow | Cut |

## 5. Phase plans (Medium seconds; Easy stretches warnings ×1.25, never the gaps)

Each line is one block.

**Phase 1, Perfect Order (introductions, 2 patterns max):**
1. Lances (3) + Starfall
2. **Meridian Towers (alone)**
3. Ecliptic Order (3)
4. Zenith Collapse + Lances
5. **Sunspots (alone, pools)**
6. Zenith Marks + Lances
7. Punish 3.5 s

**Intermission:** Total Eclipse, ≤ 25 s.

**Phase 2, Misalignment (3 patterns):**
1. Sunspots + Starfall
2. Towers + Zenith Marks (conflict: your tower may be in the wrong zone; co-op swaps)
3. Ecliptic (4) + Lances
4. Zenith Collapse + Lances + Sunspot pools still live
5. Orrery + Horizon + Starfall
6. Punish 3.5 s

**Phase 3, Exposed Meridian (3 patterns):**
1. **Severance** (25 s, per-side mechanics, Reunion tower)
2. Cascading Lances + Starfall
3. Sunspots + Zenith Collapse
4. Towers + Ecliptic (co-op beams)
5. Hour Hands (Hard)
6. Punish 3.5 s

**Phase 4, Last Light (4 patterns, floor fill, enrage):** the rotation in §3.6 until he dies or the floor is full. No punish window here; it's the burn. That breaks the "a 3–4 s punish per boss" rule only for this phase; the boss still has punishes in Phases 1–3.

## 6. Damage and lethality

| Tier | Medium | Hard (×1.3) | Easy (×0.6) | Mechanics |
|---|---|---|---|---|
| Punish | 30 | 39 | 18 | Lances, Starfall, Hour Hands |
| Kill | 50 | 65 | 30 | everything else |
| Burns | 20/s or 25/s | ×1.3 | ×0.6 | Umbra, scorch pools, shadow sectors |
| Wipe | the party goes down | | | empty tower, Solar Flare chain, unbroken Corona Shield, empty Severance side, Final Noon |

With 300 effective HP, that's about 4–5 heavy mistakes per fight before wipes are even counted.

## 7. Tests (Milestone gates)

- **Perfect route survives** every block at every level, for N = 1, 2, 4 and 8, using `AllyBot` Perfect: assignment-follower for towers, Sunspots, sides and marks.
- **Every wipe fires when it should:** a test leaves a tower empty, chains a Sunspot, keeps the shield up and empties a side.
- **The memory-blind reflex bot goes down in Phase 1**, on every level except Easy.
- **Escape check** for every new shape and every 3–4-pattern overlap, at 4.2 and 6 m/s, with the loop stamina.
- **Fight length:** with `AllyBot.Attacks` on (expected damage), a solo ranged bot reaches 0 % in 5–7 min.
- **The enrage fires** at 7:00.

## 8. New systems needed (shared code is small and kept separate)

| System | How |
|---|---|
| Party wipe | `AurelionEncounter.Wipe(reason)` downs every player, with a big banner |
| Damage shield | A small `BossEncounter` hook (`virtual int Absorb(int)`) |
| Persistent pools | Existing `Hazard.BurnPerSecond` (Crucivar's slag), cleared at phase end |
| Per-player marks | Existing `Hazard.Affects` |
| Side tracking (Severance) | Per tick in the ability |
| Shared towers | `HitOnResolve = false`, damage split in OnResolve |
| Fight timer and enrage banner | HUD |

## 9. Open questions

1. Can Phase 4 drop the punish window (it's the burn), or keep a short one?
2. Severance solo: is "swap mechanics at 12 s" enough, or should solo also have a no-cross rule against a moving line (current design: yes, both)?
3. Easy: keep every wipe, or make Easy's wipes a heavy hit instead?
4. Should the 7:00 enrage scale with N, or stay fixed?
