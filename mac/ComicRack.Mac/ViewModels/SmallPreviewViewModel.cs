using System;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ComicRack.Mac.Host;
using ComicRack.Mac.Reader;
using ComicRack.Mac.Services;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Display;

namespace ComicRack.Mac.ViewModels
{
	/// <summary>
	/// The small preview under the tree of the side panel (<c>SmallComicPreview</c> and its host in <c>ComicExplorerView</c>, Browse &gt; Small Preview, Shift+F7): it
	/// shows the first selected book of the browser in a reader of its own. The selection settles for 500 ms (<c>previewTimer</c>) before the book is opened; the book
	/// shown is a copy (<c>new ComicBook(first)</c>) opened as <c>OpenComicOptions.DisableAll</c> says (from the first page, nothing is written back: see
	/// <see cref="CoreReadingSession.IsPreview"/>), so previewing never changes the library. The same book stays open while it stays the first selected one
	/// (the original compares the file paths).
	///
	/// <para>The buttons are those of <c>toolStripPreview</c>: Open (a new tab for the book, <c>OpenInNewSlot</c>), First, Previous, Next, Last, Two Pages (a
	/// single or an adaptive double page, <see cref="TwoPageDisplay"/>), Refresh, Close (turns the preview off). The reader is the same
	/// <see cref="ReaderViewModel"/> as the reading area uses, without its overlays.</para>
	///
	/// <para>The preview is visible when <see cref="IsVisible"/> is on and the side panel is open (<see cref="SidebarAvailable"/>: the pane lives in the side panel and
	/// <c>UpdatePreview</c> looks at <c>ISidebar.Visible</c>); while it is not, the book that is open stays open and nothing is opened.</para>
	/// </summary>
	public sealed class SmallPreviewViewModel : ObservableObject, IDisposable
	{
		/// <summary><c>previewTimer.Interval</c>: how long the selection has to rest before the preview shows it.</summary>
		public static readonly TimeSpan ShowDelay = TimeSpan.FromMilliseconds(500);

		private readonly AppPreferences preferences;
		private IDisposable pending;
		private ReaderViewModel reader;
		private ComicBook shown;
		private ComicBook source;
		private bool isVisible;
		private double extent = AppPreferences.DefaultPreviewSplit;
		private bool twoPages;
		private string text;
		private int generation;
		private bool disposed;

		/// <param name="preferences">Where Show, the height and Two Pages are kept (<c>ShowPreview</c>, <c>PreviewSplit</c>, <c>TwoPagePreview</c>); null keeps nothing.</param>
		/// <param name="scheduler">Runs the 500 ms timer; a test passes one it releases by hand. The default is a thread pool timer whose action is posted to the UI thread.</param>
		public SmallPreviewViewModel(AppPreferences preferences = null, ISaveScheduler scheduler = null)
		{
			this.preferences = preferences;
			Scheduler = scheduler ?? new TimerSaveScheduler();
			text = NothingSelectedText;
			if (preferences != null)
			{
				isVisible = preferences.ShowPreview;
				twoPages = preferences.TwoPagePreview;
				double split = preferences.PreviewSplit;
				extent = double.IsNaN(split) || double.IsInfinity(split) ? AppPreferences.DefaultPreviewSplit : Math.Clamp(split, MinimumExtent, 5000);
			}
			OpenCommand = new RelayCommand(Open, () => HasBook);
			FirstCommand = new RelayCommand(() => reader?.FirstPage(), () => reader != null && reader.CanGoBack);
			PreviousCommand = new RelayCommand(() => reader?.PreviousPage(), () => reader != null && reader.CanGoBack);
			NextCommand = new RelayCommand(() => reader?.NextPage(), () => reader != null && reader.CanGoForward);
			LastCommand = new RelayCommand(() => reader?.LastPage(), () => reader != null && reader.CanGoForward);
			TwoPagesCommand = new RelayCommand(() => TwoPageDisplay = !TwoPageDisplay, () => HasBook);
			RefreshCommand = new RelayCommand(() => reader?.Refresh(), () => HasBook);
			CloseCommand = new RelayCommand(() => IsVisible = false);
		}

		/// <summary>The smallest height a drag of the grip can leave: the grip and a sliver (<c>CrSizableContainer.MinimumExtent</c>).</summary>
		public const double MinimumExtent = 26;

		/// <summary>The text of <c>pageViewer</c> with nothing to show: <c>Nothing Selected</c>.</summary>
		public static string NothingSelectedText => UiText.Get("SmallComicPreview", "pageViewer", "Nothing Selected");

		/// <summary>The text of the page viewer for a book that has no file: <c>Preview is only available for Books</c>.</summary>
		public static string OnlyForBooksText => UiText.Get("SmallComicPreview", "PreviewOnlyForComics", "Preview is only available for Books");

		/// <summary>Runs the 500 ms timer; a test replaces it with one it releases by hand.</summary>
		public ISaveScheduler Scheduler { get; set; }

		/// <summary>Makes the reading session of a book copy; the default is the Core's (<see cref="CoreReadingSession"/> as a preview), a test supplies its own.</summary>
		public Func<ComicBook, IReadingSession> SessionFactory { get; set; }

		/// <summary>The first selected book of the browser (<c>GetBookList(Selected).First()</c>), or null.</summary>
		public Func<ComicBook> FirstSelectedBook { get; set; }

		/// <summary>Whether the side panel, which holds the pane, is open (<c>ISidebar.Visible</c>).</summary>
		public Func<bool> SidebarAvailable { get; set; } = () => true;

		/// <summary>Open (<c>OpenBook</c>): the book of the selection in a new tab. Set by the main view model.</summary>
		public Action<ComicBook> OpenBook { get; set; }

		/// <summary>The reader that shows the book, or null.</summary>
		public ReaderViewModel Reader => reader;

		/// <summary>The book of the library that the preview shows, or null.</summary>
		public ComicBook Source => source;

		public bool HasBook => reader != null;

		/// <summary>The text over the page viewer: <c>Nothing Selected</c>, <c>Preview is only available for Books</c>, why the book did not open, or empty while a book is shown.</summary>
		public string Text
		{
			get => text;
			private set => SetProperty(ref text, value);
		}

		/// <summary>Browse &gt; Small Preview (<c>ISidebar.Preview</c>, <c>previewPane.Expanded</c>): the pane is open. Kept in the preferences.</summary>
		public bool IsVisible
		{
			get => isVisible;
			set
			{
				if (SetProperty(ref isVisible, value))
				{
					if (preferences != null)
					{
						preferences.ShowPreview = value;
					}
					UpdateNow();
				}
			}
		}

		/// <summary>The height of the pane when it is open, grip included (<c>previewPane.ExpandedWidth</c>, 207). Kept in the preferences.</summary>
		public double Extent
		{
			get => extent;
			set
			{
				double clamped = double.IsNaN(value) || double.IsInfinity(value) ? AppPreferences.DefaultPreviewSplit : Math.Max(MinimumExtent, value);
				if (SetProperty(ref extent, clamped) && preferences != null)
				{
					preferences.PreviewSplit = clamped;
				}
			}
		}

		/// <summary><c>TwoPageDisplay</c>: two pages (adaptive) or one. Kept in the preferences.</summary>
		public bool TwoPageDisplay
		{
			get => twoPages;
			set
			{
				if (SetProperty(ref twoPages, value))
				{
					if (preferences != null)
					{
						preferences.TwoPagePreview = value;
					}
					ApplyLayout();
				}
			}
		}

		public IRelayCommand OpenCommand { get; }

		public IRelayCommand FirstCommand { get; }

		public IRelayCommand PreviousCommand { get; }

		public IRelayCommand NextCommand { get; }

		public IRelayCommand LastCommand { get; }

		public IRelayCommand TwoPagesCommand { get; }

		public IRelayCommand RefreshCommand { get; }

		public IRelayCommand CloseCommand { get; }

		// ---- the timer and the selection

		/// <summary>True while the 500 ms timer runs (a selection change was seen and the preview has not been updated yet).</summary>
		public bool IsPending => pending != null;

		/// <summary>The selection (or a selected book) changed: the timer starts again (<c>RestartPreviewTimer</c>).</summary>
		public void SelectionChanged()
		{
			if (disposed)
			{
				return;
			}
			pending?.Dispose();
			IDisposable mine = null;
			mine = Scheduler.Schedule(ShowDelay, () => UiDispatch.Post(() =>
			{
				if (ReferenceEquals(pending, mine))
				{
					pending = null;
					Update();
				}
			}));
			pending = mine;
		}

		/// <summary>Updates the preview at once (<c>sidePanel_ExpandedChanged</c> and <c>previewPane.ExpandedChanged</c> do not wait for the timer).</summary>
		public void UpdateNow()
		{
			pending?.Dispose();
			pending = null;
			Update();
		}

		/// <summary><c>UpdatePreview</c>: shows the first selected book if the preview is on and the side panel is open.</summary>
		public void Update()
		{
			if (disposed || !isVisible || !SidebarAvailable())
			{
				return;
			}
			ComicBook first = FirstSelectedBook?.Invoke();
			ShowPreview(first);
		}

		// ---- the book

		/// <summary>
		/// <c>SmallComicPreview.ShowPreview</c>: no book closes the one that is open; another book (another file) replaces it; the same file keeps it open as it is.
		/// A book without a file says <see cref="OnlyForBooksText"/>.
		/// </summary>
		public void ShowPreview(ComicBook book)
		{
			if (disposed)
			{
				return;
			}
			if (book == null)
			{
				CloseReader();
				source = null;
			}
			else if (reader == null || !string.Equals(book.FilePath, shown?.FilePath, StringComparison.Ordinal))
			{
				CloseReader();
				source = book;
				OpenReader(book);
			}
			else
			{
				source = book;
			}
			if (reader != null)
			{
				// a book that failed to open at once has already said why (OpenAsync)
				Text = reader.OpenFailed ? Text : string.Empty;
			}
			else
			{
				Text = book == null || book.IsLinked ? NothingSelectedText : OnlyForBooksText;
			}
		}

		private void OpenReader(ComicBook book)
		{
			if (!book.IsLinked)
			{
				return;
			}
			var copy = new ComicBook(book);
			IReadingSession session = SessionFactory != null ? SessionFactory(copy) : null;
			if (session == null)
			{
				return;
			}
			shown = copy;
			var options = new ReaderOptions
			{
				Fit = ImageFitMode.Fit,
				Mode = twoPages ? PageLayoutMode.DoubleAdaptive : PageLayoutMode.Single,
				Ahead = 1,
				Behind = 0,
				Log = null, // the page turns of the preview are not measured
			};
			var model = new ReaderViewModel(session, options);
			reader = model;
			model.PageInfoChanged += OnPageChanged;
			model.PropertyChanged += OnReaderChanged;
			int mine = ++generation;
			AppLog.Info("Small preview: showing " + System.IO.Path.GetFileName(book.FilePath) + ".");
			OnPropertyChanged(nameof(Reader));
			OnPropertyChanged(nameof(HasBook));
			RaiseCanExecute();
			_ = OpenAsync(model, mine);
		}

		private async Task OpenAsync(ReaderViewModel model, int mine)
		{
			await model.OpenAsync();
			if (disposed || mine != generation || !ReferenceEquals(reader, model))
			{
				return;
			}
			if (model.OpenFailed)
			{
				Text = model.Notice ?? NothingSelectedText;
			}
			RaiseCanExecute();
		}

		private void CloseReader()
		{
			ReaderViewModel old = reader;
			generation++;
			reader = null;
			shown = null;
			if (old != null)
			{
				old.PageInfoChanged -= OnPageChanged;
				old.PropertyChanged -= OnReaderChanged;
				OnPropertyChanged(nameof(Reader));
				OnPropertyChanged(nameof(HasBook));
				old.Dispose();
			}
			RaiseCanExecute();
		}

		private void ApplyLayout()
		{
			reader?.SetLayoutMode(twoPages ? PageLayoutMode.DoubleAdaptive : PageLayoutMode.Single);
			RaiseCanExecute();
		}

		private void OnPageChanged(object sender, EventArgs e)
		{
			if (reader != null)
			{
				AppLog.Info("Small preview: page " + (reader.CurrentPage + 1) + " of " + reader.PageCount + ".");
			}
			RaiseCanExecute();
		}

		private void OnReaderChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(ReaderViewModel.CurrentPage) || e.PropertyName == nameof(ReaderViewModel.ModeText) || e.PropertyName == nameof(ReaderViewModel.PageCountSegmentText))
			{
				RaiseCanExecute();
			}
		}

		private void RaiseCanExecute()
		{
			OpenCommand.NotifyCanExecuteChanged();
			FirstCommand.NotifyCanExecuteChanged();
			PreviousCommand.NotifyCanExecuteChanged();
			NextCommand.NotifyCanExecuteChanged();
			LastCommand.NotifyCanExecuteChanged();
			TwoPagesCommand.NotifyCanExecuteChanged();
			RefreshCommand.NotifyCanExecuteChanged();
		}

		private void Open()
		{
			if (source != null)
			{
				OpenBook?.Invoke(source);
			}
		}

		/// <summary>Closes the book and the timer (before the library is replaced, and at quit).</summary>
		public void Close()
		{
			pending?.Dispose();
			pending = null;
			CloseReader();
			source = null;
			Text = NothingSelectedText;
		}

		public void Dispose()
		{
			if (disposed)
			{
				return;
			}
			Close();
			disposed = true;
		}
	}
}
