<#
.SYNOPSIS
  Worst-case escape check for the Briar Metronome's attack shapes in the run-loop arena.

.DESCRIPTION
  Documentation support for docs/design/minibosses/briar-metronome.md section 5. It is NOT the production
  validator (that is Unity/C# work on the shared contract, #6). Pure geometry, no Unity dependency. Same method
  as the Bellkeeper's escape-check.ps1 and sylvara-escape-check.ps1, with this boss's shapes.

  Hit tests copy Hazard.Contains in the run loop (unity/Assets/LastBell/Run/Combat/Hazards.cs): radii are
  expanded by the 0.3 m margin, and a sector's half-angle by atan(margin / distance). The annular sector
  (a ring limited to an angle) is the one new shape; it combines the run loop's Ring and Cone tests.

  All radii here are from the boss CENTRE. The packet measures from the collision boundary, so the defaults
  add the 1.0 m body radius (e.g. Inward Prune 2-4.5 m from the boundary = 3.0-5.5 m from the centre).

  For every sampled boss position (radius 0..BossBound, by rotational symmetry) and facing, it finds the
  reachable danger point whose straight-line distance to the nearest safe point is largest. Player centres
  stay inside the arena and outside the boss body. That distance becomes escape time for walk-only and
  walk plus one dash, and the minimum window for each difficulty's reaction margin.

  Multi-beat attacks: each beat is checked on its own, because the packet retires the earlier footprint
  before the later one warns (spec section 3).

  Limits: straight-line travel (no routing around the body), no other players, no acceleration,
  constant-speed dash, grid sampling (~0.1 m danger grid, 0.05 m boundary grid), one Winter Rest
  marker (merged markers are larger but give more edge to leave by).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/encounter-escape/briar-metronome-escape-check.ps1
#>
param(
  [double]$ArenaRadius = 11.5,     # CombatWorld.PlayerBound in the run loop (key-art dais); packet proposes a 24 m disk
  [double]$BossBound = 9.5,        # CombatWorld.BossBound in the run loop
  [double]$BossBody = 1.0,         # two planted root legs, about 2 m stance on the sheet
  [double]$PlayerMargin = 0.3,     # Hazard.Margin
  [double]$InwardIn = 3.0, [double]$InwardOut = 5.5, [double]$InwardHalfDeg = 50.0,
  [double]$OutwardR = 4.0, [double]$OutwardHalfDeg = 45.0,
  [double]$ThrustWidth = 1.4, [double]$ThrustLength = 10.0,
  [double]$ReturnR = 3.5, [double]$ReturnHalfDeg = 37.5,
  [double]$ReachIn = 4.0, [double]$ReachOut = 6.0, [double]$ReachHalfDeg = 75.0,
  [double]$RestRadius = 2.0
)

$code = @'
using System;
using System.Collections.Generic;
public static class Escape {
    public static double A, Bmax, Body, M, II, IO, IH, OR, OH, TW, TL, RR, RH, XI, XO, XH;
    // Hazard.Contains equivalents. (ox,oy) = origin, dir = facing in radians.
    public static bool Sector(double x, double y, double ox, double oy, double dir, double inner, double outer, double halfDeg) {
        double dx = x-ox, dy = y-oy, d = Math.Sqrt(dx*dx+dy*dy);
        if (d > outer+M) return false;
        if (inner > 0 && d < inner-M) return false;
        if (d < 1e-6) return true;
        double a = Math.Atan2(dy,dx)-dir; while (a > Math.PI) a -= 2*Math.PI; while (a < -Math.PI) a += 2*Math.PI;
        return Math.Abs(a)*180/Math.PI <= halfDeg + Math.Atan2(M, d)*180/Math.PI;
    }
    public static bool Lane(double x, double y, double ox, double oy, double dir, double L, double w) {
        double ux = Math.Cos(dir), uy = Math.Sin(dir);
        double lx = (x-ox)*ux+(y-oy)*uy, ly = -(x-ox)*uy+(y-oy)*ux;
        return Math.Abs(ly) <= w/2+M && lx >= -M && lx <= L+M;
    }
    public delegate bool Shape(double x, double y);
    // Worst escape distance for one configuration. Returns -1 if some reachable danger point has no safe point.
    public static double Worst(Shape danger, double bx, double by, bool hasBody, double cx, double cy, double reach) {
        double step = 0.05, coarse = 0.1;
        double x0 = Math.Max(-A, cx-reach), x1 = Math.Min(A, cx+reach), y0 = Math.Max(-A, cy-reach), y1 = Math.Min(A, cy+reach);
        Func<double,double,bool> ok = (x,y) => x*x+y*y <= A*A && (!hasBody || (x-bx)*(x-bx)+(y-by)*(y-by) >= (Body+M)*(Body+M));
        var safe = new List<double[]>();
        int nx = (int)Math.Ceiling((x1-x0)/step), ny = (int)Math.Ceiling((y1-y0)/step);
        var grid = new bool[nx+1, ny+1];
        for (int i = 0; i <= nx; i++) for (int j = 0; j <= ny; j++) grid[i,j] = danger(x0+i*step, y0+j*step);
        for (int i = 0; i <= nx; i++) for (int j = 0; j <= ny; j++) {
            if (grid[i,j]) continue; double x = x0+i*step, y = y0+j*step; if (!ok(x,y)) continue;
            bool edge = (i>0&&grid[i-1,j])||(i<nx&&grid[i+1,j])||(j>0&&grid[i,j-1])||(j<ny&&grid[i,j+1]);
            if (edge) safe.Add(new[]{x,y});
        }
        // bucket the safe boundary points in 0.5 m cells so the nearest-safe search only scans nearby cells
        double cell = 0.5; int bx0 = (int)Math.Floor(x0/cell), by0 = (int)Math.Floor(y0/cell);
        int cw = (int)Math.Ceiling((x1-x0)/cell)+2, ch = (int)Math.Ceiling((y1-y0)/cell)+2;
        var buckets = new List<double[]>[cw, ch];
        foreach (var s in safe) {
            int ci = (int)Math.Floor(s[0]/cell)-bx0, cj = (int)Math.Floor(s[1]/cell)-by0;
            if (buckets[ci,cj] == null) buckets[ci,cj] = new List<double[]>();
            buckets[ci,cj].Add(s);
        }
        double worst = 0; bool any = false;
        for (double x = x0; x <= x1; x += coarse) for (double y = y0; y <= y1; y += coarse) {
            if (!ok(x,y) || !danger(x,y)) continue; any = true;
            double best = double.MaxValue;
            int pi = (int)Math.Floor(x/cell)-bx0, pj = (int)Math.Floor(y/cell)-by0;
            for (int ring = 0; ring < Math.Max(cw, ch); ring++) {
                if (best < double.MaxValue && (ring-1)*cell > Math.Sqrt(best)) break;
                for (int i = pi-ring; i <= pi+ring; i++) for (int j = pj-ring; j <= pj+ring; j++) {
                    if (Math.Max(Math.Abs(i-pi), Math.Abs(j-pj)) != ring || i < 0 || j < 0 || i >= cw || j >= ch || buckets[i,j] == null) continue;
                    foreach (var s in buckets[i,j]) { double d = (s[0]-x)*(s[0]-x)+(s[1]-y)*(s[1]-y); if (d < best) best = d; }
                }
            }
            if (best == double.MaxValue) return -1;
            worst = Math.Max(worst, Math.Sqrt(best));
        }
        return any ? worst : 0;
    }
    static double[] BossRadii() { var l = new List<double>(); for (double b = 0; b < Bmax; b += 1.0) l.Add(b); l.Add(Bmax); return l.ToArray(); }
    // Shapes anchored on the boss centre at (b,0), facing dir.
    static bool Shp(int id, double x, double y, double b, double dir) {
        switch (id) {
            case 0: return Sector(x,y,b,0,dir,II,IO,IH);   // Inward Prune
            case 1: return Sector(x,y,b,0,dir,0,OR,OH);    // Outward Prune
            case 2: return Lane(x,y,b,0,dir,TL,TW);        // Seedline Thrust
            case 3: return Sector(x,y,b,0,dir,0,RR,RH);    // Returning Prune beat 2
            default: return Sector(x,y,b,0,dir,XI,XO,XH);  // Overgrown Reach beat 2
        }
    }
    // Swept over boss positions and facings 0..180 deg (mirror symmetry covers the rest).
    public static string FromBoss(int id, double reach) {
        double w = 0; string at = "";
        foreach (double b in BossRadii()) for (int k = 0; k <= 18; k++) {
            double dir = Math.PI*k/18, bb = b;
            double d = Worst((x,y) => Shp(id,x,y,bb,dir), b, 0, true, b, 0, reach);
            if (d < 0) return "UNSOLVABLE at boss r=" + b + " dir=" + (k*10);
            if (d > w) { w = d; at = string.Format("boss r={0:F1} m, facing {1} deg from the outward radial", b, k*10); }
        }
        return string.Format("{0:F2}|{1}", w, at);
    }
    public static string Circle(double r) {
        double w = 0; string at = "";
        for (double c = 0; c <= A; c += 0.5) {
            double cc = c;
            double d = Worst((x,y) => (x-cc)*(x-cc)+y*y <= (r+M)*(r+M), 0, 0, false, cc, 0, r+M+0.5);
            if (d < 0) return "UNSOLVABLE at marker r=" + c;
            if (d > w) { w = d; at = string.Format("marker centre r={0:F1} m", c); }
        }
        return string.Format("{0:F2}|{1}", w, at);
    }
}
'@
Add-Type -TypeDefinition $code -Language CSharp
[Escape]::A = $ArenaRadius; [Escape]::Bmax = $BossBound; [Escape]::Body = $BossBody; [Escape]::M = $PlayerMargin

[Escape]::II = $InwardIn; [Escape]::IO = $InwardOut; [Escape]::IH = $InwardHalfDeg; [Escape]::OR = $OutwardR; [Escape]::OH = $OutwardHalfDeg
[Escape]::TW = $ThrustWidth; [Escape]::TL = $ThrustLength; [Escape]::RR = $ReturnR; [Escape]::RH = $ReturnHalfDeg
[Escape]::XI = $ReachIn; [Escape]::XO = $ReachOut; [Escape]::XH = $ReachHalfDeg

$results = [ordered]@{
  'Inward Prune (P1; Returning beat 1)'  = [Escape]::FromBoss(0, $InwardOut + 1)
  'Outward Prune (P1; Overgrown beat 1)' = [Escape]::FromBoss(1, $OutwardR + 1)
  'Seedline Thrust'                      = [Escape]::FromBoss(2, $ThrustLength + 1)
  'Returning Prune beat 2 (inner cone)'  = [Escape]::FromBoss(3, $ReturnR + 1)
  'Overgrown Reach beat 2 (outer arc)'   = [Escape]::FromBoss(4, $ReachOut + 1)
  'Winter Rest (one marker)'             = [Escape]::Circle($RestRadius)
}

# Movement sets: the run loop / game design (#33, PlayerController.WalkSpeed) and the earlier Unity test walk (#27).
$sets = @(
  @{ Name = 'run loop';  Walk = 6.0; Dash = 3.0; DashTime = 0.20 },
  @{ Name = 'prototype'; Walk = 4.2; Dash = 3.0; DashTime = 0.20 }
)
function WalkTime($d, $s) { $d / $s.Walk }
function DashTime($d, $s) { if ($d -le $s.Dash) { $s.DashTime * $d / $s.Dash } else { $s.DashTime + ($d - $s.Dash) / $s.Walk } }

$tiers = @(
  @{ Name = 'First Chime';    Reaction = 0.60; Dash = $false },
  @{ Name = 'Steady Ticking'; Reaction = 0.45; Dash = $true },
  @{ Name = 'Striking Hour';  Reaction = 0.35; Dash = $true },
  @{ Name = 'Midnight Toll';  Reaction = 0.30; Dash = $true }
)

"Arena player bound $ArenaRadius m, boss centre bound $BossBound m, boss body $BossBody m, player margin $PlayerMargin m."
""
"## Worst-case escape distance (m)"
""
"| Attack | d_max | Worst configuration |"
"|---|---|---|"
foreach ($k in $results.Keys) { $p = $results[$k] -split '\|'; "| $k | $($p[0]) | $($p[1]) |" }
""
foreach ($s in $sets) {
  "## Escape time and minimum window, movement set '$($s.Name)' (walk $($s.Walk) m/s, dash $($s.Dash) m in $($s.DashTime) s)"
  ""
  "| Attack | Walk only (s) | Walk + 1 dash (s) | " + (($tiers | ForEach-Object { "$($_.Name) (ticks)" }) -join ' | ') + " |"
  "|---|---|---|" + (($tiers | ForEach-Object { '---' }) -join '|') + "|"
  foreach ($k in $results.Keys) {
    $d = [double](($results[$k] -split '\|')[0])
    $w = WalkTime $d $s; $dd = DashTime $d $s
    $cells = foreach ($t in $tiers) { $need = $t.Reaction + $(if ($t.Dash) { $dd } else { $w }); [math]::Ceiling([math]::Round($need * 60, 6)) }
    "| $k | {0:F2} | {1:F2} | {2} |" -f $w, $dd, ($cells -join ' | ')
  }
  ""
}
"Tick rate 60 Hz. First Chime is walk-only by design (no dash required); the other tiers allow one dash per attack."
