[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('CV1', 'CV2')][string]$Handler,
    [ValidateSet(1000, 10000, 50000)][int]$Count = 10000,
    [ValidateRange(5, 120)][int]$Seconds = 30,
    [string]$Exe,
    [string]$DotNet,
    [string]$Output,
    # Explicit user approval to open a window, not approval to begin recording.
    [switch]$ReadyToOpen
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $ReadyToOpen) { throw 'No app launched. Coordinate an unlocked desktop, then pass -ReadyToOpen. Recording still requires an in-app Start click.' }
if (-not $IsWindows -or -not [Environment]::UserInteractive -or [Diagnostics.Process]::GetCurrentProcess().SessionId -eq 0) {
    throw 'An interactive, unlocked Windows desktop is required. No app launched.'
}
if (-not ('CVBenchManualDesktop' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class CVBenchManualDesktop {
    [DllImport("user32.dll")] public static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] public static extern bool CloseDesktop(IntPtr desktop);
}
'@
}
$desktop = [CVBenchManualDesktop]::OpenInputDesktop(0, $false, 0x0100)
if ($desktop -eq [IntPtr]::Zero) { throw 'Input desktop inaccessible. No app launched; coordinate the unlocked desktop.' }
if (-not [CVBenchManualDesktop]::CloseDesktop($desktop)) { throw 'Input desktop probe cleanup failed. No app launched.' }
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
if (-not $Exe) { $Exe = Join-Path $root 'artifacts\bin\Maui.Controls.Sample.Sandbox\Release\net11.0-windows10.0.19041.0\win-x64\Maui.Controls.Sample.Sandbox.exe' }
$Exe = (Resolve-Path $Exe).Path
if ($Exe -notmatch '[\\/]Release[\\/]' -or $Exe -notmatch 'net11\.0-windows') {
    throw 'Select the built Release net11 Windows executable.'
}
if (-not $DotNet) { $DotNet = Join-Path $root '.dotnet\dotnet.exe' }
$DotNet = (Resolve-Path $DotNet).Path
$runtimeRoot = Split-Path $DotNet -Parent
$architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToUpperInvariant()
if ($Exe -notmatch "[\\/]win-$($architecture.ToLowerInvariant())[\\/]") { throw 'Executable RID must match the native host architecture.' }
$assemblyHash = (Get-FileHash ([IO.Path]::ChangeExtension($Exe, '.dll')) -Algorithm SHA256).Hash
if (-not $Output) { $Output = Join-Path $root 'CustomAgentLogsTmp\CollectionView2Benchmark' }
$Output = Join-Path ([IO.Path]::GetFullPath($Output)) ("manual-$Handler-" + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($Output) | Out-Null
$start = [Diagnostics.ProcessStartInfo]::new($Exe)
$start.UseShellExecute = $false
$start.WorkingDirectory = $root
# Apphosts do not read global.json to select their runtime. No parent/global env changes.
$start.Environment['DOTNET_ROOT'] = $runtimeRoot
$start.Environment["DOTNET_ROOT_$architecture"] = $runtimeRoot
$start.Environment['CVBENCH_HANDLER'] = $Handler
$start.Environment['CVBENCH_COUNT'] = $Count.ToString([Globalization.CultureInfo]::InvariantCulture)
$start.Environment['CVBENCH_MANUAL_SECONDS'] = $Seconds.ToString([Globalization.CultureInfo]::InvariantCulture)
$start.Environment['CVBENCH_SCENARIO'] = 'manual'
$start.Environment['CVBENCH_AUTO'] = '0'
$start.Environment['CVBENCH_WARMUP'] = '0'
$start.Environment['CVBENCH_RUN'] = '1'
$start.Environment['CVBENCH_SEED'] = '1729'
$start.Environment['CVBENCH_RUNID'] = [guid]::NewGuid().ToString('N')
$start.Environment['CVBENCH_OUTPUT'] = $Output
$process = $null
try {
    $process = [Diagnostics.Process]::Start($start)
    [ordered]@{
        ProcessId = $process.Id; Handler = $Handler; Count = $Count; Seconds = $Seconds
        Executable = $Exe; AssemblySHA256 = $assemblyHash
        RuntimeRoot = $runtimeRoot; Output = $Output; Utc = [DateTime]::UtcNow.ToString('o')
        Capture = 'NOT started. Explicit in-app Start manual capture required.'
    } | ConvertTo-Json | Set-Content (Join-Path $Output 'launch.json')
    Write-Host "Launch requested: PID $($process.Id), $Handler, $Count rows. No render/startup success claimed."
    Write-Host "Wait for verified native handler / Ready; click Start manual capture (RunBenchmark), then scroll down/up for $Seconds seconds."
    Write-Host "Artifacts: $Output. App stays open after export; close it before launching the other handler."
} finally {
    if ($process) { $process.Dispose() }
}
