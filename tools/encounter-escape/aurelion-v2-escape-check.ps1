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

# ---- the numbers under test (Medium, proposal; see ABILITIES.md) ----
$easy = $Difficulty -eq "Easy"; $k = if ($easy) { 1.25 } else { 1.0 }
$L = @{ HW = 1.1; Track1 = 1.2*$k; Window = 1.2*$k; Track2 = 1.0*$k }   # Meridian Lances: 2.2 m beams; aim tracks, then locks
$S = @{ R4 = 2.2; R8 = 1.8; Lock = 2.0*$k; Resolve = 3.1*$k }     # Starfall Mirror
$N = @{ Warn = 3.8*$k }                                           # Noon Shadow (half arena)
$O = @{ RimIn = 8.5; Crown = 3.6; Warn = 2.3*$k }                 # Orrery Closing
$H = @{ HW = 0.9; Lock = 1.5; Resolve = 2.5 }                     # Hour Hands (Hard only)
$react = 0.25

function EscapeTimes([double]$d, [double]$walk) {
  $w = $react + $d / $walk
  $dash = if ($d -le 3) { $react + $d / 3 * 0.2 } else { $react + 0.2 + 4/60 + ($d - 3) / $walk }
  return @([math]::Round($w, 2), [math]::Round([math]::Min($w, $dash), 2))
}
$rows = New-Object System.Collections.ArrayList
function Row([string]$name, [double]$d, [double]$window, [string]$where) {
  $a = EscapeTimes $d 4.2; $b = EscapeTimes $d 6.0
  $m = [math]::Round($window - $a[1], 2)
  $verdict = if ($d -lt 0) { "FAIL (no safe point)" } elseif ($m -lt 0.5) { "FAIL (margin < 0.5 s)" } elseif ($m -gt 1.2) { "pass (loose)" } else { "pass" }
  [void]$rows.Add("| $name | $([math]::Round($d,2)) | $window | $($a[0]) / $($a[1]) | $($b[0]) / $($b[1]) | $m | $verdict | $where |")
}

# 1. Meridian Lances: 3 beams (P1) and 4 beams (P2/P3), every boss radius and aim
foreach ($beams in 3, 4) {
  $at = ""; $worst = [AE]::Lances($beams, $L.HW, [ref]$at)
  Row "Meridian Lances, $beams beams, volley 1 (tracks $($L.Track1) s, locks)" $worst $L.Window $at
  Row "Meridian Lances, $beams beams, volley 2 / cascade (tracks $($L.Track2) s, locks)" $worst $L.Window $at
}

# 2. Starfall Mirror: own star at lock (r + margin), N <= 4 and N >= 5 radii
Row "Starfall Mirror, own star (N 1-4, r 2.2)" ($S.R4 + $M) ($S.Resolve - $S.Lock) "centre of your own star"
Row "Starfall Mirror, own star (N 5-8, r 1.8)" ($S.R8 + $M) ($S.Resolve - $S.Lock) "centre of your own star"

# 3. Noon Shadow: half arena through the centre (cone, half-angle 90 deg, from the centre)
$at = ""; $d = [AE]::Noon([ref]$at)
Row "Noon Shadow (half arena)" $d $N.Warn $at

# 4. Orrery Closing: rim ring 8.5 -> outside the bound, plus the crown circle round the boss
$at = ""; $d = [AE]::Orrery($O.RimIn, $O.Crown, [ref]$at)
Row "Orrery Closing (rim + crown)" $d $O.Warn $at

# 5. Hour Hands (Hard): your own spoke (lane half-width 0.9)
Row "Hour Hands (Hard), own spoke" ($H.HW + $M) ($H.Resolve - $H.Lock) "on your spoke's centre line"

# ---- pairings (joint routes) ----
# Noon + Starfall: the deep-edge runner (fastest route: one dash, then walk). Starfall starts 0.7 s into Noon, locks 2.0 s later on the
# runner's position, both resolve together. Running straight across clears the star and the half line together.
$t = $N.Warn; $lockT = 0.7*$k + $S.Lock
foreach ($walk in 4.2, 6.0) {
  $dLine = $Bound + $M; $need = (EscapeTimes $dLine $walk)[1]
  $ranAtLock = [math]::Max(0, ($lockT - $react) * $walk)
  $starExit = if ($ranAtLock -ge $dLine) { 0 } else { ($S.R4 + $M) / $walk }
  [void]$rows.Add("| Pairing: Noon + Starfall, edge runner at $walk m/s | $([math]::Round($dLine,2)) | $t | $([math]::Round($need,2)) | - | $([math]::Round($t - $need,2)) | $(if ($t - $need -ge 0.5) {'pass'} else {'FAIL'}) | star locks $([math]::Round($ranAtLock,1)) m into the run; exiting it takes $([math]::Round($starExit,2)) s on the same line |")
}
# Horizon + Starfall at N = 4 and N = 8: players on a 6 m circle round a centred boss, stars and mirrors locked on
# them; each must reach a point clear of every star and mirror, inside the rim and outside the crown.
foreach ($n in 4, 8) {
  $r = if ($n -le 4) { $S.R4 } else { $S.R8 }
  $d = [AE]::Layout($n, 6.0, $r, $O.RimIn, $O.Crown)
  Row "Pairing: Horizon + Starfall, N = $n on a 6 m circle" $d ($S.Resolve - $S.Lock) "every player to a spot clear of all stars and mirrors, inside the rim, outside the crown"
}

"# Aurelion ability set v2: escape check ($Difficulty)"
""
"Produced by ``tools/encounter-escape/aurelion-v2-escape-check.ps1 -Difficulty $Difficulty`` on $(Get-Date -Format 'yyyy-MM-dd HH:mm'). Arena: Skyward Spire play bound $Bound m, boss inside $BossMax m, boss body $Body m + player radius $PlayerR m, hit margin $M m. Reaction $react s is included in every escape time. Margin = window - fastest route at 4.2 m/s; the brief asks for 0.5-0.8 s."
""
"| Case | Worst escape (m) | Window (s) | 4.2 m/s walk / walk+dash (s) | 6 m/s walk / walk+dash (s) | Margin at 4.2 (s) | Verdict | Worst configuration |"
"|---|---|---|---|---|---|---|---|"
$rows
