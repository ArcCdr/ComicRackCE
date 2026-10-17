using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Host;
using ComicRack.Mac.Reader;
using ComicRack.Mac.Tests.Thumbnails;
using ComicRack.Mac.Thumbnails;
using cYo.Common.Collections;
using cYo.Common.Drawing;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;
using SkiaSharp;
using Xunit;

namespace ComicRack.Mac.Tests.Reader
{
	/// <summary>A host on a private folder (Core, page cache and database), opened and closed by the test.</summary>
	public sealed class CoreFixture : IDisposable
	{
		private readonly TempDir temp = new TempDir();
		private readonly RealLibraryGuard guard = new RealLibraryGuard();

		public CoreFixture(bool open = true)
		{
			if (open)
			{
				Open();
			}
		}

		public string Folder => temp.Path;

		public MacHost Host { get; private set; }

		public MacHost Open()
		{
			Host = MacHost.Open(new MacHostOptions { Paths = MacPaths.Under(temp.Path) });
			return Host;
		}

		public void CloseHost()
		{
			Host?.Dispose();
			Host = null;
		}

		/// <summary>A Core book for a file (not added to the library).</summary>
		public ComicBook Book(string file, CreateBookOption option = CreateBookOption.DoNotAdd)
		{
			ComicBook book = Host.DatabaseManager.BookFactory.Create(file, option, RefreshInfoOptions.GetPageCount);
			Assert.NotNull(book);
			return book;
		}

		public static string FirstFixtureBook(string folder)
		{
			return Directory.EnumerateFiles(folder, "*.cbz", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).First();
		}

		public void Dispose()
		{
			CloseHost();
			temp.Dispose();
			guard.Dispose();
		}
	}

	/// <summary>The reader's Core path on real files: pages through the navigator and the ImagePool, the read state written to the book, and the formats of M1.4.</summary>
	public class CoreReaderTests
	{
		private static string Small => Path.Combine(Fixtures.Root ?? "", "small");

		private static async Task<CoreReadingSession> OpenSession(CoreFixture core, ComicBook book)
		{
			var session = new CoreReadingSession(book, core.Host.ImagePool);
			await session.OpenAsync(CancellationToken.None);
			return session;
		}

		[FixtureFact("BD-reader")]
		public async Task A_cbz_opens_through_the_navigator_and_its_pages_decode_to_pixels_of_our_own()
		{
			using (var core = new CoreFixture())
			{
				string file = CoreFixture.FirstFixtureBook(Fixtures.BDReader);
				ComicBook book = core.Book(file);
				using (CoreReadingSession session = await OpenSession(core, book))
				{
					Assert.Equal(24, session.PageCount); // the stand-ins have 24 pages
					Assert.Equal(0, session.ResumePage);
					Assert.False(string.IsNullOrEmpty(session.Title));
					long live = PixelBlock.LiveCount;
					PixelBlock first = session.Decoder.Decode(0, CancellationToken.None);
					PixelBlock last = session.Decoder.Decode(23, CancellationToken.None);
					try
					{
						Assert.InRange(first.Width, 400, 900);
						Assert.InRange(first.Height, 600, 1000);
						Assert.Equal(SKAlphaType.Opaque, first.AlphaType); // JPEG page: opaque without a scan
						Assert.Equal(live + 2, PixelBlock.LiveCount);
					}
					finally
					{
						first.Release();
						last.Release();
					}
					Assert.Equal(live, PixelBlock.LiveCount);

					// a page that does not exist is an error, not a crash
					Assert.ThrowsAny<Exception>(() => session.Decoder.Decode(24, CancellationToken.None));
					Assert.ThrowsAny<Exception>(() => session.Decoder.Decode(-1, CancellationToken.None));
				}
			}
		}

		[FixtureFact("small")]
		public async Task Pages_of_all_three_codecs_decode_at_their_real_size()
		{
			using (var core = new CoreFixture())
			{
				ComicBook book = core.Book(Path.Combine(Small, "Fixture Series 01 001.cbz")); // pages alternate JPEG, PNG and WebP, 2 to 6 MP
				using (CoreReadingSession session = await OpenSession(core, book))
				{
					for (int page = 0; page < 6; page++)
					{
						PixelBlock block = session.Decoder.Decode(page, CancellationToken.None);
						try
						{
							long pixels = (long)block.Width * block.Height;
							Assert.InRange(pixels, 1_500_000, 7_000_000);
						}
						finally
						{
							block.Release();
						}
					}
				}
			}
		}

		[FixtureFact("pdf/test.pdf")]
		public async Task A_pdf_page_is_rendered_by_pdfium_and_decodes()
		{
			using (var core = new CoreFixture())
			{
				ComicBook book = core.Book(Path.Combine(Fixtures.Root, "pdf", "test.pdf"));
				using (CoreReadingSession session = await OpenSession(core, book))
				{
					Assert.Equal(3, session.PageCount);
					PixelBlock block = session.Decoder.Decode(1, CancellationToken.None);
					try
					{
						Assert.Equal(1920, block.Width); // the Core's PdfiumImageSize limit applied to an A5 page (M1.4)
						Assert.InRange(block.Height, 2600, 2800);
						using (SKImage image = block.CreateImage())
						{
							// the page is not blank: the generated PDF has a frame and panels
							SKColor corner = PageImages.PixelOf(image, 2, 2);
							SKColor middle = PageImages.PixelOf(image, block.Width / 2, block.Height / 2);
							Assert.True(corner != middle || corner != SKColors.White, "a blank page");
						}
					}
					finally
					{
						block.Release();
					}
				}
			}
		}

		[FixtureFact("cb7/test.cb7")]
		public async Task A_cb7_opens_and_its_pages_decode()
		{
			using (var core = new CoreFixture())
			{
				ComicBook book = core.Book(Path.Combine(Fixtures.Root, "cb7", "test.cb7"));
				using (CoreReadingSession session = await OpenSession(core, book))
				{
					Assert.Equal(24, session.PageCount);
					PixelBlock block = session.Decoder.Decode(1, CancellationToken.None);
					try
					{
						Assert.InRange((long)block.Width * block.Height, 1_000_000, 7_000_000);
					}
					finally
					{
						block.Release();
					}
				}
			}
		}

		[FixtureFact("BD-reader")]
		public async Task The_page_service_prefetches_real_pages_and_serves_them_from_memory()
		{
			using (var core = new CoreFixture())
			{
				ComicBook book = core.Book(CoreFixture.FirstFixtureBook(Fixtures.BDReader));
				using (CoreReadingSession session = await OpenSession(core, book))
				using (var pages = new PageService(session.Decoder, new PageServiceOptions { Post = a => a() }))
				{
					pages.SetWindow(new[] { 5, 6, 7, 4 });
					var watch = System.Diagnostics.Stopwatch.StartNew();
					while (pages.GetStats().CachedCount < 4)
					{
						Assert.True(watch.ElapsedMilliseconds < 20000, "prefetch did not finish");
						await Task.Delay(5);
					}
					foreach (int page in new[] { 4, 5, 6, 7 })
					{
						CoverImage image = pages.TryGetCached(page);
						Assert.NotNull(image);
						Assert.InRange(image.Width, 400, 900);
						image.Release();
					}
					Assert.Equal(4, pages.GetStats().DecodesCompleted);
				}
			}
		}

		[FixtureFact("BD-reader")]
		public async Task Opening_counts_the_open_sets_the_time_and_every_page_is_written_to_the_book()
		{
			using (var core = new CoreFixture())
			{
				ComicBook book = core.Book(CoreFixture.FirstFixtureBook(Fixtures.BDReader), CreateBookOption.AddToStorage);
				Assert.Equal(0, book.OpenedCount);
				Assert.Equal(DateTime.MinValue, book.OpenedTime);
				using (CoreReadingSession session = await OpenSession(core, book))
				{
					Assert.Equal(1, book.OpenedCount);
					Assert.NotEqual(DateTime.MinValue, book.OpenedTime);
					Assert.True((DateTime.Now - book.OpenedTime).TotalMinutes < 5);
					session.ReportPage(7);
					Assert.Equal(7, book.CurrentPage);
					Assert.Equal(7, book.LastPageRead);
					session.ReportPage(12);
					session.ReportPage(9); // back: the current page follows, the furthest page stays
					Assert.Equal(9, book.CurrentPage);
					Assert.Equal(12, book.LastPageRead);
				}
			}
		}

		[FixtureFact("BD-reader")]
		public async Task Reopening_resumes_where_the_reader_was_left_also_after_the_library_was_saved_and_loaded_again()
		{
			using (var core = new CoreFixture())
			{
				string file = CoreFixture.FirstFixtureBook(Fixtures.BDReader);
				ComicBook book = core.Book(file, CreateBookOption.AddToStorage);
				using (CoreReadingSession first = await OpenSession(core, book))
				{
					Assert.Equal(0, first.ResumePage);
					first.ReportPage(14);
					first.ReportPage(10);
				}
				using (CoreReadingSession second = await OpenSession(core, book))
				{
					Assert.Equal(10, second.ResumePage); // where it was left
					Assert.Equal(2, book.OpenedCount);
				}

				// quit and relaunch: the library file keeps the page, the furthest page and the count
				core.CloseHost();
				core.Open();
				ComicBook loaded = core.Host.DatabaseManager.Database.Books.Lock().Single(b => string.Equals(b.FilePath, file, StringComparison.Ordinal));
				Assert.Equal(10, loaded.CurrentPage);
				Assert.Equal(14, loaded.LastPageRead);
				Assert.Equal(2, loaded.OpenedCount);
				Assert.NotEqual(DateTime.MinValue, loaded.OpenedTime);
				using (CoreReadingSession third = await OpenSession(core, loaded))
				{
					Assert.Equal(10, third.ResumePage);
					Assert.Equal(3, loaded.OpenedCount);
				}
			}
		}

		[FixtureFact("BD-reader")]
		public async Task A_book_whose_last_page_is_beyond_its_end_resumes_on_the_last_page()
		{
			using (var core = new CoreFixture())
			{
				ComicBook book = core.Book(CoreFixture.FirstFixtureBook(Fixtures.BDReader));
				book.CurrentPage = 400; // the file got shorter, or the library was edited
				using (CoreReadingSession session = await OpenSession(core, book))
				{
					Assert.Equal(session.PageCount - 1, session.ResumePage);
				}
			}
		}

		[FixtureFact("BD-reader")]
		public async Task With_page_tracking_off_nothing_is_written_to_the_book()
		{
			bool before = ComicBookNavigator.TrackCurrentPage;
			ComicBookNavigator.TrackCurrentPage = false;
			try
			{
				using (var core = new CoreFixture())
				{
					ComicBook book = core.Book(CoreFixture.FirstFixtureBook(Fixtures.BDReader));
					using (CoreReadingSession session = await OpenSession(core, book))
					{
						session.ReportPage(5);
						Assert.Equal(0, book.CurrentPage);
						Assert.Equal(0, book.LastPageRead);
						Assert.Equal(DateTime.MinValue, book.OpenedTime);
					}
				}
			}
			finally
			{
				ComicBookNavigator.TrackCurrentPage = before;
			}
		}

		[Fact]
		public async Task A_missing_file_and_a_file_that_is_not_a_comic_fail_to_open_with_a_clear_message()
		{
			using (var core = new CoreFixture())
			using (var temp = new TempDir())
			{
				var missing = new ComicBook { FilePath = temp.Combine("gone.cbz") };
				var session = new CoreReadingSession(missing, core.Host.ImagePool);
				Exception ex = await Assert.ThrowsAnyAsync<Exception>(() => session.OpenAsync(CancellationToken.None));
				Assert.Contains("gone.cbz", ex.Message);
				session.Dispose();

				string junk = temp.Combine("junk.cbz");
				File.WriteAllText(junk, "this is not a zip file");
				var notComic = new ComicBook { FilePath = junk };
				session = new CoreReadingSession(notComic, core.Host.ImagePool);
				ex = await Assert.ThrowsAnyAsync<Exception>(() => session.OpenAsync(CancellationToken.None));
				Assert.Contains("junk.cbz", ex.Message);
				session.Dispose();
				Assert.Equal(0, notComic.OpenedCount); // a book that did not open is not counted
			}
		}

		[Fact]
		public async Task Cancelling_the_open_stops_it_and_disposes_the_navigator()
		{
			using (var core = new CoreFixture())
			using (var temp = new TempDir())
			{
				string file = MakeCbz(temp.Combine("c.cbz"), 3);
				ComicBook book = core.Book(file);
				var session = new CoreReadingSession(book, core.Host.ImagePool);
				using (var cts = new CancellationTokenSource())
				{
					cts.Cancel();
					await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.OpenAsync(cts.Token));
				}
				session.Dispose();
				Assert.Equal(0, book.OpenedCount);
			}
		}

		[Fact]
		public async Task A_book_of_one_page_is_marked_as_read_when_the_reader_goes_past_its_end()
		{
			using (var core = new CoreFixture())
			using (var temp = new TempDir())
			{
				string file = MakeCbz(temp.Combine("one.cbz"), 1);
				ComicBook book = core.Book(file);
				using (CoreReadingSession session = await OpenSession(core, book))
				{
					Assert.Equal(1, session.PageCount);
					Assert.Equal(0, book.ReadPercentage);
					session.ReportEndReached();
					Assert.Equal(100, book.ReadPercentage);
				}
				// a longer book is not touched by it
				string longer = MakeCbz(temp.Combine("three.cbz"), 3);
				ComicBook three = core.Book(longer);
				using (CoreReadingSession session = await OpenSession(core, three))
				{
					session.ReportEndReached();
					Assert.Equal(0, three.LastPageRead);
				}
			}
		}

		[Fact]
		public async Task A_page_that_the_core_rotated_is_copied_from_its_bitmap_not_decoded_from_the_lossy_cache_bytes()
		{
			using (var core = new CoreFixture())
			using (var temp = new TempDir())
			{
				string file = MakeCbz(temp.Combine("rot.cbz"), 3, width: 300, height: 200);
				ComicBook book = core.Book(file);
				book.UpdatePageRotation(1, ImageRotation.Rotate90);
				using (CoreReadingSession session = await OpenSession(core, book))
				{
					PixelBlock plain = session.Decoder.Decode(0, CancellationToken.None);
					PixelBlock rotated = session.Decoder.Decode(1, CancellationToken.None);
					try
					{
						Assert.Equal(300, plain.Width);
						Assert.Equal(200, plain.Height);
						Assert.Equal(200, rotated.Width); // the Core rotated it
						Assert.Equal(300, rotated.Height);
					}
					finally
					{
						plain.Release();
						rotated.Release();
					}
				}
			}
		}

		[Fact]
		public async Task Decoding_never_touches_memory_the_core_has_released()
		{
			using (var core = new CoreFixture())
			using (var temp = new TempDir())
			{
				string file = MakeCbz(temp.Combine("many.cbz"), 12, width: 400, height: 600);
				ComicBook book = core.Book(file);
				using (CoreReadingSession session = await OpenSession(core, book))
				{
					var blocks = new PixelBlock[12];
					// Decode every page while the Core's page cache (5 pages) evicts and disposes its own images underneath.
					Parallel.For(0, 12, new ParallelOptions { MaxDegreeOfParallelism = 4 }, page =>
					{
						blocks[page] = session.Decoder.Decode(page, CancellationToken.None);
					});
					foreach (PixelBlock block in blocks)
					{
						Assert.Equal(400, block.Width);
						using (SKImage image = block.CreateImage())
						{
							Assert.True(PageImages.PixelOf(image, 200, 300).Alpha == 255);
						}
						block.Release();
					}
				}
			}
		}

		/// <summary>A CBZ of generated JPEG pages (gradient pages, 'page' in the file name).</summary>
		private static string MakeCbz(string path, int pages, int width = 300, int height = 400)
		{
			using (FileStream stream = File.Create(path))
			using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
			{
				for (int i = 0; i < pages; i++)
				{
					ZipArchiveEntry entry = zip.CreateEntry((i + 1).ToString("000") + ".jpg", CompressionLevel.NoCompression);
					using (Stream s = entry.Open())
					{
						byte[] jpeg = PageImages.Jpeg(width, height);
						s.Write(jpeg, 0, jpeg.Length);
					}
				}
			}
			return path;
		}
	}
}
