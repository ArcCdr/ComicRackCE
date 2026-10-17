using System.Threading;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.IO;

namespace ComicRack.Mac.Thumbnails
{
	/// <summary>One thumbnail to produce: a book, the Core key of its front cover, and the (snapped) height in pixels.</summary>
	public sealed class ThumbnailRequest
	{
		public ThumbnailRequest(ComicBook book, ThumbnailKey key, int height)
		{
			Book = book;
			Key = key;
			Height = height;
		}

		public ComicBook Book { get; }

		/// <summary>The Core's key of the front cover thumbnail (location, file size and time, page index, rotation).</summary>
		public ThumbnailKey Key { get; }

		/// <summary>One of <see cref="ThumbnailSteps"/>.</summary>
		public int Height { get; }
	}

	/// <summary>
	/// Turns a <see cref="ThumbnailRequest"/> into pixels. The service calls it on a pool thread, never more than its concurrency
	/// limit at a time, and not at all for a request that was cancelled before its turn. The seam exists so the service can be
	/// tested with a decoder that blocks, counts and fails on command (the real one is <see cref="CoreThumbnailDecoder"/>).
	/// </summary>
	public interface IThumbnailDecoder
	{
		/// <summary>
		/// Returns a block with one reference, which the caller owns. Throws if the image cannot be produced (the service then shows
		/// a placeholder); should throw <see cref="System.OperationCanceledException"/> soon after <paramref name="cancellationToken"/>
		/// is cancelled, which happens only when nobody waits for the image any more.
		/// </summary>
		PixelBlock Decode(ThumbnailRequest request, CancellationToken cancellationToken);
	}
}
