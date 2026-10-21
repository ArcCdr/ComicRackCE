using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Diagnostics;
using ComicRack.Mac.Reader;
using ComicRack.Mac.ViewModels;

namespace ComicRack.Mac.Views
{
	/// <summary>
	/// The reader in its own window (<c>ReaderForm</c>, Display > Reader in own Window): a window around a <see cref="ReaderArea"/>. The main window shows the
	/// tab strip, the status bar and the browser; this window shows the book of the current tab, follows the current tab (<see cref="Display"/>) and, when the
	/// user closes it, the reader goes back into the main window. A window made for one reader with <paramref name="ownsModel"/> (the tests do) disposes it
	/// when it is closed.
	/// </summary>
	public partial class ReaderWindow : Window
	{
		private readonly bool ownsModel;
		private readonly PerfOverlayController perf;
		private ReaderViewModel model;

		public ReaderWindow()
		{
			InitializeComponent();
		}

		public ReaderWindow(ReaderViewModel model, bool ownsModel = false, PerfOverlayController perf = null, INavigationThumbnailSource thumbnails = null) : this()
		{
			this.ownsModel = ownsModel;
			this.perf = perf;
			if (perf != null)
			{
				Area.Surface.Children.Add(new PerfOverlay(perf, StopwatchClock.Now));
			}
			Display(model, thumbnails);
			if (ownsModel && model != null)
			{
				// a window that owns its reader closes with it (the tabs of the main window do this through the ReaderManager)
				model.CloseRequested += OnCloseRequested;
			}
			Opened += (sender, e) => ReadingArea.Focus();
		}

		private void OnCloseRequested(object sender, EventArgs e) => Close();

		public ReaderViewModel Model => model;

		/// <summary>The reader area the window is made of.</summary>
		public ReaderArea Area => Pane;

		/// <summary>Shows another book (the current tab changed) or none (null: the window is empty, the main window hides it).</summary>
		public void Display(ReaderViewModel next, INavigationThumbnailSource thumbnails)
		{
			if (model != null)
			{
				model.PropertyChanged -= OnModelChanged;
			}
			model = next;
			Area.Thumbnails = thumbnails;
			Area.DataContext = next;
			if (model != null)
			{
				model.PropertyChanged += OnModelChanged;
			}
			Title = model?.BookSegmentText ?? UiText.Default("None", "None");
		}

		public ReaderView ReadingArea => Area.ReadingArea;

		public CrTextOverlay PageInfo => Area.PageInfo;

		/// <summary>"Loading Page N..." in the middle of the page.</summary>
		public CrTextOverlay Loading => Area.Loading;

		/// <summary>The message in the middle of the page: opening the book, why it or a page could not be shown.</summary>
		public CrTextOverlay Message => Area.Message;

		/// <summary>The miniature of the page with the visible part, at the bottom right.</summary>
		public CrPartOverlay Part => Area.Part;

		public CrNavigationOverlay Navigation => Area.Navigation;

		private void OnModelChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(ReaderViewModel.Title))
			{
				Title = model.BookSegmentText;
			}
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);
			if (!e.Handled && perf != null && e.Key == MainMenu.PerfKey && e.KeyModifiers == MainMenu.PerfModifiers)
			{
				perf.Toggle();
				e.Handled = true;
				return;
			}
			// the area handles the keys while the page has the focus; this is for the moments when it has not
			if (!e.Handled && model != null && model.HandleKey(e.Key, e.KeyModifiers))
			{
				e.Handled = true;
			}
		}

		protected override void OnClosed(EventArgs e)
		{
			Area.DataContext = null;
			Area.Thumbnails = null;
			if (model != null)
			{
				model.PropertyChanged -= OnModelChanged;
				if (ownsModel)
				{
					model.CloseRequested -= OnCloseRequested;
					model.Dispose();
				}
			}
			base.OnClosed(e);
		}
	}
}
