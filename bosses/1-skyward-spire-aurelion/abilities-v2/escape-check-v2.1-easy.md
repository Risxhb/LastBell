# Aurelion ability set v2.1: escape check (Easy)

Produced by `tools/encounter-escape/aurelion-v2-escape-check.ps1 -Difficulty Easy` on 2026-10-22 14:50. Arena: Skyward Spire play bound 13.9 m, boss inside 9.5 m, boss body 1.1 m + player radius 0.45 m, hit margin 0.3 m. Reaction 0.25 s is included in every escape time. Routes: walk, walk + one dash, sprint at 9 m/s (25 stamina/s). Margin = window - fastest route at 4.2 m/s walk; pass needs >= 0.3 s (v2.1: Hard >= 0 on the fastest route, Medium and Easy >= 0.3 s).

| Case | Worst escape (m) | Window (s) | 4.2 m/s walk / walk+dash / sprint (s) | 6 m/s walk / walk+dash (s) | Margin at 4.2 (s, route) | Verdict | Worst configuration |
|---|---|---|---|---|---|---|---|
| Meridian Lances, 3 beams, volley 1 (tracks 1.5 s, locks) | 1.7 | 1.5 | 0.65 / 0.36 / 0.44 | 0.53 / 0.36 | 1.14 (dash) | pass | boss r=0, aim 15 deg, worst (-1.1, 1.1) |
| Meridian Lances, 3 beams, volley 2 / cascade (tracks 1.25 s, locks) | 1.7 | 1.5 | 0.65 / 0.36 / 0.44 | 0.53 / 0.36 | 1.14 (dash) | pass | boss r=0, aim 15 deg, worst (-1.1, 1.1) |
| Meridian Lances, 4 beams, volley 1 (tracks 1.5 s, locks) | 2.4 | 1.5 | 0.82 / 0.41 / 0.52 | 0.65 / 0.41 | 1.09 (dash) | pass | boss r=0, aim 0 deg, worst (-1.1, -1.1) |
| Meridian Lances, 4 beams, volley 2 / cascade (tracks 1.25 s, locks) | 2.4 | 1.5 | 0.82 / 0.41 / 0.52 | 0.65 / 0.41 | 1.09 (dash) | pass | boss r=0, aim 0 deg, worst (-1.1, -1.1) |
| Starfall Mirror, own star (N 1-4, r 2.2) | 2.5 | 1.38 | 0.85 / 0.42 / 0.53 | 0.67 / 0.42 | 0.96 (dash) | pass | centre of your own star |
| Starfall Mirror, own star (N 5-8, r 1.8) | 2.1 | 1.38 | 0.75 / 0.39 / 0.48 | 0.6 / 0.39 | 0.98 (dash) | pass | centre of your own star |
| Noon Shadow / Afternoon (half arena) | 14 | 4.75 | 3.58 / 3.14 / 1.81 | 2.58 / 2.35 | 2.94 (sprint (39 st)) | pass | edge point opposite the line (13.7, -2.3) |
| Orrery Closing (rim + crown) | 7.65 | 2.88 | 2.07 / 1.62 / 1.1 | 1.52 / 1.29 | 1.78 (sprint (21 st)) | pass | boss r=6, worst (13.7, -0.3) |
| Hour Hands, own spoke | 1.2 | 1.25 | 0.54 / 0.33 / 0.38 | 0.45 / 0.33 | 0.92 (dash) | pass | on your spoke's centre line |
| Zenith Collapse, safe disc 4.5 m | 9.75 | 3.75 | 2.57 / 2.12 / 1.33 | 1.88 / 1.64 | 2.42 (sprint (27 st)) | pass | rim point (-13.5, -3.3) |
| Zenith Collapse, safe disc 5 m | 9.25 | 3.75 | 2.45 / 2 / 1.28 | 1.79 / 1.56 | 2.47 (sprint (26 st)) | pass | rim point (-13.5, -3.3) |
| Zenith Collapse, safe disc 5.5 m | 8.75 | 3.75 | 2.33 / 1.89 / 1.22 | 1.71 / 1.47 | 2.53 (sprint (24 st)) | pass | rim point (-13.5, -3.3) |
| Zenith Collapse, safe disc 6.5 m | 7.75 | 3.75 | 2.1 / 1.65 / 1.11 | 1.54 / 1.31 | 2.64 (sprint (22 st)) | pass | rim point (-13.5, -3.3) |
| Pairing: Noon + Starfall, edge runner (walk 4.2 m/s) | 14.2 | 4.75 | 3.63 / 3.18 / 1.83 | - | 2.92 | pass | running straight across clears the star on the same line |
| Pairing: Noon + Starfall, edge runner (walk 6 m/s) | 14.2 | 4.75 | 2.62 / 2.38 / 1.83 | - | 2.92 | pass | running straight across clears the star on the same line |
| Pairing: Horizon + Starfall, N = 4 on a 6 m circle | 2.55 | 1.38 | 0.86 / 0.42 / 0.53 | 0.67 / 0.42 | 0.96 (dash) | pass | every player to a spot clear of all stars and mirrors, inside the rim, outside the crown |
| Pairing: Horizon + Starfall, N = 8 on a 6 m circle | 2.1 | 1.38 | 0.75 / 0.39 / 0.48 | 0.6 / 0.39 | 0.98 (dash) | pass | every player to a spot clear of all stars and mirrors, inside the rim, outside the crown |
| Pairing (v2.1): Lances + Starfall (star window kept) | 4.1 | 1.38 | 1.23 / 0.78 / 0.71 | 0.93 / 0.7 | 0.66 (sprint (11 st)) | pass | own star centre, then the nearest beam gap |
| Pairing (v2.1): Zenith Collapse (4.5 m) + Lances (3 beams) | 10.3 | 3.75 | 2.7 / 2.25 / 1.39 | 1.97 / 1.73 | 2.36 (sprint (29 st)) | pass | aim 15 deg, from (-13.5, -3.3) |
| Pairing (v2.1): Zenith Collapse (5.5 m) + Lances (3 beams) | 9.15 | 3.75 | 2.43 / 1.98 / 1.27 | 1.77 / 1.54 | 2.48 (sprint (25 st)) | pass | aim 15 deg, from (-13.5, -3.3) |
| Pairing (v2.1): Zenith Collapse (6.5 m) + Lances (3 beams) | 8.05 | 3.75 | 2.17 / 1.72 / 1.14 | 1.59 / 1.36 | 2.61 (sprint (22 st)) | pass | aim 7.5 deg, from (-13.7, -2.1) |
| Pairing (v2.1): Zenith Collapse (5 m) + Lances (4 beams) | 9.7 | 3.75 | 2.56 / 2.11 / 1.33 | 1.87 / 1.63 | 2.42 (sprint (27 st)) | pass | aim 15 deg, from (-13.5, -3.3) |
