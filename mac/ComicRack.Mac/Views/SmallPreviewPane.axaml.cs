using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using ComicRack.Mac.Controls;
using ComicRack.Mac.ViewModels;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// <c>SmallComicPreview</c>: the preview of the first selected book under the tree of the side panel. Its DataContext is the <see cref="SmallPreviewViewModel"/>; the page viewer
	/// shows the reader of that model (<see cref="ReaderView"/>) and its text, Up and Down scroll the page and so does the wheel, a double click opens the book in a new tab
	/// (the preview's keyboard map has <c>MoveUp</c>, <c>MoveDown</c> and <c>OpenBook</c> on top of the display's own), there is no context menu.
	/// </summary>
	public partial class SmallPreviewPane : UserControl
	{
		private SmallPreviewViewModel model;

		public SmallPreviewPane()
		{
			InitializeComponent();
			ApplyTexts();
			PageView.DoubleClicked += (sender, e) => model?.OpenCommand.Execute(null);
		}

		/// <summary>The toolbar (<c>toolStripPreview</c>).</summary>
		public CrToolBar Toolbar => PreviewToolBar;

		/// <summary>The page viewer (<c>pageViewer</c>).</summary>
		public ReaderView Viewer => PageView;

		// The tool tip of a button is its ToolTipText when it has one, else its Text (SmallComicPreview.xml: tsbOpen, tsbRefresh, tbClose have no tool tip key).
		private void ApplyTexts()
		{
			Tip("PreviewOpenButton", "tsbOpen", "Open", null);
			Tip("PreviewFirstButton", "tsbFirst", "First", "Go to first page");
			Tip("PreviewPreviousButton", "tsbPrev", "Previous", "Go to previous page");
			Tip("PreviewNextButton", "tsbNext", "Next", "Go to next page");
			Tip("PreviewLastButton", "tsbLast", "Last", "Go to last page");
			Tip("PreviewTwoPagesButton", "tsbTwoPages", "Two Pages", "Show one or two pages");
			Tip("PreviewRefreshButton", "tsbRefresh", "Refresh", null);
			Tip("PreviewCloseButton", "tbClose", "Close", null);
		}

		private void Tip(string name, string key, string english, string tooltipEnglish)
		{
			string text = UiText.Get("SmallComicPreview", key, english);
			string tip = tooltipEnglish == null ? text : UiText.Get("SmallComicPreview", key + ".Tooltip", tooltipEnglish);
			ToolTip.SetTip(this.FindControl<CrToolButton>(name), tip);
		}

		protected override void OnDataContextChanged(EventArgs e)
		{
			base.OnDataContextChanged(e);
			if (model != null)
			{
				model.PropertyChanged -= OnModelChanged;
			}
			model = DataContext as SmallPreviewViewModel;
			if (model != null)
			{
				model.PropertyChanged += OnModelChanged;
			}
			Sync();
		}

		private void OnModelChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(SmallPreviewViewModel.Reader) || e.PropertyName == nameof(SmallPreviewViewModel.Text))
			{
				Sync();
			}
		}

		// The page viewer shows the reader of the model, and the text of the model over it.
		private void Sync()
		{
			PageView.DataContext = model?.Reader;
			PageView.PlaceholderText = model?.Text;
			PageView.InvalidateVisual();
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);
			if (e.Handled || model?.Reader == null || e.KeyModifiers != KeyModifiers.None)
			{
				return;
			}
			// MoveUp and MoveDown: Up and Down scroll the page
			if (e.Key == Key.Up)
			{
				model.Reader.ScrollBy(-model.Reader.Options.ArrowScrollStep);
				e.Handled = true;
			}
			else if (e.Key == Key.Down)
			{
				model.Reader.ScrollBy(model.Reader.Options.ArrowScrollStep);
				e.Handled = true;
			}
		}
	}
}
