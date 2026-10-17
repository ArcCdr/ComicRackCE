using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using cYo.Common.Drawing;
using cYo.Common.Threading;
using cYo.Projects.ComicRack.Engine.IO;
using cYo.Projects.ComicRack.Engine.IO.Provider;
using Xunit;

namespace ComicRack.Mac.Host.Tests
{
	/// <summary>Run-time gaps of the Core on .NET 10 that compiling does not show (BinaryFormatter, Thread.Abort, the error page).</summary>
	public class CoreRuntimeTests
	{
		[Fact]
		public void CreateErrorPage_returns_the_picture_without_text()
		{
			using (var temp = new TempFolder())
			using (var host = MacHost.Open(new MacHostOptions { Paths = temp.Paths }))
			using (Bitmap page = host.ImagePool.CreateErrorPage())
			{
				Assert.Equal(1000, page.Width);
				Assert.Equal(1402, page.Height);
			}
		}

		[Fact]
		public void CreateErrorThumbnail_works()
		{
			using (var temp = new TempFolder())
			using (var host = MacHost.Open(new MacHostOptions { Paths = temp.Paths }))
			using (Bitmap thumb = host.ImagePool.CreateErrorThumbnail(300))
			{
				Assert.Equal(200, thumb.Width);
				Assert.Equal(300, thumb.Height);
			}
		}

		[Fact]
		public void A_page_that_cannot_be_loaded_gives_the_error_page()
		{
			using (var temp = new TempFolder())
			using (var host = MacHost.Open(new MacHostOptions { Paths = temp.Paths }))
			{
				var key = new PageKey(null, Path.Combine(temp.Path, "missing.cbz"), 0, DateTime.UtcNow, 0, ImageRotation.None, BitmapAdjustment.Empty);
				using (var item = host.ImagePool.GetPage(key, (IImageProvider)null, onErrorThrowException: false))
				{
					Assert.NotNull(item);
					Assert.Equal(1000, item.Item.Width);
					Assert.Equal(1402, item.Item.Height);
				}
			}
		}

		[Fact]
		public void The_disk_cache_index_survives_a_close_although_BinaryFormatter_is_gone()
		{
			// DiskCache writes its index as XML and, in addition, with BinaryFormatter, which throws on .NET 10 (inside try/catch).
			// The XML index is the one that matters: an item added in one session is found in the next.
			using (var temp = new TempFolder())
			{
				var key = new PageKey(null, "/books/a.cbz", 1234, new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc), 3, ImageRotation.None, BitmapAdjustment.Empty);
				using (var host = MacHost.Open(new MacHostOptions { Paths = temp.Paths }))
				using (var bmp = new Bitmap(30, 40))
				using (var page = PageImage.CreateFrom(bmp))
				{
					Assert.False(host.ImagePool.Pages.DiskCache.IsAvailable(key));
					Assert.True(host.ImagePool.Pages.DiskCache.AddItem(key, page));
				}
				string images = Path.Combine(temp.Paths.CacheFolder, "Images");
				Assert.True(new FileInfo(Path.Combine(images, "cache.idx.xml")).Length > 0);
				Assert.Equal(0, new FileInfo(Path.Combine(images, "cache.idx")).Length); // File.Create ran, then Serialize threw
				using (var host = MacHost.Open(new MacHostOptions { Paths = temp.Paths }))
				{
					Assert.True(host.ImagePool.Pages.DiskCache.IsAvailable(key));
					using (PageImage page = host.ImagePool.Pages.DiskCache.GetItem(key))
					{
						Assert.NotNull(page);
						Assert.Equal(30, page.Width);
					}
				}
			}
		}

		[Fact]
		public void Thread_Abort_throws_PlatformNotSupportedException_on_net10()
		{
			// The Core aborts a worker only after a Join timeout (ThreadUtility.Abort, BackgroundRunner.Dispose, ComicScanner.Stop).
			// On .NET 10 that call throws instead of ending the thread; MacHost logs it and goes on with the other close steps.
			var release = new ManualResetEventSlim(false);
			var thread = new Thread(() => release.Wait()) { IsBackground = true };
			thread.Start();
			try
			{
				Assert.Throws<PlatformNotSupportedException>(() => ThreadUtility.Abort(thread, 50));
			}
			finally
			{
				release.Set();
				thread.Join();
			}
		}
	}
}
