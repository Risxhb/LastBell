param([string]$Query = 'clean&view=game', [string]$Name = 'game', [int]$Width = 1920, [int]$Height = 1080, [string]$Page = 'shattered-hour.html')
# Renders the Area 4 mockup to shots\<Name>.png with headless Edge (WebGL via ANGLE). Example:
#   powershell -NoProfile -ExecutionPolicy Bypass -File capture.ps1 -Query "clean&view=cine" -Name cine
$ErrorActionPreference = 'Stop'
$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge not found' }
$shots = Join-Path $PSScriptRoot 'shots'; New-Item -ItemType Directory -Force $shots | Out-Null
$out = Join-Path $shots "$Name.png"; if (Test-Path $out) { Remove-Item $out }
$url = ([System.Uri](Join-Path $PSScriptRoot $Page)).AbsoluteUri + '?' + $Query
$profile = Join-Path $env:TEMP 'lb-area4-capture-profile'
$argLine = "--headless=new --hide-scrollbars --use-angle=d3d11 --enable-unsafe-swiftshader --allow-file-access-from-files --user-data-dir=`"$profile`" --window-size=$Width,$Height --virtual-time-budget=12000 --screenshot=`"$out`" `"$url`""
$p = Start-Process -FilePath $edge -ArgumentList $argLine -PassThru -Wait -WindowStyle Hidden
if (-not (Test-Path $out)) { throw "No screenshot written (exit $($p.ExitCode))" }
Get-Item $out | Select-Object Name, Length
