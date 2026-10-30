# Aurelion ability set v2: escape check (Medium)

Produced by `tools/encounter-escape/aurelion-v2-escape-check.ps1 -Difficulty Medium` on 2026-10-22 00:54. Arena: Skyward Spire play bound 13.9 m, boss inside 9.5 m, boss body 1.1 m + player radius 0.45 m, hit margin 0.3 m. Reaction 0.25 s is included in every escape time. Margin = window - fastest route at 4.2 m/s; the brief asks for 0.5-0.8 s.

| Case | Worst escape (m) | Window (s) | 4.2 m/s walk / walk+dash (s) | 6 m/s walk / walk+dash (s) | Margin at 4.2 (s) | Verdict | Worst configuration |
|---|---|---|---|---|---|---|---|
| Meridian Lances, 3 beams, volley 1 (tracks 1.2 s, locks) | 1.7 | 1.2 | 0.65 / 0.36 | 0.53 / 0.36 | 0.84 | pass | boss r=0, aim 15 deg, worst (-1.1, 1.1) |
| Meridian Lances, 3 beams, volley 2 / cascade (tracks 1 s, locks) | 1.7 | 1.2 | 0.65 / 0.36 | 0.53 / 0.36 | 0.84 | pass | boss r=0, aim 15 deg, worst (-1.1, 1.1) |
| Meridian Lances, 4 beams, volley 1 (tracks 1.2 s, locks) | 2.4 | 1.2 | 0.82 / 0.41 | 0.65 / 0.41 | 0.79 | pass | boss r=0, aim 0 deg, worst (-1.1, -1.1) |
| Meridian Lances, 4 beams, volley 2 / cascade (tracks 1 s, locks) | 2.4 | 1.2 | 0.82 / 0.41 | 0.65 / 0.41 | 0.79 | pass | boss r=0, aim 0 deg, worst (-1.1, -1.1) |
| Starfall Mirror, own star (N 1-4, r 2.2) | 2.5 | 1.1 | 0.85 / 0.42 | 0.67 / 0.42 | 0.68 | pass | centre of your own star |
| Starfall Mirror, own star (N 5-8, r 1.8) | 2.1 | 1.1 | 0.75 / 0.39 | 0.6 / 0.39 | 0.71 | pass | centre of your own star |
| Noon Shadow (half arena) | 14 | 3.8 | 3.58 / 3.14 | 2.58 / 2.35 | 0.66 | pass | edge point opposite the line (13.7, -2.3) |
| Orrery Closing (rim + crown) | 7.65 | 2.3 | 2.07 / 1.62 | 1.52 / 1.29 | 0.68 | pass | boss r=6, worst (13.7, -0.3) |
| Hour Hands (Hard), own spoke | 1.2 | 1 | 0.54 / 0.33 | 0.45 / 0.33 | 0.67 | pass | on your spoke's centre line |
| Pairing: Noon + Starfall, edge runner at 4.2 m/s | 14.2 | 3.8 | 3.18 | - | 0.62 | pass | star locks 10 m into the run; exiting it takes 0.6 s on the same line |
| Pairing: Noon + Starfall, edge runner at 6 m/s | 14.2 | 3.8 | 2.38 | - | 1.42 | pass | star locks 15 m into the run; exiting it takes 0 s on the same line |
| Pairing: Horizon + Starfall, N = 4 on a 6 m circle | 2.55 | 1.1 | 0.86 / 0.42 | 0.67 / 0.42 | 0.68 | pass | every player to a spot clear of all stars and mirrors, inside the rim, outside the crown |
| Pairing: Horizon + Starfall, N = 8 on a 6 m circle | 2.1 | 1.1 | 0.75 / 0.39 | 0.6 / 0.39 | 0.71 | pass | every player to a spot clear of all stars and mirrors, inside the rim, outside the crown |
