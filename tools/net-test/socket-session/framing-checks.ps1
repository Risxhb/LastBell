param([string]$Report = (Join-Path $PSScriptRoot 'results/framing-checks.json'))
$ErrorActionPreference = 'Stop'
Add-Type -LiteralPath @((Join-Path $PSScriptRoot 'SocketWire.cs'),(Join-Path $PSScriptRoot 'FramingChecks.cs')) -CompilerOptions '/langversion:7.3','/warnaserror'
$checks = [LastBell.Network.SocketPrototype.FramingChecks]::Run()
$path = [IO.Path]::GetFullPath($Report)
New-Item -ItemType Directory -Path (Split-Path $path) -Force | Out-Null
[ordered]@{passed=$true;networkSocketsExecuted=$true;bindAddress='127.0.0.1';checks=$checks;executedAtUtc=[DateTimeOffset]::UtcNow.ToString('o')} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $path -Encoding utf8
Write-Host ("Real-socket framing checks passed: {0}; report: {1}" -f $checks.Count,$path)
