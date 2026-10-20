using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using ComicRack.Mac.Reader;
using ComicRack.Mac.Tests.Reader;
using ComicRack.Mac.Tests.Thumbnails;
using ComicRack.Mac.Thumbnails;
using ComicRack.Mac.ViewModels;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Display;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace ComicRack.Mac.Tests.HiDpi
{
	/// <summary>
	/// The reader at 1x, 1.5x, 2x and 3x: pages land on whole device pixels with sharp edges and no seam in a spread, the fit and the
	/// zoom give the same logical geometry, a drag pans by the same logical distance, and the frame at 2x is the frame at 1x.
	/// </summary>
	[Trait("Category", "HiDpi")]
	public class ReaderHiDpiTests
	{
		private static readonly SKColor Paper = new SKColor(0xF5, 0xF5, 0xF5);
		private readonly ITestOutputHelper output;

		// The pixel blocks of the pages are released when the window closes; the assembly's TestGuard waits for the count to return, so no
		// per test settling of the garbage collector is needed here (it costs a second per test in a full run).
		public ReaderHiDpiTests(ITestOutputHelper output)
		{
			this.output = output;
		}

		/// <summary>An opened reader of solid coloured pages at the given scale.</summary>
		private static async Task<ReaderHarness> Open(double scale, Func<int, (int, int)> size = null, PageLayoutMode mode = PageLayoutMode.Single, int pages = 8,
			double width = 800, double height = 600, int resume = 0, bool numbers = false, bool ornaments = false)
		{
			ReaderOptions options = ReaderHarness.DefaultOptions();
			options.Mode = mode;
			options.RealisticPages = ornaments;
			var decoder = new FakePageDecoder { PageCount = pages, Size = size ?? (p => (800, 1200)), DrawNumbers = numbers };
			var session = new FakeSession(decoder) { ResumePage = resume };
			ReaderHarness h = await ReaderHarness.Open(session, width, height, options);
			Dpi.Apply(h.Window, scale);
			await h.Settled();
			return h;
		}

		/// <summary>Where the layout puts the pages, in device pixels of the window frame.</summary>
		private static RectInt[] DevicePages(ReaderHarness h, double scale)
		{
			return h.PageRects().Select(r => RectInt.Snapped(r, scale)).ToArray();
		}

		/// <summary>The same rectangle to within a unit (the chrome around the reading area has heights that are not whole device pixels at 1.5x).</summary>
		private static void AssertNear(Rect expected, Rect actual)
		{
			Assert.InRange(actual.X, expected.X - 1, expected.X + 1);
			Assert.InRange(actual.Y, expected.Y - 1, expected.Y + 1);
			Assert.InRange(actual.Width, expected.Width - 1, expected.Width + 1);
			Assert.InRange(actual.Height, expected.Height - 1, expected.Height + 1);
		}

		private static bool IsPage(SKColor c, int page) => Raster.Near(c, FakePageDecoder.ColorFor(page), 3);

		private static bool IsPaper(SKColor c) => Raster.Near(c, Paper, 1);

		[Theory]
		[MemberData(nameof(Scales.AllData), MemberType = typeof(Scales))]
		public async Task A_page_fitted_to_the_window_lands_on_whole_pixels_with_sharp_edges(double scale)
		{
			await Ui.Run(async () =>
			{
				using (ReaderHarness h = await Open(scale))
				{
					Raster frame = Dpi.Capture(h.Window, "reader-fit");
					Assert.Equal(Scales.Device(800, scale), frame.Width);
					RectInt expected = DevicePages(h, scale).Single();
					RectInt found = frame.Bounds(c => IsPage(c, 0)).Value;
					output.WriteLine($"scale {scale}: layout says {expected}, frame shows {found}");
					Assert.Equal(expected, found);
					// along the middle row and column there is paper, then page, then paper again: no pixel of any other colour (no blended edge)
					int midY = (found.Top + found.Bottom) / 2, midX = (found.Left + found.Right) / 2;
					Assert.Equal(0, frame.CountInRow(midY, 0, frame.Width, c => !IsPaper(c) && !IsPage(c, 0)));
					RectInt reading = RectInt.Snapped(h.ReadingArea, scale);
					Assert.Equal(0, frame.CountInColumn(midX, reading.Top, reading.Bottom, c => !IsPaper(c) && !IsPage(c, 0)));
					// the page fills its rectangle completely
					Assert.Equal(found.Width, frame.CountInRow(midY, found.Left, found.Right, c => IsPage(c, 0)));
				}
			});
		}

		[Theory]
		[MemberData(nameof(Scales.AllData), MemberType = typeof(Scales))]
		public async Task The_fit_gives_the_same_logical_arrangement_at_every_scale(double scale)
		{
			await Ui.Run(async () =>
			{
				PageArrangement reference;
				using (ReaderHarness one = await Open(1.0))
				{
					reference = one.Vm.Arrangement;
				}
				using (ReaderHarness h = await Open(scale))
				{
					PageArrangement a = h.Vm.Arrangement;
					Assert.Equal(reference.Pages.Length, a.Pages.Length);
					// the layout rounds the heights of the tab strip and the status bar to whole device pixels, so the reading area differs by up to a unit
					AssertNear(reference.Pages[0], a.Pages[0]);
					Assert.InRange(a.Scale, reference.Scale - 0.002, reference.Scale + 0.002);
					AssertNear(reference.Content, a.Content);
					// the same number of page pixels is behind every logical unit: the device scale is the logical one times the display's
					Assert.InRange(DevicePages(h, scale).Single().Width / 800.0, a.Scale * scale - 0.005, a.Scale * scale + 0.005);
				}
			});
		}

		[Theory]
		[MemberData(nameof(Scales.AllData), MemberType = typeof(Scales))]
		public async Task A_spread_has_no_seam_and_no_blended_pixel_between_its_pages(double scale)
		{
			await Ui.Run(async () =>
			{
				// odd widths and heights put the shared edge of the two pages on a fractional device position at most scales
				foreach ((double width, double height) in new[] { (900.0, 700.0), (887.0, 641.0), (777.0, 555.0) })
				{
					using (ReaderHarness h = await Open(scale, mode: PageLayoutMode.Double, resume: 1, width: width, height: height))
					{
						Raster frame = Dpi.Capture(h.Window, "reader-spread");
						RectInt[] pages = DevicePages(h, scale);
						Assert.Equal(2, pages.Length);
						Assert.Equal(pages[0].Right, pages[1].Left); // one shared edge: rounded once
						int y = (pages[0].Top + pages[0].Bottom) / 2;
						int span = pages[1].Right - pages[0].Left;
						Assert.Equal(span, frame.CountInRow(y, pages[0].Left, pages[1].Right, c => IsPage(c, 1) || IsPage(c, 2)));
						Assert.Equal(pages[0].Width, frame.CountInRow(y, pages[0].Left, pages[0].Right, c => IsPage(c, 1)));
						Assert.Equal(pages[1].Width, frame.CountInRow(y, pages[1].Left, pages[1].Right, c => IsPage(c, 2)));
						Assert.Equal(0, frame.CountInRow(y, pages[0].Left - 1, pages[0].Left, c => !IsPaper(c)));
						Assert.Equal(0, frame.CountInRow(y, pages[1].Right, pages[1].Right + 1, c => !IsPaper(c)));
					}
				}
			});
		}

		[Theory]
		[MemberData(nameof(Scales.AllData), MemberType = typeof(Scales))]
		public async Task Zoom_and_pan_are_in_logical_units_and_the_pages_follow_them_in_device_pixels(double scale)
		{
			await Ui.Run(async () =>
			{
				PageArrangement reference;
				using (ReaderHarness one = await Open(1.0))
				{
					one.Vm.ZoomBy(2.5, new Point(300, 200));
					one.Vm.PanBy(new Vector(30, 17));
					reference = one.Vm.Arrangement;
				}
				using (ReaderHarness h = await Open(scale, numbers: true))
				{
					h.Vm.ZoomBy(2.5, new Point(300, 200));
					h.Vm.PanBy(new Vector(30, 17));
					h.Tick();
					PageArrangement a = h.Vm.Arrangement;
					AssertNear(reference.Pages[0], a.Pages[0]);
					Assert.Equal(reference.ScrollX, a.ScrollX, 9);
					Assert.Equal(reference.Scroll, a.Scroll, 9);
					Assert.Equal(2.5, h.Vm.Zoom, 9);
					// the frame shows the visible part of the page where the layout says it is
					Raster frame = Dpi.Capture(h.Window, "reader-zoom");
					RectInt area = RectInt.Snapped(h.ReadingArea, scale);
					RectInt visible = RectInt.Snapped(h.PageRects().Single().Intersect(h.ReadingArea), scale);
					RectInt found = frame.Bounds(c => IsPage(c, 0), area).Value;
					output.WriteLine($"scale {scale}: visible {visible}, found {found}");
					// the number and the frame drawn on the page change some of its pixels; the extent is that of the layout, to a pixel
					Assert.InRange(found.Left - visible.Left, 0, 3);
					Assert.InRange(found.Top - visible.Top, 0, 3);
					Assert.InRange(visible.Right - found.Right, 0, 3);
					Assert.InRange(visible.Bottom - found.Bottom, 0, 3);
					// panning by 40 logical units moves the picture by 40 x scale device pixels
					Raster before = Dpi.Capture(h.Window, "reader-zoom-before");
					h.Vm.PanBy(new Vector(40, 0));
					h.Tick();
					Raster after = Dpi.Capture(h.Window, "reader-zoom-after");
					int shift = Scales.Device(40, scale);
					RectInt inner = area.Inflate(-60);
					Raster expectedPart = before.Crop(new RectInt(inner.Left + shift, inner.Top, inner.Right + shift, inner.Bottom));
					Raster movedPart = after.Crop(inner);
					Difference exact = movedPart.Compare(expectedPart, 32);
					Difference unmoved = movedPart.Compare(before.Crop(inner), 32);
					output.WriteLine($"scale {scale}: against the shifted frame {exact}; against the unshifted frame {unmoved}");
					Assert.True(exact.Mean < 1.0 && exact.Bad < 0.01, exact.ToString());
					Assert.True(unmoved.Mean > exact.Mean * 3, "the pictures differ, so the comparison is meaningful: " + unmoved);
				}
			});
		}

		[Theory]
		[MemberData(nameof(Scales.AllData), MemberType = typeof(Scales))]
		public async Task A_zoom_about_a_point_keeps_the_picture_under_that_point(double scale)
		{
			await Ui.Run(async () =>
			{
				using (ReaderHarness h = await Open(scale, size: p => (1000, 1000)))
				{
					// a pinch at a logical point of the reading area: the content point under it stays under it (a logical fact, whatever the scale)
					var focal = new Point(250, 170);
					Rect box = h.Vm.Arrangement.Content;
					double ux = (focal.X - box.X) / box.Width, uy = (focal.Y - box.Y) / box.Height;
					h.Vm.ZoomBy(3.0, focal);
					Rect zoomed = h.Vm.Arrangement.Content;
					Assert.Equal(focal.X, zoomed.X + ux * zoomed.Width, 6);
					Assert.Equal(focal.Y, zoomed.Y + uy * zoomed.Height, 6);
					h.Tick();
					Raster frame = Dpi.Capture(h.Window, "reader-pinch");
					Point origin = h.ReadingArea.TopLeft;
					Assert.True(IsPage(frame[Scales.Device(origin.X + focal.X, scale), Scales.Device(origin.Y + focal.Y, scale)], 0), "the picture is under the pointer");
				}
			});
		}

		[Theory]
		[MemberData(nameof(Scales.AllData), MemberType = typeof(Scales))]
		public async Task A_drag_pans_by_the_logical_distance_of_the_pointer(double scale)
		{
			await Ui.Run(async () =>
			{
				using (ReaderHarness h = await Open(scale))
				{
					h.Vm.ZoomBy(3.0);
					h.Tick();
					Assert.True(h.Vm.CanPan);
					double x0 = h.Vm.ScrollX, y0 = h.Vm.ScrollY;
					Point start = h.ReadingArea.Center;
					h.Window.MouseMove(start);
					h.Window.MouseDown(start, MouseButton.Left);
					h.Window.MouseMove(start + new Vector(-40, -25)); // the content follows the pointer
					h.Window.MouseUp(start + new Vector(-40, -25), MouseButton.Left);
					h.Tick();
					Assert.Equal(x0 + 40, h.Vm.ScrollX, 6);
					Assert.Equal(y0 + 25, h.Vm.ScrollY, 6);
				}
			});
		}

		[Theory]
		[MemberData(nameof(Scales.AllData), MemberType = typeof(Scales))]
		public async Task The_page_label_text_has_the_same_logical_box_and_a_device_size_that_follows_the_scale(double scale)
		{
			await Ui.Run(async () =>
			{
				Avalonia.Rect logicalOne;
				int inkOne;
				using (ReaderHarness one = await Open(1.0))
				{
					(logicalOne, inkOne) = MeasureLabel(one, 1.0);
				}
				using (ReaderHarness h = await Open(scale))
				{
					(Avalonia.Rect logical, int ink) = MeasureLabel(h, scale);
					output.WriteLine($"scale {scale}: label {logical}, ink width {ink} device pixels (1x: {inkOne})");
					Assert.Equal(logicalOne.Width, logical.Width, 0);
					Assert.Equal(logicalOne.Height, logical.Height, 0);
					Assert.InRange((double)ink / inkOne, scale * 0.9, scale * 1.1);
				}
			});
		}

		private static (Avalonia.Rect box, int ink) MeasureLabel(ReaderHarness h, double scale)
		{
			var label = h.Window.FindControl<TextBlock>("BookSegment");
			Avalonia.Rect box = Dpi.Area(label, h.Window);
			Raster frame = Dpi.Capture(h.Window, "reader-label");
			RectInt region = RectInt.Snapped(box, scale);
			SKColor ground = frame[region.Left - 2, region.Top + 1];
			RectInt? ink = frame.Bounds(c => !Raster.Near(c, ground, 40), region);
			Assert.NotNull(ink);
			return (box, ink.Value.Width);
		}

		[Theory]
		[MemberData(nameof(Scales.MainData), MemberType = typeof(Scales))]
		public async Task The_frame_at_each_scale_is_the_frame_at_1x_in_more_pixels(double scale)
		{
			await Ui.Run(async () =>
			{
				Raster reference;
				using (ReaderHarness one = await Open(1.0, size: p => (1600, 2400), numbers: true))
				{
					reference = Dpi.Capture(one.Window, "reader-frame");
				}
				using (ReaderHarness h = await Open(scale, size: p => (1600, 2400), numbers: true))
				{
					Raster frame = Dpi.Capture(h.Window, "reader-frame");
					Raster page = frame.Crop(RectInt.Snapped(h.PageRects().Single(), scale));
					Raster reduced = page.Downscale(reference.Crop(RectInt.Snapped(h.PageRects().Single(), 1.0)).Width, reference.Crop(RectInt.Snapped(h.PageRects().Single(), 1.0)).Height);
					Raster expected = reference.Crop(RectInt.Snapped(h.PageRects().Single(), 1.0));
					Difference d = reduced.Compare(expected, 64);
					output.WriteLine($"scale {scale}: the page reduced to 1x against the 1x page: {d}");
					Assert.True(d.Mean < 2.0, d.ToString());
					Assert.True(d.Bad < 0.02, d.ToString());
				}
			});
		}

		[Theory]
		[MemberData(nameof(Scales.AllData), MemberType = typeof(Scales))]
		public async Task A_page_shown_at_exactly_one_pixel_per_device_pixel_is_a_copy(double scale)
		{
			await Ui.Run(async () =>
			{
				// a page 400 points wide at `scale` has 400 x scale pixels: fitted to the width of a 400 point window it needs no resampling
				int w = Scales.Device(400, scale), tall = Scales.Device(1200, scale);
				using (ReaderHarness h = await Open(scale, size: p => (w, tall), width: 400, height: 600, numbers: true))
				{
					h.Vm.FitWidth();
					await h.Settled();
					Rect page = h.PageRects().Single();
					RectInt area = RectInt.Snapped(h.ReadingArea, scale);
					Assert.Equal(w, RectInt.Snapped(page, scale).Width);
					Raster frame = Dpi.Capture(h.Window, "reader-copy");
					var decoder = ((FakeSession)h.Vm.Session).FakeDecoder;
					// the decoder made one picture per page, in the order the decodes finished: page 1 is the one with its colour
					using (SKImage picture = FirstPage(decoder))
					{
						Raster source = Raster.From(picture);
						RectInt placed = RectInt.Snapped(page, scale);
						int rows = Math.Min(area.Bottom, placed.Bottom) - Math.Max(area.Top, placed.Top);
						Raster shown = frame.Crop(new RectInt(placed.Left, Math.Max(area.Top, placed.Top), placed.Left + w, Math.Max(area.Top, placed.Top) + rows));
						Raster expected = source.Crop(new RectInt(0, Math.Max(area.Top, placed.Top) - placed.Top, w, Math.Max(area.Top, placed.Top) - placed.Top + rows));
						Difference d = shown.Compare(expected, 0);
						output.WriteLine($"scale {scale}: {d}");
						Assert.Equal(0, d.Max);
					}
				}
			});
		}

		private static SKImage FirstPage(FakePageDecoder decoder)
		{
			SKImage found = null;
			foreach (PixelBlock block in decoder.Blocks)
			{
				SKImage image = block.CreateImage();
				if (found == null && IsPage(Raster.From(image)[image.Width / 8, image.Height / 8], 0))
				{
					found = image;
				}
				else
				{
					image.Dispose();
				}
			}
			return found ?? throw new InvalidOperationException("The first page was not decoded.");
		}
	}
}
