using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ComicRack.Cli.Fixtures;
using ComicRack.Mac.Host.Library;
using ComicRack.Mac.Host.Platform;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;
using Xunit;

namespace ComicRack.Mac.Host.Tests
{
	/// <summary>
	/// P5.9 (ledger MF.07, BC.16): <see cref="BookFileUpdates"/> around the Core's file update queue, on real CBZ books and the bundled 7zz. A changed book is marked dirty
	/// whatever the settings say; it is written when the automatic update is on, or when Update Book Files / Update all Book Files queue it; nothing is written while "Allow
	/// writing of Book info into files" is off; a book that is open waits for its reader; a failed write is listed. The tests return quietly on a machine where
	/// mac/native/fetch-7zz.sh has not been run (as the other 7zz tests do). A write is observed by the book's dirty flag and the file read back through the Core, never by a pause.
	/// </summary>
	public class BookFileUpdatesTests
	{
		private sealed class Setup : IDisposable
		{
			public TempFolder Temp { get; } = new TempFolder();
			public MacHost Host { get; }
			public AppPreferences Preferences { get; }
			public BookFileUpdates Updates { get; }
			public string BooksFolder { get; }
			public HashSet<ComicBook> Open { get; } = new HashSet<ComicBook>();

			public Setup(bool writing, bool automatic, int books = 3, bool start = true)
			{
				BooksFolder = Path.Combine(Temp.Path, "books");
				var env = new FixtureEnvironment { FixturesRoot = Path.Combine(Temp.Path, "fixtures"), PrintDiskFree = false, SevenZip = null };
				string made = Path.Combine(env.FixturesRoot, "set");
				var output = new StringWriter();
				Assert.Equal(0, FixtureCommands.Run(new[] { "make", made, "--books", books.ToString(), "--pages", "3" }, output, output, env));
				Directory.CreateDirectory(BooksFolder);
				foreach (string file in Directory.GetFiles(made, "*.cbz"))
				{
					File.Copy(file, Path.Combine(BooksFolder, Path.GetFileName(file)));
				}
				Preferences = new AppPreferences { UpdateComicFiles = writing, AutoUpdateComicsFiles = automatic };
				Host = MacHost.Open(new MacHostOptions { Paths = Temp.Paths, Settings = new MacSettings(Preferences) });
				ScanResult scan = new FolderScanner(Host).ScanAsync(BooksFolder, null, CancellationToken.None).GetAwaiter().GetResult();
				Assert.Equal(ScanOutcome.Completed, scan.Outcome);
				Assert.Equal(books, Host.DatabaseManager.Database.Books.Count);
				Updates = new BookFileUpdates(Host, book => Open.Contains(book));
				if (start)
				{
					Updates.Start();
				}
			}

			public ComicBook[] Books => Host.DatabaseManager.Database.Books.OrderBy(b => b.FilePath, StringComparer.Ordinal).ToArray();

			/// <summary>What the file says now: a book made from it outside the library, read through the Core as `crcli info show` does.</summary>
			public ComicBook FromFile(ComicBook book)
			{
				ComicBook copy = Host.DatabaseManager.BookFactory.Create(book.FilePath, CreateBookOption.DoNotAdd, RefreshInfoOptions.GetPageCount);
				copy.RefreshInfoFromFile(RefreshInfoOptions.ForceRefresh | RefreshInfoOptions.GetPageCount);
				return copy;
			}

			public static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));

			public void Dispose()
			{
				Updates.Dispose();
				Host.Dispose();
				Temp.Dispose();
			}
		}

		private static bool NoSevenZip => ToolLocator.SevenZip == null;

		private static void WaitFor(Func<bool> condition, string what, int timeoutMs = 60000)
		{
			DateTime end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
			while (!condition())
			{
				if (DateTime.UtcNow > end)
				{
					throw new TimeoutException("Not reached: " + what);
				}
				Thread.Sleep(10);
			}
		}

		[Fact]
		public void A_changed_book_is_marked_and_waits_when_the_automatic_update_is_off_and_Update_Book_Files_writes_it()
		{
			if (NoSevenZip)
			{
				return;
			}
			using (var s = new Setup(writing: true, automatic: false))
			{
				ComicBook[] books = s.Books;
				ComicBook book = books[0];
				string before = Setup.Hash(book.FilePath);
				Assert.False(book.ComicInfoIsDirty);

				book.Series = "Written Series";
				book.Number = "7";
				Assert.True(book.ComicInfoIsDirty); // marked by the change, as MainForm.WatchedBookHasChanged does
				Assert.False(books[1].ComicInfoIsDirty);
				Assert.Equal(0, s.Updates.PendingCount);
				// nothing was queued (the automatic update is off), so the file is as it was; no write can be in flight to wait for
				Assert.False(s.Updates.IsWriting);
				Assert.Equal(before, Setup.Hash(book.FilePath));

				Assert.Equal(1, s.Updates.UpdateBooks(new[] { book }));
				WaitFor(() => !book.ComicInfoIsDirty && !s.Updates.IsWriting, "the book is written");
				Assert.NotEqual(before, Setup.Hash(book.FilePath));
				ComicBook read = s.FromFile(book);
				Assert.Equal("Written Series", read.Series);
				Assert.Equal("7", read.Number);
				Assert.Equal(0, s.Updates.ErrorCount);
			}
		}

		[Fact]
		public void The_automatic_update_writes_a_changed_book_without_a_command()
		{
			if (NoSevenZip)
			{
				return;
			}
			using (var s = new Setup(writing: true, automatic: true))
			{
				ComicBook book = s.Books[1];
				book.Publisher = "Auto Press";
				book.Title = "Auto Title";
				WaitFor(() => !book.ComicInfoIsDirty && !s.Updates.IsWriting && s.FromFile(book).Publisher == "Auto Press", "the automatic write");
				ComicBook read = s.FromFile(book);
				Assert.Equal("Auto Title", read.Title); // both changes are in the one write (the queue collects them in batches)
				Assert.Equal(0, s.Updates.ErrorCount);
			}
		}

		[Fact]
		public void A_book_that_is_open_in_a_reader_waits_until_the_reader_closes()
		{
			if (NoSevenZip)
			{
				return;
			}
			using (var s = new Setup(writing: true, automatic: true))
			{
				ComicBook book = s.Books[0];
				string before = Setup.Hash(book.FilePath);
				s.Open.Add(book);
				book.Series = "Open Book";
				Assert.True(book.ComicInfoIsDirty);
				Assert.Equal(0, s.Updates.PendingCount); // not queued while open
				Assert.False(s.Updates.IsWriting);
				Assert.Equal(before, Setup.Hash(book.FilePath));

				s.Open.Remove(book);
				s.Updates.BookClosed(book); // OnBookClosed
				WaitFor(() => !book.ComicInfoIsDirty && !s.Updates.IsWriting, "the write after the close");
				Assert.Equal("Open Book", s.FromFile(book).Series);
			}
		}

		[Fact]
		public void Update_all_writes_every_changed_book_and_leaves_the_others_alone()
		{
			if (NoSevenZip)
			{
				return;
			}
			using (var s = new Setup(writing: true, automatic: false))
			{
				ComicBook[] books = s.Books;
				string untouched = Setup.Hash(books[2].FilePath);
				books[0].Series = "All A";
				books[1].Series = "All B";
				Assert.Equal(3, s.Updates.UpdateAll()); // every book is offered; the queue takes the ones that are dirty
				WaitFor(() => !books[0].ComicInfoIsDirty && !books[1].ComicInfoIsDirty && !s.Updates.IsWriting, "both books are written");
				Assert.Equal("All A", s.FromFile(books[0]).Series);
				Assert.Equal("All B", s.FromFile(books[1]).Series);
				Assert.Equal(untouched, Setup.Hash(books[2].FilePath));
			}
		}

		[Fact]
		public void Nothing_is_written_while_writing_into_files_is_off_but_the_book_stays_marked()
		{
			if (NoSevenZip)
			{
				return;
			}
			using (var s = new Setup(writing: false, automatic: true))
			{
				ComicBook book = s.Books[0];
				string before = Setup.Hash(book.FilePath);
				book.Series = "Not written";
				Assert.True(book.ComicInfoIsDirty); // marked whatever the settings say
				Assert.Equal(1, s.Updates.UpdateBooks(new[] { book })); // offered, and ignored by the queue
				Assert.Equal(3, s.Updates.UpdateAll());
				Assert.Equal(0, s.Updates.PendingCount);
				Assert.False(s.Updates.IsWriting);
				Assert.True(book.ComicInfoIsDirty);
				Assert.Equal(before, Setup.Hash(book.FilePath));

				// the setting turned on later: the same command now writes (the queue reads the setting when it works)
				s.Preferences.UpdateComicFiles = true;
				s.Updates.UpdateBooks(new[] { book });
				WaitFor(() => !book.ComicInfoIsDirty && !s.Updates.IsWriting, "the write after the setting was turned on");
				Assert.Equal("Not written", s.FromFile(book).Series);
			}
		}

		[Fact]
		public void The_start_offers_books_that_were_dirty_before_to_the_queue()
		{
			if (NoSevenZip)
			{
				return;
			}
			using (var s = new Setup(writing: true, automatic: true, start: false))
			{
				ComicBook book = s.Books[0];
				book.Series = "From an earlier run";
				book.ComicInfoIsDirty = true; // what the library file holds for a book that was changed before the last quit
				Assert.False(s.Updates.IsWriting);
				s.Updates.Start();
				Assert.Equal(3, s.Updates.QueueDirtyBooks());
				WaitFor(() => !book.ComicInfoIsDirty && !s.Updates.IsWriting, "the write at the start");
				Assert.Equal("From an earlier run", s.FromFile(book).Series);
			}
		}

		[Fact]
		public void A_book_without_a_file_is_not_offered()
		{
			using (var s = new Setup(writing: true, automatic: false, books: 1))
			{
				var fileless = new ComicBook { Series = "No file" };
				Assert.Equal(0, s.Updates.UpdateBooks(new[] { fileless, null }));
				Assert.Equal(1, s.Updates.UpdateBooks(new[] { fileless, s.Books[0] }));
			}
		}

		[Fact]
		public void A_failed_write_is_listed_with_the_file_name_and_the_book_stays_marked()
		{
			if (NoSevenZip)
			{
				return;
			}
			using (var s = new Setup(writing: true, automatic: false, books: 1))
			{
				ComicBook book = s.Books[0];
				string folder = Path.GetDirectoryName(book.FilePath);
				string before = Setup.Hash(book.FilePath);
				book.Series = "Cannot be written";
				try
				{
					// 7zz writes a new archive next to the book, so a folder that cannot be written makes the update fail (exit code 2, errno 13)
					File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
					if (CanWriteIn(folder))
					{
						return; // running as an account that ignores the permission (root): nothing to test here
					}
					s.Updates.UpdateBooks(new[] { book });
					WaitFor(() => s.Updates.ErrorCount > 0 && !s.Updates.IsWriting, "the failure is listed");
				}
				finally
				{
					File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
				}
				// the Core raises the provider's error more than once for one failed run (three entries here); the list is its own, every entry names the book's file
				Assert.NotEmpty(s.Updates.Errors);
				Assert.All(s.Updates.Errors, error =>
				{
					Assert.Equal(Path.GetFileName(book.FilePath), error.Item);
					Assert.Contains("Permission denied", error.Message);
				});
				Assert.True(book.ComicInfoIsDirty); // still marked: the next update tries again
				Assert.Equal(before, Setup.Hash(book.FilePath));
			}
		}

		private static bool CanWriteIn(string folder)
		{
			string probe = Path.Combine(folder, ".probe-" + Guid.NewGuid().ToString("N"));
			try
			{
				File.WriteAllText(probe, "x");
				File.Delete(probe);
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}

		[Fact]
		public void After_dispose_the_changes_of_books_are_ignored()
		{
			using (var s = new Setup(writing: true, automatic: true, books: 1))
			{
				ComicBook book = s.Books[0];
				s.Updates.Dispose();
				book.Series = "After dispose";
				Assert.False(book.ComicInfoIsDirty);
				Assert.False(s.Updates.IsStarted);
			}
		}
	}
}
