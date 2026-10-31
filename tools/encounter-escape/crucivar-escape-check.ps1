<#
.SYNOPSIS
  Worst-case escape check for Crucivar's ability set v2 (bosses/2-brass-foundry-crucivar/abilities-v2/ABILITIES.md).

.DESCRIPTION
  Same method as escape-check.ps1 (the Bellkeeper's, collab docs/bellkeeper-spec): pure geometry on a grid, no Unity.
  For every case it finds the worst straight-line distance a player must cover (to leave a danger shape, or to enter a
  soak / lane / band), converts it to time at the 4.2 m/s prototype walk (walk only, and walk plus one 3 m dash) and at
  the 6 m/s design walk, and compares it with that ability's warning. PASS needs a reaction margin of at least 0.5 s at
  4.2 m/s with one dash (finite stamina: a dash costs 30 of 100) and at 6 m/s walking only.
  Shapes are expanded by the player hit margin (Hazard.Margin 0.3 m); player centres stay inside the Brass Foundry
  bound (13.7 m) and outside the boss body.

  Limits: straight-line travel, no other players' bodies, no acceleration, grid sampling (0.1 m danger grid, 0.05 m
  boundary grid). Two-partner cases (Gear Chain) assume both partners move.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/encounter-escape/crucivar-escape-check.ps1
  powershell -ExecutionPolicy Bypass -File tools/encounter-escape/crucivar-escape-check.ps1 -Level Easy
#>
param(
  [ValidateSet('Easy', 'Medium', 'Hard')][string]$Level = 'Medium',
  [double]$Arena = 13.7,          # Brass Foundry player bound (RunMaps)
  [double]$BossBody = 1.7,        # CrucivarEncounter.BodyRadius
  [double]$M = 0.3,               # Hazard.Margin
  [double]$Reaction = 0.5         # minimum reaction margin to pass
)

# ---------------- ability numbers (Medium; Easy stretches warnings x1.25 and widens the chain band) ----------------
$ws = if ($Level -eq 'Easy') { 1.25 } else { 1.0 }
$N = @{
  BayR = 6.0; TowerR = 2.0; MouldWarn = 4.2 * $ws; SoloGap = 2.6 * $ws
  PressR = 2.2; PressWarn = 2.0
  ChainMin = 6.0; ChainMax = 10.0; ChainWarn = 3.0 * $ws
  RimInner = 9.5; RimWarn = 2.0 * $ws; BlastR = 4.5; BlastWarn = 3.0 * $ws; Pull = 1.5; PullToBlast = 0.9 * $ws; BossNearCentre = 3.0
  LaneW = 2.6; CastWarn = 3.5 * $ws; CastLockToHit = 2.0 * $ws; StampR = 1.2; StampWarn = 2.0 * $ws; Spokes = 6
}
if ($Level -eq 'Easy') { $N.ChainMin = 5.0; $N.ChainMax = 11.0 }

$code = @'
using System;
using System.Collections.Generic;
public static class CEsc {
    public static double A, Body, M;
    static bool InArena(double x, double y) { return x*x + y*y <= A*A; }
    // worst distance from a reachable danger point to the nearest reachable safe point (leave a shape)
    static double Exit(Func<double,double,bool> danger, Func<double,double,bool> blocked, double cx, double cy, double reach) {
        double step = 0.05, coarse = 0.1;
        double x0 = Math.Max(-A, cx-reach), x1 = Math.Min(A, cx+reach), y0 = Math.Max(-A, cy-reach), y1 = Math.Min(A, cy+reach);
        var safe = new List<double[]>();
        int nx = (int)Math.Ceiling((x1-x0)/step), ny = (int)Math.Ceiling((y1-y0)/step);
        var g = new bool[nx+1, ny+1];
        for (int i = 0; i <= nx; i++) for (int j = 0; j <= ny; j++) g[i,j] = danger(x0+i*step, y0+j*step);
        for (int i = 0; i <= nx; i++) for (int j = 0; j <= ny; j++) {
            if (g[i,j]) continue; double x = x0+i*step, y = y0+j*step;
            if (!InArena(x,y) || (blocked != null && blocked(x,y))) continue;
            bool edge = (i>0&&g[i-1,j])||(i<nx&&g[i+1,j])||(j>0&&g[i,j-1])||(j<ny&&g[i,j+1]);
            if (edge) safe.Add(new[]{x,y});
        }
        double worst = 0;
        for (double x = x0; x <= x1; x += coarse) for (double y = y0; y <= y1; y += coarse) {
            if (!InArena(x,y) || (blocked != null && blocked(x,y)) || !danger(x,y)) continue;
            double best = double.MaxValue;
            foreach (var s in safe) { double d = (s[0]-x)*(s[0]-x)+(s[1]-y)*(s[1]-y); if (d < best) best = d; }
            if (best == double.MaxValue) return -1;
            worst = Math.Max(worst, Math.Sqrt(best));
        }
        return worst;
    }
    // worst over reachable points of an analytic distance-to-inside function (enter a soak / lane)
    static double Enter(Func<double,double,double> distToInside, Func<double,double,bool> blocked) {
        double worst = 0;
        for (double x = -A; x <= A; x += 0.1) for (double y = -A; y <= A; y += 0.1) {
            if (!InArena(x,y) || (blocked != null && blocked(x,y))) continue;
            worst = Math.Max(worst, Math.Max(0, distToInside(x,y)));
        }
        return worst;
    }
    static double D(double x, double y, double cx, double cy) { return Math.Sqrt((x-cx)*(x-cx)+(y-cy)*(y-cy)); }
    static double SegDist(double px, double py, double ax, double ay, double bx, double by) {
        double vx = bx-ax, vy = by-ay, wx = px-ax, wy = py-ay;
        double t = Math.Max(0, Math.Min(1, (wx*vx+wy*vy)/(vx*vx+vy*vy)));
        return D(px, py, ax+t*vx, ay+t*vy);
    }
    // 1. Mould Press: bays at (b,b) and (-b,-b); inside = within tin of a bay centre
    public static double MouldNearest(double b, double tin) { return Enter((x,y) => Math.Min(D(x,y,b,b), D(x,y,-b,-b)) - tin, null); }
    public static double MouldFar(double b, double tin) { return Enter((x,y) => D(x,y,b,b) - tin, null); }
    // 3. Bellows Draw: slag rim = everything at r >= inner - M
    public static double RimExit(double inner) { return Exit((x,y) => Math.Sqrt(x*x+y*y) >= inner - M, null, 0, 0, A); }
    // point-blank blast around a boss at (bx,0); the slag edge is still burning and the body blocks
    public static double BlastExit(double bx, double r, double rimInner) {
        double bb = Body + 0.45;
        return Exit((x,y) => D(x,y,bx,0) <= r + M, (x,y) => D(x,y,bx,0) < bb || Math.Sqrt(x*x+y*y) >= rimInner - M, bx, 0, r + M + 1);
    }
    // 4. Great Casting: join a pour lane from the centre to the edge (the body blocks the centre)
    public static double LaneEnter(double w) { double bb = Body + 0.45; return Enter((x,y) => SegDist(x,y,0,0,A,0) - (w/2 + M), (x,y) => D(x,y,0,0) < bb); }
    // one ring of die-stamps on the spokes: leave one without entering a neighbour
    public static double StampExit(double rr, double sr, int spokes) {
        double bb = Body + 0.45; var c = new double[spokes, 2];
        for (int k = 0; k < spokes; k++) { double a = 2*Math.PI*k/spokes; c[k,0] = rr*Math.Cos(a); c[k,1] = rr*Math.Sin(a); }
        Func<double,double,bool> dz = (x,y) => { for (int k = 0; k < spokes; k++) if (D(x,y,c[k,0],c[k,1]) <= sr + M) return true; return false; };
        return Exit(dz, (x,y) => D(x,y,0,0) < bb, rr, 0, sr + M + 2);
    }
}
'@
Add-Type -TypeDefinition $code -Language CSharp
[CEsc]::A = $Arena; [CEsc]::Body = $BossBody; [CEsc]::M = $M

$rows = New-Object System.Collections.ArrayList
function Add-Row($ability, $case, $dist, $window) { [void]$rows.Add([pscustomobject]@{ Ability = $ability; Case = $case; Dist = $dist; Window = $window }) }

# 1. Mould Press: soak towers on four die bays at r 6 m (diagonals); pairs alternate A/C, B/D
$b = $N.BayR / [math]::Sqrt(2); $tin = $N.TowerR + $M
Add-Row 'Mould Press' 'N>=2: reach the nearer of the two opposite bays, from anywhere' ([CEsc]::MouldNearest($b, $tin)) $N.MouldWarn
Add-Row 'Mould Press' 'N=4/8: a bad split sends you to the FAR bay of the pair' ([CEsc]::MouldFar($b, $tin)) $N.MouldWarn
Add-Row 'Mould Press' 'N=1: tower I to tower II (adjacent bay), from the far side of I' ([math]::Sqrt(2) * $N.BayR) $N.SoloGap
if ($Level -eq 'Hard') { Add-Row 'Second Press (Hard)' 'leave the re-stamped tower from its centre' ($N.PressR + $M) $N.PressWarn }

# 2. Gear Chain: partners end 6-10 m apart; odd / solo player 6-10 m from its rim post
Add-Row 'Gear Chain' 'partners on the same spot: each walks min / 2' ($N.ChainMin / 2) $N.ChainWarn
Add-Row 'Gear Chain' 'partners at opposite edges: each walks (sep - max) / 2' (((2 * $Arena) - $N.ChainMax) / 2) $N.ChainWarn
Add-Row 'Gear Chain' 'solo / odd: standing on its rim post, walk in to min' $N.ChainMin $N.ChainWarn
Add-Row 'Gear Chain + Bellows' 'partners at opposite sides of the slag-edge disc' (((2 * ($N.RimInner - $M)) - $N.ChainMax) / 2) $N.ChainWarn

# 3. Bellows Draw: slag rim from r 9.5 out, inhale pull, point-blank blast r 4.5 around the boss
Add-Row 'Bellows Draw' 'slag rim: from the arena edge inward' ([CEsc]::RimExit($N.RimInner)) $N.RimWarn
$wb = 0; for ($br = 0; $br -le $N.BossNearCentre; $br += 1.0) { $d = [CEsc]::BlastExit($br, $N.BlastR, $N.RimInner); if ($d -lt 0) { $wb = -1; break }; if ($d -gt $wb) { $wb = $d } }
Add-Row 'Bellows Draw' 'blast: from the boss body outward, slag edge still burning (boss <= 3 m from centre)' $wb $N.BlastWarn
Add-Row 'Bellows Draw' 'after the 1.5 m inhale pull: walk back out before the blast' $N.Pull $N.PullToBlast

# 4. The Great Casting: pour line stack from the boss to the farthest player, then marching die-stamps on 6 spokes
Add-Row 'Great Casting' 'join the pour line from anywhere (its boss end never moves)' ([CEsc]::LaneEnter($N.LaneW)) $N.CastWarn
Add-Row 'Great Casting' 'solo: dodge the locked pour lane' ($N.LaneW / 2 + $M) $N.CastLockToHit
$wst = 0; $at = 0; foreach ($rr in 4.5, 6.5, 8.5, 10.5, 12.5) { $d = [CEsc]::StampExit($rr, $N.StampR, $N.Spokes); if ($d -lt 0) { $wst = -1; $at = $rr; break }; if ($d -gt $wst) { $wst = $d; $at = $rr } }
Add-Row 'Great Casting' "marching stamps: leave a stamp without entering a neighbouring spoke (worst ring r $at m)" $wst $N.StampWarn

# ---------------- report ----------------
"## Crucivar ability set v2: escape check ($Level)"
""
"Arena bound $Arena m, boss body $BossBody m, hit margin $M m. PASS = reaction margin >= $Reaction s at 4.2 m/s with one 3 m / 0.2 s dash, and at 6 m/s walking."
""
"| Ability | Worst case | Distance (m) | Window (s) | 4.2 walk (s) | 4.2 walk + dash (s) | 6.0 walk (s) | Margin at 4.2 + dash (s) | Result |"
"|---|---|---|---|---|---|---|---|---|"
$fail = 0
foreach ($r in $rows) {
  if ($r.Dist -lt 0) { "| $($r.Ability) | $($r.Case) | none | $($r.Window) | - | - | - | - | **FAIL (no safe point)** |"; $fail++; continue }
  $w42 = $r.Dist / 4.2; $dash = if ($r.Dist -le 3) { 0.2 * $r.Dist / 3 } else { 0.2 + ($r.Dist - 3) / 4.2 }; $w6 = $r.Dist / 6.0
  $margin = $r.Window - $dash; $ok = ($margin -ge $Reaction) -and (($r.Window - $w6) -ge $Reaction)
  if (-not $ok) { $fail++ }
  "| {0} | {1} | {2:F2} | {3:F2} | {4:F2} | {5:F2} | {6:F2} | {7:F2} | {8} |" -f $r.Ability, $r.Case, $r.Dist, $r.Window, $w42, $dash, $w6, $margin, $(if ($ok) { 'PASS' } else { '**FAIL**' })
}
""
"Failures: $fail"
exit $fail
