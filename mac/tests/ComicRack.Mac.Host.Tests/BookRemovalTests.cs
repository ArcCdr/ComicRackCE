using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ComicRack.Mac.Host.Library;
using cYo.Common.Collections;
using cYo.Common.Win32;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;
using Xunit;

namespace ComicRack.Mac.Host.Tests
{
	/// <summary>The Remove command of the browser without its questions (P5.5, BC.23): <see cref="BookRemoval"/> does what <c>ComicListLibraryBrowser.RemoveBookHandler</c> does after them.</summary>
	public class BookRemovalTests
	{
		// The Trash is a folder of the test; a file that "cannot be moved" stays where it is.
		private sealed class FakeTrash : IDisposable
		{
			private readonly Action<string> before = ShellFile.TrashHandler;

			public List<string> Moved { get; } = new List<string>();

			public HashSet<string> Refuse { get; } = new HashSet<string>();

			public FakeTrash(string folder)
			{
				ShellFile.TrashHandler = path =>
				{
					if (Refuse.Contains(path))
					{
						throw new IOException("in use");
					}
					File.Move(path, Path.Combine(folder, Path.GetFileName(path)));
					Moved.Add(path);
				};
			}

			public void Dispose() => ShellFile.TrashHandler = before;
		}

		private static ComicBook Book(string path, string title)
		{
			if (path != null)
			{
				File.WriteAllText(path, title);
			}
			return path == null ? new ComicBook { Title = title, PageCount = 10 } : new ComicBook { FilePath = path, Title = title, PageCount = 10 };
		}

		[Fact]
		public void The_library_a_folder_and_nothing_remove_from_the_library_and_a_fixed_or_smart_list_from_the_list()
		{
			Assert.Equal(RemoveMode.Library, BookRemoval.ModeOf(new ComicLibraryListItem()));
			Assert.Equal(RemoveMode.Library, BookRemoval.ModeOf(new ComicListItemFolder()));
			Assert.Equal(RemoveMode.Library, BookRemoval.ModeOf(null));
			Assert.Equal(RemoveMode.List, BookRemoval.ModeOf(new ComicIdListItem("fixed")));
			Assert.Equal(RemoveMode.List, BookRemoval.ModeOf(new ComicSmartListItem("smart")));
			Assert.True(BookRemoval.IsFiltered(new ComicSmartListItem("smart")));
			Assert.False(BookRemoval.IsFiltered(new ComicIdListItem("fixed")));
		}

		[Fact]
		public void Removing_from_the_library_takes_the_books_out_blacklists_their_files_and_keeps_the_files()
		{
			using (var temp = new TempFolder())
			using (var host = MacHost.Open(new MacHostOptions { Paths = temp.Paths }))
			{
				ComicDatabase db = host.DatabaseManager.Database;
				ComicBook a = Book(Path.Combine(temp.Path, "a.cbz"), "A");
				ComicBook b = Book(Path.Combine(temp.Path, "b.cbz"), "B");
				ComicBook c = Book(null, "C (no file)");
				ComicBook keep = Book(Path.Combine(temp.Path, "keep.cbz"), "Keep");
				foreach (ComicBook book in new[] { a, b, c, keep })
				{
					db.Books.Add(book);
				}
				ComicLibraryListItem library = db.ComicLists.GetItems<ComicLibraryListItem>().Single();

				RemoveResult result = BookRemoval.Remove(db, library, new[] { a, b, c }, alsoFromLibrary: false, moveFilesToTrash: false);

				Assert.Equal(3, result.RemovedFromLibrary);
				Assert.Equal(0, result.FilesTrashed);
				Assert.Equal(new[] { keep }, db.Books.Lock().ToArray());
				Assert.True(db.IsBlacklisted(a.FilePath));
				Assert.True(db.IsBlacklisted(b.FilePath));
				Assert.Equal(2, db.BlackList.Count); // the book without a file is not on the list
				Assert.True(File.Exists(a.FilePath) && File.Exists(b.FilePath));
			}
		}

		[Fact]
		public void With_the_Trash_option_the_files_go_to_the_Trash_and_a_book_whose_file_stays_is_not_removed()
		{
			using (var temp = new TempFolder())
			using (var host = MacHost.Open(new MacHostOptions { Paths = temp.Paths }))
			{
				string trashFolder = Path.Combine(temp.Path, "trash");
				Directory.CreateDirectory(trashFolder);
				ComicDatabase db = host.DatabaseManager.Database;
				ComicBook a = Book(Path.Combine(temp.Path, "a.cbz"), "A");
				ComicBook inUse = Book(Path.Combine(temp.Path, "inuse.cbz"), "In use");
				db.Books.Add(a);
				db.Books.Add(inUse);
				using (var trash = new FakeTrash(trashFolder))
				{
					trash.Refuse.Add(inUse.FilePath);
					RemoveResult result = BookRemoval.Remove(db, db.ComicLists.GetItems<ComicLibraryListItem>().Single(), new[] { a, inUse }, alsoFromLibrary: false, moveFilesToTrash: true);

					Assert.Equal(1, result.RemovedFromLibrary);
					Assert.Equal(1, result.FilesTrashed);
					Assert.Equal(1, result.FilesFailed);
					Assert.Equal(new[] { inUse }, db.Books.Lock().ToArray());
					Assert.False(File.Exists(a.FilePath));
					Assert.True(File.Exists(Path.Combine(trashFolder, "a.cbz")));
					Assert.True(File.Exists(inUse.FilePath), "the file that could not be moved stays");
					Assert.True(db.IsBlacklisted(a.FilePath));
					Assert.False(db.IsBlacklisted(inUse.FilePath), "a book that stays is not blacklisted");
				}
			}
		}

		[Fact]
		public void Removing_from_a_fixed_list_leaves_the_library_alone_unless_it_is_asked_too()
		{
			using (var temp = new TempFolder())
			using (var host = MacHost.Open(new MacHostOptions { Paths = temp.Paths }))
			{
				ComicDatabase db = host.DatabaseManager.Database;
				ComicBook a = Book(Path.Combine(temp.Path, "a.cbz"), "A");
				ComicBook b = Book(Path.Combine(temp.Path, "b.cbz"), "B");
				db.Books.Add(a);
				db.Books.Add(b);
				var list = new ComicIdListItem("Favourites");
				db.ComicLists.Add(list);
				list.Add(a);
				list.Add(b);

				RemoveResult first = BookRemoval.Remove(db, list, new[] { a }, alsoFromLibrary: false, moveFilesToTrash: true);

				Assert.Equal(1, first.RemovedFromList);
				Assert.Equal(0, first.RemovedFromLibrary);
				Assert.Equal(0, first.FilesTrashed);
				Assert.Equal(new[] { b }, list.GetBooks().ToArray());
				Assert.Equal(2, db.Books.Count);
				Assert.True(File.Exists(a.FilePath), "the Trash option only applies to books that leave the library");

				RemoveResult second = BookRemoval.Remove(db, list, new[] { b }, alsoFromLibrary: true, moveFilesToTrash: false);

				Assert.Equal(1, second.RemovedFromList);
				Assert.Equal(1, second.RemovedFromLibrary);
				Assert.Empty(list.GetBooks());
				Assert.Equal(new[] { a }, db.Books.Lock().ToArray());
				Assert.True(db.IsBlacklisted(b.FilePath));
			}
		}

		[Fact]
		public void Removing_from_a_smart_list_hides_the_books_in_it_and_removing_them_from_the_library_takes_them_out()
		{
			using (var temp = new TempFolder())
			using (var host = MacHost.Open(new MacHostOptions { Paths = temp.Paths }))
			{
				ComicDatabase db = host.DatabaseManager.Database;
				ComicBook a = Book(Path.Combine(temp.Path, "a.cbz"), "A");
				ComicBook b = Book(Path.Combine(temp.Path, "b.cbz"), "B");
				db.Books.Add(a);
				db.Books.Add(b);
				var smart = new ComicSmartListItem("Everything");
				db.ComicLists.Add(smart);

				RemoveResult hidden = BookRemoval.Remove(db, smart, new[] { a }, alsoFromLibrary: false, moveFilesToTrash: false);

				Assert.Equal(1, hidden.RemovedFromList);
				Assert.Equal(0, hidden.RemovedFromLibrary);
				Assert.True(smart.IsFiltered(a));
				Assert.False(smart.IsFiltered(b));
				Assert.Equal(2, db.Books.Count);

				RemoveResult gone = BookRemoval.Remove(db, smart, new[] { b }, alsoFromLibrary: true, moveFilesToTrash: false);

				Assert.Equal(1, gone.RemovedFromLibrary);
				Assert.False(smart.IsFiltered(b), "a book that leaves the library is not hidden in the list");
				Assert.Equal(new[] { a }, db.Books.Lock().ToArray());
			}
		}
	}
}
