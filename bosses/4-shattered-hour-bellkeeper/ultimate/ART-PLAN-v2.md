# Ultimate v2: art and modelling plan (proposal)

Chzy18, 25 Oct: "start modelling everything out, from the mechanics, to the ability hints, to the stage transitions."

Every v2 mechanic currently draws with stand-in primitives (`Prim` cubes, cylinders and discs). This plan replaces them
with real models and effects in the house style (`gpt/02-visual-style-guide.md`, `gpt/06-icons-ui-vfx.md`).

**How it's made**
- **Models:** Blender Python scripts build each prop, painterly low-poly with 2–3 tone bands, and export a GLB (the
  Crucivar pipeline: `tools/crucivar/*`).
- **Loading:** the GLBs live in `Resources/Ultimate/` and load at runtime, as the character models do.
- **The code doesn't change behaviour:** each mechanic keeps its rules and hit areas. Only its `Build`/`Present` swap
  primitives for models.
- **2D icons:** made with the ChatGPT Project art pipeline (`gpt/07-prompt-templates.md`). I write the prompts; Chzy18
  generates the images.

**The three readability rules** (from the style guide)
- **Hostile:** coral/amber, filled, crisp edges.
- **Friendly or yours:** thin, dotted, unfilled, teal/gold.
- **Decor:** quiet during warnings.

## Order of work (one batch at a time, each shown in a clip before the next)

1. **P4 · The Gear Floor**, the heaviest stand-in:
   - outer and centre gears: brass rims, enamel faces, real teeth, spokes;
   - the gold cores;
   - the pawls: brass ratchet arms with coral tips;
   - the mesh teeth, with the countdown ring as a brass dial bezel;
   - **the hammer:** a giant bell-hammer that swings down onto the gear (today it is only a red disc);
   - the shards: brass splinters.
2. **The transitions:**
   - **The Floor Breaks:** slate floor tiles that crack along the gear outlines and fall away into the void.
   - **The Train Runs Wild:** outer gears that tear off and tumble away; **the great bell** model descending over the
     arena and closing out the sky.
3. **P5 · Final Toll:**
   - the bell's interior: inner wall, rim, floor;
   - **the clapper:** a hanging clapper that swings through the lane;
   - the sound ring and echo ring as resonance linework;
   - the plate, the debt porcelain rings and the hammer burst point;
   - **the Echo Bellkeeper:** a translucent teal-blue ghost of the boss model;
   - the last-stand platforms and the bell shattering into falling shards;
   - the final "THE LAST BELL" crack.
4. **P1–P3:**
   - P1: the brass plates, the hand (a real clock hand), the TICK/TOCK counterweights (brass weights on chains) and the
     pendulum chain;
   - P2: the gear-train chains between partners and the INVERTED bell marker;
   - P3: the Mainspring arms, the echo discs and the bell curtains.
5. **Ability hints:**
   - the job glyphs over heads and in the HUD: SOAK, TICK, TOCK, CHAIN, INVERTED, GAP I–IV, platform;
   - the practice beam as a thin teal dotted ring;
   - the gold LIVE halo for the Echo/Boss;
   - one icon per mechanic for the HUD hint line.

## Decided (Chzy18, 26 Oct)
- **Job glyphs:** made with the ChatGPT art project. The prompts are in `CHATGPT-PROMPTS-2026-10-26-ULTIMATE-GLYPHS.md`
  (18 icons, saved to `Reference/Ultimate-Glyphs/`). The words stay beside them at first.
- **Camera exception:** a short 1–2 s camera pull-back and return is allowed for the stage transitions (the great bell
  falling, the bell shattering). Gameplay otherwise keeps the fixed camera.
