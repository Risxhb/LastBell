# Arcanist redesign: design

Status: **built, October 26, 2026** (Risxhb with risxhb/claude), in `unity/Assets/LastBell/Run/Combat/ArcanistKit.cs` and `PlayerFx.Arcanist.cs`. Revised the same day after the first playtest: the Astral Orb pet is gone, replaced by the **glyph** core mechanic and **Arcane Surge** (see [Core mechanic: glyphs](#core-mechanic-glyphs)). Every number is a starting value to tune in play.

## Identity

- **Role:** ranged glass cannon. No defensive ability; survival comes from Blink and positioning.
- **Theme:** pure arcane wizard. Runes, glyphs, astral light. The time-magic theme is dropped.
- **Signature prop:** the astrolabe floating at his right shoulder (`orbit` bone). Its rings act as a lens for Disintegrate, and Arcane Surge pulls the glyphs into it.
- **Rotation:** channel Disintegrate on the boss to carve glyphs, spend them on full-power Rune Sigils and Arcane Comets, or cash them all into Arcane Surge. Missiles fill the gaps.
- **Damage:** lower than the current kit (open playtest note PF-08: too much damage). Basic attack range goes up slightly (open range complaint).

## Colour

Every spell is violet, so each one uses its own shade and brightness:

| Spell | Colour |
|---|---|
| Magic Missile | Pale lilac body, white-hot tip |
| Disintegrate | Deep ultraviolet core with a near-black sheath; magenta-violet erosion edges on the ash |
| Rune Sigil | Mid violet lines, gold-white when armed |
| Glyphs | Rune violet with a gold edge, orbiting his waist |
| Arcane Surge | The same violet with gold accents ("the glyphs, released") |
| Arcane Comet | Violet body with a white core and a long trail; violet-white impact |
| Blink | Violet motes and shards |

Player effects never use filled glowing floor discs, so they can't be mistaken for boss warnings. Boss warnings must not use these violets.

## Kit

| Slot | Key | Ability | Role |
|---|---|---|---|
| Basic | LMB | Magic Missile | Constant damage |
| 2 | 2 (hold) | Disintegrate | Steered beam he channels; **builds glyphs** |
| 3 | 3 | Rune Sigil | Short-delay skill shot (0.8 s); **spends 1 glyph** |
| 4 | 4 | Arcane Comet | Long-delay skill shot (3 s), the big hit; **spends 1 glyph** |
| 5 | 5 | Arcane Surge | Empowerment; **spends every glyph** |
| Dodge | RMB | Blink | Shared dash rules, teleport look |

**Controls:** WASD moves, so the mouse buttons carry the basic attack and dodge, and the number keys carry the spells. The basic attack is **left mouse only** (tap and hold), the dodge is **right mouse only**, and the spells are **2, 3, 4, 5**. Key 1 and key 6 are unused. Spells aim at the mouse cursor when the key is pressed.

### Magic Missile (basic, LMB)

**Rules**
- **Tap LMB:** fires one missile straight at the aim point. It can miss. About 10 damage, repeating about every 0.25 s while tapping.
- **Hold LMB:** summons missiles one at a time, about 0.25 s apart, up to 3 (5 in Surge, 0.17 s apart). Once all are summoned they wait. Release fires them 0.05 s apart.
- **Charged missiles fly straight at the cursor** (changed after the playtest; they used to home). They can miss. About 13 damage each.
- He moves at about 50% speed while charging. A dash cancels the charge, and the summoned missiles are lost.
- Range: about 18 m (current Clockbolt is 15 m).
- Tapping and charging both come to about 40 damage a second. In Surge a tap fires two missiles side by side.

**Animation:** reuse the alternating-hand Clockbolt / Clockbolt2 clips for taps. Charging needs a held pose with the casting hand raised and drawn back.

**Effect**

| Stage | Look |
|---|---|
| Charge | Each missile appears as a lilac dart hovering in a fan behind his shoulder, near the astrolabe. A glyph ring at the casting hand fills in thirds, one per missile. |
| Release | The glyph flashes. Charged missiles leave their spots in the fan and fly straight at the cursor, a little spread. A tap fires one dart straight from the hand. In Surge the darts have gold edges. |
| Missile | White-hot arrowhead tip, lilac body, a short ribbon trail shedding a few sparkles, a slight weave in flight. Small and sharp. |
| Contact | A small starburst, a few rune shards, one thin ring. The three charged hits land about 0.05 s apart. |
| Cancel | The hovering darts collapse inward and the glyph cracks. No release flash. |

### Disintegrate (2)

**Rules**
- Press 2 to start. He channels a beam for about 2 s; releasing 2 early ends it. It turns toward the cursor at a capped turn speed.
- He moves at about 20% speed while channelling. A dash cancels it.
- Damage builds in 3 steps the longer the beam is held (8, 12, 16 per hit, every 1/6 s).
- **Each step completed while the beam is hitting the boss carves a glyph** (max 3). A full channel on the boss gives all 3; a dash that breaks the beam keeps the glyphs already carved.
- Cooldown about 8 s.

**Animation** (new clips; the start of the existing ArcLance clip can stand in for the windup)
- **Start (about 0.35 s):** he plants his back foot and thrusts his casting arm forward, palm open. His off hand grips that wrist to hold back the recoil. The astrolabe swings in front of the hand.
- **Hold (loop):** his arm trembles slightly. His robe and sleeves are pushed back by the force (the robe's cloth chains). His head is turned slightly away from the glare. The astrolabe rings spin faster at each damage step.
- **End:** his arm snaps back and he shakes out his hand.
- **Broken:** he staggers back half a step.

**Effect**

| Stage | Look |
|---|---|
| Windup | The astrolabe rings line up into a lens. Motes are drawn into his palm; a small glyph forms at the lens. |
| Release | A very thin ray snaps out with a near-black ultraviolet sheath. A sharp ring kicks out from the lens. |
| Beam | Thin bright core inside a flickering dark-violet sheath, with two strands twisting along it. It gets thicker at each damage step, so others can see how far into the channel he is. |
| Contact | The signature piece. The boss's surface flakes into dark ash that drifts and falls, each flake eroding with a glowing magenta-violet edge. A few hard sparks, and a dark scorch rather than a bright flash. |
| End | The beam thins to a thread and goes out; the last ash drifts. |
| Cancel | The beam stutters, breaks into separate pieces that fade, and the lens rings spin apart. |

### Rune Sigil (3)

**Rules**
- Placed at the cursor. The rune is drawn on the floor stroke by stroke, then arms and explodes **0.8 s** after it is placed (0.4 s in Surge).
- **Spends one glyph** for full damage (55). Cast without a glyph, the rune is drawn faint and deals 35% (19). Free in Surge.
- Damages the boss if the boss is inside it when it explodes. Small radius (about 2.5 m), so it is a skill shot.
- Cooldown about 6 s.

**Animation:** a quick drawing gesture with the casting hand, as if tracing the rune in the air.

**Effect**

| Stage | Look |
|---|---|
| Drawing | Thin violet strokes draw the rune on the floor. The drawing is the timer. |
| Armed | The lines light up from the inside out and turn gold-white. |
| Explosion | A short column of violet light, rune pieces thrown outward, one thin ring. |
| Ending | Burnt rune lines fade from the floor. |

### Core mechanic: glyphs

- Disintegrate is the builder: every damage step that hits the boss carves one glyph, up to 3. They stay until spent (no decay).
- Rune Sigil and Arcane Comet each spend one glyph for full power. Without one they still cast but deal 35% and draw a faint rune, so a missed build is visible and costs damage.
- Arcane Surge spends all of them at once.
- **Look:** a faint socket ring at his waist (0.75 m) with three sockets; each glyph is a small violet rune standing at its socket, turning with the ring (faster with more glyphs). Gaining one: a spark races back along the beam and the glyph draws itself. Spending one: it arcs into the rune's centre and lands with a gold flash. At 3 the glyphs pulse together. Prompt: E7 in [arcanist-fx-prompts.md](arcanist-fx-prompts.md).

### Arcane Surge (5)

**Rules**
- Needs at least one glyph. A 0.3 s cast (release at 0.17 s); a dash before the release cancels it for free and keeps the glyphs.
- Spends every glyph for **2 s of Surge per glyph** (2, 4 or 6 s). Cooldown 15 s.
- While surging: the missile charge summons 5, taps fire two, and Rune Sigil and Arcane Comet cost no glyph and arm or fall twice as fast (0.4 s and 1.5 s).

**Animation:** the ArcaneSurge clip if present, otherwise the old AstralOrb release clip stands in (he raises his free hand to the astrolabe and opens it).

**Effect** (prompt E8)

| Stage | Look |
|---|---|
| Cast | The glyphs are pulled into the astrolabe; a ring tightens on it. |
| Release | A gold-violet column through him and a double shockwave on the floor, both larger with each glyph spent. |
| Active | A gold dashed ring at his feet, a gold chest ring that empties as the timer, a crown of the spent glyphs above his head, a faint rim of light, rising motes. Empowered missiles have gold edges. |
| Last second | Everything flickers. |
| End | The rings and crown fold inward to a small glint. |
| Cancel | The glyphs snap back to their sockets. |

### Arcane Comet (4)

**Rules**
- Marks a spot at the cursor. The mark stays where it was placed. The comet lands **3 s** later (1.5 s in Surge), so the player has to predict where the boss will be.
- **Spends one glyph** for full damage (90, bullseye 180). Without a glyph it deals 35%. Free in Surge.
- Bigger radius than the Rune Sigil (about 4 m) and his biggest single hit. The inner third of the radius (the bullseye) deals double damage, rewarding precise timing.
- Cooldown about 18 s.
- The Rune Sigil is the quick, small skill shot; the Comet is the slow, big one.

**Animation:** he raises both arms to the sky, then pulls them down hard as if dragging the comet out of the sky. The Convergence clip can stand in at first.

**Effect**

| Stage | Look |
|---|---|
| Mark | A large rune drawn on the floor over the whole fall (its outer ring is the timer), faint without a glyph. A ground shadow tightens as the comet nears. |
| Descent | The comet starts high out of view and streaks in across the screen in the last part of the fall: a big burning rock (charred faceted stone with molten violet cracks and a hot leading face), violet flames streaming off its back edge, a fire plume and tail, embers and a smoke trail. |
| Impact | The meteor bursts, not the rune: the rune is snuffed to ash over about 0.12 s, while flame tongues and a fire bloom burst out of the crater, the meteor's own charred fragments are thrown out (landing within about 5 m), thin pale shockwaves race out, smoke rolls up, and a short real-light pulse fires. |
| Aftermath | A cracked crater mark with glowing violet seams that erode away over about 1.5 s; a few embers rise. |

### Blink (dodge, RMB)

**Rules:** the shared dash, unchanged (`PlayerController`: 3 m over 0.2 s, 30 stamina; like every class it has no invincibility frames). Only the look changes: he is hidden for the dash and seen at the start and end. No instant teleport, so he can't skip hazards the other classes can't.

**Animation:** one clip, **BlinkIn**: he arrives folded (half-crouch, arms crossed over the chest) and unfolds; robe and sleeves flare out, then settle. There is no departure clip: the shared dash has no windup, so his body is hidden on the press and the departure is drawn by the effect from a snapshot of his current pose.

**Effect**

| Stage | Look |
|---|---|
| Departure | A small glyph ring flashes at his feet. His body breaks apart into violet motes and a few angular shards, leaving a hollow silhouette that erodes in about 0.15 s. |
| Travel | Hidden. A faint thin violet thread runs along the path with a few motes moving down it. |
| Arrival | The motes rush inward and rebuild him from the feet up. A thin ring at his feet, a brief small light, one soft pulse on the astrolabe. |
| Blocked | If the dodge is stopped, his body flickers and snaps back together where he stands, with no arrival ring. |

Keep him hidden for as short a time as possible; players lose track of their own character quickly.

## How the effects should be built

Use the layered method from the Ashen Covenant spell guide (Priest v002):
- **Stages:** each spell is designed as preparation, release, contact, active state and ending. Success and cancel get different endings.
- **Construction:** small procedural meshes (rings, tubes, ribbons, shards) plus camera-facing billboards in bounded counts. A flowing-noise shader with erosion and a glowing erosion edge.
- **Timing:** effects are posed from game time, so pausing freezes them and captures repeat exactly.
- **Light:** keep the brightest core small, with a visible coloured middle layer. Real light ends within about 0.3 s.
- **Rules stay separate:** gameplay rules stay in `ArcanistKit`. Effects only present events (`PlayerFx.Arcanist.cs`).

## Open items

- All numbers are starting values to tune against the bosses, including against PF-08.
- Clips built (in `Arcanist.glb`): missile charge/volley, Disintegrate start/hold/end/broken, Rune Sigil draw, Comet call, BlinkIn. An ArcaneSurge clip is still to make; the AstralOrb clip stands in.
- Check Disintegrate's dark-violet sheath against dark arenas (Shattered Hour), and the lilac missiles against Verdant Chimes.
- Controls: the shared input (`KeyboardMouseInput` in `PlayerController.cs`) was changed for every class on October 26 to match the table above (basic LMB only, dash RMB only, slot 5 on 5).
