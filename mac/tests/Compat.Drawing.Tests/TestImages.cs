using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using cYo.Compat.Drawing;
using SkiaSharp;

namespace Compat.Drawing.Tests
{
	internal static class TestImages
	{
		public static readonly PixelFormat[] AllFormats =
		{
			PixelFormat.Format32bppArgb,
			PixelFormat.Format32bppPArgb,
			PixelFormat.Format32bppRgb,
			PixelFormat.Format24bppRgb,
		};

		/// <summary>
		/// The number of live image storages once the finalizer thread has caught up with the garbage of earlier tests. Tests that assert
		/// exact counts take their baseline here: an image that an earlier test left undisposed is finalized (and the counter drops) on
		/// the finalizer thread at any moment, which would shift a baseline taken without this.
		/// </summary>
		public static int LiveSettled()
		{
			int last = -1;
			for (int round = 0; round < 20; round++)
			{
				GC.Collect();
				GC.WaitForPendingFinalizers();
				int now = Image.LiveStorageCount;
				if (now == last)
				{
					return now;
				}
				last = now;
			}
			return last;
		}

		/// <summary>A smooth gradient (red grows to the right, green downwards, blue fixed), so that lossy codecs stay close.</summary>
		public static Color Gradient(int x, int y, int w, int h) => Color.FromArgb(255, x * 255 / Math.Max(1, w - 1), y * 255 / Math.Max(1, h - 1), 128);

		/// <summary>A bitmap with the gradient, written pixel by pixel.</summary>
		public static Bitmap GradientBitmap(int w, int h, PixelFormat format = PixelFormat.Format24bppRgb)
		{
			Bitmap bitmap = new Bitmap(w, h, format);
			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					bitmap.SetPixel(x, y, Gradient(x, y, w, h));
				}
			}
			return bitmap;
		}

		public static Bitmap Solid(int w, int h, Color color, PixelFormat format = PixelFormat.Format32bppArgb)
		{
			Bitmap bitmap = new Bitmap(w, h, format);
			using (Graphics g = Graphics.FromImage(bitmap))
			{
				g.Clear(color);
			}
			return bitmap;
		}

		/// <summary>Reproducible noise: JPEG and PNG sizes react strongly to the quality.</summary>
		public static Bitmap NoiseBitmap(int w, int h, PixelFormat format = PixelFormat.Format24bppRgb, int seed = 7)
		{
			Random random = new Random(seed);
			Bitmap bitmap = new Bitmap(w, h, format);
			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					bitmap.SetPixel(x, y, Color.FromArgb(255, random.Next(256), random.Next(256), random.Next(256)));
				}
			}
			return bitmap;
		}

		/// <summary>The raw 32-bit value of a pixel in the backing SKBitmap (0xAARRGGBB), read without going through the shim's pixel API.</summary>
		public static uint Raw(Bitmap bitmap, int x, int y)
		{
			SKBitmap sk = SkiaInterop.GetSKBitmap(bitmap);
			return BitConverter.ToUInt32(sk.GetPixelSpan().Slice(y * sk.RowBytes + x * 4, 4));
		}

		/// <summary>Encodes with SkiaSharp itself, so that the decoder tests do not depend on the shim's encoder.</summary>
		public static byte[] SkiaEncode(SKBitmap bitmap, SKEncodedImageFormat format, int quality = 100)
		{
			using (SKData data = bitmap.Encode(format, quality))
			{
				return data.ToArray();
			}
		}

		/// <summary>A gradient photo-like JPEG made by SkiaSharp (opaque).</summary>
		public static byte[] SkiaJpeg(int w, int h, int quality = 100)
		{
			using (SKBitmap sk = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque))
			{
				for (int y = 0; y < h; y++)
				{
					for (int x = 0; x < w; x++)
					{
						Color c = Gradient(x, y, w, h);
						sk.SetPixel(x, y, new SKColor(c.R, c.G, c.B, 255));
					}
				}
				return SkiaEncode(sk, SKEncodedImageFormat.Jpeg, quality);
			}
		}

		/// <summary>A 2 x 1 PNG with a not-premultiplied translucent red and an opaque blue pixel, made by SkiaSharp.</summary>
		public static byte[] SkiaPngWithAlpha()
		{
			using (SKBitmap sk = new SKBitmap(2, 1, SKColorType.Bgra8888, SKAlphaType.Unpremul))
			{
				sk.SetPixel(0, 0, new SKColor(255, 0, 0, 128));
				sk.SetPixel(1, 0, new SKColor(0, 0, 255, 255));
				return SkiaEncode(sk, SKEncodedImageFormat.Png);
			}
		}

		public static byte[] SkiaPngOpaque()
		{
			using (SKBitmap sk = new SKBitmap(3, 2, SKColorType.Bgra8888, SKAlphaType.Opaque))
			{
				for (int y = 0; y < 2; y++)
				{
					for (int x = 0; x < 3; x++)
					{
						sk.SetPixel(x, y, new SKColor((byte)(x * 100), (byte)(y * 100), 50));
					}
				}
				return SkiaEncode(sk, SKEncodedImageFormat.Png);
			}
		}

		public static MemoryStream ToStream(byte[] bytes) => new MemoryStream(bytes);

		public static void AssertColor(Color expected, Color actual, int tolerance = 0)
		{
			bool ok = Math.Abs(expected.A - actual.A) <= tolerance
				&& Math.Abs(expected.R - actual.R) <= tolerance
				&& Math.Abs(expected.G - actual.G) <= tolerance
				&& Math.Abs(expected.B - actual.B) <= tolerance;
			if (!ok)
			{
				throw new Xunit.Sdk.XunitException($"Expected ARGB {expected.A},{expected.R},{expected.G},{expected.B} (+-{tolerance}) but was {actual.A},{actual.R},{actual.G},{actual.B}");
			}
		}

		/// <summary>A stream that cannot seek, like a network or archive stream.</summary>
		public sealed class ForwardOnlyStream : Stream
		{
			private readonly Stream inner;

			public ForwardOnlyStream(byte[] bytes)
			{
				inner = new MemoryStream(bytes);
			}

			public override bool CanRead => true;
			public override bool CanSeek => false;
			public override bool CanWrite => false;
			public override long Length => throw new NotSupportedException();
			public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
			public override void Flush() { }
			public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
			public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
			public override void SetLength(long value) => throw new NotSupportedException();
			public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		}
	}
}
