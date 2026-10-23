# Last Bell browser demo

The browser demo builds the [current main player](current-player.md): both menus, HeroSelection and every scene in `RunMaps.All`. ShowcaseMainMenu is the startup router; new profiles default to Alternative Confrontation and saved menu choices remain respected. No JavaScript combat implementation is involved.

Public address: https://risxhb.github.io/lastbell/

## Build

Use Unity **6000.6.6f1**, including its matching **Web Build Support** module. Work in a separate checkout so Web-target imports and settings do not alter a developer's current Windows project.

Run `setup-assets.cmd` before opening the isolated Unity checkout. The current-player asset pack supplies the exact ignored Brass Foundry generated assets, source scene and run scene with their original metadata. This preserves the GUIDs referenced by the saved showcase. Rebuild authoring scenes only for an intentional arena change, then update the versioned pack and validate the combined player. Do not replace another developer's scenes during publication.

Run `Last Bell > Main Menu > Build Browser Demo`, or invoke Unity with:

```text
-batchmode -quit -projectPath <checkout>/unity -buildTarget WebGL -executeMethod LastBell.Menu.EditorTools.WebDemoBuild.Build -logFile <build-log>
```

Output defaults to `unity/Builds/Web/lastbell`. Set `LASTBELL_WEB_OUTPUT` to an absolute directory to override it. The builder uses existing saved scenes; it does not regenerate arenas or the menu. The custom template is `unity/Assets/WebGLTemplates/LastBell/`.

Gzip plus JavaScript decompression fallback supports GitHub Pages without custom Content-Encoding headers. WebAssembly threading is disabled because Pages does not provide configurable cross-origin isolation headers. The player starts on a click, focuses the canvas, prevents the right-click context menu over the game, provides download progress/errors and exposes fullscreen. Browser menus use keyboard/mouse, matching gameplay, so connected gamepad peripherals cannot submit or navigate unexpectedly. Windows gamepad support is unchanged. Music and quality settings stay in Unity; browser sizing uses the page's fullscreen button. The browser's Restart Menu button reloads the menu instead of attempting to quit a tab.

## Publish without replacing the other project

The existing `Risxhb/risxhb.github.io` repository serves its `main` branch root. Add or replace **only `lastbell/`** with the Web output. Preserve every other tracked path and the existing `.nojekyll` file. No repository rename, CNAME, root redirect, root-index change or Pages source change is needed. Generated player binaries belong to the Pages repository, not the Unity source repository.

The build packages the compressed Unity data into content-addressed parts no larger than 64 MiB. The page downloads those parts with progress, verifies their SHA-256 hashes, and assembles a local Blob URL for Unity's normal decompression/loading. `Build/data-manifest.json` records the exact order, lengths and hashes. This preserves all five maps while respecting GitHub's 100 MiB regular-Git file limit, without changing the project's unresolved LFS policy.

Before committing, verify all changed paths begin with `lastbell/` and every file is below 100 MiB. Verify the live main menu, settings, class/map selector, gameplay, pause and return route after deployment. Prior Windows tests do not establish browser acceptance.
