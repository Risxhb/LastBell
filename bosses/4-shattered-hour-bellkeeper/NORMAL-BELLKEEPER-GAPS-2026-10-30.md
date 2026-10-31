# The normal Bellkeeper against the design rules (proposal, 30 Oct)

Status: **proposal**. Nothing here is built. Sources: `gpt/08-design-rules.md` (29 Oct), `unity/Assets/LastBell/Run/Encounters/BellkeeperEncounter.cs` (`ScriptV1`), `BellkeeperV2.cs` (`RequestedSet`), and `RunDirector.cs` (`Bosses`).

## What "the normal Bellkeeper" is in the build

The run's final slot is the entry `"Bellkeeper"` (`final: true`, in runs). It spawns `BellkeeperEncounter` with the **v1** set (spec v0.3), because `RequestedSet` falls back to v1 when no flag is given. The v2 set is retired and can only be reached with `-lastbellAbilitySet v2`. The v1 fight has three HP phases (70 % / 35 %), 3600 HP, and these attacks:
- Bell Sweep: a 6.5 m cone.
- Hollow Toll: a ring from 2.6 to 6.8 m.
- Pendulum March: a 2.2 m lane.
- Shattered Chime: three 1.4 m circles that follow players.
- The Unfinished Hour: replays one recorded attack.
- The Final Toll: a fixed sequence of the above.

## Gaps

1. **The run still ends on a retired fight (§1, §5).** Since 25 Oct the Bellkeeper "is fought only in its Ultimate version; the normal v2 fight is retired". The build's run, however, ends on the v1 fight, which is older than v2. This is the only gap that matters if the rule stands. The rest of this list applies only if a normal fight is kept.
2. **No 3–4 s punish window (§5).** The longest recovery is 2.0 s (`Recovery = 120`, after the echo and after the Final Toll). P1's window after the chimes is 0.9 s (`Vulnerable(54)`).
3. **No second form (§5).** The final boss must change into a second form partway through. The v1 fight has only three phase shifts, each 1.5 s and in the same look.
4. **Ranged players are not in enough danger (§5).** The rule is that at least 3 of 4 core attacks hit both a player hugging the boss and one at 8 m or more.
   - Bell Sweep (6.5 m) and Hollow Toll (6.8 m outer edge) never reach 8 m.
   - Pendulum March and Shattered Chime target players, so they reach any range.
   - That makes 2 of 4. Nothing is anchored to the arena edge or comes in from it, so standing at range is safe outside the March and the Chimes.
5. **A safe spot at the boss during Hollow Toll.** The toll's inner 2.6 m hole is safe, and the Toll is the only attack in its block. This is allowed by the letter of §5 (melee keeps the Sweep), but it is the "permanently safe spot" pattern when the Toll repeats in Phase 2.
6. **No Hard mechanic (§5).** v1 never reads `CombatWorld.Difficulty`. The level only scales damage taken, and there is no one named extra mechanic on Hard.
7. **No co-op scaling (§5).** v1 has 3600 HP for every party size: `FitHpToParty` runs only for v2 and the Ultimate. There are no spatial jobs, soaks or spread targets, so 8 players fight the same 3600 HP as 1.
8. **Length (§5).** 3600 HP is the shortened test bar. No measured-DPS length exists for a final boss outside the Ultimate.
9. **Old comments.** The file header still cites "two damaging patterns at most", a rule removed on 26 Oct. This is not a rule break, but it is misleading.

Not checked here: telegraph style against `Reference/Telegraph-Style/` v2, and the look.

## Proposal

**Option A (recommended): the run's final slot fights the Ultimate.** The `"Bellkeeper"` run entry spawns the Ultimate at the run's level (Easy, Medium or Hard, already built and tested solo with every class), and the v1 fight leaves the run. This follows the settled 25 Oct rule and needs no new boss work. The v1 fight could stay as a practice-only "Bellkeeper (classic)" entry on the test map, which §7 says may become a tutorial, or be removed.

**Option B: keep a normal fight for runs.** This needs Chzy18 to reopen the 25 Oct rule, then fixes for gaps 2–8: a 3–4 s recovery, a second form, an edge-anchored attack, a Hard mechanic, party scaling and a measured HP length. That is a full boss lane's work.

## Open questions

1. A or B?
2. Under A, does the run's Ultimate use the run's level exactly as the practice entry does (the Hard baseline, scaled down for Easy and Medium)?
3. Under A, should the v1 fight stay as a practice or tutorial entry, or be removed?
4. Under A, should there be a final shop before the Bellkeeper? (Still open in §1.)
