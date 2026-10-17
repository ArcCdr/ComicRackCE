using System.Threading;
using ComicRack.Mac.Thumbnails;

namespace ComicRack.Mac.Reader
{
	/// <summary>
	/// Turns a page number into pixels. <see cref="PageService"/> calls it on pool threads, never more than its concurrency limit at
	/// a time, and cancels the token of a decode that nobody needs any more (the reader moved away from that page). The seam exists so
	/// the service can be tested with a decoder that blocks, counts and fails on command; the real one is <see cref="CorePageDecoder"/>.
	/// </summary>
	public interface IPageDecoder
	{
		/// <summary>The number of pages of the open book.</summary>
		int PageCount { get; }

		/// <summary>
		/// Decodes page <paramref name="page"/> (0-based) into memory the caller owns: the returned block has one reference, which the
		/// caller gives back. Throws if the page cannot be produced; should throw <see cref="System.OperationCanceledException"/> soon
		/// after <paramref name="cancellationToken"/> is cancelled.
		/// </summary>
		PixelBlock Decode(int page, CancellationToken cancellationToken);
	}
}
