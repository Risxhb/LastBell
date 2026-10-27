# Starting straight into a boss practice

Added October 26, 2026 for boss work. The Windows player can skip the menus and start one boss's practice fight:

```
LastBell.exe -lastbellPractice <boss> [-lastbellClass Vanguard|Chimesinger|Arcanist]
```

- **Boss names** are matched loosely: case, spaces, dashes and the `(practice)` / `(area n)` suffix are ignored, and any unique part of the name works. Examples: `tempo-tyrant`, `tempo`, `oracle`, `chimebound-oracle`, `aurelion`, `bellkeeper`.
- **Class** defaults to Vanguard.
- It uses the same path as **Boss Rush → Launch Test Rush**: the boss's own arena loads, practice starts by itself after a short beat, and **Esc → Main Menu** returns to the normal menu.
- An unknown or ambiguous name logs an error listing the valid bosses (see `player.log`) and leaves you on the main menu.
- Test suites are unaffected: they don't pass this flag.

When working on a boss, also go through [frame-pacing.md](frame-pacing.md): drawing is now smoothed between the 60 Hz ticks and the FPS cap / VSync are player settings, so frame-timed boss code needs a check.

Code: `unity/Assets/LastBell/Menu/MenuPracticeLaunch.cs`. A new boss needs nothing extra: anything in `RunDirector.Bosses` can be launched this way.

Workspace launchers in `F:\Last Bell\Collab` use it, e.g. `Play Tempo Tyrant Practice.cmd` and `Play Chimebound Oracle Practice.cmd`. To add one for a new boss, copy one of those and change the name after `-lastbellPractice`.
