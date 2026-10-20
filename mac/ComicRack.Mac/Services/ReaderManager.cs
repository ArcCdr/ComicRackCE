using System;
using System.Collections.Generic;
using System.Linq;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Display;
using ComicRack.Mac.Diagnostics;
using ComicRack.Mac.Host;
using ComicRack.Mac.Reader;
using ComicRack.Mac.ViewModels;
using ComicRack.Mac.Views;

namespace ComicRack.Mac.Services
{
	/// <summary>What the library window asks of the reader: open a book, and close every reader before the library is replaced or the app quits.</summary>
	public interface IReaderLauncher
	{
		/// <summary>Opens <paramref name="book"/> in a reader window (brings the window forward if the book is already open).</summary>
		void Open(ComicBook book);

		/// <summary>Closes every reader window, which records the page each one was on. Call before the host is closed or replaced.</summary>
		void CloseAll();

		/// <summary>Readers that are open.</summary>
		int OpenCount { get; }

		/// <summary>True if <paramref name="book"/> is open in a reader (the browser draws the "open" state icon on its cover).</summary>
		bool IsOpen(ComicBook book);

		/// <summary>Raised on the UI thread when a reader was opened or closed.</summary>
		event EventHandler OpenBooksChanged;
	}

	/// <summary>
	/// Opens reader windows for the books of the open library and keeps track of them. One window per book; the fit mode, the page layout and the reading
	/// direction last chosen are the start values of the next reader, and with the window frame they are kept in the saved preferences (P5.1). A reader uses the page cache of the host's <c>ImagePool</c>, so every reader has to be
	/// closed before the host is: <see cref="CloseAll"/> is called before an import replaces the library and before the app quits.
	/// </summary>
	public sealed class ReaderManager : IReaderLauncher
	{
		private readonly HostService hostService;
		private readonly Dictionary<ComicBook, ReaderWindow> windows = new Dictionary<ComicBook, ReaderWindow>();
		private readonly AppPreferences preferences;
		private ImageFitMode fit = ImageFitMode.Fit;
		private PageLayoutMode layoutMode = PageLayoutMode.Single;

		private readonly PerfOverlayController perf;
		private ReaderWindow recent;

		/// <param name="preferences">The saved preferences the readers start from and write back to; null for none (every reader starts with the defaults).</param>
		public ReaderManager(HostService hostService, PerfOverlayController perf = null, AppPreferences preferences = null)
		{
			this.hostService = hostService ?? throw new ArgumentNullException(nameof(hostService));
			this.perf = perf;
			this.preferences = preferences;
			if (preferences != null)
			{
				fit = ReaderOptions.ParseFit(preferences.ReaderFit);
				layoutMode = ReaderOptions.ParseMode(preferences.ReaderLayout);
			}
		}

		/// <summary>The reader the overlay reports on: the active reader window, else the one used last. Null if none is open.</summary>
		public ReaderViewModel FrontReader
		{
			get
			{
				ReaderWindow active = windows.Values.FirstOrDefault(w => w.IsActive);
				if (active != null)
				{
					return active.Model;
				}
				return recent != null && windows.ContainsValue(recent) ? recent.Model : windows.Values.LastOrDefault()?.Model;
			}
		}

		/// <summary>The decode time of the last page the front reader decoded, or null.</summary>
		public double? LastDecodeMilliseconds()
		{
			double ms = FrontReader?.Pages?.GetStats().LastDecodeMilliseconds ?? 0;
			return ms > 0 ? ms : (double?)null;
		}

		/// <summary>The total time of the last page turn of the front reader that needed no decoding, or null.</summary>
		public double? LastCachedTurnMilliseconds()
		{
			PageTurnRecord[] records = FrontReader?.Meter.Records;
			PageTurnRecord last = records?.LastOrDefault(r => r.Cached);
			return last?.TotalMilliseconds;
		}

		public int OpenCount => windows.Count;

		public bool IsOpen(ComicBook book) => book != null && windows.ContainsKey(book);

		public event EventHandler OpenBooksChanged;

		/// <summary>The readers that are open (for tests).</summary>
		public IReadOnlyCollection<ReaderWindow> Windows => windows.Values.ToArray();

		public void Open(ComicBook book)
		{
			if (book == null)
			{
				return;
			}
			if (windows.TryGetValue(book, out ReaderWindow existing))
			{
				existing.Activate();
				return;
			}
			var host = hostService.Host;
			if (host == null)
			{
				AppLog.Info("A reader was requested while the library engine is not running.");
				return;
			}
			var session = new CoreReadingSession(book, host.ImagePool);
			ReaderOptions readerOptions = ReaderOptions.From(preferences);
			readerOptions.Fit = fit;
			readerOptions.Mode = layoutMode;
			var model = new ReaderViewModel(session, readerOptions);
			model.FitChanged += (sender, e) =>
			{
				fit = model.Fit;
				if (preferences != null)
				{
					preferences.ReaderFit = fit.ToString();
				}
			};
			model.LayoutChanged += (sender, e) =>
			{
				layoutMode = model.LayoutMode;
				if (preferences != null)
				{
					preferences.ReaderLayout = layoutMode.ToString();
				}
			};
			// A book that says nothing about its direction starts the way the last such book was read.
			model.PropertyChanged += (sender, e) =>
			{
				if (e.PropertyName == nameof(ReaderViewModel.RightToLeft) && preferences != null && session.RightToLeft == null)
				{
					preferences.RightToLeftDefault = model.RightToLeft;
				}
			};
			var thumbnails = hostService.Thumbnails != null ? new CorePageThumbnails(book, hostService.Thumbnails) : null;
			var window = new ReaderWindow(model, false, perf, thumbnails);
			if (preferences != null)
			{
				// Where the last reader was; one that opens while others are open is moved aside so that the windows do not cover each other.
				int others = windows.Count;
				WindowFrame placed = WindowFrameTracker.Restore(window, preferences.ReaderWindow, others);
				var tracker = WindowFrameTracker.Attach(window, frame => preferences.ReaderWindow = frame, placed);
				window.Closed += (sender, e) => tracker.Dispose();
			}
			window.Activated += (sender, e) => recent = window;
			recent = window;
			windows[book] = window;
			OpenBooksChanged?.Invoke(this, EventArgs.Empty);
			window.Closed += (sender, e) =>
			{
				windows.Remove(book);
				OpenBooksChanged?.Invoke(this, EventArgs.Empty);
				try
				{
					model.Dispose();
					thumbnails?.Dispose();
				}
				catch (Exception ex)
				{
					AppLog.Error("Closing the reader failed.", ex);
				}
				AppLog.Info("Reader closed: " + book.FilePath + " on page " + (model.CurrentPage + 1) + ".");
			};
			AppLog.Info("Reader opened: " + book.FilePath);
			window.Show();
			_ = OpenAsync(model);
		}

		private static async System.Threading.Tasks.Task OpenAsync(ReaderViewModel model)
		{
			try
			{
				await model.OpenAsync();
			}
			catch (Exception ex)
			{
				AppLog.Error("Opening the reader failed.", ex);
			}
		}

		public void CloseAll()
		{
			foreach (ReaderWindow window in windows.Values.ToArray())
			{
				window.Close();
			}
		}
	}
}
