using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Host;
using ComicRack.Mac.Host.Library;
using ComicRack.Mac.Services;

namespace ComicRack.Mac.ViewModels
{
	/// <summary>
	/// State and commands of the main window. Every command is wired to a menu item (<see cref="MainMenu"/>); those that
	/// later tasks implement report that they are not available yet.
	///
	/// File > Add Folder to Library and File > Import Windows Library (M2.1) live here: the dialogs come from <see cref="ILibraryDialogs"/>,
	/// the work from <see cref="FolderScanner"/> and <see cref="HostService"/>, and progress comes back through
	/// <see cref="UiProgress{T}"/>. One long operation runs at a time (<see cref="IsBusy"/>); it can be cancelled.
	/// </summary>
	public sealed partial class MainViewModel : ObservableObject
	{
		private readonly IShell shell;
		private readonly HostService hostService;
		private readonly ILibraryDialogs dialogs;
		private readonly IReaderLauncher readers;
		private readonly AppPreferences preferences;
		private CancellationTokenSource operation;

		/// <param name="preferences">The saved preferences (P5.1): the side panel, the size of the thumbnails, the sort, the thumbnail options and the last folder start from them
		/// and are written back to them. Null (tests without a settings file) keeps nothing.</param>
		public MainViewModel(IShell shell, HostService hostService, ILibraryDialogs dialogs, IReaderLauncher readers = null, AppPreferences preferences = null)
		{
			this.shell = shell;
			this.hostService = hostService;
			this.dialogs = dialogs;
			this.readers = readers;
			this.preferences = preferences;
			StatusText = "Starting the library engine...";
			BookCountText = "Starting the library engine...";
			Browser.OpenBookRequested += OnOpenBookRequested;
			Browser.PropertyChanged += (sender, e) =>
			{
				if (e.PropertyName == nameof(BrowserViewModel.SummaryText))
				{
					OnPropertyChanged(nameof(StatusLine));
				}
			};
			Sidebar.SelectionChanged += (sender, e) => _ = ShowSelectedListAsync();
			hostService.Closing += (sender, e) =>
			{
				Browser.Thumbnails = null; // the cover service is about to be disposed (import)
				StopWatching();
			};
			if (readers != null)
			{
				Browser.IsBookOpen = readers.IsOpen;
				openReaders = readers.OpenCount;
				readers.OpenBooksChanged += OnOpenBooksChanged;
			}
			if (preferences != null)
			{
				RestoreAndFollow(preferences);
			}
		}

		// ---- live refresh (P5.2)

		private ISaveScheduler liveScheduler;
		private LibraryWatcher watcher;
		private Task liveTask = Task.CompletedTask;
		private bool liveRunning, livePending;
		private int openReaders;

		/// <summary>
		/// Turns the live refresh on: from now on the changes of the open library (books added by a scan, a page read, a rating, an edit) update the covers that
		/// show them at once, and the counters of the tree and the shown list a moment later, off the UI thread, keeping the selection and the scroll position
		/// (<see cref="LibraryWatcher"/> collects the changes and delivers them on <paramref name="scheduler"/>; the default is a timer). The window does it
		/// at start; a test passes a scheduler it releases by hand.
		/// </summary>
		public void EnableLiveRefresh(ISaveScheduler scheduler = null)
		{
			liveScheduler = scheduler ?? new TimerSaveScheduler();
			StartWatching();
		}

		private void StartWatching()
		{
			watcher?.Dispose();
			watcher = null;
			MacHost host = hostService.Host;
			if (liveScheduler == null || host == null)
			{
				return;
			}
			watcher = new LibraryWatcher(host.DatabaseManager.Database, liveScheduler, change => UiDispatch.Post(() => OnLibraryChanged(change)));
		}

		private void StopWatching()
		{
			watcher?.Dispose();
			watcher = null;
			Sidebar.CancelCounts();
		}

		/// <summary>The watcher of the open library, or null (for tests).</summary>
		public LibraryWatcher Watcher => watcher;

		// A reader was opened or closed: the "open" state icons follow, and a closed reader has just recorded its page, so the lists count at once.
		private void OnOpenBooksChanged(object sender, EventArgs e)
		{
			Browser.NotifyMarkersChanged();
			int now = readers.OpenCount;
			bool closed = now < openReaders;
			openReaders = now;
			if (closed)
			{
				watcher?.Flush();
			}
		}

		private void OnLibraryChanged(LibraryChange change)
		{
			if (hostService.Host == null)
			{
				return;
			}
			Browser.NotifyBooksChanged(change.Books);
			if (change.ListChanged)
			{
				SetBookCount(hostService.Host.DatabaseManager.Database.Books.Count);
			}
			RequestLiveRefresh();
		}

		/// <summary>Counts the lists again and refreshes the shown list now (one pass at a time; a request made during a pass runs another pass after it).</summary>
		public void RequestLiveRefresh()
		{
			if (liveRunning)
			{
				livePending = true;
				return;
			}
			liveRunning = true;
			liveTask = RunLiveRefreshAsync();
		}

		/// <summary>Completes when no refresh is running or waiting.</summary>
		public async Task WhenLiveRefreshedAsync()
		{
			Task seen;
			do
			{
				seen = liveTask;
				await seen.ConfigureAwait(true);
			}
			while (!ReferenceEquals(seen, liveTask) || liveRunning);
		}

		private async Task RunLiveRefreshAsync()
		{
			try
			{
				do
				{
					livePending = false;
					if (hostService.Host == null)
					{
						break;
					}
					var watch = System.Diagnostics.Stopwatch.StartNew();
					SidebarNode node = Sidebar.SelectedNode;
					string notice = null;
					Task counts = Sidebar.RefreshCountsAsync();
					Task grid = Browser.RefreshAsync(Sidebar.LoaderFor(node, text => notice = text));
					await Task.WhenAll(counts, grid).ConfigureAwait(true);
					if (Sidebar.SelectedNode == node && hostService.Host != null)
					{
						Sidebar.Notice = notice;
					}
					// the numbers of the rows are posted to the UI thread by the count pass: let them arrive before they are logged
					await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
					// the counters as the tree now shows them: total/unread/new of every list that has books
					string counters = string.Join(", ", Sidebar.AllNodes().Where(n => n.Count > 0).Select(n => $"{n.Name} {n.Count}/{n.UnreadCount}/{n.NewCount}"));
					AppLog.Info($"Live refresh: counts and list updated in {watch.ElapsedMilliseconds} ms. Counters {counters}.");
				}
				while (livePending);
			}
			catch (Exception ex)
			{
				AppLog.Error("The live refresh failed.", ex);
			}
			finally
			{
				liveRunning = false;
			}
		}

		// The state of the library window that is kept between runs: it starts from the preferences and every change is written back.
		private void RestoreAndFollow(AppPreferences saved)
		{
			Sidebar.BindGauges(saved);
			Sidebar.IsVisible = saved.SidebarVisible;
			Sidebar.Width = SidebarViewModel.ClampWidth(saved.SidebarWidth);
			Browser.ThumbnailSize = Math.Clamp(saved.ThumbnailSize, BrowserViewModel.MinThumbnailSize, BrowserViewModel.MaxThumbnailSize);
			Browser.SortIndex = Enum.TryParse(saved.SortBy, out BookSort sort) && Enum.IsDefined(typeof(BookSort), sort) ? (int)sort : 0;
			Browser.ThumbnailOptions.Bind(saved);
			Sidebar.PropertyChanged += (sender, e) =>
			{
				if (e.PropertyName == nameof(SidebarViewModel.IsVisible))
				{
					saved.SidebarVisible = Sidebar.IsVisible;
				}
				else if (e.PropertyName == nameof(SidebarViewModel.Width))
				{
					saved.SidebarWidth = Sidebar.Width;
				}
			};
			Browser.PropertyChanged += (sender, e) =>
			{
				if (e.PropertyName == nameof(BrowserViewModel.ThumbnailSize))
				{
					saved.ThumbnailSize = Browser.ThumbnailSize;
				}
				else if (e.PropertyName == nameof(BrowserViewModel.SortIndex))
				{
					saved.SortBy = Browser.Sort.ToString();
				}
			};
		}

		/// <summary>The folder the folder picker starts in: the last one that was added to the library, if it still exists.</summary>
		public string LastFolder
		{
			get
			{
				string folder = preferences?.LastFolder;
				return !string.IsNullOrEmpty(folder) && Directory.Exists(folder) ? folder : null;
			}
		}

		/// <summary>The reader windows of this app (null when the view model was made without a launcher).</summary>
		public IReaderLauncher Readers => readers;

		/// <summary>The book grid: list, search, sort, thumbnail size, selection.</summary>
		public BrowserViewModel Browser { get; } = new BrowserViewModel();

		/// <summary>The tree of lists (M2.2c). The selected row decides which books the grid shows.</summary>
		public SidebarViewModel Sidebar { get; } = new SidebarViewModel();

		// Double click or Enter on a book: opens it in a reader window (M2.3a).
		private void OnOpenBookRequested(object sender, BookItem item)
		{
			AppLog.Info("Open book requested: " + item.Book?.FilePath);
			if (item.Book == null || readers == null)
			{
				StatusText = $"\"{item.Title}\" cannot be opened here.";
				return;
			}
			if (!item.Book.IsLinked || !File.Exists(item.Book.FilePath))
			{
				StatusText = $"\"{item.Title}\" cannot be opened: the file is missing ({item.Book.FilePath}).";
				return;
			}
			readers.Open(item.Book);
			StatusText = $"Reading \"{item.Title}\".";
		}

		/// <summary>Closes the reader windows (each records its page). Called before the library is replaced and when the app quits.</summary>
		public void CloseReaders()
		{
			readers?.CloseAll();
		}

		/// <summary>
		/// Shows the books of the selected list in the grid (a snapshot built on a worker thread; the library when nothing else is
		/// selected); an empty grid and tree if there is no library. The tree is rebuilt first, so a scan or an import shows up in it.
		/// </summary>
		private void ReloadBrowser()
		{
			var host = hostService.Host;
			if (host == null)
			{
				Sidebar.Clear();
				Browser.Clear();
				return;
			}
			Volatile.Write(ref browserReload, ReloadBrowserAsync(host));
		}

		private Task browserReload = Task.CompletedTask;

		/// <summary>
		/// Completes when the newest reload of the sidebar and the grid is done: the tree is built, the counts are computed and the
		/// selected list is loaded. Those run on worker threads against the open library, so a caller that changes the library itself
		/// (tests, scripted use) waits for this first.
		/// </summary>
		public async Task WhenLibraryShownAsync()
		{
			Task seen;
			do
			{
				seen = Volatile.Read(ref browserReload);
				await seen.ConfigureAwait(false);
			}
			while (!ReferenceEquals(seen, Volatile.Read(ref browserReload)));
			await Sidebar.CountsReady.ConfigureAwait(false);
		}

		private async Task ReloadBrowserAsync(ComicRack.Mac.Host.MacHost host)
		{
			try
			{
				await Sidebar.ReloadAsync(host);
				await ShowSelectedListAsync(resetScroll: false);
			}
			catch (Exception ex)
			{
				AppLog.Error("The sidebar could not be loaded.", ex);
			}
		}

		// Loads the books of the selected row into the grid, on a worker thread.
		private async Task ShowSelectedListAsync(bool resetScroll = true)
		{
			SidebarNode node = Sidebar.SelectedNode;
			Browser.ListName = node?.Name;
			string notice = null;
			Sidebar.Notice = null;
			Task load = Browser.LoadAsync(Sidebar.LoaderFor(node, text => notice = text), resetScroll);
			await load;
			if (Sidebar.SelectedNode == node)
			{
				Sidebar.Notice = notice;
			}
		}

		public string AppName => "ComicRack CE (MVP)";

		/// <summary>The title of the window: the form's text of the original (<c>MainForm.xml</c> key MainForm), "ComicRack".</summary>
		public string WindowTitle => UiText.Get("MainForm", "MainForm", "ComicRack");

		/// <summary>
		/// A message of the app (starting, the engine did not start, a failed operation). The status bar shows it until a library is open; after that
		/// it shows <see cref="BrowserViewModel.SummaryText"/> like the original (<c>MainForm.OnUpdateGui</c> writes <c>SelectionInfo</c> into <c>tsText</c>).
		/// </summary>
		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(StatusLine))]
		public partial string StatusText { get; set; }

		/// <summary>The text of the status bar's first segment (<c>tsText</c>).</summary>
		public string StatusLine => BookCount < 0 || string.IsNullOrEmpty(Browser.SummaryText) ? StatusText : Browser.SummaryText;

		// The segments of the open book (tsBook, tsCurrentPage, tsPageCount). The library window shows no book, so they read as the original does
		// without one: "None", "NA", "NA" (MainForm.OnUpdateGui, TotalPageInformation). The reader is a separate window until P5.32a.
		public string BookSegmentText => UiText.Default("None", "None");

		public string PageSegmentText => UiText.Default("NotAvailable", "NA");

		public string PageCountSegmentText => UiText.Default("NotAvailable", "NA");

		public string BookSegmentTip => UiText.Tooltip("MainForm", "tsBook") ?? "Name of the opened Book";

		public string PageSegmentTip => UiText.Tooltip("MainForm", "tsCurrentPage") ?? "Current Page of the open Book";

		public string PageCountSegmentTip => UiText.Tooltip("MainForm", "tsPageCount") ?? "Page count of the open Book";

		[ObservableProperty]
		public partial string HostText { get; set; }

		/// <summary>Number of books in the library, as shown in the middle of the window.</summary>
		[ObservableProperty]
		public partial string BookCountText { get; set; }

		/// <summary>Books in the library, or -1 while there is no open library engine.</summary>
		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(ShowEmptyMessage))]
		[NotifyPropertyChangedFor(nameof(StatusLine))]
		public partial int BookCount { get; set; } = -1;

		/// <summary>True while there is no book to show: the window then shows the title and the book count message instead of the grid.</summary>
		public bool ShowEmptyMessage => BookCount <= 0;

		/// <summary>True from the start of Add Folder to Library / Import until it ends, including while its dialog is open. Disables both commands.</summary>
		[ObservableProperty]
		[NotifyCanExecuteChangedFor(nameof(OpenFolderCommand))]
		[NotifyCanExecuteChangedFor(nameof(ImportWindowsLibraryCommand))]
		public partial bool IsBusy { get; set; }

		/// <summary>True while a scan or an import shows its progress panel.</summary>
		[ObservableProperty]
		public partial bool IsProgressVisible { get; set; }

		[ObservableProperty]
		public partial string ProgressTitle { get; set; }

		/// <summary>"1,234 files, 1,200 books added".</summary>
		[ObservableProperty]
		public partial string ProgressCountText { get; set; }

		/// <summary>The file being scanned.</summary>
		[ObservableProperty]
		public partial string ProgressFileText { get; set; }

		[ObservableProperty]
		[NotifyCanExecuteChangedFor(nameof(CancelOperationCommand))]
		public partial bool CanCancel { get; set; }

		/// <summary>Called on the UI thread when the host opened or failed.</summary>
		public void OnHostOpened()
		{
			var host = hostService.Host;
			if (host == null)
			{
				HostText = "Engine failed to start";
				StatusText = "The library engine did not start: " + (hostService.Failure?.Message ?? "unknown error") + " (see the log)";
				BookCount = -1;
				BookCountText = "The library engine did not start.";
				Browser.Thumbnails = null;
				ReloadBrowser();
				return;
			}
			HostText = host.IsNewDatabase ? "New empty library" : "Library opened";
			SetBookCount(host.DatabaseManager.Database.Books.Count);
			Browser.Thumbnails = hostService.Thumbnails;
			ReloadBrowser();
			StartWatching();
			StatusText = $"Library engine ready. {BookCount} books. Data: {host.Paths.DataFolder}";
		}

		/// <summary>The title of the folder question in the original (<c>Messages.xml</c> key AddFolderLibraryTitle).</summary>
		public static string AddFolderTitle => UiText.Message("AddFolderLibraryTitle", "Add Folder to Library");

		/// <summary>"File › Add Folder to Library..." with the original's menu texts.</summary>
		public static string AddFolderMenuPath => UiText.Get("MainForm", "fileMenu", "&File") + " › " + UiText.Get("MainForm", "miAddFolderToLibrary", "&Add Folder to Library...");

		private void SetBookCount(int count)
		{
			BookCount = count;
			BookCountText = count == 0
				? "The library is empty. Use " + AddFolderMenuPath + " (⇧⌘A) to add comics."
				: count.ToString("N0", CultureInfo.CurrentCulture) + (count == 1 ? " book" : " books") + " in the library";
		}

		// File

		private bool CanStartOperation() => !IsBusy;

		[RelayCommand(CanExecute = nameof(CanStartOperation))]
		private async Task OpenFolderAsync()
		{
			await RunExclusive(async () =>
			{
				string folder = await dialogs.PickFolderAsync("Choose a folder of comics to add to the library", LastFolder);
				if (folder == null)
				{
					StatusText = AddFolderTitle + " cancelled.";
					return;
				}
				await ScanFolderCore(folder);
			});
		}

		[RelayCommand(CanExecute = nameof(CanStartOperation))]
		private async Task ImportWindowsLibraryAsync()
		{
			await RunExclusive(async () =>
			{
				ImportRequest request = await dialogs.AskImportAsync();
				if (request == null)
				{
					StatusText = "Import cancelled.";
					return;
				}
				await ImportCore(request);
			});
		}

		/// <summary>Scans a folder and adds its books, as File > Add Folder to Library does after the picker. For tests and scripted use.</summary>
		public Task<ScanResult> ScanFolderAsync(string folder)
		{
			return RunExclusive(() => ScanFolderCore(folder));
		}

		/// <summary>Imports a Windows database, as File > Import does after the dialog.</summary>
		public Task<ImportResult> ImportLibraryAsync(ImportRequest request)
		{
			return RunExclusive(() => ImportCore(request));
		}

		[RelayCommand(CanExecute = nameof(CanCancel))]
		private void CancelOperation()
		{
			CancellationTokenSource cts = operation;
			if (cts == null)
			{
				return;
			}
			CanCancel = false;
			ProgressTitle = "Cancelling...";
			AppLog.Info("Cancel requested by the user.");
			cts.Cancel();
		}

		/// <summary>Cancels a running scan or import (at exit). Does not wait.</summary>
		public void CancelRunningOperation()
		{
			try
			{
				operation?.Cancel();
			}
			catch (ObjectDisposedException)
			{
			}
		}

		private async Task RunExclusive(Func<Task> work)
		{
			await RunExclusive<object>(async () =>
			{
				await work();
				return null;
			});
		}

		private async Task<T> RunExclusive<T>(Func<Task<T>> work)
		{
			if (IsBusy)
			{
				throw new InvalidOperationException("Another library operation is running.");
			}
			IsBusy = true;
			try
			{
				return await work();
			}
			finally
			{
				operation = null;
				IsProgressVisible = false;
				CanCancel = false;
				IsBusy = false;
			}
		}

		private CancellationToken BeginProgress(string title)
		{
			var cts = new CancellationTokenSource();
			operation = cts;
			ProgressTitle = title;
			ProgressCountText = string.Empty;
			ProgressFileText = string.Empty;
			IsProgressVisible = true;
			CanCancel = true;
			return cts.Token;
		}

		private async Task<ScanResult> ScanFolderCore(string folder)
		{
			var host = hostService.Host;
			if (host == null)
			{
				await ReportProblem(AddFolderTitle, "The library engine is not running, so a folder cannot be added.");
				return null;
			}
			if (preferences != null)
			{
				preferences.LastFolder = folder;
			}
			CancellationToken token = BeginProgress("Scanning " + Path.GetFileName(folder.TrimEnd('/')) + "...");
			AppLog.Info("Scan of " + folder + " started.");
			ScanResult result;
			try
			{
				result = await new FolderScanner(host).ScanAsync(folder, new UiProgress<ScanProgress>(OnScanProgress), token);
			}
			catch (Exception ex)
			{
				AppLog.Error("The scan failed.", ex);
				await ReportProblem(AddFolderTitle, "The folder could not be scanned: " + ex.Message);
				return null;
			}

			// The books that were added stay, also after a cancel. Save them now, so that a crash does not lose a long scan.
			ProgressTitle = "Saving the library...";
			CanCancel = false;
			SaveVerification saved = await SaveAfterOperation();
			SetBookCount(result.BooksTotal);
			ReloadBrowser();
			string what = result.Outcome == ScanOutcome.Cancelled ? "Scan cancelled" : "Scan finished";
			StatusText = $"{what}: {result.FilesSeen:N0} files looked at, {result.BooksAdded:N0} books added, {result.BooksTotal:N0} in the library ({result.Elapsed.TotalSeconds:0.0} s).";
			AppLog.Info(StatusText);
			if (saved != null && !saved.Ok)
			{
				await ReportSaveProblem(saved);
			}
			return result;
		}

		private void OnScanProgress(ScanProgress p)
		{
			if (!IsProgressVisible && !p.IsFinal)
			{
				return;
			}
			ProgressCountText = $"{p.FilesSeen:N0} files, {p.BooksAdded:N0} books added";
			ProgressFileText = p.CurrentFile ?? string.Empty;
			if (p.BooksTotal >= 0)
			{
				SetBookCount(p.BooksTotal);
			}
		}

		private async Task<ImportResult> ImportCore(ImportRequest request)
		{
			if (hostService.Paths == null)
			{
				await ReportProblem("Import", "The library engine was not started.");
				return null;
			}
			// A reader uses the open library's page cache and records its page in the library: close it first, while the library is still open.
			CloseReaders();
			CancellationToken token = BeginProgress("Importing " + Path.GetFileName(request.DatabasePath) + "...");
			AppLog.Info("Import of " + request.DatabasePath + " started.");
			ImportResult result;
			try
			{
				result = await hostService.ImportAsync(request, new UiProgress<int>(books =>
				{
					ProgressCountText = books.ToString("N0", CultureInfo.CurrentCulture) + " books copied";
				}), token);
			}
			catch (OperationCanceledException)
			{
				StatusText = "Import cancelled. The library was not changed.";
				AppLog.Info(StatusText);
				return null;
			}
			catch (Exception ex)
			{
				AppLog.Error("The import failed.", ex);
				await ReportProblem("Import", "The library could not be imported: " + ex.Message);
				RefreshFromHost();
				return null;
			}
			RefreshFromHost();
			StatusText = $"Imported {result.Books:N0} books ({result.BooksRemapped:N0} remapped, {result.BooksFound:N0} of them found on this Mac)."
				+ (result.BackupFile != null ? " Previous library kept as " + Path.GetFileName(result.BackupFile) + "." : string.Empty);
			AppLog.Info(StatusText);
			if (result.Books > 0 && result.BooksFound == 0)
			{
				await ReportProblem("Import", $"{result.Books:N0} books were imported, but none of their files exists on this Mac. Check the path mappings: the first part of the Windows path must be replaced by the folder where the comics are now. Use File › Import Windows Library again with the right mapping.");
			}
			return result;
		}

		private void RefreshFromHost()
		{
			if (hostService.Host != null)
			{
				SetBookCount(hostService.Host.DatabaseManager.Database.Books.Count);
			}
		}

		private async Task<SaveVerification> SaveAfterOperation()
		{
			if (hostService.Host == null)
			{
				return null; // the app is quitting: the host has been closed and saved already
			}
			try
			{
				return await hostService.SaveAsync();
			}
			catch (Exception ex)
			{
				AppLog.Error("Saving the library failed.", ex);
				return new SaveVerification { Ok = false, Problem = ex.Message };
			}
		}

		private Task ReportSaveProblem(SaveVerification v)
		{
			return ReportProblem("The library was not saved", "ComicRack could not write the library file: " + (v.Problem ?? "unknown error") + "\n\nThe books added in this session may be lost if the app quits now.");
		}

		private async Task ReportProblem(string title, string message)
		{
			StatusText = title + ": " + message;
			try
			{
				await dialogs.ShowMessageAsync(title, message);
			}
			catch (Exception ex)
			{
				AppLog.Error("Showing a message failed.", ex);
			}
		}

		// Browse and Display

		[RelayCommand]
		private void ToggleSidebar() => Sidebar.IsVisible = !Sidebar.IsVisible;

		[RelayCommand]
		private void ToggleFullScreen() => shell.ToggleFullScreen();

		/// <summary>True while the window is full screen (Display > Full Screen is checked); the window keeps it up to date.</summary>
		[ObservableProperty]
		public partial bool IsFullScreen { get; set; }

		// Window

		[RelayCommand]
		private void Minimize() => shell.Minimize();

		[RelayCommand]
		private void Zoom() => shell.Zoom();

		// Help

		[RelayCommand]
		private void ShowHelp() => NotYet(UiText.Get("MainForm", "miWebHelp", "ComicRack Documentation..."), "P5.31");

		// App

		[RelayCommand]
		private void About() => shell.ShowAbout();

		private void NotYet(string what, string task)
		{
			AppLog.Info(what + " chosen (not implemented before " + task + ").");
			StatusText = $"{what} is not available yet ({task}).";
		}
	}
}
