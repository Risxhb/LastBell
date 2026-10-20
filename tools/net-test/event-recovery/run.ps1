param([string]$Report = (Join-Path $PSScriptRoot 'results\event-recovery.json'))
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$sources = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$sources += Join-Path $PSScriptRoot '..\fault-lab\FaultLab.cs'
$sources += @(Get-ChildItem -LiteralPath (Join-Path $repo 'unity\Assets\LastBell\Network\Rules') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
if ('LastBell.Network.EventRecovery.EventLab' -as [type]) { throw 'Use a fresh PowerShell process' }
Add-Type -LiteralPath $sources -CompilerOptions '/langversion:7.3','/warnaserror' -ErrorAction Stop
$checks = [LastBell.Network.EventRecovery.EventChecks]::Run()
$checksRepeat = [LastBell.Network.EventRecovery.EventChecks]::Run()
$first = [LastBell.Network.EventRecovery.EventLab]::Run()
$second = [LastBell.Network.EventRecovery.EventLab]::Run()
$matched = ($first | ConvertTo-Json -Depth 10 -Compress) -eq ($second | ConvertTo-Json -Depth 10 -Compress)
$checksMatched = ($checks | ConvertTo-Json -Depth 10 -Compress) -eq ($checksRepeat | ConvertTo-Json -Depth 10 -Compress)
$failures = @($checks.Failures)
foreach ($case in $first) { foreach ($failure in $case.Failures) { $failures += ("{0}/{1} peers/RTT {2}/seed {3}: {4}" -f $case.Name,$case.Participants,$case.RttMs,$case.Seed,$failure) } }
if (-not $matched -or -not $checksMatched) { $failures += 'Seeded repeat did not match' }
$totals = [ordered]@{}
foreach ($name in @('EventsIssued','FeedbackIssued','AppliedRecords','FeedbackEffects','FeedbackSuppressed','CheckpointsInstalled','DuplicateEffects','ProjectionConflicts','Reconnects','GenerationResets','Sent','Dropped','Duplicated','Delivered','AckBlackoutDrops','StaleFramesRejected')) {
    $totals[$name] = ($first | Measure-Object $name -Sum).Sum
}
$data = [ordered]@{
    evidenceType = 'tools-only-event-wire-and-recovery'
    virtualTimeSimulation = $true
    unityEditorExecuted = $false
    networkSocketsExecuted = $false
    combatCheckpointRestoreValidated = $false
    diagnosticProjectionOnly = $true
    participantMeaning = 'subscriber projections around one journal; not launched Unity players/processes'
    eventAckCheckpointWireCodecsExercised = $true
    executedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    powershell = $PSVersionTable.PSVersion.ToString()
    runtime = [Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
    languageVersion = '7.3'
    deterministicRepeatMatched = $matched
    checkRepeatMatched = $checksMatched
    checks = @($checks.Checks)
    assertions = $checks.Assertions
    totalsPerPass = $totals
    cases = @($first)
    failures = $failures
}
$absoluteReport = [IO.Path]::GetFullPath($Report)
New-Item -ItemType Directory -Path (Split-Path $absoluteReport) -Force | Out-Null
$data | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $absoluteReport -Encoding utf8
Write-Host ("Checks: {0}; assertions: {1}; cases: {2}; repeat: {3}; failures: {4}" -f $checks.Checks.Length,$checks.Assertions,$first.Length,$matched,$failures.Count)
foreach ($failure in $failures) { Write-Host $failure }
if ($failures.Count -gt 0) { exit 1 }
