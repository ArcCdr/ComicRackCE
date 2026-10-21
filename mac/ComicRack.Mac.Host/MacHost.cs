using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using cYo.Common.Collections;
using cYo.Common.Text;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;
using cYo.Projects.ComicRack.Engine.IO.Cache;
using cYo.Projects.ComicRack.Engine.IO.Provider.Readers.Archive;
using cYo.Projects.ComicRack.Engine.Sync;
using ComicRack.Mac.Host.Library;
using ComicRack.Mac.Host.Platform;
using cYo.Projects.ComicRack.Plugins;

namespace ComicRack.Mac.Host
{
	/// <summary>
	/// The composition root of the macOS build: what ComicRack/Program.cs StartNew builds for the WinForms app, reduced to the
	/// DatabaseManager -> CacheManager -> QueueManager chain. Skipped on purpose: news, backup, plugins and scripts, single
	/// instance, device and network sync, splash, themes, WinForms renderers, icon packages, default lists.
	///
	/// The Core keeps process-wide static state (EngineConfiguration.Default, the virtual tag settings, the query cache mode), so
	/// only one host can be open at a time. Open, use and Dispose it, then open another one if needed.
	/// </summary>
	public sealed class MacHost : IDisposable
	{
		private static readonly object gate = new object();
		private static MacHost current;
		private static bool matchersRegistered;

		private readonly bool readOnly;
		private int booksAtFinalSave = -1;
		private string finalSaveNote;
		private bool disposed;

		public MacPaths Paths { get; }

		public SystemPaths SystemPaths { get; private set; }

		public MacSettings Settings { get; }

		public HostLog Log { get; private set; }

		public DatabaseManager DatabaseManager { get; private set; }

		public CacheManager CacheManager { get; private set; }

		public QueueManager QueueManager { get; private set; }

		public ImagePool ImagePool => CacheManager.ImagePool;

		/// <summary>The ComicDb.xml that was opened.</summary>
		public string DatabaseFile { get; }

		/// <summary>True if the database file did not exist, so the database started empty.</summary>
		public bool IsNewDatabase { get; private set; }

		/// <summary>The Core's remark about the open (a restored backup, a corrupt file replaced by an empty database), or null.</summary>
		public string OpenMessage { get; private set; }

		public bool IsDisposed => disposed;

		private HostTraceListener traceListener;

		private MacHost(MacPaths paths, MacSettings settings, string databaseFile, bool readOnly)
		{
			Paths = paths;
			Settings = settings;
			DatabaseFile = databaseFile;
			this.readOnly = readOnly;
		}

		/// <summary>Builds the Core services and opens the database (synchronously).</summary>
		/// <exception cref="InvalidOperationException">Another host is still open.</exception>
		public static MacHost Open(MacHostOptions options = null)
		{
			options = options ?? new MacHostOptions();
			MacPaths paths = options.Paths ?? MacPaths.ForCurrentUser();
			string databaseFile = Path.GetFullPath(options.DatabaseFile ?? paths.DatabaseFile);
			if (!string.Equals(Path.GetExtension(databaseFile), ".xml", StringComparison.OrdinalIgnoreCase))
			{
				throw new ArgumentException("The database file must end with .xml: " + databaseFile, nameof(options));
			}
			lock (gate)
			{
				if (current != null)
				{
					throw new InvalidOperationException("A MacHost is already open. Dispose it first.");
				}
				var host = new MacHost(paths, options.Settings ?? new MacSettings(), databaseFile, options.DatabaseReadOnly);
				current = host;
				try
				{
					host.Start(options);
				}
				catch (Exception ex)
				{
					host.Log?.Error("Opening the host failed.", ex);
					try
					{
						host.Teardown(rethrow: false);
					}
					catch (Exception)
					{
					}
					current = null;
					throw;
				}
				return host;
			}
		}

		private void Start(MacHostOptions options)
		{
			Paths.CreateFolders();
			Log = new HostLog(Paths.LogFile);
			NativeLibraryResolver.Log = Log;
			// The Core reports an abandoned worker thread (the pass B replacements of Thread.Abort, P1.1) through Trace.
			traceListener = new HostTraceListener(Log);
			Trace.Listeners.Add(traceListener);
			AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
			TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
			Log.Info($"Opening host. Data {Paths.DataFolder}, cache {Paths.CacheFolder}, database {DatabaseFile}{(readOnly ? " (read only)" : "")}, {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription}");

			ConfigureCore();

			// Program.cs:116 builds SystemPaths with the /db and /cp switches; here they are always explicit.
			SystemPaths = Paths.CreateSystemPaths();

			// Program.cs:136 DatabaseManager, :1030 InitializeDatabase (Open with the path without ".xml").
			DatabaseManager = new DatabaseManager();
			IsNewDatabase = !File.Exists(DatabaseFile);
			DatabaseManager.Open(DatabaseFile.Substring(0, DatabaseFile.Length - ".xml".Length), null, dontLoadQueryCaches: false, progress: null);
			// DatabaseManager.Database is not safe for two first accesses at once (the first one closes its wait handle): make it here, on the opening thread.
			_ = DatabaseManager.Database;
			OpenMessage = IsNewDatabase ? null : DatabaseManager.OpenMessage;
			if (OpenMessage != null)
			{
				Log.Info("Database: " + OpenMessage);
			}
			if (readOnly)
			{
				DatabaseManager.InitialConnectionError = true;
			}
			else
			{
				DatabaseManager.BackgroundSaveInterval = options.DatabaseBackgroundSaveSeconds;
			}

			// Program.cs:882-883. The WinForms app also passes Resources.ResourceManager (the constructor ignores it and reads
			// Resources.ResourceManager itself) and Settings.Devices; the MVP has no devices.
			CacheManager = new CacheManager(DatabaseManager, SystemPaths, Settings);
			QueueManager = new QueueManager(DatabaseManager, CacheManager, Settings, new List<DeviceSyncSettings>());
			Log.Info("Host open.");
		}

		/// <summary>
		/// <c>Program.LoadCustomThumbnail</c> for a picture file: the picture becomes a custom thumbnail in the Core's image pool and its key is returned (a book without a file
		/// shows it as its cover). Null when the file cannot be read as a picture.
		/// </summary>
		public string AddCustomThumbnail(string file)
		{
			try
			{
				using (var bitmap = cYo.Common.Drawing.BitmapExtensions.BitmapFromFile(file))
				{
					return bitmap == null ? null : ImagePool.AddCustomThumbnail(bitmap);
				}
			}
			catch (Exception ex)
			{
				Log?.Error("The thumbnail picture " + file + " could not be used.", ex);
				return null;
			}
		}

		/// <summary>The static configuration that StartNew applies before the services exist.</summary>
		private void ConfigureCore()
		{
			NativeLibraryResolver.Install();

			// Program.cs:742-743. The smart lists of a real database contain script based matchers, so the types must be known to load it.
			if (!matchersRegistered)
			{
				ComicBookValueMatcher.RegisterMatcherType(typeof(ComicBookPluginMatcher));
				ComicBookValueMatcher.RegisterMatcherType(typeof(ComicBookExpressionMatcher));
				matchersRegistered = true;
			}

			// The archive engines default to SevenZip (a Windows DLL); SharpCompress is managed.
			EngineConfiguration config = EngineConfiguration.Default;
			config.CbzUses = config.CbrUses = config.Cb7Uses = config.CbtUses = EngineConfiguration.CbEngines.SharpCompress;

			// ComicInfo writes and CB7 creation run the bundled 7zz (P2.4b); without one the Core's calls fail like a missing 7z.exe (the write returns false).
			string sevenZip = ToolLocator.SevenZip;
			if (sevenZip != null)
			{
				SevenZipEngine.PackExe = sevenZip;
			}

			// ShellFile.DeleteFile (queue, exporter, backups) sends files to the Trash; the pass B ShellFile has no shell of its own (P2.1).
			cYo.Common.Win32.ShellFile.TrashHandler = path => MacShell.MoveToTrash(path);

			// The NTFS alternate data stream store (NtfsInfoStorage) calls kernel32 CreateFile for every scanned book.
			config.DisableNTFS = true;

			// Program.cs:780-787 with the ExtendedSettings defaults (ExtendedSettings.cs:57, 75, 82; not set by its constructor: DisableBackgroundQueryCacheUpdate stays false).
			ListExtensions.ParallelEnabled = config.EnableParallelQueries;
			if (config.IgnoredArticles != null)
			{
				StringUtility.Articles = config.IgnoredArticles;
			}
			ComicLibrary.QueryCacheMode = QueryCacheMode.InstantUpdate;
			ComicLibrary.BackgroundQueryCacheUpdate = true;
			ComicBook.EnableGroupNameCompression = false;

			// ComicBrowserControl.cs:1609 registers the settings before the first virtual tag is read.
			VirtualTagsCollection.RegisterSettings(Settings);

			WarmLanguageCultures();
			ConfigureIcons();
		}

		// Program.cs:838-843: the publisher, age rating, format and special icon packages that the covers and the Info dialog show. The folder of the program
		// holds the repository's icons; a folder Resources/Icons in the data folder takes the user's own (the Windows app reads the same relative path in its data folders).
		private void ConfigureIcons()
		{
			try
			{
				IconPackages.Load(new[] { IconPackages.DefaultFolder, System.IO.Path.Combine(Paths.DataFolder, "Resources", "Icons") });
			}
			catch (Exception ex)
			{
				Log?.Error("The icon packages could not be loaded.", ex);
			}
		}

		private static readonly object WarmGate = new object();
		private static bool languagesWarmed;

		/// <summary>
		/// ComicBook.GetIsoCulture fills a static, unsynchronized Dictionary on first use per language. A smart list with a language
		/// matcher evaluates its books in PLINQ, so concurrent first calls corrupt the dictionary and lose books (found by P0.7a: a
		/// 64-book list returned 30 or 31 in 2 of 12 fresh processes). Filling it once on this thread, for every two-letter ISO code it
		/// can ever cache, makes all later calls read-only. Other lazily filled statics reached from list evaluation:
		/// hasAsText and WebComic.rxCache are locked; searchableProperties and Description are idempotent single assignments;
		/// cachedTypeList is filled by the matcher registration above.
		/// </summary>
		private static void WarmLanguageCultures()
		{
			lock (WarmGate)
			{
				if (languagesWarmed)
				{
					return;
				}
				foreach (string iso in CultureInfo.GetCultures(CultureTypes.NeutralCultures).Select(c => c.TwoLetterISOLanguageName).Distinct())
				{
					ComicBook.GetIsoCulture(iso);
				}
				languagesWarmed = true;
			}
		}

		/// <summary>
		/// Writes the database to <see cref="DatabaseFile"/> now and checks the file. <c>DatabaseManager.Save</c> swallows its
		/// exceptions, so the file is the only evidence: it must have been modified by this call and (with
		/// <paramref name="countBooks"/>) hold as many books as the library had. Do not call it while a scan adds books.
		/// </summary>
		public SaveVerification SaveDatabase(bool countBooks = true)
		{
			if (disposed)
			{
				throw new ObjectDisposedException(nameof(MacHost));
			}
			if (readOnly)
			{
				return new SaveVerification { File = DatabaseFile, Ok = true, Skipped = true };
			}
			DateTime startedUtc = DateTime.UtcNow;
			int books = DatabaseManager.Database.Books.Count;
			DatabaseManager.Save();
			SaveVerification v = SaveVerification.Check(DatabaseFile, startedUtc, countBooks ? books : -1);
			if (v.Ok)
			{
				Log?.Info(v.ToString());
			}
			else
			{
				Log?.Error(v.ToString());
			}
			return v;
		}

		/// <summary>
		/// <see cref="Dispose"/> (which saves the database, unless read only) and then checks the file as <see cref="SaveDatabase"/> does.
		/// Used at quit: the result tells the caller whether the library really reached the disk. The expected number of books is the one at the moment the queues have stopped (a scan that was running adds books until then).
		/// </summary>
		public SaveVerification DisposeAndVerify(bool countBooks = true)
		{
			if (disposed)
			{
				return new SaveVerification { File = DatabaseFile, Ok = true, Skipped = true };
			}
			DateTime startedUtc = DateTime.UtcNow;
			Exception closeError = null;
			try
			{
				Dispose();
			}
			catch (Exception ex)
			{
				closeError = ex;
			}
			// A scanner thread that outlived the close may still add books: the file cannot be compared with a book count then.
			string note = finalSaveNote;
			SaveVerification v = readOnly
				? new SaveVerification { File = DatabaseFile, Ok = true, Skipped = true }
				: SaveVerification.Check(DatabaseFile, startedUtc, countBooks && note == null ? booksAtFinalSave : -1);
			if (note != null && !readOnly)
			{
				v.Unchecked = true;
				v.Note = note;
				if (v.Ok)
				{
					// still well formed, with whatever books it holds
					try
					{
						v.BooksInFile = SaveVerification.CountBooks(DatabaseFile);
					}
					catch (Exception ex)
					{
						v.Ok = false;
						v.Problem = "The database file could not be read back: " + ex.Message;
					}
				}
			}
			v.CloseError = closeError;
			return v;
		}

		/// <summary>
		/// Stops the queues, saves the database (unless read only) and releases everything. Every step runs even if an earlier one
		/// fails; the failures are logged and thrown together afterwards. Calling it again does nothing.
		/// </summary>
		public void Dispose()
		{
			lock (gate)
			{
				if (disposed)
				{
					return;
				}
				try
				{
					Teardown(rethrow: true);
				}
				finally
				{
					if (current == this)
					{
						current = null;
					}
				}
			}
		}

		private void Teardown(bool rethrow)
		{
			disposed = true;
			var errors = new List<Exception>();
			Log?.Info("Closing host.");

			// Program.cs CleanUp (:1043): QueueManager, ImagePool, DatabaseManager (which saves). The CacheManager has nothing to dispose.
			// The scanner is read before the QueueManager is disposed (its getter creates the scanner on first use and the QueueManager is gone afterwards).
			ComicScanner scanner = null;
			try
			{
				scanner = QueueManager?.Scanner;
			}
			catch (Exception)
			{
			}
			Step(errors, "QueueManager", QueueManager);
			Step(errors, "ImagePool", CacheManager?.ImagePool);
			// The queues (a running scan) have stopped, so this is the number of books the final save writes.
			try
			{
				booksAtFinalSave = DatabaseManager?.Database.Books.Count ?? -1;
			}
			catch (Exception)
			{
			}
			// ComicScanner.Stop gives up on a scanning thread after 10 s (the pass B replacement of Thread.Abort). That thread can still add a
			// book after the count above and after the final save, so the save cannot be checked against a count and the loss is not silent.
			if (scanner != null && scanner.HasAbandonedThread)
			{
				finalSaveNote = "A book scanner thread did not stop and was abandoned; books it adds after the final save are not in the database file, so the book count was not checked.";
				Log?.Write("WARN", finalSaveNote);
			}
			Step(errors, "DatabaseManager", DatabaseManager);

			if (traceListener != null)
			{
				Trace.Listeners.Remove(traceListener);
				traceListener = null;
			}
			AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
			TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
			Log?.Info(errors.Count == 0 ? "Host closed." : $"Host closed with {errors.Count} error(s).");
			if (ReferenceEquals(NativeLibraryResolver.Log, Log))
			{
				NativeLibraryResolver.Log = null;
			}
			Log?.Dispose();

			if (rethrow && errors.Count > 0)
			{
				throw new AggregateException("Closing the host failed.", errors);
			}
		}

		private void Step(List<Exception> errors, string name, IDisposable item)
		{
			if (item == null)
			{
				return;
			}
			try
			{
				item.Dispose();
			}
			catch (Exception ex)
			{
				Log?.Error("Closing " + name + " failed.", ex);
				errors.Add(ex);
			}
		}

		private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
		{
			Log?.Error("Unhandled exception" + (e.IsTerminating ? " (the process ends)." : "."), e.ExceptionObject as Exception);
		}

		private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
		{
			Log?.Error("Unobserved task exception.", e.Exception);
		}
	}
}
