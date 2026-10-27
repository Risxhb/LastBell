param()
$ErrorActionPreference = 'Stop'
$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$core = Join-Path $repo 'unity\Assets\LastBell\Core\ClassTest'
$sources = @(Get-ChildItem -LiteralPath $core -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$sources += Join-Path $PSScriptRoot 'ClassTestChecks.cs'
# A fresh PowerShell process per run avoids Add-Type assembly reuse.
if ('LastBell.ClassTest.Checks.ClassTestChecks' -as [type]) { throw 'Run in a fresh PowerShell process' }
Add-Type -LiteralPath $sources -ErrorAction Stop
$checks = [LastBell.ClassTest.Checks.ClassTestChecks]::Run()
[ordered]@{
    evidenceType = 'package-free-csharp-class-test'
    unityEditorExecuted = $false
    assertions = $checks.Assertions
    passedScenarios = @($checks.PassedScenarios)
    failures = @($checks.Failures)
} | ConvertTo-Json -Depth 4
if ($checks.Failures.Length -gt 0) { exit 1 }
