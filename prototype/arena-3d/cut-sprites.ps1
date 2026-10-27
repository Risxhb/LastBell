# Cuts the Vanguard turnaround out of reference/LAST-BELL-VANGUARD-MOCKUP.png into transparent PNG sprites.
# The background is removed by flood fill from the crop border over low-saturation grey, so interior greys survive.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$src = Join-Path $PSScriptRoot '..\..\reference\LAST-BELL-VANGUARD-MOCKUP.png'
$out = Join-Path $PSScriptRoot 'sprites'; New-Item -ItemType Directory -Force $out | Out-Null
$sheet = [System.Drawing.Bitmap]::FromFile((Resolve-Path $src))
$views = [ordered]@{ front = @(780, 52, 148, 252); side = @(935, 52, 76, 252); back = @(1020, 52, 130, 252); threequarter = @(1148, 52, 140, 252) }
function IsBg($c) { $mx = [Math]::Max($c.R, [Math]::Max($c.G, $c.B)); $mn = [Math]::Min($c.R, [Math]::Min($c.G, $c.B)); return ($mx -gt 105) -and (($mx - $mn) -lt 26) }
foreach ($name in $views.Keys) {
  $v = $views[$name]; $rect = New-Object System.Drawing.Rectangle $v[0], $v[1], $v[2], $v[3]
  $crop = $sheet.Clone($rect, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $w = $crop.Width; $h = $crop.Height; $bg = New-Object 'bool[,]' $w, $h
  $q = New-Object System.Collections.Generic.Queue[int]
  for ($x = 0; $x -lt $w; $x++) { $q.Enqueue($x); $q.Enqueue(0); $q.Enqueue($x); $q.Enqueue($h - 1) }
  for ($y = 0; $y -lt $h; $y++) { $q.Enqueue(0); $q.Enqueue($y); $q.Enqueue($w - 1); $q.Enqueue($y) }
  while ($q.Count -gt 0) {
    $x = $q.Dequeue(); $y = $q.Dequeue()
    if ($x -lt 0 -or $y -lt 0 -or $x -ge $w -or $y -ge $h -or $bg[$x, $y]) { continue }
    if (-not (IsBg $crop.GetPixel($x, $y))) { continue }
    $bg[$x, $y] = $true
    $q.Enqueue($x + 1); $q.Enqueue($y); $q.Enqueue($x - 1); $q.Enqueue($y); $q.Enqueue($x); $q.Enqueue($y + 1); $q.Enqueue($x); $q.Enqueue($y - 1)
  }
  for ($y = 0; $y -lt $h; $y++) { for ($x = 0; $x -lt $w; $x++) {
    if ($bg[$x, $y]) { $crop.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(0, 0, 0, 0)); continue }
    $edge = ($x -gt 0 -and $bg[($x - 1), $y]) -or ($x -lt $w - 1 -and $bg[($x + 1), $y]) -or ($y -gt 0 -and $bg[$x, ($y - 1)]) -or ($y -lt $h - 1 -and $bg[$x, ($y + 1)])
    if ($edge) { $c = $crop.GetPixel($x, $y); $crop.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(150, $c.R, $c.G, $c.B)) }
  } }
  # 3x high-quality upscale so the sprite holds up in close views
  $up = New-Object System.Drawing.Bitmap ($w * 3), ($h * 3), ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($up); $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'; $g.DrawImage($crop, 0, 0, $w * 3, $h * 3); $g.Dispose()
  $up.Save((Join-Path $out "vanguard-$name.png"), [System.Drawing.Imaging.ImageFormat]::Png); $up.Dispose(); $crop.Dispose()
  "cut $name ($w x $h -> x3)"
}
$sheet.Dispose()
