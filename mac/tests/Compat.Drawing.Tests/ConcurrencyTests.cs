using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Xunit;
using static Compat.Drawing.Tests.TestImages;

namespace Compat.Drawing.Tests
{
	/// <summary>The Core decodes, converts and saves pages on many threads at once.</summary>
	public class ConcurrencyTests
	{
		[Fact]
		public void Independent_bitmaps_can_be_used_from_many_threads()
		{
			int before = LiveSettled();
			byte[] jpeg = SkiaJpeg(300, 200);
			Parallel.For(0, 64, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
			{
				using (Bitmap decoded = (Bitmap)Image.FromStream(new MemoryStream(jpeg)))
				using (Bitmap copy = decoded.Clone(new Rectangle(0, 0, 300, 200), PixelFormat.Format32bppArgb))
				{
					BitmapData data = copy.LockBits(new Rectangle(0, 0, 300, 200), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
					Marshal.WriteByte(data.Scan0, 0, (byte)i);
					copy.UnlockBits(data);
					Assert.Equal(i, copy.GetPixel(0, 0).B);
					copy.RotateFlip(RotateFlipType.Rotate90FlipNone);
					using (MemoryStream stream = new MemoryStream())
					{
						copy.Save(stream, ImageFormat.Png);
						Assert.True(stream.Length > 100);
					}
				}
			});
			Assert.Equal(before, Live);
		}

		[Fact]
		public void One_bitmap_can_be_read_and_cropped_by_many_threads_and_locked_in_disjoint_bands()
		{
			using (Bitmap shared = GradientBitmap(200, 160, PixelFormat.Format24bppRgb))
			{
				Parallel.For(0, 160 / 10, new ParallelOptions { MaxDegreeOfParallelism = 8 }, band =>
				{
					Rectangle rect = new Rectangle(0, band * 10, 200, 10);
					BitmapData data = shared.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
					try
					{
						Marshal.WriteByte(data.Scan0, 2, 77);
					}
					finally
					{
						shared.UnlockBits(data);
					}
					using (Bitmap part = shared.Clone(new Rectangle(1, band * 10, 5, 5), PixelFormat.Format24bppRgb))
					{
						Assert.Equal(new Size(5, 5), part.Size);
					}
				});
				for (int band = 0; band < 16; band++)
				{
					Assert.Equal(77, shared.GetPixel(0, band * 10).R);
				}
			}
		}

		[Fact]
		public async Task Locking_the_same_region_from_two_threads_fails_for_one_of_them_instead_of_corrupting()
		{
			using (Bitmap shared = new Bitmap(50, 50))
			{
				int failures = 0;
				int successes = 0;
				using (System.Threading.Barrier barrier = new System.Threading.Barrier(2))
				{
					Task[] tasks = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
					{
						barrier.SignalAndWait();
						try
						{
							BitmapData data = shared.LockBits(new Rectangle(0, 0, 50, 50), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
							System.Threading.Thread.Sleep(100);
							shared.UnlockBits(data);
							System.Threading.Interlocked.Increment(ref successes);
						}
						catch (InvalidOperationException)
						{
							System.Threading.Interlocked.Increment(ref failures);
						}
					})).ToArray();
					await Task.WhenAll(tasks);
				}
				Assert.Equal(1, successes);
				Assert.Equal(1, failures);
			}
		}
	}
}
