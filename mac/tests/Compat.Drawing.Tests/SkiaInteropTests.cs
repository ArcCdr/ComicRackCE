using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using cYo.Compat.Drawing;
using SkiaSharp;
using Xunit;
using static Compat.Drawing.Tests.TestImages;

namespace Compat.Drawing.Tests
{
	/// <summary>SkiaInterop.GetSKBitmap: the storage itself, without a copy.</summary>
	public class SkiaInteropTests
	{
		[Theory]
		[InlineData(PixelFormat.Format32bppArgb, SKAlphaType.Unpremul)]
		[InlineData(PixelFormat.Format32bppPArgb, SKAlphaType.Premul)]
		[InlineData(PixelFormat.Format32bppRgb, SKAlphaType.Opaque)]
		[InlineData(PixelFormat.Format24bppRgb, SKAlphaType.Opaque)]
		public void GetSKBitmap_is_bgra8888_with_the_alpha_type_of_the_pixel_format(PixelFormat format, SKAlphaType alphaType)
		{
			using (Bitmap b = new Bitmap(6, 4, format))
			{
				SKBitmap sk = SkiaInterop.GetSKBitmap(b);
				Assert.Equal(SKColorType.Bgra8888, sk.ColorType);
				Assert.Equal(alphaType, sk.AlphaType);
				Assert.Equal(6, sk.Width);
				Assert.Equal(4, sk.Height);
			}
		}

		[Fact]
		public void GetSKBitmap_does_not_copy()
		{
			using (Bitmap b = new Bitmap(4, 3, PixelFormat.Format32bppArgb))
			{
				SKBitmap first = SkiaInterop.GetSKBitmap(b);
				Assert.Same(first, SkiaInterop.GetSKBitmap(b));

				BitmapData data = b.LockBits(new Rectangle(0, 0, 4, 3), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
				Assert.Equal(first.GetPixels(), data.Scan0);
				b.UnlockBits(data);

				// A change through the SKBitmap shows in the Bitmap and the other way round.
				first.SetPixel(2, 1, new SKColor(10, 20, 30, 255));
				Assert.Equal(Color.FromArgb(255, 10, 20, 30), b.GetPixel(2, 1));
				b.SetPixel(0, 0, Color.FromArgb(200, 1, 2, 3));
				SKColor read = first.GetPixel(0, 0);
				Assert.Equal((200, 1, 2, 3), (read.Alpha, read.Red, read.Green, read.Blue));
			}
		}

		[Fact]
		public void GetSKBitmap_of_a_decoded_image_is_the_decoded_pixels()
		{
			using (Bitmap b = (Bitmap)Image.FromStream(ToStream(SkiaPngOpaque())))
			{
				SKBitmap sk = SkiaInterop.GetSKBitmap(b);
				Assert.Equal(SKAlphaType.Opaque, sk.AlphaType);
				SKColor c = sk.GetPixel(2, 1);
				Assert.Equal((200, 100, 50), (c.Red, c.Green, c.Blue));
			}
		}

		[Fact]
		public void GetSKBitmap_rejects_null_and_disposed_bitmaps()
		{
			Assert.Throws<ArgumentNullException>(() => SkiaInterop.GetSKBitmap(null));
			Bitmap b = new Bitmap(2, 2);
			b.Dispose();
			Assert.Throws<ObjectDisposedException>(() => SkiaInterop.GetSKBitmap(b));
		}

		[Fact]
		public void The_SKBitmap_is_released_when_the_bitmap_is_disposed()
		{
			Bitmap b = new Bitmap(8, 8);
			SKBitmap sk = SkiaInterop.GetSKBitmap(b);
			Assert.NotEqual(IntPtr.Zero, sk.Handle);
			b.Dispose();
			Assert.Equal(IntPtr.Zero, sk.Handle);
		}
	}
}
