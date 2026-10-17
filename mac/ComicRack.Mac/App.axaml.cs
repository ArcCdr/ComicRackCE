using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using cYo.Projects.ComicRack.Engine;
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
			if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
			{
				// An exception on the UI thread is logged through the host log and the app keeps running.
				Dispatcher.UIThread.UnhandledException += OnUiThreadUnhandledException;

				hostService = new HostService(options);
				var window = new MainWindow();
				readers = new ReaderManager(hostService);
				viewModel = new MainViewModel(new WindowShell(window), hostService, new AvaloniaLibraryDialogs(window), readers);
				window.DataContext = viewModel;
				NativeMenu.SetMenu(this, MainMenu.BuildAppMenu(viewModel));
				NativeMenu.SetMenu(window, MainMenu.BuildMenuBar(viewModel));
				hostService.Opened += (sender, e) => viewModel.OnHostOpened();
				desktop.MainWindow = window;
				desktop.ShutdownRequested += (sender, e) => AppLog.Info("Shutdown requested (Cmd+Q or the last window closed).");
				desktop.Exit += OnExit;

				hostService.Start();
				Task commandLineBook = options.ReadBook != null ? OpenCommandLineBookAsync(options.ReadBook) : Task.CompletedTask;
				if (options.SmokeTest)
				{
					RunSmokeTest(desktop, window, commandLineBook);
				}
			}
			base.OnFrameworkInitializationCompleted();
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
				ReaderWindow reader = readers.Windows.FirstOrDefault(w => ReferenceEquals(((CoreReadingSession)w.Model.Session).Book, book));
				var watch = System.Diagnostics.Stopwatch.StartNew();
				while (reader != null && !(reader.Model.Scene != null || (reader.Model.HasNotice && reader.Model.Notice != "Opening..." && !reader.Model.IsOpen)) && watch.Elapsed < TimeSpan.FromSeconds(30))
				{
					await Task.Delay(20);
				}
				ReaderScene scene = reader?.Model.Scene;
				if (scene == null)
				{
					AppLog.Error("Reader: the first page of " + path + " did not show: " + (reader?.Model.Notice ?? "no window"));
					return;
				}
				string first = scene.Pages[0].Image.Width + " x " + scene.Pages[0].Image.Height;
				string second = "-";
				if (reader.Model.PageCount > 1)
				{
					reader.Model.NextPage();
					await reader.Model.WhenSettled;
					ReaderScene next = reader.Model.Scene;
					second = next != null && next.Position == 1 ? next.Pages[0].Image.Width + " x " + next.Pages[0].Image.Height : "FAILED";
					if (next == null || next.Position != 1)
					{
						AppLog.Error("Reader: page 2 of " + path + " did not show: " + reader.Model.Notice);
						return;
					}
				}
				AppLog.Info($"Reader check: {Path.GetFileName(path)}, {reader.Model.PageCount} pages, page 1 {first}, page 2 {second}, first page after {watch.ElapsedMilliseconds} ms.");
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
			AppLog.Info("Exit requested, code " + e.ApplicationExitCode + ".");
			// The readers go first: each records its page in the library, which is saved when the host closes.
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
