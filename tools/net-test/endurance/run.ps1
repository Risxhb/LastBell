param(
    [string]$Report = (Join-Path $PSScriptRoot 'results\endurance.json'),
    [ValidateRange(120,86400)][int]$MatrixSeconds = 600,
    [switch]$SkipTwoHour
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$sources = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$sources += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '..\reliable-actions') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$sources += Join-Path $PSScriptRoot '..\fault-lab\FaultLab.cs'
$sources += @(Get-ChildItem -LiteralPath (Join-Path $repo 'unity\Assets\LastBell\Network\Rules') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
if ('LastBell.Network.Endurance.EnduranceRunner' -as [type]) { throw 'Use a fresh PowerShell process' }
Add-Type -LiteralPath $sources -CompilerOptions '/langversion:7.3','/warnaserror' -ErrorAction Stop
$timer = [Diagnostics.Stopwatch]::StartNew()
$checks = [LastBell.Network.Endurance.RecoveryChecks]::Run()
$checksRepeat = [LastBell.Network.Endurance.RecoveryChecks]::Run()
Write-Host ("Recovery groups: {0}; assertions: {1}; failures: {2}" -f $checks.Checks.Length,$checks.Assertions,$checks.Failures.Length)
$first = [LastBell.Network.Endurance.EnduranceRunner]::Run($MatrixSeconds, -not $SkipTwoHour)
Write-Host ("First pass: {0} cases; failures: {1}" -f $first.Length,@($first | Where-Object { -not $_.Passed }).Count)
$second = [LastBell.Network.Endurance.EnduranceRunner]::Run($MatrixSeconds, -not $SkipTwoHour)
$timer.Stop()
$repeatMatched = ($first | ConvertTo-Json -Depth 12 -Compress) -eq ($second | ConvertTo-Json -Depth 12 -Compress)
$checksMatched = ($checks | ConvertTo-Json -Depth 12 -Compress) -eq ($checksRepeat | ConvertTo-Json -Depth 12 -Compress)
$failures = @($checks.Failures)
foreach ($case in $first) {
    foreach ($failure in $case.Failures) { $failures += ("{0}/{1} peers/seed {2}: {3}" -f $case.Name,$case.Participants,$case.Seed,$failure) }
}
if (-not $repeatMatched -or -not $checksMatched) { $failures += 'Seeded repeat did not match' }
$totals = [ordered]@{}
foreach ($name in @('Generated','Consumed','ConfirmedConsumed','Expired','Unknown','Rejected','Cancelled','Abandoned','ConsumedWithoutConfirmation','UnknownActuallyConsumed','AbandonedActuallyConsumed','Reconnects','Resets','RecoveryWindows','RecoveredWindows','Sent','Delivered','Dropped','Duplicated','DuplicateConsumptions','LateConsumptions','InactiveConsumptions','ConflictingOutcomes')) {
    $totals[$name] = ($first | Measure-Object $name -Sum).Sum
}
$maxima = [ordered]@{}
foreach ($name in @('MaxRecoveryMs','PeakClientPending','PeakClientResults','PeakHostTickets','PeakPacketQueue','PeakClockSamples','PeakClockProbes','MaxClockErrorMs','MaxClockUncertaintyMs','MaxPendingAfterDrain','PacketsAfterDrain')) {
    $maxima[$name] = ($first | Measure-Object $name -Maximum).Maximum
}
$data = [ordered]@{
    evidenceType = 'tools-only-network-endurance-and-recovery'
    virtualTimeSimulation = $true
    unityEditorExecuted = $false
    networkSocketsExecuted = $false
    wallClockSoakExecuted = $false
    combatAbilityActivationValidated = $false
    requestAndConfirmationCodecsExercised = $true
    clockProbeTransport = 'fixture metadata; no production clock wire/authentication implementation'
    protocolMemoryMetric = 'resident collection sizes, not managed heap or Unity allocation performance'
    auditLedgerRetainsEntireRun = $true
    executedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    powershell = $PSVersionTable.PSVersion.ToString()
    runtime = [Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
    languageVersion = '7.3'
    wallSeconds = [Math]::Round($timer.Elapsed.TotalSeconds,3)
    matrixSeconds = $MatrixSeconds
    includesTwoHourCases = -not $SkipTwoHour
    virtualSecondsPerPass = ($first | Measure-Object VirtualSeconds -Sum).Sum
    deterministicRepeatMatched = $repeatMatched
    recoveryRepeatMatched = $checksMatched
    recoveryChecks = @($checks.Checks)
    recoveryAssertions = $checks.Assertions
    totalsPerPass = $totals
    maximaPerPass = $maxima
    cases = @($first)
    failures = $failures
}
$absoluteReport = [IO.Path]::GetFullPath($Report)
New-Item -ItemType Directory -Path (Split-Path $absoluteReport) -Force | Out-Null
$data | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $absoluteReport -Encoding utf8
Write-Host ("Cases: {0}; virtual hours per pass: {1}; repeat matched: {2}; elapsed seconds: {3}; failures: {4}" -f $first.Length,($data.virtualSecondsPerPass/3600),$repeatMatched,$data.wallSeconds,$failures.Count)
foreach ($failure in $failures) { Write-Host $failure }
if ($failures.Count -gt 0) { exit 1 }
