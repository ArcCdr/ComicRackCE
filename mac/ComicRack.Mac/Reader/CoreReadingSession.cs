using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.IO.Cache;

namespace ComicRack.Mac.Reader
{
	/// <summary>
	/// A Core book open for reading: the sequence of <c>NavigatorManager.OpenComic</c> and <c>MainForm.OnBookOpened</c> of the
	/// WinForms app, without the tabs and slots. In order: <c>RefreshInfoFromFile</c> (file size and time, which are part of the page
	/// keys, and the page count), <c>CreateNavigator</c>, <c>Open(async: true)</c> (the index of the archive is read on the navigator's
	/// own thread), and when the index is complete: <c>OpenedCount</c> + 1, <c>OpenedTime</c> now and <c>NewPages</c> 0 (the latter two
	/// only if <c>ComicBookNavigator.TrackCurrentPage</c> is on, as the WinForms app does).
	///
	/// <b>What "write the last page back" means in the Core.</b> <c>ComicBookNavigator.CurrentPage</c> sets <c>ComicBook.CurrentPage</c>,
	/// and <c>ComicBook.CurrentPage</c> raises <c>LastPageRead</c> when it passes it; both are properties of the book in the library
	/// and are saved with the database. So the session writes through the navigator at every page turn (<see cref="ReportPage"/>) and
	/// once more when the reader closes; nothing is left to do at close except to make sure the last page was recorded. The navigator's
	/// own <c>LastPageRead</c> setter is a no-op in the Core (it compares the value with itself after assigning it).
	/// Resuming reopens at <c>ComicBook.CurrentPage</c>, where the reader was left, as the WinForms app does with "open last page";
	/// <c>LastPageRead</c> keeps the furthest page.
	///
	/// The events of the navigator come from its parser thread; the handlers only complete a task, which is safe on any thread, so they
	/// do not go through <see cref="UiDispatch"/>.
	/// </summary>
	public sealed class CoreReadingSession : IReadingSession
	{
		private readonly ComicBook book;
		private readonly ImagePool pool;
		private readonly long maxPixels;
		private ComicBookNavigator navigator;
		private CorePageDecoder decoder;
		private int pageCount;
		private int resumePage;
		private bool disposed;

		public CoreReadingSession(ComicBook book, ImagePool pool, long maxPixels = PagePixels.DefaultMaxPixels)
		{
			this.book = book ?? throw new ArgumentNullException(nameof(book));
			this.pool = pool ?? throw new ArgumentNullException(nameof(pool));
			this.maxPixels = maxPixels;
		}

		public ComicBook Book => book;

		/// <summary>
		/// False for a book that is reopened by <c>OpenLastFile</c> at the start (<c>OpenComicOptions.NoIncreaseOpenedCount</c>): the book is read again, but the
		/// library does not count another opening.
		/// </summary>
		public bool IncreaseOpenedCount { get; set; } = true;

		/// <summary>
		/// <c>OpenComicOptions.DisableAll</c> of the small preview (<c>SmallComicPreview.ShowPreview</c>): the book is read from its first page and nothing is written back:
		/// the file info is not refreshed, the opened count, the opened time and the new pages are left alone, and the current page and the furthest page read are not recorded.
		/// </summary>
		public bool IsPreview { get; set; }

		public float Rating => book.Rating;

		public void SetRating(float rating)
		{
			book.Rating = rating;
		}

		/// <summary>The Core navigator, once opened (for tests).</summary>
		public ComicBookNavigator Navigator => navigator;

		public string Title
		{
			get
			{
				try
				{
					string caption = book.Caption;
					if (!string.IsNullOrWhiteSpace(caption))
					{
						return caption;
					}
				}
				catch (Exception)
				{
				}
				return string.IsNullOrEmpty(book.FilePath) ? "Untitled" : Path.GetFileNameWithoutExtension(book.FilePath);
			}
		}

		public IPageDecoder Decoder => decoder ?? throw new InvalidOperationException("The book is not open.");

		public int PageCount => pageCount;

		public int ResumePage => resumePage;

		public async Task OpenAsync(CancellationToken cancellationToken)
		{
			if (navigator != null)
			{
				throw new InvalidOperationException("The book is already open.");
			}
			if (!book.IsLinked)
			{
				throw new FileNotFoundException("The book has no file.");
			}
			var opened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			EventHandler onOpened = (sender, e) => opened.TrySetResult(true);
			EventHandler onError = (sender, e) => opened.TrySetException(new InvalidDataException("The book could not be opened: " + Path.GetFileName(book.FilePath ?? string.Empty) + " (the file is missing, damaged or not a comic format)."));
			ComicBookNavigator created = null;
			try
			{
				await Task.Run(() =>
				{
					cancellationToken.ThrowIfCancellationRequested();
					try
					{
						// As NavigatorManager.OpenComic does. Failures here (a locked file, a bad ComicInfo) do not stop the book from opening.
						if (!IsPreview)
						{
							book.RefreshInfoFromFile();
						}
					}
					catch (Exception)
					{
					}
					created = book.CreateNavigator();
					if (created == null)
					{
						throw new InvalidDataException("The book could not be opened: " + Path.GetFileName(book.FilePath ?? string.Empty) + ".");
					}
					created.Opened += onOpened;
					created.ErrorOpening += onError;
					// The start page is applied by this class once the index is complete: the navigator's own initial-page argument is
					// applied from its parser thread when that page's index entry arrives, and is assigned after Open returns.
					created.Open(async: true, 0);
				}, cancellationToken).ConfigureAwait(false);
				using (cancellationToken.Register(() => opened.TrySetCanceled(cancellationToken)))
				{
					await opened.Task.ConfigureAwait(false);
				}
			}
			catch (Exception)
			{
				if (created != null)
				{
					created.Opened -= onOpened;
					created.ErrorOpening -= onError;
					created.Dispose();
				}
				throw;
			}
			created.Opened -= onOpened;
			created.ErrorOpening -= onError;
			if (disposed)
			{
				created.Dispose();
				throw new ObjectDisposedException(nameof(CoreReadingSession));
			}
			navigator = created;
			decoder = new CorePageDecoder(navigator, pool, maxPixels);
			pageCount = navigator.ProviderPageCount;
			resumePage = IsPreview ? 0 : Math.Max(0, Math.Min(book.CurrentPage, pageCount - 1));

			if (IsPreview)
			{
				return;
			}
			if (IncreaseOpenedCount)
			{
				book.OpenedCount++;
			}
			if (ComicBookNavigator.TrackCurrentPage)
			{
				book.OpenedTime = DateTime.Now;
				book.NewPages = 0;
			}
			navigator.CurrentPage = resumePage;
		}

		public void ReportPage(int page)
		{
			ComicBookNavigator nav = navigator;
			if (nav == null || disposed || IsPreview)
			{
				return;
			}
			try
			{
				nav.CurrentPage = page;
			}
			catch (ObjectDisposedException)
			{
			}
		}

		public PageTraits GetPageTraits(int page)
		{
			try
			{
				ComicPageInfo info = book.GetPage(page);
				return new PageTraits(info.IsSingleRightPageType, info.IsSinglePageType, info.PagePosition, info.ImageWidth, info.ImageHeight);
			}
			catch (Exception)
			{
				return PageTraits.Default(page);
			}
		}

		/// <summary>The manga setting of the book as <c>ComicBook.CreateNavigator</c> turns it into the navigator's flag: only "Yes and right to left" reads from the right.</summary>
		public bool? RightToLeft
		{
			get
			{
				switch (navigator?.RightToLeftReading ?? YesNo.Unknown)
				{
					case YesNo.Yes:
						return true;
					case YesNo.No:
						return false;
					default:
						return null;
				}
			}
		}

		public void ReportFurthest(int page)
		{
			if (navigator == null || disposed || IsPreview || !ComicBookNavigator.TrackCurrentPage)
			{
				return;
			}
			if (page > book.LastPageRead && page < pageCount)
			{
				book.LastPageRead = page;
			}
		}

		/// <summary>
		/// A book of one page cannot be marked as read by reaching its last page (<c>ReadPercentage</c> is 0 while <c>LastPageRead</c> is
		/// 0). The Core's own <c>Navigate</c> sets the current page one past the end in that case; so does this.
		/// </summary>
		public void ReportEndReached()
		{
			ComicBookNavigator nav = navigator;
			if (nav == null || disposed || IsPreview || pageCount != 1)
			{
				return;
			}
			try
			{
				nav.CurrentPage = 1;
			}
			catch (ObjectDisposedException)
			{
			}
		}

		public void Dispose()
		{
			if (disposed)
			{
				return;
			}
			disposed = true;
			ComicBookNavigator nav = navigator;
			navigator = null;
			if (nav != null)
			{
				try
				{
					nav.Dispose(); // the provider is disposed on a background thread
				}
				catch (Exception)
				{
				}
			}
		}
	}
}
