# Builds this worktree (feature/miniboss-crucible-skater) into its own test player and runs the Crucible Skater autotest
# plus our usual quick checks (Arcanist-only, menu flow quick) and a practice launch. Chzy18's suites are skipped.
# Never touches Play-Last Bell. Other miniboss sessions build at the same time: waits while 2+ Last Bell Unity editors run.
# Usage: powershell -File tools\crucible-skater\build-and-test.ps1 [-Tag r1] [-SkipBuild]
param([string]$Tag = "r1", [switch]$SkipBuild, [string]$Only = "")
$root = 'F:\Last Bell\Collab'; $unity = "$root\.unity-tools\6000.6.6f1\Editor\Unity.exe"
$proj = "$root\minibosses\crucible-skater\unity"
$stage = "$root\builds\miniboss-crucible-skater\$Tag"; $ev = "$root\build-evidence\miniboss-crucible-skater\$Tag"
New-Item -ItemType Directory -Force $ev | Out-Null
function Stamp { Get-Date -Format HH:mm:ss }
function LastBellEditors { @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -like '*Last Bell*' -and $_.CommandLine -like '*-batchmode*' }).Count }
function Players { @(Get-CimInstance Win32_Process -Filter "Name='LastBell.exe'").Count }

if (-not $SkipBuild) {
    while ((LastBellEditors) -ge 2) { Start-Sleep 20 }
    "unity slot free $(Stamp)"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    $env:LASTBELL_WINDOWS_OUTPUT = "$stage\LastBell.exe"
    $b = Start-Process $unity -ArgumentList "-batchmode -projectPath `"$proj`" -executeMethod LastBell.Menu.EditorTools.ConfrontationMenuBuild.BuildWindows -logFile `"$ev\build.log`"" -PassThru -Wait
    "build exit=$($b.ExitCode) $(Stamp)"
    Select-String -Path "$ev\build.log" -Pattern 'Confrontation build:|error CS|BUILD ERROR' | Select-Object -First 25 | ForEach-Object { $_.Line }
    if ($b.ExitCode -ne 0) { "BUILD FAILED"; exit 1 }
}
$exe = "$stage\LastBell.exe"; $ok = $true; $failed = @()
$runs = @(
    @('crucible-skater', '1920', '1080', '-crucibleSkaterTest'),
    @('arcanist', '1600', '900', '-lastbellAutotest -lastbellArcanistOnly'),
    @('flow', '1920', '1080', '-lastbellFlowStage 8 -lastbellFlowQuick'))
foreach ($r in $runs) {
    if ($Only -and $r[0] -ne $Only) { continue }
    $passed = $false
    foreach ($attempt in 1, 2) {
        while ((Players) -ge 2) { Start-Sleep 10 }
        $d = "$ev\$($r[0])"; if ($attempt -gt 1) { $d = "$ev\$($r[0])-rerun" }
        New-Item -ItemType Directory -Force $d | Out-Null
        $p = Start-Process $exe -ArgumentList "-screen-fullscreen 0 -screen-width $($r[1]) -screen-height $($r[2]) $($r[3]) -lastbellOut `"$d`" -logFile `"$d\player.log`"" -PassThru
        if (-not $p.WaitForExit(1800000)) { Stop-Process -Id $p.Id -Force; "$($r[0]) attempt $attempt TIMEOUT" }
        else { "$($r[0]) attempt $attempt exit=$($p.ExitCode) $(Stamp)"; if ($p.ExitCode -eq 0) { $passed = $true } }
        Get-ChildItem $d -Filter *.txt | ForEach-Object { Select-String -Path $_.FullName -Pattern 'autotest:|RESULT|passed,|^FAIL' | Select-Object -First 15 | ForEach-Object { "    $($_.Line)" } }
        if ($passed) { break }
    }
    if (-not $passed) { $ok = $false; $failed += $r[0] }
}
if (-not $Only -or $Only -eq 'launch') {
    while ((Players) -ge 2) { Start-Sleep 10 }
    $d = "$ev\practice-launch"; New-Item -ItemType Directory -Force $d | Out-Null
    $p = Start-Process $exe -ArgumentList "-screen-fullscreen 0 -screen-width 1600 -screen-height 900 -lastbellPractice skater -lastbellClass Arcanist -logFile `"$d\player.log`"" -PassThru
    Start-Sleep 35; if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
    $ex = @(Select-String -Path "$d\player.log" -Pattern 'Exception' -ErrorAction SilentlyContinue).Count
    $started = @(Select-String -Path "$d\player.log" -Pattern 'Crucible Skater' -ErrorAction SilentlyContinue).Count
    "practice-launch exceptions=$ex mentions=$started"; if ($ex -gt 0) { $ok = $false; $failed += 'practice-launch' }
}
if ($ok) { "ALL PASSED" } else { "FAILED: $($failed -join ', ')" }
