# Last Bell browser demo

The browser demo is a cut of the [current main player](current-player.md): Boss Rush only, on two arenas, without the minibosses and without LAN (Risx, 11 Nov 2026). ShowcaseMainMenu is the startup router. No JavaScript combat implementation is involved.

Public address: https://risxhb.github.io/lastbell/

## What the demo leaves out

The full player as a browser build is about 1.05 GB, over GitHub Pages' 1 GB site limit. The cut is about 490 MB.

- **In:** the five bosses and the tutorial in Boss Rush, on the Bellkeeper's Dais (`test`) and Metronome Court (`WebDemoBuild.DemoMaps`).
- **Greyed out:** Start Run, the nine miniboss cards and the other four arenas. `TestRushSetup.DemoCut` (true only in a WebGL player) does this, and `ConfrontationMenu` leaves out the Multiplayer row on WebGL.
- **Not in the download:** the other run scenes, the miniboss `Resources` folders, the wing music, and the other arenas' showcase backdrops.

Windows builds are not affected: `DemoCut` is false there.

**The build edits the project.** `WebDemoBuild.StripForDemo` moves each excluded character's `Resources` folder to `NotInWebDemo`, moves `Menu/Resources/Menu/WingMusic` out of `Resources`, and empties the showcase stages of the arenas that are left out, then saves the showcase scene. It refuses to run unless `LASTBELL_WEB_ISOLATED=1` is set. Set that only in the separate web checkout, never commit the result, and restore the checkout with git before the next build or any other use.

## Build

Use Unity **6000.6.6f1**, including its matching **Web Build Support** module. Work in a separate checkout so Web-target imports and settings do not alter a developer's current Windows project.

Run `setup-assets.cmd` before opening the isolated Unity checkout. The current-player asset pack supplies the exact ignored Brass Foundry generated assets, source scene and run scene with their original metadata. This preserves the GUIDs referenced by the saved showcase. Rebuild authoring scenes only for an intentional arena change, then update the versioned pack and validate the combined player. Do not replace another developer's scenes during publication.

Run `Last Bell > Main Menu > Build Browser Demo`, or invoke Unity with:

```text
-batchmode -quit -projectPath <checkout>/unity -buildTarget WebGL -executeMethod LastBell.Menu.EditorTools.WebDemoBuild.Build -logFile <build-log>
```

Set `LASTBELL_WEB_ISOLATED=1` in that checkout first (see above). Output defaults to `unity/Builds/Web/lastbell`. Set `LASTBELL_WEB_OUTPUT` to an absolute directory to override it; the folder name becomes the file prefix (for example `lastbell-v13`). The builder uses existing saved scenes; it does not regenerate arenas or the menu. The custom template is `unity/Assets/WebGLTemplates/LastBell/`.

Gzip plus JavaScript decompression fallback supports GitHub Pages without custom Content-Encoding headers. WebAssembly threading is disabled because Pages does not provide configurable cross-origin isolation headers. The player starts on a click, focuses the canvas, prevents the right-click context menu over the game, provides download progress/errors and exposes fullscreen. Browser menus use keyboard/mouse, matching gameplay, so connected gamepad peripherals cannot submit or navigate unexpectedly. Windows gamepad support is unchanged. Music and quality settings stay in Unity; browser sizing uses the page's fullscreen button. The browser's Restart Menu button reloads the menu instead of attempting to quit a tab.

## Publish without replacing the other project

The existing `Risxhb/risxhb.github.io` repository serves its `main` branch root. Add or replace **only `lastbell/`** with the Web output. Preserve every other tracked path and the existing `.nojekyll` file. No repository rename, CNAME, root redirect, root-index change or Pages source change is needed. Generated player binaries belong to the Pages repository, not the Unity source repository.

The build packages the compressed Unity data into content-addressed parts no larger than 64 MiB. The page downloads those parts with progress, verifies their SHA-256 hashes, and assembles a local Blob URL for Unity's normal decompression/loading. `Build/data-manifest.json` records the exact order, lengths and hashes. This respects GitHub's 100 MiB regular-Git file limit without changing the project's unresolved LFS policy.

Before committing, verify all changed paths begin with `lastbell/`, every file is below 100 MiB, and the whole Pages site stays under 1 GB (the rest of the site is about 340 MB). Verify the live main menu, settings, Boss Rush arena and boss selection, gameplay, pause and return route after deployment. Prior Windows tests do not establish browser acceptance.
