#pragma warning disable xUnit1031 // The tests pump a fake UI thread first, then read tasks that are already completed (or use a bounded wait).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComicRack.Mac.Host;
using ComicRack.Mac.Services;
using ComicRack.Mac.Thumbnails;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace ComicRack.Mac.Tests.Thumbnails
{
	/// <summary>The service with the real decoder: the Core's caches and the generated fixture books (mac/fixtures/BD, git-ignored).</summary>
	public class CoreThumbnailDecoderTests
	{
		private readonly ITestOutputHelper output;

		public CoreThumbnailDecoderTests(ITestOutputHelper output)
		{
			this.output = output;
		}

		/// <summary>A host on private folders, a service on its cache manager, and a stand-in for the UI thread.</summary>
		private sealed class Env : IDisposable
		{
			private readonly TempDir temp = new TempDir();
			private readonly List<SKImage> images = new List<SKImage>();
			public readonly MacHost Host;
			public readonly FakeUi Ui = new FakeUi();
			public readonly ThumbnailService Service;
			public readonly List<string> Log = new List<string>();

			public Env(long maxBytes = 64L * 1024 * 1024, int concurrency = 4)
			{
				Host = MacHost.Open(new MacHostOptions { Paths = MacPaths.Under(temp.Combine("app")), DatabaseReadOnly = true });
				try
				{
					Service = new ThumbnailService(new ThumbnailServiceOptions
					{
						Decoder = new CoreThumbnailDecoder(Host.CacheManager),
						Post = Ui.Post,
						MaxCacheBytes = maxBytes,
						MaxConcurrency = concurrency,
						Log = message => { lock (Log) { Log.Add(message); } },
					});
				}
				catch (Exception)
				{
					Host.Dispose();
					temp.Dispose();
					throw;
				}
			}

			public string Temp(string name) => temp.Combine(name);

			public ComicBook OpenBook(string file)
			{
				ComicBook book = Host.DatabaseManager.BookFactory.Create(file, CreateBookOption.DoNotAdd, RefreshInfoOptions.GetPageCount);
				Assert.NotNull(book);
				book.RefreshInfoFromFile(RefreshInfoOptions.GetPageCount);
				return book;
			}

			public ThumbnailResult Get(ComicBook book, int size)
			{
				Task<ThumbnailResult> task = Service.RequestResult(book, size, CancellationToken.None);
				Ui.PumpUntil(() => task.IsCompleted, 60000);
				ThumbnailResult result = task.GetAwaiter().GetResult();
				images.Add(result.Image);
				return result;
			}

			public void Keep(SKImage image) => images.Add(image);

			public void Dispose()
			{
				try
				{
					Service.Dispose(); // first: the decoder must not read from a host that is closing
					Ui.Pump();
					foreach (SKImage image in images)
					{
						image.Dispose();
					}
				}
				finally
				{
					// whatever failed above, the host must close: the Core allows one per process
					Host.Dispose();
					temp.Dispose();
				}
			}
		}

		private static string[] FixtureBooks(int count)
		{
			return Directory.EnumerateFiles(Fixtures.BD, "*.cbz", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).Take(count).ToArray();
		}

		[FixtureFact("BD")]
		public void Fixture_covers_come_out_as_opaque_images_at_the_requested_step_and_fill_the_cores_cache()
		{
			using (new RealLibraryGuard())
			using (var env = new Env())
			{
				long cacheSize0 = env.Host.ImagePool.Thumbs.MemoryCache.Size;
				foreach (string file in FixtureBooks(6))
				{
					ComicBook book = env.OpenBook(file);
					var watch = Stopwatch.StartNew();
					ThumbnailResult result = env.Get(book, 300);
					long firstMs = watch.ElapsedMilliseconds;
					Assert.False(result.IsPlaceholder, file + ": " + result.Error);
					Assert.Equal(384, result.Image.Height); // 300 px is served at the 384 step
					Assert.InRange(result.Image.Width, 150, 400);
					Assert.Equal(SKAlphaType.Opaque, result.Image.AlphaType);
					using (SKPixmap pixmap = result.Image.PeekPixels())
					{
						Assert.NotNull(pixmap);
						var distinct = new HashSet<uint>();
						for (int y = 0; y < pixmap.Height; y += 16)
						{
							for (int x = 0; x < pixmap.Width; x += 16)
							{
								distinct.Add((uint)pixmap.GetPixelColor(x, y));
							}
						}
						Assert.True(distinct.Count > 5, "the cover looks flat: " + file);
					}
					Assert.True(env.Host.ImagePool.Thumbs.MemoryCache.IsCached(book.GetFrontCoverThumbnailKey()), "the Core cached the thumbnail");

					// Second size of the same book: the Core's memory cache answers, only the Skia decode is left.
					env.Service.Clear();
					watch.Restart();
					ThumbnailResult smaller = env.Get(book, 150);
					output.WriteLine($"{Path.GetFileName(file)}: first {firstMs} ms ({result.Image.Width}x{result.Image.Height}), from the Core cache {watch.ElapsedMilliseconds} ms ({smaller.Image.Width}x{smaller.Image.Height})");
					Assert.Equal(162, smaller.Image.Height);
					Assert.False(smaller.IsPlaceholder);
				}
				// The Core decodes a 512 px bitmap of every ThumbnailImage it makes or saves (and keeps it for 15 s). We only need the stored
				// bytes, so the decoder lets that bitmap go at once; otherwise a fast scroll would hold hundreds of megabytes in the Core.
				var cache = env.Host.ImagePool.Thumbs.MemoryCache;
				Assert.True(cache.Size > cacheSize0);
				Assert.Equal(6, cache.GetKeys().Length);
				foreach (var key in cache.GetKeys())
				{
					using (var item = cache.LockItem(key, dontUpdateAccess: true))
					{
						Assert.NotNull(item);
						Assert.False(item.Item.IsImage, "the Core still holds a decoded bitmap of " + key.Location);
					}
				}
				Assert.Equal(0, env.Service.GetStats().Failures);
			}
		}

		[Fact]
		public void A_missing_file_gives_a_placeholder_and_no_error_in_the_log()
		{
			using (new RealLibraryGuard())
			using (var env = new Env())
			{
				int errors = AppLog.ErrorCount;
				string file = env.Temp("nowhere/Gone.cbz");
				ComicBook book = new ComicBook { FilePath = file };
				ThumbnailResult result = env.Get(book, 200);
				Assert.True(result.IsPlaceholder);
				FileNotFoundException error = Assert.IsType<FileNotFoundException>(result.Error);
				Assert.Contains("missing", error.Message);
				Assert.Contains(file, error.Message);
				Assert.Equal(216, result.Image.Height);
				Assert.Equal(errors, AppLog.ErrorCount); // a missing book is not an error of the app
				lock (env.Log)
				{
					Assert.Single(env.Log);
					Assert.Contains("Gone.cbz", env.Log[0]);
				}
			}
		}

		[Fact]
		public void A_damaged_archive_gives_a_placeholder()
		{
			using (new RealLibraryGuard())
			using (var env = new Env())
			{
				string file = env.Temp("Damaged.cbz");
				var junk = new byte[5000];
				new Random(7).NextBytes(junk);
				File.WriteAllBytes(file, junk);
				ThumbnailResult result = env.Get(new ComicBook { FilePath = file }, 200);
				Assert.True(result.IsPlaceholder);
				Assert.NotNull(result.Error);
				Assert.IsNotType<FileNotFoundException>(result.Error);

				string empty = env.Temp("Empty.cbz");
				File.WriteAllBytes(empty, new byte[0]);
				Assert.True(env.Get(new ComicBook { FilePath = empty }, 200).IsPlaceholder);
			}
		}

		[Fact]
		public void A_book_without_a_file_shows_the_cores_unknown_cover()
		{
			using (new RealLibraryGuard())
			using (var env = new Env())
			{
				ThumbnailResult result = env.Get(new ComicBook(), 512);
				Assert.False(result.IsPlaceholder, "" + result.Error);
				Assert.Equal(512, result.Image.Height);
				Assert.InRange(result.Image.Width, 300, 400);
			}
		}

		[FixtureFact("BD")]
		public void Cancelling_most_of_a_scroll_leaves_most_of_it_undecoded()
		{
			using (new RealLibraryGuard())
			using (var env = new Env())
			{
				string[] files = FixtureBooks(120);
				ComicBook[] books = files.Select(env.OpenBook).ToArray();
				var cts = new CancellationTokenSource();
				var tasks = new List<Task<ThumbnailResult>>();
				for (int i = 0; i < books.Length; i++)
				{
					bool keep = i < 8; // the first eight stay on screen; all the others scroll away at once
					tasks.Add(env.Service.RequestResult(books[i], 200, keep ? CancellationToken.None : cts.Token));
				}
				cts.Cancel();
				env.Ui.PumpUntil(() => tasks.All(t => t.IsCompleted), 60000);
				for (int i = 0; i < tasks.Count; i++)
				{
					if (i < 8)
					{
						ThumbnailResult r = tasks[i].GetAwaiter().GetResult();
						env.Keep(r.Image);
						Assert.False(r.IsPlaceholder, "" + r.Error);
					}
					else
					{
						Assert.True(tasks[i].IsCanceled, "book " + i);
					}
				}
				ThumbnailServiceStats stats = env.Service.GetStats();
				output.WriteLine($"started {stats.DecodesStarted}, dropped from the queue {stats.DroppedFromQueue}, cancelled while running {stats.DecodesCancelled}, peak in flight {stats.PeakInFlight}");
				Assert.True(stats.DecodesStarted <= 8 + 4 + 4, "decoded " + stats.DecodesStarted); // the eight, plus what was running when the others left
				Assert.True(stats.DroppedFromQueue >= 100, "dropped " + stats.DroppedFromQueue);
				Assert.True(stats.PeakInFlight <= 4);
			}
		}

		[FixtureFact("BD")]
		public void A_long_scroll_stays_within_the_cache_bound()
		{
			const long bound = 4L * 1024 * 1024;
			using (new RealLibraryGuard())
			using (var env = new Env(maxBytes: bound))
			{
				Gc.Flush();
				long liveBytes0 = PixelBlock.LiveBytes;
				ComicBook[] books = FixtureBooks(150).Select(env.OpenBook).ToArray();
				var watch = Stopwatch.StartNew();
				var tasks = books.Select(b => env.Service.RequestResult(b, 200, CancellationToken.None)).ToArray();
				env.Ui.PumpUntil(() => tasks.All(t => t.IsCompleted), 120000);
				long ms = watch.ElapsedMilliseconds;
				foreach (Task<ThumbnailResult> t in tasks)
				{
					Assert.False(t.Result.IsPlaceholder, "" + t.Result.Error);
					t.Result.Image.Dispose(); // the grid drops the cell's image when it is recycled
				}
				ThumbnailServiceStats stats = env.Service.GetStats();
				output.WriteLine($"{books.Length} covers in {ms} ms ({ms * 1.0 / books.Length:F1} ms each, 4 workers, first run: the Core made the thumbnails); cached {stats.CachedCount} images, {stats.CachedBytes} bytes");
				Assert.True(stats.CachedBytes <= bound);
				Assert.True(stats.CachedCount < books.Length);
				Assert.True(PixelBlock.LiveBytes - liveBytes0 <= bound, "pixel memory " + (PixelBlock.LiveBytes - liveBytes0));

				// Second pass: the Core has the thumbnails in memory now, only the Skia decode is left.
				watch.Restart();
				var again = books.Select(b => env.Service.RequestResult(b, 200, CancellationToken.None)).ToArray();
				env.Ui.PumpUntil(() => again.All(t => t.IsCompleted), 120000);
				output.WriteLine($"second pass, {books.Length} covers in {watch.ElapsedMilliseconds} ms (Core cache hits, Skia decode)");
				foreach (Task<ThumbnailResult> t in again)
				{
					t.Result.Image.Dispose();
				}
				Assert.True(env.Service.GetStats().CachedBytes <= bound);
			}
		}
	}
}
