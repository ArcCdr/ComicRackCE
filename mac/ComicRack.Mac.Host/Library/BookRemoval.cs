using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using cYo.Common.Win32;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;

namespace ComicRack.Mac.Host.Library
{
	/// <summary>What the Remove command of a list does with the books (<c>ComicListLibraryBrowser.RemoveBookHandler</c>).</summary>
	public enum RemoveMode
	{
		/// <summary>The library, or a list that cannot drop a book by itself (a folder of lists): the books leave the library.</summary>
		Library,

		/// <summary>A fixed list or a smart list: the books leave the list, and the library as well if the user says so.</summary>
		List,
	}

	/// <summary>What <see cref="BookRemoval.Remove"/> did.</summary>
	public sealed class RemoveResult
	{
		/// <summary>Books taken out of the library.</summary>
		public int RemovedFromLibrary { get; set; }

		/// <summary>Books taken out of a fixed list or hidden in a smart list.</summary>
		public int RemovedFromList { get; set; }

		/// <summary>Files that went to the Trash.</summary>
		public int FilesTrashed { get; set; }

		/// <summary>Books whose file could not be moved to the Trash: they stay in the library (the original shows <c>FailedDeleteBooks</c>).</summary>
		public int FilesFailed { get; set; }
	}

	/// <summary>
	/// The Remove command without a window: the rules of <c>ComicListLibraryBrowser.RemoveBookHandler.RemoveBooks</c> that follow the questions. The questions
	/// themselves are the caller's (<c>MainViewModel</c>); this class only does what they decided.
	/// </summary>
	public static class BookRemoval
	{
		/// <summary>
		/// Which question the list asks: a list that is the library, or has neither <see cref="IEditableComicBookListProvider"/> nor
		/// <see cref="IFilteredComicBookList"/>, removes from the library; the others remove from the list. No list (nothing shown) is the library.
		/// </summary>
		public static RemoveMode ModeOf(ComicListItem list)
		{
			bool editable = list is IEditableComicBookListProvider;
			bool filtered = list is IFilteredComicBookList;
			if (list == null || list is ComicLibraryListItem || (!editable && !filtered))
			{
				return RemoveMode.Library;
			}
			return RemoveMode.List;
		}

		/// <summary>True for a smart list: its books are hidden (filtered) instead of removed.</summary>
		public static bool IsFiltered(ComicListItem list) => list is IFilteredComicBookList;

		/// <summary>
		/// Removes <paramref name="books"/>. In <see cref="RemoveMode.List"/> they leave <paramref name="list"/> (a smart list marks them filtered), and, when
		/// <paramref name="alsoFromLibrary"/> is set, the library too. In <see cref="RemoveMode.Library"/> they leave the library. A book that leaves the library
		/// goes on the black list (a scan does not add it again) if it has a file; with <paramref name="moveFilesToTrash"/> its file goes to the Trash first and
		/// the book stays when the file is still there afterwards.
		/// </summary>
		public static RemoveResult Remove(ComicDatabase library, ComicListItem list, IEnumerable<ComicBook> books, bool alsoFromLibrary, bool moveFilesToTrash)
		{
			if (library == null)
			{
				throw new ArgumentNullException(nameof(library));
			}
			ComicBook[] all = (books ?? Enumerable.Empty<ComicBook>()).ToArray();
			var result = new RemoveResult();
			bool deleteFromLibrary = true;
			if (ModeOf(list) == RemoveMode.List)
			{
				deleteFromLibrary = alsoFromLibrary;
				var editable = list as IEditableComicBookListProvider;
				var filtered = list as IFilteredComicBookList;
				foreach (ComicBook book in all)
				{
					if (editable != null && editable.Remove(book))
					{
						result.RemovedFromList++;
					}
					if (filtered != null && !deleteFromLibrary)
					{
						filtered.SetFiltered(book, filtered: true);
						result.RemovedFromList++;
					}
				}
			}
			if (!deleteFromLibrary)
			{
				return result;
			}
			foreach (ComicBook book in all)
			{
				if (book.IsLinked && moveFilesToTrash)
				{
					try
					{
						ShellFile.DeleteFile(ShellFileDeleteOptions.None, book.FilePath);
					}
					catch (Exception)
					{
						// the file is checked below, as the original does
					}
					if (File.Exists(book.FilePath))
					{
						result.FilesFailed++;
						continue;
					}
					result.FilesTrashed++;
				}
				library.Remove(book);
				result.RemovedFromLibrary++;
				if (book.IsLinked)
				{
					library.AddToBlackList(book.FilePath);
				}
			}
			return result;
		}
	}
}
