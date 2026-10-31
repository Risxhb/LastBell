# 01 · Mould Press (`crucivar.mould_press`) — proposal

Part of Crucivar's ability set v2 (`ABILITIES.md`, same folder). Build only this ability.

## Fantasy

Crucivar walks to the central anvil, raises the RIGHT hammer and slams the die. Brass casting moulds rise from the die bays, each with:
- a gear-tooth rim,
- a large ivory numeral (the headcount),
- a loud chime that repeats each second.

When the count ends, the furnace channels pour into every mould. If a mould is empty, the pour overflows the whole floor.

**Look:**
- The mould ring is a coral/amber hatched circle with a crisp edge, and molten fill rising with the countdown.
- The numeral is friendly ivory (identity only). Players standing inside get a thin ivory tick on the numeral.

## Shape and numbers (Medium)

- **Shape:** circle, r **2.0 m**, Ground (Brace never helps).
- **Bays:** four fixed die bays at r **6.0 m** from the arena centre, on the diagonals (NE, SE, SW, NW).
  - Casts alternate pairs: NE+SW, then NW+SE, then NE+SW…
  - Solo uses two **adjacent** bays (the next pair's first bay).
- **Towers per player count N**, with each tower's numeral = how many players it needs:

  | N | Towers | Numerals |
  |---|---|---|
  | 1 | 2 in sequence (I, then II) | 1 each |
  | 2 | 2 at once | 1, 1 |
  | 3 | 2 | 2, 1 |
  | 4 | 2 | 2, 2 |
  | 5 | 4 | 2, 1, 1, 1 |
  | 6 | 4 | 2, 2, 1, 1 |
  | 7 | 4 | 2, 2, 2, 1 |
  | 8 | 4 | 2, 2, 2, 2 |

  Downed players don't count when the numerals are set at cast.
- **Damage:** **20 per share**. Each player inside takes total ÷ max(numeral, players inside), so it's never more than one share each when over-soaked. Missing players' shares fall on those inside (settled), so a "2" mould taken alone deals 40.
  - **An empty mould downs every standing player** (settled "empty soak wipes the party").
  - No knockback. Stun 0.25 s as usual.
- **Telegraph:** **4.2 s** warning (R&S stack median 4.35 s, §3.0). Bays are fixed, so it's locked from tick 0.
  - Solo tower II resolves **2.6 s** after tower I. Both are shown from the start, with II dimmer until I resolves.
  - Easy: ×1.25 (5.25 s, solo gap 3.25 s).
- **Punish after:** 2.0 s recovery (the hammer lifts out of the die).

## Escape route (from `escape-medium.md`)

| Case | Distance | 4.2 m/s walk | 4.2 + dash | 6 m/s | Window | Margin (4.2 + dash) |
|---|---|---|---|---|---|---|
| reach the nearer bay of the pair, from anywhere | 12.6 m | 3.00 s | 2.48 s | 2.10 s | 4.2 s | 1.72 s |
| bad split: the far bay of the pair, from the far edge | 17.4 m | 4.14 s | 3.62 s | 2.90 s | 4.2 s | 0.58 s |
| solo I → II, from the far side of I | 8.5 m | 2.02 s | 1.51 s | 1.41 s | 2.6 s | 1.09 s |

- **Melee:** bays are 6 m from the anvil (4.3 m from the body edge), so a melee player must leave the boss.
- **Ranged:** a player at the edge is at least 5.4 m from the nearest bay, so they must come in.
- **Standing still anywhere except in a mould is a failure:** solo is a wipe, and in a group your share lands on others (an empty mould wipes).

## Co-op and solo

- **Co-op rule:** fill each mould with its numeral.
- **Solo rule:** two moulds in sequence on adjacent bays. Step into I, then into II before it resolves 2.6 s later.
- N = 1 / 2 / 4 / 8 follows the table above.
- Damage doesn't scale with N; boss HP does (pending D6).

## Phase use

| Phase | How it's used |
|---|---|
| 1 | Alone. It's the soak's introduction, so the wipe rule is learned before any pairing. |
| 2 | With **Slag Rim** (Bellows Draw's rim part). The bays sit inside the 9.2 m safe disc (bay edge at 8.3 m). |
| 3 | Alone, plus **Second Press** on Hard (brief 05). |

## Easy / Hard

- **Easy:** warnings ×1.25.
- **Hard:** Second Press follows every mould (brief 05). Global ×1.3 damage taken.

## R&S source

- §4 block 2 (soak circle / stack).
- §5 proposal 2 (Harmony Tower), with Chzy18's §7 answers 3 and 14.
- R&S splits stacks by player count with `condPlayerCount`; the solo twin becomes a sequence (`_s` scripts).

## Escape-check plan

- **Worst case:** a bad split at 4 or 8 players sends a player from the far edge to the far bay of the pair (17.4 m in 4.2 s; it needs the dash at 4.2 m/s).
- **Autotest it** with a scripted player starting at the edge opposite its assigned bay.

## Build notes

- **Code:** `Run/Encounters/Crucivar/Abilities/MouldPress.cs` (one class). Spawn through `W.Hazards` like the existing attacks.
- **Shared soak:** needs a small shared hazard change, in its own commit with a note for the Abilities session:
  - shares, missing-share transfer and empty = wipe,
  - one hit per instance,
  - a numeral label on the telegraph.

  Suggested API: `Hazard.ShareTotal`, `Hazard.ShareCount`, `Hazard.WipeIfEmpty`.
- **Practice hook:** `-lastbellAbility crucivar.mould_press` fires it on loop in practice.
