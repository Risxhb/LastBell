# Prompt: Brass Foundry detail and resolution pass, with flowing lava (Unity URP)

Paste everything below the line into a Claude Code session that has the `unity-session` checkout open.

---

## Task

Do a full detail and resolution pass over **Area 2, The Brass Foundry** arena in Unity. The goal is a finished, high-quality stylised environment. The art bar is Hades and Windblown: painterly, crisp and readable from the gameplay camera.

The work has two parts. Both are required:

1. **The molten ring around the arena must visibly flow.** It must animate in Play mode and in the editor's Scene and Game views, using a real flowing-lava shader rather than a slow texture scroll.
2. **Every part of the map gets higher resolution and more detail:** floor, rim, molten channel, outer wall, towers, gantries, entry bridge, furnace gate, chains, the city below, and the sky and atmosphere.

Today the arena scores **6.8 / 10** with an art critic. The goal of this pass is **8 / 10 or better** (see "Critic loop" at the end).

## Read first

1. `gpt/08-design-rules.md`. These are settled rules and they override everything else. They matter most for telegraphs, arenas and hazards.
2. `gpt/05-arenas.md` and `gpt/02-visual-style-guide.md`. The `gpt/` folder is the source of truth for art.
3. `prototype/arena-3d/areas/area-2/README.md`, `BRASS-FOUNDRY-UNITY-PROMPT.md` (the original brief, whose sizes and layout still apply) and `ref-brass-foundry-topdown.webp` (the reference image; keep it open the whole time).
4. The current code:
   - `unity/Assets/LastBell/Areas/Area2/Editor/BrassFoundryArenaBuilder.cs`: everything is generated from code, including meshes, textures, materials, lights, post and FX.
   - `unity/Assets/LastBell/Areas/Area2/BrassFoundryArena.cs`: views, `quiet` dimming, the current molten UV scroll and lantern sway.
5. The current renders in `prototype/arena-3d/areas/area-2/shots/unity-*.jpg` and the last critic's open items (below).

**Open items from the last critic round:**
- The rim blocks still read a little like crates.
- The molten ring is a flat orange band.
- The city below looks dead; it needs glowing lava rivers and depth.
- From above, the towers read as lids.
- The gantries need pylons and end structures.
- Too many chains cross the top view.

## Rules you must keep

### Design rules (from `gpt/08-design-rules.md`)
- **Decorative art must never look like a hazard.** The lava sits **outside** the playable floor and is not dangerous. Only gameplay code makes something dangerous, and there is no damage volume anywhere in this arena.
- **Hostile telegraphs are coral or amber.** The molten glow and every decoration must stay quieter than a telegraph on the floor. Keep the floor itself free of glow, and keep `quiet` dimming the molten and window glow while telegraphs are up.
- **Run layout:** centre origin, entry to the south (+Z), floor at y = 0, walkable radius at least 13.5 m (the floor is 14 m), and the run's 11.5 m play bound.
- **Camera:** fixed high-oblique camera. The playable floor stays broad, flat and unobstructed for 8 players. Tall architecture sits outside the playable area or fades when it blocks the view.

### Run-loop contract
The run loop (`unity/Assets/LastBell/Run/`) builds its Brass Foundry map from this builder. Don't break it:
- Keep `public static void BrassFoundryArenaBuilder.Build()`. `Run/Editor/RunBuild.cs` calls it when the scene is missing.
- Keep the scene path `Assets/LastBell/Areas/Area2/BrassFoundry_Arena.unity` and the root `LB_ENV_BrassFoundry_Arena`.
- Keep `Gameplay/BossSpawn`, `Gameplay/PlayerSpawn_1` to `_8` and `Gameplay/Boundary`.
- The run loop disables objects named `ScaleReference_Vanguard`, `TelegraphCheck` and `TelegraphTest`. Any look-check prop you add must use one of those names, or be created only in the batch capture and never saved.
- `Generated/` and the scene stay git-ignored. Everything must regenerate from the builder alone.

### Files and git
- **Files:** edit only `unity/Assets/LastBell/Areas/Area2/**` and `prototype/arena-3d/areas/area-2/**`. New shaders go in `unity/Assets/LastBell/Areas/Area2/Shaders/`. Don't edit the run loop, other areas, characters, `ProjectSettings` or the URP pipeline assets. If something there needs to change, stop and ask.
- **Branch:** work on `feature/class-test-arena`. Run `git pull --ff-only` first. Other sessions keep uncommitted work in this checkout, so never stash, reset or switch branches. Stage only your own files, by path. When it's done, commit and push (the user has approved pushing Last Bell work). End the commit message with the attribution line you've been given.

## Build and test setup

- **The Unity editor usually has this project open**, and batch mode can't open a project that is already open. Build and capture in a scratch copy of the project:
  1. Copy `Assets`, `Packages`, `ProjectSettings` and `Library` to your scratchpad.
  2. Run `Unity.exe -batchmode -projectPath <clone> -executeMethod LastBell.Areas.Area2.EditorTools.BrassFoundryArenaBuilder.BatchBuildAndCapture -shots <dir> -logFile <log>` in the background. Each build takes 2–4 minutes.
  3. When it's clean, copy only `Areas/Area2` back.
- **Rebuild from empty every time:** delete the clone's `Area2/Generated` and the scene first, so stale assets never hide a bug.
- **After copying back,** open the scene once in the user's editor (menu **Last Bell > Rebuild Area 2 – Brass Foundry**), or ask the user to. Make sure the console shows no errors.

## Part 1: Flowing molten ring (do this first)

Replace the current UV-offset scroll with a custom URP shader, `LastBell/Area2/MoltenFlow` (HLSL, unlit, HDR, fog-aware, SRP-Batcher compatible). Use it on the channel ring, the city rivers and pools, and the molten falls on the outer drum.

**How it should look:**
- **Flow along the ring.** The channel mesh's U coordinate is arc length in metres and V runs across the channel. The lava flows clockwise as seen from above, at about 0.3 m/s at the centre line and slower near the banks (a parabolic speed profile across V).
- **Two-phase flow-map blending:** two noise samples offset by half a period and cross-faded, so the lava streams continuously without visible resets or stretching.
- **Crust:**
  - Dark cooling crust plates (`#2A0C04` to `#4A1406`) cover about 45–55% of the surface. They drift with the flow and slowly break apart and re-form, with a time-varying threshold on a second noise.
  - The cracks between plates glow from orange `#FF6A1A` to hot yellow-white `#FFD27A` in the cores.
  - Base emission is about 2.5–3; the cores peak around 5 for bloom.
- **Banks:** darker, cooler edges where the lava meets the stone, about 0.3 m wide on each side. The inner edge tucks under the rim blocks.
- **Life:**
  - A slow pulse, ±8% over about 4 s.
  - Occasional hot bubbles: small bright spots that swell and pop over about 1.5 s, driven by cell noise.
  - A subtle vertex undulation of 2–4 cm (the channel mesh needs enough segments for this; see Part 2).
- **Falls** use the same shader with V-axis flow at about 3 m/s. They are brighter at the lip and break into streaks lower down.
- **Rivers and pools** in the city below use the same shader, slower (about 0.1 m/s), with more crust.

**Animation in both modes:**
- Drive time from a global `_LB_Area2Time`.
- `BrassFoundryArena` sets it every frame in Play mode. In edit mode it uses `EditorApplication.update`, guarded with `#if UNITY_EDITOR`, so Scene and Game views animate when "Always Refresh" is on.
- The batch capture can set the value directly, which keeps screenshots deterministic.

**Quiet:** expose `_Quiet` (0–1). The driver lerps it in 0.25 s, and it dims emission to 60%.

**Light:** keep the molten uplights. Add a gentle flicker (±10%, 3–6 Hz noise, de-synced per light). Keep the glow curtain, and let its alpha breathe with the pulse.

**Verify the motion:**
- In the batch capture, render the gameplay view at `_LB_Area2Time` = 0 and again at 1.0.
- Compute the mean absolute pixel difference inside a channel mask and inside a floor mask (r ≤ 13).
- Write both numbers to `unity-report.txt`. Required: channel difference above 4/255 and floor difference below 0.5/255.
- Save the two frames as `unity-flow-a.png` and `unity-flow-b.png`, and convert them to JPG for the repo.

## Part 2: Resolution

**Texture targets:** generated in code as today, saved as PNG, with mipmaps and anisotropic filtering at 8 or more.

| Surface | Now | Target |
|---|---|---|
| Combat floor albedo, height to normal | 2048 | **4096**, plus a packed mask (R = AO from seams, G = smoothness variation) |
| Masonry | 1024 | **2048**, in **two variants** (large blocks and coursed bricks) plus an edge-wear mask |
| Iron plate | 512 | **1024**, with rust streaks running down from rivets |
| Deck planks | 512 | **1024**, with wood grain, worn centre path and brass nosing |
| Brass | 256 | **512**, with a curvature-style edge highlight and patina in recesses |
| Molten noise | 512 | **1024**, tileable, in two channels (crust and cracks) |
| Sky panorama | 1024×512 | **2048×1024**, with smoke banks and a horizon glow |

**Detail normal:** add a shared 512 detail normal (fine pitting and grain), tiled at about 0.5 m on stone and iron. Fade it out beyond about 40 m.

**Texel density:**
- At least 140 px/m on the floor.
- At least 128 px/m on the rim, gate and bridges.
- At least 64 px/m on the outer wall and towers.
- Log the numbers in the report.

**Mesh resolution:**
- Rings and curved surfaces: about 0.25 m per segment on the channel (so the undulation reads) and 256 segments on rim and floor rings.
- Tower lathes: 64 segments.
- Chain links: 12 × 8.
- Floor disc: 16 rings × 256 segments.

**Bevels everywhere it matters:**
- 0.06–0.08 m on large blocks and pillars.
- 0.02–0.03 m on trims and caps.
- No unbevelled hero-scale boxes remain on the rim, gate, bridges or towers.

## Part 3: Detail pass, area by area

Work through each area. Keep the reference's layout and proportions; add detail, not clutter.

1. **Combat floor (keep it calm):**
   - Slab edges get subtle bevel AO.
   - Brass inlays get a 1 cm bevel and slight edge wear.
   - Add fine engraved tick rings around the medallion, plus a few shallow worn footpaths to the four entries (albedo and roughness only).
   - Keep cracks sparse and low-contrast.
   - No glow and no orange on the floor. The telegraph test must stay the most eye-catching thing on it.
2. **Furnace rim:** a single ring of separate gear-tooth blocks.
   - Leave gaps of 0.3–0.5 m so the lava glow shows between them.
   - Big and small teeth alternate.
   - Heights vary ±15%. About one in three big blocks has a stacked 0.6 m block.
   - Brass edge trims, rivet rows, a few chain anchor rings and small gauge or valve props on some tops.
   - No lid seams, and nothing that reads as a crate.
3. **Molten channel:** Part 1's shader, plus:
   - Heat-haze particles: soft, slow and dim.
   - Occasional spark bursts.
   - Darker banks where it meets stone.
4. **Outer wall:** make it massive.
   - Thick buttresses.
   - Pipe runs with brass flanges.
   - Furnace hatches glowing dimly.
   - Steam vents with occasional puffs.
   - Iron catwalk segments.
   - Uneven, blocky silhouettes like the reference.
5. **Corner towers:** from above they must read as towers, not lids.
   - Three stepped tier rings with crenellations.
   - A crown of merlons and a ring of machinery boxes.
   - Glowing window slits on the upper tier, visible from the top view.
   - A chimney or bell cupola on the far pair, with smoke.
   - The camera-side pair stays low enough to pass the occlusion check.
6. **East and west gantries:**
   - Plank decks with brass nosing.
   - Visible pylons at r 26 and truss undersides.
   - An end tower or block cluster at r 34–40, as in the reference.
   - Lamp posts with cage lanterns.
   - Two suspension chains per pylon, and no chains crossing over the deck.
7. **Entry bridge (south):**
   - Plank deck with the brass centre line and the inward arrows.
   - Gate pillars with lanterns, and drawbridge chains.
   - Side curbs with brass caps.
   - A small gatehouse or arch at the far end.
8. **Furnace gate (north):**
   - Dark iron portcullis over a dim, flickering glow.
   - A gear emblem with real teeth.
   - Stepped pillar caps and furnace windows.
   - A short deck behind, with chains rising into the haze.
9. **Chains:** heavy and readable, but at most 2 per tower and 2 per pylon. Route them outward so they don't criss-cross the top view.
10. **City below and abyss:**
    - Dark silhouetted foundry blocks in three depth layers (y −20 to −60).
    - Lit window slits and roof vents.
    - Chimney stacks with smoke.
    - **Glowing lava rivers and pools** between the blocks (Part 1's shader).
    - Warm light pooling under the arena drum.
    - Linear fog that tints distance toward smoky brown-grey `#3A2C26`, with no flat rust plane.
    - From the top view, the bottom corners should glow like the reference.
11. **Atmosphere and FX:**
    - Embers rising from the ring.
    - Slow smoke banks below.
    - Occasional spark showers near the furnace gate.
    - Everything subtle, with no particles over the floor.

## Part 4: Lighting and post

- Keep the key, cool fill and furnace uplight balance: the floor is cool graphite and the rim is warm.
- Add light flicker to the molten and lantern lights (Part 1).
- Keep shadows only where they help: rim blocks, gate and towers onto themselves. Chains and city don't cast onto the floor.
- Use the existing post profile. You may tune bloom (threshold ≥ 1.0), vignette and colour adjustments. Don't touch the URP pipeline or renderer assets.

## Performance budget

60 fps at 1080p on a mid-range GPU, measured in the Game view:
- Under 1,500 draw calls after batching.
- About 1.5 M triangles.
- Generated textures under about 120 MB of GPU memory.
- Shadows from one directional light; unshadowed point lights only, at most about 90.

Log renderers, materials, triangles, lights and texture memory in `unity-report.txt`. If you go over budget, say so and why.

## Acceptance (check each one and put the results in your report)

1. **Lava flows:**
   - The flow diff numbers pass (channel above 4/255, floor below 0.5/255).
   - It animates in the editor without Play mode.
   - `quiet` dims it.
2. **Occlusion:** 0 of 128 floor sample points blocked from the gameplay camera.
3. **Telegraph test:** the coral disc is the most eye-catching thing on the floor (`unity-game-tele`).
4. **Clean output:** no console errors or warnings from Area 2 code, no pink shaders and no z-fighting.
5. **Rebuilds from empty:** after deleting `Generated/` and the scene, the menu item rebuilds everything.
6. **Run loop still works:**
   - Run `LastBell.Run.EditorTools.RunBuild.BuildScene` in batch on the clone.
   - Open `Run_BrassFoundry.unity` and check that the lava animates there too.
   - If the run loop's play-mode autotest is available (`RunBuild.PlayModeAutotest`), run it and report the pass count.
7. **Screenshots:** game, top, hero, game-tele, flow-a and flow-b, plus close-ups of the rim, a tower and the gate. Save them as JPG in `prototype/arena-3d/areas/area-2/shots/`.
8. **Docs:** `README.md` in `areas/area-2` is updated (shader, time driver, new textures, budgets).

## Critic loop

When the build passes acceptance, spawn a critic subagent:
- Give it the reference image, the new screenshots, `unity-report.txt` and this prompt.
- Have it score 1–10 against: layout fidelity 30%, materials and colour 25%, lighting and atmosphere 20%, detail and craft 15%, readability 10%. It must also name the top 6 fixes.

Refine and re-run the critic until it scores **8 / 10** or after **4 refinement rounds**, whichever comes first. Report each round's score.

## Report back (the user reads on a phone)

Keep it short and plain:
- What changed.
- Whether the lava flows (with the diff numbers).
- The critic scores per round.
- Acceptance results.
- Anything that missed.
- The commit link.

Send the key screenshots as files.
