<#
.SYNOPSIS
  Paper proof for the Bellkeeper Ultimate (proposal; bosses/4-shattered-hour-bellkeeper/ultimate/ULTIMATE.md).

.DESCRIPTION
  For every mechanic: the worst-case distance a player must cover inside its window, at 4.2 and 6 m/s walk, walk-only
  and walk + one dash (3 m in 0.2 s), plus a 0.6 s reaction margin. Distances are sampled on the Throne (circle 13.0 m;
  boss body 1.4 m, player margin 0.3 m). Simultaneous resolutions are checked as one route (the point must satisfy every
  rule on that tick). Memory mechanics (Ledger, Futures, Backwards, announced Leaning Bell) are checked for the
  pre-positioned route (the intended solution) and reported as "fails without memory" where that is the design.
  A stamina trace per phase loop counts the dashes the intended route needs and checks stamina never goes below 0
  (100 max, 30 per dash, regen 20/s after 0.75 s).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/encounter-escape/bellkeeper-ultimate-escape-check.ps1
#>
param([double]$Reaction = 0.6, [double]$A = 13.0, [double]$Body = 1.4, [double]$M = 0.3)

function T-Walk($d, $v) { $d / $v }
function T-Dash($d, $v) { if ($d -le 3) { 0.2 * $d / 3 } else { 0.2 + ($d - 3) / $v } }
$rows = New-Object System.Collections.Generic.List[object]
function Row($name, $d, $window, $note) {
  foreach ($v in 4.2, 6.0) {
    $w = $Reaction + (T-Walk $d $v); $dd = $Reaction + (T-Dash $d $v)
    $res = if ($w -le $window) { 'PASS (walk)' } elseif ($dd -le $window) { 'PASS (dash)' } else { 'FAIL' }
    $rows.Add([pscustomobject]@{ Mechanic = $name; Speed = "$v m/s"; 'd (m)' = '{0:F2}' -f $d; 'walk (s)' = '{0:F2}' -f $w; '+dash (s)' = '{0:F2}' -f $dd; 'window (s)' = $window; Result = $res; Note = $note })
  }
}

# --- sampled worst cases on the Throne -------------------------------------------------------------------------------
# Distance from the worst point in the arena to the nearest point of a target region (both inside the map).
function Worst-To-Region([scriptblock]$inRegion, [scriptblock]$valid) {
  $step = 0.25; $pts = @(); $reg = @()
  for ($x = -$A; $x -le $A; $x += $step) { for ($y = -$A; $y -le $A; $y += $step) {
    if ($x*$x + $y*$y -gt $A*$A) { continue }
    if (-not (& $valid $x $y)) { continue }
    if (& $inRegion $x $y) { $reg += ,@($x, $y) } else { $pts += ,@($x, $y) } } }
  if ($reg.Count -eq 0) { return -1 }
  $worst = 0
  for ($i = 0; $i -lt $pts.Count; $i += 7) {
    $p = $pts[$i]; $best = 1e9
    foreach ($r in $reg) { $dx = $r[0] - $p[0]; $dy = $r[1] - $p[1]; $d = $dx*$dx + $dy*$dy; if ($d -lt $best) { $best = $d } }
    if ($best -gt 0 -and [Math]::Sqrt($best) -gt $worst) { $worst = [Math]::Sqrt($best) }
  }
  return $worst
}

# Clock owner reaching a striking spot: far side of the boss from the station, 3.8-7.4 m from the boss centre, inside
# the 25-degree cone that includes the boss. Boss at 3 m from centre (worst), station east (+x).
$bx = 3.0; $by = 0.0
$clockSpot = { param($x, $y) $dx = $x - $bx; $dy = $y - $by; $r = [Math]::Sqrt($dx*$dx + $dy*$dy)
  ($dx -lt 0) -and ($r -ge 3.8) -and ($r -le 7.4) -and ([Math]::Abs($dy) -le 1.0) }
$outsideBody = { param($x, $y) (($x - $bx)*($x - $bx) + ($y - $by)*($y - $by)) -ge (($Body + $M)*($Body + $M)) }
$dClock = Worst-To-Region $clockSpot $outsideBody
Row 'Pendulum Clocks: owner reaches the striking spot' $dClock 6.0 'numerals >= 6 s before hour I; boss 3 m off-centre, worst station'
Row 'Pendulum Clocks: others leave a locked ally cone' (6.0 * [Math]::Tan(25 * [Math]::PI / 180) + $M) 1.5 'half-width at 6 m + margin; window from lock'

# Stopped Hour: hide behind the boss from your station: within +-1.1 m of the boss-station line, beyond the body.
$bx2 = 3.0
$hide = { param($x, $y) $dx = $x - $bx2; ($dx -lt -($Body + $M)) -and ([Math]::Abs($y) -le 1.1) }
$dHide = Worst-To-Region $hide { param($x, $y) (($x - $bx2)*($x - $bx2) + $y*$y) -ge (($Body + $M)*($Body + $M)) }
Row 'Stopped Hour: Stilled player reaches the hiding strip' $dHide 5.0 'glyph 5.0 s before the stop; boss within 3 m of centre'
# simultaneous (P2 29 s, P4): the lash and a Heart Toll (r 4.0 at the boss) on the same tick
$hide2 = { param($x, $y) $dx = $x - $bx2; ($dx -lt -(4.0 + $M)) -and ([Math]::Abs($y) -le 1.1) }
$dHide2 = Worst-To-Region $hide2 { param($x, $y) $true }
Row 'Stopped Hour + Heart Toll (one tick): strip beyond 4.3 m' $dHide2 5.0 'one route satisfies both rules'

# Fixed-shape v2 vocabulary in Ultimate tuning
Row 'Toll Tide (2.5 s): wall -> inside 6.7 m' (13.0 - 6.7) 2.5 'from v2 check'
Row 'Heart Toll (2.0 s): body edge -> 4.3 m' (4.3 - 1.0) 2.0 'from v2 check'
Row 'Leaning Bell alone (3.5 s): far wall -> line' 13.43 3.5 'from v2 check'
Row 'Leaning Bell announced by the Hand (phrase 5 s + 2.0 s)' 13.43 7.0 'reader starts at the phrase'
Row 'Leaning Bell announced, WITHOUT reading the phrase' 13.43 2.0 'fails without memory: by design'

# Memory chains (intended pre-positioned route): authored so consecutive safe spots are <= 2.5 m apart
Row 'Ledger replay step (1.5 s)' 2.5 1.5 'pre-positioned from memory'
Row 'Two Futures step (1.2 s)' 2.5 1.2 'pre-positioned from memory'
Row 'Backwards Bell rewind step (1.0 s apart, 1.5 s warning)' 2.5 1.5 'pre-positioned from memory'
Row 'Ledger replay step WITHOUT memory (Leaning half)' 13.43 1.5 'fails without memory: by design'
Row 'Two Futures step WITHOUT memory (cone 60 deg, 9 m)' 5.0 1.2 'fails without memory: by design'

# Simultaneous: clock III strike + Leaning Bell leaning toward that hour's station (P1 22 s). The striking spot at 3.8 m
# behind a boss 3 m off-centre is at signed distance 3 - 3.8 - 0.3 < 0 from the cleave line: on the safe half.
$signed = 3.0 - 3.8
$rows.Add([pscustomobject]@{ Mechanic = 'Clock strike + Leaning Bell (one tick)'; Speed = '-'; 'd (m)' = '-'; 'walk (s)' = '-'; '+dash (s)' = '-'; 'window (s)' = '-'; Result = $(if ($signed + $M -le 0) { 'PASS (spot on safe half)' } else { 'FAIL' }); Note = ('striker spot {0:F1} m from the line on the safe side' -f (-$signed)) })

# Fracture crossing (P5): 1.4 m lane burning 30/s
$rows.Add([pscustomobject]@{ Mechanic = 'Fracture crossing (dash)'; Speed = '-'; 'd (m)' = '2.0'; 'walk (s)' = '0.48 (14 dmg)'; '+dash (s)' = '0.13 (4 dmg)'; 'window (s)' = '-'; Result = 'PASS'; Note = 'walking costs 14 HP, dashing 4 HP + 30 stamina' })

# --- stamina trace per phase loop (intended route) -------------------------------------------------------------------
# Each entry: times (s) within the loop when the intended route needs a dash.
$loops = [ordered]@{
  'P1 (35 s)' = @{ Len = 35; Dashes = @(6.5, 20.0) }          # two Leaning Bells
  'P2 (33 s)' = @{ Len = 33; Dashes = @(6.0, 10.0, 15.0) }    # phrase move, block A, rewind A
  'P3 (31.5 s)' = @{ Len = 31.5; Dashes = @(11.0, 14.0, 22.0) } # futures (2), hiding strip
  'P4 (33 s)' = @{ Len = 33; Dashes = @(3.0, 6.0, 20.0) }     # ledger halves, hide
  'P5 (30 s)' = @{ Len = 30; Dashes = @(4.0, 9.0, 14.0, 25.0) } # phrase, futures, fissure crossing
}
$trace = foreach ($k in $loops.Keys) {
  $L = $loops[$k]; $st = 100.0; $min = 100.0; $last = -10.0; $ok = $true
  for ($t = 0.0; $t -lt 3 * $L.Len; $t += 0.05) {            # three loops back to back
    $tl = $t % $L.Len
    if ($L.Dashes | Where-Object { [Math]::Abs($_ - $tl) -lt 0.025 }) { $st -= 30; $last = $t; if ($st -lt 0) { $ok = $false } }
    elseif ($t - $last -gt 0.75) { $st = [Math]::Min(100, $st + 20 * 0.05) }
    $min = [Math]::Min($min, $st)
  }
  [pscustomobject]@{ Phase = $k; 'dashes / loop' = $L.Dashes.Count; 'lowest stamina' = '{0:F0}' -f $min; Result = $(if ($ok) { 'PASS' } else { 'FAIL' }) }
}

$rows | Format-Table -AutoSize -Wrap | Out-String -Width 240
"Stamina trace (three loops back to back, intended route):"
$trace | Format-Table -AutoSize | Out-String -Width 200
"Reaction margin $Reaction s. 'Fails without memory: by design' rows are the Ultimate checks: a real route exists only for players who read or remember."
