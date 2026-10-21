using System;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using ComicRack.Mac.Host;
using ComicRack.Mac.Reader;
using ComicRack.Mac.Services;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.ViewModels
{
	/// <summary>
	/// The small preview of the side panel (P5.6a, Browse &gt; Small Preview): the selection of the browser feeds it (after the 500 ms of <c>previewTimer</c>), it
	/// follows the side panel and the host, and Open puts the book into a new tab. The preview itself is <see cref="SmallPreviewViewModel"/>.
	/// </summary>
	public sealed partial class MainViewModel
	{
		/// <summary>The small preview under the tree of the side panel.</summary>
		public SmallPreviewViewModel SmallPreview { get; private set; }

		private void WirePreview()
		{
			SmallPreview = new SmallPreviewViewModel(preferences)
			{
				FirstSelectedBook = () => Browser.SelectedBooks.FirstOrDefault(),
				SidebarAvailable = () => Sidebar.IsVisible,
				SessionFactory = book =>
				{
					MacHost host = hostService.Host;
					return host == null ? null : new CoreReadingSession(book, host.ImagePool) { IsPreview = true };
				},
				OpenBook = book =>
				{
					if (tabs != null)
					{
						tabs.Open(book, OpenBookOptions.InNewTab);
					}
					else
					{
						readers?.Open(book);
					}
				},
			};
			Browser.SelectionChanged += (sender, e) => SmallPreview.SelectionChanged();
			Sidebar.PropertyChanged += (sender, e) =>
			{
				if (e.PropertyName == nameof(SidebarViewModel.IsVisible))
				{
					SmallPreview.UpdateNow();
				}
			};
			SmallPreview.PropertyChanged += (sender, e) =>
			{
				if (e.PropertyName == nameof(SmallPreviewViewModel.IsVisible))
				{
					OnPropertyChanged(nameof(IsSmallPreviewVisible));
				}
			};
			// the preview reads through the open library's page cache: it goes before the library does
			hostService.Closing += (sender, e) => SmallPreview.Close();
		}

		/// <summary>Browse &gt; Small Preview is checked (<c>CheckSmallPreviewEnabled</c>).</summary>
		public bool IsSmallPreviewVisible => SmallPreview.IsVisible;

		/// <summary>Browse &gt; Small Preview (<c>ToggleSmallPreview</c>, Shift+F7): opens or closes the preview pane.</summary>
		[RelayCommand]
		private void ToggleSmallPreview() => SmallPreview.IsVisible = !SmallPreview.IsVisible;
	}
}
