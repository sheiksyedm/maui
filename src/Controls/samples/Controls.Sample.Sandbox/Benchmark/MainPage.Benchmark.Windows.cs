#if WINDOWS
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;

namespace Maui.Controls.Sample;

public partial class MainPage
{
	readonly CollectionView _collection = new() { AutomationId = "BenchmarkCollection", SelectionMode = SelectionMode.None };
	readonly Label _status = new() { AutomationId = "BenchmarkStatus", Text = "Ready" };
	readonly Label _handlerStatus = new() { AutomationId = "HandlerStatus" };
	readonly Picker _countPicker = new() { AutomationId = "ItemCount", Title = "Items" };
	readonly Picker _scenarioPicker = new() { AutomationId = "Scenario", Title = "Scenario" };
	readonly HorizontalStackLayout _controls = new() { Spacing = 6 };
	Button _runButton = null!;
	bool _manualPrepared;
	readonly Dictionary<int, BenchmarkItem> _expected = new();
	readonly Label _header = new() { Text = "CVBENCH header", HeightRequest = 40, WidthRequest = 180, AutomationId = "BenchmarkHeader" };
	readonly Label _footer = new() { Text = "CVBENCH footer", HeightRequest = 40, WidthRequest = 180, AutomationId = "BenchmarkFooter" };
	readonly Label _empty = new() { Text = "No benchmark rows", AutomationId = "BenchmarkEmpty" };
	ObservableCollection<BenchmarkItem> _items = new();
	CancellationTokenSource? _cancel;
	BenchmarkResult? _result;
	NativeViewportProbe? _probe;
	DispatcherQueue Queue => ((Microsoft.UI.Xaml.FrameworkElement)Handler!.PlatformView!).DispatcherQueue;
	bool _startedAutomatically;
	bool _variable;
	bool _horizontal;
	int _count = CollectionViewBenchmarkOptions.Current.Count;
	string SelectedScenario => _scenarioPicker.SelectedItem is string scenario ? scenario
		: throw new InvalidOperationException("Select a benchmark scenario.");
	int SelectedCount => _countPicker.SelectedItem is string count ? int.Parse(count, System.Globalization.CultureInfo.InvariantCulture)
		: throw new InvalidOperationException("Select a benchmark item count.");

	void InitializeBenchmark()
	{
		Title = "Windows CollectionView empirical benchmark";
		_handlerStatus.Text = "Requested " + CollectionViewBenchmarkOptions.Current.Handler + " (fresh process only)";
		_countPicker.Items.Add("1000"); _countPicker.Items.Add("10000"); _countPicker.Items.Add("50000");
		_countPicker.SelectedIndex = _count == 1000 ? 0 : _count == 50000 ? 2 : 1;
		for (int i = 0; i < CollectionViewBenchmarkOptions.Scenarios.Length; i++)
			_scenarioPicker.Items.Add(CollectionViewBenchmarkOptions.Scenarios[i]);
		_scenarioPicker.SelectedIndex = Array.IndexOf(CollectionViewBenchmarkOptions.Scenarios, CollectionViewBenchmarkOptions.Current.Scenario);
		var controls = _controls;
		controls.Add(_countPicker); controls.Add(_scenarioPicker);
		controls.Add(Button("Generate/reset", "GenerateData", GenerateClicked));
		_runButton = Button("Run", "RunBenchmark", RunClicked);
		controls.Add(_runButton);
		controls.Add(Button("Cancel", "CancelBenchmark", CancelClicked));
		controls.Add(Button("Export", "ExportResults", ExportClicked));
		controls.Add(Button("End", "ScrollEnd", EndClicked));
		controls.Add(Button("Beginning", "ScrollBeginning", BeginningClicked));
		var grid = new Grid { Padding = 8, RowSpacing = 6 };
		grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
		grid.Add(controls); grid.Add(_handlerStatus, 0, 1); grid.Add(_status, 0, 2); grid.Add(_collection, 0, 3);
		Content = grid;
		_collection.EmptyView = _empty;
		Loaded += BenchmarkLoaded;
		Unloaded += BenchmarkUnloaded;
		_scenarioPicker.SelectedIndexChanged += ConfigurationChanged;
		_countPicker.SelectedIndexChanged += ConfigurationChanged;
		UpdateRunButton();
	}
	static Button Button(string text, string id, EventHandler clicked)
	{
		var button = new Button { Text = text, AutomationId = id, FontSize = 12, Padding = 6 };
		button.Clicked += clicked;
		return button;
	}
	async void BenchmarkLoaded(object? sender, EventArgs e)
	{
		if (CollectionViewBenchmarkOptions.Current.Scenario == "manual" && !_startedAutomatically)
		{
			_startedAutomatically = true;
			await PrepareManualAsync();
			return;
		}
		if (!CollectionViewBenchmarkOptions.Current.Auto || _startedAutomatically) return;
		_startedAutomatically = true;
		await RunBenchmarkAsync();
	}
	void BenchmarkUnloaded(object? sender, EventArgs e) => _cancel?.Cancel();
	void CancelClicked(object? sender, EventArgs e) => _cancel?.Cancel();
	async void GenerateClicked(object? sender, EventArgs e)
	{
		if (_cancel != null) return;
		if (_scenarioPicker.SelectedItem?.ToString() == "manual")
		{
			await PrepareManualAsync();
			return;
		}
		_manualPrepared = false;
		_count = SelectedCount;
		ConfigureLayout(SelectedScenario);
		_items = Generate(_count);
		_collection.ItemsSource = _items;
		_status.Text = "Generated " + _count;
	}
	void ConfigurationChanged(object? sender, EventArgs e)
	{
		_manualPrepared = false;
		UpdateRunButton();
		if (_scenarioPicker.SelectedItem?.ToString() == "manual")
			_status.Text = "Manual: press Generate/reset to prepare; recording starts only on Start manual capture.";
	}
	void UpdateRunButton()
	{
		bool manual = _scenarioPicker.SelectedItem?.ToString() == "manual";
		_runButton.Text = manual ? "Start manual capture" : "Run";
		_runButton.IsEnabled = _cancel == null && (!manual || _manualPrepared);
		_status.HeightRequest = manual ? 72 : -1;
		_handlerStatus.HeightRequest = manual ? 48 : -1;
	}
	void SetControlsEnabled(bool enabled)
	{
		foreach (var child in _controls.Children)
			if (child is VisualElement element && element.AutomationId != "CancelBenchmark")
				element.IsEnabled = enabled;
		UpdateRunButton();
	}
	async Task PrepareManualAsync()
	{
		if (_cancel != null) return;
		_cancel = new CancellationTokenSource();
		_manualPrepared = false;
		_result = null;
		SetControlsEnabled(false);
		var token = _cancel.Token;
		try
		{
			_count = SelectedCount;
			ConfigureLayout("manual");
			_items = Generate(_count);
			_status.Text = "Preparing manual rows and native handler; NOT recording.";
			await WaitLayoutAsync(() => _collection.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement native && native.IsLoaded &&
				NativeViewportProbe.Find<Microsoft.UI.Xaml.Controls.ScrollViewer>(native) != null, token);
			_probe = new(_collection, _expected) { Horizontal = false, Source = _items };
			var proof = new BenchmarkResult();
			_probe.Attach(proof, CollectionViewBenchmarkOptions.Current.Handler);
			_handlerStatus.Text = proof.HandlerType + " / " + proof.NativeItemsType;
			_collection.ItemsSource = _items;
			await WaitLayoutAsync(() => _probe.Valid(out _) && _probe.Has(_items[0].Id), token);
			_probe.VerifyOrder(_items);
			_manualPrepared = true;
			_status.Text = $"{CollectionViewBenchmarkOptions.Current.Handler}: ready, {_count} rows. Click Start manual capture when ready; {CollectionViewBenchmarkOptions.Current.ManualSeconds}s, then scroll down/up.";
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			_status.Text = "Manual preparation cancelled; no capture started.";
		}
		catch (Exception error)
		{
			_status.Text = "Manual preparation failed: " + error.Message;
			Console.Error.WriteLine(error);
		}
		finally
		{
			_cancel.Dispose(); _cancel = null;
			SetControlsEnabled(true);
		}
	}
	async void RunClicked(object? sender, EventArgs e) => await RunBenchmarkAsync();
	async void ExportClicked(object? sender, EventArgs e)
	{
		if (_cancel != null || _result == null) return;
		try { await ExportAsync(); _status.Text = "Exported " + CollectionViewBenchmarkOptions.Current.Output; }
		catch (Exception error) { _status.Text = "Export failed: " + error.Message; Console.Error.WriteLine(error); }
	}
	void EndClicked(object? sender, EventArgs e)
	{
		if (_cancel == null && _items.Count > 0) _collection.ScrollTo(_items[^1], position: ScrollToPosition.End, animate: false);
	}
	void BeginningClicked(object? sender, EventArgs e)
	{
		if (_cancel == null && _items.Count > 0) _collection.ScrollTo(_items[0], position: ScrollToPosition.Start, animate: false);
	}
	void ConfigureLayout(string scenario)
	{
		_horizontal = scenario.Contains("horizontal", StringComparison.Ordinal);
		_variable = scenario.Contains("variable", StringComparison.Ordinal);
		_collection.ItemsLayout = new LinearItemsLayout(_horizontal ? ItemsLayoutOrientation.Horizontal : ItemsLayoutOrientation.Vertical) { ItemSpacing = 0 };
		_collection.ItemSizingStrategy = _variable ? ItemSizingStrategy.MeasureAllItems : ItemSizingStrategy.MeasureFirstItem;
		_collection.ItemTemplate = new DataTemplate(() => new BenchmarkRow { WidthRequest = _horizontal ? 320 : -1 });
		_collection.Header = _header;
		_collection.Footer = _footer;
	}
	BenchmarkItem MakeItem(int id, string suffix = "")
	{
		// Stateless seeded hash keeps item values independent of operation/run order.
		uint hash = unchecked((uint)(id * 1103515245 + CollectionViewBenchmarkOptions.Current.Seed));
		return new(id, $"Row {id:D6}{suffix}", $"Stable local data • seed {CollectionViewBenchmarkOptions.Current.Seed} • value {hash % 100000}",
			Color.FromRgb((byte)(hash >> 16), (byte)(hash >> 8), (byte)hash),
			id % 2 == 0 ? Color.FromArgb("#E8EDF4") : Color.FromArgb("#F8FAFC"), _variable ? 64 + id % 5 * 16 : 72);
	}
	ObservableCollection<BenchmarkItem> Generate(int count)
	{
		_expected.Clear();
		_expected.EnsureCapacity(count + 3000);
		var rows = new List<BenchmarkItem>(count);
		for (int i = 0; i < count; i++) { var item = MakeItem(i); rows.Add(item); _expected.Add(item.Id, item); }
		return new(rows);
	}
	void Mark(string action)
	{
		var options = _result?.Configuration ?? CollectionViewBenchmarkOptions.Current;
		string line = $"CVBENCH: {action} handler={options.Handler} count={_count} scenario={_result?.Configuration.Scenario ?? options.Scenario} run={options.Run} warmup={options.Warmup} runId={options.RunId}";
		_result?.Logs.Add(line);
		Console.WriteLine(line);
	}
	async Task RunBenchmarkAsync()
	{
		if (_cancel != null) return;
		if (_scenarioPicker.SelectedItem?.ToString() == "manual")
		{
			await RunManualCaptureAsync();
			return;
		}
		_manualPrepared = false;
		_cancel = new CancellationTokenSource();
		var token = _cancel.Token;
		var options = CollectionViewBenchmarkOptions.Current;
		string scenario = options.Auto ? options.Scenario : SelectedScenario;
		_count = options.Auto ? options.Count : SelectedCount;
		_result = new BenchmarkResult { Configuration = new CollectionViewBenchmarkOptions
		{
			Handler = options.Handler, Count = _count, Seed = options.Seed, Run = options.Run, Warmup = options.Warmup,
			Auto = options.Auto, ManualSeconds = options.ManualSeconds, Scenario = scenario,
			RunId = options.Auto ? options.RunId : Guid.NewGuid().ToString("N"), Output = options.Output
		} };
		int exit = 0;
		try
		{
			if (Debugger.IsAttached) throw new InvalidOperationException("Detach debugger for measured runs.");
			ConfigureLayout(scenario);
			var generated = Generate(_count); // Generation is excluded from all measured intervals.
			_status.Text = "Running " + scenario;
			await WaitLayoutAsync(() => _collection.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement native && native.IsLoaded &&
				NativeViewportProbe.Find<Microsoft.UI.Xaml.Controls.ScrollViewer>(native) != null, token);
			_probe = new(_collection, _expected) { Horizontal = _horizontal, Source = generated };
			_probe.Attach(_result, options.Handler);
			_result.WinUIAssembly = typeof(Microsoft.UI.Xaml.Controls.ItemsRepeater).Assembly.FullName;
			_handlerStatus.Text = _result.HandlerType + " / " + _result.NativeItemsType;
			_result.RasterizationScale = ((Microsoft.UI.Xaml.FrameworkElement)_collection.Handler!.PlatformView!).XamlRoot.RasterizationScale;
			_result.WindowWidth = Window.Width; _result.WindowHeight = Window.Height;
			Mark("START native=" + _result.NativeItemsType);
			var initialSnapshot = ProcessSnapshot.Take();
			_result.Metrics["initialManagedHeapBytes"] = initialSnapshot.Heap;
			_result.Metrics["initialWorkingSetBytes"] = initialSnapshot.WorkingSet;
			var initial = Stopwatch.StartNew();
			_items = generated;
			_collection.ItemsSource = _items;
			await VerifyVisibleAsync(_items[0].Id, token);
			_result.Metrics["initialVisibleLayoutMs"] = initial.Elapsed.TotalMilliseconds;
			if (_probe.Scrollable <= 0) throw new InvalidOperationException("Native scroll extent not ready.");
			_result.Metrics["readyForScrollLayoutMs"] = initial.Elapsed.TotalMilliseconds;
			_result.ViewportWidth = _probe.Scroll.ViewportWidth; _result.ViewportHeight = _probe.Scroll.ViewportHeight;
			RecordRealization("before");
			VerifySourceOrder();
			if (scenario == "initial")
			{
				_result.Metrics["durationMs"] = initial.Elapsed.TotalMilliseconds;
				initialSnapshot.Finish(_result);
				_result.NotMeasured["uiResponsivenessMs"] = "Initial scenario too short for periodic queue probe; measured in other scenarios.";
			}
			else
			{
				var before = ProcessSnapshot.Take();
				using var response = new ResponsivenessSampler(Queue, _result.ResponsivenessMs);
				var clock = Stopwatch.StartNew();
				switch (scenario)
				{
					case "sequential": await SequentialAsync(token); break;
					case "rapid": await RapidAsync(token); break;
					case "native-positions": await NativePositionsAsync(token); break;
					case "updates": await UpdatesAsync(token); break;
					default: await CoverageAsync(token); break;
				}
				clock.Stop();
				response.Dispose();
				_result.Metrics["durationMs"] = clock.Elapsed.TotalMilliseconds;
				before.Finish(_result);
				SummarizeResponsiveness();
			}
			_result.Status = "passed";
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			exit = 2; _result.Status = "cancelled"; _result.Failures.Add("User/runner cancellation.");
		}
		catch (Exception error)
		{
			exit = 1; _result.Status = "failed"; _result.Failures.Add(error.ToString()); Console.Error.WriteLine(error);
		}
		finally
		{
			_cancel.Dispose(); _cancel = null;
			_status.Text = _result.Status;
			Mark("END status=" + _result.Status + " metrics=" + JsonSerializer.Serialize(_result.Metrics) +
				" realization=" + JsonSerializer.Serialize(_result.Realization));
			try { await ExportAsync(); }
			catch (Exception error) { exit = 3; _status.Text = "Export failed: " + error.Message; Console.Error.WriteLine(error); }
			if (options.Auto) Environment.ExitCode = exit;
			if (options.Auto) Application.Current!.Quit();
		}
	}
	async Task RunManualCaptureAsync()
	{
		if (_cancel != null || !_manualPrepared) return;
		var options = CollectionViewBenchmarkOptions.Current;
		if (options.Auto) throw new InvalidOperationException("Manual capture cannot run automatically.");
		_cancel = new CancellationTokenSource();
		var token = _cancel.Token;
		SetControlsEnabled(false);
		_result = new BenchmarkResult { Configuration = new CollectionViewBenchmarkOptions
		{
			Handler = options.Handler, Count = _count, Seed = options.Seed, Run = options.Run,
			ManualSeconds = options.ManualSeconds, Scenario = "manual", RunId = Guid.NewGuid().ToString("N"), Output = options.Output
		} };
		_result.NotMeasured["initialVisibleLayoutMs"] = "Manual rows were prepared before explicit capture; startup/loading is outside this interval.";
		_result.NotMeasured["manualInputRepeatability"] = "Human input is uncontrolled. Offset samples cannot prove input origin or full traversal; no default batch comparison.";
		_result.NotMeasured["instrumentationOverhead"] = "Counters include 1 Hz visual-tree snapshots/status updates and 100 ms UI queue probes; their CPU/allocation cost is not subtracted.";
		var clock = new Stopwatch();
		ProcessSnapshot before = default;
		bool recording = false;
		ResponsivenessSampler? response = null;
		try
		{
			if (Debugger.IsAttached) throw new InvalidOperationException("Detach debugger for measured runs.");
			_probe!.Attach(_result, options.Handler);
			_result.WinUIAssembly = typeof(Microsoft.UI.Xaml.Controls.ItemsRepeater).Assembly.FullName;
			_result.RasterizationScale = ((Microsoft.UI.Xaml.FrameworkElement)_collection.Handler!.PlatformView!).XamlRoot.RasterizationScale;
			_result.WindowWidth = Window.Width; _result.WindowHeight = Window.Height;
			_result.ViewportWidth = _probe.Scroll.ViewportWidth; _result.ViewportHeight = _probe.Scroll.ViewportHeight;
			Mark("START native=" + _result.NativeItemsType + " input=human");
			before = ProcessSnapshot.Take();
			clock.Start(); recording = true;
			response = new ResponsivenessSampler(Queue, _result.ResponsivenessMs);
			var baseline = SampleManualViewport("manual-baseline", clock.Elapsed.TotalMilliseconds);
			double previousOffset = baseline.Offset, minimumOffset = baseline.Offset, maximumOffset = baseline.Offset;
			bool forward = false, reverse = false, midpoint = false;
			while (clock.Elapsed.TotalSeconds < options.ManualSeconds)
			{
				CheckCancellation(token);
				_status.Text = $"{options.Handler}: manual capture, {Math.Ceiling(options.ManualSeconds - clock.Elapsed.TotalSeconds)}s remaining. Scroll DOWN then UP; no automated movement.";
				await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.ManualSeconds - clock.Elapsed.TotalSeconds, 0, 1)), token);
				CheckCancellation(token);
				if (clock.Elapsed.TotalSeconds >= options.ManualSeconds) break;
				string stage = !midpoint && clock.Elapsed.TotalSeconds >= options.ManualSeconds / 2.0
					? "manual-time-midpoint" : "manual-sample";
				if (stage == "manual-time-midpoint") midpoint = true;
				var sample = SampleManualViewport(stage, clock.Elapsed.TotalMilliseconds);
				forward |= sample.Offset - previousOffset > 2;
				reverse |= previousOffset - sample.Offset > 2;
				previousOffset = sample.Offset;
				minimumOffset = Math.Min(minimumOffset, sample.Offset);
				maximumOffset = Math.Max(maximumOffset, sample.Offset);
			}
			var end = SampleManualViewport("manual-end", clock.Elapsed.TotalMilliseconds);
			forward |= end.Offset - previousOffset > 2;
			reverse |= previousOffset - end.Offset > 2;
			minimumOffset = Math.Min(minimumOffset, end.Offset);
			maximumOffset = Math.Max(maximumOffset, end.Offset);
			_result.Metrics["manualMinimumOffsetDips"] = minimumOffset;
			_result.Metrics["manualMaximumOffsetDips"] = maximumOffset;
			_result.Metrics["manualOffsetMovementObserved"] = forward || reverse ? 1 : 0;
			_result.Metrics["manualForwardMovementObserved"] = forward ? 1 : 0;
			_result.Metrics["manualReverseMovementObserved"] = reverse ? 1 : 0;
			_result.Checks.Add(forward || reverse ? "Native offset movement observed at sampling times; input origin/full traversal not verified."
				: "NO native offset movement observed; this is NOT a completed scrolling test.");
			if (!midpoint) _result.NotMeasured["manualTimeMidpoint"] = "UI delivery skipped the temporal midpoint; actual sample timestamps retained.";
			_result.Status = "captured";
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			_result.Status = "cancelled"; _result.Failures.Add("User/runner cancellation.");
		}
		catch (Exception error)
		{
			_result.Status = "failed"; _result.Failures.Add(error.ToString()); Console.Error.WriteLine(error);
		}
		finally
		{
			clock.Stop(); response?.Dispose();
			if (recording)
			{
				_result.Metrics["durationMs"] = clock.Elapsed.TotalMilliseconds;
				_result.Metrics["manualRequestedDurationMs"] = options.ManualSeconds * 1000;
				_result.Metrics["manualOverrunMs"] = Math.Max(0, clock.Elapsed.TotalMilliseconds - options.ManualSeconds * 1000);
				before.Finish(_result);
				SummarizeResponsiveness();
			}
			_cancel.Dispose(); _cancel = null;
			Mark("END status=" + _result.Status + " metrics=" + JsonSerializer.Serialize(_result.Metrics) +
				" realization=" + JsonSerializer.Serialize(_result.Realization));
			try
			{
				await ExportAsync();
				_status.Text = $"{options.Handler}: {_result.Status}; sampled movement={_result.Metrics.GetValueOrDefault("manualOffsetMovementObserved")}, invalid observations={_result.InvalidLayoutObservations.Values.Sum()}. Exported {Path.Combine(options.Output, _result.Configuration.RunId + ".json")}. App stays open.";
			}
			catch (Exception error)
			{
				_status.Text = "Export failed: " + error.Message; Console.Error.WriteLine(error);
			}
			SetControlsEnabled(true);
		}
	}
	NativeSample SampleManualViewport(string stage, double elapsedMs)
	{
		bool valid = _probe!.Valid(out string reason);
		var sample = _probe.Snapshot(stage, _count, _items.Count);
		_result!.Realization.Add(sample);
		_result.ManualViewportObservations.Add(new(stage, elapsedMs, valid, reason));
		if (!valid)
		{
			_result.InvalidLayoutObservations.TryGetValue(reason, out int count);
			_result.InvalidLayoutObservations[reason] = count + 1;
		}
		_result.Metrics["manualSampledMaxRealizedRows"] = Math.Max(_result.Metrics.GetValueOrDefault("manualSampledMaxRealizedRows"), sample.RealizedRows);
		_result.Metrics["manualSampledMaxNativeContainers"] = Math.Max(_result.Metrics.GetValueOrDefault("manualSampledMaxNativeContainers"), sample.NativeContainers);
		_result.Metrics["manualSampleCount"] = _result.ManualViewportObservations.Count;
		if (Array.IndexOf(sample.VisibleIds, _items[0].Id) >= 0) _result.Metrics["manualFirstItemObserved"] = 1;
		if (Array.IndexOf(sample.VisibleIds, _items[^1].Id) >= 0) _result.Metrics["manualLastItemObserved"] = 1;
		return sample;
	}
	void CheckCancellation(CancellationToken token)
	{
		var options = _result?.Configuration ?? CollectionViewBenchmarkOptions.Current;
		if (File.Exists(options.CancelFile)) _cancel?.Cancel();
		token.ThrowIfCancellationRequested();
	}
	async Task WaitLayoutAsync(Func<bool> condition, CancellationToken token, int timeoutMs = 10000)
	{
		// Rendering is a post-layout observation opportunity, NOT a presented-frame event.
		// No delay is used as proof of rendering. Predicate checks actual native geometry/bindings.
		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		int consecutive = 0;
		void Observe(object? sender, object args)
		{
			try
			{
				CheckCancellation(token);
				if (condition()) { if (++consecutive >= 2) completion.TrySetResult(); }
				else consecutive = 0;
			}
			catch (OperationCanceledException) { completion.TrySetCanceled(token); }
			catch (Exception error) { completion.TrySetException(error); }
		}
		CompositionTarget.Rendering += Observe;
		try
		{
			await completion.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), token);
		}
		catch (TimeoutException)
		{
			CheckCancellation(token); // Handle runner cancellation even without rendering callbacks.
			throw;
		}
		finally { CompositionTarget.Rendering -= Observe; }
	}
	async Task VerifyVisibleAsync(int id, CancellationToken token)
	{
		await WaitLayoutAsync(() => ObserveViewport() && _probe!.Has(id), token);
		if (!_probe!.Valid(out var reason)) throw new InvalidOperationException(reason);
		VerifySourceOrder();
	}
	bool ObserveViewport()
	{
		if (_probe!.Valid(out var reason)) return true;
		// Diagnostic layout observations may be transitory while settling. Never call
		// them presented blank frames, nor silently discard evidence of them.
		_result!.InvalidLayoutObservations.TryGetValue(reason, out int count);
		_result.InvalidLayoutObservations[reason] = count + 1;
		return false;
	}
	void VerifySourceOrder()
	{
		// Infrequent assertion after settling, not on each native callback.
		_probe!.VerifyOrder(_items);
	}
	void SummarizeResponsiveness()
	{
		var values = _result!.ResponsivenessMs.ToArray();
		if (values.Length == 0)
		{
			_result.NotMeasured["uiResponsivenessMs"] = "No queue samples delivered during this scenario.";
			return;
		}
		Array.Sort(values);
		double sum = 0;
		for (int i = 0; i < values.Length; i++) sum += values[i];
		_result.Metrics["uiQueueLatencyAverageMs"] = sum / values.Length;
		_result.Metrics["uiQueueLatencyP95Ms"] = values[(int)Math.Ceiling(values.Length * 0.95) - 1];
		_result.Metrics["uiQueueLatencyMaxMs"] = values[^1];
	}
	void RecordRealization(string stage)
	{
		var sample = _probe!.Snapshot(stage, _count, _items.Count);
		_result!.Realization.Add(sample);
		VerifySourceOrder();
	}
	async Task JumpAsync(int index, CancellationToken token)
	{
		CheckCancellation(token);
		_collection.ScrollTo(_items[index], position: ScrollToPosition.MakeVisible, animate: false);
		try { await VerifyVisibleAsync(_items[index].Id, token); }
		catch (TimeoutException)
		{
			_result!.Realization.Add(_probe!.Snapshot("ScrollTo-timeout-target-" + index, _count, _items.Count));
			_result.Failures.Add("Requested MAUI ScrollTo target index/ID " + index + "/" + _items[index].Id + " was not verified visible.");
			throw;
		}
	}
	async Task NativePositionsAsync(CancellationToken token)
	{
		// Bounded realization/memory experiment, NOT sustained scrolling or ScrollTo
		// correctness. Native offsets avoid silently substituting success for a
		// failed programmatic item-target assertion in the separate rapid scenario.
		int[] positions = { 0, 50, 100, 50, 0 };
		string[] stages = { "beginning", "midpoint-forward", "end", "midpoint-reverse", "beginning-return" };
		for (int i = 0; i < positions.Length; i++)
		{
			int percent = positions[i];
			bool verified = false;
			for (int attempt = 0; attempt < 12; attempt++)
			{
				CheckCancellation(token);
				double target = _probe!.Scrollable * percent / 100;
				_probe.Scroll.ChangeView(_horizontal ? target : null, _horizontal ? null : target, null, true);
				try
				{
					// WinUI's legacy list refines its estimated extent on realization;
					// an offset request beyond a shrinking extent is legitimately clamped.
					await WaitLayoutAsync(() => Math.Abs(_probe.Offset - Math.Min(target, _probe.Scrollable)) <= 2 &&
						ObserveViewport(), token);
				}
				catch (TimeoutException)
				{
					_result!.Realization.Add(_probe.Snapshot("native-position-timeout-" + percent, _count, _items.Count));
					_result.Failures.Add("Native requested offset " + target.ToString(System.Globalization.CultureInfo.InvariantCulture) +
						" at percent " + percent + "; actual clamped offset/extent are in the timeout snapshot.");
					throw;
				}
				VerifySourceOrder();
				if (percent == 100 && (!_probe.Has(_items[^1].Id) || _probe.Scrollable - _probe.Offset > 2)) continue;
				if (percent == 0 && !_probe.Has(_items[0].Id)) continue;
				verified = true;
				break;
			}
			if (!verified) throw new InvalidOperationException("Native position did not reach the verified endpoint after extent refinement.");
			RecordRealization(stages[i]);
		}
		_result!.Checks.Add("Native ScrollViewer beginning/midpoint/end/midpoint/beginning positions; native layout, item identity/order, and bounded counts verified. Not sustained scrolling or MAUI ScrollTo validation.");
	}
	async Task SequentialAsync(CancellationToken token)
	{
		await JumpAsync(0, token);
		await SweepAsync(true, token);
		await SweepAsync(false, token);
		await VerifyVisibleAsync(_items[0].Id, token);
		_result!.Checks.Add("Controlled native ChangeView sweep forward/reverse, endpoint identities verified.");
	}
	async Task SweepAsync(bool forward, CancellationToken token)
	{
		const double dipsPerSecond = 6000;
		var clock = Stopwatch.StartNew();
		double previousMs = 0;
		bool midpoint = false;
		var deadline = DateTime.UtcNow.AddMinutes(50);
		while (true)
		{
			CheckCancellation(token);
			if (DateTime.UtcNow > deadline) throw new TimeoutException("Native sweep did not reach endpoint in 50 minutes.");
			double nowMs = clock.Elapsed.TotalMilliseconds;
			// Clamp each increment to half a viewport so a stalled UI cannot turn sustained
			// scrolling into distant jumps. Slow delivery reduces effective rate, never hides it.
			double delta = Math.Min(dipsPerSecond * (nowMs - previousMs) / 1000, _probe!.Viewport / 2);
			previousMs = nowMs;
			double target = Math.Clamp(_probe.Offset + (forward ? delta : -delta), 0, _probe.Scrollable);
			if (!_probe.Scroll.ChangeView(_horizontal ? target : null, _horizontal ? null : target, null, true) &&
				Math.Abs(_probe.Offset - target) > 1)
				throw new InvalidOperationException("Native ChangeView rejected scroll request.");
			// This delay controls INPUT rate only; geometry is checked separately.
			await Task.Delay(25, token);
			await WaitLayoutAsync(() => Math.Abs(_probe.Offset - target) <= 2 && ObserveViewport(), token);
			if (!midpoint && (forward ? _probe.Offset >= _probe.Scrollable / 2 : _probe.Offset <= _probe.Scrollable / 2))
			{
				RecordRealization(forward ? "midpoint-forward" : "midpoint-reverse"); midpoint = true;
			}
			if (forward ? _probe.Scrollable - _probe.Offset <= 2 : _probe.Offset <= 2) break;
		}
		await VerifyVisibleAsync(forward ? _items[^1].Id : _items[0].Id, token);
		RecordRealization(forward ? "end" : "beginning-return");
	}
	async Task RapidAsync(CancellationToken token)
	{
		// Fixed distant forward/reverse sequence, repeated; no timing-dependent random input.
		int[] percent = { 0, 90, 20, 100, 50, 10, 80, 30, 0 };
		for (int repeat = 0; repeat < 3; repeat++)
			for (int i = 0; i < percent.Length; i++)
			{
				await JumpAsync((_items.Count - 1) * percent[i] / 100, token);
				if (repeat == 0 && percent[i] == 50) RecordRealization("midpoint");
				if (repeat == 0 && percent[i] == 100) RecordRealization("end");
			}
		_result!.Checks.Add("27 distant jumps including reverse; sampled viewport bindings, geometry, and target identities verified.");
	}
	async Task OperationAsync(string name, Action operation, Func<bool> verify, CancellationToken token)
	{
		CheckCancellation(token);
		var clock = Stopwatch.StartNew();
		operation();
		await WaitLayoutAsync(verify, token);
		_result!.Metrics[name + "VisibleLayoutMs"] = clock.Elapsed.TotalMilliseconds;
		_result.Checks.Add(name + " observed in native viewport.");
	}
	async Task UpdatesAsync(CancellationToken token)
	{
		// Prepare new values outside the operation intervals.
		var append = new BenchmarkItem[1000];
		var insert = new BenchmarkItem[100];
		for (int i = 0; i < append.Length; i++) append[i] = MakeItem(_count + i);
		for (int i = 0; i < insert.Length; i++) insert[i] = MakeItem(_count + 1000 + i);
		await JumpAsync(_items.Count - 1, token);
		await OperationAsync("append1000", () =>
		{
			for (int i = 0; i < append.Length; i++) { _expected.Add(append[i].Id, append[i]); _items.Add(append[i]); }
			_collection.ScrollTo(append[^1], position: ScrollToPosition.End, animate: false);
		}, () => _probe!.Valid(out _) && _probe.Has(append[^1].Id), token);
		await JumpAsync(0, token);
		await OperationAsync("insert100", () =>
		{
			for (int i = 0; i < insert.Length; i++) { _expected.Add(insert[i].Id, insert[i]); _items.Insert(i, insert[i]); }
			_collection.ScrollTo(insert[0], position: ScrollToPosition.Start, animate: false);
		}, () => _probe!.Valid(out _) && _probe.Has(insert[0].Id), token);
		await OperationAsync("remove100", () =>
		{
			for (int i = 0; i < 100; i++) { _expected.Remove(_items[0].Id); _items.RemoveAt(0); }
			_collection.ScrollTo(_items[0], position: ScrollToPosition.Start, animate: false);
		}, () => _probe!.Valid(out _) && _probe.Has(0) && !_probe.Has(insert[0].Id), token);
		var visible = _probe!.FirstVisible();
		var replacement = MakeItem(visible.Id, " • replaced");
		int index = _items.IndexOf(visible);
		await OperationAsync("replace", () =>
		{
			_expected[replacement.Id] = replacement; _items[index] = replacement;
		}, () => _probe.Valid(out _) && _probe.Has(replacement.Id), token);
		await JumpAsync(_items.Count / 2, token);
		RecordRealization("midpoint-away");
		var away = _probe.FirstVisible();
		var awayInsert = MakeItem(_count + 2000);
		await OperationAsync("mutationWhileAway", () =>
		{
			_expected.Add(awayInsert.Id, awayInsert); _items.Insert(0, awayInsert);
			_collection.ScrollTo(away, position: ScrollToPosition.MakeVisible, animate: false);
		}, () => _probe.Valid(out _) && _probe.Has(away.Id), token);
		// Clear + add is an observable Reset followed by Adds, not silently replacing ItemsSource.
		var reset = new List<BenchmarkItem>(_count);
		for (int i = 0; i < _count; i++) reset.Add(MakeItem(i));
		await OperationAsync("reset", () =>
		{
			_items.Clear(); _expected.Clear();
			for (int i = 0; i < reset.Count; i++) { _expected.Add(reset[i].Id, reset[i]); _items.Add(reset[i]); }
			_collection.ScrollTo(_items[0], position: ScrollToPosition.Start, animate: false);
		}, () => _probe.Valid(out _) && _probe.Has(0) && !_probe.Has(awayInsert.Id), token);
	}
	async Task CoverageAsync(CancellationToken token)
	{
		await JumpAsync(0, token);
		await WaitLayoutAsync(() => NativeViewportProbe.IsVisible(_header, _probe!.Scroll), token);
		_result!.Checks.Add("Header intersects viewport.");
		await JumpAsync(_items.Count / 2, token); RecordRealization("midpoint");
		await JumpAsync(_items.Count - 1, token);
		// Make footer visible independently of row ScrollTo.
		_probe!.Scroll.ChangeView(_horizontal ? _probe.Scrollable : null, _horizontal ? null : _probe.Scrollable, null, true);
		await WaitLayoutAsync(() => NativeViewportProbe.IsVisible(_footer, _probe.Scroll), token);
		RecordRealization("end"); _result.Checks.Add("Footer intersects viewport.");
		_collection.SelectionMode = SelectionMode.Single;
		_collection.SelectedItem = _items[^1];
		await WaitLayoutAsync(() => NativeSelectionMatches(_items.Count - 1), token);
		_result.Checks.Add("Single selection verified in native selection model.");
		_collection.SelectionMode = SelectionMode.Multiple;
		_collection.SelectedItems = new ObservableCollection<object> { _items[^1], _items[^2] };
		await WaitLayoutAsync(() => NativeSelectionMatches(_items.Count - 1, _items.Count - 2), token);
		_result.Checks.Add("Multiple selection verified in native selection model.");
		_collection.SelectedItems = new ObservableCollection<object>();
		_collection.SelectedItem = null; _collection.SelectionMode = SelectionMode.None;
		await UpdatesAsync(token);
		_items.Clear(); _expected.Clear();
		await WaitLayoutAsync(() => NativeViewportProbe.IsVisible(_empty, _probe.Scroll), token);
		_result.Checks.Add("Observable Clear/EmptyView has positive native viewport intersection.");
	}
	bool NativeSelectionMatches(params int[] indices)
	{
		if (_probe!.LegacyList is { } list)
		{
			if (list.SelectedItems.Count != indices.Length) return false;
			for (int i = 0; i < indices.Length; i++)
			{
				if (list.ContainerFromIndex(indices[i]) is not Microsoft.UI.Xaml.Controls.ListViewItem container || !container.IsSelected) return false;
			}
			return true;
		}
		if (_collection.Handler!.PlatformView is Microsoft.UI.Xaml.Controls.ItemsView view)
		{
			if (view.SelectedItems.Count != indices.Length) return false;
			for (int i = 0; i < indices.Length; i++) if (!view.IsSelected(indices[i])) return false;
			return true;
		}
		throw new InvalidOperationException("Unsupported native selection model.");
	}
	async Task ExportAsync()
	{
		if (_result == null) return;
		Directory.CreateDirectory(_result.Configuration.Output);
		string path = Path.Combine(_result.Configuration.Output, _result.Configuration.RunId + ".json");
		string json = JsonSerializer.Serialize(_result, new JsonSerializerOptions { WriteIndented = true });
		await File.WriteAllTextAsync(path + ".tmp", json);
		File.Move(path + ".tmp", path, true);
		await File.WriteAllLinesAsync(Path.ChangeExtension(path, ".log"), _result.Logs);
	}

	// A bounded queue probe measures UI-thread delivery delay, not render/frame times.
	sealed class ResponsivenessSampler : IDisposable
	{
		readonly DispatcherQueue _queue;
		readonly List<double> _samples;
		readonly System.Threading.Timer _timer;
		readonly DispatcherQueueHandler _observe;
		int _pending, _disposed;
		long _posted;
		public ResponsivenessSampler(DispatcherQueue queue, List<double> samples)
		{
			_queue = queue; _samples = samples;
			_observe = Observe;
			_timer = new System.Threading.Timer(Tick, null, 100, 100);
		}
		void Tick(object? state)
		{
			if (Volatile.Read(ref _disposed) != 0 || Interlocked.Exchange(ref _pending, 1) != 0) return;
			_posted = Stopwatch.GetTimestamp();
			if (!_queue.TryEnqueue(_observe)) Interlocked.Exchange(ref _pending, 0);
		}
		void Observe()
		{
			if (Volatile.Read(ref _disposed) == 0) _samples.Add(Stopwatch.GetElapsedTime(_posted).TotalMilliseconds);
			Interlocked.Exchange(ref _pending, 0);
		}
		public void Dispose() { Interlocked.Exchange(ref _disposed, 1); _timer.Dispose(); }
	}
}
#endif
