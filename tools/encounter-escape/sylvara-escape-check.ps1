<#
.SYNOPSIS
  Worst-case escape check for Sylvara's attack shapes in a circular placeholder arena.

.DESCRIPTION
  Documentation support for docs/design/sylvara.md section 5. It is NOT the production validator
  (that is Unity/C# work on the shared contract, #6). Pure geometry, no Unity dependency. Same method
  as the Bellkeeper's escape-check.ps1 (docs/bellkeeper-spec branch), with Sylvara's shapes.

  For every sampled boss position (radius 0..BossRadiusMax, by rotational symmetry of the arena) and
  attack direction, it finds the reachable danger point whose straight-line distance to the nearest safe
  point is largest. Danger shapes are expanded by the player hit margin; player centres stay inside the
  arena and outside the boss body. That distance becomes escape time for walk-only and for walk plus one
  dash, and the minimum lock-to-resolve window for each difficulty's reaction margin.

  Threefold Bloom is checked as a chain: at flower k's resolve, the danger is flowers k..III together
  (conservative: the player may not shelter in a flower that is still warning) and spent flowers are safe.

  Limits: straight-line travel (no routing around the boss body), no other players, no acceleration,
  constant-speed dash, grid sampling (~0.1 m danger grid, 0.05 m boundary grid). The Verdant Chimes arena
  has no dimensions yet, so the arena is a placeholder circle.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/encounter-escape/sylvara-escape-check.ps1
#>
param(
  [double]$ArenaRadius = 11.5,     # player-centre bound: placeholder, same as the key-art test arena
  [double]$BossBody = 1.8,         # boss collision radius: four-root stance, about 3.6 m across on the sheet
  [double]$PlayerMargin = 0.3,     # player body radius; touching a border counts as inside
  [double]$ArcRadius = 5.5, [double]$ArcHalfDeg = 50.0,
  [double]$RootLaneWidth = 1.8, [double]$RootLaneLength = 7.0, [double]$RootSpreadDeg = 60.0,
  [double]$SeedRadius = 1.5,
  [double]$ProcessionWidth = 2.4, [double]$ProcessionLength = 7.0,
  [double]$BloomRadius = 2.0, [double]$BloomSpacing = 4.2, [double]$BloomRing = 5.0
)

# Keep a player-wide gap between the boss body and the wall: boss centre + body + one player diameter <= wall.
$BossRadiusMax = [math]::Floor(($ArenaRadius + $PlayerMargin - $BossBody - 2 * $PlayerMargin) * 2) / 2

$code = @'
using System;
using System.Collections.Generic;
public static class Escape {
    public static double A, Bmax, Body, M, AR, AH, RW, RL, RS, SR, PW, PL, BR, BS, BRing;
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
    public static string Arc() {
        double half = AH*Math.PI/180, w = 0; string at = "";
        foreach (double b in BossRadii()) for (int k = 0; k <= 18; k++) {
            double dir = Math.PI*k/18; // 0..180 deg; mirror symmetry covers the rest
            double d = Worst((x,y) => SectorDist(x,y,b,0,dir,half,AR) <= M, b, 0, true, b, 0, AR+M+0.5);
            if (d < 0) return "UNSOLVABLE at boss r=" + b + " dir=" + (k*10);
            if (d > w) { w = d; at = string.Format("boss r={0:F1} m, facing {1} deg from the outward radial", b, k*10); }
        }
        return string.Format("{0:F2}|{1}", w, at);
    }
    // Rootscript: three lanes fanned RS degrees apart, starting at the boss centre, clipped by the arena.
    public static string Roots() {
        double w = 0; string at = ""; double sp = RS*Math.PI/180;
        foreach (double b in BossRadii()) for (int k = 0; k <= 18; k++) {
            double dir = Math.PI*k/18;
            Shape s = (x,y) => InLane(x,y,b,0,dir-sp,RL,RW/2) || InLane(x,y,b,0,dir,RL,RW/2) || InLane(x,y,b,0,dir+sp,RL,RW/2);
            double d = Worst(s, b, 0, true, b, 0, RL+RW+0.5);
            if (d < 0) return "UNSOLVABLE at boss r=" + b + " dir=" + (k*10);
            if (d > w) { w = d; at = string.Format("boss r={0:F1} m, centre lane {1} deg from the outward radial", b, k*10); }
        }
        return string.Format("{0:F2}|{1}", w, at);
    }
    public static string Seed() {
        double w = 0; string at = "";
        for (double c = 0; c <= A; c += 0.5) {
            double cc = c;
            double d = Worst((x,y) => (x-cc)*(x-cc)+y*y <= (SR+M)*(SR+M), 0, 0, false, cc, 0, SR+M+0.5);
            if (d < 0) return "UNSOLVABLE at seed r=" + c;
            if (d > w) { w = d; at = string.Format("seed centre r={0:F1} m", c); }
        }
        return string.Format("{0:F2}|{1}", w, at);
    }
    // Briar Procession: lane from the boss centre, length up to PL, end clamped to the boss bound.
    public static string Procession() {
        double w = 0; string at = "";
        foreach (double b in BossRadii()) for (int k = 0; k <= 18; k++) {
            double dir = Math.PI*k/18, ux = Math.Cos(dir), uy = Math.Sin(dir);
            double L = 0; while (L < PL) { double ex = b+(L+0.1)*ux, ey = (L+0.1)*uy; if (ex*ex+ey*ey > Bmax*Bmax) break; L += 0.1; }
            if (L < 1) continue;
            double LL = L, cx = b+ux*L/2, cy = uy*L/2;
            double d = Worst((x,y) => InLane(x,y,b,0,dir,LL,PW/2), b, 0, true, cx, cy, L/2+PW+0.5);
            if (d < 0) return "UNSOLVABLE at boss r=" + b + " dir=" + (k*10);
            if (d > w) { w = d; at = string.Format("boss r={0:F1} m, lane {1:F1} m", b, L); }
        }
        return string.Format("{0:F2}|{1}", w, at);
    }
    // Threefold Bloom: flowers I, II, III on an arc of radius BRing around the boss, centres BS apart,
    // the chain centred on the boss's facing. Step k: danger = flowers k..III. Returns the worst over k.
    public static string Bloom() {
        double w = 0; string at = ""; double ang = 2*Math.Asin(Math.Min(1, BS/(2*BRing)));
        foreach (double b in BossRadii()) for (int k = 0; k <= 18; k++) {
            double dir = Math.PI*k/18;
            var cx = new double[3]; var cy = new double[3];
            for (int f = 0; f < 3; f++) { double a = dir+(f-1)*ang; cx[f] = b+BRing*Math.Cos(a); cy[f] = BRing*Math.Sin(a); }
            bool outside = false;
            for (int f = 0; f < 3; f++) if (Math.Sqrt(cx[f]*cx[f]+cy[f]*cy[f]) > A-BR) outside = true;
            if (outside) continue; // the director only uses layouts whose flowers sit fully inside the arena
            for (int step = 0; step < 3; step++) {
                int s0 = step;
                Shape s = (x,y) => { for (int f = s0; f < 3; f++) if ((x-cx[f])*(x-cx[f])+(y-cy[f])*(y-cy[f]) <= (BR+M)*(BR+M)) return true; return false; };
                double d = Worst(s, b, 0, true, cx[step], cy[step], BR+M+BS+0.5);
                if (d < 0) return "UNSOLVABLE at boss r=" + b + " dir=" + (k*10) + " flower " + (step+1);
                if (d > w) { w = d; at = string.Format("boss r={0:F1} m, chain {1} deg from the outward radial, flower {2}", b, k*10, step+1); }
            }
        }
        return string.Format("{0:F2}|{1}", w, at);
    }
}
'@
Add-Type -TypeDefinition $code -Language CSharp
[Escape]::A = $ArenaRadius; [Escape]::Bmax = $BossRadiusMax; [Escape]::Body = $BossBody; [Escape]::M = $PlayerMargin
[Escape]::AR = $ArcRadius; [Escape]::AH = $ArcHalfDeg
[Escape]::RW = $RootLaneWidth; [Escape]::RL = $RootLaneLength; [Escape]::RS = $RootSpreadDeg
[Escape]::SR = $SeedRadius; [Escape]::PW = $ProcessionWidth; [Escape]::PL = $ProcessionLength
[Escape]::BR = $BloomRadius; [Escape]::BS = $BloomSpacing; [Escape]::BRing = $BloomRing

$results = [ordered]@{
  'Pruning Arc'         = [Escape]::Arc()
  'Rootscript (3 lanes)'= [Escape]::Roots()
  'Seed of the Refrain' = [Escape]::Seed()
  'Briar Procession'    = [Escape]::Procession()
  'Threefold Bloom'     = [Escape]::Bloom()
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
