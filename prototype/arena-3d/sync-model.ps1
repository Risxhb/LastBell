# Copies vanguard-model.js into the single-file pages (they must open from file:// without a server).
# Replaces everything between the <vanguard-model> markers; inserts the block after INSERT-MARKER if absent.
$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding $false
$src = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot "vanguard-model.js"), $utf8)
$block = $src.Substring($src.IndexOf('// <vanguard-model>'))
$block = $block.Substring(0, $block.IndexOf('// </vanguard-model>') + '// </vanguard-model>'.Length)
foreach ($page in 'index.html', 'mockup.html') {
  $path = Join-Path $PSScriptRoot $page
  $html = [System.IO.File]::ReadAllText($path, $utf8)
  $start = $html.IndexOf('// <vanguard-model>')
  if ($start -ge 0) {
    $end = $html.IndexOf('// </vanguard-model>') + '// </vanguard-model>'.Length
    $html = $html.Substring(0, $start) + $block + $html.Substring($end)
  } else {
    $marker = '/* VANGUARD-MODEL-INSERT */'
    if ($html.IndexOf($marker) -lt 0) { throw "$page has neither the model block nor $marker" }
    $html = $html.Replace($marker, $block)
  }
  [System.IO.File]::WriteAllText($path, $html, $utf8)
  "synced $page"
}
