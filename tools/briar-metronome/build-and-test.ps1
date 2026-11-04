# Builds this worktree (feature/miniboss-briar-metronome) into its own test player and runs the Briar Metronome
# acceptance test, then our usual checks (Arcanist-only autotest, menu flow quick) and a -lastbellPractice briar launch
# smoke. Chzy18's suites are skipped. Never touches Play-Last Bell or collab\.
#   powershell -File tools\briar-metronome\build-and-test.ps1 [-Tag r1] [-SkipBuild] [-Only briar]
param([string]$Tag="r1", [switch]$SkipBuild, [string]$Only="")
$root='F:\Last Bell\Collab'; $unity="$root\.unity-tools\6000.6.6f1\Editor\Unity.exe"
$proj="$root\minibosses\briar-metronome\unity"
$stage="$root\builds\miniboss-briar-metronome\$Tag"; $ev="$root\build-evidence\miniboss-briar-metronome\$Tag"
New-Item -ItemType Directory -Force $ev | Out-Null
# other sessions build Unity on this machine too: wait while 2 or more main editors run (import workers don't count)
function Editors { @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -notmatch 'AssetImportWorker' }).Count }
if (-not $SkipBuild) {
  while ((Editors) -ge 2) { Start-Sleep 20 }
  "unity free $(Get-Date -Format HH:mm)"
  if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
  $env:LASTBELL_WINDOWS_OUTPUT="$stage\LastBell.exe"
  $b=Start-Process $unity -ArgumentList "-batchmode -projectPath `"$proj`" -executeMethod LastBell.Menu.EditorTools.ConfrontationMenuBuild.BuildWindows -logFile `"$ev\build.log`"" -PassThru
  $b.WaitForExit()   # not Start-Process -Wait: that also waits for helpers Unity leaves running (the licensing client)
  "build exit=$($b.ExitCode) $(Get-Date -Format HH:mm)"
  Select-String -Path "$ev\build.log" -Pattern 'Confrontation build:|error CS' | Select-Object -First 30 | ForEach-Object { $_.Line }
  if ($b.ExitCode -ne 0) { "BUILD FAILED"; exit 1 }
}
$exe="$stage\LastBell.exe"; $ok=$true; $failed=@()
$runs=@(
  @('briar','1920','1080','-briarMetronomeTest'),
  @('arcanist','1600','900','-lastbellAutotest -lastbellArcanistOnly'),
  @('flow','1920','1080','-lastbellFlowStage 8 -lastbellFlowQuick'))
foreach($r in $runs){
  if ($Only -and $r[0] -ne $Only) { continue }
  $passed=$false
  foreach($attempt in 1,2){
    while (Get-CimInstance Win32_Process -Filter "Name='LastBell.exe'" | Where-Object { $_.ExecutablePath -like "*miniboss-briar-metronome*" }) { Start-Sleep 10 }
    $d="$ev\$($r[0])"; if ($attempt -gt 1) { $d="$ev\$($r[0])-rerun" }
    New-Item -ItemType Directory -Force $d | Out-Null
    $p=Start-Process $exe -ArgumentList "-screen-fullscreen 0 -screen-width $($r[1]) -screen-height $($r[2]) $($r[3]) -lastbellOut `"$d`" -logFile `"$d\player.log`"" -PassThru
    if(-not $p.WaitForExit(1500000)){ Stop-Process -Id $p.Id -Force; "$($r[0]) attempt $attempt TIMEOUT" }
    else { "$($r[0]) attempt $attempt exit=$($p.ExitCode) $(Get-Date -Format HH:mm)"; if($p.ExitCode -eq 0){ $passed=$true } }
    Get-ChildItem $d -Filter *.txt | ForEach-Object { Select-String -Path $_.FullName -Pattern 'autotest:|passed|RESULT|^FAIL' | Select-Object -First 12 | ForEach-Object { $_.Line } }
    if ($passed) { break }
  }
  if (-not $passed) { $ok=$false; $failed+=$r[0] }
}
if (-not $Only -or $Only -eq 'launch') {
  $d="$ev\practice-briar"; New-Item -ItemType Directory -Force $d | Out-Null
  $p=Start-Process $exe -ArgumentList "-screen-fullscreen 0 -screen-width 1600 -screen-height 900 -lastbellPractice briar -lastbellAllies 3 -logFile `"$d\player.log`"" -PassThru
  Start-Sleep 40; if(-not $p.HasExited){ Stop-Process -Id $p.Id -Force }
  $ex=@(Select-String -Path "$d\player.log" -Pattern 'Exception' -ErrorAction SilentlyContinue).Count
  Select-String -Path "$d\player.log" -Pattern 'lastbellPractice' | Select-Object -First 3 | ForEach-Object { $_.Line }
  "practice-briar exceptions=$ex"; if($ex -gt 0){ $ok=$false; $failed+='practice-briar' }
}
if ($ok) { "ALL PASSED" } else { "FAILED: $($failed -join ', ')" }
