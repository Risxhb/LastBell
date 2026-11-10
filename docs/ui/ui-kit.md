# UI kit (Phase 1) — API note for the screen sub-threads

November 10, 2026. Branch `feature/ui-kit`. Plan: `docs/plans/ui-uniformity-plan-20261110.md` (workspace docs).
Goal: every screen speaks the Confrontation main menu's language. Build screens from these pieces; don't copy colours,
gradients or focus code into screen files.

## The language in one paragraph

No boxes over the game. A soft dark gradient (side or centre band) behind the controls. Header = spaced Gold eyebrow,
Ivory title, 96×2 Gold rule (left-aligned on full screens, centred on centre-band overlays). Buttons are **text rows
only**: RestLabel at rest; on focus or hover GoldBright over the gold highlight strip. The main action is larger text
(28 px vs 24), never a gold box. Choosers use drawn chevrons. Everything works with keyboard, mouse and controller,
with wrap-around navigation; Esc / B goes back.

## Tokens and textures — `UI/UiTheme.cs`

| Token | Value | Use |
|---|---|---|
| `RestLabel` | (.93,.88,.76,.74) | text row at rest |
| `RestLabelMuted` | (.93,.88,.76,.62) | card / tab label at rest |
| `RuleFaint` | (.93,.88,.76,.16) | footer rules, card and tab edges |
| `PrimaryInk` | (.07,.05,.02) | dark text on gold (legacy boxed buttons only) |
| `DisabledFill`, `DisabledLabel` | (.22,.2,.17,.9), (.6,.57,.5) | disabled gold fill and its label (legacy) |
| `Backdrop` | (.012,.014,.026) | opaque navy behind full-screen menus |
| `GradientInk` | (.008,.008,.022) | the side gradient's ink |
| `BandInk` | (.01,.012,.025) | the centre band's ink |
| `StripFill`, `StripEdge` | Gold .26, Gold .55 | the highlight strip and its 1 px edges |
| `RuleWidth`, `RuleHeight` | 96, 2 | the title rule |

Shared textures (built once, never destroy them; tint with `GUI.color` / `Graphic.color`):

- `UiTheme.StripTexture` — 128×1 white, fades over the first 22 % and last 32 %.
- `UiTheme.SideGradient` — 128×1 GradientInk, clear at u=0, ~0.985 from u≈0.43. Mirror it for a left column.
- `UiTheme.CentreBand` — 1×64 BandInk, 0.78·sin² top to bottom.
- `UiTheme.Spaced(s)` — the only letter-spacing helper (`"S E L E C T   C L A S S"`). The copy in TestRushSetup is gone.

## uGUI (menus) — `Menu/MenuUI.cs`

| Builder | What it makes |
|---|---|
| `MenuUI.Row(label, parent, min, max, action, size = 24, inset = RowInset)` | the text row. Label is the first `Text` child (tests read it). Use 28 for the screen's main action. |
| `MenuUI.Strip(parent)` | a highlight strip (hidden until `NavFocus` shows it) for custom controls (sliders, icon buttons). |
| `MenuUI.Chooser(parent, value, min, max, size, onPrev, onNext)` → `Chooser` | value label between two drawn chevron buttons; call `chooser.Place()` every frame so they hug the text. |
| `MenuUI.ChevronButton(parent, next, action)` / `MenuUI.Chevron(parent, next, size, color)` | one chevron button (Ivory → GoldBright) / a non-interactive chevron cue. |
| `MenuUI.Header(parent, eyebrow, title, min, max, titleSize = 52, alignment)` → `Header` | eyebrow (auto-spaced) + title + rule laid out in Settings' proportions. The long overload takes exact rects (the main menu and Boss Rush use it). `header.SetActive(bool)`. |
| `MenuUI.SideGradient(parent, min, max)` | the main menu's right contrast gradient. |
| `MenuUI.LinkColumn(items, left, right)` / `MenuUI.LinkGrid(rows…)` | explicit wrap-around column / proportional grid navigation. |
| `MenuUI.HalfWidth(text)` | rendered half-width of a label (best-fit aware), for placing cues beside text. |

**`NavFocus`** is the one focus component (replaces `ConfrontationNavFocus` and `TestRushFocus`, both deleted):

- `Mode = Row` (default): `Label`, `Icon`, `Strip`, `Rest`, `IconRest`. Focus/hover → strip on, label/icon GoldBright; disabled → label at 35 %.
- `Mode = Card`: `Frame`, `Art`, `CardLabel`, `Accent`, `Chosen`, `Grow`, `ChosenEdge`, `FocusEdge`. Chosen → GoldBright frame + accent + Gold label; focus → GoldDim edge, brighter art, Ivory label, slight grow.
- `Mode = Tab`: same states as Card; set `Frame` to the underline and `Accent` to the diamond.

`MenuFocus` (the boxed house button from `MenuUI.Button`) still exists for the LAN lobby, the Showcase menu and Hero
Selection. Sub-threads A and E retire it as those screens move to rows; don't use it on new screens.

`MenuUI.Ink` now equals `UiTheme.Ink` (was a third copy with .97 alpha; only Hero Selection's header used the default).

## IMGUI (in-run screens) — `UI/UiImgui.cs`

All rects are virtual 1080p units (as elsewhere in RunHud). Styles: `UiImgui.Styles` (Eyebrow, EyebrowCentre, Title,
TitleCentre, Row, RowLeft, Primary, PrimaryLeft, Body, BodyCentre, Hint) are cached per screen height; don't mutate them
and don't build `GUIStyle`s every frame.

| Call | What it draws |
|---|---|
| `UiImgui.Row(focus, rect, label, style = Styles.Row, enabled = true)` | a text row in a focus list. True on click, or when focused and Confirm pressed. |
| `UiImgui.Row(rect, label, style, rest = RestLabel, enabled = true)` | hover-only row (no keyboard focus). `RunHud.StripButton` now calls this (keeping its own Ivory .78 rest colour). |
| `UiImgui.Strip(screenRect)` | the highlight strip alone. |
| `UiImgui.Header(rect, eyebrow, title, centred)` → y below the rule | eyebrow + title + 96×2 rule. |
| `UiImgui.Band(rect, alpha)` | the centre band. `RunHud.DrawBand` now calls this. |
| `UiImgui.SideGradient(rect, right = true, alpha)` | the main menu's side gradient (mirrored when `right` is false). |
| `UiImgui.InfoStrip(rect, fill = InkDeep)` | ink strip + 3 px Gold left bar for Ready, tutorial prompts and tooltips. |
| `UiImgui.Rule(x, y)` | the 96×2 rule. |

**`ImguiFocus`** — keyboard/controller focus for a list of `UiImgui.Row`s:

```csharp
readonly ImguiFocus pauseFocus = new ImguiFocus();          // a field: focus survives across frames
void OnGUI()
{
    pauseFocus.Begin();                                     // top of every OnGUI pass, before the rows
    if (UiImgui.Row(pauseFocus, r0, InputHints.Label("Resume", HintAction.Resume), UiImgui.Styles.Primary)) Resume();
    if (UiImgui.Row(pauseFocus, r1, "Settings")) OpenSettings();
    if (UiImgui.Row(pauseFocus, r2, "Main Menu")) ToMenu();
    if (pauseFocus.Cancelled) Resume();                     // Esc / B: true once per press (first pass of the frame)
}
```

- Rows get ids 0, 1, 2… in draw order; draw them in the same order every pass. Disabled rows are skipped.
- `Wrap` (default true), `Columns` (> 1 = grid: left/right step one, up/down a row), `Active` (false = ignore input while
  another layer owns it), `Reset(i)` to put focus on the main action when a screen opens.
- Moving the mouse over a row takes focus; clicks still work.
- Input comes from `MenuInput.Poll()`: the same bindings as the uGUI menus (arrows, dpad, left stick with 0.3 dead zone;
  Enter / numpad Enter / A; Esc / B), one step per press, repeating after 0.5 s every 0.1 s. Polled once per frame however
  many OnGUI passes run. Autotests can restrict it with `MenuInput.PollDevices = new InputDevice[] { kb, pad }`.
- **Watch for double handling:** RunDirector already toggles pause on Esc and readies on Enter, and RunHud / RunShopFlow
  read R and M directly. When a screen moves to `ImguiFocus`, remove or guard the old hotkey for the same key so one press
  doesn't act twice (e.g. Esc both closing via `Cancelled` and re-opening via RunDirector).

## Input hints — `UI/InputHints.cs`

- `InputHints.Key(HintAction a)` → key text for the last device used: `Enter`/`A`, `Esc`/`B`, `R`, `M`, `LMB`, `2`–`5`,
  `RMB`, `F`, `Shift`, `WASD`. `""` when the device has no key for it.
- `InputHints.Label("Retry Run", HintAction.Retry)` → `"Retry Run  (R)"` (house format) or `"Retry Run"` on a gamepad.
  Cached, so it's safe in OnGUI every frame.
- `InputHints.Slot(i)` → ability slot 0..4 key (`LMB`, `2`..`5`).
- `InputHints.Last`, `InputHints.Changed` (event), `InputHints.Force(device)` for captures; or launch with
  `-lastbellHints gamepad` / `-lastbellHints keyboard`.
- Actions: Confirm, Ready, Back, Resume, Pause, Retry, MainMenu, Reroll (menu) and Primary, Slot1–4, Dash, Potion,
  Sprint, Move (gameplay).
- **Gameplay hints are always keyboard/mouse:** gameplay input (`KeyboardMouseInput`) reads no gamepad at all.
  Its bindings now live in `KeyboardMouseInput.SlotKeys` / `PotionKey`, which InputHints reads, so hints can't drift.
- **Gamepad has no button of its own** for Pause, Retry, Main Menu or Reroll; on a gamepad those are reached by focusing
  the row and pressing A, so their hints are empty. Only Confirm/Ready (A) and Back/Resume (B) have gamepad hints.
- Last device: any keyboard key, mouse click or mouse movement → keyboard; any standard-gamepad button or a stick past
  half → gamepad. Other joysticks (rudder pedals) never count. WebGL stays on keyboard.

Call sites replaced so far: Results' "Retry Run (R)" / "Retry Practice (R)" and "Main Menu (M)". Everything else is
left for the screen threads (about 25 hard-coded hints; see the plan's appendix).

## What changed visibly

Only Settings: **Apply** is now a text row (26 px, right-aligned in the right third of the action line) instead of a
gold-filled box; Back stays 22 px on the left. Everything else on the main menu, Settings and Boss Rush is a pure port
(same geometry, colours, fonts and sibling order); see `build-evidence/ui-kit-20261110/` for the pixel compare.

## Checks

- `-lastbellUiKitTest [-lastbellOut dir]` (`Menu/UiKitCheck.cs`): shared textures, the ported main menu (NavFocus,
  Apply as a row), InputHints device switching, and ImguiFocus on a sample IMGUI overlay (arrows, wrap, disabled skip,
  dpad, stick, Enter/A, Esc/B, held repeat). Captures `imgui-kit-gamepad.png`, `imgui-kit-keyboard.png`,
  `settings-apply-focused.png`.
- Build + Confrontation + kit check + menu flow + menu smoke: `tools/combined-rebuild/build-ui-kit.ps1` (workspace).

## Not in Phase 1

Screen redesigns (sub-threads A–D), the Showcase menu / MainMenu / Hero Selection / MenuAppearance clean-up (E), boss
intro / cleared / defeat banners (by design), the 3D menu set, `menu-refine/`.
