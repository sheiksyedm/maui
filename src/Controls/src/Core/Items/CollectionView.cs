using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace Microsoft.Maui.Controls
{
	/// <include file="../../../docs/Microsoft.Maui.Controls/CollectionView.xml" path="Type[@FullName='Microsoft.Maui.Controls.CollectionView']/Docs/*" />
	public class CollectionView : ReorderableItemsView
	{
		private Element? _previousParent;

		/// <summary>
		/// Initializes a new instance of the <see cref="CollectionView"/> class.
		/// </summary>
		public CollectionView()
		{
			PropertyChanging += OnCollectionViewPropertyChanging;
			PropertyChanged += OnCollectionViewPropertyChanged;
		}

		private void OnCollectionViewPropertyChanging(object? sender, PropertyChangingEventArgs e)
		{
			if (e.PropertyName == nameof(Parent))
			{
				// Store the current parent before it changes
				_previousParent = Parent;
			}
		}

		private void OnCollectionViewPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(Parent))
			{
				var currentParent = Parent;
				
				// If we're being removed from a Page (going from a Page parent to null),
				// clear the ItemsSource to allow garbage collection of model objects
				if (currentParent == null && IsDescendantOfPage(_previousParent))
				{
					ItemsSource = null;
				}
			}
		}

		/// <summary>
		/// Determines if the given element is a descendant of a Page.
		/// </summary>
		/// <param name="element">The element to check</param>
		/// <returns>True if the element is or is a descendant of a Page</returns>
		private static bool IsDescendantOfPage(Element? element)
		{
			while (element != null)
			{
				if (element is Page)
					return true;
				element = element.Parent;
			}
			return false;
		}
	}
}
