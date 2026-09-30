# Windows CollectionView: measured initial-layout and memory comparison

Measured on September 30, 2026. This report covers initial population only.
It does **not** establish a scrolling, smoothness, or overall performance winner.

## Environment and methodology

| Setting | Value |
|---|---|
| OS | Windows 11 Enterprise, 10.0.26100, x64 |
| CPU | Intel Core i5-1335U, 12 logical processors |
| OS-visible physical memory | Approximately 15.63 GiB |
| SDK/runtime | .NET SDK 11.0.100-rc.2.26470.103; .NET runtime 11.0.0-rc.2.26470.103 |
| Native runtime | Windows App Runtime 2.3.1 x64 |
| Configuration | Release, x64, no debugger |
| Display | 1920 x 1200 physical resolution, 125% scaling |
| Application window | 1100 x 800 DIPs |
| Native viewport | Approximately 1069.6 x 581.6 DIPs |
| Dataset sizes | 10,000 and 50,000 items |
| Repetitions | Two warmups and ten measured fresh processes per handler and item count |
| Run order | Alternating CV1/CV2 order |

There are 40 successful measured initial-population runs and eight warmups.
Warmups, failed scrolling attempts, and superseded batches are excluded.
Each handler pair within a dataset size used the same binary and item template.
Actual native types were verified in every included run:

- CV1: `CollectionViewHandler` with `FormsListView`.
- CV2: `CollectionViewHandler2` with `ItemsRepeater`.

The startup AppContext switch is configured before handler registration. See
[README.md](README.md) for build, configuration, and reproduction commands.

## Complete item template

The generated `ObservableCollection` data uses seed `1729`, deterministic IDs,
titles, secondary values, and colors. Both handlers use this identical structure:

```text
CollectionView
  Header
  Item template: Grid, fixed height 72 DIPs
    Padding: 8; column spacing: 12
    Columns: 32 | 64 | remaining width
    Column 0: generated colored BoxView, 24 x 24 DIPs
    Column 1: ID label, font size 12
    Column 2: VerticalStackLayout, spacing 2
      Title label, font size 16, bold
      Secondary label, font size 12, maximum two lines
    Alternating backgrounds: #E8EDF4 / #F8FAFC
  Footer
```

There are no network requests, external images, or disk-loaded item data.

## Results

Values are median / interpolated across-run p95. Memory is in MiB.
The difference is the CV2 median minus the CV1 median; the percentage is
`((CV2 - CV1) / CV1) * 100`, calculated before rounding.
Lower latency, allocation, and footprint are generally preferable, subject to
the measurement boundaries below. GC counts alone are not a better/worse score.

| Items | Metric | CV1 median / p95 | CV2 median / p95 | Median difference | Difference % |
|---:|---|---:|---:|---:|---:|
| 10,000 | Initial visible layout, ms | 199.68 / 243.00 | 181.99 / 217.30 | -17.70 | -8.86% |
| 10,000 | Ready-for-scroll layout, ms | 199.69 / 243.01 | 181.99 / 217.30 | -17.70 | -8.86% |
| 10,000 | Initial scenario duration, ms | 206.23 / 249.20 | 189.12 / 225.49 | -17.11 | -8.30% |
| 10,000 | Process CPU delta, ms | 234.38 / 265.63 | 250.00 / 296.88 | +15.63 | +6.67% |
| 10,000 | Managed heap before, MiB | 5.368 / 5.372 | 5.405 / 5.412 | +0.038 | +0.70% |
| 10,000 | Managed heap after, MiB | 8.575 / 8.584 | 8.732 / 8.765 | +0.157 | +1.84% |
| 10,000 | Allocation delta, MiB | 3.202 / 3.214 | 3.316 / 3.372 | +0.114 | +3.56% |
| 10,000 | Working set before, MiB | 231.086 / 231.963 | 221.258 / 222.047 | -9.828 | -4.25% |
| 10,000 | Working set after, MiB | 246.182 / 247.156 | 247.727 / 248.089 | +1.545 | +0.63% |
| 10,000 | Lifetime peak working set, MiB | 246.604 / 249.291 | 252.338 / 253.312 | +5.734 | +2.33% |
| 50,000 | Initial visible layout, ms | 236.60 / 275.89 | 217.41 / 249.26 | -19.19 | -8.11% |
| 50,000 | Ready-for-scroll layout, ms | 236.61 / 275.89 | 217.42 / 249.26 | -19.19 | -8.11% |
| 50,000 | Initial scenario duration, ms | 241.18 / 280.36 | 223.59 / 254.52 | -17.60 | -7.30% |
| 50,000 | Process CPU delta, ms | 281.25 / 305.47 | 281.25 / 352.34 | 0.00 | 0.00% |
| 50,000 | Managed heap before, MiB | 19.726 / 19.755 | 19.752 / 19.789 | +0.026 | +0.13% |
| 50,000 | Managed heap after, MiB | 27.676 / 27.683 | 28.546 / 28.572 | +0.870 | +3.14% |
| 50,000 | Allocation delta, MiB | 11.296 / 11.306 | 11.715 / 11.778 | +0.419 | +3.71% |
| 50,000 | Working set before, MiB | 242.967 / 247.521 | 232.611 / 233.587 | -10.355 | -4.26% |
| 50,000 | Working set after, MiB | 266.344 / 266.887 | 265.184 / 269.338 | -1.160 | -0.44% |
| 50,000 | Lifetime peak working set, MiB | 266.990 / 267.838 | 270.254 / 270.981 | +3.264 | +1.22% |

For both handlers, median and p95 Gen0/Gen1/Gen2 collection deltas were `0/0/0`
at 10,000 items and `2/2/1` at 50,000 items.

### Consistency and interpretation

- CV2 initial-layout latency was lower in nine of ten paired 10,000-item runs
  and six of ten paired 50,000-item runs. The 18-19 ms median advantage was
  smaller than the observed 28-48 ms interquartile spreads. A reliable loading
  improvement is **not established**.
- CV2 post-layout managed heap and allocation delta were higher in every pair,
  by approximately 1.8-3.1% and 3.6-3.7%, respectively.
- CV2 post-layout working set was higher in nine of ten 10,000-item pairs, but
  lower in only five of ten 50,000-item pairs. The differences are small and
  do not establish an overall process-memory advantage.
- CV2 lifetime peak working set was higher in every pair. This peak includes
  startup and data generation, not just the measured layout interval.

## Initial virtualization evidence

Across the ten measured 50,000-item initial runs for each handler:

| Observation | CV1 | CV2 |
|---|---:|---:|
| Source items | 50,000 | 50,000 |
| Realized row roots | 9 | 10 |
| Native containers | 10 | 10 |
| Visible IDs | 0-7 | 0-7 |

Both demonstrate bounded **initial** realization relative to the viewport.
Neither result establishes midpoint/end recycling, realization stability after
repeated scrolling, or a smoothness advantage. Both handlers support UI
virtualization; the full generated data remains in memory.

## Failures, unavailable evidence, and limitations

- Initial-layout timing excludes process startup and data generation. It measures
  validated visible layout, **not** first presentation on screen.
- Managed heap readings use `GC.GetTotalMemory(false)`, not forced-GC retained
  memory. Allocation and CPU deltas include instrumentation overhead.
- Working set includes managed, native, and framework memory. Lifetime peak is
  not a resettable scenario peak.
- Ten samples provide a weak p95 estimate. Results apply to this template,
  machine, and configuration, not all CollectionView applications.
- Selected initial runs passed the harness's native handler, visible-layout,
  and binding/source-order checks; no invalid-layout observations were recorded.
  This is not exhaustive binding/layout-cycle diagnostic coverage.
- Rapid/native-position attempts failed target/source-order validation.
  There are zero valid measured scrolling runs; failed attempts are not pooled.
- A later diagnostic launch timed out without a benchmark marker or JSON export.
  Its startup cause is unresolved, and it is not a measured handler regression.
- The successful loading batches precede later diagnostic and manual-capture
  changes. The latest build compiles but has no completed runtime verification.
- Presented FPS, frame-time distributions, blank presented frames, and
  first-present latency are **not measured**. No ETW trace was collected.
- Manual-scrolling capture support is available, but no manual capture has
  been performed.

## Raw evidence provenance

Raw logs and machine-specific JSON remain local in the gitignored
`CustomAgentLogsTmp\CollectionView2Benchmark\` directory; they are not included
in this pull request.

| Artifact | Purpose |
|---|---|
| `205543776dbd4438aaa15124d592a161\` | Selected 10,000-item initial batch |
| `89b90a19bf97484b9fb931a9aa5e7b94\` | Selected 50,000-item initial batch |
| `comparison-selected-initial.json` | Aggregates, sample counts, variability, and native snapshots |
| `comparison-selected-initial.md` | Original full aggregate table |
| `comparison-failed-attempts.json` | Excluded failure evidence |

This committed report preserves the observed aggregates and their limitations.
Independent reproduction requires rerunning the harness on an interactive
Windows host after resolving the startup issue.
