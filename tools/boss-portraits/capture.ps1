# Captures the boss-step fighter card art (Risx, 4 Nov 2026): launches each boss's practice fight with
# -lastbellPortrait, which frames the boss when the fight starts, saves a PNG and quits. Rebuild afterwards so the
# player picks up the new Resources/Menu/BossPortraits images.
param([Parameter(Mandatory)][string]$Exe,
      [string]$Out="F:\Last Bell\Collab\builds\rebuild-20261102\src\unity\Assets\LastBell\Menu\Resources\Menu\BossPortraits",
      [string[]]$Only=@())
$bosses=[ordered]@{
  'aurelion'='Aurelion'; 'crucivar'='Crucivar'; 'sylvara'='Sylvara'; 'tempo-tyrant'='TempoTyrant'; 'bellkeeper'='Bellkeeper';
  'chimebound-oracle'='ChimeboundOracle'; 'dial-warden'='DialWarden'; 'pendulum-sentinel'='PendulumSentinel';
  'escapement-twins'='EscapementTwins'; 'rivet-cantor'='RivetCantor'; 'crucible-skater'='CrucibleSkater';
  'briar-metronome'='BriarMetronome'; 'silt-carillon'='SiltCarillon'; 'vesper-seedkeeper'='VesperSeedkeeper' }
New-Item -ItemType Directory -Force $Out | Out-Null
$logs=Join-Path $env:TEMP 'lastbell-portraits'; New-Item -ItemType Directory -Force $logs | Out-Null
foreach($k in $bosses.Keys){
  if($Only.Count -and $Only -notcontains $k){continue}
  $png=Join-Path $Out ($bosses[$k]+'.png'); $log=Join-Path $logs "$k.log"
  $p=Start-Process $Exe -ArgumentList "-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -lastbellPractice $k -lastbellPortrait `"$png`" -logFile `"$log`"" -PassThru
  if(-not $p.WaitForExit(120000)){ Stop-Process -Id $p.Id -Force }
  $line=(Select-String -Path $log -Pattern '\[portrait\]' | ForEach-Object { $_.Line }) -join ' | '
  "$k -> $(if(Test-Path $png){'ok'}else{'MISSING'})  $line"
}
