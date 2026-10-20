param(
    [ValidateSet('All','Two','Eight','HostLoss')][string]$Scenario = 'All',
    [ValidateRange(1,5)][int]$Repeat = 2,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'results')
)
$ErrorActionPreference = 'Stop'
$shellPath = Join-Path $PSHOME $(if ($IsWindows) {'pwsh.exe'} else {'pwsh'})
$runId = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
$runDirectory = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) $runId
New-Item -ItemType Directory -Path $runDirectory | Out-Null
function Start-Worker([string]$Name,[string[]]$WorkerArguments) {
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $shellPath; $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    foreach ($argument in @('-NoLogo','-NoProfile','-NonInteractive','-File',(Join-Path $PSScriptRoot 'worker.ps1')) + $WorkerArguments) {
        $info.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::new(); $process.StartInfo = $info; [void]$process.Start()
    $worker = [pscustomobject]@{Name=$Name;Process=$process;Stdout=$process.StandardOutput.ReadToEndAsync();Stderr=$process.StandardError.ReadToEndAsync();Killed=$false}
    $owned.Add($worker); return $worker
}
function Wait-For([scriptblock]$Condition,[int]$Seconds,[string]$Label) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    while (-not (& $Condition)) {
        if ([DateTime]::UtcNow -gt $deadline) { throw "Timeout: $Label" }
        Start-Sleep -Milliseconds 50
    }
}
function Read-Worker([string]$Name,$Worker,[bool]$AllowKilled=$false) {
    Wait-For { $Worker.Process.HasExited } 15 $Name
    if ($AllowKilled -and $Worker.Killed) { return $null }
    $path = Join-Path $caseDirectory ($Name + '.json')
    if ($Worker.Process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $path)) { throw "Worker $Name failed; see its log" }
    $data = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if (-not $data.Passed -or $data.Failures.Count -ne 0 -or $data.Pid -ne $Worker.Process.Id) { throw "Worker $Name report failed" }
    return $data
}
$cases = [Collections.Generic.List[object]]::new()
$scenarios = $(if ($Scenario -eq 'All') {@('Two','Eight','HostLoss')} else {@($Scenario)})
foreach ($pass in 1..$Repeat) {
    foreach ($name in $scenarios) {
        $caseName = $name.ToLowerInvariant() + '-' + $pass
        $caseDirectory = Join-Path $runDirectory $caseName; New-Item -ItemType Directory -Path $caseDirectory | Out-Null
        $owned = [Collections.Generic.List[object]]::new(); $errors = [Collections.Generic.List[string]]::new()
        $remotes = $(if ($name -eq 'Eight') {7} else {1})
        $hostWorker = $null; $hostReport = $null; $reports = @(); $probeReports = @(); $playerPids = @(); $portNotListening = $false
        try {
            $hostWorker = Start-Worker 'host' @('-Role','Host','-Directory',$caseDirectory,'-ExpectedRemotes',[string]$remotes,'-Report',(Join-Path $caseDirectory 'host.json'))
            $endpointPath = Join-Path $caseDirectory 'endpoint.txt'
            Wait-For { (Test-Path -LiteralPath $endpointPath) -or $hostWorker.Process.HasExited } 15 'host endpoint'
            if ($hostWorker.Process.HasExited) { throw 'Host exited before publishing endpoint' }
            $endpoint = Get-Content -LiteralPath $endpointPath; $port = [int]$endpoint[0]; $sessionId = $endpoint[1]
            if ([int]$endpoint[2] -ne $hostWorker.Process.Id) { throw 'Endpoint PID mismatch' }
            $players = @()
            for ($i=0; $i -lt $remotes; $i++) {
                $mode = $(if ($name -eq 'HostLoss') {'expect-eof'} elseif ($name -eq 'Eight' -and $i -eq 6) {'disconnect'} else {'play'})
                $workerName = 'client-' + $i
                $arguments = @('-Role','Client','-Port',[string]$port,'-Session',$sessionId,'-Mode',$mode,'-Report',(Join-Path $caseDirectory ($workerName + '.json')))
                if ($i -eq 0) { $arguments += '-InjectChecks' }
                $players += Start-Worker $workerName $arguments
            }
            Wait-For { (Test-Path -LiteralPath (Join-Path $caseDirectory 'lobby.txt')) -or $hostWorker.Process.HasExited } 25 'ready lobby'
            if ($hostWorker.Process.HasExited) { throw 'Host exited before lobby gate' }
            if ($name -eq 'Eight') {
                foreach ($probe in @('capacity','bad-length','partial-close','stall','slow-drip','wrong-room')) {
                    $probeName = 'probe-' + $probe
                    $worker = Start-Worker $probeName @('-Role','Client','-Port',[string]$port,'-Session',$sessionId,'-Mode',$probe,'-Report',(Join-Path $caseDirectory ($probeName + '.json')))
                    $probeReports += Read-Worker $probeName $worker
                }
            }
            Set-Content -LiteralPath (Join-Path $caseDirectory 'start.signal') -Value 'start'
            if ($name -eq 'HostLoss') {
                $status = Join-Path $caseDirectory 'running.txt'
                Wait-For { (Test-Path -LiteralPath $status) -and [int](Get-Content -LiteralPath $status -Raw) -ge 30 } 15 'host reached tick 30'
                # Kill only the host Process instance this run created; no name-based process termination.
                $hostWorker.Process.Kill($true); $hostWorker.Killed = $true; $hostWorker.Process.WaitForExit()
            }
            Wait-For { @($players | Where-Object { -not $_.Process.HasExited }).Count -eq 0 } 20 'client completion'
            for ($i=0; $i -lt $players.Count; $i++) { $reports += Read-Worker ('client-' + $i) $players[$i] }
            $hostReport = Read-Worker 'host' $hostWorker ($name -eq 'HostLoss')
            $playerPids = @($hostWorker.Process.Id) + @($players | ForEach-Object { $_.Process.Id })
            if (@($playerPids | Sort-Object -Unique).Count -ne $remotes+1) { throw 'Players did not run in distinct processes' }
            if ($name -eq 'HostLoss') {
                if ($reports[0].EndReason -ne 'socket_eof' -or $reports[0].LastTick -lt 30 -or $reports[0].Counter -lt 1) { throw 'Host loss not observed after live exchange' }
            }
            else {
                if ($hostReport.MaxRoster -ne $remotes+1 -or $hostReport.PhaseAfterShutdown -ne 'Closed' -or $hostReport.CompletedTicks -ne 180) { throw 'Host lifecycle/capacity mismatch' }
                foreach ($client in $reports) {
                    $mapping = @($hostReport.Peers | Where-Object ClientPid -eq $client.Pid)
                    if ($mapping.Count -ne 1 -or $mapping[0].PlayerId -ne $client.PlayerId -or $mapping[0].Connection -ne $client.Connection) { throw 'Socket-to-actor mapping mismatch' }
                    if ($client.Mode -eq 'play' -and ($client.Counter -ne 18 -or $client.FeedbackEffects -ne 18 -or $client.LastProcessed -ne $client.SentInputs)) { throw 'Client final state/input mismatch' }
                }
                $expectedDisconnects = $(if ($name -eq 'Eight') {1} else {0})
                if (@($hostReport.Peers | Where-Object Disconnected).Count -ne $expectedDisconnects) { throw 'Unexpected player disconnect' }
                foreach ($reason in @('stale_sequence','malformed_input','wrong_session','wrong_generation')) {
                    if ($hostReport.Rejections.$reason -lt 1) { throw "Input rejection not exercised: $reason" }
                }
                if ($name -eq 'Eight') {
                    foreach ($reason in @('session_full','frame_length','truncated_frame','frame_timeout','frame_deadline','wrong_room')) {
                        if ($hostReport.Rejections.$reason -lt 1) { throw "Probe not rejected: $reason" }
                    }
                }
            }
            $check = [Net.Sockets.TcpClient]::new()
            try {
                if (-not $check.ConnectAsync([Net.IPAddress]::Loopback,$port).Wait(5000)) { throw 'Listener-close probe timed out' }
            }
            catch {
                $cause = $_.Exception.GetBaseException()
                if ($cause -is [Net.Sockets.SocketException] -and $cause.SocketErrorCode -eq [Net.Sockets.SocketError]::ConnectionRefused) { $portNotListening = $true }
                else { throw }
            }
            finally { $check.Dispose() }
            if (-not $portNotListening) { throw 'Host listener still accepts connections after exit' }
        }
        catch { $errors.Add($_.Exception.Message) }
        finally {
            foreach ($worker in $owned) {
                if (-not $worker.Process.HasExited) { $worker.Process.Kill($true); $worker.Process.WaitForExit(); $worker.Killed=$true }
                $stdout = $worker.Stdout.GetAwaiter().GetResult(); $stderr = $worker.Stderr.GetAwaiter().GetResult()
                Set-Content -LiteralPath (Join-Path $caseDirectory ($worker.Name + '.log')) -Value ($stdout + $stderr) -Encoding utf8
                if ($worker.Process.ExitCode -ne 0 -and -not $worker.Killed) { $errors.Add($worker.Name + ': exit ' + $worker.Process.ExitCode) }
            }
        }
        $row = [ordered]@{name=$caseName;passed=($errors.Count -eq 0);participants=$remotes+1;playerProcessIds=$playerPids
            launchedProcesses=$owned.Count;listenerStopped=$portNotListening;hostKilled=($null -ne $hostWorker -and $hostWorker.Killed)
            host=$hostReport;clients=@($reports);probes=@($probeReports);failures=@($errors.ToArray())}
        $cases.Add([pscustomobject]$row)
        Write-Host ("{0}: passed={1}; player processes={2}; failures={3}" -f $caseName,$row.passed,$playerPids.Count,($errors -join '; '))
    }
}
$failed = @($cases | Where-Object { -not $_.passed })
$summary = [ordered]@{
    evidenceType='csharp-loopback-tcp-separate-processes';executedAtUtc=[DateTimeOffset]::UtcNow.ToString('o');runId=$runId
    networkSocketsExecuted=$true;separateProcessesExecuted=$true;unityEditorExecuted=$false;internetExecuted=$false
    combatValidated=$false;productionTransportSelected=$false;bindAddress='127.0.0.1';scenario=$Scenario;repeat=$Repeat
    powershell=$PSVersionTable.PSVersion.ToString();runtime=[Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
    cases=@($cases.ToArray());passed=($failed.Count -eq 0)
}
$summaryPath = Join-Path $runDirectory 'summary.json'; $summary | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $summaryPath -Encoding utf8
Write-Host ("Socket cases passed: {0}/{1}; summary: {2}" -f ($cases.Count-$failed.Count),$cases.Count,$summaryPath)
if ($failed.Count -gt 0) { exit 1 }
