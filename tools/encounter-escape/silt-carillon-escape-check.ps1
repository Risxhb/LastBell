<#
.SYNOPSIS
  Worst-case escape check for the Silt Carillon's attack shapes in a circular placeholder arena.

.DESCRIPTION
  Documentation support for docs/design/silt-carillon.md section 5. It is NOT the production validator
  (that is Unity/C# work on the shared contract, #6). Pure geometry, no Unity dependency. Same method
  as the Bellkeeper's escape-check.ps1 and Sylvara's sylvara-escape-check.ps1, with the Silt Carillon's shapes.

  For every sampled boss position (radius 0..BossRadiusMax, by rotational symmetry of the arena) and
  facing, it finds the reachable danger point whose straight-line distance to the nearest safe point is
  largest. Danger shapes are expanded by the player hit margin; player centres stay inside the arena and
  outside the boss body. That distance becomes escape time for walk-only and for walk plus one dash, and
  the minimum window for each difficulty's reaction margin.

  Silt Steps / Broken Steps are checked as a chain: at lane k's resolve, every lane not yet spent counts as
  danger (conservative: the player may not shelter in a lane that is still warning) and spent lanes are safe.
  Basin Toll's Midnight Toll second beat (Ebb) is checked with the spent annulus counted as safe.

  Lane frame: a lane starts at the boss's front tangent (boss centre + BossBody along the facing) and runs
  forward. Lane I is on the boss's left, II in the centre, III on its right.

  Limits: straight-line travel (no routing around the boss body), no other players, no acceleration,
  constant-speed dash, grid sampling (~0.1 m danger grid, 0.05 m boundary grid). Verdant Chimes has no
  arena dimensions yet, so the arena is a placeholder circle.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/encounter-escape/silt-carillon-escape-check.ps1
#>
param(
  [double]$ArenaRadius = 11.5,     # player-centre bound: placeholder (packet proposes a 24 m floor), same as Sylvara
  [double]$BossBody = 1.8,         # boss collision radius: four planted brass-root legs (D1)
  [double]$PlayerMargin = 0.3,     # player body radius; touching a border counts as inside
  [double]$NoteWidth = 2.0, [double]$NoteLength = 9.0,
  [double]$SplitWidth = 1.5, [double]$SplitGap = 2.6,
  [double]$StepWidth = 1.5, [double]$StepLength = 7.0, [double]$StepPitch = 2.0,
  [double]$BackRange = 3.5, [double]$BackDeg = 110.0,
  [double]$TollInner = 2.5, [double]$TollOuter = 5.0
)

# Keep a player-wide gap between the boss body and the wall: boss centre + body + one player diameter <= wall.
$BossRadiusMax = [math]::Floor(($ArenaRadius + $PlayerMargin - $BossBody - 2 * $PlayerMargin) * 2) / 2

$code = @'
using System;
using System.Collections.Generic;
public static class Escape {
    public static double A, Bmax, Body, M, NW, NL, SW, SG, TW, TL, TP, BR, BD, TI, TO;
    // rectangle in the boss frame: forward from the front tangent, lateral offset off (left positive), expanded by M
    static bool InLane(double x, double y, double bx, double dir, double off, double L, double hw) {
        double ux = Math.Cos(dir), uy = Math.Sin(dir);
        double lx = (x-bx)*ux+y*uy - Body, ly = -(x-bx)*uy+y*ux - off;
        return Math.Abs(ly) <= hw+M && lx >= -M && lx <= L+M;
    }
    static double Seg(double px, double py, double ax, double ay, double bx, double by) {
        double vx = bx-ax, vy = by-ay, wx = px-ax, wy = py-ay;
        double t = Math.Max(0, Math.Min(1, (wx*vx+wy*vy)/(vx*vx+vy*vy)));
        double dx = px-(ax+t*vx), dy = py-(ay+t*vy); return Math.Sqrt(dx*dx+dy*dy);
    }
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
    public static double Worst(Shape danger, double bx, double cx, double cy, double reach) {
        double step = 0.05, coarse = 0.1;
        double x0 = Math.Max(-A, cx-reach), x1 = Math.Min(A, cx+reach), y0 = Math.Max(-A, cy-reach), y1 = Math.Min(A, cy+reach);
        Func<double,double,bool> ok = (x,y) => x*x+y*y <= A*A && (x-bx)*(x-bx)+y*y >= (Body+M)*(Body+M);
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
    // Facings 0..180 deg from the outward radial; the left/right-symmetric shapes need no more.
    static string Sweep(Func<double,double,Shape> make, Func<double,double,double[]> centre, double reach, int maxDeg) {
        double w = 0; string at = "";
        foreach (double b in BossRadii()) for (int k = 0; k*10 <= maxDeg; k++) {
            double dir = Math.PI*k/18; var c = centre(b, dir);
            double d = Worst(make(b, dir), b, c[0], c[1], reach);
            if (d < 0) return "UNSOLVABLE at boss r=" + b + " dir=" + (k*10);
            if (d > w) { w = d; at = string.Format("boss r={0:F1} m, facing {1} deg from the outward radial", b, k*10); }
        }
        return string.Format("{0:F2}|{1}", w, at);
    }
    static double[] Ahead(double b, double dir, double dist) { return new[]{ b+dist*Math.Cos(dir), dist*Math.Sin(dir) }; }
    public static string Note() {
        return Sweep((b,dir) => (x,y) => InLane(x,y,b,dir,0,NL,NW/2), (b,dir) => Ahead(b,dir,Body+NL/2), NL/2+NW+0.5, 180);
    }
    public static string Split() {
        double off = SG/2+SW/2;
        return Sweep((b,dir) => (x,y) => InLane(x,y,b,dir,off,NL,SW/2) || InLane(x,y,b,dir,-off,NL,SW/2),
                     (b,dir) => Ahead(b,dir,Body+NL/2), NL/2+SG+2*SW+0.5, 180);
    }
    // Rear cone: apex at the boss centre, pointing away from the facing, radius = body + BackRange.
    public static string Back() {
        double half = BD/2*Math.PI/180, r = Body+BR;
        return Sweep((b,dir) => (x,y) => SectorDist(x,y,b,0,dir+Math.PI,half,r) <= M, (b,dir) => new[]{ b, 0.0 }, r+M+0.5, 180);
    }
    // Annulus measured from the collision boundary: centre radii Body+TI .. Body+TO.
    public static string Toll() {
        double ri = Body+TI, ro = Body+TO;
        return Sweep((b,dir) => (x,y) => { double d = Math.Sqrt((x-b)*(x-b)+y*y); return d >= ri-M && d <= ro+M; },
                     (b,dir) => new[]{ b, 0.0 }, ro+M+0.5, 0);
    }
    // Midnight Toll Ebb: the inner refuge (boundary .. Body+TI) tolls after the annulus is spent.
    public static string Ebb() {
        double ri = Body+TI;
        return Sweep((b,dir) => (x,y) => (x-b)*(x-b)+y*y <= (ri+M)*(ri+M), (b,dir) => new[]{ b, 0.0 }, ri+M+0.5, 0);
    }
    // Steps chain. order = lane numbers in resolve order (1 = left). Returns the worst for each step position.
    public static string[] Steps(int[] order) {
        var res = new string[order.Length];
        for (int s = 0; s < order.Length; s++) {
            var pending = new List<double>();
            for (int k = s; k < order.Length; k++) pending.Add((2-order[k])*TP); // lane I at +pitch (left), III at -pitch
            var p = pending.ToArray();
            double w = 0; string at = "";
            foreach (double b in BossRadii()) for (int k = 0; k < 36; k++) { // full circle: the order is not mirror-symmetric
                double dir = Math.PI*k/18; var c = Ahead(b, dir, Body+TL/2);
                Shape sh = (x,y) => { foreach (var off in p) if (InLane(x,y,b,dir,off,TL,TW/2)) return true; return false; };
                double d = Worst(sh, b, c[0], c[1], TL/2+2*TP+TW+0.5);
                if (d < 0) { at = "UNSOLVABLE at boss r=" + b + " dir=" + (k*10); w = -1; break; }
                if (d > w) { w = d; at = string.Format("boss r={0:F1} m, facing {1} deg from the outward radial", b, k*10); }
            }
            res[s] = string.Format("{0:F2}|{1}", w, at);
        }
        return res;
    }
}
'@
Add-Type -TypeDefinition $code -Language CSharp
[Escape]::A = $ArenaRadius; [Escape]::Bmax = $BossRadiusMax; [Escape]::Body = $BossBody; [Escape]::M = $PlayerMargin
[Escape]::NW = $NoteWidth; [Escape]::NL = $NoteLength; [Escape]::SW = $SplitWidth; [Escape]::SG = $SplitGap
[Escape]::TW = $StepWidth; [Escape]::TL = $StepLength; [Escape]::TP = $StepPitch
[Escape]::BR = $BackRange; [Escape]::BD = $BackDeg; [Escape]::TI = $TollInner; [Escape]::TO = $TollOuter

$p1 = [Escape]::Steps(@(1, 2, 3)); $p2 = [Escape]::Steps(@(1, 3, 2))
$results = [ordered]@{
  'Spillway Note'                 = [Escape]::Note()
  'Split Spillway'                = [Escape]::Split()
  'Silt Steps, lane I (all pending)'  = $p1[0]
  'Silt Steps, lane II (II+III)'      = $p1[1]
  'Silt Steps, lane III'              = $p1[2]
  'Broken Steps, lane I (all pending)'= $p2[0]
  'Broken Steps, lane III (III+II)'   = $p2[1]
  'Broken Steps, lane II'             = $p2[2]
  'Bellback'                      = [Escape]::Back()
  'Basin Toll'                    = [Escape]::Toll()
  'Ebb (Midnight Toll)'           = [Escape]::Ebb()
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
