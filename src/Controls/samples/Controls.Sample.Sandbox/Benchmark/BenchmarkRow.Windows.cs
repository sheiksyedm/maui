#if WINDOWS
using Microsoft.Maui.Graphics;

namespace Maui.Controls.Sample;

internal sealed record BenchmarkItem(int Id, string Title, string Secondary, Color Accent, Color Background, double RowHeight)
{
	public string IdText { get; } = Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

internal sealed class BenchmarkRow : Grid
{
	readonly Label _id = new() { FontSize = 12, TextColor = Colors.Black };
	readonly Label _title = new() { FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = Colors.Black };
	readonly Label _secondary = new() { FontSize = 12, MaxLines = 2, TextColor = Colors.Black };
	public BenchmarkRow()
	{
		Padding = new Thickness(8);
		VerticalOptions = LayoutOptions.Start;
		ColumnSpacing = 12;
		ColumnDefinitions.Add(new ColumnDefinition { Width = 32 });
		ColumnDefinitions.Add(new ColumnDefinition { Width = 64 });
		ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
		var visual = new BoxView { WidthRequest = 24, HeightRequest = 24, VerticalOptions = LayoutOptions.Center };
		visual.SetBinding(BoxView.ColorProperty, new Binding(nameof(BenchmarkItem.Accent)));
		_id.SetBinding(Label.TextProperty, new Binding(nameof(BenchmarkItem.IdText)));
		_title.SetBinding(Label.TextProperty, new Binding(nameof(BenchmarkItem.Title)));
		_secondary.SetBinding(Label.TextProperty, new Binding(nameof(BenchmarkItem.Secondary)));
		var text = new VerticalStackLayout { Spacing = 2, Children = { _title, _secondary } };
		Grid.SetColumn(_id, 1);
		Grid.SetColumn(text, 2);
		Children.Add(visual);
		Children.Add(_id);
		Children.Add(text);
		SetBinding(BackgroundColorProperty, new Binding(nameof(BenchmarkItem.Background)));
		SetBinding(HeightRequestProperty, new Binding(nameof(BenchmarkItem.RowHeight)));
	}
	protected override void OnHandlerChanged()
	{
		base.OnHandlerChanged();
		if (Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement native)
			native.Tag = this; // Harness-owned template root, not a MAUI handler edit.
	}
	public bool Matches(BenchmarkItem item) => ReferenceEquals(BindingContext, item) &&
		_id.Text == item.IdText && _title.Text == item.Title && _secondary.Text == item.Secondary;
}
#endif
