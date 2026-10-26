<#
.SYNOPSIS
  Escape check for Aurelion's ability set v3, Milestone A (proposal): Ecliptic Order and Zenith Marks, the Marks +
  Lances pairing, the memory test and a stamina check over the heaviest block. Skyward Spire arena.

.DESCRIPTION
  Closed-form geometry (the new shapes are discs and rings, so the worst point is known exactly). For each case it takes
  the worst starting point, the distance to the nearest safe point (hit margin 0.3 m included), and the cheapest route
  that keeps the level's margin: walk, walk + one 3 m / 0.2 s dash (4-tick recovery), or dash + sprint (x1.5). Reaction
  0.25 s is in every route. It reports the margin at base walk 4.2 m/s (prototype) and 6 m/s (design walk).
  Targets (HARDER-PROMPT §5.8): Hard >= 0.3 s, Medium >= 0.5 s, Easy >= 0.8 s, after the fastest legal route.
  Memory test (§8): the late coral lead + 0.25 s must be below the reflex escape (no reaction, dash + sprint at 9 m/s)
  from the worst spot the previous answer leaves you in.
  Stamina: the Ecliptic chain (4 strikes, worst spots every time) using the cheapest route per step that meets the
  margin, with the real numbers (100 max, dash 30, sprint 25/s, regen 20/s after 0.75 s).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/encounter-escape/aurelion-v3-escape-check.ps1 > abilities-v3-escape.md
#>
param([double]$Bound = 13.9, [double]$M = 0.3, [double]$BodyGap = 1.75)

$react = 0.25; $dash = 3.0; $dashT = 0.2 + 4/60
$need = @{ Easy = 0.8; Medium = 0.5; Hard = 0.3 }
$scale = @{ Easy = 1.25; Medium = 1.0; Hard = 1.0 }       # v3 mechanics keep Medium timings on Hard
# Ecliptic Order (EclipticOrder.cs)
$E = @{ Anchor = 5.0; First = 3.2; Step = 2.2; Lead = 0.35; LeadEasy = 1.0; Safe = @{ 1 = 3.0; 4 = 3.5; 8 = 4.5 } }
$orders = @{
  "3a" = @(0,120,240); "3b" = @(90,330,210); "3c" = @(30,270,150)
  "4a" = @(0,180,60,240); "4b" = @(90,270,30,210); "4c" = @(315,135,15,195)
}
# Zenith Marks (ZenithMarks.cs); the block walks him to the centre first
$Z = @{ Shade = 5.5; Sun = 8.5; Mark = 3.0; MarkEasy = 4.0; Lead = 0.35 }

function Chord([double]$a, [double]$b, [double]$r) { $d = [math]::Abs($a - $b) % 360; if ($d -gt 180) { $d = 360 - $d }; return 2 * $r * [math]::Sin($d / 2 * [math]::PI / 180) }
# route options for distance d at base walk w: returns @(time, stamina) for walk, dash+walk, dash+sprint
function Routes([double]$d, [double]$w) {
  $walk = @(($react + $d / $w), 0.0)
  $dw = if ($d -le $dash) { @(($react + $d / $dash * 0.2), 30.0) } else { @(($react + $dashT + ($d - $dash) / $w), 30.0) }
  $sp = 1.5 * $w; $st = [math]::Max(0, $d - $dash) / $sp
  $ds = @(($react + $dashT + $st), (30 + 25 * $st))
  return @($walk, $dw, $ds)
}
function Cheapest([double]$d, [double]$w, [double]$window, [double]$margin) {
  foreach ($r in (Routes $d $w)) { if ($window - $r[0] -ge $margin) { return $r } }
  return (Routes $d $w)[2]
}
function Reflex([double]$d) { if ($d -le $dash) { return $d / $dash * 0.2 } ; return 0.2 + ($d - $dash) / 9.0 }

$rows = New-Object System.Collections.ArrayList
$fail = 0
function Row([string]$level, [string]$name, [double]$d, [double]$window, [string]$where) {
  $m = $need[$level]; $out = @()
  foreach ($w in 4.2, 6.0) {
    $r = Cheapest $d $w $window $m
    $out += [math]::Round($window - $r[0], 2)
    $out += [math]::Round($r[1], 0)
  }
  $ok = $out[0] -ge $m -and $out[2] -ge $m
  if (-not $ok) { $script:fail++ }
  [void]$rows.Add("| $level | $name | $([math]::Round($d,2)) | $window | $($out[0]) ($($out[1]) st) | $($out[2]) ($($out[3]) st) | $(if ($ok) {'pass'} else {'FAIL'}) | $where |")
}

foreach ($level in "Easy", "Medium", "Hard") {
  $k = $scale[$level]
  foreach ($n in 1, 4, 8) {
    $rEff = $E.Safe[$n] - $M
    # strike I from the far rim (anywhere at the reveal)
    Row $level "Ecliptic Order, strike I from the reveal (N $n, disc $($E.Safe[$n]) m)" ($Bound + $E.Anchor - $rEff) ($E.First * $k) "rim point opposite anchor I"
    # each next strike, from the far side of the previous disc
    $worst = 0; $at = ""
    foreach ($o in $orders.GetEnumerator()) { $a = $o.Value; for ($i = 1; $i -lt $a.Count; $i++) { $c = Chord $a[$i-1] $a[$i] $E.Anchor; if ($c -gt $worst) { $worst = $c; $at = "order $($o.Key), $($a[$i-1])->$($a[$i]) deg" } } }
    Row $level "Ecliptic Order, next strike (N $n)" $worst ($E.Step * $k) "far side of the last disc, $at"
  }
  # Zenith Marks (boss at the centre)
  $mk = if ($level -eq "Easy") { $Z.MarkEasy } else { $Z.Mark }
  Row $level "Zenith Marks, SHADE from the rim" ($Bound - ($Z.Shade - $M)) $mk "rim, boss at the centre"
  Row $level "Zenith Marks, SUN from beside his body" (($Z.Sun + $M) - $BodyGap) $mk "touching his body"
  # Misalignment two-step (SHADE > SUN or SUN > SHADE): the gap between the volleys (Hard: Lances track 1.2 + window 0.84 s;
  # Medium 1.0 + 1.2; Easy 1.25 + 1.5)
  $gap = @{ Easy = 2.75; Medium = 2.2; Hard = 2.04 }[$level]
  Row $level "Zenith Marks two-step, SHADE then SUN (beside his body to beyond 8.5 m)" (($Z.Sun + $M) - $BodyGap) $gap "touching his body"
  Row $level "Zenith Marks two-step, SUN then SHADE (rim to within 5.5 m)" ($Bound - ($Z.Shade - $M)) $gap "rim, boss at the centre"
  # pairing: SHADE + Meridian Lances (beam locked on you as you arrive radially): side-step 1.4 m inside the shade.
  # Hard lock window 0.84 s (v2.1 x0.7; Medium windows with -lastbellWindowsMedium), Medium 1.2, Easy 1.5
  $win = @{ Easy = 1.5; Medium = 1.2; Hard = 0.84 }[$level]
  Row $level "Pairing: SHADE + Lances, side-step out of the beam that locked on you in the shade" (1.1 + $M) $win "on the beam's centre line, 3 m from him"
}

# memory test
$mem = New-Object System.Collections.ArrayList
$minChord = 99; foreach ($o in $orders.GetEnumerator()) { $a = $o.Value; for ($i = 1; $i -lt $a.Count; $i++) { $c = Chord $a[$i-1] $a[$i] $E.Anchor; if ($c -lt $minChord) { $minChord = $c } } }
$rE = $E.Safe[1] - $M
$cases = @(
  @("Ecliptic next strike, worst spot in the last disc (far side, closest pair)", $minChord, $E.Lead),
  @("Ecliptic next strike, best spot (near edge, closest pair)", ($minChord - 2 * $rE), $E.Lead),
  @("Zenith Marks SHADE from the rim", ($Bound - ($Z.Shade - $M)), $Z.Lead),
  @("Zenith Marks SUN from beside his body", (($Z.Sun + $M) - $BodyGap), $Z.Lead)
)
foreach ($c in $cases) {
  $t = Reflex $c[1]; $ok = $c[2] + 0.25 -le $t
  if ($c[0] -notlike "*best spot*" -and -not $ok) { $fail++ }
  [void]$mem.Add("| $($c[0]) | $([math]::Round($c[1],2)) | $([math]::Round($t,2)) | $($c[2]) | $(if ($ok) {'memory needed'} elseif ($c[0] -like '*best spot*') {'reflex can make it (info: only from the one right spot)'} else {'FAIL: reflex beats it'}) |")
}

# stamina and margins over the heaviest block: Ecliptic, 4 strikes, played as a route. Start anywhere on the rim (72
# points) at the reveal; each leg runs the cheapest route (meeting the margin) to the nearest edge of the next disc (0.1 m
# inside) and stands there. Stamina: 100 max, dash 30, sprint 25/s, regen 20/s once 0.75 s have passed since the last spend.
$stam = New-Object System.Collections.ArrayList
foreach ($level in "Medium", "Hard") {
  foreach ($w in 4.2, 6.0) {
    $k = $scale[$level]; $rEff = $E.Safe[1] - $M - 0.1; $low = 100.0; $minM = 99.0; $worst = ""
    foreach ($o in $orders.GetEnumerator()) {
      if ($o.Value.Count -ne 4) { continue }
      for ($deg = 0; $deg -lt 360; $deg += 5) {
        $px = $Bound * [math]::Sin($deg * [math]::PI / 180); $py = $Bound * [math]::Cos($deg * [math]::PI / 180)
        $s = 100.0; $since = 99.0; $first = $true
        foreach ($yaw in $o.Value) {
          $ax = $E.Anchor * [math]::Sin($yaw * [math]::PI / 180); $ay = $E.Anchor * [math]::Cos($yaw * [math]::PI / 180)
          $dx = $px - $ax; $dy = $py - $ay; $dist = [math]::Sqrt($dx*$dx + $dy*$dy)
          $d = [math]::Max(0, $dist - $rEff); $window = if ($first) { $E.First * $k } else { $E.Step * $k }; $first = $false
          $r = Cheapest $d $w $window $need[$level]
          $mm = $window - $r[0]; if ($mm -lt $minM) { $minM = $mm; $worst = "order $($o.Key), start $deg deg" }
          # regen before this leg's spend (at the reaction point), then the spend, then regen after it
          $spendAt = $react
          if ($r[1] -gt 0) {
            $since += $spendAt; if ($since -gt 0.75) { $s = [math]::Min(100, $s + 20 * [math]::Min($since - 0.75, $spendAt)) }
            $s -= $r[1]; $low = [math]::Min($low, $s)
            $endSpend = if ($r[1] -gt 30) { $r[0] } else { $react + $dashT }
            $since = $window - $endSpend
            if ($since -gt 0.75) { $s = [math]::Min(100, $s + 20 * ($since - 0.75)) }
          } else { $since += $window; $s = [math]::Min(100, $s + 20 * [math]::Max(0, [math]::Min($window, $since - 0.75))) }
          if ($dist -gt 0) { $px = $ax + $dx / $dist * [math]::Min($dist, $rEff); $py = $ay + $dy / $dist * [math]::Min($dist, $rEff) }
        }
      }
    }
    if ($low -lt 0 -or $minM -lt $need[$level]) { $script:fail++ }
    [void]$stam.Add("| $level | $w | $([math]::Round($minM,2)) | $([math]::Round($low,0)) | $(if ($low -ge 0 -and $minM -ge $need[$level]) {'pass'} else {'FAIL'}) | $worst |")
  }
}
"# Aurelion ability set v3 (Milestone A): escape check"
""
"Produced by ``tools/encounter-escape/aurelion-v3-escape-check.ps1`` on $(Get-Date -Format 'yyyy-MM-dd HH:mm'). Bound $Bound m, hit margin $M m, reaction $react s, dash 3 m / 0.2 s, sprint x1.5. Margin = window - the cheapest route (least stamina) that meets the level's target; st = stamina it spends. Targets: Easy 0.8, Medium 0.5, Hard 0.3 s."
""
"| Level | Case | Worst escape (m) | Window (s) | Margin at 4.2 m/s | Margin at 6 m/s | Verdict | Worst configuration |"
"|---|---|---|---|---|---|---|---|"
$rows
""
"## Memory test (reflex escape, no reaction, dash + sprint 9 m/s, vs the coral lead + 0.25 s)"
""
"| Case | Distance (m) | Reflex escape (s) | Coral lead (s) | Verdict |"
"|---|---|---|---|---|"
$mem
""
"## The heaviest block as a route (Ecliptic, 4 strikes, every rim start and order): worst margin and lowest stamina"
""
"| Level | Base walk (m/s) | Worst leg margin (s) | Lowest stamina | Verdict | Worst at |"
"|---|---|---|---|---|---|"
$stam
""
"**$(if ($fail -eq 0) {'All cases pass.'} else {"$fail case(s) FAIL."})**"
