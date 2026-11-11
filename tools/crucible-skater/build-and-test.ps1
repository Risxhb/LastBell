# Builds this checkout into its own test player and runs the Crucible Skater checks: the autotest (-crucibleSkaterTest,
# which also writes in-game frames of every clip), a 60 s practice launch (-lastbellPractice skater) and one LAN
# two-process run. Chzy18's suites are skipped. Never touches Play-Last Bell. Other sessions build at the same time:
# waits while 2+ Unity.exe processes run (and while 2+ LastBell players run).
# Usage: powershell -File tools\crucible-skater\build-and-test.ps1 [-Tag art1] [-SkipBuild] [-Only crucible-skater|launch|lan]
param([string]$Tag = "art1", [switch]$SkipBuild, [string]$Only = "")
$root = 'F:\Last Bell\Collab'; $unity = "$root\.unity-tools\6000.6.6f1\Editor\Unity.exe"
$repo = (Resolve-Path "$PSScriptRoot\..\..").Path; $proj = "$repo\unity"
$stage = "$root\builds\miniboss-art-crucible-skater\$Tag"; $ev = "$root\build-evidence\miniboss-art-20261109\crucible-skater\$Tag"
New-Item -ItemType Directory -Force $ev | Out-Null
function Stamp { Get-Date -Format HH:mm:ss }
function Editors { @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -notlike '*AssetImportWorker*' }).Count }
function Players { @(Get-CimInstance Win32_Process -Filter "Name='LastBell.exe'").Count }

if (-not $SkipBuild) {
    while ((Editors) -ge 2) { Start-Sleep 30 }
    "unity slot free $(Stamp)"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    $env:LASTBELL_WINDOWS_OUTPUT = "$stage\LastBell.exe"
    $b = Start-Process $unity -ArgumentList "-batchmode -quit -projectPath `"$proj`" -executeMethod LastBell.Menu.EditorTools.ConfrontationMenuBuild.BuildWindows -logFile `"$ev\build.log`"" -PassThru -Wait
    "build exit=$($b.ExitCode) $(Stamp)"
    Select-String -Path "$ev\build.log" -Pattern 'Confrontation build:|error CS|BUILD ERROR' | Select-Object -First 25 | ForEach-Object { $_.Line }
    if ($b.ExitCode -ne 0 -or -not (Test-Path "$stage\LastBell.exe")) { "BUILD FAILED"; exit 1 }
}
$exe = "$stage\LastBell.exe"; $ok = $true; $failed = @()
if (-not $Only -or $Only -eq 'crucible-skater') {
    $passed = $false
    foreach ($attempt in 1, 2) {
        while ((Players) -ge 2) { Start-Sleep 10 }
        $d = "$ev\crucible-skater"; if ($attempt -gt 1) { $d = "$ev\crucible-skater-rerun" }
        New-Item -ItemType Directory -Force $d | Out-Null
        $p = Start-Process $exe -ArgumentList "-screen-fullscreen 0 -screen-width 1920 -screen-height 1080 -crucibleSkaterTest -lastbellOut `"$d`" -logFile `"$d\player.log`"" -PassThru
        if (-not $p.WaitForExit(1800000)) { Stop-Process -Id $p.Id -Force; "crucible-skater attempt $attempt TIMEOUT" }
        else { "crucible-skater attempt $attempt exit=$($p.ExitCode) $(Stamp)"; if ($p.ExitCode -eq 0) { $passed = $true } }
        Get-ChildItem $d -Filter *.txt | ForEach-Object { Select-String -Path $_.FullName -Pattern 'RESULT|passed,|^FAIL' | Select-Object -First 20 | ForEach-Object { "    $($_.Line)" } }
        if ($passed) { break }
    }
    if (-not $passed) { $ok = $false; $failed += 'crucible-skater' }
}
if (-not $Only -or $Only -eq 'launch') {
    while ((Players) -ge 2) { Start-Sleep 10 }
    $d = "$ev\practice-launch"; New-Item -ItemType Directory -Force $d | Out-Null
    $p = Start-Process $exe -ArgumentList "-screen-fullscreen 0 -screen-width 1600 -screen-height 900 -lastbellPractice skater -lastbellClass Arcanist -logFile `"$d\player.log`"" -PassThru
    Start-Sleep 60; if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
    $ex = @(Select-String -Path "$d\player.log" -Pattern 'Exception' -ErrorAction SilentlyContinue).Count
    $miss = @(Select-String -Path "$d\player.log" -Pattern 'Resources/Characters/Crucible\w+ missing' -ErrorAction SilentlyContinue).Count
    $started = @(Select-String -Path "$d\player.log" -Pattern 'Crucible Skater' -ErrorAction SilentlyContinue).Count
    "practice-launch 60s exceptions=$ex model-missing=$miss mentions=$started"; if ($ex -gt 0 -or $miss -gt 0) { $ok = $false; $failed += 'practice-launch' }
}
if (-not $Only -or $Only -eq 'lan') {
    while ((Players) -ge 1) { Start-Sleep 10 }
    $d = "$ev\lan"; if (Test-Path $d) { Remove-Item $d -Recurse -Force }
    & powershell -NoProfile -ExecutionPolicy Bypass -File "$repo\tools\lan-test\Run-TwoProcess.ps1" -Player $exe -OutputDirectory $d -Boss 'Crucible Skater' -Arena brass-foundry 2>&1 | Select-Object -Last 15
    "lan exit=$LASTEXITCODE $(Stamp)"; if ($LASTEXITCODE -ne 0) { $ok = $false; $failed += 'lan' }
}
if ($ok) { "ALL PASSED" } else { "FAILED: $($failed -join ', ')" }
