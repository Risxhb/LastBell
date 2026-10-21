# Area 1 — The Skyward Spire (look mockup)

A three.js look mockup for Area 1, built as close as possible to the first panel of `Reference/LAST-BELL-ARENA-ART-HANDOFF/artwork/a_wide_cinematic_concept_art_style_layout_poster.png`. It is not built on the Bellkeeper key art: that one is a square dais at sunset, while the Spire is a round ivory dais with four bridges under a blue daytime sky.

Open `skyward-spire.html` through a local web server (the Vanguard model loads from `../../vanguard-model.js`). Views: `?view=game` (the same gameplay camera as `arena-look.html`), `?view=angle`, `?view=top`. Add `&tele` to draw a generic hostile circle and cone (a readability check, not a boss mechanic) and `&clean` to hide the tabs. `shots/spire-vs-reference.png` shows the mockup next to the reference.

## What's in it

- Dais: 14 m radius, flat and open, with nothing tall inside the rim. One arena hosts 3 minibosses and the area boss, so the floor stays generic.
- Floor: painted in the page at 3072 px, with colour, bump, roughness/metal and glow layers. It has large cream flagstones, a gold compass medallion with scrollwork and a sapphire heart, and eight spokes with enamel lozenges. There are also four round star emblems, scattered sapphire studs, and a gold-framed cobalt panel border.
- Rim: a curb, a parapet with gold coping, pinnacle posts, and a drum of gold-framed cobalt panels.
- Bridges: four, with balustrades, a cobalt runner and gothic arches beneath. Each has a gatehouse at the mouth (no arch on the camera side) and a gear landing with a gate tower at the far end.
- Towers: gothic, with square tiers, corner pinnacles, pointed cobalt windows, gold cornices, tall ribbed spires or domes, and star banners. They're tall at the back and short at the front, so the gameplay camera keeps the floor in view.
- Below the dais: hanging columns, great gears and a tiered underside.
- Around it: mid-distance spires on rocks, floating cathedral islands, upside-down towers and clouds. Gold and enamel reflect the sky through an environment map.

## Findings

- On a bright ivory floor, a coral warning wash disappears. Warnings here need a deep red-orange fill with a dark outer rim, so they stand out by brightness, not just by colour. The `&tele` shapes use that, and they still read over the detailed floor.
- Fully metallic gold reflects the deep-blue sky overhead and turns olive. Gold here is part-metal with a warm glow.

## Gaps against the reference

The reference is painted and warmer, with heavier sculpted gold (filigree on the towers, ornate gate crowns). The shapes and layout match. The finer surface ornament comes with the Unity pass and modelled assets.

## Next

Once the look is approved, build the Unity scene at `unity/Assets/LastBell/Areas/Area1/SkywardSpire_Arena.unity` with the prefab root `LB_ENV_SkywardSpire_Arena`.

## Update: rebuilt from the top-down reference, and the Unity scene

The user's top-down picture (`reference-topdown.webp`) replaced the poster panel as the target. The current files are:
- `skyward-spire-env.js`: the arena, shared by the look page and the Unity export
- `skyward-spire.html`: the look page (`?view=game|top|angle`, `&overlay=0.5` lays the reference over the top view, `&tele` shows warning shapes)
- `export-glb.mjs`: writes `unity/Assets/LastBell/Areas/Area1/SkywardSpire.glb`

The Unity scene lives in `unity/Assets/LastBell/Areas/Area1/`; see its README. `shots/unity-vs-reference.png` compares the result with the reference. `skyward-spire-prompt.md` (image prompt) and `skyward-spire-unity-build-prompt.md` (the build brief this follows) are kept here.
