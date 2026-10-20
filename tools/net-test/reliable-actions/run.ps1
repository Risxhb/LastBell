param([string]$Report = (Join-Path $PSScriptRoot 'results\reliable-actions.json'))
$ErrorActionPreference = 'Stop'
$sources = @((Join-Path $PSScriptRoot 'ActionProtocol.cs'), (Join-Path $PSScriptRoot 'ActionChecks.cs'), (Join-Path $PSScriptRoot '..\fault-lab\FaultLab.cs'))
$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$sources += @(Get-ChildItem -LiteralPath (Join-Path $repo 'unity\Assets\LastBell\Network\Rules') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
if ('LastBell.Network.ActionChecks.Runner' -as [type]) { throw 'Use a fresh PowerShell process' }
Add-Type -LiteralPath $sources -CompilerOptions '/langversion:7.3','/warnaserror' -ErrorAction Stop
$first = [LastBell.Network.ActionChecks.Runner]::Run()
$second = [LastBell.Network.ActionChecks.Runner]::Run()
$repeatMatched = ($first | ConvertTo-Json -Depth 10 -Compress) -eq ($second | ConvertTo-Json -Depth 10 -Compress)
$data = [ordered]@{
    evidenceType = 'tools-only-reliable-action-prototype'
    unityEditorExecuted = $false
    networkSocketsExecuted = $false
    combatAbilityActivationValidated = $false
    executedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    powershell = $PSVersionTable.PSVersion.ToString()
    runtime = [System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
    languageVersion = '7.3'
    deterministicRepeatMatched = $repeatMatched
    boundaryChecks = @($first.PassedChecks)
    assertions = $first.Assertions
    cases = @($first.Cases)
    failures = @($first.Failures)
}
$absoluteReport = [System.IO.Path]::GetFullPath($Report)
New-Item -ItemType Directory -Path (Split-Path $absoluteReport) -Force | Out-Null
$data | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $absoluteReport -Encoding utf8
Write-Host ("Checks: " + $first.PassedChecks.Length + "; fault cases: " + $first.Cases.Length + "; failures: " + $first.Failures.Length)
foreach ($failure in $first.Failures) { Write-Host $failure }
if (-not $repeatMatched) { throw 'Seeded repeat did not match' }
if ($first.Failures.Length -gt 0) { exit 1 }
