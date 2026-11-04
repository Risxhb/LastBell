param(
    [Parameter(Mandatory=$true)][string]$Player,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateSet('Bellkeeper','Aurelion','Crucivar','Sylvara','Tempo Tyrant','Chimebound Oracle','Pendulum Sentinel','Dial Warden','Escapement Twins','Rivet Cantor','Crucible Skater','Briar Metronome','Silt Carillon','Vesper Seedkeeper')][string]$Boss='Bellkeeper',
    [ValidateSet('test','skyward-spire','brass-foundry','verdant-chimes','shattered-hour','metronome-court')][string]$Arena='test',
    [ValidateRange(1,3)][int]$PeerCount=1,
    [ValidateSet('Tollbreaker','Chimesinger','Arcanist')][string]$ClientClass='Arcanist'
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Player -PathType Leaf)) { throw "Player not found: $Player" }
if (Test-Path -LiteralPath $OutputDirectory) { throw "Output already exists: $OutputDirectory" }
$hostOut = Join-Path $OutputDirectory 'host'
New-Item -ItemType Directory -Path $hostOut | Out-Null
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop()
$hostLog = Join-Path $hostOut 'player.log'
$hostProc = $null; $clients = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()
$clientOutputs = [System.Collections.Generic.List[string]]::new()
try {
    $common = @('-timestamps','-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-lastbellLanPort',"$port",'-lastbellLanClientClass',$ClientClass)
    $hostArgs = $common + @('-lastbellProfile','Host','-lastbellLanSmokeHost','-lastbellLanPeerCount',"$PeerCount",'-lastbellLanBoss',('"'+$Boss+'"'),'-lastbellLanArena',$Arena,'-lastbellOut',('"'+$hostOut+'"'),'-logFile',('"'+$hostLog+'"'))
    $hostProc = Start-Process -FilePath $Player -ArgumentList $hostArgs -WindowStyle Hidden -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(75)
    $ready = $false
    while ([DateTime]::UtcNow -lt $deadline -and -not $hostProc.HasExited) {
        if ((Test-Path -LiteralPath $hostLog) -and (Select-String -LiteralPath $hostLog -Pattern '\[LAN smoke\] HOST READY' -Quiet)) { $ready = $true; break }
        Start-Sleep -Seconds 1; $hostProc.Refresh()
    }
    if (-not $ready) { throw 'Host did not become ready.' }
    for ($i=1; $i -le $PeerCount; $i++) {
        $name = if ($PeerCount -eq 1) { 'client' } else { "client-$i" }
        $clientOut = Join-Path $OutputDirectory $name
        New-Item -ItemType Directory -Path $clientOut | Out-Null
        $clientOutputs.Add($clientOut)
        $clientLog = Join-Path $clientOut 'player.log'
        $clientArgs = $common + @('-lastbellProfile',"Client-$i",'-lastbellLanSmokeClient','-lastbellOut',('"'+$clientOut+'"'),'-logFile',('"'+$clientLog+'"'))
        $clients.Add((Start-Process -FilePath $Player -ArgumentList $clientArgs -WindowStyle Hidden -PassThru))
        if ($PeerCount -gt 1) { Start-Sleep -Milliseconds 500 }
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(330)
    while ([DateTime]::UtcNow -lt $deadline) {
        $hostProc.Refresh(); foreach ($client in $clients) { $client.Refresh() }
        if ($hostProc.HasExited -and @($clients | Where-Object { -not $_.HasExited }).Count -eq 0) { break }
        Start-Sleep -Seconds 1
    }
    $hostProc.Refresh(); foreach ($client in $clients) { $client.Refresh() }
    if (-not $hostProc.HasExited -or @($clients | Where-Object { -not $_.HasExited }).Count -gt 0) { throw 'Multi-process test timed out.' }
    $reports = @((Join-Path $hostOut 'lan-host-test.txt')) + @($clientOutputs | ForEach-Object { Join-Path $_ 'lan-client-test.txt' })
    foreach ($path in $reports) {
        if (-not (Test-Path -LiteralPath $path)) { throw "Missing test report: $path" }
        Get-Content -LiteralPath $path
    }
    if ($hostProc.ExitCode -ne 0 -or @($clients | Where-Object { $_.ExitCode -ne 0 }).Count -gt 0) { throw "One or more player processes failed; see reports in $OutputDirectory" }
} finally {
    foreach ($p in (@($hostProc) + @($clients))) { if ($null -ne $p) { $p.Refresh(); if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force } } }
}
