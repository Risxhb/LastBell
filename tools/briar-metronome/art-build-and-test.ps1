# Miniboss art pass (9 Nov 2026): builds this worktree into its own test player and runs the Briar Metronome autotest, a 60 s
# practice launch and one LAN two-process run (Chzy18's suites skipped). Never touches Play-Last Bell.
#   powershell -ExecutionPolicy Bypass -File tools/briar-metronome/art-build-and-test.ps1 [-Run r1] [-SkipBuild]
# Player: F:\Last Bell\Collab\builds\miniboss-art-briar-metronome\<run>\
# Evidence: F:\Last Bell\Collab\build-evidence\miniboss-art-20261109\briar-metronome\build-<run>\
# Waits while 2 or more Unity editors are running (the other art threads build on this machine too).
param([string]$Run="r1", [switch]$SkipBuild)
$root = 'F:\Last Bell\Collab'
$src = (Resolve-Path "$PSScriptRoot\..\..").Path
$unity = "$root\.unity-tools\6000.6.6f1\Editor\Unity.exe"
$stage = "$root\builds\miniboss-art-briar-metronome\$Run"; $ev = "$root\build-evidence\miniboss-art-20261109\briar-metronome\build-$Run"
New-Item -ItemType Directory -Force $ev | Out-Null
if (-not $SkipBuild) {
  while (@(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -notlike '*AssetImportWorker*' }).Count -ge 2) { Start-Sleep 30 }
  "unity free $(Get-Date -Format HH:mm)"
  if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
  $env:LASTBELL_WINDOWS_OUTPUT = "$stage\LastBell.exe"
  $b = Start-Process $unity -ArgumentList "-batchmode -projectPath `"$src\unity`" -executeMethod LastBell.Menu.EditorTools.ConfrontationMenuBuild.BuildWindows -logFile `"$ev\build.log`"" -PassThru -Wait
  "build exit=$($b.ExitCode) $(Get-Date -Format HH:mm)"
  Select-String -Path "$ev\build.log" -Pattern 'Confrontation build:|error CS' | Select-Object -First 20 | ForEach-Object { $_.Line }
  if ($b.ExitCode -ne 0) { "BUILD FAILED"; exit 1 }
}
$exe = "$stage\LastBell.exe"; $ok = $true; $failed = @()
# the Briar Metronome autotest (one rerun on failure)
$passed = $false
foreach ($attempt in 1, 2) {
  $d = "$ev\briar"; if ($attempt -gt 1) { $d = "$ev\briar-rerun" }
  if (Test-Path $d) { Remove-Item $d -Recurse -Force }; New-Item -ItemType Directory -Force $d | Out-Null
  $p = Start-Process $exe -ArgumentList "-screen-fullscreen 0 -screen-width 1920 -screen-height 1080 -briarMetronomeTest -lastbellOut `"$d`" -logFile `"$d\player.log`"" -PassThru
  if (-not $p.WaitForExit(1500000)) { Stop-Process -Id $p.Id -Force; "briar attempt $attempt TIMEOUT" }
  else { "briar attempt $attempt exit=$($p.ExitCode) $(Get-Date -Format HH:mm)"; if ($p.ExitCode -eq 0) { $passed = $true } }
  if ($passed) { break }
}
if (-not $passed) { $ok = $false; $failed += 'briar' }
# practice launch: 60 s, no exceptions
$d = "$ev\practice-briar"; New-Item -ItemType Directory -Force $d | Out-Null
$p = Start-Process $exe -ArgumentList "-screen-fullscreen 0 -screen-width 1600 -screen-height 900 -lastbellPractice briar -logFile `"$d\player.log`"" -PassThru
Start-Sleep 60; if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
$ex = @(Select-String -Path "$d\player.log" -Pattern 'Exception' -ErrorAction SilentlyContinue).Count
"practice-briar exceptions=$ex"; if ($ex -gt 0) { $ok = $false; $failed += 'practice-briar' }
# one LAN two-process run on Verdant Chimes
$d = "$ev\lan"; if (Test-Path $d) { Remove-Item $d -Recurse -Force }
try { & "$src\tools\lan-test\Run-TwoProcess.ps1" -Player $exe -OutputDirectory $d -Boss 'Briar Metronome' -Arena verdant-chimes; "lan exit=$LASTEXITCODE" }
catch { "lan FAILED: $_"; $ok = $false; $failed += 'lan' }
Get-ChildItem $ev -Recurse -Include *.txt | ForEach-Object { "-- $($_.Directory.Name) / $($_.Name)"; Select-String -Path $_.FullName -Pattern 'RESULT|autotest:|passed|^FAIL|PASS' | Select-Object -First 12 | ForEach-Object { $_.Line } }
if ($ok) { "ALL PASSED" } else { "FAILED: $($failed -join ', ')" }
