param([string]$Report = (Join-Path $PSScriptRoot 'results\csharp-rules.json'))
$ErrorActionPreference = 'Stop'
$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$rules = Join-Path $repo 'unity\Assets\LastBell\Network\Rules'
$sources = @(Get-ChildItem -LiteralPath $rules -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$sources += Join-Path $PSScriptRoot 'SessionChecks.cs'
# A fresh PowerShell process per run avoids Add-Type assembly reuse.
if ('LastBell.Network.Checks.SessionChecks' -as [type]) { throw 'Run in a fresh pwsh process' }
Add-Type -LiteralPath $sources -CompilerOptions '/langversion:7.3','/warnaserror' -ErrorAction Stop
$checks = [LastBell.Network.Checks.SessionChecks]::Run()
$data = [ordered]@{
    evidenceType = 'package-free-csharp-rules'
    unityEditorExecuted = $false
    networkSocketsExecuted = $false
    executedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    powershellVersion = $PSVersionTable.PSVersion.ToString()
    runtime = [System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
    platform = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    architecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    languageVersion = '7.3'
    compiler = ([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'Microsoft.CodeAnalysis.CSharp' } | Select-Object -First 1).GetName().Version.ToString()
    assertions = $checks.Assertions
    passedScenarios = @($checks.PassedScenarios)
    failures = @($checks.Failures)
}
$absoluteReport = [System.IO.Path]::GetFullPath($Report)
New-Item -ItemType Directory -Path (Split-Path $absoluteReport) -Force | Out-Null
$data | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $absoluteReport -Encoding utf8
$data | ConvertTo-Json -Depth 8
if ($checks.Failures.Length -gt 0) { exit 1 }
