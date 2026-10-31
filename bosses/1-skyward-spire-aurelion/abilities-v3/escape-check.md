# Aurelion ability set v3 (Milestone A): escape check

Produced by `tools/encounter-escape/aurelion-v3-escape-check.ps1` on 2026-10-22 17:49. Bound 13.9 m, hit margin 0.3 m, reaction 0.25 s, dash 3 m / 0.2 s, sprint x1.5. Margin = window - the cheapest route (least stamina) that meets the level's target; st = stamina it spends. Targets: Easy 0.8, Medium 0.5, Hard 0.3 s.

| Level | Case | Worst escape (m) | Window (s) | Margin at 4.2 m/s | Margin at 6 m/s | Verdict | Worst configuration |
|---|---|---|---|---|---|---|---|
| Easy | Ecliptic Order, strike I from the reveal (N 1, disc 3 m) | 16.2 | 4 | 1.42 (82 st) | 1.05 (0 st) | pass | rim point opposite anchor I |
| Easy | Ecliptic Order, next strike (N 1) | 10 | 2.75 | 1.12 (58 st) | 0.83 (0 st) | pass | far side of the last disc, order 4b, 90->270 deg |
| Easy | Ecliptic Order, strike I from the reveal (N 4, disc 3.5 m) | 15.7 | 4 | 1.42 (82 st) | 1.13 (0 st) | pass | rim point opposite anchor I |
| Easy | Ecliptic Order, next strike (N 4) | 10 | 2.75 | 1.12 (58 st) | 0.83 (0 st) | pass | far side of the last disc, order 4b, 90->270 deg |
| Easy | Ecliptic Order, strike I from the reveal (N 8, disc 4.5 m) | 14.7 | 4 | 1.58 (78 st) | 1.3 (0 st) | pass | rim point opposite anchor I |
| Easy | Ecliptic Order, next strike (N 8) | 10 | 2.75 | 1.12 (58 st) | 0.83 (0 st) | pass | far side of the last disc, order 4b, 90->270 deg |
| Easy | Zenith Marks, SHADE from the rim | 8.7 | 4 | 1.68 (0 st) | 2.3 (0 st) | pass | rim, boss at the centre |
| Easy | Zenith Marks, SUN from beside his body | 7.05 | 4 | 2.07 (0 st) | 2.58 (0 st) | pass | touching his body |
| Easy | Zenith Marks two-step, SHADE then SUN (beside his body to beyond 8.5 m) | 7.05 | 2.75 | 0.82 (0 st) | 1.32 (0 st) | pass | touching his body |
| Easy | Zenith Marks two-step, SUN then SHADE (rim to within 5.5 m) | 8.7 | 2.75 | 0.88 (30 st) | 1.05 (0 st) | pass | rim, boss at the centre |
| Easy | Pairing: SHADE + Lances, side-step out of the beam that locked on you in the shade | 1.4 | 1.5 | 0.92 (0 st) | 1.02 (0 st) | pass | on the beam's centre line, 3 m from him |
| Medium | Ecliptic Order, strike I from the reveal (N 1, disc 3 m) | 16.2 | 3.2 | 0.62 (82 st) | 1.24 (66 st) | pass | rim point opposite anchor I |
| Medium | Ecliptic Order, next strike (N 1) | 10 | 2.2 | 0.57 (58 st) | 0.52 (30 st) | pass | far side of the last disc, order 4b, 90->270 deg |
| Medium | Ecliptic Order, strike I from the reveal (N 4, disc 3.5 m) | 15.7 | 3.2 | 0.62 (82 st) | 0.57 (30 st) | pass | rim point opposite anchor I |
| Medium | Ecliptic Order, next strike (N 4) | 10 | 2.2 | 0.57 (58 st) | 0.52 (30 st) | pass | far side of the last disc, order 4b, 90->270 deg |
| Medium | Ecliptic Order, strike I from the reveal (N 8, disc 4.5 m) | 14.7 | 3.2 | 0.78 (78 st) | 0.5 (0 st) | pass | rim point opposite anchor I |
| Medium | Ecliptic Order, next strike (N 8) | 10 | 2.2 | 0.57 (58 st) | 0.52 (30 st) | pass | far side of the last disc, order 4b, 90->270 deg |
| Medium | Zenith Marks, SHADE from the rim | 8.7 | 3 | 0.68 (0 st) | 1.3 (0 st) | pass | rim, boss at the centre |
| Medium | Zenith Marks, SUN from beside his body | 7.05 | 3 | 1.07 (0 st) | 1.58 (0 st) | pass | touching his body |
| Medium | Zenith Marks two-step, SHADE then SUN (beside his body to beyond 8.5 m) | 7.05 | 2.2 | 0.72 (30 st) | 0.78 (0 st) | pass | touching his body |
| Medium | Zenith Marks two-step, SUN then SHADE (rim to within 5.5 m) | 8.7 | 2.2 | 0.73 (54 st) | 0.5 (0 st) | pass | rim, boss at the centre |
| Medium | Pairing: SHADE + Lances, side-step out of the beam that locked on you in the shade | 1.4 | 1.2 | 0.62 (0 st) | 0.72 (0 st) | pass | on the beam's centre line, 3 m from him |
| Hard | Ecliptic Order, strike I from the reveal (N 1, disc 3 m) | 16.2 | 3.2 | 0.62 (82 st) | 0.48 (30 st) | pass | rim point opposite anchor I |
| Hard | Ecliptic Order, next strike (N 1) | 10 | 2.2 | 0.57 (58 st) | 0.52 (30 st) | pass | far side of the last disc, order 4b, 90->270 deg |
| Hard | Ecliptic Order, strike I from the reveal (N 4, disc 3.5 m) | 15.7 | 3.2 | 0.62 (82 st) | 0.33 (0 st) | pass | rim point opposite anchor I |
| Hard | Ecliptic Order, next strike (N 4) | 10 | 2.2 | 0.57 (58 st) | 0.52 (30 st) | pass | far side of the last disc, order 4b, 90->270 deg |
| Hard | Ecliptic Order, strike I from the reveal (N 8, disc 4.5 m) | 14.7 | 3.2 | 0.78 (78 st) | 0.5 (0 st) | pass | rim point opposite anchor I |
| Hard | Ecliptic Order, next strike (N 8) | 10 | 2.2 | 0.57 (58 st) | 0.52 (30 st) | pass | far side of the last disc, order 4b, 90->270 deg |
| Hard | Zenith Marks, SHADE from the rim | 8.7 | 3 | 0.68 (0 st) | 1.3 (0 st) | pass | rim, boss at the centre |
| Hard | Zenith Marks, SUN from beside his body | 7.05 | 3 | 1.07 (0 st) | 1.58 (0 st) | pass | touching his body |
| Hard | Zenith Marks two-step, SHADE then SUN (beside his body to beyond 8.5 m) | 7.05 | 2.04 | 0.56 (30 st) | 0.62 (0 st) | pass | touching his body |
| Hard | Zenith Marks two-step, SUN then SHADE (rim to within 5.5 m) | 8.7 | 2.04 | 0.57 (54 st) | 0.34 (0 st) | pass | rim, boss at the centre |
| Hard | Pairing: SHADE + Lances, side-step out of the beam that locked on you in the shade | 1.4 | 0.84 | 0.5 (30 st) | 0.36 (0 st) | pass | on the beam's centre line, 3 m from him |

## Memory test (reflex escape, no reaction, dash + sprint 9 m/s, vs the coral lead + 0.25 s)

| Case | Distance (m) | Reflex escape (s) | Coral lead (s) | Verdict |
|---|---|---|---|---|
| Ecliptic next strike, worst spot in the last disc (far side, closest pair) | 8.66 | 0.83 | 0.35 | memory needed |
| Ecliptic next strike, best spot (near edge, closest pair) | 3.26 | 0.23 | 0.35 | reflex can make it (info: only from the one right spot) |
| Zenith Marks SHADE from the rim | 8.7 | 0.83 | 0.35 | memory needed |
| Zenith Marks SUN from beside his body | 7.05 | 0.65 | 0.35 | memory needed |

## The heaviest block as a route (Ecliptic, 4 strikes, every rim start and order): worst margin and lowest stamina

| Level | Base walk (m/s) | Worst leg margin (s) | Lowest stamina | Verdict | Worst at |
|---|---|---|---|---|---|
| Medium | 4.2 | 0.52 | 12 | pass | order 4b, start 190 deg |
| Medium | 6 | 0.52 | 70 | pass | order 4b, start 65 deg |
| Hard | 4.2 | 0.3 | 18 | pass | order 4b, start 185 deg |
| Hard | 6 | 0.45 | 70 | pass | order 4b, start 40 deg |

**All cases pass.**
