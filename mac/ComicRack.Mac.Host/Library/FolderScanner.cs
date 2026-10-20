using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.Host.Library
{
	public enum ScanOutcome
	{
		Completed,
		Cancelled,
	}

	/// <summary>A snapshot of a running scan. Created on the scanner thread; immutable.</summary>
	public sealed class ScanProgress
	{
		/// <summary>Files the scanner has started to look at (a book file is counted when its turn comes, before it is read).</summary>
		public int FilesSeen { get; }

		/// <summary>Books that were not in the library before this scan and are now.</summary>
		public int BooksAdded { get; }

		/// <summary>Books in the library now.</summary>
		public int BooksTotal { get; }

		/// <summary>The file being looked at (full path), or null before the first file.</summary>
		public string CurrentFile { get; }

		/// <summary>True for the last report of a scan.</summary>
		public bool IsFinal { get; }

		public ScanProgress(int filesSeen, int booksAdded, int booksTotal, string currentFile, bool isFinal)
		{
			FilesSeen = filesSeen;
			BooksAdded = booksAdded;
			BooksTotal = booksTotal;
			CurrentFile = currentFile;
			IsFinal = isFinal;
		}
	}

	public sealed class ScanResult
	{
		public ScanOutcome Outcome { get; internal set; }

		public int FilesSeen { get; internal set; }

		public int BooksAdded { get; internal set; }

		public int BooksTotal { get; internal set; }

		public TimeSpan Elapsed { get; internal set; }
	}

	/// <summary>
	/// Scan orchestration on top of the Core's <see cref="ComicScanner"/> (the queue behind <c>QueueManager.Scanner</c>): runs one
	/// scan of a folder, reports progress (a count and the current file, at most every <c>ReportInterval</c>) and can be
	/// cancelled. No UI types: the progress callback is called on the scanner's thread, and the caller marshals it
	/// (the app does it through UiDispatch).
	///
	/// Cancel is cooperative, as in the Core: the scanner raises ScanNotify before each file, and a handler that sets
	/// <c>Cancel</c> makes it return (and <c>ClearQueue</c> empties its queue). That handler runs on the scanner thread and must
	/// answer at once, so it is not routed through the UI thread. Books that were added before the cancel stay in the library,
	/// so a cancelled scan leaves a complete, consistent database; scanning the same folder again adds only the rest.
	/// </summary>
	public sealed class FolderScanner
	{
		private readonly MacHost host;
		private int running;

		/// <summary>Minimum time between two progress reports (the last one is always sent).</summary>
		public TimeSpan ReportInterval { get; set; } = TimeSpan.FromMilliseconds(100);

		public FolderScanner(MacHost host)
		{
			this.host = host ?? throw new ArgumentNullException(nameof(host));
		}

		public bool IsRunning => Volatile.Read(ref running) != 0;

		/// <summary>
		/// Scans <paramref name="folder"/> (all sub folders) and completes when the scanner has finished or acknowledged the cancel.
		/// Never throws for a cancel: check <see cref="ScanResult.Outcome"/>.
		/// </summary>
		/// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
		/// <exception cref="InvalidOperationException">A scan of this scanner is already running.</exception>
		public async Task<ScanResult> ScanAsync(string folder, IProgress<ScanProgress> progress, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(folder))
			{
				throw new ArgumentException("No folder given.", nameof(folder));
			}
			folder = Path.GetFullPath(folder);
			if (!Directory.Exists(folder))
			{
				throw new DirectoryNotFoundException("The folder does not exist: " + folder);
			}
			if (Interlocked.Exchange(ref running, 1) != 0)
			{
				throw new InvalidOperationException("A scan is already running.");
			}
			try
			{
				return await Run(folder, progress, cancellationToken).ConfigureAwait(false);
			}
			finally
			{
				Volatile.Write(ref running, 0);
			}
		}

		private async Task<ScanResult> Run(string folder, IProgress<ScanProgress> progress, CancellationToken ct)
		{
			var sw = Stopwatch.StartNew();
			ComicScanner scanner = host.QueueManager.Scanner;
			var books = host.DatabaseManager.Database.Books;
			int booksBefore = books.Count;
			long interval = (long)(ReportInterval.TotalSeconds * Stopwatch.Frequency);

			// An earlier (cancelled) scan thread of this scanner may still be on its way out: its final notification must not end this
			// scan and its queue clearing must not remove this scan's folder. Only the thread this scanner started is awaited; another
			// thread that happens to carry the same name (another host, a test) is not ours.
			await WaitForThreadToExit(scanner.LastScanThread).ConfigureAwait(false);

			var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			int files = 0;
			bool cancelled = false;
			string current = null;
			long lastReport = 0;

			ScanProgress Snapshot(bool final)
			{
				int total = books.Count;
				return new ScanProgress(Volatile.Read(ref files), Math.Max(0, total - booksBefore), total, Volatile.Read(ref current), final);
			}

			EventHandler<ComicScanNotifyEventArgs> handler = (sender, e) =>
			{
				if (e.File.Length == 0)
				{
					// The scanner is done (it raises this from its finally block, also after a cancel).
					finished.TrySetResult(true);
					return;
				}
				if (ct.IsCancellationRequested)
				{
					cancelled = true;
					e.Cancel = true;
					e.ClearQueue = true;
					return;
				}
				Volatile.Write(ref current, e.File);
				Interlocked.Increment(ref files);
				long now = Stopwatch.GetTimestamp();
				if (progress != null && now - lastReport >= interval)
				{
					lastReport = now;
					progress.Report(Snapshot(final: false));
				}
			};

			scanner.ScanNotify += handler;
			try
			{
				scanner.ScanFileOrFolder(folder, all: true, removeMissing: false);
				await finished.Task.ConfigureAwait(false);
			}
			finally
			{
				scanner.ScanNotify -= handler;
			}

			ScanProgress last = Snapshot(final: true);
			progress?.Report(last);
			return new ScanResult
			{
				Outcome = cancelled ? ScanOutcome.Cancelled : ScanOutcome.Completed,
				FilesSeen = last.FilesSeen,
				BooksAdded = last.BooksAdded,
				BooksTotal = last.BooksTotal,
				Elapsed = sw.Elapsed,
			};
		}

		private static async Task WaitForThreadToExit(Thread thread)
		{
			if (thread == null)
			{
				return;
			}
			var deadline = DateTime.UtcNow.AddSeconds(15);
			while (thread.IsAlive && DateTime.UtcNow < deadline)
			{
				await Task.Delay(10).ConfigureAwait(false);
			}
		}
	}
}
