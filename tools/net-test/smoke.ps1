param(
    [Parameter(Mandatory=$true)][string]$Godot,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'results'),
    [int]$BasePort = 27841
)
$ErrorActionPreference = 'Stop'
$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$game = Join-Path $repo 'game'
$runner = Join-Path $PSScriptRoot 'peer.gd'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
# Godot data/cache stays in the test output folder.
$priorAppData = $env:APPDATA
$env:APPDATA = Join-Path $OutputDirectory 'userdata'
New-Item -ItemType Directory -Path $env:APPDATA -Force | Out-Null
$summary = @()
try {
    $cases = @(
        @{name='two'; count=2; scenario='smoke'},
        @{name='eight'; count=8; scenario='smoke'},
        @{name='ninth'; count=8; scenario='capacity'},
        @{name='disconnect'; count=2; scenario='disconnect'},
        @{name='host-loss'; count=2; scenario='host-loss'},
        @{name='crash-client'; count=2; scenario='crash-client'},
        @{name='crash-host'; count=2; scenario='crash-host'}
    )
    $caseIndex = 0
    foreach ($case in $cases) {
        $port = $BasePort + $caseIndex
        $caseIndex++
        $processes = @()
        $reports = @()
        $caseDir = Join-Path $OutputDirectory $case.name
        New-Item -ItemType Directory -Path $caseDir -Force | Out-Null
        function Start-TestPeer([string]$Role, [int]$Index) {
            $report = Join-Path $caseDir "$Index-$Role.json"
            foreach ($stale in @($report, ($report + '.crash-ready'))) {
                if (Test-Path -LiteralPath $stale) { Remove-Item -LiteralPath $stale -Force }
            }
            $stdout = Join-Path $caseDir "$Index-$Role.stdout.log"
            $stderr = Join-Path $caseDir "$Index-$Role.stderr.log"
            $arguments = @('--headless', '--path', ('"{0}"' -f $game), '--script', ('"{0}"' -f $runner), '--', '--role', $Role, '--scenario', $case.scenario, '--expected', $case.count, '--port', $port, '--report', ('"{0}"' -f $report))
            $process = Start-Process -FilePath $Godot -ArgumentList $arguments -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
            return @{process=$process; report=$report; stderr=$stderr; stdout=$stdout}
        }
        try {
            $processes += Start-TestPeer 'host' 0
            Start-Sleep -Milliseconds 600
            for ($i = 1; $i -lt $case.count; $i++) {
                $processes += Start-TestPeer 'client' $i
                Start-Sleep -Milliseconds 100
            }
            if ($case.scenario -eq 'capacity') {
                Start-Sleep -Milliseconds 300
                $processes += Start-TestPeer 'overflow' 8
            }
            if ($case.scenario -in @('crash-host','crash-client')) {
                $crashIndex = if ($case.scenario -eq 'crash-host') { 0 } else { 1 }
                $crash = $processes[$crashIndex]
                $deadline = [DateTime]::UtcNow.AddSeconds(10)
                while (-not (Test-Path -LiteralPath ($crash.report + '.crash-ready'))) {
                    if ([DateTime]::UtcNow -gt $deadline) { throw 'Crash peer never reached running tick 90' }
                    Start-Sleep -Milliseconds 50
                }
                Stop-Process -Id $crash.process.Id -Force
                $crash.process.WaitForExit()
                $reports += @{role='forced-termination'; passed=$true; reason=$case.scenario}
            }
            foreach ($entry in $processes) {
                if ($case.scenario -in @('crash-host','crash-client') -and $entry -eq $crash) { continue }
                if (-not $entry.process.WaitForExit(15000)) { throw "Timeout in $($case.name)" }
                $entry.process.Refresh()
                if (-not (Test-Path -LiteralPath $entry.report)) {
                    Get-Content -LiteralPath $entry.stderr
                    Get-Content -LiteralPath $entry.stdout
                    throw "Missing report: $($entry.report)"
                }
                $data = Get-Content -LiteralPath $entry.report -Raw | ConvertFrom-Json
                $reports += $data
                $errors = Get-Content -LiteralPath $entry.stderr -Raw
                if ($entry.process.ExitCode -ne 0 -or -not $data.passed -or $errors -match 'SCRIPT ERROR|Parse Error|ERROR:') {
                    Get-Content -LiteralPath $entry.stdout
                    if ($errors) { Write-Host $errors }
                    throw "Failed peer in $($case.name): $($entry.report)"
                }
            }
            $summary += @{case=$case.name; passed=$true; peers=$reports}
            Write-Host "PASS $($case.name): $($reports.Count) processes"
        } finally {
            foreach ($entry in $processes) {
                if (-not $entry.process.HasExited) { Stop-Process -Id $entry.process.Id -Force }
            }
        }
    }
    $summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'summary.json') -Encoding utf8
} finally {
    $env:APPDATA = $priorAppData
}
