# Area 1 — The Skyward Spire (look mockup)

A quick three.js look mockup for Area 1, built from the first panel of `Reference/LAST-BELL-ARENA-ART-HANDOFF/artwork/a_wide_cinematic_concept_art_style_layout_poster.png`. It is not built on the Bellkeeper key art: that one is a square dais at sunset, while the Spire is a round ivory dais with four bridges under a blue daytime sky.

Open `skyward-spire.html` through a local web server (the Vanguard model loads from `../../vanguard-model.js`). Views: `?view=game` (the same gameplay camera as `arena-look.html`), `?view=angle`, `?view=top`. Add `&tele` to draw a generic hostile circle and cone (a readability check, not a boss mechanic) and `&clean` to hide the tabs.

## What's in it

- Dais: 14 m radius, flat and open, with nothing tall inside the rim. One arena hosts 3 minibosses and the area boss, so the floor stays generic.
- Floor: radial ivory marble slabs, two muted slate-cobalt bands, thin gold hairlines, and a cobalt compass medallion at the centre.
- Four bridges in a cross, each ending at a gold gear landing with a small obelisk.
- Spires stand on the diagonals: tall at the back, short domed ones at the front so the gameplay camera keeps the floor in view.
- Below: a tiered underside with turning gears; floating spire islands and clouds all around.

## Findings

- On a bright ivory floor, a coral warning wash disappears. Warnings here need a deep red-orange fill with a dark outer rim, so they stand out by brightness, not just by colour. The `&tele` shapes use that.
- Pure cobalt rings on ivory looked as loud as the warnings did, so the bands are toned down to slate-cobalt. Only the centre medallion stays saturated.

## Gaps against the reference

There is less gold ornament, fewer spires and no hanging banners off the rim yet. These get added in the Unity pass.

## Next

Once the look is approved, build the Unity scene at `unity/Assets/LastBell/Areas/Area1/SkywardSpire_Arena.unity` with the prefab root `LB_ENV_SkywardSpire_Arena`.
