# Bellkeeper's Dais — 3D look prototype

A browser prototype for settling how the classes and the Bellkeeper should look and feel in the final game, before the look is ported to Unity. Double-click `Play 3D Arena.cmd` at the repository root, or open `index.html` in Chrome or Edge. It needs an internet connection (three.js 0.160 and Google Fonts load from CDNs).

## What it shows

- **Arena:** the Bellkeeper's Dais from the art bible §8.4. Cobalt slate floor with broad low-frequency tiles, a low-contrast brass dial that dims during warnings, ornate curb and bell towers outside the playable bounds, the great ring mechanism, suspended spires, a violet cloud ocean and drifting motes.
- **Classes** (art bible §9), built from shapes, toon shading and effects. No modeled art yet.
  - Vanguard: cobalt enamel, semicircular brass guard plate, clock-hand blade, gold slashes and a golden clock-hand Signature.
  - Arcanist: tall collar, split robe tails, floating astrolabe and lenses, straight-line teal beams, and a rooted cast that lifts the astrolabe overhead.
  - Healer, shown as a "Chime Warden": ivory mantle, halo, tuning-fork staff, hovering chimes, dotted friendly rings. It comes with a training ally to keep alive.
- **The Bellkeeper** (art bible §10): hooded bell torso, ivory mask with a single focal glow, a visible pendulum core, segmented arms and a broken halo. It acts out idle, windup (turns toward the attack, coral surge), resolve and recovery.
- **Encounter:** by default the Bellkeeper v1.0 design (HP phases, Pendulum Charge, Shattered Chime, The Final Toll; see `docs/design/bellkeeper-v1-prototype.md`). Esc → Encounter switches to the v0.1 solo timeline from `docs/design/bellkeeper.md` (Echo marks, Sanctuary Peal, 4:00 Final Hour enrage).
- **Telegraphs** are drawn in a shader from the exact simulation geometry (art bible §11). Each has a geometry layer, an identity pattern and a timing pulse.
- **Failure cards** state what failed and why, with a small arena diagram. Guarded, immune and warded outcomes never read as "avoided".

## Controls

WASD move · mouse aims (Vanguard) · 1 / left mouse basic (hold) · 2 / right mouse technique · 3 signature · 4 guard · 5 dash (every class) · 6 heal target (Healer) · hold Shift self-target · R restart · Esc menu (boss health, start phase, invulnerable, knockout rule, ally, range rings, Pendulum Strikes, mouse aim, sound). Gamepad: left stick move, right stick aim, A basic, X technique, Y signature, RB guard, B dash. Standard-layout gamepads only; other HID devices (pedals, flight sticks) are ignored.

The Vanguard is the 2.0 redesign (Clockblade Combo, Pendulum/Reversal Rush, Final Hour, Resonant Guard counters); see `docs/design/vanguard-v2-implementation.md`. `?autoplay=vanguard&inv` runs a test bot.

## Limits

- Class rules mirror `unity/Assets/LastBell/Core/ClassTest` by hand in JavaScript. If the C# kits change, update the numbers here.
- Boss HP (1,800 standard) is a placeholder. With a bot that stands still and attacks, the boss dies at about 2:40 for Vanguard and 2:50 for Arcanist. Healer plus ally is slower.
- Players are pushed out of a 56-unit boss body radius. The legacy rules have no boss collision.
- Everything visual is a target to port to Unity URP, not production assets.
