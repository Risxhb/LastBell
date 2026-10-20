param([string]$Query = 'clean', [string]$Name = 'hd-establishing', [int]$Width = 1920, [int]$Height = 1080, [string]$Page = 'mockup.html')
# Renders a prototype page to a PNG with headless Edge (WebGL via ANGLE). Example:
#   powershell -NoProfile -ExecutionPolicy Bypass -File capture.ps1 -Query "clean&view=gameplay&tele" -Name hd-gameplay
$ErrorActionPreference = 'Stop'
$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge not found' }
$shots = Join-Path $PSScriptRoot 'shots'; New-Item -ItemType Directory -Force $shots | Out-Null
$out = Join-Path $shots "$Name.png"
$url = ([System.Uri](Join-Path $PSScriptRoot $Page)).AbsoluteUri + '?' + $Query
$profile = Join-Path $env:TEMP 'lb-edge-capture-profile'
$argLine = "--headless=new --hide-scrollbars --use-angle=d3d11 --enable-unsafe-swiftshader --user-data-dir=`"$profile`" --window-size=$Width,$Height --virtual-time-budget=9000 --screenshot=`"$out`" `"$url`""
$p = Start-Process -FilePath $edge -ArgumentList $argLine -PassThru -Wait -WindowStyle Hidden
if (-not (Test-Path $out)) { throw "No screenshot written (exit $($p.ExitCode))" }
Get-Item $out | Select-Object Name, Length
