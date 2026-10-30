# Area 4 detail pass: audit checklist

Status: proposal (environment art only). This is the starting audit for `SHATTERED-HOUR-DETAIL-PASS-PROMPT.md`, taken from build `ade14c7` (critic 6.5/10). "Proc" means procedural geometry in the builder; "Tex" means a C#-painted texture plus normal map.

| Object group | Now | Target | Approach |
|---|---|---|---|
| Shared helpers | Unity cube primitives, flat colours, UVs that smear on curved walls | Bevelled boxes everywhere, arc-length UVs, profile sweeps, jagged lathe, mesh consolidation for draw calls | Proc |
| Trim sheets | One brick texture, no normals | Stone-block trim (courses 0.6 m, chamfers, chips, AO) plus normal map; aged-brass and rock-strata sheets | Tex |
| Combat floor | Even Voronoi "turtle shell", heavy grout | Large quiet slabs, radial rings inside r 7.5, running bond outside, ±8 % per slab, soft thin grout, edge wear, drift | Tex (4096 albedo, 2048 normal) |
| Brass inlay | Flat painted lines | Bevelled engraved look with a dark centre line, medallion micro-ticks, fleur ray tips; stars, diamonds and arrows 30 % larger; visible rim ticks | Tex |
| Fissure | Gold emission over dark albedo (done) | Plus tiny stone chips on the lips; fixed 0.12–0.22 m width | Tex |
| Parapet | Brick prism, flat cap | Stone trim, swept gothic cap moulding with drip edge, brass bead plus band, blind arcading on the outer face | Proc + Tex |
| Drum | Plain boxes | Stepped buttresses with sloped caps, tracery windows (mullion plus rose), every third glowing faintly, brass band with numeral studs | Proc |
| Rock spire | Flat colour | Painted strata | Tex |
| Pinnacles | Box shaft plus 4-sided spire | Ribs, gablets, octagonal crocketed spire with brass leaves, orb plus cross finial, 3 heights | Proc |
| Lanterns | Box flame | Glass panes, roof spire, ring handle, 2× flame | Proc |
| Rim stations | Flat disc plus dome | Stepped plinth, two brass rings, 2048 engraved dial (ticks, runes, numerals) plus normal, finial, four gothic posts, tracery gate | Proc + Tex |
| South entrance | Boxes plus flat banners | Bevelled treads plus stringers, flagstone causeway, moulded walls with buttress pinnacles, gatehouses with blind tracery, folded swallowtail banners on rods | Proc |
| North stair | Boxes | Nosings, moulded walls, pedestal landing with engraved brass ring; three steps so the re-tilted bell clears | Proc |
| E/W bridges | Boxes | Flagstone deck, moulded walls with post spires, corbels, under-arch, tracery gateway fragments, ragged end | Proc |
| Bell | 72-segment lathe, clean slice, plain bronze | 128-segment lathe with a 41-point profile, jagged break, painted bronze 2048 plus normal with glyph bands, tapered branching groove cracks, yoke crown, clapper, tilt −45° so the mouth reads | Proc + Tex |
| Astrolabe and rings | Flat band, plates | Degree scale, rete (eccentric rings plus star pointers), hub pointer, drifting fragments at the break | Proc |
| Floating ruins | Box C-shapes | Gothic modules: clustered columns, bases, brass-banded capitals, pointed arches with inner order, moulded entablature; fewer and larger, pushed back | Proc |
| Rocks, chains, hanging bells | Flat colour, separate objects | Strata texture, consolidated chains, bells with clapper | Proc + Tex |
| Void | Flat purple | Nebula bands behind the bell and at the horizon, a stronger vortex, warm shafts behind the bell, layered fog | Shader |
| Lighting | Key plus ambient | Plus cool fill from the south-east and a cool rim from the north; lower ambient; brass edge highlights | Scene |
| Performance | About 1,500 renderers | Consolidated static groups; stats written to `Shots/stats.txt` | Proc |

Build order note: to save Unity batch time, the steps are grouped into three builds (helpers + floor + rim; stations + entrances + bell; void + lighting). Every build renders all shots.
