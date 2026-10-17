using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ComicRack.Mac.Services;
using ComicRack.Mac.ViewModels;
using ComicRack.Mac.Views;

namespace ComicRack.Mac
{
	public sealed class App : Application
	{
		private AppOptions options = new AppOptions();
		private HostService hostService;

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
				var viewModel = new MainViewModel(new WindowShell(window), hostService);
				window.DataContext = viewModel;
				NativeMenu.SetMenu(this, MainMenu.BuildAppMenu(viewModel));
				NativeMenu.SetMenu(window, MainMenu.BuildMenuBar(viewModel));
				hostService.Opened += (sender, e) => viewModel.OnHostOpened();
				desktop.MainWindow = window;
				desktop.ShutdownRequested += (sender, e) => AppLog.Info("Shutdown requested (Cmd+Q or the last window closed).");
				desktop.Exit += OnExit;

				hostService.Start();
				if (options.SmokeTest)
				{
					RunSmokeTest(desktop, window);
				}
			}
			base.OnFrameworkInitializationCompleted();
		}

		private static void OnUiThreadUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
		{
			AppLog.Error("Unhandled exception on the UI thread.", e.Exception);
			e.Handled = true;
		}

		private void OnExit(object sender, ControlledApplicationLifetimeExitEventArgs e)
		{
			AppLog.Info("Exit requested, code " + e.ApplicationExitCode + ".");
			hostService?.Dispose();
		}

		/// <summary>
		/// Startup check for scripted runs: the window opens, the host opens, the app stays up for the requested time and
		/// shuts down normally (exit code 0), or with 1 if the host did not open or an error was logged.
		/// </summary>
		private async void RunSmokeTest(IClassicDesktopStyleApplicationLifetime desktop, Window window)
		{
			int code = 1;
			try
			{
				var opened = new TaskCompletionSource<bool>();
				window.Opened += (sender, e) => opened.TrySetResult(true);
				await opened.Task;
				await hostService.Ready;
				AppLog.Info($"Smoke test: window open, host {(hostService.Host != null ? "open" : "FAILED")}; waiting {options.SmokeDuration.TotalSeconds:0.##} s.");
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
