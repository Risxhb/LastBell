## Crucivar ability set v2: escape check (Medium)

Arena bound 13.7 m, boss body 1.7 m, hit margin 0.3 m. PASS = reaction margin >= 0.5 s at 4.2 m/s with one 3 m / 0.2 s dash, and at 6 m/s walking.

| Ability | Worst case | Distance (m) | Window (s) | 4.2 walk (s) | 4.2 walk + dash (s) | 6.0 walk (s) | Margin at 4.2 + dash (s) | Result |
|---|---|---|---|---|---|---|---|---|
| Mould Press | N>=2: reach the nearer of the two opposite bays, from anywhere | 12.58 | 4.20 | 3.00 | 2.48 | 2.10 | 1.72 | PASS |
| Mould Press | N=4/8: a bad split sends you to the FAR bay of the pair | 17.38 | 4.20 | 4.14 | 3.62 | 2.90 | 0.58 | PASS |
| Mould Press | N=1: tower I to tower II (adjacent bay), from the far side of I | 8.49 | 2.60 | 2.02 | 1.51 | 1.41 | 1.09 | PASS |
| Gear Chain | partners on the same spot: each walks min / 2 | 3.00 | 3.00 | 0.71 | 0.20 | 0.50 | 2.80 | PASS |
| Gear Chain | partners at opposite edges: each walks (sep - max) / 2 | 8.70 | 3.00 | 2.07 | 1.56 | 1.45 | 1.44 | PASS |
| Gear Chain | solo / odd: standing on its rim post, walk in to min | 6.00 | 3.00 | 1.43 | 0.91 | 1.00 | 2.09 | PASS |
| Gear Chain + Bellows | partners at opposite sides of the slag-edge disc | 4.20 | 3.00 | 1.00 | 0.49 | 0.70 | 2.51 | PASS |
| Bellows Draw | slag rim: from the arena edge inward | 4.55 | 2.00 | 1.08 | 0.57 | 0.76 | 1.43 | PASS |
| Bellows Draw | blast: from the boss body outward, slag edge still burning (boss <= 3 m from centre) | 2.65 | 3.00 | 0.63 | 0.18 | 0.44 | 2.82 | PASS |
| Bellows Draw | after the 1.5 m inhale pull: walk back out before the blast | 1.50 | 0.90 | 0.36 | 0.10 | 0.25 | 0.80 | PASS |
| Great Casting | join the pour line from anywhere (its boss end never moves) | 12.10 | 3.50 | 2.88 | 2.37 | 2.02 | 1.13 | PASS |
| Great Casting | solo: dodge the locked pour lane | 1.60 | 2.00 | 0.38 | 0.11 | 0.27 | 1.89 | PASS |
| Great Casting | marching stamps: leave a stamp without entering a neighbouring spoke (worst ring r 4.5 m) | 1.50 | 2.00 | 0.36 | 0.10 | 0.25 | 1.90 | PASS |

Failures: 0
