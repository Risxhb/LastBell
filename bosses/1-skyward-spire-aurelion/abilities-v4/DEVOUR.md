# A17 Devour (accepted 2 Nov, Chzy18)

Aurelion v4, every difficulty. It follows Zenith Collapse and is fed by Solar Hunger. It is an original adaptation of Dimensius's Devour, with no copied numbers. The telegraph audit lists it as A17.

Chzy18's change on accepting it: "walking doesn't beat the pull".

## What it does
- When a Zenith Collapse lands, Aurelion breathes in the light for **2.0 s**. During that breath every standing player is **pulled toward him**, and the pull stops at his feet.
- The pull grows with each Collapse in the fight, like Solar Hunger: **6.5, then 7.0, then 7.5 m/s** (the cap) on Medium and Hard. **Easy is a fixed 6.5 m/s.**
- **Walking (6 m/s) loses to the pull and sprinting (9 m/s) beats it.** Sprinting gains 1.5–2.5 m/s outward.
- **Dash:** there is no immunity, so the pull keeps acting through a dash. One 3 m dash still nets about 1.5 m outward against 7.5 m/s.
- The pull **deals no damage** and never pushes.
- **The danger is the Meridian Lances, which land 1.2 s after the Collapse.** Their gaps open 5 m out, and the pull drags a walking player toward the middle, where the beams cross and stack. Each beam is its own hit; Chzy18 decided on 2 Nov that a volley is not capped.

## Where it plays
Devour follows every Zenith Collapse:
- Phases 1 and 2: Collapse + Lances.
- Phase 3: Sunspots, then Collapse.

It never plays in Total Eclipse or Last Light, which have no Collapse.

## How it looks
- A **DEVOUR** banner shows for the 2.0 s.
- Gold dust streams in from the rim to his chest along 16 spokes, at the pull's own speed (`DevourFx.cs`).
- There is no coral ground footprint, because the pull itself never hurts.

## Rules check
- **No one-shot:** the pull deals no damage, and each Lances beam is a separate hit.
- **No class needed:** every class can sprint and dash.
- **No immunity:** the pull acts through a dash.
- **1–4 players:** the pull is per player and isn't scaled by party size.

## Code
- The encounter is `AurelionEncounter.V4.cs` (`StartDevour` and `StepDevour`). The pull runs on the encounter tick, so the timing of the blocks is unchanged.
- Ally bots sprint whenever they move against the pull.
- The test is `RunAutoTest.Aurelion.V4.cs`, which checks the speeds and a 0.5 s pull distance.

## Timing (Option A, accepted 2 Nov)
The 2.0 s pull keeps running through the Lances landing at 1.2 s, so players have to sprint or dash out. Option B, stopping the pull when the Lances land, was rejected.
