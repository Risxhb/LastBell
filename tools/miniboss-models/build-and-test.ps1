# Builds this worktree into one test player and runs only the miniboss-models checks (plan v3 "Tests"): the Rivet Cantor,
# Briar Metronome and Escapement Twins autotests, then a 30 s practice launch of each (0 exceptions). Chzy18's suites and
# the Arcanist / flow suites are skipped. Never touches Play-Last Bell. Needs >= 15 GB free on F: (checked first).
#   powershell -ExecutionPolicy Bypass -File tools/miniboss-models/build-and-test.ps1 [-Run models1] [-SkipBuild]
# Player: F:\Last Bell\Collab\builds\miniboss-escapement-twins\<run>\   Evidence: F:\Last Bell\Collab\build-evidence\miniboss-models-20261109\<run>\
param([string]$Run="models1", [switch]$SkipBuild)
$root = 'F:\Last Bell\Collab'
$src = (Resolve-Path "$PSScriptRoot\..\..").Path
$unity = "$root\.unity-tools\6000.6.6f1\Editor\Unity.exe"
$stage = "$root\builds\miniboss-escapement-twins\$Run"; $ev = "$root\build-evidence\miniboss-models-20261109\$Run"
New-Item -ItemType Directory -Force $ev | Out-Null
if (-not $SkipBuild) {
  $free = (Get-PSDrive F).Free / 1GB
  if ($free -lt 15) { "only $([Math]::Round($free,1)) GB free on F: (need 15): not building"; exit 2 }
  while (@(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -like '*Last Bell*' -and $_.CommandLine -notlike '*AssetImportWorker*' }).Count -ge 2) { Start-Sleep 30 }
  "unity free $(Get-Date -Format HH:mm), $([Math]::Round($free,1)) GB free"
  if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
  $env:LASTBELL_WINDOWS_OUTPUT = "$stage\LastBell.exe"
  $b = Start-Process $unity -ArgumentList "-batchmode -projectPath `"$src\unity`" -executeMethod LastBell.Menu.EditorTools.ConfrontationMenuBuild.BuildWindows -logFile `"$ev\build.log`"" -PassThru -Wait
  "build exit=$($b.ExitCode) $(Get-Date -Format HH:mm)"
  Select-String -Path "$ev\build.log" -Pattern 'Confrontation build:|error CS' | Select-Object -First 20 | ForEach-Object { $_.Line }
  if ($b.ExitCode -ne 0) { "BUILD FAILED"; exit 1 }
}
$exe = "$stage\LastBell.exe"; $ok = $true; $failed = @()
$runs = @(
  @('rivet', '-rivetCantorTest'),   # the miniboss tests boot themselves; -lastbellAutotest would also start the run suite (Chzy18's)
  @('briar', '-briarMetronomeTest'),
  @('twins', '-escapementTwinsTest'))
foreach ($r in $runs) {
  $d = "$ev\$($r[0])"; if (Test-Path $d) { Remove-Item $d -Recurse -Force }; New-Item -ItemType Directory -Force $d | Out-Null
  $p = Start-Process $exe -ArgumentList "-screen-fullscreen 0 -screen-width 1920 -screen-height 1080 $($r[1]) -lastbellOut `"$d`" -logFile `"$d\player.log`"" -PassThru
  if (-not $p.WaitForExit(1500000)) { Stop-Process -Id $p.Id -Force; "$($r[0]) TIMEOUT"; $ok = $false; $failed += $r[0] }
  else { "$($r[0]) exit=$($p.ExitCode) $(Get-Date -Format HH:mm)"; if ($p.ExitCode -ne 0) { $ok = $false; $failed += $r[0] } }
}
foreach ($boss in 'rivet', 'briar', 'twins') {
  $d = "$ev\practice-$boss"; New-Item -ItemType Directory -Force $d | Out-Null
  $p = Start-Process $exe -ArgumentList "-screen-fullscreen 0 -screen-width 1600 -screen-height 900 -lastbellPractice $boss -logFile `"$d\player.log`"" -PassThru
  Start-Sleep 30; if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
  $ex = @(Select-String -Path "$d\player.log" -Pattern 'Exception' -ErrorAction SilentlyContinue).Count
  $model = @(Select-String -Path "$d\player.log" -Pattern 'Resources/Characters/(RivetCantor|BriarMetronome|EscapementTick|EscapementTock|EscapementWheel) missing' -ErrorAction SilentlyContinue).Count
  "practice-$boss exceptions=$ex missing-model-warnings=$model"; if ($ex -gt 0 -or $model -gt 0) { $ok = $false; $failed += "practice-$boss" }
}
Get-ChildItem $ev -Recurse -Include *.txt | ForEach-Object { "-- $($_.Directory.Name) / $($_.Name)"; Select-String -Path $_.FullName -Pattern 'RESULT|autotest:|passed|^FAIL' | Select-Object -First 12 | ForEach-Object { $_.Line } }
if ($ok) { "ALL PASSED" } else { "FAILED: $($failed -join ', ')" }
