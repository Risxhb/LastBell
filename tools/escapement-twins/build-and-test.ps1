# Builds this worktree into its own test player and runs the Escapement Twins autotest plus our usual quick checks
# (Arcanist-only autotest, menu flow quick, a practice launch). Chzy18's suites are skipped. Never touches Play-Last Bell.
#   powershell -ExecutionPolicy Bypass -File tools/escapement-twins/build-and-test.ps1 [-Run r1] [-SkipBuild] [-TwinsOnly] [-PracticeSeconds 30]
#     [-Builds miniboss-escapement-twins] [-Evidence miniboss-escapement-twins]   (folders under builds\ and build-evidence\)
# Player: F:\Last Bell\Collab\builds\miniboss-escapement-twins\<run>\   Evidence: F:\Last Bell\Collab\build-evidence\miniboss-escapement-twins\<run>\
# Waits while 2 or more Last Bell Unity editors are running (other miniboss sessions build at the same time).
param([string]$Run="r1", [switch]$SkipBuild, [switch]$TwinsOnly, [int]$PracticeSeconds=30, [string]$Builds="miniboss-escapement-twins", [string]$Evidence="miniboss-escapement-twins")
$root = 'F:\Last Bell\Collab'
$src = (Resolve-Path "$PSScriptRoot\..\..").Path
$unity = "$root\.unity-tools\6000.6.6f1\Editor\Unity.exe"
$stage = "$root\builds\$Builds\$Run"; $ev = "$root\build-evidence\$Evidence\$Run"
New-Item -ItemType Directory -Force $ev | Out-Null
if (-not $SkipBuild) {
  # count Last Bell editors only: an unrelated project on this machine keeps its own Unity processes running all day
  while (@(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -like '*Last Bell*' -and $_.CommandLine -notlike '*AssetImportWorker*' }).Count -ge 2) { Start-Sleep 30 }
  "unity free $(Get-Date -Format HH:mm)"
  if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
  $env:LASTBELL_WINDOWS_OUTPUT = "$stage\LastBell.exe"
  $b = Start-Process $unity -ArgumentList "-batchmode -projectPath `"$src\unity`" -executeMethod LastBell.Menu.EditorTools.ConfrontationMenuBuild.BuildWindows -logFile `"$ev\build.log`"" -PassThru -Wait
  "build exit=$($b.ExitCode) $(Get-Date -Format HH:mm)"
  Select-String -Path "$ev\build.log" -Pattern 'Confrontation build:|error CS' | Select-Object -First 20 | ForEach-Object { $_.Line }
  if ($b.ExitCode -ne 0) { "BUILD FAILED"; exit 1 }
}
$exe = "$stage\LastBell.exe"; $ok = $true; $failed = @()
$runs = @(
  @('twins', '1920', '1080', '-escapementTwinsTest'),
  @('arcanist', '1600', '900', '-lastbellAutotest -lastbellArcanistOnly'),
  @('flow', '1920', '1080', '-lastbellFlowStage 8 -lastbellFlowQuick'))
if ($TwinsOnly) { $runs = @(,$runs[0]) }
# one rerun per failing suite, as in tools/combined-rebuild/build-worktree-test.ps1 (the flow suite has known timing flakes)
foreach ($r in $runs) {
  $passed = $false
  foreach ($attempt in 1, 2) {
    $d = "$ev\$($r[0])"; if ($attempt -gt 1) { $d = "$ev\$($r[0])-rerun" }
    if (Test-Path $d) { Remove-Item $d -Recurse -Force }; New-Item -ItemType Directory -Force $d | Out-Null
    $p = Start-Process $exe -ArgumentList "-screen-fullscreen 0 -screen-width $($r[1]) -screen-height $($r[2]) $($r[3]) -lastbellOut `"$d`" -logFile `"$d\player.log`"" -PassThru
    if (-not $p.WaitForExit(1500000)) { Stop-Process -Id $p.Id -Force; "$($r[0]) attempt $attempt TIMEOUT" }
    else { "$($r[0]) attempt $attempt exit=$($p.ExitCode) $(Get-Date -Format HH:mm)"; if ($p.ExitCode -eq 0) { $passed = $true } }
    if ($passed) { break }
  }
  if (-not $passed) { $ok = $false; $failed += $r[0] }
}
# practice launch: the root launcher's flag, $PracticeSeconds s, no exceptions
$d = "$ev\practice-twins"; New-Item -ItemType Directory -Force $d | Out-Null
$p = Start-Process $exe -ArgumentList "-screen-fullscreen 0 -screen-width 1600 -screen-height 900 -lastbellPractice twins -logFile `"$d\player.log`"" -PassThru
Start-Sleep $PracticeSeconds; if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
$ex = @(Select-String -Path "$d\player.log" -Pattern 'Exception' -ErrorAction SilentlyContinue).Count
"practice-twins exceptions=$ex"; if ($ex -gt 0) { $ok = $false; $failed += 'practice-twins' }
Get-ChildItem $ev -Recurse -Include *.txt | ForEach-Object { "-- $($_.Directory.Name) / $($_.Name)"; Select-String -Path $_.FullName -Pattern 'RESULT|autotest:|passed|^FAIL' | Select-Object -First 12 | ForEach-Object { $_.Line } }
if ($ok) { "ALL PASSED" } else { "FAILED: $($failed -join ', ')" }

