#if WINDOWS
using System.Globalization;

namespace Maui.Controls.Sample;

internal sealed class CollectionViewBenchmarkOptions
{
	public const string SwitchName = "Microsoft.Maui.RuntimeFeature.IsWindowsCollectionView2HandlerEnabled";
	public static CollectionViewBenchmarkOptions Current { get; private set; } = new();
	public string Handler { get; init; } = "CV2";
	public int Count { get; init; } = 10000;
	public int Seed { get; init; } = 1729;
	public int Run { get; init; } = 1;
	public bool Warmup { get; init; }
	public bool Auto { get; init; }
	public int ManualSeconds { get; init; } = 30;
	public string Scenario { get; init; } = "initial";
	public string RunId { get; init; } = Guid.NewGuid().ToString("N");
	public string Output { get; init; } = Path.Combine(Environment.CurrentDirectory, "CustomAgentLogsTmp", "CollectionView2Benchmark");
	public string CancelFile => Path.Combine(Output, RunId + ".cancel");
	public static readonly string[] Scenarios = { "initial", "sequential", "rapid", "native-positions", "manual", "updates",
		"coverage-vertical-fixed", "coverage-vertical-variable", "coverage-horizontal-fixed", "coverage-horizontal-variable" };

	public static void ConfigureStartup()
	{
		static string Read(string name, string fallback) => Environment.GetEnvironmentVariable("CVBENCH_" + name) ?? fallback;
		static int Number(string name, int fallback) => int.Parse(Read(name, fallback.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
		Current = new()
		{
			Handler = Read("HANDLER", "CV2"), Count = Number("COUNT", 10000), Seed = Number("SEED", 1729),
			Run = Number("RUN", 1), Warmup = Read("WARMUP", "0") == "1", Auto = Read("AUTO", "0") == "1",
			ManualSeconds = Number("MANUAL_SECONDS", 30),
			Scenario = Read("SCENARIO", "initial"), RunId = Read("RUNID", Guid.NewGuid().ToString("N")),
			Output = Path.GetFullPath(Read("OUTPUT", Path.Combine(Environment.CurrentDirectory, "CustomAgentLogsTmp", "CollectionView2Benchmark")))
		};
		if (Current.Handler is not ("CV1" or "CV2") || Current.Count is not (1000 or 10000 or 50000) ||
			Array.IndexOf(Scenarios, Current.Scenario) < 0 || Path.GetFileName(Current.RunId) != Current.RunId)
			throw new ArgumentException("Invalid CVBENCH configuration.");
		if (Current.ManualSeconds is < 5 or > 120)
			throw new ArgumentException("CVBENCH_MANUAL_SECONDS must be between 5 and 120.");
		if (Current.Auto && Current.Scenario == "manual")
			throw new ArgumentException("Manual capture requires an explicit Start manual capture click; CVBENCH_AUTO=1 is not allowed.");
		// Must precede CreateBuilder/UseMauiApp: AppHostBuilderExtensions consumes this
		// getter during handler registration. Never toggle it for a running page.
		AppContext.SetSwitch(SwitchName, Current.Handler == "CV2");
	}
}
#endif
