# Windows CollectionView empirical benchmark

Custom scenario implementing the supplied large-data comparison specification, not
a handler fix, general review, BenchmarkDotNet benchmark, or CI performance verdict.
Only Sandbox and this directory are changed. No additional packages.

The shareable [measured comparison report](RESULTS.md) contains the initial-layout
and memory results, full template structure, and limitations. Scrolling and
full-journey virtualization remain unverified; no overall winner is claimed.

## Build and run on an interactive Windows desktop

Use the SDK pinned by `global.json` (`11.0.100-rc.2.26470.103` in this branch),
the `maui-windows` workload, native x64/arm64 architecture, and an unlocked desktop.
Parent/coordinator owns builds and environment verification; implementation/static
checks do **not** establish that a WinUI runtime is installed or that this app renders.
Do not launch the app, perform interactive setup, or begin measurements until the
parent has coordinated that step with the user. Pause and report setup difficulties;
do not automatically install more workloads or proceed to desktop testing.

## Explicit user-started manual capture (no measurements collected for this change)

The latest startup stall remains unresolved. This support was compiled/static-checked
only: no window was opened, user scrolling was not observed, and no manual results
exist yet. Coordinate an unlocked desktop and inspect any startup dialog before
using the following commands. A PID or successful launch request is not proof the
page rendered. If the app never shows the verified native handler and Ready, stop;
do not retry batches, install SDKs, or speculate about a handler performance regression.

From the **real worktree root**, using PowerShell 7, after the user agrees to open
the first window:

```powershell
$manual = '.\src\Controls\samples\Controls.Sample.Sandbox\Benchmark\Start-ManualCapture.ps1'
& $manual -Handler CV1 -Count 50000 -Seconds 30 -ReadyToOpen
# Finish/export, then close CV1. Coordinate readiness before opening CV2:
& $manual -Handler CV2 -Count 50000 -Seconds 30 -ReadyToOpen
# Use -Count 10000 for a separate 10k pair. Default count is 10000.
```

The script selects the existing Release `win-x64` executable by default; pass `-Exe`
for a different native-host RID/output and `-DotNet` for the installed pinned
`dotnet.exe`. It sets `DOTNET_ROOT`, architecture-specific `DOTNET_ROOT_*`, and
`CVBENCH_HANDLER`, `COUNT`, `SCENARIO=manual`, `AUTO=0`, `MANUAL_SECONDS`, `SEED=1729`,
output and run identity in **that child process only**, before handler registration.
It does not change shared environment variables, install/deploy packages, elevate,
trace, or record immediately. Without `-ReadyToOpen`, it rejects before launching.
`-Seconds` accepts **5 through 120**, default **30**.

1. Wait for **HandlerStatus** to identify the actual native handler and
   **BenchmarkStatus** to say Ready. Preparation generates the same fixed 72-DIP
   rich template and verifies visible layout, but is outside the capture interval.
2. Click **Start manual capture** (`AutomationId=RunBenchmark`) only when ready.
   For the selected duration, scroll down and back up using the wheel/touchpad or
   native scrollbar. The status shows requested CV1/CV2 and seconds remaining;
   HandlerStatus retains the verified full handler/native types.
3. Wait for `captured` and the export path. **The app stays open**; no need to close
   it to export. Cancel (`CancelBenchmark`) saves a separate cancelled result.
   Export (`ExportResults`) can write the completed result again.
4. Close this process before opening the other handler. Keep count, duration,
   viewport, scaling, template and approximate manual input comparable. These
   human-input records are descriptive, not the automatic 2+10 protocol.

Selecting manual in an existing window requires **Generate/reset** (`GenerateData`)
before Start is enabled. Changing count/scenario invalidates readiness. Controls
that could mutate/reset or programmatically scroll are disabled while capturing.
There are **no ScrollTo, ChangeView, automated scroll, or source mutation commands**
in the capture interval.

Each isolated launch gets a directory under
`CustomAgentLogsTmp\CollectionView2Benchmark\manual-<handler>-<id>`.
`launch.json` identifies the owned PID/configuration/binary hash but does **not**
claim render success. After an explicit Start, the app writes `<runId>.json` and
`<runId>.log` with scenario `manual`, actual handler/native proof, `CVBENCH: START`
and `END`, and status **captured**, **cancelled**, or **failed**, never a scrolling
test `passed` status.

Capture reuses process CPU, unforced managed heap, allocation delta, GC counts,
working set and process-lifetime peak working set. Baseline, **temporal midpoint**
(not source midpoint), end, and approximately **1 Hz** native snapshots include
visible IDs, source count, offset/extent, viewport, realized roots/containers and
the existing 50k bound. `ManualViewportObservations` records actual sample timestamps,
geometry/binding validity and reasons; transient invalid observations are retained.
`manualSampledMaxRealizedRows` and `manualSampledMaxNativeContainers` are maxima of
**samples**, not guaranteed instantaneous peaks. Missed temporal midpoint samples
are explicitly unavailable. `durationMs` is actual elapsed time; requested duration
and overrun are separate, since a blocked UI can postpone timer delivery.

Movement flags, sampled minimum/maximum offsets and first/last-item observations
are evidence only at sampling times. **No observed movement is explicitly NOT a
completed scrolling test**. Movement cannot establish input origin, every traversed
page, source-midpoint visitation, full down/up traversal or absence of transient
blank frames. An unobserved first/last item is not asserted as reached.

The 100-ms queue probe measures **UI queue delivery latency**, not frame time.
Visual-tree sampling, status updates and queue probes contribute CPU/allocation
overhead included in the counters; there is no overhead subtraction. The manual
status/handler rows have fixed heights to avoid countdown-induced viewport resizing.
No presented FPS, frame timing or first-present/loading metric is available in this
scenario. Human input is not repeatable; the batch runner rejects `manual`,
startup rejects `CVBENCH_AUTO=1` with manual, and the batch summarizer refuses manual
manifest entries rather than manufacturing medians or a CV1/CV2 superiority claim.

## Automatic benchmark commands

From the repository root in PowerShell 7:

```powershell
$bench = 'src/Controls/samples/Controls.Sample.Sandbox/Benchmark'
pwsh -File "$bench/Build-Benchmark.ps1" -DotNet "$PWD/.dotnet/dotnet.exe" -Architecture x64
# If this worktree's scoped Release BuildTasks prerequisite has already succeeded:
# pwsh -File "$bench/Build-Benchmark.ps1" -DotNet "$PWD/.dotnet/dotnet.exe" -Architecture x64 -SkipBuildTasks

# Explicitly select the output from THAT Release net11 build, not a stale binary.
# Listing is intentional: RID/output directories depend on the host/build properties.
Get-ChildItem artifacts/bin/Maui.Controls.Sample.Sandbox -Recurse -Filter Maui.Controls.Sample.Sandbox.exe |
    Where-Object FullName -Match 'Release.*net11\.0-windows'
$exe = '<absolute Release net11 Windows Maui.Controls.Sample.Sandbox.exe path>'

# Full comparison: all 8 scenarios, 2 warmups + 10 measured per handler/scenario.
pwsh -File "$bench/Run-Benchmark.ps1" -Exe $exe -Count 10000

# Full-extent scrolling at 50k can take many minutes per process. Do not shorten
# it to one jump and call that sequential scrolling. Allow adequate timeout.
pwsh -File "$bench/Run-Benchmark.ps1" -Exe $exe -Count 50000 -TimeoutSeconds 7200

# Bounded-realization comparison only, still 2+10 fresh processes per handler:
pwsh -File "$bench/Run-Benchmark.ps1" -Exe $exe -Count 50000 -Scenarios rapid

# Reaggregate an existing batch (no app launch):
pwsh -File "$bench/Summarize-Benchmark.ps1" -Directory '<batch directory>' -RequiredRuns 10
```

`Build-Benchmark.ps1` first builds `Microsoft.Maui.BuildTasks.slnf -c Release`,
then builds Sandbox using the Windows build-only recipe from
`.github/scripts/shared/Build-AndDeploy.ps1`, targeting
`net11.0-windows10.0.19041.0` in Release. Both commands explicitly pass:

```text
-p:IncludeAndroidTargetFrameworks=false
-p:IncludeIosTargetFrameworks=false
-p:IncludeMacCatalystTargetFrameworks=false
-p:IncludeTizenTargetFrameworks=false
-p:IncludePreviousTfms=false
-p:UseMaui=false
-p:UseWorkload=false
```

These are command-line global properties, not environment defaults: the repository
assigns some platform flags unconditionally. Unrestricted build-task restore can
request an irrelevant iOS workload on a Windows-only machine. `UseMaui=false` and
`UseWorkload=false` preserve the Sandbox's in-tree MAUI project references.

The higher-level `BuildAndRunSandbox.ps1` *does advertise
Windows support*, but hardcodes `net10.0-windows10.0.19041.0`; it is not correct
for this branch's net11 experiment. Its shared helper cannot forward the additional
global properties, so the narrow Sandbox wrapper uses the same Windows command
recipe with those arguments explicitly added. That Windows path builds without
deploying; the normal workflow expects Appium/WinAppDriver to launch afterward. Here the Sandbox
runner launches a narrow self-driving unpackaged Windows executable instead.
Neither `.github` script is changed; no Android deployment is used.
The Sandbox wrapper retains the helper's warnings-as-errors override; review its full build output
for warnings. Build/deployment/startup are outside every measured interval.

### Temporary short-path workaround (build only)

Deep worktree paths can exceed the OS's 260-character limit while copying
Blazor styles. Use a temporary `subst` drive instead of changing the registry
or requiring administrator access. This invokes the same wrapper and flags:

```powershell
# From the REAL repository root. Never replace/remove an existing V: drive.
$realRoot = $PWD.Path
$subst = Join-Path $env:SystemRoot 'System32/subst.exe'
$log = Join-Path $realRoot 'CustomAgentLogsTmp/CollectionView2Benchmark/sandbox-shortpath-rebuild.log'
if ((Get-PSDrive -Name V -ErrorAction SilentlyContinue) -or
    [IO.Directory]::Exists('V:\') -or
    (@(& $subst) | Where-Object { $_ -match '^V:\\' })) {
    throw 'V: is occupied. Pause; do not replace its mapping.'
}
$createdAlias = $false
try {
    & $subst 'V:' $realRoot
    if ($LASTEXITCODE -ne 0) { throw 'Could not create V: alias.' }
    $createdAlias = $true
    Push-Location 'V:\'
    try {
        # Capture full compiler output without launching the app.
        $output = & pwsh -NoProfile -File `
            'V:\src\Controls\samples\Controls.Sample.Sandbox\Benchmark\Build-Benchmark.ps1' `
            -DotNet 'V:\.dotnet\dotnet.exe' -Architecture x64 -SkipBuildTasks 2>&1
        $buildExit = $LASTEXITCODE
        [IO.Directory]::CreateDirectory((Split-Path $log)) | Out-Null
        [IO.File]::WriteAllLines($log, [string[]]$output)
        $output
        if ($buildExit -ne 0) { throw "Release build failed ($buildExit); see $log. Pause before interactive work." }
    } finally { Pop-Location }
} finally {
    if ($createdAlias) {
        & $subst 'V:' /D
        if ($LASTEXITCODE -ne 0) { Write-Warning 'Temporary V: alias removal failed; report it.' }
    }
}
```

Remove only the alias created by this block, and only after the build process
has completed. Parent-owned `buildtasks.log`, `environment.json`, and the original
`sandbox-shortpath-build.log` are not overwritten. After alias removal, use the
executable's **real worktree path**; generated intermediates may reference `V:`,
so recreate the alias for a subsequent build rather than mixing path roots.

### Manual controls and single-process configuration

Set environment **before** launching the executable:

```powershell
$env:CVBENCH_HANDLER = 'CV1' # or CV2, never toggle in the running app
$env:CVBENCH_AUTO = '0'
$env:CVBENCH_OUTPUT = "$PWD/CustomAgentLogsTmp/CollectionView2Benchmark/manual"
& $exe
```

Pick 1k/10k/50k and scenario. Generate/reset, Run, Cancel, Export, End, Beginning
have AutomationIds `GenerateData`, `RunBenchmark`, `CancelBenchmark`,
`ExportResults`, `ScrollEnd`, `ScrollBeginning`. Pickers are `ItemCount` and
`Scenario`; status values are `BenchmarkStatus` and `HandlerStatus`. The list
is `BenchmarkCollection`; header/footer/empty labels have corresponding
`BenchmarkHeader`, `BenchmarkFooter`, `BenchmarkEmpty` IDs. Only the in-process
auto runner results qualify for comparison; repeated manual runs are not fresh
processes. Each manual run gets its own artifact ID.

Auto mode is configured using `CVBENCH_AUTO=1`, `HANDLER`, `COUNT`, `SEED`,
`RUN`, `WARMUP`, `SCENARIO`, `RUNID`, `OUTPUT` (all prefixed `CVBENCH_`).
The launch script assigns these per process without changing the parent environment.

## Exact handler selection and proof

`MauiProgram.CreateMauiApp()` calls `ConfigureStartup()` before `CreateBuilder`
and `UseMauiApp`. It calls `AppContext.SetSwitch` with
`Microsoft.Maui.RuntimeFeature.IsWindowsCollectionView2HandlerEnabled`.
`src/Core/src/RuntimeFeature.cs` uses `AppContext.TryGetSwitch` and defaults true.
`src/Controls/src/Core/Hosting/AppHostBuilderExtensions.cs` consumes it during
Windows registration, registering `CollectionViewHandler2` or
`CollectionViewHandler`, **not** when a later collection is first instantiated.
There is no live handler replacement.

The getter carries `FeatureSwitchDefinition` on net11. The controls targets
emit a `Trim=true` runtime option when `UseWindowsCollectionView2Handler` is
set, which can constant-fold and remove the other implementation. This Sandbox
uses `PublishTrimmed=false` for Windows and leaves that MSBuild property unset.
Do not override trimming or supply a compile-time feature constant for the
runtime-selection experiment. If separately publishing trimmed builds, use the
documented `UseWindowsCollectionView2Handler=false/true` property in *separate*
output directories and independently verify each binary; that is not this runner's
single-build methodology.

Every run records managed handler type, platform root type, WinUI assembly identity,
and actual native descendant type. CV2 requires `.CollectionViewHandler2`,
an `ItemsRepeater`, and **no** legacy list. CV1 requires
`.CollectionViewHandler`, a `ListViewBase`-derived platform view, and **no**
repeater. Missing/mismatched types fail the run; switch values alone are not proof.

## Data, dimensions, and scenarios

Default 10,000 `ObservableCollection` rows; options 1,000 and 50,000. IDs,
titles, secondary text, seeded color hash (`1729`), alternating backgrounds,
and 24-DIP generated colored box are stable local values. No images, network,
disk data loading, or third-party benchmark package. Window requests 1100×800
DIPs. Fixed rows are 72 DIPs; variable rows cycle 64/80/96/112/128.
Horizontal row width is 320 DIPs. Header/footer are present throughout.
MeasureFirstItem is used for fixed rows, MeasureAllItems for variable rows.

Each process executes exactly one of:

| Scenario | Operations and assertions |
|---|---|
| `initial` | Assign pre-generated source; observe first row and laid-out visible batch, positive native extent; initial memory/CPU snapshots. |
| `sequential` | Native ScrollViewer.ChangeView sustained forward and reverse sweeps at requested 6000 DIPs/sec, minimum 25ms input pacing, each step capped at half a viewport. Dynamic estimated extent followed until actual endpoint. Both endpoint IDs verified. |
| `rapid` | Fixed sequence 0,90,20,100,50,10,80,30,0 percent, three repetitions; native target identity, bindings, ordering, and geometry verified after each jump. |
| `native-positions` (opt-in) | Native ScrollViewer offsets at beginning/midpoint/end/midpoint/beginning with extent refinement; bounded realization and memory experiment, **not** sustained scrolling or MAUI ScrollTo validation. |
| `updates` | Append1000 at end (then make appended last item visible), insert100 near beginning, remove100 there, replace visible row, insert while away at midpoint, observable Clear/reset + adds, return to beginning. |
| `coverage-vertical-fixed` | Vertical/fixed plus header, footer, native single/multiple selection, programmatic ScrollTo, updates while away, EmptyView. |
| `coverage-vertical-variable` | Same coverage with variable heights. |
| `coverage-horizontal-fixed` | Same coverage horizontally/fixed. |
| `coverage-horizontal-variable` | Same coverage horizontally/variable row heights. |

Append and insert latency includes the explicitly recorded programmatic navigation
to changed data, because offscreen updates cannot have visible-update latency
without navigation. Reset is observable `Clear` (Reset) followed by Adds, not a
single bulk Reset notification. Replacement verifies changed title binding, not
just a property in the source. Selection checks the native selection model, not
only MAUI `SelectedItem`. If either handler lacks support, that run fails separately;
it is not silently skipped or included in performance averages.

## Measurement boundaries and reliability

* `initialVisibleLayoutMs`: assigning ItemsSource to two consecutive post-layout
  observations with nonzero native geometry, correctly bound rows intersecting
  the viewport, contiguous row bounds and batch coverage. **Not true first
  presentation latency.** `readyForScrollLayoutMs` additionally checks positive
  native scroll extent. No timer delay is accepted as proof of rendering.
* `durationMs`: the scenario's operations, input pacing, and viewport settling/
  verification. For initial, the initial layout interval including landmark checks.
  It is not a smoothness score. Sequential excludes generation, build, startup,
  Console output, and serialization. Verification overhead is intentionally identical.
* Operation `*VisibleLayoutMs`: operation start to geometry/binding predicates
  passing on two consecutive CompositionTarget callbacks. Rendering callbacks
  provide observation opportunities, **not presented frames or FPS**.
* `cpuMs`: process TotalProcessorTime delta; heap before/after from
  GC.GetTotalMemory(false), allocation delta from GC.GetTotalAllocatedBytes(true),
  Gen0/1/2 collection count deltas, working set before/after. No forced GC.
  `processLifetimePeakWorkingSetBytes` includes startup/setup, not a resettable
  scenario peak. Allocation/CPU include harness assertions and pacing.
* UI responsiveness: a background timer every 100ms enqueues one timestamped UI
  callback via DispatcherQueue.TryEnqueue. At most one outstanding callback.
  Queue-delay raw samples, average/p95/max are exported. This is **UI dispatch
  latency**, not frame time; callbacks delayed past scenario end are discarded.
* Native tree snapshots count all harness row roots and native
  ListViewItem/GridViewItem/ItemContainer counts, total visual tree elements,
  including cached/hidden elements in the tree (not just visible ones), viewport,
  extent, offset, visible IDs and row-height ranges. Viewport assertions separately
  require positive size and visible ancestors. Snapshots at beginning/midpoint/end and return.
  For 50k, realization is required to be ≤ `12 * ceil(viewport / smallestRowExtent)
  + 64` for both row roots and native containers. This predeclared generous cache
  allowance is relative to viewport, not dataset size. Total visual descendants
  are descriptive (rich rows have multiple descendants). Different IDs visible
  at widely separated offsets with bounded counts are recycling/realization
  evidence, not proof of allocations being recycled rather than recreated.
* Settled viewport checks cover binding identity/text against the **native container
  index in the current source**, duplicate IDs, contiguous native geometry and
  actual source order. Legacy ItemsStackPanel/ItemsWrapGrid visible indices and
  repeater active-element indices disambiguate cached roots with stale transforms.
  Cached/hidden roots are still included in realization counts.
  `InvalidLayoutObservations` retains
  transient blank/gap/stale-binding callback observations during initial/scroll
  settling. They are **not** presented-frame failures; permanent failures time
  out/fail separately. Transient observations warrant trace investigation.

No per-frame Console logging. Source-order traversal is done after settling and at
landmarks, not on every native callback. The viewport tree probe is itself
instrumentation overhead; use the same harness for both handlers.

### Explicitly unavailable metrics

True first-present latency, average/p95/p99/max *presented* frame time, effective
FPS, frames above 16.67/33.33ms and their percentages, and blank **presented**
frames are `NotMeasured`, never estimated. This implementation does not parse
ETW or claim callback cadence proves smoothness. The visual probes cannot catch
every transient missing frame, selection appearance, pixel corruption, or incorrect
color. Heap and allocation counters are managed-only.

For reliable presentation/smoothness analysis, collect WPR/PerfView/ETW on the
same unlocked desktop using a validated WinUI/DWM/Present tracing profile,
with process IDs and `CVBENCH` interval markers correlated to actual Present
events. Store ETL/profile/tool versions and derived raw event tables in the
batch directory. Do not substitute a generic WPR CPU profile for presentation
evidence. Trace collection/parsing is deliberately not automated here; no trace
files are claimed to exist. ETW allocation profiling is optional corroboration
of managed counters. Binding/layout-cycle diagnostics are not exhaustively
observable here; inspect build warnings, stderr/debug output and traces.

## Repetition, cancellation, and artifacts

The runner checks SDK/workload and usable input desktop, records OS/CPU/logical
cores/RAM/architecture/primary resolution, executable SHA256 and workload list.
Each raw result records actual XamlRoot scaling, window and viewport dimensions
and WinUI assembly identity. Successful startup provides runtime-use evidence.
Keep machine/display/window/power settings and input sequence unchanged.
No debugger. Avoid competing workloads, minimize thermal drift, keep the window
visible/unlocked, and do not interact with the list during a run.
Apphost runtime discovery uses `DOTNET_ROOT` and the architecture-specific
`DOTNET_ROOT_X64`/`DOTNET_ROOT_ARM64` **in the child only**, selecting the local
already-installed runtime beside `-DotNet`. `global.json` SDK paths alone do not
configure apphost runtime discovery. No machine/session environment is changed.
`-StopOnInvalid` pauses further launches after preserving the first failed run;
use it during interactive validation. The manifest also captures the managed
assembly SHA256 for new batches (apphost SHA256 alone does not fingerprint code).

For each scenario, execute two fresh-process warmups per handler followed by ten
fresh-process measurements per handler. Odd-numbered pairs CV1→CV2, even pairs
CV2→CV1. The runner never launches handlers concurrently and waits for exit.
It validates configuration/process ID, expected native proof, start/end markers,
positive metrics, functional checks, geometry invariants, stderr errors, and
50k bounds. Timeout, crash, cancellation, missing export, and functional errors
are excluded individually. There is no successful-run replacement or hidden retry.

Cancel with the in-app button, create the batch cancellation file printed by the
runner, or Ctrl+C. The runner writes a per-run cancellation file, allows 20 seconds
for graceful export/exit, then terminates **only its owned PID** if necessary.
The app exports cancellation/failures separately. If killed/crashed before export,
manifest records missing raw data; no measurements are invented. Exit codes:
0 success, 1 scenario failure, 2 cancellation, 3 export failure (runner also validates
JSON status). Runtime callback and outer-run exceptions are recorded, never
silently swallowed. A batch interruption retains the partial manifest; reaggregate
it manually and treat incomplete comparisons as inconclusive.

Artifacts under the already-gitignored
`CustomAgentLogsTmp/CollectionView2Benchmark/<batch>/`:

* `environment.json`, `manifest.json` (launch/validation/failure provenance)
* `<scenario>-<phase>-<run>-<handler>.json` (raw metrics, checks, observations,
  failures, unavailable reasons, native proof, realization and queue samples)
* matching `.log` (CVBENCH start/end) and `.stdout.log` / `.stderr.log`
* `failures.json` (separate from aggregates), `coverage-report.json` (all expected
  scenario/handler repetitions, including missing results), `summary.json`,
  `not-measured.json` (per-run unavailable metric reasons)
* optional externally collected `.etl`/profiles/derived event tables

Export is outside measurement, uses temp-file + rename for result JSON. Manual
export rewrites the same last-run result; it does not repeat the measurement.

## Aggregation and interpretation

Only manifest-validated, passed, measured runs are included. Warmups and failed
rendering/functional runs are never averaged. Each available metric reports CV1/
CV2 successful N, median, interpolated across-run p95, IQR, median absolute
difference and `((CV2 - CV1) / CV1) * 100`; zero CV1 yields null percentage.
Raw responsiveness samples remain available; across-run p95 of the per-run p95
is not a frame distribution.

Paired direction consistency is the fraction of same-run-number pairs with the
sign of the median difference. Complete comparisons with ≥80% paired agreement
and difference larger than either IQR are described as consistently lower/higher
observed values. Complete comparisons within 5% median difference are tagged
approximately equivalent **descriptively only**, not statistical equivalence.
Otherwise inconclusive. These are predeclared descriptive rules, not hypothesis
tests. Ten samples give a weak tail estimate. Missing/failed runs make inference
inconclusive, including cases where only one handler passes rendering.

Lower latency/CPU/allocations/working set is generally preferable but contextual;
heap/GC count/lifetime peak have caveats and GC counts are not an intrinsic
better/worse score. Do not call CV2 universally better, infer smoothness from
duration alone, or mix functional failures into performance medians. Investigate
transient invalid layout observations even when settling eventually passes.

## Handoff status

Parent-reported environment (2026-09-30): Windows 11 Enterprise 10.0.26100 x64,
Intel i5-1335U, 12 logical processors, 16,393,144 KiB RAM (~15.63 GiB),
1920×1200 physical display at DPI 120 (125% scaling), interactive session 2,
and matching x64 Windows App Runtime 2.3.1. Screen APIs may report DPI-virtualized
1536×960; do not label that as physical resolution. Pinned SDK and `maui-windows`
setup succeeded. WPR is available; PerfView/PresentMon are absent. These are
environment facts supplied by the parent, not measured benchmark results or
permission to start interactive work.

The parent subsequently reported the Windows-scoped prerequisite build succeeded
(188 pre-existing warnings, zero errors, 5m23), with parent-owned
`CustomAgentLogsTmp/CollectionView2Benchmark/buildtasks.log` and `environment.json`.
The input-desktop probe succeeded without elevation. WPR DesktopComposition/GPU/XAML
profiles are present, but no trace was started; elevation remains a separately
coordinated optional step. `-SkipBuildTasks` avoids repeating that completed
prerequisite. Do not overwrite the parent-owned logs/environment capture.

Release compilation subsequently succeeded through the temporary `V:` alias:
exit 0, 94 WMC1510 warnings in existing Controls XAML, zero errors, compiler elapsed
3m58.26s (wrapper wall time ~4m37s). No diagnostics matched the harness files.
Full output is preserved in
`CustomAgentLogsTmp/CollectionView2Benchmark/sandbox-shortpath-rebuild.log`.
The created `V:` alias was removed and its absence verified. No application
launch or desktop measurement was performed.

The built executable, relative to the **real worktree root**, is:

```text
artifacts\bin\Maui.Controls.Sample.Sandbox\Release\net11.0-windows10.0.19041.0\win-x64\Maui.Controls.Sample.Sandbox.exe
```

Compilation is not a rendering/measurement pass. No measured comparison, native
handler proof, virtualization verdict, or ETW result is claimed until the parent
coordinates execution on the interactive host. Report scenario-specific results,
failures, warnings, native snapshots, raw paths, and unavailable metrics afterward.

### Actual comparison follow-up (2026-09-30)

App execution was subsequently authorized. Complete **initial** comparisons were
captured at 10k and 50k: 10 measured successes and 2 warmups per handler/count.
Selected batches are `205543776dbd4438aaa15124d592a161` (10k) and
`89b90a19bf97484b9fb931a9aa5e7b94` (50k), under the ignored artifact root.
`comparison-selected-initial.json` / `.md` there contain the actual comparison
table and native initial snapshots. These completed batches used identical rich
templates for both handlers within each count; they precede the final diagnostic
rebuild. Do not pool their numbers with subsequent or superseded batches.

Loading medians were lower for CV2, but the differences were within observed
run-to-run IQRs and the predeclared interpretation is inconclusive. CV2 had
slightly higher managed heap/allocation deltas after layout in these batches.
50k initial realized counts were bounded; **midpoint/end virtualization and
scrolling comparisons did not complete** and must not be claimed to have passed.

Rapid/native-position attempts hit target/source-order assertions; failed runs
remain separate and are never averaged. Diagnostic instrumentation was tightened
to distinguish cached roots and to wait for binding/source/native-index agreement
inside the layout predicate, rather than asserting source order only afterward.
The final retry (`27d1d7eb66ae45928c527914393840bb`) then stalled before any
CVBENCH marker or JSON export: 90-second launch timeout, 20-second cancellation
grace, runner terminated its owned PID 5768, exit -1, empty stdout/stderr.
Further app launches were paused; the exact bootstrap/environment cause is not
established and the final native-binding changes have no completed runtime result.
Do not describe that timeout as a measured handler performance regression.

Before retrying, coordinate with the parent/user to verify the unlocked desktop
and inspect any native bootstrap dialog. Preserve that batch's manifest/logs;
do not install SDKs, change registry settings, elevate tracing, or weaken failed
assertions. Once the desktop startup issue is resolved, reproduce only the initial
comparison first, using the child-runtime-aware runner:

```powershell
$exe = Join-Path $PWD 'artifacts\bin\Maui.Controls.Sample.Sandbox\Release\net11.0-windows10.0.19041.0\win-x64\Maui.Controls.Sample.Sandbox.exe'
$runner = 'src/Controls/samples/Controls.Sample.Sandbox/Benchmark/Run-Benchmark.ps1'
& $runner -Exe $exe -Count 10000 -Scenarios @('initial') -TimeoutSeconds 90 -StopOnInvalid
& $runner -Exe $exe -Count 50000 -Scenarios @('initial') -TimeoutSeconds 90 -StopOnInvalid
# Only after startup/initial validation succeeds, and with parent/user coordination:
# & $runner -Exe $exe -Count 50000 -Scenarios @('native-positions') -TimeoutSeconds 90 -StopOnInvalid
```
