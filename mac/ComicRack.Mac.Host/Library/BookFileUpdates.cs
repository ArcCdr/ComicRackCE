using System;
using System.Collections.Generic;
using System.Linq;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;

namespace ComicRack.Mac.Host.Library
{
	/// <summary>
	/// Writing the information of the library into the book files (P5.9, ledger MF.07 and BC.16): what <c>MainForm</c> and <c>ComicBrowserControl</c> do around the
	/// Core's file update queue (<see cref="QueueManager.AddBookToFileUpdate(ComicBook, bool)"/>), with no UI.
	/// <list type="bullet">
	/// <item><b>Marking</b> (<c>MainForm.WatchedBookHasChanged</c>): a change of a book of the library (or of a temporary book) that comes from the file's information sets
	/// <c>ComicInfoIsDirty</c> (a change of the extra library information sets <c>ComicBookIsDirty</c>) and queues the book, unless it is open in a reader (it is queued when
	/// the reader closes, <see cref="BookClosed"/>). The queue itself writes only when "Allow writing of Book info into files" (<c>UpdateComicFiles</c>) is on and, without
	/// a forced write, "Book files are updated automatically" (<c>AutoUpdateComicsFiles</c>) too; the flags are set whatever the settings say, as in the original.</item>
	/// <item><b>Update Book Files</b> (<see cref="UpdateBooks"/>, <c>ComicBrowserControl.UpdateFiles</c>) and <b>Update all Book Files</b> (<see cref="UpdateAll"/>,
	/// <c>MainForm.UpdateComics</c>): every book with a file is queued with <c>alwaysWrite</c>, so the update happens at once whatever the automatic setting says (the
	/// writing setting still has to be on).</item>
	/// <item><b>Start</b> (<c>MainForm.OnLoad</c>): every book of the library is offered to the queue once, so dirty books are written after a start with the automatic
	/// update on.</item>
	/// </list>
	/// The state of the queue (<see cref="IsWriting"/>, <see cref="PendingCount"/>) and its failures (<see cref="Errors"/>: the file name and the message, the list the
	/// Errors dialog of P5.26 shows) are the Core's own.
	/// </summary>
	public sealed class BookFileUpdates : IDisposable
	{
		private readonly MacHost host;
		private readonly Func<ComicBook, bool> isOpen;
		private bool started;
		private bool disposed;

		/// <param name="isOpen">Tells whether a book is open in a reader: such a book is queued when its reader closes (<c>books.IsOpen</c>).</param>
		public BookFileUpdates(MacHost host, Func<ComicBook, bool> isOpen = null)
		{
			this.host = host ?? throw new ArgumentNullException(nameof(host));
			this.isOpen = isOpen ?? (book => false);
		}

		private QueueManager Queue => host.QueueManager;

		/// <summary>True after <see cref="Start"/> until <see cref="Dispose"/>.</summary>
		public bool IsStarted => started;

		/// <summary>Starts watching the books (<c>OnLoad</c> subscribes <c>Database.BookChanged</c> and <c>TemporaryBookChanged</c>).</summary>
		public void Start()
		{
			if (started || disposed)
			{
				return;
			}
			started = true;
			host.DatabaseManager.Database.BookChanged += OnBookChanged;
			host.DatabaseManager.BookFactory.TemporaryBookChanged += OnBookChanged;
		}

		/// <summary>
		/// <c>Program.Database.Books.ForEach(Program.QueueManager.AddBookToFileUpdate)</c> of <c>OnLoad</c>: offers every book of the library to the queue (a book that is not
		/// dirty is ignored). Returns the number of books looked at. Meant for a worker thread.
		/// </summary>
		public int QueueDirtyBooks()
		{
			ComicBook[] books = host.DatabaseManager.Database.Books.ToArray();
			foreach (ComicBook book in books)
			{
				if (disposed)
				{
					break;
				}
				Queue.AddBookToFileUpdate(book);
			}
			return books.Length;
		}

		/// <summary><c>MainForm.WatchedBookHasChanged</c>.</summary>
		private void OnBookChanged(object sender, ContainerBookChangedEventArgs e)
		{
			if (disposed)
			{
				return;
			}
			ComicBook book = e.Book;
			if ((e.IsComicInfo || e.IsComicBook) && book.EditMode.IsLocalComic() && book.FileInfoRetrieved)
			{
				if (e.IsComicInfo)
				{
					book.ComicInfoIsDirty = true;
				}
				if (e.IsComicBook && !book.IsDynamicSource)
				{
					book.ComicBookIsDirty = true;
				}
				if (!isOpen(book))
				{
					Queue.AddBookToFileUpdate(book);
				}
			}
		}

		/// <summary><c>MainForm.OnBookClosed</c>: a reader closed, so the book that was kept back while it was open is offered to the queue.</summary>
		public void BookClosed(ComicBook book)
		{
			if (book != null && !disposed)
			{
				Queue.AddBookToFileUpdate(book);
			}
		}

		/// <summary><c>ComicBrowserControl.UpdateFiles</c>: queues the books that have a file with <c>alwaysWrite</c>. Returns how many were offered.</summary>
		public int UpdateBooks(IEnumerable<ComicBook> books)
		{
			int count = 0;
			foreach (ComicBook book in books.Where(b => b != null && b.IsLinked).ToArray())
			{
				Queue.AddBookToFileUpdate(book, alwaysWrite: true);
				count++;
			}
			return count;
		}

		/// <summary><c>MainForm.UpdateComics</c>: queues every book of the library and every temporary book with <c>alwaysWrite</c>. Returns how many were offered.</summary>
		public int UpdateAll()
		{
			ComicBook[] books = host.DatabaseManager.Database.Books.Concat(host.DatabaseManager.BookFactory.TemporaryBooks).ToArray();
			foreach (ComicBook book in books)
			{
				Queue.AddBookToFileUpdate(book, alwaysWrite: true);
			}
			return books.Length;
		}

		/// <summary>True while the queue writes (<c>QueueManager.IsInComicFileUpdate</c>, the icon of <c>tsWriteInfoActivity</c>).</summary>
		public bool IsWriting => Queue.IsInComicFileUpdate;

		/// <summary>The books waiting in the write queue.</summary>
		public int PendingCount => Queue.WriteComicBookInfoFileQueue.Count;

		/// <summary>The failures of the writes, newest last: the file name and the message (<c>QueueManager.UpdateErrors</c>).</summary>
		public IReadOnlyList<(string Item, string Message)> Errors => Queue.UpdateErrors.ToArray();

		/// <summary>How many writes failed (<c>UpdateErrors.Count</c>, part of the number the export icon of the original shows).</summary>
		public int ErrorCount => Queue.UpdateErrors.Count;

		public void Dispose()
		{
			if (disposed)
			{
				return;
			}
			disposed = true;
			if (started)
			{
				started = false;
				try
				{
					host.DatabaseManager.Database.BookChanged -= OnBookChanged;
					host.DatabaseManager.BookFactory.TemporaryBookChanged -= OnBookChanged;
				}
				catch (Exception)
				{
				}
			}
		}
	}
}
