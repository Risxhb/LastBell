# Vesper Seedkeeper art pass (9 Nov 2026): builds this worktree into its own player and runs the Vesper Seedkeeper autotest
# (it also saves the art frames), plus, with -Full, a 60 s practice launch and one LAN two-process run. Chzy18's suites
# are skipped. Never touches Play-Last Bell.
#   powershell -ExecutionPolicy Bypass -File tools/vesper-seedkeeper/build-and-test.ps1 -Run tex1 [-Full] [-SkipBuild]
# Player: F:\Last Bell\Collab\builds\miniboss-art-vesper-seedkeeper\<run>\
# Evidence: F:\Last Bell\Collab\build-evidence\miniboss-art-20261109\vesper-seedkeeper\<run>\
# Waits while 2 or more Last Bell Unity editors are running (the other art threads build at the same time).
param([string]$Run="r1", [switch]$Full, [switch]$SkipBuild)
$root = 'F:\Last Bell\Collab'
$src = (Resolve-Path "$PSScriptRoot\..\..").Path
$unity = "$root\.unity-tools\6000.6.6f1\Editor\Unity.exe"
$stage = "$root\builds\miniboss-art-vesper-seedkeeper\$Run"; $ev = "$root\build-evidence\miniboss-art-20261109\vesper-seedkeeper\$Run"
New-Item -ItemType Directory -Force $ev | Out-Null
if (-not $SkipBuild) {
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
# the Vesper autotest, one rerun on failure
$passed = $false
foreach ($attempt in 1, 2) {
  $d = "$ev\vesper"; if ($attempt -gt 1) { $d = "$ev\vesper-rerun" }
  if (Test-Path $d) { Remove-Item $d -Recurse -Force }; New-Item -ItemType Directory -Force $d | Out-Null
  $p = Start-Process $exe -ArgumentList "-screen-fullscreen 0 -screen-width 1920 -screen-height 1080 -vesperSeedkeeperTest -lastbellOut `"$d`" -logFile `"$d\player.log`"" -PassThru
  if (-not $p.WaitForExit(1500000)) { Stop-Process -Id $p.Id -Force; "vesper attempt $attempt TIMEOUT" }
  else { "vesper attempt $attempt exit=$($p.ExitCode) $(Get-Date -Format HH:mm)"; if ($p.ExitCode -eq 0) { $passed = $true } }
  if ($passed) { break }
}
if (-not $passed) { $ok = $false; $failed += 'vesper' }
if ($Full) {
  # practice launch: the root launcher's flag, 60 s, no exceptions
  $d = "$ev\practice-vesper"; New-Item -ItemType Directory -Force $d | Out-Null
  $p = Start-Process $exe -ArgumentList "-screen-fullscreen 0 -screen-width 1600 -screen-height 900 -lastbellPractice vesper -logFile `"$d\player.log`"" -PassThru
  Start-Sleep 60; if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
  $ex = @(Select-String -Path "$d\player.log" -Pattern 'Exception' -ErrorAction SilentlyContinue).Count
  "practice-vesper exceptions=$ex"; if ($ex -gt 0) { $ok = $false; $failed += 'practice-vesper' }
  # one LAN two-process run (host + one client) on the Verdant Chimes
  $d = "$ev\lan"; if (Test-Path $d) { Remove-Item $d -Recurse -Force }
  try {
    & "$src\tools\lan-test\Run-TwoProcess.ps1" -Player $exe -OutputDirectory $d -Boss 'Vesper Seedkeeper' -Arena verdant-chimes 2>&1 | Select-Object -Last 15
  } catch { "LAN run threw: $_"; $ok = $false; $failed += 'lan' }
}
Get-ChildItem $ev -Recurse -Include *.txt | ForEach-Object { "-- $($_.Directory.Name) / $($_.Name)"; Select-String -Path $_.FullName -Pattern 'RESULT|autotest:|passed|^FAIL' | Select-Object -First 12 | ForEach-Object { $_.Line } }
if ($ok) { "ALL PASSED" } else { "FAILED: $($failed -join ', ')" }
