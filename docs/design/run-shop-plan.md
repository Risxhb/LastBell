# Run shop: build plan

**Status:** build plan for an approved system. Chzy18 accepted the shop spec (`docs/proposals/meta-shop-spec-2026-10-29.md`) and all 8 of its §12 answers on 2026-10-28. This file turns spec §11 into build steps. Anything below marked **proposal** is a choice the spec doesn't cover. Those choices go to the review inbox, and the build uses the default given here until Chzy18 answers.

Lane: run loop and shop. Branch `feature/run-shop`, cut from `azure/feature/confrontation-menu` at `4f5c342`. Written 2026-10-28.

Sources: `AGENTS.md`, `gpt/08-design-rules.md` (§1 shop, §2 no iframes, §3 potions, §4 classes, §0.3 approval), the shop spec, `meta-loot-and-toll-2026-10-29.md` §1.5–1.9 (item pool and dropped items), `meta-picks-vs-shop-2026-10-29.md`, and `session-handoffs/meta-game-ideas.md`.

---

## 0. What the build looks like today

The plan has to fit what `feature/confrontation-menu` actually does:

- **Run order:** Aurelion, Sylvara and Crucivar are shuffled, then the Bellkeeper comes last (`RunDirector.CreateRunOrder`). **No minibosses are in the run yet.** They're practice-only entries.
- **One local player.** `CombatWorld.Players` is a list, but nothing adds a second player on this branch: no party bots and no networking.
- **States:** `Menu → Ready → [Intro → Combat → Cleared]… → Results`. Ready, the preparation card, only appears before the first boss. A retry starts a fresh run.
- **Health policy:** health refills before every boss after the first. Potions refill only before the last boss.

So every run encounter today is a main boss, and every gap between fights is an intermission. That gives the four shops the spec asks for: one before each area boss and the final shop before the Bellkeeper.

## 1. The Chimes economy (step 1)

| Rule | Build |
|---|---|
| Area boss clear: 150 each | Paid on `Cleared` in Run mode, once per fight |
| Miniboss clear: 100 each | Paid the same way once minibosses join the run (`BossEntry` gets a miniboss flag) |
| +20 nobody went down, +20 under par | Party bonuses, paid to everyone |
| No bonus for not using potions, no extra for retries | Not implemented, on purpose |
| Solo earns the same | Same code path |
| Gone at run end | The wallet resets on `Results` and on every fresh run |

- **`RunWallet`**: one per player slot, holding `Chimes`, `Earned` and `ReceivedThisVisit`.
- **Stand-in miniboss pay (accepted by Chzy18, 2026-10-28).** The run has no minibosses yet, so Shop 1 would open with 0 Chimes. Each area shop also pays the area's three missing miniboss clears (3 × 100 = 300), shown on the screen as "Area minibosses (stand-in)". It switches off for an area once that area's minibosses are in the run. This keeps the spec's ~1,350–1,830 Chimes per run.
- **Par times (accepted by Chzy18, 2026-10-28):** area bosses 4:00, the Bellkeeper 6:00, minibosses 2:30 (the spec's §5 lengths), kept in one table so playtests can tune them.

## 2. The shop screen at the intermission (step 2)

Flow, using the existing states and adding no new `RunState`:

1. A boss is cleared. Chimes are paid and the clear card plays as before.
2. Instead of going straight to the next Intro, the director goes to **Ready** for the next boss. Health is refreshed then, so the shop shows the refilled bar. Potions follow the existing rule.
3. While Ready is showing in a run, the shop screen replaces the preparation card. Shop 1 is the Ready before the first boss.
4. **Ready** (button or Enter) closes the shop and starts the Intro. Solo has no timer, because the shop waits for you (spec §6).

Screen contents, per spec §11:
- 6 cards: 3 class, 2 general and 1 Rare-or-Keystone slot. Each card shows name, tier, effect, price, **Buy** and **Lock**.
- **Owned items** with **Temper** (110, once per item).
- **Reroll**: 30, then +15 each time in the same visit. It rerolls every unlocked slot. One lock per visit, and the lock is free.
- The **allies panel** shows each ally's class, Chimes and items, with **Chip in** buttons. It's hidden in solo.
- A Chimes counter and the visit number ("Shop 2 of 4 · next: Crucivar").

Stock rules:
- Per-player stock, seeded from run seed + player slot + visit + reroll count. The `-lastbellSeed N` argument makes it reproducible.
- No Keystones in Shop 1. The final shop's special slot is always Rare or Keystone. Each player can own at most 2 Keystones per run.
- Party-gated items appear only when that class is in the party. Items with no solo value never appear solo.
- No item is offered twice to the same player, and owned items leave the pool.
- No selling (rejected, §12.7).

**Practice:** one-area practice gets a single shop with that area's 300 stand-in Chimes in step 5. Until then practice is unchanged, so the boss lanes' practice tests keep their auto-start.

## 3. The item framework and class hooks (step 3)

**`ItemDef`** (data, `Run/Shop/ItemCatalog.cs`):
- `Id`, `Name`, `Class` ("Vanguard" / "Chimesinger" / "Arcanist" / null for general), `Tier` (Common/Rare/Keystone), and `Price` from the tier;
- `Tags` (Crack, Ring, Glyph, Party);
- `Budget` (+5 / +8 / +12);
- `Value` and `Tempered` (the + version is +50%), with a null `Tempered` meaning it can't be tempered;
- `OfferedSolo`, `SoloNote` (the solo fallback text), `Effect` (card text).

**Run state** (`Run/Shop/RunShop.cs`): `ShopRun` holds a `PlayerBuild` per slot (owned items, tempered flags, Keystone count, wallet), the seed, the visit number, and each slot's `ShopStock` for the current visit.

**Hooks in shared code.** These are kept small so Risx can review them line by line:

| File | Hook | Why |
|---|---|---|
| `CombatWorld.DamageBoss` | `ItemHooks.Outgoing(...)` before the hit, `ItemHooks.Dealt(...)` after | every damage item (keyed by the kit's ability name) |
| `PlayerController` | `Build` field; potion heal, potions per refill, sprint drain and regen delay read item stats | general items |
| `VanguardKit2` | Last Word cooldown, `OnOpening`, Sunder empower check | Countdown, Oathbound |
| `ChimesingerKit` | Mending Chorus cooldown, each ally touched by a Chorus | Swift Chorus, Chorus of Valour |
| `ArcanistKit` | Surge seconds per glyph, glyph cap | Long Surge, Perpetual Beam |
| `RunDirector` | add the shop component; at a clear in a run, go to Ready instead of straight to Intro | the shop flow |
| `RunHud.ReadyCard` | hidden while the shop shows | the shop replaces the card |

All item math stays in `Run/Shop/`. A kit only asks "what's my cooldown / cap / bonus?" and gets the kit's own number back when there are no items.

**First slice: 15 items** (spec step 1 asks for 10; this covers each class and the general pool):

| Item | Class | Tier | Effect (+ = tempered) |
|---|---|---|---|
| Tempered Edge | Vanguard | C | Steel Rhythm hit 3 +20% (+30%) |
| Countdown | Vanguard | C | Last Word cooldown −20% (−30%) |
| Cracked Armour | Vanguard | R · Crack | Sundering Blow cracks the boss 4 s; heavy hits on it +15% (+22%). Solo: your Last Word |
| Oathbound | Vanguard | K | Sundering Blow can't be empowered; every 3rd Opening (2nd) resets Last Word |
| Heavy Bell | Chimesinger | C | Bellstrike +20% (+30%) |
| Swift Chorus | Chimesinger | C | Mending Chorus cooldown −15% (−22%) |
| Chorus of Valour | Chimesinger | R · Party | Allies your Chorus touches get +12% (+18%) damage for 4 s. Solo: you |
| Unbound Cast | Arcanist | C | Sigil and Comet without a glyph deal 50% (57%) instead of 35% |
| Long Surge | Arcanist | R | Surge 2.5 s (2.75 s) per glyph instead of 2 s |
| Perpetual Beam | Arcanist | K | Glyph cap 2; Sigil and Comet without a glyph deal 70% (87%) |
| Strong Brew | general | C | Potions heal 48 (52) instead of 40 |
| Sprinter | general | C | Sprint drains 21/s (19/s) instead of 25/s |
| Deep Breath | general | C | Stamina regen starts after 0.6 s (0.53 s) instead of 0.75 s |
| Lone Bell | general | R | +15% (+22%) damage while no ally is within 8 m |
| Spare Flask | general | R | +1 potion at each refill (no Temper) |

**Rule checks for every item:**
- No dash, dodge or Blink invincibility, and no post-hit immunity.
- No revive, no lifesteal, and nothing refills potions mid-fight. Spare Flask only raises the refill count, and the carry-over rule stays as it is.
- Every item works solo with every class and none is required. Party items carry their solo fallback.
- Defensive caps: max HP items (later) stay ≤ +20%, knockback reduction ≤ −30%, and Brace stays unchanged.
- No item visual uses coral, amber or Arcanist violet on a boss warning. Slice 1 adds no world VFX at all: card text and floating numbers only.

**Later items** (step 6, the full pool) need two more hooks: a max HP stat on `PlayerController` (Grit, Glass Edge) and per-ability range/arc numbers in each kit. Those come as their own reviewed hook commits.

## 4. Co-op sync (step 4)

The branch has no networking. Even so, the shop is built host-authoritative from the start, so co-op slots in without a rewrite:
- Every change goes through `ShopRun.TryBuy / TryTemper / TryReroll / TryLock / TryGift / SetReady (slot, …)`. Each call checks the slot's own wallet, stock, Keystone cap and the 150-per-visit receive cap. The UI never edits state directly.
- A request names the requesting slot. When netcode lands, clients send these requests and only the host runs them (spec §5.5), then sends back the new state.
- Ready check: the shop closes when every slot is ready, or after 90 s in co-op. Solo has no timer.
- Disconnect and rejoin: slots are kept by player index for the run (spec §5.6). This hooks into netcode later.
- Party gating reads the party's classes. Bots count once party bots land on this branch.

Until netcode lands, the autotest adds **test slots** (wallet and build only, no body) to check chip-in, the receive cap and party gating.

## 5. Tests and the bot path (step 5)

**`RunShopAutoTest`** (`-lastbellShopTest`), per spec §11:
- buy one item of each tier; check the Keystone cap (2) and that Shop 1 has no Keystone;
- temper (once only), reroll price steps (30 → 45 → 60), lock survives a reroll, one lock per visit;
- refusals: can't afford, already owned, not in stock;
- gift with a test slot, the 150 receive cap, no gift in solo;
- ready closes the shop and the boss starts;
- the build carries into the boss fight: each class's item measurably changes damage, cooldown, glyph cap or potion heal through the real kit;
- Chimes are paid on a clear (150 + bonuses + stand-in), and the next shop opens before the next boss;
- everything is cleared at the end of the run (results and a fresh run);
- seeded stock repeats with the same seed.

**Shop bot** for the playtest agent:
- `-lastbellShop` turns the shop on in automated runs. It's off by default there, so existing suites behave exactly as before.
- `-lastbellShopBot` adds a greedy buyer: Keystone if allowed, then Rare, then Common for its class, then general, then Temper, one reroll if nothing is affordable, then Ready.
- The bot logs each purchase to the run log, so playtest reports can say which build cleared.

## 6. Order of work

1. ~~Branch and plan~~ (this file).
2. Chimes economy + `ShopRun` model + host checks.
3. Shop screen (IMGUI, matches `UiTheme`) + director flow.
4. Item framework + the 15 slice items + kit hooks.
5. Autotest + shop bot. Build and test under the Unity lock.
6. **First playable slice → Azure PR** into `feature/confrontation-menu`.
7. Practice shop, the full 70-item pool (with the max HP and range hooks), co-op once netcode or party bots land, then the Standard Kit for Ultimate practice and Toll (spec §11 order).

## 7. Open questions

None open. Chzy18 accepted both review-inbox items on 2026-10-28: 300 stand-in Chimes per area shop until that area's minibosses are in the run, and par times of 4:00 / 6:00 / 2:30 in one table to tune.
