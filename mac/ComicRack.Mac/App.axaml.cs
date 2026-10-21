using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using cYo.Projects.ComicRack.Engine;
using ComicRack.Mac.Diagnostics;
using ComicRack.Mac.Host;
using ComicRack.Mac.Reader;
using ComicRack.Mac.Services;
using ComicRack.Mac.ViewModels;
using ComicRack.Mac.Views;

namespace ComicRack.Mac
{
	public sealed class App : Application
	{
		private AppOptions options = new AppOptions();
		private HostService hostService;
		private MainViewModel viewModel;
		private ReaderManager readers;
		private PerfOverlayController perf;

		public App()
		{
		}

		/// <summary>Options from the command line; set by <see cref="Program"/> before the application starts.</summary>
		internal static AppOptions StartupOptions { get; set; } = new AppOptions();

		public override void Initialize()
		{
			AvaloniaXamlLoader.Load(this);
		}

		public override void OnFrameworkInitializationCompleted()
		{
			options = StartupOptions;
			// C-APP-1: follow the macOS appearance unless --theme forces the original's light or dark theme.
			// The look of each chrome element: the table in Themes/ChromeVariants.cs, then --chrome or COMICRACK_MAC_CHROME on top.
			Themes.ChromeVariants.Reset();
			Themes.ChromeVariants.Configure(options.Chrome);
			RequestedThemeVariant = options.Theme == "light" ? ThemeVariant.Light : options.Theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Default;
			// The tile of a thumbnail that cannot be made follows the appearance (the decoder draws it on a worker thread from this snapshot).
			Controls.ThumbnailPlaceholderLook.Follow(this);
			// The saved settings are read before anything else that depends on them (P5.1): the host service owns the file, and only a desktop run has one.
			if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
			{
				hostService = new HostService(options);
			}
			// Before any window: the texts of the Core's TR (and later every label of the UI) come from Languages/ next to the executable.
			// The language is --language (or the environment) over the saved CultureName, as ExtendedSettings.Language over Settings.CultureName in the original (Program.cs:792).
			Localization.Apply(options.Language ?? hostService?.Preferences.Current.CultureName);
			// FadeInThumbnails: covers fade in only in a running desktop app; a headless test session shows the finished frame at once.
			Controls.CoverView.FadeEnabled = ApplicationLifetime is IClassicDesktopStyleApplicationLifetime && !options.Headless;
			// The fades and the slide of the reader overlays run on the same condition; a test moves them by hand (ReaderOverlayClock.Advance).
			Controls.ReaderOverlayClock.Enabled = Controls.CoverView.FadeEnabled;
			// The rubber band of the browser scrolls the view with a 20 ms timer while the pointer is beyond its edge (ScrollControl); a test calls AdvanceAutoScroll.
			Views.BookGridView.AutoScrollEnabled = Controls.CoverView.FadeEnabled;
			if (options.Headless)
			{
				// The system alert runs a modal loop of its own: not on the headless platform (the alert window answers instead).
				Platform.MacAlert.UseNative = false;
			}
			if (!options.Headless && OperatingSystem.IsMacOS())
			{
				// C-WIN-3: the app has its own tab bars, so macOS must not merge its windows into tabs.
				Platform.WindowTabbing.Disable();
				// C-SCROLL-1: overlay scrollers hide when idle, legacy ones stay visible, as System Settings says.
				var autoHide = new Avalonia.Styling.Style(x => x.OfType<ScrollViewer>());
				autoHide.Setters.Add(new Avalonia.Styling.Setter(ScrollViewer.AllowAutoHideProperty, Platform.ScrollerStyle.IsOverlay));
				Styles.Add(autoHide);
			}
			if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
			{
				// An exception on the UI thread is logged through the host log and the app keeps running.
				Dispatcher.UIThread.UnhandledException += OnUiThreadUnhandledException;

				AppPreferences preferences = hostService.Preferences.Current;
				var window = new MainWindow();
				// Where the library window was (moved back on a screen if its monitor is gone); from the first frame on, its moves and resizes are kept.
				WindowFrame placed = WindowFrameTracker.Restore(window, preferences.LibraryWindow);
				WindowFrameTracker.Attach(window, frame => preferences.LibraryWindow = frame, placed);
				AppLog.Info(placed == null ? "Library window: no saved frame." : "Library window restored to " + placed + " (saved " + preferences.LibraryWindow + ").");
				perf = new PerfOverlayController(new PerfSources(), new AvaloniaPerfTicker(), StopwatchClock.Now, AppLog.Info);
				readers = new ReaderManager(hostService, preferences);
				perf.Sources.DecodeMilliseconds = readers.LastDecodeMilliseconds;
				perf.Sources.CachedTurnMilliseconds = readers.LastCachedTurnMilliseconds;
				perf.Sources.Thumbnails = () =>
				{
					var stats = hostService.Thumbnails?.GetStats();
					return stats == null ? null : ((int, int)?)(stats.Queued, stats.InFlight);
				};
				window.AttachPerfOverlay(perf);
				viewModel = new MainViewModel(new WindowShell(window), hostService, new AvaloniaLibraryDialogs(window), readers, preferences);
				window.DataContext = viewModel;
				if (options.BrowserHeight.HasValue)
				{
					viewModel.BrowserDockHeight = options.BrowserHeight.Value;
				}
				viewModel.EnableLiveRefresh();
				ViewRegistry.Library = viewModel;
				ViewRegistry.MainWindow = window;
				// Avalonia.Native has made the app menu before this runs and ignores a menu set now: its items are changed in place (MainMenu.FillAppMenu).
				NativeMenu appMenu = NativeMenu.GetMenu(this);
				if (appMenu != null)
				{
					MainMenu.FillAppMenu(appMenu, viewModel);
				}
				else
				{
					NativeMenu.SetMenu(this, MainMenu.BuildAppMenu(viewModel));
				}
				NativeMenu.SetMenu(window, MainMenu.BuildMenuBar(viewModel, perf));
				hostService.Opened += (sender, e) => viewModel.OnHostOpened();
				desktop.MainWindow = window;
				desktop.ShutdownRequested += (sender, e) => AppLog.Info("Shutdown requested (Cmd+Q or the last window closed).");
				desktop.Exit += OnExit;

				hostService.Start();
				Task commandLineBook = options.ReadBook != null ? OpenCommandLineBookAsync(options.ReadBook) : Task.CompletedTask;
				if (options.FixtureLibrary != null)
				{
					commandLineBook = Task.WhenAll(commandLineBook, ScanFixtureLibraryAsync(options.FixtureLibrary));
				}
				if (options.Show != null && options.Show != ViewRegistry.MainWindowName)
				{
					// after the fixture library is scanned (a view that shows books, such as Reader, needs them); a view that needs none opens at once
					Task ready = commandLineBook;
					window.Opened += async (sender, e) =>
					{
						try
						{
							await ready;
						}
						catch (Exception)
						{
							// the failure is logged by the task itself
						}
						await ShowRequestedViewAsync(window, options.Show);
					};
				}
				if (options.SmokeTest)
				{
					RunSmokeTest(desktop, window, commandLineBook);
				}
			}
			base.OnFrameworkInitializationCompleted();
		}

		/// <summary>
		/// <c>--show NAME</c> after the fixture scan: a view that needs the books (Reader) says so with an <see cref="InvalidOperationException"/> while the
		/// grid is still loading; it is tried again for up to 20 seconds.
		/// </summary>
		private static async Task ShowRequestedViewAsync(Window owner, string name)
		{
			for (int attempt = 0; attempt < 100; attempt++)
			{
				try
				{
					Window shown = ViewRegistry.Show(name, owner);
					AppLog.Info("Showing the view " + name + (shown != null ? " (\"" + shown.Title + "\")." : "."));
					return;
				}
				catch (InvalidOperationException) when (attempt < 99)
				{
					await Task.Delay(200);
				}
				catch (Exception ex)
				{
					AppLog.Error("The view " + name + " did not open.", ex);
					return;
				}
			}
		}

		/// <summary><c>--fixture-library DIR</c>: scans DIR into the (private) library once the engine is up.</summary>
		private async Task ScanFixtureLibraryAsync(string folder)
		{
			try
			{
				await hostService.Ready;
				if (hostService.Host == null)
				{
					AppLog.Error("Fixture library: the library engine did not start.");
					return;
				}
				await viewModel.ScanFolderAsync(folder);
				AppLog.Info("Fixture library: " + folder + " scanned.");
			}
			catch (Exception ex)
			{
				AppLog.Error("Fixture library failed.", ex);
			}
		}

		/// <summary>
		/// <c>--read-book FILE</c>: opens the file in a reader window through the same path as a double click (without adding it to the
		/// library), waits for the first page, turns one page and logs the sizes. A page that does not show is logged as an error.
		/// </summary>
		private async Task OpenCommandLineBookAsync(string path)
		{
			try
			{
				await hostService.Ready;
				var host = hostService.Host;
				if (host == null || !File.Exists(path))
				{
					AppLog.Error(host == null ? "Reader: the library engine did not start." : "Reader: the file does not exist: " + path);
					return;
				}
				ComicBook book = host.DatabaseManager.BookFactory.Create(path, CreateBookOption.DoNotAdd, RefreshInfoOptions.GetPageCount);
				if (book == null)
				{
					AppLog.Error("Reader: not a book the Core can read: " + path);
					return;
				}
				readers.Open(book);
				ReaderViewModel reader = readers.Slots.FirstOrDefault(s => ReferenceEquals(s.Book, book))?.Model;
				var watch = System.Diagnostics.Stopwatch.StartNew();
				while (reader != null && !(reader.Scene != null || reader.OpenFailed) && watch.Elapsed < TimeSpan.FromSeconds(30))
				{
					await Task.Delay(20);
				}
				ReaderScene scene = reader?.Scene;
				if (scene == null)
				{
					AppLog.Error("Reader: the first page of " + path + " did not show: " + (reader?.Notice ?? "no tab"));
					return;
				}
				string first = scene.Pages[0].Image.Width + " x " + scene.Pages[0].Image.Height;
				string second = "-";
				if (reader.PageCount > 1)
				{
					reader.NextPage();
					await reader.WhenSettled;
					ReaderScene next = reader.Scene;
					second = next != null && next.Position == 1 ? next.Pages[0].Image.Width + " x " + next.Pages[0].Image.Height : "FAILED";
					if (next == null || next.Position != 1)
					{
						AppLog.Error("Reader: page 2 of " + path + " did not show: " + reader.Notice);
						return;
					}
				}
				AppLog.Info($"Reader check: {Path.GetFileName(path)}, {reader.PageCount} pages, page 1 {first}, page 2 {second}, first page after {watch.ElapsedMilliseconds} ms.");
			}
			catch (Exception ex)
			{
				AppLog.Error("Reader check failed.", ex);
			}
		}

		private static void OnUiThreadUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
		{
			AppLog.Error("Unhandled exception on the UI thread.", e.Exception);
			e.Handled = true;
		}

		private void OnExit(object sender, ControlledApplicationLifetimeExitEventArgs e)
		{
			perf?.Hide();
			AppLog.Info("Exit requested, code " + e.ApplicationExitCode + ".");
			// The open books are remembered for the next start (Reopen Books from last session); then the readers go: each records its page in the library, which is saved when the host closes.
			viewModel?.RememberOpenBooks();
			viewModel?.CloseReaders();
			// A running scan stops at its next file; the host then closes and saves the library (and the save is checked).
			viewModel?.CancelRunningOperation();
			hostService?.Dispose();
		}

		/// <summary>
		/// Startup check for scripted runs: the window opens, the host opens, the app stays up for the requested time and
		/// shuts down normally (exit code 0), or with 1 if the host did not open or an error was logged.
		/// </summary>
		private async void RunSmokeTest(IClassicDesktopStyleApplicationLifetime desktop, Window window, Task commandLineBook)
		{
			int code = 1;
			try
			{
				var opened = new TaskCompletionSource<bool>();
				window.Opened += (sender, e) => opened.TrySetResult(true);
				await opened.Task;
				await hostService.Ready;
				AppLog.Info($"Smoke test: window open, host {(hostService.Host != null ? "open" : "FAILED")}; waiting {options.SmokeDuration.TotalSeconds:0.##} s.");
				await commandLineBook; // --read-book: the first page must be shown (a failure is logged as an error)
				await Task.Delay(options.SmokeDuration);
				code = hostService.Host != null && AppLog.ErrorCount == 0 ? 0 : 1;
				AppLog.Info("Smoke test finished, exit code " + code + ".");
			}
			catch (Exception ex)
			{
				AppLog.Error("Smoke test failed.", ex);
			}
			desktop.Shutdown(code);
		}
	}
}
