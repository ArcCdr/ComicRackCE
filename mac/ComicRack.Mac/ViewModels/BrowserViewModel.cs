using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Services;
using ComicRack.Mac.Thumbnails;

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
		public partial int SortIndex { get; set; }

		/// <summary>Height of a cover in device independent pixels.</summary>
		[ObservableProperty]
		public partial double ThumbnailSize { get; set; } = DefaultThumbnailSize;

		/// <summary>Index into <see cref="Items"/>, or -1.</summary>
		[ObservableProperty]
		public partial int SelectedIndex { get; set; } = -1;

		/// <summary>"14,706 books" or "312 of 14,706 books".</summary>
		[ObservableProperty]
		public partial string SummaryText { get; set; }

		[ObservableProperty]
		public partial bool IsLoading { get; set; }

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
		public void SetCatalog(BookCatalog value, bool resetScroll = false)
		{
			catalog = value ?? BookCatalog.Empty;
			OnPropertyChanged(nameof(TotalCount));
			StartQuery(debounce: false, resetScroll: resetScroll);
		}

		/// <summary>Empties the list, e.g. while the library is closed.</summary>
		public void Clear()
		{
			Interlocked.Increment(ref loadVersion);
			SetCatalog(BookCatalog.Empty);
		}

		private void StartQuery(bool debounce, bool resetScroll)
		{
			int mine = Interlocked.Increment(ref version);
			queryCts.Cancel();
			queryCts = new CancellationTokenSource();
			CancellationToken token = queryCts.Token;
			lastQuery = RunQuery(mine, token, debounce, resetScroll, catalog, Sort, SearchText, SelectedItem);
		}

		private async Task RunQuery(int mine, CancellationToken token, bool debounce, bool resetScroll, BookCatalog cat, BookSort sort, string text, BookItem selected)
		{
			try
			{
				if (debounce && SearchDelay > TimeSpan.Zero)
				{
					await Task.Delay(SearchDelay, token).ConfigureAwait(true);
				}
				var sw = Stopwatch.StartNew();
				(BookItem[] items, int selectedIndex) = await Task.Run(() =>
				{
					BookItem[] result = cat.Query(sort, text, token);
					return (result, selected == null ? -1 : Array.IndexOf(result, selected));
				}, token).ConfigureAwait(true);
				if (mine != Volatile.Read(ref version))
				{
					return;
				}
				LastQueryMilliseconds = sw.Elapsed.TotalMilliseconds;
				scrollReset |= resetScroll;
				SelectedIndex = -1; // the old index means another book in the new list
				Items = items;
				SelectedIndex = selectedIndex;
				SummaryText = Describe(items.Length, cat.Count);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				AppLog.Error("The book list query failed.", ex);
			}
		}

		internal static string Describe(int shown, int total)
		{
			string books = total == 1 ? " book" : " books";
			return shown == total
				? total.ToString("N0", CultureInfo.CurrentCulture) + books
				: shown.ToString("N0", CultureInfo.CurrentCulture) + " of " + total.ToString("N0", CultureInfo.CurrentCulture) + books;
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
