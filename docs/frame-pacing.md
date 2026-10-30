# Frame pacing: 60 Hz logic, smoothed drawing, VSync and FPS cap

Added October 26, 2026.

## What changed

- **Logic still steps at 60 Hz** (`CombatWorld.Hz`). Every boss timing, warning, cooldown and hit test counts these ticks, and tests depend on them. This did not change.
- **Drawing is smoothed between ticks** (`Run/Combat/SimSmoother.cs`). The player and every boss are drawn between their last two tick poses, so motion is even at any frame rate. Before this, at 120 fps the character froze on every other frame (measured: 65 of 130 frames still while walking). Turn it off with `-lastbellNoSmoothing` to compare.
- **Settings → VSync** (off by default) and **Settings → FPS cap** (60 / 120 / 144 / 240 / Unlimited, default 120, the old fixed value). They are saved with the other settings and applied at start-up and on Apply (`Menu/MenuSettings.cs`). With VSync on, the cap is ignored.

- **Runs in the background.** The Windows player keeps running when its window loses focus (`runInBackground`, set by `ConfrontationMenuBuild.BuildWindows`; the web demo keeps it off). A fight keeps going while alt-tabbed; there is no automatic pause on focus loss yet.

## To inspect when working on bosses

The FPS options mean frame rates other than 120 now happen, and smoothing means a boss is drawn up to one tick (about 17 ms) behind its logic pose. Gameplay inside ticks is safe, but check these when you next work on each boss:

1. **Frame-time code.** These files use `Update`/`LateUpdate` with `Time.deltaTime`, `Time.time` or unscaled time. Anything there that affects gameplay, or must line up exactly with a tick-based telegraph, could now drift with the frame rate:
   - `Arenas/MetronomeCourt/MetronomeCourtArena.cs` (12 uses: rod swing, beat pulse, lantern chase, lights)
   - `Run/Encounters/TempoTyrant/TempoTyrantRig.cs`, `TempoTyrantFx.cs`, `TempoTyrantHud.cs`, `TempoClock.cs`
   - `Run/Encounters/ChimeboundOracle/ChimeboundOracleRig.cs`
   - `Run/Encounters/SylvaraEncounter.cs`, `BellkeeperEncounter.cs`, `AurelionEncounter.cs`, `Aurelion/Abilities/OrreryClosing.cs`, `CrucivarRig.cs`, `BossEncounter.cs`
   - `Arenas/KeyArtArena.cs`
2. **Visual vs. hit alignment.** A boss's model is drawn up to one tick behind its logic position. For lunges, charges and sweeps that move the boss fast (Aurelion's thrust, Bellkeeper's charge, Crucivar's ram, Tempo Tyrant's dash and wake), check that the swing still looks like it lands where the hit lands.
3. **Effects parented to a boss or the player.** They now move with the smoothed pose. A warning drawn from the logic position (hazards) and an effect attached to the model can be up to one tick apart while moving.
4. **Test runs at other frame rates.** The suites have only been run at the default 120 fps cap. Run a boss's suite once with VSync on and once at 60 and 240 when it is next touched.
5. **Tests that sample a boss or the player every frame** (an `Observe`/`Update` in an autotest) see the drawn pose. To check gameplay state, read `SimSmoother.SimPosition(t)` / `SimRotation(t)` instead (Tempo Tyrant's Severance lock check was the first to need this).
6. **Teleports.** A move of more than 2 m in one tick snaps instead of sliding (`SimSmoother.TeleportDistance`). A boss that blinks a shorter distance will visibly slide; give it an explicit snap if that looks wrong.
