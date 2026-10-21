using System;
using System.Collections.Generic;
using Avalonia.Controls;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.Info
{
	/// <summary>
	/// A page of the info panel (<c>ComicPageControl</c>, hosted by <c>ComicPageContainerControl</c> under the browser). The original has no built-in page: pages come only
	/// from scripts (the <c>ComicInfoHtml</c> and <c>ComicInfoUI</c> hooks, P4.11a, which implements this interface and calls
	/// <see cref="ViewModels.InfoPanelViewModel.AddPage"/>, <c>ISidebar.AddInfo</c> in the original).
	/// </summary>
	public interface IInfoPage
	{
		/// <summary>The text of the page's tab (<c>Control.Text</c>); changes are announced by <see cref="TitleChanged"/>.</summary>
		string Title { get; }

		/// <summary>The icon of the tab by name (<c>ComicPageControl.Icon</c>), or null for none.</summary>
		string IconName { get; }

		/// <summary>The page's content. Created when first asked for, on the UI thread.</summary>
		Control View { get; }

		/// <summary>The title changed (<c>Control.TextChanged</c>).</summary>
		event EventHandler TitleChanged;

		/// <summary>
		/// <c>ComicPageControl.OnShowInfo</c>: shows the selected books. It is called only while the page is the page that is showing; a page that is not showing gets the
		/// books (or, after <see cref="InfoPageItem.MarkAsDirty"/>, an empty list) when its tab is selected.
		/// </summary>
		void ShowInfo(IReadOnlyList<ComicBook> books);
	}
}
