<#
.SYNOPSIS
  Worst-case escape check for the Vesper Seedkeeper mini-boss's attack shapes in a circular placeholder arena.

.DESCRIPTION
  Documentation support for docs/design/vesper-seedkeeper.md section 5. It is NOT the production validator
  (that is Unity/C# work on the shared contract, #6). Pure geometry, no Unity dependency. Same method as the
  Bellkeeper's escape-check.ps1 and Sylvara's sylvara-escape-check.ps1, with the Seedkeeper's shapes.

  For every sampled boss position (radius 0..BossRadiusMax; the packet keeps the boss within 3 m of the
  arena centre) and attack direction, it finds the reachable danger point whose straight-line distance to the
  nearest safe point is largest. Danger shapes are expanded by the player hit margin; player centres stay
  inside the arena and outside the boss body. That distance becomes escape time for walk-only and for walk
  plus one dash, and the minimum lock-to-resolve window for each difficulty's reaction margin.

  Multi-beat attacks are checked beat by beat: spent footprints are safe, so Alternating Petals II counts
  only cone II as danger, and the Bloom ring treats its hollow centre (the retracted disk) as safe.

  Limits: straight-line travel (no routing around the boss body), no other players, no acceleration,
  constant-speed dash, grid sampling (~0.1 m danger grid, 0.05 m boundary grid). The Verdant Chimes arena
  has no dimensions yet, so the arena is a placeholder circle.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/encounter-escape/vesper-escape-check.ps1
  powershell -ExecutionPolicy Bypass -File tools/encounter-escape/vesper-escape-check.ps1 -PlayerMargin 0.7
#>
param(
  [double]$ArenaRadius = 11.5,     # player-centre bound: placeholder, same as the Sylvara spec
  [double]$BossBody = 1.2,         # boss collision radius: the hovering bud's floor shadow
  [double]$BossRadiusMax = 3.0,    # packet: the boss drifts within 3 m of the arena centre
  [double]$PlayerMargin = 0.3,     # player body radius; touching a border counts as inside
  [double]$MarkRadius = 1.6, [double]$RingOuter = 3.2, [double]$MarkSpacing = 4.8,
  [double]$FanReach = 5.0, [double]$FanHalfDeg = 45.0, [double]$PetalHalfDeg = 35.0,
  [double]$ThreadWidth = 1.4, [double]$ThreadReach = 9.0,
  [double]$ClosureReach = 2.2,
  [double]$EvensongRadius = 1.6, [double]$EvensongRing = 5.0
)

$code = @'
using System;
using System.Collections.Generic;
public static class Escape {
    public static double A, Bmax, Body, M, MR, RO, MS, FR, FH, PH, TW, TR, CR, ER, ERing;
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
    static bool InDisk(double x, double y, double cx, double cy, double r) { return (x-cx)*(x-cx)+(y-cy)*(y-cy) <= (r+M)*(r+M); }
    // Annulus inner..outer around (cx,cy): the hollow centre is safe only where the player fully clears the inner edge.
    static bool InRing(double x, double y, double cx, double cy, double inner, double outer) {
        double d2 = (x-cx)*(x-cx)+(y-cy)*(y-cy);
        return d2 <= (outer+M)*(outer+M) && d2 >= Math.Max(0, inner-M)*Math.Max(0, inner-M);
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
    static string Fmt(double w, string at) { return string.Format("{0:F2}|{1}", w, at); }
    // Planting Mark disk, anywhere in the arena (the boss may be beside it).
    public static string Mark() {
        double w = 0; string at = "";
        for (double c = 0; c <= A; c += 0.5) {
            double cc = c;
            double d = Worst((x,y) => InDisk(x,y,cc,0,MR), 0, 0, false, cc, 0, MR+M+0.5);
            if (d < 0) return "UNSOLVABLE at mark r=" + c;
            if (d > w) { w = d; at = string.Format("mark centre r={0:F1} m", c); }
        }
        return Fmt(w, at);
    }
    // Bloom and Rest ring around one mark, with the boss body at every bound radius and the mark at any offset.
    public static string Ring() {
        double w = 0; string at = "";
        foreach (double b in BossRadii()) for (double r = 0; r <= A+b; r += 1.0) for (int k = 0; k <= 6; k++) {
            double a = Math.PI*k/6, cx = b+r*Math.Cos(a), cy = r*Math.Sin(a);
            if (cx*cx+cy*cy > A*A) continue;
            if ((cx-b)*(cx-b)+cy*cy < (Body+M)*(Body+M)) continue; // a mark never centres under the body
            double d = Worst((x,y) => InRing(x,y,cx,cy,MR,RO), b, 0, true, cx, cy, RO+M+0.5);
            if (d < 0) return string.Format("UNSOLVABLE at boss r={0} mark ({1:F1},{2:F1})", b, cx, cy);
            if (d > w) { w = d; at = string.Format("boss r={0:F1} m, mark {1:F1} m from the boss", b, r); }
        }
        return Fmt(w, at);
    }
    // Three rings at the minimum spacing the cancel rule allows (equilateral, side MS), anywhere they fit.
    public static string Ring3() {
        double w = 0; string at = ""; double R = MS/Math.Sqrt(3);
        for (double c = 0; c + R <= A; c += 1.0) for (int k = 0; k < 4; k++) {
            double rot = Math.PI*k/6; var px = new double[3]; var py = new double[3];
            for (int f = 0; f < 3; f++) { double a = rot+2*Math.PI*f/3; px[f] = c+R*Math.Cos(a); py[f] = R*Math.Sin(a); }
            Shape s = (x,y) => { for (int f = 0; f < 3; f++) if (InRing(x,y,px[f],py[f],MR,RO)) return true; return false; };
            double d = Worst(s, 0, 0, false, c, 0, R+RO+M+0.5);
            if (d < 0) return "UNSOLVABLE at triangle centre r=" + c;
            if (d > w) { w = d; at = string.Format("triangle centre r={0:F1} m, side {1:F1} m", c, MS); }
        }
        return Fmt(w, at);
    }
    // Cone from the boss centre; reach is measured from the body edge (packet contract).
    static string Cone(double halfDeg, double offsetDeg, int dirs) {
        double half = halfDeg*Math.PI/180, off = offsetDeg*Math.PI/180, R = Body+FR, w = 0; string at = "";
        foreach (double b in BossRadii()) for (int k = 0; k <= dirs; k++) {
            double dir = Math.PI*k/18 + off;
            double d = Worst((x,y) => SectorDist(x,y,b,0,dir,half,R) <= M, b, 0, true, b, 0, R+M+0.5);
            if (d < 0) return "UNSOLVABLE at boss r=" + b + " dir=" + (k*10);
            if (d > w) { w = d; at = string.Format("boss r={0:F1} m, facing {1} deg from the outward radial", b, k*10); }
        }
        return Fmt(w, at);
    }
    public static string Fan() { return Cone(FH, 0, 18); }
    // Petals II: adjacent cone sharing cone I's edge; cone I is spent (safe). Both sides sampled.
    public static string Petals() { return Cone(PH, FH+PH, 36); }
    public static string Thread() {
        double w = 0; string at = "";
        foreach (double b in BossRadii()) for (int k = 0; k <= 18; k++) {
            double dir = Math.PI*k/18, L = Body+TR;
            double d = Worst((x,y) => InLane(x,y,b,0,dir,L,TW/2), b, 0, true, b, 0, L+TW+0.5);
            if (d < 0) return "UNSOLVABLE at boss r=" + b + " dir=" + (k*10);
            if (d > w) { w = d; at = string.Format("boss r={0:F1} m, line {1} deg from the outward radial", b, k*10); }
        }
        return Fmt(w, at);
    }
    public static string Closure() {
        double w = 0; string at = "";
        foreach (double b in BossRadii()) {
            double bb = b;
            double d = Worst((x,y) => InDisk(x,y,bb,0,Body+CR), b, 0, true, b, 0, Body+CR+M+0.5);
            if (d < 0) return "UNSOLVABLE at boss r=" + b;
            if (d > w) { w = d; at = string.Format("boss r={0:F1} m, from body contact", b); }
        }
        return Fmt(w, at);
    }
    // Midnight Toll Evensong seeds: three disks 120 deg apart, ERing from the body edge; Closure is spent.
    public static string Evensong() {
        double w = 0; string at = ""; double R = Body+ERing;
        foreach (double b in BossRadii()) for (int k = 0; k < 12; k++) {
            double rot = Math.PI*k/6; var px = new double[3]; var py = new double[3]; bool fits = true;
            for (int f = 0; f < 3; f++) { double a = rot+2*Math.PI*f/3; px[f] = b+R*Math.Cos(a); py[f] = R*Math.Sin(a); if (Math.Sqrt(px[f]*px[f]+py[f]*py[f]) > A-ER) fits = false; }
            if (!fits) continue; // the director rotates the triangle until every seed sits inside the arena
            Shape s = (x,y) => { for (int f = 0; f < 3; f++) if (InDisk(x,y,px[f],py[f],ER)) return true; return false; };
            double d = Worst(s, b, 0, true, b, 0, R+ER+M+0.5);
            if (d < 0) return "UNSOLVABLE at boss r=" + b + " rot=" + (k*30);
            if (d > w) { w = d; at = string.Format("boss r={0:F1} m, triangle rotated {1} deg", b, k*30); }
        }
        return Fmt(w, at);
    }
}
'@
Add-Type -TypeDefinition $code -Language CSharp
[Escape]::A = $ArenaRadius; [Escape]::Bmax = $BossRadiusMax; [Escape]::Body = $BossBody; [Escape]::M = $PlayerMargin
[Escape]::MR = $MarkRadius; [Escape]::RO = $RingOuter; [Escape]::MS = $MarkSpacing
[Escape]::FR = $FanReach; [Escape]::FH = $FanHalfDeg; [Escape]::PH = $PetalHalfDeg
[Escape]::TW = $ThreadWidth; [Escape]::TR = $ThreadReach; [Escape]::CR = $ClosureReach
[Escape]::ER = $EvensongRadius; [Escape]::ERing = $EvensongRing

$results = [ordered]@{
  'Planting Mark (disk)'          = [Escape]::Mark()
  'Bloom and Rest (ring)'         = [Escape]::Ring()
  'Bloom and Rest (3 rings)'      = [Escape]::Ring3()
  'Petal Fan / Petals I'          = [Escape]::Fan()
  'Alternating Petals II'         = [Escape]::Petals()
  'Gathering Thread'              = [Escape]::Thread()
  'Vesper Closure'                = [Escape]::Closure()
  'Evensong seeds (Midnight Toll)'= [Escape]::Evensong()
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
