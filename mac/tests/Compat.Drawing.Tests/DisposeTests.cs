using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using cYo.Compat.Drawing;
using SkiaSharp;
using Xunit;
using static Compat.Drawing.Tests.TestImages;

namespace Compat.Drawing.Tests
{
	/// <summary>Dispose: no SKBitmap outlives its image, on any path.</summary>
	public class DisposeTests
	{
		private static int Live => Image.LiveStorageCount;

		[Fact]
		public void Dispose_releases_the_storage_once_and_can_be_repeated()
		{
			int before = Live;
			Bitmap b = new Bitmap(10, 10);
			Assert.Equal(before + 1, Live);
			b.Dispose();
			Assert.Equal(before, Live);
			b.Dispose();
			((IDisposable)b).Dispose();
			Assert.Equal(before, Live);
		}

		[Fact]
		public void Using_blocks_release_the_storage()
		{
			int before = Live;
			using (Bitmap b = new Bitmap(10, 10))
			using (Image image = Image.FromStream(ToStream(SkiaPngOpaque())))
			{
				Assert.Equal(before + 2, Live);
			}
			Assert.Equal(before, Live);
		}

		[Fact]
		public void Members_throw_ObjectDisposedException_after_Dispose()
		{
			Bitmap b = new Bitmap(4, 4);
			b.Dispose();
			Assert.Throws<ObjectDisposedException>(() => b.Width);
			Assert.Throws<ObjectDisposedException>(() => b.Height);
			Assert.Throws<ObjectDisposedException>(() => b.Size);
			Assert.Throws<ObjectDisposedException>(() => b.PixelFormat);
			Assert.Throws<ObjectDisposedException>(() => b.Flags);
			Assert.Throws<ObjectDisposedException>(() => b.GetPixel(0, 0));
			Assert.Throws<ObjectDisposedException>(() => b.SetPixel(0, 0, Color.Red));
			Assert.Throws<ObjectDisposedException>(() => b.Clone());
			Assert.Throws<ObjectDisposedException>(() => b.Clone(new Rectangle(0, 0, 1, 1), PixelFormat.Format32bppArgb));
			Assert.Throws<ObjectDisposedException>(() => b.RotateFlip(RotateFlipType.Rotate90FlipNone));
			Assert.Throws<ObjectDisposedException>(() => b.LockBits(new Rectangle(0, 0, 1, 1), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb));
			Assert.Throws<ObjectDisposedException>(() => b.Save(new MemoryStream(), ImageFormat.Png));
		}

		[Fact]
		public void Every_way_to_make_an_image_balances_with_Dispose()
		{
			int before = Live;
			for (int i = 0; i < 200; i++)
			{
				SKBitmap[] storages;
				using (Bitmap a = new Bitmap(64, 64, PixelFormat.Format24bppRgb))
				using (Bitmap b = (Bitmap)a.Clone())
				using (Bitmap c = a.Clone(new Rectangle(1, 1, 8, 8), PixelFormat.Format32bppPArgb))
				using (Bitmap d = (Bitmap)Image.FromStream(ToStream(SkiaPngWithAlpha())))
				{
					storages = new[] { SkiaInterop.GetSKBitmap(a), SkiaInterop.GetSKBitmap(b), SkiaInterop.GetSKBitmap(c), SkiaInterop.GetSKBitmap(d) };
					b.RotateFlip(RotateFlipType.Rotate90FlipNone);
					c.RotateFlip(RotateFlipType.Rotate180FlipX);
					BitmapData data = a.LockBits(new Rectangle(0, 0, 64, 64), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
					a.UnlockBits(data);
					using (MemoryStream stream = new MemoryStream())
					{
						a.Save(stream, ImageFormat.Jpeg);
						a.Save(stream, ImageFormat.Png);
					}
					Assert.Equal(before + 4, Live);
				}
				Assert.All(storages, sk => Assert.Equal(IntPtr.Zero, sk.Handle));
			}
			Assert.Equal(before, Live);
		}

		[Fact]
		public void RotateFlip_releases_the_old_storage()
		{
			int before = Live;
			using (Bitmap b = new Bitmap(30, 20))
			{
				SKBitmap old = SkiaInterop.GetSKBitmap(b);
				b.RotateFlip(RotateFlipType.Rotate90FlipNone);
				Assert.Equal(before + 1, Live);
				Assert.Equal(IntPtr.Zero, old.Handle);
				Assert.NotSame(old, SkiaInterop.GetSKBitmap(b));
			}
			Assert.Equal(before, Live);
		}

		[Fact]
		public void Failed_decodes_and_failed_constructors_leave_nothing_behind()
		{
			int before = Live;
			Assert.ThrowsAny<Exception>(() => Image.FromStream(ToStream(new byte[] { 9, 9, 9, 9, 9, 9, 9, 9, 9, 9 })));
			Assert.ThrowsAny<Exception>(() => Image.FromStream(new MemoryStream()));
			Assert.ThrowsAny<Exception>(() => new Bitmap(0, 0));
			Assert.ThrowsAny<Exception>(() => new Bitmap(4, 4, PixelFormat.Format8bppIndexed));
			using (Bitmap source = new Bitmap(4, 4))
			{
				Assert.ThrowsAny<Exception>(() => source.Clone(new Rectangle(0, 0, 9, 9), PixelFormat.Format32bppArgb));
				Assert.ThrowsAny<Exception>(() => source.Clone(new Rectangle(0, 0, 2, 2), PixelFormat.Format8bppIndexed));
				Assert.ThrowsAny<Exception>(() => source.Save(new MemoryStream(), ImageFormat.Tiff));
				Assert.ThrowsAny<Exception>(() => new Bitmap(source, 0, 2));
			}
			Assert.Equal(before, Live);
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void CreateAndForget(int count)
		{
			for (int i = 0; i < count; i++)
			{
				Bitmap b = new Bitmap(100, 100);
				Image d = Image.FromStream(new MemoryStream(SkiaPngOpaque()));
				GC.KeepAlive(b);
				GC.KeepAlive(d);
			}
		}

		[Fact]
		public void Images_that_are_never_disposed_are_released_by_the_finalizer()
		{
			int before = Live;
			CreateAndForget(20);
			Assert.True(Live >= before + 40 || Live == before, "the images were either still alive or already collected");
			for (int i = 0; i < 5 && Live != before; i++)
			{
				GC.Collect();
				GC.WaitForPendingFinalizers();
			}
			Assert.Equal(before, Live);
		}

		[Fact]
		public void Disposing_a_bitmap_with_an_outstanding_lock_does_not_crash_and_the_late_unlock_fails_cleanly()
		{
			int before = Live;
			Bitmap b = new Bitmap(4, 4, PixelFormat.Format24bppRgb);
			BitmapData data = b.LockBits(new Rectangle(0, 0, 4, 4), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
			b.Dispose();
			Assert.Equal(before, Live);
			// The converted buffer belongs to the BitmapData and is still readable.
			Assert.Equal(0, Marshal.ReadByte(data.Scan0));
			Assert.Throws<InvalidOperationException>(() => b.UnlockBits(data));
		}

		[Fact]
		public void Finalizing_balances_the_GC_memory_pressure()
		{
			// AddMemoryPressure/RemoveMemoryPressure must stay balanced, or RemoveMemoryPressure throws ArgumentOutOfRangeException.
			for (int i = 0; i < 500; i++)
			{
				using (Bitmap b = new Bitmap(257, 129))
				{
					b.RotateFlip(RotateFlipType.Rotate270FlipNone);
				}
			}
		}
	}
}
