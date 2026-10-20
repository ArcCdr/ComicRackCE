using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ComicRack.Mac.Host;
using ComicRack.Mac.Host.Library;
using ComicRack.Mac.Thumbnails;

namespace ComicRack.Mac.Services
{
	/// <summary>
	/// Owns the <see cref="MacHost"/> of the app: opened in the background when the app starts (the Core can take seconds to
	/// load a big database, and the window must not wait for it), saved and disposed when the app exits. It also replaces the
	/// library when a Windows database is imported, which needs the host to be closed and opened again.
	///
	/// Everything here that waits uses ConfigureAwait(false): <see cref="Dispose"/> blocks the UI thread at exit, and the
	/// operations it waits for must not need that thread to finish.
	/// </summary>
	public sealed class HostService : IDisposable
	{
		private readonly AppOptions options;
		private readonly SemaphoreSlim importGate = new SemaphoreSlim(1, 1);
		private readonly CancellationTokenSource shutdown = new CancellationTokenSource();
		private Task<MacHost> opening;
		private bool disposed;
		private SettingsStore preferences;
		private readonly object preferencesGate = new object();

		public HostService(AppOptions options)
		{
			this.options = options;
		}

		/// <summary>The open host, or null while opening, after a failure and after <see cref="Dispose"/>.</summary>
		public MacHost Host { get; private set; }

		/// <summary>
		/// The saved settings of the app (<c>&lt;data&gt;/Settings.xml</c>, P5.1), read on first use. They are available before the host opens, so the windows can
		/// be placed where they were. Every change is saved after a short quiet time and everything pending is written by <see cref="Dispose"/>.
		/// </summary>
		public SettingsStore Preferences
		{
			get
			{
				lock (preferencesGate)
				{
					if (preferences == null)
					{
						MacPaths paths = options.DataDirectory != null ? MacPaths.Under(options.DataDirectory) : MacPaths.ForCurrentUser();
						preferences = SettingsStore.Open(SettingsStore.PathIn(paths.DataFolder), log: message => AppLog.Error(message));
						if (preferences.LoadNote != null)
						{
							AppLog.Info(preferences.LoadNote);
						}
					}
					return preferences;
				}
			}
		}

		/// <summary>The folders of the host (data, cache, logs); set by <see cref="Start"/>.</summary>
		public MacPaths Paths { get; private set; }

		/// <summary>
		/// The cover service of the open host, or null while there is no host. It is bound to the host's <c>CacheManager</c>, so it is
		/// created after every open (also after an import) and disposed <b>before</b> the host is closed: a cell that still asks the old
		/// one gets <see cref="ObjectDisposedException"/> or a cancelled task and must cope with both (<c>BookCell</c> does).
		/// </summary>
		public ThumbnailService Thumbnails { get; private set; }

		/// <summary>Why the host did not open, or null.</summary>
		public Exception Failure { get; private set; }

		/// <summary>The result of the last save (explicit, or at close), or null if there was none.</summary>
		public SaveVerification LastSave { get; private set; }

		/// <summary>Raised on the UI thread when the host opened or failed to open (also after an import).</summary>
		public event EventHandler Opened;

		/// <summary>Raised on the UI thread (posted) when the host is about to be closed for an import: stop using <see cref="Thumbnails"/>.</summary>
		public event EventHandler Closing;

		/// <summary>Completes when the host opened or failed (it never faults; check <see cref="Failure"/>).</summary>
		public Task Ready { get; private set; } = Task.CompletedTask;

		public void Start()
		{
			if (opening != null)
			{
				return;
			}
			Paths = options.DataDirectory != null ? MacPaths.Under(options.DataDirectory) : MacPaths.ForCurrentUser();
			AppLog.Info("Starting the host. Data " + Paths.DataFolder);
			BeginOpen();
		}

		private void BeginOpen()
		{
			MacPaths paths = Paths;
			Failure = null;
			var settings = new MacSettings(Preferences.Current);
			opening = Task.Run(() => MacHost.Open(new MacHostOptions { Paths = paths, Settings = settings }));
			Ready = Complete(opening);
		}

		private async Task Complete(Task<MacHost> task)
		{
			try
			{
				MacHost opened = await task.ConfigureAwait(false);
				Thumbnails = new ThumbnailService(opened.CacheManager);
				Host = opened;
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

		/// <summary>Saves the library now (on a worker thread) and checks the file. Throws if there is no open host.</summary>
		public async Task<SaveVerification> SaveAsync(bool countBooks = true)
		{
			MacHost host = Host ?? throw new InvalidOperationException("The library engine is not open.");
			SaveVerification v = await Task.Run(() => host.SaveDatabase(countBooks)).ConfigureAwait(false);
			LastSave = v;
			if (!v.Ok)
			{
				AppLog.Error(v.ToString());
			}
			return v;
		}

		/// <summary>
		/// Replaces the library by a remapped copy of a Windows database. In order: the copy is made next to the library file
		/// (the open library is untouched until it is complete; a failure or a cancel here changes nothing); the open library is
		/// saved and closed; the library file moves to <c>ComicDb.before-import-DATE.xml</c> (with its .bak); the copy takes its
		/// place; the host opens it. If the Core cannot load the copy, the old file moves back and the host opens that again.
		/// The source file is only read.
		/// </summary>
		/// <exception cref="OperationCanceledException">Cancelled while copying.</exception>
		/// <exception cref="InvalidDataException">The source is not a ComicRack database.</exception>
		public async Task<ImportResult> ImportAsync(ImportRequest request, IProgress<int> progress, CancellationToken cancellationToken)
		{
			if (request == null)
			{
				throw new ArgumentNullException(nameof(request));
			}
			await Ready.ConfigureAwait(false);
			if (Paths == null)
			{
				throw new InvalidOperationException("The host was not started.");
			}
			await importGate.WaitAsync(cancellationToken).ConfigureAwait(false);
			try
			{
				using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdown.Token))
				{
					return await ImportCore(request, progress, linked.Token).ConfigureAwait(false);
				}
			}
			finally
			{
				importGate.Release();
			}
		}

		private async Task<ImportResult> ImportCore(ImportRequest request, IProgress<int> progress, CancellationToken ct)
		{
			string target = Path.GetFullPath(Paths.DatabaseFile);
			string source = Path.GetFullPath(request.DatabasePath ?? string.Empty);
			if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
			{
				throw new ArgumentException("That is the library the app is already using: " + target);
			}
			Directory.CreateDirectory(Paths.DataFolder);
			string staged = Path.Combine(Paths.DataFolder, "ComicDb.import.xml");
			AppLog.Info($"Import: {source} with {request.Mappings.Count} mapping(s) into {target}.");

			// 1. The copy, off to the side. Nothing of the open library changes if this fails or is cancelled.
			ImportResult result = await Task.Run(() => LibraryImporter.Import(source, staged, request.Mappings, progress, ct)).ConfigureAwait(false);
			AppLog.Info("Import copy ready: " + result.Summary);

			// 2. From here on the import is not cancelled: it is a few seconds, and stopping half way would leave no library.
			string backup = Path.Combine(Paths.DataFolder, "ComicDb.before-import-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".xml");
			try
			{
				SaveVerification closed = await CloseHostAsync().ConfigureAwait(false);
				if (closed != null && !closed.Ok)
				{
					AppLog.Error("The library could not be saved before the import: " + closed.Problem);
				}
				// A library without books (the one a first launch creates) is not worth a backup file.
				bool hadLibrary = File.Exists(target) && !(closed != null && closed.Ok && closed.BooksInFile == 0);
				if (!hadLibrary)
				{
					TryDelete(target);
					TryDelete(target + ".bak");
				}
				else
				{
					File.Move(target, backup);
					if (File.Exists(target + ".bak"))
					{
						File.Move(target + ".bak", backup + ".bak", overwrite: true);
					}
					result.BackupFile = backup;
				}
				File.Move(staged, target, overwrite: true);

				// 3. Open the copy.
				BeginOpen();
				await Ready.ConfigureAwait(false);
				string problem = Host == null ? "The library engine did not start: " + Failure?.Message : Host.OpenMessage;
				if (problem != null)
				{
					AppLog.Error("The imported library could not be opened: " + problem);
					await RollBackAsync(target, hadLibrary ? backup : null).ConfigureAwait(false);
					throw new InvalidDataException("The imported database could not be opened (" + problem + "). The previous library was restored.");
				}
			}
			finally
			{
				TryDelete(staged);
			}
			return result;
		}

		private async Task RollBackAsync(string target, string backup)
		{
			try
			{
				await CloseHostAsync().ConfigureAwait(false);
				TryDelete(target);
				TryDelete(target + ".bak");
				if (backup != null && File.Exists(backup))
				{
					File.Move(backup, target);
					if (File.Exists(backup + ".bak"))
					{
						File.Move(backup + ".bak", target + ".bak");
					}
				}
			}
			catch (Exception ex)
			{
				AppLog.Error("Restoring the previous library failed. The backup is " + backup, ex);
			}
			BeginOpen();
			await Ready.ConfigureAwait(false);
		}

		private async Task<SaveVerification> CloseHostAsync()
		{
			MacHost host = Host;
			Host = null;
			if (host == null)
			{
				return null;
			}
			AppLog.Detach();
			UiDispatch.Post(() => Closing?.Invoke(this, EventArgs.Empty));
			SaveVerification v = await Task.Run(() =>
			{
				DisposeThumbnails();
				return host.DisposeAndVerify();
			}).ConfigureAwait(false);
			LastSave = v;
			return v;
		}

		private void FlushPreferences()
		{
			SettingsStore store;
			lock (preferencesGate)
			{
				store = preferences;
			}
			try
			{
				store?.Dispose();
			}
			catch (Exception ex)
			{
				AppLog.Error("Saving the settings failed.", ex);
			}
		}

		/// <summary>Stops the cover service (cancels queued and running decodes, waits for the ones that cannot be interrupted). Must precede the close of the host.</summary>
		private void DisposeThumbnails()
		{
			ThumbnailService service = Thumbnails;
			Thumbnails = null;
			try
			{
				service?.Dispose();
			}
			catch (Exception ex)
			{
				AppLog.Error("Stopping the cover service failed.", ex);
			}
		}

		private static void TryDelete(string file)
		{
			try
			{
				File.Delete(file);
			}
			catch (Exception)
			{
			}
		}

		/// <summary>
		/// Closes the host (which saves the database), checks that the file was written and deletes a temporary data folder. Call
		/// on the UI thread at exit. A running import is cancelled if it is still copying, and waited for if it is already replacing
		/// the library. The outcome is in <see cref="LastSave"/>; a failed save is logged as an error.
		/// </summary>
		public void Dispose()
		{
			if (disposed)
			{
				return;
			}
			disposed = true;
			// What was changed in the last moments (a window moved, a size picked) is written first: nothing below can fail it.
			FlushPreferences();
			try
			{
				shutdown.Cancel();
				importGate.Wait(TimeSpan.FromSeconds(60));
				Ready.GetAwaiter().GetResult();
				MacHost host = Host;
				Host = null;
				AppLog.Detach();
				DisposeThumbnails();
				if (host != null)
				{
					SaveVerification v = host.DisposeAndVerify();
					LastSave = v;
					if (v.CloseError != null)
					{
						AppLog.Error("Closing the host failed.", v.CloseError);
					}
					if (v.Ok)
					{
						AppLog.Info(v.ToString());
					}
					else
					{
						AppLog.Error(v.ToString());
					}
				}
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
