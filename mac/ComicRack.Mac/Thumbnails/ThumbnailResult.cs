using System;
using SkiaSharp;

namespace ComicRack.Mac.Thumbnails
{
	/// <summary>What <see cref="ThumbnailService.RequestResult"/> completes with.</summary>
	public sealed class ThumbnailResult
	{
		public ThumbnailResult(SKImage image, bool isPlaceholder, Exception error)
		{
			Image = image;
			IsPlaceholder = isPlaceholder;
			Error = error;
		}

		/// <summary>
		/// The thumbnail, owned by whoever received this result: it is an <see cref="SKImage"/> of its own over pixels that the
		/// service shares, so the receiver may dispose it whenever it is done (a recycled cell, a replaced image) and need not
		/// worry about the cache evicting the entry meanwhile. An image that is never disposed is freed by the garbage collector.
		/// </summary>
		public SKImage Image { get; }

		/// <summary>True if <see cref="Image"/> is the stand-in picture because the real thumbnail could not be made.</summary>
		public bool IsPlaceholder { get; }

		/// <summary>Why the thumbnail could not be made (a missing or damaged file, ...), or null.</summary>
		public Exception Error { get; }
	}
}
