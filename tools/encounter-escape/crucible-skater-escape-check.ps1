<#
.SYNOPSIS
  Worst-case escape check for the Crucible Skater's attack shapes in the shared Brass Foundry mini-boss floor.

.DESCRIPTION
  Documentation support for docs/design/crucible-skater.md section 5. It is NOT the production validator
  (that is Unity/C# work on the shared contract, #6). Pure geometry, no Unity dependency. Same method
  as the Bellkeeper's escape-check.ps1 (docs/bellkeeper-spec) and Sylvara's (docs/sylvara-spec), with the
  Skater's shapes.

  For every sampled boss position (radius 0..BossRadiusMax, by rotational symmetry of the arena) and
  attack direction, it finds the reachable danger point whose straight-line distance to the nearest safe
  point is largest. Danger shapes are expanded by the player hit margin; player centres stay inside the
  arena and outside the boss body. That distance becomes escape time for walk-only and for walk plus one
  dash, and the minimum lock-to-resolve window for each difficulty's reaction margin.

  Casting Run is checked conservatively as the whole swept lane resolving at once when motion starts
  (the real trail only reaches the far end 1.2 s later). Elbow Cast's second leg starts at the first
  leg's end, turns 90 degrees either way, and is checked with the first trail already cooled (safe).

  Limits: straight-line travel (no routing around the boss body), no other players, no acceleration,
  constant-speed dash, grid sampling (~0.1 m danger grid, 0.05 m boundary grid). The Brass Foundry floor
  has no collision mesh yet, so the arena is the packet's proposed 24 m circle.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/encounter-escape/crucible-skater-escape-check.ps1
#>
param(
  [double]$ArenaRadius = 11.7,     # player-centre bound: packet's proposed 24 m floor minus one player radius
  [double]$BossBody = 1.5,         # boss collision radius: the bowl only (3 m across); skates never block
  [double]$PlayerMargin = 0.3,     # player body radius; touching a border counts as inside
  [double]$RunWidth = 3.0, [double]$RunLength = 7.0, [double]$ElbowLength = 5.0,
  [double]$PoolRadius = 1.8,
  [double]$KickReach = 3.5, [double]$KickHalfDeg = 50.0,
  [double]$LadleInner = 2.5, [double]$LadleOuter = 5.0
)

# Keep a player-wide gap between the boss body and the wall: boss centre + body + one player diameter <= wall.
$BossRadiusMax = [math]::Floor(($ArenaRadius + $PlayerMargin - $BossBody - 2 * $PlayerMargin) * 2) / 2

$code = @'
using System;
using System.Collections.Generic;
public static class Escape {
    public static double A, Bmax, Body, M, RW, RL, EL, PR, KR, KH, LI, LO;
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
    // swept body: capsule from (ax,ay) to (bx,by) with radius hw, expanded by M
    static bool InCapsule(double x, double y, double ax, double ay, double bx, double by, double hw) {
        return Seg(x,y,ax,ay,bx,by) <= hw+M;
    }
    public delegate bool Shape(double x, double y);
    // Worst escape distance for one configuration. Returns -1 if some reachable danger point has no safe point.
    public static double Worst(Shape danger, double bx, double by, bool hasBody, double cx, double cy, double reach) {
        double step = 0.05, coarse = 0.1;
        double x0 = Math.Max(-A, cx-reach), x1 = Math.Min(A, cx+reach), y0 = Math.Max(-A, cy-reach), y1 = Math.Min(A, cy+reach);
        Func<double,double,bool> ok = (x,y) => x*x+y*y <= A*A && (!hasBody || (x-bx)*(x-bx)+(y-by)*(y-by) >= (Body+M)*(Body+M));
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
    // Longest travel up to maxL from (sx,sy) along dir whose end stays inside the boss bound.
    static double Fit(double sx, double sy, double dir, double maxL) {
        double ux = Math.Cos(dir), uy = Math.Sin(dir), L = 0;
        while (L < maxL) { double ex = sx+(L+0.1)*ux, ey = sy+(L+0.1)*uy; if (ex*ex+ey*ey > Bmax*Bmax) break; L += 0.1; }
        return Math.Min(L, maxL);
    }
    // Casting Run: body travels up to RL from its centre, end clamped to the boss bound; danger = swept capsule.
    public static string Run() {
        double w = 0; string at = "";
        foreach (double b in BossRadii()) for (int k = 0; k <= 18; k++) {
            double dir = Math.PI*k/18, L = Fit(b, 0, dir, RL);
            if (L < 1) continue;
            double ex = b+L*Math.Cos(dir), ey = L*Math.Sin(dir);
            double d = Worst((x,y) => InCapsule(x,y,b,0,ex,ey,RW/2), b, 0, true, (b+ex)/2, ey/2, L/2+RW+0.5);
            if (d < 0) return "UNSOLVABLE at boss r=" + b + " dir=" + (k*10);
            if (d > w) { w = d; at = string.Format("boss r={0:F1} m, run {1:F1} m", b, L); }
        }
        return string.Format("{0:F2}|{1}", w, at);
    }
    // Elbow Cast leg 2: from the end of a full-length first leg, 90 degrees either way, up to EL, clamped.
    // The body sits at the elbow, so the body circle there is excluded from player space.
    public static string Elbow() {
        double w = 0; string at = "";
        foreach (double b in BossRadii()) for (int k = 0; k <= 36; k++) {
            double dir = Math.PI*k/18, L1 = Fit(b, 0, dir, RL);
            if (L1 < 1) continue;
            double sx = b+L1*Math.Cos(dir), sy = L1*Math.Sin(dir);
            foreach (int side in new[]{-1, 1}) {
                double d2 = dir+side*Math.PI/2, L2 = Fit(sx, sy, d2, EL);
                if (L2 < 1) continue;
                double ex = sx+L2*Math.Cos(d2), ey = sy+L2*Math.Sin(d2);
                double d = Worst((x,y) => InCapsule(x,y,sx,sy,ex,ey,RW/2), sx, sy, true, (sx+ex)/2, (sy+ey)/2, L2/2+RW+0.5);
                if (d < 0) return "UNSOLVABLE at boss r=" + b + " dir=" + (k*10) + " side " + side;
                if (d > w) { w = d; at = string.Format("start r={0:F1} m, first leg {1:F1} m, second leg {2:F1} m", b, L1, L2); }
            }
        }
        return string.Format("{0:F2}|{1}", w, at);
    }
    // Measured Spill / Paired Pour: one pool on a player, anywhere in the arena (boss elsewhere).
    public static string Pool() {
        double w = 0; string at = "";
        for (double c = 0; c <= A; c += 0.5) {
            double cc = c;
            double d = Worst((x,y) => (x-cc)*(x-cc)+y*y <= (PR+M)*(PR+M), 0, 0, false, cc, 0, PR+M+0.5);
            if (d < 0) return "UNSOLVABLE at pool r=" + c;
            if (d > w) { w = d; at = string.Format("pool centre r={0:F1} m", c); }
        }
        return string.Format("{0:F2}|{1}", w, at);
    }
    // Rim Kick: sector from the boss centre, reach measured from the body edge.
    public static string Kick() {
        double half = KH*Math.PI/180, r = Body+KR, w = 0; string at = "";
        foreach (double b in BossRadii()) for (int k = 0; k <= 18; k++) {
            double dir = Math.PI*k/18;
            double d = Worst((x,y) => SectorDist(x,y,b,0,dir,half,r) <= M, b, 0, true, b, 0, r+M+0.5);
            if (d < 0) return "UNSOLVABLE at boss r=" + b + " dir=" + (k*10);
            if (d > w) { w = d; at = string.Format("boss r={0:F1} m, facing {1} deg from the outward radial", b, k*10); }
        }
        return string.Format("{0:F2}|{1}", w, at);
    }
    // Empty the Ladle: annulus from body edge + LI to body edge + LO, centred on the boss.
    public static string Ladle() {
        double ri = Body+LI-M, ro = Body+LO+M, w = 0; string at = "";
        foreach (double b in BossRadii()) {
            double bb = b;
            double d = Worst((x,y) => { double q = (x-bb)*(x-bb)+y*y; return q >= ri*ri && q <= ro*ro; }, b, 0, true, b, 0, ro+0.5);
            if (d < 0) return "UNSOLVABLE at boss r=" + b;
            if (d > w) { w = d; at = string.Format("boss r={0:F1} m", b); }
        }
        return string.Format("{0:F2}|{1}", w, at);
    }
}
'@
Add-Type -TypeDefinition $code -Language CSharp
[Escape]::A = $ArenaRadius; [Escape]::Bmax = $BossRadiusMax; [Escape]::Body = $BossBody; [Escape]::M = $PlayerMargin
[Escape]::RW = $RunWidth; [Escape]::RL = $RunLength; [Escape]::EL = $ElbowLength
[Escape]::PR = $PoolRadius; [Escape]::KR = $KickReach; [Escape]::KH = $KickHalfDeg
[Escape]::LI = $LadleInner; [Escape]::LO = $LadleOuter

$results = [ordered]@{
  'Casting Run / Elbow leg 1' = [Escape]::Run()
  'Elbow Cast leg 2'          = [Escape]::Elbow()
  'Spill / Paired Pour pool'  = [Escape]::Pool()
  'Rim Kick'                  = [Escape]::Kick()
  'Empty the Ladle'           = [Escape]::Ladle()
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
  "## Escape time and minimum lock-to-resolve window, movement set '$($s.Name)' (walk $($s.Walk) m/s, dash $($s.Dash) m in $($s.DashTime) s)"
  ""
  "| Attack | Walk only (s) | Walk + 1 dash (s) | Steady walk-only (ticks) | " + (($tiers | ForEach-Object { "$($_.Name) min window (ticks)" }) -join ' | ') + " |"
  "|---|---|---|---|" + (($tiers | ForEach-Object { '---' }) -join '|') + "|"
  foreach ($k in $results.Keys) {
    $d = [double](($results[$k] -split '\|')[0])
    $w = WalkTime $d $s; $dd = DashTime $d $s
    $steadyWalk = [math]::Ceiling([math]::Round((0.45 + $w) * 60, 6))
    $cells = foreach ($t in $tiers) { $need = $t.Reaction + $(if ($t.Dash) { $dd } else { $w }); [math]::Ceiling([math]::Round($need * 60, 6)) }
    "| $k | {0:F2} | {1:F2} | {2} | {3} |" -f $w, $dd, $steadyWalk, ($cells -join ' | ')
  }
  ""
}
"Tick rate 60 Hz. First Chime is walk-only by design (no dash required); the other tiers allow one dash per attack."
