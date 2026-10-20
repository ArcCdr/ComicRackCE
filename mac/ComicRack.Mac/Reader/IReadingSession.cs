using System;
using System.Threading;
using System.Threading.Tasks;

namespace ComicRack.Mac.Reader
{
	/// <summary>
	/// One book open for reading: what the reader view model needs from the Core, behind an interface so that the view model can be
	/// tested with a book of generated pages. The real one is <see cref="CoreReadingSession"/>.
	/// </summary>
	public interface IReadingSession : IDisposable
	{
		/// <summary>The window title: the book's caption.</summary>
		string Title { get; }

		/// <summary>The decoder of the pages; valid once <see cref="OpenAsync"/> has completed.</summary>
		IPageDecoder Decoder { get; }

		/// <summary>The number of pages; valid once <see cref="OpenAsync"/> has completed.</summary>
		int PageCount { get; }

		/// <summary>The page to start at (0-based): where the reader was left, within the book; valid once <see cref="OpenAsync"/> has completed.</summary>
		int ResumePage { get; }

		/// <summary>Opens the book and reads its page index. Throws if the book cannot be read or the token is cancelled.</summary>
		Task OpenAsync(CancellationToken cancellationToken);

		/// <summary>The reader is on <paramref name="page"/> (0-based): record it in the book (current page, last page read).</summary>
		void ReportPage(int page);

		/// <summary>The reader was asked to go past the last page.</summary>
		void ReportEndReached();

		/// <summary>What the layout needs to know about a page (types, position, size if the Core has it). Valid once the book is open.</summary>
		PageTraits GetPageTraits(int page) => PageTraits.Default(page);

		/// <summary>Whether the book asks to be read from right to left (the Core's manga setting), or null if it does not say.</summary>
		bool? RightToLeft => null;

		/// <summary>
		/// A spread showed up to <paramref name="page"/> (0-based): the furthest page read moves there if it is further, as the WinForms
		/// display does for the second page of a spread (the current page stays the first).
		/// </summary>
		void ReportFurthest(int page)
		{
		}

		/// <summary>The user's rating of the book, 0 (none) to 5 stars (<c>ComicBook.Rating</c>).</summary>
		float Rating => 0f;

		/// <summary>Sets the rating of the book (the page menu's My Rating).</summary>
		void SetRating(float rating)
		{
		}
	}
}
