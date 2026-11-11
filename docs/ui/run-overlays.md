# Run overlays on the UI kit (sub-thread B)

November 11, 2026. Branch `feature/ui-overlays`. Plan: `docs/plans/ui-uniformity-plan-20261110.md` (workspace docs), kit API:
[ui-kit.md](ui-kit.md).

Every in-run overlay now speaks the main menu's language: no boxes, a dim plus a soft gradient, the kit header, text
rows with the gold strip, and full keyboard / controller focus.

| Overlay | Where | Backdrop | Rows (main action first) | Back (Esc / B) |
|---|---|---|---|---|
| Pause | `RunHud.Overlays.cs` `Pause` | dim + centre band | **Resume**, Restart Run / Practice, Main Menu, (Test Setup) | resumes |
| Results | `Results` | deeper dim + centre band | **Retry Run / Practice**, Main Menu, (Test Setup) | — |
| LAN client Results | `LanResults` | deeper dim + centre band | Leave Session | — |
| Ready card | `ReadyCard` | soft ink strip (fades right), 4 px gold bar | **Ready** | — |
| Downed card | `DownedCard` (lower third) | soft ink strip, 4 px gold bar | none | — |
| Shop | `Shop/RunShopFlow.cs` | light dim + mirrored side gradient (left) | per card Buy / Lock, Temper, Chip in, Reroll, **Ready** | focus back to Ready |

- **Header:** `UiImgui.Header`, centred on Pause and Results (eyebrow = difficulty + "run", or "Practice"), left-aligned
  on the shop and the Ready card. Titles are Ivory and unspaced ("Paused", "Victory", "Defeat", "Intermission").
- **Focus:** one `ImguiFocus` per overlay. When an overlay appears, focus goes to its main action and that frame's
  Confirm / Back are dropped (`RunHud.FreshFrame`), so the key that opened it doesn't also act on it. The shop is a
  two-column grid (Buy | Lock per card, one line per build item and ally, Reroll | Ready); empty cells are skipped.
- **Old hotkeys:** Esc still toggles Pause in `RunDirector`, R / M still retry / leave on Results, R still rerolls.
  Enter no longer readies straight from `RunDirector` while the shop is open (it confirms the focused shop row; Ready is
  one of them). The shop is hidden while paused. **New:** the gamepad's Start toggles Pause (`RunHud.OverlayInput`; it
  resumes any time but never opens Pause on the menu, in a boss intro or over Results) and Y rerolls in the shop;
  `InputHints` shows both.
- **Hints:** row labels carry no key; each overlay has one 14 px hint line (`HintLine` in `RunHud.Overlays.cs`, built
  per device from `InputHints.Key`), e.g. "Enter  Select  ·  R  Retry  ·  M  Main Menu" / "A  Select".
- **Colours:** theme tokens only. The shop's rare tier is Gold (was a hard-coded blue); keystone GoldBright; common
  IvoryMuted.
- **Death recap:** 15 px rows (was 13), 32-34 px apart, side columns sized to fit 4:3.
- `RunHud.StripButton` is gone; every overlay button is `UiImgui.Row`.

## Checks

`-lastbellOverlayTest -lastbellShop [-lastbellOverlayPad] -lastbellOut <dir>` (`Run/OverlayCaptureTest.cs`): a
campaign run without travel through the real launch path; captures the Ready card, Pause, the final intermission shop,
the downed card, defeat Results with the death recap (and its pinned tooltip), the LAN client's Results and victory
Results. With `-lastbellOverlayPad` a virtual gamepad alone readies, works Pause (Start, B, wrap, A), buys and readies
in a shop, and retries from Results. Workspace script: `tools/combined-rebuild/build-ui-overlays.ps1`.
