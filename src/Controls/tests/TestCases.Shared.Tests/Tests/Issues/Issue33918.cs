using NUnit.Framework;
using UITest.Appium;
using UITest.Core;

namespace Microsoft.Maui.TestCases.Tests.Issues
{
	public class Issue33918 : _IssuesUITest
	{
		public override string Issue => "Modal TabbedPage with NavigationPage tabs leaks on Android";

		public Issue33918(TestDevice device) : base(device) { }

		[Test]
		[Category(UITestCategories.TabbedPage)]
		public void ModalTabbedPageWithNavigationPageTabsDoesNotLeak()
		{
			// Wait for the page to load
			App.WaitForElement("RunTestButton");

			// Run the memory test
			App.Tap("RunTestButton");

			// Wait for test to complete (up to 10 seconds)
			App.WaitForElement("StatusLabel", timeout: TimeSpan.FromSeconds(10));

			// Give it time to finish the GC and update the status
			Task.Delay(2000).Wait();

			// Verify the test passed
			var statusText = App.FindElement("StatusLabel").GetText();
			
			Assert.That(statusText, Does.Contain("Success"),
				$"Memory leak detected. Status: {statusText}");
		}
	}
}
