param(
    [ValidateSet('Host','Client')][string]$Role,
    [string]$Directory,
    [ValidateRange(1,7)][int]$ExpectedRemotes = 1,
    [int]$Port,
    [string]$Session,
    [string]$Mode = 'play',
    [switch]$InjectChecks,
    [string]$Report
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$sources = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$sources += Join-Path $PSScriptRoot '..\event-recovery\EventWire.cs'
$sources += @(Get-ChildItem -LiteralPath (Join-Path $repo 'unity\Assets\LastBell\Network\Rules') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
Add-Type -LiteralPath $sources -CompilerOptions '/langversion:7.3','/warnaserror' -ErrorAction Stop
if ($Role -eq 'Host') { $result = [LastBell.Network.SocketPrototype.SocketSession]::Host($Directory,$ExpectedRemotes) }
else { $result = [LastBell.Network.SocketPrototype.SocketSession]::Client($Port,[Guid]::Parse($Session),$Mode,$InjectChecks) }
$result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $Report -Encoding utf8
Write-Host ("{0} PID {1}: passed={2}" -f $Role,$PID,$result.Passed)
if (-not $result.Passed) { exit 1 }
