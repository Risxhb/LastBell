<#
.SYNOPSIS
  Worst-case escape check for the Bellkeeper's four attack shapes in a circular arena.

.DESCRIPTION
  Documentation support for docs/design/bellkeeper.md section 5. It is NOT the production validator
  (that is Unity/C# work after the shared contract, #6 U3). Pure geometry, no Unity dependency.

  For every sampled boss position (radius 0..BossRadiusMax, by rotational symmetry of the arena) and
  attack direction, it finds the reachable danger point whose straight-line distance to the nearest safe
  point is largest. Danger shapes are expanded by the player hit margin; player centres stay inside the
  arena and outside the boss body. Then it converts that distance into escape time for walk-only and for
  walk plus one dodge, and into the minimum lock-to-resolve window for each difficulty's reaction margin.

  Limits: straight-line travel (paths are not routed around the boss body), no other players, no
  acceleration, constant-speed dodge, grid sampling (~0.1 m danger grid, 0.05 m boundary grid).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/encounter-escape/escape-check.ps1
#>
param(
  [double]$ArenaRadius = 11.5,     # player-centre bound (Unity test VanguardTestController.ArenaRadius)
  [double]$BossRadiusMax = 9.5,    # boss-centre bound (Unity test BellkeeperTestController.ArenaRadius)
  [double]$BossBody = 1.4,         # boss collision radius
  [double]$PlayerMargin = 0.3,     # player body radius; touching a border counts as inside
  [double]$SweepRadius = 8.0, [double]$SweepHalfDeg = 40.0,
  [double]$TollInner = 2.6, [double]$TollOuter = 6.8,
  [double]$LaneWidth = 2.2,
  [double]$ChimeRadius = 1.4
)

$code = @'
using System;
using System.Collections.Generic;
public static class Escape {
    public static double A, Bmax, Body, M, SR, SH, TI, TO, LW, CR;
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
    public static string Sweep() {
        double half = SH*Math.PI/180, w = 0; string at = "";
        foreach (double b in BossRadii()) for (int k = 0; k <= 18; k++) {
            double dir = Math.PI*k/18; // 0..180 deg; mirror symmetry covers the rest
            double d = Worst((x,y) => SectorDist(x,y,b,0,dir,half,SR) <= M, b, 0, true, b, 0, SR+M+0.5);
            if (d < 0) return "UNSOLVABLE at boss r=" + b + " dir=" + (k*10);
            if (d > w) { w = d; at = string.Format("boss r={0:F1} m, facing {1} deg from the outward radial", b, k*10); }
        }
        return string.Format("{0:F2}|{1}", w, at);
    }
    public static string Toll() {
        double w = 0; string at = "";
        foreach (double b in BossRadii()) {
            double d = Worst((x,y) => { double r = Math.Sqrt((x-b)*(x-b)+y*y); return r >= TI-M && r <= TO+M; }, b, 0, true, b, 0, TO+M+0.5);
            if (d < 0) return "UNSOLVABLE at boss r=" + b;
            if (d > w) { w = d; at = string.Format("boss r={0:F1} m", b); }
        }
        return string.Format("{0:F2}|{1}", w, at);
    }
    public static string Lane() {
        // Lane = rectangle from the boss centre along dir, length L, half-width LW/2, expanded by M (as the Unity test checks it).
        double w = 0; string at = ""; double hw = LW/2 + M;
        foreach (double b in BossRadii()) for (int k = 0; k <= 18; k++) {
            double dir = Math.PI*k/18, ux = Math.Cos(dir), uy = Math.Sin(dir);
            // longest lane that keeps its end inside the boss bound
            double L = 0; while (L < 2*Bmax) { double ex = b+(L+0.1)*ux, ey = (L+0.1)*uy; if (ex*ex+ey*ey > Bmax*Bmax) break; L += 0.1; }
            if (L < 1) continue;
            double cx = b+ux*L/2, cy = uy*L/2;
            double d = Worst((x,y) => { double lx = (x-b)*ux+y*uy, ly = -(x-b)*uy+y*ux; return Math.Abs(ly) <= hw && lx >= -M && lx <= L+M; }, b, 0, false, cx, cy, L/2+hw+0.5);
            if (d < 0) return "UNSOLVABLE at boss r=" + b + " dir=" + (k*10);
            if (d > w) { w = d; at = string.Format("boss r={0:F1} m, lane {1:F1} m", b, L); }
        }
        return string.Format("{0:F2}|{1}", w, at);
    }
    public static string Chime() {
        double w = 0; string at = "";
        for (double c = 0; c <= A; c += 0.5) {
            double cc = c;
            double d = Worst((x,y) => (x-cc)*(x-cc)+y*y <= (CR+M)*(CR+M), 0, 0, false, cc, 0, CR+M+0.5);
            if (d < 0) return "UNSOLVABLE at marker r=" + c;
            if (d > w) { w = d; at = string.Format("marker centre r={0:F1} m", c); }
        }
        return string.Format("{0:F2}|{1}", w, at);
    }
}
'@
Add-Type -TypeDefinition $code -Language CSharp
[Escape]::A = $ArenaRadius; [Escape]::Bmax = $BossRadiusMax; [Escape]::Body = $BossBody; [Escape]::M = $PlayerMargin
[Escape]::SR = $SweepRadius; [Escape]::SH = $SweepHalfDeg; [Escape]::TI = $TollInner; [Escape]::TO = $TollOuter
[Escape]::LW = $LaneWidth; [Escape]::CR = $ChimeRadius

$results = [ordered]@{
  'Bell Sweep'       = [Escape]::Sweep()
  'Hollow Toll'      = [Escape]::Toll()
  'Pendulum Charge'  = [Escape]::Lane()
  'Shattered Chime'  = [Escape]::Chime()
}

# Movement sets: game-design defaults (#33) and the current Unity Vanguard test controller (#27).
$sets = @(
  @{ Name = 'design';     Walk = 6.0; Dodge = 3.0; DodgeTime = 0.20 },
  @{ Name = 'unity-test'; Walk = 4.2; Dodge = 4.5; DodgeTime = 0.40 }   # dodge duration assumed (clip-driven in Unity)
)
function WalkTime($d, $s) { $d / $s.Walk }
function DodgeTime($d, $s) { if ($d -le $s.Dodge) { $s.DodgeTime * $d / $s.Dodge } else { $s.DodgeTime + ($d - $s.Dodge) / $s.Walk } }

$tiers = @(
  @{ Name = 'First Chime';    Reaction = 0.60; Dodge = $false },
  @{ Name = 'Steady Ticking'; Reaction = 0.45; Dodge = $true },
  @{ Name = 'Striking Hour';  Reaction = 0.35; Dodge = $true },
  @{ Name = 'Midnight Toll';  Reaction = 0.30; Dodge = $true }
)

"## Worst-case escape distance (m)"
""
"| Attack | d_max | Worst configuration |"
"|---|---|---|"
foreach ($k in $results.Keys) { $p = $results[$k] -split '\|'; "| $k | $($p[0]) | $($p[1]) |" }
""
foreach ($s in $sets) {
  "## Escape time and minimum lock-to-resolve window, movement set '$($s.Name)' (walk $($s.Walk) m/s, dodge $($s.Dodge) m in $($s.DodgeTime) s)"
  ""
  "| Attack | Walk only (s) | Walk + 1 dodge (s) | " + (($tiers | ForEach-Object { "$($_.Name) min window (ticks)" }) -join ' | ') + " |"
  "|---|---|---|" + (($tiers | ForEach-Object { '---' }) -join '|') + "|"
  foreach ($k in $results.Keys) {
    $d = [double](($results[$k] -split '\|')[0])
    $w = WalkTime $d $s; $dd = DodgeTime $d $s
    $cells = foreach ($t in $tiers) { $need = $t.Reaction + $(if ($t.Dodge) { $dd } else { $w }); [math]::Ceiling([math]::Round($need * 60, 6)) }
    "| $k | {0:F2} | {1:F2} | {2} |" -f $w, $dd, ($cells -join ' | ')
  }
  ""
}
"Tick rate 60 Hz. First Chime is walk-only by design (no dodge required); the other tiers allow one dodge per attack."
