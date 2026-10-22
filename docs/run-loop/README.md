# Last Bell — playable run loop (overnight handoff, October 21, 2026)

**Owner:** chzy18/claude, operator Chzy18. **Branch:** `feature/run-loop`, stacked on `feature/class-test-arena` (draft PR #27). **Engine:** Unity 6000.6.6f1, URP 17.6.0, glTFast 6.20, Input System 1.20. No package or pipeline changes. Asset manifest: [asset-manifest.md](asset-manifest.md).

## What's playable end to end

Menu → **Preparation** (Ready) → **Dial Warden** → **Pendulum Sentinel** → **Chimebound Oracle** → **Intermission** (future-shop placeholder, Continue) → **The Bellkeeper** (three HP phases) → **Results** (victory or defeat, with death recap and damage meter) → **Retry Run** (fresh run at encounter 1) or **Main Menu**. There's also **Practice** for any single encounter, which never advances a run, and a **Pause** menu (Esc) with Resume / Restart / Main Menu.

It runs on any of five maps (see [Maps](#maps)); the default is the key-art test map, `Assets/LastBell/Run/RunArena.unity`, a copy of `KeyArtTestArena.unity` with a `RunDirector`. Three classes are selectable: **Vanguard** (approved four-slot kit), **Chimesinger** (approved DPS kit) and **Arcanist** (approved hard rotation, #41; added Oct 21 from the Arcanist session's handover).

## Maps

The menu has a **Map** row (or press **[** / **]**) to pick which arena to test. Encounters, bounds and camera are identical on every map: all five arenas share the same layout convention (centre origin, entry to the south at +z, floor at y = 0, walkable radius of at least 13.5 m), and the run keeps its 11.5 m play bound everywhere.

| Map | Source scene | Run scene |
| --- | --- | --- |
| **Test Map** (Bellkeeper's Dais, key art; boots here) | `Arenas/KeyArt/KeyArtTestArena.unity` | `Run/RunArena.unity` |
| **Skyward Spire** (Area 1) | `Areas/Area1/SkywardSpire_Arena.unity` | `Run/Maps/Run_SkywardSpire.unity` |
| **Brass Foundry** (Area 2) | `Areas/Area2/BrassFoundry_Arena.unity` (generated, not committed) | `Run/Maps/Run_BrassFoundry.unity` |
| **Verdant Chimes** (Area 3) | `Areas/Area3/VerdantChimes_Arena.unity` | `Run/Maps/Run_VerdantChimes.unity` |
| **Shattered Hour** (Area 4, final arena) | `Areas/Area4/ShatteredHour_FinalArena.unity` | `Run/Maps/Run_ShatteredHour.unity` |

- The list lives in `Run/RunMaps.cs`. **Last Bell → Run Loop → Rebuild Run Scenes (all maps)** copies each source scene, hides the look-check props (`ScaleReference_Vanguard`, `TelegraphCheck`, `TelegraphTest`), adds a `RunDirector` and puts all five in the build settings. A missing source scene is generated with its area builder first.
- Start on a given map from the command line: `LastBell.exe -lastbellMap brass-foundry` (ids: `test`, `skyward-spire`, `brass-foundry`, `verdant-chimes`, `shattered-hour`). This works with `-lastbellAutotest` too.
- The arena art is owned by the four area sessions (Areas 1-4). After one of them rebuilds its arena, rerun Rebuild Run Scenes.

## Difficulty

The menu has a **Difficulty** row: **Easy**, **Medium** or **Hard** (or press **D** to cycle). Chzy18 chose three levels on 2026-10-22, replacing the four provisional tiers. Medium is the baseline every boss is authored for.

- The choice is remembered between sessions. `LastBell.exe -lastbellDifficulty hard` overrides it for one launch without saving. The autotest always starts on Medium and never saves.
- A run or practice copies the level when it starts. It can't change mid-run, and a retry keeps it. The HUD footer, Ready panel and results screen show it.
- **What it changes today:** only damage taken from boss attacks, ×0.6 on Easy and ×1.3 on Hard (`RunDifficulty.DamageTakenScale`). It's applied once, in `PlayerController.ReceiveHit`, so echoes that copy a recorded attack's damage aren't scaled twice. These values are provisional.
- **Per-boss changes:** encounter code can read `CombatWorld.Difficulty` (or `RunDirector.Difficulty`), for example for longer Easy warnings or Hard's one named extra mechanic (design rules §5). Change a boss's own schedule there rather than stretching hazards centrally, so its movement stays in sync with its hit tests.

## Launch

- **Windows build:** `run-loop/unity/Builds/Windows/LastBell.exe` (not committed; about 425 MB). Double-click `Play Run Loop.cmd` in the repo root.
- **Rebuild:** in Unity, **Last Bell → Run Loop → Build Windows Player**. In batch mode:
  `Unity.exe -batchmode -projectPath unity -executeMethod LastBell.Run.EditorTools.RunBuild.BuildWindows`
- **In the editor:** **Last Bell → Run Loop → Open Run Scene**, then press Play.
- **Autotest:** `LastBell.exe -lastbellAutotest -lastbellOut <folder>` (or the editor: `-executeMethod LastBell.Run.EditorTools.RunBuild.PlayModeAutotest -lastbellAutotest -lastbellOut <folder>`). It writes `autotest-report.txt`, screenshots and video frames. `tools/run-loop/frames_to_mp4.py` (Blender) turns the frames into MP4s.

## Controls (shown in the menu, the HUD and the skill tooltips)

| Input | Action |
| --- | --- |
| WASD | move (camera-relative) |
| Mouse | aim; every ability fires toward the cursor |
| 1 / LMB (hold to repeat) | Basic: Steel Rhythm / Resonant Note |
| 2, 3, 4 | Vanguard: Sundering Blow, Last Word, **hold** Brace. Chimesinger: Bellstrike, Harmonic Refrain, Crescendo. Arcanist: Time Seal, Arc Lance, Convergence |
| 6 | Chimesinger: Mending Chorus · Arcanist: Temporal Anchor (press again to return) |
| 5 / RMB | dash (Clockstep / Harmonic Step) |
| Space / Shift | jump / sprint (shared movement stamina) |
| F | drink a potion (+40 HP) |
| Enter | Ready / Continue / Start run |
| [ / ] | menu: previous / next map |
| D | menu: next difficulty (Easy → Medium → Hard) |
| Tab | damage meter (this encounter) |
| Esc | pause |
| R / M | Retry / Main menu on the results screen |
| F9 / F8 | **dev:** defeat the current boss / jump it to its next phase |

Gamepads aren't read: the user's rudder pedals register as a controller. UI clicks can't trigger attacks: gameplay input is blocked in menus and stays disarmed until every action button has been released once after a menu closes.

## Run rules used (provisional where noted)

- **Movement** (Game-Design delegated defaults): walk 6 m/s, sprint 9 m/s, dash 3 m in 0.2 s, jump about 0.8 m over 0.6 s. Stamina 100: sprint 25/s, jump 20, dash 30, regenerates 20/s after 0.75 s.
- **No invincibility frames** anywhere. A hazard instance hits a player at most once, which replaces the old 0.75 s post-hit immunity. A hit causes 0.25 s stun and 1.5 m knockback (spec v0.2 §5).
- **Health and potions (Chzy18, Oct 21):** health refills before every boss after the first. Potions (F: +40 HP, 1 s apart, 5 per refill) carry over between the minibosses, and both health and potions refill before the area's final boss (the Bellkeeper). A fresh run starts full. Stamina, cooldowns and class state (Opening, Harmony, bells) reset before each encounter.
- **Dodging does not make you immune** (Chzy18, Oct 21; settles PF-01): a dash only avoids a hit by leaving the shape before it resolves.
- **Defeat precedence:** if the player goes down on the same tick the boss dies, defeat wins (plan §10 / spec §4).
- **Bellkeeper:** spec v0.2 Steady Ticking schedule. D1–D6 are applied provisionally: 6.5 m sweep, damage 30/25/30/15 (45 for the Final Toll sweep), phases at 70 % and 35 % queued to block boundaries.
- **Boss HP is shortened for this test build:** minibosses 1500 / 1700 / 1700, Bellkeeper 3600. Design durations are 60–105 s and 3–4 min, so tuning is still open.
- **Minibosses:** first-pass schedules with labelled stand-in geometry (encounter lane #8 to author):
  - Dial Warden: Hand Sweep cone 6 m and Hollow Dial ring 2.4–6 m.
  - Pendulum Sentinel: Charge lane 2.2 m wide, and Low Pendulum (a teal **JUMP** bar 1.4 m wide that can be jumped or gone around).
  - Chimebound Oracle: three Shattered Chime markers, then the Dividing Chime cone.

## Per-character status

| Character | Model | Rig | Animation | VFX |
| --- | --- | --- | --- | --- |
| **Vanguard** | Rough production (Meshy multi-view, decimated to 40k tris) | Rough production (scripted armature, heat weights) | Rough production. **New tonight:** SteelRhythm1–3, SunderingBlow, LastWord, BraceRaise / Hold / Walk / Hit / Lower, Hit, Down, GetUp. No placeholders are needed in the run except Land | Rough production: slashes, Opening ring, Sundering split, Last Word windup / slam / shockwave, Brace disc |
| **Chimesinger** | Rough production, **Unity copy is a stand-in** (pre-fix f84c4d5 export). The real export is owned by the Chimesinger animation-fixes session | Rough production | Approved-kit clips exist. **Placeholder:** Hit / Down / GetUp (procedural) until that session adds them | Rough production: note, bolt, bell mark with strength ring and echo pips, Refrain ripples, Crescendo burst, Chorus ring, note trail |
| **Arcanist** | Rough production (Meshy body plus astrolabe, from the Arcanist session) | Rough production (robe cloth chains, astrolabe orbit bone) | Approved-kit clips from the Arcanist session. **Placeholder:** Hit / Down / GetUp | Rough production: bolt, seal dials (ripe ring and inscribed gem as shapes), Lance (normal and empowered), Convergence (early and perfect), Anchor marker / return / shield, Clockstep afterimage |
| **Bellkeeper** (= "Bellwarden") | Rough production (Meshy) | Rough production | Rough production. **New tonight:** PhaseShift, Defeat | Existing telegraphs and resolves; gold (non-danger) phase-shift and defeat bursts |
| Minibosses ×3 | **Placeholder** (code-built) | none | none (scripted transforms) | shared telegraphs and resolves |

## Tests actually run (evidence in `run-loop-evidence/`, not committed)

- **Windows player build:** succeeded seven times tonight (0 errors). The packaged exe launched and ran the full autotest each time.
- **Autotest in the packaged build, latest run (build 15, after removing the old test code): 107/107 passed, 0 errors logged.** Copy: [media/autotest-report-build15.txt](media/autotest-report-build15.txt). Arcanist coverage: Tick cap, Clockstep cancelling an unfinished Lance with Ticks kept, seal placement and charges, the empowered Lance inscribing the oldest seal, two separate seals on one target, both ripe together, a perfect Convergence (300) against an early one (30) resolving exactly once, Anchor record and return, the shield absorbing a hit, and a full Arcanist run to victory. Video: media/arcanist_rotation.mp4. Build 7 (84/84) is kept for the media. It covers:
  - boot to menu in the arena
  - repeated Ready, Continue and Retry presses (each starts exactly one transition)
  - all four encounter slots, each started and ended exactly once
  - boss death during a windup on all four bosses (pending hazards cancelled in the same tick, no damage or resolve afterwards, boss collision off)
  - intermission free of hazards; a potion heals +40 with a 1 s lockout; health refills before miniboss 2 while the spent potion stays spent; health and potions both refill for the final boss
  - Bellkeeper phase 2 and 3 transitions with the PhaseShift and Defeat clips playing
  - victory after slot 4; retry after victory and after defeat both give a fresh run (HP/stamina full, cooldowns reset, no stale bosses, hazards or projectiles, one player)
  - real-hazard defeat with its cause; return to menu from pause leaves no run objects
  - a second full run with the Chimesinger; practice that never advances a run
  - Vanguard kit: Opening from hit 3; dash cancelling an unreleased Sundering Blow with no cooldown spent; empowered Sundering Blow consuming Opening and taking 2 s off Last Word; Brace frontal −60 % with no knockback; rear hits bypassing Brace; Brace absorption granting Opening
  - Low Pendulum avoided by jumping
  - Chimesinger kit: Bellstrike charges, bell, refresh and strengthen; echo cap of two; Crescendo resolving exactly once; the full sequence out-damaging early detonation (196 vs 50); instant self-inclusive heal; heal and dash not clearing setup
  - approved Vanguard clips actually playing
  - zero errors or exceptions in the player log
- **Unity compilation:** clean apart from pre-existing obsolete-API warnings in other files.
- **Editor Play Mode** (same autotest, `RunBuild.PlayModeAutotest`, GUI editor launched from the command line): 83/84 passed. The only failure is the "no errors logged" check, tripped by one Unity-internal exception at editor startup (`UnityEditor.Search.SearchDatabase.EnumerateAll` during search indexing), not by game code. Report: `run-loop-evidence/editor-playmode/autotest-report.txt`.
- **Not tested:** co-op or networking (on hold), gamepad, other resolutions, performance profiling, and human feel or readability review. The bot uses scripted input, not a human.

Screens and videos: `run-loop-evidence/autotest-7/*.png` and `*.mp4` (vanguard_kit, low_pendulum_jump, bellkeeper_fight, bellkeeper_defeat, chimesinger_rotation). Contact sheets: `run-loop-evidence/vanguard/kit-contact-sheet.png`, `run-loop-evidence/bellkeeper-phase-defeat-sheet.png`.

## Playtest feedback (PF, wiki, October 21) addressed in this branch

- **PF-02** (key 4 froze the Vanguard): in the old test controller, key 4 was the superseded 1.85 s Guard pose. In the run, key 4 is **hold-to-Brace**: it's capped at 1.5 s, you can walk slowly, it lowers on release, and its exit after interruption, death and reset is tested.
- **PF-03** skill bar: bindings, cooldown fill and seconds, charges, Opening / Harmony / bell state, and a dash slot.
- **PF-04** hover tooltips on every skill, including the dash.
- **Skill-bar icons** (Codex, #43 / PR #45, merged into this branch): 18 icons covering every class ability, the class dashes and the potion. Each tile shows the keybind, the icon, and a state line (seconds, charges, potions); a cooldown darkens the tile from the top. Screenshot: [media/skill-bar-icons.png](media/skill-bar-icons.png).
- **PF-05** responsiveness:
  - a 0.15 s input buffer for abilities
  - after a basic hit connects, a ready ability cancels the rest of the swing (Basic 3 → Sundering Blow)
  - Last Word recovery shortened from 0.6 s to 0.4 s after the release
  - dash cancels basics and unreleased windups

  Feel still needs a human playtest.
- **PF-06** death recap: the last six hits with the killing blow marked.
- **PF-07** first pass: a per-ability damage meter (damage, share, hits, largest hit, DPS) for the encounter (Tab) and the run (results). This is not Details! parity: no history segments, charts or damage-taken breakdown yet.
- **PF-01** (dodge doesn't negate damage): **settled by Chzy18 (Oct 21): dodging does not make you immune.** No change; a dash avoids a hit only by carrying you out of the shape before it resolves.

## Provisional decisions to review

1. ~~Health policy~~: decided by Chzy18 (Oct 21). Health refills before each boss after the first; potions carry over between minibosses; both refill before the final boss.
2. Boss HP is shortened for testing.
3. Bellkeeper D1/D2 values are applied before review.
4. Chimesinger tuning is entirely prototype: notes 16 dmg, Bellstrike 40 dmg with 2 charges and 6 s recharge, bell lasts 8 s, strength ×1.5 up to twice, Refrain 34 dmg with echoes worth 60 % (4 s cooldown), Crescendo 12 per Harmony + 50 bell + echoes (5 s cooldown), Chorus +30 HP within 6 m (15 s cooldown).
5. Arcanist tuning is prototype:
   - Clockbolt: 14 damage, +1 Tick on hit only.
   - Time Seal: 10 damage, 2 charges, 5 s recharge, and a missed seal still spends its charge.
   - Arc Lance: 28 damage (46 empowered), and an empowered Lance spends Ticks even on a miss.
   - Convergence: each seal deals 30, ×2 if ripe and ×2 if inscribed, plus 60 for a perfect pair; expired seals burst for 8; 4 s cooldown.
   - Temporal Anchor: 4 s return window, 30-point shield for 2 s, 12 s cooldown.

   **Measured schedule:** Seal A → three bolts → empowered Lance → Seal B took **1.52 s**, leaving a **0.48 s shared ripe window** for Convergence. That's feasible but tight. The perfect payoff is 300 damage against 30 for an early detonation. Both need playtesting.
6. Vanguard damage values (Steel Rhythm 22/22/42, Sundering 70 (+60 empowered), Last Word 140 + 90 shockwave) and frame timings are prototype.
7. Mending Chorus and Temporal Anchor are on key **6**, because those classes have five actions plus a dash.
8. Brace mitigates `Strike` hazards (cones, lanes, bars) and never `Ground` ones (rings, chime circles).

## Known bugs and limits

- The Chimesinger in Unity is the pre-grip-fix export. Its Hit, Down and GetUp are procedural tilts.
- The Down / GetUp silhouettes are rough. Land is the tail of Jump.
- The camera follows closely, so the player can sit behind a tall boss for a moment.
- The minibosses have no art and no animation.
- There's no audio.
- Pausing stops gameplay, but idle animations and FX keep moving.
- Recreating the effect layers on retry crashed the player natively in `Mesh.set_colors`. They're now created once and reset between runs, but the root cause isn't understood.

## Next steps (highest priority first)

1. **Human playtest** of this build: feel (PF-05), Brace (PF-02) and readability. Decide the Bellkeeper D1–D6. (PF-01 and the health policy were settled on Oct 21.)
2. Swap in the **fixed Chimesinger GLB** when the Chimesinger session delivers it, then add its Hit / Down / GetUp.
3. Author the **three miniboss** models and schedules (encounter lane #8), replacing the stand-ins without changing the run flow.
4. Tune boss HP and damage toward the design fight lengths, and run the escape checker on the miniboss schedules.
5. An animation polish pass on the Vanguard ability clips (contact clearance, Down / GetUp), and Hit / Down / GetUp for the Arcanist and the Chimesinger.
6. Move the shared run/hazard code behind the foundation #6 contract (U3) when it lands.

## Media

Screenshots, videos and contact sheets from the gameplay camera are in [media/](media/): menu, Dial Warden combat, Low Pendulum JUMP warning, intermission, Bellkeeper Final Toll, victory and defeat results (with death recap and meter), Chimesinger full setup, plus vanguard_kit.mp4, chimesinger_rotation.mp4, low_pendulum_jump.mp4, bellkeeper_defeat.mp4.
