# Confrontation main menu: refinement brief (October 26, 2026)

Risxhb asked for a refinement pass on the main menu (the Confrontation menu, `Assets/LastBell/Menu/ConfrontationMainMenu.unity`) because parts of the set still read as placeholders. Work one asset at a time; each asset gets a build, a capture, **one critic agent review** and **one refinement pass** before the next asset starts.

## The shot

- Camera: `(0.6, 0.75, 10.9)` looking at `(-0.6, 2.2, -7)`, FOV 42. Low, over the hero's shoulder; the floor is seen at a grazing angle.
- Hero (selected class) foreground left at `(1.45, 0, 8.3)`; Bellwarden boss at `(0, 0, -15)`, centre frame; menu column on the right third over a dark gradient.
- Palette: night storm. Violet sky and fog `(0.11, 0.09, 0.19)`, cool blue rim light on the hero, warm amber lantern points `(1, 0.48, 0.16)`, damp gold accents, ivory/gold UI. Rain particles and lightning flashes (`ConfrontationWeather`, global `_MenuStormFlash`).
- The set is built by re-runnable editor passes in `Menu/Editor/ConfrontationMenuBuild.cs` (Prepare, Refine, FinishArt, PolishPass, AaaPass): absolute values, menu-only material copies in `Menu/ConfrontationGenerated/`. Gameplay arenas must never change.

## Hard constraints

1. **Menu only.** Every material, texture and mesh change is a menu copy under `Menu/ConfrontationGenerated/` or a new menu-only object. The Key Art arena and every run scene are untouched.
2. **Re-runnable.** Each asset is one public static method in a new editor file (`Menu/Editor/ConfrontationMenuRefine.cs`) that deletes what it generated last time and rebuilds with absolute values, then saves the scene. Run in batch: `-executeMethod LastBell.Menu.EditorTools.ConfrontationMenuRefine.<Asset>`.
3. **Leave alone:** the boss and its lighting, the dial numerals' colour, the music player, the hero models and their rim lights, the menu layout and focus styling.
4. **No new gameplay cost:** no shadow-casting lights, no per-frame scripts beyond what the menu already runs, transparent overdraw kept small (the menu must stay well above the 120 fps cap on an RTX 3060 at 2560×1440).
5. **Readability first:** the menu column (right third) must stay the clearest thing on screen; nothing new may sit behind the buttons brighter than the existing gradient allows.
6. **Every resolution:** check 1600×900, 1280×720, 1024×768 and 2560×1080 (the Confrontation test's capture set).

## Assets (in order)

### 1. Clock floor: wear and rain puddles
- **Now:** the dial (`WetDial`) and paving (`WetPaving`) are clean, uniform and flat; nothing shows that it is raining on stone.
- **Target:** old, damp stone that has been walked on for centuries. Subtle grime collecting toward the rim and in the dial's recessed rings, scuffs along the walking line from the camera toward the boss, a few hairline cracks. **Rain puddles**: 5–8 irregular, shallow pools of varying size on the dial and paving, darker and glossier than the stone, catching the warm lantern points and the violet sky as soft streaked reflections (the scene's `Wet stone reflection` probe). Optionally faint rain ripples in the puddles.
- **Keep:** numeral colour and brightness, the dial's overall dark navy value (it frames the boss).
- **Accept when:** at 1600×900 the floor reads as wet, worn stone at a glance; puddles are clearly puddles (not stains or holes); no z-fighting or shimmer at the grazing camera angle; the dial still reads as a clock.

### 2. Gold rim: one deliberate finish
- **Now:** the large ring around the dial (`MenuDampGold`) is a flat brownish-beige with no highlight or wear; it reads neither polished nor aged.
- **Decision:** **aged, rain-wet brass.** Darkened, tarnished body (brown-olive in the recesses, a hint of verdigris low on the outer face) with a **polished, brighter top edge** where it has been worn by centuries of contact, plus a wet specular sheen that picks up the lanterns and lightning. It should look like a real metal object that belongs to the clock, not a painted band.
- **Accept when:** the ring has a visible bright worn edge and darker tarnished body; specular highlights move believably with the lightning; it does not out-shine the numerals or the logo gold.

### 3. Lantern heads
- **Now:** each lantern's light is a bright, untextured emissive tube (`GlowLantern` on the Key Art dais lantern mesh), which looks unfinished next to the hand-modelled characters.
- **Target:** a proper **glass-and-brass lantern head** in the bell/clockwork style: a dark brass cage (base plate, four corner posts, pierced top cap and a small finial or bell ring), slightly frosted amber glass panes, and a small bright **flame core** inside, with the light visibly coming *through* the glass. A **soft warm halo** (camera-facing, additive, faint) around each head, and the existing point light kept.
- **Applies to:** every lantern visible in the menu (the plaza's own and the three `Menu Lantern` copies). The original tube renderer is hidden, not deleted.
- **Accept when:** at 1600×900 the nearest lantern reads as an object with a frame and glass, not a light stick; the halo is soft (no hard disc edge) and does not bloom into the menu column.

### 4. Defined clouds in the purple sky
- **Now:** `MenuStormSky` draws soft, low-contrast fbm noise; the sky reads as one flat purple.
- **Target:** clearly **defined storm clouds**: layered banks with readable shapes and darker undersides, brighter violet-lit edges toward the horizon glow, a slower high layer and a faster low layer (parallax), still very dark at the zenith so the logo and menu stay legible. Lightning flashes keep lighting the cloud undersides.
- **Accept when:** cloud shapes are distinguishable at 1600×900 behind the boss and towers; the top-right sky behind the menu column stays dark and calm; no banding or tiling visible; motion is slow and calm.

### 5. Difficulty text
- **Now:** "Difficulty: Hard" is 16 px (18 px on short screens), small next to the main buttons.
- **Target:** larger (about 20 px, 22 px on short screens) with its chevrons re-fitted; still clearly secondary to Start Run / Boss Rush / Settings / Exit.
- **Accept when:** readable at 1024×768 and 2560×1080 without crowding Exit or the music player.

### 6. Mauve blocks behind the boss
- **Now:** a row of plain, flat-coloured rectangular blocks along the back of the dais behind the Bellwarden's legs; the most "greybox" shapes on screen.
- **Target:** a carved stone **parapet / balustrade** in the dais's own stone family (dark violet-grey masonry with damp darker bases), with capstones, chamfers and a little brass trim, so it frames the boss's legs instead of competing with them. Same footprint; quieter value than the boss.
- **Accept when:** no block reads as a primitive; the boss's silhouette and legs stay the brightest shapes in that area.

### 7. Green round rocks at the boss's feet
- **Now:** low-poly green lumps that read as placeholder shrubs and are the only green in a violet/gold/navy palette.
- **Target:** **rubble and fallen bell fragments** (broken masonry chunks, a cracked bell shard with a brass lip) in the scene's stone and dark brass, wet and dark. If replacing a piece is impractical, recolour it to damp stone with subtle moss in the crevices (desaturated, never bright green).
- **Accept when:** nothing green stands out; the debris supports the "the hour is breaking" mood without drawing the eye from the boss.

### 8. Cloud cards (Sea of Clouds)
- **Now:** 134 flat cards with a simple texture; when the camera is still they read as stacked sheets with visible edges.
- **Target:** a soft, continuous **cloud floor** below the platform: fewer, larger, softly scrolling layers with feathered edges and depth fade, lit violet from above and darker below, matching the sky clouds of asset 4. No visible card edges or intersections.
- **Accept when:** no straight card edge or intersection line is visible at any resolution; it reads as a cloud sea, not sheets.

### 9. Banner (left edge, visible on wide screens)
- **Now:** a flat plane with a simple emblem.
- **Target:** a **cloth banner** with folds and weight (a few vertical folds, a slightly heavier wet hem, a frayed lower edge) on a brass pole with finial, emblem kept, and a slow, subtle sway. If kept static, the folds must be modelled.
- **Accept when:** it reads as hanging cloth at 2560×1080; motion (if any) is slow and does not distract from the menu.

## Procedure per asset

1. Implement the pass; run it in batch on `collab/unity`.
2. Build the Windows player (`ConfrontationMenuBuild.BuildWindows`) to `builds/menu-refine-20261026`.
3. Capture with `LastBell.exe -lastbellConfrontationTest -lastbellConfrontationCaptureOnly -lastbellOut <dir>` (all classes × the four resolutions) and crop the asset's region.
4. **Critic agent** (one per asset): given this brief, the before and after crops and full frames, it scores the rubric below and lists concrete, actionable fixes in priority order.
5. **One refinement pass** addressing the critic's top issues, rebuild and capture again, then move on.
6. At the end: full Confrontation and menu-flow suites, then a report with before/after images.

## Critic rubric (1–5 each, with evidence)

- **Placeholder test:** does it still read as a placeholder or primitive?
- **Material believability:** do surface, gloss and wear match a real object in this weather?
- **Fit:** palette, lighting direction and scale consistent with the scene and the hand-modelled characters?
- **Composition:** does it keep the eye on the hero, boss and menu? Anything distracting or competing with the UI?
- **Technical:** z-fighting, shimmer, hard alpha edges, banding, tiling, overdraw-heavy effects, artefacts at any of the four resolutions.

## Results (October 26, 2026)

Branch `menu/refine-20261026` (worktree `menu-refine`), passes in `Menu/Editor/ConfrontationMenuRefine.cs`; evidence in `build-evidence/menu-refine-20261026/<asset>-<n>/` (before/after crops) and `report/` (full frames against the `polish-20261026c` baseline).

- **Floor:** wear disc on the dial, 8 dark puddles with gloss/occlusion maps, glints in two. **Rim:** aged brass shader (polish on tops, verdigris underneath) instead of chrome.
- **Lanterns:** iron posts, procedural cage + frosted amber glass + halo instead of glowing tubes.
- **Sky:** defined cloud banks (`StormSky` `_Defined=1`); the old sky is still the `_Defined=0` branch.
- **Difficulty:** 20 px (22 small) with a soft shadow.
- **Merlons:** matte dark stone with caps, shadow line, plinth, damp band; posts get edge strips and seams.
- **Rubble:** bushes hidden; wet stone chunks and five grounded bell shards.
- **Cloud sea:** the cards sit ~20 m below the dais and are almost out of view; their only visible effect was a grey film at the parapet base. Refined to alpha 0.2 (film gone). Parapet wisps were tried and removed: they did not read against the same-hue sky.
- **Banners:** the authored mesh is subdivided and only displaced (outline, pointed hem, emblem and trim kept): 3 folds of 3.5 cm pinned at the bar, rolled hem bead, 2 cm sag at the point, matte menu-only material.
- **Fix found in testing:** the Settings resolution dropdown kept the list it built at start-up, so after the window changed size it showed the wrong current resolution and failed the Confrontation check. It now resyncs on every open.

Suites on the final build: Confrontation 110/0, menu flow 141/0, Tempo Tyrant 134/0, Chimebound Oracle 68/0 (+19/0 with smoothing). Run autotest 469/8: all 8 are the Aurelion v2 phase checks, which fail identically on the collab build without these changes.
