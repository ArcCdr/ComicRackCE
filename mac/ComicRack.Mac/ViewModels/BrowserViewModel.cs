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
				foreach (ComicBook book in books)
				{
					if (book.OpenedTime > t && Array.Exists(Items, i => ReferenceEquals(i.Book, book)))
					{
						t = book.OpenedTime;
						newest = book;
					}
				}
				if (newest != null)
				{
					lastOpened = newest;
				}
			}
			BooksChanged?.Invoke(this, books);
		}

		partial void OnListNameChanged(string value) => UpdateSummary();

		partial void OnSelectedIndexChanged(int value) => UpdateSummary();

		// Writes the status text from the books shown and the selected book (UI thread).
		private void UpdateSummary()
		{
			BookItem selected = SelectedItem;
			string path = selected?.Book != null && selected.Book.IsLinked ? selected.Book.FilePath : null;
			SummaryText = SelectionInfo.Build(ListName, shownCount, TotalCount, shownSize, selected == null ? 0 : 1, path, selected?.Book?.FileSize ?? 0);
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

		/// <summary>The text of the Arrange button: the name of the column the list is arranged by (<c>tbbSort.Text</c>).</summary>
		public string SortName => SortNames[Math.Clamp(SortIndex, 0, SortNames.Length - 1)];

		/// <summary>Height of a cover in device independent pixels.</summary>
		[ObservableProperty]
		public partial double ThumbnailSize { get; set; } = DefaultThumbnailSize;

		/// <summary>True while a refresh replaces <see cref="Items"/> and the selection: the view keeps its scroll position and does not scroll to the selected book.</summary>
		public bool KeepsView { get; private set; }

		/// <summary>Index into <see cref="Items"/>, or -1.</summary>
		[ObservableProperty]
		public partial int SelectedIndex { get; set; } = -1;

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

		public BookSort Sort => (BookSort)Math.Clamp(SortIndex, 0, SortNames.Length - 1);

		/// <summary>Books in the catalog (before the search).</summary>
		public int TotalCount => catalog.Count;

		public double LastQueryMilliseconds { get; private set; }

		public double LastCatalogMilliseconds { get; private set; }

		/// <summary>Raised on the UI thread when the user opens a book (double click or Enter).</summary>
		public event EventHandler<BookItem> OpenBookRequested;

		public BookItem SelectedItem
		{
			get
			{
				BookItem[] items = Items;
				int i = SelectedIndex;
				return i >= 0 && i < items.Length ? items[i] : null;
			}
		}

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

		partial void OnSortIndexChanged(int value) => StartQuery(debounce: false, resetScroll: true);

		private static int IndexOfBook(BookItem[] items, BookItem wanted)
		{
			if (wanted == null)
			{
				return -1;
			}
			for (int i = 0; i < items.Length; i++)
			{
				if (ReferenceEquals(items[i], wanted) || (wanted.Book != null && ReferenceEquals(items[i].Book, wanted.Book)))
				{
					return i;
				}
			}
			return -1;
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
			StartQuery(debounce: false, resetScroll: resetScroll, keepView: keepView);
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

		private void StartQuery(bool debounce, bool resetScroll, bool keepView = false)
		{
			int mine = Interlocked.Increment(ref version);
			queryCts.Cancel();
			queryCts = new CancellationTokenSource();
			CancellationToken token = queryCts.Token;
			lastQuery = RunQuery(mine, token, debounce, resetScroll, keepView, catalog, Sort, SearchText, SelectedItem);
		}

		private async Task RunQuery(int mine, CancellationToken token, bool debounce, bool resetScroll, bool keepView, BookCatalog cat, BookSort sort, string text, BookItem selected)
		{
			try
			{
				if (debounce && SearchDelay > TimeSpan.Zero)
				{
					await Task.Delay(SearchDelay, token).ConfigureAwait(true);
				}
				var sw = Stopwatch.StartNew();
				(BookItem[] items, int selectedIndex, long size, ComicBook last) = await Task.Run(() =>
				{
					BookItem[] result = cat.Query(sort, text, token);
					long total = 0;
					foreach (BookItem item in result)
					{
						total += item.Book?.FileSize ?? 0;
					}
					return (result, IndexOfBook(result, selected), total, BookCatalog.LastOpened(result));
				}, token).ConfigureAwait(true);
				if (mine != Volatile.Read(ref version))
				{
					return;
				}
				LastQueryMilliseconds = sw.Elapsed.TotalMilliseconds;
				scrollReset |= resetScroll;
				lastOpened = last;
				KeepsView = keepView;
				try
				{
					SelectedIndex = -1; // the old index means another book in the new list
					Items = items;
					shownCount = items.Length;
					shownSize = size;
					SelectedIndex = selectedIndex;
				}
				finally
				{
					KeepsView = false;
				}
				UpdateSummary();
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
		public void OpenSelected()
		{
			BookItem item = SelectedItem;
			if (item != null)
			{
				OpenBookRequested?.Invoke(this, item);
			}
		}
	}
}
