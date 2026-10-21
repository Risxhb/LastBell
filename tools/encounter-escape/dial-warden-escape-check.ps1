<#
.SYNOPSIS
  Worst-case escape check for the Dial Warden's attack shapes in a circular placeholder arena.

.DESCRIPTION
  Documentation support for docs/design/dial-warden.md section 5. It is NOT the production validator
  (that is Unity/C# work on the shared contract, #6). Pure geometry, no Unity dependency. Same method
  as the Bellkeeper and Sylvara escape checks, with the Dial Warden's shapes.

  Mini-boss packet rule: reach is measured from the boss collision boundary, so every shape that starts
  at the boss uses radius (body + listed reach). Cones and lanes have their apex at the boss centre.

  For every sampled boss position (radius 0..BossRadiusMax, by rotational symmetry of the arena) and
  attack direction, it finds the reachable danger point whose straight-line distance to the nearest safe
  point is largest. Danger shapes are expanded by the player hit margin; player centres stay inside the
  arena and outside the boss body. That distance becomes escape time for walk-only and for walk plus one
  dash, and the minimum window for each difficulty's reaction margin.

  Offset Sweep beat II is checked alone (beat I has cleared). Unwound Dial's burst is checked alone (the
  ring has cleared), at both the packet's 2 m and this spec's 2.5 m radius.

  Limits: straight-line travel (no routing around the boss body), no other players, no acceleration,
  constant-speed dash, grid sampling (~0.1 m danger grid, 0.05 m boundary grid). The Skyward Spire arena
  has no collision mesh yet, so the arena is a placeholder circle.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/encounter-escape/dial-warden-escape-check.ps1
#>
param(
  [double]$ArenaRadius = 11.5,     # player-centre bound: packet's 24 m floor minus rim and player radius
  [double]$BossBody = 1.4,         # boss collision radius: the run-loop stand-in value (#42), 2.8 m across
  [double]$PlayerMargin = 0.3,     # Hazard.Margin in the run loop; touching a border counts as inside
  [double]$Clearance = 3.0,        # packet: at least 3 m of traversal space around the body
  [double]$SweepReach = 4.0, [double]$SweepHalfDeg = 50.0,
  [double]$OffsetReach = 4.0, [double]$OffsetHalfDeg = 40.0,
  [double]$DialInner = 2.5, [double]$DialOuter = 5.0,
  [double]$BurstPacket = 2.0, [double]$BurstSpec = 2.5,
  [double]$PinWidth = 1.5, [double]$PinReach = 10.0,
  [double]$QuarterReach = 7.0, [double]$QuarterHalfDeg = 30.0
)

# Boss centre + body + clearance must stay inside the player bound.
$BossRadiusMax = [math]::Floor(($ArenaRadius + $PlayerMargin - $BossBody - $Clearance) * 2) / 2

$code = @'
using System;
using System.Collections.Generic;
public static class Escape {
    public static double A, Bmax, Body, M;
    static double Seg(double px, double py, double ax, double ay, double bx, double by) {
        double vx = bx-ax, vy = by-ay, wx = px-ax, wy = py-ay;
        double t = Math.Max(0, Math.Min(1, (wx*vx+wy*vy)/(vx*vx+vy*vy)));
        double dx = px-(ax+t*vx), dy = py-(ay+t*vy); return Math.Sqrt(dx*dx+dy*dy);
    }
    // distance from point to the (unexpanded) sector with apex (ox,oy)
    static double SectorDist(double px, double py, double ox, double oy, double dir, double half, double r) {
        double dx = px-ox, dy = py-oy, d = Math.Sqrt(dx*dx+dy*dy);
        double a = Math.Atan2(dy,dx)-dir; while (a > Math.PI) a -= 2*Math.PI; while (a < -Math.PI) a += 2*Math.PI;
        if (Math.Abs(a) <= half) return Math.Max(0, d-r);
        double e1 = Seg(px,py,ox,oy,ox+r*Math.Cos(dir+half),oy+r*Math.Sin(dir+half));
        double e2 = Seg(px,py,ox,oy,ox+r*Math.Cos(dir-half),oy+r*Math.Sin(dir-half));
        return Math.Min(e1,e2);
    }
    // rectangle from (ox,oy) along dir, length L, half-width hw, expanded by M
    static bool InLane(double x, double y, double ox, double oy, double dir, double L, double hw) {
        double ux = Math.Cos(dir), uy = Math.Sin(dir);
        double lx = (x-ox)*ux+(y-oy)*uy, ly = -(x-ox)*uy+(y-oy)*ux;
        return Math.Abs(ly) <= hw+M && lx >= -M && lx <= L+M;
    }
    public delegate bool Shape(double x, double y);
    // Worst escape distance for one configuration. Returns -1 if some reachable danger point has no safe point.
    public static double Worst(Shape danger, double bx, double by, double cx, double cy, double reach) {
        double step = 0.05, coarse = 0.1;
        double x0 = Math.Max(-A, cx-reach), x1 = Math.Min(A, cx+reach), y0 = Math.Max(-A, cy-reach), y1 = Math.Min(A, cy+reach);
        Func<double,double,bool> ok = (x,y) => x*x+y*y <= A*A && (x-bx)*(x-bx)+(y-by)*(y-by) >= (Body+M)*(Body+M);
        var safe = new List<double[]>();
        int nx = (int)Math.Ceiling((x1-x0)/step), ny = (int)Math.Ceiling((y1-y0)/step);
        var grid = new bool[nx+1, ny+1]; // true = danger
        for (int i = 0; i <= nx; i++) for (int j = 0; j <= ny; j++) grid[i,j] = danger(x0+i*step, y0+j*step);
        for (int i = 0; i <= nx; i++) for (int j = 0; j <= ny; j++) {
            if (grid[i,j]) continue; double x = x0+i*step, y = y0+j*step; if (!ok(x,y)) continue;
            bool edge = (i>0&&grid[i-1,j])||(i<nx&&grid[i+1,j])||(j>0&&grid[i,j-1])||(j<ny&&grid[i,j+1]);
            if (edge) safe.Add(new[]{x,y});
        }
        double worst = 0; bool any = false;
        for (double x = x0; x <= x1; x += coarse) for (double y = y0; y <= y1; y += coarse) {
            if (!ok(x,y) || !danger(x,y)) continue; any = true;
            double best = double.MaxValue;
            foreach (var s in safe) { double d = (s[0]-x)*(s[0]-x)+(s[1]-y)*(s[1]-y); if (d < best) best = d; }
            if (best == double.MaxValue) return -1;
            worst = Math.Max(worst, Math.Sqrt(best));
        }
        return any ? worst : 0;
    }
    static double[] BossRadii() { var l = new List<double>(); for (double b = 0; b < Bmax; b += 1.0) l.Add(b); l.Add(Bmax); return l.ToArray(); }
    // Cone from the boss centre, radius body + reach.
    public static string Cone(double reach, double halfDeg) {
        double half = halfDeg*Math.PI/180, R = Body+reach, w = 0; string at = "";
        foreach (double b in BossRadii()) for (int k = 0; k <= 18; k++) {
            double dir = Math.PI*k/18; // 0..180 deg; mirror symmetry covers the rest
            double bb = b;
            double d = Worst((x,y) => SectorDist(x,y,bb,0,dir,half,R) <= M, b, 0, b, 0, R+M+0.5);
            if (d < 0) return "UNSOLVABLE at boss r=" + b + " dir=" + (k*10);
            if (d > w) { w = d; at = string.Format("boss r={0:F1} m, facing {1} deg from the outward radial", b, k*10); }
        }
        return string.Format("{0:F2}|{1}", w, at);
    }
    // Annulus around the boss, from body+inner to body+outer.
    public static string Ring(double inner, double outer) {
        double ri = Body+inner-M, ro = Body+outer+M, w = 0; string at = "";
        foreach (double b in BossRadii()) {
            double bb = b;
            double d = Worst((x,y) => { double q = (x-bb)*(x-bb)+y*y; return q >= ri*ri && q <= ro*ro; }, b, 0, b, 0, ro+0.5);
            if (d < 0) return "UNSOLVABLE at boss r=" + b;
            if (d > w) { w = d; at = string.Format("boss r={0:F1} m", b); }
        }
        return string.Format("{0:F2}|{1}", w, at);
    }
    // Disc around the boss out to body+reach.
    public static string Burst(double reach) {
        double ro = Body+reach+M, w = 0; string at = "";
        foreach (double b in BossRadii()) {
            double bb = b;
            double d = Worst((x,y) => (x-bb)*(x-bb)+y*y <= ro*ro, b, 0, b, 0, ro+0.5);
            if (d < 0) return "UNSOLVABLE at boss r=" + b;
            if (d > w) { w = d; at = string.Format("boss r={0:F1} m", b); }
        }
        return string.Format("{0:F2}|{1}", w, at);
    }
    // Lane from the boss centre, length body + reach, clipped by the arena wall.
    public static string Lane(double width, double reach) {
        double L = Body+reach, w = 0; string at = "";
        foreach (double b in BossRadii()) for (int k = 0; k <= 18; k++) {
            double dir = Math.PI*k/18, bb = b;
            double d = Worst((x,y) => InLane(x,y,bb,0,dir,L,width/2), b, 0, b, 0, L+width+0.5);
            if (d < 0) return "UNSOLVABLE at boss r=" + b + " dir=" + (k*10);
            if (d > w) { w = d; at = string.Format("boss r={0:F1} m, lane {1} deg from the outward radial", b, k*10); }
        }
        return string.Format("{0:F2}|{1}", w, at);
    }
}
'@
Add-Type -TypeDefinition $code -Language CSharp
[Escape]::A = $ArenaRadius; [Escape]::Bmax = $BossRadiusMax; [Escape]::Body = $BossBody; [Escape]::M = $PlayerMargin

$results = [ordered]@{
  'Hand Sweep'                  = [Escape]::Cone($SweepReach, $SweepHalfDeg)
  'Offset Sweep, beat II'       = [Escape]::Cone($OffsetReach, $OffsetHalfDeg)
  'Hollow Dial ring'            = [Escape]::Ring($DialInner, $DialOuter)
  "Unwound burst, $BurstPacket m (packet)" = [Escape]::Burst($BurstPacket)
  "Unwound burst, $BurstSpec m (spec)"     = [Escape]::Burst($BurstSpec)
  'Minute Pin'                  = [Escape]::Lane($PinWidth, $PinReach)
  'Stolen Quarter'              = [Escape]::Cone($QuarterReach, $QuarterHalfDeg)
}

# Movement sets: game-design defaults (#33) and the current Unity test controller walk (#27, #42).
$sets = @(
  @{ Name = 'design';     Walk = 6.0; Dash = 3.0; DashTime = 0.20 },
  @{ Name = 'prototype';  Walk = 4.2; Dash = 3.0; DashTime = 0.20 }
)
function WalkTime($d, $s) { $d / $s.Walk }
function DashTime($d, $s) { if ($d -le $s.Dash) { $s.DashTime * $d / $s.Dash } else { $s.DashTime + ($d - $s.Dash) / $s.Walk } }

$tiers = @(
  @{ Name = 'First Chime';    Reaction = 0.60; Dash = $false },
  @{ Name = 'Steady Ticking'; Reaction = 0.45; Dash = $true },
  @{ Name = 'Striking Hour';  Reaction = 0.35; Dash = $true },
  @{ Name = 'Midnight Toll';  Reaction = 0.30; Dash = $true }
)

"Arena player bound $ArenaRadius m, boss centre bound $BossRadiusMax m, boss body $BossBody m, player margin $PlayerMargin m."
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
  "| Attack | Walk only (s) | Walk + 1 dash (s) | " + (($tiers | ForEach-Object { "$($_.Name) min window (ticks)" }) -join ' | ') + " |"
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
