<#
Uses the repository's Windows build recipe, with explicit Windows-only global properties.
No deployment/test is performed by this script. The benchmark runner launches the exe.
#>
[CmdletBinding()]
param(
    [string]$DotNet,
    [ValidateSet('x64', 'arm64')][string]$Architecture = 'x64',
    # Use only after the scoped prerequisite build succeeded for this worktree.
    [switch]$SkipBuildTasks
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../../../..')).Path
if (-not $DotNet) { $DotNet = Join-Path $root '.dotnet/dotnet.exe' }
$DotNet = (Resolve-Path $DotNet).Path
if (-not $IsWindows) { throw 'Windows host required.' }
$required = (Get-Content (Join-Path $root 'global.json') -Raw | ConvertFrom-Json).tools.dotnet
if ((& $DotNet --version).Trim() -ne $required) { throw "SDK must be $required." }
$oldPath = $env:PATH
# These MUST be command-line global properties: Directory.Build.props assigns
# several platform flags unconditionally, overriding environment variables.
$windowsOnly = @(
    '-p:IncludeAndroidTargetFrameworks=false',
    '-p:IncludeIosTargetFrameworks=false',
    '-p:IncludeMacCatalystTargetFrameworks=false',
    '-p:IncludeTizenTargetFrameworks=false',
    '-p:IncludePreviousTfms=false',
    '-p:UseMaui=false',
    '-p:UseWorkload=false'
)
try {
    $env:PATH = "$(Split-Path $DotNet);$oldPath"
    Push-Location $root
    try {
        if (-not $SkipBuildTasks) {
            & $DotNet build Microsoft.Maui.BuildTasks.slnf -c Release @windowsOnly
            if ($LASTEXITCODE -ne 0) { throw "Build tasks failed: $LASTEXITCODE" }
        }
        # BuildAndRunSandbox.ps1 hardcodes net10. Its shared Windows helper has no
        # extra-MSBuild-properties parameter. Use that helper's Windows build-only
        # recipe here, adding explicit globals so restore cannot request iOS/Android.
        # RID defaults follow the host; run on the requested native architecture.
        $hostArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
        if ($hostArchitecture -ne $Architecture) { throw 'Build on the requested native architecture; do not silently compare different RIDs.' }
        & $DotNet build (Join-Path $root 'src/Controls/samples/Controls.Sample.Sandbox/Maui.Controls.Sample.Sandbox.csproj') `
            -f net11.0-windows10.0.19041.0 -c Release -p:TreatWarningsAsErrors=false @windowsOnly
        if ($LASTEXITCODE -ne 0) { throw "Sandbox build failed: $LASTEXITCODE" }
    } finally { Pop-Location }
} finally { $env:PATH = $oldPath }
Write-Host 'Build complete. Locate the Release net11 Windows Maui.Controls.Sample.Sandbox.exe, then invoke Run-Benchmark.ps1 -Exe <absolute path>.'
