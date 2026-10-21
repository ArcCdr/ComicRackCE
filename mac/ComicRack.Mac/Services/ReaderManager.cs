using System;
using System.Collections.Generic;
using System.Linq;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Display;
using ComicRack.Mac.Diagnostics;
using ComicRack.Mac.Host;
using ComicRack.Mac.Reader;
using ComicRack.Mac.ViewModels;

namespace ComicRack.Mac.Services
{
	/// <summary>What the library window asks of the reader: open a book, and close every reader before the library is replaced or the app quits.</summary>
	public interface IReaderLauncher
	{
		/// <summary>Opens <paramref name="book"/> in a book tab (brings the tab forward if the book is already open).</summary>
		void Open(ComicBook book);

		/// <summary>Closes every book tab, which records the page each one was on. Call before the host is closed or replaced.</summary>
		void CloseAll();

		/// <summary>Books that are open.</summary>
		int OpenCount { get; }

		/// <summary>True if <paramref name="book"/> is open in a tab (the browser draws the "open" state icon on its cover).</summary>
		bool IsOpen(ComicBook book);

		/// <summary>Raised on the UI thread when a book was opened or closed.</summary>
		event EventHandler OpenBooksChanged;
	}

	/// <summary>How a book is opened (<c>OpenComicOptions</c> of the original).</summary>
	[Flags]
	public enum OpenBookOptions
	{
		None = 0,

		/// <summary><c>OpenInNewSlot</c>: the book gets a tab of its own; without it the book takes the place of the book in the current tab.</summary>
		InNewTab = 1,

		/// <summary><c>AppendNewSlots</c>: a new tab is added at the right end (a restored session) instead of at the left end.</summary>
		Append = 2,

		/// <summary><c>NoIncreaseOpenedCount</c>: the library does not count this opening (a restored session).</summary>
		NoIncreaseOpenedCount = 4,
	}

	/// <summary>
	/// One tab of the reader area (<c>ComicBookNavigator</c> in <c>NavigatorManager.Slots</c>): a book that is open with its <see cref="ReaderViewModel"/>, or an empty
	/// slot (the New Tab button) that shows nothing until a book is opened into it.
	/// </summary>
	public sealed class ReaderSlot
	{
		internal ReaderSlot(ComicBook book, ReaderViewModel model, Reader.CorePageThumbnails thumbnails)
		{
			Book = book;
			Model = model;
			Thumbnails = thumbnails;
		}

		/// <summary>The book of the tab, or null for an empty slot.</summary>
		public ComicBook Book { get; }

		/// <summary>The reader of the book, or null for an empty slot.</summary>
		public ReaderViewModel Model { get; }

		/// <summary>The page pictures of the navigation overlay for this book, or null (no thumbnail service, or an empty slot).</summary>
		public Reader.CorePageThumbnails Thumbnails { get; }

		public bool IsEmpty => Model == null;

		internal Action Cleanup { get; set; }

		/// <summary>The text of the tab (<c>GetSlotCaption</c>): the caption of the book, else "None".</summary>
		public string Caption => Model != null ? Model.BookSegmentText : UiText.Default("None", "None");
	}

	/// <summary>
	/// The open books of the main window (<c>NavigatorManager</c> of the original, <see cref="ReaderSlot"/> = its slots): a list of tabs, one of them current, whose
	/// book the reader area shows. Opening a book follows <c>NavigatorManager.Open</c>: a book that is open already is brought forward (its page does not
	/// change and it is not counted again); a new book takes the place of the book in the current tab, unless it is opened in a new tab, which is added at the
	/// left end (the original inserts at index 0) or, for a restored session, at the right end; the New Tab button adds an empty slot at the right end. Closing the
	/// current tab shows the next one (the previous one when it was the last).
	///
	/// <para>A reader uses the page cache of the host's <c>ImagePool</c>, so every tab has to be closed before the host is: <see cref="CloseAll"/> is called before
	/// an import replaces the library and before the app quits. The fit mode, the page layout and the reading direction last chosen are the start values of the next
	/// reader, and with the other reader options they are kept in the saved preferences (P5.1).</para>
	/// </summary>
	public sealed class ReaderManager : IReaderLauncher
	{
		private readonly HostService hostService;
		private readonly List<ReaderSlot> slots = new List<ReaderSlot>();
		private readonly AppPreferences preferences;
		private ImageFitMode fit = ImageFitMode.Fit;
		private PageLayoutMode layoutMode = PageLayoutMode.Single;
		private int current = -1;

		/// <param name="preferences">The saved preferences the readers start from and write back to; null for none (every reader starts with the defaults).</param>
		public ReaderManager(HostService hostService, AppPreferences preferences = null)
		{
			this.hostService = hostService ?? throw new ArgumentNullException(nameof(hostService));
			this.preferences = preferences;
			if (preferences != null)
			{
				fit = ReaderOptions.ParseFit(preferences.ReaderFit);
				layoutMode = ReaderOptions.ParseMode(preferences.ReaderLayout);
				// Read > Track current Page: the Core's navigators read the switch from a static field (ComicBookNavigator.TrackCurrentPage)
				ComicBookNavigator.TrackCurrentPage = preferences.TrackCurrentPage;
				preferences.PropertyChanged += (sender, e) =>
				{
					if (e.PropertyName == nameof(AppPreferences.TrackCurrentPage))
					{
						ComicBookNavigator.TrackCurrentPage = preferences.TrackCurrentPage;
					}
				};
			}
		}

		// ---- the tabs

		/// <summary>The tabs, left to right.</summary>
		public IReadOnlyList<ReaderSlot> Slots => slots;

		/// <summary>The index of the current tab, or -1 when there is none (<c>CurrentSlot</c>). Setting it shows that tab.</summary>
		public int CurrentSlot
		{
			get => current;
			set => Select(value);
		}

		/// <summary>The current tab, or null.</summary>
		public ReaderSlot Current => current >= 0 && current < slots.Count ? slots[current] : null;

		/// <summary>The reader of the current tab, or null when there is no tab or the tab is empty.</summary>
		public ReaderViewModel CurrentModel => Current?.Model;

		/// <summary>The reader the performance overlay reports on: the current one.</summary>
		public ReaderViewModel FrontReader => CurrentModel;

		/// <summary>Books that are open (the empty slots do not count, <c>OpenCount</c>).</summary>
		public int OpenCount => slots.Count(s => !s.IsEmpty);

		public bool IsOpen(ComicBook book) => book != null && slots.Any(s => ReferenceEquals(s.Book, book));

		/// <summary>The files of the open books that are local, in tab order (<c>OpenFiles</c>): what the next start reopens.</summary>
		public IReadOnlyList<string> OpenFiles => slots
			.Where(s => !s.IsEmpty && !string.IsNullOrEmpty(s.Book.FilePath) && s.Book.EditMode.IsLocalComic())
			.Select(s => s.Book.FilePath)
			.ToArray();

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

		/// <summary>The tabs changed (one opened, closed, moved or replaced).</summary>
		public event EventHandler SlotsChanged;

		/// <summary>The current tab changed, or the book in it was replaced (<c>CurrentSlotChanged</c>).</summary>
		public event EventHandler CurrentSlotChanged;

		/// <summary>The text of a tab changed (the caption of its book was edited, <c>OpenComicsChanged</c>).</summary>
		public event EventHandler CaptionsChanged;

		/// <summary>A book was opened or closed (the browser's "open" state icons follow).</summary>
		public event EventHandler OpenBooksChanged;

		/// <summary>
		/// Raised after a book was closed (its page is recorded in the book): the place of <c>MainForm.OnBookClosing</c>, which asks for a quick review of a
		/// book that was read to the end (<c>AutoShowQuickReview</c>). Not raised for an empty tab.
		/// </summary>
		public event EventHandler<ComicBook> BookClosed;

		/// <summary>The reader asked for the browser panel to open or close (Esc, <c>ShowBrowser</c>).</summary>
		public event EventHandler ToggleBrowserRequested;

		/// <summary>The reader asked to move into its own window or back into the main window (D, <c>ToggleUndockReader</c>).</summary>
		public event EventHandler ToggleUndockRequested;

		// ---- opening

		/// <summary>Opens a book as the setting Open in new Tab says (<c>Settings.OpenInNewTab</c>, off by default).</summary>
		public void Open(ComicBook book)
		{
			Open(book, preferences != null && preferences.OpenInNewTab ? OpenBookOptions.InNewTab : OpenBookOptions.None);
		}

		/// <summary>
		/// <c>NavigatorManager.Open</c>: opens <paramref name="book"/> in a tab and shows it. Returns the tab, or null if the book has no file or the library engine is not
		/// running. A book that is open already is shown without being opened again.
		/// </summary>
		public ReaderSlot Open(ComicBook book, OpenBookOptions options)
		{
			if (book == null || !book.IsLinked)
			{
				return null;
			}
			int existing = slots.FindIndex(s => ReferenceEquals(s.Book, book));
			if (existing >= 0)
			{
				Select(existing);
				return slots[existing];
			}
			MacHost host = hostService.Host;
			if (host == null)
			{
				AppLog.Info("A reader was requested while the library engine is not running.");
				return null;
			}
			ReaderSlot slot = CreateSlot(book, host, options);
			Place(slot, options);
			_ = OpenAsync(slot.Model);
			return slot;
		}

		/// <summary>
		/// Puts a reader that the caller made into a tab, as <see cref="Open(ComicBook, OpenBookOptions)"/> does with the reader it makes (tests, which read a book of
		/// generated pages). The reader is opened by the caller; the tab owns it from now on and disposes it when it is closed.
		/// </summary>
		public ReaderSlot Adopt(ReaderViewModel model, ComicBook book, OpenBookOptions options = OpenBookOptions.None)
		{
			if (model == null)
			{
				throw new ArgumentNullException(nameof(model));
			}
			if (book == null)
			{
				throw new ArgumentNullException(nameof(book));
			}
			ReaderSlot slot = new ReaderSlot(book, model, null) { Cleanup = HookModel(model, book) };
			Place(slot, options);
			return slot;
		}

		// A book that is not opened in a new tab takes the place of the book of the current tab, empty or not (flag2 and the replacement in NavigatorManager.Open);
		// a new tab is inserted at the left end, or added at the right end for a restored session.
		private void Place(ReaderSlot slot, OpenBookOptions options)
		{
			ReaderSlot replaced = null;
			if ((options & OpenBookOptions.InNewTab) == 0 && current >= 0 && current < slots.Count)
			{
				replaced = slots[current];
				slots[current] = slot;
			}
			else if ((options & OpenBookOptions.Append) == 0)
			{
				slots.Insert(0, slot);
				current = 0;
			}
			else
			{
				slots.Add(slot);
				current = slots.Count - 1;
			}
			AppLog.Info("Reader opened: " + slot.Book.FilePath);
			Raise(SlotsChanged);
			Raise(CurrentSlotChanged);
			Raise(OpenBooksChanged);
			if (replaced != null)
			{
				DisposeSlot(replaced);
			}
		}

		/// <summary>Opens the files of a restored session in tab order, each in a tab of its own at the right end, without counting an opening.</summary>
		public void Restore(IEnumerable<string> files)
		{
			MacHost host = hostService.Host;
			if (host == null || files == null)
			{
				return;
			}
			foreach (string file in files)
			{
				if (string.IsNullOrEmpty(file) || !System.IO.File.Exists(file))
				{
					continue;
				}
				try
				{
					ComicBook book = host.DatabaseManager.BookFactory.Create(file, CreateBookOption.DoNotAdd, RefreshInfoOptions.GetPageCount);
					Open(book, OpenBookOptions.InNewTab | OpenBookOptions.Append | OpenBookOptions.NoIncreaseOpenedCount);
				}
				catch (Exception ex)
				{
					AppLog.Error("The book of the last session could not be reopened: " + file, ex);
				}
			}
		}

		private ReaderSlot CreateSlot(ComicBook book, MacHost host, OpenBookOptions options)
		{
			var session = new CoreReadingSession(book, host.ImagePool) { IncreaseOpenedCount = (options & OpenBookOptions.NoIncreaseOpenedCount) == 0 };
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
			var thumbnails = hostService.Thumbnails != null ? new Reader.CorePageThumbnails(book, hostService.Thumbnails) : null;
			return new ReaderSlot(book, model, thumbnails) { Cleanup = HookModel(model, book) };
		}

		// The requests of a reader go to the manager; the caption of the tab follows the book as it is edited. Returns what undoes it.
		private Action HookModel(ReaderViewModel model, ComicBook book)
		{
			model.PropertyChanged += (sender, e) =>
			{
				if (e.PropertyName == nameof(ReaderViewModel.Title))
				{
					Raise(CaptionsChanged);
				}
			};
			model.CloseRequested += (sender, e) => Close(slots.FindIndex(s => ReferenceEquals(s.Model, model)));
			model.NextTabRequested += (sender, e) => Next();
			model.PreviousTabRequested += (sender, e) => Previous();
			model.ToggleBrowserRequested += (sender, e) => Raise(ToggleBrowserRequested);
			model.ToggleUndockRequested += (sender, e) => Raise(ToggleUndockRequested);
			// The Core raises BookChanged on any thread, and for every page turn too.
			EventHandler<BookChangedEventArgs> changed = (sender, e) =>
			{
				if (e.PropertyName != nameof(ComicBook.CurrentPage) && e.PropertyName != nameof(ComicBook.LastPageRead) && e.PropertyName != nameof(ComicBook.OpenedTime)
					&& e.PropertyName != nameof(ComicBook.OpenedCount) && e.PropertyName != nameof(ComicBook.NewPages))
				{
					UiDispatch.Post(model.RefreshTitle);
				}
			};
			book.BookChanged += changed;
			return () => book.BookChanged -= changed;
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

		/// <summary>New Tab (<c>AddSlot</c>): an empty slot at the right end, which becomes the current tab. The next book that is opened takes its place.</summary>
		public void AddSlot()
		{
			slots.Add(new ReaderSlot(null, null, null));
			current = slots.Count - 1;
			Raise(SlotsChanged);
			Raise(CurrentSlotChanged);
		}

		// ---- selecting

		/// <summary>Shows tab <paramref name="index"/> (clamped to -1 and the last one).</summary>
		public void Select(int index)
		{
			index = Math.Max(-1, Math.Min(slots.Count - 1, index));
			if (index == current)
			{
				return;
			}
			current = index;
			Raise(CurrentSlotChanged);
		}

		/// <summary>Next Tab (<c>NextSlot</c>): the tab to the right, the first one after the last.</summary>
		public void Next()
		{
			if (current >= 0)
			{
				Select(current + 1 >= slots.Count ? 0 : current + 1);
			}
		}

		/// <summary>Previous Tab (<c>PreviousSlot</c>): the tab to the left, the last one before the first.</summary>
		public void Previous()
		{
			if (current >= 0)
			{
				Select(current - 1 < 0 ? slots.Count - 1 : current - 1);
			}
		}

		/// <summary>Drags a tab to a new place (<c>MoveSlot</c>); the moved tab stays the current one.</summary>
		public void Move(int from, int to)
		{
			if (from < 0 || from >= slots.Count || to < 0 || to >= slots.Count || from == to)
			{
				return;
			}
			ReaderSlot keep = Current;
			ReaderSlot moved = slots[from];
			slots.RemoveAt(from);
			slots.Insert(to, moved);
			current = slots.IndexOf(keep);
			Raise(SlotsChanged);
			Raise(CaptionsChanged);
		}

		// ---- closing

		/// <summary>Close (<c>Close(int)</c>): closes tab <paramref name="index"/>. When it was the current one the tab to its right is shown, else the one to its left.</summary>
		public void Close(int index)
		{
			if (index < 0 || index >= slots.Count)
			{
				return;
			}
			ReaderSlot closing = slots[index];
			ReaderSlot before = Current;
			slots.RemoveAt(index);
			if (current == index)
			{
				current = slots.Count == 0 ? -1 : Math.Min(index, slots.Count - 1);
			}
			else if (current > index)
			{
				current--;
			}
			Raise(SlotsChanged);
			if (!ReferenceEquals(before, Current))
			{
				Raise(CurrentSlotChanged);
			}
			DisposeSlot(closing);
			if (!closing.IsEmpty)
			{
				Raise(OpenBooksChanged);
			}
		}

		/// <summary>Close (File, Cmd+W): the current tab.</summary>
		public void Close() => Close(current);

		/// <summary>Close All (<c>CloseAll</c>): every tab. Records the page each book was on.</summary>
		public void CloseAll()
		{
			if (slots.Count == 0)
			{
				return;
			}
			ReaderSlot[] closing = slots.ToArray();
			slots.Clear();
			current = -1;
			Raise(SlotsChanged);
			Raise(CurrentSlotChanged);
			foreach (ReaderSlot slot in closing)
			{
				DisposeSlot(slot);
			}
			Raise(OpenBooksChanged);
		}

		/// <summary>Close All But This (<c>CloseAllButCurrent</c>).</summary>
		public void CloseAllButCurrent()
		{
			ReaderSlot keep = Current;
			if (keep == null)
			{
				return;
			}
			ReaderSlot[] closing = slots.Where(s => !ReferenceEquals(s, keep)).ToArray();
			slots.Clear();
			slots.Add(keep);
			current = 0;
			Raise(SlotsChanged);
			foreach (ReaderSlot slot in closing)
			{
				DisposeSlot(slot);
			}
			if (closing.Any(s => !s.IsEmpty))
			{
				Raise(OpenBooksChanged);
			}
		}

		/// <summary>Close All to the Right (<c>CloseAllToTheRight</c>): the tabs after the current one.</summary>
		public void CloseAllToTheRight()
		{
			if (current < 0 || current >= slots.Count - 1)
			{
				return;
			}
			ReaderSlot[] closing = slots.Skip(current + 1).ToArray();
			slots.RemoveRange(current + 1, slots.Count - current - 1);
			Raise(SlotsChanged);
			foreach (ReaderSlot slot in closing)
			{
				DisposeSlot(slot);
			}
			if (closing.Any(s => !s.IsEmpty))
			{
				Raise(OpenBooksChanged);
			}
		}

		// The page is recorded in the book, the pages and the book are released: the order of the original's Close (OnBookClosing, remove, dispose).
		private void DisposeSlot(ReaderSlot slot)
		{
			if (slot.IsEmpty)
			{
				return;
			}
			ReaderViewModel model = slot.Model;
			slot.Cleanup?.Invoke();
			try
			{
				model.Dispose();
				slot.Thumbnails?.Dispose();
			}
			catch (Exception ex)
			{
				AppLog.Error("Closing the reader failed.", ex);
			}
			AppLog.Info("Reader closed: " + slot.Book.FilePath + " on page " + (model.CurrentPage + 1) + ".");
			BookClosed?.Invoke(this, slot.Book);
		}

		private void Raise(EventHandler handler) => handler?.Invoke(this, EventArgs.Empty);
	}
}
