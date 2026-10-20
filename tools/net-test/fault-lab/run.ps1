param([string]$Report = (Join-Path $PSScriptRoot 'results\fault-lab.json'))
$ErrorActionPreference = 'Stop'
$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$sources = @(Get-ChildItem -LiteralPath (Join-Path $repo 'unity\Assets\LastBell\Network\Rules') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$sources += Join-Path $PSScriptRoot 'FaultLab.cs'
if ('LastBell.Network.FaultLab.Runner' -as [type]) { throw 'Run in a fresh PowerShell process' }
Add-Type -LiteralPath $sources -CompilerOptions '/langversion:7.3','/warnaserror' -ErrorAction Stop
$reportData = [LastBell.Network.FaultLab.Runner]::Run()
$repeat = [LastBell.Network.FaultLab.Runner]::Run()
$deterministic = ($reportData | ConvertTo-Json -Depth 10 -Compress) -eq ($repeat | ConvertTo-Json -Depth 10 -Compress)
$data = [ordered]@{
    evidenceType = 'deterministic-in-memory-fault-simulation'
    unityEditorExecuted = $false
    networkSocketsExecuted = $false
    productionTransportValidated = $false
    executedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    powershell = $PSVersionTable.PSVersion.ToString()
    runtime = [System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
    languageVersion = '7.3'
    matrixCases = 42
    orderedBaselineCases = 21
    deterministicRepeatMatched = $deterministic
    diagnosticCases = 2
    cases = @($reportData.Cases)
    droppedPressRegressionReproduced = $reportData.DroppedPressRegressionReproduced
    failures = @($reportData.Failures)
}
$absoluteReport = [System.IO.Path]::GetFullPath($Report)
New-Item -ItemType Directory -Path (Split-Path $absoluteReport) -Force | Out-Null
$data | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $absoluteReport -Encoding utf8
Write-Host ("Matrix: 42 cases; ordered controls: 21; probes: 2; failures: " + $reportData.Failures.Length)
Write-Host ("Dropped-press regression reproduced: " + $reportData.DroppedPressRegressionReproduced)
foreach ($failure in $reportData.Failures) { Write-Host $failure }
if (-not $deterministic) { throw "Seeded repeat produced different metrics" }
if ($reportData.Failures.Length -gt 0) { exit 1 }
