using System.Linq;
using CommunityToolkit.Mvvm.Input;

namespace ComicRack.Mac.ViewModels
{
	/// <summary>
	/// The info panel under the browser (P5.6b, Browse &gt; Info Panel): the selection of the browser feeds its pages (after the 500 ms of <c>previewTimer</c>). The panel
	/// itself is <see cref="InfoPanelViewModel"/>; pages are added to <see cref="InfoPanel"/> (<c>ISidebar.AddInfo</c>).
	/// </summary>
	public sealed partial class MainViewModel
	{
		/// <summary>The info panel (<c>ComicExplorerView.pluginContainer</c>).</summary>
		public InfoPanelViewModel InfoPanel { get; private set; }

		private void WireInfo()
		{
			InfoPanel = new InfoPanelViewModel(preferences)
			{
				SelectedBooks = () => Browser.SelectedBooks.ToArray(),
			};
			Browser.SelectionChanged += (sender, e) => InfoPanel.SelectionChanged();
			InfoPanel.PropertyChanged += (sender, e) =>
			{
				if (e.PropertyName == nameof(InfoPanelViewModel.IsVisible))
				{
					OnPropertyChanged(nameof(IsInfoPanelVisible));
				}
				else if (e.PropertyName == nameof(InfoPanelViewModel.HasPages))
				{
					OnPropertyChanged(nameof(HasInfoPanels));
				}
			};
		}

		/// <summary>Browse &gt; Info Panel is checked (<c>CheckInfoPanelEnabled</c>).</summary>
		public bool IsInfoPanelVisible => InfoPanel.IsVisible;

		/// <summary>Browse &gt; Info Panel is available (<c>CheckInfoPanelAvailable</c>, <c>ISidebar.HasInfoPanels</c>): a page was added.</summary>
		public bool HasInfoPanels => InfoPanel.HasPages;

		/// <summary>Browse &gt; Info Panel (<c>ToggleInfoPanel</c>, Shift+F9).</summary>
		public IRelayCommand ToggleInfoPanelCommand => InfoPanel.ToggleCommand;
	}
}
