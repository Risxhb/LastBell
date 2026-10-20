param([string]$Report = (Join-Path $PSScriptRoot 'results\verifier-checks.json'))
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$shellPath = Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })
$fixture = Join-Path $PSScriptRoot ('results\verifier fixture ' + [Guid]::NewGuid().ToString('N'))
$root = Join-Path $fixture 'tools\net-test'
New-Item -ItemType Directory -Path $root -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'verify.ps1') -Destination (Join-Path $root 'verify.ps1')
$rules = Join-Path $fixture 'unity\Assets\LastBell\Network\Rules'
New-Item -ItemType Directory -Path $rules -Force | Out-Null
$source = Join-Path $rules 'Fixture.cs'
$stub = @'
param([string]$Report, [int]$MatrixSeconds=600, [switch]$SkipTwoHour)
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$mode = Get-Content -LiteralPath (Join-Path $repo 'mode.txt') -Raw
$module = Split-Path $PSScriptRoot -Leaf
$data = [ordered]@{ unityEditorExecuted=$false; networkSocketsExecuted=$false; failures=@() }
$groups = 0; $cases = 0; $caseFlag = 'Passed'
switch ($module) {
    'unity' { $data.evidenceType='package-free-csharp-rules'; $data.passedScenarios=@(1..14); $data.assertions=1 }
    'fault-lab' { $data.evidenceType='deterministic-in-memory-fault-simulation'; $data.deterministicRepeatMatched=$true; $data.droppedPressRegressionReproduced=$true; $cases=65; $caseFlag='SafetyPassed' }
    'reliable-actions' {
        $data.evidenceType='tools-only-ability-confirmation-wire'; $data.boundaryChecks=@(1..9); $data.receiptWireChecks=@(1..8)
        $data.assertions=1; $data.receiptWireAssertions=1; $data.deterministicRepeatMatched=$true; $data.receiptWireRepeatMatched=$true
        $data.requestWireCodecExercised=$true; $data.confirmationWireCodecExercised=$true; $cases=65
    }
    'endurance' {
        $data.evidenceType='tools-only-network-endurance-and-recovery'; $data.recoveryChecks=@(1..10); $data.recoveryAssertions=1
        $data.deterministicRepeatMatched=$true; $data.recoveryRepeatMatched=$true; $data.includesTwoHourCases=(-not $SkipTwoHour)
        $data.matrixSeconds=$MatrixSeconds; $cases=$(if ($SkipTwoHour) {21} else {23})
    }
    'event-recovery' {
        $data.evidenceType='tools-only-event-wire-and-recovery'; $data.checks=@(1..11); $data.assertions=1
        $data.deterministicRepeatMatched=$true; $data.checkRepeatMatched=$true; $data.eventAckCheckpointWireCodecsExercised=$true
        $data.diagnosticProjectionOnly=$true; $cases=67
    }
}
if ($cases -gt 0) {
    $data.cases=@(for ($i=0; $i -lt $cases; $i++) { $case=[ordered]@{ Failures=@(); EventsCaughtUp=$true; CaughtUp=$true }; $case[$caseFlag]=$true; $case })
}
if ($module -eq 'unity') {
    switch ($mode) {
        'missing-report' { exit 0 }
        'malformed-report' { Set-Content -LiteralPath $Report -Value '{'; exit 0 }
        'reported-failure' { $data.failures=@('injected application failure') }
        'empty-checks' { $data.passedScenarios=@() }
        'missing-scope' { $data.Remove('networkSocketsExecuted') }
        'changed-source' { Add-Content -LiteralPath (Join-Path $repo 'unity\Assets\LastBell\Network\Rules\Fixture.cs') -Value '// injected change' }
    }
}
if ($module -eq 'fault-lab') {
    if ($mode -eq 'missing-repeat') { $data.Remove('deterministicRepeatMatched') }
    if ($mode -eq 'failed-case') { $data.cases[0].SafetyPassed=$false }
}
$data | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Report -Encoding utf8
if ($module -eq 'unity' -and $mode -eq 'nonzero-exit') { exit 7 }
'@
foreach ($entry in @(@('unity','check.ps1'),@('fault-lab','run.ps1'),@('reliable-actions','run.ps1'),@('endurance','run.ps1'),@('event-recovery','run.ps1'))) {
    $folder = Join-Path $root $entry[0]; New-Item -ItemType Directory -Path $folder -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $folder $entry[1]) -Value $stub -Encoding utf8
}
$checks = [Collections.Generic.List[object]]::new()
$failures = [Collections.Generic.List[string]]::new()
$seenRuns = [Collections.Generic.HashSet[string]]::new()
foreach ($mode in @('valid-quick','valid-full','nonzero-exit','reported-failure','missing-report','malformed-report','empty-checks','missing-repeat','failed-case','missing-scope','changed-source')) {
    Set-Content -LiteralPath (Join-Path $fixture 'mode.txt') -Value $mode -NoNewline
    Set-Content -LiteralPath $source -Value '// immutable fixture source'
    $profile = $(if ($mode -eq 'valid-full') {'Full'} else {'Quick'})
    $log = Join-Path $fixture ($mode + '.log')
    & $shellPath -NoProfile -NonInteractive -File (Join-Path $root 'verify.ps1') -Profile $profile *> $log
    $code = $LASTEXITCODE
    $summaryFile = Get-ChildItem -LiteralPath (Join-Path $root 'results\verification') -Recurse -Filter summary.json |
        Where-Object { -not $seenRuns.Contains($_.FullName) } | Select-Object -First 1
    $ok = $false
    if ($summaryFile) {
        [void]$seenRuns.Add($summaryFile.FullName)
        $summary = Get-Content -LiteralPath $summaryFile.FullName -Raw | ConvertFrom-Json
        $expectedPass = $mode -in 'valid-quick','valid-full'
        $ok = ($summary.passed -eq $expectedPass) -and (($code -eq 0) -eq $expectedPass) -and $summary.suites.Count -eq 5
        $ok = $ok -and $summary.fullCoverage -eq ($profile -eq 'Full')
        if ($expectedPass) { $ok = $ok -and @($summary.suites | Where-Object { -not $_.passed }).Count -eq 0 }
        elseif ($mode -eq 'changed-source') { $ok = $ok -and @($summary.failures | Where-Object { $_ -like '*changed during*' }).Count -eq 1 }
        else { $ok = $ok -and $summary.suites[-1].passed -and @($summary.suites | Where-Object { -not $_.passed }).Count -eq 1 }
        if ($mode -eq 'missing-report') { $ok = $ok -and $null -eq $summary.suites[0].reportSha256 }
    }
    $checks.Add([pscustomobject]@{name=$mode;passed=[bool]$ok;exitCode=$code})
    if (-not $ok) { $failures.Add($mode); Get-Content -LiteralPath $log -Tail 5 | ForEach-Object { Write-Host $_ } }
}
$result = [ordered]@{ evidenceType='verification-runner-error-path-checks'; fixtureReportsAreSynthetic=$true; checks=@($checks.ToArray()); failures=@($failures.ToArray()) }
$reportPath = [IO.Path]::GetFullPath($Report)
New-Item -ItemType Directory -Path (Split-Path $reportPath) -Force | Out-Null
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8
Write-Host ("Verifier checks: {0}; failures: {1}" -f $checks.Count,$failures.Count)
if ($failures.Count -gt 0) { exit 1 }
