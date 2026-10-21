<#
.SYNOPSIS
  Worst-case escape and self-strike geometry check for the Escapement Twins mini-boss.

.DESCRIPTION
  Documentation support for docs/design/escapement-twins.md section 5. It is NOT the production validator
  (that is Unity/C# work on the shared contract, #6). Pure geometry, no Unity dependency. Same method as the
  Bellkeeper and Sylvara escape checks, with two differences: the boss never leaves the arena centre (it only
  rotates), and its body is a stadium (a rounded rectangle: segment plus radius), not a circle.

  Boss frame: centre at the origin, facing +x. The long axis is y. Sun tower centre (0, +TowerY), Moon tower
  centre (0, -TowerY). Every attack is sampled over all directions in 10 degree steps (the boss can face any way).

  For each attack it finds the reachable danger point farthest from safety (danger shapes expanded by the
  player margin; player centres inside the arena and outside the body) and turns that into escape times and
  the minimum lock-to-resolve window per difficulty.

  It also measures the self-strike bait: the lane directions from a sending tower whose centreline passes
  within the pawl's hit radius of the opposite pawl, and where a targeted player has to stand for that.

  Limits: straight-line travel (no routing around the body), no other players, no acceleration,
  constant-speed dash, grid sampling (~0.1 m danger grid, 0.05 m boundary grid). Placeholder arena.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/encounter-escape/escapement-twins-escape-check.ps1
#>
param(
  [double]$ArenaRadius = 11.5,     # player-centre bound: placeholder, same as the key-art test arena
  [double]$BodyHalfSeg = 1.5,      # stadium: segment from (0,-1.5) to (0,+1.5) ...
  [double]$BodyRadius = 1.1,       # ... plus 1.1 m: 5.2 m long, 2.2 m deep
  [double]$PlayerMargin = 0.3,     # player body radius; touching a border counts as inside
  [double]$TowerY = 1.6,           # tower centres on the long axis
  [double]$VerdictWidth = 1.5, [double]$VerdictReach = 8.0,          # reach past where the lane leaves the body
  [double]$RebuttalHalfDeg = 50.0, [double]$PairHalfDeg = 40.0, [double]$ConeRadius = 5.1,  # from tower centre
  [double]$RingInner = 2.5, [double]$RingOuter = 5.5,                # from the body boundary
  [double]$DeadbeatInner = 1.3, [double]$DeadbeatWidth = 1.5, [double]$DeadbeatReach = 8.0,
  [double]$P1PawlX = 1.3, [double]$P1PawlY = 0.7, [double]$P1PawlR = 0.5,
  [double]$P2PawlX = 1.5, [double]$P2PawlY = 0.9, [double]$P2PawlR = 0.6
)

$code = @'
using System;
using System.Collections.Generic;
public static class Twins {
    public static double A, HS, BR, M, TY, VW, VR, RH, PH, CR, RI, RO, DI, DW, DR;
    static double Seg(double px, double py, double ax, double ay, double bx, double by) {
        double vx = bx-ax, vy = by-ay, wx = px-ax, wy = py-ay;
        double t = Math.Max(0, Math.Min(1, (wx*vx+wy*vy)/(vx*vx+vy*vy)));
        double dx = px-(ax+t*vx), dy = py-(ay+t*vy); return Math.Sqrt(dx*dx+dy*dy);
    }
    // signed-ish distance from a point to the body boundary (negative inside)
    public static double BodyDist(double x, double y) {
        double cy = Math.Max(-HS, Math.Min(HS, y));
        return Math.Sqrt(x*x+(y-cy)*(y-cy)) - BR;
    }
    static double SectorDist(double px, double py, double ox, double oy, double dir, double half, double r) {
        double dx = px-ox, dy = py-oy, d = Math.Sqrt(dx*dx+dy*dy);
        double a = Math.Atan2(dy,dx)-dir; while (a > Math.PI) a -= 2*Math.PI; while (a < -Math.PI) a += 2*Math.PI;
        if (Math.Abs(a) <= half) return Math.Max(0, d-r);
        double e1 = Seg(px,py,ox,oy,ox+r*Math.Cos(dir+half),oy+r*Math.Sin(dir+half));
        double e2 = Seg(px,py,ox,oy,ox+r*Math.Cos(dir-half),oy+r*Math.Sin(dir-half));
        return Math.Min(e1,e2);
    }
    static bool InLane(double x, double y, double ox, double oy, double dir, double L, double hw) {
        double ux = Math.Cos(dir), uy = Math.Sin(dir);
        double lx = (x-ox)*ux+(y-oy)*uy, ly = -(x-ox)*uy+(y-oy)*ux;
        return Math.Abs(ly) <= hw+M && lx >= -M && lx <= L+M;
    }
    // distance along dir from (ox,oy) until the centreline leaves the body for good
    static double ExitDist(double ox, double oy, double dir) {
        double last = 0;
        for (double t = 0; t < 8; t += 0.02) if (BodyDist(ox+t*Math.Cos(dir), oy+t*Math.Sin(dir)) <= 0) last = t;
        return last;
    }
    public delegate bool Shape(double x, double y);
    static bool Ok(double x, double y) { return x*x+y*y <= A*A && BodyDist(x,y) >= M; }
    public static double Worst(Shape danger) {
        double step = 0.05, coarse = 0.1, x0 = -A, y0 = -A;
        int n = (int)Math.Ceiling(2*A/step);
        var grid = new bool[n+1, n+1];
        for (int i = 0; i <= n; i++) for (int j = 0; j <= n; j++) grid[i,j] = danger(x0+i*step, y0+j*step);
        var safe = new List<double[]>();
        for (int i = 0; i <= n; i++) for (int j = 0; j <= n; j++) {
            if (grid[i,j]) continue; double x = x0+i*step, y = y0+j*step; if (!Ok(x,y)) continue;
            bool edge = (i>0&&grid[i-1,j])||(i<n&&grid[i+1,j])||(j>0&&grid[i,j-1])||(j<n&&grid[i,j+1]);
            if (edge) safe.Add(new[]{x,y});
        }
        double worst = 0;
        for (double x = x0; x <= A; x += coarse) for (double y = y0; y <= A; y += coarse) {
            if (!Ok(x,y) || !danger(x,y)) continue;
            double best = double.MaxValue;
            foreach (var s in safe) { double d = (s[0]-x)*(s[0]-x)+(s[1]-y)*(s[1]-y); if (d < best) best = d; }
            if (best == double.MaxValue) return -1;
            worst = Math.Max(worst, Math.Sqrt(best));
        }
        return worst;
    }
    static string Sweep(string what, Func<double, Shape> make) {
        double w = 0; string at = "";
        for (int k = 0; k < 36; k++) {
            double dir = Math.PI*k/18; double d = Worst(make(dir));
            if (d < 0) return "-1|UNSOLVABLE at " + (k*10) + " deg";
            if (d > w) { w = d; at = string.Format("{0} {1} deg from the boss's facing", what, k*10); }
        }
        return string.Format("{0:F2}|{1}", w, at);
    }
    // Sun Verdict: lane from the Sun tower centre, reaching VR past the point where it leaves the body.
    public static string Verdict() {
        return Sweep("lane", dir => { double L = ExitDist(0, TY, dir) + VR; return (x,y) => InLane(x,y,0,TY,dir,L,VW/2); });
    }
    public static string Rebuttal(double halfDeg) {
        double half = halfDeg*Math.PI/180;
        return Sweep("cone", dir => (x,y) => SectorDist(x,y,0,-TY,dir,half,CR) <= M);
    }
    public static string Ring() {
        double d = Worst((x,y) => { double b = BodyDist(x,y); return b >= RI-M && b <= RO+M; });
        return string.Format("{0:F2}|stadium ring, any facing", d);
    }
    public static string Deadbeat() {
        Shape s = (x,y) => { double dx = x-BR; if (dx < -M || dx > DR+M) return false; double ay = Math.Abs(y); return ay >= DI-M && ay <= DI+DW+M; };
        double d = Worst(s);
        // corridor between the lanes for player centres, at the body's front edge
        double corridor = 2*(DI-M);
        return string.Format("{0:F2}|corridor for player centres {1:F2} m", d, corridor);
    }
    public static string InnerBand() {
        double d = Worst((x,y) => BodyDist(x,y) <= RI+M);
        return string.Format("{0:F2}|from the body edge outward into the spent ring", d);
    }
    // Self-strike bait: directions from the sender (0, TY) whose centreline passes within pr of the opposite pawl (px, -py).
    public static string Bait(double px, double py, double pr) {
        double lo = double.MaxValue, hi = double.MinValue; int hits = 0;
        for (double deg = -180; deg < 180; deg += 0.1) {
            double dir = deg*Math.PI/180, ux = Math.Cos(dir), uy = Math.Sin(dir);
            double wx = px, wy = -py-TY, along = wx*ux+wy*uy, perp = Math.Abs(-wx*uy+wy*ux);
            if (along > 0 && perp <= pr) { hits++; lo = Math.Min(lo, deg); hi = Math.Max(hi, deg); }
        }
        // where a target can stand: player centres in the arena, outside the body, whose bearing from the sender is in the wedge
        double area = 0, nearest = double.MaxValue, cell = 0.05;
        for (double x = -A; x <= A; x += cell) for (double y = -A; y <= A; y += cell) {
            if (!Ok(x,y)) continue;
            double deg = Math.Atan2(y-TY, x)*180/Math.PI;
            if (deg < lo || deg > hi) continue;
            if ((x-px)*Math.Cos(deg*Math.PI/180)+(y+py)*Math.Sin(deg*Math.PI/180) < 0) continue; // beyond the pawl
            area += cell*cell; nearest = Math.Min(nearest, BodyDist(x,y));
        }
        double mid = (lo+hi)/2;
        return string.Format("wedge {0:F1} deg wide, bearing {1:F1} to {2:F1} deg (centre {3:F1}) from the sender; bait area {4:F1} m2; nearest bait spot {5:F2} m from the body edge",
            hi-lo, lo, hi, mid, area, nearest);
    }
}
'@
Add-Type -TypeDefinition $code -Language CSharp
[Twins]::A = $ArenaRadius; [Twins]::HS = $BodyHalfSeg; [Twins]::BR = $BodyRadius; [Twins]::M = $PlayerMargin
[Twins]::TY = $TowerY; [Twins]::VW = $VerdictWidth; [Twins]::VR = $VerdictReach
[Twins]::RH = $RebuttalHalfDeg; [Twins]::PH = $PairHalfDeg; [Twins]::CR = $ConeRadius
[Twins]::RI = $RingInner; [Twins]::RO = $RingOuter
[Twins]::DI = $DeadbeatInner; [Twins]::DW = $DeadbeatWidth; [Twins]::DR = $DeadbeatReach

# Window after lock, in seconds, at Steady Ticking (from the spec's attack table).
$results = [ordered]@{
  'Sun Verdict / Alternating Verdict' = @{ R = [Twins]::Verdict();                  W = 1.00 }
  'Moon Rebuttal (100 deg)'           = @{ R = [Twins]::Rebuttal($RebuttalHalfDeg); W = 1.60 }
  'Rebuttal Pair, second cone (80 deg)' = @{ R = [Twins]::Rebuttal($PairHalfDeg);   W = 1.50 }
  'Common Measure'                    = @{ R = [Twins]::Ring();                     W = 2.00 }
  'Deadbeat'                          = @{ R = [Twins]::Deadbeat();                 W = 2.00 }
  'Counter-Measure (Midnight Toll)'   = @{ R = [Twins]::InnerBand();                W = 1.50 }
}

$sets = @(
  @{ Name = 'design';    Walk = 6.0; Dash = 3.0; DashTime = 0.20 },
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

"Arena player bound $ArenaRadius m. Body stadium $([double]2*($BodyHalfSeg+$BodyRadius)) m long x $([double]2*$BodyRadius) m deep at the centre. Player margin $PlayerMargin m."
""
"## Worst-case escape distance (m)"
""
"| Attack | d_max | Worst configuration |"
"|---|---|---|"
foreach ($k in $results.Keys) { $p = $results[$k].R -split '\|'; "| $k | $($p[0]) | $($p[1]) |" }
""
foreach ($s in $sets) {
  "## Escape time and minimum window (ticks), movement set '$($s.Name)' (walk $($s.Walk) m/s, dash $($s.Dash) m in $($s.DashTime) s)"
  ""
  "| Attack | Walk only (s) | Walk + 1 dash (s) | " + (($tiers | ForEach-Object { $_.Name }) -join ' | ') + " | Spec window |"
  "|---|---|---|" + (($tiers | ForEach-Object { '---' }) -join '|') + "|---|"
  foreach ($k in $results.Keys) {
    $d = [double](($results[$k].R -split '\|')[0])
    $w = WalkTime $d $s; $dd = DashTime $d $s
    $cells = foreach ($t in $tiers) { $need = $t.Reaction + $(if ($t.Dash) { $dd } else { $w }); [math]::Ceiling([math]::Round($need * 60, 6)) }
    "| $k | {0:F2} | {1:F2} | {2} | {3} |" -f $w, $dd, ($cells -join ' | '), [math]::Round($results[$k].W * 60)
  }
  ""
}
"## Self-strike bait (Sun sender, Moon pawl; mirrored for Moon sender in Phase 2)"
""
"- Phase 1 pawl ($P1PawlX, -$P1PawlY), hit radius $P1PawlR m: " + [Twins]::Bait($P1PawlX, $P1PawlY, $P1PawlR)
"- Phase 2 pawl ($P2PawlX, -$P2PawlY), hit radius $P2PawlR m: " + [Twins]::Bait($P2PawlX, $P2PawlY, $P2PawlR)
""
"Bearings: 0 = the boss's facing, negative = toward the Moon side. Tick rate 60 Hz. First Chime is walk-only by design."
