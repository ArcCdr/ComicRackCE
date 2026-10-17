using System;
using System.IO;
using System.Threading;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.IO;
using cYo.Projects.ComicRack.Engine.IO.Provider;

namespace ComicRack.Mac.Thumbnails
{
	/// <summary>
	/// The real decoder: asks the Core's <c>ImagePool</c> for the book's front cover thumbnail (its memory cache, then its disk
	/// cache, and only then the book file: first page decoded, reduced to 512 px, stored as JPEG), and decodes the stored
	/// JPEG with Skia at the requested step.
	///
	/// <b>Why not wrap the Core's <c>ThumbnailImage.Bitmap</c> with <c>SkiaInterop.GetSKBitmap</c>?</b> That bitmap is not ours.
	/// The <c>ThumbnailImage</c> owns it, replaces and disposes it when another height is asked for, releases it after
	/// <c>SecondsToKeepDecodedImage</c> (15 s) without a request, and disposes it when the image leaves the Core's cache. An
	/// <c>SKImage</c> over it could therefore be drawn from freed memory, and the only safe way to use it is to copy the pixels
	/// (and to hold the Core's <c>ItemMonitor</c> lock while doing so). Decoding the stored bytes ourselves costs one decode of
	/// a 512 px JPEG (a millisecond or two), allocates exactly the pixels that the image keeps, copies nothing, leaves no second
	/// decoded copy in the Core for 15 seconds, and does not share any mutable state with the Core. <c>SkiaInterop.GetSKBitmap</c>
	/// is the right tool for the reader's pages (M2.3), where the page bitmap is ours.
	/// </summary>
	public sealed class CoreThumbnailDecoder : IThumbnailDecoder
	{
		private readonly CacheManager cache;

		public CoreThumbnailDecoder(CacheManager cache)
		{
			this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
		}

		public PixelBlock Decode(ThumbnailRequest request, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ComicBook book = request.Book;
			byte[] data;
			try
			{
				using (IImageProvider provider = new ComicBookImageProvider(book, null, book.TranslateImageIndexToPage(request.Key.Index)))
				using (var item = cache.ImagePool.GetThumbnail(request.Key, provider, onErrorThrowException: true))
				{
					ThumbnailImage thumbnail = item?.Item;
					if (thumbnail == null)
					{
						throw new InvalidDataException("The Core has no thumbnail for " + request.Key.Location);
					}
					data = thumbnail.Data;
					// The Core decodes a 512 px bitmap of a ThumbnailImage when it saves it to the disk cache, and of every one it makes
					// or loads after the first such request (a static "last request height"), and keeps that bitmap for 15 seconds. We
					// only need the stored bytes. Letting the bitmap go now keeps a fast scroll from piling up hundreds of megabytes in
					// the Core, and it also publishes the data size that the Core's cache trims by.
					thumbnail.Bitmap = null;
				}
			}
			catch (Exception ex) when (!(ex is OperationCanceledException) && book.IsLinked && !File.Exists(book.FilePath))
			{
				// The Core only says "Could not load thumbnail". The usual cause deserves its own message (an unmounted volume, a moved file).
				throw new FileNotFoundException("The book file is missing: " + book.FilePath, book.FilePath, ex);
			}
			cancellationToken.ThrowIfCancellationRequested();
			return ThumbnailPixels.Decode(data, request.Height, cancellationToken);
		}
	}
}
