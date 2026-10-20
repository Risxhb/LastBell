param(
    [ValidateSet('Full','Quick')][string]$Profile = 'Full',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'results\verification')
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 is required' }
$shellPath = Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$runId = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
$runDirectory = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) $runId
New-Item -ItemType Directory -Path $runDirectory | Out-Null
$suites = @(
    @{ Name='session-rules'; Script='unity/check.ps1'; Evidence='package-free-csharp-rules'; Groups=@('passedScenarios'); GroupCount=14; Assertions=@('assertions'); Repeat=@(); Cases=0 },
    @{ Name='fault-lab'; Script='fault-lab/run.ps1'; Evidence='deterministic-in-memory-fault-simulation'; Groups=@(); GroupCount=0; Assertions=@(); Repeat=@('deterministicRepeatMatched'); Cases=65; CaseFlag='SafetyPassed'; CaseExtra=@('EventsCaughtUp') },
    @{ Name='reliable-actions'; Script='reliable-actions/run.ps1'; Evidence='tools-only-ability-confirmation-wire'; Groups=@('boundaryChecks','receiptWireChecks'); GroupCount=17; Assertions=@('assertions','receiptWireAssertions'); Repeat=@('deterministicRepeatMatched','receiptWireRepeatMatched'); Cases=65; CaseFlag='Passed' },
    @{ Name='endurance'; Script='endurance/run.ps1'; Evidence='tools-only-network-endurance-and-recovery'; Groups=@('recoveryChecks'); GroupCount=10; Assertions=@('recoveryAssertions'); Repeat=@('deterministicRepeatMatched','recoveryRepeatMatched'); Cases=$(if ($Profile -eq 'Full') {23} else {21}); CaseFlag='Passed' },
    @{ Name='event-recovery'; Script='event-recovery/run.ps1'; Evidence='tools-only-event-wire-and-recovery'; Groups=@('checks'); GroupCount=11; Assertions=@('assertions'); Repeat=@('deterministicRepeatMatched','checkRepeatMatched'); Cases=67; CaseFlag='Passed'; CaseExtra=@('CaughtUp') }
)
function Get-SourceManifest {
    $files = @(Get-Item -LiteralPath $PSCommandPath)
    $rules = Join-Path $repo 'unity/Assets/LastBell/Network/Rules'
    $files += @(Get-ChildItem -LiteralPath $rules -Filter '*.cs')
    foreach ($suite in $suites) {
        $folder = Split-Path (Join-Path $PSScriptRoot $suite.Script)
        $files += @(Get-ChildItem -LiteralPath $folder -File | Where-Object { $_.Extension -in '.cs','.ps1' })
    }
    $manifest = [ordered]@{}
    foreach ($file in ($files | Sort-Object FullName -Unique)) {
        $relative = [IO.Path]::GetRelativePath($repo,$file.FullName).Replace('\','/')
        $manifest[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    return $manifest
}
function Require-Flag($Data, [string]$Name, [bool]$Expected) {
    $property = $Data.PSObject.Properties[$Name]
    if ($null -eq $property -or $property.Value -isnot [bool] -or $property.Value -ne $Expected) {
        throw "Invalid or missing flag: $Name (expected $Expected)"
    }
}
function Require-Array($Data, [string]$Name) {
    $property = $Data.PSObject.Properties[$Name]
    if ($null -eq $property -or $property.Value -isnot [Array]) { throw "Invalid or missing array: $Name" }
    # Keep empty arrays intact rather than letting PowerShell's pipeline unwrap them.
    return ,$property.Value
}
function Read-SuiteReport($Definition, [string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw 'Child did not produce a report' }
    $data = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -Depth 30
    if ($null -eq $data -or $data.evidenceType -ne $Definition.Evidence) { throw 'Unexpected evidence type' }
    Require-Flag $data 'unityEditorExecuted' $false
    Require-Flag $data 'networkSocketsExecuted' $false
    if ((Require-Array $data 'failures').Count -ne 0) { throw 'Child report contains failures' }
    foreach ($flag in $Definition.Repeat) { Require-Flag $data $flag $true }
    $groupCount = 0
    foreach ($name in $Definition.Groups) { $groupCount += (Require-Array $data $name).Count }
    if ($groupCount -ne $Definition.GroupCount) { throw "Unexpected check-group count: $groupCount" }
    $assertionCount = 0L
    foreach ($name in $Definition.Assertions) {
        $value = $data.PSObject.Properties[$name].Value
        if (($value -isnot [int] -and $value -isnot [long]) -or $value -le 0) { throw "Invalid assertion count: $name" }
        $assertionCount += $value
    }
    if ($Definition.Cases -gt 0) {
        $cases = Require-Array $data 'cases'
        if ($cases.Count -ne $Definition.Cases) { throw "Unexpected case count: $($cases.Count)" }
        foreach ($case in $cases) {
            Require-Flag $case $Definition.CaseFlag $true
            foreach ($flag in $Definition.CaseExtra) { Require-Flag $case $flag $true }
            if ((Require-Array $case 'Failures').Count -ne 0) { throw 'A case contains failures' }
        }
    }
    switch ($Definition.Name) {
        'fault-lab' { Require-Flag $data 'droppedPressRegressionReproduced' $true }
        'reliable-actions' {
            Require-Flag $data 'requestWireCodecExercised' $true
            Require-Flag $data 'confirmationWireCodecExercised' $true
        }
        'endurance' {
            Require-Flag $data 'includesTwoHourCases' ($Profile -eq 'Full')
            if ($data.matrixSeconds -ne $(if ($Profile -eq 'Full') {600} else {120})) { throw 'Endurance profile mismatch' }
        }
        'event-recovery' {
            Require-Flag $data 'eventAckCheckpointWireCodecsExercised' $true
            Require-Flag $data 'diagnosticProjectionOnly' $true
        }
    }
    return [pscustomobject]@{ CheckGroups=$groupCount; Assertions=$assertionCount; Cases=$Definition.Cases }
}
$sourceManifest = Get-SourceManifest
$rows = [Collections.Generic.List[object]]::new()
$failures = [Collections.Generic.List[string]]::new()
foreach ($suite in $suites) {
    $reportFile = $suite.Name + '.json'; $logFile = $suite.Name + '.log'
    $reportPath = Join-Path $runDirectory $reportFile; $logPath = Join-Path $runDirectory $logFile
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $exitCode = $null; $validation = $null; $failure = $null
    try {
        $arguments = @('-NoLogo','-NoProfile','-NonInteractive','-File',(Join-Path $PSScriptRoot $suite.Script),'-Report',$reportPath)
        if ($suite.Name -eq 'endurance' -and $Profile -eq 'Quick') { $arguments += @('-MatrixSeconds','120','-SkipTwoHour') }
        & $shellPath @arguments *> $logPath
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) { throw "Child exited with code $exitCode" }
        $validation = Read-SuiteReport $suite $reportPath
    }
    catch { $failure = $_.Exception.Message; $failures.Add($suite.Name + ': ' + $failure) }
    $timer.Stop()
    $row = [ordered]@{
        name=$suite.Name; passed=($null -eq $failure); exitCode=$exitCode
        checkGroups=$(if ($validation) {$validation.CheckGroups} else {$null})
        assertions=$(if ($validation) {$validation.Assertions} else {$null})
        cases=$(if ($validation) {$validation.Cases} else {$null})
        report=$reportFile; log=$logFile; seconds=[Math]::Round($timer.Elapsed.TotalSeconds,3)
        reportSha256=$(if (Test-Path -LiteralPath $reportPath -PathType Leaf) {(Get-FileHash -LiteralPath $reportPath -Algorithm SHA256).Hash.ToLowerInvariant()} else {$null})
        failure=$failure
    }
    $rows.Add([pscustomobject]$row)
    Write-Host ("{0}: {1}" -f $suite.Name,$(if ($row.passed) {'PASS'} else {'FAIL - ' + $failure}))
    if ($failure -and (Test-Path -LiteralPath $logPath)) { Get-Content -LiteralPath $logPath -Tail 6 | ForEach-Object { Write-Host $_ } }
}
try {
    $after = Get-SourceManifest
    if (($sourceManifest | ConvertTo-Json -Compress) -ne ($after | ConvertTo-Json -Compress)) { throw 'Source files changed during verification' }
}
catch { $failures.Add($_.Exception.Message) }
$summary = [ordered]@{
    evidenceType='standalone-network-verification'; runId=$runId; executedAtUtc=[DateTimeOffset]::UtcNow.ToString('o')
    profile=$Profile; fullCoverage=($Profile -eq 'Full'); passed=($failures.Count -eq 0)
    unityEditorExecuted=$false; networkSocketsExecuted=$false; combatIntegrationValidated=$false
    powershell=$PSVersionTable.PSVersion.ToString(); runtime=[Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
    sourceSha256=$sourceManifest; suites=@($rows.ToArray())
    totals=[ordered]@{checkGroups=($rows | Measure-Object checkGroups -Sum).Sum; assertions=($rows | Measure-Object assertions -Sum).Sum; cases=($rows | Measure-Object cases -Sum).Sum}
    preservedFindings=@('Processed movement acknowledgments do not guarantee earlier unreliable action delivery.',
        'Timeout/abandonment does not prove non-execution; same-generation host restart can replay consumption.',
        'Event checkpoints restore only a diagnostic projection here; Unity combat snapshot and authenticated transport contracts remain open.')
    failures=@($failures.ToArray())
}
$summaryPath = Join-Path $runDirectory 'summary.json'
$summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $summaryPath -Encoding utf8
Write-Host ("Profile: {0}; suites passed: {1}/{2}; summary: {3}" -f $Profile,@($rows | Where-Object passed).Count,$suites.Count,$summaryPath)
if (-not $summary.passed) { exit 1 }
