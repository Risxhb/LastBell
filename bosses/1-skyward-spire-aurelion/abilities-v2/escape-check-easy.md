# Aurelion ability set v2: escape check (Easy)

Produced by `tools/encounter-escape/aurelion-v2-escape-check.ps1 -Difficulty Easy` on 2026-10-22 00:59. Arena: Skyward Spire play bound 13.9 m, boss inside 9.5 m, boss body 1.1 m + player radius 0.45 m, hit margin 0.3 m. Reaction 0.25 s is included in every escape time. Margin = window - fastest route at 4.2 m/s; the brief asks for 0.5-0.8 s.

| Case | Worst escape (m) | Window (s) | 4.2 m/s walk / walk+dash (s) | 6 m/s walk / walk+dash (s) | Margin at 4.2 (s) | Verdict | Worst configuration |
|---|---|---|---|---|---|---|---|
| Meridian Lances, 3 beams, volley 1 (tracks 1.5 s, locks) | 1.7 | 1.5 | 0.65 / 0.36 | 0.53 / 0.36 | 1.14 | pass | boss r=0, aim 15 deg, worst (-1.1, 1.1) |
| Meridian Lances, 3 beams, volley 2 / cascade (tracks 1.25 s, locks) | 1.7 | 1.5 | 0.65 / 0.36 | 0.53 / 0.36 | 1.14 | pass | boss r=0, aim 15 deg, worst (-1.1, 1.1) |
| Meridian Lances, 4 beams, volley 1 (tracks 1.5 s, locks) | 2.4 | 1.5 | 0.82 / 0.41 | 0.65 / 0.41 | 1.09 | pass | boss r=0, aim 0 deg, worst (-1.1, -1.1) |
| Meridian Lances, 4 beams, volley 2 / cascade (tracks 1.25 s, locks) | 2.4 | 1.5 | 0.82 / 0.41 | 0.65 / 0.41 | 1.09 | pass | boss r=0, aim 0 deg, worst (-1.1, -1.1) |
| Starfall Mirror, own star (N 1-4, r 2.2) | 2.5 | 1.375 | 0.85 / 0.42 | 0.67 / 0.42 | 0.96 | pass | centre of your own star |
| Starfall Mirror, own star (N 5-8, r 1.8) | 2.1 | 1.375 | 0.75 / 0.39 | 0.6 / 0.39 | 0.98 | pass | centre of your own star |
| Noon Shadow (half arena) | 14 | 4.75 | 3.58 / 3.14 | 2.58 / 2.35 | 1.61 | pass (loose) | edge point opposite the line (13.7, -2.3) |
| Orrery Closing (rim + crown) | 7.65 | 2.875 | 2.07 / 1.62 | 1.52 / 1.29 | 1.25 | pass (loose) | boss r=6, worst (13.7, -0.3) |
| Hour Hands (Hard), own spoke | 1.2 | 1 | 0.54 / 0.33 | 0.45 / 0.33 | 0.67 | pass | on your spoke's centre line |
| Pairing: Noon + Starfall, edge runner at 4.2 m/s | 14.2 | 4.75 | 3.18 | - | 1.57 | pass | star locks 13 m into the run; exiting it takes 0.6 s on the same line |
| Pairing: Noon + Starfall, edge runner at 6 m/s | 14.2 | 4.75 | 2.38 | - | 2.37 | pass | star locks 19 m into the run; exiting it takes 0 s on the same line |
| Pairing: Horizon + Starfall, N = 4 on a 6 m circle | 2.55 | 1.375 | 0.86 / 0.42 | 0.67 / 0.42 | 0.96 | pass | every player to a spot clear of all stars and mirrors, inside the rim, outside the crown |
| Pairing: Horizon + Starfall, N = 8 on a 6 m circle | 2.1 | 1.375 | 0.75 / 0.39 | 0.6 / 0.39 | 0.98 | pass | every player to a spot clear of all stars and mirrors, inside the rim, outside the crown |
