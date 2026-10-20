using System;
using System.Threading.Tasks;
using ComicRack.Mac.Host.Library;
using ComicRack.Mac.Services;
using ComicRack.Mac.ViewModels;

namespace ComicRack.Mac.Tests
{
	/// <summary>
	/// One launch of the app without a window: the same objects App.OnFrameworkInitializationCompleted wires (HostService,
	/// MainViewModel, the Opened handler), on a private data folder. <see cref="Quit"/> does what App.OnExit does.
	/// </summary>
	public sealed class Launch : IDisposable
	{
		public HostService Service { get; private set; }

		public MainViewModel ViewModel { get; private set; }

		public FakeDialogs Dialogs { get; } = new FakeDialogs();

		public string DataDirectory { get; private set; }

		public bool Quit_Done { get; private set; }

		/// <summary>Starts the host on <paramref name="dataDirectory"/> (the same folder again = a relaunch) and waits for it to open.</summary>
		public static async Task<Launch> Start(string dataDirectory, Func<HostService, IReaderLauncher> readers = null)
		{
			var launch = new Launch { DataDirectory = dataDirectory };
			AppOptions options = AppOptions.Parse(new[] { "--data-dir", dataDirectory });
			if (options.DataDirectory == null)
			{
				throw new InvalidOperationException("A test launch must always have its own data folder.");
			}
			try
			{
				launch.Service = new HostService(options);
				launch.ViewModel = new MainViewModel(new NullShell(), launch.Service, launch.Dialogs, readers?.Invoke(launch.Service));
				launch.Service.Opened += (sender, e) => launch.ViewModel.OnHostOpened();
				launch.Service.Start();
				await launch.Service.Ready;
				launch.ViewModel.OnHostOpened();
				// The sidebar and the grid load on worker threads that read the library's list caches; a test that then changes the library
				// itself must not do it while they run (the Core's cache bookkeeping is not safe against that).
				Task shown = launch.ViewModel.WhenLibraryShownAsync();
				if (await Task.WhenAny(shown, Task.Delay(TimeSpan.FromSeconds(60))) != shown)
				{
					throw new TimeoutException("The sidebar and the grid did not finish loading after the host opened.");
				}
				await shown;
				if (launch.Service.Host == null)
				{
					throw new InvalidOperationException("The host did not open: " + launch.Service.Failure);
				}
				return launch;
			}
			catch (Exception)
			{
				// A failed start must not leave the process-wide host open for the next test.
				try
				{
					launch.Service?.Dispose();
				}
				catch (Exception)
				{
				}
				throw;
			}
		}

		/// <summary>Cmd+Q: cancel a running operation, close the host (which saves) and check the saved file. Returns the check.</summary>
		public SaveVerification Quit()
		{
			ViewModel.CloseReaders(); // App.OnExit closes the readers first: each records its page in the library that is saved next
			ViewModel.CancelRunningOperation();
			Service.Dispose();
			Quit_Done = true;
			return Service.LastSave;
		}

		public void Dispose()
		{
			if (!Quit_Done)
			{
				Quit();
			}
		}
	}
}
