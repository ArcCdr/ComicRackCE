using System;
using System.IO;
using System.Threading;
using cYo.Common.Threading;
using cYo.Compat.Drawing;
using ComicRack.Mac.Thumbnails;
using SkiaSharp;

namespace ComicRack.Mac.Reader
{
	/// <summary>Skia-side helpers of the reader's page path: decoding the bytes of a page, and copying a bitmap that belongs to the Core.</summary>
	public static class PagePixels
	{
		/// <summary>
		/// The most pixels a decoded page may have (48 million, a 7000 x 7000 scan: 192 MB). A bigger one is decoded at a reduced size
		/// (JPEG decodes at 1/2, 1/4 or 1/8 directly) and, if the format cannot do that, reduced after the decode. A normal comic page
		/// has 1 to 6 million; a 4K screen shows 8.
		/// </summary>
		public const long DefaultMaxPixels = 48_000_000;

		/// <summary>
		/// Decodes the bytes of a page (JPEG, PNG, WebP, ... whatever Skia reads) into a BGRA bitmap that the returned block owns: one
		/// allocation, no copy afterwards. A JPEG comes out <see cref="SKAlphaType.Opaque"/>; a PNG that really has alpha stays
		/// premultiplied. No colour management and no EXIF rotation, as in GDI+. A file that is cut short but partly decodable is
		/// accepted (the missing part stays transparent or black), because a reader should show what is there.
		/// </summary>
		/// <exception cref="InvalidDataException">The bytes are not an image that Skia can decode.</exception>
		public static PixelBlock Decode(byte[] data, long maxPixels = DefaultMaxPixels, CancellationToken cancellationToken = default)
		{
			if (data == null || data.Length == 0)
			{
				throw new InvalidDataException("The page has no data.");
			}
			SKBitmap bitmap = null;
			try
			{
				using (SKData skData = SKData.CreateCopy(data))
				using (SKCodec codec = SKCodec.Create(skData))
				{
					if (codec == null)
					{
						throw new InvalidDataException("The page is not an image that can be decoded (" + data.Length + " bytes).");
					}
					SKImageInfo source = codec.Info;
					int width = source.Width, height = source.Height;
					if (maxPixels > 0 && (long)width * height > maxPixels)
					{
						float scale = (float)Math.Sqrt((double)maxPixels / ((double)width * height));
						SKSizeI scaled = codec.GetScaledDimensions(scale);
						if (scaled.Width > 0 && scaled.Height > 0)
						{
							width = scaled.Width;
							height = scaled.Height;
						}
					}
					var info = new SKImageInfo(width, height, SKColorType.Bgra8888, source.IsOpaque ? SKAlphaType.Opaque : SKAlphaType.Premul);
					bitmap = new SKBitmap(info);
					SKCodecResult result = codec.GetPixels(info, bitmap.GetPixels());
					if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
					{
						throw new InvalidDataException("The page could not be decoded (" + result + ").");
					}
				}
				cancellationToken.ThrowIfCancellationRequested();
				if (maxPixels > 0 && (long)bitmap.Width * bitmap.Height > maxPixels)
				{
					// The format has no reduced decode (PNG): reduce now, with an averaging filter.
					double f = Math.Sqrt((double)maxPixels / ((double)bitmap.Width * bitmap.Height));
					var smallInfo = new SKImageInfo(Math.Max(1, (int)(bitmap.Width * f)), Math.Max(1, (int)(bitmap.Height * f)), SKColorType.Bgra8888, bitmap.AlphaType);
					SKBitmap small = bitmap.Resize(smallInfo, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
					if (small == null)
					{
						throw new InvalidDataException("The page is too large and could not be reduced.");
					}
					bitmap.Dispose();
					bitmap = small;
				}
				PixelBlock block = PixelBlock.FromBitmap(bitmap, onFree: null, detectOpaque: true);
				bitmap = null;
				return block;
			}
			finally
			{
				bitmap?.Dispose();
			}
		}

		/// <summary>
		/// Copies the pixels of a bitmap that belongs to the Core into memory of our own. The Core disposes its bitmaps whenever it
		/// likes (cache eviction, the 5 second release of a decoded page), and it does so while holding <c>ItemMonitor</c> on the bitmap;
		/// holding the same lock while copying is the only safe way to read it. If the Core already disposed it, this throws
		/// <see cref="ObjectDisposedException"/> and the caller decodes the page bytes instead.
		/// </summary>
		public static PixelBlock CopyFrom(System.Drawing.Bitmap bitmap)
		{
			if (bitmap == null)
			{
				throw new ArgumentNullException(nameof(bitmap));
			}
			SKBitmap copy;
			using (ItemMonitor.Lock(bitmap))
			{
				copy = SkiaInterop.GetSKBitmap(bitmap).Copy();
			}
			if (copy == null)
			{
				throw new InvalidDataException("The page bitmap could not be copied.");
			}
			try
			{
				PixelBlock block = PixelBlock.FromBitmap(copy, onFree: null, detectOpaque: true);
				copy = null;
				return block;
			}
			finally
			{
				copy?.Dispose();
			}
		}
	}
}
