# Mechanic icons in the run loop

Codex made 28 mechanic icons: 12 friendly co-op markers and 16 hostile mechanic icons. Chzy18 approved both sets on 24 October 2026 (ability-icons commit `54f7d22` on `art/mechanic-icons-20261022`, approval record `docs/mechanic-icons/APPROVAL.md`). This page explains how the run loop loads them and where they show.

**Status of the placement: approved.** Chzy18 approved both placements below on 24 October 2026 ("Approve both placements: mechanic icons in the boss panel warnings and the order/team/mark icons over players' heads."). No encounter sets a mechanic yet, so in normal play nothing new shows until a boss tags its mechanics.

## The 28 IDs

| Set | IDs |
|---|---|
| Friendly (identity only, never hurt) | `order_i` … `order_viii`, `team_brass`, `team_cobalt`, `carried_mark`, `tether_partner` |
| Hostile | `spread`, `soak_tower`, `line_stack`, `stretch_tether`, `pass_the_mark`, `movement_stop`, `movement_move`, `one_side_cleave`, `player_mirror`, `pull`, `shrinking_edge`, `cone`, `ring`, `lane`, `delayed_circle`, `low_bar` |

The IDs are the manifest IDs. Code names them only through the constants in `MechanicIcons` (`MechanicIcons.Spread`, `MechanicIcons.Order(2)` and so on). Nothing is looked up by file name.

**The name is always drawn next to the icon.** Three pairs look alike at 64 px: brass team vs ring, tether partner vs stretch tether, and carried mark vs pass the mark. So the icon alone never identifies the mechanic.

## Where it lives (`unity/Assets/LastBell/Run/`)

| File | What it does |
|---|---|
| `Resources/Run/MechanicIcons/catalog.json` | 28 entries: `id`, `set` (friendly or hostile), `name`, `meaning` (one line, for tooltips later) and `icon` (file name). |
| `Resources/Run/MechanicIcons/*.png` | The 256 px RGBA exports, byte-identical to the export hashes in both manifests. Mipmaps are on, so they stay clean at 64, 40 and 22 px. |
| `MechanicIcons.cs` | The lookup: ID constants, `Order(n)`, `Get(id)`, `Icon(id)` (Texture2D), `IconSprite(id)` (for uGUI) and `All`. |
| `Combat/Hazards.cs` | New optional field `Hazard.MechanicId`. |
| `Combat/PlayerController.cs` | New list `PlayerController.Marks` (mechanic IDs over that player). It's cleared in `PrepareForEncounter`, so marks never carry into the next fight. |
| `RunHud.cs` | The boss panel warning, the overhead marks (`PlayerMarks`) and a test-only gallery (`ShowMechanicGallery`). |
| `RunAutoTest.MechanicIcons.cs` | The checks listed under "Verified". |

## Where they appear (approved 24 October 2026)

1. **Boss panel warnings (hostile).** When a live hazard has `MechanicId`, the boss panel shows a row for it: the attack's own 22 px icon and name when it's a catalogued boss attack, then the mechanic's 22 px icon and name, for example "ATTACK NAME (BOSS) [icon] SPREAD".
   - **Note:** Risx removed the boss panel's attack warning rows on 24 October 2026 ("telegraphs on the floor carry the warning"). That clashes with this placement, which Chzy18 approved the same day. On the current player, rows come back **only for hazards tagged with a mechanic**. Every other hazard stays floor-only, so the panel looks exactly like Risx's until an encounter tags a mechanic.
2. **Over players' heads (friendly, and hostile target marks).** Each ID in `PlayerController.Marks` draws as a 40 px icon above that player, side by side, with its name on a dark strip under it. This is where order numerals, team rims, the carried mark and the tether partner go, matching the marker rule in §5 ("ivory numerals over heads for order", "brass and cobalt rims for two teams"). A hostile mark that targets a player (spread, pass the mark) can go in the same list.

Nothing assigns these yet. A boss that uses a mechanic sets them:

```csharp
// a hazard that is a spread circle on a player
W.Hazards.Spawn(new Hazard { Source = Name, AttackId = Id, MechanicId = MechanicIcons.Spread, ... });
// order numerals for a sequence, team rims for two groups
for (int i = 0; i < order.Count; i++) order[i].Marks.Add(MechanicIcons.Order(i + 1));
player.Marks.Add(MechanicIcons.TeamCobalt);
// remove a mark when it's no longer true
player.Marks.Remove(MechanicIcons.CarriedMark);
```

### Open questions for Chzy18

- Later candidates beyond the two approved places: the death recap (mechanic beside the killing blow), a tooltip on the warning row using `meaning`, or the ground telegraph itself.
- Overhead size: 40 px with the name under it. Is that readable at the game camera, and should the names hide after the first few seconds once players know the icons?
- Should a hostile target mark (spread on you) sit in the same overhead row as friendly identity marks, or in its own row?
- The `meaning` lines in `catalog.json` are a first draft written for this integration, not from a design doc. Please correct any that are wrong.

## Adding or changing an icon

Put the PNG (256 px RGBA) next to `catalog.json`, copy an existing icon's `.meta` with a new GUID (mipmaps on) and add an entry to `catalog.json`. Add a constant to `MechanicIcons.cs`. If it's an approved mechanic, add its ID to `ApprovedFriendlyMechanicIds` or `ApprovedHostileMechanicIds` in `RunAutoTest.MechanicIcons.cs`. Those lists are written out by hand on purpose, so the catalog or the constants can't drift without a test failing.

## Verified

**24 October 2026, `feature/mechanic-icons` (on `feature/boss-attack-icons` `ca1dd3f`):** full autotest 256/256 with 0 errors. That's the 248 checks already on the boss attack icon branch plus 8 new ones:
- the catalog lists exactly the 28 approved IDs;
- all 28 icons load at 256 × 256 with mipmaps, a name and the right set (friendly or hostile);
- the ID constants in code match the 28 approved IDs;
- `Order(1..8)` maps to `order_i`…`order_viii`;
- every mechanic has its own icon file;
- the sprite helper works;
- in a live practice fight, the player's marks and two tagged test hazards are up for the screenshot;
- `PrepareForEncounter` clears the marks.

Screenshots are 1600×900, so the icons appear at their real size. They're kept locally, outside the repo, in `mechanic-icons-evidence/autotest-2/`:
- `24_mechanic_icons_marks_and_warnings.png`: "Order II" and "Cobalt team" over the Vanguard's head, and two warnings in the boss panel tagged Spread and Soak tower. The test hazards are harmless and are named "Test hazard".
- `25_mechanic_icons_gallery.png`: all 28 icons at 64 px with their names.
