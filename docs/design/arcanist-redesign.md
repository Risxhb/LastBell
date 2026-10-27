# Arcanist redesign: design

Status: **agreed concept, October 26, 2026** (Risxhb with risxhb/claude). Nothing is built yet. Every number is a starting value to tune in play. The current kit (`unity/Assets/LastBell/Run/Combat/ArcanistKit.cs`: Clockbolt, Time Seal, Arc Lance, Convergence, Temporal Anchor) stays in the game until this one replaces it.

## Identity

- **Role:** ranged glass cannon. No defensive ability; survival comes from Blink and positioning.
- **Theme:** pure arcane wizard. Runes, glyphs, astral light. The time-magic theme is dropped.
- **Signature prop:** the astrolabe floating at his right shoulder (`orbit` bone). Its orb becomes a weapon (Astral Orb), and its rings act as a lens for Disintegrate.
- **Damage:** lower than the current kit (open playtest note PF-08: too much damage). Basic attack range goes up slightly (open range complaint).

## Colour

Every spell is violet, so each one uses its own shade and brightness:

| Spell | Colour |
|---|---|
| Magic Missile | Pale lilac body, white-hot tip |
| Disintegrate | Deep ultraviolet core with a near-black sheath; magenta-violet erosion edges on the ash |
| Rune Sigil | Mid violet lines, gold-white when armed |
| Astral Orb shots | Silver-white with a violet edge (starlight) |
| Arcane Comet | Violet body with a white core and a long trail; violet-white impact |
| Blink | Violet motes and shards |

Player effects never use filled glowing floor discs, so they can't be mistaken for boss warnings. Boss warnings must not use these violets.

## Kit

| Slot | Key | Ability | Role |
|---|---|---|---|
| Basic | LMB | Magic Missile | Constant damage |
| 2 | 2 | Disintegrate | Steered beam he channels |
| 3 | 3 | Rune Sigil | Short-delay skill shot |
| 4 | 4 | Astral Orb | 8 s of automatic fire |
| 5 | 5 | Arcane Comet | Long-delay skill shot, the big hit |
| Dodge | RMB | Blink | Shared dash rules, teleport look |

**Controls:** WASD moves, so the mouse buttons carry the basic attack and dodge, and the number keys carry the spells. The basic attack is **left mouse only** (tap and hold), the dodge is **right mouse only**, and the spells are **2, 3, 4, 5**. Key 1 and key 6 are unused. Spells aim at the mouse cursor when the key is pressed.

### Magic Missile (basic, LMB)

**Rules**
- **Tap LMB:** fires one missile straight at the aim point. It can miss. About 10 damage, repeating about every 0.25 s while tapping.
- **Hold LMB:** summons missiles one at a time, about 0.25 s apart, up to 3. Once all three are summoned they wait. Release fires them 0.05 s apart.
- **Charged missiles home** on the boss and always hit. About 13 damage each.
- He moves at about 50% speed while charging. A dash cancels the charge, and the summoned missiles are lost.
- Range: about 18 m (current Clockbolt is 15 m).
- Tapping and charging both come to about 40 damage a second. Tapping rewards aim; charging lands every hit while he dodges.

**Animation:** reuse the alternating-hand Clockbolt / Clockbolt2 clips for taps. Charging needs a held pose with the casting hand raised and drawn back.

**Effect**

| Stage | Look |
|---|---|
| Charge | Each missile appears as a lilac dart hovering in a fan behind his shoulder, near the astrolabe. A glyph ring at the casting hand fills in thirds, one per missile. |
| Release | The glyph flashes. Charged missiles burst outward, then curve in toward the boss in an arc. A tap fires one dart straight from the hand. |
| Missile | White-hot arrowhead tip, lilac body, a short ribbon trail shedding a few sparkles, a slight weave in flight. Small and sharp. |
| Contact | A small starburst, a few rune shards, one thin ring. The three charged hits land about 0.05 s apart. |
| Cancel | The hovering darts collapse inward and the glyph cracks. No release flash. |

### Disintegrate (2)

**Rules**
- Press 2 to start. He channels a beam for about 2 s; releasing 2 early ends it. It turns toward the cursor at a capped turn speed.
- He moves at about 20% speed while channelling. A dash cancels it.
- Damage builds in 3 steps the longer the beam is held.
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
- Placed at the cursor. The rune is drawn on the floor stroke by stroke, then arms after about 0.6 s and explodes.
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

### Astral Orb (4)

**Rules**
- Charge-up about 0.75 s. He moves at about 50% speed while it charges. A dash cancels it without spending the cooldown.
- Active for 8 s. The orb floats above his shoulder and fires a bolt at the boss every 1 s (8 shots), about 8 damage each. It fires on its own while he does anything else.
- Cooldown 10 s from the cast (80% uptime). If it should feel like a big moment instead, raise the cooldown to about 15 s.

**Animation:** he raises his free hand to the astrolabe and opens it, releasing the orb. The start of the current TimeSeal clip can stand in.

**Effect**

| Stage | Look |
|---|---|
| Charge | The astrolabe rings spin faster and spread apart. Motes are drawn into the orb as it brightens; a thin ring tightens around it to show progress. |
| Release | The rings flash and the orb lifts free with a small burst of light. The empty rings keep turning slowly at his shoulder. |
| Active | The orb floats with two thin rings crossing around it. Before each shot it contracts slightly, then pulses and fires. |
| Bolt | Small, round, silver-white with a violet edge. Tiny trail. |
| Impact | A tiny star-shaped burst, no ring, so 8 shots don't clutter the boss. |
| Last 1.5 s | The orb flickers and its rings slow. |
| Ending | The orb drifts back into the astrolabe and the rings settle. |

### Arcane Comet (5)

**Rules**
- Marks a spot at the cursor. The mark stays where it was placed. The comet lands about **2 s** later, so the player has to predict where the boss will be.
- Bigger radius than the Rune Sigil (about 4 m) and his biggest single hit. The inner third of the radius (the bullseye) deals double damage, rewarding precise timing.
- Cooldown about 18 s.
- The Rune Sigil is the quick, small skill shot; the Comet is the slow, big one.

**Animation:** he raises both arms to the sky, then pulls them down hard as if dragging the comet out of the sky. The Convergence clip can stand in at first.

**Effect**

| Stage | Look |
|---|---|
| Mark | A thin violet crosshair ring with a small bullseye ring at the cursor. Lines only, no filled disc. |
| Descent | The comet is visible high in the sky from the cast, falling at a steep angle with a long violet trail. Its approach is the timer. The mark's outer ring closes in toward the bullseye as it falls. |
| Impact | A white flash at the core, a shockwave ring along the floor, rock and crystal shards thrown outward, a short real-light pulse (gone by about 0.3 s). |
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
- New clips needed: Missile charge hold, Disintegrate start/hold/end/broken, Rune Sigil draw, Astral Orb release, Comet call, Blink departure/arrival. Stand-in clips are named above.
- Check Disintegrate's dark-violet sheath against dark arenas (Shattered Hour), and the lilac missiles against Verdant Chimes.
- Controls: the shared input (`KeyboardMouseInput` in `PlayerController.cs`) was changed for every class on October 26 to match the table above (basic LMB only, dash RMB only, slot 5 on 5).
