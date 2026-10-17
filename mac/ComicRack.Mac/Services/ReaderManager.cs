using System;
using System.Collections.Generic;
using System.Linq;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Display;
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
	}

	/// <summary>
	/// Opens reader windows for the books of the open library and keeps track of them. One window per book; the fit mode last chosen
	/// is the start mode of the next reader. A reader uses the page cache of the host's <c>ImagePool</c>, so every reader has to be
	/// closed before the host is: <see cref="CloseAll"/> is called before an import replaces the library and before the app quits.
	/// </summary>
	public sealed class ReaderManager : IReaderLauncher
	{
		private readonly HostService hostService;
		private readonly Dictionary<ComicBook, ReaderWindow> windows = new Dictionary<ComicBook, ReaderWindow>();
		private ImageFitMode fit = ImageFitMode.Fit;

		public ReaderManager(HostService hostService)
		{
			this.hostService = hostService ?? throw new ArgumentNullException(nameof(hostService));
		}

		public int OpenCount => windows.Count;

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
			var model = new ReaderViewModel(session, new ReaderOptions { Fit = fit });
			model.FitChanged += (sender, e) => fit = model.Fit;
			var window = new ReaderWindow(model);
			windows[book] = window;
			window.Closed += (sender, e) =>
			{
				windows.Remove(book);
				try
				{
					model.Dispose();
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
