// xUnit1031: the lock test blocks a thread on purpose.
#pragma warning disable xUnit1031
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComicRack.Mac.Reader;
using ComicRack.Mac.Tests.Thumbnails;
using ComicRack.Mac.Thumbnails;
using cYo.Common.Threading;
using SkiaSharp;
using Xunit;

namespace ComicRack.Mac.Tests.Reader
{
	/// <summary>Decoding the bytes of a page, and copying a bitmap of the Core: pixel formats, the size cap, damaged data, and the Core's lock.</summary>
	public class PagePixelsTests : IDisposable
	{
		private readonly long liveBefore;

		public PagePixelsTests()
		{
			liveBefore = Gc.BeginCount();
		}

		public void Dispose()
		{
			Gc.WaitForLive(liveBefore);
			Assert.Equal(liveBefore, Gc.Live);
		}

		[Fact]
		public void A_jpeg_is_decoded_opaque_at_its_size()
		{
			PixelBlock block = PagePixels.Decode(PageImages.Jpeg(800, 1200));
			try
			{
				Assert.Equal(800, block.Width);
				Assert.Equal(1200, block.Height);
				Assert.Equal(SKAlphaType.Opaque, block.AlphaType);
				Assert.Equal(800L * 1200 * 4, block.ByteCount);
			}
			finally
			{
				block.Release();
			}
		}

		[Fact]
		public void A_png_without_alpha_is_opaque_and_one_with_alpha_stays_premultiplied()
		{
			PixelBlock opaque = PagePixels.Decode(PageImages.Png(40, 60, new SKColor(10, 20, 30), withAlpha: false));
			PixelBlock translucent = PagePixels.Decode(PageImages.Png(40, 60, new SKColor(200, 100, 50, 128), withAlpha: true));
			try
			{
				Assert.Equal(SKAlphaType.Opaque, opaque.AlphaType);
				Assert.Equal(SKAlphaType.Premul, translucent.AlphaType);
				using (SKImage image = translucent.CreateImage())
				{
					Assert.InRange((int)PageImages.PixelOf(image, 5, 5).Alpha, 126, 130);
				}
			}
			finally
			{
				opaque.Release();
				translucent.Release();
			}
		}

		[Fact]
		public void A_webp_is_decoded()
		{
			PixelBlock block = PagePixels.Decode(PageImages.Webp(90, 120, new SKColor(40, 120, 200)));
			try
			{
				Assert.Equal(90, block.Width);
				Assert.Equal(120, block.Height);
				using (SKImage image = block.CreateImage())
				{
					Assert.True(PageImages.Near(PageImages.PixelOf(image, 10, 10), new SKColor(40, 120, 200), 6));
				}
			}
			finally
			{
				block.Release();
			}
		}

		[Theory]
		[InlineData(0)]
		[InlineData(10)]
		public void Nothing_that_is_not_an_image_is_accepted(int length)
		{
			byte[] junk = Enumerable.Range(0, length).Select(i => (byte)(i * 31 + 7)).ToArray();
			Assert.Throws<InvalidDataException>(() => PagePixels.Decode(junk));
			Assert.Throws<InvalidDataException>(() => PagePixels.Decode(null));
		}

		[Fact]
		public void A_jpeg_that_is_cut_short_shows_what_there_is()
		{
			byte[] jpeg = PageImages.Jpeg(600, 900);
			byte[] cut = jpeg.Take(jpeg.Length * 6 / 10).ToArray();
			PixelBlock block = PagePixels.Decode(cut);
			try
			{
				Assert.Equal(600, block.Width);
				Assert.Equal(900, block.Height);
			}
			finally
			{
				block.Release();
			}
		}

		[Fact]
		public void A_page_above_the_pixel_cap_is_reduced_to_fit_it_and_keeps_its_proportions()
		{
			byte[] jpeg = PageImages.Jpeg(2000, 3000); // 6 MP
			PixelBlock block = PagePixels.Decode(jpeg, maxPixels: 1_000_000);
			try
			{
				Assert.True((long)block.Width * block.Height <= 1_000_000, block.Width + " x " + block.Height);
				Assert.True((long)block.Width * block.Height >= 400_000, "not reduced much more than needed: " + block.Width + " x " + block.Height);
				Assert.Equal(2.0 / 3, (double)block.Width / block.Height, 2);
			}
			finally
			{
				block.Release();
			}
			PixelBlock whole = PagePixels.Decode(jpeg, maxPixels: 0); // 0 = no cap
			try
			{
				Assert.Equal(2000, whole.Width);
			}
			finally
			{
				whole.Release();
			}
		}

		[Fact]
		public void A_png_above_the_cap_is_reduced_after_the_decode()
		{
			byte[] png = PageImages.Png(1500, 2000, new SKColor(90, 90, 90), withAlpha: false);
			PixelBlock block = PagePixels.Decode(png, maxPixels: 500_000);
			try
			{
				Assert.True((long)block.Width * block.Height <= 500_000);
				Assert.True(block.Width > 300);
			}
			finally
			{
				block.Release();
			}
		}

		[Fact]
		public void A_cancelled_token_stops_the_decode_without_leaking_the_bitmap()
		{
			using (var cts = new CancellationTokenSource())
			{
				cts.Cancel();
				Assert.Throws<OperationCanceledException>(() => PagePixels.Decode(PageImages.Jpeg(300, 300), PagePixels.DefaultMaxPixels, cts.Token));
			}
		}

		[Fact]
		public void A_copy_of_a_core_bitmap_does_not_depend_on_it()
		{
			var source = new System.Drawing.Bitmap(32, 24, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
			using (var g = System.Drawing.Graphics.FromImage(source))
			{
				g.Clear(System.Drawing.Color.FromArgb(255, 200, 100, 50));
			}
			PixelBlock copy = PagePixels.CopyFrom(source);
			try
			{
				source.Dispose(); // the Core disposes its bitmaps whenever it likes
				Assert.Equal(32, copy.Width);
				Assert.Equal(24, copy.Height);
				Assert.Equal(SKAlphaType.Opaque, copy.AlphaType); // Format32bppArgb is Unpremul in the shim, but every alpha byte is 255
				using (SKImage image = copy.CreateImage())
				{
					Assert.True(PageImages.Near(PageImages.PixelOf(image, 3, 3), new SKColor(200, 100, 50)));
				}
			}
			finally
			{
				copy.Release();
			}
		}

		[Fact]
		public void Copying_a_bitmap_that_the_core_already_disposed_throws_object_disposed()
		{
			var source = new System.Drawing.Bitmap(8, 8);
			source.Dispose();
			Assert.Throws<ObjectDisposedException>(() => PagePixels.CopyFrom(source));
		}

		[Fact]
		public void The_copy_waits_for_the_lock_that_the_core_holds_while_it_uses_a_bitmap()
		{
			var source = new System.Drawing.Bitmap(16, 16);
			using (var held = new ManualResetEventSlim())
			using (var release = new ManualResetEventSlim())
			{
				// What the Core does while it painted or released a bitmap: ItemMonitor.Lock(bitmap).
				Task holder = Task.Run(() =>
				{
					using (ItemMonitor.Lock(source))
					{
						held.Set();
						release.Wait();
					}
				});
				held.Wait();
				Task<PixelBlock> copying = Task.Run(() => PagePixels.CopyFrom(source));
				Assert.False(copying.Wait(150), "the copy must wait for the Core's lock");
				release.Set();
				holder.Wait();
				Assert.True(copying.Wait(5000));
				copying.Result.Release();
			}
			source.Dispose();
		}
	}
}
