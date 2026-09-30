#if WINDOWS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WRect = Windows.Foundation.Rect;
using WVisibility = Microsoft.UI.Xaml.Visibility;

namespace Maui.Controls.Sample;

// Reads the actual visual tree, not the source count or Scrolled event indices.
internal sealed class NativeViewportProbe
{
	readonly CollectionView _view;
	readonly Dictionary<int, BenchmarkItem> _expected;
	readonly List<VisibleRow> _visible = new();
	static readonly Comparison<VisibleRow> s_compareRows = VisibleRow.Compare;
	FrameworkElement Root => (FrameworkElement)(_view.Handler?.PlatformView ?? throw new InvalidOperationException("No platform view."));
	public ScrollViewer Scroll { get; private set; } = null!;
	public ItemsRepeater? Repeater { get; private set; }
	public ListViewBase? LegacyList { get; private set; }
	public bool Horizontal { get; set; }
	public IList<BenchmarkItem> Source { get; set; } = null!;
	int _rows, _containers, _visuals;
	int _firstNativeVisible = -1, _lastNativeVisible = -1, _excludedCachedRows;
	public int VisibleCount => _visible.Count;
	public double Offset => Horizontal ? Scroll.HorizontalOffset : Scroll.VerticalOffset;
	public double Scrollable => Horizontal ? Scroll.ScrollableWidth : Scroll.ScrollableHeight;
	public double Extent => Horizontal ? Scroll.ExtentWidth : Scroll.ExtentHeight;
	public double Viewport => Horizontal ? Scroll.ViewportWidth : Scroll.ViewportHeight;
	public NativeViewportProbe(CollectionView view, Dictionary<int, BenchmarkItem> expected)
	{
		_view = view;
		_expected = expected;
	}
	public void Attach(BenchmarkResult result, string requested)
	{
		Repeater = Find<ItemsRepeater>(Root);
		LegacyList = Root as ListViewBase ?? Find<ListViewBase>(Root);
		Scroll = Find<ScrollViewer>(Root) ?? throw new InvalidOperationException("Native ScrollViewer not found.");
		result.HandlerType = _view.Handler!.GetType().FullName;
		result.PlatformType = Root.GetType().FullName;
		result.NativeItemsType = (Repeater as object ?? LegacyList)?.GetType().FullName;
		bool actualCv2 = result.HandlerType?.EndsWith(".CollectionViewHandler2", StringComparison.Ordinal) == true &&
			Repeater != null && LegacyList == null;
		bool actualCv1 = result.HandlerType?.EndsWith(".CollectionViewHandler", StringComparison.Ordinal) == true &&
			LegacyList != null && Repeater == null;
		if (!(requested == "CV2" ? actualCv2 : actualCv1))
			throw new InvalidOperationException($"Handler mismatch: requested {requested}, attached {result.HandlerType}, platform {result.PlatformType}, native {result.NativeItemsType}.");
	}
	public static T? Find<T>(DependencyObject root) where T : DependencyObject
	{
		if (root is T found) return found;
		int count = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < count; i++)
		{
			var child = Find<T>(VisualTreeHelper.GetChild(root, i));
			if (child != null) return child;
		}
		return null;
	}
	public static bool IsVisible(View view, ScrollViewer viewport)
	{
		if (view.Handler?.PlatformView is not FrameworkElement native || !native.IsLoaded ||
			!AncestorsVisible(native) || native.ActualWidth <= 0 || native.ActualHeight <= 0) return false;
		var rect = native.TransformToVisual(viewport).TransformBounds(new WRect(0, 0, native.ActualWidth, native.ActualHeight));
		return rect.Right > 0 && rect.Bottom > 0 && rect.Left < viewport.ViewportWidth && rect.Top < viewport.ViewportHeight;
	}
	static bool AncestorsVisible(DependencyObject node)
	{
		DependencyObject? current = node;
		while (current != null)
		{
			if (current is UIElement element && (element.Visibility != WVisibility.Visible || element.Opacity <= 0)) return false;
			current = VisualTreeHelper.GetParent(current);
		}
		return true;
	}
	public void Read()
	{
		_rows = _containers = _visuals = 0;
		_excludedCachedRows = 0;
		_firstNativeVisible = _lastNativeVisible = -1;
		if (LegacyList?.ItemsPanelRoot is ItemsStackPanel stack)
		{
			_firstNativeVisible = stack.FirstVisibleIndex;
			_lastNativeVisible = stack.LastVisibleIndex;
		}
		else if (LegacyList?.ItemsPanelRoot is ItemsWrapGrid wrap)
		{
			_firstNativeVisible = wrap.FirstVisibleIndex;
			_lastNativeVisible = wrap.LastVisibleIndex;
		}
		_visible.Clear();
		Walk(Root, true);
		_visible.Sort(s_compareRows);
	}
	void Walk(DependencyObject node, bool ancestorsVisible)
	{
		_visuals++;
		bool visible = ancestorsVisible && (node is not UIElement ui || ui.Visibility == WVisibility.Visible && ui.Opacity > 0);
		if (node is ListViewItem || node is GridViewItem || node is ItemContainer) _containers++;
		if (node is FrameworkElement element && element.Tag is BenchmarkRow row)
		{
			// Count cached/hidden roots too. Hidden native elements still cost memory;
			// skipping collapsed subtrees would give falsely bounded realization counts.
			_rows++;
			if (!IsActiveNativeRow(element, out int nativeIndex)) _excludedCachedRows++;
			else if (visible && element.IsLoaded && element.ActualWidth > 0 && element.ActualHeight > 0)
			{
				var rect = element.TransformToVisual(Scroll).TransformBounds(new WRect(0, 0, element.ActualWidth, element.ActualHeight));
				if (rect.Right > 0 && rect.Bottom > 0 && rect.Left < Scroll.ViewportWidth && rect.Top < Scroll.ViewportHeight)
					_visible.Add(new(row, rect, Horizontal, nativeIndex));
			}
		}
		int count = VisualTreeHelper.GetChildrenCount(node);
		for (int i = 0; i < count; i++) Walk(VisualTreeHelper.GetChild(node, i), visible);
	}
	bool IsActiveNativeRow(FrameworkElement element, out int nativeIndex)
	{
		// Recycling caches can keep loaded roots in the tree with stale transforms.
		// Geometry alone therefore does not prove viewport membership. Retain ALL
		// roots in realization counts, but require native panel/index membership
		// for visible binding/order assertions.
		DependencyObject? node = element;
		nativeIndex = -1;
		while (node != null && node != Root)
		{
			if (LegacyList != null && (node is ListViewItem || node is GridViewItem))
			{
				int index = LegacyList.IndexFromContainer(node);
				nativeIndex = index;
				return index >= 0 && (_firstNativeVisible < 0 ||
					index >= _firstNativeVisible && index <= _lastNativeVisible);
			}
			if (Repeater != null && node is UIElement child && VisualTreeHelper.GetParent(node) == Repeater)
			{
				nativeIndex = Repeater.GetElementIndex(child);
				return nativeIndex >= 0;
			}
			node = VisualTreeHelper.GetParent(node);
		}
		return false;
	}
	public bool Has(int id)
	{
		for (int i = 0; i < _visible.Count; i++)
			if (_visible[i].Row.BindingContext is BenchmarkItem item && item.Id == id && _visible[i].Row.Matches(item)) return true;
		return false;
	}
	public BenchmarkItem FirstVisible()
	{
		Read();
		if (_visible.Count == 0) throw new InvalidOperationException("Blank native viewport.");
		return (BenchmarkItem)_visible[0].Row.BindingContext;
	}
	public void VerifyOrder(IList<BenchmarkItem> items)
	{
		Read();
		if (_visible.Count == 0) throw new InvalidOperationException("No visible source order evidence.");
		var first = (BenchmarkItem)_visible[0].Row.BindingContext;
		int index = items.IndexOf(first);
		if (index < 0) throw new InvalidOperationException("Visible item not in current source.");
		for (int i = 0; i < _visible.Count; i++)
			if (index + i >= items.Count || !_visible[i].Row.Matches(items[index + i]))
				throw new InvalidOperationException("Visible source ordering/missing item assertion failed at slot " + i +
					", first source index " + index + ", actual ID " + ((BenchmarkItem)_visible[i].Row.BindingContext).Id +
					", native visible range " + _firstNativeVisible + ".." + _lastNativeVisible + ".");
	}
	public bool Valid(out string reason)
	{
		Read();
		if (_visible.Count == 0) { reason = "No laid-out rows intersect native viewport."; return false; }
		for (int i = 0; i < _visible.Count; i++)
		{
			var visible = _visible[i];
			if (visible.Row.BindingContext is not BenchmarkItem item || !_expected.TryGetValue(item.Id, out var expected) ||
				!visible.Row.Matches(expected) || visible.NativeIndex < 0 || visible.NativeIndex >= Source.Count ||
				!visible.Row.Matches(Source[visible.NativeIndex]))
			{
				reason = "Missing/stale item binding or incorrect reuse in visible native row."; return false;
			}
			// Fixed spacing is zero. Adjacent visible roots must not leave a blank hole.
			if (i > 0 && visible.Start - _visible[i - 1].End > 4)
			{
				reason = "Gap between laid-out visible row bounds exceeds 4 DIPs."; return false;
			}
			for (int j = 0; j < i; j++)
				if (_visible[j].Row.BindingContext is BenchmarkItem previous && previous.Id == item.Id)
				{
					reason = "Duplicate item identity in visible native rows."; return false;
				}
		}
		// Header/footer may occupy the edges at the endpoints, but not in the middle.
		if (Offset > 200 && _visible[0].Start > 4 ||
			Scrollable - Offset > 200 && _visible[^1].End < Viewport - 4)
		{
			reason = "Visible row batch does not cover the interior viewport edge."; return false;
		}
		reason = "";
		return true;
	}
	public NativeSample Snapshot(string stage, int count, int sourceCount)
	{
		Read();
		// Predeclared generous cache allowance: <= 12 viewports + 64 containers.
		// Smallest row is 64 DIPs; horizontal width is 320 DIPs.
		int bound = (int)Math.Ceiling(Viewport / (Horizontal ? 320 : 64)) * 12 + 64;
		if (count == 50000 && (_rows > bound || _containers > bound))
			throw new InvalidOperationException($"Virtualization bound exceeded at {stage}: rows={_rows}, containers={_containers}, bound={bound}.");
		double minimumHeight = double.MaxValue, maximumHeight = 0;
		var ids = new int[_visible.Count];
		for (int i = 0; i < _visible.Count; i++)
		{
			ids[i] = ((BenchmarkItem)_visible[i].Row.BindingContext).Id;
			minimumHeight = Math.Min(minimumHeight, _visible[i].Bounds.Height);
			maximumHeight = Math.Max(maximumHeight, _visible[i].Bounds.Height);
		}
		return new(stage, _rows, _containers, _visuals, _visible.Count, Scroll.ViewportWidth, Scroll.ViewportHeight, Offset, Extent, bound,
			sourceCount, _visible.Count == 0 ? null : ((BenchmarkItem)_visible[0].Row.BindingContext).Id,
			_visible.Count == 0 ? null : ((BenchmarkItem)_visible[^1].Row.BindingContext).Id,
			_visible.Count == 0 ? 0 : minimumHeight, maximumHeight, ids,
			_firstNativeVisible, _lastNativeVisible, _excludedCachedRows);
	}
	readonly record struct VisibleRow(BenchmarkRow Row, WRect Bounds, bool Horizontal, int NativeIndex)
	{
		public double Start => Horizontal ? Bounds.Left : Bounds.Top;
		public double End => Horizontal ? Bounds.Right : Bounds.Bottom;
		public static int Compare(VisibleRow left, VisibleRow right) => left.Start.CompareTo(right.Start);
	}
}
#endif
