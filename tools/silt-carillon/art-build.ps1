# Silt Carillon art pass (9 Nov 2026): builds this worktree into builds\miniboss-art-silt-carillon\<Run>\ and runs the art capture
# (-siltCarillonArtShots: every body clip from the game camera and a close camera, plus the bell, mound, Drained and Peal moments).
# Waits while 2 or more Last Bell Unity editors run (shared machine). Deletes the previous capture build of the same name first.
#   powershell -ExecutionPolicy Bypass -File tools/silt-carillon/art-build.ps1 -Run model-r1 [-SkipBuild] [-Keep <old run to keep>]
param([string]$Run = "model-r1", [switch]$SkipBuild)
$root = 'F:\Last Bell\Collab'
$src = (Resolve-Path "$PSScriptRoot\..\..").Path
$unity = "$root\.unity-tools\6000.6.6f1\Editor\Unity.exe"
$stage = "$root\builds\miniboss-art-silt-carillon\$Run"; $ev = "$root\build-evidence\miniboss-art-20261109\silt-carillon\$Run"
New-Item -ItemType Directory -Force $ev | Out-Null
if (-not $SkipBuild) {
  $free = (Get-PSDrive F).Free / 1GB
  if ($free -lt 15) { "only $([Math]::Round($free,1)) GB free on F: (need 15): not building"; exit 2 }
  while (@(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -like '*Last Bell*' -and $_.CommandLine -notlike '*AssetImportWorker*' }).Count -ge 2) { Start-Sleep 30 }
  "unity free $(Get-Date -Format HH:mm), $([Math]::Round($free,1)) GB free"
  if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
  $env:LASTBELL_WINDOWS_OUTPUT = "$stage\LastBell.exe"
  $b = Start-Process $unity -ArgumentList "-batchmode -quit -projectPath `"$src\unity`" -executeMethod LastBell.Menu.EditorTools.ConfrontationMenuBuild.BuildWindows -logFile `"$ev\build.log`"" -PassThru -Wait
  "build exit=$($b.ExitCode) $(Get-Date -Format HH:mm)"
  Select-String -Path "$ev\build.log" -Pattern 'Confrontation build:|error CS' | Select-Object -First 20 | ForEach-Object { $_.Line }
  if ($b.ExitCode -ne 0 -or -not (Test-Path "$stage\LastBell.exe")) { "BUILD FAILED"; exit 1 }
}
$d = "$ev\frames"; if (Test-Path $d) { Remove-Item $d -Recurse -Force }; New-Item -ItemType Directory -Force $d | Out-Null
$p = Start-Process "$stage\LastBell.exe" -ArgumentList "-screen-fullscreen 0 -screen-width 1600 -screen-height 900 -siltCarillonArtShots -lastbellOut `"$d`" -logFile `"$d\player.log`"" -PassThru
if (-not $p.WaitForExit(300000)) { Stop-Process -Id $p.Id -Force; "capture TIMEOUT" }
"capture exit=$($p.ExitCode) $(Get-Date -Format HH:mm)"
Get-Content "$d\art-shots.txt" -ErrorAction SilentlyContinue
"exceptions: $(@(Select-String -Path "$d\player.log" -Pattern 'Exception' -ErrorAction SilentlyContinue).Count)"
