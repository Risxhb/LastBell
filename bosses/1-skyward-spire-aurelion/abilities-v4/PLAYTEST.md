# Aurelion v4 (mythic): playtest

*Proposal build on `integrate/aurelion` (Azure PR 2). Design: `ABILITIES-v4.md`. Player guide: `GUIDE.md`.*

v4 is the only Aurelion fight on every difficulty. The older v1–v3 sets were retired on 1 Nov, and `-lastbellAbilitySet` no longer exists.

## Start a fight, click by click

1. Run `aurelion-azure\unity\Builds\Integrate\LastBell.exe`.
2. On the main menu, click **Difficulty** until it reads **Hard** (or Easy / Medium).
3. Click **Boss Rush**.
4. Click the **Skyward Spire** arena card, then **Continue**.
5. Click the **Aurelion** card.
6. Under **Start Phase**, click **Phase 1** for the whole fight, or **Phase 2**, **3** or **4** (Last Light) to start there with full health and potions.
7. Click **Launch Test Rush**.

With bot allies: click **Allies (bots)** on the boss step until it shows the number you want (0–7). For bots that attack too, start the exe from a terminal with `-lastbellAllies 3 -lastbellAllyAttack`.

## The fight

Boss HP: 12,000 solo, plus 1.25 × 12,000 for each extra fighter (57,000 for 4 players).

Warning windows: Medium is 0.85 × the base time (since 1 Nov, halfway to Hard), Hard 0.7 ×, Easy 1.25 ×. Medium and Hard keep the same minimum times; for example the Lances lock 1.2 s → 1.02 s on Medium.

| Phase | HP | What's in it |
|---|---|---|
| 1 Perfect Order | 100–70 % | Paired from the pull: Ecliptic + Lances, Towers + Starfall, Sunspots + Starfall, Collapse + Lances, Towers + Marks, Marks + Lances |
| Intermission: Total Eclipse | at 70 % | Stay in the moving light wells; break the Corona Shield in 25 s or the party wipes |
| 2 Misalignment | 70–40 % | Three at once. **Hard: Meridian Recall** after the first volley |
| 3 Exposed Meridian | 40–20 % | Severance, Cascading Lances + Starfall + Hour Hands (**Hard: Meridian Recall** after it), Sunspots + Collapse, Towers + Lances + Starfall |
| 4 Last Light | 20–0 % | A slice falls into the sky every 12 s (every 10 s from the 5th); the 8th ends the fight |

**Last Light holes:** step onto one and you fall, landing on the nearest floor for 40 (Hard 52).

**Final Noon:** the hard enrage at **6:30** (warned at 6:00). The boss bar shows the clock.

## New in this build

- **Meridian Recall (Hard only):** he fires his last Lances volley again, once, from where he stood when he first fired it, on the same lines. The beams show locked for 3.5 s, then hit for 65. Stand in a gap between the old lines.
- **Last Light's floor falls:** each slice trembles through its warning, then drops into the sky below. **Step onto a hole and you fall:** you land at once on the nearest standing floor and take 40 (Easy 24, Hard 52). One fall never kills from full health; two in a row can. A jump carries you over a hole; a dash does not.
- **Start Phase** row in Boss Rush (step 6 above).
- **Allies (bots)** button in Boss Rush, beside Launch Test Rush: click it to cycle 0–7 bots.
- **Total Eclipse wells** are now thin, dotted cobalt circles with no fill, like the towers: places to stand in, not things to dodge.
- **Last Light camera:** for the whole phase the camera keeps Aurelion in view with you, pulling back as he gets further away.

## Keys

| Key | What it does |
|---|---|
| **F6** | Practice one ability on loop. The v4 ones come last; Meridian Recall loops a volley and then its replay. |
| **F8** | Next phase. |
| **F9** | Defeat the boss. |

## Tell us

1. Did you wipe? To what? Did you always know why?
2. Is the 6:30 enrage reachable at full HP solo on Hard?
3. Meridian Recall: could you remember the old lines? Too easy, fair, or unfair?
4. Which mechanic felt unfair rather than hard?
