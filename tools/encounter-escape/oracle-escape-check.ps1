<#
.SYNOPSIS
  Worst-case escape check for the Chimebound Oracle's attack shapes in the key-art arena.

.DESCRIPTION
  Documentation support for docs/design/chimebound-oracle.md section 5. Same method as the Bellkeeper's
  escape-check.ps1 (pure geometry, no Unity), plus two things the Oracle needs:

  - Paths route around the boss body. The straight-line check is too optimistic for Silent Fifth, where the
    nearest safe sector is often on the far side of the sphere. Distance is the shortest path that stays outside
    the body circle (radius body + player margin): straight if clear, otherwise tangent + arc + tangent.
  - Sector attacks. Silent Fifth and Passing Silence are ONE cone of 288 degrees (four 72-degree sectors) with a
    72-degree safe gap, so a player on a sector border is hit once, not twice.

  For each boss position and orientation it finds the reachable danger point farthest from safety, then turns
  that distance into the time needed (reaction + walk) and compares it with the attack's window from final lock
  to resolve. No dodge, sprint or jump is assumed: the packet says no pattern may need stamina.

  Limits: no other players, no acceleration, grid sampling (0.2 m danger grid, 0.1 m boundary grid; cone rows use
  0.1 m / 0.05 m), arena treated as a circle of player-centre radius ArenaRadius.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/encounter-escape/oracle-escape-check.ps1
#>
param(
  [double]$ArenaRadius = 11.5,     # player-centre bound (Unity CombatWorld)
  [double]$DriftMax = 3.0,         # boss-centre bound (spec: 3 m leash; the Unity stand-in drifts to 8.5)
  [double]$Leash = 3.0,            # sector casts only start within this distance of the arena centre
  [double]$BossBody = 1.2,         # Oracle collision radius (Unity ChimeboundOracle.BodyRadius)
  [double]$PlayerMargin = 0.3,     # player body radius; touching a border counts as inside
  [double]$ChimeRadius = 1.4,
  [double]$ConeRadius = 6.5, [double]$ConeHalfDeg = 35.0,
  [double]$SectorRadius = 6.5,
  [double]$NeedleWidth = 1.5
)

$code = @'
using System;
using System.Collections.Generic;
public static class Oracle {
    public static double A, Dmax, Leash, Body, M, CR, KR, KH, SR, NW;
    static double Norm(double a) { while (a > Math.PI) a -= 2*Math.PI; while (a < -Math.PI) a += 2*Math.PI; return a; }
    static double Seg(double px, double py, double ax, double ay, double bx, double by) {
        double vx = bx-ax, vy = by-ay, wx = px-ax, wy = py-ay;
        double t = Math.Max(0, Math.Min(1, (wx*vx+wy*vy)/(vx*vx+vy*vy)));
        double dx = px-(ax+t*vx), dy = py-(ay+t*vy); return Math.Sqrt(dx*dx+dy*dy);
    }
    // distance from a point to an (unexpanded) sector with apex o, direction dir, half-angle half, radius r
    static double SectorDist(double px, double py, double ox, double oy, double dir, double half, double r) {
        double dx = px-ox, dy = py-oy, d = Math.Sqrt(dx*dx+dy*dy);
        if (Math.Abs(Norm(Math.Atan2(dy,dx)-dir)) <= half) return Math.Max(0, d-r);
        double e1 = Seg(px,py,ox,oy,ox+r*Math.Cos(dir+half),oy+r*Math.Sin(dir+half));
        double e2 = Seg(px,py,ox,oy,ox+r*Math.Cos(dir-half),oy+r*Math.Sin(dir-half));
        return Math.Min(e1,e2);
    }
    // shortest path from p to q that stays outside the disc (bx,by,R)
    static double Path(double px, double py, double qx, double qy, double bx, double by, double R) {
        double sx = qx-px, sy = qy-py, L2 = sx*sx+sy*sy;
        if (R <= 0 || L2 < 1e-12) return Math.Sqrt(L2);
        double t = Math.Max(0, Math.Min(1, ((bx-px)*sx+(by-py)*sy)/L2));
        double cx = px+t*sx-bx, cy = py+t*sy-by;
        if (cx*cx+cy*cy >= R*R) return Math.Sqrt(L2);
        double dp = Math.Max(R, Math.Sqrt((px-bx)*(px-bx)+(py-by)*(py-by))), dq = Math.Max(R, Math.Sqrt((qx-bx)*(qx-bx)+(qy-by)*(qy-by)));
        double th = Math.Abs(Norm(Math.Atan2(py-by,px-bx)-Math.Atan2(qy-by,qx-bx)));
        double arc = Math.Max(0, th - Math.Acos(R/dp) - Math.Acos(R/dq));
        return Math.Sqrt(dp*dp-R*R) + Math.Sqrt(dq*dq-R*R) + R*arc;
    }
    public delegate bool Shape(double x, double y);
    // Worst escape for one configuration. start: which danger points count as starting positions (null = all).
    // Returns -1 if a reachable danger point has no safe point.
    public static double Worst(Shape danger, Shape start, double bx, double by, bool body, double step, double coarse) {
        double R = body ? Body+M : 0;
        Func<double,double,bool> ok = (x,y) => x*x+y*y <= A*A && (!body || (x-bx)*(x-bx)+(y-by)*(y-by) >= R*R);
        int n = (int)Math.Ceiling(2*A/step);
        var grid = new bool[n+1, n+1];
        for (int i = 0; i <= n; i++) for (int j = 0; j <= n; j++) grid[i,j] = danger(-A+i*step, -A+j*step);
        var safe = new List<double[]>();
        for (int i = 0; i <= n; i++) for (int j = 0; j <= n; j++) {
            if (grid[i,j]) continue; double x = -A+i*step, y = -A+j*step; if (!ok(x,y)) continue;
            bool edge = (i>0&&grid[i-1,j])||(i<n&&grid[i+1,j])||(j>0&&grid[i,j-1])||(j<n&&grid[i,j+1]);
            if (edge) safe.Add(new[]{x,y});
        }
        double worst = 0;
        for (double x = -A; x <= A; x += coarse) for (double y = -A; y <= A; y += coarse) {
            if (!ok(x,y) || !danger(x,y) || (start != null && !start(x,y))) continue;
            double best = double.MaxValue;
            foreach (var s in safe) {
                double e = (s[0]-x)*(s[0]-x)+(s[1]-y)*(s[1]-y);
                if (e >= best*best) continue;            // straight line is a lower bound on the routed path
                double d = Path(x,y,s[0],s[1],bx,by,R); if (d < best) best = d;
            }
            if (best == double.MaxValue) return -1;
            worst = Math.Max(worst, best);
        }
        return worst;
    }
    static double[] Radii(double max) { var l = new List<double>(); for (double b = 0; b < max; b += 1.0) l.Add(b); l.Add(max); return l.ToArray(); }

    public static string Chime() {
        // a marker centred anywhere on the floor; no boss body (worst case is a marker against the wall)
        double w = 0; string at = "";
        for (double c = 0; c <= A; c += 0.5) {
            double cc = c;
            double d = Worst((x,y) => (x-cc)*(x-cc)+y*y <= (CR+M)*(CR+M), null, 0, 0, false, 0.05, 0.1);
            if (d < 0) return "UNSOLVABLE at marker r=" + c;
            if (d > w) { w = d; at = string.Format("marker centre r={0:F1} m", c); }
        }
        return string.Format("{0:F2}|{1}", w, at);
    }
    public static string Cone() {
        double half = KH*Math.PI/180, w = 0; string at = "";
        foreach (double b in Radii(Dmax)) for (int k = 0; k <= 12; k++) {
            double dir = Math.PI*k/12, bb = b;
            double d = Worst((x,y) => SectorDist(x,y,bb,0,dir,half,KR) <= M, null, b, 0, true, 0.1, 0.1);
            if (d < 0) return "UNSOLVABLE at boss r=" + b + " dir=" + (k*15);
            if (d > w) { w = d; at = string.Format("boss r={0:F1} m, facing {1} deg from the outward radial", b, k*15); }
        }
        return string.Format("{0:F2}|{1}", w, at);
    }
    // 288-degree cone pointing away from the safe sector; gap = safe sector direction
    static Shape Fifth(double bx, double gap) {
        double half = 4*Math.PI/5;   // 144 degrees
        return (x,y) => SectorDist(x,y,bx,0,gap+Math.PI,half,SR) <= M;
    }
    public static string Silent() {
        double w = 0; string at = "";
        foreach (double b in Radii(Leash)) for (int k = 0; k <= 12; k++) {
            double gap = Math.PI*k/12;
            double d = Worst(Fifth(b, gap), null, b, 0, true, 0.1, 0.2);
            if (d < 0) return "UNSOLVABLE at boss r=" + b + " V=" + (k*15);
            if (d > w) { w = d; at = string.Format("boss r={0:F1} m, sector V at {1} deg from the outward radial", b, k*15); }
        }
        return string.Format("{0:F2}|{1}", w, at);
    }
    // Passing Silence second beat: safe sector moves one step (72 degrees). onlySafe: start only where beat 1 was safe.
    public static string Passing(bool onlySafe) {
        double w = 0; string at = "";
        foreach (double b in Radii(Leash)) for (int k = 0; k < 24; k++) foreach (int s in new[]{1,-1}) {
            double gap1 = Math.PI*k/12, gap2 = gap1 + s*2*Math.PI/5;
            var first = Fifth(b, gap1);
            double d = Worst(Fifth(b, gap2), onlySafe ? (Shape)((x,y) => !first(x,y)) : null, b, 0, true, 0.1, 0.2);
            if (d < 0) return "UNSOLVABLE at boss r=" + b;
            if (d > w) { w = d; at = string.Format("boss r={0:F1} m, V at {1} deg, steps {2}", b, k*15, s > 0 ? "anticlockwise" : "clockwise"); }
        }
        return string.Format("{0:F2}|{1}", w, at);
    }
    public static string Needle() {
        // lane from the boss centre to the arena edge, any direction; the body blocks nothing inside a lane this narrow
        double hw = NW/2 + M, w = 0; string at = "";
        foreach (double b in Radii(Dmax)) for (int k = 0; k <= 12; k++) {
            double dir = Math.PI*k/12, ux = Math.Cos(dir), uy = Math.Sin(dir), bb = b;
            double d = Worst((x,y) => { double lx = (x-bb)*ux+y*uy, ly = -(x-bb)*uy+y*ux; return Math.Abs(ly) <= hw && lx >= -M; }, null, b, 0, true, 0.05, 0.1);
            if (d < 0) return "UNSOLVABLE at boss r=" + b + " dir=" + (k*15);
            if (d > w) { w = d; at = string.Format("boss r={0:F1} m, beam {1} deg from the outward radial", b, k*15); }
        }
        return string.Format("{0:F2}|{1}", w, at);
    }
}
'@
Add-Type -TypeDefinition $code -Language CSharp
[Oracle]::A = $ArenaRadius; [Oracle]::Dmax = $DriftMax; [Oracle]::Leash = $Leash; [Oracle]::Body = $BossBody
[Oracle]::M = $PlayerMargin; [Oracle]::CR = $ChimeRadius; [Oracle]::KR = $ConeRadius; [Oracle]::KH = $ConeHalfDeg
[Oracle]::SR = $SectorRadius; [Oracle]::NW = $NeedleWidth

# Used = ticks from final lock (or from warning, for attacks fixed at warning) to resolve at Steady Ticking (spec section 3).
$rows = @(
  @{ Name = 'Shattered Chime, after lock';           R = [Oracle]::Chime();         Used = 48 },
  @{ Name = 'Afterchime repeat, from warning';       R = $null;                     Used = 90 },
  @{ Name = 'Dividing Chime, after lock';            R = [Oracle]::Cone();          Used = 51 },
  @{ Name = 'Silent Fifth, from warning';            R = [Oracle]::Silent();        Used = 144 },
  @{ Name = 'Passing Silence beat 2, from safety';   R = [Oracle]::Passing($true);  Used = 120 },
  @{ Name = 'Passing Silence beat 2, from anywhere'; R = [Oracle]::Passing($false); Used = 120 },
  @{ Name = 'Unison Needle, after lock';             R = [Oracle]::Needle();        Used = 60 }
)
$rows[1].R = $rows[0].R   # the repeat uses the same circles at the same positions

# Tiers and movement as in the Bellkeeper spec section 5.1: First Chime walk-only, the others may use one dodge.
$tiers = @(
  @{ Name = 'First Chime';    Reaction = 0.60; Dodge = $false },
  @{ Name = 'Steady Ticking'; Reaction = 0.45; Dodge = $true },
  @{ Name = 'Striking Hour';  Reaction = 0.35; Dodge = $true },
  @{ Name = 'Midnight Toll';  Reaction = 0.30; Dodge = $true }
)
function Need($d, $walk, $dodge, $dodgeTime, $useDodge) {
  if (-not $useDodge) { return $d / $walk }
  if ($d -le $dodge) { return $dodgeTime * $d / $dodge }
  return $dodgeTime + ($d - $dodge) / $walk
}
function Ticks($sec) { [int][math]::Ceiling($sec * 60 - 1e-9) }

"Arena $ArenaRadius m, drift bound $DriftMax m, leash $Leash m, body $BossBody m, margin $PlayerMargin m."
""
"## Worst-case escape distance (m, routed around the boss body)"
""
"| Attack | d_max | Worst configuration |"
"|---|---|---|"
foreach ($r in $rows) { $p = $r.R -split '\|'; "| $($r.Name) | $($p[0]) | $($p[1]) |" }
""
"## Minimum window in ticks (60 Hz)"
""
"Design movement: walk 6 m/s, dodge 3 m in 0.20 s. Steady Ticking walk-only uses the same 0.45 s reaction with no dodge. Last column: First Chime margin at the older 4.2 m/s test walk."
""
"| Attack | First Chime (walk) | Steady Ticking | Steady Ticking walk-only | Striking Hour | Midnight Toll | 4.2 m/s walk | Used at Steady Ticking |"
"|---|---|---|---|---|---|---|---|"
foreach ($r in $rows) {
  $d = [double](($r.R -split '\|')[0])
  $c = foreach ($t in $tiers) { Ticks ($t.Reaction + (Need $d 6.0 3.0 0.20 $t.Dodge)) }
  $walkSteady = Ticks (0.45 + $d / 6.0)
  $slow = Ticks (0.60 + $d / 4.2)
  $ok = if ($r.Used -ge $walkSteady) { 'walk ok' } elseif ($r.Used -ge $c[1]) { 'needs dodge' } else { 'FAILS' }
  "| $($r.Name) | $($c[0]) | $($c[1]) | $walkSteady | $($c[2]) | $($c[3]) | $slow | $($r.Used) ($ok) |"
}
