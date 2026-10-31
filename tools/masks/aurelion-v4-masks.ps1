<#
.SYNOPSIS
  Top-down danger/safe masks for Aurelion v4 (Last Light, Final Noon, Total Eclipse wells), drawn from the runtime
  constants so art can be regenerated against the real footprints.

.DESCRIPTION
  Values copied from boss-aurelion feature/aurelion-v3 (038571a):
    Skyward Spire play bound 13.9 m (round, centred on the origin)
    LastLight.cs     45-degree wedges (half-angle 22.5) from the centre to bound + 0.5 m, order 0,180,90,270,45,225,135,315
    TotalEclipse.cs  wells orbit 6 m from the centre at 15 deg/s; N 1-4: 4 wells of 3.5 m, N 5-8: 6 wells of 3.0 m
  Orientation: +z (yaw 0, Sim.Dir(0) = forward) is UP, +x (yaw 90) is RIGHT; yaw turns clockwise seen from above.
  Scale: 1024 px = 30 m (34.13 px/m), arena centre at the image centre. Outside the play bound is transparent.
  Colours: coral #FF6F5E = danger, warm stone #D9D2C3 = safe. Shapes are the nominal hazard shapes; the hit test adds
  a 0.3 m player margin (Hazard.Margin) on top.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/masks/aurelion-v4-masks.ps1 -Out bosses/1-skyward-spire-aurelion/abilities-v4/masks
#>
param([string]$Out = "bosses/1-skyward-spire-aurelion/abilities-v4/masks")
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $Out | Out-Null

$Px = 1024; $Metres = 30.0; $S = $Px / $Metres; $C = $Px / 2
$Bound = 13.9; $Reach = $Bound + 0.5
$Danger = [System.Drawing.Color]::FromArgb(255, 0xFF, 0x6F, 0x5E)
$Safe = [System.Drawing.Color]::FromArgb(255, 0xD9, 0xD2, 0xC3)
$Order = @(0, 180, 90, 270, 45, 225, 135, 315)

function New-Canvas {
  $bmp = New-Object System.Drawing.Bitmap $Px, $Px, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.Clear([System.Drawing.Color]::Transparent)
  return @($bmp, $g)
}
# the arena disc as a clip region, filled with a base colour
function Fill-Arena($g, $color) {
  $r = $Bound * $S
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $path.AddEllipse([float]($C - $r), [float]($C - $r), [float](2 * $r), [float](2 * $r))
  $g.SetClip($path)
  $g.FillPath((New-Object System.Drawing.SolidBrush $color), $path)
}
# a wedge centred on game yaw (0 = up, clockwise), half-angle in degrees. GDI angles: 0 = +x, clockwise (y down).
function Fill-Wedge($g, [double]$yaw, [double]$half, $color) {
  $r = $Reach * $S
  $start = $yaw - 90 - $half
  $g.FillPie((New-Object System.Drawing.SolidBrush $color), [float]($C - $r), [float]($C - $r), [float](2 * $r), [float](2 * $r), [float]$start, [float](2 * $half))
}
function Fill-Disc($g, [double]$x, [double]$z, [double]$radius, $color) {
  $px = $C + $x * $S; $py = $C - $z * $S; $r = $radius * $S
  $g.FillEllipse((New-Object System.Drawing.SolidBrush $color), [float]($px - $r), [float]($py - $r), [float](2 * $r), [float](2 * $r))
}
function Save($bmp, $g, $name) { $g.Dispose(); $bmp.Save((Join-Path $Out $name), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose(); $name }

# Last Light: the pre-resolve moment of slice k (slices 1..k-1 burning, slice k in its 3 s warning) -- all danger
for ($k = 1; $k -le 8; $k++) {
  $bmp, $g = New-Canvas
  Fill-Arena $g $Safe
  for ($i = 0; $i -lt $k; $i++) { Fill-Wedge $g $Order[$i] 22.5 $Danger }
  Save $bmp $g ("last-light-step-{0}.png" -f $k)
}
# Final Noon: the eighth slice has fallen, nothing safe (also the look of the 6:30 enrage: the wipe has no shape)
$bmp, $g = New-Canvas; Fill-Arena $g $Danger; Save $bmp $g "final-noon.png"

# Last Light order key (annotated, not a mask)
$bmp, $g = New-Canvas
Fill-Arena $g $Safe
$font = New-Object System.Drawing.Font "Arial", 44, ([System.Drawing.FontStyle]::Bold)
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 120, 60, 50)), 3
for ($i = 0; $i -lt 8; $i++) {
  $shade = [System.Drawing.Color]::FromArgb(255, 0xFF, [int](0xB0 - 0x0C * $i), [int](0xA0 - 0x0B * $i))
  Fill-Wedge $g $Order[$i] 22.5 $shade
  $r = $Reach * $S; $g.DrawPie($pen, [float]($C - $r), [float]($C - $r), [float](2 * $r), [float](2 * $r), [float]($Order[$i] - 90 - 22.5), [float]45)
  $a = $Order[$i] * [Math]::PI / 180; $tx = $C + [Math]::Sin($a) * 8.5 * $S; $ty = $C - [Math]::Cos($a) * 8.5 * $S
  $g.DrawString(("{0}" -f ($i + 1)), $font, [System.Drawing.Brushes]::Black, [float]($tx - 16), [float]($ty - 28))
}
Save $bmp $g "last-light-order-key.png"

# Total Eclipse: Umbra everywhere except the light wells, at the intermission's first tick (orbit angle 0)
foreach ($set in @(@{ Name = "n1-4"; Count = 4; R = 3.5 }, @{ Name = "n5-8"; Count = 6; R = 3.0 })) {
  $bmp, $g = New-Canvas
  Fill-Arena $g $Danger
  for ($i = 0; $i -lt $set.Count; $i++) {
    $a = (360.0 * $i / $set.Count) * [Math]::PI / 180
    Fill-Disc $g ([Math]::Sin($a) * 6.0) ([Math]::Cos($a) * 6.0) $set.R $Safe
  }
  Save $bmp $g ("total-eclipse-wells-{0}.png" -f $set.Name)
}
