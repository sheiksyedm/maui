namespace Maui.Controls.Sample;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();
#if WINDOWS
		InitializeBenchmark();
#else
		Content = new Label { Text = "CollectionView empirical benchmark requires Windows." };
#endif
	}
}