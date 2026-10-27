# Last Bell main menu

Dedicated Unity scene: `Assets/LastBell/Menu/MainMenu.unity`.

Open **Last Bell > Main Menu > Open Scene** in Unity 6000.6.6f1. Enter Play Mode to see the menu. Build with **Last Bell > Main Menu > Build Windows Player**, then launch `Play Main Menu.cmd` from the repository root. The player is generated under `unity/Builds/Menu/`; keep its entire directory together.

The three bottom buttons are **Start Run**, **Settings**, **Exit**. Start Run enters the existing map/class/practice selection screen. Choose a class and start the run there. Returning from pause or results routes back to this main menu. Keyboard arrows/Enter and gamepad navigation/Submit work alongside the mouse. Escape/gamepad Back closes settings; an open dropdown consumes Back first. Exit stops Editor Play Mode or quits the standalone player.

## Scene and presentation

The scene copies the existing Bellkeeper's Dais environment without regenerating the arena or modifying source assets. `MenuBuild` creates the scene and places it first in Editor Build Settings. Its separate Windows build method includes the menu and all five existing run maps. The older **Run Loop > Build Windows Player** remains a diagnostic run-scene build and does not include this menu.

`MainMenu` creates the Canvas UI, contrast gradients and input module at runtime. It uses the supplied transparent Last Bell logo. `MenuBattle` spawns the current Vanguard, Arcanist, Chimesinger and Bellkeeper2 models through the existing `CharacterPresenter`. The camera, orbit speed, cycle duration and character positions can be tuned on the scene's `MenuBattle` component. The default cycle lasts 36 seconds. All four actors have separate world-space routes and departure times: Vanguard retreats and closes to melee, the two casters reposition along different sides of the arena, and Bellkeeper walks, plants for tells and charges. Attacks plant their caster for the animation's duration. Locomotion follows actual displacement and facing, with boss walk playback scaled to travel speed. No shared formation offset moves the party. The camera completes a slow orbit in 180 seconds.

The choreography calls the existing `PlayerFx` and `BellkeeperFx` renderers: Vanguard cleaves, shield and Last Word; Arcanist seal casts, empowered Lance and Convergence; Chimesinger bolts, Refrain, Crescendo and Chorus; Bellkeeper sweep, toll, charge and chimes. Disabled PlayerController components supply pose/height to those presentation APIs. They have no World, Kit or Input and are never stepped. There is no AI, damage or combat simulation. Menu-specific GPU particle batches are retained in an inactive persistent pool between visits, avoiding the project's known recreation problem, and are separate from run FX.

`MenuReturnBridge` is installed only when leaving this menu. It listens to existing RunDirector state changes; direct run-scene diagnostic sessions retain their normal flow. No production damage, movement, kit, boss schedule or shared contract changes are required.

## Settings and asset limits

Settings exposes **Main Menu Music**, display mode, resolution and quality controls. Music volume changes immediately, supports mute, and persists under `LastBell.Menu.MusicVolume`; the other settings save on Apply. The menu continues animating behind the modal and the bottom controls cannot receive input through it.

The supplied `E:\Collab\reference\Last Bell.png` and `E:\Collab\audio\mainmenu.wav` are copied byte-for-byte into `Menu/Resources/Menu/`; the originals are untouched. Unity imports the logo with alpha and streams the music as Vorbis. The track loops, fades in when the menu opens, and fades out before starting a run. Music stops when leaving the scene and restarts as one source on return. Existing character rigs/clips retain their current review status. The Chimesinger is the committed pre-grip-fix stand-in; its pending export is untouched. Human art/readability acceptance remains separate.

## Reproducible runtime checks

Windows player:

```powershell
& unity/Builds/Menu/LastBell.exe -lastbellMenuTest -lastbellOut <absolute-evidence-folder> -logFile <absolute-log-path>
```

Editor Play Mode (launch from a closed project):

```powershell
Unity.exe -projectPath <absolute-unity-folder> -executeMethod LastBell.Menu.EditorTools.MenuBuild.PlayModeSmokeTest -lastbellMenuTest -lastbellOut <absolute-evidence-folder> -logFile <absolute-log-path>
```

The opt-in test drives real InputSystem keyboard/gamepad events and public UI actions, checks five full fight cycles, captures three aspect/resolution previews, checks bounded objects/materials, and exercises fresh run and return routing. The existing `-lastbellAutotest` flag skips the menu and retains the prior run-loop test entry point. Test results must be reported with the actual runtime and logs; previous checkout results do not validate this menu.
