using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using ComicRack.Mac.Reader;
using ComicRack.Mac.ViewModels;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Display;
using SkiaSharp;
using Xunit;

namespace ComicRack.Mac.Tests.Reader
{
	/// <summary>The two-page reader on a real CBZ through the Core: the manga flag, the page types, the sizes the Core learns, the pairing around a wide page, and what is written to the book.</summary>
	public class CoreSpreadTests
	{
		private static string MakeCbz(string path, int pages, Func<int, (int, int)> size)
		{
			using (FileStream stream = File.Create(path))
			using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
			{
				for (int i = 0; i < pages; i++)
				{
					(int w, int h) = size(i);
					ZipArchiveEntry entry = zip.CreateEntry((i + 1).ToString("000") + ".jpg", CompressionLevel.NoCompression);
					using (var bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque)))
					{
						bitmap.Erase(FakePageDecoder.ColorFor(i));
						using (var canvas = new SKCanvas(bitmap))
						using (var paint = new SKPaint { IsAntialias = true, Color = SKColors.White })
						using (var font = new SKFont(SKTypeface.FromFamilyName("Helvetica", SKFontStyle.Bold), Math.Min(w, h) * 0.4f))
						{
							canvas.DrawText((i + 1).ToString(), w / 2f, h / 2f + font.Size * 0.35f, SKTextAlign.Center, font, paint);
						}
						using (SKImage image = SKImage.FromBitmap(bitmap))
						using (SKData data = image.Encode(SKEncodedImageFormat.Jpeg, 85))
						using (Stream s = entry.Open())
						{
							data.SaveTo(s);
						}
					}
				}
			}
			return path;
		}

		private static (int, int) WideFifth(int page) => page == 4 ? (1600, 900) : (900, 1350);

		private static ReaderOptions Options(PageLayoutMode mode)
		{
			return new ReaderOptions { Mode = mode, Log = m => { }, LoadingDelay = TimeSpan.FromMilliseconds(50) };
		}

		[Fact]
		public async Task The_manga_setting_of_the_book_is_the_reading_direction_and_the_page_types_come_from_its_page_info()
		{
			using (var core = new CoreFixture())
			using (var temp = new TempDir())
			{
				string file = MakeCbz(temp.Combine("manga.cbz"), 6, WideFifth);
				ComicBook book = core.Book(file);
				book.Manga = MangaYesNo.YesAndRightToLeft;
				using (var session = new CoreReadingSession(book, core.Host.ImagePool))
				{
					await session.OpenAsync(CancellationToken.None);
					Assert.True(session.RightToLeft);
					PageTraits cover = session.GetPageTraits(0);
					Assert.True(cover.IsFrontCover);
					Assert.True(cover.IsSingleType);
					Assert.False(session.GetPageTraits(1).IsSingleType);
					Assert.False(session.GetPageTraits(1).HasSize || session.GetPageTraits(1).IsWide);
					book.UpdatePageType(5, ComicPageType.BackCover);
					Assert.True(session.GetPageTraits(5).IsSingleType);
					Assert.False(session.GetPageTraits(5).IsFrontCover);
					book.UpdatePagePosition(2, ComicPagePosition.Near);
					Assert.Equal(ComicPagePosition.Near, session.GetPageTraits(2).Position);
					Assert.Equal(default(PageTraits).Width, session.GetPageTraits(99).Width); // beyond the book: nothing, no exception
				}
				foreach ((MangaYesNo manga, bool? expected) in new (MangaYesNo, bool?)[] { (MangaYesNo.Yes, false), (MangaYesNo.No, false), (MangaYesNo.Unknown, null) })
				{
					ComicBook other = core.Book(file);
					other.Manga = manga;
					using (var session = new CoreReadingSession(other, core.Host.ImagePool))
					{
						await session.OpenAsync(CancellationToken.None);
						Assert.Equal(expected, session.RightToLeft);
					}
				}
			}
		}

		[Fact]
		public async Task A_book_with_a_double_page_pairs_around_it_and_records_the_first_page_and_the_furthest()
		{
			using (var core = new CoreFixture())
			using (var temp = new TempDir())
			{
				string file = MakeCbz(temp.Combine("wide.cbz"), 8, WideFifth);
				ComicBook book = core.Book(file);
				book.Manga = MangaYesNo.YesAndRightToLeft;
				await Ui.Run(async () =>
				{
					var session = new CoreReadingSession(book, core.Host.ImagePool);
					var vm = new ReaderViewModel(session, Options(PageLayoutMode.Double));
					vm.ViewportSize = new Avalonia.Size(1000, 800);
					await vm.OpenAsync();
					await vm.WhenSettled;
					Assert.True(vm.RightToLeft);
					var positions = new System.Collections.Generic.List<string>();
					positions.Add(string.Join(",", vm.Scene.Pages.Select(p => p.Index)));
					for (int i = 0; i < 5; i++)
					{
						vm.NextPage();
						await vm.WhenSettled;
						positions.Add(string.Join(",", vm.Scene.Pages.Select(p => p.Index)));
					}
					// page 5 (index 4) is wide: the page before it is alone, and so is it
					Assert.Equal(new[] { "0", "1,2", "3", "4", "5,6", "7" }, positions.ToArray());
					Assert.Equal(7, book.CurrentPage);
					Assert.Equal(7, book.LastPageRead);
					vm.PreviousPage();
					await vm.WhenSettled;
					Assert.Equal(5, book.CurrentPage);
					Assert.Equal(7, book.LastPageRead); // the furthest page stays
					// what the Core now knows about the sizes
					Assert.True(book.GetPage(4).IsDoublePage);
					Assert.False(book.GetPage(3).IsDoublePage);
					vm.Dispose();
				});
				// reopening starts at the spread that was left, and the pairing is known from the start (the Core remembers the sizes)
				await Ui.Run(async () =>
				{
					var session = new CoreReadingSession(book, core.Host.ImagePool);
					var vm = new ReaderViewModel(session, Options(PageLayoutMode.Double));
					await vm.OpenAsync();
					await vm.WhenSettled;
					Assert.Equal(5, vm.CurrentPage);
					Assert.Equal(new[] { 5, 6 }, vm.Scene.Pages.Select(p => p.Index).ToArray());
					Assert.Equal("6-7 / 8", vm.PageText);
					vm.Dispose();
				});
			}
		}

		[Fact]
		public async Task Resuming_on_the_second_page_of_a_spread_and_the_last_spread_of_a_book_of_odd_length()
		{
			using (var core = new CoreFixture())
			using (var temp = new TempDir())
			{
				string file = MakeCbz(temp.Combine("seven.cbz"), 7, p => (900, 1350));
				ComicBook book = core.Book(file);
				book.CurrentPage = 4; // written by the single page reader: the second page of the spread 3,4
				await Ui.Run(async () =>
				{
					var vm = new ReaderViewModel(new CoreReadingSession(book, core.Host.ImagePool), Options(PageLayoutMode.Double));
					await vm.OpenAsync();
					await vm.WhenSettled;
					Assert.Equal(3, vm.CurrentPage);
					Assert.Equal(new[] { 3, 4 }, vm.Scene.Pages.Select(p => p.Index).ToArray());
					vm.LastPage();
					await vm.WhenSettled;
					Assert.Equal(new[] { 5, 6 }, vm.Scene.Pages.Select(p => p.Index).ToArray());
					Assert.Equal(5, book.CurrentPage);
					Assert.Equal(6, book.LastPageRead);
					Assert.Equal(100, book.ReadPercentage); // the last page was on screen
					vm.Dispose();
				});
			}
		}

		[Fact]
		public async Task Frames_of_a_real_cbz_with_a_double_page_read_right_to_left()
		{
			using (var core = new CoreFixture())
			using (var temp = new TempDir())
			{
				string file = MakeCbz(temp.Combine("frames.cbz"), 8, WideFifth);
				ComicBook book = core.Book(file);
				book.Manga = MangaYesNo.YesAndRightToLeft;
				await Ui.Run(async () =>
				{
					var session = new CoreReadingSession(book, core.Host.ImagePool);
					ReaderOptions options = ReaderHarness.DefaultOptions();
					options.Mode = PageLayoutMode.Double;
					using (ReaderHarness h = await ReaderHarness.Open(session, 900, 700, options))
					{
						h.Capture("m23b-19-core-cbz-cover");
						Assert.Single(h.PageRects());
						h.Press(Avalonia.Input.Key.Left); // right to left: Left is forward
						await h.Settled();
						WriteableBitmap frame = h.Capture("m23b-20-core-cbz-rtl-spread");
						Rect[] r = h.PageRects();
						Assert.Equal(2, r.Length);
						Assert.True(r[0].X > r[1].X);
						Assert.True(PageImages.Near(ReaderHarness.ColorAt(frame, (int)r[0].X + 12, (int)r[0].Y + 12), FakePageDecoder.ColorFor(1), 6), "page 2 on the right");
						Assert.True(PageImages.Near(ReaderHarness.ColorAt(frame, (int)r[1].X + 12, (int)r[1].Y + 12), FakePageDecoder.ColorFor(2), 6), "page 3 on the left");
						h.Press(Avalonia.Input.Key.Left);
						await h.Settled();
						h.Press(Avalonia.Input.Key.Left);
						await h.Settled();
						h.Capture("m23b-21-core-cbz-double-page");
						Assert.Equal(new[] { 4 }, h.Vm.Scene.Pages.Select(p => p.Index).ToArray());
					}
				});
			}
		}
	}
}
