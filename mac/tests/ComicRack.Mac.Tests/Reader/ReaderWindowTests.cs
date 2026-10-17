using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ComicRack.Mac.Reader;
using ComicRack.Mac.Services;
using ComicRack.Mac.Tests.Thumbnails;
using ComicRack.Mac.Thumbnails;
using ComicRack.Mac.ViewModels;
using ComicRack.Mac.Views;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Display;
using SkiaSharp;
using Xunit;

namespace ComicRack.Mac.Tests.Reader
{
	/// <summary>A reader window on the headless platform with the Skia renderer: frames are checked pixel by pixel and, with CRMAC_FRAMES_DIR set, saved as PNG.</summary>
	public sealed class ReaderHarness : IDisposable
	{
		public ReaderHarness(ReaderViewModel vm, double width, double height)
		{
			Vm = vm;
			Window = new ReaderWindow(vm, ownsModel: true) { Width = width, Height = height };
			Window.Show();
		}

		public ReaderViewModel Vm { get; }

		public ReaderWindow Window { get; }

		public static ReaderOptions DefaultOptions() => new ReaderOptions { Log = m => { }, LoadingDelay = TimeSpan.FromMilliseconds(50) };

		public static async Task<ReaderHarness> Open(IReadingSession session, double width = 800, double height = 700, ReaderOptions options = null)
		{
			var harness = new ReaderHarness(new ReaderViewModel(session, options ?? DefaultOptions()), width, height);
			await harness.Vm.OpenAsync();
			await harness.Vm.WhenSettled;
			harness.Tick();
			return harness;
		}

		/// <summary>Runs queued jobs, a layout pass and a render tick, so that what the view model changed is drawn.</summary>
		public void Tick()
		{
			for (int i = 0; i < 3; i++)
			{
				Dispatcher.UIThread.RunJobs();
				Window.UpdateLayout();
				AvaloniaHeadlessPlatform.ForceRenderTimerTick();
			}
		}

		public WriteableBitmap Capture(string name)
		{
			Tick();
			WriteableBitmap frame = Window.CaptureRenderedFrame();
			Assert.NotNull(frame);
			string dir = Environment.GetEnvironmentVariable("CRMAC_FRAMES_DIR");
			if (!string.IsNullOrEmpty(dir))
			{
				Directory.CreateDirectory(dir);
				frame.Save(Path.Combine(dir, name + ".png"));
			}
			return frame;
		}

		/// <summary>The reading area in window coordinates.</summary>
		public Rect ReadingArea
		{
			get
			{
				ReaderView view = Window.ReadingArea;
				Point origin = view.TranslatePoint(new Point(0, 0), Window) ?? new Point();
				return new Rect(origin, view.Bounds.Size);
			}
		}

		/// <summary>Where the pages are, in window coordinates, according to the layout.</summary>
		public Rect[] PageRects()
		{
			Rect area = ReadingArea;
			return Vm.Arrangement.Pages.Select(r => new Rect(r.X + area.X, r.Y + area.Y, r.Width, r.Height)).ToArray();
		}

		public static SKColor ColorAt(WriteableBitmap frame, int x, int y)
		{
			bool rgba = frame.Format == Avalonia.Platform.PixelFormat.Rgba8888;
			using (var buffer = frame.Lock())
			{
				unsafe
				{
					uint p = ((uint*)((byte*)buffer.Address + y * buffer.RowBytes))[x];
					return rgba
						? new SKColor((byte)p, (byte)(p >> 8), (byte)(p >> 16), 255)
						: new SKColor((byte)(p >> 16), (byte)(p >> 8), (byte)p, 255);
				}
			}
		}

		public static int DistinctColors(WriteableBitmap frame, Rect region)
		{
			using (var buffer = frame.Lock())
			{
				var seen = new HashSet<uint>();
				unsafe
				{
					for (int y = (int)region.Top; y < (int)region.Bottom; y += 2)
					{
						uint* row = (uint*)((byte*)buffer.Address + y * buffer.RowBytes);
						for (int x = (int)region.Left; x < (int)region.Right; x += 2)
						{
							seen.Add(row[x]);
						}
					}
				}
				return seen.Count;
			}
		}

		public void Press(Key key, KeyModifiers modifiers = KeyModifiers.None)
		{
			Window.KeyPress(key, ToRaw(modifiers), PhysicalKeyFor(key), null);
			Tick();
		}

		private static RawInputModifiers ToRaw(KeyModifiers m)
		{
			RawInputModifiers raw = RawInputModifiers.None;
			if ((m & KeyModifiers.Shift) != 0) raw |= RawInputModifiers.Shift;
			if ((m & KeyModifiers.Meta) != 0) raw |= RawInputModifiers.Meta;
			if ((m & KeyModifiers.Control) != 0) raw |= RawInputModifiers.Control;
			if ((m & KeyModifiers.Alt) != 0) raw |= RawInputModifiers.Alt;
			return raw;
		}

		private static PhysicalKey PhysicalKeyFor(Key key)
		{
			switch (key)
			{
				case Key.Right: return PhysicalKey.ArrowRight;
				case Key.Left: return PhysicalKey.ArrowLeft;
				case Key.Up: return PhysicalKey.ArrowUp;
				case Key.Down: return PhysicalKey.ArrowDown;
				case Key.Home: return PhysicalKey.Home;
				case Key.End: return PhysicalKey.End;
				case Key.PageUp: return PhysicalKey.PageUp;
				case Key.PageDown: return PhysicalKey.PageDown;
				case Key.Space: return PhysicalKey.Space;
				case Key.Escape: return PhysicalKey.Escape;
				case Key.D2: return PhysicalKey.Digit2;
				case Key.D3: return PhysicalKey.Digit3;
				case Key.D5: return PhysicalKey.Digit5;
				case Key.W: return PhysicalKey.W;
				case Key.F: return PhysicalKey.F;
				case Key.R: return PhysicalKey.R;
				case Key.D0: return PhysicalKey.Digit0;
				case Key.D1: return PhysicalKey.Digit1;
				case Key.OemPlus: return PhysicalKey.Equal;
				case Key.OemMinus: return PhysicalKey.Minus;
				default: return PhysicalKey.None;
			}
		}

		public async Task Settled()
		{
			await Vm.WhenSettled;
			Tick();
		}

		public void Dispose()
		{
			Window.Close();
			Tick();
		}
	}

	public class ReaderWindowTests : IDisposable
	{
		private static readonly SKColor Paper = new SKColor(0x1E, 0x1E, 0x1E);
		private readonly long liveBefore;

		public ReaderWindowTests()
		{
			Gc.Flush();
			liveBefore = PixelBlock.LiveCount;
		}

		public void Dispose()
		{
			Gc.Flush();
			Assert.Equal(liveBefore, PixelBlock.LiveCount);
		}

		[Fact]
		public async Task The_page_is_drawn_in_the_middle_of_the_reading_area_in_its_own_colour_with_paper_around_it()
		{
			await Ui.Run(async () =>
			{
				var session = new FakeSession(new FakePageDecoder { PageCount = 5, Size = p => (200, 300) }) { ResumePage = 2 };
				using (ReaderHarness h = await ReaderHarness.Open(session, 800, 700))
				{
					WriteableBitmap frame = h.Capture("reader-1-fake-page");
					Rect page = h.PageRects()[0];
					Assert.InRange(page.Height, 600, 700);
					Assert.Equal(200.0 / 300, page.Width / page.Height, 2);
					SKColor expected = FakePageDecoder.ColorFor(2);
					Assert.True(PageImages.Near(ReaderHarness.ColorAt(frame, (int)(page.X + page.Width / 2), (int)(page.Y + page.Height / 2)), expected), "centre");
					Assert.True(PageImages.Near(ReaderHarness.ColorAt(frame, (int)page.X + 3, (int)page.Y + 3), expected), "top left corner of the page");
					Assert.True(PageImages.Near(ReaderHarness.ColorAt(frame, (int)page.Right - 4, (int)page.Bottom - 4), expected), "bottom right corner of the page");
					Assert.True(PageImages.Near(ReaderHarness.ColorAt(frame, (int)(page.X - 10), (int)(page.Y + 100)), Paper, 1), "paper left of the page");
					Assert.True(PageImages.Near(ReaderHarness.ColorAt(frame, (int)(page.Right + 10), (int)(page.Y + 100)), Paper, 1), "paper right of the page");
					Assert.Equal("3 / 5", h.Window.FindControl<TextBlock>("PageLabel").Text);
					Assert.Equal("Best Fit", h.Window.FindControl<TextBlock>("FitLabel").Text);
					Assert.Equal("Fake Book", h.Window.Title);
					Assert.False(h.Window.FindControl<Border>("NoticeBox").IsVisible);
				}
			});
		}

		[Fact]
		public async Task The_arrow_keys_space_home_and_end_turn_the_pages_and_the_frame_follows()
		{
			await Ui.Run(async () =>
			{
				var session = new FakeSession(new FakePageDecoder { PageCount = 8, Size = p => (200, 300) });
				using (ReaderHarness h = await ReaderHarness.Open(session))
				{
					Func<SKColor> centre = () =>
					{
						WriteableBitmap frame = h.Capture("reader-keys");
						Rect r = h.PageRects()[0];
						return ReaderHarness.ColorAt(frame, (int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2));
					};
					Assert.True(PageImages.Near(centre(), FakePageDecoder.ColorFor(0)));
					h.Press(Key.Right);
					await h.Settled();
					Assert.True(PageImages.Near(centre(), FakePageDecoder.ColorFor(1)));
					h.Press(Key.Space);
					await h.Settled();
					Assert.True(PageImages.Near(centre(), FakePageDecoder.ColorFor(2)));
					h.Press(Key.Left);
					h.Press(Key.Space, KeyModifiers.Shift);
					await h.Settled();
					Assert.True(PageImages.Near(centre(), FakePageDecoder.ColorFor(0)));
					h.Press(Key.End);
					await h.Settled();
					Assert.True(PageImages.Near(centre(), FakePageDecoder.ColorFor(7)));
					h.Press(Key.Right); // past the end: stays
					await h.Settled();
					Assert.True(PageImages.Near(centre(), FakePageDecoder.ColorFor(7)));
					h.Press(Key.PageUp);
					await h.Settled();
					Assert.True(PageImages.Near(centre(), FakePageDecoder.ColorFor(6)));
					h.Press(Key.Home);
					await h.Settled();
					Assert.True(PageImages.Near(centre(), FakePageDecoder.ColorFor(0)));
					Assert.Equal(0, session.LastReported);
				}
			});
		}

		[Fact]
		public async Task Escape_closes_the_window_records_the_page_and_releases_the_book()
		{
			await Ui.Run(async () =>
			{
				var session = new FakeSession(new FakePageDecoder { PageCount = 6 }) { ResumePage = 1 };
				ReaderHarness h = await ReaderHarness.Open(session);
				bool closed = false;
				h.Window.Closed += (s, e) => closed = true;
				h.Press(Key.Right);
				h.Press(Key.Right);
				Assert.Equal(3, h.Vm.CurrentPage);
				h.Press(Key.Escape);
				Assert.True(closed);
				Assert.True(h.Vm.IsDisposed);
				Assert.True(session.IsDisposed);
				Assert.Equal(3, session.LastReported);
				h.Tick();
			});
		}

		[Fact]
		public async Task The_cmd_keys_choose_the_fit_mode_and_close()
		{
			await Ui.Run(async () =>
			{
				var session = new FakeSession(new FakePageDecoder { PageCount = 3, Size = p => (1200, 1800) });
				ReaderHarness h = await ReaderHarness.Open(session, 900, 700);
				WriteableBitmap best = h.Capture("reader-fit-best");
				Rect bestRect = h.PageRects()[0];
				Assert.InRange(bestRect.Height, 600, 700);

				h.Press(Key.D3, KeyModifiers.Meta);
				Assert.Equal(ImageFitMode.FitWidth, h.Vm.Fit);
				h.Tick();
				Rect widthRect = h.PageRects()[0];
				Assert.Equal(h.ReadingArea.Width, widthRect.Width, 1);
				Assert.True(widthRect.Height > h.ReadingArea.Height);
				h.Capture("reader-fit-width-top");
				Assert.Equal("Fit Width", h.Window.FindControl<TextBlock>("FitLabel").Text);

				h.Press(Key.Space); // scrolls instead of turning the page
				Assert.Equal(0, h.Vm.CurrentPage);
				Assert.True(h.Vm.ScrollY > 100);
				h.Capture("reader-fit-width-scrolled");

				h.Press(Key.D5, KeyModifiers.Meta);
				Assert.Equal(ImageFitMode.FitHeight, h.Vm.Fit);
				Assert.Equal(0, h.Vm.ScrollY);
				h.Capture("reader-fit-height");

				h.Press(Key.D2, KeyModifiers.Meta);
				Assert.Equal(ImageFitMode.Fit, h.Vm.Fit);
				bool closed = false;
				h.Window.Closed += (s, e) => closed = true;
				h.Press(Key.W, KeyModifiers.Meta);
				Assert.True(closed);
				h.Tick();
			});
		}

		[Fact]
		public async Task A_two_finger_swipe_turns_one_page_however_long_its_momentum_tail_is()
		{
			await Ui.Run(async () =>
			{
				var session = new FakeSession(new FakePageDecoder { PageCount = 10, Size = p => (200, 300) }) { ResumePage = 4 };
				using (ReaderHarness h = await ReaderHarness.Open(session))
				{
					Point middle = h.ReadingArea.Center;
					// the fingers move left: 20 events, then 60 events of decaying momentum, 8 ms apart
					for (int i = 0; i < 20; i++)
					{
						h.Window.MouseWheel(middle, new Vector(-0.3, 0));
						Thread.Sleep(8);
					}
					for (int i = 0; i < 60; i++)
					{
						h.Window.MouseWheel(middle, new Vector(-0.25 * Math.Pow(0.93, i), 0));
						Thread.Sleep(8);
					}
					await h.Settled();
					Assert.Equal(5, h.Vm.CurrentPage);

					Thread.Sleep(300); // the user lifts off and swipes back
					for (int i = 0; i < 20; i++)
					{
						h.Window.MouseWheel(middle, new Vector(0.3, 0));
						Thread.Sleep(8);
					}
					await h.Settled();
					Assert.Equal(4, h.Vm.CurrentPage);
				}
			});
		}

		[Fact]
		public async Task A_book_that_fails_to_open_shows_the_reason_over_an_empty_reading_area()
		{
			await Ui.Run(async () =>
			{
				var session = new FakeSession { OpenFailure = new InvalidDataException("The book could not be opened: broken.cbz (the file is missing, damaged or not a comic format).") };
				var vm = new ReaderViewModel(session, ReaderHarness.DefaultOptions());
				using (var h = new ReaderHarness(vm, 700, 500))
				{
					await vm.OpenAsync();
					WriteableBitmap frame = h.Capture("reader-open-failed");
					Assert.True(h.Window.FindControl<Border>("NoticeBox").IsVisible);
					Assert.Contains("broken.cbz", h.Window.FindControl<TextBlock>("NoticeLabel").Text);
					Assert.Empty(vm.Arrangement.Pages);
				}
			});
		}

		[Fact]
		public async Task A_page_that_cannot_be_decoded_shows_the_reason_and_the_next_page_clears_it()
		{
			await Ui.Run(async () =>
			{
				var session = new FakeSession(new FakePageDecoder { PageCount = 5, FailWith = p => p == 2 ? new InvalidDataException("Page 3 could not be read from x.cbz.") : null });
				using (ReaderHarness h = await ReaderHarness.Open(session))
				{
					h.Press(Key.Right);
					await h.Settled();
					h.Press(Key.Right);
					await h.Settled();
					Assert.True(h.Window.FindControl<Border>("NoticeBox").IsVisible);
					Assert.Contains("Page 3 could not be loaded", h.Window.FindControl<TextBlock>("NoticeLabel").Text);
					h.Capture("reader-page-failed");
					h.Press(Key.Right);
					await h.Settled();
					Assert.False(h.Window.FindControl<Border>("NoticeBox").IsVisible);
					Assert.Equal(3, h.Vm.Scene.Position);
				}
			});
		}

		[Fact]
		public async Task Closing_the_window_while_frames_are_in_flight_frees_every_page()
		{
			await Ui.Run(async () =>
			{
				var session = new FakeSession(new FakePageDecoder { PageCount = 20, Size = p => (600, 900) });
				ReaderHarness h = await ReaderHarness.Open(session);
				for (int i = 0; i < 6; i++)
				{
					h.Press(Key.Right);
					h.Tick();
				}
				h.Window.Close();
				h.Tick();
				Assert.True(h.Vm.IsDisposed);
				// the Dispose of this class checks the pixel memory
			});
		}

		// ---- real books

		private static async Task<(CoreFixture core, ReaderHarness h, ComicBook book)> OpenBook(string file, double width = 900, double height = 800)
		{
			var core = new CoreFixture();
			ComicBook book = core.Book(file);
			var session = new CoreReadingSession(book, core.Host.ImagePool);
			ReaderHarness h = await ReaderHarness.Open(session, width, height);
			return (core, h, book);
		}

		private static void ExpectPage(ReaderHarness h, int position)
		{
			Assert.NotNull(h.Vm.Scene);
			Assert.Equal(position, h.Vm.Scene.Position);
			Assert.Null(h.Vm.Notice);
		}

		[FixtureFact("BD-reader")]
		public async Task A_cbz_book_of_the_stand_in_library_reads_in_a_window()
		{
			await Ui.Run(async () =>
			{
				(CoreFixture core, ReaderHarness h, ComicBook book) = await OpenBook(CoreFixture.FirstFixtureBook(Fixtures.BDReader));
				using (core)
				using (h)
				{
					ExpectPage(h, 0);
					WriteableBitmap frame = h.Capture("reader-cbz-page-1");
					Rect page = h.PageRects()[0];
					Assert.True(ReaderHarness.DistinctColors(frame, page) > 20, "the page has content");
					Assert.True(PageImages.Near(ReaderHarness.ColorAt(frame, (int)(page.X - 8), (int)(page.Y + 50)), Paper, 1));
					for (int i = 0; i < 5; i++)
					{
						h.Press(Key.Right);
					}
					await h.Settled();
					ExpectPage(h, 5);
					h.Capture("reader-cbz-page-6");
					Assert.Equal(5, book.CurrentPage);
					h.Press(Key.D3, KeyModifiers.Meta);
					h.Capture("reader-cbz-fit-width");
					h.Press(Key.D5, KeyModifiers.Meta);
					h.Capture("reader-cbz-fit-height");
				}
			});
		}

		[FixtureFact("small")]
		public async Task A_book_of_large_pages_in_the_three_codecs_reads_and_pages_in_a_window()
		{
			await Ui.Run(async () =>
			{
				(CoreFixture core, ReaderHarness h, ComicBook book) = await OpenBook(Path.Combine(Fixtures.Root, "small", "Fixture Series 01 001.cbz"), 1000, 900);
				using (core)
				using (h)
				{
					for (int page = 0; page < 6; page++) // JPEG, PNG, WebP, JPEG, PNG, WebP
					{
						await h.Settled();
						ExpectPage(h, page);
						WriteableBitmap frame = h.Capture("reader-large-page-" + (page + 1));
						Assert.True(ReaderHarness.DistinctColors(frame, h.PageRects()[0]) > 20);
						h.Press(Key.Right);
					}
				}
			});
		}

		[FixtureFact("pdf/test.pdf")]
		public async Task A_pdf_reads_in_a_window()
		{
			await Ui.Run(async () =>
			{
				(CoreFixture core, ReaderHarness h, ComicBook book) = await OpenBook(Path.Combine(Fixtures.Root, "pdf", "test.pdf"));
				using (core)
				using (h)
				{
					ExpectPage(h, 0);
					Assert.Equal("1 / 3", h.Vm.PageText);
					h.Capture("reader-pdf-page-1");
					h.Press(Key.Right);
					await h.Settled();
					ExpectPage(h, 1);
					WriteableBitmap frame = h.Capture("reader-pdf-page-2");
					Assert.True(ReaderHarness.DistinctColors(frame, h.PageRects()[0]) > 10);
				}
			});
		}

		[FixtureFact("cb7/test.cb7")]
		public async Task A_cb7_reads_in_a_window()
		{
			await Ui.Run(async () =>
			{
				(CoreFixture core, ReaderHarness h, ComicBook book) = await OpenBook(Path.Combine(Fixtures.Root, "cb7", "test.cb7"));
				using (core)
				using (h)
				{
					ExpectPage(h, 0);
					Assert.Equal("1 / 24", h.Vm.PageText);
					h.Capture("reader-cb7-page-1");
					h.Press(Key.End);
					await h.Settled();
					ExpectPage(h, 23);
					WriteableBitmap frame = h.Capture("reader-cb7-page-24");
					Assert.True(ReaderHarness.DistinctColors(frame, h.PageRects()[0]) > 20);
				}
			});
		}
	}
}
