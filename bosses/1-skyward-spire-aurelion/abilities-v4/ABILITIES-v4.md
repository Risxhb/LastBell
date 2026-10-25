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


> **This section matches the runtime** (`boss-aurelion`, branch `feature/aurelion-v3`, revision 038571a; aligned 2026-10-25 from the Codex solo study, proposal 3). Code is the source of truth; nothing was retuned to match older text. Everything below is still a **proposal** for Chzy18's approval. The older draft numbers (2 solo towers with shared damage, a 5 % shield, 15 % Last Light, the 7:00 enrage, a 120 s floor, a 25 s Severance with a solo swap) are retired.

## 2. Fight outline

| Phase | HP | Theme |
|---|---|---|
| 1 Perfect Order | 100–70 % | Paired from the pull; memory from the opener |
| Intermission: Total Eclipse | at 70 % | Moving light wells and the Corona Shield (wipe if not broken in 25 s) |
| 2 Misalignment | 70–40 % | Three patterns per block |
| 3 Exposed Meridian (second form) | 40–20 % | Severance: Day and Night |
| 4 Last Light | 20–0 % | **The soft enrage:** the floor falls and he hits harder every slice |

| Setting | Value |
|---|---|
| Boss HP | **10,000** solo. Co-op `1 + 0.7 × (fighters − 1)`; fighters are players, plus bot allies only when they attack (`-lastbellAllyAttack`). |
| Final Noon (hard enrage) | Wipe at **390 s (6:30)** from the pull, warned at 6:00 |
| Last Light floor deadline | **88 s** after Phase 4 starts |
| Gaps between blocks | 0.1 s |
| Punish windows | 3.5 s, at the end of Phases 1, 2 and 3 (none in Phase 4) |
| Transitions | As settled: wait for the block, harmless, same health bar, no heal |

## 3. Mechanics (runtime values; Medium damage, Hard ×1.3, Easy ×0.6)

### 3.1 Meridian Towers (`MeridianTowers.cs`): fight-ending wipe

- **Dots** on each tower say exactly how many players it needs. A tower **counts whoever is inside at the moment it lands**: no hold timer.
- Towers rise **5.0 s** before they land (Easy 6.0).
- **Enough inside:** no damage. **Too few:** Total Eclipse, the party wipes. **Extras** (beyond the count, farthest from the centre first) take a heavy hit (50).
- **Solo:** **three towers of one**, landing in order at 5.0, 7.5 and 10.0 s (Easy 6, 9, 12). Correct soaks deal no damage.
- **Co-op:** 2–3 towers that land together, their counts adding up to the party, assigned in party order when they rise.

| Party | Towers |
|---|---|
| 2 | 1 + 1 |
| 3 | 1 + 2 |
| 4 | 2 + 2, then 1 + 3 (alternating) |
| 5 | 2 + 3 |
| 6 | 3 + 3 |
| 7 | 2 + 2 + 3 |
| 8 | 3 + 3 + 2 |

- If a tower's assigned player goes down after it rises, its count drops by one; a tower whose players are all down goes dark.
- Layout: a 6.5 m ring round the centre, three authored layouts, towers ≥ 120° apart, radius 2.5 m.

### 3.2 Sunspots (`Sunspots.cs`): staggered blasts, pools, chain = wipe

- **Solo:** two sigils on you (I, II). **Co-op:** ⌈N/2⌉ carriers, rotating.
- Blasts land 1.5 s apart, the first 3.0 s after the cast. Each locks on its carrier 1.5 s before it lands.
- **Blast:** 3.5 m Ground circle, 50.
- **Pool:** 3.0 m, burns 20/s, lasts **75 s across phase changes**. Pools are cleared when Last Light starts.
- **Chain:** a blast catching a *different* carrier with an unlanded sigil is a **Solar Flare, a wipe**. Your own second sigil never chains.

### 3.3 Solar Hunger (Zenith Collapse)

- Each Collapse in the fight: the disc shrinks **0.5 m**, and the hit goes **50, 55, then 60** (cap; 78 on Hard).
- Floor: 3.0 m + 0.5 m × (⌈N/2⌉ − 1).
- Collapse + Lances uses 3 beams, landing 1.2 s after the Collapse.

### 3.4 Total Eclipse intermission (`TotalEclipse.cs`)

- **Corona Shield:** **700 per fighting player** (700 solo). If it isn't broken within **25 s**, the party wipes. Breaking it ends the intermission at once.
- **Shelter:** several **moving light wells** (no spherical shield model):

| Party | Wells |
|---|---|
| 1–4 players | 4 wells of 3.5 m |
| 5–8 players | 6 wells of 3.0 m |

  The wells orbit 6 m from the centre at 15°/s. **Umbra:** outside every well for over 2 s deals 10 per second.
- Lances (3 beams) and Starfall keep falling meanwhile.

### 3.5 Severance (`Severance.cs`), Phase 3

1. **3 s split.** The line shows and the game calls SPLIT. Co-op players are told Day and Night alternately in party order.
2. **20 s sweep.** The sides lock; the line travels **270°**: 135° one way in 10 s, then "THE LINE TURNS BACK" (1.5 s warning) and 135° back (about 3.3 m/s at the rim).
3. **Crossing** the line, or letting it sweep over you, is a heavy hit (50), every time.
4. **Common partner schedule** for every party size: Starfall, Lances (4 beams), Starfall, Lances.
5. **Co-op only:** all standing players on one side for 0.5 s = wipe, then the **Reunion** tower (4.5 m at the centre: empty = wipe, anyone outside takes 50).
6. **Solo:** no assigned side, no empty-side check, no Reunion and no swap branch. Your side is tracked and crossings still hit.

### 3.6 Last Light (`LastLight.cs`), Phase 4: the soft enrage

- Starts at **20 % HP**. A 45° slice burns for good (25/s) after a 3 s warning. Slices fall at 12, 24, 36, 48, then 58, 68, 78, **88 s**.
- Order: 0°, 180°, 90°, 270°, 45°, 225°, 135°, 315°.
- **Every fallen slice adds +10 % to all his attacks.** The 8th slice is Final Noon, a wipe.
- **Rotation:** Cascading Lances + Starfall › Zenith Marks + Lances › Hour Hands + Starfall, on loop.
- **Kept out of Phase 4:** Towers, Ecliptic Order, Zenith Collapse and Sunspots. Their spots would land on burned floor.
- Top-down masks: `masks/`.

## 4. Other mechanics in v4

| Mechanic | Runtime |
|---|---|
| Ecliptic Order | **4 rings from Phase 1** on a 5 m circle. Numerals show **2.0 s** (Easy: stay lit). Strike I lands at 3.2 s, then every 2.2 s, everywhere except the ring. Coral shows 0.35 s ahead (Easy 1.0). Ring 3.0 m (3.5 at N ≤ 4, 4.5 above). Damage 50. Paired Lances land 0.2 s before ring II. Co-op: no Starfall in this pairing. |
| Zenith Marks | **SHADE** ≤ 5.5 m from him, **SUN** ≥ 8.5 m. Shown 3.0 s ahead (Easy 4.0). Solo alternates, SUN first. Two-step in the last block of Phases 1 and 3. With towers it lands 1.5 s after the last tower. Damage 50. |
| Meridian Lances | 30 (punish tier); Cascading in Phase 3 and Phase 4 |
| Starfall Mirror | 30; v4 always uses the full 1.1 s window. In tower pairings it lands after the towers. |
| Orrery Closing + Horizon | Phase 2, with Starfall and Hour Hands |
| Hour Hands | **Every level** from Phase 2 (was Hard only) |
| Noon Shadow | Cut |

## 5. Phase plans (runtime)

**Phase 1:**
1. Ecliptic + Lances
2. Towers + Starfall
3. Sunspots + Starfall
4. Collapse + Lances
5. Towers + Marks
6. two-step Marks + Lances
7. punish

**Intermission:** Total Eclipse.

**Phase 2:**
1. Sunspots + Starfall + Lances
2. Towers + Marks + Starfall
3. Ecliptic + Lances (+ Starfall solo)
4. Collapse + Lances
5. Orrery + Horizon + Starfall + Hour Hands
6. punish

**Phase 3:**
1. Severance (split, sweep, Reunion in co-op)
2. Cascading Lances + Starfall + Hour Hands
3. Sunspots + Collapse (2.5 s after the last blast)
4. Towers + Lances + Starfall
5. two-step Marks + Lances
6. punish

**Phase 4:** the Last Light rotation, with no punish.

## 6. Damage and lethality

| Tier | Medium | Hard | Easy | Mechanics |
|---|---|---|---|---|
| Punish | 30 | 39 | 18 | Lances, Starfall, Hour Hands |
| Kill | 50 | 65 | 30 | Ecliptic, Marks, Sunspots, Severance crossing, tower extras, Orrery; Collapse 50–60 |
| Burns | 20 or 25/s; Umbra 10/s | ×1.3 | ×0.6 | pools, slices, Umbra |
| Last Light | +10 % per fallen slice, on all of the above | | | |
| Wipe | the party goes down | | | a tower short, Solar Flare, unbroken Corona, empty Severance side (co-op), empty Reunion, the 8th slice, 390 s |

## 7. Evidence

- **Unity autotest v4:** 28/28, covering every mechanic and wipe, the enrage, the 70 % flow, the solo perfect route through Phases 1–3 on Medium and Hard, and the memory-blind bot dying.
- **Unity demo:** four perfect attacking bots win at full HP on Hard in 4:55, 3 of 4 standing, with one slice of floor to spare.
- **Codex headless solo study** (2026-10-25, `deliveries/.../aurelion-solo-20261025/REPORT.md`):
  - Arcanist and Chimesinger clear solo on Easy, Medium and Hard: 72/72 with a guarded input policy.
  - Vanguard: **not yet demonstrated**, 0/180 across five bot policies. That's not proof of impossibility.
  - No unavoidable two-body requirement in the solo branches.

## 8. New systems (shared code kept small)

| System | Where |
|---|---|
| Party wipe | `PlayerController.ForceDown`, `AurelionEncounter.Wipe` |
| Damage shield | `BossEncounter.Absorb` |
| Per-player marks | `Hazard.Affects` |
| Stand-in rings | `Hazard.Soak` |
| Enrage clock, banner, captions | `RunHud` |
| Persistent pools | `Hazard.BurnPerSecond` |

## 9. Open questions

1. **Vanguard solo reference:** which successful input route is the reference per level? In progress in the Aurelion session (Codex proposal 1). Don't infer a melee damage ceiling from failed bot policies.
2. Are the measured solo clear times the desired experience, given co-op first?
3. Should Phase 4 keep no punish window?
4. Easy: keep every wipe, or turn Easy's wipes into heavy hits?
