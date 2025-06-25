using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Xunit;

namespace Microsoft.Maui.DeviceTests.Memory;

[Category(TestCategory.Memory)]
public class CollectionViewMemoryLeakTests : ControlsHandlerTestBase
{
	[Fact("CollectionView ItemsSource Model Objects Do Not Leak")]
	public async Task CollectionViewItemsSourceModelObjectsDoNotLeak()
	{
		SetupBuilder();

		var modelReferences = new List<WeakReference>();
		var collectionViewReference = new WeakReference(null);
		var pageReference = new WeakReference(null);

		var navPage = new NavigationPage(new ContentPage { Title = "Root Page" });

		await CreateHandlerAndAddToWindow(new Window(navPage), async () =>
		{
			// Create test model objects
			var models = new List<TestModel>();
			for (int i = 0; i < 10; i++)
			{
				var model = new TestModel { Id = i, Name = $"Item {i}" };
				models.Add(model);
				modelReferences.Add(new WeakReference(model));
			}

			var observable = new ObservableCollection<TestModel>(models);

			// Create page with CollectionView
			var page = new ContentPage
			{
				Title = "CollectionView Page",
				Content = new CollectionView
				{
					ItemsSource = observable,
					ItemTemplate = new DataTemplate(() =>
					{
						var label = new Label();
						label.SetBinding(Label.TextProperty, "Name");
						return label;
					})
				}
			};

			pageReference.Target = page;
			collectionViewReference.Target = ((ContentPage)page).Content;

			// Navigate to the page
			await navPage.Navigation.PushAsync(page);

			// Clear local references
			models = null;
			observable = null;
			page = null;

			// Navigate away from the page
			await navPage.Navigation.PopAsync();
		});

		// Force garbage collection and wait
		await AssertionExtensions.WaitForGC(modelReferences.ToArray());
		await AssertionExtensions.WaitForGC(collectionViewReference, pageReference);
	}

	public class TestModel
	{
		public int Id { get; set; }
		public string Name { get; set; }
	}
}