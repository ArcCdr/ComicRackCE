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
	}
}
