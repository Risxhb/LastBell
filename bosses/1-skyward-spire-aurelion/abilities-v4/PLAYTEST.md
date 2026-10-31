# Aurelion v4 (mythic): playtest

*Proposal build, local branch `feature/aurelion-v3` (v4 commits on top). Design: `ABILITIES-v4.md`.*

**v4 is now the default on every difficulty.**

- `-lastbellAbilitySet v3` brings back the Milestone A set, and `v2` brings back v2.1.
- Full fight: about 6 minutes for a solo ranged player. Boss HP is 16,000.
- `-lastbellBossHp 0.2` makes it short, to see every phase quickly.

## The fight

| Phase | HP | What's new |
|---|---|---|
| 1 Perfect Order | 100–70 % | Each new mechanic alone once: Meridian Towers, Ecliptic Order, Sunspots, Zenith Marks |
| Intermission: Total Eclipse | at 70 % | Stay in the moving light wells; break the Corona Shield in 25 s or the party wipes |
| 2 Misalignment | 70–40 % | Three attacks at once: Sunspots + Starfall, Towers + Marks, Ecliptic + Lances, Orrery + Horizon |
| 3 Exposed Meridian | 40–15 % | Severance: Day and Night (don't cross the turning line), Sunspots + a hungry Collapse, Towers + Lances |
| 4 Last Light | 15–0 % | The floor falls one slice every 15 s; everything at once; no rest window |

**Final Noon:** the fight ends at **7:00** (warned at 6:30). The boss bar shows the clock.

## The new mechanics

- **Meridian Towers:** blue columns numbered I, II…
  - Stand in each one, in order, when it lands.
  - **An empty tower wipes the party, even solo.** A chip of 15 to whoever is inside.
- **Sunspots:** "SUNSPOT I II" over your head.
  - Each one blows up where you stand (heavy hit), 1.5 s apart.
  - Each leaves a burning pool **until the phase ends**. Drop them at the edge, not on paths you'll need.
  - **Co-op:** if a blast catches another carrier, the party wipes.
- **Total Eclipse:** the floor goes dark except the light wells. Outside a well for more than 1 s you burn. Hit him to break the shield.
- **Severance:**
  - A gold line splits the floor and turns 90° over 25 s.
  - Crossing it, or letting it sweep over you, is a heavy hit.
  - **Co-op:** both halves must keep a player, or the party wipes.
- **Solar Hunger:** every Zenith Collapse has a smaller safe disc and hits harder than the last.
- **Last Light:** every 15 s another 45° slice of the floor burns for good. The 8th slice ends the fight.

## Keys

| Key | What it does |
|---|---|
| **F6** | Practice one ability on loop. The v4 ones come last. |
| **F8** | Next phase. |
| **F9** | Defeat the boss. |
| **A** (run menu) | Allies 0–7. Allies now go into towers, light wells and their Severance side by themselves. |

## Tell us

1. Did you wipe? To what? Did you always know why?
2. Is the 7:00 enrage reachable at full HP solo?
3. Which mechanic felt unfair rather than hard?
