using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace cYo.Compat.Drawing.Internal
{
	/// <summary>Decoding and encoding through Skia. Decoding is eager: the stream is not needed after the call.</summary>
	internal static class ImageCodec
	{
		/// <summary>The quality GDI+ uses when no EncoderParameters are given.</summary>
		public const int DefaultJpegQuality = 75;

		public static Bitmap Decode(Stream stream)
		{
			if (stream == null)
			{
				throw new ArgumentNullException(nameof(stream));
			}
			using (SKData data = ReadRemaining(stream))
			{
				return Decode(data);
			}
		}

		public static Bitmap Decode(byte[] bytes)
		{
			using (SKData data = SKData.CreateCopy(bytes))
			{
				return Decode(data);
			}
		}

		/// <summary>
		/// Pixels are decoded to BGRA with no colour conversion and no EXIF rotation (as GDI+ with useEmbeddedColorManagement=false).
		/// An image without alpha becomes Format24bppRgb (opaque storage), every other one Format32bppArgb (not premultiplied).
		/// Truncated or damaged data that Skia can partly decode is accepted. Anything else is an ArgumentException("Parameter is not valid."), as in GDI+.
		/// </summary>
		private static Bitmap Decode(SKData data)
		{
			if (data.Size == 0)
			{
				throw new ArgumentException("Parameter is not valid.");
			}
			using (SKCodec codec = SKCodec.Create(data))
			{
				if (codec == null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
				{
					throw new ArgumentException("Parameter is not valid.");
				}
				bool opaque = codec.Info.AlphaType == SKAlphaType.Opaque;
				PixelFormat format = opaque ? PixelFormat.Format24bppRgb : PixelFormat.Format32bppArgb;
				SKImageInfo info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, opaque ? SKAlphaType.Opaque : SKAlphaType.Unpremul);
				SKBitmap bitmap = new SKBitmap();
				if (!bitmap.TryAllocPixels(info))
				{
					bitmap.Dispose();
					throw new OutOfMemoryException($"Not enough memory for a {info.Width} x {info.Height} bitmap");
				}
				try
				{
					SKCodecResult result = codec.GetPixels(info, bitmap.GetPixels(), bitmap.RowBytes, SKCodecOptions.Default);
					if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput && result != SKCodecResult.ErrorInInput)
					{
						throw new ArgumentException("Parameter is not valid.");
					}
					if (opaque && result != SKCodecResult.Success)
					{
						// The rows that could not be decoded are zero: make them opaque black, as the alpha byte of opaque storage must be 255.
						IntPtr pixels = bitmap.GetPixels();
						PixelConvert.Convert(pixels, bitmap.RowBytes, PixKind.Rgb32, pixels, bitmap.RowBytes, PixKind.Rgb32, info.Width, info.Height);
					}
					return Bitmap.FromStorage(bitmap, format);
				}
				catch
				{
					bitmap.Dispose();
					throw;
				}
			}
		}

		private static SKData ReadRemaining(Stream stream)
		{
			if (stream is MemoryStream memory)
			{
				return CopyRemaining(memory);
			}
			using (MemoryStream copy = new MemoryStream())
			{
				stream.CopyTo(copy);
				copy.Position = 0;
				return CopyRemaining(copy);
			}
		}

		private static SKData CopyRemaining(MemoryStream memory)
		{
			long remaining = memory.Length - memory.Position;
			if (remaining <= 0)
			{
				return SKData.Empty;
			}
			if (memory.TryGetBuffer(out ArraySegment<byte> segment))
			{
				return SKData.CreateCopy(new ReadOnlySpan<byte>(segment.Array, segment.Offset + (int)memory.Position, (int)remaining));
			}
			return SKData.CreateCopy(memory.ToArray().AsSpan((int)memory.Position));
		}

		/// <summary>
		/// Writes JPEG (quality 0 to 100, 4:2:0) or PNG. A bitmap without alpha is written without an alpha channel (24-bit PNG).
		/// JPEG ignores alpha and keeps the colour channels as they are, as GDI+ does.
		/// </summary>
		public static void Encode(Image image, Stream stream, Guid formatId, int jpegQuality)
		{
			if (stream == null)
			{
				throw new ArgumentNullException(nameof(stream));
			}
			bool jpeg = formatId == ImageFormat.Jpeg.Guid;
			if (!jpeg && formatId != ImageFormat.Png.Guid)
			{
				ImageFormat known = new[] { ImageFormat.Bmp, ImageFormat.Gif, ImageFormat.Tiff, ImageFormat.Webp, ImageFormat.Heif, ImageFormat.Icon, ImageFormat.Emf, ImageFormat.Wmf, ImageFormat.Exif, ImageFormat.MemoryBmp }.FirstOrDefault(f => f.Guid == formatId);
				throw new NotSupportedException($"Saving as {known?.ToString() ?? formatId.ToString()} is not yet supported on macOS (JPEG and PNG only)");
			}
			SKBitmap storage = image.Storage;
			SKBitmap straight = null;
			try
			{
				if (jpeg && storage.AlphaType == SKAlphaType.Premul)
				{
					straight = new SKBitmap();
					if (!straight.TryAllocPixels(new SKImageInfo(storage.Width, storage.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul)))
					{
						throw new OutOfMemoryException("Not enough memory to encode the bitmap");
					}
					PixelConvert.Convert(storage.GetPixels(), storage.RowBytes, PixKind.PArgb, straight.GetPixels(), straight.RowBytes, PixKind.Argb, storage.Width, storage.Height);
				}
				using (SKPixmap pixmap = (straight ?? storage).PeekPixels())
				{
					bool ok = jpeg
						? pixmap.Encode(stream, new SKJpegEncoderOptions(Math.Max(0, Math.Min(100, jpegQuality)), SKJpegEncoderDownsample.Downsample420, SKJpegEncoderAlphaOption.Ignore))
						: pixmap.Encode(stream, SKPngEncoderOptions.Default);
					if (!ok)
					{
						throw new ExternalException("A generic error occurred in GDI+.");
					}
				}
				GC.KeepAlive(image);
			}
			finally
			{
				straight?.Dispose();
			}
		}
	}
}
