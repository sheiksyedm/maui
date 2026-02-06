using System.Collections.Generic;
using System.Linq;

namespace Maui.Controls.Sample.Issues
{
	[Issue(IssueTracker.Github, 33918, "Modal TabbedPage with NavigationPage tabs leaks on Android", PlatformAffected.Android)]
	public class Issue33918 : ContentPage
	{
		const string RunTestButtonId = "RunTestButton";
		const string StatusLabelId = "StatusLabel";
		const string SuccessText = "Success: All references collected";
		const string FailureText = "Failed: References still alive";

		public Issue33918()
		{
			var statusLabel = new Label
			{
				AutomationId = StatusLabelId,
				Text = "Tap 'Run Test' to start"
			};

			var runTestButton = new Button
			{
				AutomationId = RunTestButtonId,
				Text = "Run Test"
			};

			runTestButton.Clicked += async (sender, e) =>
			{
				runTestButton.IsEnabled = false;
				statusLabel.Text = "Running test...";

				await RunMemoryTest(statusLabel);

				runTestButton.IsEnabled = true;
			};

			Content = new VerticalStackLayout
			{
				Padding = 20,
				Spacing = 10,
				Children =
				{
					new Label { Text = "This test verifies that a modal TabbedPage with NavigationPage-wrapped tabs does not leak after being popped." },
					runTestButton,
					statusLabel
				}
			};
		}

		async Task RunMemoryTest(Label statusLabel)
		{
			var references = new List<WeakReference>();

			// Create and push modal TabbedPage with NavigationPage-wrapped tabs
			{
				var tabbedPage = CreateLeakyTabbedPage(wrapTabsInNavigationPage: true);
				references.Add(new WeakReference(tabbedPage));

				// Add references to the NavigationPage wrappers and their children
				foreach (var child in tabbedPage.Children)
				{
					references.Add(new WeakReference(child));
					if (child is NavigationPage navPage)
					{
						references.Add(new WeakReference(navPage.CurrentPage));
					}
				}

				await Navigation.PushModalAsync(tabbedPage);
				await Task.Delay(500); // Let it render

				// Switch to a different tab
				if (tabbedPage.Children.Count > 1)
				{
					tabbedPage.CurrentPage = tabbedPage.Children[1];
					await Task.Delay(200);
				}

				await Navigation.PopModalAsync();
			}

			// Force GC and check if references are collected
			statusLabel.Text = "Forcing GC...";
			await Task.Delay(100);

			try
			{
				await GarbageCollectionHelper.WaitForGC(timeout: 5000, references.ToArray());
				statusLabel.Text = SuccessText;
			}
			catch
			{
				var stillAlive = references
					.Where(x => x.IsAlive)
					.Select(x => x.Target?.GetType().Name ?? "Unknown")
					.ToList();

				statusLabel.Text = $"{FailureText}: {string.Join(", ", stillAlive)}";
			}
		}

		static TabbedPage CreateLeakyTabbedPage(bool wrapTabsInNavigationPage)
		{
			var tabbedPage = new TabbedPage
			{
				Title = "Modal TabbedPage (Leak Test)"
			};

			Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.TabbedPage
				.SetToolbarPlacement(tabbedPage,
					Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.ToolbarPlacement.Bottom);

			var overview = CreateTabPage("Overview");
			var settings = CreateTabPage("Settings");

			if (wrapTabsInNavigationPage)
			{
				var overviewNav = new NavigationPage(overview) { Title = overview.Title };
				var settingsNav = new NavigationPage(settings) { Title = settings.Title };

				tabbedPage.Children.Add(overviewNav);
				tabbedPage.Children.Add(settingsNav);
			}
			else
			{
				tabbedPage.Children.Add(overview);
				tabbedPage.Children.Add(settings);
			}

			return tabbedPage;
		}

		static ContentPage CreateTabPage(string title)
		{
			return new ContentPage
			{
				Title = title,
				Content = new VerticalStackLayout
				{
					Padding = new Thickness(20),
					Spacing = 12,
					Children =
					{
						new Label { Text = $"{title} tab" },
						new Label { Text = "This is a minimal page for leak test." }
					}
				}
			};
		}
	}
}
