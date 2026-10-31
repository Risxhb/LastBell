## Crucivar ability set v2: escape check (Easy)

Arena bound 13.7 m, boss body 1.7 m, hit margin 0.3 m. PASS = reaction margin >= 0.5 s at 4.2 m/s with one 3 m / 0.2 s dash, and at 6 m/s walking.

| Ability | Worst case | Distance (m) | Window (s) | 4.2 walk (s) | 4.2 walk + dash (s) | 6.0 walk (s) | Margin at 4.2 + dash (s) | Result |
|---|---|---|---|---|---|---|---|---|
| Mould Press | N>=2: reach the nearer of the two opposite bays, from anywhere | 12.58 | 5.25 | 3.00 | 2.48 | 2.10 | 2.77 | PASS |
| Mould Press | N=4/8: a bad split sends you to the FAR bay of the pair | 17.38 | 5.25 | 4.14 | 3.62 | 2.90 | 1.63 | PASS |
| Mould Press | N=1: tower I to tower II (adjacent bay), from the far side of I | 8.49 | 3.25 | 2.02 | 1.51 | 1.41 | 1.74 | PASS |
| Gear Chain | partners on the same spot: each walks min / 2 | 2.50 | 3.75 | 0.60 | 0.17 | 0.42 | 3.58 | PASS |
| Gear Chain | partners at opposite edges: each walks (sep - max) / 2 | 8.20 | 3.75 | 1.95 | 1.44 | 1.37 | 2.31 | PASS |
| Gear Chain | solo / odd: standing on its rim post, walk in to min | 5.00 | 3.75 | 1.19 | 0.68 | 0.83 | 3.07 | PASS |
| Gear Chain + Bellows | partners at opposite sides of the slag-edge disc | 3.70 | 3.75 | 0.88 | 0.37 | 0.62 | 3.38 | PASS |
| Bellows Draw | slag rim: from the arena edge inward | 4.55 | 2.50 | 1.08 | 0.57 | 0.76 | 1.93 | PASS |
| Bellows Draw | blast: from the boss body outward, slag edge still burning (boss <= 3 m from centre) | 2.65 | 3.75 | 0.63 | 0.18 | 0.44 | 3.57 | PASS |
| Bellows Draw | after the 1.5 m inhale pull: walk back out before the blast | 1.50 | 1.13 | 0.36 | 0.10 | 0.25 | 1.03 | PASS |
| Great Casting | join the pour line from anywhere (its boss end never moves) | 12.10 | 4.38 | 2.88 | 2.37 | 2.02 | 2.01 | PASS |
| Great Casting | solo: dodge the locked pour lane | 1.60 | 2.50 | 0.38 | 0.11 | 0.27 | 2.39 | PASS |
| Great Casting | marching stamps: leave a stamp without entering a neighbouring spoke (worst ring r 4.5 m) | 1.50 | 2.50 | 0.36 | 0.10 | 0.25 | 2.40 | PASS |

Failures: 0
