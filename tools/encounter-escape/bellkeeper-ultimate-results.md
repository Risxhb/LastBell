# Bellkeeper Ultimate escape check results (2026-10-23)

`powershell -ExecutionPolicy Bypass -File tools/encounter-escape/bellkeeper-ultimate-escape-check.ps1`

```

Mechanic                                                 Speed   d (m) walk (s)      +dash (s)    window (s) Result                   Note                                                             
--------                                                 -----   ----- --------      ---------    ---------- ------                   ----                                                             
Pendulum Clocks: owner reaches the striking spot         4.2 m/s 13.54 3.82          3.31                6.0 PASS (walk)              numerals >= 6 s before hour I; boss 3 m off-centre, worst station
Pendulum Clocks: owner reaches the striking spot         6 m/s   13.54 2.86          2.56                6.0 PASS (walk)              numerals >= 6 s before hour I; boss 3 m off-centre, worst station
Pendulum Clocks: others leave a locked ally cone         4.2 m/s 3.10  1.34          0.82                1.5 PASS (walk)              half-width at 6 m + margin; window from lock                     
Pendulum Clocks: others leave a locked ally cone         6 m/s   3.10  1.12          0.82                1.5 PASS (walk)              half-width at 6 m + margin; window from lock                     
Stopped Hour: Stilled player reaches the hiding strip    4.2 m/s 11.75 3.40          2.88                5.0 PASS (walk)              glyph 5.0 s before the stop; boss within 3 m of centre           
Stopped Hour: Stilled player reaches the hiding strip    6 m/s   11.75 2.56          2.26                5.0 PASS (walk)              glyph 5.0 s before the stop; boss within 3 m of centre           
Stopped Hour + Heart Toll (one tick): strip beyond 4.3 m 4.2 m/s 14.29 4.00          3.49                5.0 PASS (walk)              one route satisfies both rules                                   
Stopped Hour + Heart Toll (one tick): strip beyond 4.3 m 6 m/s   14.29 2.98          2.68                5.0 PASS (walk)              one route satisfies both rules                                   
Toll Tide (2.5 s): wall -> inside 6.7 m                  4.2 m/s 6.30  2.10          1.59                2.5 PASS (walk)              from v2 check                                                    
Toll Tide (2.5 s): wall -> inside 6.7 m                  6 m/s   6.30  1.65          1.35                2.5 PASS (walk)              from v2 check                                                    
Heart Toll (2.0 s): body edge -> 4.3 m                   4.2 m/s 3.30  1.39          0.87                2.0 PASS (walk)              from v2 check                                                    
Heart Toll (2.0 s): body edge -> 4.3 m                   6 m/s   3.30  1.15          0.85                2.0 PASS (walk)              from v2 check                                                    
Leaning Bell alone (3.5 s): far wall -> line             4.2 m/s 13.43 3.80          3.28                3.5 PASS (dash)              from v2 check                                                    
Leaning Bell alone (3.5 s): far wall -> line             6 m/s   13.43 2.84          2.54                3.5 PASS (walk)              from v2 check                                                    
Leaning Bell announced by the Hand (phrase 5 s + 2.0 s)  4.2 m/s 13.43 3.80          3.28                7.0 PASS (walk)              reader starts at the phrase                                      
Leaning Bell announced by the Hand (phrase 5 s + 2.0 s)  6 m/s   13.43 2.84          2.54                7.0 PASS (walk)              reader starts at the phrase                                      
Leaning Bell announced, WITHOUT reading the phrase       4.2 m/s 13.43 3.80          3.28                2.0 FAIL                     fails without memory: by design                                  
Leaning Bell announced, WITHOUT reading the phrase       6 m/s   13.43 2.84          2.54                2.0 FAIL                     fails without memory: by design                                  
Ledger replay step (1.5 s)                               4.2 m/s 2.50  1.20          0.77                1.5 PASS (walk)              pre-positioned from memory                                       
Ledger replay step (1.5 s)                               6 m/s   2.50  1.02          0.77                1.5 PASS (walk)              pre-positioned from memory                                       
Two Futures step (1.2 s)                                 4.2 m/s 2.50  1.20          0.77                1.2 PASS (walk)              pre-positioned from memory                                       
Two Futures step (1.2 s)                                 6 m/s   2.50  1.02          0.77                1.2 PASS (walk)              pre-positioned from memory                                       
Backwards Bell rewind step (1.0 s apart, 1.5 s warning)  4.2 m/s 2.50  1.20          0.77                1.5 PASS (walk)              pre-positioned from memory                                       
Backwards Bell rewind step (1.0 s apart, 1.5 s warning)  6 m/s   2.50  1.02          0.77                1.5 PASS (walk)              pre-positioned from memory                                       
Ledger replay step WITHOUT memory (Leaning half)         4.2 m/s 13.43 3.80          3.28                1.5 FAIL                     fails without memory: by design                                  
Ledger replay step WITHOUT memory (Leaning half)         6 m/s   13.43 2.84          2.54                1.5 FAIL                     fails without memory: by design                                  
Two Futures step WITHOUT memory (cone 60 deg, 9 m)       4.2 m/s 5.0   1.79          1.28                1.2 FAIL                     fails without memory: by design                                  
Two Futures step WITHOUT memory (cone 60 deg, 9 m)       6 m/s   5.0   1.43          1.13                1.2 PASS (dash)              fails without memory: by design                                  
Clock strike + Leaning Bell (one tick)                   -       -     -             -                     - PASS (spot on safe half) striker spot 0.8 m from the line on the safe side                
Fracture crossing (dash)                                 -       2.0   0.48 (14 dmg) 0.13 (4 dmg)          - PASS                     walking costs 14 HP, dashing 4 HP + 30 stamina                   



Stamina trace (three loops back to back, intended route):

Phase       dashes / loop lowest stamina Result
-----       ------------- -------------- ------
P1 (35 s)               2 70             PASS  
P2 (33 s)               3 70             PASS  
P3 (31.5 s)             3 70             PASS  
P4 (33 s)               3 70             PASS  
P5 (30 s)               4 70             PASS  



Reaction margin 0.6 s. 'Fails without memory: by design' rows are the Ultimate checks: a real route exists only for players who read or remember.
```

Note: "Two Futures step WITHOUT memory" passes with a dash at 6 m/s for the cone step; at the 4.2 m/s prototype walk it fails, so memory is still required there.
