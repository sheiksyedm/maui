[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Exe,
    [ValidateSet(1000, 10000, 50000)][int]$Count = 10000,
    [string[]]$Scenarios = @('initial', 'sequential', 'rapid', 'updates',
        'coverage-vertical-fixed', 'coverage-vertical-variable', 'coverage-horizontal-fixed', 'coverage-horizontal-variable'),
    [ValidateRange(2, 100)][int]$Warmups = 2,
    [ValidateRange(10, 1000)][int]$MeasuredRuns = 10,
    [ValidateRange(60, 7200)][int]$TimeoutSeconds = 3600,
    [string]$DotNet,
    [string]$Output,
    [string]$CancelFile,
    [switch]$StopOnInvalid
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ('manual' -in $Scenarios) { throw 'Manual capture is not a batch scenario. Use Start-ManualCapture.ps1 and the explicit in-app Start button.' }
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../../../..')).Path
if (-not $IsWindows) { throw 'Interactive Windows host required. No measurements collected.' }
$Exe = (Resolve-Path $Exe).Path
if ($Exe -notmatch '[\\/]Release[\\/]' -or $Exe -notmatch 'net11\.0-windows') {
    throw 'Pass the Release net11 Windows executable, not dotnet run, a package, or a Debug binary.'
}
if (-not $DotNet) { $DotNet = Join-Path $root '.dotnet/dotnet.exe' }
$DotNet = (Resolve-Path $DotNet).Path
$runtimeRoot = Split-Path $DotNet -Parent
$runtimeRootVariable = 'DOTNET_ROOT_' + [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToUpperInvariant()
if (-not $Output) { $Output = Join-Path $root 'CustomAgentLogsTmp/CollectionView2Benchmark' }
$batch = [guid]::NewGuid().ToString('N')
$Output = Join-Path ([IO.Path]::GetFullPath($Output)) $batch
[IO.Directory]::CreateDirectory($Output) | Out-Null
if (-not $CancelFile) { $CancelFile = Join-Path $Output 'cancel-batch' }
$valid = @('initial', 'sequential', 'rapid', 'native-positions', 'updates',
    'coverage-vertical-fixed', 'coverage-vertical-variable', 'coverage-horizontal-fixed', 'coverage-horizontal-variable')
foreach ($scenario in $Scenarios) { if ($scenario -notin $valid) { throw "Invalid scenario $scenario" } }
if (@($Scenarios | Select-Object -Unique).Count -ne $Scenarios.Count) { throw 'Duplicate scenarios are not allowed.' }

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class CVBenchDesktop {
    [DllImport("user32.dll")] public static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] public static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
}
'@
$desktop = [CVBenchDesktop]::OpenInputDesktop(0, $false, 0x0100)
if (-not [Environment]::UserInteractive -or [Diagnostics.Process]::GetCurrentProcess().SessionId -eq 0 -or $desktop -eq [IntPtr]::Zero) {
    throw 'No usable interactive input desktop. Runtime measurements blocked; run this on an unlocked Windows desktop.'
}
[CVBenchDesktop]::CloseDesktop($desktop) | Out-Null
$sdk = (& $DotNet --version).Trim()
$required = (Get-Content (Join-Path $root 'global.json') -Raw | ConvertFrom-Json).tools.dotnet
if ($sdk -ne $required) { throw "SDK mismatch: $sdk; required $required." }
$workloads = (& $DotNet workload list 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0 -or $workloads -notmatch 'maui-windows') { throw 'maui-windows workload not found.' }
$cpu = Get-CimInstance Win32_Processor
$os = Get-CimInstance Win32_OperatingSystem
$environment = [ordered]@{
    Batch = $batch; Utc = [DateTime]::UtcNow.ToString('o'); OS = $os.Caption; OSVersion = $os.Version
    CPU = @($cpu | ForEach-Object Name); LogicalCores = [Environment]::ProcessorCount
    RAMBytes = [long]$os.TotalVisibleMemorySize * 1024; SDK = $sdk
    Architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    PrimaryDisplayWidth = [CVBenchDesktop]::GetSystemMetrics(0); PrimaryDisplayHeight = [CVBenchDesktop]::GetSystemMetrics(1)
    Scaling = 'Per-window XamlRoot.RasterizationScale in raw JSON; no registry scaling estimate.'
    Workloads = $workloads; Executable = $Exe; ExecutableSHA256 = (Get-FileHash $Exe -Algorithm SHA256).Hash
    AssemblySHA256 = (Get-FileHash ([IO.Path]::ChangeExtension($Exe, '.dll')) -Algorithm SHA256).Hash
    RuntimeRoot = $runtimeRoot
    Count = $Count; Warmups = $Warmups; MeasuredRuns = $MeasuredRuns; Scenarios = $Scenarios
    Input = '6000 DIPs/sec native sweep, <= half viewport per step, 25ms minimum pacing; fixed rapid index sequence'
    CancellationFile = $CancelFile
}
[IO.File]::WriteAllText((Join-Path $Output 'environment.json'), ($environment | ConvertTo-Json -Depth 10))
Write-Host "Artifacts: $Output"
Write-Host "Cancel gracefully by creating: $CancelFile (or use in-app Cancel / Ctrl+C)."
$records = [Collections.Generic.List[object]]::new()
$owned = $null
$runCancel = $null
$invariants = $null
try {
    foreach ($scenario in $Scenarios) {
        foreach ($phase in @('warmup', 'measured')) {
            $runs = if ($phase -eq 'warmup') { $Warmups } else { $MeasuredRuns }
            for ($run = 1; $run -le $runs; $run++) {
                # Alternate first handler per pair, including warmups.
                $order = if ($run % 2 -eq 1) { @('CV1', 'CV2') } else { @('CV2', 'CV1') }
                foreach ($handler in $order) {
                    if (Test-Path $CancelFile) { throw [OperationCanceledException]::new('Batch cancelled.') }
                    $id = "$scenario-$phase-$run-$handler"
                    $runCancel = Join-Path $Output "$id.cancel"
                    $start = [Diagnostics.ProcessStartInfo]::new($Exe)
                    $start.WorkingDirectory = $root
                    $start.UseShellExecute = $false
                    $start.RedirectStandardOutput = $true
                    $start.RedirectStandardError = $true
                    # Apphosts do not use global.json SDK paths to discover the runtime.
                    # Select the already-installed local runtime in this CHILD process only.
                    $start.Environment['DOTNET_ROOT'] = $runtimeRoot
                    $start.Environment[$runtimeRootVariable] = $runtimeRoot
                    $start.Environment['CVBENCH_AUTO'] = '1'
                    $start.Environment['CVBENCH_HANDLER'] = $handler
                    $start.Environment['CVBENCH_COUNT'] = $Count.ToString()
                    $start.Environment['CVBENCH_SCENARIO'] = $scenario
                    $start.Environment['CVBENCH_RUN'] = $run.ToString()
                    $start.Environment['CVBENCH_SEED'] = '1729'
                    $start.Environment['CVBENCH_WARMUP'] = $(if ($phase -eq 'warmup') { '1' } else { '0' })
                    $start.Environment['CVBENCH_RUNID'] = $id
                    $start.Environment['CVBENCH_OUTPUT'] = $Output
                    $owned = [Diagnostics.Process]::Start($start)
                    $stdout = $owned.StandardOutput.ReadToEndAsync()
                    $stderr = $owned.StandardError.ReadToEndAsync()
                    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
                    $timedOut = $false
                    while (-not $owned.WaitForExit(250)) {
                        if (Test-Path $CancelFile) { throw [OperationCanceledException]::new('Batch cancelled.') }
                        if ([DateTime]::UtcNow -gt $deadline) {
                            [IO.File]::WriteAllText($runCancel, 'timeout')
                            $timedOut = $true
                            if (-not $owned.WaitForExit(20000)) { Stop-Process -Id $owned.Id -Force }
                            break
                        }
                    }
                    $owned.WaitForExit()
                    $exitCode = $owned.ExitCode
                    $outputText = $stdout.GetAwaiter().GetResult()
                    [IO.File]::WriteAllText((Join-Path $Output "$id.stdout.log"), $outputText)
                    $errorText = $stderr.GetAwaiter().GetResult()
                    [IO.File]::WriteAllText((Join-Path $Output "$id.stderr.log"), $errorText)
                    $raw = Join-Path $Output "$id.json"
                    $record = [ordered]@{ Id = $id; Scenario = $scenario; Handler = $handler; Run = $run; Phase = $phase
                        ProcessId = $owned.Id; ExitCode = $exitCode; Raw = $raw; Valid = $false; Errors = @() }
                    $owned.Dispose(); $owned = $null
                    $errors = [Collections.Generic.List[string]]::new()
                    if ($timedOut) { $errors.Add('Timed out; no performance claim.') }
                    if ($exitCode -ne 0) { $errors.Add("Process exit $exitCode") }
                    if (($errorText + $outputText) -match '(?i)exception|binding.*(fail|error)|layout.*cycle') { $errors.Add('Diagnostic error in stdout/stderr; inspect log.') }
                    if (-not (Test-Path $raw)) { $errors.Add('Missing raw result (startup/crash/export failure).') }
                    else {
                        try {
                            $data = Get-Content $raw -Raw | ConvertFrom-Json
                            if ($data.SchemaVersion -ne 1) { $errors.Add('Unknown raw schema version.') }
                            $cfg = $data.Configuration
                            if ($data.Status -ne 'passed' -or $data.Failures.Count -ne 0) { $errors.Add("Status $($data.Status); functional failures excluded.") }
                            if ($cfg.Handler -ne $handler -or $cfg.Count -ne $Count -or $cfg.Scenario -ne $scenario -or
                                $cfg.Run -ne $run -or $cfg.RunId -ne $id -or $cfg.Seed -ne 1729 -or
                                $cfg.Warmup -ne ($phase -eq 'warmup') -or $data.ProcessId -ne $record.ProcessId) { $errors.Add('Run metadata mismatch.') }
                            if ($data.DebuggerAttached) { $errors.Add('Debugger attached.') }
                            if ($handler -eq 'CV2') {
                                if ($data.HandlerType -notmatch '\.CollectionViewHandler2$' -or $data.NativeItemsType -notmatch 'ItemsRepeater$') { $errors.Add('CV2 native proof missing.') }
                            } elseif ($data.HandlerType -notmatch '\.CollectionViewHandler$' -or $data.NativeItemsType -notmatch 'ListView|GridView') { $errors.Add('CV1 native proof missing.') }
                            if (@($data.Logs | Where-Object { $_ -match 'CVBENCH: START' }).Count -ne 1 -or
                                @($data.Logs | Where-Object { $_ -match 'CVBENCH: END status=passed' }).Count -ne 1) { $errors.Add('Start/end markers missing.') }
                            if ($data.Metrics.durationMs -le 0 -or $data.Metrics.initialVisibleLayoutMs -le 0) { $errors.Add('Expected metrics missing.') }
                            if ($scenario -ne 'initial' -and $data.Checks.Count -eq 0) { $errors.Add('No executed functional checks.') }
                            $stages = @($data.Realization.Stage)
                            if ('before' -notin $stages) { $errors.Add('Missing initial realization evidence.') }
                            if ($scenario -in @('rapid', 'sequential', 'native-positions') -or $scenario.StartsWith('coverage-')) {
                                if ('end' -notin $stages -or @($stages | Where-Object { $_ -match '^midpoint' }).Count -eq 0) { $errors.Add('Missing midpoint/end realization evidence.') }
                            }
                            if ($scenario -eq 'updates' -or $scenario.StartsWith('coverage-')) {
                                foreach ($metric in @('append1000VisibleLayoutMs', 'insert100VisibleLayoutMs', 'remove100VisibleLayoutMs',
                                    'replaceVisibleLayoutMs', 'mutationWhileAwayVisibleLayoutMs', 'resetVisibleLayoutMs')) {
                                    if ($null -eq $data.Metrics.PSObject.Properties[$metric]) { $errors.Add("Missing operation $metric") }
                                }
                            }
                            if ($scenario.StartsWith('coverage-') -and $data.Checks.Count -lt 10) { $errors.Add('Incomplete functional coverage checks.') }
                            $geometry = "$($data.Architecture)|$($data.RasterizationScale)|$($data.WindowWidth)|$($data.WindowHeight)|$($data.ViewportWidth)|$($data.ViewportHeight)"
                            # Orientation changes viewport dimensions slightly; compare within each scenario.
                            if (-not $invariants) { $invariants = @{} }
                            if ($invariants.ContainsKey($scenario) -and $invariants[$scenario] -ne $geometry) { $errors.Add('Architecture/scaling/window/viewport drift.') }
                            else { $invariants[$scenario] = $geometry }
                            if ($Count -eq 50000) {
                                foreach ($sample in $data.Realization) {
                                    if ($sample.RealizedRows -gt $sample.Bound -or $sample.NativeContainers -gt $sample.Bound) { $errors.Add('Virtualization bound exceeded.') }
                                }
                            }
                        } catch { $errors.Add("Invalid raw result: $($_.Exception.Message)") }
                    }
                    $record.Errors = $errors.ToArray(); $record.Valid = $errors.Count -eq 0
                    $records.Add([pscustomobject]$record)
                    Write-Host "CVBENCH: runner END $id valid=$($record.Valid) exit=$exitCode"
                    [IO.File]::WriteAllText((Join-Path $Output 'manifest.json'), (ConvertTo-Json -InputObject $records.ToArray() -Depth 10))
                    if ($StopOnInvalid -and -not $record.Valid) {
                        throw "Invalid run $id; paused before further launches. See $raw, manifest.json and stdout/stderr logs. Errors: $($record.Errors -join '; ')"
                    }
                }
            }
        }
    }
} finally {
    if ($owned -and -not $owned.HasExited) {
        [IO.File]::WriteAllText($runCancel, 'runner cancellation')
        if (-not $owned.WaitForExit(20000)) { Stop-Process -Id $owned.Id -Force }
        $owned.Dispose()
    }
    [IO.File]::WriteAllText((Join-Path $Output 'manifest.json'), (ConvertTo-Json -InputObject $records.ToArray() -Depth 10))
}
& (Join-Path $PSScriptRoot 'Summarize-Benchmark.ps1') -Directory $Output -RequiredRuns $MeasuredRuns
if (@($records | Where-Object { -not $_.Valid }).Count -gt 0) { throw 'One or more runs failed validation; see manifest.json and failures.json. Failed runs are never averaged.' }
