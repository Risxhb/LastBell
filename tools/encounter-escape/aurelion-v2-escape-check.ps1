<#
.SYNOPSIS
  Worst-case escape check for Aurelion's ability set v2 (proposal), Skyward Spire arena.

.DESCRIPTION
  Pure geometry (no Unity), same method as escape-check.ps1: for each sampled boss position and aim it finds the
  reachable danger point whose straight-line distance to the nearest safe point is largest. Danger shapes are
  expanded by the 0.3 m player margin (Hazard.Margin); player centres stay inside the 13.9 m play bound and outside
  the boss body (1.1 m + 0.45 m player radius). It converts the distance into escape time (walk only, and walk +
  one 3 m / 0.2 s dash with the 4-tick dash recovery) at 4.2 m/s and 6 m/s, and reports the margin left inside the
  window that counts (lock -> resolve). The brief asks for 0.5-0.8 s of margin at 4.2 m/s after the fastest route
  (bosses/1-skyward-spire-aurelion/abilities-v2/ABILITIES.md A1). Pairings are checked as joint routes.

  Limits: straight-line travel (no routing round the boss body), grid sampling (0.2 m danger grid, 0.05 m search).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/encounter-escape/aurelion-v2-escape-check.ps1 > results.md
#>
param(
  [double]$Bound = 13.9, [double]$BossMax = 9.5, [double]$Body = 1.1, [double]$PlayerR = 0.45, [double]$M = 0.3,
  [string]$Difficulty = "Medium"
)

$code = @'
using System;
using System.Collections.Generic;
public static class AE {
    public static double R, Bmax, Body, PR, M;
    public delegate bool Shape(double x, double y);
    static double Hyp(double x, double y) { return Math.Sqrt(x*x+y*y); }
    // beams: k full-length lanes of half-width hw through (bx,by), first at angle a0 (rad)
    public static bool InBeams(double x, double y, double bx, double by, int k, double a0, double hw) {
        for (int i = 0; i < k; i++) {
            double a = a0 + i*Math.PI/k, nx = -Math.Sin(a), ny = Math.Cos(a);
            if (Math.Abs((x-bx)*nx + (y-by)*ny) <= hw + M) return true;
        }
        return false;
    }
    public static bool InCircle(double x, double y, double cx, double cy, double r) { return Hyp(x-cx, y-cy) <= r + M; }
    public static bool InRing(double x, double y, double cx, double cy, double inner, double outer) { double d = Hyp(x-cx, y-cy); return d >= inner - M && d <= outer + M; }
    public static bool Valid(double x, double y, double bx, double by, bool body) {
        return Hyp(x, y) <= R && (!body || Hyp(x-bx, y-by) >= Body + PR);
    }
    // largest straight-line distance from a valid danger point to its nearest valid safe point (-1: no safe point)
    public static double Worst(Shape danger, double bx, double by, bool body, out double wx, out double wy) {
        double worst = 0; wx = wy = 0;
        for (double x = -R; x <= R; x += 0.2)
            for (double y = -R; y <= R; y += 0.2) {
                if (!Valid(x, y, bx, by, body) || !danger(x, y)) continue;
                double d = Nearest(danger, x, y, bx, by, body);
                if (d < 0) { wx = x; wy = y; return -1; }
                if (d > worst) { worst = d; wx = x; wy = y; }
            }
        return worst;
    }
    // ---- whole cases in C# (scriptblock delegates are far too slow) ----
    public static double Lances(int k, double hw, out string at) {
        double worst = 0; at = "";
        foreach (double br in new[] { 0, 2, 4, 6, 8, 9.5 })
            foreach (double deg in new[] { 0, 7.5, 15, 22.5, 30, 45 }) {
                double a0 = deg * Math.PI / 180, bx = br, by = 0, wx, wy;
                double d = Worst((x, y) => InBeams(x, y, bx, by, k, a0, hw), bx, by, true, out wx, out wy);
                if (d < 0 || d > worst) { worst = d; at = "boss r=" + br + ", aim " + deg + " deg, worst (" + wx.ToString("0.0") + ", " + wy.ToString("0.0") + ")"; }
                if (d < 0) return d;
            }
        return worst;
    }
    public static double Noon(out string at) {
        double wx, wy; double d = Worst((x, y) => x >= -M, 0, 0, false, out wx, out wy);
        at = "edge point opposite the line (" + wx.ToString("0.0") + ", " + wy.ToString("0.0") + ")"; return d;
    }
    public static double Orrery(double rimIn, double crown, out string at) {
        double worst = 0; at = "";
        foreach (double br in new[] { 0, 2, 4, 6, 8, 9.5 }) {
            double bx = br, by = 0, wx, wy;
            double d = Worst((x, y) => InRing(x, y, 0, 0, rimIn, 30) || InCircle(x, y, bx, by, crown), bx, by, true, out wx, out wy);
            if (d < 0 || d > worst) { worst = d; at = "boss r=" + br + ", worst (" + wx.ToString("0.0") + ", " + wy.ToString("0.0") + ")"; }
        }
        return worst;
    }
    // N players on a circle of radius rho round a centred boss, stars + mirrors locked on them, inside the Horizon
    public static double Layout(int n, double rho, double star, double rimIn, double crown) {
        double worst = 0;
        for (int i = 0; i < n; i++) {
            double px = rho * Math.Cos(2 * Math.PI * i / n), py = rho * Math.Sin(2 * Math.PI * i / n);
            Shape sh = (x, y) => {
                if (InRing(x, y, 0, 0, rimIn, 30) || InCircle(x, y, 0, 0, crown)) return true;
                for (int j = 0; j < n; j++) {
                    double sx = rho * Math.Cos(2 * Math.PI * j / n), sy = rho * Math.Sin(2 * Math.PI * j / n);
                    if (InCircle(x, y, sx, sy, star) || InCircle(x, y, -sx, -sy, star)) return true;
                }
                return false; };
            double d = Nearest(sh, px, py, 0, 0, true);
            if (d < 0) return -1;
            worst = Math.Max(worst, d);
        }
        return worst;
    }
    // v2.1 Zenith Collapse (annulus on a centred boss), alone and with k beams aimed at a0
    public static double Zenith(double disc, out double wx, out double wy) { return Worst((x, y) => InRing(x, y, 0, 0, disc, 30), 0, 0, true, out wx, out wy); }
    public static double ZenithLances(double disc, int k, double a0, double hw, out double wx, out double wy) { return Worst((x, y) => InRing(x, y, 0, 0, disc, 30) || InBeams(x, y, 0, 0, k, a0, hw), 0, 0, true, out wx, out wy); }
    public static double Nearest(Shape danger, double x, double y, double bx, double by, bool body) {
        for (double r = 0.05; r <= 2*R; r += 0.05)
            for (int k = 0; k < 120; k++) {
                double a = k*Math.PI/60, qx = x + r*Math.Cos(a), qy = y + r*Math.Sin(a);
                if (Valid(qx, qy, bx, by, body) && !danger(qx, qy)) return r;
            }
        return -1;
    }
}
'@
Add-Type -TypeDefinition $code -Language CSharp
[AE]::R = $Bound; [AE]::Bmax = $BossMax; [AE]::Body = $Body; [AE]::PR = $PlayerR; [AE]::M = $M

# ---- the numbers under test (proposal; ABILITIES.md v2, ABILITIES-v2.1.md for Hard and the new cases) ----
# Tracks and follows stretch x1.25 on Easy and stay put on Hard; lock-to-hit windows are x1.25 on Easy and x0.7 on Hard
# (v2.1 §2: never below the 2 s total-warning floor, Orrery stops at 2.0 s, Starfall keeps 1.1 s when paired with Lances).
$easy = $Difficulty -eq "Easy"; $hard = $Difficulty -eq "Hard"
$k = if ($easy) { 1.25 } else { 1.0 }
$wk = if ($easy) { 1.25 } elseif ($hard) { 0.7 } else { 1.0 }
$L = @{ HW = 1.1; Track1 = 1.2*$k; Window = 1.2*$wk; Track2 = $(if ($hard) { 1.2 } else { 1.0*$k }) }   # Meridian Lances: 2.2 m beams
$S = @{ R4 = 2.2; R8 = 1.8; Lock = 2.0*$k; Window = 1.1*$wk; Paired = 1.1*$k }   # Starfall Mirror (Paired = window beside Lances)
$N = @{ Warn = 3.8*$wk }                                          # Noon Shadow (half arena; locked at once)
$O = @{ RimIn = 8.5; Crown = 3.6; Warn = $(if ($hard) { 2.0 } else { 2.3*$k }) }   # Orrery Closing (locked at once)
$H = @{ HW = 0.9; Window = 1.0*$wk }                              # Hour Hands (Hard only)
$Z = @{ Warn = $(if ($hard) { 2.4 } else { 3.0*$k }) }            # Zenith Collapse (v2.1; locked at once)
$react = 0.25; $sprint = 9.0; $drain = 25.0
$need = if ($hard) { 0.0 } else { 0.3 }                            # v2.1 §4: Hard margins >= 0 on the fastest route, Medium/Easy >= 0.3 s

# walk, walk + one dash (3 m / 0.2 s + 4 ticks recovery), sprint (9 m/s, 25 stamina/s; always affordable from full here)
function EscapeTimes([double]$d, [double]$walk) {
  $w = $react + $d / $walk
  $dash = if ($d -le 3) { $react + $d / 3 * 0.2 } else { $react + 0.2 + 4/60 + ($d - 3) / $walk }
  $sp = $react + $d / $sprint
  return @([math]::Round($w, 2), [math]::Round([math]::Min($w, $dash), 2), [math]::Round($sp, 2))
}
$rows = New-Object System.Collections.ArrayList
function Row([string]$name, [double]$d, [double]$window, [string]$where) {
  $a = EscapeTimes $d 4.2; $b = EscapeTimes $d 6.0
  $fast = [math]::Min([math]::Min($a[0], $a[1]), $a[2])
  $route = if ($fast -eq $a[0]) { "walk" } elseif ($fast -eq $a[1]) { "dash" } else { "sprint ($([math]::Round($d / $sprint * $drain)) st)" }
  $m = [math]::Round($window - $fast, 2)
  $verdict = if ($d -lt 0) { "FAIL (no safe point)" } elseif ($m -lt $need) { "FAIL (margin < $need s)" } else { "pass" }
  [void]$rows.Add("| $name | $([math]::Round($d,2)) | $([math]::Round($window,2)) | $($a[0]) / $($a[1]) / $($a[2]) | $($b[0]) / $($b[1]) | $m ($route) | $verdict | $where |")
}

# 1. Meridian Lances: 3 beams (P1) and 4 beams (P2/P3), every boss radius and aim
foreach ($beams in 3, 4) {
  $at = ""; $worst = [AE]::Lances($beams, $L.HW, [ref]$at)
  Row "Meridian Lances, $beams beams, volley 1 (tracks $($L.Track1) s, locks)" $worst $L.Window $at
  Row "Meridian Lances, $beams beams, volley 2 / cascade (tracks $($L.Track2) s, locks)" $worst $L.Window $at
}

# 2. Starfall Mirror: own star at lock (r + margin), N <= 4 and N >= 5 radii
Row "Starfall Mirror, own star (N 1-4, r 2.2)" ($S.R4 + $M) $S.Window "centre of your own star"
Row "Starfall Mirror, own star (N 5-8, r 1.8)" ($S.R8 + $M) $S.Window "centre of your own star"

# 3. Noon Shadow: half arena through the centre (cone, half-angle 90 deg, from the centre)
$at = ""; $d = [AE]::Noon([ref]$at)
Row "Noon Shadow / Afternoon (half arena)" $d $N.Warn $at

# 4. Orrery Closing: rim ring 8.5 -> outside the bound, plus the crown circle round the boss
$at = ""; $d = [AE]::Orrery($O.RimIn, $O.Crown, [ref]$at)
Row "Orrery Closing (rim + crown)" $d $O.Warn $at

# 5. Hour Hands (Hard): your own spoke (lane half-width 0.9)
Row "Hour Hands, own spoke" ($H.HW + $M) $H.Window "on your spoke's centre line"

# 6. Zenith Collapse (v2.1): annulus on the boss at the centre; safe disc 4.5 (N <= 2), 5.0 (N <= 2, Phase 3), 5.5, 6.5 m
foreach ($disc in 4.5, 5.0, 5.5, 6.5) {
  $wx = 0.0; $wy = 0.0
  $d = [AE]::Zenith($disc, [ref]$wx, [ref]$wy)
  Row "Zenith Collapse, safe disc $disc m" $d $Z.Warn "rim point ($([math]::Round($wx,1)), $([math]::Round($wy,1)))"
}

# ---- pairings (joint routes) ----
# Noon + Starfall: the deep-edge runner. Starfall locks on the runner mid-run; running straight across clears the star
# and the half line together, so the route is the run itself.
foreach ($walk in 4.2, 6.0) {
  $dLine = $Bound + $M; $e = EscapeTimes $dLine $walk; $fast = [math]::Min([math]::Min($e[0], $e[1]), $e[2])
  [void]$rows.Add("| Pairing: Noon + Starfall, edge runner (walk $walk m/s) | $([math]::Round($dLine,2)) | $([math]::Round($N.Warn,2)) | $($e[0]) / $($e[1]) / $($e[2]) | - | $([math]::Round($N.Warn - $fast,2)) | $(if ($N.Warn - $fast -ge $need) {'pass'} else {'FAIL'}) | running straight across clears the star on the same line |")
}
# Horizon + Starfall at N = 4 and N = 8: players on a 6 m circle round a centred boss, stars and mirrors locked on
# them; each must reach a point clear of every star and mirror, inside the rim and outside the crown.
foreach ($n in 4, 8) {
  $r = if ($n -le 4) { $S.R4 } else { $S.R8 }
  $d = [AE]::Layout($n, 6.0, $r, $O.RimIn, $O.Crown)
  Row "Pairing: Horizon + Starfall, N = $n on a 6 m circle" $d $S.Window "every player to a spot clear of all stars and mirrors, inside the rim, outside the crown"
}
# v2.1 Lances + Starfall: leave your own star (r + margin) and end in a beam gap (<= 1.6 m more); the star keeps its full
# window beside the Lances, and the beams resolve 0.2 s before it.
Row "Pairing (v2.1): Lances + Starfall (star window kept)" ($S.R4 + $M + 1.6) $S.Paired "own star centre, then the nearest beam gap"
# v2.1 Zenith Collapse + Lances: from the rim, into the safe disc and on into a gap between the beams (boss at the centre,
# every aim). The runner must be inside the disc when the Collapse resolves (the beams follow 0.3 s later).
foreach ($c in @(@{ Disc = 4.5; Beams = 3 }, @{ Disc = 5.5; Beams = 3 }, @{ Disc = 6.5; Beams = 3 }, @{ Disc = 5.0; Beams = 4 })) {
  $worst = 0.0; $where = ""
  foreach ($deg in 0, 7.5, 15, 22.5, 30) {
    $a0 = $deg * [math]::PI / 180; $disc = $c.Disc; $beams = $c.Beams; $wx = 0.0; $wy = 0.0
    $d = [AE]::ZenithLances($disc, $beams, $a0, $L.HW, [ref]$wx, [ref]$wy)
    if ($d -lt 0 -or $d -gt $worst) { $worst = $d; $where = "aim $deg deg, from ($([math]::Round($wx,1)), $([math]::Round($wy,1)))" }
  }
  Row "Pairing (v2.1): Zenith Collapse ($($c.Disc) m) + Lances ($($c.Beams) beams)" $worst $Z.Warn $where
}

"# Aurelion ability set v2.1: escape check ($Difficulty)"
""
"Produced by ``tools/encounter-escape/aurelion-v2-escape-check.ps1 -Difficulty $Difficulty`` on $(Get-Date -Format 'yyyy-MM-dd HH:mm'). Arena: Skyward Spire play bound $Bound m, boss inside $BossMax m, boss body $Body m + player radius $PlayerR m, hit margin $M m. Reaction $react s is included in every escape time. Routes: walk, walk + one dash, sprint at $sprint m/s ($drain stamina/s). Margin = window - fastest route at 4.2 m/s walk; pass needs >= $need s (v2.1: Hard >= 0 on the fastest route, Medium and Easy >= 0.3 s)."
""
"| Case | Worst escape (m) | Window (s) | 4.2 m/s walk / walk+dash / sprint (s) | 6 m/s walk / walk+dash (s) | Margin at 4.2 (s, route) | Verdict | Worst configuration |"
"|---|---|---|---|---|---|---|---|"
$rows
