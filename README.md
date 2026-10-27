# Last Bell

A clockwork-fantasy boss rush with real-time, isometric combat. Choose the Vanguard, Arcanist or Chimesinger, learn boss patterns, and fight through three minibosses before the final boss.

## Play the demo

**[Play Last Bell in your browser](https://risxhb.github.io/lastbell/)**

1. Open the link on a desktop with a keyboard and mouse, then click **Play demo**. The first download includes all five arenas.
2. Choose your class above **Start Run** and choose a difficulty, then start. **Boss Rush** opens the map/encounter practice setup.
3. Start the run and press **Enter** on the Ready screen to begin fighting.

**Controls:** WASD to move, mouse to aim, left click / **1** for basic attacks, **2–4** for class skills, right click / **5** to dash, **6** for an additional class skill, **Space** to jump, **Shift** to sprint, **F** for a potion, and **Esc** to pause.

The demo is a work in progress.

This combined Unity player is the current development baseline. **All boss, arena and other game modifications must integrate into this player.** New profiles start in Alternative Confrontation; Settings > UI > Main Menu can select the five-arena Original Showcase. Boss music continues between fights and crossfades into the next track during the countdown.

For local development and the shared Windows/browser build path, read [Current main player](docs/main-menu/current-player.md). Run `setup-assets.cmd` before opening Unity, build **Last Bell > Current Player > Build Windows Player**, and launch `Play Last Bell.cmd`.
