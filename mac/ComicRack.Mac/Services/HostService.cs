using System;
using System.IO;
using System.Threading.Tasks;
using ComicRack.Mac.Host;

namespace ComicRack.Mac.Services
{
	/// <summary>
	/// Owns the <see cref="MacHost"/> of the app: opened in the background when the app starts (the Core can take seconds to
	/// load a big database, and the window must not wait for it), disposed when the app exits.
	/// </summary>
	public sealed class HostService : IDisposable
	{
		private readonly AppOptions options;
		private Task<MacHost> opening;
		private bool disposed;

		public HostService(AppOptions options)
		{
			this.options = options;
		}

		/// <summary>The open host, or null while opening, after a failure and after <see cref="Dispose"/>.</summary>
		public MacHost Host { get; private set; }

		/// <summary>Why the host did not open, or null.</summary>
		public Exception Failure { get; private set; }

		/// <summary>Raised on the UI thread when the host opened or failed to open.</summary>
		public event EventHandler Opened;

		/// <summary>Completes when the host opened or failed (it never faults; check <see cref="Failure"/>).</summary>
		public Task Ready { get; private set; } = Task.CompletedTask;

		public void Start()
		{
			if (opening != null)
			{
				return;
			}
			MacPaths paths = options.DataDirectory != null ? MacPaths.Under(options.DataDirectory) : MacPaths.ForCurrentUser();
			AppLog.Info("Starting the host. Data " + paths.DataFolder);
			opening = Task.Run(() => MacHost.Open(new MacHostOptions { Paths = paths }));
			Ready = Complete(opening);
		}

		private async Task Complete(Task<MacHost> task)
		{
			try
			{
				Host = await task.ConfigureAwait(false);
				AppLog.Attach(Host.Log);
				AppLog.Info("Host opened by the app.");
			}
			catch (Exception ex)
			{
				Failure = ex;
				AppLog.Error("The host did not open.", ex);
			}
			UiDispatch.Post(() => Opened?.Invoke(this, EventArgs.Empty));
		}

		/// <summary>Closes the host (which saves the database) and deletes a temporary data folder. Call on the UI thread at exit.</summary>
		public void Dispose()
		{
			if (disposed)
			{
				return;
			}
			disposed = true;
			try
			{
				Ready.GetAwaiter().GetResult();
				MacHost host = Host;
				Host = null;
				AppLog.Detach();
				host?.Dispose();
			}
			catch (Exception ex)
			{
				AppLog.Error("Closing the host failed.", ex);
			}
			if (options.DataDirectoryIsTemporary)
			{
				try
				{
					Directory.Delete(options.DataDirectory, recursive: true);
				}
				catch (Exception)
				{
				}
			}
		}
	}
}
