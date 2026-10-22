<#
.SYNOPSIS
  Worst-case escape check for the Bellkeeper ability set v2 (proposal; bosses/4-shattered-hour-bellkeeper/abilities-v2).

.DESCRIPTION
  Pure geometry, no Unity. For each ability shape it samples the arena (0.1 m grid) and every relevant boss position /
  orientation, finds the reachable danger point whose straight-line distance to the nearest safe point is largest, and
  turns that into the time a player needs:
    walk only            d / walk
    walk + one dash      3 m in 0.2 s, then walk (stamina: one dash = 30 of 100)
  plus the reaction margin. It then compares that with each ability's Medium window and prints PASS / FAIL.
  Player centres stay inside the map bound and outside the boss body; shapes are expanded by the 0.3 m player margin
  (touching counts). Two maps: the Throne of the Final Toll (circle, 13.0 m) and the key-art test map (square, 12.5 m).

  Limits: straight-line travel (no routing round the boss body), no other players' bodies, constant-speed dash,
  ~0.1 m sampling. Pair checks are the sequenced worst cases written in ABILITIES.md, not a full multi-player search.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/encounter-escape/bellkeeper-v2-escape-check.ps1
#>
param(
  [double]$Reaction = 0.6,          # Medium reaction margin (prompt: 0.5-0.8 s)
  [double]$BossBody = 1.4, [double]$Margin = 0.3, [double]$BossBound = 9.5,
  [double]$CleaveCastBound = 3.0,   # the Bellkeeper returns to within 3 m of the centre before a Leaning Bell
  [double]$TideInner = 7.0,
  [double]$HeartRadius = 4.0,
  [double]$OrderRadius = 2.6, [double]$OrderRadius8 = 2.2,
  [double]$MirrorRadius = 3.0, [double]$MirrorRadius8 = 2.5
)

$code = @'
using System;
public static class Esc {
    public static double A; public static bool Sq; public static double Body, M;
    public static bool InMap(double x, double y) { return Sq ? (Math.Abs(x) <= A && Math.Abs(y) <= A) : x*x+y*y <= A*A; }
    public delegate bool Shape(double x, double y);
    // worst distance from a danger point to the nearest safe point (both inside the map, outside the boss body)
    public static double Worst(Shape danger, double bx, double by, bool body) {
        double step = 0.1, lim = Sq ? A : A;
        int n = (int)Math.Ceiling(2*lim/step)+1;
        var safeX = new System.Collections.Generic.List<double>(); var safeY = new System.Collections.Generic.List<double>();
        var dX = new System.Collections.Generic.List<double>(); var dY = new System.Collections.Generic.List<double>();
        for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) {
            double x = -lim + i*step, y = -lim + j*step;
            if (!InMap(x, y)) continue;
            if (body && (x-bx)*(x-bx)+(y-by)*(y-by) < (Body+M)*(Body+M)) continue;
            if (danger(x, y)) { dX.Add(x); dY.Add(y); }
            else if (danger(x+step, y) || danger(x-step, y) || danger(x, y+step) || danger(x, y-step)) { safeX.Add(x); safeY.Add(y); }
        }
        if (dX.Count == 0) return 0;
        if (safeX.Count == 0) return -1;
        double worst = 0;
        for (int k = 0; k < dX.Count; k += 3) {
            double best = double.MaxValue;
            for (int s = 0; s < safeX.Count; s++) { double dx = safeX[s]-dX[k], dy = safeY[s]-dY[k]; double d = dx*dx+dy*dy; if (d < best) best = d; }
            worst = Math.Max(worst, Math.Sqrt(best));
        }
        return worst;
    }
}
'@
Add-Type -TypeDefinition $code -Language CSharp
[Esc]::Body = $BossBody; [Esc]::M = $Margin

$maps = @(@{ Name = 'Throne of the Final Toll (circle 13.0 m)'; A = 13.0; Sq = $false }, @{ Name = 'Test map (square 12.5 m)'; A = 12.5; Sq = $true })
$moves = @(@{ Name = '4.2 m/s'; Walk = 4.2 }, @{ Name = '6 m/s'; Walk = 6.0 })
function T-Walk($d, $v) { $d / $v }
function T-Dash($d, $v) { if ($d -le 3) { 0.2 * $d / 3 } else { 0.2 + ($d - 3) / $v } }

$rows = New-Object System.Collections.Generic.List[object]
function Add-Row($ability, $map, $d, $window, $note) {
  foreach ($mv in $moves) {
    $w = T-Walk $d $mv.Walk; $dd = T-Dash $d $mv.Walk
    $needWalk = $Reaction + $w; $needDash = $Reaction + $dd
    $ok = if ($needWalk -le $window) { 'PASS (walk)' } elseif ($needDash -le $window) { 'PASS (dash)' } else { 'FAIL' }
    $rows.Add([pscustomobject]@{ Ability = $ability; Map = $map; Speed = $mv.Name; 'd_max (m)' = '{0:F2}' -f $d; 'need walk (s)' = '{0:F2}' -f $needWalk; 'need +dash (s)' = '{0:F2}' -f $needDash; 'window (s)' = $window; Result = $ok; Note = $note })
  }
}

foreach ($m in $maps) {
  [Esc]::A = $m.A; [Esc]::Sq = $m.Sq
  # 1. Leaning Bell: half of the map beyond a line through the centre (every 15 degrees)
  $w1 = 0
  for ($deg = 0; $deg -lt 180; $deg += 15) {
    $a = $deg * [Math]::PI / 180; $nx = [Math]::Cos($a); $ny = [Math]::Sin($a)
    $d = [Esc]::Worst({ param($x, $y) ($x * $nx + $y * $ny) -ge -$Margin }, 0, 0, $true)
    if ($d -gt $w1) { $w1 = $d }
  }
  # map-adaptive warning: max(3.5, reaction + walk-and-dash escape at 4.2 m/s), rounded up to 0.1 s
  $lean = [Math]::Max(3.5, [Math]::Ceiling(($Reaction + (T-Dash $w1 4.2)) * 10) / 10)
  Add-Row 'Leaning Bell (half-map cleave)' $m.Name $w1 $lean 'line through the centre; the far edge on the hit side is the worst spot (ranged punish). Warning = max(3.5, need)'
  Add-Row 'Unfinished Hour echo (Hard: replayed cleave)' $m.Name $w1 $lean 'same shape and warning rule as Leaning Bell'

  # 2a. Toll Tide: ring from the inner radius out past the wall, centred on the arena centre
  $w2 = [Esc]::Worst({ param($x, $y) [Math]::Sqrt($x*$x + $y*$y) -ge ($TideInner - $Margin) }, 0, 0, $false)
  Add-Row 'Toll Tide (edge shock-front, r >= 7.0)' $m.Name $w2 3.0 'edge to inside 6.7 m'

  # 2b. Heart Toll: point-blank circle at the boss, every boss position along a radius (symmetry) to the boss bound
  $w3 = 0
  foreach ($b in 0, 2, 4, 6, 8, 9.5) {
    $d = [Esc]::Worst({ param($x, $y) (($x - $b) * ($x - $b) + $y * $y) -le (($HeartRadius + $Margin) * ($HeartRadius + $Margin)) }, $b, 0, $true)
    if ($d -gt $w3) { $w3 = $d }
  }
  Add-Row 'Heart Toll (circle r 4.0 at the boss)' $m.Name $w3 2.0 'from the boss body edge outward'
}

# 3/4. ring shapes: independent of the map (a locked ring can always be left sideways): d = radius + margin
foreach ($r in @(@{ N = 'Striking Order ring (N<=4, r 2.6)'; R = $OrderRadius; W = 2.0 }, @{ N = 'Striking Order ring (N>=5, r 2.2)'; R = $OrderRadius8; W = 2.0 },
                 @{ N = 'Reflection after-image (N<=4, r 3.0)'; R = $MirrorRadius; W = 2.0 }, @{ N = 'Reflection after-image (N>=5, r 2.5)'; R = $MirrorRadius8; W = 2.0 })) {
  Add-Row $r.N 'any' ($r.R + $Margin) $r.W 'window counts from lock'
}

# Solo Striking Order: four rings on one player, 1.0 s apart, each locking 2.0 s before it rings. Walking steadily away
# from each locked centre: distance after lock = walk * (t - lock). Needed speed so the ring is cleared by its resolve.
foreach ($mv in $moves) {
  $need = ($OrderRadius + $Margin) / (2.0 - $Reaction)
  $rows.Add([pscustomobject]@{ Ability = 'Striking Order solo chain (I-IV on one player)'; Map = 'any'; Speed = $mv.Name; 'd_max (m)' = '{0:F2}' -f ($OrderRadius + $Margin); 'need walk (s)' = ''; 'need +dash (s)' = ''; 'window (s)' = '1.0 s step'; Result = $(if ($mv.Walk -ge $need) { 'PASS (walk)' } else { 'FAIL' }); Note = ('needs steady walking at >= {0:F2} m/s; 4 rings x 1.0 s = 4.2-6 m path per second, keep a curve so earlier rings stay behind' -f $need) })
}

$rows | Format-Table -AutoSize -Wrap | Out-String -Width 260
"Reaction margin {0} s. Leaning Bell's warning is map-adaptive in the build: max(3.5, reaction + escape) for the current map bound." -f $Reaction
