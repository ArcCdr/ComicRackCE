using System;
using System.Collections.Generic;
using System.Threading;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;

namespace ComicRack.Mac.Host.Library
{
	/// <summary>One batch of what happened in the library since the last batch.</summary>
	public sealed class LibraryChange
	{
		public LibraryChange(IReadOnlyCollection<ComicBook> books, bool listChanged, bool readingOnly)
		{
			Books = books;
			ListChanged = listChanged;
			ReadingOnly = readingOnly;
		}

		/// <summary>The books that had a property changed (a book that was added or removed is not listed unless it also changed).</summary>
		public IReadOnlyCollection<ComicBook> Books { get; }

		/// <summary>True if books were added to or removed from the library.</summary>
		public bool ListChanged { get; }

		/// <summary>True if the only changes were the reading state of books (page, opened time and count): they do not change what a list is made of in a way a person waits for.</summary>
		public bool ReadingOnly { get; }
	}

	/// <summary>
	/// Collects the library's own change events (<c>ComicBookContainer.BookChanged</c> for a property of a book, <c>BookListChanged</c> for a book added or
	/// removed) and hands them on as one <see cref="LibraryChange"/> per quiet period. The events come from any thread (the scanner adds thousands of books
	/// a second; the reader writes the page at every turn), so the handlers only note what happened under a lock; the delivery runs once per
	/// <see cref="Delay"/> on the thread of the <see cref="ISaveScheduler"/> (a pool timer in the app, a hand released one in tests). Changes that are only
	/// the reading state wait <see cref="ReadingDelay"/> instead, so reading a book does not make the lists count again at every page; any other change that
	/// arrives meanwhile brings the delivery forward to <see cref="Delay"/>. <see cref="Flush"/> delivers at once what is pending (a reader was closed).
	/// </summary>
	public sealed class LibraryWatcher : IDisposable
	{
		/// <summary>Properties that only record where a person is in a book.</summary>
		private static readonly HashSet<string> readingProperties = new HashSet<string>(StringComparer.Ordinal)
		{
			"CurrentPage", "LastPageRead", "OpenedTime", "OpenedCount", "NewPages",
		};

		public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(1000);
		public static readonly TimeSpan DefaultReadingDelay = TimeSpan.FromMilliseconds(3000);

		private readonly ComicLibrary library;
		private readonly ISaveScheduler scheduler;
		private readonly Action<LibraryChange> deliver;
		private readonly object gate = new object();
		private readonly HashSet<ComicBook> books = new HashSet<ComicBook>();
		private bool listChanged;
		private bool anyOther;
		private bool anything;
		private bool slowTimer;
		private IDisposable pending;
		private bool disposed;

		/// <summary>How long a change waits before it is delivered.</summary>
		public TimeSpan Delay { get; set; } = DefaultDelay;

		/// <summary>How long a change of the reading state alone waits.</summary>
		public TimeSpan ReadingDelay { get; set; } = DefaultReadingDelay;

		/// <summary>How many batches were delivered.</summary>
		public int DeliveredCount => Volatile.Read(ref delivered);

		private int delivered;

		/// <param name="deliver">Called with each batch, on the scheduler's thread, never twice at the same time.</param>
		public LibraryWatcher(ComicLibrary library, ISaveScheduler scheduler, Action<LibraryChange> deliver)
		{
			this.library = library ?? throw new ArgumentNullException(nameof(library));
			this.scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
			this.deliver = deliver ?? throw new ArgumentNullException(nameof(deliver));
			library.BookChanged += OnBookChanged;
			library.BookListChanged += OnBookListChanged;
		}

		private void OnBookChanged(object sender, ContainerBookChangedEventArgs e)
		{
			bool reading = e.PropertyName != null && readingProperties.Contains(e.PropertyName);
			Note(e.Book, listChange: false, reading);
		}

		private void OnBookListChanged(object sender, EventArgs e)
		{
			Note(null, listChange: true, reading: false);
		}

		private void Note(ComicBook book, bool listChange, bool reading)
		{
			lock (gate)
			{
				if (disposed)
				{
					return;
				}
				if (book != null)
				{
					books.Add(book);
				}
				listChanged |= listChange;
				anyOther |= !reading;
				anything = true;
				if (pending == null)
				{
					slowTimer = reading;
					pending = scheduler.Schedule(reading ? ReadingDelay : Delay, Fire);
				}
				else if (slowTimer && !reading)
				{
					pending.Dispose(); // something that people wait for: do not keep it back behind the reading state
					slowTimer = false;
					pending = scheduler.Schedule(Delay, Fire);
				}
			}
		}

		/// <summary>Delivers what is pending now (on the calling thread).</summary>
		public void Flush()
		{
			Fire();
		}

		private readonly object deliveryGate = new object();

		private void Fire()
		{
			LibraryChange change;
			lock (gate)
			{
				if (disposed || !anything)
				{
					pending?.Dispose();
					pending = null;
					return;
				}
				pending?.Dispose();
				pending = null;
				change = new LibraryChange(new List<ComicBook>(books), listChanged, !anyOther);
				books.Clear();
				listChanged = false;
				anyOther = false;
				anything = false;
			}
			lock (deliveryGate)
			{
				Interlocked.Increment(ref delivered);
				deliver(change);
			}
		}

		public void Dispose()
		{
			lock (gate)
			{
				if (disposed)
				{
					return;
				}
				disposed = true;
				pending?.Dispose();
				pending = null;
				books.Clear();
			}
			library.BookChanged -= OnBookChanged;
			library.BookListChanged -= OnBookListChanged;
		}
	}
}
