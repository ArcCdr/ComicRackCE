using System;
using System.Collections.Generic;
using Avalonia.Controls;
using ComicRack.Mac.Editor;

namespace ComicRack.Mac.Views
{
	/// <summary>The popup of a box that holds a list (<c>ListSelectorControl.ShowPopup</c>): the selector opens under its button and the box takes the result when the popup closes.</summary>
	public static class ListSelectorFlyout
	{
		/// <summary>Opens the selector for <paramref name="text"/> with <paramref name="values"/> as the pool; <paramref name="assign"/> gets the result when the popup closes.</summary>
		public static Flyout Open(Control anchor, string text, List<string> values, Action<string> assign, PlacementMode placement = PlacementMode.BottomEdgeAlignedRight)
		{
			var selector = new ListSelectorModel(text, values);
			var view = new ListSelectorView();
			view.Attach(selector);
			var flyout = new Flyout { Content = view, Placement = placement };
			flyout.Closed += (sender, e) => assign(view.Result());
			flyout.ShowAt(anchor);
			return flyout;
		}
	}
}
