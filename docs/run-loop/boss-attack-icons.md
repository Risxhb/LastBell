# Boss attack icons in the run loop

Codex made 20 boss attack icons, one for each v2 attack of the four main bosses. Chzy18 approved all 20 on 23 October 2026 (ability-icons commit `0d0555a`). This page explains how the run loop shows them and what each boss branch needs to do.

> **On the current player (`feature/confrontation-menu`, 26 October 2026):** this branch carries only **Aurelion's 5 icons**. The Crucivar and Sylvara icons stay local until those bosses ship, and normal Bellkeeper v2 is retired, so the catalog, the PNGs and `ApprovedAttackIds` hold just the Aurelion five. The per-boss notes below still describe the other bosses for when their icons come back.
>
>  Risx removed the boss panel's attack warning rows on 24 October (telegraphs on the floor carry the warning). So on this player a boss attack icon shows in the death recap, the attack tooltip and the Down card. It appears in the boss panel only on a row for a hazard tagged with a mechanic (see `mechanic-icons.md`).


## What the player sees

- **Death recap** (Results screen after a defeat): the killing blow is at the top, with its icon at HUD size (64 px), the attack name, the boss and the damage. Earlier hits are listed below it, newest first, each with a 22 px icon and "Attack · Boss". Hovering over any row opens the attack tooltip.
- **Attack tooltip:** the 64 px icon, then the attack name, the boss and its tag (Strike or Ground), the shape description from the icon manifest, and the damage the hit did.
- **Down panel:** "Struck by Attack · Boss", with the 64 px icon.
- **Boss panel warnings:** each live hazard that belongs to a catalogued attack shows a 22 px icon, then the attack name and the boss in brackets.

The attack name and the boss always appear next to the icon. Several icons look alike at 64 px (the half-disc group, the ring-around-a-disc group, the linked-disc group, and Two Choirs and Antiphon), so the icon alone never identifies the attack.

Attacks that aren't in the catalog, such as v1 moves and test hazards, still show as text, exactly as before.

## Where it lives (`unity/Assets/LastBell/Run/`)

| File | What it does |
|---|---|
| `Resources/Run/BossAttackIcons/catalog.json` | 20 entries: `id`, `boss`, `bossName`, `name`, `tag`, `shape`, `icon` (file name) and `aliases` (other hazard `Source` spellings). |
| `Resources/Run/BossAttackIcons/*.png` | The 256 px RGBA exports, byte-identical to `0d0555a`. Mipmaps are on, so they stay clean at 64 and 22 px. |
| `BossAttackIcons.cs` | The lookup: `Get(id)`, `Icon(id)` (Texture2D), `IconSprite(id)` (for uGUI), `Resolve(hazard, boss)` and `All`. |
| `Combat/Hazards.cs` | New optional field `Hazard.AttackId`. |
| `Combat/CombatWorld.cs` | `TakenAttackIds`, parallel to `TakenLog`, records the resolved attack ID for every hit. |
| `RunHud.cs` | `DeathRecap`, `AttackTooltip`, the Down panel and the boss panel warnings. `RunHud.PinRecapTooltip` shows the killing blow's tooltip without a mouse, for screenshots. |
| `RunAutoTest.BossAttackIcons.cs` | Checks that each of the 20 approved IDs resolves to a 256 px icon with a name and a boss. Also checks the Source fallback cases and the recap after the defeat run. |

## How a hit finds its attack

1. **`Hazard.AttackId`**, when the ability sets it. This is the contract: exact, and it doesn't depend on display text.
2. **Fallback: the hazard's `Source` text.** It matches the attack name or an alias as a prefix, ignoring case, spaces, punctuation and a leading "The", and only within the boss currently fighting. So `Starfall Mirror II (reflection)` resolves to `aurelion.starfall_mirror`, and Sylvara never matches a Bellkeeper attack.

The fallback already covers every v2 Source string on the boss branches today (the autotest lists the cases). It is a safety net. Each boss session should still set `AttackId`, so that renaming a hazard can't silently lose its icon.

## What each boss branch needs

None of this is urgent, and none of it changes gameplay. Do it when the boss branch next merges `feature/run-loop` (or `feature/boss-attack-icons`). Until then, everything works through the fallback.

**Common step (all four bosses).** After the merge, set the ID in the ability base class's spawn helper, so every hazard an ability makes carries it:

```csharp
// in <Boss>Ability.Spawn(Hazard h), before the hazard is spawned
h.AttackId ??= Id;   // full runtime ID, e.g. "aurelion.meridian_lances"
```

Sub-hazards that one attack spawns (rims, crowns, blasts, stamps, echoes) inherit the attack's ID, because the icon is per attack, not per hazard.

### Aurelion (`feature/aurelion-abilities-v2`; v3/v4 on `feature/aurelion-v3`)
- `AurelionAbility.Id` is already the full ID (`aurelion.meridian_lances`), so add `h.AttackId ??= Id;` in `AurelionAbility.Spawn`.
- `Hazards.cs` is edited on this branch too. If it conflicts, keep both sides: the new line is the `AttackId` field just below `Source`.
- v3 adds attacks that aren't among the 20 (Reunion, Umbra and the scorch hazards). Leave them without an ID for now; they show as text. Ask Codex for icons if they stay.

### Crucivar (`feature/crucivar-abilities-v2`)
- `CrucivarAbility.Id` is already the full ID, but several abilities call `W.Hazards.Spawn` directly (Bellows Draw, for example). Set `AttackId = Id` in each `new Hazard { … }` there, or route them through one helper.
- Today's names resolve through aliases: `Slag Rim`, `Slag Edge` and `Bellows Blast` go to Bellows Draw; `Pour`, `Pour II` and `Stamp N` go to The Great Casting; `Mould Press I–IV` resolve by name.
- Anvil Sweep and Slag Spit aren't among the 20. They show as text.
- `Hazards.cs` is edited on this branch too; keep both sides.

### Sylvara (`feature/sylvara-abilities-v2`)
- `SylvaraAbility.Id` is the short form (`verdant_canticle`), so use `h.AttackId ??= "sylvara." + Id;`. The natural place is `SylvaraEncounter.Tuned`/`Tune`, or each ability's spawn calls.
- Today's names resolve: `Verdant Canticle I–III`, `Two Choirs`, `Listening Roots: Hush/Chorus/root ring` and `Refrain Seed bloom/burst` (the older `RefrainSeed …` spelling has an alias).
- Antiphon's answer is spawned by Two Choirs' `ResolveVerse`, with `Source = "Two Choirs"`, so **its hits currently show as Two Choirs**. Setting `answer.AttackId = "sylvara.antiphon";` in `Antiphon.Step`, right after `verse.ResolveVerse(...)`, fixes that. It's the one real gap on any branch, and the fallback can't fix it.

### Bellkeeper (`feature/bellkeeper-abilities-v2`)
- `BellkeeperAbility.Id` is the full ID, so add `h.AttackId ??= Id;` in `BellkeeperAbility.Spawn`.
- Today's names resolve: `Leaning Bell`, `Striking Order`, `Reflection of the Hour`, `The Unfinished Hour`, and `Toll Tide` / `Heart Toll` (aliases for The Last Toll Tide).
- This branch merges cleanly with `feature/boss-attack-icons`. The in-game proof was run on that merge (see "Verified" below).

## Adding or changing an icon

Put the PNG (256 px RGBA) next to `catalog.json`, copy an existing icon's `.meta` with a new GUID (mipmaps on), and add an entry to `catalog.json`. If it's an approved attack, add its ID to `ApprovedAttackIds` in `RunAutoTest.BossAttackIcons.cs`. That list is written out by hand on purpose, so the catalog can't drift without a test failing.

## Verified (23 October 2026)

- **`feature/boss-attack-icons` (`bc553d1`, on run-loop `dc808fc`):** full autotest 248/248 with 0 errors. That's the 223 run-loop checks plus 25 new ones: the catalog count, each of the 20 IDs resolving to its 256 px icon, the sprite, 18 Source cases, the explicit-ID rule and the recap slots. In this build the run's defeat path fights the v1 Bellkeeper, so the recap correctly shows "Bell Sweep" as text.
- **`test/boss-attack-icons-bellkeeper` (`f762246`):** this branch plus `feature/bellkeeper-abilities-v2` (`9cb018a`), merged with no conflicts, plus one test-only step. A v2 Bellkeeper practice fight on the phase plan downs a standing bot in 4 real hits: Leaning Bell, then Striking Order three times. All 4 resolve to Bellkeeper icons. The recap, the pinned tooltip, the Down panel and the boss warning all show the icon with its name and boss. Full autotest: 503/503, 0 errors. The test-only step lives in `RunAutoTest.BossAttackIcons.Bellkeeper.cs`; it depends on the Bellkeeper v2 test harness, so it belongs on the Bellkeeper branch, not on run-loop.
- Screenshots are 1600×900, so the icons appear at their real HUD size. They're kept locally, outside the repo, in `boss-attack-icons-evidence/autotest-4/` (`50_…warning`, `51_…downed`, `52_…recap`, `53_…recap_tooltip`).
