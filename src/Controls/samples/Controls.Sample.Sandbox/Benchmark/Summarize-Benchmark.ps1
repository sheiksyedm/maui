[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Directory,
    [ValidateRange(10, 1000)][int]$RequiredRuns = 10
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$manifest = @(Get-Content (Join-Path $Directory 'manifest.json') -Raw | ConvertFrom-Json)
if (@($manifest | Where-Object Scenario -eq 'manual').Count -gt 0) {
    throw 'Manual human-input captures are descriptive raw records, not repeatable batch runs. No automatic medians/comparison generated.'
}
$valid = @($manifest | Where-Object { $_.Valid -and $_.Phase -eq 'measured' })
$failures = @($manifest | Where-Object { -not $_.Valid })
[IO.File]::WriteAllText((Join-Path $Directory 'failures.json'), (ConvertTo-Json -InputObject $failures -Depth 10))
$coverage = [Collections.Generic.List[object]]::new()
if (Test-Path (Join-Path $Directory 'environment.json')) {
    $environment = Get-Content (Join-Path $Directory 'environment.json') -Raw | ConvertFrom-Json
    foreach ($scenario in $environment.Scenarios) {
        foreach ($handler in @('CV1', 'CV2')) {
            $warmups = @($manifest | Where-Object { $_.Scenario -eq $scenario -and $_.Handler -eq $handler -and $_.Phase -eq 'warmup' -and $_.Valid }).Count
            $measured = @($valid | Where-Object { $_.Scenario -eq $scenario -and $_.Handler -eq $handler }).Count
            $coverage.Add([pscustomobject]@{ Scenario = $scenario; Handler = $handler
                ValidWarmups = $warmups; ValidMeasured = $measured
                Complete = ($warmups -eq $environment.Warmups -and $measured -eq $RequiredRuns) })
        }
    }
}
[IO.File]::WriteAllText((Join-Path $Directory 'coverage-report.json'), (ConvertTo-Json -InputObject $coverage.ToArray() -Depth 10))
$raw = @{}
$unavailable = [Collections.Generic.List[object]]::new()
foreach ($entry in $valid) {
    $data = Get-Content $entry.Raw -Raw | ConvertFrom-Json
    if ($data.Status -ne 'passed' -or $data.Failures.Count -ne 0) { throw "Raw result changed since validation: $($entry.Raw)" }
    $raw[$entry.Id] = $data
    if ($null -ne $data.PSObject.Properties['NotMeasured']) {
        foreach ($property in $data.NotMeasured.PSObject.Properties) {
            $unavailable.Add([pscustomobject]@{ Scenario = $entry.Scenario; Handler = $entry.Handler
                Run = $entry.Run; Metric = $property.Name; Reason = $property.Value })
        }
    }
}
[IO.File]::WriteAllText((Join-Path $Directory 'not-measured.json'), (ConvertTo-Json -InputObject $unavailable.ToArray() -Depth 10))
function Quantile([double[]]$Values, [double]$P) {
    $sorted = @($Values | Sort-Object)
    if ($sorted.Count -eq 0) { return $null }
    $index = ($sorted.Count - 1) * $P
    $lo = [int][Math]::Floor($index); $hi = [int][Math]::Ceiling($index)
    return $sorted[$lo] + ($sorted[$hi] - $sorted[$lo]) * ($index - $lo)
}
$table = [Collections.Generic.List[object]]::new()
foreach ($scenario in @($manifest.Scenario | Select-Object -Unique)) {
    $one = @($valid | Where-Object { $_.Scenario -eq $scenario -and $_.Handler -eq 'CV1' })
    $two = @($valid | Where-Object { $_.Scenario -eq $scenario -and $_.Handler -eq 'CV2' })
    $names = [Collections.Generic.HashSet[string]]::new()
    foreach ($entry in @($one) + @($two)) {
        foreach ($property in $raw[$entry.Id].Metrics.PSObject.Properties) { $names.Add($property.Name) | Out-Null }
    }
    foreach ($name in $names) {
        $a = @(); $b = @(); $pairs = @()
        foreach ($entry in $one) {
            $property = $raw[$entry.Id].Metrics.PSObject.Properties[$name]
            if ($null -ne $property) { $a += [double]$property.Value }
        }
        foreach ($entry in $two) {
            $property = $raw[$entry.Id].Metrics.PSObject.Properties[$name]
            if ($null -ne $property) { $b += [double]$property.Value }
        }
        foreach ($entry in $one) {
            $partner = @($two | Where-Object Run -eq $entry.Run)
            if ($partner.Count -ne 1) { continue }
            $left = $raw[$entry.Id].Metrics.PSObject.Properties[$name]
            $right = $raw[$partner[0].Id].Metrics.PSObject.Properties[$name]
            if ($null -ne $left -and $null -ne $right) { $pairs += [double]$right.Value - [double]$left.Value }
        }
        $m1 = Quantile $a 0.5; $m2 = Quantile $b 0.5
        $diff = if ($null -ne $m1 -and $null -ne $m2) { $m2 - $m1 } else { $null }
        $percent = if ($null -ne $diff -and $m1 -ne 0) { $diff / $m1 * 100 } else { $null }
        $complete = $a.Count -eq $RequiredRuns -and $b.Count -eq $RequiredRuns -and $pairs.Count -eq $RequiredRuns
        if (@($coverage | Where-Object { $_.Scenario -eq $scenario -and -not $_.Complete }).Count -gt 0) { $complete = $false }
        $fraction = if ($pairs.Count -gt 0 -and $null -ne $diff -and $diff -ne 0) {
            @($pairs | Where-Object { [Math]::Sign($_) -eq [Math]::Sign($diff) }).Count / $pairs.Count
        } else { 0 }
        $iqr1 = if ($a.Count) { (Quantile $a 0.75) - (Quantile $a 0.25) } else { $null }
        $iqr2 = if ($b.Count) { (Quantile $b 0.75) - (Quantile $b 0.25) } else { $null }
        $direction = 'descriptive only; not intrinsically better'
        if ($name -match 'Ms$|Bytes$') { $direction = 'lower generally preferable; scenario/measurement semantics apply' }
        if ($name -match '^gen\dCount$') { $direction = 'descriptive; GC count alone is not better/worse' }
        $assessment = 'inconclusive'
        # Conservative descriptive consistency rule, NOT statistical significance/equivalence.
        if ($complete -and $fraction -ge 0.8 -and [Math]::Abs($diff) -gt [Math]::Max($iqr1, $iqr2)) {
            $assessment = if ($diff -lt 0) { 'consistently lower observed values' } else { 'consistently higher observed values' }
        } elseif ($complete -and $null -ne $percent -and [Math]::Abs($percent) -le 5) {
            $assessment = 'approximately equivalent descriptively (within 5%); equivalence not established'
        }
        $table.Add([pscustomobject]@{
            Scenario = $scenario; Metric = $name; CV1N = $a.Count; CV2N = $b.Count
            CV1Median = $m1; CV2Median = $m2; CV1P95 = (Quantile $a 0.95); CV2P95 = (Quantile $b 0.95)
            Difference = $diff; PercentDifference = $percent; CV1IQR = $iqr1; CV2IQR = $iqr2
            PairedDirectionFraction = $fraction; Complete = $complete; BetterDirection = $direction; Assessment = $assessment
        })
    }
}
[IO.File]::WriteAllText((Join-Path $Directory 'summary.json'), (ConvertTo-Json -InputObject $table.ToArray() -Depth 10))
$table | Format-Table Scenario, Metric, CV1N, CV2N, CV1Median, CV2Median, PercentDifference, Assessment -AutoSize
Write-Host 'p95 is the interpolated 95th percentile across successful runs, NOT frame time. See summary.json for full fields. No universal handler superiority or significance claim.'
