# Silt Carillon art pass (9 Nov 2026): the final checks on the built player in builds\miniboss-art-silt-carillon\<Run>\
#   1. the Silt autotest (-siltCarillonTest), 2. a 60 s practice launch (-lastbellPractice silt), 3. one LAN two-process run.
#   powershell -ExecutionPolicy Bypass -File tools/silt-carillon/final-tests.ps1 [-Run final]
param([string]$Run = "final")
$root = 'F:\Last Bell\Collab'
$src = (Resolve-Path "$PSScriptRoot\..\..").Path
$exe = "$root\builds\miniboss-art-silt-carillon\$Run\LastBell.exe"
$ev = "$root\build-evidence\miniboss-art-20261109\silt-carillon\$Run\tests"
if (-not (Test-Path $exe)) { "no player at $exe"; exit 2 }
if (Test-Path $ev) { Remove-Item $ev -Recurse -Force }; New-Item -ItemType Directory -Force $ev | Out-Null

# 1. autotest
$d = "$ev\autotest"; New-Item -ItemType Directory -Force $d | Out-Null
$p = Start-Process $exe -ArgumentList "-screen-fullscreen 0 -screen-width 1600 -screen-height 900 -siltCarillonTest -lastbellOut `"$d`" -logFile `"$d\player.log`"" -PassThru
if (-not $p.WaitForExit(600000)) { Stop-Process -Id $p.Id -Force; "autotest TIMEOUT" }
"autotest exit=$($p.ExitCode)"
Get-Content "$d\silt-carillon-report.txt" -ErrorAction SilentlyContinue | Select-Object -First 1
Select-String -Path "$d\silt-carillon-report.txt" -Pattern '^FAIL' -ErrorAction SilentlyContinue | ForEach-Object { $_.Line }
"autotest exceptions: $(@(Select-String -Path "$d\player.log" -Pattern 'Exception' -ErrorAction SilentlyContinue).Count)"

# 2. 60 s practice launch
$d = "$ev\practice"; New-Item -ItemType Directory -Force $d | Out-Null
$p = Start-Process $exe -ArgumentList "-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -lastbellPractice silt -logFile `"$d\player.log`"" -PassThru
Start-Sleep 60
$alive = -not $p.HasExited
if ($alive) { Stop-Process -Id $p.Id -Force }
"practice: alive after 60 s = $alive"
Select-String -Path "$d\player.log" -Pattern '-lastbellPractice:' -ErrorAction SilentlyContinue | Select-Object -First 2 | ForEach-Object { $_.Line }
"practice exceptions: $(@(Select-String -Path "$d\player.log" -Pattern 'Exception' -ErrorAction SilentlyContinue).Count)"

# 3. LAN two-process
$d = "$ev\lan"
& powershell -ExecutionPolicy Bypass -File "$src\tools\lan-test\Run-TwoProcess.ps1" -Player $exe -OutputDirectory $d -Boss "Silt Carillon" -Arena verdant-chimes 2>&1 | Select-Object -Last 15
"lan exit=$LASTEXITCODE"
