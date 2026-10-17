using System;
using System.IO;
using System.Threading;
using cYo.Common.ComponentModel;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.IO;
using cYo.Projects.ComicRack.Engine.IO.Cache;
using ComicRack.Mac.Thumbnails;

namespace ComicRack.Mac.Reader
{
	/// <summary>
	/// The real page decoder: asks the Core's <c>ImagePool</c> for the page (its memory cache, then its disk cache, then the book
	/// through the navigator, which is the <c>IImageProvider</c> the WinForms display passes too) and turns the result into pixels
	/// that belong to the reader.
	///
	/// <b>Ownership.</b> What the pool returns is not ours. The <c>PageImage</c> is disposed when the Core's page cache evicts it; its
	/// decoded <c>Bitmap</c> is released by the "Free Image Memory" thread 5 seconds (<c>SecondsToKeepDecodedImage</c>) after the last
	/// access, under <c>ItemMonitor</c> on the bitmap; and holding the pool's <c>IItemLock</c> protects neither. An <c>SKImage</c> over
	/// that bitmap could therefore be drawn from freed memory. So the decoder never keeps a reference into the Core:
	/// <list type="bullet">
	/// <item>A page read from the archive carries its original bytes (<c>PageImage.Data</c>, the file as stored: JPEG, PNG, WebP; a
	/// PDF page as JPEG). Decoding them with Skia gives pixels of our own, in one allocation, with no second decoded copy in the Core,
	/// and no shared state. This is the normal path.</item>
	/// <item>A page the Core rotated or colour-adjusted exists as a bitmap (<c>PageImage.IsImage</c>; its <c>Data</c> is only a lossy
	/// JPEG made for the disk cache). That bitmap is copied under the Core's own lock, which is how the Core protects its users.</item>
	/// </list>
	/// </summary>
	public sealed class CorePageDecoder : IPageDecoder
	{
		private readonly ComicBookNavigator navigator;
		private readonly ImagePool pool;
		private readonly long maxPixels;

		public CorePageDecoder(ComicBookNavigator navigator, ImagePool pool, long maxPixels = PagePixels.DefaultMaxPixels)
		{
			this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
			this.pool = pool ?? throw new ArgumentNullException(nameof(pool));
			this.maxPixels = maxPixels;
		}

		public int PageCount => navigator.ProviderPageCount;

		public PixelBlock Decode(int page, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			PageKey key = navigator.GetPageKey(page);
			byte[] data = null;
			PixelBlock copy = null;
			try
			{
				using (IItemLock<PageImage> item = pool.GetPage(key, navigator, onErrorThrowException: true))
				{
					PageImage image = item?.Item;
					if (image == null)
					{
						throw new InvalidDataException("The Core has no image for page " + (page + 1) + ".");
					}
					cancellationToken.ThrowIfCancellationRequested();
					if (image.IsImage)
					{
						try
						{
							copy = PagePixels.CopyFrom(image.Bitmap);
						}
						catch (ObjectDisposedException)
						{
							// The Core released the bitmap between the check and the copy; its bytes are still there.
						}
					}
					if (copy == null)
					{
						data = image.Data;
					}
				}
			}
			catch (Exception ex) when (!(ex is OperationCanceledException) && !(ex is InvalidDataException))
			{
				// The Core only says "Could not open image". The usual causes deserve a message of their own.
				ComicBook book = navigator.Comic;
				if (book.IsLinked && !File.Exists(book.FilePath))
				{
					throw new FileNotFoundException("The book file is missing: " + book.FilePath, book.FilePath, ex);
				}
				throw new InvalidDataException("Page " + (page + 1) + " could not be read from " + Path.GetFileName(book.FilePath ?? string.Empty) + ".", ex);
			}
			if (copy != null)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					copy.Release();
					cancellationToken.ThrowIfCancellationRequested();
				}
				return copy;
			}
			cancellationToken.ThrowIfCancellationRequested();
			return PagePixels.Decode(data, maxPixels, cancellationToken);
		}
	}
}
