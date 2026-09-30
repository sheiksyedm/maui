namespace Maui.Controls.Sample;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		// To test shell scenarios, change this to true
		bool useShell = false;

		if (!useShell)
		{
			var window = new Window(new NavigationPage(new MainPage()));
#if WINDOWS
			window.Width = 1100;
			window.Height = 800;
#endif
			return window;
		}
		else
		{
			return new Window(new SandboxShell());
		}
	}
}
