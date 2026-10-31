# Hostile mechanic icons: cleanup, export and QA sheets.
# Usage (from docs/mechanic-icons): powershell -NoProfile -File tools/clean-hostile-icons.ps1
#   A. source/hostile-chatgpt-provisional/*.png  (RGB, baked checkerboard) -> work/hostile-256/   (provisional)
#   B. source/hostile-regenerated/<id>.png       (flat #00FF00 or real alpha) -> export/hostile/<id>.png
#   C. QA sheets for the 16 requested mechanics -> qa/
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $PSScriptRoot 'IconTool.cs') -ReferencedAssemblies System.Drawing
function P($rel) { Join-Path $root $rel }
New-Item -ItemType Directory -Force (P 'work/hostile-cutout'), (P 'work/hostile-256'), (P 'export/hostile'), (P 'qa') | Out-Null

# A. Provisional ChatGPT crops.
$a = foreach ($f in Get-ChildItem (P 'source/hostile-chatgpt-provisional') -Filter 'hostile_*.png' | Sort-Object Name) {
    [IconTool]::Clean($f.FullName, (Join-Path (P 'work/hostile-cutout') $f.Name), (Join-Path (P 'work/hostile-256') $f.Name))
}
'[' + ($a -join ",`n") + ']' | Set-Content -Encoding utf8 (P 'qa/hostile-cleanup-report.json')

# B. Regenerated single-icon images, if any.
$regen = P 'source/hostile-regenerated'
if (Test-Path $regen) {
    $b = foreach ($f in Get-ChildItem $regen -Filter 'hostile_*.png' | Sort-Object Name) {
        [IconTool]::CleanChroma($f.FullName, (Join-Path (P 'export/hostile') $f.Name), '#00FF00')
    }
    if ($b) { '[' + ($b -join ",`n") + ']' | Set-Content -Encoding utf8 (P 'qa/hostile-regenerated-report.json') }
}

# C. QA sheets in the requested order. Prefer regenerated exports, else provisional, else a dashed empty slot.
$ids = 'spread','soak_tower','line_stack','stretch_tether','pass_the_mark','movement_stop','movement_move','one_side_cleave',
       'player_mirror','pull','shrinking_edge','cone','ring','lane','delayed_circle','low_bar'
$provisional = @{ one_side_cleave = 'hostile_one_side_cleave_substitute.png'; low_bar = 'hostile_low_bar_substitute.png' }
$files = @(); $labels = @()
foreach ($id in $ids) {
    $final = P "export/hostile/hostile_$id.png"
    $prov = P ('work/hostile-256/' + ($(if ($provisional[$id]) { $provisional[$id] } else { "hostile_$id.png" })))
    if (Test-Path $final) { $files += $final; $labels += $id }
    elseif (Test-Path $prov) { $files += $prov; $labels += "$id (prov.)" }
    else { $files += $null; $labels += "$id (missing)" }
}
$f = [string[]]$files; $l = [string[]]$labels
[IconTool]::Sheet($f, $l, (P 'qa/hostile-contact-cobalt.png'), 128, 8, '#17263F', '#F4E9D1', $false)
[IconTool]::Sheet($f, $l, (P 'qa/hostile-contact-ivory.png'), 128, 8, '#F4E9D1', '#17263F', $false)
[IconTool]::Sheet($f, $l, (P 'qa/hostile-64-color.png'), 64, 8, '#314C69', '#F4E9D1', $false)
[IconTool]::Sheet($f, $l, (P 'qa/hostile-64-gray.png'), 64, 8, '#314C69', '#F4E9D1', $true)

# Friendly vs hostile check (friendly sources as delivered by Codex, unreviewed).
$fr = Get-ChildItem (P 'source') -Filter 'friendly_*.png' | Sort-Object Name | Select-Object -First 8
$cmp = [string[]](@($fr | % FullName) + @($f[0], $f[9], $f[12], $f[1], $f[3], $f[4], $f[11], $f[13]))
$cl = [string[]](@($fr | % { $_.BaseName -replace '^friendly_', 'F ' }) + @($l[0], $l[9], $l[12], $l[1], $l[3], $l[4], $l[11], $l[13] | % { "H $_" }))
[IconTool]::Sheet($cmp, $cl, (P 'qa/friendly-vs-hostile-64-gray.png'), 64, 8, '#314C69', '#F4E9D1', $true)
$a
