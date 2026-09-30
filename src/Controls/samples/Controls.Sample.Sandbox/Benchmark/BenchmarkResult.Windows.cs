#if WINDOWS
using System.Diagnostics;

namespace Maui.Controls.Sample;

internal sealed class BenchmarkResult
{
	public int SchemaVersion { get; } = 1;
	public CollectionViewBenchmarkOptions Configuration { get; init; } = CollectionViewBenchmarkOptions.Current;
	public string Status { get; set; } = "running";
	public string? HandlerType { get; set; }
	public string? PlatformType { get; set; }
	public string? NativeItemsType { get; set; }
	public string? WinUIAssembly { get; set; }
	public int ProcessId { get; } = Environment.ProcessId;
	public string Architecture { get; } = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString();
	public string OS { get; } = System.Runtime.InteropServices.RuntimeInformation.OSDescription;
	public string Runtime { get; } = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
	public bool DebuggerAttached { get; } = Debugger.IsAttached;
	public DateTime StartedUtc { get; } = DateTime.UtcNow;
	public double RasterizationScale { get; set; }
	public double WindowWidth { get; set; }
	public double WindowHeight { get; set; }
	public double ViewportWidth { get; set; }
	public double ViewportHeight { get; set; }
	public Dictionary<string, double> Metrics { get; } = new();
	public Dictionary<string, string> NotMeasured { get; } = new()
	{
		["firstPresentLatencyMs"] = "No ETW present event correlation; initialVisibleLayoutMs is layout only.",
		["frameTimeAverageMs,p95Ms,p99Ms,maxMs,effectiveFps,over16_67Ms,over33_33Ms"] =
			"No reliable presentation trace. CompositionTarget.Rendering callbacks are NOT presented frames or FPS.",
		["blankPresentedFrames"] = "Sampled native viewport checks cannot detect every transient blank presented frame.",
		["bindingDiagnostics,layoutCycleDiagnostics"] = "No guaranteed exhaustive MAUI diagnostic source in this harness; inspect stderr/debug output and optional ETW trace."
	};
	public List<string> Failures { get; } = new();
	public List<string> Checks { get; } = new();
	public List<NativeSample> Realization { get; } = new();
	public List<ManualViewportObservation> ManualViewportObservations { get; } = new();
	public List<double> ResponsivenessMs { get; } = new();
	public List<string> Logs { get; } = new();
	public Dictionary<string, int> InvalidLayoutObservations { get; } = new();
}

internal sealed record ManualViewportObservation(string Stage, double ElapsedMs, bool BindingAndGeometryValid, string Reason);

internal sealed record NativeSample(string Stage, int RealizedRows, int NativeContainers, int VisualElements,
	int VisibleRows, double ViewportWidth, double ViewportHeight, double Offset, double Extent, int Bound,
	int SourceCount, int? FirstVisibleId, int? LastVisibleId, double MinimumVisibleRowHeight, double MaximumVisibleRowHeight,
	int[] VisibleIds, int NativeFirstVisibleIndex, int NativeLastVisibleIndex, int ExcludedCachedRows);

internal readonly record struct ProcessSnapshot(double CpuMs, long Heap, long Allocated, long WorkingSet, int Gen0, int Gen1, int Gen2)
{
	public static ProcessSnapshot Take()
	{
		using var process = Process.GetCurrentProcess();
		process.Refresh();
		return new(process.TotalProcessorTime.TotalMilliseconds, GC.GetTotalMemory(false), GC.GetTotalAllocatedBytes(true),
			process.WorkingSet64, GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
	}
	public void Finish(BenchmarkResult result)
	{
		var after = Take();
		result.Metrics["cpuMs"] = after.CpuMs - CpuMs;
		result.Metrics["managedHeapBeforeBytes"] = Heap;
		result.Metrics["managedHeapAfterBytes"] = after.Heap;
		result.Metrics["allocationDeltaBytes"] = after.Allocated - Allocated;
		result.Metrics["workingSetBeforeBytes"] = WorkingSet;
		result.Metrics["workingSetAfterBytes"] = after.WorkingSet;
		using var process = Process.GetCurrentProcess();
		result.Metrics["processLifetimePeakWorkingSetBytes"] = process.PeakWorkingSet64;
		result.Metrics["gen0Count"] = after.Gen0 - Gen0;
		result.Metrics["gen1Count"] = after.Gen1 - Gen1;
		result.Metrics["gen2Count"] = after.Gen2 - Gen2;
	}
}
#endif
