<#
.SYNOPSIS
  Worst-case escape check for Rivet Cantor's attack shapes in a circular placeholder arena.

.DESCRIPTION
  Documentation support for docs/design/rivet-cantor.md section 5. It is NOT the production validator
  (that is Unity/C# work on the shared contract, #6). Pure geometry, no Unity dependency. Same method
  as the Bellkeeper's and Sylvara's escape checks, with Rivet Cantor's shapes.

  The packet measures reach from the boss collision boundary, so every shape here starts at the body
  edge (BossBody from the centre). For every sampled boss position (radius 0..BossRadiusMax, by
  rotational symmetry of the arena) and attack direction, it finds the reachable danger point whose
  straight-line distance to the nearest safe point is largest. Danger shapes are expanded by the player
  hit margin; player centres stay inside the arena and outside the boss body. That distance becomes
  escape time for walk-only and for walk plus one dash, and the minimum window for each difficulty.

  Alternating Rivets is checked per beat: beat I's cone is spent when beat II resolves, so the danger is
  one 75-degree cone at a time. Split Note's lanes are checked on their own (the disk is spent).
  It also reports the narrowest player-centre corridor between the Split Note lanes.

  Edge rule (default on): Rivet Beat, Alternating Rivets and Rest Stroke are only legal when a player-wide
  corridor stays open between the shape's far edge and the wall; the director rotates the facing or walks
  the boss inward first. -NoEdgeRule shows the packet geometry without it.

  Limits: straight-line travel (no routing around the boss body), no other players, no acceleration,
  constant-speed dash, grid sampling (~0.1 m danger grid, 0.05 m boundary grid). Brass Foundry has no
  arena dimensions yet, so the arena is a placeholder circle.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/encounter-escape/rivet-cantor-escape-check.ps1
#>
param(
  [double]$ArenaRadius = 11.5,     # player-centre bound: placeholder, same as Sylvara and the key-art test arena
  [double]$BossBody = 1.6,         # boss collision radius: 3.2 m broad torso
  [double]$PlayerMargin = 0.3,     # player body radius; touching a border counts as inside
  [double]$BeatReach = 3.5, [double]$BeatHalfDeg = 45.0,
  [double]$RivetsReach = 3.5, [double]$RivetsHalfDeg = 37.5,
  [double]$NoteRadius = 2.0, [double]$NoteAhead = 2.0,
  [double]$ForkWidth = 1.4, [double]$ForkLength = 10.0,
  [double]$SplitWidth = 1.2, [double]$SplitLength = 6.0, [double]$SplitGap = 2.6,
  [double]$RestReach = 2.0,
  [switch]$NoEdgeRule              # report the packet geometry without the edge rule (spec section 5.4)
)

# Keep a player-wide gap between the boss body and the wall: boss centre + body + one player diameter <= wall.
$BossRadiusMax = [math]::Floor(($ArenaRadius + $PlayerMargin - $BossBody - 2 * $PlayerMargin) * 2) / 2

$code = @'
using System;
using System.Collections.Generic;
public static class Escape {
    public static double A, Bmax, Body, M, BtR, BtH, RvR, RvH, NR, NA, FW, FL, SW, SL, SG, RR;
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
    // rectangle starting at (ox,oy) offset sideways by off, along dir, length L, half-width hw, expanded by M
    static bool InLane(double x, double y, double ox, double oy, double dir, double off, double L, double hw) {
        double ux = Math.Cos(dir), uy = Math.Sin(dir);
        double lx = (x-ox)*ux+(y-oy)*uy, ly = -(x-ox)*uy+(y-oy)*ux - off;
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
    // Generic sweep over boss positions and facings (0..180 deg; mirror symmetry covers the rest).
    // Edge rule: a cast is legal only if a player-wide corridor stays open between its far edge and the wall.
    public static bool EdgeRule = true;
    static bool FarEdgeOk(double far) { return !EdgeRule || far + 3*M <= A; }
    static string Sweep(Func<double,double,Shape> make, double reach, string what, Func<double,double,double> far) {
        double w = 0; string at = "";
        foreach (double b in BossRadii()) for (int k = 0; k <= 18; k++) {
            double dir = Math.PI*k/18;
            if (far != null && !FarEdgeOk(far(b, dir))) continue; // director rotates or walks in first
            double d = Worst(make(b, dir), b, 0, b, 0, reach);
            if (d < 0) return "UNSOLVABLE at boss r=" + b + " dir=" + (k*10);
            if (d > w) { w = d; at = string.Format("boss r={0:F1} m, {1} {2} deg from the outward radial", b, what, k*10); }
        }
        return string.Format("{0:F2}|{1}", w, at);
    }
    public static string Cone(double reach, double halfDeg) {
        double half = halfDeg*Math.PI/180, r = Body+reach;
        // farthest point of the outer arc from the arena centre
        Func<double,double,double> far = (b,dir) => { double m = 0;
            for (int i = 0; i <= 20; i++) { double t = dir-half+2*half*i/20, x = b+r*Math.Cos(t), y = r*Math.Sin(t); m = Math.Max(m, Math.Sqrt(x*x+y*y)); }
            return m; };
        return Sweep((b,dir) => (x,y) => SectorDist(x,y,b,0,dir,half,r) <= M, r+M+0.5, "facing", far);
    }
    // Held Note / Split Note disk: circle centred NA m ahead of the body edge.
    public static string Note() {
        double c = Body+NA;
        return Sweep((b,dir) => { double cx = b+c*Math.Cos(dir), cy = c*Math.Sin(dir);
            return (x,y) => (x-cx)*(x-cx)+(y-cy)*(y-cy) <= (NR+M)*(NR+M); }, c+NR+M+0.5, "facing", null);
    }
    // Tuning Fork: one lane from the body edge, clipped by the arena (points outside are never reachable).
    public static string Fork() {
        return Sweep((b,dir) => { double ox = b+Body*Math.Cos(dir), oy = Body*Math.Sin(dir);
            return (x,y) => InLane(x,y,ox,oy,dir,0,FL,FW/2); }, Body+FL+FW, "lane", null);
    }
    // Split Note lanes: two parallel lanes from the body edge, centre gap SG between their inner edges.
    public static string Split() {
        double off = SG/2+SW/2;
        return Sweep((b,dir) => { double ox = b+Body*Math.Cos(dir), oy = Body*Math.Sin(dir);
            return (x,y) => InLane(x,y,ox,oy,dir,off,SL,SW/2) || InLane(x,y,ox,oy,dir,-off,SL,SW/2); }, Body+SL+SG+SW, "lanes", null);
    }
    public static string Rest() {
        double r = Body+RR;
        return Sweep((b,dir) => (x,y) => (x-b)*(x-b)+y*y <= (r+M)*(r+M), r+M+0.5, "facing", (b,dir) => b+r);
    }
}
'@
Add-Type -TypeDefinition $code -Language CSharp
[Escape]::A = $ArenaRadius; [Escape]::Bmax = $BossRadiusMax; [Escape]::Body = $BossBody; [Escape]::M = $PlayerMargin
[Escape]::NR = $NoteRadius; [Escape]::NA = $NoteAhead
[Escape]::FW = $ForkWidth; [Escape]::FL = $ForkLength
[Escape]::SW = $SplitWidth; [Escape]::SL = $SplitLength; [Escape]::SG = $SplitGap
[Escape]::RR = $RestReach
[Escape]::EdgeRule = -not $NoEdgeRule

$results = [ordered]@{
  'Rivet Beat (90 deg)'         = [Escape]::Cone($BeatReach, $BeatHalfDeg)
  'Held Note / Split Note disk' = [Escape]::Note()
  'Tuning Fork'                 = [Escape]::Fork()
  'Alternating Rivets, per beat'= [Escape]::Cone($RivetsReach, $RivetsHalfDeg)
  'Split Note lanes'            = [Escape]::Split()
  'Rest Stroke'                 = [Escape]::Rest()
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
"Edge rule (cone arcs and the Rest Stroke pulse keep a player-wide corridor to the wall): $(-not $NoEdgeRule)."
"Split Note centre corridor for player centres: {0:F2} m (gap $SplitGap m minus the margin on both sides)." -f ($SplitGap - 2 * $PlayerMargin)
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
