using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Services;
using ComicRack.Mac.Thumbnails;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.ViewModels
{
	/// <summary>How a book that the browser opens is placed among the tabs.</summary>
	public enum OpenTabMode
	{
		/// <summary>As the setting Open in new Tab says.</summary>
		Default,

		/// <summary>The opposite of the setting (a held Cmd or Shift).</summary>
		Invert,

		/// <summary>In a tab of its own, whatever the setting says.</summary>
		NewTab,
	}

	/// <summary>The commands of the book context menu that need more than the browser (the main view model carries them out; <see cref="BrowserViewModel.Run"/>).</summary>
	public enum BookCommand
	{
		/// <summary>Info... (<c>miProperties</c>, Cmd+I): the information of the selected books.</summary>
		Info,

		/// <summary>Reveal in Finder (<c>miRevealBrowser</c>, Cmd+G): the file of the first selected book that has one.</summary>
		Reveal,

		/// <summary>Mark as Read (<c>miMarkRead</c>, Option+Shift+R).</summary>
		MarkRead,

		/// <summary>Mark as Unread (<c>miMarkUnread</c>, Option+Shift+U).</summary>
		MarkUnread,

		/// <summary>Mark as Checked (<c>miMarkChecked</c>, Option+Shift+M).</summary>
		MarkChecked,

		/// <summary>Mark as Unchecked (<c>miMarkUnchecked</c>, Option+Shift+V).</summary>
		MarkUnchecked,

		/// <summary>My Rating (<c>miRating0</c> to <c>miRating5</c>, Option+Shift+0 to 5, or the stars): the rating is the value.</summary>
		Rate,

		/// <summary>Quick Rating and Review... (<c>miQuickRating</c>, Option+Shift+Q).</summary>
		QuickRating,
	}

	/// <summary>A <see cref="BookCommand"/> to carry out on the selected books.</summary>
	public sealed class BookCommandEventArgs : EventArgs
	{
		public BookCommandEventArgs(BookCommand command, IReadOnlyList<ComicBook> books, float value)
		{
			Command = command;
			Books = books;
			Value = value;
		}

		public BookCommand Command { get; }

		/// <summary>The selected books, in the order of the grid.</summary>
		public IReadOnlyList<ComicBook> Books { get; }

		/// <summary>The rating of <see cref="BookCommand.Rate"/>.</summary>
		public float Value { get; }
	}

	/// <summary>
	/// State of the book grid: the catalog (a snapshot of the library), the search text, the sort, the thumbnail size, the
	/// selection and the list the grid shows.
	///
	/// <b>Nothing here sorts or filters on the UI thread.</b> A change of the search text or the sort starts a query on a worker
	/// thread (<see cref="BookCatalog.Query"/>); the previous query is cancelled, a search waits <see cref="SearchDelay"/> for the next
	/// keystroke first, and only the result of the newest query is applied (on the UI thread, as one assignment of
	/// <see cref="Items"/>). Building the catalog from the Core's books is a worker thread job too (<see cref="LoadAsync"/>).
	/// </summary>
	public sealed partial class BrowserViewModel : ObservableObject
	{
		private static readonly string[] sortNames = { "Series", "Title", "Date added" };

		private long shownSize;
		private int shownCount;

		/// <summary>Names for the sort box; the index is <see cref="SortIndex"/>.</summary>
		public static string[] SortNames => sortNames;

		public string[] SortChoices => sortNames;

		public const double MinThumbnailSize = 80;
		public const double MaxThumbnailSize = 360;
		public const double DefaultThumbnailSize = 200;

		private BookCatalog catalog = BookCatalog.Empty;
		private CancellationTokenSource queryCts = new CancellationTokenSource();
		private int version;
		private int loadVersion;
		private bool scrollReset;
		private Task lastQuery = Task.CompletedTask;

		public BrowserViewModel()
		{
			SummaryText = string.Empty;
			ThumbnailOptions.MarkerOf = MarkerOf;
			Selection.Changed += (sender, e) =>
			{
				if (!selectionMuted)
				{
					OnSelectionChanged();
				}
			};
		}

		/// <summary>Whether a book is open in a reader (the state icon "open"); set by the window that owns the readers, null: none is.</summary>
		public Func<ComicBook, bool> IsBookOpen { get; set; }

		private ComicBook lastOpened;

		/// <summary>The book of the shown list that was opened last (the "last opened" state icon), or null.</summary>
		public ComicBook LastOpenedBook => lastOpened;

		// CoverViewItem.Marker: open in a reader wins over "last opened" (ComicBrowserControl.UpdateBookMarkers).
		private ThumbnailMarker MarkerOf(ComicBook book)
		{
			if (IsBookOpen?.Invoke(book) == true)
			{
				return ThumbnailMarker.IsOpen;
			}
			return book != null && ReferenceEquals(book, lastOpened) ? ThumbnailMarker.IsLast : ThumbnailMarker.None;
		}

		private void SetLastOpened(ComicBook book)
		{
			if (!ReferenceEquals(lastOpened, book))
			{
				lastOpened = book;
				ThumbnailOptions.RaiseChanged();
			}
		}

		/// <summary>The open books changed (a reader was opened or closed): the state icons of the cells are drawn again.</summary>
		public void NotifyMarkersChanged() => ThumbnailOptions.RaiseChanged();

		/// <summary>Raised on the UI thread with the books that changed (a page was read, a rating was set): the cells that show them are drawn again.</summary>
		public event EventHandler<IReadOnlyCollection<ComicBook>> BooksChanged;

		/// <summary>
		/// Books of the library had a property changed. The cells that show them draw their state again; a book that was opened more recently than the
		/// "last opened" one takes the marker (when only a few changed: after a scan the refresh counts them all).
		/// </summary>
		public void NotifyBooksChanged(IReadOnlyCollection<ComicBook> books)
		{
			if (books == null || books.Count == 0)
			{
				return;
			}
			if (books.Count <= 50)
			{
				DateTime t = lastOpened?.OpenedTime ?? DateTime.MinValue;
				ComicBook newest = null;
				HashSet<ComicBook> shown = null;
				foreach (ComicBook book in books)
				{
					if (book.OpenedTime > t)
					{
						shown ??= ShownBooks();
						if (shown.Contains(book))
						{
							t = book.OpenedTime;
							newest = book;
						}
					}
				}
				if (newest != null)
				{
					SetLastOpened(newest);
				}
			}
			BooksChanged?.Invoke(this, books);
		}

		// The Core books of the items that are shown, for a membership test (the change batch is checked once, not once per book).
		private HashSet<ComicBook> ShownBooks()
		{
			var set = new HashSet<ComicBook>(ReferenceEqualityComparer.Instance);
			foreach (BookItem item in Items)
			{
				if (item.Book != null)
				{
					set.Add(item.Book);
				}
			}
			return set;
		}

		partial void OnListNameChanged(string value) => UpdateSummary();

		// Writes the status text from the books shown and the selected book (UI thread).
		private void UpdateSummary()
		{
			int count = Selection.Count;
			string path = null;
			long size = 0;
			if (count > 0)
			{
				size = Selection.SelectedSize();
				if (count == 1)
				{
					// the path of the one selected book (GetBookList(IsLocal | IsNotFileless | Selected).FirstOrDefault())
					BookItem only = Selection.SelectedItems()[0];
					path = only.Book != null && only.Book.IsLinked ? only.Book.FilePath : null;
				}
			}
			SummaryText = SelectionInfo.Build(ListName, shownCount, TotalCount, shownSize, count, path, size);
		}

		/// <summary>How long a typed search waits for the next keystroke before a query starts.</summary>
		public TimeSpan SearchDelay { get; set; } = TimeSpan.FromMilliseconds(120);

		/// <summary>The books to show, filtered and sorted. Replaced as a whole (never modified), so the grid can keep a reference.</summary>
		[ObservableProperty]
		public partial BookItem[] Items { get; set; } = Array.Empty<BookItem>();

		[ObservableProperty]
		public partial string SearchText { get; set; } = string.Empty;

		/// <summary>Index into <see cref="SortNames"/>.</summary>
		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(SortName))]
		public partial int SortIndex { get; set; }

		/// <summary>
		/// The list runs from the end of the sort (<c>ItemView.ItemSortOrder</c> is <c>Descending</c>). The Arrange button flips it
		/// (<c>ComicBrowserControl.tbbSort_ButtonClick</c>, <c>ItemView.FlipSortOrder</c>) and so does choosing the sort the list already has from the
		/// menu (<c>ItemView.ArrangeMenuItemClicked</c>).
		/// </summary>
		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(SortIconName))]
		public partial bool SortDescending { get; set; }

		/// <summary>The picture of the Arrange button: <c>SortUp</c> while ascending, <c>SortDown</c> while descending (<c>ComicBrowserControl</c>: <c>tbbSort.Image</c>).</summary>
		public string SortIconName => SortDescending ? "SortDown" : "SortUp";

		/// <summary>The click on the Arrange button: the sort order flips.</summary>
		public void FlipSortOrder() => SortDescending = !SortDescending;

		/// <summary>
		/// A sort chosen in the Arrange menu (<c>ItemView.ArrangeMenuItemClicked</c>): the sort that the list has flips its order, another sort starts ascending.
		/// </summary>
		public void ArrangeBy(int index)
		{
			index = Math.Clamp(index, 0, SortNames.Length - 1);
			if (index == SortIndex)
			{
				FlipSortOrder();
				return;
			}
			if (SortDescending)
			{
				using (SuspendQueries())
				{
					SortDescending = false;
					SortIndex = index;
				}
				StartQuery(debounce: false, resetScroll: true);
				return;
			}
			SortIndex = index;
		}

		private int queriesSuspended;

		private IDisposable SuspendQueries()
		{
			queriesSuspended++;
			return new Releaser(() => queriesSuspended--);
		}

		private sealed class Releaser : IDisposable
		{
			private Action action;

			public Releaser(Action action) => this.action = action;

			public void Dispose()
			{
				Action a = action;
				action = null;
				a?.Invoke();
			}
		}

		/// <summary>The text of the Arrange button: the name of the column the list is arranged by (<c>tbbSort.Text</c>).</summary>
		public string SortName => SortNames[Math.Clamp(SortIndex, 0, SortNames.Length - 1)];

		/// <summary>Height of a cover in device independent pixels.</summary>
		[ObservableProperty]
		public partial double ThumbnailSize { get; set; } = DefaultThumbnailSize;

		/// <summary>True while a refresh replaces <see cref="Items"/> and the selection: the view keeps its scroll position and does not scroll to the selected book.</summary>
		public bool KeepsView { get; private set; }

		/// <summary>Which books are selected, which one has the focus (P5.3; the rules of the original's <c>ItemView</c>).</summary>
		public SelectionModel Selection { get; } = new SelectionModel();

		private bool selectionMuted;

		/// <summary>Raised on the UI thread when the selection or the focused book changed (also when a new list was shown).</summary>
		public event EventHandler SelectionChanged;

		/// <summary>
		/// Index into <see cref="Items"/> of the focused book (the one Enter opens), or -1. Setting it selects that book alone, as a click does;
		/// -1 selects nothing.
		/// </summary>
		public int SelectedIndex
		{
			get => Selection.FocusedIndex;
			set
			{
				if (value < 0)
				{
					Selection.Clear();
				}
				else
				{
					Selection.SelectOnly(value);
				}
			}
		}

		/// <summary>The focused book, or null.</summary>
		public BookItem SelectedItem => Selection.FocusedItem;

		/// <summary>The Core books that are selected and shown, in the order of the grid (what the commands of the browser work on).</summary>
		public IReadOnlyList<ComicBook> SelectedBooks => Selection.SelectedBooks();

		/// <summary>Number of selected books that are shown.</summary>
		public int SelectedCount => Selection.Count;

		private void OnSelectionChanged()
		{
			OnPropertyChanged(nameof(SelectedIndex));
			OnPropertyChanged(nameof(SelectedItem));
			OnPropertyChanged(nameof(SelectedCount));
			UpdateSummary();
			SelectionChanged?.Invoke(this, EventArgs.Empty);
		}

		/// <summary>
		/// The status bar text of the browser, as the original writes it (<see cref="SelectionInfo"/>): "Library: 312 Books (14394 filtered) / 1.20 GB -
		/// /Users/me/Comics/a.cbz".
		/// </summary>
		[ObservableProperty]
		public partial string SummaryText { get; set; }

		/// <summary>The name of the list that is shown (the first part of <see cref="SummaryText"/>), or null.</summary>
		[ObservableProperty]
		public partial string ListName { get; set; }

		[ObservableProperty]
		public partial bool IsLoading { get; set; }

		/// <summary>The settings that change how thumbnails look and behave (P5.R2); the grid listens to their changes.</summary>
		public ThumbnailOptions ThumbnailOptions { get; } = new ThumbnailOptions();

		/// <summary>The service the cells request covers from; null while there is no open library (cells then show empty tiles).</summary>
		[ObservableProperty]
		public partial ThumbnailService Thumbnails { get; set; }

		/// <summary>Every shown cover asks for its picture again (after the Info dialog changed pages or a custom thumbnail: the key of the cover is another one).</summary>
		public void RefreshCovers() => OnPropertyChanged(nameof(Thumbnails));

		public BookSort Sort => (BookSort)Math.Clamp(SortIndex, 0, SortNames.Length - 1);

		/// <summary>Books in the catalog (before the search).</summary>
		public int TotalCount => catalog.Count;

		public double LastQueryMilliseconds { get; private set; }

		public double LastCatalogMilliseconds { get; private set; }

		/// <summary>Raised on the UI thread when the user opens a book (double click or Enter).</summary>
		public event EventHandler<BookItem> OpenBookRequested;

		/// <summary>The task of the newest query (completes when its result is applied or it was superseded).</summary>
		public Task WhenIdle()
		{
			return lastQuery;
		}

		/// <summary>True once after a user change of search or sort replaced <see cref="Items"/>: the grid then scrolls to the top.</summary>
		public bool ConsumeScrollReset()
		{
			bool r = scrollReset;
			scrollReset = false;
			return r;
		}

		partial void OnSearchTextChanged(string value) => StartQuery(debounce: true, resetScroll: true);

		partial void OnSortIndexChanged(int value)
		{
			if (queriesSuspended == 0)
			{
				StartQuery(debounce: false, resetScroll: true);
			}
		}

		partial void OnSortDescendingChanged(bool value)
		{
			if (queriesSuspended == 0)
			{
				StartQuery(debounce: false, resetScroll: true);
			}
		}

		/// <summary>Builds the catalog from the Core's books on a worker thread and shows it. A newer load supersedes an older one.</summary>
		public Task LoadAsync(Func<CancellationToken, BookCatalog> build, bool resetScroll = false)
		{
			if (build == null)
			{
				throw new ArgumentNullException(nameof(build));
			}
			int mine = Interlocked.Increment(ref loadVersion);
			IsLoading = true;
			if (catalog.Count == 0)
			{
				SummaryText = "Loading...";
			}
			return LoadCore(build, mine, resetScroll);
		}

		private async Task LoadCore(Func<CancellationToken, BookCatalog> build, int mine, bool resetScroll)
		{
			try
			{
				var sw = Stopwatch.StartNew();
				BookCatalog built = await Task.Run(() => build(CancellationToken.None)).ConfigureAwait(true);
				if (mine != Volatile.Read(ref loadVersion))
				{
					return;
				}
				LastCatalogMilliseconds = sw.Elapsed.TotalMilliseconds;
				SetCatalog(built);
				await lastQuery.ConfigureAwait(true);
			}
			catch (Exception ex)
			{
				AppLog.Error("The book list could not be built.", ex);
			}
			finally
			{
				if (mine == Volatile.Read(ref loadVersion))
				{
					IsLoading = false;
				}
			}
		}

		/// <summary>Shows a catalog that is ready (the query runs on a worker thread). The selection is kept if the book is still listed.</summary>
		public void SetCatalog(BookCatalog value, bool resetScroll = false, bool keepView = false)
		{
			catalog = value ?? BookCatalog.Empty;
			OnPropertyChanged(nameof(TotalCount));
			// another list forgets the selection (the original makes new items); a refresh of the same list keeps the books that are still there
			StartQuery(debounce: false, resetScroll: resetScroll, keepView: keepView, newCatalog: true, forgetSelection: !keepView);
		}

		/// <summary>
		/// Builds the catalog of the shown list again on a worker thread (a scan added books, a book was read, an edit) and replaces the list only if it differs:
		/// the same books with the same shown values leave <see cref="Items"/> alone. A replacement keeps the selected book (found by its Core book) and the
		/// scroll position. The result is dropped if the user showed another list meanwhile. The "last opened" marker is updated either way.
		/// </summary>
		public async Task RefreshAsync(Func<CancellationToken, BookCatalog> build)
		{
			if (build == null)
			{
				throw new ArgumentNullException(nameof(build));
			}
			int mine = Volatile.Read(ref loadVersion);
			BookCatalog current = catalog;
			BookItem[] shown = Items;
			try
			{
				(BookCatalog built, bool same, ComicBook last) = await Task.Run(() =>
				{
					BookCatalog fresh = build(CancellationToken.None);
					return (fresh, current.HasSameContentAs(fresh), BookCatalog.LastOpened(shown));
				}).ConfigureAwait(true);
				if (mine != Volatile.Read(ref loadVersion) || !ReferenceEquals(current, catalog))
				{
					return;
				}
				if (same)
				{
					if (ReferenceEquals(shown, Items))
					{
						SetLastOpened(last);
					}
					return;
				}
				SetCatalog(built, resetScroll: false, keepView: true);
				await lastQuery.ConfigureAwait(true);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				AppLog.Error("The book list could not be refreshed.", ex);
			}
		}

		/// <summary>Empties the list, e.g. while the library is closed.</summary>
		public void Clear()
		{
			Interlocked.Increment(ref loadVersion);
			SetCatalog(BookCatalog.Empty);
		}

		private void StartQuery(bool debounce, bool resetScroll, bool keepView = false, bool newCatalog = false, bool forgetSelection = false)
		{
			int mine = Interlocked.Increment(ref version);
			queryCts.Cancel();
			queryCts = new CancellationTokenSource();
			CancellationToken token = queryCts.Token;
			lastQuery = RunQuery(mine, token, debounce, resetScroll, keepView, newCatalog, forgetSelection, catalog, Sort, SortDescending, SearchText);
		}

		private async Task RunQuery(int mine, CancellationToken token, bool debounce, bool resetScroll, bool keepView, bool newCatalog, bool forgetSelection, BookCatalog cat, BookSort sort, bool descending, string text)
		{
			try
			{
				if (debounce && SearchDelay > TimeSpan.Zero)
				{
					await Task.Delay(SearchDelay, token).ConfigureAwait(true);
				}
				var sw = Stopwatch.StartNew();
				(BookItem[] items, long size, ComicBook last) = await Task.Run(() =>
				{
					BookItem[] result = cat.Query(sort, text, token);
					if (descending)
					{
						Array.Reverse(result);
					}
					long total = 0;
					foreach (BookItem item in result)
					{
						total += item.Book?.FileSize ?? 0;
					}
					return (result, total, BookCatalog.LastOpened(result));
				}, token).ConfigureAwait(true);
				if (mine != Volatile.Read(ref version))
				{
					return;
				}
				LastQueryMilliseconds = sw.Elapsed.TotalMilliseconds;
				scrollReset |= resetScroll;
				lastOpened = last;
				KeepsView = keepView;
				selectionMuted = true;
				try
				{
					// the selection follows its books into the new list (a search keeps the ones it hides, a refresh or another list does not)
					if (forgetSelection)
					{
						Selection.Clear();
					}
					Selection.SetItems(items, keepHidden: !newCatalog);
					Items = items;
					shownCount = items.Length;
					shownSize = size;
					selectionMuted = false;
					OnSelectionChanged(); // while KeepsView still says whether the view may scroll
				}
				finally
				{
					KeepsView = false;
					selectionMuted = false;
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				AppLog.Error("The book list query failed.", ex);
			}
		}


		/// <summary>Opens the selected book (double click, Enter). Raises <see cref="OpenBookRequested"/>.</summary>
		[RelayCommand]
		public void OpenSelected() => OpenSelected(OpenTabMode.Default);

		/// <summary>How the book of the running <see cref="OpenBookRequested"/> is to be opened: in the setting's tab mode, the other one, or in a new tab.</summary>
		public OpenTabMode PendingOpenMode { get; private set; }

		/// <summary>
		/// Opens the focused book in a tab. <see cref="OpenTabMode.Invert"/> (a held Cmd or Shift, <c>OpenComic</c> of the original) does the opposite of the setting Open in
		/// new Tab, <see cref="OpenTabMode.NewTab"/> (Open in new Tab, Cmd+Shift+O) always opens a tab of its own.
		/// </summary>
		public void OpenSelected(OpenTabMode mode)
		{
			BookItem item = SelectedItem;
			if (item != null)
			{
				PendingOpenMode = mode;
				try
				{
					OpenBookRequested?.Invoke(this, item);
				}
				finally
				{
					PendingOpenMode = OpenTabMode.Default;
				}
			}
		}

		/// <summary>Open in new Tab (<c>miReadTab</c>, Cmd+Shift+O): the focused book in a tab of its own.</summary>
		[RelayCommand]
		public void OpenSelectedInNewTab() => OpenSelected(OpenTabMode.NewTab);

		/// <summary>Selects <paramref name="book"/> if the shown list has it and returns true (<c>SelectComic</c>); the grid scrolls to it.</summary>
		public bool SelectBook(ComicBook book)
		{
			BookItem[] items = Items;
			for (int i = 0; i < items.Length; i++)
			{
				if (ReferenceEquals(items[i].Book, book))
				{
					SelectedIndex = i;
					return true;
				}
			}
			return false;
		}

		/// <summary>Select All (<c>miSelectAll</c>, Cmd+A): every book of the shown list.</summary>
		[RelayCommand]
		public void SelectAll() => Selection.SelectAll();

		/// <summary>Invert Selection (<c>miInvertSelection</c>): the selected books are let go, the others are selected.</summary>
		[RelayCommand]
		public void InvertSelection() => Selection.Invert();

		/// <summary>Raised on the UI thread with the selected books when the user presses Delete (or Cmd+Backspace) or chooses Remove...; the main view model answers it with the Remove question.</summary>
		public event EventHandler<IReadOnlyList<ComicBook>> RemoveBooksRequested;

		/// <summary>
		/// False while <see cref="RemoveBooksRequested"/> runs for a remove without the question (<c>removeBooks.RemoveBooks(books, Control.ModifierKeys != Keys.Control)</c>:
		/// the original's Ctrl+Delete, here Cmd+Delete); true otherwise.
		/// </summary>
		public bool PendingRemoveAsk { get; private set; } = true;

		/// <summary>Remove (<c>miRemove</c>, Delete): asks to remove the selected books. Does nothing without a selection (<c>CanRemoveBooks</c>).</summary>
		[RelayCommand]
		public void RemoveSelected() => RemoveSelected(ask: true);

		/// <summary>Remove with or without the question (<paramref name="ask"/> false: Cmd held).</summary>
		public void RemoveSelected(bool ask)
		{
			IReadOnlyList<ComicBook> books = SelectedBooks;
			if (books.Count > 0)
			{
				PendingRemoveAsk = ask;
				try
				{
					RemoveBooksRequested?.Invoke(this, books);
				}
				finally
				{
					PendingRemoveAsk = true;
				}
			}
		}

		// ---- the context menu's commands (P5.5)

		/// <summary>Raised on the UI thread when a command of the context menu wants the selected books (Info, Reveal, Mark as, My Rating, Quick Rating); the main view model carries it out.</summary>
		public event EventHandler<BookCommandEventArgs> BookCommandRequested;

		/// <summary>
		/// Whether a command of the context menu is enabled for the selection, by the rules of <c>ComicBrowserControl</c>'s command table: Open, Open in new Tab and Reveal
		/// need books that all have a file (<c>AllSelectedLinked</c>), the others a selection (<c>SelectedCount &gt; 0</c>; <c>ComicEditMode.CanEditProperties</c> is always
		/// true for the local library).
		/// </summary>
		public bool CanRun(BookCommand command)
		{
			IReadOnlyList<ComicBook> books = SelectedBooks;
			if (books.Count == 0)
			{
				return false;
			}
			if (command == BookCommand.Reveal)
			{
				bool all = true;
				foreach (ComicBook book in books)
				{
					all &= book.IsLinked;
				}
				return all;
			}
			return true;
		}

		/// <summary>True if every selected book has a file (<c>AllSelectedLinked</c>) and there is a selection: Open, Open in new Tab.</summary>
		public bool CanOpen
		{
			get
			{
				IReadOnlyList<ComicBook> books = SelectedBooks;
				if (books.Count == 0 || SelectedItem?.Book == null)
				{
					return false;
				}
				foreach (ComicBook book in books)
				{
					if (!book.IsLinked)
					{
						return false;
					}
				}
				return true;
			}
		}

		/// <summary>Runs a command of the context menu on the selected books (does nothing when it is not enabled).</summary>
		public void Run(BookCommand command, float value = 0)
		{
			if (CanRun(command))
			{
				BookCommandRequested?.Invoke(this, new BookCommandEventArgs(command, SelectedBooks, value));
			}
		}

		/// <summary>
		/// The rating the selected books share, rounded as the menu does (<c>Math.Round(GetRating())</c>), or -1 when there is no selection or the ratings differ
		/// (<c>RatingEditor.GetRating</c>).
		/// </summary>
		public float SelectedRating
		{
			get
			{
				float rating = -1f;
				foreach (ComicBook book in SelectedBooks)
				{
					if (rating == -1f)
					{
						rating = book.Rating;
					}
					else if (rating != book.Rating)
					{
						return -1f;
					}
				}
				return rating;
			}
		}
	}
}
