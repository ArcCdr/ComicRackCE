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
		public static async Task<Launch> Start(string dataDirectory)
		{
			var launch = new Launch { DataDirectory = dataDirectory };
			AppOptions options = AppOptions.Parse(new[] { "--data-dir", dataDirectory });
			if (options.DataDirectory == null)
			{
				throw new InvalidOperationException("A test launch must always have its own data folder.");
			}
			launch.Service = new HostService(options);
			launch.ViewModel = new MainViewModel(new NullShell(), launch.Service, launch.Dialogs);
			launch.Service.Opened += (sender, e) => launch.ViewModel.OnHostOpened();
			launch.Service.Start();
			await launch.Service.Ready;
			launch.ViewModel.OnHostOpened();
			if (launch.Service.Host == null)
			{
				throw new InvalidOperationException("The host did not open: " + launch.Service.Failure);
			}
			return launch;
		}

		/// <summary>Cmd+Q: cancel a running operation, close the host (which saves) and check the saved file. Returns the check.</summary>
		public SaveVerification Quit()
		{
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
