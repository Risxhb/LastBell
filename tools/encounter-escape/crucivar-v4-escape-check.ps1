<#
.SYNOPSIS
  Worst-case escape check for Crucivar v4 (bosses/2-brass-foundry-crucivar/abilities-v4/), the default fight since 30 Oct.

.DESCRIPTION
  Same idea as crucivar-escape-check.ps1 (v2): pure geometry, no Unity. Each row is the worst straight-line distance a
  player has to cover (leave a shape, reach a soak, keep pace with a sweep) and the window the telegraph gives for it.
  It uses today's movement (PlayerController: walk 6 m/s, sprint 9 m/s, dash 3 m in about 0.2 s, no dodge immunity).
  PASS needs a reaction margin of at least 0.5 s walking at 6 m/s, or walking plus one dash. The old 4.2 m/s prototype
  walk is shown for reference only.
  Shapes are expanded by the hit margin (Hazard.Margin 0.3 m) and the player's radius where a centre must clear a body.
  Floor edges are covered by the Unity test crucivar.v4_bounds (no leaving the floor, wall attacks reach every corner).

  Limits: straight lines, no other players' bodies, no acceleration; Ring Wave and Spiral Arm are sampled on a 0.25 m
  grid with the bullets' travel time as extra window.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/encounter-escape/crucivar-v4-escape-check.ps1
  powershell -ExecutionPolicy Bypass -File tools/encounter-escape/crucivar-v4-escape-check.ps1 -Level Easy
#>
param(
  [ValidateSet('Easy', 'Medium', 'Hard')][string]$Level = 'Medium',
  [double]$Arena = 13.7,          # Brass Foundry player bound (RunMaps)
  [double]$BossBody = 1.7,        # CrucivarEncounter.BodyRadius
  [double]$PlayerR = 0.45,        # PlayerController.Radius
  [double]$M = 0.3,               # Hazard.Margin
  [double]$Walk = 6.0, [double]$Sprint = 9.0, [double]$DashM = 3.0, [double]$DashS = 0.2,
  [double]$Reaction = 0.5
)
$ws = if ($Level -eq 'Easy') { 1.25 } else { 1.0 }   # Easy stretches every v4 warning by 1.25
$hug = $BossBody + $PlayerR                         # the closest a player centre gets to the boss

$rows = New-Object System.Collections.ArrayList
function Add-Row($phase, $ability, $case, $dist, $window, $note = '') { [void]$rows.Add([pscustomobject]@{ Phase = $phase; Ability = $ability; Case = $case; Dist = $dist; Window = $window; Note = $note }) }

# ---------------- Phase 1 Forge Rite (ForgeRite.cs) ----------------
Add-Row 1 'Ember Brand' 'leave another carrier''s 3 m brand (it lands 3.5 s after it appears)' (3.0 + $M) (3.5 * $ws)
Add-Row 1 'Furnace Spin' 'at the wall: walk in to where the gap can be followed (jets turn 40 deg/s, shown 1.5 s)' ($Arena - $Walk / (40 * [math]::PI / 180)) (1.5 * $ws) ("follow radius walking {0:F1} m, sprinting {1:F1} m; at the wall the gap moves {2:F1} m/s" -f ($Walk / (40 * [math]::PI / 180)), ($Sprint / (40 * [math]::PI / 180)), ($Arena * 40 * [math]::PI / 180))
# Backdraft: from hugging the boss to outside the 5 m blast by 3.0 s, while a 2 m inward pull runs for 0.4 s of it
Add-Row 1 'Backdraft' 'from hugging the boss to outside the 5 m blast, against the 2 m pull' ((5.0 + $M - $hug) + 2.0) (3.0 * $ws) 'pull counted as extra distance'
Add-Row 1 'Backdraft II' 'carrying the second brand: out of the 5 m blast from hugging the boss, against the 2 m pull (from 14.0 s, 11-01)' ((5.0 + $M - $hug) + 2.0) (3.0 * $ws) 'the brand lands 3.5 s after it appears, so the carrier has the blast window too'
Add-Row 1 'Brand Pool' 'walk off your brand after it stops following (1.0 s before it lands and pools, 11-01)' (2.0 + $M) (1.0 * $ws)
Add-Row 1 'Casting Order' 'leave your locked pour lane (2.6 m wide, locks 1.5 s before it lands)' (2.6 / 2 + $M) (1.5 * $ws)
Add-Row 1 'Anvil Quake' 'hugging the boss: leave the inner circle (0-4 m) before 1.4 s' (4.0 + $M - $hug) (1.4 * $ws)
Add-Row 1 'Anvil Quake' 'cross a band edge into the band that just went off (0.8 s between bands)' (2 * $M) (0.8 * $ws)
if ($Level -eq 'Hard') {
  Add-Row 1 'Quake Echo (Hard)' 'cross back over a band edge between echoes (0.8 s apart, outside-in)' (2 * $M) 0.8
}

# ---------------- Phase 2 Foundry Uprising (FoundryUprising.cs) ----------------
$warn2 = if ($Level -eq 'Easy') { 2.5 } else { 2.0 }
Add-Row 2 'Lane Stream' 'leave a 1 m bullet lane (paths drawn first)' (0.5 + $M) $warn2
Add-Row 2 'Lane Stream (dense, 11-01)' 'step into the 1.0 m safe strip between two of the 5 lanes (2.6 m apart), from a lane centre' (1.3) $warn2 'or step outside the outer lanes'
$code = @'
using System;
public static class V4Esc {
    // worst time over the floor to reach a point that the pattern misses, as distance minus the bullets' travel time
    // turret on the rim at (-A-0.4, 0) facing the centre; cones given as (centre yaw offset, half width) in degrees
    public static double Worst(double A, double M, double pr, double[] cones, double warn, double bulletSpeed, double speed, out double atX, out double atY, out double dist, out double win) {
        double tx = -A - 0.4, worst = -1e9; atX = atY = 0; dist = win = 0;
        var safe = new System.Collections.Generic.List<double[]>();
        for (double x = -A; x <= A; x += 0.25) for (double y = -A; y <= A; y += 0.25) {
            if (x*x + y*y > A*A) continue;
            if (!InCone(x, y, tx, cones, M + pr)) safe.Add(new[] { x, y, Math.Sqrt((x-tx)*(x-tx) + y*y) });
        }
        for (double x = -A; x <= A; x += 0.25) for (double y = -A; y <= A; y += 0.25) {
            if (x*x + y*y > A*A || !InCone(x, y, tx, cones, M)) continue;
            double best = 1e9, bd = 0, bw = 0;
            foreach (var s in safe) {
                double d = Math.Sqrt((s[0]-x)*(s[0]-x) + (s[1]-y)*(s[1]-y));
                double window = warn + s[2] / bulletSpeed;          // the bullets reach s this long after the path appears
                if (d / speed - window < best) { best = d / speed - window; bd = d; bw = window; }
            }
            if (best > worst) { worst = best; atX = x; atY = y; dist = bd; win = bw; }
        }
        return worst;   // time short (negative = spare time)
    }
    static bool InCone(double x, double y, double tx, double[] cones, double pad) {
        double dx = x - tx, dy = y, r = Math.Sqrt(dx*dx + dy*dy); if (r < 1e-6) return true;
        double a = Math.Atan2(dy, dx) * 180 / Math.PI;
        for (int i = 0; i < cones.Length; i += 2) {
            double off = Math.Abs(a - cones[i]); if (off > 180) off = 360 - off;
            double padDeg = Math.Asin(Math.Min(1, pad / r)) * 180 / Math.PI;
            if (off <= cones[i+1] + padDeg) return true;
        }
        return false;
    }
}
'@
Add-Type -TypeDefinition $code -Language CSharp
# Ring Wave: three cones that leave two 16 deg gaps at +-26 deg (RingGapAt 26, RingGapHalf 8)
$mid = 26 - 8; $side = (180 - 26 - 8) / 2
$ring = [double[]]@(0, $mid, (26 + 8 + $side), $side, -(26 + 8 + $side), $side)
$x = 0; $y = 0; $d = 0; $w = 0
[void][V4Esc]::Worst($Arena, $M, $PlayerR, $ring, $warn2, 5.0, $Walk, [ref]$x, [ref]$y, [ref]$d, [ref]$w)
Add-Row 2 'Ring Wave' ("reach a 16 deg gap before the ring gets there (worst start {0:F1}, {1:F1}; turret on the west rim)" -f $x, $y) $d $w 'window = path warning + bullet travel to the safe spot'
# Spiral Arm: three arms 120 deg apart, each drawn as a 28 deg half-cone
$spiral = [double[]]@(0, 28, 120, 28, -120, 28)
[void][V4Esc]::Worst($Arena, $M, $PlayerR, $spiral, $warn2, 6.0, $Walk, [ref]$x, [ref]$y, [ref]$d, [ref]$w)
Add-Row 2 'Spiral Arm' ("leave a drawn arm before its bullets arrive (worst start {0:F1}, {1:F1})" -f $x, $y) $d $w 'window = path warning + bullet travel to the safe spot'
Add-Row 2 'Furnace Spin / Backdraft' 'same as Phase 1 (from the second uprising)' ($Arena - $Walk / (40 * [math]::PI / 180)) (1.5 * $ws)

# ---------------- Phase 3 Molten Crown (MoltenCrown.cs) ----------------
$rise = if ($Level -eq 'Easy') { 6.0 } else { 5.0 }; $crownWarn = if ($Level -eq 'Easy') { 4.0 } else { 3.0 }
$tin = 2.0 + $M; $rad = 5.5
Add-Row 3 'Mould Towers' 'reach a set A tower (r 5.5 m) from the far wall' (($Arena + $rad) - $tin) $rise
Add-Row 3 'Mould Towers' 'set A to set B (90 deg apart since 31 Oct), from the near edge of A' ((2 * $rad * [math]::Sin([math]::PI / 4)) - 2 * $tin) 1.6
Add-Row 3 'Mould Towers' 'set A to set B, from the far edge of A (worst soaker)' (2 * $rad * [math]::Sin([math]::PI / 4)) 1.6
Add-Row 3 'Mould Towers' 'solo: tower I to tower II (60 deg apart), from the far edge of I' (2 * $rad * [math]::Sin([math]::PI / 6)) 1.6
Add-Row 3 'Crown Rise' 'from the wall to inside the new edge (13.7 -> 11 m, the widest step)' (13.7 - 11 + $M) $crownWarn
Add-Row 3 'Tower Slag' 'step off the slag a landed tower leaves (2 m, heats 1.0 s, 11-01)' (2.0 + $M) (1.0 * $ws) 'a rising tower also cools any burn on its spot'
if ($Level -eq 'Hard') { Add-Row 3 'Second Press (Hard)' 'leave the re-stamped tower from its centre' (2.2 + $M) 2.0 }

# ---------------- report ----------------
"## Crucivar v4: escape check ($Level)"
""
"Arena $Arena m, boss body $BossBody m, hit margin $M m. PASS = reaction margin >= $Reaction s walking at $Walk m/s, or walking plus one $DashM m dash. 4.2 m/s (old prototype walk) is for reference."
""
"| Phase | Ability | Worst case | Distance (m) | Window (s) | $Walk walk (s) | $Walk + dash (s) | 4.2 + dash (s) | Margin (s) | Result | Note |"
"|---|---|---|---|---|---|---|---|---|---|---|"
$fail = 0
foreach ($r in $rows) {
  $tw = $r.Dist / $Walk
  $td = if ($r.Dist -le $DashM) { $DashS * $r.Dist / $DashM } else { $DashS + ($r.Dist - $DashM) / $Walk }
  $t42 = if ($r.Dist -le $DashM) { $DashS * $r.Dist / $DashM } else { $DashS + ($r.Dist - $DashM) / 4.2 }
  $margin = $r.Window - [math]::Min($tw, $td)
  $ok = $margin -ge $Reaction
  if (-not $ok) { $fail++ }
  "| {0} | {1} | {2} | {3:F2} | {4:F2} | {5:F2} | {6:F2} | {7:F2} | {8:F2} | {9} | {10} |" -f $r.Phase, $r.Ability, $r.Case, $r.Dist, $r.Window, $tw, $td, $t42, $margin, $(if ($ok) { 'PASS' } else { '**FAIL**' }), $r.Note
}
""
"Failures: $fail"
exit $fail
