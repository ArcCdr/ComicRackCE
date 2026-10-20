using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.VisualTree;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Tests.Browser;
using ComicRack.Mac.Tests.Thumbnails;
using Avalonia.Threading;
using cYo.Projects.ComicRack.Engine;
using ComicRack.Mac.Thumbnails;
using ComicRack.Mac.ViewModels;
using ComicRack.Mac.Views;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace ComicRack.Mac.Tests.HiDpi
{
	/// <summary>
	/// The browser grid at 1x, 1.5x, 2x and 3x: the same logical layout, covers drawn from device pixels, text and borders in device
	/// pixels, hit testing in logical units, and the thumbnail size that is asked for.
	/// </summary>
	[Trait("Category", "HiDpi")]
	public class GridHiDpiTests
	{
		private readonly ITestOutputHelper output;

		public GridHiDpiTests(ITestOutputHelper output)
		{
			this.output = output;
		}

		// 900 points: four columns whatever the scrollbar takes (at 760 the grid reports four columns while the repeater lays out three, see section 9)
		private const double Width = 900, Height = 520;

		private static BookItem[] Items(int count)
		{
			// no Core book: the cells draw no thumbnail by themselves, the tests put their own pictures into them
			return SyntheticItems.Make(count);
		}

		private static async Task<GridHarness> Open(double scale, int count = 40, double width = Width, double height = Height)
		{
			var h = new GridHarness(width, height);
			await h.Show(new BookCatalog(Items(count)));
			Dpi.Apply(h.Window, scale);
			h.Layout();
			return h;
		}

		/// <summary>Everything about the layout that must not depend on the scale: counts and texts exactly, rectangles to a device pixel.</summary>
		private sealed class LayoutSignature
		{
			public readonly List<string> Facts = new List<string>();
			public readonly List<double> Numbers = new List<double>();

			public void Add(Rect r)
			{
				Numbers.AddRange(new[] { r.X, r.Y, r.Width, r.Height });
			}

			public void AssertEqualTo(LayoutSignature reference, double scale)
			{
				Assert.Equal(reference.Facts, Facts);
				Assert.Equal(reference.Numbers.Count, Numbers.Count);
				// the layout rounds to whole device pixels, so a length can differ from the 1x value by up to one device pixel
				double tolerance = 1.0 / scale + 1e-6;
				for (int i = 0; i < Numbers.Count; i++)
				{
					Assert.True(Math.Abs(Numbers[i] - reference.Numbers[i]) <= tolerance, $"number {i}: {Numbers[i]} at {scale}x, {reference.Numbers[i]} at 1x");
				}
			}
		}

		private static LayoutSignature LogicalLayout(GridHarness h)
		{
			var sig = new LayoutSignature();
			sig.Facts.Add($"columns {h.Grid.Columns} rows {h.Grid.RowCount} cell {h.Grid.CellWidth} x {h.Grid.CellHeight} realized {h.Grid.RealizedCount}");
			sig.Add(new Rect(h.Grid.Scroller.Viewport));
			sig.Add(new Rect(h.Grid.Scroller.Extent));
			foreach (BookCell cell in h.Grid.BoundCells().OrderBy(c => c.Index).Take(16))
			{
				sig.Facts.Add($"{cell.Index} \"{cell.TitleText}\" \"{cell.SubtitleText}\"");
				sig.Add(Dpi.Area(cell, h.Window));
				sig.Add(Dpi.Area(cell.Cover, h.Window));
			}
			return sig;
		}

		[Theory]
		[MemberData(nameof(Scales.AllData), MemberType = typeof(Scales))]
		public async Task The_grid_has_the_same_logical_layout_at_every_scale(double scale)
		{
			await Ui.Run(async () =>
			{
				LayoutSignature reference;
				using (GridHarness one = await Open(1.0))
				{
					reference = LogicalLayout(one);
				}
				using (GridHarness h = await Open(scale))
				{
					LogicalLayout(h).AssertEqualTo(reference, scale);
					// the cells sit on whole device pixels (layout rounding), so their borders are sharp
					foreach (BookCell cell in h.Grid.BoundCells().Take(8))
					{
						Rect area = Dpi.Area(cell, h.Window);
						Assert.Equal(Math.Round(area.X * scale), area.X * scale, 6);
						Assert.Equal(Math.Round(area.Y * scale), area.Y * scale, 6);
					}
					Raster frame = Dpi.Capture(h.Window, "grid-layout");
					Assert.Equal(Scales.Device(Width, scale), frame.Width);
					Assert.Equal(Scales.Device(Height, scale), frame.Height);
				}
			});
		}

		/// <summary>Puts a picture of exactly the device size of the cover box into the first cell and returns where it was drawn.</summary>
		private static (BookCell cell, SKImage picture, RectInt area) ShowExactCover(GridHarness h, double scale)
		{
			BookCell cell = h.Grid.CellAt(0);
			Rect box = Dpi.Area(cell.Cover, h.Window);
			int deviceHeight = Scales.Device(box.Height, scale);
			int deviceWidth = Scales.Device(box.Height * 2 / 3, scale);
			SKImage picture = Pictures.Lines(deviceWidth, deviceHeight);
			SetCover(cell, picture);
			h.Layout();
			Rect fit = CoverView.Fit(box.Width, box.Height, deviceWidth, deviceHeight);
			var placed = new Rect(box.X + fit.X, box.Y + fit.Y, fit.Width, fit.Height);
			return (cell, picture, RectInt.Snapped(placed, scale));
		}

		/// <summary>
		/// Not at 1.5x: <see cref="CoverDrawOperation"/> does not snap the picture to whole device pixels (the reader's
		/// <see cref="ComicRack.Mac.Reader.ReaderPainter"/> does), and the centred picture lands on a half pixel there. Real displays of
		/// the Mac have 1x or 2x; the cover draw path is reworked by P3.7 (full-port.md section 9, [P3.H]).
		/// </summary>
		[Theory]
		[InlineData(1.0)]
		[InlineData(2.0)]
		[InlineData(3.0)]
		public async Task A_cover_with_the_pixels_of_its_box_is_drawn_sharp_and_not_resampled(double scale)
		{
			await Ui.Run(async () =>
			{
				using (GridHarness h = await Open(scale))
				{
					(BookCell cell, SKImage picture, RectInt area) = ShowExactCover(h, scale);
					Raster frame = Dpi.Capture(h.Window, "grid-cover-exact");
					Raster source = Raster.From(picture);
					output.WriteLine($"scale {scale}: picture {source.Width} x {source.Height}, drawn at {area}");
					Assert.True(Math.Abs(area.Width - source.Width) <= 1 && Math.Abs(area.Height - source.Height) <= 1, "the picture covers its own number of pixels: " + area);
					// the pixels of the frame inside the cover are the pixels of the picture, one to one
					Raster drawn = frame.Crop(new RectInt(area.Left, area.Top, area.Left + source.Width, area.Top + source.Height));
					Difference d = drawn.Compare(source, 24);
					output.WriteLine("difference to the picture: " + d);
					Assert.True(d.Mean < 1.0 && d.Bad < 0.005, "the cover is a copy of its picture: " + d);
				}
			});
		}

		[Theory]
		[InlineData(1.0)]
		[InlineData(2.0)]
		public async Task A_cover_with_twice_the_pixels_is_reduced_without_losing_its_lines(double scale)
		{
			await Ui.Run(async () =>
			{
				using (GridHarness h = await Open(scale))
				{
					BookCell cell = h.Grid.CellAt(0);
					Rect box = Dpi.Area(cell.Cover, h.Window);
					// a picture with 2x the device pixels of the box (what a 2x display shows of a 1x thumbnail step above)
					int w = Scales.Device(box.Height * 2 / 3, scale) * 2, hh = Scales.Device(box.Height, scale) * 2;
					{
						SetCover(cell, Pictures.Lines(w, hh));
						h.Layout();
						Raster frame = Dpi.Capture(h.Window, "grid-cover-reduced");
						Rect fit = CoverView.Fit(box.Width, box.Height, w, hh);
						RectInt area = RectInt.Snapped(new Rect(box.X + fit.X, box.Y + fit.Y, fit.Width, fit.Height), scale);
						Raster inside = frame.Crop(area.Inflate(-3));
						// black lines of the picture: reduced by 2 they are still dark (not averaged away) somewhere in every 4 pixel period
						int dark = 0;
						for (int y = 0; y < inside.Height; y += 7)
						{
							dark += inside.CountInRow(y, 0, inside.Width, c => c.Red < 70 && c.Green < 70 && c.Blue < 70);
						}
						output.WriteLine($"scale {scale}: {dark} dark pixels in the sampled rows of {inside.Width} x {inside.Height}");
						Assert.True(dark > 0, "the 1 pixel lines survive a reduction by 2");
					}
				}
			});
		}

		[Theory]
		[MemberData(nameof(Scales.MainData), MemberType = typeof(Scales))]
		public async Task A_clean_cover_frame_is_the_same_picture_at_every_scale(double scale)
		{
			await Ui.Run(async () =>
			{
				Raster reference;
				using (GridHarness one = await Open(1.0))
				{
					ShowGradientCovers(one, 1.0);
					reference = Dpi.Capture(one.Window, "grid-frame");
				}
				using (GridHarness h = await Open(scale))
				{
					ShowGradientCovers(h, scale);
					Raster frame = Dpi.Capture(h.Window, "grid-frame");
					Raster back = frame.Downscale(reference.Width, reference.Height);
					Difference d = back.Compare(reference, 64);
					output.WriteLine($"scale {scale} reduced to 1x against the 1x frame: {d}");
					// the text is drawn with other pixels at 2x (it is not a scaled picture), the covers and the cells are the same
					Assert.True(d.Mean < 1.0, d.ToString());
					Assert.True(d.Bad < 0.01, d.ToString());
				}
			});
		}

		private static void ShowGradientCovers(GridHarness h, double scale)
		{
			foreach (BookCell cell in h.Grid.BoundCells())
			{
				Rect box = Dpi.Area(cell.Cover, h.Window);
				// a smooth cover of the box proportions with the pixels of the display, so that any scale gives the same picture
				int w = Scales.Device(box.Height * 2 / 3, scale), hh = Scales.Device(box.Height, scale);
				using (var bitmap = new SKBitmap(new SKImageInfo(w, hh, SKColorType.Bgra8888, SKAlphaType.Opaque)))
				using (var canvas = new SKCanvas(bitmap))
				using (var paint = new SKPaint())
				{
					paint.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, hh),
						new[] { FakeColor(cell.Index, 0), FakeColor(cell.Index, 1) }, SKShaderTileMode.Clamp);
					canvas.DrawRect(0, 0, w, hh, paint);
					SetCover(cell, SKImage.FromBitmap(bitmap));
				}
			}
			h.Layout();
		}

		/// <summary>Gives the cell's cover the picture (the cover takes its own reference, as the cell does with a thumbnail).</summary>
		private static void SetCover(BookCell cell, SKImage picture)
		{
			var image = new CoverImage(picture);
			cell.Cover.SetImage(image);
			image.Release();
		}

		private static SKColor FakeColor(int index, int end)
		{
			return new SKColor((byte)(40 + index * 37 % 180), (byte)(60 + index * 91 % 150), (byte)(80 + (index + end * 3) * 53 % 150), 255);
		}

		private static void Click(GridHarness h, Point at)
		{
			h.Window.MouseMove(at);
			h.Window.MouseDown(at, MouseButton.Left);
			h.Window.MouseUp(at, MouseButton.Left);
			h.Layout();
		}

		[Theory]
		[MemberData(nameof(Scales.AllData), MemberType = typeof(Scales))]
		public async Task A_click_at_the_same_logical_point_selects_the_same_book_at_every_scale(double scale)
		{
			await Ui.Run(async () =>
			{
				using (GridHarness h = await Open(scale))
				{
					BookCell target = h.Grid.CellAt(5);
					Rect area = Dpi.Area(target, h.Window);
					var point = new Point(area.X + area.Width / 2, area.Y + area.Height / 3);
					Click(h, point);
					Assert.Equal(5, h.Model.SelectedIndex);
					// the corner just inside the cell and the first pixel outside it (in logical units) decide the same way
					BookCell other = h.Grid.CellAt(7);
					Rect otherArea = Dpi.Area(other, h.Window);
					var inside = new Point(otherArea.X + 10, otherArea.Y + 10);
					Click(h, inside);
					Assert.Equal(7, h.Model.SelectedIndex);
					// the gap between two cells (10 units) belongs to no cell, at any scale
					var gap = new Point(otherArea.Right + 4, otherArea.Y + 30);
					Click(h, gap);
					Assert.Equal(7, h.Model.SelectedIndex);
				}
			});
		}

		[Theory]
		[MemberData(nameof(Scales.AllData), MemberType = typeof(Scales))]
		public async Task The_selection_border_is_two_logical_units_thick_in_whole_device_pixels(double scale)
		{
			await Ui.Run(async () =>
			{
				using (GridHarness h = await Open(scale))
				{
					h.Model.SelectedIndex = 0;
					h.Layout();
					BookCell cell = h.Grid.CellAt(0);
					Assert.True(cell.IsSelectedForTest);
					Raster frame = Dpi.Capture(h.Window, "grid-selected");
					Rect area = Dpi.Area(cell, h.Window);
					int y = Scales.Device(area.Y + area.Height / 2, scale);
					int left = Scales.Device(area.X, scale);
					// the pure border colour (0A84FF) along the row through the middle of the cell, at its left edge
					int run = frame.CountInRow(y, left - 2, left + 4 * (int)Math.Ceiling(scale) + 4, c => Raster.Near(c, new SKColor(0x0A, 0x84, 0xFF), 3));
					output.WriteLine($"scale {scale}: border run {run} pixels");
					Assert.Equal(Scales.Device(2, scale), run);
					// and no half covered pixel next to it: the neighbour outside is the window, the neighbour inside the tint
					int top = Scales.Device(area.Y, scale);
					int x = Scales.Device(area.X + area.Width / 2, scale);
					int vertical = frame.CountInColumn(x, top - 2, top + 4 * (int)Math.Ceiling(scale) + 4, c => Raster.Near(c, new SKColor(0x0A, 0x84, 0xFF), 3));
					Assert.Equal(Scales.Device(2, scale), vertical);
				}
			});
		}

		[Theory]
		[MemberData(nameof(Scales.AllData), MemberType = typeof(Scales))]
		public async Task Text_keeps_its_logical_size_and_its_device_size_follows_the_scale(double scale)
		{
			await Ui.Run(async () =>
			{
				Rect logicalOne;
				double inkOne;
				using (GridHarness one = await Open(1.0))
				{
					(logicalOne, inkOne) = MeasureTitle(one, 1.0);
				}
				using (GridHarness h = await Open(scale))
				{
					(Rect logical, double ink) = MeasureTitle(h, scale);
					output.WriteLine($"scale {scale}: title box {logical}, ink width {ink} device pixels (1x: {inkOne})");
					Assert.True(Math.Abs(logicalOne.X - logical.X) <= 1 / scale && Math.Abs(logicalOne.Y - logical.Y) <= 1 / scale && logicalOne.Width == logical.Width && logicalOne.Height == logical.Height, $"{logical} at {scale}x, {logicalOne} at 1x");
					// the same string, 12 units high: the ink is about `scale` times as wide as at 1x (glyph advances are hinted differently)
					Assert.InRange(ink / inkOne, scale * 0.93, scale * 1.07);
				}
			});
		}

		private static (Rect box, double inkWidth) MeasureTitle(GridHarness h, double scale)
		{
			BookCell cell = h.Grid.CellAt(0);
			TextBlock title = cell.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == cell.TitleText);
			Rect box = Dpi.Area(title, h.Window);
			Raster frame = Dpi.Capture(h.Window, "grid-text");
			var region = new RectInt(Scales.Device(box.Left, scale), Scales.Device(box.Top, scale), Scales.Device(box.Right, scale), Scales.Device(box.Bottom, scale));
			SKColor ground = frame[region.Left, region.Top];
			RectInt? ink = frame.Bounds(c => !Raster.Near(c, ground, 40), region);
			Assert.NotNull(ink);
			return (box, ink.Value.Width);
		}

		// ---- The thumbnail size that is asked for

		private static BookItem[] VirtualBooks(int count)
		{
			var items = new BookItem[count];
			for (int i = 0; i < count; i++)
			{
				ComicBook book = Books.Make("hidpi-" + i.ToString("00000"));
				book.Series = "Series " + (i / 12 + 1);
				book.Number = (i % 12 + 1).ToString();
				book.Title = "Album " + (i + 1);
				items[i] = BookItem.From(book);
			}
			return items;
		}

		private static async Task WaitFor(Func<bool> condition, string what, int timeoutMs = 15000)
		{
			DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
			while (!condition())
			{
				if (DateTime.UtcNow > deadline)
				{
					throw new TimeoutException("Timed out waiting for " + what);
				}
				await Task.Delay(5);
				Dispatcher.UIThread.RunJobs();
			}
		}

		[Fact]
		public async Task Moving_the_window_to_a_display_with_another_scale_asks_for_the_covers_again()
		{
			await Ui.Run(async () =>
			{
				var decoder = new FakeDecoder { Size = r => (r.Height * 2 / 3, r.Height) };
				using (var service = new ThumbnailService(new ThumbnailServiceOptions { Decoder = decoder }))
				using (var h = new GridHarness(Width, Height))
				{
					h.Model.Thumbnails = service;
					await h.Show(new BookCatalog(VirtualBooks(60)));
					await WaitFor(() => h.Grid.BoundCells().All(c => c.Cover.HasImage), "the covers at 1x");
					Assert.All(h.Grid.BoundCells(), c => Assert.Equal(216, c.Cover.Image.Height)); // 200 points = 200 pixels: the 216 step

					Dpi.Apply(h.Window, 2.0); // dragged to the Retina display
					h.Layout();
					await WaitFor(() => h.Grid.BoundCells().All(c => c.Cover.HasImage && c.Cover.Image.Height == 512), "the covers at 2x");
					Assert.Equal(400, h.Grid.RequestPixels);

					Dpi.Apply(h.Window, 1.0); // and back
					h.Layout();
					await WaitFor(() => h.Grid.BoundCells().All(c => c.Cover.HasImage && c.Cover.Image.Height == 216), "the covers at 1x again");
				}
			});
		}

		[Theory]
		[InlineData(1.0, 80, 80, 91)]
		[InlineData(1.0, 200, 200, 216)]
		[InlineData(1.0, 360, 360, 384)]
		[InlineData(2.0, 80, 160, 162)]
		[InlineData(2.0, 200, 400, 512)]
		[InlineData(2.0, 256, 512, 512)]
		[InlineData(2.0, 360, 720, 512)]
		[InlineData(3.0, 200, 600, 512)]
		[InlineData(1.5, 360, 540, 512)]
		public async Task The_thumbnail_request_is_in_device_pixels_and_the_cache_step_stops_at_512(double scale, double size, int requested, int step)
		{
			await Ui.Run(async () =>
			{
				using (GridHarness h = await Open(scale, count: 4))
				{
					h.Model.ThumbnailSize = size;
					h.Layout();
					Assert.Equal(requested, h.Grid.RequestPixels);
					Assert.Equal(step, ThumbnailSteps.Snap(h.Grid.RequestPixels));
				}
			});
		}

		/// <summary>
		/// DECISION (P3.H): the 512 px cap stays as it is. The Core stores one thumbnail per book, 512 px high
		/// (<c>ThumbnailImage.MaxHeight</c>), and the cell asks for the cover height in device pixels. A 2x display therefore shows the covers
		/// up to a cell cover of 256 points (3x: 170) at their own resolution; at the largest slider position (360 points = 720 px)
		/// the 512 px cover is enlarged by 1.41 (3x: 2.1). That is the original's behaviour (it never shows a cover from more than its
		/// thumbnail) and a small loss only at the top of the slider; serving a larger cover from the page itself belongs to P3.7.
		/// This test pins the numbers; P3.7 changes the expectation when it removes the cap.
		/// </summary>
		[Theory]
		[InlineData(1.0, 360, 1.0)]
		[InlineData(2.0, 256, 1.0)]
		[InlineData(2.0, 300, 1.17)]
		[InlineData(2.0, 360, 1.41)]
		[InlineData(3.0, 170, 1.0)]
		[InlineData(3.0, 360, 2.11)]
		public void The_largest_cells_enlarge_the_512_px_cover_on_a_dense_display(double scale, double size, double enlargement)
		{
			double wanted = Math.Ceiling(size * scale);
			double served = ThumbnailSteps.Snap((int)wanted);
			double factor = Math.Max(1.0, wanted / served);
			Assert.Equal(enlargement, factor, 2);
			Assert.True(served <= ThumbnailSteps.MaxHeight);
		}
	}
}
