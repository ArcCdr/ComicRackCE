using System;
using System.Collections.Generic;
using System.Linq;
using ComicRack.Mac.Host.Library;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;
using Xunit;

namespace ComicRack.Mac.Host.Tests
{
	/// <summary>The numbers behind the library tree counters (<see cref="ListStats"/>) and the collection of the library's change events (<see cref="LibraryWatcher"/>) for the live refresh (P5.2).</summary>
	public class LiveRefreshTests
	{
		private static readonly DateTime Now = new DateTime(2026, 10, 21, 12, 0, 0);

		private static ComicBook Book(int pages, int lastPageRead, double addedDaysAgo)
		{
			return new ComicBook
			{
				FilePath = "/nowhere/" + Guid.NewGuid().ToString("N") + ".cbz",
				PageCount = pages,
				LastPageRead = lastPageRead,
				AddedTime = Now.AddDays(-addedDaysAgo),
			};
		}

		[Fact]
		public void A_book_that_is_read_counts_in_the_total_only_and_the_others_are_new_or_unread_by_their_age()
		{
			ComicBook[] books =
			{
				Book(10, 9, 100), // read (last page reached)
				Book(10, 9, 1), // read and recent
				Book(10, 0, 100), // unread
				Book(10, 3, 100), // partly read: not read, so unread
				Book(10, 0, 3), // new
				Book(10, 0, 13.9), // new (within 14 days)
				Book(10, 0, 14.1), // unread (older than 14 days)
			};
			ListStats stats = ListStats.Of(books, Now, 14);
			Assert.Equal(new ListStats(7, 3, 2), stats);
			Assert.Equal(7, stats.Total);
			Assert.Equal(3, stats.Unread);
			Assert.Equal(2, stats.New);
			Assert.Equal(ListStats.Empty, ListStats.Of(Array.Empty<ComicBook>(), Now, 14));
		}

		[Fact]
		public void Reading_a_book_moves_it_out_of_the_new_and_unread_counts()
		{
			ComicBook book = Book(10, 0, 2);
			Assert.Equal(new ListStats(1, 0, 1), ListStats.Of(new[] { book }, Now, 14));
			book.LastPageRead = 9;
			Assert.Equal(new ListStats(1, 0, 0), ListStats.Of(new[] { book }, Now, 14));
		}

		[Fact]
		public void The_numbers_are_the_ones_the_core_keeps_for_a_list()
		{
			using (var temp = new TempFolder())
			using (var host = MacHost.Open(new MacHostOptions { Paths = temp.Paths }))
			{
				ComicDatabase db = host.DatabaseManager.Database;
				DateTime now = DateTime.Now;
				var books = new List<ComicBook>
				{
					new ComicBook { FilePath = "/n/a.cbz", PageCount = 10, LastPageRead = 9, AddedTime = now.AddDays(-40) },
					new ComicBook { FilePath = "/n/b.cbz", PageCount = 10, AddedTime = now.AddDays(-40) },
					new ComicBook { FilePath = "/n/c.cbz", PageCount = 10, AddedTime = now.AddDays(-40) },
					new ComicBook { FilePath = "/n/d.cbz", PageCount = 10, AddedTime = now.AddDays(-2) },
					new ComicBook { FilePath = "/n/e.cbz", PageCount = 10, LastPageRead = 4, AddedTime = now.AddDays(-1) },
				};
				foreach (ComicBook b in books)
				{
					db.Books.Add(b);
				}
				ComicLibraryListItem library = db.ComicLists.GetItems<ComicLibraryListItem>().Single();
				ComicBook[] shown = library.GetBooks().ToArray();
				ListStats mine = ListStats.Of(shown);
				Assert.Equal(5, shown.Length);
				Assert.Equal(new ListStats(library.BookCount, library.UnreadBookCount, library.NewBookCount), mine);
				Assert.Equal(new ListStats(5, 2, 2), mine);
			}
		}

		[Fact]
		public void The_counter_settings_start_as_in_the_original_and_survive_a_save()
		{
			var fresh = new AppPreferences();
			Assert.True(fresh.DisplayLibraryGauges);
			Assert.Equal(LibraryGauges.New | LibraryGauges.Unread | LibraryGauges.Total | LibraryGauges.Numeric, fresh.LibraryGaugesFormat);
			Assert.Equal(0x1007, (int)LibraryGauges.Default);
			using (var temp = new TempFolder())
			{
				string file = System.IO.Path.Combine(temp.Path, "Settings.xml");
				SettingsStore store = SettingsStore.Open(file);
				store.Current.DisplayLibraryGauges = false;
				store.Current.LibraryGaugesFormat = LibraryGauges.Total | LibraryGauges.Unread;
				store.Dispose(); // flushes
				AppPreferences loaded = SettingsStore.Load(file, out string note);
				Assert.Null(note);
				Assert.False(loaded.DisplayLibraryGauges);
				Assert.Equal(LibraryGauges.Total | LibraryGauges.Unread, loaded.LibraryGaugesFormat);
				// the default is not written
				store = SettingsStore.Open(file);
				store.Current.DisplayLibraryGauges = true;
				store.Current.LibraryGaugesFormat = LibraryGauges.Default;
				store.Dispose();
				string text = System.IO.File.ReadAllText(file);
				Assert.DoesNotContain("LibraryGaugesFormat", text);
				Assert.DoesNotContain("DisplayLibraryGauges", text);
			}
		}

		// ---- LibraryWatcher

		/// <summary>Records what is scheduled; the test runs the newest live action by hand.</summary>
		private sealed class RecordingScheduler : ISaveScheduler
		{
			public List<TimeSpan> Delays { get; } = new List<TimeSpan>();

			private readonly List<(Action action, bool[] disposed)> live = new List<(Action, bool[])>();

			public int Pending => live.Count(l => !l.disposed[0]);

			public IDisposable Schedule(TimeSpan delay, Action action)
			{
				Delays.Add(delay);
				var flag = new bool[1];
				live.Add((action, flag));
				return new Handle(flag);
			}

			public void RunPending()
			{
				foreach ((Action action, bool[] disposed) in live.ToArray())
				{
					if (!disposed[0])
					{
						disposed[0] = true;
						action();
					}
				}
			}

			private sealed class Handle : IDisposable
			{
				private readonly bool[] flag;

				public Handle(bool[] flag) => this.flag = flag;

				public void Dispose() => flag[0] = true;
			}
		}

		private static ComicLibrary NewLibrary(out ComicBook[] books)
		{
			var library = new ComicLibrary();
			books = new[] { Book(10, 0, 100), Book(10, 0, 100), Book(10, 0, 100) };
			foreach (ComicBook b in books)
			{
				library.Books.Add(b);
			}
			return library;
		}

		[Fact]
		public void Many_changes_are_one_batch_after_the_delay()
		{
			ComicLibrary library = NewLibrary(out ComicBook[] books);
			var scheduler = new RecordingScheduler();
			var batches = new List<LibraryChange>();
			using (new LibraryWatcher(library, scheduler, batches.Add))
			{
				books[0].Rating = 3;
				books[1].Rating = 4;
				books[0].Rating = 5;
				Assert.Empty(batches);
				Assert.Equal(1, scheduler.Pending);
				Assert.Equal(LibraryWatcher.DefaultDelay, scheduler.Delays.Single());
				scheduler.RunPending();
				LibraryChange batch = Assert.Single(batches);
				Assert.Equal(new[] { books[0], books[1] }.OrderBy(b => b.Id), batch.Books.OrderBy(b => b.Id));
				Assert.False(batch.ListChanged);
				Assert.False(batch.ReadingOnly);
				// the next change starts a new period
				books[2].Rating = 2;
				Assert.Equal(1, scheduler.Pending);
				scheduler.RunPending();
				Assert.Equal(2, batches.Count);
				Assert.Equal(new[] { books[2] }, batches[1].Books);
			}
		}

		[Fact]
		public void Books_added_to_the_library_are_a_list_change()
		{
			ComicLibrary library = NewLibrary(out _);
			var scheduler = new RecordingScheduler();
			var batches = new List<LibraryChange>();
			using (new LibraryWatcher(library, scheduler, batches.Add))
			{
				for (int i = 0; i < 500; i++)
				{
					library.Books.Add(Book(10, 0, 1));
				}
				Assert.Equal(1, scheduler.Pending); // thousands of events, one timer
				scheduler.RunPending();
				LibraryChange batch = Assert.Single(batches);
				Assert.True(batch.ListChanged);
				Assert.False(batch.ReadingOnly);
			}
		}

		[Fact]
		public void Reading_alone_waits_longer_and_anything_else_brings_the_delivery_forward()
		{
			ComicLibrary library = NewLibrary(out ComicBook[] books);
			var scheduler = new RecordingScheduler();
			var batches = new List<LibraryChange>();
			using (new LibraryWatcher(library, scheduler, batches.Add))
			{
				books[0].CurrentPage = 3; // CurrentPage and LastPageRead
				books[0].CurrentPage = 4;
				Assert.Equal(new[] { LibraryWatcher.DefaultReadingDelay }, scheduler.Delays);
				scheduler.RunPending();
				Assert.True(batches.Single().ReadingOnly);
				Assert.Equal(new[] { books[0] }, batches[0].Books);

				books[1].CurrentPage = 2;
				Assert.Equal(LibraryWatcher.DefaultReadingDelay, scheduler.Delays.Last());
				books[2].Rating = 5; // a rating is not reading
				Assert.Equal(LibraryWatcher.DefaultDelay, scheduler.Delays.Last());
				Assert.Equal(1, scheduler.Pending); // the slow timer was replaced
				scheduler.RunPending();
				Assert.False(batches[1].ReadingOnly);
				Assert.Equal(2, batches[1].Books.Count);
			}
		}

		[Fact]
		public void Flush_delivers_at_once_and_a_disposed_watcher_hears_nothing()
		{
			ComicLibrary library = NewLibrary(out ComicBook[] books);
			var scheduler = new RecordingScheduler();
			var batches = new List<LibraryChange>();
			var watcher = new LibraryWatcher(library, scheduler, batches.Add);
			books[0].CurrentPage = 5;
			watcher.Flush();
			Assert.Single(batches);
			Assert.Equal(0, scheduler.Pending); // the timer was cancelled
			watcher.Flush(); // nothing pending
			Assert.Single(batches);
			watcher.Dispose();
			books[1].Rating = 3;
			library.Books.Add(Book(10, 0, 1));
			Assert.Equal(0, scheduler.Pending);
			scheduler.RunPending();
			Assert.Single(batches);
		}
	}
}
